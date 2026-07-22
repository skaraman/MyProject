using System;
using UnityEngine;

public partial class AnimationController {
  bool ShouldHoldEffectStartFrame(
    int enabledTargetCount,
    string category,
    AnimData animation
  ) {
    if (!Application.isPlaying) return false;
    if (appearancePinClass != TextureResidencyCache.PinClass.Effect) return false;
    if (enabledTargetCount <= 0) return false;
    if (string.IsNullOrWhiteSpace(category)) return false;
    if (animation == null) return false;

    var startFrame = Math.Max(animation.start, 1);
    return !AreAllTargetsReadyForWindow(category, startFrame, startFrame);
  }

  bool ShouldHoldInitialPlayerStartFrame(int enabledTargetCount, string category) {
    return Application.isPlaying &&
           appearancePinClass == TextureResidencyCache.PinClass.Player &&
           !string.IsNullOrWhiteSpace(category) &&
           !string.IsNullOrEmpty(defaultAnimation) &&
           string.IsNullOrEmpty(currentAnimation) &&
           seenAnimationCategories.Count == 0 &&
           enabledTargetCount > 1 &&
           HasMixedVisibleSpriteTargets();
  }

  bool ShouldHoldRuntimePlayerStartFrame(
    int enabledTargetCount,
    string category,
    AnimData animation
  ) {
    if (!Application.isPlaying) return false;
    if (appearancePinClass != TextureResidencyCache.PinClass.Player) return false;
    if (seenAnimationCategories.Count == 0) return false;
    if (enabledTargetCount <= 1) return false;
    if (enabledTargetCount > MaxTargetsForGateReadinessChecks) return false;
    if (string.IsNullOrWhiteSpace(category)) return false;
    if (animation == null) return false;
    if (HasSeenAnimationCategory(category)) return false;
    if (HasCurrentAppearancePinCoverage()) return false;
    if (!HasMixedVisibleSpriteTargets()) return false;

    var startFrame = Math.Max(animation.start, 1);
    return !AreAllTargetsReadyForWindow(category, startFrame, startFrame);
  }

  void BeginStartupVisualHold() {
    startupVisualHoldActive = true;
    startupVisualHoldStartedAt = Time.realtimeSinceStartup;
    HideStartupVisualTargetsForHold();
    if (!ShouldLogStartupVisualHold()) return;
    RuntimeLog.Log(
      "[AnimationController][StartupSync] stage=begin" +
      " animation='" + (defaultAnimation ?? "") + "'" +
      " targets=" + spriteTargets.Count +
      " enabled_targets=" + CountEnabledSpriteTargets()
    );
  }

  static bool ShouldLogStartupVisualHold() {
    if (!SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs) {
      return false;
    }
    return Application.isEditor || Debug.isDebugBuild;
  }

  void CompleteStartupVisualHold(string stage) {
    if (!startupVisualHoldActive) return;
    if (ShouldLogStartupVisualHold()) {
      RuntimeLog.Log(
        "[AnimationController][StartupSync] stage=" + (string.IsNullOrWhiteSpace(stage) ? "complete" : stage.Trim()) +
        " animation='" + (currentAnimation ?? "") + "'" +
        " hold_frame=" + holdFrame +
        " elapsed_ms=" + ((Time.realtimeSinceStartup - startupVisualHoldStartedAt) * 1000f).ToString("0.0")
      );
    }
    ResetStartupVisualHoldState();
  }

  void ResetStartupVisualHoldState() {
    RevealStartupVisualTargetsAfterHold();
    startupVisualHoldActive = false;
    startupVisualHoldStartedAt = 0f;
  }

  bool HasMixedVisibleSpriteTargets() {
    var hasVisibleCritical = false;
    var hasVisibleNonCritical = false;
    for (var i = 0; i < spriteTargets.Count; i++) {
      var target = spriteTargets[i];
      if (!IsSpriteTargetEnabled(target)) continue;
      if (IsCriticalSpriteTarget(target)) hasVisibleCritical = true;
      else hasVisibleNonCritical = true;
      if (hasVisibleCritical && hasVisibleNonCritical) return true;
    }
    return false;
  }

  void HideStartupVisualTargetsForHold() {
    startupVisualHoldSuppressedTargets.Clear();
    for (var i = 0; i < spriteTargets.Count; i++) {
      var target = spriteTargets[i];
      if (!IsSpriteTargetEnabled(target)) continue;
      target.SetExternalVisualSuppressed(true);
      startupVisualHoldSuppressedTargets.Add(target);
    }
  }

  void RevealStartupVisualTargetsAfterHold() {
    if (startupVisualHoldSuppressedTargets.Count <= 0) return;
    foreach (var target in startupVisualHoldSuppressedTargets) {
      if (target == null) continue;
      target.SetExternalVisualSuppressed(false);
    }
    startupVisualHoldSuppressedTargets.Clear();
  }
}
