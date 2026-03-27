using System.Collections.Generic;

public static class Abbreviations {
  public static Dictionary<string, string> all { get; } = new Dictionary<string, string> {
    { "STR", "Strength" }, { "DEX", "Dexterity" }, { "END", "Endurance" }, { "INT", "Intelligence" }, { "LCK", "Luck" }, { "AMP", "Amperage" },
    { "VLT", "Voltage" }, { "PYR", "Pyro" }, { "EMB", "Ember" }, { "CHL", "Chill" }, { "ICI", "Icicle" }, { "VAP", "Vapor" }, { "MOI", "Moist" },
    { "UMB", "Umbral" }, { "VOI", "Void" }, { "ABY", "Abyss" }, { "ECL", "Eclipse" }, { "HP", "Health Points" },
    { "HPRG", "Health Point Regeneration" }, { "ARM", "Armor" }, { "DMG", "Damage" }, { "AKSP", "Attack Speed" }, { "NRG", "Energy" },
    { "NRGRG", "Energy Regeneration" }, { "DCHC", "Direct Chance" }, { "DDMG", "Direct Damage" }, { "CCHC", "Critical Chance" },
    { "CDMG", "Critical Damage" }, { "LCHC", "Lucky Chance" }, { "LDMG", "Lucky Damage" }, { "HEAL", "Healing" }, { "BNS", "Bonus" },
    { "CDST", "Closing Distance" }, { "LDSC", "Lightning Discharge" }, { "FDMG", "Flame Damage" }, { "AREA", "Area" }, { "DUR", "Duration" },
    { "AFT", "After Effect" }, { "EVD", "Evade" }, { "CLN", "Cleanse" }, { "FEAR", "Fear" }, { "SPEC", "Spectral" }, { "PEN", "Penetration" },
    { "MVSP", "Movement Speed" }, { "RK", "Right Kick" }, { "LK", "Left Kick" }, { "RP", "Right Punch" }, { "LP", "Left Punch" },
    { "BK", "Block" }, { "DO", "Dodge" }, { "JP", "Jump" }, { "SB", "Super Blast" }, { "SK", "Super Kick" }, { "SH", "Shock" },
    { "CL", "Chain Lighting" }, { "ST", "Static" }, { "LB", "Lightning Bolt" }, { "ID", "Instant Dodge" }, { "DD", "Double Dodge" },
    { "DJ", "Double Jump" }, { "TB", "Thunder Bolt" }, { "OR", "Orbit" }, { "FT", "Flamethrower" }, { "BW", "Burning Wall" }, { "BZ", "Blaze" },
    { "PL", "Pyre Light" }, { "FS", "Flame Shield" }, { "BD", "Burning Dash" }, { "FW", "Flame Wings" }, { "MT", "Meteor" }, { "FI", "Fissure" },
    { "FC", "Frost Cloud" }, { "IB", "Ice Blast" }, { "IT", "Iceclitite" }, { "IM", "Iceclimite" }, { "IS", "Ice Shield" }, { "SL", "Slide" },
    { "FF", "Frost Float" }, { "AV", "Avalanche" }, { "BL", "Blizzard" }, { "WB", "Water Blast" }, { "CH", "Crushing Hydro" },
    { "WS", "Water Sphere" }, { "PD", "Pressure Deluge" }, { "BB", "Bubble" }, { "VD", "Vapor Dash" }, { "DV", "Diving Vortex" },
    { "RN", "Rain Needles" }, { "TS", "Tsunami Strike" }, { "RP", "Rip" }, { "TR", "Tear" }, { "RW", "Rage" }, { "SE", "Seethe" },
    { "CK", "Corrupt Kinesis" }, { "SW", "Shadow Walk" }, { "AC", "Abyssal Call" }, { "SS", "Soul Siphon" }, { "SI", "Soul Infection" }
  };

  public static Dictionary<string, List<string>> structure { get; } = new Dictionary<string, List<string>> {
    ["Major"] = new List<string> { "STR", "DEX", "END", "INT", "LCK", "AMP", "VLT", "PYR", "EMB", "CHL", "ICI", "VAP", "MOI", "UMB", "VOI", "ABY", "ECL", },
    ["Minor"] = new List<string> { "HP", "HPRG", "ARM", "DMG", "AKSP", "NRG", "NRGRG", "DCHC", "DDMG", "CCHC", "CDMG", "LCHC", "LDMG", "HEAL", "BNS", "CDST", "LDSC", "FDMG", "AREA", "DUR", "AFT", "EVD", "CLN", "FEAR", "SPEC", "PEN", "MVSP" },
    ["Ability"] = new List<string> { "RK", "LK", "RP", "LP", "BK", "DO", "JP", "SP", "SK", "SH", "CL", "ST", "LB", "ID", "DD", "DJ", "TB", "OR", "FT", "BW", "BZ", "PL", "FS", "BD", "FW", "MT", "FI", "FC", "IB", "IT", "IM", "IS", "SL", "FF", "AV", "BL", "WB", "CH", "WS", "PD", "BB", "VD", "DV", "RN", "TS", "RP", "TR", "RW", "SE", "CK", "SW", "AC", "SS", "SI" },
  };

  public static Dictionary<string, Dictionary<string, List<string>>> FormMajorMinor { get; } = new Dictionary<string, Dictionary<string, List<string>>> {
    ["Base"] = new Dictionary<string, List<string>> {
      ["STR"] = new List<string> { "HP", "DMG", "DCHC", },
      ["DEX"] = new List<string> { "AKSP", "NRGRG", "CDMG", },
      ["END"] = new List<string> { "NRG", "HPRG", "ARM", },
      ["INT"] = new List<string> { "HEAL", "CCHC", "LDMG", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BONUS", }
    },
    ["Bolt"] = new Dictionary<string, List<string>> {
      ["DEX"] = new List<string> { "DMG", "MVSP", "AKSP", },
      ["END"] = new List<string> { "NRG", "NRGRG", "HP", },
      ["AMP"] = new List<string> { "CDST", "HPRG", "ARM", },
      ["VLT"] = new List<string> { "LDSC", "CCHC", "HEAL", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BONUS", }
    },
    ["Fire"] = new Dictionary<string, List<string>> {
      ["STR"] = new List<string> { "DMG", "DCHC", "HPRG", },
      ["END"] = new List<string> { "NRG", "NRGRG", "HP", },
      ["PYR"] = new List<string> { "FDMG", "AKSP", "ARM", },
      ["EMB"] = new List<string> { "AREA", "CCHC", "HEAL", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BONUS", }
    },
    ["Cold"] = new Dictionary<string, List<string>> {
      ["END"] = new List<string> { "DMG", "NRGRG", "NRG", },
      ["INT"] = new List<string> { "HEAL", "CCHC", "LDMG", },
      ["CHL"] = new List<string> { "DUR", "HP", "ARM", },
      ["ICI"] = new List<string> { "AFT", "CCHC", "HEAL", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BONUS", }
    },
    ["Aqua"] = new Dictionary<string, List<string>> {
      ["INT"] = new List<string> { "DMG", "HEAL", "CCHC", },
      ["DEX"] = new List<string> { "AKSP", "NRGRG", "CDMG", },
      ["VAP"] = new List<string> { "EVD", "NRG", "ARM", },
      ["MOI"] = new List<string> { "CLN", "CCHC", "HP", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BONUS", }
    },
    ["Dark"] = new Dictionary<string, List<string>> {
      ["UMB"] = new List<string> { "DMG", "NRG", "FEAR", },
      ["VOI"] = new List<string> { "SPEC", "AKSP", "CCHC", },
      ["ABY"] = new List<string> { "PEN", "ARM", "LDMG", },
      ["ECL"] = new List<string> { "EVD", "NRGRG", "AREA", },
      ["LCK"] = new List<string> { "LCHC", "CDMG", "BONUS", }
    }
  };
}