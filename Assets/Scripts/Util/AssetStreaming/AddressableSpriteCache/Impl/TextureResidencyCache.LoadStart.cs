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
  static CacheEntry CreateEntry(string address) {
    return new CacheEntry {
      address = address,
      pinCount = 0,
      isDone = false,
      isSuccess = false,
      isEvicted = false,
      lastAccessTicks = DateTime.UtcNow.Ticks,
      loadStarted = false,
      countedInFlight = false,
      isQueued = false,
      queuedPriority = LoadPriority.Background
    };
  }

  static void EnsureLoadCollections(CacheEntry entry) {
    if (entry == null) return;

    if (entry.pendingAssetLoadLocations == null) {
      entry.pendingAssetLoadLocations = new List<IResourceLocation>(4);
    }
    if (entry.activeAssetLoadLocations == null) {
      entry.activeAssetLoadLocations = new List<IResourceLocation>(4);
    }
    if (entry.spritesByName == null) {
      entry.spritesByName = new Dictionary<string, Sprite>(StringComparer.Ordinal);
    }
    if (entry.generatedSprites == null) {
      entry.generatedSprites = new List<Sprite>();
    }
    if (entry.registeredTextureIds == null) {
      entry.registeredTextureIds = new HashSet<ulong>();
    }
  }

  static void EnsureExactSliceSupplementCollections(CacheEntry entry) {
    if (entry == null) return;

    if (entry.exactSliceSupplementHandles == null) {
      entry.exactSliceSupplementHandles = new List<AsyncOperationHandle<Sprite>>();
    }
    if (entry.pendingExactSliceSupplementAddresses == null) {
      entry.pendingExactSliceSupplementAddresses = new HashSet<string>(StringComparer.Ordinal);
    }
    if (entry.failedExactSliceSupplementAddresses == null) {
      entry.failedExactSliceSupplementAddresses = new HashSet<string>(StringComparer.Ordinal);
    }
  }

  static void StartLoad(CacheEntry entry) {
    if (entry == null || entry.isEvicted || entry.loadStarted || entry.isDone) return;

    EnsureLoadCollections(entry);
    entry.loadStarted = true;
    entry.isDone = false;
    entry.isSuccess = false;
    entry.primarySprite = null;
    entry.spritesByName.Clear();
    entry.spriteMapMaterialized = false;
    entry.deferredSpriteMapMaterialization = false;
    entry.generatedSpriteSetComplete = false;
    GeneratedAtlasSpriteSynthesisUtility.DestroySprites(entry.generatedSprites);
    if (entry.pendingExactSliceSupplementAddresses != null) {
      entry.pendingExactSliceSupplementAddresses.Clear();
    }
    if (entry.failedExactSliceSupplementAddresses != null) {
      entry.failedExactSliceSupplementAddresses.Clear();
    }
    entry.editorAtlasSupplementAttempted = false;
    entry.activeAssetLoadLocations.Clear();
    entry.lastAccessTicks = DateTime.UtcNow.Ticks;
    entry.atlasFallbackToDirect = false;
    entry.atlasDirectFallbackAttempted = false;
    var primaryLoadMode = ResolvePrimaryLoadMode(entry.address);
    if (ShouldLogRequestFrameDiagnostics()) {
      RuntimeLog.Log(
        "[TextureResidencyCache][PrimaryLoad] mode=" + primaryLoadMode +
        " address='" + entry.address + "'" +
        " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
        " warm_gate_running=" + (StreamingWarmOrchestrator.IsWarmGateRunning ? 1 : 0)
      );
    }
    if (string.Equals(primaryLoadMode, "grouped_generated_atlas", StringComparison.Ordinal)) {
      EnqueuePendingGroupedGeneratedAtlasLoadStart(entry);
    }
    else if (string.Equals(primaryLoadMode, "metadata_synthesized_atlas", StringComparison.Ordinal)) {
      EnqueuePendingMetadataDrivenAtlasLoadStart(entry);
    }
    else {
      EnqueuePendingDirectAssetLoadStart(entry);
    }
    pendingQueueStateRecord = true;
  }

  static bool IsGroupedGeneratedAtlasSurrogateAddress(string address) {
    var normalizedAddress = NormalizeRawAddress(address);
    return GeneratedAtlasBuildSurrogateUtility.IsBuildSurrogatePath(normalizedAddress);
  }

  static bool ShouldUseMetadataDrivenAtlasLoad(string address) {
    // Runtime atlas .json payloads are placement-only offset data. They are not
    // authoritative slice definitions and should never force a metadata-driven
    // atlas synthesis path for non-grouped atlas loads.
    return false;
  }

  static string ResolvePrimaryLoadMode(string address) {
    if (IsGroupedGeneratedAtlasSurrogateAddress(address)) return "grouped_generated_atlas";
    if (ShouldUseMetadataDrivenAtlasLoad(address)) return "metadata_synthesized_atlas";
    return "direct_subassets";
  }

  static bool SupportsAtlasOwnedRequestPath(string atlasAddress) {
    var normalizedAtlasAddress = NormalizeRawAddress(atlasAddress);
    if (string.IsNullOrWhiteSpace(normalizedAtlasAddress)) return false;
    var extension = Path.GetExtension(normalizedAtlasAddress);
    var looksLikeAtlasTexture =
      string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase);
    return IsGroupedGeneratedAtlasSurrogateAddress(normalizedAtlasAddress) ||
           ShouldUseMetadataDrivenAtlasLoad(normalizedAtlasAddress) ||
           GeneratedAtlasBuildSurrogateUtility.ShouldUseImportedSpriteSubassets(normalizedAtlasAddress) ||
           looksLikeAtlasTexture;
  }

  static void EnqueuePendingDirectAssetLoadStart(CacheEntry entry) {
    if (entry == null || entry.isEvicted) return;
    entry.pendingDirectSubAssetLoad = true;
    entry.pendingAssetLoadResourceLocationCount = 1;
    entry.pendingAssetLoadExpectedSiblingSliceCount = 0;
    if (entry.pendingAssetLoadStart) return;
    entry.pendingAssetLoadStart = true;
    pendingAssetLoadStartQueue.Enqueue(entry);
  }

  static void EnqueuePendingGroupedGeneratedAtlasLoadStart(CacheEntry entry) {
    if (entry == null || entry.isEvicted) return;
    entry.pendingGroupedGeneratedAtlasLoad = true;
    entry.pendingAssetLoadResourceLocationCount = 2;
    entry.pendingAssetLoadExpectedSiblingSliceCount = 0;
    if (entry.pendingAssetLoadStart) return;
    entry.pendingAssetLoadStart = true;
    pendingAssetLoadStartQueue.Enqueue(entry);
  }

  static void EnqueuePendingMetadataDrivenAtlasLoadStart(CacheEntry entry) {
    if (entry == null || entry.isEvicted) return;
    entry.pendingMetadataDrivenAtlasLoad = true;
    entry.pendingAssetLoadResourceLocationCount = 2;
    entry.pendingAssetLoadExpectedSiblingSliceCount = 0;
    if (entry.pendingAssetLoadStart) return;
    entry.pendingAssetLoadStart = true;
    pendingAssetLoadStartQueue.Enqueue(entry);
  }

  static void EnqueuePendingAssetLoadStart(
    CacheEntry entry,
    IList<IResourceLocation> resourceLocations,
    int resourceLocationCount,
    int expectedSiblingSliceCount
  ) {
    if (entry == null || entry.isEvicted) return;
    entry.pendingAssetLoadLocations.Clear();
    if (resourceLocations != null) {
      for (var i = 0; i < resourceLocations.Count; i++) {
        var location = resourceLocations[i];
        if (location == null) continue;
        entry.pendingAssetLoadLocations.Add(location);
      }
    }
    entry.pendingAssetLoadResourceLocationCount = Math.Max(resourceLocationCount, 0);
    entry.pendingAssetLoadExpectedSiblingSliceCount = Math.Max(expectedSiblingSliceCount, 0);
    if (entry.pendingAssetLoadStart) return;
    entry.pendingAssetLoadStart = true;
    pendingAssetLoadStartQueue.Enqueue(entry);
  }

  static void ClearPendingAssetLoadStart(CacheEntry entry) {
    if (entry == null) return;
    entry.pendingAssetLoadStart = false;
    entry.pendingDirectSubAssetLoad = false;
    entry.pendingGroupedGeneratedAtlasLoad = false;
    entry.pendingMetadataDrivenAtlasLoad = false;
    entry.pendingAssetLoadResourceLocationCount = 0;
    entry.pendingAssetLoadExpectedSiblingSliceCount = 0;
    if (entry.pendingAssetLoadLocations != null) {
      entry.pendingAssetLoadLocations.Clear();
    }
  }

  static void ClearActiveAssetLoadLocations(CacheEntry entry) {
    if (entry == null) return;
    if (entry.activeAssetLoadLocations != null) {
      entry.activeAssetLoadLocations.Clear();
    }
  }

  static int ResolvePendingAssetLoadStartBudgetPerFrame() {
    if (ShouldUseStrictSerialLoadingDebounce()) {
      return StrictSerialLoadingBudgetPerFrame;
    }
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      var cfg = GetSettings();
      return Math.Max(cfg.loadingOverlayMaxAddressableStartsPerFrame, 1);
    }
    return 4;
  }

  static void LogAtlasSynthesisFailureOnce(string loadMode, string atlasAddress, string metadataAddress, string reason) {
    var normalizedAtlasAddress = NormalizeAddress(atlasAddress);
    var normalizedMetadataAddress = NormalizeAddress(metadataAddress);
    var key = loadMode + "|" + normalizedAtlasAddress + "|" + normalizedMetadataAddress;
    if (!atlasSynthesisFailureWarnings.Add(key)) return;

    Debug.LogWarning(
      "[TextureResidencyCache] Atlas synthesis failed" +
      " mode='" + loadMode + "'" +
      " atlas='" + normalizedAtlasAddress + "'" +
      " metadata='" + normalizedMetadataAddress + "'" +
      " reason='" + (string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim()) + "'"
    );
  }

#if UNITY_EDITOR
  static bool TryLoadEditorImportedAtlasSprites(string atlasAddress, out IList<Sprite> sprites) {
    sprites = null;
    if (string.IsNullOrWhiteSpace(atlasAddress)) return false;

    var normalizedAtlasAddress = NormalizeAddress(atlasAddress);
    if (string.IsNullOrWhiteSpace(normalizedAtlasAddress)) return false;

    if (editorImportedAtlasSpriteCache.TryGetValue(normalizedAtlasAddress, out var cachedSprites)) {
      sprites = cachedSprites;
      return cachedSprites != null && cachedSprites.Count > 0;
    }

    var importedAssets = AssetDatabase.LoadAllAssetsAtPath(normalizedAtlasAddress);
    if (importedAssets == null || importedAssets.Length <= 0) {
      editorImportedAtlasSpriteCache[normalizedAtlasAddress] = null;
      return false;
    }

    var importedSprites = new List<Sprite>(importedAssets.Length);
    for (var i = 0; i < importedAssets.Length; i++) {
      if (importedAssets[i] is Sprite sprite) {
        importedSprites.Add(sprite);
      }
    }

    editorImportedAtlasSpriteCache[normalizedAtlasAddress] = importedSprites;
    if (importedSprites.Count <= 0) return false;
    sprites = importedSprites;
    return true;
  }

  static int MergeImportedSpritesIntoEntry(CacheEntry entry, IList<Sprite> sprites) {
    if (entry == null || sprites == null || sprites.Count <= 0) return 0;

    var addedSpriteCount = 0;
    for (var i = 0; i < sprites.Count; i++) {
      var sprite = sprites[i];
      if (sprite == null) continue;
      if (entry.primarySprite == null) {
        entry.primarySprite = sprite;
      }
      if (string.IsNullOrWhiteSpace(sprite.name)) continue;
      if (!entry.spritesByName.ContainsKey(sprite.name)) {
        addedSpriteCount++;
      }
      entry.spritesByName[sprite.name] = sprite;
    }

    return addedSpriteCount;
  }

  static void LogOffsetOnlyMetadataFallbackOnce(string atlasAddress, string metadataAddress, int spriteCount) {
    var normalizedAtlasAddress = NormalizeAddress(atlasAddress);
    var normalizedMetadataAddress = NormalizeAddress(metadataAddress);
    var key = normalizedAtlasAddress + "|" + normalizedMetadataAddress;
    if (!editorOffsetMetadataFallbackLogs.Add(key)) return;

    RuntimeLog.Log(
      "[TextureResidencyCache] Offset-only atlas metadata fallback" +
      " atlas='" + normalizedAtlasAddress + "'" +
      " metadata='" + normalizedMetadataAddress + "'" +
      " fallback='editor_imported_subassets'" +
      " sprite_count=" + spriteCount
    );
  }
#endif

  static void TryCompleteGroupedGeneratedAtlasLoad(
    CacheEntry entry,
    int resourceLocationCount,
    int expectedSiblingSliceCount,
    bool diagnosticsEnabled,
    float callbackStartedAt
  ) {
    if (entry == null || entry.isEvicted || !entry.pendingGroupedGeneratedAtlasLoad) return;
    if (!entry.groupedAtlasTextureHandle.IsValid() || !entry.groupedMetadataHandle.IsValid()) return;
    if (!entry.groupedAtlasTextureHandle.IsDone || !entry.groupedMetadataHandle.IsDone) return;

    ClearPendingAssetLoadStart(entry);
    MarkInFlightComplete(entry);

    if (entry.queuedAtTicks > 0) {
      var latencyMs = (float)((DateTime.UtcNow.Ticks - entry.queuedAtTicks) * (1000.0 / TimeSpan.TicksPerSecond));
      RecordLoadCompleteLatency(latencyMs);
      entry.queuedAtTicks = 0;
    }

    var metadataAddress = GeneratedAtlasBuildSurrogateUtility.BuildMetadataAssetPath(entry.address);
    var loadSucceeded =
      entry.groupedAtlasTextureHandle.Status == AsyncOperationStatus.Succeeded &&
      entry.groupedAtlasTextureHandle.Result != null &&
      entry.groupedMetadataHandle.Status == AsyncOperationStatus.Succeeded &&
      entry.groupedMetadataHandle.Result != null;
    if (!loadSucceeded) {
      LogAtlasSynthesisFailureOnce(
        "grouped_generated_atlas",
        entry.address,
        metadataAddress,
        "texture_status=" + entry.groupedAtlasTextureHandle.Status +
        " texture_loaded=" + (entry.groupedAtlasTextureHandle.Result != null ? 1 : 0) +
        " metadata_status=" + entry.groupedMetadataHandle.Status +
        " metadata_loaded=" + (entry.groupedMetadataHandle.Result != null ? 1 : 0)
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

    EnqueuePendingLoadFinalize(entry, loadSucceeded, resourceLocationCount, expectedSiblingSliceCount);
    pendingQueueStateRecord = true;

    if (diagnosticsEnabled) {
      var callbackMs = ComputeElapsedMs(callbackStartedAt);
      if (callbackMs > 0.1f) {
        RecordLoadCompletionFrameCost(callbackMs, 0f, 0f, entry.address + " (grouped_callback_enqueue)");
      }
    }
  }

  static void TryCompleteMetadataDrivenAtlasLoad(
    CacheEntry entry,
    int resourceLocationCount,
    int expectedSiblingSliceCount,
    bool diagnosticsEnabled,
    float callbackStartedAt
  ) {
    if (entry == null || entry.isEvicted || !entry.pendingMetadataDrivenAtlasLoad) return;
    if (!entry.metadataAtlasTextureHandle.IsValid() || !entry.metadataAtlasMetadataHandle.IsValid()) return;
    if (!entry.metadataAtlasTextureHandle.IsDone || !entry.metadataAtlasMetadataHandle.IsDone) return;

    ClearPendingAssetLoadStart(entry);
    MarkInFlightComplete(entry);

    if (entry.queuedAtTicks > 0) {
      var latencyMs = (float)((DateTime.UtcNow.Ticks - entry.queuedAtTicks) * (1000.0 / TimeSpan.TicksPerSecond));
      RecordLoadCompleteLatency(latencyMs);
      entry.queuedAtTicks = 0;
    }

    var metadataAddress = GeneratedAtlasBuildSurrogateUtility.BuildMetadataAssetPath(entry.address);
    var loadSucceeded =
      entry.metadataAtlasTextureHandle.Status == AsyncOperationStatus.Succeeded &&
      entry.metadataAtlasTextureHandle.Result != null &&
      entry.metadataAtlasMetadataHandle.Status == AsyncOperationStatus.Succeeded &&
      entry.metadataAtlasMetadataHandle.Result != null;
    if (!loadSucceeded) {
      LogAtlasSynthesisFailureOnce(
        "metadata_synthesized_atlas",
        entry.address,
        metadataAddress,
        "texture_status=" + entry.metadataAtlasTextureHandle.Status +
        " texture_loaded=" + (entry.metadataAtlasTextureHandle.Result != null ? 1 : 0) +
        " metadata_status=" + entry.metadataAtlasMetadataHandle.Status +
        " metadata_loaded=" + (entry.metadataAtlasMetadataHandle.Result != null ? 1 : 0)
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

    EnqueuePendingLoadFinalize(entry, loadSucceeded, resourceLocationCount, expectedSiblingSliceCount);
    pendingQueueStateRecord = true;

    if (diagnosticsEnabled) {
      var callbackMs = ComputeElapsedMs(callbackStartedAt);
      if (callbackMs > 0.1f) {
        RecordLoadCompletionFrameCost(callbackMs, 0f, 0f, entry.address + " (metadata_callback_enqueue)");
      }
    }
  }
}
