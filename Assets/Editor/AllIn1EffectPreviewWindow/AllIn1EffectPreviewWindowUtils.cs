#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class AllIn1EffectPreviewWindowUtils {
  public static Texture2D BuildPreviewTextureFromSprite(Sprite sprite, string textureLabel) {
    if (sprite == null || sprite.texture == null) return null;

    var sourceTexture = sprite.texture;
    var spriteRect = sprite.textureRect;
    var width = Mathf.Max(1, Mathf.RoundToInt(spriteRect.width));
    var height = Mathf.Max(1, Mathf.RoundToInt(spriteRect.height));

    var renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
    var previousRenderTexture = RenderTexture.active;

    try {
      RenderTexture.active = renderTexture;
      GL.PushMatrix();
      GL.LoadPixelMatrix(0f, width, height, 0f);
      GL.Clear(true, true, Color.clear);

      var sourceRect = new Rect(
        spriteRect.x / sourceTexture.width,
        spriteRect.y / sourceTexture.height,
        spriteRect.width / sourceTexture.width,
        spriteRect.height / sourceTexture.height);

      Graphics.DrawTexture(new Rect(0f, 0f, width, height), sourceTexture, sourceRect, 0, 0, 0, 0);

      var previewTexture = new Texture2D(width, height, TextureFormat.RGBA32, false) {
        name = $"Preview_{textureLabel}_{sprite.name}",
        filterMode = sourceTexture.filterMode,
        wrapMode = TextureWrapMode.Clamp,
        hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
      };

      previewTexture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
      previewTexture.Apply(false, true);
      return previewTexture;
    }
    finally {
      GL.PopMatrix();
      RenderTexture.active = previousRenderTexture;
      RenderTexture.ReleaseTemporary(renderTexture);
    }
  }

  public static void DrawCheckerboard(Rect rect, float tileSize, Color colorA, Color colorB) {
    var columns = Mathf.CeilToInt(rect.width / tileSize);
    var rows = Mathf.CeilToInt(rect.height / tileSize);

    for (var row = 0; row < rows; row++) {
      for (var column = 0; column < columns; column++) {
        var x = rect.x + (column * tileSize);
        var y = rect.y + (row * tileSize);
        var tileRect = new Rect(
          x,
          y,
          Mathf.Min(tileSize, rect.xMax - x),
          Mathf.Min(tileSize, rect.yMax - y));
        EditorGUI.DrawRect(tileRect, ((row + column) & 1) == 0 ? colorA : colorB);
      }
    }
  }

  public static Rect GetScaledPreviewRect(Rect rect, float scale, Texture texture) {
    var clampedScale = Mathf.Clamp(scale, 0.1f, 1f);
    var maxWidth = rect.width * clampedScale;
    var maxHeight = rect.height * clampedScale;
    var aspect = texture != null && texture.height > 0 ? texture.width / (float)texture.height : 1f;
    var width = maxWidth;
    var height = width / Mathf.Max(0.01f, aspect);

    if (height > maxHeight) {
      height = maxHeight;
      width = height * aspect;
    }

    var x = rect.x + ((rect.width - width) * 0.5f);
    var y = rect.y + ((rect.height - height) * 0.5f);
    return new Rect(x, y, width, height);
  }

  public static float SeamlessLayeredNoise(float u, float v, float scaleX, float scaleY, float seed) {
    var amplitude = 0.5f;
    var frequency = 1f;
    var sum = 0f;
    var weight = 0f;

    for (var i = 0; i < 3; i++) {
      float sx = scaleX * frequency;
      float sy = scaleY * frequency;
      
      float x = u * sx;
      float y = v * sy;
      
      float n00 = Mathf.PerlinNoise(x + seed, y + seed);
      float n10 = Mathf.PerlinNoise(x - sx + seed, y + seed);
      float n01 = Mathf.PerlinNoise(x + seed, y - sy + seed);
      float n11 = Mathf.PerlinNoise(x - sx + seed, y - sy + seed);
      
      float value = Mathf.Lerp(
        Mathf.Lerp(n00, n10, u),
        Mathf.Lerp(n01, n11, u),
        v
      );
      
      sum += value * amplitude;
      weight += amplitude;
      amplitude *= 0.5f;
      frequency *= 2f;
    }

    return weight > 0f ? sum / weight : 0f;
  }
}
#endif
