#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[System.Serializable]
public sealed class AllIn1EffectPreviewDrawer : IEffectPreviewDrawer {
  [SerializeField] Sprite breakupPatternSprite;
  [SerializeField] Sprite flowPatternSprite;

  [SerializeField] float fireSpeed = 1.0f;
  [SerializeField] float burnAmount = 0.5f;
  [SerializeField] float fireSpread = 1.0f;
  [SerializeField] float distortion = 0.15f;
  [SerializeField] float fireIntensity = 1.2f;

  [SerializeField] Color coreColor = new Color(1f, 0.95f, 0.6f, 1f);
  [SerializeField] Color edgeColor = new Color(1f, 0.4f, 0.05f, 1f);
  [SerializeField] Color smokeColor = new Color(0.1f, 0.02f, 0.01f, 1f);

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
    EditorGUILayout.LabelField("Fire Properties", EditorStyles.boldLabel);
    fireSpeed = EditorGUILayout.Slider("Fire Speed", fireSpeed, 0f, 5f);
    burnAmount = EditorGUILayout.Slider("Burn Amount", burnAmount, 0f, 1f);
    fireSpread = EditorGUILayout.Slider("Spread Amount", fireSpread, 0f, 5f);
    distortion = EditorGUILayout.Slider("Distortion", distortion, 0f, 1f);
    fireIntensity = EditorGUILayout.Slider("Fire Intensity", fireIntensity, 0f, 3f);
    
    EditorGUILayout.Space(6f);
    EditorGUILayout.LabelField("Colors", EditorStyles.boldLabel);
    coreColor = EditorGUILayout.ColorField("Core Color", coreColor);
    edgeColor = EditorGUILayout.ColorField("Edge Color", edgeColor);
    smokeColor = EditorGUILayout.ColorField("Smoke Color", smokeColor);

    EditorGUILayout.Space(6f);
    DrawTextureControls(window);
  }

  public void ApplyMaterialState(Material previewMaterial) {
    if (previewMaterial == null) return;

    SetTextureIfPresent(previewMaterial, "_NoiseTex", GetBreakupTexture());
    SetTextureIfPresent(previewMaterial, "_FlowTex", GetFlowTexture());

    SetFloatIfPresent(previewMaterial, "_FireSpeed", fireSpeed);
    SetFloatIfPresent(previewMaterial, "_BurnAmount", burnAmount);
    SetFloatIfPresent(previewMaterial, "_FireSpread", fireSpread);
    SetFloatIfPresent(previewMaterial, "_Distortion", distortion);
    SetFloatIfPresent(previewMaterial, "_FireIntensity", fireIntensity);

    SetColorIfPresent(previewMaterial, "_CoreColor", coreColor);
    SetColorIfPresent(previewMaterial, "_EdgeColor", edgeColor);
    SetColorIfPresent(previewMaterial, "_SmokeColor", smokeColor);
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
    fireSpeed = 1.0f;
    burnAmount = 0.5f;
    fireSpread = 1.0f;
    distortion = 0.15f;
    fireIntensity = 1.2f;
    coreColor = new Color(1f, 0.95f, 0.6f, 1f);
    edgeColor = new Color(1f, 0.4f, 0.05f, 1f);
    smokeColor = new Color(0.1f, 0.02f, 0.01f, 1f);
    Debug.Log($"[{nameof(AllIn1EffectPreviewDrawer)}] Reset fire defaults");
  }

  public void NormalizeState() {
    fireSpeed = Mathf.Clamp(fireSpeed, 0f, 5f);
    burnAmount = Mathf.Clamp(burnAmount, 0f, 1f);
    fireSpread = Mathf.Clamp(fireSpread, 0f, 5f);
    distortion = Mathf.Clamp(distortion, 0f, 1f);
    fireIntensity = Mathf.Clamp(fireIntensity, 0f, 3f);
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

  void DrawTextureControls(AllIn1EffectPreviewWindow window) {
    EditorGUILayout.LabelField("Texture Layers", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "Breakup controls the fire erosion and noise. Flow controls the heat distortion. Leaving either one on Procedural Default uses a generated texture instead of an atlas sprite.",
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
