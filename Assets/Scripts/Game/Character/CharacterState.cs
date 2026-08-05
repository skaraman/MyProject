using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CharacterState : MonoBehaviour {
  static readonly Unity.Profiling.ProfilerMarker NewGameResetProfilerMarker =
    new("CharacterState.NewGame.Reset");
  static readonly Unity.Profiling.ProfilerMarker NewGameLoadGearProfilerMarker =
    new("CharacterState.NewGame.LoadGear");
  static readonly Unity.Profiling.ProfilerMarker NewGameCreateDefaultsProfilerMarker =
    new("CharacterState.NewGame.CreateDefaults");
  static readonly Unity.Profiling.ProfilerMarker NewGameGatherStatsProfilerMarker =
    new("CharacterState.NewGame.GatherStats");
  static CharacterState runtimeInstance;

  public int level = 0;

  [Header("Debug")]
  [SerializeField, Tooltip("Treat every known form as unlocked for availability checks without bulk-unlocking saved progression.")]
  private bool debugUnlockAllForms = true;

  private Action offLoadGame;
  private GearController gearController;
  private bool formsSavePending;
  private int formsSaveSlot = -1;
  private readonly EndlessNumber currentHealth = new();
  private readonly EndlessNumber maximumHealthSnapshot = new();
  private readonly EndlessNumber lastKnownMaximumHealth = new();
  private bool currentHealthInitialized;

  // Cache list to avoid allocations
  private readonly List<string> cachedKeys = new();

  public static bool DebugUnlockAllForms =>
    runtimeInstance != null && runtimeInstance.debugUnlockAllForms;

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

  public EndlessNumber CurrentHealth {
    get {
      if (!currentHealthInitialized) {
        SynchronizeCurrentHealthToMaximum();
      }
      return currentHealth;
    }
  }

  public EndlessNumber MaximumHealth => ResolveMaximumHealth();

  public EndlessNumber ApplyDamage(EndlessNumber damage, string hitId = "") {
    var resolvedDamage = EndlessNumber.Max(damage ?? new EndlessNumber(), new EndlessNumber());
    if (!resolvedDamage.IsPositive) {
      return new EndlessNumber();
    }

    SynchronizeCurrentHealthToMaximum();
    var healthBefore = currentHealth.Copy();
    currentHealth.Set(EndlessNumber.Max(healthBefore - resolvedDamage, new EndlessNumber()));
    var actualDamage = healthBefore - currentHealth;
    if (!actualDamage.IsPositive) {
      return actualDamage;
    }

    MessageBus.Send(
      CharacterMessageTopics.Damaged,
      new CharacterDamageEvent(
        actualDamage,
        currentHealth,
        ResolveMaximumHealth(),
        hitId
      )
    );
    return actualDamage;
  }

  public void RestoreHealthToMaximum() {
    var maximumHealth = ResolveMaximumHealth();
    currentHealth.Set(maximumHealth);
    lastKnownMaximumHealth.Set(maximumHealth);
    currentHealthInitialized = true;
  }

  void Awake() {
    runtimeInstance = this;
    EnsureRuntimeReferences();
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetRuntimeInstance() {
    runtimeInstance = null;
  }

  void Start() {
    offLoadGame = MessageBus.On(CharacterMessageTopics.LoadGame, LoadState);
    EnsureRuntimeReferences();

    var lightObj = new GameObject("EsperLocalLight");
    lightObj.transform.SetParent(this.transform);
    lightObj.transform.localPosition = new Vector3(0, -1.78f, 0);
    var light2D = lightObj.AddComponent<UnityEngine.Rendering.Universal.Light2D>();
    light2D.lightType = UnityEngine.Rendering.Universal.Light2D.LightType.Point;
    light2D.intensity = 0.36f;
    light2D.pointLightOuterRadius = 2.87f;
    light2D.pointLightInnerRadius = 0f;
    light2D.pointLightOuterAngle = 360f;
    light2D.pointLightInnerAngle = 0f;
    light2D.falloffIntensity = 0.572f;
    light2D.color = new Color(1f, 0.95f, 0.85f);
  }

  void OnDestroy() {
    FlushPendingFormsSave();
    ContentEpisodeProgression.FlushPendingSave();
    if (runtimeInstance == this) {
      runtimeInstance = null;
    }
    offLoadGame?.Invoke();
#if UNITY_EDITOR
    if (!Application.isPlaying) {
      Selection.activeObject = null;
    }
#endif
  }

  void OnApplicationPause(bool isPaused) {
    if (isPaused) {
      FlushPendingFormsSave();
      ContentEpisodeProgression.FlushPendingSave();
    }
  }

  public void LoadState() {
    EnsureRuntimeReferences();
    ResetRuntimeState();

    var loadedForms = SaveSlotManager.Load(SaveKeys.Forms);
    var loadedStats = SaveSlotManager.Load(SaveKeys.Stats);
    if (ShouldLogLoadStateDebug()) {
      RuntimeLog.Log(
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
      RuntimeLog.Log(
        "[CharacterState][LoadState] stage=complete" +
        " slot=" + SaveSlotManager.slot +
        " active_form=" + EsperanzaForms.GetActive() +
        " level=" + level +
        " gear_controller=" + (gearController != null ? 1 : 0)
      );
    }
  }

  void SaveCreatedDefaultGear() {
    var gearSave = new SaveData();
    gearSave.SetComplex(SaveKeys.AllGear, EquippedItems.AllGearForms);
    SaveSlotManager.Save(SaveKeys.EquippedGear, gearSave);
  }

  public void InitializeRuntimeStateForNewGame() {
    using (NewGameResetProfilerMarker.Auto()) {
      EnsureRuntimeReferences();
      ResetRuntimeState();
      DialogController.SaveState("new_game");
    }
    if (ShouldLogLoadStateDebug()) {
      RuntimeLog.Log(
        "[CharacterState][LoadState] stage=new_game_begin" +
        " slot=" + SaveSlotManager.slot +
        " active_form=" + EsperanzaForms.GetActive() +
        " gear_controller=" + (gearController != null ? 1 : 0)
      );
    }

    using (NewGameLoadGearProfilerMarker.Auto()) {
      gearController?.LoadGear(publishReady: false);
    }
    using (NewGameCreateDefaultsProfilerMarker.Auto()) {
      EquippedItems.RandomizeDefaultBoostsForNewGame();
      SaveCreatedDefaultGear();
    }
    using (NewGameGatherStatsProfilerMarker.Auto()) {
      GatherAllStatValues();
    }
    SaveFormsState();
    MessageBus.Send(CharacterMessageTopics.DialogStateReady, "new_game");
    NotifyFormStateChanged(EsperanzaForms.GetActive(), "new_game");

    if (ShouldLogLoadStateDebug()) {
      RuntimeLog.Log(
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
      RuntimeLog.Log(
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
    RuntimeLog.Log(
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
      progress.nextLevelXp = ResolveNextLevelXp(
        thresholdBeforeLevel,
        "form",
        resolvedForm,
        progress.level,
        source
      );
    }

    var levelsGained = progress.level - previousLevel;
    QueueFormsSave();
    MessageBus.Send(CharacterMessageTopics.FormProgressChanged, resolvedForm);
    MessageBus.Send(
      CharacterMessageTopics.FormXpGained,
      new XpProgressGain(
        resolvedForm,
        previousLevel,
        previousCurrentXp,
        previousNextLevelXp,
        progress.level,
        progress.currentXp,
        progress.nextLevelXp
      )
    );

    if (ShouldLogLoadStateDebug()) {
      RuntimeLog.Log(
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
        " save_queued=1"
      );
    }

    return levelsGained;
  }

  public FormProgressState GetFormProgress(string formName) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      return null;
    }

    return EsperanzaForms.GetProgressCopy(resolvedForm);
  }

  public int GrantAbilityXp(string abilityName, int amount, string source = "runtime") {
    if (!EsperanzaAbilities.TryResolveAbilityAnimation(abilityName, out var animationName)) {
      Debug.LogWarning("[CharacterState] Ignored XP grant for unknown ability='" + (abilityName ?? "") + "'");
      return 0;
    }

    if (amount <= 0) {
      Debug.LogWarning(
        "[CharacterState][GrantAbilityXp] Ignored non-positive XP amount=" + amount +
        " ability='" + animationName + "'" +
        " source='" + (source ?? "") + "'"
      );
      return 0;
    }

    var progress = EsperanzaAbilities.EnsureProgress(animationName);
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
      progress.nextLevelXp = ResolveNextLevelXp(
        thresholdBeforeLevel,
        "ability",
        animationName,
        progress.level,
        source
      );
    }

    var levelsGained = progress.level - previousLevel;
    QueueFormsSave();
    MessageBus.Send(CharacterMessageTopics.AbilityProgressChanged, animationName);
    MessageBus.Send(
      CharacterMessageTopics.AbilityXpGained,
      new XpProgressGain(
        animationName,
        previousLevel,
        previousCurrentXp,
        previousNextLevelXp,
        progress.level,
        progress.currentXp,
        progress.nextLevelXp
      )
    );

    if (ShouldLogLoadStateDebug()) {
      RuntimeLog.Log(
        "[CharacterState][GrantAbilityXp] ability='" + animationName +
        "' amount=" + amount +
        " source='" + (source ?? "") +
        "' prev_level=" + previousLevel +
        " next_level=" + progress.level +
        " prev_current_xp=" + previousCurrentXp +
        " next_current_xp=" + progress.currentXp +
        " prev_next_level_xp=" + previousNextLevelXp +
        " next_next_level_xp=" + progress.nextLevelXp +
        " levels_gained=" + levelsGained +
        " save_queued=1"
      );
    }

    return levelsGained;
  }

  public AbilityProgressState GetAbilityProgress(string abilityName) {
    return EsperanzaAbilities.GetProgressCopy(abilityName);
  }

  public void FlushPendingProgressSave() {
    FlushPendingFormsSave();
  }

  public static bool FlushPendingProgressBeforeSlotChange() {
    return runtimeInstance == null || runtimeInstance.TryFlushPendingFormsSave();
  }

  public bool SetAbilityLoadout(string formName, IList<string> abilities, string source = "runtime") {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      return false;
    }
    if (!EsperanzaAbilityLoadouts.SetAbilities(resolvedForm, abilities)) {
      return false;
    }

    var saved = SaveFormsState();
    foreach (var changedForm in EsperanzaForms.KnownForms) {
      MessageBus.Send(CharacterMessageTopics.AbilityLoadoutChanged, changedForm);
    }
    RuntimeLog.Log(
      "[CharacterState][SetAbilityLoadout] form='" + resolvedForm +
      "' ability_count=" + EsperanzaAbilityLoadouts.GetAbilitiesCopy(resolvedForm).Count +
      " source='" + (source ?? "") +
      "' saved=" + (saved ? 1 : 0)
    );
    return true;
  }

  public bool SetComboMove(
    string formName,
    int comboIndex,
    int moveIndex,
    string abilityName,
    string source = "runtime"
  ) {
    if (!EsperanzaComboLoadouts.SetMove(formName, comboIndex, moveIndex, abilityName)) {
      return false;
    }

    var saved = SaveFormsState();
    RuntimeLog.Log(
      "[CharacterState][SetComboMove] form='" + EsperanzaForms.ResolveFormKey(formName) +
      "' combo=" + (comboIndex + 1) +
      " move=" + (moveIndex + 1) +
      " ability='" + abilityName +
      "' source='" + (source ?? "") +
      "' saved=" + (saved ? 1 : 0)
    );
    return true;
  }

  public bool MoveAbilityToForm(
    string abilityName,
    string targetFormName,
    int targetIndex,
    string source = "runtime"
  ) {
    if (!EsperanzaAbilityLoadouts.MoveAbility(
          abilityName,
          targetFormName,
          targetIndex,
          out var changedForms
        )) {
      return false;
    }

    var saved = SaveFormsState();
    for (var i = 0; i < changedForms.Count; i++) {
      MessageBus.Send(CharacterMessageTopics.AbilityLoadoutChanged, changedForms[i]);
    }

    RuntimeLog.Log(
      "[CharacterState][MoveAbilityToForm] ability='" + (abilityName ?? "") +
      "' target_form='" + (targetFormName ?? "") +
      "' target_index=" + targetIndex +
      " source='" + (source ?? "") +
      "' saved=" + (saved ? 1 : 0)
    );
    return true;
  }

  public int GetAvailableStatPoints() {
    var earnedPoints = GetEarnedStatPoints();
    var spentPoints = GetSpentStatPoints();
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

    var availablePoints = GetAvailableStatPoints();
    if (availablePoints <= 0) {
      RuntimeLog.Log(
        "[CharacterState][TryAddFormStatPoint] Ignored spend form='" + resolvedForm +
        "' stat='" + resolvedStat +
        "' source='" + (source ?? "") +
        "' available_points=" + availablePoints
      );
      return false;
    }

    var previousValue = FormStatsValues.GetValue(resolvedForm, resolvedStat);
    AddStats(resolvedForm, resolvedStat, 1, source);
    SaveFormsState();
    var nextValue = FormStatsValues.GetValue(resolvedForm, resolvedStat);
    var remainingPoints = GetAvailableStatPoints();

    RuntimeLog.Log(
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

    RuntimeLog.Log(
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
      var statName = cachedKeys[i];
      if (AllStatValues.Esperanza.TryGetValue(statName, out var statValue) && statValue != null) {
        statValue.Reset();
      } else {
        AllStatValues.Esperanza[statName] = new StatValue(statName);
      }
    }

    foreach (var form in FormStatIncreases.increases) {
      FormStatsValues.EnsureForm(form.Key);
      foreach (var majorStat in form.Value) {
        if (!FormStatsValues.values[form.Key].ContainsKey(majorStat.Key)) {
          continue;
        }

        foreach (var minorStat in majorStat.Value) {
          if (!AllStatValues.Esperanza.TryGetValue(minorStat.Key, out var statValue) || statValue == null) {
            statValue = new StatValue(minorStat.Key);
            AllStatValues.Esperanza[minorStat.Key] = statValue;
          }

          statValue.AddInPlace(
            (double)minorStat.Value * FormStatsValues.values[form.Key][majorStat.Key]
          );
        }
      }
    }

    FormStatIncreases.ApplyBonusToFlatStats(AllStatValues.Esperanza);

    foreach (var form in FormStatsValues.values) {
      foreach (var majorStat in form.Value) {
        level += majorStat.Value;
      }
    }

    SynchronizeCurrentHealthToMaximum();
  }

  void ResetRuntimeState() {
    currentHealth.Set(0d);
    maximumHealthSnapshot.Set(0d);
    lastKnownMaximumHealth.Set(0d);
    currentHealthInitialized = false;
    EsperanzaForms.ResetRuntimeState();
    EsperanzaAbilities.ResetRuntimeState();
    EsperanzaAbilityLoadouts.ResetRuntimeState();
    EsperanzaComboLoadouts.ResetRuntimeState();
    FormStatsValues.ResetToDefaults();
    EquippedItems.ResetToDefaults();
    DialogController.ResetRuntimeState("character_reset");
  }

  EndlessNumber ResolveMaximumHealth() {
    if (!AllStatValues.Esperanza.TryGetValue("HP", out var healthStat) ||
        healthStat == null ||
        healthStat.IsPercentage ||
        healthStat.EndlessValue == null) {
      return maximumHealthSnapshot.Set(0d);
    }

    return healthStat.EndlessValue.IsPositive
      ? maximumHealthSnapshot.Set(healthStat.EndlessValue)
      : maximumHealthSnapshot.Set(0d);
  }

  void SynchronizeCurrentHealthToMaximum() {
    var maximumHealth = ResolveMaximumHealth();
    var shouldRestoreToMaximum = !currentHealthInitialized ||
                                 (!lastKnownMaximumHealth.IsPositive && maximumHealth.IsPositive);

    if (shouldRestoreToMaximum) {
      currentHealth.Set(maximumHealth);
    }
    else {
      currentHealth.Set(
        EndlessNumber.Min(
          EndlessNumber.Max(currentHealth, new EndlessNumber()),
          maximumHealth
        )
      );
    }

    lastKnownMaximumHealth.Set(maximumHealth);
    currentHealthInitialized = true;
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

    if (loadedForms.HasPrefix(SaveKeys.AbilityProgress)) {
      var abilityProgress = loadedForms.GetComplex<Dictionary<string, AbilityProgressState>>(SaveKeys.AbilityProgress);
      EsperanzaAbilities.ApplyProgressState(abilityProgress);
    } else {
      EsperanzaAbilities.ApplyProgressState(null);
    }

    if (loadedForms.HasPrefix(SaveKeys.AbilityLoadouts)) {
      var abilityLoadouts = loadedForms.GetComplex<Dictionary<string, List<string>>>(SaveKeys.AbilityLoadouts);
      EsperanzaAbilityLoadouts.ApplyLoadedState(abilityLoadouts);
    } else {
      EsperanzaAbilityLoadouts.ApplyLoadedState(null);
    }

    if (loadedForms.HasPrefix(SaveKeys.ComboLoadouts)) {
      var comboLoadouts = loadedForms.GetComplex<Dictionary<string, List<EsperanzaComboState>>>(SaveKeys.ComboLoadouts);
      EsperanzaComboLoadouts.ApplyLoadedState(comboLoadouts);
    } else {
      EsperanzaComboLoadouts.ApplyLoadedState(null);
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
    if (formsSavePending && formsSaveSlot != SaveSlotManager.slot) {
      Debug.LogWarning(
        "[CharacterState][SaveFormsState] Refused cross-slot progress save." +
        " pending_slot=" + formsSaveSlot +
        " current_slot=" + SaveSlotManager.slot
      );
      return false;
    }

    try {
      var formsSave = new SaveData {
        [SaveKeys.ActiveForm] = EsperanzaForms.GetActive()
      };
      formsSave.SetComplex(SaveKeys.UnlockedForms, EsperanzaForms.GetUnlockedSnapshot());
      formsSave.SetComplex(SaveKeys.FormProgress, EsperanzaForms.GetProgressSnapshot());
      formsSave.SetComplex(SaveKeys.AbilityProgress, EsperanzaAbilities.GetProgressSnapshot());
      formsSave.SetComplex(SaveKeys.AbilityLoadouts, EsperanzaAbilityLoadouts.GetSnapshot());
      formsSave.SetComplex(SaveKeys.ComboLoadouts, EsperanzaComboLoadouts.GetSnapshot());
      formsSave[SaveKeys.AvailableStatPoints] = GetAvailableStatPoints();
      SaveSlotManager.Save(SaveKeys.Forms, formsSave);
      formsSavePending = false;
      formsSaveSlot = -1;
      return true;
    }
    catch (Exception e) {
      Debug.LogWarning("[CharacterState][SaveFormsState] Failed to save forms state: " + e.Message);
      return false;
    }
  }

  void QueueFormsSave() {
    if (!formsSavePending) {
      formsSaveSlot = SaveSlotManager.slot;
    }
    formsSavePending = true;
  }

  void FlushPendingFormsSave() {
    TryFlushPendingFormsSave();
  }

  bool TryFlushPendingFormsSave() {
    if (!formsSavePending) {
      return true;
    }

    if (SaveFormsState()) {
      return true;
    }

    return false;
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
    RuntimeLog.Log(
      "[CharacterState][NotifyFormStateChanged] form='" + (resolvedForm ?? "") +
      "' source='" + (source ?? "") + "'"
    );
    MessageBus.Send(CharacterMessageTopics.FormChanged, resolvedForm);
    MessageBus.Send(CharacterMessageTopics.GearReady, resolvedForm);
    MessageBus.Send(CharacterMessageTopics.FormProgressChanged, resolvedForm);
  }

  int ResolveNextLevelXp(
    int currentThreshold,
    string progressType,
    string progressId,
    int nextLevel,
    string source
  ) {
    var useSilverRatio = UnityEngine.Random.value < 0.5f;
    var ratio = useSilverRatio ? EsperanzaForms.SilverRatio : EsperanzaForms.GoldenRatio;
    var nextThreshold = Mathf.CeilToInt((float)(currentThreshold * ratio));
    RuntimeLog.Log(
      "[CharacterState][LevelCurve] progress_type='" + (progressType ?? "") +
      "' progress_id='" + (progressId ?? "") +
      "' next_level=" + nextLevel +
      " source='" + (source ?? "") +
      "' ratio='" + (useSilverRatio ? "silver" : "gold") +
      "' current_threshold=" + currentThreshold +
      " next_threshold=" + nextThreshold
    );
    return Mathf.Max(nextThreshold, currentThreshold + 1);
  }

  int GetEarnedStatPoints() {
    var earnedPoints = 0;
    foreach (var formName in EsperanzaForms.KnownForms) {
      var progress = EsperanzaForms.EnsureProgress(formName);
      earnedPoints += Mathf.Max(
        0,
        (progress != null ? progress.level : EsperanzaForms.DefaultLevel) - EsperanzaForms.DefaultLevel
      );
    }

    return earnedPoints;
  }

  int GetSpentStatPoints() {
    var spentPoints = 0;
    foreach (var formName in EsperanzaForms.KnownForms) {
      FormStatsValues.EnsureForm(formName);
      if (!FormStatsValues.values.TryGetValue(formName, out var stats) || stats == null) {
        continue;
      }

      foreach (var stat in stats) {
        var defaultValue = FormStatsValues.GetDefaultValue(formName, stat.Key);
        spentPoints += Mathf.Max(0, stat.Value - defaultValue);
      }
    }

    return spentPoints;
  }
}
