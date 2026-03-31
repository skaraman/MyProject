#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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

public static class ContentPackPipeline {
  public const string DefaultExternalRoot = @"d:\localDev\Unity\MyProjectContent";
  public const string CorePackId = "Core";
  public const string SlicePackId = "Slice_DomeCity_Imp_Base";
  public const string HomebaseSlicePackId = "Slice_Homebase_Placeholder";
  public const string SunkenCaveSlicePackId = "Slice_SunkenCave_Placeholder";
  public const string EpisodePackId = "Episode_01";
  public const string LegacySlicePackId = "Slice_DomeCity_Imp";

  public const string SelectionAssetPath = "Assets/Editor/ContentPackSelection.asset";
  public const string ActiveRegistryAssetPath = "Assets/Resources/ActiveContentRegistry.asset";
  public const string StageRootAssetPath = "Assets/ContentStage";
  public const string StageCoreAssetPath = "Assets/ContentStage/Core";
  public const string StageSlicesAssetPath = "Assets/ContentStage/Slices";

  const string ManifestFileName = "ContentPackManifest.json";
  const string PackDataFolderName = "_PackData";
  const string EsperanzaSnapshotFileName = "esperanza_base_snapshot.json";
  const string DomeCityLocationSnapshotFileName = "location_DomeCity.json";
  const string DomeCityDialogSnapshotFileName = "dialog_DomeCity.json";

  static readonly Regex GuidRegex = new(@"guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);
  static readonly Regex LibraryNameRegex = new(@"^\s*libraryName:\s*(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
  static readonly Regex PortraitLibraryNameRegex = new(@"^\s*portraitLibraryName:\s*(.*?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
  static readonly Regex LocalIncludeRegex = new(@"^\s*#include(?:_with_pragmas)?\s+""([^""]+)""", RegexOptions.Compiled | RegexOptions.Multiline);

  static readonly HashSet<string> IgnoredDependencyExtensions = new(StringComparer.OrdinalIgnoreCase) {
    ".cs",
    ".asmdef",
    ".asmref",
    ".dll",
    ".rsp",
    ".mdb",
    ".pdb"
  };

  static readonly HashSet<string> TextRewriteExtensions = new(StringComparer.OrdinalIgnoreCase) {
    ".asset",
    ".anim",
    ".controller",
    ".guiskin",
    ".inputactions",
    ".json",
    ".mask",
    ".mat",
    ".meta",
    ".overridecontroller",
    ".playable",
    ".prefab",
    ".shadergraph",
    ".shadersubgraph",
    ".spritelib",
    ".txt",
    ".unity",
    ".uss",
    ".uxml"
  };

  static readonly HashSet<string> LocalIncludeExtensions = new(StringComparer.OrdinalIgnoreCase) {
    ".cginc",
    ".compute",
    ".hlsl",
    ".shader"
  };

  [Serializable]
  sealed class ContentPackManifestJson {
    public string packId;
    public string kind;
    public List<string> dependencies = new();
    public List<string> ownedRoots = new();
    public List<string> ownedLocations = new();
    public List<string> ownedEnemyTypes = new();
    public List<string> dialogIds = new();
    public List<string> warmProfiles = new();
    public string exportedFromProject;
    public string sourceRevision;
  }

  [Serializable]
  sealed class ExportedLocationJson {
    public string locationId;
    public string name;
    public List<string> enemies = new();
    public int maxEnemies;
    public float spawnInterval;
    public string prefabAssetPath;
    public Vector3 localPosition;
    public Vector3 localEulerAngles;
    public Vector3 localScale = Vector3.one;
    public List<ExportedLocationObjectiveJson> objectives = new();
  }

  [Serializable]
  sealed class ExportedLocationObjectiveJson {
    public int type;
    public string description;
    public int targetCount;
    public float targetSeconds;
  }

  [Serializable]
  sealed class ExportedDialogJson {
    public string locationId;
    public List<ExportedDialogSpeakerJson> speakers = new();
  }

  [Serializable]
  sealed class ExportedDialogSpeakerJson {
    public string speakerId;
    public string speakerName;
    public string portraitLibraryName;
    public int speakerSide;
    public List<ExportedDialogLineJson> lines = new();
  }

  [Serializable]
  sealed class ExportedDialogLineJson {
    public int lineNumber;
    public string text;
    public string emotion;
    public string trigger;
    public string speakerId;
    public string speakerName;
    public int speaker;
    public string avatarForm;
    public int otherType;
    public string portraitLibraryName;
    public string locationId;
  }

  [Serializable]
  sealed class ExportedEsperanzaSnapshotJson {
    public string generatedAtUtc;
    public List<ExportedSourceFileJson> sourceFiles = new();
  }

  [Serializable]
  sealed class ExportedSourceFileJson {
    public string assetPath;
    public string sha256;
    public string text;
  }

  sealed class PackDefinition {
    public string packId;
    public string kind;
    public string externalRootPath;
    public string stageAssetRoot;
    public bool stageForRuntime = true;
    public List<string> seedRoots = new();
    public List<string> manualLibraryNames = new();
    public List<string> assetDependencies = new();
    public List<string> requiredPackIds = new();
    public List<string> ownedRoots = new();
    public List<string> ownedLocations = new();
    public List<string> ownedEnemyTypes = new();
    public List<string> dialogIds = new();
    public List<string> warmProfiles = new();
    public string defaultLocationId = "";
    public string snapshotRelativePath = "";
    public string dialogSnapshotRelativePath = "";
  }

  sealed class AssignedAsset {
    public string assetPath;
    public string originalGuid;
    public string newGuid;
    public string packId;
    public string externalAssetPath;
    public string stageAssetPath;
  }

  [MenuItem("Tools/Content Packs/Advanced/Export First Pack Set")]
  public static void ExportFirstPackSetMenu() {
    ExportFirstPackSet(logResult: true);
  }

  [MenuItem("Tools/Content Packs/Advanced/Stage Active Packs")]
  public static void StageActivePacksMenu() {
    StageActivePacks(logResult: true);
  }

  [MenuItem("Tools/Content Packs/Advanced/Audit Active Packs")]
  public static void AuditActivePacksMenu() {
    AuditActivePacks(logResult: true);
  }

  [MenuItem("Tools/Content Packs/Advanced/Prepare Active Packs")]
  public static void StageAuditAndRebuildRuntimeIndexMenu() {
    StageAuditAndRebuildRuntimeIndex(logResult: true);
  }

  [MenuItem("Tools/Content Packs/Focus First Slice")]
  public static void FocusSelectionOnFirstSliceMenu() {
    FocusSelectionOnFirstSlice(logResult: true);
  }

  public static bool FocusSelectionOnFirstSlice(bool logResult) {
    var selection = LoadOrCreateSelectionAsset(logResult);
    if (selection == null) {
      return false;
    }

    var changed = selection.SetActivePackIds(new[] { SlicePackId });
    if (!changed) {
      if (logResult) {
        Debug.Log("[ContentPackPipeline] Content pack selection already focused on the first slice.");
      }
      return true;
    }

    EditorUtility.SetDirty(selection);
    AssetDatabase.SaveAssets();
    if (logResult) {
      Debug.Log(
        "[ContentPackPipeline] Focused content pack selection on the first slice." +
        " active_pack='" + SlicePackId + "'"
      );
    }
    return true;
  }

  public static bool PrepareSelectedPacksForRuntimeIndex(string contextLabel, bool logResult) {
    var selection = LoadOrCreateSelectionAsset(logResult);
    if (selection == null) {
      Debug.LogError("[ContentPackPipeline] Failed to load content pack selection asset.");
      return false;
    }

    if (!selection.ExternalContentEnabled) {
      WriteInactiveRegistryAsset(logResult);
      return true;
    }

    if (!RefreshExportedPackSetForStage(selection, "prepare_runtime_index:" + (contextLabel ?? ""), logResult)) {
      return false;
    }

    if (!EnsureSelectedPackDirectories(selection, contextLabel, logResult)) {
      return false;
    }

    return StageActivePacksInternal(selection, logResult, contextLabel);
  }

  public static bool ExportFirstPackSet(bool logResult) {
    var selection = LoadOrCreateSelectionAsset(logResult);
    if (selection == null) return false;

    var externalRoot = NormalizeFullPath(selection.ExternalRoot);
    Directory.CreateDirectory(externalRoot);

    var packDefinitions = BuildPackDefinitions(externalRoot);
    var projectLibraries = DiscoverProjectLibraryPaths();
    var errors = new List<string>();

    for (var i = 0; i < packDefinitions.Count; i++) {
      PreparePackDependencies(packDefinitions[i], projectLibraries, errors);
    }

    if (errors.Count > 0) {
      LogErrors("export_dependency_discovery", errors);
      return false;
    }

    var assignedAssets = AssignPackAssets(packDefinitions, errors);
    if (errors.Count > 0) {
      LogErrors("export_assignment", errors);
      return false;
    }

    try {
      for (var i = 0; i < packDefinitions.Count; i++) {
        RecreatePackDirectory(packDefinitions[i].externalRootPath, externalRoot);
      }

      WriteAssignedAssets(assignedAssets, errors);
      if (errors.Count > 0) {
        LogErrors("export_copy", errors);
        return false;
      }

      for (var i = 0; i < packDefinitions.Count; i++) {
        WriteGeneratedPackData(packDefinitions[i], errors);
      }

      if (errors.Count > 0) {
        LogErrors("export_pack_data", errors);
        return false;
      }

      for (var i = 0; i < packDefinitions.Count; i++) {
        WritePackManifest(packDefinitions[i], errors);
      }

      if (errors.Count > 0) {
        LogErrors("export_manifest", errors);
        return false;
      }

      if (logResult) {
        Debug.Log(
          "[ContentPackPipeline] Exported first external pack set." +
          " external_root='" + externalRoot + "'" +
          " pack_count=" + packDefinitions.Count +
          " asset_count=" + assignedAssets.Count
        );
      }

      return true;
    }
    catch (Exception ex) {
      Debug.LogError("[ContentPackPipeline] Export failed.\n" + ex);
      return false;
    }
  }

  public static bool StageActivePacks(bool logResult) {
    var selection = LoadOrCreateSelectionAsset(logResult);
    if (selection == null) return false;
    if (!RefreshExportedPackSetForStage(selection, "manual_stage", logResult)) {
      return false;
    }
    if (!EnsureSelectedPackDirectories(selection, "manual_stage", logResult)) {
      return false;
    }
    return StageActivePacksInternal(selection, logResult, "manual_stage");
  }

  public static bool StageAuditAndRebuildRuntimeIndex(bool logResult) {
    var selection = LoadOrCreateSelectionAsset(logResult);
    if (selection == null) return false;
    if (!RefreshExportedPackSetForStage(selection, "stage_audit_rebuild_runtime_index", logResult)) {
      return false;
    }
    if (!EnsureSelectedPackDirectories(selection, "stage_audit_rebuild_runtime_index", logResult)) {
      return false;
    }
    if (!StageActivePacksInternal(selection, logResult, "stage_audit_rebuild_runtime_index")) {
      return false;
    }
    if (!AuditActivePacks(logResult)) {
      return false;
    }
    return SpriteIndexBuilder.RebuildRuntimeIndex(logResult, failOnError: false);
  }

  static bool RefreshExportedPackSetForStage(ContentPackSelection selection, string contextLabel, bool logResult) {
    if (selection == null || !selection.ExternalContentEnabled) {
      return true;
    }

    if (logResult) {
      Debug.Log(
        "[ContentPackPipeline] Refreshing external pack exports before staging." +
        " context='" + (contextLabel ?? "") + "'" +
        " external_root='" + NormalizeFullPath(selection.ExternalRoot) + "'"
      );
    }

    var exportOk = ExportFirstPackSet(logResult);
    if (!exportOk) {
      return false;
    }

    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    return true;
  }

  public static bool AuditActivePacks(bool logResult) {
    var selection = LoadOrCreateSelectionAsset(logResult: false);
    if (selection == null) {
      Debug.LogError("[ContentPackPipeline] Audit failed: selection asset is not loadable.");
      return false;
    }

    var externalRoot = NormalizeFullPath(selection.ExternalRoot);
    var packDefinitions = BuildPackDefinitions(externalRoot);
    var packById = packDefinitions.ToDictionary(pack => pack.packId, StringComparer.OrdinalIgnoreCase);
    var selectedPackIds = selection.GetNormalizedActivePackIds();
    var activePackIds = ResolveConcreteActivePackIds(selectedPackIds, packById);
    var stageErrors = CollectStageValidationErrors(packById, activePackIds);
    var packPolicyErrors = CollectActivePackPolicyValidationErrors(packById, activePackIds);
    var gameplayCoreErrors = CollectGameplayCoreValidationErrors(selection, activePackIds);
    var generatedReferenceCount = CountLegacyGeneratedReferences();

    var summary = new StringBuilder();
    summary.Append("[ContentPackPipeline] Audit summary");
    summary.Append(" external_enabled=").Append(selection.ExternalContentEnabled);
    summary.Append(" selected_packs=").Append(selectedPackIds.Count <= 0 ? "-" : string.Join(", ", selectedPackIds));
    summary.Append(" active_packs=").Append(activePackIds.Count <= 0 ? "-" : string.Join(", ", activePackIds));
    summary.Append(" external_root='").Append(externalRoot).Append("'");
    summary.Append(" registry_present=").Append(AssetDatabase.LoadAssetAtPath<ActiveContentRegistry>(ActiveRegistryAssetPath) != null);
    summary.Append(" generated_refs=").Append(generatedReferenceCount);
    summary.Append(" stage_errors=").Append(stageErrors.Count);
    summary.Append(" pack_policy_errors=").Append(packPolicyErrors.Count);
    summary.Append(" gameplay_core_errors=").Append(gameplayCoreErrors.Count);
    Debug.Log(summary.ToString());

    for (var i = 0; i < selectedPackIds.Count; i++) {
      var packId = selectedPackIds[i];
      if (!packById.TryGetValue(packId, out var pack) || pack == null) {
        Debug.LogError("[ContentPackPipeline] Audit missing pack definition. pack_id='" + packId + "'");
        continue;
      }

      Debug.Log(
        "[ContentPackPipeline] Audit pack" +
        " pack_id='" + pack.packId + "'" +
        " external_exists=" + Directory.Exists(pack.externalRootPath) +
        " manifest_exists=" + File.Exists(Path.Combine(pack.externalRootPath, ManifestFileName)) +
        " stage_exists=" + Directory.Exists(Path.GetFullPath(pack.stageAssetRoot)) +
        " stage_root='" + pack.stageAssetRoot + "'"
      );
    }

    if (stageErrors.Count > 0) {
      LogErrors("audit_stage_validation", stageErrors);
    }

    if (packPolicyErrors.Count > 0) {
      LogErrors("audit_pack_policy_validation", packPolicyErrors);
    }

    if (gameplayCoreErrors.Count > 0) {
      LogErrors("audit_gameplay_core_validation", gameplayCoreErrors);
    }

    if (generatedReferenceCount > 0 && logResult) {
      Debug.LogWarning("[ContentPackPipeline] Audit found legacy Assets/Generated references. count=" + generatedReferenceCount);
    }

    return stageErrors.Count <= 0 && packPolicyErrors.Count <= 0 && gameplayCoreErrors.Count <= 0;
  }

  public static IReadOnlyList<string> GetSpriteLibrarySearchRoots() {
    var roots = new List<string>();
    AppendStageSearchRoots(roots, includeSpriteLibraries: true, includeTextures: false);
    AddUniquePath(roots, SpriteStreamingConfig.SourceRootFolder);
    return roots;
  }

  public static IReadOnlyList<string> GetTextureSearchRoots() {
    var roots = new List<string>();
    AppendStageSearchRoots(roots, includeSpriteLibraries: false, includeTextures: true);
    AddUniquePath(roots, SpriteStreamingConfig.TextureSourceRootFolder);
    return roots;
  }

  static bool StageActivePacksInternal(ContentPackSelection selection, bool logResult, string contextLabel) {
    if (selection == null) return false;

    if (!selection.ExternalContentEnabled) {
      WriteInactiveRegistryAsset(logResult);
      return true;
    }

    var externalRoot = NormalizeFullPath(selection.ExternalRoot);
    if (!Directory.Exists(externalRoot)) {
      Debug.LogError("[ContentPackPipeline] External content root does not exist. root='" + externalRoot + "'");
      return false;
    }

    var packDefinitions = BuildPackDefinitions(externalRoot);
    var packById = packDefinitions.ToDictionary(pack => pack.packId, StringComparer.OrdinalIgnoreCase);
    var selectedPackIds = selection.GetNormalizedActivePackIds();
    var activePackIds = ResolveConcreteActivePackIds(selectedPackIds, packById);
    if (activePackIds.Count <= 0) {
      WriteInactiveRegistryAsset(logResult);
      return true;
    }

    for (var i = 0; i < selectedPackIds.Count; i++) {
      if (!packById.TryGetValue(selectedPackIds[i], out var pack)) {
        Debug.LogError("[ContentPackPipeline] Unknown selected pack id '" + selectedPackIds[i] + "'.");
        return false;
      }
    }

    for (var i = 0; i < selectedPackIds.Count; i++) {
      var pack = packById[selectedPackIds[i]];
      if (!Directory.Exists(pack.externalRootPath)) {
        Debug.LogError(
          "[ContentPackPipeline] Selected pack directory is missing." +
          " pack_id='" + pack.packId + "'" +
          " path='" + pack.externalRootPath + "'"
        );
        return false;
      }
    }

    try {
      var stageLinkChanges = 0;
      var reusedStageLinks = 0;

      EnsureDirectoryAssetPath(StageRootAssetPath);
      EnsureDirectoryAssetPath(StageSlicesAssetPath);
      stageLinkChanges += RemoveInactiveStageLinks(activePackIds);

      for (var i = 0; i < activePackIds.Count; i++) {
        var pack = packById[activePackIds[i]];
        if (EnsureStageLink(pack.stageAssetRoot, pack.externalRootPath, out var reused)) {
          stageLinkChanges++;
          continue;
        }

        if (reused) {
          reusedStageLinks++;
        }
      }

      if (stageLinkChanges > 0) {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
      }

      if (!GenerateActiveRegistryAsset(packById, activePackIds, logResult)) {
        return false;
      }

      var syncedLocationPrefabs = LocationWarmProfileBootstrap.SyncLocationPrefabAddressables(logResult: false, saveAndRefresh: false);
      var syncedPlayerPrefab = GameplayPlayerAddressablesBootstrap.SyncGameplayPlayerAddressables(logResult: false, saveAndRefresh: false);
      var syncedProjectilePrefabs = ProjectileAddressablesBootstrap.SyncProjectileAddressables(logResult: false, saveAndRefresh: false);

      AssetDatabase.SaveAssets();
      ActiveContentRegistryRuntime.ForceReload();
      SpriteIndexBuilder.ClearCachedSpriteSliceEstimates("content_pack_stage:" + contextLabel);

      if (!ValidateStagedContent(selection, packById, activePackIds)) {
        return false;
      }

      if (logResult) {
        Debug.Log(
          "[ContentPackPipeline] Staged active external packs." +
          " context='" + contextLabel + "'" +
          " selected_packs=" + string.Join(", ", selectedPackIds) +
          " active_packs=" + string.Join(", ", activePackIds) +
          " stage_link_changes=" + stageLinkChanges +
          " stage_link_reused=" + reusedStageLinks +
          " location_prefab_sync=" + (syncedLocationPrefabs ? 1 : 0) +
          " player_prefab_sync=" + (syncedPlayerPrefab ? 1 : 0) +
          " projectile_prefab_sync=" + (syncedProjectilePrefabs ? 1 : 0) +
          " stage_root='" + StageRootAssetPath + "'"
        );
      }

      return true;
    }
    catch (Exception ex) {
      Debug.LogError("[ContentPackPipeline] Stage failed.\n" + ex);
      return false;
    }
  }

  static ContentPackSelection LoadOrCreateSelectionAsset(bool logResult) {
    var asset = AssetDatabase.LoadAssetAtPath<ContentPackSelection>(SelectionAssetPath);
    if (asset != null) {
      if (asset.EnsureDefaults()) {
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
      }
      return asset;
    }

    if (!RepairBrokenSelectionAsset(logResult)) {
      return null;
    }

    EnsureDirectoryAssetPath("Assets/Editor");
    asset = ScriptableObject.CreateInstance<ContentPackSelection>();
    asset.EnsureDefaults();
    AssetDatabase.CreateAsset(asset, SelectionAssetPath);
    AssetDatabase.SaveAssets();

    if (logResult) {
      Debug.Log("[ContentPackPipeline] Created content pack selection asset at '" + SelectionAssetPath + "'.");
    }

    return asset;
  }

  static bool RepairBrokenSelectionAsset(bool logResult) {
    if (!File.Exists(NormalizeFullPath(SelectionAssetPath))) return true;

    var deleted = AssetDatabase.DeleteAsset(SelectionAssetPath);
    if (!deleted) {
      Debug.LogError("[ContentPackPipeline] Failed to repair broken content pack selection asset at '" + SelectionAssetPath + "'.");
      return false;
    }

    if (logResult) {
      Debug.LogWarning("[ContentPackPipeline] Repaired broken content pack selection asset at '" + SelectionAssetPath + "'.");
    }

    return true;
  }

  static bool DoActivePackDirectoriesExist(ContentPackSelection selection) {
    if (selection == null || !selection.ExternalContentEnabled) return true;

    var externalRoot = NormalizeFullPath(selection.ExternalRoot);
    if (!Directory.Exists(externalRoot)) return false;

    var packDefinitions = BuildPackDefinitions(externalRoot);
    var packById = packDefinitions.ToDictionary(pack => pack.packId, StringComparer.OrdinalIgnoreCase);
    var activePackIds = selection.GetNormalizedActivePackIds();

    for (var i = 0; i < activePackIds.Count; i++) {
      if (!packById.TryGetValue(activePackIds[i], out var pack)) return false;
      if (!Directory.Exists(pack.externalRootPath)) return false;
      if (!File.Exists(Path.Combine(pack.externalRootPath, ManifestFileName))) return false;
    }

    return true;
  }

  static bool EnsureSelectedPackDirectories(ContentPackSelection selection, string contextLabel, bool logResult) {
    if (selection == null || !selection.ExternalContentEnabled) {
      return true;
    }

    if (DoActivePackDirectoriesExist(selection)) {
      return true;
    }

    LogMissingSelectedPackDirectories(selection, contextLabel);
    Debug.LogWarning(
      "[ContentPackPipeline] Missing external pack directories for active selection. Exporting the first pack set before staging." +
      " context='" + contextLabel + "'"
    );
    return ExportFirstPackSet(logResult);
  }

  static void LogMissingSelectedPackDirectories(ContentPackSelection selection, string contextLabel) {
    if (selection == null || !selection.ExternalContentEnabled) {
      return;
    }

    var externalRoot = NormalizeFullPath(selection.ExternalRoot);
    if (!Directory.Exists(externalRoot)) {
      Debug.LogWarning(
        "[ContentPackPipeline] External content root is missing before stage preparation." +
        " context='" + contextLabel + "'" +
        " root='" + externalRoot + "'"
      );
      return;
    }

    var packDefinitions = BuildPackDefinitions(externalRoot);
    var packById = packDefinitions.ToDictionary(pack => pack.packId, StringComparer.OrdinalIgnoreCase);
    var selectedPackIds = selection.GetNormalizedActivePackIds();
    for (var i = 0; i < selectedPackIds.Count; i++) {
      if (!packById.TryGetValue(selectedPackIds[i], out var pack) || pack == null) {
        continue;
      }

      if (Directory.Exists(pack.externalRootPath) && File.Exists(Path.Combine(pack.externalRootPath, ManifestFileName))) {
        continue;
      }

      var legacyPath = ResolveLegacyExternalPackPath(externalRoot, pack.packId);
      Debug.LogWarning(
        "[ContentPackPipeline] Active pack export is missing." +
        " context='" + contextLabel + "'" +
        " pack_id='" + pack.packId + "'" +
        " expected_path='" + pack.externalRootPath + "'" +
        " legacy_path='" + legacyPath + "'"
      );
    }
  }

  static string ResolveLegacyExternalPackPath(string externalRoot, string packId) {
    if (!string.Equals(packId, SlicePackId, StringComparison.OrdinalIgnoreCase)) {
      return "-";
    }

    var normalizedRoot = NormalizeFullPath(externalRoot);
    var legacyPath = NormalizeFullPath(Path.Combine(normalizedRoot, "Slices", LegacySlicePackId));
    return Directory.Exists(legacyPath) ? legacyPath : "-";
  }

  static List<PackDefinition> BuildPackDefinitions(string externalRoot) {
    var normalizedRoot = NormalizeFullPath(externalRoot);

    var core = new PackDefinition {
      packId = CorePackId,
      kind = "core",
      externalRootPath = NormalizeFullPath(Path.Combine(normalizedRoot, "Core")),
      stageAssetRoot = StageCoreAssetPath,
      defaultLocationId = "",
    };
    core.seedRoots.Add("Assets/Prefabs/Characters/ESPER.prefab");
    core.seedRoots.Add("Assets/Sprites/Characters/Esperanza/GroupedGearAtlases/Base");
    core.seedRoots.Add("Assets/Sprites/Characters/Esperanza/GroupedGearAtlases/Skin");
    core.seedRoots.Add("Assets/Sprites/Characters/Esperanza/Expressions/Base");
    core.seedRoots.Add("Assets/Sprites/Characters/Esperanza/Effects");
    core.seedRoots.Add("Assets/Sprites/Characters/Esperanza/_Bounces");
    AddCoreUiOwnedRoots(core.seedRoots);
    foreach (var projectile in Projectiles.EnumerateAll()) {
      var projectilePrefabPath = NormalizeAssetPath(projectile.Value?.prefabAddress);
      if (string.IsNullOrWhiteSpace(projectilePrefabPath)) continue;
      AddUniquePath(core.seedRoots, projectilePrefabPath);
    }
    core.manualLibraryNames.Add("Dialog/DialogEsper");
    core.ownedRoots.AddRange(core.seedRoots);

    var slice = new PackDefinition {
      packId = SlicePackId,
      kind = "slice",
      externalRootPath = NormalizeFullPath(Path.Combine(normalizedRoot, "Slices", SlicePackId)),
      stageAssetRoot = StageSlicesAssetPath + "/" + SlicePackId,
      defaultLocationId = LocationEnemyData.DomeCityLocationId,
      snapshotRelativePath = PackDataFolderName + "/" + DomeCityLocationSnapshotFileName,
      dialogSnapshotRelativePath = PackDataFolderName + "/" + DomeCityDialogSnapshotFileName
    };
    slice.requiredPackIds.Add(CorePackId);
    slice.seedRoots.Add("Assets/Prefabs/Locations/DomeCity.prefab");
    slice.seedRoots.Add("Assets/Prefabs/Enemies/Imp.prefab");
    slice.seedRoots.Add("Assets/Sprites/Characters/Enemies/Imp");
    slice.seedRoots.Add("Assets/Resources/LocationWarmProfile_DomeCity.asset");
    slice.manualLibraryNames.Add("Dialog/DialogImp");
    slice.ownedRoots.AddRange(slice.seedRoots);
    slice.ownedLocations.Add(LocationEnemyData.DomeCityLocationId);
    slice.ownedEnemyTypes.Add("Imp");
    slice.dialogIds.Add(LocationEnemyData.DomeCityLocationId);
    slice.warmProfiles.Add("LocationWarmProfile_DomeCity");

    var homebase = new PackDefinition {
      packId = HomebaseSlicePackId,
      kind = "slice",
      externalRootPath = NormalizeFullPath(Path.Combine(normalizedRoot, "Slices", HomebaseSlicePackId)),
      stageAssetRoot = StageSlicesAssetPath + "/" + HomebaseSlicePackId,
      stageForRuntime = false
    };
    homebase.requiredPackIds.Add(CorePackId);

    var sunkenCave = new PackDefinition {
      packId = SunkenCaveSlicePackId,
      kind = "slice",
      externalRootPath = NormalizeFullPath(Path.Combine(normalizedRoot, "Slices", SunkenCaveSlicePackId)),
      stageAssetRoot = StageSlicesAssetPath + "/" + SunkenCaveSlicePackId,
      stageForRuntime = false
    };
    sunkenCave.requiredPackIds.Add(CorePackId);

    var episode = new PackDefinition {
      packId = EpisodePackId,
      kind = "episode",
      externalRootPath = NormalizeFullPath(Path.Combine(normalizedRoot, "Episodes", EpisodePackId)),
      stageAssetRoot = StageRootAssetPath + "/Episodes/" + EpisodePackId,
      stageForRuntime = false,
      defaultLocationId = LocationEnemyData.DomeCityLocationId,
    };
    episode.requiredPackIds.Add(SlicePackId);
    episode.requiredPackIds.Add(HomebaseSlicePackId);
    episode.requiredPackIds.Add(SunkenCaveSlicePackId);

    return new List<PackDefinition> { core, slice, homebase, sunkenCave, episode };
  }

  static void AddCoreUiOwnedRoots(List<string> output) {
    if (output == null) return;

    AddUniquePath(output, "Assets/Sprites/Fonts");
    AddUniquePath(output, "Assets/Sprites/GameInterface");
  }

  static void PreparePackDependencies(
    PackDefinition pack,
    Dictionary<string, string> projectLibraries,
    List<string> errors
  ) {
    if (pack == null) return;

    var seedAssetPaths = ExpandProjectRoots(pack.seedRoots, errors);
    var libraryNames = CollectReferencedLibraryNamesFromAssets(seedAssetPaths);
    for (var i = 0; i < pack.manualLibraryNames.Count; i++) {
      AddUniqueLibraryName(libraryNames, pack.manualLibraryNames[i]);
    }

    var libraryAssetPaths = ResolveLibraryAssetPaths(libraryNames, projectLibraries, errors);
    var allSeedPaths = new List<string>(seedAssetPaths.Count + libraryAssetPaths.Count);
    allSeedPaths.AddRange(seedAssetPaths);
    for (var i = 0; i < libraryAssetPaths.Count; i++) {
      AddUniquePath(allSeedPaths, libraryAssetPaths[i]);
    }

    pack.assetDependencies = CollectPackDependencies(allSeedPaths, errors);
  }

  static Dictionary<string, AssignedAsset> AssignPackAssets(List<PackDefinition> packDefinitions, List<string> errors) {
    var usageByAssetPath = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    var packById = packDefinitions.ToDictionary(pack => pack.packId, StringComparer.OrdinalIgnoreCase);

    foreach (var pack in packDefinitions) {
      if (pack == null || pack.assetDependencies == null) continue;
      for (var i = 0; i < pack.assetDependencies.Count; i++) {
        var assetPath = NormalizeAssetPath(pack.assetDependencies[i]);
        if (string.IsNullOrWhiteSpace(assetPath)) continue;
        if (!usageByAssetPath.TryGetValue(assetPath, out var usage)) {
          usage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
          usageByAssetPath[assetPath] = usage;
        }
        usage.Add(pack.packId);
      }
    }

    var result = new Dictionary<string, AssignedAsset>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in usageByAssetPath) {
      var assetPath = pair.Key;
      if (assetPath.StartsWith("Assets/Generated/", StringComparison.OrdinalIgnoreCase)) {
        errors.Add("Generated assets are not allowed in external packs. asset='" + assetPath + "'");
        continue;
      }

      var assignedPackId = ResolveAssignedPackId(pair.Value);
      if (!packById.TryGetValue(assignedPackId, out var pack)) {
        errors.Add("Failed to resolve assigned pack for asset '" + assetPath + "'.");
        continue;
      }

      var originalGuid = AssetDatabase.AssetPathToGUID(assetPath);
      if (string.IsNullOrWhiteSpace(originalGuid)) {
        errors.Add("Asset is missing a GUID and cannot be exported. asset='" + assetPath + "'");
        continue;
      }

      var relativePath = assetPath.Substring("Assets/".Length);
      result[assetPath] = new AssignedAsset {
        assetPath = assetPath,
        originalGuid = originalGuid,
        newGuid = ComputeDeterministicExportGuid(assignedPackId, assetPath),
        packId = assignedPackId,
        externalAssetPath = NormalizeFullPath(Path.Combine(pack.externalRootPath, relativePath)),
        stageAssetPath = NormalizeAssetPath(pack.stageAssetRoot + "/" + relativePath)
      };
    }

    return result;
  }

  static string ResolveAssignedPackId(HashSet<string> usage) {
    if (usage == null || usage.Count <= 0) return CorePackId;
    if (usage.Contains(CorePackId) || usage.Count > 1) return CorePackId;
    foreach (var packId in usage) return packId;
    return CorePackId;
  }

  static void WriteAssignedAssets(Dictionary<string, AssignedAsset> assignedAssets, List<string> errors) {
    var guidMap = BuildGuidMap(assignedAssets);
    var orderedAssets = assignedAssets.Values.OrderBy(asset => asset.externalAssetPath, StringComparer.OrdinalIgnoreCase).ToList();

    for (var i = 0; i < orderedAssets.Count; i++) {
      var assigned = orderedAssets[i];
      try {
        CopyAssetPayload(assigned.assetPath, assigned.externalAssetPath, guidMap);
        CopyMetaPayload(assigned.assetPath, assigned.externalAssetPath, guidMap);
      }
      catch (Exception ex) {
        errors.Add(
          "Failed to copy asset." +
          " asset='" + assigned.assetPath + "'" +
          " target='" + assigned.externalAssetPath + "'" +
          " error='" + ex.Message + "'"
        );
      }
    }
  }

  static Dictionary<string, string> BuildGuidMap(Dictionary<string, AssignedAsset> assignedAssets) {
    var guidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var assigned in assignedAssets.Values) {
      guidMap[assigned.originalGuid] = assigned.newGuid;
    }
    return guidMap;
  }

  static string ComputeDeterministicExportGuid(string packId, string assetPath) {
    var normalizedPackId = NormalizeToken(packId);
    var normalizedAssetPath = NormalizeAssetPath(assetPath);
    var seed = normalizedPackId + "|" + normalizedAssetPath;
    using var sha = SHA256.Create();
    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
    var builder = new StringBuilder(32);
    for (var i = 0; i < 16; i++) {
      builder.Append(hash[i].ToString("x2"));
    }

    return builder.ToString();
  }

  static void CopyAssetPayload(string sourceAssetPath, string targetFullPath, Dictionary<string, string> guidMap) {
    var sourceFullPath = Path.GetFullPath(sourceAssetPath);
    EnsureDirectoryFullPath(Path.GetDirectoryName(targetFullPath));

    if (ShouldRewriteTextFile(sourceFullPath)) {
      var text = File.ReadAllText(sourceFullPath);
      File.WriteAllText(targetFullPath, RewriteGuids(text, guidMap), new UTF8Encoding(false));
      return;
    }

    File.Copy(sourceFullPath, targetFullPath, overwrite: true);
  }

  static void CopyMetaPayload(string sourceAssetPath, string targetFullPath, Dictionary<string, string> guidMap) {
    var sourceMetaFullPath = Path.GetFullPath(sourceAssetPath + ".meta");
    if (!File.Exists(sourceMetaFullPath)) {
      throw new FileNotFoundException("Missing meta file.", sourceMetaFullPath);
    }

    var targetMetaFullPath = targetFullPath + ".meta";
    var metaText = File.ReadAllText(sourceMetaFullPath);
    File.WriteAllText(targetMetaFullPath, RewriteGuids(metaText, guidMap), new UTF8Encoding(false));
  }

  static string RewriteGuids(string text, Dictionary<string, string> guidMap) {
    if (string.IsNullOrWhiteSpace(text) || guidMap == null || guidMap.Count <= 0) {
      return text ?? "";
    }

    return GuidRegex.Replace(text, match => {
      var originalGuid = match.Groups[1].Value;
      if (!guidMap.TryGetValue(originalGuid, out var newGuid)) {
        return match.Value;
      }

      return match.Value.Replace(originalGuid, newGuid);
    });
  }

  static void WriteGeneratedPackData(PackDefinition pack, List<string> errors) {
    if (pack == null) return;

    try {
      if (string.Equals(pack.packId, CorePackId, StringComparison.OrdinalIgnoreCase)) {
        WriteEsperanzaSnapshot(pack);
        return;
      }

      if (string.Equals(pack.packId, SlicePackId, StringComparison.OrdinalIgnoreCase)) {
        WriteDomeCitySnapshots(pack);
      }
    }
    catch (Exception ex) {
      errors.Add(
        "Failed to write generated pack data." +
        " pack_id='" + pack.packId + "'" +
        " error='" + ex.Message + "'"
      );
    }
  }

  static void WritePackManifest(PackDefinition pack, List<string> errors) {
    if (pack == null) return;

    try {
      var manifest = new ContentPackManifestJson {
        packId = pack.packId,
        kind = pack.kind,
        dependencies = new List<string>(pack.requiredPackIds),
        ownedRoots = new List<string>(pack.ownedRoots),
        ownedLocations = new List<string>(pack.ownedLocations),
        ownedEnemyTypes = new List<string>(pack.ownedEnemyTypes),
        dialogIds = new List<string>(pack.dialogIds),
        warmProfiles = new List<string>(pack.warmProfiles),
        exportedFromProject = new DirectoryInfo(GetProjectRoot()).Name,
        sourceRevision = TryGetGitRevision()
      };

      var manifestPath = Path.Combine(pack.externalRootPath, ManifestFileName);
      WriteJson(manifestPath, manifest);
    }
    catch (Exception ex) {
      errors.Add(
        "Failed to write pack manifest." +
        " pack_id='" + pack.packId + "'" +
        " error='" + ex.Message + "'"
      );
    }
  }

  static void WriteEsperanzaSnapshot(PackDefinition pack) {
    var snapshot = new ExportedEsperanzaSnapshotJson {
      generatedAtUtc = DateTime.UtcNow.ToString("O")
    };

    var sourcePaths = new List<string>();
    sourcePaths.Add("Assets/Scripts/Data/Stats.cs");
    var esperanzaDataDirectory = "Assets/Scripts/Data/Esperanza";
    if (Directory.Exists(Path.GetFullPath(esperanzaDataDirectory))) {
      var files = Directory.GetFiles(Path.GetFullPath(esperanzaDataDirectory), "*.cs", SearchOption.TopDirectoryOnly);
      Array.Sort(files, StringComparer.OrdinalIgnoreCase);
      for (var i = 0; i < files.Length; i++) {
        sourcePaths.Add(ToProjectAssetPath(files[i]));
      }
    }

    for (var i = 0; i < sourcePaths.Count; i++) {
      var assetPath = NormalizeAssetPath(sourcePaths[i]);
      if (string.IsNullOrWhiteSpace(assetPath)) continue;
      var fullPath = Path.GetFullPath(assetPath);
      if (!File.Exists(fullPath)) continue;

      var text = File.ReadAllText(fullPath);
      snapshot.sourceFiles.Add(new ExportedSourceFileJson {
        assetPath = assetPath,
        sha256 = ComputeSha256(text),
        text = text
      });
    }

    var outputPath = Path.Combine(pack.externalRootPath, PackDataFolderName, EsperanzaSnapshotFileName);
    WriteJson(outputPath, snapshot);
  }

  static void WriteDomeCitySnapshots(PackDefinition pack) {
    if (!LocationEnemyData.TryGetBuiltInLocation(LocationEnemyData.DomeCityLocationId, out var locationInfo) || locationInfo == null) {
      throw new InvalidOperationException("Built-in DomeCity location data was not found.");
    }

    if (!DialogData.TryGetBuiltInLocation(LocationEnemyData.DomeCityLocationId, out var dialogInfo) || dialogInfo == null) {
      throw new InvalidOperationException("Built-in DomeCity dialog data was not found.");
    }

    var locationSnapshot = new ExportedLocationJson {
      locationId = LocationEnemyData.NormalizeLocationId(locationInfo.id),
      name = locationInfo.name ?? "",
      enemies = locationInfo.enemies != null ? new List<string>(locationInfo.enemies) : new List<string>(),
      maxEnemies = locationInfo.maxEnemies,
      spawnInterval = locationInfo.spawnInterval,
      prefabAssetPath = BuildStageAssetPath(pack, "Assets/Prefabs/Locations/DomeCity.prefab"),
      localPosition = locationInfo.locationPrefabData != null ? locationInfo.locationPrefabData.localPosition : Vector3.zero,
      localEulerAngles = locationInfo.locationPrefabData != null ? locationInfo.locationPrefabData.localEulerAngles : Vector3.zero,
      localScale = locationInfo.locationPrefabData != null ? locationInfo.locationPrefabData.localScale : Vector3.one
    };

    if (locationInfo.objectives != null) {
      for (var i = 0; i < locationInfo.objectives.Count; i++) {
        var objective = locationInfo.objectives[i];
        if (objective == null) continue;
        locationSnapshot.objectives.Add(new ExportedLocationObjectiveJson {
          type = (int)objective.type,
          description = objective.description ?? "",
          targetCount = objective.targetCount,
          targetSeconds = objective.targetSeconds
        });
      }
    }

    var dialogSnapshot = new ExportedDialogJson {
      locationId = dialogInfo.locationId ?? ""
    };

    if (dialogInfo.speakers != null) {
      for (var i = 0; i < dialogInfo.speakers.Count; i++) {
        var speaker = dialogInfo.speakers[i];
        if (speaker == null) continue;

        var exportedSpeaker = new ExportedDialogSpeakerJson {
          speakerId = speaker.speakerId ?? "",
          speakerName = speaker.speakerName ?? "",
          portraitLibraryName = speaker.portraitLibraryName ?? "",
          speakerSide = (int)speaker.speakerSide
        };

        if (speaker.lines != null) {
          for (var lineIndex = 0; lineIndex < speaker.lines.Count; lineIndex++) {
            var line = speaker.lines[lineIndex];
            if (line == null) continue;
            exportedSpeaker.lines.Add(new ExportedDialogLineJson {
              lineNumber = line.lineNumber,
              text = line.text ?? "",
              emotion = line.emotion ?? "",
              trigger = line.trigger ?? "",
              speakerId = line.speakerId ?? "",
              speakerName = line.speakerName ?? "",
              speaker = (int)line.speaker,
              avatarForm = line.avatarForm ?? "",
              otherType = (int)line.otherType,
              portraitLibraryName = line.portraitLibraryName ?? "",
              locationId = line.locationId ?? ""
            });
          }
        }

        dialogSnapshot.speakers.Add(exportedSpeaker);
      }
    }

    WriteJson(Path.Combine(pack.externalRootPath, pack.snapshotRelativePath), locationSnapshot);
    WriteJson(Path.Combine(pack.externalRootPath, pack.dialogSnapshotRelativePath), dialogSnapshot);
  }

  static bool GenerateActiveRegistryAsset(
    Dictionary<string, PackDefinition> packById,
    List<string> activePackIds,
    bool logResult
  ) {
    var registry = AssetDatabase.LoadAssetAtPath<ActiveContentRegistry>(ActiveRegistryAssetPath);
    if (registry == null) {
      EnsureDirectoryAssetPath("Assets/Resources");
      registry = ScriptableObject.CreateInstance<ActiveContentRegistry>();
      AssetDatabase.CreateAsset(registry, ActiveRegistryAssetPath);
    }

    var stagedTextureRoots = new List<string>();
    var stagedSpriteLibraryRoots = new List<string>();
    var coreContentRoots = new List<string>();
    var locations = new List<LocationInfo>();
    var dialogs = new List<LocationDialogDefinition>();
    var warmProfiles = new List<LocationWarmRegistryEntry>();
    var defaultLocationId = "";

    for (var i = 0; i < activePackIds.Count; i++) {
      var packId = activePackIds[i];
      if (!packById.TryGetValue(packId, out var pack) || pack == null) continue;

      var stagedSpritesRoot = NormalizeAssetPath(pack.stageAssetRoot + "/Sprites");
      if (Directory.Exists(Path.GetFullPath(stagedSpritesRoot))) {
        AddUniquePath(stagedTextureRoots, stagedSpritesRoot);
      }

      var stagedSpriteLibraryRoot = NormalizeAssetPath(pack.stageAssetRoot + "/Sprites/SpriteLibraries");
      if (Directory.Exists(Path.GetFullPath(stagedSpriteLibraryRoot))) {
        AddUniquePath(stagedSpriteLibraryRoots, stagedSpriteLibraryRoot);
      }

      if (string.Equals(pack.packId, CorePackId, StringComparison.OrdinalIgnoreCase)) {
        for (var rootIndex = 0; rootIndex < pack.ownedRoots.Count; rootIndex++) {
          AddUniquePath(coreContentRoots, BuildStageAssetPath(pack, pack.ownedRoots[rootIndex]));
        }
        continue;
      }

      if (!string.IsNullOrWhiteSpace(pack.defaultLocationId) && string.IsNullOrWhiteSpace(defaultLocationId)) {
        defaultLocationId = pack.defaultLocationId;
      }

      if (TryReadLocationSnapshot(pack, out var locationSnapshot) && locationSnapshot != null) {
        locations.Add(locationSnapshot);
      }

      if (TryReadDialogSnapshot(pack, out var dialogSnapshot) && dialogSnapshot != null) {
        dialogs.Add(dialogSnapshot);
      }

      TryAddWarmProfiles(pack, locationSnapshot, warmProfiles);
    }

    registry.Configure(
      externalContentActive: activePackIds.Count > 0,
      defaultLocationId: defaultLocationId,
      activePackIds: activePackIds,
      stagedTextureRoots: stagedTextureRoots,
      stagedSpriteLibraryRoots: stagedSpriteLibraryRoots,
      coreContentRoots: coreContentRoots,
      locations: locations,
      dialogs: dialogs,
      warmProfiles: warmProfiles
    );

    EditorUtility.SetDirty(registry);
    ActiveContentRegistryRuntime.ForceReload();

    if (logResult) {
      Debug.Log(
        "[ContentPackPipeline] Generated active content registry." +
        " active_packs=" + string.Join(", ", activePackIds) +
        " default_location='" + (string.IsNullOrWhiteSpace(defaultLocationId) ? "-" : defaultLocationId) + "'" +
        " staged_texture_roots=" + stagedTextureRoots.Count +
        " staged_library_roots=" + stagedSpriteLibraryRoots.Count
      );
    }

    return true;
  }

  static void WriteInactiveRegistryAsset(bool logResult) {
    var registry = AssetDatabase.LoadAssetAtPath<ActiveContentRegistry>(ActiveRegistryAssetPath);
    if (registry == null) {
      EnsureDirectoryAssetPath("Assets/Resources");
      registry = ScriptableObject.CreateInstance<ActiveContentRegistry>();
      AssetDatabase.CreateAsset(registry, ActiveRegistryAssetPath);
    }

    registry.Configure(
      externalContentActive: false,
      defaultLocationId: "",
      activePackIds: Array.Empty<string>(),
      stagedTextureRoots: Array.Empty<string>(),
      stagedSpriteLibraryRoots: Array.Empty<string>(),
      coreContentRoots: Array.Empty<string>(),
      locations: Array.Empty<LocationInfo>(),
      dialogs: Array.Empty<LocationDialogDefinition>(),
      warmProfiles: Array.Empty<LocationWarmRegistryEntry>()
    );

    EditorUtility.SetDirty(registry);
    AssetDatabase.SaveAssets();
    ActiveContentRegistryRuntime.ForceReload();

    if (logResult) {
      Debug.Log("[ContentPackPipeline] Wrote inactive content registry fallback.");
    }
  }

  static bool TryReadLocationSnapshot(PackDefinition pack, out LocationInfo locationInfo) {
    locationInfo = null;
    if (pack == null || string.IsNullOrWhiteSpace(pack.snapshotRelativePath)) return false;

    var snapshotAssetPath = NormalizeAssetPath(pack.stageAssetRoot + "/" + pack.snapshotRelativePath);
    var snapshotFullPath = Path.GetFullPath(snapshotAssetPath);
    if (!File.Exists(snapshotFullPath)) return false;

    var json = JsonUtility.FromJson<ExportedLocationJson>(File.ReadAllText(snapshotFullPath));
    if (json == null || string.IsNullOrWhiteSpace(json.locationId)) return false;

    var objectives = new List<LocationObjective>();
    if (json.objectives != null) {
      for (var i = 0; i < json.objectives.Count; i++) {
        var objective = json.objectives[i];
        if (objective == null) continue;
        objectives.Add(new LocationObjective(
          (LocationObjectiveType)Mathf.Clamp(objective.type, 0, (int)LocationObjectiveType.Custom),
          objective.description ?? "",
          objective.targetCount,
          objective.targetSeconds
        ));
      }
    }

    locationInfo = new LocationInfo(
      id: json.locationId,
      name: json.name,
      enemies: json.enemies != null ? new List<string>(json.enemies) : new List<string>(),
      maxEnemies: json.maxEnemies,
      spawnInterval: json.spawnInterval,
      objectives: objectives,
      locationPrefabData: new LocationPrefabData(
        prefab: null,
        assetPath: json.prefabAssetPath,
        localPosition: json.localPosition,
        localEulerAngles: json.localEulerAngles,
        localScale: json.localScale
      )
    );
    return true;
  }

  static bool TryReadDialogSnapshot(PackDefinition pack, out LocationDialogDefinition dialogInfo) {
    dialogInfo = null;
    if (pack == null || string.IsNullOrWhiteSpace(pack.dialogSnapshotRelativePath)) return false;

    var snapshotAssetPath = NormalizeAssetPath(pack.stageAssetRoot + "/" + pack.dialogSnapshotRelativePath);
    var snapshotFullPath = Path.GetFullPath(snapshotAssetPath);
    if (!File.Exists(snapshotFullPath)) return false;

    var json = JsonUtility.FromJson<ExportedDialogJson>(File.ReadAllText(snapshotFullPath));
    if (json == null || string.IsNullOrWhiteSpace(json.locationId)) return false;

    var speakers = new List<DialogSpeakerDefinition>();
    if (json.speakers != null) {
      for (var i = 0; i < json.speakers.Count; i++) {
        var speaker = json.speakers[i];
        if (speaker == null) continue;

        var lines = new List<GameplayDialogController.GameplayDialogNode>();
        if (speaker.lines != null) {
          for (var lineIndex = 0; lineIndex < speaker.lines.Count; lineIndex++) {
            var line = speaker.lines[lineIndex];
            if (line == null) continue;
            lines.Add(new GameplayDialogController.GameplayDialogNode {
              lineNumber = line.lineNumber,
              text = line.text ?? "",
              emotion = line.emotion ?? "",
              trigger = line.trigger ?? "",
              speakerId = line.speakerId ?? "",
              speakerName = line.speakerName ?? "",
              speaker = (GameplayDialogController.DialogSpeakerSide)line.speaker,
              avatarForm = line.avatarForm ?? "",
              otherType = (GameplayDialogController.DialogOtherType)line.otherType,
              portraitLibraryName = line.portraitLibraryName ?? "",
              locationId = line.locationId ?? ""
            });
          }
        }

        speakers.Add(new DialogSpeakerDefinition(
          speakerId: speaker.speakerId ?? "",
          speakerName: speaker.speakerName ?? "",
          portraitLibraryName: speaker.portraitLibraryName ?? "",
          speakerSide: (GameplayDialogController.DialogSpeakerSide)speaker.speakerSide,
          lines: lines.ToArray()
        ));
      }
    }

    dialogInfo = new LocationDialogDefinition(json.locationId, speakers.ToArray());
    return true;
  }

  static void TryAddWarmProfiles(PackDefinition pack, LocationInfo locationSnapshot, List<LocationWarmRegistryEntry> output) {
    if (pack == null || output == null || pack.warmProfiles == null) return;

    for (var i = 0; i < pack.warmProfiles.Count; i++) {
      var warmProfileName = pack.warmProfiles[i];
      if (string.IsNullOrWhiteSpace(warmProfileName)) continue;

      var assetPath = NormalizeAssetPath(pack.stageAssetRoot + "/Resources/" + warmProfileName + ".asset");
      var profile = AssetDatabase.LoadAssetAtPath<LocationWarmProfile>(assetPath);
      if (profile == null) continue;
      var locationId = ResolvePackWarmProfileLocationId(pack, profile, locationSnapshot);
      if (string.IsNullOrWhiteSpace(locationId)) continue;

      output.Add(new LocationWarmRegistryEntry {
        locationId = locationId,
        profile = profile
      });
    }
  }

  static bool ValidateStagedContent(
    ContentPackSelection selection,
    Dictionary<string, PackDefinition> packById,
    List<string> activePackIds
  ) {
    var stageErrors = CollectStageValidationErrors(packById, activePackIds);
    var packPolicyErrors = CollectActivePackPolicyValidationErrors(packById, activePackIds);
    var gameplayCoreErrors = CollectGameplayCoreValidationErrors(selection, activePackIds);

    if (stageErrors.Count > 0) {
      LogErrors("stage_validation", stageErrors);
    }

    if (packPolicyErrors.Count > 0) {
      LogErrors("stage_pack_policy_validation", packPolicyErrors);
    }

    if (gameplayCoreErrors.Count > 0) {
      LogErrors("stage_gameplay_core_validation", gameplayCoreErrors);
    }

    if (stageErrors.Count > 0 || packPolicyErrors.Count > 0 || gameplayCoreErrors.Count > 0) {
      return false;
    }

    return true;
  }

  static List<string> CollectStageValidationErrors(Dictionary<string, PackDefinition> packById, List<string> activePackIds) {
    var errors = new List<string>();
    var stageRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
      NormalizeAssetPath(StageCoreAssetPath)
    };

    for (var i = 0; i < activePackIds.Count; i++) {
      if (packById.TryGetValue(activePackIds[i], out var pack) && pack != null) {
        stageRoots.Add(NormalizeAssetPath(pack.stageAssetRoot));
      }
    }

    for (var i = 0; i < activePackIds.Count; i++) {
      if (!packById.TryGetValue(activePackIds[i], out var pack) || pack == null) continue;
      var stagedOwnedRoots = ExpandStagedOwnedRoots(pack);
      for (var rootIndex = 0; rootIndex < stagedOwnedRoots.Count; rootIndex++) {
        ValidateStagedRoot(stagedOwnedRoots[rootIndex], stageRoots, errors);
      }
    }

    return errors;
  }

  static List<string> CollectGameplayCoreValidationErrors(ContentPackSelection selection, List<string> activePackIds) {
    var errors = new List<string>();
    if (selection == null || !selection.ExternalContentEnabled || activePackIds == null || activePackIds.Count <= 0) {
      return errors;
    }

    if (!activePackIds.Contains(CorePackId, StringComparer.OrdinalIgnoreCase)) {
      errors.Add("Active pack selection is missing the required Core pack.");
      return errors;
    }

    ValidateGameplayCoreAssetExists(GameplayCoreAssetPaths.EsperanzaPrefabAssetPath, "player_prefab", errors);

    foreach (var projectile in Projectiles.EnumerateAll()) {
      var projectileAssetPath = NormalizeAssetPath(projectile.Value?.prefabAddress);
      if (string.IsNullOrWhiteSpace(projectileAssetPath)) {
        errors.Add("Projectile '" + projectile.Key + "' is missing a prefab asset path.");
        continue;
      }

      ValidateGameplayCoreAssetExists(projectileAssetPath, "projectile_prefab:" + projectile.Key, errors);
    }

    ValidateGameplayCoreAddressable(GameplayCoreAssetPaths.EsperanzaPrefabAssetPath, "player_prefab", errors);

    foreach (var projectile in Projectiles.EnumerateAll()) {
      var projectileAssetPath = NormalizeAssetPath(projectile.Value?.prefabAddress);
      if (string.IsNullOrWhiteSpace(projectileAssetPath)) continue;
      ValidateGameplayCoreAddressable(projectileAssetPath, "projectile_prefab:" + projectile.Key, errors);
    }

    return errors;
  }

  static List<string> CollectActivePackPolicyValidationErrors(
    Dictionary<string, PackDefinition> packById,
    List<string> activePackIds
  ) {
    var errors = new List<string>();
    if (packById == null || activePackIds == null || activePackIds.Count <= 0) {
      return errors;
    }

    var defaultLocationPackIds = new List<string>();
    var defaultLocationIds = new List<string>();
    var hasGameplayLocations = false;

    for (var i = 0; i < activePackIds.Count; i++) {
      if (!packById.TryGetValue(activePackIds[i], out var pack) || pack == null) {
        continue;
      }

      var ownsLocations = pack.ownedLocations != null && pack.ownedLocations.Count > 0;
      if (ownsLocations) {
        hasGameplayLocations = true;
      }

      var defaultLocationId = NormalizeToken(pack.defaultLocationId);
      if (!string.IsNullOrWhiteSpace(defaultLocationId)) {
        defaultLocationPackIds.Add(pack.packId);
        defaultLocationIds.Add(defaultLocationId);
      }

      var hasLocationSnapshot = TryReadLocationSnapshot(pack, out var locationSnapshot) && locationSnapshot != null;
      var hasDialogSnapshot = TryReadDialogSnapshot(pack, out var dialogSnapshot) && dialogSnapshot != null;

      if (ownsLocations && !hasLocationSnapshot) {
        errors.Add(
          "Active pack is missing a staged location snapshot." +
          " pack_id='" + pack.packId + "'" +
          " snapshot_path='" + NormalizeAssetPath(pack.stageAssetRoot + "/" + pack.snapshotRelativePath) + "'"
        );
      }

      var snapshotLocationId = NormalizeToken(locationSnapshot != null ? locationSnapshot.id : "");
      if (ownsLocations && hasLocationSnapshot && !PackOwnsLocation(pack, snapshotLocationId)) {
        errors.Add(
          "Active pack location snapshot resolved to an unowned location." +
          " pack_id='" + pack.packId + "'" +
          " location_id='" + snapshotLocationId + "'"
        );
      }

      if (!string.IsNullOrWhiteSpace(defaultLocationId) && ownsLocations && !PackOwnsLocation(pack, defaultLocationId)) {
        errors.Add(
          "Pack defaultLocationId does not belong to its owned locations." +
          " pack_id='" + pack.packId + "'" +
          " default_location_id='" + defaultLocationId + "'"
        );
      }

      if (hasLocationSnapshot) {
        var prefabAssetPath = NormalizeAssetPath(locationSnapshot.locationPrefabData != null ? locationSnapshot.locationPrefabData.AssetPath : "");
        if (string.IsNullOrWhiteSpace(prefabAssetPath)) {
          errors.Add(
            "Location snapshot is missing a prefab asset path." +
            " pack_id='" + pack.packId + "'" +
            " location_id='" + (string.IsNullOrWhiteSpace(snapshotLocationId) ? "-" : snapshotLocationId) + "'"
          );
        }
        else {
          var stagedPrefabAssetPath = BuildStageAssetPath(pack, prefabAssetPath);
          if (string.IsNullOrWhiteSpace(stagedPrefabAssetPath) || !File.Exists(Path.GetFullPath(stagedPrefabAssetPath))) {
            errors.Add(
              "Location snapshot prefab is missing from the staged pack." +
              " pack_id='" + pack.packId + "'" +
              " location_id='" + (string.IsNullOrWhiteSpace(snapshotLocationId) ? "-" : snapshotLocationId) + "'" +
              " prefab_path='" + prefabAssetPath + "'" +
              " staged_prefab_path='" + stagedPrefabAssetPath + "'"
            );
          }
        }

        if ((locationSnapshot.enemies == null || locationSnapshot.enemies.Count <= 0) &&
            (pack.ownedEnemyTypes == null || pack.ownedEnemyTypes.Count <= 0)) {
          errors.Add(
            "Location snapshot is missing enemy ownership." +
            " pack_id='" + pack.packId + "'" +
            " location_id='" + (string.IsNullOrWhiteSpace(snapshotLocationId) ? "-" : snapshotLocationId) + "'"
          );
        }
      }

      if (pack.dialogIds != null && pack.dialogIds.Count > 0 && !hasDialogSnapshot) {
        errors.Add(
          "Active pack is missing a staged dialog snapshot." +
          " pack_id='" + pack.packId + "'" +
          " snapshot_path='" + NormalizeAssetPath(pack.stageAssetRoot + "/" + pack.dialogSnapshotRelativePath) + "'"
        );
      }

      if (hasDialogSnapshot) {
        var dialogLocationId = NormalizeToken(dialogSnapshot.locationId);
        if (ownsLocations && !PackOwnsLocation(pack, dialogLocationId)) {
          errors.Add(
            "Dialog snapshot resolved to an unowned location." +
            " pack_id='" + pack.packId + "'" +
            " location_id='" + dialogLocationId + "'"
          );
        }

        if (hasLocationSnapshot && !string.Equals(dialogLocationId, snapshotLocationId, StringComparison.OrdinalIgnoreCase)) {
          errors.Add(
            "Location and dialog snapshots disagree on the location id." +
            " pack_id='" + pack.packId + "'" +
            " location_snapshot='" + snapshotLocationId + "'" +
            " dialog_snapshot='" + dialogLocationId + "'"
          );
        }

        ValidateDialogSnapshot(pack, dialogSnapshot, errors);
      }

      if (ownsLocations && (pack.warmProfiles == null || pack.warmProfiles.Count <= 0)) {
        errors.Add(
          "Active pack is missing warm profiles for its owned locations." +
          " pack_id='" + pack.packId + "'"
        );
      }

      ValidatePackWarmProfiles(pack, locationSnapshot, errors);
    }

    if (hasGameplayLocations && defaultLocationPackIds.Count <= 0) {
      errors.Add("Active gameplay packs are missing a defaultLocationId policy.");
    }

    if (defaultLocationPackIds.Count > 1) {
      errors.Add(
        "Multiple active packs define defaultLocationId." +
        " pack_ids='" + string.Join(", ", defaultLocationPackIds) + "'" +
        " location_ids='" + string.Join(", ", defaultLocationIds) + "'"
      );
    }

    return errors;
  }

  static void ValidatePackWarmProfiles(PackDefinition pack, LocationInfo locationSnapshot, List<string> errors) {
    if (pack == null || errors == null || pack.warmProfiles == null) return;

    for (var i = 0; i < pack.warmProfiles.Count; i++) {
      var warmProfileName = NormalizeToken(pack.warmProfiles[i]);
      if (string.IsNullOrWhiteSpace(warmProfileName)) continue;

      var assetPath = NormalizeAssetPath(pack.stageAssetRoot + "/Resources/" + warmProfileName + ".asset");
      var profile = AssetDatabase.LoadAssetAtPath<LocationWarmProfile>(assetPath);
      if (profile == null) {
        errors.Add(
          "Active pack is missing a staged warm profile asset." +
          " pack_id='" + pack.packId + "'" +
          " asset_path='" + assetPath + "'"
        );
        continue;
      }

      var resolvedLocationId = ResolvePackWarmProfileLocationId(pack, profile, locationSnapshot);
      if (string.IsNullOrWhiteSpace(resolvedLocationId)) {
        errors.Add(
          "Unable to resolve a warm profile location id for the active pack." +
          " pack_id='" + pack.packId + "'" +
          " profile='" + warmProfileName + "'"
        );
        continue;
      }

      if (pack.ownedLocations != null && pack.ownedLocations.Count > 0 && !PackOwnsLocation(pack, resolvedLocationId)) {
        errors.Add(
          "Warm profile resolved to an unowned location." +
          " pack_id='" + pack.packId + "'" +
          " profile='" + warmProfileName + "'" +
          " location_id='" + resolvedLocationId + "'"
        );
      }
    }
  }

  static void ValidateDialogSnapshot(PackDefinition pack, LocationDialogDefinition dialogSnapshot, List<string> errors) {
    if (pack == null || dialogSnapshot == null || errors == null) {
      return;
    }

    var dialogLocationId = NormalizeToken(dialogSnapshot.locationId);
    if (string.IsNullOrWhiteSpace(dialogLocationId)) {
      errors.Add(
        "Dialog snapshot is missing a location id." +
        " pack_id='" + pack.packId + "'"
      );
      return;
    }

    if (dialogSnapshot.speakers == null || dialogSnapshot.speakers.Count <= 0) {
      errors.Add(
        "Dialog snapshot is missing speaker chains." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'"
      );
      return;
    }

    var seenLocationLineNumbers = new HashSet<int>();
    for (var speakerIndex = 0; speakerIndex < dialogSnapshot.speakers.Count; speakerIndex++) {
      ValidateDialogSpeaker(pack, dialogLocationId, dialogSnapshot.speakers[speakerIndex], seenLocationLineNumbers, errors);
    }
  }

  static void ValidateDialogSpeaker(
    PackDefinition pack,
    string dialogLocationId,
    DialogSpeakerDefinition speaker,
    HashSet<int> seenLocationLineNumbers,
    List<string> errors
  ) {
    var speakerId = NormalizeToken(speaker != null ? speaker.speakerId : "");
    if (string.IsNullOrWhiteSpace(speakerId)) {
      errors.Add(
        "Dialog snapshot contains a speaker with no speaker id." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'"
      );
      return;
    }

    if (speaker == null || speaker.lines == null || speaker.lines.Count <= 0) {
      errors.Add(
        "Dialog speaker chain is empty." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'" +
        " speaker_id='" + speakerId + "'"
      );
      return;
    }

    var seenLineNumbers = new HashSet<int>();
    for (var lineIndex = 0; lineIndex < speaker.lines.Count; lineIndex++) {
      ValidateDialogLine(pack, dialogLocationId, speakerId, speaker.lines[lineIndex], seenLineNumbers, seenLocationLineNumbers, errors);
    }
  }

  static void ValidateDialogLine(
    PackDefinition pack,
    string dialogLocationId,
    string speakerId,
    GameplayDialogController.GameplayDialogNode line,
    HashSet<int> seenLineNumbers,
    HashSet<int> seenLocationLineNumbers,
    List<string> errors
  ) {
    if (line == null) {
      errors.Add(
        "Dialog speaker chain contains a null line." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'" +
        " speaker_id='" + speakerId + "'"
      );
      return;
    }

    var lineNumber = Mathf.Max(line.lineNumber, 0);
    if (lineNumber <= 0) {
      errors.Add(
        "Dialog line number must be greater than zero." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'" +
        " speaker_id='" + speakerId + "'"
      );
    }
    else if (seenLineNumbers != null && !seenLineNumbers.Add(lineNumber)) {
      errors.Add(
        "Dialog speaker chain reuses a seen line number." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'" +
        " speaker_id='" + speakerId + "'" +
        " line_number='" + lineNumber + "'"
      );
    }

    if (lineNumber > 0 && seenLocationLineNumbers != null && !seenLocationLineNumbers.Add(lineNumber)) {
      errors.Add(
        "Dialog location reuses a line number across speakers." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'" +
        " speaker_id='" + speakerId + "'" +
        " line_number='" + lineNumber + "'"
      );
    }

    if (string.IsNullOrWhiteSpace(line.text)) {
      errors.Add(
        "Dialog line is missing text." +
        " pack_id='" + pack.packId + "'" +
        " location_id='" + dialogLocationId + "'" +
        " speaker_id='" + speakerId + "'" +
        " line_number='" + lineNumber + "'"
      );
    }
  }

  static string ResolvePackWarmProfileLocationId(
    PackDefinition pack,
    LocationWarmProfile profile,
    LocationInfo locationSnapshot
  ) {
    var profileLocationId = NormalizeToken(profile != null ? profile.LocationId : "");
    if (!string.IsNullOrWhiteSpace(profileLocationId)) {
      return profileLocationId;
    }

    var snapshotLocationId = NormalizeToken(locationSnapshot != null ? locationSnapshot.id : "");
    if (!string.IsNullOrWhiteSpace(snapshotLocationId)) {
      return snapshotLocationId;
    }

    if (pack == null || pack.ownedLocations == null || pack.ownedLocations.Count <= 0) {
      return "";
    }

    var resolvedLocationId = "";
    for (var i = 0; i < pack.ownedLocations.Count; i++) {
      var candidate = NormalizeToken(pack.ownedLocations[i]);
      if (string.IsNullOrWhiteSpace(candidate)) continue;
      if (string.IsNullOrWhiteSpace(resolvedLocationId)) {
        resolvedLocationId = candidate;
        continue;
      }
      if (!string.Equals(resolvedLocationId, candidate, StringComparison.OrdinalIgnoreCase)) {
        return "";
      }
    }

    return resolvedLocationId;
  }

  static bool PackOwnsLocation(PackDefinition pack, string locationId) {
    var normalizedLocationId = NormalizeToken(locationId);
    if (pack == null || string.IsNullOrWhiteSpace(normalizedLocationId) || pack.ownedLocations == null) {
      return false;
    }

    for (var i = 0; i < pack.ownedLocations.Count; i++) {
      if (string.Equals(NormalizeToken(pack.ownedLocations[i]), normalizedLocationId, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static void ValidateGameplayCoreAssetExists(string projectAssetPath, string label, List<string> errors) {
    var stagedAssetPath = BuildCoreStageAssetPath(projectAssetPath);
    if (string.IsNullOrWhiteSpace(stagedAssetPath) || !File.Exists(Path.GetFullPath(stagedAssetPath))) {
      errors?.Add(
        "Missing staged gameplay core asset." +
        " label='" + label + "'" +
        " project_path='" + NormalizeAssetPath(projectAssetPath) + "'" +
        " staged_path='" + stagedAssetPath + "'"
      );
    }
  }

  static void ValidateGameplayCoreAddressable(string projectAssetPath, string label, List<string> errors) {
    var stagedAssetPath = BuildCoreStageAssetPath(projectAssetPath);
    if (string.IsNullOrWhiteSpace(stagedAssetPath)) return;

    var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
    if (settings == null) {
      errors?.Add("Addressables settings were not found while validating gameplay core asset '" + label + "'.");
      return;
    }

    var guid = AssetDatabase.AssetPathToGUID(stagedAssetPath);
    if (string.IsNullOrWhiteSpace(guid)) {
      errors?.Add(
        "Missing GUID for staged gameplay core asset." +
        " label='" + label + "'" +
        " staged_path='" + stagedAssetPath + "'"
      );
      return;
    }

    var entry = settings.FindAssetEntry(guid);
    if (entry == null) {
      errors?.Add(
        "Missing Addressables entry for staged gameplay core asset." +
        " label='" + label + "'" +
        " staged_path='" + stagedAssetPath + "'"
      );
      return;
    }

    if (!string.Equals(entry.address, stagedAssetPath, StringComparison.Ordinal)) {
      errors?.Add(
        "Addressables entry address mismatch for staged gameplay core asset." +
        " label='" + label + "'" +
        " staged_path='" + stagedAssetPath + "'" +
        " address='" + entry.address + "'"
      );
    }
  }

  static string BuildCoreStageAssetPath(string projectAssetPath) {
    var normalizedProjectPath = NormalizeAssetPath(projectAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedProjectPath) ||
        !normalizedProjectPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
      return "";
    }

    return NormalizeAssetPath(StageCoreAssetPath + "/" + normalizedProjectPath.Substring("Assets/".Length));
  }

  static List<string> ExpandStagedOwnedRoots(PackDefinition pack) {
    var result = new List<string>();
    if (pack == null || pack.ownedRoots == null) return result;

    for (var i = 0; i < pack.ownedRoots.Count; i++) {
      var stageRoot = BuildStageAssetPath(pack, pack.ownedRoots[i]);
      var expanded = ExpandProjectRoots(new[] { stageRoot }, errors: null);
      for (var assetIndex = 0; assetIndex < expanded.Count; assetIndex++) {
        AddUniquePath(result, expanded[assetIndex]);
      }
    }

    return result;
  }

  static void ValidateStagedRoot(string stagedAssetPath, HashSet<string> stageRoots, List<string> errors) {
    if (string.IsNullOrWhiteSpace(stagedAssetPath) || stageRoots == null || errors == null) return;
    if (!File.Exists(Path.GetFullPath(stagedAssetPath))) return;

    var dependencies = AssetDatabase.GetDependencies(new[] { stagedAssetPath }, true);
    for (var i = 0; i < dependencies.Length; i++) {
      var dependency = NormalizeAssetPath(dependencies[i]);
      if (string.IsNullOrWhiteSpace(dependency)) continue;
      if (!dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
      if (AssetDatabase.IsValidFolder(dependency)) continue;
      if (ShouldIgnoreDependency(dependency)) continue;
      ValidateDependencyUnderStageRoots(stagedAssetPath, dependency, stageRoots, errors);
    }

    var includeDependencies = CollectLocalTextIncludeDependencies(stagedAssetPath, errors);
    for (var i = 0; i < includeDependencies.Count; i++) {
      ValidateDependencyUnderStageRoots(stagedAssetPath, includeDependencies[i], stageRoots, errors);
    }
  }

  static void ValidateDependencyUnderStageRoots(
    string stagedAssetPath,
    string dependency,
    HashSet<string> stageRoots,
    List<string> errors
  ) {
    if (string.IsNullOrWhiteSpace(dependency) || stageRoots == null || errors == null) return;

    var isStaged = false;
    foreach (var stageRoot in stageRoots) {
      if (string.IsNullOrWhiteSpace(stageRoot)) continue;
      if (dependency.StartsWith(stageRoot + "/", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(stageRoot, dependency, StringComparison.OrdinalIgnoreCase)) {
        isStaged = true;
        break;
      }
    }

    if (isStaged) return;

    errors.Add(
      "Staged asset leaked an original project dependency." +
      " staged_asset='" + stagedAssetPath + "'" +
      " dependency='" + dependency + "'"
    );
  }

  static void AppendStageSearchRoots(List<string> output, bool includeSpriteLibraries, bool includeTextures) {
    if (output == null) return;

    var selection = AssetDatabase.LoadAssetAtPath<ContentPackSelection>(SelectionAssetPath);
    if (selection == null || !selection.ExternalContentEnabled) return;

    var packDefinitions = BuildPackDefinitions(selection.ExternalRoot);
    var packById = packDefinitions.ToDictionary(pack => pack.packId, StringComparer.OrdinalIgnoreCase);
    var activePackIds = ResolveConcreteActivePackIds(selection.GetNormalizedActivePackIds(), packById);
    for (var i = 0; i < activePackIds.Count; i++) {
      var stageRoot = GetStageAssetRoot(activePackIds[i], packById);
      if (string.IsNullOrWhiteSpace(stageRoot)) continue;
      if (includeTextures) {
        AddUniquePath(output, stageRoot + "/Sprites");
      }
      if (includeSpriteLibraries) {
        AddUniquePath(output, stageRoot + "/Sprites/SpriteLibraries");
      }
    }
  }

  static string GetStageAssetRoot(string packId, Dictionary<string, PackDefinition> packById = null) {
    var normalizedPackId = NormalizeToken(packId);
    if (string.IsNullOrWhiteSpace(normalizedPackId)) return "";

    if (packById != null &&
        packById.TryGetValue(normalizedPackId, out var pack) &&
        pack != null &&
        !string.IsNullOrWhiteSpace(pack.stageAssetRoot)) {
      return NormalizeAssetPath(pack.stageAssetRoot);
    }

    return string.Equals(normalizedPackId, CorePackId, StringComparison.OrdinalIgnoreCase)
      ? StageCoreAssetPath
      : StageSlicesAssetPath + "/" + normalizedPackId;
  }

  static List<string> ResolveConcreteActivePackIds(
    List<string> selectedPackIds,
    Dictionary<string, PackDefinition> packById
  ) {
    var resolved = new List<string>();
    if (selectedPackIds == null || selectedPackIds.Count <= 0 || packById == null || packById.Count <= 0) {
      return resolved;
    }

    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    void Visit(string packId) {
      var normalizedPackId = NormalizeToken(packId);
      if (string.IsNullOrWhiteSpace(normalizedPackId) || !visited.Add(normalizedPackId)) {
        return;
      }

      if (!packById.TryGetValue(normalizedPackId, out var pack) || pack == null) {
        return;
      }

      if (pack.requiredPackIds != null) {
        for (var i = 0; i < pack.requiredPackIds.Count; i++) {
          Visit(pack.requiredPackIds[i]);
        }
      }

      if (pack.stageForRuntime && !string.IsNullOrWhiteSpace(pack.stageAssetRoot)) {
        resolved.Add(pack.packId);
      }
    }

    for (var i = 0; i < selectedPackIds.Count; i++) {
      Visit(selectedPackIds[i]);
    }

    return resolved;
  }

  static int RemoveInactiveStageLinks(List<string> activePackIds) {
    var removedCount = 0;
    EnsureDirectoryAssetPath(StageRootAssetPath);
    EnsureDirectoryAssetPath(StageSlicesAssetPath);

    if (RemoveStageLinkIfInactive(StageCoreAssetPath, activePackIds.Contains(CorePackId, StringComparer.OrdinalIgnoreCase))) {
      removedCount++;
    }

    var existingSliceDirectories = Directory.Exists(Path.GetFullPath(StageSlicesAssetPath))
      ? Directory.GetDirectories(Path.GetFullPath(StageSlicesAssetPath))
      : Array.Empty<string>();

    for (var i = 0; i < existingSliceDirectories.Length; i++) {
      var assetPath = ToProjectAssetPath(existingSliceDirectories[i]);
      var packId = Path.GetFileName(assetPath);
      var keep = activePackIds.Contains(packId, StringComparer.OrdinalIgnoreCase);
      if (RemoveStageLinkIfInactive(assetPath, keep)) {
        removedCount++;
      }
    }

    return removedCount;
  }

  static bool RemoveStageLinkIfInactive(string stageAssetPath, bool keep) {
    if (keep) return false;
    return DeleteStagePath(stageAssetPath);
  }

  static bool EnsureStageLink(string stageAssetPath, string externalRootPath, out bool reused) {
    reused = false;
    var normalizedStageAssetPath = NormalizeAssetPath(stageAssetPath);
    var stageFullPath = NormalizeFullPath(stageAssetPath);
    var targetFullPath = NormalizeFullPath(externalRootPath);

    if (CanReuseStageLink(stageFullPath, targetFullPath)) {
      reused = true;
      return false;
    }

    DeleteStagePath(normalizedStageAssetPath);
    EnsureDirectoryFullPath(Path.GetDirectoryName(stageFullPath));
    CreateJunction(stageFullPath, targetFullPath);
    return true;
  }

  static bool CanReuseStageLink(string stageFullPath, string targetFullPath) {
    if (!Directory.Exists(stageFullPath)) return false;
    if (!IsReparsePoint(stageFullPath)) return false;

    var existingTargetFullPath = TryGetLinkTargetFullPath(stageFullPath);
    return !string.IsNullOrWhiteSpace(existingTargetFullPath) &&
           string.Equals(existingTargetFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase);
  }

  static string TryGetLinkTargetFullPath(string linkFullPath) {
    if (string.IsNullOrWhiteSpace(linkFullPath)) return "";

    try {
      var directoryInfo = new DirectoryInfo(linkFullPath);
      var resolveLinkTargetMethod = typeof(DirectoryInfo).GetMethod("ResolveLinkTarget", new[] { typeof(bool) });
      if (resolveLinkTargetMethod != null) {
        var resolvedTarget = resolveLinkTargetMethod.Invoke(directoryInfo, new object[] { false }) as FileSystemInfo;
        if (resolvedTarget != null) {
          return NormalizeFullPath(resolvedTarget.FullName);
        }
      }

      var linkTargetProperty = typeof(FileSystemInfo).GetProperty("LinkTarget");
      if (linkTargetProperty != null) {
        var rawTarget = linkTargetProperty.GetValue(directoryInfo) as string;
        if (!string.IsNullOrWhiteSpace(rawTarget)) {
          if (Path.IsPathRooted(rawTarget)) {
            return NormalizeFullPath(rawTarget);
          }

          var parentDirectory = Path.GetDirectoryName(linkFullPath) ?? "";
          var reflectedTargetFullPath = NormalizeFullPath(Path.Combine(parentDirectory, rawTarget));
          if (!string.IsNullOrWhiteSpace(reflectedTargetFullPath)) {
            return reflectedTargetFullPath;
          }
        }
      }
    }
    catch {
    }

    return TryGetLinkTargetFullPathWithPowerShell(linkFullPath);
  }

  static string TryGetLinkTargetFullPathWithPowerShell(string linkFullPath) {
    if (string.IsNullOrWhiteSpace(linkFullPath)) return "";

    try {
      var escapedLinkPath = linkFullPath.Replace("'", "''");
      var startInfo = new ProcessStartInfo {
        FileName = "powershell",
        Arguments = "-NoProfile -Command \"(Get-Item -LiteralPath '" + escapedLinkPath + "').Target\"",
        CreateNoWindow = true,
        UseShellExecute = false,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        WorkingDirectory = GetProjectRoot()
      };

      using var process = Process.Start(startInfo);
      var output = process.StandardOutput.ReadToEnd().Trim();
      var error = process.StandardError.ReadToEnd().Trim();
      process.WaitForExit();

      if (process.ExitCode != 0) {
        if (!string.IsNullOrWhiteSpace(error)) {
          Debug.LogWarning(
            "[ContentPackPipeline] Failed to read stage link target via PowerShell." +
            " path='" + linkFullPath + "'" +
            " exit_code=" + process.ExitCode +
            " error='" + error + "'"
          );
        }
        return "";
      }

      return string.IsNullOrWhiteSpace(output) ? "" : NormalizeFullPath(output);
    }
    catch (Exception ex) {
      Debug.LogWarning(
        "[ContentPackPipeline] Exception while reading stage link target via PowerShell." +
        " path='" + linkFullPath + "'" +
        " exception='" + ex.GetType().Name + "'" +
        " message='" + ex.Message + "'"
      );
      return "";
    }
  }

  static bool DeleteStagePath(string stageAssetPath) {
    var fullPath = Path.GetFullPath(stageAssetPath);
    if (!Directory.Exists(fullPath) && !File.Exists(fullPath)) {
      return DeleteMetaIfPresent(stageAssetPath);
    }

    var deleted = false;
    if (Directory.Exists(fullPath)) {
      if (IsReparsePoint(fullPath)) {
        Directory.Delete(fullPath);
      }
      else {
        Directory.Delete(fullPath, recursive: true);
      }
      deleted = true;
    }
    else if (File.Exists(fullPath)) {
      File.Delete(fullPath);
      deleted = true;
    }

    return DeleteMetaIfPresent(stageAssetPath) || deleted;
  }

  static bool DeleteMetaIfPresent(string assetPath) {
    var metaFullPath = Path.GetFullPath(assetPath + ".meta");
    if (!File.Exists(metaFullPath)) return false;
    File.Delete(metaFullPath);
    return true;
  }

  static bool IsReparsePoint(string fullPath) {
    if (!Directory.Exists(fullPath)) return false;
    var attributes = File.GetAttributes(fullPath);
    return (attributes & FileAttributes.ReparsePoint) != 0;
  }

  static void CreateJunction(string stageFullPath, string targetFullPath) {
    var startInfo = new ProcessStartInfo {
      FileName = "cmd.exe",
      Arguments = "/c mklink /J \"" + stageFullPath + "\" \"" + targetFullPath + "\"",
      CreateNoWindow = true,
      UseShellExecute = false,
      RedirectStandardError = true,
      RedirectStandardOutput = true,
      WorkingDirectory = GetProjectRoot()
    };

    using var process = Process.Start(startInfo);
    process.WaitForExit();

    if (process.ExitCode == 0) return;

    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    throw new InvalidOperationException(
      "mklink /J failed." +
      " stage='" + stageFullPath + "'" +
      " target='" + targetFullPath + "'" +
      " exit_code=" + process.ExitCode +
      " output='" + output + "'" +
      " error='" + error + "'"
    );
  }

  static List<string> ExpandProjectRoots(IEnumerable<string> roots, List<string> errors) {
    var result = new List<string>();
    if (roots == null) return result;

    foreach (var root in roots) {
      var normalizedRoot = NormalizeAssetPath(root);
      if (string.IsNullOrWhiteSpace(normalizedRoot)) continue;

      var fullPath = Path.GetFullPath(normalizedRoot);
      if (File.Exists(fullPath)) {
        AddUniquePath(result, normalizedRoot);
        continue;
      }

      if (!Directory.Exists(fullPath)) {
        errors?.Add("Missing asset root '" + normalizedRoot + "'.");
        continue;
      }

      var guids = AssetDatabase.FindAssets("", new[] { normalizedRoot });
      Array.Sort(guids, StringComparer.OrdinalIgnoreCase);
      for (var i = 0; i < guids.Length; i++) {
        var assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guids[i]));
        if (string.IsNullOrWhiteSpace(assetPath) || AssetDatabase.IsValidFolder(assetPath)) continue;
        AddUniquePath(result, assetPath);
      }
    }

    return result;
  }

  static HashSet<string> CollectReferencedLibraryNamesFromAssets(IEnumerable<string> assetPaths) {
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (assetPaths == null) return result;

    foreach (var assetPath in assetPaths) {
      var normalizedAssetPath = NormalizeAssetPath(assetPath);
      if (string.IsNullOrWhiteSpace(normalizedAssetPath)) continue;
      if (!ShouldRewriteTextFile(normalizedAssetPath)) continue;

      var fullPath = Path.GetFullPath(normalizedAssetPath);
      if (!File.Exists(fullPath)) continue;
      var text = File.ReadAllText(fullPath);

      AddLibraryNamesFromRegex(result, text, LibraryNameRegex);
      AddLibraryNamesFromRegex(result, text, PortraitLibraryNameRegex);
    }

    return result;
  }

  static void AddLibraryNamesFromRegex(HashSet<string> output, string text, Regex regex) {
    if (output == null || string.IsNullOrWhiteSpace(text) || regex == null) return;

    var matches = regex.Matches(text);
    for (var i = 0; i < matches.Count; i++) {
      if (!matches[i].Success) continue;
      AddUniqueLibraryName(output, matches[i].Groups[1].Value);
    }
  }

  static void AddUniqueLibraryName(HashSet<string> output, string value) {
    if (output == null) return;
    var normalized = NormalizeLibraryName(value);
    if (string.IsNullOrWhiteSpace(normalized)) return;
    output.Add(normalized);
  }

  static List<string> ResolveLibraryAssetPaths(
    HashSet<string> libraryNames,
    Dictionary<string, string> librariesByKey,
    List<string> errors
  ) {
    var result = new List<string>();
    if (libraryNames == null) return result;

    foreach (var libraryName in libraryNames.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)) {
      if (!librariesByKey.TryGetValue(libraryName, out var libraryPath)) {
        errors?.Add("Missing sprite library '" + libraryName + "'.");
        continue;
      }

      AddUniquePath(result, libraryPath);

      var normalLibraryName = libraryName + "N";
      if (librariesByKey.TryGetValue(normalLibraryName, out var normalLibraryPath)) {
        AddUniquePath(result, normalLibraryPath);
      }
    }

    return result;
  }

  static Dictionary<string, string> DiscoverProjectLibraryPaths() {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var root = NormalizeAssetPath(SpriteStreamingConfig.SourceRootFolder);
    var fullRoot = Path.GetFullPath(root);
    if (!Directory.Exists(fullRoot)) return result;

    var files = Directory.GetFiles(fullRoot, "*.spriteLib", SearchOption.AllDirectories);
    Array.Sort(files, StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < files.Length; i++) {
      var assetPath = ToProjectAssetPath(files[i]);
      var relativePath = assetPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
        ? assetPath.Substring(root.Length + 1)
        : assetPath;
      var key = RemoveExtension(relativePath);
      result[key] = assetPath;
    }

    return result;
  }

  static List<string> CollectPackDependencies(List<string> seedAssetPaths, List<string> errors) {
    var result = new List<string>();
    if (seedAssetPaths == null || seedAssetPaths.Count <= 0) return result;

    var dependencies = AssetDatabase.GetDependencies(seedAssetPaths.ToArray(), true);
    Array.Sort(dependencies, StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < dependencies.Length; i++) {
      var dependency = NormalizeAssetPath(dependencies[i]);
      if (string.IsNullOrWhiteSpace(dependency)) continue;
      if (!dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
      if (AssetDatabase.IsValidFolder(dependency)) continue;
      if (ShouldIgnoreDependency(dependency)) continue;
      if (dependency.StartsWith(StageRootAssetPath + "/", StringComparison.OrdinalIgnoreCase)) continue;
      if (dependency.StartsWith("Assets/Generated/", StringComparison.OrdinalIgnoreCase)) {
        errors?.Add("Generated asset dependency detected '" + dependency + "'.");
        continue;
      }
      AddUniquePath(result, dependency);
    }

    var includeDependencies = new List<string>();
    for (var i = 0; i < result.Count; i++) {
      var localIncludes = CollectLocalTextIncludeDependencies(result[i], errors);
      for (var includeIndex = 0; includeIndex < localIncludes.Count; includeIndex++) {
        AddUniquePath(includeDependencies, localIncludes[includeIndex]);
      }
    }

    for (var i = 0; i < includeDependencies.Count; i++) {
      AddUniquePath(result, includeDependencies[i]);
    }

    return result;
  }

  static List<string> CollectLocalTextIncludeDependencies(string assetPath, List<string> errors) {
    var result = new List<string>();
    if (!ShouldScanLocalIncludes(assetPath)) return result;

    var pending = new Queue<string>();
    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    pending.Enqueue(NormalizeAssetPath(assetPath));

    while (pending.Count > 0) {
      var currentAssetPath = NormalizeAssetPath(pending.Dequeue());
      if (string.IsNullOrWhiteSpace(currentAssetPath) || !visited.Add(currentAssetPath)) continue;
      if (!ShouldScanLocalIncludes(currentAssetPath)) continue;

      var currentFullPath = Path.GetFullPath(currentAssetPath);
      if (!File.Exists(currentFullPath)) {
        errors?.Add("Missing include source asset '" + currentAssetPath + "'.");
        continue;
      }

      var text = File.ReadAllText(currentFullPath);
      var matches = LocalIncludeRegex.Matches(text);
      for (var i = 0; i < matches.Count; i++) {
        if (!matches[i].Success) continue;
        var includeAssetPath = ResolveLocalIncludeAssetPath(currentAssetPath, matches[i].Groups[1].Value, errors);
        if (string.IsNullOrWhiteSpace(includeAssetPath)) continue;
        AddUniquePath(result, includeAssetPath);
        if (ShouldScanLocalIncludes(includeAssetPath)) {
          pending.Enqueue(includeAssetPath);
        }
      }
    }

    return result;
  }

  static string ResolveLocalIncludeAssetPath(string sourceAssetPath, string includePath, List<string> errors) {
    var normalizedInclude = NormalizeAssetPath(includePath);
    if (string.IsNullOrWhiteSpace(normalizedInclude)) return "";
    if (normalizedInclude.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return "";

    string resolvedAssetPath;
    if (normalizedInclude.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
      resolvedAssetPath = normalizedInclude;
    }
    else {
      var sourceDirectory = Path.GetDirectoryName(sourceAssetPath) ?? "";
      resolvedAssetPath = NormalizeAssetPath(Path.Combine(sourceDirectory, normalizedInclude));
    }

    if (!resolvedAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
      return "";
    }

    if (!File.Exists(Path.GetFullPath(resolvedAssetPath))) {
      errors?.Add(
        "Missing local include dependency." +
        " source='" + sourceAssetPath + "'" +
        " include='" + includePath + "'" +
        " resolved='" + resolvedAssetPath + "'"
      );
      return "";
    }

    if (ShouldIgnoreDependency(resolvedAssetPath)) return "";
    if (resolvedAssetPath.StartsWith("Assets/Generated/", StringComparison.OrdinalIgnoreCase)) {
      errors?.Add("Generated include dependency detected '" + resolvedAssetPath + "'.");
      return "";
    }

    return resolvedAssetPath;
  }

  static bool ShouldScanLocalIncludes(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return false;
    var extension = Path.GetExtension(assetPath);
    return !string.IsNullOrWhiteSpace(extension) && LocalIncludeExtensions.Contains(extension);
  }

  static bool ShouldIgnoreDependency(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return true;
    if (assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return true;

    var extension = Path.GetExtension(assetPath);
    return !string.IsNullOrWhiteSpace(extension) && IgnoredDependencyExtensions.Contains(extension);
  }

  static bool ShouldRewriteTextFile(string pathOrAssetPath) {
    var extension = Path.GetExtension(pathOrAssetPath);
    if (string.IsNullOrWhiteSpace(extension)) return false;
    return TextRewriteExtensions.Contains(extension);
  }

  static string BuildStageAssetPath(PackDefinition pack, string projectAssetPath) {
    if (pack == null || string.IsNullOrWhiteSpace(projectAssetPath)) return "";
    var normalizedProjectPath = NormalizeAssetPath(projectAssetPath);
    if (!string.IsNullOrWhiteSpace(pack.stageAssetRoot) &&
        normalizedProjectPath.StartsWith(NormalizeAssetPath(pack.stageAssetRoot) + "/", StringComparison.OrdinalIgnoreCase)) {
      return normalizedProjectPath;
    }
    if (!normalizedProjectPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return normalizedProjectPath;
    return NormalizeAssetPath(pack.stageAssetRoot + "/" + normalizedProjectPath.Substring("Assets/".Length));
  }

  static void RecreatePackDirectory(string packRootPath, string externalRoot) {
    var normalizedPackRoot = NormalizeFullPath(packRootPath);
    var normalizedExternalRoot = NormalizeFullPath(externalRoot);

    if (!normalizedPackRoot.StartsWith(normalizedExternalRoot + "/", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(normalizedPackRoot, normalizedExternalRoot, StringComparison.OrdinalIgnoreCase)) {
      throw new InvalidOperationException("Pack root escaped external root. pack='" + normalizedPackRoot + "'");
    }

    if (Directory.Exists(normalizedPackRoot)) {
      Directory.Delete(normalizedPackRoot, recursive: true);
    }

    Directory.CreateDirectory(normalizedPackRoot);
  }

  static void EnsureDirectoryAssetPath(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return;
    var fullPath = Path.GetFullPath(assetPath);
    Directory.CreateDirectory(fullPath);
  }

  static void EnsureDirectoryFullPath(string fullPath) {
    if (string.IsNullOrWhiteSpace(fullPath)) return;
    Directory.CreateDirectory(fullPath);
  }

  static void WriteJson<T>(string fullPath, T payload) {
    EnsureDirectoryFullPath(Path.GetDirectoryName(fullPath));
    File.WriteAllText(fullPath, JsonUtility.ToJson(payload, prettyPrint: true), new UTF8Encoding(false));
  }

  static string NormalizeLibraryName(string value) {
    var normalized = NormalizeAssetPath(value);
    if (string.IsNullOrWhiteSpace(normalized)) return "";
    if (normalized.EndsWith(".spriteLib", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(0, normalized.Length - ".spriteLib".Length);
    }

    var root = NormalizeAssetPath(SpriteStreamingConfig.SourceRootFolder);
    if (string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase)) return "";
    if (normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(root.Length + 1);
    }

    return normalized.Trim('/');
  }

  static void AddUniquePath(List<string> paths, string value) {
    if (paths == null) return;
    var normalized = NormalizeAssetPath(value);
    if (string.IsNullOrWhiteSpace(normalized)) return;

    for (var i = 0; i < paths.Count; i++) {
      if (string.Equals(paths[i], normalized, StringComparison.OrdinalIgnoreCase)) return;
    }

    paths.Add(normalized);
  }

  static string RemoveExtension(string assetPath) {
    var normalized = NormalizeAssetPath(assetPath);
    return string.IsNullOrWhiteSpace(normalized) ? "" : Path.ChangeExtension(normalized, null)?.Replace('\\', '/') ?? "";
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  static string NormalizeAssetPath(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().Replace('\\', '/');
  }

  static string NormalizeFullPath(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : Path.GetFullPath(value).Replace('\\', '/');
  }

  static string ToProjectAssetPath(string fullPath) {
    var normalizedFullPath = NormalizeFullPath(fullPath);
    var projectRoot = NormalizeFullPath(GetProjectRoot());
    if (normalizedFullPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase)) {
      return normalizedFullPath.Substring(projectRoot.Length + 1);
    }
    return normalizedFullPath;
  }

  static string GetProjectRoot() {
    return Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
  }

  static int CountLegacyGeneratedReferences() {
    var count = CountTextOccurrences("Assets/AddressableAssetsData/AssetGroups/SpriteRuntimeIndex.asset", "Assets/Generated/");

    var runtimeIndexRoot = "Assets/Sprites/SpriteLibraries/RuntimeIndex";
    var runtimeIndexFullPath = Path.GetFullPath(runtimeIndexRoot);
    if (!Directory.Exists(runtimeIndexFullPath)) return count;

    var files = Directory.GetFiles(runtimeIndexFullPath, "*", SearchOption.AllDirectories);
    for (var i = 0; i < files.Length; i++) {
      count += CountTextOccurrences(ToProjectAssetPath(files[i]), "Assets/Generated/");
    }

    return count;
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
