
using System;
using System.Collections.Generic;
using UnityEngine;

public class SingleSceneManager : MonoBehaviour {
  public InputProcessor inputProcessor;
  public MouseManager mouseManager;
  public GameObject Blackscreen;
  private All1AnimatorScript blackscreen;

  public GameObject MainMenu;
  public GameObject LoadMenu;
  public GameObject SettingsMenu;
  public GameObject GameplayInterface;
  public GameObject PauseMenu;

  public AutoSaver autoSaver;
  public SaveSlotView saveSlotView;

  private List<Action> actions = new();

  private bool init;

  void Start() {
    actions.Add(MessageBus.On("startGame", o => StartGame()));
    actions.Add(MessageBus.On("openLoadMenu", o => OpenLoadMenu()));
    actions.Add(MessageBus.On("openSettingsMenu", o => OpenSettingsMenu()));
    actions.Add(MessageBus.On("backToMainMenu", o => OpenMainMenu()));

    actions.Add(MessageBus.On("closePauseMenu", o => OpenGameplay()));
    actions.Add(MessageBus.On("openPauseMenu", o => OpenPauseMenu()));
    
    Blackscreen.SetActive(true);
    MainMenu.SetActive(true);
    _SwitchMap("mainMenu");

    blackscreen = Blackscreen.GetComponent<All1AnimatorScript>();
    blackscreen.AddFloatAnim("alphaIn", "_Alpha", 0f, 1f, 2f);
    blackscreen.AddFloatAnim("alphaOut", "_Alpha", 1f, 0f, 2f);

  }

  void Update() {
    if (!init) {
      blackscreen.Play("alphaOut");
      init = true;
    }
  }



  void StartGame() {
    if (!_isNewGame()) {
      var loaded = SaveSlotManager.Load("slot");
      autoSaver.SetPlaytime((int)loaded["playtimeHours"], (int)loaded["playtimeMinutes"], (int)loaded["playtimeSeconds"]);
    }
    autoSaver.enableTimeTracking = true;
    _SwitchMap("none");
    
    blackscreen.Play("alphaIn");

    AsyncCoroutine.RunAfterDelay(2, () => {
      blackscreen.Play("alphaOut");
      MainMenu.SetActive(false);
      GameplayInterface.SetActive(true);
      LoadMenu.SetActive(false);
      _SwitchMap("gameplay");
    });
  }

  void OpenLoadMenu() {
    _SwitchMap("loadMenu");
    MainMenu.SetActive(false);
    SettingsMenu.SetActive(false);
    LoadMenu.SetActive(true);
  }

  void OpenSettingsMenu() {
    _SwitchMap("settingsMenu");

    MainMenu.SetActive(false);
    SettingsMenu.SetActive(true);
    GameplayInterface.SetActive(false);
  }

  void OpenGameplay() {
    _SwitchMap("gameplay");

    MainMenu.SetActive(false);
    SettingsMenu.SetActive(false);
    GameplayInterface.SetActive(true);
    PauseMenu.SetActive(false);
  }

  void OpenMainMenu() {
    _SwitchMap("mainMenu");

    SettingsMenu.SetActive(false);
    LoadMenu.SetActive(false);
    MainMenu.SetActive(true);
  }

  void OpenPauseMenu() {
    _SwitchMap("pauseMenu");

    PauseMenu.SetActive(true);
    GameplayInterface.SetActive(false);

  }

  private void _SwitchMap(string map) {
    inputProcessor.SwitchMap(map);
    mouseManager.SwitchMap(map);   
  }

  private bool _isNewGame() {
    return SaveSlotManager.slot > saveSlotView.SavesCount;
  }
}