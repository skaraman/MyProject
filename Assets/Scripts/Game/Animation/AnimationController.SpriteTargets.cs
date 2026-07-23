using System;
using System.Collections.Generic;
using UnityEngine;

public partial class AnimationController {
  public void CopySpriteTargetsTo(List<SpriteWithNormals> destination) {
    if (destination == null) {
      return;
    }

    destination.Clear();
    for (var i = 0; i < spriteTargets.Count; i++) {
      var target = spriteTargets[i];
      if (target != null) {
        destination.Add(target);
      }
    }
  }

  private void CacheSpriteTargets() {
    spriteTargets.Clear();
    criticalSpriteTargets.Clear();
    spriteTargetRenderers.Clear();
    if (spriteObjects != null && spriteObjects.Length > 0) {
      foreach (var go in spriteObjects) {
        if (go == null) continue;
        var sprite = go.GetComponent<SpriteWithNormals>();
        if (sprite != null) {
          spriteTargets.Add(sprite);
          if (IsCriticalSpriteTarget(sprite)) criticalSpriteTargets.Add(sprite);
          spriteTargetRenderers[sprite] = go.GetComponent<SpriteRenderer>();
        }
      }
    }
    if (spriteTargets.Count == 0 && rootTransform != null) {
      spriteTargetScanBuffer.Clear();
      rootTransform.GetComponentsInChildren(true, spriteTargetScanBuffer);
      for (var i = 0; i < spriteTargetScanBuffer.Count; i++) {
        var sprite = spriteTargetScanBuffer[i];
        if (sprite != null) {
          spriteTargets.Add(sprite);
          if (IsCriticalSpriteTarget(sprite)) criticalSpriteTargets.Add(sprite);
          spriteTargetRenderers[sprite] = sprite.GetComponent<SpriteRenderer>();
        }
      }
      spriteTargetScanBuffer.Clear();
    }
  }

  static bool IsCriticalSpriteTarget(SpriteWithNormals target) {
    if (target == null) return false;
    var lib = target.libraryName;
    if (string.IsNullOrWhiteSpace(lib)) return false;
    return lib.IndexOf("/Skin/", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  bool HasEnabledSpriteTargets() {
    for (var i = 0; i < spriteTargets.Count; i++) {
      if (IsSpriteTargetEnabled(spriteTargets[i])) return true;
    }
    return false;
  }

  int CountEnabledSpriteTargets() {
    var count = 0;
    for (var i = 0; i < spriteTargets.Count; i++) {
      if (IsSpriteTargetEnabled(spriteTargets[i])) count++;
    }
    return count;
  }

  SpriteRenderer ResolveSpriteTargetRenderer(SpriteWithNormals target) {
    if (target == null) return null;
    if (!spriteTargetRenderers.TryGetValue(target, out var renderer) || renderer == null) {
      renderer = target.GetComponent<SpriteRenderer>();
      spriteTargetRenderers[target] = renderer;
    }
    return renderer;
  }

  bool IsSpriteTargetEnabled(SpriteWithNormals target) {
    if (ReferenceEquals(target, null) || !target.isActiveAndEnabled || target.DoNotRender) return false;
    if (spriteTargetRenderers.TryGetValue(target, out var renderer) && renderer != null) {
      return renderer.gameObject.activeInHierarchy;
    }
    return target.gameObject.activeInHierarchy;
  }
}
