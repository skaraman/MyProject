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
    return RefreshExportedPackSetForStage(selection, contextLabel, logResult, TransitionPipelineMode.Smart, stats: null);
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

  public static IReadOnlyList<string> GetSpriteLibrarySearchRoots() {
    var roots = new List<string>();
    AppendStageSearchRoots(roots, includeSpriteLibraries: true, includeTextures: false);
    if (!IsExternalContentEnabled()) {
      AddUniquePath(roots, SpriteStreamingConfig.SourceRootFolder);
    }
    return roots;
  }

  public static IReadOnlyList<string> GetTextureSearchRoots() {
    var roots = new List<string>();
    AppendStageSearchRoots(roots, includeSpriteLibraries: false, includeTextures: true);
    if (!IsExternalContentEnabled()) {
      AddUniquePath(roots, SpriteStreamingConfig.TextureSourceRootFolder);
    }
    return roots;
  }

  public static IReadOnlyList<string> GetActiveStageAssetRoots() {
    var roots = new List<string>();

    var selection = AssetDatabase.LoadAssetAtPath<ContentPackSelection>(SelectionAssetPath);
    if (selection == null || !selection.ExternalContentEnabled) return roots;

    var packDefinitions = BuildPackDefinitions(selection.ExternalRoot);
    var packById = packDefinitions.ToDictionary(pack => pack.packId, StringComparer.OrdinalIgnoreCase);
    var activePackIds = ResolveConcreteActivePackIds(selection.GetNormalizedActivePackIds(), packById);

    for (var i = 0; i < activePackIds.Count; i++) {
      var stageRoot = GetStageAssetRoot(activePackIds[i], packById);
      if (string.IsNullOrWhiteSpace(stageRoot)) continue;
      AddUniquePath(roots, stageRoot);
    }

    return roots;
  }

  static bool IsExternalContentEnabled() {
    var selection = AssetDatabase.LoadAssetAtPath<ContentPackSelection>(SelectionAssetPath);
    return selection != null && selection.ExternalContentEnabled;
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
    for (var i = 0; i < selectedPackIds.Count; i++) {
      if (!packById.TryGetValue(selectedPackIds[i], out var pack)) {
        Debug.LogError("[ContentPackPipeline] Unknown selected pack id '" + selectedPackIds[i] + "'.");
        return false;
      }
    }

    var activePackIds = ResolveConcreteActivePackIds(selectedPackIds, packById);
    if (activePackIds.Count <= 0) {
      WriteInactiveRegistryAsset(logResult);
      return true;
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
      EnsureDirectoryAssetPath(StageEpisodesAssetPath);
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

      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
      ActiveContentRegistryRuntime.ForceReload();
      SpriteIndexBuilder.ClearCachedSpriteSliceEstimates("content_pack_stage:" + contextLabel);

      var runtimeAddressablesChanged = LocationAddressablesBootstrap.SyncLocationAddressables(
        logResult: false,
        saveAndRefresh: false
      );
      if (IsGameplayContentRequested(packById, activePackIds)) {
        runtimeAddressablesChanged |= GameplayPlayerAddressablesBootstrap.SyncGameplayPlayerAddressables(
          logResult: false,
          saveAndRefresh: false
        );
        runtimeAddressablesChanged |= ProjectileAddressablesBootstrap.SyncProjectileAddressables(
          logResult: false,
          saveAndRefresh: false
        );
        runtimeAddressablesChanged |= RuntimeMaterialAddressablesBootstrap.SyncRuntimeMaterialAddressables(
          logResult: false,
          saveAndRefresh: false
        );
      }
      if (runtimeAddressablesChanged) {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
      }

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
    return EnsureSelectedPackDirectories(selection, contextLabel, logResult, TransitionPipelineMode.Smart, stats: null);
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

      Debug.LogWarning(
        "[ContentPackPipeline] Active pack export is missing." +
        " context='" + contextLabel + "'" +
        " pack_id='" + pack.packId + "'" +
        " expected_path='" + pack.externalRootPath + "'"
      );
    }
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

  public static string ResolveStageAssetRoot(string packId) {
    return GetStageAssetRoot(packId);
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

    if (normalizedPackId.StartsWith("Episode_", StringComparison.OrdinalIgnoreCase)) {
      return StageEpisodesAssetPath + "/" + normalizedPackId;
    }

    return StageRootAssetPath + "/" + normalizedPackId;
  }

  static int RemoveInactiveStageLinks(List<string> activePackIds) {
    var removedCount = 0;
    EnsureDirectoryAssetPath(StageRootAssetPath);
    EnsureDirectoryAssetPath(StageFormsAssetPath);
    EnsureDirectoryAssetPath(StageGearsAssetPath);
    EnsureDirectoryAssetPath(StageSlicesAssetPath);
    EnsureDirectoryAssetPath(StageEpisodesAssetPath);

    if (RemoveStageLinkIfInactive(StageCoreAssetPath, activePackIds.Contains(CorePackId, StringComparer.OrdinalIgnoreCase))) {
      removedCount++;
    }

    var knownStageFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
      Path.GetFileName(StageCoreAssetPath),
      Path.GetFileName(StageFormsAssetPath),
      Path.GetFileName(StageGearsAssetPath),
      Path.GetFileName(StageSlicesAssetPath),
      Path.GetFileName(StageEpisodesAssetPath)
    };
    var existingPackDirectories = Directory.Exists(Path.GetFullPath(StageRootAssetPath))
      ? Directory.GetDirectories(Path.GetFullPath(StageRootAssetPath))
      : Array.Empty<string>();

    for (var i = 0; i < existingPackDirectories.Length; i++) {
      var assetPath = ToProjectAssetPath(existingPackDirectories[i]);
      var packId = Path.GetFileName(assetPath);
      if (knownStageFolders.Contains(packId)) continue;
      var keep = activePackIds.Contains(packId, StringComparer.OrdinalIgnoreCase);
      if (RemoveStageLinkIfInactive(assetPath, keep)) {
        removedCount++;
      }
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

    var existingEpisodeDirectories = Directory.Exists(Path.GetFullPath(StageEpisodesAssetPath))
      ? Directory.GetDirectories(Path.GetFullPath(StageEpisodesAssetPath))
      : Array.Empty<string>();

    for (var i = 0; i < existingEpisodeDirectories.Length; i++) {
      var assetPath = ToProjectAssetPath(existingEpisodeDirectories[i]);
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

    if (string.Equals(stageFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase)) {
      EnsureDirectoryFullPath(targetFullPath);
      reused = true;
      return false;
    }

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
}
#endif
