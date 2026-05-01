#if false
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AddressableSpriteCacheAssetStreaming {

public static partial class TextureResidencyCache {

  static void StartGroupedGeneratedAtlasLoad(CacheEntry entry) {
    var metadataAddress = GeneratedAtlasBuildSurrogateUtility.BuildMetadataAssetPath(entry.address);
    entry.groupedAtlasTextureHandle = Addressables.LoadAssetAsync<Texture2D>(entry.address);
    entry.groupedMetadataHandle = Addressables.LoadAssetAsync<TextAsset>(metadataAddress);
    entry.countedInFlight = true;
    inFlightLoads++;
    SpriteStreamingDiagnostics.RecordLoadStarted();
    SpriteStreamingDiagnostics.RecordAtlasLoadStarted();
    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
    RecordAssetTraceStart(entry, "grouped_generated_atlas", 2, 0);
    AttachCompletionCallback(entry, "grouped_generated_atlas", entry.groupedAtlasTextureHandle, entry.groupedMetadataHandle);
  }

  static void StartMetadataDrivenAtlasLoad(CacheEntry entry) {
    var metadataAddress = GeneratedAtlasBuildSurrogateUtility.BuildMetadataAssetPath(entry.address);
    entry.metadataAtlasTextureHandle = Addressables.LoadAssetAsync<Texture2D>(entry.address);
    entry.metadataAtlasMetadataHandle = Addressables.LoadAssetAsync<TextAsset>(metadataAddress);
    entry.countedInFlight = true;
    inFlightLoads++;
    SpriteStreamingDiagnostics.RecordLoadStarted();
    SpriteStreamingDiagnostics.RecordAtlasLoadStarted();
    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
    RecordAssetTraceStart(entry, "metadata_synthesized_atlas", 2, 0);
    AttachCompletionCallback(entry, "metadata_synthesized_atlas", entry.metadataAtlasTextureHandle, entry.metadataAtlasMetadataHandle);
  }

  static void StartDirectAssetLoad(CacheEntry entry) {
    var loadAddress = ResolveDirectSpriteLoadAddress(entry);
    AsyncOperationHandle<Sprite> handle;
    var traceMode = "direct_subasset";
    
    if (ShouldUseAtlasOwnerSubassetLoad(entry) &&
        TryBuildDirectAtlasSliceLoadKeys(entry, out var keys, out var keyCount, out var siblingSliceCount)) {
      handle = Addressables.LoadAssetsAsync<Sprite>(keys, null, Addressables.MergeMode.Union, false);
      traceMode = "atlas_slice_keys";
    }
    else {
      handle = Addressables.LoadAssetsAsync<Sprite>(loadAddress, null, false);
    }
    
    entry.handle = handle;
    entry.countedInFlight = true;
    inFlightLoads++;
    SpriteStreamingDiagnostics.RecordLoadStarted();
    SpriteStreamingDiagnostics.RecordAtlasLoadStarted();
    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
    RecordAssetTraceStart(entry, traceMode, 1, 0);
    AttachCompletionCallback(entry, traceMode, entry.handle);
  }

  static void AttachCompletionCallback(CacheEntry entry, string loadMode, AsyncOperationHandle<Texture2D> handle1, AsyncOperationHandle<TextAsset> handle2) {
    var callback = CreateAtlasCompletionCallback(entry, loadMode);
    handle1.Completed += callback;
    handle2.Completed += callback;
  }

  static void AttachCompletionCallback(CacheEntry entry, string loadMode, AsyncOperationHandle<Sprite> handle) {
    handle.Completed += CreateDirectCompletionCallback(entry, loadMode);
  }

  static AsyncOperationHandleCompletedCallback CreateAtlasCompletionCallback(CacheEntry entry, string loadMode) {
    return _ => {
      var diagnosticsEnabled = ShouldLogLoadCompletionDiagnostics();
      var callbackStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
      var succeeded = TryCompleteAtlasLoad(entry, loadMode, diagnosticsEnabled, callbackStartedAt);
      if (!succeeded) {
        FinalizeLoadFailure(entry, diagnosticsEnabled, callbackStartedAt);
      }
    };
  }

  static AsyncOperationHandleCompletedCallback CreateDirectCompletionCallback(CacheEntry entry, string loadMode) {
    return assetOp => {
      var diagnosticsEnabled = ShouldLogLoadCompletionDiagnostics();
      var callbackStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
      MarkInFlightComplete(entry);
      RecordLoadLatencyIfQueued(entry);
      var loadSucceeded = HasAnyLoadedSprite(assetOp.Result);
      SpriteStreamingDiagnostics.RecordAtlasLoadCompleted();
      ClearActiveAssetLoadLocations(entry);
      
      if (entry.isEvicted) {
        ClearPendingLoadFinalize(entry);
        entry.loadStarted = false;
        pendingQueueStateRecord = true;
        if (diagnosticsEnabled && ComputeElapsedMs(callbackStartedAt) > 0.1f) {
          RecordLoadCompletionFrameCost(ComputeElapsedMs(callbackStartedAt), 0f, 0f, entry.address);
        }
        return;
      }
      
      EnqueuePendingLoadFinalize(entry, loadSucceeded, 1, 0);
      pendingQueueStateRecord = true;
      if (diagnosticsEnabled && ComputeElapsedMs(callbackStartedAt) > 0.1f) {
        RecordLoadCompletionFrameCost(ComputeElapsedMs(callbackStartedAt), 0f, 0f, entry.address + " (callback_enqueue)");
      }
    };
  }

  static bool TryCompleteAtlasLoad(CacheEntry entry, string loadMode, bool diagnosticsEnabled, float callbackStartedAt) {
    if (entry == null || entry.isEvicted) return false;
    ClearPendingAssetLoadStart(entry);
    MarkInFlightComplete(entry);
    RecordLoadLatencyIfQueued(entry);
    
    AsyncOperationHandle<Texture2D> texHandle;
    AsyncOperationHandle<TextAsset> metaHandle;
    
    if (loadMode == "grouped_generated_atlas") {
      texHandle = entry.groupedAtlasTextureHandle;
      metaHandle = entry.groupedMetadataHandle;
    }
    else {
      texHandle = entry.metadataAtlasTextureHandle;
      metaHandle = entry.metadataAtlasMetadataHandle;
    }
    
    if (!texHandle.IsValid() || !metaHandle.IsValid() || !texHandle.IsDone || !metaHandle.IsDone) return false;
    
    var loadSucceeded = texHandle.Status == AsyncOperationStatus.Succeeded && texHandle.Result != null &&
                       metaHandle.Status == AsyncOperationStatus.Succeeded && metaHandle.Result != null;
    if (!loadSucceeded) {
      LogAtlasSynthesisFailureOnce(loadMode, entry.address, GeneratedAtlasBuildSurrogateUtility.BuildMetadataAssetPath(entry.address),
        "texture_status=" + texHandle.Status + " metadata_status=" + metaHandle.Status);
    }
    
    SpriteStreamingDiagnostics.RecordAtlasLoadCompleted();
    ClearActiveAssetLoadLocations(entry);
    
    if (entry.isEvicted) {
      ClearPendingLoadFinalize(entry);
      entry.loadStarted = false;
      pendingQueueStateRecord = true;
      if (diagnosticsEnabled) {
        RecordLoadCompletionFrameCost(ComputeElapsedMs(callbackStartedAt), 0f, 0f, entry.address);
      }
      return false;
    }
    
    EnqueuePendingLoadFinalize(entry, loadSucceeded, 2, 0);
    pendingQueueStateRecord = true;
    if (diagnosticsEnabled && ComputeElapsedMs(callbackStartedAt) > 0.1f) {
      RecordLoadCompletionFrameCost(ComputeElapsedMs(callbackStartedAt), 0f, 0f, entry.address + " (atlas_callback_enqueue)");
    }
    return true;
  }

  static void RecordLoadLatencyIfQueued(CacheEntry entry) {
    if (entry.queuedAtTicks > 0) {
      var latencyMs = (float)((DateTime.UtcNow.Ticks - entry.queuedAtTicks) * (1000.0 / TimeSpan.TicksPerSecond));
      RecordLoadCompleteLatency(latencyMs);
      entry.queuedAtTicks = 0;
    }
  }

}
}
#endif

