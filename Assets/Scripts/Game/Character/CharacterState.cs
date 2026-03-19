using UnityEngine;
using System.Collections.Generic;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CharacterState : MonoBehaviour {
  public int level = 0;

  private SaveData gameData = new();
  private Action offLoadGame;
  GearController gearController;

  // Cache list to avoid allocations
  private List<string> cachedKeys = new List<string>();

  static bool ShouldLogLoadStateDebug() {
    return Application.isEditor || Debug.isDebugBuild;
  }

  void EnsureRuntimeReferences() {
    gearController ??= GetComponent<GearController>();
  }

  void Start() {
    offLoadGame = MessageBus.On("loadGame", o => LoadState());
    EnsureRuntimeReferences();
  }

  void OnDestroy() {
    offLoadGame?.Invoke();
#if UNITY_EDITOR
    if (!Application.isPlaying) {
      Selection.activeObject = null;
    }
#endif
  }

  public void LoadState() {
    EnsureRuntimeReferences();
    var loadedForms = SaveSlotManager.Load("forms");
    var loadedStats = SaveSlotManager.Load("stats");
    if (ShouldLogLoadStateDebug()) {
      Debug.Log(
        "[CharacterState][LoadState] stage=begin" +
        " slot=" + SaveSlotManager.slot +
        " forms_keys=" + (loadedForms != null ? loadedForms.Count : 0) +
        " stats_keys=" + (loadedStats != null ? loadedStats.Count : 0) +
        " gear_controller=" + (gearController != null ? 1 : 0)
      );
    }
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
    if (ShouldLogLoadStateDebug()) {
      var activeForm = loadedForms != null && loadedForms.ContainsKey("activeForm")
        ? Convert.ToString(loadedForms["activeForm"])
        : "";
      Debug.Log(
        "[CharacterState][LoadState] stage=complete" +
        " slot=" + SaveSlotManager.slot +
        " active_form=" + (string.IsNullOrWhiteSpace(activeForm) ? "-" : activeForm.Trim()) +
        " level=" + level +
        " gear_controller=" + (gearController != null ? 1 : 0)
      );
    }
  }

  public void InitializeRuntimeStateForNewGame() {
    EnsureRuntimeReferences();
    if (ShouldLogLoadStateDebug()) {
      Debug.Log(
        "[CharacterState][LoadState] stage=new_game_begin" +
        " slot=" + SaveSlotManager.slot +
        " active_form=" + EsperanzaForms.GetActive() +
        " gear_controller=" + (gearController != null ? 1 : 0)
      );
    }

    gearController?.LoadGear();
    GatherAllStatValues();

    if (ShouldLogLoadStateDebug()) {
      Debug.Log(
        "[CharacterState][LoadState] stage=new_game_complete" +
        " slot=" + SaveSlotManager.slot +
        " active_form=" + EsperanzaForms.GetActive() +
        " level=" + level +
        " gear_controller=" + (gearController != null ? 1 : 0)
      );
    }
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
    // Reuse cached list instead of creating new one
    cachedKeys.Clear();
    cachedKeys.AddRange(AllStatValues.Esperanza.Keys);

    for (int i = 0; i < cachedKeys.Count; i++) {
      var key = cachedKeys[i];
      AllStatValues.Esperanza[key] = 0f;
    }
    foreach (var form in FormStatIncreases.increases) {
      foreach (var majorStat in form.Value) {
        foreach (var minorStat in majorStat.Value) {
          AllStatValues.Esperanza[minorStat.Key] += minorStat.Value * FormStatsValues.values[form.Key][majorStat.Key];
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
