#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed partial class TrimmedAtlasExporterWindow {
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
      AtlasAuthoringLog.Warning(
        "[TrimAtlasExport] Trimmed sprite exceeds padded atlas width." +
        " sprite='" + spriteName + "'" +
        " sprite_width=" + widestSprite +
        " max_content_width=" + maxContentWidth +
        " atlas_limit=" + targetWidth +
        " padding=" + padding);
      return false;
    }

    var ordered = BuildTrimmedPackSequence(items);

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
      AtlasAuthoringLog.Warning(
        "[TrimAtlasExport] Packed atlas width exceeded limit after packing." +
        " packed_width=" + packedWidth +
        " atlas_limit=" + targetWidth +
        " padding=" + padding);
      return false;
    }

    return true;
  }

  static List<TrimmedSpriteBuildData> BuildTrimmedPackSequence(IEnumerable<TrimmedSpriteBuildData> items) {
    if (items == null) return new List<TrimmedSpriteBuildData>();

    var ordered = new List<TrimmedSpriteBuildData>();
    foreach (var item in items) {
      if (item == null || item.metadata == null) continue;
      ordered.Add(item);
    }

    return ordered;
  }

  Texture2D BuildPackedTexture(int packedWidth, int packedHeight, List<TrimmedSpriteBuildData> items) {
    var atlasTexture = new Texture2D(packedWidth, packedHeight, TextureFormat.RGBA32, false);
    atlasTexture.filterMode = FilterMode.Point;
    atlasTexture.wrapMode = TextureWrapMode.Clamp;
    atlasTexture.SetPixels32(BuildPackedPixels(packedWidth, packedHeight, items));
    atlasTexture.Apply(false, false);
    return atlasTexture;
  }

  static Color32[] BuildPackedPixels(int packedWidth, int packedHeight, List<TrimmedSpriteBuildData> items) {
    var packedPixels = new Color32[Math.Max(1, packedWidth * packedHeight)];
    if (items == null || items.Count <= 0) {
      return packedPixels;
    }

    for (var i = 0; i < items.Count; i++) {
      var item = items[i];
      if (item == null || item.metadata == null || item.trimmedPixels == null) {
        continue;
      }

      var rect = item.metadata.packedRect;
      if (rect.width <= 0 || rect.height <= 0) {
        continue;
      }

      for (var row = 0; row < rect.height; row++) {
        var srcIndex = row * rect.width;
        var dstIndex = ((rect.y + row) * packedWidth) + rect.x;
        Array.Copy(item.trimmedPixels, srcIndex, packedPixels, dstIndex, rect.width);
      }
    }

    return packedPixels;
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
      var runtimeMetadataAssetPath = WriteMetadataJson(exportedAtlasPath, exportData);
      pendingExport = new PendingTrimmedAtlasExport {
        sourceAtlasAssetPath = sourcePath,
        exportedAtlasAssetPath = exportedAtlasPath,
        runtimeMetadataAssetPath = runtimeMetadataAssetPath,
        editorImportMetadataJson = BuildEditorImportMetadataJson(exportData),
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
    List<string> sourceAssetPaths,
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

    var resolvedOutputName = ResolveSafeOutputName(outputName, outputFolderPath, sourceAssetPaths);
    exportData.exportedAtlasAssetPath = BuildOutputAtlasAssetPath(resolvedOutputName, outputFolderPath);
    DeleteExistingOutputTargets(outputFolderPath, resolvedOutputName, sourcePath);

    var pageCount = CalculateExportPageCount(exportedBuildItems.Count);
    if (pageCount <= 1) {
      exportData.exportedAtlasAssetPath = BuildOutputAtlasAssetPath(resolvedOutputName, outputFolderPath);
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
      var pageOutputName = BuildPagedOutputName(resolvedOutputName, pageIndex);
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

  string ResolveSafeOutputName(string outputName, string outputFolderPath, List<string> sourceAssetPaths) {
    var resolvedOutputName = string.IsNullOrWhiteSpace(outputName) ? "trimmed" : outputName;
    if (!DoesOutputPathCollideWithSources(resolvedOutputName, outputFolderPath, sourceAssetPaths)) {
      return resolvedOutputName;
    }

    var baseOutputName = resolvedOutputName + "_atlas";
    resolvedOutputName = baseOutputName;
    var suffix = 2;
    while (DoesOutputPathCollideWithSources(resolvedOutputName, outputFolderPath, sourceAssetPaths)) {
      resolvedOutputName = baseOutputName + suffix;
      suffix++;
    }

    AtlasAuthoringLog.Verbose(
      "[TrimAtlasExport] Adjusted output name to avoid overwriting a source atlas." +
      " requested_output='" + outputName + "'" +
      " resolved_output='" + resolvedOutputName + "'" +
      " output_folder='" + outputFolderPath + "'");
    return resolvedOutputName;
  }

  bool DoesOutputPathCollideWithSources(string outputName, string outputFolderPath, List<string> sourceAssetPaths) {
    if (sourceAssetPaths == null || sourceAssetPaths.Count <= 0) {
      return false;
    }

    var outputAssetPath = NormalizeAssetPath(BuildOutputAtlasAssetPath(outputName, outputFolderPath));
    if (string.IsNullOrWhiteSpace(outputAssetPath)) {
      return false;
    }

    for (var i = 0; i < sourceAssetPaths.Count; i++) {
      var sourceAssetPath = NormalizeAssetPath(sourceAssetPaths[i]);
      if (string.IsNullOrWhiteSpace(sourceAssetPath)) {
        continue;
      }

      if (string.Equals(outputAssetPath, sourceAssetPath, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
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
      sliceExportedAtlas = sourceExportData.sliceExportedAtlas,
      spritePixelsPerUnit = sourceExportData.spritePixelsPerUnit,
      spriteMeshType = sourceExportData.spriteMeshType,
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

  void DeleteExistingOutputTargets(string outputFolderPath, string outputName, string sourcePath) {
    var normalizedOutputFolderPath = NormalizeAssetPath(outputFolderPath);
    if (string.IsNullOrWhiteSpace(normalizedOutputFolderPath) || string.IsNullOrWhiteSpace(outputName)) return;

    var outputFolderFullPath = Path.GetFullPath(normalizedOutputFolderPath);
    if (!Directory.Exists(outputFolderFullPath)) return;

    var normalizedSourcePath = NormalizeAssetPath(sourcePath);
    var candidateFiles = Directory.GetFiles(outputFolderFullPath, "*.png", SearchOption.TopDirectoryOnly);
    var deletedAssetCount = 0;
    var deletedMetadataOnlyCount = 0;
    for (var i = 0; i < candidateFiles.Length; i++) {
      if (!TryConvertFullPathToAssetPath(candidateFiles[i], out var candidateAssetPath)) continue;
      if (!MatchesGeneratedOutputName(candidateAssetPath, outputName)) continue;

      var normalizedCandidateAssetPath = NormalizeAssetPath(candidateAssetPath);
      if (string.Equals(normalizedCandidateAssetPath, normalizedSourcePath, StringComparison.OrdinalIgnoreCase)) {
        DeleteMetadataAsset(BuildRuntimeMetadataAssetPath(normalizedCandidateAssetPath));
        DeleteMetadataAsset(BuildEditorMetadataAssetPath(normalizedCandidateAssetPath));
        deletedMetadataOnlyCount++;
        continue;
      }

      DeleteGeneratedOutputAssetPair(normalizedCandidateAssetPath);
      deletedAssetCount++;
    }

    if (deletedAssetCount <= 0 && deletedMetadataOnlyCount <= 0) return;

    AtlasAuthoringLog.Verbose(
      "[TrimAtlasExport] Cleared existing target outputs before overwrite." +
      " output_folder='" + normalizedOutputFolderPath + "'" +
      " output_name='" + outputName + "'" +
      " deleted_assets=" + deletedAssetCount +
      " deleted_metadata_only=" + deletedMetadataOnlyCount);
  }

}
#endif
