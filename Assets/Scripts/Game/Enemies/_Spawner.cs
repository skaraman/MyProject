using System;
using System.Collections.Generic;
using CustomInspector;
using UnityEngine;

public class Spawner : MonoBehaviour {
  public SerializableSortedDictionary<string, GameObject> enemyPrefabs;
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
  private Dictionary<string, Pool> EnemyPools = new();
  private LocationInfo LocationData;

  private List<Action> actions = new();

  void Start() {
    actions.Add(MessageBus.On("ReadyForSpawns", (o) => { InitLocation(); }));

  }

  void InitLocation() {
    if (!TryResolveLocation(out LocationData)) {
      Debug.LogError("[Spawner] No valid location found for location '" + LocationManager.currentLocation + "'.");
      canSpawn = false;
      return;
    }
    if (LocationData.enemies == null || LocationData.enemies.Count <= 0 || LocationData.maxEnemies <= 0) {
      Debug.LogWarning("[Spawner] Location '" + LocationData.id + "' has no enemy spawn configuration. Spawning disabled.");
      canSpawn = false;
      return;
    }
    canSpawn = false;
    foreach (var enemyType in LocationData.enemies) {
      if (!EnemyPools.ContainsKey(enemyType)) {
        var pool = new Pool();
        pool.Initialize(enemyPrefabs[enemyType], ParentHolder.transform, poolSize: LocationData.maxEnemies);
        EnemyPools[enemyType] = pool;
      }
    }

    if (!Application.isPlaying || !prewarmEnemyWaveBeforeSpawning) {
      canSpawn = true;
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

  void Update() {
    if (!canSpawn) return;
    timer += Time.deltaTime;
    if (timer >= LocationData.spawnInterval) {
      SpawnEnemy();
      timer = 0f;
    }
  }

  private void SpawnEnemy() {
    var rndEnemyIndex = UnityEngine.Random.Range(0, LocationData.enemies.Count);
    var selectedEnemyType = LocationData.enemies[rndEnemyIndex];
    if (!enemyPrefabs.TryGetValue(selectedEnemyType, out var enemyPrefab) || enemyPrefab == null) {
      Debug.LogError($"[Spawner] Missing enemy prefab for enemy type '{selectedEnemyType}'.");
      return;
    }

    bool chooseA = UnityEngine.Random.value > 0.5f;
    Vector3 spawnPosition = GetSpawnPosition(chooseA);
    if (!EnemyPools.TryGetValue(selectedEnemyType, out var pool) || pool == null) {
      Debug.LogError($"[Spawner] Missing pool for enemy type '{selectedEnemyType}'.");
      return;
    }

    var spawned = pool.Spawn(spawnPosition, Quaternion.identity);
    if (spawned == null) return;

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
    var enemyType = enemy.GetComponent<EnemyInfo>().enemyType;
    EnemyPools[enemyType].Despawn(enemy);

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
    var fromProfile = ResolveArchetypesFromLocationProfile();
    if (fromProfile.Count > 0) return fromProfile;
    return BuildArchetypePrefabsByType();
  }

  Dictionary<string, GameObject> BuildArchetypePrefabsByType() {
    var map = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
    if (LocationData == null) {
      TryResolveLocation(out LocationData);
    }
    if (LocationData == null || LocationData.enemies == null || enemyPrefabs == null) return map;

    for (var i = 0; i < LocationData.enemies.Count; i++) {
      var enemyType = LocationData.enemies[i];
      if (string.IsNullOrWhiteSpace(enemyType)) continue;
      if (!enemyPrefabs.TryGetValue(enemyType, out var prefab) || prefab == null) continue;
      map[enemyType.Trim()] = prefab;
    }

    return map;
  }

  Dictionary<string, GameObject> ResolveArchetypesFromLocationProfile() {
    var map = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
    var profile = LocationWarmRegistryRuntime.ResolveForLocation(LocationManager.currentLocation);
    if (profile == null) return map;
    var profileMap = profile.BuildEnemyArchetypePrefabMap();
    if (profileMap == null || profileMap.Count <= 0) return map;
    foreach (var pair in profileMap) {
      if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) continue;
      map[pair.Key.Trim()] = pair.Value;
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

}

