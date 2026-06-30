#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public sealed class GeneratedAtlasImportPostprocessor : AssetPostprocessor {
  const string TrimmedMetadataKind = "trimmed";
  const string GroupedMetadataKind = "grouped";
  static bool pendingSpriteWithNormalsRefresh;

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
      ApplySpriteEditorData(importer, definition.sprites, clearSprites: false);
      return;
    }

    ApplySingleSpriteImportMode(importer);
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
      SpriteIndexBuilder.InvalidateCachedSpriteSliceEstimate(atlasAssetPath);
    }

    if (metadataAssetPaths.Count > 0) {
      AssetDatabase.SaveAssets();
    }

    if (atlasAssetPaths.Count > 0) {
      SpriteWithNormals.InvalidateEditorRuntimeAtlasAvailabilityCache();
      QueueSpriteWithNormalsRefresh();
    }

    if (deletedAssets == null) return;
    for (var i = 0; i < deletedAssets.Length; i++) {
      var deletedAssetPath = deletedAssets[i];
      if (string.IsNullOrWhiteSpace(deletedAssetPath)) continue;
      if (IsSupportedGeneratedAtlasTextureAssetPath(deletedAssetPath)) {
        TrimmedSpriteOffsetResolver.InvalidateAtlas(deletedAssetPath);
        continue;
      }

      if (!deletedAssetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
      var runtimeMetadataAssetPath = TrimmedAtlasExporterWindow.ResolveRuntimeMetadataAssetPath(deletedAssetPath);
      if (string.IsNullOrWhiteSpace(runtimeMetadataAssetPath)) continue;
      InvalidateSiblingGeneratedAtlas(Path.ChangeExtension(runtimeMetadataAssetPath, ".png"));
      InvalidateSiblingGeneratedAtlas(Path.ChangeExtension(runtimeMetadataAssetPath, ".jpg"));
      InvalidateSiblingGeneratedAtlas(Path.ChangeExtension(runtimeMetadataAssetPath, ".jpeg"));
    }
  }

  static void CollectProcessedAtlasPaths(string[] assetPaths, HashSet<string> metadataAssetPaths, HashSet<string> atlasAssetPaths) {
    if (assetPaths == null) return;

    for (var i = 0; i < assetPaths.Length; i++) {
      var assetPath = assetPaths[i];
      if (string.IsNullOrWhiteSpace(assetPath)) continue;

      if (IsSupportedGeneratedAtlasTextureAssetPath(assetPath)) {
        if (!TryBuildImportDefinition(assetPath, out var definition)) continue;
        metadataAssetPaths?.Add(definition.metadataAssetPath);
        atlasAssetPaths?.Add(definition.atlasAssetPath);
        continue;
      }

      if (!assetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
      if (!TryBuildImportDefinitionForMetadataAsset(assetPath, out var definitionFromJson)) continue;
      metadataAssetPaths?.Add(definitionFromJson.metadataAssetPath);
      atlasAssetPaths?.Add(definitionFromJson.atlasAssetPath);
    }
  }

  static void QueueSpriteWithNormalsRefresh() {
    if (pendingSpriteWithNormalsRefresh) return;
    pendingSpriteWithNormalsRefresh = true;
    EditorApplication.delayCall += RefreshSpriteWithNormalsPreviews;
  }

  static void RefreshSpriteWithNormalsPreviews() {
    pendingSpriteWithNormalsRefresh = false;
    var targets = Resources.FindObjectsOfTypeAll<SpriteWithNormals>();
    if (targets == null || targets.Length <= 0) return;

    var refreshedCount = 0;
    for (var i = 0; i < targets.Length; i++) {
      var target = targets[i];
      if (target == null || EditorUtility.IsPersistent(target)) continue;
      if (!target.gameObject.scene.IsValid()) continue;
      if (!target.enabled) continue;

      var refreshFrame = target.IsAnimation ? Mathf.Max(target.LastRequestedFrame, 1) : 0;
      target.ForceUpdateSpriteAndNormal(refreshFrame);
      refreshedCount++;
    }

    if (refreshedCount > 0) {
      Debug.Log("[SpriteWithNormals] Refreshed edit-mode previews after atlas import. targets=" + refreshedCount);
    }
  }

  static bool TryBuildImportDefinition(string atlasAssetPath, out GeneratedAtlasImportDefinition definition) {
    definition = null;
    var normalizedAtlasAssetPath = TrimmedAtlasExporterWindow.NormalizeAssetPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasAssetPath) ||
        !IsSupportedGeneratedAtlasTextureAssetPath(normalizedAtlasAssetPath)) {
      return false;
    }

    var runtimeMetadataAssetPath = TrimmedAtlasExporterWindow.BuildRuntimeMetadataAssetPath(normalizedAtlasAssetPath);
    if (string.IsNullOrWhiteSpace(runtimeMetadataAssetPath)) return false;

    if (GeneratedAtlasImportMetadataStore.TryRead(normalizedAtlasAssetPath, out var importerJson)) {
      if (TryBuildImportDefinitionFromJson(normalizedAtlasAssetPath, runtimeMetadataAssetPath, importerJson, out definition)) {
        return true;
      }
    }

    var editorMetadataAssetPath = TrimmedAtlasExporterWindow.BuildEditorMetadataAssetPath(normalizedAtlasAssetPath);
    return TryBuildImportDefinitionFromMetadataJson(normalizedAtlasAssetPath, runtimeMetadataAssetPath, editorMetadataAssetPath, out definition) ||
           TryBuildImportDefinitionFromMetadataJson(normalizedAtlasAssetPath, runtimeMetadataAssetPath, runtimeMetadataAssetPath, out definition);
  }

  static bool TryBuildImportDefinitionForMetadataAsset(string metadataAssetPath, out GeneratedAtlasImportDefinition definition) {
    definition = null;
    if (string.IsNullOrWhiteSpace(metadataAssetPath) ||
        !metadataAssetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    var runtimeMetadataAssetPath = TrimmedAtlasExporterWindow.ResolveRuntimeMetadataAssetPath(metadataAssetPath);
    if (string.IsNullOrWhiteSpace(runtimeMetadataAssetPath)) return false;

    var pngAtlasAssetPath = Path.ChangeExtension(runtimeMetadataAssetPath, ".png");
    if (TryBuildImportDefinition(pngAtlasAssetPath, out definition)) return true;

    var jpgAtlasAssetPath = Path.ChangeExtension(runtimeMetadataAssetPath, ".jpg");
    if (TryBuildImportDefinition(jpgAtlasAssetPath, out definition)) return true;

    var jpegAtlasAssetPath = Path.ChangeExtension(runtimeMetadataAssetPath, ".jpeg");
    return TryBuildImportDefinition(jpegAtlasAssetPath, out definition);
  }

  static bool TryBuildImportDefinitionFromMetadataJson(
    string atlasAssetPath,
    string runtimeMetadataAssetPath,
    string metadataReadAssetPath,
    out GeneratedAtlasImportDefinition definition) {
    definition = null;
    if (!TryReadMetadataJson(metadataReadAssetPath, out var json)) return false;

    return TryBuildImportDefinitionFromJson(atlasAssetPath, runtimeMetadataAssetPath, json, out definition);
  }

  static bool TryBuildImportDefinitionFromJson(
    string atlasAssetPath,
    string runtimeMetadataAssetPath,
    string json,
    out GeneratedAtlasImportDefinition definition) {
    definition = null;
    return TryBuildTrimmedImportDefinition(atlasAssetPath, runtimeMetadataAssetPath, json, out definition) ||
           TryBuildGroupedImportDefinition(atlasAssetPath, runtimeMetadataAssetPath, json, out definition);
  }

  static bool TryReadMetadataJson(string metadataAssetPath, out string json) {
    json = "";
    var normalizedMetadataAssetPath = TrimmedAtlasExporterWindow.NormalizeAssetPath(metadataAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedMetadataAssetPath)) return false;

    var metadataFullPath = Path.GetFullPath(normalizedMetadataAssetPath);
    if (!File.Exists(metadataFullPath)) return false;

    try {
      json = File.ReadAllText(metadataFullPath);
      return !string.IsNullOrWhiteSpace(json);
    }
    catch {
      return false;
    }
  }

  static bool IsSupportedGeneratedAtlasTextureAssetPath(string assetPath) {
    return !string.IsNullOrWhiteSpace(assetPath) &&
           (assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            assetPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            assetPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
  }

  static void ApplySingleSpriteImportMode(TextureImporter importer) {
    if (importer == null) return;

    var settings = new TextureImporterSettings();
    importer.ReadTextureSettings(settings);
    if (settings.spriteMode != (int)SpriteImportMode.Single) {
      settings.spriteMode = (int)SpriteImportMode.Single;
      importer.SetTextureSettings(settings);
    }

    if (importer.spriteImportMode != SpriteImportMode.Single) {
      importer.spriteImportMode = SpriteImportMode.Single;
    }

    ClearSpriteEditorData(importer);
  }

  static void ClearSpriteEditorData(TextureImporter importer) {
    if (importer == null) return;

    var dataProvider = CreateSpriteEditorDataProvider(importer);
    if (dataProvider == null) return;

    var existingRects = dataProvider.GetSpriteRects();
    var existingCount = existingRects == null ? 0 : existingRects.Length;
    if (existingCount <= 0 && !dataProvider.HasDataProvider(typeof(ISpriteNameFileIdDataProvider))) return;

    dataProvider.SetSpriteRects(Array.Empty<SpriteRect>());

    if (dataProvider.HasDataProvider(typeof(ISpriteNameFileIdDataProvider))) {
      var nameFileIdProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
      nameFileIdProvider.SetNameFileIdPairs(Array.Empty<SpriteNameFileIdPair>());
    }

    dataProvider.Apply();

    if (existingCount > 0 && GeneratedAtlasBuildSurrogateUtility.IsContentStagePath(importer.assetPath)) {
      Debug.Log(
        "[GeneratedAtlasImport] Cleared staged atlas sprite sheet data." +
        " path='" + importer.assetPath + "'" +
        " prior_sprite_rects=" + existingCount);
    }
  }

  static void InvalidateSiblingGeneratedAtlas(string atlasAssetPath) {
    var normalizedAtlasAssetPath = TrimmedAtlasExporterWindow.NormalizeAssetPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasAssetPath)) return;
    TrimmedSpriteOffsetResolver.InvalidateAtlas(normalizedAtlasAssetPath);
    SpriteIndexBuilder.InvalidateCachedSpriteSliceEstimate(normalizedAtlasAssetPath);
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
      sliceAtlas = !GeneratedAtlasBuildSurrogateUtility.ShouldImportGroupedAtlasAsSingleSprite(atlasAssetPath),
      spritePixelsPerUnit = payload.spritePixelsPerUnit,
      spriteMeshType = payload.spriteMeshType,
      hasImporterSnapshot = payload.spritePixelsPerUnit > 0f && payload.spriteMeshType >= 0
    };

    BuildSpriteMetadata(payload.sprites, definition.sprites);
    return !definition.sliceAtlas || definition.sprites.Count > 0;
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

    SortSpriteMetadata(spriteMetaData);
  }

  static void BuildSpriteMetadata(List<GroupedSpriteImportPayload> sprites, List<SpriteMetaData> spriteMetaData) {
    if (sprites == null || spriteMetaData == null) return;

    for (var i = 0; i < sprites.Count; i++) {
      var sprite = sprites[i];
      if (sprite == null) continue;
      if (!TryBuildSpriteMetaData(sprite.name, sprite.packedRect, out var spriteData)) continue;
      spriteMetaData.Add(spriteData);
    }

    SortSpriteMetadata(spriteMetaData);
  }

  static void SortSpriteMetadata(List<SpriteMetaData> spriteMetaData) {
    if (spriteMetaData == null || spriteMetaData.Count <= 1) return;
    spriteMetaData.Sort(CompareSpriteMetaData);
  }

  static int CompareSpriteMetaData(SpriteMetaData left, SpriteMetaData right) {
    var nameCompare = SpriteSliceAddressUtility.CompareNaturally(left.name, right.name);
    if (nameCompare != 0) return nameCompare;

    var yCompare = left.rect.yMin.CompareTo(right.rect.yMin);
    if (yCompare != 0) return yCompare;

    var xCompare = left.rect.xMin.CompareTo(right.rect.xMin);
    if (xCompare != 0) return xCompare;

    var heightCompare = left.rect.height.CompareTo(right.rect.height);
    if (heightCompare != 0) return heightCompare;

    return left.rect.width.CompareTo(right.rect.width);
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

  static void ApplySpriteEditorData(TextureImporter importer, List<SpriteMetaData> sprites, bool clearSprites) {
    var dataProvider = CreateSpriteEditorDataProvider(importer);
    if (dataProvider == null) {
      Debug.LogError("[GeneratedAtlasImport] Failed to create sprite data provider for '" + importer.assetPath + "'.");
      return;
    }

    var existingSpriteIds = CollectExistingSpriteIds(dataProvider);
    var spriteRects = clearSprites ? Array.Empty<SpriteRect>() : BuildSpriteRects(sprites, existingSpriteIds);
    dataProvider.SetSpriteRects(spriteRects);

    if (dataProvider.HasDataProvider(typeof(ISpriteNameFileIdDataProvider))) {
      var nameFileIdProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
      var pairs = spriteRects
        .Select(spriteRect => new SpriteNameFileIdPair(spriteRect.name, spriteRect.spriteID))
        .ToList();
      nameFileIdProvider.SetNameFileIdPairs(pairs);
    }
    else if (!clearSprites) {
      Debug.LogWarning(
        "[GeneratedAtlasImport] Sprite name/fileID provider unavailable for '" + importer.assetPath +
        "'. spriteCount=" + spriteRects.Length);
    }

    dataProvider.Apply();
  }

  static ISpriteEditorDataProvider CreateSpriteEditorDataProvider(TextureImporter importer) {
    var factory = new SpriteDataProviderFactories();
    factory.Init();
    var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer) as ISpriteEditorDataProvider;
    dataProvider?.InitSpriteEditorDataProvider();
    return dataProvider;
  }

  static Dictionary<string, GUID> CollectExistingSpriteIds(ISpriteEditorDataProvider dataProvider) {
    var spriteIds = new Dictionary<string, GUID>(StringComparer.Ordinal);
    if (dataProvider == null) return spriteIds;

    var existingRects = dataProvider.GetSpriteRects();
    if (existingRects != null) {
      for (var i = 0; i < existingRects.Length; i++) {
        var spriteRect = existingRects[i];
        if (string.IsNullOrWhiteSpace(spriteRect.name) || IsEmptyGuid(spriteRect.spriteID)) continue;
        spriteIds[spriteRect.name] = spriteRect.spriteID;
      }
    }

    if (!dataProvider.HasDataProvider(typeof(ISpriteNameFileIdDataProvider))) return spriteIds;
    return spriteIds;
  }

  static SpriteRect[] BuildSpriteRects(List<SpriteMetaData> sprites, Dictionary<string, GUID> existingSpriteIds) {
    if (sprites == null || sprites.Count == 0) return Array.Empty<SpriteRect>();

    var spriteRects = new SpriteRect[sprites.Count];
    for (var i = 0; i < sprites.Count; i++) {
      var sprite = sprites[i];
      var spriteId = ResolveSpriteId(sprite.name, existingSpriteIds);
      spriteRects[i] = new SpriteRect {
        name = sprite.name,
        spriteID = spriteId,
        rect = sprite.rect,
        alignment = ConvertAlignment(sprite.alignment),
        pivot = sprite.pivot,
        border = sprite.border
      };
    }

    return spriteRects;
  }

  static GUID ResolveSpriteId(string spriteName, Dictionary<string, GUID> existingSpriteIds) {
    if (!string.IsNullOrWhiteSpace(spriteName) &&
        existingSpriteIds != null &&
        existingSpriteIds.TryGetValue(spriteName, out var spriteId) &&
        !IsEmptyGuid(spriteId)) {
      return spriteId;
    }

    return GUID.Generate();
  }

  static bool IsEmptyGuid(GUID value) {
    return string.Equals(value.ToString(), default(GUID).ToString(), StringComparison.Ordinal);
  }

  static SpriteAlignment ConvertAlignment(int alignment) {
    if (!Enum.IsDefined(typeof(SpriteAlignment), alignment)) {
      return SpriteAlignment.Center;
    }

    return (SpriteAlignment)alignment;
  }
}
#endif
