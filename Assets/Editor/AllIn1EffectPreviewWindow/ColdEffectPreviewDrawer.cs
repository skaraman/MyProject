#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[System.Serializable]
public sealed class ColdEffectPreviewDrawer : IEffectPreviewDrawer {
  [SerializeField] float freeze = 0.72f;
  [SerializeField] float icicleSize = 0.58f;
  [SerializeField] float freezeCycle = 0.48f;
  [SerializeField] float frostDetail = 0.66f;
  [SerializeField] float snowfall = 0.5f;
  [SerializeField] float spriteVisibility = 0.88f;
  [SerializeField] float iceShine = 0.72f;

  [SerializeField] Color iceColor = new(0.13f, 0.58f, 0.95f, 1f);
  [SerializeField] Color frozenHighlight = new(0.82f, 0.97f, 1f, 1f);

  public string DisplayName => "Cold Preview";
  public string ShaderName => "Hidden/Esperanza/ColdPreview";
  public string Description =>
    "Translucent frost forms and recedes across the visible sprite while sharp icicles grow from its lower silhouette. Small snowflakes drift around the outside.";
  public Vector4 PreviewPadding => new(0.34f, 0.34f, 0.3f, 0.48f);

  public void OnEnable(AllIn1EffectPreviewWindow window) {
  }

  public void OnDisable() {
  }

  public void DrawControls(AllIn1EffectPreviewWindow window) {
    EditorGUILayout.LabelField("Ice On Sprite", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "Each control coordinates the frost layer, forming icicles, and surrounding snow into one readable cold effect.",
      MessageType.None);

    freeze = DrawFriendlySlider(
      "Freeze",
      freeze,
      "Controls how much frost can spread across the sprite and how strongly it freezes.");
    icicleSize = DrawFriendlySlider(
      "Icicle Size",
      icicleSize,
      "Sets the overall size range while each icicle receives its own randomized base width and length.");
    freezeCycle = DrawFriendlySlider(
      "Form & Melt",
      freezeCycle,
      "Controls how quickly the ice forms and then recedes.");
    frostDetail = DrawFriendlySlider(
      "Frost Detail",
      frostDetail,
      "Moves from broad soft ice to smaller branching crystal patterns.");
    snowfall = DrawFriendlySlider(
      "Snowflakes",
      snowfall,
      "Controls how many small flakes drift around the sprite.");
    spriteVisibility = DrawFriendlySlider(
      "Sprite Visibility",
      spriteVisibility,
      "Keeps the original sprite readable beneath the translucent ice.");
    iceShine = DrawFriendlySlider(
      "Ice Shine",
      iceShine,
      "Changes the brightness of frozen edges, crystals, and icicle highlights.");

    EditorGUILayout.Space(6f);
    EditorGUILayout.LabelField("Ice Colors", EditorStyles.boldLabel);
    iceColor = EditorGUILayout.ColorField("Ice Color", iceColor);
    frozenHighlight = EditorGUILayout.ColorField("Frozen Highlight", frozenHighlight);
  }

  public void ApplyMaterialState(Material previewMaterial) {
    if (previewMaterial == null) return;

    var freezeCurve = Mathf.SmoothStep(0f, 1f, freeze);
    var icicleCurve = Mathf.SmoothStep(0f, 1f, icicleSize);
    var cycleCurve = Mathf.SmoothStep(0f, 1f, freezeCycle);
    var detailCurve = Mathf.SmoothStep(0f, 1f, frostDetail);
    var snowfallCurve = Mathf.SmoothStep(0f, 1f, snowfall);
    var shineCurve = Mathf.SmoothStep(0f, 1f, iceShine);

    SetFloatIfPresent(previewMaterial, "_Freeze", Mathf.Lerp(0.28f, 1f, freezeCurve));
    SetFloatIfPresent(previewMaterial, "_IcicleLength", Mathf.Lerp(0.07f, 0.38f, icicleCurve));
    SetFloatIfPresent(previewMaterial, "_IcicleWidth", Mathf.Lerp(0.022f, 0.07f, icicleCurve));
    SetFloatIfPresent(previewMaterial, "_IcicleCount", Mathf.Lerp(13f, 7f, icicleCurve));
    SetFloatIfPresent(previewMaterial, "_CycleSpeed", Mathf.Lerp(0.16f, 1.45f, cycleCurve));
    SetFloatIfPresent(previewMaterial, "_FrostScale", Mathf.Lerp(2.6f, 8.2f, detailCurve));
    SetFloatIfPresent(previewMaterial, "_CrystalDetail", detailCurve);
    SetFloatIfPresent(previewMaterial, "_SnowAmount", snowfallCurve);
    SetFloatIfPresent(previewMaterial, "_SurfaceOpacity", Mathf.Lerp(0.5f, 0.08f, spriteVisibility));
    SetFloatIfPresent(previewMaterial, "_IceOpacity", Mathf.Lerp(0.62f, 0.94f, freezeCurve));
    SetFloatIfPresent(previewMaterial, "_Specular", Mathf.Lerp(0.35f, 1.7f, shineCurve));
    SetFloatIfPresent(previewMaterial, "_Brightness", Mathf.Lerp(0.78f, 1.32f, shineCurve));
    SetColorIfPresent(previewMaterial, "_IceColor", iceColor);
    SetColorIfPresent(previewMaterial, "_HighlightColor", frozenHighlight);
  }

  public void ResetDefaults() {
    freeze = 0.72f;
    icicleSize = 0.58f;
    freezeCycle = 0.48f;
    frostDetail = 0.66f;
    snowfall = 0.5f;
    spriteVisibility = 0.88f;
    iceShine = 0.72f;
    iceColor = new Color(0.13f, 0.58f, 0.95f, 1f);
    frozenHighlight = new Color(0.82f, 0.97f, 1f, 1f);
    Debug.Log($"[{nameof(ColdEffectPreviewDrawer)}] Reset forming-ice defaults");
  }

  public void NormalizeState() {
    freeze = Mathf.Clamp01(freeze);
    icicleSize = Mathf.Clamp01(icicleSize);
    freezeCycle = Mathf.Clamp01(freezeCycle);
    frostDetail = Mathf.Clamp01(frostDetail);
    snowfall = Mathf.Clamp01(snowfall);
    spriteVisibility = Mathf.Clamp01(spriteVisibility);
    iceShine = Mathf.Clamp01(iceShine);
  }

  public void CopySettingsFrom(IEffectPreviewDrawer source) {
    if (source is not ColdEffectPreviewDrawer values) return;

    freeze = values.freeze;
    icicleSize = values.icicleSize;
    freezeCycle = values.freezeCycle;
    frostDetail = values.frostDetail;
    snowfall = values.snowfall;
    spriteVisibility = values.spriteVisibility;
    iceShine = values.iceShine;
    iceColor = values.iceColor;
    frozenHighlight = values.frozenHighlight;
    NormalizeState();
  }

  public Sprite GetTextureSlotSprite(string slotName) {
    return null;
  }

  public void SetTextureSlotSprite(string slotName, Sprite sprite) {
  }

  public bool AllowsProceduralDefault(string slotName) {
    return false;
  }

  public string GetTextureSlotDisplayName(string slotName) {
    return "Texture Selector";
  }

  static float DrawFriendlySlider(string label, float value, string tooltip) {
    return EditorGUILayout.Slider(new GUIContent(label, tooltip), value, 0f, 1f);
  }

  static void SetFloatIfPresent(Material material, string propertyName, float value) {
    if (material.HasProperty(propertyName)) {
      material.SetFloat(propertyName, value);
    }
  }

  static void SetColorIfPresent(Material material, string propertyName, Color value) {
    if (material.HasProperty(propertyName)) {
      material.SetColor(propertyName, value);
    }
  }
}
#endif
