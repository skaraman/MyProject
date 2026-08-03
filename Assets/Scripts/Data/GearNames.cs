using System.Collections.Generic;

public static class GearNames {
  const string SuffixKey = "suffix";

  public static Dictionary<string, Dictionary<string, List<string>>> names { get; } = new Dictionary<string, Dictionary<string, List<string>>> {
    ["STR"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Durable", "Firm", "Stable", "Tough", },
      ["suffix"] = new List<string> { "Courage", "Clout", "Power", "Vigor", "Brawn", "Force", "Strength", }
    },
    ["DEX"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Fast", "Tactical", "Proficient", "Handy", },
      ["suffix"] = new List<string> { "Artistry", "Finesse", "Knack", "Nimbleness", "Readiness", "Dexterity", },
    },
    ["END"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Tenacious", "Resolute", "Tolerant", "Continuing", },
      ["suffix"] = new List<string> { "Fortitude", "Vitality", "Mettle", "Withstanding", "Forbearance", "Endurance", },
    },
    ["INT"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Brilliant", "Clever", "Alert", "Bright", "Savvy" },
      ["suffix"] = new List<string> { "Ingenuity", "Understanding", "Wit", "Comprehension", "Savvy", "Intelligence", },
    },
    ["LCK"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Fortunate", "Godsend", "Victorious", },
      ["suffix"] = new List<string> { "Advantage", "Blessing", "Opportunity", "Prosperity", "Serendipity", "Luck", },
    },
    ["AMP"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Extended", },
      ["suffix"] = new List<string> { "Amplitude", },
    },
    ["VLT"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Surging", },
      ["suffix"] = new List<string> { "Voltage", },
    },
    ["PYR"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Mindfire", },
      ["suffix"] = new List<string> { "Pyrokinesis", },
    },
    ["EMB"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Emblazed", },
      ["suffix"] = new List<string> { "Emblaze", },
    },
    ["CHL"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Chilling", },
      ["suffix"] = new List<string> { "Chill", },
    },
    ["ICI"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Frozen", },
      ["suffix"] = new List<string> { "Icicle", },
    },
    ["VAP"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Wispy", },
      ["suffix"] = new List<string> { "Vapor", },
    },
    ["MOI"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Wet", },
      ["suffix"] = new List<string> { "Moisture", },
    },
    ["UMB"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Dark", },
      ["suffix"] = new List<string> { "Umbral", },
    },
    ["VOI"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Empty", },
      ["suffix"] = new List<string> { "Void", },
    },
    ["ABY"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Abysmal", },
      ["suffix"] = new List<string> { "Dread", },
    },
    ["ECL"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Shadowy", },
      ["suffix"] = new List<string> { "Eclipse", },
    },
    ["HP"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Healthy", },
      ["suffix"] = new List<string> { "Health", },
    },
    ["HPRG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Regenerative", },
      ["suffix"] = new List<string> { "Regeneration", },
    },
    ["ARM"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Protective", },
      ["suffix"] = new List<string> { "Protection", },
    },
    ["DMG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Damaging", },
      ["suffix"] = new List<string> { "Damage", },
    },
    ["AKSP"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Speedy", },
      ["suffix"] = new List<string> { "Speed", },
    },
    ["NRG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Energetic", },
      ["suffix"] = new List<string> { "Energy", },
    },
    ["NRGRG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Restorative", },
      ["suffix"] = new List<string> { "Restoration", },
    },
    ["DCHC"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Direct", },
      ["suffix"] = new List<string> { "Precision", },
    },
    ["DDMG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Destructive", },
      ["suffix"] = new List<string> { "Destruction", },
    },
    ["CCHC"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Critical", },
      ["suffix"] = new List<string> { "Priority", },
    },
    ["CDMG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Potent", },
      ["suffix"] = new List<string> { "Pain", },
    },
    ["LCHC"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Lucky", },
      ["suffix"] = new List<string> { "Luck", },
    },
    ["LDMG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Boon", },
      ["suffix"] = new List<string> { "Windfall", },
    },
    ["HEAL"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Mending", },
      ["suffix"] = new List<string> { "Healing", },
    },
    ["BNS"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Bonus", },
      ["suffix"] = new List<string> { "Bonus", },
    },
    ["CDST"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Zipping", },
      ["suffix"] = new List<string> { "Snapping", },
    },
    ["LDSC"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Static", },
      ["suffix"] = new List<string> { "Discharge", },
    },
    ["FDMG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Flaming", },
      ["suffix"] = new List<string> { "Flame", },
    },
    ["AREA"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Mighty", },
      ["suffix"] = new List<string> { "Area", },
    },
    ["DUR"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Lasting", },
      ["suffix"] = new List<string> { "Duration", },
    },
    ["AFT"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Residual", },
      ["suffix"] = new List<string> { "Effect", },
    },
    ["EVD"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Evasive", },
      ["suffix"] = new List<string> { "Evasion", },
    },
    ["CLN"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Cleansing", },
      ["suffix"] = new List<string> { "Cleaning", },
    },
    ["FEAR"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Fearful", },
      ["suffix"] = new List<string> { "Fear", },
    },
    ["SPEC"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Spectral", },
      ["suffix"] = new List<string> { "Specter", },
    },
    ["PEN"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Penetrating", },
      ["suffix"] = new List<string> { "Penetration", },
    },
    ["MVSP"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Swift", },
      ["suffix"] = new List<string> { "Swiftness", },
    }
  };

  public static string Generate(string gearId, string slot, IList<BoostEntry> boosts) {
    var baseName = ResolveBaseName(gearId, slot);
    if (boosts == null || boosts.Count == 0 || boosts[0] == null) {
      return baseName;
    }

    var statName = Normalize(boosts[0].statName);
    if (string.IsNullOrEmpty(statName) ||
        !names.TryGetValue(statName, out var statNames) ||
        statNames == null ||
        !statNames.TryGetValue(SuffixKey, out var suffixes) ||
        suffixes == null ||
        suffixes.Count == 0 ||
        string.IsNullOrWhiteSpace(suffixes[0])) {
      return baseName;
    }

    return baseName + " of " + suffixes[0].Trim();
  }

  static string ResolveBaseName(string gearId, string slot) {
    var normalizedGearId = Normalize(gearId);
    var normalizedSlot = string.IsNullOrWhiteSpace(slot) ? "" : slot.Trim();
    var gearPartId = string.IsNullOrEmpty(normalizedSlot)
      ? normalizedGearId
      : normalizedGearId + "_" + normalizedSlot;

    if (EsperanzaGearParts.GearNames.TryGetValue(gearPartId, out var authoredName) &&
        !string.IsNullOrWhiteSpace(authoredName)) {
      return authoredName.Trim();
    }

    var separatorIndex = normalizedGearId.IndexOf('_');
    var formName = separatorIndex > 0
      ? normalizedGearId.Substring(0, separatorIndex)
      : normalizedGearId;
    return (formName + " " + normalizedSlot).Trim();
  }

  static string Normalize(string value) {
    return string.IsNullOrWhiteSpace(value)
      ? ""
      : value.Trim().ToUpperInvariant();
  }
}
