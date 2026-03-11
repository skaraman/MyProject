#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class GeneratedAtlasImportPostprocessor : AssetPostprocessor {
  const string TrimmedMetadataKind = "trimmed";
  const string GroupedMetadataKind = "grouped";

  [Serializable]
  sealed class ImportPixelRect {
    public int x;
    public int y;
    public int width;
    public int height;
  }

  [Serializable]
  sealed class TrimmedAtlasImportPayload {
    public string metadataKind;
    public string coordinateOrigin;
    public string sourceAtlasAssetPath;
    public bool sliceExportedAtlas = true;
    public float spritePixelsPerUnit;
    public int spriteMeshType = -1;
    public List<TrimmedSpriteImportPayload> sprites = new();
  }

  [Serializable]
  sealed class TrimmedSpriteImportPayload {
    public string name;
    public bool empty;
    public ImportPixelRect packedRect;
  }

  [Serializable]
  sealed class GroupedAtlasImportPayload {
    public string metadataKind;
    public string groupKey;
    public string representativeSourceAtlasAssetPath;
    public float spritePixelsPerUnit;
    public int spriteMeshType = -1;
    public List<GroupedSpriteImportPayload> sprites = new();
  }

  [Serializable]
  sealed class GroupedSpriteImportPayload {
    public string name;
    public bool empty;
    public string sourceAtlasAssetPath;
    public ImportPixelRect packedRect;
  }

  sealed class GeneratedAtlasImportDefinition {
    public string atlasAssetPath;
    public string metadataAssetPath;
    public string sourceAtlasAssetPath;
    public bool sliceAtlas;
    public float spritePixelsPerUnit = 100f;
    public int spriteMeshType = (int)SpriteMeshType.Tight;
    public bool hasImporterSnapshot;
    public List<SpriteMetaData> sprites = new();
  }

  void OnPreprocessTexture() {
    var importer = assetImporter as TextureImporter;
    if (importer == null) return;
    if (!TryBuildImportDefinition(assetPath, out var definition)) return;

    SpriteStreamingTextureImportPolicy.Apply(importer, definition.sliceAtlas);
    if (definition.hasImporterSnapshot) {
      TrimmedAtlasExporterWindow.ApplyImporterSnapshot(importer, definition.spritePixelsPerUnit, definition.spriteMeshType);
    }
    else if (!string.IsNullOrWhiteSpace(definition.sourceAtlasAssetPath)) {
      TrimmedAtlasExporterWindow.CopySourceImporterSettings(definition.sourceAtlasAssetPath, importer);
    }

    if (!importer.alphaIsTransparency) {
      importer.alphaIsTransparency = true;
    }

    if (definition.sliceAtlas) {
      if (importer.spriteImportMode != SpriteImportMode.Multiple) {
        importer.spriteImportMode = SpriteImportMode.Multiple;
      }

#pragma warning disable 618
      importer.spritesheet = definition.sprites.ToArray();
#pragma warning restore 618
      return;
    }

    if (importer.spriteImportMode != SpriteImportMode.Single) {
      importer.spriteImportMode = SpriteImportMode.Single;
    }

#pragma warning disable 618
    importer.spritesheet = Array.Empty<SpriteMetaData>();
#pragma warning restore 618
  }

  static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths) {
    var metadataAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var atlasAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    CollectProcessedAtlasPaths(importedAssets, metadataAssetPaths, atlasAssetPaths);
    CollectProcessedAtlasPaths(movedAssets, metadataAssetPaths, atlasAssetPaths);

    foreach (var metadataAssetPath in metadataAssetPaths) {
      TrimmedAtlasExporterWindow.EnsureMetadataAddressable(metadataAssetPath, saveAssets: false);
    }

    foreach (var atlasAssetPath in atlasAssetPaths) {
      TrimmedSpriteOffsetResolver.InvalidateAtlas(atlasAssetPath);
    }

    if (metadataAssetPaths.Count > 0) {
      AssetDatabase.SaveAssets();
    }

    if (deletedAssets == null) return;
    for (var i = 0; i < deletedAssets.Length; i++) {
      var deletedAssetPath = deletedAssets[i];
      if (string.IsNullOrWhiteSpace(deletedAssetPath)) continue;
      if (deletedAssetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) {
        TrimmedSpriteOffsetResolver.InvalidateAtlas(deletedAssetPath);
        continue;
      }

      if (!deletedAssetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
      TrimmedSpriteOffsetResolver.InvalidateAtlas(Path.ChangeExtension(deletedAssetPath, ".png"));
    }
  }

  static void CollectProcessedAtlasPaths(string[] assetPaths, HashSet<string> metadataAssetPaths, HashSet<string> atlasAssetPaths) {
    if (assetPaths == null) return;

    for (var i = 0; i < assetPaths.Length; i++) {
      var assetPath = assetPaths[i];
      if (string.IsNullOrWhiteSpace(assetPath)) continue;

      if (assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) {
        if (!TryBuildImportDefinition(assetPath, out var definition)) continue;
        metadataAssetPaths?.Add(definition.metadataAssetPath);
        atlasAssetPaths?.Add(definition.atlasAssetPath);
        continue;
      }

      if (!assetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
      if (!TryBuildImportDefinition(Path.ChangeExtension(assetPath, ".png"), out var definitionFromJson)) continue;
      metadataAssetPaths?.Add(definitionFromJson.metadataAssetPath);
    }
  }

  static bool TryBuildImportDefinition(string atlasAssetPath, out GeneratedAtlasImportDefinition definition) {
    definition = null;
    var normalizedAtlasAssetPath = TrimmedAtlasExporterWindow.NormalizeAssetPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasAssetPath) ||
        !normalizedAtlasAssetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    var metadataAssetPath = TrimmedAtlasExporterWindow.NormalizeAssetPath(Path.ChangeExtension(normalizedAtlasAssetPath, ".json"));
    var metadataFullPath = Path.GetFullPath(metadataAssetPath);
    if (!File.Exists(metadataFullPath)) return false;

    string json;
    try {
      json = File.ReadAllText(metadataFullPath);
    }
    catch {
      return false;
    }

    return TryBuildTrimmedImportDefinition(normalizedAtlasAssetPath, metadataAssetPath, json, out definition) ||
           TryBuildGroupedImportDefinition(normalizedAtlasAssetPath, metadataAssetPath, json, out definition);
  }

  static bool TryBuildTrimmedImportDefinition(string atlasAssetPath, string metadataAssetPath, string json, out GeneratedAtlasImportDefinition definition) {
    definition = null;
    if (string.IsNullOrWhiteSpace(json)) return false;

    TrimmedAtlasImportPayload payload;
    try {
      payload = JsonUtility.FromJson<TrimmedAtlasImportPayload>(json);
    }
    catch {
      return false;
    }

    if (payload == null || payload.sprites == null) return false;
    if (!string.IsNullOrWhiteSpace(payload.metadataKind) &&
        !string.Equals(payload.metadataKind, TrimmedMetadataKind, StringComparison.OrdinalIgnoreCase)) {
      return false;
    }
    if (string.IsNullOrWhiteSpace(payload.metadataKind) && string.IsNullOrWhiteSpace(payload.coordinateOrigin)) {
      return false;
    }

    definition = new GeneratedAtlasImportDefinition {
      atlasAssetPath = atlasAssetPath,
      metadataAssetPath = metadataAssetPath,
      sourceAtlasAssetPath = TrimmedAtlasExporterWindow.NormalizeAssetPath(payload.sourceAtlasAssetPath),
      sliceAtlas = payload.sliceExportedAtlas,
      spritePixelsPerUnit = payload.spritePixelsPerUnit,
      spriteMeshType = payload.spriteMeshType,
      hasImporterSnapshot = payload.spritePixelsPerUnit > 0f && payload.spriteMeshType >= 0
    };

    BuildSpriteMetadata(payload.sprites, definition.sprites);
    return !definition.sliceAtlas || definition.sprites.Count > 0;
  }

  static bool TryBuildGroupedImportDefinition(string atlasAssetPath, string metadataAssetPath, string json, out GeneratedAtlasImportDefinition definition) {
    definition = null;
    if (string.IsNullOrWhiteSpace(json)) return false;

    GroupedAtlasImportPayload payload;
    try {
      payload = JsonUtility.FromJson<GroupedAtlasImportPayload>(json);
    }
    catch {
      return false;
    }

    if (payload == null || payload.sprites == null) return false;
    if (!string.IsNullOrWhiteSpace(payload.metadataKind) &&
        !string.Equals(payload.metadataKind, GroupedMetadataKind, StringComparison.OrdinalIgnoreCase)) {
      return false;
    }
    if (string.IsNullOrWhiteSpace(payload.metadataKind) && string.IsNullOrWhiteSpace(payload.groupKey)) {
      return false;
    }

    definition = new GeneratedAtlasImportDefinition {
      atlasAssetPath = atlasAssetPath,
      metadataAssetPath = metadataAssetPath,
      sourceAtlasAssetPath = ResolveGroupedSourceAtlasAssetPath(payload),
      sliceAtlas = true,
      spritePixelsPerUnit = payload.spritePixelsPerUnit,
      spriteMeshType = payload.spriteMeshType,
      hasImporterSnapshot = payload.spritePixelsPerUnit > 0f && payload.spriteMeshType >= 0
    };

    BuildSpriteMetadata(payload.sprites, definition.sprites);
    return definition.sprites.Count > 0;
  }

  static string ResolveGroupedSourceAtlasAssetPath(GroupedAtlasImportPayload payload) {
    if (payload == null) return "";
    var representativeSourceAtlasAssetPath = TrimmedAtlasExporterWindow.NormalizeAssetPath(payload.representativeSourceAtlasAssetPath);
    if (!string.IsNullOrWhiteSpace(representativeSourceAtlasAssetPath)) return representativeSourceAtlasAssetPath;
    if (payload.sprites == null) return "";

    for (var i = 0; i < payload.sprites.Count; i++) {
      var sourceAtlasAssetPath = TrimmedAtlasExporterWindow.NormalizeAssetPath(payload.sprites[i]?.sourceAtlasAssetPath);
      if (!string.IsNullOrWhiteSpace(sourceAtlasAssetPath)) return sourceAtlasAssetPath;
    }

    return "";
  }

  static void BuildSpriteMetadata(List<TrimmedSpriteImportPayload> sprites, List<SpriteMetaData> spriteMetaData) {
    if (sprites == null || spriteMetaData == null) return;

    for (var i = 0; i < sprites.Count; i++) {
      var sprite = sprites[i];
      if (sprite == null || sprite.empty) continue;
      if (!TryBuildSpriteMetaData(sprite.name, sprite.packedRect, out var spriteData)) continue;
      spriteMetaData.Add(spriteData);
    }
  }

  static void BuildSpriteMetadata(List<GroupedSpriteImportPayload> sprites, List<SpriteMetaData> spriteMetaData) {
    if (sprites == null || spriteMetaData == null) return;

    for (var i = 0; i < sprites.Count; i++) {
      var sprite = sprites[i];
      if (sprite == null) continue;
      if (!TryBuildSpriteMetaData(sprite.name, sprite.packedRect, out var spriteData)) continue;
      spriteMetaData.Add(spriteData);
    }
  }

  static bool TryBuildSpriteMetaData(string spriteName, ImportPixelRect packedRect, out SpriteMetaData spriteMetaData) {
    spriteMetaData = default;
    if (string.IsNullOrWhiteSpace(spriteName) || packedRect == null) return false;
    if (packedRect.width <= 0 || packedRect.height <= 0) return false;

    spriteMetaData = new SpriteMetaData {
      name = spriteName,
      rect = new Rect(packedRect.x, packedRect.y, packedRect.width, packedRect.height),
      alignment = (int)SpriteAlignment.Center,
      pivot = new Vector2(0.5f, 0.5f),
      border = Vector4.zero
    };
    return true;
  }
}
#endif
