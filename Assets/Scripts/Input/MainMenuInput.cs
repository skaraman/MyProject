using System;
using System.Collections.Generic;
using UnityEngine;

public class MainMenuInput : ButtonGroup {
  private int activeIndexMainMenu = -1;
  private List<Action> actions = new();
  public SaveSlotView saveSlotView;

  void Start() {
    actions.Add(MessageBus.On("mainMenu.up", o => MenuUp()));
    actions.Add(MessageBus.On("mainMenu.down", o => MenuDown()));
    actions.Add(MessageBus.On("mainMenu.select", o => MenuSelect()));
    actions.Add(MessageBus.On("mainMenu.hover", o => MouseHover(o)));
    actions.Add(MessageBus.On("mainMenu.click", o => MenuSelect()));
  }

  void OnDestroy() {
    for (int i = 0; i < actions.Count; i++) {
      actions[i].Invoke();
    }
    actions.Clear();
  }

  public void MenuUp() { 
    if (activeIndexMainMenu < 0) {
      activeIndexMainMenu = 0;
    } 
    else {
      activeIndexMainMenu -= 1;
    }
    if (activeIndexMainMenu < 0) {
      activeIndexMainMenu = buttons.Count - 1;
    }
    SetActiveIndex(activeIndexMainMenu);
  }

  public void MenuDown() {
    if (activeIndexMainMenu < 0) {
      activeIndexMainMenu = 0;
    }
    else {
      activeIndexMainMenu += 1;
    }
    if (activeIndexMainMenu >= buttons.Count) {
      activeIndexMainMenu = 0;
    }
    SetActiveIndex(activeIndexMainMenu);
  }

  public void MouseHover(object target) {
    activeIndexMainMenu = buttons.IndexOf((GameObject)target);
    SetActiveIndex(activeIndexMainMenu);
  }

  public void MenuSelect() {
    if (activeIndexMainMenu == 0) {
      MessageBus.Send("startGame");
    }
    else if (activeIndexMainMenu == 1) {
      MessageBus.Send("openLoadMenu");
    }
    else if (activeIndexMainMenu == 2) {
      MessageBus.Send("openSettingsMenu");
    }
  }

  protected override void HandleActiveState(GameObject button) {
    var shader = button.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(0);
    shader.SetKeyword("OUTBASE_ON", true);
  }
  
  protected override void HandleInactiveState(GameObject button) {
    var shader = button.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(0);
    shader.SetKeyword("OUTBASE_ON", false);
  }   
}

