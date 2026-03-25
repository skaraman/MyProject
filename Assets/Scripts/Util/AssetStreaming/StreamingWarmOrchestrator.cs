using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public enum WarmContext {
  StartGame = 0,
  LoadSave = 1,
  GearApplyReturn = 2,
  EnemyWaveSpawn = 3
}

public readonly struct WarmRequest {
  public readonly WarmContext context;
  public readonly GearController playerController;
  public readonly EnemyController[] criticalEnemyControllers;
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
  public readonly List<string> extraCriticalAssetAddresses;
  public readonly List<string> extraWarmAssetAddresses;
  public readonly float hardTimeoutSeconds;
  public readonly bool allowHardTimeoutBypass;
  public readonly string idempotencyToken;
  public readonly bool skipIfTokenAlreadyWarm;
  public readonly List<string> extraCriticalLabels;
  public readonly List<string> extraWarmLabels;
  public readonly List<string> extraCriticalAssetLabels;
  public readonly List<string> extraWarmAssetLabels;
  public readonly List<string> criticalPlayerEffectKeys;
  public readonly bool allowCriticalReadySoftTimeout;

  public WarmRequest(
    WarmContext context,
    GearController playerController = null,
    EnemyController[] criticalEnemyControllers = null,
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
    List<string> extraCriticalAssetAddresses = null,
    List<string> extraWarmAssetAddresses = null,
    float hardTimeoutSeconds = 6.0f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = false,
    List<string> extraCriticalLabels = null,
    List<string> extraWarmLabels = null,
    List<string> extraCriticalAssetLabels = null,
    List<string> extraWarmAssetLabels = null,
    List<string> criticalPlayerEffectKeys = null,
    bool allowCriticalReadySoftTimeout = true
  ) {
    this.context = context;
    this.playerController = playerController;
    this.criticalEnemyControllers = criticalEnemyControllers;
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
    this.extraCriticalAssetAddresses = extraCriticalAssetAddresses;
    this.extraWarmAssetAddresses = extraWarmAssetAddresses;
    this.hardTimeoutSeconds = hardTimeoutSeconds;
    this.allowHardTimeoutBypass = allowHardTimeoutBypass;
    this.idempotencyToken = idempotencyToken;
    this.skipIfTokenAlreadyWarm = skipIfTokenAlreadyWarm;
    this.extraCriticalLabels = extraCriticalLabels;
    this.extraWarmLabels = extraWarmLabels;
    this.extraCriticalAssetLabels = extraCriticalAssetLabels;
    this.extraWarmAssetLabels = extraWarmAssetLabels;
    this.criticalPlayerEffectKeys = criticalPlayerEffectKeys;
    this.allowCriticalReadySoftTimeout = allowCriticalReadySoftTimeout;
  }

  public static WarmRequest CreateStartGame(
    GearController playerController,
    EnemyController[] criticalEnemyControllers = null,
    EnemyController[] enemyControllers = null,
    Dictionary<string, GameObject> enemyArchetypePrefabsByType = null,
    float timeoutSeconds = 3.0f,
    float requiredReadyRatio = 0.95f,
    List<string> extraCriticalLibraries = null,
    List<string> extraCriticalAddresses = null,
    List<string> extraWarmLibraries = null,
    List<string> extraWarmAddresses = null,
    List<string> extraCriticalAssetAddresses = null,
    List<string> extraWarmAssetAddresses = null,
    float hardTimeoutSeconds = 6.0f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = false,
    List<string> extraCriticalLabels = null,
    List<string> extraWarmLabels = null,
    List<string> extraCriticalAssetLabels = null,
    List<string> extraWarmAssetLabels = null,
    List<string> criticalPlayerEffectKeys = null,
    bool allowCriticalReadySoftTimeout = false
  ) {
    return new WarmRequest(
      context: WarmContext.StartGame,
      playerController: playerController,
      criticalEnemyControllers: criticalEnemyControllers,
      enemyControllers: enemyControllers,
      enemyArchetypePrefabsByType: enemyArchetypePrefabsByType,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: requiredReadyRatio,
      playerWarmFrames: 1,
      enemyWarmFrames: 1,
      effectWarmFrames: 1,
      maxRequestedAddresses: 262144,
      includeEffects: true,
      extraCriticalLibraries: extraCriticalLibraries,
      extraCriticalAddresses: extraCriticalAddresses,
      extraWarmLibraries: extraWarmLibraries,
      extraWarmAddresses: extraWarmAddresses,
      extraCriticalAssetAddresses: extraCriticalAssetAddresses,
      extraWarmAssetAddresses: extraWarmAssetAddresses,
      hardTimeoutSeconds: hardTimeoutSeconds,
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: idempotencyToken,
      skipIfTokenAlreadyWarm: skipIfTokenAlreadyWarm,
      extraCriticalLabels: extraCriticalLabels,
      extraWarmLabels: extraWarmLabels,
      extraCriticalAssetLabels: extraCriticalAssetLabels,
      extraWarmAssetLabels: extraWarmAssetLabels,
      criticalPlayerEffectKeys: criticalPlayerEffectKeys,
      allowCriticalReadySoftTimeout: allowCriticalReadySoftTimeout
    );
  }

  public static WarmRequest CreateLoadSave(
    GearController playerController,
    EnemyController[] criticalEnemyControllers = null,
    EnemyController[] enemyControllers = null,
    Dictionary<string, GameObject> enemyArchetypePrefabsByType = null,
    float timeoutSeconds = 3.5f,
    float requiredReadyRatio = 0.95f,
    List<string> extraCriticalLibraries = null,
    List<string> extraCriticalAddresses = null,
    List<string> extraWarmLibraries = null,
    List<string> extraWarmAddresses = null,
    List<string> extraCriticalAssetAddresses = null,
    List<string> extraWarmAssetAddresses = null,
    float hardTimeoutSeconds = 6.5f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = false,
    List<string> extraCriticalLabels = null,
    List<string> extraWarmLabels = null,
    List<string> extraCriticalAssetLabels = null,
    List<string> extraWarmAssetLabels = null,
    List<string> criticalPlayerEffectKeys = null,
    bool allowCriticalReadySoftTimeout = false
  ) {
    return new WarmRequest(
      context: WarmContext.LoadSave,
      playerController: playerController,
      criticalEnemyControllers: criticalEnemyControllers,
      enemyControllers: enemyControllers,
      enemyArchetypePrefabsByType: enemyArchetypePrefabsByType,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: requiredReadyRatio,
      playerWarmFrames: 1,
      enemyWarmFrames: 1,
      effectWarmFrames: 1,
      maxRequestedAddresses: 262144,
      includeEffects: true,
      extraCriticalLibraries: extraCriticalLibraries,
      extraCriticalAddresses: extraCriticalAddresses,
      extraWarmLibraries: extraWarmLibraries,
      extraWarmAddresses: extraWarmAddresses,
      extraCriticalAssetAddresses: extraCriticalAssetAddresses,
      extraWarmAssetAddresses: extraWarmAssetAddresses,
      hardTimeoutSeconds: hardTimeoutSeconds,
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: idempotencyToken,
      skipIfTokenAlreadyWarm: skipIfTokenAlreadyWarm,
      extraCriticalLabels: extraCriticalLabels,
      extraWarmLabels: extraWarmLabels,
      extraCriticalAssetLabels: extraCriticalAssetLabels,
      extraWarmAssetLabels: extraWarmAssetLabels,
      criticalPlayerEffectKeys: criticalPlayerEffectKeys,
      allowCriticalReadySoftTimeout: allowCriticalReadySoftTimeout
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
    List<string> extraCriticalAssetAddresses = null,
    List<string> extraWarmAssetAddresses = null,
    float hardTimeoutSeconds = 4.5f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = false,
    List<string> extraCriticalLabels = null,
    List<string> extraWarmLabels = null,
    List<string> extraCriticalAssetLabels = null,
    List<string> extraWarmAssetLabels = null,
    List<string> criticalPlayerEffectKeys = null,
    bool allowCriticalReadySoftTimeout = false
  ) {
    return new WarmRequest(
      context: WarmContext.GearApplyReturn,
      playerController: playerController,
      criticalEnemyControllers: null,
      enemyControllers: null,
      enemyArchetypePrefabsByType: null,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: requiredReadyRatio,
      playerWarmFrames: 1,
      enemyWarmFrames: 0,
      effectWarmFrames: 1,
      maxRequestedAddresses: 131072,
      includeEffects: true,
      extraCriticalLibraries: extraCriticalLibraries,
      extraCriticalAddresses: extraCriticalAddresses,
      extraWarmLibraries: extraWarmLibraries,
      extraWarmAddresses: extraWarmAddresses,
      extraCriticalAssetAddresses: extraCriticalAssetAddresses,
      extraWarmAssetAddresses: extraWarmAssetAddresses,
      hardTimeoutSeconds: hardTimeoutSeconds,
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: idempotencyToken,
      skipIfTokenAlreadyWarm: skipIfTokenAlreadyWarm,
      extraCriticalLabels: extraCriticalLabels,
      extraWarmLabels: extraWarmLabels,
      extraCriticalAssetLabels: extraCriticalAssetLabels,
      extraWarmAssetLabels: extraWarmAssetLabels,
      criticalPlayerEffectKeys: criticalPlayerEffectKeys,
      allowCriticalReadySoftTimeout: allowCriticalReadySoftTimeout
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
    List<string> extraCriticalAssetAddresses = null,
    List<string> extraWarmAssetAddresses = null,
    float hardTimeoutSeconds = 4.5f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = true,
    List<string> extraCriticalLabels = null,
    List<string> extraWarmLabels = null,
    List<string> extraCriticalAssetLabels = null,
    List<string> extraWarmAssetLabels = null,
    List<string> criticalPlayerEffectKeys = null,
    bool allowCriticalReadySoftTimeout = true
  ) {
    return new WarmRequest(
      context: WarmContext.EnemyWaveSpawn,
      playerController: null,
      criticalEnemyControllers: null,
      enemyControllers: null,
      enemyArchetypePrefabsByType: enemyArchetypePrefabsByType,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: requiredReadyRatio,
      playerWarmFrames: 0,
      enemyWarmFrames: enemyWarmFrames,
      effectWarmFrames: 2,
      maxRequestedAddresses: 262144,
      includeEffects: true,
      extraCriticalLibraries: extraCriticalLibraries,
      extraCriticalAddresses: extraCriticalAddresses,
      extraWarmLibraries: extraWarmLibraries,
      extraWarmAddresses: extraWarmAddresses,
      extraCriticalAssetAddresses: extraCriticalAssetAddresses,
      extraWarmAssetAddresses: extraWarmAssetAddresses,
      hardTimeoutSeconds: hardTimeoutSeconds,
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: idempotencyToken,
      skipIfTokenAlreadyWarm: skipIfTokenAlreadyWarm,
      extraCriticalLabels: extraCriticalLabels,
      extraWarmLabels: extraWarmLabels,
      extraCriticalAssetLabels: extraCriticalAssetLabels,
      extraWarmAssetLabels: extraWarmAssetLabels,
      criticalPlayerEffectKeys: criticalPlayerEffectKeys,
      allowCriticalReadySoftTimeout: allowCriticalReadySoftTimeout
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
  readonly HashSet<string> warmLabelSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> highPriorityAddressSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> highPriorityLabelSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> readyAddressSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> readyLabelSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> criticalReadyAddressSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> criticalReadyLabelSet = new(StringComparer.OrdinalIgnoreCase);
  readonly HashSet<string> playerCriticalAtlasSeedAddresses = new(StringComparer.OrdinalIgnoreCase);
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
  static readonly List<string> highPriorityAddressBatchBuffer = new();
  static readonly List<string> rescueAddressBuffer = new();
  static readonly HashSet<string> rescueSeenAddressBuffer = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<EnemyController> rescueEnemyControllerBuffer = new();
  static readonly List<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation> locationBuffer = new();
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

  enum WarmPlanSliceAction {
    Continue = 0,
    Yield = 1,
    Stop = 2
  }

  sealed class WarmPlanSliceBudget {
    readonly float maxSliceSeconds;
    readonly int maxWorkItems;
    float sliceStartedAt;
    int sliceWorkItems;

    public int YieldedFrames { get; private set; }
    public int TotalWorkItems { get; private set; }
    public float MaxObservedSliceSeconds { get; private set; }

    public WarmPlanSliceBudget(float maxSliceSeconds, int maxWorkItems) {
      this.maxSliceSeconds = Mathf.Max(maxSliceSeconds, 0.001f);
      this.maxWorkItems = Math.Max(maxWorkItems, 1);
      Reset();
    }

    public bool Consume() {
      TotalWorkItems++;
      sliceWorkItems++;
      return sliceWorkItems >= maxWorkItems || Time.realtimeSinceStartup - sliceStartedAt >= maxSliceSeconds;
    }

    public void RecordYield() {
      var elapsed = Mathf.Max(Time.realtimeSinceStartup - sliceStartedAt, 0f);
      if (elapsed > MaxObservedSliceSeconds) {
        MaxObservedSliceSeconds = elapsed;
      }
      YieldedFrames++;
    }

    public void Reset() {
      sliceStartedAt = Time.realtimeSinceStartup;
      sliceWorkItems = 0;
    }
  }

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

    yield return ExpandPlayerAtlasSeedsRoutine(context, hardTimeoutAt, debugLogs);

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
        " addresses=" + warmAddressSet.Count +
        " critical=" + criticalReadyAddressSet.Count +
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
    TextureResidencyCache.BeginSession(scheduledAddressSet.Count);

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
        var spriteReadyCount = CountReadyAddresses(scheduledReadyAddressSet, pumpEntries: false);
        var spriteCriticalReadyCount = CountReadyAddresses(scheduledCriticalReadyAddressSet, pumpEntries: false);
        var spriteTotalCount = scheduledReadyAddressSet.Count;
        var spriteCriticalTotalCount = scheduledCriticalReadyAddressSet.Count;
        readyCount = spriteReadyCount + runtimeSnapshot.readyCount;
        totalCount = spriteTotalCount + runtimeSnapshot.totalCount;
        criticalReadyCount = spriteCriticalReadyCount + runtimeSnapshot.criticalReadyCount;
        criticalTotalCount = spriteCriticalTotalCount + runtimeSnapshot.criticalTotalCount;
        ratio = totalCount > 0 ? ((float)readyCount / totalCount) : 1f;
        var spriteCriticalReady = spriteCriticalTotalCount <= 0 || spriteCriticalReadyCount >= spriteCriticalTotalCount;
        criticalReady = spriteCriticalReady && runtimeSnapshot.criticalReady;
        reachedThreshold = criticalReady && ratio >= requiredReadyRatio;
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
        hardTimedOut = true;
        break;
      }
      yield return null;
    }

    TextureResidencyCache.EndSession();

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
      requestedAddressCount: scheduledAddressSet.Count + GetRuntimeWarmSnapshot(activeRuntimeTrackedWarmId).totalCount,
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

  IEnumerator BuildWarmPlanRoutine(
    WarmRequest request,
    bool includeResolvedAddressSweeps,
    bool includeStaticSeedWork,
    float deadlineAt,
    bool debugLogs
  ) {
    var budget = new WarmPlanSliceBudget(WarmPlanSliceBudgetSeconds, WarmPlanSliceWorkItemBudget);

    if (includeStaticSeedWork) {
      AddLibraries(request.extraCriticalLibraries);
      AddLibraries(request.extraWarmLibraries);
      CollectLabels(request.extraCriticalLabels, markCritical: true);
      CollectLabels(request.extraWarmLabels, markCritical: false);
      CollectLabels(SpriteStreamingRuntimeSettings.CriticalAddressableLabels, markCritical: true);
      CollectLabels(SpriteStreamingRuntimeSettings.WarmAddressableLabels, markCritical: false);
      CollectLabels(SpriteStreamingRuntimeSettings.WarmUiAddressableLabels, markCritical: false);
      AddDirectAddresses(request.extraCriticalAddresses, markCritical: true, markHighPriority: true);
      AddDirectAddresses(request.extraWarmAddresses, markCritical: false, markHighPriority: false);
      if (ShouldAbortWarmPlanPass(request, includeResolvedAddressSweeps, includeStaticSeedWork, budget, deadlineAt, debugLogs)) yield break;
    }

    if (request.playerController != null) {
      yield return CollectPlayerWarmPlan(
        request.playerController,
        request.playerWarmFrames,
        request.effectWarmFrames,
        request.includeEffects,
        includeResolvedAddressSweeps,
        includeStaticSeedWork,
        request.criticalPlayerEffectKeys,
        budget,
        deadlineAt
      );
      if (ShouldAbortWarmPlanPass(request, includeResolvedAddressSweeps, includeStaticSeedWork, budget, deadlineAt, debugLogs)) yield break;
    }

    if (request.criticalEnemyControllers != null && request.criticalEnemyControllers.Length > 0) {
      for (var i = 0; i < request.criticalEnemyControllers.Length; i++) {
        yield return CollectEnemyControllerWarmPlan(
          request.criticalEnemyControllers[i],
          request.enemyWarmFrames,
          request.effectWarmFrames,
          request.includeEffects,
          includeResolvedAddressSweeps,
          includeStaticSeedWork,
          markCritical: true,
          budget: budget,
          deadlineAt: deadlineAt
        );
        if (ShouldAbortWarmPlanPass(request, includeResolvedAddressSweeps, includeStaticSeedWork, budget, deadlineAt, debugLogs)) yield break;
      }
    }

    if (request.enemyControllers != null && request.enemyControllers.Length > 0) {
      for (var i = 0; i < request.enemyControllers.Length; i++) {
        yield return CollectEnemyControllerWarmPlan(
          request.enemyControllers[i],
          request.enemyWarmFrames,
          request.effectWarmFrames,
          request.includeEffects,
          includeResolvedAddressSweeps,
          includeStaticSeedWork,
          markCritical: false,
          budget: budget,
          deadlineAt: deadlineAt
        );
        if (ShouldAbortWarmPlanPass(request, includeResolvedAddressSweeps, includeStaticSeedWork, budget, deadlineAt, debugLogs)) yield break;
      }
    }

    if (request.enemyArchetypePrefabsByType != null && request.enemyArchetypePrefabsByType.Count > 0) {
      yield return CollectEnemyArchetypeWarmPlan(
        request.enemyArchetypePrefabsByType,
        request.enemyWarmFrames,
        request.effectWarmFrames,
        request.includeEffects,
        includeResolvedAddressSweeps,
        includeStaticSeedWork,
        budget,
        deadlineAt
      );
      if (ShouldAbortWarmPlanPass(request, includeResolvedAddressSweeps, includeStaticSeedWork, budget, deadlineAt, debugLogs)) yield break;
    }

    LogWarmPlanPassSummary(request, includeResolvedAddressSweeps, includeStaticSeedWork, budget, deadlineHit: false, debugLogs: debugLogs);
  }

  IEnumerator CollectPlayerWarmPlan(
    GearController controller,
    int warmFrames,
    int effectWarmFrames,
    bool includeEffects,
    bool includeResolvedAddressSweeps,
    bool includeStaticSeedWork,
    List<string> criticalPlayerEffectKeys,
    WarmPlanSliceBudget budget,
    float deadlineAt
  ) {
    if (controller == null || HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    var playerAnimationManifest = Animations.Esperanza;

    if (includeStaticSeedWork) {
      yield return AddLibrariesFromGameObjects(controller.SkinObjects, budget, deadlineAt);
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      yield return AddLibrariesFromGameObjects(controller.GearObjects, budget, deadlineAt);
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      if (includeEffects && controller.effectNode != null) {
        AddLibrary(controller.effectNode.libraryName);
      }
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    }

    if (!includeResolvedAddressSweeps) yield break;

    yield return CollectAtlasSeedAddressesForObjects(controller.SkinObjects, playerAnimationManifest, warmFrames, playerCriticalAtlasSeedAddresses, budget, deadlineAt);
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    yield return CollectAtlasSeedAddressesForObjects(controller.GearObjects, playerAnimationManifest, warmFrames, playerWarmAtlasSeedAddresses, budget, deadlineAt);
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    yield return CollectAnimationStartsForObjects(controller.SkinObjects, playerAnimationManifest, warmFrames, markCritical: true, budget: budget, deadlineAt: deadlineAt);
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    yield return CollectAnimationStartsForObjects(controller.GearObjects, playerAnimationManifest, warmFrames, markCritical: false, budget: budget, deadlineAt: deadlineAt);
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    if (!includeEffects || controller.effectNode == null) yield break;

    var criticalEffectKeySet = BuildNormalizedTokenSet(criticalPlayerEffectKeys);
    if (criticalEffectKeySet != null && criticalEffectKeySet.Count > 0) {
      yield return CollectEffectStartsForTarget(
        controller.effectNode,
        Effects.Esperanza,
        effectWarmFrames,
        markCritical: true,
        budget: budget,
        deadlineAt: deadlineAt,
        allowInactive: false,
        includedEffectKeys: criticalEffectKeySet
      );
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      yield return CollectEffectStartsForTarget(
        controller.effectNode,
        Effects.Things,
        effectWarmFrames,
        markCritical: true,
        budget: budget,
        deadlineAt: deadlineAt,
        allowInactive: false,
        includedEffectKeys: criticalEffectKeySet
      );
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      yield return CollectEffectStartsForTarget(
        controller.effectNode,
        Effects.Imp,
        effectWarmFrames,
        markCritical: true,
        budget: budget,
        deadlineAt: deadlineAt,
        allowInactive: false,
        includedEffectKeys: criticalEffectKeySet
      );
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    }

    yield return CollectEffectStartsForTarget(
      controller.effectNode,
      Effects.Esperanza,
      effectWarmFrames,
      markCritical: false,
      budget: budget,
      deadlineAt: deadlineAt,
      allowInactive: false,
      excludedEffectKeys: criticalEffectKeySet
    );
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    yield return CollectEffectStartsForTarget(
      controller.effectNode,
      Effects.Things,
      effectWarmFrames,
      markCritical: false,
      budget: budget,
      deadlineAt: deadlineAt,
      allowInactive: false,
      excludedEffectKeys: criticalEffectKeySet
    );
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    yield return CollectEffectStartsForTarget(
      controller.effectNode,
      Effects.Imp,
      effectWarmFrames,
      markCritical: false,
      budget: budget,
      deadlineAt: deadlineAt,
      allowInactive: false,
      excludedEffectKeys: criticalEffectKeySet
    );
  }

  IEnumerator CollectEnemyControllerWarmPlan(
    EnemyController controller,
    int warmFrames,
    int effectWarmFrames,
    bool includeEffects,
    bool includeResolvedAddressSweeps,
    bool includeStaticSeedWork,
    bool markCritical,
    WarmPlanSliceBudget budget,
    float deadlineAt
  ) {
    if (controller == null || controller.spriteObjects == null || controller.spriteObjects.Length == 0) yield break;
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    var enemyType = NormalizeToken(controller.enemyType);
    if (string.IsNullOrWhiteSpace(enemyType)) yield break;
    if (!Animations.Enemies.TryGetValue(enemyType, out var enemyAnimations) || enemyAnimations == null || enemyAnimations.Count == 0) yield break;

    if (includeStaticSeedWork) {
      yield return AddLibrariesFromGameObjects(controller.spriteObjects, budget, deadlineAt);
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      if (includeEffects && controller.effectNode != null) {
        AddLibrary(controller.effectNode.libraryName);
      }
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    }

    if (!includeResolvedAddressSweeps) yield break;

    yield return CollectAnimationStartsForObjects(controller.spriteObjects, enemyAnimations, warmFrames, markCritical, budget, deadlineAt);
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    if (includeEffects && controller.effectNode != null) {
      yield return CollectEffectStartsForTarget(
        controller.effectNode,
        ResolveEnemyEffectAnimations(enemyType),
        effectWarmFrames,
        markCritical,
        budget,
        deadlineAt
      );
    }
  }

  IEnumerator CollectEnemyArchetypeWarmPlan(
    Dictionary<string, GameObject> enemyArchetypePrefabsByType,
    int warmFrames,
    int effectWarmFrames,
    bool includeEffects,
    bool includeResolvedAddressSweeps,
    bool includeStaticSeedWork,
    WarmPlanSliceBudget budget,
    float deadlineAt
  ) {
    if (enemyArchetypePrefabsByType == null || enemyArchetypePrefabsByType.Count == 0) yield break;
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    foreach (var pair in enemyArchetypePrefabsByType) {
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      var enemyType = NormalizeToken(pair.Key);
      var root = pair.Value;
      if (string.IsNullOrWhiteSpace(enemyType) || root == null) continue;
      if (!Animations.Enemies.TryGetValue(enemyType, out var enemyAnimations) || enemyAnimations == null || enemyAnimations.Count == 0) continue;

      var targets = GetCachedArchetypeTargets(root);
      var sliceAction = NoteWarmPlanWork(budget, deadlineAt);
      if (sliceAction == WarmPlanSliceAction.Stop) yield break;
      if (sliceAction == WarmPlanSliceAction.Yield) {
        budget.RecordYield();
        TextureResidencyCache.Pump();
        yield return null;
        budget.Reset();
      }

      for (var i = 0; i < targets.Length; i++) {
        if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
        var target = targets[i];
        if (!IsTargetWarmable(target, allowInactive: true)) continue;
        if (includeStaticSeedWork) {
          AddLibrary(target.libraryName);
        }
        if (!includeResolvedAddressSweeps) {
          sliceAction = NoteWarmPlanWork(budget, deadlineAt);
          if (sliceAction == WarmPlanSliceAction.Stop) yield break;
          if (sliceAction == WarmPlanSliceAction.Yield) {
            budget.RecordYield();
            TextureResidencyCache.Pump();
            yield return null;
            budget.Reset();
          }
          continue;
        }
        yield return CollectAnimationStartsForTarget(target, enemyAnimations, warmFrames, markCritical: false, budget: budget, deadlineAt: deadlineAt, allowInactive: true);
      }

      if (!includeEffects || !includeResolvedAddressSweeps) continue;
      var effectAnimations = ResolveEnemyEffectAnimations(enemyType);
      if (effectAnimations == null || effectAnimations.Count == 0) continue;
      for (var i = 0; i < targets.Length; i++) {
        if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
        var target = targets[i];
        if (!IsTargetWarmable(target, allowInactive: true)) continue;
        yield return CollectEffectStartsForTarget(target, effectAnimations, effectWarmFrames, markCritical: false, budget: budget, deadlineAt: deadlineAt, allowInactive: true);
      }
    }
  }

  IEnumerator CollectAnimationStartsForObjects(
    GameObject[] objects,
    Dictionary<string, AnimData> animations,
    int warmFrames,
    bool markCritical,
    WarmPlanSliceBudget budget,
    float deadlineAt
  ) {
    if (objects == null || objects.Length == 0) yield break;
    if (animations == null || animations.Count == 0) yield break;
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    var clampedWarmFrames = Mathf.Max(warmFrames, 1);
    for (var i = 0; i < objects.Length; i++) {
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      var go = objects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (!IsTargetWarmable(target)) continue;
      yield return CollectAnimationStartsForTarget(target, animations, clampedWarmFrames, markCritical, budget, deadlineAt);
    }
  }

  IEnumerator CollectAnimationStartsForTarget(
    SpriteWithNormals target,
    Dictionary<string, AnimData> animations,
    int warmFrames,
    bool markCritical,
    WarmPlanSliceBudget budget,
    float deadlineAt,
    bool allowInactive = false
  ) {
    if (!IsTargetWarmable(target, allowInactive)) yield break;
    if (animations == null || animations.Count == 0) yield break;

    if (!target.IsAnimation) {
      if (TryGetFrameAddressPairBudgeted(target, 0, out var staticPair, categoryOverride: null)) {
        AddPairAddresses(staticPair, markCritical);
      }
      yield break;
    }

    foreach (var pair in animations) {
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      var animationName = pair.Key;
      var anim = pair.Value;
      if (anim == null || string.IsNullOrWhiteSpace(animationName)) continue;

      var category = ResolveAnimationCategory(animationName, anim);
      var clipStart = Mathf.Max(anim.start, 1);
      var clipEnd = Mathf.Max(anim.end, clipStart);
      var frameEnd = Mathf.Min(clipEnd, clipStart + Mathf.Max(warmFrames, 1) - 1);
      for (var frame = clipStart; frame <= frameEnd; frame++) {
        if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
        if (TryGetFrameAddressPairBudgeted(target, frame, out var addressPair, category)) {
          AddPairAddresses(addressPair, markCritical);
        }

        var sliceAction = NoteWarmPlanWork(budget, deadlineAt);
        if (sliceAction == WarmPlanSliceAction.Stop) yield break;
        if (sliceAction == WarmPlanSliceAction.Yield) {
          budget.RecordYield();
          TextureResidencyCache.Pump();
          yield return null;
          budget.Reset();
        }
      }
    }
  }

  IEnumerator CollectEffectStartsForTarget(
    SpriteWithNormals target,
    Dictionary<string, EffectData> effects,
    int warmFrames,
    bool markCritical,
    WarmPlanSliceBudget budget,
    float deadlineAt,
    bool allowInactive = false,
    ISet<string> includedEffectKeys = null,
    ISet<string> excludedEffectKeys = null
  ) {
    if (!IsTargetWarmable(target, allowInactive)) yield break;
    if (effects == null || effects.Count == 0) yield break;

    foreach (var pair in effects) {
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      var effectName = pair.Key;
      var effect = pair.Value;
      if (effect == null || string.IsNullOrWhiteSpace(effectName)) continue;
      var normalizedEffectName = NormalizeToken(effectName);
      if (includedEffectKeys != null &&
          includedEffectKeys.Count > 0 &&
          !includedEffectKeys.Contains(normalizedEffectName)) {
        continue;
      }
      if (excludedEffectKeys != null && excludedEffectKeys.Contains(normalizedEffectName)) {
        continue;
      }
      var clipStart = Mathf.Max(effect.start, 1);
      var clipEnd = Mathf.Max(effect.end, clipStart);
      var frameEnd = Mathf.Min(clipEnd, clipStart + Mathf.Max(warmFrames, 1) - 1);
      for (var frame = clipStart; frame <= frameEnd; frame++) {
        if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
        if (TryGetFrameAddressPairBudgeted(target, frame, out var addressPair, effectName)) {
          AddPairAddresses(addressPair, markCritical);
        }

        var sliceAction = NoteWarmPlanWork(budget, deadlineAt);
        if (sliceAction == WarmPlanSliceAction.Stop) yield break;
        if (sliceAction == WarmPlanSliceAction.Yield) {
          budget.RecordYield();
          TextureResidencyCache.Pump();
          yield return null;
          budget.Reset();
        }
      }
    }
  }

  ISet<string> BuildNormalizedTokenSet(List<string> values) {
    if (values == null || values.Count <= 0) return null;
    var set = normalizedTokenSetScratch;
    set.Clear();
    for (var i = 0; i < values.Count; i++) {
      var normalized = NormalizeToken(values[i]);
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      set.Add(normalized);
    }
    return set.Count > 0 ? set : null;
  }

  bool TryGetFrameAddressPairBudgeted(SpriteWithNormals target, int frame, out SpriteAddressPair pair, string categoryOverride = null) {
    pair = default;
    if (target == null) return false;
    if (HasReachedWarmAddressCap()) return false;
    if (warmPlanFrameAddressProbeCount >= activeFrameAddressProbeBudget) {
      return false;
    }
    warmPlanFrameAddressProbeCount++;
    return target.TryGetFrameAddressPair(frame, out pair, categoryOverride);
  }

  void AddPairAddresses(SpriteAddressPair pair, bool markCritical) {
    if (!AddReadyAddress(pair.RuntimeColorAddress, markCritical, markCritical)) return;
    AddWarmAddress(pair.RuntimeNormalAddress, markHighPriority: false);
  }

  bool AddReadyAddress(string address, bool markCritical, bool markHighPriority) {
    // Warm gate runs as a single-tier preload queue: no per-source priority classes.
    if (!AddWarmAddress(address, markHighPriority: false)) return false;
    var normalized = NormalizeToken(address);
    if (string.IsNullOrWhiteSpace(normalized)) return false;
    readyAddressSet.Add(normalized);
    if (markCritical &&
        !criticalReadyAddressSet.Contains(normalized) &&
        activeCriticalReadyAddressCap > 0 &&
        criticalReadyAddressSet.Count >= activeCriticalReadyAddressCap) {
      markCritical = false;
      warmPlanDroppedCriticalReadyAddresses++;
    }
    if (markCritical) criticalReadyAddressSet.Add(normalized);
    return true;
  }

  bool AddWarmAddress(string address, bool markHighPriority) {
    var normalized = NormalizeToken(address);
    if (string.IsNullOrWhiteSpace(normalized)) return false;
    if (warmAddressSet.Contains(normalized)) {
      return true;
    }
    if (HasReachedWarmAddressCap()) {
      warmPlanDroppedAddresses++;
      return false;
    }
    warmAddressSet.Add(normalized);
    return true;
  }

  bool HasReachedWarmAddressCap() {
    if (activeMaxRequestedAddresses <= 0) return false;
    if (warmAddressSet.Count < activeMaxRequestedAddresses) return false;
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

  IEnumerator CollectAtlasSeedAddressesForObjects(
    GameObject[] objects,
    Dictionary<string, AnimData> animations,
    int warmFrames,
    HashSet<string> seedSet,
    WarmPlanSliceBudget budget,
    float deadlineAt
  ) {
    if (objects == null || objects.Length == 0) yield break;
    if (animations == null || animations.Count == 0) yield break;
    if (seedSet == null) yield break;
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    var clampedWarmFrames = Mathf.Max(warmFrames, 1);
    for (var i = 0; i < objects.Length; i++) {
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      var go = objects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (!IsTargetWarmable(target)) continue;
      yield return CollectAtlasSeedAddressesForTarget(target, animations, clampedWarmFrames, seedSet, budget, deadlineAt);
    }
  }

  IEnumerator CollectAtlasSeedAddressesForTarget(
    SpriteWithNormals target,
    Dictionary<string, AnimData> animations,
    int warmFrames,
    HashSet<string> seedSet,
    WarmPlanSliceBudget budget,
    float deadlineAt
  ) {
    if (!IsTargetWarmable(target)) yield break;
    if (animations == null || animations.Count == 0 || seedSet == null) yield break;
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    var requestedSamples = Mathf.Clamp(Mathf.Max(warmFrames, 1), 1, 3);

    if (!target.IsAnimation) {
      if (TryGetFrameAddressPairBudgeted(target, 0, out var staticPair, categoryOverride: null)) {
        AddAtlasSeedAddress(staticPair.RuntimeColorAddress, seedSet);
        AddAtlasSeedAddress(staticPair.RuntimeNormalAddress, seedSet);
      }
      yield break;
    }

    foreach (var pair in animations) {
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
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
        if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
        var frame = sampleCount <= 1
          ? clipStart
          : Mathf.RoundToInt(Mathf.Lerp(clipStart, clipEnd, sampleIndex / (float)sampleDenominator));
        frame = Mathf.Clamp(frame, clipStart, clipEnd);
        if (frame == lastFrame) continue;
        lastFrame = frame;

        if (TryGetFrameAddressPairBudgeted(target, frame, out var addressPair, category)) {
          AddAtlasSeedAddress(addressPair.RuntimeColorAddress, seedSet);
          AddAtlasSeedAddress(addressPair.RuntimeNormalAddress, seedSet);
        }

        var sliceAction = NoteWarmPlanWork(budget, deadlineAt);
        if (sliceAction == WarmPlanSliceAction.Stop) yield break;
        if (sliceAction == WarmPlanSliceAction.Yield) {
          budget.RecordYield();
          TextureResidencyCache.Pump();
          yield return null;
          budget.Reset();
        }
      }
    }
  }

  static void AddAtlasSeedAddress(string address, HashSet<string> seedSet) {
    if (seedSet == null || string.IsNullOrWhiteSpace(address)) return;
    seedSet.Add(address.Trim());
  }

  IEnumerator ExpandPlayerAtlasSeedsRoutine(WarmContext context, float deadlineAt, bool debugLogs) {
    if (!SpriteStreamingRuntimeSettings.EnableAtlasExpansionOnSliceRequest) yield break;
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    var budget = new WarmPlanSliceBudget(WarmPlanSliceBudgetSeconds, WarmPlanSliceWorkItemBudget);
    yield return ExpandAtlasSeedSet(playerCriticalAtlasSeedAddresses, markHighPriority: false, budget: budget, deadlineAt: deadlineAt);
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) {
      LogAtlasSeedExpansionSummary(context, budget, deadlineHit: HasWarmPlanDeadlineElapsed(deadlineAt), debugLogs: debugLogs);
      yield break;
    }
    yield return ExpandAtlasSeedSet(playerWarmAtlasSeedAddresses, markHighPriority: false, budget: budget, deadlineAt: deadlineAt);
    LogAtlasSeedExpansionSummary(context, budget, deadlineHit: HasWarmPlanDeadlineElapsed(deadlineAt), debugLogs: debugLogs);
  }

  IEnumerator ExpandAtlasSeedSet(HashSet<string> seedSet, bool markHighPriority, WarmPlanSliceBudget budget, float deadlineAt) {
    if (seedSet == null || seedSet.Count <= 0) yield break;
    foreach (var seedAddress in seedSet) {
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      if (string.IsNullOrWhiteSpace(seedAddress)) continue;
      AddWarmAddress(seedAddress, markHighPriority);

      var sliceAction = NoteWarmPlanWork(budget, deadlineAt);
      if (sliceAction == WarmPlanSliceAction.Stop) yield break;
      if (sliceAction == WarmPlanSliceAction.Yield) {
        budget.RecordYield();
        TextureResidencyCache.Pump();
        yield return null;
        budget.Reset();
      }
    }
  }

  IEnumerator AddLibrariesFromGameObjects(GameObject[] objects, WarmPlanSliceBudget budget, float deadlineAt) {
    if (objects == null || objects.Length == 0) yield break;
    for (var i = 0; i < objects.Length; i++) {
      if (HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      var go = objects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (target == null) continue;
      AddLibrary(target.libraryName);

      var sliceAction = NoteWarmPlanWork(budget, deadlineAt);
      if (sliceAction == WarmPlanSliceAction.Stop) yield break;
      if (sliceAction == WarmPlanSliceAction.Yield) {
        budget.RecordYield();
        TextureResidencyCache.Pump();
        yield return null;
        budget.Reset();
      }
    }
  }

  SpriteWithNormals[] GetCachedArchetypeTargets(GameObject root) {
    if (root == null) return Array.Empty<SpriteWithNormals>();
    if (archetypeTargetCache.TryGetValue(root, out var cachedTargets) && cachedTargets != null) {
      return cachedTargets;
    }
    var targets = root.GetComponentsInChildren<SpriteWithNormals>(true);
    archetypeTargetCache[root] = targets ?? Array.Empty<SpriteWithNormals>();
    return archetypeTargetCache[root];
  }

  bool ShouldAbortWarmPlanPass(
    WarmRequest request,
    bool includeResolvedAddressSweeps,
    bool includeStaticSeedWork,
    WarmPlanSliceBudget budget,
    float deadlineAt,
    bool debugLogs
  ) {
    var deadlineHit = HasWarmPlanDeadlineElapsed(deadlineAt);
    if (!deadlineHit && !HasReachedWarmAddressCap()) return false;
    LogWarmPlanPassSummary(request, includeResolvedAddressSweeps, includeStaticSeedWork, budget, deadlineHit, debugLogs);
    return true;
  }

  void LogWarmPlanPassSummary(
    WarmRequest request,
    bool includeResolvedAddressSweeps,
    bool includeStaticSeedWork,
    WarmPlanSliceBudget budget,
    bool deadlineHit,
    bool debugLogs
  ) {
    if (!debugLogs) return;
    if (!deadlineHit && (budget == null || budget.YieldedFrames <= 0)) return;
    Debug.Log(
      "[StreamingWarmOrchestrator] Warm plan pass." +
      " context=" + request.context +
      " static_seed=" + (includeStaticSeedWork ? 1 : 0) +
      " resolved_sweep=" + (includeResolvedAddressSweeps ? 1 : 0) +
      " yielded_frames=" + (budget != null ? budget.YieldedFrames : 0) +
      " work_items=" + (budget != null ? budget.TotalWorkItems : 0) +
      " max_slice_ms=" + ((budget != null ? budget.MaxObservedSliceSeconds : 0f) * 1000f).ToString("0.0") +
      " deadline_hit=" + (deadlineHit ? 1 : 0) +
      " libraries=" + warmLibrarySet.Count +
      " labels=" + warmLabelSet.Count +
      " addresses=" + warmAddressSet.Count +
      " critical=" + criticalReadyAddressSet.Count +
      " frame_probes=" + warmPlanFrameAddressProbeCount
    );
  }

  void LogAtlasSeedExpansionSummary(WarmContext context, WarmPlanSliceBudget budget, bool deadlineHit, bool debugLogs) {
    if (!debugLogs) return;
    if (!deadlineHit && (budget == null || budget.YieldedFrames <= 0)) return;
    Debug.Log(
      "[StreamingWarmOrchestrator] Atlas seed expansion." +
      " context=" + context +
      " yielded_frames=" + (budget != null ? budget.YieldedFrames : 0) +
      " work_items=" + (budget != null ? budget.TotalWorkItems : 0) +
      " max_slice_ms=" + ((budget != null ? budget.MaxObservedSliceSeconds : 0f) * 1000f).ToString("0.0") +
      " deadline_hit=" + (deadlineHit ? 1 : 0) +
      " seed_critical=" + playerCriticalAtlasSeedAddresses.Count +
      " seed_warm=" + playerWarmAtlasSeedAddresses.Count
    );
  }

  static bool HasWarmPlanDeadlineElapsed(float deadlineAt) {
    return deadlineAt > 0f && Time.realtimeSinceStartup >= deadlineAt;
  }

  static WarmPlanSliceAction NoteWarmPlanWork(WarmPlanSliceBudget budget, float deadlineAt) {
    if (HasWarmPlanDeadlineElapsed(deadlineAt)) return WarmPlanSliceAction.Stop;
    if (budget == null) return WarmPlanSliceAction.Continue;
    if (!budget.Consume()) return WarmPlanSliceAction.Continue;
    return WarmPlanSliceAction.Yield;
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

  void CollectLabels(IReadOnlyList<string> labels, bool markCritical) {
    if (labels == null || labels.Count <= 0) return;
    for (var i = 0; i < labels.Count; i++) {
      var normalized = NormalizeToken(labels[i]);
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      warmLabelSet.Add(normalized);
      if (markCritical) criticalReadyLabelSet.Add(normalized);
    }
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
        cap = Math.Min(cap, 384);
        break;
      case WarmContext.GearApplyReturn:
        cap = Math.Min(cap, 256);
        break;
      case WarmContext.EnemyWaveSpawn:
        cap = Math.Min(cap, 320);
        break;
      default:
        cap = Math.Min(cap, 512);
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

  void ClearScratch() {
    warmLibrarySet.Clear();
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
    playerCriticalAtlasSeedAddresses.Clear();
    playerWarmAtlasSeedAddresses.Clear();
    warmAddressBatch.Clear();
    normalizedTokenSetScratch.Clear();
    rescueDispatchBuffer.Clear();
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
      // Visible sprite subasset catalog entries are disabled during builds, so
      // label warmup must resolve the atlas asset locations directly.
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

  static bool ShouldLogLoadingDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }
}
