#pragma warning disable CS0162 // Unreachable code detected
using System;
using UnityEngine;

public partial class AnimationController {
  void BeginActivePunchTrace(AnimData anim, string category) {
    if (anim == null || string.IsNullOrWhiteSpace(currentAnimation)) {
      ResetActivePunchTraceState();
      return;
    }
    if (!PunchLeftTraceGate.ShouldTraceAnimation(currentAnimation, category)) {
      ResetActivePunchTraceState();
      return;
    }
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    activePunchTrace = true;
    activePunchTraceAnimation = currentAnimation;
    activePunchTraceCategory = category ?? "";
    activePunchTraceStartRealtime = Time.realtimeSinceStartup;
    activePunchTracePreviousFrame = currentFrame;
    activePunchTraceAdvancedFrames = 0;
    activePunchTraceSkippedFrames = 0;
    PunchLeftTraceGate.LogAnimationStart(
      currentAnimation,
      category,
      anim.start,
      anim.end,
      queuedAnimation,
      queue.queuedCount,
      queue.inFlightCount
    );
  }

  void TraceActivePunchFrameStep(int targetFrame, float deltaMs) {
    if (!activePunchTrace) return;
    if (activePunchTracePreviousFrame == int.MinValue) {
      activePunchTracePreviousFrame = targetFrame;
      return;
    }
    var fromFrame = activePunchTracePreviousFrame;
    var toFrame = targetFrame;
    var delta = toFrame - fromFrame;
    if (delta > 0) {
      activePunchTraceAdvancedFrames += delta;
      if (delta > 1) {
        activePunchTraceSkippedFrames += delta - 1;
      }
    }
    PunchLeftTraceGate.LogFrameAdvance(
      activePunchTraceAnimation,
      activePunchTraceCategory,
      fromFrame,
      toFrame,
      deltaMs
    );
    activePunchTracePreviousFrame = toFrame;
  }

  void EndActivePunchTrace(string reason, int finalFrame) {
    if (!activePunchTrace) return;
    var elapsedMs = Mathf.Max((Time.realtimeSinceStartup - activePunchTraceStartRealtime) * 1000f, 0f);
    PunchLeftTraceGate.LogAnimationEnd(
      activePunchTraceAnimation,
      activePunchTraceCategory,
      finalFrame,
      reason,
      elapsedMs,
      activePunchTraceAdvancedFrames,
      activePunchTraceSkippedFrames
    );
    ResetActivePunchTraceState();
  }

  void ResetActivePunchTraceState() {
    activePunchTrace = false;
    activePunchTraceAnimation = null;
    activePunchTraceCategory = null;
    activePunchTraceStartRealtime = 0f;
    activePunchTracePreviousFrame = int.MinValue;
    activePunchTraceAdvancedFrames = 0;
    activePunchTraceSkippedFrames = 0;
  }

  void LogAttackTrace(
    string stage,
    string requestedAnimation = null,
    string resolvedAnimation = null,
    string queuedAnimationName = null,
    string category = null,
    string note = null
  ) {
    if (!ShouldLogAttackTrace(requestedAnimation, resolvedAnimation, queuedAnimationName, category)) return;
    Debug.Log(
      "[AnimationController][AttackTrace] stage='" + (stage ?? "") +
      "' requested='" + (requestedAnimation ?? "") +
      "' resolved='" + (resolvedAnimation ?? "") +
      "' queued='" + (queuedAnimationName ?? "") +
      "' category='" + (category ?? "") +
      "' current='" + (currentAnimation ?? "") +
      "' pending='" + (pendingAnimation ?? "") +
      "' has_pending=" + (hasPendingAnimationSwitch ? 1 : 0) +
      " playing=" + (isPlaying ? 1 : 0) +
      " frame=" + currentFrame +
      " note='" + (note ?? "") + "'"
    );
  }

  bool ShouldLogAttackTrace(
    string requestedAnimation,
    string resolvedAnimation,
    string queuedAnimationName,
    string category
  ) {
    if (!EnableAttackTraceLogs) return false;
    return IsPunchLeftTraceToken(requestedAnimation) ||
           IsPunchLeftTraceToken(resolvedAnimation) ||
           IsPunchLeftTraceToken(queuedAnimationName) ||
           IsPunchLeftTraceToken(category) ||
           IsPunchLeftTraceToken(currentAnimation) ||
           IsPunchLeftTraceToken(pendingAnimation) ||
           IsPunchLeftTraceToken(queuedAnimation);
  }

  static bool IsPunchLeftTraceToken(string value) {
    if (string.IsNullOrWhiteSpace(value)) return false;
    return value.IndexOf("PunchLeft", StringComparison.OrdinalIgnoreCase) >= 0;
  }
}
