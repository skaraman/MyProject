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
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Profiling;


public static partial class SpriteIndexBuilder {
  static bool RunAddressablesPlayerBuildPass(
    string contextLabel,
    string passLabel,
    string progressText,
    float progress,
    bool logResult,
    out AddressablesPlayerBuildResult result
  ) {
    result = null;
    EditorUtility.DisplayProgressBar("Sprite Streaming", progressText, progress);

    var phaseSuffix = string.Equals(passLabel, "final", StringComparison.OrdinalIgnoreCase)
      ? ""
      : "_" + passLabel;
    LogAddressablesBuildMemorySnapshot(contextLabel, "before_build" + phaseSuffix);
    AddressableAssetSettings.BuildPlayerContent(out result);
    LogAddressablesBuildMemorySnapshot(contextLabel, "after_build" + phaseSuffix);

    if (result == null) {
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "][" + passLabel + "] Addressables build did not return a result.");
      return false;
    }
    if (!string.IsNullOrWhiteSpace(result.Error)) {
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "][" + passLabel + "] Addressables build failed: " + result.Error);
      return false;
    }

    if (logResult) {
      Debug.Log(
        "[SpriteIndexBuilder] [" + contextLabel + "][" + passLabel + "] Build complete. output='" + (result.OutputPath ?? "") +
        "' locations=" + result.LocationCount +
        " duration=" + result.Duration.ToString("0.00", CultureInfo.InvariantCulture) + "s"
      );
    }

    return true;
  }

  static List<TextureBuildChunk> BuildManagedTextureBuildChunks(List<AddressableAssetGroup> managedTextureGroups) {
    var chunks = new List<TextureBuildChunk>();
    if (managedTextureGroups == null || managedTextureGroups.Count == 0) return chunks;

    var groupInfos = new List<(AddressableAssetGroup group, long approxSourceBytes, int entryCount)>(managedTextureGroups.Count);
    for (var i = 0; i < managedTextureGroups.Count; i++) {
      var group = managedTextureGroups[i];
      if (group == null) continue;
      var approxSourceBytes = GetApproxTextureGroupSourceBytes(group, out var entryCount);
      groupInfos.Add((group, approxSourceBytes, entryCount));
    }

    groupInfos.Sort((left, right) => {
      var byBytes = right.approxSourceBytes.CompareTo(left.approxSourceBytes);
      if (byBytes != 0) return byBytes;
      return string.Compare(left.group?.Name, right.group?.Name, StringComparison.OrdinalIgnoreCase);
    });

    TextureBuildChunk currentChunk = null;
    for (var i = 0; i < groupInfos.Count; i++) {
      var info = groupInfos[i];
      if (currentChunk == null ||
          (currentChunk.approxSourceBytes >= BuilderConfig.ChunkedTextureBuildTargetApproxBytes && currentChunk.groups.Count > 0) ||
          currentChunk.groups.Count >= 20) {
        currentChunk = new TextureBuildChunk();
        chunks.Add(currentChunk);
      }

      currentChunk.groups.Add(info.group);
      currentChunk.approxSourceBytes += info.approxSourceBytes;
      currentChunk.entryCount += info.entryCount;
    }

    return chunks;
  }

  static long GetApproxTextureGroupSourceBytes(AddressableAssetGroup group, out int entryCount) {
    entryCount = 0;
    if (group == null) return 0;

    var totalApproxBytes = 0L;
    foreach (var entry in group.entries) {
      if (entry == null) continue;
      entryCount++;
      var assetPath = NormalizePath(AssetDatabase.GUIDToAssetPath(entry.guid));
      if (string.IsNullOrWhiteSpace(assetPath)) {
        assetPath = NormalizePath(entry.AssetPath);
      }

      totalApproxBytes += GetApproxAssetSourceBytes(assetPath, out _, out _);
    }

    return totalApproxBytes;
  }

  static Dictionary<AddressableAssetGroup, bool> CaptureIncludeInBuildStates(AddressableAssetSettings settings) {
    var states = new Dictionary<AddressableAssetGroup, bool>();
    if (settings == null || settings.groups == null) return states;

    for (var i = 0; i < settings.groups.Count; i++) {
      var group = settings.groups[i];
      var schema = group?.GetSchema<BundledAssetGroupSchema>();
      if (schema == null) continue;
      states[group] = schema.IncludeInBuild;
    }

    return states;
  }

  static bool BuildOptionalContentPackCatalogs(
    AddressableAssetSettings settings,
    Dictionary<AddressableAssetGroup, bool> originalStates,
    List<ContentPackPipeline.ContentPackRuntimeCatalogBuildInfo> optionalPackCatalogs,
    string contextLabel,
    bool logResult,
    bool cleanCachesBeforeBuild
  ) {
    if (settings == null || optionalPackCatalogs == null || optionalPackCatalogs.Count <= 0) {
      return true;
    }

    for (var i = 0; i < optionalPackCatalogs.Count; i++) {
      var catalog = optionalPackCatalogs[i];
      if (catalog == null || string.IsNullOrWhiteSpace(catalog.groupName)) {
        continue;
      }

      if (!cleanCachesBeforeBuild) {
        var catalogPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(catalog.externalRootPath, catalog.catalogRelativePath));
        if (System.IO.File.Exists(catalogPath)) {
          if (logResult) {
            UnityEngine.Debug.Log(
              "[SpriteIndexBuilder] [" + contextLabel + "][PackCatalog] Skipping already built content pack catalog." +
              " pack_id='" + (catalog.packId ?? "") + "'"
            );
          }
          continue;
        }
      }

      var group = settings.FindGroup(catalog.groupName);
      if (group == null || group.entries.Count <= 0) {
        Debug.LogError(
          "[SpriteIndexBuilder] [" + contextLabel + "][PackCatalog] Missing Addressables group entries for required content pack catalog." +
          " pack_id='" + (catalog.packId ?? "") + "'" +
          " group='" + (catalog.groupName ?? "") + "'"
        );
        return false;
      }

      ApplyChunkIncludeInBuildSelection(settings, originalStates, new[] { group });
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();

      var passContextLabel = contextLabel + "][PackCatalog " + (catalog.packId ?? "");
      if (!ValidateAddressablesPackedBuildSpriteSliceRisk(settings, passContextLabel)) {
        return false;
      }

      var passLabel = "pack_" + SanitizeAddressablesLabel(catalog.packId);
      ReleaseEditorBuildPrepMemory(contextLabel + " " + catalog.packId);
      if (!RunAddressablesPlayerBuildPass(
            contextLabel,
            passLabel,
            "Building content pack catalog " + (i + 1) + "/" + optionalPackCatalogs.Count + "...",
            0.8f,
            logResult,
            out var result)) {
        return false;
      }

      if (!CopyPackCatalogBuildOutput(result, catalog, contextLabel, logResult)) {
        return false;
      }
    }

    return true;
  }

  static void ApplyHybridBaseIncludeInBuildSelection(
    AddressableAssetSettings settings,
    Dictionary<AddressableAssetGroup, bool> originalStates,
    List<ContentPackPipeline.ContentPackRuntimeCatalogBuildInfo> optionalPackCatalogs
  ) {
    if (settings == null || originalStates == null) {
      return;
    }

    var optionalGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (optionalPackCatalogs != null) {
      for (var i = 0; i < optionalPackCatalogs.Count; i++) {
        if (optionalPackCatalogs[i] == null) continue;
        if (string.IsNullOrWhiteSpace(optionalPackCatalogs[i].groupName)) continue;
        optionalGroupNames.Add(optionalPackCatalogs[i].groupName);
      }
    }

    foreach (var pair in originalStates) {
      var group = pair.Key;
      var schema = group?.GetSchema<BundledAssetGroupSchema>();
      if (schema == null) continue;

      var include = pair.Value;
      if (group != null && optionalGroupNames.Contains(group.Name)) {
        include = false;
      }
      if (IsOptionalManagedTextureGroup(group)) {
        include = false;
      }
      if (group == settings.DefaultGroup) {
        include = true;
      }
      if (schema.IncludeInBuild == include) continue;

      schema.IncludeInBuild = include;
      EditorUtility.SetDirty(schema);
      if (group != null) {
        EditorUtility.SetDirty(group);
      }
    }

    EditorUtility.SetDirty(settings);
  }

  static bool IsOptionalManagedTextureGroup(AddressableAssetGroup group) {
    if (group == null || string.IsNullOrWhiteSpace(group.Name)) {
      return false;
    }
    if (!group.Name.StartsWith(BuilderConfig.ManagedTextureGroupPrefix, StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    var packId = group.Name.Substring(BuilderConfig.ManagedTextureGroupPrefix.Length);
    if (string.IsNullOrWhiteSpace(packId)) {
      return false;
    }

    return !string.Equals(packId, ContentPackPipeline.CorePackId, StringComparison.OrdinalIgnoreCase);
  }

  static bool ValidateBaseAddressablesBootstrapContract(
    AddressableAssetSettings settings,
    string contextLabel,
    bool logResult
  ) {
    var errors = new List<string>();
    if (settings == null) {
      errors.Add("Addressables settings were not found.");
      return LogBaseAddressablesBootstrapValidation(contextLabel, errors, 0, 0, logResult);
    }

    var bootstrapGroup = settings.DefaultGroup;
    if (bootstrapGroup == null) {
      errors.Add("The configured default Addressables bootstrap group was not found.");
      return LogBaseAddressablesBootstrapValidation(contextLabel, errors, 0, 0, logResult);
    }

    var bootstrapSchema = bootstrapGroup.GetSchema<BundledAssetGroupSchema>();
    if (bootstrapSchema == null) {
      errors.Add("Bootstrap group is missing BundledAssetGroupSchema. group='" + bootstrapGroup.Name + "'");
    }
    else if (!bootstrapSchema.IncludeInBuild) {
      errors.Add("Bootstrap group is excluded from the final base build. group='" + bootstrapGroup.Name + "'");
    }

    ValidateOptionalTextureGroupsExcludedFromBase(settings, errors);

    var requiredPrefabPaths = CollectRequiredBootstrapPrefabPaths();
    for (var i = 0; i < requiredPrefabPaths.Count; i++) {
      ValidateRequiredBootstrapAssetEntry(
        settings,
        bootstrapGroup,
        requiredPrefabPaths[i],
        "prefab",
        errors
      );
    }

    var requiredMaterialPaths = RuntimeMaterialAddressablesBootstrap.CollectRequiredGameplayMaterialAssetPaths();
    for (var i = 0; i < requiredMaterialPaths.Count; i++) {
      ValidateRequiredBootstrapAssetEntry(
        settings,
        bootstrapGroup,
        requiredMaterialPaths[i],
        "material",
        errors
      );
    }

    return LogBaseAddressablesBootstrapValidation(
      contextLabel,
      errors,
      requiredPrefabPaths.Count,
      requiredMaterialPaths.Count,
      logResult
    );
  }

  static List<string> CollectRequiredBootstrapPrefabPaths() {
    var result = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    AddRequiredBootstrapPrefabPath(GameplayCoreAssetPaths.EsperanzaPrefabAssetPath, result, seen);

    foreach (var location in LocationEnemyData.locations.Values) {
      var assetPath = location?.locationPrefabData != null
        ? location.locationPrefabData.AssetPath
        : "";
      AddRequiredBootstrapPrefabPath(assetPath, result, seen);
    }

    foreach (var projectile in Projectiles.EnumerateAll()) {
      var data = projectile.Value;
      if (data == null) continue;
      if (!data.TryGetPrefabAddress(out var assetPath)) continue;
      AddRequiredBootstrapPrefabPath(assetPath, result, seen);
    }

    result.Sort(StringComparer.Ordinal);
    return result;
  }

  static void AddRequiredBootstrapPrefabPath(
    string assetPath,
    List<string> output,
    HashSet<string> seen
  ) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return;
    if (!seen.Add(normalizedAssetPath)) return;
    output.Add(normalizedAssetPath);
  }

  static void ValidateRequiredBootstrapAssetEntry(
    AddressableAssetSettings settings,
    AddressableAssetGroup bootstrapGroup,
    string assetPath,
    string assetKind,
    List<string> errors
  ) {
    var normalizedAssetKind = string.IsNullOrWhiteSpace(assetKind) ? "asset" : assetKind;
    if (!RuntimeMaterialAddressablesBootstrap.TryResolveGuid(assetPath, out var guid)) {
      errors.Add(
        "Required bootstrap " + normalizedAssetKind + " was not found." +
        " asset_path='" + assetPath + "'"
      );
      return;
    }

    var entry = settings.FindAssetEntry(guid);
    if (entry == null) {
      errors.Add(
        "Required bootstrap " + normalizedAssetKind + " has no Addressables entry." +
        " asset_path='" + assetPath + "'"
      );
      return;
    }

    if (entry.parentGroup != bootstrapGroup) {
      var actualGroupName = entry.parentGroup != null ? entry.parentGroup.Name : "";
      errors.Add(
        "Required bootstrap " + normalizedAssetKind + " is outside the included bootstrap group." +
        " asset_path='" + assetPath + "'" +
        " expected_group='" + bootstrapGroup.Name + "'" +
        " actual_group='" + actualGroupName + "'"
      );
    }

    var address = NormalizePath(entry.address);
    if (!string.Equals(address, assetPath, StringComparison.Ordinal)) {
      errors.Add(
        "Required bootstrap " + normalizedAssetKind + " address is not its runtime asset key." +
        " asset_path='" + assetPath + "'" +
        " address='" + address + "'"
      );
    }
  }

  static void ValidateOptionalTextureGroupsExcludedFromBase(
    AddressableAssetSettings settings,
    List<string> errors
  ) {
    if (settings.groups == null) return;

    for (var i = 0; i < settings.groups.Count; i++) {
      var group = settings.groups[i];
      if (!IsOptionalManagedTextureGroup(group)) continue;

      var schema = group.GetSchema<BundledAssetGroupSchema>();
      if (schema == null || !schema.IncludeInBuild) continue;
      errors.Add("Optional texture group leaked into the final base build. group='" + group.Name + "'");
    }
  }

  static bool LogBaseAddressablesBootstrapValidation(
    string contextLabel,
    List<string> errors,
    int requiredPrefabCount,
    int requiredMaterialCount,
    bool logResult
  ) {
    if (errors.Count > 0) {
      var message = new StringBuilder();
      message.Append("[SpriteIndexBuilder] [").Append(contextLabel)
        .Append("] ERROR: Base Addressables bootstrap contract failed.")
        .Append(" requiredPrefabs=").Append(requiredPrefabCount)
        .Append(" requiredMaterials=").Append(requiredMaterialCount)
        .Append(" errors=").Append(errors.Count);
      for (var i = 0; i < errors.Count; i++) {
        message.Append("\n  ").Append(errors[i]);
      }
      Debug.LogError(message.ToString());
      return false;
    }

    if (logResult) {
      Debug.Log(
        "[SpriteIndexBuilder] [" + contextLabel + "] Base Addressables bootstrap contract validated." +
        " requiredPrefabs=" + requiredPrefabCount +
        " requiredMaterials=" + requiredMaterialCount
      );
    }

    return true;
  }

  static bool CopyPackCatalogBuildOutput(
    AddressablesPlayerBuildResult result,
    ContentPackPipeline.ContentPackRuntimeCatalogBuildInfo catalog,
    string contextLabel,
    bool logResult
  ) {
    var outputPath = NormalizePath(result?.OutputPath);
    if (string.IsNullOrWhiteSpace(outputPath)) {
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "][PackCatalog] Addressables build result had no output path.");
      return false;
    }

    var outputRoot = File.Exists(outputPath)
      ? Path.GetDirectoryName(outputPath)
      : outputPath;
    outputRoot = string.IsNullOrWhiteSpace(outputRoot) ? "" : Path.GetFullPath(outputRoot);
    if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot)) {
      Debug.LogError(
        "[SpriteIndexBuilder] [" + contextLabel + "][PackCatalog] Addressables output folder was not found." +
        " pack_id='" + (catalog?.packId ?? "") + "'" +
        " output='" + outputPath + "'"
      );
      return false;
    }

    var externalRoot = Path.GetFullPath(catalog.externalRootPath);
    var targetRoot = Path.GetFullPath(Path.Combine(externalRoot, catalog.bundleRootRelativePath));
    if (!targetRoot.StartsWith(externalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
      Debug.LogError(
        "[SpriteIndexBuilder] [" + contextLabel + "][PackCatalog] Pack catalog output escaped pack root." +
        " pack_id='" + (catalog.packId ?? "") + "'" +
        " target='" + targetRoot + "'"
      );
      return false;
    }

    if (Directory.Exists(targetRoot)) {
      Directory.Delete(targetRoot, recursive: true);
    }
    Directory.CreateDirectory(targetRoot);
    CopyDirectoryContents(outputRoot, targetRoot);
    EnsureCatalogAlias(externalRoot, catalog.catalogRelativePath);

    var catalogPath = Path.GetFullPath(Path.Combine(externalRoot, catalog.catalogRelativePath));
    if (!File.Exists(catalogPath)) {
      Debug.LogError(
        "[SpriteIndexBuilder] [" + contextLabel + "][PackCatalog] Expected content pack catalog was not written." +
        " pack_id='" + (catalog.packId ?? "") + "'" +
        " catalog='" + catalogPath.Replace('\\', '/') + "'"
      );
      return false;
    }

    if (logResult) {
      Debug.Log(
        "[SpriteIndexBuilder] [" + contextLabel + "][PackCatalog] Wrote content pack catalog." +
        " pack_id='" + (catalog.packId ?? "") + "'" +
        " output='" + targetRoot.Replace('\\', '/') + "'" +
        " catalog='" + (catalog.catalogRelativePath ?? "") + "'"
      );
    }

    return true;
  }

  static void CopyDirectoryContents(string sourceRoot, string targetRoot) {
    var directories = Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories);
    for (var i = 0; i < directories.Length; i++) {
      var relativeDirectory = GetRelativeFileSystemPath(sourceRoot, directories[i]);
      Directory.CreateDirectory(Path.Combine(targetRoot, relativeDirectory));
    }

    var files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
    for (var i = 0; i < files.Length; i++) {
      var relativeFile = GetRelativeFileSystemPath(sourceRoot, files[i]);
      var targetFile = Path.Combine(targetRoot, relativeFile);
      var targetDirectory = Path.GetDirectoryName(targetFile);
      if (!string.IsNullOrWhiteSpace(targetDirectory)) {
        Directory.CreateDirectory(targetDirectory);
      }
      File.Copy(files[i], targetFile, overwrite: true);
    }
  }

  static string GetRelativeFileSystemPath(string root, string path) {
    if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path)) {
      return "";
    }

    var rootFullPath = Path.GetFullPath(root);
    if (!rootFullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) &&
        !rootFullPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)) {
      rootFullPath += Path.DirectorySeparatorChar;
    }

    var pathFullPath = Path.GetFullPath(path);
    var rootUri = new Uri(rootFullPath);
    var pathUri = new Uri(pathFullPath);
    var relativeUri = rootUri.MakeRelativeUri(pathUri);
    return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
  }

  static void EnsureCatalogAlias(string externalRoot, string catalogRelativePath) {
    var targetCatalogPath = Path.GetFullPath(Path.Combine(externalRoot, catalogRelativePath));
    if (File.Exists(targetCatalogPath)) {
      return;
    }

    var targetFolder = Path.GetDirectoryName(targetCatalogPath);
    if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder)) {
      return;
    }

    var catalogs = Directory.GetFiles(targetFolder, "catalog*.bin", SearchOption.AllDirectories);
    if (catalogs.Length <= 0) {
      return;
    }

    Array.Sort(catalogs, StringComparer.OrdinalIgnoreCase);
    var targetDirectory = Path.GetDirectoryName(targetCatalogPath);
    if (!string.IsNullOrWhiteSpace(targetDirectory)) {
      Directory.CreateDirectory(targetDirectory);
    }
    File.Copy(catalogs[0], targetCatalogPath, overwrite: true);
  }

  static void ApplyChunkIncludeInBuildSelection(
    AddressableAssetSettings settings,
    Dictionary<AddressableAssetGroup, bool> originalStates,
    IReadOnlyCollection<AddressableAssetGroup> activeGroups
  ) {
    if (settings == null || originalStates == null) return;

    var activeGroupSet = new HashSet<AddressableAssetGroup>(activeGroups ?? Array.Empty<AddressableAssetGroup>());
    foreach (var pair in originalStates) {
      var group = pair.Key;
      var schema = group?.GetSchema<BundledAssetGroupSchema>();
      if (schema == null) continue;

      var shouldInclude = activeGroupSet.Contains(group);
      if (schema.IncludeInBuild == shouldInclude) continue;

      schema.IncludeInBuild = shouldInclude;
      EditorUtility.SetDirty(schema);
      if (group != null) {
        EditorUtility.SetDirty(group);
      }
    }

    EditorUtility.SetDirty(settings);
  }

  static void RestoreIncludeInBuildStates(AddressableAssetSettings settings, Dictionary<AddressableAssetGroup, bool> originalStates) {
    if (settings == null || originalStates == null) return;

    foreach (var pair in originalStates) {
      var group = pair.Key;
      var schema = group?.GetSchema<BundledAssetGroupSchema>();
      if (schema == null) continue;
      if (schema.IncludeInBuild == pair.Value) continue;

      schema.IncludeInBuild = pair.Value;
      EditorUtility.SetDirty(schema);
      if (group != null) {
        EditorUtility.SetDirty(group);
      }
    }

    EditorUtility.SetDirty(settings);
  }

  static string FormatByteCount(long bytes) {
    if (bytes < 1024L) return bytes + " B";
    if (bytes < 1024L * 1024L) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
    if (bytes < 1024L * 1024L * 1024L) return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
  }

  static List<AddressableAssetGroup> GetManagedTextureGroups(AddressableAssetSettings settings) {
    var result = new List<AddressableAssetGroup>();
    if (settings == null || settings.groups == null) return result;
    for (var i = 0; i < settings.groups.Count; i++) {
      var group = settings.groups[i];
      if (group == null) continue;
      var schema = group.GetSchema<BundledAssetGroupSchema>();
      if (schema == null || !schema.IncludeInBuild) continue;
      if (group.Name.StartsWith(BuilderConfig.ManagedTextureGroupPrefix, StringComparison.OrdinalIgnoreCase)) {
        result.Add(group);
      }
    }
    return result;
  }

  static long GetApproxAssetSourceBytes(string assetPath, out string extension, out bool isDirectory) {
    extension = "";
    isDirectory = false;
    if (string.IsNullOrWhiteSpace(assetPath)) return 0;

    extension = Path.GetExtension(assetPath).ToLowerInvariant();
    var physicalPath = ContentPackPipeline.GetPhysicalPath(assetPath);
    if (Directory.Exists(physicalPath)) {
      isDirectory = true;
      return 0;
    }

    try {
      var info = new FileInfo(physicalPath);
      return info.Exists ? info.Length : 0;
    }
    catch {
      return 0;
    }
  }

  static bool ValidateAddressablesPackedBuildSpriteSliceRisk(AddressableAssetSettings settings, string contextLabel) {
    if (settings == null) return true;

    var totalEstimatedSlices = 0L;
    var riskCandidates = new List<(string assetPath, int sliceCount)>();

    for (var gi = 0; gi < settings.groups.Count; gi++) {
      var group = settings.groups[gi];
      if (group == null) continue;
      var schema = group.GetSchema<BundledAssetGroupSchema>();
      if (schema == null || !schema.IncludeInBuild) continue;

      foreach (var entry in group.entries) {
        if (entry == null) continue;
        var assetPath = NormalizePath(entry.AssetPath);
        if (string.IsNullOrWhiteSpace(assetPath)) continue;

        var sliceCount = EstimateSpriteSliceCount(assetPath);
        totalEstimatedSlices += sliceCount;
        if (sliceCount > 0) {
          riskCandidates.Add((assetPath, sliceCount));
        }
      }
    }

    if (totalEstimatedSlices <= BuilderConfig.MaxEstimatedSpriteSlicesForPackedBuild) return true;

    riskCandidates.Sort((a, b) => b.sliceCount.CompareTo(a.sliceCount));
    var sb = new StringBuilder();
    sb.Append("[SpriteIndexBuilder] [").Append(contextLabel)
      .Append("] ERROR: Estimated sprite slice count exceeds budget.")
      .Append(" estimated=").Append(totalEstimatedSlices)
      .Append(" budget=").Append(BuilderConfig.MaxEstimatedSpriteSlicesForPackedBuild);

    var shown = Math.Min(BuilderConfig.MaxLoggedSpriteSliceRiskCandidates, riskCandidates.Count);
    for (var i = 0; i < shown; i++) {
      sb.Append("\n  ").Append(riskCandidates[i].assetPath).Append(" slices~").Append(riskCandidates[i].sliceCount);
    }

    Debug.LogError(sb.ToString());
    return false;
  }

  static bool ValidateBuilderManagedAddressableNamingContract(
    AddressableAssetSettings settings,
    string contextLabel,
    bool logResult
  ) {
    if (settings == null) return false;

    var errors = new List<string>();
    var ownerByAddress = new Dictionary<string, string>(StringComparer.Ordinal);
    var managedEntryCount = 0;

    for (var groupIndex = 0; groupIndex < settings.groups.Count; groupIndex++) {
      var group = settings.groups[groupIndex];
      if (group == null || group.entries == null) continue;

      foreach (var entry in group.entries) {
        if (entry == null) continue;

        var assetPath = NormalizePath(entry.AssetPath);
        if (!IsBuilderManagedCanonicalPathAsset(assetPath)) continue;

        managedEntryCount++;
        var expectedAddress = assetPath;
        var actualAddress = NormalizePath(entry.address);
        if (!string.Equals(actualAddress, expectedAddress, StringComparison.Ordinal)) {
          errors.Add(
            "Managed runtime asset has a non-canonical Addressables address." +
            " group='" + group.Name + "'" +
            " asset_path='" + assetPath + "'" +
            " address='" + (entry.address ?? "") + "'"
          );
          continue;
        }

        var owner = group.Name + " | " + assetPath;
        if (ownerByAddress.TryGetValue(actualAddress, out var existingOwner)) {
          if (!string.Equals(existingOwner, owner, StringComparison.Ordinal)) {
            errors.Add(
              "Managed runtime asset address is duplicated." +
              " address='" + actualAddress + "'" +
              " first='" + existingOwner + "'" +
              " second='" + owner + "'"
            );
          }
          continue;
        }

        ownerByAddress[actualAddress] = owner;
      }
    }

    if (errors.Count > 0) {
      var sb = new StringBuilder();
      sb.Append("[SpriteIndexBuilder] [").Append(contextLabel).Append("] ERROR: Addressables naming contract failed.");
      sb.Append(" managedEntries=").Append(managedEntryCount);
      sb.Append(" errors=").Append(errors.Count);
      var shown = Math.Min(errors.Count, 20);
      for (var i = 0; i < shown; i++) {
        sb.Append("\n  ").Append(errors[i]);
      }
      if (errors.Count > shown) {
        sb.Append("\n  ... ").Append(errors.Count - shown).Append(" more");
      }
      Debug.LogError(sb.ToString());
      return false;
    }

    if (logResult) {
      Debug.Log(
        "[SpriteIndexBuilder] [" + contextLabel + "] Addressables naming contract validated." +
        " managedEntries=" + managedEntryCount +
        " uniqueAddresses=" + ownerByAddress.Count
      );
    }

    return true;
  }

  static bool IsBuilderManagedCanonicalPathAsset(string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return false;
    if (IsRuntimeTextureAssetPath(normalizedAssetPath)) return true;
    if (!TrimmedAtlasExporterWindow.IsRuntimeMetadataAssetPath(normalizedAssetPath)) return false;
    if (IsRuntimeTextureAssetPath(Path.ChangeExtension(normalizedAssetPath, ".png"))) return true;
    if (IsRuntimeTextureAssetPath(Path.ChangeExtension(normalizedAssetPath, ".jpg"))) return true;
    if (IsRuntimeTextureAssetPath(Path.ChangeExtension(normalizedAssetPath, ".jpeg"))) return true;
    return false;
  }

  static int EstimateSpriteSliceCount(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return 0;
    if (!assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
        !assetPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
        !assetPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) &&
        !assetPath.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) &&
        !assetPath.EndsWith(".psd", StringComparison.OrdinalIgnoreCase)) {
      return 0;
    }

    if (cachedSpriteSliceCountsByAssetPath.TryGetValue(assetPath, out var cached)) return cached;

    var metaPath = assetPath + ".meta";
    var physicalMetaPath = ContentPackPipeline.GetPhysicalPath(metaPath);
    if (!File.Exists(physicalMetaPath)) {
      cachedSpriteSliceCountsByAssetPath[assetPath] = 0;
      return 0;
    }

    var count = 0;
    foreach (var line in File.ReadLines(physicalMetaPath)) {
      var trimmed = line.Trim();
      if (trimmed.StartsWith("- serializedVersion:", StringComparison.Ordinal) ||
          trimmed.StartsWith("serializedVersion:", StringComparison.Ordinal) &&
          line.StartsWith("    - ", StringComparison.Ordinal)) {
        count++;
      }
    }

    cachedSpriteSliceCountsByAssetPath[assetPath] = count;
    return count;
  }

  static void LogAddressablesBuildPlan(AddressableAssetSettings settings, string contextLabel) {
    if (settings == null) return;

    var totalGroups = 0;
    var totalEntries = 0;
    var totalApproxBytes = 0L;
    var includedGroups = 0;

    for (var i = 0; i < settings.groups.Count; i++) {
      var group = settings.groups[i];
      if (group == null) continue;
      totalGroups++;
      var schema = group.GetSchema<BundledAssetGroupSchema>();
      if (schema == null || !schema.IncludeInBuild) continue;
      includedGroups++;
      totalEntries += group.entries.Count;
      totalApproxBytes += GetApproxTextureGroupSourceBytes(group, out _);
    }

    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "] Addressables build plan:" +
      " groups=" + totalGroups +
      " includedGroups=" + includedGroups +
      " entries=" + totalEntries +
      " approxSource=" + FormatByteCount(totalApproxBytes)
    );
  }

  static void LogAddressablesBuildMemorySnapshot(string contextLabel, string phase) {
    var mono = Profiler.GetMonoHeapSizeLong();
    var monoUsed = Profiler.GetMonoUsedSizeLong();
    var total = Profiler.GetTotalAllocatedMemoryLong();
    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "][mem:" + phase + "]" +
      " mono=" + FormatByteCount(mono) +
      " monoUsed=" + FormatByteCount(monoUsed) +
      " totalAlloc=" + FormatByteCount(total)
    );
  }

  static void ReleaseEditorBuildPrepMemory(string contextLabel) {
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    LogAddressablesBuildMemorySnapshot(contextLabel, "after_gc");
  }

  // Content pack builds are driven by Tools/ContentPackIterationUI.py.

  public static bool PrepareForPlayerBuild(bool logResult, bool failOnError) {
    if (logResult) {
      Debug.Log(
        "[SpriteIndexBuilder] [" + BuildContext.PlayerPrebuild + "] Preparing staged active packs, auditing staged content, and rebuilding the runtime index for player build. Addressables content build is handled by Unity's build pipeline."
      );
    }
    return RebuildRuntimeIndexInternal(logResult, failOnError, BuildContext.PlayerPrebuild, prepareSelectedPacks: false);
  }

  /// <summary>
  /// Builds addressables for active content packs.
  /// </summary>
  public static bool BuildActiveContentMenu(bool logResult = true, bool cleanCachesBeforeBuild = false) {
    return BuildAddressablesContentPrepared(
      contextLabel: "Active Content Menu",
      logResult: logResult,
      cleanCachesBeforeBuild: cleanCachesBeforeBuild
    );
  }

}
#endif
