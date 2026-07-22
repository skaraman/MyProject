using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static partial class TextureResidencyCache {
  static readonly ProfilerMarker PumpProfilerMarker = new ProfilerMarker("TextureResidencyCache.Pump");

  public enum LoadPriority {
    Immediate = 0,
    Warmup = 1,
    Background = 2
  }

  public enum PinClass {
    Player = 0,
    Enemy = 1,
    UI = 2,
    Effect = 3,
    WarmGate = 4
  }

  sealed class OwnerPinState {
    public string ownerId;
    public PinClass pinClass;
    public readonly Dictionary<string, Lease> leases = new(StringComparer.OrdinalIgnoreCase);
    public long lastRefreshTicks;
  }

  internal sealed class CacheEntry {
    public string address;
    public AsyncOperationHandle<IList<IResourceLocation>> locationHandle;
    public AsyncOperationHandle<IList<Sprite>> handle;
    public AsyncOperationHandle<IList<Sprite>> groupedSingleSpriteHandle;
    public AsyncOperationHandle<Texture2D> groupedAtlasTextureHandle;
    public AsyncOperationHandle<TextAsset> groupedMetadataHandle;
    public AsyncOperationHandle<Texture2D> metadataAtlasTextureHandle;
    public AsyncOperationHandle<TextAsset> metadataAtlasMetadataHandle;
    public GeneratedAtlasSpriteSynthesisUtility.AtlasImportPayload parsedGroupedMetadata;
    public GeneratedAtlasSpriteSynthesisUtility.AtlasImportPayload parsedMetadataAtlasMetadata;
    public Dictionary<string, GeneratedAtlasSpriteSynthesisUtility.AtlasSpriteImportPayload> groupedPayloadsByName;
    public Dictionary<string, GeneratedAtlasSpriteSynthesisUtility.AtlasSpriteImportPayload> metadataPayloadsByName;
    public List<AsyncOperationHandle<Sprite>> exactSliceSupplementHandles;
    public List<IResourceLocation> pendingAssetLoadLocations;
    public List<IResourceLocation> activeAssetLoadLocations;
    public HashSet<string> pendingExactSliceSupplementAddresses;
    public HashSet<string> failedExactSliceSupplementAddresses;
    public Dictionary<string, Sprite> spritesByName;
    public List<Sprite> generatedSprites;
    public HashSet<ulong> registeredTextureIds;
    public Sprite primarySprite;
    public bool generatedSpriteSetComplete;
    public int pinCount;
    public bool isDone;
    public bool isSuccess;
    public bool isEvicted;
    public long lastAccessTicks;
    public bool hasTextureRegistration;
    public bool loadStarted;
    public bool countedInFlight;
    public bool isQueued;
    public LoadPriority queuedPriority;
    public long queuedAtTicks; // set when first enqueued; used for enqueue->complete latency tracking
    public int sessionCompletionGeneration;
    public bool editorAtlasSupplementPending;
    public bool editorAtlasSupplementAttempted;
    public bool pendingLoadFinalize;
    public bool pendingLoadSucceeded;
    public int pendingResourceLocationCount;
    public int pendingExpectedSiblingSliceCount;
    public bool pendingAssetLoadStart;
    public bool pendingDirectSubAssetLoad;
    public bool pendingGroupedGeneratedAtlasLoad;
    public bool pendingMetadataDrivenAtlasLoad;
    public int pendingAssetLoadResourceLocationCount;
    public int pendingAssetLoadExpectedSiblingSliceCount;
    public string requestedSpriteNameHint;
    public bool requestedSpriteNameConflict;
    public bool spriteMapMaterialized;
    public bool deferredSpriteMapMaterialization;
    public string requestStrategy;
    public string lastRequestedAddress;
    public bool atlasFallbackToDirect;
    public bool atlasDirectFallbackAttempted;
    public string sourceTag;
  }

  public readonly struct PinSnapshot {
    public readonly int pinnedOwnerCount;
    public readonly int pinnedAddressCount;
    public readonly int pinnedPlayerAddresses;
    public readonly int pinnedEnemyAddresses;
    public readonly int pinnedUiAddresses;
    public readonly int pinnedEffectAddresses;
    public readonly int pinDemotions;
    public readonly int classBudgetHitCount;
    public readonly int classBudgetDroppedAddresses;

    public PinSnapshot(
      int pinnedOwnerCount,
      int pinnedAddressCount,
      int pinnedPlayerAddresses,
      int pinnedEnemyAddresses,
      int pinnedUiAddresses,
      int pinnedEffectAddresses,
      int pinDemotions,
      int classBudgetHitCount,
      int classBudgetDroppedAddresses
    ) {
      this.pinnedOwnerCount = pinnedOwnerCount;
      this.pinnedAddressCount = pinnedAddressCount;
      this.pinnedPlayerAddresses = pinnedPlayerAddresses;
      this.pinnedEnemyAddresses = pinnedEnemyAddresses;
      this.pinnedUiAddresses = pinnedUiAddresses;
      this.pinnedEffectAddresses = pinnedEffectAddresses;
      this.pinDemotions = pinDemotions;
      this.classBudgetHitCount = classBudgetHitCount;
      this.classBudgetDroppedAddresses = classBudgetDroppedAddresses;
    }
  }

  public readonly struct QueueSnapshot {
    public readonly int queuedCount;
    public readonly int inFlightCount;

    public QueueSnapshot(int queuedCount, int inFlightCount) {
      this.queuedCount = Mathf.Max(queuedCount, 0);
      this.inFlightCount = Mathf.Max(inFlightCount, 0);
    }

    public bool IsIdle => queuedCount <= 0 && inFlightCount <= 0;
  }

  public readonly struct DeferredSnapshot {
    public readonly int pendingCount;
    public readonly int flushedThisFrame;
    public readonly int totalDeferredCount;
    public readonly int totalPromotedCount;
    public readonly int totalDeferralRequestCount;

    public DeferredSnapshot(
      int pendingCount,
      int flushedThisFrame,
      int totalDeferredCount,
      int totalPromotedCount,
      int totalDeferralRequestCount
    ) {
      this.pendingCount = Mathf.Max(pendingCount, 0);
      this.flushedThisFrame = Mathf.Max(flushedThisFrame, 0);
      this.totalDeferredCount = Mathf.Max(totalDeferredCount, 0);
      this.totalPromotedCount = Mathf.Max(totalPromotedCount, 0);
      this.totalDeferralRequestCount = Mathf.Max(totalDeferralRequestCount, 0);
    }
  }

  public readonly struct SessionSnapshot {
    public readonly int expectedTotal;
    public readonly int scheduledTotal;
    public readonly int completedTotal;

    public SessionSnapshot(int expectedTotal, int scheduledTotal, int completedTotal) {
      this.expectedTotal = Math.Max(expectedTotal, 0);
      this.scheduledTotal = Math.Max(scheduledTotal, 0);
      this.completedTotal = Math.Max(completedTotal, 0);
    }

    public int EffectiveTotal => expectedTotal > 0 ? Math.Max(expectedTotal, scheduledTotal) : 0;
    public bool HasKnownTotal => expectedTotal > 0;

    public float Progress {
      get {
        if (!HasKnownTotal) return 1f;
        return Mathf.Clamp01((float)completedTotal / EffectiveTotal);
      }
    }
  }

  public sealed class Lease {
    CacheEntry entry;
    bool released;

    internal Lease() {
    }

    internal void Bind(CacheEntry entry) {
      this.entry = entry;
      released = false;
    }

    public bool IsDone => entry == null || entry.isDone;
    public bool IsSuccess => entry != null && entry.isDone && entry.isSuccess && entry.primarySprite != null;
    public Sprite Sprite => IsSuccess ? entry.primarySprite : null;
    public string Address => entry != null ? entry.address : "";
    public bool HasPendingSpriteMapSupplement =>
      entry != null &&
      (entry.editorAtlasSupplementPending ||
       (entry.pendingExactSliceSupplementAddresses != null &&
        entry.pendingExactSliceSupplementAddresses.Count > 0));

    public bool TryGetSprite(string spriteName, out Sprite sprite) {
      sprite = null;
      if (entry == null || !entry.isDone || !entry.isSuccess) return false;
      return TryGetSpriteFromEntry(entry, spriteName, out sprite);
    }

    public bool TryGetSpriteByAddress(string sliceOrAtlasAddress, out Sprite sprite) {
      sprite = null;
      if (entry == null || !entry.isDone || !entry.isSuccess) return false;
      return TryGetSpriteByAddressInternal(entry, sliceOrAtlasAddress, out sprite);
    }

    public bool TryGetSpriteByAddressWithoutEditorSupplement(string sliceOrAtlasAddress, out Sprite sprite) {
      sprite = null;
      if (entry == null || !entry.isDone || !entry.isSuccess) return false;
      return TryGetSpriteByAddressInternal(entry, sliceOrAtlasAddress, out sprite);
    }

    public bool NeedsPendingSpriteMapSupplement(string sliceOrAtlasAddress) {
      if (entry == null || !entry.isDone || !entry.isSuccess) return false;
      if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceOrAtlasAddress, out _, out _)) return false;
      if (TryGetSpriteByAddressWithoutEditorSupplement(sliceOrAtlasAddress, out _)) return false;
      if (entry.editorAtlasSupplementPending) {
        EnsureOverlayExactSliceSupplement(entry, sliceOrAtlasAddress);
        return true;
      }

      EnsureRequestedSliceSupplement(entry, sliceOrAtlasAddress);
      var normalizedSliceAddress = string.IsNullOrWhiteSpace(sliceOrAtlasAddress) ? "" : sliceOrAtlasAddress.Trim();
      return !string.IsNullOrWhiteSpace(normalizedSliceAddress) &&
             entry.pendingExactSliceSupplementAddresses != null &&
             entry.pendingExactSliceSupplementAddresses.Contains(normalizedSliceAddress);
    }

    bool TryGetSpriteByAddressInternal(CacheEntry targetEntry, string sliceOrAtlasAddress, out Sprite sprite) {
      sprite = null;
      if (targetEntry == null || !targetEntry.isDone || !targetEntry.isSuccess) return false;
      if (SpriteSliceAddressUtility.TryParseSliceAddress(sliceOrAtlasAddress, out var atlasAssetPath, out var spriteName)) {
        var normalizedAtlasAddress = NormalizeAddress(atlasAssetPath);
        if (!string.Equals(normalizedAtlasAddress, Address, StringComparison.OrdinalIgnoreCase)) return false;
        if (TryGetSpriteFromEntry(targetEntry, spriteName, out sprite)) return true;
        EnsureRequestedSliceSupplement(targetEntry, sliceOrAtlasAddress);
        return false;
      }

      var normalizedAddress = NormalizeAddress(sliceOrAtlasAddress);
      if (!string.Equals(normalizedAddress, Address, StringComparison.OrdinalIgnoreCase)) return false;
      sprite = targetEntry.primarySprite;
      return sprite != null;
    }

    public void Release() {
      if (released) return;
      released = true;
      var releasedEntry = entry;
      entry = null;
      if (releasedEntry != null) {
        ReleaseInternal(releasedEntry);
      }
      ReturnLeaseToPool(this);
    }
  }

  struct CacheSettings {
    public long softTextureBudgetBytes;
    public long hardTextureBudgetBytes;
    public int maxAddressableStartsPerFrame;
    public int loadingOverlayMaxAddressableStartsPerFrame;
  }

  struct DeferredRequestState {
    public LoadPriority priority;
    public bool pinEntry;
    public string sourceTag;
  }

  readonly struct ExactSliceSupplementRequest {
    public readonly CacheEntry entry;
    public readonly string sliceAddress;

    public ExactSliceSupplementRequest(CacheEntry entry, string sliceAddress) {
      this.entry = entry;
      this.sliceAddress = string.IsNullOrWhiteSpace(sliceAddress) ? "" : sliceAddress.Trim();
    }
  }

  static readonly Dictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<ulong, int> textureRefCounts = new();
  static readonly Dictionary<ulong, long> textureBytesById = new();
  static readonly Queue<CacheEntry> immediateQueue = new();
  static readonly Queue<CacheEntry> warmupQueue = new();
  static readonly Queue<CacheEntry> backgroundQueue = new();
  static readonly Queue<CacheEntry> pendingAssetLoadStartQueue = new();
  static readonly Queue<CacheEntry> pendingLoadFinalizeQueue = new();
  static readonly Queue<ExactSliceSupplementRequest> pendingExactSliceSupplementQueue = new();
  static readonly Queue<CacheEntry> pendingTextureRegisterQueue = new();
  static readonly Dictionary<string, DeferredRequestState> deferredRequests = new(StringComparer.OrdinalIgnoreCase);
  static readonly Queue<string> deferredImmediateQueue = new();
  static readonly Queue<string> deferredWarmupQueue = new();
  static readonly Queue<string> deferredBackgroundQueue = new();
  static readonly Stack<Lease> pooledLeases = new();
  static readonly Dictionary<string, OwnerPinState> ownerPins = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> desiredOwnerAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, string> desiredOwnerRequestScratch = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<string> ownerReleaseAddressScratch = new(256);
  static readonly HashSet<string> expandedAtlasKeys = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, int> atlasExpansionRetryFrames = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> gameplayColdMissAtlasKeys = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<string> atlasSiblingAddressScratch = new(512);
  static readonly List<string> atlasSiblingSpriteNameScratch = new(512);
  static readonly List<KeyValuePair<string, int>> requestDiagTopSourcesScratch = new(8);
  static readonly StringBuilder requestDiagTopSourcesBuilder = new(256);
  static readonly List<OwnerPinState> ownerDemoteScratch = new(16);
  static readonly HashSet<string> incompleteAtlasLoadWarnings = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> atlasSynthesisFailureWarnings = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> spriteLoadOperationFailureWarnings = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> unsupportedSpriteAddressWarnings = new(StringComparer.OrdinalIgnoreCase);
#if UNITY_EDITOR
  static readonly Queue<CacheEntry> pendingEditorAtlasSupplementQueue = new();
  static readonly HashSet<string> editorAtlasSupplementWarnings = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> editorOffsetMetadataFallbackLogs = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, IList<Sprite>> editorImportedAtlasSpriteCache = new(StringComparer.OrdinalIgnoreCase);
#endif

  static CacheSettings settings;
  static bool settingsLoaded;
  static long residentBytes;
  static int queuedEntryCount;
  static int inFlightLoads;
  static int startedLoadsThisFrame;
  static int lastPumpFrame = -1;
  static int pumpOncePerFrameFrame = -1;
  static int lastPumpSnapshotFrame = -1;
  static int lastPumpSnapshotQueuedCount = -1;
  static int lastPumpSnapshotInFlightCount = -1;
  static int deferredFlushFrame = -1;
  static int deferredFlushedThisFrame;
  static int deferredTotalCount;
  static int deferredPromotedCount;
  static int deferredRequestCount;
  static int completionFollowupFrame = -1;
  static bool pendingBudgetMaintain;
  static bool pendingQueueStateRecord;
  static long cacheHits;
  static long cacheMisses;
  static int pinDemotions;
  static int pinClassBudgetHitCount;
  static int pinClassBudgetDroppedAddresses;
  static int ownerPinMutationDepth;
  static int lastBudgetMaintainFrame = -1;
  static bool enableLoadStartDiagnostics = true;
  static float loadStartSlowThresholdMs = 25f;
  static bool enableLoadCompletionDiagnostics = true;
  static float loadCompletionSlowStepThresholdMs = 50f;
  static int loadCompletionDiagFrame = -1;
  static float loadCompletionDiagFrameTotalMs;
  static float loadCompletionDiagFrameRegisterMs;
  static float loadCompletionDiagFrameMaintainMs;
  static int loadCompletionDiagFrameCount;
  static bool loadCompletionDiagFrameReported;
  static float loadCompleteLatencyRollingAvgMs;
  static int loadCompleteLatencyRollingCount;
  static long frameAccessTicks; // cached once per Pump() call; used for hot-path lastAccessTicks writes
  static bool enableRequestFrameDiagnostics = false;
  static int requestDiagFrame = -1;
  static int requestDiagAcquireCalls;
  static int requestDiagWarmupCalls;
  static int requestDiagQueueAdds;
  static int requestDiagNewEntries;
  static int requestDiagPumpCalls;
  static int requestDiagStartedLoads;
  static float requestDiagPumpTotalMs;
  static readonly Dictionary<string, int> requestDiagSourceCounts = new(StringComparer.OrdinalIgnoreCase);
  static int atlasExpansionFrame = -1;
  static int atlasExpansionCountThisFrame;
  static int atlasExpansionAddressBudgetFrame = -1;
  static int atlasExpansionAddressesQueuedThisFrame;
  static float overlayStartTokens;
  static float overlayStartTokenLastRefillAt = -1f;
  static bool loadingContextModeLogged;
  static string loadingContextModeReason = "";
  static int completionPressureUntilFrame = -1;
  static int sessionTotalScheduled;
  static int sessionTotalCompleted;
  static int sessionExpectedTotal;
  static int sessionGeneration;
  const int MaxPooledLeaseCount = 32768;
  const int RequestDiagRequestThreshold = 5000;
  const int RequestDiagQueueAddsThreshold = 5000;
  const int RequestDiagNewEntriesThreshold = 2000;
  const float RequestDiagPumpMsThreshold = 20f;
  static readonly bool EnableStrictSerialLoadingDebounce = true;
  const int StrictSerialLoadingBudgetPerFrame = 1;
  const int AtlasExpansionMaxPerFrame = 4;
  const int AtlasExpansionMaxPerFrameLoading = 4;
  const int AtlasExpansionHardSiblingCap = 96;
  const int AtlasExpansionMaxAddressesPerFrame = 256;
  const int AtlasExpansionMaxAddressesPerFrameLoading = 64;
  const int CompletionPressureCooldownFrames = 8;
  const float CompletionPressureOverlayScale = 0.35f;
  const int DesktopInFlightLoadCap = 96;
  const int MobileInFlightLoadCap = 64;
  const int DesktopOverlayInFlightLoadCap = 12;
  const int MobileOverlayInFlightLoadCap = 8;
  const int DesktopGameplayInFlightLoadCap = 48;
  const int MobileGameplayInFlightLoadCap = 32;
  const int DesktopGameplayMaxStartsPerFrameCap = 6;
  const int MobileGameplayMaxStartsPerFrameCap = 4;
  const int DesktopGameplayImmediateBurstCap = 4;
  const int MobileGameplayImmediateBurstCap = 2;
  const float DesktopOverlayStartRatePerSecond = 10f;
  const float MobileOverlayStartRatePerSecond = 6f;
  const int DesktopOverlayStartBurstCap = 4;
  const int MobileOverlayStartBurstCap = 3;
  const int DeferredFlushOverlayBudgetPerFrame = 8;
  const int DeferredFlushDefaultBudgetPerFrame = 64;
  const int DeferredFlushPressureBudgetPerFrame = 16;
  const int CompletionRegisterProtectedOverlayBudgetPerFrame = 4;
  const int CompletionRegisterOverlayBudgetPerFrame = 8;
  const int CompletionRegisterLoadingBudgetPerFrame = 24;
  const int CompletionRegisterGameplayBudgetPerFrame = 12;
  const int CompletionFinalizeOverlayBudgetPerFrame = 1;
  const int CompletionFinalizeLoadingBudgetPerFrame = 4;
  const int CompletionFinalizeGameplayBudgetPerFrame = 8;
  const float CompletionFollowupOverlayBudgetMs = 1.5f;
  const float CompletionFollowupLoadingBudgetMs = 3.0f;
  const float CompletionFollowupGameplayBudgetMs = 1.0f;
#if UNITY_EDITOR
  const int EditorAtlasSupplementOverlayBudgetPerFrame = 1;
  const int EditorAtlasSupplementLoadingBudgetPerFrame = 4;
  const int EditorAtlasSupplementGameplayBudgetPerFrame = 1;
  const int ExactSliceSupplementOverlayBudgetPerFrame = 24;
  const int ExactSliceSupplementLoadingBudgetPerFrame = 16;
  const int ExactSliceSupplementGameplayBudgetPerFrame = 8;
#endif

  const int MaxBudgetDemotionPassesPerFrame = 3;
  const int MaxBudgetEvictionsPerFrame = 24;


}
