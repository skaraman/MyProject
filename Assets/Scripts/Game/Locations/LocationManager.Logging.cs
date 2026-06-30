using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class LocationManager {
  bool ShouldLogVerboseLoadDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!(Application.isEditor || Debug.isDebugBuild)) return false;
    return SingleSceneManager.HasActiveGameplayLoadFlow;
  }

  bool ShouldLogLocationActivationTrace() {
    if (!ShouldLogVerboseLoadDebug()) return false;
    return SpriteStreamingRuntimeSettings.EnableDiagnostics;
  }

  void LogLocationLoadTiming(string stage, string locationId, float requestStartedAt, float stageStartedAt, string extraFields = "") {
    if (!ShouldLogVerboseLoadDebug()) return;
    var now = Time.realtimeSinceStartup;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    var flowKind = SingleSceneManager.ActiveGameplayLoadFlowKind;
    var flowTargetLocation = SingleSceneManager.ActiveGameplayLoadFlowTargetLocation;
    Debug.Log(
      "[LocationManager][LocationLoadTiming] flow_id=" + SingleSceneManager.ActiveGameplayLoadFlowId +
      " flow_kind=" + (string.IsNullOrWhiteSpace(flowKind) ? "-" : flowKind.Trim()) +
      " flow_target_location=" + (string.IsNullOrWhiteSpace(flowTargetLocation) ? "-" : flowTargetLocation.Trim()) +
      " stage=" + (string.IsNullOrWhiteSpace(stage) ? "-" : stage.Trim()) +
      " location=" + (string.IsNullOrWhiteSpace(locationId) ? "-" : locationId.Trim()) +
      " elapsed_ms=" + (requestStartedAt >= 0f ? ((now - requestStartedAt) * 1000f).ToString("0.0") : "-") +
      " stage_ms=" + (stageStartedAt >= 0f ? ((now - stageStartedAt) * 1000f).ToString("0.0") : "-") +
      " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
      " overlay_protected=" + (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive ? 1 : 0) +
      " queue_queued=" + queue.queuedCount +
      " queue_in_flight=" + queue.inFlightCount +
      " deferred=" + deferredPending +
      " pending_blocking=" + (pendingBlockingLocationActivationRoutine != null ? 1 : 0) +
      " pending_deferred=" + (pendingDeferredLocationActivationRoutine != null ? 1 : 0) +
      (string.IsNullOrWhiteSpace(extraFields) ? "" : " " + extraFields.Trim())
    );
  }

  void LogLocationStagePlanSummary(
    string stage,
    string locationId,
    List<ActivationStagePlan> blockingPlans,
    List<ActivationStagePlan> deferredPlans,
    bool promotedDeferred
  ) {
    if (!ShouldLogVerboseLoadDebug()) return;
    Debug.Log(
      "[LocationManager][StagePlan] flow_id=" + SingleSceneManager.ActiveGameplayLoadFlowId +
      " flow_kind=" + (string.IsNullOrWhiteSpace(SingleSceneManager.ActiveGameplayLoadFlowKind) ? "-" : SingleSceneManager.ActiveGameplayLoadFlowKind.Trim()) +
      " stage=" + (string.IsNullOrWhiteSpace(stage) ? "-" : stage.Trim()) +
      " location=" + (string.IsNullOrWhiteSpace(locationId) ? "-" : locationId.Trim()) +
      " blocking_stages=" + (blockingPlans != null ? blockingPlans.Count : 0) +
      " deferred_stages=" + (deferredPlans != null ? deferredPlans.Count : 0) +
      " promoted_deferred=" + (promotedDeferred ? 1 : 0) +
      " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
      " overlay_protected=" + (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive ? 1 : 0) +
      " blocking='" + BuildActivationStagePlanSummary(blockingPlans) +
      "' deferred='" + BuildActivationStagePlanSummary(deferredPlans) + "'"
    );
  }

  string BuildActivationStagePlanSummary(List<ActivationStagePlan> plans) {
    if (plans == null || plans.Count <= 0) return "-";
    var builder = stagePlanSummaryBuilder;
    builder.Clear();
    for (var i = 0; i < plans.Count; i++) {
      var plan = plans[i];
      if (plan == null || plan.root == null) continue;
      if (builder.Length > 0) builder.Append('|');
      builder
        .Append(plan.root.name)
        .Append("(nodes=").Append(plan.nodes != null ? plan.nodes.Count : 0)
        .Append(",renderers=").Append(CountStageComponents<SpriteRenderer>(plan))
        .Append(",sprite_targets=").Append(CountStageComponents<SpriteWithNormals>(plan))
        .Append(",camera_followers=").Append(CountStageComponents<CameraPositionFollower>(plan))
        .Append(",colliders2d=").Append(CountStageComponents<Collider2D>(plan))
        .Append(')');
    }
    if (builder.Length <= 0) return "-";
    return builder.ToString();
  }

  static int CountStageComponents<T>(ActivationStagePlan plan) where T : Component {
    if (plan == null || plan.root == null) return 0;
    var count = plan.root.GetComponent<T>() != null ? 1 : 0;
    if (plan.nodes == null) return count;
    for (var i = 0; i < plan.nodes.Count; i++) {
      var node = plan.nodes[i];
      if (node == null) continue;
      if (node.GetComponent<T>() != null) {
        count++;
      }
    }
    return count;
  }

  void LogLocationActivationSetActiveBegin(
    string locationId,
    float activationStartedAt,
    Transform stageRoot,
    Transform target,
    string activationTarget,
    int stageIndex,
    int nodeIndex,
    int stageNodesTotal,
    int totalNodesDone,
    bool isStageRoot
  ) {
    if (!ShouldLogLocationActivationTrace() || target == null) return;

    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    var directComponentCount = BuildDirectComponentSummary(target);
    var descendantCount = CountDescendants(target);
    var subtreeFields = "";
    if (isStageRoot) {
      subtreeFields =
        " subtree_sprite_with_normals=" + target.GetComponentsInChildren<SpriteWithNormals>(true).Length +
        " subtree_sprite_renderers=" + target.GetComponentsInChildren<SpriteRenderer>(true).Length +
        " subtree_camera_followers=" + target.GetComponentsInChildren<CameraPositionFollower>(true).Length +
        " subtree_colliders_2d=" + target.GetComponentsInChildren<Collider2D>(true).Length +
        " subtree_behaviours=" + target.GetComponentsInChildren<MonoBehaviour>(true).Length;
    }

    Debug.Log(
      "[LocationManager][ActivationTrace] phase=set_active_begin" +
      " location=" + (string.IsNullOrWhiteSpace(locationId) ? "-" : locationId.Trim()) +
      " elapsed_ms=" + ((Time.realtimeSinceStartup - activationStartedAt) * 1000f).ToString("0.0") +
      " stage='" + (stageRoot != null ? stageRoot.name : "-") + "'" +
      " target='" + (string.IsNullOrWhiteSpace(activationTarget) ? target.name : activationTarget.Trim()) + "'" +
      " object='" + target.name + "'" +
      " is_stage_root=" + (isStageRoot ? 1 : 0) +
      " stage_index=" + stageIndex +
      " node_index=" + nodeIndex +
      " stage_nodes_total=" + stageNodesTotal +
      " total_nodes_done=" + totalNodesDone +
      " active_self_before=" + (target.gameObject.activeSelf ? 1 : 0) +
      " active_hierarchy_before=" + (target.gameObject.activeInHierarchy ? 1 : 0) +
      " direct_children=" + target.childCount +
      " descendants=" + descendantCount +
      " direct_components=" + directComponentCount +
      " component_names='" + BuildActivationComponentNameList() + "'" +
      subtreeFields +
      " queued=" + queue.queuedCount +
      " in_flight=" + queue.inFlightCount +
      " deferred=" + deferredPending
    );
    activationComponentScratch.Clear();
  }

  int BuildDirectComponentSummary(Transform target) {
    activationComponentScratch.Clear();
    if (target == null) return 0;
    target.GetComponents(activationComponentScratch);
    return activationComponentScratch.Count;
  }

  string BuildActivationComponentNameList() {
    if (activationComponentScratch.Count <= 0) return "-";
    var result = "";
    var limit = Mathf.Min(activationComponentScratch.Count, ActivationTraceComponentNameLimit);
    for (var i = 0; i < limit; i++) {
      var component = activationComponentScratch[i];
      var componentName = component != null ? component.GetType().Name : "Missing";
      result += (i == 0 ? "" : "|") + componentName;
    }
    if (activationComponentScratch.Count > limit) {
      result += "|+" + (activationComponentScratch.Count - limit);
    }
    return result;
  }

  static int CountDescendants(Transform target) {
    if (target == null) return 0;
    var count = 0;
    for (var i = 0; i < target.childCount; i++) {
      var child = target.GetChild(i);
      if (child == null) continue;
      count++;
      count += CountDescendants(child);
    }
    return count;
  }

  void LogLocationActivationProgress(
    string phase,
    string locationId,
    float activationStartedAt,
    float stageStartedAt,
    int stageIndex,
    int stageCount,
    string stageName,
    string nodePath,
    int stageNodesDone,
    int stageNodesTotal,
    int totalNodesDone
  ) {
    if (!ShouldLogLocationActivationTrace()) return;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    Debug.Log(
      "[LocationManager][ActivationTrace] phase=" + (string.IsNullOrWhiteSpace(phase) ? "-" : phase.Trim()) +
      " location=" + (string.IsNullOrWhiteSpace(locationId) ? "-" : locationId.Trim()) +
      " elapsed_ms=" + ((Time.realtimeSinceStartup - activationStartedAt) * 1000f).ToString("0.0") +
      " stage_ms=" + ((Time.realtimeSinceStartup - stageStartedAt) * 1000f).ToString("0.0") +
      " stage_index=" + stageIndex +
      " stage_count=" + stageCount +
      " stage_name='" + (string.IsNullOrWhiteSpace(stageName) ? "-" : stageName.Trim()) + "'" +
      " node='" + (string.IsNullOrWhiteSpace(nodePath) ? "-" : nodePath.Trim()) + "'" +
      " stage_nodes_done=" + stageNodesDone +
      " stage_nodes_total=" + stageNodesTotal +
      " total_nodes_done=" + totalNodesDone +
      " queued=" + queue.queuedCount +
      " in_flight=" + queue.inFlightCount +
      " deferred=" + deferredPending
    );
  }

  void LogSlowLocationActivationStep(
    string locationId,
    float activationStartedAt,
    string stageName,
    string activationTarget,
    float stepStartedAt,
    int totalNodesDone
  ) {
    if (!ShouldLogLocationActivationTrace()) return;
    var stepSeconds = Time.realtimeSinceStartup - stepStartedAt;
    if (stepSeconds < SlowActivationStepLogSeconds) return;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    Debug.Log(
      "[LocationManager][ActivationTrace] phase=set_active_complete_slow" +
      " location=" + (string.IsNullOrWhiteSpace(locationId) ? "-" : locationId.Trim()) +
      " elapsed_ms=" + ((Time.realtimeSinceStartup - activationStartedAt) * 1000f).ToString("0.0") +
      " target='" + (string.IsNullOrWhiteSpace(activationTarget) ? "-" : activationTarget.Trim()) + "'" +
      " stage_name='" + (string.IsNullOrWhiteSpace(stageName) ? "-" : stageName.Trim()) + "'" +
      " set_active_ms=" + (stepSeconds * 1000f).ToString("0.0") +
      " total_nodes_done=" + totalNodesDone +
      " queued=" + queue.queuedCount +
      " in_flight=" + queue.inFlightCount +
      " deferred=" + deferredPending
    );
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
}
