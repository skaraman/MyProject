using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LocationManager : MonoBehaviour {
  const float OverlayLocationResolverBarrierTimeoutSeconds = 8.0f;
  const float ActivationWaitStateLogIntervalSeconds = 5.0f;
  const int ProtectedOverlayActivationMaxOutstandingDesktop = 24;
  const int ProtectedOverlayActivationMaxOutstandingMobile = 16;
  const int ProtectedOverlayActivationMaxInFlightDesktop = 0;
  const int ProtectedOverlayActivationMaxInFlightMobile = 0;
  const int OverlayActivationMaxOutstandingDesktop = 6;
  const int OverlayActivationMaxOutstandingMobile = 4;
  const int OverlayActivationMaxInFlightDesktop = 1;
  const int OverlayActivationMaxInFlightMobile = 0;
  const int RuntimeActivationMaxOutstanding = 2;
  const int RuntimeActivationMaxInFlight = 1;

  sealed class ActivationStagePlan {
    public Transform root;
    public readonly List<Transform> nodes = new();
  }

  static LocationManager runtimeInstance;

  // Former LocationTracker logic, kept static for cross-system access.
  public static string currentLocation = "nowhere";

  public static void UpdateLocation(string newLocation) {
    currentLocation = string.IsNullOrWhiteSpace(newLocation) ? "nowhere" : newLocation.Trim();
    MessageBus.Send("LocationUpdated", currentLocation);
  }

  [Header("Location Runtime")]
  [SerializeField] Transform locationRoot;
  [SerializeField] Transform environmentRoot;
  [SerializeField] bool loadFromTrackerOnStart = true;
  [SerializeField] bool logLoads = true;

  readonly List<Action> actions = new();
  readonly HashSet<string> locationLibraryScratch = new(StringComparer.OrdinalIgnoreCase);
  readonly List<string> locationLibraryListScratch = new(64);
  readonly List<string> relativeNodeSegmentsScratch = new(16);
  LocationInfo activeLocation;
  string currentLocationId = "";
  GameObject activeLocationInstance;
  Coroutine pendingLocationActivationRoutine;
  int pendingLocationActivationGeneration;

  public static bool HasPendingActivationWork =>
    runtimeInstance != null && runtimeInstance.pendingLocationActivationRoutine != null;
  public string CurrentLocationId => currentLocationId;
  public LocationInfo CurrentLocation => activeLocation;
  public IReadOnlyList<LocationObjective> CurrentObjectives =>
    activeLocation != null && activeLocation.objectives != null ? activeLocation.objectives : Array.Empty<LocationObjective>();
  public GameObject ActiveLocationInstance => activeLocationInstance;

  bool ShouldLogVerboseLoadDebug() {
    if (!logLoads) return false;
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

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
    var resolvedId = LocationEnemyData.ResolveRequestedOrDefault(requestedLocationId);
    if (!LocationEnemyData.TryGetLocation(resolvedId, out var info) || info == null) {
      Debug.LogWarning("[LocationManager] Unable to resolve location '" + requestedLocationId + "'.");
      return false;
    }

    if (updateTrackerIfDifferent && !string.Equals(currentLocation, resolvedId, StringComparison.OrdinalIgnoreCase)) {
      UpdateLocation(resolvedId);
      updateTrackerIfDifferent = false;
    }

    var changedLocation = !string.Equals(currentLocationId, resolvedId, StringComparison.OrdinalIgnoreCase);
    currentLocationId = resolvedId;
    activeLocation = info;
    var logVerbose = ShouldLogVerboseLoadDebug();

    if (!changedLocation && pendingLocationActivationRoutine != null) {
      if (logVerbose) {
        Debug.Log(
          "[LocationManager] Skipping duplicate pending location load id='" + resolvedId +
          "' overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
          " overlay_protected=" + (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive ? 1 : 0)
        );
      }
      return true;
    }

    ApplyLocationPrefab(activeLocation, changedLocation);
    MessageBus.Send("LocationLoaded", activeLocation);

    if (logVerbose) {
      Debug.Log(
        "[LocationManager] Loaded location id='" + resolvedId +
        "' name='" + activeLocation.name +
        "' enemies=" + activeLocation.enemies.Count +
        " maxEnemies=" + activeLocation.maxEnemies +
        " objectives=" + (activeLocation.objectives != null ? activeLocation.objectives.Count : 0)
      );
    }
    return true;
  }

  void ApplyLocationPrefab(LocationInfo info, bool forceRefresh) {
    if (info == null) {
      ClearLocationInstance();
      MessageBus.Send("LocationLocationChanged", null);
      return;
    }

    var prefabData = info.locationPrefabData;
    var prefab = prefabData != null ? prefabData.ResolvePrefab() : null;

    if (prefab == null) {
      ClearLocationInstance();
      MessageBus.Send("LocationLocationChanged", null);
      return;
    }

    if (!forceRefresh && activeLocationInstance != null) {
      MessageBus.Send("LocationLocationChanged", activeLocationInstance);
      return;
    }

    ClearLocationInstance();
    var parent = environmentRoot != null
      ? environmentRoot
      : locationRoot != null
        ? locationRoot
        : transform;
    var requiredLibraries = locationLibraryListScratch;
    CollectPrefabLibraries(prefab, requiredLibraries);
    pendingLocationActivationGeneration++;
    pendingLocationActivationRoutine = StartCoroutine(
      ActivateLocationPrefabWhenResolverReady(
        pendingLocationActivationGeneration,
        info.id,
        prefab,
        prefabData,
        parent,
        requiredLibraries
      )
    );
    MessageBus.Send("LocationLocationChanged", null);
  }

  void ClearLocationInstance() {
    StopPendingLocationActivation();
    if (activeLocationInstance == null) return;
    if (activeLocationInstance.activeSelf) {
      activeLocationInstance.SetActive(false);
    }
    Destroy(activeLocationInstance);
    activeLocationInstance = null;
  }

  void StopPendingLocationActivation() {
    pendingLocationActivationGeneration++;
    if (pendingLocationActivationRoutine == null) return;
    StopCoroutine(pendingLocationActivationRoutine);
    pendingLocationActivationRoutine = null;
  }

  bool ShouldWaitForResolverBarrier(List<string> requiredLibraries) {
    if (!Application.isPlaying) return false;
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive) return false;
    return requiredLibraries != null && requiredLibraries.Count > 0;
  }

  void CollectPrefabLibraries(GameObject prefab, List<string> output) {
    if (output == null) return;
    output.Clear();
    if (prefab == null) return;
    locationLibraryScratch.Clear();
    var targets = prefab.GetComponentsInChildren<SpriteWithNormals>(true);
    for (var i = 0; i < targets.Length; i++) {
      var target = targets[i];
      if (target == null || string.IsNullOrWhiteSpace(target.libraryName)) continue;
      locationLibraryScratch.Add(target.libraryName.Trim());
    }
    if (locationLibraryScratch.Count <= 0) return;
    foreach (var library in locationLibraryScratch) {
      output.Add(library);
    }
  }

  IEnumerator ActivateLocationPrefabWhenResolverReady(
    int activationGeneration,
    string locationId,
    GameObject prefab,
    LocationPrefabData prefabData,
    Transform parent,
    List<string> requiredLibraries
  ) {
    var shouldWaitForBarrier = ShouldWaitForResolverBarrier(requiredLibraries);
    var logVerbose = ShouldLogVerboseLoadDebug();
    if (requiredLibraries != null && requiredLibraries.Count > 0) {
      SpriteRuntimeResolver.WarmupLibraries(requiredLibraries);
    }

    var startedAt = Time.realtimeSinceStartup;
    if (shouldWaitForBarrier && logVerbose) {
      Debug.Log(
        "[LocationManager] Deferring location activation id='" + locationId +
        "' libraries=" + requiredLibraries.Count +
        " overlay_active=1"
      );
    }

    while (shouldWaitForBarrier &&
           activationGeneration == pendingLocationActivationGeneration &&
           SpriteStreamingLoadingState.IsLoadingOverlayActive &&
           !SpriteRuntimeResolver.AreShardsReady(requiredLibraries)) {
      var waitedSeconds = Time.realtimeSinceStartup - startedAt;
      if (waitedSeconds >= OverlayLocationResolverBarrierTimeoutSeconds) {
        break;
      }
      yield return null;
    }

    if (activationGeneration != pendingLocationActivationGeneration) {
      pendingLocationActivationRoutine = null;
      yield break;
    }

    if (!string.Equals(currentLocationId, locationId, StringComparison.OrdinalIgnoreCase)) {
      pendingLocationActivationRoutine = null;
      yield break;
    }

    if (shouldWaitForBarrier && logVerbose) {
      var totalWaitSeconds = Time.realtimeSinceStartup - startedAt;
      var shardsReady = SpriteRuntimeResolver.AreShardsReady(requiredLibraries);
      Debug.Log(
        "[LocationManager] Activating deferred location id='" + locationId +
        "' waited_s=" + totalWaitSeconds.ToString("0.000") +
        " shards_ready=" + (shardsReady ? 1 : 0) +
        " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0)
      );
    }

    if (TryInstantiateStagedLocationPrefab(locationId, prefab, prefabData, parent, out var stagedInstance, out var stagePlans)) {
      activeLocationInstance = stagedInstance;
      MessageBus.Send("LocationLocationChanged", activeLocationInstance);
      yield return ActivateLocationStageChildren(activationGeneration, locationId, stagePlans);
      if (activationGeneration == pendingLocationActivationGeneration) {
        pendingLocationActivationRoutine = null;
      }
      yield break;
    }

    activeLocationInstance = InstantiateConfiguredLocationPrefab(locationId, prefab, prefabData, parent);
    pendingLocationActivationRoutine = null;
    MessageBus.Send("LocationLocationChanged", activeLocationInstance);
  }

  bool TryInstantiateStagedLocationPrefab(
    string locationId,
    GameObject prefab,
    LocationPrefabData prefabData,
    Transform parent,
    out GameObject stagedInstance,
    out List<ActivationStagePlan> stagePlans
  ) {
    stagedInstance = null;
    stagePlans = null;
    if (prefab == null || prefabData == null || parent == null) return false;

    var wrapper = new GameObject("Location_" + locationId + "_" + prefab.name);
    wrapper.layer = prefab.layer;
    var wrapperTransform = wrapper.transform;
    wrapperTransform.SetParent(parent, false);
    wrapperTransform.localPosition = prefabData.localPosition;
    wrapperTransform.localRotation = Quaternion.Euler(prefabData.localEulerAngles);
    wrapperTransform.localScale = prefabData.localScale;
    wrapper.SetActive(false);

    var contentRoot = Instantiate(prefab, wrapperTransform, false);
    if (!TryBuildStagePlans(contentRoot.transform, out stagePlans) || stagePlans == null || stagePlans.Count <= 0) {
      Destroy(wrapper);
      return false;
    }

    PrepareStagePlansForActivation(stagePlans);
    wrapper.SetActive(true);
    stagedInstance = wrapper;
    return true;
  }

  static bool TryBuildStagePlans(Transform contentRoot, out List<ActivationStagePlan> stagePlans) {
    stagePlans = null;
    if (contentRoot == null) return false;

    var orderedPlans = new List<ActivationStagePlan>(4);
    AddStagePlan(orderedPlans, FindDirectChild(contentRoot, "BG"));
    var fg = FindDirectChild(contentRoot, "FG");
    AddStagePlan(orderedPlans, FindDirectChild(fg, "Static"));
    AddStagePlan(orderedPlans, FindDirectChild(fg, "Dynamic"));
    AddStagePlan(orderedPlans, FindDirectChild(fg, "Destruct"));
    if (orderedPlans.Count <= 0) return false;
    stagePlans = orderedPlans;
    return true;
  }

  static void AddStagePlan(List<ActivationStagePlan> stagePlans, Transform stageRoot) {
    if (stagePlans == null || stageRoot == null) return;
    var plan = new ActivationStagePlan { root = stageRoot };
    CollectActivationNodesDepthFirst(stageRoot, plan.nodes);
    stagePlans.Add(plan);
  }

  static Transform FindDirectChild(Transform parent, string childName) {
    if (parent == null || string.IsNullOrWhiteSpace(childName)) return null;
    for (var i = 0; i < parent.childCount; i++) {
      var child = parent.GetChild(i);
      if (child == null) continue;
      if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase)) return child;
    }
    return null;
  }

  static void CollectActivationNodesDepthFirst(Transform parent, List<Transform> nodes) {
    if (parent == null || nodes == null) return;
    for (var i = 0; i < parent.childCount; i++) {
      var child = parent.GetChild(i);
      if (child == null) continue;
      nodes.Add(child);
      CollectActivationNodesDepthFirst(child, nodes);
    }
  }

  static void PrepareStagePlansForActivation(List<ActivationStagePlan> stagePlans) {
    if (stagePlans == null) return;
    for (var i = 0; i < stagePlans.Count; i++) {
      var plan = stagePlans[i];
      if (plan == null || plan.root == null) continue;
      SetDescendantsActive(plan.root, active: false);
      plan.root.gameObject.SetActive(false);
    }
  }

  static void SetDescendantsActive(Transform parent, bool active) {
    if (parent == null) return;
    for (var i = 0; i < parent.childCount; i++) {
      var child = parent.GetChild(i);
      if (child == null) continue;
      SetDescendantsActive(child, active);
      child.gameObject.SetActive(active);
    }
  }

  IEnumerator ActivateLocationStageChildren(int activationGeneration, string locationId, List<ActivationStagePlan> stagePlans) {
    if (stagePlans == null || stagePlans.Count <= 0) yield break;
    var logVerbose = ShouldLogVerboseLoadDebug();

    for (var stageIndex = 0; stageIndex < stagePlans.Count; stageIndex++) {
      if (activationGeneration != pendingLocationActivationGeneration) yield break;
      var stagePlan = stagePlans[stageIndex];
      if (stagePlan == null || stagePlan.root == null) continue;
      var stageRoot = stagePlan.root;
      var stageTarget = "stage_root:" + stageRoot.name;

      yield return WaitForActivationCapacity(activationGeneration, locationId, stageTarget);
      if (activationGeneration != pendingLocationActivationGeneration) yield break;

      stageRoot.gameObject.SetActive(true);
      if (logVerbose) {
        Debug.Log(
          "[LocationManager] Activating stage root id='" + locationId +
          "' stage='" + stageRoot.name +
          "' node_count=" + stagePlan.nodes.Count
        );
      }
      yield return null;

      for (var nodeIndex = 0; nodeIndex < stagePlan.nodes.Count; nodeIndex++) {
        if (activationGeneration != pendingLocationActivationGeneration) yield break;
        var node = stagePlan.nodes[nodeIndex];
        if (node == null) continue;
        var nodePath = BuildRelativeNodePath(stageRoot, node);

        yield return WaitForActivationCapacity(activationGeneration, locationId, "node:" + nodePath);
        if (activationGeneration != pendingLocationActivationGeneration) yield break;

        node.gameObject.SetActive(true);
        if (logVerbose) {
          var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
          var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
          var outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount + deferredPending, 0);
          Debug.Log(
            "[LocationManager] Activated staged node id='" + locationId +
            "' stage='" + stageRoot.name +
            "' node='" + nodePath +
            "' queued=" + queue.queuedCount +
            " in_flight=" + queue.inFlightCount +
            " deferred=" + deferredPending +
            " outstanding=" + outstanding +
            " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0)
          );
        }
        yield return null;
      }
    }

    if (logVerbose) {
      Debug.Log(
        "[LocationManager] Completed staged activation id='" + locationId +
        "' stages=" + stagePlans.Count
      );
    }
  }

  string BuildRelativeNodePath(Transform stageRoot, Transform node) {
    if (node == null) return "-";
    if (stageRoot == null || ReferenceEquals(stageRoot, node)) return node.name;

    var segments = relativeNodeSegmentsScratch;
    segments.Clear();
    var current = node;
    while (current != null && !ReferenceEquals(current, stageRoot)) {
      segments.Add(current.name);
      current = current.parent;
    }
    segments.Reverse();
    var relativePath = segments.Count > 0 ? string.Join("/", segments) : node.name;
    segments.Clear();
    return relativePath;
  }

  IEnumerator WaitForActivationCapacity(int activationGeneration, string locationId, string activationTarget) {
    var nextLogAt = Time.realtimeSinceStartup + ActivationWaitStateLogIntervalSeconds;
    var logVerbose = ShouldLogVerboseLoadDebug();
    while (activationGeneration == pendingLocationActivationGeneration) {
      if (HasActivationCapacity(
            out var queue,
            out var deferredPending,
            out var outstanding,
            out var maxOutstanding,
            out var maxInFlight,
            out var resolverIdle)) {
        yield break;
      }

      if (logVerbose && Time.realtimeSinceStartup >= nextLogAt) {
        Debug.Log(
          "[LocationManager] Waiting for activation capacity id='" + locationId +
          "' target='" + activationTarget +
          "' mode=" + ResolveActivationCapacityMode() +
          " queued=" + queue.queuedCount +
          " in_flight=" + queue.inFlightCount +
          " deferred=" + deferredPending +
          " outstanding=" + outstanding +
          " max_outstanding=" + maxOutstanding +
          " max_in_flight=" + maxInFlight +
          " resolver_idle=" + (resolverIdle ? 1 : 0) +
          " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
          " overlay_protected=" + (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive ? 1 : 0)
        );
        nextLogAt = Time.realtimeSinceStartup + ActivationWaitStateLogIntervalSeconds;
      }
      yield return null;
    }
  }

  static bool HasActivationCapacity(
    out TextureResidencyCache.QueueSnapshot queue,
    out int deferredPending,
    out int outstanding,
    out int maxOutstanding,
    out int maxInFlight,
    out bool resolverIdle
  ) {
    queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount + deferredPending, 0);
    ResolveActivationQueueThresholds(out maxOutstanding, out maxInFlight);
    resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
    if (!resolverIdle) return false;
    if (outstanding > maxOutstanding) return false;
    if (queue.inFlightCount > maxInFlight) return false;
    return true;
  }

  static void ResolveActivationQueueThresholds(out int maxOutstanding, out int maxInFlight) {
    if (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive) {
      maxOutstanding = Application.isMobilePlatform
        ? ProtectedOverlayActivationMaxOutstandingMobile
        : ProtectedOverlayActivationMaxOutstandingDesktop;
      maxInFlight = Application.isMobilePlatform
        ? ProtectedOverlayActivationMaxInFlightMobile
        : ProtectedOverlayActivationMaxInFlightDesktop;
      return;
    }

    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      maxOutstanding = Application.isMobilePlatform
        ? OverlayActivationMaxOutstandingMobile
        : OverlayActivationMaxOutstandingDesktop;
      maxInFlight = Application.isMobilePlatform
        ? OverlayActivationMaxInFlightMobile
        : OverlayActivationMaxInFlightDesktop;
      return;
    }

    maxOutstanding = RuntimeActivationMaxOutstanding;
    maxInFlight = RuntimeActivationMaxInFlight;
  }

  static string ResolveActivationCapacityMode() {
    if (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive) return "protected_overlay";
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) return "overlay";
    return "runtime";
  }

  GameObject InstantiateConfiguredLocationPrefab(
    string locationId,
    GameObject prefab,
    LocationPrefabData prefabData,
    Transform parent
  ) {
    var instance = Instantiate(prefab);
    instance.name = "Location_" + locationId + "_" + prefab.name;
    instance.transform.SetParent(parent, false);
    instance.transform.localPosition = prefabData.localPosition;
    instance.transform.localRotation = Quaternion.Euler(prefabData.localEulerAngles);
    instance.transform.localScale = prefabData.localScale;
    return instance;
  }
}
