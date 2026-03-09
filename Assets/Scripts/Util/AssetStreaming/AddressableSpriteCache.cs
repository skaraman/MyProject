using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class TextureResidencyCache {
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
    public readonly Dictionary<string, Sprite> spritesByName = new(StringComparer.Ordinal);
    public readonly HashSet<int> registeredTextureIds = new();
    public Sprite primarySprite;
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

    internal Lease(CacheEntry entry) {
      this.entry = entry;
    }

    public bool IsDone => entry == null || entry.isDone;
    public bool IsSuccess => entry != null && entry.isDone && entry.isSuccess && entry.primarySprite != null;
    public Sprite Sprite => IsSuccess ? entry.primarySprite : null;
    public string Address => entry != null ? entry.address : "";
    public bool HasPendingSpriteMapSupplement => entry != null && entry.editorAtlasSupplementPending;

    public bool TryGetSprite(string spriteName, out Sprite sprite) {
      sprite = null;
      if (entry == null || !entry.isDone || !entry.isSuccess) return false;
      return TryGetSpriteFromEntry(entry, spriteName, out sprite);
    }

    public bool TryGetSpriteByAddress(string sliceOrAtlasAddress, out Sprite sprite) {
      sprite = null;
      if (entry == null || !entry.isDone || !entry.isSuccess) return false;
      if (SpriteSliceAddressUtility.TryParseSliceAddress(sliceOrAtlasAddress, out var atlasAssetPath, out var spriteName)) {
        var normalizedAtlasAddress = NormalizeAddress(atlasAssetPath);
        if (!string.Equals(normalizedAtlasAddress, Address, StringComparison.OrdinalIgnoreCase)) return false;
        return TryGetSpriteFromEntry(entry, spriteName, out sprite);
      }

      var normalizedAddress = NormalizeAddress(sliceOrAtlasAddress);
      if (!string.Equals(normalizedAddress, Address, StringComparison.OrdinalIgnoreCase)) return false;
      sprite = entry.primarySprite;
      return sprite != null;
    }

    public void Release() {
      if (released) return;
      released = true;
      if (entry != null) {
        ReleaseInternal(entry);
      }
      entry = null;
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
  }

  static readonly Dictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<int, int> textureRefCounts = new();
  static readonly Dictionary<int, long> textureBytesById = new();
  static readonly Queue<CacheEntry> immediateQueue = new();
  static readonly Queue<CacheEntry> warmupQueue = new();
  static readonly Queue<CacheEntry> backgroundQueue = new();
  static readonly Queue<CacheEntry> pendingTextureRegisterQueue = new();
  static readonly Dictionary<string, DeferredRequestState> deferredRequests = new(StringComparer.OrdinalIgnoreCase);
  static readonly Queue<string> deferredImmediateQueue = new();
  static readonly Queue<string> deferredWarmupQueue = new();
  static readonly Queue<string> deferredBackgroundQueue = new();
  static readonly Dictionary<string, OwnerPinState> ownerPins = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> desiredOwnerAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<string> ownerReleaseAddressScratch = new(256);
  static readonly HashSet<string> expandedAtlasKeys = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, int> atlasExpansionRetryFrames = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> gameplayColdMissAtlasKeys = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<string> atlasSiblingAddressScratch = new(512);
  static readonly List<OwnerPinState> ownerDemoteScratch = new(16);
  static readonly HashSet<string> incompleteAtlasLoadWarnings = new(StringComparer.OrdinalIgnoreCase);
#if UNITY_EDITOR
  static readonly Queue<CacheEntry> pendingEditorAtlasSupplementQueue = new();
  static readonly HashSet<string> editorAtlasSupplementWarnings = new(StringComparer.OrdinalIgnoreCase);
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
  static bool enableLoadCompletionDiagnostics = false;
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
  static int completionPressureUntilFrame = -1;
  static int sessionTotalScheduled;
  static int sessionTotalCompleted;
  static int sessionExpectedTotal;
  static int sessionGeneration;
  const int RequestDiagRequestThreshold = 5000;
  const int RequestDiagQueueAddsThreshold = 5000;
  const int RequestDiagNewEntriesThreshold = 2000;
  const float RequestDiagPumpMsThreshold = 20f;
  const int AtlasExpansionMaxPerFrame = 4;
  const int AtlasExpansionMaxPerFrameLoading = 24;
  const int AtlasExpansionHardSiblingCap = 96;
  const int AtlasExpansionMaxAddressesPerFrame = 256;
  const int AtlasExpansionMaxAddressesPerFrameLoading = 2048;
  const int CompletionPressureCooldownFrames = 8;
  const float CompletionPressureOverlayScale = 0.35f;
  const int DesktopInFlightLoadCap = 96;
  const int MobileInFlightLoadCap = 64;
  const int DesktopGameplayInFlightLoadCap = 48;
  const int MobileGameplayInFlightLoadCap = 32;
  const int DesktopGameplayMaxStartsPerFrameCap = 6;
  const int MobileGameplayMaxStartsPerFrameCap = 4;
  const int DesktopGameplayImmediateBurstCap = 4;
  const int MobileGameplayImmediateBurstCap = 2;
  const int DeferredFlushOverlayBudgetPerFrame = 32;
  const int DeferredFlushDefaultBudgetPerFrame = 64;
  const int DeferredFlushPressureBudgetPerFrame = 16;
  const int CompletionRegisterOverlayBudgetPerFrame = 16;
  const int CompletionRegisterLoadingBudgetPerFrame = 24;
  const int CompletionRegisterGameplayBudgetPerFrame = 12;
#if UNITY_EDITOR
  const int EditorAtlasSupplementOverlayBudgetPerFrame = 2;
  const int EditorAtlasSupplementLoadingBudgetPerFrame = 4;
  const int EditorAtlasSupplementGameplayBudgetPerFrame = 1;
#endif

  const int MaxBudgetDemotionPassesPerFrame = 3;
  const int MaxBudgetEvictionsPerFrame = 24;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    settingsLoaded = false;
    settings = default;
    residentBytes = 0;
    queuedEntryCount = 0;
    inFlightLoads = 0;
    startedLoadsThisFrame = 0;
    lastPumpFrame = -1;
    pumpOncePerFrameFrame = -1;
    lastPumpSnapshotFrame = -1;
    lastPumpSnapshotQueuedCount = -1;
    lastPumpSnapshotInFlightCount = -1;
    deferredFlushFrame = -1;
    deferredFlushedThisFrame = 0;
    deferredTotalCount = 0;
    deferredPromotedCount = 0;
    deferredRequestCount = 0;
    completionFollowupFrame = -1;
    pendingBudgetMaintain = false;
    pendingQueueStateRecord = false;
    cacheHits = 0;
    cacheMisses = 0;
    pinDemotions = 0;
    pinClassBudgetHitCount = 0;
    pinClassBudgetDroppedAddresses = 0;
    ownerPinMutationDepth = 0;
    lastBudgetMaintainFrame = -1;
    loadCompletionDiagFrame = -1;
    loadCompletionDiagFrameTotalMs = 0f;
    loadCompletionDiagFrameRegisterMs = 0f;
    loadCompletionDiagFrameMaintainMs = 0f;
    loadCompletionDiagFrameCount = 0;
    loadCompletionDiagFrameReported = false;
    requestDiagFrame = -1;
    requestDiagAcquireCalls = 0;
    requestDiagWarmupCalls = 0;
    requestDiagQueueAdds = 0;
    requestDiagNewEntries = 0;
    requestDiagPumpCalls = 0;
    requestDiagStartedLoads = 0;
    requestDiagPumpTotalMs = 0f;
    requestDiagSourceCounts.Clear();
    atlasExpansionFrame = -1;
    atlasExpansionCountThisFrame = 0;
    atlasExpansionAddressBudgetFrame = -1;
    atlasExpansionAddressesQueuedThisFrame = 0;
    completionPressureUntilFrame = -1;
    sessionTotalScheduled = 0;
    sessionTotalCompleted = 0;
    sessionExpectedTotal = 0;
    sessionGeneration = 0;
    textureRefCounts.Clear();
    textureBytesById.Clear();
    immediateQueue.Clear();
    warmupQueue.Clear();
    backgroundQueue.Clear();
    pendingTextureRegisterQueue.Clear();
#if UNITY_EDITOR
    pendingEditorAtlasSupplementQueue.Clear();
    editorAtlasSupplementWarnings.Clear();
#endif
    incompleteAtlasLoadWarnings.Clear();
    deferredRequests.Clear();
    deferredImmediateQueue.Clear();
    deferredWarmupQueue.Clear();
    deferredBackgroundQueue.Clear();
    ownerPins.Clear();
    expandedAtlasKeys.Clear();
    atlasExpansionRetryFrames.Clear();
    gameplayColdMissAtlasKeys.Clear();
    atlasSiblingAddressScratch.Clear();
    ownerDemoteScratch.Clear();
    PurgeAll();
  }

  public static int LoadedEntryCount => cache.Count;

  public static long EstimatedResidentBytes {
    get { return Math.Max(residentBytes, 0); }
  }

  public static QueueSnapshot GetQueueSnapshot(bool pump = true) {
    if (pump) {
      Pump();
    }
    return new QueueSnapshot(queuedEntryCount, inFlightLoads);
  }

  public static void PumpOncePerFrame() {
    var frame = Time.frameCount;
    if (pumpOncePerFrameFrame == frame) return;
    pumpOncePerFrameFrame = frame;
    Pump();
  }

  public static DeferredSnapshot GetDeferredSnapshot() {
    var flushedThisFrame = deferredFlushFrame == Time.frameCount ? deferredFlushedThisFrame : 0;
    return new DeferredSnapshot(
      pendingCount: deferredRequests.Count,
      flushedThisFrame: flushedThisFrame,
      totalDeferredCount: deferredTotalCount,
      totalPromotedCount: deferredPromotedCount,
      totalDeferralRequestCount: deferredRequestCount
    );
  }

  public static float GetAverageLoadCompleteLatencyMs() => loadCompleteLatencyRollingAvgMs;

  public static bool IsQueueIdle(bool pump = true) {
    return GetQueueSnapshot(pump).IsIdle;
  }

  public static void BeginSession(int expectedTotal) {
    sessionTotalScheduled = 0;
    sessionTotalCompleted = 0;
    sessionExpectedTotal = Math.Max(expectedTotal, 0);
    sessionGeneration = sessionGeneration == int.MaxValue ? 1 : sessionGeneration + 1;
  }

  public static void EndSession() {
    sessionExpectedTotal = 0;
    sessionGeneration = 0;
  }

  public static SessionSnapshot GetSessionSnapshot() {
    return new SessionSnapshot(
      expectedTotal: sessionExpectedTotal,
      scheduledTotal: sessionTotalScheduled,
      completedTotal: sessionTotalCompleted
    );
  }

  public static float GetSessionProgress() {
    return GetSessionSnapshot().Progress;
  }

  public static Lease AcquireAsync(
    string address,
    LoadPriority priority = LoadPriority.Immediate,
    bool warmGateManaged = false,
    [CallerMemberName] string callerMemberName = "",
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLineNumber = 0
  ) {
    var normalizedAddress = NormalizeAddress(address);
    if (string.IsNullOrEmpty(normalizedAddress)) return null;
    var sourceTag = ShouldLogRequestFrameDiagnostics()
      ? BuildRequestDiagSourceTag(callerMemberName, callerFilePath, callerLineNumber)
      : null;
    return AcquireAsyncNormalized(
      address,
      normalizedAddress,
      priority,
      runPumpAndMaintain: ShouldRunInlinePumpAfterRequest(priority),
      sourceTag: sourceTag,
      warmGateManaged: warmGateManaged
    );
  }

  static Lease AcquireAsyncNormalized(
    string requestedAddress,
    string normalizedAddress,
    LoadPriority priority,
    bool runPumpAndMaintain,
    string sourceTag,
    bool warmGateManaged
  ) {
    RecordRequestForFrame(isAcquire: true, sourceTag: sourceTag);
    var entry = ResolveEntryForLoad(normalizedAddress, out var hit);
    RecordLookup(hit);
    RecordGameplayColdAtlasMiss(normalizedAddress, hit);
    QueueEntryForLoad(entry, priority, pinEntry: true, runPumpAndMaintain, warmGateManaged);
    TryExpandAtlasOnSliceRequest(requestedAddress, priority, runPumpAndMaintain);
    return new Lease(entry);
  }

  public static void RequestLoad(
    string address,
    LoadPriority priority = LoadPriority.Warmup,
    bool warmGateManaged = false,
    [CallerMemberName] string callerMemberName = "",
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLineNumber = 0
  ) {
    var normalizedAddress = NormalizeAddress(address);
    if (string.IsNullOrEmpty(normalizedAddress)) return;
    var requestDiagnosticsEnabled = ShouldLogRequestFrameDiagnostics();
    var sourceTag = requestDiagnosticsEnabled
      ? BuildRequestDiagSourceTag(callerMemberName, callerFilePath, callerLineNumber)
      : null;
    if (requestDiagnosticsEnabled) {
      RecordRequestForFrame(isAcquire: false, sourceTag: sourceTag);
    }

    var entry = ResolveEntryForLoad(normalizedAddress, out var hit);
    RecordLookup(hit);
    RecordGameplayColdAtlasMiss(normalizedAddress, hit);
    var runPumpAndMaintain = ShouldRunInlinePumpAfterRequest(priority);
    QueueEntryForLoad(entry, priority, pinEntry: false, runPumpAndMaintain, warmGateManaged);
    TryExpandAtlasOnSliceRequest(address, priority, runPumpAndMaintain);
  }

  public static void RequestLoadBatch(
    IEnumerable<string> addresses,
    LoadPriority priority = LoadPriority.Warmup,
    bool allowAtlasExpansion = true,
    bool warmGateManaged = false,
    [CallerMemberName] string callerMemberName = "",
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLineNumber = 0
  ) {
    if (addresses == null) return;
    var requestDiagnosticsEnabled = ShouldLogRequestFrameDiagnostics();
    var sourceTag = requestDiagnosticsEnabled
      ? BuildRequestDiagSourceTag(callerMemberName, callerFilePath, callerLineNumber)
      : null;

    foreach (var address in addresses) {
      var normalizedAddress = NormalizeAddress(address);
      if (string.IsNullOrEmpty(normalizedAddress)) continue;
      if (requestDiagnosticsEnabled) {
        RecordRequestForFrame(isAcquire: false, sourceTag: sourceTag);
      }

      var entry = ResolveEntryForLoad(normalizedAddress, out var hit);
      RecordLookup(hit);
      RecordGameplayColdAtlasMiss(normalizedAddress, hit);
      QueueEntryForLoad(entry, priority, pinEntry: false, runPumpAndMaintain: false, warmGateManaged: warmGateManaged);
      if (allowAtlasExpansion) {
        TryExpandAtlasOnSliceRequest(address, priority, runPumpAndMaintain: false);
      }
    }

    Pump();
    MaintainBudget();
  }

  public static IEnumerator RequestLoadBatchThrottled(
    IEnumerable<string> addresses,
    LoadPriority priority = LoadPriority.Warmup,
    bool allowAtlasExpansion = true,
    int enqueueBudgetPerFrame = 128,
    bool warmGateManaged = false,
    [CallerMemberName] string callerMemberName = "",
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLineNumber = 0
  ) {
    if (addresses == null) yield break;
    var requestDiagnosticsEnabled = ShouldLogRequestFrameDiagnostics();
    var sourceTag = requestDiagnosticsEnabled
      ? BuildRequestDiagSourceTag(callerMemberName, callerFilePath, callerLineNumber)
      : null;

    // Keep per-frame enqueues in the 50-200 window suggested by AGENTS guidance.
    // Callers must pre-rank addresses (player current/next first, nearest enemies next) so
    // throttling preserves deterministic first-play frame continuity under queue pressure.
    enqueueBudgetPerFrame = ResolveAdaptiveEnqueueBudgetPerFrame(enqueueBudgetPerFrame);
    var remainingThisFrame = enqueueBudgetPerFrame;

    foreach (var address in addresses) {
      var normalizedAddress = NormalizeAddress(address);
      if (string.IsNullOrEmpty(normalizedAddress)) continue;
      if (requestDiagnosticsEnabled) {
        RecordRequestForFrame(isAcquire: false, sourceTag: sourceTag);
      }

      var entry = ResolveEntryForLoad(normalizedAddress, out var hit);
      RecordLookup(hit);
      RecordGameplayColdAtlasMiss(normalizedAddress, hit);
      QueueEntryForLoad(entry, priority, pinEntry: false, runPumpAndMaintain: false, warmGateManaged: warmGateManaged);
      if (allowAtlasExpansion) {
        TryExpandAtlasOnSliceRequest(address, priority, runPumpAndMaintain: false);
      }

      remainingThisFrame--;
      if (remainingThisFrame > 0) continue;

      Pump();
      MaintainBudget();
      remainingThisFrame = enqueueBudgetPerFrame;
      yield return null;
    }

    Pump();
    MaintainBudget();
  }

  static int ResolveAdaptiveEnqueueBudgetPerFrame(int requestedBudgetPerFrame) {
    var budget = Mathf.Clamp(requestedBudgetPerFrame, 50, 200);
    var memoryMb = Math.Max(SystemInfo.systemMemorySize, 0);
    if (memoryMb > 0 && memoryMb <= 4096) budget = Math.Min(budget, 80);
    else if (memoryMb > 0 && memoryMb <= 8192) budget = Math.Min(budget, 120);

    if (queuedEntryCount >= 1400 || inFlightLoads >= 192) budget = Math.Min(budget, 60);
    else if (queuedEntryCount >= 900 || inFlightLoads >= 128) budget = Math.Min(budget, 90);
    else if (queuedEntryCount >= 500 || inFlightLoads >= 64) budget = Math.Min(budget, 120);

    return Mathf.Clamp(budget, 50, 200);
  }

  static bool ShouldRunInlinePumpAfterRequest(LoadPriority priority) {
    if (priority != LoadPriority.Immediate) return false;
    // Keep request paths non-blocking during gameplay. A single per-frame PumpOncePerFrame
    // call (from animation ticks) is enough to advance queue work without transition spikes.
    return false;
  }

  public static bool TryGetLoadedSprite(string address, out Sprite sprite, bool pump = true) {
    if (SpriteSliceAddressUtility.TryParseSliceAddress(address, out var atlasAddress, out var spriteName)) {
      return TryGetLoadedSprite(atlasAddress, spriteName, out sprite, pump);
    }
    return TryGetLoadedSprite(address, spriteName: "", out sprite, pump);
  }

  public static bool TryGetLoadedSprite(string atlasAddress, string spriteName, out Sprite sprite, bool pump = true) {
    sprite = null;
    var normalizedAddress = NormalizeAddress(atlasAddress);
    if (string.IsNullOrEmpty(normalizedAddress)) return false;

    if (pump) {
      Pump();
    }
    if (!cache.TryGetValue(normalizedAddress, out var entry) || entry == null) return false;
    if (!entry.isDone || !entry.isSuccess || entry.primarySprite == null) return false;

    entry.lastAccessTicks = frameAccessTicks;
    var resolved = TryGetSpriteFromEntry(entry, spriteName, out sprite);
    if (resolved && !string.IsNullOrWhiteSpace(spriteName)) {
      SpriteStreamingDiagnostics.RecordResidentSliceLookup();
    }
    return resolved;
  }

  public static bool IsReady(string address, bool pump = true) {
    return IsAtlasReady(address, pump);
  }

  public static bool IsAtlasReady(string atlasAddress, bool pump = true) {
    var normalizedAddress = NormalizeAddress(atlasAddress);
    if (string.IsNullOrEmpty(normalizedAddress)) return false;

    if (pump) {
      Pump();
    }
    if (!cache.TryGetValue(normalizedAddress, out var entry) || entry == null) return false;
    return entry.isDone && entry.isSuccess && entry.primarySprite != null;
  }

  public static void UpdateOwnerPins(
    string ownerId,
    PinClass pinClass,
    List<string> addresses,
    LoadPriority priority = LoadPriority.Warmup
  ) {
    var normalizedOwnerId = NormalizeOwnerId(ownerId);
    if (string.IsNullOrWhiteSpace(normalizedOwnerId)) return;

    if (addresses == null || addresses.Count == 0) {
      ReleaseOwnerPins(normalizedOwnerId);
      return;
    }

    // Normalize upfront so AddressesMatchExistingLeases and the diff loop both use the same pre-normalized set.
    desiredOwnerAddressScratch.Clear();
    for (var i = 0; i < addresses.Count; i++) {
      var normalizedAddress = NormalizeAddress(addresses[i]);
      if (string.IsNullOrWhiteSpace(normalizedAddress)) continue;
      desiredOwnerAddressScratch.Add(normalizedAddress);
    }

    if (desiredOwnerAddressScratch.Count == 0) {
      desiredOwnerAddressScratch.Clear();
      ReleaseOwnerPins(normalizedOwnerId);
      return;
    }

    // Fast path: leases already match — just refresh metadata without mutating the pin state.
    if (ownerPins.TryGetValue(normalizedOwnerId, out var existingState) &&
        existingState != null &&
        AddressesMatchExistingLeases(existingState.leases, desiredOwnerAddressScratch)) {
      existingState.pinClass = pinClass;
      existingState.lastRefreshTicks = DateTime.UtcNow.Ticks;
      desiredOwnerAddressScratch.Clear();
      return;
    }

    ownerPinMutationDepth++;
    try {
      if (!ownerPins.TryGetValue(normalizedOwnerId, out var state) || state == null) {
        state = new OwnerPinState {
          ownerId = normalizedOwnerId,
          pinClass = pinClass,
          lastRefreshTicks = DateTime.UtcNow.Ticks
        };
        ownerPins[normalizedOwnerId] = state;
      }

      state.pinClass = pinClass;
      state.lastRefreshTicks = DateTime.UtcNow.Ticks;
      var classBudget = GetPinClassBudget(pinClass);
      var classBudgetHit = false;
      var classBudgetDropped = 0;

      ownerReleaseAddressScratch.Clear();
      foreach (var pair in state.leases) {
        if (desiredOwnerAddressScratch.Contains(pair.Key)) continue;
        ownerReleaseAddressScratch.Add(pair.Key);
      }

      for (var i = 0; i < ownerReleaseAddressScratch.Count; i++) {
        if (!state.leases.TryGetValue(ownerReleaseAddressScratch[i], out var lease) || lease == null) continue;
        lease.Release();
        state.leases.Remove(ownerReleaseAddressScratch[i]);
      }

      if (classBudget > 0 && state.leases.Count > classBudget) {
        classBudgetHit = true;
        var overflow = state.leases.Count - classBudget;
        // Reuse pooled scratch (already processed above) to avoid allocating a new key list.
        ownerReleaseAddressScratch.Clear();
        foreach (var key in state.leases.Keys) ownerReleaseAddressScratch.Add(key);
        for (var i = 0; i < ownerReleaseAddressScratch.Count && overflow > 0; i++) {
          if (!state.leases.TryGetValue(ownerReleaseAddressScratch[i], out var trimLease) || trimLease == null) continue;
          trimLease.Release();
          state.leases.Remove(ownerReleaseAddressScratch[i]);
          overflow--;
          classBudgetDropped++;
        }
      }

      foreach (var desiredAddress in desiredOwnerAddressScratch) {
        if (state.leases.ContainsKey(desiredAddress)) continue;
        if (!EnsurePinClassBudgetCapacity(pinClass, normalizedOwnerId, classBudget)) {
          classBudgetHit = true;
          classBudgetDropped++;
          continue;
        }
        var lease = AcquireAsyncNormalized(
          desiredAddress,
          desiredAddress,
          priority,
          runPumpAndMaintain: false,
          sourceTag: "UpdateOwnerPins",
          warmGateManaged: false
        );
        if (lease == null) continue;
        state.leases[desiredAddress] = lease;
      }

      if (state.leases.Count == 0) {
        ownerPins.Remove(normalizedOwnerId);
      }

      if (classBudgetHit) {
        pinClassBudgetHitCount++;
        pinClassBudgetDroppedAddresses += Math.Max(classBudgetDropped, 0);
        SpriteStreamingDiagnostics.RecordPinBudgetPressure(1, classBudgetDropped);
      }
    }
    finally {
      ownerReleaseAddressScratch.Clear();
      desiredOwnerAddressScratch.Clear();
      ownerPinMutationDepth = Math.Max(ownerPinMutationDepth - 1, 0);
    }

    Pump();
    MaintainBudget();
    RecordPinStateIfEnabled();
  }

  public static void ReleaseOwnerPins(string ownerId) {
    var normalizedOwnerId = NormalizeOwnerId(ownerId);
    if (string.IsNullOrWhiteSpace(normalizedOwnerId)) return;
    if (!ownerPins.TryGetValue(normalizedOwnerId, out var state) || state == null) return;

    ownerPinMutationDepth++;
    try {
      foreach (var lease in state.leases.Values) {
        lease?.Release();
      }
      state.leases.Clear();
      ownerPins.Remove(normalizedOwnerId);
    }
    finally {
      ownerPinMutationDepth = Math.Max(ownerPinMutationDepth - 1, 0);
    }

    MaintainBudget();
    RecordPinStateIfEnabled();
  }

  public static PinSnapshot GetPinSnapshot() {
    var pinnedOwnerCount = 0;
    var pinnedAddressCount = 0;
    var pinnedPlayerAddresses = 0;
    var pinnedEnemyAddresses = 0;
    var pinnedUiAddresses = 0;
    var pinnedEffectAddresses = 0;

    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state == null) continue;
      var addressCount = state.leases.Count;
      if (addressCount <= 0) continue;
      pinnedOwnerCount++;
      pinnedAddressCount += addressCount;
      switch (state.pinClass) {
        case PinClass.Player:
          pinnedPlayerAddresses += addressCount;
          break;
        case PinClass.Enemy:
          pinnedEnemyAddresses += addressCount;
          break;
        case PinClass.UI:
          pinnedUiAddresses += addressCount;
          break;
        case PinClass.Effect:
          pinnedEffectAddresses += addressCount;
          break;
      }
    }

    return new PinSnapshot(
      pinnedOwnerCount: pinnedOwnerCount,
      pinnedAddressCount: pinnedAddressCount,
      pinnedPlayerAddresses: pinnedPlayerAddresses,
      pinnedEnemyAddresses: pinnedEnemyAddresses,
      pinnedUiAddresses: pinnedUiAddresses,
      pinnedEffectAddresses: pinnedEffectAddresses,
      pinDemotions: Math.Max(pinDemotions, 0),
      classBudgetHitCount: Math.Max(pinClassBudgetHitCount, 0),
      classBudgetDroppedAddresses: Math.Max(pinClassBudgetDroppedAddresses, 0)
    );
  }

  public static void Pump() {
    frameAccessTicks = DateTime.UtcNow.Ticks;
    ResetFrameCounterIfNeeded();
    FlushDeferredRequestsIntoMainQueues();
    ProcessPendingCompletionFollowups();
    var diagnosticsEnabled = ShouldLogRequestFrameDiagnostics();
    var pumpStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
    var cfg = GetSettings();
    var maxStarts = ResolveMaxStartsPerFrame(cfg);
    var maxInFlightLoads = ResolveMaxInFlightLoads();
    if (ShouldSkipPumpForCurrentFrame(maxStarts)) {
      SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
      RecordPinStateIfEnabled();
      return;
    }
    var startedBefore = startedLoadsThisFrame;

    while (startedLoadsThisFrame < maxStarts && inFlightLoads < maxInFlightLoads) {
      if (!TryDequeueNext(out var entry, out var sourcePriority)) break;
      if (entry == null || entry.isEvicted) continue;
      if (!entry.isQueued) continue;
      if (entry.queuedPriority != sourcePriority) continue;
      if (entry.isDone || entry.loadStarted) {
        ClearQueuedFlag(entry);
        continue;
      }

      ClearQueuedFlag(entry);
      StartLoad(entry);
      startedLoadsThisFrame++;
    }

    // Drain a bounded set of remaining Immediate requests so Warmup/Background
    // backlog cannot starve active animation frame loads, without allowing
    // unbounded gameplay-time load bursts in a single frame.
    var immediateBurstRemaining = ResolveImmediateBurstBudget();
    while (immediateQueue.Count > 0) {
      if (immediateBurstRemaining == 0) break;
      if (inFlightLoads >= maxInFlightLoads) break;
      var immediateEntry = immediateQueue.Dequeue();
      if (immediateEntry == null || immediateEntry.isEvicted || !immediateEntry.isQueued) continue;
      if (immediateEntry.queuedPriority != LoadPriority.Immediate) continue;
      if (immediateEntry.isDone || immediateEntry.loadStarted) { ClearQueuedFlag(immediateEntry); continue; }
      ClearQueuedFlag(immediateEntry);
      StartLoad(immediateEntry);
      startedLoadsThisFrame++;
      if (immediateBurstRemaining != int.MaxValue) immediateBurstRemaining--;
    }

    CachePumpFrameSnapshot();

    if (diagnosticsEnabled) {
      var pumpMs = ComputeElapsedMs(pumpStartedAt);
      var startedThisPump = Math.Max(startedLoadsThisFrame - startedBefore, 0);
      RecordPumpForFrame(pumpMs, startedThisPump);
    }

    ProcessPendingCompletionFollowups();
    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
    RecordPinStateIfEnabled();
  }

  static int ResolveMaxStartsPerFrame(CacheSettings cfg) {
    var baseStarts = Math.Max(cfg.maxAddressableStartsPerFrame, 1);
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      var gameplayCap = Application.isMobilePlatform
        ? MobileGameplayMaxStartsPerFrameCap
        : DesktopGameplayMaxStartsPerFrameCap;
      return Mathf.Clamp(baseStarts, 1, gameplayCap);
    }
    var overlayStarts = Math.Max(cfg.loadingOverlayMaxAddressableStartsPerFrame, baseStarts);
    if (deferredRequests.Count > 0) {
      var drainCap = Application.isMobilePlatform ? 2 : 3;
      overlayStarts = Math.Min(overlayStarts, drainCap);
    }
    if (!IsCompletionPressureActive()) return overlayStarts;
    var throttledStarts = Mathf.CeilToInt(overlayStarts * CompletionPressureOverlayScale);
    var minStarts = Math.Min(baseStarts, overlayStarts);
    return Mathf.Clamp(throttledStarts, minStarts, overlayStarts);
  }

  static int ResolveImmediateBurstBudget() {
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      return int.MaxValue;
    }
    return Application.isMobilePlatform
      ? MobileGameplayImmediateBurstCap
      : DesktopGameplayImmediateBurstCap;
  }

  static int ResolveMaxInFlightLoads() {
    var cap = Application.isMobilePlatform ? MobileInFlightLoadCap : DesktopInFlightLoadCap;
    var memoryMb = Math.Max(SystemInfo.systemMemorySize, 0);
    if (memoryMb > 0 && memoryMb <= 4096) cap = Math.Min(cap, 64);
    else if (memoryMb > 0 && memoryMb <= 8192) cap = Math.Min(cap, 96);

    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      var overlayCap = Application.isMobilePlatform ? 48 : 64;
      cap = Math.Min(cap, overlayCap);
    }
    else {
      var gameplayCap = Application.isMobilePlatform
        ? MobileGameplayInFlightLoadCap
        : DesktopGameplayInFlightLoadCap;
      cap = Math.Min(cap, gameplayCap);
    }

    if (IsCompletionPressureActive()) {
      var throttled = Mathf.CeilToInt(cap * 0.75f);
      var minThrottledCap = Application.isMobilePlatform ? 24 : 32;
      cap = Mathf.Max(throttled, minThrottledCap);
    }

    return Math.Max(cap, Application.isMobilePlatform ? 24 : 32);
  }

  public static void Release(Lease lease) {
    if (lease == null) return;
    lease.Release();
    MaintainBudget();
  }

  public static void PurgeAll() {
    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state == null) continue;
      foreach (var lease in state.leases.Values) {
        lease?.Release();
      }
      state.leases.Clear();
    }
    ownerPins.Clear();
    expandedAtlasKeys.Clear();
    atlasExpansionRetryFrames.Clear();
    atlasSiblingAddressScratch.Clear();
#if UNITY_EDITOR
    editorAtlasSupplementWarnings.Clear();
    pendingEditorAtlasSupplementQueue.Clear();
#endif
    incompleteAtlasLoadWarnings.Clear();
    deferredRequests.Clear();
    deferredImmediateQueue.Clear();
    deferredWarmupQueue.Clear();
    deferredBackgroundQueue.Clear();
    deferredFlushFrame = -1;
    deferredFlushedThisFrame = 0;
    deferredTotalCount = 0;
    deferredPromotedCount = 0;
    deferredRequestCount = 0;
    completionFollowupFrame = -1;
    pendingBudgetMaintain = false;
    pendingQueueStateRecord = false;

    foreach (var pair in cache) {
      var entry = pair.Value;
      if (entry == null) continue;
      entry.isEvicted = true;
      ClearQueuedFlag(entry);
      UnregisterTextureContribution(entry);
      ReleaseHandle(entry);
    }

    cache.Clear();
    residentBytes = 0;
    queuedEntryCount = 0;
    inFlightLoads = 0;
    pumpOncePerFrameFrame = -1;
    immediateQueue.Clear();
    warmupQueue.Clear();
    backgroundQueue.Clear();
    pendingTextureRegisterQueue.Clear();
    textureRefCounts.Clear();
    textureBytesById.Clear();
    loadCompleteLatencyRollingAvgMs = 0f;
    loadCompleteLatencyRollingCount = 0;
    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
    RecordPinStateIfEnabled();
  }

  public static int EvictAllUnpinnedCompleted(int maxEvictions = int.MaxValue) {
    var budget = Math.Max(maxEvictions, 1);
    var evicted = 0;
    while (evicted < budget && TryEvictOldestUnpinned()) {
      evicted++;
    }
    if (evicted > 0) {
      SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
      RecordPinStateIfEnabled();
    }
    return evicted;
  }

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

  static void StartLoad(CacheEntry entry) {
    if (entry == null || entry.isEvicted || entry.loadStarted || entry.isDone) return;

    entry.loadStarted = true;
    entry.isDone = false;
    entry.isSuccess = false;
    entry.primarySprite = null;
    entry.spritesByName.Clear();
    entry.lastAccessTicks = DateTime.UtcNow.Ticks;
    var atlasLocationKeys = BuildAtlasLocationKeys(entry.address, out var atlasSiblingSliceCount);
    // Ideal frame pacing depends on bounded in-flight starts per frame.
    // Pump/start budgets keep this call from turning into bursty main-thread work.
    entry.locationHandle = atlasLocationKeys != null && atlasLocationKeys.Count > 1
      ? Addressables.LoadResourceLocationsAsync(atlasLocationKeys, Addressables.MergeMode.Union, typeof(Sprite))
      : Addressables.LoadResourceLocationsAsync(entry.address, typeof(Sprite));
    entry.countedInFlight = true;
    inFlightLoads++;
    SpriteStreamingDiagnostics.RecordLoadStarted();
    SpriteStreamingDiagnostics.RecordAtlasLoadStarted();
    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);

    entry.locationHandle.Completed += locationOp => {
      if (entry == null || entry.isEvicted) {
        MarkInFlightComplete(entry);
        SpriteStreamingDiagnostics.RecordAtlasLoadCompleted();
        ReleaseLocationHandle(entry);
        return;
      }

      if (locationOp.Status != AsyncOperationStatus.Succeeded || locationOp.Result == null || locationOp.Result.Count <= 0) {
        FinalizeLoadFailure(entry, diagnosticsEnabled: ShouldLogLoadCompletionDiagnostics(), completionStartedAt: Time.realtimeSinceStartup);
        ReleaseLocationHandle(entry);
        return;
      }

      var resourceLocationCount = locationOp.Result.Count;
      entry.handle = Addressables.LoadAssetsAsync<Sprite>(locationOp.Result, null, releaseDependenciesOnFailure: false);
      entry.handle.Completed += assetOp => {
        var diagnosticsEnabled = ShouldLogLoadCompletionDiagnostics();
        var completionStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
        MarkInFlightComplete(entry);
        entry.loadStarted = false;

        if (entry.queuedAtTicks > 0) {
          var latencyMs = (float)((DateTime.UtcNow.Ticks - entry.queuedAtTicks) * (1000.0 / TimeSpan.TicksPerSecond));
          RecordLoadCompleteLatency(latencyMs);
          entry.queuedAtTicks = 0;
        }

        var loadSucceeded = assetOp.Status == AsyncOperationStatus.Succeeded && assetOp.Result != null && assetOp.Result.Count > 0;
        SpriteStreamingDiagnostics.RecordAtlasLoadCompleted();

        if (entry.isEvicted) {
          ReleaseLocationHandle(entry);
          pendingQueueStateRecord = true;
          if (diagnosticsEnabled) {
            var evictedMs = ComputeElapsedMs(completionStartedAt);
            RecordLoadCompletionFrameCost(evictedMs, 0f, 0f, entry.address);
          }
          return;
        }

        entry.isDone = true;
        entry.isSuccess = loadSucceeded;
        PopulateEntrySpriteMap(entry, entry.isSuccess ? assetOp.Result : null);
        entry.lastAccessTicks = DateTime.UtcNow.Ticks;
        LogIncompleteAtlasSpriteMap(entry, atlasSiblingSliceCount, resourceLocationCount, entry.isSuccess ? assetOp.Result.Count : 0);

        if (entry.isSuccess) {
          EnqueuePendingTextureRegister(entry);
        }

        ReleaseLocationHandle(entry);
        pendingBudgetMaintain = true;
        pendingQueueStateRecord = true;

        if (diagnosticsEnabled) {
          var totalMs = ComputeElapsedMs(completionStartedAt);
          RecordLoadCompletionFrameCost(totalMs, 0f, 0f, entry.address);
        }
      };
    };
  }

  static void FinalizeLoadFailure(CacheEntry entry, bool diagnosticsEnabled, float completionStartedAt) {
    if (entry == null) return;
    MarkInFlightComplete(entry);
    entry.loadStarted = false;
    entry.isDone = true;
    entry.isSuccess = false;
    SpriteStreamingDiagnostics.RecordAtlasLoadCompleted();
    entry.primarySprite = null;
    entry.spritesByName.Clear();
    if (entry.queuedAtTicks > 0) {
      var latencyMs = (float)((DateTime.UtcNow.Ticks - entry.queuedAtTicks) * (1000.0 / TimeSpan.TicksPerSecond));
      RecordLoadCompleteLatency(latencyMs);
      entry.queuedAtTicks = 0;
    }
    pendingBudgetMaintain = true;
    pendingQueueStateRecord = true;
    if (diagnosticsEnabled) {
      var totalMs = ComputeElapsedMs(completionStartedAt);
      RecordLoadCompletionFrameCost(totalMs, 0f, 0f, entry.address);
    }
  }

  static void PopulateEntrySpriteMap(CacheEntry entry, IList<Sprite> loadedSprites) {
    if (entry == null) return;
    entry.spritesByName.Clear();
    entry.primarySprite = null;
    entry.editorAtlasSupplementPending = false;
    if (loadedSprites == null) return;
    for (var i = 0; i < loadedSprites.Count; i++) {
      var sprite = loadedSprites[i];
      if (sprite == null) continue;
      if (entry.primarySprite == null) {
        entry.primarySprite = sprite;
      }
      if (string.IsNullOrWhiteSpace(sprite.name)) continue;
      entry.spritesByName[sprite.name] = sprite;
    }
#if UNITY_EDITOR
    EnqueueEditorAtlasSpriteMapSupplement(entry, loadedSprites.Count);
#endif
  }

  static List<object> BuildAtlasLocationKeys(string atlasAddress, out int siblingSliceCount) {
    siblingSliceCount = 0;
    if (string.IsNullOrWhiteSpace(atlasAddress)) return null;

    const int MaxAtlasPrimaryLoadKeys = 512;
    atlasSiblingAddressScratch.Clear();
    if (!SpriteRuntimeResolver.TryCollectAtlasSiblingAddresses(atlasAddress, atlasSiblingAddressScratch, MaxAtlasPrimaryLoadKeys)) {
      atlasSiblingAddressScratch.Clear();
      return null;
    }

    if (atlasSiblingAddressScratch.Count <= 0) {
      atlasSiblingAddressScratch.Clear();
      return null;
    }

    var keys = new List<object>(atlasSiblingAddressScratch.Count + 1) { atlasAddress };
    for (var i = 0; i < atlasSiblingAddressScratch.Count; i++) {
      var siblingAddress = atlasSiblingAddressScratch[i];
      if (string.IsNullOrWhiteSpace(siblingAddress)) continue;
      if (string.Equals(siblingAddress, atlasAddress, StringComparison.OrdinalIgnoreCase)) continue;
      keys.Add(siblingAddress);
      siblingSliceCount++;
    }
    atlasSiblingAddressScratch.Clear();
    return siblingSliceCount > 0 ? keys : null;
  }

  static void LogIncompleteAtlasSpriteMap(CacheEntry entry, int expectedSiblingSliceCount, int resourceLocationCount, int loadedSpriteCount) {
    if (entry == null || !entry.isSuccess) return;
    if (expectedSiblingSliceCount <= 0) return;
    if (entry.spritesByName.Count > 1) return;
    if (!incompleteAtlasLoadWarnings.Add(entry.address)) return;

    Debug.LogWarning(
      "[TextureResidencyCache] Atlas load completed with an incomplete sprite map" +
      " address='" + entry.address + "'" +
      " expected_slices=" + expectedSiblingSliceCount +
      " location_count=" + resourceLocationCount +
      " loaded_count=" + loadedSpriteCount +
      " mapped_count=" + entry.spritesByName.Count
    );
  }

#if UNITY_EDITOR
  static void EnqueueEditorAtlasSpriteMapSupplement(CacheEntry entry, int loadedSpriteCount) {
    if (entry == null) return;
    if (entry.spritesByName.Count > 1) return;
    if (string.IsNullOrWhiteSpace(entry.address)) return;
    if (loadedSpriteCount > 1) return;
    if (entry.editorAtlasSupplementPending) return;
    entry.editorAtlasSupplementPending = true;
    pendingEditorAtlasSupplementQueue.Enqueue(entry);
  }

  static int ResolveEditorAtlasSupplementBudgetPerFrame() {
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      return EditorAtlasSupplementOverlayBudgetPerFrame;
    }
    if (queuedEntryCount > 0 || inFlightLoads > 0 || deferredRequests.Count > 0) {
      return EditorAtlasSupplementLoadingBudgetPerFrame;
    }
    return EditorAtlasSupplementGameplayBudgetPerFrame;
  }

  static void ProcessPendingEditorAtlasSupplements() {
    var budget = Math.Max(ResolveEditorAtlasSupplementBudgetPerFrame(), 1);
    var processed = 0;
    while (processed < budget && pendingEditorAtlasSupplementQueue.Count > 0) {
      var entry = pendingEditorAtlasSupplementQueue.Dequeue();
      if (entry == null) continue;
      entry.editorAtlasSupplementPending = false;
      if (entry.isEvicted || !entry.isDone || !entry.isSuccess) continue;
      if (entry.spritesByName.Count > 1) continue;
      TrySupplementEntrySpriteMapFromEditor(entry);
      processed++;
    }
  }

  static void TrySupplementEntrySpriteMapFromEditor(CacheEntry entry) {
    if (entry == null) return;
    if (entry.spritesByName.Count > 1) return;
    if (string.IsNullOrWhiteSpace(entry.address)) return;

    var assets = AssetDatabase.LoadAllAssetsAtPath(entry.address);
    if (assets == null || assets.Length <= 0) return;

    var addedSpriteCount = 0;
    for (var i = 0; i < assets.Length; i++) {
      var sprite = assets[i] as Sprite;
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

    if (entry.spritesByName.Count <= 1) return;
    if (!editorAtlasSupplementWarnings.Add(entry.address)) return;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return;

    Debug.LogWarning(
      "[TextureResidencyCache] Supplemented editor atlas sprite map address='" + entry.address +
      "' addressables_count=1" +
      " editor_count=" + entry.spritesByName.Count +
      " added=" + addedSpriteCount
    );
  }
#endif

  static void ReleaseLocationHandle(CacheEntry entry) {
    if (entry == null) return;
    if (!entry.locationHandle.IsValid()) return;
    Addressables.Release(entry.locationHandle);
    entry.locationHandle = default;
  }

  static void EnqueueLoad(CacheEntry entry, LoadPriority priority) {
    if (entry == null || entry.isEvicted || entry.isDone || entry.loadStarted) return;

    if (!entry.isQueued) {
      entry.isQueued = true;
      entry.queuedPriority = priority;
      entry.queuedAtTicks = DateTime.UtcNow.Ticks;
      queuedEntryCount++;
      if (sessionExpectedTotal > 0 && sessionGeneration > 0) {
        sessionTotalScheduled++;
      }
      RecordQueueAddForFrame();
      EnqueueByPriority(entry, priority);
      return;
    }

    if (priority < entry.queuedPriority) {
      entry.queuedPriority = priority;
      EnqueueByPriority(entry, priority);
    }
  }

  static void EnqueueByPriority(CacheEntry entry, LoadPriority priority) {
    switch (priority) {
      case LoadPriority.Immediate:
        immediateQueue.Enqueue(entry);
        break;
      case LoadPriority.Warmup:
        warmupQueue.Enqueue(entry);
        break;
      default:
        backgroundQueue.Enqueue(entry);
        break;
    }
  }

  static bool TryDequeueNext(out CacheEntry entry, out LoadPriority priority) {
    if (immediateQueue.Count > 0) {
      entry = immediateQueue.Dequeue();
      priority = LoadPriority.Immediate;
      return true;
    }
    if (warmupQueue.Count > 0) {
      entry = warmupQueue.Dequeue();
      priority = LoadPriority.Warmup;
      return true;
    }
    if (backgroundQueue.Count > 0) {
      entry = backgroundQueue.Dequeue();
      priority = LoadPriority.Background;
      return true;
    }

    entry = null;
    priority = LoadPriority.Background;
    return false;
  }

  static void ClearQueuedFlag(CacheEntry entry) {
    if (entry == null || !entry.isQueued) return;
    entry.isQueued = false;
    if (queuedEntryCount > 0) queuedEntryCount--;
  }

  static void MarkInFlightComplete(CacheEntry entry) {
    if (entry == null || !entry.countedInFlight) return;
    entry.countedInFlight = false;
    if (inFlightLoads > 0) inFlightLoads--;
    MarkSessionEntryCompleted(entry);
  }

  static void MarkSessionEntryCompleted(CacheEntry entry) {
    if (entry == null || sessionExpectedTotal <= 0 || sessionGeneration <= 0) return;
    if (entry.sessionCompletionGeneration == sessionGeneration) return;
    entry.sessionCompletionGeneration = sessionGeneration;
    sessionTotalCompleted++;
  }

  static void EnqueuePendingTextureRegister(CacheEntry entry) {
    if (entry == null || entry.hasTextureRegistration) return;
    pendingTextureRegisterQueue.Enqueue(entry);
  }

  static void ResetFrameCounterIfNeeded() {
    var frame = Time.frameCount;
    if (frame == lastPumpFrame) return;
    lastPumpFrame = frame;
    startedLoadsThisFrame = 0;
  }

  static bool ShouldSkipPumpForCurrentFrame(int maxStarts) {
    if (pendingBudgetMaintain || pendingQueueStateRecord || pendingTextureRegisterQueue.Count > 0) return false;
    if (!StreamingWarmOrchestrator.IsWarmGateRunning && deferredRequests.Count > 0) return false;
    var frame = Time.frameCount;
    if (frame != lastPumpSnapshotFrame) return false;
    if (queuedEntryCount != lastPumpSnapshotQueuedCount) return false;
    if (inFlightLoads != lastPumpSnapshotInFlightCount) return false;
    if (queuedEntryCount <= 0) return true;
    // Never skip when Immediate items are pending: they must not be starved by the frame budget.
    if (immediateQueue.Count > 0) return false;
    return startedLoadsThisFrame >= Math.Max(maxStarts, 1);
  }

  static void CachePumpFrameSnapshot() {
    lastPumpSnapshotFrame = Time.frameCount;
    lastPumpSnapshotQueuedCount = queuedEntryCount;
    lastPumpSnapshotInFlightCount = inFlightLoads;
  }

  static void ReleaseInternal(CacheEntry entry) {
    if (entry == null) return;
    if (entry.pinCount > 0) {
      entry.pinCount--;
    }
    entry.lastAccessTicks = DateTime.UtcNow.Ticks;
  }

  static void MaintainBudget() {
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
    if (SpriteStreamingRuntimeSettings.KeepLoadedSpritesForSession) {
      // Keep richer residency when configured, but never exceed hard cap in runtime.
      return Math.Max(hardBytes, 0);
    }
    return Math.Max(softBytes, 0);
  }

  static bool TryEvictOldestUnpinned() {
    string oldestKey = null;
    CacheEntry oldestEntry = null;

    foreach (var pair in cache) {
      var candidate = pair.Value;
      if (candidate == null) continue;
      if (candidate.pinCount > 0) continue;
      if (!candidate.isDone) continue;
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
      if (state == null || state.leases == null || state.leases.Count <= 0) continue;
      return true;
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
      if (state == null) continue;
      if (state.pinClass != pinClass) continue;
      if (state.leases.Count <= 0) continue;
      ownerDemoteScratch.Add(state);
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
        if (!state.leases.TryGetValue(ownerReleaseAddressScratch[k], out var lease) || lease == null) continue;
        lease.Release();
        state.leases.Remove(ownerReleaseAddressScratch[k]);
        pinDemotions++;
        released++;
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
    foreach (var sprite in entry.spritesByName.Values) {
      RegisterTextureForEntry(entry, sprite);
    }
    if (entry.primarySprite != null) {
      RegisterTextureForEntry(entry, entry.primarySprite);
    }
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
      if (!textureBytesById.TryGetValue(textureId, out var bytes)) continue;
      textureBytesById.Remove(textureId);
      residentBytes -= bytes;
      if (residentBytes < 0) residentBytes = 0;
    }
    entry.registeredTextureIds.Clear();
  }

  static void RegisterTextureForEntry(CacheEntry entry, Sprite sprite) {
    if (entry == null || sprite == null) return;
    var texture = sprite.texture;
    if (texture == null) return;

    var textureId = texture.GetInstanceID();
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
    entry.isEvicted = true;
    ClearQueuedFlag(entry);
    UnregisterTextureContribution(entry);
    ReleaseHandle(entry);
    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
  }

  static void ReleaseHandle(CacheEntry entry) {
    if (entry == null) return;
    MarkInFlightComplete(entry);
    if (entry.handle.IsValid()) {
      Addressables.Release(entry.handle);
    }
    ReleaseLocationHandle(entry);

    entry.handle = default;
    entry.primarySprite = null;
    entry.spritesByName.Clear();
    entry.isDone = false;
    entry.isSuccess = false;
    entry.hasTextureRegistration = false;
    entry.registeredTextureIds.Clear();
    entry.loadStarted = false;
    entry.countedInFlight = false;
    ClearQueuedFlag(entry);
  }

  static CacheSettings GetSettings() {
    if (settingsLoaded) return settings;
    settingsLoaded = true;
    settings = new CacheSettings {
      softTextureBudgetBytes = 1024L * 1024L * 1024L,
      hardTextureBudgetBytes = 1536L * 1024L * 1024L,
      maxAddressableStartsPerFrame = 8,
      loadingOverlayMaxAddressableStartsPerFrame = 24
    };

    var settingsAsset = SpriteStreamingRuntimeSettings.Asset;
    if (settingsAsset != null) {
      settings.softTextureBudgetBytes = settingsAsset.SoftTextureBudgetBytes;
      settings.hardTextureBudgetBytes = settingsAsset.HardTextureBudgetBytes;
      settings.maxAddressableStartsPerFrame = Math.Max(SpriteStreamingRuntimeSettings.MaxAddressableStartsPerFrame, 1);
    }

    var overlayConfigured = SpriteStreamingRuntimeSettings.LoadingOverlayMaxAddressableStartsPerFrame;

    if (overlayConfigured <= 0) overlayConfigured = 24;

    settings.loadingOverlayMaxAddressableStartsPerFrame = Math.Max(
      overlayConfigured,
      settings.maxAddressableStartsPerFrame
    );

    return settings;
  }

  static bool TryGetSpriteFromEntry(CacheEntry entry, string spriteName, out Sprite sprite) {
    sprite = null;
    if (entry == null || !entry.isDone || !entry.isSuccess) return false;
    if (string.IsNullOrWhiteSpace(spriteName)) {
      sprite = entry.primarySprite;
      return sprite != null;
    }
    var normalizedName = spriteName.Trim();
    if (entry.spritesByName.TryGetValue(normalizedName, out sprite) && sprite != null) {
      return true;
    }

    if (!SpriteSliceAddressUtility.TryExtractNumericLabelValue(normalizedName, out var numericLabelValue)) return false;
    return TryGetSpriteByNumericLabel(entry, numericLabelValue, out sprite);
  }

  static bool TryGetSpriteByNumericLabel(CacheEntry entry, string numericLabelValue, out Sprite sprite) {
    sprite = null;
    if (entry == null || entry.spritesByName == null || entry.spritesByName.Count <= 0) return false;
    if (string.IsNullOrWhiteSpace(numericLabelValue)) return false;

    Sprite match = null;
    foreach (var pair in entry.spritesByName) {
      if (!SpriteSliceAddressUtility.TryExtractNumericLabelValue(pair.Key, out var candidateNumericValue)) continue;
      if (!string.Equals(candidateNumericValue, numericLabelValue, StringComparison.Ordinal)) continue;

      if (match != null && match != pair.Value) {
        sprite = null;
        return false;
      }

      match = pair.Value;
    }

    sprite = match;
    return sprite != null;
  }

  static string NormalizeAddress(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    var normalized = value.Trim();
    if (SpriteSliceAddressUtility.TryParseSliceAddress(normalized, out var atlasAssetPath, out _)) {
      return string.IsNullOrWhiteSpace(atlasAssetPath) ? "" : atlasAssetPath.Trim();
    }
    return normalized;
  }

  static string NormalizeOwnerId(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  static int GetPinClassBudget(PinClass pinClass) {
    switch (pinClass) {
      case PinClass.Player:
        return Math.Max(SpriteStreamingRuntimeSettings.PinBudgetPlayerAddresses, 1);
      case PinClass.Enemy:
        return Math.Max(SpriteStreamingRuntimeSettings.PinBudgetEnemyAddresses, 1);
      case PinClass.UI:
        return Math.Max(SpriteStreamingRuntimeSettings.PinBudgetUiAddresses, 1);
      case PinClass.Effect:
        return Math.Max(SpriteStreamingRuntimeSettings.PinBudgetEffectAddresses, 1);
      case PinClass.WarmGate:
        // Warm-gate owner pins are capped by caller-provided address lists.
        // Keep class budget effectively unbounded so first-load resident sets
        // are not trimmed before gameplay unlock.
        return int.MaxValue;
      default:
        return int.MaxValue;
    }
  }

  static int CountPinnedAddressesForClass(PinClass pinClass) {
    var count = 0;
    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state == null) continue;
      if (state.pinClass != pinClass) continue;
      count += Math.Max(state.leases.Count, 0);
    }
    return count;
  }

  static bool EnsurePinClassBudgetCapacity(PinClass pinClass, string protectedOwnerId, int classBudget) {
    if (classBudget <= 0) return true;
    var normalizedProtectedOwner = NormalizeOwnerId(protectedOwnerId);

    var used = CountPinnedAddressesForClass(pinClass);
    if (used < classBudget) return true;

    while (used >= classBudget) {
      if (!TryReleaseOldestLeaseFromClass(pinClass, normalizedProtectedOwner)) {
        return false;
      }
      used--;
    }

    return true;
  }

  static bool TryReleaseOldestLeaseFromClass(PinClass pinClass, string protectedOwnerId) {
    OwnerPinState ownerCandidate = null;
    string leaseKey = null;
    long oldestTicks = long.MaxValue;

    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state == null) continue;
      if (state.pinClass != pinClass) continue;
      if (state.leases == null || state.leases.Count <= 0) continue;
      if (string.Equals(state.ownerId, protectedOwnerId, StringComparison.OrdinalIgnoreCase)) continue;
      if (state.lastRefreshTicks > oldestTicks) continue;

      foreach (var leasePair in state.leases) {
        leaseKey = leasePair.Key;
        break;
      }
      if (string.IsNullOrWhiteSpace(leaseKey)) continue;
      oldestTicks = state.lastRefreshTicks;
      ownerCandidate = state;
    }

    if (ownerCandidate == null || string.IsNullOrWhiteSpace(leaseKey)) return false;
    if (!ownerCandidate.leases.TryGetValue(leaseKey, out var lease) || lease == null) return false;
    lease.Release();
    ownerCandidate.leases.Remove(leaseKey);
    if (ownerCandidate.leases.Count <= 0) {
      ownerPins.Remove(ownerCandidate.ownerId);
    }
    return true;
  }

  // Accepts a pre-normalized address set; caller is responsible for normalization.
  static bool AddressesMatchExistingLeases(Dictionary<string, Lease> existingLeases, HashSet<string> normalizedAddresses) {
    if (existingLeases == null || normalizedAddresses == null) return false;
    if (existingLeases.Count != normalizedAddresses.Count) return false;

    foreach (var address in normalizedAddresses) {
      if (!existingLeases.ContainsKey(address)) return false;
    }

    return true;
  }

  static void RecordPinStateIfEnabled() {
    if (!SpriteStreamingDiagnostics.Enabled) return;
    SpriteStreamingDiagnostics.RecordPinState(GetPinSnapshot());
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
    bool warmGateManaged
  ) {
    if (entry == null) return;
    // If the entry is already done (cached), we count it towards the session progress immediately.
    if (entry.isDone) {
      MarkSessionEntryCompleted(entry);
    }

    entry.lastAccessTicks = frameAccessTicks != 0 ? frameAccessTicks : DateTime.UtcNow.Ticks;
    if (warmGateManaged &&
        TryPromoteDeferredRequest(entry.address, priority, out var promotedPriority, out _)) {
      priority = promotedPriority;
    }

    if (pinEntry) entry.pinCount++;

    if (!warmGateManaged && ShouldDeferNonManagedWarmGateRequest(entry)) {
      EnqueueDeferredRequest(entry.address, priority, pinEntry);
      if (!runPumpAndMaintain) return;
      Pump();
      return;
    }

    EnqueueLoad(entry, priority);
    if (!runPumpAndMaintain) return;
    Pump();
    MaintainBudget();
  }

  static bool ShouldDeferNonManagedWarmGateRequest(CacheEntry entry) {
    if (entry == null) return false;
    if (entry.isDone || entry.loadStarted || entry.isEvicted || entry.isQueued) return false;
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive) return false;
    if (StreamingWarmOrchestrator.IsWarmGateRunning) return true;
    // Keep arbitration active while overlay is still up and deferred backlog is draining.
    // This prevents a one-frame queue explosion when warm gate flips off.
    return deferredRequests.Count > 0;
  }

  static void EnqueueDeferredRequest(string normalizedAddress, LoadPriority priority, bool pinEntry) {
    if (string.IsNullOrWhiteSpace(normalizedAddress)) return;
    deferredRequestCount++;
    if (!deferredRequests.TryGetValue(normalizedAddress, out var state)) {
      state = new DeferredRequestState {
        priority = priority,
        pinEntry = pinEntry
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
    deferredRequests[normalizedAddress] = state;

    if (priorityChanged) {
      EnqueueDeferredByPriority(normalizedAddress, mergedPriority);
    }
  }

  static bool TryPromoteDeferredRequest(
    string normalizedAddress,
    LoadPriority requestedPriority,
    out LoadPriority effectivePriority,
    out bool deferredPinEntry
  ) {
    effectivePriority = requestedPriority;
    deferredPinEntry = false;
    if (string.IsNullOrWhiteSpace(normalizedAddress)) return false;
    if (!deferredRequests.TryGetValue(normalizedAddress, out var deferredState)) return false;

    deferredRequests.Remove(normalizedAddress);
    deferredPromotedCount++;
    if (deferredState.priority < effectivePriority) {
      effectivePriority = deferredState.priority;
    }
    deferredPinEntry = deferredState.pinEntry;
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
    if (deferredBackgroundQueue.Count > 0) {
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
      EnqueueLoad(entry, deferredState.priority);
      deferredFlushedThisFrame++;
    }
  }

  static void ProcessPendingCompletionFollowups() {
    if (!pendingBudgetMaintain && !pendingQueueStateRecord && pendingTextureRegisterQueue.Count <= 0
#if UNITY_EDITOR
      && pendingEditorAtlasSupplementQueue.Count <= 0
#endif
    ) return;
    var frame = Time.frameCount;
    if (completionFollowupFrame == frame) return;
    completionFollowupFrame = frame;
    var diagnosticsEnabled = ShouldLogLoadCompletionDiagnostics();
    var measureFollowupCost = diagnosticsEnabled || SpriteStreamingLoadingState.IsLoadingOverlayActive;
    var followupStartedAt = measureFollowupCost ? Time.realtimeSinceStartup : 0f;

#if UNITY_EDITOR
    if (pendingEditorAtlasSupplementQueue.Count > 0) {
      ProcessPendingEditorAtlasSupplements();
    }
#endif

    if (pendingTextureRegisterQueue.Count > 0) {
      ProcessPendingTextureRegistrations();
    }

    var followupMs = measureFollowupCost ? ComputeElapsedMs(followupStartedAt) : 0f;
    if (measureFollowupCost) {
      if (pendingTextureRegisterQueue.Count > 0) {
        pendingBudgetMaintain = true;
      }
      UpdateCompletionPressureFromCosts(followupMs, followupMs, 0f);
      if (diagnosticsEnabled && followupMs > 0.1f) {
        RecordLoadCompletionFrameCost(followupMs, followupMs, 0f, "(completion_followups)");
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
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      return CompletionRegisterOverlayBudgetPerFrame;
    }
    if (queuedEntryCount > 0 || inFlightLoads > 0 || deferredRequests.Count > 0) {
      return CompletionRegisterLoadingBudgetPerFrame;
    }
    return CompletionRegisterGameplayBudgetPerFrame;
  }

  static void ProcessPendingTextureRegistrations() {
    var budget = Math.Max(ResolveCompletionRegisterBudgetPerFrame(), 1);
    var processed = 0;
    while (processed < budget && pendingTextureRegisterQueue.Count > 0) {
      var entry = pendingTextureRegisterQueue.Dequeue();
      if (entry == null || entry.isEvicted || entry.hasTextureRegistration) continue;
      if (!entry.isDone || !entry.isSuccess || entry.primarySprite == null) continue;
      RegisterTextureContribution(entry);
      processed++;
    }
  }

  static void TryExpandAtlasOnSliceRequest(string requestedAddress, LoadPriority requestPriority, bool runPumpAndMaintain) {
    if (!SpriteStreamingRuntimeSettings.EnableAtlasExpansionOnSliceRequest) return;
    if (!Application.isPlaying) return;
    var loadingContextActive = StreamingWarmOrchestrator.IsWarmGateRunning || SpriteStreamingLoadingState.IsLoadingOverlayActive;
    if (!loadingContextActive) return;
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
      var siblingAddress = NormalizeAddress(atlasSiblingAddressScratch[i]);
      if (string.IsNullOrWhiteSpace(siblingAddress)) continue;
      if (string.Equals(siblingAddress, requestedAddress, StringComparison.OrdinalIgnoreCase)) continue;
      if (!TryConsumeAtlasExpansionAddressBudget()) break;
      RecordRequestForFrame(isAcquire: false, sourceTag: "AtlasExpansion");
      var siblingEntry = ResolveEntryForLoad(siblingAddress, out var siblingHit);
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

  static bool TryConsumeAtlasExpansionBudget() {
    var frame = Time.frameCount;
    if (atlasExpansionFrame != frame) {
      atlasExpansionFrame = frame;
      atlasExpansionCountThisFrame = 0;
    }
    var loadingContextActive = StreamingWarmOrchestrator.IsWarmGateRunning || SpriteStreamingLoadingState.IsLoadingOverlayActive;
    var maxPerFrame = loadingContextActive ? AtlasExpansionMaxPerFrameLoading : AtlasExpansionMaxPerFrame;
    if (atlasExpansionCountThisFrame >= maxPerFrame) return false;
    atlasExpansionCountThisFrame++;
    return true;
  }

  static bool TryConsumeAtlasExpansionAddressBudget() {
    var frame = Time.frameCount;
    if (atlasExpansionAddressBudgetFrame != frame) {
      atlasExpansionAddressBudgetFrame = frame;
      atlasExpansionAddressesQueuedThisFrame = 0;
    }
    var loadingContextActive = StreamingWarmOrchestrator.IsWarmGateRunning || SpriteStreamingLoadingState.IsLoadingOverlayActive;
    var maxAddressesPerFrame = loadingContextActive ? AtlasExpansionMaxAddressesPerFrameLoading : AtlasExpansionMaxAddressesPerFrame;
    if (atlasExpansionAddressesQueuedThisFrame >= maxAddressesPerFrame) return false;
    atlasExpansionAddressesQueuedThisFrame++;
    return true;
  }

  static void RecordRequestForFrame(bool isAcquire, string sourceTag) {
    if (!ShouldLogRequestFrameDiagnostics()) return;
    EnsureRequestDiagFrameCurrent();
    if (isAcquire) requestDiagAcquireCalls++;
    else requestDiagWarmupCalls++;
    RecordRequestDiagSource(sourceTag);
  }

  static void RecordQueueAddForFrame() {
    if (!ShouldLogRequestFrameDiagnostics()) return;
    EnsureRequestDiagFrameCurrent();
    requestDiagQueueAdds++;
  }

  static void RecordNewEntryForFrame() {
    if (!ShouldLogRequestFrameDiagnostics()) return;
    EnsureRequestDiagFrameCurrent();
    requestDiagNewEntries++;
  }

  static void RecordLoadCompleteLatency(float latencyMs) {
    const int window = 64;
    loadCompleteLatencyRollingCount = Math.Min(loadCompleteLatencyRollingCount + 1, window);
    var alpha = 1f / loadCompleteLatencyRollingCount;
    loadCompleteLatencyRollingAvgMs += alpha * (latencyMs - loadCompleteLatencyRollingAvgMs);
  }

  static void RecordPumpForFrame(float pumpMs, int startedLoads) {
    if (!ShouldLogRequestFrameDiagnostics()) return;
    EnsureRequestDiagFrameCurrent();
    requestDiagPumpCalls++;
    requestDiagPumpTotalMs += Mathf.Max(pumpMs, 0f);
    requestDiagStartedLoads += Math.Max(startedLoads, 0);
  }

  static void EnsureRequestDiagFrameCurrent() {
    var frame = Time.frameCount;
    if (requestDiagFrame == frame) return;
    FlushRequestDiagFrame();
    requestDiagFrame = frame;
    requestDiagAcquireCalls = 0;
    requestDiagWarmupCalls = 0;
    requestDiagQueueAdds = 0;
    requestDiagNewEntries = 0;
    requestDiagPumpCalls = 0;
    requestDiagStartedLoads = 0;
    requestDiagPumpTotalMs = 0f;
    requestDiagSourceCounts.Clear();
  }

  static void FlushRequestDiagFrame() {
    if (!ShouldLogRequestFrameDiagnostics()) return;
    if (requestDiagFrame < 0) return;

    var requestTotal = requestDiagAcquireCalls + requestDiagWarmupCalls;
    var shouldReport = requestTotal >= RequestDiagRequestThreshold ||
      requestDiagQueueAdds >= RequestDiagQueueAddsThreshold ||
      requestDiagNewEntries >= RequestDiagNewEntriesThreshold ||
      requestDiagPumpTotalMs >= RequestDiagPumpMsThreshold;
    if (!shouldReport) return;

    var topSources = BuildTopRequestDiagSources(maxSources: 5);
    Debug.LogWarning(
      "[TextureResidencyCache][RequestDiag] frame=" + requestDiagFrame +
      " requests=" + requestTotal +
      " acquire=" + requestDiagAcquireCalls +
      " warmup=" + requestDiagWarmupCalls +
      " queue_adds=" + requestDiagQueueAdds +
      " new_entries=" + requestDiagNewEntries +
      " pump_calls=" + requestDiagPumpCalls +
      " pump_ms=" + requestDiagPumpTotalMs.ToString("0.0") +
      " started_loads=" + requestDiagStartedLoads +
      " top_sources=" + topSources
    );

  }

  static void RecordRequestDiagSource(string sourceTag) {
    var normalized = string.IsNullOrWhiteSpace(sourceTag) ? "(unknown)" : sourceTag.Trim();
    if (requestDiagSourceCounts.TryGetValue(normalized, out var existing)) {
      requestDiagSourceCounts[normalized] = existing + 1;
      return;
    }
    requestDiagSourceCounts[normalized] = 1;
  }

  static string BuildTopRequestDiagSources(int maxSources) {
    if (requestDiagSourceCounts.Count <= 0) return "(none)";
    maxSources = Math.Max(maxSources, 1);
    var topSources = new List<KeyValuePair<string, int>>(maxSources);
    foreach (var pair in requestDiagSourceCounts) {
      if (topSources.Count < maxSources) {
        topSources.Add(pair);
        continue;
      }
      var weakestIndex = 0;
      for (var i = 1; i < topSources.Count; i++) {
        if (topSources[i].Value < topSources[weakestIndex].Value) weakestIndex = i;
      }
      if (pair.Value <= topSources[weakestIndex].Value) continue;
      topSources[weakestIndex] = pair;
    }

    topSources.Sort((left, right) => right.Value.CompareTo(left.Value));
    var parts = new List<string>(topSources.Count);
    for (var i = 0; i < topSources.Count; i++) {
      parts.Add(topSources[i].Key + "=" + topSources[i].Value);
    }
    return string.Join(", ", parts);
  }

  static string BuildRequestDiagSourceTag(string callerMemberName, string callerFilePath, int callerLineNumber) {
    var member = string.IsNullOrWhiteSpace(callerMemberName) ? "(unknown)" : callerMemberName.Trim();
    var file = string.IsNullOrWhiteSpace(callerFilePath) ? "" : Path.GetFileName(callerFilePath);
    if (string.IsNullOrWhiteSpace(file)) return member;
    var line = Math.Max(callerLineNumber, 0);
    return file + ":" + line + "/" + member;
  }

  static string ResolveAtlasExpansionContext() {
    if (StreamingWarmOrchestrator.IsWarmGateRunning) return "warm_gate";
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) return "loading_overlay";
    return "live";
  }

  static bool IsLoadingScreenStreamingContextActive() {
    return StreamingWarmOrchestrator.IsWarmGateRunning || SpriteStreamingLoadingState.IsLoadingOverlayActive;
  }

  static bool ShouldLogRequestFrameDiagnostics() {
    if (!enableRequestFrameDiagnostics) return false;
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!IsLoadingScreenStreamingContextActive()) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static bool ShouldLogAtlasExpansion() {
    if (!SpriteStreamingRuntimeSettings.EnableAtlasExpansionLogs) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static bool ShouldLogLoadCompletionDiagnostics() {
    if (!enableLoadCompletionDiagnostics) return false;
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!IsLoadingScreenStreamingContextActive()) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static float ResolveLoadCompletionSlowThresholdMs() {
    return Mathf.Max(loadCompletionSlowStepThresholdMs, 1f);
  }

  static float ResolveLoadCompletionFrameSlowThresholdMs() {
    return Mathf.Max(ResolveLoadCompletionSlowThresholdMs() * 4f, 100f);
  }

  static bool IsCompletionPressureActive() {
    return Time.frameCount <= completionPressureUntilFrame;
  }

  static void UpdateCompletionPressureFromCosts(float totalMs, float registerMs, float maintainMs) {
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive) return;
    var slowThresholdMs = ResolveLoadCompletionSlowThresholdMs();
    if (totalMs < slowThresholdMs && registerMs < slowThresholdMs && maintainMs < slowThresholdMs) return;
    completionPressureUntilFrame = Math.Max(completionPressureUntilFrame, Time.frameCount + CompletionPressureCooldownFrames);
  }

  static float ComputeElapsedMs(float startedAt) {
    return Mathf.Max((Time.realtimeSinceStartup - startedAt) * 1000f, 0f);
  }

  static void RecordLoadCompletionFrameCost(float totalMs, float registerMs, float maintainMs, string address) {
    if (!ShouldLogLoadCompletionDiagnostics()) return;

    var frame = Time.frameCount;
    if (loadCompletionDiagFrame != frame) {
      loadCompletionDiagFrame = frame;
      loadCompletionDiagFrameTotalMs = 0f;
      loadCompletionDiagFrameRegisterMs = 0f;
      loadCompletionDiagFrameMaintainMs = 0f;
      loadCompletionDiagFrameCount = 0;
      loadCompletionDiagFrameReported = false;
    }

    loadCompletionDiagFrameTotalMs += Mathf.Max(totalMs, 0f);
    loadCompletionDiagFrameRegisterMs += Mathf.Max(registerMs, 0f);
    loadCompletionDiagFrameMaintainMs += Mathf.Max(maintainMs, 0f);
    loadCompletionDiagFrameCount++;

    if (loadCompletionDiagFrameReported) return;
    var thresholdMs = ResolveLoadCompletionFrameSlowThresholdMs();
    if (loadCompletionDiagFrameTotalMs < thresholdMs) return;

    loadCompletionDiagFrameReported = true;

  }

  static bool ShouldLogLoadingScreenAddressableLoad() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableAddressableLoadLogs) return false;
    var duringWarmGate = StreamingWarmOrchestrator.IsWarmGateRunning;
    var duringOverlay = SpriteStreamingRuntimeSettings.LogAddressableLoadsOutsideWarmGate &&
      SpriteStreamingLoadingState.IsLoadingOverlayActive;
    if (!duringWarmGate && !duringOverlay) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }
}
