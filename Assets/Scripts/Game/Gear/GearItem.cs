using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class BoostEntry {
  public string statName { get; set; }  // The stat name, like "HP", "DMG", etc.
  public float value { get; set; }  // The value for this stat. 

  public bool HasKnownStat() {
    return ItemTierRules.IsBoostStat(statName);
  }
}

[Serializable]
public class GearItem {
  public string type { get; set; } // The item tier. See ItemTier.
  public string name { get; set; } // name is the fancy name of the item
  public string slot { get; set; } // the gear slot it belong to
  public string gearId { get; set; } //  gearId is the visible sprite
  public string gearColor { get; set; }
  public List<BoostEntry> boosts { get; set; }

  public bool TryGetTier(out ItemTier tier) {
    return ItemTierRules.TryParse(type, out tier);
  }

  public bool ValidateTierBoosts(out string error) {
    if (!TryGetTier(out var tier)) {
      error = "Unknown item tier '" + type + "'.";
      return false;
    }

    return ItemTierRules.ValidateBoosts(tier, boosts, out error);
  }
}


