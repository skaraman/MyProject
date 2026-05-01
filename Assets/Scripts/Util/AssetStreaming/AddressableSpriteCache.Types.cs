#if false
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AddressableSpriteCacheAssetStreaming {

    // Data structures for TextureResidencyCache
    // Moved from main file to reduce line count and improve compilation performance

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

    public sealed class OwnerPinState {
        public string ownerId;
        public PinClass pinClass;
        public readonly Dictionary<string, Lease> leases = new(StringComparer.OrdinalIgnoreCase);
        public long lastRefreshTicks;
    }

    public sealed class CacheEntry {
        public string address;
        public AsyncOperationHandle<IList<IResourceLocation>> locationHandle;
        public AsyncOperationHandle<IList<Sprite>> handle;
        public AsyncOperationHandle<IList<Sprite>> groupedSingleSpriteHandle;
        public AsyncOperationHandle<Texture2D> groupedAtlasTextureHandle;
        public AsyncOperationHandle<TextAsset> groupedMetadataHandle;
        public AsyncOperationHandle<Texture2D> metadataAtlasTextureHandle;
        public AsyncOperationHandle<TextAsset> metadataAtlasMetadataHandle;
        public readonly List<IResourceLocation> pendingAssetLoadLocations = new(4);
        public readonly List<IResourceLocation> activeAssetLoadLocations = new(4);
        public readonly HashSet<string> pendingExactSliceSupplementAddresses = new(StringComparer.Ordinal);
        public readonly HashSet<string> failedExactSliceSupplementAddresses = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Sprite> spritesByName = new(StringComparer.Ordinal);
        public readonly List<Sprite> generatedSprites = new();
        public readonly HashSet<ulong> registeredTextureIds = new();
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
        public long queuedAtTicks;
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
    }

    public sealed class Lease {
        public CacheEntry entry;
        private bool released;

        internal Lease() { }

        internal void Bind(CacheEntry entry) {
            this.entry = entry;
            released = false;
        }

        public bool IsDone => entry == null || entry.isDone;
        public bool IsSuccess => entry != null && entry.isDone && entry.isSuccess && entry.primarySprite != null;
        public Sprite Sprite => IsSuccess ? entry.primarySprite : null;
        public string Address => entry != null ? entry.address : "";
        public bool HasPendingSpriteMapSupplement => entry != null && entry.editorAtlasSupplementPending;
    }

    public struct CacheSettings {
        public long softTextureBudgetBytes;
        public long hardTextureBudgetBytes;
        public int maxAddressableStartsPerFrame;
        public int loadingOverlayMaxAddressableStartsPerFrame;
    }

    public struct DeferredRequestState {
        public LoadPriority priority;
        public bool pinEntry;
    }

    public readonly struct ExactSliceSupplementRequest {
        public readonly CacheEntry entry;
        public readonly string sliceAddress;

        public ExactSliceSupplementRequest(CacheEntry entry, string sliceAddress) {
            this.entry = entry;
            this.sliceAddress = string.IsNullOrWhiteSpace(sliceAddress) ? "" : sliceAddress.Trim();
        }
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
}
#endif

