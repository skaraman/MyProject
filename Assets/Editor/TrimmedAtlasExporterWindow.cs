#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public sealed class TrimmedAtlasExporterWindow : EditorWindow {
  const string DefaultOutputPrefix = "trimmed";
  const int MaxExportSpriteSliceCount = 1024;
  static readonly string[] SupportedSourceExtensions = { ".png" };

  [Serializable]
  sealed class TrimmedAtlasExport {
    public string sourceAtlasAssetPath;
    public string exportedAtlasAssetPath;
    public string coordinateOrigin = "bottom-left";
    public int sourceAtlasCount = 1;
    public int sourceWidth;
    public int sourceHeight;
    public int cellWidth;
    public int cellHeight;
    public int columns;
    public int rows;
    public int padding;
    public int atlasWidth;
    public int atlasHeight;
    public int emptyCellCount;
    public float packedAreaPctOfSource;
    public List<TrimmedSpriteMetadata> sprites = new();
  }

  [Serializable]
  sealed class TrimmedSpriteMetadata {
    public int index;
    public string name;
    public bool empty;
    public PixelRect sourceCell;
    public PixelRect trimRectInCell;
    public PixelRect packedRect;
    public PixelPoint offsetFromCellCenterPx;
    public PixelPoint weightedCenterOffsetPx;
  }

  [Serializable]
  struct PixelRect {
    public int x;
    public int y;
    public int width;
    public int height;

    public PixelRect(int x, int y, int width, int height) {
      this.x = x;
      this.y = y;
      this.width = width;
      this.height = height;
    }
  }

  [Serializable]
  struct PixelPoint {
    public float x;
    public float y;

    public PixelPoint(float x, float y) {
      this.x = x;
      this.y = y;
    }
  }

  sealed class TrimmedSpriteBuildData {
    public TrimmedSpriteMetadata metadata;
    public Color32[] trimmedPixels;

    public int Width => Math.Max(1, metadata.trimRectInCell.width);
    public int Height => Math.Max(1, metadata.trimRectInCell.height);
  }

  sealed class SourceAtlasExportBatch {
    public string primarySourcePath;
    public string outputName;
    public bool groupedNumericSiblings;
    public List<string> sourcePaths = new();
  }

  sealed class SourceCellDefinition {
    public PixelRect sourceCell;
    public Rect logicalCellRect;
    public string spriteName;
  }

  sealed class PendingTrimmedAtlasExport {
    public SourceAtlasExportBatch batch;
    public string sourceAtlasAssetPath;
    public string exportedAtlasAssetPath;
    public string metadataAssetPath;
    public TrimmedAtlasExport exportData;
  }

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

  [MenuItem("Tools/Sprite Streaming/Trim Atlas + Export Offsets")]
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

    var failureLogs = new List<string>();
    var finalizedExports = FinalizeWrittenAtlasExports(pendingExports, failureLogs);
    if (finalizedExports.Count <= 0) {
      var message = failureLogs.Count > 0 ? failureLogs[0] : "Unity import failed for the written atlas export.";
      EditorUtility.DisplayDialog("Trim Export Failed", message, "OK");
      return;
    }

    Debug.Log(
      "[TrimAtlasExport] Exported atlas." +
      " source='" + sourcePath + "'" +
      " outputs=" + finalizedExports.Count +
      " first_output='" + finalizedExports[0].exportedAtlasAssetPath + "'" +
      " sprites=" + finalizedExports.Sum(export => export?.exportData?.sprites?.Count ?? 0) +
      " empty=" + exportData.emptyCellCount);
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
    var pendingExports = new List<PendingTrimmedAtlasExport>();
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

        for (var pendingIndex = 0; pendingIndex < batchPendingExports.Count; pendingIndex++) {
          var pendingExport = batchPendingExports[pendingIndex];
          pendingExport.batch = batch;
          pendingExports.Add(pendingExport);
        }

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
      }
    }
    finally {
      if (deferredWritePhaseStarted) {
        EndDeferredTrimmedWritePhase(sourceFolderPath, pendingExports.Count, failedCount);
      }
    }

    if (pendingExports.Count > 0) {
      var finalizedExports = FinalizeWrittenAtlasExports(pendingExports, failureLogs);
      failedCount += pendingExports.Count - finalizedExports.Count;
      var expectedPageCountsByBatch = new Dictionary<SourceAtlasExportBatch, int>();
      var finalizedPageCountsByBatch = new Dictionary<SourceAtlasExportBatch, int>();
      var finalizedExportPathByBatch = new Dictionary<SourceAtlasExportBatch, string>();
      for (var i = 0; i < pendingExports.Count; i++) {
        var batch = pendingExports[i]?.batch;
        if (batch == null) continue;
        expectedPageCountsByBatch[batch] = expectedPageCountsByBatch.TryGetValue(batch, out var count) ? count + 1 : 1;
      }

      for (var i = 0; i < finalizedExports.Count; i++) {
        var batch = finalizedExports[i]?.batch;
        if (batch == null) continue;
        finalizedPageCountsByBatch[batch] = finalizedPageCountsByBatch.TryGetValue(batch, out var count) ? count + 1 : 1;
        if (!finalizedExportPathByBatch.ContainsKey(batch)) {
          finalizedExportPathByBatch[batch] = finalizedExports[i].exportedAtlasAssetPath;
        }
      }

      foreach (var pair in expectedPageCountsByBatch) {
        var batch = pair.Key;
        if (batch == null) continue;
        if (!finalizedPageCountsByBatch.TryGetValue(batch, out var finalizedPageCount) || finalizedPageCount != pair.Value) {
          if (finalizedPageCount > 0) {
            Debug.LogWarning(
              "[TrimAtlasExport] Skipping source cleanup because not all export pages finalized." +
              " source='" + batch.primarySourcePath + "'" +
              " finalized_pages=" + finalizedPageCount +
              " expected_pages=" + pair.Value);
          }
          continue;
        }

        deletedSourceCount += DeletePackedSourceAssets(batch, finalizedExportPathByBatch[batch]);
      }
      exportedCount = finalizedExports.Count;
    }

    var summary =
      "Processed " + sourceAtlasCount + " source atlas(es) in " + exportBatches.Count + " export batch(es)." +
      " Exported " + exportedCount + ", deleted_sources " + deletedSourceCount + ", failed " + failedCount + ".";
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

  bool TryAnalyzeSourceAtlas(string sourcePath, string outputFolderPath, out TrimmedAtlasExport exportData, out List<TrimmedSpriteBuildData> buildItems, out Texture2D previewTexture, out string error) {
    exportData = null;
    buildItems = null;
    previewTexture = null;
    error = "";

    if (!IsSupportedSourceTextureAssetPath(sourcePath)) {
      error = "Only PNG atlas files are supported: " + sourcePath;
      return false;
    }

    if (IsGeneratedNormalAtlasAssetPath(sourcePath)) {
      error = "Normal atlas variants with the '_N' suffix are skipped: " + sourcePath;
      return false;
    }

    if (!TryLoadTextureFromDisk(sourcePath, out previewTexture, out error)) {
      return false;
    }

    try {
      var sourcePixels = previewTexture.GetPixels32();
      if (!TryResolveSourceCells(
            sourcePath,
            previewTexture,
            out var sourceCells,
            out var columns,
            out var rows,
            out var resolvedCellWidth,
            out var resolvedCellHeight,
            out error)) {
        DestroyImmediate(previewTexture);
        previewTexture = null;
        return false;
      }

      buildItems = new List<TrimmedSpriteBuildData>(sourceCells.Count);
      var emptyCellCount = 0;

      for (var i = 0; i < sourceCells.Count; i++) {
        var sourceCell = sourceCells[i];
        var buildData = AnalyzeCell(previewTexture.width, sourcePixels, sourceCell.sourceCell, sourceCell.logicalCellRect, sourceCell.spriteName, i);
        buildItems.Add(buildData);
        if (buildData.metadata.empty) emptyCellCount++;
      }

      var exportedBuildItems = BuildExportBuildItems(buildItems);
      long packedArea = 0;
      for (var i = 0; i < exportedBuildItems.Count; i++) {
        packedArea += (long)exportedBuildItems[i].Width * exportedBuildItems[i].Height;
      }

      if (!TryPackTrimmedSprites(exportedBuildItems, out var packedWidth, out var packedHeight, out error)) {
        buildItems = null;
        if (previewTexture != null) {
          DestroyImmediate(previewTexture);
          previewTexture = null;
        }

        return false;
      }

      exportData = new TrimmedAtlasExport {
        sourceAtlasAssetPath = sourcePath,
        exportedAtlasAssetPath = BuildOutputAtlasAssetPath(Path.GetFileNameWithoutExtension(sourcePath), outputFolderPath),
        sourceWidth = previewTexture.width,
        sourceHeight = previewTexture.height,
        cellWidth = resolvedCellWidth,
        cellHeight = resolvedCellHeight,
        columns = columns,
        rows = rows,
        padding = padding,
        atlasWidth = packedWidth,
        atlasHeight = packedHeight,
        emptyCellCount = emptyCellCount,
        packedAreaPctOfSource = (float)Math.Round((packedArea / (double)(previewTexture.width * previewTexture.height)) * 100.0, 2)
      };

      for (var i = 0; i < exportedBuildItems.Count; i++) {
        exportData.sprites.Add(exportedBuildItems[i].metadata);
      }

      return true;
    }
    catch (Exception ex) {
      error = ex.Message;
      buildItems = null;
      exportData = null;
      if (previewTexture != null) {
        DestroyImmediate(previewTexture);
        previewTexture = null;
      }

      return false;
    }
  }

  bool TryResolveSourceCells(
    string sourcePath,
    Texture2D previewTexture,
    out List<SourceCellDefinition> sourceCells,
    out int columns,
    out int rows,
    out int resolvedCellWidth,
    out int resolvedCellHeight,
    out string error) {
    var sourceSprites = LoadSourceSprites(sourcePath);
    sourceCells = TryBuildImportedSourceCells(
      sourcePath,
      previewTexture,
      sourceSprites,
      out columns,
      out rows,
      out resolvedCellWidth,
      out resolvedCellHeight,
      out error);
    if (!string.IsNullOrWhiteSpace(error)) return false;
    if (sourceCells != null && sourceCells.Count > 0) return true;

    return TryBuildGridSourceCells(
      sourcePath,
      previewTexture,
      sourceSprites,
      out sourceCells,
      out columns,
      out rows,
      out resolvedCellWidth,
      out resolvedCellHeight,
      out error);
  }

  List<SourceCellDefinition> TryBuildImportedSourceCells(
    string sourcePath,
    Texture2D previewTexture,
    List<Sprite> sourceSprites,
    out int columns,
    out int rows,
    out int resolvedCellWidth,
    out int resolvedCellHeight,
    out string error) {
    columns = 0;
    rows = 0;
    resolvedCellWidth = 0;
    resolvedCellHeight = 0;
    error = "";
    if (previewTexture == null) return null;

    var sprites = sourceSprites ?? new List<Sprite>();
    if (sprites.Count <= 0) return null;
    if (sprites.Count == 1 && DoesSpriteCoverEntireTexture(sprites[0], previewTexture)) return null;

    var orderedSprites = sprites
      .OrderBy(sprite => sprite.rect.yMin)
      .ThenBy(sprite => sprite.rect.xMin)
      .ThenBy(sprite => sprite.name, StringComparer.Ordinal)
      .ToList();

    var sourceCells = new List<SourceCellDefinition>(orderedSprites.Count);
    for (var i = 0; i < orderedSprites.Count; i++) {
      var sprite = orderedSprites[i];
      var roundedRect = RoundSpriteRectToPixelRect(sprite.rect, previewTexture.width, previewTexture.height);
      if (roundedRect.width <= 0 || roundedRect.height <= 0) {
        error =
          "Sprite '" + sprite.name +
          "' in '" + sourcePath +
          "' resolved to an invalid pixel rect during trim analysis.";
        return null;
      }

      sourceCells.Add(new SourceCellDefinition {
        sourceCell = roundedRect,
        logicalCellRect = sprite.rect,
        spriteName = preserveSpriteNames && !string.IsNullOrWhiteSpace(sprite.name)
          ? sprite.name
          : Path.GetFileNameWithoutExtension(sourcePath) + "_" + (i + 1)
      });
    }

    columns = CountDistinctCellOrigins(orderedSprites.Select(sprite => sprite.rect.xMin));
    rows = CountDistinctCellOrigins(orderedSprites.Select(sprite => sprite.rect.yMin));
    resolvedCellWidth = Mathf.Max(1, Mathf.RoundToInt((float)orderedSprites.Average(sprite => sprite.rect.width)));
    resolvedCellHeight = Mathf.Max(1, Mathf.RoundToInt((float)orderedSprites.Average(sprite => sprite.rect.height)));
    if (columns > 0 && rows > 0 && (columns * rows) != orderedSprites.Count) {
      Debug.LogWarning(
        "[TrimAtlasExport] Imported sprite layout is sparse or irregular." +
        " source='" + sourcePath + "'" +
        " sprite_count=" + orderedSprites.Count +
        " grid=" + columns + "x" + rows);
    }

    var minWidth = orderedSprites.Min(sprite => sprite.rect.width);
    var maxWidth = orderedSprites.Max(sprite => sprite.rect.width);
    var minHeight = orderedSprites.Min(sprite => sprite.rect.height);
    var maxHeight = orderedSprites.Max(sprite => sprite.rect.height);
    var frameRangeDiffersFromConfigured =
      Mathf.Abs(minWidth - cellWidth) > 0.01f ||
      Mathf.Abs(maxWidth - cellWidth) > 0.01f ||
      Mathf.Abs(minHeight - cellHeight) > 0.01f ||
      Mathf.Abs(maxHeight - cellHeight) > 0.01f;
    if (frameRangeDiffersFromConfigured) {
      Debug.Log(
        "[TrimAtlasExport] Using imported sprite layout for trim analysis." +
        " source='" + sourcePath + "'" +
        " sprite_count=" + orderedSprites.Count +
        " columns=" + columns +
        " rows=" + rows +
        " frame_width_range=" + minWidth.ToString("0.###") + "-" + maxWidth.ToString("0.###") +
        " frame_height_range=" + minHeight.ToString("0.###") + "-" + maxHeight.ToString("0.###") +
        " configured_cell=" + cellWidth + "x" + cellHeight);
    }

    return sourceCells;
  }

  bool TryBuildGridSourceCells(
    string sourcePath,
    Texture2D previewTexture,
    List<Sprite> sourceSprites,
    out List<SourceCellDefinition> sourceCells,
    out int columns,
    out int rows,
    out int resolvedCellWidth,
    out int resolvedCellHeight,
    out string error) {
    sourceCells = null;
    columns = 0;
    rows = 0;
    resolvedCellWidth = Mathf.Max(1, cellWidth);
    resolvedCellHeight = Mathf.Max(1, cellHeight);
    error = "";
    if (previewTexture == null) {
      error = "No preview texture is available for trim analysis.";
      return false;
    }

    columns = previewTexture.width / resolvedCellWidth;
    rows = previewTexture.height / resolvedCellHeight;
    if (columns <= 0 || rows <= 0) {
      error =
        "Atlas dimensions are smaller than the configured cell size." +
        " atlas=" + previewTexture.width + "x" + previewTexture.height +
        " configured_cell=" + resolvedCellWidth + "x" + resolvedCellHeight;
      return false;
    }

    var ignoredWidth = previewTexture.width - (columns * resolvedCellWidth);
    var ignoredHeight = previewTexture.height - (rows * resolvedCellHeight);
    if (ignoredWidth > 0 || ignoredHeight > 0) {
      Debug.Log(
        "[TrimAtlasExport] Ignoring atlas-edge remainder outside the configured grid." +
        " source='" + sourcePath + "'" +
        " texture=" + previewTexture.width + "x" + previewTexture.height +
        " configured_cell=" + resolvedCellWidth + "x" + resolvedCellHeight +
        " grid=" + columns + "x" + rows +
        " ignored_px=" + ignoredWidth + "x" + ignoredHeight);
    }

    var spriteNamesByIndex = preserveSpriteNames
      ? BuildSpriteNameLookup(sourceSprites, columns, rows, resolvedCellWidth, resolvedCellHeight)
      : new Dictionary<int, string>();
    var atlasName = Path.GetFileNameWithoutExtension(sourcePath);
    sourceCells = new List<SourceCellDefinition>(columns * rows);
    for (var row = 0; row < rows; row++) {
      for (var column = 0; column < columns; column++) {
        var index = row * columns + column;
        var sourceCell = new PixelRect(column * resolvedCellWidth, row * resolvedCellHeight, resolvedCellWidth, resolvedCellHeight);
        sourceCells.Add(new SourceCellDefinition {
          sourceCell = sourceCell,
          logicalCellRect = new Rect(sourceCell.x, sourceCell.y, sourceCell.width, sourceCell.height),
          spriteName = ResolveSpriteName(spriteNamesByIndex, index, atlasName, index)
        });
      }
    }

    return true;
  }

  bool TryAnalyzeSourceAtlasBatch(SourceAtlasExportBatch batch, string outputFolderPath, out TrimmedAtlasExport exportData, out List<TrimmedSpriteBuildData> buildItems, out string error) {
    exportData = null;
    buildItems = null;
    error = "";
    if (batch == null || batch.sourcePaths == null || batch.sourcePaths.Count <= 0) {
      error = "No source atlases were supplied for folder export.";
      return false;
    }

    if (batch.sourcePaths.Count == 1) {
      if (!TryAnalyzeSourceAtlas(batch.primarySourcePath, outputFolderPath, out exportData, out buildItems, out var previewTexture, out error)) {
        return false;
      }

      if (previewTexture != null) {
        DestroyImmediate(previewTexture);
      }

      exportData.exportedAtlasAssetPath = BuildOutputAtlasAssetPath(batch.outputName, outputFolderPath);
      return true;
    }

    var mergedBuildItems = new List<TrimmedSpriteBuildData>();
    var totalEmptyCellCount = 0;
    long packedArea = 0;
    long totalSourceArea = 0;
    var sourceWidth = 0;
    var sourceHeight = 0;
    var columns = 0;
    var rows = 0;

    for (var sourceIndex = 0; sourceIndex < batch.sourcePaths.Count; sourceIndex++) {
      var sourcePath = batch.sourcePaths[sourceIndex];
      Texture2D previewTexture = null;
      try {
        if (!TryAnalyzeSourceAtlas(sourcePath, outputFolderPath, out var sourceExportData, out var sourceBuildItems, out previewTexture, out error)) {
          return false;
        }

        if (sourceExportData == null || sourceBuildItems == null) {
          error = "No analyzed atlas data was returned for '" + sourcePath + "'.";
          return false;
        }

        if (sourceIndex == 0) {
          sourceWidth = sourceExportData.sourceWidth;
          sourceHeight = sourceExportData.sourceHeight;
          columns = sourceExportData.columns;
          rows = sourceExportData.rows;
        }
        else if (sourceExportData.sourceWidth != sourceWidth ||
                 sourceExportData.sourceHeight != sourceHeight ||
                 sourceExportData.columns != columns ||
                 sourceExportData.rows != rows) {
          Debug.LogWarning(
            "[TrimAtlasExport] Grouping numeric atlases with mixed source dimensions." +
            " output_name='" + batch.outputName + "'" +
            " first=" + sourceWidth + "x" + sourceHeight + " (" + columns + "x" + rows + " cells)" +
            " current=" + sourceExportData.sourceWidth + "x" + sourceExportData.sourceHeight + " (" + sourceExportData.columns + "x" + sourceExportData.rows + " cells)" +
            " source='" + sourcePath + "'");
        }

        totalEmptyCellCount += sourceExportData.emptyCellCount;
        totalSourceArea += (long)sourceExportData.sourceWidth * sourceExportData.sourceHeight;

        for (var itemIndex = 0; itemIndex < sourceBuildItems.Count; itemIndex++) {
          var item = sourceBuildItems[itemIndex];
          if (item == null || item.metadata == null) continue;
          item.metadata.index = mergedBuildItems.Count;
          mergedBuildItems.Add(item);
        }
      }
      finally {
        if (previewTexture != null) {
          DestroyImmediate(previewTexture);
        }
      }
    }

    var exportedBuildItems = BuildExportBuildItems(mergedBuildItems);
    if (exportedBuildItems.Count <= 0) {
      error = "The grouped atlas batch has no visible slices to export after trimming.";
      return false;
    }

    for (var i = 0; i < exportedBuildItems.Count; i++) {
      packedArea += (long)exportedBuildItems[i].Width * exportedBuildItems[i].Height;
    }

    if (!TryPackTrimmedSprites(exportedBuildItems, out var packedWidth, out var packedHeight, out error)) {
      return false;
    }

    exportData = new TrimmedAtlasExport {
      sourceAtlasAssetPath = batch.primarySourcePath,
      exportedAtlasAssetPath = BuildOutputAtlasAssetPath(batch.outputName, outputFolderPath),
      sourceAtlasCount = batch.sourcePaths.Count,
      sourceWidth = sourceWidth,
      sourceHeight = sourceHeight,
      cellWidth = cellWidth,
      cellHeight = cellHeight,
      columns = columns,
      rows = rows,
      padding = padding,
      atlasWidth = packedWidth,
      atlasHeight = packedHeight,
      emptyCellCount = totalEmptyCellCount,
      packedAreaPctOfSource = totalSourceArea > 0
        ? (float)Math.Round((packedArea / (double)totalSourceArea) * 100.0, 2)
        : 0f
    };

    buildItems = mergedBuildItems;
    for (var i = 0; i < exportedBuildItems.Count; i++) {
      exportData.sprites.Add(exportedBuildItems[i].metadata);
    }

    return true;
  }

  TrimmedSpriteBuildData AnalyzeCell(int atlasWidth, Color32[] sourcePixels, PixelRect sourceCell, Rect logicalCellRect, string spriteName, int index) {
    var minX = sourceCell.width;
    var minY = sourceCell.height;
    var maxX = -1;
    var maxY = -1;
    long visiblePixelCount = 0;
    double weightedSumX = 0.0;
    double weightedSumY = 0.0;
    var logicalWidth = logicalCellRect.width > 0f ? logicalCellRect.width : sourceCell.width;
    var logicalHeight = logicalCellRect.height > 0f ? logicalCellRect.height : sourceCell.height;
    var localOriginX = sourceCell.x - logicalCellRect.xMin;
    var localOriginY = sourceCell.y - logicalCellRect.yMin;

    for (var localY = 0; localY < sourceCell.height; localY++) {
      for (var localX = 0; localX < sourceCell.width; localX++) {
        var atlasX = sourceCell.x + localX;
        var atlasY = sourceCell.y + localY;
        var color = sourcePixels[(atlasY * atlasWidth) + atlasX];
        if (!IsVisible(color)) continue;
        if (localX < minX) minX = localX;
        if (localY < minY) minY = localY;
        if (localX > maxX) maxX = localX;
        if (localY > maxY) maxY = localY;
        visiblePixelCount++;
        weightedSumX += localOriginX + localX + 0.5;
        weightedSumY += localOriginY + localY + 0.5;
      }
    }

    var metadata = new TrimmedSpriteMetadata {
      index = index,
      name = spriteName,
      empty = visiblePixelCount <= 0,
      sourceCell = sourceCell
    };

    if (visiblePixelCount <= 0) {
      metadata.trimRectInCell = new PixelRect(0, 0, 1, 1);
      metadata.offsetFromCellCenterPx = new PixelPoint(0f, 0f);
      metadata.weightedCenterOffsetPx = new PixelPoint(0f, 0f);
      return new TrimmedSpriteBuildData { metadata = metadata, trimmedPixels = new[] { new Color32(0, 0, 0, 0) } };
    }

    var trimWidth = maxX - minX + 1;
    var trimHeight = maxY - minY + 1;
    metadata.trimRectInCell = new PixelRect(minX, minY, trimWidth, trimHeight);
    metadata.offsetFromCellCenterPx = new PixelPoint(
      (float)Math.Round((localOriginX + minX + (trimWidth * 0.5f)) - (logicalWidth * 0.5f), 3),
      (float)Math.Round((localOriginY + minY + (trimHeight * 0.5f)) - (logicalHeight * 0.5f), 3));
    metadata.weightedCenterOffsetPx = new PixelPoint(
      (float)Math.Round((weightedSumX / visiblePixelCount) - (logicalWidth * 0.5f), 3),
      (float)Math.Round((weightedSumY / visiblePixelCount) - (logicalHeight * 0.5f), 3));

    return new TrimmedSpriteBuildData {
      metadata = metadata,
      trimmedPixels = CopyTrimmedPixels(sourcePixels, atlasWidth, sourceCell, metadata.trimRectInCell)
    };
  }

  bool IsVisible(Color32 color) {
    if (color.a <= alphaThreshold) return false;
    if (!treatNearWhiteAsEmpty) return true;
    return color.r < nearWhiteThreshold || color.g < nearWhiteThreshold || color.b < nearWhiteThreshold;
  }

  Color32[] CopyTrimmedPixels(Color32[] sourcePixels, int atlasWidth, PixelRect sourceCell, PixelRect trimRect) {
    var trimmedPixels = new Color32[trimRect.width * trimRect.height];
    var dst = 0;
    for (var y = 0; y < trimRect.height; y++) {
      var srcY = sourceCell.y + trimRect.y + y;
      for (var x = 0; x < trimRect.width; x++) {
        var srcX = sourceCell.x + trimRect.x + x;
        trimmedPixels[dst++] = sourcePixels[(srcY * atlasWidth) + srcX];
      }
    }

    return trimmedPixels;
  }

  bool TryPackTrimmedSprites(List<TrimmedSpriteBuildData> items, out int packedWidth, out int packedHeight, out string error) {
    packedWidth = 1;
    packedHeight = 1;
    error = "";
    if (items == null || items.Count <= 0) {
      return true;
    }

    var targetWidth = Math.Max(1, maxAtlasWidth);
    var maxContentWidth = Math.Max(1, targetWidth - (padding * 2));
    TrimmedSpriteBuildData widestItem = null;
    var widestSprite = 0;
    for (var i = 0; i < items.Count; i++) {
      var item = items[i];
      if (item == null || item.metadata == null) continue;
      if (item.Width <= widestSprite) continue;

      widestSprite = item.Width;
      widestItem = item;
    }

    if (widestItem == null) {
      return true;
    }

    if (widestSprite > maxContentWidth) {
      var spriteName = string.IsNullOrWhiteSpace(widestItem.metadata.name) ? "<unnamed>" : widestItem.metadata.name;
      error = "Trimmed sprite '" + spriteName + "' is too wide to fit within the max atlas width once padding is applied.";
      Debug.LogWarning(
        "[TrimAtlasExport] Trimmed sprite exceeds padded atlas width." +
        " sprite='" + spriteName + "'" +
        " sprite_width=" + widestSprite +
        " max_content_width=" + maxContentWidth +
        " atlas_limit=" + targetWidth +
        " padding=" + padding);
      return false;
    }

    var ordered = items
      .Where(item => item != null && item.metadata != null)
      .OrderByDescending(item => item.Height)
      .ThenByDescending(item => item.Width)
      .ThenBy(item => item.metadata.index)
      .ToList();

    var x = padding;
    var y = padding;
    var rowHeight = 0;
    var usedWidth = 0;
    for (var i = 0; i < ordered.Count; i++) {
      var item = ordered[i];
      if (x > padding && x + item.Width + padding > targetWidth) {
        y += rowHeight + padding;
        x = padding;
        rowHeight = 0;
      }

      item.metadata.packedRect = new PixelRect(x, y, item.Width, item.Height);
      x += item.Width + padding;
      if (item.Height > rowHeight) rowHeight = item.Height;
      if (x > usedWidth) usedWidth = x;
    }

    packedWidth = Math.Max(1, usedWidth);
    packedHeight = Math.Max(1, y + rowHeight + padding);
    if (packedWidth > targetWidth) {
      error = "Packed atlas width exceeded the configured max atlas width.";
      Debug.LogWarning(
        "[TrimAtlasExport] Packed atlas width exceeded limit after packing." +
        " packed_width=" + packedWidth +
        " atlas_limit=" + targetWidth +
        " padding=" + padding);
      return false;
    }

    return true;
  }

  Texture2D BuildPackedTexture(int packedWidth, int packedHeight, List<TrimmedSpriteBuildData> items) {
    var atlasTexture = new Texture2D(packedWidth, packedHeight, TextureFormat.RGBA32, false);
    atlasTexture.filterMode = FilterMode.Point;
    atlasTexture.wrapMode = TextureWrapMode.Clamp;
    atlasTexture.SetPixels32(new Color32[packedWidth * packedHeight]);
    for (var i = 0; i < items.Count; i++) {
      var rect = items[i].metadata.packedRect;
      atlasTexture.SetPixels32(rect.x, rect.y, rect.width, rect.height, items[i].trimmedPixels);
    }

    atlasTexture.Apply(false, false);
    return atlasTexture;
  }

  bool TryWriteAtlasExport(
    string sourcePath,
    string outputFolderPath,
    TrimmedAtlasExport exportData,
    List<TrimmedSpriteBuildData> buildItems,
    out PendingTrimmedAtlasExport pendingExport,
    out string error) {
    pendingExport = null;
    error = "";
    if (exportData == null || buildItems == null) {
      error = "No analyzed atlas data is available for export.";
      return false;
    }

    var exportedBuildItems = BuildExportBuildItems(buildItems);
    if (exportedBuildItems.Count <= 0) {
      error = "The selected atlas has no visible slices to export after trimming.";
      return false;
    }

    var atlasTexture = BuildPackedTexture(exportData.atlasWidth, exportData.atlasHeight, exportedBuildItems);
    try {
      var exportedAtlasPath = WriteAtlasTexture(exportData.exportedAtlasAssetPath, atlasTexture);
      exportData.exportedAtlasAssetPath = exportedAtlasPath;
      var metadataAssetPath = WriteMetadataJson(exportedAtlasPath, exportData);
      pendingExport = new PendingTrimmedAtlasExport {
        sourceAtlasAssetPath = sourcePath,
        exportedAtlasAssetPath = exportedAtlasPath,
        metadataAssetPath = metadataAssetPath,
        exportData = exportData
      };

      return true;
    }
    catch (Exception ex) {
      error = ex.Message;
      return false;
    }
    finally {
      DestroyImmediate(atlasTexture);
    }
  }

  bool TryWriteAtlasExports(
    string sourcePath,
    string outputFolderPath,
    string outputName,
    TrimmedAtlasExport exportData,
    List<TrimmedSpriteBuildData> buildItems,
    out List<PendingTrimmedAtlasExport> pendingExports,
    out string error) {
    pendingExports = new List<PendingTrimmedAtlasExport>();
    error = "";
    if (exportData == null || buildItems == null) {
      error = "No analyzed atlas data is available for export.";
      return false;
    }

    var exportedBuildItems = BuildExportBuildItems(buildItems);
    if (exportedBuildItems.Count <= 0) {
      error = "The selected atlas has no visible slices to export after trimming.";
      return false;
    }

    var pageCount = CalculateExportPageCount(exportedBuildItems.Count);
    if (pageCount <= 1) {
      if (!TryWriteAtlasExport(sourcePath, outputFolderPath, exportData, buildItems, out var pendingExport, out error)) {
        return false;
      }

      if (pendingExport != null) {
        pendingExports.Add(pendingExport);
      }
      return true;
    }

    var estimatedTotalSourceArea = EstimateTotalSourceArea(exportData);
    for (var pageIndex = 0; pageIndex < pageCount; pageIndex++) {
      var pageItems = GetExportPageItems(exportedBuildItems, pageIndex);
      if (pageItems.Count <= 0) continue;
      var pageOutputName = BuildPagedOutputName(outputName, pageIndex);
      if (!TryValidateSpriteSliceLimit(pageOutputName, pageItems.Count, out error)) {
        return false;
      }
      if (!TryPackTrimmedSprites(pageItems, out var packedWidth, out var packedHeight, out error)) {
        return false;
      }

      long packedArea = 0;
      for (var itemIndex = 0; itemIndex < pageItems.Count; itemIndex++) {
        packedArea += (long)pageItems[itemIndex].Width * pageItems[itemIndex].Height;
      }

      var pageExportData = BuildPagedExportData(exportData, outputFolderPath, pageOutputName, pageItems, packedWidth, packedHeight, packedArea, estimatedTotalSourceArea);
      if (!TryWriteAtlasExport(sourcePath, outputFolderPath, pageExportData, pageItems, out var pendingExport, out error)) {
        pendingExports.Clear();
        return false;
      }

      if (pendingExport != null) {
        pendingExports.Add(pendingExport);
      }
    }

    return pendingExports.Count > 0;
  }

  List<PendingTrimmedAtlasExport> FinalizeWrittenAtlasExports(List<PendingTrimmedAtlasExport> pendingExports, List<string> failureLogs) {
    var finalizedExports = new List<PendingTrimmedAtlasExport>();
    if (pendingExports == null || pendingExports.Count <= 0) return finalizedExports;

    Debug.Log(
      "[TrimAtlasExport] Final import phase started." +
      " pending_exports=" + pendingExports.Count +
      " create_slices=" + createAtlasSlices);
    AssetDatabase.Refresh();

    for (var i = 0; i < pendingExports.Count; i++) {
      var pendingExport = pendingExports[i];
      if (pendingExport == null || string.IsNullOrWhiteSpace(pendingExport.exportedAtlasAssetPath)) continue;
      if (!TryValidateSpriteSliceLimit(pendingExport.exportedAtlasAssetPath, pendingExport.exportData?.sprites?.Count ?? 0, out var limitError)) {
        AddFailureLog(failureLogs, pendingExport.sourceAtlasAssetPath, limitError);
        continue;
      }

      if (createAtlasSlices) {
        if (!TryFinalizeAtlasTexture(pendingExport.sourceAtlasAssetPath, pendingExport.exportedAtlasAssetPath, pendingExport.exportData, out var error)) {
          AddFailureLog(failureLogs, pendingExport.sourceAtlasAssetPath, error);
          continue;
        }
      }
      else {
        if (!TryFinalizeUnslicedTextureAsset(pendingExport.exportedAtlasAssetPath, pendingExport.exportData, out var error)) {
          AddFailureLog(failureLogs, pendingExport.sourceAtlasAssetPath, error);
          continue;
        }
      }

      EnsureMetadataAddressable(pendingExport.metadataAssetPath, saveAssets: false);
      TrimmedSpriteOffsetResolver.InvalidateAtlas(pendingExport.exportedAtlasAssetPath);
      finalizedExports.Add(pendingExport);
    }

    if (finalizedExports.Count > 0) {
      AssetDatabase.SaveAssets();
    }

    return finalizedExports;
  }

  static List<TrimmedSpriteBuildData> BuildExportBuildItems(List<TrimmedSpriteBuildData> buildItems) {
    if (buildItems == null || buildItems.Count <= 0) return new List<TrimmedSpriteBuildData>();

    var exportedBuildItems = new List<TrimmedSpriteBuildData>(buildItems.Count);
    for (var i = 0; i < buildItems.Count; i++) {
      var item = buildItems[i];
      if (item == null || item.metadata.empty) continue;
      exportedBuildItems.Add(item);
    }

    return exportedBuildItems;
  }

  static int CalculateExportPageCount(int spriteCount) {
    if (spriteCount <= 0) return 0;
    return (spriteCount + MaxExportSpriteSliceCount - 1) / MaxExportSpriteSliceCount;
  }

  static List<TrimmedSpriteBuildData> GetExportPageItems(List<TrimmedSpriteBuildData> exportedBuildItems, int pageIndex) {
    var pageItems = new List<TrimmedSpriteBuildData>();
    if (exportedBuildItems == null || exportedBuildItems.Count <= 0 || pageIndex < 0) return pageItems;

    var startIndex = pageIndex * MaxExportSpriteSliceCount;
    if (startIndex >= exportedBuildItems.Count) return pageItems;
    var endIndex = Math.Min(exportedBuildItems.Count, startIndex + MaxExportSpriteSliceCount);
    pageItems.Capacity = endIndex - startIndex;
    for (var i = startIndex; i < endIndex; i++) {
      var item = exportedBuildItems[i];
      if (item == null || item.metadata == null) continue;
      pageItems.Add(item);
    }

    return pageItems;
  }

  static long EstimateTotalSourceArea(TrimmedAtlasExport exportData) {
    if (exportData == null) return 0;
    var sourceAtlasCount = Math.Max(1, exportData.sourceAtlasCount);
    return Math.Max(1L, (long)Math.Max(1, exportData.sourceWidth) * Math.Max(1, exportData.sourceHeight) * sourceAtlasCount);
  }

  TrimmedAtlasExport BuildPagedExportData(
    TrimmedAtlasExport sourceExportData,
    string outputFolderPath,
    string outputName,
    List<TrimmedSpriteBuildData> pageItems,
    int packedWidth,
    int packedHeight,
    long packedArea,
    long estimatedTotalSourceArea) {
    var pageExportData = new TrimmedAtlasExport {
      sourceAtlasAssetPath = sourceExportData.sourceAtlasAssetPath,
      exportedAtlasAssetPath = BuildOutputAtlasAssetPath(outputName, outputFolderPath),
      coordinateOrigin = sourceExportData.coordinateOrigin,
      sourceAtlasCount = sourceExportData.sourceAtlasCount,
      sourceWidth = sourceExportData.sourceWidth,
      sourceHeight = sourceExportData.sourceHeight,
      cellWidth = sourceExportData.cellWidth,
      cellHeight = sourceExportData.cellHeight,
      columns = sourceExportData.columns,
      rows = sourceExportData.rows,
      padding = sourceExportData.padding,
      atlasWidth = packedWidth,
      atlasHeight = packedHeight,
      emptyCellCount = sourceExportData.emptyCellCount,
      packedAreaPctOfSource = estimatedTotalSourceArea > 0
        ? (float)Math.Round((packedArea / (double)estimatedTotalSourceArea) * 100.0, 2)
        : 0f
    };

    for (var i = 0; i < pageItems.Count; i++) {
      if (pageItems[i]?.metadata == null) continue;
      pageExportData.sprites.Add(pageItems[i].metadata);
    }

    return pageExportData;
  }

  static bool TryValidateSpriteSliceLimit(string contextLabel, int spriteCount, out string error) {
    error = "";
    if (spriteCount <= MaxExportSpriteSliceCount) return true;

    error =
      "Trimmed atlas export would create " + spriteCount +
      " sprite slices for '" + (contextLabel ?? "") +
      "', which exceeds the hard safety limit of " + MaxExportSpriteSliceCount + ".";
    return false;
  }

  static string BuildPagedOutputName(string outputName, int pageIndex) {
    if (pageIndex <= 0) return outputName ?? "trimmed";
    return (outputName ?? "trimmed") + "_p" + (pageIndex + 1);
  }

  string WriteAtlasTexture(string outputAssetPath, Texture2D atlasTexture) {
    var outputFullPath = Path.GetFullPath(outputAssetPath);
    Directory.CreateDirectory(Path.GetDirectoryName(outputFullPath) ?? "");
    File.WriteAllBytes(outputFullPath, atlasTexture.EncodeToPNG());
    return outputAssetPath.Replace("\\", "/");
  }

  static bool TryPrepareOverwriteImport(TextureImporter importer, out int previousSliceCount) {
    previousSliceCount = 0;
    if (importer == null) return false;

    var factory = new SpriteDataProviderFactories();
    factory.Init();
    var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer) as ISpriteEditorDataProvider;
    if (dataProvider != null) {
      dataProvider.InitSpriteEditorDataProvider();
      var existingRects = dataProvider.GetSpriteRects();
      previousSliceCount = existingRects?.Length ?? 0;
      if (previousSliceCount <= 0) return false;

      dataProvider.SetSpriteRects(Array.Empty<SpriteRect>());
      if (dataProvider.HasDataProvider(typeof(ISpriteNameFileIdDataProvider))) {
        var nameFileIdProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        nameFileIdProvider.SetNameFileIdPairs(new List<SpriteNameFileIdPair>());
      }

      dataProvider.Apply();
      return true;
    }

    if (importer.spriteImportMode != SpriteImportMode.Multiple) {
      return false;
    }

    importer.spriteImportMode = SpriteImportMode.Single;
    previousSliceCount = -1;
    return true;
  }

  string WriteMetadataJson(string exportedAtlasAssetPath, TrimmedAtlasExport exportData) {
    var jsonPath = Path.ChangeExtension(exportedAtlasAssetPath, ".json");
    var fullJsonPath = Path.GetFullPath(jsonPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullJsonPath) ?? "");
    File.WriteAllText(fullJsonPath, JsonUtility.ToJson(exportData, true));
    return jsonPath.Replace("\\", "/");
  }

  bool TryFinalizeAtlasTexture(string sourceAtlasAssetPath, string exportedAtlasAssetPath, TrimmedAtlasExport exportData, out string error) {
    error = "";
    if (string.IsNullOrWhiteSpace(exportedAtlasAssetPath)) {
      error = "Missing exported atlas path for final import.";
      return false;
    }
    if (exportData?.sprites == null) {
      error = "Missing trimmed sprite metadata for final import '" + exportedAtlasAssetPath + "'.";
      return false;
    }
    if (!TryValidateSpriteSliceLimit(exportedAtlasAssetPath, exportData.sprites.Count, out error)) {
      return false;
    }

    var importer = AssetImporter.GetAtPath(exportedAtlasAssetPath) as TextureImporter;
    if (importer == null) {
      error = "Texture importer is unavailable for '" + exportedAtlasAssetPath + "'.";
      return false;
    }

    var importerChanged = SpriteStreamingTextureImportPolicy.Apply(importer, true);
    importerChanged |= CopySourceImporterSettings(sourceAtlasAssetPath, importer);
    if (!importer.alphaIsTransparency) {
      importer.alphaIsTransparency = true;
      importerChanged = true;
    }

    var factory = new SpriteDataProviderFactories();
    factory.Init();
    var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer) as ISpriteEditorDataProvider;
    if (dataProvider == null) {
      error = "Sprite data provider is unavailable for '" + exportedAtlasAssetPath + "'.";
      return false;
    }

    dataProvider.InitSpriteEditorDataProvider();

    var rects = new List<SpriteRect>(exportData.sprites.Count);
    for (var i = 0; i < exportData.sprites.Count; i++) {
      var sprite = exportData.sprites[i];
      rects.Add(new SpriteRect {
        name = sprite.name,
        rect = new Rect(sprite.packedRect.x, sprite.packedRect.y, sprite.packedRect.width, sprite.packedRect.height),
        alignment = (int)SpriteAlignment.Center,
        pivot = new Vector2(0.5f, 0.5f),
        border = Vector4.zero
      });
    }

    dataProvider.SetSpriteRects(rects.ToArray());
    if (dataProvider.HasDataProvider(typeof(ISpriteNameFileIdDataProvider))) {
      var nameFileIdProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
      var pairs = new List<SpriteNameFileIdPair>(rects.Count);
      for (var i = 0; i < rects.Count; i++) {
        pairs.Add(new SpriteNameFileIdPair(rects[i].name, GUID.Generate()));
      }

      nameFileIdProvider.SetNameFileIdPairs(pairs);
    }

    dataProvider.Apply();
    if (importerChanged || rects.Count > 0) {
      importer.SaveAndReimport();
    }

    return true;
  }

  bool TryFinalizeUnslicedTextureAsset(string exportedAtlasAssetPath, TrimmedAtlasExport exportData, out string error) {
    error = "";
    if (string.IsNullOrWhiteSpace(exportedAtlasAssetPath)) {
      error = "Missing exported atlas path for final import.";
      return false;
    }
    if (exportData == null) {
      error = "Missing export metadata for final import '" + exportedAtlasAssetPath + "'.";
      return false;
    }

    var importer = AssetImporter.GetAtPath(exportedAtlasAssetPath) as TextureImporter;
    if (importer == null) {
      error = "Texture importer is unavailable for '" + exportedAtlasAssetPath + "'.";
      return false;
    }

    if (!TryPrepareOverwriteImport(importer, out var previousSliceCount)) {
      AssetDatabase.ImportAsset(exportedAtlasAssetPath, ImportAssetOptions.ForceUpdate);
      return true;
    }

    Debug.Log(
      "[TrimAtlasExport] Clearing stale sprite metadata before final import." +
      " asset='" + exportedAtlasAssetPath + "'" +
      " previous_slices=" + (previousSliceCount >= 0 ? previousSliceCount.ToString() : "unknown") +
      " new_size=" + exportData.atlasWidth + "x" + exportData.atlasHeight);
    importer.SaveAndReimport();
    return true;
  }

  internal static bool CopySourceImporterSettings(string sourceAtlasAssetPath, TextureImporter targetImporter) {
    if (targetImporter == null || string.IsNullOrWhiteSpace(sourceAtlasAssetPath)) return false;
    var sourceImporter = AssetImporter.GetAtPath(sourceAtlasAssetPath) as TextureImporter;
    if (sourceImporter == null) return false;

    var changed = false;
    if (!Mathf.Approximately(targetImporter.spritePixelsPerUnit, sourceImporter.spritePixelsPerUnit)) {
      targetImporter.spritePixelsPerUnit = sourceImporter.spritePixelsPerUnit;
      changed = true;
    }

    var sourceSettings = new TextureImporterSettings();
    sourceImporter.ReadTextureSettings(sourceSettings);
    var targetSettings = new TextureImporterSettings();
    targetImporter.ReadTextureSettings(targetSettings);
    if (targetSettings.spriteMeshType != sourceSettings.spriteMeshType) {
      targetSettings.spriteMeshType = sourceSettings.spriteMeshType;
      targetImporter.SetTextureSettings(targetSettings);
      changed = true;
    }

    return changed;
  }

  void DrawAnalysisPreview() {
    EditorGUILayout.Space();
    EditorGUILayout.LabelField("Slice Preview", EditorStyles.boldLabel);

    if (!TryGetSourcePath(out var sourcePath, false)) {
      EditorGUILayout.HelpBox("Select a source atlas to analyze slices and offsets.", MessageType.None);
      return;
    }

    if (!HasFreshAnalysis(sourcePath)) {
      EditorGUILayout.HelpBox("Click 'Analyze Slice Offsets' to browse slices and inspect their x/y offsets.", MessageType.None);
      return;
    }

    if (analyzedAtlas == null || analyzedAtlas.sprites == null || analyzedAtlas.sprites.Count <= 0) {
      EditorGUILayout.HelpBox("No slice data is available for this atlas.", MessageType.Warning);
      return;
    }

    EditorGUILayout.HelpBox(
      "Exact Offset is the value to use for exact reconstruction. Weighted Offset is the visible-pixel center of mass and is included for comparison only.",
      MessageType.None);
    EditorGUILayout.LabelField(
      "Summary",
      analyzedAtlas.columns + "x" + analyzedAtlas.rows +
      " cells, empty=" + analyzedAtlas.emptyCellCount +
      ", packed=" + analyzedAtlas.atlasWidth + "x" + analyzedAtlas.atlasHeight +
      ", packed_area_pct=" + analyzedAtlas.packedAreaPctOfSource.ToString("0.00"));

    hideEmptySlices = EditorGUILayout.Toggle("Hide Empty Slices", hideEmptySlices);
    selectedSliceIndex = Mathf.Clamp(selectedSliceIndex < 0 ? 0 : selectedSliceIndex, 0, analyzedAtlas.sprites.Count - 1);
    if (hideEmptySlices && analyzedAtlas.sprites[selectedSliceIndex].empty) {
      selectedSliceIndex = FindFirstVisibleSliceIndex();
    }

    selectedSliceIndex = EditorGUILayout.IntSlider("Selected Slice", selectedSliceIndex + 1, 1, analyzedAtlas.sprites.Count) - 1;
    if (hideEmptySlices && analyzedAtlas.sprites[selectedSliceIndex].empty) {
      selectedSliceIndex = FindFirstVisibleSliceIndex();
    }

    var selected = analyzedAtlas.sprites[selectedSliceIndex];
    using (new EditorGUILayout.VerticalScope("box")) {
      EditorGUILayout.LabelField(selected.name + " (slice " + (selected.index + 1) + ")", EditorStyles.boldLabel);
      EditorGUILayout.LabelField("Exact Offset", FormatPoint(selected.offsetFromCellCenterPx));
      EditorGUILayout.LabelField("Weighted Offset", FormatPoint(selected.weightedCenterOffsetPx));
      EditorGUILayout.LabelField("Trim Rect In Cell", FormatRect(selected.trimRectInCell));
      EditorGUILayout.LabelField("Packed Rect", FormatRect(selected.packedRect));

      var previewRow = GUILayoutUtility.GetRect(10f, 210f, GUILayout.ExpandWidth(true));
      var panelWidth = Mathf.Max(120f, (previewRow.width - 16f) * 0.5f);
      var leftRect = new Rect(previewRow.x, previewRow.y, panelWidth, previewRow.height);
      var rightRect = new Rect(previewRow.x + panelWidth + 16f, previewRow.y, panelWidth, previewRow.height);
      DrawTexturePreview(leftRect, "Source Cell", selected.sourceCell, selected.empty, fitToContent: false);
      DrawTexturePreview(rightRect, "Trimmed Crop", BuildAtlasRect(selected.sourceCell, selected.trimRectInCell), selected.empty, fitToContent: true);
    }

    EditorGUILayout.Space();
    EditorGUILayout.LabelField("Offsets", EditorStyles.boldLabel);
    using (var scroll = new EditorGUILayout.ScrollViewScope(sliceListScrollPosition, GUILayout.Height(280f))) {
      sliceListScrollPosition = scroll.scrollPosition;
      for (var i = 0; i < analyzedAtlas.sprites.Count; i++) {
        var sprite = analyzedAtlas.sprites[i];
        if (hideEmptySlices && sprite.empty) continue;

        using (new EditorGUILayout.HorizontalScope("box")) {
          GUILayout.Label(selectedSliceIndex == i ? ">" : "", GUILayout.Width(10f));
          EditorGUILayout.LabelField(sprite.name, GUILayout.Width(180f));
          EditorGUILayout.LabelField(FormatPoint(sprite.offsetFromCellCenterPx), GUILayout.Width(150f));
          EditorGUILayout.LabelField(sprite.empty ? "Empty" : (sprite.trimRectInCell.width + "x" + sprite.trimRectInCell.height), GUILayout.Width(70f));
          if (GUILayout.Button("View", GUILayout.Width(60f))) {
            selectedSliceIndex = i;
          }
        }
      }
    }
  }

  int FindFirstVisibleSliceIndex() {
    if (analyzedAtlas == null || analyzedAtlas.sprites == null || analyzedAtlas.sprites.Count <= 0) return 0;
    for (var i = 0; i < analyzedAtlas.sprites.Count; i++) {
      if (hideEmptySlices && analyzedAtlas.sprites[i].empty) continue;
      return i;
    }

    return 0;
  }

  void DrawTexturePreview(Rect rect, string title, PixelRect atlasRect, bool empty, bool fitToContent) {
    var titleRect = new Rect(rect.x, rect.y, rect.width, 18f);
    var imageRect = new Rect(rect.x, rect.y + 20f, rect.width, rect.height - 20f);
    GUI.Label(titleRect, title, EditorStyles.miniBoldLabel);
    EditorGUI.DrawRect(imageRect, new Color(0.14f, 0.14f, 0.14f, 1f));

    if (empty) {
      GUI.Label(imageRect, "Empty", EditorStyles.centeredGreyMiniLabel);
      DrawOutline(imageRect, Color.gray, 1f);
      return;
    }

    var drawRect = fitToContent ? FitRectInside(imageRect, atlasRect.width, atlasRect.height, 8f) : imageRect;
    DrawTextureRegion(drawRect, atlasRect);
    DrawOutline(drawRect, Color.gray, 1f);
  }

  void DrawTextureRegion(Rect rect, PixelRect atlasRect) {
    if (Event.current.type != EventType.Repaint) return;
    if (analyzedPreviewTexture == null || analyzedAtlas == null) return;

    var uv = new Rect(
      atlasRect.x / (float)analyzedAtlas.sourceWidth,
      atlasRect.y / (float)analyzedAtlas.sourceHeight,
      atlasRect.width / (float)analyzedAtlas.sourceWidth,
      atlasRect.height / (float)analyzedAtlas.sourceHeight);
    GUI.DrawTextureWithTexCoords(rect, analyzedPreviewTexture, uv, true);
  }

  void DrawOutline(Rect rect, Color color, float thickness) {
    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
    EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
    EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
    EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
  }

  Rect FitRectInside(Rect container, int contentWidth, int contentHeight, float inset) {
    var inner = new Rect(
      container.x + inset,
      container.y + inset,
      Mathf.Max(1f, container.width - (inset * 2f)),
      Mathf.Max(1f, container.height - (inset * 2f)));
    if (contentWidth <= 0 || contentHeight <= 0) return inner;

    var scale = Mathf.Min(inner.width / contentWidth, inner.height / contentHeight);
    var width = contentWidth * scale;
    var height = contentHeight * scale;
    return new Rect(
      inner.x + ((inner.width - width) * 0.5f),
      inner.y + ((inner.height - height) * 0.5f),
      width,
      height);
  }

  static PixelRect BuildAtlasRect(PixelRect sourceCell, PixelRect trimRectInCell) {
    return new PixelRect(
      sourceCell.x + trimRectInCell.x,
      sourceCell.y + trimRectInCell.y,
      trimRectInCell.width,
      trimRectInCell.height);
  }

  static string FormatPoint(PixelPoint point) {
    return "x=" + point.x.ToString("0.###") + ", y=" + point.y.ToString("0.###");
  }

  static string FormatRect(PixelRect rect) {
    return "x=" + rect.x + ", y=" + rect.y + ", w=" + rect.width + ", h=" + rect.height;
  }

  bool TryGetSourcePath(out string sourcePath, bool showDialog) {
    sourcePath = "";
    if (sourceTexture == null) {
      if (showDialog) EditorUtility.DisplayDialog("Missing Source Atlas", "Select a source atlas first.", "OK");
      return false;
    }

    sourcePath = AssetDatabase.GetAssetPath(sourceTexture);
    if (!string.IsNullOrWhiteSpace(sourcePath)) return true;
    if (showDialog) EditorUtility.DisplayDialog("Invalid Source Atlas", "Could not resolve the source atlas asset path.", "OK");
    return false;
  }

  bool TryGetSourceFolderPath(out string sourceFolderPath, bool showDialog) {
    sourceFolderPath = "";
    if (sourceFolder == null) {
      if (showDialog) EditorUtility.DisplayDialog("Missing Source Folder", "Select a source folder first.", "OK");
      return false;
    }

    sourceFolderPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(sourceFolder));
    if (AssetDatabase.IsValidFolder(sourceFolderPath)) return true;
    if (showDialog) EditorUtility.DisplayDialog("Invalid Source Folder", "Could not resolve the source folder asset path.", "OK");
    return false;
  }

  bool HasFreshAnalysis(string sourcePath) {
    return analyzedAtlas != null &&
           analyzedBuildItems != null &&
           analyzedPreviewTexture != null &&
           string.Equals(analyzedSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(analyzedSettingsSignature, BuildAnalysisSignature(sourcePath), StringComparison.Ordinal);
  }

  string BuildAnalysisSignature(string sourcePath) {
    return string.Join("|", sourcePath ?? "", cellWidth, cellHeight, maxAtlasWidth, padding, alphaThreshold, treatNearWhiteAsEmpty ? 1 : 0, nearWhiteThreshold, preserveSpriteNames ? 1 : 0);
  }

  void CacheAnalysis(string sourcePath, TrimmedAtlasExport exportData, List<TrimmedSpriteBuildData> buildItems, Texture2D previewTexture) {
    InvalidateAnalysis();
    analyzedSourcePath = sourcePath;
    analyzedSettingsSignature = BuildAnalysisSignature(sourcePath);
    analyzedAtlas = exportData;
    analyzedBuildItems = buildItems;
    analyzedPreviewTexture = previewTexture;
  }

  void InvalidateAnalysis() {
    analyzedAtlas = null;
    analyzedBuildItems = null;
    analyzedSourcePath = "";
    analyzedSettingsSignature = "";
    selectedSliceIndex = -1;
    if (analyzedPreviewTexture != null) {
      DestroyImmediate(analyzedPreviewTexture);
      analyzedPreviewTexture = null;
    }
  }

  void SelectDefaultSlice() {
    selectedSliceIndex = 0;
    if (analyzedAtlas == null || analyzedAtlas.sprites == null) return;
    for (var i = 0; i < analyzedAtlas.sprites.Count; i++) {
      if (analyzedAtlas.sprites[i].empty) continue;
      selectedSliceIndex = i;
      return;
    }
  }

  void OnDisable() {
    InvalidateAnalysis();
  }

  void OnDestroy() {
    InvalidateAnalysis();
  }

  internal static bool TryLoadTextureFromDisk(string assetPath, out Texture2D texture, out string error) {
    texture = null;
    error = "";
    var fullPath = Path.GetFullPath(assetPath);
    if (!File.Exists(fullPath)) {
      error = "Atlas file does not exist on disk: " + fullPath;
      return false;
    }

    var bytes = File.ReadAllBytes(fullPath);
    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
    if (!ImageConversion.LoadImage(texture, bytes, false)) {
      DestroyImmediate(texture);
      texture = null;
      error = "Failed to decode atlas file: " + assetPath;
      return false;
    }

    texture.filterMode = FilterMode.Point;
    texture.wrapMode = TextureWrapMode.Clamp;
    return true;
  }

  static List<Sprite> LoadSourceSprites(string sourcePath) {
    if (string.IsNullOrWhiteSpace(sourcePath)) return new List<Sprite>();
    return AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<Sprite>().ToList();
  }

  Dictionary<int, string> BuildSpriteNameLookup(IEnumerable<Sprite> sprites, int columns, int rows, int gridCellWidth, int gridCellHeight) {
    var result = new Dictionary<int, string>();
    if (sprites == null || columns <= 0 || rows <= 0 || gridCellWidth <= 0 || gridCellHeight <= 0) return result;

    foreach (var sprite in sprites) {
      if (sprite == null) continue;
      var rect = sprite.rect;
      var column = Mathf.RoundToInt(rect.x / gridCellWidth);
      var row = Mathf.RoundToInt(rect.y / gridCellHeight);
      if (column < 0 || column >= columns || row < 0 || row >= rows) continue;
      result[(row * columns) + column] = sprite.name;
    }

    return result;
  }

  static bool DoesSpriteCoverEntireTexture(Sprite sprite, Texture2D texture) {
    if (sprite == null || texture == null) return false;
    var rect = sprite.rect;
    return Mathf.Abs(rect.xMin) <= 0.01f &&
           Mathf.Abs(rect.yMin) <= 0.01f &&
           Mathf.Abs(rect.width - texture.width) <= 0.01f &&
           Mathf.Abs(rect.height - texture.height) <= 0.01f;
  }

  static PixelRect RoundSpriteRectToPixelRect(Rect rect, int atlasWidth, int atlasHeight) {
    var xMin = Mathf.Clamp(Mathf.RoundToInt(rect.xMin), 0, Math.Max(0, atlasWidth - 1));
    var yMin = Mathf.Clamp(Mathf.RoundToInt(rect.yMin), 0, Math.Max(0, atlasHeight - 1));
    var xMax = Mathf.Clamp(Mathf.RoundToInt(rect.xMax), xMin + 1, Math.Max(xMin + 1, atlasWidth));
    var yMax = Mathf.Clamp(Mathf.RoundToInt(rect.yMax), yMin + 1, Math.Max(yMin + 1, atlasHeight));
    return new PixelRect(xMin, yMin, xMax - xMin, yMax - yMin);
  }

  static int CountDistinctCellOrigins(IEnumerable<float> values) {
    const float epsilon = 0.01f;
    var orderedValues = values.OrderBy(value => value).ToList();
    if (orderedValues.Count <= 0) return 0;

    var count = 1;
    var previous = orderedValues[0];
    for (var i = 1; i < orderedValues.Count; i++) {
      if (Mathf.Abs(orderedValues[i] - previous) <= epsilon) continue;
      count++;
      previous = orderedValues[i];
    }

    return count;
  }

  internal static string NormalizeAssetPath(string assetPath) {
    return string.IsNullOrWhiteSpace(assetPath) ? "" : assetPath.Replace("\\", "/").Trim();
  }

  static bool IsSupportedSourceTextureAssetPath(string assetPath) {
    var extension = Path.GetExtension(assetPath);
    for (var i = 0; i < SupportedSourceExtensions.Length; i++) {
      if (string.Equals(extension, SupportedSourceExtensions[i], StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static bool IsGeneratedNormalAtlasAssetPath(string assetPath) {
    var fileName = Path.GetFileNameWithoutExtension(assetPath ?? "");
    return fileName.EndsWith("_N", StringComparison.OrdinalIgnoreCase);
  }

  static string ResolveSpriteName(Dictionary<int, string> namesByIndex, int index, string atlasName, int fallbackIndex) {
    if (namesByIndex != null && namesByIndex.TryGetValue(index, out var name) && !string.IsNullOrWhiteSpace(name)) {
      return name;
    }

    return atlasName + "_" + (fallbackIndex + 1);
  }

  string ResolveConfiguredOutputFolderPath() {
    if (outputFolder == null) return "";
    var folderPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(outputFolder));
    return AssetDatabase.IsValidFolder(folderPath) ? folderPath : "";
  }

  string ResolveOutputFolderPath(string sourcePath) {
    return ResolveOutputFolderPath(sourcePath, "");
  }

  string ResolveOutputFolderPath(string sourcePath, string sourceRootPath) {
    var configuredOutputFolderPath = ResolveConfiguredOutputFolderPath();
    if (!string.IsNullOrWhiteSpace(configuredOutputFolderPath)) {
      var relativeSourceFolder = BuildRelativeSourceFolderPath(sourcePath, sourceRootPath);
      if (!string.IsNullOrWhiteSpace(relativeSourceFolder)) {
        return NormalizeAssetPath(configuredOutputFolderPath + "/" + relativeSourceFolder);
      }

      return configuredOutputFolderPath;
    }

    var sourceDirectoryPath = NormalizeAssetPath(Path.GetDirectoryName(sourcePath));
    return sourceDirectoryPath;
  }

  static string BuildRelativeSourceFolderPath(string sourcePath, string sourceRootPath) {
    var normalizedRootPath = NormalizeAssetPath(sourceRootPath).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(normalizedRootPath)) return "";

    var normalizedSourceFolderPath = NormalizeAssetPath(Path.GetDirectoryName(sourcePath)).TrimEnd('/');
    if (string.Equals(normalizedSourceFolderPath, normalizedRootPath, StringComparison.OrdinalIgnoreCase)) {
      return "";
    }

    var prefix = normalizedRootPath + "/";
    if (!normalizedSourceFolderPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
      return "";
    }

    return normalizedSourceFolderPath.Substring(prefix.Length).Trim('/');
  }

  List<string> CollectSourceAtlasPaths(string sourceFolderPath) {
    var atlasPathsByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var normalizedSourceFolderPath = NormalizeAssetPath(sourceFolderPath).TrimEnd('/');
    var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { normalizedSourceFolderPath });
    for (var i = 0; i < textureGuids.Length; i++) {
      var assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(textureGuids[i]));
      if (string.IsNullOrWhiteSpace(assetPath)) continue;
      if (!IsSupportedSourceTextureAssetPath(assetPath)) continue;
      if (IsGeneratedNormalAtlasAssetPath(assetPath)) continue;
      if (ShouldSkipGeneratedOutput(assetPath)) continue;

      var parentFolderPath = NormalizeAssetPath(Path.GetDirectoryName(assetPath));
      if (!includeSubfolders && !string.Equals(parentFolderPath, normalizedSourceFolderPath, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      var key = parentFolderPath + "|" + Path.GetFileNameWithoutExtension(assetPath);
      if (atlasPathsByKey.TryGetValue(key, out var existingPath)) {
        atlasPathsByKey[key] = PreferSourceAtlasPath(existingPath, assetPath);
        continue;
      }

      atlasPathsByKey[key] = assetPath;
    }

    var atlasPaths = atlasPathsByKey.Values.ToList();
    atlasPaths.Sort(StringComparer.OrdinalIgnoreCase);
    return atlasPaths;
  }

  List<SourceAtlasExportBatch> CollectSourceAtlasExportBatches(string sourceFolderPath) {
    var sourceAtlasPaths = CollectSourceAtlasPaths(sourceFolderPath);
    var sourcePathsByFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < sourceAtlasPaths.Count; i++) {
      var sourcePath = sourceAtlasPaths[i];
      var parentFolderPath = NormalizeAssetPath(Path.GetDirectoryName(sourcePath));
      if (!sourcePathsByFolder.TryGetValue(parentFolderPath, out var folderSourcePaths)) {
        folderSourcePaths = new List<string>();
        sourcePathsByFolder[parentFolderPath] = folderSourcePaths;
      }

      folderSourcePaths.Add(sourcePath);
    }

    var exportBatches = new List<SourceAtlasExportBatch>(sourceAtlasPaths.Count);
    var orderedFolderPaths = sourcePathsByFolder.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    for (var folderIndex = 0; folderIndex < orderedFolderPaths.Count; folderIndex++) {
      var folderPath = orderedFolderPaths[folderIndex];
      var folderSourcePaths = sourcePathsByFolder[folderPath];
      var numericSourcePaths = new List<string>();
      var nonNumericSourcePaths = new List<string>();

      for (var sourceIndex = 0; sourceIndex < folderSourcePaths.Count; sourceIndex++) {
        var sourcePath = folderSourcePaths[sourceIndex];
        if (TryParseNumericSourceName(sourcePath, out _)) {
          numericSourcePaths.Add(sourcePath);
          continue;
        }

        nonNumericSourcePaths.Add(sourcePath);
      }

      if (numericSourcePaths.Count > 1) {
        numericSourcePaths.Sort(CompareNumericSourcePaths);
        var groupedOutputName = BuildNumericBatchOutputName(folderPath, numericSourcePaths);
        nonNumericSourcePaths.RemoveAll(sourcePath => ShouldSkipGroupedNumericOutput(sourcePath, groupedOutputName));
        exportBatches.Add(new SourceAtlasExportBatch {
          primarySourcePath = numericSourcePaths[0],
          outputName = groupedOutputName,
          groupedNumericSiblings = true,
          sourcePaths = numericSourcePaths
        });
      }
      else if (numericSourcePaths.Count == 1) {
        exportBatches.Add(BuildSingleSourceExportBatch(numericSourcePaths[0]));
      }

      nonNumericSourcePaths.Sort(StringComparer.OrdinalIgnoreCase);
      for (var sourceIndex = 0; sourceIndex < nonNumericSourcePaths.Count; sourceIndex++) {
        exportBatches.Add(BuildSingleSourceExportBatch(nonNumericSourcePaths[sourceIndex]));
      }
    }

    exportBatches.Sort((left, right) => string.Compare(left.primarySourcePath, right.primarySourcePath, StringComparison.OrdinalIgnoreCase));
    return exportBatches;
  }

  static void AddFailureLog(List<string> failureLogs, string sourcePath, string error) {
    if (failureLogs == null || failureLogs.Count >= 20) return;
    failureLogs.Add((sourcePath ?? "<unknown>") + " :: " + (string.IsNullOrWhiteSpace(error) ? "Unknown export failure." : error));
  }

  bool ShouldSkipGeneratedOutput(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return false;

    var fileName = Path.GetFileNameWithoutExtension(assetPath);
    var parentFolderPath = NormalizeAssetPath(Path.GetDirectoryName(assetPath));
    if (TryExtractGeneratedOutputBaseName(fileName, out var generatedBaseName)) {
      for (var i = 0; i < SupportedSourceExtensions.Length; i++) {
        var candidatePath = NormalizeAssetPath(parentFolderPath + "/" + generatedBaseName + SupportedSourceExtensions[i]);
        if (string.Equals(candidatePath, assetPath, StringComparison.OrdinalIgnoreCase)) continue;
        if (!File.Exists(Path.GetFullPath(candidatePath))) continue;
        return true;
      }
    }

    var prefix = GetSanitizedOutputPrefix();
    if (!writePrefixedCopy || string.IsNullOrWhiteSpace(prefix)) return false;

    var prefixWithSeparator = prefix + "_";
    if (!fileName.StartsWith(prefixWithSeparator, StringComparison.OrdinalIgnoreCase)) return false;

    var originalName = fileName.Substring(prefixWithSeparator.Length);
    if (string.IsNullOrWhiteSpace(originalName)) return false;

    for (var i = 0; i < SupportedSourceExtensions.Length; i++) {
      var candidatePath = NormalizeAssetPath(parentFolderPath + "/" + originalName + SupportedSourceExtensions[i]);
      if (string.Equals(candidatePath, assetPath, StringComparison.OrdinalIgnoreCase)) continue;
      if (!File.Exists(Path.GetFullPath(candidatePath))) continue;
      return true;
    }

    return false;
  }

  static string PreferSourceAtlasPath(string existingPath, string candidatePath) {
    var existingPriority = GetSourceExtensionPriority(existingPath);
    var candidatePriority = GetSourceExtensionPriority(candidatePath);
    if (candidatePriority < existingPriority) return candidatePath;
    if (candidatePriority > existingPriority) return existingPath;
    return string.Compare(candidatePath, existingPath, StringComparison.OrdinalIgnoreCase) < 0 ? candidatePath : existingPath;
  }

  static int GetSourceExtensionPriority(string assetPath) {
    var extension = Path.GetExtension(assetPath);
    for (var i = 0; i < SupportedSourceExtensions.Length; i++) {
      if (string.Equals(extension, SupportedSourceExtensions[i], StringComparison.OrdinalIgnoreCase)) {
        return i;
      }
    }

    return int.MaxValue;
  }

  static SourceAtlasExportBatch BuildSingleSourceExportBatch(string sourcePath) {
    return new SourceAtlasExportBatch {
      primarySourcePath = sourcePath,
      outputName = Path.GetFileNameWithoutExtension(sourcePath),
      groupedNumericSiblings = false,
      sourcePaths = new List<string> { sourcePath }
    };
  }

  static bool TryParseNumericSourceName(string sourcePath, out int numericValue) {
    numericValue = 0;
    if (string.IsNullOrWhiteSpace(sourcePath)) return false;
    return int.TryParse(Path.GetFileNameWithoutExtension(sourcePath), out numericValue);
  }

  bool TryExtractGeneratedOutputBaseName(string fileName, out string baseName) {
    baseName = "";
    if (string.IsNullOrWhiteSpace(fileName)) return false;

    var workingName = fileName.Trim();
    var prefix = GetSanitizedOutputPrefix();
    var prefixWithSeparator = prefix + "_";
    if (writePrefixedCopy &&
        !string.IsNullOrWhiteSpace(prefix) &&
        workingName.StartsWith(prefixWithSeparator, StringComparison.OrdinalIgnoreCase)) {
      workingName = workingName.Substring(prefixWithSeparator.Length);
    }

    var pageMarkerIndex = workingName.LastIndexOf("_p", StringComparison.OrdinalIgnoreCase);
    if (pageMarkerIndex <= 0 || pageMarkerIndex >= workingName.Length - 2) return false;
    for (var i = pageMarkerIndex + 2; i < workingName.Length; i++) {
      if (!char.IsDigit(workingName[i])) return false;
    }

    baseName = workingName.Substring(0, pageMarkerIndex);
    return !string.IsNullOrWhiteSpace(baseName);
  }

  static int CompareNumericSourcePaths(string leftPath, string rightPath) {
    var leftHasNumber = TryParseNumericSourceName(leftPath, out var leftValue);
    var rightHasNumber = TryParseNumericSourceName(rightPath, out var rightValue);
    if (leftHasNumber && rightHasNumber) {
      var numericComparison = leftValue.CompareTo(rightValue);
      if (numericComparison != 0) return numericComparison;
    }

    return string.Compare(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
  }

  static string BuildNumericBatchOutputName(string folderPath, List<string> sourcePaths) {
    var outputName = ExtractTrailingPathSegment(folderPath);
    if (string.IsNullOrWhiteSpace(outputName) && sourcePaths != null && sourcePaths.Count > 0) {
      outputName = Path.GetFileNameWithoutExtension(sourcePaths[0]);
    }

    if (sourcePaths != null) {
      for (var i = 0; i < sourcePaths.Count; i++) {
        if (!string.Equals(Path.GetFileNameWithoutExtension(sourcePaths[i]), outputName, StringComparison.OrdinalIgnoreCase)) continue;
        return outputName + "_atlas";
      }
    }

    return outputName;
  }

  static string ExtractTrailingPathSegment(string path) {
    var normalizedPath = NormalizeAssetPath(path).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(normalizedPath)) return "";
    var separatorIndex = normalizedPath.LastIndexOf('/');
    return separatorIndex >= 0 ? normalizedPath.Substring(separatorIndex + 1) : normalizedPath;
  }

  bool ShouldSkipGroupedNumericOutput(string assetPath, string groupedOutputName) {
    if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(groupedOutputName)) return false;

    var fileName = Path.GetFileNameWithoutExtension(assetPath);
    if (string.Equals(fileName, groupedOutputName, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }
    if (TryExtractGeneratedOutputBaseName(fileName, out var generatedBaseName) &&
        string.Equals(generatedBaseName, groupedOutputName, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    var prefix = GetSanitizedOutputPrefix();
    if (!writePrefixedCopy || string.IsNullOrWhiteSpace(prefix)) return false;
    if (string.Equals(fileName, prefix + "_" + groupedOutputName, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }
    return false;
  }

  string BuildOutputAtlasAssetPath(string outputName, string outputFolderPath) {
    var outputFileName = writePrefixedCopy && !string.IsNullOrWhiteSpace(GetSanitizedOutputPrefix())
      ? GetSanitizedOutputPrefix() + "_" + outputName + ".png"
      : outputName + ".png";
    return (outputFolderPath.TrimEnd('/') + "/" + outputFileName).Replace("\\", "/");
  }

  string GetSanitizedOutputPrefix() {
    if (string.IsNullOrWhiteSpace(outputPrefix)) return "";

    var invalidChars = Path.GetInvalidFileNameChars();
    var sanitizedChars = new char[outputPrefix.Length];
    var count = 0;
    for (var i = 0; i < outputPrefix.Length; i++) {
      var c = outputPrefix[i];
      if (Array.IndexOf(invalidChars, c) >= 0) {
        sanitizedChars[count++] = '_';
        continue;
      }

      sanitizedChars[count++] = c;
    }

    return new string(sanitizedChars, 0, count).Trim().Trim('_');
  }

  internal static void EnsureMetadataAddressable(string metadataAssetPath, bool saveAssets = true) {
    var normalizedAssetPath = (metadataAssetPath ?? "").Replace("\\", "/");
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return;

    var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
    if (settings == null) return;

    var group = settings.FindGroup(SpriteStreamingConfig.IndexAddressablesGroupName);
    if (group == null) {
      group = settings.CreateGroup(
        SpriteStreamingConfig.IndexAddressablesGroupName,
        false,
        false,
        false,
        null,
        typeof(BundledAssetGroupSchema),
        typeof(ContentUpdateGroupSchema)
      );
    }

    if (group == null) return;

    var guid = AssetDatabase.AssetPathToGUID(normalizedAssetPath);
    if (string.IsNullOrWhiteSpace(guid)) return;

    var entry = settings.FindAssetEntry(guid);
    if (entry == null || entry.parentGroup != group) {
      entry = settings.CreateOrMoveEntry(guid, group, false, false);
    }

    if (entry == null) return;
    if (!string.Equals(entry.address, normalizedAssetPath, StringComparison.Ordinal)) {
      entry.SetAddress(normalizedAssetPath, false);
    }
    settings.AddLabel(SpriteStreamingConfig.AtlasMetadataAddressablesLabel, false);
    entry.SetLabel(SpriteStreamingConfig.AtlasMetadataAddressablesLabel, true, true, false);

    EditorUtility.SetDirty(group);
    EditorUtility.SetDirty(settings);
    if (saveAssets) {
      AssetDatabase.SaveAssets();
    }
  }
}
#endif
