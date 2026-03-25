
using UnityEngine;

public class FormButtons : ButtonGroup {

  void Start() {
    RefreshState();
  }

  void OnEnable() {
    RefreshState();
  }

  public void RefreshState() {
    RefreshUnlockedVisuals();
    SyncActiveFormSelection();
  }

  public void RefreshUnlockedVisuals() {
    for (var i = 0; i < buttons.Count; i++) {
      var button = buttons[i];
      if (button == null) continue;

      var refs = button.GetComponent<ReferenceListGameObject>();
      if (refs == null) continue;

      var isUnlocked = EsperanzaForms.IsUnlocked(button.name);
      var activeState = refs.Get(0);
      var idleState = refs.Get(1);
      var lockedState = refs.Get(2);

      if (lockedState != null) {
        lockedState.SetActive(!isUnlocked);
      }

      if (!isUnlocked) {
        if (activeState != null) activeState.SetActive(false);
        if (idleState != null) idleState.SetActive(false);
        continue;
      }

      if (activeIndex == i) {
        if (activeState != null) activeState.SetActive(true);
        if (idleState != null) idleState.SetActive(false);
      } else {
        if (activeState != null) activeState.SetActive(false);
        if (idleState != null) idleState.SetActive(true);
      }
    }
  }

  public void SyncActiveFormSelection() {
    var activeForm = EsperanzaForms.GetActive();
    if (string.IsNullOrWhiteSpace(activeForm)) {
      SetActiveIndex(-1);
      return;
    }

    for (var i = 0; i < buttons.Count; i++) {
      var button = buttons[i];
      if (button == null) continue;
      if (!string.Equals(button.name, activeForm, System.StringComparison.OrdinalIgnoreCase)) continue;
      SetActiveIndex(i);
      return;
    }

    SetActiveIndex(-1);
  }

  protected override void HandleActiveState(GameObject button) {
    if (IsLocked(button)) return;
    var refs = button.GetComponent<ReferenceListGameObject>();
    if (refs == null) return;
    refs.Get(0)?.SetActive(true);
    refs.Get(1)?.SetActive(false);
  }

  protected override void HandleInactiveState(GameObject button) {
    if (IsLocked(button)) return;
    var refs = button.GetComponent<ReferenceListGameObject>();
    if (refs == null) return;
    refs.Get(0)?.SetActive(false);
    refs.Get(1)?.SetActive(true);
  }

  protected override void HandleHoverState(GameObject button) {
    if (IsLocked(button)) return;
    ApplyHoverState(button, isHovered: true);
  }

  protected override void HandleUnhoverState(GameObject button) {
    if (IsLocked(button)) return;
    ApplyHoverState(button, isHovered: false);
  }

  bool IsLocked(GameObject button) {
    if (button == null) return true;
    return !EsperanzaForms.IsUnlocked(button.name);
  }

  static void ApplyHoverState(GameObject button, bool isHovered) {
    if (button == null) {
      return;
    }

    var resolvedAnimators = ButtonShaderKeywords.ApplyToButton(button, "OUTBASE_ON", isHovered);
    if (resolvedAnimators > 0) {
      return;
    }

    Debug.LogWarning("[FormButtons] No hover animators resolved for button='" + button.name + "'");
  }
}
