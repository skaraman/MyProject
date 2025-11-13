using System;
using System.Collections.Generic;
using UnityEngine;
using CustomInspector;

public class Spawner : MonoBehaviour {
  public SerializableSortedDictionary<string, GameObject> enemyPrefabs;

  public float spawnInterval = 5f;
  public int maxEnemies = 10;
  private float timer = 0f;

  private List<GameObject> spawnedEnemies = new();
  private bool canSpawn = false;

  private List<Action> actions = new();

  void Start() {
    actions.Add(MessageBus.On("ReadyForSpawns", (o) => { canSpawn = true; }));
  }

  void Update() {
    if (!canSpawn) return;

    timer += Time.deltaTime;

    // TODO
    if (timer >= spawnInterval && spawnedEnemies.Count < maxEnemies) {
      SpawnEnemy();
      timer = 0f;
    }
  }



  private void SpawnEnemy() {

    var ZoneData = LocationEnemyData.zones[LocationTracker.currentLocation];
    var rndEnemyIndex = UnityEngine.Random.Range(0, ZoneData.enemies.Count);
    GameObject enemyPrefab = enemyPrefabs[ZoneData.enemies[rndEnemyIndex]];

    Vector3 spawnPosition = transform.position;
    Quaternion spawnRotation = Quaternion.identity;

    GameObject newEnemy = Instantiate(enemyPrefab, spawnPosition, spawnRotation);
    spawnedEnemies.Add(newEnemy);
  }

}

