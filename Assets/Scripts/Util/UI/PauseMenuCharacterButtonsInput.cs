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
  GearButtons gearButtons;
  bool topMenuFocused = true;

  public bool HasFocusedButton => focusedButton != null ||
    (gearButtons != null && gearButtons.IsChoiceWindowOpen);

  void OnEnable() {
    EnsureViewsResolved(force: true);
    ShowAllStatsView(false);
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
      EnsureViewsResolved(force: true);
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
    actions.Add(MessageBus.On(ButtonSelectedMessage, OnCharacterButtonSelected));
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
        if (button == null || !button.activeInHierarchy || ContainsButton(button)) {
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

  public void RefreshButtons() {
    EnsureViewsResolved();
    RebuildButtons();
    if (focusedButton != null) {
      hoveredGroup = focusedButton.group;
      focusedButton.group.SetHoverIndex(focusedButton.groupIndex);
      focusedButton.group.SetActiveIndex(focusedButton.groupIndex);
      return;
    }

    if (hoveredGroup != null) {
      hoveredGroup.SetHoverIndex(-1);
      hoveredGroup = null;
    }
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

    var spriteRenderer = button.GetComponent<SpriteRenderer>() ??
                         button.GetComponentInChildren<SpriteRenderer>(includeInactive: true);
    if (spriteRenderer == null || spriteRenderer.sprite == null) {
      return;
    }

    var collider = button.AddComponent<BoxCollider2D>();
    collider.isTrigger = true;
    collider.offset = spriteRenderer.sprite.bounds.center;
    collider.size = spriteRenderer.sprite.bounds.size;
  }

  void OnHover(object payload) {
    if (gearButtons != null && gearButtons.IsChoiceWindowOpen) {
      gearButtons.TryHandleChoiceHover(payload as GameObject);
      return;
    }

    var entry = ResolveButton(payload as GameObject);
    if (entry == null) {
      ClearHover();
      return;
    }

    SetFocus(entry, playMoveSound: true);
  }

  void OnClick(object payload) {
    if (gearButtons != null && gearButtons.IsChoiceWindowOpen) {
      gearButtons.TryHandleChoiceClick(payload as GameObject);
      return;
    }

    var entry = ResolveButton(payload as GameObject);
    if (entry == null) {
      return;
    }

    SetFocus(entry, playMoveSound: true);
    MessageBus.Send(ButtonSelectedMessage, entry.button);
  }

  void MoveFocus(Vector2 direction) {
    if (gearButtons != null && gearButtons.IsChoiceWindowOpen) {
      gearButtons.TryMoveChoice(direction);
      return;
    }

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

    var currentY = GetNavigationPosition(current.button).y;
    for (var i = 0; i < buttons.Count; i++) {
      var candidate = buttons[i];
      if (candidate == null || candidate.button == null || candidate == current) {
        continue;
      }

      if (GetNavigationPosition(candidate.button).y > currentY + 0.01f) {
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
      var score = -Vector2.Dot(GetNavigationPosition(candidate.button), direction);
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
    var currentPosition = GetNavigationPosition(current.button);
    for (var i = 0; i < buttons.Count; i++) {
      var candidate = buttons[i];
      if (candidate == current) {
        continue;
      }

      var offset = GetNavigationPosition(candidate.button) - currentPosition;
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

  static Vector2 GetNavigationPosition(GameObject button) {
    if (button == null) {
      return Vector2.zero;
    }

    var collider = button.GetComponent<Collider2D>();
    if (collider != null) {
      return collider.bounds.center;
    }

    var renderer = button.GetComponent<SpriteRenderer>() ??
                   button.GetComponentInChildren<SpriteRenderer>(includeInactive: true);
    return renderer != null ? (Vector2)renderer.bounds.center : (Vector2)button.transform.position;
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

    if (focusedButton != null &&
        focusedButton.group != entry.group &&
        !ShouldPreservePreviousGroupSelection(focusedButton.group, entry.group)) {
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

  static bool ShouldPreservePreviousGroupSelection(ButtonGroup previous, ButtonGroup next) {
    return previous is InventoryButtons && next is ItemButtons;
  }

  void SelectFocusedButton() {
    if (gearButtons != null && gearButtons.IsChoiceWindowOpen) {
      gearButtons.TrySelectChoice();
      return;
    }

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
    if (gearButtons != null && gearButtons.IsChoiceWindowOpen) {
      gearButtons.ClearChoiceHover();
      return;
    }

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

    if (!(focusedButton.group is InventoryButtons)) {
      focusedButton.group.SetActiveIndex(-1);
    }
    focusedButton = null;
  }

  public void FocusTopMenu() {
    TryCancelGearChoice();
    ClearHover();
    ClearFocus();
    SetTopMenuFocused(true);
  }

  public bool TryCancelGearChoice() {
    return gearButtons != null && gearButtons.TryCancelChoice();
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

  GameObject statsView;
  GameObject allStatsView;
  GameObject openAllStatsButton;
  GameObject closeAllStatsButton;

  void EnsureViewsResolved(bool force = false) {
    if (force || gearButtons == null) {
      gearButtons = GetComponentInChildren<GearButtons>(includeInactive: true);
    }
    if (force || statsView == null) {
      statsView = FindChildRecursive(transform, "StatsView")?.gameObject;
    }
    if (force || allStatsView == null) {
      allStatsView = FindChildRecursive(transform, "AllStatsView")?.gameObject;
    }
    if (force || openAllStatsButton == null) {
      var btn = statsView != null ? FindChildRecursive(statsView.transform, "levelNumber") : null;
      btn ??= FindChildRecursive(transform, "levelNumber");
      openAllStatsButton = btn != null ? btn.gameObject : null;
    }
    if (force || closeAllStatsButton == null) {
      var btn = allStatsView != null ? FindChildRecursive(allStatsView.transform, "CloseAllStats") : null;
      btn ??= FindChildRecursive(transform, "CloseAllStats");
      closeAllStatsButton = btn != null ? btn.gameObject : null;
    }

    EnsureViewButtonsRegistered();
  }

  void EnsureViewButtonsRegistered() {
    if (statsView != null && openAllStatsButton != null) {
      var statsButtons = statsView.GetComponentInChildren<StatsButtons>(includeInactive: true);
      if (statsButtons == null) {
        statsButtons = statsView.AddComponent<StatsButtons>();
      }
      if (!statsButtons.buttons.Contains(openAllStatsButton)) {
        statsButtons.buttons.Add(openAllStatsButton);
      }
    }

    if (allStatsView != null && closeAllStatsButton != null) {
      var allStatsButtons = allStatsView.GetComponentInChildren<StatsButtons>(includeInactive: true);
      if (allStatsButtons == null) {
        allStatsButtons = allStatsView.AddComponent<StatsButtons>();
      }
      if (!allStatsButtons.buttons.Contains(closeAllStatsButton)) {
        allStatsButtons.buttons.Add(closeAllStatsButton);
      }
    }
  }

  void OnCharacterButtonSelected(object payload) {
    if (!isActiveAndEnabled) {
      return;
    }

    var buttonObject = payload as GameObject;
    if (buttonObject == null) {
      return;
    }

    EnsureViewsResolved();
    if (MatchesButtonTarget(buttonObject, openAllStatsButton)) {
      ShowAllStatsView(true);
    }
    else if (MatchesButtonTarget(buttonObject, closeAllStatsButton)) {
      ShowAllStatsView(false);
    }
  }

  void ShowAllStatsView(bool showAllStats) {
    EnsureViewsResolved();

    if (statsView != null) {
      statsView.SetActive(!showAllStats);
    }
    if (allStatsView != null) {
      allStatsView.SetActive(showAllStats);
    }

    RebuildButtons();
    ClearHover();
    ClearFocus();

    RuntimeLog.Log(
      "[PauseMenuCharacterButtonsInput] ShowAllStatsView showAllStats=" + (showAllStats ? 1 : 0) +
      " statsView=" + ((statsView != null && statsView.activeSelf) ? 1 : 0) +
      " allStatsView=" + ((allStatsView != null && allStatsView.activeSelf) ? 1 : 0)
    );
  }

  static bool MatchesButtonTarget(GameObject target, GameObject button) {
    if (target == null || button == null) {
      return false;
    }

    if (target == button) {
      return true;
    }

    return target.transform.IsChildOf(button.transform);
  }

  static Transform FindChildRecursive(Transform root, string targetName) {
    if (root == null || string.IsNullOrWhiteSpace(targetName)) {
      return null;
    }

    if (string.Equals(root.name, targetName, StringComparison.OrdinalIgnoreCase)) {
      return root;
    }

    for (var i = 0; i < root.childCount; i++) {
      var result = FindChildRecursive(root.GetChild(i), targetName);
      if (result != null) {
        return result;
      }
    }

    return null;
  }
}
