using System;
using System.Collections.Generic;
using CustomInspector;
using UnityEngine;

public class Spawner : MonoBehaviour {
  public SerializableSortedDictionary<string, GameObject> enemyPrefabs;
  public GameObject ParentHolder;

  private Camera mainCamera;
  private float offset = 5f;
  private float timer = 0f;
  private bool canSpawn = false;
  private Dictionary<string, Pool> EnemyPools = new();
  private LocationInfo ZoneData;

  private List<Action> actions = new();

  void Start() {
    actions.Add(MessageBus.On("ReadyForSpawns", (o) => { InitZone(); }));

  }

  void InitZone() {
    ZoneData = LocationEnemyData.zones[LocationTracker.currentLocation];
    canSpawn = true;
    foreach (var enemyType in ZoneData.enemies) {
      if (!EnemyPools.ContainsKey(enemyType)) {
        var pool = new Pool();
        pool.Initialize(enemyPrefabs[enemyType], ParentHolder.transform, poolSize: ZoneData.maxEnemies);
        EnemyPools[enemyType] = pool;
      }
    }
  }

  void Update() {
    if (!canSpawn) return;
    timer += Time.deltaTime;
    if (timer >= ZoneData.spawnInterval) {
      SpawnEnemy();
      timer = 0f;
    }
  }

  private void SpawnEnemy() {
    var rndEnemyIndex = UnityEngine.Random.Range(0, ZoneData.enemies.Count);
    GameObject enemyPrefab = enemyPrefabs[ZoneData.enemies[rndEnemyIndex]];
    bool chooseA = UnityEngine.Random.value > 0.5f;
    Vector3 spawnPosition = GetSpawnPosition(chooseA);

    EnemyPools[enemyPrefab.GetComponent<EnemyInfo>().enemyType].Spawn(spawnPosition, Quaternion.identity);

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

}

