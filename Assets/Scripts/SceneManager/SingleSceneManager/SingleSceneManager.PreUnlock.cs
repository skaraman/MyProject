using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class SingleSceneManager {
  float ResolvePreUnlockBlockingDeadline() {
    var budgetSeconds = Mathf.Max(preUnlockBlockingBudgetSeconds, 0f);
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount, 0);
    if (outstanding >= 2000) {
      budgetSeconds = Mathf.Max(budgetSeconds, 6f);
    }
    else if (outstanding >= 1200) {
      budgetSeconds = Mathf.Max(budgetSeconds, 4f);
    }
    else if (outstanding >= 600) {
      budgetSeconds = Mathf.Max(budgetSeconds, 2.5f);
    }
    if (budgetSeconds <= 0f) return float.PositiveInfinity;
    return Time.realtimeSinceStartup + budgetSeconds;
  }

  void LogPreUnlockBlockingBudget(string stage, float deadline, string state) {
    if (!ShouldLogLoadingProgressDebug()) return;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var remainingText = float.IsInfinity(deadline)
      ? "inf"
      : Mathf.Max(deadline - Time.realtimeSinceStartup, 0f).ToString("0.000");
    Debug.Log(
      "[SingleSceneManager][PreUnlockBudget] stage=" + stage +
      " state=" + state +
      " remaining_s=" + remainingText +
      " queue_queued=" + queue.queuedCount +
      " queue_in_flight=" + queue.inFlightCount +
      " queue_outstanding=" + Mathf.Max(queue.queuedCount + queue.inFlightCount, 0) +
      " reveal_critical_ready=" + (IsCriticalScopeReadyForReveal() ? 1 : 0)
    );
  }

  [SerializeField] bool enablePreUnlockRevealCriticalPrefix = true;
  int preUnlockRevealCriticalPrefixCount;

  void ResolvePreUnlockRevealCriticalPrefixCount(List<string> addresses, int playerAddressCount = -1) {
    if (!enablePreUnlockRevealCriticalPrefix || addresses == null || addresses.Count <= 0) return;
    
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var clampedPlayerAddressCount = playerAddressCount >= 0
      ? Mathf.Clamp(playerAddressCount, 0, addresses.Count)
      : Mathf.Clamp(preUnlockLastPlayerAddressCount, 0, addresses.Count);
    
    preUnlockRevealCriticalPrefixCount = ResolveWarmupPriorityPrefixCount(addresses.Count, queue, clampedPlayerAddressCount);
  }

  bool TryGetRemainingPreUnlockBlockingBudget(float deadline, out float remainingSeconds) {
    if (float.IsInfinity(deadline)) {
      remainingSeconds = float.PositiveInfinity;
      return true;
    }

    remainingSeconds = deadline - Time.realtimeSinceStartup;
    
    // Enforce reveal-critical prefix constraint: pre-unlock must not exceed thresholds after deferred processing.
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    if (enablePreUnlockRevealCriticalPrefix) {
      var queuedPlusInFlight = Mathf.Max(queue.queuedCount + queue.inFlightCount, 0);
      if (queuedPlusInFlight > streamingBlockingReadyMaxOutstandingDesktop ||
          queue.inFlightCount > streamingBlockingReadyMaxInFlightDesktop) {
        remainingSeconds = Mathf.Min(remainingSeconds, 0.1f); // Force quick exit if thresholds violated
      }
    }
    
    return true;
  }

  static void DisposeEnumerator(IEnumerator routine) {
    if (routine is IDisposable disposable) {
      disposable.Dispose();
    }
  }

  static void DisposeEnumeratorStack(Stack<IEnumerator> stack) {
    if (stack == null) return;
    while (stack.Count > 0) {
      DisposeEnumerator(stack.Pop());
    }
  }

  IEnumerator RunPreUnlockStepWithBudget(IEnumerator routine, float deadline, string stage) {
    if (routine == null) yield break;

    var stack = preUnlockEnumeratorStack;
    stack.Clear();
    stack.Push(routine);

    try {
      while (true) {
        if (stack.Count <= 0) {
          yield break;
        }

        if (!float.IsInfinity(deadline) && Time.realtimeSinceStartup >= deadline) {
          LogPreUnlockBlockingBudget(stage, deadline, "budget_exhausted");
          yield break;
        }

        var currentRoutine = stack.Peek();
        if (!currentRoutine.MoveNext()) {
          DisposeEnumerator(currentRoutine);
          stack.Pop();
          continue;
        }

        var yielded = currentRoutine.Current;
        if (yielded is IEnumerator nestedRoutine) {
          stack.Push(nestedRoutine);
          continue;
        }

        yield return yielded;
      }
    }
    finally {
      DisposeEnumeratorStack(stack);
      stack.Clear();
    }
  }

  void ResetPreUnlockResidentPins() {
    preUnlockResidentPinAddressScratch.Clear();
    preUnlockResidentPinReadyAddressScratch.Clear();
    preUnlockResidentPinSeenAddressScratch.Clear();
    if (!Application.isPlaying) return;
    TextureResidencyCache.ReleaseOwnerPins(PreUnlockResidentPinOwnerId);
  }

  int ResolvePreUnlockResidentPinAddressCap() {
    var hardCap = Application.isMobilePlatform ? PreUnlockResidentPinHardCapMobile : PreUnlockResidentPinHardCapDesktop;
    var memoryMb = Math.Max(SystemInfo.systemMemorySize, 0);
    if (memoryMb > 0 && memoryMb <= 4096) hardCap = Math.Min(hardCap, 768);
    else if (memoryMb > 0 && memoryMb <= 8192) hardCap = Math.Min(hardCap, 1536);
    return Mathf.Clamp(Math.Min(preUnlockResidentPinMaxAddresses, hardCap), 1, hardCap);
  }

  void AccumulatePreUnlockResidentPins(List<string> addresses) {
    AccumulatePreUnlockResidentPins(addresses, 0, addresses != null ? addresses.Count : 0);
  }

  void AccumulatePreUnlockResidentPins(List<string> addresses, int startInclusive, int count) {
    if (!enablePreUnlockResidentPinning) return;
    if (addresses == null || addresses.Count <= 0 || count <= 0) return;

    var maxAddresses = ResolvePreUnlockResidentPinAddressCap();
    var target = preUnlockResidentPinAddressScratch;
    var seen = preUnlockResidentPinSeenAddressScratch;
    var start = Mathf.Clamp(startInclusive, 0, addresses.Count);
    var endExclusive = Mathf.Clamp(start + Mathf.Max(count, 0), start, addresses.Count);

    for (var i = start; i < endExclusive; i++) {
      if (target.Count >= maxAddresses) break;
      var normalized = string.IsNullOrWhiteSpace(addresses[i]) ? "" : addresses[i].Trim();
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      if (!seen.Add(normalized)) continue;
      target.Add(normalized);
    }
  }

  void CommitPreUnlockResidentPins(string stage) {
    if (!Application.isPlaying) return;
    var trackedAddresses = preUnlockResidentPinAddressScratch;
    if (!enablePreUnlockResidentPinning || trackedAddresses.Count <= 0) {
      TextureResidencyCache.ReleaseOwnerPins(PreUnlockResidentPinOwnerId);
      return;
    }

    var readyAddresses = preUnlockResidentPinReadyAddressScratch;
    readyAddresses.Clear();
    for (var i = 0; i < trackedAddresses.Count; i++) {
      var address = trackedAddresses[i];
      if (string.IsNullOrWhiteSpace(address)) continue;
      if (!TextureResidencyCache.IsReady(address, pump: false)) continue;
      readyAddresses.Add(address);
    }

    if (readyAddresses.Count <= 0) {
      TextureResidencyCache.ReleaseOwnerPins(PreUnlockResidentPinOwnerId);
      if (!ShouldLogLoadingProgressDebug()) return;
      Debug.Log(
        "[SingleSceneManager][PreUnlockPin] stage=" + stage +
        " tracked_addresses=" + trackedAddresses.Count +
        " pinned_ready_addresses=0 queue_only_skip=1"
      );
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      PreUnlockResidentPinOwnerId,
      TextureResidencyCache.PinClass.WarmGate,
      readyAddresses,
      TextureResidencyCache.LoadPriority.Warmup
    );

    if (!ShouldLogLoadingProgressDebug()) return;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    Debug.Log(
      "[SingleSceneManager][PreUnlockPin] stage=" + stage +
      " tracked_addresses=" + trackedAddresses.Count +
      " pinned_ready_addresses=" + readyAddresses.Count +
      " queue_queued=" + queue.queuedCount +
      " queue_in_flight=" + queue.inFlightCount +
      " resident_mb=" + (TextureResidencyCache.EstimatedResidentBytes / (1024f * 1024f)).ToString("0.0")
    );
  }

  void ReleasePreUnlockResidentPins(string reason) {
    var hadTrackedAddresses = preUnlockResidentPinAddressScratch.Count > 0;
    preUnlockResidentPinAddressScratch.Clear();
    preUnlockResidentPinReadyAddressScratch.Clear();
    preUnlockResidentPinSeenAddressScratch.Clear();
    if (!Application.isPlaying) return;
    TextureResidencyCache.ReleaseOwnerPins(PreUnlockResidentPinOwnerId);
    if (!hadTrackedAddresses || !ShouldLogLoadingProgressDebug()) return;
    Debug.Log("[SingleSceneManager][PreUnlockPin] release reason=" + (string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim()));
  }

  void StopDeferredPostRevealWarmup(string reason) {
    var hadQueuedAddresses = deferredPostRevealWarmupAddressScratch.Count > 0;
    deferredPostRevealWarmupAddressScratch.Clear();
    deferredPostRevealWarmupSeenAddressScratch.Clear();
    if (deferredPostRevealWarmupRoutine == null) {
      if (!hadQueuedAddresses || !ShouldLogLoadingProgressDebug()) return;
      Debug.Log("[SingleSceneManager][DeferredWarmup] stage=clear reason=" + (string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim()));
      return;
    }

    StopCoroutine(deferredPostRevealWarmupRoutine);
    deferredPostRevealWarmupRoutine = null;
    if (!ShouldLogLoadingProgressDebug()) return;
    Debug.Log("[SingleSceneManager][DeferredWarmup] stage=stop reason=" + (string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim()));
  }

  void QueueDeferredPostRevealWarmupAddresses(List<string> addresses, int startInclusive, int count, string stage) {
    if (!Application.isPlaying || addresses == null || addresses.Count <= 0 || count <= 0) return;
    var start = Mathf.Clamp(startInclusive, 0, addresses.Count);
    var endExclusive = Mathf.Clamp(start + Mathf.Max(count, 0), start, addresses.Count);
    var added = 0;
    for (var i = start; i < endExclusive; i++) {
      var normalized = string.IsNullOrWhiteSpace(addresses[i]) ? "" : addresses[i].Trim();
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      if (!deferredPostRevealWarmupSeenAddressScratch.Add(normalized)) continue;
      deferredPostRevealWarmupAddressScratch.Add(normalized);
      added++;
    }
    if (added <= 0 || !ShouldLogLoadingProgressDebug()) return;
    Debug.Log(
      "[SingleSceneManager][DeferredWarmup] stage=queue" +
      " source=" + (string.IsNullOrWhiteSpace(stage) ? "-" : stage.Trim()) +
      " added=" + added +
      " pending=" + deferredPostRevealWarmupAddressScratch.Count
    );
  }

  void StartDeferredPostRevealWarmupIfNeeded(string reason) {
    if (!Application.isPlaying) return;
    if (deferredPostRevealWarmupRoutine != null) return;
    if (deferredPostRevealWarmupAddressScratch.Count <= 0) return;
    deferredPostRevealWarmupRoutine = StartCoroutine(RunDeferredPostRevealWarmupRoutine(reason));
  }

  IEnumerator RunDeferredPostRevealWarmupRoutine(string reason) {
    while (Application.isPlaying && SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      yield return null;
    }
    if (!Application.isPlaying) {
      deferredPostRevealWarmupRoutine = null;
      yield break;
    }

    var processedAddressCount = deferredPostRevealWarmupAddressScratch.Count;

    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][DeferredWarmup] stage=begin" +
        " reason=" + (string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim()) +
        " pending=" + processedAddressCount
      );
    }

    if (processedAddressCount > 0) {
      yield return PreloadAnimationAddressBatch(
        deferredPostRevealWarmupAddressScratch,
        0,
        processedAddressCount,
        resetLoadingProgress: false,
        trackResidentPins: false,
        settleAfterEnqueue: false
      );
    }

    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][DeferredWarmup] stage=complete" +
        " processed=" + processedAddressCount
      );
    }

    deferredPostRevealWarmupAddressScratch.Clear();
    deferredPostRevealWarmupSeenAddressScratch.Clear();
    deferredPostRevealWarmupRoutine = null;
  }

  int ResolvePreUnlockBlockingPrefixCount(List<string> addresses, int playerAddressCount = -1) {
    if (addresses == null || addresses.Count <= 0) return 0;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var clampedPlayerAddressCount = playerAddressCount >= 0
      ? Mathf.Clamp(playerAddressCount, 0, addresses.Count)
      : Mathf.Clamp(preUnlockLastPlayerAddressCount, 0, addresses.Count);
    return Mathf.Clamp(
      ResolveWarmupPriorityPrefixCount(addresses.Count, queue, clampedPlayerAddressCount),
      0,
      addresses.Count
    );
  }

  IEnumerator WaitForStreamingIdleBeforeUnlock(
    bool prefetchVisibleSprites = false,
    bool warmAnimationsBeforeUnlock = false
  ) {
    if (!Application.isPlaying) yield break;
    var preUnlockStartedAt = Time.realtimeSinceStartup;
    ResetPreUnlockResidentPins();
    if (ShouldLogLoadFlowWarnings()) {
      Debug.Log("[SingleSceneManager][PreUnlock] Starting WaitForStreamingIdleBeforeUnlock...");
      Debug.Log(TextureResidencyCache.GetQueueSourceBreakdown());
      TextureResidencyCache.LogRequestDiagnosticsSummary();
    }
    if (!waitForStreamingIdleBeforeFadeOut) {
      var preUnlockBlockingDeadlineWithoutIdleWait = ResolvePreUnlockBlockingDeadline();
      if (warmAnimationsBeforeUnlock) {
        SetLoadingStatusOverride("Warming animations");
        if (TryGetRemainingPreUnlockBlockingBudget(preUnlockBlockingDeadlineWithoutIdleWait, out _)) {
          yield return RunPreUnlockStepWithBudget(
            RunPreUnlockAnimationWarmupSequence(prefetchVisibleSprites),
            preUnlockBlockingDeadlineWithoutIdleWait,
            "animation_warmup_no_idle_wait"
          );
        }
        else {
          LogPreUnlockBlockingBudget("animation_warmup_no_idle_wait", preUnlockBlockingDeadlineWithoutIdleWait, "skipped_no_budget");
        }
      }
      SetLoadingStatusOverride("Finalizing reveal");
      CommitPreUnlockResidentPins("no_idle_wait");
      var noIdleQueue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      var noIdleDeferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
      LogGameplayLoadTiming(
        "pre_unlock",
        "no_idle_wait",
        preUnlockStartedAt,
        "queued=" + noIdleQueue.queuedCount +
        " in_flight=" + noIdleQueue.inFlightCount +
        " deferred=" + noIdleDeferredPending +
        " warmup_done=" + (warmAnimationsBeforeUnlock ? 1 : 0) +
        BuildPreUnlockThresholdFields()
      );
      if (ShouldLogLoadFlowWarnings()) {
        Debug.Log("[SingleSceneManager][PreUnlock] Exiting WaitForStreamingIdleBeforeUnlock (no_idle_wait)...");
        Debug.Log(TextureResidencyCache.GetQueueSourceBreakdown());
        TextureResidencyCache.LogRequestDiagnosticsSummary();
      }
      yield break;
    }
    if (LoadingScreen != null && !LoadingScreen.activeSelf) {
      SetLoadingRootActive(true);
    }
    SetLoadingBlackscreenHold(true);
    EnsureLoadingProgressForPhase();
    var preUnlockBlockingDeadline = ResolvePreUnlockBlockingDeadline();
    LogPreUnlockConfig("begin", prefetchVisibleSprites, warmAnimationsBeforeUnlock, preUnlockBlockingDeadline);
    if (prefetchVisibleSprites) {
      if (TryGetRemainingPreUnlockBlockingBudget(preUnlockBlockingDeadline, out _)) {
        yield return RunPreUnlockStepWithBudget(
          PreloadVisibleSpriteWindowsUnderBlack(),
          preUnlockBlockingDeadline,
          "visible_prefetch"
        );
      }
      else {
        LogPreUnlockBlockingBudget("visible_prefetch", preUnlockBlockingDeadline, "skipped_no_budget");
      }
    }
    var stableFramesRequired = Mathf.Max(streamingIdleStableFrames, 1);
    // Legacy queue-idle fallback is retained for non-warm-gate transitions where
    // no blocking snapshot exists.
    var allowedQueued = Mathf.Max(streamingIdleAllowedQueued, 0);
    var allowedInFlight = Mathf.Max(streamingIdleAllowedInFlight, 0);
    var minimumWaitSeconds = Mathf.Max(streamingIdleMinimumWaitSeconds, 0f);
    var timeoutSeconds = Mathf.Max(streamingIdleTimeoutSeconds, 0f);
    var startedAt = Time.realtimeSinceStartup;
    var stableFrames = 0;
    var nextWaitStateLogAt = -1f;

    var warmupDone = false;

    while (true) {
      TextureResidencyCache.Pump();
      var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
      var queueIdle = queue.queuedCount <= allowedQueued && queue.inFlightCount <= allowedInFlight;
      var elapsed = Time.realtimeSinceStartup - startedAt;
      var minimumWaitReached = elapsed >= minimumWaitSeconds;
      var playerReady = IsPlayerFirstFrameReady();
      var hasBlockingProgress = TryGetBlockingProgressState(
        out var blockingProgress,
        out var blockingReadyCount,
        out var blockingTotalCount,
        out var blockingCriticalReady,
        out var blockingHardBypassUsed
      );
      var locationActivationPending = LocationManager.HasPendingBlockingActivationWork;
      var locationDeferredPending = LocationManager.HasPendingDeferredActivationWork;
      var blockingReady = hasBlockingProgress
        ? IsBlockingScopeReady(resolverIdle, playerReady, blockingCriticalReady, blockingHardBypassUsed, queue) &&
          !locationActivationPending
        : (queueIdle && resolverIdle && playerReady && !locationActivationPending);

      if (minimumWaitReached && blockingReady) {
        stableFrames++;
      }
      else {
        stableFrames = 0;
      }
      MaybeLogStreamingIdleWaitState(
        ref nextWaitStateLogAt,
        elapsed,
        minimumWaitSeconds,
        timeoutSeconds,
        stableFrames,
        stableFramesRequired,
        queue,
        resolverIdle,
        playerReady,
        queueIdle,
        hasBlockingProgress,
        blockingReadyCount,
        blockingTotalCount,
        blockingProgress,
        blockingCriticalReady,
        blockingHardBypassUsed,
        blockingReady,
        locationActivationPending,
        locationDeferredPending,
        warmupDone
      );

      if (stableFrames >= stableFramesRequired) {
        if (warmAnimationsBeforeUnlock && !warmupDone) {
          warmupDone = true;
          SetLoadingStatusOverride("Warming animations");
          if (TryGetRemainingPreUnlockBlockingBudget(preUnlockBlockingDeadline, out _)) {
            yield return RunPreUnlockStepWithBudget(
              RunPreUnlockAnimationWarmupSequence(prefetchVisibleSprites),
              preUnlockBlockingDeadline,
              "animation_warmup"
            );
          }
          else {
            LogPreUnlockBlockingBudget("animation_warmup", preUnlockBlockingDeadline, "skipped_no_budget");
          }
          ClearLoadingStatusOverride();
          stableFrames = 0;
          startedAt = Time.realtimeSinceStartup;
          continue;
        }
        SetLoadingStatusOverride("Finalizing reveal");
        CommitPreUnlockResidentPins("stable_ready");
        var stableDeferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
        LogGameplayLoadTiming(
          "pre_unlock",
          "stable_ready",
          preUnlockStartedAt,
          "queued=" + queue.queuedCount +
          " in_flight=" + queue.inFlightCount +
          " deferred=" + stableDeferredPending +
          " resolver_idle=" + (resolverIdle ? 1 : 0) +
          " player_ready=" + (playerReady ? 1 : 0) +
          " blocking_ready=" + (blockingReady ? 1 : 0) +
          " stable_frames=" + stableFrames +
          " warmup_done=" + (warmupDone ? 1 : 0) +
          BuildPreUnlockThresholdFields()
        );
        if (ShouldLogLoadFlowWarnings()) {
          Debug.Log("[SingleSceneManager][PreUnlock] Exiting WaitForStreamingIdleBeforeUnlock (stable_ready)...");
          Debug.Log(TextureResidencyCache.GetQueueSourceBreakdown());
          TextureResidencyCache.LogRequestDiagnosticsSummary();
        }
        yield break;
      }

      if (timeoutSeconds > 0f && elapsed >= timeoutSeconds) {
        var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
        var queueFullyDrained = queue.queuedCount <= 0 && queue.inFlightCount <= 0 && deferredPending <= 0;
        var forcedByBlockingReady = !allowStreamingIdleTimeoutBypass && hasBlockingProgress && blockingReady;
        var forcedByLegacyDrain =
          !allowStreamingIdleTimeoutBypass &&
          !hasBlockingProgress &&
          queueFullyDrained &&
          !locationActivationPending;
        if ((allowStreamingIdleTimeoutBypass || forcedByBlockingReady || forcedByLegacyDrain) && IsCriticalScopeReadyForReveal()) {
          if (warmAnimationsBeforeUnlock && !warmupDone) {
            warmupDone = true;
            SetLoadingStatusOverride("Warming animations");
            if (TryGetRemainingPreUnlockBlockingBudget(preUnlockBlockingDeadline, out _)) {
              yield return RunPreUnlockStepWithBudget(
                RunPreUnlockAnimationWarmupSequence(prefetchVisibleSprites),
                preUnlockBlockingDeadline,
                "animation_warmup_after_timeout"
              );
            }
            else {
              LogPreUnlockBlockingBudget("animation_warmup_after_timeout", preUnlockBlockingDeadline, "skipped_no_budget");
            }
            ClearLoadingStatusOverride();
            stableFrames = 0;
            startedAt = Time.realtimeSinceStartup;
            continue;
          }
          SetLoadingStatusOverride("Finalizing reveal");
          CommitPreUnlockResidentPins("timeout_release");
          LogGameplayLoadTiming(
            "pre_unlock",
            "timeout_release",
            preUnlockStartedAt,
            "queued=" + queue.queuedCount +
            " in_flight=" + queue.inFlightCount +
            " deferred=" + deferredPending +
            " resolver_idle=" + (resolverIdle ? 1 : 0) +
            " player_ready=" + (playerReady ? 1 : 0) +
            " blocking_ready=" + (blockingReady ? 1 : 0) +
            " stable_frames=" + stableFrames +
            " warmup_done=" + (warmupDone ? 1 : 0) +
            BuildPreUnlockThresholdFields()
          );
          if (ShouldLogLoadFlowWarnings()) {
            Debug.Log("[SingleSceneManager][PreUnlock] Exiting WaitForStreamingIdleBeforeUnlock (timeout_release)...");
            Debug.Log(TextureResidencyCache.GetQueueSourceBreakdown());
            TextureResidencyCache.LogRequestDiagnosticsSummary();
          }
          yield break;
        }
      }

      yield return null;
    }
  }

  bool IsPlayerFirstFrameReady() {
    return !TryGetPlayerFirstFrameBlocker(out _);
  }

  bool IsPlayerHierarchyReady() {
    return ResolvePlayerGearController() != null;
  }

  bool IsCriticalEnemiesReady() {
    var activeEnemies = ResolveActiveEnemyControllers();
    if (activeEnemies == null || activeEnemies.Length <= 0) return true;
    for (var i = 0; i < activeEnemies.Length; i++) {
      var enemy = activeEnemies[i];
      if (enemy == null) continue;
      if (!enemy.gameObject.activeInHierarchy) continue;
      if (TryDescribeFirstUnreadySprite(enemy.spriteObjects, "enemy", out _, false)) {
        return false;
      }
    }
    return true;
  }

  bool IsCriticalScopeReadyForReveal() {
    return IsPlayerFirstFrameReady() &&
           !LocationManager.HasPendingBlockingActivationWork &&
           IsCriticalEnemiesReady() &&
           IsGameplayUiReadyForLoadingProgress() &&
           IsGameplayDialogReadyForLoadingProgress();
  }

  public static bool IsCriticalScopeReadyForRevealStatic() {
    return instance != null && instance.IsCriticalScopeReadyForReveal();
  }


  bool IsRevealActivationSettled(
    TextureResidencyCache.QueueSnapshot queue,
    int deferredPending,
    bool resolverIdle,
    bool playerReady,
    bool uiReady,
    bool dialogReady,
    bool locationActivationPending
  ) {
    if (!resolverIdle || !playerReady || !uiReady || !dialogReady || locationActivationPending || !IsCriticalEnemiesReady()) {
      return false;
    }
    return IsQueueWithinBlockingReadyThresholds(queue, deferredPending);
  }

  string ResolveRevealSettleStatusDetail(
    TextureResidencyCache.QueueSnapshot queue,
    int deferredPending,
    bool resolverIdle,
    bool playerReady,
    bool uiReady,
    bool dialogReady,
    bool locationActivationPending
  ) {
    if (locationActivationPending) {
      return "Activating gameplay";
    }
    if (!playerReady) {
      return "Preparing player";
    }
    if (!IsCriticalEnemiesReady()) {
      return "Preparing enemies";
    }
    if (!uiReady) {
      return "Preparing UI";
    }
    if (!dialogReady) {
      return "Preparing dialog";
    }
    if (!resolverIdle) {
      return "Resolving assets";
    }
    if (ShouldKeepFinalizingRevealStatus(queue, deferredPending)) {
      return "Finalizing reveal";
    }
    if (queue.queuedCount > 0 || queue.inFlightCount > 0 || deferredPending > 0) {
      return "Draining queue";
    }
    return "Finalizing reveal";
  }

  static bool ShouldKeepFinalizingRevealStatus(TextureResidencyCache.QueueSnapshot queue, int deferredPending) {
    return queue.queuedCount <= 0 &&
           deferredPending <= 0 &&
           queue.inFlightCount <= 1;
  }

  void MaybeLogRevealSettleState(
    ref string lastState,
    string state,
    float startedAt,
    TextureResidencyCache.QueueSnapshot queue,
    int deferredPending,
    bool resolverIdle,
    bool playerReady,
    bool uiReady,
    bool dialogReady,
    bool locationActivationPending,
    bool locationDeferredPending
  ) {
    if (!ShouldLogLoadFlowWarnings()) return;
    if (string.Equals(lastState, state, StringComparison.Ordinal)) return;

    lastState = state;
    var blockerSummary = playerReady || !TryGetPlayerFirstFrameBlocker(out var blocker, generateSummary: true) ? "-" : blocker;
    Debug.Log(
      "[SingleSceneManager][RevealSettle] state='" + state +
      "' elapsed_s=" + (Time.realtimeSinceStartup - startedAt).ToString("0.000") +
      " queued=" + queue.queuedCount +
      " in_flight=" + queue.inFlightCount +
      " deferred=" + deferredPending +
      " resolver_idle=" + (resolverIdle ? 1 : 0) +
      " player_ready=" + (playerReady ? 1 : 0) +
      " ui_ready=" + (uiReady ? 1 : 0) +
      " dialog_ready=" + (dialogReady ? 1 : 0) +
      " player_blocker='" + blockerSummary +
      "' location_activation_pending=" + (locationActivationPending ? 1 : 0) +
      " location_deferred_pending=" + (locationDeferredPending ? 1 : 0) +
      " current_section=" + ResolveCurrentSection() +
      " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation)
    );
  }

  void LogRevealHandoff(string stage, float handoffStartedAt, float stepStartedAt = -1f) {
    if (!ShouldLogLoadFlowWarnings()) return;
    var now = Time.realtimeSinceStartup;
    var builder = BeginLoadFlowLog("[SingleSceneManager][RevealHandoff]");
    AppendLoadFlowField(builder, "stage", ResolveLoadFlowValue(stage, "unspecified"));
    if (handoffStartedAt >= 0f) {
      AppendLoadFlowFloat(builder, "elapsed_s", Mathf.Max(now - handoffStartedAt, 0f));
    }
    if (stepStartedAt >= 0f) {
      AppendLoadFlowFloat(builder, "step_ms", Mathf.Max(now - stepStartedAt, 0f) * 1000f, "0.0");
    }
    AppendLoadFlowField(builder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
    AppendLoadFlowField(builder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(builder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    AppendLoadFlowInt(builder, "loading_percent", loadingPercent);
    AppendLoadFlowField(builder, "loading_status", ResolveLoadFlowValue(loadingStatusDetail));
    AppendLoadFlowField(builder, "active_input_map", ResolveLoadFlowValue(activeInputMap));
    AppendGameplayLoadPipelineFields(builder);
    Debug.Log(builder.ToString());
  }

  IEnumerator WaitForRevealActivationSettle() {
    var stableFramesRequired = Mathf.Max(RevealOpaqueSettleFrames, 1);
    var stableSecondsRequired = Mathf.Max(RevealOpaqueSettleMinimumStableSeconds, 0f);
    var timeoutSeconds = Mathf.Max(RevealOpaqueSettleTimeoutSeconds, 0f);
    var startedAt = Time.realtimeSinceStartup;
    var stableFrames = 0;
    var stableStartedAt = -1f;
    string lastLoggedState = null;
    if (ShouldLogLoadFlowWarnings()) {
      Debug.Log("[SingleSceneManager][RevealSettle] Starting WaitForRevealActivationSettle...");
      Debug.Log(TextureResidencyCache.GetQueueSourceBreakdown());
    }

    while (true) {
      TextureResidencyCache.Pump();
      var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
      var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
      var playerReady = IsPlayerFirstFrameReady();
      var locationActivationPending = LocationManager.HasPendingBlockingActivationWork;
      var locationDeferredPending = LocationManager.HasPendingDeferredActivationWork;
      var uiReady = IsGameplayUiReadyForLoadingProgress();
      var dialogReady = IsGameplayDialogReadyForLoadingProgress();
      var statusDetail = ResolveRevealSettleStatusDetail(
        queue,
        deferredPending,
        resolverIdle,
        playerReady,
        uiReady,
        dialogReady,
        locationActivationPending
      );

      SetLoadingStatusOverride(statusDetail);
      MaybeLogRevealSettleState(
        ref lastLoggedState,
        statusDetail,
        startedAt,
        queue,
        deferredPending,
        resolverIdle,
        playerReady,
        uiReady,
        dialogReady,
        locationActivationPending,
        locationDeferredPending
      );

      if (IsRevealActivationSettled(
            queue,
            deferredPending,
            resolverIdle,
            playerReady,
            uiReady,
            dialogReady,
            locationActivationPending
          )) {
        if (stableFrames <= 0) {
          stableStartedAt = Time.realtimeSinceStartup;
        }
        stableFrames++;
        var stableElapsed = stableStartedAt >= 0f
          ? Time.realtimeSinceStartup - stableStartedAt
          : 0f;
        if (stableFrames >= stableFramesRequired && stableElapsed >= stableSecondsRequired) {
          LogGameplayLoadTiming(
            "reveal_settle",
            "exit",
            startedAt,
            "status=" + ResolveLoadFlowValue(statusDetail) +
            " queued=" + queue.queuedCount +
            " in_flight=" + queue.inFlightCount +
            " deferred=" + deferredPending +
            " resolver_idle=" + (resolverIdle ? 1 : 0) +
            " player_ready=" + (playerReady ? 1 : 0) +
            " ui_ready=" + (uiReady ? 1 : 0) +
            " dialog_ready=" + (dialogReady ? 1 : 0) +
            " location_activation_pending=" + (locationActivationPending ? 1 : 0) +
            " location_deferred_pending=" + (locationDeferredPending ? 1 : 0)
          );
          if (ShouldLogLoadFlowWarnings()) {
            Debug.Log(
              "[SingleSceneManager][RevealSettle] exit" +
              " elapsed_s=" + (Time.realtimeSinceStartup - startedAt).ToString("0.000") +
              " stable_s=" + stableElapsed.ToString("0.000") +
              " stable_frames=" + stableFrames
            );
            Debug.Log(TextureResidencyCache.GetQueueSourceBreakdown());
          }
          yield break;
        }
      }
      else {
        stableFrames = 0;
        stableStartedAt = -1f;
      }

      var elapsed = Time.realtimeSinceStartup - startedAt;
      if (timeoutSeconds > 0f && elapsed >= timeoutSeconds && IsCriticalScopeReadyForReveal()) {
        LogGameplayLoadTiming(
          "reveal_settle",
          "timeout",
          startedAt,
          "status=" + ResolveLoadFlowValue(statusDetail) +
          " queued=" + queue.queuedCount +
          " in_flight=" + queue.inFlightCount +
          " deferred=" + deferredPending +
          " resolver_idle=" + (resolverIdle ? 1 : 0) +
          " player_ready=" + (playerReady ? 1 : 0) +
          " ui_ready=" + (uiReady ? 1 : 0) +
          " dialog_ready=" + (dialogReady ? 1 : 0) +
          " location_activation_pending=" + (locationActivationPending ? 1 : 0) +
          " location_deferred_pending=" + (locationDeferredPending ? 1 : 0)
        );
        if (ShouldLogLoadFlowWarnings()) {
          Debug.LogWarning(
            "[SingleSceneManager][RevealSettle] timeout" +
            " elapsed_s=" + elapsed.ToString("0.000") +
            " queued=" + queue.queuedCount +
            " in_flight=" + queue.inFlightCount +
            " deferred=" + deferredPending +
            " resolver_idle=" + (resolverIdle ? 1 : 0) +
            " player_ready=" + (playerReady ? 1 : 0) +
            " ui_ready=" + (uiReady ? 1 : 0) +
            " dialog_ready=" + (dialogReady ? 1 : 0) +
            " location_activation_pending=" + (locationActivationPending ? 1 : 0) +
            " location_deferred_pending=" + (locationDeferredPending ? 1 : 0) +
            " current_section=" + ResolveCurrentSection() +
            " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation)
          );
          Debug.Log(TextureResidencyCache.GetQueueSourceBreakdown());
        }
        yield break;
      }

      yield return null;
    }
  }

  private void CaptureBlockingProgressStateFromWarmResult(WarmResult result) {
    loadingBlockingTotalCount = Mathf.Max(result.criticalTotalCount, 0);
    loadingBlockingReadyCount = Mathf.Clamp(result.criticalReadyCount, 0, loadingBlockingTotalCount);
    loadingBlockingCriticalReady = result.playerCriticalReady;
    loadingBlockingHardBypassUsed = result.hardTimeoutBypassUsed;
    loadingBlockingStateKnown = loadingBlockingTotalCount > 0 || loadingBlockingCriticalReady || loadingBlockingHardBypassUsed;
  }

  private bool TryGetBlockingProgressState(
    out float progress,
    out int readyCount,
    out int totalCount,
    out bool criticalReady,
    out bool hardBypassUsed
  ) {
    if (StreamingWarmOrchestrator.TryGetActiveProgress(out var snapshot)) {
      loadingBlockingTotalCount = Mathf.Max(snapshot.criticalTotalCount, 0);
      loadingBlockingReadyCount = Mathf.Clamp(snapshot.criticalReadyCount, 0, loadingBlockingTotalCount);
      loadingBlockingCriticalReady = snapshot.criticalReady;
      loadingBlockingHardBypassUsed = false;
      loadingBlockingStateKnown = true;
    }

    if (!loadingBlockingStateKnown) {
      progress = 0f;
      readyCount = 0;
      totalCount = 0;
      criticalReady = false;
      hardBypassUsed = false;
      return false;
    }

    readyCount = loadingBlockingReadyCount;
    totalCount = loadingBlockingTotalCount;
    criticalReady = loadingBlockingCriticalReady;
    hardBypassUsed = loadingBlockingHardBypassUsed;
    progress = totalCount > 0 ? (float)readyCount / totalCount : 0f;
    return true;
  }
}
