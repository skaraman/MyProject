#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[System.Serializable]
public sealed class AquaEffectPreviewDrawer : IEffectPreviewDrawer {
  [SerializeField] Sprite beadPatternSprite;
  [SerializeField] Sprite flowPatternSprite;

  [SerializeField] float wetness = 0.72f;
  [SerializeField] float dripSize = 0.58f;
  [SerializeField] float flow = 0.52f;
  [SerializeField] float beading = 0.64f;
  [SerializeField] float spriteVisibility = 0.88f;
  [SerializeField] float shine = 0.62f;

  [SerializeField] Color waterColor = new(0.04f, 0.42f, 0.92f, 1f);
  [SerializeField] Color highlightColor = new(0.54f, 0.95f, 1f, 1f);
  [SerializeField] bool showAdvancedPatterns;

  Texture2D proceduralBeadTexture;
  Texture2D proceduralFlowTexture;
  readonly SpriteTextureCache beadTextureCache = new("AquaBeads");
  readonly SpriteTextureCache flowTextureCache = new("AquaFlow");

  public string DisplayName => "Aqua Preview";
  public string ShaderName => "Hidden/Esperanza/AquaPreview";
  public string Description =>
    "A transparent wet layer forms tapered beads, runs down the sprite, and gathers into drops that detach and fall. The original sprite remains clearly visible underneath.";
  public Vector4 PreviewPadding => new(0.09f, 0.09f, 0.05f, 0.34f);

  public void OnEnable(AllIn1EffectPreviewWindow window) {
    if (proceduralBeadTexture == null) {
      proceduralBeadTexture = BuildProceduralBeadTexture();
    }
    if (proceduralFlowTexture == null) {
      proceduralFlowTexture = BuildProceduralFlowTexture();
    }
  }

  public void OnDisable() {
    if (proceduralBeadTexture != null) {
      Object.DestroyImmediate(proceduralBeadTexture);
      proceduralBeadTexture = null;
    }
    if (proceduralFlowTexture != null) {
      Object.DestroyImmediate(proceduralFlowTexture);
      proceduralFlowTexture = null;
    }
    beadTextureCache.Clear();
    flowTextureCache.Clear();
  }

  public void DrawControls(AllIn1EffectPreviewWindow window) {
    EditorGUILayout.LabelField("Water On Sprite", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "Surface rivulets and beads use organic breakup, while separate solid edge drips gather, detach, and fall below the sprite.",
      MessageType.None);

    wetness = DrawFriendlySlider(
      "Wetness",
      wetness,
      "Moves from a few damp paths to a fully wet surface with more wandering flow lines.");
    dripSize = DrawFriendlySlider(
      "Drip Size",
      dripSize,
      "Changes gathering length, randomized drip sizes, and spacing together.");
    flow = DrawFriendlySlider(
      "Downward Flow",
      flow,
      "Moves from slow clinging water to frequent, fast-falling drops on varied paths.");
    beading = DrawFriendlySlider(
      "Beading",
      beading,
      "Changes smooth sheets of water into separated, staggered teardrops.");
    spriteVisibility = DrawFriendlySlider(
      "Sprite Visibility",
      spriteVisibility,
      "Keeps the original sprite readable through the colored water.");
    shine = DrawFriendlySlider(
      "Wet Shine",
      shine,
      "Controls the bright glossy highlights on the surface and drop tips.");

    EditorGUILayout.Space(6f);
    EditorGUILayout.LabelField("Water Colors", EditorStyles.boldLabel);
    waterColor = EditorGUILayout.ColorField("Water Color", waterColor);
    highlightColor = EditorGUILayout.ColorField("Highlight Color", highlightColor);

    EditorGUILayout.Space(6f);
    showAdvancedPatterns = EditorGUILayout.Foldout(showAdvancedPatterns, "Advanced Pattern Sources", true);
    if (showAdvancedPatterns) {
      DrawTextureControls(window);
    }
  }

  public void ApplyMaterialState(Material previewMaterial) {
    if (previewMaterial == null) return;

    SetTextureIfPresent(previewMaterial, "_NoiseTex", GetBeadTexture());
    SetTextureIfPresent(previewMaterial, "_FlowTex", GetFlowTexture());

    var wetnessCurve = Mathf.SmoothStep(0f, 1f, wetness);
    var sizeCurve = Mathf.SmoothStep(0f, 1f, dripSize);
    var flowCurve = Mathf.SmoothStep(0f, 1f, flow);
    var beadingCurve = Mathf.SmoothStep(0f, 1f, beading);
    var shineCurve = Mathf.SmoothStep(0f, 1f, shine);

    SetFloatIfPresent(previewMaterial, "_Wetness", Mathf.Lerp(0.28f, 0.98f, wetnessCurve));
    SetFloatIfPresent(previewMaterial, "_DripLength", Mathf.Lerp(0.055f, 0.33f, sizeCurve));
    SetFloatIfPresent(
      previewMaterial,
      "_DripWidth",
      Mathf.Lerp(0.018f, 0.072f, sizeCurve) * Mathf.Lerp(1.2f, 0.82f, beadingCurve));
    SetFloatIfPresent(previewMaterial, "_DripCount", Mathf.Lerp(13f, 6f, sizeCurve));
    SetFloatIfPresent(previewMaterial, "_FlowSpeed", Mathf.Lerp(0.12f, 1.85f, flowCurve));
    SetFloatIfPresent(
      previewMaterial,
      "_Wobble",
      Mathf.Lerp(0.002f, 0.042f, beadingCurve) * Mathf.Lerp(0.55f, 1.2f, flowCurve));
    SetFloatIfPresent(previewMaterial, "_Beading", Mathf.Lerp(0.12f, 0.92f, beadingCurve));
    SetFloatIfPresent(previewMaterial, "_NoiseScale", Mathf.Lerp(2.4f, 6.8f, beadingCurve));
    SetFloatIfPresent(
      previewMaterial,
      "_SurfaceOpacity",
      Mathf.Lerp(0.42f, 0.075f, spriteVisibility) * Mathf.Lerp(0.68f, 1.08f, wetnessCurve));
    SetFloatIfPresent(
      previewMaterial,
      "_DripOpacity",
      Mathf.Lerp(0.48f, 0.92f, Mathf.Max(wetnessCurve, shineCurve * 0.72f)));
    SetFloatIfPresent(previewMaterial, "_Specular", Mathf.Lerp(0.18f, 1.15f, shineCurve));
    SetFloatIfPresent(previewMaterial, "_Brightness", Mathf.Lerp(0.72f, 1.62f, shineCurve));

    SetColorIfPresent(previewMaterial, "_WaterColor", waterColor);
    SetColorIfPresent(previewMaterial, "_HighlightColor", highlightColor);
  }

  public void ResetDefaults() {
    wetness = 0.72f;
    dripSize = 0.58f;
    flow = 0.52f;
    beading = 0.64f;
    spriteVisibility = 0.88f;
    shine = 0.62f;
    waterColor = new Color(0.04f, 0.42f, 0.92f, 1f);
    highlightColor = new Color(0.54f, 0.95f, 1f, 1f);
    Debug.Log($"[{nameof(AquaEffectPreviewDrawer)}] Reset sprite-water defaults");
  }

  public void NormalizeState() {
    wetness = Mathf.Clamp01(wetness);
    dripSize = Mathf.Clamp01(dripSize);
    flow = Mathf.Clamp01(flow);
    beading = Mathf.Clamp01(beading);
    spriteVisibility = Mathf.Clamp01(spriteVisibility);
    shine = Mathf.Clamp01(shine);
  }

  public void CopySettingsFrom(IEffectPreviewDrawer source) {
    if (source is not AquaEffectPreviewDrawer values) return;

    beadPatternSprite = values.beadPatternSprite;
    flowPatternSprite = values.flowPatternSprite;
    wetness = values.wetness;
    dripSize = values.dripSize;
    flow = values.flow;
    beading = values.beading;
    spriteVisibility = values.spriteVisibility;
    shine = values.shine;
    waterColor = values.waterColor;
    highlightColor = values.highlightColor;
    NormalizeState();
  }

  public Sprite GetTextureSlotSprite(string slotName) {
    return slotName switch {
      "Beads" => beadPatternSprite,
      "Flow" => flowPatternSprite,
      _ => null
    };
  }

  public void SetTextureSlotSprite(string slotName, Sprite sprite) {
    switch (slotName) {
      case "Beads":
        beadPatternSprite = sprite;
        break;
      case "Flow":
        flowPatternSprite = sprite;
        break;
    }
  }

  public bool AllowsProceduralDefault(string slotName) {
    return slotName is "Beads" or "Flow";
  }

  public string GetTextureSlotDisplayName(string slotName) {
    return slotName switch {
      "Beads" => "Bead Pattern Sprite",
      "Flow" => "Flow Pattern Sprite",
      _ => "Texture Selector"
    };
  }

  void DrawTextureControls(AllIn1EffectPreviewWindow window) {
    EditorGUILayout.HelpBox(
      "Optional: replace the generated bead and flow patterns. The friendly controls above are enough for most looks.",
      MessageType.None);
    beadPatternSprite = window.DrawAtlasSpritePopup("Bead Pattern", beadPatternSprite, true, "Beads");
    flowPatternSprite = window.DrawAtlasSpritePopup("Flow Pattern", flowPatternSprite, true, "Flow");
  }

  Texture GetBeadTexture() {
    return beadTextureCache.GetTexture(beadPatternSprite) ?? proceduralBeadTexture;
  }

  Texture GetFlowTexture() {
    return flowTextureCache.GetTexture(flowPatternSprite) ?? proceduralFlowTexture;
  }

  float DrawFriendlySlider(string label, float value, string tooltip) {
    return EditorGUILayout.Slider(new GUIContent(label, tooltip), value, 0f, 1f);
  }

  void SetFloatIfPresent(Material material, string propertyName, float value) {
    if (material.HasProperty(propertyName)) {
      material.SetFloat(propertyName, value);
    }
  }

  void SetColorIfPresent(Material material, string propertyName, Color value) {
    if (material.HasProperty(propertyName)) {
      material.SetColor(propertyName, value);
    }
  }

  void SetTextureIfPresent(Material material, string propertyName, Texture texture) {
    if (texture != null && material.HasProperty(propertyName)) {
      material.SetTexture(propertyName, texture);
    }
  }

  Texture2D BuildProceduralBeadTexture() {
    const int size = 96;
    var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
      name = "AquaPreview_ProceduralBeads",
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Repeat,
      hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
    };

    for (var y = 0; y < size; y++) {
      for (var x = 0; x < size; x++) {
        var u = x / (float)(size - 1);
        var v = y / (float)(size - 1);
        var broad = AllIn1EffectPreviewWindowUtils.SeamlessLayeredNoise(u, v, 2.7f, 3.6f, 31.4f);
        var beads = AllIn1EffectPreviewWindowUtils.SeamlessLayeredNoise(u, v, 7.2f, 5.8f, 9.7f);
        var value = Mathf.Clamp01((broad * 0.48f) + (beads * 0.52f));
        texture.SetPixel(x, y, new Color(value, value, value, 1f));
      }
    }

    texture.Apply(false, true);
    return texture;
  }

  Texture2D BuildProceduralFlowTexture() {
    const int size = 96;
    var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
      name = "AquaPreview_ProceduralFlow",
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Repeat,
      hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
    };

    for (var y = 0; y < size; y++) {
      for (var x = 0; x < size; x++) {
        var u = x / (float)(size - 1);
        var v = y / (float)(size - 1);
        var sideFlow = AllIn1EffectPreviewWindowUtils.SeamlessLayeredNoise(u, v, 3.3f, 5.1f, 12.6f);
        var downFlow = AllIn1EffectPreviewWindowUtils.SeamlessLayeredNoise(u, v, 5.7f, 2.8f, 43.2f);
        texture.SetPixel(x, y, new Color(sideFlow, downFlow, 0.5f, 1f));
      }
    }

    texture.Apply(false, true);
    return texture;
  }
}
#endif
