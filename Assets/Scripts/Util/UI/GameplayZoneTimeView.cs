using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(FontText))]
public sealed class GameplayZoneTimeView : MonoBehaviour {
  FontText text;
  DayNightCycle2D cycle;
  string displayedValue;
  string displayedLocation;
  int displayedHour = -1;
  int displayedMinute = -1;

  void Awake() {
    text = GetComponent<FontText>();
  }

  void OnEnable() {
    displayedValue = null;
    Refresh();
  }

  void Update() {
    Refresh();
  }

  void Refresh() {
    if (text == null) {
      text = GetComponent<FontText>();
    }

    var location = ResolveGameplayLocation();
    if (string.IsNullOrEmpty(location) || !SingleSceneManager.IsBlackscreenFullyTransparent) {
      ApplyText("");
      return;
    }

    if (cycle == null) {
      cycle = DayNightCycle2D.Instance;
      if (cycle == null) {
        cycle = FindAnyObjectByType<DayNightCycle2D>();
      }
    }
    if (cycle == null) {
      ApplyText(location);
      return;
    }

    if (!string.IsNullOrEmpty(displayedValue) &&
        string.Equals(displayedLocation, location, StringComparison.Ordinal) &&
        displayedHour == cycle.Hour &&
        displayedMinute == cycle.Minute) {
      return;
    }

    displayedLocation = location;
    displayedHour = cycle.Hour;
    displayedMinute = cycle.Minute;
    ApplyText($"{location}  {cycle.GetFormattedTime()}  {cycle.GetPhaseName()}");
  }

  static string ResolveGameplayLocation() {
    var location = LocationEnemyData.NormalizeLocationId(LocationManager.currentLocation);
    if (string.IsNullOrWhiteSpace(location) ||
        string.Equals(location, "nowhere", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(location, LocationEnemyData.MainMenuLocationId, StringComparison.OrdinalIgnoreCase)) {
      return "";
    }

    return location;
  }

  void ApplyText(string value) {
    if (string.Equals(displayedValue, value, StringComparison.Ordinal)) return;

    displayedValue = value;
    text.content = value;
    text.Generate();
  }
}
