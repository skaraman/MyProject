#if false
using System.Collections.Generic;
using UnityEngine;

namespace AddressableSpriteCacheAssetStreaming {

public static partial class TextureResidencyCache {

  static void FlushDeferredRequestsIntoMainQueues() {
    if (StreamingWarmOrchestrator.IsWarmGateRunning) return;
    if (deferredRequests.Count <= 0) {
      if (deferredImmediateQueue.Count > 0 || deferredWarmupQueue.Count > 0 || deferredBackgroundQueue.Count > 0) {
        deferredImmediateQueue.Clear();
        deferredWarmupQueue.Clear();
        deferredBackgroundQueue.Clear();
      }
      return;
    }
    var frame = Time.frameCount;
    if (deferredFlushFrame != frame) {
      deferredFlushFrame = frame;
      deferredFlushedThisFrame = 0;
    }
    var flushBudget = SpriteStreamingLoadingState.IsLoadingOverlayActive
      ? DeferredFlushOverlayBudgetPerFrame
      : DeferredFlushDefaultBudgetPerFrame;
    if (ShouldUseStrictSerialLoadingDebounce()) flushBudget = StrictSerialLoadingBudgetPerFrame;
    if (queuedEntryCount >= 512 || inFlightLoads >= 64) flushBudget = Math.Min(flushBudget, DeferredFlushPressureBudgetPerFrame);
    flushBudget = Math.Max(flushBudget, 0);
    if (deferredFlushedThisFrame >= flushBudget) return;
    var remainingBudget = flushBudget - deferredFlushedThisFrame;
    var attempts = 0;
    while (attempts < remainingBudget) {
      if (!TryDequeueDeferredRequest(out var normalizedAddress, out var sourcePriority)) break;
      attempts++;
      if (string.IsNullOrWhiteSpace(normalizedAddress)) continue;
      if (!deferredRequests.TryGetValue(normalizedAddress, out var deferredState)) continue;
      if (deferredState.priority != sourcePriority) continue;
      deferredRequests.Remove(normalizedAddress);
      var entry = ResolveEntryForLoad(normalizedAddress, out _);
      EnqueueLoad(entry, deferredState.priority);
      deferredFlushedThisFrame++;
    }
  }

  static void EnqueueDeferredRequest(string normalizedAddress, LoadPriority priority, bool pinEntry) {
    if (string.IsNullOrWhiteSpace(normalizedAddress)) return;
    deferredRequestCount++;
    if (!deferredRequests.TryGetValue(normalizedAddress, out var state)) {
      state = new DeferredRequestState { priority = priority, pinEntry = pinEntry };
      deferredRequests[normalizedAddress] = state;
      deferredTotalCount++;
      EnqueueDeferredByPriority(normalizedAddress, priority);
      return;
    }
    var mergedPriority = priority < state.priority ? priority : state.priority;
    var mergedPinEntry = state.pinEntry || pinEntry;
    var priorityChanged = mergedPriority != state.priority;
    state.priority = mergedPriority;
    state.pinEntry = mergedPinEntry;
    deferredRequests[normalizedAddress] = state;
    if (priorityChanged) EnqueueDeferredByPriority(normalizedAddress, mergedPriority);
  }

  static bool TryPromoteDeferredRequest(string normalizedAddress, LoadPriority requestedPriority, out LoadPriority effectivePriority, out bool deferredPinEntry) {
    effectivePriority = requestedPriority;
    deferredPinEntry = false;
    if (string.IsNullOrWhiteSpace(normalizedAddress)) return false;
    if (!deferredRequests.TryGetValue(normalizedAddress, out var deferredState)) return false;
    deferredRequests.Remove(normalizedAddress);
    deferredPromotedCount++;
    if (deferredState.priority < effectivePriority) effectivePriority = deferredState.priority;
    deferredPinEntry = deferredState.pinEntry;
    return true;
  }

  static void EnqueueDeferredByPriority(string normalizedAddress, LoadPriority priority) {
    switch (priority) {
      case LoadPriority.Immediate: deferredImmediateQueue.Enqueue(normalizedAddress); break;
      case LoadPriority.Warmup: deferredWarmupQueue.Enqueue(normalizedAddress); break;
      default: deferredBackgroundQueue.Enqueue(normalizedAddress); break;
    }
  }

  static bool TryDequeueDeferredRequest(out string normalizedAddress, out LoadPriority sourcePriority) {
    if (deferredImmediateQueue.Count > 0) { normalizedAddress = deferredImmediateQueue.Dequeue(); sourcePriority = LoadPriority.Immediate; return true; }
    if (deferredWarmupQueue.Count > 0) { normalizedAddress = deferredWarmupQueue.Dequeue(); sourcePriority = LoadPriority.Warmup; return true; }
    if (deferredBackgroundQueue.Count > 0) { normalizedAddress = deferredBackgroundQueue.Dequeue(); sourcePriority = LoadPriority.Background; return true; }
    normalizedAddress = "";
    sourcePriority = LoadPriority.Background;
    return false;
  }

  static bool ShouldDeferNonManagedWarmGateRequest(CacheEntry entry) {
    if (entry == null) return false;
    if (entry.isDone || entry.loadStarted || entry.isEvicted || entry.isQueued) return false;
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive) return false;
    if (StreamingWarmOrchestrator.IsWarmGateRunning) return true;
    return deferredRequests.Count > 0;
  }

}
}
#endif

