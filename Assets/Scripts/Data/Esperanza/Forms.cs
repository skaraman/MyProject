using System.Collections.Generic;

public static class EsperanzaForms {
  public static Dictionary<string, int> Active { get; set; } = new Dictionary<string, int> { { "Base", 1 }, { "Bolt", 0 }, { "Cold", 0 }, { "Fire", 0 }, { "Aqua", 0 }, { "Dark", 0 } };

  public static Dictionary<string, int> Unlocked { get; set; } = new Dictionary<string, int> { { "Base", 1 }, { "Bolt", 0 }, { "Aqua", 0 }, { "Cold", 0 }, { "Fire", 0 }, { "Dark", 0 } };

  public static void SetActive(string v) {
    foreach (var item in Active) {
      if (item.Key == v) { Unlocked[item.Key] = 1; }
      else { Unlocked[item.Key] = 0; }
    }
  }

  public static string GetActive() {
    var v = "";
    foreach (var item in Active) {
      if (item.Value == 1) {
        v = item.Key;
      }
    }
    return v;
  }

  public static void UnlockForm(string v) {
    if (Unlocked.ContainsKey(v)) {
      Unlocked[v] = 1;
    }
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
      ["superattack1"] = "SuperBlast",
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