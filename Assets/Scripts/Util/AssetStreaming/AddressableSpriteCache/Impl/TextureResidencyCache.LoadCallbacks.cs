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
  static void ProcessPendingAssetLoadStarts(float deadlineAt) {
    var budget = Math.Max(ResolvePendingAssetLoadStartBudgetPerFrame(), 1);
    var processed = 0;
    while (processed < budget && pendingAssetLoadStartQueue.Count > 0) {
      if (!HasCompletionFollowupBudgetRemaining(deadlineAt)) break;
      var entry = pendingAssetLoadStartQueue.Dequeue();
      if (entry == null || !entry.pendingAssetLoadStart) continue;

      if (entry.isEvicted) {
        ClearPendingAssetLoadStart(entry);
        ReleaseLocationHandle(entry);
        continue;
      }

      try {
        if (entry.pendingGroupedGeneratedAtlasLoad) {
          var groupedLoadStartedAt = ShouldMeasureLoadStartCosts() ? Time.realtimeSinceStartup : 0f;
          var metadataAddress = GeneratedAtlasBuildSurrogateUtility.BuildMetadataAssetPath(entry.address);
          entry.groupedAtlasTextureHandle = Addressables.LoadAssetAsync<Texture2D>(entry.address);
          entry.groupedMetadataHandle = Addressables.LoadAssetAsync<TextAsset>(metadataAddress);
          MaybeLogSlowLoadStart(
            phase: "load_grouped_surrogate",
            address: entry.address,
            startedAt: groupedLoadStartedAt,
            locationCount: 2
          );
          var groupedResourceLocationCount = entry.pendingAssetLoadResourceLocationCount;
          var groupedExpectedSiblingSliceCount = entry.pendingAssetLoadExpectedSiblingSliceCount;
          entry.pendingAssetLoadStart = false;

          entry.countedInFlight = true;
          inFlightLoads++;
          SpriteStreamingDiagnostics.RecordLoadStarted();
          SpriteStreamingDiagnostics.RecordAtlasLoadStarted();
          SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
          RecordAssetTraceStart(entry, "grouped_generated_atlas", groupedResourceLocationCount, groupedExpectedSiblingSliceCount);

          entry.groupedAtlasTextureHandle.Completed += _ => {
            var diagnosticsEnabled = ShouldLogLoadCompletionDiagnostics();
            var callbackStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
            TryCompleteGroupedGeneratedAtlasLoad(
              entry,
              groupedResourceLocationCount,
              groupedExpectedSiblingSliceCount,
              diagnosticsEnabled,
              callbackStartedAt
            );
          };

          entry.groupedMetadataHandle.Completed += _ => {
            var diagnosticsEnabled = ShouldLogLoadCompletionDiagnostics();
            var callbackStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
            TryCompleteGroupedGeneratedAtlasLoad(
              entry,
              groupedResourceLocationCount,
              groupedExpectedSiblingSliceCount,
              diagnosticsEnabled,
              callbackStartedAt
            );
          };

          processed++;
          continue;
        }

        if (entry.pendingMetadataDrivenAtlasLoad) {
          var metadataLoadStartedAt = ShouldMeasureLoadStartCosts() ? Time.realtimeSinceStartup : 0f;
          var metadataAddress = GeneratedAtlasBuildSurrogateUtility.BuildMetadataAssetPath(entry.address);
          entry.metadataAtlasTextureHandle = Addressables.LoadAssetAsync<Texture2D>(entry.address);
          entry.metadataAtlasMetadataHandle = Addressables.LoadAssetAsync<TextAsset>(metadataAddress);
          MaybeLogSlowLoadStart(
            phase: "load_metadata_synthesized_atlas",
            address: entry.address,
            startedAt: metadataLoadStartedAt,
            locationCount: 2
          );
          var metadataResourceLocationCount = entry.pendingAssetLoadResourceLocationCount;
          var metadataExpectedSiblingSliceCount = entry.pendingAssetLoadExpectedSiblingSliceCount;
          entry.pendingAssetLoadStart = false;

          entry.countedInFlight = true;
          inFlightLoads++;
          SpriteStreamingDiagnostics.RecordLoadStarted();
          SpriteStreamingDiagnostics.RecordAtlasLoadStarted();
          SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
          RecordAssetTraceStart(entry, "metadata_synthesized_atlas", metadataResourceLocationCount, metadataExpectedSiblingSliceCount);

          entry.metadataAtlasTextureHandle.Completed += _ => {
            var diagnosticsEnabled = ShouldLogLoadCompletionDiagnostics();
            var callbackStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
            TryCompleteMetadataDrivenAtlasLoad(
              entry,
              metadataResourceLocationCount,
              metadataExpectedSiblingSliceCount,
              diagnosticsEnabled,
              callbackStartedAt
            );
          };

          entry.metadataAtlasMetadataHandle.Completed += _ => {
            var diagnosticsEnabled = ShouldLogLoadCompletionDiagnostics();
            var callbackStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
            TryCompleteMetadataDrivenAtlasLoad(
              entry,
              metadataResourceLocationCount,
              metadataExpectedSiblingSliceCount,
              diagnosticsEnabled,
              callbackStartedAt
            );
          };

          processed++;
          continue;
        }

        if (entry.pendingDirectSubAssetLoad) {
          var directLoadStartedAt = ShouldMeasureLoadStartCosts() ? Time.realtimeSinceStartup : 0f;
          var directLoadTraceMode = "direct_subasset";
          var directLoadAddress = ResolveDirectSpriteLoadAddress(entry);
          var directResourceLocationCount = entry.pendingAssetLoadResourceLocationCount;
          var directExpectedSiblingSliceCount = entry.pendingAssetLoadExpectedSiblingSliceCount;
          if (ShouldLogAtlasNameDiagnostics(entry.address)) {
            Debug.Log(
              "[TextureResidencyCache] Direct sprite primary load" +
              " atlas='" + (entry.address ?? "") + "'" +
              " requested='" + (entry.lastRequestedAddress ?? "") + "'" +
              " load_address='" + (directLoadAddress ?? "") + "'" +
              " hint='" + (entry.requestedSpriteNameHint ?? "") + "'" +
              " conflict=" + (entry.requestedSpriteNameConflict ? 1 : 0)
            );
          }
          if (!ShouldUseAtlasOwnerSubassetLoad(entry) &&
              TryBuildDirectAtlasSliceLoadKeys(
            entry,
            out var directLoadKeys,
            out var directKeyCount,
            out var directSiblingSliceCount
          )) {
            entry.handle = Addressables.LoadAssetsAsync<Sprite>(
              directLoadKeys,
              null,
              Addressables.MergeMode.Union,
              false
            );
            directLoadTraceMode = "atlas_slice_keys";
            directResourceLocationCount = Math.Max(directKeyCount, 1);
            directExpectedSiblingSliceCount = Math.Max(directSiblingSliceCount, 0);
            MaybeLogSlowLoadStart(
              phase: "load_atlas_slice_keys",
              address: entry.address,
              startedAt: directLoadStartedAt,
              locationCount: directResourceLocationCount
            );
          }
          else {
            entry.handle = Addressables.LoadAssetsAsync<Sprite>(directLoadAddress, null, false);
            MaybeLogSlowLoadStart(
              phase: "load_subassets",
              address: directLoadAddress,
              startedAt: directLoadStartedAt,
              locationCount: 1
            );
          }
          ClearPendingAssetLoadStart(entry);

          entry.countedInFlight = true;
          inFlightLoads++;
          SpriteStreamingDiagnostics.RecordLoadStarted();
          SpriteStreamingDiagnostics.RecordAtlasLoadStarted();
          SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
          RecordAssetTraceStart(entry, directLoadTraceMode, directResourceLocationCount, directExpectedSiblingSliceCount);

          entry.handle.Completed += assetOp => {
            var diagnosticsEnabled = ShouldLogLoadCompletionDiagnostics();
            var callbackStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
            MarkInFlightComplete(entry);

            if (entry.queuedAtTicks > 0) {
              var latencyMs = (float)((DateTime.UtcNow.Ticks - entry.queuedAtTicks) * (1000.0 / TimeSpan.TicksPerSecond));
              RecordLoadCompleteLatency(latencyMs);
              entry.queuedAtTicks = 0;
            }

            var loadSucceeded = HasAnyLoadedSprite(assetOp.Result);
            SpriteStreamingDiagnostics.RecordAtlasLoadCompleted();
            ClearActiveAssetLoadLocations(entry);

            if (entry.isEvicted) {
              ClearPendingLoadFinalize(entry);
              entry.loadStarted = false;
              pendingQueueStateRecord = true;
              if (diagnosticsEnabled) {
                var evictedMs = ComputeElapsedMs(callbackStartedAt);
                RecordLoadCompletionFrameCost(evictedMs, 0f, 0f, entry.address);
              }
              return;
            }

            EnqueuePendingLoadFinalize(entry, loadSucceeded, directResourceLocationCount, directExpectedSiblingSliceCount);
            pendingQueueStateRecord = true;

            if (diagnosticsEnabled) {
              var callbackMs = ComputeElapsedMs(callbackStartedAt);
              if (callbackMs > 0.1f) {
                RecordLoadCompletionFrameCost(callbackMs, 0f, 0f, entry.address + " (callback_enqueue)");
              }
            }
          };

          processed++;
          continue;
        }

        if (entry.pendingAssetLoadLocations.Count <= 0) {
          ClearPendingAssetLoadStart(entry);
          FinalizeLoadFailure(entry, diagnosticsEnabled: ShouldLogLoadCompletionDiagnostics(), completionStartedAt: Time.realtimeSinceStartup);
          ReleaseLocationHandle(entry);
          continue;
        }

        var loadLocations = entry.activeAssetLoadLocations;
        loadLocations.Clear();
        for (var i = 0; i < entry.pendingAssetLoadLocations.Count; i++) {
          var location = entry.pendingAssetLoadLocations[i];
          if (location == null) continue;
          loadLocations.Add(location);
        }
        var loadStartStartedAt = ShouldMeasureLoadStartCosts() ? Time.realtimeSinceStartup : 0f;
        entry.handle = Addressables.LoadAssetsAsync<Sprite>(loadLocations, null, releaseDependenciesOnFailure: false);
        MaybeLogSlowLoadStart(
          phase: "load_assets",
          address: entry.address,
          startedAt: loadStartStartedAt,
          locationCount: entry.pendingAssetLoadResourceLocationCount
        );
        var resourceLocationCount = entry.pendingAssetLoadResourceLocationCount;
        var expectedSiblingSliceCount = entry.pendingAssetLoadExpectedSiblingSliceCount;
        ClearPendingAssetLoadStart(entry);
        ReleaseLocationHandle(entry);

        entry.countedInFlight = true;
        inFlightLoads++;
        SpriteStreamingDiagnostics.RecordLoadStarted();
        SpriteStreamingDiagnostics.RecordAtlasLoadStarted();
        SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
        RecordAssetTraceStart(entry, "resolved_locations", resourceLocationCount, expectedSiblingSliceCount);

        entry.handle.Completed += assetOp => {
          var diagnosticsEnabled = ShouldLogLoadCompletionDiagnostics();
          var callbackStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
          MarkInFlightComplete(entry);

          if (entry.queuedAtTicks > 0) {
            var latencyMs = (float)((DateTime.UtcNow.Ticks - entry.queuedAtTicks) * (1000.0 / TimeSpan.TicksPerSecond));
            RecordLoadCompleteLatency(latencyMs);
            entry.queuedAtTicks = 0;
          }

          var loadSucceeded = HasAnyLoadedSprite(assetOp.Result);
          SpriteStreamingDiagnostics.RecordAtlasLoadCompleted();
          ClearActiveAssetLoadLocations(entry);

          if (entry.isEvicted) {
            ClearPendingLoadFinalize(entry);
            entry.loadStarted = false;
            pendingQueueStateRecord = true;
            if (diagnosticsEnabled) {
              var evictedMs = ComputeElapsedMs(callbackStartedAt);
              RecordLoadCompletionFrameCost(evictedMs, 0f, 0f, entry.address);
            }
            return;
          }

          EnqueuePendingLoadFinalize(entry, loadSucceeded, resourceLocationCount, expectedSiblingSliceCount);
          pendingQueueStateRecord = true;

          if (diagnosticsEnabled) {
            var callbackMs = ComputeElapsedMs(callbackStartedAt);
            if (callbackMs > 0.1f) {
              RecordLoadCompletionFrameCost(callbackMs, 0f, 0f, entry.address + " (callback_enqueue)");
            }
          }
        };

        processed++;
      }
      catch (Exception ex) {
        Debug.LogWarning("[TextureResidencyCache] Synchronous exception during load start for address='" + entry.address + "': " + ex);
        ClearPendingAssetLoadStart(entry);
        FinalizeLoadFailure(entry, diagnosticsEnabled: ShouldLogLoadCompletionDiagnostics(), completionStartedAt: Time.realtimeSinceStartup);
        ReleaseLocationHandle(entry);
      }
    }
  }

  static void FinalizeLoadFailure(CacheEntry entry, bool diagnosticsEnabled, float completionStartedAt) {
    if (entry == null) return;
    MarkInFlightComplete(entry);
    ClearPendingAssetLoadStart(entry);
    ClearActiveAssetLoadLocations(entry);
    ClearPendingLoadFinalize(entry);
    entry.loadStarted = false;
    entry.isDone = true;
    entry.isSuccess = false;
    SpriteStreamingDiagnostics.RecordAtlasLoadCompleted();
    entry.primarySprite = null;
    entry.spritesByName.Clear();
    entry.spriteMapMaterialized = false;
    entry.deferredSpriteMapMaterialization = false;
    entry.generatedSpriteSetComplete = false;
    entry.requestedSpriteNameHint = "";
    entry.requestedSpriteNameConflict = false;
    GeneratedAtlasSpriteSynthesisUtility.DestroySprites(entry.generatedSprites);
    if (entry.queuedAtTicks > 0) {
      var latencyMs = (float)((DateTime.UtcNow.Ticks - entry.queuedAtTicks) * (1000.0 / TimeSpan.TicksPerSecond));
      RecordLoadCompleteLatency(latencyMs);
      entry.queuedAtTicks = 0;
    }
    RecordAssetTraceFailure(entry, "finalize_load_failure");
    pendingBudgetMaintain = true;
    pendingQueueStateRecord = true;
    if (diagnosticsEnabled) {
      var totalMs = ComputeElapsedMs(completionStartedAt);
      RecordLoadCompletionFrameCost(totalMs, 0f, 0f, entry.address);
    }
  }

  static Lease AcquireLease(CacheEntry entry) {
    var lease = pooledLeases.Count > 0
      ? pooledLeases.Pop()
      : new Lease();
    lease.Bind(entry);
    return lease;
  }

  static void ReturnLeaseToPool(Lease lease) {
    if (lease == null) return;
    if (pooledLeases.Count >= MaxPooledLeaseCount) return;
    pooledLeases.Push(lease);
  }

  static void ClearPendingLoadFinalize(CacheEntry entry) {
    if (entry == null) return;
    entry.pendingLoadFinalize = false;
    entry.pendingLoadSucceeded = false;
    entry.pendingResourceLocationCount = 0;
    entry.pendingExpectedSiblingSliceCount = 0;
  }

  static void ReleaseAtlasOnlyHandles(CacheEntry entry) {
    if (entry == null) return;
    GeneratedAtlasSpriteSynthesisUtility.DestroySprites(entry.generatedSprites);
    if (entry.groupedSingleSpriteHandle.IsValid()) {
      Addressables.Release(entry.groupedSingleSpriteHandle);
    }
    if (entry.groupedAtlasTextureHandle.IsValid()) {
      Addressables.Release(entry.groupedAtlasTextureHandle);
    }
    if (entry.groupedMetadataHandle.IsValid()) {
      Addressables.Release(entry.groupedMetadataHandle);
    }
    if (entry.metadataAtlasTextureHandle.IsValid()) {
      Addressables.Release(entry.metadataAtlasTextureHandle);
    }
    if (entry.metadataAtlasMetadataHandle.IsValid()) {
      Addressables.Release(entry.metadataAtlasMetadataHandle);
    }

    entry.groupedSingleSpriteHandle = default;
    entry.groupedAtlasTextureHandle = default;
    entry.groupedMetadataHandle = default;
    entry.metadataAtlasTextureHandle = default;
    entry.metadataAtlasMetadataHandle = default;
  }

  static bool TryQueueAtlasDirectFallback(
    CacheEntry entry,
    int resourceLocationCount,
    int expectedSiblingSliceCount
  ) {
    if (entry == null || entry.isEvicted) return false;
    if (entry.atlasDirectFallbackAttempted) return false;

    var primaryLoadMode = ResolvePrimaryLoadMode(entry.address);
    var atlasMode =
      string.Equals(primaryLoadMode, "metadata_synthesized_atlas", StringComparison.Ordinal) ||
      string.Equals(primaryLoadMode, "grouped_generated_atlas", StringComparison.Ordinal);
    if (!atlasMode) return false;

    entry.atlasDirectFallbackAttempted = true;
    entry.atlasFallbackToDirect = true;
    entry.requestStrategy = "atlas_fallback_direct";
    entry.loadStarted = false;
    entry.isDone = false;
    entry.isSuccess = false;
    entry.primarySprite = null;
    entry.spritesByName.Clear();
    entry.spriteMapMaterialized = false;
    entry.deferredSpriteMapMaterialization = false;
    ReleaseAtlasOnlyHandles(entry);
    ClearPendingLoadFinalize(entry);

    entry.pendingAssetLoadResourceLocationCount = Math.Max(resourceLocationCount, 1);
    entry.pendingAssetLoadExpectedSiblingSliceCount = Math.Max(expectedSiblingSliceCount, 0);
    EnqueuePendingDirectAssetLoadStart(entry);
    pendingQueueStateRecord = true;
    return true;
  }

  static void EnqueuePendingLoadFinalize(
    CacheEntry entry,
    bool loadSucceeded,
    int resourceLocationCount,
    int expectedSiblingSliceCount
  ) {
    if (entry == null || entry.isEvicted) return;
    entry.pendingLoadSucceeded = loadSucceeded;
    entry.pendingResourceLocationCount = Math.Max(resourceLocationCount, 0);
    entry.pendingExpectedSiblingSliceCount = Math.Max(expectedSiblingSliceCount, 0);
    if (entry.pendingLoadFinalize) return;
    entry.pendingLoadFinalize = true;
    pendingLoadFinalizeQueue.Enqueue(entry);
    pendingBudgetMaintain = true;
  }

  static void PopulateEntrySpriteMap(CacheEntry entry, IList<Sprite> loadedSprites) {
    if (entry == null) return;
    var preferredPrimary = entry.primarySprite;
    var preferredPrimaryName = preferredPrimary != null ? preferredPrimary.name : "";
    entry.spritesByName.Clear();
    entry.primarySprite = null;
    entry.spriteMapMaterialized = false;
    entry.deferredSpriteMapMaterialization = false;
    entry.editorAtlasSupplementPending = false;
    entry.editorAtlasSupplementAttempted = false;
    if (loadedSprites == null) return;
    Sprite fallbackPrimary = null;
    for (var i = 0; i < loadedSprites.Count; i++) {
      var sprite = loadedSprites[i];
      if (sprite == null) continue;
      if (fallbackPrimary == null) {
        fallbackPrimary = sprite;
      }
      if (entry.primarySprite == null &&
          preferredPrimary != null &&
          (ReferenceEquals(preferredPrimary, sprite) ||
           (!string.IsNullOrWhiteSpace(preferredPrimaryName) &&
            string.Equals(preferredPrimaryName, sprite.name, StringComparison.Ordinal)))) {
        entry.primarySprite = sprite;
      }
      if (string.IsNullOrWhiteSpace(sprite.name)) continue;
      entry.spritesByName[sprite.name] = sprite;
    }
    if (entry.primarySprite == null) {
      entry.primarySprite = fallbackPrimary;
    }
    var loadedSpriteCount = CountLoadedSprites(loadedSprites);
    entry.spriteMapMaterialized = ShouldMarkSpriteMapMaterialized(entry, loadedSpriteCount);
    entry.deferredSpriteMapMaterialization = !entry.spriteMapMaterialized && CanMaterializeEntrySpriteMapOnDemand(entry);
    LogAtlasSpriteMapBuildDiagnostics(entry, loadedSprites, "sprite_names", 0);
#if UNITY_EDITOR
    if (!IsGroupedGeneratedAtlasSurrogateAddress(entry.address)) {
      EnqueueEditorAtlasSpriteMapSupplement(entry, loadedSprites.Count);
    }
#endif
  }


}
