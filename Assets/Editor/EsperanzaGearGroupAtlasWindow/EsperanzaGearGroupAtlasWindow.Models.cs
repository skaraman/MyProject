#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed partial class EsperanzaGearGroupAtlasWindow : EditorWindow {
  [Serializable]
  sealed class GroupedAtlasMetadataPayload {
    public string metadataKind = "grouped";
    public string groupKey;
    public string category;
    public string form;
    public string variant;
    public string partCode;
    public string fileBase;
    public string sourceKind;
    public string representativeSourceAtlasAssetPath;
    public float spritePixelsPerUnit = 100f;
    public int spriteMeshType = (int)SpriteMeshType.Tight;
    public int sourceAtlasCount;
    public int pageIndex;
    public int atlasWidth;
    public int atlasHeight;
    public int padding;
    public List<string> sourceCategories = new();
    public List<GroupedAtlasSpriteMetadata> sprites = new();
  }

  [Serializable]
  sealed class GroupedAtlasRuntimePayload {
    public string metadataKind = "grouped";
    public float spritePixelsPerUnit = 100f;
    public int spriteMeshType = (int)SpriteMeshType.Tight;
    public List<GroupedAtlasRuntimeSpriteMetadata> sprites = new();
  }

  [Serializable]
  sealed class GroupedAtlasRuntimeSpriteMetadata {
    public string name;
    public bool empty;
    public PixelRect packedRect;
  }

  [Serializable]
  sealed class GroupedAtlasSpriteMetadata {
    public string name;
    public bool empty;
    public string sourceCategory;
    public string sourceAtlasAssetPath;
    public string sourceSpriteName;
    public string sourcePartCode;
    public PixelRect trimRectInSourceSprite;
    public PixelRect packedRect;
    public PixelPoint offsetFromCellCenterPx;
  }

  [Serializable]
  sealed class ExistingTrimmedAtlasMetadataPayload {
    public string metadataKind;
    public string coordinateOrigin;
    public List<ExistingTrimmedAtlasSpriteMetadata> sprites = new();
  }

  [Serializable]
  sealed class ExistingTrimmedAtlasSpriteMetadata {
    public string name;
    public bool empty;
    public PixelRect trimRectInCell;
    public PixelRect packedRect;
    public PixelPoint offsetFromCellCenterPx;
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

  sealed class SourceAtlasRecord {
    public string category;
    public string form;
    public string variant;
    public string partCode;
    public string atlasPath;
    public string normalAtlasPath;
    public string fileBase;
  }

  sealed class GroupCandidate {
    public string form;
    public string variant;
    public string partCode;
    public bool isSkin;
    public List<SourceAtlasRecord> sourceAtlases = new();
    public List<string> sourceCategories = new();
    public int normalAtlasCount;
  }

  sealed class LoadedAtlas {
    public string atlasPath;
    public Texture2D texture;
    public Color32[] pixels;
    public List<Sprite> orderedSprites = new();
    public Dictionary<string, Sprite> spritesByName = new(StringComparer.Ordinal);
    public Dictionary<string, ExistingTrimmedAtlasSpriteMetadata> trimmedSourceMetadataByName = new(StringComparer.Ordinal);
  }

  sealed class PackedSpriteBuildItem {
    public string outputSpriteName;
    public string sourceCategory;
    public string colorSourceAtlasPath;
    public string normalSourceAtlasPath;
    public string sourceSpriteName;
    public string sourcePartCode;
    public bool empty;
    public PixelRect trimRectInSourceSprite;
    public PixelRect packedRect;
    public PixelPoint offsetFromCellCenterPx;
    public Color32[] colorPixels;
    public Color32[] normalPixels;
    public int pageIndex;
    public bool inheritedTrimMetadata;

    public int Width => Math.Max(1, trimRectInSourceSprite.width);
    public int Height => Math.Max(1, trimRectInSourceSprite.height);
  }

  sealed class AtlasPage {
    public int pageIndex;
    public int width;
    public int height;
    public List<PackedSpriteBuildItem> items = new();
    public string colorAtlasPath;
    public string normalAtlasPath;
  }

  sealed class CleanupPlan {
    public string folderPath;
    public string filePrefix;
    public bool isSkinLibrary;
    public HashSet<string> keepAssetPaths = new(StringComparer.OrdinalIgnoreCase);
  }

  sealed class ExportCleanupSummary {
    public int deletedAssetCount;
    public int deletedFolderCount;
  }

  readonly struct SpriteAssetReference : IEquatable<SpriteAssetReference> {
    public readonly string guid;
    public readonly long localFileId;
    public readonly string assetPath;
    public readonly string spriteName;

    public SpriteAssetReference(string guid, long localFileId, string assetPath, string spriteName) {
      this.guid = guid ?? "";
      this.localFileId = localFileId;
      this.assetPath = assetPath ?? "";
      this.spriteName = spriteName ?? "";
    }

    public bool IsValid => !string.IsNullOrWhiteSpace(guid);

    public bool Equals(SpriteAssetReference other) {
      return localFileId == other.localFileId &&
             string.Equals(guid, other.guid, StringComparison.Ordinal) &&
             string.Equals(spriteName, other.spriteName, StringComparison.Ordinal);
    }

    public override bool Equals(object obj) {
      return obj is SpriteAssetReference other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = 17;
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(guid ?? "");
        hash = (hash * 31) + localFileId.GetHashCode();
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(spriteName ?? "");
        return hash;
      }
    }
  }

  sealed class PendingGroupedAtlasImport {
    public GroupCandidate candidate;
  }

  readonly struct LibraryEntryScopeKey : IEquatable<LibraryEntryScopeKey> {
    public readonly bool isNormal;
    public readonly bool isSkinLibrary;
    public readonly string category;
    public readonly string partCode;

    public LibraryEntryScopeKey(bool isNormal, bool isSkinLibrary, string category, string partCode) {
      this.isNormal = isNormal;
      this.isSkinLibrary = isSkinLibrary;
      this.category = category ?? "";
      this.partCode = partCode ?? "";
    }

    public bool Equals(LibraryEntryScopeKey other) {
      return isNormal == other.isNormal &&
             isSkinLibrary == other.isSkinLibrary &&
             string.Equals(category, other.category, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(partCode, other.partCode, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object obj) {
      return obj is LibraryEntryScopeKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = 17;
        hash = (hash * 31) + isNormal.GetHashCode();
        hash = (hash * 31) + isSkinLibrary.GetHashCode();
        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(category ?? "");
        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(partCode ?? "");
        return hash;
      }
    }
  }

  readonly struct LibraryEntryKey : IEquatable<LibraryEntryKey> {
    public readonly LibraryEntryScopeKey scopeKey;
    public readonly string label;

    public LibraryEntryKey(bool isNormal, bool isSkinLibrary, string category, string partCode, string label) {
      scopeKey = new LibraryEntryScopeKey(isNormal, isSkinLibrary, category, partCode);
      this.label = label ?? "";
    }

    public bool Equals(LibraryEntryKey other) {
      return scopeKey.Equals(other.scopeKey) &&
             string.Equals(label, other.label, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object obj) {
      return obj is LibraryEntryKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = scopeKey.GetHashCode();
        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(label ?? "");
        return hash;
      }
    }
  }

  readonly struct LibraryEntrySequenceKey : IEquatable<LibraryEntrySequenceKey> {
    public readonly LibraryEntryScopeKey scopeKey;
    public readonly string labelPrefix;

    public LibraryEntrySequenceKey(bool isNormal, bool isSkinLibrary, string category, string partCode, string labelPrefix) {
      scopeKey = new LibraryEntryScopeKey(isNormal, isSkinLibrary, category, partCode);
      this.labelPrefix = labelPrefix ?? "";
    }

    public bool Equals(LibraryEntrySequenceKey other) {
      return scopeKey.Equals(other.scopeKey) &&
             string.Equals(labelPrefix, other.labelPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object obj) {
      return obj is LibraryEntrySequenceKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = scopeKey.GetHashCode();
        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(labelPrefix ?? "");
        return hash;
      }
    }
  }

  sealed class RebindLabelCleanupPlan {
    public HashSet<string> expectedLabels = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ownedLabelPrefixes = new(StringComparer.OrdinalIgnoreCase);
    public bool deleteNumericLabels;
  }

  sealed class GroupedSpriteReplacementIndex {
    public Dictionary<LibraryEntryKey, SpriteAssetReference> spritesByKey = new();
    public Dictionary<LibraryEntryScopeKey, Dictionary<string, SpriteAssetReference>> labelsByScope = new();
    public Dictionary<LibraryEntryScopeKey, RebindLabelCleanupPlan> cleanupByScope = new();
    public List<CleanupPlan> cleanupPlans = new();
    public int metadataFileCount;
    public int indexedSpriteCount;
    public int duplicateKeyCount;
    public int filledSliceGapCount;
    public List<string> duplicateKeySamples = new();
  }

  sealed class SpriteLibraryCategoryPlan {
    public Dictionary<string, SpriteAssetReference> replacementsByLabel;
    public RebindLabelCleanupPlan cleanupPlan;
  }

  sealed class PendingGroupedSpriteReplacement {
    public SpriteAssetReference replacementSprite;
    public string atlasAssetPath;
    public string groupedSpriteName;
    public string sourceAtlasAssetPath;
    public string sourceSpriteName;
    public string sourceCategory;
    public string form;
    public string variant;
  }
}
#endif
