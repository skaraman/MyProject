using System;
using System.Collections.Generic;
using UnityEngine;

public class MainMenuInput : ButtonGroup {
  private const int LoadButtonInsertIndex = 1;
  private const string MenuMoveSoundId = "menu.move";

  private int activeIndexMainMenu = -1;
  private readonly List<Action> actions = new();

  [SerializeField] private GameObject newGameButton;
  [SerializeField] private GameObject settingsButton;
  [SerializeField] private GameObject loadGameButton;

  public SaveSlotView saveSlotView;

  void Start() {
    ResolveButtonReferences();
    actions.Add(MessageBus.On("mainMenu.up", o => { if (InputMessageValue.IsPressed(o)) MenuUp(); }));
    actions.Add(MessageBus.On("mainMenu.down", o => { if (InputMessageValue.IsPressed(o)) MenuDown(); }));
    actions.Add(MessageBus.On("mainMenu.select", o => { if (InputMessageValue.IsPressed(o)) MenuSelect(); }));
    actions.Add(MessageBus.On("mainMenu.hover", o => MouseHover(o)));
    actions.Add(MessageBus.On("mainMenu.click", o => MenuSelect()));
  }

  void OnDestroy() {
    for (int i = 0; i < actions.Count; i++) {
      actions[i].Invoke();
    }
    actions.Clear();
  }

  public void SetLoadButtonState(GameObject button, bool enabled) {
    if (button == null) return;

    loadGameButton = button;
    buttons.Remove(loadGameButton);

    if (enabled) {
      var index = Mathf.Clamp(LoadButtonInsertIndex, 0, buttons.Count);
      buttons.Insert(index, loadGameButton);
    }

    ClampActiveIndex();
  }

  public void MenuUp() {
    if (buttons.Count == 0) return;

    if (activeIndexMainMenu < 0) {
      activeIndexMainMenu = 0;
    }
    else {
      activeIndexMainMenu -= 1;
    }
    if (activeIndexMainMenu < 0) {
      activeIndexMainMenu = buttons.Count - 1;
    }
    SetActiveIndexWithSound(activeIndexMainMenu);
  }

  public void MenuDown() {
    if (buttons.Count == 0) return;

    if (activeIndexMainMenu < 0) {
      activeIndexMainMenu = 0;
    }
    else {
      activeIndexMainMenu += 1;
    }
    if (activeIndexMainMenu >= buttons.Count) {
      activeIndexMainMenu = 0;
    }
    SetActiveIndexWithSound(activeIndexMainMenu);
  }

  public void MouseHover(object target) {
    if (!(target is GameObject targetButton)) return;

    var resolvedIndex = ResolveButtonIndex(targetButton);
    if (resolvedIndex < 0 || resolvedIndex == activeIndexMainMenu) return;

    activeIndexMainMenu = resolvedIndex;
    SetActiveIndexWithSound(activeIndexMainMenu);
  }

  public void MenuSelect() {
    if (activeIndexMainMenu < 0 || activeIndexMainMenu >= buttons.Count) return;

    var selectedButton = buttons[activeIndexMainMenu];
    if (selectedButton == null) return;

    if (selectedButton == newGameButton || selectedButton.name.Equals("New Game", StringComparison.OrdinalIgnoreCase)) {
      var newSlot = SaveSlotManager.ResolveNextAvailableSlot();
      SaveSlotManager.SetSlot(newSlot);
      MessageBus.Send("startGame");
      return;
    }

    if (selectedButton == loadGameButton || selectedButton.name.Equals("Load Game", StringComparison.OrdinalIgnoreCase)) {
      MessageBus.Send("openLoadMenu");
      return;
    }

    if (selectedButton == settingsButton || selectedButton.name.Equals("Settings", StringComparison.OrdinalIgnoreCase)) {
      MessageBus.Send("openSettingsMenu");
    }
  }

  void ResolveButtonReferences() {
    if (newGameButton == null) {
      newGameButton = FindButtonByName("New Game");
    }

    if (settingsButton == null) {
      settingsButton = FindButtonByName("Settings");
    }

    if (loadGameButton == null && saveSlotView != null) {
      loadGameButton = saveSlotView.loadButton;
    }
  }

  GameObject FindButtonByName(string buttonName) {
    for (int i = 0; i < buttons.Count; i++) {
      var button = buttons[i];
      if (button == null) continue;
      if (button.name.Equals(buttonName, StringComparison.OrdinalIgnoreCase)) {
        return button;
      }
    }
    return null;
  }

  int ResolveButtonIndex(GameObject targetButton) {
    if (targetButton == null) return -1;

    var directIndex = buttons.IndexOf(targetButton);
    if (directIndex >= 0) return directIndex;

    var targetTransform = targetButton.transform;
    for (int i = 0; i < buttons.Count; i++) {
      var button = buttons[i];
      if (button == null) continue;
      if (targetTransform.IsChildOf(button.transform)) {
        return i;
      }
    }

    return -1;
  }

  void ClampActiveIndex() {
    if (buttons.Count <= 0) {
      activeIndexMainMenu = -1;
      SetActiveIndex(-1);
      return;
    }

    if (activeIndexMainMenu < 0) return;

    if (activeIndexMainMenu >= buttons.Count) {
      activeIndexMainMenu = buttons.Count - 1;
      SetActiveIndex(activeIndexMainMenu);
    }
  }

  void SetActiveIndexWithSound(int index) {
    var previousIndex = activeIndex;
    SetActiveIndex(index);
    if (activeIndex == previousIndex) {
      return;
    }

    MessageBus.Send(SoundEffectPlayer.PlayMessage, MenuMoveSoundId);
  }

  protected override void HandleActiveState(GameObject button) {
    var shader = button.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(0);
    shader.SetKeyword("OUTBASE_ON", true);
    shader.SetKeyword("SHINE_ON", true);
  }

  protected override void HandleInactiveState(GameObject button) {
    var shader = button.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(0);
    shader.SetKeyword("OUTBASE_ON", false);
    shader.SetKeyword("SHINE_ON", false);
  
  }
}

