using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class DemonStatModifier {
  public string stat;
  public float amount;

  public DemonStatModifier(string stat = "", float amount = 0f) {
    this.stat = NormalizeStatKey(stat);
    this.amount = amount;
  }

  public string StatKey => stat ?? "";

  public DemonStatModifier Clone() {
    return new DemonStatModifier(StatKey, amount);
  }

  public static string NormalizeStatKey(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToUpperInvariant();
  }
}

public sealed class DemonStatDefinition {
  readonly Dictionary<string, float> baseStats;
  readonly Dictionary<string, float> growthPerLevel;

  public DemonStatDefinition(
    Dictionary<string, float> baseStats,
    Dictionary<string, float> growthPerLevel = null
  ) {
    this.baseStats = CloneStats(baseStats);
    this.growthPerLevel = CloneStats(growthPerLevel);
  }

  public Dictionary<string, StatValue> ResolveStats(int level, IList<DemonStatModifier> bonuses = null) {
    var resolved = new Dictionary<string, StatValue>(StringComparer.OrdinalIgnoreCase);
    var statNameScratch = new List<string>();
    ResolveStatsInto(resolved, statNameScratch, level, bonuses);
    return resolved;
  }

  public void ResolveStatsInto(
    Dictionary<string, StatValue> resolved,
    List<string> statNameScratch,
    int level,
    IList<DemonStatModifier> bonuses = null
  ) {
    if (resolved == null) {
      return;
    }

    resolved.Clear();
    foreach (var stat in baseStats) {
      resolved[stat.Key] = new StatValue(stat.Key, stat.Value);
    }
    var resolvedLevel = Mathf.Max(level, 1);
    var growthStepCount = Mathf.Max(resolvedLevel - 1, 0);

    if (growthStepCount > 0) {
      foreach (var growth in growthPerLevel) {
        if (Mathf.Approximately(growth.Value, 0f)) continue;
        if (!resolved.TryGetValue(growth.Key, out var current) || current == null) {
          current = new StatValue(growth.Key);
          resolved[growth.Key] = current;
        }
        current.AddInPlace((double)growth.Value * growthStepCount);
      }
    }

    ApplyBonuses(resolved, bonuses);
    FormStatIncreases.ApplyBonusToFlatStats(resolved, statNameScratch);
  }

  static void ApplyBonuses(Dictionary<string, StatValue> resolved, IList<DemonStatModifier> bonuses) {
    if (resolved == null || bonuses == null || bonuses.Count <= 0) {
      return;
    }

    for (var i = 0; i < bonuses.Count; i++) {
      var bonus = bonuses[i];
      if (bonus == null) continue;
      var statKey = bonus.StatKey;
      if (string.IsNullOrWhiteSpace(statKey)) continue;

      if (!resolved.TryGetValue(statKey, out var current) || current == null) {
        current = new StatValue(statKey);
        resolved[statKey] = current;
      }
      current.AddInPlace(bonus.amount);
    }
  }

  static Dictionary<string, float> CloneStats(Dictionary<string, float> source) {
    var clone = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    if (source == null) {
      return clone;
    }

    foreach (var pair in source) {
      var statKey = DemonStatModifier.NormalizeStatKey(pair.Key);
      if (string.IsNullOrWhiteSpace(statKey)) continue;
      clone[statKey] = pair.Value;
    }
    return clone;
  }
}

public static class DemonStats {
  public static Dictionary<string, DemonStatDefinition> Definitions { get; } = new(StringComparer.OrdinalIgnoreCase) {
    [ImpData.EnemyType] = ImpData.Stats
  };

  public static bool TryGetDefinition(string demonType, out DemonStatDefinition definition) {
    definition = null;
    var normalizedType = NormalizeDemonType(demonType);
    if (string.IsNullOrWhiteSpace(normalizedType)) {
      return false;
    }

    return Definitions.TryGetValue(normalizedType, out definition) && definition != null;
  }

  public static Dictionary<string, StatValue> ResolveStats(
    string demonType,
    int level,
    IList<DemonStatModifier> bonuses = null
  ) {
    var resolvedStats = new Dictionary<string, StatValue>(StringComparer.OrdinalIgnoreCase);
    var statNameScratch = new List<string>();
    ResolveStatsInto(demonType, level, bonuses, resolvedStats, statNameScratch);
    return resolvedStats;
  }

  public static bool ResolveStatsInto(
    string demonType,
    int level,
    IList<DemonStatModifier> bonuses,
    Dictionary<string, StatValue> resolvedStats,
    List<string> statNameScratch
  ) {
    if (resolvedStats == null || statNameScratch == null) {
      return false;
    }

    if (!TryGetDefinition(demonType, out var definition)) {
      resolvedStats.Clear();
      return false;
    }

    definition.ResolveStatsInto(resolvedStats, statNameScratch, level, bonuses);
    ApplyEpisodeProgressMultiplier(resolvedStats, statNameScratch);


    return true;
  }

  public static EndlessNumber ResolveEpisodeProgressMultiplier() {
    var completedPartCount = ContentEpisodeProgression.ResolveCompletedPartCount();
    return EndlessNumber.Pow(2d, completedPartCount);
  }

  static void ApplyEpisodeProgressMultiplier(
    Dictionary<string, StatValue> resolvedStats,
    List<string> statNameScratch
  ) {
    if (resolvedStats == null || resolvedStats.Count <= 0) {
      return;
    }

    var multiplier = ResolveEpisodeProgressMultiplier();
    if (multiplier == new EndlessNumber(1d)) {
      return;
    }

    statNameScratch.Clear();
    foreach (var stat in resolvedStats) {
      statNameScratch.Add(stat.Key);
    }
    for (var i = 0; i < statNameScratch.Count; i++) {
      var statKey = statNameScratch[i];
      if (resolvedStats.TryGetValue(statKey, out var statValue) && statValue != null) {
        statValue.MultiplyInPlace(multiplier);
      }
    }
    statNameScratch.Clear();
  }

  public static string NormalizeDemonType(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }
}
