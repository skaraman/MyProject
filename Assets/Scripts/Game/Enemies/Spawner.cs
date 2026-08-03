using System;
using System.Collections.Generic;
using CustomInspector;
using UnityEngine;

public class Spawner : MonoBehaviour {
  const string AutoDialogTrigger = "auto";
  const string ImpSpawnSoundId = "enemy.imp.spawn";
  const float EnemySpawnSoundCooldownSeconds = 4f;
  const float InitialWaveSameSideSpacing = 2f;
  const int LoadingEnemyPoolInstantiatesPerFrameDesktop = 2;
  const int LoadingEnemyPoolInstantiatesPerFrameMobile = 1;
  static readonly Action<GameObject> PreparePooledEnemyInstanceCallback =
    PreparePooledEnemyInstance;

  sealed class SpawnRuleState {
    public string enemyType;
    public GameObject prefab;
    public int maxAlive;
    public int initialSpawnedCount;
    public int respawnDelaySeconds = -1;
    public List<float> pendingRespawnSeconds = new();
    public int level;
    public float statMultiplier = 1f;
    public List<DemonStatModifier> statBonuses = new();
  }

  public GameObject ParentHolder;
  [Header("Wave Warmup")]
  [SerializeField] bool prewarmEnemyWaveBeforeSpawning = true;
  [SerializeField, Min(0.5f)] float enemyWaveWarmTimeoutSeconds = 2.0f;
  [SerializeField, Min(0.5f)] float enemyWaveRequiredReadyRatio = 0.95f;
  [SerializeField, Min(1)] int enemyWaveWarmFrames = 48;

  private Camera mainCamera;
  private float offset = 5f;
  private bool canSpawn = false;
  private bool spawnGateOpen = false;
  private bool spawnReadinessReceived = false;
  private bool waitingForEpisodeDialog = false;
  private LocationInfo LocationData;
  readonly Dictionary<GameObject, Pool> enemyPoolsByPrefab = new();
  readonly Dictionary<GameObject, Pool> activeInstancePools = new();
  readonly List<SpawnRuleState> activeSpawnRules = new();
  readonly List<SpawnRuleState> spawnCandidateScratch = new();
  readonly List<string> episodeEnemyTypeScratch = new(8);
  readonly List<HurtBox2D> hurtBoxScratch = new();
  readonly List<GameObject> activeEnemyScratch = new(32);
  readonly HashSet<string> pendingEnemySpawnSoundTypes = new(StringComparer.OrdinalIgnoreCase);
  readonly Dictionary<string, float> enemySpawnSoundCooldownUntil = new(StringComparer.OrdinalIgnoreCase);

  private List<Action> actions = new();
  string pendingSpawnLocationId = "";
  string lastDeferredInitState = "";
  GameObject initializedLocationInstance;
  string initializedLocationId = "";
  int initializedEpisodeRevision = -1;
  int initializedRegistryVersion = -1;
  int spawnWarmupGeneration;
  int loadingPoolBuildFrame = -1;
  int loadingPoolInstancesBuiltThisFrame;
  bool spawningInitialWave;

  void Start() {
    actions.Add(MessageBus.On("ReadyForSpawns", o => OnReadyForSpawns()));
    actions.Add(MessageBus.On("LocationUpdated", o => OnLocationUpdated(o)));
    actions.Add(MessageBus.On("LocationLocationChanged", o => OnLocationLocationChanged(o)));
    actions.Add(MessageBus.On("dialog.finished", o => OnDialogFinished(o)));
    actions.Add(MessageBus.On(CharacterMessageTopics.DialogStateReady, o => OnDialogStateReady()));
    actions.Add(MessageBus.On(ContentEpisodeProgression.ObjectivesCompletedTopic, o => OnEpisodeObjectivesCompleted()));
    actions.Add(MessageBus.On(
      SingleSceneManager.BlackscreenFullyTransparentTopic,
      () => TryPlayPendingEnemySpawnSounds()
    ));
  }

  void OnDestroy() {
    spawnWarmupGeneration += 1;
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
    ClearEnemyPools();
  }

  public void InitLocation() {
    spawnReadinessReceived = true;
    TryOpenSpawnGate("manual", ignoreDialogGate: false);
  }

  public int GetCurrentLocationArchetypeWarmupCount() {
    EnsureWarmupSpawnRules();
    return activeSpawnRules.Count;
  }

  void OnReadyForSpawns() {
    spawnReadinessReceived = true;
    TryOpenSpawnGate("ready_for_spawns", ignoreDialogGate: false);
  }

  void OnDialogFinished(object payload) {
    if (!waitingForEpisodeDialog) return;

    var reason = payload != null ? payload.ToString() : "";
    if (!reason.EndsWith("_complete", StringComparison.OrdinalIgnoreCase)) return;

    TryOpenSpawnGate("dialog_finished", ignoreDialogGate: true);
  }

  void OnDialogStateReady() {
    if (!waitingForEpisodeDialog) return;
    TryOpenSpawnGate("dialog_state_ready", ignoreDialogGate: false);
  }

  void TryOpenSpawnGate(string source, bool ignoreDialogGate) {
    if (!spawnReadinessReceived) return;

    if (ContentEpisodeProgression.HasCurrentObjectives()) {
      if (!ContentEpisodeProgression.HasIncompleteCurrentObjectives()) {
        CloseSpawnGate();
        return;
      }

      var locationId = NormalizeLocationId(LocationManager.currentLocation);
      var hasPendingDialog = DialogController.HasPendingTriggeredSequence(
        locationId,
        AutoDialogTrigger
      );
      if (!ignoreDialogGate &&
          (!DialogController.IsStateReadyForCurrentSlot || hasPendingDialog)) {
        waitingForEpisodeDialog = true;
        spawnGateOpen = false;
        canSpawn = false;
        return;
      }
    }

    waitingForEpisodeDialog = false;
    spawnGateOpen = true;
    pendingSpawnLocationId = NormalizeLocationId(LocationManager.currentLocation);
    TryInitializeLocation(source);
  }

  void CloseSpawnGate() {
    spawnWarmupGeneration += 1;
    spawnReadinessReceived = false;
    waitingForEpisodeDialog = false;
    spawnGateOpen = false;
    canSpawn = false;
    ReturnActiveEnemiesToPools();
  }

  void OnEpisodeObjectivesCompleted() {
    CloseSpawnGate();
  }

  void EnsureWarmupSpawnRules() {
    if (LocationData == null) {
      TryResolveLocation(out LocationData);
    }
    if (activeSpawnRules.Count > 0) {
      return;
    }
    TryBuildActiveSpawnRules(logSummary: false);
  }

  void OnLocationUpdated(object payload) {
    spawnWarmupGeneration += 1;
    pendingSpawnLocationId = NormalizeLocationId(payload as string);
    spawnReadinessReceived = false;
    waitingForEpisodeDialog = false;
    spawnGateOpen = false;
    canSpawn = false;
    ReturnActiveEnemiesToPools();
    activeSpawnRules.Clear();
    LocationData = null;
    lastDeferredInitState = "";
    initializedLocationInstance = null;
    initializedLocationId = "";
    initializedEpisodeRevision = -1;
    initializedRegistryVersion = -1;
  }

  void OnLocationLocationChanged(object payload) {
    if (!(payload is GameObject locationInstance) || locationInstance == null) {
      canSpawn = false;
      return;
    }

    var locationId = NormalizeLocationId(LocationManager.currentLocation);
    if (string.Equals(
      locationId,
      LocationEnemyData.MainMenuLocationId,
      StringComparison.OrdinalIgnoreCase
    )) {
      CloseSpawnGate();
      return;
    }

    spawnReadinessReceived = true;
    TryOpenSpawnGate("location_instance_changed", ignoreDialogGate: false);
  }

  public bool PrepareRuntimeForReveal() {
    return TryPrepareLocationPools("loading_reveal", out _);
  }

  bool TryInitializeLocation(string source) {
    if (!TryPrepareLocationPools(source, out var activeLocationInstance)) {
      return false;
    }
    spawnWarmupGeneration += 1;
    var warmupGeneration = spawnWarmupGeneration;
    var warmupLocationId = NormalizeLocationId(LocationManager.currentLocation);

    if (!Application.isPlaying || !prewarmEnemyWaveBeforeSpawning) {
      lastDeferredInitState = "";
      canSpawn = true;
      SpawnInitialWave();
      RuntimeLog.Log(
        "[Spawner] Spawn init ready source='" + source +
        "' location='" + (LocationData != null ? LocationData.id : pendingSpawnLocationId) +
        "' active_location='" + activeLocationInstance.name +
        "' rules=" + activeSpawnRules.Count +
        " prewarm=" + (prewarmEnemyWaveBeforeSpawning ? 1 : 0)
      );
      return true;
    }

    if (ShouldDeferEnemyWaveWarmupToActiveLoadingOverlay()) {
      lastDeferredInitState = "";
      canSpawn = true;
      SpawnInitialWave();
      RuntimeLog.Log(
        "[Spawner] Skipping local enemy-wave warmup because an active loading overlay/warm gate is already warming startup archetypes." +
        " location='" + LocationManager.currentLocation + "'"
      );
      RuntimeLog.Log(
        "[Spawner] Spawn init ready source='" + source +
        "' location='" + (LocationData != null ? LocationData.id : pendingSpawnLocationId) +
        "' active_location='" + activeLocationInstance.name +
        "' rules=" + activeSpawnRules.Count +
        " prewarm=0"
      );
      return true;
    }

    var archetypes = BuildCurrentLocationArchetypeMapForWarmup();
    if (archetypes.Count <= 0) {
      lastDeferredInitState = "";
      canSpawn = true;
      SpawnInitialWave();
      RuntimeLog.Log(
        "[Spawner] Spawn init ready source='" + source +
        "' location='" + (LocationData != null ? LocationData.id : pendingSpawnLocationId) +
        "' active_location='" + activeLocationInstance.name +
        "' rules=" + activeSpawnRules.Count +
        " archetypes=0"
      );
      return true;
    }

    var orchestrator = StreamingWarmOrchestrator.Instance;
    if (orchestrator == null) {
      lastDeferredInitState = "";
      canSpawn = true;
      SpawnInitialWave();
      RuntimeLog.Log(
        "[Spawner] Spawn init ready source='" + source +
        "' location='" + (LocationData != null ? LocationData.id : pendingSpawnLocationId) +
        "' active_location='" + activeLocationInstance.name +
        "' rules=" + activeSpawnRules.Count +
        " warm_orchestrator=0"
      );
      return true;
    }

    var request = WarmRequest.CreateEnemyWaveSpawn(
      enemyArchetypePrefabsByType: archetypes,
      timeoutSeconds: enemyWaveWarmTimeoutSeconds,
      requiredReadyRatio: enemyWaveRequiredReadyRatio,
      enemyWarmFrames: enemyWaveWarmFrames,
      idempotencyToken: StreamingWarmOrchestrator.BuildEnemyArchetypeToken(LocationManager.currentLocation, archetypes),
      skipIfTokenAlreadyWarm: true
    );
    orchestrator.Run(request, _ => {
      if (warmupGeneration != spawnWarmupGeneration ||
          !spawnGateOpen ||
          !spawnReadinessReceived ||
          !string.Equals(
            warmupLocationId,
            NormalizeLocationId(LocationManager.currentLocation),
            StringComparison.OrdinalIgnoreCase
          ) ||
          !ReferenceEquals(activeLocationInstance, ResolveActiveLocationInstance())) {
        return;
      }

      lastDeferredInitState = "";
      canSpawn = true;
      SpawnInitialWave();
      RuntimeLog.Log(
        "[Spawner] Enemy warmup complete location='" + (LocationData != null ? LocationData.id : pendingSpawnLocationId) +
        "' rules=" + activeSpawnRules.Count +
        " can_spawn=1"
      );
    });
    return true;
  }

  bool TryPrepareLocationPools(string source, out GameObject activeLocationInstance) {
    activeLocationInstance = ResolveActiveLocationInstance();
    if (activeLocationInstance == null) {
      LogDeferredInitState(source, "active_location_instance_missing");
      canSpawn = false;
      return false;
    }

    if (!TryResolveLocation(out LocationData)) {
      Debug.LogError("[Spawner] No valid location found for location '" + LocationManager.currentLocation + "'.");
      canSpawn = false;
      return false;
    }
    var samePoolPreparationContext =
      ReferenceEquals(initializedLocationInstance, activeLocationInstance) &&
      string.Equals(initializedLocationId, LocationData.id, StringComparison.OrdinalIgnoreCase) &&
      initializedEpisodeRevision == ContentEpisodeProgression.EpisodeRevision &&
      initializedRegistryVersion == ActiveContentRegistryRuntime.ReloadVersion;
    if (samePoolPreparationContext && AreEnemyPoolsReadyForActiveRules()) {
      return true;
    }
    if (!samePoolPreparationContext || activeSpawnRules.Count <= 0) {
      if (!TryBuildActiveSpawnRules()) {
        Debug.LogWarning(
          "[Spawner] Location '" + LocationData.id + "' has no spawn rules in the current episode objectives. Spawning disabled."
        );
        canSpawn = false;
        return false;
      }

      canSpawn = false;
      ReturnActiveEnemiesToPools();
      initializedLocationInstance = activeLocationInstance;
      initializedLocationId = LocationData.id;
      initializedEpisodeRevision = ContentEpisodeProgression.EpisodeRevision;
      initializedRegistryVersion = ActiveContentRegistryRuntime.ReloadVersion;
    }

    if (!TryInitializeEnemyPools()) {
      canSpawn = false;
      return false;
    }
    initializedLocationInstance = activeLocationInstance;
    initializedLocationId = LocationData != null ? LocationData.id : pendingSpawnLocationId;
    initializedEpisodeRevision = ContentEpisodeProgression.EpisodeRevision;
    initializedRegistryVersion = ActiveContentRegistryRuntime.ReloadVersion;
    return true;
  }

  static bool ShouldDeferEnemyWaveWarmupToActiveLoadingOverlay() {
    if (!Application.isPlaying) return false;
    return SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning;
  }

  static bool ShouldLogGameplaySpawnDebug() {
    return SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs &&
           (Application.isEditor || Debug.isDebugBuild);
  }

  void Update() {
    if (!canSpawn) return;

    var deltaTime = TimeScale.GetDeltaTime(this);
    UpdateRespawnTimers(deltaTime);
    SpawnEnemy();
  }

  void SpawnInitialWave() {
    if (!Application.isPlaying) return;

    var initialSpawnCount = 0;
    for (var i = 0; i < activeSpawnRules.Count; i++) {
      var spawnRule = activeSpawnRules[i];
      if (spawnRule == null) continue;
      initialSpawnCount += Mathf.Max(0, spawnRule.maxAlive - spawnRule.initialSpawnedCount);
    }

    var spawnedCount = 0;
    var firstSpawnUsesRightSide = UnityEngine.Random.value > 0.5f;
    spawningInitialWave = true;
    try {
      for (var i = 0; i < initialSpawnCount; i++) {
        var rightSide = (i & 1) == 0
          ? firstSpawnUsesRightSide
          : !firstSpawnUsesRightSide;
        if (!SpawnEnemy(rightSide, i / 2)) break;
        spawnedCount += 1;
      }
    }
    finally {
      spawningInitialWave = false;
    }
    TryPlayPendingEnemySpawnSounds();

    RuntimeLog.Log(
      "[Spawner] Initial episode wave spawned" +
      " requested=" + initialSpawnCount +
      " spawned=" + spawnedCount +
      " episode='" + ContentEpisodeProgression.ResolveCurrentEpisodeId() + "'"
    );
  }

  private bool SpawnEnemy(bool? requestedRightSide = null, int sameSideSlot = 0) {
    if (!TryPickSpawnRule(out var spawnRule)) {
      return false;
    }
    var selectedEnemyType = spawnRule.enemyType;

    var rightSide = requestedRightSide ?? (UnityEngine.Random.value > 0.5f);
    var spawnPosition = GetSpawnPosition(rightSide, sameSideSlot);
    if (!enemyPoolsByPrefab.TryGetValue(spawnRule.prefab, out var pool) || pool == null) {
      Debug.LogError(
        "[Spawner] Missing pool for enemy type '" + selectedEnemyType + "' prefab='" +
        (spawnRule.prefab != null ? spawnRule.prefab.name : "-") + "'."
      );
      return false;
    }

    var spawned = pool.Acquire(spawnPosition, Quaternion.identity);
    if (spawned == null) return false;

    if (spawnRule.initialSpawnedCount < spawnRule.maxAlive) {
      spawnRule.initialSpawnedCount += 1;
    }
    else {
      ConsumeReadyRespawn(spawnRule);
    }

    activeInstancePools[spawned] = pool;

    ApplySpawnContextToEnemy(spawned, spawnRule);

    var enemyController = spawned.GetComponent<EnemyController>();
    if (enemyController != null) {
      enemyController.SetEnemyType(selectedEnemyType, playDefaultImmediately: true);
    }
    else {
      var enemyInfo = spawned.GetComponent<EnemyInfo>();
      if (enemyInfo != null) {
        enemyInfo.enemyType = selectedEnemyType;
      }
    }

    pool.Activate(spawned);
    QueueEnemySpawnSound(selectedEnemyType);
    return true;
  }

  void QueueEnemySpawnSound(string enemyType) {
    if (!TryResolveEnemySpawnSoundId(enemyType, out _)) {
      return;
    }

    pendingEnemySpawnSoundTypes.Add(NormalizeEnemyType(enemyType));
    if (!spawningInitialWave) {
      TryPlayPendingEnemySpawnSounds();
    }
  }

  void TryPlayPendingEnemySpawnSounds() {
    if (pendingEnemySpawnSoundTypes.Count <= 0 ||
        !Application.isPlaying ||
        !SingleSceneManager.IsGameplayActive ||
        !SingleSceneManager.IsBlackscreenFullyTransparent) {
      return;
    }

    var now = Time.unscaledTime;
    foreach (var enemyType in pendingEnemySpawnSoundTypes) {
      if (!TryResolveEnemySpawnSoundId(enemyType, out var soundId)) {
        continue;
      }
      if (enemySpawnSoundCooldownUntil.TryGetValue(enemyType, out var cooldownUntil) &&
          now < cooldownUntil) {
        continue;
      }

      SoundEffectPlayer.Play(soundId);
      enemySpawnSoundCooldownUntil[enemyType] = now + EnemySpawnSoundCooldownSeconds;
    }
    pendingEnemySpawnSoundTypes.Clear();
  }

  static bool TryResolveEnemySpawnSoundId(string enemyType, out string soundId) {
    if (string.Equals(NormalizeEnemyType(enemyType), "Imp", StringComparison.OrdinalIgnoreCase)) {
      soundId = ImpSpawnSoundId;
      return true;
    }

    soundId = null;
    return false;
  }

  public void DespawnEnemy(GameObject enemy) {
    if (enemy == null) return;
    if (!activeInstancePools.TryGetValue(enemy, out var pool) || pool == null) {
      var enemyInfo = enemy.GetComponent<EnemyInfo>();
      var enemyType = NormalizeEnemyType(enemyInfo != null ? enemyInfo.enemyType : "");
      Debug.LogError("[Spawner] Cannot despawn enemy because no pool is registered for enemy type '" + enemyType + "'.");
      return;
    }

    var spawnRule = FindSpawnRule(pool);
    activeInstancePools.Remove(enemy);
    pool.Despawn(enemy);

    if (spawnRule == null) return;
    if (spawnRule.respawnDelaySeconds < 0) return;
    if (!spawnGateOpen) return;

    spawnRule.pendingRespawnSeconds.Add(spawnRule.respawnDelaySeconds);
  }

  public Vector3 GetSpawnPosition(bool rightSide, int sameSideSlot = 0) {
    if (mainCamera == null) mainCamera = Camera.main;
    var viewZ = Mathf.Abs(mainCamera.transform.position.z - transform.position.z);
    var worldLeft = mainCamera.ViewportToWorldPoint(new Vector3(0, 0.5f, viewZ)).x;
    var worldRight = mainCamera.ViewportToWorldPoint(new Vector3(1, 0.5f, viewZ)).x;
    var y = transform.position.y;
    var sideOffset = offset + Mathf.Max(sameSideSlot, 0) * InitialWaveSameSideSpacing;
    var x = rightSide ? worldRight + sideOffset : worldLeft - sideOffset;
    var spawnPosition = new Vector3(x, y, transform.position.z);
    if (ShouldLogGameplaySpawnDebug()) {
      RuntimeLog.Log(
        "[OffscreenSpawner]" +
        " right_side=" + (rightSide ? 1 : 0) +
        " world_left=" + worldLeft +
        " world_right=" + worldRight +
        " offset=" + offset +
        " spawn=" + spawnPosition
      );
    }
    return spawnPosition;
  }

  public Dictionary<string, GameObject> BuildCurrentLocationArchetypeMapForWarmup() {
    return BuildArchetypePrefabsByType();
  }

  Dictionary<string, GameObject> BuildArchetypePrefabsByType() {
    var map = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
    if (LocationData == null) {
      TryResolveLocation(out LocationData);
    }
    if (activeSpawnRules.Count <= 0) {
      TryBuildActiveSpawnRules(logSummary: false);
    }

    for (var i = 0; i < activeSpawnRules.Count; i++) {
      var spawnRule = activeSpawnRules[i];
      if (spawnRule == null || string.IsNullOrWhiteSpace(spawnRule.enemyType) || spawnRule.prefab == null) continue;
      map[spawnRule.enemyType] = spawnRule.prefab;
    }

    return map;
  }
  bool TryResolveLocation(out LocationInfo location) {
    location = null;
    var requested = string.IsNullOrWhiteSpace(LocationManager.currentLocation) ? "" : LocationManager.currentLocation.Trim();
    if (!string.IsNullOrWhiteSpace(requested) && LocationEnemyData.TryGetLocation(requested, out location) && location != null) {
      return true;
    }

    var fallbackId = LocationEnemyData.GetDefaultLocation();
    if (LocationEnemyData.TryGetLocation(fallbackId, out location) && location != null) {
      if (!string.Equals(requested, fallbackId, StringComparison.OrdinalIgnoreCase)) {
        LocationManager.UpdateLocation(fallbackId);
      }
      return true;
    }

    foreach (var pair in LocationEnemyData.locations) {
      if (pair.Value == null) continue;
      location = pair.Value;
      if (!string.IsNullOrWhiteSpace(pair.Key) && !string.Equals(requested, pair.Key, StringComparison.OrdinalIgnoreCase)) {
        LocationManager.UpdateLocation(pair.Key);
      }
      return true;
    }

    return false;
  }

  bool TryInitializeEnemyPools() {
    if (activeSpawnRules.Count <= 0) {
      return false;
    }
    if (ParentHolder == null) {
      Debug.LogError("[Spawner] Cannot initialize enemy pools because ParentHolder is not assigned.");
      return false;
    }

    var buildIncrementally = Application.isPlaying && SpriteStreamingLoadingState.IsLoadingOverlayActive;
    var remainingInstanceBudget = int.MaxValue;
    if (buildIncrementally) {
      if (loadingPoolBuildFrame != Time.frameCount) {
        loadingPoolBuildFrame = Time.frameCount;
        loadingPoolInstancesBuiltThisFrame = 0;
      }
      var frameBudget = Application.isMobilePlatform
        ? LoadingEnemyPoolInstantiatesPerFrameMobile
        : LoadingEnemyPoolInstantiatesPerFrameDesktop;
      remainingInstanceBudget = Mathf.Max(0, frameBudget - loadingPoolInstancesBuiltThisFrame);
    }
    var allPoolsReady = true;

    for (var i = 0; i < activeSpawnRules.Count; i++) {
      var spawnRule = activeSpawnRules[i];
      if (spawnRule == null || string.IsNullOrWhiteSpace(spawnRule.enemyType) || spawnRule.prefab == null) continue;
      var requiredPoolSize = Mathf.Max(spawnRule.maxAlive, 1);
      if (!enemyPoolsByPrefab.TryGetValue(spawnRule.prefab, out var pool) || pool == null) {
        pool = new Pool();
        enemyPoolsByPrefab[spawnRule.prefab] = pool;
        if (buildIncrementally) {
          pool.InitializeEmpty(
            spawnRule.prefab,
            ParentHolder.transform,
            requiredPoolSize,
            onInstanceCreated: PreparePooledEnemyInstanceCallback
          );
        }
        else {
          pool.Initialize(
            spawnRule.prefab,
            ParentHolder.transform,
            poolSize: requiredPoolSize,
            onInstanceCreated: PreparePooledEnemyInstanceCallback
          );
        }
      }
      else if (!buildIncrementally) {
        pool.EnsureCapacity(requiredPoolSize);
      }

      if (buildIncrementally && pool.poolSize < requiredPoolSize) {
        var previousPoolSize = pool.poolSize;
        var poolReady = pool.EnsureCapacityIncremental(requiredPoolSize, remainingInstanceBudget);
        var instancesBuilt = pool.poolSize - previousPoolSize;
        remainingInstanceBudget = Mathf.Max(0, remainingInstanceBudget - instancesBuilt);
        loadingPoolInstancesBuiltThisFrame += instancesBuilt;
        if (!poolReady) {
          allPoolsReady = false;
          continue;
        }
      }

      RuntimeLog.Log(
        "[Spawner] Initialized enemy pool" +
        " enemy_type='" + spawnRule.enemyType + "'" +
        " prefab='" + spawnRule.prefab.name + "'" +
        " level=" + spawnRule.level +
        " stat_multiplier=" + spawnRule.statMultiplier +
        " bonuses=" + (spawnRule.statBonuses != null ? spawnRule.statBonuses.Count : 0) +
        " max_alive=" + spawnRule.maxAlive +
        " pool_size=" + requiredPoolSize
      );
    }

    return allPoolsReady;
  }

  static void PreparePooledEnemyInstance(GameObject instance) {
    if (instance == null) {
      return;
    }

    var shadowCaster = instance.GetComponent<ProjectedSpriteShadowCaster2D>();
    shadowCaster?.PrepareProxyHierarchyForActivation();
  }

  bool AreEnemyPoolsReadyForActiveRules() {
    if (activeSpawnRules.Count <= 0 || enemyPoolsByPrefab.Count <= 0) {
      return false;
    }

    for (var i = 0; i < activeSpawnRules.Count; i++) {
      var spawnRule = activeSpawnRules[i];
      if (spawnRule == null || spawnRule.prefab == null) continue;
      if (!enemyPoolsByPrefab.TryGetValue(spawnRule.prefab, out var pool) ||
          pool == null ||
          pool.poolSize < Mathf.Max(spawnRule.maxAlive, 1)) {
        return false;
      }
    }

    return true;
  }

  bool TryBuildActiveSpawnRules(bool logSummary = true) {
    activeSpawnRules.Clear();
    return TryBuildSpawnRulesFromCurrentEpisodeObjectives(logSummary);
  }

  bool TryBuildSpawnRulesFromCurrentEpisodeObjectives(bool logSummary) {
    if (!ContentEpisodeProgression.HasCurrentObjectives()) {
      return false;
    }

    ContentEpisodeProgression.CollectCurrentEpisodeSpawnEnemyTypes(episodeEnemyTypeScratch);
    for (var i = 0; i < episodeEnemyTypeScratch.Count; i++) {
      var enemyType = episodeEnemyTypeScratch[i];
      if (!EnemyPrefabResolver.TryResolve(enemyType, out var enemyPrefab) || enemyPrefab == null) {
        Debug.LogWarning(
          "[Spawner] Skipping objective spawn rule because no prefab could be resolved for enemy type '" +
          enemyType + "'."
        );
        continue;
      }

      var maxAlive = 0;
      var respawnDelaySeconds = -1;
      var statMultiplier = 1f;
      if (!ContentEpisodeProgression.TryResolveCurrentEpisodeSpawnCount(enemyType, out maxAlive)) continue;
      if (!ContentEpisodeProgression.TryResolveCurrentEpisodeRespawnSeconds(
        enemyType,
        out respawnDelaySeconds
      )) {
        respawnDelaySeconds = -1;
      }
      ContentEpisodeProgression.TryResolveCurrentEpisodeEnemyStatMultiplier(
        enemyType,
        out statMultiplier
      );

      if (maxAlive <= 0) continue;
      TryAddSpawnRule(
        enemyType,
        enemyPrefab,
        maxAlive,
        respawnDelaySeconds,
        level: 1,
        statMultiplier: statMultiplier,
        statBonuses: null
      );
    }

    if (activeSpawnRules.Count <= 0) {
      return false;
    }

    if (logSummary) {
      RuntimeLog.Log(
        "[Spawner] Using episode objective spawn rules" +
        " location='" + (LocationData != null ? LocationData.id : LocationManager.currentLocation) + "'" +
        " episode='" + ContentEpisodeProgression.ResolveCurrentEpisodeId() + "'" +
        " rules=" + activeSpawnRules.Count
      );
    }
    return true;
  }

  bool TryAddSpawnRule(
    string enemyType,
    GameObject enemyPrefab,
    int maxAlive,
    int respawnDelaySeconds,
    int level,
    float statMultiplier,
    IList<DemonStatModifier> statBonuses
  ) {
    var normalizedEnemyType = NormalizeEnemyType(enemyType);
    if (enemyPrefab == null || maxAlive <= 0 || string.IsNullOrWhiteSpace(normalizedEnemyType)) return false;

    for (var i = 0; i < activeSpawnRules.Count; i++) {
      var existing = activeSpawnRules[i];
      if (!ReferenceEquals(existing.prefab, enemyPrefab)) continue;
      existing.enemyType = normalizedEnemyType;
      existing.maxAlive = Mathf.Max(existing.maxAlive, maxAlive);
      if (existing.pendingRespawnSeconds.Capacity < existing.maxAlive) {
        existing.pendingRespawnSeconds.Capacity = existing.maxAlive;
      }
      existing.respawnDelaySeconds = ResolveRespawnDelay(
        existing.respawnDelaySeconds,
        respawnDelaySeconds
      );
      existing.level = Mathf.Max(level, 1);
      existing.statMultiplier = Mathf.Max(statMultiplier, 0.0001f);
      existing.statBonuses = CloneStatBonuses(statBonuses);
      return true;
    }

    var spawnRule = new SpawnRuleState {
      enemyType = normalizedEnemyType,
      prefab = enemyPrefab,
      maxAlive = Mathf.Max(maxAlive, 1),
      respawnDelaySeconds = respawnDelaySeconds,
      level = Mathf.Max(level, 1),
      statMultiplier = Mathf.Max(statMultiplier, 0.0001f),
      statBonuses = CloneStatBonuses(statBonuses)
    };
    spawnRule.pendingRespawnSeconds.Capacity = spawnRule.maxAlive;
    activeSpawnRules.Add(spawnRule);
    return true;
  }

  bool TryPickSpawnRule(out SpawnRuleState spawnRule) {
    spawnCandidateScratch.Clear();
    for (var i = 0; i < activeSpawnRules.Count; i++) {
      var candidate = activeSpawnRules[i];
      if (candidate == null || candidate.prefab == null || candidate.maxAlive <= 0) continue;
      if (!enemyPoolsByPrefab.TryGetValue(candidate.prefab, out var pool) || pool == null) continue;
      if (pool.ActiveCount >= candidate.maxAlive) continue;

      var hasInitialSpawn = candidate.initialSpawnedCount < candidate.maxAlive;
      var hasReadyRespawn = HasReadyRespawn(candidate);
      if (!hasInitialSpawn && !hasReadyRespawn) continue;

      spawnCandidateScratch.Add(candidate);
    }

    if (spawnCandidateScratch.Count <= 0) {
      spawnRule = null;
      return false;
    }

    spawnRule = spawnCandidateScratch[UnityEngine.Random.Range(0, spawnCandidateScratch.Count)];
    return spawnRule != null;
  }

  void UpdateRespawnTimers(float deltaTime) {
    if (deltaTime <= 0f) return;

    for (var ruleIndex = 0; ruleIndex < activeSpawnRules.Count; ruleIndex++) {
      var spawnRule = activeSpawnRules[ruleIndex];
      if (spawnRule == null || spawnRule.pendingRespawnSeconds == null) continue;

      for (var timerIndex = 0; timerIndex < spawnRule.pendingRespawnSeconds.Count; timerIndex++) {
        spawnRule.pendingRespawnSeconds[timerIndex] -= deltaTime;
      }
    }
  }

  static bool HasReadyRespawn(SpawnRuleState spawnRule) {
    if (spawnRule == null || spawnRule.pendingRespawnSeconds == null) return false;

    for (var i = 0; i < spawnRule.pendingRespawnSeconds.Count; i++) {
      if (spawnRule.pendingRespawnSeconds[i] <= 0f) return true;
    }

    return false;
  }

  static void ConsumeReadyRespawn(SpawnRuleState spawnRule) {
    if (spawnRule == null || spawnRule.pendingRespawnSeconds == null) return;

    for (var i = 0; i < spawnRule.pendingRespawnSeconds.Count; i++) {
      if (spawnRule.pendingRespawnSeconds[i] > 0f) continue;
      spawnRule.pendingRespawnSeconds.RemoveAt(i);
      return;
    }
  }

  SpawnRuleState FindSpawnRule(Pool pool) {
    if (pool == null) return null;

    for (var i = 0; i < activeSpawnRules.Count; i++) {
      var spawnRule = activeSpawnRules[i];
      if (spawnRule == null || spawnRule.prefab == null) continue;
      if (!enemyPoolsByPrefab.TryGetValue(spawnRule.prefab, out var candidatePool)) continue;
      if (ReferenceEquals(candidatePool, pool)) return spawnRule;
    }

    return null;
  }

  static int ResolveRespawnDelay(int currentDelay, int candidateDelay) {
    if (currentDelay < 0) return candidateDelay;
    if (candidateDelay < 0) return currentDelay;
    return Mathf.Min(currentDelay, candidateDelay);
  }

  GameObject ResolveActiveLocationInstance() {
    return LocationManager.ResolveActiveLocationInstance();
  }

  void ApplySpawnContextToEnemy(GameObject enemyObject, SpawnRuleState spawnRule) {
    if (enemyObject == null || spawnRule == null) {
      return;
    }

    var enemyInfo = enemyObject.GetComponent<EnemyInfo>();
    if (enemyInfo != null) {
      enemyInfo.ApplySpawnContext(
        spawnRule.enemyType,
        spawnRule.level,
        spawnRule.statMultiplier,
        spawnRule.statBonuses,
        this
      );
    }

    DisableEnemyHurtBoxLaunchRandomOnHit(enemyObject);

    var enemyHealth = enemyObject.GetComponent<EnemyHealth>();
    if (enemyHealth == null) {
      enemyHealth = enemyObject.AddComponent<EnemyHealth>();
    }
    if (enemyHealth != null) {
      enemyHealth.RefreshFromEnemyInfo("spawn");
    }

    var enemyAiController = enemyObject.GetComponent<EnemyAIController>();
    if (enemyAiController != null) {
      enemyAiController.RefreshResolvedCombatStats(force: true);
    }
  }

  void DisableEnemyHurtBoxLaunchRandomOnHit(GameObject enemyObject) {
    if (enemyObject == null) {
      return;
    }

    hurtBoxScratch.Clear();
    enemyObject.GetComponentsInChildren<HurtBox2D>(true, hurtBoxScratch);
    for (var i = 0; i < hurtBoxScratch.Count; i++) {
      var hurtBox = hurtBoxScratch[i];
      if (hurtBox == null) continue;
      hurtBox.launchRandomOnHit = false;
    }
    hurtBoxScratch.Clear();
  }

  static List<DemonStatModifier> CloneStatBonuses(IList<DemonStatModifier> source) {
    var clone = new List<DemonStatModifier>();
    if (source == null || source.Count <= 0) {
      return clone;
    }

    for (var i = 0; i < source.Count; i++) {
      var modifier = source[i];
      if (modifier == null) continue;
      clone.Add(modifier.Clone());
    }

    return clone;
  }

  void ReturnActiveEnemiesToPools() {
    pendingEnemySpawnSoundTypes.Clear();
    enemySpawnSoundCooldownUntil.Clear();
    activeEnemyScratch.Clear();
    foreach (var pair in activeInstancePools) {
      if (pair.Key != null) {
        activeEnemyScratch.Add(pair.Key);
      }
    }

    for (var i = 0; i < activeEnemyScratch.Count; i++) {
      var enemy = activeEnemyScratch[i];
      if (activeInstancePools.TryGetValue(enemy, out var pool)) {
        pool?.Despawn(enemy);
      }
    }
    activeInstancePools.Clear();
    activeEnemyScratch.Clear();

    for (var i = 0; i < activeSpawnRules.Count; i++) {
      var spawnRule = activeSpawnRules[i];
      if (spawnRule == null) continue;
      spawnRule.initialSpawnedCount = 0;
      spawnRule.pendingRespawnSeconds.Clear();
    }
  }

  void ClearEnemyPools() {
    foreach (var pair in enemyPoolsByPrefab) {
      pair.Value?.Clear();
    }
    enemyPoolsByPrefab.Clear();
    activeInstancePools.Clear();
  }

  static string NormalizeEnemyType(string enemyType) {
    return string.IsNullOrWhiteSpace(enemyType) ? "" : enemyType.Trim();
  }

  static string NormalizeLocationId(string locationId) {
    return string.IsNullOrWhiteSpace(locationId) ? "" : locationId.Trim();
  }

  void LogDeferredInitState(string source, string state) {
    var message =
      "source='" + source +
      "' state='" + state +
      "' pending_location='" + pendingSpawnLocationId +
      "' current_location='" + NormalizeLocationId(LocationManager.currentLocation) +
      "' active_location_instance=" + (ResolveActiveLocationInstance() != null ? 1 : 0);
    if (string.Equals(lastDeferredInitState, message, StringComparison.Ordinal)) {
      return;
    }

    lastDeferredInitState = message;
    RuntimeLog.Log("[Spawner] Deferred spawn init " + message);
  }
}

