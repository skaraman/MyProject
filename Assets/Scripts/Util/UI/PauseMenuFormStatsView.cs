using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

[DisallowMultipleComponent]
public class PauseMenuFormStatsView : MonoBehaviour {
  const int MinorRowCount = 3;

  [Serializable]
  class StatRow {
    public Transform root;
    public FontText nameText;
    public FontText valueText;
    public FontText increaseText;
  }

  readonly List<Action> actions = new();
  readonly StatRow[] statRows = new StatRow[MinorRowCount];
  readonly Dictionary<string, Dictionary<string, int>> sessionSpendPreview = new(StringComparer.Ordinal);

  CharacterState characterState;
  StatsButtons statsButtons;
  GameObject statsLeftButton;
  GameObject statsRightButton;
  GameObject statsPlusButton;
  FontText statsLabelText;
  FontText statNameText;
  FontText statsNumText;
  FontText statsAvailText;
  SpriteWithNormals[] themedSprites = Array.Empty<SpriteWithNormals>();

  string selectedForm;
  int selectedMajorIndex;
  int hoveredButtonIndex = -1;

  void OnEnable() {
    EnsureResolved();
    RegisterHandlers();
    ResetSelectedMajorStat(EsperanzaForms.GetActive(), "enable", force: true);
    Refresh("enable");
  }

  void OnDisable() {
    UnregisterHandlers();
    ClearSessionSpendPreview("disable");
    SetPressedButton(-1);
    SetHoveredButton(-1);
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

    actions.Add(MessageBus.On(CharacterMessageTopics.FormChanged, form => OnFormChanged(form)));
    actions.Add(MessageBus.On(CharacterMessageTopics.FormProgressChanged, form => RefreshForPayload(form, "form_progress_changed")));
    actions.Add(MessageBus.On(CharacterMessageTopics.FormStatsChanged, form => RefreshForPayload(form, "form_stats_changed")));
    actions.Add(MessageBus.On("pauseMenu.hover", o => OnHover(o)));
    actions.Add(MessageBus.On("pauseMenu.unhover", o => OnUnhover()));
    actions.Add(MessageBus.On("pauseMenu.click", o => OnClick(o)));
    actions.Add(MessageBus.On("pauseMenu.select", o => OnSelect()));
  }

  void UnregisterHandlers() {
    for (var i = 0; i < actions.Count; i++) {
      actions[i]?.Invoke();
    }
    actions.Clear();
  }

  void EnsureResolved(bool force = false) {
    if (force || characterState == null) {
      characterState = SingleSceneManager.ResolveGameplayCharacterState();
    }
    if (force || statsButtons == null) {
      statsButtons = GetComponent<StatsButtons>();
    }
    if (force || statsLeftButton == null) {
      statsLeftButton = FindChildRecursive(transform, "StatsLeft")?.gameObject;
    }
    if (force || statsRightButton == null) {
      statsRightButton = FindChildRecursive(transform, "StatsRight")?.gameObject;
    }
    if (force || statsPlusButton == null) {
      statsPlusButton = FindChildRecursive(transform, "StatsPlus")?.gameObject;
    }
    if (force || statsLabelText == null) {
      statsLabelText = FindFontText(transform, "statsLabelText");
    }
    if (force || statNameText == null) {
      statNameText = FindFontText(transform, "statNameText");
    }
    if (force || statsNumText == null) {
      statsNumText = FindFontText(transform, "statsNumText");
    }
    if (force || statsAvailText == null) {
      statsAvailText = FindFontText(transform, "statsAvailText");
    }
    if (force || themedSprites == null || themedSprites.Length == 0) {
      themedSprites = GetComponentsInChildren<SpriteWithNormals>(includeInactive: true);
    }

    ResolveStatRow(0, "substat1");
    ResolveStatRow(1, "substat2");
    ResolveStatRow(2, "substat3");
  }

  void ResolveStatRow(int index, string rowName) {
    if (index < 0 || index >= statRows.Length) {
      return;
    }

    statRows[index] ??= new StatRow();
    var row = statRows[index];
    row.root = FindChildRecursive(transform, rowName);
    row.nameText = FindFontText(row.root, "name");
    row.valueText = FindFontText(row.root, "num");
    row.increaseText =
      FindFontText(row.root, "increaseText" + (index + 1).ToString(CultureInfo.InvariantCulture)) ??
      FindFontText(row.root, "increaseText");
  }

  void OnFormChanged(object payload) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(payload as string);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      resolvedForm = EsperanzaForms.GetActive();
    }

    ResetSelectedMajorStat(resolvedForm, "form_changed", force: true);
    Refresh("form_changed");
  }

  void RefreshForPayload(object payload, string source) {
    var requestedForm = EsperanzaForms.ResolveFormKey(payload as string);
    var activeForm = EsperanzaForms.GetActive();
    if (!string.IsNullOrWhiteSpace(requestedForm) &&
        !string.Equals(requestedForm, activeForm, StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    Refresh(source);
  }

  void OnHover(object payload) {
    if (!isActiveAndEnabled) {
      return;
    }

    var target = payload as GameObject;
    SetHoveredButton(ResolveButtonIndex(target));
  }

  void OnUnhover() {
    SetHoveredButton(-1);
  }

  void OnClick(object payload) {
    if (!isActiveAndEnabled) {
      return;
    }

    var target = payload as GameObject;
    var buttonIndex = ResolveButtonIndex(target);
    if (buttonIndex < 0) {
      return;
    }

    SetHoveredButton(buttonIndex);
    SetPressedButton(buttonIndex);
    HandleButtonAction(buttonIndex, "pause_menu_click");
  }

  void OnSelect() {
    if (!isActiveAndEnabled || hoveredButtonIndex < 0) {
      return;
    }

    SetPressedButton(hoveredButtonIndex);
    HandleButtonAction(hoveredButtonIndex, "pause_menu_select");
  }

  void HandleButtonAction(int buttonIndex, string source) {
    switch (buttonIndex) {
      case 0:
        ShiftMajorStat(-1, source);
        break;
      case 1:
        ShiftMajorStat(1, source);
        break;
      case 2:
        SpendPoint(source);
        break;
    }
  }

  void ShiftMajorStat(int direction, string source) {
    var activeForm = EsperanzaForms.GetActive();
    var orderedMajorStats = FormStatIncreases.GetOrderedMajorStats(activeForm);
    if (orderedMajorStats.Count == 0) {
      return;
    }

    EnsureSelectedMajorStat(activeForm, orderedMajorStats, source);
    var previousIndex = selectedMajorIndex;
    var previousStat = orderedMajorStats[previousIndex];
    selectedMajorIndex = WrapIndex(selectedMajorIndex + direction, orderedMajorStats.Count);
    var nextStat = orderedMajorStats[selectedMajorIndex];

    RuntimeLog.Log(
      "[PauseMenuFormStatsView] Shifted major stat form='" + activeForm +
      "' source='" + (source ?? "") +
      "' prev_stat='" + previousStat +
      "' next_stat='" + nextStat +
      "' prev_index=" + previousIndex +
      " next_index=" + selectedMajorIndex
    );

    Refresh(source + "_shift");
  }

  void SpendPoint(string source) {
    var activeForm = EsperanzaForms.GetActive();
    var orderedMajorStats = FormStatIncreases.GetOrderedMajorStats(activeForm);
    if (orderedMajorStats.Count == 0) {
      return;
    }

    EnsureSelectedMajorStat(activeForm, orderedMajorStats, source);
    var selectedMajorStat = orderedMajorStats[selectedMajorIndex];
    var availablePoints = ResolveAvailableStatPoints(activeForm);
    if (availablePoints <= 0) {
      RuntimeLog.Log(
        "[PauseMenuFormStatsView] Ignored stat spend form='" + activeForm +
        "' stat='" + selectedMajorStat +
        "' source='" + (source ?? "") +
        "' available_points=" + availablePoints
      );
      Refresh(source + "_ignored");
      return;
    }

    if (characterState == null) {
      Debug.LogWarning("[PauseMenuFormStatsView] CharacterState was not found for stat spending.");
      return;
    }

    var spent = characterState.TryAddFormStatPoint(activeForm, selectedMajorStat, source);
    RuntimeLog.Log(
      "[PauseMenuFormStatsView] Spend attempt form='" + activeForm +
      "' stat='" + selectedMajorStat +
      "' source='" + (source ?? "") +
      "' available_before=" + availablePoints +
      " spent=" + (spent ? 1 : 0)
    );

    if (spent) {
      AddSessionSpendPreview(activeForm, selectedMajorStat);
      Refresh(source + "_spent");
    }
  }

  void Refresh(string source) {
    EnsureResolved();
    var activeForm = EsperanzaForms.GetActive();
    if (string.IsNullOrWhiteSpace(activeForm)) {
      ClearUi();
      return;
    }

    var orderedMajorStats = FormStatIncreases.GetOrderedMajorStats(activeForm);
    if (orderedMajorStats.Count == 0) {
      ClearUi();
      return;
    }

    EnsureSelectedMajorStat(activeForm, orderedMajorStats, source);
    var selectedMajorStat = orderedMajorStats[selectedMajorIndex];
    var majorValue = FormStatsValues.GetValue(activeForm, selectedMajorStat);
    var availablePoints = ResolveAvailableStatPoints(activeForm);
    var orderedMinorStats = FormStatIncreases.GetOrderedMinorStats(activeForm, selectedMajorStat);
    var sessionSpentCount = ResolveSessionSpendPreview(activeForm, selectedMajorStat);

    ApplyTheme(activeForm);
    ApplyText(statsLabelText, "Stats");
    ApplyText(statNameText, selectedMajorStat);
    ApplyText(statsNumText, majorValue.ToString(CultureInfo.InvariantCulture));
    ApplyText(statsAvailText, availablePoints.ToString(CultureInfo.InvariantCulture));
    ApplyMinorRows(majorValue, orderedMinorStats, sessionSpentCount);

    RuntimeLog.Log(
      "[PauseMenuFormStatsView] Refreshed source='" + (source ?? "") +
      "' form='" + activeForm +
      "' selected_stat='" + selectedMajorStat +
      "' selected_index=" + selectedMajorIndex +
      " major_value=" + majorValue +
      " available_points=" + availablePoints +
      " session_spent=" + sessionSpentCount +
      " minor_count=" + orderedMinorStats.Count
    );
  }

  void ApplyMinorRows(int majorValue, List<KeyValuePair<string, float>> orderedMinorStats, int sessionSpentCount) {
    for (var i = 0; i < statRows.Length; i++) {
      var row = statRows[i];
      if (row == null) {
        continue;
      }

      if (orderedMinorStats != null && i < orderedMinorStats.Count) {
        var minorStat = orderedMinorStats[i];
        var currentValue = majorValue * minorStat.Value;
        var increaseValue = sessionSpentCount > 0 ? minorStat.Value * sessionSpentCount : 0f;
        ApplyText(row.nameText, minorStat.Key);
        ApplyText(row.valueText, FormatNumber(currentValue));
        ApplyText(row.increaseText, sessionSpentCount > 0 ? FormatSignedNumber(increaseValue) : "");
        continue;
      }

      ClearRow(row);
    }
  }

  void ClearUi() {
    ApplyText(statsLabelText, "Stats");
    ApplyText(statNameText, "");
    ApplyText(statsNumText, "");
    ApplyText(statsAvailText, "");

    for (var i = 0; i < statRows.Length; i++) {
      ClearRow(statRows[i]);
    }
  }

  void ClearRow(StatRow row) {
    if (row == null) {
      return;
    }

    ApplyText(row.nameText, "");
    ApplyText(row.valueText, "");
    ApplyText(row.increaseText, "");
  }

  void ResetSelectedMajorStat(string formName, string source, bool force = false) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      return;
    }

    if (!force &&
        string.Equals(selectedForm, resolvedForm, StringComparison.OrdinalIgnoreCase) &&
        selectedMajorIndex == 0) {
      return;
    }

    selectedForm = resolvedForm;
    selectedMajorIndex = 0;
    RuntimeLog.Log(
      "[PauseMenuFormStatsView] Reset selected major stat form='" + resolvedForm +
      "' source='" + (source ?? "") +
      "' selected_index=" + selectedMajorIndex
    );
  }

  void EnsureSelectedMajorStat(string activeForm, List<string> orderedMajorStats, string source) {
    if (!string.Equals(selectedForm, activeForm, StringComparison.OrdinalIgnoreCase)) {
      ResetSelectedMajorStat(activeForm, source, force: true);
    }

    if (orderedMajorStats == null || orderedMajorStats.Count == 0) {
      selectedMajorIndex = 0;
      return;
    }

    var clampedIndex = Mathf.Clamp(selectedMajorIndex, 0, orderedMajorStats.Count - 1);
    if (clampedIndex != selectedMajorIndex) {
      RuntimeLog.Log(
        "[PauseMenuFormStatsView] Clamped selected major index form='" + activeForm +
        "' source='" + (source ?? "") +
        "' prev_index=" + selectedMajorIndex +
        " next_index=" + clampedIndex
      );
      selectedMajorIndex = clampedIndex;
    }
  }

  int ResolveAvailableStatPoints(string formName) {
    if (characterState != null) {
      return characterState.GetAvailableStatPoints(formName);
    }

    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      return 0;
    }

    var progress = EsperanzaForms.EnsureProgress(resolvedForm);
    var earnedPoints = Mathf.Max(0, (progress != null ? progress.level : EsperanzaForms.DefaultLevel) - EsperanzaForms.DefaultLevel);
    var spentPoints = 0;
    var orderedMajorStats = FormStatIncreases.GetOrderedMajorStats(resolvedForm);
    for (var i = 0; i < orderedMajorStats.Count; i++) {
      var statName = orderedMajorStats[i];
      var currentValue = FormStatsValues.GetValue(resolvedForm, statName);
      var defaultValue = FormStatsValues.GetDefaultValue(resolvedForm, statName);
      spentPoints += Mathf.Max(0, currentValue - defaultValue);
    }

    return Mathf.Max(0, earnedPoints - spentPoints);
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

      if (!string.Equals(sprite.labelPrefix, formName, StringComparison.Ordinal)) {
        sprite.SetLabelPrefix(formName);
        sprite.ForceUpdateSpriteAndNormal();
      }
    }
  }

  void SetHoveredButton(int buttonIndex) {
    hoveredButtonIndex = buttonIndex;
    if (statsButtons == null) {
      return;
    }

    statsButtons.SetHoverIndex(buttonIndex);
    if (buttonIndex < 0) {
      statsButtons.SetActiveIndex(-1);
    }
  }

  void SetPressedButton(int buttonIndex) {
    if (statsButtons == null) {
      return;
    }

    statsButtons.SetActiveIndex(buttonIndex);
  }

  void AddSessionSpendPreview(string formName, string majorStat) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    var resolvedStat = FormStatIncreases.ResolveMajorStatKey(resolvedForm, majorStat);
    if (string.IsNullOrWhiteSpace(resolvedForm) || string.IsNullOrWhiteSpace(resolvedStat)) {
      return;
    }

    if (!sessionSpendPreview.TryGetValue(resolvedForm, out var formPreview) || formPreview == null) {
      formPreview = new Dictionary<string, int>(StringComparer.Ordinal);
      sessionSpendPreview[resolvedForm] = formPreview;
    }

    if (!formPreview.ContainsKey(resolvedStat)) {
      formPreview[resolvedStat] = 0;
    }

    formPreview[resolvedStat] += 1;
    RuntimeLog.Log(
      "[PauseMenuFormStatsView] Session preview updated form='" + resolvedForm +
      "' stat='" + resolvedStat +
      "' session_points=" + formPreview[resolvedStat]
    );
  }

  int ResolveSessionSpendPreview(string formName, string majorStat) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    var resolvedStat = FormStatIncreases.ResolveMajorStatKey(resolvedForm, majorStat);
    if (string.IsNullOrWhiteSpace(resolvedForm) || string.IsNullOrWhiteSpace(resolvedStat)) {
      return 0;
    }

    if (!sessionSpendPreview.TryGetValue(resolvedForm, out var formPreview) || formPreview == null) {
      return 0;
    }

    return formPreview.TryGetValue(resolvedStat, out var previewCount) ? Mathf.Max(0, previewCount) : 0;
  }

  void ClearSessionSpendPreview(string source) {
    if (sessionSpendPreview.Count == 0) {
      return;
    }

    RuntimeLog.Log(
      "[PauseMenuFormStatsView] Cleared session preview source='" + (source ?? "") +
      "' form_count=" + sessionSpendPreview.Count
    );
    sessionSpendPreview.Clear();
  }

  int ResolveButtonIndex(GameObject target) {
    if (target == null) {
      return -1;
    }
    if (statsButtons != null && statsButtons.buttons.Count > 0) {
      var directIndex = statsButtons.buttons.IndexOf(target);
      if (directIndex >= 0) {
        return directIndex;
      }

      var targetTransform = target.transform;
      for (var i = 0; i < statsButtons.buttons.Count; i++) {
        var button = statsButtons.buttons[i];
        if (button == null) {
          continue;
        }
        if (targetTransform.IsChildOf(button.transform)) {
          return i;
        }
      }
    }
    if (MatchesButtonTarget(target, statsLeftButton)) {
      return 0;
    }
    if (MatchesButtonTarget(target, statsRightButton)) {
      return 1;
    }
    if (MatchesButtonTarget(target, statsPlusButton)) {
      return 2;
    }
    return -1;
  }

  static bool MatchesButtonTarget(GameObject target, GameObject button) {
    if (target == null || button == null) {
      return false;
    }

    if (target == button) {
      return true;
    }

    return target.transform.IsChildOf(button.transform);
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

  static string FormatNumber(float value) {
    if (Mathf.Approximately(value, Mathf.Round(value))) {
      return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
    }

    return value.ToString("0.##", CultureInfo.InvariantCulture);
  }

  static string FormatSignedNumber(float value) {
    var prefix = value >= 0f ? "+" : "";
    return prefix + FormatNumber(value);
  }

  static int WrapIndex(int index, int count) {
    if (count <= 0) {
      return 0;
    }

    var wrapped = index % count;
    return wrapped < 0 ? wrapped + count : wrapped;
  }

  static FontText FindFontText(Transform root, string nodeName) {
    if (root == null || string.IsNullOrWhiteSpace(nodeName)) {
      return null;
    }

    var target = FindChildRecursive(root, nodeName);
    return target != null ? target.GetComponentInChildren<FontText>(includeInactive: true) : null;
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
