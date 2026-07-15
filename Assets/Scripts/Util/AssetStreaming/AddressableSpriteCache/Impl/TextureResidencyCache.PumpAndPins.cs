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
  public static SpriteColdLoadState GetRequestState(string address, bool pump = true) {
    if (string.IsNullOrWhiteSpace(address)) return SpriteColdLoadState.Missing;
    if (pump) {
      Pump();
    }

    if (!TryResolveRequestOwnerAddress(address, out var ownerAddress, out _, out _)) {
      return SpriteColdLoadState.Missing;
    }

    var normalizedAddress = NormalizeAddress(ownerAddress);
    if (string.IsNullOrEmpty(normalizedAddress)) return SpriteColdLoadState.Missing;
    if (!cache.TryGetValue(normalizedAddress, out var entry) || entry == null) {
      return SpriteColdLoadState.Pending;
    }

    return GetRequestStateFromEntry(entry, address);
  }

  static SpriteColdLoadState GetRequestStateFromEntry(CacheEntry entry, string requestedAddress) {
    if (entry == null) return SpriteColdLoadState.Pending;
    if (!TryResolveRequestOwnerAddress(requestedAddress, out _, out var spriteName, out _)) {
      return SpriteColdLoadState.Missing;
    }

    return GetRequestStateFromEntry(entry, requestedAddress, spriteName);
  }

  static SpriteColdLoadState GetRequestStateFromEntry(CacheEntry entry, string requestedAddress, string spriteName) {
    if (entry == null) return SpriteColdLoadState.Pending;
    if (!entry.isDone) return SpriteColdLoadState.Pending;
    if (!entry.isSuccess || entry.primarySprite == null) return SpriteColdLoadState.Missing;
    if (string.IsNullOrWhiteSpace(spriteName)) {
      return entry.primarySprite != null ? SpriteColdLoadState.Ready : SpriteColdLoadState.Missing;
    }

    if (TryGetSpriteFromEntryWithoutMaterialization(entry, spriteName, out _)) {
      entry.lastAccessTicks = frameAccessTicks;
      return SpriteColdLoadState.Ready;
    }

    EnsureRequestedSliceSupplement(entry, requestedAddress);
    var normalizedRequestedAddress = string.IsNullOrWhiteSpace(requestedAddress) ? "" : requestedAddress.Trim();
    if (!string.IsNullOrWhiteSpace(normalizedRequestedAddress)) {
      if (entry.pendingExactSliceSupplementAddresses != null &&
          entry.pendingExactSliceSupplementAddresses.Contains(normalizedRequestedAddress)) {
        return SpriteColdLoadState.Pending;
      }

      if (entry.failedExactSliceSupplementAddresses != null &&
          entry.failedExactSliceSupplementAddresses.Contains(normalizedRequestedAddress)) {
        return SpriteColdLoadState.Missing;
      }
    }

    if (entry.editorAtlasSupplementPending) {
      return SpriteColdLoadState.Pending;
    }

    if (!entry.spriteMapMaterialized &&
        (entry.deferredSpriteMapMaterialization || CanMaterializeEntrySpriteMapOnDemand(entry))) {
      return SpriteColdLoadState.Pending;
    }

    return SpriteColdLoadState.Missing;
  }

  public static void UpdateOwnerPins(
    string ownerId,
    PinClass pinClass,
    List<string> addresses,
    LoadPriority priority = LoadPriority.Warmup
  ) {
    var normalizedOwnerId = NormalizeOwnerId(ownerId);
    if (string.IsNullOrWhiteSpace(normalizedOwnerId)) return;

    if (addresses == null || addresses.Count == 0) {
      ReleaseOwnerPins(normalizedOwnerId);
      return;
    }

    // Residency ownership is one lease per physical parent atlas.
    // Keep one exact request per parent so load hints and atlas expansion remain intact.
    desiredOwnerAddressScratch.Clear();
    desiredOwnerRequestScratch.Clear();
    for (var i = 0; i < addresses.Count; i++) {
      var requestedAddress = addresses[i];
      var normalizedAddress = NormalizePinLeaseAddress(requestedAddress);
      if (string.IsNullOrWhiteSpace(normalizedAddress)) continue;
      desiredOwnerAddressScratch.Add(normalizedAddress);
      if (desiredOwnerRequestScratch.ContainsKey(normalizedAddress)) continue;
      desiredOwnerRequestScratch[normalizedAddress] = requestedAddress;
    }

    if (desiredOwnerAddressScratch.Count == 0) {
      desiredOwnerAddressScratch.Clear();
      desiredOwnerRequestScratch.Clear();
      ReleaseOwnerPins(normalizedOwnerId);
      return;
    }

    // Fast path: leases already match â€” just refresh metadata without mutating the pin state.
    if (ownerPins.TryGetValue(normalizedOwnerId, out var existingState) &&
        existingState != null &&
        AddressesMatchExistingLeases(existingState.leases, desiredOwnerAddressScratch)) {
      existingState.pinClass = pinClass;
      existingState.lastRefreshTicks = DateTime.UtcNow.Ticks;
      desiredOwnerAddressScratch.Clear();
      desiredOwnerRequestScratch.Clear();
      return;
    }

    ownerPinMutationDepth++;
    try {
      if (!ownerPins.TryGetValue(normalizedOwnerId, out var state) || state == null) {
        state = new OwnerPinState {
          ownerId = normalizedOwnerId,
          pinClass = pinClass,
          lastRefreshTicks = DateTime.UtcNow.Ticks
        };
        ownerPins[normalizedOwnerId] = state;
      }

      state.pinClass = pinClass;
      state.lastRefreshTicks = DateTime.UtcNow.Ticks;
      var classBudget = GetPinClassBudget(pinClass);
      var classBudgetHit = false;
      var classBudgetDropped = 0;

      ownerReleaseAddressScratch.Clear();
      foreach (var pair in state.leases) {
        if (desiredOwnerAddressScratch.Contains(pair.Key)) continue;
        ownerReleaseAddressScratch.Add(pair.Key);
      }

      for (var i = 0; i < ownerReleaseAddressScratch.Count; i++) {
        if (!state.leases.TryGetValue(ownerReleaseAddressScratch[i], out var lease) || lease == null) continue;
        lease.Release();
        state.leases.Remove(ownerReleaseAddressScratch[i]);
      }

      if (classBudget > 0 && state.leases.Count > classBudget) {
        classBudgetHit = true;
        var overflow = state.leases.Count - classBudget;
        // Reuse pooled scratch (already processed above) to avoid allocating a new key list.
        ownerReleaseAddressScratch.Clear();
        foreach (var key in state.leases.Keys) ownerReleaseAddressScratch.Add(key);
        for (var i = 0; i < ownerReleaseAddressScratch.Count && overflow > 0; i++) {
          if (!state.leases.TryGetValue(ownerReleaseAddressScratch[i], out var trimLease) || trimLease == null) continue;
          trimLease.Release();
          state.leases.Remove(ownerReleaseAddressScratch[i]);
          overflow--;
          classBudgetDropped++;
        }
      }

      foreach (var desiredAddress in desiredOwnerAddressScratch) {
        if (state.leases.ContainsKey(desiredAddress)) continue;
        if (!EnsurePinClassBudgetCapacity(pinClass, normalizedOwnerId, classBudget)) {
          classBudgetHit = true;
          classBudgetDropped++;
          continue;
        }
        if (!desiredOwnerRequestScratch.TryGetValue(desiredAddress, out var requestedAddress) ||
            string.IsNullOrWhiteSpace(requestedAddress)) {
          requestedAddress = desiredAddress;
        }
        var lease = AcquireAsyncNormalized(
          requestedAddress,
          desiredAddress,
          priority,
          runPumpAndMaintain: false,
          sourceTag: "UpdateOwnerPins",
          warmGateManaged: false
        );
        if (lease == null) continue;
        state.leases[desiredAddress] = lease;
      }

      if (state.leases.Count == 0) {
        ownerPins.Remove(normalizedOwnerId);
      }

      if (classBudgetHit) {
        pinClassBudgetHitCount++;
        pinClassBudgetDroppedAddresses += Math.Max(classBudgetDropped, 0);
        SpriteStreamingDiagnostics.RecordPinBudgetPressure(1, classBudgetDropped);
      }
    }
    finally {
      ownerReleaseAddressScratch.Clear();
      desiredOwnerAddressScratch.Clear();
      desiredOwnerRequestScratch.Clear();
      ownerPinMutationDepth = Math.Max(ownerPinMutationDepth - 1, 0);
    }

    pendingBudgetMaintain = true;
    pendingQueueStateRecord = true;
  }

  public static void ReleaseOwnerPins(string ownerId) {
    var normalizedOwnerId = NormalizeOwnerId(ownerId);
    if (string.IsNullOrWhiteSpace(normalizedOwnerId)) return;
    if (!ownerPins.TryGetValue(normalizedOwnerId, out var state) || state == null) return;

    ownerPinMutationDepth++;
    try {
      foreach (var lease in state.leases.Values) {
        lease?.Release();
      }
      state.leases.Clear();
      ownerPins.Remove(normalizedOwnerId);
    }
    finally {
      ownerPinMutationDepth = Math.Max(ownerPinMutationDepth - 1, 0);
    }

    pendingBudgetMaintain = true;
    pendingQueueStateRecord = true;
  }

  public static int GetOwnerPinCount(string ownerId) {
    var normalizedOwnerId = NormalizeOwnerId(ownerId);
    if (string.IsNullOrWhiteSpace(normalizedOwnerId)) return 0;
    if (!ownerPins.TryGetValue(normalizedOwnerId, out var state) || state == null) return 0;
    return Math.Max(state.leases.Count, 0);
  }

  public static PinSnapshot GetPinSnapshot() {
    var pinnedOwnerCount = 0;
    var pinnedAddressCount = 0;
    var pinnedPlayerAddresses = 0;
    var pinnedEnemyAddresses = 0;
    var pinnedUiAddresses = 0;
    var pinnedEffectAddresses = 0;

    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state == null) continue;
      var addressCount = state.leases.Count;
      if (addressCount <= 0) continue;
      pinnedOwnerCount++;
      pinnedAddressCount += addressCount;
      switch (state.pinClass) {
        case PinClass.Player:
          pinnedPlayerAddresses += addressCount;
          break;
        case PinClass.Enemy:
          pinnedEnemyAddresses += addressCount;
          break;
        case PinClass.UI:
          pinnedUiAddresses += addressCount;
          break;
        case PinClass.Effect:
          pinnedEffectAddresses += addressCount;
          break;
      }
    }

    return new PinSnapshot(
      pinnedOwnerCount: pinnedOwnerCount,
      pinnedAddressCount: pinnedAddressCount,
      pinnedPlayerAddresses: pinnedPlayerAddresses,
      pinnedEnemyAddresses: pinnedEnemyAddresses,
      pinnedUiAddresses: pinnedUiAddresses,
      pinnedEffectAddresses: pinnedEffectAddresses,
      pinDemotions: Math.Max(pinDemotions, 0),
      classBudgetHitCount: Math.Max(pinClassBudgetHitCount, 0),
      classBudgetDroppedAddresses: Math.Max(pinClassBudgetDroppedAddresses, 0)
    );
  }

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
      if (entry == null || entry.isEvicted) continue;
      if (!entry.isQueued) continue;
      if (entry.queuedPriority != sourcePriority) continue;
      if (entry.isDone || entry.loadStarted) {
        ClearQueuedFlag(entry);
        continue;
      }

      ClearQueuedFlag(entry);
      StartLoad(entry);
      startedLoadsThisFrame++;
      if (overlayStartAllowance != int.MaxValue) {
        overlayStartAllowance = Math.Max(overlayStartAllowance - 1, 0);
        ConsumeOverlayStartToken();
      }
    }

    // Drain a bounded set of remaining Immediate requests so Warmup/Background
    // backlog cannot starve active animation frame loads, without allowing
    // unbounded gameplay-time load bursts in a single frame.
    var immediateBurstRemaining = ResolveImmediateBurstBudget();
    while (immediateQueue.Count > 0) {
      if (immediateBurstRemaining == 0) break;
      if (inFlightLoads >= maxInFlightLoads) break;
      var immediateEntry = immediateQueue.Dequeue();
      if (immediateEntry == null || immediateEntry.isEvicted || !immediateEntry.isQueued) continue;
      if (immediateEntry.queuedPriority != LoadPriority.Immediate) continue;
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
    if (ShouldUseStrictSerialLoadingDebounce()) {
      return StrictSerialLoadingBudgetPerFrame;
    }
    var baseStarts = Math.Max(cfg.maxAddressableStartsPerFrame, 1);
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      var gameplayCap = Application.isMobilePlatform
        ? MobileGameplayMaxStartsPerFrameCap
        : DesktopGameplayMaxStartsPerFrameCap;
      return Mathf.Clamp(baseStarts, 1, gameplayCap);
    }
    var overlayStarts = Math.Max(cfg.loadingOverlayMaxAddressableStartsPerFrame, baseStarts);
    if (deferredRequests.Count > 0) {
      var drainCap = Application.isMobilePlatform ? 2 : 3;
      overlayStarts = Math.Min(overlayStarts, drainCap);
    }
    if (!IsCompletionPressureActive()) return overlayStarts;
    var throttledStarts = Mathf.CeilToInt(overlayStarts * CompletionPressureOverlayScale);
    var minStarts = Math.Min(baseStarts, overlayStarts);
    return Mathf.Clamp(throttledStarts, minStarts, overlayStarts);
  }

  static int ResolveImmediateBurstBudget() {
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      // The main pump already prioritizes Immediate ahead of Warmup/Background.
      // Allowing a second unbounded Immediate drain here bypasses the overlay cap
      // and creates the large queue bursts seen in loading-heartbeat gaps.
      return 0;
    }
    return Application.isMobilePlatform
      ? MobileGameplayImmediateBurstCap
      : DesktopGameplayImmediateBurstCap;
  }

  static int ResolveMaxInFlightLoads() {
    if (ShouldUseStrictSerialLoadingDebounce()) {
      return StrictSerialLoadingBudgetPerFrame;
    }
    var cap = Application.isMobilePlatform ? MobileInFlightLoadCap : DesktopInFlightLoadCap;
    var memoryMb = Math.Max(SystemInfo.systemMemorySize, 0);
    if (memoryMb > 0 && memoryMb <= 4096) cap = Math.Min(cap, 64);
    else if (memoryMb > 0 && memoryMb <= 8192) cap = Math.Min(cap, 96);

    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      var overlayCap = Application.isMobilePlatform ? MobileOverlayInFlightLoadCap : DesktopOverlayInFlightLoadCap;
      cap = Math.Min(cap, overlayCap);
    }
    else {
      var gameplayCap = Application.isMobilePlatform
        ? MobileGameplayInFlightLoadCap
        : DesktopGameplayInFlightLoadCap;
      cap = Math.Min(cap, gameplayCap);
    }

    if (IsCompletionPressureActive()) {
      var throttled = Mathf.CeilToInt(cap * 0.75f);
      var minThrottledCap = IsLoadingScreenStreamingContextActive()
        ? (Application.isMobilePlatform ? MobileOverlayInFlightLoadCap : DesktopOverlayInFlightLoadCap)
        : (Application.isMobilePlatform ? 24 : 32);
      cap = Mathf.Max(throttled, minThrottledCap);
    }

    var minCap = Application.isMobilePlatform ? 24 : 32;
    if (IsLoadingScreenStreamingContextActive()) {
      minCap = Application.isMobilePlatform ? MobileOverlayInFlightLoadCap : DesktopOverlayInFlightLoadCap;
    }
    return Math.Max(cap, minCap);
  }

  public static void Release(Lease lease) {
    if (lease == null) return;
    lease.Release();
    MaintainBudget();
  }

  public static void PurgeAll() {
    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state == null) continue;
      foreach (var lease in state.leases.Values) {
        lease?.Release();
      }
      state.leases.Clear();
    }
    ownerPins.Clear();
    expandedAtlasKeys.Clear();
    atlasExpansionRetryFrames.Clear();
    atlasSiblingAddressScratch.Clear();
#if UNITY_EDITOR
    editorAtlasSupplementWarnings.Clear();
    pendingEditorAtlasSupplementQueue.Clear();
    editorImportedAtlasSpriteCache.Clear();
#endif
    incompleteAtlasLoadWarnings.Clear();
    atlasSynthesisFailureWarnings.Clear();
    spriteLoadOperationFailureWarnings.Clear();
    deferredRequests.Clear();
    deferredImmediateQueue.Clear();
    deferredWarmupQueue.Clear();
    deferredBackgroundQueue.Clear();
    deferredFlushFrame = -1;
    deferredFlushedThisFrame = 0;
    deferredTotalCount = 0;
    deferredPromotedCount = 0;
    deferredRequestCount = 0;
    completionFollowupFrame = -1;
    pendingBudgetMaintain = false;
    pendingQueueStateRecord = false;

    foreach (var pair in cache) {
      var entry = pair.Value;
      if (entry == null) continue;
      entry.isEvicted = true;
      RecordAssetTraceRelease(entry, "purge_all");
      ClearQueuedFlag(entry);
      UnregisterTextureContribution(entry);
      ReleaseHandle(entry);
    }

    cache.Clear();
    residentBytes = 0;
    queuedEntryCount = 0;
    inFlightLoads = 0;
    pumpOncePerFrameFrame = -1;
    immediateQueue.Clear();
    warmupQueue.Clear();
    backgroundQueue.Clear();
    pendingAssetLoadStartQueue.Clear();
    pendingLoadFinalizeQueue.Clear();
    pendingExactSliceSupplementQueue.Clear();
    pendingTextureRegisterQueue.Clear();
    textureRefCounts.Clear();
    textureBytesById.Clear();
    loadCompleteLatencyRollingAvgMs = 0f;
    loadCompleteLatencyRollingCount = 0;
    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
    RecordPinStateIfEnabled();
  }

  public static int EvictAllUnpinnedCompleted(int maxEvictions = int.MaxValue) {
    var budget = Math.Max(maxEvictions, 1);
    var evicted = 0;
    while (evicted < budget && TryEvictOldestUnpinned()) {
      evicted++;
    }
    if (evicted > 0) {
      SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
      RecordPinStateIfEnabled();
    }
    return evicted;
  }


}
