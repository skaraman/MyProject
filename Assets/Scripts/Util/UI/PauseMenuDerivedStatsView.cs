using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public sealed class PauseMenuDerivedStatsView : MonoBehaviour {
  [SerializeField] FontText statNamesText;
  [SerializeField] FontText statValuesText;
  [SerializeField] FontText statDescriptionsText;

  readonly List<Action> actions = new();

  CharacterState characterState;
  SpriteWithNormals[] themedSprites = Array.Empty<SpriteWithNormals>();
  string selectedForm;

  void OnEnable() {
    EnsureResolved();
    RegisterHandlers();
    Refresh("enable");
  }

  void OnDisable() {
    UnregisterHandlers();
  }

  void EnsureResolved(bool force = false) {
    if (force || characterState == null) {
      characterState = SingleSceneManager.ResolveGameplayCharacterState();
    }
    if (force || statNamesText == null) {
      statNamesText = FindFontText(transform, "statNamesText") ??
                      FindFontText(transform, "names") ??
                      FindFontText(transform, "statNames");
    }
    if (force || statValuesText == null) {
      statValuesText = FindFontText(transform, "statValuesText") ??
                       FindFontText(transform, "values") ??
                       FindFontText(transform, "numbers");
    }
    if (force || statDescriptionsText == null) {
      statDescriptionsText = FindFontText(transform, "statDescriptionsText") ??
                             FindFontText(transform, "description") ??
                             FindFontText(transform, "statDescription");
    }
    if (force || themedSprites == null || themedSprites.Length == 0) {
      themedSprites = GetComponentsInChildren<SpriteWithNormals>(includeInactive: true);
    }
  }

  void RegisterHandlers() {
    if (actions.Count > 0) {
      return;
    }
    actions.Add(MessageBus.On(CharacterMessageTopics.FormChanged, form => OnFormChanged(form)));
    actions.Add(MessageBus.On(CharacterMessageTopics.FormProgressChanged, _ => Refresh("form_progress_changed")));
    actions.Add(MessageBus.On(CharacterMessageTopics.FormStatsChanged, _ => Refresh("form_stats_changed")));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  void OnFormChanged(object payload) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(payload as string);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      resolvedForm = EsperanzaForms.GetActive();
    }
    Refresh("form_changed");
  }

  public void Refresh(string source = "manual") {
    EnsureResolved();
    var activeForm = EsperanzaForms.GetActive();
    if (characterState != null) {
      characterState.GatherAllStatValues();
    }

    var minorStats = Abbreviations.structure != null &&
                     Abbreviations.structure.TryGetValue("Minor", out var list) &&
                     list != null
      ? list
      : new List<string>();

    var statNames = new List<string>();
    var statValues = new List<string>();
    var statDescriptions = new List<string>();

    var derivedStats = AllStatValues.Esperanza;
    if (derivedStats != null) {
      for (var i = 0; i < minorStats.Count; i++) {
        var statKey = minorStats[i];
        if (string.IsNullOrWhiteSpace(statKey)) {
          continue;
        }

        statNames.Add(statKey);

        if (derivedStats.TryGetValue(statKey, out var statVal) && statVal != null) {
          statValues.Add(statVal.ToString());
        } else {
          statValues.Add("0");
        }

        statDescriptions.Add(Abbreviations.GetDescription(statKey));
      }
    }

    ApplyTheme(activeForm);
    ApplyText(statNamesText, string.Join("\n", statNames));
    ApplyText(statValuesText, string.Join("\n", statValues));
    ApplyText(statDescriptionsText, string.Join("\n", statDescriptions));

    RuntimeLog.Log(
      "[PauseMenuDerivedStatsView] Refreshed source='" + (source ?? "") +
      "' form='" + activeForm +
      "' stat_count=" + statNames.Count
    );
  }

  void ApplyTheme(string activeForm) {
    var themeName = !string.IsNullOrWhiteSpace(activeForm) ? activeForm : "Base";
    if (string.Equals(themeName, selectedForm, StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    selectedForm = themeName;
    for (var i = 0; i < themedSprites.Length; i++) {
      var themedSprite = themedSprites[i];
      if (themedSprite == null) {
        continue;
      }
      themedSprite.labelPrefix = themeName;
    }
  }

  static void ApplyText(FontText fontText, string value) {
    if (fontText == null) {
      return;
    }
    if (fontText.content == value) {
      return;
    }
    fontText.content = value;
    fontText.Generate();
  }

  static FontText FindFontText(Transform root, string childName) {
    var child = FindChildRecursive(root, childName);
    return child != null ? child.GetComponent<FontText>() : null;
  }

  static Transform FindChildRecursive(Transform parent, string targetName) {
    if (parent == null || string.IsNullOrWhiteSpace(targetName)) {
      return null;
    }

    var count = parent.childCount;
    for (var i = 0; i < count; i++) {
      var child = parent.GetChild(i);
      if (string.Equals(child.name, targetName, StringComparison.OrdinalIgnoreCase)) {
        return child;
      }
    }

    for (var i = 0; i < count; i++) {
      var nested = FindChildRecursive(parent.GetChild(i), targetName);
      if (nested != null) {
        return nested;
      }
    }

    return null;
  }
}
