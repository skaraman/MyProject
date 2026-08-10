using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class SingleSceneManager {
  static bool ShouldLogSectionTransitionDebug() {
    return ShouldLogLoadFlowDebug();
  }

  void LogSectionTransitionState(string stage, Section fromSection, Section toSection, string overlayTag, bool showProgressUi) {
    if (!ShouldLogSectionTransitionDebug()) return;
    var loadingRootActive = LoadingScreen != null && LoadingScreen.activeSelf;
    var loadingLightActive = loadingLightObject != null && loadingLightObject.activeSelf;
    var sceneLights = ResolveSceneObjectLights();
    var sceneLightsActive = sceneLights != null && sceneLights.activeSelf;
    RuntimeLog.Log(
      "[SingleSceneManager][SectionTransition] stage=" + (string.IsNullOrWhiteSpace(stage) ? "unspecified" : stage.Trim()) +
      " from=" + fromSection +
      " to=" + toSection +
      " overlay=" + (string.IsNullOrWhiteSpace(overlayTag) ? "-" : overlayTag.Trim()) +
      " loading_root=" + (loadingRootActive ? 1 : 0) +
      " loading_light=" + (loadingLightActive ? 1 : 0) +
      " progress_ui=" + (IsLoadingProgressUiVisible() ? 1 : 0) +
      " requested_progress=" + (showProgressUi ? 1 : 0) +
      " black_hold=" + (holdBlackscreenOpaqueDuringLoad ? 1 : 0) +
      " black_visible=" + (loadingBlackscreen != null && loadingBlackscreen.activeInHierarchy ? 1 : 0) +
      " overlay_active=" + (SpriteStreamingLoadingState.IsLoadingOverlayActive ? 1 : 0) +
      " overlay_reason=" + (string.IsNullOrWhiteSpace(SpriteStreamingLoadingState.ActiveReason) ? "-" : SpriteStreamingLoadingState.ActiveReason) +
      " scene_lights=" + (sceneLightsActive ? 1 : 0) +
      " current_section=" + ResolveCurrentSection()
    );
  }

  void InvalidateCachedPlayerGearController(string reason = null) {
    if (cachedPlayerGearController == null) {
      cachedPlayerCharacterState = null;
      lastPlayerResolveTime = -1f;
      return;
    }
    if (ShouldLogLoadFlowDebug()) {
      RuntimeLog.Log(
        "[SingleSceneManager][PlayerResolve] invalidate reason=" + (string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim()) +
        " previous=" + DescribeGearController(cachedPlayerGearController)
      );
    }
    cachedPlayerGearController = null;
    cachedPlayerCharacterState = null;
    lastPlayerResolveTime = -1f;
  }

  string DescribeGearController(GearController controller) {
    if (controller == null) return "player=-";
    var go = controller.gameObject;
    var inSceneRoot = Scene != null && go != null && go.transform.IsChildOf(Scene.transform);
    return "player=" + go.name +
           " enabled=" + (controller.enabled ? 1 : 0) +
           " active=" + (go.activeInHierarchy ? 1 : 0) +
           " scene='" + (go.scene.IsValid() ? go.scene.name : "-") + "'" +
           " in_scene_root=" + (inSceneRoot ? 1 : 0) +
           " appearance_rev=" + controller.AppearanceRevision;
  }

  bool IsPreferredGameplayPlayerController(GearController controller) {
    if (controller == null) return false;
    var go = controller.gameObject;
    if (go == null || !go.scene.IsValid()) return false;
    if ((go.hideFlags & HideFlags.HideAndDontSave) != 0) return false;
    if (!controller.enabled || !go.activeInHierarchy) return false;
    if (Scene != null && Scene.activeInHierarchy) {
      return go.transform.IsChildOf(Scene.transform);
    }
    return true;
  }

  GearController ResolveBestAvailablePlayerController() {
    var controllers = playerControllerResolveScratch;
    controllers.Clear();
    if (Scene != null) {
      Scene.GetComponentsInChildren(true, controllers);
    }

    for (var i = 0; i < controllers.Count; i++) {
      var candidate = controllers[i];
      if (IsPreferredGameplayPlayerController(candidate)) {
        return candidate;
      }
    }

    for (var i = 0; i < controllers.Count; i++) {
      var candidate = controllers[i];
      if (candidate == null) continue;
      var go = candidate.gameObject;
      if (go == null || !go.scene.IsValid()) continue;
      if ((go.hideFlags & HideFlags.HideAndDontSave) != 0) continue;
      return candidate;
    }
    return null;
  }

  void EnsureGameplayPlayerBootstrap(string source) {
    using var profilerScope = EnsureGameplayPlayerBootstrapProfilerMarker.Auto();
    var existing = FindScenePlayerController();
    if (existing != null) {
      EnsureGameplayPlayerEnabled(existing);
      ApplyGameplayPlayerReferences(existing.gameObject, source, instantiated: false);
      return;
    }

    var gameplayPlayerPrefab = ResolveGameplayPlayerBootstrapPrefab(source);
    if (gameplayPlayerPrefab == null) {
      Debug.LogWarning(
        "[SingleSceneManager][PlayerBootstrap] stage=missing_prefab" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " asset_path='" + ResolveGameplayPlayerBootstrapAssetPath() + "'"
      );
      return;
    }

    if (Scene == null) {
      Debug.LogWarning(
        "[SingleSceneManager][PlayerBootstrap] stage=missing_scene_root" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " prefab=" + gameplayPlayerPrefab.name
      );
      return;
    }

    var instance = Instantiate(gameplayPlayerPrefab, Scene.transform, false);
    instance.name = gameplayPlayerPrefab.name;
    var gear = instance.GetComponent<GearController>();
    if (gear == null) {
      Debug.LogWarning(
        "[SingleSceneManager][PlayerBootstrap] stage=missing_gear_controller" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " prefab=" + instance.name
      );
      return;
    }

    EnsureGameplayPlayerEnabled(gear);
    ApplyGameplayPlayerReferences(instance, source, instantiated: true);
  }

  IEnumerator PrewarmGameplayPlayerBootstrapAssets(string source) {
    if (FindScenePlayerController() != null) {
      yield break;
    }

    var gameplayPlayerPrefab = ResolveGameplayPlayerBootstrapPrefab(source);
    if (gameplayPlayerPrefab == null) {
      yield break;
    }

    var gear = gameplayPlayerPrefab.GetComponent<GearController>();
    if (gear == null) {
      yield break;
    }

    playerBootstrapWarmAddressScratch.Clear();
    playerBootstrapWarmSeenAddressScratch.Clear();
    var maxPinnedAddresses = Math.Max(SpriteStreamingRuntimeSettings.PinBudgetPlayerAddresses, 1);
    var collectedCount = gear.CollectBootstrapSkinStartupAddresses(
      playerBootstrapWarmAddressScratch,
      playerBootstrapWarmSeenAddressScratch,
      maxPinnedAddresses
    );
    if (collectedCount <= 0 || playerBootstrapWarmAddressScratch.Count <= 0) {
      playerBootstrapWarmAddressScratch.Clear();
      playerBootstrapWarmSeenAddressScratch.Clear();
      yield break;
    }

    TextureResidencyCache.UpdateOwnerPins(
      PersistentPlayerAppearanceAtlasPinOwnerId,
      TextureResidencyCache.PinClass.Player,
      playerBootstrapWarmAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup
    );
    QueuePersistentAtlasMetadataWarmup(playerBootstrapWarmAddressScratch);
    yield return TextureResidencyCache.RequestLoadBatchThrottled(
      playerBootstrapWarmAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup,
      allowAtlasExpansion: true,
      enqueueBudgetPerFrame: 96,
      warmGateManaged: false
    );
    yield return WaitForPlayerBootstrapReadiness(source, gear);

    if (ShouldLogLoadingProgressDebug()) {
      var readyCount = CountReadyPlayerBootstrapSamples(gear, out var totalReadySamples);
      RuntimeLog.Log(
        "[SingleSceneManager][PlayerBootstrap] stage=prewarm_complete" +
        " source='" + (source ?? "") + "'" +
        " addresses=" + playerBootstrapWarmAddressScratch.Count +
        " ready=" + readyCount +
        "/" + totalReadySamples
      );
    }

    playerBootstrapWarmAddressScratch.Clear();
    playerBootstrapWarmSeenAddressScratch.Clear();
  }

  IEnumerator WaitForPlayerBootstrapReadiness(string source, GearController gear) {
    if (playerBootstrapWarmAddressScratch.Count <= 0) {
      yield break;
    }

    var timeoutSeconds = 1.5f;
    var startedAt = Time.realtimeSinceStartup;
    var readyCount = CountReadyPlayerBootstrapSamples(gear, out var totalSampleCount);
    while (readyCount < totalSampleCount &&
           Time.realtimeSinceStartup - startedAt < timeoutSeconds) {
      SetLoadingStatusOverride("Preparing player");
      yield return null;
      readyCount = CountReadyPlayerBootstrapSamples(gear, out totalSampleCount);
    }
    ClearLoadingStatusOverride();

    if (!ShouldLogLoadingProgressDebug()) {
      yield break;
    }

    RuntimeLog.Log(
      "[SingleSceneManager][PlayerBootstrap] stage=prewarm_wait_complete" +
      " source='" + (source ?? "") + "'" +
      " ready=" + readyCount +
      "/" + totalSampleCount +
      " elapsed_ms=" + ((Time.realtimeSinceStartup - startedAt) * 1000f).ToString("0.0")
    );
  }

  int CountReadyPlayerBootstrapSamples(GearController gear, out int totalSampleCount) {
    totalSampleCount = 0;
    if (gear == null) {
      return 0;
    }
    return gear.CountBootstrapSkinStartupReadySamples(out totalSampleCount);
  }

  string ResolveGameplayPlayerBootstrapAssetPath() {
    return GameplayCoreAssetPaths.EsperanzaPrefabAssetPath;
  }

  GameObject ResolveGameplayPlayerBootstrapPrefab(string source) {
    var resolvedAssetPath = ResolveGameplayPlayerBootstrapAssetPath();
    if (!string.IsNullOrWhiteSpace(resolvedAssetPath)) {
      gameplayPlayerBootstrapPrefabData.assetPath = resolvedAssetPath;
      var resolvedPrefab = gameplayPlayerBootstrapPrefabData.ResolvePrefab();
      if (resolvedPrefab != null) {
        if (ShouldLogLoadFlowDebug()) {
          RuntimeLog.Log(
            "[SingleSceneManager][PlayerBootstrap] stage=resolved_prefab" +
            " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
            " asset_path='" + resolvedAssetPath + "'" +
            " prefab='" + resolvedPrefab.name + "'"
          );
        }
        return resolvedPrefab;
      }

      if (ShouldLogLoadFlowWarnings()) {
        Debug.LogWarning(
          "[SingleSceneManager][PlayerBootstrap] stage=resolved_prefab_unavailable" +
          " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
          " asset_path='" + resolvedAssetPath + "'" +
          " fallback_serialized=" + (playerCharacterPrefab != null ? 1 : 0)
        );
      }
    }

    if (playerCharacterPrefab != null && ShouldLogLoadFlowDebug()) {
      RuntimeLog.Log(
        "[SingleSceneManager][PlayerBootstrap] stage=fallback_serialized_prefab" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " prefab='" + playerCharacterPrefab.name + "'"
      );
    }

    return playerCharacterPrefab;
  }

  GearController FindScenePlayerController() {
    if (Scene == null) return null;
    var controllers = playerControllerResolveScratch;
    controllers.Clear();
    Scene.GetComponentsInChildren(true, controllers);
    for (var i = 0; i < controllers.Count; i++) {
      var candidate = controllers[i];
      if (candidate == null) continue;
      var go = candidate.gameObject;
      if (go == null || !go.scene.IsValid()) continue;
      if ((go.hideFlags & HideFlags.HideAndDontSave) != 0) continue;
      return candidate;
    }
    return null;
  }

  void EnsureGameplayPlayerEnabled(GearController gear) {
    if (gear == null) return;

    var root = gear.gameObject;
    if (root != null && !root.activeSelf) {
      root.SetActive(true);
    }

    if (!gear.enabled) {
      gear.enabled = true;
    }

    var characterState = gear.GetComponent<CharacterState>();
    if (characterState != null && !characterState.enabled) {
      characterState.enabled = true;
    }
  }

  CharacterState ResolvePlayerCharacterState() {
    if (IsLiveSceneComponent(cachedPlayerCharacterState)) {
      return cachedPlayerCharacterState;
    }

    cachedPlayerCharacterState = null;
    var player = ResolvePlayerGearController();
    if (player != null) {
      cachedPlayerCharacterState = player.GetComponent<CharacterState>();
      if (IsLiveSceneComponent(cachedPlayerCharacterState)) {
        return cachedPlayerCharacterState;
      }
      cachedPlayerCharacterState = null;
    }

    cachedPlayerCharacterState = FindAnyObjectByType<CharacterState>();
    return cachedPlayerCharacterState;
  }

  GameObject ResolveGameplayPlayerRootInternal() {
    var player = ResolvePlayerGearController();
    if (player != null && IsLiveSceneObject(player.gameObject)) {
      return player.gameObject;
    }

    var characterState = ResolvePlayerCharacterState();
    if (IsLiveSceneComponent(characterState)) {
      return characterState.gameObject;
    }

    return null;
  }

  void ApplyGameplayPlayerReferences(GameObject playerRoot, string source, bool instantiated) {
    if (playerRoot == null) return;

    var gear = playerRoot.GetComponent<GearController>();
    var characterState = playerRoot.GetComponent<CharacterState>();
    var sharedProjectileManager = ResolveGameplayProjectileManagerInternal();
    if (gear != null) {
      if (sharedProjectileManager != null) {
        gear.projectileManager = sharedProjectileManager;
      }
      cachedPlayerGearController = gear;
      lastPlayerResolveTime = Time.realtimeSinceStartup;
    }
    cachedPlayerCharacterState = characterState;
    InvalidatePreUnlockTargetCache();

    var gameplayInput = FindAnyObjectByType<GameplayInput>();
    if (gameplayInput != null) {
      gameplayInput.ApplyPlayerBootstrap(playerRoot, gear, characterState);
    }

    if (autoSaver != null && characterState != null) {
      autoSaver.characterState = characterState;
    }

    cachedGameplayInput = gameplayInput;
    gameplayInputCacheRefreshedAt = -1f;
    RefreshPersistentPlayerBaselineAtlasPins(string.IsNullOrWhiteSpace(source) ? "player_bootstrap_ready" : source + "_player_bootstrap_ready");

    if (!ShouldLogLoadFlowDebug()) return;
    RuntimeLog.Log(
      "[SingleSceneManager][PlayerBootstrap] stage=ready" +
      " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
      " action=" + (instantiated ? "instantiate" : "reuse") +
      " player=" + playerRoot.name +
      " gameplay_input=" + (gameplayInput != null ? 1 : 0) +
      " character_state=" + (characterState != null ? 1 : 0) +
      " projectile_manager=" + (sharedProjectileManager != null ? 1 : 0) +
      " parent=" + (playerRoot.transform.parent != null ? playerRoot.transform.parent.name : "-")
    );
  }

  int ResolveSpriteReadinessFrame(SpriteWithNormals sprite) {
    if (sprite == null) return 1;
    if (!sprite.IsAnimation) return 0;
    return Mathf.Max(sprite.LastRequestedFrame, 1);
  }

  bool TryDescribeFirstUnreadySprite(GameObject[] objects, string groupName, out string blockerSummary, bool generateSummary = false) {
    if (objects != null) {
      for (var i = 0; i < objects.Length; i++) {
        var go = objects[i];
        if (go == null) continue;
        var sprite = go.GetComponent<SpriteWithNormals>();
        if (sprite == null || !sprite.isActiveAndEnabled || sprite.DoNotRender) continue;
        var frame = ResolveSpriteReadinessFrame(sprite);
        if (sprite.IsFrameReady(frame, out var colorReadyOnly)) continue;
        if (generateSummary) {
          blockerSummary =
            "group=" + groupName +
            " sprite=" + go.name +
            " lib=" + (string.IsNullOrWhiteSpace(sprite.libraryName) ? "-" : sprite.libraryName) +
            " label=" + (string.IsNullOrWhiteSpace(sprite.labelPrefix) ? "-" : sprite.labelPrefix) +
            " category=" + (string.IsNullOrWhiteSpace(sprite.category) ? "-" : sprite.category) +
            " frame=" + frame +
            " color_only=" + (colorReadyOnly ? 1 : 0) +
            " active=" + (go.activeInHierarchy ? 1 : 0);
        } else {
          blockerSummary = "";
        }
        return true;
      }
    }

    blockerSummary = "";
    return false;
  }

  bool TryGetPlayerFirstFrameBlocker(out string blockerSummary, bool generateSummary = false) {
    var player = ResolvePlayerGearController();
    if (player == null) {
      blockerSummary = generateSummary ? "player=-" : "";
      return false;
    }

    if (Scene != null && Scene.activeInHierarchy && !player.gameObject.activeInHierarchy) {
      blockerSummary = generateSummary ? DescribeGearController(player) + " inactive_under_scene" : "";
      return true;
    }

    if (TryDescribeFirstUnreadySprite(player.SkinObjects, "skin", out var skinBlocker, generateSummary)) {
      blockerSummary = generateSummary ? DescribeGearController(player) + " " + skinBlocker : "";
      return true;
    }
    if (TryDescribeFirstUnreadySprite(player.GearObjects, "gear", out var gearBlocker, generateSummary)) {
      blockerSummary = generateSummary ? DescribeGearController(player) + " " + gearBlocker : "";
      return true;
    }

    blockerSummary = generateSummary ? DescribeGearController(player) + " ready" : "";
    return false;
  }

  void RotateLoadingCircle() {
    if (loadingCircle == null || !loadingCircle.activeInHierarchy) return;
    var spinSpeed = Mathf.Max(loadingCircleSpinSpeedDegreesPerSecond, 0f);
    if (spinSpeed <= 0f) return;
    var dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
    if (dt <= 0f) return;
    loadingCircle.transform.Rotate(0f, 0f, -spinSpeed * dt, Space.Self);
  }
}
