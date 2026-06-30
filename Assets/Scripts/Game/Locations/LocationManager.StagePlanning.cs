using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class LocationManager {
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

  static Transform FindDirectChild(Transform parent, string childName) {
    if (parent == null || string.IsNullOrWhiteSpace(childName)) return null;
    for (var i = 0; i < parent.childCount; i++) {
      var child = parent.GetChild(i);
      if (child == null) continue;
      if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase)) return child;
    }
    return null;
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
}
