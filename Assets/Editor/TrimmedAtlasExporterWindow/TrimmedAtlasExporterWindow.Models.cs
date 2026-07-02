#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrimmedAtlasExporterWindow {
  [Serializable]
  sealed class TrimmedAtlasExport {
    public string metadataKind = "trimmed";
    public string sourceAtlasAssetPath;
    public string exportedAtlasAssetPath;
    public string coordinateOrigin = "bottom-left";
    public bool sliceExportedAtlas = true;
    public float spritePixelsPerUnit = 100f;
    public int spriteMeshType = (int)SpriteMeshType.Tight;
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
  sealed class RuntimeTrimmedAtlasExport {
    public string metadataKind = "trimmed";
    public string coordinateOrigin = "bottom-left";
    public string sourceAtlasAssetPath;
    public bool sliceExportedAtlas = true;
    public float spritePixelsPerUnit = 100f;
    public int spriteMeshType = (int)SpriteMeshType.Tight;
    public List<RuntimeTrimmedSpriteMetadata> sprites = new();
  }

  [Serializable]
  sealed class RuntimeTrimmedSpriteMetadata {
    public string name;
    public bool empty;
    public PixelRect packedRect;
    public PixelPoint offsetFromCellCenterPx;
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
    public string sourceFolderPath;
    public string primarySourcePath;
    public string outputName;
    public bool groupedNumericSiblings;
    public List<string> sourcePaths = new();
  }

  enum SourceAtlasBatchAnalysisStatus {
    Succeeded,
    Skipped,
    Failed
  }

  sealed class SourceAtlasScanResult {
    public List<string> sourceAtlasPaths = new();
    public int deletedGeneratedAtlasCount;
    public int deletedGeneratedMetadataCount;
  }

  sealed class SourceCellDefinition {
    public PixelRect sourceCell;
    public Rect logicalCellRect;
    public string spriteName;
  }

  sealed class VisiblePixelComponent {
    public readonly List<int> pixelIndices = new();
    public int minX = int.MaxValue;
    public int minY = int.MaxValue;
    public int maxX = int.MinValue;
    public int maxY = int.MinValue;

    public int pixelCount => pixelIndices.Count;
  }

  sealed class PendingTrimmedAtlasExport {
    public string sourceAtlasAssetPath;
    public string exportedAtlasAssetPath;
    public string runtimeMetadataAssetPath;
    public string editorImportMetadataJson;
    public TrimmedAtlasExport exportData;
  }

  sealed class PendingTrimmedSourceCleanup {
    public SourceAtlasExportBatch batch;
    public string exportedAtlasAssetPath;
    public List<PendingTrimmedAtlasExport> pendingExports = new();
    public List<string> sourcePathsToDelete = new();
  }

  [Serializable]
  sealed class ExistingTrimmedAtlasMetadata {
    public string metadataKind;
    public string coordinateOrigin;
    public string exportedAtlasAssetPath;
  }
}
#endif
