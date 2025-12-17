using System.Collections.Generic;

public class LocationInfo {
  public string name;
  public List<string> enemies;
  public int maxEnemies;
  public float spawnInterval;
  public int finalKillCount;

  public LocationInfo(string name, List<string> enemies, int maxEnemies, float spawnInterval, int finalKillCount) {
    this.name = name;
    this.enemies = enemies;
    this.maxEnemies = maxEnemies;
    this.spawnInterval = spawnInterval;
    this.finalKillCount = finalKillCount;
  }

}

public static class LocationEnemyData {
  public static Dictionary<string, LocationInfo> zones { get; } = new Dictionary<string, LocationInfo> {
    { "DomeCity", new LocationInfo("DomeCity", new List<string> { "Imp" }, 1, 2.0f, 3) },
  };

  public static Dictionary<string, int> totalKills { get; } = new Dictionary<string, int> {
    { "Imp", 0 },
  };
}


