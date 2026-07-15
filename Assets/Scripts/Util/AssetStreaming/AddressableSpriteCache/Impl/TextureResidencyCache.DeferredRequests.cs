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
  static void RecordPinStateIfEnabled() {
    if (!SpriteStreamingDiagnostics.Enabled) return;
    SpriteStreamingDiagnostics.RecordPinState(GetPinSnapshot());
  }

  static void TrackEntryRequestedSpriteHint(CacheEntry entry, string requestedAddress) {
    if (entry == null || string.IsNullOrWhiteSpace(requestedAddress)) return;
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(requestedAddress, out _, out var spriteName)) return;
    if (string.IsNullOrWhiteSpace(spriteName)) return;

    var normalizedSpriteName = spriteName.Trim();
    if (string.IsNullOrWhiteSpace(normalizedSpriteName)) return;
    if (string.IsNullOrWhiteSpace(entry.requestedSpriteNameHint)) {
      entry.requestedSpriteNameHint = normalizedSpriteName;
      return;
    }

    if (!string.Equals(entry.requestedSpriteNameHint, normalizedSpriteName, StringComparison.Ordinal)) {
      entry.requestedSpriteNameConflict = true;
    }
  }

  static bool ShouldUseProtectedPrimarySpriteOnly(CacheEntry entry, int loadedSpriteCount) {
    if (entry == null || loadedSpriteCount <= 1) return false;
    if (!IsProtectedLoadingScreenStreamingContextActive()) return false;
    return !ShouldForceProtectedSpriteMapMaterialization(entry);
  }

  static bool ShouldForceProtectedSpriteMapMaterialization(CacheEntry entry) {
    return IsEntryPinned(entry);
  }

  static bool IsEntryPinned(CacheEntry entry) {
    if (entry == null || string.IsNullOrWhiteSpace(entry.address)) return false;
    var normalizedAddress = NormalizeAddress(entry.address);
    if (string.IsNullOrWhiteSpace(normalizedAddress)) return false;

    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state == null) continue;
      if (state.leases == null || state.leases.Count <= 0) continue;
      if (state.leases.ContainsKey(normalizedAddress)) return true;
      foreach (var leaseKey in state.leases.Keys) {
        if (string.Equals(NormalizeAddress(leaseKey), normalizedAddress, StringComparison.OrdinalIgnoreCase)) return true;
      }
    }

    return false;
  }

  static bool IsEnvironmentAddress(string address) {
    return !string.IsNullOrWhiteSpace(address) &&
           address.IndexOf("/Environments/", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  static bool ShouldDeferPendingLoadFinalize(CacheEntry entry) {
    if (entry == null) return false;
    if (!IsProtectedLoadingScreenStreamingContextActive()) return false;
    if (!IsEnvironmentAddress(entry.address)) return false;
    return entry.queuedPriority != LoadPriority.Immediate;
  }

  static bool IsEntryPinnedByClass(CacheEntry entry, PinClass pinClass) {
    if (entry == null || string.IsNullOrWhiteSpace(entry.address)) return false;
    var normalizedAddress = NormalizeAddress(entry.address);
    if (string.IsNullOrWhiteSpace(normalizedAddress)) return false;

    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state == null) continue;
      if (state.pinClass != pinClass) continue;
      if (state.leases == null || state.leases.Count <= 0) continue;
      if (state.leases.ContainsKey(normalizedAddress)) return true;
      foreach (var leaseKey in state.leases.Keys) {
        if (string.Equals(NormalizeAddress(leaseKey), normalizedAddress, StringComparison.OrdinalIgnoreCase)) return true;
      }
    }

    return false;
  }

  static bool CanMaterializeEntrySpriteMapOnDemand(CacheEntry entry) {
    if (entry == null) return false;
    if (entry.handle.IsValid() && entry.handle.Result != null && entry.handle.Result.Count > 0) return true;
    if (entry.groupedAtlasTextureHandle.IsValid() && entry.groupedMetadataHandle.IsValid()) return true;
    if (entry.metadataAtlasTextureHandle.IsValid() && entry.metadataAtlasMetadataHandle.IsValid()) return true;
    return false;
  }

  static bool ShouldMarkSpriteMapMaterialized(CacheEntry entry, int loadedSpriteCount) {
    if (entry == null || entry.primarySprite == null) return false;
    if (!string.Equals(entry.requestStrategy, "atlas_backed", StringComparison.Ordinal)) return true;
    return loadedSpriteCount > 1;
  }

  static bool TryResolvePrimarySpriteFromLoadedSet(CacheEntry entry, IList<Sprite> loadedSprites, out Sprite sprite) {
    sprite = null;
    if (loadedSprites == null || loadedSprites.Count <= 0) return false;

    if (entry != null &&
        !entry.requestedSpriteNameConflict &&
        !string.IsNullOrWhiteSpace(entry.requestedSpriteNameHint)) {
      var requestedSpriteName = entry.requestedSpriteNameHint.Trim();
      for (var i = 0; i < loadedSprites.Count; i++) {
        var candidate = loadedSprites[i];
        if (candidate == null || string.IsNullOrWhiteSpace(candidate.name)) continue;
        if (!string.Equals(candidate.name.Trim(), requestedSpriteName, StringComparison.Ordinal)) continue;
        sprite = candidate;
        return true;
      }
    }

    return TryGetFirstLoadedSprite(loadedSprites, out sprite);
  }

  static void CaptureEntryResolvedSprites(CacheEntry entry, IList<Sprite> loadedSprites) {
    if (entry == null) return;
    entry.spritesByName.Clear();
    entry.primarySprite = null;
    entry.spriteMapMaterialized = false;
    entry.deferredSpriteMapMaterialization = false;
    entry.editorAtlasSupplementPending = false;
    entry.editorAtlasSupplementAttempted = false;
    if (!TryResolvePrimarySpriteFromLoadedSet(entry, loadedSprites, out var primarySprite) || primarySprite == null) {
      return;
    }

    entry.primarySprite = primarySprite;
    if (!string.IsNullOrWhiteSpace(primarySprite.name)) {
      entry.spritesByName[primarySprite.name] = primarySprite;
    }

    var loadedSpriteCount = CountLoadedSprites(loadedSprites);
    if (loadedSpriteCount <= 1) {
      entry.spriteMapMaterialized = ShouldMarkSpriteMapMaterialized(entry, loadedSpriteCount);
      entry.deferredSpriteMapMaterialization = !entry.spriteMapMaterialized && CanMaterializeEntrySpriteMapOnDemand(entry);
      if (!entry.spriteMapMaterialized && ShouldLogAtlasNameDiagnostics(entry.address)) {
        RuntimeLog.Log(
          "[TextureResidencyCache] Atlas entry kept in pending map state" +
          " atlas='" + (entry.address ?? "") + "'" +
          " requested='" + (entry.lastRequestedAddress ?? "") + "'" +
          " primary='" + (entry.primarySprite != null ? entry.primarySprite.name : "") + "'" +
          " loaded_count=" + loadedSpriteCount
        );
      }
      return;
    }

    if (ShouldUseProtectedPrimarySpriteOnly(entry, loadedSpriteCount)) {
      entry.deferredSpriteMapMaterialization = CanMaterializeEntrySpriteMapOnDemand(entry);
      return;
    }

    PopulateEntrySpriteMap(entry, loadedSprites);
    entry.spriteMapMaterialized = true;
  }

  static void DestroyGeneratedSprite(Sprite sprite) {
    if (sprite == null) return;
    if (Application.isPlaying) {
      UnityEngine.Object.Destroy(sprite);
      return;
    }
    UnityEngine.Object.DestroyImmediate(sprite);
  }

  static void MergeMaterializedGeneratedSprites(CacheEntry entry, List<Sprite> materializedSprites) {
    if (materializedSprites == null || materializedSprites.Count <= 0) return;
    if (entry == null) {
      GeneratedAtlasSpriteSynthesisUtility.DestroySprites(materializedSprites);
      return;
    }

    var primaryName = entry.primarySprite != null ? entry.primarySprite.name : "";
    for (var i = 0; i < materializedSprites.Count; i++) {
      var sprite = materializedSprites[i];
      if (sprite == null) continue;
      if (!string.IsNullOrWhiteSpace(primaryName) &&
          string.Equals(primaryName, sprite.name, StringComparison.Ordinal)) {
        DestroyGeneratedSprite(sprite);
        continue;
      }
      entry.generatedSprites.Add(sprite);
    }
    materializedSprites.Clear();
  }

  static bool TryMaterializeDeferredGeneratedSpriteMap(CacheEntry entry) {
    if (entry == null) return false;
    if (entry.generatedSpriteSetComplete && entry.generatedSprites.Count > 0) {
      return true;
    }

    if (entry.groupedAtlasTextureHandle.IsValid() && entry.groupedMetadataHandle.IsValid()) {
      var atlasTexture = entry.groupedAtlasTextureHandle.Result;
      var metadataAsset = entry.groupedMetadataHandle.Result;
      if (atlasTexture == null || metadataAsset == null) return false;
      if (!GeneratedAtlasSpriteSynthesisUtility.TryCreateGroupedSurrogateSprites(atlasTexture, metadataAsset, out var groupedSprites)) {
        return false;
      }
      MergeMaterializedGeneratedSprites(entry, groupedSprites);
      entry.generatedSpriteSetComplete = entry.generatedSprites.Count > 0;
      return entry.generatedSpriteSetComplete;
    }

    if (entry.metadataAtlasTextureHandle.IsValid() && entry.metadataAtlasMetadataHandle.IsValid()) {
      var atlasTexture = entry.metadataAtlasTextureHandle.Result;
      var metadataAsset = entry.metadataAtlasMetadataHandle.Result;
      if (atlasTexture == null || metadataAsset == null) return false;
      if (!GeneratedAtlasSpriteSynthesisUtility.TryCreateSpritesFromMetadata(
        atlasTexture,
        fallbackPixelsPerUnit: 100f,
        fallbackMeshType: SpriteMeshType.FullRect,
        metadataAsset,
        out var generatedSprites,
        out _)) {
        return false;
      }
      MergeMaterializedGeneratedSprites(entry, generatedSprites);
      entry.generatedSpriteSetComplete = entry.generatedSprites.Count > 0;
      return entry.generatedSpriteSetComplete;
    }

    return entry.generatedSpriteSetComplete && entry.generatedSprites.Count > 0;
  }

  static bool TryEnsureEntrySpriteMapMaterialized(CacheEntry entry) {
    if (entry == null || !entry.isDone || !entry.isSuccess || entry.primarySprite == null) return false;
    if (entry.spriteMapMaterialized) return true;
    if (!entry.deferredSpriteMapMaterialization) return false;

    if (entry.groupedAtlasTextureHandle.IsValid() || entry.groupedMetadataHandle.IsValid() ||
        entry.metadataAtlasTextureHandle.IsValid() || entry.metadataAtlasMetadataHandle.IsValid()) {
      if (!TryMaterializeDeferredGeneratedSpriteMap(entry)) return false;
      PopulateEntrySpriteMap(entry, entry.generatedSprites);
      return entry.spriteMapMaterialized && entry.spritesByName.Count > 0;
    }

    if (!entry.handle.IsValid() || entry.handle.Result == null || entry.handle.Result.Count <= 0) return false;
    PopulateEntrySpriteMap(entry, entry.handle.Result);
    return entry.spriteMapMaterialized && entry.spritesByName.Count > 0;
  }

  static CacheEntry ResolveEntryForLoad(string normalizedAddress, out bool hit) {
    hit = false;
    if (!cache.TryGetValue(normalizedAddress, out var entry)) {
      entry = CreateEntry(normalizedAddress);
      cache[normalizedAddress] = entry;
      RecordNewEntryForFrame();
      return entry;
    }

    if (entry.isDone && !entry.isSuccess) {
      Evict(normalizedAddress, entry);
      entry = CreateEntry(normalizedAddress);
      cache[normalizedAddress] = entry;
      RecordNewEntryForFrame();
      return entry;
    }

    if (entry.isDone && entry.isSuccess && entry.primarySprite != null) {
      hit = true;
    }
    return entry;
  }

  static void RecordLookup(bool hit) {
    if (hit) cacheHits++;
    else cacheMisses++;
    SpriteStreamingDiagnostics.RecordCacheLookup(hit);
    SpriteStreamingDiagnostics.RecordAtlasCacheLookup(hit);
  }

  static void RecordGameplayColdAtlasMiss(string normalizedAddress, bool hit) {
    if (hit || string.IsNullOrWhiteSpace(normalizedAddress)) return;
    if (StreamingWarmOrchestrator.IsWarmGateRunning || SpriteStreamingLoadingState.IsLoadingOverlayActive) return;
    if (!gameplayColdMissAtlasKeys.Add(normalizedAddress)) return;
    SpriteStreamingDiagnostics.RecordGameplayColdAtlasMiss();
  }

  static void QueueEntryForLoad(
    CacheEntry entry,
    LoadPriority priority,
    bool pinEntry,
    bool runPumpAndMaintain,
    bool warmGateManaged,
    string sourceTag = null
  ) {
    if (entry == null) return;
    // If the entry is already done (cached), we count it towards the session progress immediately.
    if (entry.isDone) {
      MarkSessionEntryCompleted(entry);
    }

    entry.lastAccessTicks = frameAccessTicks != 0 ? frameAccessTicks : DateTime.UtcNow.Ticks;
    if (!string.IsNullOrEmpty(sourceTag)) {
      entry.sourceTag = sourceTag;
    }
    if (warmGateManaged &&
        TryPromoteDeferredRequest(entry.address, priority, out var promotedPriority, out _, out var promotedSourceTag)) {
      priority = promotedPriority;
      if (!string.IsNullOrEmpty(promotedSourceTag)) {
        sourceTag = promotedSourceTag;
        entry.sourceTag = promotedSourceTag;
      }
    }

    if (pinEntry) entry.pinCount++;

    if (!warmGateManaged && ShouldDeferNonManagedWarmGateRequest(entry, priority, pinEntry)) {
      EnqueueDeferredRequest(entry.address, priority, pinEntry, sourceTag);
      if (!runPumpAndMaintain) return;
      Pump();
      return;
    }

    EnqueueLoad(entry, priority);
    if (!runPumpAndMaintain) return;
    Pump();
    MaintainBudget();
  }

  static bool ShouldDeferNonManagedWarmGateRequest(CacheEntry entry, LoadPriority priority, bool pinEntry) {
    if (entry == null) return false;
    if (entry.isDone || entry.loadStarted || entry.isEvicted || entry.isQueued) return false;
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive) return false;
    // Only defer while warm gate is actively running. Once it ends, pre-unlock
    // controls drain rate via queue thresholds and flush budgets prevent
    // one-frame explosions. Deferring longer creates a deadlock: pre-unlock
    // waits for outstanding to drop, outstanding includes deferred_pending,
    // and deferred never flushes while the protected overlay is still up.
    return StreamingWarmOrchestrator.IsWarmGateRunning;
  }

  static void EnqueueDeferredRequest(string normalizedAddress, LoadPriority priority, bool pinEntry, string sourceTag = null) {
    if (string.IsNullOrWhiteSpace(normalizedAddress)) return;
    deferredRequestCount++;
    if (!deferredRequests.TryGetValue(normalizedAddress, out var state)) {
      state = new DeferredRequestState {
        priority = priority,
        pinEntry = pinEntry,
        sourceTag = sourceTag
      };
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
    if (!string.IsNullOrEmpty(sourceTag)) {
      state.sourceTag = sourceTag;
    }
    deferredRequests[normalizedAddress] = state;

    if (priorityChanged) {
      EnqueueDeferredByPriority(normalizedAddress, mergedPriority);
    }
  }

  static bool TryPromoteDeferredRequest(
    string normalizedAddress,
    LoadPriority requestedPriority,
    out LoadPriority effectivePriority,
    out bool deferredPinEntry,
    out string deferredSourceTag
  ) {
    effectivePriority = requestedPriority;
    deferredPinEntry = false;
    deferredSourceTag = null;
    if (string.IsNullOrWhiteSpace(normalizedAddress)) return false;
    if (!deferredRequests.TryGetValue(normalizedAddress, out var deferredState)) return false;

    deferredRequests.Remove(normalizedAddress);
    deferredPromotedCount++;
    if (deferredState.priority < effectivePriority) {
      effectivePriority = deferredState.priority;
    }
    deferredPinEntry = deferredState.pinEntry;
    deferredSourceTag = deferredState.sourceTag;
    return true;
  }

  static void EnqueueDeferredByPriority(string normalizedAddress, LoadPriority priority) {
    switch (priority) {
      case LoadPriority.Immediate:
        deferredImmediateQueue.Enqueue(normalizedAddress);
        break;
      case LoadPriority.Warmup:
        deferredWarmupQueue.Enqueue(normalizedAddress);
        break;
      default:
        deferredBackgroundQueue.Enqueue(normalizedAddress);
        break;
    }
  }

  static bool TryDequeueDeferredRequest(out string normalizedAddress, out LoadPriority sourcePriority) {
    if (deferredImmediateQueue.Count > 0) {
      normalizedAddress = deferredImmediateQueue.Dequeue();
      sourcePriority = LoadPriority.Immediate;
      return true;
    }
    if (deferredWarmupQueue.Count > 0) {
      normalizedAddress = deferredWarmupQueue.Dequeue();
      sourcePriority = LoadPriority.Warmup;
      return true;
    }
    if (!IsProtectedLoadingScreenStreamingContextActive() && deferredBackgroundQueue.Count > 0) {
      normalizedAddress = deferredBackgroundQueue.Dequeue();
      sourcePriority = LoadPriority.Background;
      return true;
    }

    normalizedAddress = "";
    sourcePriority = LoadPriority.Background;
    return false;
  }

  static void FlushDeferredRequestsIntoMainQueues() {
    if (StreamingWarmOrchestrator.IsWarmGateRunning) return;
    if (IsProtectedLoadingScreenStreamingContextActive()) return;
    // Deferred requests are long-tail work. First-frame readiness ignores this
    // count, so keep it deferred while the protected overlay is active.
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
    if (ShouldUseStrictSerialLoadingDebounce()) {
      flushBudget = StrictSerialLoadingBudgetPerFrame;
    }
    if (queuedEntryCount >= 512 || inFlightLoads >= 64) {
      flushBudget = Math.Min(flushBudget, DeferredFlushPressureBudgetPerFrame);
    }
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
      if (!string.IsNullOrEmpty(deferredState.sourceTag)) {
        entry.sourceTag = deferredState.sourceTag;
      }
      EnqueueLoad(entry, deferredState.priority);
      deferredFlushedThisFrame++;
    }
  }


}
