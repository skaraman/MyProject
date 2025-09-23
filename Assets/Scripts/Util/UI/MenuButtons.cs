
using UnityEngine;

public class MenuButtons : ButtonGroup {

  protected override void HandleHoverState(GameObject button) {
    var shader = button.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(0);
    var shader2 = button.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(1);
    shader.SetKeyword("OUTBASE_ON", true);
    shader2.SetKeyword("OUTBASE_ON", true);
  }

  protected override void HandleUnhoverState(GameObject button) {
    var shader = button.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(0);
    var shader2 = button.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(1);
    shader2.SetKeyword("OUTBASE_ON", false);
    shader.SetKeyword("OUTBASE_ON", false);
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
}
