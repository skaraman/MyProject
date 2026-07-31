using System;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class FormSpriteEffect : MonoBehaviour {
  public enum EffectSelection {
    FollowCharacterForm,
    None,
    Fire,
    Aqua,
    Bolt,
    Cold,
    Dark
  }

  const string OverlayObjectName = "FormEffectOverlay";

  static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
  static readonly int NoiseTextureId = Shader.PropertyToID("_NoiseTex");
  static readonly int FlowTextureId = Shader.PropertyToID("_FlowTex");
  static readonly int PreviewTimeId = Shader.PropertyToID("_PreviewTime");
  static readonly int SourceRectId = Shader.PropertyToID("_SourceRectInEffect");
  static readonly int SpriteUvRectId = Shader.PropertyToID("_SpriteUvRect");
  static readonly int HasNormalMapId = Shader.PropertyToID("_HasNormalMap");

  [Header("Effect")]
  [SerializeField] EffectSelection effect = EffectSelection.FollowCharacterForm;
  [SerializeField, Tooltip("Fixed effect shown in edit mode when Follow Character Form is selected.")]
  EffectSelection editorPreviewEffect = EffectSelection.Fire;
  [SerializeField] bool previewInEditor;
  [SerializeField, Min(0f)] float animationSpeed = 1f;
  [SerializeField] int sortingOrderOffset = 1;

  [Header("Optional Material Overrides")]
  [SerializeField] Material fireMaterial;
  [SerializeField] Material aquaMaterial;
  [SerializeField] Material boltMaterial;
  [SerializeField] Material coldMaterial;
  [SerializeField] Material darkMaterial;

  [SerializeField, HideInInspector] Shader fireShader;
  [SerializeField, HideInInspector] Shader aquaShader;
  [SerializeField, HideInInspector] Shader boltShader;
  [SerializeField, HideInInspector] Shader coldShader;
  [SerializeField, HideInInspector] Shader darkShader;

  SpriteRenderer sourceRenderer;
  MeshRenderer overlayRenderer;
  MeshFilter overlayFilter;
  Mesh overlayMesh;
  Material overlayMaterial;
  Texture2D proceduralNoise;
  Texture2D proceduralFlow;
  Sprite lastSprite;
  Vector2 lastRendererSize;
  Color lastSourceColor;
  bool lastFlipX;
  bool lastFlipY;
  EffectSelection activeEffect = (EffectSelection)(-1);
  Material activeTemplate;
  Shader activeShader;
  Action offFormChanged;
  bool refreshRequested = true;
  bool loggedUnsupportedPacking;

#if UNITY_EDITOR
  bool refreshQueued;
#endif

  void Reset() {
    CacheSourceRenderer();
    ResolveShaderReferences();
    QueueRefresh();
  }

  void OnEnable() {
    CacheSourceRenderer();
    ResolveShaderReferences();
    RegisterFormHandler();
    QueueRefresh();
  }

  void OnValidate() {
    animationSpeed = Mathf.Max(0f, animationSpeed);
    editorPreviewEffect = editorPreviewEffect == EffectSelection.FollowCharacterForm
      ? EffectSelection.Fire
      : editorPreviewEffect;
    ResolveShaderReferences();
    if (Application.isPlaying) {
      RegisterFormHandler();
    }
    refreshRequested = true;
    QueueRefresh(deferInEditor: true);
  }

  void LateUpdate() {
    RefreshOverlay(refreshRequested);
  }

  void OnDisable() {
    UnregisterFormHandler();
    SetOverlayEnabled(false);
  }

  void OnDestroy() {
    UnregisterFormHandler();
    ReleaseGeneratedResources();
  }

  [ContextMenu("Refresh Form Effect")]
  public void RefreshEffect() {
    refreshRequested = true;
    RefreshOverlay(true);
  }

  public void SetEffect(EffectSelection nextEffect) {
    if (effect == nextEffect) return;
    effect = nextEffect;
    RegisterFormHandler();
    RefreshEffect();
  }

  void RegisterFormHandler() {
    UnregisterFormHandler();
    if (!Application.isPlaying || effect != EffectSelection.FollowCharacterForm) return;
    offFormChanged = MessageBus.On(
      CharacterMessageTopics.FormChanged,
      _ => RefreshEffect()
    );
  }

  void UnregisterFormHandler() {
    offFormChanged?.Invoke();
    offFormChanged = null;
  }

  void QueueRefresh(bool deferInEditor = false) {
#if UNITY_EDITOR
    if (deferInEditor || !Application.isPlaying) {
      if (refreshQueued) return;
      refreshQueued = true;
      EditorApplication.delayCall += HandleQueuedRefresh;
      return;
    }
#endif
    RefreshOverlay(true);
  }

#if UNITY_EDITOR
  void HandleQueuedRefresh() {
    refreshQueued = false;
    if (this == null) return;
    if (!isActiveAndEnabled) {
      SetOverlayEnabled(false);
      return;
    }
    RefreshOverlay(true);
  }
#endif

  void RefreshOverlay(bool force) {
    refreshRequested = false;
    CacheSourceRenderer();
    if (sourceRenderer == null) return;

    var resolvedEffect = ResolveEffect();
    if (!ShouldShowOverlay(resolvedEffect)) {
      activeEffect = resolvedEffect;
      SetOverlayEnabled(false);
      return;
    }

    var sourceSprite = sourceRenderer.sprite;
    if (sourceSprite == null || sourceSprite.texture == null) {
      SetOverlayEnabled(false);
      return;
    }

    var template = GetMaterialTemplate(resolvedEffect);
    var shader = template != null ? template.shader : GetShader(resolvedEffect);
    if (shader == null) {
      Debug.LogError(
        $"[{nameof(FormSpriteEffect)}] Missing shader for effect '{resolvedEffect}'.",
        this
      );
      SetOverlayEnabled(false);
      return;
    }

    var effectChanged = resolvedEffect != activeEffect ||
                        template != activeTemplate ||
                        shader != activeShader;
    if (effectChanged) {
      activeEffect = resolvedEffect;
      activeTemplate = template;
      activeShader = shader;
      RebuildOverlayMaterial(resolvedEffect, template, shader);
      force = true;
    }

    if (overlayMaterial == null || !EnsureOverlayObjects()) {
      SetOverlayEnabled(false);
      return;
    }

    var sourceChanged = sourceSprite != lastSprite ||
                        sourceRenderer.flipX != lastFlipX ||
                        sourceRenderer.flipY != lastFlipY ||
                        sourceRenderer.size != lastRendererSize ||
                        sourceRenderer.color != lastSourceColor;
    if (force || sourceChanged) {
      if (!SyncOverlayGeometry(sourceSprite, resolvedEffect)) {
        SetOverlayEnabled(false);
        return;
      }
      ApplySourceMaterialState(sourceSprite, resolvedEffect);
      lastSprite = sourceSprite;
      lastFlipX = sourceRenderer.flipX;
      lastFlipY = sourceRenderer.flipY;
      lastRendererSize = sourceRenderer.size;
      lastSourceColor = sourceRenderer.color;
    }

    SyncRendererState();
    if (overlayMaterial.HasProperty(PreviewTimeId)) {
      overlayMaterial.SetFloat(PreviewTimeId, GetAnimationTime() * animationSpeed);
    }
    SetOverlayEnabled(sourceRenderer.enabled);
  }

  void CacheSourceRenderer() {
    sourceRenderer ??= GetComponent<SpriteRenderer>();
  }

  EffectSelection ResolveEffect() {
    if (effect != EffectSelection.FollowCharacterForm) {
      return effect;
    }

    if (!Application.isPlaying) {
      return editorPreviewEffect;
    }

    return FormToEffect(EsperanzaForms.GetActive());
  }

  static EffectSelection FormToEffect(string formName) {
    if (string.Equals(formName, "Fire", StringComparison.OrdinalIgnoreCase)) {
      return EffectSelection.Fire;
    }
    if (string.Equals(formName, "Aqua", StringComparison.OrdinalIgnoreCase)) {
      return EffectSelection.Aqua;
    }
    if (string.Equals(formName, "Bolt", StringComparison.OrdinalIgnoreCase)) {
      return EffectSelection.Bolt;
    }
    if (string.Equals(formName, "Cold", StringComparison.OrdinalIgnoreCase)) {
      return EffectSelection.Cold;
    }
    if (string.Equals(formName, "Dark", StringComparison.OrdinalIgnoreCase)) {
      return EffectSelection.Dark;
    }
    return EffectSelection.None;
  }

  bool ShouldShowOverlay(EffectSelection resolvedEffect) {
    if (resolvedEffect is EffectSelection.None or EffectSelection.FollowCharacterForm) {
      return false;
    }
    return Application.isPlaying || previewInEditor;
  }

  bool EnsureOverlayObjects() {
    if (overlayRenderer != null && overlayFilter != null && overlayMesh != null) {
      return true;
    }

    var overlayObject = new GameObject(OverlayObjectName) {
      hideFlags = HideFlags.HideAndDontSave,
      layer = gameObject.layer
    };
    overlayObject.transform.SetParent(transform, false);
    overlayFilter = overlayObject.AddComponent<MeshFilter>();
    overlayRenderer = overlayObject.AddComponent<MeshRenderer>();
    overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
    overlayRenderer.receiveShadows = false;
    overlayRenderer.lightProbeUsage = LightProbeUsage.Off;
    overlayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    overlayRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    overlayRenderer.allowOcclusionWhenDynamic = false;

    overlayMesh = new Mesh {
      name = $"{name}_FormEffectQuad",
      hideFlags = HideFlags.HideAndDontSave
    };
    overlayMesh.MarkDynamic();
    overlayMesh.vertices = new[] {
      new Vector3(-0.5f, -0.5f, 0f),
      new Vector3(0.5f, -0.5f, 0f),
      new Vector3(-0.5f, 0.5f, 0f),
      new Vector3(0.5f, 0.5f, 0f)
    };
    overlayMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
    overlayMesh.colors32 = new[] {
      new Color32(255, 255, 255, 255),
      new Color32(255, 255, 255, 255),
      new Color32(255, 255, 255, 255),
      new Color32(255, 255, 255, 255)
    };
    overlayMesh.RecalculateBounds();
    overlayFilter.sharedMesh = overlayMesh;
    overlayRenderer.sharedMaterial = overlayMaterial;
    return true;
  }

  void RebuildOverlayMaterial(
    EffectSelection resolvedEffect,
    Material template,
    Shader shader
  ) {
    ReleaseOverlayMaterial();
    overlayMaterial = template != null
      ? new Material(template)
      : new Material(shader);
    overlayMaterial.name = $"{name}_{resolvedEffect}EffectMaterial";
    overlayMaterial.hideFlags = HideFlags.HideAndDontSave;
    if (template == null) {
      ApplyCurrentPreviewDefaults(overlayMaterial, resolvedEffect);
    }
    if (overlayRenderer != null) {
      overlayRenderer.sharedMaterial = overlayMaterial;
    }
  }

  bool SyncOverlayGeometry(Sprite sprite, EffectSelection resolvedEffect) {
    if (overlayMesh == null || overlayRenderer == null || sprite == null) {
      return false;
    }

    if (sprite.packed && sprite.packingRotation != SpritePackingRotation.None) {
      if (!loggedUnsupportedPacking) {
        loggedUnsupportedPacking = true;
        Debug.LogWarning(
          $"[{nameof(FormSpriteEffect)}] Rotated packed sprite '{sprite.name}' is not supported.",
          this
        );
      }
      return false;
    }

    var texture = sprite.texture;
    var textureRect = sprite.textureRect;
    var uMin = textureRect.xMin / texture.width;
    var uMax = textureRect.xMax / texture.width;
    var vMin = textureRect.yMin / texture.height;
    var vMax = textureRect.yMax / texture.height;
    var leftU = sourceRenderer.flipX ? uMax : uMin;
    var rightU = sourceRenderer.flipX ? uMin : uMax;
    var bottomV = sourceRenderer.flipY ? vMax : vMin;
    var topV = sourceRenderer.flipY ? vMin : vMax;
    overlayMesh.uv = new[] {
      new Vector2(leftU, bottomV),
      new Vector2(rightU, bottomV),
      new Vector2(leftU, topV),
      new Vector2(rightU, topV)
    };

    var alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(sourceRenderer.color.a) * 255f);
    var vertexColor = new Color32(255, 255, 255, alpha);
    overlayMesh.colors32 = new[] {
      vertexColor,
      vertexColor,
      vertexColor,
      vertexColor
    };

    ResolveSourceBounds(sprite, out var sourceCenter, out var sourceSize);
    var padding = GetPadding(resolvedEffect);
    var totalWidth = 1f + padding.x + padding.y;
    var totalHeight = 1f + padding.z + padding.w;
    var canvasOffset = new Vector2(
      (padding.y - padding.x) * sourceSize.x * 0.5f,
      (padding.z - padding.w) * sourceSize.y * 0.5f
    );

    var overlayTransform = overlayRenderer.transform;
    overlayTransform.localPosition = new Vector3(
      sourceCenter.x + canvasOffset.x,
      sourceCenter.y + canvasOffset.y,
      0f
    );
    overlayTransform.localRotation = Quaternion.identity;
    overlayTransform.localScale = new Vector3(
      sourceSize.x * totalWidth,
      sourceSize.y * totalHeight,
      1f
    );
    return true;
  }

  void ResolveSourceBounds(
    Sprite sprite,
    out Vector2 sourceCenter,
    out Vector2 sourceSize
  ) {
    if (sourceRenderer.drawMode == SpriteDrawMode.Simple) {
      sourceCenter = sprite.bounds.center;
      sourceSize = sprite.bounds.size;
    }
    else {
      sourceSize = sourceRenderer.size;
      var spriteRect = sprite.rect;
      var pivot = sprite.pivot;
      var pivot01 = new Vector2(
        spriteRect.width > 0f ? pivot.x / spriteRect.width : 0.5f,
        spriteRect.height > 0f ? pivot.y / spriteRect.height : 0.5f
      );
      sourceCenter = new Vector2(
        (0.5f - pivot01.x) * sourceSize.x,
        (0.5f - pivot01.y) * sourceSize.y
      );
    }

    if (sourceRenderer.flipX) {
      sourceCenter.x = -sourceCenter.x;
    }
    if (sourceRenderer.flipY) {
      sourceCenter.y = -sourceCenter.y;
    }
    sourceSize.x = Mathf.Max(sourceSize.x, 0.0001f);
    sourceSize.y = Mathf.Max(sourceSize.y, 0.0001f);
  }

  void ApplySourceMaterialState(Sprite sprite, EffectSelection resolvedEffect) {
    if (overlayMaterial == null || sprite == null || sprite.texture == null) return;

    var texture = sprite.texture;
    var textureRect = sprite.textureRect;
    var spriteUvRect = new Vector4(
      textureRect.x / texture.width,
      textureRect.y / texture.height,
      textureRect.width / texture.width,
      textureRect.height / texture.height
    );
    var padding = GetPadding(resolvedEffect);
    var totalWidth = 1f + padding.x + padding.y;
    var totalHeight = 1f + padding.z + padding.w;
    var sourceRect = new Vector4(
      padding.x / totalWidth,
      padding.w / totalHeight,
      1f / totalWidth,
      1f / totalHeight
    );

    SetTextureIfPresent(overlayMaterial, MainTextureId, texture);
    SetVectorIfPresent(overlayMaterial, SpriteUvRectId, spriteUvRect);
    SetVectorIfPresent(overlayMaterial, SourceRectId, sourceRect);
    SetFloatIfPresent(overlayMaterial, HasNormalMapId, 0f);
    if (resolvedEffect is EffectSelection.Fire or EffectSelection.Aqua) {
      EnsureProceduralTextures();
      SetTextureIfPresent(overlayMaterial, NoiseTextureId, proceduralNoise);
      SetTextureIfPresent(overlayMaterial, FlowTextureId, proceduralFlow);
    }
  }

  void SyncRendererState() {
    if (sourceRenderer == null || overlayRenderer == null) return;
    overlayRenderer.gameObject.layer = gameObject.layer;
    overlayRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
    overlayRenderer.sortingOrder = sourceRenderer.sortingOrder + sortingOrderOffset;
    if (overlayRenderer.sharedMaterial != overlayMaterial) {
      overlayRenderer.sharedMaterial = overlayMaterial;
    }
  }

  void SetOverlayEnabled(bool enabledState) {
    if (overlayRenderer == null) return;
    overlayRenderer.enabled = enabledState &&
                              sourceRenderer != null &&
                              sourceRenderer.sprite != null &&
                              ShouldShowOverlay(activeEffect);
  }

  void ResolveShaderReferences() {
    fireShader ??= Shader.Find("Hidden/Esperanza/FirePreview");
    aquaShader ??= Shader.Find("Hidden/Esperanza/AquaPreview");
    boltShader ??= Shader.Find("Hidden/Esperanza/BoltPreview");
    coldShader ??= Shader.Find("Hidden/Esperanza/ColdPreview");
    darkShader ??= Shader.Find("Hidden/Esperanza/DarkPreview");
  }

  Shader GetShader(EffectSelection resolvedEffect) {
    return resolvedEffect switch {
      EffectSelection.Fire => fireShader,
      EffectSelection.Aqua => aquaShader,
      EffectSelection.Bolt => boltShader,
      EffectSelection.Cold => coldShader,
      EffectSelection.Dark => darkShader,
      _ => null
    };
  }

  Material GetMaterialTemplate(EffectSelection resolvedEffect) {
    return resolvedEffect switch {
      EffectSelection.Fire => fireMaterial,
      EffectSelection.Aqua => aquaMaterial,
      EffectSelection.Bolt => boltMaterial,
      EffectSelection.Cold => coldMaterial,
      EffectSelection.Dark => darkMaterial,
      _ => null
    };
  }

  static Vector4 GetPadding(EffectSelection resolvedEffect) {
    return resolvedEffect switch {
      EffectSelection.Fire => new Vector4(0.11f, 0.11f, 0.34f, 0.04f),
      EffectSelection.Aqua => new Vector4(0.09f, 0.09f, 0.05f, 0.34f),
      EffectSelection.Bolt => new Vector4(0.5f, 0.5f, 0.5f, 0.5f),
      EffectSelection.Cold => new Vector4(0.34f, 0.34f, 0.3f, 0.48f),
      EffectSelection.Dark => new Vector4(0.48f, 0.48f, 0.46f, 0.46f),
      _ => Vector4.zero
    };
  }

  static void ApplyCurrentPreviewDefaults(
    Material material,
    EffectSelection resolvedEffect
  ) {
    switch (resolvedEffect) {
      case EffectSelection.Fire:
        ApplyFireDefaults(material);
        break;
      case EffectSelection.Aqua:
        ApplyAquaDefaults(material);
        break;
      case EffectSelection.Bolt:
        ApplyBoltDefaults(material);
        break;
      case EffectSelection.Cold:
        ApplyColdDefaults(material);
        break;
      case EffectSelection.Dark:
        ApplyDarkDefaults(material);
        break;
    }
  }

  static void ApplyFireDefaults(Material material) {
    const float coverage = 0.47f;
    var size = Mathf.SmoothStep(0f, 1f, 0.352f);
    var movement = Mathf.SmoothStep(0f, 1f, 0.766f);
    var wildness = Mathf.SmoothStep(0f, 1f, 0f);
    var brightness = Mathf.SmoothStep(0f, 1f, 0f);

    SetFloatIfPresent(material, "_FlameCoverage", coverage);
    SetFloatIfPresent(material, "_FlameHeight", Mathf.Lerp(0.055f, 0.31f, size));
    SetFloatIfPresent(material, "_TongueWidth", Mathf.Lerp(0.025f, 0.095f, size));
    SetFloatIfPresent(material, "_TongueCount", Mathf.Lerp(12f, 5.5f, size));
    SetFloatIfPresent(material, "_FlowSpeed", Mathf.Lerp(0.22f, 2.35f, movement));
    SetFloatIfPresent(
      material,
      "_Sway",
      Mathf.Lerp(0.004f, 0.075f, wildness) * Mathf.Lerp(0.65f, 1.25f, movement)
    );
    SetFloatIfPresent(material, "_Breakup", Mathf.Lerp(0.08f, 0.78f, wildness));
    SetFloatIfPresent(material, "_NoiseScale", Mathf.Lerp(2.2f, 6.4f, wildness));
    SetFloatIfPresent(material, "_SurfaceOpacity", Mathf.Lerp(0.62f, 0.16f, 1f));
    SetFloatIfPresent(material, "_FlameOpacity", Mathf.Lerp(0.66f, 1f, brightness));
    SetFloatIfPresent(material, "_Brightness", Mathf.Lerp(0.85f, 2.35f, brightness));
    SetColorIfPresent(material, "_HotColor", new Color(1f, 0f, 0.007926464f, 1f));
    SetColorIfPresent(material, "_FlameColor", new Color(1f, 0.7532815f, 0f, 1f));
  }

  static void ApplyAquaDefaults(Material material) {
    var wetness = Mathf.SmoothStep(0f, 1f, 0.686f);
    var size = Mathf.SmoothStep(0f, 1f, 0f);
    var flow = Mathf.SmoothStep(0f, 1f, 1f);
    var beading = Mathf.SmoothStep(0f, 1f, 1f);
    var shine = Mathf.SmoothStep(0f, 1f, 1f);

    SetFloatIfPresent(material, "_Wetness", Mathf.Lerp(0.28f, 0.98f, wetness));
    SetFloatIfPresent(material, "_DripLength", Mathf.Lerp(0.055f, 0.33f, size));
    SetFloatIfPresent(
      material,
      "_DripWidth",
      Mathf.Lerp(0.018f, 0.072f, size) * Mathf.Lerp(1.2f, 0.82f, beading)
    );
    SetFloatIfPresent(material, "_DripCount", Mathf.Lerp(13f, 6f, size));
    SetFloatIfPresent(material, "_FlowSpeed", Mathf.Lerp(0.12f, 1.85f, flow));
    SetFloatIfPresent(
      material,
      "_Wobble",
      Mathf.Lerp(0.002f, 0.042f, beading) * Mathf.Lerp(0.55f, 1.2f, flow)
    );
    SetFloatIfPresent(material, "_Beading", Mathf.Lerp(0.12f, 0.92f, beading));
    SetFloatIfPresent(material, "_NoiseScale", Mathf.Lerp(2.4f, 6.8f, beading));
    SetFloatIfPresent(
      material,
      "_SurfaceOpacity",
      Mathf.Lerp(0.42f, 0.075f, 0.711f) * Mathf.Lerp(0.68f, 1.08f, wetness)
    );
    SetFloatIfPresent(
      material,
      "_DripOpacity",
      Mathf.Lerp(0.48f, 0.92f, Mathf.Max(wetness, shine * 0.72f))
    );
    SetFloatIfPresent(material, "_Specular", Mathf.Lerp(0.18f, 1.15f, shine));
    SetFloatIfPresent(material, "_Brightness", Mathf.Lerp(0.72f, 1.62f, shine));
    SetColorIfPresent(material, "_WaterColor", new Color(0.04f, 0.42f, 0.92f, 1f));
    SetColorIfPresent(material, "_HighlightColor", new Color(0.54f, 0.95f, 1f, 1f));
  }

  static void ApplyBoltDefaults(Material material) {
    var charge = Mathf.SmoothStep(0f, 1f, 1f);
    var reach = Mathf.SmoothStep(0f, 1f, 0f);
    var thickness = Mathf.SmoothStep(0f, 1f, 0f);
    var activity = Mathf.SmoothStep(0f, 1f, 0.351f);
    var randomness = Mathf.SmoothStep(0f, 1f, 0.536f);
    var branching = Mathf.SmoothStep(0f, 1f, 1f);
    var brightness = Mathf.SmoothStep(0f, 1f, 0f);

    SetFloatIfPresent(material, "_Charge", Mathf.Lerp(0.28f, 1f, charge));
    SetFloatIfPresent(material, "_BoltCount", Mathf.Lerp(3f, 8f, charge));
    SetFloatIfPresent(material, "_Reach", reach);
    SetFloatIfPresent(material, "_BoltWidth", Mathf.Lerp(0.0025f, 0.014f, thickness));
    SetFloatIfPresent(material, "_Activity", Mathf.Lerp(0.24f, 2.4f, activity));
    SetFloatIfPresent(material, "_Randomness", randomness);
    SetFloatIfPresent(material, "_Branching", branching);
    SetFloatIfPresent(material, "_SurfaceOpacity", Mathf.Lerp(0.4f, 0.055f, 1f));
    SetFloatIfPresent(material, "_BoltOpacity", Mathf.Lerp(0.7f, 1f, brightness));
    SetFloatIfPresent(material, "_Glow", Mathf.Lerp(0.8f, 2.8f, brightness));
    SetColorIfPresent(material, "_CoreColor", new Color(0.7405064f, 1f, 0f, 1f));
    SetColorIfPresent(material, "_BoltColor", new Color(0.20717126f, 1f, 0f, 1f));
  }

  static void ApplyColdDefaults(Material material) {
    var freeze = Mathf.SmoothStep(0f, 1f, 0.598f);
    var icicle = Mathf.SmoothStep(0f, 1f, 0.205f);
    var cycle = Mathf.SmoothStep(0f, 1f, 0.204f);
    var detail = Mathf.SmoothStep(0f, 1f, 0.591f);
    var snowfall = Mathf.SmoothStep(0f, 1f, 0.61f);
    var shine = Mathf.SmoothStep(0f, 1f, 1f);

    SetFloatIfPresent(material, "_Freeze", Mathf.Lerp(0.28f, 1f, freeze));
    SetFloatIfPresent(material, "_IcicleLength", Mathf.Lerp(0.07f, 0.38f, icicle));
    SetFloatIfPresent(material, "_IcicleWidth", Mathf.Lerp(0.022f, 0.07f, icicle));
    SetFloatIfPresent(material, "_IcicleCount", Mathf.Lerp(13f, 7f, icicle));
    SetFloatIfPresent(material, "_CycleSpeed", Mathf.Lerp(0.16f, 1.45f, cycle));
    SetFloatIfPresent(material, "_FrostScale", Mathf.Lerp(2.6f, 8.2f, detail));
    SetFloatIfPresent(material, "_CrystalDetail", detail);
    SetFloatIfPresent(material, "_SnowAmount", snowfall);
    SetFloatIfPresent(material, "_SurfaceOpacity", Mathf.Lerp(0.5f, 0.08f, 1f));
    SetFloatIfPresent(material, "_IceOpacity", Mathf.Lerp(0.62f, 0.94f, freeze));
    SetFloatIfPresent(material, "_Specular", Mathf.Lerp(0.35f, 1.7f, shine));
    SetFloatIfPresent(material, "_Brightness", Mathf.Lerp(0.78f, 1.32f, shine));
    SetColorIfPresent(material, "_IceColor", new Color(0f, 0.5502393f, 1f, 1f));
    SetColorIfPresent(material, "_HighlightColor", Color.white);
  }

  static void ApplyDarkDefaults(Material material) {
    var presence = Mathf.SmoothStep(0f, 1f, 1f);
    var edge = Mathf.SmoothStep(0f, 1f, 0.474f);
    var reach = Mathf.SmoothStep(0f, 1f, 0.26f);
    var movement = Mathf.SmoothStep(0f, 1f, 0.259f);
    var veins = Mathf.SmoothStep(0f, 1f, 1f);
    var depth = Mathf.SmoothStep(0f, 1f, 1f);

    SetFloatIfPresent(material, "_Presence", Mathf.Lerp(0.24f, 1f, presence));
    SetFloatIfPresent(material, "_TendrilCount", Mathf.Lerp(3f, 8f, presence));
    SetFloatIfPresent(material, "_EdgeWidth", Mathf.Lerp(0.005f, 0.05f, edge));
    SetFloatIfPresent(material, "_EdgeOpacity", Mathf.Lerp(0.32f, 0.95f, edge));
    SetFloatIfPresent(material, "_TendrilReach", Mathf.Lerp(0.12f, 0.68f, reach));
    SetFloatIfPresent(material, "_TendrilWidth", Mathf.Lerp(0.006f, 0.022f, presence));
    SetFloatIfPresent(material, "_Movement", Mathf.Lerp(0.12f, 1.45f, movement));
    SetFloatIfPresent(material, "_VeinAmount", Mathf.Lerp(0.08f, 1f, veins));
    SetFloatIfPresent(material, "_VeinScale", Mathf.Lerp(3.2f, 9.4f, veins));
    SetFloatIfPresent(material, "_SurfaceOpacity", Mathf.Lerp(0.42f, 0.055f, 1f));
    SetFloatIfPresent(material, "_DarkOpacity", Mathf.Lerp(0.52f, 0.96f, depth));
    SetFloatIfPresent(material, "_Glow", Mathf.Lerp(0.68f, 1.45f, depth));
    SetColorIfPresent(material, "_PurpleColor", new Color(0.22f, 0.015f, 0.36f, 1f));
    SetColorIfPresent(
      material,
      "_AbyssColor",
      new Color(0.07342473f, 0.03938234f, 0.14150941f, 1f)
    );
  }

  void EnsureProceduralTextures() {
    if (proceduralNoise == null) {
      proceduralNoise = BuildProceduralNoiseTexture();
    }
    if (proceduralFlow == null) {
      proceduralFlow = BuildProceduralFlowTexture();
    }
  }

  Texture2D BuildProceduralNoiseTexture() {
    const int size = 96;
    var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
      name = $"{name}_FormEffectNoise",
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Repeat,
      hideFlags = HideFlags.HideAndDontSave
    };

    for (var y = 0; y < size; y++) {
      for (var x = 0; x < size; x++) {
        var u = x / (float)(size - 1);
        var v = y / (float)(size - 1);
        var broad = LayeredNoise(u * 2.7f, v * 3.6f, 31.4f);
        var detail = LayeredNoise(u * 7.2f, v * 5.8f, 9.7f);
        var value = Mathf.Clamp01((broad * 0.52f) + (detail * 0.48f));
        texture.SetPixel(x, y, new Color(value, value, value, 1f));
      }
    }
    texture.Apply(false, true);
    return texture;
  }

  Texture2D BuildProceduralFlowTexture() {
    const int size = 96;
    var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
      name = $"{name}_FormEffectFlow",
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Repeat,
      hideFlags = HideFlags.HideAndDontSave
    };

    for (var y = 0; y < size; y++) {
      for (var x = 0; x < size; x++) {
        var u = x / (float)(size - 1);
        var v = y / (float)(size - 1);
        var horizontal = LayeredNoise(u * 3.3f, v * 5.1f, 12.6f);
        var vertical = LayeredNoise(u * 5.7f, v * 2.8f, 43.2f);
        texture.SetPixel(x, y, new Color(horizontal, vertical, 0.5f, 1f));
      }
    }
    texture.Apply(false, true);
    return texture;
  }

  static float LayeredNoise(float x, float y, float seed) {
    var amplitude = 0.5f;
    var frequency = 1f;
    var sum = 0f;
    var weight = 0f;
    for (var octave = 0; octave < 3; octave++) {
      var value = Mathf.PerlinNoise(
        (x * frequency) + seed,
        (y * frequency) + (seed * 0.37f)
      );
      sum += value * amplitude;
      weight += amplitude;
      amplitude *= 0.5f;
      frequency *= 2f;
    }
    return weight > 0f ? sum / weight : 0f;
  }

  float GetAnimationTime() {
#if UNITY_EDITOR
    if (!Application.isPlaying) {
      return (float)EditorApplication.timeSinceStartup;
    }
#endif
    return Time.time;
  }

  void ReleaseGeneratedResources() {
    SetOverlayEnabled(false);
    ReleaseOverlayMaterial();
    if (overlayFilter != null) {
      overlayFilter.sharedMesh = null;
    }
    if (overlayRenderer != null) {
      overlayRenderer.sharedMaterial = null;
    }
    if (overlayRenderer != null) {
      DestroyGeneratedObject(overlayRenderer.gameObject);
    }
    overlayRenderer = null;
    overlayFilter = null;
    DestroyGeneratedObject(overlayMesh);
    DestroyGeneratedObject(proceduralNoise);
    DestroyGeneratedObject(proceduralFlow);
    overlayMesh = null;
    proceduralNoise = null;
    proceduralFlow = null;
  }

  void ReleaseOverlayMaterial() {
    if (overlayRenderer != null && overlayRenderer.sharedMaterial == overlayMaterial) {
      overlayRenderer.sharedMaterial = null;
    }
    DestroyGeneratedObject(overlayMaterial);
    overlayMaterial = null;
  }

  static void DestroyGeneratedObject(UnityEngine.Object generatedObject) {
    if (generatedObject == null) return;
    if (Application.isPlaying) {
      Destroy(generatedObject);
    }
    else {
      DestroyImmediate(generatedObject);
    }
  }

  static void SetFloatIfPresent(Material material, string property, float value) {
    if (material != null && material.HasProperty(property)) {
      material.SetFloat(property, value);
    }
  }

  static void SetFloatIfPresent(Material material, int property, float value) {
    if (material != null && material.HasProperty(property)) {
      material.SetFloat(property, value);
    }
  }

  static void SetColorIfPresent(Material material, string property, Color value) {
    if (material != null && material.HasProperty(property)) {
      material.SetColor(property, value);
    }
  }

  static void SetVectorIfPresent(Material material, int property, Vector4 value) {
    if (material != null && material.HasProperty(property)) {
      material.SetVector(property, value);
    }
  }

  static void SetTextureIfPresent(Material material, int property, Texture value) {
    if (material != null && value != null && material.HasProperty(property)) {
      material.SetTexture(property, value);
    }
  }
}
