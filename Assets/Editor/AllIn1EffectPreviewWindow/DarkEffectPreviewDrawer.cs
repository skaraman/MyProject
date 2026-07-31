#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[System.Serializable]
public sealed class DarkEffectPreviewDrawer : IEffectPreviewDrawer {
  [SerializeField] float presence = 0.74f;
  [SerializeField] float edgeAura = 0.68f;
  [SerializeField] float tendrilReach = 0.64f;
  [SerializeField] float restlessness = 0.48f;
  [SerializeField] float veins = 0.66f;
  [SerializeField] float spriteVisibility = 0.82f;
  [SerializeField] float voidDepth = 0.78f;

  [SerializeField] Color darkPurple = new(0.22f, 0.015f, 0.36f, 1f);
  [SerializeField] Color abyssColor = new(0.004f, 0f, 0.012f, 1f);

  public string DisplayName => "Dark Preview";
  public string ShaderName => "Hidden/Esperanza/DarkPreview";
  public string Description =>
    "A dark purple and black aura clings to the visible sprite, curling tendrils move around its silhouette, and branching void veins pulse beneath the surface.";
  public Vector4 PreviewPadding => new(0.48f, 0.48f, 0.46f, 0.46f);

  public void OnEnable(AllIn1EffectPreviewWindow window) {
  }

  public void OnDisable() {
  }

  public void DrawControls(AllIn1EffectPreviewWindow window) {
    EditorGUILayout.LabelField("Darkness On Sprite", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "These controls shape the silhouette aura, moving tendrils, and inner veins together without exposing individual shader variables.",
      MessageType.None);

    presence = DrawFriendlySlider(
      "Dark Presence",
      presence,
      "Moves from a faint corruption to a dense aura with more simultaneous tendrils.");
    edgeAura = DrawFriendlySlider(
      "Edge Aura",
      edgeAura,
      "Changes the width and strength of the purple-black outline around the sprite.");
    tendrilReach = DrawFriendlySlider(
      "Tendril Reach",
      tendrilReach,
      "Controls how far the curling tendrils travel beyond the silhouette.");
    restlessness = DrawFriendlySlider(
      "Restlessness",
      restlessness,
      "Controls how quickly the aura crawls, curls, and pulses.");
    veins = DrawFriendlySlider(
      "Dark Veins",
      veins,
      "Changes the amount and detail of branching veins inside the sprite.");
    spriteVisibility = DrawFriendlySlider(
      "Sprite Visibility",
      spriteVisibility,
      "Keeps the original sprite readable beneath the dark surface effect.");
    voidDepth = DrawFriendlySlider(
      "Void Depth",
      voidDepth,
      "Moves from translucent purple shadow to deep black corruption.");

    EditorGUILayout.Space(6f);
    EditorGUILayout.LabelField("Dark Colors", EditorStyles.boldLabel);
    darkPurple = EditorGUILayout.ColorField("Dark Purple", darkPurple);
    abyssColor = EditorGUILayout.ColorField("Black Highlight", abyssColor);
  }

  public void ApplyMaterialState(Material previewMaterial) {
    if (previewMaterial == null) return;

    var presenceCurve = Mathf.SmoothStep(0f, 1f, presence);
    var edgeCurve = Mathf.SmoothStep(0f, 1f, edgeAura);
    var reachCurve = Mathf.SmoothStep(0f, 1f, tendrilReach);
    var movementCurve = Mathf.SmoothStep(0f, 1f, restlessness);
    var veinCurve = Mathf.SmoothStep(0f, 1f, veins);
    var depthCurve = Mathf.SmoothStep(0f, 1f, voidDepth);

    SetFloatIfPresent(previewMaterial, "_Presence", Mathf.Lerp(0.24f, 1f, presenceCurve));
    SetFloatIfPresent(previewMaterial, "_TendrilCount", Mathf.Lerp(3f, 8f, presenceCurve));
    SetFloatIfPresent(previewMaterial, "_EdgeWidth", Mathf.Lerp(0.005f, 0.05f, edgeCurve));
    SetFloatIfPresent(previewMaterial, "_EdgeOpacity", Mathf.Lerp(0.32f, 0.95f, edgeCurve));
    SetFloatIfPresent(previewMaterial, "_TendrilReach", Mathf.Lerp(0.12f, 0.68f, reachCurve));
    SetFloatIfPresent(previewMaterial, "_TendrilWidth", Mathf.Lerp(0.006f, 0.022f, presenceCurve));
    SetFloatIfPresent(previewMaterial, "_Movement", Mathf.Lerp(0.12f, 1.45f, movementCurve));
    SetFloatIfPresent(previewMaterial, "_VeinAmount", Mathf.Lerp(0.08f, 1f, veinCurve));
    SetFloatIfPresent(previewMaterial, "_VeinScale", Mathf.Lerp(3.2f, 9.4f, veinCurve));
    SetFloatIfPresent(previewMaterial, "_SurfaceOpacity", Mathf.Lerp(0.42f, 0.055f, spriteVisibility));
    SetFloatIfPresent(previewMaterial, "_DarkOpacity", Mathf.Lerp(0.52f, 0.96f, depthCurve));
    SetFloatIfPresent(previewMaterial, "_Glow", Mathf.Lerp(0.68f, 1.45f, depthCurve));
    SetColorIfPresent(previewMaterial, "_PurpleColor", darkPurple);
    SetColorIfPresent(previewMaterial, "_AbyssColor", abyssColor);
  }

  public void ResetDefaults() {
    presence = 0.74f;
    edgeAura = 0.68f;
    tendrilReach = 0.64f;
    restlessness = 0.48f;
    veins = 0.66f;
    spriteVisibility = 0.82f;
    voidDepth = 0.78f;
    darkPurple = new Color(0.22f, 0.015f, 0.36f, 1f);
    abyssColor = new Color(0.004f, 0f, 0.012f, 1f);
    Debug.Log($"[{nameof(DarkEffectPreviewDrawer)}] Reset dark-aura defaults");
  }

  public void NormalizeState() {
    presence = Mathf.Clamp01(presence);
    edgeAura = Mathf.Clamp01(edgeAura);
    tendrilReach = Mathf.Clamp01(tendrilReach);
    restlessness = Mathf.Clamp01(restlessness);
    veins = Mathf.Clamp01(veins);
    spriteVisibility = Mathf.Clamp01(spriteVisibility);
    voidDepth = Mathf.Clamp01(voidDepth);
  }

  public void CopySettingsFrom(IEffectPreviewDrawer source) {
    if (source is not DarkEffectPreviewDrawer values) return;

    presence = values.presence;
    edgeAura = values.edgeAura;
    tendrilReach = values.tendrilReach;
    restlessness = values.restlessness;
    veins = values.veins;
    spriteVisibility = values.spriteVisibility;
    voidDepth = values.voidDepth;
    darkPurple = values.darkPurple;
    abyssColor = values.abyssColor;
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
