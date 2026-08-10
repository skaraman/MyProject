using UnityEngine;

public class InventoryButtons : ButtonGroup {
  ItemButtons itemButtons;

  void OnEnable() {
    ResolveItemButtons();
    var selectedIndex = activeIndex >= 0 && activeIndex < buttons.Count ? activeIndex : 0;
    if (selectedIndex < buttons.Count) {
      SetActiveIndex(selectedIndex);
      itemButtons?.ShowCategory(buttons[selectedIndex].name);
    }
  }

  protected override void HandleActiveState(GameObject button) {
    SetVisualState(button, isActive: true);
    ResolveItemButtons();
    itemButtons?.ShowCategory(button != null ? button.name : "GEAR");
  }

  protected override void HandleInactiveState(GameObject button) {
    SetVisualState(button, isActive: false);
  }

  protected override void HandleHoverState(GameObject button) {
    SetVisualState(button, isActive: true);
  }

  protected override void HandleUnhoverState(GameObject button) {
    SetVisualState(button, GetActiveButton() == button);
  }

  void ResolveItemButtons() {
    if (itemButtons == null) {
      itemButtons = GetComponentInChildren<ItemButtons>(includeInactive: true);
    }
  }

  static void SetVisualState(GameObject button, bool isActive) {
    if (button == null) {
      return;
    }

    var activeVisual = FindDirectChild(button.transform, "active");
    var inactiveVisual = FindDirectChild(button.transform, "inactive");
    if (activeVisual != null) {
      activeVisual.gameObject.SetActive(isActive);
    }
    if (inactiveVisual != null) {
      inactiveVisual.gameObject.SetActive(!isActive);
    }
  }

  static Transform FindDirectChild(Transform parent, string childName) {
    if (parent == null) {
      return null;
    }

    for (var i = 0; i < parent.childCount; i++) {
      var child = parent.GetChild(i);
      if (child != null && child.name == childName) {
        return child;
      }
    }
    return null;
  }
}
