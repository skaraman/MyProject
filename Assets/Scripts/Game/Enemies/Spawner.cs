using System;
using System.Collections.Generic;
using CustomInspector;
using UnityEngine;

public class Spawner : MonoBehaviour {
  sealed class SpawnRuleState {
    public string enemyType;
    public GameObject prefab;
    public int maxAlive;
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
  private LocationInfo LocationData;
  readonly Dictionary<GameObject, Pool> enemyPoolsByPrefab = new();
  readonly Dictionary<GameObject, Pool> activeInstancePools = new();
  readonly List<SpawnRuleState> activeSpawnRules = new();
  readonly List<SpawnRuleState> spawnCandidateScratch = new();

  private List<Action> actions = new();

  void Start() {
    actions.Add(MessageBus.On("ReadyForSpawns", (o) => { InitLocation(); }));
  }

  void OnDestroy() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
    ClearEnemyPools();
  }

  public void InitLocation() {
    if (!TryResolveLocation(out LocationData)) {
      Debug.LogError("[Spawner] No valid location found for location '" + LocationManager.currentLocation + "'.");
      canSpawn = false;
      return;
    }
    if (!TryBuildActiveSpawnRules()) {
      Debug.LogWarning(
        "[Spawner] Location '" + LocationData.id + "' has no prefab-based spawn rules on the active location instance. Spawning disabled."
      );
      canSpawn = false;
      return;
    }

    canSpawn = false;
    ClearEnemyPools();
    if (!TryInitializeEnemyPools()) {
      canSpawn = false;
      return;
    }

    if (!Application.isPlaying || !prewarmEnemyWaveBeforeSpawning) {
      canSpawn = true;
      return;
    }

    if (ShouldDeferEnemyWaveWarmupToActiveLoadingOverlay()) {
      timer = 0f;
      canSpawn = true;
      Debug.Log(
        "[Spawner] Skipping local enemy-wave warmup because an active loading overlay/warm gate is already warming startup archetypes." +
        " location='" + LocationManager.currentLocation + "'"
      );
      return;
    }

    var archetypes = BuildCurrentLocationArchetypeMapForWarmup();
    if (archetypes.Count <= 0) {
      canSpawn = true;
      return;
    }

    var orchestrator = StreamingWarmOrchestrator.Instance;
    if (orchestrator == null) {
      canSpawn = true;
      return;
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
      canSpawn = true;
    });
  }

  static bool ShouldDeferEnemyWaveWarmupToActiveLoadingOverlay() {
    if (!Application.isPlaying) return false;
    return SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning;
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
    Debug.Log($"[OffscreenSpawner] rightSide={rightSide}, worldLeft={worldLeft}, worldRight={worldRight}, offset={offset}, spawn={spawnPosition}");
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
      TryAddSpawnRule(rule.prefab, rule.maxAlive);
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

  bool TryAddSpawnRule(GameObject enemyPrefab, int maxAlive) {
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
      return true;
    }

    activeSpawnRules.Add(new SpawnRuleState {
      enemyType = normalizedEnemyType,
      prefab = enemyPrefab,
      maxAlive = Mathf.Max(maxAlive, 1)
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
    var locationManager = FindAnyObjectByType<LocationManager>();
    return locationManager != null ? locationManager.ActiveLocationInstance : null;
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
}

