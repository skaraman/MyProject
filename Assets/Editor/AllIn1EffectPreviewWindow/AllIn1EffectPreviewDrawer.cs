#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[System.Serializable]
public sealed class AllIn1EffectPreviewDrawer : IEffectPreviewDrawer {
  [SerializeField] Sprite breakupPatternSprite;
  [SerializeField] Sprite flowPatternSprite;

  [SerializeField] float flameOpacity = 0.92f;
  [SerializeField] float flameHeight = 0.68f;
  [SerializeField] float bodyWidth = 0.46f;
  [SerializeField] float tipWidth = 0.08f;
  [SerializeField] float taperExponent = 1.15f;
  [SerializeField] float innerWidthRatio = 0.32f;
  [SerializeField] float innerSharpness = 1.9f;
  [SerializeField] float verticalFalloff = 2f;
  [SerializeField] float edgeSoftness = 0.09f;
  [SerializeField] float breakupAmount = 0.6f;
  [SerializeField] float coreIntensity = 1.6f;
  [SerializeField] float rimPower = 3.4f;

  [SerializeField] float flowSpeed = 1.1f;
  [SerializeField] float tongueStrength = 0.18f;
  [SerializeField] float tongueFrequency = 7.5f;
  [SerializeField] float distortionStrength = 0.12f;
  [SerializeField] float sourceMotion = 0.12f;
  [SerializeField] float patternRepeat = 5f;
  [SerializeField] float sourceFeatureBoost = 1f;
  [SerializeField] float noiseScale = 2.2f;
  [SerializeField] float detailScale = 6f;
  [SerializeField] float ribbonFrequency = 26f;
  [SerializeField] float ribbonThresholdMin = 0.54f;
  [SerializeField] float ribbonThresholdMax = 0.9f;
  [SerializeField] float ribbonInfluence = 0.82f;
  [SerializeField] float bodyIntensity = 1.35f;
  [SerializeField] float hotIntensity = 0.95f;
  [SerializeField] float brightIntensity = 0.78f;
  [SerializeField] float veilStrength = 0.38f;
  [SerializeField] float veilExponent = 1.55f;
  [SerializeField] float veilStart = 0.08f;
  [SerializeField] float veilEnd = 0.9f;
  [SerializeField] float sparkAmount = 1f;
  [SerializeField] float sparkThreshold = 0.84f;
  [SerializeField] float sparkSizeMin = 0.06f;
  [SerializeField] float sparkSizeMax = 0.18f;
  [SerializeField] float sparkRiseSpeed = 3.6f;
  [SerializeField] float sparkDrift = 0.65f;
  [SerializeField] float sparkGridX = 10f;
  [SerializeField] float sparkGridY = 18f;
  [SerializeField] float sparkLife = 1.5f;
  [SerializeField] float sparkBandStart = 0.15f;
  [SerializeField] float sparkBandEnd = 1.26f;
  [SerializeField] float sparkEnvelopePower = 2.4f;
  [SerializeField] float sparkHotIntensity = 0.45f;
  [SerializeField] float sparkBrightIntensity = 1.35f;

  [SerializeField] Color brightColor = new(1f, 0.98f, 0.9f, 1f);
  [SerializeField] Color hotColor = new(1f, 0.63f, 0.2f, 1f);
  [SerializeField] Color bodyColor = new(0.19f, 0.04f, 0.01f, 1f);

  Texture2D proceduralBreakupTexture;
  Texture2D proceduralFlowTexture;
  readonly SpriteTextureCache breakupTextureCache = new("Breakup");
  readonly SpriteTextureCache flowTextureCache = new("Flow");

  public string DisplayName => "Fire Preview";

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
    DrawFlameControls();
    EditorGUILayout.Space(6f);
    DrawMotionControls();
    EditorGUILayout.Space(6f);
    DrawSourcePatternControls();
    EditorGUILayout.Space(6f);
    DrawRibbonControls();
    EditorGUILayout.Space(6f);
    DrawVeilControls();
    EditorGUILayout.Space(6f);
    DrawSparkControls();
    EditorGUILayout.Space(6f);
    DrawIntensityControls();
    EditorGUILayout.Space(6f);
    DrawColorControls();
    EditorGUILayout.Space(6f);
    DrawTextureControls(window);
  }

  public void ApplyMaterialState(Material previewMaterial) {
    if (previewMaterial == null) return;

    SetTextureIfPresent(previewMaterial, "_NoiseTex", GetBreakupTexture());
    SetTextureIfPresent(previewMaterial, "_FlowTex", GetFlowTexture());

    SetFloatIfPresent(previewMaterial, "_Opacity", flameOpacity);
    SetFloatIfPresent(previewMaterial, "_FlameHeight", flameHeight);
    SetFloatIfPresent(previewMaterial, "_BodyWidth", bodyWidth);
    SetFloatIfPresent(previewMaterial, "_TipWidth", tipWidth);
    SetFloatIfPresent(previewMaterial, "_TaperExponent", taperExponent);
    SetFloatIfPresent(previewMaterial, "_InnerWidthRatio", innerWidthRatio);
    SetFloatIfPresent(previewMaterial, "_InnerSharpness", innerSharpness);
    SetFloatIfPresent(previewMaterial, "_VerticalFalloff", verticalFalloff);
    SetFloatIfPresent(previewMaterial, "_EdgeSoftness", edgeSoftness);
    SetFloatIfPresent(previewMaterial, "_Breakup", breakupAmount);
    SetFloatIfPresent(previewMaterial, "_NoiseScale", noiseScale);
    SetFloatIfPresent(previewMaterial, "_DetailScale", detailScale);
    SetFloatIfPresent(previewMaterial, "_FlowSpeed", flowSpeed);
    SetFloatIfPresent(previewMaterial, "_TongueStrength", tongueStrength);
    SetFloatIfPresent(previewMaterial, "_TongueFrequency", tongueFrequency);
    SetFloatIfPresent(previewMaterial, "_DistortionStrength", distortionStrength);
    SetFloatIfPresent(previewMaterial, "_SourceMotion", sourceMotion);
    SetFloatIfPresent(previewMaterial, "_PatternRepeat", patternRepeat);
    SetFloatIfPresent(previewMaterial, "_SourceFeatureBoost", sourceFeatureBoost);
    SetFloatIfPresent(previewMaterial, "_RibbonFrequency", ribbonFrequency);
    SetFloatIfPresent(previewMaterial, "_RibbonThresholdMin", ribbonThresholdMin);
    SetFloatIfPresent(previewMaterial, "_RibbonThresholdMax", ribbonThresholdMax);
    SetFloatIfPresent(previewMaterial, "_RibbonInfluence", ribbonInfluence);
    SetFloatIfPresent(previewMaterial, "_CoreIntensity", coreIntensity);
    SetFloatIfPresent(previewMaterial, "_RimPower", rimPower);
    SetFloatIfPresent(previewMaterial, "_BodyIntensity", bodyIntensity);
    SetFloatIfPresent(previewMaterial, "_HotIntensity", hotIntensity);
    SetFloatIfPresent(previewMaterial, "_BrightIntensity", brightIntensity);
    SetFloatIfPresent(previewMaterial, "_VeilStrength", veilStrength);
    SetFloatIfPresent(previewMaterial, "_VeilExponent", veilExponent);
    SetFloatIfPresent(previewMaterial, "_VeilStart", veilStart);
    SetFloatIfPresent(previewMaterial, "_VeilEnd", veilEnd);
    SetFloatIfPresent(previewMaterial, "_SparkAmount", sparkAmount);
    SetFloatIfPresent(previewMaterial, "_SparkThreshold", sparkThreshold);
    SetFloatIfPresent(previewMaterial, "_SparkSizeMin", sparkSizeMin);
    SetFloatIfPresent(previewMaterial, "_SparkSizeMax", sparkSizeMax);
    SetFloatIfPresent(previewMaterial, "_SparkRiseSpeed", sparkRiseSpeed);
    SetFloatIfPresent(previewMaterial, "_SparkDrift", sparkDrift);
    SetFloatIfPresent(previewMaterial, "_SparkGridX", sparkGridX);
    SetFloatIfPresent(previewMaterial, "_SparkGridY", sparkGridY);
    SetFloatIfPresent(previewMaterial, "_SparkLife", sparkLife);
    SetFloatIfPresent(previewMaterial, "_SparkBandStart", sparkBandStart);
    SetFloatIfPresent(previewMaterial, "_SparkBandEnd", sparkBandEnd);
    SetFloatIfPresent(previewMaterial, "_SparkEnvelopePower", sparkEnvelopePower);
    SetFloatIfPresent(previewMaterial, "_SparkHotIntensity", sparkHotIntensity);
    SetFloatIfPresent(previewMaterial, "_SparkBrightIntensity", sparkBrightIntensity);
    SetColorIfPresent(previewMaterial, "_BrightColor", brightColor);
    SetColorIfPresent(previewMaterial, "_HotColor", hotColor);
    SetColorIfPresent(previewMaterial, "_BodyColor", bodyColor);
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
    flameOpacity = 0.92f;
    flameHeight = 0.68f;
    bodyWidth = 0.46f;
    tipWidth = 0.08f;
    taperExponent = 1.15f;
    innerWidthRatio = 0.32f;
    innerSharpness = 1.9f;
    verticalFalloff = 2f;
    edgeSoftness = 0.09f;
    breakupAmount = 0.6f;
    coreIntensity = 1.6f;
    rimPower = 3.4f;
    flowSpeed = 1.1f;
    tongueStrength = 0.18f;
    tongueFrequency = 7.5f;
    distortionStrength = 0.12f;
    sourceMotion = 0.12f;
    patternRepeat = 5f;
    sourceFeatureBoost = 1f;
    noiseScale = 2.2f;
    detailScale = 6f;
    ribbonFrequency = 26f;
    ribbonThresholdMin = 0.54f;
    ribbonThresholdMax = 0.9f;
    ribbonInfluence = 0.82f;
    bodyIntensity = 1.35f;
    hotIntensity = 0.95f;
    brightIntensity = 0.78f;
    veilStrength = 0.38f;
    veilExponent = 1.55f;
    veilStart = 0.08f;
    veilEnd = 0.9f;
    sparkAmount = 1f;
    sparkThreshold = 0.84f;
    sparkSizeMin = 0.06f;
    sparkSizeMax = 0.18f;
    sparkRiseSpeed = 3.6f;
    sparkDrift = 0.65f;
    sparkGridX = 10f;
    sparkGridY = 18f;
    sparkLife = 1.5f;
    sparkBandStart = 0.15f;
    sparkBandEnd = 1.26f;
    sparkEnvelopePower = 2.4f;
    sparkHotIntensity = 0.45f;
    sparkBrightIntensity = 1.35f;
    brightColor = new Color(1f, 0.98f, 0.9f, 1f);
    hotColor = new Color(1f, 0.63f, 0.2f, 1f);
    bodyColor = new Color(0.19f, 0.04f, 0.01f, 1f);
    Debug.Log(
      $"[{nameof(AllIn1EffectPreviewDrawer)}] Reset edge-lit fire defaults " +
      $"edgeBrightness={coreIntensity:F2} darkInterior={bodyColor} hotEdge={hotColor} brightRim={brightColor}");
  }

  public void NormalizeState() {
    flameOpacity = Mathf.Clamp(flameOpacity, 0f, 1.5f);
    flameHeight = Mathf.Clamp(flameHeight, 0.02f, 1.5f);
    bodyWidth = Mathf.Clamp(bodyWidth, 0.02f, 1.5f);
    tipWidth = Mathf.Clamp(tipWidth, 0.01f, 1f);
    taperExponent = Mathf.Clamp(taperExponent, 0.1f, 4f);
    innerWidthRatio = Mathf.Clamp(innerWidthRatio, 0.05f, 0.95f);
    innerSharpness = Mathf.Clamp(innerSharpness, 0.2f, 6f);
    verticalFalloff = Mathf.Clamp(verticalFalloff, 0.2f, 4f);
    edgeSoftness = Mathf.Clamp(edgeSoftness, 0.001f, 0.8f);
    breakupAmount = Mathf.Clamp(breakupAmount, 0f, 4f);
    coreIntensity = Mathf.Clamp(coreIntensity, 0f, 6f);
    rimPower = Mathf.Clamp(rimPower, 0.2f, 8f);

    flowSpeed = Mathf.Clamp(flowSpeed, -2f, 8f);
    tongueStrength = Mathf.Clamp(tongueStrength, 0f, 1f);
    tongueFrequency = Mathf.Clamp(tongueFrequency, 0f, 40f);
    distortionStrength = Mathf.Clamp(distortionStrength, 0f, 1f);
    sourceMotion = Mathf.Clamp(sourceMotion, 0f, 1f);
    patternRepeat = Mathf.Clamp(patternRepeat, 1f, 20f);
    sourceFeatureBoost = Mathf.Clamp(sourceFeatureBoost, 0f, 3f);
    noiseScale = Mathf.Clamp(noiseScale, 0.05f, 20f);
    detailScale = Mathf.Clamp(detailScale, 0.1f, 40f);

    ribbonFrequency = Mathf.Clamp(ribbonFrequency, 0f, 80f);
    ribbonThresholdMin = Mathf.Clamp01(ribbonThresholdMin);
    ribbonThresholdMax = Mathf.Clamp01(ribbonThresholdMax);
    if (ribbonThresholdMax < ribbonThresholdMin) {
      ribbonThresholdMax = ribbonThresholdMin;
    }
    ribbonInfluence = Mathf.Clamp(ribbonInfluence, 0f, 2f);

    bodyIntensity = Mathf.Clamp(bodyIntensity, 0f, 4f);
    hotIntensity = Mathf.Clamp(hotIntensity, 0f, 4f);
    brightIntensity = Mathf.Clamp(brightIntensity, 0f, 4f);

    veilStrength = Mathf.Clamp(veilStrength, 0f, 3f);
    veilExponent = Mathf.Clamp(veilExponent, 0.2f, 4f);
    veilStart = Mathf.Clamp01(veilStart);
    veilEnd = Mathf.Clamp01(veilEnd);
    if (veilEnd < veilStart) {
      veilEnd = veilStart;
    }

    sparkAmount = Mathf.Clamp(sparkAmount, 0f, 4f);
    sparkThreshold = Mathf.Clamp01(sparkThreshold);
    sparkSizeMin = Mathf.Clamp(sparkSizeMin, 0.005f, 0.25f);
    sparkSizeMax = Mathf.Clamp(sparkSizeMax, sparkSizeMin, 0.5f);
    sparkRiseSpeed = Mathf.Clamp(sparkRiseSpeed, 0f, 12f);
    sparkDrift = Mathf.Clamp(sparkDrift, 0f, 2f);
    sparkGridX = Mathf.Clamp(sparkGridX, 1f, 40f);
    sparkGridY = Mathf.Clamp(sparkGridY, 1f, 60f);
    sparkLife = Mathf.Clamp(sparkLife, 0.2f, 6f);
    sparkBandStart = Mathf.Clamp(sparkBandStart, 0f, 1f);
    sparkBandEnd = Mathf.Clamp(sparkBandEnd, sparkBandStart, 2f);
    sparkEnvelopePower = Mathf.Clamp(sparkEnvelopePower, 0.1f, 6f);
    sparkHotIntensity = Mathf.Clamp(sparkHotIntensity, 0f, 4f);
    sparkBrightIntensity = Mathf.Clamp(sparkBrightIntensity, 0f, 6f);
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

  void DrawFlameControls() {
    EditorGUILayout.LabelField("Flame Shape", EditorStyles.boldLabel);
    flameOpacity = EditorGUILayout.Slider("Opacity", flameOpacity, 0f, 1.5f);
    flameHeight = EditorGUILayout.Slider("Flame Height", flameHeight, 0.02f, 1.5f);
    bodyWidth = EditorGUILayout.Slider("Body Width", bodyWidth, 0.02f, 1.5f);
    tipWidth = EditorGUILayout.Slider("Tip Width", tipWidth, 0.01f, 1f);
    taperExponent = EditorGUILayout.Slider("Taper Exponent", taperExponent, 0.1f, 4f);
    innerWidthRatio = EditorGUILayout.Slider("Inner Width", innerWidthRatio, 0.05f, 0.95f);
    innerSharpness = EditorGUILayout.Slider("Inner Sharpness", innerSharpness, 0.2f, 6f);
    verticalFalloff = EditorGUILayout.Slider("Vertical Falloff", verticalFalloff, 0.2f, 4f);
    edgeSoftness = EditorGUILayout.Slider("Edge Softness", edgeSoftness, 0.001f, 0.8f);
    breakupAmount = EditorGUILayout.Slider("Breakup", breakupAmount, 0f, 4f);
    coreIntensity = EditorGUILayout.Slider("Edge Brightness", coreIntensity, 0f, 6f);
    rimPower = EditorGUILayout.Slider("Rim Power", rimPower, 0.2f, 8f);
  }

  void DrawMotionControls() {
    EditorGUILayout.LabelField("Motion", EditorStyles.boldLabel);
    flowSpeed = EditorGUILayout.Slider("Flow Speed", flowSpeed, -2f, 8f);
    tongueStrength = EditorGUILayout.Slider("Tongue Strength", tongueStrength, 0f, 1f);
    tongueFrequency = EditorGUILayout.Slider("Tongue Frequency", tongueFrequency, 0f, 40f);
    distortionStrength = EditorGUILayout.Slider("Distortion", distortionStrength, 0f, 1f);
    noiseScale = EditorGUILayout.Slider("Breakup Scale", noiseScale, 0.05f, 20f);
    detailScale = EditorGUILayout.Slider("Detail Scale", detailScale, 0.1f, 40f);
  }

  void DrawSourcePatternControls() {
    EditorGUILayout.LabelField("Source Pattern", EditorStyles.boldLabel);
    sourceMotion = EditorGUILayout.Slider("Source Motion", sourceMotion, 0f, 1f);
    patternRepeat = EditorGUILayout.Slider("Pattern Repeat", patternRepeat, 1f, 20f);
    sourceFeatureBoost = EditorGUILayout.Slider("Feature Boost", sourceFeatureBoost, 0f, 3f);
  }

  void DrawRibbonControls() {
    EditorGUILayout.LabelField("Ribbon / Rim", EditorStyles.boldLabel);
    ribbonFrequency = EditorGUILayout.Slider("Ribbon Frequency", ribbonFrequency, 0f, 80f);
    ribbonThresholdMin = EditorGUILayout.Slider("Threshold Min", ribbonThresholdMin, 0f, 1f);
    ribbonThresholdMax = EditorGUILayout.Slider("Threshold Max", ribbonThresholdMax, 0f, 1f);
    ribbonInfluence = EditorGUILayout.Slider("Ribbon Influence", ribbonInfluence, 0f, 2f);
  }

  void DrawVeilControls() {
    EditorGUILayout.LabelField("Veil", EditorStyles.boldLabel);
    veilStrength = EditorGUILayout.Slider("Veil Strength", veilStrength, 0f, 3f);
    veilExponent = EditorGUILayout.Slider("Veil Exponent", veilExponent, 0.2f, 4f);
    veilStart = EditorGUILayout.Slider("Veil Start", veilStart, 0f, 1f);
    veilEnd = EditorGUILayout.Slider("Veil End", veilEnd, 0f, 1f);
  }

  void DrawSparkControls() {
    EditorGUILayout.LabelField("Sparks", EditorStyles.boldLabel);
    sparkAmount = EditorGUILayout.Slider("Spark Amount", sparkAmount, 0f, 4f);
    sparkThreshold = EditorGUILayout.Slider("Spawn Threshold", sparkThreshold, 0f, 1f);
    sparkSizeMin = EditorGUILayout.Slider("Size Min", sparkSizeMin, 0.005f, 0.25f);
    sparkSizeMax = EditorGUILayout.Slider("Size Max", sparkSizeMax, 0.01f, 0.5f);
    sparkRiseSpeed = EditorGUILayout.Slider("Rise Speed", sparkRiseSpeed, 0f, 12f);
    sparkDrift = EditorGUILayout.Slider("Drift", sparkDrift, 0f, 2f);
    sparkGridX = EditorGUILayout.Slider("Grid X", sparkGridX, 1f, 40f);
    sparkGridY = EditorGUILayout.Slider("Grid Y", sparkGridY, 1f, 60f);
    sparkLife = EditorGUILayout.Slider("Life", sparkLife, 0.2f, 6f);
    sparkBandStart = EditorGUILayout.Slider("Band Start", sparkBandStart, 0f, 1f);
    sparkBandEnd = EditorGUILayout.Slider("Band End", sparkBandEnd, 0.2f, 2f);
    sparkEnvelopePower = EditorGUILayout.Slider("Envelope Power", sparkEnvelopePower, 0.1f, 6f);
    sparkHotIntensity = EditorGUILayout.Slider("Hot Intensity", sparkHotIntensity, 0f, 4f);
    sparkBrightIntensity = EditorGUILayout.Slider("Bright Intensity", sparkBrightIntensity, 0f, 6f);
  }

  void DrawIntensityControls() {
    EditorGUILayout.LabelField("Intensities", EditorStyles.boldLabel);
    bodyIntensity = EditorGUILayout.Slider("Body", bodyIntensity, 0f, 4f);
    hotIntensity = EditorGUILayout.Slider("Hot", hotIntensity, 0f, 4f);
    brightIntensity = EditorGUILayout.Slider("Bright", brightIntensity, 0f, 4f);
  }

  void DrawColorControls() {
    EditorGUILayout.LabelField("Colors", EditorStyles.boldLabel);
    brightColor = EditorGUILayout.ColorField("Bright Rim", brightColor);
    hotColor = EditorGUILayout.ColorField("Hot Edge", hotColor);
    bodyColor = EditorGUILayout.ColorField("Dark Interior", bodyColor);
  }

  void DrawTextureControls(AllIn1EffectPreviewWindow window) {
    EditorGUILayout.LabelField("Texture Layers", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "Breakup controls the rim erosion, spark breakup, tongues, and gaps. Flow controls directional movement and swirl. Leaving either one on Procedural Default uses a generated texture instead of an atlas sprite.",
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
        var noiseA = AllIn1EffectPreviewWindowUtils.LayeredNoise(u * 2.4f, v * 3.2f, 13.7f);
        var noiseB = AllIn1EffectPreviewWindowUtils.LayeredNoise(u * 6.8f, v * 5.1f, 27.3f);
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
        var flowX = AllIn1EffectPreviewWindowUtils.LayeredNoise(u * 3.1f, v * 4.7f, 5.3f);
        var flowY = AllIn1EffectPreviewWindowUtils.LayeredNoise(u * 5.4f, v * 2.5f, 19.4f);
        texture.SetPixel(x, y, new Color(flowX, flowY, 0.5f, 1f));
      }
    }

    texture.Apply(false, true);
    return texture;
  }
}
#endif
