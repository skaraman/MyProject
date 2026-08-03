#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed partial class TrimmedAtlasExporterWindow {
  string WriteMetadataJson(string exportedAtlasAssetPath, TrimmedAtlasExport exportData) {
    var runtimeMetadataAssetPath = BuildRuntimeMetadataAssetPath(exportedAtlasAssetPath);
    var editorMetadataAssetPath = BuildEditorMetadataAssetPath(exportedAtlasAssetPath);
    var runtimeMetadata = BuildRuntimeMetadata(exportData);
    WriteMetadataPayload(runtimeMetadataAssetPath, JsonUtility.ToJson(runtimeMetadata, true));
    DeleteMetadataAsset(editorMetadataAssetPath);
    return runtimeMetadataAssetPath;
  }

  static string BuildEditorImportMetadataJson(TrimmedAtlasExport exportData) {
    return exportData == null ? "" : JsonUtility.ToJson(exportData, true);
  }

  static RuntimeTrimmedAtlasExport BuildRuntimeMetadata(TrimmedAtlasExport exportData) {
    var runtimeMetadata = new RuntimeTrimmedAtlasExport();
    if (exportData == null) return runtimeMetadata;

    runtimeMetadata.metadataKind = exportData.metadataKind;
    runtimeMetadata.coordinateOrigin = exportData.coordinateOrigin;
    runtimeMetadata.sourceAtlasAssetPath = exportData.sourceAtlasAssetPath;
    runtimeMetadata.sliceExportedAtlas = exportData.sliceExportedAtlas;
    runtimeMetadata.spritePixelsPerUnit = exportData.spritePixelsPerUnit;
    runtimeMetadata.spriteMeshType = exportData.spriteMeshType;
    if (exportData?.sprites == null || exportData.sprites.Count <= 0) return runtimeMetadata;

    runtimeMetadata.sprites.Capacity = exportData.sprites.Count;
    for (var i = 0; i < exportData.sprites.Count; i++) {
      var sprite = exportData.sprites[i];
      if (sprite == null || string.IsNullOrWhiteSpace(sprite.name)) continue;
      runtimeMetadata.sprites.Add(new RuntimeTrimmedSpriteMetadata {
        name = sprite.name,
        empty = sprite.empty,
        packedRect = sprite.packedRect,
        offsetFromCellCenterPx = sprite.offsetFromCellCenterPx
      });
    }
    return runtimeMetadata;
  }

  static void WriteMetadataPayload(string metadataAssetPath, string jsonText) {
    var normalizedAssetPath = NormalizeAssetPath(metadataAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return;

    var metadataFullPath = Path.GetFullPath(normalizedAssetPath);
    Directory.CreateDirectory(Path.GetDirectoryName(metadataFullPath) ?? "");
    File.WriteAllText(metadataFullPath, jsonText ?? "");
  }

  static void CopySourceImporterSnapshot(string sourceAtlasAssetPath, TrimmedAtlasExport exportData) {
    if (exportData == null) return;
    GetSourceImporterSnapshot(sourceAtlasAssetPath, out exportData.spritePixelsPerUnit, out exportData.spriteMeshType);
  }

  internal static void GetSourceImporterSnapshot(string sourceAtlasAssetPath, out float spritePixelsPerUnit, out int spriteMeshType) {
    spritePixelsPerUnit = 100f;
    spriteMeshType = (int)SpriteMeshType.Tight;
    if (string.IsNullOrWhiteSpace(sourceAtlasAssetPath)) return;

    var sourceImporter = AssetImporter.GetAtPath(sourceAtlasAssetPath) as TextureImporter;
    if (sourceImporter == null) return;

    spritePixelsPerUnit = sourceImporter.spritePixelsPerUnit;
    var sourceSettings = new TextureImporterSettings();
    sourceImporter.ReadTextureSettings(sourceSettings);
    spriteMeshType = (int)sourceSettings.spriteMeshType;
  }

  internal static bool ApplyImporterSnapshot(TextureImporter targetImporter, float spritePixelsPerUnit, int spriteMeshType) {
    if (targetImporter == null) return false;

    var changed = false;
    if (spritePixelsPerUnit > 0f && !Mathf.Approximately(targetImporter.spritePixelsPerUnit, spritePixelsPerUnit)) {
      targetImporter.spritePixelsPerUnit = spritePixelsPerUnit;
      changed = true;
    }

    var targetSettings = new TextureImporterSettings();
    targetImporter.ReadTextureSettings(targetSettings);
    var resolvedSpriteMeshType = Enum.IsDefined(typeof(SpriteMeshType), spriteMeshType)
      ? (SpriteMeshType)spriteMeshType
      : SpriteMeshType.Tight;
    if (targetSettings.spriteMeshType != resolvedSpriteMeshType) {
      targetSettings.spriteMeshType = resolvedSpriteMeshType;
      targetImporter.SetTextureSettings(targetSettings);
      changed = true;
    }

    return changed;
  }

  internal static bool CopySourceImporterSettings(string sourceAtlasAssetPath, TextureImporter targetImporter) {
    GetSourceImporterSnapshot(sourceAtlasAssetPath, out var spritePixelsPerUnit, out var spriteMeshType);
    return ApplyImporterSnapshot(targetImporter, spritePixelsPerUnit, spriteMeshType);
  }
}
#endif
