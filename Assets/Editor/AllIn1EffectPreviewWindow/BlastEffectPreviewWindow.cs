#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class BlastEffectPreviewWindow : EditorWindow {
  const string MenuPath = "Tools/Shader Preview/Other Effects/Blast Preview";
  const string ShaderName = "Esperanza/Effects/BlastEnergy";
  const string MaterialPath = "Assets/Materials/Gameplay/BlastEnergy.mat";
  const string BlastBallTexturePath = "Assets/Sprites/Effects/Ball/Ball.png";
  const float AnimationDuration = 1.5f;

  [SerializeField] bool animate = true;
  [SerializeField] float animationSpeed = 1f;
  [SerializeField] int selectedFrame = 1;
  [SerializeField] Color primaryColor = Color.white;
  [SerializeField] Color secondaryColor = new(0.05f, 0.35f, 1f, 1f);
  [SerializeField] float swirlSpeed = 2.4f;
  [SerializeField] float swirl = 6f;
  [SerializeField] float bands = 5f;
  [SerializeField] float gleamWidth = 0.035f;
  [SerializeField] float intensity = 1.45f;
  [SerializeField] float normalStrength = 1f;
  [SerializeField] float lightInfluence = 0.75f;

  readonly List<Sprite> blastSprites = new();
  readonly SpriteTextureCache spriteTextureCache = new("Blast");

  Material previewMaterial;
  Vector2 scrollPosition;
  double lastEditorTime;
  float previewTime;

  [MenuItem(MenuPath)]
  static void ShowWindow() {
    var window = GetWindow<BlastEffectPreviewWindow>("Blast Preview");
    window.minSize = new Vector2(620f, 720f);
    window.Show();
  }

  void OnEnable() {
    LoadPreviewSprites();
    LoadMaterialSettings();
    CreatePreviewMaterial();
    lastEditorTime = EditorApplication.timeSinceStartup;
    EditorApplication.update -= HandleEditorUpdate;
    EditorApplication.update += HandleEditorUpdate;
  }

  void OnDisable() {
    EditorApplication.update -= HandleEditorUpdate;
    spriteTextureCache.Clear();
    DestroyPreviewMaterial();
  }

  void OnGUI() {
    EnsureAssets();
    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
    EditorGUILayout.LabelField("Blast Gleaming Energy", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "The BlastBall PNG seeds a projected 3D vortex. Tilted energy strands " +
      "dim behind the volume, brighten in front, and carry moving light knots.",
      MessageType.Info);

    DrawPlaybackControls();
    EditorGUILayout.Space(8f);
    DrawPreview();
    EditorGUILayout.Space(8f);
    DrawShaderControls();
    EditorGUILayout.EndScrollView();
  }

  void DrawPlaybackControls() {
    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
      EditorGUILayout.LabelField("Shader Animation", EditorStyles.boldLabel);
      animate = EditorGUILayout.Toggle("Animate", animate);
      animationSpeed = EditorGUILayout.Slider("Animation Speed", animationSpeed, 0f, 3f);
    }
  }

  void DrawPreview() {
    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
      EditorGUILayout.LabelField("Effect Sprite", EditorStyles.boldLabel);
      var sprite = GetCurrentSprite();
      var texture = spriteTextureCache.GetTexture(sprite);
      if (texture == null || previewMaterial == null) {
        EditorGUILayout.HelpBox("Blast sprites or shader could not be loaded.", MessageType.Error);
        return;
      }

      var layoutRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.MinHeight(420f));
      AllIn1EffectPreviewWindowUtils.DrawCheckerboard(
        layoutRect,
        18f,
        new Color(0.08f, 0.09f, 0.12f, 1f),
        new Color(0.13f, 0.15f, 0.2f, 1f));

      ApplyPreviewMaterial(texture);
      if (Event.current.type != EventType.Repaint) return;

      var previewPass = previewMaterial.FindPass("Blast Forward");
      if (previewPass < 0) {
        previewPass = 0;
      }

      Graphics.DrawTexture(
        layoutRect,
        texture,
        new Rect(0f, 0f, 1f, 1f),
        0,
        0,
        0,
        0,
        Color.white,
        previewMaterial,
        previewPass);
    }
  }

  void DrawShaderControls() {
    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
      EditorGUILayout.LabelField("Energy", EditorStyles.boldLabel);
      primaryColor = EditorGUILayout.ColorField("Primary", primaryColor);
      secondaryColor = EditorGUILayout.ColorField("Secondary", secondaryColor);
      swirlSpeed = EditorGUILayout.Slider("Swirl Speed", swirlSpeed, 0f, 8f);
      swirl = EditorGUILayout.Slider("Orbit Wobble", swirl, 0f, 12f);
      bands = EditorGUILayout.Slider("Light Knots", bands, 1f, 12f);
      gleamWidth = EditorGUILayout.Slider("Strand Width", gleamWidth, 0.005f, 0.12f);
      intensity = EditorGUILayout.Slider("Intensity", intensity, 0f, 4f);

      EditorGUILayout.Space(6f);
      EditorGUILayout.LabelField("2D Lighting", EditorStyles.boldLabel);
      normalStrength = EditorGUILayout.Slider("Normal Strength", normalStrength, 0f, 4f);
      lightInfluence = EditorGUILayout.Slider("Light Influence", lightInfluence, 0f, 1f);

      if (GUILayout.Button("Save Settings To Blast Material")) {
        SaveMaterialSettings();
      }
    }
  }

  void HandleEditorUpdate() {
    if (!this) return;

    var now = EditorApplication.timeSinceStartup;
    var deltaTime = Mathf.Clamp((float)(now - lastEditorTime), 0f, 0.05f);
    lastEditorTime = now;
    if (!animate) return;

    previewTime += deltaTime * animationSpeed;
    selectedFrame = GetAnimatedFrameNumber();
    Repaint();
  }

  void EnsureAssets() {
    if (blastSprites.Count == 0) {
      LoadPreviewSprites();
    }
    if (previewMaterial == null) {
      CreatePreviewMaterial();
    }
  }

  void LoadPreviewSprites() {
    blastSprites.Clear();
    var assets = AssetDatabase.LoadAllAssetsAtPath(BlastBallTexturePath);

    foreach (var asset in assets) {
      if (asset is not Sprite sprite) continue;
      blastSprites.Add(sprite);
    }

    selectedFrame = Mathf.Clamp(selectedFrame, 1, GetAvailableFrameCount());
    spriteTextureCache.Clear();
  }

  void CreatePreviewMaterial() {
    DestroyPreviewMaterial();
    var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
    if (sourceMaterial != null) {
      previewMaterial = new Material(sourceMaterial);
    }
    else {
      var shader = Shader.Find(ShaderName);
      if (shader == null) return;
      previewMaterial = new Material(shader);
    }

    previewMaterial.name = "BlastEnergy_Preview";
    previewMaterial.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
  }

  void LoadMaterialSettings() {
    var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
    if (material == null) return;

    primaryColor = material.GetColor("_PrimaryColor");
    secondaryColor = material.GetColor("_SecondaryColor");
    swirlSpeed = material.GetFloat("_Speed");
    swirl = material.GetFloat("_Swirl");
    bands = material.GetFloat("_Bands");
    gleamWidth = material.GetFloat("_GleamWidth");
    intensity = material.GetFloat("_Intensity");
    normalStrength = material.GetFloat("_NormalStrength");
    lightInfluence = material.GetFloat("_LightInfluence");
  }

  void DestroyPreviewMaterial() {
    if (previewMaterial == null) return;
    DestroyImmediate(previewMaterial);
    previewMaterial = null;
  }

  void ApplyPreviewMaterial(Texture texture) {
    previewMaterial.mainTexture = texture;
    previewMaterial.SetVector("_SpriteUvRect", new Vector4(0f, 0f, 1f, 1f));
    previewMaterial.SetFloat("_SpriteEffectActive", 1f);
    previewMaterial.SetFloat("_PreviewTime", previewTime);
    previewMaterial.SetFloat("_UsePreviewTime", 1f);
    ApplyShaderSettings(previewMaterial);
  }

  void SaveMaterialSettings() {
    var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
    if (material == null) return;

    Undo.RecordObject(material, "Save Blast Energy Settings");
    ApplyShaderSettings(material);
    EditorUtility.SetDirty(material);
    AssetDatabase.SaveAssets();
  }

  void ApplyShaderSettings(Material material) {
    material.SetColor("_PrimaryColor", primaryColor);
    material.SetColor("_SecondaryColor", secondaryColor);
    material.SetFloat("_Speed", swirlSpeed);
    material.SetFloat("_Swirl", swirl);
    material.SetFloat("_Bands", bands);
    material.SetFloat("_GleamWidth", gleamWidth);
    material.SetFloat("_Intensity", intensity);
    material.SetFloat("_NormalStrength", normalStrength);
    material.SetFloat("_LightInfluence", lightInfluence);
  }

  Sprite GetCurrentSprite() {
    if (blastSprites.Count == 0) return null;
    var index = Mathf.Clamp(GetCurrentFrameNumber() - 1, 0, blastSprites.Count - 1);
    return blastSprites[index];
  }

  int GetCurrentFrameNumber() {
    return Mathf.Clamp(selectedFrame, 1, GetAvailableFrameCount());
  }

  int GetAnimatedFrameNumber() {
    var frameCount = GetAvailableFrameCount();
    var normalizedTime = Mathf.Repeat(previewTime, AnimationDuration) / AnimationDuration;
    var frameIndex = Mathf.FloorToInt(normalizedTime * frameCount);
    return Mathf.Clamp(frameIndex + 1, 1, frameCount);
  }

  int GetAvailableFrameCount() {
    return Mathf.Max(1, blastSprites.Count);
  }
}
#endif
