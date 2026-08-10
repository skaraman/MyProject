using System;
using System.Collections.Generic;
using UnityEngine;

public static class Inventory {
  public const string ChangedMessage = "inventory.changed";
  const int CurrentSaveVersion = 1;

  static SaveData loadedSaveData = new();
  static bool savePending;
  static int pendingSaveSlot = -1;
  static int loadedSlot = -1;
  static bool newerSaveWriteProtected;
  static bool newerSaveMutationWarningLogged;

  public static List<GearItem> Gear { set; get; } = new();
  public static List<ConsumableItem> Consumables { set; get; } = new();
  public static List<QuestItem> Quest { set; get; } = new();
  public static List<GemItem> Gems { set; get; } = new();
  public static int Gold { get; set; }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetRuntimeState() {
    ResetCollections();
    loadedSaveData = new SaveData();
    savePending = false;
    pendingSaveSlot = -1;
    loadedSlot = -1;
    newerSaveWriteProtected = false;
    newerSaveMutationWarningLogged = false;
  }

  public static bool LoadCurrentSlot() {
    ResetCollections();
    loadedSlot = SaveSlotManager.slot;
    loadedSaveData = new SaveData();
    savePending = false;
    pendingSaveSlot = -1;

    try {
      loadedSaveData = SaveSlotManager.Load(SaveKeys.Inventory) ?? new SaveData();
      newerSaveWriteProtected = ReadSaveVersion(loadedSaveData) > CurrentSaveVersion;
      newerSaveMutationWarningLogged = false;
      if (loadedSaveData.HasPrefix(SaveKeys.InventoryGear)) {
        Gear = loadedSaveData.GetComplex<List<GearItem>>(SaveKeys.InventoryGear);
      }
      if (loadedSaveData.HasPrefix(SaveKeys.InventoryGems)) {
        Gems = loadedSaveData.GetComplex<List<GemItem>>(SaveKeys.InventoryGems);
      }
      if (loadedSaveData.HasPrefix(SaveKeys.InventoryConsumables)) {
        Consumables = loadedSaveData.GetComplex<List<ConsumableItem>>(SaveKeys.InventoryConsumables);
      }
      if (loadedSaveData.HasPrefix(SaveKeys.InventoryQuest)) {
        Quest = loadedSaveData.GetComplex<List<QuestItem>>(SaveKeys.InventoryQuest);
      }
      if (loadedSaveData.TryGetValue(SaveKeys.InventoryGold, out var savedGold)) {
        Gold = Mathf.Max(0, Convert.ToInt32(savedGold));
      }

      NormalizeCollections();
      WarnIfSaveIsNewer();
      NotifyChanged();
      return true;
    }
    catch (Exception exception) {
      ResetCollections();
      loadedSaveData = new SaveData();
      newerSaveWriteProtected = false;
      newerSaveMutationWarningLogged = false;
      Debug.LogWarning(
        "[Inventory] Failed to load slot=" + loadedSlot + ": " + exception.Message
      );
      NotifyChanged();
      return false;
    }
  }

  public static bool InitializeCurrentSlotForNewGame() {
    ResetCollections();
    loadedSlot = SaveSlotManager.slot;
    loadedSaveData = new SaveData();
    savePending = false;
    pendingSaveSlot = -1;
    newerSaveWriteProtected = false;
    newerSaveMutationWarningLogged = false;
    QueueSave();
    NotifyChanged();
    return TryFlushPendingSave();
  }

  public static bool AddGear(GearItem gearItem) {
    if (gearItem == null) {
      return false;
    }

    PrepareMutation();
    Gear.Add(EquippedItems.CloneGearItem(gearItem));
    MarkChanged();
    return true;
  }

  public static bool TryGetGear(int index, out GearItem gearItem) {
    PrepareMutation();
    gearItem = null;
    if (index < 0 || index >= Gear.Count || Gear[index] == null) {
      return false;
    }

    gearItem = EquippedItems.CloneGearItem(Gear[index]);
    return true;
  }

  public static List<GearItem> CreateGearSnapshot() {
    PrepareMutation();
    var snapshot = new List<GearItem>(Gear.Count);
    for (var i = 0; i < Gear.Count; i++) {
      snapshot.Add(EquippedItems.CloneGearItem(Gear[i]));
    }
    return snapshot;
  }

  public static bool TryExchangeEquippedGear(
    int inventoryIndex,
    GearItem previouslyEquipped,
    out GearItem selectedGear
  ) {
    selectedGear = null;
    if (!TryPrepareWritableMutation() ||
        inventoryIndex < 0 || inventoryIndex >= Gear.Count ||
        Gear[inventoryIndex] == null) {
      return false;
    }

    selectedGear = EquippedItems.CloneGearItem(Gear[inventoryIndex]);
    if (previouslyEquipped == null) {
      Gear.RemoveAt(inventoryIndex);
    }
    else {
      Gear[inventoryIndex] = EquippedItems.CloneGearItem(previouslyEquipped);
    }
    MarkChanged();
    return true;
  }

  public static bool TryStoreUnequippedGear(GearItem gearItem) {
    if (gearItem == null || !TryPrepareWritableMutation()) {
      return false;
    }

    Gear.Add(EquippedItems.CloneGearItem(gearItem));
    MarkChanged();
    return true;
  }

  public static void RestoreGearSnapshot(List<GearItem> snapshot) {
    PrepareMutation();
    Gear = new List<GearItem>(snapshot != null ? snapshot.Count : 0);
    if (snapshot != null) {
      for (var i = 0; i < snapshot.Count; i++) {
        var item = EquippedItems.CloneGearItem(snapshot[i]);
        if (item != null) {
          Gear.Add(item);
        }
      }
    }
    MarkChanged();
  }

  public static bool AddGem(string type, int amount) {
    if (string.IsNullOrWhiteSpace(type) || amount <= 0) {
      return false;
    }

    PrepareMutation();
    var normalizedType = type.Trim();
    var existing = FindGem(normalizedType);
    if (existing == null) {
      Gems.Add(new GemItem { Type = normalizedType, Amount = amount });
    }
    else {
      existing.Amount = AddAmounts(existing.Amount, amount);
    }
    MarkChanged();
    return true;
  }

  public static bool AddConsumable(ConsumableItem item) {
    if (item == null || string.IsNullOrWhiteSpace(item.Type) || item.Amount <= 0) {
      return false;
    }

    PrepareMutation();
    var existing = FindConsumable(item.Type);
    if (existing == null) {
      Consumables.Add(CloneConsumable(item));
    }
    else {
      existing.Amount = AddAmounts(existing.Amount, item.Amount);
      FillMissingDisplayData(existing, item);
    }
    MarkChanged();
    return true;
  }

  public static bool AddQuestItem(QuestItem item) {
    if (item == null || string.IsNullOrWhiteSpace(item.Type) || item.Amount <= 0) {
      return false;
    }

    PrepareMutation();
    var existing = FindQuestItem(item.Type);
    if (existing == null) {
      Quest.Add(CloneQuestItem(item));
    }
    else {
      existing.Amount = AddAmounts(existing.Amount, item.Amount);
      FillMissingDisplayData(existing, item);
    }
    MarkChanged();
    return true;
  }

  public static bool AddGold(int amount) {
    if (amount <= 0) {
      return false;
    }

    PrepareMutation();
    Gold = AddAmounts(Gold, amount);
    MarkChanged();
    return true;
  }

  public static bool TryFlushPendingSave() {
    if (newerSaveWriteProtected) {
      savePending = false;
      pendingSaveSlot = -1;
      return true;
    }
    if (!savePending) {
      return true;
    }
    if (pendingSaveSlot != SaveSlotManager.slot) {
      Debug.LogWarning(
        "[Inventory] Refused cross-slot save pending_slot=" + pendingSaveSlot +
        " current_slot=" + SaveSlotManager.slot
      );
      return false;
    }

    try {
      NormalizeCollections();
      var saveData = loadedSaveData ?? new SaveData();
      var savedVersion = ReadSaveVersion(saveData);
      saveData[SaveKeys.InventoryVersion] = Mathf.Max(CurrentSaveVersion, savedVersion);
      saveData.SetComplex(SaveKeys.InventoryGear, Gear);
      saveData.SetComplex(SaveKeys.InventoryGems, Gems);
      saveData.SetComplex(SaveKeys.InventoryConsumables, Consumables);
      saveData.SetComplex(SaveKeys.InventoryQuest, Quest);
      saveData[SaveKeys.InventoryGold] = Mathf.Max(0, Gold);
      SaveSlotManager.Save(SaveKeys.Inventory, saveData);
      loadedSaveData = saveData;
      savePending = false;
      pendingSaveSlot = -1;
      return true;
    }
    catch (Exception exception) {
      Debug.LogWarning(
        "[Inventory] Failed to save slot=" + pendingSaveSlot + ": " + exception.Message
      );
      return false;
    }
  }

  static void PrepareMutation() {
    if (loadedSlot == SaveSlotManager.slot) {
      EnsureCollections();
      return;
    }

    ResetCollections();
    loadedSaveData = new SaveData();
    loadedSlot = SaveSlotManager.slot;
    savePending = false;
    pendingSaveSlot = -1;
    newerSaveWriteProtected = false;
    newerSaveMutationWarningLogged = false;
  }

  static bool TryPrepareWritableMutation() {
    PrepareMutation();
    if (!newerSaveWriteProtected) {
      return true;
    }

    if (!newerSaveMutationWarningLogged) {
      newerSaveMutationWarningLogged = true;
      Debug.LogWarning(
        "[Inventory] Inventory mutation was refused because this slot uses a newer " +
        "inventory save version."
      );
    }
    return false;
  }

  static void MarkChanged() {
    QueueSave();
    NotifyChanged();
  }

  static void QueueSave() {
    if (newerSaveWriteProtected) {
      if (!newerSaveMutationWarningLogged) {
        newerSaveMutationWarningLogged = true;
        Debug.LogWarning(
          "[Inventory] Runtime inventory changed, but this slot uses a newer inventory save " +
          "version. The existing file is write-protected and will not be overwritten."
        );
      }
      return;
    }
    if (!savePending) {
      pendingSaveSlot = SaveSlotManager.slot;
    }
    savePending = true;
  }

  static void NotifyChanged() {
    MessageBus.Send(ChangedMessage, null);
  }

  static void ResetCollections() {
    Gear = new List<GearItem>();
    Gems = new List<GemItem>();
    Consumables = new List<ConsumableItem>();
    Quest = new List<QuestItem>();
    Gold = 0;
  }

  static void EnsureCollections() {
    Gear ??= new List<GearItem>();
    Gems ??= new List<GemItem>();
    Consumables ??= new List<ConsumableItem>();
    Quest ??= new List<QuestItem>();
  }

  static void NormalizeCollections() {
    EnsureCollections();

    var normalizedGear = new List<GearItem>(Gear.Count);
    for (var i = 0; i < Gear.Count; i++) {
      if (Gear[i] != null) {
        normalizedGear.Add(Gear[i]);
      }
    }
    Gear = normalizedGear;

    var loadedGems = Gems;
    Gems = new List<GemItem>();
    for (var i = 0; i < loadedGems.Count; i++) {
      var gem = loadedGems[i];
      if (gem == null || string.IsNullOrWhiteSpace(gem.Type) || gem.Amount <= 0) continue;
      var existing = FindGem(gem.Type);
      if (existing == null) {
        Gems.Add(new GemItem { Type = gem.Type.Trim(), Amount = gem.Amount });
      }
      else {
        existing.Amount = AddAmounts(existing.Amount, gem.Amount);
      }
    }

    var loadedConsumables = Consumables;
    Consumables = new List<ConsumableItem>();
    for (var i = 0; i < loadedConsumables.Count; i++) {
      var item = loadedConsumables[i];
      if (item == null || string.IsNullOrWhiteSpace(item.Type) || item.Amount <= 0) continue;
      var existing = FindConsumable(item.Type);
      if (existing == null) {
        Consumables.Add(CloneConsumable(item));
      }
      else {
        existing.Amount = AddAmounts(existing.Amount, item.Amount);
        FillMissingDisplayData(existing, item);
      }
    }

    var loadedQuestItems = Quest;
    Quest = new List<QuestItem>();
    for (var i = 0; i < loadedQuestItems.Count; i++) {
      var item = loadedQuestItems[i];
      if (item == null || string.IsNullOrWhiteSpace(item.Type) || item.Amount <= 0) continue;
      var existing = FindQuestItem(item.Type);
      if (existing == null) {
        Quest.Add(CloneQuestItem(item));
      }
      else {
        existing.Amount = AddAmounts(existing.Amount, item.Amount);
        FillMissingDisplayData(existing, item);
      }
    }

    Gold = Mathf.Max(0, Gold);
  }

  static GemItem FindGem(string type) {
    for (var i = 0; i < Gems.Count; i++) {
      var item = Gems[i];
      if (item != null && string.Equals(item.Type, type, StringComparison.OrdinalIgnoreCase)) {
        return item;
      }
    }
    return null;
  }

  static ConsumableItem FindConsumable(string type) {
    for (var i = 0; i < Consumables.Count; i++) {
      var item = Consumables[i];
      if (item != null && string.Equals(item.Type, type, StringComparison.OrdinalIgnoreCase)) {
        return item;
      }
    }
    return null;
  }

  static QuestItem FindQuestItem(string type) {
    for (var i = 0; i < Quest.Count; i++) {
      var item = Quest[i];
      if (item != null && string.Equals(item.Type, type, StringComparison.OrdinalIgnoreCase)) {
        return item;
      }
    }
    return null;
  }

  static ConsumableItem CloneConsumable(ConsumableItem item) {
    return new ConsumableItem {
      Type = item.Type.Trim(),
      Name = item.Name,
      IconLibrary = item.IconLibrary,
      IconCategory = item.IconCategory,
      IconId = item.IconId,
      Amount = item.Amount
    };
  }

  static QuestItem CloneQuestItem(QuestItem item) {
    return new QuestItem {
      Type = item.Type.Trim(),
      Name = item.Name,
      IconLibrary = item.IconLibrary,
      IconCategory = item.IconCategory,
      IconId = item.IconId,
      Amount = item.Amount
    };
  }

  static void FillMissingDisplayData(ConsumableItem target, ConsumableItem source) {
    target.Name = FirstValue(target.Name, source.Name);
    target.IconLibrary = FirstValue(target.IconLibrary, source.IconLibrary);
    target.IconCategory = FirstValue(target.IconCategory, source.IconCategory);
    target.IconId = FirstValue(target.IconId, source.IconId);
  }

  static void FillMissingDisplayData(QuestItem target, QuestItem source) {
    target.Name = FirstValue(target.Name, source.Name);
    target.IconLibrary = FirstValue(target.IconLibrary, source.IconLibrary);
    target.IconCategory = FirstValue(target.IconCategory, source.IconCategory);
    target.IconId = FirstValue(target.IconId, source.IconId);
  }

  static string FirstValue(string current, string fallback) {
    return string.IsNullOrWhiteSpace(current) ? fallback : current;
  }

  static int AddAmounts(int current, int amount) {
    return (int)Math.Min(int.MaxValue, (long)Mathf.Max(0, current) + Mathf.Max(0, amount));
  }

  static int ReadSaveVersion(SaveData saveData) {
    if (saveData == null ||
        !saveData.TryGetValue(SaveKeys.InventoryVersion, out var versionValue)) {
      return 0;
    }

    try {
      return Mathf.Max(0, Convert.ToInt32(versionValue));
    }
    catch {
      return 0;
    }
  }

  static void WarnIfSaveIsNewer() {
    var savedVersion = ReadSaveVersion(loadedSaveData);
    if (savedVersion <= CurrentSaveVersion) {
      return;
    }

    Debug.LogWarning(
      "[Inventory] Save version=" + savedVersion +
      " is newer than supported version=" + CurrentSaveVersion +
      "; known fields were loaded read-only and the file will not be overwritten."
    );
  }
}
