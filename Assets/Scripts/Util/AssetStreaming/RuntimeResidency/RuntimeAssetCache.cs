using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

public enum RuntimeAssetResidencyScope {
  Session = 0,
  GlobalUi = 1
}

public readonly struct RuntimeAssetQueueSnapshot {
  public readonly int queuedCount;
  public readonly int inFlightCount;
  public readonly int preparingCount;
  public readonly int loadedCount;

  public RuntimeAssetQueueSnapshot(
    int queuedCount,
    int inFlightCount,
    int preparingCount,
    int loadedCount
  ) {
    this.queuedCount = Mathf.Max(queuedCount, 0);
    this.inFlightCount = Mathf.Max(inFlightCount, 0);
    this.preparingCount = Mathf.Max(preparingCount, 0);
    this.loadedCount = Mathf.Max(loadedCount, 0);
  }
}

public readonly struct RuntimeAssetWarmSnapshot {
  public readonly bool prepared;
  public readonly int readyCount;
  public readonly int totalCount;
  public readonly int criticalReadyCount;
  public readonly int criticalTotalCount;
  public readonly bool criticalReady;
  public readonly RuntimeAssetQueueSnapshot queue;

  public RuntimeAssetWarmSnapshot(
    bool prepared,
    int readyCount,
    int totalCount,
    int criticalReadyCount,
    int criticalTotalCount,
    bool criticalReady,
    RuntimeAssetQueueSnapshot queue
  ) {
    this.prepared = prepared;
    this.readyCount = Mathf.Max(readyCount, 0);
    this.totalCount = Mathf.Max(totalCount, 0);
    this.criticalReadyCount = Mathf.Max(criticalReadyCount, 0);
    this.criticalTotalCount = Mathf.Max(criticalTotalCount, 0);
    this.criticalReady = criticalReady;
    this.queue = queue;
  }
}

public static class RuntimeAssetCache {
  sealed class CacheEntry {
    public readonly string key;
    public readonly string address;
    public readonly Type assetType;
    public RuntimeAssetResidencyScope scope;
    public AsyncOperationHandle loadHandle;
    public UnityEngine.Object loadedAsset;
    public bool isLoaded;
    public bool isLoading;
    public bool failed;
    public bool pendingQueued;
    public bool pendingHighPriority;
    public bool releaseWhenLoadCompletes;
    public string lastError;

    public CacheEntry(string key, string address, Type assetType, RuntimeAssetResidencyScope scope) {
      this.key = key;
      this.address = address;
      this.assetType = assetType;
      this.scope = scope;
      loadHandle = default;
      loadedAsset = null;
      isLoaded = false;
      isLoading = false;
      failed = false;
      pendingQueued = false;
      pendingHighPriority = false;
      releaseWhenLoadCompletes = false;
      lastError = "";
    }
  }

  sealed class TrackedWarmRequest {
    public readonly int id;
    public readonly string reason;
    public readonly HashSet<string> allKeys = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> criticalKeys = new(StringComparer.OrdinalIgnoreCase);
    public readonly bool expectsCriticalSources;
    public bool prepared;
    public bool cancelled;

    public TrackedWarmRequest(int id, string reason, bool expectsCriticalSources) {
      this.id = id;
      this.reason = string.IsNullOrWhiteSpace(reason) ? "" : reason.Trim();
      this.expectsCriticalSources = expectsCriticalSources;
      prepared = false;
      cancelled = false;
    }
  }

  enum QueueOutcome {
    None = 0,
    Enqueued = 1,
    CacheHit = 2,
    AlreadyPending = 3,
    Failed = 4
  }

  static readonly Dictionary<string, CacheEntry> entriesByKey = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<int, TrackedWarmRequest> trackedRequests = new();
  static readonly Queue<string> highPriorityQueue = new();
  static readonly Queue<string> normalPriorityQueue = new();
  static readonly HashSet<string> unsupportedSourceWarnings = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<string> clearSessionKeysScratch = new(64);
  static RuntimeAssetCacheRunner runner;
  static int nextTrackedWarmId = 1;
  static int activeResolveCount;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    foreach (var pair in entriesByKey) {
      var entry = pair.Value;
      if (entry == null || entry.isLoading) continue;
      ReleaseEntry(entry, reason: "subsystem_reset");
    }
    entriesByKey.Clear();
    trackedRequests.Clear();
    highPriorityQueue.Clear();
    normalPriorityQueue.Clear();
    unsupportedSourceWarnings.Clear();
    clearSessionKeysScratch.Clear();
    runner = null;
    nextTrackedWarmId = 1;
    activeResolveCount = 0;
  }

  public static int BeginTrackedWarmup(
    IEnumerable<string> criticalAddresses,
    IEnumerable<string> criticalLabels,
    IEnumerable<string> warmAddresses,
    IEnumerable<string> warmLabels,
    RuntimeAssetResidencyScope scope,
    string reason = ""
  ) {
    if (!Application.isPlaying) return 0;
    EnsureRunner();
    var trackedId = nextTrackedWarmId++;
    var tracker = new TrackedWarmRequest(
      trackedId,
      reason,
      expectsCriticalSources: HasAnySource(criticalAddresses) || HasAnySource(criticalLabels)
    );
    trackedRequests[trackedId] = tracker;
    if (ShouldLogDebug()) {
      RuntimeLog.Log($"[RuntimeAssetCache] BeginTrackedWarmup: id={trackedId}, reason='{reason}', expectsCritical={tracker.expectsCriticalSources}");
    }
    runner.StartCoroutine(
      ResolveWarmupSourcesRoutine(
        criticalAddresses,
        criticalLabels,
        warmAddresses,
        warmLabels,
        scope,
        tracker
      )
    );
    return trackedId;
  }

  public static void ReleaseTrackedWarmup(int trackedWarmId) {
    if (trackedWarmId <= 0) return;
    if (trackedRequests.TryGetValue(trackedWarmId, out var tracker) && tracker != null) {
      tracker.cancelled = true;
    }
    trackedRequests.Remove(trackedWarmId);
  }

  public static void QueueWarmup(
    IEnumerable<string> addresses,
    IEnumerable<string> labels,
    RuntimeAssetResidencyScope scope,
    string reason = ""
  ) {
    if (!Application.isPlaying) return;
    EnsureRunner();
    runner.StartCoroutine(
      ResolveWarmupSourcesRoutine(
        criticalAddresses: null,
        criticalLabels: null,
        warmAddresses: addresses,
        warmLabels: labels,
        scope: scope,
        tracker: null,
        reason: reason
      )
    );
  }

  public static bool TryGetTrackedWarmSnapshot(int trackedWarmId, out RuntimeAssetWarmSnapshot snapshot) {
    snapshot = default;
    if (trackedWarmId <= 0) return false;
    if (!trackedRequests.TryGetValue(trackedWarmId, out var tracker) || tracker == null) return false;
    snapshot = BuildTrackedSnapshot(tracker);
    return true;
  }

  public static RuntimeAssetQueueSnapshot GetQueueSnapshot() {
    var queuedCount = 0;
    var inFlightCount = 0;
    var loadedCount = 0;
    foreach (var pair in entriesByKey) {
      var entry = pair.Value;
      if (entry == null) continue;
      if (entry.pendingQueued) queuedCount++;
      if (entry.isLoading) inFlightCount++;
      if (entry.isLoaded && entry.loadedAsset != null) loadedCount++;
    }
    return new RuntimeAssetQueueSnapshot(queuedCount, inFlightCount, activeResolveCount, loadedCount);
  }

  public static bool IsIdle() {
    var snapshot = GetQueueSnapshot();
    return snapshot.queuedCount <= 0 && snapshot.inFlightCount <= 0 && snapshot.preparingCount <= 0;
  }

  public static void ClearSessionScope(string reason = "") {
    if (entriesByKey.Count <= 0) return;
    var keysToRemove = clearSessionKeysScratch;
    keysToRemove.Clear();
    foreach (var pair in entriesByKey) {
      var entry = pair.Value;
      if (entry == null || entry.scope != RuntimeAssetResidencyScope.Session) continue;
      if (entry.isLoading) {
        entry.pendingQueued = false;
        entry.pendingHighPriority = false;
        entry.releaseWhenLoadCompletes = true;
        continue;
      }

      ReleaseEntry(entry, reason: "clear_session_scope");
      keysToRemove.Add(pair.Key);
    }

    for (var i = 0; i < keysToRemove.Count; i++) {
      entriesByKey.Remove(keysToRemove[i]);
    }

    if (ShouldLogDebug() && keysToRemove.Count > 0) {
      RuntimeLog.Log(
        "[RuntimeAssetCache] Cleared session scope" +
        " released=" + keysToRemove.Count +
        " reason='" + (reason ?? "") + "'"
      );
    }
    keysToRemove.Clear();
  }

  public static bool TryGetLoaded<T>(string address, out T asset) where T : UnityEngine.Object {
    asset = null;
    var supportedType = ResolveSupportedAssetType(typeof(T));
    if (supportedType == null) return false;

    var normalizedAddress = NormalizeToken(address);
    if (string.IsNullOrWhiteSpace(normalizedAddress)) return false;

    var key = BuildEntryKey(normalizedAddress, supportedType);
    if (!entriesByKey.TryGetValue(key, out var entry) || entry == null || !entry.isLoaded || entry.loadedAsset == null) {
      return false;
    }

    asset = entry.loadedAsset as T;
    if (asset == null) return false;

    if (ShouldLogDebug()) {
      RuntimeLog.Log(
        "[RuntimeAssetCache] Cache hit" +
        " address='" + normalizedAddress + "'" +
        " type=" + supportedType.Name +
        " scope=" + entry.scope
      );
    }
    return true;
  }

  internal static void Tick() {
    if (!Application.isPlaying) return;
    var startBudget = ResolveStartBudgetPerFrame();
    while (startBudget > 0) {
      var entry = DequeueNextPendingEntry();
      if (entry == null) break;
      StartLoad(entry);
      startBudget--;
    }
  }

  static IEnumerator ResolveWarmupSourcesRoutine(
    IEnumerable<string> criticalAddresses,
    IEnumerable<string> criticalLabels,
    IEnumerable<string> warmAddresses,
    IEnumerable<string> warmLabels,
    RuntimeAssetResidencyScope scope,
    TrackedWarmRequest tracker,
    string reason = ""
  ) {
    if (ShouldLogDebug()) {
      RuntimeLog.Log($"[RuntimeAssetCache] ResolveWarmupSourcesRoutine START for tracker={tracker?.id}");
    }
    var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    yield return ResolveSourceList(criticalAddresses, isLabel: false, markCritical: true, scope, tracker, seenSources, reason);
    yield return ResolveSourceList(criticalLabels, isLabel: true, markCritical: true, scope, tracker, seenSources, reason);
    yield return ResolveSourceList(warmAddresses, isLabel: false, markCritical: false, scope, tracker, seenSources, reason);
    yield return ResolveSourceList(warmLabels, isLabel: true, markCritical: false, scope, tracker, seenSources, reason);

    if (tracker != null) {
      if (tracker.cancelled) {
        if (ShouldLogDebug()) {
          RuntimeLog.Log($"[RuntimeAssetCache] ResolveWarmupSourcesRoutine CANCELLED for tracker={tracker.id}");
        }
        yield break;
      }
      tracker.prepared = true;
      if (ShouldLogDebug()) {
        RuntimeLog.Log(
          "[RuntimeAssetCache] Tracked warm prepared" +
          " id=" + tracker.id +
          " reason='" + tracker.reason + "'" +
          " total=" + tracker.allKeys.Count +
          " critical=" + tracker.criticalKeys.Count
        );
      }
    }
  }

  static IEnumerator ResolveSourceList(
    IEnumerable<string> sources,
    bool isLabel,
    bool markCritical,
    RuntimeAssetResidencyScope scope,
    TrackedWarmRequest tracker,
    HashSet<string> seenSources,
    string reason
  ) {
    if (sources == null) yield break;
    foreach (var value in sources) {
      if (tracker != null && tracker.cancelled) yield break;
      var normalized = NormalizeToken(value);
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      var sourceKey = BuildSourceKey(normalized, isLabel);
      if (seenSources != null && !seenSources.Add(sourceKey)) continue;
      yield return ResolveSourceRoutine(normalized, isLabel, markCritical, scope, tracker, reason);
    }
  }

  static IEnumerator ResolveSourceRoutine(
    string source,
    bool isLabel,
    bool markCritical,
    RuntimeAssetResidencyScope scope,
    TrackedWarmRequest tracker,
    string reason
  ) {
    if (ShouldLogDebug()) {
      RuntimeLog.Log($"[RuntimeAssetCache] ResolveSourceRoutine: source='{source}', isLabel={isLabel}, tracker={tracker?.id}");
    }
    var queueHits = 0;
    var queueEnqueues = 0;
    var alreadyPending = 0;
    var failedCount = 0;
    var supportedCount = 0;
    var skippedUnsupported = 0;
    var sourceKey = BuildSourceKey(source, isLabel);
    var handle = default(AsyncOperationHandle<IList<IResourceLocation>>);
    activeResolveCount++;
    try {
      handle = Addressables.LoadResourceLocationsAsync(source);
      while (!handle.IsDone) {
        yield return null;
      }

      if (ShouldLogDebug()) {
        RuntimeLog.Log($"[RuntimeAssetCache] ResolveSourceRoutine handle done: source='{source}', status={handle.Status}, count={(handle.Status == AsyncOperationStatus.Succeeded ? handle.Result?.Count : 0)}");
      }

      if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null) {
        failedCount = 1;
        var error = handle.OperationException != null ? handle.OperationException.Message : "none";
        Debug.LogWarning(
          "[RuntimeAssetCache] Failed to resolve source" +
          " key='" + source + "'" +
          " label=" + (isLabel ? 1 : 0) +
          " reason='" + (reason ?? "") + "'" +
          " error='" + error + "'"
        );
        yield break;
      }

      var locations = handle.Result;
      for (var i = 0; i < locations.Count; i++) {
        if (tracker != null && tracker.cancelled) yield break;
        var location = locations[i];
        if (!TryResolveSupportedAssetType(location, out var assetType)) {
          skippedUnsupported++;
          continue;
        }

        var address = NormalizeToken(location.PrimaryKey);
        if (string.IsNullOrWhiteSpace(address)) {
          skippedUnsupported++;
          continue;
        }

        supportedCount++;
        var outcome = QueueResolvedAsset(address, assetType, scope, markCritical, tracker);
        if (outcome == QueueOutcome.CacheHit) {
          queueHits++;
        }
        else if (outcome == QueueOutcome.Enqueued) {
          queueEnqueues++;
        }
        else if (outcome == QueueOutcome.AlreadyPending) {
          alreadyPending++;
        }
        else if (outcome == QueueOutcome.Failed) {
          failedCount++;
        }
      }
    }
    finally {
      activeResolveCount = Mathf.Max(activeResolveCount - 1, 0);
      if (handle.IsValid()) {
        Addressables.Release(handle);
      }
    }

    if (skippedUnsupported > 0 && unsupportedSourceWarnings.Add(sourceKey)) {
      Debug.LogWarning(
        "[RuntimeAssetCache] Skipped unsupported source members" +
        " key='" + source + "'" +
        " label=" + (isLabel ? 1 : 0) +
        " skipped=" + skippedUnsupported
      );
    }

    if (!ShouldLogDebug()) yield break;

    RuntimeLog.Log(
      "[RuntimeAssetCache] Source resolved" +
      " key='" + source + "'" +
      " label=" + (isLabel ? 1 : 0) +
      " critical=" + (markCritical ? 1 : 0) +
      " scope=" + scope +
      " supported=" + supportedCount +
      " enqueued=" + queueEnqueues +
      " hits=" + queueHits +
      " pending=" + alreadyPending +
      " failed=" + failedCount +
      " skipped=" + skippedUnsupported +
      " reason='" + (reason ?? "") + "'"
    );
  }

  static RuntimeAssetWarmSnapshot BuildTrackedSnapshot(TrackedWarmRequest tracker) {
    var logSnapshotKeys = ShouldLogSnapshotKeys();
    var readyCount = 0;
    foreach (var key in tracker.allKeys) {
      var ready = IsEntryReady(key);
      var failed = IsEntryFailed(key);
      if (ready || failed) readyCount++;
      if (logSnapshotKeys) {
        RuntimeLog.Log($"[RuntimeAssetCache][DebugSnapshot] id={tracker.id} allKey='{key}' ready={ready} failed={failed}");
      }
    }

    var criticalReadyCount = 0;
    foreach (var key in tracker.criticalKeys) {
      var ready = IsEntryReady(key);
      var failed = IsEntryFailed(key);
      if (ready || failed) criticalReadyCount++;
      if (logSnapshotKeys) {
        RuntimeLog.Log($"[RuntimeAssetCache][DebugSnapshot] id={tracker.id} criticalKey='{key}' ready={ready} failed={failed}");
      }
    }

    var criticalTotalCount = tracker.criticalKeys.Count;
    var criticalReady = criticalTotalCount <= 0 || (tracker.prepared && criticalReadyCount >= criticalTotalCount);
    if (tracker.expectsCriticalSources && !tracker.prepared && criticalTotalCount <= 0) {
      criticalReady = false;
    }

    return new RuntimeAssetWarmSnapshot(
      prepared: tracker.prepared,
      readyCount: readyCount,
      totalCount: tracker.allKeys.Count,
      criticalReadyCount: criticalReadyCount,
      criticalTotalCount: criticalTotalCount,
      criticalReady: criticalReady,
      queue: GetQueueSnapshot()
    );
  }

  static CacheEntry DequeueNextPendingEntry() {
    while (highPriorityQueue.Count > 0) {
      var key = highPriorityQueue.Dequeue();
      if (!entriesByKey.TryGetValue(key, out var entry) || entry == null) continue;
      if (!entry.pendingQueued || !entry.pendingHighPriority || entry.isLoading || entry.isLoaded || entry.failed) continue;
      return entry;
    }

    while (normalPriorityQueue.Count > 0) {
      var key = normalPriorityQueue.Dequeue();
      if (!entriesByKey.TryGetValue(key, out var entry) || entry == null) continue;
      if (!entry.pendingQueued || entry.pendingHighPriority || entry.isLoading || entry.isLoaded || entry.failed) continue;
      return entry;
    }

    return null;
  }

  static void RecordRuntimeAssetTraceQueue(CacheEntry entry, bool highPriority) {
    if (entry == null) return;
    if (!AssetLoadTraceMonitor.IsEnabled) return;
    AssetLoadTraceMonitor.RecordEvent(
      source: "RuntimeAssetCache",
      stage: "queue",
      address: entry.address,
      assetTypeOverride: entry.assetType != null ? entry.assetType.Name : "",
      detail:
        "priority=" + (highPriority ? "high" : "normal") +
        " scope=" + entry.scope
    );
  }

  static void RecordRuntimeAssetTracePromote(CacheEntry entry) {
    if (entry == null) return;
    if (!AssetLoadTraceMonitor.IsEnabled) return;
    AssetLoadTraceMonitor.RecordEvent(
      source: "RuntimeAssetCache",
      stage: "promote",
      address: entry.address,
      assetTypeOverride: entry.assetType != null ? entry.assetType.Name : "",
      detail: "priority=high scope=" + entry.scope
    );
  }

  static void RecordRuntimeAssetTraceStart(CacheEntry entry) {
    if (entry == null) return;
    if (!AssetLoadTraceMonitor.IsEnabled) return;
    AssetLoadTraceMonitor.RecordEvent(
      source: "RuntimeAssetCache",
      stage: "start",
      address: entry.address,
      assetTypeOverride: entry.assetType != null ? entry.assetType.Name : "",
      detail: "scope=" + entry.scope
    );
  }

  static void RecordRuntimeAssetTraceComplete(CacheEntry entry, UnityEngine.Object asset, bool loadSucceeded) {
    if (entry == null) return;
    if (!AssetLoadTraceMonitor.IsEnabled) return;
    AssetLoadTraceMonitor.RecordEvent(
      source: "RuntimeAssetCache",
      stage: loadSucceeded ? "complete" : "fail",
      address: entry.address,
      asset: asset,
      assetTypeOverride: entry.assetType != null ? entry.assetType.Name : "",
      detail:
        "scope=" + entry.scope +
        " release_when_complete=" + (entry.releaseWhenLoadCompletes ? 1 : 0),
      error: loadSucceeded ? "" : entry.lastError
    );
  }

  static void RecordRuntimeAssetTraceRelease(CacheEntry entry, string reason) {
    if (entry == null) return;
    if (!AssetLoadTraceMonitor.IsEnabled) return;
    AssetLoadTraceMonitor.RecordEvent(
      source: "RuntimeAssetCache",
      stage: "release",
      address: entry.address,
      asset: entry.loadedAsset,
      assetTypeOverride: entry.assetType != null ? entry.assetType.Name : "",
      detail:
        "scope=" + entry.scope +
        " reason=" + NormalizeToken(reason) +
        " was_loaded=" + (entry.isLoaded && entry.loadedAsset != null ? 1 : 0)
    );
  }

  static void StartLoad(CacheEntry entry) {
    if (entry == null || entry.isLoaded || entry.isLoading || entry.failed) return;
    entry.pendingQueued = false;
    entry.pendingHighPriority = false;
    entry.isLoading = true;
    if (entry.assetType == typeof(GameObject)) {
      var handle = Addressables.LoadAssetAsync<GameObject>(entry.address);
      entry.loadHandle = handle;
      RecordRuntimeAssetTraceStart(entry);
      handle.Completed += op => CompleteLoad(entry.key, op);
      return;
    }

    if (entry.assetType == typeof(Material)) {
      var handle = Addressables.LoadAssetAsync<Material>(entry.address);
      entry.loadHandle = handle;
      RecordRuntimeAssetTraceStart(entry);
      handle.Completed += op => CompleteLoad(entry.key, op);
      return;
    }

    entry.isLoading = false;
    entry.failed = true;
    entry.lastError = "unsupported_type";
    RecordRuntimeAssetTraceComplete(entry, asset: null, loadSucceeded: false);
  }

  static void CompleteLoad<T>(string key, AsyncOperationHandle<T> operation) where T : UnityEngine.Object {
    if (!entriesByKey.TryGetValue(key, out var entry) || entry == null) {
      if (operation.IsValid()) {
        Addressables.Release(operation);
      }
      return;
    }

    entry.isLoading = false;
    if (operation.Status == AsyncOperationStatus.Succeeded && operation.Result != null) {
      entry.loadHandle = operation;
      entry.loadedAsset = operation.Result;
      entry.isLoaded = true;
      entry.failed = false;
      entry.lastError = "";
      RecordRuntimeAssetTraceComplete(entry, operation.Result, loadSucceeded: true);
      if (ShouldLogDebug()) {
        RuntimeLog.Log(
          "[RuntimeAssetCache] Loaded asset" +
          " address='" + entry.address + "'" +
          " type=" + entry.assetType.Name +
          " scope=" + entry.scope
        );
      }
    }
    else {
      entry.failed = true;
      entry.lastError = operation.OperationException != null ? operation.OperationException.Message : "load_failed";
      if (operation.IsValid()) {
        Addressables.Release(operation);
      }
      RecordRuntimeAssetTraceComplete(entry, asset: null, loadSucceeded: false);
      Debug.LogWarning(
        "[RuntimeAssetCache] Failed to load asset" +
        " address='" + entry.address + "'" +
        " type=" + entry.assetType.Name +
        " error='" + entry.lastError + "'"
      );
    }

    if (!entry.releaseWhenLoadCompletes) return;

    ReleaseEntry(entry, reason: "release_when_complete");
    entriesByKey.Remove(key);
  }

  static QueueOutcome QueueResolvedAsset(
    string address,
    Type assetType,
    RuntimeAssetResidencyScope scope,
    bool markCritical,
    TrackedWarmRequest tracker
  ) {
    var key = BuildEntryKey(address, assetType);
    if (tracker != null) {
      tracker.allKeys.Add(key);
      if (markCritical) tracker.criticalKeys.Add(key);
    }

    if (!entriesByKey.TryGetValue(key, out var entry) || entry == null) {
      entry = new CacheEntry(key, address, assetType, scope);
      entriesByKey[key] = entry;
    }
    else {
      entry.scope = PromoteScope(entry.scope, scope);
    }

    if (entry.isLoaded && entry.loadedAsset != null) {
      return QueueOutcome.CacheHit;
    }

    if (entry.failed) {
      return QueueOutcome.Failed;
    }

    if (entry.isLoading) {
      return QueueOutcome.AlreadyPending;
    }

    if (markCritical) {
      if (!entry.pendingQueued) {
        entry.pendingQueued = true;
        entry.pendingHighPriority = true;
        highPriorityQueue.Enqueue(key);
        RecordRuntimeAssetTraceQueue(entry, highPriority: true);
        return QueueOutcome.Enqueued;
      }

      if (!entry.pendingHighPriority) {
        entry.pendingHighPriority = true;
        highPriorityQueue.Enqueue(key);
        RecordRuntimeAssetTracePromote(entry);
      }
      return QueueOutcome.AlreadyPending;
    }

    if (!entry.pendingQueued) {
      entry.pendingQueued = true;
      entry.pendingHighPriority = false;
      normalPriorityQueue.Enqueue(key);
      RecordRuntimeAssetTraceQueue(entry, highPriority: false);
      return QueueOutcome.Enqueued;
    }

    return QueueOutcome.AlreadyPending;
  }

  static bool IsEntryReady(string key) {
    if (!entriesByKey.TryGetValue(key, out var entry) || entry == null) return false;
    return entry.isLoaded && entry.loadedAsset != null;
  }

  static bool IsEntryFailed(string key) {
    if (!entriesByKey.TryGetValue(key, out var entry) || entry == null) return false;
    return entry.failed;
  }

  static void ReleaseEntry(CacheEntry entry, string reason = "") {
    if (entry == null) return;
    RecordRuntimeAssetTraceRelease(entry, reason);
    if (entry.loadHandle.IsValid()) {
      Addressables.Release(entry.loadHandle);
    }
    entry.loadHandle = default;
    entry.loadedAsset = null;
    entry.isLoaded = false;
    entry.isLoading = false;
    entry.pendingQueued = false;
    entry.pendingHighPriority = false;
    entry.releaseWhenLoadCompletes = false;
  }

  static void EnsureRunner() {
    if (!Application.isPlaying) return;
    if (runner != null) return;
    var go = new GameObject("RuntimeAssetCacheRunner") { hideFlags = HideFlags.HideAndDontSave };
    UnityEngine.Object.DontDestroyOnLoad(go);
    runner = go.AddComponent<RuntimeAssetCacheRunner>();
    if (ShouldLogDebug()) {
      RuntimeLog.Log("[RuntimeAssetCache] Created RuntimeAssetCacheRunner game object.");
    }
  }

  static int ResolveStartBudgetPerFrame() {
    var configured = SpriteStreamingLoadingState.IsLoadingOverlayActive
      ? SpriteStreamingRuntimeSettings.LoadingOverlayMaxAddressableStartsPerFrame
      : SpriteStreamingRuntimeSettings.MaxAddressableStartsPerFrame;
    return Mathf.Max(configured, 1);
  }

  static RuntimeAssetResidencyScope PromoteScope(RuntimeAssetResidencyScope current, RuntimeAssetResidencyScope requested) {
    return requested > current ? requested : current;
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  static string BuildEntryKey(string address, Type assetType) {
    var normalizedAddress = NormalizeToken(address);
    return assetType.FullName + "|" + normalizedAddress;
  }

  static string BuildSourceKey(string source, bool isLabel) {
    return (isLabel ? "label:" : "address:") + NormalizeToken(source);
  }

  static bool HasAnySource(IEnumerable<string> values) {
    if (values == null) return false;
    foreach (var value in values) {
      if (!string.IsNullOrWhiteSpace(NormalizeToken(value))) return true;
    }
    return false;
  }

  static bool TryResolveSupportedAssetType(IResourceLocation location, out Type assetType) {
    assetType = ResolveSupportedAssetType(location != null ? location.ResourceType : null);
    return assetType != null;
  }

  static Type ResolveSupportedAssetType(Type candidateType) {
    if (candidateType == null) return null;
    if (typeof(GameObject).IsAssignableFrom(candidateType)) return typeof(GameObject);
    if (typeof(Material).IsAssignableFrom(candidateType)) return typeof(Material);
    return null;
  }

  static bool ShouldLogDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static bool ShouldLogSnapshotKeys() {
    return ShouldLogDebug() && SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs;
  }
}

sealed class RuntimeAssetCacheRunner : MonoBehaviour {
  void Update() {
    RuntimeAssetCache.Tick();
  }
}
