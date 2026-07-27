using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

public static class RuntimeUpdateHub {
  readonly struct UpdateEntry {
    public readonly int Order;
    public readonly Action Callback;
    public readonly ProfilerMarker Marker;

    public UpdateEntry(int order, string markerName, Action callback) {
      Order = order;
      Callback = callback;
      Marker = new ProfilerMarker(markerName);
    }
  }

  static readonly List<UpdateEntry> updateEntries = new(8);
  static readonly List<UpdateEntry> pendingEntries = new(4);
  static readonly HashSet<Action> registeredCallbacks = new();
  static readonly HashSet<Action> pendingRemovals = new();
  static RuntimeUpdateHubRunner runner;
  static bool isTicking;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetStatics() {
    updateEntries.Clear();
    pendingEntries.Clear();
    registeredCallbacks.Clear();
    pendingRemovals.Clear();
    runner = null;
    isTicking = false;
  }

  public static void Register(int order, string profilerMarkerName, Action callback) {
    if (!Application.isPlaying || callback == null) return;
    if (!registeredCallbacks.Add(callback)) return;

    var entry = new UpdateEntry(
      order,
      string.IsNullOrWhiteSpace(profilerMarkerName)
        ? "RuntimeUpdateHub.Callback"
        : profilerMarkerName,
      callback
    );
    if (isTicking) {
      pendingEntries.Add(entry);
    }
    else {
      InsertEntry(entry);
    }
    EnsureRunner();
  }

  public static void Unregister(Action callback) {
    if (callback == null || !registeredCallbacks.Remove(callback)) return;
    if (isTicking) {
      pendingRemovals.Add(callback);
      for (var i = pendingEntries.Count - 1; i >= 0; i--) {
        if (pendingEntries[i].Callback == callback) {
          pendingEntries.RemoveAt(i);
        }
      }
      return;
    }

    RemoveEntry(callback);
  }

  static void InsertEntry(UpdateEntry entry) {
    var insertIndex = updateEntries.Count;
    while (insertIndex > 0 && updateEntries[insertIndex - 1].Order > entry.Order) {
      insertIndex--;
    }
    updateEntries.Insert(insertIndex, entry);
  }

  static void RemoveEntry(Action callback) {
    for (var i = updateEntries.Count - 1; i >= 0; i--) {
      if (updateEntries[i].Callback != callback) continue;
      updateEntries.RemoveAt(i);
      return;
    }
  }

  static void EnsureRunner() {
    if (runner != null || !Application.isPlaying) return;

    var go = new GameObject("Runtime Update Hub") {
      hideFlags = HideFlags.HideAndDontSave
    };
    UnityEngine.Object.DontDestroyOnLoad(go);
    runner = go.AddComponent<RuntimeUpdateHubRunner>();
  }

  internal static void Tick() {
    isTicking = true;
    try {
      var count = updateEntries.Count;
      for (var i = 0; i < count; i++) {
        var entry = updateEntries[i];
        if (!registeredCallbacks.Contains(entry.Callback)) continue;
        using (entry.Marker.Auto()) {
          try {
            entry.Callback();
          }
          catch (Exception exception) {
            Debug.LogException(exception);
          }
        }
      }
    }
    finally {
      isTicking = false;
      FlushPendingMutations();
    }
  }

  static void FlushPendingMutations() {
    foreach (var callback in pendingRemovals) {
      RemoveEntry(callback);
    }
    pendingRemovals.Clear();

    for (var i = 0; i < pendingEntries.Count; i++) {
      var entry = pendingEntries[i];
      if (registeredCallbacks.Contains(entry.Callback)) {
        InsertEntry(entry);
      }
    }
    pendingEntries.Clear();
  }
}

sealed class RuntimeUpdateHubRunner : MonoBehaviour {
  void Update() {
    RuntimeUpdateHub.Tick();
  }
}
