using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class SingleSceneManager {
  readonly struct GameplayLoadStepTrace {
    public readonly string Step;
    public readonly float StartedAt;
    public readonly int FlowId;
    public readonly int QueuedCount;
    public readonly int InFlightCount;
    public readonly int DeferredPendingCount;
    public readonly int OutstandingCount;
    public readonly bool ResolverIdle;
    public readonly bool PlayerReady;
    public readonly string LocationId;

    public GameplayLoadStepTrace(
      string step,
      float startedAt,
      int flowId,
      int queuedCount,
      int inFlightCount,
      int deferredPendingCount,
      bool resolverIdle,
      bool playerReady,
      string locationId
    ) {
      Step = string.IsNullOrWhiteSpace(step) ? "-" : step.Trim();
      StartedAt = startedAt;
      FlowId = flowId;
      QueuedCount = Mathf.Max(queuedCount, 0);
      InFlightCount = Mathf.Max(inFlightCount, 0);
      DeferredPendingCount = Mathf.Max(deferredPendingCount, 0);
      OutstandingCount = Mathf.Max(queuedCount + inFlightCount + deferredPendingCount, 0);
      ResolverIdle = resolverIdle;
      PlayerReady = playerReady;
      LocationId = ResolveLoadFlowValue(locationId);
    }
  }

  void BeginStartupMainMenuReveal() {
    PrepareLoadingScreenCarrier();
    SetLoadingBlackscreenHold(false);
    ForceBlackscreenVisible(true);
    LogSectionTransitionState("startup_reveal_begin", Section.None, ResolveCurrentSection(), "MainMenuStartup", false);
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
  }

  void InitializeLoadingScreenReferences() {
    loadingBlackscreen = null;
    loadingBlackscreenRenderer = null;
    loadingHeldProgressBlackscreenVisualApplied = false;
    blackscreen = null;
    loadingLightObject = null;
    loadingCircle = null;
    loadingTextObject = null;
    loadingText = null;
    loadingUiFeedbackActive = false;
    loadingOverlayChildrenReady = false;
    loadingPercent = -1;
    loadingStatusDetail = "";
    loadingStatusOverride = "";
    loadingPercentDisplayInitialized = false;
    loadingProgressUiArmCheckFrame = -1;
    loadingPercentDisplayValue = 0f;
    loadingProgressPeakOutstanding = -1;
    loadingProgressGoalTotal = -1;
    loadingProgressGoalBestRemaining = int.MaxValue;
    loadingProgressIdleStartedAt = -1f;
    loadingProgressObservedWork = false;
    loadingProgressNextDebugLogAt = -1f;
    ResetLoadingHeartbeatDebugState();
    ResetLoadingStageStallState();
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



    var lightTransform = FindChildByName(LoadingScreen.transform, "light");
    if (lightTransform != null) {
      loadingLightObject = lightTransform.gameObject;
    }

    var textTransform = FindChildByName(LoadingScreen.transform, "text");
    if (textTransform != null) {
      loadingTextObject = textTransform.gameObject;
      loadingText = textTransform.GetComponent<FontText>();
    }

    PrimeLoadingTextRuntimeAssets("initialize_loading_screen_references");
  }

  void UpdateLoadingScreenFeedback() {
    if (!PrepareLoadingScreenFeedbackState()) return;
    MaybeLogLoadingScreenHeartbeat();
    MaybeWarnLoadingProgressUiNotArmed();
    if (!loadingOverlayChildrenReady) return;
    var percent = CalculateLoadingPercentFromTextureQueue(out var statusDetail);
    UpdateLoadingScreenText(percent, statusDetail);
    MaybeLogLoadingStageStall(percent, statusDetail);
  }

  bool PrepareLoadingScreenFeedbackState() {
    var loadingActive = holdBlackscreenOpaqueDuringLoad || IsLoadingFlowActive();
    if (!loadingActive) {
      if (!loadingUiFeedbackActive &&
          !loadingOverlayChildrenReady &&
          loadingPercent < 0 &&
          !loadingPercentDisplayInitialized &&
          loadingHeartbeatStartedAt < 0f &&
          !loadingProgressObservedWork &&
          loadingProgressGoalTotal < 0 &&
          loadingProgressPeakOutstanding < 0 &&
          loadingProgressNextDebugLogAt < 0f &&
          !loadingBlockingStateKnown) {
        return false;
      }
      loadingUiFeedbackActive = false;
      loadingOverlayChildrenReady = false;
      loadingPercent = -1;
      loadingStatusDetail = "";
      loadingStatusOverride = "";
      loadingPercentDisplayInitialized = false;
      loadingProgressUiArmCheckFrame = -1;
      loadingPercentDisplayValue = 0f;
      loadingProgressPeakOutstanding = -1;
      loadingProgressGoalTotal = -1;
      loadingProgressGoalBestRemaining = int.MaxValue;
      loadingProgressIdleStartedAt = -1f;
      loadingProgressObservedWork = false;
      loadingProgressNextDebugLogAt = -1f;
      ResetLoadingHeartbeatDebugState();
      ResetLoadingStageStallState();
      ResetBlockingProgressState();
      ResetGameplayLoadStageTracking();
      ResetZeroPercentStallDebugState();
      SetLoadingLightActive(false);
      SetLoadingProgressUiActive(false);
      SetLoadingText("");
      return false;
    }

    if (LoadingScreen != null && !LoadingScreen.activeSelf) {
      SetLoadingRootActive(true);
    }

    SetLoadingProgressUiActive(loadingOverlayChildrenReady);
    loadingUiFeedbackActive = true;

    if (loadingOverlayChildrenReady) {
      RotateLoadingCircle();
    }

    return true;
  }

  void ResetGameplayLoadStageTracking() {
    gameplayReadyForSpawnsSentForLoad = false;
    gameplayWarmGateStartedForLoad = false;
    gameplayWarmGateCompletedForLoad = false;
    gameplayLoadingStageForLoad = OptimalGameplayLoadingStage.Player;
    gameplayLoadingStageLocationId = "";
    runtimeBaseSetupPreparedFlowId = 0;
    runtimeRevealSetupPreparedFlowId = 0;
  }

  void ResetGameplayLoadFlowTrace() {
    activeGameplayLoadFlowId = 0;
    activeGameplayLoadFlowStartedAt = -1f;
    activeGameplayLoadFlowKind = "";
    activeGameplayLoadFlowTargetLocation = "";
    activeGameplayLoadFlowOriginSection = Section.None;
    activeGameplayLoadFlowIsNewGame = false;
    activeGameplayLoadFlowSlot = -1;
  }

  string ResolveGameplayLoadTraceTargetLocation(bool resolveLocationForStart, bool isNewGame, SaveData loadedSlot) {
    if (resolveLocationForStart) {
      return LocationEnemyData.NormalizeLocationId(ResolveLocationForStart(isNewGame, loadedSlot));
    }

    if (IsGameplayLocation(pendingGameplayLocationId)) {
      return LocationEnemyData.NormalizeLocationId(pendingGameplayLocationId);
    }

    return LocationEnemyData.NormalizeLocationId(ResolveGameplayLocationRequest(pendingGameplayLocationId, out _));
  }

  void UpdateActiveGameplayLoadTargetLocation(string locationId) {
    if (activeGameplayLoadFlowId <= 0) return;
    var normalized = LocationEnemyData.NormalizeLocationId(locationId);
    if (string.IsNullOrWhiteSpace(normalized)) return;
    activeGameplayLoadFlowTargetLocation = normalized;
  }

  int BeginGameplayLoadFlowTrace(
    string overlayTag,
    WarmGateMode warmContext,
    bool isNewGame,
    SaveData loadedSlot,
    bool resolveLocationForStart,
    Section fromSection
  ) {
    activeGameplayLoadFlowId = nextGameplayLoadFlowId++;
    activeGameplayLoadFlowStartedAt = Time.realtimeSinceStartup;
    activeGameplayLoadFlowKind = string.IsNullOrWhiteSpace(overlayTag) ? warmContext.ToString() : overlayTag.Trim();
    activeGameplayLoadFlowTargetLocation = ResolveGameplayLoadTraceTargetLocation(resolveLocationForStart, isNewGame, loadedSlot);
    activeGameplayLoadFlowOriginSection = fromSection;
    activeGameplayLoadFlowIsNewGame = isNewGame;
    activeGameplayLoadFlowSlot = SaveSlotManager.slot;

    if (!ShouldLogLoadFlowWarnings()) return activeGameplayLoadFlowId;

    var savedLocation = loadedSlot != null && loadedSlot.ContainsKey("location")
      ? Convert.ToString(loadedSlot["location"])
      : "";
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    var builder = BeginLoadFlowLog("[SingleSceneManager][LoadFlow]");
    AppendLoadFlowField(builder, "phase", "begin");
    AppendLoadFlowField(builder, "warm_context", warmContext.ToString());
    AppendLoadFlowField(builder, "from", fromSection.ToString());
    AppendLoadFlowField(builder, "to", Section.Gameplay.ToString());
    AppendLoadFlowBool(builder, "is_new_game", isNewGame);
    AppendLoadFlowInt(builder, "slot", activeGameplayLoadFlowSlot);
    AppendLoadFlowField(builder, "saved_location", ResolveLoadFlowValue(savedLocation));
    AppendLoadFlowField(builder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    AppendLoadFlowBool(builder, "resolve_location_for_start", resolveLocationForStart);
    AppendLoadFlowInt(builder, "queue_queued", queue.queuedCount);
    AppendLoadFlowInt(builder, "queue_in_flight", queue.inFlightCount);
    AppendLoadFlowInt(builder, "deferred_pending", deferredPending);
    AppendLoadFlowBool(builder, "resolver_idle", SpriteRuntimeResolver.IsWarmupIdle());
    AppendLoadFlowBool(builder, "player_ready", IsPlayerFirstFrameReady());
    RuntimeLog.Log(builder.ToString());
    return activeGameplayLoadFlowId;
  }

  void EndGameplayLoadFlowTrace(int flowId, string result) {
    if (flowId <= 0 || activeGameplayLoadFlowId != flowId) return;

    if (ShouldLogLoadFlowWarnings()) {
      var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
      var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
      var builder = BeginLoadFlowLog("[SingleSceneManager][LoadFlow]");
      AppendLoadFlowField(builder, "phase", "end");
      AppendLoadFlowField(builder, "result", ResolveLoadFlowValue(result));
      AppendLoadFlowFloat(
        builder,
        "elapsed_ms",
        activeGameplayLoadFlowStartedAt >= 0f
          ? Mathf.Max(Time.realtimeSinceStartup - activeGameplayLoadFlowStartedAt, 0f) * 1000f
          : 0f,
        "0.0"
      );
      AppendLoadFlowField(builder, "from", activeGameplayLoadFlowOriginSection.ToString());
      AppendLoadFlowField(builder, "to", Section.Gameplay.ToString());
      AppendLoadFlowBool(builder, "is_new_game", activeGameplayLoadFlowIsNewGame);
      AppendLoadFlowInt(builder, "slot", activeGameplayLoadFlowSlot);
      AppendLoadFlowField(builder, "current_section", ResolveCurrentSection().ToString());
      AppendLoadFlowField(builder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
      AppendLoadFlowInt(builder, "queue_queued", queue.queuedCount);
      AppendLoadFlowInt(builder, "queue_in_flight", queue.inFlightCount);
      AppendLoadFlowInt(builder, "deferred_pending", deferredPending);
      AppendLoadFlowBool(builder, "resolver_idle", SpriteRuntimeResolver.IsWarmupIdle());
      AppendLoadFlowBool(builder, "player_ready", IsPlayerFirstFrameReady());
      RuntimeLog.Log(builder.ToString());
    }

    ResetGameplayLoadFlowTrace();
  }

  GameplayLoadStepTrace BeginGameplayLoadStepTrace(string step, string extraFields = "") {
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    var trace = new GameplayLoadStepTrace(
      step,
      Time.realtimeSinceStartup,
      activeGameplayLoadFlowId,
      queue.queuedCount,
      queue.inFlightCount,
      deferredPending,
      SpriteRuntimeResolver.IsWarmupIdle(),
      IsPlayerFirstFrameReady(),
      LocationManager.currentLocation
    );

    if (!ShouldLogLoadFlowWarnings()) return trace;

    var builder = BeginLoadFlowLog("[SingleSceneManager][LoadStep]");
    AppendLoadFlowField(builder, "phase", "begin");
    AppendLoadFlowField(builder, "step", trace.Step);
    AppendLoadFlowField(builder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(builder, "current_location", trace.LocationId);
    AppendLoadFlowInt(builder, "queue_queued", trace.QueuedCount);
    AppendLoadFlowInt(builder, "queue_in_flight", trace.InFlightCount);
    AppendLoadFlowInt(builder, "deferred_pending", trace.DeferredPendingCount);
    AppendLoadFlowInt(builder, "outstanding", trace.OutstandingCount);
    AppendLoadFlowBool(builder, "resolver_idle", trace.ResolverIdle);
    AppendLoadFlowBool(builder, "player_ready", trace.PlayerReady);
    if (!string.IsNullOrWhiteSpace(extraFields)) {
      builder.Append(' ').Append(extraFields.Trim());
    }
    RuntimeLog.Log(builder.ToString());
    return trace;
  }

  void EndGameplayLoadStepTrace(GameplayLoadStepTrace trace, string result = "complete", string extraFields = "") {
    if (!ShouldLogLoadFlowWarnings()) return;

    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    var playerReady = IsPlayerFirstFrameReady();
    var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
    var currentLocation = ResolveLoadFlowValue(LocationManager.currentLocation);
    var elapsedMs = trace.StartedAt >= 0f
      ? Mathf.Max(Time.realtimeSinceStartup - trace.StartedAt, 0f) * 1000f
      : 0f;
    var outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount + deferredPending, 0);
    var builder = BeginLoadFlowLog("[SingleSceneManager][LoadStep]");
    AppendLoadFlowField(builder, "phase", "end");
    AppendLoadFlowField(builder, "step", trace.Step);
    AppendLoadFlowField(builder, "result", ResolveLoadFlowValue(result));
    AppendLoadFlowFloat(builder, "elapsed_ms", elapsedMs, "0.0");
    AppendLoadFlowField(builder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(builder, "location_start", trace.LocationId);
    AppendLoadFlowField(builder, "current_location", currentLocation);
    AppendLoadFlowBool(builder, "location_changed", !string.Equals(trace.LocationId, currentLocation, StringComparison.OrdinalIgnoreCase));
    AppendLoadFlowInt(builder, "queue_queued_start", trace.QueuedCount);
    AppendLoadFlowInt(builder, "queue_queued_end", queue.queuedCount);
    AppendLoadFlowInt(builder, "queue_in_flight_start", trace.InFlightCount);
    AppendLoadFlowInt(builder, "queue_in_flight_end", queue.inFlightCount);
    AppendLoadFlowInt(builder, "deferred_pending_start", trace.DeferredPendingCount);
    AppendLoadFlowInt(builder, "deferred_pending_end", deferredPending);
    AppendLoadFlowInt(builder, "outstanding_start", trace.OutstandingCount);
    AppendLoadFlowInt(builder, "outstanding_end", outstanding);
    AppendLoadFlowInt(builder, "outstanding_delta", outstanding - trace.OutstandingCount);
    AppendLoadFlowBool(builder, "resolver_idle_start", trace.ResolverIdle);
    AppendLoadFlowBool(builder, "resolver_idle_end", resolverIdle);
    AppendLoadFlowBool(builder, "player_ready_start", trace.PlayerReady);
    AppendLoadFlowBool(builder, "player_ready_end", playerReady);
    if (!string.IsNullOrWhiteSpace(extraFields)) {
      builder.Append(' ').Append(extraFields.Trim());
    }

    RuntimeLog.Log(builder.ToString());
  }

  bool IsGameplayLoadPipelineTrackingActive() {
    if (startGameRoutine != null ||
        resumeGameplayRoutine != null ||
        startupGameplayRoutine != null ||
        runtimeLocationTransitionRoutine != null ||
        pendingRevealSection == Section.Gameplay) {
      return true;
    }

    if (!gameplayWarmGateStartedForLoad && !gameplayWarmGateCompletedForLoad) {
      return false;
    }

    var overlayReason = SpriteStreamingLoadingState.ActiveReason;
    return string.Equals(overlayReason, "StartGameFlow", StringComparison.Ordinal) ||
           string.Equals(overlayReason, "LoadGameFlow", StringComparison.Ordinal) ||
           string.Equals(overlayReason, "ResumeGameplayFlow", StringComparison.Ordinal) ||
           string.Equals(overlayReason, "StartupGameplayFlow", StringComparison.Ordinal) ||
           string.Equals(overlayReason, BuildSectionOverlayTag(Section.Gameplay), StringComparison.Ordinal);
  }

  bool IsGameplayPlayerBootstrapReady() {
    return IsPlayerHierarchyReady() && IsPlayerFirstFrameReady();
  }

  GameplayDialogController ResolveGameplayDialogController() {
    if (cachedGameplayDialogController != null) {
      if (cachedGameplayDialogController.gameObject != null) {
        return cachedGameplayDialogController;
      }
      cachedGameplayDialogController = null;
    }

    if (GameplayInterface == null) {
      return null;
    }

    cachedGameplayDialogController = GameplayInterface.GetComponentInChildren<GameplayDialogController>(true);
    return cachedGameplayDialogController;
  }

  static bool ShouldExpectEnemyWarmStageForCurrentLocation() {
    return ContentEpisodeProgression.HasCurrentEpisodeSpawnRules();
  }

  int ResolveCurrentLocationLoadingArchetypeCount() {
    var spawner = ResolveGameplaySpawner();
    return spawner != null ? spawner.GetCurrentLocationArchetypeWarmupCount() : 0;
  }

  bool IsGameplayUiReadyForLoadingProgress() {
    if (GameplayInterface == null) {
      return false;
    }
    if (!GameplayInterface.activeInHierarchy) {
      return true;
    }

    var dialogController = ResolveGameplayDialogController();
    return dialogController != null && dialogController.HasResolvedUiReferencesForLoadingProgress;
  }

  string GetGameplayUiReadyBlockerSummary() {
    if (GameplayInterface == null) {
      return "GameplayInterface_Null";
    }
    var dialogController = ResolveGameplayDialogController();
    if (dialogController == null) {
      return "DialogController_Null";
    }
    if (!dialogController.HasResolvedUiReferencesForLoadingProgress) {
      return "DialogController_ReferencesPending(" + dialogController.ResolvedUiReferencesBlockerSummary + ")";
    }
    return "None";
  }

  bool IsGameplayDialogReadyForLoadingProgress() {
    if (GameplayInterface == null) {
      return false;
    }
    if (!GameplayInterface.activeInHierarchy) {
      return true;
    }

    var dialogController = ResolveGameplayDialogController();
    return dialogController != null && dialogController.IsReadyForLoadingProgress;
  }

  string GetGameplayDialogReadyBlockerSummary() {
    var dialogController = ResolveGameplayDialogController();
    if (dialogController == null) {
      return "DialogController_Null";
    }
    if (!dialogController.enabled) {
      return "DialogController_Disabled";
    }
    if (!dialogController.HasResolvedUiReferencesForLoadingProgress) {
      return "DialogController_ReferencesPending(" + dialogController.ResolvedUiReferencesBlockerSummary + ")";
    }
    if (!dialogController.IsReadyForLoadingProgress) {
      return "DialogState_NotReady";
    }
    return "None";
  }

  static float ResolveStagedLoadingProgress(float start, float end, float stageProgress) {
    return Mathf.Lerp(start, end, Mathf.Clamp01(stageProgress));
  }

  float ResolveGameplayPlayerStageProgress() {
    var player = ResolvePlayerGearController();
    if (player == null || player.gameObject == null) {
      return 0f;
    }

    if (!player.gameObject.scene.IsValid()) {
      return 0.45f;
    }

    if (!player.gameObject.activeInHierarchy) {
      return 0.8f;
    }

    if (!TryMeasurePlayerFirstFrameReadiness(out var readyCount, out var totalCount) || totalCount <= 0) {
      return IsPlayerFirstFrameReady() ? 1f : 0.86f;
    }

    var readiness = Mathf.Clamp01((float)readyCount / totalCount);
    return Mathf.Lerp(0.82f, 1f, readiness);
  }

  bool TryMeasurePlayerFirstFrameReadiness(out int readyCount, out int totalCount) {
    readyCount = 0;
    totalCount = 0;
    var player = ResolvePlayerGearController();
    if (player == null) return false;

    MeasureSpriteGroupFirstFrameReadiness(player.SkinObjects, ref readyCount, ref totalCount);
    MeasureSpriteGroupFirstFrameReadiness(player.GearObjects, ref readyCount, ref totalCount);
    return totalCount > 0;
  }

  void MeasureSpriteGroupFirstFrameReadiness(GameObject[] objects, ref int readyCount, ref int totalCount) {
    if (objects == null || objects.Length <= 0) return;

    for (var i = 0; i < objects.Length; i++) {
      var go = objects[i];
      if (go == null || !go.activeInHierarchy) continue;
      var sprite = go.GetComponent<SpriteWithNormals>();
      if (sprite == null || !sprite.enabled || sprite.DoNotRender) continue;
      var frame = ResolveSpriteReadinessFrame(sprite);
      totalCount++;
      if (sprite.IsFrameReady(frame, out _)) {
        readyCount++;
      }
    }
  }

  float ResolveGameplayLocationStageProgress(bool locationReady) {
    if (locationReady) {
      return 1f;
    }

    if (string.IsNullOrWhiteSpace(LocationManager.currentLocation)) {
      return 0.1f;
    }

    return 0.55f;
  }

  float ResolveGameplayUiStageProgress(GameplayDialogController dialogController, bool uiReady) {
    if (uiReady) {
      return 1f;
    }

    var progress = 0f;
    var gameplayUiActive = GameplayInterface != null && GameplayInterface.activeInHierarchy;
    if (GameplayInterface != null) {
      progress = 0.2f;
    }

    if (gameplayUiActive) {
      progress = 0.55f;
    }

    if (dialogController != null && gameplayUiActive) {
      progress = 0.8f;
      if (dialogController.HasResolvedUiReferencesForLoadingProgress) {
        progress = 0.95f;
      }
    }

    return progress;
  }

  float ResolveGameplayDialogStageProgress(GameplayDialogController dialogController, bool dialogReady) {
    if (dialogReady) {
      return 1f;
    }

    if (dialogController == null) {
      return 0f;
    }

    if (!dialogController.isActiveAndEnabled) {
      return 0.35f;
    }

    return dialogController.HasResolvedUiReferencesForLoadingProgress ? 0.75f : 0.55f;
  }

  void AdvanceOptimalGameplayLoadingStageForLoad(
    bool playerReady,
    bool locationReady,
    bool enemiesReady,
    bool uiReady,
    bool dialogReady
  ) {
    var previousStage = gameplayLoadingStageForLoad;

    var playerStageReady = playerReady;
    var locationStageReady = playerStageReady && locationReady;
    var enemiesStageReady = locationStageReady && enemiesReady;
    var uiStageReady = enemiesStageReady && uiReady;
    var dialogStageReady = uiStageReady && dialogReady;

    if (gameplayLoadingStageForLoad <= OptimalGameplayLoadingStage.Player && playerStageReady) {
      gameplayLoadingStageForLoad = OptimalGameplayLoadingStage.Location;
    }
    if (gameplayLoadingStageForLoad <= OptimalGameplayLoadingStage.Location && locationStageReady) {
      gameplayLoadingStageForLoad = OptimalGameplayLoadingStage.Enemies;
    }
    if (gameplayLoadingStageForLoad <= OptimalGameplayLoadingStage.Enemies && enemiesStageReady) {
      gameplayLoadingStageForLoad = OptimalGameplayLoadingStage.Ui;
    }
    if (gameplayLoadingStageForLoad <= OptimalGameplayLoadingStage.Ui && uiStageReady) {
      gameplayLoadingStageForLoad = OptimalGameplayLoadingStage.Dialog;
    }
    if (gameplayLoadingStageForLoad <= OptimalGameplayLoadingStage.Dialog && dialogStageReady) {
      gameplayLoadingStageForLoad = OptimalGameplayLoadingStage.FinalizingReveal;
    }

    if (previousStage == gameplayLoadingStageForLoad || !ShouldLogLoadFlowWarnings()) {
      return;
    }

    RuntimeLog.Log(
      "[SingleSceneManager][OptimalLoadingProgress] stage='" + gameplayLoadingStageForLoad +
      "' player_ready=" + (playerReady ? 1 : 0) +
      " location_ready=" + (locationReady ? 1 : 0) +
      " enemies_ready=" + (enemiesReady ? 1 : 0) +
      " ui_ready=" + (uiReady ? 1 : 0) +
      " dialog_ready=" + (dialogReady ? 1 : 0) +
      " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation)
    );
  }

  void AppendGameplayLoadPipelineFields(StringBuilder builder) {
    if (builder == null) {
      return;
    }

    var shouldExpectEnemyWarmStage = ShouldExpectEnemyWarmStageForCurrentLocation();
    var archetypeCount = shouldExpectEnemyWarmStage ? ResolveCurrentLocationLoadingArchetypeCount() : 0;
    var registry = ActiveContentRegistryRuntime.Registry;
    var activePackCount = registry != null && registry.ActivePackIds != null ? registry.ActivePackIds.Count : 0;
    AppendLoadFlowBool(builder, "pipeline_player_ready", IsGameplayPlayerBootstrapReady());
    AppendLoadFlowBool(builder, "pipeline_location_ready", !LocationManager.HasPendingBlockingActivationWork);
    AppendLoadFlowBool(builder, "pipeline_enemies_ready", gameplayWarmGateCompletedForLoad);
    AppendLoadFlowBool(builder, "pipeline_ui_ready", IsGameplayUiReadyForLoadingProgress());
    AppendLoadFlowField(builder, "pipeline_ui_blocker", ResolveLoadFlowValue(GetGameplayUiReadyBlockerSummary()));
    AppendLoadFlowBool(builder, "pipeline_dialog_ready", IsGameplayDialogReadyForLoadingProgress());
    AppendLoadFlowField(builder, "pipeline_dialog_blocker", ResolveLoadFlowValue(GetGameplayDialogReadyBlockerSummary()));
    AppendLoadFlowBool(builder, "pipeline_expect_enemy_stage", shouldExpectEnemyWarmStage);
    AppendLoadFlowInt(builder, "pipeline_enemy_archetypes", archetypeCount);
    AppendLoadFlowField(builder, "pipeline_stage", gameplayLoadingStageForLoad.ToString());
    AppendLoadFlowBool(builder, "content_external_active", ActiveContentRegistryRuntime.HasActiveExternalContent());
    AppendLoadFlowInt(builder, "content_active_pack_count", activePackCount);
    AppendLoadFlowField(builder, "content_default_location", ResolveLoadFlowValue(ActiveContentRegistryRuntime.GetDefaultLocationId()));
    AppendLoadFlowBool(builder, "reveal_critical_ready", IsCriticalScopeReadyForReveal());
    AppendLoadFlowBool(builder, "warm_plan_critical_ready", loadingBlockingCriticalReady);
  }

  static string ResolveGameplayWarmStageDetail(bool shouldExpectEnemyWarmStage) {
    return shouldExpectEnemyWarmStage ? "Preparing enemies" : "Warming gameplay";
  }

  OptimalGameplayLoadingProgress ResolveOptimalGameplayLoadingProgress(bool hasBlockingProgress, float blockingProgress) {
    if (!IsGameplayLoadPipelineTrackingActive()) {
      return default;
    }

    var currentLocationId = string.IsNullOrWhiteSpace(LocationManager.currentLocation)
      ? ""
      : LocationManager.currentLocation.Trim();
    if (!string.Equals(gameplayLoadingStageLocationId, currentLocationId, StringComparison.OrdinalIgnoreCase)) {
      gameplayLoadingStageLocationId = currentLocationId;
      gameplayLoadingStageForLoad = OptimalGameplayLoadingStage.Player;
      ResetLoadingStageStallState();
    }

    var playerReady = IsGameplayPlayerBootstrapReady();
    var locationReady = !LocationManager.HasPendingBlockingActivationWork;
    var shouldExpectEnemyWarmStage = ShouldExpectEnemyWarmStageForCurrentLocation();
    var archetypeCount = shouldExpectEnemyWarmStage ? ResolveCurrentLocationLoadingArchetypeCount() : 0;
    var enemyWarmInputsReady = !shouldExpectEnemyWarmStage || archetypeCount > 0;
    var dialogController = ResolveGameplayDialogController();
    var enemiesReady = gameplayWarmGateCompletedForLoad;
    var uiReadyForProgress = IsGameplayUiReadyForLoadingProgress();
    var dialogReadyForProgress = dialogController != null && dialogController.IsReadyForLoadingProgress;

    AdvanceOptimalGameplayLoadingStageForLoad(
      playerReady,
      locationReady,
      enemiesReady,
      uiReadyForProgress,
      dialogReadyForProgress
    );

    switch (gameplayLoadingStageForLoad) {
      case OptimalGameplayLoadingStage.Player:
        return new OptimalGameplayLoadingProgress(
          true,
          ResolveStagedLoadingProgress(0f, OptimalLoadingProgressPlayerFloor, ResolveGameplayPlayerStageProgress()),
          OptimalLoadingProgressPlayerFloor,
          "Preparing player"
        );
      case OptimalGameplayLoadingStage.Location:
        return new OptimalGameplayLoadingProgress(
          true,
          ResolveStagedLoadingProgress(
            OptimalLoadingProgressPlayerFloor,
            OptimalLoadingProgressLocationFloor,
            ResolveGameplayLocationStageProgress(locationReady)
          ),
          OptimalLoadingProgressLocationFloor,
          "Activating location"
        );
      case OptimalGameplayLoadingStage.Enemies:
        var enemyStageProgress = 0f;
        if (gameplayWarmGateStartedForLoad && hasBlockingProgress) {
          enemyStageProgress = Mathf.Clamp01(blockingProgress);
        }
        else if (gameplayReadyForSpawnsSentForLoad && enemyWarmInputsReady) {
          enemyStageProgress = 0.2f;
        }
        return new OptimalGameplayLoadingProgress(
          true,
          ResolveStagedLoadingProgress(OptimalLoadingProgressLocationFloor, OptimalLoadingProgressEnemiesFloor, enemyStageProgress),
          OptimalLoadingProgressEnemiesFloor,
          ResolveGameplayWarmStageDetail(shouldExpectEnemyWarmStage)
        );
      case OptimalGameplayLoadingStage.Ui:
        return new OptimalGameplayLoadingProgress(
          true,
          ResolveStagedLoadingProgress(
            OptimalLoadingProgressEnemiesFloor,
            OptimalLoadingProgressUiFloor,
            ResolveGameplayUiStageProgress(dialogController, uiReadyForProgress)
          ),
          OptimalLoadingProgressUiFloor,
          "Preparing UI"
        );
      case OptimalGameplayLoadingStage.Dialog:
        return new OptimalGameplayLoadingProgress(
          true,
          ResolveStagedLoadingProgress(
            OptimalLoadingProgressUiFloor,
            OptimalLoadingProgressDialogFloor,
            ResolveGameplayDialogStageProgress(dialogController, dialogReadyForProgress)
          ),
          OptimalLoadingProgressDialogFloor,
          "Preparing dialog"
        );
      default:
        return new OptimalGameplayLoadingProgress(
          true,
          OptimalLoadingProgressDialogFloor,
          LoadingProgressPreReadyCap,
          "Finalizing reveal"
        );
    }
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

  void ResetZeroPercentStallDebugState() {
    loadingZeroPercentStartedAt = -1f;
    loadingZeroPercentNextLogAt = -1f;
  }

  void ResetLoadingHeartbeatDebugState() {
    loadingHeartbeatStartedAt = -1f;
    loadingHeartbeatLastLoggedAt = -1f;
    loadingHeartbeatCount = 0;
    loadingHeartbeatNextLogAt = -1f;
  }

  void ResetLoadingStageStallState() {
    loadingStageStallStateInitialized = false;
    loadingStageStallPercent = -1;
    loadingStageStallDetail = "";
    loadingStageStallStage = default;
    loadingStageStallStartedAt = -1f;
    loadingStageStallNextLogAt = -1f;
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
    // Deferred requests are long-tail work. They must stay visible in diagnostics,
    // but they cannot own reveal readiness once the first-frame scope is ready.
    var outstanding = Mathf.Max(queue.queuedCount + queue.inFlightCount, 0);
    if (outstanding > maxOutstanding) return false;
    if (queue.inFlightCount > maxInFlight) return false;
    return true;
  }

  int CalculateLoadingPercentFromTextureQueue(out string statusDetail) {
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
    var locationActivationPending = LocationManager.HasPendingBlockingActivationWork;
    if (hasBlockingProgress && (blockingTotalCount > 0 || blockingProgress > 0f)) {
      loadingProgressObservedWork = true;
    }

    var blockingReady = hasBlockingProgress &&
      IsBlockingScopeReady(resolverIdle, playerReady, blockingCriticalReady, blockingHardBypassUsed, queue) &&
      !locationActivationPending;

    var hasOutstandingWork = hasBlockingProgress
      ? !blockingReady
      : (remainingWork > 0 || !resolverIdle || !playerReady || locationActivationPending);
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

    // Blocking progress should determine release gating, but the visible percentage
    // should still reflect real queue/session progress so startup does not appear frozen.
    var nonBlockingProgress = Mathf.Max(goalProgress, queueProgress);
    var targetProgress = hasBlockingProgress
      ? Mathf.Max(blockingProgress, Mathf.Min(nonBlockingProgress, 0.9f))
      : goalProgress;
    var optimalGameplayProgress = ResolveOptimalGameplayLoadingProgress(hasBlockingProgress, blockingProgress);
    if (optimalGameplayProgress.IsActive) {
      targetProgress = Mathf.Max(targetProgress, optimalGameplayProgress.TargetProgress);
      targetProgress = Mathf.Min(targetProgress, optimalGameplayProgress.ProgressCeiling);
    }
    if (!loadingProgressObservedWork && !hasBlockingProgress && !optimalGameplayProgress.IsActive) {
      targetProgress = 0f;
    }
    targetProgress = ClampLoadingTargetProgressForReveal(targetProgress, hasOutstandingWork, completionSettled);
    var targetPercent = Mathf.Clamp(targetProgress * 100f, 0f, 100f);
    var actualPercent = AdvanceLoadingPercentDisplay(targetPercent);
    statusDetail = ResolveLoadingStatusDetail(
      hasBlockingProgress,
      blockingProgress,
      resolverIdle,
      playerReady,
      locationActivationPending,
      outstanding,
      hasOutstandingWork,
      completionSettled,
      optimalGameplayProgress
    );

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
    MaybeLogZeroPercentStall(
      queue,
      session,
      outstanding,
      remainingWork,
      resolverIdle,
      playerReady,
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

  string ResolveLoadingStatusDetail(
    bool hasBlockingProgress,
    float blockingProgress,
    bool resolverIdle,
    bool playerReady,
    bool locationActivationPending,
    int outstanding,
    bool hasOutstandingWork,
    bool completionSettled,
    OptimalGameplayLoadingProgress optimalGameplayProgress
  ) {
    if (!string.IsNullOrWhiteSpace(loadingStatusOverride)) {
      return loadingStatusOverride;
    }
    if (optimalGameplayProgress.IsActive) {
      return optimalGameplayProgress.Detail;
    }
    if (!loadingProgressObservedWork && !hasBlockingProgress && outstanding <= 0) {
      return "Starting";
    }
    if (hasBlockingProgress && blockingProgress < 0.999f) {
      return "Warming critical";
    }
    if (locationActivationPending) {
      return "Activating location";
    }
    if (!playerReady) {
      return "Preparing player";
    }
    if (!resolverIdle) {
      return "Resolving assets";
    }
    if (ShouldKeepFinalizingRevealStatus(outstanding, hasOutstandingWork)) {
      return "Finalizing reveal";
    }
    if (outstanding > 0 || hasOutstandingWork) {
      return "Draining queue";
    }
    if (!completionSettled) {
      return "Settling";
    }
    return "Finalizing reveal";
  }

  static bool ShouldKeepFinalizingRevealStatus(int outstanding, bool hasOutstandingWork) {
    return hasOutstandingWork && outstanding <= 1;
  }

  void SetLoadingStatusOverride(string detail) {
    loadingStatusOverride = string.IsNullOrWhiteSpace(detail) ? "" : detail.Trim();
  }

  void ClearLoadingStatusOverride() {
    loadingStatusOverride = "";
  }

  float ClampLoadingTargetProgressForReveal(float targetProgress, bool hasOutstandingWork, bool completionSettled) {
    // Keep the visible percent inside the active stage band until the explicit
    // release step owns 100%. Auto-promoting to 99% here creates a false stall:
    // any brief "ready" blip becomes sticky even if the pipeline still reports
    // "Preparing UI", "Preparing dialog", or a late reveal blocker afterward.
    return Mathf.Min(targetProgress, LoadingProgressPreReadyCap);
  }

  int AdvanceLoadingPercentDisplay(float targetPercent) {
    if (!loadingPercentDisplayInitialized) {
      loadingPercentDisplayInitialized = true;
      loadingPercentDisplayValue = Mathf.Max(loadingPercent >= 0 ? loadingPercent : 0, 0);
    }

    // Loading feedback should not move backwards when queue/session totals resize mid-flight.
    var clampedTargetPercent = Mathf.Max(targetPercent, loadingPercentDisplayValue);
    var riseRate = Mathf.Max(loadingPercentRisePerSecond, 1f);
    var dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
    loadingPercentDisplayValue = Mathf.MoveTowards(
      loadingPercentDisplayValue,
      clampedTargetPercent,
      riseRate * dt
    );
    return Mathf.Clamp(Mathf.RoundToInt(loadingPercentDisplayValue), 0, 100);
  }

  static string BuildLoadingDisplayText(int percent, string detail) {
    if (string.IsNullOrWhiteSpace(detail)) {
      return percent + "%";
    }
    return percent + "% - " + detail.Trim();
  }

  void UpdateLoadingScreenText(int percent, string detail) {
    var normalizedDetail = string.IsNullOrWhiteSpace(detail) ? "" : detail.Trim();
    var detailChanged = !string.Equals(loadingStatusDetail, normalizedDetail, StringComparison.Ordinal);
    if (percent == loadingPercent && !detailChanged) return;
    loadingPercent = percent;
    loadingStatusDetail = normalizedDetail;
    SetLoadingText(BuildLoadingDisplayText(percent, normalizedDetail));
    if (!detailChanged || !ShouldLogLoadFlowWarnings()) return;

    var builder = BeginLoadFlowLog("[SingleSceneManager][LoadingStatus]");
    AppendLoadFlowInt(builder, "percent", percent);
    AppendLoadFlowField(builder, "detail", "'" + (string.IsNullOrWhiteSpace(normalizedDetail) ? "-" : normalizedDetail) + "'");
    AppendLoadFlowField(builder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
    AppendLoadFlowField(builder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(builder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    AppendGameplayLoadPipelineFields(builder);
    RuntimeLog.Log(builder.ToString());
  }

  void MaybeLogLoadingStageStall(int percent, string detail) {
    if (!ShouldLogLoadFlowWarnings()) return;
    var normalizedDetail = string.IsNullOrWhiteSpace(detail) ? "-" : detail.Trim();
    var stage = gameplayLoadingStageForLoad;
    var now = Time.realtimeSinceStartup;
    if (!loadingStageStallStateInitialized ||
        loadingStageStallPercent != percent ||
        loadingStageStallStage != stage ||
        !string.Equals(loadingStageStallDetail, normalizedDetail, StringComparison.Ordinal)) {
      loadingStageStallStateInitialized = true;
      loadingStageStallPercent = percent;
      loadingStageStallDetail = normalizedDetail;
      loadingStageStallStage = stage;
      loadingStageStallStartedAt = now;
      loadingStageStallNextLogAt = now + LoadingStageStallDelaySeconds;
      return;
    }
    if (loadingStageStallStartedAt < 0f) {
      loadingStageStallStartedAt = now;
      loadingStageStallNextLogAt = now + LoadingStageStallDelaySeconds;
      return;
    }
    if (now < loadingStageStallNextLogAt) return;
    loadingStageStallNextLogAt = now + LoadingStageStallLogIntervalSeconds;

    var remainingWork = GetRemainingStreamingWork(
      out var queue,
      out var session,
      out var outstanding,
      out var sessionRemaining
    );
    var deferredSnapshot = TextureResidencyCache.GetDeferredSnapshot();
    var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
    var playerReady = IsPlayerFirstFrameReady();
    var playerBlocker = playerReady || !TryGetPlayerFirstFrameBlocker(out var blocker, generateSummary: true) ? "-" : blocker;
    var hasBlockingProgress = TryGetBlockingProgressState(
      out var blockingProgress,
      out var blockingReadyCount,
      out var blockingTotalCount,
      out var blockingCriticalReady,
      out var blockingHardBypassUsed
    );
    var dialogController = ResolveGameplayDialogController();
    var stageName = stage.ToString();
    var builder = BeginLoadFlowLog("[SingleSceneManager][LoadingStageStall]");
    AppendLoadFlowFloat(builder, "unchanged_s", now - loadingStageStallStartedAt);
    AppendLoadFlowInt(builder, "percent", percent);
    AppendLoadFlowField(builder, "detail", "'" + ResolveLoadFlowValue(normalizedDetail) + "'");
    AppendLoadFlowField(builder, "stage", stageName);
    AppendLoadFlowField(builder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
    AppendLoadFlowField(builder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(builder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    AppendLoadFlowInt(builder, "session_completed", session.completedTotal);
    AppendLoadFlowInt(builder, "session_total", session.EffectiveTotal);
    AppendLoadFlowInt(builder, "session_remaining", sessionRemaining);
    AppendLoadFlowInt(builder, "remaining_work", remainingWork);
    AppendLoadFlowInt(builder, "queue_queued", queue.queuedCount);
    AppendLoadFlowInt(builder, "queue_in_flight", queue.inFlightCount);
    AppendLoadFlowInt(builder, "outstanding", outstanding);
    AppendLoadFlowInt(builder, "deferred_pending", deferredSnapshot.pendingCount);
    AppendLoadFlowBool(builder, "resolver_idle", resolverIdle);
    AppendLoadFlowBool(builder, "player_ready", playerReady);
    AppendLoadFlowField(builder, "player_blocker", ResolveLoadFlowValue(playerBlocker));
    AppendLoadFlowBool(builder, "blocking_mode", hasBlockingProgress);
    AppendLoadFlowInt(builder, "blocking_ready_count", blockingReadyCount);
    AppendLoadFlowInt(builder, "blocking_total_count", blockingTotalCount);
    AppendLoadFlowFloat(builder, "blocking_progress", blockingProgress);
    AppendLoadFlowBool(builder, "blocking_critical_ready", blockingCriticalReady);
    AppendLoadFlowBool(builder, "blocking_hard_bypass", blockingHardBypassUsed);
    AppendLoadFlowBool(builder, "location_activation_pending", LocationManager.HasPendingBlockingActivationWork);
    AppendLoadFlowBool(builder, "location_deferred_pending", LocationManager.HasPendingDeferredActivationWork);
    AppendLoadFlowBool(builder, "gameplay_interface_active", GameplayInterface != null && GameplayInterface.activeInHierarchy);
    AppendLoadFlowBool(builder, "dialog_controller_present", dialogController != null);
    AppendLoadFlowBool(builder, "dialog_controller_active", dialogController != null && dialogController.isActiveAndEnabled);
    AppendLoadFlowField(builder, "ui_blocker", ResolveLoadFlowValue(GetGameplayUiReadyBlockerSummary()));
    AppendLoadFlowField(builder, "dialog_blocker", ResolveLoadFlowValue(GetGameplayDialogReadyBlockerSummary()));
    AppendGameplayLoadPipelineFields(builder);
    RuntimeLog.Log(builder.ToString());
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
    ResetZeroPercentStallDebugState();
    ResetLoadingStageStallState();
    loadingPercent = -1;
    loadingStatusDetail = "Starting";
    loadingStatusOverride = "";
    loadingPercentDisplayInitialized = true;
    loadingPercentDisplayValue = 0f;
    SetLoadingText(BuildLoadingDisplayText(0, loadingStatusDetail));
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
      RuntimeLog.Log(
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
    ResetLoadingStageStallState();
    loadingPercent = -1;
    loadingStatusDetail = "Starting";
    loadingStatusOverride = "";
    loadingPercentDisplayInitialized = true;
    loadingPercentDisplayValue = 0f;
    UpdateLoadingScreenText(0, loadingStatusDetail);
    loadingOverlayChildrenReady = true;
    SetLoadingProgressUiActive(true);
    ScheduleLoadingProgressUiArmCheck(remainingWork > 0 || loadingProgressGoalTotal > 0);

    if (ShouldLogLoadingProgressDebug()) {
      RuntimeLog.Log(
        "[SingleSceneManager][LoadingProgress] activate_after_fade_in goal_total=" + loadingProgressGoalTotal +
        " queue_outstanding=" + outstanding +
        " remaining_work=" + remainingWork
      );
    }
  }

  void ScheduleLoadingProgressUiArmCheck(bool shouldCheck) {
    loadingProgressUiArmCheckFrame = shouldCheck ? Time.frameCount + 1 : -1;
  }

  void MaybeWarnLoadingProgressUiNotArmed() {
    if (loadingProgressUiArmCheckFrame < 0 || Time.frameCount < loadingProgressUiArmCheckFrame) return;
    loadingProgressUiArmCheckFrame = -1;
    if (IsLoadingProgressUiVisible() || !loadingOverlayChildrenReady) return;
    if (!(holdBlackscreenOpaqueDuringLoad || IsLoadingFlowActive())) return;
    if (!ShouldLogLoadFlowWarnings()) return;

    RuntimeLog.Log(
      "[SingleSceneManager][LoadingProgressUi] arm_failed" +
      " loading_root=" + (LoadingScreen != null && LoadingScreen.activeSelf ? 1 : 0) +
      " progress_ui=" + (IsLoadingProgressUiVisible() ? 1 : 0) +
      " overlay_reason=" + ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason) +
      " current_section=" + ResolveCurrentSection() +
      " slot=" + SaveSlotManager.slot
    );
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
    var gameplayUiReady = IsGameplayUiReadyForLoadingProgress();
    var gameplayDialogReady = IsGameplayDialogReadyForLoadingProgress();

    RuntimeLog.Log(
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
      " pipeline_player_ready=" + (IsGameplayPlayerBootstrapReady() ? 1 : 0) +
      " pipeline_location_ready=" + (!LocationManager.HasPendingBlockingActivationWork ? 1 : 0) +
      " pipeline_enemies_ready=" + (gameplayWarmGateCompletedForLoad ? 1 : 0) +
      " pipeline_ui_ready=" + (gameplayUiReady ? 1 : 0) +
      " pipeline_ui_blocker='" + GetGameplayUiReadyBlockerSummary() + "'" +
      " pipeline_dialog_ready=" + (gameplayDialogReady ? 1 : 0) +
      " pipeline_dialog_blocker='" + GetGameplayDialogReadyBlockerSummary() + "'" +
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

  static bool ShouldLogLoadFlowWarnings() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static bool ShouldLogLoadFlowDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    return ShouldLogLoadFlowWarnings();
  }

  public static bool ShouldLogGameplayPostRevealInputTrace() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!(Application.isEditor || Debug.isDebugBuild)) return false;
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) return true;
    return IsGameplayRevealInputTraceWindowActive;
  }

  void AppendActiveGameplayLoadFlowFields(StringBuilder builder) {
    if (builder == null || activeGameplayLoadFlowId <= 0) return;
    AppendLoadFlowInt(builder, "flow_id", activeGameplayLoadFlowId);
    AppendLoadFlowField(builder, "flow_kind", ResolveLoadFlowValue(activeGameplayLoadFlowKind));
    AppendLoadFlowField(builder, "flow_target_location", ResolveLoadFlowValue(activeGameplayLoadFlowTargetLocation));
  }

  StringBuilder BeginLoadFlowLog(string prefix) {
    var builder = loadFlowLogBuilder;
    builder.Clear();
    builder.Append(prefix);
    AppendActiveGameplayLoadFlowFields(builder);
    return builder;
  }

  static void AppendLoadFlowField(StringBuilder builder, string name, string value) {
    if (builder == null || string.IsNullOrWhiteSpace(name)) return;
    builder.Append(' ').Append(name).Append('=').Append(value ?? "");
  }

  static void AppendLoadFlowInt(StringBuilder builder, string name, int value) {
    if (builder == null || string.IsNullOrWhiteSpace(name)) return;
    builder.Append(' ').Append(name).Append('=').Append(value);
  }

  static void AppendLoadFlowFloat(StringBuilder builder, string name, float value, string format = "0.000") {
    if (builder == null || string.IsNullOrWhiteSpace(name)) return;
    builder.Append(' ').Append(name).Append('=').Append(value.ToString(format));
  }

  static void AppendLoadFlowBool(StringBuilder builder, string name, bool value) {
    if (builder == null || string.IsNullOrWhiteSpace(name)) return;
    builder.Append(' ').Append(name).Append('=').Append(value ? 1 : 0);
  }

  void LogGameplayLoadTiming(string stage, string result, float startedAt, string extraFields = "") {
    if (!ShouldLogLoadFlowWarnings()) return;
    var builder = BeginLoadFlowLog("[SingleSceneManager][LoadTiming]");
    AppendLoadFlowField(builder, "stage", ResolveLoadFlowValue(stage));
    AppendLoadFlowField(builder, "result", ResolveLoadFlowValue(result));
    AppendLoadFlowFloat(builder, "elapsed_ms", startedAt >= 0f ? Mathf.Max(Time.realtimeSinceStartup - startedAt, 0f) * 1000f : 0f, "0.0");
    AppendLoadFlowField(builder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
    AppendLoadFlowField(builder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(builder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    if (!string.IsNullOrWhiteSpace(extraFields)) {
      builder.Append(' ').Append(extraFields.Trim());
    }
    RuntimeLog.Log(builder.ToString());
  }

  void LogWarmGateConfig(WarmGateMode context, float requestedTimeoutSeconds, float requestedRatio, WarmRequest request, GearController playerController, EnemyController[] activeEnemies) {
    if (!ShouldLogLoadFlowWarnings()) return;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferred = TextureResidencyCache.GetDeferredSnapshot();
    var builder = BeginLoadFlowLog("[SingleSceneManager][WarmGateConfig]");
    AppendLoadFlowField(builder, "context", context.ToString());
    AppendLoadFlowFloat(builder, "requested_timeout_s", requestedTimeoutSeconds);
    AppendLoadFlowFloat(builder, "actual_timeout_s", request.timeoutSeconds);
    AppendLoadFlowFloat(builder, "hard_timeout_s", request.hardTimeoutSeconds);
    AppendLoadFlowFloat(builder, "requested_ratio", requestedRatio);
    AppendLoadFlowFloat(builder, "actual_ratio", request.requiredReadyRatio);
    AppendLoadFlowBool(builder, "allow_hard_bypass", request.allowHardTimeoutBypass);
    AppendLoadFlowBool(builder, "allow_critical_soft_timeout", request.allowCriticalReadySoftTimeout);
    AppendLoadFlowBool(builder, "player_present", playerController != null);
    AppendLoadFlowInt(builder, "player_warm_frames", request.playerWarmFrames);
    AppendLoadFlowInt(builder, "enemy_warm_frames", request.enemyWarmFrames);
    AppendLoadFlowInt(builder, "effect_warm_frames", request.effectWarmFrames);
    AppendLoadFlowInt(builder, "active_enemies", activeEnemies != null ? activeEnemies.Length : 0);
    AppendLoadFlowInt(builder, "critical_enemies", request.criticalEnemyControllers != null ? request.criticalEnemyControllers.Length : 0);
    AppendLoadFlowInt(builder, "enemy_archetypes", request.enemyArchetypePrefabsByType != null ? request.enemyArchetypePrefabsByType.Count : 0);
    AppendLoadFlowInt(builder, "critical_libraries", request.extraCriticalLibraries != null ? request.extraCriticalLibraries.Count : 0);
    AppendLoadFlowInt(builder, "critical_addresses", request.extraCriticalAddresses != null ? request.extraCriticalAddresses.Count : 0);
    AppendLoadFlowInt(builder, "critical_labels", request.extraCriticalLabels != null ? request.extraCriticalLabels.Count : 0);
    AppendLoadFlowInt(builder, "critical_asset_addresses", request.extraCriticalAssetAddresses != null ? request.extraCriticalAssetAddresses.Count : 0);
    AppendLoadFlowInt(builder, "critical_asset_labels", request.extraCriticalAssetLabels != null ? request.extraCriticalAssetLabels.Count : 0);
    AppendLoadFlowInt(builder, "warm_libraries", request.extraWarmLibraries != null ? request.extraWarmLibraries.Count : 0);
    AppendLoadFlowInt(builder, "warm_addresses", request.extraWarmAddresses != null ? request.extraWarmAddresses.Count : 0);
    AppendLoadFlowInt(builder, "warm_labels", request.extraWarmLabels != null ? request.extraWarmLabels.Count : 0);
    AppendLoadFlowInt(builder, "warm_asset_addresses", request.extraWarmAssetAddresses != null ? request.extraWarmAssetAddresses.Count : 0);
    AppendLoadFlowInt(builder, "warm_asset_labels", request.extraWarmAssetLabels != null ? request.extraWarmAssetLabels.Count : 0);
    AppendLoadFlowInt(builder, "critical_player_effects", request.criticalPlayerEffectKeys != null ? request.criticalPlayerEffectKeys.Count : 0);
    AppendLoadFlowInt(builder, "queue_queued", queue.queuedCount);
    AppendLoadFlowInt(builder, "queue_in_flight", queue.inFlightCount);
    AppendLoadFlowInt(builder, "deferred_pending", deferred.pendingCount);
    RuntimeLog.Log(builder.ToString());
  }

  string BuildPreUnlockThresholdFields() {
    ResolveBlockingReadyQueueThresholds(out var maxOutstanding, out var maxInFlight);
    return
      " min_wait_s=" + streamingIdleMinimumWaitSeconds.ToString("0.000") +
      " timeout_s=" + streamingIdleTimeoutSeconds.ToString("0.000") +
      " stable_required=" + Mathf.Max(streamingIdleStableFrames, 1) +
      " allowed_queued=" + Mathf.Max(streamingIdleAllowedQueued, 0) +
      " allowed_in_flight=" + Mathf.Max(streamingIdleAllowedInFlight, 0) +
      " blocking_max_outstanding=" + maxOutstanding +
      " blocking_max_in_flight=" + maxInFlight +
      " blocking_counts_deferred=0";
  }

  void LogPreUnlockConfig(string stage, bool prefetchVisibleSprites, bool warmAnimationsBeforeUnlock, float deadline) {
    if (!ShouldLogLoadFlowWarnings()) return;
    ResolveBlockingReadyQueueThresholds(out var maxOutstanding, out var maxInFlight);
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferred = TextureResidencyCache.GetDeferredSnapshot();
    var builder = BeginLoadFlowLog("[SingleSceneManager][PreUnlockConfig]");
    AppendLoadFlowField(builder, "stage", ResolveLoadFlowValue(stage));
    AppendLoadFlowBool(builder, "prefetch_visible", prefetchVisibleSprites);
    AppendLoadFlowBool(builder, "warm_animations", warmAnimationsBeforeUnlock);
    AppendLoadFlowFloat(builder, "blocking_budget_s", preUnlockBlockingBudgetSeconds);
    AppendLoadFlowField(builder, "deadline_remaining_s", float.IsInfinity(deadline) ? "inf" : Mathf.Max(deadline - Time.realtimeSinceStartup, 0f).ToString("0.000"));
    AppendLoadFlowFloat(builder, "min_wait_s", streamingIdleMinimumWaitSeconds);
    AppendLoadFlowFloat(builder, "timeout_s", streamingIdleTimeoutSeconds);
    AppendLoadFlowInt(builder, "stable_required", Mathf.Max(streamingIdleStableFrames, 1));
    AppendLoadFlowInt(builder, "allowed_queued", Mathf.Max(streamingIdleAllowedQueued, 0));
    AppendLoadFlowInt(builder, "allowed_in_flight", Mathf.Max(streamingIdleAllowedInFlight, 0));
    AppendLoadFlowInt(builder, "blocking_max_outstanding", maxOutstanding);
    AppendLoadFlowInt(builder, "blocking_max_in_flight", maxInFlight);
    AppendLoadFlowBool(builder, "enforce_zero_outstanding", enforceZeroOutstandingBeforeUnlock);
    AppendLoadFlowInt(builder, "prefetch_frames", preUnlockPrefetchAnimationFrames);
    AppendLoadFlowInt(builder, "prefetch_lookahead", preUnlockPrefetchLookAheadFrames);
    AppendLoadFlowInt(builder, "prefetch_max_addresses", preUnlockPrefetchMaxAddresses);
    AppendLoadFlowInt(builder, "prefetch_min_addresses", preUnlockPrefetchMinAddresses);
    AppendLoadFlowInt(builder, "prefetch_enqueue_budget", preUnlockPrefetchEnqueueBudgetPerFrame);
    AppendLoadFlowInt(builder, "playback_passes", preUnlockAnimationPlaybackPasses);
    AppendLoadFlowInt(builder, "frame_preload_passes", preUnlockAnimationFramePreloadPasses);
    AppendLoadFlowBool(builder, "reprefetch_after_animation", preUnlockReprefetchVisibleSpritesAfterAnimationWarmup);
    AppendLoadFlowInt(builder, "queue_queued", queue.queuedCount);
    AppendLoadFlowInt(builder, "queue_in_flight", queue.inFlightCount);
    AppendLoadFlowInt(builder, "deferred_pending", deferred.pendingCount);
    AppendLoadFlowBool(builder, "resolver_idle", SpriteRuntimeResolver.IsWarmupIdle());
    AppendLoadFlowBool(builder, "player_ready", IsPlayerFirstFrameReady());
    AppendLoadFlowBool(builder, "location_activation_pending", LocationManager.HasPendingBlockingActivationWork);
    AppendLoadFlowBool(builder, "location_deferred_pending", LocationManager.HasPendingDeferredActivationWork);
    RuntimeLog.Log(builder.ToString());
  }

  static string ResolveLoadFlowValue(string value, string fallback = "-") {
    return string.IsNullOrWhiteSpace(value) ? fallback : value;
  }

  void MaybeLogLoadingScreenHeartbeat() {
    var shouldLogWarnings = ShouldLogLoadFlowWarnings();
    var shouldLogVerbose = ShouldLogLoadFlowDebug();
    if (!shouldLogWarnings && !shouldLogVerbose) return;
    var now = Time.realtimeSinceStartup;
    if (loadingHeartbeatStartedAt < 0f) {
      loadingHeartbeatStartedAt = now;
      loadingHeartbeatLastLoggedAt = now;
      loadingHeartbeatCount = 0;
      loadingHeartbeatNextLogAt = now;
    }
    if (now < loadingHeartbeatNextLogAt) return;
    var scheduledAt = loadingHeartbeatNextLogAt;
    var gapSeconds = Mathf.Max(now - loadingHeartbeatLastLoggedAt, 0f);
    var overdueSeconds = Mathf.Max(now - scheduledAt, 0f);
    var missedBeats = overdueSeconds > 0f
      ? Mathf.FloorToInt(overdueSeconds / LoadingHeartbeatLogIntervalSeconds)
      : 0;
    if (gapSeconds > LoadingHeartbeatAcceptableGapSeconds && shouldLogWarnings) {
      var warningBuilder = BeginLoadFlowLog("[SingleSceneManager][LoadingHeartbeatGap]");
      AppendLoadFlowFloat(warningBuilder, "gap_s", gapSeconds);
      AppendLoadFlowFloat(warningBuilder, "acceptable_s", LoadingHeartbeatAcceptableGapSeconds);
      AppendLoadFlowInt(warningBuilder, "missed_beats", missedBeats);
      AppendLoadFlowInt(warningBuilder, "investigation_required", 1);
      AppendLoadFlowField(warningBuilder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
      AppendLoadFlowField(warningBuilder, "current_section", ResolveCurrentSection().ToString());
      AppendLoadFlowField(warningBuilder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
      RuntimeLog.Log(warningBuilder.ToString());
    }
    loadingHeartbeatLastLoggedAt = now;
    loadingHeartbeatCount++;
    loadingHeartbeatNextLogAt = loadingHeartbeatStartedAt + (loadingHeartbeatCount * LoadingHeartbeatLogIntervalSeconds);
    while (loadingHeartbeatNextLogAt <= now) {
      loadingHeartbeatCount++;
      loadingHeartbeatNextLogAt = loadingHeartbeatStartedAt + (loadingHeartbeatCount * LoadingHeartbeatLogIntervalSeconds);
    }
    if (!shouldLogVerbose) return;

    var remainingWork = GetRemainingStreamingWork(
      out var queue,
      out var session,
      out var outstanding,
      out _
    );
    var deferredSnapshot = TextureResidencyCache.GetDeferredSnapshot();
    var resolverIdle = SpriteRuntimeResolver.IsWarmupIdle();
    var playerReady = IsPlayerFirstFrameReady();
    var blockerSummary = playerReady || !TryGetPlayerFirstFrameBlocker(out var blocker, generateSummary: true) ? "" : blocker;
    var hasBlockingProgress = TryGetBlockingProgressState(
      out var blockingProgress,
      out var blockingReadyCount,
      out var blockingTotalCount,
      out var blockingCriticalReady,
      out var blockingHardBypassUsed
    );
    var locationActivationPending = LocationManager.HasPendingBlockingActivationWork;

    var infoBuilder = BeginLoadFlowLog("[SingleSceneManager][LoadingHeartbeat]");
    AppendLoadFlowFloat(infoBuilder, "elapsed_s", now - loadingHeartbeatStartedAt);
    AppendLoadFlowFloat(infoBuilder, "gap_s", gapSeconds);
    AppendLoadFlowFloat(infoBuilder, "scheduled_at_s", Mathf.Max(scheduledAt - loadingHeartbeatStartedAt, 0f));
    AppendLoadFlowInt(infoBuilder, "missed_beats", missedBeats);
    AppendLoadFlowInt(infoBuilder, "frame", Time.frameCount);
    AppendLoadFlowFloat(infoBuilder, "dt_unscaled", Time.unscaledDeltaTime);
    AppendLoadFlowField(infoBuilder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
    AppendLoadFlowField(infoBuilder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(infoBuilder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    AppendLoadFlowBool(infoBuilder, "loading_root", LoadingScreen != null && LoadingScreen.activeSelf);
    AppendLoadFlowBool(infoBuilder, "black_hold", holdBlackscreenOpaqueDuringLoad);
    AppendLoadFlowBool(infoBuilder, "black_visible", loadingBlackscreen != null && loadingBlackscreen.activeInHierarchy);
    AppendLoadFlowBool(infoBuilder, "progress_ui", IsLoadingProgressUiVisible());
    AppendLoadFlowBool(infoBuilder, "overlay_children_ready", loadingOverlayChildrenReady);
    AppendLoadFlowInt(infoBuilder, "percent", loadingPercent);
    AppendLoadFlowInt(infoBuilder, "session_completed", session.completedTotal);
    AppendLoadFlowInt(infoBuilder, "session_total", session.EffectiveTotal);
    AppendLoadFlowInt(infoBuilder, "remaining_work", remainingWork);
    AppendLoadFlowInt(infoBuilder, "queue_queued", queue.queuedCount);
    AppendLoadFlowInt(infoBuilder, "queue_in_flight", queue.inFlightCount);
    AppendLoadFlowInt(infoBuilder, "outstanding", outstanding);
    AppendLoadFlowInt(infoBuilder, "deferred_pending", deferredSnapshot.pendingCount);
    AppendLoadFlowBool(infoBuilder, "resolver_idle", resolverIdle);
    AppendLoadFlowBool(infoBuilder, "player_ready", playerReady);
    AppendLoadFlowBool(infoBuilder, "player_animation_held", playerAnimationHeldForLoadingOverlay);
    AppendLoadFlowField(infoBuilder, "player_blocker", playerReady ? "-" : ResolveLoadFlowValue(blockerSummary));
    AppendLoadFlowBool(infoBuilder, "blocking_mode", hasBlockingProgress);
    AppendLoadFlowInt(infoBuilder, "blocking_ready_count", blockingReadyCount);
    AppendLoadFlowInt(infoBuilder, "blocking_total_count", blockingTotalCount);
    AppendLoadFlowFloat(infoBuilder, "blocking_progress", blockingProgress);
    AppendLoadFlowBool(infoBuilder, "blocking_critical_ready", blockingCriticalReady);
    AppendLoadFlowBool(infoBuilder, "blocking_hard_bypass", blockingHardBypassUsed);
    AppendLoadFlowBool(infoBuilder, "location_activation_pending", locationActivationPending);
    AppendGameplayLoadPipelineFields(infoBuilder);
    RuntimeLog.Log(infoBuilder.ToString());
  }

  void LogStartGameRequest(bool isNewGame, SaveData loadedSlot) {
    if (!ShouldLogLoadFlowDebug()) return;
    var savedLocation = loadedSlot != null && loadedSlot.ContainsKey("location")
      ? Convert.ToString(loadedSlot["location"])
      : "";
    RuntimeLog.Log(
      "[SingleSceneManager][LoadStart] kind=" + (isNewGame ? "new_game" : "load_save") +
      " slot=" + SaveSlotManager.slot +
      " slot_exists=" + (SaveSlotManager.CurrentSlotExists() ? 1 : 0) +
      " save_key_count=" + (loadedSlot != null ? loadedSlot.Count : 0) +
      " saved_location=" + (string.IsNullOrWhiteSpace(savedLocation) ? "-" : savedLocation.Trim()) +
      " current_location=" + (string.IsNullOrWhiteSpace(LocationManager.currentLocation) ? "-" : LocationManager.currentLocation) +
      " current_section=" + ResolveCurrentSection() +
      " overlay_reason=" + (string.IsNullOrWhiteSpace(SpriteStreamingLoadingState.ActiveReason) ? "-" : SpriteStreamingLoadingState.ActiveReason)
    );
  }

  void LogLoadStateDispatch(string stage) {
    if (!ShouldLogLoadFlowDebug()) return;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var deferredSnapshot = TextureResidencyCache.GetDeferredSnapshot();
    var playerReady = IsPlayerFirstFrameReady();
    RuntimeLog.Log(
      "[SingleSceneManager][LoadState] stage=" + (string.IsNullOrWhiteSpace(stage) ? "unspecified" : stage.Trim()) +
      " slot=" + SaveSlotManager.slot +
      " overlay_reason=" + (string.IsNullOrWhiteSpace(SpriteStreamingLoadingState.ActiveReason) ? "-" : SpriteStreamingLoadingState.ActiveReason) +
      " current_location=" + (string.IsNullOrWhiteSpace(LocationManager.currentLocation) ? "-" : LocationManager.currentLocation) +
      " queue_queued=" + queue.queuedCount +
      " queue_in_flight=" + queue.inFlightCount +
      " deferred_pending=" + deferredSnapshot.pendingCount +
      " player_ready=" + (playerReady ? 1 : 0) +
      " current_section=" + ResolveCurrentSection()
    );
  }

  void LogLocationLoadRequest(string requestedLocationId, string resolvedLocationId) {
    if (!ShouldLogLoadFlowDebug()) return;
    RuntimeLog.Log(
      "[SingleSceneManager][LocationLoad] requested=" + (string.IsNullOrWhiteSpace(requestedLocationId) ? "-" : requestedLocationId.Trim()) +
      " resolved=" + (string.IsNullOrWhiteSpace(resolvedLocationId) ? "-" : resolvedLocationId.Trim()) +
      " current_before=" + (string.IsNullOrWhiteSpace(LocationManager.currentLocation) ? "-" : LocationManager.currentLocation) +
      " overlay_reason=" + (string.IsNullOrWhiteSpace(SpriteStreamingLoadingState.ActiveReason) ? "-" : SpriteStreamingLoadingState.ActiveReason) +
      " current_section=" + ResolveCurrentSection()
    );
  }

  void LogLocationUpdate(string previousLocationId, string currentLocationId) {
    if (!ShouldLogLoadFlowDebug()) return;
    RuntimeLog.Log(
      "[SingleSceneManager][LocationLoad] updated previous=" + (string.IsNullOrWhiteSpace(previousLocationId) ? "-" : previousLocationId.Trim()) +
      " current=" + (string.IsNullOrWhiteSpace(currentLocationId) ? "-" : currentLocationId.Trim()) +
      " overlay_reason=" + (string.IsNullOrWhiteSpace(SpriteStreamingLoadingState.ActiveReason) ? "-" : SpriteStreamingLoadingState.ActiveReason) +
      " current_section=" + ResolveCurrentSection()
    );
  }

  void MaybeLogZeroPercentStall(
    TextureResidencyCache.QueueSnapshot queue,
    TextureResidencyCache.SessionSnapshot session,
    int outstanding,
    int remainingWork,
    bool resolverIdle,
    bool playerReady,
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
    if (!ShouldLogLoadFlowWarnings()) return;
    if (!loadingOverlayChildrenReady || actualPercent > 0) {
      ResetZeroPercentStallDebugState();
      return;
    }

    var overlayActive = holdBlackscreenOpaqueDuringLoad || SpriteStreamingLoadingState.IsLoadingOverlayActive;
    if (!overlayActive) {
      ResetZeroPercentStallDebugState();
      return;
    }

    var now = Time.realtimeSinceStartup;
    if (loadingZeroPercentStartedAt < 0f) {
      loadingZeroPercentStartedAt = now;
      loadingZeroPercentNextLogAt = now + LoadingZeroPercentStallDelaySeconds;
      return;
    }

    if (now < loadingZeroPercentNextLogAt) return;
    loadingZeroPercentNextLogAt = now + LoadingZeroPercentStallLogIntervalSeconds;
    var deferredSnapshot = TextureResidencyCache.GetDeferredSnapshot();
    var blockerSummary = playerReady || !TryGetPlayerFirstFrameBlocker(out var blocker, generateSummary: true) ? "" : blocker;
    var warningBuilder = BeginLoadFlowLog("[SingleSceneManager][LoadingZeroStall]");
    AppendLoadFlowFloat(warningBuilder, "elapsed_s", now - loadingZeroPercentStartedAt);
    AppendLoadFlowField(warningBuilder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
    AppendLoadFlowField(warningBuilder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(warningBuilder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    AppendLoadFlowInt(warningBuilder, "slot", SaveSlotManager.slot);
    AppendLoadFlowFloat(warningBuilder, "target_progress", targetProgress);
    AppendLoadFlowInt(warningBuilder, "actual_percent", actualPercent);
    AppendLoadFlowInt(warningBuilder, "goal_total", loadingProgressGoalTotal);
    AppendLoadFlowInt(warningBuilder, "goal_remaining_best", loadingProgressGoalBestRemaining);
    AppendLoadFlowInt(warningBuilder, "remaining_work", remainingWork);
    AppendLoadFlowInt(warningBuilder, "session_expected", session.expectedTotal);
    AppendLoadFlowInt(warningBuilder, "session_scheduled", session.scheduledTotal);
    AppendLoadFlowInt(warningBuilder, "session_completed", session.completedTotal);
    AppendLoadFlowInt(warningBuilder, "session_total", session.EffectiveTotal);
    AppendLoadFlowInt(warningBuilder, "queue_queued", queue.queuedCount);
    AppendLoadFlowInt(warningBuilder, "queue_in_flight", queue.inFlightCount);
    AppendLoadFlowInt(warningBuilder, "deferred_pending", deferredSnapshot.pendingCount);
    AppendLoadFlowInt(warningBuilder, "outstanding", outstanding);
    AppendLoadFlowBool(warningBuilder, "blocking_mode", usingBlockingProgress);
    AppendLoadFlowInt(warningBuilder, "blocking_ready_count", blockingReadyCount);
    AppendLoadFlowInt(warningBuilder, "blocking_total_count", blockingTotalCount);
    AppendLoadFlowFloat(warningBuilder, "blocking_progress", blockingProgress);
    AppendLoadFlowBool(warningBuilder, "blocking_critical_ready", blockingCriticalReady);
    AppendLoadFlowBool(warningBuilder, "blocking_hard_bypass", blockingHardBypassUsed);
    AppendLoadFlowBool(warningBuilder, "blocking_ready", blockingReady);
    AppendLoadFlowBool(warningBuilder, "resolver_idle", resolverIdle);
    AppendLoadFlowBool(warningBuilder, "player_ready", playerReady);
    AppendLoadFlowField(warningBuilder, "player_blocker", playerReady ? "-" : ResolveLoadFlowValue(blockerSummary));
    AppendLoadFlowBool(warningBuilder, "progress_ui", IsLoadingProgressUiVisible());
    AppendLoadFlowBool(warningBuilder, "loading_light", loadingLightObject != null && loadingLightObject.activeSelf);
    AppendGameplayLoadPipelineFields(warningBuilder);
    RuntimeLog.Log(warningBuilder.ToString());
  }

  void MaybeLogStreamingIdleWaitState(
    ref float nextLogAt,
    float elapsed,
    float minimumWaitSeconds,
    float timeoutSeconds,
    int stableFrames,
    int stableFramesRequired,
    TextureResidencyCache.QueueSnapshot queue,
    bool resolverIdle,
    bool playerReady,
    bool queueIdle,
    bool hasBlockingProgress,
    int blockingReadyCount,
    int blockingTotalCount,
    float blockingProgress,
    bool blockingCriticalReady,
    bool blockingHardBypassUsed,
    bool blockingReady,
    bool locationActivationPending,
    bool locationDeferredPending,
    bool warmupDone
  ) {
    if (!ShouldLogLoadFlowDebug()) return;
    var now = Time.realtimeSinceStartup;
    if (nextLogAt < 0f) {
      nextLogAt = now + LoadingWaitStateLogIntervalSeconds;
      return;
    }
    if (now < nextLogAt) return;
    nextLogAt = now + LoadingWaitStateLogIntervalSeconds;
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    var blockerSummary = playerReady || !TryGetPlayerFirstFrameBlocker(out var blocker, generateSummary: true) ? "" : blocker;
    var playerController = ResolvePlayerAnimationController();
    var infoBuilder = BeginLoadFlowLog("[SingleSceneManager][WaitForIdle]");
    AppendLoadFlowFloat(infoBuilder, "elapsed_s", elapsed);
    AppendLoadFlowFloat(infoBuilder, "min_wait_s", minimumWaitSeconds);
    AppendLoadFlowFloat(infoBuilder, "timeout_s", timeoutSeconds);
    AppendLoadFlowInt(infoBuilder, "stable_frames", stableFrames);
    AppendLoadFlowInt(infoBuilder, "stable_required", stableFramesRequired);
    AppendLoadFlowField(infoBuilder, "overlay_reason", ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason));
    AppendLoadFlowField(infoBuilder, "current_section", ResolveCurrentSection().ToString());
    AppendLoadFlowField(infoBuilder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    AppendLoadFlowInt(infoBuilder, "current_percent", loadingPercent);
    AppendLoadFlowBool(infoBuilder, "queue_idle", queueIdle);
    AppendLoadFlowBool(infoBuilder, "resolver_idle", resolverIdle);
    AppendLoadFlowBool(infoBuilder, "player_ready", playerReady);
    AppendLoadFlowBool(infoBuilder, "player_animation_held", playerAnimationHeldForLoadingOverlay);
    AppendLoadFlowField(
      infoBuilder,
      "player_animation",
      playerController != null && !string.IsNullOrWhiteSpace(playerController.CurrentAnimation)
        ? playerController.CurrentAnimation.Trim()
        : "-"
    );
    AppendLoadFlowField(infoBuilder, "player_blocker", playerReady ? "-" : ResolveLoadFlowValue(blockerSummary));
    AppendLoadFlowInt(infoBuilder, "queue_queued", queue.queuedCount);
    AppendLoadFlowInt(infoBuilder, "queue_in_flight", queue.inFlightCount);
    AppendLoadFlowInt(infoBuilder, "deferred_pending", deferredPending);
    AppendLoadFlowBool(infoBuilder, "blocking_mode", hasBlockingProgress);
    AppendLoadFlowInt(infoBuilder, "blocking_ready_count", blockingReadyCount);
    AppendLoadFlowInt(infoBuilder, "blocking_total_count", blockingTotalCount);
    AppendLoadFlowFloat(infoBuilder, "blocking_progress", blockingProgress);
    AppendLoadFlowBool(infoBuilder, "blocking_critical_ready", blockingCriticalReady);
    AppendLoadFlowBool(infoBuilder, "blocking_hard_bypass", blockingHardBypassUsed);
    AppendLoadFlowBool(infoBuilder, "blocking_ready", blockingReady);
    AppendLoadFlowBool(infoBuilder, "location_activation_pending", locationActivationPending);
    AppendLoadFlowBool(infoBuilder, "location_deferred_pending", locationDeferredPending);
    AppendLoadFlowBool(infoBuilder, "warmup_done", warmupDone);
    RuntimeLog.Log(infoBuilder.ToString());
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
    ResetZeroPercentStallDebugState();
    ResetLoadingStageStallState();
    loadingProgressPeakOutstanding = Mathf.Max(loadingProgressPeakOutstanding, 1);
    loadingPercentDisplayInitialized = true;
    loadingPercentDisplayValue = 100f;
    loadingPercent = 100;
    loadingStatusDetail = "Ready";
    loadingStatusOverride = "";
    SetLoadingText(BuildLoadingDisplayText(100, loadingStatusDetail));
  }

  void SetLoadingText(string value) {
    if (loadingText == null) return;
    var textValue = value ?? "";
    if (string.Equals(loadingText.content, textValue, StringComparison.Ordinal)) return;
    loadingText.content = textValue;
    loadingText.Generate();
    if (ShouldLogLoadingProgressDebug()) {
      RuntimeLog.Log(
        "[SingleSceneManager][LoadingText] value='" + textValue +
        "' visible=" + (loadingTextObject != null && loadingTextObject.activeSelf ? 1 : 0) +
        " overlay_reason=" + ResolveLoadFlowValue(SpriteStreamingLoadingState.ActiveReason) +
        " current_section=" + ResolveCurrentSection()
      );
    }
  }

  void SetLoadingRootActive(bool active) {
    if (LoadingScreen == null || LoadingScreen.activeSelf == active) return;
    LoadingScreen.SetActive(active);
  }

  void SetLoadingLightActive(bool active) {
    if (loadingLightObject == null || loadingLightObject.activeSelf == active) return;
    loadingLightObject.SetActive(active);
  }

  void SetLoadingProgressUiActive(bool active) {
    if (active &&
        (!holdBlackscreenOpaqueDuringLoad || !loadingHeldProgressBlackscreenVisualApplied)) {
      ApplyLoadingBlackscreenVisual(loadingBlackscreenRenderer, 1f, 1f);
      loadingHeldProgressBlackscreenVisualApplied =
        holdBlackscreenOpaqueDuringLoad && loadingBlackscreenRenderer != null;
    }
    else if (!active) {
      loadingHeldProgressBlackscreenVisualApplied = false;
    }
    if (loadingCircle != null && loadingCircle.activeSelf != active) {
      loadingCircle.SetActive(active);
    }
    if (loadingTextObject != null && loadingTextObject.activeSelf != active) {
      loadingTextObject.SetActive(active);
    }
  }

  bool IsLoadingProgressUiVisible() {
    return (loadingCircle != null && loadingCircle.activeSelf) ||
           (loadingTextObject != null && loadingTextObject.activeSelf);
  }

  void DisableLoadingUiFeedback(bool clearText = true, bool includeLoadingLight = true) {
    loadingUiFeedbackActive = false;
    loadingOverlayChildrenReady = false;
    loadingStatusOverride = "";
    loadingProgressUiArmCheckFrame = -1;
    if (includeLoadingLight) {
      SetLoadingLightActive(false);
    }
    SetLoadingProgressUiActive(false);
    if (clearText) {
      SetLoadingText("");
    }
  }

  void PrepareLoadingScreenCarrier(bool clearText = true) {
    loadingUiFeedbackActive = false;
    loadingOverlayChildrenReady = false;
    loadingProgressUiArmCheckFrame = -1;
    SetLoadingRootActive(true);
    PrimePersistentFontAtlasPins("prepare_loading_screen_carrier");
    PrimeLoadingTextRuntimeAssets("prepare_loading_screen_carrier");
    SetLoadingLightActive(false);
    SetLoadingProgressUiActive(false);
    if (clearText) {
      SetLoadingText("");
    }
  }

  void ReleaseLoadingScreenIfIdle() {
    if (holdBlackscreenOpaqueDuringLoad || SpriteStreamingLoadingState.IsLoadingOverlayActive) return;
    SetLoadingRootActive(false);
  }

  bool IsBlockingScopeReady(
    bool resolverIdle,
    bool playerReady,
    bool criticalReady,
    bool hardBypassUsed,
    TextureResidencyCache.QueueSnapshot queue
  ) {
    if (!resolverIdle || !playerReady) return false;
    if (hardBypassUsed) {
      if (!IsCriticalScopeReadyForReveal()) return false;
    } else {
      if (!criticalReady) return false;
    }
    var deferredPending = TextureResidencyCache.GetDeferredSnapshot().pendingCount;
    return IsQueueWithinBlockingReadyThresholds(queue, deferredPending);
  }
}
