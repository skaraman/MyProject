using System;
using UnityEngine;

public sealed class GameplayZoneInfoView : MonoBehaviour {
  [SerializeField] FontText zoneText;
  [SerializeField] FontText episodeText;

  string displayedLocation = "";
  string displayedEpisode = "";

  void OnEnable() {
    Refresh();
  }

  void Update() {
    var location = ResolveLocation();
    var episode = ResolveEpisode();

    if (string.Equals(displayedLocation, location, StringComparison.Ordinal) &&
        string.Equals(displayedEpisode, episode, StringComparison.Ordinal)) {
      return;
    }

    Refresh();
  }

  void Refresh() {
    displayedLocation = ResolveLocation();
    displayedEpisode = ResolveEpisode();

    ApplyText(zoneText, displayedLocation);
    ApplyText(episodeText, FormatEpisode(displayedEpisode));
  }

  static string ResolveLocation() {
    var location = LocationEnemyData.NormalizeLocationId(LocationManager.currentLocation);
    if (!string.IsNullOrWhiteSpace(location) &&
        !string.Equals(location, "nowhere", StringComparison.OrdinalIgnoreCase)) {
      return location;
    }

    return LocationEnemyData.GetDefaultLocation();
  }

  static string ResolveEpisode() {
    var episode = ContentEpisodeProgression.ResolveCurrentEpisodeId();
    if (!string.IsNullOrWhiteSpace(episode)) {
      return episode.Trim();
    }

    return SaveSlotManager.DefaultEpisodeId;
  }

  static string FormatEpisode(string episode) {
    if (string.IsNullOrWhiteSpace(episode)) return "";
    if (!episode.StartsWith("Episode", StringComparison.OrdinalIgnoreCase)) {
      return episode;
    }

    var number = episode.Substring("Episode".Length).Trim();
    return "episode " + number;
  }

  static void ApplyText(FontText text, string value) {
    if (text == null) return;
    if (string.Equals(text.content, value, StringComparison.Ordinal)) return;

    text.content = value;
    text.Generate();
  }
}
