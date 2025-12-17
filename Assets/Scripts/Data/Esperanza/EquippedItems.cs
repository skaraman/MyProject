using System.Collections.Generic;

public static class EquippedItems {
  public static Dictionary<string, GearItem> Base { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Regular Top", gearId = "Base_aa", gearColor = "Brown", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Regular Bottoms", gearId = "Base_aa", gearColor = "Brown", boosts = new List<BoostEntry>() } },
    { "Feet", new GearItem { type = "Normal", name = "Regular Boots", gearId = "Base_aa", gearColor = "Brown", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null },
    { "Ring1", null }, { "Ring2", null }, { "Ring3", null }, { "Ring4", null }, { "Ring5", null },
    { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };

  public static Dictionary<string, GearItem> Aqua { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Wetsuit Top", gearId = "Aqua_aa", gearColor = "LightBlue", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Wetsuit Bottoms", gearId = "Aqua_aa", gearColor = "LightBlue", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Feet", null }, { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null },
    { "Ring1", null }, { "Ring2", null }, { "Ring3", null }, { "Ring4", null }, { "Ring5", null },
    { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };

  public static Dictionary<string, GearItem> Bolt { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Anti-Static Top", gearId = "Bolt_aa", gearColor = "Grey", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Anti-Static Pants", gearId = "Bolt_aa", gearColor = "Grey", boosts = new List<BoostEntry>() } },
    { "Feet", new GearItem { type = "Normal", name = "Anti-static Boots", gearId = "Bolt_aa", gearColor = "Grey", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null },
    { "Ring1", null }, { "Ring2", null }, { "Ring3", null }, { "Ring4", null }, { "Ring5", null },
    { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };

  public static Dictionary<string, GearItem> Cold { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Warm Top", gearId = "Cold_aa", gearColor = "DarkBlue", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Warm Bottoms", gearId = "Cold_aa", gearColor = "DarkBlue", boosts  = new List<BoostEntry>() } },
    { "Feet", new GearItem { type = "Normal", name = "Warm Footies", gearId = "Cold_aa", gearColor = "DarkBlue", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null },
    { "Ring1", null }, { "Ring2", null }, { "Ring3", null }, { "Ring4", null }, { "Ring5", null },
    { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };

  public static Dictionary<string, GearItem> Fire { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Sheer Top", gearId = "Fire_aa", gearColor = "Yellow", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Skimmies", gearId = "Fire_aa", gearColor = "Yellow", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Feet", null }, { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null },
    { "Ring1", null }, { "Ring2", null }, { "Ring3", null }, { "Ring4", null }, { "Ring5", null },
    { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };

  public static Dictionary<string, GearItem> Dark { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Void Shirt", gearId = "Dark_aa", gearColor = "DarkPurple", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Void Pants", gearId = "Dark_aa", gearColor = "DarkPurple", boosts = new List<BoostEntry>() } },
    { "Feet", new GearItem { type = "Normal", name = "Void Footies", gearId = "Dark_aa", gearColor = "DarkPurple", boosts = new List<BoostEntry>() } },
    { "Arms", new GearItem { type = "Normal", name = "Void Gloves", gearId = "Dark_aa", gearColor = "DarkPurple", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Shoulders", null }, { "Belt", null }, { "Zemi", null },
    { "Ring1", null }, { "Ring2", null }, { "Ring3", null }, { "Ring4", null }, { "Ring5", null },
    { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };

  public static Dictionary<string, Dictionary<string, GearItem>> AllGearForms { get; } = new Dictionary<string, Dictionary<string, GearItem>> {
    { "Base", Base }, { "Aqua", Aqua }, { "Bolt", Bolt }, { "Cold", Cold }, { "Fire", Fire }, { "Dark", Dark }
  };

}