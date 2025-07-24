
using System;
using System.Collections.Generic;
using UnityEngine;

public class SingleSceneManager : MonoBehaviour {
  public InputProcessor inputProcessor;
  public GameObject Blackscreen;
  private All1AnimatorScript blackscreen;

  public GameObject MainMenu;
  public GameObject LoadMenu;
  public GameObject SettingsMenu;
  public GameObject GameplayInterface;

  public AutoSaver autoSaver;
  public SaveSlotView saveSlotView;

  private List<Action> actions = new();

  private bool init;

  void Start() {
    actions.Add(MessageBus.On("startGame", o => StartGame()));
    actions.Add(MessageBus.On("openLoadMenu", o => OpenLoadMenu()));
    actions.Add(MessageBus.On("openSettingsMenu", o => OpenSettingsMenu()));
    actions.Add(MessageBus.On("backToMainMenu", o => OpenMainMenu()));
    Blackscreen.SetActive(true);
    MainMenu.SetActive(true);
    inputProcessor.SwitchMap("mainMenu");

    blackscreen = Blackscreen.GetComponent<All1AnimatorScript>();
    blackscreen.AddFloatAnim("alphaIn", "_Alpha", 0f, 1f, 2f);
    blackscreen.AddFloatAnim("alphaOut", "_Alpha", 1f, 0f, 2f);

    // controller.Play("glowWide");
    // controller.Play("glowPulse");
    // controller.Stop("glowPulse");
    // controller.Restart("glowWide");

  }

  void Update() {
    if (!init) {
      blackscreen.Play("alphaOut");
      init = true;
    }
  }

  void StartGame() {
    // Save Slot Checker
    if (saveSlotView.SavesCount > 0) {
      var loaded = SaveSlotManager.Load("slot");
      autoSaver.SetPlaytime((float)loaded["playtime"]);
    }
    autoSaver.enableTimeTracking = true;

    // Scene Change Animations
    blackscreen.Play("alphaIn");
    inputProcessor.SwitchMap("gameplay");
    AsyncCoroutine.RunAfterDelay(2, () => {
      MainMenu.SetActive(false);
      blackscreen.Play("alphaOut");
    });
  }

  void OpenLoadMenu() {
    // Scene Change Animations
    inputProcessor.SwitchMap("loadMenu");
    LoadMenu.SetActive(true);
  }

  void OpenSettingsMenu() {
    SettingsMenu.SetActive(true);
  }

  void OpenMainMenu() {
    SettingsMenu.SetActive(false);
    LoadMenu.SetActive(false);

  }

}