
using UnityEngine;

public class MenuButtons : ButtonGroup {

  protected override void HandleHoverState(GameObject button) {
    ApplyHoverState(button, isHovered: true);
  }

  protected override void HandleUnhoverState(GameObject button) {
    ApplyHoverState(button, isHovered: false);
  }

  protected override void HandleActiveState(GameObject button) {
    ApplyVisualState(button, isActive: true);
  }

  protected override void HandleInactiveState(GameObject button) {
    ApplyVisualState(button, isActive: false);
  }

  static void ApplyHoverState(GameObject button, bool isHovered) {
    if (button == null) {
      return;
    }

    var resolvedAnimators = ButtonShaderKeywords.ApplyToButton(button, "OUTBASE_ON", isHovered);
    if (resolvedAnimators > 0) {
      return;
    }

    Debug.LogWarning("[MenuButtons] No hover animators resolved for button='" + button.name + "'");
  }

  static void ApplyVisualState(GameObject button, bool isActive) {
    if (button == null) {
      return;
    }

    var visualReferences = button.GetComponent<ReferenceListGameObject>();
    if (visualReferences == null) {
      Debug.LogWarning("[MenuButtons] Missing visual references for button='" + button.name + "'");
      return;
    }

    var activeVisual = visualReferences.Get(0);
    var idleVisual = visualReferences.Get(1);
    if (activeVisual != null) {
      activeVisual.SetActive(isActive);
    }
    if (idleVisual != null) {
      idleVisual.SetActive(!isActive);
    }
  }
}
