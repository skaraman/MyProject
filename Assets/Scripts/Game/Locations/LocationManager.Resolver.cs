using System;
using System.Collections.Generic;
using UnityEngine;

public partial class LocationManager {
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
}
