using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class GameplayFormWheelController : MonoBehaviour {
  readonly List<Action> actions = new();
  FormButtons formButtons;
  CharacterState characterState;

  void OnEnable() {
    EnsureResolved();
    RegisterHandlers();
    SyncButtons("enable");
  }

  void OnDisable() {
    if (formButtons != null) {
      formButtons.SetHoverIndex(-1);
    }
    UnregisterHandlers();
  }

  void RegisterHandlers() {
    if (actions.Count > 0) {
      return;
    }

    actions.Add(MessageBus.On("gameplay.hover", o => HandleHover(o)));
    actions.Add(MessageBus.On("gameplay.unhover", o => HandleUnhover()));
    actions.Add(MessageBus.On("gameplay.click", o => HandleClick(o)));
    actions.Add(MessageBus.On("formChanged", o => SyncButtons("form_changed")));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  void EnsureResolved() {
    formButtons ??= GetComponent<FormButtons>();
    characterState ??= FindAnyObjectByType<CharacterState>();
  }

  void SyncButtons(string source) {
    EnsureResolved();
    if (formButtons == null) {
      Debug.LogWarning("[GameplayFormWheelController] Missing FormButtons component.");
      return;
    }

    formButtons.RefreshState();
    formButtons.SetHoverIndex(-1);
    Debug.Log(
      "[GameplayFormWheelController] source='" + (source ?? "") +
      "' active_form='" + EsperanzaForms.GetActive() + "'"
    );
  }

  void HandleHover(object payload) {
    if (!isActiveAndEnabled || formButtons == null) {
      return;
    }

    var target = payload as GameObject;
    var index = target != null ? formButtons.buttons.IndexOf(target) : -1;
    if (index < 0 || !IsUnlocked(index)) {
      formButtons.SetHoverIndex(-1);
      return;
    }

    formButtons.SetHoverIndex(index);
  }

  void HandleUnhover() {
    if (!isActiveAndEnabled || formButtons == null) {
      return;
    }

    formButtons.SetHoverIndex(-1);
  }

  void HandleClick(object payload) {
    if (!isActiveAndEnabled) {
      return;
    }

    EnsureResolved();
    if (formButtons == null || characterState == null) {
      Debug.LogWarning(
        "[GameplayFormWheelController] Missing dependency" +
        " form_buttons=" + (formButtons != null ? 1 : 0) +
        " character_state=" + (characterState != null ? 1 : 0)
      );
      return;
    }

    var target = payload as GameObject;
    var index = target != null ? formButtons.buttons.IndexOf(target) : -1;
    if (index < 0) {
      return;
    }

    if (!IsUnlocked(index)) {
      Debug.Log(
        "[GameplayFormWheelController] Ignored locked form click" +
        " button='" + (target != null ? target.name : "-") + "'"
      );
      return;
    }

    var selectedButton = formButtons.buttons[index];
    var resolvedForm = EsperanzaForms.ResolveFormKey(selectedButton.name);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      Debug.LogWarning(
        "[GameplayFormWheelController] Ignored unresolved form button='" + selectedButton.name + "'"
      );
      return;
    }

    if (!characterState.SetActiveForm(resolvedForm, "gameplay_wheel")) {
      return;
    }

    formButtons.SetHoverIndex(-1);
    SyncButtons("gameplay_click");
    gameObject.SetActive(false);
    Debug.Log(
      "[GameplayFormWheelController] Selected form='" + resolvedForm +
      "' source='gameplay_wheel'"
    );
  }

  bool IsUnlocked(int index) {
    if (formButtons == null || index < 0 || index >= formButtons.buttons.Count) {
      return false;
    }

    var button = formButtons.buttons[index];
    return button != null && EsperanzaForms.IsUnlocked(button.name);
  }
}
