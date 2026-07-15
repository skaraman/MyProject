using System;
using System.Collections.Generic;
using UnityEngine;

public class EnemyInfo : MonoBehaviour {
  public string enemyType;
  public int level = 1;

  [NonSerialized] public float currentHp;
  [NonSerialized] public Spawner ownerSpawner;

  readonly Dictionary<string, float> resolvedStats = new(16, StringComparer.OrdinalIgnoreCase);
  readonly List<DemonStatModifier> runtimeStatBonuses = new(4);
  readonly List<string> resolvedStatKeyScratch = new(16);

  public int SpawnContextVersion { get; private set; }
  public IReadOnlyDictionary<string, float> ResolvedStats => resolvedStats;
  public IReadOnlyList<DemonStatModifier> StatBonuses => runtimeStatBonuses;

  static bool ShouldLogSpawnDebug() {
    return SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs &&
           (Application.isEditor || Debug.isDebugBuild);
  }

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

    if (ShouldLogSpawnDebug()) {
      RuntimeLog.Log(
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
    if (string.IsNullOrWhiteSpace(statName)) {
      return fallback;
    }

    return resolvedStats.TryGetValue(statName, out var value) ? value : fallback;
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
    if (runtimeStatBonuses.Capacity < statBonuses.Count) {
      runtimeStatBonuses.Capacity = statBonuses.Count;
    }

    for (var i = 0; i < statBonuses.Count; i++) {
      var bonus = statBonuses[i];
      if (bonus == null) continue;
      runtimeStatBonuses.Add(bonus);
    }
  }

  void RebuildResolvedStats() {
    DemonStats.ResolveStatsInto(
      enemyType,
      level,
      runtimeStatBonuses,
      resolvedStats,
      resolvedStatKeyScratch
    );
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
