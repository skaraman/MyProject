using UnityEngine;

/// <summary>
/// Shared colors for floating combat numbers.
/// </summary>
public static class CombatNumberPalette {
  public static readonly Color PlayerDamage = new Color32(255, 64, 64, 255);
  public static readonly Color NormalDamage = new Color32(255, 224, 64, 255);
  public static readonly Color CriticalDamage = new Color32(255, 128, 32, 255);
  public static readonly Color LuckyDamage = new Color32(64, 160, 255, 255);
  public static readonly Color DirectDamage = PlayerDamage;
  public static readonly Color Healing = new Color32(64, 220, 96, 255);

  public static Color ResolveDamage(CombatDamageKind damageKind) {
    switch (damageKind) {
      case CombatDamageKind.Critical:
        return CriticalDamage;
      case CombatDamageKind.Lucky:
        return LuckyDamage;
      case CombatDamageKind.Direct:
        return DirectDamage;
      default:
        return NormalDamage;
    }
  }
}
