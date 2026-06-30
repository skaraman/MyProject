using System;
using System.Collections.Generic;
using CustomInspector;
using UnityEngine;

public class Spawner : MonoBehaviour {
  sealed class SpawnRuleState {
    public string enemyType;
    public GameObject prefab;
    public int maxAlive;
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
  private LocationInfo LocationData;
  readonly Dictionary<GameObject, Pool> enemyPoolsByPrefab = new();
  readonly Dictionary<GameObject, Pool> activeInstancePools = new();
  readonly List<SpawnRuleState> activeSpawnRules = new();
  readonly List<SpawnRuleState> spawnCandidateScratch = new();
  readonly List<HurtBox2D> hurtBoxScratch = new();

  private List<Action> actions = new();
  string pendingSpawnLocationId = "";
  string lastDeferredInitState = "";
  GameObject initializedLocationInstance;
  string initializedLocationId = "";

  void Start() {
    actions.Add(MessageBus.On("ReadyForSpawns", o => OnReadyForSpawns()));
    actions.Add(MessageBus.On("LocationUpdated", o => OnLocationUpdated(o)));
    actions.Add(MessageBus.On("LocationLocationChanged", o => OnLocationLocationChanged(o)));
  }

  void OnDestroy() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
    ClearEnemyPools();
  }

  public void InitLocation() {
    spawnGateOpen = true;
    pendingSpawnLocationId = NormalizeLocationId(LocationManager.currentLocation);
    TryInitializeLocation("manual");
  }

  public int GetCurrentLocationArchetypeWarmupCount() {
    EnsureWarmupSpawnRules();
    return activeSpawnRules.Count;
  }

  void OnReadyForSpawns() {
    spawnGateOpen = true;
    pendingSpawnLocationId = NormalizeLocationId(LocationManager.currentLocation);
    TryInitializeLocation("ready_for_spawns");
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
    pendingSpawnLocationId = NormalizeLocationId(payload as string);
    if (string.IsNullOrWhiteSpace(pendingSpawnLocationId) ||
        string.Equals(pendingSpawnLocationId, LocationEnemyData.MainMenuLocationId, StringComparison.OrdinalIgnoreCase)) {
      spawnGateOpen = false;
    }
    canSpawn = false;
    timer = 0f;
    ClearEnemyPools();
    activeSpawnRules.Clear();
    LocationData = null;
    lastDeferredInitState = "";
    initializedLocationInstance = null;
    initializedLocationId = "";
  }

  void OnLocationLocationChanged(object payload) {
    if (!spawnGateOpen) return;

    if (!(payload is GameObject locationInstance) || locationInstance == null) {
      canSpawn = false;
      return;
    }

    TryInitializeLocation("location_instance_changed");
  }

  bool TryInitializeLocation(string source) {
    var activeLocationInstance = ResolveActiveLocationInstance();
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
    ClearEnemyPools();
    if (!TryInitializeEnemyPools()) {
      canSpawn = false;
      return false;
    }
    initializedLocationInstance = activeLocationInstance;
    initializedLocationId = LocationData != null ? LocationData.id : pendingSpawnLocationId;

    if (!Application.isPlaying || !prewarmEnemyWaveBeforeSpawning) {
      lastDeferredInitState = "";
      canSpawn = true;
      Debug.Log(
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
      Debug.Log(
        "[Spawner] Skipping local enemy-wave warmup because an active loading overlay/warm gate is already warming startup archetypes." +
        " location='" + LocationManager.currentLocation + "'"
      );
      Debug.Log(
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
      Debug.Log(
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
      Debug.Log(
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
      timer = 0f;
      lastDeferredInitState = "";
      canSpawn = true;
      Debug.Log(
        "[Spawner] Enemy warmup complete location='" + (LocationData != null ? LocationData.id : pendingSpawnLocationId) +
        "' rules=" + activeSpawnRules.Count +
        " can_spawn=1"
      );
    });
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
    timer += TimeScale.GetDeltaTime(this);
    if (timer >= LocationData.spawnInterval) {
      SpawnEnemy();
      timer = 0f;
    }
  }

  private void SpawnEnemy() {
    if (!TryPickSpawnRule(out var spawnRule)) {
      return;
    }
    var selectedEnemyType = spawnRule.enemyType;

    bool chooseA = UnityEngine.Random.value > 0.5f;
    Vector3 spawnPosition = GetSpawnPosition(chooseA);
    if (!enemyPoolsByPrefab.TryGetValue(spawnRule.prefab, out var pool) || pool == null) {
      Debug.LogError(
        "[Spawner] Missing pool for enemy type '" + selectedEnemyType + "' prefab='" +
        (spawnRule.prefab != null ? spawnRule.prefab.name : "-") + "'."
      );
      return;
    }

    var spawned = pool.Spawn(spawnPosition, Quaternion.identity);
    if (spawned == null) return;
    activeInstancePools[spawned] = pool;

    ApplySpawnContextToEnemy(spawned, spawnRule);

    var enemyController = spawned.GetComponent<EnemyController>();
    if (enemyController != null) {
      enemyController.SetEnemyType(selectedEnemyType, playDefaultImmediately: true);
      return;
    }

    var enemyInfo = spawned.GetComponent<EnemyInfo>();
    if (enemyInfo != null) {
      enemyInfo.enemyType = selectedEnemyType;
    }
  }

  public void DespawnEnemy(GameObject enemy) {
    if (enemy == null) return;
    if (!activeInstancePools.TryGetValue(enemy, out var pool) || pool == null) {
      var enemyInfo = enemy.GetComponent<EnemyInfo>();
      var enemyType = NormalizeEnemyType(enemyInfo != null ? enemyInfo.enemyType : "");
      Debug.LogError("[Spawner] Cannot despawn enemy because no pool is registered for enemy type '" + enemyType + "'.");
      return;
    }

    activeInstancePools.Remove(enemy);
    pool.Despawn(enemy);
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
      Debug.Log(
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
      var pool = new Pool();
      pool.Initialize(spawnRule.prefab, ParentHolder.transform, poolSize: Mathf.Max(spawnRule.maxAlive, 1));
      enemyPoolsByPrefab[spawnRule.prefab] = pool;
      Debug.Log(
        "[Spawner] Initialized enemy pool" +
        " enemy_type='" + spawnRule.enemyType + "'" +
        " prefab='" + spawnRule.prefab.name + "'" +
        " level=" + spawnRule.level +
        " bonuses=" + (spawnRule.statBonuses != null ? spawnRule.statBonuses.Count : 0) +
        " max_alive=" + spawnRule.maxAlive +
        " pool_size=" + Mathf.Max(spawnRule.maxAlive, 1)
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
      if (rule == null || rule.prefab == null || rule.maxAlive <= 0) continue;
      TryAddSpawnRule(rule.prefab, rule.maxAlive, rule.level, rule.statBonuses);
    }

    if (activeSpawnRules.Count <= 0) {
      return false;
    }

    if (logSummary) {
      Debug.Log(
        "[Spawner] Using location prefab spawn rules" +
        " location='" + (LocationData != null ? LocationData.id : LocationManager.currentLocation) + "'" +
        " prefab_root='" + spawnRules.gameObject.name + "'" +
        " rules=" + activeSpawnRules.Count
      );
    }
    return true;
  }

  bool TryAddSpawnRule(GameObject enemyPrefab, int maxAlive, int level, IList<DemonStatModifier> statBonuses) {
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
      existing.level = Mathf.Max(level, 1);
      existing.statBonuses = CloneStatBonuses(statBonuses);
      return true;
    }

    activeSpawnRules.Add(new SpawnRuleState {
      enemyType = normalizedEnemyType,
      prefab = enemyPrefab,
      maxAlive = Mathf.Max(maxAlive, 1),
      level = Mathf.Max(level, 1),
      statBonuses = CloneStatBonuses(statBonuses)
    });
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
      spawnCandidateScratch.Add(candidate);
    }

    if (spawnCandidateScratch.Count <= 0) {
      spawnRule = null;
      return false;
    }

    spawnRule = spawnCandidateScratch[UnityEngine.Random.Range(0, spawnCandidateScratch.Count)];
    return spawnRule != null;
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
    Debug.Log("[Spawner] Deferred spawn init " + message);
  }
}

