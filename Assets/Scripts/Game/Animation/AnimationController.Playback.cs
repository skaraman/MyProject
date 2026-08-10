#pragma warning disable CS0162 // Unreachable code detected
using System;
using UnityEngine;

public partial class AnimationController {
  public void PauseAnimation(bool applyCurrentFrame = false) {
    EndActivePunchTrace("paused", currentFrame);
    isPlaying = false;
    ClearPendingAnimationSwitch();
    ClearStartFrameHold();
    CancelAllTweens();
    if (applyCurrentFrame) {
      ApplyCurrentFrameImmediately();
    }
  }

  void ApplyCurrentFrameImmediately() {
    if (currentFrame == int.MinValue || spriteTargets == null) return;

    lastAppliedSpriteFrame = currentFrame;
    lastSpriteApplyTime = Application.isPlaying ? Time.unscaledTime : -1f;
    SpriteApplyProfilerMarker.Begin();
    for (var i = 0; i < spriteTargets.Count; i++) {
      var target = spriteTargets[i];
      if (!IsSpriteTargetEnabled(target)) continue;
      target.ForceUpdateSpriteAndNormal(currentFrame);
    }
    SpriteApplyProfilerMarker.End();
  }

  public void ResumeAnimation() {
    if (!string.IsNullOrEmpty(currentAnimation)) {
      isPlaying = true;
      SetBounces();
    }
  }

  public void StopAnimation(bool resetToDefault = false) {
    EndActivePunchTrace("stopped", currentFrame);
    isPlaying = false;
    queuedAnimation = null;
    ClearPendingAnimationSwitch();
    ClearStartFrameHold();
    animationTimer = 0f;
    currentFrame = 0;
    CancelAllTweens();
    ResetOffensiveHBoxes();
    if (resetToDefault && !string.IsNullOrEmpty(defaultAnimation)) {
      PlayAnimation(defaultAnimation, true);
    }
  }

  public void TogglePause(string forcePause = null) {
    isPlaying = forcePause != null ? false : !isPlaying;
    foreach (var kvp in activeTweens) {
      foreach (int tweenId in kvp.Value) {
        if (isPlaying) {
          LeanTween.resume(tweenId);
        }
        else {
          LeanTween.pause(tweenId);
        }
      }
    }
  }

  public float GetAnimationDurationSeconds(string animationName) {
    if (animationData != null && animationData.TryGetValue(animationName, out var anim) && anim.duration > 0) {
      return ResolveAnimationPlaybackDurationMilliseconds(anim) / 1000f;
    }
    return 0f;
  }

  public bool HasReachedAnimationNormalizedTime(
    string animationName,
    float normalizedTime
  ) {
    if (!TryGetAnimationKey(animationName, out var resolvedAnimation)) return false;
    if (!string.Equals(currentAnimation, resolvedAnimation, StringComparison.Ordinal)) return false;
    if (!animationData.TryGetValue(resolvedAnimation, out var animation)) return false;
    if (animation == null || animation.duration <= 0) return false;

    var legDuration = ResolveAnimationDurationMilliseconds(animation);
    var elapsed = animation.pingPongOnce && pingPong
      ? legDuration + animationTimer
      : animationTimer;
    var threshold = ResolveAnimationPlaybackDurationMilliseconds(animation) * Mathf.Clamp01(normalizedTime);
    return elapsed >= threshold;
  }

  public void QueueFlip() {
    pendingFlip = true;
  }

  public void SetFacingDirection(float xDirection) {
    if (Mathf.Approximately(xDirection, 0f)) return;
    var faceRight = xDirection >= 0f;
    if (faceRight == isFacingRight) return;
    isFacingRight = faceRight;
    ApplyFlip();
  }

  public void Cleanup(bool resetLeanTweenManager) {
    EndActivePunchTrace("cleanup", currentFrame);
    ReleaseAppearancePins();
    ClearPendingAnimationSwitch();
    ClearStartFrameHold();
    CancelAllTweens();
    ResetOffensiveHBoxes();
    if (resetLeanTweenManager && !hasResetLeanTween) {
      LeanTween.reset();
      hasResetLeanTween = true;
    }
  }

  private void AdvanceAnimation(float deltaTime) {
    var deltaMs = deltaTime * 1000f;
    TryFinalizePendingAnimationSwitch();
    if (holdCurrentAnimationOnStartFrameUntilReady) {
      if (string.IsNullOrWhiteSpace(currentAnimation) || !animationData.ContainsKey(currentAnimation)) {
        ClearStartFrameHold();
      }
      else if (!AreAllTargetsReadyForWindow(holdCategory, holdFrame, holdFrame)) {
        PrimeTargetsForAnimation(holdCategory, holdFrame, holdFrame);
        var holdSeconds = Time.realtimeSinceStartup - pendingSwitchStartTime;
        if (holdSeconds >= MaxSwitchHardTimeoutSeconds) {
          UpdateSprites(holdFrame);
          CompleteStartupVisualHold("timeout");
          isPlaying = true;
          ClearStartFrameHold();
        }
        else {
          TraceActivePunchFrameStep(holdFrame, deltaMs);
          return;
        }
      }
      else {
        UpdateSprites(holdFrame);
        CompleteStartupVisualHold("ready");
        isPlaying = true;
        ClearStartFrameHold();
      }
    }
    if (!isPlaying || string.IsNullOrEmpty(currentAnimation) || animationData == null) return;
    if (!animationData.TryGetValue(currentAnimation, out var anim)) return;

    float slowFactor = SlowDown ? 20f : 1f;
    animationTimer += (deltaTime * 1000f) / slowFactor;
    float normalTime = animationTimer / Mathf.Max(1f, ResolveAnimationDurationMilliseconds(anim));
    bool cycleReset = false;

    if (!pingPong) {
      int frameOffset = Mathf.FloorToInt((anim.end - anim.start) * normalTime);
      currentFrame = anim.start + frameOffset;
      if (normalTime >= 1f) {
        if (!string.IsNullOrEmpty(queuedAnimation)) {
          TraceActivePunchFrameStep(anim.end, deltaMs);
          EndActivePunchTrace("queued_next", anim.end);
          var next = queuedAnimation;
          queuedAnimation = null;
          currentAnimation = null;
          if (!PlayAnimation(next, resolveInterrupts: false)) {
            if (!string.IsNullOrWhiteSpace(defaultAnimation) && animationData.ContainsKey(defaultAnimation)) {
              PlayAnimation(defaultAnimation, forceRestart: true, resolveInterrupts: false);
            }
            else {
              isPlaying = false;
            }
          }
          return;
        }
        if (anim.loop || ForceLoop) {
          currentFrame = anim.start;
          pingPong = false;
          animationTimer = 0f;
          SetBounces(resetOffensiveHBoxes: true);
          cycleReset = true;
        }
        else {
          currentFrame = anim.end;
          isPlaying = false;
          if (anim.pingPong || anim.pingPongOnce) {
            animationTimer = 0f;
            isPlaying = true;
            pingPong = true;
          }
        }
      }
    }
    else {
      int frameOffset = Mathf.FloorToInt((anim.end - anim.start) * normalTime);
      currentFrame = anim.end - frameOffset;
      if (normalTime >= 1f) {
        currentFrame = anim.start;
        pingPong = false;
        animationTimer = 0f;
        if (anim.pingPongOnce) {
          isPlaying = false;
        }
        else {
          isPlaying = true;
          SetBounces(resetOffensiveHBoxes: true);
          cycleReset = true;
        }
      }
    }

    if (pendingFlip) {
      pendingFlip = false;
      isFacingRight = !isFacingRight;
      ApplyFlip();
    }

    var renderCategory = string.IsNullOrWhiteSpace(activeSpriteCategory)
      ? ResolveAnimationCategory(currentAnimation, anim)
      : activeSpriteCategory;
    var frameToApply = ResolveVisualFrameForApply(currentFrame, renderCategory);
    UpdateSprites(frameToApply);
    if (cycleReset) {
      ResetAnimationEvents(anim);
    }
    TryTriggerFrameEvents(anim, lastFrame, currentFrame);
    lastFrame = currentFrame;
    TraceActivePunchFrameStep(currentFrame, deltaMs);
    if (!isPlaying && !anim.loop &&
        ((!anim.pingPong && !anim.pingPongOnce && currentFrame >= anim.end) ||
         (anim.pingPongOnce && currentFrame <= anim.start))) {
      EndActivePunchTrace("completed", currentFrame);
    }
  }

  float ResolveAnimationDurationMilliseconds(AnimData animation) {
    if (animation == null || animation.duration <= 0f) return 0f;
    if (animation.isLocomotion) return animation.duration;

    return AttackSpeedTiming.ResolveMoveDurationSeconds(
      animation.duration / 1000f,
      AttackSpeedSeconds
    ) * 1000f;
  }

  float ResolveAnimationPlaybackDurationMilliseconds(AnimData animation) {
    var duration = ResolveAnimationDurationMilliseconds(animation);
    return animation != null && animation.pingPongOnce ? duration * 2f : duration;
  }

  float ResolveCurrentAnimationDurationScale() {
    if (string.IsNullOrEmpty(currentAnimation) ||
        animationData == null ||
        !animationData.TryGetValue(currentAnimation, out var animation) ||
        animation == null ||
        animation.duration <= 0f) {
      return 1f;
    }

    return ResolveAnimationDurationMilliseconds(animation) / animation.duration;
  }

  private bool TryResolveInterrupt(string requestedAnimation, out string resolvedAnimation, out string queued) {
    resolvedAnimation = requestedAnimation;
    queued = null;
    if (string.IsNullOrEmpty(currentAnimation)) return true;
    if (interruptData != null && interruptData.TryGetValue(currentAnimation, out var allowedInterrupts)) {
      if (System.Array.IndexOf(allowedInterrupts, requestedAnimation) < 0) {
        return false;
      }
    }
    return true;
  }

  bool TryGetAnimationKey(string candidate, out string resolved) {
    resolved = "";
    if (animationData == null || animationData.Count == 0) return false;

    var normalized = NormalizeAnimationName(candidate);
    if (string.IsNullOrWhiteSpace(normalized)) return false;

    if (animationData.ContainsKey(normalized)) {
      resolved = normalized;
      return true;
    }

    if (animationKeyLookup.TryGetValue(normalized, out var keyed)) {
      resolved = keyed;
      return true;
    }

    return false;
  }

  static string NormalizeAnimationName(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    var hasZWSP = value.IndexOf('\u200B') >= 0;
    var hasBOM = value.IndexOf('\uFEFF') >= 0;
    if (hasZWSP || hasBOM) {
      return value.Replace("\u200B", "").Replace("\uFEFF", "").Trim();
    }
    return value;
  }

  void CommitAnimationSwitch(
    string nextAnimation,
    string nextQueuedAnimation,
    string targetCategory,
    bool holdOnStartFrameUntilReady = false,
    bool deferInitialVisualApply = false
  ) {
    EndActivePunchTrace("interrupted_by_switch", currentFrame);
    currentAnimation = nextAnimation;
    queuedAnimation = string.IsNullOrWhiteSpace(nextQueuedAnimation) ? null : nextQueuedAnimation;

    animationTimer = 0f;
    pingPong = false;
    isPlaying = !holdOnStartFrameUntilReady;
    var anim = animationData[currentAnimation];
    currentFrame = anim.start;

    if (holdOnStartFrameUntilReady) {
      holdCurrentAnimationOnStartFrameUntilReady = true;
      holdCategory = targetCategory;
      holdFrame = Math.Max(anim.start, 1);
      pendingSwitchStartTime = Time.realtimeSinceStartup;
    }
    else {
      ClearStartFrameHold();
    }

    SwitchCategoryProfilerMarker.Begin();
    SetAnimationCategory(targetCategory);
    SwitchCategoryProfilerMarker.End();
    if (holdOnStartFrameUntilReady) {
      PrimeTargetsForAnimation(targetCategory, currentFrame, currentFrame);
    }
    else if (!deferInitialVisualApply) {
      if (ShouldDeferInitialVisualApply(targetCategory, currentFrame)) {
        PrimeTargetsForAnimation(targetCategory, currentFrame, currentFrame);
      }
      else {
        UpdateSprites(currentFrame);
      }
    }
    SwitchBounceProfilerMarker.Begin();
    SetBounces(resetOffensiveHBoxes: true);
    SwitchBounceProfilerMarker.End();

    SwitchEventsProfilerMarker.Begin();
    ResetAnimationEvents(anim);
    TryTriggerFrameEvents(anim, lastFrame, currentFrame);
    lastFrame = currentFrame;
    MarkAnimationCategorySeen(targetCategory);
    BeginActivePunchTrace(anim, targetCategory);
    SwitchEventsProfilerMarker.End();
  }

  bool ShouldDeferInitialVisualApply(string targetCategory, int frame) {
    if (!Application.isPlaying) return false;
    if (string.IsNullOrWhiteSpace(targetCategory)) return false;
    if (spriteTargets.Count <= 1) return false;

    var loadingOverlayWarmGateActive =
      SpriteStreamingLoadingState.IsLoadingOverlayActive &&
      StreamingWarmOrchestrator.IsWarmGateRunning;
    if (loadingOverlayWarmGateActive) return false;
    if (spriteTargets.Count > MaxTargetsForGateReadinessChecks) return false;
    if (HasSeenAnimationCategory(targetCategory)) return false;
    if (!HasMixedVisibleSpriteTargets()) return false;
    if (AreAllTargetsReadyForFrame(targetCategory, frame)) return false;

    return true;
  }

  static string ResolveAnimationCategory(string animationName, AnimData anim) {
    if (anim == null) return animationName ?? "";
    if (anim.To == 1) return "To";
    if (anim.To == 2) return "To2";
    if (string.IsNullOrWhiteSpace(anim.category)) return animationName ?? "";
    
    // Avoid allocating strings on every frame with Trim().
    // Try returning the original string if it doesn't need trimming.
    bool needsTrim = false;
    if (anim.category.Length > 0) {
      if (char.IsWhiteSpace(anim.category[0]) || char.IsWhiteSpace(anim.category[anim.category.Length - 1])) {
        needsTrim = true;
      }
    }
    return needsTrim ? anim.category.Trim() : anim.category;
  }

  bool HasSeenAnimationCategory(string category) {
    if (string.IsNullOrWhiteSpace(category)) return false;
    return seenAnimationCategories.Contains(category);
  }

  void MarkAnimationCategorySeen(string category) {
    if (string.IsNullOrWhiteSpace(category)) return;
    seenAnimationCategories.Add(category);
  }

  private void SetAnimationCategory(string category) {
    var normalizedCategory = category ?? "";
    if (EnableAttackTraceLogs) LogAttackTrace("set_category", category: normalizedCategory, note: "targets=" + spriteTargets.Count);
    activeSpriteCategory = normalizedCategory;
    foreach (var target in spriteTargets) {
      if (target == null) continue;
      target.SetAnimation(normalizedCategory, currentFrame);
    }
    InvalidateSpriteFrameCache();
  }

  private void ResetAnimationEvents(AnimData anim) {
    effectTriggered = false;
    projectileTriggered = false;
    lastFrame = anim != null ? anim.start - 1 : int.MinValue;
  }

  private void TryTriggerFrameEvents(AnimData anim, int previousFrame, int currentFrame) {
    if (anim == null) return;
    if (!effectTriggered && !string.IsNullOrEmpty(anim.effect) && anim.effectFrame > 0) {
      if (previousFrame < anim.effectFrame && currentFrame >= anim.effectFrame) {
        effectTriggered = true;
        OnEffectTriggered?.Invoke(anim.effect);
      }
    }
    if (!projectileTriggered && !string.IsNullOrEmpty(anim.projectile) && anim.projectileFrame > 0) {
      if (previousFrame < anim.projectileFrame && currentFrame >= anim.projectileFrame) {
        projectileTriggered = true;
        OnProjectileTriggered?.Invoke(anim.projectile);
      }
    }
  }

  private void UpdateSprites(int frame) {
    if (frame == int.MinValue) return;
    if (frame == lastAppliedSpriteFrame) return;

    if (Application.isPlaying) {
      var now = Time.unscaledTime;
      if (lastSpriteApplyTime >= 0f && now - lastSpriteApplyTime < 0.016f) {
        return;
      }
      lastSpriteApplyTime = now;
    }

    lastAppliedSpriteFrame = frame;

    SpriteApplyProfilerMarker.Begin();
    foreach (var target in spriteTargets) {
      if (!IsSpriteTargetEnabled(target)) continue;
      target.UpdateSpriteAndNormal(frame);
    }
    SpriteApplyProfilerMarker.End();
  }

  private void ApplyFlip() {
    if (rootTransform == null) return;
    var scale = baseScale;
    scale.x = Mathf.Abs(scale.x) * (isFacingRight ? 1f : -1f);
    rootTransform.localScale = scale;
    SetBounces();
    
    var softBones = rootTransform.GetComponentsInChildren<EZhex1991.EZSoftBone.EZSoftBone>(true);
    for (int i = 0; i < softBones.Length; i++) {
      softBones[i].SetRestState();
    }
  }
}
