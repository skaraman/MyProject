using System;
using System.Collections.Generic;
using UnityEngine;

public class EnemyInfo : MonoBehaviour {
  public string enemyType;
  public int level = 1;
  public float statMultiplier = 1f;

  [NonSerialized] public EndlessNumber currentHp = new();
  [NonSerialized] public Spawner ownerSpawner;

  readonly Dictionary<string, StatValue> resolvedStats = new(16, StringComparer.OrdinalIgnoreCase);
  readonly List<DemonStatModifier> runtimeStatBonuses = new(4);
  readonly List<string> resolvedStatKeyScratch = new(16);

  public int SpawnContextVersion { get; private set; }
  public IReadOnlyDictionary<string, StatValue> ResolvedStats => resolvedStats;
  public IReadOnlyList<DemonStatModifier> StatBonuses => runtimeStatBonuses;

  static bool ShouldLogSpawnDebug() {
    return SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs &&
           (Application.isEditor || Debug.isDebugBuild);
  }

  public void ApplySpawnContext(
    string resolvedEnemyType,
    int resolvedLevel,
    float resolvedStatMultiplier,
    IList<DemonStatModifier> statBonuses,
    Spawner resolvedOwnerSpawner
  ) {
    enemyType = DemonStats.NormalizeDemonType(resolvedEnemyType);
    level = Mathf.Max(resolvedLevel, 1);
    statMultiplier = Mathf.Max(resolvedStatMultiplier, 0.0001f);
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
        " stat_multiplier=" + statMultiplier +
        " bonuses=" + runtimeStatBonuses.Count +
        " stats={" + DescribeResolvedStats() + "}"
      );
    }
  }

  // Physics/timing APIs still require floats. Combat and HP must use ResolvedStats/endless accessors.
  public float GetResolvedEngineStat(string statName, float fallback = 0f) {
    if (string.IsNullOrWhiteSpace(statName)) {
      return fallback;
    }

    return resolvedStats.TryGetValue(statName, out var value) && value != null
      ? value.ToSingleClamped()
      : fallback;
  }

  public EndlessNumber GetResolvedEndlessStat(string statName) {
    if (string.IsNullOrWhiteSpace(statName) ||
        !resolvedStats.TryGetValue(statName, out var value) ||
        value == null ||
        value.IsPercentage ||
        value.EndlessValue == null) {
      return new EndlessNumber();
    }

    return value.EndlessValue.Copy();
  }

  public EndlessNumber ResolveMaxHp() {
    return EndlessNumber.Max(GetResolvedEndlessStat("HP"), new EndlessNumber(1d));
  }

  public void ResetHealthFromResolvedStats() {
    (currentHp ??= new EndlessNumber()).Set(ResolveMaxHp());
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
    if (Mathf.Approximately(statMultiplier, 1f)) return;

    var multiplier = new EndlessNumber(statMultiplier);
    foreach (var stat in resolvedStats) {
      stat.Value?.MultiplyInPlace(multiplier);
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
      parts.Add(key + "=" + resolvedStats[key]);
    }

    return string.Join(", ", parts);
  }
}
