using UnityEngine;
using System.Collections.Generic;
using System;

public class CharacterState : MonoBehaviour {
  public LocationTracker locationTracker = new();
  public int level = 0;

  private SaveData gameData = new();
  private Action offLoadGame;
  GearController gearController;

  void Start() {
    offLoadGame = MessageBus.On("loadGame", o => LoadState());
    gearController = GetComponent<GearController>();
  }

  void OnDestroy() {
    offLoadGame?.Invoke();
  }

  public void LoadState() {
    var loadedForms = SaveSlotManager.Load("forms");
    var loadedStats = SaveSlotManager.Load("stats");
    if (loadedForms.Keys.Count != 0) {
      EsperanzaForms.SetActive((string)loadedForms["activeForm"]);
      foreach (var item in (Dictionary<string, int>)loadedForms["unlockedForms"]) {
        if (item.Value == 1) EsperanzaForms.UnlockForm(item.Key);
      }
    }
    if (loadedStats.Keys.Count != 0) {
      foreach (var form in (Dictionary<string, Dictionary<string, int>>)loadedStats["formStats"]) {
        foreach (var stat in form.Value) {
          FormStatsValues.values[form.Key][stat.Key] = stat.Value;
        }
      }
    }
    gearController.LoadGear();
    GatherAllStatValues();
  }

  public void AddStats(string form, string stat, int amount) {
    // float oldAmount = stats[form][stat];
    FormStatsValues.values[form][stat] += amount;
    GatherAllStatValues();
    gameData.SetComplex("formStats", FormStatsValues.values);
    SaveSlotManager.Save("stats", gameData);
  }

  public void GatherAllStatValues() {
    level = 0;
    var keys = new List<string>(AllStatValues.allStats.Keys);
    for (int i = 0; i < keys.Count; i++) {
      var key = keys[i];
      AllStatValues.allStats[key] = 0f;
    }
    foreach (var form in FormStatIncreases.increases) {
      foreach (var majorStat in form.Value) {
        foreach (var minorStat in majorStat.Value) {
          AllStatValues.allStats[minorStat.Key] += minorStat.Value * FormStatsValues.values[form.Key][majorStat.Key];
        }
      }
    }
    foreach (var form in FormStatsValues.values) {
      foreach (var majorStat in form.Value) {
        level += majorStat.Value;
      }
    }
  }


}
