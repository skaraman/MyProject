using System;
using System.Collections.Generic;

public enum ItemTier {
  Basic,
  Magic,
  Legend,
  Mythic,
  Cosmic,
  Eternal
}

public static class ItemTierRules {
  static readonly Dictionary<ItemTier, int> BoostCounts = new Dictionary<ItemTier, int> {
    [ItemTier.Basic] = 1,
    [ItemTier.Magic] = 2,
    [ItemTier.Legend] = 3,
    [ItemTier.Mythic] = 4,
    [ItemTier.Cosmic] = 6,
    [ItemTier.Eternal] = 8
  };

  public static int GetBoostCount(ItemTier tier) {
    return BoostCounts[tier];
  }

  public static bool TryParse(string value, out ItemTier tier) {
    return Enum.TryParse(value, true, out tier) && BoostCounts.ContainsKey(tier);
  }

  public static bool IsBoostStat(string statName) {
    if (string.IsNullOrWhiteSpace(statName)) {
      return false;
    }

    return Abbreviations.all.ContainsKey(statName.Trim().ToUpperInvariant());
  }

  public static bool ValidateBoosts(
    ItemTier tier,
    IList<BoostEntry> boosts,
    out string error
  ) {
    var requiredCount = GetBoostCount(tier);
    var actualCount = boosts != null ? boosts.Count : 0;
    if (actualCount != requiredCount) {
      error = tier + " items require exactly " + requiredCount +
        " boost stats, but received " + actualCount + ".";
      return false;
    }

    var usedStats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < boosts.Count; i++) {
      var boost = boosts[i];
      if (boost == null || !IsBoostStat(boost.statName)) {
        error = "Boost " + i + " must use a stat key from Abbreviations.all.";
        return false;
      }

      var statName = boost.statName.Trim().ToUpperInvariant();
      if (!usedStats.Add(statName)) {
        error = "Boost stat " + statName + " is duplicated.";
        return false;
      }
    }

    error = "";
    return true;
  }
}
