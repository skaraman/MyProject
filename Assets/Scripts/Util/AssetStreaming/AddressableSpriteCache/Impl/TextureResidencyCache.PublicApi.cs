using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static partial class TextureResidencyCache {
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    settingsLoaded = false;
    settings = default;
    residentBytes = 0;
    queuedEntryCount = 0;
    inFlightLoads = 0;
    startedLoadsThisFrame = 0;
    lastPumpFrame = -1;
    pumpOncePerFrameFrame = -1;
    lastPumpSnapshotFrame = -1;
    lastPumpSnapshotQueuedCount = -1;
    lastPumpSnapshotInFlightCount = -1;
    deferredFlushFrame = -1;
    deferredFlushedThisFrame = 0;
    deferredTotalCount = 0;
    deferredPromotedCount = 0;
    deferredRequestCount = 0;
    completionFollowupFrame = -1;
    pendingBudgetMaintain = false;
    pendingQueueStateRecord = false;
    cacheHits = 0;
    cacheMisses = 0;
    pinDemotions = 0;
    pinClassBudgetHitCount = 0;
    pinClassBudgetDroppedAddresses = 0;
    ownerPinMutationDepth = 0;
    lastBudgetMaintainFrame = -1;
    loadCompletionDiagFrame = -1;
    loadCompletionDiagFrameTotalMs = 0f;
    loadCompletionDiagFrameRegisterMs = 0f;
    loadCompletionDiagFrameMaintainMs = 0f;
    loadCompletionDiagFrameCount = 0;
    loadCompletionDiagFrameReported = false;
    requestDiagFrame = -1;
    requestDiagAcquireCalls = 0;
    requestDiagWarmupCalls = 0;
    requestDiagQueueAdds = 0;
    requestDiagNewEntries = 0;
    requestDiagPumpCalls = 0;
    requestDiagStartedLoads = 0;
    requestDiagPumpTotalMs = 0f;
    requestDiagSourceCounts.Clear();
    atlasExpansionFrame = -1;
    atlasExpansionCountThisFrame = 0;
    atlasExpansionAddressBudgetFrame = -1;
    atlasExpansionAddressesQueuedThisFrame = 0;
    overlayStartTokens = 0f;
    overlayStartTokenLastRefillAt = -1f;
    loadingContextModeLogged = false;
    loadingContextModeReason = "";
    completionPressureUntilFrame = -1;
    sessionTotalScheduled = 0;
    sessionTotalCompleted = 0;
    sessionExpectedTotal = 0;
    sessionGeneration = 0;
    textureRefCounts.Clear();
    textureBytesById.Clear();
    immediateQueue.Clear();
    warmupQueue.Clear();
    backgroundQueue.Clear();
    pendingAssetLoadStartQueue.Clear();
    pendingLoadFinalizeQueue.Clear();
    pendingExactSliceSupplementQueue.Clear();
    pendingTextureRegisterQueue.Clear();
    pooledLeases.Clear();
#if UNITY_EDITOR
    pendingEditorAtlasSupplementQueue.Clear();
    editorAtlasSupplementWarnings.Clear();
    editorImportedAtlasSpriteCache.Clear();
#endif
    incompleteAtlasLoadWarnings.Clear();
    atlasSynthesisFailureWarnings.Clear();
    spriteLoadOperationFailureWarnings.Clear();
    deferredRequests.Clear();
    deferredImmediateQueue.Clear();
    deferredWarmupQueue.Clear();
    deferredBackgroundQueue.Clear();
    ownerPins.Clear();
    expandedAtlasKeys.Clear();
    atlasExpansionRetryFrames.Clear();
    gameplayColdMissAtlasKeys.Clear();
    unsupportedSpriteAddressWarnings.Clear();
    atlasSiblingAddressScratch.Clear();
    ownerDemoteScratch.Clear();
    enableLoadStartDiagnostics = true;
    loadStartSlowThresholdMs = 25f;
    PurgeAll();
  }

  public static int LoadedEntryCount => cache.Count;

  public static long EstimatedResidentBytes {
    get { return Math.Max(residentBytes, 0); }
  }

  public static QueueSnapshot GetQueueSnapshot(bool pump = true) {
    if (pump) {
      Pump();
    }
    return new QueueSnapshot(queuedEntryCount, inFlightLoads);
  }

  public static void PumpOncePerFrame() {
    var frame = Time.frameCount;
    if (pumpOncePerFrameFrame == frame) return;
    pumpOncePerFrameFrame = frame;

    PumpProfilerMarker.Begin();
    Pump();
    PumpProfilerMarker.End();
  }

  public static DeferredSnapshot GetDeferredSnapshot() {
    var flushedThisFrame = deferredFlushFrame == Time.frameCount ? deferredFlushedThisFrame : 0;
    return new DeferredSnapshot(
      pendingCount: deferredRequests.Count,
      flushedThisFrame: flushedThisFrame,
      totalDeferredCount: deferredTotalCount,
      totalPromotedCount: deferredPromotedCount,
      totalDeferralRequestCount: deferredRequestCount
    );
  }

  public static float GetAverageLoadCompleteLatencyMs() => loadCompleteLatencyRollingAvgMs;

  public static bool IsQueueIdle(bool pump = true) {
    return GetQueueSnapshot(pump).IsIdle;
  }

  public static void BeginSession(int expectedTotal) {
    sessionTotalScheduled = 0;
    sessionTotalCompleted = 0;
    sessionExpectedTotal = Math.Max(expectedTotal, 0);
    sessionGeneration = sessionGeneration == int.MaxValue ? 1 : sessionGeneration + 1;
  }

  public static void EndSession() {
    sessionExpectedTotal = 0;
    sessionGeneration = 0;
  }

  public static SessionSnapshot GetSessionSnapshot() {
    return new SessionSnapshot(
      expectedTotal: sessionExpectedTotal,
      scheduledTotal: sessionTotalScheduled,
      completedTotal: sessionTotalCompleted
    );
  }

  public static float GetSessionProgress() {
    return GetSessionSnapshot().Progress;
  }

  public static Lease AcquireAsync(
    string address,
    LoadPriority priority = LoadPriority.Immediate,
    bool warmGateManaged = false,
    [CallerMemberName] string callerMemberName = "",
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLineNumber = 0
  ) {
    var normalizedAddress = NormalizeAddress(address);
    if (string.IsNullOrEmpty(normalizedAddress)) return null;
    var sourceTag = ShouldLogRequestFrameDiagnostics()
      ? BuildRequestDiagSourceTag(callerMemberName, callerFilePath, callerLineNumber)
      : "";
    return AcquireAsyncNormalized(
      address,
      normalizedAddress,
      priority,
      runPumpAndMaintain: ShouldRunInlinePumpAfterRequest(priority),
      sourceTag: sourceTag,
      warmGateManaged: warmGateManaged
    );
  }

  static Lease AcquireAsyncNormalized(
    string requestedAddress,
    string normalizedAddress,
    LoadPriority priority,
    bool runPumpAndMaintain,
    string sourceTag,
    bool warmGateManaged
  ) {
    RecordRequestForFrame(isAcquire: true, sourceTag: sourceTag);
    var entry = ResolveEntryForLoad(normalizedAddress, out var hit);
    TrackEntryRequestContext(entry, requestedAddress);
    TrackEntryRequestedSpriteHint(entry, requestedAddress);
    RecordLookup(hit);
    RecordGameplayColdAtlasMiss(normalizedAddress, hit);
    QueueEntryForLoad(entry, priority, pinEntry: true, runPumpAndMaintain, warmGateManaged, sourceTag);
    TryExpandAtlasOnSliceRequest(requestedAddress, priority, runPumpAndMaintain);
    return AcquireLease(entry);
  }

  public static void RequestLoad(
    string address,
    LoadPriority priority = LoadPriority.Warmup,
    bool warmGateManaged = false,
    [CallerMemberName] string callerMemberName = "",
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLineNumber = 0
  ) {
    var normalizedAddress = NormalizeAddress(address);
    if (string.IsNullOrEmpty(normalizedAddress)) return;
    var requestDiagnosticsEnabled = ShouldLogRequestFrameDiagnostics();
    var sourceTag = requestDiagnosticsEnabled
      ? BuildRequestDiagSourceTag(callerMemberName, callerFilePath, callerLineNumber)
      : "";
    if (requestDiagnosticsEnabled) {
      RecordRequestForFrame(isAcquire: false, sourceTag: sourceTag);
    }

    var entry = ResolveEntryForLoad(normalizedAddress, out var hit);
    TrackEntryRequestContext(entry, address);
    TrackEntryRequestedSpriteHint(entry, address);
    RecordLookup(hit);
    RecordGameplayColdAtlasMiss(normalizedAddress, hit);
    var runPumpAndMaintain = ShouldRunInlinePumpAfterRequest(priority);
    QueueEntryForLoad(entry, priority, pinEntry: false, runPumpAndMaintain, warmGateManaged, sourceTag);
    TryExpandAtlasOnSliceRequest(address, priority, runPumpAndMaintain);
  }

  public static void RequestLoadBatch(
    IEnumerable<string> addresses,
    LoadPriority priority = LoadPriority.Warmup,
    bool allowAtlasExpansion = true,
    bool warmGateManaged = false,
    [CallerMemberName] string callerMemberName = "",
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLineNumber = 0
  ) {
    if (addresses == null) return;
    var requestDiagnosticsEnabled = ShouldLogRequestFrameDiagnostics();
    var sourceTag = requestDiagnosticsEnabled
      ? BuildRequestDiagSourceTag(callerMemberName, callerFilePath, callerLineNumber)
      : "";

    foreach (var address in addresses) {
      var normalizedAddress = NormalizeAddress(address);
      if (string.IsNullOrEmpty(normalizedAddress)) continue;
      if (requestDiagnosticsEnabled) {
        RecordRequestForFrame(isAcquire: false, sourceTag: sourceTag);
      }

      var entry = ResolveEntryForLoad(normalizedAddress, out var hit);
      TrackEntryRequestContext(entry, address);
      TrackEntryRequestedSpriteHint(entry, address);
      RecordLookup(hit);
      RecordGameplayColdAtlasMiss(normalizedAddress, hit);
      QueueEntryForLoad(entry, priority, pinEntry: false, runPumpAndMaintain: false, warmGateManaged: warmGateManaged, sourceTag: sourceTag);
      if (allowAtlasExpansion) {
        TryExpandAtlasOnSliceRequest(address, priority, runPumpAndMaintain: false);
      }
    }

    Pump();
    MaintainBudget();
  }

  public static IEnumerator RequestLoadBatchThrottled(
    IEnumerable<string> addresses,
    LoadPriority priority = LoadPriority.Warmup,
    bool allowAtlasExpansion = true,
    int enqueueBudgetPerFrame = 128,
    bool warmGateManaged = false,
    [CallerMemberName] string callerMemberName = "",
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLineNumber = 0
  ) {
    if (addresses == null) yield break;
    var requestDiagnosticsEnabled = ShouldLogRequestFrameDiagnostics();
    var sourceTag = requestDiagnosticsEnabled
      ? BuildRequestDiagSourceTag(callerMemberName, callerFilePath, callerLineNumber)
      : "";

    enqueueBudgetPerFrame = ResolveAdaptiveEnqueueBudgetPerFrame(enqueueBudgetPerFrame);
    var remainingThisFrame = enqueueBudgetPerFrame;

    foreach (var address in addresses) {
      var normalizedAddress = NormalizeAddress(address);
      if (string.IsNullOrEmpty(normalizedAddress)) continue;
      if (requestDiagnosticsEnabled) {
        RecordRequestForFrame(isAcquire: false, sourceTag: sourceTag);
      }

      var entry = ResolveEntryForLoad(normalizedAddress, out var hit);
      TrackEntryRequestContext(entry, address);
      TrackEntryRequestedSpriteHint(entry, address);
      RecordLookup(hit);
      RecordGameplayColdAtlasMiss(normalizedAddress, hit);
      QueueEntryForLoad(entry, priority, pinEntry: false, runPumpAndMaintain: false, warmGateManaged: warmGateManaged, sourceTag: sourceTag);
      if (allowAtlasExpansion) {
        TryExpandAtlasOnSliceRequest(address, priority, runPumpAndMaintain: false);
      }

      remainingThisFrame--;
      if (remainingThisFrame > 0) continue;

      Pump();
      MaintainBudget();
      remainingThisFrame = enqueueBudgetPerFrame;
      yield return null;
    }

    Pump();
    MaintainBudget();
  }

  public static IEnumerator RequestLoadBatchThrottled(
    IList<string> addresses,
    int startInclusive,
    int count,
    LoadPriority priority = LoadPriority.Warmup,
    bool allowAtlasExpansion = true,
    int enqueueBudgetPerFrame = 128,
    bool warmGateManaged = false,
    [CallerMemberName] string callerMemberName = "",
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLineNumber = 0
  ) {
    if (addresses == null || addresses.Count <= 0 || count <= 0) yield break;
    var start = Mathf.Clamp(startInclusive, 0, addresses.Count);
    var requestedCount = Math.Max(count, 0);
    var endExclusive = (int)Math.Min((long)addresses.Count, (long)start + requestedCount);
    if (start >= endExclusive) yield break;

    var requestDiagnosticsEnabled = ShouldLogRequestFrameDiagnostics();
    var sourceTag = requestDiagnosticsEnabled
      ? BuildRequestDiagSourceTag(callerMemberName, callerFilePath, callerLineNumber)
      : "";

    enqueueBudgetPerFrame = ResolveAdaptiveEnqueueBudgetPerFrame(enqueueBudgetPerFrame);
    var remainingThisFrame = enqueueBudgetPerFrame;

    for (var i = start; i < endExclusive; i++) {
      var address = addresses[i];
      var normalizedAddress = NormalizeAddress(address);
      if (string.IsNullOrEmpty(normalizedAddress)) continue;
      if (requestDiagnosticsEnabled) {
        RecordRequestForFrame(isAcquire: false, sourceTag: sourceTag);
      }

      var entry = ResolveEntryForLoad(normalizedAddress, out var hit);
      TrackEntryRequestContext(entry, address);
      TrackEntryRequestedSpriteHint(entry, address);
      RecordLookup(hit);
      RecordGameplayColdAtlasMiss(normalizedAddress, hit);
      QueueEntryForLoad(entry, priority, pinEntry: false, runPumpAndMaintain: false, warmGateManaged: warmGateManaged, sourceTag: sourceTag);
      if (allowAtlasExpansion) {
        TryExpandAtlasOnSliceRequest(address, priority, runPumpAndMaintain: false);
      }

      remainingThisFrame--;
      if (remainingThisFrame > 0) continue;

      Pump();
      MaintainBudget();
      remainingThisFrame = enqueueBudgetPerFrame;
      yield return null;
    }

    Pump();
    MaintainBudget();
  }

  static int ResolveAdaptiveEnqueueBudgetPerFrame(int requestedBudgetPerFrame) {
    if (ShouldUseStrictSerialLoadingDebounce()) {
      return StrictSerialLoadingBudgetPerFrame;
    }
    var budget = Mathf.Clamp(requestedBudgetPerFrame, 50, 200);
    var memoryMb = Math.Max(SystemInfo.systemMemorySize, 0);
    if (memoryMb > 0 && memoryMb <= 4096) budget = Math.Min(budget, 80);
    else if (memoryMb > 0 && memoryMb <= 8192) budget = Math.Min(budget, 120);

    if (queuedEntryCount >= 1400 || inFlightLoads >= 192) budget = Math.Min(budget, 60);
    else if (queuedEntryCount >= 900 || inFlightLoads >= 128) budget = Math.Min(budget, 90);
    else if (queuedEntryCount >= 500 || inFlightLoads >= 64) budget = Math.Min(budget, 120);

    return Mathf.Clamp(budget, 50, 200);
  }

  static bool ShouldRunInlinePumpAfterRequest(LoadPriority priority) {
    if (priority != LoadPriority.Immediate) return false;
    // Keep request paths non-blocking during gameplay. A single per-frame PumpOncePerFrame
    // call (from animation ticks) is enough to advance queue work without transition spikes.
    return false;
  }

  public static bool TryGetLoadedSprite(string address, out Sprite sprite, bool pump = true) {
    if (SpriteSliceAddressUtility.TryParseSliceAddress(address, out _, out var spriteName)) {
      return TryGetLoadedSprite(ResolveRequestOwnerAddress(address), spriteName, out sprite, pump, address);
    }
    return TryGetLoadedSprite(address, spriteName: "", out sprite, pump);
  }

  public static bool TryGetLoadedSprite(string atlasAddress, string spriteName, out Sprite sprite, bool pump = true) {
    return TryGetLoadedSprite(atlasAddress, spriteName, out sprite, pump, atlasAddress);
  }

  static bool TryGetLoadedSprite(string atlasAddress, string spriteName, out Sprite sprite, bool pump, string requestedAddress) {
    sprite = null;
    var normalizedAddress = NormalizeAddress(atlasAddress);
    if (string.IsNullOrEmpty(normalizedAddress)) return false;

    if (pump) {
      Pump();
    }
    if (!cache.TryGetValue(normalizedAddress, out var entry) || entry == null) return false;
    if (!entry.isDone || !entry.isSuccess || entry.primarySprite == null) return false;

    entry.lastAccessTicks = frameAccessTicks;
    var resolved = TryGetSpriteFromReadyEntry(entry, spriteName, out sprite);
    if (resolved && !string.IsNullOrWhiteSpace(spriteName)) {
      SpriteStreamingDiagnostics.RecordResidentSliceLookup();
    }
    if (!resolved) {
      EnsureRequestedSliceSupplement(entry, requestedAddress);
    }
    return resolved;
  }

  public static bool IsReady(string address, bool pump = true) {
    return GetRequestState(address, pump).IsCommitReady();
  }

  public static bool IsAtlasReady(string atlasAddress, bool pump = true) {
    return GetRequestState(atlasAddress, pump) == SpriteColdLoadState.Ready;
  }

  public static SpriteColdLoadState GetPreparedAtlasState(string address, bool pump = true) {
    if (string.IsNullOrWhiteSpace(address)) return SpriteColdLoadState.Missing;
    if (pump) {
      Pump();
    }

    if (!TryResolveRequestOwnerAddress(address, out var ownerAddress, out _, out _)) {
      return SpriteColdLoadState.Missing;
    }
    if (!cache.TryGetValue(ownerAddress, out var entry) || entry == null) {
      return SpriteColdLoadState.Pending;
    }
    if (!entry.isDone) return SpriteColdLoadState.Pending;
    if (!entry.isSuccess || entry.primarySprite == null) return SpriteColdLoadState.Missing;
    if (entry.spriteMapMaterialized) return SpriteColdLoadState.Ready;
    if (TryEnsureEntrySpriteMapMaterialized(entry)) return SpriteColdLoadState.Ready;
    return SpriteColdLoadState.Missing;
  }

  public static string GetQueueSourceBreakdown() {
    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var deferredCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    foreach (var pair in cache) {
      var entry = pair.Value;
      if (entry == null) continue;
      if (entry.isQueued || (entry.loadStarted && !entry.isDone)) {
        var src = string.IsNullOrEmpty(entry.sourceTag) ? "Unknown" : entry.sourceTag;
        counts[src] = counts.TryGetValue(src, out var c) ? c + 1 : 1;
      }
    }

    foreach (var pair in deferredRequests) {
      var state = pair.Value;
      var src = string.IsNullOrEmpty(state.sourceTag) ? "Unknown" : state.sourceTag;
      deferredCounts[src] = deferredCounts.TryGetValue(src, out var c) ? c + 1 : 1;
    }

    var sb = new StringBuilder();
    sb.AppendLine("=== TextureResidencyCache Source Breakdown ===");
    sb.AppendLine($"Total Queued/InFlight: {queuedEntryCount} (InFlight: {inFlightLoads})");
    foreach (var pair in counts) {
      sb.AppendLine($"  Source: '{pair.Key}' -> {pair.Value} active/queued");
    }
    sb.AppendLine($"Total Deferred: {deferredRequests.Count}");
    foreach (var pair in deferredCounts) {
      sb.AppendLine($"  Source: '{pair.Key}' -> {pair.Value} deferred");
    }
    return sb.ToString();
  }

  public static void LogRequestDiagnosticsSummary() {
    var sb = new StringBuilder();
    sb.AppendLine("=== TextureResidencyCache Detailed Diagnostics ===");
    sb.AppendLine($"Resident Bytes: {residentBytes} | Active Loads: {inFlightLoads} | Queued: {queuedEntryCount}");
    sb.AppendLine($"Deferred Request Count: {deferredRequests.Count}");
    
    sb.AppendLine("Pending/Active Cache Entries:");
    var count = 0;
    foreach (var pair in cache) {
      var entry = pair.Value;
      if (entry == null) continue;
      if (entry.isQueued || (entry.loadStarted && !entry.isDone)) {
        sb.AppendLine($"  Address: '{entry.address}' | Priority: {entry.queuedPriority} | Done: {entry.isDone} | Success: {entry.isSuccess} | Source: '{entry.sourceTag}'");
        count++;
        if (count >= 100) {
          sb.AppendLine("  ... (truncated output for first 100 entries)");
          break;
        }
      }
    }

    sb.AppendLine("Deferred Requests:");
    var defCount = 0;
    foreach (var pair in deferredRequests) {
      var address = pair.Key;
      var state = pair.Value;
      sb.AppendLine($"  Address: '{address}' | Priority: {state.priority} | Pin: {state.pinEntry} | Source: '{state.sourceTag}'");
      defCount++;
      if (defCount >= 100) {
        sb.AppendLine("  ... (truncated output for first 100 entries)");
        break;
      }
    }
    
    RuntimeLog.Log(sb.ToString());
  }
}
