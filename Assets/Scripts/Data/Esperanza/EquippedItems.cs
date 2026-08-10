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

  public static bool TryResolveSlot(string formName, string slotName, out string resolvedSlot) {
    resolvedSlot = null;
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm) || string.IsNullOrWhiteSpace(slotName)) {
      return false;
    }

    EnsureForm(resolvedForm);
    if (!AllGearForms.TryGetValue(resolvedForm, out var slots) || slots == null) {
      return false;
    }

    resolvedSlot = ResolveSlotKey(slots, slotName);
    return !string.IsNullOrWhiteSpace(resolvedSlot);
  }

  public static bool IsDefaultGearSlot(string formName, string slotName) {
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm) ||
        !DefaultGearForms.TryGetValue(resolvedForm, out var defaultSlots) ||
        defaultSlots == null) {
      return false;
    }

    var resolvedSlot = ResolveSlotKey(defaultSlots, slotName);
    return !string.IsNullOrWhiteSpace(resolvedSlot) && defaultSlots[resolvedSlot] != null;
  }

  public static bool AreSlotsCompatible(string itemSlot, string targetSlot) {
    var normalizedItemSlot = NormalizeComparableSlot(itemSlot);
    var normalizedTargetSlot = NormalizeComparableSlot(targetSlot);
    return !string.IsNullOrWhiteSpace(normalizedItemSlot) &&
      string.Equals(normalizedItemSlot, normalizedTargetSlot, StringComparison.OrdinalIgnoreCase);
  }

  public static bool TrySetGear(
    string formName,
    string slotName,
    GearItem gearItem,
    out GearItem previousGear
  ) {
    previousGear = null;
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm) ||
        !TryResolveSlot(resolvedForm, slotName, out var resolvedSlot)) {
      return false;
    }

    if (gearItem == null && IsDefaultGearSlot(resolvedForm, resolvedSlot)) {
      return false;
    }
    if (gearItem != null && !AreSlotsCompatible(gearItem.slot, resolvedSlot)) {
      return false;
    }

    var slots = AllGearForms[resolvedForm];
    previousGear = CloneGearItem(slots[resolvedSlot]);
    var nextGear = CloneGearItem(gearItem);
    if (nextGear != null) {
      nextGear.slot = resolvedSlot;
    }
    slots[resolvedSlot] = nextGear;
    return true;
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
        if (gearItem == null && IsDefaultGearSlot(form, slot)) {
          continue;
        }
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

  public static void RandomizeDefaultBoostsForNewGame() {
    EnsureKnownForms();

    foreach (var formEntry in AllGearForms) {
      if (formEntry.Value == null ||
          !Abbreviations.FormMajorMinor.TryGetValue(formEntry.Key, out var majorMinor) ||
          majorMinor == null ||
          majorMinor.Count == 0) {
        continue;
      }

      var availableMajorStats = new List<string>(majorMinor.Keys);
      Shuffle(availableMajorStats);
      var nextStatIndex = 0;

      foreach (var slotEntry in formEntry.Value) {
        var item = slotEntry.Value;
        if (item == null) {
          continue;
        }

        var statName = availableMajorStats[nextStatIndex % availableMajorStats.Count];
        nextStatIndex++;
        item.boosts = new List<BoostEntry> {
          new BoostEntry {
            statName = statName,
            value = 1f
          }
        };
        item.name = GearNames.Generate(item.gearId, item.slot, item.boosts);
      }
    }
  }

  static void Shuffle(List<string> values) {
    for (var i = values.Count - 1; i > 0; i--) {
      var swapIndex = UnityEngine.Random.Range(0, i + 1);
      (values[i], values[swapIndex]) = (values[swapIndex], values[i]);
    }
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

  static string NormalizeComparableSlot(string value) {
    var normalized = NormalizeToken(value);
    if (normalized.StartsWith("Ring", StringComparison.OrdinalIgnoreCase)) {
      var suffix = normalized.Substring("Ring".Length);
      if (suffix.Length == 0 || int.TryParse(suffix, out _)) {
        return "Ring";
      }
    }
    return normalized;
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
        ["Chest"] = CreateFormGearItem("Base", "Basic", "Chest", "Base_aa", "STR"),
        ["Legs"] = CreateFormGearItem("Base", "Basic", "Legs", "Base_aa", "DEX"),
        ["Feet"] = CreateFormGearItem("Base", "Basic", "Feet", "Base_aa", "END"),
        ["Head"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Aqua"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateFormGearItem("Aqua", "Basic", "Chest", "Aqua_aa", "INT"),
        ["Legs"] = CreateFormGearItem("Aqua", "Basic", "Legs", "Aqua_aa", "DEX"),
        ["Head"] = null, ["Feet"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Bolt"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateFormGearItem("Bolt", "Basic", "Chest", "Bolt_aa", "DEX"),
        ["Legs"] = CreateFormGearItem("Bolt", "Basic", "Legs", "Bolt_aa", "END"),
        ["Feet"] = CreateFormGearItem("Bolt", "Basic", "Feet", "Bolt_aa", "AMP"),
        ["Head"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Cold"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateFormGearItem("Cold", "Basic", "Chest", "Cold_aa", "END"),
        ["Legs"] = CreateFormGearItem("Cold", "Basic", "Legs", "Cold_aa", "INT"),
        ["Feet"] = CreateFormGearItem("Cold", "Basic", "Feet", "Cold_aa", "CHL"),
        ["Head"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Fire"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateFormGearItem("Fire", "Basic", "Chest", "Fire_aa", "STR"),
        ["Legs"] = CreateFormGearItem("Fire", "Basic", "Legs", "Fire_aa", "END"),
        ["Head"] = null, ["Feet"] = null, ["Shoulders"] = null, ["Arms"] = null, ["Belt"] = null, ["Zemi"] = null,
        ["Ring1"] = null, ["Ring2"] = null, ["Ring3"] = null, ["Ring4"] = null, ["Ring5"] = null,
        ["Ring6"] = null, ["Ring7"] = null, ["Ring8"] = null, ["Ring9"] = null,
        ["Ring10"] = null, ["Ring11"] = null, ["Ring12"] = null
      },
      ["Dark"] = new Dictionary<string, GearItem>(StringComparer.Ordinal) {
        ["Chest"] = CreateFormGearItem("Dark", "Basic", "Chest", "Dark_aa", "UMB"),
        ["Legs"] = CreateFormGearItem("Dark", "Basic", "Legs", "Dark_aa", "VOI"),
        ["Feet"] = CreateFormGearItem("Dark", "Basic", "Feet", "Dark_aa", "ABY"),
        ["Arms"] = CreateFormGearItem("Dark", "Basic", "Arms", "Dark_aa", "ECL"),
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

  static GearItem CreateGearItem(
    string type,
    string slot,
    string gearId,
    string gearColor,
    string boostStat
  ) {
    var boosts = new List<BoostEntry> {
      new BoostEntry {
        statName = boostStat,
        value = 1f
      }
    };
    return new GearItem {
      type = type,
      name = GearNames.Generate(gearId, slot, boosts),
      slot = slot,
      gearId = gearId,
      gearColor = gearColor,
      boosts = boosts
    };
  }

  static GearItem CreateFormGearItem(
    string formName,
    string type,
    string slot,
    string gearId,
    string boostStat
  ) {
    ShaderColors.TryGetFormColor(
      formName,
      ShaderColors.PrimaryGroup,
      out _,
      out var colorName
    );
    return CreateGearItem(type, slot, gearId, colorName ?? "", boostStat);
  }
}
