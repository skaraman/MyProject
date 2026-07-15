using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

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
  public string assetPath;
  public Vector3 localPosition;
  public Vector3 localEulerAngles;
  public Vector3 localScale = Vector3.one;

  bool addressablesLoadFailed;
  const int MaxCachedGameplayPrefabs = 2;
  static readonly Dictionary<string, GameObject> prefabCache = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, AsyncOperationHandle<GameObject>> prefabHandleCache = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<string> prefabCacheLru = new(MaxCachedGameplayPrefabs + 1);

  public LocationPrefabData(
    GameObject prefab = null,
    string assetPath = "",
    Vector3? localPosition = null,
    Vector3? localEulerAngles = null,
    Vector3? localScale = null
  ) {
    this.prefab = prefab;
    this.assetPath = string.IsNullOrWhiteSpace(assetPath) ? "" : assetPath.Trim();
    this.localPosition = localPosition ?? Vector3.zero;
    this.localEulerAngles = localEulerAngles ?? Vector3.zero;
    this.localScale = localScale ?? Vector3.one;
  }

  public string AssetPath => string.IsNullOrWhiteSpace(assetPath) ? "" : assetPath.Trim();

  public bool HasConfiguredPrefab() {
    return prefab != null || !string.IsNullOrWhiteSpace(AssetPath);
  }

  public GameObject ResolvePrefab() {
    var address = AssetPath;
    if (string.IsNullOrWhiteSpace(address)) {
      return prefab;
    }

    if (RuntimeAssetCache.TryGetLoaded<GameObject>(address, out var prewarmedPrefab)) {
      RuntimeLog.Log("[LocationPrefabData] Using runtime asset cache prefab address='" + address + "'.");
      addressablesLoadFailed = false;
      return prewarmedPrefab;
    }

    if (TryGetCachedPrefab(address, out var cachedPrefab)) {
      RuntimeLog.Log("[LocationPrefabData] Using cached addressable prefab address='" + address + "'.");
      addressablesLoadFailed = false;
      return cachedPrefab;
    }

    if (addressablesLoadFailed) return null;
    var loadedPrefab = LoadPrefabFromAddressables(address);
    addressablesLoadFailed = loadedPrefab == null;
    return loadedPrefab;
  }

  static bool TryGetCachedPrefab(string address, out GameObject cachedPrefab) {
    cachedPrefab = null;
    if (string.IsNullOrWhiteSpace(address)) return false;
    if (!prefabCache.TryGetValue(address, out cachedPrefab) || cachedPrefab == null) {
      return false;
    }

    TouchCachedPrefab(address);
    return true;
  }

  static void TouchCachedPrefab(string address) {
    if (string.IsNullOrWhiteSpace(address)) return;

    for (var i = prefabCacheLru.Count - 1; i >= 0; i--) {
      if (!string.Equals(prefabCacheLru[i], address, StringComparison.OrdinalIgnoreCase)) continue;
      prefabCacheLru.RemoveAt(i);
      break;
    }

    prefabCacheLru.Insert(0, address);
  }

  static void RememberCachedPrefab(string address, AsyncOperationHandle<GameObject> handle, GameObject loadedPrefab) {
    if (string.IsNullOrWhiteSpace(address) || loadedPrefab == null) {
      if (handle.IsValid()) {
        Addressables.Release(handle);
      }
      return;
    }

    if (prefabHandleCache.TryGetValue(address, out var previousHandle) && previousHandle.IsValid()) {
      Addressables.Release(previousHandle);
    }

    prefabCache[address] = loadedPrefab;
    prefabHandleCache[address] = handle;
    TouchCachedPrefab(address);

    while (prefabCacheLru.Count > MaxCachedGameplayPrefabs) {
      var evictIndex = prefabCacheLru.Count - 1;
      var evictAddress = prefabCacheLru[evictIndex];
      prefabCacheLru.RemoveAt(evictIndex);
      if (string.IsNullOrWhiteSpace(evictAddress)) continue;
      if (string.Equals(evictAddress, address, StringComparison.OrdinalIgnoreCase)) continue;

      prefabCache.Remove(evictAddress);
      if (prefabHandleCache.TryGetValue(evictAddress, out var evictHandle) && evictHandle.IsValid()) {
        Addressables.Release(evictHandle);
      }
      prefabHandleCache.Remove(evictAddress);
      RuntimeLog.Log("[LocationPrefabData] Evicted cached addressable prefab address='" + evictAddress + "'.");
    }
  }

  GameObject LoadPrefabFromAddressables(string address) {
    var startedAt = Time.realtimeSinceStartup;

    // Load the prefab asset, not an instance. LocationManager owns instantiation and staged child activation.
    try {
      var loadHandle = Addressables.LoadAssetAsync<GameObject>(address);
      var loadedPrefab = loadHandle.WaitForCompletion();

      if (loadHandle.Status == AsyncOperationStatus.Succeeded && loadedPrefab != null) {
        RememberCachedPrefab(address, loadHandle, loadedPrefab);
        var loadSeconds = Time.realtimeSinceStartup - startedAt;
        RuntimeLog.Log(
          "[LocationPrefabData] Loaded addressable prefab address='" + address +
          "' load_s=" + loadSeconds.ToString("0.0000") +
          " child_count=" + loadedPrefab.transform.childCount
        );
        return loadedPrefab;
      }

      var status = loadHandle.Status.ToString();
      var errorMessage = loadHandle.OperationException != null ? loadHandle.OperationException.Message : "none";
      if (loadHandle.IsValid()) {
        Addressables.Release(loadHandle);
      }

      Debug.LogError(
        "[LocationPrefabData] Failed to load addressable prefab address='" + address +
        "' status=" + status +
        " error='" + errorMessage + "'"
      );
    }
    catch (Exception ex) {
      Debug.LogError(
        "[LocationPrefabData] Exception loading addressable prefab address='" + address +
        "' error='" + ex.Message + "'"
      );
    }
    return null;
  }
}

[Serializable]
public class LocationInfo {
  public string id;
  public string name;
  public List<string> enemies;
  public int maxEnemies;
  public float spawnInterval;
  public List<LocationObjective> objectives;
  public LocationPrefabData locationPrefabData;

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
    this.locationPrefabData = locationPrefabData ?? new LocationPrefabData();
  }

  static string NormalizeId(string locationId) {
    return string.IsNullOrWhiteSpace(locationId) ? "" : locationId.Trim();
  }
}

public static class LocationEnemyData {
  public const string MainMenuLocationId = "mainmenu";
  public const string DomeCityLocationId = "DomeCity";
  public const string HomebaseLocationId = "Homebase";
  public const string SunkenCaveLocationId = "SunkenCave";

  static readonly Dictionary<string, LocationInfo> defaultLocations = new(StringComparer.OrdinalIgnoreCase) {
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
          assetPath: "Assets/Prefabs/Locations/DomeCity.prefab", // Updated path
          localPosition: Vector3.zero,
          localEulerAngles: Vector3.zero,
          localScale: Vector3.one
        )
      )
    }
  };

  static Dictionary<string, LocationInfo> cachedLocationsView;
  static int cachedLocationsReloadVersion = -1;

  public static Dictionary<string, LocationInfo> locations => GetActiveLocationsView();

  public static string NormalizeLocationId(string locationId) {
    return string.IsNullOrWhiteSpace(locationId) ? "" : locationId.Trim();
  }

  public static bool ContainsLocation(string locationId) {
    var normalized = NormalizeLocationId(locationId);
    return !string.IsNullOrWhiteSpace(normalized) && GetActiveLocationsView().ContainsKey(normalized);
  }

  public static bool TryGetLocation(string locationId, out LocationInfo locationInfo) {
    locationInfo = null;
    var normalized = NormalizeLocationId(locationId);
    if (string.IsNullOrWhiteSpace(normalized)) return false;
    if (!GetActiveLocationsView().TryGetValue(normalized, out var found) || found == null) return false;
    locationInfo = ActiveContentRegistry.CloneLocation(found) ?? found;
    return true;
  }

  public static bool TryGetBuiltInLocation(string locationId, out LocationInfo locationInfo) {
    locationInfo = null;
    var normalized = NormalizeLocationId(locationId);
    if (string.IsNullOrWhiteSpace(normalized)) return false;
    if (!defaultLocations.TryGetValue(normalized, out var found) || found == null) return false;
    locationInfo = ActiveContentRegistry.CloneLocation(found) ?? found;
    return true;
  }

  public static bool TryGetLocationByPrefab(GameObject prefab, out LocationInfo locationInfo) {
    locationInfo = null;
    if (prefab == null) return false;

    var selectedAssetPath = GetPrefabAssetPath(prefab);
    var activeLocations = GetActiveLocationsView();
    if (TryFindLocationByPrefab(activeLocations, prefab, selectedAssetPath, out locationInfo)) {
      return true;
    }

    if (!TryFindLocationByPrefab(defaultLocations, prefab, selectedAssetPath, out var builtInLocation) || builtInLocation == null) {
      return false;
    }

    var normalizedLocationId = NormalizeLocationId(builtInLocation.id);
    if (!string.IsNullOrWhiteSpace(normalizedLocationId) &&
        activeLocations.TryGetValue(normalizedLocationId, out var activeLocation) &&
        activeLocation != null) {
      locationInfo = ActiveContentRegistry.CloneLocation(activeLocation) ?? activeLocation;
    }
    else {
      locationInfo = ActiveContentRegistry.CloneLocation(builtInLocation) ?? builtInLocation;
    }

    if (ShouldLogLocationDebug()) {
      RuntimeLog.Log(
        "[LocationEnemyData] Resolved prefab via built-in fallback" +
        " prefab='" + prefab.name +
        "' asset_path='" + (string.IsNullOrWhiteSpace(selectedAssetPath) ? "-" : selectedAssetPath) +
        "' location='" + normalizedLocationId + "'"
      );
    }

    return locationInfo != null;
  }

  public static string ResolveRequestedOrDefault(string locationId) {
    if (ContainsLocation(locationId)) return NormalizeLocationId(locationId);
    return GetDefaultLocation();
  }

  public static string GetDefaultLocation() {
    var externalDefault = NormalizeLocationId(ActiveContentRegistryRuntime.GetDefaultLocationId());
    var activeLocations = GetActiveLocationsView();
    if (!string.IsNullOrWhiteSpace(externalDefault) && activeLocations.ContainsKey(externalDefault)) {
      return externalDefault;
    }

    if (activeLocations.ContainsKey(DomeCityLocationId)) return DomeCityLocationId;
    foreach (var pair in activeLocations) {
      if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null) return pair.Key;
    }
    return DomeCityLocationId;
  }

  static Dictionary<string, LocationInfo> GetActiveLocationsView() {
    if (!ActiveContentRegistryRuntime.HasActiveExternalContent()) {
      cachedLocationsView = null;
      cachedLocationsReloadVersion = -1;
      return defaultLocations;
    }

    var reloadVersion = ActiveContentRegistryRuntime.ReloadVersion;
    if (cachedLocationsView != null && cachedLocationsReloadVersion == reloadVersion) {
      return cachedLocationsView;
    }

    var merged = new Dictionary<string, LocationInfo>(defaultLocations, StringComparer.OrdinalIgnoreCase);
    var registry = ActiveContentRegistryRuntime.Registry;
    var externalLocations = registry != null ? registry.Locations : null;
    if (externalLocations != null) {
      for (var i = 0; i < externalLocations.Count; i++) {
        var location = externalLocations[i];
        if (location == null) continue;
        var normalizedId = NormalizeLocationId(location.id);
        if (string.IsNullOrWhiteSpace(normalizedId)) continue;
        merged[normalizedId] = ActiveContentRegistry.CloneLocation(location) ?? location;
      }
    }

    cachedLocationsView = merged;
    cachedLocationsReloadVersion = reloadVersion;
    return cachedLocationsView;
  }

  static bool DoesLocationUsePrefab(LocationInfo locationInfo, GameObject prefab, string selectedAssetPath) {
    if (locationInfo == null || prefab == null) return false;

    var prefabData = locationInfo.locationPrefabData;
    if (prefabData == null) return false;
    if (ReferenceEquals(prefabData.prefab, prefab)) return true;

    var configuredAssetPath = NormalizeAssetPath(prefabData.AssetPath);
    if (!string.IsNullOrWhiteSpace(selectedAssetPath) &&
        !string.IsNullOrWhiteSpace(configuredAssetPath) &&
        string.Equals(configuredAssetPath, selectedAssetPath, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    if (prefabData.prefab == null && string.IsNullOrWhiteSpace(configuredAssetPath)) {
      return false;
    }

    var resolvedPrefab = prefabData.ResolvePrefab();
    return ReferenceEquals(resolvedPrefab, prefab);
  }

  static bool TryFindLocationByPrefab(
    Dictionary<string, LocationInfo> locationsView,
    GameObject prefab,
    string selectedAssetPath,
    out LocationInfo locationInfo
  ) {
    locationInfo = null;
    if (locationsView == null || prefab == null) {
      return false;
    }

    foreach (var pair in locationsView) {
      var candidate = pair.Value;
      if (candidate == null) continue;
      if (!DoesLocationUsePrefab(candidate, prefab, selectedAssetPath)) continue;
      locationInfo = candidate;
      return true;
    }

    return false;
  }

  static bool ShouldLogLocationDebug() {
    return Application.isEditor || Debug.isDebugBuild;
  }

  static string NormalizeAssetPath(string assetPath) {
    return string.IsNullOrWhiteSpace(assetPath)
      ? ""
      : assetPath.Trim().Replace('\\', '/');
  }

  static string GetPrefabAssetPath(GameObject prefab) {
#if UNITY_EDITOR
    return NormalizeAssetPath(UnityEditor.AssetDatabase.GetAssetPath(prefab));
#else
    return "";
#endif
  }
}
