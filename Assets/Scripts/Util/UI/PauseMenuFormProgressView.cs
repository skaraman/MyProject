using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PauseMenuFormProgressView : MonoBehaviour {
  readonly List<Action> actions = new();
  FontText levelNumberText;
  FontText currentXpText;
  FontText neededXpText;
  AnchoredSpriteStretch xpBarStretch;
  SpriteWithNormals[] themedSprites = Array.Empty<SpriteWithNormals>();
  string appliedThemeForm;
  int displayedLevel = int.MinValue;
  int displayedCurrentXp = int.MinValue;
  int displayedNextLevelXp = int.MinValue;
  bool hierarchyResolved;

  static bool ShouldLogDebug() {
    return SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs &&
           (Application.isEditor || Debug.isDebugBuild);
  }

  void OnEnable() {
    EnsureResolved(force: true);
    RegisterHandlers();
    Refresh("enable");
  }

  void OnDisable() {
    UnregisterHandlers();
  }

  void OnTransformChildrenChanged() {
    if (!isActiveAndEnabled) {
      return;
    }

    EnsureResolved(force: true);
    Refresh("children_changed");
  }

  void RegisterHandlers() {
    if (actions.Count > 0) {
      return;
    }

    actions.Add(MessageBus.On(CharacterMessageTopics.FormChanged, _ => Refresh("form_changed")));
    actions.Add(MessageBus.On(CharacterMessageTopics.FormProgressChanged, form => RefreshProgressForPayload(form, "form_progress_changed")));
    actions.Add(MessageBus.On(CharacterMessageTopics.GearReady, _ => Refresh("gear_ready")));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  void RefreshProgressForPayload(object payload, string source) {
    var requestedForm = payload as string;
    var activeForm = EsperanzaForms.GetActive();
    if (!string.IsNullOrWhiteSpace(requestedForm) &&
        !string.Equals(requestedForm, activeForm, StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    Refresh(source);
  }

  void EnsureResolved(bool force = false) {
    if (!force && hierarchyResolved) {
      return;
    }

    if (force) {
      appliedThemeForm = null;
      displayedLevel = int.MinValue;
      displayedCurrentXp = int.MinValue;
      displayedNextLevelXp = int.MinValue;
    }

    if (force || levelNumberText == null) {
      levelNumberText = FindFontText("levelNumberText", "levelNumber");
    }
    if (force || currentXpText == null) {
      currentXpText = FindFontText("currentXpText", "currentXP");
    }
    if (force || neededXpText == null) {
      neededXpText = FindFontText("neededXpText", "neededXP");
    }
    if (force || xpBarStretch == null) {
      var xpBarFill = FindChildRecursive(transform, "XPBarFill");
      xpBarStretch = xpBarFill != null ? xpBarFill.GetComponent<AnchoredSpriteStretch>() : null;
    }
    if (force || themedSprites == null || themedSprites.Length == 0) {
      themedSprites = GetComponentsInChildren<SpriteWithNormals>(includeInactive: true);
    }
    hierarchyResolved = true;
  }

  void Refresh(string source) {
    EnsureResolved();
    var activeForm = EsperanzaForms.GetActive();
    if (string.IsNullOrWhiteSpace(activeForm)) {
      return;
    }
    if (!EsperanzaForms.TryGetProgressValues(
          activeForm,
          out var level,
          out var currentXp,
          out var nextLevelXp
        )) {
      return;
    }

    if (displayedLevel != level) {
      displayedLevel = level;
      ApplyText(levelNumberText, IntegerTextCache.Get(level));
    }
    if (displayedCurrentXp != currentXp) {
      displayedCurrentXp = currentXp;
      ApplyText(currentXpText, IntegerTextCache.Get(currentXp));
    }
    if (displayedNextLevelXp != nextLevelXp) {
      displayedNextLevelXp = nextLevelXp;
      ApplyText(neededXpText, IntegerTextCache.Get(nextLevelXp));
    }

    ApplyFill(currentXp, nextLevelXp);
    if (!string.Equals(appliedThemeForm, activeForm, StringComparison.OrdinalIgnoreCase)) {
      appliedThemeForm = activeForm;
      ApplyTheme(activeForm);
    }

    if (ShouldLogDebug()) {
      RuntimeLog.Log(
        "[PauseMenuFormProgressView] source='" + (source ?? "") +
        "' form='" + activeForm +
        "' level=" + level +
        " current_xp=" + currentXp +
        " next_level_xp=" + nextLevelXp
      );
    }
  }

  void ApplyText(FontText fontText, string value) {
    if (fontText == null) {
      return;
    }

    if (fontText.content == value) {
      return;
    }

    fontText.content = value;
    fontText.Generate();
  }

  void ApplyFill(int currentXp, int nextLevelXp) {
    if (xpBarStretch == null) {
      return;
    }

    var safeNextLevelXp = Mathf.Max(nextLevelXp, 1);
    var progressPercent = Mathf.Clamp01((float)currentXp / safeNextLevelXp) * 100f;
    if (Mathf.Approximately(xpBarStretch.stretchPercent.x, progressPercent)) {
      return;
    }

    xpBarStretch.stretchPercent = new Vector2(progressPercent, xpBarStretch.stretchPercent.y);
    xpBarStretch.RefreshStretch();
  }

  void ApplyTheme(string formName) {
    if (themedSprites == null || themedSprites.Length == 0) {
      return;
    }

    for (var i = 0; i < themedSprites.Length; i++) {
      var sprite = themedSprites[i];
      if (sprite == null) {
        continue;
      }
      if (!string.Equals(sprite.libraryName, "UI/CharUI", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }
      if (string.IsNullOrWhiteSpace(sprite.category)) {
        continue;
      }
      if (sprite.category.IndexOf("XP", StringComparison.OrdinalIgnoreCase) < 0 &&
          sprite.category.IndexOf("Level", StringComparison.OrdinalIgnoreCase) < 0) {
        continue;
      }

      if (!string.Equals(sprite.labelPrefix, formName, StringComparison.Ordinal)) {
        sprite.SetLabelPrefix(formName);
        sprite.ForceUpdateSpriteAndNormal();
      }
    }
  }

  FontText FindFontText(params string[] nodeNames) {
    if (nodeNames == null || nodeNames.Length == 0) {
      return null;
    }

    for (var i = 0; i < nodeNames.Length; i++) {
      var nodeName = nodeNames[i];
      var target = FindChildRecursive(transform, nodeName);
      if (target == null) {
        continue;
      }

      var fontText = target.GetComponentInChildren<FontText>(includeInactive: true);
      if (fontText != null) {
        return fontText;
      }
    }

    return null;
  }

  static Transform FindChildRecursive(Transform root, string targetName) {
    if (root == null || string.IsNullOrWhiteSpace(targetName)) {
      return null;
    }

    if (string.Equals(root.name, targetName, StringComparison.OrdinalIgnoreCase)) {
      return root;
    }

    for (var i = 0; i < root.childCount; i++) {
      var result = FindChildRecursive(root.GetChild(i), targetName);
      if (result != null) {
        return result;
      }
    }

    return null;
  }
}
