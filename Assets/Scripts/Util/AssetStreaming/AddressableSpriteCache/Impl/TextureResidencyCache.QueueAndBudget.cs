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
  static void ProcessPendingExactSliceSupplements(float deadlineAt) {
    var budget = Math.Max(ResolveExactSliceSupplementBudgetPerFrame(), 1);
    var processed = 0;
    while (processed < budget && pendingExactSliceSupplementQueue.Count > 0) {
      if (!HasCompletionFollowupBudgetRemaining(deadlineAt)) break;
      var request = pendingExactSliceSupplementQueue.Dequeue();
      var entry = request.entry;
      var sliceAddress = request.sliceAddress;
      if (entry == null || string.IsNullOrWhiteSpace(sliceAddress)) continue;
      if (!entry.pendingExactSliceSupplementAddresses.Contains(sliceAddress)) continue;
      if (entry.isEvicted || !entry.isDone || !entry.isSuccess) {
        entry.pendingExactSliceSupplementAddresses.Remove(sliceAddress);
        continue;
      }

      try {
        var loadStartedAt = ShouldMeasureLoadStartCosts() ? Time.realtimeSinceStartup : 0f;
        var handle = Addressables.LoadAssetAsync<Sprite>(sliceAddress);
        MaybeLogSlowLoadStart(
          phase: "exact_slice_supplement",
          address: sliceAddress,
          startedAt: loadStartedAt,
          locationCount: 1
        );
        handle.Completed += operation => {
          var retainHandle = false;
          if (entry != null) {
            entry.pendingExactSliceSupplementAddresses.Remove(sliceAddress);
            if (entry.isEvicted) {
              entry.failedExactSliceSupplementAddresses.Add(sliceAddress);
            }
            else if (operation.Status == AsyncOperationStatus.Succeeded && operation.Result != null) {
              var sprite = operation.Result;
              var hasRequestedSpriteName =
                SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out _, out var requestedSpriteName) &&
                !string.IsNullOrWhiteSpace(requestedSpriteName);
              if (hasRequestedSpriteName && !SpriteMatchesRequestedSlice(sprite, requestedSpriteName)) {
                entry.failedExactSliceSupplementAddresses.Add(sliceAddress);
                LogExactSliceSupplementMismatchOnce(entry, sliceAddress, requestedSpriteName, sprite);
                pendingQueueStateRecord = true;
                if (operation.IsValid()) {
                  Addressables.Release(operation);
                }
                return;
              }
              if (entry.primarySprite == null) {
                entry.primarySprite = sprite;
              }
              if (hasRequestedSpriteName) {
                RegisterSpriteMapKey(entry, requestedSpriteName, sprite);
              }
              if (!string.IsNullOrWhiteSpace(sprite.name)) {
                RegisterSpriteMapKey(entry, sprite.name, sprite);
              }
              if (operation.IsValid()) {
                entry.exactSliceSupplementHandles.Add(operation);
                retainHandle = true;
              }
              entry.failedExactSliceSupplementAddresses.Remove(sliceAddress);
              if (ShouldLogAtlasNameDiagnostics(entry.address)) {
                Debug.Log(
                  "[TextureResidencyCache] Exact slice supplement resolved" +
                  " requested='" + sliceAddress + "'" +
                  " sprite_name='" + (sprite.name ?? "") + "'" +
                  " mapped_keys=" + entry.spritesByName.Count
                );
              }
            }
            else {
              entry.failedExactSliceSupplementAddresses.Add(sliceAddress);
              LogSpriteLoadOperationFailureOnce(
                entry,
                "exact_slice_supplement",
                sliceAddress,
                operation.Status,
                operation.OperationException
              );
            }
            pendingQueueStateRecord = true;
          }

          if (!retainHandle && operation.IsValid()) {
            Addressables.Release(operation);
          }
        };
      }
      catch (Exception ex) {
        Debug.LogWarning("[TextureResidencyCache] Synchronous exception during exact slice supplement load start for address='" + sliceAddress + "': " + ex);
        entry.pendingExactSliceSupplementAddresses.Remove(sliceAddress);
        entry.failedExactSliceSupplementAddresses.Add(sliceAddress);
        pendingQueueStateRecord = true;
      }

      processed++;
    }
  }

  static void ReleaseLocationHandle(CacheEntry entry) {
    if (entry == null) return;
    if (!entry.locationHandle.IsValid()) return;
    Addressables.Release(entry.locationHandle);
    entry.locationHandle = default;
  }

  static Texture ResolveTraceTexture(CacheEntry entry) {
    if (entry == null || entry.primarySprite == null) return null;
    return entry.primarySprite.texture;
  }

  static string BuildTracePriorityDetail(LoadPriority priority) {
    return "priority=" + priority;
  }

  static string BuildTraceLoadDetail(CacheEntry entry, string loadMode, int resourceLocationCount, int expectedSiblingSliceCount) {
    return
      "load_mode=" + NormalizeAddress(loadMode) +
      " atlas_strategy=" + (entry != null && !string.IsNullOrWhiteSpace(entry.requestStrategy) ? entry.requestStrategy : "direct_only") +
      " queued_priority=" + entry.queuedPriority +
      " resource_locations=" + Math.Max(resourceLocationCount, 0) +
      " expected_siblings=" + Math.Max(expectedSiblingSliceCount, 0);
  }

  static string BuildTraceFinalizeDetail(CacheEntry entry, int loadedSpriteCount) {
    return
      "load_mode=" + ResolvePrimaryLoadMode(entry.address) +
      " atlas_strategy=" + (entry != null
        ? (entry.atlasFallbackToDirect ? "atlas_fallback_direct" : (string.IsNullOrWhiteSpace(entry.requestStrategy) ? "direct_only" : entry.requestStrategy))
        : "direct_only") +
      " queued_priority=" + entry.queuedPriority +
      " loaded_sprites=" + Math.Max(loadedSpriteCount, 0) +
      " resource_locations=" + Math.Max(entry.pendingResourceLocationCount, 0) +
      " expected_siblings=" + Math.Max(entry.pendingExpectedSiblingSliceCount, 0);
  }

  static string BuildTraceResidentDetail(CacheEntry entry) {
    return
      "load_mode=" + ResolvePrimaryLoadMode(entry.address) +
      " atlas_strategy=" + (entry != null
        ? (entry.atlasFallbackToDirect ? "atlas_fallback_direct" : (string.IsNullOrWhiteSpace(entry.requestStrategy) ? "direct_only" : entry.requestStrategy))
        : "direct_only") +
      " registered_textures=" + entry.registeredTextureIds.Count +
      " sprite_map_entries=" + entry.spritesByName.Count +
      " generated_sprite_set_complete=" + (entry.generatedSpriteSetComplete ? 1 : 0);
  }

  static string BuildTraceReleaseDetail(CacheEntry entry, string reason) {
    return
      "reason=" + NormalizeAddress(reason) +
      " load_mode=" + ResolvePrimaryLoadMode(entry.address) +
      " atlas_strategy=" + (entry != null
        ? (entry.atlasFallbackToDirect ? "atlas_fallback_direct" : (string.IsNullOrWhiteSpace(entry.requestStrategy) ? "direct_only" : entry.requestStrategy))
        : "direct_only") +
      " was_done=" + (entry.isDone ? 1 : 0) +
      " was_success=" + (entry.isSuccess ? 1 : 0) +
      " registered_textures=" + entry.registeredTextureIds.Count;
  }

  static void RecordAssetTraceQueue(CacheEntry entry, LoadPriority priority) {
    if (entry == null) return;
    AssetLoadTraceMonitor.RecordEvent(
      source: "TextureResidencyCache",
      stage: "queue",
      address: entry.address,
      assetTypeOverride: "SpriteStream",
      detail:
        BuildTracePriorityDetail(priority) +
        " load_mode=" + ResolvePrimaryLoadMode(entry.address) +
        " atlas_strategy=" + (string.IsNullOrWhiteSpace(entry.requestStrategy) ? "direct_only" : entry.requestStrategy)
    );
  }

  static void RecordAssetTraceStart(CacheEntry entry, string loadMode, int resourceLocationCount, int expectedSiblingSliceCount) {
    if (entry == null) return;
    AssetLoadTraceMonitor.RecordEvent(
      source: "TextureResidencyCache",
      stage: "start",
      address: entry.address,
      assetTypeOverride: "SpriteStream",
      detail: BuildTraceLoadDetail(entry, loadMode, resourceLocationCount, expectedSiblingSliceCount)
    );
  }

  static void RecordAssetTraceFinalize(CacheEntry entry, int loadedSpriteCount, bool loadSucceeded) {
    if (entry == null) return;
    var texture = ResolveTraceTexture(entry);
    AssetLoadTraceMonitor.RecordEvent(
      source: "TextureResidencyCache",
      stage: loadSucceeded ? "finalize" : "fail",
      address: entry.address,
      asset: texture,
      assetTypeOverride: texture != null ? "Texture2D" : "SpriteStream",
      detail: BuildTraceFinalizeDetail(entry, loadedSpriteCount),
      error: loadSucceeded ? "" : "sprite_load_failed"
    );
  }

  static void RecordAssetTraceFailure(CacheEntry entry, string reason) {
    if (entry == null) return;
    AssetLoadTraceMonitor.RecordEvent(
      source: "TextureResidencyCache",
      stage: "fail",
      address: entry.address,
      assetTypeOverride: "SpriteStream",
      detail:
        "load_mode=" + ResolvePrimaryLoadMode(entry.address) +
        " reason=" + NormalizeAddress(reason),
      error: NormalizeAddress(reason)
    );
  }

  static void RecordAssetTraceResident(CacheEntry entry) {
    if (entry == null) return;
    var texture = ResolveTraceTexture(entry);
    AssetLoadTraceMonitor.RecordEvent(
      source: "TextureResidencyCache",
      stage: "resident",
      address: entry.address,
      asset: texture,
      assetTypeOverride: texture != null ? "Texture2D" : "SpriteStream",
      detail: BuildTraceResidentDetail(entry)
    );
  }

  static void RecordAssetTraceRelease(CacheEntry entry, string reason) {
    if (entry == null) return;
    var texture = ResolveTraceTexture(entry);
    AssetLoadTraceMonitor.RecordEvent(
      source: "TextureResidencyCache",
      stage: "release",
      address: entry.address,
      asset: texture,
      assetTypeOverride: texture != null ? "Texture2D" : "SpriteStream",
      detail: BuildTraceReleaseDetail(entry, reason)
    );
  }

  static void EnqueueLoad(CacheEntry entry, LoadPriority priority) {
    if (entry == null || entry.isEvicted || entry.isDone || entry.loadStarted) return;

    if (!entry.isQueued) {
      entry.isQueued = true;
      entry.queuedPriority = priority;
      entry.queuedAtTicks = DateTime.UtcNow.Ticks;
      queuedEntryCount++;
      if (sessionExpectedTotal > 0 && sessionGeneration > 0) {
        sessionTotalScheduled++;
      }
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
      case LoadPriority.Immediate:
        immediateQueue.Enqueue(entry);
        break;
      case LoadPriority.Warmup:
        warmupQueue.Enqueue(entry);
        break;
      default:
        backgroundQueue.Enqueue(entry);
        break;
    }
  }

  static bool TryDequeueNext(out CacheEntry entry, out LoadPriority priority) {
    if (immediateQueue.Count > 0) {
      entry = immediateQueue.Dequeue();
      priority = LoadPriority.Immediate;
      return true;
    }
    if (warmupQueue.Count > 0) {
      entry = warmupQueue.Dequeue();
      priority = LoadPriority.Warmup;
      return true;
    }
    if (backgroundQueue.Count > 0) {
      entry = backgroundQueue.Dequeue();
      priority = LoadPriority.Background;
      return true;
    }

    entry = null;
    priority = LoadPriority.Background;
    return false;
  }

  static void ClearQueuedFlag(CacheEntry entry) {
    if (entry == null || !entry.isQueued) return;
    entry.isQueued = false;
    if (queuedEntryCount > 0) queuedEntryCount--;
  }

  static void MarkInFlightStarted(CacheEntry entry) {
    if (entry == null || entry.countedInFlight) return;
    entry.countedInFlight = true;
    inFlightLoads++;
    SpriteStreamingDiagnostics.RecordLoadStarted();
    SpriteStreamingDiagnostics.RecordAtlasLoadStarted();
    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
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

  static void EnqueuePendingTextureRegister(CacheEntry entry) {
    if (entry == null || entry.hasTextureRegistration) return;
    pendingTextureRegisterQueue.Enqueue(entry);
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
    if (queuedEntryCount != lastPumpSnapshotQueuedCount) return false;
    if (inFlightLoads != lastPumpSnapshotInFlightCount) return false;
    if (queuedEntryCount <= 0) return true;
    // Never skip when Immediate items are pending: they must not be starved by the frame budget.
    if (immediateQueue.Count > 0) return false;
    return startedLoadsThisFrame >= Math.Max(maxStarts, 1);
  }

  static void CachePumpFrameSnapshot() {
    lastPumpSnapshotFrame = Time.frameCount;
    lastPumpSnapshotQueuedCount = queuedEntryCount;
    lastPumpSnapshotInFlightCount = inFlightLoads;
  }

  static void ReleaseInternal(CacheEntry entry) {
    if (entry == null) return;
    if (entry.pinCount > 0) {
      entry.pinCount--;
    }
    entry.lastAccessTicks = DateTime.UtcNow.Ticks;
  }

  static void MaintainBudget() {
    if (ShouldDeferBudgetMaintenanceForLoadingPhase()) return;
    if (ownerPinMutationDepth > 0) return;
    var frame = Time.frameCount;
    if (frame == lastBudgetMaintainFrame) return;
    lastBudgetMaintainFrame = frame;

    var cfg = GetSettings();
    var softBytes = cfg.softTextureBudgetBytes;
    var hardBytes = cfg.hardTextureBudgetBytes;
    if (hardBytes < softBytes) hardBytes = softBytes;

    if (residentBytes <= hardBytes) return;

    var demoteBatchSize = Math.Max(SpriteStreamingRuntimeSettings.PinDemoteBatchSize, 1);
    var demotionPasses = 0;
    while (residentBytes > hardBytes && HasAnyPinnedAddresses() && demotionPasses < MaxBudgetDemotionPassesPerFrame) {
      if (!DemotePinsByPriority(demoteBatchSize)) break;
      demotionPasses++;
    }

    var targetBytes = ResolveBudgetEvictionTargetBytes(softBytes, hardBytes);
    var evictions = 0;
    while (residentBytes > targetBytes && evictions < MaxBudgetEvictionsPerFrame) {
      if (!TryEvictOldestUnpinned()) break;
      evictions++;
    }
  }

  static bool ShouldDeferBudgetMaintenanceForLoadingPhase() {
    if (!SpriteStreamingRuntimeSettings.KeepLoadedSpritesForSession) return false;
    return StreamingWarmOrchestrator.IsWarmGateRunning || SpriteStreamingLoadingState.IsLoadingOverlayActive;
  }

  static long ResolveBudgetEvictionTargetBytes(long softBytes, long hardBytes) {
    if (SpriteStreamingRuntimeSettings.KeepLoadedSpritesForSession) {
      // Keep richer residency when configured, but never exceed hard cap in runtime.
      return Math.Max(hardBytes, 0);
    }
    return Math.Max(softBytes, 0);
  }

  static bool TryEvictOldestUnpinned() {
    string oldestKey = null;
    CacheEntry oldestEntry = null;

    foreach (var pair in cache) {
      var candidate = pair.Value;
      if (candidate == null) continue;
      if (candidate.pinCount > 0) continue;
      if (!candidate.isDone) continue;
      if (oldestEntry == null || candidate.lastAccessTicks < oldestEntry.lastAccessTicks) {
        oldestEntry = candidate;
        oldestKey = pair.Key;
      }
    }

    if (oldestEntry == null || oldestKey == null) return false;
    Evict(oldestKey, oldestEntry);
    return true;
  }

  static bool HasAnyPinnedAddresses() {
    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state == null || state.leases == null || state.leases.Count <= 0) continue;
      return true;
    }

    return false;
  }

  static bool DemotePinsByPriority(int maxReleases) {
    var remaining = Math.Max(maxReleases, 1);
    var released = 0;

    released += DemotePinClass(PinClass.WarmGate, remaining - released);
    if (released >= remaining) return true;

    released += DemotePinClass(PinClass.Enemy, remaining - released);
    if (released >= remaining) return true;

    released += DemotePinClass(PinClass.UI, remaining - released);
    if (released >= remaining) return true;

    released += DemotePinClass(PinClass.Effect, remaining - released);
    if (released >= remaining) return true;

    released += DemotePinClass(PinClass.Player, remaining - released);
    return released > 0;
  }

  static int DemotePinClass(PinClass pinClass, int maxReleases) {
    if (maxReleases <= 0) return 0;

    ownerDemoteScratch.Clear();
    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state == null) continue;
      if (state.pinClass != pinClass) continue;
      if (state.leases.Count <= 0) continue;
      ownerDemoteScratch.Add(state);
    }

    if (ownerDemoteScratch.Count == 0) return 0;
    ownerDemoteScratch.Sort((left, right) => left.lastRefreshTicks.CompareTo(right.lastRefreshTicks));

    var released = 0;
    for (var i = 0; i < ownerDemoteScratch.Count; i++) {
      var state = ownerDemoteScratch[i];
      if (state == null || state.leases.Count <= 0) continue;

      ownerReleaseAddressScratch.Clear();
      foreach (var key in state.leases.Keys) ownerReleaseAddressScratch.Add(key);
      for (var k = 0; k < ownerReleaseAddressScratch.Count && released < maxReleases; k++) {
        if (!state.leases.TryGetValue(ownerReleaseAddressScratch[k], out var lease) || lease == null) continue;
        lease.Release();
        state.leases.Remove(ownerReleaseAddressScratch[k]);
        pinDemotions++;
        released++;
      }

      if (state.leases.Count > 0) continue;
      ownerPins.Remove(state.ownerId);
      if (released >= maxReleases) break;
    }

    return released;
  }


}
