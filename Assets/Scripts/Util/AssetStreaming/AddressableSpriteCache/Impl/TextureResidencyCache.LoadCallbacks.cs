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

          MarkInFlightStarted(entry);
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

          MarkInFlightStarted(entry);
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
          if (ShouldUseAtlasOwnerSubassetLoad(entry)) {
            entry.handle = Addressables.LoadAssetAsync<IList<Sprite>>(directLoadAddress);
            directLoadTraceMode = "atlas_all_subassets";
            directResourceLocationCount = Math.Max(directResourceLocationCount, 1);
            MaybeLogSlowLoadStart(
              phase: "load_all_sprite_subassets",
              address: directLoadAddress,
              startedAt: directLoadStartedAt,
              locationCount: directResourceLocationCount
            );
            ClearPendingAssetLoadStart(entry);

            MarkInFlightStarted(entry);
            RecordAssetTraceStart(
              entry,
              directLoadTraceMode,
              directResourceLocationCount,
              directExpectedSiblingSliceCount
            );

            entry.handle.Completed += assetOp => {
              CompleteDirectSpriteAssetLoad(
                entry,
                assetOp,
                directLoadTraceMode,
                directLoadAddress,
                directResourceLocationCount,
                directExpectedSiblingSliceCount
              );
            };

            processed++;
            continue;
          }
          entry.locationHandle = Addressables.LoadResourceLocationsAsync(
            directLoadAddress,
            typeof(Sprite)
          );
          MaybeLogSlowLoadStart(
            phase: "resolve_subasset_locations",
            address: directLoadAddress,
            startedAt: directLoadStartedAt,
            locationCount: 1
          );
          ClearPendingAssetLoadStart(entry);

          MarkInFlightStarted(entry);
          RecordAssetTraceStart(entry, directLoadTraceMode, directResourceLocationCount, directExpectedSiblingSliceCount);

          entry.locationHandle.Completed += locationOp => {
            CompleteDirectSpriteLocationResolve(
              entry,
              locationOp,
              directResourceLocationCount,
              directExpectedSiblingSliceCount
            );
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

        MarkInFlightStarted(entry);
        RecordAssetTraceStart(entry, "resolved_locations", resourceLocationCount, expectedSiblingSliceCount);

        entry.handle.Completed += assetOp => {
          CompleteDirectSpriteAssetLoad(
            entry,
            assetOp,
            "resolved_locations",
            entry.address,
            resourceLocationCount,
            expectedSiblingSliceCount
          );
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

  static void CompleteDirectSpriteAssetLoad(
    CacheEntry entry,
    AsyncOperationHandle<IList<Sprite>> assetOp,
    string loadPhase,
    string requestedAddress,
    int resourceLocationCount,
    int expectedSiblingSliceCount
  ) {
    if (entry == null) return;

    var diagnosticsEnabled = ShouldLogLoadCompletionDiagnostics();
    var callbackStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
    MarkInFlightComplete(entry);

    if (entry.queuedAtTicks > 0) {
      var latencyMs = (float)((DateTime.UtcNow.Ticks - entry.queuedAtTicks) * (1000.0 / TimeSpan.TicksPerSecond));
      RecordLoadCompleteLatency(latencyMs);
      entry.queuedAtTicks = 0;
    }

    var loadSucceeded = HasAnyLoadedSprite(assetOp.Result);
    if (!loadSucceeded) {
      LogSpriteLoadOperationFailureOnce(
        entry,
        loadPhase,
        requestedAddress,
        assetOp.Status,
        assetOp.OperationException
      );
    }

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

    EnqueuePendingLoadFinalize(
      entry,
      loadSucceeded,
      resourceLocationCount,
      expectedSiblingSliceCount
    );
    pendingQueueStateRecord = true;

    if (!diagnosticsEnabled) return;
    var callbackMs = ComputeElapsedMs(callbackStartedAt);
    if (callbackMs <= 0.1f) return;
    RecordLoadCompletionFrameCost(
      callbackMs,
      0f,
      0f,
      entry.address + " (callback_enqueue)"
    );
  }

  static void LogSpriteLoadOperationFailureOnce(
    CacheEntry entry,
    string loadPhase,
    string requestedAddress,
    AsyncOperationStatus status,
    Exception operationException
  ) {
    if (entry == null) return;

    var normalizedPhase = string.IsNullOrWhiteSpace(loadPhase)
      ? "unknown"
      : loadPhase.Trim();
    var warningKey = normalizedPhase + "|" + (entry.address ?? "");
    if (!spriteLoadOperationFailureWarnings.Add(warningKey)) return;

    var error = "Operation completed without a usable Sprite result.";
    if (operationException != null) {
      error = operationException.ToString();
    }

    Debug.LogWarning(
      "[TextureResidencyCache] Sprite load operation failed" +
      " phase='" + normalizedPhase + "'" +
      " owner='" + (entry.address ?? "") + "'" +
      " requested='" + (requestedAddress ?? "") + "'" +
      " status='" + status + "'" +
      " error='" + error + "'"
    );
    RecordAssetTraceFailure(entry, normalizedPhase + "_operation_failed");
  }

  static void CompleteDirectSpriteLocationResolve(
    CacheEntry entry,
    AsyncOperationHandle<IList<IResourceLocation>> locationOp,
    int resourceLocationCount,
    int expectedSiblingSliceCount
  ) {
    if (entry == null) return;

    var diagnosticsEnabled = ShouldLogLoadCompletionDiagnostics();
    var callbackStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;

    if (entry.isEvicted) {
      ReleaseLocationHandle(entry);
      MarkInFlightComplete(entry);
      ClearPendingLoadFinalize(entry);
      entry.loadStarted = false;
      SpriteStreamingDiagnostics.RecordAtlasLoadCompleted();
      pendingQueueStateRecord = true;
      if (diagnosticsEnabled) {
        var evictedMs = ComputeElapsedMs(callbackStartedAt);
        RecordLoadCompletionFrameCost(evictedMs, 0f, 0f, entry.address);
      }
      return;
    }

    var locations = locationOp.Result;
    var hasLocations =
      locationOp.Status == AsyncOperationStatus.Succeeded &&
      locations != null &&
      locations.Count > 0;

    if (!hasLocations) {
      FinalizeLoadFailure(
        entry,
        diagnosticsEnabled,
        callbackStartedAt
      );
      ReleaseLocationHandle(entry);
      return;
    }

    var resolvedLocationCount = Math.Max(
      resourceLocationCount,
      locations.Count
    );

    EnqueuePendingAssetLoadStart(
      entry,
      locations,
      resolvedLocationCount,
      expectedSiblingSliceCount
    );
    pendingQueueStateRecord = true;

    if (diagnosticsEnabled) {
      var callbackMs = ComputeElapsedMs(callbackStartedAt);
      if (callbackMs > 0.1f) {
        RecordLoadCompletionFrameCost(
          callbackMs,
          0f,
          0f,
          entry.address + " (location_resolve_enqueue)"
        );
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
  }


}
