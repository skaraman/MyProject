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
  static class CombatStatKeys {
    public const string Armor = "ARM";
    public const string Damage = "DMG";
    public const string CriticalChance = "CCHC";
    public const string CriticalDamage = "CDMG";
    public const string LuckyChance = "LCHC";
    public const string LuckyDamage = "LDMG";
    public const string DirectChance = "DCHC";
    public const string DirectDamage = "DDMG";
  }

  readonly struct AttackerCombatStats {
    public float Damage { get; }
    public float CriticalChance { get; }
    public float CriticalDamage { get; }
    public float LuckyChance { get; }
    public float LuckyDamage { get; }
    public float DirectChance { get; }
    public float DirectDamage { get; }

    public AttackerCombatStats(IReadOnlyDictionary<string, float> stats) {
      Damage = GetStat(stats, CombatStatKeys.Damage);
      CriticalChance = GetStat(stats, CombatStatKeys.CriticalChance);
      CriticalDamage = GetStat(stats, CombatStatKeys.CriticalDamage);
      LuckyChance = GetStat(stats, CombatStatKeys.LuckyChance);
      LuckyDamage = GetStat(stats, CombatStatKeys.LuckyDamage);
      DirectChance = GetStat(stats, CombatStatKeys.DirectChance);
      DirectDamage = GetStat(stats, CombatStatKeys.DirectDamage);
    }
  }

  public static CombatDamageResult ResolveEsperanzaHit(
    IReadOnlyDictionary<string, float> attackerStats,
    IReadOnlyDictionary<string, float> defenderStats
  ) {
    var attacker = new AttackerCombatStats(attackerStats);
    var armor = GetStat(defenderStats, CombatStatKeys.Armor);

    var criticalChance = Mathf.Clamp01(attacker.CriticalChance);
    var luckyChance = Mathf.Clamp01(attacker.LuckyChance);
    var directChance = Mathf.Clamp01(attacker.DirectChance);

    // Special hits use independent rolls with explicit Critical > Lucky > Direct priority.
    var criticalRoll = Random.value;
    if (RollSucceeds(criticalChance, criticalRoll)) {
      return BuildResult(
        kind: CombatDamageKind.Critical,
        baseDamage: attacker.CriticalDamage,
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
    if (RollSucceeds(luckyChance, luckyRoll)) {
      return BuildResult(
        kind: CombatDamageKind.Lucky,
        baseDamage: attacker.LuckyDamage,
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
    if (RollSucceeds(directChance, directRoll)) {
      return BuildResult(
        kind: CombatDamageKind.Direct,
        baseDamage: attacker.DirectDamage,
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
      baseDamage: attacker.Damage,
      armorApplied: armor,
      criticalChance: criticalChance,
      luckyChance: luckyChance,
      directChance: directChance,
      criticalRoll: criticalRoll,
      luckyRoll: luckyRoll,
      directRoll: directRoll
    );
  }

  public static CombatDamageResult ResolveEnemyDamage(
    IReadOnlyDictionary<string, float> attackerStats,
    IReadOnlyDictionary<string, float> defenderStats
  ) {
    return BuildResult(
      kind: CombatDamageKind.Damage,
      baseDamage: GetStat(attackerStats, CombatStatKeys.Damage),
      armorApplied: GetStat(defenderStats, CombatStatKeys.Armor),
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

  static bool RollSucceeds(float chance, float roll) {
    if (chance <= 0f) return false;
    if (chance >= 1f) return true;
    return roll < chance;
  }

  static float GetStat(IReadOnlyDictionary<string, float> stats, string statName) {
    if (stats == null || string.IsNullOrWhiteSpace(statName)) {
      return 0f;
    }

    return stats.TryGetValue(statName, out var value) ? value : 0f;
  }
}
