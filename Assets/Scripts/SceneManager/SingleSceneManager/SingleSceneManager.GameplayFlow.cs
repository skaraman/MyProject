using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class SingleSceneManager {

  IEnumerator StartGameFlowRoutine(bool isNewGame, SaveData loadedSlot) {
    var context = isNewGame ? WarmGateMode.StartGame : WarmGateMode.LoadSave;
    var overlayTag = isNewGame ? "StartGameFlow" : "LoadGameFlow";
    yield return RunGameplayFlowRoutine(
      overlayTag: overlayTag,
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
      resolveLocationForStart: startupInDebugGameplay
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
    var previousSection = ResolveCurrentSection();
    BeginGameplayLoadFlowTrace(
      overlayTag,
      warmContext,
      isNewGame,
      loadedSlot,
      resolveLocationForStart,
      previousSection
    );
    ConfigureRuntimeContentPacksForGameplayFlow(warmContext, isNewGame, overlayTag + "_begin");
    var gameplayStateAppliedUnderBlack = false;
    pendingGameplayLocationId = "";
    ResetGameplayLoadStageTracking();
    StopDeferredPostRevealWarmup("begin_gameplay_flow");
    pendingRevealSection = Section.Gameplay;
    LogSectionTransitionState("begin", previousSection, Section.Gameplay, overlayTag, true);
    SpriteStreamingLoadingState.BeginLoadingOverlay(overlayTag);
    // Start resolver manifest work as soon as the overlay appears so shard warmup can
    // overlap the fade/black window instead of waiting for the first sprite request.
    SpriteRuntimeResolver.WarmupLibraries(Array.Empty<string>());
    ResetLoadingProgressForPhase(force: true);
    if (switchInputMapToNone) {
      _SwitchMap("none");
    }
    yield return FadeToBlackBeforeLoadRoutine();
    SetLoadingLightActive(true);
    BeginLoadingProgressUiAfterFadeIn();
    LogSectionTransitionState("loading_phase", previousSection, Section.Gameplay, overlayTag, true);

    if (resolveLocationForStart) {
      ResolveAndApplyLocationForStart(isNewGame, loadedSlot);
    }

    yield return PrewarmGameplayPlayerBootstrapAssets(overlayTag);
    EnsureGameplayPlayerBootstrap(overlayTag);

    if (!isNewGame) {
      if (applyGameplayStateBeforeWarmGate && !gameplayStateAppliedUnderBlack) {
        ApplyGameplayStateUnderBlack();
        gameplayStateAppliedUnderBlack = true;
      }
      LogLoadStateDispatch("before_load_game_message");
      ApplySavedGameplayStateUnderLoadingOverlay();
      RuntimeContentPackResolver.ConfigureForCurrentRuntimeState("after_load_state");
      LogLoadStateDispatch("after_load_game_message");
      yield return null;
      LogLoadStateDispatch("after_load_game_one_frame");
    }

    if (applyGameplayStateBeforeWarmGate) {
      if (!gameplayStateAppliedUnderBlack) {
        ApplyGameplayStateUnderBlack();
        gameplayStateAppliedUnderBlack = true;
      }
      if (isNewGame) {
        PrepareNewGameRuntimeStateUnderLoadingOverlay();
        RuntimeContentPackResolver.ConfigureForCurrentRuntimeState("after_new_game_state");
      }
      // Freeze the player on a stable first-contact frame while the overlay is up.
      // Otherwise the runtime loader chases advancing animation frames behind black.
      HoldPlayerAnimationForLoadingOverlay("warm_gate");
      yield return WaitForGameplayWarmGatePrerequisites(sendReadyForSpawns);
    }

    var allowGameplayUnlock = true;
    yield return RunScenarioWarmGate(
      warmContext,
      warmTimeoutSeconds,
      warmRequiredRatio,
      allow => allowGameplayUnlock = allow
    );
    if (!allowGameplayUnlock) {
      if (!applyGameplayStateBeforeWarmGate) {
        ApplyGameplayStateUnderBlack();
      }
      var revealHandoffStartedAt = Time.realtimeSinceStartup;
      LogRevealHandoff("warm_gate_release", revealHandoffStartedAt);
      yield return UnlockGameplayFromBlackRoutine(overlayTag, revealHandoffStartedAt);
      yield break;
    }

    if (!applyGameplayStateBeforeWarmGate) {
      ApplyGameplayStateUnderBlack();
      HoldPlayerAnimationForLoadingOverlay("wait_for_streaming_idle");
    }

    yield return WaitForStreamingIdleBeforeUnlock(prefetchVisibleSprites: true, warmAnimationsBeforeUnlock: true);
    var revealHandoffStartedAtAfterIdle = Time.realtimeSinceStartup;
    LogRevealHandoff("streaming_idle_complete", revealHandoffStartedAtAfterIdle);
    yield return UnlockGameplayFromBlackRoutine(overlayTag, revealHandoffStartedAtAfterIdle);
  }

  void ConfigureRuntimeContentPacksForGameplayFlow(WarmGateMode warmContext, bool isNewGame, string source) {
    if (warmContext == WarmGateMode.GearApplyReturn) {
      RuntimeContentPackResolver.ConfigureForCurrentRuntimeState(source);
      return;
    }

    RuntimeContentPackResolver.ConfigureForGameplayStart(isNewGame, source);
  }

  void ApplySavedGameplayStateUnderLoadingOverlay() {
    EnsureGameplayPlayerBootstrap("load_game");
    var player = ResolvePlayerGearController();
    var sceneActive = Scene != null && Scene.activeInHierarchy;
    var playerActive = player != null && player.gameObject.activeInHierarchy;
    if (!sceneActive || !playerActive) {
      if (ShouldLogLoadFlowWarnings()) {
        Debug.LogWarning(
          "[SingleSceneManager][LoadState] gameplay hierarchy inactive before save-state apply" +
          " scene_active=" + (sceneActive ? 1 : 0) +
          " player_active=" + (playerActive ? 1 : 0) +
          " player=" + (player != null ? player.gameObject.name : "-") +
          " current_section=" + ResolveCurrentSection() +
          " overlay_reason=" + (string.IsNullOrWhiteSpace(SpriteStreamingLoadingState.ActiveReason) ? "-" : SpriteStreamingLoadingState.ActiveReason)
        );
      }
      ApplyGameplayStateUnderBlack();
      EnsureGameplayPlayerBootstrap("load_game_after_activate");
    }

    var characterState = ResolvePlayerCharacterState();
    if (characterState != null) {
      LogLoadStateDispatch("direct_load_game");
      characterState.LoadState();
    }
    else {
      LogLoadStateDispatch("dispatch_load_game");
      MessageBus.Send(CharacterMessageTopics.LoadGame);
    }
    if (!ShouldLogLoadingProgressDebug()) return;
    Debug.Log(
      "[SingleSceneManager][LoadState] Applied save-state under loading overlay" +
      " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
      " warm_gate_running=" + (StreamingWarmOrchestrator.IsWarmGateRunning ? 1 : 0) +
      " slot=" + SaveSlotManager.slot
    );
  }

  void PrepareNewGameRuntimeStateUnderLoadingOverlay() {
    EnsureGameplayPlayerBootstrap("new_game");
    var player = ResolvePlayerGearController();
    var characterState = ResolvePlayerCharacterState();

    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][NewGameState] stage=begin" +
        " slot=" + SaveSlotManager.slot +
        " character_state=" + (characterState != null ? 1 : 0) +
        " " + DescribeGearController(player)
      );
    }

    if (characterState == null) {
      Debug.LogWarning(
        "[SingleSceneManager][NewGameState] stage=missing_character_state" +
        " slot=" + SaveSlotManager.slot +
        " " + DescribeGearController(player)
      );
      return;
    }

    characterState.InitializeRuntimeStateForNewGame();

    if (!ShouldLogLoadFlowDebug()) return;
    var playerReady = IsPlayerFirstFrameReady();
    var blockerSummary = playerReady || !TryGetPlayerFirstFrameBlocker(out var blocker) ? "-" : blocker;
    Debug.Log(
      "[SingleSceneManager][NewGameState] stage=complete" +
      " slot=" + SaveSlotManager.slot +
      " player_ready=" + (playerReady ? 1 : 0) +
      " player_blocker=" + blockerSummary
    );
  }

  void MaybeLogGameplayWarmGatePrereqState(
    ref float nextLogAt,
    string stage,
    bool playerBootstrapReady,
    bool locationActivationPending,
    bool shouldExpectEnemyWarmStage,
    int archetypeCount
  ) {
    if (!ShouldLogLoadFlowDebug()) return;
    var now = Time.realtimeSinceStartup;
    if (nextLogAt < 0f) {
      nextLogAt = now;
    }
    if (now < nextLogAt) return;
    nextLogAt = now + GameplayWarmGatePrereqLogIntervalSeconds;
    var infoBuilder = BeginLoadFlowLog("[SingleSceneManager][WarmGatePrereq]");
    AppendLoadFlowField(infoBuilder, "stage", string.IsNullOrWhiteSpace(stage) ? "-" : stage.Trim());
    AppendLoadFlowField(infoBuilder, "current_location", ResolveLoadFlowValue(LocationManager.currentLocation));
    AppendLoadFlowBool(infoBuilder, "player_bootstrap_ready", playerBootstrapReady);
    AppendLoadFlowBool(infoBuilder, "location_activation_pending", locationActivationPending);
    AppendLoadFlowBool(infoBuilder, "expect_enemy_stage", shouldExpectEnemyWarmStage);
    AppendLoadFlowInt(infoBuilder, "enemy_archetype_count", archetypeCount);
    AppendLoadFlowBool(infoBuilder, "ready_for_spawns_sent", gameplayReadyForSpawnsSentForLoad);
    AppendLoadFlowBool(infoBuilder, "warm_gate_started", gameplayWarmGateStartedForLoad);
    AppendLoadFlowBool(infoBuilder, "warm_gate_completed", gameplayWarmGateCompletedForLoad);
    Debug.Log(infoBuilder.ToString());
  }

  IEnumerator WaitForGameplayWarmGatePrerequisites(bool sendReadyForSpawns) {
    if (!Application.isPlaying) yield break;
    var nextLogAt = -1f;
    var enemyArchetypeWaitStartedAt = -1f;
    var startedAt = Time.realtimeSinceStartup;
    var prerequisitesReady = false;
    while (true) {
      var playerBootstrapReady = IsPlayerHierarchyReady();
      var locationActivationPending = LocationManager.HasPendingBlockingActivationWork;
      var shouldExpectEnemyWarmStage = ShouldExpectEnemyWarmStageForCurrentLocation();
      var archetypeCount = shouldExpectEnemyWarmStage ? ResolveCurrentLocationLoadingArchetypeCount() : 0;
      var shouldWaitForEnemyArchetypes = playerBootstrapReady &&
                                         !locationActivationPending &&
                                         shouldExpectEnemyWarmStage &&
                                         archetypeCount <= 0;
      if (!playerBootstrapReady) {
        enemyArchetypeWaitStartedAt = -1f;
        SetLoadingStatusOverride("Preparing player");
        if (ShouldTimeoutGameplayWarmGatePrerequisites(
          startedAt,
          "player",
          playerBootstrapReady,
          locationActivationPending,
          shouldExpectEnemyWarmStage,
          archetypeCount
        )) {
          break;
        }
        MaybeLogGameplayWarmGatePrereqState(
          ref nextLogAt,
          "player",
          playerBootstrapReady,
          locationActivationPending,
          shouldExpectEnemyWarmStage,
          archetypeCount
        );
        yield return null;
        continue;
      }

      if (locationActivationPending) {
        enemyArchetypeWaitStartedAt = -1f;
        SetLoadingStatusOverride("Activating location");
        if (ShouldTimeoutGameplayWarmGatePrerequisites(
          startedAt,
          "location",
          playerBootstrapReady,
          locationActivationPending,
          shouldExpectEnemyWarmStage,
          archetypeCount
        )) {
          break;
        }
        MaybeLogGameplayWarmGatePrereqState(
          ref nextLogAt,
          "location",
          playerBootstrapReady,
          locationActivationPending,
          shouldExpectEnemyWarmStage,
          archetypeCount
        );
        yield return null;
        continue;
      }

      if (shouldWaitForEnemyArchetypes) {
        if (enemyArchetypeWaitStartedAt < 0f) {
          enemyArchetypeWaitStartedAt = Time.realtimeSinceStartup;
        }
        var waitedSeconds = Time.realtimeSinceStartup - enemyArchetypeWaitStartedAt;
        if (waitedSeconds < GameplayWarmGateEnemyArchetypeWaitSeconds) {
          SetLoadingStatusOverride(ResolveGameplayWarmStageDetail(shouldExpectEnemyWarmStage));
          MaybeLogGameplayWarmGatePrereqState(
            ref nextLogAt,
            "enemies",
            playerBootstrapReady,
            locationActivationPending,
            shouldExpectEnemyWarmStage,
            archetypeCount
          );
          yield return null;
          continue;
        }
      }

      ClearLoadingStatusOverride();
      prerequisitesReady = true;
      MaybeLogGameplayWarmGatePrereqState(
        ref nextLogAt,
        "ready",
        playerBootstrapReady,
        locationActivationPending,
        shouldExpectEnemyWarmStage,
        archetypeCount
      );
      break;
    }

    if (!prerequisitesReady) {
      ClearLoadingStatusOverride();
      yield break;
    }
    if (!sendReadyForSpawns || gameplayReadyForSpawnsSentForLoad) yield break;
    gameplayReadyForSpawnsSentForLoad = true;
    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][WarmGatePrereq] Dispatch ReadyForSpawns" +
        " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation) +
        " enemy_archetype_count=" + ResolveCurrentLocationLoadingArchetypeCount()
      );
    }
    MessageBus.Send("ReadyForSpawns");
  }

  bool ShouldTimeoutGameplayWarmGatePrerequisites(
    float startedAt,
    string stage,
    bool playerBootstrapReady,
    bool locationActivationPending,
    bool shouldExpectEnemyWarmStage,
    int archetypeCount
  ) {
    var timeoutSeconds = Mathf.Max(GameplayWarmGatePrereqTimeoutSeconds, 1f);
    var elapsed = Time.realtimeSinceStartup - startedAt;
    if (elapsed < timeoutSeconds) {
      return false;
    }

    if (ShouldLogLoadFlowWarnings()) {
      Debug.LogWarning(
        "[SingleSceneManager][WarmGatePrereq] timeout" +
        " stage=" + ResolveLoadFlowValue(stage) +
        " elapsed_s=" + elapsed.ToString("0.000") +
        " timeout_s=" + timeoutSeconds.ToString("0.000") +
        " player_bootstrap_ready=" + (playerBootstrapReady ? 1 : 0) +
        " location_activation_pending=" + (locationActivationPending ? 1 : 0) +
        " expect_enemy_stage=" + (shouldExpectEnemyWarmStage ? 1 : 0) +
        " enemy_archetype_count=" + archetypeCount +
        " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation)
      );
    }

    return true;
  }

  void HoldPlayerAnimationForLoadingOverlay(string reason) {
    if (playerAnimationHeldForLoadingOverlay) return;

    var player = ResolvePlayerGearController();
    var controller = player != null ? player.Controller : null;
    if (controller == null) {
      if (ShouldLogLoadFlowWarnings()) {
        Debug.Log(
          "[SingleSceneManager][PlayerAnimationHold] stage=skip_missing_controller" +
          " reason=" + (string.IsNullOrWhiteSpace(reason) ? "-" : reason.Trim()) +
          " " + DescribeGearController(player)
        );
      }
      return;
    }

    var targetAnimation = ResolvePlayerLoadingOverlayAnimation(player, controller);
    var restarted = false;
    if (!string.IsNullOrWhiteSpace(targetAnimation)) {
      restarted = controller.PlayAnimation(targetAnimation, forceRestart: true, resolveInterrupts: false);
    }

    if (!restarted && string.IsNullOrWhiteSpace(controller.CurrentAnimation)) {
      if (ShouldLogLoadFlowWarnings()) {
        Debug.LogWarning(
          "[SingleSceneManager][PlayerAnimationHold] stage=skip_missing_animation" +
          " reason=" + (string.IsNullOrWhiteSpace(reason) ? "-" : reason.Trim()) +
          " target_animation=" + (string.IsNullOrWhiteSpace(targetAnimation) ? "-" : targetAnimation.Trim()) +
          " " + DescribeGearController(player)
        );
      }
      return;
    }

    controller.PauseAnimation();
    playerAnimationHeldForLoadingOverlay = true;

    if (!ShouldLogLoadFlowWarnings()) return;
    Debug.Log(
      "[SingleSceneManager][PlayerAnimationHold] stage=applied" +
      " reason=" + (string.IsNullOrWhiteSpace(reason) ? "-" : reason.Trim()) +
      " target_animation=" + (string.IsNullOrWhiteSpace(targetAnimation) ? "-" : targetAnimation.Trim()) +
      " current_animation=" + (string.IsNullOrWhiteSpace(controller.CurrentAnimation) ? "-" : controller.CurrentAnimation.Trim()) +
      " restarted=" + (restarted ? 1 : 0) +
      " " + DescribeGearController(player)
    );
  }

  void ResumePlayerAnimationAfterLoadingOverlay(string reason) {
    if (!playerAnimationHeldForLoadingOverlay) return;

    var player = ResolvePlayerGearController();
    var controller = player != null ? player.Controller : null;
    playerAnimationHeldForLoadingOverlay = false;
    if (controller == null) {
      if (ShouldLogLoadFlowWarnings()) {
        Debug.Log(
          "[SingleSceneManager][PlayerAnimationHold] stage=release_missing_controller" +
          " reason=" + (string.IsNullOrWhiteSpace(reason) ? "-" : reason.Trim()) +
          " " + DescribeGearController(player)
        );
      }
      return;
    }

    controller.ResumeAnimation();
    if (!ShouldLogLoadFlowWarnings()) return;
    Debug.Log(
      "[SingleSceneManager][PlayerAnimationHold] stage=released" +
      " reason=" + (string.IsNullOrWhiteSpace(reason) ? "-" : reason.Trim()) +
      " current_animation=" + (string.IsNullOrWhiteSpace(controller.CurrentAnimation) ? "-" : controller.CurrentAnimation.Trim()) +
      " " + DescribeGearController(player)
    );
  }

  static string ResolvePlayerLoadingOverlayAnimation(GearController player, AnimationController controller) {
    if (controller != null && !string.IsNullOrWhiteSpace(controller.CurrentAnimation)) {
      return controller.CurrentAnimation.Trim();
    }
    if (player != null && !string.IsNullOrWhiteSpace(player.defaultAnimation)) {
      return player.defaultAnimation.Trim();
    }
    return null;
  }

  IEnumerator FadeToBlackBeforeLoadRoutine() {
    SetSceneObjectLightsActive(false);
    SetLoadingBlackscreenHold(false);
    BeginLoadingProgressGoalAtFadeStart();
    PrepareLoadingScreenCarrier();
    if (blackscreen != null) {
      PlayBlackscreen("alphaIn");
    }
    else {
      ForceBlackscreenVisible(true);
    }
    var waitSeconds = Mathf.Max(fadeToBlackSeconds, 0f);
    if (waitSeconds > 0f) {
      yield return FadeToBlackDelay;
    }
    ForceBlackscreenVisible(true);
    SetLoadingBlackscreenHold(true);
  }

  IEnumerator RunScenarioWarmGate(
    WarmGateMode context,
    float timeoutSeconds,
    float requiredRatio,
    Action<bool> onComplete
  ) {
    var gateStartedAt = Time.realtimeSinceStartup;
    ResetBlockingProgressState();
    gameplayWarmGateStartedForLoad = false;
    gameplayWarmGateCompletedForLoad = false;
    var allowGameplayUnlock = true;
    var leadSeconds = Mathf.Max(fadeLeadSeconds, 0f);
    if (leadSeconds > 0f) {
      yield return WarmGateLeadDelay;
    }

    if (!useScenarioWarmGate || !Application.isPlaying) {
      gameplayWarmGateCompletedForLoad = true;
      if (fallbackTransitionSeconds > 0f) {
        yield return FallbackTransitionDelay;
      }
      LogGameplayLoadTiming(
        "warm_gate",
        "skipped",
        gateStartedAt,
        "context=" + context +
        " use_scenario_warm_gate=" + (useScenarioWarmGate ? 1 : 0) +
        " playing=" + (Application.isPlaying ? 1 : 0)
      );
      onComplete?.Invoke(allowGameplayUnlock);
      yield break;
    }

    var playerController = ResolvePlayerGearController();
    var activeEnemies = ResolveActiveEnemyControllers();
    var request = BuildWarmRequest(context, timeoutSeconds, requiredRatio, playerController, activeEnemies);
    LogWarmGateConfig(context, timeoutSeconds, requiredRatio, request, playerController, activeEnemies);
    var orchestrator = StreamingWarmOrchestrator.Instance;
    if (orchestrator == null) {
      gameplayWarmGateCompletedForLoad = true;
      if (fallbackTransitionSeconds > 0f) {
        yield return FallbackTransitionDelay;
      }
      LogGameplayLoadTiming(
        "warm_gate",
        "missing_orchestrator",
        gateStartedAt,
        "context=" + context
      );
      onComplete?.Invoke(allowGameplayUnlock);
      yield break;
    }

    var completed = false;
    var hasResult = false;
    WarmResult warmResult = default;
    gameplayWarmGateStartedForLoad = true;
    if (ShouldLogLoadFlowWarnings()) {
      Debug.Log(
        "[SingleSceneManager][WarmGate] stage=begin" +
        " context=" + context +
        " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation) +
        " ready_for_spawns_sent=" + (gameplayReadyForSpawnsSentForLoad ? 1 : 0)
      );
    }
    orchestrator.Run(request, result => {
      warmResult = result;
      hasResult = true;
      completed = true;
    });

    while (!completed && orchestrator.IsRunning) {
      yield return null;
    }

    if (!completed && !hasResult) {
      gameplayWarmGateCompletedForLoad = true;
      if (fallbackTransitionSeconds > 0f) {
        yield return FallbackTransitionDelay;
      }
      LogGameplayLoadTiming(
        "warm_gate",
        "no_result",
        gateStartedAt,
        "context=" + context +
        " orchestrator_running=" + (orchestrator.IsRunning ? 1 : 0)
      );
      onComplete?.Invoke(allowGameplayUnlock);
      yield break;
    }

    CaptureBlockingProgressStateFromWarmResult(warmResult);
    gameplayWarmGateCompletedForLoad = true;
    if (ShouldLogLoadFlowWarnings()) {
      Debug.Log(
        "[SingleSceneManager][WarmGate] stage=complete" +
        " context=" + context +
        " reached_ready=" + (warmResult.reachedReadyThreshold ? 1 : 0) +
        " hard_bypass=" + (warmResult.hardTimeoutBypassUsed ? 1 : 0) +
        " critical_ready=" + warmResult.criticalReadyCount +
        "/" + warmResult.criticalTotalCount
      );
    }
    allowGameplayUnlock = warmResult.reachedReadyThreshold || warmResult.hardTimeoutBypassUsed || IsCriticalScopeReadyForReveal();
    LogGameplayLoadTiming(
      "warm_gate",
      allowGameplayUnlock ? "complete" : "blocked",
      gateStartedAt,
      "context=" + context +
      " orchestrator_ms=" + warmResult.elapsedMs.ToString("0.0") +
      " reached_ready=" + (warmResult.reachedReadyThreshold ? 1 : 0) +
      " hard_bypass=" + (warmResult.hardTimeoutBypassUsed ? 1 : 0) +
      " ready=" + warmResult.readyCount + "/" + warmResult.totalCount +
      " critical=" + warmResult.criticalReadyCount + "/" + warmResult.criticalTotalCount +
      " requested=" + warmResult.requestedAddressCount +
      " failure=" + ResolveLoadFlowValue(warmResult.failureReason) +
      " reveal_critical_ready=" + (IsCriticalScopeReadyForReveal() ? 1 : 0) +
      " warm_plan_critical_ready=" + (warmResult.playerCriticalReady ? 1 : 0)
    );
    onComplete?.Invoke(allowGameplayUnlock);
  }
}
