using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class SingleSceneManager {
  float ResolveRequiredWarmRatio(WarmGateMode context, float configuredRatio, EnemyController[] activeEnemies) {
    var ratio = Mathf.Clamp(configuredRatio, 0.1f, 0.99f);
    if (context != WarmGateMode.LoadSave) {
      LogWarmRatioTuning(context, configuredRatio, ratio, ratio, ratio, activeEnemies, "non_loadsave");
      return ratio;
    }

    var cap = Mathf.Clamp(loadSaveWarmRequiredRatioCap, 0.1f, 0.99f);
    var floor = Mathf.Clamp(loadSaveWarmRequiredRatioFloor, 0.1f, 0.99f);
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

    var tunedRatio = Mathf.Clamp(ratio, floor, 0.99f);
    LogWarmRatioTuning(context, configuredRatio, tunedRatio, floor, cap, activeEnemies, "loadsave");
    return tunedRatio;
  }

  void LogWarmRatioTuning(WarmGateMode context, float configuredRatio, float tunedRatio, float floor, float cap, EnemyController[] activeEnemies, string reason) {
    if (!ShouldLogLoadFlowWarnings()) return;
    var queue = TextureResidencyCache.GetQueueSnapshot(pump: false);
    var builder = BeginLoadFlowLog("[SingleSceneManager][WarmRatio]");
    AppendLoadFlowField(builder, "context", context.ToString());
    AppendLoadFlowField(builder, "reason", ResolveLoadFlowValue(reason));
    AppendLoadFlowFloat(builder, "configured_ratio", configuredRatio);
    AppendLoadFlowFloat(builder, "tuned_ratio", tunedRatio);
    AppendLoadFlowFloat(builder, "floor", floor);
    AppendLoadFlowFloat(builder, "cap", cap);
    AppendLoadFlowInt(builder, "memory_mb", SystemInfo.systemMemorySize);
    AppendLoadFlowInt(builder, "active_enemies", activeEnemies != null ? activeEnemies.Length : 0);
    AppendLoadFlowInt(builder, "queue_queued", queue.queuedCount);
    AppendLoadFlowInt(builder, "queue_in_flight", queue.inFlightCount);
    Debug.Log(builder.ToString());
  }

  void LogWarmRequestScope(
    WarmGateMode context,
    List<string> criticalLibraries,
    List<string> criticalAddresses,
    List<string> warmLibraries,
    List<string> warmAddresses,
    List<string> criticalLabels,
    List<string> warmLabels,
    List<string> criticalAssetAddresses,
    List<string> warmAssetAddresses,
    List<string> criticalAssetLabels,
    List<string> warmAssetLabels,
    EnemyController[] criticalEnemies,
    List<string> criticalPlayerEffectKeys,
    float requiredRatio
  ) {
    if (!ShouldLogLoadingProgressDebug()) return;
    Debug.Log(
      "[SingleSceneManager][WarmScope] context=" + context +
      " current_location=" + ResolveLoadFlowValue(LocationManager.currentLocation) +
      " blocking_libraries=" + (criticalLibraries != null ? criticalLibraries.Count : 0) +
      " blocking_addresses=" + (criticalAddresses != null ? criticalAddresses.Count : 0) +
      " blocking_labels=" + (criticalLabels != null ? criticalLabels.Count : 0) +
      " blocking_asset_addresses=" + (criticalAssetAddresses != null ? criticalAssetAddresses.Count : 0) +
      " blocking_asset_labels=" + (criticalAssetLabels != null ? criticalAssetLabels.Count : 0) +
      " background_libraries=" + (warmLibraries != null ? warmLibraries.Count : 0) +
      " background_addresses=" + (warmAddresses != null ? warmAddresses.Count : 0) +
      " background_labels=" + (warmLabels != null ? warmLabels.Count : 0) +
      " background_asset_addresses=" + (warmAssetAddresses != null ? warmAssetAddresses.Count : 0) +
      " background_asset_labels=" + (warmAssetLabels != null ? warmAssetLabels.Count : 0) +
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
    warmRequestCriticalLibrariesScratch.Clear();
    var criticalLibraries = warmRequestCriticalLibrariesScratch;
    warmRequestCriticalAddressesScratch.Clear();
    var criticalAddresses = warmRequestCriticalAddressesScratch;
    warmRequestWarmLibrariesScratch.Clear();
    var warmLibraries = warmRequestWarmLibrariesScratch;
    warmRequestWarmAddressesScratch.Clear();
    var warmAddresses = warmRequestWarmAddressesScratch;
    warmRequestCriticalLabelsScratch.Clear();
    var criticalLabels = warmRequestCriticalLabelsScratch;
    warmRequestWarmLabelsScratch.Clear();
    var warmLabels = warmRequestWarmLabelsScratch;
    warmRequestCriticalAssetAddressesScratch.Clear();
    var criticalAssetAddresses = warmRequestCriticalAssetAddressesScratch;
    warmRequestWarmAssetAddressesScratch.Clear();
    var warmAssetAddresses = warmRequestWarmAssetAddressesScratch;
    warmRequestCriticalAssetLabelsScratch.Clear();
    var criticalAssetLabels = warmRequestCriticalAssetLabelsScratch;
    warmRequestWarmAssetLabelsScratch.Clear();
    var warmAssetLabels = warmRequestWarmAssetLabelsScratch;
    var archetypes = ResolveLocationArchetypePrefabs();
    var combatPopulationTypes = ResolveCombatPopulationWarmTypes(activeEnemies, archetypes, combatPopulationTypesScratch);
    var criticalEnemies = ResolveCriticalWarmEnemies(activeEnemies, playerController);
    var criticalPlayerEffectKeys = ResolveCriticalPlayerEffectKeys(playerController, criticalPlayerEffectKeysScratch);

    if (playerController != null) {
      playerController.CollectPersistentProjectileStartupAssetAddresses(
        criticalAssetAddresses,
        CorePlayerWarmAnimationKeys
      );
    }
    CollectCombatPopulationProjectileStartupAssetAddresses(combatPopulationTypes, criticalAssetAddresses);
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
      criticalAssetAddresses,
      warmAssetAddresses,
      criticalAssetLabels,
      warmAssetLabels,
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
        extraWarmLibraries: warmLibraries,
        extraWarmAddresses: warmAddresses,
        extraCriticalAssetAddresses: criticalAssetAddresses,
        extraWarmAssetAddresses: warmAssetAddresses,
        hardTimeoutSeconds: Mathf.Max(startWarmHardTimeoutSeconds, timeoutSeconds, 3.0f),
        allowHardTimeoutBypass: allowHardTimeoutBypass,
        idempotencyToken: token,
        skipIfTokenAlreadyWarm: true,
        extraCriticalLabels: criticalLabels,
        extraWarmLabels: warmLabels,
        extraCriticalAssetLabels: criticalAssetLabels,
        extraWarmAssetLabels: warmAssetLabels,
        criticalPlayerEffectKeys: criticalPlayerEffectKeys,
        allowCriticalReadySoftTimeout: true
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
        extraCriticalAssetAddresses: criticalAssetAddresses,
        extraCriticalAssetLabels: criticalAssetLabels,
        extraWarmLibraries: warmLibraries,
        extraWarmAddresses: warmAddresses,
        extraWarmLabels: warmLabels,
        extraWarmAssetAddresses: warmAssetAddresses,
        extraWarmAssetLabels: warmAssetLabels,
        hardTimeoutSeconds: Mathf.Max(gearReturnWarmHardTimeoutSeconds, timeoutSeconds, 2.5f),
        allowHardTimeoutBypass: allowHardTimeoutBypass,
        idempotencyToken: "",
        skipIfTokenAlreadyWarm: false,
        criticalPlayerEffectKeys: criticalPlayerEffectKeys,
        allowCriticalReadySoftTimeout: true
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
      extraCriticalAssetAddresses: criticalAssetAddresses,
      extraCriticalAssetLabels: criticalAssetLabels,
      extraWarmLibraries: warmLibraries,
      extraWarmAddresses: warmAddresses,
      extraWarmLabels: warmLabels,
      extraWarmAssetAddresses: warmAssetAddresses,
      extraWarmAssetLabels: warmAssetLabels,
      hardTimeoutSeconds: Mathf.Max(startWarmHardTimeoutSeconds, timeoutSeconds, 3.0f),
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: token,
      skipIfTokenAlreadyWarm: true,
      criticalPlayerEffectKeys: criticalPlayerEffectKeys,
      allowCriticalReadySoftTimeout: true
    );
  }

  List<string> ResolveCombatPopulationWarmTypes(
    EnemyController[] activeEnemies,
    Dictionary<string, GameObject> archetypes,
    List<string> output
  ) {
    var enemyTypes = output ?? combatPopulationTypesScratch;
    enemyTypes.Clear();

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

  int CollectCombatPopulationProjectileStartupAssetAddresses(
    IReadOnlyList<string> combatPopulationTypes,
    List<string> outAddresses,
    int maxUniqueAddresses = int.MaxValue
  ) {
    if (combatPopulationTypes == null || combatPopulationTypes.Count <= 0 || outAddresses == null || maxUniqueAddresses <= 0) {
      return 0;
    }

    var beforeCount = outAddresses.Count;
    combatPopulationProjectileKeysScratch.Clear();
    combatPopulationProjectileKeySeenScratch.Clear();

    for (var i = 0; i < combatPopulationTypes.Count; i++) {
      if (outAddresses.Count >= maxUniqueAddresses) {
        break;
      }

      if (!TryGetEnemyAnimationManifest(combatPopulationTypes[i], out var animations)) {
        continue;
      }

      AnimationLinkUtility.CollectLinkedProjectileKeys(
        animations,
        null,
        combatPopulationProjectileKeysScratch,
        combatPopulationProjectileKeySeenScratch
      );
    }

    for (var i = 0; i < combatPopulationProjectileKeysScratch.Count; i++) {
      if (outAddresses.Count >= maxUniqueAddresses) {
        break;
      }

      TryAddProjectilePrefabWarmAddress(combatPopulationProjectileKeysScratch[i], outAddresses);
    }

    var addedCount = Mathf.Max(outAddresses.Count - beforeCount, 0);
    if (addedCount > 0 && ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][EnemyProjectileWarmup]" +
        " location=" + ResolveLoadFlowValue(LocationManager.currentLocation) +
        " enemy_types=" + combatPopulationTypes.Count +
        " projectile_keys=" + combatPopulationProjectileKeysScratch.Count +
        " asset_addresses_added=" + addedCount
      );
    }

    combatPopulationProjectileKeysScratch.Clear();
    combatPopulationProjectileKeySeenScratch.Clear();
    return addedCount;
  }

  static bool TryGetEnemyAnimationManifest(string enemyType, out Dictionary<string, AnimData> animations) {
    animations = null;
    if (string.IsNullOrWhiteSpace(enemyType)) {
      return false;
    }

    var normalized = enemyType.Trim();
    if (Animations.Enemies.TryGetValue(normalized, out animations) && animations != null) {
      return true;
    }

    foreach (var pair in Animations.Enemies) {
      if (!string.Equals(pair.Key, normalized, StringComparison.OrdinalIgnoreCase) || pair.Value == null) {
        continue;
      }

      animations = pair.Value;
      return true;
    }

    return false;
  }

  static bool TryAddProjectilePrefabWarmAddress(string projectileKey, List<string> outAddresses) {
    if (outAddresses == null || string.IsNullOrWhiteSpace(projectileKey)) {
      return false;
    }

    if (!Projectiles.TryGetPrefabAddress(projectileKey, out var address) || string.IsNullOrWhiteSpace(address)) {
      Debug.LogWarning(
        "[SingleSceneManager][EnemyProjectileWarmup] MissingProjectilePrefabAddress" +
        " projectile='" + projectileKey.Trim() + "'"
      );
      return false;
    }

    if (ContainsAddressIgnoreCase(outAddresses, address)) {
      return false;
    }

    outAddresses.Add(address);
    return true;
  }

  static bool ContainsAddressIgnoreCase(List<string> addresses, string address) {
    if (addresses == null || string.IsNullOrWhiteSpace(address)) {
      return false;
    }

    for (var i = 0; i < addresses.Count; i++) {
      if (string.Equals(addresses[i], address, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
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

  List<string> ResolveCriticalPlayerEffectKeys(GearController playerController, List<string> output) {
    if (!warmGatePreloadCorePlayerEffects || playerController == null || playerController.effectNode == null) {
      return null;
    }

    var keys = output ?? criticalPlayerEffectKeysScratch;
    keys.Clear();
    AnimationLinkUtility.CollectLinkedEffectKeys(Animations.Esperanza, CorePlayerWarmAnimationKeys, keys);
    return keys.Count > 0 ? keys : null;
  }

  Dictionary<string, GameObject> ResolveLocationArchetypePrefabs() {
    var spawner = ResolveGameplaySpawner();
    if (spawner != null) {
      var map = spawner.BuildCurrentLocationArchetypeMapForWarmup();
      if (map != null && map.Count > 0) {
        Debug.Log(
          "[SingleSceneManager] Using active location prefab enemy archetypes for warmup" +
          " location='" + LocationManager.currentLocation + "'" +
          " archetypes=" + map.Count
        );
        return map;
      }
    }

    Debug.LogWarning(
      "[SingleSceneManager] No location prefab enemy archetypes available for warmup" +
      " location='" + LocationManager.currentLocation + "'" +
      " spawner=" + (spawner != null ? 1 : 0) +
      " location_activation_pending=" + (LocationManager.HasPendingBlockingActivationWork ? 1 : 0) +
      " location_deferred_pending=" + (LocationManager.HasPendingDeferredActivationWork ? 1 : 0) +
      " ready_for_spawns_sent=" + (gameplayReadyForSpawnsSentForLoad ? 1 : 0) + "."
    );
    return new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
  }

  GearController ResolvePlayerGearController() {
    var now = Time.realtimeSinceStartup;
    if (cachedPlayerGearController != null) {
      var cachedGo = cachedPlayerGearController.gameObject;
      var cachedValid = cachedGo != null &&
                        cachedGo.scene.IsValid() &&
                        (cachedGo.hideFlags & HideFlags.HideAndDontSave) == 0;
      if (cachedValid) {
        if (Scene == null || !Scene.activeInHierarchy || IsPreferredGameplayPlayerController(cachedPlayerGearController)) {
          return cachedPlayerGearController;
        }

        // Throttle the expensive ResolveBestAvailablePlayerController check to at most once per 500ms
        if (lastPlayerResolveTime >= 0f && now - lastPlayerResolveTime < 0.5f) {
          return cachedPlayerGearController;
        }
        lastPlayerResolveTime = now;

        var replacement = ResolveBestAvailablePlayerController();
        if (replacement == null || ReferenceEquals(replacement, cachedPlayerGearController)) {
          return cachedPlayerGearController;
        }

        InvalidateCachedPlayerGearController("cached_candidate_replaced");
      }
      else {
        InvalidateCachedPlayerGearController("cached_candidate_invalid");
      }
    }

    if (cachedPlayerGearController == null) {
      cachedPlayerGearController = ResolveBestAvailablePlayerController();
      lastPlayerResolveTime = now;
      if (ShouldLogLoadFlowDebug()) {
        Debug.Log(
          "[SingleSceneManager][PlayerResolve] resolved " +
          DescribeGearController(cachedPlayerGearController) +
          " scene_active=" + (Scene != null && Scene.activeInHierarchy ? 1 : 0)
        );
      }
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

    var enemies = FindObjectsByType<EnemyController>(FindObjectsInactive.Exclude);
    activeEnemyControllersCache = enemies != null && enemies.Length > 0 ? enemies : Array.Empty<EnemyController>();
    activeEnemyControllersCacheRefreshedAt = now;
    return activeEnemyControllersCache;
  }

  void InvalidateActiveEnemyControllersCache() {
    activeEnemyControllersCache = Array.Empty<EnemyController>();
    activeEnemyControllersCacheRefreshedAt = -1f;
  }
}
