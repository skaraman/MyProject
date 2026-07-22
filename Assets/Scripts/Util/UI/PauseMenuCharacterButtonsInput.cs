using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PauseMenuCharacterButtonsInput : MonoBehaviour {
  public const string ButtonSelectedMessage = "pauseMenu.character.buttonSelected";
  public const string TopMenuFocusChangedMessage = "pauseMenu.character.topMenuFocusChanged";

  sealed class ButtonEntry {
    public ButtonGroup group;
    public GameObject button;
    public int groupIndex;
  }

  readonly List<Action> actions = new();
  readonly List<ButtonEntry> buttons = new();

  ButtonEntry focusedButton;
  ButtonGroup hoveredGroup;
  bool topMenuFocused = true;

  public bool HasFocusedButton => focusedButton != null;

  void OnEnable() {
    RebuildButtons();
    RegisterHandlers();
    SetTopMenuFocused(focusedButton == null);
  }

  void OnDisable() {
    UnregisterHandlers();
    ClearHover();
    ClearFocus();
  }

  void OnTransformChildrenChanged() {
    if (isActiveAndEnabled) {
      RebuildButtons();
    }
  }

  void RegisterHandlers() {
    if (actions.Count > 0) {
      return;
    }

    actions.Add(MessageBus.On("pauseMenu.up", o => { if (InputMessageValue.IsPressed(o)) MoveFocus(Vector2.up); }));
    actions.Add(MessageBus.On("pauseMenu.left", o => { if (InputMessageValue.IsPressed(o)) MoveFocus(Vector2.left); }));
    actions.Add(MessageBus.On("pauseMenu.right", o => { if (InputMessageValue.IsPressed(o)) MoveFocus(Vector2.right); }));
    actions.Add(MessageBus.On("pauseMenu.down", o => { if (InputMessageValue.IsPressed(o)) MoveFocus(Vector2.down); }));
    actions.Add(MessageBus.On("pauseMenu.select", o => { if (InputMessageValue.IsPressed(o)) SelectFocusedButton(); }));
    actions.Add(MessageBus.On("pauseMenu.hover", OnHover));
    actions.Add(MessageBus.On("pauseMenu.unhover", _ => ClearHover()));
    actions.Add(MessageBus.On("pauseMenu.click", OnClick));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }

    actions.Clear();
  }

  void RebuildButtons() {
    var previousFocusedButton = focusedButton != null ? focusedButton.button : null;
    buttons.Clear();

    var buttonGroups = GetComponentsInChildren<ButtonGroup>(includeInactive: true);
    for (var groupIndex = 0; groupIndex < buttonGroups.Length; groupIndex++) {
      var group = buttonGroups[groupIndex];
      if (group == null) {
        continue;
      }

      for (var buttonIndex = 0; buttonIndex < group.buttons.Count; buttonIndex++) {
        var button = group.buttons[buttonIndex];
        if (button == null || ContainsButton(button)) {
          continue;
        }

        EnsureBoxCollider(button);
        buttons.Add(new ButtonEntry {
          group = group,
          button = button,
          groupIndex = buttonIndex
        });
      }
    }

    focusedButton = ResolveButton(previousFocusedButton);
  }

  bool ContainsButton(GameObject button) {
    for (var i = 0; i < buttons.Count; i++) {
      if (buttons[i].button == button) {
        return true;
      }
    }

    return false;
  }

  static void EnsureBoxCollider(GameObject button) {
    if (button == null || button.GetComponent<BoxCollider2D>() != null) {
      return;
    }

    var spriteRenderer = button.GetComponent<SpriteRenderer>();
    if (spriteRenderer == null || spriteRenderer.sprite == null) {
      return;
    }

    var collider = button.AddComponent<BoxCollider2D>();
    collider.isTrigger = true;
    collider.offset = spriteRenderer.sprite.bounds.center;
    collider.size = spriteRenderer.sprite.bounds.size;
  }

  void OnHover(object payload) {
    var entry = ResolveButton(payload as GameObject);
    if (entry == null) {
      ClearHover();
      return;
    }

    SetFocus(entry, playMoveSound: true);
  }

  void OnClick(object payload) {
    var entry = ResolveButton(payload as GameObject);
    if (entry == null) {
      return;
    }

    SetFocus(entry, playMoveSound: true);
    MessageBus.Send(ButtonSelectedMessage, entry.button);
  }

  void MoveFocus(Vector2 direction) {
    if (topMenuFocused) {
      return;
    }

    if (buttons.Count == 0) {
      return;
    }

    if (focusedButton == null) {
      SetFocus(FindInitialButton(direction), playMoveSound: true);
      return;
    }

    if (direction == Vector2.up && IsTopmostButton(focusedButton)) {
      FocusTopMenu();
      return;
    }

    var next = FindNearestButtonInDirection(focusedButton, direction);
    if (next != null) {
      SetFocus(next, playMoveSound: true);
    }
  }

  bool IsTopmostButton(ButtonEntry current) {
    if (current == null || current.button == null) {
      return false;
    }

    var currentY = current.button.transform.position.y;
    for (var i = 0; i < buttons.Count; i++) {
      var candidate = buttons[i];
      if (candidate == null || candidate.button == null || candidate == current) {
        continue;
      }

      if (candidate.button.transform.position.y > currentY + 0.01f) {
        return false;
      }
    }

    return true;
  }

  ButtonEntry FindInitialButton(Vector2 direction) {
    ButtonEntry best = null;
    var bestScore = float.NegativeInfinity;
    for (var i = 0; i < buttons.Count; i++) {
      var candidate = buttons[i];
      var score = -Vector2.Dot(candidate.button.transform.position, direction);
      if (score <= bestScore) {
        continue;
      }

      bestScore = score;
      best = candidate;
    }

    return best;
  }

  ButtonEntry FindNearestButtonInDirection(ButtonEntry current, Vector2 direction) {
    ButtonEntry best = null;
    var bestScore = float.NegativeInfinity;
    var currentPosition = (Vector2)current.button.transform.position;
    for (var i = 0; i < buttons.Count; i++) {
      var candidate = buttons[i];
      if (candidate == current) {
        continue;
      }

      var offset = (Vector2)candidate.button.transform.position - currentPosition;
      var distance = offset.magnitude;
      if (distance <= 0.001f) {
        continue;
      }

      var alignment = Vector2.Dot(offset / distance, direction);
      if (alignment <= 0.01f) {
        continue;
      }

      var score = alignment * 1000f - distance;
      if (score <= bestScore) {
        continue;
      }

      bestScore = score;
      best = candidate;
    }

    return best;
  }

  void SetFocus(ButtonEntry entry, bool playMoveSound) {
    if (entry == null) {
      return;
    }

    SetTopMenuFocused(false);
    if (entry == focusedButton) {
      hoveredGroup = entry.group;
      entry.group.SetHoverIndex(entry.groupIndex);
      return;
    }

    if (focusedButton != null && focusedButton.group != entry.group) {
      focusedButton.group.SetActiveIndex(-1);
    }

    if (hoveredGroup != null && hoveredGroup != entry.group) {
      hoveredGroup.SetHoverIndex(-1);
    }

    focusedButton = entry;
    hoveredGroup = entry.group;
    entry.group.SetHoverIndex(entry.groupIndex);
    if (playMoveSound) {
      entry.group.SetActiveIndexWithSound(entry.groupIndex);
      return;
    }

    entry.group.SetActiveIndex(entry.groupIndex);
  }

  void SelectFocusedButton() {
    if (topMenuFocused) {
      return;
    }

    if (focusedButton == null) {
      var initialButton = FindInitialButton(Vector2.down);
      if (initialButton == null) {
        return;
      }

      SetFocus(initialButton, playMoveSound: false);
    }

    MessageBus.Send(ButtonSelectedMessage, focusedButton.button);
  }

  void ClearHover() {
    if (hoveredGroup == null) {
      return;
    }

    hoveredGroup.SetHoverIndex(-1);
    hoveredGroup = null;
  }

  void ClearFocus() {
    if (focusedButton == null) {
      return;
    }

    focusedButton.group.SetActiveIndex(-1);
    focusedButton = null;
  }

  public void FocusTopMenu() {
    ClearHover();
    ClearFocus();
    SetTopMenuFocused(true);
  }

  public bool FocusTopmostButton() {
    if (buttons.Count == 0) {
      return false;
    }

    var topmostButton = FindInitialButton(Vector2.down);
    if (topmostButton == null) {
      return false;
    }

    SetFocus(topmostButton, playMoveSound: true);
    return true;
  }

  void SetTopMenuFocused(bool isFocused) {
    if (topMenuFocused == isFocused) {
      return;
    }

    topMenuFocused = isFocused;
    MessageBus.Send(TopMenuFocusChangedMessage, topMenuFocused);
  }

  ButtonEntry ResolveButton(GameObject target) {
    if (target == null) {
      return null;
    }

    for (var i = 0; i < buttons.Count; i++) {
      var entry = buttons[i];
      if (entry.button == target || target.transform.IsChildOf(entry.button.transform)) {
        return entry;
      }

      if (IsSingleGearButtonContainerTarget(entry, target.transform)) {
        return entry;
      }
    }

    return null;
  }

  bool IsSingleGearButtonContainerTarget(ButtonEntry entry, Transform target) {
    if (!(entry.group is GearButtons) || entry.button == null || target == null) {
      return false;
    }

    var container = entry.button.transform.parent;
    if (container == null || (target != container && !target.IsChildOf(container))) {
      return false;
    }

    var matchingButtonCount = 0;
    for (var i = 0; i < buttons.Count; i++) {
      var candidate = buttons[i];
      if (candidate.group != entry.group || candidate.button == null ||
          candidate.button.transform.parent != container) {
        continue;
      }

      matchingButtonCount++;
      if (matchingButtonCount > 1) {
        return false;
      }
    }

    return matchingButtonCount == 1;
  }
}
