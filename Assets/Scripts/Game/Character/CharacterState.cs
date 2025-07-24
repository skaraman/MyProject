using UnityEngine;
using System.Collections.Generic;
using System.Linq;
public class CharacterState : MonoBehaviour {
  public Dictionary<string, Dictionary<string, int>> formStats;
  public Dictionary<string, float> allStats;
  public Dictionary<string, Dictionary<string, GearItem>> equippedGear;
  public LocationTracker locationTracker = new LocationTracker();
  public int level = 0;
  //public Loader loader = new Loader();

  public void RefreshSave() {
    //loader.LoadForms();
    //loader.LoadEquip();
    // if (forms == null && SaveData.ValueExists("EsperStats"))
    // {
    //   forms = SaveData.GetValue<Dictionary<string, Dictionary<string, int>>>("EsperStats");
    // }
    // else
    // {
    //   forms = new FormStatsValues().GetValues();
    // }
  }

  public void UnlockForm(string v) {
    EsperanzaForms.Unlocked[v] = 1;
    //loader.SaveForms();
  }

  public void SetForm(string v) {
    EsperanzaForms.SetActive(v);
    //loader.SaveForms();
  }

  public Dictionary<string, Dictionary<string, int>> GetStats() {
    return formStats;
  }

  public void AddStats(string form, string stat, int amount) {
    // float oldAmount = stats[form][stat];
    formStats[form][stat] += amount;
    GatherALlStatValues();
  }

  public void GatherALlStatValues() {
    level = 0;
    foreach (var form in FormStatIncreases.increases) {
      foreach (var majorStat in form.Value) {
        foreach (var minorStat in majorStat.Value) {
          allStats[minorStat.Key] += minorStat.Value * formStats[form.Key][majorStat.Key];
          level += formStats[form.Key][majorStat.Key];
        }
      }
    }
  }


}
