using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Sprites;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class GearController {
  sealed class GearDamagePieceState {
    public float currentFade = IntactGearFadeAmount;
    public float targetFade = IntactGearFadeAmount;
    public float fadePerSecond;
  }

  const string GearDamageFadeKeyword = "FADE_ON";
  const string GearDamageFadeProperty = "_FadeAmount";
  const string GearDamageLocationUpdatedTopic = "LocationUpdated";
  const string TorsoGearDamagePiece = "Torso";
  const string PelvisGearDamagePiece = "Pelvis";
  const float IntactGearFadeAmount = 0f;
  const float VisibleGearFadeStartAmount = 0.29f;
  const float TorsoAndPelvisMaximumGearDamageFade = 0.39f;
  static readonly int GearDamageFadePropertyId = Shader.PropertyToID(GearDamageFadeProperty);
  static readonly int FadeUseSpriteUvRectPropertyId = Shader.PropertyToID("_FadeUseSpriteUvRect");
  static readonly int FadeTexturePropertyId = Shader.PropertyToID("_FadeTex");
  static readonly int FadeTextureTransformPropertyId = Shader.PropertyToID("_FadeTex_ScaleAndTiling");
  static readonly int FadeBurnTexturePropertyId = Shader.PropertyToID("_FadeBurnTex");
  static readonly int FadeBurnTextureTransformPropertyId = Shader.PropertyToID("_FadeBurnTex_ScaleAndTiling");
  static readonly int FadeBurnWidthPropertyId = Shader.PropertyToID("_FadeBurnWidth");
  static readonly int FadeBurnTransitionPropertyId = Shader.PropertyToID("_FadeBurnTransition");
  static readonly int FadeBurnGlowPropertyId = Shader.PropertyToID("_FadeBurnGlow");
  static readonly int FadeBurnColorPropertyId = Shader.PropertyToID("_FadeBurnColor");
  static readonly int SpriteUvRectPropertyId = Shader.PropertyToID("_SpriteUvRect");

  [Header("Gear Damage Fade")]
  [SerializeField, Range(0.01f, 1f), Tooltip("Base fade factor multiplied by percentage points of maximum health lost. The multiplier is never below one.")]
  float gearDamageFadePerHit = 0.01f;
  [SerializeField, Range(VisibleGearFadeStartAmount, 1f), Tooltip("Shader Fade Amount at 100% gear damage. Damage begins visually at 0.29. Skin, hair, and eyes are never included.")]
  float maximumGearDamageFade = 0.49f;
  [SerializeField, Min(0.01f), Tooltip("Time for one hit's fade step to finish.")]
  float gearDamageFadeSecondsPerHit = 0.6f;

  [Header("Gear Damage Fade Live Shader Settings")]
  [SerializeField, Tooltip("Optional Fade Texture override. Leave empty to use the gear material's texture.")]
  Texture2D gearDamageFadeTextureOverride;
  [SerializeField, Tooltip("Fade Texture X scale.")]
  float gearDamageFadeTextureScaleX = 1f;
  [SerializeField, Tooltip("Fade Texture Y scale.")]
  float gearDamageFadeTextureScaleY = 1f;
  [SerializeField, Tooltip("Fade Texture X/Y offset.")]
  Vector2 gearDamageFadeTextureOffset;
  [SerializeField, Tooltip("Optional Fade Burn Texture override. Leave empty to use the gear material's texture.")]
  Texture2D gearDamageFadeBurnTextureOverride;
  [SerializeField, Min(0f), Tooltip("Fade Burn Texture X scale. The authored gear default is zero.")]
  float gearDamageFadeBurnTextureScaleX;
  [SerializeField, Min(0f), Tooltip("Fade Burn Texture Y scale. The authored gear default is zero.")]
  float gearDamageFadeBurnTextureScaleY;
  [SerializeField, Tooltip("Fade Burn Texture X/Y offset.")]
  Vector2 gearDamageFadeBurnTextureOffset;
  [SerializeField, Range(0f, 1f), Tooltip("Fade Burn Width.")]
  float gearDamageFadeBurnWidth;
  [SerializeField, Range(0f, 0.5f), Tooltip("Fade Burn Transition. Zero uses a hard fade edge.")]
  float gearDamageFadeBurnTransition;
  [SerializeField, Range(0f, 250f), Tooltip("Fade Burn Glow.")]
  float gearDamageFadeBurnGlow;
  [SerializeField, Tooltip("Fade Burn Color.")]
  Color gearDamageFadeBurnColor = Color.clear;

  [Header("Gear Damage Fade Live Amount Preview")]
  [SerializeField, Tooltip("Overrides visible gear damage for live inspection without changing accumulated damage.")]
  bool previewGearDamageFade;
  [SerializeField, Range(0f, 1f), Tooltip("Visible gear fade while Preview Gear Damage Fade is enabled.")]
  float gearDamageFadePreviewAmount;

  readonly Dictionary<string, GearDamagePieceState> gearDamageByPiece =
    new(StringComparer.OrdinalIgnoreCase);
  readonly List<string> gearDamagePieceCandidates = new(32);
  readonly HashSet<string> gearDamagePieceCandidateSet =
    new(StringComparer.OrdinalIgnoreCase);
  readonly List<SpriteRenderer> gearDamageRendererScratch = new(8);
  MaterialPropertyBlock gearDamagePropertyBlock;
  Action offGearDamageLocationUpdated;
  string gearDamageLocationId;
  int nextGearDamagePieceIndex;

#if UNITY_EDITOR
  void OnValidate() {
    NormalizeGearDamageDefaultsForRuntime();
    if (!Application.isPlaying) {
      return;
    }

    EditorApplication.delayCall -= ReapplyGearDamageFadeAfterInspectorChange;
    EditorApplication.delayCall += ReapplyGearDamageFadeAfterInspectorChange;
  }

  void ReapplyGearDamageFadeAfterInspectorChange() {
    if (this == null || !Application.isPlaying) {
      return;
    }

    ReapplyVisibleGearDamageFade();
  }

  void Reset() {
    gearDamageFadeTextureScaleX = 1f;
    gearDamageFadeTextureScaleY = 1f;
  }
#endif

  void NormalizeGearDamageDefaultsForRuntime() {
    if (Mathf.Approximately(gearDamageFadeTextureScaleX, 25f)) {
      gearDamageFadeTextureScaleX = 1f;
    }
    if (Mathf.Approximately(gearDamageFadeTextureScaleY, 25f)) {
      gearDamageFadeTextureScaleY = 1f;
    }
  }

  void InitializeGearDamageLocationReset() {
    gearDamageLocationId =
      LocationEnemyData.NormalizeLocationId(LocationManager.currentLocation);
    offGearDamageLocationUpdated?.Invoke();
    offGearDamageLocationUpdated = MessageBus.On(
      GearDamageLocationUpdatedTopic,
      HandleGearDamageLocationUpdated
    );
  }

  void DisposeGearDamageLocationReset() {
    offGearDamageLocationUpdated?.Invoke();
    offGearDamageLocationUpdated = null;
  }

  void HandleGearDamageLocationUpdated(object payload) {
    var nextLocationId =
      LocationEnemyData.NormalizeLocationId(Convert.ToString(payload));
    if (string.IsNullOrWhiteSpace(nextLocationId) ||
        string.Equals(
          gearDamageLocationId,
          nextLocationId,
          StringComparison.OrdinalIgnoreCase
        )) {
      return;
    }

    gearDamageLocationId = nextLocationId;
    previewGearDamageFade = false;
    gearDamageFadePreviewAmount = IntactGearFadeAmount;
    ResetGearDamageFade();
    ReapplyVisibleGearDamageFade();
  }

  public void ApplyGearHitDamage(
    EndlessNumber actualDamage,
    EndlessNumber maximumHealth
  ) {
    var fadePerHit = ResolveGearDamageFadePerHit(actualDamage, maximumHealth);
    var defaultFadeLimit = ResolveGearDamageProgressLimit(maximumGearDamageFade);
    if (fadePerHit <= 0f || defaultFadeLimit <= IntactGearFadeAmount) {
      return;
    }

    CollectEquippedGearDamagePieceCandidates(defaultFadeLimit);
    if (gearDamagePieceCandidates.Count == 0) {
      return;
    }

    var selectedIndex = nextGearDamagePieceIndex % gearDamagePieceCandidates.Count;
    var pieceName = gearDamagePieceCandidates[selectedIndex];
    nextGearDamagePieceIndex = (selectedIndex + 1) % gearDamagePieceCandidates.Count;

    var state = GetOrCreateGearDamagePieceState(pieceName);
    var pieceFadeLimit = ResolveGearDamageFadeLimit(pieceName, defaultFadeLimit);
    state.targetFade = Mathf.Min(pieceFadeLimit, state.targetFade + fadePerHit);
    state.fadePerSecond = Mathf.Max(
      0.0001f,
      (state.targetFade - state.currentFade) /
      Mathf.Max(0.01f, gearDamageFadeSecondsPerHit)
    );
    ConfigureGearDamagePieceRenderers(pieceName, state.currentFade);
  }

  float ResolveGearDamageFadePerHit(
    EndlessNumber actualDamage,
    EndlessNumber maximumHealth
  ) {
    var baseFade = Mathf.Max(0.01f, gearDamageFadePerHit);
    if (actualDamage == null ||
        !actualDamage.IsPositive ||
        maximumHealth == null ||
        !maximumHealth.IsPositive) {
      return baseFade;
    }

    var damagePercent = actualDamage.RatioTo(maximumHealth) * 100d;
    if (double.IsNaN(damagePercent) || damagePercent <= 1d) {
      return baseFade;
    }

    var multiplier = Math.Min(damagePercent, float.MaxValue);
    return (float)Math.Min(baseFade * multiplier, float.MaxValue);
  }

  void ResetGearDamageFade() {
    foreach (var pair in gearDamageByPiece) {
      var state = pair.Value;
      if (state == null) {
        continue;
      }

      state.currentFade = IntactGearFadeAmount;
      state.targetFade = IntactGearFadeAmount;
      state.fadePerSecond = 0f;
      ApplyGearDamagePieceToRenderers(
        pair.Key,
        state.currentFade,
        configureKeyword: false,
        includeInactive: true
      );
    }

    gearDamageByPiece.Clear();
    nextGearDamagePieceIndex = 0;
  }

  void TickGearDamageFade(float deltaTime) {
    if (deltaTime <= 0f || gearDamageByPiece.Count == 0) {
      return;
    }

    foreach (var pair in gearDamageByPiece) {
      var state = pair.Value;
      if (state == null || state.currentFade >= state.targetFade) {
        continue;
      }

      state.currentFade = Mathf.MoveTowards(
        state.currentFade,
        state.targetFade,
        Mathf.Max(0.0001f, state.fadePerSecond) * deltaTime
      );
      if (state.currentFade >= state.targetFade) {
        state.fadePerSecond = 0f;
      }
      ApplyGearDamagePieceToRenderers(
        pair.Key,
        state.currentFade,
        configureKeyword: false
      );
    }
  }

  void RefreshGearDamageFadeAfterEquip() {
    ReapplyVisibleGearDamageFade();
  }

  void ReapplyVisibleGearDamageFade() {
    ReapplyVisibleGearDamageFade(GearObjects);
    ReapplyVisibleGearDamageFade(OtherBounceGearObjects);
  }

  void ReapplyVisibleGearDamageFade(GameObject[] roots) {
    if (roots == null || roots.Length == 0) {
      return;
    }

    for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++) {
      var root = roots[rootIndex];
      if (root == null) {
        continue;
      }

      var fadeAmount = IntactGearFadeAmount;
      if (gearDamageByPiece.TryGetValue(
            NormalizeGearDamagePieceName(root.name),
            out var state
          ) &&
          state != null) {
        fadeAmount = state.currentFade;
      }

      gearDamageRendererScratch.Clear();
      root.GetComponentsInChildren(false, gearDamageRendererScratch);
      for (var rendererIndex = 0; rendererIndex < gearDamageRendererScratch.Count; rendererIndex++) {
        ApplyGearDamageFade(
          gearDamageRendererScratch[rendererIndex],
          fadeAmount,
          configureKeyword: true
        );
      }
    }
  }

  void CollectEquippedGearDamagePieceCandidates(float defaultFadeLimit) {
    gearDamagePieceCandidates.Clear();
    gearDamagePieceCandidateSet.Clear();

    var activeForm = EsperanzaForms.GetActive();
    EquippedItems.EnsureForm(activeForm);
    if (!EquippedItems.AllGearForms.TryGetValue(activeForm, out var slots) || slots == null) {
      return;
    }

    foreach (var slot in slots) {
      var gearItem = slot.Value;
      if (gearItem == null && !string.Equals(slot.Key, "Head", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      var mappingId = gearItem == null
        ? activeForm + "_no_Head"
        : gearItem.gearId + "_" + slot.Key;
      if (!EsperanzaGearParts.gearParts.TryGetValue(mappingId, out var mappedParts) ||
          mappedParts == null) {
        continue;
      }

      for (var partIndex = 0; partIndex < mappedParts.Count; partIndex++) {
        var pieceName = NormalizeGearDamagePieceName(mappedParts[partIndex]);
        if (string.IsNullOrEmpty(pieceName) ||
            !HasGearDamagePieceRoot(pieceName) ||
            !gearDamagePieceCandidateSet.Add(pieceName)) {
          continue;
        }

        var pieceFadeLimit = ResolveGearDamageFadeLimit(pieceName, defaultFadeLimit);
        if (gearDamageByPiece.TryGetValue(pieceName, out var state) &&
            state != null &&
            state.targetFade >= pieceFadeLimit) {
          continue;
        }

        gearDamagePieceCandidates.Add(pieceName);
      }
    }

    gearDamagePieceCandidates.Sort(StringComparer.OrdinalIgnoreCase);
  }

  GearDamagePieceState GetOrCreateGearDamagePieceState(string pieceName) {
    if (gearDamageByPiece.TryGetValue(pieceName, out var state) && state != null) {
      return state;
    }

    state = new GearDamagePieceState();
    gearDamageByPiece[pieceName] = state;
    return state;
  }

  void ConfigureGearDamagePieceRenderers(string pieceName, float fadeAmount) {
    ApplyGearDamagePieceToRenderers(pieceName, fadeAmount, configureKeyword: true);
  }

  void ApplyGearDamagePieceToRenderers(
    string pieceName,
    float fadeAmount,
    bool configureKeyword,
    bool includeInactive = false
  ) {
    VisitGearDamagePieceRenderers(
      GearObjects,
      pieceName,
      fadeAmount,
      configureKeyword,
      includeInactive
    );
    VisitGearDamagePieceRenderers(
      OtherBounceGearObjects,
      pieceName,
      fadeAmount,
      configureKeyword,
      includeInactive
    );
  }

  void VisitGearDamagePieceRenderers(
    GameObject[] roots,
    string pieceName,
    float fadeAmount,
    bool configureKeyword,
    bool includeInactive
  ) {
    if (roots == null || roots.Length == 0) {
      return;
    }

    for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++) {
      var root = roots[rootIndex];
      if (root == null || !GearDamagePieceNamesMatch(root.name, pieceName)) {
        continue;
      }

      gearDamageRendererScratch.Clear();
      root.GetComponentsInChildren(includeInactive, gearDamageRendererScratch);
      for (var rendererIndex = 0; rendererIndex < gearDamageRendererScratch.Count; rendererIndex++) {
        ApplyGearDamageFade(
          gearDamageRendererScratch[rendererIndex],
          fadeAmount,
          configureKeyword
        );
      }
    }
  }

  bool HasGearDamagePieceRoot(string pieceName) {
    return ContainsGearDamagePieceRoot(GearObjects, pieceName) ||
           ContainsGearDamagePieceRoot(OtherBounceGearObjects, pieceName);
  }

  static bool ContainsGearDamagePieceRoot(GameObject[] roots, string pieceName) {
    if (roots == null || roots.Length == 0) {
      return false;
    }

    for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++) {
      var root = roots[rootIndex];
      if (root != null && GearDamagePieceNamesMatch(root.name, pieceName)) {
        return true;
      }
    }

    return false;
  }

  static bool GearDamagePieceNamesMatch(string left, string right) {
    return string.Equals(
      NormalizeGearDamagePieceName(left),
      NormalizeGearDamagePieceName(right),
      StringComparison.OrdinalIgnoreCase
    );
  }

  static string NormalizeGearDamagePieceName(string pieceName) {
    return string.IsNullOrWhiteSpace(pieceName) ? "" : pieceName.Trim();
  }

  static float ResolveGearDamageFadeLimit(string pieceName, float defaultFadeLimit) {
    if (GearDamagePieceNamesMatch(pieceName, TorsoGearDamagePiece) ||
        GearDamagePieceNamesMatch(pieceName, PelvisGearDamagePiece)) {
      return Mathf.Min(
        defaultFadeLimit,
        ResolveGearDamageProgressLimit(TorsoAndPelvisMaximumGearDamageFade)
      );
    }

    return defaultFadeLimit;
  }

  static float ResolveGearDamageProgressLimit(float shaderFadeLimit) {
    return Mathf.Max(
      0f,
      Mathf.Clamp01(shaderFadeLimit) - VisibleGearFadeStartAmount
    );
  }

  static float ResolveVisibleGearFadeAmount(float damageProgress) {
    if (damageProgress <= 0f) {
      return IntactGearFadeAmount;
    }

    return Mathf.Clamp01(VisibleGearFadeStartAmount + damageProgress);
  }

  void ApplyGearDamageFade(
    SpriteRenderer spriteRenderer,
    float fadeAmount,
    bool configureKeyword
  ) {
    if (spriteRenderer == null) {
      return;
    }

    var material = spriteRenderer.sharedMaterial;
    if (material == null || !material.HasProperty(GearDamageFadePropertyId)) {
      return;
    }

    if (Application.isPlaying) {
      material = spriteRenderer.material;
    }
    if (configureKeyword || Application.isPlaying) {
      material.EnableKeyword(GearDamageFadeKeyword);
    }

    gearDamagePropertyBlock ??= new MaterialPropertyBlock();
    spriteRenderer.GetPropertyBlock(gearDamagePropertyBlock);
    var visibleFadeAmount = previewGearDamageFade
      ? gearDamageFadePreviewAmount
      : ResolveVisibleGearFadeAmount(fadeAmount);
    var clampedVisibleFadeAmount = Mathf.Clamp01(visibleFadeAmount);
    gearDamagePropertyBlock.SetFloat(
      GearDamageFadePropertyId,
      clampedVisibleFadeAmount
    );
    gearDamagePropertyBlock.SetFloat(FadeUseSpriteUvRectPropertyId, 1f);
    var fadeTexture = gearDamageFadeTextureOverride != null
      ? gearDamageFadeTextureOverride
      : material.GetTexture(FadeTexturePropertyId);
    if (fadeTexture != null) {
      gearDamagePropertyBlock.SetTexture(FadeTexturePropertyId, fadeTexture);
    }
    var textureTransform = new Vector4(
      gearDamageFadeTextureScaleX,
      gearDamageFadeTextureScaleY,
      gearDamageFadeTextureOffset.x,
      gearDamageFadeTextureOffset.y
    );
    gearDamagePropertyBlock.SetVector(FadeTextureTransformPropertyId, textureTransform);
    var fadeBurnTexture = gearDamageFadeBurnTextureOverride != null
      ? gearDamageFadeBurnTextureOverride
      : material.GetTexture(FadeBurnTexturePropertyId);
    if (fadeBurnTexture != null) {
      gearDamagePropertyBlock.SetTexture(FadeBurnTexturePropertyId, fadeBurnTexture);
    }
    var fadeBurnTextureTransform = new Vector4(
      Mathf.Max(0f, gearDamageFadeBurnTextureScaleX),
      Mathf.Max(0f, gearDamageFadeBurnTextureScaleY),
      gearDamageFadeBurnTextureOffset.x,
      gearDamageFadeBurnTextureOffset.y
    );
    gearDamagePropertyBlock.SetVector(
      FadeBurnTextureTransformPropertyId,
      fadeBurnTextureTransform
    );
    var fadeBurnWidth = Mathf.Clamp01(gearDamageFadeBurnWidth);
    var fadeBurnTransition = Mathf.Clamp(gearDamageFadeBurnTransition, 0f, 0.5f);
    var fadeBurnGlow = Mathf.Clamp(gearDamageFadeBurnGlow, 0f, 250f);
    gearDamagePropertyBlock.SetFloat(
      FadeBurnWidthPropertyId,
      fadeBurnWidth
    );
    gearDamagePropertyBlock.SetFloat(
      FadeBurnTransitionPropertyId,
      fadeBurnTransition
    );
    gearDamagePropertyBlock.SetFloat(
      FadeBurnGlowPropertyId,
      fadeBurnGlow
    );
    gearDamagePropertyBlock.SetColor(FadeBurnColorPropertyId, gearDamageFadeBurnColor);
    var spriteUvRect = ResolveSpriteUvRect(spriteRenderer.sprite);
    gearDamagePropertyBlock.SetVector(SpriteUvRectPropertyId, spriteUvRect);

    if (Application.isPlaying) {
      material.EnableKeyword(GearDamageFadeKeyword);
      material.SetFloat(GearDamageFadePropertyId, clampedVisibleFadeAmount);
      material.SetFloat(FadeUseSpriteUvRectPropertyId, 1f);
      if (fadeTexture != null) {
        material.SetTexture(FadeTexturePropertyId, fadeTexture);
      }
      material.SetVector(FadeTextureTransformPropertyId, textureTransform);
      if (fadeBurnTexture != null) {
        material.SetTexture(FadeBurnTexturePropertyId, fadeBurnTexture);
      }
      material.SetVector(
        FadeBurnTextureTransformPropertyId,
        fadeBurnTextureTransform
      );
      material.SetFloat(FadeBurnWidthPropertyId, fadeBurnWidth);
      material.SetFloat(FadeBurnTransitionPropertyId, fadeBurnTransition);
      material.SetFloat(FadeBurnGlowPropertyId, fadeBurnGlow);
      material.SetColor(FadeBurnColorPropertyId, gearDamageFadeBurnColor);
      material.SetVector(SpriteUvRectPropertyId, spriteUvRect);
    }

    spriteRenderer.SetPropertyBlock(gearDamagePropertyBlock);
  }

  static Vector4 ResolveSpriteUvRect(Sprite sprite) {
    if (sprite == null || sprite.texture == null) {
      return new Vector4(0f, 0f, 1f, 1f);
    }

    var outerUv = DataUtility.GetOuterUV(sprite);
    return new Vector4(
      outerUv.x,
      outerUv.y,
      outerUv.z - outerUv.x,
      outerUv.w - outerUv.y
    );
  }
}
