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

  public string StatKey => NormalizeStatKey(stat);

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

  public Dictionary<string, float> ResolveStats(int level, IList<DemonStatModifier> bonuses = null) {
    var resolved = CloneStats(baseStats);
    var resolvedLevel = Mathf.Max(level, 1);
    var growthStepCount = Mathf.Max(resolvedLevel - 1, 0);

    if (growthStepCount > 0) {
      foreach (var growth in growthPerLevel) {
        if (Mathf.Approximately(growth.Value, 0f)) continue;
        var current = resolved.TryGetValue(growth.Key, out var value) ? value : 0f;
        resolved[growth.Key] = current + growth.Value * growthStepCount;
      }
    }

    ApplyBonuses(resolved, bonuses);
    return resolved;
  }

  static void ApplyBonuses(Dictionary<string, float> resolved, IList<DemonStatModifier> bonuses) {
    if (resolved == null || bonuses == null || bonuses.Count <= 0) {
      return;
    }

    for (var i = 0; i < bonuses.Count; i++) {
      var bonus = bonuses[i];
      if (bonus == null) continue;
      var statKey = bonus.StatKey;
      if (string.IsNullOrWhiteSpace(statKey)) continue;

      var current = resolved.TryGetValue(statKey, out var value) ? value : 0f;
      resolved[statKey] = current + bonus.amount;
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
    ["Imp"] = new DemonStatDefinition(
      baseStats: new Dictionary<string, float> {
        ["DMG"] = 1f,
        ["HP"] = 10f,
        ["AKSP"] = 1f,
        ["HPRG"] = 0f,
        ["ARM"] = 0f,
        ["BONUS"] = 0f,
        ["MVSP"] = 1f,
        ["CDST"] = 1f,
        ["EVD"] = 0f,
        ["FEAR"] = 0f,
        ["SPEC"] = 0f,
        ["PEN"] = 0f
      },
      growthPerLevel: new Dictionary<string, float> {
        ["DMG"] = 0f,
        ["HP"] = 0f,
        ["AKSP"] = 0f,
        ["HPRG"] = 0f,
        ["ARM"] = 0f,
        ["BONUS"] = 0f,
        ["MVSP"] = 0f,
        ["CDST"] = 0f,
        ["EVD"] = 0f,
        ["FEAR"] = 0f,
        ["SPEC"] = 0f,
        ["PEN"] = 0f
      }
    )
  };

  public static bool TryGetDefinition(string demonType, out DemonStatDefinition definition) {
    definition = null;
    var normalizedType = NormalizeDemonType(demonType);
    if (string.IsNullOrWhiteSpace(normalizedType)) {
      return false;
    }

    return Definitions.TryGetValue(normalizedType, out definition) && definition != null;
  }

  public static Dictionary<string, float> ResolveStats(
    string demonType,
    int level,
    IList<DemonStatModifier> bonuses = null
  ) {
    if (!TryGetDefinition(demonType, out var definition)) {
      return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    }

    return definition.ResolveStats(level, bonuses);
  }

  public static string NormalizeDemonType(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }
}
