using System;
using System.Collections.Generic;
using UnityEngine;

public enum LocationObjectiveType {
  FinalKillCount = 0,
  SurvivalTimeSeconds = 1,
  Custom = 2
}

[Serializable]
public class LocationObjective {
  public LocationObjectiveType type;
  public string description;
  public int targetCount;
  public float targetSeconds;

  public LocationObjective(LocationObjectiveType type, string description, int targetCount = 0, float targetSeconds = 0f) {
    this.type = type;
    this.description = description ?? "";
    this.targetCount = targetCount;
    this.targetSeconds = targetSeconds;
  }

  public static LocationObjective FinalKillCount(int requiredKills, string description = "Defeat target enemies") {
    return new LocationObjective(
      type: LocationObjectiveType.FinalKillCount,
      description: description,
      targetCount: Mathf.Max(0, requiredKills),
      targetSeconds: 0f
    );
  }

  public static LocationObjective SurvivalTime(float requiredSeconds, string description = "Survive the encounter") {
    return new LocationObjective(
      type: LocationObjectiveType.SurvivalTimeSeconds,
      description: description,
      targetCount: 0,
      targetSeconds: Mathf.Max(0f, requiredSeconds)
    );
  }

  public static LocationObjective Custom(string description) {
    return new LocationObjective(LocationObjectiveType.Custom, description ?? "");
  }
}

[Serializable]
public class LocationPrefabData {
  public GameObject prefab;
  public string resourcePath;
  public Vector3 localPosition;
  public Vector3 localEulerAngles;
  public Vector3 localScale = Vector3.one;
  bool resourceLoadAttempted;

  public LocationPrefabData(
    GameObject prefab = null,
    string resourcePath = "",
    Vector3? localPosition = null,
    Vector3? localEulerAngles = null,
    Vector3? localScale = null
  ) {
    this.prefab = prefab;
    this.resourcePath = string.IsNullOrWhiteSpace(resourcePath) ? "" : resourcePath.Trim();
    this.localPosition = localPosition ?? Vector3.zero;
    this.localEulerAngles = localEulerAngles ?? Vector3.zero;
    this.localScale = localScale ?? Vector3.one;
  }

  public string ResourcePath => string.IsNullOrWhiteSpace(resourcePath) ? "" : resourcePath.Trim();

  public GameObject ResolvePrefab() {
    if (prefab != null) return prefab;
    if (resourceLoadAttempted) return null;
    var path = ResourcePath;
    if (string.IsNullOrWhiteSpace(path)) return null;
    resourceLoadAttempted = true;
    prefab = Resources.Load<GameObject>(path);
    return prefab;
  }
}

[Serializable]
public class LocationInfo {
  public string id;
  public string name;
  public List<string> enemies;
  public int maxEnemies;
  public float spawnInterval;
  public int finalKillCount;
  public List<LocationObjective> objectives;
  public LocationPrefabData locationPrefabData;

  // Compatibility alias for older call sites that used "locationPrefab" directly.
  public LocationPrefabData locationPrefab => locationPrefabData;

  public LocationInfo(
    string id,
    string name,
    List<string> enemies,
    int maxEnemies,
    float spawnInterval,
    List<LocationObjective> objectives = null,
    LocationPrefabData locationPrefabData = null
  ) {
    this.id = NormalizeId(id);
    this.name = string.IsNullOrWhiteSpace(name) ? this.id : name.Trim();
    this.enemies = enemies != null ? new List<string>(enemies) : new List<string>();
    this.maxEnemies = Mathf.Max(0, maxEnemies);
    this.spawnInterval = Mathf.Max(0f, spawnInterval);
    this.objectives = objectives != null ? new List<LocationObjective>(objectives) : new List<LocationObjective>();
    this.finalKillCount = ResolveFinalKillObjectiveCount(this.objectives);
    this.locationPrefabData = locationPrefabData ?? new LocationPrefabData();
  }

  // Legacy signature retained so existing code compiles while objectives migrate.
  public LocationInfo(
    string name,
    List<string> enemies,
    int maxEnemies,
    float spawnInterval,
    int finalKillCount,
    GameObject locationPrefab
  ) : this(
    id: name,
    name: name,
    enemies: enemies,
    maxEnemies: maxEnemies,
    spawnInterval: spawnInterval,
    objectives: new List<LocationObjective> {
      LocationObjective.FinalKillCount(finalKillCount, "Defeat target enemies")
    },
    locationPrefabData: new LocationPrefabData(prefab: locationPrefab)
  ) {
    this.finalKillCount = Mathf.Max(0, finalKillCount);
  }

  static int ResolveFinalKillObjectiveCount(List<LocationObjective> source) {
    if (source == null || source.Count <= 0) return 0;
    for (var i = 0; i < source.Count; i++) {
      var objective = source[i];
      if (objective == null) continue;
      if (objective.type != LocationObjectiveType.FinalKillCount) continue;
      return Mathf.Max(0, objective.targetCount);
    }
    return 0;
  }

  static string NormalizeId(string locationId) {
    return string.IsNullOrWhiteSpace(locationId) ? "" : locationId.Trim();
  }
}

public static class LocationEnemyData {
  public const string MainMenuLocationId = "mainmenu";
  public const string DomeCityLocationId = "DomeCity";

  public static Dictionary<string, LocationInfo> locations { get; } = new(StringComparer.OrdinalIgnoreCase) {
    {
      MainMenuLocationId,
      new LocationInfo(
        id: MainMenuLocationId,
        name: "Main Menu",
        enemies: new List<string>(),
        maxEnemies: 0,
        spawnInterval: 0f,
        objectives: new List<LocationObjective>(),
        locationPrefabData: new LocationPrefabData()
      )
    },
    {
      DomeCityLocationId,
      new LocationInfo(
        id: DomeCityLocationId,
        name: "Dome City",
        enemies: new List<string> { "Imp" },
        maxEnemies: 1,
        spawnInterval: 2.0f,
        objectives: new List<LocationObjective> {
          LocationObjective.FinalKillCount(3, "Defeat 3 enemies"),
          LocationObjective.SurvivalTime(60f, "Survive for 60 seconds")
        },
        locationPrefabData: new LocationPrefabData(
          resourcePath: "Locations/DomeCity",
          localPosition: Vector3.zero,
          localEulerAngles: Vector3.zero,
          localScale: Vector3.one
        )
      )
    }
  };

  public static Dictionary<string, int> totalKills { get; } = new(StringComparer.OrdinalIgnoreCase) {
    { "Imp", 0 },
  };

  public static string NormalizeLocationId(string locationId) {
    return string.IsNullOrWhiteSpace(locationId) ? "" : locationId.Trim();
  }

  public static bool ContainsLocation(string locationId) {
    var normalized = NormalizeLocationId(locationId);
    return !string.IsNullOrWhiteSpace(normalized) && locations.ContainsKey(normalized);
  }

  public static bool TryGetLocation(string locationId, out LocationInfo locationInfo) {
    locationInfo = null;
    var normalized = NormalizeLocationId(locationId);
    if (string.IsNullOrWhiteSpace(normalized)) return false;
    if (!locations.TryGetValue(normalized, out var found) || found == null) return false;
    locationInfo = found;
    return true;
  }

  public static LocationInfo GetLocationOrDefault(string locationId) {
    if (TryGetLocation(locationId, out var location)) return location;
    if (TryGetLocation(GetDefaultLocation(), out var fallback)) return fallback;
    return null;
  }

  public static string ResolveRequestedOrDefault(string locationId) {
    if (ContainsLocation(locationId)) return NormalizeLocationId(locationId);
    return GetDefaultLocation();
  }

  public static string GetDefaultLocation() {
    if (locations.ContainsKey(DomeCityLocationId)) return DomeCityLocationId;
    foreach (var pair in locations) {
      if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null) return pair.Key;
    }
    return DomeCityLocationId;
  }
}


