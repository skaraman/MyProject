#pragma warning disable CS0162 // Unreachable code detected
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class AnimationController {
  public void ReleaseAppearancePins() {
    if (string.IsNullOrWhiteSpace(appearanceOwnerId)) return;
    TextureResidencyCache.ReleaseOwnerPins(appearanceOwnerId);
    appearancePinAddressBuffer.Clear();
    appearancePinAddressSet.Clear();
    InvalidateAppearancePinSnapshot();
  }

  public void SetAppearancePinOwner(
    string newOwnerId,
    TextureResidencyCache.PinClass newPinClass = TextureResidencyCache.PinClass.Enemy
  ) {
    var normalizedOwnerId = string.IsNullOrWhiteSpace(newOwnerId) ? "" : newOwnerId.Trim();
    if (string.Equals(appearanceOwnerId, normalizedOwnerId, StringComparison.Ordinal) &&
        appearancePinClass == newPinClass) {
      return;
    }

    if (!string.IsNullOrWhiteSpace(appearanceOwnerId)) {
      TextureResidencyCache.ReleaseOwnerPins(appearanceOwnerId);
    }

    appearanceOwnerId = normalizedOwnerId;
    appearancePinClass = newPinClass;
    appearancePinAddressBuffer.Clear();
    appearancePinAddressSet.Clear();
    InvalidateAppearancePinSnapshot();
  }

  public void InvalidateSpriteFrameCache() {
    lastAppliedSpriteFrame = int.MinValue;
    lastAppliedSpriteCategory = null;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetRuntimeSwitchSettingsCache() {
    runtimeSettingsLoaded = false;
    runtimeWarmupFrames = 24;
    runtimeSwitchGateMs = 0;
  }

  static void EnsureRuntimeSwitchSettings() {
    if (runtimeSettingsLoaded) return;
    runtimeSettingsLoaded = true;
    runtimeWarmupFrames = SpriteStreamingRuntimeSettings.AnimationWarmupFrames;
    runtimeSwitchGateMs = SpriteStreamingRuntimeSettings.AnimationSwitchGateMs;
  }

  void BeginPendingAnimationSwitch(
    string nextAnimation,
    string nextQueuedAnimation,
    string nextCategory,
    int startFrame,
    int readyEndFrame,
    int gateMs
  ) {
    if (hasPendingAnimationSwitch &&
        string.Equals(pendingAnimation, nextAnimation, StringComparison.Ordinal) &&
        string.Equals(pendingCategory, nextCategory, StringComparison.Ordinal) &&
        pendingStartFrame == startFrame &&
        pendingReadyEndFrame == readyEndFrame) {
      if (!string.IsNullOrWhiteSpace(nextQueuedAnimation)) {
        pendingQueuedAnimation = nextQueuedAnimation;
      }
      return;
    }

    hasPendingAnimationSwitch = true;
    pendingAnimation = nextAnimation ?? "";
    pendingQueuedAnimation = nextQueuedAnimation ?? "";
    pendingCategory = nextCategory ?? "";
    pendingStartFrame = startFrame;
    pendingReadyEndFrame = Math.Max(readyEndFrame, startFrame);
    pendingSwitchStartTime = Time.realtimeSinceStartup;
    pendingSwitchDeadline = pendingSwitchStartTime + Math.Max(gateMs, 0) / 1000f;
    RefreshAppearancePins();
  }

  void TryFinalizePendingAnimationSwitch() {
    if (!hasPendingAnimationSwitch) return;
    if (animationData == null || string.IsNullOrWhiteSpace(pendingAnimation) || !animationData.ContainsKey(pendingAnimation)) {
      ClearPendingAnimationSwitch();
      return;
    }

    var now = Time.realtimeSinceStartup;
    PrimeTargetsForAnimation(pendingCategory, pendingStartFrame, pendingReadyEndFrame);
    var isReady = AreTargetsReadyForWindow(pendingCategory, pendingStartFrame, pendingReadyEndFrame);

    if (!isReady) {
      if (isPlaying && now < pendingSwitchDeadline) return;

      var isFirstFrameReady = AreTargetsReadyForWindow(pendingCategory, pendingStartFrame, pendingStartFrame);
      if (!isFirstFrameReady && now < pendingSwitchStartTime + MaxSwitchHardTimeoutSeconds) return;

      var timeoutWaitMs = Mathf.Max((now - pendingSwitchStartTime) * 1000f, 0f);
      SpriteStreamingDiagnostics.RecordAnimationSwitchWait(timeoutWaitMs, timeoutWaitMs > 0.1f);

      if (!isFirstFrameReady &&
          IsTransitionCategory(pendingCategory) &&
          !string.IsNullOrWhiteSpace(pendingQueuedAnimation) &&
          TryGetAnimationKey(pendingQueuedAnimation, out var queuedResolved) &&
          animationData.TryGetValue(queuedResolved, out var queuedAnim) &&
          queuedAnim != null) {
        var queuedCategory = ResolveAnimationCategory(queuedResolved, queuedAnim);
        if (!SuppressRuntimeWarningLogsForPerfPass) {
          Debug.LogWarning(
            "[AnimationController] Transition gate timed out; skipping transition. " +
            "transition='" + pendingAnimation +
            "' queued='" + queuedResolved +
            "' wait_ms=" + timeoutWaitMs.ToString("0.0")
          );
        }
        CommitAnimationSwitch(
          queuedResolved,
          nextQueuedAnimation: null,
          queuedCategory,
          holdOnStartFrameUntilReady: false
        );
        ClearPendingAnimationSwitch();
        return;
      }

      if (!isFirstFrameReady) {
        if (!SuppressRuntimeWarningLogsForPerfPass) {
          Debug.LogWarning(
            "[AnimationController] Switch gate timed out; committing without start-frame hold. " +
            "animation='" + pendingAnimation +
            "' category='" + pendingCategory +
            "' wait_ms=" + timeoutWaitMs.ToString("0.0") +
            " start_frame=" + pendingStartFrame +
            " ready_end_frame=" + pendingReadyEndFrame
          );
        }
      }
      CommitAnimationSwitch(
        pendingAnimation,
        pendingQueuedAnimation,
        pendingCategory,
        holdOnStartFrameUntilReady: false
      );
      ClearPendingAnimationSwitch();
      return;
    }

    var waitMs = Mathf.Max((now - pendingSwitchStartTime) * 1000f, 0f);
    SpriteStreamingDiagnostics.RecordAnimationSwitchWait(waitMs, waitMs > 0.1f);
    CommitAnimationSwitch(pendingAnimation, pendingQueuedAnimation, pendingCategory);
    ClearPendingAnimationSwitch();
  }

  void PrimeTargetsForAnimation(string targetCategory, int startFrame, int endFrame, bool primeFullWindow = false) {
    var warmupFrames = Math.Max(runtimeWarmupFrames, 1);
    var firstPlayActionFrames = Math.Max(warmupFrames, FirstPlayActionPrimeMaxFrames);
    var clampedStart = Math.Max(startFrame, 1);
    var clampedEnd = Math.Max(endFrame, clampedStart);
    var primeWindowFrames = primeFullWindow ? firstPlayActionFrames : warmupFrames;
    var targetEnd = Math.Min(clampedEnd, clampedStart + primeWindowFrames - 1);
    var immediateBudget = PrimeImmediateStartFrameBudget;
    PrimeTargetsForAnimationSet(
      criticalSpriteTargets,
      targetCategory,
      clampedStart,
      targetEnd,
      skipCriticalTargets: false,
      maxTargets: int.MaxValue,
      ref immediateBudget
    );
    PrimeTargetsForAnimationSet(
      spriteTargets,
      targetCategory,
      clampedStart,
      targetEnd,
      skipCriticalTargets: true,
      maxTargets: int.MaxValue,
      ref immediateBudget
    );
  }

  public void PrimeAllAnimationStarts(int framesPerAnimation = 1, int maxAnimations = 0) {
    if (!Application.isPlaying) return;
    if (animationData == null || animationData.Count == 0 || spriteTargets.Count == 0) return;

    var warmFrames = Math.Max(framesPerAnimation, 1);
    var primed = 0;
    foreach (var pair in animationData) {
      var animationName = pair.Key;
      var anim = pair.Value;
      if (anim == null || string.IsNullOrWhiteSpace(animationName)) continue;

      var categoryName = ResolveAnimationCategory(animationName, anim);
      var startFrame = Math.Max(anim.start, 1);
      var endFrame = Math.Max(startFrame, startFrame + warmFrames - 1);
      PrimeTargetsForAnimation(categoryName, startFrame, endFrame);

      primed++;
      if (maxAnimations > 0 && primed >= maxAnimations) break;
    }
  }

  public void PrimeAnimationStarts(IReadOnlyList<string> animationKeys, int framesPerAnimation = 1, int maxAnimations = 0) {
    if (!Application.isPlaying) return;
    if (animationData == null || animationData.Count == 0 || spriteTargets.Count == 0) return;
    if (animationKeys == null || animationKeys.Count <= 0) {
      PrimeAllAnimationStarts(framesPerAnimation, maxAnimations);
      return;
    }

    var warmFrames = Math.Max(framesPerAnimation, 1);
    var primed = 0;
    for (var i = 0; i < animationKeys.Count; i++) {
      if (maxAnimations > 0 && primed >= maxAnimations) {
        break;
      }

      if (!TryGetAnimationKey(animationKeys[i], out var animationName)) {
        continue;
      }

      if (!animationData.TryGetValue(animationName, out var anim) || anim == null) {
        continue;
      }

      var categoryName = ResolveAnimationCategory(animationName, anim);
      var startFrame = Math.Max(anim.start, 1);
      var endFrame = Math.Max(startFrame, startFrame + warmFrames - 1);
      PrimeTargetsForAnimation(categoryName, startFrame, endFrame);
      primed++;
    }
  }

  public int WarmAllAnimationPlayback(
    int passCount = 1,
    int maxAnimations = 12,
    int maxFramesPerAnimation = MaxSwitchReadinessWindowFrames
  ) {
    if (!Application.isPlaying) return 0;
    var addresses = warmPlaybackAddressScratch;
    var seenAddresses = warmPlaybackSeenAddressScratch;
    addresses.Clear();
    seenAddresses.Clear();
    var added = CollectWarmPlaybackAddresses(addresses, seenAddresses, passCount, maxAnimations, maxFramesPerAnimation);
    if (addresses.Count > 0) {
      TextureResidencyCache.RequestLoadBatch(
        addresses,
        TextureResidencyCache.LoadPriority.Warmup,
        allowAtlasExpansion: true
      );
    }
    addresses.Clear();
    seenAddresses.Clear();
    return added;
  }

  public int CollectWarmPlaybackAddresses(
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int passCount = 1,
    int maxAnimations = 12,
    int maxFramesPerAnimation = MaxSwitchReadinessWindowFrames,
    int maxAddresses = int.MaxValue
  ) {
    if (animationData == null || animationData.Count == 0 || spriteTargets.Count == 0) return 0;
    if (outAddresses == null) return 0;

    var beforeCount = outAddresses.Count;
    var passes = Math.Max(passCount, 1);
    var cappedAnimations = maxAnimations > 0 ? maxAnimations : animationData.Count;
    cappedAnimations = Math.Max(cappedAnimations, 1);
    var frameLimit = Math.Max(maxFramesPerAnimation, 1);
    var prioritizedAnimations = CollectWarmPlaybackAnimations(cappedAnimations);

    for (var pass = 0; pass < passes; pass++) {
      for (var i = 0; i < prioritizedAnimations.Count; i++) {
        if (outAddresses.Count >= maxAddresses) return Math.Max(outAddresses.Count - beforeCount, 0);
        var animationName = prioritizedAnimations[i];
        if (!animationData.TryGetValue(animationName, out var anim) || anim == null) continue;
        if (string.IsNullOrWhiteSpace(animationName)) continue;

        var categoryName = ResolveAnimationCategory(animationName, anim);
        var startFrame = Math.Max(anim.start, 1);
        var clipEnd = Math.Max(anim.end, startFrame);
        var endFrame = Math.Min(clipEnd, startFrame + frameLimit - 1);

        CollectAnimationStartAddressesForTargetSet(
          criticalSpriteTargets,
          categoryName,
          startFrame,
          endFrame,
          outAddresses,
          seenAddresses,
          maxAddresses,
          skipCriticalTargets: false
        );
        if (outAddresses.Count >= maxAddresses) return Math.Max(outAddresses.Count - beforeCount, 0);
        CollectAnimationStartAddressesForTargetSet(
          spriteTargets,
          categoryName,
          startFrame,
          endFrame,
          outAddresses,
          seenAddresses,
          maxAddresses,
          skipCriticalTargets: true
        );
      }
    }

    return Math.Max(outAddresses.Count - beforeCount, 0);
  }

  List<string> CollectWarmPlaybackAnimations(int maxAnimations) {
    maxAnimations = Math.Max(maxAnimations, 1);
    var ordered = warmPlaybackAnimationScratch;
    var seen = warmPlaybackAnimationSeenScratch;
    ordered.Clear();
    seen.Clear();

    var targetCapacity = Math.Min(maxAnimations, animationData.Count);
    if (targetCapacity > ordered.Capacity) {
      ordered.Capacity = targetCapacity;
    }

    TryAddWarmPlaybackAnimation(currentAnimation, ordered, seen, maxAnimations);
    TryAddWarmPlaybackAnimation(defaultAnimation, ordered, seen, maxAnimations);
    TryAddWarmPlaybackAnimation(pendingAnimation, ordered, seen, maxAnimations);
    TryAddWarmPlaybackAnimation(queuedAnimation, ordered, seen, maxAnimations);

    if (ordered.Count < maxAnimations) {
      foreach (var pair in animationData) {
        if (ordered.Count >= maxAnimations) break;
        if (!IsLocomotionAnimationName(pair.Key)) continue;
        TryAddWarmPlaybackAnimation(pair.Key, ordered, seen, maxAnimations);
      }
    }

    if (ordered.Count < maxAnimations) {
      foreach (var pair in animationData) {
        if (ordered.Count >= maxAnimations) break;
        TryAddWarmPlaybackAnimation(pair.Key, ordered, seen, maxAnimations);
      }
    }

    return ordered;
  }

  void TryAddWarmPlaybackAnimation(
    string candidate,
    List<string> ordered,
    HashSet<string> seen,
    int maxAnimations
  ) {
    if (ordered == null || seen == null) return;
    if (ordered.Count >= maxAnimations) return;
    if (!TryGetAnimationKey(candidate, out var resolved)) return;
    if (!seen.Add(resolved)) return;
    ordered.Add(resolved);
  }

  public int CollectAnimationStartAddresses(
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int framesPerAnimation = 1,
    int maxAnimations = 0,
    int maxAddresses = int.MaxValue
  ) {
    if (!Application.isPlaying) return 0;
    if (outAddresses == null) return 0;
    if (animationData == null || animationData.Count == 0 || spriteTargets.Count == 0) return 0;

    maxAddresses = Math.Max(maxAddresses, 1);
    if (outAddresses.Count >= maxAddresses) return 0;
    var warmFrames = Math.Max(framesPerAnimation, 1);
    var collectedAnimations = 0;
    var beforeCount = outAddresses.Count;

    foreach (var pair in animationData) {
      if (outAddresses.Count >= maxAddresses) break;
      var animationName = pair.Key;
      var anim = pair.Value;
      if (anim == null || string.IsNullOrWhiteSpace(animationName)) continue;

      var categoryName = ResolveAnimationCategory(animationName, anim);
      var startFrame = Math.Max(anim.start, 1);
      var endFrame = Math.Max(startFrame, startFrame + warmFrames - 1);

      CollectAnimationStartAddressesForTargetSet(
        criticalSpriteTargets,
        categoryName,
        startFrame,
        endFrame,
        outAddresses,
        seenAddresses,
        maxAddresses
      );
      if (outAddresses.Count < maxAddresses) {
        CollectAnimationStartAddressesForTargetSet(
          spriteTargets,
          categoryName,
          startFrame,
          endFrame,
          outAddresses,
          seenAddresses,
          maxAddresses,
          skipCriticalTargets: true
        );
      }

      collectedAnimations++;
      if (maxAnimations > 0 && collectedAnimations >= maxAnimations) break;
    }

    return Math.Max(outAddresses.Count - beforeCount, 0);
  }

  public int CollectAllAnimationFrameAddresses(
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxAddresses = int.MaxValue
  ) {
    if (!Application.isPlaying) return 0;
    if (outAddresses == null) return 0;
    if (animationData == null || animationData.Count == 0 || spriteTargets.Count == 0) return 0;

    maxAddresses = Math.Max(maxAddresses, 1);
    if (outAddresses.Count >= maxAddresses) return 0;
    var beforeCount = outAddresses.Count;

    foreach (var pair in animationData) {
      if (outAddresses.Count >= maxAddresses) break;
      var animationName = pair.Key;
      var anim = pair.Value;
      if (anim == null || string.IsNullOrWhiteSpace(animationName)) continue;

      var categoryName = ResolveAnimationCategory(animationName, anim);
      var startFrame = Math.Max(anim.start, 1);
      var endFrame = Math.Max(anim.end, startFrame);

      CollectAnimationStartAddressesForTargetSet(
        criticalSpriteTargets,
        categoryName,
        startFrame,
        endFrame,
        outAddresses,
        seenAddresses,
        maxAddresses
      );
      if (outAddresses.Count < maxAddresses) {
        CollectAnimationStartAddressesForTargetSet(
          spriteTargets,
          categoryName,
          startFrame,
          endFrame,
          outAddresses,
          seenAddresses,
          maxAddresses,
          skipCriticalTargets: true
        );
      }
    }

    return Math.Max(outAddresses.Count - beforeCount, 0);
  }

  void CollectAnimationStartAddressesForTargetSet(
    List<SpriteWithNormals> targets,
    string categoryName,
    int startFrame,
    int endFrame,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxAddresses,
    bool skipCriticalTargets = false
  ) {
    if (targets == null || targets.Count == 0) return;
    if (outAddresses == null || outAddresses.Count >= maxAddresses) return;

    for (var i = 0; i < targets.Count; i++) {
      if (outAddresses.Count >= maxAddresses) return;
      var target = targets[i];
      if (target == null) continue;
      if (skipCriticalTargets && IsCriticalSpriteTarget(target)) continue;
      if (!IsSpriteTargetEnabled(target)) continue;

      target.CollectAnimationWindowAddresses(
        categoryName,
        startFrame,
        endFrame,
        lookAheadFrames: 0,
        outAddresses,
        seenAddresses,
        maxAddresses
      );
    }
  }

  bool AreTargetsReadyForFrame(string targetCategory, int frame) {
    return AreTargetsReadyForWindow(targetCategory, frame, frame);
  }

  bool AreTargetsReadyForWindow(string targetCategory, int startFrame, int endFrame) {
    var minFrame = Math.Max(startFrame, 1);
    var maxFrame = Math.Max(endFrame, minFrame);
    var hasCriticalTargets = false;
    for (var i = 0; i < criticalSpriteTargets.Count; i++) {
      var target = criticalSpriteTargets[i];
      if (!IsSpriteTargetEnabled(target)) continue;
      hasCriticalTargets = true;
      for (var frame = minFrame; frame <= maxFrame; frame++) {
        if (!target.IsFrameReady(frame, out _, targetCategory)) return false;
      }
    }

    if (hasCriticalTargets) return true;

    // TODO: foreach on spriteTargets allocates an enumerator on every call â€” this runs per-frame
    // during active gating. Replace with an indexed for-loop (same pattern as criticalSpriteTargets above).
    for (var i = 0; i < spriteTargets.Count; i++) {
      var target = spriteTargets[i];
      if (!IsSpriteTargetEnabled(target)) continue;
      for (var frame = minFrame; frame <= maxFrame; frame++) {
        if (!target.IsFrameReady(frame, out _, targetCategory)) return false;
      }
    }
    return true;
  }

  public bool TryGetAnimationReadinessForDiagnostics(
    string animationName,
    out string resolvedAnimation,
    out string category,
    out int startFrame,
    out int readinessEndFrame,
    out int enabledTargetCount,
    out bool firstFrameReady,
    out bool readinessWindowReady
  ) {
    resolvedAnimation = "";
    category = "";
    startFrame = 0;
    readinessEndFrame = 0;
    enabledTargetCount = CountEnabledSpriteTargets();
    firstFrameReady = false;
    readinessWindowReady = false;

    if (!TryGetAnimationKey(animationName, out resolvedAnimation)) return false;
    if (string.IsNullOrWhiteSpace(resolvedAnimation) ||
        animationData == null ||
        !animationData.TryGetValue(resolvedAnimation, out var anim) ||
        anim == null) {
      return false;
    }

    EnsureRuntimeSwitchSettings();
    category = ResolveAnimationCategory(resolvedAnimation, anim);
    startFrame = Math.Max(anim.start, 1);
    readinessEndFrame = CalculateSwitchReadinessEndFrame(anim.start, anim.end);
    firstFrameReady = AreTargetsReadyForWindow(category, startFrame, startFrame);
    readinessWindowReady = AreTargetsReadyForWindow(category, startFrame, readinessEndFrame);
    return true;
  }

  bool AreAllTargetsReadyForFrame(string targetCategory, int frame) {
    return AreAllTargetsReadyForWindow(targetCategory, frame, frame);
  }

  bool AreAllTargetsReadyForWindow(string targetCategory, int startFrame, int endFrame) {
    var minFrame = Math.Max(startFrame, 1);
    var maxFrame = Math.Max(endFrame, minFrame);
    for (var i = 0; i < spriteTargets.Count; i++) {
      var target = spriteTargets[i];
      if (!IsSpriteTargetEnabled(target)) continue;
      for (var frame = minFrame; frame <= maxFrame; frame++) {
        if (!target.IsFrameReady(frame, out _, targetCategory)) return false;
      }
    }
    return true;
  }

  static int CalculateSwitchReadinessEndFrame(int startFrame, int endFrame) {
    var clampedStart = Math.Max(startFrame, 1);
    var clampedClipEnd = Math.Max(endFrame, clampedStart);
    var readinessFrames = Math.Max(1, Math.Min(runtimeWarmupFrames, MaxSwitchReadinessWindowFrames));
    return Math.Min(clampedClipEnd, clampedStart + readinessFrames - 1);
  }

  static int CalculateTransitionPrimeEndFrame(int startFrame, int endFrame) {
    var clampedStart = Math.Max(startFrame, 1);
    var clampedClipEnd = Math.Max(endFrame, clampedStart);
    var primeFrames = Math.Max(1, Math.Min(runtimeWarmupFrames, TransitionPrimeWindowFrames));
    return Math.Min(clampedClipEnd, clampedStart + primeFrames - 1);
  }

  void PrimeTransitionAndQueuedWindows(string transitionCategory, AnimData transitionAnim, string queuedAnimationName) {
    if (transitionAnim == null) return;
    var transitionPrimeEnd = CalculateTransitionPrimeEndFrame(transitionAnim.start, transitionAnim.end);
    if (!HasSeenAnimationCategory(transitionCategory)) {
      var transitionClipEnd = Math.Max(transitionAnim.end, transitionAnim.start);
      var firstPlayTransitionEnd = transitionAnim.start + FirstPlayTransitionPrimeMaxFrames - 1;
      transitionPrimeEnd = Math.Min(transitionClipEnd, Math.Max(transitionPrimeEnd, firstPlayTransitionEnd));
    }
    PrimeTargetsForAnimation(transitionCategory, transitionAnim.start, transitionPrimeEnd);

    if (string.IsNullOrWhiteSpace(queuedAnimationName)) return;
    if (!TryGetAnimationKey(queuedAnimationName, out var queuedResolved)) return;
    if (!animationData.TryGetValue(queuedResolved, out var queuedAnim) || queuedAnim == null) return;

    var queuedCategory = ResolveAnimationCategory(queuedResolved, queuedAnim);
    var queuedPrimeEnd = CalculateTransitionPrimeEndFrame(queuedAnim.start, queuedAnim.end);
    var shouldPrimeQueuedFullWindow =
      !HasSeenAnimationCategory(queuedCategory) &&
      !IsLocomotionAnimationName(queuedResolved) &&
      !IsTransitionCategory(queuedCategory);
    if (shouldPrimeQueuedFullWindow) {
      PrimeTargetsForAnimation(queuedCategory, queuedAnim.start, queuedAnim.end, primeFullWindow: true);
      return;
    }
    PrimeTargetsForAnimation(queuedCategory, queuedAnim.start, queuedPrimeEnd);
  }
  static bool IsTransitionCategory(string category) {
    return string.Equals(category, "To", StringComparison.Ordinal) ||
           string.Equals(category, "To2", StringComparison.Ordinal);
  }

  // TODO: hardcoded locomotion names. Adding a new locomotion animation requires a code change.
  // Consider marking locomotion in AnimData (e.g. a bool isLocomotive flag) so this list is data-driven.
  static bool IsLocomotionAnimationName(string animationName) {
    if (string.IsNullOrWhiteSpace(animationName)) return false;
    return string.Equals(animationName, "Breathe", StringComparison.Ordinal) ||
           string.Equals(animationName, "Walk", StringComparison.Ordinal) ||
           string.Equals(animationName, "Run", StringComparison.Ordinal) ||
           string.Equals(animationName, "Sprint", StringComparison.Ordinal) ||
           string.Equals(animationName, "Stance", StringComparison.Ordinal);
  }

  void ClearPendingAnimationSwitch() {
    hasPendingAnimationSwitch = false;
    pendingAnimation = null;
    pendingQueuedAnimation = null;
    pendingCategory = null;
    pendingStartFrame = 0;
    pendingReadyEndFrame = 0;
    pendingSwitchStartTime = 0f;
    pendingSwitchDeadline = 0f;
  }

  void ClearStartFrameHold() {
    ResetStartupVisualHoldState();
    holdCurrentAnimationOnStartFrameUntilReady = false;
    holdCategory = null;
    holdFrame = 0;
    ClearVisualFrameHold();
  }

  void ClearVisualFrameHold() {
    pendingVisualFrame = int.MinValue;
    pendingVisualCategory = null;
    pendingVisualFrameStartedAt = 0f;
  }

  int ResolveVisualFrameForApply(int desiredFrame, string targetCategory) {
    if (!Application.isPlaying) {
      ClearVisualFrameHold();
      return desiredFrame;
    }
    if (spriteTargets.Count <= 1 || string.IsNullOrWhiteSpace(targetCategory)) {
      ClearVisualFrameHold();
      return desiredFrame;
    }
    var loadingOverlayWarmGateActive = SpriteStreamingLoadingState.IsLoadingOverlayActive &&
                                       StreamingWarmOrchestrator.IsWarmGateRunning;
    var gameplayMixedAppearanceSync =
      !loadingOverlayWarmGateActive &&
      HasMixedVisibleSpriteTargets() &&
      spriteTargets.Count <= MaxTargetsForGateReadinessChecks;
    var startupPlayerVisualHold =
      holdCurrentAnimationOnStartFrameUntilReady &&
      startupVisualHoldActive &&
      appearancePinClass == TextureResidencyCache.PinClass.Player;
    if (!loadingOverlayWarmGateActive && !gameplayMixedAppearanceSync) {
      ClearVisualFrameHold();
      return desiredFrame;
    }
    if (AreAllTargetsReadyForFrame(targetCategory, desiredFrame)) {
      ClearVisualFrameHold();
      return desiredFrame;
    }
    // Request the blocked frame across visible parts and keep the actor coherent briefly.
    PrimeTargetsForAnimation(targetCategory, desiredFrame, desiredFrame);
    // Keep the first blocked-frame timestamp stable so timeout can elapse even as desiredFrame advances.
    if (pendingVisualFrame == int.MinValue ||
        !string.Equals(pendingVisualCategory, targetCategory, StringComparison.Ordinal)) {
      pendingVisualFrame = desiredFrame;
      pendingVisualCategory = targetCategory;
      pendingVisualFrameStartedAt = Time.realtimeSinceStartup;
    }

    var waitSeconds = Time.realtimeSinceStartup - pendingVisualFrameStartedAt;
    var maxHoldSeconds = startupPlayerVisualHold
      ? MaxStartupPlayerVisualFrameSyncHoldSeconds
      : (loadingOverlayWarmGateActive
        ? MaxVisualFrameSyncHoldSeconds
        : MaxGameplayVisualFrameSyncHoldSeconds);
    if (waitSeconds >= maxHoldSeconds) {
      if (visualSyncTimeoutLogFrame != Time.frameCount) {
        visualSyncTimeoutLogFrame = Time.frameCount;
        if (!SuppressRuntimeWarningLogsForPerfPass) {
          Debug.LogWarning(
            "[AnimationController] Visual frame sync timeout; applying unsynchronized frame. " +
            "animation='" + currentAnimation +
            "' category='" + targetCategory +
            "' hold_frame=" + pendingVisualFrame +
            " frame=" + desiredFrame +
            " wait_ms=" + (waitSeconds * 1000f).ToString("0.0")
          );
        }
      }
      if (startupPlayerVisualHold) {
        CompleteStartupVisualHold("timeout");
      }
      ClearVisualFrameHold();
      return desiredFrame;
    }

    if (lastAppliedSpriteFrame == int.MinValue) {
      return int.MinValue;
    }

    return lastAppliedSpriteFrame;
  }
  void RefreshAppearancePins() {
    if (!Application.isPlaying) return;
    if (string.IsNullOrWhiteSpace(appearanceOwnerId)) return;
    var loadingOverlayWarmGateActive = SpriteStreamingLoadingState.IsLoadingOverlayActive &&
                                       StreamingWarmOrchestrator.IsWarmGateRunning;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var queueBusy = queue.queuedCount > 0 || queue.inFlightCount > 0;
    var keepLoadedForSession = SpriteStreamingRuntimeSettings.KeepLoadedSpritesForSession;
    var pinWindowFrames = Math.Max(SpriteStreamingRuntimeSettings.PinWindowFrames, 1);
    var maxPredicted = Math.Max(SpriteStreamingRuntimeSettings.PinPredictedNextAnimations, 0);
    if (!loadingOverlayWarmGateActive) {
      // Gameplay mode: keep pin churn light to avoid trigger-time spikes.
      pinWindowFrames = Math.Min(pinWindowFrames, 8);
      maxPredicted = 0;
    }
    var pinRefreshBucketSize = Math.Max(SpriteStreamingRuntimeSettings.PinRefreshFrameBucketSize, 1);
    if (loadingOverlayWarmGateActive) {
      pinRefreshBucketSize = Math.Max(pinRefreshBucketSize, 64);
    }
    else if (keepLoadedForSession && !queueBusy && !hasPendingAnimationSwitch) {
      pinRefreshBucketSize = Math.Max(pinRefreshBucketSize, 48);
      pinWindowFrames = Math.Min(pinWindowFrames, 12);
      maxPredicted = Math.Min(maxPredicted, 1);
    }
    else {
      pinRefreshBucketSize = Math.Max(pinRefreshBucketSize, 16);
    }
    var maxPinAddresses = Math.Max(SpriteStreamingRuntimeSettings.MaxPinnedAddressesPerOwner, MinAppearancePinAddressBudget);
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      maxPinAddresses = Math.Max(Math.Min(maxPinAddresses, LoadingOverlayPinAddressCap), MinAppearancePinAddressBudget);
    }
    var currentFrameBucket = Mathf.Max(Time.frameCount + appearancePinRefreshOffset, 0) / pinRefreshBucketSize;

    if (!SpriteStreamingRuntimeSettings.EnableAppearanceSetStreaming ||
        !SpriteStreamingRuntimeSettings.EnablePinnedHotset) {
      if (IsAppearancePinSnapshotCurrent(false, pinWindowFrames, maxPredicted, currentFrameBucket)) return;
      TextureResidencyCache.ReleaseOwnerPins(appearanceOwnerId);
      appearancePinAddressBuffer.Clear();
      appearancePinAddressSet.Clear();
      UpdateAppearancePinSnapshot(false, pinWindowFrames, maxPredicted, currentFrameBucket);
      return;
    }

    if (animationData == null || animationData.Count == 0 || spriteTargets.Count == 0) {
      if (IsAppearancePinSnapshotCurrent(true, pinWindowFrames, maxPredicted, currentFrameBucket)) return;
      TextureResidencyCache.ReleaseOwnerPins(appearanceOwnerId);
      appearancePinAddressBuffer.Clear();
      appearancePinAddressSet.Clear();
      UpdateAppearancePinSnapshot(true, pinWindowFrames, maxPredicted, currentFrameBucket);
      return;
    }

    if (IsAppearancePinSnapshotCurrent(true, pinWindowFrames, maxPredicted, currentFrameBucket)) return;

    appearancePinAddressBuffer.Clear();
    appearancePinAddressSet.Clear();

    if (!string.IsNullOrWhiteSpace(currentAnimation) && animationData.TryGetValue(currentAnimation, out var currentAnim)) {
      var currentCategory = ResolveAnimationCategory(currentAnimation, currentAnim);
      var currentStart = isPlaying ? Mathf.Clamp(currentFrame, currentAnim.start, currentAnim.end) : currentAnim.start;
      CollectWindowAddresses(currentCategory, currentStart, currentAnim.end, pinWindowFrames, maxPinAddresses);
      if (loadingOverlayWarmGateActive) {
        CollectPredictedInterruptWindows(currentAnimation, pinWindowFrames, maxPredicted, maxPinAddresses);
      }
    }

    if (appearancePinAddressBuffer.Count < maxPinAddresses &&
        loadingOverlayWarmGateActive &&
        hasPendingAnimationSwitch &&
        !string.IsNullOrWhiteSpace(pendingAnimation) &&
        animationData.TryGetValue(pendingAnimation, out var pendingAnim)) {
      var pendingCategoryName = string.IsNullOrWhiteSpace(pendingCategory)
        ? ResolveAnimationCategory(pendingAnimation, pendingAnim)
        : pendingCategory;
      var pendingStart = Mathf.Clamp(Math.Max(pendingStartFrame, pendingAnim.start), pendingAnim.start, pendingAnim.end);
      CollectWindowAddresses(pendingCategoryName, pendingStart, pendingAnim.end, pinWindowFrames, maxPinAddresses);
    }

    if (appearancePinAddressBuffer.Count < maxPinAddresses &&
        loadingOverlayWarmGateActive &&
        !string.IsNullOrWhiteSpace(queuedAnimation) &&
        animationData.TryGetValue(queuedAnimation, out var queuedAnim)) {
      var queuedCategory = ResolveAnimationCategory(queuedAnimation, queuedAnim);
      CollectWindowAddresses(queuedCategory, queuedAnim.start, queuedAnim.end, pinWindowFrames, maxPinAddresses);
    }

    if (appearancePinAddressBuffer.Count <= 0) {
      TextureResidencyCache.ReleaseOwnerPins(appearanceOwnerId);
      UpdateAppearancePinSnapshot(true, pinWindowFrames, maxPredicted, currentFrameBucket);
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      appearanceOwnerId,
      appearancePinClass,
      appearancePinAddressBuffer,
      TextureResidencyCache.LoadPriority.Warmup
    );
    UpdateAppearancePinSnapshot(true, pinWindowFrames, maxPredicted, currentFrameBucket);
  }

  void CollectWindowAddresses(string categoryName, int startFrame, int maxClipFrame, int pinWindowFrames, int maxPinAddresses) {
    if (appearancePinAddressBuffer.Count >= maxPinAddresses) return;
    var clampedStart = Math.Max(startFrame, 1);
    var clampedMax = Math.Max(maxClipFrame, clampedStart);
    var clampedEnd = Math.Min(clampedMax, clampedStart + pinWindowFrames - 1);

    // Frame-first + critical-first collection protects core body continuity (Skin*) during switch windows.
    for (var frame = clampedStart; frame <= clampedEnd; frame++) {
      CollectWindowAddressesForTargetSet(criticalSpriteTargets, categoryName, frame, maxPinAddresses);
      if (appearancePinAddressBuffer.Count >= maxPinAddresses) return;
      CollectWindowAddressesForTargetSet(spriteTargets, categoryName, frame, maxPinAddresses, skipCriticalTargets: true);
      if (appearancePinAddressBuffer.Count >= maxPinAddresses) return;
    }
  }

  void CollectWindowAddressesForTargetSet(
    List<SpriteWithNormals> targets,
    string categoryName,
    int frame,
    int maxPinAddresses,
    bool skipCriticalTargets = false
  ) {
    if (targets == null || targets.Count == 0) return;
    for (var i = 0; i < targets.Count; i++) {
      if (appearancePinAddressBuffer.Count >= maxPinAddresses) return;
      var target = targets[i];
      if (target == null) continue;
      if (skipCriticalTargets && IsCriticalSpriteTarget(target)) continue;
      if (!IsSpriteTargetEnabled(target)) continue;
      if (!target.TryGetFrameAddressPair(frame, out var pair, categoryName)) continue;

      AddAppearancePinAddress(pair.StreamingColorAddress, maxPinAddresses);
      AddAppearancePinAddress(pair.StreamingNormalAddress, maxPinAddresses);
    }
  }

  void CollectPredictedInterruptWindows(string sourceAnimation, int pinWindowFrames, int maxPredicted, int maxPinAddresses) {
    if (maxPredicted <= 0) return;
    if (appearancePinAddressBuffer.Count >= maxPinAddresses) return;
    if (interruptData == null || !interruptData.TryGetValue(sourceAnimation, out var nextMap) || nextMap == null || nextMap.Count == 0) return;

    predictedAnimations.Clear();
    foreach (var pair in nextMap) {
      var predictedAnimationName = pair.Value;
      if (string.IsNullOrWhiteSpace(predictedAnimationName)) continue;
      if (!predictedAnimations.Add(predictedAnimationName)) continue;
      if (!animationData.TryGetValue(predictedAnimationName, out var predictedAnim)) continue;

      var predictedCategory = ResolveAnimationCategory(predictedAnimationName, predictedAnim);
      CollectWindowAddresses(predictedCategory, predictedAnim.start, predictedAnim.end, pinWindowFrames, maxPinAddresses);
      if (appearancePinAddressBuffer.Count >= maxPinAddresses) break;
      if (predictedAnimations.Count >= maxPredicted) break;
    }
  }

  void AddAppearancePinAddress(string address, int maxPinAddresses) {
    if (appearancePinAddressBuffer.Count >= maxPinAddresses) return;
    if (string.IsNullOrWhiteSpace(address)) return;
    var normalized = address;
    if (!appearancePinAddressSet.Add(normalized)) return;
    if (appearancePinAddressBuffer.Count >= maxPinAddresses) return;
    appearancePinAddressBuffer.Add(normalized);
  }

  bool IsAppearancePinSnapshotCurrent(bool streamingEnabled, int pinWindowFrames, int maxPredicted, int currentFrameBucket) {
    return pinSnapshotStreamingEnabled == streamingEnabled &&
           pinSnapshotWindowFrames == pinWindowFrames &&
           pinSnapshotPredictedAnimations == maxPredicted &&
           pinSnapshotCurrentFrameBucket == currentFrameBucket &&
           pinSnapshotHasPendingSwitch == hasPendingAnimationSwitch &&
           string.Equals(pinSnapshotCurrentAnimation, currentAnimation, StringComparison.Ordinal) &&
           string.Equals(pinSnapshotPendingAnimation, pendingAnimation, StringComparison.Ordinal) &&
           pinSnapshotPendingStartFrame == pendingStartFrame &&
           pinSnapshotPendingReadyEndFrame == pendingReadyEndFrame &&
           string.Equals(pinSnapshotQueuedAnimation, queuedAnimation, StringComparison.Ordinal);
  }

  void UpdateAppearancePinSnapshot(bool streamingEnabled, int pinWindowFrames, int maxPredicted, int currentFrameBucket) {
    pinSnapshotStreamingEnabled = streamingEnabled;
    pinSnapshotWindowFrames = pinWindowFrames;
    pinSnapshotPredictedAnimations = maxPredicted;
    pinSnapshotCurrentAnimation = currentAnimation;
    pinSnapshotCurrentFrameBucket = currentFrameBucket;
    pinSnapshotHasPendingSwitch = hasPendingAnimationSwitch;
    pinSnapshotPendingAnimation = pendingAnimation;
    pinSnapshotPendingStartFrame = pendingStartFrame;
    pinSnapshotPendingReadyEndFrame = pendingReadyEndFrame;
    pinSnapshotQueuedAnimation = queuedAnimation;
  }

  void InvalidateAppearancePinSnapshot() {
    pinSnapshotStreamingEnabled = false;
    pinSnapshotWindowFrames = int.MinValue;
    pinSnapshotPredictedAnimations = int.MinValue;
    pinSnapshotCurrentAnimation = null;
    pinSnapshotCurrentFrameBucket = int.MinValue;
    pinSnapshotHasPendingSwitch = false;
    pinSnapshotPendingAnimation = null;
    pinSnapshotPendingStartFrame = int.MinValue;
    pinSnapshotPendingReadyEndFrame = int.MinValue;
    pinSnapshotQueuedAnimation = null;
  }

  void PrimeSampledTargetsForAnimation(string targetCategory, int startFrame, int endFrame, int maxSampleTargets) {
    var warmupFrames = Math.Max(runtimeWarmupFrames, 1);
    var clampedStart = Math.Max(startFrame, 1);
    var clampedEnd = Math.Max(endFrame, clampedStart);
    var targetEnd = Math.Min(clampedEnd, clampedStart + warmupFrames - 1);
    var sampleBudget = Math.Max(maxSampleTargets, 1);
    var immediateBudget = PrimeImmediateStartFrameBudget;
    var sampled = PrimeTargetsForAnimationSet(
      criticalSpriteTargets,
      targetCategory,
      clampedStart,
      targetEnd,
      skipCriticalTargets: false,
      maxTargets: sampleBudget,
      ref immediateBudget
    );
    if (sampled >= sampleBudget) return;
    PrimeTargetsForAnimationSet(
      spriteTargets,
      targetCategory,
      clampedStart,
      targetEnd,
      skipCriticalTargets: true,
      maxTargets: sampleBudget - sampled,
      ref immediateBudget
    );
  }

  int PrimeTargetsForAnimationSet(
    List<SpriteWithNormals> targets,
    string targetCategory,
    int startFrame,
    int endFrame,
    bool skipCriticalTargets,
    int maxTargets,
    ref int immediateBudget
  ) {
    if (targets == null || targets.Count <= 0 || maxTargets <= 0) return 0;
    var primed = 0;
    for (var i = 0; i < targets.Count; i++) {
      if (primed >= maxTargets) break;
      var target = targets[i];
      if (target == null) continue;
      if (skipCriticalTargets && IsCriticalSpriteTarget(target)) continue;
      if (!IsSpriteTargetEnabled(target)) continue;

      target.PrimeAnimationWindow(targetCategory, startFrame, endFrame, 0);
      PrimeImmediateStartFrame(target, targetCategory, startFrame, ref immediateBudget);
      primed++;
    }
    return primed;
  }

  void PrimeImmediateStartFrame(SpriteWithNormals target, string targetCategory, int frame, ref int immediateBudget) {
    if (target == null) return;
    var clampedFrame = Math.Max(frame, 1);
    if (!target.TryGetFrameAddressPair(clampedFrame, out var pair, targetCategory)) return;
    var loadingContextActive = SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning;
    var colorPriority = loadingContextActive ? TextureResidencyCache.LoadPriority.Warmup : TextureResidencyCache.LoadPriority.Immediate;

    if (!string.IsNullOrWhiteSpace(pair.StreamingColorAddress)) {
      if (immediateBudget > 0) {
        TextureResidencyCache.RequestLoad(pair.StreamingColorAddress, colorPriority);
        immediateBudget--;
      }
      else if (!loadingContextActive) {
        // If immediate budget is exhausted in gameplay, still enqueue as warmup.
        TextureResidencyCache.RequestLoad(pair.StreamingColorAddress, TextureResidencyCache.LoadPriority.Warmup);
      }
    }
    if (!string.IsNullOrWhiteSpace(pair.StreamingNormalAddress)) {
      if (loadingContextActive) {
        if (immediateBudget > 0) {
          TextureResidencyCache.RequestLoad(pair.StreamingNormalAddress, TextureResidencyCache.LoadPriority.Warmup);
          immediateBudget--;
        }
      }
      else {
        // Keep normal maps warmup-priority to reduce immediate queue pressure.
        TextureResidencyCache.RequestLoad(pair.StreamingNormalAddress, TextureResidencyCache.LoadPriority.Warmup);
      }
    }
  }

  bool AreSampledTargetsReadyForWindow(string targetCategory, int startFrame, int endFrame, int maxSampleTargets) {
    var sampleBudget = Math.Max(maxSampleTargets, 1);
    var sampled = 0;
    sampled += CountReadySampleTargetsForSet(
      criticalSpriteTargets,
      targetCategory,
      startFrame,
      endFrame,
      skipCriticalTargets: false,
      maxTargets: sampleBudget
    );
    if (sampled < 0) return false;
    if (sampled >= sampleBudget) return true;
    var secondaryReady = CountReadySampleTargetsForSet(
      spriteTargets,
      targetCategory,
      startFrame,
      endFrame,
      skipCriticalTargets: true,
      maxTargets: sampleBudget - sampled
    );
    if (secondaryReady < 0) return false;
    return true;
  }

  int CountReadySampleTargetsForSet(
    List<SpriteWithNormals> targets,
    string targetCategory,
    int startFrame,
    int endFrame,
    bool skipCriticalTargets,
    int maxTargets
  ) {
    if (targets == null || targets.Count <= 0 || maxTargets <= 0) return 0;
    var minFrame = Math.Max(startFrame, 1);
    var maxFrame = Math.Max(endFrame, minFrame);
    var sampled = 0;
    for (var i = 0; i < targets.Count; i++) {
      if (sampled >= maxTargets) break;
      var target = targets[i];
      if (target == null) continue;
      if (skipCriticalTargets && IsCriticalSpriteTarget(target)) continue;
      if (!IsSpriteTargetEnabled(target)) continue;
      for (var frame = minFrame; frame <= maxFrame; frame++) {
        if (!target.IsFrameReady(frame, out _, targetCategory)) return -1;
      }
      sampled++;
    }
    return sampled;
  }
}
