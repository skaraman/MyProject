using System.Collections.Generic;
using UnityEngine;

public class ButtonGroup : MonoBehaviour {
  public List<GameObject> buttons = new();
  GameObject activeButton;

  public void SetActiveButton(GameObject target) {
    for (int i = 0; i < buttons.Count; i++) {
      var btn = buttons[i];
      var shouldBeActive = btn == target;
      if (shouldBeActive) {
        HandleActiveState(btn);
        activeButton = target;
      }
      else {
        HandleInactiveState(btn);
      }
    }
  }

  public void SetActiveIndex(int index) {
    if (index < 0 || index >= buttons.Count) {
      Debug.Log($"[ButtonGroup] Index out of range: {index}");
      return;
    }
    SetActiveButton(buttons[index]);
  }

  public GameObject GetActiveButton() {
    //Debug.Log($"[ButtonGroup] Active button: {activeButton?.name}");
    return activeButton;
  }

  protected virtual void HandleActiveState(GameObject button) {
    Debug.Log("override this");
  }
  protected virtual void HandleInactiveState(GameObject button) {
    Debug.Log("override this");
  }
}

