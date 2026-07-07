using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyDefeatedEvent {
  public string enemyType;
  public string locationId;
  public GameObject enemyObject;

  public EnemyDefeatedEvent(string enemyType, string locationId, GameObject enemyObject) {
    this.enemyType = NormalizeToken(enemyType);
    this.locationId = NormalizeToken(locationId);
    this.enemyObject = enemyObject;
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }
}

public static class ContentEpisodeProgression {
  const string SaveName = "episode";
  const string EpisodeIdKey = "episodeId";
  const string SliceIdKey = "sliceId";
  const string SliceIndexKey = "sliceIndex";
  const string ObjectiveCountPrefix = "objectiveCount_";

  public static bool HasRuntimeEpisodes() {
    var registry = ActiveContentRegistryRuntime.Registry;
    return registry != null &&
           registry.Episodes != null &&
           registry.Episodes.Count > 0 &&
           registry.Slices != null &&
           registry.Slices.Count > 0;
  }

  public static void ConfigureForGameplayStart(bool isNewGame, string source) {
    if (!HasRuntimeEpisodes()) return;

    if (isNewGame) {
      SaveState(ResolveInitialState(), "new_game:" + (source ?? ""));
      return;
    }

    EnsureSavedState("load_game:" + (source ?? ""));
  }

  public static void ConfigureForCurrentRuntimeState(string source) {
    if (!HasRuntimeEpisodes()) return;
    EnsureSavedState("runtime_state:" + (source ?? ""));
  }

  public static IReadOnlyList<string> GetActivePackIds() {
    var result = new List<string>();
    if (!HasRuntimeEpisodes()) return result;

    var state = ResolveSavedOrInitialState();
    var episode = FindEpisode(state.episodeId);
    if (episode == null || episode.slices == null || episode.slices.Count <= 0) {
      AppendSlicePackIds(state.sliceId, result, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
      return result;
    }

    var maxIndex = Mathf.Clamp(state.sliceIndex, 0, episode.slices.Count - 1);
    for (var i = 0; i <= maxIndex; i++) {
      AppendSlicePackIds(episode.slices[i], result, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    return result;
  }

  public static bool TryAdvanceForEnemyDefeated(EnemyDefeatedEvent defeatedEvent, string source) {
    if (defeatedEvent == null) return false;
    if (!HasRuntimeEpisodes()) return false;

    var state = ResolveSavedOrInitialState();
    if (!TryGetCurrentObjective(state, out var objective)) return false;
    if (!TryParseDefeatObjective(objective.objective, out var targetEnemyType, out var targetCount)) return false;
    if (!string.Equals(NormalizeToken(defeatedEvent.enemyType), targetEnemyType, StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    var data = LoadData();
    var countKey = BuildObjectiveCountKey(objective);
    var nextCount = ReadInt(data, countKey, 0) + 1;
    data[countKey] = nextCount;
    SaveSlotManager.Save(SaveName, data);

    if (nextCount < targetCount) {
      return false;
    }

    return AdvanceToNextSlice("objective:" + (source ?? ""));
  }

  public static bool AdvanceToNextSlice(string source) {
    if (!HasRuntimeEpisodes()) return false;

    var state = ResolveSavedOrInitialState();
    var episode = FindEpisode(state.episodeId);
    if (episode == null || episode.slices == null || episode.slices.Count <= 0) return false;

    for (var i = state.sliceIndex + 1; i < episode.slices.Count; i++) {
      var nextSliceId = NormalizeToken(episode.slices[i]);
      if (string.IsNullOrWhiteSpace(nextSliceId)) continue;
      if (!IsEpisodeProgressSlice(nextSliceId)) continue;

      SaveState(new EpisodeProgressState(episode.id, nextSliceId, i), source);
      return true;
    }

    return false;
  }

  static bool TryGetCurrentObjective(EpisodeProgressState state, out ContentObjectiveDefinition objective) {
    objective = null;

    var currentSlice = FindSlice(state.sliceId);
    if (currentSlice == null || currentSlice.ids == null) return false;

    for (var i = 0; i < currentSlice.ids.Count; i++) {
      var packId = NormalizeToken(currentSlice.ids[i]);
      if (string.IsNullOrWhiteSpace(packId)) continue;
      if (!TryGetObjectiveForPack(packId, out objective)) continue;
      return true;
    }

    return false;
  }

  static bool TryGetObjectiveForPack(string packId, out ContentObjectiveDefinition objective) {
    objective = null;
    var registry = ActiveContentRegistryRuntime.Registry;
    var objectives = registry != null ? registry.Objectives : null;
    if (objectives == null) return false;

    for (var i = 0; i < objectives.Count; i++) {
      var candidate = objectives[i];
      if (candidate == null) continue;
      if (!string.Equals(NormalizeToken(candidate.packId), packId, StringComparison.OrdinalIgnoreCase)) continue;
      objective = candidate;
      return true;
    }

    return false;
  }

  static bool TryParseDefeatObjective(string objective, out string enemyType, out int targetCount) {
    enemyType = "";
    targetCount = 0;

    var normalized = NormalizeToken(objective);
    if (!normalized.StartsWith("Defeat_", StringComparison.OrdinalIgnoreCase)) return false;

    var body = normalized.Substring("Defeat_".Length);
    var lastSeparator = body.LastIndexOf('_');
    if (lastSeparator <= 0 || lastSeparator >= body.Length - 1) return false;

    enemyType = NormalizeToken(body.Substring(0, lastSeparator));
    if (!int.TryParse(body.Substring(lastSeparator + 1), out targetCount)) return false;

    targetCount = Mathf.Max(1, targetCount);
    return !string.IsNullOrWhiteSpace(enemyType);
  }

  static EpisodeProgressState ResolveSavedOrInitialState() {
    var data = LoadData();
    var episodeId = ReadString(data, EpisodeIdKey);
    var sliceId = ReadString(data, SliceIdKey);
    var sliceIndex = ReadInt(data, SliceIndexKey, -1);

    if (TryNormalizeState(episodeId, sliceId, sliceIndex, out var state)) {
      return state;
    }

    return ResolveInitialState();
  }

  static void EnsureSavedState(string source) {
    var data = LoadData();
    var episodeId = ReadString(data, EpisodeIdKey);
    var sliceId = ReadString(data, SliceIdKey);
    var sliceIndex = ReadInt(data, SliceIndexKey, -1);
    if (TryNormalizeState(episodeId, sliceId, sliceIndex, out _)) return;

    SaveState(ResolveInitialState(), source);
  }

  static EpisodeProgressState ResolveInitialState() {
    var registry = ActiveContentRegistryRuntime.Registry;
    var episodes = registry != null ? registry.Episodes : null;
    if (episodes != null) {
      for (var i = 0; i < episodes.Count; i++) {
        var episode = episodes[i];
        if (episode == null || episode.slices == null || episode.slices.Count <= 0) continue;

        var sliceIndex = ResolveInitialSliceIndex(episode);
        var sliceId = NormalizeToken(episode.slices[sliceIndex]);
        return new EpisodeProgressState(episode.id, sliceId, sliceIndex);
      }
    }

    var slice = FindFirstEpisodeProgressSlice();
    return slice != null
      ? new EpisodeProgressState("", slice.id, 0)
      : new EpisodeProgressState("", "", 0);
  }

  static int ResolveInitialSliceIndex(ContentEpisodeDefinition episode) {
    if (episode == null || episode.slices == null || episode.slices.Count <= 0) return 0;

    for (var i = 0; i < episode.slices.Count; i++) {
      if (IsEpisodeProgressSlice(episode.slices[i])) return i;
    }

    return 0;
  }

  static bool TryNormalizeState(
    string episodeId,
    string sliceId,
    int sliceIndex,
    out EpisodeProgressState state
  ) {
    state = default;

    var episode = FindEpisode(episodeId);
    if (episode == null || episode.slices == null || episode.slices.Count <= 0) return false;

    if (sliceIndex >= 0 && sliceIndex < episode.slices.Count) {
      var indexedSliceId = NormalizeToken(episode.slices[sliceIndex]);
      if (string.Equals(indexedSliceId, sliceId, StringComparison.OrdinalIgnoreCase) &&
          FindSlice(indexedSliceId) != null) {
        state = new EpisodeProgressState(episode.id, indexedSliceId, sliceIndex);
        return true;
      }
    }

    for (var i = 0; i < episode.slices.Count; i++) {
      var candidateSliceId = NormalizeToken(episode.slices[i]);
      if (!string.Equals(candidateSliceId, sliceId, StringComparison.OrdinalIgnoreCase)) continue;
      if (FindSlice(candidateSliceId) == null) return false;
      state = new EpisodeProgressState(episode.id, candidateSliceId, i);
      return true;
    }

    return false;
  }

  static void SaveState(EpisodeProgressState state, string source) {
    var data = LoadData();
    data[EpisodeIdKey] = NormalizeToken(state.episodeId);
    data[SliceIdKey] = NormalizeToken(state.sliceId);
    data[SliceIndexKey] = Mathf.Max(0, state.sliceIndex);
    SaveSlotManager.Save(SaveName, data);

    Debug.Log(
      "[ContentEpisodeProgression] state_saved" +
      " source='" + (source ?? "") + "'" +
      " episode='" + NormalizeToken(state.episodeId) + "'" +
      " slice='" + NormalizeToken(state.sliceId) + "'" +
      " index=" + Mathf.Max(0, state.sliceIndex)
    );
  }

  static void AppendSlicePackIds(string sliceId, List<string> target, HashSet<string> stack) {
    if (target == null || stack == null) return;

    var normalizedSliceId = NormalizeToken(sliceId);
    if (string.IsNullOrWhiteSpace(normalizedSliceId)) return;
    if (stack.Contains(normalizedSliceId)) return;

    var slice = FindSlice(normalizedSliceId);
    if (slice == null || slice.ids == null) {
      AddUnique(target, normalizedSliceId);
      return;
    }

    stack.Add(normalizedSliceId);
    for (var i = 0; i < slice.ids.Count; i++) {
      var id = NormalizeToken(slice.ids[i]);
      if (string.IsNullOrWhiteSpace(id)) continue;
      if (FindSlice(id) != null) {
        AppendSlicePackIds(id, target, stack);
        continue;
      }
      AddUnique(target, id);
    }
    stack.Remove(normalizedSliceId);
  }

  static ContentEpisodeDefinition FindEpisode(string episodeId) {
    var normalized = NormalizeToken(episodeId);
    var registry = ActiveContentRegistryRuntime.Registry;
    var episodes = registry != null ? registry.Episodes : null;
    if (episodes == null || episodes.Count <= 0) return null;

    if (string.IsNullOrWhiteSpace(normalized)) {
      return episodes[0];
    }

    for (var i = 0; i < episodes.Count; i++) {
      var candidate = episodes[i];
      if (candidate == null) continue;
      if (!string.Equals(NormalizeToken(candidate.id), normalized, StringComparison.OrdinalIgnoreCase)) continue;
      return candidate;
    }

    return null;
  }

  static ContentSliceDefinition FindSlice(string sliceId) {
    var normalized = NormalizeToken(sliceId);
    if (string.IsNullOrWhiteSpace(normalized)) return null;

    var registry = ActiveContentRegistryRuntime.Registry;
    var slices = registry != null ? registry.Slices : null;
    if (slices == null) return null;

    for (var i = 0; i < slices.Count; i++) {
      var candidate = slices[i];
      if (candidate == null) continue;
      if (!string.Equals(NormalizeToken(candidate.id), normalized, StringComparison.OrdinalIgnoreCase)) continue;
      return candidate;
    }

    return null;
  }

  static ContentSliceDefinition FindFirstEpisodeProgressSlice() {
    var registry = ActiveContentRegistryRuntime.Registry;
    var slices = registry != null ? registry.Slices : null;
    if (slices == null) return null;

    for (var i = 0; i < slices.Count; i++) {
      var slice = slices[i];
      if (slice == null) continue;
      if (IsEpisodeProgressSlice(slice.id)) return slice;
    }

    return null;
  }

  static bool IsEpisodeProgressSlice(string sliceId) {
    var normalized = NormalizeToken(sliceId);
    if (!normalized.StartsWith("Episode", StringComparison.OrdinalIgnoreCase)) return false;
    return normalized.IndexOf('_') > "Episode".Length;
  }

  static string BuildObjectiveCountKey(ContentObjectiveDefinition objective) {
    var key = !string.IsNullOrWhiteSpace(objective.id) ? objective.id : objective.packId;
    return ObjectiveCountPrefix + NormalizeToken(key).Replace(' ', '_');
  }

  static SaveData LoadData() {
    return SaveSlotManager.Load(SaveName) ?? new SaveData();
  }

  static string ReadString(SaveData data, string key) {
    if (data == null || string.IsNullOrWhiteSpace(key)) return "";
    if (!data.TryGetValue(key, out var value) || value == null) return "";
    return Convert.ToString(value);
  }

  static int ReadInt(SaveData data, string key, int fallback) {
    if (data == null || string.IsNullOrWhiteSpace(key)) return fallback;
    if (!data.TryGetValue(key, out var value) || value == null) return fallback;

    try {
      return Convert.ToInt32(value);
    }
    catch {
      return fallback;
    }
  }

  static void AddUnique(List<string> target, string value) {
    var normalized = NormalizeToken(value);
    if (string.IsNullOrWhiteSpace(normalized)) return;

    for (var i = 0; i < target.Count; i++) {
      if (string.Equals(target[i], normalized, StringComparison.OrdinalIgnoreCase)) return;
    }

    target.Add(normalized);
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  readonly struct EpisodeProgressState {
    public readonly string episodeId;
    public readonly string sliceId;
    public readonly int sliceIndex;

    public EpisodeProgressState(string episodeId, string sliceId, int sliceIndex) {
      this.episodeId = ContentEpisodeProgression.NormalizeToken(episodeId);
      this.sliceId = ContentEpisodeProgression.NormalizeToken(sliceId);
      this.sliceIndex = Mathf.Max(0, sliceIndex);
    }
  }
}
