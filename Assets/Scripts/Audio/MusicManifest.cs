using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class MusicManifestData {
  public List<ZoneMusicManifestEntry> zones = new();
  public List<EpisodeMusicManifestEntry> episodes = new();
}

[Serializable]
public sealed class ZoneMusicManifestEntry {
  public string zone;
  public List<string> tracks = new();
  public bool playOnAwake;
  public bool loop;
}

[Serializable]
public sealed class EpisodeMusicManifestEntry {
  public string episode;
  public List<string> tracks = new();
  public bool loop;
}

public sealed class MusicPlaylistDefinition {
  public string zoneId;
  public string[] tracks;
  public bool playOnAwake;
  public bool loop;
}

public static class MusicManifestCatalog {
  public const string AssetPath = "Assets/Music/MusicManifest.json";

  public static bool TryBuildPlaylists(
    TextAsset manifestAsset,
    out Dictionary<string, MusicPlaylistDefinition> playlists
  ) {
    return TryBuildPlaylists(manifestAsset, out playlists, out _);
  }

  public static bool TryBuildPlaylists(
    TextAsset manifestAsset,
    out Dictionary<string, MusicPlaylistDefinition> playlists,
    out Dictionary<string, MusicPlaylistDefinition> episodePlaylists
  ) {
    playlists = new Dictionary<string, MusicPlaylistDefinition>(StringComparer.OrdinalIgnoreCase);
    episodePlaylists = new Dictionary<string, MusicPlaylistDefinition>(
      StringComparer.OrdinalIgnoreCase
    );
    if (manifestAsset == null) {
      Debug.LogError("[MusicManifest] Manifest asset is missing.");
      return false;
    }

    MusicManifestData manifest;
    try {
      manifest = JsonUtility.FromJson<MusicManifestData>(manifestAsset.text);
    }
    catch (Exception exception) {
      Debug.LogError("[MusicManifest] Invalid JSON. error='" + exception.Message + "'");
      return false;
    }

    if (manifest == null || manifest.zones == null || manifest.episodes == null) {
      Debug.LogError("[MusicManifest] The zones array is missing.");
      return false;
    }

    var hasPlayOnAwakeEntry = false;
    for (var i = 0; i < manifest.zones.Count; i++) {
      var entry = manifest.zones[i];
      if (entry == null || string.IsNullOrWhiteSpace(entry.zone)) {
        Debug.LogError("[MusicManifest] Zone entry " + i + " has no zone id.");
        return false;
      }

      var zoneId = entry.zone.Trim();
      if (playlists.ContainsKey(zoneId)) {
        Debug.LogError("[MusicManifest] Duplicate zone id '" + zoneId + "'.");
        return false;
      }

      var tracks = NormalizeTracks(entry.tracks);
      if (entry.playOnAwake && hasPlayOnAwakeEntry) {
        Debug.LogError("[MusicManifest] Only one zone can use playOnAwake.");
        return false;
      }

      if (entry.playOnAwake && tracks.Length == 0) {
        Debug.LogError("[MusicManifest] playOnAwake zone '" + zoneId + "' has no tracks.");
        return false;
      }

      hasPlayOnAwakeEntry |= entry.playOnAwake;
      playlists.Add(zoneId, new MusicPlaylistDefinition {
        zoneId = zoneId,
        tracks = tracks,
        playOnAwake = entry.playOnAwake,
        loop = entry.loop
      });
    }

    for (var i = 0; i < manifest.episodes.Count; i++) {
      var entry = manifest.episodes[i];
      if (entry == null || string.IsNullOrWhiteSpace(entry.episode)) {
        Debug.LogError("[MusicManifest] Episode entry " + i + " has no episode id.");
        return false;
      }

      var episodeId = entry.episode.Trim();
      if (episodePlaylists.ContainsKey(episodeId)) {
        Debug.LogError("[MusicManifest] Duplicate episode id '" + episodeId + "'.");
        return false;
      }

      var tracks = NormalizeTracks(entry.tracks);
      if (tracks.Length == 0) {
        Debug.LogError("[MusicManifest] Episode '" + episodeId + "' has no tracks.");
        return false;
      }

      episodePlaylists.Add(episodeId, new MusicPlaylistDefinition {
        zoneId = episodeId,
        tracks = tracks,
        playOnAwake = false,
        loop = entry.loop
      });
    }

    return true;
  }

  static string[] NormalizeTracks(List<string> tracks) {
    if (tracks == null || tracks.Count == 0) {
      return Array.Empty<string>();
    }

    var normalized = new List<string>(tracks.Count);
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < tracks.Count; i++) {
      var track = tracks[i];
      if (string.IsNullOrWhiteSpace(track)) {
        continue;
      }

      var address = track.Replace("\\", "/").Trim();
      if (seen.Add(address)) {
        normalized.Add(address);
      }
    }

    return normalized.ToArray();
  }
}
