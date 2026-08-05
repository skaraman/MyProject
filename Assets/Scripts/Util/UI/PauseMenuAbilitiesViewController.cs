using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PauseMenuAbilitiesViewController : MonoBehaviour {
  const string HighlightKeyword = "OUTBASE_ON";

  readonly List<Action> actions = new();

  GameObject switchObject;
  GameObject abilitiesRoot;
  GameObject combosRoot;
  GameObject abilitiesLabel;
  GameObject combosLabel;
  AllIn1AnimatorInspector switchHighlight;
  bool showingCombos;
  bool switchFocused;
  bool switchHovered;
  int ignoreDirectionsThroughFrame = -1;

  public bool ShowingCombos => showingCombos;

  void OnEnable() {
    ResolveHierarchy();
    EnsureSwitchCollider();
    RegisterHandlers();

    var authoredCombosState = combosRoot != null && combosRoot.activeSelf;
    ApplyView(authoredCombosState, playSound: false);
  }

  void OnDisable() {
    switchFocused = false;
    switchHovered = false;
    ApplySwitchHighlight();
    UnregisterHandlers();
  }

  void ResolveHierarchy() {
    switchObject = FindDirectChild("switch");
    abilitiesRoot = FindDirectChild("ABILITIES");
    combosRoot = FindDirectChild("COMBOS");

    var labelRoot = FindDirectChild("label");
    abilitiesLabel = FindLabelByContent(labelRoot, "Abilities");
    combosLabel = FindLabelByContent(labelRoot, "Combos");

    if (switchObject != null) {
      switchHighlight = switchObject.GetComponent<AllIn1AnimatorInspector>();
      if (switchHighlight == null) {
        switchHighlight = switchObject.AddComponent<AllIn1AnimatorInspector>();
      }
    }
  }

  void EnsureSwitchCollider() {
    if (switchObject == null || switchObject.GetComponent<Collider2D>() != null) return;

    var renderer = switchObject.GetComponent<SpriteRenderer>();
    if (renderer == null) return;

    var collider = switchObject.AddComponent<BoxCollider2D>();
    if (renderer.sprite != null) {
      var spriteBounds = renderer.sprite.bounds;
      collider.offset = new Vector2(spriteBounds.center.x, spriteBounds.center.y);
      collider.size = new Vector2(spriteBounds.size.x, spriteBounds.size.y);
      return;
    }

    var min = switchObject.transform.InverseTransformPoint(renderer.bounds.min);
    var max = switchObject.transform.InverseTransformPoint(renderer.bounds.max);
    collider.offset = (min + max) * 0.5f;
    collider.size = new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y));
  }

  void RegisterHandlers() {
    if (actions.Count > 0) return;

    actions.Add(MessageBus.On("pauseMenu.hover", OnHover));
    actions.Add(MessageBus.On("pauseMenu.unhover", _ => OnUnhover()));
    actions.Add(MessageBus.On("pauseMenu.click", OnClick));
    actions.Add(MessageBus.On("pauseMenu.select", value => {
      if (InputMessageValue.IsPressed(value)) OnSelect();
    }));
    actions.Add(MessageBus.On("pauseMenu.left", value => {
      if (InputMessageValue.IsPressed(value)) MoveHorizontal(-1);
    }));
    actions.Add(MessageBus.On("pauseMenu.right", value => {
      if (InputMessageValue.IsPressed(value)) MoveHorizontal(1);
    }));
    actions.Add(MessageBus.On("pauseMenu.up", value => {
      if (InputMessageValue.IsPressed(value)) MoveUp();
    }));
    actions.Add(MessageBus.On("pauseMenu.down", value => {
      if (InputMessageValue.IsPressed(value)) MoveDown();
    }));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  public void FocusSwitch() {
    if (!isActiveAndEnabled || switchObject == null) return;

    switchFocused = true;
    ignoreDirectionsThroughFrame = Time.frameCount;
    ApplySwitchHighlight();
    MessageBus.Send(PauseMenuCharacterButtonsInput.TopMenuFocusChangedMessage, false);
  }

  void OnHover(object payload) {
    if (!IsSwitchTarget(payload as GameObject)) return;
    switchHovered = true;
    ApplySwitchHighlight();
  }

  void OnUnhover() {
    if (!switchHovered) return;
    switchHovered = false;
    ApplySwitchHighlight();
  }

  void OnClick(object payload) {
    if (!IsSwitchTarget(payload as GameObject)) return;
    ApplyView(!showingCombos, playSound: true);
  }

  void OnSelect() {
    if (!switchFocused) return;
    ApplyView(!showingCombos, playSound: true);
  }

  void MoveHorizontal(int direction) {
    if (!switchFocused || Time.frameCount <= ignoreDirectionsThroughFrame) return;
    ApplyView(direction > 0, playSound: true);
  }

  void MoveUp() {
    if (!switchFocused || Time.frameCount <= ignoreDirectionsThroughFrame) return;

    switchFocused = false;
    ApplySwitchHighlight();
    MessageBus.Send(PauseMenuCharacterButtonsInput.TopMenuFocusChangedMessage, true);
  }

  void MoveDown() {
    if (!switchFocused || Time.frameCount <= ignoreDirectionsThroughFrame || !showingCombos) return;

    var combo = combosRoot != null
      ? combosRoot.GetComponentInChildren<ComboChoiceController>()
      : null;
    if (combo == null) return;

    switchFocused = false;
    ApplySwitchHighlight();
    combo.FocusFirstItem();
  }

  void ApplyView(bool showCombos, bool playSound) {
    var changed = showingCombos != showCombos;
    showingCombos = showCombos;

    SetActive(abilitiesRoot, !showingCombos);
    SetActive(combosRoot, showingCombos);
    SetActive(abilitiesLabel, !showingCombos);
    SetActive(combosLabel, showingCombos);

    if (playSound && changed) {
      MessageBus.Send(SoundEffectPlayer.PlayMessage, "menu.move");
    }
    MouseManager.Instance?.RefreshHoverTarget();
  }

  void ApplySwitchHighlight() {
    switchHighlight?.SetKeyword(HighlightKeyword, switchFocused || switchHovered);
  }

  bool IsSwitchTarget(GameObject target) {
    return target != null && switchObject != null &&
           (target == switchObject || target.transform.IsChildOf(switchObject.transform));
  }

  GameObject FindDirectChild(string childName) {
    for (var i = 0; i < transform.childCount; i++) {
      var child = transform.GetChild(i);
      if (string.Equals(child.name, childName, StringComparison.Ordinal)) {
        return child.gameObject;
      }
    }
    return null;
  }

  static GameObject FindLabelByContent(GameObject labelRoot, string content) {
    if (labelRoot == null) return null;

    var labels = labelRoot.GetComponentsInChildren<FontText>(includeInactive: true);
    for (var i = 0; i < labels.Length; i++) {
      var label = labels[i];
      if (label != null && string.Equals(label.content, content, StringComparison.OrdinalIgnoreCase)) {
        return label.gameObject;
      }
    }
    return null;
  }

  static void SetActive(GameObject target, bool active) {
    if (target != null && target.activeSelf != active) {
      target.SetActive(active);
    }
  }
}
