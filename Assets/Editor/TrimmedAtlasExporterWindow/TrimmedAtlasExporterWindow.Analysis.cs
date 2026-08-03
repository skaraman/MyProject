#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed partial class TrimmedAtlasExporterWindow {
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
        var buildData = AnalyzeCell(sourcePath, previewTexture.width, sourcePixels, sourceCell.sourceCell, sourceCell.logicalCellRect, sourceCell.spriteName, i);
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
        sliceExportedAtlas = createAtlasSlices,
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
      CopySourceImporterSnapshot(sourcePath, exportData);

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
    if (ShouldUseConfiguredGrid(previewTexture, out resolvedCellWidth, out resolvedCellHeight)) {
      if (sourceSprites != null && sourceSprites.Count > 0) {
        AtlasAuthoringLog.Verbose(
          "[TrimAtlasExport] Using configured grid as authoritative trim layout." +
          " source='" + sourcePath + "'" +
          " configured_cell=" + resolvedCellWidth + "x" + resolvedCellHeight +
          " imported_sprite_count=" + sourceSprites.Count);
      }

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

  bool ShouldUseConfiguredGrid(Texture2D previewTexture, out int resolvedCellWidth, out int resolvedCellHeight) {
    resolvedCellWidth = Mathf.Max(1, cellWidth);
    resolvedCellHeight = Mathf.Max(1, cellHeight);
    if (previewTexture == null) return false;
    if (previewTexture.width < resolvedCellWidth || previewTexture.height < resolvedCellHeight) return false;
    if ((previewTexture.width % resolvedCellWidth) != 0) return false;
    if ((previewTexture.height % resolvedCellHeight) != 0) return false;
    return true;
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

    var orderedSprites = new List<Sprite>(sprites);
    orderedSprites.Sort(CompareImportedSourceSprites);

    var sourceCells = new List<SourceCellDefinition>(orderedSprites.Count);
    var distinctColumns = new HashSet<int>();
    var distinctRows = new HashSet<int>();
    var widthSum = 0f;
    var heightSum = 0f;
    var minWidth = float.MaxValue;
    var maxWidth = float.MinValue;
    var minHeight = float.MaxValue;
    var maxHeight = float.MinValue;
    for (var i = 0; i < orderedSprites.Count; i++) {
      var sprite = orderedSprites[i];
      var rect = sprite.rect;
      var roundedRect = RoundSpriteRectToPixelRect(rect, previewTexture.width, previewTexture.height);
      if (roundedRect.width <= 0 || roundedRect.height <= 0) {
        error =
          "Sprite '" + sprite.name +
          "' in '" + sourcePath +
          "' resolved to an invalid pixel rect during trim analysis.";
        return null;
      }

      sourceCells.Add(new SourceCellDefinition {
        sourceCell = roundedRect,
        logicalCellRect = rect,
        spriteName = preserveSpriteNames && !string.IsNullOrWhiteSpace(sprite.name)
          ? sprite.name
          : Path.GetFileNameWithoutExtension(sourcePath) + "_" + (i + 1)
      });

      distinctColumns.Add(Mathf.RoundToInt(rect.xMin));
      distinctRows.Add(Mathf.RoundToInt(rect.yMin));
      widthSum += rect.width;
      heightSum += rect.height;
      if (rect.width < minWidth) minWidth = rect.width;
      if (rect.width > maxWidth) maxWidth = rect.width;
      if (rect.height < minHeight) minHeight = rect.height;
      if (rect.height > maxHeight) maxHeight = rect.height;
    }

    columns = distinctColumns.Count;
    rows = distinctRows.Count;
    resolvedCellWidth = Mathf.Max(1, Mathf.RoundToInt(widthSum / orderedSprites.Count));
    resolvedCellHeight = Mathf.Max(1, Mathf.RoundToInt(heightSum / orderedSprites.Count));
    if (columns > 0 && rows > 0 && (columns * rows) != orderedSprites.Count) {
      AtlasAuthoringLog.VerboseWarning(
        "[TrimAtlasExport] Imported sprite layout is sparse or irregular." +
        " source='" + sourcePath + "'" +
        " sprite_count=" + orderedSprites.Count +
        " grid=" + columns + "x" + rows);
    }

    var frameRangeDiffersFromConfigured =
      Mathf.Abs(minWidth - cellWidth) > 0.01f ||
      Mathf.Abs(maxWidth - cellWidth) > 0.01f ||
      Mathf.Abs(minHeight - cellHeight) > 0.01f ||
      Mathf.Abs(maxHeight - cellHeight) > 0.01f;
    if (frameRangeDiffersFromConfigured) {
      AtlasAuthoringLog.Verbose(
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
      AtlasAuthoringLog.Verbose(
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

  static bool ShouldSkipSourceAtlasForFolderExport(string error) {
    return !string.IsNullOrWhiteSpace(error) &&
           error.StartsWith("Atlas dimensions are smaller than the configured cell size.", StringComparison.Ordinal);
  }

  SourceAtlasBatchAnalysisStatus AnalyzeSourceAtlasBatch(
    SourceAtlasExportBatch batch,
    string outputFolderPath,
    out TrimmedAtlasExport exportData,
    out List<TrimmedSpriteBuildData> buildItems,
    out List<string> analyzedSourcePaths,
    out int skippedSourceCount,
    out string error) {
    exportData = null;
    buildItems = null;
    analyzedSourcePaths = new List<string>();
    skippedSourceCount = 0;
    error = "";
    if (batch == null || batch.sourcePaths == null || batch.sourcePaths.Count <= 0) {
      error = "No source atlases were supplied for folder export.";
      return SourceAtlasBatchAnalysisStatus.Failed;
    }

    if (batch.sourcePaths.Count == 1) {
      if (!TryAnalyzeSourceAtlas(batch.primarySourcePath, outputFolderPath, out exportData, out buildItems, out var previewTexture, out error)) {
        if (ShouldSkipSourceAtlasForFolderExport(error)) {
          skippedSourceCount = 1;
          error = "";
          return SourceAtlasBatchAnalysisStatus.Skipped;
        }

        return SourceAtlasBatchAnalysisStatus.Failed;
      }

      if (previewTexture != null) {
        DestroyImmediate(previewTexture);
      }

      analyzedSourcePaths.Add(batch.primarySourcePath);
      exportData.exportedAtlasAssetPath = BuildOutputAtlasAssetPath(batch.outputName, outputFolderPath);
      return SourceAtlasBatchAnalysisStatus.Succeeded;
    }

    var mergedBuildItems = new List<TrimmedSpriteBuildData>();
    var totalEmptyCellCount = 0;
    long packedArea = 0;
    long totalSourceArea = 0;
    var representativeSourcePath = "";
    var sourceWidth = 0;
    var sourceHeight = 0;
    var columns = 0;
    var rows = 0;

    for (var sourceIndex = 0; sourceIndex < batch.sourcePaths.Count; sourceIndex++) {
      var sourcePath = batch.sourcePaths[sourceIndex];
      Texture2D previewTexture = null;
      try {
        if (!TryAnalyzeSourceAtlas(sourcePath, outputFolderPath, out var sourceExportData, out var sourceBuildItems, out previewTexture, out error)) {
          if (ShouldSkipSourceAtlasForFolderExport(error)) {
            skippedSourceCount++;
            error = "";
            continue;
          }

          return SourceAtlasBatchAnalysisStatus.Failed;
        }

        if (sourceExportData == null || sourceBuildItems == null) {
          error = "No analyzed atlas data was returned for '" + sourcePath + "'.";
          return SourceAtlasBatchAnalysisStatus.Failed;
        }

        if (string.IsNullOrWhiteSpace(representativeSourcePath)) {
          representativeSourcePath = sourcePath;
        }

        analyzedSourcePaths.Add(sourcePath);
        if (analyzedSourcePaths.Count == 1) {
          sourceWidth = sourceExportData.sourceWidth;
          sourceHeight = sourceExportData.sourceHeight;
          columns = sourceExportData.columns;
          rows = sourceExportData.rows;
        }
        else if (sourceExportData.sourceWidth != sourceWidth ||
                 sourceExportData.sourceHeight != sourceHeight ||
                 sourceExportData.columns != columns ||
                 sourceExportData.rows != rows) {
          AtlasAuthoringLog.VerboseWarning(
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

    if (analyzedSourcePaths.Count <= 0) {
      error = "";
      return SourceAtlasBatchAnalysisStatus.Skipped;
    }

    var exportedBuildItems = BuildExportBuildItems(mergedBuildItems);
    if (exportedBuildItems.Count <= 0) {
      error = "The grouped atlas batch has no visible slices to export after trimming.";
      return SourceAtlasBatchAnalysisStatus.Failed;
    }

    for (var i = 0; i < exportedBuildItems.Count; i++) {
      packedArea += (long)exportedBuildItems[i].Width * exportedBuildItems[i].Height;
    }

    if (!TryPackTrimmedSprites(exportedBuildItems, out var packedWidth, out var packedHeight, out error)) {
      return SourceAtlasBatchAnalysisStatus.Failed;
    }

    exportData = new TrimmedAtlasExport {
      sourceAtlasAssetPath = representativeSourcePath,
      exportedAtlasAssetPath = BuildOutputAtlasAssetPath(batch.outputName, outputFolderPath),
      sourceAtlasCount = analyzedSourcePaths.Count,
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
    CopySourceImporterSnapshot(representativeSourcePath, exportData);

    buildItems = mergedBuildItems;
    for (var i = 0; i < exportedBuildItems.Count; i++) {
      exportData.sprites.Add(exportedBuildItems[i].metadata);
    }

    return SourceAtlasBatchAnalysisStatus.Succeeded;
  }

  TrimmedSpriteBuildData AnalyzeCell(string sourcePath, int atlasWidth, Color32[] sourcePixels, PixelRect sourceCell, Rect logicalCellRect, string spriteName, int index) {
    var localPixelCount = sourceCell.width * sourceCell.height;
    var visibleMask = RentAnalysisBoolScratch(ref analysisVisibleMaskScratch, localPixelCount);
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
        visibleMask[(localY * sourceCell.width) + localX] = true;
        if (localX < minX) minX = localX;
        if (localY < minY) minY = localY;
        if (localX > maxX) maxX = localX;
        if (localY > maxY) maxY = localY;
        visiblePixelCount++;
        weightedSumX += localOriginX + localX + 0.5;
        weightedSumY += localOriginY + localY + 0.5;
      }
    }

    var rawTrimWidth = maxX >= minX ? (maxX - minX + 1) : 0;
    var rawTrimHeight = maxY >= minY ? (maxY - minY + 1) : 0;
    var rawTrimArea = rawTrimWidth * rawTrimHeight;
    if (visiblePixelCount > strayIslandMaxPixels &&
        rawTrimArea > (visiblePixelCount * 10) &&
        TryApplyDistantStrayIslandCutoff(sourcePath, spriteName, index, sourceCell, visibleMask, out var filteredVisiblePixelCount)) {
      minX = sourceCell.width;
      minY = sourceCell.height;
      maxX = -1;
      maxY = -1;
      weightedSumX = 0.0;
      weightedSumY = 0.0;
      visiblePixelCount = filteredVisiblePixelCount;

      for (var localY = 0; localY < sourceCell.height; localY++) {
        for (var localX = 0; localX < sourceCell.width; localX++) {
          if (!visibleMask[(localY * sourceCell.width) + localX]) continue;
          if (localX < minX) minX = localX;
          if (localY < minY) minY = localY;
          if (localX > maxX) maxX = localX;
          if (localY > maxY) maxY = localY;
          weightedSumX += localOriginX + localX + 0.5;
          weightedSumY += localOriginY + localY + 0.5;
        }
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

  bool TryApplyDistantStrayIslandCutoff(
    string sourcePath,
    string spriteName,
    int index,
    PixelRect sourceCell,
    bool[] visibleMask,
    out long filteredVisiblePixelCount) {
    filteredVisiblePixelCount = 0;
    if (!ignoreDistantStrayIslands || visibleMask == null) return false;
    if (strayIslandGapCutoffPx <= 0 || strayIslandMaxPixels <= 0) return false;
    if (!TryBuildVisiblePixelComponents(visibleMask, sourceCell.width, sourceCell.height, out var components)) return false;
    if (components.Count <= 1) return false;

    VisiblePixelComponent largestComponent = null;
    for (var i = 0; i < components.Count; i++) {
      var component = components[i];
      if (largestComponent == null || component.pixelCount > largestComponent.pixelCount) {
        largestComponent = component;
      }
    }

    if (largestComponent == null || largestComponent.pixelCount <= strayIslandMaxPixels) return false;

    var anchorComponents = new List<VisiblePixelComponent>();
    for (var i = 0; i < components.Count; i++) {
      var component = components[i];
      if (component == null || component.pixelCount <= strayIslandMaxPixels) continue;
      anchorComponents.Add(component);
    }

    var ignoredPixelCount = 0;
    var ignoredComponentCount = 0;
    var maxIgnoredGapPx = 0f;
    for (var i = 0; i < components.Count; i++) {
      var component = components[i];
      if (component.pixelCount > strayIslandMaxPixels) continue;

      var gapPx = CalculateClosestComponentGapPx(anchorComponents, component);
      if (gapPx <= strayIslandGapCutoffPx) continue;

      ignoredComponentCount++;
      ignoredPixelCount += component.pixelCount;
      if (gapPx > maxIgnoredGapPx) maxIgnoredGapPx = gapPx;
      for (var pixelIndex = 0; pixelIndex < component.pixelIndices.Count; pixelIndex++) {
        visibleMask[component.pixelIndices[pixelIndex]] = false;
      }
    }

    if (ignoredPixelCount <= 0) return false;

    filteredVisiblePixelCount = 0;
    for (var i = 0; i < components.Count; i++) {
      var component = components[i];
      if (component.pixelCount > strayIslandMaxPixels ||
          CalculateClosestComponentGapPx(anchorComponents, component) <= strayIslandGapCutoffPx) {
        filteredVisiblePixelCount += component.pixelCount;
      }
    }

    AtlasAuthoringLog.Verbose(
      "[TrimAtlasExport] Ignored distant stray sprite pixels." +
      " source='" + sourcePath + "'" +
      " sprite='" + spriteName + "'" +
      " index=" + index +
      " ignored_components=" + ignoredComponentCount +
      " ignored_pixels=" + ignoredPixelCount +
      " gap_cutoff=" + strayIslandGapCutoffPx +
      " max_pixels=" + strayIslandMaxPixels +
      " max_ignored_gap_px=" + maxIgnoredGapPx.ToString("0.###") +
      " source_cell=" + sourceCell.width + "x" + sourceCell.height);
    return true;
  }

  bool TryBuildVisiblePixelComponents(bool[] visibleMask, int width, int height, out List<VisiblePixelComponent> components) {
    components = new List<VisiblePixelComponent>();
    var pixelCount = width * height;
    if (visibleMask == null || width <= 0 || height <= 0 || visibleMask.Length < pixelCount) return false;

    var visited = RentAnalysisBoolScratch(ref analysisVisitedScratch, pixelCount);
    var queue = RentAnalysisIntScratch(ref analysisQueueScratch, pixelCount);
    for (var i = 0; i < pixelCount; i++) {
      if (!visibleMask[i] || visited[i]) continue;

      var component = new VisiblePixelComponent();
      var queueHead = 0;
      var queueTail = 0;
      queue[queueTail++] = i;
      visited[i] = true;
      while (queueHead < queueTail) {
        var current = queue[queueHead++];
        var x = current % width;
        var y = current / width;
        component.pixelIndices.Add(current);
        if (x < component.minX) component.minX = x;
        if (y < component.minY) component.minY = y;
        if (x > component.maxX) component.maxX = x;
        if (y > component.maxY) component.maxY = y;

        var minNeighborY = Math.Max(0, y - 1);
        var maxNeighborY = Math.Min(height - 1, y + 1);
        var minNeighborX = Math.Max(0, x - 1);
        var maxNeighborX = Math.Min(width - 1, x + 1);
        for (var neighborY = minNeighborY; neighborY <= maxNeighborY; neighborY++) {
          var neighborRowOffset = neighborY * width;
          for (var neighborX = minNeighborX; neighborX <= maxNeighborX; neighborX++) {
            var neighborIndex = neighborRowOffset + neighborX;
            if (neighborIndex == current || !visibleMask[neighborIndex] || visited[neighborIndex]) continue;
            visited[neighborIndex] = true;
            queue[queueTail++] = neighborIndex;
          }
        }
      }

      components.Add(component);
    }

    return true;
  }

  static float CalculateComponentGapPx(VisiblePixelComponent primaryComponent, VisiblePixelComponent candidateComponent) {
    if (primaryComponent == null || candidateComponent == null) return 0f;

    var gapX = CalculateAxisGap(primaryComponent.minX, primaryComponent.maxX, candidateComponent.minX, candidateComponent.maxX);
    var gapY = CalculateAxisGap(primaryComponent.minY, primaryComponent.maxY, candidateComponent.minY, candidateComponent.maxY);
    if (gapX <= 0 && gapY <= 0) return 0f;
    return Mathf.Sqrt((gapX * gapX) + (gapY * gapY));
  }

  static float CalculateClosestComponentGapPx(List<VisiblePixelComponent> anchorComponents, VisiblePixelComponent candidateComponent) {
    if (candidateComponent == null || anchorComponents == null || anchorComponents.Count <= 0) return 0f;

    var closestGapPx = float.MaxValue;
    for (var i = 0; i < anchorComponents.Count; i++) {
      var anchorComponent = anchorComponents[i];
      var gapPx = CalculateComponentGapPx(anchorComponent, candidateComponent);
      if (gapPx < closestGapPx) closestGapPx = gapPx;
      if (closestGapPx <= 0f) return 0f;
    }

    return closestGapPx == float.MaxValue ? 0f : closestGapPx;
  }

  static int CalculateAxisGap(int firstMin, int firstMax, int secondMin, int secondMax) {
    if (firstMax < secondMin) return Math.Max(0, secondMin - firstMax - 1);
    if (secondMax < firstMin) return Math.Max(0, firstMin - secondMax - 1);
    return 0;
  }

  static int CompareImportedSourceSprites(Sprite left, Sprite right) {
    if (ReferenceEquals(left, right)) return 0;
    if (left == null) return -1;
    if (right == null) return 1;

    var nameComparison = SpriteSliceAddressUtility.NaturalStringComparer.Compare(left.name, right.name);
    if (nameComparison != 0) return nameComparison;

    var yComparison = left.rect.yMin.CompareTo(right.rect.yMin);
    if (yComparison != 0) return yComparison;

    return left.rect.xMin.CompareTo(right.rect.xMin);
  }

  static bool[] RentAnalysisBoolScratch(ref bool[] scratch, int requiredLength) {
    if (requiredLength <= 0) {
      return Array.Empty<bool>();
    }

    if (scratch == null || scratch.Length < requiredLength) {
      scratch = new bool[requiredLength];
    }
    else {
      Array.Clear(scratch, 0, requiredLength);
    }

    return scratch;
  }

  static int[] RentAnalysisIntScratch(ref int[] scratch, int requiredLength) {
    if (requiredLength <= 0) {
      return Array.Empty<int>();
    }

    if (scratch == null || scratch.Length < requiredLength) {
      scratch = new int[requiredLength];
    }

    return scratch;
  }

  bool IsVisible(Color32 color) {
    if (color.a <= alphaThreshold) return false;
    if (!treatNearWhiteAsEmpty) return true;
    return color.r < nearWhiteThreshold || color.g < nearWhiteThreshold || color.b < nearWhiteThreshold;
  }

  Color32[] CopyTrimmedPixels(Color32[] sourcePixels, int atlasWidth, PixelRect sourceCell, PixelRect trimRect) {
    var trimmedPixels = new Color32[trimRect.width * trimRect.height];
    for (var y = 0; y < trimRect.height; y++) {
      var srcY = sourceCell.y + trimRect.y + y;
      var srcIndex = (srcY * atlasWidth) + sourceCell.x + trimRect.x;
      var dstIndex = y * trimRect.width;
      Array.Copy(sourcePixels, srcIndex, trimmedPixels, dstIndex, trimRect.width);
    }

    return trimmedPixels;
  }

}
#endif
