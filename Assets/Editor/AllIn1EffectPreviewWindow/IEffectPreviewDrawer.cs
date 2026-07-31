#if UNITY_EDITOR
using UnityEngine;

public interface IEffectPreviewDrawer {
  string DisplayName { get; }
  string ShaderName { get; }
  string Description { get; }
  // Left, right, top, and bottom space around the intact source sprite.
  Vector4 PreviewPadding { get; }
  void OnEnable(AllIn1EffectPreviewWindow window);
  void OnDisable();
  void DrawControls(AllIn1EffectPreviewWindow window);
  void ApplyMaterialState(Material material);
  void ResetDefaults();
  void NormalizeState();
  void CopySettingsFrom(IEffectPreviewDrawer source);

  Sprite GetTextureSlotSprite(string slotName);
  void SetTextureSlotSprite(string slotName, Sprite sprite);
  bool AllowsProceduralDefault(string slotName);
  string GetTextureSlotDisplayName(string slotName);
}
#endif
