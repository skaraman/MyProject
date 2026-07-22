using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyAttackDefinition {
  public string AnimationName { get; }
  public float MinimumDistance { get; }
  public float MaximumDistance { get; }
  public bool LandAtTargetSnapshot { get; }

  public EnemyAttackDefinition(
    string animationName,
    float minimumDistance,
    float maximumDistance,
    bool landAtTargetSnapshot = false
  ) {
    AnimationName = animationName ?? "";
    MinimumDistance = Mathf.Max(0f, minimumDistance);
    MaximumDistance = Mathf.Max(MinimumDistance, maximumDistance);
    LandAtTargetSnapshot = landAtTargetSnapshot;
  }

  public bool IsInRange(float distance) {
    return distance >= MinimumDistance && distance <= MaximumDistance;
  }
}

public static class EnemyAttacks {
  static readonly Dictionary<string, IReadOnlyList<EnemyAttackDefinition>> Definitions =
    new(StringComparer.OrdinalIgnoreCase) {
      [ImpData.EnemyType] = ImpData.Attacks
    };

  public static bool HasDefinitions(string enemyType) {
    return !string.IsNullOrWhiteSpace(enemyType) &&
           Definitions.TryGetValue(enemyType.Trim(), out var attacks) &&
           attacks != null &&
           attacks.Count > 0;
  }

  public static bool TrySelectForDistance(
    string enemyType,
    float distance,
    out EnemyAttackDefinition selectedAttack
  ) {
    selectedAttack = null;
    if (string.IsNullOrWhiteSpace(enemyType) ||
        !Definitions.TryGetValue(enemyType.Trim(), out var attacks) ||
        attacks == null) {
      return false;
    }

    var eligibleCount = 0;
    for (var i = 0; i < attacks.Count; i++) {
      var attack = attacks[i];
      if (attack == null || !attack.IsInRange(distance)) continue;

      eligibleCount += 1;
      if (UnityEngine.Random.Range(0, eligibleCount) == 0) {
        selectedAttack = attack;
      }
    }

    return selectedAttack != null;
  }
}
