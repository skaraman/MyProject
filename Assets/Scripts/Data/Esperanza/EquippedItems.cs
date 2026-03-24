using System;
using System.Collections.Generic;

public static class EquippedItems {
  static readonly Dictionary<string, Dictionary<string, GearItem>> DefaultGearForms = CreateDefaultGearForms();

  public static Dictionary<string, Dictionary<string, GearItem>> AllGearForms { get; private set; } = CloneGearForms(DefaultGearForms);

  public static void ResetToDefaults() {
    AllGearForms = CloneGearForms(DefaultGearForms);
    EnsureKnownForms();
  }

  public static void EnsureKnownForms() {
    foreach (var form in EsperanzaForms.KnownForms) {
      EnsureForm(form);
    }
  }

  public static void EnsureForm(string formName) {
    if (string.IsNullOrWhiteSpace(formName)) {
      return;
    }

    if (!AllGearForms.TryGetValue(formName, out var slots) || slots == null) {
      slots = DefaultGearForms.TryGetValue(formName, out var defaults)
        ? CloneGearSlots(defaults)
        : CreateEmptyGearSlots();
      AllGearForms[formName] = slots;
    }

    var defaultSlots = DefaultGearForms.TryGetValue(formName, out var formDefaults)
      ? formDefaults
      : CreateEmptyGearSlots();

    foreach (var slot in defaultSlots) {
      if (!slots.ContainsKey(slot.Key)) {
        slots[slot.Key] = CloneGearItem(slot.Value);
      }
    }
  }

  public static GearItem CloneGearItem(GearItem gearItem) {
    if (gearItem == null) {
      return null;
    }

    var boosts = new List<BoostEntry>();
    if (gearItem.boosts != null) {
      for (var i = 0; i < gearItem.boosts.Count; i++) {
        var boost = gearItem.boosts[i];
        if (boost == null) {
          continue;
        }

        boosts.Add(new BoostEntry {
          statName = boost.statName,
          value = boost.value
        });
      }
    }

    return new GearItem {
      type = gearItem.type,
      name = gearItem.name,
      slot = gearItem.slot,
      gearId = gearItem.gearId,
      gearColor = gearItem.gearColor,
      boosts = boosts
    };
  }

  static Dictionary<string, Dictionary<string, GearItem>> CreateDefaultGearForms() {
    return new Dictionary<string, Dictionary<string, GearItem>>(StringComparer.Ordinal) {
      ["Base"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateGearItem("Normal", "Regular Top", "Chest", "Base_aa", "Brown"),
        ["Legs"] = CreateGearItem("Normal", "Regular Bottoms", "Legs", "Base_aa", "Brown"),
        ["Feet"] = CreateGearItem("Normal", "Regular Boots", "Feet", "Base_aa", "Brown"),
        ["Head"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Aqua"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateGearItem("Normal", "Wetsuit Top", "Chest", "Aqua_aa", "LightBlue"),
        ["Legs"] = CreateGearItem("Normal", "Wetsuit Bottoms", "Legs", "Aqua_aa", "LightBlue"),
        ["Head"] = null, ["Feet"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Bolt"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateGearItem("Normal", "Anti-Static Top", "Chest", "Bolt_aa", "Grey"),
        ["Legs"] = CreateGearItem("Normal", "Anti-Static Pants", "Legs", "Bolt_aa", "Grey"),
        ["Feet"] = CreateGearItem("Normal", "Anti-static Boots", "Feet", "Bolt_aa", "Grey"),
        ["Head"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Cold"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateGearItem("Normal", "Warm Top", "Chest", "Cold_aa", "DarkBlue"),
        ["Legs"] = CreateGearItem("Normal", "Warm Bottoms", "Legs", "Cold_aa", "DarkBlue"),
        ["Feet"] = CreateGearItem("Normal", "Warm Footies", "Feet", "Cold_aa", "DarkBlue"),
        ["Head"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Fire"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateGearItem("Normal", "Sheer Top", "Chest", "Fire_aa", "Yellow"),
        ["Legs"] = CreateGearItem("Normal", "Skimmies", "Legs", "Fire_aa", "Yellow"),
        ["Head"] = null, ["Feet"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Dark"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateGearItem("Normal", "Void Shirt", "Chest", "Dark_aa", "DarkPurple"),
        ["Legs"] = CreateGearItem("Normal", "Void Pants", "Legs", "Dark_aa", "DarkPurple"),
        ["Feet"] = CreateGearItem("Normal", "Void Footies", "Feet", "Dark_aa", "DarkPurple"),
        ["Arms"] = CreateGearItem("Normal", "Void Gloves", "Arms", "Dark_aa", "DarkPurple"),
        ["Head"] = null, ["Shoulders"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      }
    };
  }

  static Dictionary<string, Dictionary<string, GearItem>> CloneGearForms(Dictionary<string, Dictionary<string, GearItem>> source) {
    var clone = new Dictionary<string, Dictionary<string, GearItem>>(StringComparer.Ordinal);
    foreach (var form in source) {
      clone[form.Key] = CloneGearSlots(form.Value);
    }
    return clone;
  }

  static Dictionary<string, GearItem> CloneGearSlots(Dictionary<string, GearItem> source) {
    var clone = new Dictionary<string, GearItem>(StringComparer.Ordinal);
    foreach (var slot in source) {
      clone[slot.Key] = CloneGearItem(slot.Value);
    }
    return clone;
  }

  static Dictionary<string, GearItem> CreateEmptyGearSlots() {
    var emptySlots = new Dictionary<string, GearItem>(StringComparer.Ordinal);
    foreach (var slot in DefaultGearForms["Base"].Keys) {
      emptySlots[slot] = null;
    }
    return emptySlots;
  }

  static GearItem CreateGearItem(string type, string name, string slot, string gearId, string gearColor) {
    return new GearItem {
      type = type,
      name = name,
      slot = slot,
      gearId = gearId,
      gearColor = gearColor,
      boosts = new List<BoostEntry>()
    };
  }
}
