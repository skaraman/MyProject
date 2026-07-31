using System;
using System.Collections.Generic;
using UnityEngine;


public class Avatar : MonoBehaviour {
  const string DirectFormUiRoot = "FormUIs/";
  const string DirectFormUiSuffix = "UI";

  public SpriteWithNormals spriteWithNormals;
  private List<Action> actions = new();

  void OnDestroy() {
    foreach (var action in actions) {
      action();
    }
    actions.Clear();
  }

  void Awake() {
    actions.Add(MessageBus.On(CharacterMessageTopics.GearReady, _ => UpdateSprite()));
  }

  private void UpdateSprite() {
    if (spriteWithNormals == null) {
      return;
    }

    var activeForm = EsperanzaForms.GetActive();
    if (IsDirectFormUiLibrary(spriteWithNormals.libraryName) && !string.IsNullOrWhiteSpace(activeForm)) {
      spriteWithNormals.SetLibraryName(DirectFormUiRoot + activeForm.Trim() + DirectFormUiSuffix);
    }
    else {
      spriteWithNormals.SetLabelPrefix(activeForm);
    }
    spriteWithNormals.ForceUpdateSpriteAndNormal();
  }

  static bool IsDirectFormUiLibrary(string libraryName) {
    if (string.IsNullOrWhiteSpace(libraryName)) {
      return false;
    }

    var normalized = libraryName.Trim().Replace('\\', '/');
    return normalized.StartsWith(DirectFormUiRoot, StringComparison.OrdinalIgnoreCase) &&
           normalized.EndsWith(DirectFormUiSuffix, StringComparison.OrdinalIgnoreCase);
  }
}
