using System.Collections.Generic;
using UnityEngine;

public enum CombatDamageKind {
  Damage = 0,
  Direct = 1,
  Critical = 2,
  Lucky = 3
}

public struct CombatDamageResult {
  public CombatDamageKind kind;
  public float amount;
  public float baseDamage;
  public float armorApplied;
  public float criticalChance;
  public float luckyChance;
  public float directChance;
  public float criticalRoll;
  public float luckyRoll;
  public float directRoll;
}

public static class CombatDamageResolver {
  public static CombatDamageResult ResolveEsperanzaHit(IReadOnlyDictionary<string, float> defenderStats) {
    var armor = GetStat(defenderStats, "ARM");

    var criticalChance = Mathf.Clamp01(GetEsperanzaStat("CCHC"));
    var luckyChance = Mathf.Clamp01(GetEsperanzaStat("LCHC"));
    var directChance = Mathf.Clamp01(GetEsperanzaStat("DCHC"));

    var criticalRoll = Random.value;
    if (criticalRoll <= criticalChance) {
      return BuildResult(
        kind: CombatDamageKind.Critical,
        baseDamage: GetEsperanzaStat("CDMG"),
        armorApplied: armor,
        criticalChance: criticalChance,
        luckyChance: luckyChance,
        directChance: directChance,
        criticalRoll: criticalRoll,
        luckyRoll: -1f,
        directRoll: -1f
      );
    }

    var luckyRoll = Random.value;
    if (luckyRoll <= luckyChance) {
      return BuildResult(
        kind: CombatDamageKind.Lucky,
        baseDamage: GetEsperanzaStat("LDMG"),
        armorApplied: 0f,
        criticalChance: criticalChance,
        luckyChance: luckyChance,
        directChance: directChance,
        criticalRoll: criticalRoll,
        luckyRoll: luckyRoll,
        directRoll: -1f
      );
    }

    var directRoll = Random.value;
    if (directRoll <= directChance) {
      return BuildResult(
        kind: CombatDamageKind.Direct,
        baseDamage: GetEsperanzaStat("DDMG"),
        armorApplied: 0f,
        criticalChance: criticalChance,
        luckyChance: luckyChance,
        directChance: directChance,
        criticalRoll: criticalRoll,
        luckyRoll: luckyRoll,
        directRoll: directRoll
      );
    }

    return BuildResult(
      kind: CombatDamageKind.Damage,
      baseDamage: GetEsperanzaStat("DMG"),
      armorApplied: armor,
      criticalChance: criticalChance,
      luckyChance: luckyChance,
      directChance: directChance,
      criticalRoll: criticalRoll,
      luckyRoll: luckyRoll,
      directRoll: directRoll
    );
  }

  public static CombatDamageResult ResolveEnemyDamage(IReadOnlyDictionary<string, float> attackerStats, IReadOnlyDictionary<string, float> defenderStats) {
    return BuildResult(
      kind: CombatDamageKind.Damage,
      baseDamage: GetStat(attackerStats, "DMG"),
      armorApplied: GetStat(defenderStats, "ARM"),
      criticalChance: 0f,
      luckyChance: 0f,
      directChance: 0f,
      criticalRoll: -1f,
      luckyRoll: -1f,
      directRoll: -1f
    );
  }

  static CombatDamageResult BuildResult(
    CombatDamageKind kind,
    float baseDamage,
    float armorApplied,
    float criticalChance,
    float luckyChance,
    float directChance,
    float criticalRoll,
    float luckyRoll,
    float directRoll
  ) {
    var result = new CombatDamageResult {
      kind = kind,
      baseDamage = Mathf.Max(baseDamage, 0f),
      armorApplied = Mathf.Max(armorApplied, 0f),
      criticalChance = criticalChance,
      luckyChance = luckyChance,
      directChance = directChance,
      criticalRoll = criticalRoll,
      luckyRoll = luckyRoll,
      directRoll = directRoll
    };
    result.amount = ResolveFinalAmount(result);
    return result;
  }

  static float ResolveFinalAmount(CombatDamageResult result) {
    var finalAmount = result.baseDamage;

    switch (result.kind) {
      case CombatDamageKind.Damage:
        finalAmount -= result.armorApplied;
        break;
      case CombatDamageKind.Direct:
        break;
      case CombatDamageKind.Critical:
        finalAmount -= result.armorApplied;
        break;
      case CombatDamageKind.Lucky:
        break;
    }

    return Mathf.Max(finalAmount, 0f);
  }

  static float GetEsperanzaStat(string statName) {
    return GetStat(AllStatValues.Esperanza, statName);
  }

  static float GetStat(IReadOnlyDictionary<string, float> stats, string statName) {
    if (stats == null || string.IsNullOrWhiteSpace(statName)) {
      return 0f;
    }

    return stats.TryGetValue(statName, out var value) ? value : 0f;
  }
}
