using System;
using System.Collections.Generic;

public static class Abbreviations {
  public static Dictionary<string, string> all { get; } = new Dictionary<string, string> {
    { "STR", "Strength" }, { "DEX", "Dexterity" }, { "END", "Endurance" }, { "INT", "Intelligence" }, { "LCK", "Luck" }, { "AMP", "Amperage" },
    { "VLT", "Voltage" }, { "PYR", "Pyro" }, { "EMB", "Ember" }, { "CHL", "Chill" }, { "ICI", "Icicle" }, { "VAP", "Vapor" }, { "MOI", "Moist" },
    { "UMB", "Umbral" }, { "VOI", "Void" }, { "ABY", "Abyss" }, { "ECL", "Eclipse" },
    { "HP", "Health Points" }, { "HPRG", "Health Point Regeneration" }, 
    { "ARM", "Armor" }, { "DMG", "Damage" }, { "AKSP", "Attack Speed" }, 
    { "NRG", "Energy" }, { "NRGRG", "Energy Regeneration" }, 
    { "DCHC", "Direct Chance" }, { "DDMG", "Direct Damage" }, 
    { "CCHC", "Critical Chance" }, { "CDMG", "Critical Damage" }, 
    { "LCHC", "Lucky Chance" }, { "LDMG", "Lucky Damage" }, { "HEAL", "Healing" }, { "BNS", "Bonus" },
    { "CDST", "Closing Distance" }, { "LDSC", "Lightning Discharge" }, { "FDMG", "Flame Damage" }, { "AREA", "Area" }, { "DUR", "Duration" },
    { "AFT", "After Effect" }, { "EVD", "Evade" }, { "CLN", "Cleanse" }, { "FEAR", "Fear" }, { "SPEC", "Spectral" }, { "PEN", "Penetration" },
    { "MVSP", "Movement Speed" }, 

    { "RK", "Right Kick" }, { "LK", "Left Kick" }, { "RP", "Right Punch" }, { "LP", "Left Punch" }, { "SB", "Super Blast" }, { "SK", "Super Kick" }, 
    { "BK", "Block" }, { "DO", "Dodge" }, { "JP", "Jump" }, { "DS", "Dash" },

    { "SH", "Shock" }, { "CL", "Chain Lighting" }, { "ST", "Static" }, { "LB", "Lightning Bolt" }, { "TB", "Thunder Bolt" }, { "OR", "Orbit" }, 
    { "ID", "Instant Dodge" }, { "DD", "Double Dodge" }, { "DJ", "Double Jump" }, 

    { "FT", "Flamethrower" }, { "BW", "Burning Wall" }, { "BZ", "Blaze" }, { "PL", "Pyre Light" }, { "FS", "Flame Shield" }, { "MT", "Meteor" }, { "FI", "Fissure" }, 
    { "BD", "Burning Dash" }, { "FW", "Flame Wings" }, 
  
    { "FC", "Frost Cloud" }, { "IB", "Ice Blast" }, { "IT", "Iceclitite" }, { "IM", "Iceclimite" }, { "IS", "Ice Shield" }, { "AV", "Avalanche" }, { "BL", "Blizzard" }, 
    { "SL", "Slide" }, { "FF", "Frost Float" }, 

    { "WB", "Water Blast" }, { "CH", "Crushing Hydro" }, { "WS", "Water Sphere" }, { "PD", "Pressure Deluge" }, { "RN", "Rain Needles" }, { "TS", "Tsunami Strike" }, 
    { "BB", "Bubble" }, { "VD", "Vapor Dash" },

    { "RI", "Rip" }, { "TR", "Tear" }, { "RW", "Rage" }, { "SE", "Seethe" }, { "SS", "Soul Siphon" }, { "SI", "Soul Infection" },
    { "CK", "Corrupt Kinesis" }, { "SW", "Shadow Walk" },
  };

  public static Dictionary<string, List<string>> structure { get; } = new Dictionary<string, List<string>> {
    ["Major"] = new List<string> { "STR", "DEX", "END", "INT", "LCK", "AMP", "VLT", "PYR", "EMB", "CHL", "ICI", "VAP", "MOI", "UMB", "VOI", "ABY", "ECL", },
    ["Minor"] = new List<string> { "HP", "HPRG", "ARM", "DMG", "AKSP", "NRG", "NRGRG", "DCHC", "DDMG", "CCHC", "CDMG", "LCHC", "LDMG", "HEAL", "BNS", "CDST", "LDSC", "FDMG", "AREA", "DUR", "AFT", "EVD", "CLN", "FEAR", "SPEC", "PEN", "MVSP" },
    ["Ability"] = new List<string> { "RK", "LK", "RP", "LP", "BK", "DO", "JP", "SB", "SK", "SH", "CL", "ST", "LB", "ID", "DD", "DJ", "TB", "OR", "FT", "BW", "BZ", "PL", "FS", "BD", "FW", "MT", "FI", "FC", "IB", "IT", "IM", "IS", "SL", "FF", "AV", "BL", "WB", "CH", "WS", "PD", "BB", "VD", "DV", "RN", "TS", "RI", "TR", "RW", "SE", "CK", "SW", "AC", "SS", "SI" },
  };

  public static Dictionary<string, Dictionary<string, List<string>>> FormMajorMinor { get; } = new Dictionary<string, Dictionary<string, List<string>>> {
    ["Base"] = new Dictionary<string, List<string>> {
      ["STR"] = new List<string> { "HP", "DMG", "DCHC", },
      ["DEX"] = new List<string> { "AKSP", "NRGRG", "CDMG", },
      ["END"] = new List<string> { "NRG", "HPRG", "ARM", },
      ["INT"] = new List<string> { "HEAL", "CCHC", "LDMG", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BNS", }
    },
    ["Bolt"] = new Dictionary<string, List<string>> {
      ["DEX"] = new List<string> { "DMG", "MVSP", "AKSP", },
      ["END"] = new List<string> { "NRG", "NRGRG", "HP", },
      ["AMP"] = new List<string> { "CDST", "HPRG", "ARM", },
      ["VLT"] = new List<string> { "LDSC", "CCHC", "HEAL", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BNS", }
    },
    ["Fire"] = new Dictionary<string, List<string>> {
      ["STR"] = new List<string> { "DMG", "DCHC", "HPRG", },
      ["END"] = new List<string> { "NRG", "NRGRG", "HP", },
      ["PYR"] = new List<string> { "FDMG", "AKSP", "ARM", },
      ["EMB"] = new List<string> { "AREA", "CCHC", "HEAL", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BNS", }
    },
    ["Cold"] = new Dictionary<string, List<string>> {
      ["END"] = new List<string> { "DMG", "NRGRG", "NRG", },
      ["INT"] = new List<string> { "HEAL", "CCHC", "LDMG", },
      ["CHL"] = new List<string> { "DUR", "HP", "ARM", },
      ["ICI"] = new List<string> { "AFT", "CCHC", "HEAL", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BNS", }
    },
    ["Aqua"] = new Dictionary<string, List<string>> {
      ["INT"] = new List<string> { "DMG", "HEAL", "CCHC", },
      ["DEX"] = new List<string> { "AKSP", "NRGRG", "CDMG", },
      ["VAP"] = new List<string> { "EVD", "NRG", "ARM", },
      ["MOI"] = new List<string> { "CLN", "CCHC", "HP", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BNS", }
    },
    ["Dark"] = new Dictionary<string, List<string>> {
      ["UMB"] = new List<string> { "DMG", "NRG", "FEAR", },
      ["VOI"] = new List<string> { "SPEC", "AKSP", "CCHC", },
      ["ABY"] = new List<string> { "PEN", "ARM", "LDMG", },
      ["ECL"] = new List<string> { "EVD", "NRGRG", "AREA", },
      ["LCK"] = new List<string> { "LCHC", "CDMG", "BNS", }
    }
  };

  public static Dictionary<string, string> descriptions { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
    ["HP"] = "Health Points - reduce to 0 leads to death",
    ["HPRG"] = "Health Point Regeneration - rate at which health is restored over time",
    ["ARM"] = "Armor - reduces incoming physical and elemental damage",
    ["DMG"] = "Damage - base damage multiplier applied to all attacks",
    ["AKSP"] = "Attack Speed - increases the speed of attack animations and cooldown recovery",
    ["NRG"] = "Energy - maximum capacity for casting abilities and special moves",
    ["NRGRG"] = "Energy Regeneration - rate at which energy is restored over time",
    ["DCHC"] = "Direct Chance - probability to deal direct hits ignoring resistance",
    ["DDMG"] = "Direct Damage - bonus damage multiplier dealt on direct hits",
    ["CCHC"] = "Critical Chance - probability to score a critical hit on enemies",
    ["CDMG"] = "Critical Damage - damage multiplier applied when scoring critical hits",
    ["LCHC"] = "Lucky Chance - probability to trigger fortunate bonus effects in combat",
    ["LDMG"] = "Lucky Damage - extra damage multiplier applied on lucky strikes",
    ["HEAL"] = "Healing - increases the effectiveness of healing effects and restoration",
    ["BNS"] = "Bonus - amplifies overall reward and bonus effect scaling",
    ["CDST"] = "Closing Distance - increases reach and movement speed toward targets",
    ["LDSC"] = "Lightning Discharge - chance and intensity of chain lightning arcs",
    ["FDMG"] = "Flame Damage - increases fire and burn over time damage dealt",
    ["AREA"] = "Area - expands the radius and area of effect for abilities",
    ["DUR"] = "Duration - extends the active duration of buffs and area effects",
    ["AFT"] = "After Effect - enhances lingering secondary effects after attack completion",
    ["EVD"] = "Evade - chance to completely dodge incoming enemy attacks",
    ["CLN"] = "Cleanse - reduces negative status effect duration and resistance",
    ["FEAR"] = "Fear - chance to terrify enemies and cause them to flee",
    ["SPEC"] = "Spectral - enhances ghostly and umbral ability effectiveness",
    ["PEN"] = "Penetration - ignores a percentage of enemy armor and defenses",
    ["MVSP"] = "Movement Speed - increases character running and traversal speed",
  };

  public static string GetDescription(string statName) {
    if (string.IsNullOrWhiteSpace(statName)) {
      return "";
    }
    if (descriptions.TryGetValue(statName, out var desc)) {
      return desc;
    }
    var name = all.TryGetValue(statName, out var full) ? full : statName;
    return $"{statName} = {name}";
  }
}
