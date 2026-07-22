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

  public static void ApplySavedGearForms(
    Dictionary<string, Dictionary<string, GearItem>> targetForms,
    Dictionary<string, Dictionary<string, GearItem>> loadedForms
  ) {
    if (targetForms == null || loadedForms == null) return;

    foreach (var loadedForm in loadedForms) {
      var form = EsperanzaForms.ResolveFormKey(loadedForm.Key);
      if (string.IsNullOrWhiteSpace(form) || loadedForm.Value == null) {
        continue;
      }

      if (!targetForms.TryGetValue(form, out var targetSlots) || targetSlots == null) {
        continue;
      }

      foreach (var loadedSlot in loadedForm.Value) {
        var slot = ResolveSlotKey(targetSlots, loadedSlot.Key);
        if (string.IsNullOrWhiteSpace(slot)) {
          continue;
        }

        var gearItem = CloneGearItem(loadedSlot.Value);
        if (gearItem != null) {
          gearItem.slot = slot;
        }

        targetSlots[slot] = gearItem;
      }
    }
  }

  public static Dictionary<string, Dictionary<string, GearItem>> CreateDefaultGearFormsSnapshot() {
    return CloneGearForms(DefaultGearForms);
  }

  public static List<string> GetEquippedGearIds(IEnumerable<string> forms = null) {
    EnsureKnownForms();

    var result = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (forms == null) {
      forms = AllGearForms.Keys;
    }

    foreach (var form in forms) {
      var resolvedForm = EsperanzaForms.ResolveFormKey(form) ?? NormalizeToken(form);
      if (string.IsNullOrWhiteSpace(resolvedForm)) {
        continue;
      }

      EnsureForm(resolvedForm);
      if (!AllGearForms.TryGetValue(resolvedForm, out var slots) || slots == null || slots.Count <= 0) {
        continue;
      }

      foreach (var slotEntry in slots) {
        var gearId = NormalizeToken(slotEntry.Value != null ? slotEntry.Value.gearId : "");
        if (string.IsNullOrWhiteSpace(gearId) || !seen.Add(gearId)) {
          continue;
        }

        result.Add(gearId);
      }
    }

    return result;
  }

  public static string BuildGearPackId(string gearId, string leafCode) {
    var normalizedGearId = NormalizeToken(gearId);
    var normalizedLeafCode = NormalizeToken(leafCode);
    if (string.IsNullOrWhiteSpace(normalizedGearId) || string.IsNullOrWhiteSpace(normalizedLeafCode)) {
      return "";
    }

    return "Gear" + normalizedGearId.Replace(' ', '_') + "_" + normalizedLeafCode.Replace(' ', '_');
  }

  public static bool TryParseGearPackId(string packId, out string gearForm, out string gearCode, out string leafCode) {
    gearForm = "";
    gearCode = "";
    leafCode = "";

    var normalizedPackId = NormalizeToken(packId);
    string payload;
    if (normalizedPackId.StartsWith("Gear_", StringComparison.OrdinalIgnoreCase)) {
      payload = normalizedPackId.Substring("Gear_".Length);
    }
    else if (normalizedPackId.StartsWith("Gear", StringComparison.OrdinalIgnoreCase)) {
      payload = normalizedPackId.Substring("Gear".Length);
    }
    else {
      return false;
    }

    var parts = payload.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 3) {
      return false;
    }

    leafCode = parts[parts.Length - 1];
    gearCode = parts[parts.Length - 2];
    gearForm = string.Join("_", parts, 0, parts.Length - 2);
    return !string.IsNullOrWhiteSpace(gearForm) && !string.IsNullOrWhiteSpace(gearCode);
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  static string ResolveSlotKey(Dictionary<string, GearItem> targetSlots, string value) {
    if (targetSlots == null || string.IsNullOrWhiteSpace(value)) return null;

    var requestedSlot = value.Trim();
    foreach (var slot in targetSlots.Keys) {
      if (string.Equals(slot, requestedSlot, StringComparison.OrdinalIgnoreCase)) {
        return slot;
      }
    }

    return null;
  }

  static Dictionary<string, Dictionary<string, GearItem>> CreateDefaultGearForms() {
    return new Dictionary<string, Dictionary<string, GearItem>>(StringComparer.Ordinal) {
      ["Base"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateFormGearItem("Base", "Normal", "Regular Top", "Chest", "Base_aa"),
        ["Legs"] = CreateFormGearItem("Base", "Normal", "Regular Bottoms", "Legs", "Base_aa"),
        ["Feet"] = CreateFormGearItem("Base", "Normal", "Regular Boots", "Feet", "Base_aa"),
        ["Head"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Aqua"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateFormGearItem("Aqua", "Normal", "Wetsuit Top", "Chest", "Aqua_aa"),
        ["Legs"] = CreateFormGearItem("Aqua", "Normal", "Wetsuit Bottoms", "Legs", "Aqua_aa"),
        ["Head"] = null, ["Feet"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Bolt"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateFormGearItem("Bolt", "Normal", "Anti-Static Top", "Chest", "Bolt_aa"),
        ["Legs"] = CreateFormGearItem("Bolt", "Normal", "Anti-Static Pants", "Legs", "Bolt_aa"),
        ["Feet"] = CreateFormGearItem("Bolt", "Normal", "Anti-static Boots", "Feet", "Bolt_aa"),
        ["Head"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Cold"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateFormGearItem("Cold", "Normal", "Warm Top", "Chest", "Cold_aa"),
        ["Legs"] = CreateFormGearItem("Cold", "Normal", "Warm Bottoms", "Legs", "Cold_aa"),
        ["Feet"] = CreateFormGearItem("Cold", "Normal", "Warm Footies", "Feet", "Cold_aa"),
        ["Head"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Fire"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateFormGearItem("Fire", "Normal", "Sheer Top", "Chest", "Fire_aa"),
        ["Legs"] = CreateFormGearItem("Fire", "Normal", "Skimmies", "Legs", "Fire_aa"),
        ["Head"] = null, ["Feet"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Dark"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateFormGearItem("Dark", "Normal", "Void Shirt", "Chest", "Dark_aa"),
        ["Legs"] = CreateFormGearItem("Dark", "Normal", "Void Pants", "Legs", "Dark_aa"),
        ["Feet"] = CreateFormGearItem("Dark", "Normal", "Void Footies", "Feet", "Dark_aa"),
        ["Arms"] = CreateFormGearItem("Dark", "Normal", "Void Gloves", "Arms", "Dark_aa"),
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

  static GearItem CreateFormGearItem(string formName, string type, string name, string slot, string gearId) {
    ShaderColors.TryGetFormColor(
      formName,
      ShaderColors.PrimaryGroup,
      out _,
      out var colorName
    );
    return CreateGearItem(type, name, slot, gearId, colorName ?? "");
  }
}
