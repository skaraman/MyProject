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

public static class ContentPackPipeline {
  public const string DefaultExternalRoot = @"d:\localDev\Unity\MyProjectContent";
  public const string CorePackId = "Core";
  public const string BaseFormPackId = "Form_Base";
  public const string SlicePackId = "Slice_DomeCity_Imp_Base";
  public const string HomebaseSlicePackId = "Slice_Homebase_Placeholder";
  public const string SunkenCaveSlicePackId = "Slice_SunkenCave_Placeholder";
  public const string EpisodePackId = "Episode_01";
  public const string LegacySlicePackId = "Slice_DomeCity_Imp";

  public const string SelectionAssetPath = "Assets/Editor/ContentPackSelection.asset";
  public const string ActiveRegistryAssetPath = "Assets/Resources/ActiveContentRegistry.asset";
  public const string StageRootAssetPath = "Assets/ContentStage";
  public const string StageCoreAssetPath = "Assets/ContentStage/Core";
  public const string StageFormsAssetPath = "Assets/ContentStage/Forms";
  public const string StageGearsAssetPath = "Assets/ContentStage/Gears";
  public const string StageSlicesAssetPath = "Assets/ContentStage/Slices";
  const string EsperanzaGroupedGearRoot = "Assets/Sprites/Characters/Esperanza/GroupedGearAtlases";

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

  public enum TransitionPipelineMode {
    Smart = 0,
    Clean = 1
  }

  sealed class ExportSyncStats {
    public int packDirectoriesCreated;
    public int packDirectoriesRecreated;
    public int assetPayloadsWritten;
    public int assetPayloadsSkipped;
    public int metaPayloadsWritten;
    public int metaPayloadsSkipped;
    public int generatedFilesWritten;
    public int manifestsWritten;
  }

  sealed class OwnershipAnalysisReport {
    public string authoritativeExternalRoot;
    public int legacyGeneratedReferenceCount;
    public int spriteDuplicateCount;
    public int ownershipViolationCount;
    public int placeholderExemptionCount;
    public int stagedProjectTreeDependencyCount;
    public int stagedCodeDependencyCount;
    public readonly List<string> coreFindings = new();
    public readonly List<string> formFindings = new();
    public readonly List<string> gearFindings = new();
    public readonly List<string> sliceFindings = new();
    public readonly List<string> episodeFindings = new();
    public readonly List<string> legacyFindings = new();
    public readonly List<string> unknownFindings = new();
    public readonly List<string> placeholderFindings = new();
    public readonly List<string> stagedDependencyLeaks = new();
    public readonly List<string> stagedCodeDependencies = new();
  }

  sealed class TransitionRunSummary {
    public readonly TransitionPipelineMode mode;
    public readonly ExportSyncStats export = new();
    public OwnershipAnalysisReport analysis;
    public bool stageCompleted;
    public bool auditCompleted;
    public bool runtimeIndexCompleted;
    public bool addressablesCompleted;
    public bool unifiedImportCompleted;
    public bool hotsetCompleted;

    public TransitionRunSummary(TransitionPipelineMode mode) {
      this.mode = mode;
    }
  }

  [MenuItem("Tools/Content Pipeline/1) Build Active Content (Smart)")]
  public static void BuildActiveContentSmartMenu() {
    RunFullMigrationPass(logResult: true, TransitionPipelineMode.Smart);
  }

  [MenuItem("Tools/Content Pipeline/2) Build Active Content (Clean)")]
  public static void BuildActiveContentCleanMenu() {
    RunFullMigrationPass(logResult: true, TransitionPipelineMode.Clean);
  }

  [MenuItem("Tools/Content Pipeline/Transition/1) Analyze Ownership + Duplicates")]
  public static void AnalyzeOwnershipAndDuplicatesMenu() {
    AnalyzeOwnershipAndDuplicates(logResult: true);
  }

  [MenuItem("Tools/Content Pipeline/Transition/2) Export Missing Pack Content")]
  public static void ExportMissingPackContentMenu() {
    RunExportTransitionStep(logResult: true, TransitionPipelineMode.Smart);
  }

  [MenuItem("Tools/Content Pipeline/Transition/3) Stage Active Packs")]
  public static void StageTransitionActivePacksMenu() {
    RunStageTransitionStep(logResult: true, TransitionPipelineMode.Smart);
  }

  [MenuItem("Tools/Content Pipeline/Transition/4) Audit Legacy Dependencies")]
  public static void AuditLegacyDependenciesMenu() {
    AuditLegacyDependencies(logResult: true);
  }

  [MenuItem("Tools/Content Pipeline/Transition/5) Rebuild Runtime Index")]
  public static void RebuildTransitionRuntimeIndexMenu() {
    RunRebuildRuntimeIndexTransitionStep(logResult: true);
  }

  [MenuItem("Tools/Content Pipeline/Transition/6) Build Addressables")]
  public static void BuildTransitionAddressablesMenu() {
    RunBuildAddressablesTransitionStep(logResult: true, cleanCachesBeforeBuild: false);
  }

  [MenuItem("Tools/Content Pipeline/Transition/7) Full Migration Pass (Smart)")]
  public static void FullMigrationPassSmartMenu() {
    RunFullMigrationPass(logResult: true, TransitionPipelineMode.Smart);
  }

  [MenuItem("Tools/Content Pipeline/Transition/8) Full Migration Pass (Clean)")]
  public static void FullMigrationPassCleanMenu() {
    RunFullMigrationPass(logResult: true, TransitionPipelineMode.Clean);
  }

  [MenuItem("Tools/Content Pipeline/Advanced/Export First Pack Set (Clean)")]
  public static void ExportFirstPackSetMenu() {
    ExportFirstPackSet(logResult: true);
  }

  [MenuItem("Tools/Content Pipeline/Advanced/Stage Active Packs")]
  public static void StageActivePacksMenu() {
    StageActivePacks(logResult: true);
  }

  [MenuItem("Tools/Content Pipeline/Advanced/Audit Active Packs")]
  public static void AuditActivePacksMenu() {
    AuditActivePacks(logResult: true);
  }

  [MenuItem("Tools/Content Pipeline/Advanced/Prepare Active Packs")]
  public static void StageAuditAndRebuildRuntimeIndexMenu() {
    RunPrepareActivePacksPipeline(logResult: true);
  }

  [MenuItem("Tools/Content Pipeline/Advanced/Focus First Slice")]
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
    return PrepareSelectedPacksForRuntimeIndex(contextLabel, logResult, TransitionPipelineMode.Clean);
  }

  public static bool PrepareSelectedPacksForRuntimeIndex(string contextLabel, bool logResult, TransitionPipelineMode mode) {
    var selection = LoadOrCreateSelectionAsset(logResult);
    if (selection == null) {
      Debug.LogError("[ContentPackPipeline] Failed to load content pack selection asset.");
      return false;
    }

    if (!selection.ExternalContentEnabled) {
      WriteInactiveRegistryAsset(logResult);
      return true;
    }

    if (!RefreshExportedPackSetForStage(selection, "prepare_runtime_index:" + (contextLabel ?? ""), logResult, mode, stats: null)) {
      return false;
    }

    return StageActivePacksInternal(selection, logResult, contextLabel);
  }

  public static bool PrepareSelectedPacksForPlayerBuild(string contextLabel, bool logResult) {
    return PrepareSelectedPacksForPlayerBuild(contextLabel, logResult, TransitionPipelineMode.Smart);
  }

  public static bool PrepareSelectedPacksForPlayerBuild(string contextLabel, bool logResult, TransitionPipelineMode mode) {
    if (!PrepareSelectedPacksForRuntimeIndex(contextLabel, logResult, mode)) {
      return false;
    }
    return AuditActivePacks(logResult);
  }

  public static bool ExportFirstPackSet(bool logResult) {
    return ExportPackSet(logResult, TransitionPipelineMode.Clean, stats: null);
  }

  static bool RunExportTransitionStep(bool logResult, TransitionPipelineMode mode) {
    var summary = new TransitionRunSummary(mode);
    var ok = ExportPackSet(logResult, mode, summary.export);
    if (logResult) {
      LogTransitionRunSummary("Export Pack Content", summary);
    }
    return ok;
  }

  static bool RunStageTransitionStep(bool logResult, TransitionPipelineMode mode) {
    var summary = new TransitionRunSummary(mode);
    var selection = LoadOrCreateSelectionAsset(logResult);
    if (selection == null) {
      return false;
    }

    if (!RefreshExportedPackSetForStage(selection, "transition_stage", logResult, mode, summary.export)) {
      return false;
    }

    summary.stageCompleted = StageActivePacksInternal(selection, logResult, "transition_stage");
    if (logResult) {
      LogTransitionRunSummary("Stage Active Packs", summary);
    }
    return summary.stageCompleted;
  }

  static bool RunRebuildRuntimeIndexTransitionStep(bool logResult) {
    return SpriteIndexBuilder.RebuildRuntimeIndexPrepared("Content Pipeline Transition", logResult, failOnError: false);
  }

  static bool RunBuildAddressablesTransitionStep(bool logResult, bool cleanCachesBeforeBuild) {
    return SpriteIndexBuilder.BuildAddressablesContentPrepared(
      "Content Pipeline Transition",
      logResult,
      cleanCachesBeforeBuild,
      useChunkedWarmup: false
    );
  }

  static bool RunFullMigrationPass(bool logResult, TransitionPipelineMode mode) {
    const int stepCount = 8;
    const string pipelineLabel = "Full Migration Pass";
    var summary = new TransitionRunSummary(mode);
    var startedAt = EditorApplication.timeSinceStartup;
    var stepIndex = 1;

    bool RunStep(string stepName, Func<bool> action) {
      ShowContentPackProgress(pipelineLabel, stepIndex, stepCount, stepName);
      if (logResult) {
        Debug.Log(
          "[ContentPackPipeline] [" + pipelineLabel + "] mode='" + mode + "' step " + stepIndex + "/" + stepCount +
          " - " + stepName + " (start)"
        );
      }

      try {
        if (!action()) {
          Debug.LogError(
            "[ContentPackPipeline] [" + pipelineLabel + "] mode='" + mode + "' step " + stepIndex + "/" + stepCount +
            " - " + stepName + " failed."
          );
          return false;
        }
      }
      catch (Exception ex) {
        Debug.LogError(
          "[ContentPackPipeline] [" + pipelineLabel + "] mode='" + mode + "' step " + stepIndex + "/" + stepCount +
          " - " + stepName + " threw an exception.\n" + ex
        );
        return false;
      }

      if (logResult) {
        Debug.Log(
          "[ContentPackPipeline] [" + pipelineLabel + "] mode='" + mode + "' step " + stepIndex + "/" + stepCount +
          " - " + stepName + " (done)"
        );
      }

      stepIndex++;
      return true;
    }

    try {
      if (!RunStep("Analyze ownership + duplicates", () => {
        summary.analysis = AnalyzeOwnershipAndDuplicates(logResult);
        return summary.analysis != null;
      })) return false;

      var selection = LoadOrCreateSelectionAsset(logResult);
      if (selection == null) {
        return false;
      }

      if (!RunStep("Export external pack content", () =>
        RefreshExportedPackSetForStage(selection, "full_migration_pass", logResult, mode, summary.export))) return false;

      if (!RunStep("Stage active packs", () => {
        summary.stageCompleted = StageActivePacksInternal(selection, logResult, "full_migration_pass");
        return summary.stageCompleted;
      })) return false;

      if (!RunStep("Audit legacy dependencies", () => {
        summary.auditCompleted = AuditLegacyDependencies(summary.analysis, logResult);
        return summary.auditCompleted;
      })) return false;

      if (!RunStep("Apply unified import flow", () => {
        summary.unifiedImportCompleted = SpriteStreamingHotsetConfigurator.ApplyUnifiedImportFlow(saveAndRefreshAtEnd: false, logResult: logResult);
        return summary.unifiedImportCompleted;
      })) return false;

      if (!RunStep("Rebuild runtime index", () => {
        summary.runtimeIndexCompleted = SpriteIndexBuilder.RebuildRuntimeIndexPrepared("Content Pipeline Transition", logResult, failOnError: false);
        return summary.runtimeIndexCompleted;
      })) return false;

      if (!RunStep("Apply gameplay + location hotset", () => {
        summary.hotsetCompleted = SpriteStreamingHotsetConfigurator.ApplyPerformanceHotset(
          rebuildRuntimeIndexFirst: false,
          saveAndRefreshAtEnd: false,
          logResult: logResult
        );
        return summary.hotsetCompleted;
      })) return false;

      if (!RunStep("Build Addressables content", () => {
        summary.addressablesCompleted = RunBuildAddressablesTransitionStep(logResult, cleanCachesBeforeBuild: mode == TransitionPipelineMode.Clean);
        return summary.addressablesCompleted;
      })) return false;

      return true;
    }
    finally {
      EditorUtility.ClearProgressBar();
      if (logResult) {
        var duration = (float)(EditorApplication.timeSinceStartup - startedAt);
        Debug.Log(
          "[ContentPackPipeline] [" + pipelineLabel + "] mode='" + mode + "' finished in " +
          duration.ToString("0.00", CultureInfo.InvariantCulture) + "s."
        );
        LogTransitionRunSummary(pipelineLabel, summary);
      }
    }
  }

  static bool ExportPackSet(bool logResult, TransitionPipelineMode mode, ExportSyncStats stats) {
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
        PreparePackDirectory(packDefinitions[i].externalRootPath, externalRoot, mode, stats);
      }

      WriteAssignedAssets(assignedAssets, errors, mode, stats);
      if (errors.Count > 0) {
        LogErrors("export_copy", errors);
        return false;
      }

      for (var i = 0; i < packDefinitions.Count; i++) {
        WriteGeneratedPackData(packDefinitions[i], errors, mode, stats);
      }

      if (errors.Count > 0) {
        LogErrors("export_pack_data", errors);
        return false;
      }

      for (var i = 0; i < packDefinitions.Count; i++) {
        WritePackManifest(packDefinitions[i], errors, mode, stats);
      }

      if (errors.Count > 0) {
        LogErrors("export_manifest", errors);
        return false;
      }

      if (logResult) {
        Debug.Log(
          "[ContentPackPipeline] Exported external pack content." +
          " mode='" + mode + "'" +
          " external_root='" + externalRoot + "'" +
          " pack_count=" + packDefinitions.Count +
          " asset_count=" + assignedAssets.Count +
          FormatExportStats(stats)
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
    return StageActivePacksInternal(selection, logResult, "manual_stage");
  }

  public static bool StageAuditAndRebuildRuntimeIndex(bool logResult) {
    var selection = LoadOrCreateSelectionAsset(logResult);
    if (selection == null) return false;
    if (!RefreshExportedPackSetForStage(selection, "stage_audit_rebuild_runtime_index", logResult)) {
      return false;
    }
    if (!StageActivePacksInternal(selection, logResult, "stage_audit_rebuild_runtime_index")) {
      return false;
    }
    if (!AuditActivePacks(logResult)) {
      return false;
    }
    return SpriteIndexBuilder.RebuildRuntimeIndexPrepared("Prepare Active Packs", logResult, failOnError: false);
  }

  static bool RunPrepareActivePacksPipeline(bool logResult) {
    const string pipelineLabel = "Prepare Active Packs";
    const int stepCount = 4;
    const string contextLabel = "prepare_active_packs";
    var startedAt = EditorApplication.timeSinceStartup;
    var completed = false;

    bool RunStep(int stepIndex, string stepName, Func<bool> action) {
      ShowContentPackProgress(pipelineLabel, stepIndex, stepCount, stepName);
      if (logResult) {
        Debug.Log(
          "[ContentPackPipeline] [" + pipelineLabel + "] Step " + stepIndex + "/" + stepCount +
          " - " + stepName + " (start)"
        );
      }

      try {
        if (!action()) {
          Debug.LogError(
            "[ContentPackPipeline] [" + pipelineLabel + "] Step " + stepIndex + "/" + stepCount +
            " - " + stepName + " failed."
          );
          return false;
        }
      }
      catch (Exception ex) {
        Debug.LogError(
          "[ContentPackPipeline] [" + pipelineLabel + "] Step " + stepIndex + "/" + stepCount +
          " - " + stepName + " threw an exception.\n" + ex
        );
        return false;
      }

      if (logResult) {
        Debug.Log(
          "[ContentPackPipeline] [" + pipelineLabel + "] Step " + stepIndex + "/" + stepCount +
          " - " + stepName + " (done)"
        );
      }
      return true;
    }

    try {
      var selection = LoadOrCreateSelectionAsset(logResult);
      if (selection == null) {
        return false;
      }

      if (!selection.ExternalContentEnabled) {
        ShowContentPackProgress(pipelineLabel, 1, 1, "Write inactive registry fallback");
        WriteInactiveRegistryAsset(logResult);
        completed = true;
        return true;
      }

      if (!RunStep(1, "Refresh external pack exports", () =>
        RefreshExportedPackSetForStage(selection, contextLabel, logResult))) {
        return false;
      }

      if (!RunStep(2, "Stage active packs", () => {
        return StageActivePacksInternal(selection, logResult, contextLabel);
      })) {
        return false;
      }

      if (!RunStep(3, "Audit staged packs", () => AuditActivePacks(logResult))) {
        return false;
      }

      if (!RunStep(4, "Rebuild sprite runtime index", () =>
        SpriteIndexBuilder.RebuildRuntimeIndexPrepared(pipelineLabel, logResult, failOnError: false))) {
        return false;
      }

      completed = true;
      return true;
    }
    finally {
      if (completed) {
        EditorUtility.DisplayProgressBar("Content Packs", pipelineLabel + " complete.", 1f);
      }
      EditorUtility.ClearProgressBar();
      if (logResult) {
        var duration = (float)(EditorApplication.timeSinceStartup - startedAt);
        Debug.Log(
          "[ContentPackPipeline] [" + pipelineLabel + "] " + (completed ? "completed" : "aborted") +
          " in " + duration.ToString("0.00", CultureInfo.InvariantCulture) + "s."
        );
      }
    }
  }

  static void ShowContentPackProgress(string pipelineLabel, int stepIndex, int stepCount, string stepName) {
    var normalizedStepCount = Math.Max(stepCount, 1);
    var clampedStepIndex = Math.Max(Math.Min(stepIndex, normalizedStepCount), 1);
    var progress = normalizedStepCount <= 1 ? 0f : (float)(clampedStepIndex - 1) / normalizedStepCount;
    EditorUtility.DisplayProgressBar(
      "Content Packs",
      pipelineLabel + " " + clampedStepIndex + "/" + normalizedStepCount + ": " + (stepName ?? ""),
      progress
    );
  }

  static bool RefreshExportedPackSetForStage(ContentPackSelection selection, string contextLabel, bool logResult) {
    return RefreshExportedPackSetForStage(selection, contextLabel, logResult, TransitionPipelineMode.Clean, stats: null);
  }

  static bool RefreshExportedPackSetForStage(
    ContentPackSelection selection,
    string contextLabel,
    bool logResult,
    TransitionPipelineMode mode,
    ExportSyncStats stats
  ) {
    if (selection == null || !selection.ExternalContentEnabled) {
      return true;
    }

    if (logResult) {
      Debug.Log(
        "[ContentPackPipeline] Refreshing external pack exports before staging." +
        " mode='" + mode + "'" +
        " context='" + (contextLabel ?? "") + "'" +
        " external_root='" + NormalizeFullPath(selection.ExternalRoot) + "'"
      );
    }

    var exportOk = ExportPackSet(logResult, mode, stats);
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
    var stageCodeDependencies = CollectStageCodeDependencies(packById, activePackIds);
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
    summary.Append(" stage_code_refs=").Append(stageCodeDependencies.Count);
    summary.Append(" pack_policy_errors=").Append(packPolicyErrors.Count);
    summary.Append(" gameplay_core_errors=").Append(gameplayCoreErrors.Count);
    Debug.Log(summary.ToString());

    if (stageCodeDependencies.Count > 0) {
      LogInfoBucket("StageCodeRefs", stageCodeDependencies);
    }

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
      EnsureDirectoryAssetPath(StageFormsAssetPath);
      EnsureDirectoryAssetPath(StageGearsAssetPath);
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
    return EnsureSelectedPackDirectories(selection, contextLabel, logResult, TransitionPipelineMode.Clean, stats: null);
  }

  static bool EnsureSelectedPackDirectories(
    ContentPackSelection selection,
    string contextLabel,
    bool logResult,
    TransitionPipelineMode mode,
    ExportSyncStats stats
  ) {
    if (selection == null || !selection.ExternalContentEnabled) {
      return true;
    }

    if (DoActivePackDirectoriesExist(selection)) {
      return true;
    }

    LogMissingSelectedPackDirectories(selection, contextLabel);
    Debug.LogWarning(
      "[ContentPackPipeline] Missing external pack directories for active selection. Exporting the first pack set before staging." +
      " mode='" + mode + "'" +
      " context='" + contextLabel + "'"
    );
    return ExportPackSet(logResult, mode, stats);
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
    core.seedRoots.Add("Assets/Sprites/Characters/Esperanza/GroupedGearAtlases/Skin");
    core.seedRoots.Add("Assets/Sprites/Characters/Esperanza/Expressions/Base");
    core.seedRoots.Add("Assets/Sprites/Characters/Esperanza/_Bounces");
    core.seedRoots.Add("Assets/Prefabs/Fonts/FontCharacter.prefab");
    AddCoreUiOwnedRoots(core.seedRoots);
    foreach (var projectile in Projectiles.EnumerateAll()) {
      var projectilePrefabPath = NormalizeAssetPath(projectile.Value?.prefabAddress);
      if (string.IsNullOrWhiteSpace(projectilePrefabPath) ||
          string.Equals(projectilePrefabPath, "Assets/Prefabs/Projectiles/BlastBall.prefab", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }
      AddUniquePath(core.seedRoots, projectilePrefabPath);
    }
    core.manualLibraryNames.Add("Dialog/DialogEsper");
    core.ownedRoots.AddRange(core.seedRoots);

    var baseForm = new PackDefinition {
      packId = BaseFormPackId,
      kind = "form",
      externalRootPath = NormalizeFullPath(Path.Combine(normalizedRoot, "Forms", BaseFormPackId)),
      stageAssetRoot = StageFormsAssetPath + "/" + BaseFormPackId,
      defaultLocationId = ""
    };
    baseForm.requiredPackIds.Add(CorePackId);
    baseForm.seedRoots.Add("Assets/Prefabs/Projectiles/BlastBall.prefab");
    baseForm.seedRoots.Add("Assets/Sprites/Characters/Esperanza/Effects");
    baseForm.ownedRoots.AddRange(baseForm.seedRoots);

    var gearPacks = DiscoverGearPackDefinitions(normalizedRoot);

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
    slice.requiredPackIds.Add(BaseFormPackId);
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

    var result = new List<PackDefinition> { core, baseForm };
    result.AddRange(gearPacks);
    result.Add(slice);
    result.Add(homebase);
    result.Add(sunkenCave);
    result.Add(episode);
    return result;
  }

  static List<PackDefinition> DiscoverGearPackDefinitions(string normalizedExternalRoot) {
    var result = new List<PackDefinition>();
    var groupedGearRoot = NormalizeAssetPath(EsperanzaGroupedGearRoot);
    var groupedGearFullPath = Path.GetFullPath(groupedGearRoot);
    if (!Directory.Exists(groupedGearFullPath)) {
      return result;
    }

    var formDirectories = Directory.GetDirectories(groupedGearFullPath);
    Array.Sort(formDirectories, StringComparer.OrdinalIgnoreCase);
    for (var formIndex = 0; formIndex < formDirectories.Length; formIndex++) {
      var formAssetPath = ToProjectAssetPath(formDirectories[formIndex]);
      var formName = Path.GetFileName(formAssetPath);
      if (string.IsNullOrWhiteSpace(formName) ||
          string.Equals(formName, "Skin", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      var gearDirectories = Directory.GetDirectories(formDirectories[formIndex]);
      Array.Sort(gearDirectories, StringComparer.OrdinalIgnoreCase);
      for (var gearIndex = 0; gearIndex < gearDirectories.Length; gearIndex++) {
        var gearAssetPath = ToProjectAssetPath(gearDirectories[gearIndex]);
        var gearCode = Path.GetFileName(gearAssetPath);
        if (string.IsNullOrWhiteSpace(gearCode)) {
          continue;
        }

        var leafDirectories = Directory.GetDirectories(gearDirectories[gearIndex]);
        Array.Sort(leafDirectories, StringComparer.OrdinalIgnoreCase);
        for (var leafIndex = 0; leafIndex < leafDirectories.Length; leafIndex++) {
          var leafAssetPath = ToProjectAssetPath(leafDirectories[leafIndex]);
          var leafCode = Path.GetFileName(leafAssetPath);
          var packId = EquippedItems.BuildGearPackId(formName + "_" + gearCode, leafCode);
          if (string.IsNullOrWhiteSpace(packId)) {
            continue;
          }

          var pack = new PackDefinition {
            packId = packId,
            kind = "gear",
            externalRootPath = NormalizeFullPath(Path.Combine(normalizedExternalRoot, "Gears", packId)),
            stageAssetRoot = StageGearsAssetPath + "/" + packId,
            defaultLocationId = ""
          };
          pack.requiredPackIds.Add(CorePackId);
          pack.seedRoots.Add(leafAssetPath);
          pack.ownedRoots.Add(leafAssetPath);
          result.Add(pack);
        }
      }
    }

    return result;
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

      var assignedPackId = ResolveAssignedPackId(assetPath, pair.Value, packDefinitions);
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

  static string ResolveAssignedPackId(string assetPath, HashSet<string> usage, List<PackDefinition> packDefinitions) {
    var ownedPackId = ResolveOwnedPackId(assetPath, packDefinitions);
    if (!string.IsNullOrWhiteSpace(ownedPackId)) {
      return ownedPackId;
    }

    if (usage == null || usage.Count <= 0) return CorePackId;
    if (usage.Contains(CorePackId) || usage.Count > 1) return CorePackId;
    foreach (var packId in usage) return packId;
    return CorePackId;
  }

  static string ResolveOwnedPackId(string assetPath, List<PackDefinition> packDefinitions) {
    var normalizedAssetPath = NormalizeAssetPath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath) || packDefinitions == null || packDefinitions.Count <= 0) {
      return "";
    }

    string bestPackId = "";
    var bestMatchLength = -1;

    for (var packIndex = 0; packIndex < packDefinitions.Count; packIndex++) {
      var pack = packDefinitions[packIndex];
      if (pack == null || pack.ownedRoots == null || pack.ownedRoots.Count <= 0) {
        continue;
      }

      for (var rootIndex = 0; rootIndex < pack.ownedRoots.Count; rootIndex++) {
        var ownedRoot = NormalizeAssetPath(pack.ownedRoots[rootIndex]);
        if (string.IsNullOrWhiteSpace(ownedRoot)) {
          continue;
        }

        var isDirectMatch = string.Equals(normalizedAssetPath, ownedRoot, StringComparison.OrdinalIgnoreCase);
        var isUnderRoot = normalizedAssetPath.StartsWith(ownedRoot + "/", StringComparison.OrdinalIgnoreCase);
        if (!isDirectMatch && !isUnderRoot) {
          continue;
        }

        if (ownedRoot.Length < bestMatchLength) {
          continue;
        }

        if (ownedRoot.Length == bestMatchLength &&
            string.Equals(bestPackId, CorePackId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(pack.packId, CorePackId, StringComparison.OrdinalIgnoreCase)) {
          bestPackId = pack.packId;
          continue;
        }

        if (ownedRoot.Length > bestMatchLength) {
          bestMatchLength = ownedRoot.Length;
          bestPackId = pack.packId;
        }
      }
    }

    return bestPackId;
  }

  static void WriteAssignedAssets(
    Dictionary<string, AssignedAsset> assignedAssets,
    List<string> errors,
    TransitionPipelineMode mode,
    ExportSyncStats stats
  ) {
    var guidMap = BuildGuidMap(assignedAssets);
    var orderedAssets = assignedAssets.Values.OrderBy(asset => asset.externalAssetPath, StringComparer.OrdinalIgnoreCase).ToList();

    for (var i = 0; i < orderedAssets.Count; i++) {
      var assigned = orderedAssets[i];
      try {
        CopyAssetPayload(assigned.assetPath, assigned.externalAssetPath, guidMap, mode, stats);
        CopyMetaPayload(assigned.assetPath, assigned.externalAssetPath, guidMap, mode, stats);
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

  static void CopyAssetPayload(
    string sourceAssetPath,
    string targetFullPath,
    Dictionary<string, string> guidMap,
    TransitionPipelineMode mode,
    ExportSyncStats stats
  ) {
    var sourceFullPath = Path.GetFullPath(sourceAssetPath);
    EnsureDirectoryFullPath(Path.GetDirectoryName(targetFullPath));
    if (mode == TransitionPipelineMode.Smart && File.Exists(targetFullPath)) {
      if (stats != null) {
        stats.assetPayloadsSkipped++;
      }
      return;
    }

    if (ShouldRewriteTextFile(sourceFullPath)) {
      var text = File.ReadAllText(sourceFullPath);
      File.WriteAllText(targetFullPath, RewriteGuids(text, guidMap), new UTF8Encoding(false));
      if (stats != null) {
        stats.assetPayloadsWritten++;
      }
      return;
    }

    File.Copy(sourceFullPath, targetFullPath, overwrite: true);
    if (stats != null) {
      stats.assetPayloadsWritten++;
    }
  }

  static void CopyMetaPayload(
    string sourceAssetPath,
    string targetFullPath,
    Dictionary<string, string> guidMap,
    TransitionPipelineMode mode,
    ExportSyncStats stats
  ) {
    var sourceMetaFullPath = Path.GetFullPath(sourceAssetPath + ".meta");
    if (!File.Exists(sourceMetaFullPath)) {
      throw new FileNotFoundException("Missing meta file.", sourceMetaFullPath);
    }

    var targetMetaFullPath = targetFullPath + ".meta";
    if (mode == TransitionPipelineMode.Smart && File.Exists(targetMetaFullPath)) {
      if (stats != null) {
        stats.metaPayloadsSkipped++;
      }
      return;
    }

    var metaText = File.ReadAllText(sourceMetaFullPath);
    File.WriteAllText(targetMetaFullPath, RewriteGuids(metaText, guidMap), new UTF8Encoding(false));
    if (stats != null) {
      stats.metaPayloadsWritten++;
    }
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

  static void WriteGeneratedPackData(PackDefinition pack, List<string> errors, TransitionPipelineMode mode, ExportSyncStats stats) {
    if (pack == null) return;

    try {
      if (string.Equals(pack.packId, CorePackId, StringComparison.OrdinalIgnoreCase)) {
        WriteEsperanzaSnapshot(pack, mode, stats);
        return;
      }

      if (string.Equals(pack.packId, SlicePackId, StringComparison.OrdinalIgnoreCase)) {
        WriteDomeCitySnapshots(pack, mode, stats);
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

  static void WritePackManifest(PackDefinition pack, List<string> errors, TransitionPipelineMode mode, ExportSyncStats stats) {
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
      WriteJson(manifestPath, manifest, mode, stats, generatedFile: false);
    }
    catch (Exception ex) {
      errors.Add(
        "Failed to write pack manifest." +
        " pack_id='" + pack.packId + "'" +
        " error='" + ex.Message + "'"
      );
    }
  }

  static void WriteEsperanzaSnapshot(PackDefinition pack, TransitionPipelineMode mode, ExportSyncStats stats) {
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
    WriteJson(outputPath, snapshot, mode, stats, generatedFile: true);
  }

  static void WriteDomeCitySnapshots(PackDefinition pack, TransitionPipelineMode mode, ExportSyncStats stats) {
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

    WriteJson(Path.Combine(pack.externalRootPath, pack.snapshotRelativePath), locationSnapshot, mode, stats, generatedFile: true);
    WriteJson(Path.Combine(pack.externalRootPath, pack.dialogSnapshotRelativePath), dialogSnapshot, mode, stats, generatedFile: true);
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
      var activeForm = EsperanzaForms.GetActive();
      var equippedGearPackIds = ResolveEquippedGearPackIds(packById);
      Debug.Log(
        "[ContentPackPipeline] Generated active content registry." +
        " active_packs=" + string.Join(", ", activePackIds) +
        " active_form='" + (string.IsNullOrWhiteSpace(activeForm) ? "-" : activeForm) + "'" +
        " equipped_gear_packs=" + (equippedGearPackIds.Count <= 0 ? "-" : string.Join(", ", equippedGearPackIds)) +
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
    var stageCodeDependencies = CollectStageCodeDependencies(packById, activePackIds);
    var packPolicyErrors = CollectActivePackPolicyValidationErrors(packById, activePackIds);
    var gameplayCoreErrors = CollectGameplayCoreValidationErrors(selection, activePackIds);

    if (stageErrors.Count > 0) {
      LogErrors("stage_validation", stageErrors);
    }

    if (stageCodeDependencies.Count > 0) {
      LogInfoBucket("StageCodeRefs", stageCodeDependencies);
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
    var stageRoots = BuildActiveStageRoots(packById, activePackIds);

    for (var i = 0; i < activePackIds.Count; i++) {
      if (!packById.TryGetValue(activePackIds[i], out var pack) || pack == null) continue;
      var stagedOwnedRoots = ExpandStagedOwnedRoots(pack);
      for (var rootIndex = 0; rootIndex < stagedOwnedRoots.Count; rootIndex++) {
        ValidateStagedRoot(stagedOwnedRoots[rootIndex], stageRoots, errors);
      }
    }

    return errors;
  }

  static List<string> CollectStageCodeDependencies(Dictionary<string, PackDefinition> packById, List<string> activePackIds) {
    var codeDependencies = new List<string>();
    var stageRoots = BuildActiveStageRoots(packById, activePackIds);

    for (var i = 0; i < activePackIds.Count; i++) {
      if (!packById.TryGetValue(activePackIds[i], out var pack) || pack == null) continue;
      var stagedOwnedRoots = ExpandStagedOwnedRoots(pack);
      for (var rootIndex = 0; rootIndex < stagedOwnedRoots.Count; rootIndex++) {
        CollectCodeDependenciesForStagedRoot(stagedOwnedRoots[rootIndex], stageRoots, codeDependencies);
      }
    }

    return codeDependencies;
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

      ValidateActivePackAssetExists(projectileAssetPath, "projectile_prefab:" + projectile.Key, activePackIds, errors);
    }

    ValidateGameplayCoreAddressable(GameplayCoreAssetPaths.EsperanzaPrefabAssetPath, "player_prefab", errors);

    foreach (var projectile in Projectiles.EnumerateAll()) {
      var projectileAssetPath = NormalizeAssetPath(projectile.Value?.prefabAddress);
      if (string.IsNullOrWhiteSpace(projectileAssetPath)) continue;
      ValidateActivePackAddressable(projectileAssetPath, "projectile_prefab:" + projectile.Key, activePackIds, errors);
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

  static void ValidateActivePackAssetExists(string projectAssetPath, string label, List<string> activePackIds, List<string> errors) {
    var stagedAssetPath = ResolveStagedAssetPathForActivePacks(projectAssetPath, activePackIds);
    if (string.IsNullOrWhiteSpace(stagedAssetPath) || !File.Exists(Path.GetFullPath(stagedAssetPath))) {
      errors?.Add(
        "Missing staged active-pack asset." +
        " label='" + label + "'" +
        " project_path='" + NormalizeAssetPath(projectAssetPath) + "'" +
        " staged_path='" + stagedAssetPath + "'"
      );
    }
  }

  static void ValidateActivePackAddressable(string projectAssetPath, string label, List<string> activePackIds, List<string> errors) {
    var stagedAssetPath = ResolveStagedAssetPathForActivePacks(projectAssetPath, activePackIds);
    if (string.IsNullOrWhiteSpace(stagedAssetPath)) return;

    var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
    if (settings == null) {
      errors?.Add("Addressables settings were not found while validating active-pack asset '" + label + "'.");
      return;
    }

    var guid = AssetDatabase.AssetPathToGUID(stagedAssetPath);
    if (string.IsNullOrWhiteSpace(guid)) {
      errors?.Add(
        "Missing GUID for staged active-pack asset." +
        " label='" + label + "'" +
        " staged_path='" + stagedAssetPath + "'"
      );
      return;
    }

    var entry = settings.FindAssetEntry(guid);
    if (entry == null) {
      errors?.Add(
        "Missing Addressables entry for staged active-pack asset." +
        " label='" + label + "'" +
        " staged_path='" + stagedAssetPath + "'"
      );
      return;
    }

    if (!string.Equals(entry.address, stagedAssetPath, StringComparison.Ordinal)) {
      errors?.Add(
        "Addressables entry address mismatch for staged active-pack asset." +
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

  static string ResolveStagedAssetPathForActivePacks(string projectAssetPath, List<string> activePackIds) {
    var normalizedProjectPath = NormalizeAssetPath(projectAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedProjectPath) ||
        !normalizedProjectPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
        activePackIds == null ||
        activePackIds.Count <= 0) {
      return "";
    }

    var relativePath = normalizedProjectPath.Substring("Assets/".Length);
    for (var i = 0; i < activePackIds.Count; i++) {
      var stageRoot = GetStageAssetRoot(activePackIds[i]);
      if (string.IsNullOrWhiteSpace(stageRoot)) continue;
      var stagedAssetPath = NormalizeAssetPath(stageRoot + "/" + relativePath);
      if (File.Exists(Path.GetFullPath(stagedAssetPath))) {
        return stagedAssetPath;
      }
    }

    return "";
  }

  static HashSet<string> BuildActiveStageRoots(Dictionary<string, PackDefinition> packById, List<string> activePackIds) {
    var stageRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
      NormalizeAssetPath(StageCoreAssetPath)
    };
    if (packById == null || activePackIds == null) return stageRoots;

    for (var i = 0; i < activePackIds.Count; i++) {
      if (packById.TryGetValue(activePackIds[i], out var pack) && pack != null) {
        stageRoots.Add(NormalizeAssetPath(pack.stageAssetRoot));
      }
    }

    return stageRoots;
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

  static void CollectCodeDependenciesForStagedRoot(string stagedAssetPath, HashSet<string> stageRoots, List<string> output) {
    if (string.IsNullOrWhiteSpace(stagedAssetPath) || stageRoots == null || output == null) return;
    if (!File.Exists(Path.GetFullPath(stagedAssetPath))) return;

    var dependencies = AssetDatabase.GetDependencies(new[] { stagedAssetPath }, true);
    for (var i = 0; i < dependencies.Length; i++) {
      var dependency = NormalizeAssetPath(dependencies[i]);
      if (string.IsNullOrWhiteSpace(dependency) ||
          string.Equals(dependency, stagedAssetPath, StringComparison.OrdinalIgnoreCase) ||
          !dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
          AssetDatabase.IsValidFolder(dependency) ||
          !IsCodeDependency(dependency)) {
        continue;
      }

      output.Add("staged_asset='" + stagedAssetPath + "' dependency='" + dependency + "'");
      break;
    }
  }

  static void ValidateDependencyUnderStageRoots(
    string stagedAssetPath,
    string dependency,
    HashSet<string> stageRoots,
    List<string> errors
  ) {
    if (string.IsNullOrWhiteSpace(dependency) || stageRoots == null || errors == null) return;
    if (IsCodeDependency(dependency)) return;

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

    if (string.Equals(normalizedPackId, CorePackId, StringComparison.OrdinalIgnoreCase)) {
      return StageCoreAssetPath;
    }

    if (normalizedPackId.StartsWith("Form_", StringComparison.OrdinalIgnoreCase)) {
      return StageFormsAssetPath + "/" + normalizedPackId;
    }

    if (normalizedPackId.StartsWith("Gear_", StringComparison.OrdinalIgnoreCase)) {
      return StageGearsAssetPath + "/" + normalizedPackId;
    }

    return StageSlicesAssetPath + "/" + normalizedPackId;
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

    var equippedGearPackIds = ResolveEquippedGearPackIds(packById);
    for (var i = 0; i < equippedGearPackIds.Count; i++) {
      Visit(equippedGearPackIds[i]);
    }

    return resolved;
  }

  static List<string> ResolveEquippedGearPackIds(Dictionary<string, PackDefinition> packById) {
    var result = new List<string>();
    if (packById == null || packById.Count <= 0) {
      return result;
    }

    var equippedGearIds = EquippedItems.GetEquippedGearIds();
    if (equippedGearIds.Count <= 0) {
      return result;
    }

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in packById) {
      var pack = pair.Value;
      if (pack == null || !string.Equals(pack.kind, "gear", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (!EquippedItems.TryParseGearPackId(pack.packId, out var gearForm, out var gearCode, out _)) {
        continue;
      }

      var equippedGearId = NormalizeToken(gearForm + "_" + gearCode);
      if (string.IsNullOrWhiteSpace(equippedGearId) ||
          !equippedGearIds.Contains(equippedGearId, StringComparer.OrdinalIgnoreCase) ||
          !seen.Add(pack.packId)) {
        continue;
      }

      result.Add(pack.packId);
    }

    result.Sort(StringComparer.OrdinalIgnoreCase);
    return result;
  }

  static int RemoveInactiveStageLinks(List<string> activePackIds) {
    var removedCount = 0;
    EnsureDirectoryAssetPath(StageRootAssetPath);
    EnsureDirectoryAssetPath(StageFormsAssetPath);
    EnsureDirectoryAssetPath(StageGearsAssetPath);
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

    var existingFormDirectories = Directory.Exists(Path.GetFullPath(StageFormsAssetPath))
      ? Directory.GetDirectories(Path.GetFullPath(StageFormsAssetPath))
      : Array.Empty<string>();

    for (var i = 0; i < existingFormDirectories.Length; i++) {
      var assetPath = ToProjectAssetPath(existingFormDirectories[i]);
      var packId = Path.GetFileName(assetPath);
      var keep = activePackIds.Contains(packId, StringComparer.OrdinalIgnoreCase);
      if (RemoveStageLinkIfInactive(assetPath, keep)) {
        removedCount++;
      }
    }

    var existingGearDirectories = Directory.Exists(Path.GetFullPath(StageGearsAssetPath))
      ? Directory.GetDirectories(Path.GetFullPath(StageGearsAssetPath))
      : Array.Empty<string>();

    for (var i = 0; i < existingGearDirectories.Length; i++) {
      var assetPath = ToProjectAssetPath(existingGearDirectories[i]);
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

  static bool IsCodeDependency(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return false;
    var extension = Path.GetExtension(assetPath);
    return !string.IsNullOrWhiteSpace(extension) &&
           (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".asmdef", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".asmref", StringComparison.OrdinalIgnoreCase));
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

  static void PreparePackDirectory(string packRootPath, string externalRoot, TransitionPipelineMode mode, ExportSyncStats stats) {
    var normalizedPackRoot = NormalizeFullPath(packRootPath);
    var normalizedExternalRoot = NormalizeFullPath(externalRoot);

    if (!normalizedPackRoot.StartsWith(normalizedExternalRoot + "/", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(normalizedPackRoot, normalizedExternalRoot, StringComparison.OrdinalIgnoreCase)) {
      throw new InvalidOperationException("Pack root escaped external root. pack='" + normalizedPackRoot + "'");
    }

    if (mode == TransitionPipelineMode.Clean && Directory.Exists(normalizedPackRoot)) {
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

  static void EnsureDirectoryAssetPath(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return;
    var fullPath = Path.GetFullPath(assetPath);
    Directory.CreateDirectory(fullPath);
  }

  static void EnsureDirectoryFullPath(string fullPath) {
    if (string.IsNullOrWhiteSpace(fullPath)) return;
    Directory.CreateDirectory(fullPath);
  }

  static void WriteJson<T>(string fullPath, T payload, TransitionPipelineMode mode, ExportSyncStats stats, bool generatedFile) {
    EnsureDirectoryFullPath(Path.GetDirectoryName(fullPath));
    File.WriteAllText(fullPath, JsonUtility.ToJson(payload, prettyPrint: true), new UTF8Encoding(false));
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

  static OwnershipAnalysisReport AnalyzeOwnershipAndDuplicates(bool logResult) {
    var selection = LoadOrCreateSelectionAsset(logResult: false);
    var externalRoot = selection != null ? NormalizeFullPath(selection.ExternalRoot) : NormalizeFullPath(DefaultExternalRoot);
    var report = new OwnershipAnalysisReport {
      authoritativeExternalRoot = externalRoot,
      legacyGeneratedReferenceCount = CountLegacyGeneratedReferences(),
      spriteDuplicateCount = CountSpriteExternalDuplicates(externalRoot)
    };

    var packDefinitions = BuildPackDefinitions(externalRoot);
    for (var i = 0; i < packDefinitions.Count; i++) {
      AnalyzePackOwnership(packDefinitions[i], report);
    }

    report.stagedProjectTreeDependencyCount = CountStageDependenciesOutsideStageRoots(packDefinitions);
    report.stagedDependencyLeaks.Clear();
    CollectStageDependencyLeaks(packDefinitions, report.stagedDependencyLeaks);
    report.stagedCodeDependencyCount = CountStageCodeDependenciesOutsideStageRoots(packDefinitions);
    report.stagedCodeDependencies.Clear();
    CollectStageCodeDependencies(packDefinitions, report.stagedCodeDependencies);
    report.ownershipViolationCount =
      report.coreFindings.Count +
      report.formFindings.Count +
      report.gearFindings.Count +
      report.sliceFindings.Count +
      report.episodeFindings.Count +
      report.legacyFindings.Count +
      report.unknownFindings.Count;

    if (logResult) {
      LogOwnershipAnalysisReport(report);
    }

    return report;
  }

  static bool AuditLegacyDependencies(bool logResult) {
    return AuditLegacyDependencies(report: null, logResult);
  }

  static bool AuditLegacyDependencies(OwnershipAnalysisReport report, bool logResult) {
    report ??= AnalyzeOwnershipAndDuplicates(logResult);
    var auditOk = AuditActivePacks(logResult);
    var analysisOk =
      report.legacyGeneratedReferenceCount <= 0 &&
      report.stagedProjectTreeDependencyCount <= 0 &&
      report.ownershipViolationCount <= 0;

    if (logResult && report.spriteDuplicateCount > 0) {
      Debug.Log(
        "[ContentPackPipeline] Duplicate sprite assets remain as transition debt. " +
        "duplicate_assets=" + report.spriteDuplicateCount +
        " duplicate assets are reported but do not block the migration pass.");
    }

    return auditOk && analysisOk;
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

  static int CountSpriteExternalDuplicates(string authoritativeExternalRoot) {
    var spritesRoot = NormalizeFullPath("Assets/Sprites");
    var externalRoot = NormalizeFullPath(authoritativeExternalRoot);
    if (!Directory.Exists(spritesRoot) || !Directory.Exists(externalRoot)) return 0;

    var externalSpriteRoots = BuildPackDefinitions(externalRoot)
      .Where(pack => pack != null && !string.IsNullOrWhiteSpace(pack.externalRootPath))
      .Select(pack => NormalizeFullPath(Path.Combine(pack.externalRootPath, "Sprites")))
      .Where(Directory.Exists)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();
    if (externalSpriteRoots.Count <= 0) return 0;

    var files = Directory.GetFiles(spritesRoot, "*", SearchOption.AllDirectories);
    var duplicateCount = 0;
    for (var i = 0; i < files.Length; i++) {
      var fullPath = NormalizeFullPath(files[i]);
      if (Directory.Exists(fullPath)) continue;
      var relativePath = fullPath.Substring(spritesRoot.Length).TrimStart('/');
      if (string.IsNullOrWhiteSpace(relativePath)) continue;

      for (var rootIndex = 0; rootIndex < externalSpriteRoots.Count; rootIndex++) {
        var externalMatch = NormalizeFullPath(Path.Combine(externalSpriteRoots[rootIndex], relativePath));
        if (!File.Exists(externalMatch)) continue;
        duplicateCount++;
        break;
      }
    }

    return duplicateCount;
  }

  static void AnalyzePackOwnership(PackDefinition pack, OwnershipAnalysisReport report) {
    if (pack == null || report == null) return;
    if (IsPlaceholderPack(pack)) {
      report.placeholderExemptionCount++;
      report.placeholderFindings.Add(
        "Placeholder ownership checks deferred. pack_id='" + pack.packId + "'"
      );
      return;
    }

    var findings = GetOwnershipFindingsBucket(pack, report);
    if (findings == null) return;

    if (pack.ownedRoots == null || pack.ownedRoots.Count <= 0) {
      findings.Add("Pack has no declared owned roots. pack_id='" + pack.packId + "'");
    }

    if (string.Equals(pack.kind, "slice", StringComparison.OrdinalIgnoreCase)) {
      if (pack.ownedLocations == null || pack.ownedLocations.Count <= 0) {
        findings.Add("Slice has no owned location. pack_id='" + pack.packId + "'");
      }
      if (pack.dialogIds == null || pack.dialogIds.Count <= 0) {
        findings.Add("Slice has no dialog ownership declared. pack_id='" + pack.packId + "'");
      }
      if (pack.warmProfiles == null || pack.warmProfiles.Count <= 0) {
        findings.Add("Slice has no warm profile ownership declared. pack_id='" + pack.packId + "'");
      }
    }

    if (string.Equals(pack.kind, "episode", StringComparison.OrdinalIgnoreCase) &&
        (pack.requiredPackIds == null || pack.requiredPackIds.Count <= 0)) {
      findings.Add("Episode has no slice dependencies declared. pack_id='" + pack.packId + "'");
    }
  }

  static List<string> GetOwnershipFindingsBucket(PackDefinition pack, OwnershipAnalysisReport report) {
    if (pack == null || report == null) return null;
    if (string.Equals(pack.packId, CorePackId, StringComparison.OrdinalIgnoreCase)) return report.coreFindings;
    if (string.Equals(pack.kind, "form", StringComparison.OrdinalIgnoreCase)) return report.formFindings;
    if (string.Equals(pack.kind, "gear", StringComparison.OrdinalIgnoreCase)) return report.gearFindings;
    if (string.Equals(pack.kind, "slice", StringComparison.OrdinalIgnoreCase)) return report.sliceFindings;
    if (string.Equals(pack.kind, "episode", StringComparison.OrdinalIgnoreCase)) return report.episodeFindings;
    return report.unknownFindings;
  }

  static bool IsPlaceholderPack(PackDefinition pack) {
    if (pack == null || string.IsNullOrWhiteSpace(pack.packId)) return false;
    return pack.packId.IndexOf("Placeholder", StringComparison.OrdinalIgnoreCase) >= 0 ||
           string.Equals(pack.packId, EpisodePackId, StringComparison.OrdinalIgnoreCase);
  }

  static int CountStageDependenciesOutsideStageRoots(List<PackDefinition> packDefinitions) {
    var stageRoots = BuildStageRoots(packDefinitions);
    var stageRootFullPath = Path.GetFullPath(StageRootAssetPath);
    if (!Directory.Exists(stageRootFullPath)) return 0;

    var files = Directory.GetFiles(stageRootFullPath, "*", SearchOption.AllDirectories);
    var count = 0;
    for (var i = 0; i < files.Length; i++) {
      var assetPath = ToProjectAssetPath(files[i]);
      if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
      if (AssetDatabase.IsValidFolder(assetPath)) continue;
      var dependencies = AssetDatabase.GetDependencies(new[] { assetPath }, true);
      for (var dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++) {
        var dependency = NormalizeAssetPath(dependencies[dependencyIndex]);
        if (string.IsNullOrWhiteSpace(dependency) ||
            string.Equals(dependency, assetPath, StringComparison.OrdinalIgnoreCase) ||
            !dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            IsCodeDependency(dependency)) {
          continue;
        }

        var underStage = false;
        foreach (var stageRoot in stageRoots) {
          if (dependency.StartsWith(stageRoot + "/", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(dependency, stageRoot, StringComparison.OrdinalIgnoreCase)) {
            underStage = true;
            break;
          }
        }

        if (!underStage) {
          count++;
          break;
        }
      }
    }

    return count;
  }

  static int CountStageCodeDependenciesOutsideStageRoots(List<PackDefinition> packDefinitions) {
    var stageRoots = BuildStageRoots(packDefinitions);
    var stageRootFullPath = Path.GetFullPath(StageRootAssetPath);
    if (!Directory.Exists(stageRootFullPath)) return 0;

    var files = Directory.GetFiles(stageRootFullPath, "*", SearchOption.AllDirectories);
    var count = 0;
    for (var i = 0; i < files.Length; i++) {
      var assetPath = ToProjectAssetPath(files[i]);
      if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
      if (AssetDatabase.IsValidFolder(assetPath)) continue;
      var dependencies = AssetDatabase.GetDependencies(new[] { assetPath }, true);
      for (var dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++) {
        var dependency = NormalizeAssetPath(dependencies[dependencyIndex]);
        if (string.IsNullOrWhiteSpace(dependency) ||
            string.Equals(dependency, assetPath, StringComparison.OrdinalIgnoreCase) ||
            !dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            !IsCodeDependency(dependency)) {
          continue;
        }

        if (IsUnderStageRoots(dependency, stageRoots)) continue;

        count++;
        break;
      }
    }

    return count;
  }

  static void CollectStageDependencyLeaks(List<PackDefinition> packDefinitions, List<string> output) {
    if (output == null) return;

    var stageRoots = BuildStageRoots(packDefinitions);
    var stageRootFullPath = Path.GetFullPath(StageRootAssetPath);
    if (!Directory.Exists(stageRootFullPath)) return;

    var files = Directory.GetFiles(stageRootFullPath, "*", SearchOption.AllDirectories);
    for (var i = 0; i < files.Length; i++) {
      var assetPath = ToProjectAssetPath(files[i]);
      if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
      if (AssetDatabase.IsValidFolder(assetPath)) continue;
      var dependencies = AssetDatabase.GetDependencies(new[] { assetPath }, true);
      for (var dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++) {
        var dependency = NormalizeAssetPath(dependencies[dependencyIndex]);
        if (string.IsNullOrWhiteSpace(dependency) ||
            string.Equals(dependency, assetPath, StringComparison.OrdinalIgnoreCase) ||
            !dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            IsCodeDependency(dependency)) {
          continue;
        }

        if (IsUnderStageRoots(dependency, stageRoots)) continue;

        output.Add("staged_asset='" + assetPath + "' dependency='" + dependency + "'");
        break;
      }
    }
  }

  static void CollectStageCodeDependencies(List<PackDefinition> packDefinitions, List<string> output) {
    if (output == null) return;

    var stageRoots = BuildStageRoots(packDefinitions);
    var stageRootFullPath = Path.GetFullPath(StageRootAssetPath);
    if (!Directory.Exists(stageRootFullPath)) return;

    var files = Directory.GetFiles(stageRootFullPath, "*", SearchOption.AllDirectories);
    for (var i = 0; i < files.Length; i++) {
      var assetPath = ToProjectAssetPath(files[i]);
      if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
      if (AssetDatabase.IsValidFolder(assetPath)) continue;
      var dependencies = AssetDatabase.GetDependencies(new[] { assetPath }, true);
      for (var dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++) {
        var dependency = NormalizeAssetPath(dependencies[dependencyIndex]);
        if (string.IsNullOrWhiteSpace(dependency) ||
            string.Equals(dependency, assetPath, StringComparison.OrdinalIgnoreCase) ||
            !dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            !IsCodeDependency(dependency)) {
          continue;
        }

        if (IsUnderStageRoots(dependency, stageRoots)) continue;

        output.Add("staged_asset='" + assetPath + "' dependency='" + dependency + "'");
        break;
      }
    }
  }

  static HashSet<string> BuildStageRoots(List<PackDefinition> packDefinitions) {
    var stageRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
      NormalizeAssetPath(StageCoreAssetPath)
    };
    if (packDefinitions == null) return stageRoots;

    for (var i = 0; i < packDefinitions.Count; i++) {
      var pack = packDefinitions[i];
      if (pack == null || string.IsNullOrWhiteSpace(pack.stageAssetRoot)) continue;
      stageRoots.Add(NormalizeAssetPath(pack.stageAssetRoot));
    }

    return stageRoots;
  }

  static bool IsUnderStageRoots(string dependency, HashSet<string> stageRoots) {
    if (string.IsNullOrWhiteSpace(dependency) || stageRoots == null || stageRoots.Count <= 0) return false;
    foreach (var stageRoot in stageRoots) {
      if (dependency.StartsWith(stageRoot + "/", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(dependency, stageRoot, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static void LogOwnershipAnalysisReport(OwnershipAnalysisReport report) {
    if (report == null) return;

    Debug.Log(
      "[ContentPackPipeline] [TransitionAnalysis] legacy_generated_refs=" + report.legacyGeneratedReferenceCount +
      " sprite_duplicates=" + report.spriteDuplicateCount +
      " staged_project_tree_dependencies=" + report.stagedProjectTreeDependencyCount +
      " staged_code_dependencies=" + report.stagedCodeDependencyCount +
      " ownership_findings=" + report.ownershipViolationCount +
      " placeholder_exemptions=" + report.placeholderExemptionCount +
      " authoritative_external_root='" + NormalizeFullPath(report.authoritativeExternalRoot) + "'" +
      " stage_root='" + NormalizeAssetPath(StageRootAssetPath) + "'"
    );
    LogFindingBucket("Core", report.coreFindings);
    LogFindingBucket("Form", report.formFindings);
    LogFindingBucket("Gear", report.gearFindings);
    LogFindingBucket("Slice", report.sliceFindings);
    LogFindingBucket("Episode", report.episodeFindings);
    LogFindingBucket("Legacy/Unknown", report.legacyFindings.Concat(report.unknownFindings).ToList());
    LogInfoBucket("Placeholder", report.placeholderFindings);
    LogInfoBucket("StageLeaks", report.stagedDependencyLeaks);
    LogInfoBucket("StageCodeRefs", report.stagedCodeDependencies);
  }

  static void LogFindingBucket(string label, List<string> findings) {
    if (findings == null || findings.Count <= 0) return;
    for (var i = 0; i < findings.Count; i++) {
      Debug.LogWarning("[ContentPackPipeline] [TransitionAnalysis][" + label + "] " + findings[i]);
    }
  }

  static void LogInfoBucket(string label, List<string> findings) {
    if (findings == null || findings.Count <= 0) return;
    for (var i = 0; i < findings.Count; i++) {
      Debug.Log("[ContentPackPipeline] [TransitionAnalysis][" + label + "] " + findings[i]);
    }
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
