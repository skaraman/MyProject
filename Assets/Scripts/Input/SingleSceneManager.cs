
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class SingleSceneManager : MonoBehaviour {
  enum WarmGateMode {
    StartGame = 0,
    LoadSave = 1,
    GearApplyReturn = 2
  }

  enum UiMode {
    MainMenu,
    Gameplay,
    Pause
  }

  enum SettingsReturnTarget {
    MainMenu,
    PauseMenu
  }

  public InputProcessor inputProcessor;
  public MouseManager mouseManager;
  [FormerlySerializedAs("Blackscreen")]
  public GameObject LoadingScreen;
  private All1AnimatorScript blackscreen;
  private GameObject loadingBlackscreen;
  private SpriteRenderer loadingBlackscreenRenderer;
  private GameObject loadingCircle;
  private GameObject loadingTextObject;
  private FontText loadingText;
  private bool loadingUiFeedbackActive;
  private bool loadingOverlayChildrenReady;
  private int loadingPercent = -1;
  private float loadingPercentDisplayValue;
  private bool loadingPercentDisplayInitialized;
  private int loadingProgressPeakOutstanding = -1;
  private int loadingProgressGoalTotal = -1;
  private int loadingProgressGoalBestRemaining = int.MaxValue;
  private float loadingProgressIdleStartedAt = -1f;
  private bool loadingProgressObservedWork;
  private float loadingProgressNextDebugLogAt = -1f;
  private int loadingBlockingReadyCount;
  private int loadingBlockingTotalCount;
  private bool loadingBlockingCriticalReady;
  private bool loadingBlockingHardBypassUsed;
  private bool loadingBlockingStateKnown;
  const float LoadingProgressCompletionSettleSeconds = 0.15f;
  const float LoadingProgressSoftCap = 0.99f;

  public GameObject MainMenu;
  public GameObject LoadMenu;
  public GameObject SettingsMenu;
  public GameObject GameplayInterface;
  public GameObject PauseMenu;
  public GameObject Scene;
  [SerializeField] GameObject sceneObjectLights;

  public AutoSaver autoSaver;
  public SaveSlotView saveSlotView;
  // Runtime tuning targets:
  // - Critical warm scope should complete within soft timeout on normal loads.
  // - Gate-time queue pressure should trend down quickly (avoid long tails > ~1500 outstanding).
  // - In-flight addressable loads should usually stay under ~128 to reduce completion spikes.
  // - Pre-unlock resident pinning should stay bounded (prefer <= ~2048 addresses on desktop).
  // - Estimated resident texture bytes should sit under configured soft budget with headroom.
  [Header("Warm Gate")]
  [SerializeField] bool useScenarioWarmGate = true;
  [SerializeField, Min(0.5f)] float startWarmTimeoutSeconds = 2.0f;
  [SerializeField, Min(0.5f)] float startWarmHardTimeoutSeconds = 25.0f;
  [SerializeField, Min(0.5f)] float startWarmRequiredRatio = 0.97f;
  [SerializeField, Range(0.5f, 0.99f)] float loadSaveWarmRequiredRatioCap = 0.72f;
  [SerializeField, Range(0.5f, 0.99f)] float loadSaveWarmRequiredRatioFloor = 0.62f;
  [SerializeField, Min(0)] int warmGateCriticalEnemyCount = 3;
  [SerializeField, Min(0f)] float warmGateCriticalEnemyDistance = 25f;
  [SerializeField] bool warmGatePreloadCorePlayerEffects = true;
  [SerializeField, Min(0.5f)] float gearReturnWarmTimeoutSeconds = 3.0f;
  [SerializeField, Min(0.5f)] float gearReturnWarmHardTimeoutSeconds = 16.0f;
  [SerializeField, Min(0.5f)] float gearReturnRequiredRatio = 0.95f;
  [SerializeField] bool allowHardTimeoutBypass = true;
  [SerializeField, Min(0f)] float fadeLeadSeconds = 0.15f;
  [SerializeField, Min(0f)] float fallbackTransitionSeconds = 2.0f;
  [SerializeField, Min(0f)] float fadeToBlackSeconds = 0.2f;
  [SerializeField, Min(0f)] float fadeFromBlackSeconds = 0.25f;
  [SerializeField, Min(0f)] float loadingCircleSpinSpeedDegreesPerSecond = 360f;
  [SerializeField] bool waitForStreamingIdleBeforeFadeOut = true;
  [SerializeField, Min(0f)] float streamingIdleMinimumWaitSeconds = 0.1f;
  [SerializeField, Min(0f)] float streamingIdleTimeoutSeconds = 20.0f;
  [SerializeField] bool allowStreamingIdleTimeoutBypass = false;
  [SerializeField, Min(1)] int streamingIdleStableFrames = 2;
  [SerializeField, Min(0)] int streamingIdleAllowedQueued = 0;
  [SerializeField, Min(0)] int streamingIdleAllowedInFlight = 0;
  [SerializeField, Min(0)] int streamingBlockingReadyMaxOutstandingDesktop = 384;
  [SerializeField, Min(0)] int streamingBlockingReadyMaxOutstandingMobile = 256;
  [SerializeField, Min(0)] int streamingBlockingReadyMaxInFlightDesktop = 48;
  [SerializeField, Min(0)] int streamingBlockingReadyMaxInFlightMobile = 32;
  [SerializeField] bool enforceZeroOutstandingBeforeUnlock = true;
  [SerializeField, Min(1f)] float loadingPercentRisePerSecond = 500f;
  [Header("Pre-Unlock Prefetch")]
  [SerializeField] bool enablePreUnlockVisibleSpritePrefetch = true;
  // Increase this if animations are choppy immediately after unlock (e.g. to 12 or 24).
  [SerializeField, Min(1)] int preUnlockPrefetchAnimationFrames = 12;
  [SerializeField, Min(0)] int preUnlockPrefetchLookAheadFrames = 6;
  [SerializeField, Min(1)] int preUnlockPrefetchMaxAddresses = 12288;
  [SerializeField, Min(1)] int preUnlockPrefetchMinAddresses = 512;
  [SerializeField, Min(50)] int preUnlockPrefetchEnqueueBudgetPerFrame = 200;
  [SerializeField, Min(1)] int preUnlockPrefetchFrameJumpClamp = 4;
  [SerializeField, Min(0f)] float preUnlockTargetCacheRefreshSeconds = 0.5f;
  [SerializeField] bool preUnlockPrefetchExpandAtlasSiblings = true;
  [SerializeField, Min(1)] int preUnlockPrefetchMaxAtlasSiblingsPerSeed = 24;
  [SerializeField] bool preUnlockPrefetchIncludeUiTargets = false;
  [SerializeField] bool enablePreUnlockControllerAnimationPrefetch = true;
  [SerializeField, Min(1)] int preUnlockPlayerAnimationStarts = 64;
  [SerializeField, Min(0)] int preUnlockEnemyAnimationStartsPerController = 24;
  [Header("Pre-Unlock Animation Playback")]
  [SerializeField] bool enablePreUnlockAnimationPlaybackWarmup = true;
  [SerializeField, Min(1)] int preUnlockAnimationPlaybackPasses = 1;
  [SerializeField, Min(1)] int preUnlockAnimationFramePreloadPasses = 3;
  [SerializeField] bool preUnlockReprefetchVisibleSpritesAfterAnimationWarmup = true;
  [SerializeField, Min(0f)] float preUnlockWarmupQueueSettleTimeoutSeconds = 2.0f;
  [SerializeField, Min(0f)] float preUnlockBlockingBudgetSeconds = 1.25f;
  [SerializeField] bool enablePreUnlockResidentPinning = true;
  [SerializeField, Min(1)] int preUnlockResidentPinMaxAddresses = 2048;
  [SerializeField] bool preUnlockWarmEnemyAnimationPlayback = true;
  [SerializeField, Min(1)] int preUnlockAnimationWarmupControllersPerFrame = 1;
  [SerializeField, Min(0)] int preUnlockAnimationWarmupMaxEnemyControllers = 6;
  [SerializeField, Min(0f)] float preUnlockAnimationWarmupEnemyDistance = 25f;
  [SerializeField, Min(0f)] float startupFadeWatchdogSeconds = 2.5f;
  [SerializeField] bool enableLoadingStallEmergencyUnlock = true;
  [SerializeField, Min(1f)] float loadingStallEmergencyUnlockSeconds = 12.0f;
  [SerializeField, Min(0f)] float postUnlockPinReleaseDelaySeconds = 8.0f;
  [SerializeField, Min(0)] int postUnlockPinReleaseMaxOutstanding = 192;
  [SerializeField, Min(0f)] float postUnlockPinReleaseTimeoutSeconds = 20.0f;
  [SerializeField] string defaultStartLocation = LocationEnemyData.DomeCityLocationId;
  const string mainMenuFlowLocationId = LocationEnemyData.MainMenuLocationId;
  const string gameplayFlowFallbackLocationId = LocationEnemyData.DomeCityLocationId;
  private List<Action> actions = new();

  private bool init;
  private Coroutine startGameRoutine;
  private Coroutine resumeGameplayRoutine;
  private Coroutine startupGameplayRoutine;
  private Coroutine startupFadeWatchdogRoutine;
  private Coroutine unlockFadeFailSafeRoutine;
  private Coroutine uiModeRoutine;
  private GearController cachedPlayerGearController;
  private int pauseMenuOpenAppearanceRevision = -1;
  private bool holdBlackscreenOpaqueDuringLoad;
  private string lastPurgedLocationId = "";
  private float loadingStallStartedAt = -1f;
  private SettingsReturnTarget settingsReturnTarget = SettingsReturnTarget.MainMenu;
  private GameObject settingsCloseButton;
  private GameObject settingsHoveredTarget;
  private string activeInputMap = "";
  readonly List<string> preUnlockAddressScratch = new(4096);
  readonly HashSet<string> preUnlockSeenAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  readonly List<string> preUnlockAtlasSiblingScratch = new(64);
  readonly List<AnimationController> preUnlockEnemyControllerScratch = new(32);
  readonly List<(float sqrDist, AnimationController controller)> preUnlockFilteredEnemyScratch = new(32);
  readonly List<(float sqrDist, EnemyController enemy)> warmGateCriticalEnemyScratch = new(32);
  readonly List<string> preUnlockResidentPinAddressScratch = new(16384);
  readonly List<string> preUnlockResidentPinReadyAddressScratch = new(16384);
  readonly HashSet<string> preUnlockResidentPinSeenAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  readonly Stack<Transform> findChildScratch = new(64);
  MaterialPropertyBlock loadingBlackscreenPropertyBlock;
  EnemyController[] activeEnemyControllersCache = Array.Empty<EnemyController>();
  float activeEnemyControllersCacheRefreshedAt = -1f;
  GameplayInput cachedGameplayInput;
  float gameplayInputCacheRefreshedAt = -1f;
  int preUnlockLastPlayerAddressCount;
  SpriteWithNormals[] preUnlockVisibleSpriteTargetsCache = Array.Empty<SpriteWithNormals>();
  float preUnlockVisibleSpriteTargetsCacheRefreshedAt = -1f;
  const float ActiveEnemyControllersCacheRefreshSeconds = 0.2f;
  const float GameplayInputCacheRefreshSeconds = 0.2f;
  const long LocationPurgeAllThresholdBytes = 2L * 1024L * 1024L * 1024L;
  const string PreUnlockResidentPinOwnerId = "single_scene_manager.pre_unlock";
  const int PreUnlockResidentPinHardCapDesktop = 2048;
  const int PreUnlockResidentPinHardCapMobile = 1024;
  static readonly string[] CorePlayerEffectWarmKeys = { "SuperBlast", "SuperBlastBall" };
  void Start() {
    RegisterMessageBusHandlers();
    SetActiveSafe(LoadingScreen, true);
    InitializeLoadingScreenReferences();
    // Boot in fully black state, then reveal with explicit startup fade-out.
    ForceBlackscreenVisible(true);
    loadingOverlayChildrenReady = false;
    SetLoadingOverlayChildrenActive(false);
    SetLoadingText("");
    ApplyConfiguredStartupMode();
    ApplyInputMapForCurrentUiState(preferGameplayWhenNoUi: false);
  }

  void RegisterMessageBusHandlers() {
    actions.Add(MessageBus.On("startGame", o => StartGame()));
    actions.Add(MessageBus.On("openLoadMenu", o => OpenLoadMenu()));
    actions.Add(MessageBus.On("openSettingsMenu", o => OpenSettingsMenu()));
    actions.Add(MessageBus.On("backToMainMenu", o => OpenMainMenu()));
    actions.Add(MessageBus.On("settingsMenu.click", o => OnSettingsMenuClick(o)));
    actions.Add(MessageBus.On("settingsMenu.hover", o => OnSettingsMenuHover(o)));
    actions.Add(MessageBus.On("settingsMenu.unhover", o => OnSettingsMenuUnhover()));
    actions.Add(MessageBus.On("settingsMenu.select", o => OnSettingsMenuSelect()));
    actions.Add(MessageBus.On("settingsMenu.cancel", o => CloseSettingsMenu()));

    actions.Add(MessageBus.On("closePauseMenu", o => OpenGameplay()));
    actions.Add(MessageBus.On("openPauseMenu", o => OpenPauseMenu()));
    actions.Add(MessageBus.On("LocationUpdated", o => OnLocationUpdated(o)));
  }

  void Update() {
    UpdateLoadingScreenFeedback();

    if (!init) {
      if (ShouldRunStartupGameplayWarmFlow()) {
        SetLoadingBlackscreenHold(true);
        if (startupGameplayRoutine == null) {
          startupGameplayRoutine = StartCoroutine(StartupGameplayFlowRoutine());
        }
        init = true;
        return;
      }
      SetLoadingBlackscreenHold(false);
      ForceBlackscreenVisible(true);
      if (blackscreen != null) {
        PlayBlackscreen("alphaOut");
      }
      else {
        // Startup must never remain black if animator wiring is missing.
        SetLoadingBlackscreenHold(false);
        ForceBlackscreenVisible(false);
      }
      if (startupFadeWatchdogRoutine != null) {
        StopCoroutine(startupFadeWatchdogRoutine);
      }
      startupFadeWatchdogRoutine = StartCoroutine(StartupFadeWatchdogRoutine());
      init = true;
    }
  }

  void InitializeLoadingScreenReferences() {
    loadingBlackscreen = null;
    loadingBlackscreenRenderer = null;
    blackscreen = null;
    loadingCircle = null;
    loadingTextObject = null;
    loadingText = null;
    loadingUiFeedbackActive = false;
    loadingOverlayChildrenReady = false;
    loadingPercent = -1;
    loadingPercentDisplayInitialized = false;
    loadingPercentDisplayValue = 0f;
    loadingProgressPeakOutstanding = -1;
    loadingProgressGoalTotal = -1;
    loadingProgressGoalBestRemaining = int.MaxValue;
    loadingProgressIdleStartedAt = -1f;
    loadingProgressObservedWork = false;
    loadingProgressNextDebugLogAt = -1f;
    ResetBlockingProgressState();

    if (LoadingScreen == null) {
      return;
    }

    var blackscreenTransform = FindChildByName(LoadingScreen.transform, "blackscreen");
    if (blackscreenTransform != null) {
      loadingBlackscreen = blackscreenTransform.gameObject;
      loadingBlackscreenRenderer = loadingBlackscreen.GetComponent<SpriteRenderer>();
      blackscreen = loadingBlackscreen.GetComponent<All1AnimatorScript>();
      if (blackscreen != null) {
        blackscreen.AddFloatAnim("alphaIn", "_Alpha", 0f, 1f, Mathf.Max(fadeToBlackSeconds, 0f));
        blackscreen.AddFloatAnim("alphaOut", "_Alpha", 1f, 0f, Mathf.Max(fadeFromBlackSeconds, 0f));
      }
    }

    var circleTransform = FindChildByName(LoadingScreen.transform, "circle");
    if (circleTransform != null) {
      loadingCircle = circleTransform.gameObject;
    }

    var textTransform = FindChildByName(LoadingScreen.transform, "text");
    if (textTransform != null) {
      loadingTextObject = textTransform.gameObject;
      loadingText = textTransform.GetComponent<FontText>();
    }
  }

  void UpdateLoadingScreenFeedback() {
    if (!PrepareLoadingScreenFeedbackState()) return;
    if (!loadingOverlayChildrenReady) return;
    var percent = CalculateLoadingPercentFromTextureQueue();
    UpdateLoadingScreenPercentText(percent);
  }

  bool PrepareLoadingScreenFeedbackState() {
    var loadingActive = holdBlackscreenOpaqueDuringLoad || IsLoadingFlowActive();
    if (!loadingActive) {
      if (!loadingUiFeedbackActive && !loadingOverlayChildrenReady) return false;
      loadingUiFeedbackActive = false;
      loadingOverlayChildrenReady = false;
      loadingPercent = -1;
      loadingPercentDisplayInitialized = false;
      loadingPercentDisplayValue = 0f;
      loadingProgressPeakOutstanding = -1;
      loadingProgressGoalTotal = -1;
      loadingProgressGoalBestRemaining = int.MaxValue;
      loadingProgressIdleStartedAt = -1f;
      loadingProgressObservedWork = false;
      loadingProgressNextDebugLogAt = -1f;
      ResetBlockingProgressState();
      SetLoadingOverlayChildrenActive(false);
      SetLoadingText("");
      return false;
    }

    if (LoadingScreen != null && !LoadingScreen.activeSelf) {
      LoadingScreen.SetActive(true);
    }

    SetLoadingOverlayChildrenActive(loadingOverlayChildrenReady);
    loadingUiFeedbackActive = true;

    if (loadingOverlayChildrenReady) {
      RotateLoadingCircle();
    }

    return true;
  }

  int GetRemainingStreamingWork(
    out TextureResidencyCache.QueueSnapshot queue,
    out TextureResidencyCache.SessionSnapshot session,
    out int outstanding,
    out int sessionRemaining
  ) {
    queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    session = TextureResidencyCache.GetSessionSnapshot();
    var deferredSnapshot = TextureResidencyCache.GetDeferredSnapshot();
    outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount + deferredSnapshot.pendingCount, 0);
    sessionRemaining = session.HasKnownTotal
      ? Mathf.Max(session.EffectiveTotal - session.completedTotal, 0)
      : 0;
    return Mathf.Max(outstanding, sessionRemaining);
  }

  void ResetBlockingProgressState() {
    loadingBlockingReadyCount = 0;
    loadingBlockingTotalCount = 0;
    loadingBlockingCriticalReady = false;
    loadingBlockingHardBypassUsed = false;
    loadingBlockingStateKnown = false;
  }

  void CaptureBlockingProgressStateFromWarmResult(WarmResult result) {
    loadingBlockingTotalCount = Mathf.Max(result.criticalTotalCount, 0);
    loadingBlockingReadyCount = Mathf.Clamp(result.criticalReadyCount, 0, loadingBlockingTotalCount);
    loadingBlockingCriticalReady = result.playerCriticalReady;
    loadingBlockingHardBypassUsed = result.hardTimeoutBypassUsed;
    loadingBlockingStateKnown = loadingBlockingTotalCount > 0 || loadingBlockingCriticalReady || loadingBlockingHardBypassUsed;
  }

  bool TryGetBlockingProgressState(
    out float progress,
    out int readyCount,
    out int totalCount,
    out bool criticalReady,
    out bool hardBypassUsed
  ) {
    // Blocking scope comes from the warm "critical" addresses: player starters, selected
    // nearby enemies, core player effects, and location-declared critical content.
    if (StreamingWarmOrchestrator.TryGetActiveProgress(out var snapshot)) {
      loadingBlockingTotalCount = Mathf.Max(snapshot.criticalTotalCount, 0);
      loadingBlockingReadyCount = Mathf.Clamp(snapshot.criticalReadyCount, 0, loadingBlockingTotalCount);
      loadingBlockingCriticalReady = snapshot.criticalReady;
      loadingBlockingHardBypassUsed = false;
      loadingBlockingStateKnown = true;
    }

    if (!loadingBlockingStateKnown) {
      progress = 0f;
      readyCount = 0;
      totalCount = 0;
      criticalReady = false;
      hardBypassUsed = false;
      return false;
    }

    readyCount = loadingBlockingReadyCount;
    totalCount = loadingBlockingTotalCount;
    criticalReady = loadingBlockingCriticalReady;
    hardBypassUsed = loadingBlockingHardBypassUsed;
    progress = totalCount > 0
      ? (float)readyCount / totalCount
      : (criticalReady || hardBypassUsed ? 1f : 0f);
    progress = Mathf.Clamp01(progress);
    return true;
  }

  bool IsBlockingScopeReady(
    bool resolverIdle,
    bool playerReady,
    bool criticalReady,
    bool hardBypassUsed,
    TextureResidencyCache.QueueSnapshot queue
  ) {
    if (!resolverIdle || !playerReady) return false;
    if (!criticalReady && !hardBypassUsed) return false;
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    return IsQueueWithinBlockingReadyThresholds(queue, deferredPending);
  }

  void ResolveBlockingReadyQueueThresholds(out int maxOutstanding, out int maxInFlight) {
    maxOutstanding = Application.isMobilePlatform
      ? Mathf.Max(streamingBlockingReadyMaxOutstandingMobile, 0)
      : Mathf.Max(streamingBlockingReadyMaxOutstandingDesktop, 0);
    maxInFlight = Application.isMobilePlatform
      ? Mathf.Max(streamingBlockingReadyMaxInFlightMobile, 0)
      : Mathf.Max(streamingBlockingReadyMaxInFlightDesktop, 0);

    // Blocking progress already scopes the required first-frame content.
    // Forcing the entire queue to zero here parks the overlay at 99% while
    // long-tail warm work drains, which is the opposite of the intended model.
    if (enforceZeroOutstandingBeforeUnlock && !loadingBlockingStateKnown) {
      maxOutstanding = 0;
      maxInFlight = 0;
    }
  }

  bool IsQueueWithinBlockingReadyThresholds(TextureResidencyCache.QueueSnapshot queue, int deferredPending) {
    ResolveBlockingReadyQueueThresholds(out var maxOutstanding, out var maxInFlight);
    var outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount + deferredPending, 0);
    if (outstanding > maxOutstanding) return false;
    if (queue.inFlightCount > maxInFlight) return false;
    return true;
  }

  int CalculateLoadingPercentFromTextureQueue() {
    var remainingWork = GetRemainingStreamingWork(
      out var queue,
      out var session,
      out var outstanding,
      out var sessionRemaining
    );
    if (outstanding > 0) {
      loadingProgressObservedWork = true;
      if (loadingProgressPeakOutstanding < 0) {
        loadingProgressPeakOutstanding = outstanding;
      }
      else if (outstanding > loadingProgressPeakOutstanding) {
        loadingProgressPeakOutstanding = outstanding;
      }
    }

    var rawSessionProgress = session.Progress;
    if (sessionRemaining > 0) {
      loadingProgressObservedWork = true;
    }

    if (loadingProgressGoalTotal <= 0 && remainingWork > 0) {
      loadingProgressGoalTotal = remainingWork;
      loadingProgressGoalBestRemaining = loadingProgressGoalTotal;
    }

    if (loadingProgressGoalTotal > 0) {
      if (remainingWork > loadingProgressGoalTotal) {
        var completedBeforeResize = Mathf.Clamp(loadingProgressGoalTotal - loadingProgressGoalBestRemaining, 0, loadingProgressGoalTotal);
        loadingProgressGoalTotal = remainingWork;
        loadingProgressGoalBestRemaining = Mathf.Clamp(loadingProgressGoalTotal - completedBeforeResize, 0, loadingProgressGoalTotal);
      }
      loadingProgressGoalBestRemaining = Mathf.Clamp(
        Mathf.Min(loadingProgressGoalBestRemaining, remainingWork),
        0,
        loadingProgressGoalTotal
      );
    }

    var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
    var playerReady = IsPlayerFirstFrameReady();
    var hasBlockingProgress = TryGetBlockingProgressState(
      out var blockingProgress,
      out var blockingReadyCount,
      out var blockingTotalCount,
      out var blockingCriticalReady,
      out var blockingHardBypassUsed
    );
    if (hasBlockingProgress && (blockingTotalCount > 0 || blockingProgress > 0f)) {
      loadingProgressObservedWork = true;
    }

    var blockingReady = hasBlockingProgress &&
      IsBlockingScopeReady(resolverIdle, playerReady, blockingCriticalReady, blockingHardBypassUsed, queue);

    var hasOutstandingWork = hasBlockingProgress
      ? !blockingReady
      : (remainingWork > 0 || !resolverIdle || !playerReady);
    if (hasOutstandingWork) {
      loadingProgressIdleStartedAt = -1f;
    }
    else if (loadingProgressIdleStartedAt < 0f) {
      loadingProgressIdleStartedAt = Time.realtimeSinceStartup;
    }

    var completionSettled =
      !hasOutstandingWork &&
      loadingProgressIdleStartedAt >= 0f &&
      (Time.realtimeSinceStartup - loadingProgressIdleStartedAt) >= LoadingProgressCompletionSettleSeconds;

    var goalProgress = loadingProgressGoalTotal > 0
      ? 1f - ((float)loadingProgressGoalBestRemaining / loadingProgressGoalTotal)
      : 0f;
    goalProgress = Mathf.Clamp01(goalProgress);

    var queueProgress = 0f;
    if (loadingProgressPeakOutstanding > 0) {
      queueProgress = 1f - ((float)outstanding / loadingProgressPeakOutstanding);
    }
    queueProgress = Mathf.Clamp01(queueProgress);

    // Ideal loading UX tracks blocking scope (critical first-frame readiness)
    // rather than the full long-tail warm queue that can continue post-unlock.
    var targetProgress = hasBlockingProgress ? blockingProgress : goalProgress;
    if (!loadingProgressObservedWork && !hasBlockingProgress) {
      targetProgress = 0f;
    }
    if (hasBlockingProgress && blockingReady) {
      // Keep a small headroom until completion is settled so brief queue churn
      // does not show 100% and then regress.
      targetProgress = Mathf.Max(targetProgress, LoadingProgressSoftCap);
    }

    // Never show 100% while loading overlay is still active; only show 100% on explicit release.
    targetProgress = Mathf.Min(targetProgress, LoadingProgressSoftCap);
    var targetPercent = Mathf.Clamp(targetProgress * 100f, 0f, 100f);

    if (!loadingPercentDisplayInitialized) {
      loadingPercentDisplayInitialized = true;
      loadingPercentDisplayValue = Mathf.Max(loadingPercent >= 0 ? loadingPercent : 0, 0);
    }
    var riseRate = Mathf.Max(loadingPercentRisePerSecond, 1f);
    var dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
    var changeRate = targetPercent >= loadingPercentDisplayValue ? riseRate : riseRate * 2f;
    loadingPercentDisplayValue = Mathf.MoveTowards(
      loadingPercentDisplayValue,
      targetPercent,
      changeRate * dt
    );
    var actualPercent = Mathf.Clamp(Mathf.RoundToInt(loadingPercentDisplayValue), 0, 100);

    MaybeLogLoadingProgressDebug(
      session,
      rawSessionProgress,
      queue,
      outstanding,
      queueProgress,
      remainingWork,
      goalProgress,
      resolverIdle,
      playerReady,
      hasOutstandingWork,
      completionSettled,
      hasBlockingProgress,
      blockingReadyCount,
      blockingTotalCount,
      blockingProgress,
      blockingCriticalReady,
      blockingHardBypassUsed,
      blockingReady,
      targetProgress,
      actualPercent
    );

    return actualPercent;
  }

  void UpdateLoadingScreenPercentText(int percent) {
    if (percent == loadingPercent) return;
    loadingPercent = percent;
    SetLoadingText(percent + "%");
  }

  void ResetLoadingProgressForPhase(bool force = false) {
    var loadingActive = holdBlackscreenOpaqueDuringLoad || IsLoadingFlowActive();
    if (!loadingActive) return;
    if (!force && loadingPercent >= 0) return;

    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount, 0);
    loadingProgressPeakOutstanding = outstanding > 0 ? outstanding : -1;
    loadingProgressGoalTotal = -1;
    loadingProgressGoalBestRemaining = int.MaxValue;
    loadingProgressIdleStartedAt = -1f;
    loadingProgressObservedWork = outstanding > 0;
    loadingProgressNextDebugLogAt = -1f;
    ResetBlockingProgressState();
    loadingPercent = -1;
    loadingPercentDisplayInitialized = true;
    loadingPercentDisplayValue = 0f;
    SetLoadingText("0%");
    loadingPercent = 0;
  }

  void BeginLoadingProgressGoalAtFadeStart() {
    // Queue snapshot includes all load priorities (Immediate/Warmup/Background).
    var remainingWork = GetRemainingStreamingWork(
      out _,
      out _,
      out var outstanding,
      out _
    );
    loadingProgressGoalTotal = remainingWork > 0 ? remainingWork : -1;
    loadingProgressGoalBestRemaining = loadingProgressGoalTotal > 0 ? loadingProgressGoalTotal : int.MaxValue;
    loadingProgressPeakOutstanding = outstanding > 0 ? outstanding : -1;
    loadingProgressObservedWork = remainingWork > 0;
    loadingProgressNextDebugLogAt = -1f;
    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][LoadingProgress] prime_at_fade_start goal_total=" + loadingProgressGoalTotal +
        " queue_outstanding=" + outstanding +
        " remaining_work=" + remainingWork
      );
    }
  }

  void BeginLoadingProgressUiAfterFadeIn() {
    var remainingWork = GetRemainingStreamingWork(
      out _,
      out _,
      out var outstanding,
      out _
    );

    if (loadingProgressGoalTotal <= 0 && remainingWork > 0) {
      loadingProgressGoalTotal = remainingWork;
      loadingProgressGoalBestRemaining = remainingWork;
    }
    if (loadingProgressGoalTotal > 0 && remainingWork > loadingProgressGoalTotal) {
      var completedBeforeResize = Mathf.Clamp(loadingProgressGoalTotal - loadingProgressGoalBestRemaining, 0, loadingProgressGoalTotal);
      loadingProgressGoalTotal = remainingWork;
      loadingProgressGoalBestRemaining = Mathf.Clamp(loadingProgressGoalTotal - completedBeforeResize, 0, loadingProgressGoalTotal);
    }

    if (loadingProgressGoalTotal > 0) {
      loadingProgressGoalBestRemaining = Mathf.Clamp(
        Mathf.Min(loadingProgressGoalBestRemaining, remainingWork),
        0,
        loadingProgressGoalTotal
      );
    }
    if (outstanding > 0) {
      loadingProgressPeakOutstanding = Mathf.Max(loadingProgressPeakOutstanding, outstanding);
    }
    loadingProgressObservedWork = loadingProgressObservedWork || remainingWork > 0;
    loadingProgressNextDebugLogAt = -1f;
    loadingPercent = -1;
    loadingPercentDisplayInitialized = true;
    loadingPercentDisplayValue = 0f;
    UpdateLoadingScreenPercentText(0);
    loadingOverlayChildrenReady = true;
    SetLoadingOverlayChildrenActive(true);

    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][LoadingProgress] activate_after_fade_in goal_total=" + loadingProgressGoalTotal +
        " queue_outstanding=" + outstanding +
        " remaining_work=" + remainingWork
      );
    }
  }

  void MaybeLogLoadingProgressDebug(
    TextureResidencyCache.SessionSnapshot session,
    float rawSessionProgress,
    TextureResidencyCache.QueueSnapshot queue,
    int outstanding,
    float queueProgress,
    int remainingWork,
    float goalProgress,
    bool resolverIdle,
    bool playerReady,
    bool hasOutstandingWork,
    bool completionSettled,
    bool usingBlockingProgress,
    int blockingReadyCount,
    int blockingTotalCount,
    float blockingProgress,
    bool blockingCriticalReady,
    bool blockingHardBypassUsed,
    bool blockingReady,
    float targetProgress,
    int actualPercent
  ) {
    if (!ShouldLogLoadingProgressDebug()) return;
    var now = Time.realtimeSinceStartup;
    var logIntervalSeconds = Mathf.Max(SpriteStreamingRuntimeSettings.LoadingProgressLogIntervalMs, 100) / 1000f;
    var expectedPercent = Mathf.Clamp(Mathf.RoundToInt(targetProgress * 100f), 0, 100);
    var anomaly = expectedPercent >= 95 && actualPercent <= 5;
    if (!anomaly && now < loadingProgressNextDebugLogAt) return;
    loadingProgressNextDebugLogAt = now + logIntervalSeconds;
    var deferredSnapshot = TextureResidencyCache.GetDeferredSnapshot();

    Debug.Log(
      "[SingleSceneManager][LoadingProgress] expected=" + expectedPercent +
      " actual=" + actualPercent +
      " target=" + targetProgress.ToString("0.000") +
      " display=" + loadingPercentDisplayValue.ToString("0.00") +
      " raw_session=" + rawSessionProgress.ToString("0.000") +
      " session_expected=" + session.expectedTotal +
      " session_scheduled=" + session.scheduledTotal +
      " session_completed=" + session.completedTotal +
      " session_total=" + session.EffectiveTotal +
      " queue_queued=" + queue.queuedCount +
      " queue_in_flight=" + queue.inFlightCount +
      " queue_outstanding=" + outstanding +
      " queue_peak=" + loadingProgressPeakOutstanding +
      " queue_progress=" + queueProgress.ToString("0.000") +
      " goal_total=" + loadingProgressGoalTotal +
      " goal_remaining_best=" + loadingProgressGoalBestRemaining +
      " remaining_work=" + remainingWork +
      " goal_progress=" + goalProgress.ToString("0.000") +
      " blocking_mode=" + (usingBlockingProgress ? 1 : 0) +
      " blocking_ready_count=" + blockingReadyCount +
      " blocking_total_count=" + blockingTotalCount +
      " blocking_progress=" + blockingProgress.ToString("0.000") +
      " blocking_critical_ready=" + (blockingCriticalReady ? 1 : 0) +
      " blocking_hard_bypass=" + (blockingHardBypassUsed ? 1 : 0) +
      " blocking_ready=" + (blockingReady ? 1 : 0) +
      " observed_work=" + (loadingProgressObservedWork ? 1 : 0) +
      " resolver_idle=" + (resolverIdle ? 1 : 0) +
      " player_ready=" + (playerReady ? 1 : 0) +
      " has_work=" + (hasOutstandingWork ? 1 : 0) +
      " settled=" + (completionSettled ? 1 : 0) +
      " deferred_pending=" + deferredSnapshot.pendingCount +
      " deferred_flushed_frame=" + deferredSnapshot.flushedThisFrame +
      " deferred_total=" + deferredSnapshot.totalDeferredCount +
      " deferred_requests_total=" + deferredSnapshot.totalDeferralRequestCount +
      " deferred_promoted=" + deferredSnapshot.totalPromotedCount
    );
  }

  static bool ShouldLogLoadingProgressDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  void EnsureLoadingProgressForPhase() {
    if (loadingPercent >= 0) return;
    ResetLoadingProgressForPhase();
  }

  void FinalizeLoadingProgressForRelease() {
    loadingProgressIdleStartedAt = -1f;
    loadingProgressPeakOutstanding = Mathf.Max(loadingProgressPeakOutstanding, 1);
    loadingPercentDisplayInitialized = true;
    loadingPercentDisplayValue = 100f;
    loadingPercent = 100;
    SetLoadingText("100%");
  }

  void SetLoadingText(string value) {
    if (loadingText == null) return;
    var textValue = value ?? "";
    if (string.Equals(loadingText.content, textValue, StringComparison.Ordinal)) return;
    loadingText.content = textValue;
  }

  void SetLoadingOverlayChildrenActive(bool active) {
    if (loadingCircle != null && loadingCircle.activeSelf != active) {
      loadingCircle.SetActive(active);
    }
    if (loadingTextObject != null && loadingTextObject.activeSelf != active) {
      loadingTextObject.SetActive(active);
    }
  }

  void RotateLoadingCircle() {
    if (loadingCircle == null || !loadingCircle.activeInHierarchy) return;
    var spinSpeed = Mathf.Max(loadingCircleSpinSpeedDegreesPerSecond, 0f);
    if (spinSpeed <= 0f) return;
    var dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
    if (dt <= 0f) return;
    loadingCircle.transform.Rotate(0f, 0f, -spinSpeed * dt, Space.Self);
  }

  void LateUpdate() {
    if (!holdBlackscreenOpaqueDuringLoad) return;
    ForceBlackscreenVisible(true);
  }

  void FixedUpdate() {
    TickLoadingStallEmergencyUnlock();
  }

  void StartGame() {
    ReleasePreUnlockResidentPins("start_game");
    StopStartupFadeWatchdog();
    StopStartupGameplayFlow();
    SaveData loadedSlot = null;
    var isNewGame = _isNewGame();
    if (!isNewGame) {
      loadedSlot = SaveSlotManager.Load("slot");
      if (loadedSlot != null && loadedSlot.ContainsKey("playtimeHours") && loadedSlot.ContainsKey("playtimeMinutes") && loadedSlot.ContainsKey("playtimeSeconds")) {
        autoSaver.SetPlaytime((int)loadedSlot["playtimeHours"], (int)loadedSlot["playtimeMinutes"], (int)loadedSlot["playtimeSeconds"]);
      }
    }
    autoSaver.enableTimeTracking = true;
    _SwitchMap("none");
    if (resumeGameplayRoutine != null) {
      StopCoroutine(resumeGameplayRoutine);
      resumeGameplayRoutine = null;
      SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    }
    if (startGameRoutine != null) {
      StopCoroutine(startGameRoutine);
      SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    }
    startGameRoutine = StartCoroutine(StartGameFlowRoutine(isNewGame, loadedSlot));
  }

  void OpenLoadMenu() {
    _SwitchMap("loadMenu");
    SetActiveSafe(MainMenu, false);
    SetActiveSafe(SettingsMenu, false);
    SetActiveSafe(LoadMenu, true);
  }

  void OpenSettingsMenu() {
    var openedFromPause = PauseMenu != null && PauseMenu.activeInHierarchy;
    settingsReturnTarget = openedFromPause ? SettingsReturnTarget.PauseMenu : SettingsReturnTarget.MainMenu;
    settingsHoveredTarget = null;
    settingsCloseButton = null;

    _SwitchMap("settingsMenu");

    SetActiveSafe(MainMenu, false);
    SetActiveSafe(LoadMenu, false);
    SetActiveSafe(PauseMenu, false);
    SetActiveSafe(SettingsMenu, true);
    SetActiveSafe(GameplayInterface, false);
  }

  void CloseSettingsMenu() {
    if (SettingsMenu != null && !SettingsMenu.activeInHierarchy) return;

    SetActiveSafe(SettingsMenu, false);
    SetActiveSafe(LoadMenu, false);
    SetActiveSafe(GameplayInterface, false);

    if (settingsReturnTarget == SettingsReturnTarget.PauseMenu) {
      SetActiveSafe(MainMenu, false);
      SetActiveSafe(PauseMenu, true);
      _SwitchMap("pauseMenu");
      return;
    }

    SetActiveSafe(PauseMenu, false);
    SetActiveSafe(MainMenu, true);
    _SwitchMap("mainMenu");
  }

  void OnSettingsMenuClick(object payload) {
    var target = payload as GameObject;
    if (!IsSettingsCloseTarget(target)) return;
    CloseSettingsMenu();
  }

  void OnSettingsMenuHover(object payload) {
    settingsHoveredTarget = payload as GameObject;
  }

  void OnSettingsMenuUnhover() {
    settingsHoveredTarget = null;
  }

  void OnSettingsMenuSelect() {
    if (!IsSettingsCloseTarget(settingsHoveredTarget)) return;
    CloseSettingsMenu();
  }

  bool IsSettingsCloseTarget(GameObject target) {
    if (target == null) return false;

    var closeTarget = ResolveSettingsCloseButton();
    if (closeTarget != null) {
      var targetTransform = target.transform;
      var closeTransform = closeTarget.transform;
      return target == closeTarget ||
             targetTransform.IsChildOf(closeTransform) ||
             closeTransform.IsChildOf(targetTransform);
    }

    var current = target.transform;
    while (current != null) {
      if (string.Equals(current.name, "Close", StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
      current = current.parent;
    }

    return false;
  }

  GameObject ResolveSettingsCloseButton() {
    if (settingsCloseButton != null) return settingsCloseButton;
    if (SettingsMenu == null) return null;

    var found = FindChildByName(SettingsMenu.transform, "Close");
    if (found != null) {
      settingsCloseButton = found.gameObject;
    }
    return settingsCloseButton;
  }

  Transform FindChildByName(Transform root, string name) {
    if (root == null || string.IsNullOrWhiteSpace(name)) return null;

    findChildScratch.Clear();
    findChildScratch.Push(root);

    while (findChildScratch.Count > 0) {
      var current = findChildScratch.Pop();
      if (string.Equals(current.name, name, StringComparison.OrdinalIgnoreCase)) {
        findChildScratch.Clear();
        return current;
      }

      for (var i = 0; i < current.childCount; i++) {
        findChildScratch.Push(current.GetChild(i));
      }
    }

    return null;
  }

  GameObject ResolveSceneObjectLights() {
    if (sceneObjectLights != null) return sceneObjectLights;
    if (Scene == null) return null;
    var lights = FindChildByName(Scene.transform, "SCENEOBJECT LIGHTS");
    if (lights == null) return null;
    sceneObjectLights = lights.gameObject;
    return sceneObjectLights;
  }

  void SetSceneObjectLightsActive(bool active) {
    var lights = ResolveSceneObjectLights();
    if (lights == null || lights.activeSelf == active) return;
    lights.SetActive(active);
  }

  void RestoreSceneLightingForCurrentActivation() {
    // Safety net for aborted/forced transitions that skip the normal fade-out completion.
    SetSceneObjectLightsActive(Scene != null && Scene.activeInHierarchy);
  }

  void OpenGameplay() {
    ReleasePreUnlockResidentPins("open_gameplay");
    StopStartupFadeWatchdog();
    StopStartupGameplayFlow();
    if (startGameRoutine != null) return;
    if (resumeGameplayRoutine != null) {
      StopCoroutine(resumeGameplayRoutine);
      resumeGameplayRoutine = null;
      SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    }

    if (ShouldWarmGearReturn()) {
      resumeGameplayRoutine = StartCoroutine(ResumeGameplayFlowRoutine());
      return;
    }

    SetUiMode(UiMode.Gameplay);
  }

  void OpenMainMenu() {
    ReleasePreUnlockResidentPins("open_main_menu");
    if (uiModeRoutine != null) StopCoroutine(uiModeRoutine);
    uiModeRoutine = StartCoroutine(SwitchUiModeRoutine(UiMode.MainMenu));
  }

  void OpenPauseMenu() {
    var gear = ResolvePlayerGearController();
    pauseMenuOpenAppearanceRevision = gear != null ? gear.AppearanceRevision : -1;
    SetUiMode(UiMode.Pause);

  }

  private void _SwitchMap(string map) {
    if (string.IsNullOrWhiteSpace(map)) return;
    if (string.Equals(activeInputMap, map, StringComparison.Ordinal)) return;
    activeInputMap = map;
    if (inputProcessor != null) inputProcessor.SwitchMap(map);
    if (mouseManager != null) mouseManager.SwitchMap(map);
  }

  private bool _isNewGame() {
    return SaveSlotManager.slot > saveSlotView.SavesCount;
  }

  IEnumerator StartGameFlowRoutine(bool isNewGame, SaveData loadedSlot) {
    var context = isNewGame ? WarmGateMode.StartGame : WarmGateMode.LoadSave;
    yield return RunGameplayFlowRoutine(
      overlayTag: "StartGameFlow",
      warmContext: context,
      warmTimeoutSeconds: startWarmTimeoutSeconds,
      warmRequiredRatio: startWarmRequiredRatio,
      applyGameplayStateBeforeWarmGate: true,
      sendReadyForSpawns: true,
      switchInputMapToNone: false,
      resolveLocationForStart: true,
      isNewGame: isNewGame,
      loadedSlot: loadedSlot
    );
    startGameRoutine = null;
  }

  IEnumerator ResumeGameplayFlowRoutine() {
    yield return RunGameplayFlowRoutine(
      overlayTag: "ResumeGameplayFlow",
      warmContext: WarmGateMode.GearApplyReturn,
      warmTimeoutSeconds: gearReturnWarmTimeoutSeconds,
      warmRequiredRatio: gearReturnRequiredRatio,
      applyGameplayStateBeforeWarmGate: false,
      sendReadyForSpawns: false,
      switchInputMapToNone: true,
      resolveLocationForStart: false
    );
    resumeGameplayRoutine = null;
  }

  IEnumerator StartupGameplayFlowRoutine() {
    yield return RunGameplayFlowRoutine(
      overlayTag: "StartupGameplayFlow",
      warmContext: WarmGateMode.StartGame,
      warmTimeoutSeconds: startWarmTimeoutSeconds,
      warmRequiredRatio: startWarmRequiredRatio,
      applyGameplayStateBeforeWarmGate: true,
      sendReadyForSpawns: true,
      switchInputMapToNone: true,
      resolveLocationForStart: false
    );
    startupGameplayRoutine = null;
  }

  IEnumerator RunGameplayFlowRoutine(
    string overlayTag,
    WarmGateMode warmContext,
    float warmTimeoutSeconds,
    float warmRequiredRatio,
    bool applyGameplayStateBeforeWarmGate,
    bool sendReadyForSpawns,
    bool switchInputMapToNone,
    bool resolveLocationForStart,
    bool isNewGame = true,
    SaveData loadedSlot = null
  ) {
    SpriteStreamingLoadingState.BeginLoadingOverlay(overlayTag);
    ResetLoadingProgressForPhase(force: true);
    if (switchInputMapToNone) {
      _SwitchMap("none");
    }
    yield return FadeToBlackBeforeLoadRoutine();

    if (resolveLocationForStart) {
      ResolveAndApplyLocationForStart(isNewGame, loadedSlot);
    }

    if (!isNewGame) {
      ApplySavedGameplayStateUnderLoadingOverlay();
      yield return null;
    }

    if (applyGameplayStateBeforeWarmGate) {
      ApplyGameplayStateUnderBlack();
      if (sendReadyForSpawns) {
        MessageBus.Send("ReadyForSpawns");
        yield return null;
      }
    }

    var allowGameplayUnlock = true;
    yield return RunScenarioWarmGate(
      warmContext,
      warmTimeoutSeconds,
      warmRequiredRatio,
      allow => allowGameplayUnlock = allow
    );
    if (!allowGameplayUnlock) {
      ForceGameplayUnlockFallback();
      yield break;
    }

    if (!applyGameplayStateBeforeWarmGate) {
      ApplyGameplayStateUnderBlack();
    }

    yield return WaitForStreamingIdleBeforeUnlock(prefetchVisibleSprites: true, warmAnimationsBeforeUnlock: true);
    UnlockGameplayFromBlack();
  }

  void ApplySavedGameplayStateUnderLoadingOverlay() {
    MessageBus.Send("loadGame");
    if (!ShouldLogLoadingProgressDebug()) return;
    Debug.Log(
      "[SingleSceneManager][LoadState] Applied save-state under loading overlay" +
      " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
      " warm_gate_running=" + (StreamingWarmOrchestrator.IsWarmGateRunning ? 1 : 0) +
      " slot=" + SaveSlotManager.slot
    );
  }

  IEnumerator FadeToBlackBeforeLoadRoutine() {
    SetSceneObjectLightsActive(false);
    SetLoadingBlackscreenHold(false);
    BeginLoadingProgressGoalAtFadeStart();
    loadingOverlayChildrenReady = false;
    SetLoadingOverlayChildrenActive(false);
    SetLoadingText("");
    if (LoadingScreen != null && !LoadingScreen.activeSelf) {
      LoadingScreen.SetActive(true);
    }
    if (blackscreen != null) {
      PlayBlackscreen("alphaIn");
    }
    else {
      ForceBlackscreenVisible(true);
    }
    var waitSeconds = Mathf.Max(fadeToBlackSeconds, 0f);
    if (waitSeconds > 0f) {
      yield return new WaitForSecondsRealtime(waitSeconds);
    }
    ForceBlackscreenVisible(true);
    SetLoadingBlackscreenHold(true);
    BeginLoadingProgressUiAfterFadeIn();
  }

  IEnumerator RunScenarioWarmGate(
    WarmGateMode context,
    float timeoutSeconds,
    float requiredRatio,
    Action<bool> onComplete
  ) {
    ResetBlockingProgressState();
    var allowGameplayUnlock = true;
    var leadSeconds = Mathf.Max(fadeLeadSeconds, 0f);
    if (leadSeconds > 0f) {
      yield return new WaitForSecondsRealtime(leadSeconds);
    }

    if (!useScenarioWarmGate || !Application.isPlaying) {
      if (fallbackTransitionSeconds > 0f) {
        yield return new WaitForSecondsRealtime(fallbackTransitionSeconds);
      }
      onComplete?.Invoke(allowGameplayUnlock);
      yield break;
    }

    var playerController = ResolvePlayerGearController();
    var activeEnemies = ResolveActiveEnemyControllers();
    var request = BuildWarmRequest(context, timeoutSeconds, requiredRatio, playerController, activeEnemies);
    var orchestrator = StreamingWarmOrchestrator.Instance;
    if (orchestrator == null) {
      if (fallbackTransitionSeconds > 0f) {
        yield return new WaitForSecondsRealtime(fallbackTransitionSeconds);
      }
      onComplete?.Invoke(allowGameplayUnlock);
      yield break;
    }

    var completed = false;
    var hasResult = false;
    WarmResult warmResult = default;
    orchestrator.Run(request, result => {
      warmResult = result;
      hasResult = true;
      completed = true;
    });

    while (!completed && orchestrator.IsRunning) {
      yield return null;
    }

    if (!completed && !hasResult) {
      if (fallbackTransitionSeconds > 0f) {
        yield return new WaitForSecondsRealtime(fallbackTransitionSeconds);
      }
      onComplete?.Invoke(allowGameplayUnlock);
      yield break;
    }

    CaptureBlockingProgressStateFromWarmResult(warmResult);
    allowGameplayUnlock = warmResult.reachedReadyThreshold || warmResult.hardTimeoutBypassUsed;
    onComplete?.Invoke(allowGameplayUnlock);
  }

  float ResolvePreUnlockBlockingDeadline() {
    var budgetSeconds = Mathf.Max(preUnlockBlockingBudgetSeconds, 0f);
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount, 0);
    if (outstanding >= 2000) {
      budgetSeconds = Mathf.Max(budgetSeconds, 6f);
    }
    else if (outstanding >= 1200) {
      budgetSeconds = Mathf.Max(budgetSeconds, 4f);
    }
    else if (outstanding >= 600) {
      budgetSeconds = Mathf.Max(budgetSeconds, 2.5f);
    }
    if (budgetSeconds <= 0f) return float.PositiveInfinity;
    return Time.realtimeSinceStartup + budgetSeconds;
  }

  bool TryGetRemainingPreUnlockBlockingBudget(float deadline, out float remainingSeconds) {
    if (float.IsInfinity(deadline)) {
      remainingSeconds = float.PositiveInfinity;
      return true;
    }

    remainingSeconds = deadline - Time.realtimeSinceStartup;
    return remainingSeconds > 0f;
  }

  void LogPreUnlockBlockingBudget(string stage, float deadline, string state) {
    if (!ShouldLogLoadingProgressDebug()) return;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var remainingText = float.IsInfinity(deadline)
      ? "inf"
      : Mathf.Max(deadline - Time.realtimeSinceStartup, 0f).ToString("0.000");
    Debug.Log(
      "[SingleSceneManager][PreUnlockBudget] stage=" + stage +
      " state=" + state +
      " remaining_s=" + remainingText +
      " queue_queued=" + queue.queuedCount +
      " queue_in_flight=" + queue.inFlightCount +
      " queue_outstanding=" + Mathf.Max(queue.queuedCount + queue.inFlightCount, 0)
    );
  }

  static void DisposeEnumerator(IEnumerator routine) {
    if (routine is IDisposable disposable) {
      disposable.Dispose();
    }
  }

  static void DisposeEnumeratorStack(Stack<IEnumerator> stack) {
    if (stack == null) return;
    while (stack.Count > 0) {
      DisposeEnumerator(stack.Pop());
    }
  }

  IEnumerator RunPreUnlockStepWithBudget(IEnumerator routine, float deadline, string stage) {
    if (routine == null) yield break;

    var stack = new Stack<IEnumerator>();
    stack.Push(routine);

    while (true) {
      if (stack.Count <= 0) {
        yield break;
      }

      if (!float.IsInfinity(deadline) && Time.realtimeSinceStartup >= deadline) {
        DisposeEnumeratorStack(stack);
        LogPreUnlockBlockingBudget(stage, deadline, "budget_exhausted");
        yield break;
      }

      var currentRoutine = stack.Peek();
      if (!currentRoutine.MoveNext()) {
        DisposeEnumerator(currentRoutine);
        stack.Pop();
        continue;
      }

      var yielded = currentRoutine.Current;
      if (yielded is IEnumerator nestedRoutine) {
        stack.Push(nestedRoutine);
        continue;
      }

      yield return yielded;
    }
  }

  void ResetPreUnlockResidentPins() {
    preUnlockResidentPinAddressScratch.Clear();
    preUnlockResidentPinReadyAddressScratch.Clear();
    preUnlockResidentPinSeenAddressScratch.Clear();
    if (!Application.isPlaying) return;
    TextureResidencyCache.ReleaseOwnerPins(PreUnlockResidentPinOwnerId);
  }

  int ResolvePreUnlockResidentPinAddressCap() {
    var hardCap = Application.isMobilePlatform ? PreUnlockResidentPinHardCapMobile : PreUnlockResidentPinHardCapDesktop;
    var memoryMb = Math.Max(SystemInfo.systemMemorySize, 0);
    if (memoryMb > 0 && memoryMb <= 4096) hardCap = Math.Min(hardCap, 768);
    else if (memoryMb > 0 && memoryMb <= 8192) hardCap = Math.Min(hardCap, 1536);
    return Mathf.Clamp(Math.Min(preUnlockResidentPinMaxAddresses, hardCap), 1, hardCap);
  }

  void AccumulatePreUnlockResidentPins(List<string> addresses) {
    if (!enablePreUnlockResidentPinning) return;
    if (addresses == null || addresses.Count <= 0) return;

    var maxAddresses = ResolvePreUnlockResidentPinAddressCap();
    var target = preUnlockResidentPinAddressScratch;
    var seen = preUnlockResidentPinSeenAddressScratch;

    for (var i = 0; i < addresses.Count; i++) {
      if (target.Count >= maxAddresses) break;
      var normalized = string.IsNullOrWhiteSpace(addresses[i]) ? "" : addresses[i].Trim();
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      if (!seen.Add(normalized)) continue;
      target.Add(normalized);
    }
  }

  void CommitPreUnlockResidentPins(string stage) {
    if (!Application.isPlaying) return;
    var trackedAddresses = preUnlockResidentPinAddressScratch;
    if (!enablePreUnlockResidentPinning || trackedAddresses.Count <= 0) {
      TextureResidencyCache.ReleaseOwnerPins(PreUnlockResidentPinOwnerId);
      return;
    }

    var readyAddresses = preUnlockResidentPinReadyAddressScratch;
    readyAddresses.Clear();
    for (var i = 0; i < trackedAddresses.Count; i++) {
      var address = trackedAddresses[i];
      if (string.IsNullOrWhiteSpace(address)) continue;
      if (!TextureResidencyCache.IsReady(address, pump: false)) continue;
      readyAddresses.Add(address);
    }

    if (readyAddresses.Count <= 0) {
      TextureResidencyCache.ReleaseOwnerPins(PreUnlockResidentPinOwnerId);
      if (!ShouldLogLoadingProgressDebug()) return;
      Debug.Log(
        "[SingleSceneManager][PreUnlockPin] stage=" + stage +
        " tracked_addresses=" + trackedAddresses.Count +
        " pinned_ready_addresses=0 queue_only_skip=1"
      );
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      PreUnlockResidentPinOwnerId,
      TextureResidencyCache.PinClass.WarmGate,
      readyAddresses,
      TextureResidencyCache.LoadPriority.Warmup
    );

    if (!ShouldLogLoadingProgressDebug()) return;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    Debug.Log(
      "[SingleSceneManager][PreUnlockPin] stage=" + stage +
      " tracked_addresses=" + trackedAddresses.Count +
      " pinned_ready_addresses=" + readyAddresses.Count +
      " queue_queued=" + queue.queuedCount +
      " queue_in_flight=" + queue.inFlightCount +
      " resident_mb=" + (TextureResidencyCache.EstimatedResidentBytes / (1024f * 1024f)).ToString("0.0")
    );
  }

  void ReleasePreUnlockResidentPins(string reason) {
    var hadTrackedAddresses = preUnlockResidentPinAddressScratch.Count > 0;
    preUnlockResidentPinAddressScratch.Clear();
    preUnlockResidentPinReadyAddressScratch.Clear();
    preUnlockResidentPinSeenAddressScratch.Clear();
    if (!Application.isPlaying) return;
    TextureResidencyCache.ReleaseOwnerPins(PreUnlockResidentPinOwnerId);
    if (!hadTrackedAddresses || !ShouldLogLoadingProgressDebug()) return;
    Debug.Log("[SingleSceneManager][PreUnlockPin] release reason=" + (string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim()));
  }

  IEnumerator WaitForStreamingIdleBeforeUnlock(
    bool prefetchVisibleSprites = false,
    bool warmAnimationsBeforeUnlock = false
  ) {
    if (!Application.isPlaying) yield break;
    ResetPreUnlockResidentPins();
    if (!waitForStreamingIdleBeforeFadeOut) {
      var preUnlockBlockingDeadlineWithoutIdleWait = ResolvePreUnlockBlockingDeadline();
      if (warmAnimationsBeforeUnlock) {
        if (TryGetRemainingPreUnlockBlockingBudget(preUnlockBlockingDeadlineWithoutIdleWait, out _)) {
          yield return RunPreUnlockStepWithBudget(
            RunPreUnlockAnimationWarmupSequence(prefetchVisibleSprites),
            preUnlockBlockingDeadlineWithoutIdleWait,
            "animation_warmup_no_idle_wait"
          );
        }
        else {
          LogPreUnlockBlockingBudget("animation_warmup_no_idle_wait", preUnlockBlockingDeadlineWithoutIdleWait, "skipped_no_budget");
        }
      }
      CommitPreUnlockResidentPins("no_idle_wait");
      yield break;
    }
    if (LoadingScreen != null && !LoadingScreen.activeSelf) {
      LoadingScreen.SetActive(true);
    }
    SetLoadingBlackscreenHold(true);
    EnsureLoadingProgressForPhase();
    var preUnlockBlockingDeadline = ResolvePreUnlockBlockingDeadline();
    if (prefetchVisibleSprites) {
      if (TryGetRemainingPreUnlockBlockingBudget(preUnlockBlockingDeadline, out _)) {
        yield return RunPreUnlockStepWithBudget(
          PreloadVisibleSpriteWindowsUnderBlack(),
          preUnlockBlockingDeadline,
          "visible_prefetch"
        );
      }
      else {
        LogPreUnlockBlockingBudget("visible_prefetch", preUnlockBlockingDeadline, "skipped_no_budget");
      }
    }
    var stableFramesRequired = Mathf.Max(streamingIdleStableFrames, 1);
    // Legacy queue-idle fallback is retained for non-warm-gate transitions where
    // no blocking snapshot exists.
    var allowedQueued = Mathf.Max(streamingIdleAllowedQueued, 0);
    var allowedInFlight = Mathf.Max(streamingIdleAllowedInFlight, 0);
    var minimumWaitSeconds = Mathf.Max(streamingIdleMinimumWaitSeconds, 0f);
    var timeoutSeconds = Mathf.Max(streamingIdleTimeoutSeconds, 0f);
    var startedAt = Time.realtimeSinceStartup;
    var stableFrames = 0;

    var warmupDone = false;

    while (true) {
      TextureResidencyCache.Pump();
      var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
      var queueIdle = queue.queuedCount <= allowedQueued && queue.inFlightCount <= allowedInFlight;
      var elapsed = Time.realtimeSinceStartup - startedAt;
      var minimumWaitReached = elapsed >= minimumWaitSeconds;
      var playerReady = IsPlayerFirstFrameReady();
      var hasBlockingProgress = TryGetBlockingProgressState(
        out _,
        out _,
        out _,
        out var blockingCriticalReady,
        out var blockingHardBypassUsed
      );
      var blockingReady = hasBlockingProgress
        ? IsBlockingScopeReady(resolverIdle, playerReady, blockingCriticalReady, blockingHardBypassUsed, queue)
        : (queueIdle && resolverIdle && playerReady);

      if (minimumWaitReached && blockingReady) {
        stableFrames++;
      }
      else {
        stableFrames = 0;
      }

      if (stableFrames >= stableFramesRequired) {
        if (warmAnimationsBeforeUnlock && !warmupDone) {
          warmupDone = true;
          if (TryGetRemainingPreUnlockBlockingBudget(preUnlockBlockingDeadline, out _)) {
            yield return RunPreUnlockStepWithBudget(
              RunPreUnlockAnimationWarmupSequence(prefetchVisibleSprites),
              preUnlockBlockingDeadline,
              "animation_warmup"
            );
          }
          else {
            LogPreUnlockBlockingBudget("animation_warmup", preUnlockBlockingDeadline, "skipped_no_budget");
          }
          stableFrames = 0;
          startedAt = Time.realtimeSinceStartup;
          continue;
        }
        CommitPreUnlockResidentPins("stable_ready");
        yield break;
      }

      if (timeoutSeconds > 0f && elapsed >= timeoutSeconds) {
        var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
        var queueFullyDrained = queue.queuedCount <= 0 && queue.inFlightCount <= 0 && deferredPending <= 0;
        var forcedByBlockingReady = !allowStreamingIdleTimeoutBypass && hasBlockingProgress && blockingReady;
        var forcedByLegacyDrain = !allowStreamingIdleTimeoutBypass && !hasBlockingProgress && queueFullyDrained;
        if (allowStreamingIdleTimeoutBypass || forcedByBlockingReady || forcedByLegacyDrain) {
          if (warmAnimationsBeforeUnlock && !warmupDone) {
            warmupDone = true;
            if (TryGetRemainingPreUnlockBlockingBudget(preUnlockBlockingDeadline, out _)) {
              yield return RunPreUnlockStepWithBudget(
                RunPreUnlockAnimationWarmupSequence(prefetchVisibleSprites),
                preUnlockBlockingDeadline,
                "animation_warmup_after_timeout"
              );
            }
            else {
              LogPreUnlockBlockingBudget("animation_warmup_after_timeout", preUnlockBlockingDeadline, "skipped_no_budget");
            }
            stableFrames = 0;
            startedAt = Time.realtimeSinceStartup;
            continue;
          }
          CommitPreUnlockResidentPins("timeout_release");
          yield break;
        }
      }

      yield return null;
    }
  }

  bool IsPlayerFirstFrameReady() {
    var player = ResolvePlayerGearController();
    if (player == null) return true;
    if (!AreSpriteTargetsFrameReady(player.SkinObjects, 1)) return false;
    if (!AreSpriteTargetsFrameReady(player.GearObjects, 1)) return false;
    return true;
  }

  static bool AreSpriteTargetsFrameReady(GameObject[] objects, int frame) {
    if (objects == null || objects.Length == 0) return true;
    for (var i = 0; i < objects.Length; i++) {
      var go = objects[i];
      if (go == null) continue;
      var sprite = go.GetComponent<SpriteWithNormals>();
      if (sprite == null || !sprite.isActiveAndEnabled || sprite.DoNotRender) continue;
      if (!sprite.IsFrameReady(frame, out _)) return false;
    }
    return true;
  }

  IEnumerator RunPreUnlockAnimationWarmupSequence(bool includeVisibleSpriteReprefetch) {
    var playerController = ResolvePlayerAnimationController();
    BuildEnemyAnimationControllerSnapshot(preUnlockEnemyControllerScratch);

    yield return WarmAnimationPlaybackBeforeUnlock(playerController, preUnlockEnemyControllerScratch);

    BuildControllerAnimationFrameAddressSnapshot(
      playerController,
      preUnlockEnemyControllerScratch,
      preUnlockAddressScratch
    );

    if (preUnlockAddressScratch.Count > 0) {
      var preloadPasses = Mathf.Max(preUnlockAnimationFramePreloadPasses, 1);
      for (var pass = 0; pass < preloadPasses; pass++) {
        yield return PreloadAnimationAddressBatch(preUnlockAddressScratch, resetLoadingProgress: pass == 0);
        yield return WaitForPreUnlockWarmupQueueSettle();
        if (pass + 1 < preloadPasses) {
          yield return null;
        }
      }
    }

    if (includeVisibleSpriteReprefetch && preUnlockReprefetchVisibleSpritesAfterAnimationWarmup) {
      yield return PreloadVisibleSpriteWindowsUnderBlack(preUnlockEnemyControllerScratch);
      yield return WaitForPreUnlockWarmupQueueSettle();
    }
  }

  IEnumerator WaitForPreUnlockWarmupQueueSettle() {
    var timeoutSeconds = Mathf.Max(preUnlockWarmupQueueSettleTimeoutSeconds, 0f);
    if (timeoutSeconds <= 0f) yield break;

    var startedAt = Time.realtimeSinceStartup;
    while (true) {
      TextureResidencyCache.Pump();
      var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
      if (queue.queuedCount <= 0 && queue.inFlightCount <= 0 && deferredPending <= 0) {
        yield break;
      }

      var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
      var playerReady = IsPlayerFirstFrameReady();
      var hasBlockingProgress = TryGetBlockingProgressState(
        out _,
        out _,
        out _,
        out var blockingCriticalReady,
        out var blockingHardBypassUsed
      );
      var settledForBlockingReady =
        hasBlockingProgress &&
        resolverIdle &&
        playerReady &&
        (blockingCriticalReady || blockingHardBypassUsed) &&
        IsQueueWithinBlockingReadyThresholds(queue, deferredPending);
      if (settledForBlockingReady) {
        if (ShouldLogLoadingProgressDebug()) {
          ResolveBlockingReadyQueueThresholds(out var maxOutstanding, out var maxInFlight);
          Debug.Log(
            "[SingleSceneManager][PreUnlockSettle] early_release" +
            " queued=" + queue.queuedCount +
            " in_flight=" + queue.inFlightCount +
            " deferred=" + deferredPending +
            " max_outstanding=" + maxOutstanding +
            " max_in_flight=" + maxInFlight
          );
        }
        yield break;
      }

      if ((Time.realtimeSinceStartup - startedAt) >= timeoutSeconds) {
        yield break;
      }
      yield return null;
    }
  }

  IEnumerator PreloadVisibleSpriteWindowsUnderBlack(List<AnimationController> enemyControllers = null) {
    if (!enablePreUnlockVisibleSpritePrefetch || !Application.isPlaying) yield break;

    var targets = ResolvePreUnlockVisibleSpriteTargets();
    if (targets == null || targets.Length <= 0) yield break;

    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var maxAddresses = ResolvePreUnlockMaxAddresses(queue);
    var addresses = preUnlockAddressScratch;
    var seenAddresses = preUnlockSeenAddressScratch;
    addresses.Clear();
    seenAddresses.Clear();
    var animationFrames = Mathf.Max(preUnlockPrefetchAnimationFrames, 1);
    var lookAheadFrames = ResolvePreUnlockLookAheadFrames(queue);
    var frameJumpClamp = Mathf.Max(preUnlockPrefetchFrameJumpClamp, 1);
    SortTargetsByPriority(targets);

    for (var i = 0; i < targets.Length; i++) {
      var target = targets[i];
      if (target == null) continue;
      if (!target.isActiveAndEnabled || target.DoNotRender) continue;
      if (!preUnlockPrefetchIncludeUiTargets && target.IsUiTarget()) continue;

      var startFrame = target.IsAnimation
        ? Mathf.Max(Mathf.Max(target.LastRequestedFrame, 1) - (frameJumpClamp - 1), 1)
        : 0;
      var endFrame = target.IsAnimation ? Mathf.Max(startFrame + animationFrames - 1, startFrame) : 0;

      target.CollectAnimationWindowAddresses(
        target.category,
        startFrame,
        endFrame,
        lookAheadFrames,
        addresses,
        seenAddresses,
        maxAddresses
      );

      if (addresses.Count >= maxAddresses) break;
    }

    if (enablePreUnlockControllerAnimationPrefetch && addresses.Count < maxAddresses) {
      var playerController = ResolvePlayerAnimationController();
      if (playerController != null) {
        playerController.CollectAnimationStartAddresses(
          addresses,
          seenAddresses,
          framesPerAnimation: animationFrames,
          maxAnimations: Mathf.Max(preUnlockPlayerAnimationStarts, 1),
          maxAddresses: maxAddresses
        );
      }

      var enemyMaxAnimations = Mathf.Max(preUnlockEnemyAnimationStartsPerController, 0);
      if (enemyMaxAnimations > 0 && addresses.Count < maxAddresses) {
        if (enemyControllers != null && enemyControllers.Count > 0) {
          for (var i = 0; i < enemyControllers.Count; i++) {
            if (addresses.Count >= maxAddresses) break;
            var controller = enemyControllers[i];
            if (controller == null) continue;
            controller.CollectAnimationStartAddresses(
              addresses,
              seenAddresses,
              framesPerAnimation: animationFrames,
              maxAnimations: enemyMaxAnimations,
              maxAddresses: maxAddresses
            );
          }
        }
        else {
          var activeEnemies = ResolveActiveEnemyControllers();
          for (var i = 0; i < activeEnemies.Length; i++) {
            if (addresses.Count >= maxAddresses) break;
            var enemy = activeEnemies[i];
            if (enemy == null || enemy.Controller == null) continue;
            enemy.Controller.CollectAnimationStartAddresses(
              addresses,
              seenAddresses,
              framesPerAnimation: animationFrames,
              maxAnimations: enemyMaxAnimations,
              maxAddresses: maxAddresses
            );
          }
        }
      }
    }

    if (preUnlockPrefetchExpandAtlasSiblings && addresses.Count > 0 && addresses.Count < maxAddresses) {
      var maxSiblingsPerSeed = Mathf.Clamp(preUnlockPrefetchMaxAtlasSiblingsPerSeed, 1, 256);
      var siblingScratch = preUnlockAtlasSiblingScratch;
      if (siblingScratch.Capacity < maxSiblingsPerSeed) {
        siblingScratch.Capacity = maxSiblingsPerSeed;
      }
      var seedCount = addresses.Count;
      for (var i = 0; i < seedCount; i++) {
        if (addresses.Count >= maxAddresses) break;
        var seedAddress = addresses[i];
        if (string.IsNullOrWhiteSpace(seedAddress)) continue;

        siblingScratch.Clear();
        if (!SpriteRuntimeResolver.TryCollectAtlasSiblingAddresses(seedAddress, siblingScratch, maxSiblingsPerSeed)) continue;

        for (var s = 0; s < siblingScratch.Count; s++) {
          if (addresses.Count >= maxAddresses) break;
          var siblingAddress = siblingScratch[s];
          if (string.IsNullOrWhiteSpace(siblingAddress)) continue;
          if (!seenAddresses.Add(siblingAddress)) continue;
          addresses.Add(siblingAddress);
        }
      }
    }

    if (addresses.Count <= 0) yield break;

    yield return PreloadAnimationAddressBatch(addresses, resetLoadingProgress: false);
  }

  void SortTargetsByPriority(SpriteWithNormals[] targets) {
    if (targets == null || targets.Length <= 1) return;
    var player = ResolvePlayerGearController();
    var playerPos = player != null ? player.transform.position : Vector3.zero;
    var hasPlayer = player != null;

    Array.Sort(targets, (a, b) => {
      if (a == null && b == null) return 0;
      if (a == null) return 1;
      if (b == null) return -1;
      if (!hasPlayer) return 0;
      var distA = (a.transform.position - playerPos).sqrMagnitude;
      var distB = (b.transform.position - playerPos).sqrMagnitude;
      return distA.CompareTo(distB);
    });
  }

  IEnumerator WarmAnimationPlaybackBeforeUnlock(
    AnimationController playerController = null,
    List<AnimationController> enemyControllers = null
  ) {
    if (!enablePreUnlockAnimationPlaybackWarmup || !Application.isPlaying) yield break;

    var passes = ResolvePreUnlockPlaybackPasses();
    var controllersPerFrame = Mathf.Max(preUnlockAnimationWarmupControllersPerFrame, 1);
    var warmedControllers = 0;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var maxAddresses = ResolvePreUnlockMaxAddresses(queue);
    var addresses = preUnlockAddressScratch;
    var seenAddresses = preUnlockSeenAddressScratch;
    addresses.Clear();
    seenAddresses.Clear();

    SetLoadingBlackscreenHold(true);

    if (playerController == null) {
      playerController = ResolvePlayerAnimationController();
    }

    if (playerController != null) {
      var playerMaxAnimations = Mathf.Max(preUnlockPlayerAnimationStarts, 1);
      playerController.CollectWarmPlaybackAddresses(
        addresses,
        seenAddresses,
        passCount: passes,
        maxAnimations: playerMaxAnimations,
        maxAddresses: maxAddresses
      );
      warmedControllers++;
      if (warmedControllers % controllersPerFrame == 0) {
        yield return null;
      }
    }

    if (preUnlockWarmEnemyAnimationPlayback) {
      if (enemyControllers == null || enemyControllers.Count <= 0) {
        BuildEnemyAnimationControllerSnapshot(preUnlockEnemyControllerScratch);
        enemyControllers = preUnlockEnemyControllerScratch;
      }

      var maxEnemies = ResolvePreUnlockEnemyControllerCap(enemyControllers != null ? enemyControllers.Count : 0);
      var enemyMaxAnimations = Mathf.Max(preUnlockEnemyAnimationStartsPerController, 0);
      var warmedEnemies = 0;
      for (var i = 0; i < enemyControllers.Count; i++) {
        if (maxEnemies > 0 && warmedEnemies >= maxEnemies) break;
        if (enemyMaxAnimations <= 0) break;
        var controller = enemyControllers[i];
        if (controller == null) continue;
        controller.CollectWarmPlaybackAddresses(
          addresses,
          seenAddresses,
          passCount: passes,
          maxAnimations: enemyMaxAnimations,
          maxAddresses: maxAddresses
        );
        warmedControllers++;
        warmedEnemies++;
        if (addresses.Count >= maxAddresses) break;
        if (warmedControllers % controllersPerFrame == 0) {
          yield return null;
        }
      }
    }

    if (addresses.Count > 0) {
      yield return PreloadAnimationAddressBatch(addresses, resetLoadingProgress: true);
      yield return WaitForPreUnlockWarmupQueueSettle();
    }
  }

  IEnumerator PreloadAllControllerAnimationFrames(
    AnimationController playerController = null,
    List<AnimationController> enemyControllers = null
  ) {
    if (!Application.isPlaying) yield break;

    if (playerController == null) {
      playerController = ResolvePlayerAnimationController();
    }

    if (enemyControllers == null) {
      BuildEnemyAnimationControllerSnapshot(preUnlockEnemyControllerScratch);
      enemyControllers = preUnlockEnemyControllerScratch;
    }

    BuildControllerAnimationFrameAddressSnapshot(
      playerController,
      enemyControllers,
      preUnlockAddressScratch
    );

    yield return PreloadAnimationAddressBatch(preUnlockAddressScratch, resetLoadingProgress: true);
  }

  AnimationController ResolvePlayerAnimationController() {
    var player = ResolvePlayerGearController();
    if (player == null) return null;
    return player.Controller;
  }

  void BuildEnemyAnimationControllerSnapshot(List<AnimationController> outControllers) {
    if (outControllers == null) return;
    outControllers.Clear();

    var activeEnemies = ResolveActiveEnemyControllers();
    if (activeEnemies.Length <= 0) return;

    var player = ResolvePlayerGearController();
    var hasPlayer = player != null;
    var playerPosition = hasPlayer ? player.transform.position : Vector3.zero;
    var maxDistance = Mathf.Max(preUnlockAnimationWarmupEnemyDistance, 0f);
    var maxDistanceSqr = maxDistance > 0f ? maxDistance * maxDistance : -1f;

    // Collect filtered enemies with their squared distances, then sort nearest-first so
    // their animation addresses land at the front of the preload list (deterministic ordering).
    var filteredScratch = preUnlockFilteredEnemyScratch;
    filteredScratch.Clear();
    for (var i = 0; i < activeEnemies.Length; i++) {
      var enemy = activeEnemies[i];
      if (enemy == null || enemy.Controller == null) continue;
      var sqrDist = 0f;
      if (hasPlayer) {
        var delta = enemy.transform.position - playerPosition;
        sqrDist = delta.sqrMagnitude;
        if (maxDistanceSqr > 0f && sqrDist > maxDistanceSqr) continue;
      }
      filteredScratch.Add((sqrDist, enemy.Controller));
    }

    if (hasPlayer && filteredScratch.Count > 1) {
      filteredScratch.Sort((a, b) => a.sqrDist.CompareTo(b.sqrDist));
    }

    for (var i = 0; i < filteredScratch.Count; i++) {
      outControllers.Add(filteredScratch[i].controller);
    }
  }

  void BuildControllerAnimationFrameAddressSnapshot(
    AnimationController playerController,
    List<AnimationController> enemyControllers,
    List<string> outAddresses
  ) {
    if (outAddresses == null) return;

    var maxAddresses = ResolvePreUnlockMaxAddresses(TextureResidencyCache.GetQueueSnapshot(pump: false));
    var seenAddresses = preUnlockSeenAddressScratch;
    outAddresses.Clear();
    seenAddresses.Clear();
    var animationFrames = Mathf.Max(preUnlockPrefetchAnimationFrames, 1);

    if (playerController != null) {
      playerController.CollectAnimationStartAddresses(
        outAddresses,
        seenAddresses,
        framesPerAnimation: animationFrames,
        maxAnimations: Mathf.Max(preUnlockPlayerAnimationStarts, 1),
        maxAddresses: maxAddresses
      );
    }
    preUnlockLastPlayerAddressCount = outAddresses.Count;

    if (outAddresses.Count < maxAddresses) {
      var enemyMaxAnimations = Mathf.Max(preUnlockEnemyAnimationStartsPerController, 0);
      if (enemyMaxAnimations <= 0) return;
      if (enemyControllers != null) {
        for (var i = 0; i < enemyControllers.Count; i++) {
          if (outAddresses.Count >= maxAddresses) break;
          var controller = enemyControllers[i];
          if (controller == null) continue;
          controller.CollectAnimationStartAddresses(
            outAddresses,
            seenAddresses,
            framesPerAnimation: animationFrames,
            maxAnimations: enemyMaxAnimations,
            maxAddresses: maxAddresses
          );
        }
      }
      else {
        var activeEnemies = ResolveActiveEnemyControllers();
        for (var i = 0; i < activeEnemies.Length; i++) {
          if (outAddresses.Count >= maxAddresses) break;
          var enemy = activeEnemies[i];
          if (enemy == null || enemy.Controller == null) continue;
          enemy.Controller.CollectAnimationStartAddresses(
            outAddresses,
            seenAddresses,
            framesPerAnimation: animationFrames,
            maxAnimations: enemyMaxAnimations,
            maxAddresses: maxAddresses
          );
        }
      }
    }
  }

  IEnumerator PreloadAnimationAddressBatch(List<string> addresses, bool resetLoadingProgress) {
    if (addresses == null || addresses.Count <= 0) yield break;
    AccumulatePreUnlockResidentPins(addresses);

    if (resetLoadingProgress) {
      ResetLoadingProgressForPhase();
    }

    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var enqueueBudget = ResolvePreUnlockEnqueueBudget(queue);

    // Process in batches to avoid stalling the main thread while filling the request queue.
    // Matches Aguide.txt "Batched load" guidance (50-200).
    // Tune this value based on platform performance (lower for mobile).
    const int BatchSize = 100;
    var processedCount = 0;

    while (processedCount < addresses.Count) {
      var remaining = addresses.Count - processedCount;
      var count = Mathf.Min(BatchSize, remaining);
      var chunkStart = processedCount;
      yield return TextureResidencyCache.RequestLoadBatchThrottled(
        EnumerateAddressRange(addresses, chunkStart, count),
        TextureResidencyCache.LoadPriority.Warmup,
        // Atlas-first preload: ensure sibling slices from the same atlas are resident before gameplay unlock.
        allowAtlasExpansion: true,
        enqueueBudgetPerFrame: enqueueBudget
      );

      processedCount += count;
      yield return WaitForPreUnlockWarmupQueueSettle();

      queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      enqueueBudget = ResolvePreUnlockEnqueueBudget(queue);
    }
  }

  SpriteWithNormals[] ResolvePreUnlockVisibleSpriteTargets() {
    var now = Time.realtimeSinceStartup;
    var refreshSeconds = Mathf.Max(preUnlockTargetCacheRefreshSeconds, 0f);
    var hasCache = preUnlockVisibleSpriteTargetsCache != null && preUnlockVisibleSpriteTargetsCache.Length > 0;
    var cacheExpired = refreshSeconds <= 0f ||
      preUnlockVisibleSpriteTargetsCacheRefreshedAt < 0f ||
      (now - preUnlockVisibleSpriteTargetsCacheRefreshedAt) >= refreshSeconds;
    if (hasCache && !cacheExpired) {
      return preUnlockVisibleSpriteTargetsCache;
    }

    preUnlockVisibleSpriteTargetsCache =
      FindObjectsByType<SpriteWithNormals>(FindObjectsInactive.Exclude, FindObjectsSortMode.None) ??
      Array.Empty<SpriteWithNormals>();
    preUnlockVisibleSpriteTargetsCacheRefreshedAt = now;
    return preUnlockVisibleSpriteTargetsCache;
  }

  void InvalidatePreUnlockTargetCache() {
    preUnlockVisibleSpriteTargetsCache = Array.Empty<SpriteWithNormals>();
    preUnlockVisibleSpriteTargetsCacheRefreshedAt = -1f;
  }

  int ResolvePreUnlockMaxAddresses(TextureResidencyCache.QueueSnapshot queue) {
    var configuredMax = Mathf.Max(preUnlockPrefetchMaxAddresses, 1);
    var configuredMin = Mathf.Clamp(preUnlockPrefetchMinAddresses, 1, configuredMax);
    var scale = 1f;

    if (SystemInfo.systemMemorySize <= 8192) {
      scale = 0.45f;
    }
    else if (SystemInfo.systemMemorySize <= 12288) {
      scale = 0.65f;
    }
    else if (SystemInfo.systemMemorySize <= 16384) {
      scale = 0.8f;
    }

    if (queue.queuedCount >= 1400 || queue.inFlightCount >= 192) {
      scale *= 0.5f;
    }
    else if (queue.queuedCount >= 900 || queue.inFlightCount >= 128) {
      scale *= 0.7f;
    }

    var scaled = Mathf.RoundToInt(configuredMax * scale);
    return Mathf.Clamp(scaled, configuredMin, configuredMax);
  }

  int ResolvePreUnlockLookAheadFrames(TextureResidencyCache.QueueSnapshot queue) {
    var lookAhead = Mathf.Max(preUnlockPrefetchLookAheadFrames, 0);
    if (lookAhead <= 0) return 0;
    if (queue.queuedCount >= 1400 || queue.inFlightCount >= 192) return 0;
    if (queue.queuedCount >= 900 || queue.inFlightCount >= 128) return Mathf.Min(lookAhead, 1);
    return lookAhead;
  }

  int ResolvePreUnlockPlaybackPasses() {
    var passes = Mathf.Max(preUnlockAnimationPlaybackPasses, 1);
    if (passes <= 1) return 1;

    if (SystemInfo.systemMemorySize <= 12288) return 1;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    if (queue.queuedCount >= 900 || queue.inFlightCount >= 128) return 1;
    return passes;
  }

  int ResolvePreUnlockEnemyControllerCap(int availableCount) {
    if (availableCount <= 0) return 0;
    if (preUnlockAnimationWarmupMaxEnemyControllers > 0) {
      return Mathf.Min(preUnlockAnimationWarmupMaxEnemyControllers, availableCount);
    }

    var autoCap = 6;
    if (SystemInfo.systemMemorySize <= 8192) {
      autoCap = 2;
    }
    else if (SystemInfo.systemMemorySize <= 12288) {
      autoCap = 4;
    }
    return Mathf.Min(autoCap, availableCount);
  }

  int ResolvePreUnlockEnqueueBudget(TextureResidencyCache.QueueSnapshot queue) {
    var budget = Mathf.Clamp(preUnlockPrefetchEnqueueBudgetPerFrame, 50, 200);
    if (queue.queuedCount >= 1400 || queue.inFlightCount >= 192) return Mathf.Min(budget, 60);
    if (queue.queuedCount >= 900 || queue.inFlightCount >= 128) return Mathf.Min(budget, 90);
    if (queue.queuedCount >= 500 || queue.inFlightCount >= 64) return Mathf.Min(budget, 120);
    return budget;
  }

  int ResolveWarmupPriorityPrefixCount(int totalAddressCount, TextureResidencyCache.QueueSnapshot queue, int playerAddressCount = 0) {
    if (totalAddressCount <= 0) return 0;
    // TODO(smooth-first-play): Replace static queue thresholds with observed first-frame miss rate
    // so prefix sizing adapts to real animation smoothness.
    int queueBasedCount;
    if (queue.queuedCount >= 1400 || queue.inFlightCount >= 192) {
      queueBasedCount = Mathf.Clamp(totalAddressCount / 3, 32, totalAddressCount);
    }
    else if (queue.queuedCount >= 900 || queue.inFlightCount >= 128) {
      queueBasedCount = Mathf.Clamp((totalAddressCount * 2) / 3, 64, totalAddressCount);
    }
    else {
      return totalAddressCount;
    }
    // Floor at player address count so all player-critical sprites always get Warmup priority
    // regardless of queue pressure, reflecting measured time-to-first-ready-frame for the player.
    return Mathf.Clamp(Mathf.Max(queueBasedCount, playerAddressCount), min: 0, max: totalAddressCount);
  }

  IEnumerable<string> EnumerateAddressRange(List<string> addresses, int startInclusive, int count) {
    if (addresses == null || addresses.Count <= 0 || count <= 0) yield break;
    var start = Mathf.Clamp(startInclusive, 0, addresses.Count);
    var end = Mathf.Clamp(start + count, start, addresses.Count);
    for (var i = start; i < end; i++) {
      var address = addresses[i];
      if (string.IsNullOrWhiteSpace(address)) continue;
      yield return address;
    }
  }

  float ResolveRequiredWarmRatio(WarmGateMode context, float configuredRatio, EnemyController[] activeEnemies) {
    var ratio = Mathf.Clamp(configuredRatio, 0.5f, 0.99f);
    if (context != WarmGateMode.LoadSave) return ratio;

    var cap = Mathf.Clamp(Mathf.Max(loadSaveWarmRequiredRatioCap, 0.95f), 0.5f, 0.99f);
    var floor = Mathf.Clamp(Mathf.Max(loadSaveWarmRequiredRatioFloor, 0.9f), 0.5f, 0.99f);
    ratio = Mathf.Min(ratio, cap);
    if (SystemInfo.systemMemorySize <= 8192) {
      ratio -= 0.03f;
    }
    else if (SystemInfo.systemMemorySize <= 12288) {
      ratio -= 0.015f;
    }

    var enemyCount = activeEnemies != null ? activeEnemies.Length : 0;
    if (enemyCount >= 10) {
      ratio -= 0.03f;
    }
    else if (enemyCount >= 5) {
      ratio -= 0.015f;
    }

    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    if (queue.queuedCount >= 900 || queue.inFlightCount >= 128) {
      ratio -= 0.02f;
    }

    return Mathf.Clamp(ratio, floor, 0.99f);
  }

  void LogWarmRequestScope(
    WarmGateMode context,
    List<string> criticalLibraries,
    List<string> criticalAddresses,
    List<string> warmLibraries,
    List<string> warmAddresses,
    List<string> criticalLabels,
    List<string> warmLabels,
    EnemyController[] criticalEnemies,
    List<string> criticalPlayerEffectKeys,
    float requiredRatio
  ) {
    if (!ShouldLogLoadingProgressDebug()) return;
    Debug.Log(
      "[SingleSceneManager][WarmScope] context=" + context +
      " blocking_libraries=" + (criticalLibraries != null ? criticalLibraries.Count : 0) +
      " blocking_addresses=" + (criticalAddresses != null ? criticalAddresses.Count : 0) +
      " blocking_labels=" + (criticalLabels != null ? criticalLabels.Count : 0) +
      " background_libraries=" + (warmLibraries != null ? warmLibraries.Count : 0) +
      " background_addresses=" + (warmAddresses != null ? warmAddresses.Count : 0) +
      " background_labels=" + (warmLabels != null ? warmLabels.Count : 0) +
      " critical_enemies=" + (criticalEnemies != null ? criticalEnemies.Length : 0) +
      " critical_player_effects=" + (criticalPlayerEffectKeys != null ? criticalPlayerEffectKeys.Count : 0) +
      " required_ratio=" + requiredRatio.ToString("0.000")
    );
  }

  WarmRequest BuildWarmRequest(
    WarmGateMode context,
    float timeoutSeconds,
    float requiredRatio,
    GearController playerController,
    EnemyController[] activeEnemies
  ) {
    var profile = LocationWarmRegistryRuntime.ResolveForLocation(LocationManager.currentLocation);
    var criticalLibraries = new List<string>();
    var criticalAddresses = new List<string>();
    var warmLibraries = new List<string>();
    var warmAddresses = new List<string>();
    var criticalLabels = new List<string>();
    var warmLabels = new List<string>();
    var archetypes = ResolveLocationArchetypePrefabs(profile);
    var combatPopulationTypes = ResolveCombatPopulationWarmTypes(activeEnemies, archetypes);
    if (profile != null) {
      profile.CollectGameplayWarmLists(
        combatPopulationTypes,
        criticalLibraries,
        criticalAddresses,
        warmLibraries,
        warmAddresses,
        criticalLabels,
        warmLabels
      );
    }

    var criticalEnemies = ResolveCriticalWarmEnemies(activeEnemies, playerController);
    var criticalPlayerEffectKeys = ResolveCriticalPlayerEffectKeys(playerController);
    var token = StreamingWarmOrchestrator.BuildEnemyArchetypeToken(LocationManager.currentLocation, archetypes);
    var tunedRequiredRatio = ResolveRequiredWarmRatio(context, requiredRatio, activeEnemies);
    LogWarmRequestScope(
      context,
      criticalLibraries,
      criticalAddresses,
      warmLibraries,
      warmAddresses,
      criticalLabels,
      warmLabels,
      criticalEnemies,
      criticalPlayerEffectKeys,
      tunedRequiredRatio
    );

    if (context == WarmGateMode.LoadSave) {
      return WarmRequest.CreateLoadSave(
        playerController: playerController,
        criticalEnemyControllers: criticalEnemies,
        enemyControllers: activeEnemies,
        enemyArchetypePrefabsByType: archetypes,
        timeoutSeconds: timeoutSeconds,
        requiredReadyRatio: tunedRequiredRatio,
        extraCriticalLibraries: criticalLibraries,
        extraCriticalAddresses: criticalAddresses,
        extraCriticalLabels: criticalLabels,
        extraWarmLibraries: warmLibraries,
        extraWarmAddresses: warmAddresses,
        extraWarmLabels: warmLabels,
        hardTimeoutSeconds: Mathf.Max(startWarmHardTimeoutSeconds, timeoutSeconds, 3.0f),
        allowHardTimeoutBypass: allowHardTimeoutBypass,
        idempotencyToken: token,
        skipIfTokenAlreadyWarm: true,
        criticalPlayerEffectKeys: criticalPlayerEffectKeys,
        allowCriticalReadySoftTimeout: false
      );
    }

    if (context == WarmGateMode.GearApplyReturn) {
      return WarmRequest.CreateGearApplyReturn(
        playerController: playerController,
        timeoutSeconds: timeoutSeconds,
        requiredReadyRatio: tunedRequiredRatio,
        extraCriticalLibraries: criticalLibraries,
        extraCriticalAddresses: criticalAddresses,
        extraCriticalLabels: criticalLabels,
        extraWarmLibraries: warmLibraries,
        extraWarmAddresses: warmAddresses,
        extraWarmLabels: warmLabels,
        hardTimeoutSeconds: Mathf.Max(gearReturnWarmHardTimeoutSeconds, timeoutSeconds, 2.5f),
        allowHardTimeoutBypass: allowHardTimeoutBypass,
        idempotencyToken: "",
        skipIfTokenAlreadyWarm: false,
        criticalPlayerEffectKeys: criticalPlayerEffectKeys,
        allowCriticalReadySoftTimeout: false
      );
    }

    return WarmRequest.CreateStartGame(
      playerController: playerController,
      criticalEnemyControllers: criticalEnemies,
      enemyControllers: activeEnemies,
      enemyArchetypePrefabsByType: archetypes,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: tunedRequiredRatio,
      extraCriticalLibraries: criticalLibraries,
      extraCriticalAddresses: criticalAddresses,
      extraCriticalLabels: criticalLabels,
      extraWarmLibraries: warmLibraries,
      extraWarmAddresses: warmAddresses,
      extraWarmLabels: warmLabels,
      hardTimeoutSeconds: Mathf.Max(startWarmHardTimeoutSeconds, timeoutSeconds, 3.0f),
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: token,
      skipIfTokenAlreadyWarm: true,
      criticalPlayerEffectKeys: criticalPlayerEffectKeys,
      allowCriticalReadySoftTimeout: false
    );
  }

  List<string> ResolveCombatPopulationWarmTypes(
    EnemyController[] activeEnemies,
    Dictionary<string, GameObject> archetypes
  ) {
    var enemyTypes = new List<string>();

    if (activeEnemies != null) {
      for (var i = 0; i < activeEnemies.Length; i++) {
        var enemy = activeEnemies[i];
        if (enemy == null) continue;
        AddUniqueCombatPopulationType(enemyTypes, enemy.enemyType);
      }
    }

    if (enemyTypes.Count <= 0 && archetypes != null && archetypes.Count > 0) {
      foreach (var pair in archetypes) {
        AddUniqueCombatPopulationType(enemyTypes, pair.Key);
      }
    }

    if (enemyTypes.Count <= 0 &&
        LocationEnemyData.TryGetLocation(LocationManager.currentLocation, out var locationInfo) &&
        locationInfo != null &&
        locationInfo.enemies != null) {
      for (var i = 0; i < locationInfo.enemies.Count; i++) {
        AddUniqueCombatPopulationType(enemyTypes, locationInfo.enemies[i]);
      }
    }

    return enemyTypes;
  }

  static void AddUniqueCombatPopulationType(List<string> output, string enemyType) {
    if (output == null || string.IsNullOrWhiteSpace(enemyType)) return;
    var normalized = enemyType.Trim();
    for (var i = 0; i < output.Count; i++) {
      if (string.Equals(output[i], normalized, StringComparison.OrdinalIgnoreCase)) {
        return;
      }
    }
    output.Add(normalized);
  }

  EnemyController[] ResolveCriticalWarmEnemies(EnemyController[] activeEnemies, GearController playerController) {
    var maxCriticalEnemies = Mathf.Max(warmGateCriticalEnemyCount, 0);
    if (maxCriticalEnemies <= 0 || activeEnemies == null || activeEnemies.Length <= 0) {
      return Array.Empty<EnemyController>();
    }

    var hasPlayer = playerController != null;
    var playerPosition = hasPlayer ? playerController.transform.position : Vector3.zero;
    var maxDistance = Mathf.Max(warmGateCriticalEnemyDistance, 0f);
    var maxDistanceSqr = maxDistance > 0f ? maxDistance * maxDistance : -1f;
    var filteredEnemies = warmGateCriticalEnemyScratch;
    filteredEnemies.Clear();

    for (var i = 0; i < activeEnemies.Length; i++) {
      var enemy = activeEnemies[i];
      if (enemy == null || enemy.Controller == null) continue;
      var sqrDist = 0f;
      if (hasPlayer) {
        var delta = enemy.transform.position - playerPosition;
        sqrDist = delta.sqrMagnitude;
        if (maxDistanceSqr > 0f && sqrDist > maxDistanceSqr) continue;
      }
      filteredEnemies.Add((sqrDist, enemy));
    }

    if (filteredEnemies.Count <= 0) {
      return Array.Empty<EnemyController>();
    }

    if (hasPlayer && filteredEnemies.Count > 1) {
      filteredEnemies.Sort((a, b) => a.sqrDist.CompareTo(b.sqrDist));
    }

    var count = Mathf.Min(maxCriticalEnemies, filteredEnemies.Count);
    var criticalEnemies = new EnemyController[count];
    for (var i = 0; i < count; i++) {
      criticalEnemies[i] = filteredEnemies[i].enemy;
    }
    return criticalEnemies;
  }

  List<string> ResolveCriticalPlayerEffectKeys(GearController playerController) {
    if (!warmGatePreloadCorePlayerEffects || playerController == null || playerController.effectNode == null) {
      return null;
    }

    var keys = new List<string>(CorePlayerEffectWarmKeys.Length);
    for (var i = 0; i < CorePlayerEffectWarmKeys.Length; i++) {
      var key = CorePlayerEffectWarmKeys[i];
      if (string.IsNullOrWhiteSpace(key)) continue;
      keys.Add(key);
    }
    return keys.Count > 0 ? keys : null;
  }

  Dictionary<string, GameObject> ResolveLocationArchetypePrefabs(LocationWarmProfile profile) {
    var map = profile != null ? profile.BuildEnemyArchetypePrefabMap() : new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
    if (map.Count > 0) return map;

    var spawner = FindFirstObjectByType<Spawner>();
    if (spawner != null) {
      var fallback = spawner.BuildCurrentLocationArchetypeMapForWarmup();
      if (fallback != null && fallback.Count > 0) return fallback;
    }

    return map;
  }

  GearController ResolvePlayerGearController() {
    if (cachedPlayerGearController != null) return cachedPlayerGearController;
    cachedPlayerGearController = FindFirstObjectByType<GearController>();
    if (cachedPlayerGearController != null) return cachedPlayerGearController;

    var all = Resources.FindObjectsOfTypeAll<GearController>();
    for (var i = 0; i < all.Length; i++) {
      var candidate = all[i];
      if (candidate == null) continue;
      if (!candidate.gameObject.scene.IsValid()) continue;
      if ((candidate.hideFlags & HideFlags.HideAndDontSave) != 0) continue;
      cachedPlayerGearController = candidate;
      break;
    }
    return cachedPlayerGearController;
  }

  bool ShouldWarmGearReturn() {
    if (!useScenarioWarmGate || !Application.isPlaying) return false;
    var gear = ResolvePlayerGearController();
    if (gear == null) return false;
    if (pauseMenuOpenAppearanceRevision < 0) return true;
    return gear.AppearanceRevision != pauseMenuOpenAppearanceRevision;
  }

  EnemyController[] ResolveActiveEnemyControllers() {
    var now = Time.unscaledTime;
    if (activeEnemyControllersCacheRefreshedAt >= 0f &&
        now - activeEnemyControllersCacheRefreshedAt < ActiveEnemyControllersCacheRefreshSeconds) {
      return activeEnemyControllersCache;
    }

    var enemies = FindObjectsByType<EnemyController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
    activeEnemyControllersCache = enemies != null && enemies.Length > 0 ? enemies : Array.Empty<EnemyController>();
    activeEnemyControllersCacheRefreshedAt = now;
    return activeEnemyControllersCache;
  }

  void InvalidateActiveEnemyControllersCache() {
    activeEnemyControllersCache = Array.Empty<EnemyController>();
    activeEnemyControllersCacheRefreshedAt = -1f;
  }

  void ApplyGameplayStateUnderBlack() {
    if (LoadingScreen != null && !LoadingScreen.activeSelf) {
      LoadingScreen.SetActive(true);
    }
    RequestLocationLoadForGameplay(LocationManager.currentLocation);
    SetLoadingBlackscreenHold(true);
    _SwitchMap("none");
    SetActiveSafe(MainMenu, false);
    SetActiveSafe(SettingsMenu, false);
    SetActiveSafe(LoadMenu, false);
    SetActiveSafe(GameplayInterface, true);
    SetActiveSafe(PauseMenu, false);
    SetActiveSafe(Scene, true);
    SetSceneObjectLightsActive(false);
    pauseMenuOpenAppearanceRevision = -1;
  }

  void UnlockGameplayFromBlack() {
    FinalizeLoadingProgressForRelease();
    SetSceneObjectLightsActive(true);
    SetLoadingBlackscreenHold(false);
    if (blackscreen != null) {
      PlayBlackscreen("alphaOut");
    }
    else {
      ForceBlackscreenVisible(false);
    }
    if (unlockFadeFailSafeRoutine != null) {
      StopCoroutine(unlockFadeFailSafeRoutine);
    }
    unlockFadeFailSafeRoutine = StartCoroutine(EnsureBlackscreenClearsAfterUnlockRoutine());
    _SwitchMap("gameplay");
    SpriteStreamingLoadingState.EndLoadingOverlay("Gameplay");
  }

  void PlayBlackscreen(string animationName) {
    if (blackscreen == null) return;
    if (holdBlackscreenOpaqueDuringLoad && string.Equals(animationName, "alphaOut", StringComparison.Ordinal)) {
      return;
    }
    if (string.Equals(animationName, "alphaOut", StringComparison.Ordinal)) {
      loadingOverlayChildrenReady = false;
      SetLoadingOverlayChildrenActive(false);
      SetLoadingText("");
    }
    if (loadingBlackscreenRenderer != null) {
      var c = loadingBlackscreenRenderer.color;
      if (!Mathf.Approximately(c.a, 1f)) {
        c.a = 1f;
        loadingBlackscreenRenderer.color = c;
      }
    }
    blackscreen.Play(animationName);
  }

  IEnumerator StartupFadeWatchdogRoutine() {
    var wait = Mathf.Max(startupFadeWatchdogSeconds, 0f);
    if (wait > 0f) {
      yield return new WaitForSecondsRealtime(wait);
    }
    if (IsLoadingFlowActive()) {
      startupFadeWatchdogRoutine = null;
      yield break;
    }
    if (ShouldRunStartupGameplayWarmFlow()) {
      if (startupGameplayRoutine == null) {
        startupGameplayRoutine = StartCoroutine(StartupGameplayFlowRoutine());
      }
      startupFadeWatchdogRoutine = null;
      yield break;
    }

    SetLoadingBlackscreenHold(false);
    ForceBlackscreenVisible(false);
    SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    ApplyStartupInputFallbackAfterWatchdog();
    startupFadeWatchdogRoutine = null;
  }

  bool IsLoadingFlowActive() {
    if (startGameRoutine != null || resumeGameplayRoutine != null || startupGameplayRoutine != null) return true;
    return SpriteStreamingLoadingState.IsLoadingOverlayActive;
  }

  void StopStartupFadeWatchdog() {
    if (startupFadeWatchdogRoutine == null) return;
    StopCoroutine(startupFadeWatchdogRoutine);
    startupFadeWatchdogRoutine = null;
  }

  void StopStartupGameplayFlow() {
    if (startupGameplayRoutine == null) return;
    StopCoroutine(startupGameplayRoutine);
    startupGameplayRoutine = null;
    SpriteStreamingLoadingState.ForceClearLoadingOverlay();
  }

  void ApplyStartupInputFallbackAfterWatchdog() {
    if (startGameRoutine != null || resumeGameplayRoutine != null || startupGameplayRoutine != null) {
      return;
    }
    ApplyInputMapForCurrentUiState(preferGameplayWhenNoUi: false);
  }

  bool ShouldRunStartupGameplayWarmFlow() {
    return false;
  }

  void ApplyConfiguredStartupMode() {
    ReleasePreUnlockResidentPins("startup_mode_main_menu");
    RequestLocationLoadForMainMenu();
    ApplyUiModeActivations(UiMode.MainMenu);
    pauseMenuOpenAppearanceRevision = -1;
    SpriteStreamingLoadingState.EndLoadingOverlay("MainMenuStartup");
  }

  void ApplyInputMapForCurrentUiState(bool preferGameplayWhenNoUi) {
    if (PauseMenu != null && PauseMenu.activeInHierarchy) {
      _SwitchMap("pauseMenu");
      return;
    }
    if (SettingsMenu != null && SettingsMenu.activeInHierarchy) {
      _SwitchMap("settingsMenu");
      return;
    }
    if (LoadMenu != null && LoadMenu.activeInHierarchy) {
      _SwitchMap("loadMenu");
      return;
    }
    if (MainMenu != null && MainMenu.activeInHierarchy) {
      _SwitchMap("mainMenu");
      return;
    }
    if (GameplayInterface != null && GameplayInterface.activeInHierarchy) {
      _SwitchMap("gameplay");
      return;
    }
    if (preferGameplayWhenNoUi && HasLiveGameplayInput()) {
      _SwitchMap("gameplay");
      return;
    }
    if (preferGameplayWhenNoUi) {
      _SwitchMap("mainMenu");
    }
  }

  bool HasLiveGameplayInput() {
    if (IsGameplayInputLive(cachedGameplayInput)) return true;

    var now = Time.unscaledTime;
    if (gameplayInputCacheRefreshedAt >= 0f &&
        now - gameplayInputCacheRefreshedAt < GameplayInputCacheRefreshSeconds) {
      return false;
    }

    gameplayInputCacheRefreshedAt = now;
    cachedGameplayInput = FindFirstObjectByType<GameplayInput>();
    return IsGameplayInputLive(cachedGameplayInput);
  }

  static bool IsGameplayInputLive(GameplayInput gameplayInput) {
    return gameplayInput != null &&
           gameplayInput.enabled &&
           gameplayInput.gameObject.activeInHierarchy;
  }

  void ResolveAndApplyLocationForStart(bool isNewGame, SaveData loadedSlot) {
    var resolved = ResolveDefaultLocation();

    if (!isNewGame && loadedSlot != null && loadedSlot.ContainsKey("location")) {
      var loadedLocation = Convert.ToString(loadedSlot["location"]);
      if (IsKnownLocation(loadedLocation)) {
        resolved = loadedLocation.Trim();
      }
    }

    if (!string.Equals(LocationManager.currentLocation, resolved, StringComparison.OrdinalIgnoreCase)) {
      LocationManager.UpdateLocation(resolved);
    }
  }

  bool IsKnownLocation(string location) {
    return LocationEnemyData.ContainsLocation(location);
  }

  string ResolveDefaultLocation() {
    if (IsKnownLocation(defaultStartLocation)) return defaultStartLocation.Trim();
    if (IsKnownLocation(gameplayFlowFallbackLocationId)) return gameplayFlowFallbackLocationId;
    return LocationEnemyData.GetDefaultLocation();
  }

  void RequestLocationLoadForMainMenu() {
    RequestLocationLoad(mainMenuFlowLocationId);
  }

  void RequestLocationLoadForGameplay(string preferredLocationId) {
    var locationId = string.IsNullOrWhiteSpace(preferredLocationId) ? gameplayFlowFallbackLocationId : preferredLocationId.Trim();
    if (!IsKnownLocation(locationId)) {
      locationId = ResolveDefaultLocation();
    }
    RequestLocationLoad(locationId);
  }

  void RequestLocationLoad(string locationId) {
    var resolved = LocationEnemyData.ResolveRequestedOrDefault(locationId);
    if (string.IsNullOrWhiteSpace(resolved)) return;
    if (!string.Equals(LocationManager.currentLocation, resolved, StringComparison.OrdinalIgnoreCase)) {
      LocationManager.UpdateLocation(resolved);
    }
    ResetLoadingProgressForPhase();
    MessageBus.Send("RequestLocationLoad", resolved);
  }

  void OnLocationUpdated(object payload) {
    if (!Application.isPlaying) return;
    InvalidatePreUnlockTargetCache();
    InvalidateActiveEnemyControllersCache();
    cachedGameplayInput = null;
    gameplayInputCacheRefreshedAt = -1f;

    var locationId = payload as string;
    if (string.IsNullOrWhiteSpace(locationId)) {
      locationId = LocationManager.currentLocation;
    }
    locationId = string.IsNullOrWhiteSpace(locationId) ? "" : locationId.Trim();
    if (string.Equals(lastPurgedLocationId, locationId, StringComparison.OrdinalIgnoreCase)) return;
    var previousLocationId = lastPurgedLocationId;
    lastPurgedLocationId = locationId;
    HandleLocationCacheTransition(previousLocationId, locationId);
  }

  void HandleLocationCacheTransition(string previousLocationId, string currentLocationId) {
    if (string.IsNullOrWhiteSpace(previousLocationId)) return;
    if (string.Equals(previousLocationId, currentLocationId, StringComparison.OrdinalIgnoreCase)) return;

    // Location transition lifecycle: old environment/enemy/effect sets are no longer
    // required once switching to a different location. Evict completed unpinned entries.
    TextureResidencyCache.EvictAllUnpinnedCompleted();
  }

  void ForceBlackscreenVisible(bool visible) {
    if (loadingBlackscreen == null && LoadingScreen != null) {
      var blackscreenTransform = FindChildByName(LoadingScreen.transform, "blackscreen");
      if (blackscreenTransform != null) {
        loadingBlackscreen = blackscreenTransform.gameObject;
        loadingBlackscreenRenderer = loadingBlackscreen.GetComponent<SpriteRenderer>();
      }
    }
    if (loadingBlackscreen == null) return;
    var alpha = visible ? 1f : 0f;
    var spriteRenderer = loadingBlackscreenRenderer;
    if (spriteRenderer != null) {
      var c = spriteRenderer.color;
      if (!Mathf.Approximately(c.a, alpha)) {
        c.a = alpha;
        spriteRenderer.color = c;
      }

      if (loadingBlackscreenPropertyBlock == null) {
        loadingBlackscreenPropertyBlock = new MaterialPropertyBlock();
      }
      var block = loadingBlackscreenPropertyBlock;
      spriteRenderer.GetPropertyBlock(block);
      block.SetFloat("_Alpha", alpha);
      spriteRenderer.SetPropertyBlock(block);
    }
  }

  void SetLoadingBlackscreenHold(bool hold) {
    if (holdBlackscreenOpaqueDuringLoad == hold) return;
    holdBlackscreenOpaqueDuringLoad = hold;
    if (blackscreen != null) {
      blackscreen.enabled = !hold;
    }
    if (hold) {
      ForceBlackscreenVisible(true);
    }
  }

  IEnumerator EnsureBlackscreenClearsAfterUnlockRoutine() {
    var waitSeconds = Mathf.Max(fadeFromBlackSeconds + 0.15f, 0.5f);
    if (waitSeconds > 0f) {
      yield return new WaitForSecondsRealtime(waitSeconds);
    }
    if (!holdBlackscreenOpaqueDuringLoad) {
      ForceBlackscreenVisible(false);
    }
    var pinReleaseDelay = Mathf.Max(postUnlockPinReleaseDelaySeconds, 0f);
    if (pinReleaseDelay > 0f) {
      yield return new WaitForSecondsRealtime(pinReleaseDelay);
    }
    var releaseOutstandingCap = Mathf.Max(postUnlockPinReleaseMaxOutstanding, 0);
    var releaseTimeout = Mathf.Max(postUnlockPinReleaseTimeoutSeconds, 0f);
    var releaseStartedAt = Time.realtimeSinceStartup;
    while (true) {
      TextureResidencyCache.PumpOncePerFrame();
      var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
      var outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount + deferredPending, 0);
      if (outstanding <= releaseOutstandingCap) break;
      if (releaseTimeout > 0f && (Time.realtimeSinceStartup - releaseStartedAt) >= releaseTimeout) break;
      yield return null;
    }
    ReleasePreUnlockResidentPins("post_unlock");
    unlockFadeFailSafeRoutine = null;
  }

  void TickLoadingStallEmergencyUnlock() {
    if (!enableLoadingStallEmergencyUnlock || !Application.isPlaying) {
      loadingStallStartedAt = -1f;
      return;
    }

    var loadingActive = holdBlackscreenOpaqueDuringLoad || SpriteStreamingLoadingState.IsLoadingOverlayActive;
    if (!loadingActive) {
      loadingStallStartedAt = -1f;
      return;
    }

    TextureResidencyCache.Pump();
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    var queueFullyDrained = queue.queuedCount <= 0 && queue.inFlightCount <= 0 && deferredPending <= 0;
    if (!queueFullyDrained) {
      loadingStallStartedAt = -1f;
      return;
    }

    if (loadingStallStartedAt < 0f) {
      loadingStallStartedAt = Time.realtimeSinceStartup;
      return;
    }

    var elapsed = Time.realtimeSinceStartup - loadingStallStartedAt;
    var timeout = Mathf.Max(loadingStallEmergencyUnlockSeconds, 1f);
    if (elapsed < timeout) return;

    ForceReleaseLoadingState();
    loadingStallStartedAt = -1f;
  }

  void ForceReleaseLoadingState() {
    if (uiModeRoutine != null) {
      StopCoroutine(uiModeRoutine);
      uiModeRoutine = null;
    }
    if (startGameRoutine != null) {
      StopCoroutine(startGameRoutine);
      startGameRoutine = null;
    }
    if (resumeGameplayRoutine != null) {
      StopCoroutine(resumeGameplayRoutine);
      resumeGameplayRoutine = null;
    }
    if (startupGameplayRoutine != null) {
      StopCoroutine(startupGameplayRoutine);
      startupGameplayRoutine = null;
    }
    if (unlockFadeFailSafeRoutine != null) {
      StopCoroutine(unlockFadeFailSafeRoutine);
      unlockFadeFailSafeRoutine = null;
    }

    FinalizeLoadingProgressForRelease();
    ReleasePreUnlockResidentPins("force_release");
    SetLoadingBlackscreenHold(false);
    ForceBlackscreenVisible(false);
    RestoreSceneLightingForCurrentActivation();
    SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    ApplyInputMapForCurrentUiState(preferGameplayWhenNoUi: false);
  }

  void ForceGameplayUnlockFallback() {
    if (LoadingScreen != null && !LoadingScreen.activeSelf) {
      LoadingScreen.SetActive(true);
    }
    ApplyGameplayStateUnderBlack();
    UnlockGameplayFromBlack();
  }

  void SetUiMode(UiMode mode, bool waitForStreamingIdle = true) {
    if (uiModeRoutine != null) {
      StopCoroutine(uiModeRoutine);
      uiModeRoutine = null;
      // Prior routine may have disabled lights before reaching FadeFromBlackRoutine.
      RestoreSceneLightingForCurrentActivation();
    }
    uiModeRoutine = StartCoroutine(SwitchUiModeRoutine(mode, waitForStreamingIdle));
  }

  string BuildUiOverlayTag(UiMode mode) {
    return "UiMode_" + mode;
  }

  IEnumerator SwitchUiModeRoutine(UiMode mode, bool waitForStreamingIdle = true) {
    var overlayTag = BuildUiOverlayTag(mode);
    SpriteStreamingLoadingState.BeginLoadingOverlay(overlayTag);
    ResetLoadingProgressForPhase(force: true);
    yield return FadeToBlackBeforeLoadRoutine();
    ApplyUiModeActivations(mode);
    ApplyInputForUiMode(mode);
    if (waitForStreamingIdle) {
      yield return WaitForStreamingIdleBeforeUnlock();
    }
    yield return FadeFromBlackRoutine(overlayTag);
    uiModeRoutine = null;
  }

  void ApplyUiModeActivations(UiMode mode) {
    switch (mode) {
      case UiMode.MainMenu:
        RequestLocationLoadForMainMenu();
        SetActiveSafe(MainMenu, true);
        SetActiveSafe(LoadMenu, false);
        SetActiveSafe(SettingsMenu, false);
        SetActiveSafe(GameplayInterface, false);
        SetActiveSafe(PauseMenu, false);
        SetActiveSafe(Scene, false);
        SetSceneObjectLightsActive(false);
        break;
      case UiMode.Gameplay:
        SetActiveSafe(MainMenu, false);
        SetActiveSafe(LoadMenu, false);
        SetActiveSafe(SettingsMenu, false);
        SetActiveSafe(PauseMenu, false);
        SetActiveSafe(GameplayInterface, true);
        SetActiveSafe(Scene, true);
        SetSceneObjectLightsActive(false);
        pauseMenuOpenAppearanceRevision = -1;
        break;
      case UiMode.Pause:
        SetActiveSafe(MainMenu, false);
        SetActiveSafe(GameplayInterface, false);
        SetActiveSafe(PauseMenu, true);
        SetActiveSafe(LoadMenu, false);
        SetActiveSafe(SettingsMenu, false);
        SetActiveSafe(Scene, true);
        break;
    }
  }

  void ApplyInputForUiMode(UiMode mode) {
    switch (mode) {
      case UiMode.MainMenu:
        _SwitchMap("mainMenu");
        break;
      case UiMode.Gameplay:
        _SwitchMap("gameplay");
        break;
      case UiMode.Pause:
        _SwitchMap("pauseMenu");
        break;
    }
  }

  IEnumerator FadeFromBlackRoutine(string overlayTag) {
    FinalizeLoadingProgressForRelease();
    SetSceneObjectLightsActive(Scene != null && Scene.activeInHierarchy);
    SetLoadingBlackscreenHold(false);
    if (blackscreen != null) {
      PlayBlackscreen("alphaOut");
    }
    else {
      ForceBlackscreenVisible(false);
    }
    if (unlockFadeFailSafeRoutine != null) {
      StopCoroutine(unlockFadeFailSafeRoutine);
    }
    unlockFadeFailSafeRoutine = StartCoroutine(EnsureBlackscreenClearsAfterUnlockRoutine());
    if (!string.IsNullOrWhiteSpace(overlayTag)) {
      SpriteStreamingLoadingState.EndLoadingOverlay(overlayTag);
    }
    yield return null;
  }

  void SetActiveSafe(GameObject target, bool active) {
    if (target == null) return;
    target.SetActive(active);
  }
}
