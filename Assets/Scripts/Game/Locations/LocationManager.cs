using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public partial class LocationManager : MonoBehaviour {
  sealed class CachedLocationInstance {
    public GameObject prefab;
    public GameObject instance;
    public int saveSlot;
    public int registryVersion;
    public int episodeRevision;
    public long lastUsedSequence;
  }

  const float OverlayLocationResolverBarrierTimeoutSeconds = 8.0f;
  const float OverlayActivationCapacityWaitTimeoutSeconds = 0.1f;
  const float ProtectedOverlayActivationCapacityWaitTimeoutSeconds = 0.05f;
  const float RuntimeActivationCapacityWaitTimeoutSeconds = 0.02f;
  const float ActivationWaitStateLogIntervalSeconds = 5.0f;
  const float ActivationProgressLogIntervalSeconds = 1.0f;
  const float SlowActivationStepLogSeconds = 0.05f;
  const int ActivationTraceComponentNameLimit = 8;
  const int ProtectedOverlayActivationMaxOutstandingDesktop = 512;
  const int ProtectedOverlayActivationMaxOutstandingMobile = 256;
  const int ProtectedOverlayActivationMaxInFlightDesktop = 128;
  const int ProtectedOverlayActivationMaxInFlightMobile = 64;
  const int OverlayActivationMaxOutstandingDesktop = 6;
  const int OverlayActivationMaxOutstandingMobile = 4;
  const int OverlayActivationMaxInFlightDesktop = 1;
  const int OverlayActivationMaxInFlightMobile = 0;
  const int RuntimeActivationMaxOutstanding = 2;
  const int RuntimeActivationMaxInFlight = 1;
  const int ProtectedOverlayActivationBurstPerFrame = 128;
  const int OverlayActivationBurstPerFrame = 2;
  const int RuntimeActivationBurstPerFrame = 1;
  const int MaxRetainedLocationInstances = 2;

  static LocationManager runtimeInstance;

  // Former LocationTracker logic, kept static for cross-system access.
  public static string currentLocation = "nowhere";

  public static void UpdateLocation(string newLocation) {
    var resolvedLocation = string.IsNullOrWhiteSpace(newLocation)
      ? "nowhere"
      : newLocation.Trim();
    if (!LocationEnemyData.ContainsLocation(resolvedLocation)) {
      Debug.LogWarning(
        "[LocationManager] Ignored unknown location update '" +
        resolvedLocation +
        "'."
      );
      return;
    }
    if (SingleSceneManager.TryBeginRuntimeLocationTransition(resolvedLocation)) {
      return;
    }

    CommitLocationUpdate(resolvedLocation);
  }

  internal static void CommitLocationForLoadingFlow(string newLocation) {
    var resolvedLocation = LocationEnemyData.NormalizeLocationId(newLocation);
    if (!LocationEnemyData.ContainsLocation(resolvedLocation)) {
      return;
    }

    CommitLocationUpdate(resolvedLocation);
  }

  static void CommitLocationUpdate(string resolvedLocation) {
    currentLocation = resolvedLocation;
    MessageBus.Send("LocationUpdated", currentLocation);
  }

  [Header("Location Runtime")]
  [Header("Enemy Loading Pipeline")]
  [SerializeField] LocationLoadingPipeline enemyLoadingPipeline;

  [SerializeField] Transform locationRoot;
  [SerializeField] Transform environmentRoot;
  [SerializeField] bool loadFromTrackerOnStart = true;

  readonly List<Action> actions = new();
  readonly HashSet<string> locationLibraryScratch = new(StringComparer.OrdinalIgnoreCase);
  readonly List<string> locationLibraryListScratch = new(64);
  readonly List<string> locationDeferredLibraryListScratch = new(64);
  readonly List<string> relativeNodeSegmentsScratch = new(16);
  readonly List<Component> activationComponentScratch = new(32);
  readonly StringBuilder stagePlanSummaryBuilder = new(512);
  readonly Dictionary<string, CachedLocationInstance> locationInstanceCache =
    new(StringComparer.OrdinalIgnoreCase);
  LocationInfo activeLocation;
  string currentLocationId = "";
  GameObject activeLocationInstance;
  string activeLocationInstanceId = "";
  GameObject activeLocationInstancePrefab;
  int activeLocationInstanceSaveSlot = -1;
  int activeLocationInstanceRegistryVersion = -1;
  int activeLocationInstanceEpisodeRevision = -1;
  Coroutine pendingBlockingLocationActivationRoutine;
  Coroutine pendingDeferredLocationActivationRoutine;
  int pendingLocationActivationGeneration;
  long locationCacheSequence;

  public static bool HasPendingActivationWork =>
    runtimeInstance != null &&
    (runtimeInstance.pendingBlockingLocationActivationRoutine != null ||
     runtimeInstance.pendingDeferredLocationActivationRoutine != null);
  public static bool HasPendingBlockingActivationWork =>
    runtimeInstance != null && runtimeInstance.pendingBlockingLocationActivationRoutine != null;
  public static bool HasPendingDeferredActivationWork =>
    runtimeInstance != null && runtimeInstance.pendingDeferredLocationActivationRoutine != null;
  public static GameObject ResolveActiveLocationInstance() =>
    runtimeInstance != null ? runtimeInstance.activeLocationInstance : null;
  public string CurrentLocationId => currentLocationId;
  public LocationInfo CurrentLocation => activeLocation;
  public IReadOnlyList<LocationObjective> CurrentObjectives =>
    activeLocation != null && activeLocation.objectives != null ? activeLocation.objectives : Array.Empty<LocationObjective>();
  public GameObject ActiveLocationInstance => activeLocationInstance;

  void Awake() {
    runtimeInstance = this;
  }

  void Start() {
    actions.Add(MessageBus.On("RequestLocationLoad", OnRequestLocationLoad));
    actions.Add(MessageBus.On("LocationUpdated", OnLocationUpdated));

    if (!loadFromTrackerOnStart) return;
    var tracked = LocationEnemyData.NormalizeLocationId(currentLocation);
    if (string.IsNullOrWhiteSpace(tracked) || string.Equals(tracked, "nowhere", StringComparison.OrdinalIgnoreCase)) {
      return;
    }
    var initial = LocationEnemyData.ResolveRequestedOrDefault(tracked);
    LoadLocation(initial, updateTrackerIfDifferent: true);
  }

  void OnDestroy() {
    if (ReferenceEquals(runtimeInstance, this)) {
      runtimeInstance = null;
    }
    StopPendingLocationActivation();
    DestroyAllLocationInstances();
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  void OnRequestLocationLoad(object payload) {
    var requested = Convert.ToString(payload);
    LoadLocation(requested, updateTrackerIfDifferent: true);
  }

  void OnLocationUpdated(object payload) {
    var requested = Convert.ToString(payload);
    LoadLocation(requested, updateTrackerIfDifferent: false);
  }

  public bool LoadLocation(string requestedLocationId, bool updateTrackerIfDifferent = true) {
    var requestStartedAt = Time.realtimeSinceStartup;
    var resolvedId = LocationEnemyData.ResolveRequestedOrDefault(requestedLocationId);
    if (!LocationEnemyData.TryGetLocation(resolvedId, out var info) || info == null) {
      Debug.LogWarning("[LocationManager] Unable to resolve location '" + requestedLocationId + "'.");
      return false;
    }

    if (updateTrackerIfDifferent && !string.Equals(currentLocation, resolvedId, StringComparison.OrdinalIgnoreCase)) {
      if (SingleSceneManager.TryBeginRuntimeLocationTransition(resolvedId)) {
        return true;
      }
      CommitLocationUpdate(resolvedId);
      updateTrackerIfDifferent = false;
    }

    var changedLocation = !string.Equals(currentLocationId, resolvedId, StringComparison.OrdinalIgnoreCase);
    currentLocationId = resolvedId;
    activeLocation = info;
    var logVerbose = ShouldLogVerboseLoadDebug();
    var pendingBlockingActivation = HasPendingBlockingActivationWork;
    var pendingDeferredActivation = HasPendingDeferredActivationWork;
    var refreshPendingLocationUnderOverlay = ShouldRefreshPendingLocationUnderOverlay(changedLocation);
    LogLocationLoadTiming(
      "request",
      resolvedId,
      requestStartedAt,
      requestStartedAt,
      "changed_location=" + (changedLocation ? 1 : 0) +
      " update_tracker=" + (updateTrackerIfDifferent ? 1 : 0) +
      " pending_blocking=" + (pendingBlockingActivation ? 1 : 0) +
      " pending_deferred=" + (pendingDeferredActivation ? 1 : 0) +
      " refresh_under_overlay=" + (refreshPendingLocationUnderOverlay ? 1 : 0)
    );

    if (!changedLocation && (pendingBlockingActivation || pendingDeferredActivation) && !refreshPendingLocationUnderOverlay) {
      LogLocationLoadTiming(
        "skip_duplicate_pending",
        resolvedId,
        requestStartedAt,
        requestStartedAt,
        "pending_blocking=" + (pendingBlockingActivation ? 1 : 0) +
        " pending_deferred=" + (pendingDeferredActivation ? 1 : 0)
      );
      if (logVerbose) {
        RuntimeLog.Log(
          "[LocationManager] Skipping duplicate pending location load id='" + resolvedId +
          "' pending_blocking=" + (pendingBlockingActivation ? 1 : 0) +
          " pending_deferred=" + (pendingDeferredActivation ? 1 : 0) +
          " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
          " overlay_protected=" + (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive ? 1 : 0)
        );
      }
      return true;
    }

    if (refreshPendingLocationUnderOverlay && logVerbose) {
      RuntimeLog.Log(
        "[LocationManager] Refreshing active location under overlay id='" + resolvedId +
        "' pending_blocking=" + (pendingBlockingActivation ? 1 : 0) +
        " pending_deferred=" + (pendingDeferredActivation ? 1 : 0) +
        " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
        " overlay_protected=" + (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive ? 1 : 0)
      );
    }

    ApplyLocationPrefab(activeLocation, changedLocation || refreshPendingLocationUnderOverlay, requestStartedAt);
    MessageBus.Send("LocationLoaded", activeLocation);

    // Initialize enemy loading pipeline if assigned
    if (enemyLoadingPipeline != null)
      enemyLoadingPipeline.RequestLoad(resolvedId);

    if (logVerbose) {
      RuntimeLog.Log(
        "[LocationManager] Loaded location id='" + resolvedId +
        "' name='" + activeLocation.name +
        "' enemies=" + activeLocation.enemies.Count +
        " maxEnemies=" + activeLocation.maxEnemies +
        " objectives=" + (activeLocation.objectives != null ? activeLocation.objectives.Count : 0)
      );
    }
    return true;
  }

  static bool ShouldRefreshPendingLocationUnderOverlay(bool changedLocation) {
    if (changedLocation) return false;
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive) return false;
    return HasPendingBlockingActivationWork || HasPendingDeferredActivationWork;
  }

  static bool IsIntentionalPrefablessMainMenu(LocationInfo info, LocationPrefabData prefabData) {
    if (info == null) return false;
    if (!string.Equals(info.id, LocationEnemyData.MainMenuLocationId, StringComparison.OrdinalIgnoreCase)) {
      return false;
    }
    return prefabData == null || !prefabData.HasConfiguredPrefab();
  }

  void ClearLocationAndNotify() {
    ClearLocationInstance();
    MessageBus.Send("LocationLocationChanged", null);
  }

  void LogMainMenuPrefablessLocation(LocationInfo info) {
    RuntimeLog.Log(
      "[LocationManager] Main menu location intentionally uses no prefab." +
      " location_id='" + (info != null ? info.id : "") + "'" +
      " clearing_active_location=1"
    );
  }

  void ApplyLocationPrefab(LocationInfo info, bool forceRefresh, float requestStartedAt = -1f) {
    if (info == null) {
      ClearLocationAndNotify();
      return;
    }

    var prefabData = info.locationPrefabData;
    if (IsIntentionalPrefablessMainMenu(info, prefabData)) {
      LogMainMenuPrefablessLocation(info);
      ClearLocationAndNotify();
      return;
    }

    var prefabResolveStartedAt = Time.realtimeSinceStartup;
    var prefab = prefabData != null ? prefabData.ResolvePrefab() : null;
    LogLocationLoadTiming(
      "prefab_resolved",
      info.id,
      requestStartedAt,
      prefabResolveStartedAt,
      "prefab=" + (prefab != null ? prefab.name : "-") +
      " force_refresh=" + (forceRefresh ? 1 : 0)
    );

    if (prefab == null) {
      ClearLocationAndNotify();
      return;
    }

    if (!forceRefresh && activeLocationInstance != null) {
      LogLocationLoadTiming(
        "reuse_active_instance",
        info.id,
        requestStartedAt,
        Time.realtimeSinceStartup,
        "instance=" + activeLocationInstance.name
      );
      MessageBus.Send("LocationLocationChanged", activeLocationInstance);
      return;
    }

    ClearLocationInstance();
    var parent = environmentRoot != null
      ? environmentRoot
      : locationRoot != null
        ? locationRoot
        : transform;
    if (TryReuseCachedLocationInstance(info.id, prefab, prefabData, parent, requestStartedAt)) {
      return;
    }
    var blockingLibraries = locationLibraryListScratch;
    var deferredLibraries = locationDeferredLibraryListScratch;
    CollectPrefabLibraries(prefab, blockingLibraries, includeBlockingStages: true, includeDeferredStages: false);
    CollectPrefabLibraries(prefab, deferredLibraries, includeBlockingStages: false, includeDeferredStages: true);
    var blockingLibrarySnapshot = blockingLibraries.Count > 0 ? new List<string>(blockingLibraries) : null;
    var deferredLibrarySnapshot = deferredLibraries.Count > 0 ? new List<string>(deferredLibraries) : null;
    pendingLocationActivationGeneration++;
    pendingBlockingLocationActivationRoutine = StartCoroutine(
      ActivateLocationPrefabWhenResolverReady(
        pendingLocationActivationGeneration,
        info.id,
        prefab,
        prefabData,
        parent,
        blockingLibrarySnapshot,
        deferredLibrarySnapshot
      )
    );
    MessageBus.Send("LocationLocationChanged", null);
  }

  void ClearLocationInstance() {
    var activationWasPending = pendingBlockingLocationActivationRoutine != null ||
                               pendingDeferredLocationActivationRoutine != null;
    StopPendingLocationActivation();
    if (activeLocationInstance == null) return;
    var instance = activeLocationInstance;
    var locationId = activeLocationInstanceId;
    var prefab = activeLocationInstancePrefab;
    var saveSlot = activeLocationInstanceSaveSlot;
    var registryVersion = activeLocationInstanceRegistryVersion;
    var episodeRevision = activeLocationInstanceEpisodeRevision;
    activeLocationInstance = null;
    activeLocationInstanceId = "";
    activeLocationInstancePrefab = null;
    activeLocationInstanceSaveSlot = -1;
    activeLocationInstanceRegistryVersion = -1;
    activeLocationInstanceEpisodeRevision = -1;
    if (instance.activeSelf) {
      instance.SetActive(false);
    }

    if (!activationWasPending &&
        !string.IsNullOrWhiteSpace(locationId) &&
        prefab != null) {
      CacheLocationInstance(
        locationId,
        prefab,
        instance,
        saveSlot,
        registryVersion,
        episodeRevision
      );
      return;
    }

    Destroy(instance);
  }

  void SetActiveLocationInstance(string locationId, GameObject prefab, GameObject instance) {
    activeLocationInstance = instance;
    activeLocationInstanceId = LocationEnemyData.NormalizeLocationId(locationId);
    activeLocationInstancePrefab = prefab;
    activeLocationInstanceSaveSlot = SaveSlotManager.slot;
    activeLocationInstanceRegistryVersion = ActiveContentRegistryRuntime.ReloadVersion;
    activeLocationInstanceEpisodeRevision = ContentEpisodeProgression.EpisodeRevision;
  }

  void CacheLocationInstance(
    string locationId,
    GameObject prefab,
    GameObject instance,
    int saveSlot,
    int registryVersion,
    int episodeRevision
  ) {
    if (instance == null || prefab == null || string.IsNullOrWhiteSpace(locationId)) {
      return;
    }

    if (locationInstanceCache.TryGetValue(locationId, out var existing) &&
        existing != null) {
      if (existing.instance != null && existing.instance != instance) {
        Destroy(existing.instance);
      }
      existing.prefab = prefab;
      existing.instance = instance;
      StampCachedLocationIdentity(
        existing,
        saveSlot,
        registryVersion,
        episodeRevision
      );
      TrimLocationInstanceCache();
      return;
    }

    var cached = new CachedLocationInstance {
      prefab = prefab,
      instance = instance
    };
    StampCachedLocationIdentity(
      cached,
      saveSlot,
      registryVersion,
      episodeRevision
    );
    locationInstanceCache[locationId] = cached;
    TrimLocationInstanceCache();
  }

  bool TryReuseCachedLocationInstance(
    string locationId,
    GameObject prefab,
    LocationPrefabData prefabData,
    Transform parent,
    float requestStartedAt
  ) {
    if (prefab == null || prefabData == null || parent == null) {
      return false;
    }
    if (!locationInstanceCache.TryGetValue(locationId, out var cached) ||
        cached == null ||
        cached.instance == null) {
      locationInstanceCache.Remove(locationId);
      return false;
    }
    if (cached.prefab != prefab || !IsCachedLocationIdentityCurrent(cached)) {
      Destroy(cached.instance);
      locationInstanceCache.Remove(locationId);
      return false;
    }

    var instance = cached.instance;
    var instanceTransform = instance.transform;
    instanceTransform.SetParent(parent, false);
    instanceTransform.localPosition = prefabData.localPosition;
    instanceTransform.localRotation = Quaternion.Euler(prefabData.localEulerAngles);
    instanceTransform.localScale = prefabData.localScale;
    SetActiveLocationInstance(locationId, prefab, instance);
    TouchCachedLocation(cached);
    instance.SetActive(true);
    MessageBus.Send("LocationLocationChanged", instance);
    LogLocationLoadTiming(
      "reuse_cached_instance",
      locationId,
      requestStartedAt,
      Time.realtimeSinceStartup,
      "instance=" + instance.name
    );
    return true;
  }

  void StampCachedLocationIdentity(
    CachedLocationInstance cached,
    int saveSlot,
    int registryVersion,
    int episodeRevision
  ) {
    if (cached == null) {
      return;
    }

    cached.saveSlot = saveSlot;
    cached.registryVersion = registryVersion;
    cached.episodeRevision = episodeRevision;
    TouchCachedLocation(cached);
  }

  void TouchCachedLocation(CachedLocationInstance cached) {
    locationCacheSequence += 1;
    cached.lastUsedSequence = locationCacheSequence;
  }

  static bool IsCachedLocationIdentityCurrent(CachedLocationInstance cached) {
    return cached != null &&
           cached.saveSlot == SaveSlotManager.slot &&
           cached.registryVersion == ActiveContentRegistryRuntime.ReloadVersion &&
           cached.episodeRevision == ContentEpisodeProgression.EpisodeRevision;
  }

  void TrimLocationInstanceCache() {
    while (locationInstanceCache.Count > MaxRetainedLocationInstances) {
      string oldestKey = null;
      CachedLocationInstance oldest = null;
      foreach (var pair in locationInstanceCache) {
        var candidate = pair.Value;
        if (candidate == null || candidate.instance == activeLocationInstance) {
          continue;
        }
        if (oldest != null && candidate.lastUsedSequence >= oldest.lastUsedSequence) {
          continue;
        }
        oldestKey = pair.Key;
        oldest = candidate;
      }

      if (oldestKey == null) {
        return;
      }
      if (oldest != null && oldest.instance != null) {
        Destroy(oldest.instance);
      }
      locationInstanceCache.Remove(oldestKey);
    }
  }

  void DestroyAllLocationInstances() {
    var activeInstance = activeLocationInstance;
    if (activeInstance != null) {
      Destroy(activeInstance);
    }

    foreach (var pair in locationInstanceCache) {
      var cached = pair.Value;
      if (cached == null || cached.instance == null || cached.instance == activeInstance) {
        continue;
      }
      Destroy(cached.instance);
    }
    locationInstanceCache.Clear();
    activeLocationInstance = null;
    activeLocationInstanceId = "";
    activeLocationInstancePrefab = null;
    activeLocationInstanceSaveSlot = -1;
    activeLocationInstanceRegistryVersion = -1;
    activeLocationInstanceEpisodeRevision = -1;
  }

  void StopPendingLocationActivation() {
    pendingLocationActivationGeneration++;
    if (pendingBlockingLocationActivationRoutine != null) {
      StopCoroutine(pendingBlockingLocationActivationRoutine);
      pendingBlockingLocationActivationRoutine = null;
    }
    if (pendingDeferredLocationActivationRoutine != null) {
      StopCoroutine(pendingDeferredLocationActivationRoutine);
      pendingDeferredLocationActivationRoutine = null;
    }
  }
}
