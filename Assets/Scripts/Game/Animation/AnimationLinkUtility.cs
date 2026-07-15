using System;
using System.Collections.Generic;

public static class AnimationLinkUtility {
  public static int CollectLinkedEffectKeys(
    Dictionary<string, AnimData> animations,
    IReadOnlyList<string> animationKeys,
    List<string> outKeys,
    HashSet<string> seenKeys = null,
    int maxKeys = int.MaxValue
  ) {
    return CollectLinkedKeys(animations, animationKeys, outKeys, seenKeys, maxKeys, includeEffects: true);
  }

  public static int CollectLinkedProjectileKeys(
    Dictionary<string, AnimData> animations,
    IReadOnlyList<string> animationKeys,
    List<string> outKeys,
    HashSet<string> seenKeys = null,
    int maxKeys = int.MaxValue
  ) {
    return CollectLinkedKeys(animations, animationKeys, outKeys, seenKeys, maxKeys, includeEffects: false);
  }

  static int CollectLinkedKeys(
    Dictionary<string, AnimData> animations,
    IReadOnlyList<string> animationKeys,
    List<string> outKeys,
    HashSet<string> seenKeys,
    int maxKeys,
    bool includeEffects
  ) {
    if (animations == null || animations.Count <= 0 || outKeys == null || maxKeys <= 0) {
      return 0;
    }

    var beforeCount = outKeys.Count;
    if (animationKeys == null || animationKeys.Count <= 0) {
      foreach (var pair in animations) {
        if (outKeys.Count >= maxKeys) {
          break;
        }

        TryAddLinkedKey(ResolveLinkedKey(pair.Value, includeEffects), outKeys, seenKeys);
      }

      return Math.Max(outKeys.Count - beforeCount, 0);
    }

    for (var i = 0; i < animationKeys.Count; i++) {
      if (outKeys.Count >= maxKeys) {
        break;
      }

      var requestedKey = NormalizeKey(animationKeys[i]);
      if (string.IsNullOrWhiteSpace(requestedKey)) {
        continue;
      }

      foreach (var pair in animations) {
        if (outKeys.Count >= maxKeys) {
          break;
        }

        var animationName = pair.Key;
        var animation = pair.Value;
        if (animation == null || string.IsNullOrWhiteSpace(animationName)) {
          continue;
        }

        if (!MatchesRequestedLink(requestedKey, animationName, animation)) {
          continue;
        }

        TryAddLinkedKey(ResolveLinkedKey(animation, includeEffects), outKeys, seenKeys);
      }
    }

    return Math.Max(outKeys.Count - beforeCount, 0);
  }

  static bool MatchesRequestedLink(string requestedKey, string animationName, AnimData animation) {
    return string.Equals(requestedKey, NormalizeKey(animationName), StringComparison.OrdinalIgnoreCase) ||
           string.Equals(requestedKey, NormalizeKey(animation != null ? animation.effect : null), StringComparison.OrdinalIgnoreCase) ||
           string.Equals(requestedKey, NormalizeKey(animation != null ? animation.projectile : null), StringComparison.OrdinalIgnoreCase);
  }

  static string ResolveLinkedKey(AnimData animation, bool includeEffects) {
    if (animation == null) {
      return "";
    }

    return NormalizeKey(includeEffects ? animation.effect : animation.projectile);
  }

  static void TryAddLinkedKey(string linkedKey, List<string> outKeys, HashSet<string> seenKeys) {
    if (string.IsNullOrWhiteSpace(linkedKey) || outKeys == null) {
      return;
    }

    if (seenKeys != null) {
      if (!seenKeys.Add(linkedKey)) {
        return;
      }
    }
    else if (outKeys.Contains(linkedKey)) {
      return;
    }

    outKeys.Add(linkedKey);
  }

  static string NormalizeKey(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value;
  }
}
