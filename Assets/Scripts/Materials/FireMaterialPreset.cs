using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class FireMaterialPreset : MonoBehaviour {
  const string RuntimeShaderName = "Hidden/Esperanza/FirePreview";
  const string OverlayObjectName = "FireOverlay";
  static readonly Color FireNearBlack = new(0.03f, 0.01f, 0.01f, 1f);

  [SerializeField] bool autoRefresh = true;
  [SerializeField] bool previewInEditor = true;
  [SerializeField] bool verboseLogging;
  [SerializeField] int sortingOrderOffset = 1;
  [SerializeField] Vector3 overlayLocalOffset = new(0f, 0.08f, 0f);
  [SerializeField] Vector3 overlayLocalScale = new(1.04f, 1.18f, 1f);

  [Range(0f, 1f)] [SerializeField] float overlayAlpha = 0.84f;
  [Range(0f, 1f)] [SerializeField] float clipBottom = 0.58f;
  [Range(0f, 20f)] [SerializeField] float glowMin = 8f;
  [Range(0f, 20f)] [SerializeField] float glowMax = 12f;
  [Range(0f, 0.4f)] [SerializeField] float sourceMotion = 0.12f;
  [Range(1f, 8f)] [SerializeField] float patternRepeat = 5f;
  [SerializeField] Color glowColor = new(1f, 0.98f, 0.9f, 1f);
  [SerializeField] Color hotColor = new(1f, 0.64f, 0.22f, 1f);
  [SerializeField] Color emberColor = new(0.2f, 0.04f, 0.01f, 1f);

  [SerializeField] Material templateMaterial;
  [SerializeField] SpriteRenderer overlayRenderer;
  [SerializeField] AllIn1AnimatorInspector overlayAnimator;

  static Texture2D sharedBreakupTexture;
  static Texture2D sharedFlowTexture;

  Material overlayMaterialInstance;
  Material overlayMaterialSourceAsset;
  Shader overlayMaterialSourceShader;
  SpriteRenderer sourceRenderer;
  bool loggedLegacyTemplateWarning;

#if UNITY_EDITOR
  bool refreshQueued;
#endif

  void Reset() {
    CacheSourceRenderer();
    QueueRefresh();
  }

  void OnEnable() {
    CacheSourceRenderer();
    QueueRefresh();
  }

  void OnValidate() {
    glowMax = Mathf.Max(glowMin, glowMax);
    clipBottom = Mathf.Clamp01(clipBottom);
    overlayAlpha = Mathf.Clamp01(overlayAlpha);
    sourceMotion = Mathf.Clamp(sourceMotion, 0f, 0.4f);
    patternRepeat = Mathf.Clamp(patternRepeat, 1f, 8f);
    CacheSourceRenderer();
    QueueRefresh();
  }

  void LateUpdate() {
    CacheSourceRenderer();
    if (sourceRenderer == null) return;

    if (!ShouldShowOverlay()) {
      SetOverlayEnabled(false);
      return;
    }

    if (autoRefresh) {
      ApplyOverlayState("LateUpdate");
      return;
    }

    SyncOverlayRendererFromSource();
  }

  void OnDisable() {
    if (!Application.isPlaying) {
      SetOverlayEnabled(false);
    }
  }

  void OnDestroy() {
    ReleaseOverlayMaterial();
  }

  [ContextMenu("Apply Fire Material")]
  public void ApplyToRenderer() {
    ApplyOverlayState("ContextMenu");
  }

  void QueueRefresh() {
#if UNITY_EDITOR
    if (Application.isPlaying) {
      ApplyOverlayState("PlayModeRefresh");
      return;
    }

    if (refreshQueued) return;
    refreshQueued = true;
    EditorApplication.delayCall += HandleQueuedRefresh;
#else
    ApplyOverlayState("RuntimeRefresh");
#endif
  }

#if UNITY_EDITOR
  void HandleQueuedRefresh() {
    refreshQueued = false;
    if (this == null) return;
    ApplyOverlayState("EditorRefresh");
  }
#endif

  void ApplyOverlayState(string reason) {
    CacheSourceRenderer();
    if (sourceRenderer == null) return;

    if (!ShouldShowOverlay()) {
      SetOverlayEnabled(false);
      return;
    }

    var fireOverlay = EnsureOverlayRenderer();
    if (fireOverlay == null) return;

    var fireMaterial = EnsureOverlayMaterial();
    if (fireMaterial == null) {
      SetOverlayEnabled(false);
      return;
    }

    ApplyToMaterial(fireMaterial, GetAnimationTime());
    if (fireOverlay.sharedMaterial != fireMaterial) {
      fireOverlay.sharedMaterial = fireMaterial;
    }

    SyncOverlayRendererFromSource();
    SetOverlayEnabled(sourceRenderer.enabled && sourceRenderer.sprite != null);

    if (verboseLogging) {
      Debug.Log(
        $"[{nameof(FireMaterialPreset)}] {reason} overlay='{fireOverlay.name}' " +
        $"shader='{fireMaterial.shader.name}' interior={GetDarkInteriorColor()} " +
        $"hotEdge={GetHotEdgeColor()} brightEdge={GetBrightEdgeColor()}",
        this);
    }
  }

  public void ApplyToMaterial(Material material, float timeValue) {
    if (material == null) return;

    EnsureSharedEffectTextures();
    var sourceTexture = sourceRenderer != null && sourceRenderer.sprite != null ? sourceRenderer.sprite.texture : null;

    var glowPulse = PingPong01(timeValue, 0.8f);
    var clipPulse = PingPong01(timeValue + 0.21f, 1.15f);
    var breakupPulse = PingPong01(timeValue + 0.34f, 0.92f);
    var detailPulse = PingPong01(timeValue + 0.48f, 0.7f);

    var darkInteriorColor = GetDarkInteriorColor();
    var hotEdgeColor = GetHotEdgeColor();
    var brightEdgeColor = GetBrightEdgeColor();
    var edgeBrightness = Mathf.Clamp(Mathf.Lerp(glowMin, glowMax, glowPulse) / 7.25f, 0.5f, 2.8f);

    SetTextureIfPresent(material, "_MainTex", sourceTexture);
    SetTextureIfPresent(material, "_NoiseTex", sharedBreakupTexture);
    SetTextureIfPresent(material, "_FlowTex", sharedFlowTexture);
    SetFloatIfPresent(material, "_PreviewTime", timeValue);
    SetFloatIfPresent(material, "_Opacity", overlayAlpha);
    SetFloatIfPresent(material, "_FlameHeight", Mathf.Lerp(0.92f, 0.5f, clipBottom));
    SetFloatIfPresent(material, "_BodyWidth", Mathf.Lerp(0.54f, 0.4f, clipPulse));
    SetFloatIfPresent(material, "_EdgeSoftness", 0.09f);
    SetFloatIfPresent(material, "_Breakup", Mathf.Lerp(0.55f, 0.85f, breakupPulse));
    SetFloatIfPresent(material, "_NoiseScale", 2.35f);
    SetFloatIfPresent(material, "_DetailScale", Mathf.Lerp(5.5f, 7.2f, detailPulse));
    SetFloatIfPresent(material, "_FlowSpeed", 1.12f);
    SetFloatIfPresent(material, "_TongueStrength", Mathf.Lerp(0.16f, 0.25f, glowPulse));
    SetFloatIfPresent(material, "_TongueFrequency", 8.2f);
    SetFloatIfPresent(material, "_DistortionStrength", 0.14f);
    SetFloatIfPresent(material, "_SourceMotion", sourceMotion);
    SetFloatIfPresent(material, "_PatternRepeat", patternRepeat);
    SetFloatIfPresent(material, "_CoreIntensity", edgeBrightness);
    SetColorIfPresent(material, "_BrightColor", brightEdgeColor);
    SetColorIfPresent(material, "_HotColor", hotEdgeColor);
    SetColorIfPresent(material, "_BodyColor", darkInteriorColor);
  }

  void CacheSourceRenderer() {
    if (sourceRenderer == null) {
      sourceRenderer = GetComponent<SpriteRenderer>();
    }
  }

  bool ShouldShowOverlay() {
    return Application.isPlaying || previewInEditor;
  }

  float GetAnimationTime() {
#if UNITY_EDITOR
    if (!Application.isPlaying) {
      return (float)EditorApplication.timeSinceStartup;
    }
#endif
    return Time.time;
  }

  SpriteRenderer EnsureOverlayRenderer() {
    CacheSourceRenderer();
    if (sourceRenderer == null) return null;

    if (overlayRenderer != null) return overlayRenderer;

    var overlayTransform = transform.Find(OverlayObjectName);
    if (overlayTransform == null) {
      overlayTransform = new GameObject(OverlayObjectName).transform;
      overlayTransform.SetParent(transform, false);
    }

    overlayRenderer = overlayTransform.GetComponent<SpriteRenderer>();
    if (overlayRenderer == null) {
      overlayRenderer = overlayTransform.gameObject.AddComponent<SpriteRenderer>();
    }

    overlayAnimator = overlayTransform.GetComponent<AllIn1AnimatorInspector>();

#if UNITY_EDITOR
    EditorUtility.SetDirty(this);
    EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    return overlayRenderer;
  }

  void SyncOverlayRendererFromSource() {
    if (sourceRenderer == null || overlayRenderer == null) return;

    var overlayTransform = overlayRenderer.transform;
    if (overlayTransform.parent != transform) {
      overlayTransform.SetParent(transform, false);
    }

    overlayTransform.localPosition = overlayLocalOffset;
    overlayTransform.localRotation = Quaternion.identity;
    overlayTransform.localScale = overlayLocalScale;

    overlayRenderer.sprite = sourceRenderer.sprite;
    overlayRenderer.flipX = sourceRenderer.flipX;
    overlayRenderer.flipY = sourceRenderer.flipY;
    overlayRenderer.color = new Color(1f, 1f, 1f, sourceRenderer.color.a);
    overlayRenderer.drawMode = sourceRenderer.drawMode;
    overlayRenderer.size = sourceRenderer.size;
    overlayRenderer.tileMode = sourceRenderer.tileMode;
    overlayRenderer.maskInteraction = sourceRenderer.maskInteraction;
    overlayRenderer.spriteSortPoint = sourceRenderer.spriteSortPoint;
    overlayRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
    overlayRenderer.sortingOrder = sourceRenderer.sortingOrder + sortingOrderOffset;
  }

  void SetOverlayEnabled(bool enabledState) {
    if (overlayRenderer == null) return;
    overlayRenderer.enabled = enabledState && ShouldShowOverlay();
  }

  Material EnsureOverlayMaterial() {
    if (TryGetTemplateMaterial(out var sourceMaterial)) {
      if (overlayMaterialInstance != null &&
          overlayMaterialSourceAsset == sourceMaterial &&
          overlayMaterialSourceShader == null) {
        return overlayMaterialInstance;
      }

      ReleaseOverlayMaterial();
      overlayMaterialInstance = new Material(sourceMaterial) {
        name = $"{sourceMaterial.name}_OverlayInstance",
        hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
      };
      overlayMaterialSourceAsset = sourceMaterial;
      overlayMaterialSourceShader = null;
      return overlayMaterialInstance;
    }

    var shader = Shader.Find(RuntimeShaderName);
    if (shader == null) {
      Debug.LogError($"[{nameof(FireMaterialPreset)}] Shader '{RuntimeShaderName}' was not found.", this);
      return null;
    }

    if (overlayMaterialInstance != null &&
        overlayMaterialSourceAsset == null &&
        overlayMaterialSourceShader == shader) {
      return overlayMaterialInstance;
    }

    ReleaseOverlayMaterial();
    overlayMaterialInstance = new Material(shader) {
      name = $"{name}_FireOverlayMaterial",
      hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
    };
    overlayMaterialSourceAsset = null;
    overlayMaterialSourceShader = shader;
    return overlayMaterialInstance;
  }

  bool TryGetTemplateMaterial(out Material sourceMaterial) {
    sourceMaterial = null;
    if (templateMaterial == null) return false;
    if (HasEdgeLitFireProperties(templateMaterial)) {
      sourceMaterial = templateMaterial;
      return true;
    }

    if (IsLegacyFireTemplateMaterial(templateMaterial)) {
      LogLegacyTemplateFallback(templateMaterial, LogType.Log);
      return false;
    }

    if (!loggedLegacyTemplateWarning) {
      loggedLegacyTemplateWarning = true;
      Debug.LogWarning(
        $"[{nameof(FireMaterialPreset)}] Template material '{templateMaterial.name}' does not expose " +
        "edge-lit fire properties. Using the dedicated fire shader fallback instead.",
        this);
    }
    return false;
  }

  bool HasEdgeLitFireProperties(Material material) {
    if (material == null) return false;
    return material.HasProperty("_BrightColor") &&
           material.HasProperty("_HotColor") &&
           material.HasProperty("_BodyColor");
  }

  bool IsLegacyFireTemplateMaterial(Material material) {
    if (material == null || material.shader == null) return false;

    var shaderName = material.shader.name;
    return shaderName.Contains("AllIn1SpriteShader") ||
           shaderName.Contains("AllIn1Urp2dRenderer") ||
           material.name == "Fire";
  }

  void LogLegacyTemplateFallback(Material material, LogType logType) {
    if (loggedLegacyTemplateWarning) return;
    if (!verboseLogging && logType != LogType.Warning) return;

    loggedLegacyTemplateWarning = true;
    var message =
      $"[{nameof(FireMaterialPreset)}] Legacy template material '{material.name}' uses shader " +
      $"'{material.shader.name}'. Falling back to '{RuntimeShaderName}'.";

    if (logType == LogType.Warning) {
      Debug.LogWarning(message, this);
      return;
    }

    Debug.Log(message, this);
  }

  void ReleaseOverlayMaterial() {
    overlayMaterialSourceAsset = null;
    overlayMaterialSourceShader = null;

    if (overlayMaterialInstance == null) return;

    if (overlayRenderer != null && overlayRenderer.sharedMaterial == overlayMaterialInstance) {
      overlayRenderer.sharedMaterial = null;
    }

    if (Application.isPlaying) {
      Destroy(overlayMaterialInstance);
    }
    else {
      DestroyImmediate(overlayMaterialInstance);
    }
    overlayMaterialInstance = null;
  }

  void SetFloatIfPresent(Material material, string property, float value) {
    if (material == null || !material.HasProperty(property)) return;
    material.SetFloat(property, value);
  }

  void SetColorIfPresent(Material material, string property, Color value) {
    if (material == null || !material.HasProperty(property)) return;
    material.SetColor(property, value);
  }

  void SetTextureIfPresent(Material material, string property, Texture texture) {
    if (material == null || texture == null || !material.HasProperty(property)) return;
    material.SetTexture(property, texture);
  }

  Color GetDarkInteriorColor() {
    return Color.Lerp(emberColor, FireNearBlack, 0.72f);
  }

  Color GetHotEdgeColor() {
    return Color.Lerp(emberColor, hotColor, 0.82f);
  }

  Color GetBrightEdgeColor() {
    var edgeBase = Color.Lerp(hotColor, glowColor, 0.45f);
    return Color.Lerp(edgeBase, Color.white, 0.35f);
  }

  float PingPong01(float timeValue, float cycleSeconds) {
    if (cycleSeconds <= 0.0001f) return 0f;
    return Mathf.SmoothStep(0f, 1f, Mathf.PingPong(timeValue / cycleSeconds, 1f));
  }

  void EnsureSharedEffectTextures() {
    if (sharedBreakupTexture == null) {
      sharedBreakupTexture = BuildProceduralBreakupTexture();
    }

    if (sharedFlowTexture == null) {
      sharedFlowTexture = BuildProceduralFlowTexture();
    }
  }

  Texture2D BuildProceduralBreakupTexture() {
    const int size = 96;
    var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
      name = "FireOverlay_ProceduralBreakup",
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Repeat,
      hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
    };

    for (var y = 0; y < size; y++) {
      for (var x = 0; x < size; x++) {
        var u = x / (float)(size - 1);
        var v = y / (float)(size - 1);
        var noiseA = LayeredNoise(u * 2.4f, v * 3.2f, 13.7f);
        var noiseB = LayeredNoise(u * 6.8f, v * 5.1f, 27.3f);
        var value = Mathf.Clamp01((noiseA * 0.68f) + (noiseB * 0.32f));
        texture.SetPixel(x, y, new Color(value, value, value, 1f));
      }
    }

    texture.Apply(false, true);
    return texture;
  }

  Texture2D BuildProceduralFlowTexture() {
    const int size = 96;
    var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
      name = "FireOverlay_ProceduralFlow",
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Repeat,
      hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
    };

    for (var y = 0; y < size; y++) {
      for (var x = 0; x < size; x++) {
        var u = x / (float)(size - 1);
        var v = y / (float)(size - 1);
        var flowX = LayeredNoise(u * 3.1f, v * 4.7f, 5.3f);
        var flowY = LayeredNoise(u * 5.4f, v * 2.5f, 19.4f);
        texture.SetPixel(x, y, new Color(flowX, flowY, 0.5f, 1f));
      }
    }

    texture.Apply(false, true);
    return texture;
  }

  float LayeredNoise(float x, float y, float seed) {
    var amplitude = 0.5f;
    var frequency = 1f;
    var sum = 0f;
    var weight = 0f;

    for (var i = 0; i < 3; i++) {
      var value = Mathf.PerlinNoise((x * frequency) + seed, (y * frequency) + (seed * 0.37f));
      sum += value * amplitude;
      weight += amplitude;
      amplitude *= 0.5f;
      frequency *= 2f;
    }

    return weight > 0f ? sum / weight : 0f;
  }
}
