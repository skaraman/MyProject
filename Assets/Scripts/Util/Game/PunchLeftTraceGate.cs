using System;
using UnityEngine;

public static class PunchLeftTraceGate {
  static readonly bool EnableTraceLogs = false;
  const int ActiveFrameWindow = 900;
  const float HitchDeltaMsThreshold = 22f;

  static int activeUntilFrame = -1;
  static int sequence;

  public static bool IsActive {
    get {
      if (!EnableTraceLogs) return false;
      if (!Application.isPlaying) return false;
      return Time.frameCount <= activeUntilFrame;
    }
  }

  public static int Sequence => sequence;

  public static bool ContainsPunchLeft(string value) {
    if (string.IsNullOrWhiteSpace(value)) return false;
    return value.IndexOf("PunchLeft", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  [System.Diagnostics.Conditional("ENABLE_RUNTIME_DEBUG_LOGS")]
  public static void OpenFromClick(string actionKey, string mappedAnimation, string currentAnimation) {
    if (!EnableTraceLogs) return;
    if (!ContainsPunchLeft(mappedAnimation)) return;
    sequence++;
    activeUntilFrame = Time.frameCount + ActiveFrameWindow;
    RuntimeLog.Log(
      "[PunchLeftTrace][Click] seq=" + sequence +
      " frame=" + Time.frameCount +
      " action='" + (actionKey ?? "") +
      "' mapped='" + (mappedAnimation ?? "") +
      "' current='" + (currentAnimation ?? "") + "'"
    );
  }

  [System.Diagnostics.Conditional("ENABLE_RUNTIME_DEBUG_LOGS")]
  public static void LogClickDispatchResult(
    string actionKey,
    string mappedAnimation,
    bool played,
    string currentAnimation
  ) {
    if (!EnableTraceLogs) return;
    if (!ContainsPunchLeft(mappedAnimation) && !ContainsPunchLeft(currentAnimation)) return;
    RuntimeLog.Log(
      "[PunchLeftTrace][Dispatch] seq=" + sequence +
      " frame=" + Time.frameCount +
      " action='" + (actionKey ?? "") +
      "' mapped='" + (mappedAnimation ?? "") +
      "' played=" + (played ? 1 : 0) +
      " current='" + (currentAnimation ?? "") + "'"
    );
  }

  public static bool ShouldTraceAnimation(string animationName, string category = null) {
    if (!EnableTraceLogs) return false;
    if (!IsActive) return false;
    return ContainsPunchLeft(animationName) || ContainsPunchLeft(category);
  }

  [System.Diagnostics.Conditional("ENABLE_RUNTIME_DEBUG_LOGS")]
  public static void LogAnimationStart(
    string animationName,
    string category,
    int startFrame,
    int endFrame,
    string queuedAnimation,
    int queuedLoads,
    int inFlightLoads
  ) {
    if (!ShouldTraceAnimation(animationName, category)) return;
    RuntimeLog.Log(
      "[PunchLeftTrace][AnimStart] seq=" + sequence +
      " frame=" + Time.frameCount +
      " animation='" + (animationName ?? "") +
      "' category='" + (category ?? "") +
      "' start=" + startFrame +
      " end=" + endFrame +
      " queued='" + (queuedAnimation ?? "") +
      "' queue_loads=" + queuedLoads +
      " in_flight=" + inFlightLoads
    );
  }

  [System.Diagnostics.Conditional("ENABLE_RUNTIME_DEBUG_LOGS")]
  public static void LogAnimationEnd(
    string animationName,
    string category,
    int finalFrame,
    string reason,
    float elapsedMs,
    int advancedFrames,
    int skippedFrames
  ) {
    if (!ShouldTraceAnimation(animationName, category)) return;
    RuntimeLog.Log(
      "[PunchLeftTrace][AnimEnd] seq=" + sequence +
      " frame=" + Time.frameCount +
      " animation='" + (animationName ?? "") +
      "' category='" + (category ?? "") +
      "' final_frame=" + finalFrame +
      " reason='" + (reason ?? "") +
      "' elapsed_ms=" + elapsedMs.ToString("0.0") +
      " advanced_frames=" + advancedFrames +
      " skipped_frames=" + skippedFrames
    );
  }

  [System.Diagnostics.Conditional("ENABLE_RUNTIME_DEBUG_LOGS")]
  public static void LogFrameAdvance(
    string animationName,
    string category,
    int fromFrame,
    int toFrame,
    float deltaMs
  ) {
    if (!ShouldTraceAnimation(animationName, category)) return;
    var frameDelta = toFrame - fromFrame;
    if (Mathf.Abs(frameDelta) <= 1 && deltaMs < HitchDeltaMsThreshold) return;
    RuntimeLog.Log(
      "[PunchLeftTrace][FrameStep] seq=" + sequence +
      " frame=" + Time.frameCount +
      " animation='" + (animationName ?? "") +
      "' category='" + (category ?? "") +
      "' from=" + fromFrame +
      " to=" + toFrame +
      " delta=" + frameDelta +
      " dt_ms=" + deltaMs.ToString("0.0")
    );
  }

  public static bool ShouldTraceCategory(string category) {
    if (!EnableTraceLogs) return false;
    if (!IsActive) return false;
    return ContainsPunchLeft(category);
  }

  [System.Diagnostics.Conditional("ENABLE_RUNTIME_DEBUG_LOGS")]
  public static void LogFrameRequest(
    string objectName,
    string category,
    int requestedFrame,
    string libraryName,
    string labelPrefix
  ) {
    if (!EnableTraceLogs) return;
    if (!ShouldTraceCategory(category)) return;
    RuntimeLog.Log(
      "[PunchLeftTrace][FrameRequest] seq=" + sequence +
      " frame=" + Time.frameCount +
      " object='" + (objectName ?? "") +
      "' category='" + (category ?? "") +
      "' requested_frame=" + requestedFrame +
      " library='" + (libraryName ?? "") +
      "' label='" + (labelPrefix ?? "") + "'"
    );
  }
}
