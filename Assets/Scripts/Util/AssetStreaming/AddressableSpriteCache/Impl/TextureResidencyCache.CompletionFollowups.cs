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
  static void ProcessPendingCompletionFollowups() {
    if (!pendingBudgetMaintain && !pendingQueueStateRecord && pendingAssetLoadStartQueue.Count <= 0 && pendingLoadFinalizeQueue.Count <= 0 && pendingExactSliceSupplementQueue.Count <= 0 && pendingTextureRegisterQueue.Count <= 0
#if UNITY_EDITOR
      && pendingEditorAtlasSupplementQueue.Count <= 0
#endif
    ) return;
    var frame = Time.frameCount;
    if (completionFollowupFrame == frame) return;
    completionFollowupFrame = frame;
    var diagnosticsEnabled = ShouldLogLoadCompletionDiagnostics();
    var measureFollowupCost = diagnosticsEnabled || SpriteStreamingLoadingState.IsLoadingOverlayActive || IsCompletionFollowupDeadlineActive();
    var followupStartedAt = measureFollowupCost ? Time.realtimeSinceStartup : 0f;
    var followupDeadlineAt = ResolveCompletionFollowupDeadline(followupStartedAt);

    if (pendingAssetLoadStartQueue.Count > 0 && HasCompletionFollowupBudgetRemaining(followupDeadlineAt)) {
      ProcessPendingAssetLoadStarts(followupDeadlineAt);
    }

    if (pendingLoadFinalizeQueue.Count > 0 && HasCompletionFollowupBudgetRemaining(followupDeadlineAt)) {
      ProcessPendingLoadFinalizations(followupDeadlineAt);
    }

    if (pendingExactSliceSupplementQueue.Count > 0 && HasCompletionFollowupBudgetRemaining(followupDeadlineAt)) {
      ProcessPendingExactSliceSupplements(followupDeadlineAt);
    }

#if UNITY_EDITOR
    if (pendingEditorAtlasSupplementQueue.Count > 0 && HasCompletionFollowupBudgetRemaining(followupDeadlineAt)) {
      ProcessPendingEditorAtlasSupplements(followupDeadlineAt);
    }
#endif

    if (pendingTextureRegisterQueue.Count > 0 &&
        !ShouldDeferTextureRegistrationsForCurrentStreamingContext() &&
        HasCompletionFollowupBudgetRemaining(followupDeadlineAt)) {
      ProcessPendingTextureRegistrations(followupDeadlineAt);
    }

    var followupMs = measureFollowupCost ? ComputeElapsedMs(followupStartedAt) : 0f;
    if (measureFollowupCost) {
      if (pendingTextureRegisterQueue.Count > 0) {
        pendingBudgetMaintain = true;
      }
      UpdateCompletionPressureFromCosts(followupMs, 0f, 0f);
      if (diagnosticsEnabled && followupMs > 0.1f) {
        RecordLoadCompletionFrameCost(followupMs, 0f, 0f, "(completion_followups)");
      }
    }

    if (pendingBudgetMaintain) {
      pendingBudgetMaintain = false;
      MaintainBudget();
    }

    if (pendingQueueStateRecord) {
      pendingQueueStateRecord = false;
      SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
    }
  }

  static int ResolveCompletionRegisterBudgetPerFrame() {
    if (ShouldUseStrictSerialLoadingDebounce()) {
      return StrictSerialLoadingBudgetPerFrame;
    }
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      if (IsProtectedLoadingScreenStreamingContextActive()) {
        return CompletionRegisterProtectedOverlayBudgetPerFrame;
      }
      return CompletionRegisterOverlayBudgetPerFrame;
    }
    if (queuedEntryCount > 0 || inFlightLoads > 0 || deferredRequests.Count > 0) {
      return CompletionRegisterLoadingBudgetPerFrame;
    }
    return CompletionRegisterGameplayBudgetPerFrame;
  }

  static bool ShouldDeferTextureRegistrationsForCurrentStreamingContext() {
    return SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning;
  }

  static int ResolveCompletionFinalizeBudgetPerFrame() {
    if (ShouldUseStrictSerialLoadingDebounce()) {
      return StrictSerialLoadingBudgetPerFrame;
    }
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      return CompletionFinalizeOverlayBudgetPerFrame;
    }
    if (queuedEntryCount > 0 || inFlightLoads > 0 || deferredRequests.Count > 0) {
      return CompletionFinalizeLoadingBudgetPerFrame;
    }
    return CompletionFinalizeGameplayBudgetPerFrame;
  }

  static void ProcessPendingLoadFinalizations(float deadlineAt) {
    var budget = Math.Max(ResolveCompletionFinalizeBudgetPerFrame(), 1);
    var processed = 0;
    var deferred = 0;
    var diagnosticsEnabled = ShouldLogLoadCompletionDiagnostics();
    while (processed < budget && pendingLoadFinalizeQueue.Count > 0) {
      if (!HasCompletionFollowupBudgetRemaining(deadlineAt)) break;
      if (deferred >= pendingLoadFinalizeQueue.Count) break;
      var entry = pendingLoadFinalizeQueue.Dequeue();
      if (entry == null || !entry.pendingLoadFinalize) continue;
      if (entry.isEvicted) {
        ClearPendingLoadFinalize(entry);
        continue;
      }
      if (ShouldDeferPendingLoadFinalize(entry)) {
        pendingLoadFinalizeQueue.Enqueue(entry);
        deferred++;
        continue;
      }

      var finalizeStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
      var loadedSprites = ResolvePendingLoadFinalizeSprites(entry, out var loadSucceeded);
      if (!loadSucceeded &&
          TryQueueAtlasDirectFallback(
            entry,
            entry.pendingResourceLocationCount,
            entry.pendingExpectedSiblingSliceCount
          )) {
        if (diagnosticsEnabled) {
          var fallbackMs = ComputeElapsedMs(finalizeStartedAt);
          if (fallbackMs > 0.1f) {
            RecordLoadCompletionFrameCost(fallbackMs, 0f, 0f, entry.address + " (atlas_fallback_direct)");
          }
        }
        processed++;
        deferred = 0;
        continue;
      }

      entry.loadStarted = false;
      entry.isDone = true;
      entry.isSuccess = loadSucceeded;
      CaptureEntryResolvedSprites(entry, loadedSprites);
      entry.atlasFallbackToDirect = false;
      if (loadSucceeded &&
          string.Equals(entry.requestStrategy, "atlas_backed", StringComparison.Ordinal) &&
          string.Equals(ResolvePrimaryLoadMode(entry.address), "direct_subassets", StringComparison.Ordinal)) {
        entry.atlasFallbackToDirect = !HasLoadedAtlasSubassetSet(entry);
      }
      entry.lastAccessTicks = DateTime.UtcNow.Ticks;
      LogIncompleteAtlasSpriteMap(
        entry,
        entry.pendingExpectedSiblingSliceCount,
        entry.pendingResourceLocationCount,
        loadSucceeded && loadedSprites != null ? loadedSprites.Count : 0
      );
      RecordAssetTraceFinalize(entry, loadedSprites != null ? loadedSprites.Count : 0, loadSucceeded);

      if (entry.isSuccess) {
        EnqueuePendingTextureRegister(entry);
      }

      ClearPendingLoadFinalize(entry);
      pendingBudgetMaintain = true;
      pendingQueueStateRecord = true;
      if (diagnosticsEnabled) {
        var finalizeMs = ComputeElapsedMs(finalizeStartedAt);
        if (finalizeMs > 0.1f) {
          RecordLoadCompletionFrameCost(finalizeMs, 0f, 0f, entry.address + " (finalize)");
        }
      }
      processed++;
      deferred = 0;
    }
  }

  static IList<Sprite> ResolvePendingLoadFinalizeSprites(CacheEntry entry, out bool loadSucceeded) {
    loadSucceeded = false;
    if (entry == null) return null;
    if (entry.groupedAtlasTextureHandle.IsValid() || entry.groupedMetadataHandle.IsValid()) {
      if (!entry.pendingLoadSucceeded) return null;
      if (!entry.groupedAtlasTextureHandle.IsValid() || !entry.groupedMetadataHandle.IsValid()) {
        return null;
      }

      var metadataAddress = GeneratedAtlasBuildSurrogateUtility.BuildMetadataAssetPath(entry.address);
      var atlasTexture = entry.groupedAtlasTextureHandle.Result;
      if (atlasTexture == null) {
        LogAtlasSynthesisFailureOnce(
          "grouped_generated_atlas",
          entry.address,
          metadataAddress,
          "Grouped surrogate atlas load completed without a Texture2D result"
        );
        return null;
      }

      var metadataAsset = entry.groupedMetadataHandle.Result;
      if (!GeneratedAtlasSpriteSynthesisUtility.TryParseMetadata(metadataAsset, out entry.parsedGroupedMetadata) || entry.parsedGroupedMetadata == null) {
        LogAtlasSynthesisFailureOnce(
          "grouped_generated_atlas",
          entry.address,
          metadataAddress,
          "Failed to parse grouped metadata payload"
        );
        return null;
      }
      entry.groupedPayloadsByName = BuildSpritePayloadLookup(
        entry.parsedGroupedMetadata.sprites
      );

      GeneratedAtlasSpriteSynthesisUtility.AtlasSpriteImportPayload primaryPayload = null;
      if (!string.IsNullOrWhiteSpace(entry.requestedSpriteNameHint)) {
        var hintName = entry.requestedSpriteNameHint.Trim();
        primaryPayload = FindSpritePayloadByName(
          entry.groupedPayloadsByName,
          entry.parsedGroupedMetadata.sprites,
          hintName
        );
      }
      if (primaryPayload == null && entry.parsedGroupedMetadata.sprites.Count > 0) {
        primaryPayload = entry.parsedGroupedMetadata.sprites[0];
      }

      if (primaryPayload == null) {
        LogAtlasSynthesisFailureOnce(
          "grouped_generated_atlas",
          entry.address,
          metadataAddress,
          "Grouped metadata payload does not contain any sprites"
        );
        return null;
      }

      var pixelsPerUnit = entry.parsedGroupedMetadata.spritePixelsPerUnit > 0f ? entry.parsedGroupedMetadata.spritePixelsPerUnit : 100f;
      var meshType = GeneratedAtlasSpriteSynthesisUtility.ResolveMeshType(entry.parsedGroupedMetadata.spriteMeshType, SpriteMeshType.FullRect);
      var primarySprite = GeneratedAtlasSpriteSynthesisUtility.CreateSpriteFromPayload(atlasTexture, primaryPayload, pixelsPerUnit, meshType);
      if (primarySprite == null) {
        LogAtlasSynthesisFailureOnce(
          "grouped_generated_atlas",
          entry.address,
          metadataAddress,
          "Failed to synthesize primary sprite from payload"
        );
        return null;
      }

      entry.generatedSprites.Clear();
      entry.generatedSprites.Add(primarySprite);
      entry.generatedSpriteSetComplete = false;
      entry.deferredSpriteMapMaterialization = true;
      loadSucceeded = true;
      return entry.generatedSprites;
    }

    if (entry.metadataAtlasTextureHandle.IsValid() || entry.metadataAtlasMetadataHandle.IsValid()) {
      if (!entry.pendingLoadSucceeded) return null;
      if (!entry.metadataAtlasTextureHandle.IsValid() || !entry.metadataAtlasMetadataHandle.IsValid()) {
        return null;
      }

      var metadataAddress = GeneratedAtlasBuildSurrogateUtility.BuildMetadataAssetPath(entry.address);
      var atlasTexture = entry.metadataAtlasTextureHandle.Result;
      if (atlasTexture == null) {
        LogAtlasSynthesisFailureOnce(
          "metadata_synthesized_atlas",
          entry.address,
          metadataAddress,
          "Metadata-driven atlas load completed without a Texture2D result"
        );
        return null;
      }

      var metadataAsset = entry.metadataAtlasMetadataHandle.Result;
      if (!GeneratedAtlasSpriteSynthesisUtility.TryParseMetadata(metadataAsset, out entry.parsedMetadataAtlasMetadata) || entry.parsedMetadataAtlasMetadata == null) {
#if UNITY_EDITOR
        if (GeneratedAtlasSpriteSynthesisUtility.IsOffsetOnlyRuntimeMetadata(metadataAsset) &&
            TryLoadEditorImportedAtlasSprites(entry.address, out var importedSprites)) {
          LogOffsetOnlyMetadataFallbackOnce(entry.address, metadataAddress, importedSprites.Count);
          entry.generatedSpriteSetComplete = false;
          loadSucceeded = importedSprites.Count > 0;
          return importedSprites;
        }
#endif
        LogAtlasSynthesisFailureOnce(
          "metadata_synthesized_atlas",
          entry.address,
          metadataAddress,
          "Failed to parse metadata payload"
        );
        return null;
      }
      entry.metadataPayloadsByName = BuildSpritePayloadLookup(
        entry.parsedMetadataAtlasMetadata.sprites
      );

      GeneratedAtlasSpriteSynthesisUtility.AtlasSpriteImportPayload primaryPayload = null;
      if (!string.IsNullOrWhiteSpace(entry.requestedSpriteNameHint)) {
        var hintName = entry.requestedSpriteNameHint.Trim();
        primaryPayload = FindSpritePayloadByName(
          entry.metadataPayloadsByName,
          entry.parsedMetadataAtlasMetadata.sprites,
          hintName
        );
      }
      if (primaryPayload == null && entry.parsedMetadataAtlasMetadata.sprites.Count > 0) {
        primaryPayload = entry.parsedMetadataAtlasMetadata.sprites[0];
      }

      if (primaryPayload == null) {
        LogAtlasSynthesisFailureOnce(
          "metadata_synthesized_atlas",
          entry.address,
          metadataAddress,
          "Metadata payload does not contain any sprites"
        );
        return null;
      }

      var pixelsPerUnit = entry.parsedMetadataAtlasMetadata.spritePixelsPerUnit > 0f ? entry.parsedMetadataAtlasMetadata.spritePixelsPerUnit : 100f;
      var meshType = GeneratedAtlasSpriteSynthesisUtility.ResolveMeshType(entry.parsedMetadataAtlasMetadata.spriteMeshType, SpriteMeshType.FullRect);
      var primarySprite = GeneratedAtlasSpriteSynthesisUtility.CreateSpriteFromPayload(atlasTexture, primaryPayload, pixelsPerUnit, meshType);
      if (primarySprite == null) {
        LogAtlasSynthesisFailureOnce(
          "metadata_synthesized_atlas",
          entry.address,
          metadataAddress,
          "Failed to synthesize primary sprite from payload"
        );
        return null;
      }

      entry.generatedSprites.Clear();
      entry.generatedSprites.Add(primarySprite);
      entry.generatedSpriteSetComplete = false;
      entry.deferredSpriteMapMaterialization = true;
      loadSucceeded = true;
      return entry.generatedSprites;
    }

    if (!entry.pendingLoadSucceeded) return null;
    if (!entry.handle.IsValid()) return null;
    var loadedSprites = entry.handle.Result;
    if (loadedSprites == null || loadedSprites.Count <= 0) return null;
    entry.generatedSpriteSetComplete = false;
    loadSucceeded = true;
    return loadedSprites;
  }

  static void ProcessPendingTextureRegistrations(float deadlineAt) {
    var budget = Math.Max(ResolveCompletionRegisterBudgetPerFrame(), 1);
    var processed = 0;
    while (processed < budget && pendingTextureRegisterQueue.Count > 0) {
      if (!HasCompletionFollowupBudgetRemaining(deadlineAt)) break;
      var entry = pendingTextureRegisterQueue.Dequeue();
      if (entry == null || entry.isEvicted || entry.hasTextureRegistration) continue;
      if (!entry.isDone || !entry.isSuccess || entry.primarySprite == null) continue;
      RegisterTextureContribution(entry);
      RecordAssetTraceResident(entry);
      processed++;
    }
  }

  static void TryExpandAtlasOnSliceRequest(string requestedAddress, LoadPriority requestPriority, bool runPumpAndMaintain) {
    if (!SpriteStreamingRuntimeSettings.EnableAtlasExpansionOnSliceRequest) return;
    if (!Application.isPlaying) return;
    var loadingContextActive = StreamingWarmOrchestrator.IsWarmGateRunning || SpriteStreamingLoadingState.IsLoadingOverlayActive;
    if (!loadingContextActive) return;
    if (IsProtectedLoadingScreenStreamingContextActive()) {
      // Protected startup already has a curated warm plan. Expanding every slice
      // into atlas siblings here only grows deferred backlog and can force large
      // shard atlas-lookup builds onto the main thread during the overlay.
      return;
    }
    var allowLoadingExpansion = requestPriority != LoadPriority.Background;
    if (!allowLoadingExpansion) return;
    if (!TryConsumeAtlasExpansionBudget()) return;
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(requestedAddress, out var atlasAssetPath, out _)) return;

    var atlasKey = NormalizeAddress(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(atlasKey)) return;
    if (expandedAtlasKeys.Contains(atlasKey)) return;
    if (atlasExpansionRetryFrames.TryGetValue(atlasKey, out var nextRetryFrame) && Time.frameCount < nextRetryFrame) return;

    var configuredMaxSiblings = Math.Max(SpriteStreamingRuntimeSettings.AtlasExpansionMaxSiblingAddresses, 1);
    var maxSiblings = Math.Min(configuredMaxSiblings, AtlasExpansionHardSiblingCap);
    atlasSiblingAddressScratch.Clear();
    var hasSiblingMap = SpriteRuntimeResolver.TryCollectAtlasSiblingAddresses(requestedAddress, atlasSiblingAddressScratch, maxSiblings);
    if (!hasSiblingMap) {
      var retryWindowFrames = 1;
      atlasExpansionRetryFrames[atlasKey] = Time.frameCount + retryWindowFrames;
      SpriteStreamingDiagnostics.RecordAtlasExpansionFallback();
      return;
    }

    var siblingCount = atlasSiblingAddressScratch.Count;
    var queuedCount = 0;
    var expansionPriority = LoadPriority.Warmup;
    for (var i = 0; i < atlasSiblingAddressScratch.Count; i++) {
      var siblingAddress = string.IsNullOrWhiteSpace(atlasSiblingAddressScratch[i]) ? "" : atlasSiblingAddressScratch[i].Trim();
      if (string.IsNullOrWhiteSpace(siblingAddress)) continue;
      if (string.Equals(siblingAddress, requestedAddress, StringComparison.OrdinalIgnoreCase)) continue;
      if (!TryConsumeAtlasExpansionAddressBudget()) break;
      RecordRequestForFrame(isAcquire: false, sourceTag: "AtlasExpansion");
      var siblingEntry = ResolveEntryForLoad(NormalizeAddress(siblingAddress), out var siblingHit);
      TrackEntryRequestContext(siblingEntry, siblingAddress);
      TrackEntryRequestedSpriteHint(siblingEntry, siblingAddress);
      RecordLookup(siblingHit);
      // Loading-context expansion always queues as Warmup so first-play requests do not burst.
      QueueEntryForLoad(
        siblingEntry,
        expansionPriority,
        pinEntry: false,
        runPumpAndMaintain: false,
        warmGateManaged: false
      );
      queuedCount++;
    }

    expandedAtlasKeys.Add(atlasKey);
    atlasExpansionRetryFrames.Remove(atlasKey);
    SpriteStreamingDiagnostics.RecordAtlasExpansion(siblingCount, queuedCount);


    if (!runPumpAndMaintain) return;
    Pump();
    MaintainBudget();
  }


}
