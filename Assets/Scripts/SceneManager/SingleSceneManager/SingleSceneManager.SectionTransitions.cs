using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class SingleSceneManager {
  void LateUpdate() {
    if (!holdBlackscreenOpaqueDuringLoad) return;
    ForceBlackscreenVisible(true);
  }

  void FixedUpdate() {
    TickLoadingStallEmergencyUnlock();
  }

  void StartGame() {
    ClearPauseDialogResumeState("start_game");
    ReleasePreUnlockResidentPins("start_game");
    StopStartupFadeWatchdog();
    StopStartupGameplayFlow();
    StopSectionTransition(clearLoadingOverlay: true, restoreVisibleState: true);
    InvalidateCachedPlayerGearController("start_game");
    SaveData loadedSlot = null;
    var isNewGame = _isNewGame();
    if (!isNewGame) {
      loadedSlot = SaveSlotManager.Load("slot");
      if (loadedSlot != null && loadedSlot.ContainsKey("playtimeHours") && loadedSlot.ContainsKey("playtimeMinutes") && loadedSlot.ContainsKey("playtimeSeconds")) {
        autoSaver.SetPlaytime((int)loadedSlot["playtimeHours"], (int)loadedSlot["playtimeMinutes"], (int)loadedSlot["playtimeSeconds"]);
      }
    }
    LogStartGameRequest(isNewGame, loadedSlot);
    autoSaver.enableTimeTracking = true;
    _SwitchMap("none");
    if (resumeGameplayRoutine != null) {
      StopCoroutine(resumeGameplayRoutine);
      resumeGameplayRoutine = null;
      SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    }
    if (startGameRoutine != null) {
      StopCoroutine(startGameRoutine);
      startGameRoutine = null;
      SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    }
    startGameRoutine = StartCoroutine(StartGameFlowRoutine(isNewGame, loadedSlot));
  }

  void OpenLoadMenu() {
    ClearPauseDialogResumeState("open_load_menu");
    QueueMenuRuntimeAssetWarmup("open_load_menu");
    SwitchSectionInstantly(Section.LoadMenu, "open_load_menu");
  }

  void CloseLoadMenu() {
    if (ResolveCurrentSection() != Section.LoadMenu) return;
    SwitchSectionInstantly(Section.MainMenu, "close_load_menu");
  }

  void OpenSettingsMenu() {
    var openedFromPause = ResolveCurrentSection() == Section.Pause;
    if (!openedFromPause) {
      ClearPauseDialogResumeState("open_settings_menu");
    }
    QueueMenuRuntimeAssetWarmup("open_settings_menu");
    PrepareSettingsMenuState(openedFromPause ? Section.Pause : Section.MainMenu);
    Debug.Log(
      "[SingleSceneManager][SettingsMenu] action=open" +
      " from=" + ResolveCurrentSection() +
      " return_target=" + settingsReturnTarget +
      " instant_switch=1"
    );
    SwitchSectionInstantly(Section.SettingsMenu, "open_settings_menu");
  }

  void CloseSettingsMenu() {
    if (SettingsMenu != null && !SettingsMenu.activeInHierarchy) return;
    var targetSection = settingsReturnTarget == Section.Pause ? Section.Pause : Section.MainMenu;
    Debug.Log(
      "[SingleSceneManager][SettingsMenu] action=close" +
      " to=" + targetSection +
      " instant_switch=1"
    );
    SwitchSectionInstantly(targetSection, "close_settings_menu");
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

  void PrepareSettingsMenuState(Section returnTarget) {
    settingsReturnTarget = returnTarget == Section.Pause ? Section.Pause : Section.MainMenu;
    settingsHoveredTarget = null;
    settingsCloseButton = null;
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

  Transform FindDirectChildByName(Transform root, string name) {
    if (root == null || string.IsNullOrWhiteSpace(name)) return null;

    for (var i = 0; i < root.childCount; i++) {
      var child = root.GetChild(i);
      if (child == null) continue;
      if (string.Equals(child.name, name, StringComparison.Ordinal)) {
        return child;
      }
    }

    return null;
  }

  static SectionDescriptor GetSectionDescriptor(Section section) {
    switch (section) {
      case Section.MainMenu:
        return new SectionDescriptor("mainMenu", sceneActiveByDefault: false, restoreSceneLightsByDefault: false, resetPauseAppearanceRevision: true);
      case Section.LoadMenu:
        return new SectionDescriptor("loadMenu", sceneActiveByDefault: false, restoreSceneLightsByDefault: false, resetPauseAppearanceRevision: false);
      case Section.SettingsMenu:
        return new SectionDescriptor("settingsMenu", sceneActiveByDefault: false, restoreSceneLightsByDefault: false, resetPauseAppearanceRevision: false);
      case Section.Gameplay:
        return new SectionDescriptor("gameplay", sceneActiveByDefault: true, restoreSceneLightsByDefault: true, resetPauseAppearanceRevision: true);
      case Section.Pause:
        return new SectionDescriptor("pauseMenu", sceneActiveByDefault: true, restoreSceneLightsByDefault: false, resetPauseAppearanceRevision: false);
      default:
        return new SectionDescriptor("", sceneActiveByDefault: false, restoreSceneLightsByDefault: false, resetPauseAppearanceRevision: false);
    }
  }

  Section ResolveActiveSectionFromHierarchy() {
    if (PauseMenu != null && PauseMenu.activeInHierarchy) return Section.Pause;
    if (SettingsMenu != null && SettingsMenu.activeInHierarchy) return Section.SettingsMenu;
    if (LoadMenu != null && LoadMenu.activeInHierarchy) return Section.LoadMenu;
    if (MainMenu != null && MainMenu.activeInHierarchy) return Section.MainMenu;
    if (GameplayInterface != null && GameplayInterface.activeInHierarchy) return Section.Gameplay;
    return Section.None;
  }

  Section ResolveCurrentSection() {
    var activeSection = ResolveActiveSectionFromHierarchy();
    if (activeSection != Section.None) {
      currentSection = activeSection;
      return activeSection;
    }
    return currentSection;
  }

  bool ShouldSectionKeepSceneActive(Section section) {
    var descriptor = GetSectionDescriptor(section);
    if (section == Section.SettingsMenu && settingsReturnTarget == Section.Pause) {
      return true;
    }
    return descriptor.sceneActiveByDefault;
  }

  bool ShouldRestoreSceneLightsForSection(Section section) {
    var descriptor = GetSectionDescriptor(section);
    if (section == Section.SettingsMenu && settingsReturnTarget == Section.Pause) {
      return true;
    }
    return descriptor.restoreSceneLightsByDefault;
  }

  void HideAllSectionsForTransition(Section targetSection) {
    SetActiveSafe(MainMenu, false);
    SetActiveSafe(LoadMenu, false);
    SetActiveSafe(SettingsMenu, false);
    SetActiveSafe(GameplayInterface, false);
    SetActiveSafe(PauseMenu, false);
    SetActiveSafe(Scene, ShouldSectionKeepSceneActive(targetSection));
  }

  void ApplySectionActivation(Section section) {
    currentSection = section;
    pendingRevealSection = Section.None;
    ApplySceneTimeForSection(section, "apply_section_activation");
    HideAllSectionsForTransition(section);
    if (ShouldSectionKeepSceneActive(section)) {
      EnsureDamageEntitiesRootEnabled("apply_section_activation:" + section);
    }

    switch (section) {
      case Section.MainMenu:
        SetActiveSafe(MainMenu, true);
        break;
      case Section.LoadMenu:
        SetActiveSafe(LoadMenu, true);
        break;
      case Section.SettingsMenu:
        SetActiveSafe(SettingsMenu, true);
        break;
      case Section.Gameplay:
        SetActiveSafe(GameplayInterface, true);
        pauseMenuOpenAppearanceRevision = -1;
        break;
      case Section.Pause:
        SetActiveSafe(PauseMenu, true);
        break;
    }

    if (GetSectionDescriptor(section).resetPauseAppearanceRevision) {
      pauseMenuOpenAppearanceRevision = -1;
    }
  }

  void ApplySceneTimeForSection(Section section, string reason) {
    var shouldFreezeSceneTime = ShouldFreezeSceneTimeForSection(section);
    TimeScale.SetSceneMultiplier(shouldFreezeSceneTime ? 0f : 1f, reason + ":" + section);
  }

  bool ShouldFreezeSceneTimeForSection(Section section) {
    return section == Section.Pause ||
      (section == Section.SettingsMenu && settingsReturnTarget == Section.Pause) ||
      (section == Section.Gameplay && dialogInputOverrideActive);
  }

  void RefreshSceneTimeForCurrentUiState(string reason) {
    var section = ResolveCurrentSection();
    if (section == Section.None) {
      section = currentSection;
    }

    ApplySceneTimeForSection(section, reason);
  }

  void ApplyInputForSection(Section section) {
    var inputMap = ResolveInputMapForSection(section);
    if (string.IsNullOrWhiteSpace(inputMap)) return;
    _SwitchMap(inputMap);
  }

  string ResolveInputMapForSection(Section section) {
    var inputMap = GetSectionDescriptor(section).inputMap;
    if (section == Section.Gameplay) {
      return ResolveGameplayInputMap();
    }
    return inputMap;
  }

  string ResolveGameplayInputMap() {
    if (dialogInputOverrideActive) {
      return "dialog";
    }

    var dialogController = ResolveGameplayDialogController();
    if (dialogController != null && dialogController.HasPendingLocationDialog) {
      return "none";
    }

    return GetSectionDescriptor(Section.Gameplay).inputMap;
  }

  GameObject ResolveSceneObjectLights() {
    if (sceneObjectLights != null) return sceneObjectLights;
    if (Scene == null) return null;
    var lights = FindDirectChildByName(Scene.transform, "LIGHTS");
    if (lights == null) {
      lights = FindDirectChildByName(Scene.transform, "Lights");
    }
    if (lights == null) {
      lights = FindDirectChildByName(Scene.transform, "SCENEOBJECT LIGHTS");
    }
    if (lights == null) {
      lights = FindChildByName(Scene.transform, "SCENEOBJECT LIGHTS");
    }
    if (lights == null) return null;
    sceneObjectLights = lights.gameObject;
    return sceneObjectLights;
  }

  GameObject ResolveDamageEntitiesRoot() {
    if (IsLiveSceneObject(damageEntitiesRoot)) {
      return damageEntitiesRoot;
    }

    damageEntitiesRoot = null;
    if (Scene == null) {
      return null;
    }

    var sceneObjectsRoot = FindChildByName(Scene.transform, "SCENEOBJECTS");
    var damageEntities = sceneObjectsRoot != null
      ? FindChildByName(sceneObjectsRoot, "DAMAGEENTITIES")
      : FindChildByName(Scene.transform, "DAMAGEENTITIES");
    if (damageEntities == null) {
      return null;
    }

    damageEntitiesRoot = damageEntities.gameObject;
    return damageEntitiesRoot;
  }

  void EnsureDamageEntitiesRootEnabled(string source) {
    var damageRoot = ResolveDamageEntitiesRoot();
    if (damageRoot == null) {
      return;
    }

    var rootWasInactive = !damageRoot.activeSelf;
    if (rootWasInactive) {
      damageRoot.SetActive(true);
    }

    var manager = gameplayProjectileManager;
    if (!IsLiveSceneComponent(manager)) {
      manager = damageRoot.GetComponent<ProjectileManager>();
      if (manager == null) {
        manager = damageRoot.GetComponentInChildren<ProjectileManager>(true);
      }
      gameplayProjectileManager = manager;
    }

    var managerWasDisabled = manager != null && !manager.enabled;
    if (managerWasDisabled) {
      manager.enabled = true;
    }

    if ((rootWasInactive || managerWasDisabled) && ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][ProjectileManager] stage=ensure_damage_root_enabled" +
        " source=" + ResolveLoadFlowValue(source) +
        " root_active=" + (damageRoot.activeSelf ? 1 : 0) +
        " scene_active=" + (Scene != null && Scene.activeInHierarchy ? 1 : 0) +
        " manager=" + (manager != null ? manager.gameObject.name : "-") +
        " manager_enabled=" + (manager != null && manager.enabled ? 1 : 0)
      );
    }
  }

  ProjectileManager ResolveGameplayProjectileManagerInternal() {
    EnsureDamageEntitiesRootEnabled("resolve_projectile_manager");
    if (IsLiveSceneComponent(gameplayProjectileManager)) {
      return gameplayProjectileManager;
    }

    var previousManager = gameplayProjectileManager;
    gameplayProjectileManager = null;
    var damageRoot = ResolveDamageEntitiesRoot();
    if (damageRoot != null) {
      gameplayProjectileManager = damageRoot.GetComponent<ProjectileManager>();
      if (gameplayProjectileManager == null) {
        gameplayProjectileManager = damageRoot.GetComponentInChildren<ProjectileManager>(true);
      }
    }

    if (gameplayProjectileManager == null && Scene != null) {
      gameplayProjectileManager = Scene.GetComponentInChildren<ProjectileManager>(true);
    }

    if (gameplayProjectileManager != null &&
        !ReferenceEquals(previousManager, gameplayProjectileManager) &&
        ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][ProjectileManager] stage=resolved" +
        " manager=" + gameplayProjectileManager.gameObject.name +
        " root=" + (damageRoot != null ? damageRoot.name : "-")
      );
    }

    return gameplayProjectileManager;
  }

  static bool IsLiveSceneObject(GameObject candidate) {
    return candidate != null &&
           candidate.scene.IsValid() &&
           (candidate.hideFlags & HideFlags.HideAndDontSave) == 0;
  }

  static bool IsLiveSceneComponent(Component candidate) {
    return candidate != null && IsLiveSceneObject(candidate.gameObject);
  }

  Spawner ResolveGameplaySpawner() {
    if (IsLiveSceneComponent(cachedSpawner)) {
      return cachedSpawner;
    }

    cachedSpawner = null;
    if (Scene != null) {
      cachedSpawner = Scene.GetComponentInChildren<Spawner>(true);
    }
    if (cachedSpawner == null) {
      cachedSpawner = FindAnyObjectByType<Spawner>();
    }
    return cachedSpawner;
  }

  void SetSceneObjectLightsActive(bool active) {
    var lights = ResolveSceneObjectLights();
    if (lights == null || lights.activeSelf == active) return;
    lights.SetActive(active);
  }

  void RestoreSceneLightingForCurrentActivation() {
    // Safety net for aborted/forced transitions that skip the normal fade-out completion.
    var section = ResolveActiveSectionFromHierarchy();
    if (section == Section.None) {
      section = currentSection;
    }
    var shouldEnableLights = Scene != null &&
                             Scene.activeInHierarchy &&
                             ShouldRestoreSceneLightsForSection(section);
    SetSceneObjectLightsActive(shouldEnableLights);
  }

  void OpenGameplay() {
    ReleasePreUnlockResidentPins("open_gameplay");
    StopStartupFadeWatchdog();
    StopStartupGameplayFlow();
    StopSectionTransition(clearLoadingOverlay: true, restoreVisibleState: true);
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

    StartSectionTransition(new SectionTransitionRequest(
      Section.Gameplay,
      BuildSectionOverlayTag(Section.Gameplay),
      requestMainMenuLocation: false,
      waitForStreamingIdle: false,
      showProgressUi: false,
      switchInputMapToNone: true
    ));
  }

  void OpenMainMenu() {
    ClearPauseDialogResumeState("open_main_menu");
    ReleasePreUnlockResidentPins("open_main_menu");
    RuntimeAssetCache.ClearSessionScope("open_main_menu");
    QueueMenuRuntimeAssetWarmup("open_main_menu", includeLocationProfile: false);
    StartSectionTransition(new SectionTransitionRequest(
      Section.MainMenu,
      BuildSectionOverlayTag(Section.MainMenu),
      requestMainMenuLocation: true,
      waitForStreamingIdle: true,
      showProgressUi: true,
      switchInputMapToNone: true
    ));
  }

  void OpenPauseMenu() {
    var gear = ResolvePlayerGearController();
    pauseMenuOpenAppearanceRevision = gear != null ? gear.AppearanceRevision : -1;
    QueueMenuRuntimeAssetWarmup("open_pause_menu");
    if (dialogInputOverrideActive) {
      pendingPauseDialogResumeToken = 0;
      activePauseDialogResumeToken = nextPauseDialogResumeToken++;
      if (ShouldLogPauseDialogResumeDebug()) {
        Debug.Log(
          "[SingleSceneManager][PauseDialogResume] suspend_token=" + activePauseDialogResumeToken +
          " section=" + ResolveCurrentSection()
        );
      }
    }
    SwitchSectionInstantly(Section.Pause, "open_pause_menu");
  }

  void ClosePauseMenu() {
    if (ResolveCurrentSection() != Section.Pause) return;
    pendingPauseDialogResumeToken = activePauseDialogResumeToken;
    activePauseDialogResumeToken = 0;
    if (pendingPauseDialogResumeToken > 0 && ShouldLogPauseDialogResumeDebug()) {
      Debug.Log(
        "[SingleSceneManager][PauseDialogResume] resume_token=" + pendingPauseDialogResumeToken +
        " section=" + ResolveCurrentSection()
      );
    }
    SwitchSectionInstantly(Section.Gameplay, "close_pause_menu");
  }

  void OnDialogStarted(object payload) {
    dialogInputOverrideActive = true;
    RefreshSceneTimeForCurrentUiState("dialog_started");
    if (ShouldLogGameplayRuntimeDebug()) {
      Debug.Log(
        "[SingleSceneManager][DialogInput] active=1 source='" + (payload != null ? payload.ToString() : "") +
        "' current_section=" + ResolveCurrentSection() +
        " scene_multiplier=" + TimeScale.GetSceneMultiplier()
      );
    }
    ApplyInputMapForCurrentUiState(preferGameplayWhenNoUi: false);
  }

  void OnDialogFinished(object payload) {
    dialogInputOverrideActive = false;
    RefreshSceneTimeForCurrentUiState("dialog_finished");
    if (ShouldLogGameplayRuntimeDebug()) {
      Debug.Log(
        "[SingleSceneManager][DialogInput] active=0 source='" + (payload != null ? payload.ToString() : "") +
        "' current_section=" + ResolveCurrentSection() +
        " scene_multiplier=" + TimeScale.GetSceneMultiplier()
      );
    }
    ApplyInputMapForCurrentUiState(preferGameplayWhenNoUi: false);
  }

  void StartSectionTransition(SectionTransitionRequest request) {
    if (request.targetSection == Section.None) return;
    StopStartupFadeWatchdog();
    StopStartupGameplayFlow();
    StopSectionTransition(clearLoadingOverlay: true, restoreVisibleState: true);
    if (request.targetSection != Section.Gameplay) {
      ResetGameplayLoadStageTracking();
    }

    var current = ResolveCurrentSection();
    if (current == request.targetSection && !request.requestMainMenuLocation) {
      ApplySectionActivation(request.targetSection);
      ApplyInputForSection(request.targetSection);
      return;
    }

    pendingRevealSection = request.targetSection;
    sectionTransitionRoutine = StartCoroutine(SwitchSectionRoutine(request));
  }

  void StopSectionTransition(bool clearLoadingOverlay = true, bool restoreVisibleState = true) {
    if (sectionTransitionRoutine != null) {
      StopCoroutine(sectionTransitionRoutine);
      sectionTransitionRoutine = null;
    }
    if (unlockFadeFailSafeRoutine != null) {
      StopCoroutine(unlockFadeFailSafeRoutine);
      unlockFadeFailSafeRoutine = null;
    }

    DisableLoadingUiFeedback(clearText: true, includeLoadingLight: true);
    loadingStallStartedAt = -1f;
    pendingRevealSection = Section.None;

    if (clearLoadingOverlay) {
      SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    }

    if (restoreVisibleState) {
      SetLoadingBlackscreenHold(false);
      ForceBlackscreenVisible(false);
      RestoreSceneLightingForCurrentActivation();
      ReleaseLoadingScreenIfIdle();
    }
  }

  void SwitchSectionInstantly(Section targetSection, string reason) {
    if (targetSection == Section.None) return;
    if (IsLoadingFlowActive()) {
      LogSectionTransitionState("instant_switch_skipped", ResolveCurrentSection(), targetSection, reason, false);
      return;
    }

    var previousSection = ResolveCurrentSection();
    StopSectionTransition(clearLoadingOverlay: false, restoreVisibleState: false);
    if (targetSection != Section.Gameplay) {
      ResetGameplayLoadStageTracking();
    }
    ApplySectionActivation(targetSection);
    ApplyInputForSection(targetSection);
    LogSectionTransitionState("instant_switch_complete", previousSection, targetSection, reason, false);
  }

  string BuildSectionOverlayTag(Section section) {
    return "Section_" + section;
  }

  void HandleDebugMainMenuShortcut() {
    if (!IsDebugMainMenuShortcutPressed()) return;
    var current = ResolveCurrentSection();
    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][DebugShortcut] action=return_to_main_menu" +
        " section=" + current +
        " scene_active=" + (Scene != null && Scene.activeInHierarchy ? 1 : 0) +
        " shift_pressed=1 escape_pressed=1"
      );
    }
    OpenMainMenu();
  }

  bool IsDebugMainMenuShortcutPressed() {
    if (IsLoadingFlowActive()) return false;
    var current = ResolveCurrentSection();
    if (current != Section.Gameplay && current != Section.Pause) return false;
    if (Scene == null || !Scene.activeInHierarchy) return false;

    var keyboard = Keyboard.current;
    if (keyboard == null) return false;
    if (!keyboard.escapeKey.wasPressedThisFrame) return false;
    return keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
  }

  IEnumerator SwitchSectionRoutine(SectionTransitionRequest request) {
    var previousSection = ResolveCurrentSection();
    var overlayTag = string.IsNullOrWhiteSpace(request.overlayTag)
      ? BuildSectionOverlayTag(request.targetSection)
      : request.overlayTag.Trim();

    LogSectionTransitionState("begin", previousSection, request.targetSection, overlayTag, request.showProgressUi);
    SpriteStreamingLoadingState.BeginLoadingOverlay(overlayTag);
    SpriteRuntimeResolver.WarmupLibraries(Array.Empty<string>());
    ResetLoadingProgressForPhase(force: true);
    if (request.switchInputMapToNone) {
      _SwitchMap("none");
    }

    yield return FadeToBlackBeforeLoadRoutine();

    HideAllSectionsForTransition(request.targetSection);
    LogSectionTransitionState("opaque_previous_hidden", previousSection, request.targetSection, overlayTag, request.showProgressUi);

    SetLoadingLightActive(request.showLoadingLight);
    if (request.requestMainMenuLocation) {
      RequestLocationLoadForMainMenu();
    }

    if (request.showProgressUi) {
      BeginLoadingProgressUiAfterFadeIn();
    }
    else {
      DisableLoadingUiFeedback(clearText: true, includeLoadingLight: false);
    }

    LogSectionTransitionState("loading_phase", previousSection, request.targetSection, overlayTag, request.showProgressUi);

    if (request.waitForStreamingIdle) {
      yield return WaitForStreamingIdleBeforeUnlock();
    }

    if (request.showProgressUi) {
      FinalizeLoadingProgressForRelease();
    }
    SetLoadingLightActive(false);
    DisableLoadingUiFeedback(clearText: true, includeLoadingLight: false);
    ApplySectionActivation(request.targetSection);
    ApplyInputForSection(request.targetSection);
    LogSectionTransitionState("ready_to_reveal", previousSection, request.targetSection, overlayTag, request.showProgressUi);

    for (var i = 0; i < 10; i++) {
      yield return null;
    }

    yield return FadeFromBlackRoutine(overlayTag, request.targetSection);
    sectionTransitionRoutine = null;
  }

  private void _SwitchMap(string map) {
    if (string.IsNullOrWhiteSpace(map)) return;
    if (string.Equals(activeInputMap, map, StringComparison.Ordinal)) return;
    activeInputMap = map;
    if (inputProcessor != null) inputProcessor.SwitchMap(map);
    if (mouseManager != null) mouseManager.SwitchMap(map);
  }

  private bool _isNewGame() {
    return !SaveSlotManager.CurrentSlotExists();
  }
}
