using System;
using System.Collections.Generic;
using UnityEngine;

public class EnemyInfo : MonoBehaviour {
  public string enemyType;
  public int level = 1;

  [NonSerialized] public float currentHp;
  [NonSerialized] public Spawner ownerSpawner;

  readonly Dictionary<string, float> resolvedStats = new(StringComparer.OrdinalIgnoreCase);
  readonly List<DemonStatModifier> runtimeStatBonuses = new();

  public int SpawnContextVersion { get; private set; }
  public IReadOnlyDictionary<string, float> ResolvedStats => resolvedStats;
  public IReadOnlyList<DemonStatModifier> StatBonuses => runtimeStatBonuses;

  public void ApplySpawnContext(
    string resolvedEnemyType,
    int resolvedLevel,
    IList<DemonStatModifier> statBonuses,
    Spawner resolvedOwnerSpawner
  ) {
    enemyType = DemonStats.NormalizeDemonType(resolvedEnemyType);
    level = Mathf.Max(resolvedLevel, 1);
    ownerSpawner = resolvedOwnerSpawner;

    CopyStatBonuses(statBonuses);
    RebuildResolvedStats();
    ResetHealthFromResolvedStats();
    SpawnContextVersion += 1;

    if (Application.isEditor || Debug.isDebugBuild) {
      Debug.Log(
        "[EnemyInfo][ApplySpawnContext]" +
        " object='" + gameObject.name + "'" +
        " enemy_type='" + enemyType + "'" +
        " level=" + level +
        " bonuses=" + runtimeStatBonuses.Count +
        " stats={" + DescribeResolvedStats() + "}"
      );
    }
  }

  public float GetResolvedStat(string statName, float fallback = 0f) {
    var normalizedStat = DemonStatModifier.NormalizeStatKey(statName);
    if (string.IsNullOrWhiteSpace(normalizedStat)) {
      return fallback;
    }

    return resolvedStats.TryGetValue(normalizedStat, out var value) ? value : fallback;
  }

  public float ResolveMaxHp() {
    return Mathf.Max(GetResolvedStat("HP", 0f), 1f);
  }

  public void ResetHealthFromResolvedStats() {
    currentHp = ResolveMaxHp();
  }

  void CopyStatBonuses(IList<DemonStatModifier> statBonuses) {
    runtimeStatBonuses.Clear();
    if (statBonuses == null || statBonuses.Count <= 0) {
      return;
    }

    for (var i = 0; i < statBonuses.Count; i++) {
      var bonus = statBonuses[i];
      if (bonus == null) continue;
      runtimeStatBonuses.Add(bonus.Clone());
    }
  }

  void RebuildResolvedStats() {
    resolvedStats.Clear();
    var rebuiltStats = DemonStats.ResolveStats(enemyType, level, runtimeStatBonuses);
    foreach (var stat in rebuiltStats) {
      resolvedStats[stat.Key] = stat.Value;
    }
  }

  string DescribeResolvedStats() {
    if (resolvedStats.Count <= 0) {
      return "";
    }

    var keys = new List<string>(resolvedStats.Keys);
    keys.Sort(StringComparer.OrdinalIgnoreCase);

    var parts = new List<string>(keys.Count);
    for (var i = 0; i < keys.Count; i++) {
      var key = keys[i];
      parts.Add(key + "=" + resolvedStats[key].ToString("0.###"));
    }

    return string.Join(", ", parts);
  }
}
