using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class SingleSceneManager {
  static readonly int LoadingBlackscreenColorId = Shader.PropertyToID("_Color");
  static readonly int LoadingBlackscreenAlphaId = Shader.PropertyToID("_Alpha");
  static readonly int[] LoadingBlackscreenDisabledEffectIds = {
    Shader.PropertyToID("_AlphaOutlineBlend"),
    Shader.PropertyToID("_BlurIntensity"),
    Shader.PropertyToID("_Brightness"),
    Shader.PropertyToID("_ChromAberrAmount"),
    Shader.PropertyToID("_ColorRampBlend"),
    Shader.PropertyToID("_ColorSwapBlend"),
    Shader.PropertyToID("_DistortAmount"),
    Shader.PropertyToID("_GlitchAmount"),
    Shader.PropertyToID("_Glow"),
    Shader.PropertyToID("_GradBlend"),
    Shader.PropertyToID("_GreyscaleBlend"),
    Shader.PropertyToID("_HitEffectBlend"),
    Shader.PropertyToID("_HologramBlend"),
    Shader.PropertyToID("_InnerOutlineAlpha"),
    Shader.PropertyToID("_NegativeAmount"),
    Shader.PropertyToID("_OutlineAlpha"),
    Shader.PropertyToID("_OverlayBlend"),
    Shader.PropertyToID("_PosterizeOutline"),
    Shader.PropertyToID("_ShadowAlpha"),
  };

  void ApplyGameplayStateUnderBlack() {
    using var profilerScope = ApplyGameplayStateUnderBlackProfilerMarker.Auto();
    pendingRevealSection = Section.Gameplay;
    SetLoadingRootActive(true);
    InvalidateCachedPlayerGearController("apply_gameplay_state_under_black");
    if (runtimeLocationTransitionInProgress) {
      runtimeLocationTransitionCommitApplied = true;
    }
    RequestLocationLoadForGameplay(ConsumePendingGameplayLocationId("apply_gameplay_state_under_black"));
    SetLoadingBlackscreenHold(true);
    _SwitchMap("none");
    HideAllSectionsForTransition(Section.Gameplay);
    SetSceneObjectLightsActive(false);
    pauseMenuOpenAppearanceRevision = -1;
    LogSectionTransitionState("gameplay_under_black", ResolveCurrentSection(), Section.Gameplay, SpriteStreamingLoadingState.ActiveReason, IsLoadingProgressUiVisible());
  }

  IEnumerator UnlockGameplayFromBlackRoutine(string overlayTag, float revealHandoffStartedAt = -1f) {
    var previousSection = ResolveCurrentSection();
    SetLoadingLightActive(false);
    yield return WaitForPersistentPlayerAppearanceAtlases();
    SetLoadingStatusOverride("Activating gameplay");
    var activationStartedAt = ShouldLogLoadFlowWarnings() ? Time.realtimeSinceStartup : -1f;
    ApplySectionActivation(Section.Gameplay);
    _SwitchMap("none");
    PrepareRuntimeRevealUnderLoadingOverlay();
    LogRevealHandoff("gameplay_activation_applied", revealHandoffStartedAt, activationStartedAt);
    yield return WaitForRevealActivationSettle();
    yield return CollectManagedGarbageUnderLoadingOverlay();
    RestoreSceneLightingForCurrentActivation();
    LogRevealHandoff("reveal_settle_complete", revealHandoffStartedAt);
    LogSectionTransitionState("ready_to_reveal", previousSection, Section.Gameplay, overlayTag, false);
    yield return FadeFromBlackRoutine(overlayTag, Section.Gameplay);
  }

  IEnumerator WaitForPersistentPlayerAppearanceAtlases() {
    var contentVersion = ActiveContentRegistryRuntime.ReloadVersion;
    if (!persistentPlayerAppearancePlanEvaluated ||
        persistentPlayerAppearanceContentVersion != contentVersion) {
      RefreshPersistentPlayerSkinAtlasPins("before_gameplay_reveal");
    }
    if (!persistentPlayerEffectPlanEvaluated ||
        persistentPlayerEffectContentVersion != contentVersion) {
      RefreshPersistentPlayerEffectAtlasPins("before_gameplay_reveal");
    }
    var hasAppearancePlan = persistentPlayerAppearanceAtlasAddresses.Count > 0;
    var hasEffectPlan = persistentPlayerEffectAtlasAddresses.Count > 0;
    if (!hasAppearancePlan && !hasEffectPlan) yield break;

    const float timeoutSeconds = 5f;
    var startedAt = Time.realtimeSinceStartup;
    var appearanceAtlasPlanReady = !hasAppearancePlan;
    var appearanceAtlasIndex = 0;
    var readyAppearanceAtlasesInPass = 0;
    while (!appearanceAtlasPlanReady &&
           Time.realtimeSinceStartup - startedAt < timeoutSeconds) {
      var address = persistentPlayerAppearanceAtlasAddresses[appearanceAtlasIndex];
      if (TextureResidencyCache.GetPreparedAtlasState(address, pump: false).IsCommitReady()) {
        readyAppearanceAtlasesInPass++;
      }
      appearanceAtlasIndex++;
      if (appearanceAtlasIndex >= persistentPlayerAppearanceAtlasAddresses.Count) {
        appearanceAtlasPlanReady =
          readyAppearanceAtlasesInPass >= persistentPlayerAppearanceAtlasAddresses.Count;
        appearanceAtlasIndex = 0;
        readyAppearanceAtlasesInPass = 0;
      }
      SetLoadingStatusOverride("Preparing character animations");
      TextureResidencyCache.PumpOncePerFrame();
      yield return null;
    }

    if (!appearanceAtlasPlanReady) {
      Debug.LogWarning(
        "[SingleSceneManager] Character animation atlas preparation timed out before reveal." +
        " atlases=" + persistentPlayerAppearanceAtlasAddresses.Count
      );
    }

    startedAt = Time.realtimeSinceStartup;
    var effectAtlasPlanReady = !hasEffectPlan;
    var effectAtlasIndex = 0;
    var readyEffectAtlasesInPass = 0;
    while (!effectAtlasPlanReady &&
           Time.realtimeSinceStartup - startedAt < timeoutSeconds) {
      var address = persistentPlayerEffectAtlasAddresses[effectAtlasIndex];
      if (TextureResidencyCache.GetPreparedAtlasState(address, pump: false).IsCommitReady()) {
        readyEffectAtlasesInPass++;
      }
      effectAtlasIndex++;
      if (effectAtlasIndex >= persistentPlayerEffectAtlasAddresses.Count) {
        effectAtlasPlanReady =
          readyEffectAtlasesInPass >= persistentPlayerEffectAtlasAddresses.Count;
        effectAtlasIndex = 0;
        readyEffectAtlasesInPass = 0;
      }
      SetLoadingStatusOverride("Preparing combat effects");
      TextureResidencyCache.PumpOncePerFrame();
      yield return null;
    }

    if (!effectAtlasPlanReady) {
      Debug.LogWarning(
        "[SingleSceneManager] Player effect atlas preparation timed out before reveal." +
        " atlases=" + persistentPlayerEffectAtlasAddresses.Count
      );
    }

    var player = ResolvePlayerGearController();
    var readySamples = 0;
    var totalSamples = 0;
    var framePlanReady = !hasAppearancePlan;
    if (player != null && appearanceAtlasPlanReady && hasAppearancePlan) {
      startedAt = Time.realtimeSinceStartup;
      readySamples = player.CountPersistentSkinStartupReadySamples(out totalSamples);
      while (readySamples < totalSamples &&
             Time.realtimeSinceStartup - startedAt < timeoutSeconds) {
        SetLoadingStatusOverride("Preparing character animation frames");
        TextureResidencyCache.PumpOncePerFrame();
        TrimmedSpriteOffsetResolver.PumpDeferredRuntimeLoads();
        yield return null;
        readySamples = player.CountPersistentSkinStartupReadySamples(out totalSamples);
      }
      framePlanReady = readySamples >= totalSamples;
    }

    if (!appearanceAtlasPlanReady || !framePlanReady) {
      TextureResidencyCache.ReleaseOwnerPins(PersistentPlayerAppearanceAtlasPinOwnerId);
      persistentPlayerAppearanceAtlasAddresses.Clear();
      persistentPlayerAppearancePlanEvaluated = false;
      player?.SetSceneAppearanceAtlasPinsManaged(false);
      if (!framePlanReady) {
        Debug.LogWarning(
          "[SingleSceneManager] Character animation frame preparation timed out before reveal." +
          " ready=" + readySamples +
          "/" + totalSamples +
          " dynamic_fallback=1"
        );
      }
    }

    if (effectAtlasPlanReady) yield break;
    TextureResidencyCache.ReleaseOwnerPins(PersistentPlayerEffectAtlasPinOwnerId);
    persistentPlayerEffectAtlasAddresses.Clear();
    persistentPlayerEffectPlanEvaluated = false;
  }

  void PlayBlackscreen(string animationName) {
    if (blackscreen == null) return;
    if (holdBlackscreenOpaqueDuringLoad && string.Equals(animationName, "alphaOut", StringComparison.Ordinal)) {
      return;
    }
    if (string.Equals(animationName, "alphaOut", StringComparison.Ordinal)) {
      loadingOverlayChildrenReady = false;
      SetLoadingLightActive(false);
      SetLoadingProgressUiActive(false);
      SetLoadingText("");
    }
    ApplyLoadingBlackscreenVisual(loadingBlackscreenRenderer, 1f, -1f);
    blackscreen.Play(animationName);
  }

  IEnumerator StartupFadeWatchdogRoutine() {
    var wait = Mathf.Max(startupFadeWatchdogSeconds, 0f);
    if (wait > 0f) {
      yield return StartupFadeWatchdogDelay;
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

    SetLoadingLightActive(false);
    SetLoadingProgressUiActive(false);
    SetLoadingText("");
    SetLoadingBlackscreenHold(false);
    ForceBlackscreenVisible(false);
    SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    RestoreSceneLightingForCurrentActivation();
    ReleaseLoadingScreenIfIdle();
    LogSectionTransitionState("startup_reveal_complete", Section.None, ResolveCurrentSection(), "MainMenuStartup", false);
    ApplyStartupInputFallbackAfterWatchdog();
    startupFadeWatchdogRoutine = null;
  }

  bool IsLoadingFlowActive() {
    if (startGameRoutine != null ||
        resumeGameplayRoutine != null ||
        startupGameplayRoutine != null ||
        runtimeLocationTransitionRoutine != null) {
      return true;
    }
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
    pendingRevealSection = Section.None;
    ResumePlayerAnimationAfterLoadingOverlay("stop_startup_gameplay_flow");
    SpriteStreamingLoadingState.ForceClearLoadingOverlay();
  }

  void ApplyStartupInputFallbackAfterWatchdog() {
    if (startGameRoutine != null || resumeGameplayRoutine != null || startupGameplayRoutine != null) {
      return;
    }
    var section = ResolveCurrentSection();
    if (section != Section.None) {
      ApplyInputForSection(section);
      return;
    }
    ApplyInputMapForCurrentUiState(preferGameplayWhenNoUi: false);
  }

  bool ShouldRunStartupGameplayWarmFlow() {
    return startupInDebugGameplay;
  }

  void ApplyConfiguredStartupMode() {
    ResolveStartupMode();
    if (startupInDebugGameplay) {
      ApplyConfiguredDebugStartupMode();
      return;
    }

    ApplyConfiguredMainMenuStartupMode();
  }

  void ResolveStartupMode() {
    startupInDebugGameplay = false;
    startupDebugLocationId = "";

    if (!IsEditorStartupDebugEnabled()) {
      if (ShouldLogLoadFlowDebug()) {
        RuntimeLog.Log("[SingleSceneManager][StartupMode] mode=main_menu debug_mode=0 debug_location=-");
      }
      return;
    }

    var resolvedDebugLocationId = ResolveConfiguredDebugLocationId(out var debugLocationReason);
    if (!IsGameplayLocation(resolvedDebugLocationId) ||
        !LocationEnemyData.TryGetLocation(resolvedDebugLocationId, out var locationInfo) ||
        locationInfo == null) {
      if (ShouldLogLoadFlowWarnings()) {
        Debug.LogWarning(
          "[SingleSceneManager][StartupMode] mode=main_menu debug_mode=1" +
          " debug_location='" + ResolveLoadFlowValue(debugLocationId) + "'" +
          " legacy_debug_prefab='" + (debugLocationPrefab != null ? debugLocationPrefab.name : "-") + "'" +
          " reason=" + debugLocationReason
        );
      }
      return;
    }

    startupInDebugGameplay = true;
    startupDebugLocationId = LocationEnemyData.NormalizeLocationId(resolvedDebugLocationId);
    if (ShouldLogLoadFlowDebug()) {
      RuntimeLog.Log(
        "[SingleSceneManager][StartupMode] mode=debug_gameplay debug_mode=1" +
        " debug_location='" + ResolveLoadFlowValue(debugLocationId) + "'" +
        " resolved_location='" + startupDebugLocationId + "'" +
        " legacy_debug_prefab='" + (debugLocationPrefab != null ? debugLocationPrefab.name : "-") + "'" +
        " reason=" + debugLocationReason
      );
    }
  }

  void ApplyConfiguredDebugStartupMode() {
    ReleasePreUnlockResidentPins("startup_mode_debug_gameplay");
    pendingRevealSection = Section.None;
    currentSection = Section.None;
    if (autoSaver != null) {
      autoSaver.enableTimeTracking = true;
    }
    HideAllSectionsForTransition(Section.Gameplay);
    _SwitchMap("none");
    SetSceneObjectLightsActive(false);
    pauseMenuOpenAppearanceRevision = -1;
    SpriteStreamingLoadingState.EndLoadingOverlay("MainMenuStartup");
  }

  void ApplyConfiguredMainMenuStartupMode() {
    ReleasePreUnlockResidentPins("startup_mode_main_menu");
    pendingRevealSection = Section.None;
    RequestLocationLoadForMainMenu();
    ApplySectionActivation(Section.MainMenu);
    ApplyInputForSection(Section.MainMenu);
    if (autoSaver != null) {
      autoSaver.enableTimeTracking = false;
    }
    SetSceneObjectLightsActive(false);
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
      _SwitchMap(ResolveGameplayInputMap());
      return;
    }
    if (preferGameplayWhenNoUi && HasLiveGameplayInput()) {
      _SwitchMap(ResolveGameplayInputMap());
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
    cachedGameplayInput = FindAnyObjectByType<GameplayInput>();
    return IsGameplayInputLive(cachedGameplayInput);
  }

  static bool IsGameplayInputLive(GameplayInput gameplayInput) {
    return gameplayInput != null &&
           gameplayInput.enabled &&
           gameplayInput.gameObject.activeInHierarchy;
  }

  string ConsumePendingGameplayLocationId(string source) {
    var locationId = pendingGameplayLocationId;
    pendingGameplayLocationId = "";
    if (ShouldLogLoadFlowDebug()) {
      RuntimeLog.Log(
        "[SingleSceneManager][GameplayLocation] stage=consume_pending" +
        " source=" + ResolveLoadFlowValue(source) +
        " pending=" + ResolveLoadFlowValue(locationId) +
        " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation) +
        " last_known_gameplay=" + ResolveLoadFlowValue(lastKnownGameplayLocationId)
      );
    }
    return locationId;
  }

  void ResolveAndApplyLocationForStart(bool isNewGame, SaveData loadedSlot) {
    var resolved = ResolveLocationForStart(isNewGame, loadedSlot);
    var previousLocation = LocationManager.currentLocation;
    pendingGameplayLocationId = resolved;
    RememberGameplayLocation(resolved, "resolve_start");

    if (!ShouldLogLoadFlowDebug()) return;

    RuntimeLog.Log(
      "[SingleSceneManager][StartLocation] resolved=" + resolved +
      " previous=" + previousLocation +
      " staged_for_gameplay=1" +
      " is_new_game=" + (isNewGame ? 1 : 0) +
      " debug_mode=" + (startupInDebugGameplay ? 1 : 0) +
      " debug_location='" + ResolveLoadFlowValue(debugLocationId) + "'" +
      " startup_debug_location='" + ResolveLoadFlowValue(startupDebugLocationId) + "'"
    );
  }

  bool IsKnownLocation(string location) {
    return LocationEnemyData.ContainsLocation(location);
  }

  static bool IsMainMenuLocation(string locationId) {
    return string.Equals(
      LocationEnemyData.NormalizeLocationId(locationId),
      mainMenuFlowLocationId,
      StringComparison.OrdinalIgnoreCase
    );
  }

  bool IsGameplayLocation(string locationId) {
    var normalized = LocationEnemyData.NormalizeLocationId(locationId);
    return !string.IsNullOrWhiteSpace(normalized) &&
           !IsMainMenuLocation(normalized) &&
           IsKnownLocation(normalized);
  }

  void RememberGameplayLocation(string locationId, string source) {
    var normalized = LocationEnemyData.NormalizeLocationId(locationId);
    if (!IsGameplayLocation(normalized)) return;
    TrackEnvironmentHotCacheLocation(normalized, source);
    UpdateActiveGameplayLoadTargetLocation(normalized);
    if (string.Equals(lastKnownGameplayLocationId, normalized, StringComparison.OrdinalIgnoreCase)) return;

    lastKnownGameplayLocationId = normalized;
    if (!ShouldLogLoadFlowDebug()) return;

    RuntimeLog.Log(
      "[SingleSceneManager][GameplayLocation] stage=remember" +
      " source=" + ResolveLoadFlowValue(source) +
      " location=" + ResolveLoadFlowValue(normalized) +
      " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation)
    );
  }

  string ResolveGameplayLocationRequest(string preferredLocationId, out string source) {
    var preferred = LocationEnemyData.NormalizeLocationId(preferredLocationId);
    if (IsGameplayLocation(preferred)) {
      source = "preferred";
      return preferred;
    }

    var current = LocationEnemyData.NormalizeLocationId(LocationManager.currentLocation);
    if (IsGameplayLocation(current)) {
      source = "current";
      return current;
    }

    var lastKnown = LocationEnemyData.NormalizeLocationId(lastKnownGameplayLocationId);
    if (IsGameplayLocation(lastKnown)) {
      source = "last_known";
      return lastKnown;
    }

    source = "default";
    return ResolveDefaultLocation();
  }

  string ResolveDefaultLocation() {
    if (IsKnownLocation(defaultStartLocation)) return defaultStartLocation.Trim();
    if (IsKnownLocation(gameplayFlowFallbackLocationId)) return gameplayFlowFallbackLocationId;
    return LocationEnemyData.GetDefaultLocation();
  }

  string ResolveDefaultDebugLocationId() {
    foreach (var pair in LocationEnemyData.locations) {
      var candidateLocationId = LocationEnemyData.NormalizeLocationId(pair.Key);
      if (!IsGameplayLocation(candidateLocationId)) {
        continue;
      }

      return candidateLocationId;
    }

    return "";
  }

  string ResolveConfiguredDebugLocationId(out string reason) {
    var configuredLocationId = LocationEnemyData.NormalizeLocationId(debugLocationId);
    if (IsGameplayLocation(configuredLocationId)) {
      reason = "configured_location";
      return configuredLocationId;
    }

    if (string.IsNullOrWhiteSpace(configuredLocationId) &&
        debugLocationPrefab != null &&
        LocationEnemyData.TryGetLocationByPrefab(debugLocationPrefab, out var legacyLocationInfo) &&
        legacyLocationInfo != null) {
      var legacyLocationId = LocationEnemyData.NormalizeLocationId(legacyLocationInfo.id);
      if (IsGameplayLocation(legacyLocationId)) {
        reason = "legacy_prefab";
        return legacyLocationId;
      }
    }

    var fallbackLocationId = ResolveDefaultDebugLocationId();
    if (IsGameplayLocation(fallbackLocationId)) {
      reason = string.IsNullOrWhiteSpace(configuredLocationId)
        ? "default_first_location"
        : "fallback_unknown_debug_location";
      return fallbackLocationId;
    }

    reason = string.IsNullOrWhiteSpace(configuredLocationId)
      ? "missing_debug_location"
      : "unknown_debug_location";
    return "";
  }

  string ResolveLocationForStart(bool isNewGame, SaveData loadedSlot) {
    if (startupInDebugGameplay && IsKnownLocation(startupDebugLocationId)) {
      return startupDebugLocationId;
    }

    var resolved = ResolveDefaultLocation();
    if (!isNewGame && loadedSlot != null && loadedSlot.ContainsKey("location")) {
      var loadedLocation = Convert.ToString(loadedSlot["location"]);
      if (IsKnownLocation(loadedLocation)) {
        resolved = loadedLocation.Trim();
      }
    }

    return resolved;
  }

  void RequestLocationLoadForMainMenu() {
    if (!LocationEnemyData.TryGetLocation(mainMenuFlowLocationId, out var locationInfo) || locationInfo == null) {
      if (ShouldLogLoadFlowDebug()) {
        RuntimeLog.Log("[SingleSceneManager][MainMenuLocation] skip_request reason=missing_location");
      }
      return;
    }

    if (ShouldLogLoadFlowDebug()) {
      var prefabData = locationInfo.locationPrefabData;
      var clearsActiveLocation = prefabData == null ||
                                 (prefabData.prefab == null && string.IsNullOrWhiteSpace(prefabData.AssetPath));
      RuntimeLog.Log(
        "[SingleSceneManager][MainMenuLocation] request" +
        " location=" + mainMenuFlowLocationId +
        " clears_active_location=" + (clearsActiveLocation ? 1 : 0)
      );
    }
    RequestLocationLoad(mainMenuFlowLocationId);
  }

  void RequestLocationLoadForGameplay(string preferredLocationId) {
    var locationId = ResolveGameplayLocationRequest(preferredLocationId, out var source);
    RememberGameplayLocation(locationId, "request:" + source);

    if (ShouldLogLoadFlowDebug()) {
      RuntimeLog.Log(
        "[SingleSceneManager][GameplayLocation] stage=request" +
        " source=" + ResolveLoadFlowValue(source) +
        " preferred=" + ResolveLoadFlowValue(LocationEnemyData.NormalizeLocationId(preferredLocationId)) +
        " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation) +
        " last_known_gameplay=" + ResolveLoadFlowValue(lastKnownGameplayLocationId) +
        " resolved=" + ResolveLoadFlowValue(locationId)
      );
    }

    RequestLocationLoad(locationId);
  }

  void RequestLocationLoad(string locationId) {
    var resolved = LocationEnemyData.ResolveRequestedOrDefault(locationId);
    if (string.IsNullOrWhiteSpace(resolved)) return;
    LogLocationLoadRequest(locationId, resolved);
    ResetLoadingProgressForPhase();
    if (!string.Equals(LocationManager.currentLocation, resolved, StringComparison.OrdinalIgnoreCase)) {
      LocationManager.CommitLocationForLoadingFlow(resolved);
      return;
    }
    MessageBus.Send("RequestLocationLoad", resolved);
  }

  void OnLocationUpdated(object payload) {
    if (!Application.isPlaying) return;
    InvalidatePreUnlockTargetCache();
    InvalidateActiveEnemyControllersCache();
    InvalidateCachedPlayerGearController("location_updated");
    cachedGameplayInput = null;
    gameplayInputCacheRefreshedAt = -1f;

    var locationId = payload as string;
    if (string.IsNullOrWhiteSpace(locationId)) {
      locationId = LocationManager.currentLocation;
    }
    locationId = string.IsNullOrWhiteSpace(locationId) ? "" : locationId.Trim();
    RememberGameplayLocation(locationId, "location_updated");
    if (string.Equals(lastPurgedLocationId, locationId, StringComparison.OrdinalIgnoreCase)) return;
    var previousLocationId = lastPurgedLocationId;
    lastPurgedLocationId = locationId;
    LogLocationUpdate(previousLocationId, locationId);
    HandleLocationCacheTransition(previousLocationId, locationId);
  }

  void HandleLocationCacheTransition(string previousLocationId, string currentLocationId) {
    if (string.IsNullOrWhiteSpace(previousLocationId)) return;
    if (string.Equals(previousLocationId, currentLocationId, StringComparison.OrdinalIgnoreCase)) return;

    var reason = ResolveLocationSceneChangeReason(currentLocationId);
    if (!ShouldReloadRuntimeContentForLocationSceneChange(currentLocationId)) {
      TextureResidencyCache.EvictAllUnpinnedCompleted();
      return;
    }

    lastRuntimeSceneChangeKey = NormalizeRuntimeSceneChangeKey(currentLocationId);
    ReloadRuntimeContentForSceneChange(previousLocationId, currentLocationId, reason);
  }

  void OnRuntimeSceneChanged(object payload) {
    var sceneKey = NormalizeRuntimeSceneChangeKey(Convert.ToString(payload));
    if (string.IsNullOrWhiteSpace(sceneKey)) {
      sceneKey = NormalizeRuntimeSceneChangeKey(LocationManager.currentLocation);
    }

    if (string.IsNullOrWhiteSpace(sceneKey)) {
      return;
    }

    if (IsGameplayLocation(sceneKey) &&
        !string.Equals(
          LocationManager.currentLocation,
          sceneKey,
          StringComparison.OrdinalIgnoreCase
        )) {
      LocationManager.UpdateLocation(sceneKey);
      return;
    }

    var previousSceneKey = lastRuntimeSceneChangeKey;
    lastRuntimeSceneChangeKey = sceneKey;
    ReloadRuntimeContentForSceneChange(previousSceneKey, sceneKey, "zone_entered");
  }

  void ReloadRuntimeContentForSceneChange(string previousSceneId, string currentSceneId, string reason) {
    // Scene transition lifecycle: old environment/enemy/effect sets are no longer
    // required once switching to a different runtime scene. Evict completed unpinned entries.
    var evicted = TextureResidencyCache.EvictAllUnpinnedCompleted();
    RuntimeContentPackResolver.ReloadForSceneChange(previousSceneId, currentSceneId, reason);

    RuntimeLog.Log(
      "[SingleSceneManager][RuntimeSceneChange] content_rederived=1" +
      " reason='" + (reason ?? "") + "'" +
      " previous='" + (previousSceneId ?? "") + "'" +
      " current='" + (currentSceneId ?? "") + "'" +
      " evicted_unpinned=" + evicted
    );
  }

  string ResolveLocationSceneChangeReason(string currentLocationId) {
    if (IsHomebaseLocation(currentLocationId)) {
      return "teleport_homebase";
    }

    return "location_change";
  }

  bool ShouldReloadRuntimeContentForLocationSceneChange(string currentLocationId) {
    if (IsHomebaseLocation(currentLocationId)) {
      return true;
    }

    return IsGameplayLocation(currentLocationId);
  }

  static bool IsHomebaseLocation(string locationId) {
    return string.Equals(
      LocationEnemyData.NormalizeLocationId(locationId),
      LocationEnemyData.HomebaseLocationId,
      StringComparison.OrdinalIgnoreCase
    );
  }

  static string NormalizeRuntimeSceneChangeKey(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  void ForceBlackscreenVisible(bool visible) {
    if (loadingBlackscreen == null && LoadingScreen != null) {
      var blackscreenTransform = FindChildByName(LoadingScreen.transform, "blackscreen");
      if (blackscreenTransform != null) {
        loadingBlackscreen = blackscreenTransform.gameObject;
        loadingBlackscreenRenderer = loadingBlackscreen.GetComponent<SpriteRenderer>();
      }
    }
    if (loadingBlackscreen == null) {
      SetBlackscreenTransparencyState(!visible);
      return;
    }
    var alpha = visible ? 1f : 0f;
    ApplyLoadingBlackscreenVisual(loadingBlackscreenRenderer, alpha, alpha);
    SetBlackscreenTransparencyState(!visible);
  }

  void SetBlackscreenTransparencyState(bool fullyTransparent) {
    if (!fullyTransparent) {
      IsBlackscreenFullyTransparent = false;
      return;
    }

    if (IsBlackscreenFullyTransparent) return;

    IsBlackscreenFullyTransparent = true;
    MessageBus.Send(BlackscreenFullyTransparentTopic);
  }

  void ApplyLoadingBlackscreenVisual(SpriteRenderer spriteRenderer, float rendererAlpha, float materialAlpha) {
    if (spriteRenderer == null) return;
    rendererAlpha = Mathf.Clamp01(rendererAlpha);
    var targetColor = new Color(0f, 0f, 0f, rendererAlpha);
    if (spriteRenderer.color != targetColor) {
      spriteRenderer.color = targetColor;
    }

    if (loadingBlackscreenPropertyBlock == null) {
      loadingBlackscreenPropertyBlock = new MaterialPropertyBlock();
    }
    var block = loadingBlackscreenPropertyBlock;
    spriteRenderer.GetPropertyBlock(block);
    block.SetColor(LoadingBlackscreenColorId, Color.black);
    for (var i = 0; i < LoadingBlackscreenDisabledEffectIds.Length; i++) {
      block.SetFloat(LoadingBlackscreenDisabledEffectIds[i], 0f);
    }
    if (materialAlpha >= 0f) {
      block.SetFloat(LoadingBlackscreenAlphaId, Mathf.Clamp01(materialAlpha));
    }
    spriteRenderer.SetPropertyBlock(block);
  }

  void SetLoadingBlackscreenHold(bool hold) {
    if (holdBlackscreenOpaqueDuringLoad == hold) return;
    holdBlackscreenOpaqueDuringLoad = hold;
    if (!hold) {
      loadingHeldProgressBlackscreenVisualApplied = false;
    }
    if (blackscreen != null) {
      blackscreen.enabled = !hold;
    }
    if (hold) {
      ForceBlackscreenVisible(true);
    }
  }

  IEnumerator EnsureBlackscreenClearsAfterUnlockRoutine(Section revealedSection, string overlayTag) {
    var waitSeconds = Mathf.Max(fadeFromBlackSeconds + 0.15f, 0.5f);
    if (waitSeconds > 0f) {
      yield return RevealCleanupDelay;
    }
    if (!holdBlackscreenOpaqueDuringLoad) {
      ForceBlackscreenVisible(false);
    }
    var sectionToReveal = revealedSection == Section.None ? ResolveCurrentSection() : revealedSection;
    var shouldEnableLights = Scene != null &&
                             Scene.activeInHierarchy &&
                             ShouldRestoreSceneLightsForSection(sectionToReveal);
    SetSceneObjectLightsActive(shouldEnableLights);
    if (sectionToReveal == Section.Gameplay) {
      ResumePlayerAnimationAfterLoadingOverlay("reveal_complete");
      lastGameplayRevealCompletedAt = Time.realtimeSinceStartup;
    }
    SpriteStreamingLoadingState.ReleaseOverlayProtection();
    if (!string.IsNullOrWhiteSpace(overlayTag)) {
      SpriteStreamingLoadingState.EndLoadingOverlay(overlayTag);
    }
    if (sectionToReveal == Section.Gameplay) {
      ApplyInputForSection(Section.Gameplay);
      LogRevealHandoff("gameplay_input_enabled", -1f);
    }
    DisableLoadingUiFeedback(clearText: true, includeLoadingLight: true);
    ReleaseLoadingScreenIfIdle();
    LogSectionTransitionState("reveal_complete", currentSection, sectionToReveal, overlayTag, false);
    if (sectionToReveal == Section.Gameplay) {
      EndGameplayLoadFlowTrace(activeGameplayLoadFlowId, "reveal_complete");
    }
    ReleaseLoadingScreenIfIdle();
    unlockFadeFailSafeRoutine = null;
    StartQueuedRuntimeLocationTransitionIfNeeded();
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
    if (sectionTransitionRoutine != null) {
      StopCoroutine(sectionTransitionRoutine);
      sectionTransitionRoutine = null;
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
    if (runtimeLocationTransitionRoutine != null) {
      StopCoroutine(runtimeLocationTransitionRoutine);
      runtimeLocationTransitionRoutine = null;
    }
    runtimeLocationTransitionInProgress = false;
    runtimeLocationTransitionCommitApplied = false;
    queuedRuntimeLocationId = "";
    pendingGameplayLocationId = "";
    if (unlockFadeFailSafeRoutine != null) {
      StopCoroutine(unlockFadeFailSafeRoutine);
      unlockFadeFailSafeRoutine = null;
    }

    StopStartupFadeWatchdog();
    loadingStallStartedAt = -1f;

    var sectionToReveal = ResolveActiveSectionFromHierarchy();
    if (sectionToReveal == Section.None) {
      var fallbackSection = pendingRevealSection != Section.None ? pendingRevealSection : currentSection;
      if (fallbackSection != Section.None) {
        ApplySectionActivation(fallbackSection);
        sectionToReveal = fallbackSection;
      }
    }
    pendingRevealSection = Section.None;

    if (ShouldLogLoadFlowWarnings()) {
      var forceBuilder = BeginLoadFlowLog("[SingleSceneManager][ForceRelease]");
      AppendLoadFlowField(forceBuilder, "section_to_reveal", sectionToReveal.ToString());
      AppendLoadFlowInt(forceBuilder, "loading_percent", loadingPercent);
      AppendLoadFlowField(forceBuilder, "loading_status", ResolveLoadFlowValue(loadingStatusDetail));
      AppendLoadFlowField(forceBuilder, "active_input_map", ResolveLoadFlowValue(activeInputMap));
      AppendGameplayLoadPipelineFields(forceBuilder);
      RuntimeLog.Log(forceBuilder.ToString());
    }

    FinalizeLoadingProgressForRelease();
    DisableLoadingUiFeedback(clearText: true, includeLoadingLight: true);
    ReleasePreUnlockResidentPins("force_release");
    SetLoadingBlackscreenHold(false);
    ForceBlackscreenVisible(false);
    ResumePlayerAnimationAfterLoadingOverlay("force_release");
    SpriteStreamingLoadingState.ForceClearLoadingOverlay();
    RestoreSceneLightingForCurrentActivation();
    ReleaseLoadingScreenIfIdle();
    if (sectionToReveal != Section.None) {
      if (sectionToReveal == Section.Gameplay) {
        EndGameplayLoadFlowTrace(activeGameplayLoadFlowId, "force_release");
      }
      ApplyInputForSection(sectionToReveal);
      return;
    }
    ApplyInputMapForCurrentUiState(preferGameplayWhenNoUi: false);
  }

  IEnumerator FadeFromBlackRoutine(string overlayTag, Section revealedSection) {
    FinalizeLoadingProgressForRelease();
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
    unlockFadeFailSafeRoutine = StartCoroutine(EnsureBlackscreenClearsAfterUnlockRoutine(revealedSection, overlayTag));
    var waitSeconds = Mathf.Max(fadeFromBlackSeconds, 0f);
    if (waitSeconds > 0f) {
      yield return FadeFromBlackDelay;
    }
  }

  void SetActiveSafe(GameObject target, bool active) {
    if (target == null) return;
    target.SetActive(active);
  }
}
