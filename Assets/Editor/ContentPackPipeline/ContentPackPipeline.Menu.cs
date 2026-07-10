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
  [MenuItem("Tools/Content Packs/Open UI")]
  public static void OpenContentPackIterationUiFromMenu() {
    var projectRoot = GetProjectRoot();
    var scriptPath = Path.Combine(projectRoot, "Tools", "ContentPackIterationUI.py");
    if (!File.Exists(scriptPath)) {
      EditorUtility.DisplayDialog("Content Pack UI", "Missing UI script:\n" + scriptPath, "OK");
      Debug.LogError("[ContentPackPipeline] Missing Content Pack UI script. path='" + scriptPath + "'");
      return;
    }

    var error = "";
    if (TryLaunchContentPackIterationUi("py", "-3", scriptPath, projectRoot, out error)) return;
    if (TryLaunchContentPackIterationUi("pythonw", "", scriptPath, projectRoot, out error)) return;
    if (TryLaunchContentPackIterationUi("python", "", scriptPath, projectRoot, out error)) return;

    EditorUtility.DisplayDialog(
      "Content Pack UI",
      "Failed to launch Content Pack UI.\n\n" + error,
      "OK"
    );
    Debug.LogError("[ContentPackPipeline] Failed to launch Content Pack UI. " + error);
  }

  [MenuItem("Tools/Content Packs/Build Smart")]
  public static void BuildActiveContentSmartFromMenu() {
    BuildActiveContentSmart();
  }

  public static void BuildActiveContentSmart() {
    RunFullMigrationPass(logResult: true, TransitionPipelineMode.Smart);
  }

  [MenuItem("Tools/Content Packs/Build Clean")]
  public static void BuildActiveContentCleanFromMenu() {
    BuildActiveContentClean();
  }

  public static void BuildActiveContentClean() {
    RunFullMigrationPass(logResult: true, TransitionPipelineMode.Clean);
  }

  [MenuItem("Tools/Content Packs/Build Finalize")]
  public static void BuildActiveContentFinalizeFromMenu() {
    BuildActiveContentFinalize();
  }

  public static void BuildActiveContentFinalize() {
    var selection = LoadOrCreateSelectionAsset(true);
    if (selection == null || !selection.ExternalContentEnabled) return;

    if (!EditorUtility.DisplayDialog(
      "Finalize Content",
      "This will permanently delete exported assets from the Unity project folder.\n\n" +
      "These assets will still exist in the external MyProjectContent directory. " +
      "Ensure you have a backup or are using version control before proceeding.",
      "Delete Assets",
      "Cancel"
    )) {
      return;
    }

    var externalRoot = NormalizeFullPath(selection.ExternalRoot);
    var packDefinitions = BuildPackDefinitions(externalRoot);
    var projectLibraries = DiscoverProjectLibraryPaths();
    var errors = new List<string>();

    EditorUtility.DisplayProgressBar("Finalize Content", "Discovering dependencies...", 0f);

    try {
      for (var i = 0; i < packDefinitions.Count; i++) {
        PreparePackDependencies(packDefinitions[i], projectLibraries, errors);
      }

      var assignedAssets = AssignPackAssets(packDefinitions, errors);
      if (errors.Count > 0) {
        LogErrors("finalize_assignment", errors);
        return;
      }

      int count = 0;
      int total = assignedAssets.Count;
      int iAsset = 0;

      AssetDatabase.StartAssetEditing();

      foreach (var kvp in assignedAssets) {
        var assetPath = kvp.Key;
        iAsset++;

        if (iAsset % 50 == 0) {
          EditorUtility.DisplayProgressBar("Finalize Content", "Deleting source assets...", (float)iAsset / total);
        }

        if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
          if (AssetDatabase.DeleteAsset(assetPath)) {
            count++;
          }
        }
      }

      RemoveEmptyDirectories("Assets");

      Debug.Log("[ContentPackPipeline] Build Finalize removed " + count + " source assets from the project and cleaned up empty folders.");
    }
    catch (Exception ex) {
      Debug.LogError("[ContentPackPipeline] Finalize failed.\n" + ex);
    }
    finally {
      AssetDatabase.StopAssetEditing();
      EditorUtility.ClearProgressBar();
      AssetDatabase.Refresh();
    }
  }

  static void RemoveEmptyDirectories(string directoryPath) {
    var fullDirectoryPath = Path.GetFullPath(directoryPath);
    if (!Directory.Exists(fullDirectoryPath)) return;

    var subDirectories = Directory.GetDirectories(fullDirectoryPath);
    foreach (var subDir in subDirectories) {
      RemoveEmptyDirectories(subDir);
    }

    var entries = Directory.GetFileSystemEntries(fullDirectoryPath);
    var hasMeaningfulFiles = false;
    foreach (var entry in entries) {
      if (entry.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
      hasMeaningfulFiles = true;
      break;
    }

    if (!hasMeaningfulFiles) {
      var assetPath = NormalizeAssetPath(directoryPath);
      if (!string.IsNullOrWhiteSpace(assetPath) && assetPath != "Assets") {
        AssetDatabase.DeleteAsset(assetPath);
      }
    }
  }

  static bool TryLaunchContentPackIterationUi(
    string executable,
    string prefixArguments,
    string scriptPath,
    string projectRoot,
    out string error
  ) {
    error = "";

    try {
      var arguments = "";
      if (!string.IsNullOrWhiteSpace(prefixArguments)) {
        arguments = prefixArguments + " ";
      }

      arguments += QuoteCliArgument(scriptPath);
      arguments += " --project-root ";
      arguments += QuoteCliArgument(projectRoot);

      var startInfo = new ProcessStartInfo {
        FileName = executable,
        Arguments = arguments,
        WorkingDirectory = projectRoot,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      Process.Start(startInfo);
      Debug.Log("[ContentPackPipeline] Opened Content Pack UI using '" + executable + "'.");
      return true;
    }
    catch (Exception ex) {
      error = executable + ": " + ex.Message;
      return false;
    }
  }

  static string QuoteCliArgument(string value) {
    if (string.IsNullOrEmpty(value)) {
      return "\"\"";
    }

    return "\"" + value.Replace("\"", "\\\"") + "\"";
  }

  public static bool PrepareSelectedPacksForRuntimeIndex(string contextLabel, bool logResult) {
    return PrepareSelectedPacksForRuntimeIndex(contextLabel, logResult, TransitionPipelineMode.Smart);
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

  static bool RunBuildAddressablesTransitionStep(bool logResult, bool cleanCachesBeforeBuild) {
    return SpriteIndexBuilder.BuildAddressablesContentPrepared(
      "Content Pipeline Transition",
      logResult,
      cleanCachesBeforeBuild,
      useChunkedWarmup: true
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
