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

    ApplyActiveKeyword(button.GetComponent<AllIn1AnimatorInspector>(), isActive);
  }

  static void ApplyHoverState(GameObject button, bool isHovered) {
    if (button == null) {
      return;
    }

    var shaderList = button.GetComponent<ReferenceListAllIn1AnimatorInspector>();
    if (shaderList != null) {
      ApplyHoverKeyword(shaderList.Get(0), isHovered);
      ApplyHoverKeyword(shaderList.Get(1), isHovered);
      return;
    }

    ApplyHoverKeyword(button.GetComponent<AllIn1AnimatorInspector>(), isHovered);
  }

  static void ApplyHoverKeyword(AllIn1AnimatorInspector shader, bool isHovered) {
    if (shader == null) {
      return;
    }

    shader.SetKeyword("OUTBASE_ON", isHovered);
  }

  static void ApplyActiveKeyword(AllIn1AnimatorInspector shader, bool isActive) {
    if (shader == null) {
      return;
    }

    shader.SetKeyword("SHINE_ON", isActive);
  }
}
