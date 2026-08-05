#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

public static partial class ContentPackPipeline {
  static int CleanExternalPackDestination(string externalRoot, ExportSyncStats stats) {
    var normalizedExternalRoot = NormalizeFullPath(externalRoot);
    if (string.IsNullOrWhiteSpace(normalizedExternalRoot)) {
      throw new InvalidOperationException("Cannot clean an empty external content root.");
    }

    var projectRoot = NormalizeFullPath(GetProjectRoot());
    if (string.Equals(normalizedExternalRoot, projectRoot, StringComparison.OrdinalIgnoreCase) ||
        projectRoot.StartsWith(normalizedExternalRoot + "/", StringComparison.OrdinalIgnoreCase)) {
      throw new InvalidOperationException(
        "Refusing to clean the project root or one of its parents. external_root='" + normalizedExternalRoot + "'"
      );
    }

    var volumeRoot = NormalizeFullPath(Path.GetPathRoot(normalizedExternalRoot));
    if (string.Equals(normalizedExternalRoot, volumeRoot, StringComparison.OrdinalIgnoreCase)) {
      throw new InvalidOperationException(
        "Refusing to clean a volume root. external_root='" + normalizedExternalRoot + "'"
      );
    }

    var packageManifestPath = Path.Combine(normalizedExternalRoot, "package.json");
    if (!File.Exists(packageManifestPath)) {
      throw new InvalidOperationException(
        "Refusing to clean a destination without package.json. external_root='" + normalizedExternalRoot + "'"
      );
    }

    ExternalPackageManifestJson packageManifest;
    try {
      packageManifest = JsonUtility.FromJson<ExternalPackageManifestJson>(File.ReadAllText(packageManifestPath));
    }
    catch (Exception ex) {
      throw new InvalidOperationException(
        "Refusing to clean a destination with an unreadable package.json. path='" + packageManifestPath + "'",
        ex
      );
    }

    if (packageManifest == null ||
        !string.Equals(packageManifest.name, ContentPackageName, StringComparison.OrdinalIgnoreCase)) {
      throw new InvalidOperationException(
        "Refusing to clean a destination for a different Unity package." +
        " expected='" + ContentPackageName + "'" +
        " actual='" + (packageManifest != null ? packageManifest.name : "") + "'" +
        " external_root='" + normalizedExternalRoot + "'"
      );
    }

    var deletedEntryCount = 0;
    var entries = Directory.GetFileSystemEntries(normalizedExternalRoot);
    for (var i = 0; i < entries.Length; i++) {
      var entryPath = entries[i];
      var entryName = Path.GetFileName(entryPath);
      if (ShouldPreserveCleanDestinationEntry(entryName)) continue;

      var attributes = File.GetAttributes(entryPath);
      if ((attributes & FileAttributes.Directory) != 0) {
        var isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
        Directory.Delete(entryPath, recursive: !isReparsePoint);
      }
      else {
        File.Delete(entryPath);
      }

      deletedEntryCount++;
    }

    if (stats != null) {
      stats.destinationEntriesDeleted += deletedEntryCount;
    }
    return deletedEntryCount;
  }

  static bool ShouldPreserveCleanDestinationEntry(string entryName) {
    if (string.IsNullOrWhiteSpace(entryName)) return true;
    if (entryName.StartsWith(".", StringComparison.Ordinal)) return true;
    return string.Equals(entryName, "package.json", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(entryName, "package.json.meta", StringComparison.OrdinalIgnoreCase);
  }

  static void PreparePackDirectory(string packRootPath, string externalRoot, TransitionPipelineMode mode, ExportSyncStats stats) {
    var normalizedPackRoot = NormalizeFullPath(packRootPath);
    var normalizedExternalRoot = NormalizeFullPath(externalRoot);

    if (!normalizedPackRoot.StartsWith(normalizedExternalRoot + "/", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(normalizedPackRoot, normalizedExternalRoot, StringComparison.OrdinalIgnoreCase)) {
      throw new InvalidOperationException("Pack root escaped external root. pack='" + normalizedPackRoot + "'");
    }

    if (mode == TransitionPipelineMode.Clean &&
        Directory.Exists(normalizedPackRoot) &&
        !IsPackageBackedExternalPackPath(normalizedPackRoot, normalizedExternalRoot)) {
      Directory.Delete(normalizedPackRoot, recursive: true);
      if (stats != null) {
        stats.packDirectoriesRecreated++;
      }
    }

    if (!Directory.Exists(normalizedPackRoot)) {
      Directory.CreateDirectory(normalizedPackRoot);
      if (stats != null) {
        stats.packDirectoriesCreated++;
      }
    }
  }

  static bool IsPackageBackedExternalPackPath(string normalizedPackRoot, string normalizedExternalRoot) {
    if (!StageRootAssetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return false;
    if (string.IsNullOrWhiteSpace(normalizedPackRoot) || string.IsNullOrWhiteSpace(normalizedExternalRoot)) return false;
    return string.Equals(normalizedPackRoot, normalizedExternalRoot, StringComparison.OrdinalIgnoreCase);
  }

  static void EnsureDirectoryAssetPath(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return;
    var fullPath = GetPhysicalPath(assetPath);
    Directory.CreateDirectory(fullPath);
  }

  static void EnsureDirectoryFullPath(string fullPath) {
    if (string.IsNullOrWhiteSpace(fullPath)) return;
    Directory.CreateDirectory(fullPath);
  }

  static void WriteJson<T>(string fullPath, T payload, TransitionPipelineMode mode, ExportSyncStats stats, bool generatedFile) {
    EnsureDirectoryFullPath(Path.GetDirectoryName(fullPath));
    var json = JsonUtility.ToJson(payload, prettyPrint: true);
    if (mode == TransitionPipelineMode.Smart && File.Exists(fullPath)) {
      var existingJson = File.ReadAllText(fullPath);
      if (string.Equals(existingJson, json, StringComparison.Ordinal)) {
        return;
      }
    }

    File.WriteAllText(fullPath, json, new UTF8Encoding(false));
    if (stats != null) {
      if (generatedFile) {
        stats.generatedFilesWritten++;
      }
      else {
        stats.manifestsWritten++;
      }
    }
  }

  static string NormalizeLibraryName(string value) {
    var normalized = NormalizeAssetPath(value);
    if (string.IsNullOrWhiteSpace(normalized)) return "";
    if (normalized.EndsWith(SpriteStreamingConfig.CustomSpriteLibraryExtension, StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(0, normalized.Length - SpriteStreamingConfig.CustomSpriteLibraryExtension.Length);
    }
    else if (normalized.EndsWith(SpriteStreamingConfig.LegacySpriteLibraryExtension, StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(0, normalized.Length - SpriteStreamingConfig.LegacySpriteLibraryExtension.Length);
    }

    var root = NormalizeAssetPath(SpriteStreamingConfig.SourceRootFolder);
    if (string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase)) return "";
    if (normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(root.Length + 1);
    }

    var localRoot = "Assets/Sprites/SpriteLibraries";
    if (string.Equals(normalized, localRoot, StringComparison.OrdinalIgnoreCase)) return "";
    if (normalized.StartsWith(localRoot + "/", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(localRoot.Length + 1);
    }

    return normalized.Trim('/');
  }

  static string ResolveExistingSpriteLibraryAssetPath(string value) {
    var normalized = NormalizeAssetPath(value);
    if (string.IsNullOrWhiteSpace(normalized)) return "";
    var physicalPath = GetPhysicalPath(normalized);
    if (File.Exists(physicalPath) || Directory.Exists(physicalPath)) return normalized;

    var extension = Path.GetExtension(normalized);
    if (string.Equals(
      extension,
      SpriteStreamingConfig.LegacySpriteLibraryExtension,
      StringComparison.OrdinalIgnoreCase
    )) {
      var convertedPath = NormalizeAssetPath(
        Path.ChangeExtension(normalized, SpriteStreamingConfig.CustomSpriteLibraryExtension)
      );
      return File.Exists(GetPhysicalPath(convertedPath)) ? convertedPath : normalized;
    }

    if (string.Equals(
      extension,
      SpriteStreamingConfig.CustomSpriteLibraryExtension,
      StringComparison.OrdinalIgnoreCase
    )) {
      var legacyPath = NormalizeAssetPath(
        Path.ChangeExtension(normalized, SpriteStreamingConfig.LegacySpriteLibraryExtension)
      );
      return File.Exists(GetPhysicalPath(legacyPath)) ? legacyPath : normalized;
    }

    if (!string.IsNullOrWhiteSpace(extension)) return normalized;

    var customPath = normalized + SpriteStreamingConfig.CustomSpriteLibraryExtension;
    if (File.Exists(GetPhysicalPath(customPath))) return customPath;

    var fallbackPath = normalized + SpriteStreamingConfig.LegacySpriteLibraryExtension;
    return File.Exists(GetPhysicalPath(fallbackPath)) ? fallbackPath : normalized;
  }

  static void ResolveExistingSpriteLibraryAssetPathsInPlace(List<string> paths) {
    if (paths == null || paths.Count <= 0) return;

    var resolvedPaths = new List<string>(paths.Count);
    for (var index = 0; index < paths.Count; index++) {
      AddUniquePath(resolvedPaths, ResolveExistingSpriteLibraryAssetPath(paths[index]));
    }

    paths.Clear();
    paths.AddRange(resolvedPaths);
  }

  static void AddUniquePath(List<string> paths, string value) {
    TryAddUniquePath(paths, value);
  }

  static bool TryAddUniquePath(List<string> paths, string value) {
    if (paths == null) return false;
    var normalized = NormalizeAssetPath(value);
    if (string.IsNullOrWhiteSpace(normalized)) return false;

    for (var i = 0; i < paths.Count; i++) {
      if (string.Equals(paths[i], normalized, StringComparison.OrdinalIgnoreCase)) return false;
    }

    paths.Add(normalized);
    return true;
  }

  static string RemoveExtension(string assetPath) {
    var normalized = NormalizeAssetPath(assetPath);
    return string.IsNullOrWhiteSpace(normalized) ? "" : Path.ChangeExtension(normalized, null)?.Replace('\\', '/') ?? "";
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  static string BuildRuntimeCatalogBundleRootRelativePath() {
    return NormalizeAssetPath(RuntimeCatalogFolderName + "/" + EditorUserBuildSettings.activeBuildTarget);
  }

  static string BuildRuntimeCatalogRelativePath() {
    return NormalizeAssetPath(BuildRuntimeCatalogBundleRootRelativePath() + "/" + RuntimeCatalogFileName);
  }

  public static bool IsGameplayContentRequested(IReadOnlyList<string> activePackIds) {
    if (activePackIds == null || activePackIds.Count <= 0) return false;

    for (var i = 0; i < activePackIds.Count; i++) {
      var packId = NormalizeToken(activePackIds[i]);
      if (string.IsNullOrWhiteSpace(packId)) continue;
      if (string.Equals(packId, CorePackId, StringComparison.OrdinalIgnoreCase)) return true;
      if (IsUiOnlyPackId(packId)) continue;
      if (IsGameplayPackId(packId)) return true;
    }

    return false;
  }

  static bool IsGameplayContentRequested(
    Dictionary<string, PackDefinition> packById,
    IReadOnlyList<string> activePackIds
  ) {
    if (activePackIds == null || activePackIds.Count <= 0) return false;

    for (var i = 0; i < activePackIds.Count; i++) {
      var packId = NormalizeToken(activePackIds[i]);
      if (string.IsNullOrWhiteSpace(packId)) continue;
      if (string.Equals(packId, CorePackId, StringComparison.OrdinalIgnoreCase)) return true;

      if (packById != null &&
          packById.TryGetValue(packId, out var pack) &&
          pack != null) {
        if (IsGameplayPack(pack)) return true;
        continue;
      }

      if (IsGameplayPackId(packId)) return true;
    }

    return false;
  }

  static bool IsGameplayPack(PackDefinition pack) {
    if (pack == null) return false;
    if (IsUiOnlyPackId(pack.packId)) return false;
    if (IsGameplayPackKind(pack.kind)) return true;
    if (!string.IsNullOrWhiteSpace(pack.defaultLocationId)) return true;
    if (pack.ownedLocations != null && pack.ownedLocations.Count > 0) return true;
    if (pack.ownedEnemyTypes != null && pack.ownedEnemyTypes.Count > 0) return true;
    if (PackHasGameplayRoot(pack.ownedRoots)) return true;

    if (pack.authoringSources != null) {
      for (var i = 0; i < pack.authoringSources.Count; i++) {
        var source = pack.authoringSources[i];
        if (source == null) continue;
        if (IsGameplayRoot(source.assetPath)) return true;
        if (IsGameplayRoot(source.normalAssetPath)) return true;
        if (IsGameplayRoot(source.specularAssetPath)) return true;
      }
    }

    return false;
  }

  static bool IsGameplayPackKind(string kind) {
    var normalizedKind = NormalizeToken(kind);
    return string.Equals(normalizedKind, "gear", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(normalizedKind, "enemy", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(normalizedKind, "environment", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(normalizedKind, "destructible", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(normalizedKind, "objective", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(normalizedKind, "dialog", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(normalizedKind, "form", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(normalizedKind, "slice", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(normalizedKind, "episode", StringComparison.OrdinalIgnoreCase);
  }

  static bool IsGameplayPackId(string packId) {
    var normalizedPackId = NormalizeToken(packId);
    if (string.IsNullOrWhiteSpace(normalizedPackId)) return false;
    if (IsUiOnlyPackId(normalizedPackId)) return false;

    return normalizedPackId.StartsWith("Gear", StringComparison.OrdinalIgnoreCase) ||
           normalizedPackId.StartsWith("Enemy", StringComparison.OrdinalIgnoreCase) ||
           normalizedPackId.StartsWith("Environment", StringComparison.OrdinalIgnoreCase) ||
           normalizedPackId.StartsWith("Destructible", StringComparison.OrdinalIgnoreCase) ||
           normalizedPackId.StartsWith("Objective", StringComparison.OrdinalIgnoreCase) ||
           normalizedPackId.StartsWith("Dialog", StringComparison.OrdinalIgnoreCase) ||
           normalizedPackId.StartsWith("Form_", StringComparison.OrdinalIgnoreCase) ||
           normalizedPackId.StartsWith("Slice_", StringComparison.OrdinalIgnoreCase) ||
           normalizedPackId.StartsWith("Episode_", StringComparison.OrdinalIgnoreCase);
  }

  static bool IsUiOnlyPackId(string packId) {
    var normalizedPackId = NormalizeToken(packId);
    return normalizedPackId.EndsWith("UI", StringComparison.OrdinalIgnoreCase);
  }

  static bool PackHasGameplayRoot(List<string> roots) {
    if (roots == null || roots.Count <= 0) return false;

    for (var i = 0; i < roots.Count; i++) {
      if (IsGameplayRoot(roots[i])) return true;
    }

    return false;
  }

  static bool IsGameplayRoot(string assetPath) {
    var normalized = NormalizeAssetPath(assetPath);
    if (string.IsNullOrWhiteSpace(normalized)) return false;

    return normalized.StartsWith("Assets/Prefabs/Characters/", StringComparison.OrdinalIgnoreCase) ||
           normalized.StartsWith("Assets/Prefabs/Enemies/", StringComparison.OrdinalIgnoreCase) ||
           normalized.StartsWith("Assets/Prefabs/Projectiles/", StringComparison.OrdinalIgnoreCase) ||
           normalized.StartsWith("Assets/Materials/Gameplay/", StringComparison.OrdinalIgnoreCase) ||
           normalized.StartsWith("Assets/Sprites/Characters/", StringComparison.OrdinalIgnoreCase) ||
           normalized.StartsWith("Assets/Sprites/Enemies/", StringComparison.OrdinalIgnoreCase) ||
           normalized.StartsWith("Assets/Sprites/Effects/", StringComparison.OrdinalIgnoreCase) ||
           normalized.StartsWith("Assets/Sprites/Locations/", StringComparison.OrdinalIgnoreCase);
  }

  public static bool IsGameplayContentRequestedForConfiguredSelection() {
    var selection = AssetDatabase.LoadAssetAtPath<ContentPackSelection>(SelectionAssetPath);
    if (selection != null && selection.ExternalContentEnabled) {
      var packDefinitions = BuildPackDefinitions(selection.ExternalRoot);
      var packById = packDefinitions.ToDictionary(pack => pack.packId, StringComparer.OrdinalIgnoreCase);
      var activePackIds = ResolveConcreteActivePackIds(selection.GetNormalizedActivePackIds(), packById);
      return IsGameplayContentRequested(packById, activePackIds);
    }

    var registry = AssetDatabase.LoadAssetAtPath<ActiveContentRegistry>(ActiveRegistryAssetPath);
    if (registry != null && registry.ExternalContentActive) {
      return IsGameplayContentRequested(registry.ActivePackIds);
    }

    return true;
  }

  static string NormalizeAssetPath(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().Replace('\\', '/');
  }

  public static string GetPhysicalPath(string path) {
    if (string.IsNullOrWhiteSpace(path)) return "";
    var normalized = NormalizeAssetPath(path);
    if (normalized.StartsWith(StageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase)) {
      var relative = normalized.Substring(StageRootAssetPath.Length + 1);
      return Path.GetFullPath(Path.Combine(GetConfiguredExternalRoot(), relative)).Replace('\\', '/');
    }
    if (string.Equals(normalized, StageRootAssetPath, StringComparison.OrdinalIgnoreCase)) {
      return GetConfiguredExternalRoot();
    }

    return Path.GetFullPath(normalized).Replace('\\', '/');
  }

  static string NormalizeFullPath(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    var normalized = NormalizeAssetPath(value);
    if (string.Equals(normalized, StageRootAssetPath, StringComparison.OrdinalIgnoreCase) ||
        normalized.StartsWith(StageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase)) {
      return GetPhysicalPath(normalized);
    }

    return Path.GetFullPath(value).Replace('\\', '/');
  }

  public static string ToProjectAssetPath(string fullPath) {
    var normalizedFullPath = NormalizeFullPath(fullPath);
    var externalRoot = GetConfiguredExternalRoot();
    if (string.Equals(normalizedFullPath, externalRoot, StringComparison.OrdinalIgnoreCase)) {
      return StageRootAssetPath;
    }
    if (normalizedFullPath.StartsWith(externalRoot + "/", StringComparison.OrdinalIgnoreCase)) {
      var relative = normalizedFullPath.Substring(externalRoot.Length + 1);
      return NormalizeAssetPath(StageRootAssetPath + "/" + relative);
    }

    var projectRoot = NormalizeFullPath(GetProjectRoot());
    if (normalizedFullPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase)) {
      return normalizedFullPath.Substring(projectRoot.Length + 1);
    }
    return normalizedFullPath;
  }

  static string GetConfiguredExternalRoot() {
    var selection = AssetDatabase.LoadAssetAtPath<ContentPackSelection>(SelectionAssetPath);
    var configuredRoot = selection != null && !string.IsNullOrWhiteSpace(selection.ExternalRoot)
      ? selection.ExternalRoot
      : ResolveDefaultExternalRoot();
    return Path.GetFullPath(configuredRoot).Replace('\\', '/').TrimEnd('/');
  }

  public static string ResolveDefaultExternalRoot() {
    var manifestRoot = "";
    try {
      manifestRoot = ResolvePackageFileDependencyRoot();
    }
    catch (Exception ex) {
      Debug.LogWarning("[ContentPackPipeline] Failed to resolve package external root from manifest. fallback='" + DefaultExternalRootFallback + "' error='" + ex.Message + "'");
    }
    return string.IsNullOrWhiteSpace(manifestRoot) ? DefaultExternalRootFallback : manifestRoot;
  }

  static string ResolveDefaultExternalRootFallback() {
    var projectRoot = GetProjectRoot();
    var parent = Directory.GetParent(projectRoot)?.FullName;
    var root = string.IsNullOrWhiteSpace(parent)
      ? Path.Combine(projectRoot, "..", DefaultExternalRootFolderName)
      : Path.Combine(parent, DefaultExternalRootFolderName);
    return Path.GetFullPath(root).Replace('\\', '/').TrimEnd('/');
  }

  static string ResolvePackageFileDependencyRoot() {
    var projectRoot = GetProjectRoot();
    var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
    if (!File.Exists(manifestPath)) return "";

    var manifestText = File.ReadAllText(manifestPath);
    var match = ContentPackageDependencyRegex.Match(manifestText);
    if (!match.Success) return "";

    var packagePath = Uri.UnescapeDataString(match.Groups[1].Value).Replace('\\', '/').Trim();
    if (string.IsNullOrWhiteSpace(packagePath)) return "";

    var packageRoot = Path.IsPathRooted(packagePath)
      ? packagePath
      : Path.Combine(projectRoot, "Packages", packagePath);
    return Path.GetFullPath(packageRoot).Replace('\\', '/').TrimEnd('/');
  }

  static string GetProjectRoot() {
    return Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
  }

  static void LogTransitionRunSummary(string label, TransitionRunSummary summary) {
    if (summary == null) return;

    Debug.Log(
      "[ContentPackPipeline] [TransitionSummary] label='" + label + "'" +
      " mode='" + summary.mode + "'" +
      " stage=" + (summary.stageCompleted ? 1 : 0) +
      " audit=" + (summary.auditCompleted ? 1 : 0) +
      " runtime_index=" + (summary.runtimeIndexCompleted ? 1 : 0) +
      " unified_import=" + (summary.unifiedImportCompleted ? 1 : 0) +
      " hotset=" + (summary.hotsetCompleted ? 1 : 0) +
      " addressables=" + (summary.addressablesCompleted ? 1 : 0) +
      FormatExportStats(summary.export) +
      FormatAnalysisStats(summary.analysis)
    );
  }

  static string FormatExportStats(ExportSyncStats stats) {
    if (stats == null) return "";
    return
      " destination_entries_deleted=" + stats.destinationEntriesDeleted +
      " pack_dirs_created=" + stats.packDirectoriesCreated +
      " pack_dirs_recreated=" + stats.packDirectoriesRecreated +
      " asset_writes=" + stats.assetPayloadsWritten +
      " asset_skips=" + stats.assetPayloadsSkipped +
      " meta_writes=" + stats.metaPayloadsWritten +
      " meta_skips=" + stats.metaPayloadsSkipped +
      " generated_writes=" + stats.generatedFilesWritten +
      " manifest_writes=" + stats.manifestsWritten;
  }

  static string FormatAnalysisStats(OwnershipAnalysisReport report) {
    if (report == null) return "";
    return
      " duplicate_assets=" + report.spriteDuplicateCount +
      " legacy_generated_refs=" + report.legacyGeneratedReferenceCount +
      " staged_project_tree_dependencies=" + report.stagedProjectTreeDependencyCount +
      " staged_code_dependencies=" + report.stagedCodeDependencyCount +
      " main_build_dependencies=" + report.mainBuildDependencyCount +
      " ownership_findings=" + report.ownershipViolationCount;
  }

  static int CountTextOccurrences(string assetPath, string pattern) {
    var fullPath = Path.GetFullPath(assetPath);
    if (!File.Exists(fullPath) || string.IsNullOrWhiteSpace(pattern)) return 0;

    var text = File.ReadAllText(fullPath);
    var count = 0;
    var index = 0;
    while (true) {
      index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase);
      if (index < 0) break;
      count++;
      index += pattern.Length;
    }

    return count;
  }

  static string ComputeSha256(string text) {
    var bytes = Encoding.UTF8.GetBytes(text ?? "");
    using var sha = SHA256.Create();
    var hashBytes = sha.ComputeHash(bytes);
    var builder = new StringBuilder(hashBytes.Length * 2);
    for (var i = 0; i < hashBytes.Length; i++) {
      builder.Append(hashBytes[i].ToString("x2"));
    }
    return builder.ToString();
  }

  static string TryGetGitRevision() {
    try {
      var startInfo = new ProcessStartInfo {
        FileName = "git",
        Arguments = "rev-parse HEAD",
        CreateNoWindow = true,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        WorkingDirectory = GetProjectRoot()
      };

      using var process = Process.Start(startInfo);
      var output = process.StandardOutput.ReadToEnd().Trim();
      process.WaitForExit();
      return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : "unknown";
    }
    catch {
      return "unknown";
    }
  }

  static void LogErrors(string stage, List<string> errors) {
    if (errors == null || errors.Count <= 0) return;
    for (var i = 0; i < errors.Count; i++) {
      Debug.LogError("[ContentPackPipeline] [" + stage + "] " + errors[i]);
    }
  }
}
#endif
