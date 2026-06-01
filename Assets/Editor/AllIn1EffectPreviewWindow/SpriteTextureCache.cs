#if UNITY_EDITOR
using UnityEngine;

public sealed class SpriteTextureCache {
  public readonly string Label;
  public Sprite SourceSprite;
  public Texture2D Texture;

  public SpriteTextureCache(string label) {
    Label = label;
  }

  public Texture2D GetTexture(Sprite sprite) {
    if (sprite == null) {
      Clear();
      return null;
    }

    if (SourceSprite == sprite && Texture != null) {
      return Texture;
    }

    Rebuild(sprite);
    return Texture;
  }

  public void Rebuild(Sprite sprite) {
    Clear();
    SourceSprite = sprite;
    Texture = AllIn1EffectPreviewWindowUtils.BuildPreviewTextureFromSprite(sprite, Label);
    var textureName = Texture != null ? Texture.name : "null";
    Debug.Log($"[AllIn1EffectPreviewWindow] Rebuilt {Label} preview texture from sprite '{sprite.name}' -> '{textureName}'.");
  }

  public void Clear() {
    SourceSprite = null;
    if (Texture != null) {
      Object.DestroyImmediate(Texture);
      Texture = null;
    }
  }
}
#endif
