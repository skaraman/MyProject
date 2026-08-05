#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class AllIn1EffectPreviewWindow : EditorWindow {
  const string WindowTitle = "Form Effect Preview";
  const string MenuPath = "Tools/Shader Preview/Form Effects/Form Preview";
  const string FireMenuPath = "Tools/Shader Preview/Form Effects/Fire Preview";
  const string AquaMenuPath = "Tools/Shader Preview/Form Effects/Aqua Preview";
  const string BoltMenuPath = "Tools/Shader Preview/Form Effects/Bolt Preview";
  const string ColdMenuPath = "Tools/Shader Preview/Form Effects/Cold Preview";
  const string DarkMenuPath = "Tools/Shader Preview/Form Effects/Dark Preview";
  const string PreviewTextureAssetPath = "Packages/com.skaraman.myprojectcontent/Core/Sprites/Core/Empty.png";
  const string TextureAtlasFolderPath = "Packages/com.skaraman.myprojectcontent/Core/Sprites/Core/Textures";
  const string DefaultShaderName = "Hidden/Esperanza/FirePreview";
  const float WindowMinWidth = 760f;
  const float WindowMinHeight = 660f;
  const float SideBySideLayoutWidth = 760f;
  const float PreviewColumnMinWidth = 280f;
  const float PreviewColumnMaxWidth = 360f;

  enum FormEffect {
    Fire,
    Aqua,
    Bolt,
    Cold,
    Dark
  }

  enum PreviewSourceMode {
    SolidMask,
    EmptySprite,
    AnySprite,
    AtlasSprite
  }

  static readonly string[] PreviewSourceModeLabels = {
    "Built-in Solid Sprite",
    "No Source / Transparent",
    "Any Project Sprite",
    "Core Atlas Sprite"
  };

  static readonly string[] FormEffectLabels = {
    "Fire",
    "Aqua",
    "Bolt",
    "Cold",
    "Dark"
  };

  [SerializeField] FormEffect activeEffect = FormEffect.Fire;
  [SerializeReference] IEffectPreviewDrawer activeDrawer;
  [SerializeReference] IEffectPreviewDrawer fireDrawer;
  [SerializeReference] IEffectPreviewDrawer aquaDrawer;
  [SerializeReference] IEffectPreviewDrawer boltDrawer;
  [SerializeReference] IEffectPreviewDrawer coldDrawer;
  [SerializeReference] IEffectPreviewDrawer darkDrawer;

  [SerializeField] PreviewSourceMode sourceMode = PreviewSourceMode.SolidMask;
  [SerializeField] bool animatePreview = true;
  [SerializeField] float animationSpeed = 1f;
  [SerializeField] float previewScale = 0.88f;
  [SerializeField] Material previewMaterialAsset;
  [SerializeField] Material firePreviewMaterialAsset;
  [SerializeField] Material aquaPreviewMaterialAsset;
  [SerializeField] Material boltPreviewMaterialAsset;
  [SerializeField] Material coldPreviewMaterialAsset;
  [SerializeField] Material darkPreviewMaterialAsset;
  [SerializeField] Sprite mainMaskSprite;

  [SerializeField] Color frameColor = new(0.11f, 0.11f, 0.12f, 1f);
  [SerializeField] Color previewBackgroundColor = new(0.14f, 0.14f, 0.15f, 1f);
  [SerializeField] Color previewAccentColor = new(0.09f, 0.09f, 0.1f, 1f);

  Texture2D emptyTexture;
  Texture2D solidPreviewMask;
  Material previewMaterial;
  Material previewMaterialSourceAsset;
  Shader previewMaterialSourceShader;
  Sprite[] availableAtlasSprites = new Sprite[0];
  string[] availableAtlasSpriteLabels = new string[0];
  string[] availableAtlasSpriteLabelsWithDefault = { "Procedural Default" };
  readonly SpriteTextureCache mainMaskTextureCache = new("MainMask");
  readonly SpriteTextureCache normalMaskTextureCache = new("NormalMask");
  readonly SpriteTextureCache specularMaskTextureCache = new("SpecularMask");
  string activeTextureSlotName;
  Vector2 scrollPosition;
  double lastEditorTime;
  float previewTime;
  bool loggedMissingAssets;
  bool? usedSideBySideLayoutLastFrame;

  [MenuItem(MenuPath)]
  static void ShowWindow() {
    OpenWindow();
  }

  [MenuItem(FireMenuPath)]
  static void ShowFireWindow() {
    OpenWindow(FormEffect.Fire);
  }

  [MenuItem(AquaMenuPath)]
  static void ShowAquaWindow() {
    OpenWindow(FormEffect.Aqua);
  }

  [MenuItem(BoltMenuPath)]
  static void ShowBoltWindow() {
    OpenWindow(FormEffect.Bolt);
  }

  [MenuItem(ColdMenuPath)]
  static void ShowColdWindow() {
    OpenWindow(FormEffect.Cold);
  }

  [MenuItem(DarkMenuPath)]
  static void ShowDarkWindow() {
    OpenWindow(FormEffect.Dark);
  }

  static void OpenWindow(FormEffect? requestedEffect = null) {
    var window = GetWindow<AllIn1EffectPreviewWindow>(WindowTitle);
    window.minSize = new Vector2(WindowMinWidth, WindowMinHeight);
    if (requestedEffect.HasValue) {
      window.SetActiveEffect(requestedEffect.Value);
    }
    window.Show();
  }

  void OnEnable() {
    titleContent = new GUIContent(WindowTitle);
    CacheDrawer(activeDrawer);
    EnsureDrawerSlots();
    FormEffectPreviewDefaults.instance.ApplySavedDefaults(fireDrawer);
    FormEffectPreviewDefaults.instance.ApplySavedDefaults(aquaDrawer);
    FormEffectPreviewDefaults.instance.ApplySavedDefaults(boltDrawer);
    FormEffectPreviewDefaults.instance.ApplySavedDefaults(coldDrawer);
    FormEffectPreviewDefaults.instance.ApplySavedDefaults(darkDrawer);
    activeDrawer = GetDrawer(activeEffect);
    RestoreMaterialOverrideForActiveEffect();
    activeDrawer.OnEnable(this);
    EnsurePreviewAssets();
    ResetEditorClock();
    EditorApplication.update -= HandleEditorUpdate;
    EditorApplication.update += HandleEditorUpdate;
  }

  void OnDisable() {
    CacheMaterialOverrideForActiveEffect();
    if (activeDrawer != null) {
      activeDrawer.OnDisable();
    }
    DestroyPreviewAssets();
    EditorApplication.update -= HandleEditorUpdate;
  }

  void OnDestroy() {
    CacheMaterialOverrideForActiveEffect();
    if (activeDrawer != null) {
      activeDrawer.OnDisable();
    }
    DestroyPreviewAssets();
    EditorApplication.update -= HandleEditorUpdate;
  }

  void OnGUI() {
    EnsurePreviewAssets();
    HandleTextureDropdownKeyboard();
    if (activeDrawer != null) {
      activeDrawer.NormalizeState();
    }

    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

    DrawEffectSelection();
    EditorGUILayout.Space(6f);
    var drawerDisplayName = activeDrawer != null ? activeDrawer.DisplayName : "Shader Preview";
    EditorGUILayout.LabelField($"Custom {drawerDisplayName}", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      activeDrawer != null ? activeDrawer.Description : "Custom shader preview window.",
      MessageType.Info);

    DrawSelectionAndPreview();
    EditorGUILayout.Space(10f);
    DrawControls();

    EditorGUILayout.EndScrollView();
  }

  void DrawEffectSelection() {
    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
      EditorGUILayout.LabelField("Form Effect", EditorStyles.boldLabel);
      var nextEffect = (FormEffect)EditorGUILayout.Popup(
        "Preview",
        (int)activeEffect,
        FormEffectLabels);
      if (nextEffect != activeEffect) {
        SetActiveEffect(nextEffect);
      }
    }
  }

  void SetActiveEffect(FormEffect nextEffect) {
    if (nextEffect == activeEffect && DrawerMatchesEffect(activeDrawer, nextEffect)) {
      return;
    }

    if (activeDrawer != null) {
      activeDrawer.OnDisable();
      CacheDrawer(activeDrawer);
    }
    CacheMaterialOverrideForActiveEffect();

    activeEffect = nextEffect;
    EnsureDrawerSlots();
    activeDrawer = GetDrawer(activeEffect);
    previewMaterialAsset = GetMaterialOverride(activeEffect);
    activeDrawer.OnEnable(this);
    activeTextureSlotName = null;
    loggedMissingAssets = false;
    DestroyPreviewMaterialInstance();
    EnsurePreviewMaterial();
    ResetEditorClock();
    Repaint();
  }

  void EnsureDrawerSlots() {
    fireDrawer ??= new AllIn1EffectPreviewDrawer();
    aquaDrawer ??= new AquaEffectPreviewDrawer();
    boltDrawer ??= new BoltEffectPreviewDrawer();
    coldDrawer ??= new ColdEffectPreviewDrawer();
    darkDrawer ??= new DarkEffectPreviewDrawer();
  }

  void CacheDrawer(IEffectPreviewDrawer drawer) {
    switch (drawer) {
      case AllIn1EffectPreviewDrawer fire:
        fireDrawer = fire;
        break;
      case AquaEffectPreviewDrawer aqua:
        aquaDrawer = aqua;
        break;
      case BoltEffectPreviewDrawer bolt:
        boltDrawer = bolt;
        break;
      case ColdEffectPreviewDrawer cold:
        coldDrawer = cold;
        break;
      case DarkEffectPreviewDrawer dark:
        darkDrawer = dark;
        break;
    }
  }

  IEffectPreviewDrawer GetDrawer(FormEffect effect) {
    return effect switch {
      FormEffect.Aqua => aquaDrawer,
      FormEffect.Bolt => boltDrawer,
      FormEffect.Cold => coldDrawer,
      FormEffect.Dark => darkDrawer,
      _ => fireDrawer
    };
  }

  bool DrawerMatchesEffect(IEffectPreviewDrawer drawer, FormEffect effect) {
    return effect switch {
      FormEffect.Aqua => drawer is AquaEffectPreviewDrawer,
      FormEffect.Bolt => drawer is BoltEffectPreviewDrawer,
      FormEffect.Cold => drawer is ColdEffectPreviewDrawer,
      FormEffect.Dark => drawer is DarkEffectPreviewDrawer,
      _ => drawer is AllIn1EffectPreviewDrawer
    };
  }

  void RestoreMaterialOverrideForActiveEffect() {
    var storedOverride = GetMaterialOverride(activeEffect);
    if (storedOverride == null && previewMaterialAsset != null) {
      SetMaterialOverride(activeEffect, previewMaterialAsset);
      return;
    }
    previewMaterialAsset = storedOverride;
  }

  void CacheMaterialOverrideForActiveEffect() {
    SetMaterialOverride(activeEffect, previewMaterialAsset);
  }

  Material GetMaterialOverride(FormEffect effect) {
    return effect switch {
      FormEffect.Aqua => aquaPreviewMaterialAsset,
      FormEffect.Bolt => boltPreviewMaterialAsset,
      FormEffect.Cold => coldPreviewMaterialAsset,
      FormEffect.Dark => darkPreviewMaterialAsset,
      _ => firePreviewMaterialAsset
    };
  }

  void SetMaterialOverride(FormEffect effect, Material material) {
    switch (effect) {
      case FormEffect.Aqua:
        aquaPreviewMaterialAsset = material;
        break;
      case FormEffect.Bolt:
        boltPreviewMaterialAsset = material;
        break;
      case FormEffect.Cold:
        coldPreviewMaterialAsset = material;
        break;
      case FormEffect.Dark:
        darkPreviewMaterialAsset = material;
        break;
      default:
        firePreviewMaterialAsset = material;
        break;
    }
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
      if (activeDrawer != null) {
        activeDrawer.DrawControls(this);
      }
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
    sourceMode = (PreviewSourceMode)EditorGUILayout.Popup(
      "Source",
      (int)sourceMode,
      PreviewSourceModeLabels);

    if (sourceMode == PreviewSourceMode.AnySprite) {
      mainMaskSprite = (Sprite)EditorGUILayout.ObjectField("Test Sprite", mainMaskSprite, typeof(Sprite), false);

      if (mainMaskSprite == null) {
        EditorGUILayout.HelpBox(
          "Assign any sprite asset here to preview the selected form effect on that exact silhouette.",
          MessageType.Warning);
      }
    }
    else if (sourceMode == PreviewSourceMode.AtlasSprite) {
      mainMaskSprite = DrawAtlasSpritePopup("Main Mask Sprite", mainMaskSprite, false, "MainMask");

      if (mainMaskSprite == null) {
        EditorGUILayout.HelpBox(
          $"Pick a sprite sub-asset from {TextureAtlasFolderPath} to preview the selected form effect on it.",
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

  void DrawActionButtons() {
    using (new EditorGUILayout.HorizontalScope()) {
      if (GUILayout.Button("Set Default Values")) {
        if (activeDrawer != null) {
          activeDrawer.NormalizeState();
          FormEffectPreviewDefaults.instance.SaveDefaults(activeDrawer);
          ShowNotification(new GUIContent($"{activeDrawer.DisplayName} defaults saved"));
        }
      }

      if (GUILayout.Button("Reset Defaults")) {
        if (activeDrawer != null) {
          if (!FormEffectPreviewDefaults.instance.ApplySavedDefaults(activeDrawer)) {
            activeDrawer.ResetDefaults();
          }
        }
        previewScale = 0.88f;
        animationSpeed = 1f;
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
        EditorGUILayout.HelpBox(
          $"Preview material could not be created. Missing shader '{GetActiveShaderName()}'.",
          MessageType.Error);
        return;
      }

      var layoutRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.MinHeight(320f), GUILayout.ExpandWidth(true));
      var effectCanvasRect = GetPreviewCanvasRect(layoutRect, previewTexture);
      var sourceRect = GetSourceRectWithinEffectCanvas(effectCanvasRect);
      var sourceRectInEffectUv = GetSourceRectInEffectUv();
      DrawPreviewFrame(effectCanvasRect);

      DrawSourcePreview(sourceRect, previewTexture);
      ApplyPreviewMaterialState(previewTexture, previewTime, sourceRectInEffectUv);
      
      if (Event.current.type == EventType.Repaint) {
        int pass = previewMaterial.FindPass("Universal Forward");
        if (pass == -1) pass = previewMaterial.FindPass("ForwardBase");
        if (pass == -1) pass = 0;
        
        Graphics.DrawTexture(effectCanvasRect, previewTexture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, Color.white, previewMaterial, pass);
      }
    }
  }

  Rect GetPreviewCanvasRect(Rect layoutRect, Texture previewTexture) {
    if (previewMaterial == null || !previewMaterial.HasProperty("_SourceRectInEffect")) {
      return AllIn1EffectPreviewWindowUtils.GetScaledPreviewRect(layoutRect, previewScale, previewTexture);
    }

    var padding = GetPreviewPadding();
    var totalWidth = 1f + padding.x + padding.y;
    var totalHeight = 1f + padding.z + padding.w;
    var sourceAspect = previewTexture != null && previewTexture.height > 0
      ? previewTexture.width / (float)previewTexture.height
      : 1f;
    var canvasAspect = sourceAspect * totalWidth / totalHeight;
    var maxWidth = layoutRect.width * previewScale;
    var maxHeight = layoutRect.height * previewScale;
    var width = maxWidth;
    var height = width / Mathf.Max(0.01f, canvasAspect);

    if (height > maxHeight) {
      height = maxHeight;
      width = height * canvasAspect;
    }

    return new Rect(
      layoutRect.x + ((layoutRect.width - width) * 0.5f),
      layoutRect.y + ((layoutRect.height - height) * 0.5f),
      width,
      height);
  }

  Rect GetSourceRectWithinEffectCanvas(Rect effectCanvasRect) {
    if (previewMaterial == null || !previewMaterial.HasProperty("_SourceRectInEffect")) {
      return effectCanvasRect;
    }

    var padding = GetPreviewPadding();
    var totalWidth = 1f + padding.x + padding.y;
    var totalHeight = 1f + padding.z + padding.w;
    var sourceWidth = effectCanvasRect.width / totalWidth;
    var sourceHeight = effectCanvasRect.height / totalHeight;
    return new Rect(
      effectCanvasRect.x + (effectCanvasRect.width * padding.x / totalWidth),
      effectCanvasRect.y + (effectCanvasRect.height * padding.z / totalHeight),
      sourceWidth,
      sourceHeight);
  }

  Vector4 GetSourceRectInEffectUv() {
    if (previewMaterial == null || !previewMaterial.HasProperty("_SourceRectInEffect")) {
      return new Vector4(0f, 0f, 1f, 1f);
    }

    var padding = GetPreviewPadding();
    var totalWidth = 1f + padding.x + padding.y;
    var totalHeight = 1f + padding.z + padding.w;
    return new Vector4(
      padding.x / totalWidth,
      padding.w / totalHeight,
      1f / totalWidth,
      1f / totalHeight);
  }

  Vector4 GetPreviewPadding() {
    var padding = activeDrawer != null ? activeDrawer.PreviewPadding : Vector4.zero;
    return new Vector4(
      Mathf.Max(0f, padding.x),
      Mathf.Max(0f, padding.y),
      Mathf.Max(0f, padding.z),
      Mathf.Max(0f, padding.w));
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
    AllIn1EffectPreviewWindowUtils.DrawCheckerboard(innerRect, 18f, previewBackgroundColor, previewAccentColor);
  }

  void DrawSourcePreview(Rect previewRect, Texture previewTexture) {
    if (!ShouldDrawSourcePreview(previewTexture)) return;

    var previousColor = GUI.color;
    GUI.color = Color.white;
    GUI.DrawTexture(previewRect, previewTexture, ScaleMode.StretchToFill, true);
    GUI.color = previousColor;
  }

  bool ShouldDrawSourcePreview(Texture previewTexture) {
    if (previewTexture == null) return false;
    return sourceMode != PreviewSourceMode.EmptySprite;
  }

  Texture2D GetPreviewTexture() {
    switch (sourceMode) {
      case PreviewSourceMode.AnySprite:
      case PreviewSourceMode.AtlasSprite:
        return mainMaskTextureCache.GetTexture(mainMaskSprite) ?? solidPreviewMask;
      case PreviewSourceMode.EmptySprite:
        return emptyTexture;
      default:
        return solidPreviewMask;
    }
  }

  void ApplyPreviewMaterialState(Texture sourceTexture, float timeValue, Vector4 sourceRectInEffectUv) {
    EnsurePreviewMaterial();
    if (previewMaterial == null || sourceTexture == null) return;

    previewMaterial.mainTexture = sourceTexture;
    SetTextureIfPresent("_MainTex", sourceTexture);
    SetVectorIfPresent("_SourceRectInEffect", sourceRectInEffectUv);
    SetVectorIfPresent("_SpriteUvRect", new Vector4(0f, 0f, 1f, 1f));
    SetFloatIfPresent("_HasNormalMap", 0f);
    SetTextureIfPresent("_SpecularMap", Texture2D.blackTexture);

    if (mainMaskSprite != null) {
      string spritePath = AssetDatabase.GetAssetPath(mainMaskSprite);
      if (!string.IsNullOrEmpty(spritePath) && spritePath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) {
        if (SpriteStreamingTextureImportPolicy.TryGetPairedNormalAtlasPath(spritePath, out var normalPath) &&
            TryLoadCompanionSprite(normalPath, mainMaskSprite.name, out var normalSprite)) {
          var normalTex = normalMaskTextureCache.GetTexture(normalSprite);
          if (normalTex != null) {
            SetTextureIfPresent("_NormalMap", normalTex);
            SetFloatIfPresent("_HasNormalMap", 1f);
          }
        }
        if (SpriteStreamingTextureImportPolicy.TryGetPairedSpecularAtlasPath(spritePath, out var specularPath) &&
            TryLoadCompanionSprite(specularPath, mainMaskSprite.name, out var specularSprite)) {
          var specularTex = specularMaskTextureCache.GetTexture(specularSprite);
          if (specularTex != null) {
            SetTextureIfPresent("_SpecularMap", specularTex);
          }
        }
      }
    }

    if (previewMaterial.HasProperty("_PreviewTime")) {
      previewMaterial.SetFloat("_PreviewTime", timeValue);
    }

    if (activeDrawer != null) {
      activeDrawer.ApplyMaterialState(previewMaterial);
    }
  }

  void SetTextureIfPresent(string propertyName, Texture texture) {
    if (previewMaterial == null || !previewMaterial.HasProperty(propertyName) || texture == null) return;
    previewMaterial.SetTexture(propertyName, texture);
  }

  static bool TryLoadCompanionSprite(string assetPath, string preferredSpriteName, out Sprite sprite) {
    sprite = null;
    if (string.IsNullOrWhiteSpace(assetPath)) return false;

    Sprite onlySprite = null;
    var spriteCount = 0;
    var allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
    for (var i = 0; i < allAssets.Length; i++) {
      if (allAssets[i] is not Sprite candidate) continue;
      onlySprite = candidate;
      spriteCount++;
      if (string.Equals(candidate.name, preferredSpriteName, System.StringComparison.Ordinal)) {
        sprite = candidate;
        return true;
      }
    }

    if (spriteCount != 1) return false;
    sprite = onlySprite;
    return sprite != null;
  }

  void SetVectorIfPresent(string propertyName, Vector4 value) {
    if (previewMaterial == null || !previewMaterial.HasProperty(propertyName)) return;
    previewMaterial.SetVector(propertyName, value);
  }

  void SetFloatIfPresent(string propertyName, float value) {
    if (previewMaterial == null || !previewMaterial.HasProperty(propertyName)) return;
    previewMaterial.SetFloat(propertyName, value);
  }

  void HandleTextureDropdownKeyboard() {
    if (Event.current.type != EventType.KeyDown) return;
    if (string.IsNullOrEmpty(activeTextureSlotName)) return;
    if (GUI.GetNameOfFocusedControl() != GetTexturePopupControlName(activeTextureSlotName)) return;

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
    if (!TryStepTextureSlotSelection(activeTextureSlotName, direction)) return;

    Event.current.Use();
    GUI.changed = true;
    Repaint();
  }

  public Sprite DrawAtlasSpritePopup(string label, Sprite currentSprite, bool allowDefault, string slotName) {
    EnsureAtlasSpriteOptions();
    if (availableAtlasSprites.Length == 0) {
      EditorGUILayout.HelpBox($"No sprite sub-assets were found under '{TextureAtlasFolderPath}'.", MessageType.Warning);
      return null;
    }

    var selectedIndex = GetAtlasSpriteIndex(currentSprite);
    var popupIndex = allowDefault ? selectedIndex + 1 : Mathf.Max(0, selectedIndex);
    var labels = allowDefault ? availableAtlasSpriteLabelsWithDefault : availableAtlasSpriteLabels;
    var controlName = GetTexturePopupControlName(slotName);
    GUI.SetNextControlName(controlName);
    var popupRect = EditorGUILayout.GetControlRect();
    var newPopupIndex = EditorGUI.Popup(popupRect, label, popupIndex, labels);

    if (Event.current.type == EventType.MouseDown && popupRect.Contains(Event.current.mousePosition)) {
      activeTextureSlotName = slotName;
      GUI.FocusControl(controlName);
    }

    if (GUI.GetNameOfFocusedControl() == controlName || newPopupIndex != popupIndex) {
      activeTextureSlotName = slotName;
    }

    if (allowDefault) {
      return newPopupIndex <= 0 ? null : availableAtlasSprites[newPopupIndex - 1];
    }

    return availableAtlasSprites[Mathf.Clamp(newPopupIndex, 0, availableAtlasSprites.Length - 1)];
  }

  bool TryStepTextureSlotSelection(string slotName, int direction) {
    EnsureAtlasSpriteOptions();
    if (availableAtlasSprites.Length == 0) return false;
    if (slotName == "MainMask" && sourceMode != PreviewSourceMode.AtlasSprite) return false;

    var allowDefault = AllowsProceduralDefaultInternal(slotName);
    var optionCount = availableAtlasSprites.Length + (allowDefault ? 1 : 0);
    if (optionCount <= 0) return false;

    var currentPopupIndex = GetTextureSlotPopupIndex(slotName, allowDefault);
    var nextPopupIndex = WrapIndex(currentPopupIndex + direction, optionCount);
    var nextSprite = GetSpriteFromPopupIndex(nextPopupIndex, allowDefault);
    SetTextureSlotSpriteInternal(slotName, nextSprite);

    Debug.Log($"[{nameof(AllIn1EffectPreviewWindow)}] {GetTextureSlotDisplayNameInternal(slotName)} keyboard selection -> {GetTextureSlotSelectionLabel(slotName)}");
    return true;
  }

  bool AllowsProceduralDefaultInternal(string slotName) {
    if (slotName == "MainMask") return false;
    return activeDrawer == null || activeDrawer.AllowsProceduralDefault(slotName);
  }

  int GetTextureSlotPopupIndex(string slotName, bool allowDefault) {
    var selectedIndex = GetAtlasSpriteIndex(GetTextureSlotSpriteInternal(slotName));
    return allowDefault ? selectedIndex + 1 : Mathf.Max(0, selectedIndex);
  }

  Sprite GetSpriteFromPopupIndex(int popupIndex, bool allowDefault) {
    if (allowDefault && popupIndex <= 0) return null;
    var spriteIndex = allowDefault ? popupIndex - 1 : popupIndex;
    return availableAtlasSprites[Mathf.Clamp(spriteIndex, 0, availableAtlasSprites.Length - 1)];
  }

  Sprite GetTextureSlotSpriteInternal(string slotName) {
    if (slotName == "MainMask") return mainMaskSprite;
    return activeDrawer != null ? activeDrawer.GetTextureSlotSprite(slotName) : null;
  }

  void SetTextureSlotSpriteInternal(string slotName, Sprite sprite) {
    if (slotName == "MainMask") {
      mainMaskSprite = sprite;
    } else if (activeDrawer != null) {
      activeDrawer.SetTextureSlotSprite(slotName, sprite);
    }
  }

  string GetTextureSlotDisplayNameInternal(string slotName) {
    if (slotName == "MainMask") return "Main Mask Sprite";
    return activeDrawer != null ? activeDrawer.GetTextureSlotDisplayName(slotName) : "Texture Selector";
  }

  string GetTextureSlotSelectionLabel(string slotName) {
    var sprite = GetTextureSlotSpriteInternal(slotName);
    return sprite != null ? sprite.name : "Procedural Default";
  }

  string GetTexturePopupControlName(string slotName) {
    return $"TexturePopup_{slotName}";
  }

  int WrapIndex(int value, int count) {
    if (count <= 0) return 0;
    var wrapped = value % count;
    return wrapped < 0 ? wrapped + count : wrapped;
  }

  void HandleEditorUpdate() {
    if (!this) return;
    if (!animatePreview) {
      lastEditorTime = EditorApplication.timeSinceStartup;
      return;
    }

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

  void EnsurePreviewAssets() {
    if (emptyTexture == null) {
      emptyTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(PreviewTextureAssetPath);
    }

    if (solidPreviewMask == null) {
      solidPreviewMask = BuildSolidPreviewMask();
    }

    EnsureAtlasSpriteOptions();
    EnsurePreviewMaterial();

    if (loggedMissingAssets) return;
    if (emptyTexture == null || previewMaterial == null) {
      Debug.LogWarning(
        $"[{nameof(AllIn1EffectPreviewWindow)}] Preview setup incomplete. " +
        $"source='{PreviewTextureAssetPath}' shader='{GetActiveShaderName()}'.");
      loggedMissingAssets = true;
    }
  }

  void EnsurePreviewMaterial() {
    if (previewMaterialAsset != null) {
      if (previewMaterial != null && previewMaterialSourceAsset == previewMaterialAsset && previewMaterialSourceShader == null) return;
      RebuildPreviewMaterialFromAsset(previewMaterialAsset);
      return;
    }

    var shader = Shader.Find(GetActiveShaderName());
    if (shader == null) return;
    if (previewMaterial != null && previewMaterialSourceAsset == null && previewMaterialSourceShader == shader) return;

    RebuildPreviewMaterialFromShader(shader);
  }

  string GetActiveShaderName() {
    return activeDrawer != null && !string.IsNullOrWhiteSpace(activeDrawer.ShaderName)
      ? activeDrawer.ShaderName
      : DefaultShaderName;
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
      name = $"{activeEffect}Preview_Material",
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
      name = "FormEffectPreview_Mask",
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Clamp,
      hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
    };
    texture.SetPixel(0, 0, Color.white);
    texture.Apply(false, true);
    return texture;
  }

  void DestroyPreviewAssets() {
    DestroyPreviewMaterialInstance();

    if (solidPreviewMask != null) {
      DestroyImmediate(solidPreviewMask);
      solidPreviewMask = null;
    }

    mainMaskTextureCache.Clear();
    normalMaskTextureCache.Clear();
    specularMaskTextureCache.Clear();
  }
}
#endif
