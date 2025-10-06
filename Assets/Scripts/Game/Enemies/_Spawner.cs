using System;
using System.Collections.Generic;
using UnityEngine;

public class Spawner : MonoBehaviour {
  public List<string> enemyTypes { get; set; } = new() { "Imp" };
  public float spawnInterval = 5f;
  public int maxEnemies = 10;
  
  public List<GameObject> enemyPrefabs;
  
  private float timer = 0f;
  private List<GameObject> spawnedEnemies = new();

  void Start() {

  }

  void Update() {

  }
}
