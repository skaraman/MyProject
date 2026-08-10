using System.Collections.Generic;
using UnityEngine;

public class ShinyCrossVFX : MonoBehaviour {
  private const int StarTextureSize = 64;
  private const int EdgeSamplesPerAxis = 2;

  [Header("Visual Settings")]
  public Color baseColor = new Color(1f, 0.95f, 0.6f, 1f);
  [Range(0f, 1f)] public float minOpacity = 0.5f;
  [Range(0f, 1f)] public float maxOpacity = 1.0f;

  [Header("Size Settings")]
  public float baseWidth = 0.8f;
  public float baseThickness = 0.15f;
  public float sizePulseAmount = 0.4f;

  [Header("Animation Settings")]
  public float pulseSpeed = 5f;
  public float rotationSpeed = 30f;

  [Header("Material")]
  public Material crossMaterial;

  static readonly Dictionary<int, Sprite> StarSprites = new();

  SpriteRenderer star;
  float timeOffset;

  void Start() {
    timeOffset = Random.Range(0f, Mathf.PI * 2f);

    var starObject = new GameObject("StarShine");
    starObject.transform.SetParent(transform, false);
    starObject.transform.localPosition = Vector3.zero;

    star = starObject.AddComponent<SpriteRenderer>();
    star.sprite = GetStarSprite();
    if (crossMaterial != null) star.sharedMaterial = crossMaterial;

    star.sortingLayerName = "MyUI";
    star.sortingOrder = 100;
  }

  Sprite GetStarSprite() {
    float safeWidth = Mathf.Max(0.001f, Mathf.Abs(baseWidth));
    float innerRadius = Mathf.Clamp(Mathf.Abs(baseThickness) / safeWidth, 0.06f, 0.35f);
    int cacheKey = Mathf.RoundToInt(innerRadius * 1000f);

    if (StarSprites.TryGetValue(cacheKey, out var cached) && cached != null) return cached;

    var texture = new Texture2D(StarTextureSize, StarTextureSize, TextureFormat.RGBA32, false) {
      name = $"LootStar_{cacheKey}",
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Clamp,
      hideFlags = HideFlags.HideAndDontSave
    };

    var pixels = new Color32[StarTextureSize * StarTextureSize];
    for (int y = 0; y < StarTextureSize; y++) {
      for (int x = 0; x < StarTextureSize; x++) {
        float coverage = CalculateCoverage(x, y, innerRadius);
        float normalizedX = ((x + 0.5f) / StarTextureSize) * 2f - 1f;
        float normalizedY = ((y + 0.5f) / StarTextureSize) * 2f - 1f;
        float distanceFromCenter = Mathf.Clamp01(Mathf.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY));
        byte alpha = (byte)Mathf.RoundToInt(coverage * Mathf.Lerp(255f, 150f, distanceFromCenter));
        pixels[y * StarTextureSize + x] = new Color32(255, 255, 255, alpha);
      }
    }

    texture.SetPixels32(pixels);
    texture.Apply(false, true);

    var sprite = Sprite.Create(
      texture,
      new Rect(0f, 0f, StarTextureSize, StarTextureSize),
      new Vector2(0.5f, 0.5f),
      StarTextureSize,
      0u,
      SpriteMeshType.FullRect
    );
    sprite.name = texture.name;
    sprite.hideFlags = HideFlags.HideAndDontSave;
    StarSprites[cacheKey] = sprite;
    return sprite;
  }

  static float CalculateCoverage(int pixelX, int pixelY, float innerRadius) {
    int insideSamples = 0;
    int sampleCount = EdgeSamplesPerAxis * EdgeSamplesPerAxis;

    for (int sampleY = 0; sampleY < EdgeSamplesPerAxis; sampleY++) {
      for (int sampleX = 0; sampleX < EdgeSamplesPerAxis; sampleX++) {
        float x = ((pixelX + (sampleX + 0.5f) / EdgeSamplesPerAxis) / StarTextureSize) * 2f - 1f;
        float y = ((pixelY + (sampleY + 0.5f) / EdgeSamplesPerAxis) / StarTextureSize) * 2f - 1f;
        if (IsInsideFourPointStar(Mathf.Abs(x), Mathf.Abs(y), innerRadius)) insideSamples++;
      }
    }

    return insideSamples / (float)sampleCount;
  }

  static bool IsInsideFourPointStar(float x, float y, float innerRadius) {
    if (x > 1f || y > 1f) return false;

    if (x <= innerRadius) {
      float topEdge = 1f - x * ((1f - innerRadius) / innerRadius);
      return y <= Mathf.Max(innerRadius, topEdge);
    }

    float rightEdge = innerRadius * (1f - x) / (1f - innerRadius);
    return y <= rightEdge;
  }

  void Update() {
    if (star == null) return;

    float time = Time.time * pulseSpeed + timeOffset;
    float slowWave = Mathf.Sin(time) * 0.5f + 0.5f;
    float flare = slowWave * slowWave * slowWave;
    float fineFlicker = Mathf.Sin(time * 2.71f + timeOffset * 0.37f) * 0.5f + 0.5f;
    float shine = Mathf.Clamp01(flare * 0.82f + fineFlicker * 0.18f);

    float size = Mathf.Max(0.01f, baseWidth + sizePulseAmount * shine);
    star.transform.localScale = new Vector3(size, size, 1f);

    Color glowColor = baseColor;
    glowColor.a = Mathf.Lerp(minOpacity, maxOpacity, shine);
    star.color = glowColor;

    star.transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);
  }
}
