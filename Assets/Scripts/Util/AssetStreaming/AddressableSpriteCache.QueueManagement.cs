#if false
using System.Collections.Generic;
using UnityEngine;

namespace AddressableSpriteCacheAssetStreaming {

public static partial class TextureResidencyCache {

  public static void Pump() {
    frameAccessTicks = DateTime.UtcNow.Ticks;
    ResetFrameCounterIfNeeded();
    FlushDeferredRequestsIntoMainQueues();
    ProcessPendingCompletionFollowups();
    var diagnosticsEnabled = ShouldLogRequestFrameDiagnostics();
    var pumpStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
    var cfg = GetSettings();
    var maxStarts = ResolveMaxStartsPerFrame(cfg);
    var maxInFlightLoads = ResolveMaxInFlightLoads();
    MaybeLogLoadingContextMode(maxStarts, maxInFlightLoads);
    if (ShouldSkipPumpForCurrentFrame(maxStarts)) {
      SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
      RecordPinStateIfEnabled();
      return;
    }
    var startedBefore = startedLoadsThisFrame;
    var overlayStartAllowance = ResolveOverlayStartAllowance(maxStarts);
    while (startedLoadsThisFrame < maxStarts && inFlightLoads < maxInFlightLoads) {
      if (overlayStartAllowance == 0) break;
      if (!TryDequeueNext(out var entry, out var sourcePriority)) break;
      if (entry == null || entry.isEvicted || !entry.isQueued || entry.queuedPriority != sourcePriority) continue;
      if (entry.isDone || entry.loadStarted) { ClearQueuedFlag(entry); continue; }
      ClearQueuedFlag(entry);
      StartLoad(entry);
      startedLoadsThisFrame++;
      if (overlayStartAllowance != int.MaxValue) {
        overlayStartAllowance = Math.Max(overlayStartAllowance - 1, 0);
        ConsumeOverlayStartToken();
      }
    }
    var immediateBurstRemaining = ResolveImmediateBurstBudget();
    while (immediateQueue.Count > 0 && immediateBurstRemaining > 0 && inFlightLoads < maxInFlightLoads) {
      var immediateEntry = immediateQueue.Dequeue();
      if (immediateEntry == null || immediateEntry.isEvicted || !immediateEntry.isQueued || immediateEntry.queuedPriority != LoadPriority.Immediate) continue;
      if (immediateEntry.isDone || immediateEntry.loadStarted) { ClearQueuedFlag(immediateEntry); continue; }
      ClearQueuedFlag(immediateEntry);
      StartLoad(immediateEntry);
      startedLoadsThisFrame++;
      if (immediateBurstRemaining != int.MaxValue) immediateBurstRemaining--;
    }
    CachePumpFrameSnapshot();
    if (diagnosticsEnabled) {
      var pumpMs = ComputeElapsedMs(pumpStartedAt);
      var startedThisPump = Math.Max(startedLoadsThisFrame - startedBefore, 0);
      RecordPumpForFrame(pumpMs, startedThisPump);
    }
    ProcessPendingCompletionFollowups();
    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
    RecordPinStateIfEnabled();
  }

  static int ResolveMaxStartsPerFrame(CacheSettings cfg) {
    if (ShouldUseStrictSerialLoadingDebounce()) return StrictSerialLoadingBudgetPerFrame;
    var baseStarts = Math.Max(cfg.maxAddressableStartsPerFrame, 1);
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      var gameplayCap = Application.isMobilePlatform ? MobileGameplayMaxStartsPerFrameCap : DesktopGameplayMaxStartsPerFrameCap;
      return Mathf.Clamp(baseStarts, 1, gameplayCap);
    }
    var overlayStarts = Math.Max(cfg.loadingOverlayMaxAddressableStartsPerFrame, baseStarts);
    if (deferredRequests.Count > 0) overlayStarts = Math.Min(overlayStarts, Application.isMobilePlatform ? 2 : 3);
    if (!IsCompletionPressureActive()) return overlayStarts;
    var throttledStarts = Mathf.CeilToInt(overlayStarts * CompletionPressureOverlayScale);
    return Mathf.Clamp(throttledStarts, Math.Min(baseStarts, overlayStarts), overlayStarts);
  }

  static int ResolveImmediateBurstBudget() {
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) return 0;
    return Application.isMobilePlatform ? MobileGameplayImmediateBurstCap : DesktopGameplayImmediateBurstCap;
  }

  static int ResolveMaxInFlightLoads() {
    if (ShouldUseStrictSerialLoadingDebounce()) return StrictSerialLoadingBudgetPerFrame;
    var cap = Application.isMobilePlatform ? MobileInFlightLoadCap : DesktopInFlightLoadCap;
    var memoryMb = Math.Max(SystemInfo.systemMemorySize, 0);
    if (memoryMb > 0 && memoryMb <= 4096) cap = Math.Min(cap, 64);
    else if (memoryMb > 0 && memoryMb <= 8192) cap = Math.Min(cap, 96);
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      cap = Math.Min(cap, Application.isMobilePlatform ? MobileOverlayInFlightLoadCap : DesktopOverlayInFlightLoadCap);
    }
    else {
      cap = Math.Min(cap, Application.isMobilePlatform ? MobileGameplayInFlightLoadCap : DesktopGameplayInFlightLoadCap);
    }
    if (IsCompletionPressureActive()) {
      var throttled = Mathf.CeilToInt(cap * 0.75f);
      var minThrottledCap = IsLoadingScreenStreamingContextActive()
        ? (Application.isMobilePlatform ? MobileOverlayInFlightLoadCap : DesktopOverlayInFlightLoadCap)
        : (Application.isMobilePlatform ? 24 : 32);
      cap = Mathf.Max(throttled, minThrottledCap);
    }
    var minCap = Application.isMobilePlatform ? 24 : 32;
    if (IsLoadingScreenStreamingContextActive()) minCap = Application.isMobilePlatform ? MobileOverlayInFlightLoadCap : DesktopOverlayInFlightLoadCap;
    return Math.Max(cap, minCap);
  }

  static void EnqueueLoad(CacheEntry entry, LoadPriority priority) {
    if (entry == null || entry.isEvicted || entry.isDone || entry.loadStarted) return;
    if (!entry.isQueued) {
      entry.isQueued = true;
      entry.queuedPriority = priority;
      entry.queuedAtTicks = DateTime.UtcNow.Ticks;
      queuedEntryCount++;
      if (sessionExpectedTotal > 0 && sessionGeneration > 0) sessionTotalScheduled++;
      RecordQueueAddForFrame();
      EnqueueByPriority(entry, priority);
      RecordAssetTraceQueue(entry, priority);
      return;
    }
    if (priority < entry.queuedPriority) {
      entry.queuedPriority = priority;
      EnqueueByPriority(entry, priority);
    }
  }

  static void EnqueueByPriority(CacheEntry entry, LoadPriority priority) {
    switch (priority) {
      case LoadPriority.Immediate: immediateQueue.Enqueue(entry); break;
      case LoadPriority.Warmup: warmupQueue.Enqueue(entry); break;
      default: backgroundQueue.Enqueue(entry); break;
    }
  }

  static bool TryDequeueNext(out CacheEntry entry, out LoadPriority priority) {
    if (immediateQueue.Count > 0) { entry = immediateQueue.Dequeue(); priority = LoadPriority.Immediate; return true; }
    if (warmupQueue.Count > 0) { entry = warmupQueue.Dequeue(); priority = LoadPriority.Warmup; return true; }
    if (backgroundQueue.Count > 0) { entry = backgroundQueue.Dequeue(); priority = LoadPriority.Background; return true; }
    entry = null;
    priority = LoadPriority.Background;
    return false;
  }

  static void ClearQueuedFlag(CacheEntry entry) {
    if (entry == null || !entry.isQueued) return;
    entry.isQueued = false;
    if (queuedEntryCount > 0) queuedEntryCount--;
  }

  static void MarkInFlightComplete(CacheEntry entry) {
    if (entry == null || !entry.countedInFlight) return;
    entry.countedInFlight = false;
    if (inFlightLoads > 0) inFlightLoads--;
    MarkSessionEntryCompleted(entry);
  }

  static void MarkSessionEntryCompleted(CacheEntry entry) {
    if (entry == null || sessionExpectedTotal <= 0 || sessionGeneration <= 0) return;
    if (entry.sessionCompletionGeneration == sessionGeneration) return;
    entry.sessionCompletionGeneration = sessionGeneration;
    sessionTotalCompleted++;
  }

  static void ResetFrameCounterIfNeeded() {
    var frame = Time.frameCount;
    if (frame == lastPumpFrame) return;
    lastPumpFrame = frame;
    startedLoadsThisFrame = 0;
  }

  static bool ShouldSkipPumpForCurrentFrame(int maxStarts) {
    if (pendingBudgetMaintain || pendingQueueStateRecord || pendingTextureRegisterQueue.Count > 0) return false;
    if (!StreamingWarmOrchestrator.IsWarmGateRunning && deferredRequests.Count > 0) return false;
    var frame = Time.frameCount;
    if (frame != lastPumpSnapshotFrame) return false;
    if (queuedEntryCount != lastPumpSnapshotQueuedCount || inFlightLoads != lastPumpSnapshotInFlightCount) return false;
    if (queuedEntryCount <= 0) return true;
    if (immediateQueue.Count > 0) return false;
    return startedLoadsThisFrame >= Math.Max(maxStarts, 1);
  }

  static void CachePumpFrameSnapshot() {
    lastPumpSnapshotFrame = Time.frameCount;
    lastPumpSnapshotQueuedCount = queuedEntryCount;
    lastPumpSnapshotInFlightCount = inFlightLoads;
  }

}
}
#endif

