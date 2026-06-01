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

  [SerializeReference] IEffectPreviewDrawer activeDrawer;

  [SerializeField] PreviewSourceMode sourceMode = PreviewSourceMode.SolidMask;
  [SerializeField] bool animatePreview = true;
  [SerializeField] float animationSpeed = 1f;
  [SerializeField] float previewScale = 0.88f;
  [SerializeField] Material previewMaterialAsset;
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
    if (activeDrawer == null) {
      activeDrawer = new AllIn1EffectPreviewDrawer();
    }
    activeDrawer.OnEnable(this);
    EnsurePreviewAssets();
    EditorApplication.update -= HandleEditorUpdate;
    EditorApplication.update += HandleEditorUpdate;
    ResetEditorClock();
  }

  void OnDisable() {
    EditorApplication.update -= HandleEditorUpdate;
    if (activeDrawer != null) {
      activeDrawer.OnDisable();
    }
    DestroyPreviewAssets();
  }

  void OnDestroy() {
    EditorApplication.update -= HandleEditorUpdate;
    if (activeDrawer != null) {
      activeDrawer.OnDisable();
    }
    DestroyPreviewAssets();
  }

  void OnGUI() {
    EnsurePreviewAssets();
    HandleTextureDropdownKeyboard();
    if (activeDrawer != null) {
      activeDrawer.NormalizeState();
    }

    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

    var drawerDisplayName = activeDrawer != null ? activeDrawer.DisplayName : "Shader Preview";
    EditorGUILayout.LabelField($"Custom {drawerDisplayName}", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      activeDrawer != null && activeDrawer is AllIn1EffectPreviewDrawer ?
      "This preview uses the dedicated fire shader with an edge-lit flame body. The main mask defines the silhouette, the breakup texture shapes erosion and sparks, and the flow texture keeps the upward motion and tongue shaping." :
      "Custom shader preview window.",
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
      mainMaskSprite = DrawAtlasSpritePopup("Main Mask Sprite", mainMaskSprite, false, "MainMask");

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

  void DrawActionButtons() {
    using (new EditorGUILayout.HorizontalScope()) {
      if (GUILayout.Button("Reset Defaults")) {
        if (activeDrawer != null) {
          activeDrawer.ResetDefaults();
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
        EditorGUILayout.HelpBox($"Preview material could not be created. Missing shader '{ShaderName}'.", MessageType.Error);
        return;
      }

      var layoutRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.MinHeight(320f), GUILayout.ExpandWidth(true));
      var previewRect = AllIn1EffectPreviewWindowUtils.GetScaledPreviewRect(layoutRect, previewScale, previewTexture);
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
    AllIn1EffectPreviewWindowUtils.DrawCheckerboard(innerRect, 18f, previewBackgroundColor, previewAccentColor);
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
      case PreviewSourceMode.AtlasSprite:
        return mainMaskTextureCache.GetTexture(mainMaskSprite) ?? solidPreviewMask;
      case PreviewSourceMode.EmptySprite:
        return emptyTexture;
      default:
        return solidPreviewMask;
    }
  }

  void ApplyPreviewMaterialState(Texture sourceTexture, float timeValue) {
    EnsurePreviewMaterial();
    if (previewMaterial == null || sourceTexture == null) return;

    previewMaterial.mainTexture = sourceTexture;
    SetTextureIfPresent("_MainTex", sourceTexture);

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

  void DestroyPreviewAssets() {
    DestroyPreviewMaterialInstance();

    if (solidPreviewMask != null) {
      DestroyImmediate(solidPreviewMask);
      solidPreviewMask = null;
    }

    mainMaskTextureCache.Clear();
  }
}
#endif
