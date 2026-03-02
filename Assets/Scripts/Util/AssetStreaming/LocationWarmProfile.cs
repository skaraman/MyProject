using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct EnemyArchetypeProfileEntry {
  public string enemyType;
  public GameObject prefab;
}

[CreateAssetMenu(fileName = "LocationWarmProfile", menuName = "Sprite Streaming/Location Warm Profile")]
public class LocationWarmProfile : ScriptableObject {
  [SerializeField] string locationId = "DomeCity";
  [SerializeField] List<string> criticalSpriteLibraries = new();
  [SerializeField] List<string> criticalDirectAddresses = new();
  [SerializeField] List<string> warmSpriteLibraries = new();
  [SerializeField] List<string> warmDirectAddresses = new();
  [SerializeField] List<string> warmUiDirectAddresses = new();
  [SerializeField] List<EnemyArchetypeProfileEntry> enemyArchetypes = new();

  public string LocationId => string.IsNullOrWhiteSpace(locationId) ? "" : locationId.Trim();
  public IReadOnlyList<string> CriticalSpriteLibraries => criticalSpriteLibraries;
  public IReadOnlyList<string> CriticalDirectAddresses => criticalDirectAddresses;
  public IReadOnlyList<string> WarmSpriteLibraries => warmSpriteLibraries;
  public IReadOnlyList<string> WarmDirectAddresses => warmDirectAddresses;
  public IReadOnlyList<string> WarmUiDirectAddresses => warmUiDirectAddresses;

  public Dictionary<string, GameObject> BuildEnemyArchetypePrefabMap() {
    var map = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
    if (enemyArchetypes == null || enemyArchetypes.Count <= 0) return map;

    for (var i = 0; i < enemyArchetypes.Count; i++) {
      var entry = enemyArchetypes[i];
      if (string.IsNullOrWhiteSpace(entry.enemyType) || entry.prefab == null) continue;
      map[entry.enemyType.Trim()] = entry.prefab;
    }
    return map;
  }

  public void CollectExtraWarmLists(
    List<string> outCriticalLibraries,
    List<string> outCriticalAddresses,
    List<string> outWarmLibraries,
    List<string> outWarmAddresses
  ) {
    AddRangeUnique(outCriticalLibraries, criticalSpriteLibraries);
    AddRangeUnique(outCriticalAddresses, criticalDirectAddresses);
    AddRangeUnique(outWarmLibraries, warmSpriteLibraries);
    AddRangeUnique(outWarmAddresses, warmDirectAddresses);
    AddRangeUnique(outWarmAddresses, warmUiDirectAddresses);
  }

  static void AddRangeUnique(List<string> output, List<string> input) {
    if (output == null || input == null || input.Count <= 0) return;
    for (var i = 0; i < input.Count; i++) {
      var value = string.IsNullOrWhiteSpace(input[i]) ? "" : input[i].Trim();
      if (string.IsNullOrWhiteSpace(value)) continue;
      var exists = false;
      for (var j = 0; j < output.Count; j++) {
        if (!string.Equals(output[j], value, StringComparison.OrdinalIgnoreCase)) continue;
        exists = true;
        break;
      }
      if (!exists) output.Add(value);
    }
  }
}
