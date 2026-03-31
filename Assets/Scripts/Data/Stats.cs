using System.Collections.Generic;

public static class FormStatIncreases {
  public static Dictionary<string, Dictionary<string, Dictionary<string, float>>> increases { get; } = new Dictionary<string, Dictionary<string, Dictionary<string, float>>> {
    ["Base"] = new Dictionary<string, Dictionary<string, float>> {
      ["STR"] = new Dictionary<string, float> { ["HP"] = 1000, ["DMG"] = 1, ["DCHC"] = 0.1f },
      ["DEX"] = new Dictionary<string, float> { ["AKSP"] = 0.01f, ["NRGRG"] = 0.01f, ["CDMG"] = 1 },
      ["END"] = new Dictionary<string, float> { ["NRG"] = 10, ["HPRG"] = 0.01f, ["ARM"] = 1 },
      ["INT"] = new Dictionary<string, float> { ["HEAL"] = 1, ["CCHC"] = 0.1f, ["LDMG"] = 1 },
      ["LCK"] = new Dictionary<string, float> { ["LCHC"] = 0.1f, ["DDMG"] = 1, ["BONUS"] = 1 }
    },
    ["Bolt"] = new Dictionary<string, Dictionary<string, float>> {
      ["DEX"] = new Dictionary<string, float> { ["DMG"] = 1, ["MVSP"] = 0.1f, ["AKSP"] = 0.01f },
      ["END"] = new Dictionary<string, float> { ["NRG"] = 1, ["NRGRG"] = 0.01f, ["HP"] = 10 },
      ["AMP"] = new Dictionary<string, float> { ["CDST"] = 1, ["HPRG"] = 0.01f, ["ARM"] = 1 },
      ["VLT"] = new Dictionary<string, float> { ["LDSC"] = 1, ["CCHC"] = 0.1f, ["HEAL"] = 1 },
      ["LCK"] = new Dictionary<string, float> { ["LCHC"] = 0.1f, ["DDMG"] = 1, ["BONUS"] = 1 }
    },
    ["Fire"] = new Dictionary<string, Dictionary<string, float>> {
      ["STR"] = new Dictionary<string, float> { ["DMG"] = 1, ["DCHC"] = .1f, ["HPRG"] = .01f },
      ["END"] = new Dictionary<string, float> { ["NRG"] = 1, ["NRGRG"] = .01f, ["HP"] = 10 },
      ["PYR"] = new Dictionary<string, float> { ["FDMG"] = 1, ["AKSP"] = .01f, ["ARM"] = 1 },
      ["EMB"] = new Dictionary<string, float> { ["AREA"] = 1, ["CCHC"] = .1f, ["HEAL"] = 1 },
      ["LCK"] = new Dictionary<string, float> { ["LCHC"] = .1f, ["DDMG"] = 1, ["BONUS"] = 1 }
    },
    ["Cold"] = new Dictionary<string, Dictionary<string, float>> {
      ["END"] = new Dictionary<string, float> { ["DMG"] = 1, ["NRGRG"] = .01f, ["NRG"] = 1 },
      ["INT"] = new Dictionary<string, float> { ["HEAL"] = 1, ["CCHC"] = .1f, ["LDMG"] = 1 },
      ["CHL"] = new Dictionary<string, float> { ["DUR"] = 1, ["HP"] = 10, ["ARM"] = 1 },
      ["ICI"] = new Dictionary<string, float> { ["AFT"] = .1f, ["CCHC"] = .1f, ["HEAL"] = 1 },
      ["LCK"] = new Dictionary<string, float> { ["LCHC"] = .1f, ["DDMG"] = 1, ["BONUS"] = 1 }
    },
    ["Aqua"] = new Dictionary<string, Dictionary<string, float>> {
      ["INT"] = new Dictionary<string, float> { ["DMG"] = 1, ["HEAL"] = 1, ["CCHC"] = .1f },
      ["DEX"] = new Dictionary<string, float> { ["AKSP"] = .01f, ["NRGRG"] = .01f, ["CDMG"] = 1 },
      ["VAP"] = new Dictionary<string, float> { ["EVD"] = .1f, ["NRG"] = 1, ["ARM"] = 1 },
      ["MOI"] = new Dictionary<string, float> { ["CLN"] = .01f, ["CCHC"] = .1f, ["HP"] = 10 },
      ["LCK"] = new Dictionary<string, float> { ["LCHC"] = .1f, ["DDMG"] = 1, ["BONUS"] = 1 }
    },
    ["Dark"] = new Dictionary<string, Dictionary<string, float>> {
      ["UMB"] = new Dictionary<string, float> { ["DMG"] = 1, ["NRG"] = 1, ["FEAR"] = 1 },
      ["VOI"] = new Dictionary<string, float> { ["SPEC"] = 0.1f, ["AKSP"] = .01f, ["CCHC"] = .1f },
      ["ABY"] = new Dictionary<string, float> { ["PEN"] = 1, ["ARM"] = 1, ["LDMG"] = 1 },
      ["ECL"] = new Dictionary<string, float> { ["EVD"] = 1, ["NRGRG"] = .01f, ["AREA"] = 1 },
      ["LCK"] = new Dictionary<string, float> { ["LCHC"] = .1f, ["CDMG"] = 1, ["BONUS"] = 1 }
    }
  };

  public static List<string> GetOrderedMajorStats(string formName) {
    var orderedStats = new List<string>();
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm)) {
      return orderedStats;
    }

    if (!increases.TryGetValue(resolvedForm, out var statMap) || statMap == null) {
      return orderedStats;
    }

    foreach (var stat in statMap.Keys) {
      orderedStats.Add(stat);
    }

    return orderedStats;
  }

  public static string ResolveMajorStatKey(string formName, string statName) {
    if (string.IsNullOrWhiteSpace(statName)) {
      return null;
    }

    var orderedStats = GetOrderedMajorStats(formName);
    for (var i = 0; i < orderedStats.Count; i++) {
      var key = orderedStats[i];
      if (string.Equals(key, statName.Trim(), System.StringComparison.OrdinalIgnoreCase)) {
        return key;
      }
    }

    return null;
  }

  public static List<KeyValuePair<string, float>> GetOrderedMinorStats(string formName, string majorStat) {
    var orderedStats = new List<KeyValuePair<string, float>>();
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    var resolvedMajorStat = ResolveMajorStatKey(resolvedForm, majorStat);
    if (string.IsNullOrWhiteSpace(resolvedForm) || string.IsNullOrWhiteSpace(resolvedMajorStat)) {
      return orderedStats;
    }

    if (!increases.TryGetValue(resolvedForm, out var statMap) ||
        statMap == null ||
        !statMap.TryGetValue(resolvedMajorStat, out var minorStats) ||
        minorStats == null) {
      return orderedStats;
    }

    foreach (var stat in minorStats) {
      orderedStats.Add(new KeyValuePair<string, float>(stat.Key, stat.Value));
    }

    return orderedStats;
  }
}

public static class FormStatsValues {
  static readonly Dictionary<string, Dictionary<string, int>> DefaultValues = new Dictionary<string, Dictionary<string, int>> {
    ["Base"] = new Dictionary<string, int> { ["STR"] = 1, ["DEX"] = 1, ["END"] = 1, ["INT"] = 1, ["LCK"] = 1 },
    ["Bolt"] = new Dictionary<string, int> { ["DEX"] = 0, ["END"] = 0, ["AMP"] = 0, ["VLT"] = 0, ["LCK"] = 0 },
    ["Fire"] = new Dictionary<string, int> { ["STR"] = 0, ["END"] = 0, ["PYR"] = 0, ["EMB"] = 0, ["LCK"] = 0 },
    ["Cold"] = new Dictionary<string, int> { ["END"] = 0, ["INT"] = 0, ["CHL"] = 0, ["ICI"] = 0, ["LCK"] = 0 },
    ["Aqua"] = new Dictionary<string, int> { ["INT"] = 0, ["DEX"] = 0, ["VAP"] = 0, ["MOI"] = 0, ["LCK"] = 0 },
    ["Dark"] = new Dictionary<string, int> { ["UMB"] = 0, ["VOI"] = 0, ["ABY"] = 0, ["ECL"] = 0, ["LCK"] = 0 }
  };

  public static Dictionary<string, Dictionary<string, int>> values { get; private set; } = Clone(DefaultValues);

  public static void ResetToDefaults() {
    values = Clone(DefaultValues);
    EnsureAllKnownForms();
  }

  public static void EnsureAllKnownForms() {
    foreach (var form in FormStatIncreases.increases) {
      EnsureForm(form.Key);
    }
  }

  public static void EnsureForm(string formName) {
    if (string.IsNullOrWhiteSpace(formName)) {
      return;
    }

    if (!values.TryGetValue(formName, out var stats) || stats == null) {
      stats = CreateDefaultFormStats(formName);
      values[formName] = stats;
    }

    if (!FormStatIncreases.increases.TryGetValue(formName, out var statMap) || statMap == null) {
      return;
    }

    foreach (var stat in statMap.Keys) {
      if (!stats.ContainsKey(stat)) {
        stats[stat] = 0;
      }
    }
  }

  public static int GetValue(string formName, string statName) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    var resolvedStat = FormStatIncreases.ResolveMajorStatKey(resolvedForm, statName);
    if (string.IsNullOrWhiteSpace(resolvedForm) || string.IsNullOrWhiteSpace(resolvedStat)) {
      return 0;
    }

    EnsureForm(resolvedForm);
    if (!values.TryGetValue(resolvedForm, out var stats) || stats == null) {
      return 0;
    }

    return stats.TryGetValue(resolvedStat, out var value) ? value : 0;
  }

  public static int GetDefaultValue(string formName, string statName) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    var resolvedStat = FormStatIncreases.ResolveMajorStatKey(resolvedForm, statName);
    if (string.IsNullOrWhiteSpace(resolvedForm) || string.IsNullOrWhiteSpace(resolvedStat)) {
      return 0;
    }

    if (!DefaultValues.TryGetValue(resolvedForm, out var defaults) || defaults == null) {
      return 0;
    }

    return defaults.TryGetValue(resolvedStat, out var value) ? value : 0;
  }

  static Dictionary<string, Dictionary<string, int>> Clone(Dictionary<string, Dictionary<string, int>> source) {
    var clone = new Dictionary<string, Dictionary<string, int>>();
    foreach (var form in source) {
      clone[form.Key] = new Dictionary<string, int>(form.Value);
    }
    return clone;
  }

  static Dictionary<string, int> CreateDefaultFormStats(string formName) {
    if (DefaultValues.TryGetValue(formName, out var defaults)) {
      return new Dictionary<string, int>(defaults);
    }

    var valuesForForm = new Dictionary<string, int>();
    if (!FormStatIncreases.increases.TryGetValue(formName, out var statMap) || statMap == null) {
      return valuesForForm;
    }

    foreach (var stat in statMap.Keys) {
      valuesForForm[stat] = 0;
    }
    return valuesForForm;
  }
}

public static class AllStatValues {
  public static Dictionary<string, float> Esperanza { set; get; } = new Dictionary<string, float> {
    { "DMG", 0 }, { "DCHC", 0 }, { "HP", 0 }, { "NRGRG", 0 }, { "CDMG", 0 }, { "NRG", 0 }, { "HPRG", 0 }, { "ARM", 0 }, { "HEAL", 0 },
    { "CCHC", 0 }, { "LDMG", 0 }, { "LCHC", 0 }, { "DDMG", 0 }, { "BONUS", 0 }, { "MVSP", 0 }, { "AKSP", 0 }, { "CDST", 0 }, { "LDSC", 0 }, { "FDMG", 0 },
    { "AREA", 0 }, { "DUR", 0 }, { "AFT", 0 }, { "EVD", 0 }, { "CLN", 0 }, { "FEAR", 0 }, { "SPEC", 0 }, { "PEN", 0 }
  };
}
