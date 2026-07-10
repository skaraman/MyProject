using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class StreamingWarmOrchestrator : MonoBehaviour, IStreamingWarmOrchestrator {
  static StreamingWarmOrchestrator instance;
  static readonly HashSet<string> completedWarmTokens = new(StringComparer.OrdinalIgnoreCase);
  static WarmProgressSnapshot activeProgress;
  static bool hasActiveProgress;

  readonly HashSet<string> warmLibrarySet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> criticalLibrarySet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> warmAddressSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> warmLabelSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> highPriorityAddressSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> highPriorityLabelSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> readyAddressSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> readyLabelSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> criticalReadyAddressSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> criticalReadyLabelSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> playerWarmAtlasSeedAddresses = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> scheduledAddressSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> scheduledReadyAddressSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> scheduledCriticalReadyAddressSet = new(StringComparer.OrdinalIgnoreCase);
  readonly List<string> warmAddressBatch = new(2048);
  readonly Dictionary<GameObject, SpriteWithNormals[]> archetypeTargetCache = new();

  Coroutine activeRoutine;
  Action<WarmResult> activeCallback;
  int activeMaxRequestedAddresses;
  int activeRuntimeTrackedWarmId;
  int activeCriticalReadyAddressCap;
  int activeHighPriorityAddressCap;
  int activeFrameAddressProbeBudget;
  int warmPlanFrameAddressProbeCount;
  int warmPlanDroppedAddresses;
  int warmPlanDroppedCriticalReadyAddresses;
  int warmPlanDroppedHighPriorityAddresses;

  // scratch buffers used to reduce GC pressure in hot paths.
  static readonly List<string> archetypeKeyBuffer = new();
  static readonly HashSet<string> archetypeKeySeenBuffer = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<string> sliceScratch = new();
  static readonly List<string> sortedLabelBuffer = new();
  static readonly List<string> libraryDependencyAddressBuffer = new();
  static readonly List<string> rescueAddressBuffer = new();
  static readonly HashSet<string> rescueSeenAddressBuffer = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<EnemyController> rescueEnemyControllerBuffer = new();
  static readonly List<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation> locationBuffer = new();
  static readonly HashSet<string> uniqueScheduledImagesScratch = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> uniqueScheduledReadyImagesScratch = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> uniqueScheduledCriticalReadyImagesScratch = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> uniqueWarmImagesScratch = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> uniqueCriticalImagesScratch = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> normalizedTokenSetScratch = new(StringComparer.OrdinalIgnoreCase);
  readonly List<string> rescueDispatchBuffer = new(64);
  Coroutine rescueDispatchRoutine;

  const int MinWarmPlanFrameAddressProbeBudget = 32768;
  const int MaxWarmPlanFrameAddressProbeBudget = 1000000;
  const float WarmPlanSliceBudgetSeconds = 0.004f;
  const int WarmPlanSliceWorkItemBudget = 24;
  const int MaxAnimationSamplesPerClip = 8;
  const int DesktopWarmOutstandingTarget = 1500;
  const int MobileWarmOutstandingTarget = 900;
  const int ThreadedWarmPlanMinAddressCount = 512;
  const int ThreadedWarmPlanMinProcessorCount = 4;

  public static StreamingWarmOrchestrator Instance {
    get {
      if (instance != null) return instance;
      if (!Application.isPlaying) return null;
      var go = new GameObject("StreamingWarmOrchestrator") { hideFlags = HideFlags.HideAndDontSave };
      DontDestroyOnLoad(go);
      instance = go.AddComponent<StreamingWarmOrchestrator>();
      return instance;
    }
  }

  public bool IsRunning => activeRoutine != null;
  public static bool IsWarmGateRunning => instance != null && instance.IsRunning;

  public static bool TryGetActiveProgress(out WarmProgressSnapshot snapshot) {
    snapshot = activeProgress;
    return hasActiveProgress;
  }

  public static bool HasCompletedToken(string token) {
    var normalized = NormalizeToken(token);
    if (string.IsNullOrWhiteSpace(normalized)) return false;
    return completedWarmTokens.Contains(normalized);
  }

  public static string BuildEnemyArchetypeToken(string locationId, Dictionary<string, GameObject> enemyArchetypePrefabsByType) {
    var normalizedLocation = NormalizeToken(locationId);
    archetypeKeyBuffer.Clear();
    archetypeKeySeenBuffer.Clear();
    if (enemyArchetypePrefabsByType != null) {
      foreach (var pair in enemyArchetypePrefabsByType) {
        var key = NormalizeToken(pair.Key);
        if (string.IsNullOrWhiteSpace(key)) continue;
        if (!archetypeKeySeenBuffer.Add(key)) continue;
        archetypeKeyBuffer.Add(key);
      }
    }
    archetypeKeyBuffer.Sort(StringComparer.OrdinalIgnoreCase);
    var keyCsv = archetypeKeyBuffer.Count > 0 ? string.Join(",", archetypeKeyBuffer) : "none";
    archetypeKeyBuffer.Clear();
    archetypeKeySeenBuffer.Clear();
    return "location:" + normalizedLocation + "|enemies:" + keyCsv;
  }

  int StartRuntimeAssetWarmup(WarmRequest request, WarmContext context, bool debugLogs) {
    var criticalAssetLabels = BuildMergedSourceList(
      request.extraCriticalAssetLabels,
      SpriteStreamingRuntimeSettings.CriticalRuntimeAssetLabels
    );
    var warmAssetLabels = BuildMergedSourceList(
      request.extraWarmAssetLabels,
      SpriteStreamingRuntimeSettings.WarmRuntimeAssetLabels
    );
    var hasRuntimeSources =
      HasAnyNormalizedSource(request.extraCriticalAssetAddresses) ||
      HasAnyNormalizedSource(request.extraWarmAssetAddresses) ||
      HasAnyNormalizedSource(criticalAssetLabels) ||
      HasAnyNormalizedSource(warmAssetLabels);
    if (!hasRuntimeSources) return 0;

    var trackedWarmId = RuntimeAssetCache.BeginTrackedWarmup(
      request.extraCriticalAssetAddresses,
      criticalAssetLabels,
      request.extraWarmAssetAddresses,
      warmAssetLabels,
      RuntimeAssetResidencyScope.Session,
      "warm_gate:" + context
    );

    if (debugLogs) {
      Debug.Log(
        "[StreamingWarmOrchestrator] Runtime asset warm started" +
        " context=" + context +
        " tracked_id=" + trackedWarmId +
        " critical_addresses=" + CountNormalizedSources(request.extraCriticalAssetAddresses) +
        " critical_labels=" + CountNormalizedSources(criticalAssetLabels) +
        " warm_addresses=" + CountNormalizedSources(request.extraWarmAssetAddresses) +
        " warm_labels=" + CountNormalizedSources(warmAssetLabels)
      );
    }

    return trackedWarmId;
  }

  static RuntimeAssetWarmSnapshot GetRuntimeWarmSnapshot(int trackedWarmId) {
    if (trackedWarmId <= 0) {
      return new RuntimeAssetWarmSnapshot(
        prepared: true,
        readyCount: 0,
        totalCount: 0,
        criticalReadyCount: 0,
        criticalTotalCount: 0,
        criticalReady: true,
        queue: RuntimeAssetCache.GetQueueSnapshot()
      );
    }

    if (RuntimeAssetCache.TryGetTrackedWarmSnapshot(trackedWarmId, out var snapshot)) {
      return snapshot;
    }

    return new RuntimeAssetWarmSnapshot(
      prepared: false,
      readyCount: 0,
      totalCount: 0,
      criticalReadyCount: 0,
      criticalTotalCount: 0,
      criticalReady: false,
      queue: RuntimeAssetCache.GetQueueSnapshot()
    );
  }

  static List<string> BuildMergedSourceList(IEnumerable<string> first, IEnumerable<string> second) {
    List<string> merged = null;
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    AppendMergedSources(ref merged, seen, first);
    AppendMergedSources(ref merged, seen, second);
    return merged;
  }

  static void AppendMergedSources(ref List<string> output, HashSet<string> seen, IEnumerable<string> values) {
    if (values == null) return;
    foreach (var value in values) {
      var normalized = NormalizeToken(value);
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      if (seen != null && !seen.Add(normalized)) continue;
      output ??= new List<string>();
      output.Add(normalized);
    }
  }

  static bool HasAnyNormalizedSource(IEnumerable<string> values) {
    return CountNormalizedSources(values) > 0;
  }

  static int CountNormalizedSources(IEnumerable<string> values) {
    if (values == null) return 0;
    var count = 0;
    foreach (var value in values) {
      if (!string.IsNullOrWhiteSpace(NormalizeToken(value))) count++;
    }
    return count;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    instance = null;
    completedWarmTokens.Clear();
    hasActiveProgress = false;
    activeProgress = default;
    uniqueScheduledImagesScratch.Clear();
    uniqueScheduledReadyImagesScratch.Clear();
    uniqueScheduledCriticalReadyImagesScratch.Clear();
    uniqueWarmImagesScratch.Clear();
    uniqueCriticalImagesScratch.Clear();
  }

  void Awake() {
    if (instance != null && !ReferenceEquals(instance, this)) {
      Destroy(gameObject);
      return;
    }
    instance = this;
  }

  void OnDestroy() {
    if (ReferenceEquals(instance, this)) {
      instance = null;
    }
    Cancel();
  }

  public void Run(WarmRequest request, Action<WarmResult> onComplete = null) {
    if (!Application.isPlaying) {
      onComplete?.Invoke(new WarmResult(
        context: request.context,
        completedWithinTimeout: true,
        reachedReadyThreshold: true,
        playerCriticalReady: true,
        readyRatio: 1f,
        readyCount: 0,
        totalCount: 0,
        criticalReadyCount: 0,
        criticalTotalCount: 0,
        requestedAddressCount: 0,
        elapsedMs: 0f,
        hardTimeoutBypassUsed: false,
        failureReason: ""
      ));
      return;
    }

    var sanitized = SanitizeRequest(request);
    var token = NormalizeToken(sanitized.idempotencyToken);
    if (sanitized.skipIfTokenAlreadyWarm && !string.IsNullOrWhiteSpace(token) && completedWarmTokens.Contains(token)) {
      onComplete?.Invoke(new WarmResult(
        context: sanitized.context,
        completedWithinTimeout: true,
        reachedReadyThreshold: true,
        playerCriticalReady: true,
        readyRatio: 1f,
        readyCount: 0,
        totalCount: 0,
        criticalReadyCount: 0,
        criticalTotalCount: 0,
        requestedAddressCount: 0,
        elapsedMs: 0f,
        hardTimeoutBypassUsed: false,
        failureReason: ""
      ));
      return;
    }

    Cancel();
    activeCallback = onComplete;
    activeRoutine = StartCoroutine(RunRoutine(sanitized));
  }

  public void Cancel() {
    if (activeRoutine != null) {
      StopCoroutine(activeRoutine);
      activeRoutine = null;
    }
    if (rescueDispatchRoutine != null) {
      StopCoroutine(rescueDispatchRoutine);
      rescueDispatchRoutine = null;
    }
    if (activeRuntimeTrackedWarmId > 0) {
      RuntimeAssetCache.ReleaseTrackedWarmup(activeRuntimeTrackedWarmId);
      activeRuntimeTrackedWarmId = 0;
    }
    activeCallback = null;
    hasActiveProgress = false;
    activeProgress = default;
    ClearScratch();
  }

  IEnumerator RunRoutine(WarmRequest request) {
    var context = request.context;
    var startedAt = Time.realtimeSinceStartup;
    var softTimeoutAt = startedAt + Mathf.Max(request.timeoutSeconds, 0.25f);
    var hardTimeoutAt = startedAt + Mathf.Max(request.hardTimeoutSeconds, request.timeoutSeconds);
    ClearScratch();
    activeMaxRequestedAddresses = ResolveActiveMaxRequestedAddresses(context, request.maxRequestedAddresses);
    activeHighPriorityAddressCap = ResolveHighPriorityAddressCap(context);
    activeCriticalReadyAddressCap = ResolveCriticalReadyAddressCap(context, activeHighPriorityAddressCap);
    activeFrameAddressProbeBudget = Mathf.Clamp(activeMaxRequestedAddresses * 4, MinWarmPlanFrameAddressProbeBudget, MaxWarmPlanFrameAddressProbeBudget);
    var debugLogs = ShouldLogLoadingDebug();
    var logIntervalSeconds = Mathf.Max(SpriteStreamingRuntimeSettings.LoadingProgressLogIntervalMs, 100) / 1000f;
    activeRuntimeTrackedWarmId = StartRuntimeAssetWarmup(request, context, debugLogs);

    warmPlanFrameAddressProbeCount = 0;
    warmPlanDroppedAddresses = 0;
    warmPlanDroppedCriticalReadyAddresses = 0;
    warmPlanDroppedHighPriorityAddresses = 0;
    // Build a minimal first-pass scope first; ideal startup keeps this pass
    // focused on first-contact visuals so soft timeout can be met consistently.
    yield return BuildWarmPlanRoutine(
      request,
      includeResolvedAddressSweeps: false,
      includeStaticSeedWork: true,
      deadlineAt: hardTimeoutAt,
      debugLogs: debugLogs
    );

    if (warmLibrarySet.Count > 0) {
      yield return ResolveLibraryAtlasDependenciesRoutine(hardTimeoutAt, debugLogs);
    }

    if (warmLabelSet.Count > 0) {
      yield return ResolveLabelAddressesRoutine(hardTimeoutAt, debugLogs);
    }

    var resolverWaitFrames = 0;
    var resolverSweepFrames = 0;
    var extendedSweepDeadline = hardTimeoutAt + Mathf.Max(request.timeoutSeconds, 2.0f);
    while (true) {
      var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
      yield return BuildWarmPlanRoutine(
        request,
        includeResolvedAddressSweeps: true,
        includeStaticSeedWork: false,
        deadlineAt: hardTimeoutAt,
        debugLogs: debugLogs
      );
      resolverSweepFrames++;
      var hasResolvedAddresses = warmAddressSet.Count > 0;
      var now = Time.realtimeSinceStartup;
      var reachedHardTimeout = now >= hardTimeoutAt;
      var reachedExtendedSweepTimeout = now >= extendedSweepDeadline;

      if (resolverIdle) break;
      if (reachedHardTimeout) break;
      resolverWaitFrames++;
      TextureResidencyCache.Pump();
      yield return null;
    }

    var resolverIdleForAddressSweep = SpriteRuntimeResolver.IsWarmupIdle();
    if (!resolverIdleForAddressSweep && debugLogs) {
      Debug.LogWarning(
        "[StreamingWarmOrchestrator] Resolver warmup did not become idle before hard timeout; " +
        "continuing with partial frame-address sweep. context=" + context +
        " sweeps=" + resolverSweepFrames +
        " addresses=" + warmAddressSet.Count
      );
    }
    if (debugLogs && warmAddressSet.Count <= 0) {
      Debug.LogWarning(
        "[StreamingWarmOrchestrator] Warm plan has no resolved addresses after sweep window." +
        " context=" + context +
        " sweeps=" + resolverSweepFrames +
        " resolver_idle=" + (resolverIdleForAddressSweep ? 1 : 0)
      );
    }

    if (debugLogs && resolverWaitFrames > 0) {
      Debug.Log(
        "[StreamingWarmOrchestrator] Resolver warmup wait complete. context=" + context +
        " frames=" + resolverWaitFrames +
        " idle=" + (resolverIdleForAddressSweep ? 1 : 0)
      );
    }

    yield return ExpandPlayerAtlasSeedsRoutine(context, hardTimeoutAt, debugLogs);

    GetUniqueImageSets(warmAddressSet, uniqueWarmImagesScratch);
    GetUniqueImageSets(criticalReadyAddressSet, uniqueCriticalImagesScratch);

    if (debugLogs) {
      var deferredSnapshot = TextureResidencyCache.GetDeferredSnapshot();
      Debug.Log(
        "[StreamingWarmOrchestrator] Start context=" + context +
        " timeout_s=" + request.timeoutSeconds.ToString("0.00") +
        " hard_timeout_s=" + request.hardTimeoutSeconds.ToString("0.00") +
        " required_ratio=" + request.requiredReadyRatio.ToString("0.00") +
        " player_manifest=Data/Animations.cs:Animations.Esperanza" +
        " priority_mode=single_warmup_queue" +
        " address_cap=" + activeMaxRequestedAddresses +
        " high_priority_cap=" + activeHighPriorityAddressCap +
        " critical_cap=" + activeCriticalReadyAddressCap +
        " libraries=" + warmLibrarySet.Count +
        " addresses=" + uniqueWarmImagesScratch.Count +
        " critical=" + uniqueCriticalImagesScratch.Count +
        " dropped=" + warmPlanDroppedAddresses +
        " dropped_high_priority=" + warmPlanDroppedHighPriorityAddresses +
        " dropped_critical=" + warmPlanDroppedCriticalReadyAddresses +
        " deferred_pending=" + deferredSnapshot.pendingCount +
        " deferred_flushed_frame=" + deferredSnapshot.flushedThisFrame +
        " deferred_total=" + deferredSnapshot.totalDeferredCount +
        " deferred_requests_total=" + deferredSnapshot.totalDeferralRequestCount +
        " deferred_promoted=" + deferredSnapshot.totalPromotedCount
      );
    }

    // Session total is finalized after warm batching/trimming so progress reflects
    // the addresses actually scheduled this gate.

    yield return FinalizeWarmPlanForEnqueue(context, hardTimeoutAt, debugLogs);

    // Initialize the loading session with the total known count of addresses to warm.
    // This allows the loading screen to show a more accurate progress bar.
    GetUniqueImageSets(scheduledAddressSet, uniqueScheduledImagesScratch);
    TextureResidencyCache.BeginSession(uniqueScheduledImagesScratch.Count);

    var warmEnqueueBudget = ResolveEnqueueBudgetPerFrame(warmAddressBatch.Count, isHighPriority: false);
    IEnumerator warmEnqueue = null;
    if (warmAddressBatch.Count > 0) {
      // enqueue the warm set in sub‑batches instead of all at once.  each chunk will
      // be submitted sequentially so the first frames have a chance to finish early.
      warmEnqueue = BatchedEnqueue(warmAddressBatch,
                                   TextureResidencyCache.LoadPriority.Warmup,
                                   allowAtlasExpansion: true,
                                   enqueueBudgetPerFrame: warmEnqueueBudget);
    }

    StepEnqueue(ref warmEnqueue);
    TextureResidencyCache.Pump();

    var requiredReadyRatio = Mathf.Clamp01(request.requiredReadyRatio);
    var reachedThreshold = false;
    var softTimedOut = false;
    var hardTimedOut = false;
    var readyCount = 0;
    var totalCount = 0;
    var criticalReadyCount = 0;
    var criticalTotalCount = 0;
    var ratio = 0f;
    var criticalReady = activeRuntimeTrackedWarmId <= 0;
    var nextProgressLogAt = startedAt + logIntervalSeconds;
    var progressSampleInterval = totalCount > 4096 ? 3 : (totalCount > 2048 ? 2 : 1);
    var nextProgressSampleFrame = Time.frameCount;

    while (true) {
      StepEnqueue(ref warmEnqueue);
      TextureResidencyCache.Pump();
      var frame = Time.frameCount;
      var shouldSampleProgress = frame >= nextProgressSampleFrame;
      if (shouldSampleProgress) {
        var runtimeSnapshot = GetRuntimeWarmSnapshot(activeRuntimeTrackedWarmId);

        GetUniqueImageSets(scheduledReadyAddressSet, uniqueScheduledReadyImagesScratch);
        GetUniqueImageSets(scheduledCriticalReadyAddressSet, uniqueScheduledCriticalReadyImagesScratch);

        var spriteReadyCount = CountReadyImages(uniqueScheduledReadyImagesScratch, pumpEntries: false);
        var spriteCriticalReadyCount = CountReadyImages(uniqueScheduledCriticalReadyImagesScratch, pumpEntries: false);
        var spriteTotalCount = uniqueScheduledReadyImagesScratch.Count;
        var spriteCriticalTotalCount = uniqueScheduledCriticalReadyImagesScratch.Count;

        readyCount = spriteReadyCount + runtimeSnapshot.readyCount;
        totalCount = spriteTotalCount + runtimeSnapshot.totalCount;
        criticalReadyCount = spriteCriticalReadyCount + runtimeSnapshot.criticalReadyCount;
        criticalTotalCount = spriteCriticalTotalCount + runtimeSnapshot.criticalTotalCount;
        ratio = totalCount > 0 ? ((float)readyCount / totalCount) : 1f;
        var spriteCriticalReady = spriteCriticalTotalCount <= 0 || spriteCriticalReadyCount >= spriteCriticalTotalCount;
        criticalReady = spriteCriticalReady && runtimeSnapshot.criticalReady;
        reachedThreshold = criticalReady && ratio >= requiredReadyRatio;
        if (debugLogs && SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs) {
          Debug.Log(
            $"[StreamingWarmOrchestrator][DebugLoop] reachedThreshold={reachedThreshold} " +
            $"criticalReady={criticalReady} (sprite={spriteCriticalReady}, runtime={runtimeSnapshot.criticalReady}) " +
            $"ratio={ratio:0.000} (required={requiredReadyRatio:0.000}) " +
            $"readyCount={readyCount} totalCount={totalCount} " +
            $"runtimeReady={runtimeSnapshot.readyCount} runtimeTotal={runtimeSnapshot.totalCount} " +
            $"runtimeCriticalReady={runtimeSnapshot.criticalReadyCount} runtimeCriticalTotal={runtimeSnapshot.criticalTotalCount} " +
            $"runtimePrepared={runtimeSnapshot.prepared}"
          );
        }
        progressSampleInterval = totalCount > 4096 ? 3 : (totalCount > 2048 ? 2 : 1);
        nextProgressSampleFrame = frame + progressSampleInterval;

        hasActiveProgress = true;
        activeProgress = new WarmProgressSnapshot(
          context: context,
          readyCount: readyCount,
          totalCount: totalCount,
          criticalReadyCount: criticalReadyCount,
          criticalTotalCount: criticalTotalCount,
          readyRatio: ratio,
          softTimedOut: softTimedOut,
          criticalReady: criticalReady
        );
      }

      var now = Time.realtimeSinceStartup;
      if (shouldSampleProgress && debugLogs && now >= nextProgressLogAt && !reachedThreshold) {
        var deferredSnapshot = TextureResidencyCache.GetDeferredSnapshot();
        var runtimeSnapshot = GetRuntimeWarmSnapshot(activeRuntimeTrackedWarmId);
        Debug.Log(
          "[StreamingWarmOrchestrator] Progress context=" + context +
          " ready=" + readyCount + "/" + totalCount +
          " critical=" + criticalReadyCount + "/" + criticalTotalCount +
          " ratio=" + ratio.ToString("0.000") +
          " soft_timeout=" + (softTimedOut ? 1 : 0) +
          " runtime_prepared=" + (runtimeSnapshot.prepared ? 1 : 0) +
          " runtime_queue=" + runtimeSnapshot.queue.queuedCount +
          " runtime_in_flight=" + runtimeSnapshot.queue.inFlightCount +
          " runtime_preparing=" + runtimeSnapshot.queue.preparingCount +
          " deferred_pending=" + deferredSnapshot.pendingCount +
          " deferred_flushed_frame=" + deferredSnapshot.flushedThisFrame +
          " deferred_total=" + deferredSnapshot.totalDeferredCount +
          " deferred_requests_total=" + deferredSnapshot.totalDeferralRequestCount +
          " deferred_promoted=" + deferredSnapshot.totalPromotedCount
        );
        nextProgressLogAt = now + logIntervalSeconds;
      }

      if (reachedThreshold) break;
      if (!softTimedOut && now >= softTimeoutAt) {
        softTimedOut = true;
      }
      if (softTimedOut && criticalReady && request.allowCriticalReadySoftTimeout) break;
      if (now >= hardTimeoutAt) {
        if (!request.allowHardTimeoutBypass || SingleSceneManager.IsCriticalScopeReadyForRevealStatic()) {
          hardTimedOut = true;
          break;
        }
      }
      yield return null;
    }

    TextureResidencyCache.EndSession();

    var elapsedMs = Mathf.Max((Time.realtimeSinceStartup - startedAt) * 1000f, 0f);
    var hardTimeoutBypassUsed = false;
    var failureReason = "";
    if (!reachedThreshold) {
      if (hardTimedOut && !criticalReady) {
        if (request.allowHardTimeoutBypass && SingleSceneManager.IsCriticalScopeReadyForRevealStatic()) {
          hardTimeoutBypassUsed = true;
          failureReason = "hard_timeout_critical_not_ready";
        }
        else {
          failureReason = "hard_timeout";
        }
      }
      else if (softTimedOut && criticalReady) {
        failureReason = "soft_timeout_ratio_not_reached";
      }
      else if (softTimedOut) {
        failureReason = "soft_timeout";
      }
    }

    var result = new WarmResult(
      context: context,
      completedWithinTimeout: !softTimedOut,
      reachedReadyThreshold: reachedThreshold,
      playerCriticalReady: criticalReady,
      readyRatio: ratio,
      readyCount: readyCount,
      totalCount: totalCount,
      criticalReadyCount: criticalReadyCount,
      criticalTotalCount: criticalTotalCount,
      requestedAddressCount: uniqueScheduledImagesScratch.Count + GetRuntimeWarmSnapshot(activeRuntimeTrackedWarmId).totalCount,
      elapsedMs: elapsedMs,
      hardTimeoutBypassUsed: hardTimeoutBypassUsed,
      failureReason: failureReason
    );

    var token = NormalizeToken(request.idempotencyToken);
    if (!string.IsNullOrWhiteSpace(token) && (reachedThreshold || hardTimeoutBypassUsed || criticalReady)) {
      completedWarmTokens.Add(token);
    }

    SpriteStreamingDiagnostics.RecordWarmCheckpoint(result);
    if (debugLogs) {
      var deferredSnapshot = TextureResidencyCache.GetDeferredSnapshot();
      Debug.Log(
        "[StreamingWarmOrchestrator] Complete context=" + context +
        " reached_threshold=" + result.reachedReadyThreshold +
        " completed_in_time=" + result.completedWithinTimeout +
        " ready=" + result.readyCount + "/" + result.totalCount +
        " critical=" + result.criticalReadyCount + "/" + result.criticalTotalCount +
        " hard_bypass=" + (result.hardTimeoutBypassUsed ? 1 : 0) +
        " failure_reason='" + result.failureReason + "'" +
        " elapsed_ms=" + result.elapsedMs.ToString("0.0") +
        " runtime_loaded=" + GetRuntimeWarmSnapshot(activeRuntimeTrackedWarmId).readyCount +
        " deferred_pending=" + deferredSnapshot.pendingCount +
        " deferred_flushed_frame=" + deferredSnapshot.flushedThisFrame +
        " deferred_total=" + deferredSnapshot.totalDeferredCount +
        " deferred_requests_total=" + deferredSnapshot.totalDeferralRequestCount +
        " deferred_promoted=" + deferredSnapshot.totalPromotedCount
      );
    }

    var callback = activeCallback;
    activeRoutine = null;
    activeCallback = null;
    if (activeRuntimeTrackedWarmId > 0) {
      RuntimeAssetCache.ReleaseTrackedWarmup(activeRuntimeTrackedWarmId);
      activeRuntimeTrackedWarmId = 0;
    }
    hasActiveProgress = false;
    activeProgress = default;
    ClearScratch();
    callback?.Invoke(result);
  }

  WarmRequest SanitizeRequest(WarmRequest request) {
    var timeoutSeconds = request.timeoutSeconds;
    if (timeoutSeconds <= 0f) timeoutSeconds = 2.5f;

    var hardTimeoutSeconds = request.hardTimeoutSeconds;
    if (hardTimeoutSeconds <= 0f) hardTimeoutSeconds = timeoutSeconds + 2f;
    if (hardTimeoutSeconds < timeoutSeconds) hardTimeoutSeconds = timeoutSeconds;

    var requiredReadyRatio = request.requiredReadyRatio;
    if (requiredReadyRatio <= 0f || requiredReadyRatio > 1f) requiredReadyRatio = 0.95f;

    var playerWarmFrames = request.playerWarmFrames;
    if (playerWarmFrames <= 0) playerWarmFrames = 1;

    var enemyWarmFrames = request.enemyWarmFrames;
    if (enemyWarmFrames < 0) enemyWarmFrames = 0;

    var effectWarmFrames = request.effectWarmFrames;
    if (effectWarmFrames <= 0) effectWarmFrames = 1;

    var maxRequestedAddresses = request.maxRequestedAddresses;
    if (maxRequestedAddresses <= 0) maxRequestedAddresses = 131072;

    return new WarmRequest(
      context: request.context,
      playerController: request.playerController,
      criticalEnemyControllers: request.criticalEnemyControllers,
      enemyControllers: request.enemyControllers,
      enemyArchetypePrefabsByType: request.enemyArchetypePrefabsByType,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: requiredReadyRatio,
      playerWarmFrames: playerWarmFrames,
      enemyWarmFrames: enemyWarmFrames,
      effectWarmFrames: effectWarmFrames,
      maxRequestedAddresses: maxRequestedAddresses,
      includeEffects: request.includeEffects,
      extraCriticalLibraries: request.extraCriticalLibraries,
      extraCriticalAddresses: request.extraCriticalAddresses,
      extraWarmLibraries: request.extraWarmLibraries,
      extraWarmAddresses: request.extraWarmAddresses,
      extraCriticalAssetAddresses: request.extraCriticalAssetAddresses,
      extraWarmAssetAddresses: request.extraWarmAssetAddresses,
      hardTimeoutSeconds: hardTimeoutSeconds,
      allowHardTimeoutBypass: request.allowHardTimeoutBypass,
      idempotencyToken: request.idempotencyToken,
      skipIfTokenAlreadyWarm: request.skipIfTokenAlreadyWarm,
      extraCriticalLabels: request.extraCriticalLabels,
      extraWarmLabels: request.extraWarmLabels,
      extraCriticalAssetLabels: request.extraCriticalAssetLabels,
      extraWarmAssetLabels: request.extraWarmAssetLabels,
      criticalPlayerEffectKeys: request.criticalPlayerEffectKeys,
      allowCriticalReadySoftTimeout: request.allowCriticalReadySoftTimeout
    );
  }

  int ResolveWarmOutstandingTarget() {
    var target = Application.isMobilePlatform ? MobileWarmOutstandingTarget : DesktopWarmOutstandingTarget;
    var memoryMb = Math.Max(SystemInfo.systemMemorySize, 0);
    if (memoryMb > 0 && memoryMb <= 4096) target = Math.Min(target, 700);
    else if (memoryMb > 0 && memoryMb <= 8192) target = Math.Min(target, 1100);
    return Math.Max(target, 256);
  }

  int ResolveActiveMaxRequestedAddresses(WarmContext context, int requestedMax) {
    var requested = Math.Max(requestedMax, 256);
    // Full warm-set mode: honor request cap directly so loading overlay can preload
    // all declared addresses for instant gameplay playback.
    return Mathf.Clamp(requested, 256, 1048576);
  }

  int ResolveHighPriorityAddressCap(WarmContext context) {
    var outstandingTarget = ResolveWarmOutstandingTarget();
    switch (context) {
      case WarmContext.LoadSave:
        return Mathf.Clamp(Mathf.RoundToInt(outstandingTarget * 0.45f), 256, 900);
      case WarmContext.GearApplyReturn:
        return Mathf.Clamp(Mathf.RoundToInt(outstandingTarget * 0.35f), 192, 640);
      case WarmContext.EnemyWaveSpawn:
        return Mathf.Clamp(Mathf.RoundToInt(outstandingTarget * 0.50f), 256, 900);
      default:
        return Mathf.Clamp(Mathf.RoundToInt(outstandingTarget * 0.60f), 320, 1200);
    }
  }

  int ResolveCriticalReadyAddressCap(WarmContext context, int highPriorityCap) {
    var cap = Math.Max(highPriorityCap / 2, 128);
    switch (context) {
      case WarmContext.LoadSave:
        cap = Math.Min(cap, 1024);
        break;
      case WarmContext.GearApplyReturn:
        cap = Math.Min(cap, 512);
        break;
      case WarmContext.EnemyWaveSpawn:
        cap = Math.Min(cap, 512);
        break;
      default:
        cap = Math.Min(cap, 1024);
        break;
    }
    return Math.Max(cap, 128);
  }


  int ResolveWarmBackgroundAddressCap(int highPriorityAddressCount) {
    var targetOutstanding = ResolveWarmOutstandingTarget();
    var remainingBudget = targetOutstanding - Math.Max(highPriorityAddressCount, 0);
    if (remainingBudget <= 0) return 0;
    return Math.Min(remainingBudget, Math.Max(activeMaxRequestedAddresses, 0));
  }

  void ClearScratch() {
    warmLibrarySet.Clear();
    criticalLibrarySet.Clear();
    warmAddressSet.Clear();
    warmLabelSet.Clear();
    highPriorityAddressSet.Clear();
    highPriorityLabelSet.Clear();
    readyAddressSet.Clear();
    readyLabelSet.Clear();
    criticalReadyAddressSet.Clear();
    criticalReadyLabelSet.Clear();
    scheduledAddressSet.Clear();
    scheduledReadyAddressSet.Clear();
    scheduledCriticalReadyAddressSet.Clear();
    archetypeTargetCache.Clear();
    playerWarmAtlasSeedAddresses.Clear();
    warmAddressBatch.Clear();
    normalizedTokenSetScratch.Clear();
    rescueDispatchBuffer.Clear();
    uniqueScheduledImagesScratch.Clear();
    uniqueScheduledReadyImagesScratch.Clear();
    uniqueScheduledCriticalReadyImagesScratch.Clear();
    uniqueWarmImagesScratch.Clear();
    uniqueCriticalImagesScratch.Clear();
  }

  static void GetUniqueImageSets(
    HashSet<string> addresses,
    HashSet<string> outUniqueImages
  ) {
    outUniqueImages.Clear();
    if (addresses == null) return;
    foreach (var address in addresses) {
      var normalized = NormalizeToken(address);
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      if (SpriteSliceAddressUtility.TryParseSliceAddress(normalized, out var atlasAssetPath, out _)) {
        outUniqueImages.Add(NormalizeToken(atlasAssetPath));
      } else {
        outUniqueImages.Add(normalized);
      }
    }
  }

  static int CountReadyImages(HashSet<string> uniqueImages, bool pumpEntries) {
    if (uniqueImages == null || uniqueImages.Count == 0) return 0;
    var count = 0;
    foreach (var img in uniqueImages) {
      if (TextureResidencyCache.IsReady(img, pumpEntries)) count++;
    }
    return count;
  }

  static bool ShouldLogLoadingDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }
}
