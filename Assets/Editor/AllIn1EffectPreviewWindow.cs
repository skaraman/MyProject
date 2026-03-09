#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class AllIn1EffectPreviewWindow : EditorWindow {
  const string WindowTitle = "Fire Preview";
  const string MenuPath = "Tools/Shader Preview/Fire Effect Preview";
  const string LegacyMenuPath = "Tools/Shader Preview/AllIn1 Effect Preview";
  const string PreviewTextureAssetPath = "Assets/Sprites/Core/Empty.png";
  const string TextureAtlasFolderPath = "Assets/Sprites/Core/Textures";
  const string ShaderName = "Hidden/Esperanza/FirePreview";
  const float WindowMinWidth = 760f;
  const float WindowMinHeight = 660f;
  const float SideBySideLayoutWidth = 760f;
  const float PreviewColumnMinWidth = 280f;
  const float PreviewColumnMaxWidth = 360f;

  enum PreviewSourceMode {
    SolidMask,
    EmptySprite,
    AnySprite,
    AtlasSprite
  }

  enum AtlasTextureSlot {
    None,
    MainMask,
    Breakup,
    Flow
  }

  sealed class SpriteTextureCache {
    public readonly string Label;
    public Sprite SourceSprite;
    public Texture2D Texture;

    public SpriteTextureCache(string label) {
      Label = label;
    }
  }

  [SerializeField] PreviewSourceMode sourceMode = PreviewSourceMode.SolidMask;
  [SerializeField] bool animatePreview = true;
  [SerializeField] float animationSpeed = 1f;
  [SerializeField] float previewScale = 0.88f;
  [SerializeField] Material previewMaterialAsset;
  [SerializeField] Sprite mainMaskSprite;
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

  [SerializeField] Color frameColor = new(0.11f, 0.11f, 0.12f, 1f);
  [SerializeField] Color previewBackgroundColor = new(0.14f, 0.14f, 0.15f, 1f);
  [SerializeField] Color previewAccentColor = new(0.09f, 0.09f, 0.1f, 1f);

  Texture2D emptyTexture;
  Texture2D solidPreviewMask;
  Texture2D proceduralBreakupTexture;
  Texture2D proceduralFlowTexture;
  Material previewMaterial;
  Material previewMaterialSourceAsset;
  Shader previewMaterialSourceShader;
  Sprite[] availableAtlasSprites = new Sprite[0];
  string[] availableAtlasSpriteLabels = new string[0];
  string[] availableAtlasSpriteLabelsWithDefault = { "Procedural Default" };
  readonly SpriteTextureCache mainMaskTextureCache = new("MainMask");
  readonly SpriteTextureCache breakupTextureCache = new("Breakup");
  readonly SpriteTextureCache flowTextureCache = new("Flow");
  AtlasTextureSlot activeTextureSlot = AtlasTextureSlot.None;
  Vector2 scrollPosition;
  double lastEditorTime;
  float previewTime;
  bool loggedMissingAssets;
  bool? usedSideBySideLayoutLastFrame;

  [MenuItem(MenuPath)]
  static void ShowWindow() {
    OpenWindow();
  }

  [MenuItem(LegacyMenuPath)]
  static void ShowLegacyWindow() {
    OpenWindow();
  }

  static void OpenWindow() {
    var window = GetWindow<AllIn1EffectPreviewWindow>(WindowTitle);
    window.minSize = new Vector2(WindowMinWidth, WindowMinHeight);
    window.Show();
  }

  void OnEnable() {
    titleContent = new GUIContent(WindowTitle);
    EnsurePreviewAssets();
    EditorApplication.update -= HandleEditorUpdate;
    EditorApplication.update += HandleEditorUpdate;
    ResetEditorClock();
  }

  void OnDisable() {
    EditorApplication.update -= HandleEditorUpdate;
    DestroyPreviewAssets();
  }

  void OnDestroy() {
    EditorApplication.update -= HandleEditorUpdate;
    DestroyPreviewAssets();
  }

  void OnGUI() {
    EnsurePreviewAssets();
    HandleTextureDropdownKeyboard();
    NormalizeControlState();

    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

    EditorGUILayout.LabelField("Custom Fire Effect Preview", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "This preview uses the dedicated fire shader with an edge-lit flame body. The main mask defines the silhouette, the breakup texture shapes erosion and sparks, and the flow texture keeps the upward motion and tongue shaping.",
      MessageType.Info);

    DrawSelectionAndPreview();
    EditorGUILayout.Space(10f);
    DrawControls();

    EditorGUILayout.EndScrollView();
  }

  void DrawSelectionAndPreview() {
    var useSideBySideLayout = UseSideBySideLayout();
    var previewColumnWidth = GetPreviewColumnWidth();
    LogLayoutModeIfChanged(useSideBySideLayout, previewColumnWidth);

    if (!useSideBySideLayout) {
      DrawSelectionControls();
      EditorGUILayout.Space(10f);
      DrawPreview();
      return;
    }

    using (new EditorGUILayout.HorizontalScope()) {
      using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true))) {
        DrawSelectionControls();
      }

      GUILayout.Space(10f);

      using (new EditorGUILayout.VerticalScope(GUILayout.Width(previewColumnWidth), GUILayout.ExpandWidth(false))) {
        DrawPreview();
      }
    }
  }

  void DrawSelectionControls() {
    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
      DrawMaterialControls();
      EditorGUILayout.Space(6f);
      DrawSourceControls();
    }
  }

  void DrawControls() {
    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
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
      DrawTextureControls();
      EditorGUILayout.Space(6f);
      DrawActionButtons();
    }
  }

  bool UseSideBySideLayout() {
    return position.width >= SideBySideLayoutWidth;
  }

  float GetPreviewColumnWidth() {
    var usableWidth = Mathf.Max(PreviewColumnMinWidth, position.width - 48f);
    return Mathf.Clamp(usableWidth * 0.4f, PreviewColumnMinWidth, PreviewColumnMaxWidth);
  }

  void LogLayoutModeIfChanged(bool useSideBySideLayout, float previewColumnWidth) {
    if (usedSideBySideLayoutLastFrame == useSideBySideLayout) return;

    usedSideBySideLayoutLastFrame = useSideBySideLayout;
    Debug.Log(
      $"[{nameof(AllIn1EffectPreviewWindow)}] Layout -> {(useSideBySideLayout ? "side-by-side" : "stacked")} " +
      $"(windowWidth={position.width:0.0}, previewColumnWidth={previewColumnWidth:0.0})");
  }

  void DrawMaterialControls() {
    EditorGUILayout.LabelField("Preview Material", EditorStyles.boldLabel);
    previewMaterialAsset = (Material)EditorGUILayout.ObjectField("Material Override", previewMaterialAsset, typeof(Material), false);

    if (previewMaterialAsset == null) {
      EditorGUILayout.HelpBox(
        "When empty, the preview uses the built-in fallback shader. Assign a material created from your Shader Graph to preview the actual graph output here.",
        MessageType.None);
      return;
    }

    EditorGUILayout.HelpBox(
      "The assigned material is cloned for preview, so the asset itself is not modified. Keep the Shader Graph property reference names aligned with this window if you want the controls below to drive it.",
      MessageType.None);
  }

  void DrawSourceControls() {
    EditorGUILayout.LabelField("Preview Source", EditorStyles.boldLabel);
    sourceMode = (PreviewSourceMode)EditorGUILayout.EnumPopup("Source Mode", sourceMode);

    if (sourceMode == PreviewSourceMode.AnySprite) {
      mainMaskSprite = (Sprite)EditorGUILayout.ObjectField("Test Sprite", mainMaskSprite, typeof(Sprite), false);

      if (mainMaskSprite == null) {
        EditorGUILayout.HelpBox(
          "Assign any sprite asset here to preview the fire against that exact silhouette.",
          MessageType.Warning);
      }
    }
    else if (sourceMode == PreviewSourceMode.AtlasSprite) {
      mainMaskSprite = DrawAtlasSpritePopup("Main Mask Sprite", mainMaskSprite, false, AtlasTextureSlot.MainMask);

      if (mainMaskSprite == null) {
        EditorGUILayout.HelpBox(
          $"Pick a sprite sub-asset from {TextureAtlasFolderPath} to define the main flame silhouette.",
          MessageType.Warning);
      }
    }
    else {
      using (new EditorGUI.DisabledScope(true)) {
        EditorGUILayout.ObjectField("Empty Source", emptyTexture, typeof(Texture2D), false);
      }
    }

    animatePreview = EditorGUILayout.Toggle("Animate", animatePreview);
    animationSpeed = EditorGUILayout.Slider("Animation Speed", animationSpeed, 0f, 3f);
    previewScale = EditorGUILayout.Slider("Preview Scale", previewScale, 0.35f, 1f);

    if (!animatePreview) {
      previewTime = EditorGUILayout.Slider("Preview Time", previewTime, 0f, 12f);
    }
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

  void NormalizeControlState() {
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

  void DrawColorControls() {
    EditorGUILayout.LabelField("Colors", EditorStyles.boldLabel);
    brightColor = EditorGUILayout.ColorField("Bright Rim", brightColor);
    hotColor = EditorGUILayout.ColorField("Hot Edge", hotColor);
    bodyColor = EditorGUILayout.ColorField("Dark Interior", bodyColor);
  }

  void DrawTextureControls() {
    EditorGUILayout.LabelField("Texture Layers", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "Breakup controls the rim erosion, spark breakup, tongues, and gaps. Flow controls directional movement and swirl. Leaving either one on Procedural Default uses a generated texture instead of an atlas sprite.",
      MessageType.None);
    breakupPatternSprite = DrawAtlasSpritePopup("Breakup Sprite", breakupPatternSprite, true, AtlasTextureSlot.Breakup);
    flowPatternSprite = DrawAtlasSpritePopup("Flow Sprite", flowPatternSprite, true, AtlasTextureSlot.Flow);

    if (activeTextureSlot != AtlasTextureSlot.None) {
      EditorGUILayout.HelpBox(
        $"Arrow keys: Up/Down cycles {GetTextureSlotDisplayName(activeTextureSlot)}.",
        MessageType.None);
    }
  }

  void DrawActionButtons() {
    using (new EditorGUILayout.HorizontalScope()) {
      if (GUILayout.Button("Reset Fire Defaults")) {
        ResetFireDefaults();
        Repaint();
      }

      if (GUILayout.Button("Reload Assets")) {
        ClearAtlasSpriteOptions();
        DestroyPreviewAssets();
        EnsurePreviewAssets();
        Repaint();
      }
    }
  }

  void DrawPreview() {
    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
      EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

      var previewTexture = GetPreviewTexture();
      if (previewTexture == null) {
        EditorGUILayout.HelpBox("Preview source texture could not be resolved for the current source mode.", MessageType.Error);
        return;
      }

      if (previewMaterial == null) {
        EditorGUILayout.HelpBox($"Preview material could not be created. Missing shader '{ShaderName}'.", MessageType.Error);
        return;
      }

      var layoutRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.MinHeight(320f), GUILayout.ExpandWidth(true));
      var previewRect = GetScaledPreviewRect(layoutRect, previewScale, previewTexture);
      DrawPreviewFrame(previewRect);

      DrawSourcePreview(previewRect, previewTexture);
      ApplyPreviewMaterialState(previewTexture, previewTime);
      EditorGUI.DrawPreviewTexture(previewRect, previewTexture, previewMaterial, ScaleMode.StretchToFill);
    }
  }

  void DrawPreviewFrame(Rect previewRect) {
    var outerRect = previewRect;
    outerRect.xMin -= 10f;
    outerRect.xMax += 10f;
    outerRect.yMin -= 10f;
    outerRect.yMax += 10f;
    EditorGUI.DrawRect(outerRect, frameColor);

    var innerRect = outerRect;
    innerRect.xMin += 4f;
    innerRect.xMax -= 4f;
    innerRect.yMin += 4f;
    innerRect.yMax -= 4f;
    DrawCheckerboard(innerRect, 18f, previewBackgroundColor, previewAccentColor);
  }

  void DrawSourcePreview(Rect previewRect, Texture previewTexture) {
    if (!ShouldDrawSourcePreview(previewTexture)) return;

    var previousColor = GUI.color;
    GUI.color = new Color(1f, 1f, 1f, 0.9f);
    EditorGUI.DrawPreviewTexture(previewRect, previewTexture, null, ScaleMode.StretchToFill);
    GUI.color = previousColor;
  }

  bool ShouldDrawSourcePreview(Texture previewTexture) {
    if (previewTexture == null) return false;
    return sourceMode is PreviewSourceMode.AnySprite or PreviewSourceMode.AtlasSprite;
  }

  Texture2D GetPreviewTexture() {
    switch (sourceMode) {
      case PreviewSourceMode.AnySprite:
        return GetCachedSpriteTexture(mainMaskSprite, mainMaskTextureCache) ?? solidPreviewMask;
      case PreviewSourceMode.AtlasSprite:
        return GetCachedSpriteTexture(mainMaskSprite, mainMaskTextureCache) ?? solidPreviewMask;
      case PreviewSourceMode.EmptySprite:
        return emptyTexture;
      default:
        return solidPreviewMask;
    }
  }

  Texture GetBreakupTexture() {
    return GetCachedSpriteTexture(breakupPatternSprite, breakupTextureCache) ?? proceduralBreakupTexture;
  }

  Texture GetFlowTexture() {
    return GetCachedSpriteTexture(flowPatternSprite, flowTextureCache) ?? proceduralFlowTexture;
  }

  void ApplyPreviewMaterialState(Texture sourceTexture, float timeValue) {
    EnsurePreviewMaterial();
    if (previewMaterial == null || sourceTexture == null) return;

    previewMaterial.mainTexture = sourceTexture;
    SetTextureIfPresent("_MainTex", sourceTexture);
    SetTextureIfPresent("_NoiseTex", GetBreakupTexture());
    SetTextureIfPresent("_FlowTex", GetFlowTexture());

    SetFloatIfPresent("_PreviewTime", timeValue);
    SetFloatIfPresent("_Opacity", flameOpacity);
    SetFloatIfPresent("_FlameHeight", flameHeight);
    SetFloatIfPresent("_BodyWidth", bodyWidth);
    SetFloatIfPresent("_TipWidth", tipWidth);
    SetFloatIfPresent("_TaperExponent", taperExponent);
    SetFloatIfPresent("_InnerWidthRatio", innerWidthRatio);
    SetFloatIfPresent("_InnerSharpness", innerSharpness);
    SetFloatIfPresent("_VerticalFalloff", verticalFalloff);
    SetFloatIfPresent("_EdgeSoftness", edgeSoftness);
    SetFloatIfPresent("_Breakup", breakupAmount);
    SetFloatIfPresent("_NoiseScale", noiseScale);
    SetFloatIfPresent("_DetailScale", detailScale);
    SetFloatIfPresent("_FlowSpeed", flowSpeed);
    SetFloatIfPresent("_TongueStrength", tongueStrength);
    SetFloatIfPresent("_TongueFrequency", tongueFrequency);
    SetFloatIfPresent("_DistortionStrength", distortionStrength);
    SetFloatIfPresent("_SourceMotion", sourceMotion);
    SetFloatIfPresent("_PatternRepeat", patternRepeat);
    SetFloatIfPresent("_SourceFeatureBoost", sourceFeatureBoost);
    SetFloatIfPresent("_RibbonFrequency", ribbonFrequency);
    SetFloatIfPresent("_RibbonThresholdMin", ribbonThresholdMin);
    SetFloatIfPresent("_RibbonThresholdMax", ribbonThresholdMax);
    SetFloatIfPresent("_RibbonInfluence", ribbonInfluence);
    SetFloatIfPresent("_CoreIntensity", coreIntensity);
    SetFloatIfPresent("_RimPower", rimPower);
    SetFloatIfPresent("_BodyIntensity", bodyIntensity);
    SetFloatIfPresent("_HotIntensity", hotIntensity);
    SetFloatIfPresent("_BrightIntensity", brightIntensity);
    SetFloatIfPresent("_VeilStrength", veilStrength);
    SetFloatIfPresent("_VeilExponent", veilExponent);
    SetFloatIfPresent("_VeilStart", veilStart);
    SetFloatIfPresent("_VeilEnd", veilEnd);
    SetFloatIfPresent("_SparkAmount", sparkAmount);
    SetFloatIfPresent("_SparkThreshold", sparkThreshold);
    SetFloatIfPresent("_SparkSizeMin", sparkSizeMin);
    SetFloatIfPresent("_SparkSizeMax", sparkSizeMax);
    SetFloatIfPresent("_SparkRiseSpeed", sparkRiseSpeed);
    SetFloatIfPresent("_SparkDrift", sparkDrift);
    SetFloatIfPresent("_SparkGridX", sparkGridX);
    SetFloatIfPresent("_SparkGridY", sparkGridY);
    SetFloatIfPresent("_SparkLife", sparkLife);
    SetFloatIfPresent("_SparkBandStart", sparkBandStart);
    SetFloatIfPresent("_SparkBandEnd", sparkBandEnd);
    SetFloatIfPresent("_SparkEnvelopePower", sparkEnvelopePower);
    SetFloatIfPresent("_SparkHotIntensity", sparkHotIntensity);
    SetFloatIfPresent("_SparkBrightIntensity", sparkBrightIntensity);
    SetColorIfPresent("_BrightColor", brightColor);
    SetColorIfPresent("_HotColor", hotColor);
    SetColorIfPresent("_BodyColor", bodyColor);
  }

  void SetFloatIfPresent(string propertyName, float value) {
    if (previewMaterial == null || !previewMaterial.HasProperty(propertyName)) return;
    previewMaterial.SetFloat(propertyName, value);
  }

  void SetColorIfPresent(string propertyName, Color value) {
    if (previewMaterial == null || !previewMaterial.HasProperty(propertyName)) return;
    previewMaterial.SetColor(propertyName, value);
  }

  void SetTextureIfPresent(string propertyName, Texture texture) {
    if (previewMaterial == null || !previewMaterial.HasProperty(propertyName) || texture == null) return;
    previewMaterial.SetTexture(propertyName, texture);
  }

  void HandleTextureDropdownKeyboard() {
    if (Event.current.type != EventType.KeyDown) return;
    if (activeTextureSlot == AtlasTextureSlot.None) return;
    if (GUI.GetNameOfFocusedControl() != GetTexturePopupControlName(activeTextureSlot)) return;

    var direction = 0;
    switch (Event.current.keyCode) {
      case KeyCode.UpArrow:
        direction = -1;
        break;
      case KeyCode.DownArrow:
        direction = 1;
        break;
    }

    if (direction == 0) return;
    if (!TryStepTextureSlotSelection(activeTextureSlot, direction)) return;

    Event.current.Use();
    GUI.changed = true;
    Repaint();
  }

  Sprite DrawAtlasSpritePopup(string label, Sprite currentSprite, bool allowDefault, AtlasTextureSlot slot) {
    EnsureAtlasSpriteOptions();
    if (availableAtlasSprites.Length == 0) {
      EditorGUILayout.HelpBox($"No sprite sub-assets were found under '{TextureAtlasFolderPath}'.", MessageType.Warning);
      return null;
    }

    var selectedIndex = GetAtlasSpriteIndex(currentSprite);
    var popupIndex = allowDefault ? selectedIndex + 1 : Mathf.Max(0, selectedIndex);
    var labels = allowDefault ? availableAtlasSpriteLabelsWithDefault : availableAtlasSpriteLabels;
    var controlName = GetTexturePopupControlName(slot);
    GUI.SetNextControlName(controlName);
    var popupRect = EditorGUILayout.GetControlRect();
    var newPopupIndex = EditorGUI.Popup(popupRect, label, popupIndex, labels);

    if (Event.current.type == EventType.MouseDown && popupRect.Contains(Event.current.mousePosition)) {
      activeTextureSlot = slot;
      GUI.FocusControl(controlName);
    }

    if (GUI.GetNameOfFocusedControl() == controlName || newPopupIndex != popupIndex) {
      activeTextureSlot = slot;
    }

    if (allowDefault) {
      return newPopupIndex <= 0 ? null : availableAtlasSprites[newPopupIndex - 1];
    }

    return availableAtlasSprites[Mathf.Clamp(newPopupIndex, 0, availableAtlasSprites.Length - 1)];
  }

  bool TryStepTextureSlotSelection(AtlasTextureSlot slot, int direction) {
    EnsureAtlasSpriteOptions();
    if (availableAtlasSprites.Length == 0) return false;
    if (slot == AtlasTextureSlot.MainMask && sourceMode != PreviewSourceMode.AtlasSprite) return false;

    var allowDefault = AllowsProceduralDefault(slot);
    var optionCount = availableAtlasSprites.Length + (allowDefault ? 1 : 0);
    if (optionCount <= 0) return false;

    var currentPopupIndex = GetTextureSlotPopupIndex(slot, allowDefault);
    var nextPopupIndex = WrapIndex(currentPopupIndex + direction, optionCount);
    var nextSprite = GetSpriteFromPopupIndex(nextPopupIndex, allowDefault);
    SetTextureSlotSprite(slot, nextSprite);

    Debug.Log($"[{nameof(AllIn1EffectPreviewWindow)}] {GetTextureSlotDisplayName(slot)} keyboard selection -> {GetTextureSlotSelectionLabel(slot)}");
    return true;
  }

  bool AllowsProceduralDefault(AtlasTextureSlot slot) {
    return slot is AtlasTextureSlot.Breakup or AtlasTextureSlot.Flow;
  }

  int GetTextureSlotPopupIndex(AtlasTextureSlot slot, bool allowDefault) {
    var selectedIndex = GetAtlasSpriteIndex(GetTextureSlotSprite(slot));
    return allowDefault ? selectedIndex + 1 : Mathf.Max(0, selectedIndex);
  }

  Sprite GetSpriteFromPopupIndex(int popupIndex, bool allowDefault) {
    if (allowDefault && popupIndex <= 0) return null;
    var spriteIndex = allowDefault ? popupIndex - 1 : popupIndex;
    return availableAtlasSprites[Mathf.Clamp(spriteIndex, 0, availableAtlasSprites.Length - 1)];
  }

  Sprite GetTextureSlotSprite(AtlasTextureSlot slot) {
    return slot switch {
      AtlasTextureSlot.MainMask => mainMaskSprite,
      AtlasTextureSlot.Breakup => breakupPatternSprite,
      AtlasTextureSlot.Flow => flowPatternSprite,
      _ => null
    };
  }

  void SetTextureSlotSprite(AtlasTextureSlot slot, Sprite sprite) {
    switch (slot) {
      case AtlasTextureSlot.MainMask:
        mainMaskSprite = sprite;
        break;
      case AtlasTextureSlot.Breakup:
        breakupPatternSprite = sprite;
        break;
      case AtlasTextureSlot.Flow:
        flowPatternSprite = sprite;
        break;
    }
  }

  string GetTextureSlotDisplayName(AtlasTextureSlot slot) {
    return slot switch {
      AtlasTextureSlot.MainMask => "Main Mask Sprite",
      AtlasTextureSlot.Breakup => "Breakup Sprite",
      AtlasTextureSlot.Flow => "Flow Sprite",
      _ => "Texture Selector"
    };
  }

  string GetTextureSlotSelectionLabel(AtlasTextureSlot slot) {
    var sprite = GetTextureSlotSprite(slot);
    return sprite != null ? sprite.name : "Procedural Default";
  }

  string GetTexturePopupControlName(AtlasTextureSlot slot) {
    return $"TexturePopup_{slot}";
  }

  int WrapIndex(int value, int count) {
    if (count <= 0) return 0;
    var wrapped = value % count;
    return wrapped < 0 ? wrapped + count : wrapped;
  }

  void HandleEditorUpdate() {
    if (!this) return;
    if (!animatePreview) return;

    var now = EditorApplication.timeSinceStartup;
    if (lastEditorTime <= 0d) {
      lastEditorTime = now;
      return;
    }

    var deltaTime = Mathf.Clamp((float)(now - lastEditorTime), 0f, 0.05f);
    lastEditorTime = now;
    previewTime += deltaTime * animationSpeed;
    Repaint();
  }

  void ResetEditorClock() {
    lastEditorTime = EditorApplication.timeSinceStartup;
  }

  Texture2D GetCachedSpriteTexture(Sprite sprite, SpriteTextureCache cache) {
    if (sprite == null) {
      ClearSpriteTextureCache(cache);
      return null;
    }

    if (cache.SourceSprite == sprite && cache.Texture != null) {
      return cache.Texture;
    }

    RebuildSpriteTextureCache(cache, sprite);
    return cache.Texture;
  }

  void RebuildSpriteTextureCache(SpriteTextureCache cache, Sprite sprite) {
    ClearSpriteTextureCache(cache);
    cache.SourceSprite = sprite;
    cache.Texture = BuildPreviewTextureFromSprite(sprite, cache.Label);

    var textureName = cache.Texture != null ? cache.Texture.name : "null";
    Debug.Log($"[{nameof(AllIn1EffectPreviewWindow)}] Rebuilt {cache.Label} preview texture from sprite '{sprite.name}' -> '{textureName}'.");
  }

  Texture2D BuildPreviewTextureFromSprite(Sprite sprite, string textureLabel) {
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
        name = $"FirePreview_{textureLabel}_{sprite.name}",
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

  void ClearSpriteTextureCache(SpriteTextureCache cache) {
    cache.SourceSprite = null;

    if (cache.Texture == null) return;
    DestroyImmediate(cache.Texture);
    cache.Texture = null;
  }

  void EnsurePreviewAssets() {
    if (emptyTexture == null) {
      emptyTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(PreviewTextureAssetPath);
    }

    if (solidPreviewMask == null) {
      solidPreviewMask = BuildSolidPreviewMask();
    }

    if (proceduralBreakupTexture == null) {
      proceduralBreakupTexture = BuildProceduralBreakupTexture();
    }

    if (proceduralFlowTexture == null) {
      proceduralFlowTexture = BuildProceduralFlowTexture();
    }

    EnsureAtlasSpriteOptions();
    EnsurePreviewMaterial();

    if (loggedMissingAssets) return;
    if (emptyTexture == null || previewMaterial == null) {
      Debug.LogWarning($"[{nameof(AllIn1EffectPreviewWindow)}] Preview setup incomplete. source='{PreviewTextureAssetPath}' shader='{ShaderName}'.");
      loggedMissingAssets = true;
    }
  }

  void EnsurePreviewMaterial() {
    if (previewMaterialAsset != null) {
      if (previewMaterial != null && previewMaterialSourceAsset == previewMaterialAsset && previewMaterialSourceShader == null) return;
      RebuildPreviewMaterialFromAsset(previewMaterialAsset);
      return;
    }

    var shader = Shader.Find(ShaderName);
    if (shader == null) return;
    if (previewMaterial != null && previewMaterialSourceAsset == null && previewMaterialSourceShader == shader) return;

    RebuildPreviewMaterialFromShader(shader);
  }

  void RebuildPreviewMaterialFromAsset(Material sourceMaterial) {
    DestroyPreviewMaterialInstance();
    if (sourceMaterial == null) return;

    previewMaterial = new Material(sourceMaterial) {
      name = $"{sourceMaterial.name}_PreviewInstance",
      hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
    };
    previewMaterialSourceAsset = sourceMaterial;
    previewMaterialSourceShader = null;
    Debug.Log($"[{nameof(AllIn1EffectPreviewWindow)}] Using material override '{sourceMaterial.name}' for preview.");
  }

  void RebuildPreviewMaterialFromShader(Shader shader) {
    DestroyPreviewMaterialInstance();
    if (shader == null) return;

    previewMaterial = new Material(shader) {
      name = "FirePreview_Material",
      hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
    };
    previewMaterialSourceAsset = null;
    previewMaterialSourceShader = shader;
    Debug.Log($"[{nameof(AllIn1EffectPreviewWindow)}] Using fallback shader '{shader.name}' for preview.");
  }

  void DestroyPreviewMaterialInstance() {
    previewMaterialSourceAsset = null;
    previewMaterialSourceShader = null;

    if (previewMaterial == null) return;
    DestroyImmediate(previewMaterial);
    previewMaterial = null;
  }

  void EnsureAtlasSpriteOptions() {
    if (availableAtlasSprites.Length > 0) return;

    var atlasPaths = new List<string>();
    var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { TextureAtlasFolderPath });

    foreach (var guid in textureGuids) {
      var path = AssetDatabase.GUIDToAssetPath(guid);
      if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".png")) continue;
      atlasPaths.Add(path);
    }

    atlasPaths.Sort();

    var sprites = new List<Sprite>();
    var labels = new List<string>();

    foreach (var atlasPath in atlasPaths) {
      var assets = AssetDatabase.LoadAllAssetsAtPath(atlasPath);
      var shortPath = atlasPath.Replace($"{TextureAtlasFolderPath}/", string.Empty);

      foreach (var asset in assets) {
        if (asset is not Sprite sprite) continue;

        sprites.Add(sprite);
        labels.Add($"{shortPath} / {sprite.name} [{sprite.rect.width:0}x{sprite.rect.height:0}]");
      }
    }

    availableAtlasSprites = sprites.ToArray();
    availableAtlasSpriteLabels = labels.ToArray();
    availableAtlasSpriteLabelsWithDefault = BuildAtlasSpriteLabelsWithDefault(availableAtlasSpriteLabels);
    Debug.Log($"[{nameof(AllIn1EffectPreviewWindow)}] Loaded {availableAtlasSprites.Length} atlas sprite options from '{TextureAtlasFolderPath}'.");

    if (sourceMode == PreviewSourceMode.AtlasSprite && mainMaskSprite == null && availableAtlasSprites.Length > 0) {
      mainMaskSprite = availableAtlasSprites[0];
    }
  }

  int GetAtlasSpriteIndex(Sprite sprite) {
    if (sprite == null) return -1;

    for (var i = 0; i < availableAtlasSprites.Length; i++) {
      if (availableAtlasSprites[i] == sprite) return i;
    }

    return -1;
  }

  string[] BuildAtlasSpriteLabelsWithDefault(string[] labels) {
    var popupLabels = new string[labels.Length + 1];
    popupLabels[0] = "Procedural Default";

    for (var i = 0; i < labels.Length; i++) {
      popupLabels[i + 1] = labels[i];
    }

    return popupLabels;
  }

  void ClearAtlasSpriteOptions() {
    availableAtlasSprites = new Sprite[0];
    availableAtlasSpriteLabels = new string[0];
    availableAtlasSpriteLabelsWithDefault = new[] { "Procedural Default" };
  }

  Texture2D BuildSolidPreviewMask() {
    var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) {
      name = "FirePreview_Mask",
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Clamp,
      hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
    };
    texture.SetPixel(0, 0, Color.white);
    texture.Apply(false, true);
    return texture;
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
      name = "FirePreview_ProceduralFlow",
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

  void DestroyPreviewAssets() {
    DestroyPreviewMaterialInstance();

    if (solidPreviewMask != null) {
      DestroyImmediate(solidPreviewMask);
      solidPreviewMask = null;
    }

    if (proceduralBreakupTexture != null) {
      DestroyImmediate(proceduralBreakupTexture);
      proceduralBreakupTexture = null;
    }

    if (proceduralFlowTexture != null) {
      DestroyImmediate(proceduralFlowTexture);
      proceduralFlowTexture = null;
    }

    ClearSpriteTextureCache(mainMaskTextureCache);
    ClearSpriteTextureCache(breakupTextureCache);
    ClearSpriteTextureCache(flowTextureCache);
  }

  void ResetFireDefaults() {
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
    animationSpeed = 1f;
    previewScale = 0.88f;
    Debug.Log(
      $"[{nameof(AllIn1EffectPreviewWindow)}] Reset edge-lit fire defaults " +
      $"edgeBrightness={coreIntensity:F2} darkInterior={bodyColor} hotEdge={hotColor} brightRim={brightColor}");
  }

  static void DrawCheckerboard(Rect rect, float tileSize, Color colorA, Color colorB) {
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

  static Rect GetScaledPreviewRect(Rect rect, float scale, Texture texture) {
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
}
#endif
