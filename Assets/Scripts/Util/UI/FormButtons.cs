
using UnityEngine;

public class FormButtons : ButtonGroup {

  void Start() {
    foreach (var btn in buttons) {
      if (btn.name == "Base") continue;
      foreach (var form in EsperanzaForms.Unlocked) {
        if (btn.name == form.Key) {
          btn.GetComponent<ReferenceListGameObject>().Get(2).SetActive(false);
          btn.GetComponent<ReferenceListGameObject>().Get(1).SetActive(true);
        }
      }
    }
  }

  protected override void HandleActiveState(GameObject button) {
    if (button.GetComponent<ReferenceListGameObject>().Get(2)) {
      if (button.GetComponent<ReferenceListGameObject>().Get(2).activeSelf) return;
    }
    button.GetComponent<ReferenceListGameObject>().Get(0).SetActive(true);
    button.GetComponent<ReferenceListGameObject>().Get(1).SetActive(false);
  }

  protected override void HandleInactiveState(GameObject button) {
    if (button.GetComponent<ReferenceListGameObject>().Get(2)) {
      if (button.GetComponent<ReferenceListGameObject>().Get(2).activeSelf) return;
    }
    button.GetComponent<ReferenceListGameObject>().Get(0).SetActive(false);
    button.GetComponent<ReferenceListGameObject>().Get(1).SetActive(true);
  }
  
  protected override void HandleHoverState(GameObject button) {
    if (button.GetComponent<ReferenceListGameObject>().Get(2)) {
      if (button.GetComponent<ReferenceListGameObject>().Get(2).activeSelf) return;
    }
    var shader = button.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(0);
    var shader2 = button.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(1);
    shader.SetKeyword("OUTBASE_ON", true);
    shader2.SetKeyword("OUTBASE_ON", true);
  }

  protected override void HandleUnhoverState(GameObject button) {
    if (button.GetComponent<ReferenceListGameObject>().Get(2)) {
      if (button.GetComponent<ReferenceListGameObject>().Get(2).activeSelf) return;
    }
    var shader = button.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(0);
    var shader2 = button.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(1);
    shader2.SetKeyword("OUTBASE_ON", false);
    shader.SetKeyword("OUTBASE_ON", false);
  }
}
