using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class AbilityManager : MonoBehaviour {
  [Tooltip("Ability animation name, such as PunchRight.")]
  public string animationName = "PunchRight";

  readonly List<Action> actions = new();

  FontText nameText;
  FontText levelText;
  Transform xpFill;
  Transform xpBack;
  SpriteRenderer xpFillRenderer;
  SpriteRenderer xpBackRenderer;
  SpriteWithNormals[] themedSprites = Array.Empty<SpriteWithNormals>();
  bool fillPending;
  float pendingFillPercent;
  string displayFormName;
  string displayedAnimation;
  string appliedThemeForm;
  int displayedLevel = int.MinValue;
  bool hierarchyResolved;

  void OnEnable() {
    EnsureMenuManager();
    EnsureResolved();
    RegisterHandlers();
    Refresh();
  }

  void OnDisable() {
    UnregisterHandlers();
  }

  void Update() {
    if (!fillPending) {
      return;
    }

    ApplyFill(pendingFillPercent);
  }

  void OnTransformChildrenChanged() {
    hierarchyResolved = false;
    displayedAnimation = null;
    appliedThemeForm = null;
    displayedLevel = int.MinValue;
    if (!isActiveAndEnabled) {
      return;
    }

    EnsureResolved();
    Refresh();
  }

  public void SetAbility(string value) {
    SetAbility(value, null);
  }

  public void SetAbility(string value, string formName) {
    if (!EsperanzaAbilities.TryResolveAbilityAnimation(value, out var resolvedAnimation)) {
      return;
    }

    animationName = resolvedAnimation;
    displayFormName = EsperanzaForms.ResolveFormKey(formName);
    if (!isActiveAndEnabled) {
      return;
    }
    Refresh();
  }

  void EnsureMenuManager() {
    if (!Application.isPlaying || transform.parent == null) {
      return;
    }
    if (!string.Equals(transform.parent.name, "ABILITIES", StringComparison.OrdinalIgnoreCase)) {
      return;
    }
    if (transform.parent.GetComponent<AbilityMenuManager>() != null) {
      return;
    }

    transform.parent.gameObject.AddComponent<AbilityMenuManager>();
  }

  void RegisterHandlers() {
    if (actions.Count > 0) {
      return;
    }

    actions.Add(MessageBus.On(CharacterMessageTopics.AbilityProgressChanged, HandleProgressChanged));
    actions.Add(MessageBus.On(CharacterMessageTopics.FormChanged, _ => Refresh()));
    actions.Add(MessageBus.On(CharacterMessageTopics.GearReady, _ => Refresh()));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  void HandleProgressChanged(string changedAnimation) {
    if (!EsperanzaAbilities.TryResolveAbilityAnimation(animationName, out var resolvedAnimation)) {
      return;
    }

    if (!string.Equals(changedAnimation, resolvedAnimation, StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    Refresh();
  }

  void EnsureResolved(bool force = false) {
    if (!force && hierarchyResolved) {
      return;
    }

    if (force) {
      displayedAnimation = null;
      appliedThemeForm = null;
      displayedLevel = int.MinValue;
    }

    if (force || nameText == null) {
      nameText = FindFontText("nameText");
    }
    if (force || levelText == null) {
      levelText = FindFontText("levelText");
    }
    if (force || xpFill == null) {
      xpFill = FindChildRecursive(transform, "xpfill");
    }
    if (force || xpBack == null) {
      xpBack = FindChildRecursive(transform, "xp");
    }
    if (force || xpFillRenderer == null) {
      xpFillRenderer = xpFill != null ? xpFill.GetComponent<SpriteRenderer>() : null;
    }
    if (force || xpBackRenderer == null) {
      xpBackRenderer = xpBack != null ? xpBack.GetComponent<SpriteRenderer>() : null;
    }
    if (force || themedSprites == null || themedSprites.Length == 0) {
      themedSprites = GetComponentsInChildren<SpriteWithNormals>(includeInactive: true);
    }

    hierarchyResolved = true;
  }

  void Refresh() {
    EnsureResolved();
    if (!EsperanzaAbilities.TryResolveAbilityAnimation(animationName, out var resolvedAnimation)) {
      return;
    }

    animationName = resolvedAnimation;
    if (!EsperanzaAbilities.TryGetProgressValues(
          resolvedAnimation,
          out var level,
          out var currentXp,
          out var nextLevelXp
        )) {
      return;
    }

    if (!string.Equals(displayedAnimation, resolvedAnimation, StringComparison.Ordinal)) {
      displayedAnimation = resolvedAnimation;
      ApplyText(nameText, EsperanzaAbilities.GetDisplayName(resolvedAnimation));
    }

    if (displayedLevel != level) {
      displayedLevel = level;
      ApplyText(levelText, IntegerTextCache.Get(level));
    }

    var safeNextLevelXp = Mathf.Max(nextLevelXp, 1);
    var fillPercent = Mathf.Clamp01((float)currentXp / safeNextLevelXp);
    ApplyFill(fillPercent);
    var themeForm = !string.IsNullOrWhiteSpace(displayFormName)
      ? displayFormName
      : EsperanzaAbilities.ResolveForm(resolvedAnimation);
    if (!string.Equals(appliedThemeForm, themeForm, StringComparison.OrdinalIgnoreCase)) {
      appliedThemeForm = themeForm;
      ApplyTheme(themeForm);
    }
  }

  void ApplyText(FontText fontText, string value) {
    if (fontText == null || fontText.content == value) {
      return;
    }

    fontText.content = value;
    fontText.Generate();
  }

  void ApplyFill(float fillPercent) {
    pendingFillPercent = Mathf.Clamp01(fillPercent);
    if (xpFill == null || xpBack == null) {
      fillPending = true;
      return;
    }

    if (xpFillRenderer == null || xpBackRenderer == null) {
      fillPending = true;
      return;
    }
    if (xpFillRenderer.sprite == null || xpBackRenderer.sprite == null) {
      fillPending = true;
      return;
    }

    var fillWidth = xpFillRenderer.sprite.bounds.size.x;
    var backWidth = xpBackRenderer.sprite.bounds.size.x;
    if (fillWidth <= 0f || backWidth <= 0f) {
      fillPending = true;
      return;
    }

    var backScaleX = xpBack.localScale.x;
    var fullWidth = Mathf.Abs(backWidth * backScaleX);
    var targetWidth = fullWidth * pendingFillPercent;
    var targetScaleX = targetWidth / fillWidth;

    var backBounds = xpBackRenderer.sprite.bounds;
    var backEdgeA = backBounds.min.x * backScaleX;
    var backEdgeB = backBounds.max.x * backScaleX;
    var backLeftEdge = xpBack.localPosition.x + Mathf.Min(backEdgeA, backEdgeB);

    var fillBounds = xpFillRenderer.sprite.bounds;
    var fillEdgeA = fillBounds.min.x * targetScaleX;
    var fillEdgeB = fillBounds.max.x * targetScaleX;
    var fillLeftOffset = Mathf.Min(fillEdgeA, fillEdgeB);

    var fillScale = xpFill.localScale;
    fillScale.x = targetScaleX;
    xpFill.localScale = fillScale;

    var fillPosition = xpFill.localPosition;
    fillPosition.x = backLeftEdge - fillLeftOffset;
    xpFill.localPosition = fillPosition;
    fillPending = false;
  }

  void ApplyTheme(string formName) {
    if (string.IsNullOrWhiteSpace(formName) || themedSprites == null) {
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
      if (string.Equals(sprite.labelPrefix, formName, StringComparison.Ordinal)) {
        continue;
      }

      sprite.SetLabelPrefix(formName);
      sprite.ForceUpdateSpriteAndNormal();
    }
  }

  FontText FindFontText(string nodeName) {
    var node = FindChildRecursive(transform, nodeName);
    return node != null
      ? node.GetComponentInChildren<FontText>(includeInactive: true)
      : null;
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
