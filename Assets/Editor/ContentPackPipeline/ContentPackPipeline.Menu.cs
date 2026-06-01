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
  [MenuItem("Tools/Content Pack/1) Build Active Content (Smart)")]
  public static void BuildActiveContentSmartMenu() {
    RunFullMigrationPass(logResult: true, TransitionPipelineMode.Smart);
  }

  [MenuItem("Tools/Content Pack/2) Build Active Content (Clean)")]
  public static void BuildActiveContentCleanMenu() {
    RunFullMigrationPass(logResult: true, TransitionPipelineMode.Clean);
  }

  public static void AnalyzeOwnershipAndDuplicatesMenu() {
    AnalyzeOwnershipAndDuplicates(logResult: true);
  }

  public static void ExportMissingPackContentMenu() {
    RunExportTransitionStep(logResult: true, TransitionPipelineMode.Smart);
  }

  public static void StageTransitionActivePacksMenu() {
    RunStageTransitionStep(logResult: true, TransitionPipelineMode.Smart);
  }

  public static void AuditLegacyDependenciesMenu() {
    AuditLegacyDependencies(logResult: true);
  }

  public static void RebuildTransitionRuntimeIndexMenu() {
    RunRebuildRuntimeIndexTransitionStep(logResult: true);
  }

  public static void BuildTransitionAddressablesMenu() {
    RunBuildAddressablesTransitionStep(logResult: true, cleanCachesBeforeBuild: false);
  }

  public static void FullMigrationPassSmartMenu() {
    RunFullMigrationPass(logResult: true, TransitionPipelineMode.Smart);
  }

  public static void FullMigrationPassCleanMenu() {
    RunFullMigrationPass(logResult: true, TransitionPipelineMode.Clean);
  }

  public static void ExportFirstPackSetMenu() {
    ExportFirstPackSet(logResult: true);
  }

  public static void StageActivePacksMenu() {
    StageActivePacks(logResult: true);
  }

  public static void AuditActivePacksMenu() {
    AuditActivePacks(logResult: true);
  }

  public static void StageAuditAndRebuildRuntimeIndexMenu() {
    RunPrepareActivePacksPipeline(logResult: true);
  }

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
}
#endif
