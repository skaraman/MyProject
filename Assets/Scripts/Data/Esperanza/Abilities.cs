using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[Serializable]
public class AbilityProgressState {
  public int level = EsperanzaAbilities.DefaultLevel;
  public int currentXp = EsperanzaAbilities.DefaultCurrentXp;
  public int nextLevelXp = EsperanzaAbilities.DefaultNextLevelXp;

  public AbilityProgressState Clone() {
    return new AbilityProgressState {
      level = level,
      currentXp = currentXp,
      nextLevelXp = nextLevelXp
    };
  }
}

public sealed class AbilityDefinition {
  public string animationName { get; }
  public string damageSubtype { get; }

  public AbilityDefinition(string animationName, string damageSubtype) {
    this.animationName = animationName;
    this.damageSubtype = damageSubtype;
  }
}

public static class EsperanzaAbilities {
  public const int DefaultLevel = 1;
  public const int DefaultCurrentXp = 0;
  public const int DefaultNextLevelXp = 100;

  static readonly Dictionary<string, AbilityDefinition> definitions = CreateDefinitions();
  static readonly Dictionary<string, string> resolvedAnimationsByInput = CreateResolutionCache();
  static readonly Dictionary<string, string> displayNamesByAnimation =
    new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, string> normalizedAnimationsByInput =
    new(StringComparer.Ordinal);
  static Dictionary<string, AbilityProgressState> progress = CreateDefaultProgress();

  public static void PrepareRuntimeCaches() {
    EnsureKnownAbilities();
    foreach (var definition in definitions) {
      var animationName = definition.Key;
      var displayName = GetDisplayName(animationName);
      resolvedAnimationsByInput[displayName] = animationName;
      normalizedAnimationsByInput[NormalizeIdentity(animationName)] = animationName;
      normalizedAnimationsByInput[NormalizeIdentity(displayName)] = animationName;
    }
  }

  public static void ResetRuntimeState() {
    progress = CreateDefaultProgress();
  }

  public static AbilityProgressState EnsureProgress(string value) {
    if (!TryResolveAbilityAnimation(value, out var animationName)) {
      return null;
    }

    if (!progress.TryGetValue(animationName, out var abilityProgress) || abilityProgress == null) {
      abilityProgress = CreateDefaultProgressEntry();
      progress[animationName] = abilityProgress;
    }

    SanitizeProgress(abilityProgress);
    return abilityProgress;
  }

  public static AbilityProgressState GetProgressCopy(string value) {
    var abilityProgress = EnsureProgress(value);
    return abilityProgress != null ? abilityProgress.Clone() : null;
  }

  public static bool TryGetProgressValues(
    string value,
    out int level,
    out int currentXp,
    out int nextLevelXp
  ) {
    level = DefaultLevel;
    currentXp = DefaultCurrentXp;
    nextLevelXp = DefaultNextLevelXp;

    var abilityProgress = EnsureProgress(value);
    if (abilityProgress == null) {
      return false;
    }

    level = abilityProgress.level;
    currentXp = abilityProgress.currentXp;
    nextLevelXp = abilityProgress.nextLevelXp;
    return true;
  }

  public static Dictionary<string, AbilityProgressState> GetProgressSnapshot() {
    EnsureKnownAbilities();
    var snapshot = new Dictionary<string, AbilityProgressState>(StringComparer.OrdinalIgnoreCase);
    foreach (var ability in progress) {
      snapshot[ability.Key] = ability.Value != null
        ? ability.Value.Clone()
        : CreateDefaultProgressEntry();
    }
    return snapshot;
  }

  public static void ApplyProgressState(Dictionary<string, AbilityProgressState> loadedProgress) {
    progress = CreateDefaultProgress();
    if (loadedProgress == null) {
      return;
    }

    foreach (var ability in loadedProgress) {
      if (!TryResolveAbilityAnimation(ability.Key, out var animationName)) {
        continue;
      }

      var abilityProgress = ability.Value != null
        ? ability.Value.Clone()
        : CreateDefaultProgressEntry();
      SanitizeProgress(abilityProgress);
      progress[animationName] = abilityProgress;
    }
  }

  public static int GetRawDamage(string value) {
    var abilityProgress = EnsureProgress(value);
    return abilityProgress != null ? abilityProgress.level : 0;
  }

  public static int ResolveNextLevelXp(int level) {
    var resolvedLevel = Mathf.Max(level, DefaultLevel);
    return DefaultNextLevelXp * resolvedLevel;
  }

  public static bool TryResolveAbilityAnimation(string value, out string animationName) {
    animationName = null;
    if (string.IsNullOrWhiteSpace(value)) {
      return false;
    }

    if (resolvedAnimationsByInput.TryGetValue(value, out animationName)) {
      return true;
    }

    var requestedValue = value.Trim();
    if (resolvedAnimationsByInput.TryGetValue(requestedValue, out animationName)) {
      return true;
    }

    if (TryResolveMappedAnimation(requestedValue, out animationName)) {
      resolvedAnimationsByInput[value] = animationName;
      return true;
    }

    if (TryResolveProjectileAnimation(requestedValue, out animationName)) {
      resolvedAnimationsByInput[value] = animationName;
      return true;
    }

    var transitionIndex = requestedValue.LastIndexOf("To", StringComparison.Ordinal);
    if (transitionIndex < 0) {
      return false;
    }

    var transitionTarget = requestedValue.Substring(transitionIndex + 2);
    if (!TryResolveMappedAnimation(transitionTarget, out animationName)) {
      return false;
    }

    resolvedAnimationsByInput[value] = animationName;
    return true;
  }

  public static string GetDisplayName(string value) {
    if (!TryResolveAbilityAnimation(value, out var animationName)) {
      return SplitWords(value);
    }

    if (displayNamesByAnimation.TryGetValue(animationName, out var cachedDisplayName)) {
      return cachedDisplayName;
    }

    var displayName = BuildDisplayName(animationName);
    displayNamesByAnimation[animationName] = displayName;
    return displayName;
  }

  static string BuildDisplayName(string animationName) {
    if (TryMoveSideToFront(animationName, "Right", out var displayName)) {
      return displayName;
    }

    if (TryMoveSideToFront(animationName, "Left", out displayName)) {
      return displayName;
    }

    return SplitWords(animationName);
  }

  public static string ResolveForm(string value) {
    return ResolveDamageSubtype(value);
  }

  public static string ResolveDamageSubtype(string value) {
    if (!TryResolveAbilityAnimation(value, out var animationName)) {
      return null;
    }

    if (!definitions.TryGetValue(animationName, out var definition)) {
      return null;
    }

    return definition.damageSubtype;
  }

  public static bool TryGetDefinition(string value, out AbilityDefinition definition) {
    definition = null;
    if (!TryResolveAbilityAnimation(value, out var animationName)) {
      return false;
    }

    return definitions.TryGetValue(animationName, out definition) && definition != null;
  }

  static Dictionary<string, AbilityDefinition> CreateDefinitions() {
    var result = new Dictionary<string, AbilityDefinition>(StringComparer.OrdinalIgnoreCase);

    foreach (var form in AttacksMapToForms.all) {
      foreach (var action in form.Value) {
        var animationName = action.Value;
        if (string.IsNullOrWhiteSpace(animationName)) {
          continue;
        }
        if (result.ContainsKey(animationName)) {
          continue;
        }

        result[animationName] = new AbilityDefinition(animationName, form.Key);
      }
    }

    return result;
  }

  static Dictionary<string, string> CreateResolutionCache() {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (var definition in definitions) {
      result[definition.Key] = definition.Value.animationName;
    }

    foreach (var animation in Animations.Esperanza) {
      var animationData = animation.Value;
      if (animationData == null || string.IsNullOrWhiteSpace(animationData.projectile)) {
        continue;
      }
      if (!definitions.TryGetValue(animation.Key, out var definition)) {
        continue;
      }

      result[animationData.projectile] = definition.animationName;
    }

    return result;
  }

  static Dictionary<string, AbilityProgressState> CreateDefaultProgress() {
    var defaultProgress = new Dictionary<string, AbilityProgressState>(StringComparer.OrdinalIgnoreCase);
    foreach (var form in AttacksMapToForms.all) {
      foreach (var action in form.Value) {
        if (string.IsNullOrWhiteSpace(action.Value)) {
          continue;
        }

        if (!defaultProgress.ContainsKey(action.Value)) {
          defaultProgress[action.Value] = CreateDefaultProgressEntry();
        }
      }
    }
    return defaultProgress;
  }

  static void EnsureKnownAbilities() {
    foreach (var form in AttacksMapToForms.all) {
      foreach (var action in form.Value) {
        if (string.IsNullOrWhiteSpace(action.Value)) {
          continue;
        }

        if (!progress.ContainsKey(action.Value)) {
          progress[action.Value] = CreateDefaultProgressEntry();
        }
      }
    }
  }

  static bool TryResolveMappedAnimation(string value, out string animationName) {
    animationName = null;
    if (definitions.TryGetValue(value, out var definition)) {
      animationName = definition.animationName;
      return true;
    }

    var normalizedValue = NormalizeIdentity(value);
    if (normalizedAnimationsByInput.TryGetValue(normalizedValue, out animationName)) {
      return true;
    }
    foreach (var form in AttacksMapToForms.all) {
      foreach (var action in form.Value) {
        var mappedAnimation = action.Value;
        if (string.IsNullOrWhiteSpace(mappedAnimation)) {
          continue;
        }

        if (string.Equals(mappedAnimation, value, StringComparison.OrdinalIgnoreCase)) {
          animationName = mappedAnimation;
          return true;
        }

        var displayName = GetMappedDisplayName(mappedAnimation);
        if (string.Equals(NormalizeIdentity(displayName), normalizedValue, StringComparison.Ordinal)) {
          animationName = mappedAnimation;
          normalizedAnimationsByInput[normalizedValue] = animationName;
          return true;
        }
      }
    }
    return false;
  }

  static bool TryResolveProjectileAnimation(string value, out string animationName) {
    animationName = null;
    foreach (var animation in Animations.Esperanza) {
      var animationData = animation.Value;
      if (animationData == null || string.IsNullOrWhiteSpace(animationData.projectile)) {
        continue;
      }

      if (!string.Equals(animationData.projectile, value, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      return TryResolveMappedAnimation(animation.Key, out animationName);
    }
    return false;
  }

  static string GetMappedDisplayName(string animationName) {
    if (TryMoveSideToFront(animationName, "Right", out var displayName)) {
      return displayName;
    }

    if (TryMoveSideToFront(animationName, "Left", out displayName)) {
      return displayName;
    }

    return SplitWords(animationName);
  }

  static bool TryMoveSideToFront(string value, string side, out string displayName) {
    displayName = null;
    if (string.IsNullOrWhiteSpace(value) || !value.EndsWith(side, StringComparison.Ordinal)) {
      return false;
    }

    var actionName = value.Substring(0, value.Length - side.Length);
    if (string.IsNullOrWhiteSpace(actionName)) {
      return false;
    }

    displayName = side + " " + SplitWords(actionName);
    return true;
  }

  static string SplitWords(string value) {
    if (string.IsNullOrWhiteSpace(value)) {
      return "";
    }

    var result = new StringBuilder();
    var trimmedValue = value.Trim();
    for (var i = 0; i < trimmedValue.Length; i++) {
      var character = trimmedValue[i];
      var addSpace = i > 0 &&
        char.IsUpper(character) &&
        !char.IsWhiteSpace(trimmedValue[i - 1]) &&
        !char.IsUpper(trimmedValue[i - 1]);
      if (addSpace) {
        result.Append(' ');
      }
      result.Append(character);
    }
    return result.ToString();
  }

  static string NormalizeIdentity(string value) {
    if (string.IsNullOrWhiteSpace(value)) {
      return "";
    }

    var normalizedValue = new StringBuilder();
    foreach (var character in value) {
      if (char.IsLetterOrDigit(character)) {
        normalizedValue.Append(char.ToUpperInvariant(character));
      }
    }
    return normalizedValue.ToString();
  }

  static AbilityProgressState CreateDefaultProgressEntry() {
    return new AbilityProgressState {
      level = DefaultLevel,
      currentXp = DefaultCurrentXp,
      nextLevelXp = DefaultNextLevelXp
    };
  }

  static void SanitizeProgress(AbilityProgressState abilityProgress) {
    if (abilityProgress == null) {
      return;
    }

    abilityProgress.level = Mathf.Max(abilityProgress.level, DefaultLevel);
    abilityProgress.currentXp = Mathf.Max(abilityProgress.currentXp, DefaultCurrentXp);
    abilityProgress.nextLevelXp = Mathf.Max(abilityProgress.nextLevelXp, DefaultNextLevelXp);
  }
}

public static class EsperanzaAbilityLoadouts {
  public const int MaximumAbilitiesPerForm = 10;

  static readonly string[] DefaultActionOrder = {
    "attack1",
    "attack2",
    "attack3",
    "attack4",
    "superattack1",
    "superattack2"
  };

  static Dictionary<string, List<string>> abilitiesByForm = CreateDefaultLoadouts();

  public static void ResetRuntimeState() {
    abilitiesByForm = CreateDefaultLoadouts();
  }

  public static List<string> GetAbilitiesCopy(string formName) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      return new List<string>();
    }

    EnsureForm(resolvedForm);
    return new List<string>(abilitiesByForm[resolvedForm]);
  }

  public static IReadOnlyList<string> GetAbilitiesView(string formName) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      return Array.Empty<string>();
    }

    EnsureForm(resolvedForm);
    return abilitiesByForm[resolvedForm];
  }

  public static Dictionary<string, List<string>> GetSnapshot() {
    EnsureKnownForms();
    var snapshot = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var form in abilitiesByForm) {
      snapshot[form.Key] = new List<string>(form.Value);
    }
    return snapshot;
  }

  public static void ApplyLoadedState(Dictionary<string, List<string>> loadedLoadouts) {
    abilitiesByForm = CreateDefaultLoadouts();
    if (loadedLoadouts == null) {
      return;
    }

    foreach (var form in loadedLoadouts) {
      var resolvedForm = EsperanzaForms.ResolveFormKey(form.Key);
      if (string.IsNullOrWhiteSpace(resolvedForm)) {
        continue;
      }

      abilitiesByForm[resolvedForm] = NormalizeAbilities(form.Value);
    }

    RemoveDuplicateAssignments();
  }

  public static bool SetAbilities(string formName, IList<string> abilities) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      return false;
    }

    var normalizedAbilities = NormalizeAbilities(abilities);
    RemoveAbilitiesFromOtherForms(resolvedForm, normalizedAbilities);
    abilitiesByForm[resolvedForm] = normalizedAbilities;
    return true;
  }

  public static bool MoveAbility(
    string abilityName,
    string targetFormName,
    int targetIndex,
    out List<string> changedForms
  ) {
    changedForms = new List<string>();
    var resolvedTargetForm = EsperanzaForms.ResolveFormKey(targetFormName);
    if (string.IsNullOrWhiteSpace(resolvedTargetForm)) {
      return false;
    }
    if (!EsperanzaAbilities.TryResolveAbilityAnimation(abilityName, out var animationName)) {
      return false;
    }

    EnsureKnownForms();
    var targetAbilities = abilitiesByForm[resolvedTargetForm];
    var alreadyInTarget = targetAbilities.Contains(animationName);
    if (!alreadyInTarget && targetAbilities.Count >= MaximumAbilitiesPerForm) {
      return false;
    }

    foreach (var form in abilitiesByForm) {
      if (!form.Value.Remove(animationName)) {
        continue;
      }
      if (!changedForms.Contains(form.Key)) {
        changedForms.Add(form.Key);
      }
    }

    var resolvedIndex = Mathf.Clamp(targetIndex, 0, targetAbilities.Count);
    targetAbilities.Insert(resolvedIndex, animationName);
    if (!changedForms.Contains(resolvedTargetForm)) {
      changedForms.Add(resolvedTargetForm);
    }
    return true;
  }

  static Dictionary<string, List<string>> CreateDefaultLoadouts() {
    var defaultLoadouts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var formName in EsperanzaForms.KnownForms) {
      defaultLoadouts[formName] = CreateDefaultLoadout(formName);
    }
    return defaultLoadouts;
  }

  static List<string> CreateDefaultLoadout(string formName) {
    var defaultAbilities = new List<string>();
    if (!AttacksMapToForms.all.TryGetValue(formName, out var actionMap) || actionMap == null) {
      return defaultAbilities;
    }

    var defaultCount = string.Equals(formName, "Base", StringComparison.Ordinal)
      ? 5
      : 6;
    for (var i = 0; i < DefaultActionOrder.Length; i++) {
      var actionName = DefaultActionOrder[i];
      if (!actionMap.TryGetValue(actionName, out var abilityName)) {
        continue;
      }
      if (!EsperanzaAbilities.TryResolveAbilityAnimation(abilityName, out var animationName)) {
        continue;
      }

      defaultAbilities.Add(animationName);
      if (defaultAbilities.Count >= defaultCount) {
        break;
      }
    }
    return defaultAbilities;
  }

  static List<string> NormalizeAbilities(IList<string> abilities) {
    var normalizedAbilities = new List<string>();
    var seenAbilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (abilities == null) {
      return normalizedAbilities;
    }

    for (var i = 0; i < abilities.Count; i++) {
      if (!EsperanzaAbilities.TryResolveAbilityAnimation(abilities[i], out var animationName)) {
        continue;
      }
      if (!seenAbilities.Add(animationName)) {
        continue;
      }

      normalizedAbilities.Add(animationName);
      if (normalizedAbilities.Count >= MaximumAbilitiesPerForm) {
        break;
      }
    }
    return normalizedAbilities;
  }

  static void RemoveAbilitiesFromOtherForms(string targetForm, IList<string> abilities) {
    if (abilities == null) {
      return;
    }

    foreach (var form in abilitiesByForm) {
      if (string.Equals(form.Key, targetForm, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      for (var i = 0; i < abilities.Count; i++) {
        form.Value.Remove(abilities[i]);
      }
    }
  }

  static void RemoveDuplicateAssignments() {
    var assignedAbilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var formName in EsperanzaForms.KnownForms) {
      EnsureForm(formName);
      var formAbilities = abilitiesByForm[formName];
      for (var i = formAbilities.Count - 1; i >= 0; i--) {
        if (assignedAbilities.Add(formAbilities[i])) {
          continue;
        }

        formAbilities.RemoveAt(i);
      }
    }
  }

  static void EnsureKnownForms() {
    foreach (var formName in EsperanzaForms.KnownForms) {
      EnsureForm(formName);
    }
  }

  static void EnsureForm(string formName) {
    if (!abilitiesByForm.ContainsKey(formName) || abilitiesByForm[formName] == null) {
      abilitiesByForm[formName] = CreateDefaultLoadout(formName);
    }
  }
}
