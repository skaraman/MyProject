using UnityEngine;
using System;
using System.Collections.Generic;

public class AutoSaver : MonoBehaviour {
  public CharacterState characterState;

  private List<Action> actions = new();
  public SaveData gameData = new();

  public bool enableTimeTracking = false;
  private double playtime;
  private float secondTracker;
  private int DELAY_SAVE_TIME = 1;

  void Start() {
    actions.Add(MessageBus.On("saveData", o => SaveAll()));
    playtime = 0;
  }

  void OnDestroy() {
    foreach (var unsub in actions) { unsub(); }
    actions.Clear();
  }

  void FixedUpdate() {
    if (enableTimeTracking) {
      playtime += Time.unscaledDeltaTime;
      secondTracker += Time.unscaledDeltaTime;
      if (secondTracker > DELAY_SAVE_TIME) {
        secondTracker = 0;
        SaveAll();
      }
    }
  }

  public void SetPlaytime(float time) {
    playtime += time;
  }

  void SaveAll() {
    gameData["playtime"] = playtime;
    gameData["level"] = characterState.level;
    gameData["location"] = characterState.locationTracker.location;
    SaveSlotManager.Save("slot", gameData);
  }
}