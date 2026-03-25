using UnityEngine;

public class StatsButtons : ButtonGroup {
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

  static void ApplyVisualState(GameObject button, bool isActive) {
    if (button == null) {
      return;
    }

    var referenceList = button.GetComponent<ReferenceListGameObject>();
    if (referenceList != null) {
      var activeVisual = referenceList.Get(0);
      var idleVisual = referenceList.Get(1);
      if (activeVisual != null) {
        activeVisual.SetActive(isActive);
      }
      if (idleVisual != null) {
        idleVisual.SetActive(!isActive);
      }
      return;
    }

    ApplyActiveKeyword(button, button.GetComponent<AllIn1AnimatorInspector>(), isActive);
  }

  static void ApplyHoverState(GameObject button, bool isHovered) {
    if (button == null) {
      return;
    }

    var resolvedAnimators = ButtonShaderKeywords.ApplyToButton(button, "OUTBASE_ON", isHovered);
    if (resolvedAnimators > 0) {
      return;
    }

    Debug.LogWarning("[StatsButtons] No hover animators resolved for button='" + button.name + "'");
  }

  static void ApplyActiveKeyword(GameObject button, AllIn1AnimatorInspector shader, bool isActive) {
    if (shader == null) {
      return;
    }

    ButtonShaderKeywords.ApplyToAnimator(button, shader, "SHINE_ON", isActive);
  }
}
