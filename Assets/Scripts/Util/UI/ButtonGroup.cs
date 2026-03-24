using System.Collections.Generic;
using UnityEngine;

public class ButtonGroup : MonoBehaviour {
  public List<GameObject> buttons = new();
  public int activeIndex = -1;
  public int hoverIndex = -1;
  GameObject activeButton;
  GameObject hoverButton;

  public void SetHoverButton(GameObject target) {
    if (hoverButton == target) return;

    if (hoverButton == null) {
      ClearHoverStateExcept(target);
    }

    if (hoverButton != null) {
      HandleUnhoverState(hoverButton);
      hoverButton = null;
    }

    if (target != null) {
      HandleHoverState(target);
      hoverButton = target;
    }
  }

  public void SetActiveButton(GameObject target) {
    if (activeButton == target) return;

    if (activeButton == null) {
      ClearActiveStateExcept(target);
    }

    if (activeButton != null) {
      HandleInactiveState(activeButton);
      activeButton = null;
    }

    if (target != null) {
      HandleActiveState(target);
      activeButton = target;
    }
  }

  public void SetHoverIndex(int index) {
    if (hoverIndex == index) return;
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
    if (activeIndex == index) return;
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

  void ClearHoverStateExcept(GameObject target) {
    for (int i = 0; i < buttons.Count; i++) {
      var button = buttons[i];
      if (button == null || button == target) continue;
      HandleUnhoverState(button);
    }
  }

  void ClearActiveStateExcept(GameObject target) {
    for (int i = 0; i < buttons.Count; i++) {
      var button = buttons[i];
      if (button == null || button == target) continue;
      HandleInactiveState(button);
    }
  }

  protected virtual void HandleActiveState(GameObject button) { }
  protected virtual void HandleInactiveState(GameObject button) { }
  protected virtual void HandleHoverState(GameObject button) { }
  protected virtual void HandleUnhoverState(GameObject button) { }
}

