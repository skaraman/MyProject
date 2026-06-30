using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class LocationManager {
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
      LogLocationStagePlanSummary("split", locationId, blockingPlans, deferredPlans, promoteDeferredStages);
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
        LogLocationStagePlanSummary("deferred_queued_after_blocking", locationId, null, deferredPlans, false);
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

  IEnumerator ActivateLocationStageChildren(int activationGeneration, string locationId, List<ActivationStagePlan> stagePlans) {
    if (stagePlans == null || stagePlans.Count <= 0) yield break;
    var activationsThisFrame = 0;
    var activationStartedAt = Time.realtimeSinceStartup;
    var nextProgressLogAt = activationStartedAt + ActivationProgressLogIntervalSeconds;
    var logActivationTrace = ShouldLogLocationActivationTrace();
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
      if (logActivationTrace) {
        LogLocationActivationProgress(
          "stage_begin",
          locationId,
          activationStartedAt,
          stageStartedAt,
          stageIndex,
          stagePlans.Count,
          stageRoot.name,
          "-",
          0,
          stagePlan.nodes.Count,
          totalNodes
        );
      }

      yield return WaitForActivationCapacity(activationGeneration, locationId, stageTarget);
      if (activationGeneration != pendingLocationActivationGeneration) yield break;

      var stepStartedAt = Time.realtimeSinceStartup;
      LogLocationActivationSetActiveBegin(
        locationId,
        activationStartedAt,
        stageRoot,
        stageRoot,
        stageTarget,
        stageIndex,
        -1,
        stagePlan.nodes.Count,
        totalNodes,
        true
      );
      stageRoot.gameObject.SetActive(true);
      LogSlowLocationActivationStep(locationId, activationStartedAt, stageRoot.name, stageTarget, stepStartedAt, totalNodes);
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

        stepStartedAt = Time.realtimeSinceStartup;
        LogLocationActivationSetActiveBegin(
          locationId,
          activationStartedAt,
          stageRoot,
          node,
          nodePath,
          stageIndex,
          nodeIndex,
          stagePlan.nodes.Count,
          totalNodes,
          false
        );
        node.gameObject.SetActive(true);
        totalNodes++;
        LogSlowLocationActivationStep(locationId, activationStartedAt, stageRoot.name, nodePath, stepStartedAt, totalNodes);
        if (logActivationTrace && Time.realtimeSinceStartup >= nextProgressLogAt) {
          LogLocationActivationProgress(
            "stage_progress",
            locationId,
            activationStartedAt,
            stageStartedAt,
            stageIndex,
            stagePlans.Count,
            stageRoot.name,
            nodePath,
            nodeIndex + 1,
            stagePlan.nodes.Count,
            totalNodes
          );
          nextProgressLogAt = Time.realtimeSinceStartup + ActivationProgressLogIntervalSeconds;
        }
        if (ShouldYieldAfterActivationStep(ref activationsThisFrame)) {
          yield return null;
        }
      }

      var stageMs = (Time.realtimeSinceStartup - stageStartedAt) * 1000f;
      AccumulateStageActivationMs(stageRoot.name, stageMs, ref bgMs, ref fgStaticMs, ref fgDynamicMs, ref fgDestructMs, ref otherMs);
      if (logActivationTrace) {
        LogLocationActivationProgress(
          "stage_complete",
          locationId,
          activationStartedAt,
          stageStartedAt,
          stageIndex,
          stagePlans.Count,
          stageRoot.name,
          "-",
          stagePlan.nodes.Count,
          stagePlan.nodes.Count,
          totalNodes
        );
      }
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
    LogLocationStagePlanSummary("deferred_waiting_for_reveal", locationId, null, deferredPlans, false);
    while (activationGeneration == pendingLocationActivationGeneration &&
           SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      yield return null;
    }
    if (activationGeneration != pendingLocationActivationGeneration ||
        !string.Equals(currentLocationId, locationId, StringComparison.OrdinalIgnoreCase)) {
        pendingDeferredLocationActivationRoutine = null;
      yield break;
    }

    LogLocationStagePlanSummary("deferred_begin_after_reveal", locationId, null, deferredPlans, false);
    yield return ActivateLocationStageChildren(activationGeneration, locationId, deferredPlans);
    if (activationGeneration == pendingLocationActivationGeneration) {
      pendingDeferredLocationActivationRoutine = null;
    }
  }

  IEnumerator WaitForActivationCapacity(int activationGeneration, string locationId, string activationTarget) {
    var startedAt = Time.realtimeSinceStartup;
    var timeoutSeconds = ResolveActivationCapacityWaitTimeoutSeconds();
    var nextLogAt = Time.realtimeSinceStartup + ActivationWaitStateLogIntervalSeconds;
    var logActivationTrace = ShouldLogLocationActivationTrace();
    var waitedFrames = 0;
    while (activationGeneration == pendingLocationActivationGeneration) {
      if (HasActivationCapacity(
            out var queue,
            out var deferredPending,
            out var outstanding,
            out var maxOutstanding,
            out var maxInFlight,
            out var resolverIdle)) {
        if (logActivationTrace && waitedFrames > 0) {
          Debug.Log(
            "[LocationManager][ActivationCapacity] result=ready id='" + locationId +
            "' target='" + activationTarget +
            "' wait_ms=" + ((Time.realtimeSinceStartup - startedAt) * 1000f).ToString("0.0") +
            " frames=" + waitedFrames +
            " mode=" + ResolveActivationCapacityMode() +
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

      if (timeoutSeconds > 0f &&
          Time.realtimeSinceStartup - startedAt >= timeoutSeconds) {
        if (logActivationTrace) {
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

      if (logActivationTrace && Time.realtimeSinceStartup >= nextLogAt) {
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
      waitedFrames++;
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
    outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount, 0);
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

  static float ResolveActivationCapacityWaitTimeoutSeconds() {
    if (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive) {
      return ProtectedOverlayActivationCapacityWaitTimeoutSeconds;
    }
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      return OverlayActivationCapacityWaitTimeoutSeconds;
    }
    return RuntimeActivationCapacityWaitTimeoutSeconds;
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
}
