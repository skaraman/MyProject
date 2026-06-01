#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    Debug.Log(
      "[TrimAtlasExport] Exported atlas." +
      " source='" + sourcePath + "'" +
      " outputs=" + (pendingExports?.Count ?? 0) +
      " first_output='" + (pendingExports != null && pendingExports.Count > 0 ? pendingExports[0].exportedAtlasAssetPath : "") + "'" +
      " sprites=" + (pendingExports?.Sum(export => export?.exportData?.sprites?.Count ?? 0) ?? 0) +
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
    var deferredWritePhaseStarted = false;

    try {
      BeginDeferredTrimmedWritePhase(sourceFolderPath, exportBatches.Count);
      deferredWritePhaseStarted = true;

      for (var i = 0; i < exportBatches.Count; i++) {
        var batch = exportBatches[i];
        var sourcePath = batch.primarySourcePath;
        var outputFolderPath = ResolveOutputFolderPath(sourcePath, sourceFolderPath);
        if (string.IsNullOrWhiteSpace(outputFolderPath)) {
          failedCount++;
          AddFailureLog(failureLogs, sourcePath, "Could not resolve an output folder.");
          continue;
        }

        if (!TryAnalyzeSourceAtlasBatch(batch, outputFolderPath, out var exportData, out var buildItems, out var error)) {
          failedCount++;
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
          AddFailureLog(failureLogs, sourcePath, error);
          continue;
        }

        pendingExportCount += batchPendingExports.Count;
        exportedCount += batchPendingExports.Count;
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

        deletedSourceCount += DeletePackedSourceAssets(batch, batchPendingExports[0].exportedAtlasAssetPath);
        DeleteLegacyGeneratedOutputs(batch, batch.outputName, batchPendingExports[0].exportedAtlasAssetPath);
      }
    }
    finally {
      if (deferredWritePhaseStarted) {
        EndDeferredTrimmedWritePhase(sourceFolderPath, pendingExportCount, failedCount);
      }
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

  static int DeletePackedSourceAssets(SourceAtlasExportBatch batch, string exportedAtlasPath) {
    if (batch == null || batch.sourcePaths == null || batch.sourcePaths.Count <= 0) return 0;

    var deletedCount = 0;
    var normalizedExportedAtlasPath = NormalizeAssetPath(exportedAtlasPath);
    for (var sourceIndex = 0; sourceIndex < batch.sourcePaths.Count; sourceIndex++) {
      var sourcePath = NormalizeAssetPath(batch.sourcePaths[sourceIndex]);
      if (string.IsNullOrWhiteSpace(sourcePath)) continue;
      if (string.Equals(sourcePath, normalizedExportedAtlasPath, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (!File.Exists(Path.GetFullPath(sourcePath))) continue;
      if (!AssetDatabase.DeleteAsset(sourcePath)) {
        Debug.LogWarning("[TrimAtlasExport] Failed to delete packed source atlas. asset='" + sourcePath + "'");
        continue;
      }

      deletedCount++;
    }

    return deletedCount;
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
