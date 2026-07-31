#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[System.Serializable]
public sealed class AllIn1EffectPreviewDrawer : IEffectPreviewDrawer {
  [SerializeField] Sprite breakupPatternSprite;
  [SerializeField] Sprite flowPatternSprite;

  [SerializeField] float flameCoverage = 0.82f;
  [SerializeField] float flameSize = 0.62f;
  [SerializeField] float flameMovement = 0.58f;
  [SerializeField] float flameWildness = 0.54f;
  [SerializeField] float spriteVisibility = 0.82f;
  [SerializeField] float flameBrightness = 0.68f;

  [SerializeField] Color hotColor = new(1f, 0.94f, 0.58f, 1f);
  [SerializeField] Color flameColor = new(1f, 0.26f, 0.015f, 1f);
  [SerializeField] bool showAdvancedPatterns;

  Texture2D proceduralBreakupTexture;
  Texture2D proceduralFlowTexture;
  readonly SpriteTextureCache breakupTextureCache = new("Breakup");
  readonly SpriteTextureCache flowTextureCache = new("Flow");

  public string DisplayName => "Fire Preview";
  public string ShaderName => "Hidden/Esperanza/FirePreview";
  public string Description =>
    "The source sprite stays intact while a separate transparent layer grows animated flame tongues from its silhouette. Nothing dissolves, darkens, or burns away.";
  public Vector4 PreviewPadding => new(0.11f, 0.11f, 0.34f, 0.04f);

  public void OnEnable(AllIn1EffectPreviewWindow window) {
    if (proceduralBreakupTexture == null) {
      proceduralBreakupTexture = BuildProceduralBreakupTexture();
    }
    if (proceduralFlowTexture == null) {
      proceduralFlowTexture = BuildProceduralFlowTexture();
    }
  }

  public void OnDisable() {
    if (proceduralBreakupTexture != null) {
      Object.DestroyImmediate(proceduralBreakupTexture);
      proceduralBreakupTexture = null;
    }
    if (proceduralFlowTexture != null) {
      Object.DestroyImmediate(proceduralFlowTexture);
      proceduralFlowTexture = null;
    }
    breakupTextureCache.Clear();
    flowTextureCache.Clear();
  }

  public void DrawControls(AllIn1EffectPreviewWindow window) {
    EditorGUILayout.LabelField("Flames On Sprite", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "These controls art-direct the finished look. Each slider coordinates the flame shape, motion, breakup, and layering behind the scenes.",
      MessageType.None);
    flameCoverage = DrawFriendlySlider(
      "Fire Coverage",
      flameCoverage,
      "How much of the sprite carries flame: isolated patches to fully engulfed.");
    flameSize = DrawFriendlySlider(
      "Flame Size",
      flameSize,
      "Changes tongue height, width, and spacing together.");
    flameMovement = DrawFriendlySlider(
      "Movement",
      flameMovement,
      "Moves from a slow steady rise to fast climbing fire.");
    flameWildness = DrawFriendlySlider(
      "Wildness",
      flameWildness,
      "Adds irregular tongue lengths, sideways lick, and breakup.");
    spriteVisibility = DrawFriendlySlider(
      "Sprite Visibility",
      spriteVisibility,
      "Keeps the original sprite readable underneath the flames.");
    flameBrightness = DrawFriendlySlider(
      "Brightness",
      flameBrightness,
      "Moves from a soft ember glow to a bright hot flame.");

    EditorGUILayout.Space(6f);
    EditorGUILayout.LabelField("Flame Colors", EditorStyles.boldLabel);
    hotColor = EditorGUILayout.ColorField("Hot Center", hotColor);
    flameColor = EditorGUILayout.ColorField("Outer Flame", flameColor);

    EditorGUILayout.Space(6f);
    showAdvancedPatterns = EditorGUILayout.Foldout(showAdvancedPatterns, "Advanced Pattern Sources", true);
    if (showAdvancedPatterns) {
      DrawTextureControls(window);
    }
  }

  public void ApplyMaterialState(Material previewMaterial) {
    if (previewMaterial == null) return;

    SetTextureIfPresent(previewMaterial, "_NoiseTex", GetBreakupTexture());
    SetTextureIfPresent(previewMaterial, "_FlowTex", GetFlowTexture());

    var sizeCurve = Mathf.SmoothStep(0f, 1f, flameSize);
    var movementCurve = Mathf.SmoothStep(0f, 1f, flameMovement);
    var wildnessCurve = Mathf.SmoothStep(0f, 1f, flameWildness);
    var brightnessCurve = Mathf.SmoothStep(0f, 1f, flameBrightness);

    SetFloatIfPresent(previewMaterial, "_FlameCoverage", flameCoverage);
    SetFloatIfPresent(previewMaterial, "_FlameHeight", Mathf.Lerp(0.055f, 0.31f, sizeCurve));
    SetFloatIfPresent(previewMaterial, "_TongueWidth", Mathf.Lerp(0.025f, 0.095f, sizeCurve));
    SetFloatIfPresent(previewMaterial, "_TongueCount", Mathf.Lerp(12f, 5.5f, sizeCurve));
    SetFloatIfPresent(previewMaterial, "_FlowSpeed", Mathf.Lerp(0.22f, 2.35f, movementCurve));
    SetFloatIfPresent(
      previewMaterial,
      "_Sway",
      Mathf.Lerp(0.004f, 0.075f, wildnessCurve) * Mathf.Lerp(0.65f, 1.25f, movementCurve));
    SetFloatIfPresent(previewMaterial, "_Breakup", Mathf.Lerp(0.08f, 0.78f, wildnessCurve));
    SetFloatIfPresent(previewMaterial, "_NoiseScale", Mathf.Lerp(2.2f, 6.4f, wildnessCurve));
    SetFloatIfPresent(previewMaterial, "_SurfaceOpacity", Mathf.Lerp(0.62f, 0.16f, spriteVisibility));
    SetFloatIfPresent(previewMaterial, "_FlameOpacity", Mathf.Lerp(0.66f, 1f, brightnessCurve));
    SetFloatIfPresent(previewMaterial, "_Brightness", Mathf.Lerp(0.85f, 2.35f, brightnessCurve));

    SetColorIfPresent(previewMaterial, "_HotColor", hotColor);
    SetColorIfPresent(previewMaterial, "_FlameColor", flameColor);
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

  public void ResetDefaults() {
    flameCoverage = 0.82f;
    flameSize = 0.62f;
    flameMovement = 0.58f;
    flameWildness = 0.54f;
    spriteVisibility = 0.82f;
    flameBrightness = 0.68f;
    hotColor = new Color(1f, 0.94f, 0.58f, 1f);
    flameColor = new Color(1f, 0.26f, 0.015f, 1f);
    Debug.Log($"[{nameof(AllIn1EffectPreviewDrawer)}] Reset sprite-flame defaults");
  }

  public void NormalizeState() {
    flameCoverage = Mathf.Clamp01(flameCoverage);
    flameSize = Mathf.Clamp01(flameSize);
    flameMovement = Mathf.Clamp01(flameMovement);
    flameWildness = Mathf.Clamp01(flameWildness);
    spriteVisibility = Mathf.Clamp01(spriteVisibility);
    flameBrightness = Mathf.Clamp01(flameBrightness);
  }

  public void CopySettingsFrom(IEffectPreviewDrawer source) {
    if (source is not AllIn1EffectPreviewDrawer values) return;

    breakupPatternSprite = values.breakupPatternSprite;
    flowPatternSprite = values.flowPatternSprite;
    flameCoverage = values.flameCoverage;
    flameSize = values.flameSize;
    flameMovement = values.flameMovement;
    flameWildness = values.flameWildness;
    spriteVisibility = values.spriteVisibility;
    flameBrightness = values.flameBrightness;
    hotColor = values.hotColor;
    flameColor = values.flameColor;
    NormalizeState();
  }

  public Sprite GetTextureSlotSprite(string slotName) {
    return slotName switch {
      "Breakup" => breakupPatternSprite,
      "Flow" => flowPatternSprite,
      _ => null
    };
  }

  public void SetTextureSlotSprite(string slotName, Sprite sprite) {
    switch (slotName) {
      case "Breakup":
        breakupPatternSprite = sprite;
        break;
      case "Flow":
        flowPatternSprite = sprite;
        break;
    }
  }

  public bool AllowsProceduralDefault(string slotName) {
    return slotName is "Breakup" or "Flow";
  }

  public string GetTextureSlotDisplayName(string slotName) {
    return slotName switch {
      "Breakup" => "Breakup Sprite",
      "Flow" => "Flow Sprite",
      _ => "Texture Selector"
    };
  }

  Texture GetBreakupTexture() {
    return breakupTextureCache.GetTexture(breakupPatternSprite) ?? proceduralBreakupTexture;
  }

  Texture GetFlowTexture() {
    return flowTextureCache.GetTexture(flowPatternSprite) ?? proceduralFlowTexture;
  }

  float DrawFriendlySlider(string label, float value, string tooltip) {
    var content = new GUIContent(label, tooltip);
    return EditorGUILayout.Slider(content, value, 0f, 1f);
  }

  void DrawTextureControls(AllIn1EffectPreviewWindow window) {
    EditorGUILayout.HelpBox(
      "Optional: replace the generated breakup and flow patterns. Most looks should only need the controls above.",
      MessageType.None);
    breakupPatternSprite = window.DrawAtlasSpritePopup("Breakup Sprite", breakupPatternSprite, true, "Breakup");
    flowPatternSprite = window.DrawAtlasSpritePopup("Flow Sprite", flowPatternSprite, true, "Flow");
  }

  Texture2D BuildProceduralBreakupTexture() {
    const int size = 96;
    var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
      name = "FirePreview_ProceduralBreakup",
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Repeat,
      hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
    };

    for (var y = 0; y < size; y++) {
      for (var x = 0; x < size; x++) {
        var u = x / (float)(size - 1);
        var v = y / (float)(size - 1);
        var noiseA = AllIn1EffectPreviewWindowUtils.SeamlessLayeredNoise(u, v, 2.4f, 3.2f, 13.7f);
        var noiseB = AllIn1EffectPreviewWindowUtils.SeamlessLayeredNoise(u, v, 6.8f, 5.1f, 27.3f);
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
      name = "FirePreview_ProceduralFlow",
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Repeat,
      hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
    };

    for (var y = 0; y < size; y++) {
      for (var x = 0; x < size; x++) {
        var u = x / (float)(size - 1);
        var v = y / (float)(size - 1);
        var flowX = AllIn1EffectPreviewWindowUtils.SeamlessLayeredNoise(u, v, 3.1f, 4.7f, 5.3f);
        var flowY = AllIn1EffectPreviewWindowUtils.SeamlessLayeredNoise(u, v, 5.4f, 2.5f, 19.4f);
        texture.SetPixel(x, y, new Color(flowX, flowY, 0.5f, 1f));
      }
    }

    texture.Apply(false, true);
    return texture;
  }
}
#endif
