using System;
using System.Collections.Generic;
using UnityEngine;

public class LocationManager : MonoBehaviour {
  // Former LocationTracker logic, kept static for cross-system access.
  public static string currentLocation = "nowhere";

  public static void UpdateLocation(string newLocation) {
    currentLocation = string.IsNullOrWhiteSpace(newLocation) ? "nowhere" : newLocation.Trim();
    MessageBus.Send("LocationUpdated", currentLocation);
  }

  [Header("Location Runtime")]
  [SerializeField] Transform locationRoot;
  [SerializeField] bool loadFromTrackerOnStart = true;
  [SerializeField] bool logLoads = true;

  readonly List<Action> actions = new();
  LocationInfo activeLocation;
  string currentLocationId = "";
  GameObject activeLocationInstance;

  public string CurrentLocationId => currentLocationId;
  public LocationInfo CurrentLocation => activeLocation;
  public IReadOnlyList<LocationObjective> CurrentObjectives =>
    activeLocation != null && activeLocation.objectives != null ? activeLocation.objectives : Array.Empty<LocationObjective>();
  public GameObject ActiveLocationInstance => activeLocationInstance;

  void Start() {
    actions.Add(MessageBus.On("RequestLocationLoad", OnRequestLocationLoad));
    actions.Add(MessageBus.On("LocationUpdated", OnLocationUpdated));

    if (!loadFromTrackerOnStart) return;
    var tracked = LocationEnemyData.NormalizeLocationId(currentLocation);
    if (string.IsNullOrWhiteSpace(tracked) || string.Equals(tracked, "nowhere", StringComparison.OrdinalIgnoreCase)) {
      return;
    }
    var initial = LocationEnemyData.ResolveRequestedOrDefault(tracked);
    LoadLocation(initial, updateTrackerIfDifferent: true);
  }

  void OnDestroy() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  void OnRequestLocationLoad(object payload) {
    var requested = Convert.ToString(payload);
    LoadLocation(requested, updateTrackerIfDifferent: true);
  }

  void OnLocationUpdated(object payload) {
    var requested = Convert.ToString(payload);
    LoadLocation(requested, updateTrackerIfDifferent: false);
  }

  public bool LoadLocation(string requestedLocationId, bool updateTrackerIfDifferent = true) {
    var resolvedId = LocationEnemyData.ResolveRequestedOrDefault(requestedLocationId);
    if (!LocationEnemyData.TryGetLocation(resolvedId, out var info) || info == null) {
      Debug.LogWarning("[LocationManager] Unable to resolve location '" + requestedLocationId + "'.");
      return false;
    }

    if (updateTrackerIfDifferent && !string.Equals(currentLocation, resolvedId, StringComparison.OrdinalIgnoreCase)) {
      UpdateLocation(resolvedId);
      updateTrackerIfDifferent = false;
    }

    var changedLocation = !string.Equals(currentLocationId, resolvedId, StringComparison.OrdinalIgnoreCase);
    currentLocationId = resolvedId;
    activeLocation = info;

    ApplyLocationPrefab(activeLocation, changedLocation);
    MessageBus.Send("LocationLoaded", activeLocation);

    if (logLoads) {
      Debug.Log(
        "[LocationManager] Loaded location id='" + resolvedId +
        "' name='" + activeLocation.name +
        "' enemies=" + activeLocation.enemies.Count +
        " maxEnemies=" + activeLocation.maxEnemies +
        " objectives=" + (activeLocation.objectives != null ? activeLocation.objectives.Count : 0)
      );
    }
    return true;
  }

  void ApplyLocationPrefab(LocationInfo info, bool forceRefresh) {
    if (info == null) {
      ClearLocationInstance();
      MessageBus.Send("LocationLocationChanged", null);
      return;
    }

    var prefabData = info.locationPrefabData;
    var prefab = prefabData != null ? prefabData.ResolvePrefab() : null;

    if (prefab == null) {
      ClearLocationInstance();
      MessageBus.Send("LocationLocationChanged", null);
      return;
    }

    if (!forceRefresh && activeLocationInstance != null) {
      MessageBus.Send("LocationLocationChanged", activeLocationInstance);
      return;
    }

    ClearLocationInstance();
    activeLocationInstance = Instantiate(prefab);
    activeLocationInstance.name = "Location_" + info.id + "_" + prefab.name;

    var parent = locationRoot != null ? locationRoot : transform;
    activeLocationInstance.transform.SetParent(parent, false);
    activeLocationInstance.transform.localPosition = prefabData.localPosition;
    activeLocationInstance.transform.localRotation = Quaternion.Euler(prefabData.localEulerAngles);
    activeLocationInstance.transform.localScale = prefabData.localScale;

    MessageBus.Send("LocationLocationChanged", activeLocationInstance);
  }

  void ClearLocationInstance() {
    if (activeLocationInstance == null) return;
    Destroy(activeLocationInstance);
    activeLocationInstance = null;
  }
}
