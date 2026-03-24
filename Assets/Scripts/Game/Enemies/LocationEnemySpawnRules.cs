using System;
using System.Collections.Generic;
using CustomInspector;
using UnityEngine;

[Serializable]
public class LocationEnemySpawnRule {
  [AssetsOnly] public GameObject prefab;
  [Min(0)] public int maxAlive = 1;
}

public class LocationEnemySpawnRules : MonoBehaviour {
  [SerializeField] List<LocationEnemySpawnRule> enemyPrefabRules = new();

  public IReadOnlyList<LocationEnemySpawnRule> EnemyPrefabRules => enemyPrefabRules;
  public bool HasEnemyPrefabRules => enemyPrefabRules != null && enemyPrefabRules.Count > 0;
}
