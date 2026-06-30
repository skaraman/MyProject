#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed partial class TrimmedAtlasExporterWindow : EditorWindow {
  const string DefaultOutputPrefix = "trimmed";
  const int MaxExportSpriteSliceCount = 1024;
  internal const string EditorMetadataSuffix = ".editor.json";
  static readonly string[] SupportedSourceExtensions = { ".png" };

  Texture2D sourceTexture;
  DefaultAsset sourceFolder;
  DefaultAsset outputFolder;
  bool writePrefixedCopy;
  string outputPrefix = DefaultOutputPrefix;
  int cellWidth = 192;
  int cellHeight = 192;
  int maxAtlasWidth = 2048;
  int padding = 1;
  int alphaThreshold = 1;
  bool treatNearWhiteAsEmpty;
  int nearWhiteThreshold = 250;
  bool ignoreDistantStrayIslands = true;
  int strayIslandGapCutoffPx = 24;
  int strayIslandMaxPixels = 6;
  bool preserveSpriteNames = true;
  bool createAtlasSlices = true;
  bool includeSubfolders = true;
  bool hideEmptySlices = true;
  Vector2 scrollPosition;
  Vector2 sliceListScrollPosition;
  int selectedSliceIndex = -1;

  TrimmedAtlasExport analyzedAtlas;
  List<TrimmedSpriteBuildData> analyzedBuildItems;
  Texture2D analyzedPreviewTexture;
  string analyzedSourcePath = "";
  string analyzedSettingsSignature = "";
  bool[] analysisVisibleMaskScratch;
  bool[] analysisVisitedScratch;
  int[] analysisQueueScratch;

  [MenuItem("Tools/Authoring/Trim Atlas + Export Offsets")]
  static void ShowWindow() {
    GetWindow<TrimmedAtlasExporterWindow>("Trim Atlas Export");
  }

  void OnGUI() {
    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

    EditorGUILayout.LabelField("Trim Atlas + Export Offsets", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "Imported sprite slices are trimmed independently when present. If no slice layout exists, the configured fixed-size grid is used. The JSON exports the exact local offset needed to place the cropped sprite where it originally appeared inside its original slot.",
      MessageType.Info);

    EditorGUI.BeginChangeCheck();
    sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Source Atlas", sourceTexture, typeof(Texture2D), false);
    sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField("Source Folder", sourceFolder, typeof(DefaultAsset), false);
    using (new EditorGUI.DisabledScope(sourceFolder == null)) {
      includeSubfolders = EditorGUILayout.Toggle("Include Subfolders", includeSubfolders);
    }
    cellWidth = Mathf.Max(1, EditorGUILayout.DelayedIntField("Cell Width", cellWidth));
    cellHeight = Mathf.Max(1, EditorGUILayout.DelayedIntField("Cell Height", cellHeight));
    maxAtlasWidth = Mathf.Max(64, EditorGUILayout.DelayedIntField("Max Atlas Width", maxAtlasWidth));
    padding = Mathf.Clamp(EditorGUILayout.DelayedIntField("Packing Padding", padding), 0, 64);
    alphaThreshold = Mathf.Clamp(EditorGUILayout.IntSlider("Alpha Threshold", alphaThreshold, 0, 255), 0, 255);
    treatNearWhiteAsEmpty = EditorGUILayout.Toggle("Treat Near-White As Empty", treatNearWhiteAsEmpty);
    using (new EditorGUI.DisabledScope(!treatNearWhiteAsEmpty)) {
      nearWhiteThreshold = Mathf.Clamp(EditorGUILayout.IntSlider("Near-White Threshold", nearWhiteThreshold, 0, 255), 0, 255);
    }
    ignoreDistantStrayIslands = EditorGUILayout.Toggle("Ignore Distant Stray Islands", ignoreDistantStrayIslands);
    using (new EditorGUI.DisabledScope(!ignoreDistantStrayIslands)) {
      strayIslandGapCutoffPx = Mathf.Clamp(EditorGUILayout.IntSlider("Stray Gap Cutoff", strayIslandGapCutoffPx, 0, 256), 0, 1024);
      strayIslandMaxPixels = Mathf.Clamp(EditorGUILayout.IntSlider("Stray Max Pixels", strayIslandMaxPixels, 1, 64), 1, 256);
    }

    preserveSpriteNames = EditorGUILayout.Toggle("Preserve Sprite Names", preserveSpriteNames);
    if (EditorGUI.EndChangeCheck()) {
      InvalidateAnalysis();
    }

    outputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", outputFolder, typeof(DefaultAsset), false);
    if (outputFolder != null && string.IsNullOrWhiteSpace(ResolveConfiguredOutputFolderPath())) {
      EditorGUILayout.HelpBox("Output Folder must be a project folder asset. Leave it empty to export beside the source atlas.", MessageType.Warning);
    }
    writePrefixedCopy = EditorGUILayout.Toggle("Write Prefixed Copy", writePrefixedCopy);
    using (new EditorGUI.DisabledScope(!writePrefixedCopy)) {
      outputPrefix = EditorGUILayout.DelayedTextField("Output Prefix", outputPrefix ?? "");
    }
    if (writePrefixedCopy && string.IsNullOrWhiteSpace(GetSanitizedOutputPrefix())) {
      EditorGUILayout.HelpBox("Write Prefixed Copy requires a valid prefix. Example: 'trimmed' writes 'trimmed_1.png'.", MessageType.Warning);
    }
    EditorGUILayout.HelpBox(
      "By default export writes '<source>.png' in the destination folder, which overwrites the original atlas when exporting beside it. Enable Write Prefixed Copy to write '<prefix>_<source>.png' instead. This tool only exports color PNG sources and skips '_N' normal atlas variants.",
      MessageType.None);
    createAtlasSlices = EditorGUILayout.Toggle("Slice Exported Atlas", createAtlasSlices);

    using (new EditorGUI.DisabledScope(sourceTexture == null)) {
      using (new EditorGUILayout.HorizontalScope()) {
        if (GUILayout.Button("Analyze Slice Offsets")) {
          AnalyzeSelectedAtlasForPreview();
        }

        if (GUILayout.Button("Export Trimmed Atlas + JSON")) {
          ExportSelectedAtlas();
        }
      }
    }

    using (new EditorGUI.DisabledScope(!TryGetSourceFolderPath(out _, false))) {
      if (GUILayout.Button(includeSubfolders ? "Export Folder + Subfolders" : "Export Folder")) {
        ExportSelectedFolder();
      }
    }

    DrawAnalysisPreview();
    EditorGUILayout.EndScrollView();
  }

  void AnalyzeSelectedAtlasForPreview() {
    if (!TryGetSourcePath(out var sourcePath, true)) return;
    var outputFolderPath = ResolveOutputFolderPath(sourcePath);
    if (string.IsNullOrWhiteSpace(outputFolderPath)) {
      EditorUtility.DisplayDialog("Invalid Output Folder", "Choose a valid project folder for the export.", "OK");
      return;
    }

    if (!TryAnalyzeSourceAtlas(sourcePath, outputFolderPath, out var exportData, out var buildItems, out var previewTexture, out var error)) {
      EditorUtility.DisplayDialog("Analyze Failed", error, "OK");
      return;
    }

    CacheAnalysis(sourcePath, exportData, buildItems, previewTexture);
    SelectDefaultSlice();
  }

  void ExportSelectedAtlas() {
    if (!TryGetSourcePath(out var sourcePath, true)) return;
    if (!ValidateOutputNamingOptions()) {
      return;
    }
    var outputFolderPath = ResolveOutputFolderPath(sourcePath);
    if (string.IsNullOrWhiteSpace(outputFolderPath)) {
      EditorUtility.DisplayDialog("Invalid Output Folder", "Choose a valid project folder for the export.", "OK");
      return;
    }

    if (!EnsureAnalysisAvailable(sourcePath, outputFolderPath, out var error)) {
      EditorUtility.DisplayDialog("Trim Export Failed", error, "OK");
      return;
    }

    var exportData = analyzedAtlas;
    List<PendingTrimmedAtlasExport> pendingExports = null;
    var deferredWritePhaseStarted = false;
    try {
      BeginDeferredTrimmedWritePhase(sourcePath, 1);
      deferredWritePhaseStarted = true;
      if (!TryWriteAtlasExports(
            sourcePath,
            outputFolderPath,
            Path.GetFileNameWithoutExtension(sourcePath),
            exportData,
            analyzedBuildItems,
            out pendingExports,
            out error)) {
        EditorUtility.DisplayDialog("Trim Export Failed", error, "OK");
        return;
      }
    }
    finally {
      if (deferredWritePhaseStarted) {
        EndDeferredTrimmedWritePhase(sourcePath, pendingExports?.Count ?? 0, pendingExports == null || pendingExports.Count <= 0 ? 1 : 0);
      }
    }

    if (!ApplyDeferredImportMetadata(sourcePath, pendingExports, forceReimport: true, out error)) {
      EditorUtility.DisplayDialog("Trim Export Failed", error, "OK");
      return;
    }

    DeleteSingleSourceAssetAfterSuccessfulExport(sourcePath, pendingExports);

    var exportedSpriteCount = CountExportedSprites(pendingExports);

    Debug.Log(
      "[TrimAtlasExport] Exported atlas." +
      " source='" + sourcePath + "'" +
      " outputs=" + (pendingExports?.Count ?? 0) +
      " first_output='" + (pendingExports != null && pendingExports.Count > 0 ? pendingExports[0].exportedAtlasAssetPath : "") + "'" +
      " sprites=" + exportedSpriteCount +
      " empty=" + exportData.emptyCellCount +
      " deferred_import=True");
  }

  void ExportSelectedFolder() {
    if (!TryGetSourceFolderPath(out var sourceFolderPath, true)) return;
    if (!ValidateOutputNamingOptions()) {
      return;
    }

    var exportBatches = CollectSourceAtlasExportBatches(sourceFolderPath);
    if (exportBatches.Count <= 0) {
      EditorUtility.DisplayDialog("No Source Atlases", "No supported source atlases were found in the selected folder.", "OK");
      return;
    }

    var sourceAtlasCount = 0;
    for (var batchIndex = 0; batchIndex < exportBatches.Count; batchIndex++) {
      sourceAtlasCount += exportBatches[batchIndex].sourcePaths.Count;
    }

    Debug.Log(
      "[TrimAtlasExport] Starting folder export." +
      " source_folder='" + sourceFolderPath + "'" +
      " include_subfolders=" + includeSubfolders +
      " configured_output_root='" + ResolveConfiguredOutputFolderPath() + "'" +
      " source_atlas_count=" + sourceAtlasCount +
      " export_batch_count=" + exportBatches.Count);

    var exportedCount = 0;
    var deletedSourceCount = 0;
    var failedCount = 0;
    var failureLogs = new List<string>();
    var pendingExportCount = 0;
    var batchStartIndex = 0;
    while (batchStartIndex < exportBatches.Count) {
      var folderBatchPath = ResolveBatchFolderPath(exportBatches[batchStartIndex]);
      var batchEndIndex = FindFolderBatchEndIndex(exportBatches, batchStartIndex, folderBatchPath);
      var folderPendingExportCount = 0;
      var folderFailedCount = 0;
      var folderPendingImports = new List<PendingTrimmedAtlasExport>();
      var folderPendingSourceCleanups = new List<PendingTrimmedSourceCleanup>();

      Debug.Log(
        "[TrimAtlasExport] Starting folder export phase." +
        " source_folder='" + folderBatchPath + "'" +
        " export_batch_count=" + (batchEndIndex - batchStartIndex));

      BeginDeferredTrimmedWritePhase(folderBatchPath, batchEndIndex - batchStartIndex);
      try {
        for (var i = batchStartIndex; i < batchEndIndex; i++) {
          var batch = exportBatches[i];
          var sourcePath = batch.primarySourcePath;
          var outputFolderPath = ResolveOutputFolderPath(sourcePath, sourceFolderPath);
          if (string.IsNullOrWhiteSpace(outputFolderPath)) {
            failedCount++;
            folderFailedCount++;
            AddFailureLog(failureLogs, sourcePath, "Could not resolve an output folder.");
            continue;
          }

          if (!TryAnalyzeSourceAtlasBatch(batch, outputFolderPath, out var exportData, out var buildItems, out var error)) {
            failedCount++;
            folderFailedCount++;
            AddFailureLog(failureLogs, sourcePath, error);
            continue;
          }

          if (!TryWriteAtlasExports(
                sourcePath,
                outputFolderPath,
                batch.outputName,
                exportData,
                buildItems,
                out var batchPendingExports,
                out error)) {
            failedCount++;
            folderFailedCount++;
            AddFailureLog(failureLogs, sourcePath, error);
            continue;
          }

          pendingExportCount += batchPendingExports.Count;
          folderPendingExportCount += batchPendingExports.Count;
          exportedCount += batchPendingExports.Count;
          folderPendingImports.AddRange(batchPendingExports);
          folderPendingSourceCleanups.Add(new PendingTrimmedSourceCleanup {
            batch = batch,
            exportedAtlasAssetPath = batchPendingExports[0].exportedAtlasAssetPath
          });
          if (batchPendingExports.Count > 1) {
            Debug.Log(
              "[TrimAtlasExport] Folder export split oversized atlas batch into pages." +
              " source='" + sourcePath + "'" +
              " source_count=" + batch.sourcePaths.Count +
              " grouped_numeric=" + batch.groupedNumericSiblings +
              " outputs=" + batchPendingExports.Count +
              " first_output='" + batchPendingExports[0].exportedAtlasAssetPath + "'" +
              " last_output='" + batchPendingExports[batchPendingExports.Count - 1].exportedAtlasAssetPath + "'");
          }
        }
      }
      finally {
        EndDeferredTrimmedWritePhase(folderBatchPath, folderPendingExportCount, folderFailedCount);
      }

      var metadataApplied = ApplyDeferredImportMetadata(folderBatchPath, folderPendingImports, forceReimport: true, out var importMetadataError);
      if (!metadataApplied) {
        failedCount += folderPendingImports.Count;
        folderFailedCount += folderPendingImports.Count;
        AddFailureLog(failureLogs, folderBatchPath, importMetadataError);
      }
      else {
        for (var cleanupIndex = 0; cleanupIndex < folderPendingSourceCleanups.Count; cleanupIndex++) {
          var pendingCleanup = folderPendingSourceCleanups[cleanupIndex];
          if (pendingCleanup?.batch == null || string.IsNullOrWhiteSpace(pendingCleanup.exportedAtlasAssetPath)) continue;
          deletedSourceCount += DeletePackedSourceAssets(pendingCleanup.batch, pendingCleanup.exportedAtlasAssetPath);
          DeleteLegacyGeneratedOutputs(pendingCleanup.batch, pendingCleanup.batch.outputName, pendingCleanup.exportedAtlasAssetPath);
        }
      }

      ReleaseFolderExportMemory(folderBatchPath, folderPendingExportCount, folderFailedCount);
      batchStartIndex = batchEndIndex;
    }

    var summary =
      "Processed " + sourceAtlasCount + " source atlas(es) in " + exportBatches.Count + " export batch(es)." +
      " Exported " + exportedCount + ", deleted_sources " + deletedSourceCount + ", failed " + failedCount + ", deferred_import=True.";
    Debug.Log("[TrimAtlasExport] Folder export complete. " + summary);
    for (var i = 0; i < failureLogs.Count; i++) {
      Debug.LogWarning("[TrimAtlasExport] " + failureLogs[i]);
    }

    EditorUtility.DisplayDialog(
      "Folder Export Complete",
      failedCount > 0 ? summary + "\nSee Console for the first " + failureLogs.Count + " failure(s)." : summary,
      "OK");
  }

  static void BeginDeferredTrimmedWritePhase(string sourcePath, int exportCount) {
    AssetDatabase.StartAssetEditing();
    Debug.Log(
      "[TrimAtlasExport] Deferred import write phase started." +
      " source='" + sourcePath + "'" +
      " exports=" + exportCount);
  }

  static void EndDeferredTrimmedWritePhase(string sourcePath, int pendingExportCount, int failureCount) {
    AssetDatabase.StopAssetEditing();
    Debug.Log(
      "[TrimAtlasExport] Deferred import write phase completed." +
      " source='" + sourcePath + "'" +
      " pending_exports=" + pendingExportCount +
      " failures=" + failureCount);
  }

  static string ResolveBatchFolderPath(SourceAtlasExportBatch batch) {
    if (batch == null) return "";
    if (!string.IsNullOrWhiteSpace(batch.sourceFolderPath)) {
      return NormalizeAssetPath(batch.sourceFolderPath);
    }

    return NormalizeAssetPath(Path.GetDirectoryName(batch.primarySourcePath));
  }

  static int FindFolderBatchEndIndex(List<SourceAtlasExportBatch> exportBatches, int startIndex, string folderPath) {
    if (exportBatches == null) return startIndex;

    var normalizedFolderPath = NormalizeAssetPath(folderPath);
    var endIndex = startIndex;
    while (endIndex < exportBatches.Count) {
      if (!string.Equals(ResolveBatchFolderPath(exportBatches[endIndex]), normalizedFolderPath, StringComparison.OrdinalIgnoreCase)) {
        break;
      }

      endIndex++;
    }

    return endIndex;
  }

  static void ReleaseFolderExportMemory(string folderPath, int pendingExportCount, int failureCount) {
    GC.Collect();
    EditorUtility.UnloadUnusedAssetsImmediate();
    GC.Collect();
    Debug.Log(
      "[TrimAtlasExport] Folder export cleanup complete." +
      " source_folder='" + folderPath + "'" +
      " pending_exports=" + pendingExportCount +
      " failures=" + failureCount);
  }

  static bool ApplyDeferredImportMetadata(string contextPath, List<PendingTrimmedAtlasExport> pendingExports, bool forceReimport, out string error) {
    error = "";
    if (pendingExports == null || pendingExports.Count <= 0) {
      return true;
    }

    var reimportPaths = forceReimport ? new List<string>(pendingExports.Count) : null;
    var seenReimportPaths = forceReimport ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;
    for (var i = 0; i < pendingExports.Count; i++) {
      var pendingExport = pendingExports[i];
      if (pendingExport == null) {
        continue;
      }

      if (!GeneratedAtlasImportMetadataStore.TryWrite(
            pendingExport.exportedAtlasAssetPath,
            pendingExport.editorImportMetadataJson,
            false,
            out var metadataChanged,
            out error)) {
        error =
          "Failed to store generated atlas import metadata for '" +
          (pendingExport.exportedAtlasAssetPath ?? contextPath ?? "") +
          "': " + error;
        return false;
      }

      if (!forceReimport) {
        continue;
      }

      var normalizedExportedAtlasPath = NormalizeAssetPath(pendingExport.exportedAtlasAssetPath);
      if (!metadataChanged) {
        continue;
      }
      if (string.IsNullOrWhiteSpace(normalizedExportedAtlasPath) || !seenReimportPaths.Add(normalizedExportedAtlasPath)) {
        continue;
      }

      reimportPaths.Add(normalizedExportedAtlasPath);
    }

    if (forceReimport && !GeneratedAtlasImportMetadataStore.TryBatchReimport(contextPath, reimportPaths, out error)) {
      return false;
    }

    return true;
  }

  static int CountExportedSprites(List<PendingTrimmedAtlasExport> pendingExports) {
    if (pendingExports == null || pendingExports.Count <= 0) {
      return 0;
    }

    var spriteCount = 0;
    for (var i = 0; i < pendingExports.Count; i++) {
      spriteCount += pendingExports[i]?.exportData?.sprites?.Count ?? 0;
    }

    return spriteCount;
  }

  static int DeleteSingleSourceAssetAfterSuccessfulExport(string sourcePath, List<PendingTrimmedAtlasExport> pendingExports) {
    var normalizedSourcePath = NormalizeAssetPath(sourcePath);
    if (string.IsNullOrWhiteSpace(normalizedSourcePath) || pendingExports == null || pendingExports.Count <= 0) {
      return 0;
    }

    for (var i = 0; i < pendingExports.Count; i++) {
      var exportedAtlasAssetPath = NormalizeAssetPath(pendingExports[i]?.exportedAtlasAssetPath);
      if (string.Equals(exportedAtlasAssetPath, normalizedSourcePath, StringComparison.OrdinalIgnoreCase)) {
        return 0;
      }
    }

    var deletedCount = DeletePackedSourceAsset(normalizedSourcePath, "");
    if (deletedCount > 0) {
      Debug.Log(
        "[TrimAtlasExport] Deleted original source atlas after successful export." +
        " source='" + normalizedSourcePath + "'" +
        " outputs=" + pendingExports.Count);
    }

    return deletedCount;
  }

  static int DeletePackedSourceAssets(SourceAtlasExportBatch batch, string exportedAtlasPath) {
    if (batch == null || batch.sourcePaths == null || batch.sourcePaths.Count <= 0) return 0;

    var deletedCount = 0;
    for (var sourceIndex = 0; sourceIndex < batch.sourcePaths.Count; sourceIndex++) {
      deletedCount += DeletePackedSourceAsset(batch.sourcePaths[sourceIndex], exportedAtlasPath);
    }

    return deletedCount;
  }

  static int DeletePackedSourceAsset(string sourcePath, string exportedAtlasPath) {
    var normalizedSourcePath = NormalizeAssetPath(sourcePath);
    if (string.IsNullOrWhiteSpace(normalizedSourcePath)) return 0;

    var normalizedExportedAtlasPath = NormalizeAssetPath(exportedAtlasPath);
    if (!string.IsNullOrWhiteSpace(normalizedExportedAtlasPath) &&
        string.Equals(normalizedSourcePath, normalizedExportedAtlasPath, StringComparison.OrdinalIgnoreCase)) {
      return 0;
    }

    if (!File.Exists(Path.GetFullPath(normalizedSourcePath))) return 0;
    if (!AssetDatabase.DeleteAsset(normalizedSourcePath)) {
      Debug.LogWarning("[TrimAtlasExport] Failed to delete packed source atlas. asset='" + normalizedSourcePath + "'");
      return 0;
    }

    return 1;
  }

  bool EnsureAnalysisAvailable(string sourcePath, string outputFolderPath, out string error) {
    error = "";
    if (HasFreshAnalysis(sourcePath)) return true;

    if (!TryAnalyzeSourceAtlas(sourcePath, outputFolderPath, out var exportData, out var buildItems, out var previewTexture, out error)) {
      return false;
    }

    CacheAnalysis(sourcePath, exportData, buildItems, previewTexture);
    SelectDefaultSlice();
    return true;
  }

  bool ValidateOutputNamingOptions() {
    if (!writePrefixedCopy) return true;
    var sanitizedPrefix = GetSanitizedOutputPrefix();
    if (!string.IsNullOrWhiteSpace(sanitizedPrefix)) return true;

    EditorUtility.DisplayDialog(
      "Trim Export Failed",
      "Write Prefixed Copy requires a valid prefix. Example: 'trimmed' writes 'trimmed_1.png'.",
      "OK");
    return false;
  }

}
#endif
