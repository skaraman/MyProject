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

  static bool ExportContentPackInterfaceInfoCsv(ContentPackSelection selection, bool logResult) {
    return RunContentPackIterationCommand(
      selection,
      "--export-interface-info-csv",
      "generate the content-pack recovery CSV before building",
      "Generated content-pack recovery CSV.",
      logResult
    );
  }

  static bool RecoverBuildCleanPackState(ContentPackSelection selection, bool logResult) {
    var recovered = RunContentPackIterationCommand(
      selection,
      "--recover-build-clean",
      "recover the deleted content package before Build Clean",
      "Recovered Build Clean package state.",
      logResult
    );
    if (recovered) {
      AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }
    return recovered;
  }

  static bool RunContentPackIterationCommand(
    ContentPackSelection selection,
    string commandArgument,
    string failureDescription,
    string successDescription,
    bool logResult
  ) {
    if (selection == null) return false;

    var projectRoot = GetProjectRoot();
    var scriptPath = Path.Combine(projectRoot, "Tools", "ContentPackIterationUI.py");
    if (!File.Exists(scriptPath)) {
      Debug.LogError("[ContentPackPipeline] Missing Content Pack UI script. path='" + scriptPath + "'");
      return false;
    }

    var arguments = QuoteCliArgument(scriptPath) +
                    " --project-root " + QuoteCliArgument(projectRoot) +
                    " --external-root " + QuoteCliArgument(selection.ExternalRoot) +
                    " " + commandArgument;
    var errors = new List<string>();
    if (TryRunContentPackIterationCommand("py", "-3 " + arguments, projectRoot, successDescription, logResult, out var error)) {
      return true;
    }
    errors.Add(error);
    if (TryRunContentPackIterationCommand("python", arguments, projectRoot, successDescription, logResult, out error)) {
      return true;
    }
    errors.Add(error);
    if (TryRunContentPackIterationCommand("python3", arguments, projectRoot, successDescription, logResult, out error)) {
      return true;
    }
    errors.Add(error);

    Debug.LogError(
      "[ContentPackPipeline] Failed to " + failureDescription + ".\n" +
      string.Join("\n", errors)
    );
    return false;
  }

  static bool TryRunContentPackIterationCommand(
    string executable,
    string arguments,
    string projectRoot,
    string successDescription,
    bool logResult,
    out string error
  ) {
    error = "";

    try {
      var startInfo = new ProcessStartInfo {
        FileName = executable,
        Arguments = arguments,
        WorkingDirectory = projectRoot,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
      };

      using var process = Process.Start(startInfo);
      if (process == null) {
        error = executable + ": process did not start.";
        return false;
      }

      var output = process.StandardOutput.ReadToEnd().Trim();
      var standardError = process.StandardError.ReadToEnd().Trim();
      process.WaitForExit();
      if (process.ExitCode != 0) {
        error = executable + ": exit_code=" + process.ExitCode +
                (string.IsNullOrWhiteSpace(standardError) ? "" : " error='" + standardError + "'");
        return false;
      }

      if (logResult) {
        Debug.Log(
          "[ContentPackPipeline] " + successDescription +
          " command='" + executable + "'" +
          (string.IsNullOrWhiteSpace(output) ? "" : " " + output)
        );
      }
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
    var stepCount = mode == TransitionPipelineMode.Clean ? 10 : 9;
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
      var selection = LoadOrCreateSelectionAsset(logResult);
      if (selection == null) {
        return false;
      }

      if (mode == TransitionPipelineMode.Clean &&
          !RunStep("Recover deleted package definitions", () =>
            RecoverBuildCleanPackState(selection, logResult))) return false;

      if (!RunStep("Snapshot pack setup CSV", () =>
        ExportContentPackInterfaceInfoCsv(selection, logResult))) return false;

      if (!RunStep("Analyze ownership + duplicates", () => {
        summary.analysis = AnalyzeOwnershipAndDuplicates(logResult);
        return summary.analysis != null;
      })) return false;

      if (!RunStep("Export external pack content", () =>
        RefreshExportedPackSetForStage(selection, "full_migration_pass", logResult, mode, summary.export))) return false;

      if (!RunStep("Stage active packs", () => {
        summary.stageCompleted = StageActivePacksInternal(selection, logResult, "full_migration_pass");
        return summary.stageCompleted;
      })) return false;

      if (!RunStep("Audit legacy dependencies", () => {
        summary.analysis = AnalyzeOwnershipAndDuplicates(logResult);
        summary.auditCompleted =
          summary.analysis != null &&
          AuditLegacyDependencies(summary.analysis, logResult);
        return summary.auditCompleted;
      })) return false;

      if (mode == TransitionPipelineMode.Smart) {
        var anyExportChanges = summary.export != null && (
          summary.export.assetPayloadsWritten > 0 ||
          summary.export.metaPayloadsWritten > 0 ||
          summary.export.generatedFilesWritten > 0 ||
          summary.export.manifestsWritten > 0 ||
          summary.export.destinationEntriesDeleted > 0 ||
          summary.export.packDirectoriesCreated > 0 ||
          summary.export.packDirectoriesRecreated > 0
        );

        if (!anyExportChanges) {
          if (logResult) {
            Debug.Log("[ContentPackPipeline] [" + pipelineLabel + "] mode='Smart' detected no exported pack changes. Skipping downstream Addressables/Runtime builds.");
          }
          return true;
        }
      }

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
