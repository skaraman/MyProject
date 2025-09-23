using System.Collections.Generic;
using UnityEngine;

public class ButtonGroup : MonoBehaviour {
  public List<GameObject> buttons = new();
  public int activeIndex = -1;
  public int hoverIndex = -1;
  GameObject activeButton;
  GameObject hoverButton;

  public void SetHoverButton(GameObject target) {
    for (int i = 0; i < buttons.Count; i++) {
      var btn = buttons[i];
      var shouldBeActive = btn == target;
      if (shouldBeActive) {
        // Highlight button on hover
        HandleHoverState(btn);
        hoverButton = btn;
      }
      else {
        // Remove highlight from other buttons
        HandleUnhoverState(btn);
      }
    }
  }

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

  public void SetHoverIndex(int index) {
    if (index == -1) {
      hoverIndex = -1;
      SetHoverButton(null);
      return;
    }
    if (index < 0 || index >= buttons.Count) {
      Debug.Log($"[ButtonGroup] Index out of range: {index}");
      return;
    }
    hoverIndex = index;
    SetHoverButton(buttons[index]);
  }

  public void SetActiveIndex(int index) {
    if (index == -1) {
      activeIndex = -1;
      SetActiveButton(null);
      return;
    }
    if (index < 0 || index >= buttons.Count) {
      Debug.Log($"[ButtonGroup] Index out of range: {index}");
      return;
    }
    activeIndex = index;
    SetActiveButton(buttons[index]);
  }

  public GameObject GetActiveButton() {
    return activeButton;
  }
  public GameObject GetHoverButton() {
    return hoverButton;
  }
  protected virtual void HandleActiveState(GameObject button) {
    Debug.Log("override this");
  }
  protected virtual void HandleInactiveState(GameObject button) {
    Debug.Log("override this");
  }
  protected virtual void HandleHoverState(GameObject button) {
    Debug.Log("override this");
  }
  protected virtual void HandleUnhoverState(GameObject button) {
    Debug.Log("override this");
  }
}

