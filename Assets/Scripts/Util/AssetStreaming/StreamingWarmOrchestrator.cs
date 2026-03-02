using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum WarmContext {
  StartGame = 0,
  LoadSave = 1,
  GearApplyReturn = 2,
  EnemyWaveSpawn = 3
}

public readonly struct WarmRequest {
  public readonly WarmContext context;
  public readonly GearController playerController;
  public readonly EnemyController[] enemyControllers;
  public readonly Dictionary<string, GameObject> enemyArchetypePrefabsByType;
  public readonly float timeoutSeconds;
  public readonly float requiredReadyRatio;
  public readonly int playerWarmFrames;
  public readonly int enemyWarmFrames;
  public readonly int effectWarmFrames;
  public readonly int maxRequestedAddresses;
  public readonly bool includeEffects;
  public readonly List<string> extraCriticalLibraries;
  public readonly List<string> extraCriticalAddresses;
  public readonly List<string> extraWarmLibraries;
  public readonly List<string> extraWarmAddresses;
  public readonly float hardTimeoutSeconds;
  public readonly bool allowHardTimeoutBypass;
  public readonly string idempotencyToken;
  public readonly bool skipIfTokenAlreadyWarm;

  public WarmRequest(
    WarmContext context,
    GearController playerController = null,
    EnemyController[] enemyControllers = null,
    Dictionary<string, GameObject> enemyArchetypePrefabsByType = null,
    float timeoutSeconds = 2.5f,
    float requiredReadyRatio = 0.95f,
    int playerWarmFrames = 4,
    int enemyWarmFrames = 2,
    int effectWarmFrames = 1,
    int maxRequestedAddresses = 131072,
    bool includeEffects = true,
    List<string> extraCriticalLibraries = null,
    List<string> extraCriticalAddresses = null,
    List<string> extraWarmLibraries = null,
    List<string> extraWarmAddresses = null,
    float hardTimeoutSeconds = 6.0f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = false
  ) {
    this.context = context;
    this.playerController = playerController;
    this.enemyControllers = enemyControllers;
    this.enemyArchetypePrefabsByType = enemyArchetypePrefabsByType;
    this.timeoutSeconds = timeoutSeconds;
    this.requiredReadyRatio = requiredReadyRatio;
    this.playerWarmFrames = playerWarmFrames;
    this.enemyWarmFrames = enemyWarmFrames;
    this.effectWarmFrames = effectWarmFrames;
    this.maxRequestedAddresses = maxRequestedAddresses;
    this.includeEffects = includeEffects;
    this.extraCriticalLibraries = extraCriticalLibraries;
    this.extraCriticalAddresses = extraCriticalAddresses;
    this.extraWarmLibraries = extraWarmLibraries;
    this.extraWarmAddresses = extraWarmAddresses;
    this.hardTimeoutSeconds = hardTimeoutSeconds;
    this.allowHardTimeoutBypass = allowHardTimeoutBypass;
    this.idempotencyToken = idempotencyToken;
    this.skipIfTokenAlreadyWarm = skipIfTokenAlreadyWarm;
  }

  public static WarmRequest CreateStartGame(
    GearController playerController,
    EnemyController[] enemyControllers = null,
    Dictionary<string, GameObject> enemyArchetypePrefabsByType = null,
    float timeoutSeconds = 3.0f,
    float requiredReadyRatio = 0.95f,
    List<string> extraCriticalLibraries = null,
    List<string> extraCriticalAddresses = null,
    List<string> extraWarmLibraries = null,
    List<string> extraWarmAddresses = null,
    float hardTimeoutSeconds = 6.0f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = false
  ) {
    return new WarmRequest(
      context: WarmContext.StartGame,
      playerController: playerController,
      enemyControllers: enemyControllers,
      enemyArchetypePrefabsByType: enemyArchetypePrefabsByType,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: requiredReadyRatio,
      playerWarmFrames: 1,
      enemyWarmFrames: 1,
      effectWarmFrames: 1,
      maxRequestedAddresses: 32768,
      includeEffects: true,
      extraCriticalLibraries: extraCriticalLibraries,
      extraCriticalAddresses: extraCriticalAddresses,
      extraWarmLibraries: extraWarmLibraries,
      extraWarmAddresses: extraWarmAddresses,
      hardTimeoutSeconds: hardTimeoutSeconds,
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: idempotencyToken,
      skipIfTokenAlreadyWarm: skipIfTokenAlreadyWarm
    );
  }

  public static WarmRequest CreateLoadSave(
    GearController playerController,
    EnemyController[] enemyControllers = null,
    Dictionary<string, GameObject> enemyArchetypePrefabsByType = null,
    float timeoutSeconds = 3.5f,
    float requiredReadyRatio = 0.95f,
    List<string> extraCriticalLibraries = null,
    List<string> extraCriticalAddresses = null,
    List<string> extraWarmLibraries = null,
    List<string> extraWarmAddresses = null,
    float hardTimeoutSeconds = 6.5f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = false
  ) {
    return new WarmRequest(
      context: WarmContext.LoadSave,
      playerController: playerController,
      enemyControllers: enemyControllers,
      enemyArchetypePrefabsByType: enemyArchetypePrefabsByType,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: requiredReadyRatio,
      playerWarmFrames: 1,
      enemyWarmFrames: 1,
      effectWarmFrames: 1,
      maxRequestedAddresses: 32768,
      includeEffects: true,
      extraCriticalLibraries: extraCriticalLibraries,
      extraCriticalAddresses: extraCriticalAddresses,
      extraWarmLibraries: extraWarmLibraries,
      extraWarmAddresses: extraWarmAddresses,
      hardTimeoutSeconds: hardTimeoutSeconds,
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: idempotencyToken,
      skipIfTokenAlreadyWarm: skipIfTokenAlreadyWarm
    );
  }

  public static WarmRequest CreateGearApplyReturn(
    GearController playerController,
    float timeoutSeconds = 2.0f,
    float requiredReadyRatio = 0.95f,
    List<string> extraCriticalLibraries = null,
    List<string> extraCriticalAddresses = null,
    List<string> extraWarmLibraries = null,
    List<string> extraWarmAddresses = null,
    float hardTimeoutSeconds = 4.5f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = false
  ) {
    return new WarmRequest(
      context: WarmContext.GearApplyReturn,
      playerController: playerController,
      enemyControllers: null,
      enemyArchetypePrefabsByType: null,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: requiredReadyRatio,
      playerWarmFrames: 1,
      enemyWarmFrames: 0,
      effectWarmFrames: 1,
      maxRequestedAddresses: 16384,
      includeEffects: true,
      extraCriticalLibraries: extraCriticalLibraries,
      extraCriticalAddresses: extraCriticalAddresses,
      extraWarmLibraries: extraWarmLibraries,
      extraWarmAddresses: extraWarmAddresses,
      hardTimeoutSeconds: hardTimeoutSeconds,
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: idempotencyToken,
      skipIfTokenAlreadyWarm: skipIfTokenAlreadyWarm
    );
  }

  public static WarmRequest CreateEnemyWaveSpawn(
    Dictionary<string, GameObject> enemyArchetypePrefabsByType,
    float timeoutSeconds = 2.0f,
    float requiredReadyRatio = 0.95f,
    int enemyWarmFrames = 2,
    List<string> extraCriticalLibraries = null,
    List<string> extraCriticalAddresses = null,
    List<string> extraWarmLibraries = null,
    List<string> extraWarmAddresses = null,
    float hardTimeoutSeconds = 4.5f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = true
  ) {
    return new WarmRequest(
      context: WarmContext.EnemyWaveSpawn,
      playerController: null,
      enemyControllers: null,
      enemyArchetypePrefabsByType: enemyArchetypePrefabsByType,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: requiredReadyRatio,
      playerWarmFrames: 0,
      enemyWarmFrames: enemyWarmFrames,
      effectWarmFrames: 2,
      maxRequestedAddresses: 32768,
      includeEffects: true,
      extraCriticalLibraries: extraCriticalLibraries,
      extraCriticalAddresses: extraCriticalAddresses,
      extraWarmLibraries: extraWarmLibraries,
      extraWarmAddresses: extraWarmAddresses,
      hardTimeoutSeconds: hardTimeoutSeconds,
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: idempotencyToken,
      skipIfTokenAlreadyWarm: skipIfTokenAlreadyWarm
    );
  }
}

public readonly struct WarmResult {
  public readonly WarmContext context;
  public readonly bool completedWithinTimeout;
  public readonly bool reachedReadyThreshold;
  public readonly bool playerCriticalReady;
  public readonly float readyRatio;
  public readonly int readyCount;
  public readonly int totalCount;
  public readonly int criticalReadyCount;
  public readonly int criticalTotalCount;
  public readonly int requestedAddressCount;
  public readonly float elapsedMs;
  public readonly bool hardTimeoutBypassUsed;
  public readonly string failureReason;

  public WarmResult(
    WarmContext context,
    bool completedWithinTimeout,
    bool reachedReadyThreshold,
    bool playerCriticalReady,
    float readyRatio,
    int readyCount,
    int totalCount,
    int criticalReadyCount,
    int criticalTotalCount,
    int requestedAddressCount,
    float elapsedMs,
    bool hardTimeoutBypassUsed,
    string failureReason
  ) {
    this.context = context;
    this.completedWithinTimeout = completedWithinTimeout;
    this.reachedReadyThreshold = reachedReadyThreshold;
    this.playerCriticalReady = playerCriticalReady;
    this.readyRatio = readyRatio;
    this.readyCount = readyCount;
    this.totalCount = totalCount;
    this.criticalReadyCount = criticalReadyCount;
    this.criticalTotalCount = criticalTotalCount;
    this.requestedAddressCount = requestedAddressCount;
    this.elapsedMs = elapsedMs;
    this.hardTimeoutBypassUsed = hardTimeoutBypassUsed;
    this.failureReason = string.IsNullOrWhiteSpace(failureReason) ? "" : failureReason.Trim();
  }
}

public readonly struct WarmProgressSnapshot {
  public readonly WarmContext context;
  public readonly int readyCount;
  public readonly int totalCount;
  public readonly int criticalReadyCount;
  public readonly int criticalTotalCount;
  public readonly float readyRatio;
  public readonly bool softTimedOut;
  public readonly bool criticalReady;

  public WarmProgressSnapshot(
    WarmContext context,
    int readyCount,
    int totalCount,
    int criticalReadyCount,
    int criticalTotalCount,
    float readyRatio,
    bool softTimedOut,
    bool criticalReady
  ) {
    this.context = context;
    this.readyCount = readyCount;
    this.totalCount = totalCount;
    this.criticalReadyCount = criticalReadyCount;
    this.criticalTotalCount = criticalTotalCount;
    this.readyRatio = readyRatio;
    this.softTimedOut = softTimedOut;
    this.criticalReady = criticalReady;
  }
}

public interface IStreamingWarmOrchestrator {
  bool IsRunning { get; }
  void Run(WarmRequest request, Action<WarmResult> onComplete = null);
  void Cancel();
}

public sealed class StreamingWarmOrchestrator : MonoBehaviour, IStreamingWarmOrchestrator {
  static StreamingWarmOrchestrator instance;
  static readonly HashSet<string> completedWarmTokens = new(StringComparer.OrdinalIgnoreCase);
  static WarmProgressSnapshot activeProgress;
  static bool hasActiveProgress;

  readonly HashSet<string> warmLibrarySet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> warmAddressSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> highPriorityAddressSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> readyAddressSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> criticalReadyAddressSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> playerCriticalAtlasSeedAddresses = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> playerWarmAtlasSeedAddresses = new(StringComparer.OrdinalIgnoreCase);
  readonly List<string> atlasSiblingScratch = new(512);
  readonly List<string> warmAddressBatch = new(2048);

  Coroutine activeRoutine;
  Action<WarmResult> activeCallback;
  int activeMaxRequestedAddresses;
  int activeFrameAddressProbeBudget;
  int warmPlanFrameAddressProbeCount;
  bool warmPlanProbeBudgetExceeded;
  bool warmPlanAddressCapReached;
  int warmPlanDroppedAddresses;

  const int MinWarmPlanFrameAddressProbeBudget = 32768;
  const int MaxWarmPlanFrameAddressProbeBudget = 1000000;
  const int MaxAnimationSamplesPerClip = 8;

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
    var keys = new List<string>();
    if (enemyArchetypePrefabsByType != null) {
      foreach (var pair in enemyArchetypePrefabsByType) {
        var key = NormalizeToken(pair.Key);
        if (string.IsNullOrWhiteSpace(key)) continue;
        if (keys.Contains(key)) continue;
        keys.Add(key);
      }
    }
    keys.Sort(StringComparer.OrdinalIgnoreCase);
    var keyCsv = keys.Count > 0 ? string.Join(",", keys) : "none";
    return "location:" + normalizedLocation + "|enemies:" + keyCsv;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    instance = null;
    completedWarmTokens.Clear();
    hasActiveProgress = false;
    activeProgress = default;
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
    activeMaxRequestedAddresses = Mathf.Max(request.maxRequestedAddresses, 256);
    activeFrameAddressProbeBudget = Mathf.Clamp(activeMaxRequestedAddresses * 4, MinWarmPlanFrameAddressProbeBudget, MaxWarmPlanFrameAddressProbeBudget);
    var debugLogs = ShouldLogLoadingDebug();
    var logIntervalSeconds = Mathf.Max(SpriteStreamingRuntimeSettings.LoadingProgressLogIntervalMs, 100) / 1000f;

    warmPlanFrameAddressProbeCount = 0;
    warmPlanProbeBudgetExceeded = false;
    warmPlanAddressCapReached = false;
    warmPlanDroppedAddresses = 0;
    BuildWarmPlan(request, includeResolvedAddressSweeps: false);

    if (warmLibrarySet.Count > 0) {
      SpriteRuntimeResolver.WarmupLibraries(warmLibrarySet);
    }

    var resolverWaitFrames = 0;
    var resolverSweepFrames = 0;
    var extendedSweepDeadline = hardTimeoutAt + Mathf.Max(request.timeoutSeconds, 2.0f);
    while (true) {
      var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
      BuildWarmPlan(request, includeResolvedAddressSweeps: true);
      resolverSweepFrames++;
      var hasResolvedAddresses = warmAddressSet.Count > 0;
      var now = Time.realtimeSinceStartup;
      var reachedHardTimeout = now >= hardTimeoutAt;
      var reachedExtendedSweepTimeout = now >= extendedSweepDeadline;

      if (resolverIdle && hasResolvedAddresses) break;
      if (reachedHardTimeout && (hasResolvedAddresses || reachedExtendedSweepTimeout)) {
        break;
      }
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
    if (debugLogs && warmPlanProbeBudgetExceeded) {
      Debug.LogWarning(
        "[StreamingWarmOrchestrator] Warm plan frame-address probe budget reached." +
        " context=" + context +
        " probes=" + warmPlanFrameAddressProbeCount +
        " budget=" + activeFrameAddressProbeBudget
      );
    }
    if (debugLogs && warmPlanAddressCapReached) {
      Debug.LogWarning(
        "[StreamingWarmOrchestrator] Warm plan address cap reached." +
        " context=" + context +
        " cap=" + activeMaxRequestedAddresses +
        " dropped=" + warmPlanDroppedAddresses
      );
    }
    ExpandPlayerAtlasSeeds();

    if (debugLogs) {
      Debug.Log(
        "[StreamingWarmOrchestrator] Start context=" + context +
        " timeout_s=" + request.timeoutSeconds.ToString("0.00") +
        " hard_timeout_s=" + request.hardTimeoutSeconds.ToString("0.00") +
        " required_ratio=" + request.requiredReadyRatio.ToString("0.00") +
        " libraries=" + warmLibrarySet.Count +
        " addresses=" + warmAddressSet.Count +
        " critical=" + criticalReadyAddressSet.Count
      );
    }

    var highPriorityEnqueueBudget = ResolveEnqueueBudgetPerFrame(highPriorityAddressSet.Count, isHighPriority: true);
    IEnumerator highPriorityEnqueue = null;
    if (highPriorityAddressSet.Count > 0) {
      highPriorityEnqueue = TextureResidencyCache.RequestLoadBatchThrottled(
        highPriorityAddressSet,
        TextureResidencyCache.LoadPriority.Immediate,
        allowAtlasExpansion: true,
        enqueueBudgetPerFrame: highPriorityEnqueueBudget
      );
    }

    warmAddressBatch.Clear();
    foreach (var address in warmAddressSet) {
      if (highPriorityAddressSet.Contains(address)) continue;
      warmAddressBatch.Add(address);
    }

    var warmEnqueueBudget = ResolveEnqueueBudgetPerFrame(warmAddressBatch.Count, isHighPriority: false);
    IEnumerator warmEnqueue = null;
    if (warmAddressBatch.Count > 0) {
      warmEnqueue = TextureResidencyCache.RequestLoadBatchThrottled(
        warmAddressBatch,
        TextureResidencyCache.LoadPriority.Warmup,
        allowAtlasExpansion: true,
        enqueueBudgetPerFrame: warmEnqueueBudget
      );
    }

    StepEnqueue(ref highPriorityEnqueue);
    StepEnqueue(ref warmEnqueue);
    TextureResidencyCache.Pump();

    var requiredReadyRatio = Mathf.Clamp01(request.requiredReadyRatio);
    var reachedThreshold = false;
    var softTimedOut = false;
    var hardTimedOut = false;
    var readyCount = 0;
    var totalCount = readyAddressSet.Count;
    var criticalReadyCount = 0;
    var criticalTotalCount = criticalReadyAddressSet.Count;
    var ratio = totalCount > 0 ? 0f : 1f;
    var criticalReady = criticalTotalCount <= 0;
    var nextProgressLogAt = startedAt + logIntervalSeconds;
    var progressSampleInterval = totalCount > 4096 ? 3 : (totalCount > 2048 ? 2 : 1);
    var nextProgressSampleFrame = Time.frameCount;

    while (true) {
      StepEnqueue(ref highPriorityEnqueue);
      StepEnqueue(ref warmEnqueue);
      TextureResidencyCache.Pump();
      var frame = Time.frameCount;
      var shouldSampleProgress = frame >= nextProgressSampleFrame;
      if (shouldSampleProgress) {
        readyCount = CountReadyAddresses(readyAddressSet, pumpEntries: false);
        criticalReadyCount = CountReadyAddresses(criticalReadyAddressSet, pumpEntries: false);
        ratio = totalCount > 0 ? ((float)readyCount / totalCount) : 1f;
        criticalReady = criticalTotalCount <= 0 || criticalReadyCount >= criticalTotalCount;
        reachedThreshold = criticalReady && ratio >= requiredReadyRatio;
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
        Debug.Log(
          "[StreamingWarmOrchestrator] Progress context=" + context +
          " ready=" + readyCount + "/" + totalCount +
          " critical=" + criticalReadyCount + "/" + criticalTotalCount +
          " ratio=" + ratio.ToString("0.000") +
          " soft_timeout=" + (softTimedOut ? 1 : 0)
        );
        nextProgressLogAt = now + logIntervalSeconds;
      }

      if (reachedThreshold) break;
      if (!softTimedOut && now >= softTimeoutAt) {
        softTimedOut = true;
      }
      if (softTimedOut && criticalReady) break;
      if (now >= hardTimeoutAt) {
        hardTimedOut = true;
        break;
      }
      yield return null;
    }

    var elapsedMs = Mathf.Max((Time.realtimeSinceStartup - startedAt) * 1000f, 0f);
    var hardTimeoutBypassUsed = false;
    var failureReason = "";
    if (!reachedThreshold) {
      if (hardTimedOut && !criticalReady) {
        if (request.allowHardTimeoutBypass) {
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
      requestedAddressCount: warmAddressSet.Count,
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
      Debug.Log(
        "[StreamingWarmOrchestrator] Complete context=" + context +
        " reached_threshold=" + result.reachedReadyThreshold +
        " completed_in_time=" + result.completedWithinTimeout +
        " ready=" + result.readyCount + "/" + result.totalCount +
        " critical=" + result.criticalReadyCount + "/" + result.criticalTotalCount +
        " hard_bypass=" + (result.hardTimeoutBypassUsed ? 1 : 0) +
        " failure_reason='" + result.failureReason + "'" +
        " elapsed_ms=" + result.elapsedMs.ToString("0.0")
      );
    }

    var callback = activeCallback;
    activeRoutine = null;
    activeCallback = null;
    hasActiveProgress = false;
    activeProgress = default;
    ClearScratch();
    callback?.Invoke(result);
  }

  void BuildWarmPlan(WarmRequest request, bool includeResolvedAddressSweeps) {
    AddLibraries(request.extraCriticalLibraries);
    AddLibraries(request.extraWarmLibraries);
    if (HasReachedWarmAddressCap()) return;
    AddDirectAddresses(request.extraCriticalAddresses, markCritical: true, markHighPriority: true);
    if (HasReachedWarmAddressCap()) return;
    AddDirectAddresses(request.extraWarmAddresses, markCritical: false, markHighPriority: false);
    if (HasReachedWarmAddressCap()) return;

    if (request.playerController != null) {
      CollectPlayerWarmPlan(request.playerController, request.playerWarmFrames, request.effectWarmFrames, request.includeEffects, includeResolvedAddressSweeps);
      if (HasReachedWarmAddressCap()) return;
    }

    if (request.enemyControllers != null && request.enemyControllers.Length > 0) {
      for (var i = 0; i < request.enemyControllers.Length; i++) {
        if (HasReachedWarmAddressCap()) return;
        CollectEnemyControllerWarmPlan(request.enemyControllers[i], request.enemyWarmFrames, request.effectWarmFrames, request.includeEffects, includeResolvedAddressSweeps);
      }
    }

    if (request.enemyArchetypePrefabsByType != null && request.enemyArchetypePrefabsByType.Count > 0) {
      if (HasReachedWarmAddressCap()) return;
      CollectEnemyArchetypeWarmPlan(request.enemyArchetypePrefabsByType, request.enemyWarmFrames, request.effectWarmFrames, request.includeEffects, includeResolvedAddressSweeps);
    }
  }

  void CollectPlayerWarmPlan(
    GearController controller,
    int warmFrames,
    int effectWarmFrames,
    bool includeEffects,
    bool includeResolvedAddressSweeps
  ) {
    if (controller == null) return;
    if (HasReachedWarmAddressCap()) return;
    AddLibrariesFromGameObjects(controller.SkinObjects);
    AddLibrariesFromGameObjects(controller.GearObjects);
    if (HasReachedWarmAddressCap()) return;
    if (includeEffects && controller.effectNode != null) {
      AddLibrary(controller.effectNode.libraryName);
    }
    if (HasReachedWarmAddressCap()) return;
    if (!includeResolvedAddressSweeps) return;

    CollectAtlasSeedAddressesForObjects(controller.SkinObjects, Animations.Esperanza, warmFrames, playerCriticalAtlasSeedAddresses);
    if (HasReachedWarmAddressCap()) return;
    CollectAtlasSeedAddressesForObjects(controller.GearObjects, Animations.Esperanza, warmFrames, playerWarmAtlasSeedAddresses);
    if (HasReachedWarmAddressCap()) return;
    CollectAnimationStartsForObjects(controller.SkinObjects, Animations.Esperanza, warmFrames, markCritical: true);
    if (HasReachedWarmAddressCap()) return;
    CollectAnimationStartsForObjects(controller.GearObjects, Animations.Esperanza, warmFrames, markCritical: false);
    if (HasReachedWarmAddressCap()) return;

    if (includeEffects && controller.effectNode != null) {
      CollectEffectStartsForTarget(controller.effectNode, Effects.Esperanza, effectWarmFrames, markCritical: false);
      if (HasReachedWarmAddressCap()) return;
      CollectEffectStartsForTarget(controller.effectNode, Effects.Things, effectWarmFrames, markCritical: false);
      if (HasReachedWarmAddressCap()) return;
      CollectEffectStartsForTarget(controller.effectNode, Effects.Imp, effectWarmFrames, markCritical: false);
    }
  }

  void CollectEnemyControllerWarmPlan(
    EnemyController controller,
    int warmFrames,
    int effectWarmFrames,
    bool includeEffects,
    bool includeResolvedAddressSweeps
  ) {
    if (controller == null) return;
    if (controller.spriteObjects == null || controller.spriteObjects.Length == 0) return;
    if (HasReachedWarmAddressCap()) return;

    var enemyType = NormalizeToken(controller.enemyType);
    if (string.IsNullOrWhiteSpace(enemyType)) return;
    if (!Animations.Enemies.TryGetValue(enemyType, out var enemyAnimations) || enemyAnimations == null || enemyAnimations.Count == 0) return;

    AddLibrariesFromGameObjects(controller.spriteObjects);
    if (includeEffects && controller.effectNode != null) {
      AddLibrary(controller.effectNode.libraryName);
    }
    if (HasReachedWarmAddressCap()) return;
    if (!includeResolvedAddressSweeps) return;

    CollectAnimationStartsForObjects(controller.spriteObjects, enemyAnimations, warmFrames, markCritical: false);
    if (HasReachedWarmAddressCap()) return;

    if (includeEffects && controller.effectNode != null) {
      CollectEffectStartsForTarget(controller.effectNode, ResolveEnemyEffectAnimations(enemyType), effectWarmFrames, markCritical: false);
    }
  }

  void CollectEnemyArchetypeWarmPlan(
    Dictionary<string, GameObject> enemyArchetypePrefabsByType,
    int warmFrames,
    int effectWarmFrames,
    bool includeEffects,
    bool includeResolvedAddressSweeps
  ) {
    if (enemyArchetypePrefabsByType == null || enemyArchetypePrefabsByType.Count == 0) return;
    if (HasReachedWarmAddressCap()) return;

    foreach (var pair in enemyArchetypePrefabsByType) {
      if (HasReachedWarmAddressCap()) return;
      var enemyType = NormalizeToken(pair.Key);
      var root = pair.Value;
      if (string.IsNullOrWhiteSpace(enemyType) || root == null) continue;
      if (!Animations.Enemies.TryGetValue(enemyType, out var enemyAnimations) || enemyAnimations == null || enemyAnimations.Count == 0) continue;

      var targets = root.GetComponentsInChildren<SpriteWithNormals>(true);
      for (var i = 0; i < targets.Length; i++) {
        if (HasReachedWarmAddressCap()) return;
        var target = targets[i];
        if (!IsTargetWarmable(target, allowInactive: true)) continue;
        AddLibrary(target.libraryName);
        if (!includeResolvedAddressSweeps) continue;
        CollectAnimationStartsForTarget(target, enemyAnimations, warmFrames, markCritical: false, allowInactive: true);
      }

      if (!includeEffects || !includeResolvedAddressSweeps) continue;
      var effectAnimations = ResolveEnemyEffectAnimations(enemyType);
      if (effectAnimations == null || effectAnimations.Count == 0) continue;
      for (var i = 0; i < targets.Length; i++) {
        if (HasReachedWarmAddressCap()) return;
        var target = targets[i];
        if (!IsTargetWarmable(target, allowInactive: true)) continue;
        CollectEffectStartsForTarget(target, effectAnimations, effectWarmFrames, markCritical: false, allowInactive: true);
      }
    }
  }

  void CollectAnimationStartsForObjects(
    GameObject[] objects,
    Dictionary<string, AnimData> animations,
    int warmFrames,
    bool markCritical
  ) {
    if (objects == null || objects.Length == 0) return;
    if (animations == null || animations.Count == 0) return;
    if (HasReachedWarmAddressCap()) return;

    var clampedWarmFrames = Mathf.Max(warmFrames, 1);
    for (var i = 0; i < objects.Length; i++) {
      if (HasReachedWarmAddressCap()) return;
      var go = objects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (!IsTargetWarmable(target)) continue;
      CollectAnimationStartsForTarget(target, animations, clampedWarmFrames, markCritical);
    }
  }

  void CollectAnimationStartsForTarget(
    SpriteWithNormals target,
    Dictionary<string, AnimData> animations,
    int warmFrames,
    bool markCritical,
    bool allowInactive = false
  ) {
    if (!IsTargetWarmable(target, allowInactive)) return;
    if (animations == null || animations.Count == 0) return;
    var requestedSamples = Mathf.Clamp(Mathf.Max(warmFrames, 1), 1, MaxAnimationSamplesPerClip);

    if (!target.IsAnimation) {
      if (!TryGetFrameAddressPairBudgeted(target, 0, out var staticPair, categoryOverride: null)) return;
      AddPairAddresses(staticPair, markCritical);
      return;
    }

    foreach (var pair in animations) {
      if (HasReachedWarmAddressCap()) return;
      var animationName = pair.Key;
      var anim = pair.Value;
      if (anim == null || string.IsNullOrWhiteSpace(animationName)) continue;

      var category = ResolveAnimationCategory(animationName, anim);
      var clipStart = Mathf.Max(anim.start, 1);
      var clipEnd = Mathf.Max(anim.end, clipStart);
      var clipLength = Mathf.Max(clipEnd - clipStart + 1, 1);
      var sampleCount = Mathf.Clamp(requestedSamples, 1, clipLength);
      var sampleDenominator = Mathf.Max(sampleCount - 1, 1);
      var lastFrame = int.MinValue;

      for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++) {
        if (HasReachedWarmAddressCap()) return;
        var frame = sampleCount <= 1
          ? clipStart
          : Mathf.RoundToInt(Mathf.Lerp(clipStart, clipEnd, sampleIndex / (float)sampleDenominator));
        frame = Mathf.Clamp(frame, clipStart, clipEnd);
        if (frame == lastFrame) continue;
        lastFrame = frame;

        if (!TryGetFrameAddressPairBudgeted(target, frame, out var addressPair, category)) continue;
        AddPairAddresses(addressPair, markCritical);
      }
    }
  }

  void CollectEffectStartsForTarget(
    SpriteWithNormals target,
    Dictionary<string, EffectData> effects,
    int warmFrames,
    bool markCritical,
    bool allowInactive = false
  ) {
    if (!IsTargetWarmable(target, allowInactive)) return;
    if (effects == null || effects.Count == 0) return;
    var requestedSamples = Mathf.Clamp(Mathf.Max(warmFrames, 1), 1, MaxAnimationSamplesPerClip);

    foreach (var pair in effects) {
      if (HasReachedWarmAddressCap()) return;
      var effectName = pair.Key;
      var effect = pair.Value;
      if (effect == null || string.IsNullOrWhiteSpace(effectName)) continue;
      var clipStart = Mathf.Max(effect.start, 1);
      var clipEnd = Mathf.Max(effect.end, clipStart);
      var clipLength = Mathf.Max(clipEnd - clipStart + 1, 1);
      var sampleCount = Mathf.Clamp(requestedSamples, 1, clipLength);
      var sampleDenominator = Mathf.Max(sampleCount - 1, 1);
      var lastFrame = int.MinValue;

      for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++) {
        if (HasReachedWarmAddressCap()) return;
        var frame = sampleCount <= 1
          ? clipStart
          : Mathf.RoundToInt(Mathf.Lerp(clipStart, clipEnd, sampleIndex / (float)sampleDenominator));
        frame = Mathf.Clamp(frame, clipStart, clipEnd);
        if (frame == lastFrame) continue;
        lastFrame = frame;

        if (!TryGetFrameAddressPairBudgeted(target, frame, out var addressPair, effectName)) continue;
        AddPairAddresses(addressPair, markCritical);
      }
    }
  }

  bool TryGetFrameAddressPairBudgeted(SpriteWithNormals target, int frame, out SpriteAddressPair pair, string categoryOverride = null) {
    pair = default;
    if (target == null) return false;
    if (HasReachedWarmAddressCap()) return false;
    if (warmPlanFrameAddressProbeCount >= activeFrameAddressProbeBudget) {
      warmPlanProbeBudgetExceeded = true;
      return false;
    }
    warmPlanFrameAddressProbeCount++;
    return target.TryGetFrameAddressPair(frame, out pair, categoryOverride);
  }

  void AddPairAddresses(SpriteAddressPair pair, bool markCritical) {
    if (!AddReadyAddress(pair.colorAddress, markCritical, markCritical)) return;
    AddWarmAddress(pair.normalAddress, markHighPriority: false);
  }

  bool AddReadyAddress(string address, bool markCritical, bool markHighPriority) {
    if (!AddWarmAddress(address, markHighPriority)) return false;
    var normalized = NormalizeToken(address);
    if (string.IsNullOrWhiteSpace(normalized)) return false;
    readyAddressSet.Add(normalized);
    if (markCritical) criticalReadyAddressSet.Add(normalized);
    return true;
  }

  bool AddWarmAddress(string address, bool markHighPriority) {
    var normalized = NormalizeToken(address);
    if (string.IsNullOrWhiteSpace(normalized)) return false;
    if (warmAddressSet.Contains(normalized)) {
      if (markHighPriority) highPriorityAddressSet.Add(normalized);
      return true;
    }
    if (HasReachedWarmAddressCap()) {
      warmPlanDroppedAddresses++;
      return false;
    }
    warmAddressSet.Add(normalized);
    if (markHighPriority) highPriorityAddressSet.Add(normalized);
    return true;
  }

  bool HasReachedWarmAddressCap() {
    if (activeMaxRequestedAddresses <= 0) return false;
    if (warmAddressSet.Count < activeMaxRequestedAddresses) return false;
    warmPlanAddressCapReached = true;
    return true;
  }

  static int CountReadyAddresses(HashSet<string> addresses, bool pumpEntries) {
    if (addresses == null || addresses.Count == 0) return 0;
    var count = 0;
    foreach (var address in addresses) {
      if (TextureResidencyCache.IsReady(address, pumpEntries)) count++;
    }
    return count;
  }

  void CollectAtlasSeedAddressesForObjects(
    GameObject[] objects,
    Dictionary<string, AnimData> animations,
    int warmFrames,
    HashSet<string> seedSet
  ) {
    if (objects == null || objects.Length == 0) return;
    if (animations == null || animations.Count == 0) return;
    if (seedSet == null) return;
    if (HasReachedWarmAddressCap()) return;

    var clampedWarmFrames = Mathf.Max(warmFrames, 1);
    for (var i = 0; i < objects.Length; i++) {
      if (HasReachedWarmAddressCap()) return;
      var go = objects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (!IsTargetWarmable(target)) continue;
      CollectAtlasSeedAddressesForTarget(target, animations, clampedWarmFrames, seedSet);
    }
  }

  void CollectAtlasSeedAddressesForTarget(
    SpriteWithNormals target,
    Dictionary<string, AnimData> animations,
    int warmFrames,
    HashSet<string> seedSet
  ) {
    if (!IsTargetWarmable(target)) return;
    if (animations == null || animations.Count == 0 || seedSet == null) return;
    if (HasReachedWarmAddressCap()) return;
    var requestedSamples = Mathf.Clamp(Mathf.Max(warmFrames, 1), 1, 3);

    if (!target.IsAnimation) {
      if (!TryGetFrameAddressPairBudgeted(target, 0, out var staticPair, categoryOverride: null)) return;
      AddAtlasSeedAddress(staticPair.colorAddress, seedSet);
      AddAtlasSeedAddress(staticPair.normalAddress, seedSet);
      return;
    }

    foreach (var pair in animations) {
      if (HasReachedWarmAddressCap()) return;
      var animationName = pair.Key;
      var anim = pair.Value;
      if (anim == null || string.IsNullOrWhiteSpace(animationName)) continue;

      var category = ResolveAnimationCategory(animationName, anim);
      var clipStart = Mathf.Max(anim.start, 1);
      var clipEnd = Mathf.Max(anim.end, clipStart);
      var clipLength = Mathf.Max(clipEnd - clipStart + 1, 1);
      var sampleCount = Mathf.Clamp(requestedSamples, 1, clipLength);
      var sampleDenominator = Mathf.Max(sampleCount - 1, 1);
      var lastFrame = int.MinValue;

      for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++) {
        if (HasReachedWarmAddressCap()) return;
        var frame = sampleCount <= 1
          ? clipStart
          : Mathf.RoundToInt(Mathf.Lerp(clipStart, clipEnd, sampleIndex / (float)sampleDenominator));
        frame = Mathf.Clamp(frame, clipStart, clipEnd);
        if (frame == lastFrame) continue;
        lastFrame = frame;

        if (!TryGetFrameAddressPairBudgeted(target, frame, out var addressPair, category)) continue;
        AddAtlasSeedAddress(addressPair.colorAddress, seedSet);
        AddAtlasSeedAddress(addressPair.normalAddress, seedSet);
      }
    }
  }

  static void AddAtlasSeedAddress(string address, HashSet<string> seedSet) {
    if (seedSet == null || string.IsNullOrWhiteSpace(address)) return;
    seedSet.Add(address.Trim());
  }

  void ExpandPlayerAtlasSeeds() {
    if (!SpriteStreamingRuntimeSettings.EnableAtlasExpansionOnSliceRequest) return;
    if (HasReachedWarmAddressCap()) return;
    var maxSiblings = Mathf.Max(SpriteStreamingRuntimeSettings.AtlasExpansionMaxSiblingAddresses, 1);
    if (IsWarmGateRunning || SpriteStreamingLoadingState.IsLoadingOverlayActive) {
      maxSiblings = Mathf.Min(maxSiblings, 48);
    }
    ExpandAtlasSeedSet(playerCriticalAtlasSeedAddresses, markHighPriority: false, maxSiblings);
    if (HasReachedWarmAddressCap()) return;
    ExpandAtlasSeedSet(playerWarmAtlasSeedAddresses, markHighPriority: false, maxSiblings);
  }

  void ExpandAtlasSeedSet(HashSet<string> seedSet, bool markHighPriority, int maxSiblings) {
    if (seedSet == null || seedSet.Count <= 0) return;
    foreach (var seedAddress in seedSet) {
      if (HasReachedWarmAddressCap()) return;
      if (string.IsNullOrWhiteSpace(seedAddress)) continue;
      atlasSiblingScratch.Clear();
      if (!SpriteRuntimeResolver.TryCollectAtlasSiblingAddresses(seedAddress, atlasSiblingScratch, maxSiblings)) continue;
      for (var i = 0; i < atlasSiblingScratch.Count; i++) {
        if (HasReachedWarmAddressCap()) return;
        AddWarmAddress(atlasSiblingScratch[i], markHighPriority);
      }
    }
    atlasSiblingScratch.Clear();
  }

  void AddLibrariesFromGameObjects(GameObject[] objects) {
    if (objects == null || objects.Length == 0) return;
    for (var i = 0; i < objects.Length; i++) {
      var go = objects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (target == null) continue;
      AddLibrary(target.libraryName);
    }
  }

  void AddLibraries(List<string> libraries) {
    if (libraries == null || libraries.Count <= 0) return;
    for (var i = 0; i < libraries.Count; i++) {
      AddLibrary(libraries[i]);
    }
  }

  void AddDirectAddresses(List<string> addresses, bool markCritical, bool markHighPriority) {
    if (addresses == null || addresses.Count <= 0) return;
    for (var i = 0; i < addresses.Count; i++) {
      AddReadyAddress(addresses[i], markCritical, markHighPriority);
    }
  }

  void AddLibrary(string libraryName) {
    var normalized = NormalizeToken(libraryName);
    if (string.IsNullOrWhiteSpace(normalized)) return;
    warmLibrarySet.Add(normalized);
  }

  static bool IsTargetWarmable(SpriteWithNormals target, bool allowInactive = false) {
    if (target == null) return false;
    if (!allowInactive && !target.isActiveAndEnabled) return false;
    if (allowInactive && !target.enabled) return false;
    if (target.DoNotRender) return false;
    return true;
  }

  static string ResolveAnimationCategory(string animationName, AnimData anim) {
    if (anim == null) return animationName ?? "";
    if (anim.To == 1) return "To";
    if (anim.To == 2) return "To2";
    return animationName ?? "";
  }

  static Dictionary<string, EffectData> ResolveEnemyEffectAnimations(string enemyType) {
    if (string.IsNullOrWhiteSpace(enemyType)) return null;
    if (string.Equals(enemyType, "Imp", StringComparison.OrdinalIgnoreCase)) return Effects.Imp;
    return null;
  }

  static string NormalizeToken(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    return value.Trim();
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
      hardTimeoutSeconds: hardTimeoutSeconds,
      allowHardTimeoutBypass: request.allowHardTimeoutBypass,
      idempotencyToken: request.idempotencyToken,
      skipIfTokenAlreadyWarm: request.skipIfTokenAlreadyWarm
    );
  }

  void ClearScratch() {
    warmLibrarySet.Clear();
    warmAddressSet.Clear();
    highPriorityAddressSet.Clear();
    readyAddressSet.Clear();
    criticalReadyAddressSet.Clear();
    playerCriticalAtlasSeedAddresses.Clear();
    playerWarmAtlasSeedAddresses.Clear();
    atlasSiblingScratch.Clear();
    warmAddressBatch.Clear();
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

  static bool ShouldLogLoadingDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }
}
