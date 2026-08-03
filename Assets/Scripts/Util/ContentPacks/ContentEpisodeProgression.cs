using System;
using System.Collections.Generic;
using System.Globalization;
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
  struct RuntimeObjective {
    public string action;
    public string subject;
    public int requiredCount;
    public string countKey;
    public int currentCount;
  }

  public const string ObjectivesCompletedTopic = "episode.objectives.completed";

  const string SaveName = "episode";
  const string EpisodeIdKey = "episodeId";
  const string SliceIdKey = "sliceId";
  const string SliceIndexKey = "sliceIndex";
  const string CompletedPartCountKey = "completedPartCount";
  const string CompletedPartPrefix = "completedPart_";
  const string ObjectiveCountPrefix = "objectiveCount_";

  static int cachedCompletedPartSlot = -1;
  static int cachedCompletedPartCount = -1;
  static int episodeRevision;
  static SaveData cachedData;
  static int cachedDataSlot = -1;
  static bool savePending;
  static readonly List<RuntimeObjective> runtimeObjectives = new(16);
  static readonly List<ContentObjectiveDefinition> runtimeObjectiveDefinitions = new(16);
  static readonly List<ContentObjectiveDefinition> incompleteRuntimeObjectiveDefinitions = new(16);
  static string runtimeObjectiveEpisodeId = "";
  static int runtimeObjectiveRegistryVersion = -1;
  static bool runtimeObjectiveCountsDirty;
  static bool runtimeObjectiveHasSpawnRules;

  public static int EpisodeRevision => episodeRevision;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetRuntimeCaches() {
    cachedCompletedPartSlot = -1;
    cachedCompletedPartCount = -1;
    episodeRevision = 0;
    cachedData = null;
    cachedDataSlot = -1;
    savePending = false;
    runtimeObjectiveCountsDirty = false;
    ClearRuntimeObjectiveCache();
  }

  public static void PrepareRuntimeCaches() {
    if (!HasRuntimeEpisodes()) {
      WriteRuntimeObjectiveCountsToData();
      ClearRuntimeObjectiveCache();
      return;
    }

    var state = ResolveSavedOrInitialState();
    EnsureRuntimeObjectiveCache(state);
  }

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
      ResetSavedState(ResolveInitialState(), "new_game:" + (source ?? ""));
      return;
    }

    EnsureSavedState("load_game:" + (source ?? ""));
    RestartIncompleteCurrentEpisodePart("load_game:" + (source ?? ""));
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

  public static string ResolveCurrentEpisodeId() {
    if (!HasRuntimeEpisodes()) return "";

    var state = ResolveSavedOrInitialState();
    return NormalizeToken(state.episodeId);
  }

  public static IReadOnlyList<ContentObjectiveDefinition> ResolveCurrentObjectives() {
    if (!HasRuntimeEpisodes()) {
      WriteRuntimeObjectiveCountsToData();
      ClearRuntimeObjectiveCache();
      return Array.Empty<ContentObjectiveDefinition>();
    }

    var state = ResolveSavedOrInitialState();
    EnsureRuntimeObjectiveCache(state);
    RefreshIncompleteRuntimeObjectiveDefinitions();
    return incompleteRuntimeObjectiveDefinitions;
  }

  public static bool HasCurrentObjectives() {
    PrepareRuntimeCaches();
    return runtimeObjectives.Count > 0;
  }

  public static bool HasIncompleteCurrentObjectives() {
    if (!HasRuntimeEpisodes()) return false;

    var state = ResolveSavedOrInitialState();
    EnsureRuntimeObjectiveCache(state);
    if (runtimeObjectives.Count <= 0) return false;

    return !AreObjectivesComplete(runtimeObjectives);
  }

  public static IReadOnlyList<ContentObjectiveDefinition> ResolveCurrentEpisodeObjectives() {
    return ResolveCurrentEpisodeObjectiveDefinitions();
  }

  public static bool IsCurrentEpisodeObjectiveComplete(ContentObjectiveDefinition definition) {
    if (definition == null) return false;

    var definitions = ResolveCurrentEpisodeObjectiveDefinitions();
    var count = Mathf.Min(definitions.Count, runtimeObjectives.Count);
    for (var i = 0; i < count; i++) {
      if (!ReferenceEquals(definitions[i], definition)) continue;
      return runtimeObjectives[i].currentCount >= runtimeObjectives[i].requiredCount;
    }

    return false;
  }

  public static bool HasCurrentEpisodeSpawnRules() {
    ResolveCurrentEpisodeObjectiveDefinitions();
    return runtimeObjectiveHasSpawnRules;
  }

  public static int CollectCurrentEpisodeSpawnEnemyTypes(List<string> output) {
    if (output == null) return 0;

    output.Clear();
    var objectives = ResolveCurrentEpisodeObjectiveDefinitions();
    for (var objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++) {
      var objective = objectives[objectiveIndex];
      if (objective?.spawns == null) continue;

      for (var ruleIndex = 0; ruleIndex < objective.spawns.Count; ruleIndex++) {
        if (!TryParseEnemyRule(objective.spawns[ruleIndex], out var enemyType, out _)) continue;
        AddUniqueEnemyType(output, enemyType);
      }
    }

    return output.Count;
  }

  public static bool TryResolveCurrentEpisodeSpawnCount(string enemyType, out int spawnCount) {
    return TryResolveCurrentEpisodeEnemyRule(
      enemyType,
      useRespawnRules: false,
      out spawnCount
    );
  }

  public static bool TryResolveCurrentEpisodeRespawnSeconds(string enemyType, out int respawnSeconds) {
    return TryResolveCurrentEpisodeEnemyRule(
      enemyType,
      useRespawnRules: true,
      out respawnSeconds
    );
  }

  public static bool TryResolveCurrentEpisodeEnemyStatMultiplier(
    string enemyType,
    out float statMultiplier
  ) {
    statMultiplier = 1f;
    var normalizedEnemyType = NormalizeToken(enemyType);
    if (string.IsNullOrWhiteSpace(normalizedEnemyType)) return false;

    var objectives = ResolveCurrentEpisodeObjectiveDefinitions();
    var found = false;
    for (var objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++) {
      var rules = objectives[objectiveIndex]?.enemyLevels;
      if (rules == null) continue;

      for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++) {
        if (!TryParseEnemyMultiplierRule(
              rules[ruleIndex],
              out var subject,
              out var ruleMultiplier
            )) {
          continue;
        }
        if (!string.Equals(normalizedEnemyType, subject, StringComparison.OrdinalIgnoreCase)) continue;

        statMultiplier = found
          ? Mathf.Max(statMultiplier, ruleMultiplier)
          : ruleMultiplier;
        found = true;
      }
    }

    return found;
  }

  public static int ResolveCompletedPartCount() {
    if (cachedCompletedPartSlot == SaveSlotManager.slot && cachedCompletedPartCount >= 0) {
      return cachedCompletedPartCount;
    }

    var data = LoadData();
    var completedPartCount = ReadInt(data, CompletedPartCountKey, -1);
    if (completedPartCount < 0 && HasRuntimeEpisodes()) {
      completedPartCount = InferCompletedPartCount(ResolveSavedOrInitialState());
    }

    cachedCompletedPartSlot = SaveSlotManager.slot;
    cachedCompletedPartCount = Mathf.Max(0, completedPartCount);
    return cachedCompletedPartCount;
  }

  public static bool TryAdvanceForEnemyDefeated(EnemyDefeatedEvent defeatedEvent, string source) {
    if (defeatedEvent == null) return false;

    return TryAdvanceForObjectiveEvent(
      "Kill",
      defeatedEvent.enemyType,
      source
    );
  }

  public static bool TryAdvanceForObjectiveEvent(
    string action,
    string subject,
    string source
  ) {
    if (!HasRuntimeEpisodes()) return false;

    var state = ResolveSavedOrInitialState();
    EnsureRuntimeObjectiveCache(state);
    if (runtimeObjectives.Count <= 0) return false;

    var changed = ApplyObjectiveEvent(
      runtimeObjectives,
      action,
      subject
    );
    if (!changed) return false;

    QueueRuntimeObjectiveSave();

    if (!AreObjectivesComplete(runtimeObjectives)) {
      MarkEpisodeChanged();
      return false;
    }

    return AdvanceToNextSlice("objective:" + (source ?? ""));
  }

  public static bool AdvanceToNextSlice(string source) {
    if (!HasRuntimeEpisodes()) return false;

    var state = ResolveSavedOrInitialState();
    var episode = FindEpisode(state.episodeId);
    if (episode == null || episode.slices == null || episode.slices.Count <= 0) return false;
    if (!IsEpisodeProgressSlice(state.sliceId)) return false;

    var data = LoadData();
    if (!MarkPartCompleted(data, state)) {
      return false;
    }

    if (TryResolveNextProgressState(state, episode, out var nextState)) {
      SaveState(data, nextState, source);
      return true;
    }

    SaveState(data, state, source);
    return true;
  }

  static bool TryResolveNextProgressState(
    EpisodeProgressState state,
    ContentEpisodeDefinition episode,
    out EpisodeProgressState nextState
  ) {
    nextState = default;

    for (var i = state.sliceIndex + 1; i < episode.slices.Count; i++) {
      var nextSliceId = NormalizeToken(episode.slices[i]);
      if (string.IsNullOrWhiteSpace(nextSliceId)) continue;
      if (!IsEpisodeProgressSlice(nextSliceId)) continue;

      nextState = new EpisodeProgressState(episode.id, nextSliceId, i);
      return true;
    }

    var nextEpisode = FindNextEpisode(episode);
    if (nextEpisode == null || nextEpisode.slices == null || nextEpisode.slices.Count <= 0) {
      return false;
    }

    var nextSliceIndex = ResolveInitialSliceIndex(nextEpisode);
    var firstSliceId = NormalizeToken(nextEpisode.slices[nextSliceIndex]);
    if (string.IsNullOrWhiteSpace(firstSliceId)) {
      return false;
    }
    if (!IsEpisodeProgressSlice(firstSliceId)) {
      return false;
    }

    nextState = new EpisodeProgressState(nextEpisode.id, firstSliceId, nextSliceIndex);
    return true;
  }

  static bool MarkPartCompleted(SaveData data, EpisodeProgressState state) {
    if (data == null) {
      return false;
    }

    var completionKey = BuildCompletedPartKey(state);
    if (ReadBool(data, completionKey, false)) {
      return false;
    }

    var completedPartCount = Mathf.Max(0, ReadInt(data, CompletedPartCountKey, 0));
    completedPartCount += 1;

    data[completionKey] = true;
    data[CompletedPartCountKey] = completedPartCount;
    cachedCompletedPartSlot = SaveSlotManager.slot;
    cachedCompletedPartCount = completedPartCount;
    return true;
  }

  static void EnsureRuntimeObjectiveCache(EpisodeProgressState state) {
    var normalizedEpisodeId = NormalizeToken(state.episodeId);
    var registryVersion = ActiveContentRegistryRuntime.ReloadVersion;
    if (runtimeObjectiveRegistryVersion == registryVersion &&
        string.Equals(
          runtimeObjectiveEpisodeId,
          normalizedEpisodeId,
          StringComparison.OrdinalIgnoreCase
        )) {
      return;
    }

    WriteRuntimeObjectiveCountsToData();
    ClearRuntimeObjectiveCache();
    runtimeObjectiveEpisodeId = normalizedEpisodeId;
    runtimeObjectiveRegistryVersion = registryVersion;
    if (string.IsNullOrWhiteSpace(normalizedEpisodeId)) return;

    var registry = ActiveContentRegistryRuntime.Registry;
    var objectives = registry != null ? registry.Objectives : null;
    if (objectives == null) return;
    var data = LoadData();

    for (var i = 0; i < objectives.Count; i++) {
      var candidate = objectives[i];
      if (candidate == null) continue;
      if (!string.Equals(
        NormalizeToken(candidate.id),
        normalizedEpisodeId,
        StringComparison.OrdinalIgnoreCase
      )) continue;

      if (!TryParseObjectiveKey(
            candidate.objective,
            out var action,
            out var subject,
            out var requiredCount
          )) {
        continue;
      }

      var countKey = BuildObjectiveCountKey(candidate);
      runtimeObjectiveDefinitions.Add(candidate);
      if (!runtimeObjectiveHasSpawnRules && HasPositiveSpawnRule(candidate.spawns)) {
        runtimeObjectiveHasSpawnRules = true;
      }
      runtimeObjectives.Add(new RuntimeObjective {
        action = action,
        subject = subject,
        requiredCount = requiredCount,
        countKey = countKey,
        currentCount = ReadInt(data, countKey, 0)
      });
    }
  }

  static void ClearRuntimeObjectiveCache() {
    runtimeObjectives.Clear();
    runtimeObjectiveDefinitions.Clear();
    incompleteRuntimeObjectiveDefinitions.Clear();
    runtimeObjectiveEpisodeId = "";
    runtimeObjectiveRegistryVersion = -1;
    runtimeObjectiveHasSpawnRules = false;
  }

  static bool HasPositiveSpawnRule(IReadOnlyList<string> rules) {
    if (rules == null) return false;
    for (var i = 0; i < rules.Count; i++) {
      if (TryParseEnemyRule(rules[i], out _, out var spawnCount) && spawnCount > 0) {
        return true;
      }
    }
    return false;
  }

  static void RefreshIncompleteRuntimeObjectiveDefinitions() {
    incompleteRuntimeObjectiveDefinitions.Clear();

    var count = Mathf.Min(runtimeObjectives.Count, runtimeObjectiveDefinitions.Count);
    for (var i = 0; i < count; i++) {
      var objective = runtimeObjectives[i];
      if (objective.currentCount >= objective.requiredCount) continue;

      var definition = runtimeObjectiveDefinitions[i];
      if (definition != null) {
        incompleteRuntimeObjectiveDefinitions.Add(definition);
      }
    }
  }

  static bool ApplyObjectiveEvent(
    List<RuntimeObjective> objectives,
    string action,
    string subject
  ) {
    if (objectives == null) return false;

    var changed = false;
    var normalizedAction = NormalizeToken(action);
    var normalizedSubject = NormalizeToken(subject);

    for (var i = 0; i < objectives.Count; i++) {
      var objective = objectives[i];
      if (!string.Equals(normalizedAction, objective.action, StringComparison.OrdinalIgnoreCase)) continue;
      if (!string.Equals(normalizedSubject, objective.subject, StringComparison.OrdinalIgnoreCase)) continue;

      if (objective.currentCount >= objective.requiredCount) continue;

      objective.currentCount += 1;
      objectives[i] = objective;
      changed = true;
    }

    return changed;
  }

  static IReadOnlyList<ContentObjectiveDefinition> ResolveCurrentEpisodeObjectiveDefinitions() {
    if (!HasRuntimeEpisodes()) {
      WriteRuntimeObjectiveCountsToData();
      ClearRuntimeObjectiveCache();
      return Array.Empty<ContentObjectiveDefinition>();
    }

    var state = ResolveSavedOrInitialState();
    EnsureRuntimeObjectiveCache(state);
    return runtimeObjectiveDefinitions;
  }

  static bool TryResolveCurrentEpisodeEnemyRule(
    string enemyType,
    bool useRespawnRules,
    out int value
  ) {
    value = 0;
    var normalizedEnemyType = NormalizeToken(enemyType);
    if (string.IsNullOrWhiteSpace(normalizedEnemyType)) return false;

    var objectives = ResolveCurrentEpisodeObjectiveDefinitions();
    var found = false;
    for (var objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++) {
      var objective = objectives[objectiveIndex];
      if (objective == null) continue;

      var rules = useRespawnRules ? objective.respawns : objective.spawns;
      if (rules == null) continue;

      for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++) {
        if (!TryParseEnemyRule(rules[ruleIndex], out var subject, out var ruleValue)) continue;
        if (!string.Equals(normalizedEnemyType, subject, StringComparison.OrdinalIgnoreCase)) continue;

        if (!found) {
          value = ruleValue;
          found = true;
          continue;
        }

        value = useRespawnRules
          ? Mathf.Min(value, ruleValue)
          : Mathf.Max(value, ruleValue);
      }
    }

    return found;
  }

  static bool TryParseEnemyRule(string rule, out string subject, out int value) {
    subject = "";
    value = 0;

    var normalized = NormalizeToken(rule);
    var separatorIndex = normalized.LastIndexOf('_');
    if (separatorIndex <= 0 || separatorIndex >= normalized.Length - 1) return false;

    subject = NormalizeToken(normalized.Substring(0, separatorIndex));
    if (string.IsNullOrWhiteSpace(subject)) return false;
    if (!int.TryParse(
          normalized.Substring(separatorIndex + 1),
          NumberStyles.Integer,
          CultureInfo.InvariantCulture,
          out value
        )) {
      return false;
    }
    return value >= 0;
  }

  static bool TryParseEnemyMultiplierRule(
    string rule,
    out string subject,
    out float multiplier
  ) {
    subject = "";
    multiplier = 1f;

    var normalized = NormalizeToken(rule);
    var separatorIndex = normalized.LastIndexOf('_');
    if (separatorIndex <= 0 || separatorIndex >= normalized.Length - 1) return false;

    subject = NormalizeToken(normalized.Substring(0, separatorIndex));
    if (string.IsNullOrWhiteSpace(subject)) return false;
    if (!float.TryParse(
          normalized.Substring(separatorIndex + 1),
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out multiplier
        )) {
      return false;
    }
    return multiplier > 0f && !float.IsInfinity(multiplier) && !float.IsNaN(multiplier);
  }

  static void AddUniqueEnemyType(List<string> output, string enemyType) {
    if (output == null) return;

    var normalizedEnemyType = NormalizeToken(enemyType);
    if (string.IsNullOrWhiteSpace(normalizedEnemyType)) return;

    for (var i = 0; i < output.Count; i++) {
      if (string.Equals(output[i], normalizedEnemyType, StringComparison.OrdinalIgnoreCase)) {
        return;
      }
    }

    output.Add(normalizedEnemyType);
  }

  static bool AreObjectivesComplete(
    IReadOnlyList<RuntimeObjective> objectives
  ) {
    if (objectives == null || objectives.Count <= 0) return false;

    for (var i = 0; i < objectives.Count; i++) {
      var objective = objectives[i];
      if (objective.currentCount < objective.requiredCount) return false;
    }

    return true;
  }

  static bool TryParseObjectiveKey(
    string objective,
    out string action,
    out string subject,
    out int targetCount
  ) {
    action = "";
    subject = "";
    targetCount = 0;

    var normalized = NormalizeToken(objective);
    var parts = normalized.Split('_');
    if (parts.Length != 3) return false;

    action = NormalizeToken(parts[0]);
    subject = NormalizeToken(parts[1]);
    if (!int.TryParse(parts[2], out targetCount)) return false;
    if (targetCount <= 0) return false;

    return !string.IsNullOrWhiteSpace(action) &&
           !string.IsNullOrWhiteSpace(subject);
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
    if (TryNormalizeState(episodeId, sliceId, sliceIndex, out var state)) {
      EnsureCompletedPartCount(data, state, source);
      return;
    }

    SaveState(ResolveInitialState(), source);
  }

  static void EnsureCompletedPartCount(SaveData data, EpisodeProgressState state, string source) {
    if (data.ContainsKey(CompletedPartCountKey)) {
      cachedCompletedPartSlot = SaveSlotManager.slot;
      cachedCompletedPartCount = Mathf.Max(0, ReadInt(data, CompletedPartCountKey, 0));
      return;
    }

    data[CompletedPartCountKey] = InferCompletedPartCount(state);
    SaveState(data, state, "completion_migration:" + (source ?? ""));
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

  static void ResetSavedState(EpisodeProgressState state, string source) {
    var data = new SaveData();
    data[CompletedPartCountKey] = 0;
    SaveState(data, state, source);
  }

  static void RestartIncompleteCurrentEpisodePart(string source) {
    var state = ResolveSavedOrInitialState();
    EnsureRuntimeObjectiveCache(state);
    if (runtimeObjectives.Count <= 0) return;

    if (AreObjectivesComplete(runtimeObjectives)) return;

    var changed = false;
    for (var i = 0; i < runtimeObjectives.Count; i++) {
      var objective = runtimeObjectives[i];
      if (objective.currentCount <= 0) continue;
      objective.currentCount = 0;
      runtimeObjectives[i] = objective;
      changed = true;
    }

    if (!changed) return;
    QueueRuntimeObjectiveSave();

    RuntimeLog.Log(
      "[ContentEpisodeProgression] episode_part_restarted" +
      " source='" + (source ?? "") + "'" +
      " episode='" + NormalizeToken(state.episodeId) + "'" +
      " slice='" + NormalizeToken(state.sliceId) + "'"
    );
  }

  static void SaveState(EpisodeProgressState state, string source) {
    var data = LoadData();
    SaveState(data, state, source);
  }

  static void SaveState(SaveData data, EpisodeProgressState state, string source) {
    if (data == null) {
      data = new SaveData();
    }

    data[EpisodeIdKey] = NormalizeToken(state.episodeId);
    data[SliceIdKey] = NormalizeToken(state.sliceId);
    data[SliceIndexKey] = Mathf.Max(0, state.sliceIndex);
    if (!data.ContainsKey(CompletedPartCountKey)) {
      data[CompletedPartCountKey] = 0;
    }
    QueueSave(data);
    MarkEpisodeChanged();

    cachedCompletedPartSlot = SaveSlotManager.slot;
    cachedCompletedPartCount = Mathf.Max(0, ReadInt(data, CompletedPartCountKey, 0));

    RuntimeLog.Log(
      "[ContentEpisodeProgression] state_staged" +
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

  static ContentEpisodeDefinition FindNextEpisode(ContentEpisodeDefinition currentEpisode) {
    if (currentEpisode == null) {
      return null;
    }

    var registry = ActiveContentRegistryRuntime.Registry;
    var episodes = registry != null ? registry.Episodes : null;
    if (episodes == null || episodes.Count <= 0) {
      return null;
    }

    for (var i = 0; i < episodes.Count - 1; i++) {
      var candidate = episodes[i];
      if (candidate == null) continue;
      if (!string.Equals(
        NormalizeToken(candidate.id),
        NormalizeToken(currentEpisode.id),
        StringComparison.OrdinalIgnoreCase
      )) continue;

      return episodes[i + 1];
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
    var episodeId = NormalizeToken(objective.id).Replace(' ', '_');
    var objectiveKey = NormalizeToken(objective.objective).Replace(' ', '_');
    return ObjectiveCountPrefix + episodeId + "_" + objectiveKey;
  }

  static string BuildCompletedPartKey(EpisodeProgressState state) {
    return CompletedPartPrefix + NormalizeToken(state.episodeId) + "_" + NormalizeToken(state.sliceId);
  }

  static int InferCompletedPartCount(EpisodeProgressState state) {
    var completedPartCount = 0;
    var registry = ActiveContentRegistryRuntime.Registry;
    var episodes = registry != null ? registry.Episodes : null;
    if (episodes == null) {
      return completedPartCount;
    }

    for (var i = 0; i < episodes.Count; i++) {
      var episode = episodes[i];
      if (episode == null || episode.slices == null) continue;

      var isCurrentEpisode = string.Equals(
        NormalizeToken(episode.id),
        NormalizeToken(state.episodeId),
        StringComparison.OrdinalIgnoreCase
      );

      for (var sliceIndex = 0; sliceIndex < episode.slices.Count; sliceIndex++) {
        if (!IsEpisodeProgressSlice(episode.slices[sliceIndex])) continue;
        if (isCurrentEpisode && sliceIndex >= state.sliceIndex) continue;
        completedPartCount += 1;
      }

      if (isCurrentEpisode) {
        break;
      }
    }

    return completedPartCount;
  }

  static SaveData LoadData() {
    var currentSlot = SaveSlotManager.slot;
    if (cachedData != null && cachedDataSlot == currentSlot) {
      return cachedData;
    }

    runtimeObjectiveCountsDirty = false;
    ClearRuntimeObjectiveCache();
    cachedData = SaveSlotManager.Load(SaveName) ?? new SaveData();
    cachedDataSlot = currentSlot;
    savePending = false;
    return cachedData;
  }

  static void QueueSave(SaveData data) {
    if (cachedData != null && !ReferenceEquals(cachedData, data)) {
      runtimeObjectiveCountsDirty = false;
      ClearRuntimeObjectiveCache();
    }
    cachedData = data ?? new SaveData();
    cachedDataSlot = SaveSlotManager.slot;
    savePending = true;
  }

  static void QueueRuntimeObjectiveSave() {
    runtimeObjectiveCountsDirty = true;
    savePending = true;
  }

  static void WriteRuntimeObjectiveCountsToData() {
    if (!runtimeObjectiveCountsDirty || cachedData == null) {
      return;
    }

    for (var i = 0; i < runtimeObjectives.Count; i++) {
      var objective = runtimeObjectives[i];
      if (objective.currentCount <= 0) {
        cachedData.Remove(objective.countKey);
        continue;
      }
      cachedData[objective.countKey] = objective.currentCount;
    }
    runtimeObjectiveCountsDirty = false;
  }

  public static bool FlushPendingSave() {
    if (!savePending || cachedData == null) {
      return true;
    }
    if (cachedDataSlot != SaveSlotManager.slot) {
      return false;
    }

    try {
      WriteRuntimeObjectiveCountsToData();
      SaveSlotManager.Save(SaveName, cachedData);
      savePending = false;
      return true;
    }
    catch (Exception exception) {
      Debug.LogWarning(
        "[ContentEpisodeProgression] Failed to flush pending state: " +
        exception.Message
      );
      return false;
    }
  }

  public static void DiscardRuntimeCacheForSlot(int slotNumber) {
    var cacheChanged = false;
    if (cachedDataSlot == slotNumber) {
      cachedData = null;
      cachedDataSlot = -1;
      savePending = false;
      runtimeObjectiveCountsDirty = false;
      ClearRuntimeObjectiveCache();
      cacheChanged = true;
    }
    if (cachedCompletedPartSlot == slotNumber) {
      cachedCompletedPartSlot = -1;
      cachedCompletedPartCount = -1;
      cacheChanged = true;
    }
    if (cacheChanged) {
      MarkEpisodeChanged();
    }
  }

  static void MarkEpisodeChanged() {
    unchecked {
      episodeRevision += 1;
    }
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

  static bool ReadBool(SaveData data, string key, bool fallback) {
    if (data == null || string.IsNullOrWhiteSpace(key)) return fallback;
    if (!data.TryGetValue(key, out var value) || value == null) return fallback;

    try {
      return Convert.ToBoolean(value);
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
