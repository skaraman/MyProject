using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class FormProgressState {
  public int level = EsperanzaForms.DefaultLevel;
  public int currentXp = EsperanzaForms.DefaultCurrentXp;
  public int nextLevelXp = EsperanzaForms.DefaultNextLevelXp;

  public FormProgressState Clone() {
    return new FormProgressState {
      level = level,
      currentXp = currentXp,
      nextLevelXp = nextLevelXp
    };
  }
}

public static class EsperanzaForms {
  public const int DefaultLevel = 1;
  public const int DefaultCurrentXp = 0;
  public const int DefaultNextLevelXp = 400;
  public const double GoldenRatio = 1.61803398875d;
  public const double SilverRatio = 2.41421356237d;

  static readonly string[] KnownFormOrder = { "Base", "Bolt", "Aqua", "Fire", "Cold", "Dark" };

  public static Dictionary<string, int> Active { get; private set; } = CreateDefaultActiveState();
  public static Dictionary<string, int> Unlocked { get; private set; } = CreateDefaultUnlockedState();
  public static Dictionary<string, FormProgressState> Progress { get; private set; } = CreateDefaultProgressState();

  public static IReadOnlyList<string> KnownForms => KnownFormOrder;

  public static void PrepareRuntimeCaches() {
    EnsureKnownForms();
  }

  public static void ResetRuntimeState() {
    Active = CreateDefaultActiveState();
    Unlocked = CreateDefaultUnlockedState();
    Progress = CreateDefaultProgressState();
  }

  public static void SetActive(string v) {
    var resolvedForm = ResolveFormKey(v);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      Debug.LogWarning("[EsperanzaForms] Ignored unknown active form request='" + (v ?? "") + "'");
      return;
    }

    EnsureKnownForms();
    EnsureProgress(resolvedForm);
    var previousForm = GetActive();
    for (var i = 0; i < KnownFormOrder.Length; i++) {
      var key = KnownFormOrder[i];
      Active[key] = string.Equals(key, resolvedForm, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    if (Unlocked.ContainsKey(resolvedForm)) {
      Unlocked[resolvedForm] = 1;
    }

    if (!string.Equals(previousForm, resolvedForm, StringComparison.OrdinalIgnoreCase)) {
      RuntimeLog.Log(
        "[EsperanzaForms] Active form changed previous='" + (string.IsNullOrWhiteSpace(previousForm) ? "-" : previousForm) +
        "' next='" + resolvedForm + "'"
      );
    }
  }

  public static string GetActive() {
    EnsureKnownForms();
    for (var i = 0; i < KnownFormOrder.Length; i++) {
      var form = KnownFormOrder[i];
      if (Active.TryGetValue(form, out var isActive) && isActive == 1) {
        return form;
      }
    }

    var fallback = KnownFormOrder[0];
    for (var i = 0; i < KnownFormOrder.Length; i++) {
      var form = KnownFormOrder[i];
      Active[form] = string.Equals(form, fallback, StringComparison.Ordinal) ? 1 : 0;
    }
    return fallback;
  }

  public static void UnlockForm(string v) {
    EnsureKnownForms();
    var resolvedForm = ResolveFormKey(v);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      Debug.LogWarning("[EsperanzaForms] Ignored unknown unlock form request='" + (v ?? "") + "'");
      return;
    }

    Unlocked[resolvedForm] = 1;
    EnsureProgress(resolvedForm);
  }

  public static bool IsUnlocked(string value) {
    var resolvedForm = ResolveFormKey(value);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      return false;
    }

    EnsureKnownForms();
    return Unlocked.TryGetValue(resolvedForm, out var unlocked) && unlocked == 1;
  }

  public static FormProgressState EnsureProgress(string value) {
    EnsureKnownForms();
    var resolvedForm = ResolveFormKey(value);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      return null;
    }

    if (!Progress.TryGetValue(resolvedForm, out var progress) || progress == null) {
      progress = CreateDefaultProgressEntry();
      Progress[resolvedForm] = progress;
    }

    SanitizeProgress(progress);
    return progress;
  }

  public static FormProgressState GetProgressCopy(string value) {
    var progress = EnsureProgress(value);
    return progress != null ? progress.Clone() : null;
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

    var progress = EnsureProgress(value);
    if (progress == null) {
      return false;
    }

    level = progress.level;
    currentXp = progress.currentXp;
    nextLevelXp = progress.nextLevelXp;
    return true;
  }

  public static Dictionary<string, int> GetUnlockedSnapshot() {
    EnsureKnownForms();
    var snapshot = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var form in KnownFormOrder) {
      snapshot[form] = Unlocked.TryGetValue(form, out var unlocked) ? unlocked : 0;
    }
    return snapshot;
  }

  public static Dictionary<string, FormProgressState> GetProgressSnapshot() {
    EnsureKnownForms();
    var snapshot = new Dictionary<string, FormProgressState>(StringComparer.Ordinal);
    foreach (var form in KnownFormOrder) {
      snapshot[form] = GetProgressCopy(form) ?? CreateDefaultProgressEntry();
    }
    return snapshot;
  }

  public static void ApplyUnlockedState(Dictionary<string, int> loadedUnlocked) {
    EnsureKnownForms();
    foreach (var form in KnownFormOrder) {
      Unlocked[form] = string.Equals(form, "Base", StringComparison.Ordinal) ? 1 : 0;
    }

    if (loadedUnlocked == null) {
      return;
    }

    foreach (var item in loadedUnlocked) {
      var resolvedForm = ResolveFormKey(item.Key);
      if (string.IsNullOrWhiteSpace(resolvedForm)) {
        continue;
      }

      Unlocked[resolvedForm] = item.Value > 0 ? 1 : 0;
    }

    Unlocked["Base"] = 1;
  }

  public static void ApplyProgressState(Dictionary<string, FormProgressState> loadedProgress) {
    EnsureKnownForms();
    Progress = CreateDefaultProgressState();

    if (loadedProgress == null) {
      return;
    }

    foreach (var item in loadedProgress) {
      var resolvedForm = ResolveFormKey(item.Key);
      if (string.IsNullOrWhiteSpace(resolvedForm)) {
        continue;
      }

      Progress[resolvedForm] = item.Value != null ? item.Value.Clone() : CreateDefaultProgressEntry();
      SanitizeProgress(Progress[resolvedForm]);
    }
  }

  public static void EnsureKnownForms() {
    for (var i = 0; i < KnownFormOrder.Length; i++) {
      var form = KnownFormOrder[i];
      if (!Active.ContainsKey(form)) {
        Active[form] = 0;
      }
      if (!Unlocked.ContainsKey(form)) {
        Unlocked[form] = string.Equals(form, "Base", StringComparison.Ordinal) ? 1 : 0;
      }
      if (!Progress.ContainsKey(form) || Progress[form] == null) {
        Progress[form] = CreateDefaultProgressEntry();
      }
      SanitizeProgress(Progress[form]);
    }
    if (!Active.ContainsKey("Base")) {
      Active["Base"] = 1;
    }
  }

  public static string ResolveFormKey(string value) {
    if (string.IsNullOrWhiteSpace(value)) return null;

    var requestedValue = value.Trim();
    for (var i = 0; i < KnownFormOrder.Length; i++) {
      var key = KnownFormOrder[i];
      if (string.Equals(key, requestedValue, StringComparison.OrdinalIgnoreCase)) {
        return key;
      }
    }

    return null;
  }

  static void SanitizeProgress(FormProgressState progress) {
    if (progress == null) {
      return;
    }

    progress.level = Mathf.Max(progress.level, DefaultLevel);
    progress.currentXp = Mathf.Max(progress.currentXp, DefaultCurrentXp);
    progress.nextLevelXp = Mathf.Max(progress.nextLevelXp, DefaultNextLevelXp);
  }

  static Dictionary<string, int> CreateDefaultActiveState() {
    var state = new Dictionary<string, int>(StringComparer.Ordinal);
    for (var i = 0; i < KnownFormOrder.Length; i++) {
      var form = KnownFormOrder[i];
      state[form] = string.Equals(form, "Base", StringComparison.Ordinal) ? 1 : 0;
    }
    return state;
  }

  static Dictionary<string, int> CreateDefaultUnlockedState() {
    var state = new Dictionary<string, int>(StringComparer.Ordinal);
    for (var i = 0; i < KnownFormOrder.Length; i++) {
      var form = KnownFormOrder[i];
      state[form] = string.Equals(form, "Base", StringComparison.Ordinal) ? 1 : 0;
    }
    return state;
  }

  static Dictionary<string, FormProgressState> CreateDefaultProgressState() {
    var state = new Dictionary<string, FormProgressState>(StringComparer.Ordinal);
    for (var i = 0; i < KnownFormOrder.Length; i++) {
      state[KnownFormOrder[i]] = CreateDefaultProgressEntry();
    }
    return state;
  }

  static FormProgressState CreateDefaultProgressEntry() {
    return new FormProgressState {
      level = DefaultLevel,
      currentXp = DefaultCurrentXp,
      nextLevelXp = DefaultNextLevelXp
    };
  }
}

public static class AttacksMapToForms {
  public static Dictionary<string, Dictionary<string, string>> all { set; get; } = new Dictionary<string, Dictionary<string, string>> {
    ["Base"] = new Dictionary<string, string> {
      ["attack1"] = "PunchLeft",
      ["attack2"] = "PunchRight",
      ["attack3"] = "KickLeft",
      ["attack4"] = "KickRight",
      ["dash"] = "Dash",
      ["dodge"] = "Dodge",
      ["block"] = "Block",
      ["jump"] = "Jump",
      ["superattack1"] = "Blast",
      ["superattack2"] = "TwisterKick"
    },
    ["Bolt"] = new Dictionary<string, string> {
      ["attack1"] = "Shock",
      ["attack2"] = "ChainLighting",
      ["attack3"] = "Static",
      ["attack4"] = "LightningBolt",
      ["dash"] = "DoubleDash",
      ["dodge"] = "InstantDodge",
      ["block"] = "Block",
      ["jump"] = "DoubleJump",
      ["superattack1"] = "ThunderBolt",
      ["superattack2"] = "Orbit"
    },
    ["Fire"] = new Dictionary<string, string> {
      ["attack1"] = "Flamethower",
      ["attack2"] = "BurningWall",
      ["attack3"] = "Blaze",
      ["attack4"] = "PyreLight",
      ["dash"] = "BurningDash",
      ["dodge"] = "Dodge",
      ["block"] = "FlameShield",
      ["jump"] = "FlameWings",
      ["superattack1"] = "Meteor",
      ["superattack2"] = "Fissure"
    },
    ["Cold"] = new Dictionary<string, string> {
      ["attack1"] = "FrostCloud",
      ["attack2"] = "IceBlast",
      ["attack3"] = "Iceclitite",
      ["attack4"] = "Iceclimite",
      ["dash"] = "Slide",
      ["dodge"] = "Dodge",
      ["block"] = "IceShield",
      ["jump"] = "FrostFloat",
      ["superattack1"] = "Avalanche",
      ["superattack2"] = "Blizzard"
    },
    ["Aqua"] = new Dictionary<string, string> {
      ["attack1"] = "WaterBlast",
      ["attack2"] = "CrushingHydro",
      ["attack3"] = "WaterSphere",
      ["attack4"] = "PressureDeluge",
      ["dash"] = "VaporDash",
      ["dodge"] = "Dodge",
      ["block"] = "Bubble",
      ["jump"] = "DivingVortex",
      ["superattack1"] = "RainNeedles",
      ["superattack2"] = "TsunamiStrike"
    },
    ["Dark"] = new Dictionary<string, string> {
      ["attack1"] = "Rip",
      ["attack2"] = "Tear",
      ["attack3"] = "Rage",
      ["attack4"] = "Seethe",
      ["dash"] = "ShadowWalk",
      ["dodge"] = "Dodge",
      ["block"] = "AbyssalCall",
      ["jump"] = "CorrutKinesis",
      ["superattack1"] = "SoulSiphon",
      ["superattack2"] = "SoulInfection"
    },
  };
}
