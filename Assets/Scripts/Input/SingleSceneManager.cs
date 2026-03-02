
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
  public GameObject Blackscreen;
  private All1AnimatorScript blackscreen;

  public GameObject MainMenu;
  public GameObject LoadMenu;
  public GameObject SettingsMenu;
  public GameObject GameplayInterface;
  public GameObject PauseMenu;
  public GameObject Scene;

  public AutoSaver autoSaver;
  public SaveSlotView saveSlotView;
  [Header("Warm Gate")]
  [SerializeField] bool useScenarioWarmGate = true;
  [SerializeField, Min(0.5f)] float startWarmTimeoutSeconds = 3.0f;
  [SerializeField, Min(0.5f)] float startWarmHardTimeoutSeconds = 6.0f;
  [SerializeField, Min(0.5f)] float startWarmRequiredRatio = 0.95f;
  [SerializeField, Min(0.5f)] float gearReturnWarmTimeoutSeconds = 2.0f;
  [SerializeField, Min(0.5f)] float gearReturnWarmHardTimeoutSeconds = 4.5f;
  [SerializeField, Min(0.5f)] float gearReturnRequiredRatio = 0.95f;
  [SerializeField] bool allowHardTimeoutBypass = true;
  [SerializeField, Min(0f)] float fadeLeadSeconds = 0.15f;
  [SerializeField, Min(0f)] float fallbackTransitionSeconds = 2.0f;
  [SerializeField, Min(0f)] float fadeToBlackSeconds = 2.0f;
  [SerializeField, Min(0f)] float fadeFromBlackSeconds = 2.0f;
  [SerializeField] bool waitForStreamingIdleBeforeFadeOut = true;
  [SerializeField, Min(0f)] float streamingIdleMinimumWaitSeconds = 0.5f;
  [SerializeField, Min(0f)] float streamingIdleTimeoutSeconds = 8.0f;
  [SerializeField] bool allowStreamingIdleTimeoutBypass = false;
  [SerializeField, Min(1)] int streamingIdleStableFrames = 2;
  [SerializeField, Min(0)] int streamingIdleAllowedQueued = 0;
  [SerializeField, Min(0)] int streamingIdleAllowedInFlight = 0;
  [SerializeField, Min(0f)] float startupFadeWatchdogSeconds = 2.5f;
  [SerializeField] bool startupWatchdogAllowsTransparentFallback = false;
  [SerializeField] bool enableLoadingStallEmergencyUnlock = true;
  [SerializeField, Min(1f)] float loadingStallEmergencyUnlockSeconds = 12.0f;
  [SerializeField] string defaultStartLocation = LocationEnemyData.DomeCityLocationId;
  const string mainMenuFlowLocationId = LocationEnemyData.MainMenuLocationId;
  const string gameplayFlowFallbackLocationId = LocationEnemyData.DomeCityLocationId;
  [Header("Startup Mode")]
  [Tooltip("True: keep current debug flow (boot directly into location gameplay). False: boot to MainMenu and wait for New Game / Load Game.")]
  [SerializeField] bool debugDirectLocationStartup = false;
  [Header("Debug")]
  [Tooltip("Debug-only helper: Esc swaps Gameplay/Pause back to MainMenu by toggling UI GameObjects.")]
  [SerializeField] bool debugEscToMainMenu = true;

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
  private bool lastWarmGateAllowsGameplayUnlock = true;
  private WarmResult lastWarmResult;
  private bool hasWarmResult;
  private bool holdBlackscreenOpaqueDuringLoad;
  private string lastPurgedLocationId = "";
  private float loadingStallStartedAt = -1f;
  private SettingsReturnTarget settingsReturnTarget = SettingsReturnTarget.MainMenu;
  private GameObject settingsCloseButton;
  private GameObject settingsHoveredTarget;
  private readonly HashSet<string> missingRefWarnings = new(StringComparer.Ordinal);

  void Start() {
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

    SetActiveSafe(Blackscreen, true, nameof(Blackscreen));
    ApplyConfiguredStartupMode();
    ApplyInputMapForCurrentUiState(preferGameplayWhenNoUi: debugDirectLocationStartup);

    if (Blackscreen != null) {
      blackscreen = Blackscreen.GetComponent<All1AnimatorScript>();
      if (blackscreen != null) {
        blackscreen.AddFloatAnim("alphaIn", "_Alpha", 0f, 1f, Mathf.Max(fadeToBlackSeconds, 0f));
        blackscreen.AddFloatAnim("alphaOut", "_Alpha", 1f, 0f, Mathf.Max(fadeFromBlackSeconds, 0f));
      }
      else {
        Debug.LogWarning("[SingleSceneManager][Loading] Blackscreen is missing All1AnimatorScript.");
      }
    }
    else {
      Debug.LogWarning("[SingleSceneManager][Loading] Blackscreen reference is missing.");
    }
    LogLoadingDebug("SingleSceneManager initialized");

  }

  void Update() {
    if (!init) {
      if (ShouldRunStartupGameplayWarmFlow()) {
        LogLoadingDebug("Initial fade-out deferred; startup gameplay warm flow begin");
        SetLoadingBlackscreenHold(true);
        if (startupGameplayRoutine == null) {
          startupGameplayRoutine = StartCoroutine(StartupGameplayFlowRoutine());
        }
        init = true;
        return;
      }
      LogLoadingDebug("Initial fade-out requested");
      if (blackscreen != null) {
        PlayBlackscreen("alphaOut");
      }
      else {
        // Startup must never remain black if animator wiring is missing.
        SetLoadingBlackscreenHold(false);
        ForceBlackscreenVisible(false);
        LogLoadingDebug("Initial fade-out fallback forced because blackscreen animator is unavailable");
      }
      if (startupFadeWatchdogRoutine != null) {
        StopCoroutine(startupFadeWatchdogRoutine);
      }
      startupFadeWatchdogRoutine = StartCoroutine(StartupFadeWatchdogRoutine());
      init = true;
    }

    HandleDebugEscapeToMainMenu();
  }

  void LateUpdate() {
    if (!holdBlackscreenOpaqueDuringLoad) return;
    ForceBlackscreenVisible(true);
  }

  void HandleDebugEscapeToMainMenu() {
    if (!Application.isPlaying || !debugEscToMainMenu) return;
    var keyboard = Keyboard.current;
    if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
    if (IsLoadingFlowActive()) return;
    if (!IsGameplayLikeUiState()) return;

    DebugSwapToMainMenuUiOnly();
  }

  bool IsGameplayLikeUiState() {
    if (GameplayInterface != null && GameplayInterface.activeInHierarchy) return true;
    if (PauseMenu != null && PauseMenu.activeInHierarchy) return true;
    return false;
  }

  void DebugSwapToMainMenuUiOnly() {
    SetLoadingBlackscreenHold(false);
    ForceBlackscreenVisible(false);
    SpriteStreamingLoadingState.EndLoadingOverlay("DebugEscToMainMenu");

    SetActiveSafe(GameplayInterface, false, nameof(GameplayInterface));
    SetActiveSafe(PauseMenu, false, nameof(PauseMenu));
    SetActiveSafe(LoadMenu, false, nameof(LoadMenu));
    SetActiveSafe(SettingsMenu, false, nameof(SettingsMenu));
    SetActiveSafe(MainMenu, true, nameof(MainMenu));
    pauseMenuOpenAppearanceRevision = -1;
    _SwitchMap("mainMenu");
    RequestLocationLoadForMainMenu();
    LogLoadingDebug("Debug ESC -> MainMenu (UI-only swap)");
  }

  void FixedUpdate() {
    TickLoadingStallEmergencyUnlock();
  }



  void StartGame() {
    LogLoadingDebug("StartGame requested");
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
    ResolveAndApplyLocationForStart(isNewGame, loadedSlot);
    RequestLocationLoadForGameplay(LocationManager.currentLocation);
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
    startGameRoutine = StartCoroutine(StartGameFlowRoutine());
  }

  void OpenLoadMenu() {
    _SwitchMap("loadMenu");
    SetActiveSafe(MainMenu, false, nameof(MainMenu));
    SetActiveSafe(SettingsMenu, false, nameof(SettingsMenu));
    SetActiveSafe(LoadMenu, true, nameof(LoadMenu));
  }

  void OpenSettingsMenu() {
    var openedFromPause = PauseMenu != null && PauseMenu.activeInHierarchy;
    settingsReturnTarget = openedFromPause ? SettingsReturnTarget.PauseMenu : SettingsReturnTarget.MainMenu;
    settingsHoveredTarget = null;
    settingsCloseButton = null;

    _SwitchMap("settingsMenu");

    SetActiveSafe(MainMenu, false, nameof(MainMenu));
    SetActiveSafe(LoadMenu, false, nameof(LoadMenu));
    SetActiveSafe(PauseMenu, false, nameof(PauseMenu));
    SetActiveSafe(SettingsMenu, true, nameof(SettingsMenu));
    SetActiveSafe(GameplayInterface, false, nameof(GameplayInterface));
  }

  void CloseSettingsMenu() {
    if (SettingsMenu != null && !SettingsMenu.activeInHierarchy) return;

    SetActiveSafe(SettingsMenu, false, nameof(SettingsMenu));
    SetActiveSafe(LoadMenu, false, nameof(LoadMenu));
    SetActiveSafe(GameplayInterface, false, nameof(GameplayInterface));

    if (settingsReturnTarget == SettingsReturnTarget.PauseMenu) {
      SetActiveSafe(MainMenu, false, nameof(MainMenu));
      SetActiveSafe(PauseMenu, true, nameof(PauseMenu));
      _SwitchMap("pauseMenu");
      return;
    }

    SetActiveSafe(PauseMenu, false, nameof(PauseMenu));
    SetActiveSafe(MainMenu, true, nameof(MainMenu));
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

    var stack = new Stack<Transform>();
    stack.Push(root);

    while (stack.Count > 0) {
      var current = stack.Pop();
      if (string.Equals(current.name, name, StringComparison.OrdinalIgnoreCase)) {
        return current;
      }

      for (var i = 0; i < current.childCount; i++) {
        stack.Push(current.GetChild(i));
      }
    }

    return null;
  }

  void OpenGameplay() {
    LogLoadingDebug("OpenGameplay requested");
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
    if (uiModeRoutine != null) StopCoroutine(uiModeRoutine);
    uiModeRoutine = StartCoroutine(SwitchUiModeRoutine(UiMode.MainMenu));
  }

  void OpenPauseMenu() {
    var gear = ResolvePlayerGearController();
    pauseMenuOpenAppearanceRevision = gear != null ? gear.AppearanceRevision : -1;
    SetUiMode(UiMode.Pause);

  }

  private void _SwitchMap(string map) {
    if (inputProcessor != null) {
      inputProcessor.SwitchMap(map);
    }
    else {
      WarnMissingReference(nameof(inputProcessor));
    }

    if (mouseManager != null) {
      mouseManager.SwitchMap(map);
    }
    else {
      WarnMissingReference(nameof(mouseManager));
    }
    LogLoadingDebug("SwitchMap -> " + map);
  }

  private bool _isNewGame() {
    return SaveSlotManager.slot > saveSlotView.SavesCount;
  }

  IEnumerator StartGameFlowRoutine() {
    SpriteStreamingLoadingState.BeginLoadingOverlay("StartGameFlow");
    LogLoadingDebug("StartGameFlow begin");
    yield return FadeToBlackBeforeLoadRoutine();
    var context = _isNewGame() ? WarmGateMode.StartGame : WarmGateMode.LoadSave;
    yield return RunScenarioWarmGate(context, startWarmTimeoutSeconds, startWarmRequiredRatio);
    if (!lastWarmGateAllowsGameplayUnlock) {
      Debug.LogError(
        "[SingleSceneManager][Loading] StartGameFlow blocked gameplay unlock because critical readiness failed. Forcing fallback unlock." +
        " failure_reason='" + lastWarmResult.failureReason + "'" +
        " ready=" + lastWarmResult.readyCount + "/" + lastWarmResult.totalCount +
        " critical=" + lastWarmResult.criticalReadyCount + "/" + lastWarmResult.criticalTotalCount
      );
      ForceGameplayUnlockFallback("StartGameFlow");
      startGameRoutine = null;
      yield break;
    }
    ApplyGameplayStateUnderBlack();
    LogLoadingDebug("StartGameFlow warm gate done, gameplay activated under black");
    MessageBus.Send("ReadyForSpawns");
    yield return WaitForStreamingIdleBeforeUnlock(context);
    LogLoadingDebug("StartGameFlow ready, unlocking gameplay visuals");
    UnlockGameplayFromBlack();
    startGameRoutine = null;
  }

  IEnumerator ResumeGameplayFlowRoutine() {
    SpriteStreamingLoadingState.BeginLoadingOverlay("ResumeGameplayFlow");
    _SwitchMap("none");
    LogLoadingDebug("ResumeGameplayFlow begin");
    yield return FadeToBlackBeforeLoadRoutine();
    yield return RunScenarioWarmGate(WarmGateMode.GearApplyReturn, gearReturnWarmTimeoutSeconds, gearReturnRequiredRatio);
    if (!lastWarmGateAllowsGameplayUnlock) {
      Debug.LogError(
        "[SingleSceneManager][Loading] ResumeGameplayFlow blocked gameplay unlock because critical readiness failed. Forcing fallback unlock." +
        " failure_reason='" + lastWarmResult.failureReason + "'" +
        " ready=" + lastWarmResult.readyCount + "/" + lastWarmResult.totalCount +
        " critical=" + lastWarmResult.criticalReadyCount + "/" + lastWarmResult.criticalTotalCount
      );
      ForceGameplayUnlockFallback("ResumeGameplayFlow");
      resumeGameplayRoutine = null;
      yield break;
    }
    ApplyGameplayStateUnderBlack();
    LogLoadingDebug("ResumeGameplayFlow warm gate done, gameplay activated under black");
    yield return WaitForStreamingIdleBeforeUnlock(WarmGateMode.GearApplyReturn);
    LogLoadingDebug("ResumeGameplayFlow ready, unlocking gameplay visuals");
    UnlockGameplayFromBlack();
    resumeGameplayRoutine = null;
  }

  IEnumerator StartupGameplayFlowRoutine() {
    SpriteStreamingLoadingState.BeginLoadingOverlay("StartupGameplayFlow");
    _SwitchMap("none");
    LogLoadingDebug("StartupGameplayFlow begin");
    yield return FadeToBlackBeforeLoadRoutine();
    yield return RunScenarioWarmGate(WarmGateMode.StartGame, startWarmTimeoutSeconds, startWarmRequiredRatio);
    if (!lastWarmGateAllowsGameplayUnlock) {
      Debug.LogError(
        "[SingleSceneManager][Loading] StartupGameplayFlow blocked gameplay unlock because critical readiness failed. Forcing fallback unlock." +
        " failure_reason='" + lastWarmResult.failureReason + "'" +
        " ready=" + lastWarmResult.readyCount + "/" + lastWarmResult.totalCount +
        " critical=" + lastWarmResult.criticalReadyCount + "/" + lastWarmResult.criticalTotalCount
      );
      ForceGameplayUnlockFallback("StartupGameplayFlow");
      startupGameplayRoutine = null;
      yield break;
    }
    ApplyGameplayStateUnderBlack();
    LogLoadingDebug("StartupGameplayFlow warm gate done, gameplay activated under black");
    yield return WaitForStreamingIdleBeforeUnlock(WarmGateMode.StartGame);
    LogLoadingDebug("StartupGameplayFlow ready, unlocking gameplay visuals");
    UnlockGameplayFromBlack();
    startupGameplayRoutine = null;
  }

  IEnumerator FadeToBlackBeforeLoadRoutine() {
    SetLoadingBlackscreenHold(false);
    if (Blackscreen != null && !Blackscreen.activeSelf) {
      Blackscreen.SetActive(true);
    }
    LogLoadingDebug("Fade to black start");
    if (blackscreen != null) {
      blackscreen.Play("alphaIn");
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
    LogLoadingDebug("Fade to black complete");
  }

  IEnumerator RunScenarioWarmGate(WarmGateMode context, float timeoutSeconds, float requiredRatio) {
    lastWarmGateAllowsGameplayUnlock = true;
    hasWarmResult = false;
    LogLoadingDebug(
      "Warm gate start mode=" + context +
      " timeout_s=" + timeoutSeconds.ToString("0.00") +
      " required_ratio=" + requiredRatio.ToString("0.00")
    );
    var leadSeconds = Mathf.Max(fadeLeadSeconds, 0f);
    if (leadSeconds > 0f) {
      yield return new WaitForSecondsRealtime(leadSeconds);
    }

    if (!useScenarioWarmGate || !Application.isPlaying) {
      LogLoadingDebug("Warm gate bypassed, using fallback transition");
      if (fallbackTransitionSeconds > 0f) {
        yield return new WaitForSecondsRealtime(fallbackTransitionSeconds);
      }
      yield break;
    }

    var playerController = ResolvePlayerGearController();
    var activeEnemies = ResolveActiveEnemyControllers();
    var request = BuildWarmRequest(context, timeoutSeconds, requiredRatio, playerController, activeEnemies);
    var orchestrator = StreamingWarmOrchestrator.Instance;
    if (orchestrator == null) {
      Debug.LogWarning(
        "[SingleSceneManager][Loading] Warm gate unavailable (orchestrator missing), " +
        "using fallback transition. mode=" + context +
        " timeout_s=" + timeoutSeconds.ToString("0.00") +
        " required_ratio=" + requiredRatio.ToString("0.00")
      );
      LogLoadingDebug("Warm gate unavailable, using fallback transition");
      if (fallbackTransitionSeconds > 0f) {
        yield return new WaitForSecondsRealtime(fallbackTransitionSeconds);
      }
      yield break;
    }

    var completed = false;
    orchestrator.Run(request, result => {
      lastWarmResult = result;
      hasWarmResult = true;
      completed = true;
    });

    var logIntervalSeconds = Mathf.Max(SpriteStreamingRuntimeSettings.LoadingProgressLogIntervalMs, 100) / 1000f;
    var nextProgressLogAt = Time.realtimeSinceStartup + logIntervalSeconds;

    while (!completed && orchestrator.IsRunning) {
      if (ShouldLogLoadingDebug() && Time.realtimeSinceStartup >= nextProgressLogAt) {
        LogWarmGateProgress(context);
        nextProgressLogAt = Time.realtimeSinceStartup + logIntervalSeconds;
      }
      yield return null;
    }

    if (!completed && !hasWarmResult) {
      LogLoadingDebug("Warm gate ended without callback, using fallback transition mode=" + context);
      if (fallbackTransitionSeconds > 0f) {
        yield return new WaitForSecondsRealtime(fallbackTransitionSeconds);
      }
      lastWarmGateAllowsGameplayUnlock = true;
      yield break;
    }
    lastWarmGateAllowsGameplayUnlock = lastWarmResult.playerCriticalReady || lastWarmResult.hardTimeoutBypassUsed;
    if (!lastWarmResult.playerCriticalReady && lastWarmResult.hardTimeoutBypassUsed) {
      Debug.LogError(
        "[SingleSceneManager][Loading] HARD TIMEOUT BYPASS unlock mode=" + context +
        " failure_reason='" + lastWarmResult.failureReason + "'" +
        " ready=" + lastWarmResult.readyCount + "/" + lastWarmResult.totalCount +
        " critical=" + lastWarmResult.criticalReadyCount + "/" + lastWarmResult.criticalTotalCount
      );
    }
    else if (!lastWarmResult.playerCriticalReady) {
      Debug.LogError(
        "[SingleSceneManager][Loading] Warm gate failed critical readiness mode=" + context +
        " failure_reason='" + lastWarmResult.failureReason + "'"
      );
    }

    LogLoadingDebug(
      "Warm gate complete mode=" + context +
      " unlock=" + (lastWarmGateAllowsGameplayUnlock ? 1 : 0) +
      " ready=" + lastWarmResult.readyCount + "/" + lastWarmResult.totalCount +
      " critical=" + lastWarmResult.criticalReadyCount + "/" + lastWarmResult.criticalTotalCount +
      " ratio=" + lastWarmResult.readyRatio.ToString("0.000") +
      " hard_bypass=" + (lastWarmResult.hardTimeoutBypassUsed ? 1 : 0)
    );
  }

  IEnumerator WaitForStreamingIdleBeforeUnlock(WarmGateMode context) {
    if (!waitForStreamingIdleBeforeFadeOut || !Application.isPlaying) yield break;
    if (Blackscreen != null && !Blackscreen.activeSelf) {
      Blackscreen.SetActive(true);
    }
    SetLoadingBlackscreenHold(true);

    var stableFramesRequired = Mathf.Max(streamingIdleStableFrames, 1);
    var allowedQueued = Mathf.Max(streamingIdleAllowedQueued, 0);
    var allowedInFlight = Mathf.Max(streamingIdleAllowedInFlight, 0);
    var minimumWaitSeconds = Mathf.Max(streamingIdleMinimumWaitSeconds, 0f);
    var timeoutSeconds = Mathf.Max(streamingIdleTimeoutSeconds, 0f);
    var startedAt = Time.realtimeSinceStartup;
    var stableFrames = 0;
    var timeoutReported = false;
    var logIntervalSeconds = Mathf.Max(SpriteStreamingRuntimeSettings.LoadingProgressLogIntervalMs, 100) / 1000f;
    var nextProgressLogAt = startedAt + logIntervalSeconds;

    LogLoadingDebug(
      "Streaming idle wait start mode=" + context +
      " min_wait_s=" + minimumWaitSeconds.ToString("0.00") +
      " timeout_s=" + timeoutSeconds.ToString("0.00") +
      " stable_frames=" + stableFramesRequired +
      " allow_q=" + allowedQueued +
      " allow_if=" + allowedInFlight
    );

    while (true) {
      TextureResidencyCache.Pump();
      var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
      var queueIdle = queue.queuedCount <= allowedQueued && queue.inFlightCount <= allowedInFlight;
      var elapsed = Time.realtimeSinceStartup - startedAt;
      var minimumWaitReached = elapsed >= minimumWaitSeconds;

      if (minimumWaitReached && queueIdle && resolverIdle) {
        stableFrames++;
      }
      else {
        stableFrames = 0;
      }

      if (stableFrames >= stableFramesRequired) {
        LogLoadingDebug(
          "Streaming idle wait complete mode=" + context +
          " queued=" + queue.queuedCount +
          " in_flight=" + queue.inFlightCount
        );
        yield break;
      }

      if (timeoutSeconds > 0f && elapsed >= timeoutSeconds) {
        var queueFullyDrained = queue.queuedCount <= 0 && queue.inFlightCount <= 0;
        var forcedByDrain = !allowStreamingIdleTimeoutBypass && queueFullyDrained;
        if (allowStreamingIdleTimeoutBypass || forcedByDrain) {
          Debug.LogWarning(
            "[SingleSceneManager][Loading] Streaming idle wait timeout bypass mode=" + context +
            " queued=" + queue.queuedCount +
            " in_flight=" + queue.inFlightCount +
            " resolver_idle=" + (resolverIdle ? 1 : 0) +
            " stable=" + stableFrames + "/" + stableFramesRequired +
            " timeout_s=" + timeoutSeconds.ToString("0.00") +
            " forced_by_queue_drain=" + (forcedByDrain ? 1 : 0)
          );
          yield break;
        }

        if (!timeoutReported) {
          timeoutReported = true;
          Debug.LogWarning(
            "[SingleSceneManager][Loading] Streaming idle wait timeout reached, holding blackscreen until idle mode=" + context +
            " queued=" + queue.queuedCount +
            " in_flight=" + queue.inFlightCount +
            " resolver_idle=" + (resolverIdle ? 1 : 0) +
            " stable=" + stableFrames + "/" + stableFramesRequired +
            " timeout_s=" + timeoutSeconds.ToString("0.00")
          );
        }
      }

      if (ShouldLogLoadingDebug() && Time.realtimeSinceStartup >= nextProgressLogAt) {
        LogLoadingDebug(
          "Streaming idle wait mode=" + context +
          " queued=" + queue.queuedCount +
          " in_flight=" + queue.inFlightCount +
          " resolver_idle=" + (resolverIdle ? 1 : 0) +
          " min_wait_reached=" + (minimumWaitReached ? 1 : 0) +
          " stable=" + stableFrames + "/" + stableFramesRequired
        );
        nextProgressLogAt = Time.realtimeSinceStartup + logIntervalSeconds;
      }

      yield return null;
    }
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
    if (profile != null) {
      profile.CollectExtraWarmLists(criticalLibraries, criticalAddresses, warmLibraries, warmAddresses);
    }

    var archetypes = ResolveLocationArchetypePrefabs(profile);
    var token = StreamingWarmOrchestrator.BuildEnemyArchetypeToken(LocationManager.currentLocation, archetypes);

    if (context == WarmGateMode.LoadSave) {
      return WarmRequest.CreateLoadSave(
        playerController: playerController,
        enemyControllers: activeEnemies,
        enemyArchetypePrefabsByType: archetypes,
        timeoutSeconds: timeoutSeconds,
        requiredReadyRatio: requiredRatio,
        extraCriticalLibraries: criticalLibraries,
        extraCriticalAddresses: criticalAddresses,
        extraWarmLibraries: warmLibraries,
        extraWarmAddresses: warmAddresses,
        hardTimeoutSeconds: Mathf.Max(startWarmHardTimeoutSeconds, timeoutSeconds, 3.0f),
        allowHardTimeoutBypass: allowHardTimeoutBypass,
        idempotencyToken: token,
        skipIfTokenAlreadyWarm: true
      );
    }

    if (context == WarmGateMode.GearApplyReturn) {
      return WarmRequest.CreateGearApplyReturn(
        playerController: playerController,
        timeoutSeconds: timeoutSeconds,
        requiredReadyRatio: requiredRatio,
        extraCriticalLibraries: criticalLibraries,
        extraCriticalAddresses: criticalAddresses,
        extraWarmLibraries: warmLibraries,
        extraWarmAddresses: warmAddresses,
        hardTimeoutSeconds: Mathf.Max(gearReturnWarmHardTimeoutSeconds, timeoutSeconds, 2.5f),
        allowHardTimeoutBypass: allowHardTimeoutBypass,
        idempotencyToken: "",
        skipIfTokenAlreadyWarm: false
      );
    }

    return WarmRequest.CreateStartGame(
      playerController: playerController,
      enemyControllers: activeEnemies,
      enemyArchetypePrefabsByType: archetypes,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: requiredRatio,
      extraCriticalLibraries: criticalLibraries,
      extraCriticalAddresses: criticalAddresses,
      extraWarmLibraries: warmLibraries,
      extraWarmAddresses: warmAddresses,
      hardTimeoutSeconds: Mathf.Max(startWarmHardTimeoutSeconds, timeoutSeconds, 3.0f),
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: token,
      skipIfTokenAlreadyWarm: true
    );
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
    var enemies = FindObjectsByType<EnemyController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
    return enemies != null && enemies.Length > 0 ? enemies : Array.Empty<EnemyController>();
  }

  void ApplyGameplayStateUnderBlack() {
    if (Blackscreen != null && !Blackscreen.activeSelf) {
      Blackscreen.SetActive(true);
    }
    RequestLocationLoadForGameplay(LocationManager.currentLocation);
    SetLoadingBlackscreenHold(true);
    _SwitchMap("none");
    SetActiveSafe(MainMenu, false, nameof(MainMenu));
    SetActiveSafe(SettingsMenu, false, nameof(SettingsMenu));
    SetActiveSafe(LoadMenu, false, nameof(LoadMenu));
    SetActiveSafe(GameplayInterface, true, nameof(GameplayInterface));
    SetActiveSafe(PauseMenu, false, nameof(PauseMenu));
    SetActiveSafe(Scene, true, nameof(Scene));
    pauseMenuOpenAppearanceRevision = -1;
  }

  void UnlockGameplayFromBlack() {
    LogLoadingDebug("Fade from black start");
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
    LogLoadingDebug("Gameplay state active");
  }

  void LogLoadingDebug(string message) {
    if (!ShouldLogLoadingDebug()) return;
    Debug.Log("[SingleSceneManager][Loading] " + message);
  }

  void PlayBlackscreen(string animationName) {
    if (blackscreen == null) return;
    if (holdBlackscreenOpaqueDuringLoad && string.Equals(animationName, "alphaOut", StringComparison.Ordinal)) {
      LogLoadingDebug("Ignored alphaOut while loading blackscreen hold is active");
      return;
    }
    blackscreen.Play(animationName);
  }

  IEnumerator StartupFadeWatchdogRoutine() {
    var wait = Mathf.Max(startupFadeWatchdogSeconds, 0f);
    if (wait > 0f) {
      yield return new WaitForSecondsRealtime(wait);
    }
    if (IsLoadingFlowActive()) {
      LogLoadingDebug("Startup fade watchdog skipped because loading flow is active");
      startupFadeWatchdogRoutine = null;
      yield break;
    }
    if (ShouldRunStartupGameplayWarmFlow()) {
      LogLoadingDebug("Startup fade watchdog launching startup gameplay warm flow");
      if (startupGameplayRoutine == null) {
        startupGameplayRoutine = StartCoroutine(StartupGameplayFlowRoutine());
      }
      startupFadeWatchdogRoutine = null;
      yield break;
    }

    if (!startupWatchdogAllowsTransparentFallback) {
      Debug.LogWarning(
        "[SingleSceneManager][Loading] Startup fade watchdog overriding transparent fallback gate because startup remained non-loading. " +
        "Forcing blackscreen transparent to avoid stuck black screen."
      );
    }

    SetLoadingBlackscreenHold(false);
    ForceBlackscreenVisible(false);
    SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    ApplyStartupInputFallbackAfterWatchdog();
    LogLoadingDebug("Startup fade watchdog forced blackscreen transparent");
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
      LogLoadingDebug("Startup watchdog skipped map fallback because flow routine is active");
      return;
    }
    ApplyInputMapForCurrentUiState(preferGameplayWhenNoUi: debugDirectLocationStartup);
  }

  bool HasAnyMenuUiActive() {
    if (MainMenu != null && MainMenu.activeInHierarchy) return true;
    if (LoadMenu != null && LoadMenu.activeInHierarchy) return true;
    if (SettingsMenu != null && SettingsMenu.activeInHierarchy) return true;
    if (PauseMenu != null && PauseMenu.activeInHierarchy) return true;
    return false;
  }

  bool ShouldRunStartupGameplayWarmFlow() {
    if (!Application.isPlaying) return false;
    return debugDirectLocationStartup;
  }

  void ApplyConfiguredStartupMode() {
    if (debugDirectLocationStartup) {
      RequestLocationLoadForGameplay(ResolveDefaultLocation());
      LogLoadingDebug("Startup mode -> Debug direct-location flow");
      SetActiveSafe(MainMenu, false, nameof(MainMenu));
      SetActiveSafe(LoadMenu, false, nameof(LoadMenu));
      SetActiveSafe(SettingsMenu, false, nameof(SettingsMenu));
      SetActiveSafe(PauseMenu, false, nameof(PauseMenu));
      SetActiveSafe(GameplayInterface, true, nameof(GameplayInterface));
      SetActiveSafe(Scene, true, nameof(Scene));
      return;
    }

    RequestLocationLoadForMainMenu();
    ApplyUiModeActivations(UiMode.MainMenu);
    pauseMenuOpenAppearanceRevision = -1;
    SpriteStreamingLoadingState.EndLoadingOverlay("MainMenuStartup");
    LogLoadingDebug("Startup mode -> Production main menu");
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
      LogLoadingDebug("No active menu detected, gameplay input fallback applied");
      return;
    }
    if (preferGameplayWhenNoUi) {
      _SwitchMap("mainMenu");
      LogLoadingDebug("No active menu detected, input map fallback applied to mainMenu");
    }
  }

  bool HasLiveGameplayInput() {
    var gameplayInput = FindFirstObjectByType<GameplayInput>();
    return gameplayInput != null && gameplayInput.enabled && gameplayInput.gameObject.activeInHierarchy;
  }

  void ResolveAndApplyLocationForStart(bool isNewGame, SaveData loadedSlot) {
    var resolved = ResolveDefaultLocation();

    if (!isNewGame && loadedSlot != null && loadedSlot.ContainsKey("location")) {
      var loadedLocation = Convert.ToString(loadedSlot["location"]);
      if (IsKnownLocation(loadedLocation)) {
        resolved = loadedLocation.Trim();
      }
      else {
        LogLoadingDebug("Saved location missing/unknown, using default location '" + resolved + "'");
      }
    }

    if (isNewGame && !IsKnownLocation(LocationManager.currentLocation)) {
      LogLoadingDebug("New game location unresolved, using default location '" + resolved + "'");
    }

    if (!string.Equals(LocationManager.currentLocation, resolved, StringComparison.OrdinalIgnoreCase)) {
      LocationManager.UpdateLocation(resolved);
    }
    LogLoadingDebug("Location active -> " + resolved);
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
    MessageBus.Send("RequestLocationLoad", resolved);
    LogLoadingDebug("Location load requested -> " + resolved);
  }

  void OnLocationUpdated(object payload) {
    if (!Application.isPlaying) return;
    var locationId = payload as string;
    if (string.IsNullOrWhiteSpace(locationId)) {
      locationId = LocationManager.currentLocation;
    }
    locationId = string.IsNullOrWhiteSpace(locationId) ? "" : locationId.Trim();
    if (string.Equals(lastPurgedLocationId, locationId, StringComparison.OrdinalIgnoreCase)) return;
    lastPurgedLocationId = locationId;
    TextureResidencyCache.PurgeAll();
    LogLoadingDebug("Location cache purge triggered location='" + locationId + "'");
  }

  void LogWarmGateProgress(WarmGateMode context) {
    if (StreamingWarmOrchestrator.TryGetActiveProgress(out var progress)) {
      var activeMap = inputProcessor != null ? inputProcessor.ActiveMap : "";
      LogLoadingDebug(
        "Warm gate running mode=" + context +
        " ready=" + progress.readyCount + "/" + progress.totalCount +
        " critical=" + progress.criticalReadyCount + "/" + progress.criticalTotalCount +
        " ratio=" + progress.readyRatio.ToString("0.000") +
        " soft_timeout=" + (progress.softTimedOut ? 1 : 0) +
        " map=" + (string.IsNullOrWhiteSpace(activeMap) ? "(none)" : activeMap)
      );
      return;
    }
    LogLoadingDebug("Warm gate running mode=" + context + " progress unavailable");
  }

  void ForceBlackscreenVisible(bool visible) {
    if (Blackscreen == null) return;
    var alpha = visible ? 1f : 0f;
    var spriteRenderer = Blackscreen.GetComponent<SpriteRenderer>();
    if (spriteRenderer != null) {
      var c = spriteRenderer.color;
      if (!Mathf.Approximately(c.a, alpha)) {
        c.a = alpha;
        spriteRenderer.color = c;
      }

      var block = new MaterialPropertyBlock();
      spriteRenderer.GetPropertyBlock(block);
      block.SetFloat("_Alpha", alpha);
      spriteRenderer.SetPropertyBlock(block);
    }
  }

  void SetLoadingBlackscreenHold(bool hold) {
    if (holdBlackscreenOpaqueDuringLoad == hold) return;
    holdBlackscreenOpaqueDuringLoad = hold;
    LogLoadingDebug("Blackscreen hold -> " + (hold ? 1 : 0));
    if (blackscreen != null) {
      blackscreen.enabled = !hold;
    }
    if (hold) {
      ForceBlackscreenVisible(true);
    }
  }

  bool ShouldLogLoadingDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  IEnumerator EnsureBlackscreenClearsAfterUnlockRoutine() {
    var waitSeconds = Mathf.Max(fadeFromBlackSeconds + 0.15f, 0.5f);
    if (waitSeconds > 0f) {
      yield return new WaitForSecondsRealtime(waitSeconds);
    }
    if (!holdBlackscreenOpaqueDuringLoad) {
      ForceBlackscreenVisible(false);
    }
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
    var queueFullyDrained = queue.queuedCount <= 0 && queue.inFlightCount <= 0;
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

    Debug.LogError(
      "[SingleSceneManager][Loading] Emergency unlock after stall queued=" + queue.queuedCount +
      " in_flight=" + queue.inFlightCount +
      " overlay=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
      " hold=" + (holdBlackscreenOpaqueDuringLoad ? 1 : 0) +
      " stall_s=" + elapsed.ToString("0.00")
    );
    ForceReleaseLoadingState();
    loadingStallStartedAt = -1f;
  }

  void ForceReleaseLoadingState() {
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

    SetLoadingBlackscreenHold(false);
    ForceBlackscreenVisible(false);
    SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    ApplyInputMapForCurrentUiState(preferGameplayWhenNoUi: debugDirectLocationStartup);
    LogLoadingDebug("Emergency loading stall release applied");
  }

  void ForceGameplayUnlockFallback(string sourceFlow) {
    if (Blackscreen != null && !Blackscreen.activeSelf) {
      Blackscreen.SetActive(true);
    }
    ApplyGameplayStateUnderBlack();
    UnlockGameplayFromBlack();
    LogLoadingDebug("Forced gameplay unlock fallback source=" + sourceFlow);
  }

  void SetUiMode(UiMode mode, bool waitForStreamingIdle = true) {
    if (uiModeRoutine != null) {
      StopCoroutine(uiModeRoutine);
    }
    uiModeRoutine = StartCoroutine(SwitchUiModeRoutine(mode, waitForStreamingIdle));
  }

  IEnumerator SwitchUiModeRoutine(UiMode mode, bool waitForStreamingIdle = true) {
    var overlayTag = "UiMode_" + mode;
    SpriteStreamingLoadingState.BeginLoadingOverlay(overlayTag);
    yield return FadeToBlackBeforeLoadRoutine();
    ApplyUiModeActivations(mode);
    ApplyInputForUiMode(mode);
    if (waitForStreamingIdle) {
      yield return WaitForStreamingIdleBeforeUnlock(WarmGateMode.StartGame);
    }
    yield return FadeFromBlackRoutine(overlayTag);
    uiModeRoutine = null;
  }

  void ApplyUiModeActivations(UiMode mode) {
    switch (mode) {
      case UiMode.MainMenu:
        RequestLocationLoadForMainMenu();
        SetActiveSafe(MainMenu, true, nameof(MainMenu));
        SetActiveSafe(LoadMenu, false, nameof(LoadMenu));
        SetActiveSafe(SettingsMenu, false, nameof(SettingsMenu));
        SetActiveSafe(GameplayInterface, false, nameof(GameplayInterface));
        SetActiveSafe(PauseMenu, false, nameof(PauseMenu));
        SetActiveSafe(Scene, false, nameof(Scene));
        break;
      case UiMode.Gameplay:
        SetActiveSafe(MainMenu, false, nameof(MainMenu));
        SetActiveSafe(LoadMenu, false, nameof(LoadMenu));
        SetActiveSafe(SettingsMenu, false, nameof(SettingsMenu));
        SetActiveSafe(PauseMenu, false, nameof(PauseMenu));
        SetActiveSafe(GameplayInterface, true, nameof(GameplayInterface));
        SetActiveSafe(Scene, true, nameof(Scene));
        pauseMenuOpenAppearanceRevision = -1;
        break;
      case UiMode.Pause:
        SetActiveSafe(MainMenu, false, nameof(MainMenu));
        SetActiveSafe(GameplayInterface, false, nameof(GameplayInterface));
        SetActiveSafe(PauseMenu, true, nameof(PauseMenu));
        SetActiveSafe(LoadMenu, false, nameof(LoadMenu));
        SetActiveSafe(SettingsMenu, false, nameof(SettingsMenu));
        SetActiveSafe(Scene, true, nameof(Scene));
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

  void SetActiveSafe(GameObject target, bool active, string fieldName) {
    if (target == null) {
      WarnMissingReference(fieldName);
      return;
    }
    target.SetActive(active);
  }

  void WarnMissingReference(string fieldName) {
    if (string.IsNullOrWhiteSpace(fieldName)) return;
    if (!missingRefWarnings.Add(fieldName)) return;
    Debug.LogWarning("[SingleSceneManager][Loading] Missing reference '" + fieldName + "'. Assign it in the inspector.");
  }
}
