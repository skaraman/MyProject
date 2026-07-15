using System;
using System.Collections.Generic;
using UnityEngine;

public static class DialogController {
  const string DialogSaveName = "dialog";
  const string SeenLinesPrefix = "seenLines";
  const string AutoTrigger = "auto";

  sealed class GameplayDialogNodeLineComparer : IComparer<GameplayDialogController.GameplayDialogNode> {
    public int Compare(
      GameplayDialogController.GameplayDialogNode left,
      GameplayDialogController.GameplayDialogNode right
    ) {
      if (ReferenceEquals(left, right)) {
        return 0;
      }
      if (left == null) {
        return 1;
      }
      if (right == null) {
        return -1;
      }

      var lineCompare = left.lineNumber.CompareTo(right.lineNumber);
      if (lineCompare != 0) {
        return lineCompare;
      }

      var speakerCompare = string.Compare(left.speakerId, right.speakerId, StringComparison.OrdinalIgnoreCase);
      if (speakerCompare != 0) {
        return speakerCompare;
      }

      return string.Compare(left.text, right.text, StringComparison.Ordinal);
    }
  }

  static readonly HashSet<string> seenLineKeys = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> debugSessionSeenLineKeys = new(StringComparer.OrdinalIgnoreCase);
  static readonly SaveData saveBuffer = new();
  static readonly Dictionary<string, int> seenSnapshotBuffer = new(StringComparer.OrdinalIgnoreCase);
  static readonly GameplayDialogNodeLineComparer dialogNodeLineComparer = new();

  static bool debugTreatAllDialogAsUnseen;
  static string debugSessionLocationId = "";
  static int loadedSlot = -1;
  static bool runtimeStateReady;

  public static bool IsStateReadyForCurrentSlot => runtimeStateReady && loadedSlot == SaveSlotManager.slot;

  static bool ShouldLogDialogDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs) {
      return false;
    }
    return Application.isEditor || Debug.isDebugBuild;
  }

  public static void SetDebugTreatAllDialogAsUnseen(bool enabled, string source = "runtime") {
    if (debugTreatAllDialogAsUnseen == enabled) {
      return;
    }

    debugTreatAllDialogAsUnseen = enabled;
    ClearDebugLocationSession();

    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[DialogController][SetDebugTreatAllDialogAsUnseen] source='" + (source ?? "") +
        "' enabled=" + (debugTreatAllDialogAsUnseen ? 1 : 0) +
        " seen_count=" + seenLineKeys.Count +
        " debug_session_seen_count=" + debugSessionSeenLineKeys.Count +
        " runtime_ready=" + (runtimeStateReady ? 1 : 0)
      );
    }
  }

  public static void BeginLocationDialogSession(string locationId, string source = "runtime") {
    if (!debugTreatAllDialogAsUnseen) {
      return;
    }

    debugSessionLocationId = NormalizeToken(locationId);
    debugSessionSeenLineKeys.Clear();
    if (!ShouldLogDialogDebug()) {
      return;
    }

    RuntimeLog.Log(
      "[DialogController][BeginLocationDialogSession] source='" + (source ?? "") +
      "' location='" + debugSessionLocationId + "'" +
      " debug_session_seen_count=" + debugSessionSeenLineKeys.Count
    );
  }

  public static void ResetRuntimeState(string source = "runtime") {
    seenLineKeys.Clear();
    ClearDebugLocationSession();
    loadedSlot = SaveSlotManager.slot;
    runtimeStateReady = true;

    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[DialogController][ResetRuntimeState] source='" + (source ?? "") +
        "' slot=" + loadedSlot +
        " seen_count=" + seenLineKeys.Count
      );
    }
  }

  public static void LoadState(string source = "runtime") {
    seenLineKeys.Clear();
    ClearDebugLocationSession();

    var loaded = SaveSlotManager.Load(DialogSaveName);
    LoadSeenKeys(loaded);

    loadedSlot = SaveSlotManager.slot;
    runtimeStateReady = true;

    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[DialogController][LoadState] source='" + (source ?? "") +
        "' slot=" + loadedSlot +
        " seen_count=" + seenLineKeys.Count +
        " save_keys=" + (loaded != null ? loaded.Count : 0)
      );
    }
  }

  public static bool SaveState(string source = "runtime") {
    try {
      saveBuffer.ClearPrefix(SeenLinesPrefix);
      FillSeenSnapshot(seenSnapshotBuffer);
      saveBuffer.SetComplex(SeenLinesPrefix, seenSnapshotBuffer);
      SaveSlotManager.Save(DialogSaveName, saveBuffer);

      if (ShouldLogDialogDebug()) {
        RuntimeLog.Log(
          "[DialogController][SaveState] source='" + (source ?? "") +
          "' slot=" + SaveSlotManager.slot +
          " seen_count=" + seenLineKeys.Count
        );
      }

      return true;
    }
    catch (Exception exception) {
      Debug.LogWarning(
        "[DialogController][SaveState] Failed source='" + (source ?? "") +
        "' error='" + exception.Message + "'"
      );
      return false;
    }
  }

  public static bool TryBuildUnseenSequence(string locationId, List<GameplayDialogController.GameplayDialogNode> sequence) {
    return TryBuildTriggeredSequence(locationId, AutoTrigger, sequence);
  }

  public static bool TryBuildTriggeredSequence(
    string locationId,
    string trigger,
    List<GameplayDialogController.GameplayDialogNode> sequence
  ) {
    if (sequence == null) {
      return false;
    }

    sequence.Clear();
    if (!DialogData.TryGetLocation(locationId, out var locationDialog) || locationDialog == null) {
      if (ShouldLogDialogDebug()) {
        RuntimeLog.Log(
          "[DialogController][TryBuildTriggeredSequence] Missing location dialog location='" + (locationId ?? "") + "'"
        );
      }
      return false;
    }

    var normalizedTrigger = NormalizeDialogTrigger(trigger);
    AppendTriggeredLocationLines(locationDialog, normalizedTrigger, sequence);
    if (sequence.Count <= 0) {
      if (ShouldLogDialogDebug()) {
        RuntimeLog.Log(
          "[DialogController][TryBuildTriggeredSequence] No unseen lines remain" +
          " location='" + locationDialog.locationId + "'" +
          " trigger='" + normalizedTrigger + "'"
        );
      }
      return false;
    }

    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[DialogController][TryBuildTriggeredSequence] location='" + locationDialog.locationId +
        "' trigger='" + normalizedTrigger +
        "' unseen_count=" + sequence.Count
      );
    }

    return true;
  }

  public static void CollectPendingTriggers(string locationId, List<string> triggers) {
    if (triggers == null) {
      return;
    }

    triggers.Clear();
    if (!DialogData.TryGetLocation(locationId, out var locationDialog) || locationDialog == null || locationDialog.speakers == null) {
      return;
    }

    for (var speakerIndex = 0; speakerIndex < locationDialog.speakers.Count; speakerIndex++) {
      if (!TryResolveNextUnseenChunk(locationDialog.locationId, locationDialog.speakers[speakerIndex], out _, out var chunkTrigger)) {
        continue;
      }
      if (string.Equals(chunkTrigger, AutoTrigger, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }
      AddUniqueTrigger(triggers, chunkTrigger);
    }

    if (!ShouldLogDialogDebug()) {
      return;
    }

    RuntimeLog.Log(
      "[DialogController][CollectPendingTriggers] location='" + locationDialog.locationId +
      "' trigger_count=" + triggers.Count +
      " triggers='" + string.Join(", ", triggers) + "'"
    );
  }

  public static bool HasPendingTriggeredSequence(string locationId, string trigger) {
    if (!DialogData.TryGetLocation(locationId, out var locationDialog) || locationDialog == null || locationDialog.speakers == null) {
      return false;
    }

    var normalizedTrigger = NormalizeDialogTrigger(trigger);
    for (var speakerIndex = 0; speakerIndex < locationDialog.speakers.Count; speakerIndex++) {
      if (!TryResolveNextUnseenChunk(locationDialog.locationId, locationDialog.speakers[speakerIndex], out _, out var chunkTrigger)) {
        continue;
      }
      if (string.Equals(chunkTrigger, normalizedTrigger, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  public static bool MarkSeen(GameplayDialogController.GameplayDialogNode node, string source = "runtime") {
    if (node == null) {
      return false;
    }

    return MarkSeen(node.locationId, node.speakerId, node.lineNumber, source);
  }

  public static bool MarkSeen(string locationId, string speakerId, int lineNumber, string source = "runtime") {
    if (!TryNormalizeSeenKey(locationId, speakerId, lineNumber, out var normalizedLocationId, out var normalizedSpeakerId, out var normalizedLineNumber)) {
      return false;
    }

    var seenKey = BuildSeenKey(normalizedLocationId, normalizedSpeakerId, normalizedLineNumber);
    if (!seenLineKeys.Add(seenKey)) {
      return false;
    }

    var saved = SaveState("mark_seen:" + (source ?? ""));
    if (ShouldLogDialogDebug()) {
      RuntimeLog.Log(
        "[DialogController][MarkSeen] location='" + normalizedLocationId +
        "' speaker='" + normalizedSpeakerId +
        "' line=" + normalizedLineNumber +
        " source='" + (source ?? "") +
        "' saved=" + (saved ? 1 : 0)
      );
    }

    return true;
  }

  public static bool WasSeen(string locationId, string speakerId, int lineNumber) {
    if (!TryNormalizeSeenKey(locationId, speakerId, lineNumber, out var normalizedLocationId, out var normalizedSpeakerId, out var normalizedLineNumber)) {
      return false;
    }

    var seenKey = BuildSeenKey(normalizedLocationId, normalizedSpeakerId, normalizedLineNumber);
    if (debugTreatAllDialogAsUnseen) {
      return false;
    }

    return seenLineKeys.Contains(seenKey);
  }

  static void ClearDebugLocationSession() {
    debugSessionLocationId = "";
    debugSessionSeenLineKeys.Clear();
  }

  static void LoadSeenKeys(SaveData loaded) {
    if (loaded == null || !loaded.HasPrefix(SeenLinesPrefix)) {
      return;
    }

    var savedSeen = loaded.GetComplex<Dictionary<string, int>>(SeenLinesPrefix);
    if (savedSeen == null) {
      return;
    }

    foreach (var entry in savedSeen) {
      if (entry.Value <= 0 || string.IsNullOrWhiteSpace(entry.Key)) {
        continue;
      }

      seenLineKeys.Add(entry.Key.Trim());
    }
  }

  static void AppendTriggeredLocationLines(
    LocationDialogDefinition locationDialog,
    string trigger,
    List<GameplayDialogController.GameplayDialogNode> sequence
  ) {
    if (locationDialog == null || locationDialog.speakers == null) {
      return;
    }

    var normalizedLocationId = NormalizeToken(locationDialog.locationId);
    var startCount = sequence != null ? sequence.Count : 0;
    for (var speakerIndex = 0; speakerIndex < locationDialog.speakers.Count; speakerIndex++) {
      AppendTriggeredSpeakerChunk(normalizedLocationId, locationDialog.speakers[speakerIndex], trigger, sequence);
    }

    if (sequence == null) {
      return;
    }

    var addedCount = sequence.Count - startCount;
    if (addedCount > 1) {
      sequence.Sort(startCount, addedCount, dialogNodeLineComparer);
    }
  }

  static void AppendTriggeredSpeakerChunk(
    string normalizedLocationId,
    DialogSpeakerDefinition speaker,
    string requestedTrigger,
    List<GameplayDialogController.GameplayDialogNode> sequence
  ) {
    if (sequence == null ||
        !TryResolveNextUnseenChunk(normalizedLocationId, speaker, out var startIndex, out var chunkTrigger) ||
        !string.Equals(chunkTrigger, NormalizeDialogTrigger(requestedTrigger), StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    var normalizedSpeakerId = NormalizeToken(speaker.speakerId);
    for (var lineIndex = startIndex; lineIndex < speaker.lines.Count; lineIndex++) {
      var line = speaker.lines[lineIndex];
      if (line == null) {
        continue;
      }

      var normalizedLineNumber = Mathf.Max(line.lineNumber, 0);
      if (!IsValidLineNumber(normalizedLocationId, normalizedSpeakerId, normalizedLineNumber)) {
        continue;
      }
      if (WasSeen(normalizedLocationId, normalizedSpeakerId, normalizedLineNumber)) {
        break;
      }

      if (!string.Equals(NormalizeDialogTrigger(line.trigger), chunkTrigger, StringComparison.OrdinalIgnoreCase)) {
        break;
      }

      ApplyMissingSpeakerName(line, speaker, normalizedSpeakerId);
      sequence.Add(line);
    }
  }

  static bool TryResolveNextUnseenChunk(
    string normalizedLocationId,
    DialogSpeakerDefinition speaker,
    out int startIndex,
    out string chunkTrigger
  ) {
    startIndex = -1;
    chunkTrigger = AutoTrigger;
    if (speaker == null || speaker.lines == null) {
      return false;
    }

    var normalizedSpeakerId = NormalizeToken(speaker.speakerId);
    for (var lineIndex = 0; lineIndex < speaker.lines.Count; lineIndex++) {
      var line = speaker.lines[lineIndex];
      if (line == null) {
        continue;
      }

      var normalizedLineNumber = Mathf.Max(line.lineNumber, 0);
      if (!IsValidLineNumber(normalizedLocationId, normalizedSpeakerId, normalizedLineNumber)) {
        continue;
      }
      if (WasSeen(normalizedLocationId, normalizedSpeakerId, normalizedLineNumber)) {
        continue;
      }

      startIndex = lineIndex;
      chunkTrigger = NormalizeDialogTrigger(line.trigger);
      return true;
    }

    return false;
  }

  static void ApplyMissingSpeakerName(
    GameplayDialogController.GameplayDialogNode line,
    DialogSpeakerDefinition speaker,
    string normalizedSpeakerId
  ) {
    if (line == null || !string.IsNullOrWhiteSpace(line.speakerName)) {
      return;
    }

    line.speakerName = string.IsNullOrWhiteSpace(speaker != null ? speaker.speakerName : null)
      ? normalizedSpeakerId
      : speaker.speakerName.Trim();
  }

  static bool IsValidLineNumber(string locationId, string speakerId, int lineNumber) {
    if (lineNumber > 0) {
      return true;
    }

    Debug.LogWarning(
      "[DialogController][TryBuildUnseenSequence] Ignored line with invalid line number" +
      " location='" + locationId +
      "' speaker='" + speakerId + "'"
    );
    return false;
  }

  static bool TryNormalizeSeenKey(
    string locationId,
    string speakerId,
    int lineNumber,
    out string normalizedLocationId,
    out string normalizedSpeakerId,
    out int normalizedLineNumber
  ) {
    normalizedLocationId = NormalizeToken(locationId);
    normalizedSpeakerId = NormalizeToken(speakerId);
    normalizedLineNumber = Mathf.Max(lineNumber, 0);

    return !string.IsNullOrWhiteSpace(normalizedLocationId) &&
           !string.IsNullOrWhiteSpace(normalizedSpeakerId) &&
           normalizedLineNumber > 0;
  }

  static void FillSeenSnapshot(Dictionary<string, int> snapshot) {
    if (snapshot == null) {
      return;
    }

    snapshot.Clear();
    foreach (var seenKey in seenLineKeys) {
      if (string.IsNullOrWhiteSpace(seenKey)) {
        continue;
      }

      snapshot[seenKey] = 1;
    }
  }

  static string BuildSeenKey(string locationId, string speakerId, int lineNumber) {
    return NormalizeToken(locationId) + "|" + NormalizeToken(speakerId) + "|" + Mathf.Max(lineNumber, 0);
  }

  static void AddUniqueTrigger(List<string> triggers, string trigger) {
    if (triggers == null) {
      return;
    }

    var normalizedTrigger = NormalizeDialogTrigger(trigger);
    for (var i = 0; i < triggers.Count; i++) {
      if (string.Equals(triggers[i], normalizedTrigger, StringComparison.OrdinalIgnoreCase)) {
        return;
      }
    }

    triggers.Add(normalizedTrigger);
  }

  static string NormalizeDialogTrigger(string value) {
    if (string.IsNullOrWhiteSpace(value) ||
        string.Equals(value.Trim(), AutoTrigger, StringComparison.OrdinalIgnoreCase)) {
      return AutoTrigger;
    }

    return value.Trim();
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }
}
