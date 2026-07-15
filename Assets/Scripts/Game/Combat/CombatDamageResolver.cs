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
  public float armorBeforePenetration;
  public float armorApplied;
  public float penetrationApplied;
  public float evadeChance;
  public float evadeRoll;
  public bool evaded;
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
    public const string Evade = "EVD";
    public const string Penetration = "PEN";
  }

  readonly struct AttackerCombatStats {
    public float Damage { get; }
    public float CriticalChance { get; }
    public float CriticalDamage { get; }
    public float LuckyChance { get; }
    public float LuckyDamage { get; }
    public float DirectChance { get; }
    public float DirectDamage { get; }
    public float Penetration { get; }

    public AttackerCombatStats(IReadOnlyDictionary<string, float> stats) {
      Damage = GetStat(stats, CombatStatKeys.Damage);
      CriticalChance = GetStat(stats, CombatStatKeys.CriticalChance);
      CriticalDamage = GetStat(stats, CombatStatKeys.CriticalDamage);
      LuckyChance = GetStat(stats, CombatStatKeys.LuckyChance);
      LuckyDamage = GetStat(stats, CombatStatKeys.LuckyDamage);
      DirectChance = GetStat(stats, CombatStatKeys.DirectChance);
      DirectDamage = GetStat(stats, CombatStatKeys.DirectDamage);
      Penetration = GetStat(stats, CombatStatKeys.Penetration);
    }
  }

  public static CombatDamageResult ResolveEsperanzaHit(
    IReadOnlyDictionary<string, float> attackerStats,
    IReadOnlyDictionary<string, float> defenderStats,
    float abilityRawDamage
  ) {
    var attacker = new AttackerCombatStats(attackerStats);
    var armor = GetStat(defenderStats, CombatStatKeys.Armor);
    var evadeChance = GetStat(defenderStats, CombatStatKeys.Evade);

    var criticalChance = Mathf.Clamp01(attacker.CriticalChance);
    var luckyChance = Mathf.Clamp01(attacker.LuckyChance);
    var directChance = Mathf.Clamp01(attacker.DirectChance);

    var resolvedAbilityDamage = Mathf.Max(abilityRawDamage, 0f);
    var baseAttackDamage = attacker.Damage + resolvedAbilityDamage;

    // Special hits use independent rolls with explicit Critical > Lucky > Direct priority.
    var criticalRoll = Random.value;
    if (RollSucceeds(criticalChance, criticalRoll)) {
      return BuildResult(
        kind: CombatDamageKind.Critical,
        baseDamage: baseAttackDamage + attacker.CriticalDamage,
        armor: armor,
        penetration: attacker.Penetration,
        evadeChance: evadeChance,
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
        baseDamage: baseAttackDamage + attacker.LuckyDamage,
        armor: armor,
        penetration: attacker.Penetration,
        evadeChance: evadeChance,
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
        baseDamage: baseAttackDamage + attacker.DirectDamage,
        armor: armor,
        penetration: attacker.Penetration,
        evadeChance: evadeChance,
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
      baseDamage: baseAttackDamage,
      armor: armor,
      penetration: attacker.Penetration,
      evadeChance: evadeChance,
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
      armor: GetStat(defenderStats, CombatStatKeys.Armor),
      penetration: GetStat(attackerStats, CombatStatKeys.Penetration),
      evadeChance: GetStat(defenderStats, CombatStatKeys.Evade),
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
    float armor,
    float penetration,
    float evadeChance,
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
      armorBeforePenetration = Mathf.Max(armor, 0f),
      armorApplied = 0f,
      penetrationApplied = 0f,
      evadeChance = Mathf.Clamp01(evadeChance),
      evadeRoll = -1f,
      evaded = false,
      criticalChance = criticalChance,
      luckyChance = luckyChance,
      directChance = directChance,
      criticalRoll = criticalRoll,
      luckyRoll = luckyRoll,
      directRoll = directRoll
    };
    ResolveDefense(ref result, penetration);
    result.amount = ResolveFinalAmount(result);
    return result;
  }

  static void ResolveDefense(ref CombatDamageResult result, float penetration) {
    var resolvedPenetration = Mathf.Max(penetration, 0f);

    switch (result.kind) {
      case CombatDamageKind.Damage:
        ApplyPenetratedArmor(ref result, resolvedPenetration);
        RollEvade(ref result);
        break;
      case CombatDamageKind.Critical:
        result.armorApplied = result.armorBeforePenetration;
        RollEvade(ref result);
        break;
      case CombatDamageKind.Lucky:
        ApplyPenetratedArmor(ref result, resolvedPenetration);
        break;
      case CombatDamageKind.Direct:
        break;
    }
  }

  static void ApplyPenetratedArmor(ref CombatDamageResult result, float penetration) {
    result.penetrationApplied = Mathf.Min(penetration, result.armorBeforePenetration);
    result.armorApplied = result.armorBeforePenetration - result.penetrationApplied;
  }

  static void RollEvade(ref CombatDamageResult result) {
    if (result.evadeChance <= 0f) {
      return;
    }

    result.evadeRoll = Random.value;
    result.evaded = RollSucceeds(result.evadeChance, result.evadeRoll);
  }

  static float ResolveFinalAmount(CombatDamageResult result) {
    if (result.evaded) {
      return 0f;
    }

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
        finalAmount -= result.armorApplied;
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
