using UnityEngine;
using System;
using System.Collections.Generic;

public class AutoSaver : MonoBehaviour {
  public CharacterState characterState;
  public FontText scenePlaytimeTracker;

  private List<Action> actions = new();
  
  public bool enableTimeTracking = false;
  private int playtimeHours;
  private int playtimeMinutes;
  private int playtimeSeconds;
  private float secondTracker;
  private int DELAY_SAVE_TIME = 1;
  private int wholeSeconds;

  private SaveData gameData = new();

  void Start() {
    actions.Add(MessageBus.On("saveData", o => SaveAll()));
    playtimeHours = 0;
    playtimeMinutes = 0;
    playtimeSeconds = 0;
    secondTracker = 0;
  }

  void OnDestroy() {
    foreach (var unsub in actions) unsub();
    actions.Clear();
  }

  void FixedUpdate() {
    if (!enableTimeTracking) return;
    secondTracker += Time.unscaledDeltaTime;
    if (secondTracker >= 1f) {
      wholeSeconds = Mathf.FloorToInt(secondTracker);
      AddSeconds(wholeSeconds);
      if (secondTracker > DELAY_SAVE_TIME) {
        secondTracker = 0;
        SaveAll();
      }
      var newHours =  $"{playtimeHours}";
      var newMinutes = $"{playtimeMinutes}";
      var newSeconds = $"{playtimeSeconds}";
      if (playtimeHours < 10) newHours = $"0{playtimeHours}";
      if (playtimeMinutes < 10) newMinutes = $"0{playtimeMinutes}";
      if (playtimeSeconds < 10) newSeconds = $"0{playtimeSeconds}";
      scenePlaytimeTracker.content = $"{newHours}:{newMinutes}:{newSeconds}";
    }
  }

  public void SetPlaytime(int hours, int minutes, int seconds) {
    playtimeHours = hours;
    playtimeMinutes = minutes;
    playtimeSeconds = seconds;

  }

  void AddSeconds(int seconds) {
    playtimeSeconds += seconds;
    if (playtimeSeconds >= 60) {
      playtimeMinutes += playtimeSeconds / 60;
      playtimeSeconds %= 60;
    }
    if (playtimeMinutes >= 60) {
      playtimeHours += playtimeMinutes / 60;
      playtimeMinutes %= 60;
    }
  }

  void SaveAll() {
    gameData["playtimeHours"] = playtimeHours;
    gameData["playtimeMinutes"] = playtimeMinutes;
    gameData["playtimeSeconds"] = playtimeSeconds;
    gameData["level"] = characterState.level;
    gameData["location"] = characterState.locationTracker.location;
    SaveSlotManager.Save("slot", gameData);
  }
}
