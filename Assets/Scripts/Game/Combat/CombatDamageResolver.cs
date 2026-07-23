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
  public EndlessNumber amount;
  public EndlessNumber flatDamage;
  public EndlessNumber baseDamage;
  public float abilityDamageMultiplier;
  public float damageRangeMultiplier;
  public EndlessNumber armorBeforePenetration;
  public EndlessNumber armorApplied;
  public EndlessNumber penetrationApplied;
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
  public const float MinimumDamageRangeMultiplier = 0.5f;
  public const float MaximumDamageRangeMultiplier = 1.5f;


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
    public EndlessNumber Damage { get; }
    public float CriticalChance { get; }
    public EndlessNumber CriticalDamage { get; }
    public float LuckyChance { get; }
    public EndlessNumber LuckyDamage { get; }
    public float DirectChance { get; }
    public EndlessNumber DirectDamage { get; }
    public EndlessNumber Penetration { get; }

    public AttackerCombatStats(IReadOnlyDictionary<string, StatValue> stats) {
      Damage = GetEndlessStat(stats, CombatStatKeys.Damage);
      CriticalChance = GetPercentageStat(stats, CombatStatKeys.CriticalChance);
      CriticalDamage = GetEndlessStat(stats, CombatStatKeys.CriticalDamage);
      LuckyChance = GetPercentageStat(stats, CombatStatKeys.LuckyChance);
      LuckyDamage = GetEndlessStat(stats, CombatStatKeys.LuckyDamage);
      DirectChance = GetPercentageStat(stats, CombatStatKeys.DirectChance);
      DirectDamage = GetEndlessStat(stats, CombatStatKeys.DirectDamage);
      Penetration = GetEndlessStat(stats, CombatStatKeys.Penetration);
    }
  }

  public static CombatDamageResult ResolveEsperanzaHit(
    IReadOnlyDictionary<string, StatValue> attackerStats,
    IReadOnlyDictionary<string, StatValue> defenderStats,
    int abilityRawDamage,
    float abilityDamageMultiplier
  ) {
    var attacker = new AttackerCombatStats(attackerStats);
    var armor = GetEndlessStat(defenderStats, CombatStatKeys.Armor);
    var evadeChance = GetPercentageStat(defenderStats, CombatStatKeys.Evade);

    var criticalChance = Mathf.Clamp01(attacker.CriticalChance);
    var luckyChance = Mathf.Clamp01(attacker.LuckyChance);
    var directChance = Mathf.Clamp01(attacker.DirectChance);

    var resolvedAbilityDamage = Mathf.Max(abilityRawDamage, 0);
    var baseAttackDamage = attacker.Damage.Copy().AddInPlace(resolvedAbilityDamage);
    var damageRangeMultiplier = RollDamageRangeMultiplier();


    // Special hits use independent rolls with explicit Critical > Lucky > Direct priority.
    var criticalRoll = Random.value;
    if (RollSucceeds(criticalChance, criticalRoll)) {
      return BuildResult(
        kind: CombatDamageKind.Critical,
        flatDamage: baseAttackDamage + attacker.CriticalDamage,
        abilityDamageMultiplier: abilityDamageMultiplier,
        damageRangeMultiplier: damageRangeMultiplier,
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
        flatDamage: baseAttackDamage + attacker.LuckyDamage,
        abilityDamageMultiplier: abilityDamageMultiplier,
        damageRangeMultiplier: damageRangeMultiplier,
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
        flatDamage: baseAttackDamage + attacker.DirectDamage,
        abilityDamageMultiplier: abilityDamageMultiplier,
        damageRangeMultiplier: damageRangeMultiplier,
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
      flatDamage: baseAttackDamage,
      abilityDamageMultiplier: abilityDamageMultiplier,
      damageRangeMultiplier: damageRangeMultiplier,
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
    IReadOnlyDictionary<string, StatValue> attackerStats,
    IReadOnlyDictionary<string, StatValue> defenderStats
  ) {
    return BuildResult(
      kind: CombatDamageKind.Damage,
      flatDamage: GetEndlessStat(attackerStats, CombatStatKeys.Damage),
      abilityDamageMultiplier: 1f,
      damageRangeMultiplier: RollDamageRangeMultiplier(),
      armor: GetEndlessStat(defenderStats, CombatStatKeys.Armor),
      penetration: GetEndlessStat(attackerStats, CombatStatKeys.Penetration),
      evadeChance: GetPercentageStat(defenderStats, CombatStatKeys.Evade),
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
    EndlessNumber flatDamage,
    float abilityDamageMultiplier,
    float damageRangeMultiplier,
    EndlessNumber armor,
    EndlessNumber penetration,
    float evadeChance,
    float criticalChance,
    float luckyChance,
    float directChance,
    float criticalRoll,
    float luckyRoll,
    float directRoll
  ) {
    var resolvedFlatDamage = NonNegative(flatDamage);
    var resolvedAbilityDamageMultiplier = Mathf.Max(0f, abilityDamageMultiplier);
    var resolvedDamageRangeMultiplier = Mathf.Clamp(
      damageRangeMultiplier,
      MinimumDamageRangeMultiplier,
      MaximumDamageRangeMultiplier
    );
    var scaledBaseDamage = resolvedFlatDamage.Copy()
      .MultiplyInPlace(resolvedAbilityDamageMultiplier)
      .MultiplyInPlace(resolvedDamageRangeMultiplier);

    var result = new CombatDamageResult {
      kind = kind,
      flatDamage = resolvedFlatDamage,
      baseDamage = scaledBaseDamage,
      abilityDamageMultiplier = resolvedAbilityDamageMultiplier,
      damageRangeMultiplier = resolvedDamageRangeMultiplier,
      armorBeforePenetration = NonNegative(armor),
      armorApplied = new EndlessNumber(),
      penetrationApplied = new EndlessNumber(),
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

  static float RollDamageRangeMultiplier() {
    return Random.Range(MinimumDamageRangeMultiplier, MaximumDamageRangeMultiplier);
  }

  static void ResolveDefense(ref CombatDamageResult result, EndlessNumber penetration) {
    var resolvedPenetration = NonNegative(penetration);

    switch (result.kind) {
      case CombatDamageKind.Damage:
        ApplyPenetratedArmor(ref result, resolvedPenetration);
        RollEvade(ref result);
        break;
      case CombatDamageKind.Critical:
        result.armorApplied = result.armorBeforePenetration.Copy();
        RollEvade(ref result);
        break;
      case CombatDamageKind.Lucky:
        ApplyPenetratedArmor(ref result, resolvedPenetration);
        break;
      case CombatDamageKind.Direct:
        break;
    }
  }

  static void ApplyPenetratedArmor(ref CombatDamageResult result, EndlessNumber penetration) {
    result.penetrationApplied = EndlessNumber.Min(penetration, result.armorBeforePenetration);
    result.armorApplied = result.armorBeforePenetration - result.penetrationApplied;
  }

  static void RollEvade(ref CombatDamageResult result) {
    if (result.evadeChance <= 0f) {
      return;
    }

    result.evadeRoll = Random.value;
    result.evaded = RollSucceeds(result.evadeChance, result.evadeRoll);
  }

  static EndlessNumber ResolveFinalAmount(CombatDamageResult result) {
    if (result.evaded) {
      return new EndlessNumber();
    }

    var finalAmount = result.baseDamage.Copy();

    switch (result.kind) {
      case CombatDamageKind.Damage:
        finalAmount.SubtractInPlace(result.armorApplied);
        break;
      case CombatDamageKind.Direct:
        break;
      case CombatDamageKind.Critical:
        finalAmount.SubtractInPlace(result.armorApplied);
        break;
      case CombatDamageKind.Lucky:
        finalAmount.SubtractInPlace(result.armorApplied);
        break;
    }

    finalAmount = NonNegative(finalAmount).RoundToWholeInPlace();
    return finalAmount.IsPositive
      ? finalAmount
      : new EndlessNumber(1d);
  }

  static bool RollSucceeds(float chance, float roll) {
    if (chance <= 0f) return false;
    if (chance >= 1f) return true;
    return roll < chance;
  }

  static EndlessNumber GetEndlessStat(
    IReadOnlyDictionary<string, StatValue> stats,
    string statName
  ) {
    if (stats == null || string.IsNullOrWhiteSpace(statName)) {
      return new EndlessNumber();
    }

    if (!stats.TryGetValue(statName, out var value) ||
        value == null ||
        value.IsPercentage ||
        value.EndlessValue == null) {
      return new EndlessNumber();
    }

    return value.EndlessValue.Copy();
  }

  static float GetPercentageStat(
    IReadOnlyDictionary<string, StatValue> stats,
    string statName
  ) {
    if (stats == null || string.IsNullOrWhiteSpace(statName)) {
      return 0f;
    }

    return stats.TryGetValue(statName, out var value) &&
           value != null &&
           value.IsPercentage
      ? value.PercentageValue
      : 0f;
  }

  static EndlessNumber NonNegative(EndlessNumber value) {
    return value != null && value.IsPositive
      ? value.Copy()
      : new EndlessNumber();
  }
}
