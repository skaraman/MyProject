using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public sealed partial class StreamingWarmOrchestrator : MonoBehaviour, IStreamingWarmOrchestrator {
  sealed class ThreadedWarmPlanSnapshot {
    public readonly List<string> warmAddressBatch;
    public readonly HashSet<string> scheduledAddressSet;
    public readonly HashSet<string> scheduledReadyAddressSet;
    public readonly HashSet<string> scheduledCriticalReadyAddressSet;

    public ThreadedWarmPlanSnapshot(
      List<string> warmAddressBatch,
      HashSet<string> scheduledAddressSet,
      HashSet<string> scheduledReadyAddressSet,
      HashSet<string> scheduledCriticalReadyAddressSet
    ) {
      this.warmAddressBatch = warmAddressBatch ?? new List<string>();
      this.scheduledAddressSet = scheduledAddressSet ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      this.scheduledReadyAddressSet = scheduledReadyAddressSet ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      this.scheduledCriticalReadyAddressSet = scheduledCriticalReadyAddressSet ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
  }

  // helper used by TODO sorting and batching
  static int CompareByLifecycleLabel(string a, string b) {
    // simple priority order; addresses containing these substrings
    // will be requested earlier.  more sophisticated label lookup can
    // be added when the data model exposes it.
    int Rank(string addr) {
      if (addr.IndexOf("spawn", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
      if (addr.IndexOf("locomotion", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
      if (addr.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
      return 3;
    }
    return Rank(a).CompareTo(Rank(b));
  }

  IEnumerator FinalizeWarmPlanForEnqueue(WarmContext context, float hardTimeoutAt, bool debugLogs) {
    if (!ShouldUseThreadedWarmPlanFinalize()) {
      BuildWarmPlanBatchesSync();
      yield break;
    }

    var warmAddresses = new List<string>(warmAddressSet);
    var readyAddresses = new List<string>(readyAddressSet);
    var criticalReadyAddresses = new List<string>(criticalReadyAddressSet);
    var finalizeTask = Task.Run(() => BuildThreadedWarmPlanSnapshot(warmAddresses, readyAddresses, criticalReadyAddresses));
    var waitedFrames = 0;

    while (!finalizeTask.IsCompleted) {
      if (Time.realtimeSinceStartup >= hardTimeoutAt) {
        if (debugLogs) {
          Debug.LogWarning(
            "[StreamingWarmOrchestrator] Threaded warm-plan finalize timed out; falling back to synchronous finalize." +
            " context=" + context +
            " addresses=" + warmAddresses.Count +
            " waited_frames=" + waitedFrames
          );
        }
        BuildWarmPlanBatchesSync();
        yield break;
      }

      TextureResidencyCache.Pump();
      waitedFrames++;
      yield return null;
    }

    if (finalizeTask.IsFaulted || finalizeTask.IsCanceled || finalizeTask.Result == null) {
      if (debugLogs) {
        Debug.LogWarning(
          "[StreamingWarmOrchestrator] Threaded warm-plan finalize failed; falling back to synchronous finalize." +
          " context=" + context +
          " addresses=" + warmAddresses.Count +
          " waited_frames=" + waitedFrames +
          " faulted=" + (finalizeTask.IsFaulted ? 1 : 0) +
          " canceled=" + (finalizeTask.IsCanceled ? 1 : 0)
        );
      }
      BuildWarmPlanBatchesSync();
      yield break;
    }

    ApplyThreadedWarmPlanSnapshot(finalizeTask.Result);
    if (debugLogs) {
      Debug.Log(
        "[StreamingWarmOrchestrator] Threaded warm-plan finalize complete." +
        " context=" + context +
        " addresses=" + warmAddresses.Count +
        " batch=" + warmAddressBatch.Count +
        " ready=" + scheduledReadyAddressSet.Count +
        " critical=" + scheduledCriticalReadyAddressSet.Count +
        " waited_frames=" + waitedFrames
      );
    }
  }

  bool ShouldUseThreadedWarmPlanFinalize() {
    if (warmAddressSet.Count < ThreadedWarmPlanMinAddressCount) return false;
    return SystemInfo.processorCount >= ThreadedWarmPlanMinProcessorCount;
  }

  void BuildWarmPlanBatchesSync() {
    warmAddressBatch.Clear();
    warmAddressBatch.AddRange(warmAddressSet);
    if (warmAddressBatch.Count > 1) {
      warmAddressBatch.Sort(CompareByLifecycleLabel);
    }
    BuildScheduledAddressSets();
  }

  static ThreadedWarmPlanSnapshot BuildThreadedWarmPlanSnapshot(
    List<string> warmAddresses,
    List<string> readyAddresses,
    List<string> criticalReadyAddresses
  ) {
    var batch = warmAddresses ?? new List<string>();
    if (batch.Count > 1) {
      batch.Sort(CompareByLifecycleLabel);
    }

    var scheduledAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < batch.Count; i++) {
      var address = NormalizeToken(batch[i]);
      if (string.IsNullOrWhiteSpace(address)) continue;
      scheduledAddresses.Add(address);
    }

    var scheduledReadyAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (readyAddresses != null) {
      for (var i = 0; i < readyAddresses.Count; i++) {
        var address = NormalizeToken(readyAddresses[i]);
        if (string.IsNullOrWhiteSpace(address) || !scheduledAddresses.Contains(address)) continue;
        scheduledReadyAddresses.Add(address);
      }
    }

    var scheduledCriticalAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (criticalReadyAddresses != null) {
      for (var i = 0; i < criticalReadyAddresses.Count; i++) {
        var address = NormalizeToken(criticalReadyAddresses[i]);
        if (string.IsNullOrWhiteSpace(address) || !scheduledAddresses.Contains(address)) continue;
        scheduledCriticalAddresses.Add(address);
      }
    }
    return new ThreadedWarmPlanSnapshot(batch, scheduledAddresses, scheduledReadyAddresses, scheduledCriticalAddresses);
  }

  void ApplyThreadedWarmPlanSnapshot(ThreadedWarmPlanSnapshot snapshot) {
    warmAddressBatch.Clear();
    scheduledAddressSet.Clear();
    scheduledReadyAddressSet.Clear();
    scheduledCriticalReadyAddressSet.Clear();
    if (snapshot == null) return;

    warmAddressBatch.AddRange(snapshot.warmAddressBatch);
    foreach (var address in snapshot.scheduledAddressSet) {
      scheduledAddressSet.Add(address);
    }
    foreach (var address in snapshot.scheduledReadyAddressSet) {
      scheduledReadyAddressSet.Add(address);
    }
    foreach (var address in snapshot.scheduledCriticalReadyAddressSet) {
      scheduledCriticalReadyAddressSet.Add(address);
    }
  }

  // creates a single enumerator that processes a list of addresses in chunks.
  IEnumerator BatchedEnqueue(List<string> addresses,
                             TextureResidencyCache.LoadPriority priority,
                             bool allowAtlasExpansion,
                             int enqueueBudgetPerFrame) {
    const int chunkSize = 100; // within 50-200 guidance
    for (int start = 0; start < addresses.Count; start += chunkSize) {
      int count = Math.Min(chunkSize, addresses.Count - start);
      sliceScratch.Clear();
      for (var i = 0; i < count; i++) {
        sliceScratch.Add(addresses[start + i]);
      }
      var inner = TextureResidencyCache.RequestLoadBatchThrottled(
          sliceScratch, priority, allowAtlasExpansion: allowAtlasExpansion,
          enqueueBudgetPerFrame: enqueueBudgetPerFrame,
          warmGateManaged: true);
      while (inner.MoveNext()) {
        yield return inner.Current;
      }
      // allow other work between chunks
      yield return null;
    }
    sliceScratch.Clear();
  }

  void ScheduleFirstAnimationRescue(WarmRequest request) {
    // best effort attempt to bump start-frame addresses to immediate priority
    // so the player and any nearby enemies don't have blank frames after a
    // soft timeout. This is largely a placeholder until proper instrumentation
    // and heuristics are in place.

    rescueAddressBuffer.Clear();
    rescueSeenAddressBuffer.Clear();

    if (request.playerController != null && request.playerController.Controller != null) {
      request.playerController.Controller.CollectAnimationStartAddresses(
        rescueAddressBuffer, rescueSeenAddressBuffer, framesPerAnimation: 1, maxAnimations: 4, maxAddresses: 32
      );
    }

    if (request.enemyControllers != null && request.enemyControllers.Length > 0) {
      rescueEnemyControllerBuffer.Clear();
      rescueEnemyControllerBuffer.AddRange(request.enemyControllers);
      if (request.playerController != null) {
        var playerPos = request.playerController.transform.position;
        rescueEnemyControllerBuffer.Sort((a, b) => {
          if (a == null || b == null) return 0;
          var distA = (a.transform.position - playerPos).sqrMagnitude;
          var distB = (b.transform.position - playerPos).sqrMagnitude;
          return distA.CompareTo(distB);
        });
      }

      var enemyCount = rescueEnemyControllerBuffer.Count;
      for (var i = 0; i < enemyCount && i < 5; i++) {
        var enemy = rescueEnemyControllerBuffer[i];
        if (enemy == null || enemy.Controller == null) continue;
        enemy.Controller.CollectAnimationStartAddresses(
          rescueAddressBuffer, rescueSeenAddressBuffer, framesPerAnimation: 1, maxAnimations: 1, maxAddresses: 16
        );
      }
    }

    if (rescueAddressBuffer.Count > 0) {
      if (rescueDispatchRoutine != null) {
        StopCoroutine(rescueDispatchRoutine);
        rescueDispatchRoutine = null;
      }
      rescueDispatchBuffer.Clear();
      rescueDispatchBuffer.AddRange(rescueAddressBuffer);
      rescueDispatchRoutine = StartCoroutine(DispatchFirstAnimationRescueLoads());
    }

    rescueAddressBuffer.Clear();
    rescueSeenAddressBuffer.Clear();
    rescueEnemyControllerBuffer.Clear();
  }

  IEnumerator DispatchFirstAnimationRescueLoads() {
    yield return TextureResidencyCache.RequestLoadBatchThrottled(
      rescueDispatchBuffer,
      TextureResidencyCache.LoadPriority.Immediate,
      // Rescue loads should hydrate full atlas families, not single slices, to prevent immediate re-stalls.
      allowAtlasExpansion: true,
      enqueueBudgetPerFrame: 32,
      warmGateManaged: true
    );
    rescueDispatchBuffer.Clear();
    rescueDispatchRoutine = null;
  }

  void BuildScheduledAddressSets() {
    scheduledAddressSet.Clear();
    scheduledReadyAddressSet.Clear();
    scheduledCriticalReadyAddressSet.Clear();

    for (var i = 0; i < warmAddressBatch.Count; i++) {
      var address = NormalizeToken(warmAddressBatch[i]);
      if (string.IsNullOrWhiteSpace(address)) continue;
      scheduledAddressSet.Add(address);
    }

    foreach (var address in readyAddressSet) {
      if (!scheduledAddressSet.Contains(address)) continue;
      scheduledReadyAddressSet.Add(address);
    }

    // Critical scope must stay limited to first-frame addresses so soft timeout can
    // release once gameplay-safe visuals are ready while the rest continues warming.
    foreach (var address in criticalReadyAddressSet) {
      if (!scheduledAddressSet.Contains(address)) continue;
      scheduledCriticalReadyAddressSet.Add(address);
    }
  }

  IEnumerator ResolveLibraryAtlasDependenciesRoutine(float hardTimeoutAt, bool debugLogs) {
    sortedLabelBuffer.Clear();
    sortedLabelBuffer.AddRange(warmLibrarySet);
    var libraries = sortedLabelBuffer;

    for (var i = 0; i < libraries.Count; i++) {
      if (Time.realtimeSinceStartup >= hardTimeoutAt) yield break;
      var libKey = libraries[i];
      // Library keys are used only to discover dependent sprite addresses.
      // Actual texture residency still goes through address-based cache loads.
      var locHandle = Addressables.LoadResourceLocationsAsync(libKey);
      while (!locHandle.IsDone) {
        if (Time.realtimeSinceStartup >= hardTimeoutAt) break;
        TextureResidencyCache.Pump();
        yield return null;
      }

      if (locHandle.Status == AsyncOperationStatus.Succeeded && locHandle.Result != null) {
        var locations = locHandle.Result;
        foreach (var loc in locations) {
          if (loc.Dependencies == null) continue;
          foreach (var dep in loc.Dependencies) {
            if (HasReachedWarmAddressCap()) break;
            AddWarmAddress(dep.PrimaryKey, markHighPriority: false);
          }
        }
      }
      Addressables.Release(locHandle);
    }
  }

  IEnumerator ResolveLabelAddressesRoutine(float hardTimeoutAt, bool debugLogs) {
    // use a shared buffer to avoid allocating a new list each warm gate
    sortedLabelBuffer.Clear();
    sortedLabelBuffer.AddRange(warmLabelSet);
    var labels = sortedLabelBuffer;
    if (labels.Count > 1) {
      labels.Sort(CompareByLifecycleLabel);
    }
    for (var i = 0; i < labels.Count; i++) {
      if (Time.realtimeSinceStartup >= hardTimeoutAt) yield break;
      var label = labels[i];
      var isCritical = criticalReadyLabelSet.Contains(label);
      // Build/runtime may include visible sprite subassets, but label warmup still
      // resolves atlas asset locations directly to avoid exploding warm plans into
      // every slice representation.
      var locHandle = Addressables.LoadResourceLocationsAsync(label);
      // TODO(smooth-first-play): If a critical label resolves a very large location list, split
      // the resulting address set into deterministic chunks and enqueue high-value chunks first.
      while (!locHandle.IsDone) {
        if (Time.realtimeSinceStartup >= hardTimeoutAt) break;
        TextureResidencyCache.Pump();
        yield return null;
      }
      if (locHandle.Status == AsyncOperationStatus.Succeeded && locHandle.Result != null) {
        locationBuffer.Clear();
        locationBuffer.AddRange(locHandle.Result);
        if (locationBuffer.Count > 1) {
          locationBuffer.Sort((a, b) => CompareByLifecycleLabel(a.PrimaryKey, b.PrimaryKey));
        }
        for (var j = 0; j < locationBuffer.Count; j++) {
          if (HasReachedWarmAddressCap()) break;
          AddReadyAddress(locationBuffer[j].PrimaryKey, markCritical: isCritical, markHighPriority: isCritical);
        }
        if (debugLogs) {
          Debug.Log(
            "[StreamingWarmOrchestrator] Label prewarm resolved label='" + label + "'" + " addresses=" +
            locationBuffer.Count + " critical=" + (isCritical ? 1 : 0)
          );
        }
      } else if (debugLogs) {
        Debug.LogWarning(
          "[StreamingWarmOrchestrator] Label prewarm failed to resolve label='" + label + "'" +
          " status=" + locHandle.Status
        );
      }
      Addressables.Release(locHandle);
    }
  }

  static bool StepEnqueue(ref IEnumerator routine) {
    if (routine == null) return false;
    var hasNext = routine.MoveNext();
    if (hasNext) return true;

    if (routine is IDisposable disposable) {
      disposable.Dispose();
    }
    routine = null;
    return false;
  }

  static int ResolveEnqueueBudgetPerFrame(int addressCount, bool isHighPriority) {
    if (addressCount <= 0) return 0;
    var baseStarts = Mathf.Max(SpriteStreamingRuntimeSettings.MaxAddressableStartsPerFrame, 1);
    var multiplier = isHighPriority ? 3 : 6;
    var target = baseStarts * multiplier;
    // Keep batches in the 50–200 window per AGENTS guidance.
    var minBatch = 50;
    var maxBatch = 200;
    target = Mathf.Clamp(target, minBatch, maxBatch);
    return Mathf.Clamp(target, 1, Mathf.Max(addressCount, 1));
  }
}
