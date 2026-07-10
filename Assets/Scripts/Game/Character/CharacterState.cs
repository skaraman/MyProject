using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CharacterState : MonoBehaviour {
  public int level = 0;

  private Action offLoadGame;
  private GearController gearController;

  // Cache list to avoid allocations
  private readonly List<string> cachedKeys = new();

  static bool ShouldLogLoadStateDebug() {
    if (!SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs) {
      return false;
    }
    return Application.isEditor || Debug.isDebugBuild;
  }

  void EnsureRuntimeReferences() {
    if (gearController == null) {
      gearController = GetComponent<GearController>();
    }
  }

  void Awake() {
    EnsureRuntimeReferences();
  }

  void Start() {
    offLoadGame = MessageBus.On(CharacterMessageTopics.LoadGame, LoadState);
    EnsureRuntimeReferences();
  }

  void OnDestroy() {
    offLoadGame?.Invoke();
#if UNITY_EDITOR
    if (!Application.isPlaying) {
      Selection.activeObject = null;
    }
#endif
  }

  public void LoadState() {
    EnsureRuntimeReferences();
    ResetRuntimeState();

    var loadedForms = SaveSlotManager.Load(SaveKeys.Forms);
    var loadedStats = SaveSlotManager.Load(SaveKeys.Stats);
    if (ShouldLogLoadStateDebug()) {
      Debug.Log(
        "[CharacterState][LoadState] stage=begin" +
        " slot=" + SaveSlotManager.slot +
        " forms_keys=" + (loadedForms != null ? loadedForms.Count : 0) +
        " stats_keys=" + (loadedStats != null ? loadedStats.Count : 0) +
        " gear_controller=" + (gearController != null ? 1 : 0)
      );
    }

    ApplyLoadedFormState(loadedForms);
    ApplyLoadedStatsState(loadedStats);
    DialogController.LoadState("character_load_state");

    gearController?.LoadGear(publishReady: false);
    GatherAllStatValues();
    SaveFormsState();
    MessageBus.Send(CharacterMessageTopics.DialogStateReady, "load_state");
    NotifyFormStateChanged(EsperanzaForms.GetActive(), "load_state");

    if (ShouldLogLoadStateDebug()) {
      Debug.Log(
        "[CharacterState][LoadState] stage=complete" +
        " slot=" + SaveSlotManager.slot +
        " active_form=" + EsperanzaForms.GetActive() +
        " level=" + level +
        " gear_controller=" + (gearController != null ? 1 : 0)
      );
    }
  }

  public void InitializeRuntimeStateForNewGame() {
    EnsureRuntimeReferences();
    ResetRuntimeState();
    DialogController.SaveState("new_game");
    if (ShouldLogLoadStateDebug()) {
      Debug.Log(
        "[CharacterState][LoadState] stage=new_game_begin" +
        " slot=" + SaveSlotManager.slot +
        " active_form=" + EsperanzaForms.GetActive() +
        " gear_controller=" + (gearController != null ? 1 : 0)
      );
    }

    gearController?.LoadGear(publishReady: false);
    GatherAllStatValues();
    SaveFormsState();
    MessageBus.Send(CharacterMessageTopics.DialogStateReady, "new_game");
    NotifyFormStateChanged(EsperanzaForms.GetActive(), "new_game");

    if (ShouldLogLoadStateDebug()) {
      Debug.Log(
        "[CharacterState][LoadState] stage=new_game_complete" +
        " slot=" + SaveSlotManager.slot +
        " active_form=" + EsperanzaForms.GetActive() +
        " level=" + level +
        " gear_controller=" + (gearController != null ? 1 : 0)
      );
    }
  }

  public bool SetActiveForm(string formName, string source = "runtime") {
    EnsureRuntimeReferences();
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      Debug.LogWarning("[CharacterState] Ignored unknown form change request='" + (formName ?? "") + "'");
      return false;
    }

    var previousForm = EsperanzaForms.GetActive();
    if (string.Equals(previousForm, resolvedForm, StringComparison.OrdinalIgnoreCase)) {
      Debug.Log(
        "[CharacterState][SetActiveForm] Ignored no-op request current='" + previousForm +
        "' source='" + (source ?? "") + "'"
      );
      return false;
    }

    EsperanzaForms.SetActive(resolvedForm);
    var saved = SaveFormsState();
    RuntimeContentPackResolver.ConfigureForCurrentRuntimeState("form_change:" + (source ?? ""));

    if (gearController != null) {
      gearController.RefreshGear();
    }

    NotifyFormStateChanged(resolvedForm, source);
    Debug.Log(
      "[CharacterState][SetActiveForm] previous='" + previousForm +
      "' next='" + resolvedForm +
      "' source='" + (source ?? "") +
      "' saved=" + (saved ? 1 : 0) +
      " gear_controller=" + (gearController != null ? 1 : 0)
    );

    return true;
  }

  public int GrantActiveFormXp(int amount, string source = "runtime") {
    return GrantFormXp(EsperanzaForms.GetActive(), amount, source);
  }

  public int GrantFormXp(string formName, int amount, string source = "runtime") {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      Debug.LogWarning("[CharacterState] Ignored XP grant for unknown form='" + (formName ?? "") + "'");
      return 0;
    }

    if (amount <= 0) {
      Debug.LogWarning(
        "[CharacterState][GrantFormXp] Ignored non-positive XP amount=" + amount +
        " form='" + resolvedForm + "'" +
        " source='" + (source ?? "") + "'"
      );
      return 0;
    }

    var progress = EsperanzaForms.EnsureProgress(resolvedForm);
    if (progress == null) {
      return 0;
    }

    var previousLevel = progress.level;
    var previousCurrentXp = progress.currentXp;
    var previousNextLevelXp = progress.nextLevelXp;

    progress.currentXp += amount;
    while (progress.currentXp >= progress.nextLevelXp) {
      var thresholdBeforeLevel = progress.nextLevelXp;
      progress.currentXp -= thresholdBeforeLevel;
      progress.level += 1;
      progress.nextLevelXp = ResolveNextLevelXp(thresholdBeforeLevel, resolvedForm, progress.level, source);
    }

    var levelsGained = progress.level - previousLevel;
    var saved = SaveFormsState();
    MessageBus.Send(CharacterMessageTopics.FormProgressChanged, resolvedForm);

    Debug.Log(
      "[CharacterState][GrantFormXp] form='" + resolvedForm +
      "' amount=" + amount +
      " source='" + (source ?? "") +
      "' prev_level=" + previousLevel +
      " next_level=" + progress.level +
      " prev_current_xp=" + previousCurrentXp +
      " next_current_xp=" + progress.currentXp +
      " prev_next_level_xp=" + previousNextLevelXp +
      " next_next_level_xp=" + progress.nextLevelXp +
      " levels_gained=" + levelsGained +
      " saved=" + (saved ? 1 : 0)
    );

    return levelsGained;
  }

  public FormProgressState GetFormProgress(string formName) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      return null;
    }

    return EsperanzaForms.GetProgressCopy(resolvedForm);
  }

  public int GetAvailableStatPoints(string formName) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      return 0;
    }

    FormStatsValues.EnsureForm(resolvedForm);
    var progress = EsperanzaForms.EnsureProgress(resolvedForm);
    var earnedPoints = Mathf.Max(0, (progress != null ? progress.level : EsperanzaForms.DefaultLevel) - EsperanzaForms.DefaultLevel);
    var spentPoints = GetSpentStatPoints(resolvedForm);
    return Mathf.Max(0, earnedPoints - spentPoints);
  }

  public bool TryAddFormStatPoint(string formName, string majorStat, string source = "runtime") {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    var resolvedStat = FormStatIncreases.ResolveMajorStatKey(resolvedForm, majorStat);
    if (string.IsNullOrWhiteSpace(resolvedForm) || string.IsNullOrWhiteSpace(resolvedStat)) {
      Debug.LogWarning(
        "[CharacterState][TryAddFormStatPoint] Missing stat target form='" + (formName ?? "") +
        "' stat='" + (majorStat ?? "") + "'"
      );
      return false;
    }

    var availablePoints = GetAvailableStatPoints(resolvedForm);
    if (availablePoints <= 0) {
      Debug.Log(
        "[CharacterState][TryAddFormStatPoint] Ignored spend form='" + resolvedForm +
        "' stat='" + resolvedStat +
        "' source='" + (source ?? "") +
        "' available_points=" + availablePoints
      );
      return false;
    }

    var previousValue = FormStatsValues.GetValue(resolvedForm, resolvedStat);
    AddStats(resolvedForm, resolvedStat, 1, source);
    var nextValue = FormStatsValues.GetValue(resolvedForm, resolvedStat);
    var remainingPoints = GetAvailableStatPoints(resolvedForm);

    Debug.Log(
      "[CharacterState][TryAddFormStatPoint] form='" + resolvedForm +
      "' stat='" + resolvedStat +
      "' source='" + (source ?? "") +
      "' prev_value=" + previousValue +
      " next_value=" + nextValue +
      " remaining_points=" + remainingPoints
    );
    return true;
  }

  public void AddStats(string form, string stat, int amount) {
    AddStats(form, stat, amount, "runtime");
  }

  void AddStats(string form, string stat, int amount, string source) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(form);
    var resolvedStat = FormStatIncreases.ResolveMajorStatKey(resolvedForm, stat);
    if (string.IsNullOrWhiteSpace(resolvedForm) || string.IsNullOrWhiteSpace(resolvedStat)) {
      Debug.LogWarning(
        "[CharacterState][AddStats] Missing stat target form='" + (form ?? "") +
        "' stat='" + (stat ?? "") + "'"
      );
      return;
    }

    if (amount == 0) {
      Debug.LogWarning(
        "[CharacterState][AddStats] Ignored zero amount form='" + resolvedForm +
        "' stat='" + resolvedStat +
        "' source='" + (source ?? "") + "'"
      );
      return;
    }

    FormStatsValues.EnsureForm(resolvedForm);
    if (!FormStatsValues.values.TryGetValue(resolvedForm, out var stats) || !stats.ContainsKey(resolvedStat)) {
      Debug.LogWarning(
        "[CharacterState][AddStats] Missing stat target form='" + resolvedForm +
        "' stat='" + resolvedStat + "'"
      );
      return;
    }

    var previousValue = stats[resolvedStat];
    stats[resolvedStat] += amount;
    GatherAllStatValues();
    var saved = SaveStatsState();
    MessageBus.Send(CharacterMessageTopics.FormStatsChanged, resolvedForm);

    Debug.Log(
      "[CharacterState][AddStats] form='" + resolvedForm +
      "' stat='" + resolvedStat +
      "' amount=" + amount +
      " source='" + (source ?? "") +
      "' prev_value=" + previousValue +
      " next_value=" + stats[resolvedStat] +
      " aggregate_level=" + level +
      " saved=" + (saved ? 1 : 0)
    );
  }

  public void GatherAllStatValues() {
    FormStatsValues.EnsureAllKnownForms();
    level = 0;

    cachedKeys.Clear();
    cachedKeys.AddRange(AllStatValues.Esperanza.Keys);
    for (var i = 0; i < cachedKeys.Count; i++) {
      AllStatValues.Esperanza[cachedKeys[i]] = 0f;
    }

    foreach (var form in FormStatIncreases.increases) {
      FormStatsValues.EnsureForm(form.Key);
      foreach (var majorStat in form.Value) {
        if (!FormStatsValues.values[form.Key].ContainsKey(majorStat.Key)) {
          continue;
        }

        foreach (var minorStat in majorStat.Value) {
          AllStatValues.Esperanza[minorStat.Key] += minorStat.Value * FormStatsValues.values[form.Key][majorStat.Key];
        }
      }
    }

    foreach (var form in FormStatsValues.values) {
      foreach (var majorStat in form.Value) {
        level += majorStat.Value;
      }
    }
  }

  void ResetRuntimeState() {
    EsperanzaForms.ResetRuntimeState();
    FormStatsValues.ResetToDefaults();
    EquippedItems.ResetToDefaults();
    DialogController.ResetRuntimeState("character_reset");
  }

  void ApplyLoadedFormState(SaveData loadedForms) {
    if (loadedForms == null || loadedForms.Count == 0) {
      return;
    }

    if (loadedForms.HasPrefix(SaveKeys.UnlockedForms)) {
      var unlockedForms = loadedForms.GetComplex<Dictionary<string, int>>(SaveKeys.UnlockedForms);
      EsperanzaForms.ApplyUnlockedState(unlockedForms);
    } else {
      EsperanzaForms.ApplyUnlockedState(null);
    }

    if (loadedForms.HasPrefix(SaveKeys.FormProgress)) {
      var formProgress = loadedForms.GetComplex<Dictionary<string, FormProgressState>>(SaveKeys.FormProgress);
      EsperanzaForms.ApplyProgressState(formProgress);
    } else {
      EsperanzaForms.ApplyProgressState(null);
    }

    var requestedActiveForm = loadedForms.ContainsKey(SaveKeys.ActiveForm)
      ? Convert.ToString(loadedForms[SaveKeys.ActiveForm])
      : EsperanzaForms.GetActive();
    EsperanzaForms.SetActive(requestedActiveForm);
  }

  void ApplyLoadedStatsState(SaveData loadedStats) {
    if (loadedStats == null || loadedStats.Count == 0 || !loadedStats.HasPrefix(SaveKeys.FormStats)) {
      return;
    }

    var loadedFormStats = loadedStats.GetComplex<Dictionary<string, Dictionary<string, int>>>(SaveKeys.FormStats);
    if (loadedFormStats == null) {
      return;
    }

    foreach (var form in loadedFormStats) {
      var resolvedForm = EsperanzaForms.ResolveFormKey(form.Key);
      if (string.IsNullOrWhiteSpace(resolvedForm) || form.Value == null) {
        continue;
      }

      FormStatsValues.EnsureForm(resolvedForm);
      foreach (var stat in form.Value) {
        var resolvedStat = FormStatIncreases.ResolveMajorStatKey(resolvedForm, stat.Key);
        if (string.IsNullOrWhiteSpace(resolvedStat)) {
          continue;
        }

        var defaultValue = FormStatsValues.GetDefaultValue(resolvedForm, resolvedStat);
        FormStatsValues.values[resolvedForm][resolvedStat] = Mathf.Max(stat.Value, defaultValue);
      }
    }
  }

  bool SaveFormsState() {
    try {
      var formsSave = new SaveData {
        [SaveKeys.ActiveForm] = EsperanzaForms.GetActive()
      };
      formsSave.SetComplex(SaveKeys.UnlockedForms, EsperanzaForms.GetUnlockedSnapshot());
      formsSave.SetComplex(SaveKeys.FormProgress, EsperanzaForms.GetProgressSnapshot());
      SaveSlotManager.Save(SaveKeys.Forms, formsSave);
      return true;
    }
    catch (Exception e) {
      Debug.LogWarning("[CharacterState][SaveFormsState] Failed to save forms state: " + e.Message);
      return false;
    }
  }

  bool SaveStatsState() {
    try {
      var statsSave = new SaveData();
      statsSave.SetComplex(SaveKeys.FormStats, FormStatsValues.values);
      SaveSlotManager.Save(SaveKeys.Stats, statsSave);
      return true;
    }
    catch (Exception e) {
      Debug.LogWarning("[CharacterState][SaveStatsState] Failed to save stats state: " + e.Message);
      return false;
    }
  }

  void NotifyFormStateChanged(string resolvedForm, string source) {
    Debug.Log(
      "[CharacterState][NotifyFormStateChanged] form='" + (resolvedForm ?? "") +
      "' source='" + (source ?? "") + "'"
    );
    MessageBus.Send(CharacterMessageTopics.FormChanged, resolvedForm);
    MessageBus.Send(CharacterMessageTopics.GearReady, resolvedForm);
    MessageBus.Send(CharacterMessageTopics.FormProgressChanged, resolvedForm);
  }

  int ResolveNextLevelXp(int currentThreshold, string formName, int nextLevel, string source) {
    var useSilverRatio = UnityEngine.Random.value < 0.5f;
    var ratio = useSilverRatio ? EsperanzaForms.SilverRatio : EsperanzaForms.GoldenRatio;
    var nextThreshold = Mathf.CeilToInt((float)(currentThreshold * ratio));
    Debug.Log(
      "[CharacterState][LevelCurve] form='" + (formName ?? "") +
      "' next_level=" + nextLevel +
      " source='" + (source ?? "") +
      "' ratio='" + (useSilverRatio ? "silver" : "gold") +
      "' current_threshold=" + currentThreshold +
      " next_threshold=" + nextThreshold
    );
    return Mathf.Max(nextThreshold, currentThreshold + 1);
  }

  int GetSpentStatPoints(string resolvedForm) {
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      return 0;
    }

    FormStatsValues.EnsureForm(resolvedForm);
    if (!FormStatsValues.values.TryGetValue(resolvedForm, out var stats) || stats == null) {
      return 0;
    }

    var spentPoints = 0;
    foreach (var stat in stats) {
      var defaultValue = FormStatsValues.GetDefaultValue(resolvedForm, stat.Key);
      spentPoints += Mathf.Max(0, stat.Value - defaultValue);
    }

    return spentPoints;
  }
}
