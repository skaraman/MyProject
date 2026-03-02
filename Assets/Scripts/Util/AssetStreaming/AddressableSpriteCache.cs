using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

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
    Effect = 3
  }

  sealed class OwnerPinState {
    public string ownerId;
    public PinClass pinClass;
    public readonly Dictionary<string, Lease> leases = new(StringComparer.OrdinalIgnoreCase);
    public long lastRefreshTicks;
  }

  internal sealed class CacheEntry {
    public string address;
    public AsyncOperationHandle<Sprite> handle;
    public Sprite sprite;
    public int pinCount;
    public bool isDone;
    public bool isSuccess;
    public bool isEvicted;
    public long lastAccessTicks;
    public int textureId;
    public bool hasTextureRegistration;
    public bool loadStarted;
    public bool countedInFlight;
    public bool isQueued;
    public LoadPriority queuedPriority;
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

  public sealed class Lease {
    CacheEntry entry;
    bool released;

    internal Lease(CacheEntry entry) {
      this.entry = entry;
    }

    public bool IsDone => entry == null || entry.isDone;
    public bool IsSuccess => entry != null && entry.isDone && entry.isSuccess && entry.sprite != null;
    public Sprite Sprite => IsSuccess ? entry.sprite : null;
    public string Address => entry != null ? entry.address : "";

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

  static readonly Dictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<int, int> textureRefCounts = new();
  static readonly Dictionary<int, long> textureBytesById = new();
  static readonly Queue<CacheEntry> immediateQueue = new();
  static readonly Queue<CacheEntry> warmupQueue = new();
  static readonly Queue<CacheEntry> backgroundQueue = new();
  static readonly Dictionary<string, OwnerPinState> ownerPins = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> desiredOwnerAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<string> ownerReleaseAddressScratch = new(256);
  static readonly HashSet<string> expandedAtlasKeys = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, int> atlasExpansionRetryFrames = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<string> atlasSiblingAddressScratch = new(512);

  static CacheSettings settings;
  static bool settingsLoaded;
  static long residentBytes;
  static int queuedEntryCount;
  static int inFlightLoads;
  static int startedLoadsThisFrame;
  static int lastPumpFrame = -1;
  static long cacheHits;
  static long cacheMisses;
  static int pinDemotions;
  static int pinClassBudgetHitCount;
  static int pinClassBudgetDroppedAddresses;
  static int ownerPinMutationDepth;
  static int lastBudgetMaintainFrame = -1;

  const int MaxBudgetDemotionPassesPerFrame = 3;
  const int MaxBudgetEvictionsPerFrame = 24;
  const int AtlasExpansionRetryFrameWindow = 30;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    settingsLoaded = false;
    settings = default;
    residentBytes = 0;
    queuedEntryCount = 0;
    inFlightLoads = 0;
    startedLoadsThisFrame = 0;
    lastPumpFrame = -1;
    cacheHits = 0;
    cacheMisses = 0;
    pinDemotions = 0;
    pinClassBudgetHitCount = 0;
    pinClassBudgetDroppedAddresses = 0;
    ownerPinMutationDepth = 0;
    lastBudgetMaintainFrame = -1;
    textureRefCounts.Clear();
    textureBytesById.Clear();
    immediateQueue.Clear();
    warmupQueue.Clear();
    backgroundQueue.Clear();
    ownerPins.Clear();
    expandedAtlasKeys.Clear();
    atlasExpansionRetryFrames.Clear();
    atlasSiblingAddressScratch.Clear();
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

  public static bool IsQueueIdle(bool pump = true) {
    return GetQueueSnapshot(pump).IsIdle;
  }

  public static Lease AcquireAsync(string address, LoadPriority priority = LoadPriority.Immediate) {
    var normalizedAddress = NormalizeAddress(address);
    if (string.IsNullOrEmpty(normalizedAddress)) return null;
    return AcquireAsyncNormalized(normalizedAddress, priority, runPumpAndMaintain: true);
  }

  static Lease AcquireAsyncNormalized(string normalizedAddress, LoadPriority priority, bool runPumpAndMaintain) {
    var entry = ResolveEntryForLoad(normalizedAddress, out var hit);
    RecordLookup(hit);
    QueueEntryForLoad(entry, priority, pinEntry: true, runPumpAndMaintain);
    TryExpandAtlasOnSliceRequest(normalizedAddress, runPumpAndMaintain);
    return new Lease(entry);
  }

  public static void RequestLoad(string address, LoadPriority priority = LoadPriority.Warmup) {
    var normalizedAddress = NormalizeAddress(address);
    if (string.IsNullOrEmpty(normalizedAddress)) return;

    var entry = ResolveEntryForLoad(normalizedAddress, out var hit);
    RecordLookup(hit);
    QueueEntryForLoad(entry, priority, pinEntry: false, runPumpAndMaintain: true);
    TryExpandAtlasOnSliceRequest(normalizedAddress, runPumpAndMaintain: true);
  }

  public static void RequestLoadBatch(
    IEnumerable<string> addresses,
    LoadPriority priority = LoadPriority.Warmup,
    bool allowAtlasExpansion = true
  ) {
    if (addresses == null) return;

    foreach (var address in addresses) {
      var normalizedAddress = NormalizeAddress(address);
      if (string.IsNullOrEmpty(normalizedAddress)) continue;

      var entry = ResolveEntryForLoad(normalizedAddress, out var hit);
      RecordLookup(hit);
      QueueEntryForLoad(entry, priority, pinEntry: false, runPumpAndMaintain: false);
      if (allowAtlasExpansion) {
        TryExpandAtlasOnSliceRequest(normalizedAddress, runPumpAndMaintain: false);
      }
    }

    Pump();
    MaintainBudget();
  }

  public static IEnumerator RequestLoadBatchThrottled(
    IEnumerable<string> addresses,
    LoadPriority priority = LoadPriority.Warmup,
    bool allowAtlasExpansion = true,
    int enqueueBudgetPerFrame = 128
  ) {
    if (addresses == null) yield break;

    // Keep per-frame enqueues in the 50–200 window suggested by AGENTS guidance.
    enqueueBudgetPerFrame = Mathf.Clamp(enqueueBudgetPerFrame, 50, 200);
    var remainingThisFrame = enqueueBudgetPerFrame;

    foreach (var address in addresses) {
      var normalizedAddress = NormalizeAddress(address);
      if (string.IsNullOrEmpty(normalizedAddress)) continue;

      var entry = ResolveEntryForLoad(normalizedAddress, out var hit);
      RecordLookup(hit);
      QueueEntryForLoad(entry, priority, pinEntry: false, runPumpAndMaintain: false);
      if (allowAtlasExpansion) {
        TryExpandAtlasOnSliceRequest(normalizedAddress, runPumpAndMaintain: false);
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

  public static bool TryGetLoadedSprite(string address, out Sprite sprite) {
    sprite = null;
    var normalizedAddress = NormalizeAddress(address);
    if (string.IsNullOrEmpty(normalizedAddress)) return false;

    Pump();
    if (!cache.TryGetValue(normalizedAddress, out var entry) || entry == null) return false;
    if (!entry.isDone || !entry.isSuccess || entry.sprite == null) return false;

    entry.lastAccessTicks = DateTime.UtcNow.Ticks;
    sprite = entry.sprite;
    return true;
  }

  public static bool IsReady(string address, bool pump = true) {
    var normalizedAddress = NormalizeAddress(address);
    if (string.IsNullOrEmpty(normalizedAddress)) return false;

    if (pump) {
      Pump();
    }
    if (!cache.TryGetValue(normalizedAddress, out var entry) || entry == null) return false;
    return entry.isDone && entry.isSuccess && entry.sprite != null;
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

    if (ownerPins.TryGetValue(normalizedOwnerId, out var existingState) &&
        existingState != null &&
        AddressesMatchExistingLeases(existingState.leases, addresses)) {
      existingState.pinClass = pinClass;
      existingState.lastRefreshTicks = DateTime.UtcNow.Ticks;
      return;
    }

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
        var keys = new List<string>(state.leases.Keys);
        for (var i = 0; i < keys.Count && overflow > 0; i++) {
          if (!state.leases.TryGetValue(keys[i], out var trimLease) || trimLease == null) continue;
          trimLease.Release();
          state.leases.Remove(keys[i]);
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
        var lease = AcquireAsyncNormalized(desiredAddress, priority, runPumpAndMaintain: false);
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
    ResetFrameCounterIfNeeded();
    var cfg = GetSettings();
    var maxStarts = ResolveMaxStartsPerFrame(cfg);

    while (startedLoadsThisFrame < maxStarts) {
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

    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
    RecordPinStateIfEnabled();
  }

  static int ResolveMaxStartsPerFrame(CacheSettings cfg) {
    var baseStarts = Math.Max(cfg.maxAddressableStartsPerFrame, 1);
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive) return baseStarts;
    var overlayStarts = Math.Max(cfg.loadingOverlayMaxAddressableStartsPerFrame, baseStarts);
    return overlayStarts;
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
    immediateQueue.Clear();
    warmupQueue.Clear();
    backgroundQueue.Clear();
    textureRefCounts.Clear();
    textureBytesById.Clear();
    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
    RecordPinStateIfEnabled();
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
    entry.sprite = null;
    entry.lastAccessTicks = DateTime.UtcNow.Ticks;
    entry.handle = Addressables.LoadAssetAsync<Sprite>(entry.address);
    entry.countedInFlight = true;
    inFlightLoads++;
    SpriteStreamingDiagnostics.RecordLoadStarted();
    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
    if (ShouldLogLoadingScreenAddressableLoad()) {
      Debug.Log(
        "[TextureResidencyCache] StartLoad address='" + entry.address +
        "' priority=" + entry.queuedPriority +
        " queued=" + queuedEntryCount +
        " in_flight=" + inFlightLoads
      );
    }

    entry.handle.Completed += op => {
      MarkInFlightComplete(entry);
      entry.loadStarted = false;

      if (entry.isEvicted) {
        SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
        return;
      }

      entry.isDone = true;
      entry.isSuccess = op.Status == AsyncOperationStatus.Succeeded && op.Result != null;
      entry.sprite = entry.isSuccess ? op.Result : null;
      entry.lastAccessTicks = DateTime.UtcNow.Ticks;

      if (!entry.isSuccess) {
        Debug.LogError("[TextureResidencyCache] Failed to load sprite address '" + entry.address + "'.");
      }
      else {
        RegisterTextureContribution(entry);
      }

      if (ShouldLogLoadingScreenAddressableLoad()) {
        Debug.Log(
          "[TextureResidencyCache] CompleteLoad address='" + entry.address +
          "' success=" + entry.isSuccess +
          " queued=" + queuedEntryCount +
          " in_flight=" + inFlightLoads
        );
      }

      MaintainBudget();
      SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
    };
  }

  static void EnqueueLoad(CacheEntry entry, LoadPriority priority) {
    if (entry == null || entry.isEvicted || entry.isDone || entry.loadStarted) return;

    if (!entry.isQueued) {
      entry.isQueued = true;
      entry.queuedPriority = priority;
      queuedEntryCount++;
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
  }

  static void ResetFrameCounterIfNeeded() {
    var frame = Time.frameCount;
    if (frame == lastPumpFrame) return;
    lastPumpFrame = frame;
    startedLoadsThisFrame = 0;
  }

  static void ReleaseInternal(CacheEntry entry) {
    if (entry == null) return;
    if (entry.pinCount > 0) {
      entry.pinCount--;
    }
    entry.lastAccessTicks = DateTime.UtcNow.Ticks;
  }

  static void MaintainBudget() {
    if (SpriteStreamingRuntimeSettings.KeepLoadedSpritesForSession) return;
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

    var targetBytes = Math.Max(softBytes, 0);
    var evictions = 0;
    while (residentBytes > targetBytes && evictions < MaxBudgetEvictionsPerFrame) {
      if (!TryEvictOldestUnpinned()) break;
      evictions++;
    }
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

    var owners = new List<OwnerPinState>();
    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state == null) continue;
      if (state.pinClass != pinClass) continue;
      if (state.leases.Count <= 0) continue;
      owners.Add(state);
    }

    if (owners.Count == 0) return 0;
    owners.Sort((left, right) => left.lastRefreshTicks.CompareTo(right.lastRefreshTicks));

    var released = 0;
    for (var i = 0; i < owners.Count; i++) {
      var state = owners[i];
      if (state == null || state.leases.Count <= 0) continue;

      var keys = new List<string>(state.leases.Keys);
      for (var k = 0; k < keys.Count && released < maxReleases; k++) {
        if (!state.leases.TryGetValue(keys[k], out var lease) || lease == null) continue;
        lease.Release();
        state.leases.Remove(keys[k]);
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
    if (!entry.isDone || !entry.isSuccess || entry.sprite == null) return;
    var texture = entry.sprite.texture;
    if (texture == null) return;

    var textureId = texture.GetInstanceID();
    entry.textureId = textureId;
    entry.hasTextureRegistration = true;

    if (!textureRefCounts.TryGetValue(textureId, out var refs) || refs <= 0) {
      textureRefCounts[textureId] = 1;
      var bytes = EstimateTextureBytes(texture);
      textureBytesById[textureId] = bytes;
      residentBytes += bytes;
      return;
    }

    textureRefCounts[textureId] = refs + 1;
  }

  static void UnregisterTextureContribution(CacheEntry entry) {
    if (entry == null || !entry.hasTextureRegistration) return;
    entry.hasTextureRegistration = false;

    var textureId = entry.textureId;
    entry.textureId = 0;
    if (!textureRefCounts.TryGetValue(textureId, out var refs) || refs <= 0) return;

    refs--;
    if (refs > 0) {
      textureRefCounts[textureId] = refs;
      return;
    }

    textureRefCounts.Remove(textureId);
    if (!textureBytesById.TryGetValue(textureId, out var bytes)) return;
    textureBytesById.Remove(textureId);
    residentBytes -= bytes;
    if (residentBytes < 0) residentBytes = 0;
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

    entry.handle = default;
    entry.sprite = null;
    entry.isDone = false;
    entry.isSuccess = false;
    entry.textureId = 0;
    entry.hasTextureRegistration = false;
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
      loadingOverlayMaxAddressableStartsPerFrame = 16
    };

    var settingsAsset = SpriteStreamingRuntimeSettings.Asset;
    if (settingsAsset != null) {
      settings.softTextureBudgetBytes = settingsAsset.SoftTextureBudgetBytes;
      settings.hardTextureBudgetBytes = settingsAsset.HardTextureBudgetBytes;
      settings.maxAddressableStartsPerFrame = Math.Max(settingsAsset.maxAddressableStartsPerFrame, 1);
    }

    var overlayConfigured = settingsAsset != null
      ? settingsAsset.loadingOverlayMaxAddressableStartsPerFrame
      : SpriteStreamingRuntimeSettings.LoadingOverlayMaxAddressableStartsPerFrame;

    if (overlayConfigured <= 0) overlayConfigured = 16;

    settings.loadingOverlayMaxAddressableStartsPerFrame = Math.Max(
      overlayConfigured,
      settings.maxAddressableStartsPerFrame
    );

    return settings;
  }

  static string NormalizeAddress(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
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
      used = CountPinnedAddressesForClass(pinClass);
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

  static bool AddressesMatchExistingLeases(Dictionary<string, Lease> existingLeases, List<string> addresses) {
    if (existingLeases == null || addresses == null) return false;
    if (existingLeases.Count != addresses.Count) return false;

    for (var i = 0; i < addresses.Count; i++) {
      var normalized = NormalizeAddress(addresses[i]);
      if (string.IsNullOrWhiteSpace(normalized)) return false;
      if (!existingLeases.ContainsKey(normalized)) return false;
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
      return entry;
    }

    if (entry.isDone && !entry.isSuccess) {
      Evict(normalizedAddress, entry);
      entry = CreateEntry(normalizedAddress);
      cache[normalizedAddress] = entry;
      return entry;
    }

    if (entry.isDone && entry.isSuccess && entry.sprite != null) {
      hit = true;
    }
    return entry;
  }

  static void RecordLookup(bool hit) {
    if (hit) cacheHits++;
    else cacheMisses++;
    SpriteStreamingDiagnostics.RecordCacheLookup(hit);
  }

  static void QueueEntryForLoad(CacheEntry entry, LoadPriority priority, bool pinEntry, bool runPumpAndMaintain) {
    if (entry == null) return;
    if (pinEntry) entry.pinCount++;
    entry.lastAccessTicks = DateTime.UtcNow.Ticks;
    EnqueueLoad(entry, priority);
    if (!runPumpAndMaintain) return;
    Pump();
    MaintainBudget();
  }

  static void TryExpandAtlasOnSliceRequest(string requestedAddress, bool runPumpAndMaintain) {
    if (!SpriteStreamingRuntimeSettings.EnableAtlasExpansionOnSliceRequest) return;
    if (!Application.isPlaying) return;
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(requestedAddress, out var atlasAssetPath, out _)) return;

    var atlasKey = NormalizeAddress(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(atlasKey)) return;
    if (expandedAtlasKeys.Contains(atlasKey)) return;
    if (atlasExpansionRetryFrames.TryGetValue(atlasKey, out var nextRetryFrame) && Time.frameCount < nextRetryFrame) return;

    var maxSiblings = Math.Max(SpriteStreamingRuntimeSettings.AtlasExpansionMaxSiblingAddresses, 1);
    if (StreamingWarmOrchestrator.IsWarmGateRunning || SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      maxSiblings = Math.Min(maxSiblings, 48);
    }
    atlasSiblingAddressScratch.Clear();
    var hasSiblingMap = SpriteRuntimeResolver.TryCollectAtlasSiblingAddresses(requestedAddress, atlasSiblingAddressScratch, maxSiblings);
    if (!hasSiblingMap) {
      atlasExpansionRetryFrames[atlasKey] = Time.frameCount + AtlasExpansionRetryFrameWindow;
      SpriteStreamingDiagnostics.RecordAtlasExpansionFallback();
      return;
    }

    var siblingCount = atlasSiblingAddressScratch.Count;
    var queuedCount = 0;
    for (var i = 0; i < atlasSiblingAddressScratch.Count; i++) {
      var siblingAddress = NormalizeAddress(atlasSiblingAddressScratch[i]);
      if (string.IsNullOrWhiteSpace(siblingAddress)) continue;
      if (string.Equals(siblingAddress, requestedAddress, StringComparison.OrdinalIgnoreCase)) continue;
      var siblingEntry = ResolveEntryForLoad(siblingAddress, out var siblingHit);
      RecordLookup(siblingHit);
      QueueEntryForLoad(siblingEntry, LoadPriority.Warmup, pinEntry: false, runPumpAndMaintain: false);
      queuedCount++;
    }

    expandedAtlasKeys.Add(atlasKey);
    atlasExpansionRetryFrames.Remove(atlasKey);
    SpriteStreamingDiagnostics.RecordAtlasExpansion(siblingCount, queuedCount);

    if (ShouldLogAtlasExpansion()) {
      Debug.Log(
        "[TextureResidencyCache][AtlasExpansion] context=" + ResolveAtlasExpansionContext() +
        " atlas='" + atlasKey + "'" +
        " source='" + requestedAddress + "'" +
        " siblings=" + siblingCount +
        " queued=" + queuedCount
      );
    }

    if (!runPumpAndMaintain) return;
    Pump();
    MaintainBudget();
  }

  static string ResolveAtlasExpansionContext() {
    if (StreamingWarmOrchestrator.IsWarmGateRunning) return "warm_gate";
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) return "loading_overlay";
    return "live";
  }

  static bool ShouldLogAtlasExpansion() {
    if (!SpriteStreamingRuntimeSettings.EnableAtlasExpansionLogs) return false;
    return Application.isEditor || Debug.isDebugBuild;
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
