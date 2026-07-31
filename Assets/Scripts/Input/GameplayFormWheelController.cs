using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class GameplayFormWheelController : MonoBehaviour {
  const string MenuMoveSoundId = "menu.move";
  const float MinimumDirectionalDot = 0.05f;

  readonly List<Action> actions = new();
  FormButtons formButtons;
  CharacterState characterState;
  GameplayInput gameplayInput;
  int keyboardIndex = -1;
  int pointerIndex = -1;
  bool pointerIsOverWheel;

  void OnEnable() {
    PrepareHitColliders();
    EnsureResolved();
    RegisterHandlers();
    pointerIndex = -1;
    pointerIsOverWheel = false;
    SyncButtons("enable");
    gameplayInput?.NotifyFormsWheelOpened(this);
    MouseManager.Instance?.RefreshHoverTarget();
  }

  void PrepareHitColliders() {
    // These are pointer hit targets on a camera-following overlay, not physics obstacles.
    var colliders = GetComponentsInChildren<Collider2D>(includeInactive: true);
    for (var i = 0; i < colliders.Length; i++) {
      var hitCollider = colliders[i];
      if (hitCollider != null) {
        hitCollider.isTrigger = true;
      }
    }
  }

  void OnDisable() {
    if (formButtons != null) {
      formButtons.SetHoverIndex(-1);
    }

    pointerIndex = -1;
    pointerIsOverWheel = false;
    UnregisterHandlers();
    gameplayInput?.NotifyFormsWheelClosed(this);
  }

  void RegisterHandlers() {
    if (actions.Count > 0) {
      return;
    }

    actions.Add(MessageBus.On("gameplay.hover", HandleHover));
    actions.Add(MessageBus.On("gameplay.unhover", _ => HandleUnhover()));
    actions.Add(MessageBus.On("gameplay.click", HandleClick));
    actions.Add(MessageBus.On(CharacterMessageTopics.FormChanged, _ => SyncButtons("form_changed")));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  void EnsureResolved() {
    formButtons ??= GetComponent<FormButtons>();
    characterState ??= SingleSceneManager.ResolveGameplayCharacterState();
    gameplayInput ??= GetComponentInParent<GameplayInput>();
  }

  void SyncButtons(string source) {
    EnsureResolved();
    if (formButtons == null) {
      Debug.LogWarning("[GameplayFormWheelController] Missing FormButtons component.");
      return;
    }

    formButtons.RefreshState();
    if (!IsUnlocked(keyboardIndex)) {
      keyboardIndex = ResolveInitialIndex();
    }
    RefreshFocusVisual();
    RuntimeLog.Log(
      "[GameplayFormWheelController] source='" + (source ?? "") +
      "' active_form='" + EsperanzaForms.GetActive() +
      "' keyboard_index=" + keyboardIndex
    );
  }

  void HandleHover(object payload) {
    if (!isActiveAndEnabled || formButtons == null) {
      return;
    }

    var resolvedIndex = ResolveButtonIndex(payload as GameObject);
    pointerIsOverWheel = resolvedIndex >= 0;
    pointerIndex = resolvedIndex;
    if (pointerIsOverWheel && !IsUnlocked(pointerIndex)) {
      pointerIndex = -1;
    }
    RefreshFocusVisual();
  }

  void HandleUnhover() {
    if (!isActiveAndEnabled || formButtons == null) {
      return;
    }

    pointerIsOverWheel = false;
    pointerIndex = -1;
    RefreshFocusVisual();
  }

  void HandleClick(object payload) {
    if (!isActiveAndEnabled) {
      return;
    }

    var index = ResolveButtonIndex(payload as GameObject);
    if (index < 0) {
      return;
    }
    TrySelectIndex(index, "gameplay_mouse_click");
  }

  public void Navigate(Vector2 direction) {
    if (!isActiveAndEnabled || direction.sqrMagnitude <= 0.0001f) {
      return;
    }

    EnsureResolved();
    if (formButtons == null) {
      return;
    }

    pointerIsOverWheel = false;
    pointerIndex = -1;

    if (!IsUnlocked(keyboardIndex)) {
      keyboardIndex = ResolveInitialIndex();
    }

    var nextIndex = FindDirectionalIndex(keyboardIndex, direction.normalized);
    if (nextIndex < 0 || nextIndex == keyboardIndex) {
      RefreshFocusVisual();
      return;
    }

    keyboardIndex = nextIndex;
    RefreshFocusVisual();
    MessageBus.Send(SoundEffectPlayer.PlayMessage, MenuMoveSoundId);
  }

  public bool ConfirmSelection() {
    if (!isActiveAndEnabled) {
      return false;
    }

    var index = pointerIsOverWheel && IsUnlocked(pointerIndex)
      ? pointerIndex
      : keyboardIndex;
    if (!IsUnlocked(index)) {
      index = ResolveInitialIndex();
    }
    return TrySelectIndex(index, "gameplay_keyboard_confirm");
  }

  bool TrySelectIndex(int index, string source) {
    EnsureResolved();
    if (formButtons == null || characterState == null) {
      Debug.LogWarning(
        "[GameplayFormWheelController] Missing dependency" +
        " form_buttons=" + (formButtons != null ? 1 : 0) +
        " character_state=" + (characterState != null ? 1 : 0)
      );
      return false;
    }

    if (!IsUnlocked(index)) {
      RuntimeLog.Log(
        "[GameplayFormWheelController] Ignored locked or unresolved selection" +
        " index=" + index +
        " source='" + (source ?? "") + "'"
      );
      return false;
    }

    var selectedButton = formButtons.buttons[index];
    var resolvedForm = EsperanzaForms.ResolveFormKey(selectedButton.name);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      Debug.LogWarning(
        "[GameplayFormWheelController] Ignored unresolved form button='" + selectedButton.name + "'"
      );
      return false;
    }

    var isAlreadyActive = string.Equals(
      EsperanzaForms.GetActive(),
      resolvedForm,
      StringComparison.OrdinalIgnoreCase
    );
    if (!isAlreadyActive && !characterState.SetActiveForm(resolvedForm, "gameplay_wheel")) {
      return false;
    }

    keyboardIndex = index;
    SyncButtons(source);
    gameObject.SetActive(false);
    RuntimeLog.Log(
      "[GameplayFormWheelController] Confirmed form='" + resolvedForm +
      "' source='" + (source ?? "") + "'"
    );
    return true;
  }

  void RefreshFocusVisual() {
    if (formButtons == null) {
      return;
    }

    var focusIndex = pointerIsOverWheel ? pointerIndex : keyboardIndex;
    formButtons.SetHoverIndex(IsUnlocked(focusIndex) ? focusIndex : -1);
  }

  int ResolveInitialIndex() {
    if (formButtons == null) {
      return -1;
    }

    if (IsUnlocked(formButtons.activeIndex)) {
      return formButtons.activeIndex;
    }

    for (var i = 0; i < formButtons.buttons.Count; i++) {
      if (IsUnlocked(i)) {
        return i;
      }
    }

    return -1;
  }

  int ResolveButtonIndex(GameObject target) {
    if (formButtons == null || target == null) {
      return -1;
    }

    var targetTransform = target.transform;
    for (var i = 0; i < formButtons.buttons.Count; i++) {
      var button = formButtons.buttons[i];
      if (button == null) {
        continue;
      }

      if (target == button || targetTransform.IsChildOf(button.transform)) {
        return i;
      }
    }

    return -1;
  }

  int FindDirectionalIndex(int currentIndex, Vector2 direction) {
    if (formButtons == null) {
      return -1;
    }

    var hasCurrent = IsUnlocked(currentIndex);
    var origin = hasCurrent ? GetButtonPosition(currentIndex) : Vector2.zero;
    var bestIndex = -1;
    var bestScore = float.NegativeInfinity;

    for (var i = 0; i < formButtons.buttons.Count; i++) {
      if (!IsUnlocked(i) || i == currentIndex) {
        continue;
      }

      var delta = GetButtonPosition(i) - origin;
      var distance = delta.magnitude;
      if (distance <= 0.0001f) {
        continue;
      }

      var alignment = Vector2.Dot(delta / distance, direction);
      if (alignment <= MinimumDirectionalDot) {
        continue;
      }

      // Prefer the button most directly in the requested direction, using
      // distance only to break close angular ties on the six-point wheel.
      var score = (alignment * 2f) - (distance * 0.01f);
      if (score <= bestScore) {
        continue;
      }

      bestScore = score;
      bestIndex = i;
    }

    return bestIndex;
  }

  Vector2 GetButtonPosition(int index) {
    if (formButtons == null || index < 0 || index >= formButtons.buttons.Count) {
      return Vector2.zero;
    }

    var button = formButtons.buttons[index];
    if (button == null) {
      return Vector2.zero;
    }

    var hitCollider = button.GetComponent<Collider2D>();
    if (hitCollider != null) {
      var worldPosition = button.transform.TransformPoint(hitCollider.offset);
      return transform.InverseTransformPoint(worldPosition);
    }

    var visualReferences = button.GetComponent<ReferenceListGameObject>();
    if (visualReferences != null) {
      var visual = visualReferences.Get(0) ?? visualReferences.Get(1);
      if (visual != null) {
        return transform.InverseTransformPoint(visual.transform.position);
      }
    }

    return transform.InverseTransformPoint(button.transform.position);
  }

  bool IsUnlocked(int index) {
    if (formButtons == null || index < 0 || index >= formButtons.buttons.Count) {
      return false;
    }

    var button = formButtons.buttons[index];
    return button != null && EsperanzaForms.IsUnlocked(button.name);
  }
}
