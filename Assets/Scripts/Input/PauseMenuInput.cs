using System;
using System.Collections.Generic;
using UnityEngine;

public class PauseMenuInput : MonoBehaviour {
  private int activeHoverIndex = -1;
  private int activeSelectedIndex = 2;
  private int formHoverIndex = -1;
  private List<Action> actions = new();

  public MenuButtons menuButtons;
  public FormButtons formButtons;
  // public ButtonGroup StatsButtons;
  public GearButtons gearButtons;
  // public ButtonGroup AbilityButtons;
  // public ButtonGroup InventoryItems;

  public GameObject MapMenu;
  public GameObject CharacterMenu;
  public GameObject AbilityMenu;
  public GameObject InventoryMenu;
  public GameObject OptionsMenu;
  public List<GameObject> sections = new();
  public List<GameObject> changingUI = new();
  public List<GameObject> primaryUIText = new();
  public List<GameObject> secondaryUIText = new();
  
  void Awake() { }

  void Start() {
    sections.AddRange(new GameObject[] { MapMenu, CharacterMenu, AbilityMenu, InventoryMenu, OptionsMenu });

    actions.Add(MessageBus.On("pauseMenu.LeftTab", o => menuLeft()));
    actions.Add(MessageBus.On("pauseMenu.RightTab", o => menuRight()));
    actions.Add(MessageBus.On("pauseMenu.select", o => select()));
    actions.Add(MessageBus.On("pauseMenu.hover", o => hover(o)));
    actions.Add(MessageBus.On("pauseMenu.unhover", o => unhover()));
    actions.Add(MessageBus.On("pauseMenu.click", o => click(o)));
    actions.Add(MessageBus.On("pauseMenu.cancel", o => cancel()));
    actions.Add(MessageBus.On("pauseMenu.left", o => left()));
    actions.Add(MessageBus.On("pauseMenu.right", o => right()));
    actions.Add(MessageBus.On("pauseMenu.up", o => up()));
    actions.Add(MessageBus.On("pauseMenu.down", o => down()));
  }

  void OnDestroy() {
    for (int i = 0; i < actions.Count; i++) {
      actions[i].Invoke();
    }
    actions.Clear();
  }

  void menuLeft() { 
    activeSelectedIndex -= 1;
    if (activeSelectedIndex < 1) {
      activeSelectedIndex = menuButtons.buttons.Count - 2;
    }
    menuButtons.SetActiveIndex(activeSelectedIndex);
  }

  void menuRight() {
    activeSelectedIndex += 1;
    if (activeSelectedIndex >= menuButtons.buttons.Count - 1) {
      activeSelectedIndex = 1;
    }
    menuButtons.SetActiveIndex(activeSelectedIndex);
  }

  void select() {
    if (activeHoverIndex != -1) {
      activeSelectedIndex = activeHoverIndex;
      if (menuButtons.activeIndex == activeSelectedIndex) return;
      menuButtons.SetActiveIndex(activeSelectedIndex);
      foreach (var sect in sections) {
        sect.SetActive(false);
      }
      sections[activeSelectedIndex - 1].SetActive(true);
    }
    if (formHoverIndex != -1) {
      if (formButtons.activeIndex == formHoverIndex) return;
      formButtons.SetActiveIndex(formHoverIndex);
      foreach (var ui in changingUI) {
        ui.GetComponent<SpriteWithNormals>().labelPrefix = formButtons.buttons[formHoverIndex].name.ToLower();
        ui.GetComponent<SpriteWithNormals>().ForceUpdateSpriteAndNormal();
      }
      foreach (var ui in primaryUIText) {
        ui.GetComponent<AllIn1AnimatorInspector>().SetKeyword("_Color", true);
        ui.GetComponent<AllIn1AnimatorInspector>().AddColorSequence("_Color",
          ShaderColors.myColors[
            ShaderColors.pairs[formButtons.buttons[formHoverIndex].name]["primary"]["color"]],
          ShaderColors.myColors[
            ShaderColors.pairs[formButtons.buttons[formHoverIndex].name]["primary"]["color"]],
          1f
        );
      }
      foreach (var ui in secondaryUIText) {
        ui.GetComponent<AllIn1AnimatorInspector>().SetKeyword("_Color", true);
        ui.GetComponent<AllIn1AnimatorInspector>().AddColorSequence("_Color",
          ShaderColors.myColors[
            ShaderColors.pairs[formButtons.buttons[formHoverIndex].name]["secondary"]["color"]],
          ShaderColors.myColors[
            ShaderColors.pairs[formButtons.buttons[formHoverIndex].name]["secondary"]["color"]],
          1f
        );
      }
      gearButtons.OnGearReady(formButtons.buttons[formHoverIndex].name);
    }
  }

  void hover(object target) {
    // Implement hover logic here
    activeHoverIndex = menuButtons.buttons.IndexOf((GameObject)target);
    if (activeHoverIndex >= 0) {
      if (menuButtons.hoverIndex == activeHoverIndex) return;
      menuButtons.SetHoverIndex(activeHoverIndex);
    }
    formHoverIndex = formButtons.buttons.IndexOf((GameObject)target);
    if (formHoverIndex >= 0) {
      if (formButtons.hoverIndex == formHoverIndex) return;
      formButtons.SetHoverIndex(formHoverIndex);
    }
  }

  void unhover() {
    menuButtons.SetHoverIndex(-1);
    activeHoverIndex = -1;
    formButtons.SetHoverIndex(-1);
    
  }

  void click(object target) {
    activeHoverIndex = menuButtons.buttons.IndexOf((GameObject)target);
    if (activeHoverIndex == -1) {
      if (activeSelectedIndex == -1) return;
      formHoverIndex = formButtons.buttons.IndexOf((GameObject)target);
      if (formHoverIndex == -1) return;
      if (formHoverIndex >= 0) {
        select();
        return;
      } 
    }
    if (activeHoverIndex == 0) {
      menuLeft();
    }
    if (activeHoverIndex == 6) {
      menuRight();
    }
    if (activeHoverIndex > 0 && activeHoverIndex < 6) {
      select();
    }
    
  }



  void cancel() { }

  void left() { }

  void right() { }

  void up() { }

  void down() { }

}

