using System;
using System.Collections.Generic;
using UnityEngine;

public static class DialogController {
  const string DialogSaveName = "dialog";
  const string SeenLinesPrefix = "seenLines";

  static readonly HashSet<string> seenLineKeys = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> debugSessionSeenLineKeys = new(StringComparer.OrdinalIgnoreCase);
  static readonly SaveData saveBuffer = new();
  static readonly Dictionary<string, int> seenSnapshotBuffer = new(StringComparer.OrdinalIgnoreCase);

  static bool debugTreatAllDialogAsUnseen;
  static string debugSessionLocationId = "";
  static int loadedSlot = -1;
  static bool runtimeStateReady;

  public static bool IsStateReadyForCurrentSlot => runtimeStateReady && loadedSlot == SaveSlotManager.slot;

  static bool ShouldLogDialogDebug() {
    return Application.isEditor || Debug.isDebugBuild;
  }

  public static void SetDebugTreatAllDialogAsUnseen(bool enabled, string source = "runtime") {
    if (debugTreatAllDialogAsUnseen == enabled) {
      return;
    }

    debugTreatAllDialogAsUnseen = enabled;
    ClearDebugLocationSession();
    if (!debugTreatAllDialogAsUnseen && runtimeStateReady) {
      LoadState("debug_disabled:" + (source ?? ""));
      return;
    }

    if (ShouldLogDialogDebug()) {
      Debug.Log(
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

    Debug.Log(
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
      Debug.Log(
        "[DialogController][ResetRuntimeState] source='" + (source ?? "") +
        "' slot=" + loadedSlot +
        " seen_count=" + seenLineKeys.Count
      );
    }
  }

  public static void LoadState(string source = "runtime") {
    seenLineKeys.Clear();
    ClearDebugLocationSession();

    SaveData loaded = null;
    if (debugTreatAllDialogAsUnseen) {
      if (ShouldLogDialogDebug()) {
        Debug.Log(
          "[DialogController][LoadState] Bypassed seen-state load while debug is enabled" +
          " source='" + (source ?? "") + "'"
        );
      }
    }
    else {
      loaded = SaveSlotManager.Load(DialogSaveName);
      LoadSeenKeys(loaded);
    }

    loadedSlot = SaveSlotManager.slot;
    runtimeStateReady = true;

    if (ShouldLogDialogDebug()) {
      Debug.Log(
        "[DialogController][LoadState] source='" + (source ?? "") +
        "' slot=" + loadedSlot +
        " seen_count=" + seenLineKeys.Count +
        " save_keys=" + (loaded != null ? loaded.Count : 0)
      );
    }
  }

  public static bool SaveState(string source = "runtime") {
    if (debugTreatAllDialogAsUnseen) {
      if (ShouldLogDialogDebug()) {
        Debug.Log(
          "[DialogController][SaveState] Bypassed seen-state save while debug is enabled" +
          " source='" + (source ?? "") + "'" +
          " slot=" + SaveSlotManager.slot
        );
      }
      return true;
    }

    try {
      saveBuffer.ClearPrefix(SeenLinesPrefix);
      FillSeenSnapshot(seenSnapshotBuffer);
      saveBuffer.SetComplex(SeenLinesPrefix, seenSnapshotBuffer);
      SaveSlotManager.Save(DialogSaveName, saveBuffer);

      if (ShouldLogDialogDebug()) {
        Debug.Log(
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
    if (sequence == null) {
      return false;
    }

    sequence.Clear();
    if (!DialogData.TryGetLocation(locationId, out var locationDialog) || locationDialog == null) {
      if (ShouldLogDialogDebug()) {
        Debug.Log(
          "[DialogController][TryBuildUnseenSequence] Missing location dialog location='" + (locationId ?? "") + "'"
        );
      }
      return false;
    }

    AppendUnseenLocationLines(locationDialog, sequence);
    if (sequence.Count <= 0) {
      if (ShouldLogDialogDebug()) {
        Debug.Log(
          "[DialogController][TryBuildUnseenSequence] No unseen lines remain location='" + locationDialog.locationId + "'"
        );
      }
      return false;
    }

    if (sequence.Count > 1) {
      sequence.Sort(CompareNodes);
    }

    if (ShouldLogDialogDebug()) {
      Debug.Log(
        "[DialogController][TryBuildUnseenSequence] location='" + locationDialog.locationId +
        "' unseen_count=" + sequence.Count
      );
    }

    return true;
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
    if (debugTreatAllDialogAsUnseen) {
      var addedDebugSeen = debugSessionSeenLineKeys.Add(seenKey);
      if (ShouldLogDialogDebug()) {
        Debug.Log(
          "[DialogController][MarkSeen] Runtime-only while debug is enabled" +
          " location='" + normalizedLocationId +
          "' speaker='" + normalizedSpeakerId +
          "' line=" + normalizedLineNumber +
          " source='" + (source ?? "") + "'" +
          " added=" + (addedDebugSeen ? 1 : 0) +
          " session_location='" + debugSessionLocationId + "'" +
          " debug_session_seen_count=" + debugSessionSeenLineKeys.Count
        );
      }
      return addedDebugSeen;
    }

    if (!seenLineKeys.Add(seenKey)) {
      return false;
    }

    var saved = SaveState("mark_seen:" + (source ?? ""));
    if (ShouldLogDialogDebug()) {
      Debug.Log(
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
      return debugSessionSeenLineKeys.Contains(seenKey);
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

  static void AppendUnseenLocationLines(
    LocationDialogDefinition locationDialog,
    List<GameplayDialogController.GameplayDialogNode> sequence
  ) {
    if (locationDialog == null || locationDialog.speakers == null) {
      return;
    }

    var normalizedLocationId = NormalizeToken(locationDialog.locationId);
    for (var speakerIndex = 0; speakerIndex < locationDialog.speakers.Count; speakerIndex++) {
      AppendUnseenSpeakerLines(normalizedLocationId, locationDialog.speakers[speakerIndex], sequence);
    }
  }

  static void AppendUnseenSpeakerLines(
    string normalizedLocationId,
    DialogSpeakerDefinition speaker,
    List<GameplayDialogController.GameplayDialogNode> sequence
  ) {
    if (speaker == null || speaker.lines == null) {
      return;
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

      ApplyMissingSpeakerName(line, speaker, normalizedSpeakerId);
      sequence.Add(line);
    }
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

  static int CompareNodes(
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

    var lineComparison = left.lineNumber.CompareTo(right.lineNumber);
    if (lineComparison != 0) {
      return lineComparison;
    }

    return string.Compare(left.speakerId, right.speakerId, StringComparison.OrdinalIgnoreCase);
  }

  static string BuildSeenKey(string locationId, string speakerId, int lineNumber) {
    return NormalizeToken(locationId) + "|" + NormalizeToken(speakerId) + "|" + Mathf.Max(lineNumber, 0);
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }
}
