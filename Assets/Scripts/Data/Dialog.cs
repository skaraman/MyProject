using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class DialogSpeakerDefinition {
  public string speakerId;
  public string speakerName;
  public string portraitLibraryName;
  public GameplayDialogController.DialogSpeakerSide speakerSide;
  public List<GameplayDialogController.GameplayDialogNode> lines = new();

  public DialogSpeakerDefinition(
    string speakerId,
    string speakerName,
    string portraitLibraryName,
    GameplayDialogController.DialogSpeakerSide speakerSide,
    params GameplayDialogController.GameplayDialogNode[] lines
  ) {
    this.speakerId = NormalizeToken(speakerId);
    this.speakerName = string.IsNullOrWhiteSpace(speakerName) ? this.speakerId : speakerName.Trim();
    this.portraitLibraryName = NormalizeToken(portraitLibraryName);
    this.speakerSide = speakerSide;
    if (lines == null) {
      return;
    }

    for (var i = 0; i < lines.Length; i++) {
      if (lines[i] == null) {
        continue;
      }
      lines[i].speakerId = this.speakerId;
      lines[i].speaker = this.speakerSide;
      if (string.IsNullOrWhiteSpace(lines[i].speakerName)) {
        lines[i].speakerName = this.speakerName;
      }
      if (string.IsNullOrWhiteSpace(lines[i].portraitLibraryName) &&
          !string.IsNullOrWhiteSpace(this.portraitLibraryName)) {
        lines[i].portraitLibraryName = this.portraitLibraryName;
      }
      this.lines.Add(lines[i]);
    }
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }
}

[Serializable]
public class LocationDialogDefinition {
  public string locationId;
  public List<DialogSpeakerDefinition> speakers = new();

  public LocationDialogDefinition(string locationId, params DialogSpeakerDefinition[] speakers) {
    this.locationId = NormalizeToken(locationId);
    if (speakers == null) {
      return;
    }

    for (var i = 0; i < speakers.Length; i++) {
      if (speakers[i] == null) {
        continue;
      }
      this.speakers.Add(speakers[i]);
      if (speakers[i].lines == null) {
        continue;
      }

      for (var lineIndex = 0; lineIndex < speakers[i].lines.Count; lineIndex++) {
        var line = speakers[i].lines[lineIndex];
        if (line == null) {
          continue;
        }
        line.locationId = this.locationId;
      }
    }
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }
}

public static class DialogData {
  static readonly Dictionary<string, LocationDialogDefinition> locations = new(StringComparer.OrdinalIgnoreCase) {
    [LocationEnemyData.DomeCityLocationId] = new LocationDialogDefinition(
      LocationEnemyData.DomeCityLocationId,
      new DialogSpeakerDefinition(
        speakerId: "Imp",
        speakerName: "Imp",
        portraitLibraryName: "Dialog/DialogImp",
        speakerSide: GameplayDialogController.DialogSpeakerSide.Enemy,
        CreateNode(
          lineNumber: 1,
          text: "Your life is forfeit!",
          emotion: "Normal"
        )
      ),
      new DialogSpeakerDefinition(
        speakerId: "Esperanza",
        speakerName: "Esperanza",
        portraitLibraryName: "",
        speakerSide: GameplayDialogController.DialogSpeakerSide.Esperanza,
        CreateNode(
          lineNumber: 2,
          text: "Whoa that thing talked!",
          emotion: "Surprise"
        )
      )
    )
  };

  public static bool TryGetLocation(string locationId, out LocationDialogDefinition locationDialog) {
    locationDialog = null;
    var normalizedLocationId = NormalizeToken(locationId);
    if (string.IsNullOrWhiteSpace(normalizedLocationId)) {
      return false;
    }

    return locations.TryGetValue(normalizedLocationId, out locationDialog) && locationDialog != null;
  }

  static GameplayDialogController.GameplayDialogNode CreateNode(
    int lineNumber,
    string text,
    string emotion = "Normal",
    string speakerName = "",
    string avatarForm = "",
    GameplayDialogController.DialogOtherType otherType = GameplayDialogController.DialogOtherType.Auto,
    string portraitLibraryName = ""
  ) {
    return new GameplayDialogController.GameplayDialogNode {
      lineNumber = Mathf.Max(lineNumber, 0),
      text = text ?? "",
      emotion = string.IsNullOrWhiteSpace(emotion) ? "Normal" : emotion.Trim(),
      speakerName = speakerName ?? "",
      avatarForm = avatarForm ?? "",
      otherType = otherType,
      portraitLibraryName = portraitLibraryName ?? ""
    };
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }
}
