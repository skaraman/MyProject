using System;
using System.Collections.Generic;
using CustomInspector;

[Serializable]
public class GearSlotItemMap : SerializableSortedDictionary<string, GearItem> {
  public GearSlotItemMap() {
  }

  public GearSlotItemMap(IEnumerable<KeyValuePair<string, GearItem>> slots) {
    SetFrom(slots);
  }

  public void SetFrom(IEnumerable<KeyValuePair<string, GearItem>> slots) {
    Clear();

    if (slots == null) {
      return;
    }

    foreach (var slot in slots) {
      this[slot.Key] = slot.Value;
    }
  }

  public Dictionary<string, GearItem> ToDictionary() {
    var slots = new Dictionary<string, GearItem>(StringComparer.Ordinal);

    foreach (var slot in this) {
      slots[slot.key] = slot.value;
    }

    return slots;
  }
}

[Serializable]
public class GearFormItemMap : SerializableSortedDictionary<string, GearSlotItemMap> {
  public GearFormItemMap() {
  }

  public GearFormItemMap(Dictionary<string, Dictionary<string, GearItem>> forms) {
    SetFrom(forms);
  }

  public void SetFrom(Dictionary<string, Dictionary<string, GearItem>> forms) {
    Clear();

    if (forms == null) {
      return;
    }

    foreach (var form in forms) {
      this[form.Key] = new GearSlotItemMap(form.Value);
    }
  }

  public Dictionary<string, Dictionary<string, GearItem>> ToDictionary() {
    var forms = new Dictionary<string, Dictionary<string, GearItem>>(StringComparer.Ordinal);

    foreach (var form in this) {
      if (form.value == null) {
        forms[form.key] = new Dictionary<string, GearItem>(StringComparer.Ordinal);
        continue;
      }

      forms[form.key] = form.value.ToDictionary();
    }

    return forms;
  }
}
