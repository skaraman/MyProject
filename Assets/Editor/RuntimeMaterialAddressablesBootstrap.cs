#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class RuntimeMaterialAddressablesBootstrap {
  static readonly string[] MaterialOwnerExtensions = { ".prefab", ".unity" };
  static bool initialized;

  sealed class RuntimeMaterialAsset {
    public string assetPath;
    public string guid;
  }

  static RuntimeMaterialAddressablesBootstrap() {
    if (initialized) return;
    initialized = true;
    EditorApplication.delayCall += () => SyncRuntimeMaterialAddressables(logResult: false, saveAndRefresh: false);
  }

  public static void SyncRuntimeMaterialAddressablesMenu() {
    SyncRuntimeMaterialAddressables(logResult: true, saveAndRefresh: true);
  }

  public static bool SyncRuntimeMaterialAddressables(bool logResult, bool saveAndRefresh) {
    if (!RuntimePrefabAddressables.TryGetSettingsAndDefaultGroup(
          nameof(RuntimeMaterialAddressablesBootstrap),
          logResult,
          out var settings,
          out var defaultGroup)) {
      return false;
    }

    var externalContentActive = IsExternalContentConfigured();
    var materialAssets = CollectRuntimeMaterialAssets();
    var changed = false;
    for (var i = 0; i < materialAssets.Count; i++) {
      var materialAsset = materialAssets[i];
      if (materialAsset == null) continue;
      if (RuntimePrefabAddressables.EnsureAssetEntryByGuid(
            settings,
            defaultGroup,
            materialAsset.guid,
            materialAsset.assetPath)) {
        changed = true;
      }
    }

    if (externalContentActive && RemoveSourceMaterialEntriesWithStagedEquivalents(settings, materialAssets)) {
      changed = true;
    }

    if (changed && saveAndRefresh) {
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();
    }

    if (logResult) {
      Debug.Log(
        "[RuntimeMaterialAddressablesBootstrap] Synced runtime material Addressables entries." +
        " external_content_active=" + externalContentActive +
        FormatStageRootDiagnostics() +
        " count=" + materialAssets.Count +
        " changed=" + changed +
        " material_paths='" + FormatMaterialAssetPaths(materialAssets) + "'."
      );
    }

    return changed;
  }

  public static List<string> CollectRuntimeMaterialAssetPaths() {
    var materialAssets = CollectRuntimeMaterialAssets();
    var result = new List<string>(materialAssets.Count);
    for (var i = 0; i < materialAssets.Count; i++) {
      if (materialAssets[i] == null) continue;
      result.Add(materialAssets[i].assetPath);
    }

    return result;
  }

  static List<RuntimeMaterialAsset> CollectRuntimeMaterialAssets() {
    var result = new List<RuntimeMaterialAsset>();
    var ownerAssetPaths = new List<string>();
    var externalContentActive = IsExternalContentConfigured();
    var gameplayContentRequested = ContentPackPipeline.IsGameplayContentRequestedForConfiguredSelection();

    if (externalContentActive) {
      var activeStageRoots = CollectConfiguredActiveStageRoots();
      for (var i = 0; i < activeStageRoots.Count; i++) {
        AddMaterialFilesUnderRoot(activeStageRoots[i], result);
        AddOwnerFilesUnderRoot(activeStageRoots[i], ownerAssetPaths);
      }
      if (gameplayContentRequested) {
        AddUniqueMaterialIfExists(result, ResolvePreferredActiveAssetPath(GameplayCoreAssetPaths.EsperanzaGearMaterialAssetPath));
        AddUniqueMaterialIfExists(result, ResolvePreferredActiveAssetPath(GameplayCoreAssetPaths.EsperanzaHairMaterialAssetPath));
        AddUniqueMaterialIfExists(result, ResolvePreferredActiveAssetPath(GameplayCoreAssetPaths.EsperanzaBodyMaterialAssetPath));
      }
    }
    else {
      AddRuntimeFallbackOwners(ownerAssetPaths);
      AddUniqueMaterialIfExists(result, RuntimePrefabAddressables.NormalizeAssetPath(GameplayCoreAssetPaths.EsperanzaGearMaterialAssetPath));
      AddUniqueMaterialIfExists(result, RuntimePrefabAddressables.NormalizeAssetPath(GameplayCoreAssetPaths.EsperanzaHairMaterialAssetPath));
      AddUniqueMaterialIfExists(result, RuntimePrefabAddressables.NormalizeAssetPath(GameplayCoreAssetPaths.EsperanzaBodyMaterialAssetPath));
    }

    AddMaterialDependencies(ownerAssetPaths, result);
    result.Sort((left, right) => string.Compare(left?.assetPath, right?.assetPath, StringComparison.OrdinalIgnoreCase));
    return result;
  }

  static bool IsExternalContentConfigured() {
    var registry = AssetDatabase.LoadAssetAtPath<ActiveContentRegistry>(ContentPackPipeline.ActiveRegistryAssetPath);
    if (registry != null &&
        registry.ExternalContentActive &&
        registry.ActivePackIds != null &&
        registry.ActivePackIds.Count > 0) {
      return true;
    }

    var selection = AssetDatabase.LoadAssetAtPath<ContentPackSelection>(ContentPackPipeline.SelectionAssetPath);
    if (selection != null &&
        selection.ExternalContentEnabled &&
        selection.GetNormalizedActivePackIds().Count > 0) {
      return true;
    }

    return HasStagedExternalMaterialFiles();
  }

  static List<string> CollectConfiguredActiveStageRoots() {
    var result = new List<string>();

    var selection = AssetDatabase.LoadAssetAtPath<ContentPackSelection>(ContentPackPipeline.SelectionAssetPath);
    if (selection != null && selection.ExternalContentEnabled) {
      AddStageRootsForPackIds(result, selection.GetNormalizedActivePackIds());
      return result;
    }

    var registry = AssetDatabase.LoadAssetAtPath<ActiveContentRegistry>(ContentPackPipeline.ActiveRegistryAssetPath);
    if (registry != null && registry.ExternalContentActive) {
      AddStageRootsForPackIds(result, registry.ActivePackIds);
    }

    return result;
  }

  static void AddStageRootsForPackIds(List<string> output, IReadOnlyList<string> packIds) {
    if (output == null || packIds == null) return;

    for (var i = 0; i < packIds.Count; i++) {
      var stageRoot = RuntimePrefabAddressables.NormalizeAssetPath(
        ContentPackPipeline.ResolveStageAssetRoot(packIds[i])
      );
      if (string.IsNullOrWhiteSpace(stageRoot)) continue;
      AddUniquePath(output, stageRoot);
    }
  }

  static void AddMaterialFilesUnderRoot(string assetRoot, List<RuntimeMaterialAsset> output) {
    AddFilesUnderRoot(assetRoot, ".mat", output);
  }

  static void AddOwnerFilesUnderRoot(string assetRoot, List<string> output) {
    for (var i = 0; i < MaterialOwnerExtensions.Length; i++) {
      AddOwnerFilesUnderRoot(assetRoot, MaterialOwnerExtensions[i], output);
    }
  }

  static void AddFilesUnderRoot(string assetRoot, string extension, List<RuntimeMaterialAsset> output) {
    if (string.IsNullOrWhiteSpace(assetRoot) || string.IsNullOrWhiteSpace(extension) || output == null) return;

    var physicalRoot = ContentPackPipeline.GetPhysicalPath(assetRoot);
    if (!Directory.Exists(physicalRoot)) return;

    var files = Directory.GetFiles(physicalRoot, "*" + extension, SearchOption.AllDirectories);
    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < files.Length; i++) {
      AddUniqueMaterialIfExists(output, ContentPackPipeline.ToProjectAssetPath(files[i]));
    }
  }

  static void AddOwnerFilesUnderRoot(string assetRoot, string extension, List<string> output) {
    if (string.IsNullOrWhiteSpace(assetRoot) || string.IsNullOrWhiteSpace(extension) || output == null) return;

    var physicalRoot = ContentPackPipeline.GetPhysicalPath(assetRoot);
    if (!Directory.Exists(physicalRoot)) return;

    var files = Directory.GetFiles(physicalRoot, "*" + extension, SearchOption.AllDirectories);
    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < files.Length; i++) {
      AddUniqueAssetPathIfExists(output, ContentPackPipeline.ToProjectAssetPath(files[i]));
    }
  }

  static void AddRuntimeFallbackOwners(List<string> output) {
    AddUniqueAssetPathIfExists(output, GameplayCoreAssetPaths.EsperanzaPrefabAssetPath);

    foreach (var projectile in Projectiles.EnumerateAll()) {
      AddUniqueAssetPathIfExists(output, projectile.Value?.prefabAddress);
    }

    foreach (var location in LocationEnemyData.locations.Values) {
      AddUniqueAssetPathIfExists(output, location?.locationPrefabData != null ? location.locationPrefabData.AssetPath : "");
    }
  }

  static string ResolvePreferredActiveAssetPath(string assetPath) {
    var normalizedAssetPath = RuntimePrefabAddressables.NormalizeAssetPath(assetPath);
    if (IsExternalContentConfigured() &&
        !string.IsNullOrWhiteSpace(normalizedAssetPath) &&
        normalizedAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
      var relativePath = normalizedAssetPath.Substring("Assets/".Length);
      var activeStageRoots = CollectConfiguredActiveStageRoots();
      for (var i = 0; i < activeStageRoots.Count; i++) {
        var stagedAssetPath = RuntimePrefabAddressables.NormalizeAssetPath(
          activeStageRoots[i] + "/" + relativePath
        );
        if (File.Exists(ContentPackPipeline.GetPhysicalPath(stagedAssetPath))) {
          return stagedAssetPath;
        }
      }
    }

    var resolvedAssetPath = RuntimePrefabAddressables.NormalizeAssetPath(
      ActiveContentRegistryRuntime.ResolveActiveContentAssetPath(assetPath)
    );
    if (!string.IsNullOrWhiteSpace(resolvedAssetPath) &&
        File.Exists(ContentPackPipeline.GetPhysicalPath(resolvedAssetPath))) {
      return resolvedAssetPath;
    }

    return RuntimePrefabAddressables.NormalizeAssetPath(assetPath);
  }

  static void AddMaterialDependencies(List<string> ownerAssetPaths, List<RuntimeMaterialAsset> output) {
    if (ownerAssetPaths == null || ownerAssetPaths.Count <= 0 || output == null) return;

    var dependencies = AssetDatabase.GetDependencies(ownerAssetPaths.ToArray(), true);
    Array.Sort(dependencies, StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < dependencies.Length; i++) {
      var dependency = RuntimePrefabAddressables.NormalizeAssetPath(dependencies[i]);
      if (!string.Equals(Path.GetExtension(dependency), ".mat", StringComparison.OrdinalIgnoreCase)) continue;
      AddUniqueMaterialIfExists(output, dependency);
    }
  }

  public static bool TryResolveGuid(string assetPath, out string guid) {
    guid = "";
    var normalized = RuntimePrefabAddressables.NormalizeAssetPath(assetPath);
    if (string.IsNullOrWhiteSpace(normalized)) return false;

    if (normalized.StartsWith(ContentPackPipeline.StageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase) &&
        TryReadMetaGuid(normalized, out guid)) {
      return true;
    }

    guid = AssetDatabase.AssetPathToGUID(normalized);
    if (!string.IsNullOrWhiteSpace(guid)) return true;

    return TryReadMetaGuid(normalized, out guid);
  }

  static bool TryReadMetaGuid(string assetPath, out string guid) {
    guid = "";
    var normalized = RuntimePrefabAddressables.NormalizeAssetPath(assetPath);
    if (string.IsNullOrWhiteSpace(normalized)) return false;
    var metaPath = ContentPackPipeline.GetPhysicalPath(normalized + ".meta");
    if (!File.Exists(metaPath)) return false;

    var lines = File.ReadAllLines(metaPath);
    for (var i = 0; i < lines.Length; i++) {
      var line = lines[i]?.Trim();
      if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("guid:", StringComparison.OrdinalIgnoreCase)) continue;
      guid = line.Substring("guid:".Length).Trim();
      return !string.IsNullOrWhiteSpace(guid);
    }

    return false;
  }

  static void AddUniqueMaterialIfExists(List<RuntimeMaterialAsset> output, string assetPath) {
    if (output == null) return;
    var normalized = RuntimePrefabAddressables.NormalizeAssetPath(assetPath);
    if (string.IsNullOrWhiteSpace(normalized)) return;
    if (!string.Equals(Path.GetExtension(normalized), ".mat", StringComparison.OrdinalIgnoreCase)) return;
    if (!File.Exists(ContentPackPipeline.GetPhysicalPath(normalized))) return;
    if (!TryResolveGuid(normalized, out var guid)) return;

    for (var i = 0; i < output.Count; i++) {
      if (string.Equals(output[i]?.assetPath, normalized, StringComparison.OrdinalIgnoreCase)) return;
    }

    output.Add(new RuntimeMaterialAsset {
      assetPath = normalized,
      guid = guid
    });
  }

  static void AddUniqueAssetPathIfExists(List<string> output, string assetPath) {
    if (output == null) return;
    var normalized = RuntimePrefabAddressables.NormalizeAssetPath(assetPath);
    if (string.IsNullOrWhiteSpace(normalized)) return;
    if (!File.Exists(ContentPackPipeline.GetPhysicalPath(normalized))) return;
    if (!TryResolveGuid(normalized, out _)) return;
    AddUniquePath(output, normalized);
  }

  static void AddUniquePath(List<string> output, string assetPath) {
    if (output == null) return;
    var normalized = RuntimePrefabAddressables.NormalizeAssetPath(assetPath);
    if (string.IsNullOrWhiteSpace(normalized)) return;

    for (var i = 0; i < output.Count; i++) {
      if (string.Equals(output[i], normalized, StringComparison.OrdinalIgnoreCase)) return;
    }

    output.Add(normalized);
  }

  static bool HasStagedExternalMaterialFiles() {
    var physicalRoot = ContentPackPipeline.GetPhysicalPath(ContentPackPipeline.StageRootAssetPath);
    if (!Directory.Exists(physicalRoot)) return false;

    foreach (var _ in Directory.EnumerateFiles(physicalRoot, "*.mat", SearchOption.AllDirectories)) {
      return true;
    }

    return false;
  }

  static string FormatMaterialAssetPaths(List<RuntimeMaterialAsset> materialAssets) {
    if (materialAssets == null || materialAssets.Count <= 0) return "-";

    var paths = new List<string>(materialAssets.Count);
    for (var i = 0; i < materialAssets.Count; i++) {
      var path = RuntimePrefabAddressables.NormalizeAssetPath(materialAssets[i]?.assetPath);
      if (string.IsNullOrWhiteSpace(path)) continue;
      paths.Add(path);
    }

    return paths.Count <= 0 ? "-" : string.Join(" | ", paths);
  }

  static string FormatStageRootDiagnostics() {
    var physicalRoot = ContentPackPipeline.GetPhysicalPath(ContentPackPipeline.StageRootAssetPath);
    var exists = Directory.Exists(physicalRoot);
    var count = 0;
    var first = "";

    if (exists) {
      foreach (var file in Directory.EnumerateFiles(physicalRoot, "*.mat", SearchOption.AllDirectories)) {
        count++;
        if (string.IsNullOrWhiteSpace(first)) {
          first = ContentPackPipeline.ToProjectAssetPath(file);
        }
      }
    }

    return
      " stage_root='" + physicalRoot + "'" +
      " stage_exists=" + exists +
      " staged_mat_count=" + count +
      " first_staged_mat='" + (string.IsNullOrWhiteSpace(first) ? "-" : first) + "'";
  }

  static bool RemoveSourceMaterialEntriesWithStagedEquivalents(
    UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings,
    List<RuntimeMaterialAsset> materialAssets
  ) {
    if (settings == null || materialAssets == null || materialAssets.Count <= 0) return false;

    var stagedMaterialPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < materialAssets.Count; i++) {
      var assetPath = RuntimePrefabAddressables.NormalizeAssetPath(materialAssets[i]?.assetPath);
      if (IsStagedMaterialAssetPath(assetPath)) {
        stagedMaterialPaths.Add(assetPath);
      }
    }
    if (stagedMaterialPaths.Count <= 0) return false;

    var removeGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var groups = settings.groups;
    if (groups == null) return false;

    for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++) {
      var group = groups[groupIndex];
      if (group == null || group.entries == null) continue;

      foreach (var entry in group.entries) {
        var address = RuntimePrefabAddressables.NormalizeAssetPath(entry?.address);
        if (!IsSourceMaterialAssetPath(address)) continue;
        if (!HasStagedEquivalent(address, stagedMaterialPaths)) continue;
        removeGuids.Add(entry.guid);
      }
    }

    var changed = false;
    foreach (var guid in removeGuids) {
      if (settings.RemoveAssetEntry(guid, false)) {
        changed = true;
      }
    }

    if (changed) {
      EditorUtility.SetDirty(settings);
    }

    return changed;
  }

  static bool IsSourceMaterialAssetPath(string assetPath) {
    return !string.IsNullOrWhiteSpace(assetPath) &&
           assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(Path.GetExtension(assetPath), ".mat", StringComparison.OrdinalIgnoreCase);
  }

  static bool IsStagedMaterialAssetPath(string assetPath) {
    return !string.IsNullOrWhiteSpace(assetPath) &&
           assetPath.StartsWith(ContentPackPipeline.StageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(Path.GetExtension(assetPath), ".mat", StringComparison.OrdinalIgnoreCase);
  }

  static bool HasStagedEquivalent(string sourceMaterialPath, HashSet<string> stagedMaterialPaths) {
    if (!IsSourceMaterialAssetPath(sourceMaterialPath) || stagedMaterialPaths == null || stagedMaterialPaths.Count <= 0) {
      return false;
    }

    var relativePath = sourceMaterialPath.Substring("Assets/".Length);
    foreach (var stagedPath in stagedMaterialPaths) {
      if (stagedPath.EndsWith("/" + relativePath, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }
}
#endif
