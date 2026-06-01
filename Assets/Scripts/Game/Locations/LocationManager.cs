using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LocationManager : MonoBehaviour {
  const float OverlayLocationResolverBarrierTimeoutSeconds = 8.0f;
  const float ActivationCapacityWaitTimeoutSeconds = 2.0f;
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
  const int ProtectedOverlayActivationBurstPerFrame = 4;
  const int OverlayActivationBurstPerFrame = 2;
  const int RuntimeActivationBurstPerFrame = 1;
 
  enum ActivationStageRole {
    Blocking = 0,
    Deferred = 1
  }

  sealed class ActivationStagePlan {
    public Transform root;
    public ActivationStageRole role;
    public readonly List<Transform> nodes = new();
    public bool BlocksReveal => role == ActivationStageRole.Blocking;
  }

  static LocationManager runtimeInstance;

  // Former LocationTracker logic, kept static for cross-system access.
  public static string currentLocation = "nowhere";

  public static void UpdateLocation(string newLocation) {
    currentLocation = string.IsNullOrWhiteSpace(newLocation) ? "nowhere" : newLocation.Trim();
    MessageBus.Send("LocationUpdated", currentLocation);
  }

  [Header("Location Runtime")]
  [Header("Enemy Loading Pipeline")]
  [SerializeField] LocationLoadingPipeline enemyLoadingPipeline;

  [SerializeField] Transform locationRoot;
  [SerializeField] Transform environmentRoot;
  [SerializeField] bool loadFromTrackerOnStart = true;
  [SerializeField] bool logLoads = true;

  readonly List<Action> actions = new();
  readonly HashSet<string> locationLibraryScratch = new(StringComparer.OrdinalIgnoreCase);
  readonly List<string> locationLibraryListScratch = new(64);
  readonly List<string> locationDeferredLibraryListScratch = new(64);
  readonly List<string> relativeNodeSegmentsScratch = new(16);
  LocationInfo activeLocation;
  string currentLocationId = "";
  GameObject activeLocationInstance;
  Coroutine pendingBlockingLocationActivationRoutine;
  Coroutine pendingDeferredLocationActivationRoutine;
  int pendingLocationActivationGeneration;

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
    var requestStartedAt = Time.realtimeSinceStartup;
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
    var pendingBlockingActivation = HasPendingBlockingActivationWork;
    var pendingDeferredActivation = HasPendingDeferredActivationWork;
    var refreshPendingLocationUnderOverlay = ShouldRefreshPendingLocationUnderOverlay(changedLocation);

    if (!changedLocation && (pendingBlockingActivation || pendingDeferredActivation) && !refreshPendingLocationUnderOverlay) {
      if (logVerbose) {
        Debug.Log(
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
      Debug.Log(
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
    Debug.Log(
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
    if (pendingBlockingLocationActivationRoutine != null) {
      StopCoroutine(pendingBlockingLocationActivationRoutine);
      pendingBlockingLocationActivationRoutine = null;
    }
    if (pendingDeferredLocationActivationRoutine != null) {
      StopCoroutine(pendingDeferredLocationActivationRoutine);
      pendingDeferredLocationActivationRoutine = null;
    }
  }

  bool ShouldWaitForResolverBarrier(List<string> requiredLibraries) {
    if (!Application.isPlaying) return false;
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive) return false;
    return requiredLibraries != null && requiredLibraries.Count > 0;
  }

  void CollectPrefabLibraries(
    GameObject prefab,
    List<string> output,
    bool includeBlockingStages = true,
    bool includeDeferredStages = true
  ) {
    if (output == null) return;
    output.Clear();
    if (prefab == null) return;
    locationLibraryScratch.Clear();
    if (includeBlockingStages && includeDeferredStages) {
      CollectStageLibraries(prefab.transform, locationLibraryScratch);
    }
    else {
      var root = prefab.transform;
      var bg = FindDirectChild(root, "BG");
      var fg = FindDirectChild(root, "FG");
      if (includeBlockingStages) {
        CollectStageLibraries(bg, locationLibraryScratch);
        CollectStageLibraries(FindDirectChild(fg, "Static"), locationLibraryScratch);
        if (locationLibraryScratch.Count <= 0) {
          CollectStageLibraries(root, locationLibraryScratch);
        }
      }
      if (includeDeferredStages) {
        CollectStageLibraries(FindDirectChild(fg, "Dynamic"), locationLibraryScratch);
        CollectStageLibraries(FindDirectChild(fg, "Destruct"), locationLibraryScratch);
      }
    }
    if (locationLibraryScratch.Count <= 0) return;
    foreach (var library in locationLibraryScratch) {
      output.Add(library);
    }
  }

  static void CollectStageLibraries(Transform root, HashSet<string> output) {
    if (root == null || output == null) return;
    var targets = root.GetComponentsInChildren<SpriteWithNormals>(true);
    for (var i = 0; i < targets.Length; i++) {
      var target = targets[i];
      if (target == null || string.IsNullOrWhiteSpace(target.libraryName)) continue;
      output.Add(target.libraryName.Trim());
    }
  }

  IEnumerator ActivateLocationPrefabWhenResolverReady(
    int activationGeneration,
    string locationId,
    GameObject prefab,
    LocationPrefabData prefabData,
    Transform parent,
    List<string> blockingLibraries,
    List<string> deferredLibraries
  ) {
    var promoteDeferredStages = ShouldPromoteDeferredStagesDuringOverlay();
    var barrierLibraries = promoteDeferredStages
      ? MergeLibraryLists(blockingLibraries, deferredLibraries)
      : blockingLibraries;
    var shouldWaitForBarrier = ShouldWaitForResolverBarrier(barrierLibraries);
    var logVerbose = ShouldLogVerboseLoadDebug();
    if (barrierLibraries != null && barrierLibraries.Count > 0) {
      SpriteRuntimeResolver.WarmupLibraries(barrierLibraries);
    }

    var startedAt = Time.realtimeSinceStartup;
    if (shouldWaitForBarrier && logVerbose) {
      Debug.Log(
        "[LocationManager] Deferring location activation id='" + locationId +
        "' libraries=" + barrierLibraries.Count +
        " overlay_active=1"
      );
    }

    while (shouldWaitForBarrier &&
           activationGeneration == pendingLocationActivationGeneration &&
           SpriteStreamingLoadingState.IsLoadingOverlayActive &&
           !SpriteRuntimeResolver.AreShardsReady(barrierLibraries)) {
      var waitedSeconds = Time.realtimeSinceStartup - startedAt;
      if (waitedSeconds >= OverlayLocationResolverBarrierTimeoutSeconds) {
        break;
      }
      yield return null;
    }

    if (activationGeneration != pendingLocationActivationGeneration) {
      pendingBlockingLocationActivationRoutine = null;
      yield break;
    }

    if (!string.Equals(currentLocationId, locationId, StringComparison.OrdinalIgnoreCase)) {
      pendingBlockingLocationActivationRoutine = null;
      yield break;
    }

    if (shouldWaitForBarrier) {
      var totalWaitSeconds = Time.realtimeSinceStartup - startedAt;
      var shardsReady = SpriteRuntimeResolver.AreShardsReady(barrierLibraries);
      LogLocationLoadTiming(
        "resolver_barrier",
        locationId,
        startedAt,
        startedAt,
        "wait_ms=" + (totalWaitSeconds * 1000f).ToString("0.0") +
        " libraries=" + (barrierLibraries != null ? barrierLibraries.Count : 0) +
        " shards_ready=" + (shardsReady ? 1 : 0) +
        " timed_out=" + (!shardsReady && totalWaitSeconds >= OverlayLocationResolverBarrierTimeoutSeconds ? 1 : 0)
      );
    }

    var instantiateStartedAt = Time.realtimeSinceStartup;
    if (TryInstantiateStagedLocationPrefab(locationId, prefab, prefabData, parent, out var stagedInstance, out var stagePlans)) {
      activeLocationInstance = stagedInstance;
      MessageBus.Send("LocationLocationChanged", activeLocationInstance);
      SplitActivationStagePlans(stagePlans, out var blockingPlans, out var deferredPlans);
      if (promoteDeferredStages && deferredPlans.Count > 0) {
        PromoteDeferredStagePlansToBlocking(blockingPlans, deferredPlans);
      }
      if (logVerbose) {
        Debug.Log(
          "[LocationManager] Stage split id='" + locationId +
          "' blocking_stages=" + blockingPlans.Count +
          " deferred_stages=" + deferredPlans.Count +
          " promoted_deferred=" + (promoteDeferredStages ? 1 : 0)
        );
      }
      LogLocationLoadTiming(
        "prefab_instantiated",
        locationId,
        instantiateStartedAt,
        instantiateStartedAt,
        "instantiate_ms=" + ((Time.realtimeSinceStartup - instantiateStartedAt) * 1000f).ToString("0.0") +
        " stage_plans=" + (stagePlans != null ? stagePlans.Count : 0) +
        " blocking_stages=" + blockingPlans.Count +
        " deferred_stages=" + deferredPlans.Count +
        " nodes=" + CountActivationNodes(stagePlans)
      );
      if (blockingPlans.Count > 0) {
        yield return ActivateLocationStageChildren(activationGeneration, locationId, blockingPlans);
      }
      if (activationGeneration == pendingLocationActivationGeneration) {
        pendingBlockingLocationActivationRoutine = null;
      }
      if (activationGeneration != pendingLocationActivationGeneration) {
        yield break;
      }
      if (deferredPlans.Count > 0) {
        pendingDeferredLocationActivationRoutine = StartCoroutine(
          ActivateDeferredLocationStageChildrenAfterReveal(
            activationGeneration,
            locationId,
            deferredPlans,
            deferredLibraries
          )
        );
      }
      yield break;
    }

    instantiateStartedAt = Time.realtimeSinceStartup;
    activeLocationInstance = InstantiateConfiguredLocationPrefab(locationId, prefab, prefabData, parent);
    pendingBlockingLocationActivationRoutine = null;
    MessageBus.Send("LocationLocationChanged", activeLocationInstance);
    LogLocationLoadTiming(
      "prefab_instantiated",
      locationId,
      instantiateStartedAt,
      instantiateStartedAt,
      "instantiate_ms=" + ((Time.realtimeSinceStartup - instantiateStartedAt) * 1000f).ToString("0.0") +
      " staged=0"
    );
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
    AddStagePlan(orderedPlans, FindDirectChild(contentRoot, "BG"), ActivationStageRole.Blocking);
    var fg = FindDirectChild(contentRoot, "FG");
    AddStagePlan(orderedPlans, FindDirectChild(fg, "Static"), ActivationStageRole.Blocking);
    AddStagePlan(orderedPlans, FindDirectChild(fg, "Dynamic"), ActivationStageRole.Deferred);
    AddStagePlan(orderedPlans, FindDirectChild(fg, "Destruct"), ActivationStageRole.Deferred);
    if (orderedPlans.Count <= 0) return false;
    var hasBlockingStage = false;
    for (var i = 0; i < orderedPlans.Count; i++) {
      if (!orderedPlans[i].BlocksReveal) continue;
      hasBlockingStage = true;
      break;
    }
    if (!hasBlockingStage) {
      orderedPlans[0].role = ActivationStageRole.Blocking;
    }
    stagePlans = orderedPlans;
    return true;
  }

  static void AddStagePlan(List<ActivationStagePlan> stagePlans, Transform stageRoot, ActivationStageRole role) {
    if (stagePlans == null || stageRoot == null) return;
    var plan = new ActivationStagePlan { root = stageRoot, role = role };
    CollectActivationNodesDepthFirst(stageRoot, plan.nodes);
    stagePlans.Add(plan);
  }

  static void SplitActivationStagePlans(
    List<ActivationStagePlan> source,
    out List<ActivationStagePlan> blockingPlans,
    out List<ActivationStagePlan> deferredPlans
  ) {
    blockingPlans = new List<ActivationStagePlan>(source != null ? source.Count : 0);
    deferredPlans = new List<ActivationStagePlan>(source != null ? source.Count : 0);
    if (source == null || source.Count <= 0) return;

    for (var i = 0; i < source.Count; i++) {
      var plan = source[i];
      if (plan == null || plan.root == null) continue;
      if (plan.BlocksReveal) {
        blockingPlans.Add(plan);
      }
      else {
        deferredPlans.Add(plan);
      }
    }

    if (blockingPlans.Count <= 0 && deferredPlans.Count > 0) {
      blockingPlans.Add(deferredPlans[0]);
      deferredPlans.RemoveAt(0);
    }
  }

  static bool ShouldPromoteDeferredStagesDuringOverlay() {
    return false;
  }

  static List<string> MergeLibraryLists(List<string> primary, List<string> secondary) {
    if (primary == null || primary.Count <= 0) {
      return secondary != null && secondary.Count > 0 ? new List<string>(secondary) : primary;
    }
    if (secondary == null || secondary.Count <= 0) return primary;

    var merged = new List<string>(primary.Count + secondary.Count);
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    AppendLibraries(primary, merged, seen);
    AppendLibraries(secondary, merged, seen);
    return merged;
  }

  static void AppendLibraries(List<string> source, List<string> destination, HashSet<string> seen) {
    if (source == null || destination == null || seen == null) return;
    for (var i = 0; i < source.Count; i++) {
      var library = source[i];
      if (string.IsNullOrWhiteSpace(library)) continue;
      var normalized = library.Trim();
      if (!seen.Add(normalized)) continue;
      destination.Add(normalized);
    }
  }

  static void PromoteDeferredStagePlansToBlocking(
    List<ActivationStagePlan> blockingPlans,
    List<ActivationStagePlan> deferredPlans
  ) {
    if (blockingPlans == null || deferredPlans == null || deferredPlans.Count <= 0) return;
    for (var i = 0; i < deferredPlans.Count; i++) {
      var plan = deferredPlans[i];
      if (plan == null || plan.root == null) continue;
      blockingPlans.Add(plan);
    }
    deferredPlans.Clear();
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
    var activationsThisFrame = 0;
    var activationStartedAt = Time.realtimeSinceStartup;
    var bgMs = 0f;
    var fgStaticMs = 0f;
    var fgDynamicMs = 0f;
    var fgDestructMs = 0f;
    var otherMs = 0f;
    var totalNodes = 0;
    var blockingStages = 0;
    var deferredStages = 0;

    for (var stageIndex = 0; stageIndex < stagePlans.Count; stageIndex++) {
      if (activationGeneration != pendingLocationActivationGeneration) yield break;
      var stagePlan = stagePlans[stageIndex];
      if (stagePlan == null || stagePlan.root == null) continue;
      var stageRoot = stagePlan.root;
      var stageTarget = "stage_root:" + stageRoot.name;
      var stageStartedAt = Time.realtimeSinceStartup;
      if (stagePlan.BlocksReveal) blockingStages++;
      else deferredStages++;

      yield return WaitForActivationCapacity(activationGeneration, locationId, stageTarget);
      if (activationGeneration != pendingLocationActivationGeneration) yield break;

      stageRoot.gameObject.SetActive(true);
      if (ShouldYieldAfterActivationStep(ref activationsThisFrame)) {
        yield return null;
      }

      for (var nodeIndex = 0; nodeIndex < stagePlan.nodes.Count; nodeIndex++) {
        if (activationGeneration != pendingLocationActivationGeneration) yield break;
        var node = stagePlan.nodes[nodeIndex];
        if (node == null) continue;
        var nodePath = BuildRelativeNodePath(stageRoot, node);

        yield return WaitForActivationCapacity(activationGeneration, locationId, "node:" + nodePath);
        if (activationGeneration != pendingLocationActivationGeneration) yield break;

        node.gameObject.SetActive(true);
        totalNodes++;
        if (ShouldYieldAfterActivationStep(ref activationsThisFrame)) {
          yield return null;
        }
      }

      var stageMs = (Time.realtimeSinceStartup - stageStartedAt) * 1000f;
      AccumulateStageActivationMs(stageRoot.name, stageMs, ref bgMs, ref fgStaticMs, ref fgDynamicMs, ref fgDestructMs, ref otherMs);
    }

    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    LogLocationLoadTiming(
      "stage_activation",
      locationId,
      activationStartedAt,
      activationStartedAt,
      "total_ms=" + ((Time.realtimeSinceStartup - activationStartedAt) * 1000f).ToString("0.0") +
      " bg_ms=" + bgMs.ToString("0.0") +
      " fg_static_ms=" + fgStaticMs.ToString("0.0") +
      " fg_dynamic_ms=" + fgDynamicMs.ToString("0.0") +
      " fg_destruct_ms=" + fgDestructMs.ToString("0.0") +
      " other_ms=" + otherMs.ToString("0.0") +
      " stages=" + stagePlans.Count +
      " blocking_stages=" + blockingStages +
      " deferred_stages=" + deferredStages +
      " nodes=" + totalNodes +
      " queued=" + queue.queuedCount +
      " in_flight=" + queue.inFlightCount +
      " deferred=" + deferredPending
    );
  }

  IEnumerator ActivateDeferredLocationStageChildrenAfterReveal(
    int activationGeneration,
    string locationId,
    List<ActivationStagePlan> deferredPlans,
    List<string> deferredLibraries
  ) {
    if (deferredLibraries != null && deferredLibraries.Count > 0) {
      SpriteRuntimeResolver.WarmupLibraries(deferredLibraries);
    }
    while (activationGeneration == pendingLocationActivationGeneration &&
           SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      yield return null;
    }
    if (activationGeneration != pendingLocationActivationGeneration ||
        !string.Equals(currentLocationId, locationId, StringComparison.OrdinalIgnoreCase)) {
      pendingDeferredLocationActivationRoutine = null;
      yield break;
    }

    yield return ActivateLocationStageChildren(activationGeneration, locationId, deferredPlans);
    if (activationGeneration == pendingLocationActivationGeneration) {
      pendingDeferredLocationActivationRoutine = null;
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
    var startedAt = Time.realtimeSinceStartup;
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

      if (ActivationCapacityWaitTimeoutSeconds > 0f &&
          Time.realtimeSinceStartup - startedAt >= ActivationCapacityWaitTimeoutSeconds) {
        if (logVerbose) {
          Debug.LogWarning(
            "[LocationManager] Activation capacity timeout id='" + locationId +
            "' target='" + activationTarget +
            "' mode=" + ResolveActivationCapacityMode() +
            " queued=" + queue.queuedCount +
            " in_flight=" + queue.inFlightCount +
            " deferred=" + deferredPending +
            " outstanding=" + outstanding +
            " max_outstanding=" + maxOutstanding +
            " max_in_flight=" + maxInFlight +
            " resolver_idle=" + (resolverIdle ? 1 : 0)
          );
        }
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
    if (outstanding > maxOutstanding) return false;
    if (queue.inFlightCount > maxInFlight) return false;
    return true;
  }

  static bool ShouldYieldAfterActivationStep(ref int activationsThisFrame) {
    activationsThisFrame++;
    if (activationsThisFrame < ResolveActivationBurstPerFrame()) {
      return false;
    }

    activationsThisFrame = 0;
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

  static int ResolveActivationBurstPerFrame() {
    if (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive) {
      return ProtectedOverlayActivationBurstPerFrame;
    }
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      return OverlayActivationBurstPerFrame;
    }
    return RuntimeActivationBurstPerFrame;
  }

  static string ResolveActivationCapacityMode() {
    if (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive) return "protected_overlay";
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) return "overlay";
    return "runtime";
  }

  void LogLocationLoadTiming(string stage, string locationId, float requestStartedAt, float stageStartedAt, string extraFields = "") {
    if (!ShouldLogVerboseLoadDebug()) return;
    var now = Time.realtimeSinceStartup;
    Debug.Log(
      "[LocationManager][LocationLoadTiming] stage=" + (string.IsNullOrWhiteSpace(stage) ? "-" : stage.Trim()) +
      " location=" + (string.IsNullOrWhiteSpace(locationId) ? "-" : locationId.Trim()) +
      " elapsed_ms=" + (requestStartedAt >= 0f ? ((now - requestStartedAt) * 1000f).ToString("0.0") : "-") +
      " stage_ms=" + (stageStartedAt >= 0f ? ((now - stageStartedAt) * 1000f).ToString("0.0") : "-") +
      " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
      " overlay_protected=" + (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive ? 1 : 0) +
      (string.IsNullOrWhiteSpace(extraFields) ? "" : " " + extraFields.Trim())
    );
  }

  static int CountActivationNodes(List<ActivationStagePlan> stagePlans) {
    if (stagePlans == null || stagePlans.Count <= 0) return 0;
    var count = 0;
    for (var i = 0; i < stagePlans.Count; i++) {
      var plan = stagePlans[i];
      if (plan == null || plan.nodes == null) continue;
      count += plan.nodes.Count;
    }
    return count;
  }

  static void AccumulateStageActivationMs(
    string stageName,
    float stageMs,
    ref float bgMs,
    ref float fgStaticMs,
    ref float fgDynamicMs,
    ref float fgDestructMs,
    ref float otherMs
  ) {
    if (string.Equals(stageName, "BG", StringComparison.OrdinalIgnoreCase)) {
      bgMs += stageMs;
    }
    else if (string.Equals(stageName, "Static", StringComparison.OrdinalIgnoreCase)) {
      fgStaticMs += stageMs;
    }
    else if (string.Equals(stageName, "Dynamic", StringComparison.OrdinalIgnoreCase)) {
      fgDynamicMs += stageMs;
    }
    else if (string.Equals(stageName, "Destruct", StringComparison.OrdinalIgnoreCase)) {
      fgDestructMs += stageMs;
    }
    else {
      otherMs += stageMs;
    }
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
