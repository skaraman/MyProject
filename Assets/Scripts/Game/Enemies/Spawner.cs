using System;
using System.Collections.Generic;
using CustomInspector;
using UnityEngine;

public class Spawner : MonoBehaviour {
  const string AutoDialogTrigger = "auto";

  sealed class SpawnRuleState {
    public string enemyType;
    public GameObject prefab;
    public int maxAlive;
    public int initialSpawnedCount;
    public int respawnDelaySeconds = -1;
    public List<float> pendingRespawnSeconds = new();
    public int level;
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
  private float timer = 0f;
  private bool canSpawn = false;
  private bool spawnGateOpen = false;
  private bool spawnReadinessReceived = false;
  private bool waitingForEpisodeDialog = false;
  private LocationInfo LocationData;
  readonly Dictionary<GameObject, Pool> enemyPoolsByPrefab = new();
  readonly Dictionary<GameObject, Pool> activeInstancePools = new();
  readonly List<SpawnRuleState> activeSpawnRules = new();
  readonly List<SpawnRuleState> spawnCandidateScratch = new();
  readonly List<HurtBox2D> hurtBoxScratch = new();
  readonly List<GameObject> activeEnemyScratch = new(32);

  private List<Action> actions = new();
  string pendingSpawnLocationId = "";
  string lastDeferredInitState = "";
  GameObject initializedLocationInstance;
  string initializedLocationId = "";
  int initializedEpisodeRevision = -1;
  int initializedRegistryVersion = -1;
  int spawnWarmupGeneration;

  void Start() {
    actions.Add(MessageBus.On("ReadyForSpawns", o => OnReadyForSpawns()));
    actions.Add(MessageBus.On("LocationUpdated", o => OnLocationUpdated(o)));
    actions.Add(MessageBus.On("LocationLocationChanged", o => OnLocationLocationChanged(o)));
    actions.Add(MessageBus.On("dialog.finished", o => OnDialogFinished(o)));
    actions.Add(MessageBus.On(CharacterMessageTopics.DialogStateReady, o => OnDialogStateReady()));
    actions.Add(MessageBus.On(ContentEpisodeProgression.ObjectivesCompletedTopic, o => OnEpisodeObjectivesCompleted()));
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
    timer = 0f;
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
    timer = 0f;
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
      timer = 0f;
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

      timer = 0f;
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
    if (ReferenceEquals(initializedLocationInstance, activeLocationInstance) &&
        string.Equals(initializedLocationId, LocationData.id, StringComparison.OrdinalIgnoreCase) &&
        initializedEpisodeRevision == ContentEpisodeProgression.EpisodeRevision &&
        initializedRegistryVersion == ActiveContentRegistryRuntime.ReloadVersion &&
        enemyPoolsByPrefab.Count > 0) {
      return true;
    }
    if (!TryBuildActiveSpawnRules()) {
      Debug.LogWarning(
        "[Spawner] Location '" + LocationData.id + "' has no prefab-based spawn rules on the active location instance. Spawning disabled."
      );
      canSpawn = false;
      return false;
    }

    canSpawn = false;
    timer = 0f;
    ReturnActiveEnemiesToPools();
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
    if (LocationData == null || LocationData.spawnInterval <= 0f) return;

    var deltaTime = TimeScale.GetDeltaTime(this);
    UpdateRespawnTimers(deltaTime);
    timer += deltaTime;
    if (timer >= LocationData.spawnInterval) {
      SpawnEnemy();
      timer = 0f;
    }
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
    for (var i = 0; i < initialSpawnCount; i++) {
      if (!SpawnEnemy()) break;
      spawnedCount += 1;
    }

    RuntimeLog.Log(
      "[Spawner] Initial episode wave spawned" +
      " requested=" + initialSpawnCount +
      " spawned=" + spawnedCount +
      " episode='" + ContentEpisodeProgression.ResolveCurrentEpisodeId() + "'"
    );
  }

  private bool SpawnEnemy() {
    if (!TryPickSpawnRule(out var spawnRule)) {
      return false;
    }
    var selectedEnemyType = spawnRule.enemyType;

    bool chooseA = UnityEngine.Random.value > 0.5f;
    Vector3 spawnPosition = GetSpawnPosition(chooseA);
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
      pool.Activate(spawned);
      return true;
    }

    var enemyInfo = spawned.GetComponent<EnemyInfo>();
    if (enemyInfo != null) {
      enemyInfo.enemyType = selectedEnemyType;
    }
    pool.Activate(spawned);
    return true;
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

  public Vector3 GetSpawnPosition(bool rightSide) {
    if (mainCamera == null) mainCamera = Camera.main;
    var viewZ = Mathf.Abs(mainCamera.transform.position.z - transform.position.z);
    var worldLeft = mainCamera.ViewportToWorldPoint(new Vector3(0, 0.5f, viewZ)).x;
    var worldRight = mainCamera.ViewportToWorldPoint(new Vector3(1, 0.5f, viewZ)).x;
    var y = transform.position.y;
    var x = rightSide ? worldRight + offset : worldLeft - offset;
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

    for (var i = 0; i < activeSpawnRules.Count; i++) {
      var spawnRule = activeSpawnRules[i];
      if (spawnRule == null || string.IsNullOrWhiteSpace(spawnRule.enemyType) || spawnRule.prefab == null) continue;
      var requiredPoolSize = Mathf.Max(spawnRule.maxAlive, 1);
      if (!enemyPoolsByPrefab.TryGetValue(spawnRule.prefab, out var pool) || pool == null) {
        pool = new Pool();
        pool.Initialize(spawnRule.prefab, ParentHolder.transform, poolSize: requiredPoolSize);
        enemyPoolsByPrefab[spawnRule.prefab] = pool;
      }
      else {
        pool.EnsureCapacity(requiredPoolSize);
      }
      RuntimeLog.Log(
        "[Spawner] Initialized enemy pool" +
        " enemy_type='" + spawnRule.enemyType + "'" +
        " prefab='" + spawnRule.prefab.name + "'" +
        " level=" + spawnRule.level +
        " bonuses=" + (spawnRule.statBonuses != null ? spawnRule.statBonuses.Count : 0) +
        " max_alive=" + spawnRule.maxAlive +
        " pool_size=" + requiredPoolSize
      );
    }

    return true;
  }

  bool TryBuildActiveSpawnRules(bool logSummary = true) {
    activeSpawnRules.Clear();
    return TryBuildSpawnRulesFromActiveLocationPrefab(logSummary);
  }

  bool TryBuildSpawnRulesFromActiveLocationPrefab(bool logSummary) {
    var locationInstance = ResolveActiveLocationInstance();
    var spawnRules = locationInstance != null ? locationInstance.GetComponentInChildren<LocationEnemySpawnRules>(true) : null;
    if (spawnRules == null || !spawnRules.HasEnemyPrefabRules) {
      return false;
    }

    var rules = spawnRules.EnemyPrefabRules;
    for (var i = 0; i < rules.Count; i++) {
      var rule = rules[i];
      if (rule == null || rule.prefab == null) continue;

      if (!ContentEpisodeProgression.HasCurrentObjectives()) continue;

      var maxAlive = 0;
      var respawnDelaySeconds = -1;
      if (!TryResolveEnemyTypeFromPrefab(rule.prefab, out var enemyType)) continue;
      if (!ContentEpisodeProgression.TryResolveCurrentSpawnCount(enemyType, out maxAlive)) continue;
      if (!ContentEpisodeProgression.TryResolveCurrentRespawnSeconds(
        enemyType,
        out respawnDelaySeconds
      )) {
        respawnDelaySeconds = -1;
      }

      if (maxAlive <= 0) continue;
      TryAddSpawnRule(
        rule.prefab,
        maxAlive,
        respawnDelaySeconds,
        rule.level,
        rule.statBonuses
      );
    }

    if (activeSpawnRules.Count <= 0) {
      return false;
    }

    if (logSummary) {
      RuntimeLog.Log(
        "[Spawner] Using location prefab spawn rules" +
        " location='" + (LocationData != null ? LocationData.id : LocationManager.currentLocation) + "'" +
        " prefab_root='" + spawnRules.gameObject.name + "'" +
        " rules=" + activeSpawnRules.Count
      );
    }
    return true;
  }

  bool TryAddSpawnRule(
    GameObject enemyPrefab,
    int maxAlive,
    int respawnDelaySeconds,
    int level,
    IList<DemonStatModifier> statBonuses
  ) {
    if (enemyPrefab == null || maxAlive <= 0) return false;
    if (!TryResolveEnemyTypeFromPrefab(enemyPrefab, out var enemyType)) {
      Debug.LogWarning("[Spawner] Skipping spawn rule because enemy type could not be resolved from prefab '" + enemyPrefab.name + "'.");
      return false;
    }

    var normalizedEnemyType = NormalizeEnemyType(enemyType);
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
      existing.statBonuses = CloneStatBonuses(statBonuses);
      return true;
    }

    var spawnRule = new SpawnRuleState {
      enemyType = normalizedEnemyType,
      prefab = enemyPrefab,
      maxAlive = Mathf.Max(maxAlive, 1),
      respawnDelaySeconds = respawnDelaySeconds,
      level = Mathf.Max(level, 1),
      statBonuses = CloneStatBonuses(statBonuses)
    };
    spawnRule.pendingRespawnSeconds.Capacity = spawnRule.maxAlive;
    activeSpawnRules.Add(spawnRule);
    return true;
  }

  bool TryResolveEnemyTypeFromPrefab(GameObject enemyPrefab, out string enemyType) {
    enemyType = "";
    if (enemyPrefab == null) return false;

    var enemyInfo = enemyPrefab.GetComponent<EnemyInfo>();
    if (enemyInfo != null && !string.IsNullOrWhiteSpace(enemyInfo.enemyType)) {
      enemyType = NormalizeEnemyType(enemyInfo.enemyType);
      return !string.IsNullOrWhiteSpace(enemyType);
    }

    var enemyController = enemyPrefab.GetComponent<EnemyController>();
    if (enemyController != null && !string.IsNullOrWhiteSpace(enemyController.enemyType)) {
      enemyType = NormalizeEnemyType(enemyController.enemyType);
      return !string.IsNullOrWhiteSpace(enemyType);
    }

    enemyType = NormalizeEnemyType(enemyPrefab.name);
    return !string.IsNullOrWhiteSpace(enemyType);
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

