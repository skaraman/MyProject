using System.Text;
using UnityEngine;

public static class SpriteStreamingDiagnostics {
  const float SummaryIntervalSeconds = 5f;

  static long totalLoadStarts;
  static long cacheHits;
  static long cacheMisses;
  static long atlasLoadStarts;
  static long atlasLoadCompletions;
  static long atlasCacheHits;
  static long atlasCacheMisses;
  static long residentSliceLookups;
  static long gameplayColdAtlasMisses;
  static int queuedLoads;
  static int inFlightLoads;
  static int peakQueuedLoads;
  static int peakInFlightLoads;
  static int lastLoadStartFrame = -1;
  static int loadStartsThisFrame;
  static int peakLoadStartsPerFrame;
  static int delayedSwitchCount;
  static int switchWaitSamples;
  static float totalSwitchWaitMs;
  static float maxSwitchWaitMs;
  static int pinnedOwnerCount;
  static int pinnedAddressCount;
  static int pinnedPlayerAddresses;
  static int pinnedEnemyAddresses;
  static int pinnedUiAddresses;
  static int pinnedEffectAddresses;
  static int pinDemotions;
  static int pinBudgetPlayerAddresses;
  static int pinBudgetEnemyAddresses;
  static int pinBudgetUiAddresses;
  static int pinBudgetEffectAddresses;
  static float pinSaturationPlayerPct;
  static float pinSaturationEnemyPct;
  static float pinSaturationUiPct;
  static float pinSaturationEffectPct;
  static int pinClassBudgetHitCount;
  static int pinClassBudgetDroppedAddresses;
  static int warmRequestCount;
  static int warmTimeoutCount;
  static float warmLastReadyRatio;
  static float warmLastElapsedMs;
  static int warmLastReadyCount;
  static int warmLastTotalCount;
  static int warmLastCriticalReadyCount;
  static int warmLastCriticalTotalCount;
  static bool warmLastCriticalReady;
  static string warmLastContext;
  static bool warmLastHardTimeoutBypassUsed;
  static string warmLastFailureReason;
  static int atlasExpansionCount;
  static int atlasExpansionAddressesQueued;
  static int atlasExpansionFallbackCount;
  static float nextSummaryTime;
  static readonly StringBuilder hudBuilder = new(256);

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    totalLoadStarts = 0;
    cacheHits = 0;
    cacheMisses = 0;
    atlasLoadStarts = 0;
    atlasLoadCompletions = 0;
    atlasCacheHits = 0;
    atlasCacheMisses = 0;
    residentSliceLookups = 0;
    gameplayColdAtlasMisses = 0;
    queuedLoads = 0;
    inFlightLoads = 0;
    peakQueuedLoads = 0;
    peakInFlightLoads = 0;
    lastLoadStartFrame = -1;
    loadStartsThisFrame = 0;
    peakLoadStartsPerFrame = 0;
    delayedSwitchCount = 0;
    switchWaitSamples = 0;
    totalSwitchWaitMs = 0f;
    maxSwitchWaitMs = 0f;
    pinnedOwnerCount = 0;
    pinnedAddressCount = 0;
    pinnedPlayerAddresses = 0;
    pinnedEnemyAddresses = 0;
    pinnedUiAddresses = 0;
    pinnedEffectAddresses = 0;
    pinDemotions = 0;
    pinBudgetPlayerAddresses = 0;
    pinBudgetEnemyAddresses = 0;
    pinBudgetUiAddresses = 0;
    pinBudgetEffectAddresses = 0;
    pinSaturationPlayerPct = 0f;
    pinSaturationEnemyPct = 0f;
    pinSaturationUiPct = 0f;
    pinSaturationEffectPct = 0f;
    pinClassBudgetHitCount = 0;
    pinClassBudgetDroppedAddresses = 0;
    warmRequestCount = 0;
    warmTimeoutCount = 0;
    warmLastReadyRatio = 0f;
    warmLastElapsedMs = 0f;
    warmLastReadyCount = 0;
    warmLastTotalCount = 0;
    warmLastCriticalReadyCount = 0;
    warmLastCriticalTotalCount = 0;
    warmLastCriticalReady = false;
    warmLastContext = "";
    warmLastHardTimeoutBypassUsed = false;
    warmLastFailureReason = "";
    atlasExpansionCount = 0;
    atlasExpansionAddressesQueued = 0;
    atlasExpansionFallbackCount = 0;
    nextSummaryTime = 0f;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void EnsureRunner() {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
  }

  static bool IsEnabled {
    get {
      if (!Application.isEditor && !Debug.isDebugBuild) return false;
      return SpriteStreamingRuntimeSettings.EnableDiagnostics;
    }
  }

  public static bool Enabled => IsEnabled;

  public static void RecordLoadStarted() {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    var frame = Time.frameCount;
    if (frame != lastLoadStartFrame) {
      lastLoadStartFrame = frame;
      loadStartsThisFrame = 0;
    }

    loadStartsThisFrame++;
    totalLoadStarts++;
    if (loadStartsThisFrame > peakLoadStartsPerFrame) {
      peakLoadStartsPerFrame = loadStartsThisFrame;
    }
  }

  public static void RecordQueueState(int queued, int inFlight) {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    queuedLoads = Mathf.Max(queued, 0);
    inFlightLoads = Mathf.Max(inFlight, 0);
    if (queuedLoads > peakQueuedLoads) peakQueuedLoads = queuedLoads;
    if (inFlightLoads > peakInFlightLoads) peakInFlightLoads = inFlightLoads;
  }

  public static void RecordCacheLookup(bool hit) {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    if (hit) cacheHits++;
    else cacheMisses++;
  }

  public static void RecordAtlasLoadStarted() {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    atlasLoadStarts++;
  }

  public static void RecordAtlasLoadCompleted() {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    atlasLoadCompletions++;
  }

  public static void RecordAtlasCacheLookup(bool hit) {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    if (hit) atlasCacheHits++;
    else atlasCacheMisses++;
  }

  public static void RecordResidentSliceLookup() {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    residentSliceLookups++;
  }

  public static void RecordGameplayColdAtlasMiss() {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    gameplayColdAtlasMisses++;
  }

  public static void RecordAnimationSwitchWait(float waitMs, bool delayed) {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    var clamped = Mathf.Max(waitMs, 0f);
    totalSwitchWaitMs += clamped;
    switchWaitSamples++;
    if (clamped > maxSwitchWaitMs) maxSwitchWaitMs = clamped;
    if (delayed) delayedSwitchCount++;
  }

  public static void RecordPinState(TextureResidencyCache.PinSnapshot snapshot) {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    pinnedOwnerCount = Mathf.Max(snapshot.pinnedOwnerCount, 0);
    pinnedAddressCount = Mathf.Max(snapshot.pinnedAddressCount, 0);
    pinnedPlayerAddresses = Mathf.Max(snapshot.pinnedPlayerAddresses, 0);
    pinnedEnemyAddresses = Mathf.Max(snapshot.pinnedEnemyAddresses, 0);
    pinnedUiAddresses = Mathf.Max(snapshot.pinnedUiAddresses, 0);
    pinnedEffectAddresses = Mathf.Max(snapshot.pinnedEffectAddresses, 0);
    pinDemotions = Mathf.Max(snapshot.pinDemotions, 0);
    pinBudgetPlayerAddresses = Mathf.Max(SpriteStreamingRuntimeSettings.PinBudgetPlayerAddresses, 1);
    pinBudgetEnemyAddresses = Mathf.Max(SpriteStreamingRuntimeSettings.PinBudgetEnemyAddresses, 1);
    pinBudgetUiAddresses = Mathf.Max(SpriteStreamingRuntimeSettings.PinBudgetUiAddresses, 1);
    pinBudgetEffectAddresses = Mathf.Max(SpriteStreamingRuntimeSettings.PinBudgetEffectAddresses, 1);
    pinSaturationPlayerPct = 100f * pinnedPlayerAddresses / pinBudgetPlayerAddresses;
    pinSaturationEnemyPct = 100f * pinnedEnemyAddresses / pinBudgetEnemyAddresses;
    pinSaturationUiPct = 100f * pinnedUiAddresses / pinBudgetUiAddresses;
    pinSaturationEffectPct = 100f * pinnedEffectAddresses / pinBudgetEffectAddresses;
    pinClassBudgetHitCount = Mathf.Max(snapshot.classBudgetHitCount, 0);
    pinClassBudgetDroppedAddresses = Mathf.Max(snapshot.classBudgetDroppedAddresses, 0);
  }

  public static void RecordPinBudgetPressure(int classBudgetHitsDelta, int droppedAddressesDelta) {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    pinClassBudgetHitCount = Mathf.Max(pinClassBudgetHitCount + Mathf.Max(classBudgetHitsDelta, 0), 0);
    pinClassBudgetDroppedAddresses = Mathf.Max(pinClassBudgetDroppedAddresses + Mathf.Max(droppedAddressesDelta, 0), 0);
  }

  public static void RecordWarmCheckpoint(WarmResult result) {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    warmRequestCount++;
    if (!result.completedWithinTimeout) warmTimeoutCount++;
    warmLastReadyRatio = Mathf.Clamp01(result.readyRatio);
    warmLastElapsedMs = Mathf.Max(result.elapsedMs, 0f);
    warmLastReadyCount = Mathf.Max(result.readyCount, 0);
    warmLastTotalCount = Mathf.Max(result.totalCount, 0);
    warmLastCriticalReadyCount = Mathf.Max(result.criticalReadyCount, 0);
    warmLastCriticalTotalCount = Mathf.Max(result.criticalTotalCount, 0);
    warmLastCriticalReady = result.playerCriticalReady;
    warmLastContext = result.context.ToString();
    warmLastHardTimeoutBypassUsed = result.hardTimeoutBypassUsed;
    warmLastFailureReason = string.IsNullOrWhiteSpace(result.failureReason) ? "" : result.failureReason.Trim();
  }

  public static void RecordAtlasExpansion(int siblingCount, int queuedCount) {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    atlasExpansionCount = Mathf.Max(atlasExpansionCount + 1, 0);
    atlasExpansionAddressesQueued = Mathf.Max(atlasExpansionAddressesQueued + Mathf.Max(queuedCount, 0), 0);
  }

  public static void RecordAtlasExpansionFallback() {
    if (!IsEnabled) return;
    SpriteStreamingDiagnosticsRunner.EnsureInstance();
    atlasExpansionFallbackCount = Mathf.Max(atlasExpansionFallbackCount + 1, 0);
  }

  internal static void Tick() {
    if (!IsEnabled) return;
    var now = Time.unscaledTime;
    if (now < nextSummaryTime) return;
    nextSummaryTime = now + SummaryIntervalSeconds;

    var totalLookups = cacheHits + cacheMisses;
    var missRatePct = totalLookups > 0 ? (100f * cacheMisses / totalLookups) : 0f;
    var avgSwitchWait = switchWaitSamples > 0 ? (totalSwitchWaitMs / switchWaitSamples) : 0f;

    
  }

  internal static string BuildHudLine() {
    if (!IsEnabled) return "";
    var totalLookups = cacheHits + cacheMisses;
    var missRatePct = totalLookups > 0 ? (100f * cacheMisses / totalLookups) : 0f;
    var avgSwitchWait = switchWaitSamples > 0 ? (totalSwitchWaitMs / switchWaitSamples) : 0f;

    hudBuilder.Clear();
    hudBuilder.Append("SpriteStreaming ");
    hudBuilder.Append("Q=").Append(queuedLoads);
    hudBuilder.Append(" IF=").Append(inFlightLoads);
    hudBuilder.Append(" StartPeak=").Append(peakLoadStartsPerFrame);
    hudBuilder.Append(" Miss%=").Append(missRatePct.ToString("0.0"));
    hudBuilder.Append(" SwitchAvgMs=").Append(avgSwitchWait.ToString("0.0"));
    hudBuilder.Append(" Delayed=").Append(delayedSwitchCount);
    hudBuilder.Append(" Pins=").Append(pinnedOwnerCount).Append('/').Append(pinnedAddressCount);
    hudBuilder.Append(" PinSatP=").Append(pinSaturationPlayerPct.ToString("0"));
    hudBuilder.Append(" Demotions=").Append(pinDemotions);
    hudBuilder.Append(" BudgetHits=").Append(pinClassBudgetHitCount);
    hudBuilder.Append(" BudgetDrop=").Append(pinClassBudgetDroppedAddresses);
    hudBuilder.Append(" Warm=").Append(warmLastContext);
    hudBuilder.Append(" WarmR=").Append((warmLastReadyRatio * 100f).ToString("0"));
    hudBuilder.Append(" AtlasLd=").Append(atlasLoadCompletions).Append('/').Append(atlasLoadStarts);
    hudBuilder.Append(" AtlasC=").Append(atlasCacheHits).Append('/').Append(atlasCacheMisses);
    hudBuilder.Append(" AtlasMiss=").Append(gameplayColdAtlasMisses);
    hudBuilder.Append(" SliceHit=").Append(residentSliceLookups);
    hudBuilder.Append(" AtlasExp=").Append(atlasExpansionCount);
    hudBuilder.Append(" AtlasQ=").Append(atlasExpansionAddressesQueued);
    hudBuilder.Append(" AtlasFb=").Append(atlasExpansionFallbackCount);
    if (warmLastHardTimeoutBypassUsed) {
      hudBuilder.Append(" HardBypass=1");
    }
    return hudBuilder.ToString();
  }
}

sealed class SpriteStreamingDiagnosticsRunner : MonoBehaviour {
  static SpriteStreamingDiagnosticsRunner instance;

  internal static void EnsureInstance() {
    if (!Application.isPlaying) return;
    if (instance != null) return;
    var go = new GameObject("SpriteStreamingDiagnosticsRunner") { hideFlags = HideFlags.HideAndDontSave };
    DontDestroyOnLoad(go);
    instance = go.AddComponent<SpriteStreamingDiagnosticsRunner>();
  }

  void Update() {
    SpriteStreamingDiagnostics.Tick();
    AssetLoadTraceMonitor.Tick();
  }

  void OnGUI() {
    var text = SpriteStreamingDiagnostics.BuildHudLine();
    if (string.IsNullOrEmpty(text)) return;
    GUI.Label(new Rect(8f, 8f, 1600f, 24f), text);
  }

  void OnApplicationQuit() {
    AssetLoadTraceMonitor.Shutdown("application_quit");
  }

  void OnDestroy() {
    AssetLoadTraceMonitor.Shutdown("runner_destroy");
  }
}
