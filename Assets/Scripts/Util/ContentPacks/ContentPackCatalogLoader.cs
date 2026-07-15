using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

public static class ContentPackCatalogLoader {
  const string CorePackId = "Core";
  const string DefaultExternalRootFolderName = "MyProjectContent";

  static readonly Dictionary<string, ContentPackRuntimeManifest> availablePacks = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, AsyncOperationHandle<IResourceLocator>> loadedCatalogHandles = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> loadingPackIds = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> loadedPackIds = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> failedPackIds = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, Dictionary<string, string>> exportedAddressBySourceByPack = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, string> sourceAssetPathByExportedAddress = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, string> localBundlePathByFileName = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> missingCatalogWarnings = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> ignoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase) {
    ".git",
    ".hg",
    ".svn",
    "__pycache__"
  };
  static readonly Func<IResourceLocation, string> localInternalIdTransform = TransformLocalContentPackInternalId;

  static Func<IResourceLocation, string> previousInternalIdTransform;
  static bool internalIdTransformInstalled;
  static bool discovered;
  static string discoveredRoot = "";
  static int readyVersion;

  public static int ReadyVersion => readyVersion;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetRuntimeState() {
    foreach (var pair in loadedCatalogHandles) {
      if (pair.Value.IsValid()) {
        Addressables.Release(pair.Value);
      }
    }

    if (internalIdTransformInstalled && Addressables.InternalIdTransformFunc == localInternalIdTransform) {
      Addressables.InternalIdTransformFunc = previousInternalIdTransform;
    }

    availablePacks.Clear();
    loadedCatalogHandles.Clear();
    loadingPackIds.Clear();
    loadedPackIds.Clear();
    failedPackIds.Clear();
    exportedAddressBySourceByPack.Clear();
    sourceAssetPathByExportedAddress.Clear();
    localBundlePathByFileName.Clear();
    missingCatalogWarnings.Clear();
    previousInternalIdTransform = null;
    internalIdTransformInstalled = false;
    discovered = false;
    discoveredRoot = "";
    readyVersion = 0;
  }

  public static void RequestLoadPacks(IEnumerable<string> packIds, string source = "") {
    if (!SpriteStreamingRuntimeSettings.EnableLocalContentPackCatalogs) {
      return;
    }

    DiscoverAvailablePacks();
    if (packIds == null) return;

    foreach (var packId in packIds) {
      RequestLoadPackWithDependencies(packId, new HashSet<string>(StringComparer.OrdinalIgnoreCase), source);
    }
  }

  public static bool IsPackReady(string packId) {
    var normalizedPackId = NormalizePackId(packId);
    if (string.IsNullOrWhiteSpace(normalizedPackId)) return false;
    if (string.Equals(normalizedPackId, CorePackId, StringComparison.OrdinalIgnoreCase)) return true;
    if (!SpriteStreamingRuntimeSettings.EnableLocalContentPackCatalogs) return true;
    if (loadedPackIds.Contains(normalizedPackId)) return true;

    DiscoverAvailablePacks();
    if (!availablePacks.TryGetValue(normalizedPackId, out var manifest)) {
      return false;
    }

    return !ManifestRequiresRuntimeCatalog(manifest);
  }

  public static bool IsPackAvailable(string packId) {
    var normalizedPackId = NormalizePackId(packId);
    if (string.IsNullOrWhiteSpace(normalizedPackId)) return false;
    if (string.Equals(normalizedPackId, CorePackId, StringComparison.OrdinalIgnoreCase)) return true;
    DiscoverAvailablePacks();
    return availablePacks.ContainsKey(normalizedPackId);
  }

  public static IReadOnlyList<string> GetAvailablePackIds() {
    DiscoverAvailablePacks();
    var result = new List<string>(availablePacks.Keys);
    result.Sort(StringComparer.OrdinalIgnoreCase);
    return result;
  }

  public static IReadOnlyList<string> GetLoadedPackIds() {
    var result = new List<string>(loadedPackIds);
    result.Sort(StringComparer.OrdinalIgnoreCase);
    return result;
  }

  public static bool TryResolveExportedAddress(
    string sourceAssetPath,
    IReadOnlyList<string> packIds,
    out string address
  ) {
    address = "";
    var normalizedSourcePath = NormalizeAssetPath(sourceAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedSourcePath) || packIds == null) {
      return false;
    }

    DiscoverAvailablePacks();
    for (var i = 0; i < packIds.Count; i++) {
      var packId = NormalizePackId(packIds[i]);
      if (string.IsNullOrWhiteSpace(packId)) continue;
      if (!IsPackReady(packId)) continue;
      if (!exportedAddressBySourceByPack.TryGetValue(packId, out var addressesBySource)) continue;
      if (!addressesBySource.TryGetValue(normalizedSourcePath, out var resolvedAddress)) continue;
      if (string.IsNullOrWhiteSpace(resolvedAddress)) continue;

      address = resolvedAddress;
      return true;
    }

    return false;
  }

  public static bool TryResolveSourceAssetPath(string exportedAssetPath, out string sourceAssetPath) {
    sourceAssetPath = "";
    var normalizedExportedPath = NormalizeAssetPath(exportedAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedExportedPath)) {
      return false;
    }

    DiscoverAvailablePacks();
    return sourceAssetPathByExportedAddress.TryGetValue(normalizedExportedPath, out sourceAssetPath) &&
           !string.IsNullOrWhiteSpace(sourceAssetPath);
  }

  static void RequestLoadPackWithDependencies(string packId, HashSet<string> stack, string source) {
    var normalizedPackId = NormalizePackId(packId);
    if (string.IsNullOrWhiteSpace(normalizedPackId)) return;
    if (string.Equals(normalizedPackId, CorePackId, StringComparison.OrdinalIgnoreCase)) return;
    if (loadedPackIds.Contains(normalizedPackId)) return;
    if (loadingPackIds.Contains(normalizedPackId)) return;
    if (!stack.Add(normalizedPackId)) return;

    if (!availablePacks.TryGetValue(normalizedPackId, out var manifest) || manifest == null) {
      return;
    }

    if (manifest.dependencies != null) {
      for (var i = 0; i < manifest.dependencies.Count; i++) {
        RequestLoadPackWithDependencies(manifest.dependencies[i], stack, source);
      }
    }

    if (!ManifestRequiresRuntimeCatalog(manifest)) {
      CompleteManifestOnlyPackLoad(normalizedPackId);
      stack.Remove(normalizedPackId);
      return;
    }

    StartCatalogLoad(normalizedPackId, manifest, source);
    stack.Remove(normalizedPackId);
  }

  static void StartCatalogLoad(string packId, ContentPackRuntimeManifest manifest, string source) {
    if (manifest == null) return;

    if (!ManifestRequiresRuntimeCatalog(manifest)) {
      CompleteManifestOnlyPackLoad(packId);
      return;
    }

    var catalogPath = ResolveManifestRelativePath(manifest, manifest.catalogPath);
    if (string.IsNullOrWhiteSpace(catalogPath) || !File.Exists(catalogPath)) {
      if (missingCatalogWarnings.Add(packId)) {
        Debug.LogWarning(
          "[ContentPackCatalogLoader] Missing content pack catalog." +
          " pack_id='" + packId + "'" +
          " catalog='" + (catalogPath ?? "") + "'" +
          " source='" + (source ?? "") + "'"
        );
      }
      if (failedPackIds.Add(packId)) {
        readyVersion++;
      }
      return;
    }

    AddBundleRootMap(manifest);
    EnsureInternalIdTransform();
    loadingPackIds.Add(packId);
    var normalizedCatalogPath = catalogPath.Replace('\\', '/');
    var handle = Addressables.LoadContentCatalogAsync(normalizedCatalogPath, false);
    handle.Completed += operation => CompleteCatalogLoad(packId, operation, source);
  }

  static void CompleteManifestOnlyPackLoad(string packId) {
    failedPackIds.Remove(packId);
    loadingPackIds.Remove(packId);

    if (loadedPackIds.Add(packId)) {
      readyVersion++;
    }
  }

  static void CompleteCatalogLoad(
    string packId,
    AsyncOperationHandle<IResourceLocator> operation,
    string source
  ) {
    loadingPackIds.Remove(packId);

    if (operation.Status == AsyncOperationStatus.Succeeded && operation.Result != null) {
      if (loadedPackIds.Add(packId)) {
        readyVersion++;
      }
      failedPackIds.Remove(packId);
      loadedCatalogHandles[packId] = operation;
      RuntimeLog.Log(
        "[ContentPackCatalogLoader] Loaded content pack catalog." +
        " pack_id='" + packId + "'" +
        " source='" + (source ?? "") + "'"
      );
      return;
    }

    if (failedPackIds.Add(packId)) {
      readyVersion++;
    }
    var error = operation.OperationException != null ? operation.OperationException.Message : "none";
    Debug.LogWarning(
      "[ContentPackCatalogLoader] Failed to load content pack catalog." +
      " pack_id='" + packId + "'" +
      " status=" + operation.Status +
      " error='" + error + "'"
    );

    if (operation.IsValid()) {
      Addressables.Release(operation);
    }
  }

  static void DiscoverAvailablePacks() {
    var root = ResolveLocalContentRoot();
    if (discovered && string.Equals(root, discoveredRoot, StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    availablePacks.Clear();
    exportedAddressBySourceByPack.Clear();
    sourceAssetPathByExportedAddress.Clear();
    localBundlePathByFileName.Clear();
    discovered = true;
    discoveredRoot = root;

    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) {
      Debug.LogError(
        "[ContentPackCatalogLoader] External content root is missing." +
        " expected_root='" + (root ?? "") + "'" +
        " player_data_path='" + Application.dataPath.Replace('\\', '/') + "'" +
        " action='Rebuild the player so the content deployment postprocessor creates MyProjectContent beside the executable, or copy that folder there manually.'"
      );
      return;
    }

    var manifestPaths = new List<string>();
    CollectVisibleFiles(root, "ContentPackManifest.json", manifestPaths);
    manifestPaths.Sort(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < manifestPaths.Count; i++) {
      TryAddManifest(manifestPaths[i]);
    }
  }

  static void TryAddManifest(string manifestPath) {
    try {
      var json = File.ReadAllText(manifestPath);
      var manifest = JsonUtility.FromJson<ContentPackRuntimeManifest>(json);
      if (manifest == null) return;

      var packId = NormalizePackId(manifest.packId);
      if (string.IsNullOrWhiteSpace(packId)) {
        packId = NormalizePackId(Path.GetFileName(Path.GetDirectoryName(manifestPath)));
      }
      if (string.IsNullOrWhiteSpace(packId)) return;
      if (availablePacks.ContainsKey(packId)) return;

      manifest.packId = packId;
      manifest.manifestPath = manifestPath.Replace('\\', '/');
      manifest.rootPath = (Path.GetDirectoryName(manifestPath) ?? "").Replace('\\', '/');
      availablePacks.Add(packId, manifest);
      AddExportedAddressMap(manifest);
      AddBundleRootMap(manifest);
    }
    catch (Exception ex) {
      Debug.LogWarning(
        "[ContentPackCatalogLoader] Failed to read content pack manifest." +
        " path='" + manifestPath.Replace('\\', '/') + "'" +
        " error='" + ex.Message + "'"
      );
    }
  }

  static void AddExportedAddressMap(ContentPackRuntimeManifest manifest) {
    if (manifest == null || string.IsNullOrWhiteSpace(manifest.packId)) return;
    if (manifest.exportedAddresses == null || manifest.exportedAddresses.Count <= 0) return;

    if (!exportedAddressBySourceByPack.TryGetValue(manifest.packId, out var addressesBySource)) {
      addressesBySource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      exportedAddressBySourceByPack[manifest.packId] = addressesBySource;
    }

    for (var i = 0; i < manifest.exportedAddresses.Count; i++) {
      var entry = manifest.exportedAddresses[i];
      if (entry == null) continue;

      var source = NormalizeAssetPath(entry.sourceAssetPath);
      var address = NormalizeAssetPath(entry.address);
      if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(address)) continue;
      addressesBySource[source] = address;

      sourceAssetPathByExportedAddress[address] = source;

      var assetPath = NormalizeAssetPath(entry.assetPath);
      if (!string.IsNullOrWhiteSpace(assetPath)) {
        sourceAssetPathByExportedAddress[assetPath] = source;
      }
    }
  }

  static bool ManifestRequiresRuntimeCatalog(ContentPackRuntimeManifest manifest) {
    if (manifest == null) return false;

    if (ManifestHasRuntimeTextureAddress(manifest)) {
      return true;
    }

    var bundleRoot = ResolveManifestRelativePath(manifest, manifest.bundleRoot);
    if (string.IsNullOrWhiteSpace(bundleRoot)) {
      return false;
    }

    return Directory.Exists(bundleRoot);
  }

  static bool ManifestHasRuntimeTextureAddress(ContentPackRuntimeManifest manifest) {
    if (manifest == null || manifest.exportedAddresses == null) return false;

    for (var i = 0; i < manifest.exportedAddresses.Count; i++) {
      var entry = manifest.exportedAddresses[i];
      if (entry == null) continue;

      if (IsRuntimeTexturePath(entry.assetPath)) {
        return true;
      }

      if (IsRuntimeTexturePath(entry.address)) {
        return true;
      }
    }

    return false;
  }

  static bool IsRuntimeTexturePath(string value) {
    var normalized = NormalizeAssetPath(value);
    if (string.IsNullOrWhiteSpace(normalized)) return false;

    var extension = Path.GetExtension(normalized);
    if (string.IsNullOrWhiteSpace(extension)) return false;

    return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
      string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
      string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
  }

  static void AddBundleRootMap(ContentPackRuntimeManifest manifest) {
    if (manifest == null) return;

    var bundleRoot = ResolveManifestRelativePath(manifest, manifest.bundleRoot);
    if (string.IsNullOrWhiteSpace(bundleRoot) || !Directory.Exists(bundleRoot)) {
      return;
    }

    var files = new List<string>();
    CollectVisibleFiles(bundleRoot, "*", files);
    for (var i = 0; i < files.Count; i++) {
      var fileName = Path.GetFileName(files[i]);
      if (string.IsNullOrWhiteSpace(fileName)) continue;

      localBundlePathByFileName[fileName] = Path.GetFullPath(files[i]).Replace('\\', '/');
    }
  }

  static void CollectVisibleFiles(string root, string searchPattern, List<string> result) {
    if (result == null || string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) {
      return;
    }

    try {
      var files = Directory.GetFiles(root, searchPattern, SearchOption.TopDirectoryOnly);
      for (var i = 0; i < files.Length; i++) {
        result.Add(files[i]);
      }

      var directories = Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);
      for (var i = 0; i < directories.Length; i++) {
        if (ShouldIgnoreDirectory(directories[i])) continue;
        CollectVisibleFiles(directories[i], searchPattern, result);
      }
    }
    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
      Debug.LogWarning(
        "[ContentPackCatalogLoader] Skipped unreadable content directory." +
        " path='" + root.Replace('\\', '/') + "'" +
        " error='" + ex.Message + "'"
      );
    }
  }

  static bool ShouldIgnoreDirectory(string path) {
    var name = Path.GetFileName(path);
    if (string.IsNullOrWhiteSpace(name)) return true;
    if (name.StartsWith(".", StringComparison.Ordinal)) return true;
    if (ignoredDirectoryNames.Contains(name)) return true;

    try {
      var attributes = File.GetAttributes(path);
      return (attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0;
    }
    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
      return true;
    }
  }

  static void EnsureInternalIdTransform() {
    if (internalIdTransformInstalled) {
      return;
    }

    previousInternalIdTransform = Addressables.InternalIdTransformFunc;
    if (previousInternalIdTransform == localInternalIdTransform) {
      previousInternalIdTransform = null;
    }

    Addressables.InternalIdTransformFunc = localInternalIdTransform;
    internalIdTransformInstalled = true;
  }

  static string TransformLocalContentPackInternalId(IResourceLocation location) {
    var internalId = location != null ? location.InternalId : "";
    if (previousInternalIdTransform != null) {
      internalId = previousInternalIdTransform(location);
    }

    if (TryResolveLocalBundleInternalId(internalId, out var resolvedInternalId)) {
      return resolvedInternalId;
    }

    return internalId;
  }

  static bool TryResolveLocalBundleInternalId(string internalId, out string resolvedInternalId) {
    resolvedInternalId = "";
    var normalizedInternalId = NormalizeInternalIdFilePath(internalId);
    if (string.IsNullOrWhiteSpace(normalizedInternalId)) {
      return false;
    }

    if (File.Exists(normalizedInternalId)) {
      return false;
    }

    var fileName = Path.GetFileName(normalizedInternalId);
    if (string.IsNullOrWhiteSpace(fileName)) {
      return false;
    }

    if (!localBundlePathByFileName.TryGetValue(fileName, out var localPath)) {
      return false;
    }

    if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath)) {
      return false;
    }

    resolvedInternalId = localPath.Replace('\\', '/');
    return true;
  }

  static string NormalizeInternalIdFilePath(string internalId) {
    if (string.IsNullOrWhiteSpace(internalId)) {
      return "";
    }

    var trimmed = internalId.Trim();
    if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.IsFile) {
      trimmed = uri.LocalPath;
    }

    var queryIndex = trimmed.IndexOfAny(new[] { '?', '#' });
    if (queryIndex >= 0) {
      trimmed = trimmed.Substring(0, queryIndex);
    }

    return trimmed.Replace('\\', '/');
  }

  static string ResolveManifestRelativePath(ContentPackRuntimeManifest manifest, string relativePath) {
    if (manifest == null || string.IsNullOrWhiteSpace(relativePath)) return "";

    var normalizedPath = relativePath.Replace('\\', '/').Trim();
    if (Path.IsPathRooted(normalizedPath)) {
      return Path.GetFullPath(normalizedPath);
    }

    return Path.GetFullPath(Path.Combine(manifest.rootPath, normalizedPath));
  }

  static string ResolveLocalContentRoot() {
    var configuredRoot = SpriteStreamingRuntimeSettings.LocalContentPackRoot;
    if (!string.IsNullOrWhiteSpace(configuredRoot)) {
      return Path.GetFullPath(configuredRoot).Replace('\\', '/').TrimEnd('/');
    }

    if (Application.isEditor) {
      var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
      var parent = string.IsNullOrWhiteSpace(projectRoot)
        ? ""
        : Directory.GetParent(projectRoot)?.FullName;
      if (!string.IsNullOrWhiteSpace(parent)) {
        return Path.GetFullPath(Path.Combine(parent, DefaultExternalRootFolderName)).Replace('\\', '/').TrimEnd('/');
      }
    }

    var playerRoot = Directory.GetParent(Application.dataPath)?.FullName;
    if (string.IsNullOrWhiteSpace(playerRoot)) {
      playerRoot = Application.dataPath;
    }

    return Path.GetFullPath(Path.Combine(playerRoot, DefaultExternalRootFolderName)).Replace('\\', '/').TrimEnd('/');
  }

  static string NormalizeAssetPath(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().Replace('\\', '/');
  }

  static string NormalizePackId(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  [Serializable]
  sealed class ContentPackRuntimeManifest {
    public string packId;
    public string catalogPath;
    public string bundleRoot;
    public string addressPrefix;
    public List<string> dependencies = new();
    public List<ContentPackRuntimeAddress> exportedAddresses = new();
    [NonSerialized] public string manifestPath;
    [NonSerialized] public string rootPath;
  }

  [Serializable]
  sealed class ContentPackRuntimeAddress {
    public string sourceAssetPath;
    public string assetPath;
    public string address;
  }
}
