using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PauseMenuFormProgressView : MonoBehaviour {
  readonly List<Action> actions = new();
  CharacterState characterState;
  FontText levelNumberText;
  FontText currentXpText;
  FontText neededXpText;
  AnchoredSpriteStretch xpBarStretch;
  SpriteWithNormals[] themedSprites = Array.Empty<SpriteWithNormals>();

  void OnEnable() {
    EnsureResolved();
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

    actions.Add(MessageBus.On("formChanged", o => Refresh("form_changed")));
    actions.Add(MessageBus.On("formProgressChanged", o => RefreshProgressForPayload(o, "form_progress_changed")));
    actions.Add(MessageBus.On("gearReady", o => Refresh("gear_ready")));
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
    if (force || characterState == null) {
      characterState = FindAnyObjectByType<CharacterState>();
    }
  }

  void Refresh(string source) {
    EnsureResolved();
    var activeForm = EsperanzaForms.GetActive();
    var progress = characterState != null
      ? characterState.GetFormProgress(activeForm)
      : EsperanzaForms.GetProgressCopy(activeForm);
    if (string.IsNullOrWhiteSpace(activeForm) || progress == null) {
      return;
    }

    ApplyText(levelNumberText, progress.level.ToString());
    ApplyText(currentXpText, progress.currentXp.ToString());
    ApplyText(neededXpText, progress.nextLevelXp.ToString());
    ApplyFill(progress.currentXp, progress.nextLevelXp);
    ApplyTheme(activeForm);

    Debug.Log(
      "[PauseMenuFormProgressView] source='" + (source ?? "") +
      "' form='" + activeForm +
      "' level=" + progress.level +
      " current_xp=" + progress.currentXp +
      " next_level_xp=" + progress.nextLevelXp
    );
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
