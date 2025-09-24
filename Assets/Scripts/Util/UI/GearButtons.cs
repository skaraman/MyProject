
using UnityEngine;

public class GearButtons : ButtonGroup {

  void Start() {
    MessageBus.On("gearReady", o => OnGearReady());

  }

  protected override void HandleActiveState(GameObject button) {
    // Swap sprite to "active" variant
    button.GetComponent<ReferenceListGameObject>().Get(0).SetActive(true);
    button.GetComponent<ReferenceListGameObject>().Get(1).SetActive(false);
  }

  protected override void HandleInactiveState(GameObject button) {
    // Swap sprite to "idle" variant
    button.GetComponent<ReferenceListGameObject>().Get(0).SetActive(false);
    button.GetComponent<ReferenceListGameObject>().Get(1).SetActive(true);
  }

  public void OnGearReady(string form = null) {
    var use = form ?? EsperanzaForms.GetActive();
    foreach (var slot in EquippedItems.AllGearForms[use]) {
      var button = buttons.Find(b => b.name == slot.Key);
      if (button == null) continue;
      if (slot.Value == null) {
        button.GetComponent<SpriteRenderer>().sprite = null;
        continue;
      }
      button.GetComponent<SpriteWithNormals>().labelPrefix = slot.Value.gearId;
      var shaderAnimator = button.GetComponent<AllIn1AnimatorInspector>();
      var newColor = ShaderColors.myColors[slot.Value.gearColor];
      shaderAnimator.SetKeyword("GLOW_ON", true);
      shaderAnimator.ResetActive();
      shaderAnimator.Reset();
      shaderAnimator.AddFloatSequence("_Glow", 4f, 4f, 1f);
      shaderAnimator.AddColorSequence("_GlowColor", newColor, newColor, 1f);
      shaderAnimator.AddColorSequence("_Color", newColor, newColor, 1f);
      button.GetComponent<SpriteWithNormals>().ForceUpdateSpriteAndNormal();
    }
  }
}
