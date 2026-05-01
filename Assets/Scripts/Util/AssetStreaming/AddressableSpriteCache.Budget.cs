#if false
using System.Collections.Generic;
using UnityEngine;

namespace AddressableSpriteCacheAssetStreaming {

public static partial class TextureResidencyCache {

  public static void MaintainBudget() {
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
    if (SpriteStreamingRuntimeSettings.KeepLoadedSpritesForSession) return Math.Max(hardBytes, 0);
    return Math.Max(softBytes, 0);
  }

  static bool TryEvictOldestUnpinned() {
    string oldestKey = null;
    CacheEntry oldestEntry = null;
    foreach (var pair in cache) {
      var candidate = pair.Value;
      if (candidate == null || candidate.pinCount > 0 || !candidate.isDone) continue;
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
      if (state != null && state.leases != null && state.leases.Count > 0) return true;
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
      if (state != null && state.pinClass == pinClass && state.leases.Count > 0) {
        ownerDemoteScratch.Add(state);
      }
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
        if (state.leases.TryGetValue(ownerReleaseAddressScratch[k], out var lease)) {
          lease?.Release();
          state.leases.Remove(ownerReleaseAddressScratch[k]);
          pinDemotions++;
          released++;
        }
      }
      if (state.leases.Count > 0) continue;
      ownerPins.Remove(state.ownerId);
      if (released >= maxReleases) break;
    }
    return released;
  }

  static void RegisterTextureContribution(CacheEntry entry) {
    if (entry == null || entry.hasTextureRegistration) return;
    if (!entry.isDone || !entry.isSuccess || entry.primarySprite == null) return;
    entry.registeredTextureIds.Clear();
    RegisterTextureForEntry(entry, entry.primarySprite);
    entry.hasTextureRegistration = entry.registeredTextureIds.Count > 0;
  }

  static void UnregisterTextureContribution(CacheEntry entry) {
    if (entry == null || !entry.hasTextureRegistration) return;
    entry.hasTextureRegistration = false;
    foreach (var textureId in entry.registeredTextureIds) {
      if (!textureRefCounts.TryGetValue(textureId, out var refs) || refs <= 0) continue;
      refs--;
      if (refs > 0) {
        textureRefCounts[textureId] = refs;
        continue;
      }
      textureRefCounts.Remove(textureId);
      if (textureBytesById.TryGetValue(textureId, out var bytes)) {
        textureBytesById.Remove(textureId);
        residentBytes -= bytes;
        if (residentBytes < 0) residentBytes = 0;
      }
    }
    entry.registeredTextureIds.Clear();
  }

  static void RegisterTextureForEntry(CacheEntry entry, Sprite sprite) {
    if (entry == null || sprite == null) return;
    var texture = sprite.texture;
    if (texture == null) return;
    var textureId = ObjectEntityId.GetRawValue(texture);
    if (!entry.registeredTextureIds.Add(textureId)) return;
    if (!textureRefCounts.TryGetValue(textureId, out var refs) || refs <= 0) {
      textureRefCounts[textureId] = 1;
      var bytes = EstimateTextureBytes(texture);
      textureBytesById[textureId] = bytes;
      residentBytes += bytes;
      return;
    }
    textureRefCounts[textureId] = refs + 1;
  }

  static long EstimateTextureBytes(Texture texture) {
    if (texture == null) return 0;
    var width = Math.Max(texture.width, 1);
    var height = Math.Max(texture.height, 1);
    return width * height * 4L;
  }

  static void Evict(string key, CacheEntry entry) {
    if (entry == null || entry.isEvicted) return;
    if (cache.TryGetValue(key, out var current) && !ReferenceEquals(current, entry)) return;
    if (entry.isQueued && sessionExpectedTotal > 0 && sessionGeneration > 0 && sessionTotalScheduled > 0) {
      sessionTotalScheduled--;
    }
    cache.Remove(key);
    RecordAssetTraceRelease(entry, "evict");
    entry.isEvicted = true;
    ClearQueuedFlag(entry);
    UnregisterTextureContribution(entry);
    ReleaseHandle(entry);
    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
  }

  static void ReleaseHandle(CacheEntry entry) {
    if (entry == null) return;
    MarkInFlightComplete(entry);
    GeneratedAtlasSpriteSynthesisUtility.DestroySprites(entry.generatedSprites);
    if (entry.handle.IsValid()) Addressables.Release(entry.handle);
    if (entry.groupedSingleSpriteHandle.IsValid()) Addressables.Release(entry.groupedSingleSpriteHandle);
    if (entry.groupedAtlasTextureHandle.IsValid()) Addressables.Release(entry.groupedAtlasTextureHandle);
    if (entry.groupedMetadataHandle.IsValid()) Addressables.Release(entry.groupedMetadataHandle);
    if (entry.metadataAtlasTextureHandle.IsValid()) Addressables.Release(entry.metadataAtlasTextureHandle);
    if (entry.metadataAtlasMetadataHandle.IsValid()) Addressables.Release(entry.metadataAtlasMetadataHandle);
    ReleaseLocationHandle(entry);
    entry.handle = default;
    entry.groupedSingleSpriteHandle = default;
    entry.groupedAtlasTextureHandle = default;
    entry.groupedMetadataHandle = default;
    entry.metadataAtlasTextureHandle = default;
    entry.metadataAtlasMetadataHandle = default;
    ClearPendingAssetLoadStart(entry);
    entry.pendingExactSliceSupplementAddresses.Clear();
    entry.failedExactSliceSupplementAddresses.Clear();
    entry.primarySprite = null;
    entry.spritesByName.Clear();
    entry.spriteMapMaterialized = false;
    entry.deferredSpriteMapMaterialization = false;
    entry.generatedSpriteSetComplete = false;
    entry.isDone = false;
    entry.isSuccess = false;
    entry.hasTextureRegistration = false;
    entry.registeredTextureIds.Clear();
    entry.loadStarted = false;
    entry.countedInFlight = false;
    entry.atlasFallbackToDirect = false;
    entry.atlasDirectFallbackAttempted = false;
    entry.requestedSpriteNameHint = "";
    entry.requestedSpriteNameConflict = false;
    ClearPendingLoadFinalize(entry);
    ClearQueuedFlag(entry);
  }

  static void ReleaseLocationHandle(CacheEntry entry) {
    if (entry == null || !entry.locationHandle.IsValid()) return;
    Addressables.Release(entry.locationHandle);
    entry.locationHandle = default;
  }

}
}
#endif

