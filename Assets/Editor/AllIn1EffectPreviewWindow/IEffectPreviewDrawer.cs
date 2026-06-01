#if UNITY_EDITOR
using UnityEngine;

public interface IEffectPreviewDrawer {
  string DisplayName { get; }
  void OnEnable(AllIn1EffectPreviewWindow window);
  void OnDisable();
  void DrawControls(AllIn1EffectPreviewWindow window);
  void ApplyMaterialState(Material material);
  void ResetDefaults();
  void NormalizeState();

  Sprite GetTextureSlotSprite(string slotName);
  void SetTextureSlotSprite(string slotName, Sprite sprite);
  bool AllowsProceduralDefault(string slotName);
  string GetTextureSlotDisplayName(string slotName);
}
#endif
