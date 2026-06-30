using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class GeneratedAtlasSpriteSynthesisUtility {
  const string GroupedMetadataKind = "grouped";

  [Serializable]
  public sealed class ImportPixelRect {
    public int x;
    public int y;
    public int width;
    public int height;
  }

  [Serializable]
  public sealed class AtlasImportPayload {
    public string metadataKind;
    public float spritePixelsPerUnit;
    public int spriteMeshType = -1;
    public List<AtlasSpriteImportPayload> sprites = new();
  }

  [Serializable]
  public sealed class AtlasSpriteImportPayload {
    public string name;
    public bool empty;
    public ImportPixelRect packedRect;
  }

  public static bool TryParseMetadata(TextAsset metadataAsset, out AtlasImportPayload payload) {
    return TryParseMetadataPayload(metadataAsset, out payload);
  }

  static bool TryParseMetadataPayload(TextAsset metadataAsset, out AtlasImportPayload payload) {
    payload = null;
    if (metadataAsset == null || string.IsNullOrWhiteSpace(metadataAsset.text)) return false;

    try {
      payload = JsonUtility.FromJson<AtlasImportPayload>(metadataAsset.text);
    }
    catch {
      payload = null;
      return false;
    }

    return payload != null && payload.sprites != null && payload.sprites.Count > 0;
  }

  public static SpriteMeshType ResolveMeshType(int spriteMeshType, SpriteMeshType fallbackMeshType) {
    return ResolveSpriteMeshType(spriteMeshType, fallbackMeshType);
  }

  public static Sprite CreateSpriteFromPayload(
    Texture2D atlasTexture,
    AtlasSpriteImportPayload spritePayload,
    float pixelsPerUnit,
    SpriteMeshType spriteMeshType
  ) {
    if (atlasTexture == null || spritePayload == null || spritePayload.empty) return null;
    if (string.IsNullOrWhiteSpace(spritePayload.name) || spritePayload.packedRect == null) return null;

    var rect = new Rect(
      spritePayload.packedRect.x,
      spritePayload.packedRect.y,
      spritePayload.packedRect.width,
      spritePayload.packedRect.height
    );
    if (rect.width <= 0f || rect.height <= 0f) return null;
    if (rect.xMin < 0f || rect.yMin < 0f || rect.xMax > atlasTexture.width || rect.yMax > atlasTexture.height) return null;

    var sprite = Sprite.Create(atlasTexture, rect, new Vector2(0.5f, 0.5f), pixelsPerUnit, 0u, spriteMeshType);
    if (sprite == null) return null;
    sprite.name = spritePayload.name.Trim();
    return sprite;
  }

  public static bool TryCreateGroupedSurrogateSprites(Sprite atlasSprite, TextAsset metadataAsset, out List<Sprite> sprites) {
    sprites = null;
    return atlasSprite != null && TryCreateGroupedSurrogateSprites(atlasSprite.texture, metadataAsset, out sprites);
  }

  public static bool TryCreateGroupedSurrogateSprites(Texture2D atlasTexture, TextAsset metadataAsset, out List<Sprite> sprites) {
    sprites = null;
    if (!TryCreateSpritesFromMetadata(
      atlasTexture,
      fallbackPixelsPerUnit: 100f,
      fallbackMeshType: SpriteMeshType.FullRect,
      metadataAsset,
      out sprites,
      out var metadataKind)) {
      return false;
    }

    if (string.IsNullOrWhiteSpace(metadataKind) ||
        string.Equals(metadataKind, GroupedMetadataKind, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    DestroySprites(sprites);
    sprites = null;
    return false;
  }

  public static bool TryCreateGroupedSurrogateSprite(
    Texture2D atlasTexture,
    TextAsset metadataAsset,
    string spriteName,
    out Sprite sprite
  ) {
    sprite = null;
    if (!TryCreateSpriteFromMetadata(
      atlasTexture,
      fallbackPixelsPerUnit: 100f,
      fallbackMeshType: SpriteMeshType.FullRect,
      metadataAsset,
      spriteName,
      out sprite,
      out var metadataKind)) {
      return false;
    }

    return string.IsNullOrWhiteSpace(metadataKind) ||
      string.Equals(metadataKind, GroupedMetadataKind, StringComparison.OrdinalIgnoreCase);
  }

  public static bool TryCreateSpritesFromMetadata(
    Texture2D atlasTexture,
    float fallbackPixelsPerUnit,
    SpriteMeshType fallbackMeshType,
    TextAsset metadataAsset,
    out List<Sprite> sprites,
    out string metadataKind
  ) {
    sprites = null;
    metadataKind = "";
    if (atlasTexture == null) return false;
    if (!TryParseMetadataPayload(metadataAsset, out var payload)) return false;
    metadataKind = payload.metadataKind ?? "";

    var pixelsPerUnit = payload.spritePixelsPerUnit > 0f ? payload.spritePixelsPerUnit : fallbackPixelsPerUnit;
    if (pixelsPerUnit <= 0f) pixelsPerUnit = 100f;

    var spriteMeshType = ResolveSpriteMeshType(payload.spriteMeshType, fallbackMeshType);
    sprites = new List<Sprite>(payload.sprites.Count);
    for (var i = 0; i < payload.sprites.Count; i++) {
      var sprite = CreateSpriteFromPayload(atlasTexture, payload.sprites[i], pixelsPerUnit, spriteMeshType);
      if (sprite == null) continue;
      sprites.Add(sprite);
    }

    return sprites.Count > 0;
  }

  public static bool TryCreateSpriteFromMetadata(
    Texture2D atlasTexture,
    float fallbackPixelsPerUnit,
    SpriteMeshType fallbackMeshType,
    TextAsset metadataAsset,
    string spriteName,
    out Sprite sprite,
    out string metadataKind
  ) {
    sprite = null;
    metadataKind = "";
    if (atlasTexture == null || string.IsNullOrWhiteSpace(spriteName)) return false;
    if (!TryParseMetadataPayload(metadataAsset, out var payload)) return false;
    metadataKind = payload.metadataKind ?? "";

    var targetName = spriteName.Trim();
    var pixelsPerUnit = payload.spritePixelsPerUnit > 0f ? payload.spritePixelsPerUnit : fallbackPixelsPerUnit;
    if (pixelsPerUnit <= 0f) pixelsPerUnit = 100f;

    var spriteMeshType = ResolveSpriteMeshType(payload.spriteMeshType, fallbackMeshType);
    for (var i = 0; i < payload.sprites.Count; i++) {
      var spritePayload = payload.sprites[i];
      if (spritePayload == null || string.IsNullOrWhiteSpace(spritePayload.name)) continue;
      if (!string.Equals(spritePayload.name.Trim(), targetName, StringComparison.Ordinal)) continue;
      sprite = CreateSpriteFromPayload(atlasTexture, spritePayload, pixelsPerUnit, spriteMeshType);
      return sprite != null;
    }

    return false;
  }

  public static bool IsOffsetOnlyRuntimeMetadata(TextAsset metadataAsset) {
    if (metadataAsset == null || string.IsNullOrWhiteSpace(metadataAsset.text)) return false;

    var json = metadataAsset.text;
    return json.IndexOf("\"offsetFromCellCenterPx\"", StringComparison.Ordinal) >= 0 &&
           json.IndexOf("\"packedRect\"", StringComparison.Ordinal) < 0;
  }

  static SpriteMeshType ResolveSpriteMeshType(int spriteMeshType, SpriteMeshType fallbackMeshType) {
    return Enum.IsDefined(typeof(SpriteMeshType), spriteMeshType)
      ? (SpriteMeshType)spriteMeshType
      : fallbackMeshType;
  }

  public static void DestroySprites(List<Sprite> sprites) {
    if (sprites == null || sprites.Count <= 0) return;
    for (var i = 0; i < sprites.Count; i++) {
      var sprite = sprites[i];
      if (sprite == null) continue;
      if (Application.isPlaying) {
        UnityEngine.Object.Destroy(sprite);
      }
      else {
        UnityEngine.Object.DestroyImmediate(sprite);
      }
    }
    sprites.Clear();
  }
}
