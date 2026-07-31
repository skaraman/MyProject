#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[System.Serializable]
public sealed class BoltEffectPreviewDrawer : IEffectPreviewDrawer {
  [SerializeField] float charge = 0.72f;
  [SerializeField] float reach = 0.62f;
  [SerializeField] float thickness = 0.42f;
  [SerializeField] float activity = 0.65f;
  [SerializeField] float randomness = 0.78f;
  [SerializeField] float branching = 0.58f;
  [SerializeField] float spriteVisibility = 0.88f;
  [SerializeField] float brightness = 0.78f;

  [SerializeField] Color coreColor = new(0.84f, 1f, 0.38f, 1f);
  [SerializeField] Color electricityColor = new(0.22f, 1f, 0.015f, 1f);

  public string DisplayName => "Bolt Preview";
  public string ShaderName => "Hidden/Esperanza/BoltPreview";
  public string Description =>
    "Lime-green electricity gathers around the visible source sprite, then fires animated lightning arcs from its center in changing directions with randomized bends and branches.";
  public Vector4 PreviewPadding => new(0.5f, 0.5f, 0.5f, 0.5f);

  public void OnEnable(AllIn1EffectPreviewWindow window) {
  }

  public void OnDisable() {
  }

  public void DrawControls(AllIn1EffectPreviewWindow window) {
    EditorGUILayout.LabelField("Electricity From Sprite", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "These controls coordinate the number, reach, shape, motion, and glow of the lightning without exposing individual shader variables.",
      MessageType.None);

    charge = DrawFriendlySlider(
      "Charge",
      charge,
      "Moves from occasional sparks to a heavily energized sprite with more simultaneous bolts.");
    reach = DrawFriendlySlider(
      "Reach",
      reach,
      "Controls how far the lightning travels beyond the sprite.");
    thickness = DrawFriendlySlider(
      "Bolt Thickness",
      thickness,
      "Changes the bright core and surrounding glow width together.");
    activity = DrawFriendlySlider(
      "Activity",
      activity,
      "Only controls how quickly bolts appear, fade, and regenerate.");
    randomness = DrawFriendlySlider(
      "Bolt Randomness",
      randomness,
      "Varies each new bolt's direction, length, thickness, curvature, stagger, and branch shape.");
    branching = DrawFriendlySlider(
      "Branching",
      branching,
      "Adds smaller arcs that split away from the main lightning paths.");
    spriteVisibility = DrawFriendlySlider(
      "Sprite Visibility",
      spriteVisibility,
      "Keeps the original sprite readable beneath its electrical charge.");
    brightness = DrawFriendlySlider(
      "Brightness",
      brightness,
      "Moves from a soft green discharge to brilliant lime electricity.");

    EditorGUILayout.Space(6f);
    EditorGUILayout.LabelField("Electricity Colors", EditorStyles.boldLabel);
    coreColor = EditorGUILayout.ColorField("Electric Core", coreColor);
    electricityColor = EditorGUILayout.ColorField("Lime Glow", electricityColor);
  }

  public void ApplyMaterialState(Material previewMaterial) {
    if (previewMaterial == null) return;

    var chargeCurve = Mathf.SmoothStep(0f, 1f, charge);
    var reachCurve = Mathf.SmoothStep(0f, 1f, reach);
    var thicknessCurve = Mathf.SmoothStep(0f, 1f, thickness);
    var activityCurve = Mathf.SmoothStep(0f, 1f, activity);
    var randomnessCurve = Mathf.SmoothStep(0f, 1f, randomness);
    var branchingCurve = Mathf.SmoothStep(0f, 1f, branching);
    var brightnessCurve = Mathf.SmoothStep(0f, 1f, brightness);

    SetFloatIfPresent(previewMaterial, "_Charge", Mathf.Lerp(0.28f, 1f, chargeCurve));
    SetFloatIfPresent(previewMaterial, "_BoltCount", Mathf.Lerp(3f, 8f, chargeCurve));
    SetFloatIfPresent(previewMaterial, "_Reach", reachCurve);
    SetFloatIfPresent(previewMaterial, "_BoltWidth", Mathf.Lerp(0.0025f, 0.014f, thicknessCurve));
    SetFloatIfPresent(previewMaterial, "_Activity", Mathf.Lerp(0.24f, 2.4f, activityCurve));
    SetFloatIfPresent(previewMaterial, "_Randomness", randomnessCurve);
    SetFloatIfPresent(previewMaterial, "_Branching", branchingCurve);
    SetFloatIfPresent(previewMaterial, "_SurfaceOpacity", Mathf.Lerp(0.4f, 0.055f, spriteVisibility));
    SetFloatIfPresent(previewMaterial, "_BoltOpacity", Mathf.Lerp(0.7f, 1f, brightnessCurve));
    SetFloatIfPresent(previewMaterial, "_Glow", Mathf.Lerp(0.8f, 2.8f, brightnessCurve));
    SetColorIfPresent(previewMaterial, "_CoreColor", coreColor);
    SetColorIfPresent(previewMaterial, "_BoltColor", electricityColor);
  }

  public void ResetDefaults() {
    charge = 0.72f;
    reach = 0.62f;
    thickness = 0.42f;
    activity = 0.65f;
    randomness = 0.78f;
    branching = 0.58f;
    spriteVisibility = 0.88f;
    brightness = 0.78f;
    coreColor = new Color(0.84f, 1f, 0.38f, 1f);
    electricityColor = new Color(0.22f, 1f, 0.015f, 1f);
    Debug.Log($"[{nameof(BoltEffectPreviewDrawer)}] Reset sprite-electricity defaults");
  }

  public void NormalizeState() {
    charge = Mathf.Clamp01(charge);
    reach = Mathf.Clamp01(reach);
    thickness = Mathf.Clamp01(thickness);
    activity = Mathf.Clamp01(activity);
    randomness = Mathf.Clamp01(randomness);
    branching = Mathf.Clamp01(branching);
    spriteVisibility = Mathf.Clamp01(spriteVisibility);
    brightness = Mathf.Clamp01(brightness);
  }

  public void CopySettingsFrom(IEffectPreviewDrawer source) {
    if (source is not BoltEffectPreviewDrawer values) return;

    charge = values.charge;
    reach = values.reach;
    thickness = values.thickness;
    activity = values.activity;
    randomness = values.randomness;
    branching = values.branching;
    spriteVisibility = values.spriteVisibility;
    brightness = values.brightness;
    coreColor = values.coreColor;
    electricityColor = values.electricityColor;
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
