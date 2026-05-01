#if false
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AddressableSpriteCacheAssetStreaming {
    
    // The data file contains only the static state and configuration constants
    // shared between the public interface and internal implementations
    
    static partial class AddressableSpriteCacheData {
        // --- Cache State ---
        internal static readonly Dictionary<string, AddressableSpriteCache.CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<ulong, int> textureRefCounts = new();
        internal static readonly Dictionary<ulong, long> textureBytesById = new();
        
        // --- Queues ---
        internal static readonly Queue<AddressableSpriteCache.CacheEntry> immediateQueue = new();
        internal static readonly Queue<AddressableSpriteCache.CacheEntry> warmupQueue = new();
        internal static readonly Queue<AddressableSpriteCache.CacheEntry> backgroundQueue = new();
        internal static readonly Queue<AddressableSpriteCache.CacheEntry> pendingAssetLoadStartQueue = new();
        internal static readonly Queue<AddressableSpriteCache.CacheEntry> pendingLoadFinalizeQueue = new();
        internal static readonly Queue<AddressableSpriteCache.CacheEntry> pendingExactSliceSupplementQueue = new();
        internal static readonly Queue<AddressableSpriteCache.CacheEntry> pendingTextureRegisterQueue = new();
        
        // --- Deferred Requests ---
        internal static readonly Dictionary<string, AddressableSpriteCache.CacheEntry> deferredRequests = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly Queue<string> deferredImmediateQueue = new();
        internal static readonly Queue<string> deferredWarmupQueue = new();
        internal static readonly Queue<string> deferredBackgroundQueue = new();
        
        // --- Lifetime Management ---
        internal static readonly Stack<AddressableSpriteCache.Lease> pooledLeases = new();
        internal static readonly Dictionary<string, AddressableSpriteCache.OwnerPinState> ownerPins = new(StringComparer.OrdinalIgnoreCase);
        
        // --- Scratch Buffers ---
        internal static readonly HashSet<string> desiredOwnerAddressScratch = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly List<string> ownerReleaseAddressScratch = new(256);
        internal static readonly HashSet<string> expandedAtlasKeys = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, int> atlasExpansionRetryFrames = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly HashSet<string> gameplayColdMissAtlasKeys = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly List<string> atlasSiblingAddressScratch = new(512);
        internal static readonly List<string> atlasSiblingSpriteNameScratch = new(512);
        internal static readonly List<KeyValuePair<string, int>> requestDiagTopSourcesScratch = new(8);
        internal static readonly StringBuilder requestDiagTopSourcesBuilder = new(256);
        internal static readonly List<AddressableSpriteCache.OwnerPinState> ownerDemoteScratch = new(16);
        
        // --- Statistics & Diagnostics ---
        internal static readonly HashSet<string> incompleteAtlasLoadWarnings = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly HashSet<string> atlasSynthesisFailureWarnings = new(StringComparer.OrdinalIgnoreCase);
#if UNITY_EDITOR
        internal static readonly Queue<AddressableSpriteCache.CacheEntry> pendingEditorAtlasSupplementQueue = new();
        internal static readonly HashSet<string> editorAtlasSupplementWarnings = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly HashSet<string> editorOffsetMetadataFallbackLogs = new(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, IList<Sprite>> editorImportedAtlasSpriteCache = new(StringComparer.OrdinalIgnoreCase);
#endif
        
        // --- Internal State Flags/Counters ---
        internal static AddressableSpriteCache.CacheSettings settings;
        internal static bool settingsLoaded;
        internal static long residentBytes;
        internal static int queuedEntryCount;
        internal static int inFlightLoads;
        internal static int startedLoadsThisFrame;
        internal static int lastPumpFrame = -1;
        internal static int pumpOncePerFrameFrame = -1;
        internal static int lastPumpSnapshotFrame = -1;
        internal static int lastPumpSnapshotQueuedCount = -1;
        internal static int lastPumpSnapshotInFlightCount = -1;
        internal static int deferredFlushFrame = -1;
        internal static int deferredFlushedThisFrame;
        internal static int deferredTotalCount;
        internal static int deferredPromotedCount;
        internal static int deferredRequestCount;
        internal static int completionFollowupFrame = -1;
        internal static bool pendingBudgetMaintain;
        internal static bool pendingQueueStateRecord;
        internal static long cacheHits;
        internal static long cacheMisses;
        internal static int pinDemotions;
        internal static int pinClassBudgetHitCount;
    }
}

#endif

