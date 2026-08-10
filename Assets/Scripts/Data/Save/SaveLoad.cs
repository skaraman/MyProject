using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;


public class SaveData : Dictionary<string, object> {

  #region Core Save/Load Methods
  public void Save(string path) {
    var dir = Path.GetDirectoryName(path);
    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    var file = File.Open(path, FileMode.Create);
    using var writer = new BinaryWriter(file);
    writer.Write(Count);
    foreach (var pair in this) {
      writer.Write(pair.Key);
      var value = pair.Value;
      if (value is int i) {
        writer.Write((byte)1);
        writer.Write(i);
      }
      else if (value is float f) {
        writer.Write((byte)2);
        writer.Write(f);
      }
      else if (value is string s) {
        writer.Write((byte)3);
        writer.Write(s);
      }
      else if (value is double d) {
        writer.Write((byte)4);
        writer.Write(d);
      }
      else if (value is bool b) {
        writer.Write((byte)5);
        writer.Write(b);
      }
      else {
        writer.Write((byte)0);
      }
    }
  }

  public static SaveData Load(string path) {
    var table = new SaveData();
    if (!File.Exists(path)) return table;
    using var reader = new BinaryReader(File.Open(path, FileMode.Open));
    var count = reader.ReadInt32();
    for (int i = 0; i < count; i++) {
      var key = reader.ReadString();
      var type = reader.ReadByte();
      object value = null;
      switch (type) {
        case 1: value = reader.ReadInt32(); break;
        case 2: value = reader.ReadSingle(); break;
        case 3: value = reader.ReadString(); break;
        case 4: value = reader.ReadDouble(); break;
        case 5: value = reader.ReadBoolean(); break;
        default: Debug.LogWarning("Unsupported type byte: " + type); break;
      }
      if (value != null) table[key] = value;
    }
    return table;
  }
  #endregion

  #region Flattening Helper Methods

  /// <summary>
  /// Flattens any complex object into primitive key-value pairs
  /// </summary>
  public void SetComplex<T>(string prefix, T obj) {
    ClearPrefix(prefix);
    var flattened = FlattenObject(prefix, obj);
    foreach (var kvp in flattened) {
      this[kvp.Key] = kvp.Value;
    }
  }

  /// <summary>
  /// Reconstructs a complex object from flattened data
  /// </summary>
  public T GetComplex<T>(string prefix) where T : new() {
    return UnflattenObject<T>(prefix);
  }

  /// <summary>
  /// Reconstructs a complex object from flattened data with a custom constructor
  /// </summary>
  public T GetComplex<T>(string prefix, Func<T> constructor) {
    return UnflattenObject(prefix, constructor);
  }

  private Dictionary<string, object> FlattenObject(string prefix, object obj, int depth = 0) {
    var result = new Dictionary<string, object>();

    if (obj == null) {
      result[$"{prefix}_null"] = true;
      return result;
    }

    // Prevent infinite recursion
    if (depth > 10) {
      Debug.LogWarning($"Maximum flattening depth reached for {prefix}");
      return result;
    }

    var type = obj.GetType();

    // Handle primitives and strings
    if (IsPrimitive(type)) {
      result[prefix] = obj;
      return result;
    }

    // Handle Dictionary<string, T>
    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
        type.GetGenericArguments()[0] == typeof(string)) {
      var dict = obj as System.Collections.IDictionary;
      result[$"{prefix}_count"] = dict.Count;
      int index = 0;
      foreach (System.Collections.DictionaryEntry kvp in dict) {
        string key = kvp.Key.ToString();
        var subFlattened = FlattenObject($"{prefix}_{key}", kvp.Value, depth + 1);
        foreach (var subKvp in subFlattened) {
          result[subKvp.Key] = subKvp.Value;
        }
        index++;
      }
      return result;
    }

    // Handle Lists and Arrays
    if (typeof(System.Collections.IList).IsAssignableFrom(type)) {
      var list = obj as System.Collections.IList;
      result[$"{prefix}_count"] = list.Count;
      for (int i = 0; i < list.Count; i++) {
        var subFlattened = FlattenObject($"{prefix}_{i}", list[i], depth + 1);
        foreach (var subKvp in subFlattened) {
          result[subKvp.Key] = subKvp.Value;
        }
      }
      return result;
    }

    // Handle custom objects via reflection
    var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanRead && p.CanWrite);
    var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

    foreach (var prop in properties) {
      try {
        var value = prop.GetValue(obj);
        var subFlattened = FlattenObject($"{prefix}_{prop.Name}", value, depth + 1);
        foreach (var subKvp in subFlattened) {
          result[subKvp.Key] = subKvp.Value;
        }
      }
      catch (Exception e) {
        Debug.LogWarning($"Failed to flatten property {prop.Name}: {e.Message}");
      }
    }

    foreach (var field in fields) {
      try {
        var value = field.GetValue(obj);
        var subFlattened = FlattenObject($"{prefix}_{field.Name}", value, depth + 1);
        foreach (var subKvp in subFlattened) {
          result[subKvp.Key] = subKvp.Value;
        }
      }
      catch (Exception e) {
        Debug.LogWarning($"Failed to flatten field {field.Name}: {e.Message}");
      }
    }

    return result;
  }

  private T UnflattenObject<T>(string prefix) where T : new() {
    return UnflattenObject(prefix, () => new T());
  }

  private T UnflattenObject<T>(string prefix, Func<T> constructor) {
    var resolved = UnflattenValue(prefix, typeof(T), () => constructor());
    if (resolved == null) {
      return default;
    }

    if (resolved is T typedValue) {
      return typedValue;
    }

    return (T)Convert.ChangeType(resolved, typeof(T));
  }

  private object UnflattenValue(string prefix, Type targetType, Func<object> constructor = null) {
    if (ContainsKey($"{prefix}_null") && Convert.ToBoolean(this[$"{prefix}_null"])) {
      return null;
    }

    var nullableUnderlyingType = Nullable.GetUnderlyingType(targetType);
    if (nullableUnderlyingType != null) {
      return UnflattenValue(prefix, nullableUnderlyingType, constructor);
    }

    if (IsPrimitive(targetType)) {
      if (!ContainsKey(prefix)) {
        return GetDefaultValue(targetType);
      }

      return Convert.ChangeType(this[prefix], targetType);
    }

    if (IsStringKeyDictionary(targetType)) {
      return UnflattenDictionary(prefix, targetType, constructor);
    }

    if (IsListType(targetType)) {
      return UnflattenList(prefix, targetType, constructor);
    }

    if (!HasPrefix(prefix)) {
      return constructor != null ? constructor() : GetDefaultValue(targetType);
    }

    var instance = constructor != null ? constructor() : CreateInstance(targetType);
    if (instance == null) {
      return GetDefaultValue(targetType);
    }

    PopulateObject(instance, prefix, targetType);
    return instance;
  }

  object UnflattenDictionary(string prefix, Type targetType, Func<object> constructor = null) {
    var valueType = targetType.GetGenericArguments()[1];
    var dictionary = (System.Collections.IDictionary)(constructor != null ? constructor() : CreateInstance(targetType));
    if (dictionary == null) {
      return GetDefaultValue(targetType);
    }

    foreach (var childKey in GetDirectChildKeys(prefix)) {
      var valuePrefix = $"{prefix}_{childKey}";
      var value = UnflattenValue(valuePrefix, valueType);
      dictionary[childKey] = value;
    }

    return dictionary;
  }

  object UnflattenList(string prefix, Type targetType, Func<object> constructor = null) {
    var list = (System.Collections.IList)(constructor != null ? constructor() : CreateInstance(targetType));
    if (list == null) {
      return GetDefaultValue(targetType);
    }

    var elementType = targetType.IsArray
      ? targetType.GetElementType()
      : targetType.GetGenericArguments()[0];
    var indexedPrefixes = GetDirectChildKeys(prefix)
      .Select(key => {
        var parsed = int.TryParse(key, out var index);
        return new { key, parsed, index };
      })
      .Where(entry => entry.parsed)
      .OrderBy(entry => entry.index);

    if (targetType.IsArray) {
      var values = new List<object>();
      foreach (var entry in indexedPrefixes) {
        values.Add(UnflattenValue($"{prefix}_{entry.key}", elementType));
      }

      var array = Array.CreateInstance(elementType, values.Count);
      for (var i = 0; i < values.Count; i++) {
        array.SetValue(values[i], i);
      }
      return array;
    }

    foreach (var entry in indexedPrefixes) {
      list.Add(UnflattenValue($"{prefix}_{entry.key}", elementType));
    }

    return list;
  }

  void PopulateObject(object instance, string prefix, Type targetType) {
    var properties = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => p.CanRead && p.CanWrite);
    var fields = targetType.GetFields(BindingFlags.Public | BindingFlags.Instance);

    foreach (var property in properties) {
      TryAssignMember(
        instance,
        prefix,
        property.Name,
        property.PropertyType,
        value => property.SetValue(instance, value),
        $"property {property.Name}"
      );
    }

    foreach (var field in fields) {
      TryAssignMember(
        instance,
        prefix,
        field.Name,
        field.FieldType,
        value => field.SetValue(instance, value),
        $"field {field.Name}"
      );
    }
  }

  void TryAssignMember(
    object instance,
    string prefix,
    string memberName,
    Type memberType,
    Action<object> assign,
    string description
  ) {
    var memberPrefix = $"{prefix}_{memberName}";
    if (!HasPrefix(memberPrefix)) {
      return;
    }

    try {
      var value = UnflattenValue(memberPrefix, memberType);
      if (value == null && memberType.IsValueType && Nullable.GetUnderlyingType(memberType) == null) {
        return;
      }
      assign(value);
    }
    catch (Exception e) {
      Debug.LogWarning($"Failed to unflatten {description}: {e.Message}");
    }
  }

  IEnumerable<string> GetDirectChildKeys(string prefix) {
    var childPrefix = $"{prefix}_";
    var directChildren = new HashSet<string>(StringComparer.Ordinal);

    foreach (var key in Keys) {
      if (!key.StartsWith(childPrefix, StringComparison.Ordinal)) {
        continue;
      }

      if (string.Equals(key, $"{prefix}_count", StringComparison.Ordinal) ||
          string.Equals(key, $"{prefix}_null", StringComparison.Ordinal)) {
        continue;
      }

      var remainder = key.Substring(childPrefix.Length);
      if (string.IsNullOrWhiteSpace(remainder)) {
        continue;
      }

      var separatorIndex = remainder.IndexOf('_');
      var childKey = separatorIndex >= 0 ? remainder.Substring(0, separatorIndex) : remainder;
      if (!string.IsNullOrWhiteSpace(childKey)) {
        directChildren.Add(childKey);
      }
    }

    return directChildren;
  }

  bool IsStringKeyDictionary(Type type) {
    return type.IsGenericType &&
           type.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
           type.GetGenericArguments()[0] == typeof(string);
  }

  bool IsListType(Type type) {
    if (type.IsArray) {
      return true;
    }

    return typeof(System.Collections.IList).IsAssignableFrom(type) && type.IsGenericType;
  }

  object CreateInstance(Type type) {
    try {
      return Activator.CreateInstance(type);
    }
    catch {
      return null;
    }
  }

  private bool IsPrimitive(Type type) {
    return type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
           type == typeof(DateTime) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
           IsPrimitive(type.GetGenericArguments()[0]));
  }

  private object GetDefaultValue(Type type) {
    return type.IsValueType ? Activator.CreateInstance(type) : null;
  }
  #endregion

  #region Convenience Methods

  /// <summary>
  /// Remove all keys with the specified prefix
  /// </summary>
  public void ClearPrefix(string prefix) {
    var keysToRemove = Keys
      .Where(k => string.Equals(k, prefix, StringComparison.Ordinal) ||
                  k.StartsWith($"{prefix}_", StringComparison.Ordinal))
      .ToList();
    foreach (var key in keysToRemove) {
      Remove(key);
    }
  }

  /// <summary>
  /// Check if any keys exist with the specified prefix
  /// </summary>
  public bool HasPrefix(string prefix) {
    return Keys.Any(k => string.Equals(k, prefix, StringComparison.Ordinal) ||
                         k.StartsWith($"{prefix}_", StringComparison.Ordinal));
  }

  /// <summary>
  /// Get all keys with the specified prefix
  /// </summary>
  public IEnumerable<string> GetKeysWithPrefix(string prefix) {
    return Keys.Where(k => string.Equals(k, prefix, StringComparison.Ordinal) ||
                           k.StartsWith($"{prefix}_", StringComparison.Ordinal));
  }
  #endregion
}

// Storing a mix of simple and complex data
//saveData["score"] = 500;  // Simple int
//saveData.SetComplex("player", new Player { Name = "Bob", Health = 80 });  // Complex

// Retrieving
//int score = saveData.GetComplex<int>("score");  // Simple (500)
//Player player = saveData.GetComplex<Player>("player");  // Complex (Name="Bob", Health=80)


public static class SaveKeys {
  public const string Forms = "forms";
  public const string Stats = "stats";
  public const string ActiveForm = "activeForm";
  public const string UnlockedForms = "unlockedForms";
  public const string FormProgress = "formProgress";
  public const string AbilityProgress = "abilityProgress";
  public const string AbilityLoadouts = "abilityLoadouts";
  public const string ComboLoadouts = "comboLoadouts";
  public const string AvailableStatPoints = "availableStatPoints";
  public const string FormStats = "formStats";
  public const string EquippedGear = "equippedGear";
  public const string AllGear = "allGear";
  public const string Inventory = "inventory";
  public const string InventoryVersion = "inventoryVersion";
  public const string InventoryGear = "gear";
  public const string InventoryGems = "gems";
  public const string InventoryConsumables = "consumables";
  public const string InventoryQuest = "quest";
  public const string InventoryGold = "gold";
}

public static class SaveSlotManager {
  public const string SlotEpisodeIdKey = "episodeId";
  public const string SlotSaveDateKey = "saveDate";
  public const string DefaultEpisodeId = "Episode1.1";
  const string LegacySlotEpisodeKey = "episode";
  const string LegacySlotSaveDateKey = "date";
  const string SlotSaveDateFormat = "yyyy-MM-dd";

  static int _slot = 1;
  public static int slot {
    get => _slot;
    set {
      if (_slot == value) {
        return;
      }

      var formsFlushed = CharacterState.FlushPendingProgressBeforeSlotChange();
      var episodeFlushed = ContentEpisodeProgression.FlushPendingSave();
      var inventoryFlushed = Inventory.TryFlushPendingSave();
      if (!formsFlushed || !episodeFlushed || !inventoryFlushed) {
        Debug.LogWarning(
          "[SaveSlotManager] Refused slot change because pending state could not be saved." +
          " current_slot=" + _slot +
          " requested_slot=" + value
        );
        return;
      }
      _slot = value;
    }
  }

  static string BuildSlotDirectory(int slotNumber) {
    return Path.Combine(Application.persistentDataPath, slotNumber.ToString());
  }

  static string BuildLegacySlotDirectory(int slotNumber) {
    return Path.Combine(Application.persistentDataPath, "Saves", slotNumber.ToString());
  }

  static string BuildSavePath(string directoryPath, string name) {
    return Path.Combine(directoryPath, $"{name}.sav");
  }

  public static void SetSlot(int newSlot) {
    slot = newSlot;
  }

  public static bool SlotExists(int slotNumber) {
    if (slotNumber <= 0) return false;

    var mainPath = BuildSavePath(BuildSlotDirectory(slotNumber), "slot");
    if (File.Exists(mainPath)) {
      return true;
    }

    var legacyPath = BuildSavePath(BuildLegacySlotDirectory(slotNumber), "slot");
    return File.Exists(legacyPath);
  }

  public static bool CurrentSlotExists() {
    return SlotExists(slot);
  }

  public static int ResolveNextAvailableSlot() {
    var slotNumber = 1;

    while (SlotExists(slotNumber)) {
      slotNumber += 1;
    }

    return slotNumber;
  }

  public static void Save(string name, SaveData table) {
    NormalizeBeforeSave(name, table);
    var path = BuildSavePath(BuildSlotDirectory(slot), name);
    table.Save(path);
  }

  public static SaveData Load(string name) {
    var mainPath = BuildSavePath(BuildSlotDirectory(slot), name);
    var mainData = SaveData.Load(mainPath);
    if (mainData.Count > 0 || File.Exists(mainPath)) {
      MigrateAfterLoad(name, mainData, mainPath);
      return mainData;
    }

    var legacyPath = BuildSavePath(BuildLegacySlotDirectory(slot), name);
    var legacyData = SaveData.Load(legacyPath);
    MigrateAfterLoad(name, legacyData, legacyPath);
    return legacyData;
  }

  public static void Delete(int deleteSlot) {
    ContentEpisodeProgression.DiscardRuntimeCacheForSlot(deleteSlot);
    var mainDirectory = BuildSlotDirectory(deleteSlot);
    if (Directory.Exists(mainDirectory)) {
      Directory.Delete(mainDirectory, true);
    }

    var legacyDirectory = BuildLegacySlotDirectory(deleteSlot);
    if (Directory.Exists(legacyDirectory)) {
      Directory.Delete(legacyDirectory, true);
    }
  }

  public static string ResolveSlotEpisodeId(SaveData table) {
    var episodeId = ReadString(table, SlotEpisodeIdKey);
    if (IsKnownEpisodeId(episodeId)) {
      return episodeId;
    }

    episodeId = ReadString(table, LegacySlotEpisodeKey);
    if (IsKnownEpisodeId(episodeId)) {
      return episodeId;
    }

    return ResolveDefaultEpisodeId();
  }

  public static string ResolveSlotSaveDate(SaveData table) {
    var saveDate = ReadSlotSaveDate(table);
    if (!string.IsNullOrWhiteSpace(saveDate)) {
      return saveDate.Trim();
    }

    return "-";
  }

  static void NormalizeBeforeSave(string name, SaveData table) {
    if (!IsSlotSummarySave(name)) return;
    StampSlotEpisodeId(table);
    StampSlotSaveDate(table);
  }

  static void MigrateAfterLoad(string name, SaveData table, string sourcePath) {
    if (!IsSlotSummarySave(name)) return;
    if (table == null) return;
    if (table.Count <= 0 && !File.Exists(sourcePath)) return;

    var changed = false;
    if (EnsureSlotEpisodeId(table)) {
      changed = true;
    }

    var fallbackSaveDate = ResolveFileSaveDate(sourcePath);
    if (EnsureSlotSaveDate(table, fallbackSaveDate)) {
      changed = true;
    }

    if (!changed) return;
    table.Save(BuildSavePath(BuildSlotDirectory(slot), name));
  }

  static bool EnsureSlotEpisodeId(SaveData table) {
    if (table == null) return false;

    var episodeId = ResolveSlotEpisodeId(table);
    if (string.IsNullOrWhiteSpace(episodeId)) {
      episodeId = DefaultEpisodeId;
    }

    var current = ReadString(table, SlotEpisodeIdKey);
    var legacy = ReadString(table, LegacySlotEpisodeKey);
    var currentMatches = string.Equals(current, episodeId, StringComparison.Ordinal);
    var legacyMatches = string.Equals(legacy, episodeId, StringComparison.Ordinal);
    if (currentMatches && legacyMatches) {
      return false;
    }

    table[SlotEpisodeIdKey] = episodeId;
    table[LegacySlotEpisodeKey] = episodeId;
    return true;
  }

  static void StampSlotEpisodeId(SaveData table) {
    if (table == null) return;

    var episodeId = ContentEpisodeProgression.ResolveCurrentEpisodeId();
    if (!IsKnownEpisodeId(episodeId)) {
      episodeId = ResolveSlotEpisodeId(table);
    }

    if (string.IsNullOrWhiteSpace(episodeId)) {
      episodeId = ResolveDefaultEpisodeId();
    }

    episodeId = episodeId.Trim();
    table[SlotEpisodeIdKey] = episodeId;
    table[LegacySlotEpisodeKey] = episodeId;
  }

  static void StampSlotSaveDate(SaveData table) {
    if (table == null) return;

    var saveDate = FormatSlotSaveDate(DateTime.Now);
    table[SlotSaveDateKey] = saveDate;
    table[LegacySlotSaveDateKey] = saveDate;
  }

  static bool EnsureSlotSaveDate(SaveData table, string fallbackSaveDate) {
    if (table == null) return false;

    var saveDate = ReadSlotSaveDate(table);
    if (string.IsNullOrWhiteSpace(saveDate)) {
      saveDate = fallbackSaveDate;
    }

    if (string.IsNullOrWhiteSpace(saveDate)) {
      saveDate = FormatSlotSaveDate(DateTime.Now);
    }

    saveDate = saveDate.Trim();
    var current = ReadString(table, SlotSaveDateKey);
    var legacy = ReadString(table, LegacySlotSaveDateKey);
    var currentMatches = string.Equals(current, saveDate, StringComparison.Ordinal);
    var legacyMatches = string.Equals(legacy, saveDate, StringComparison.Ordinal);
    if (currentMatches && legacyMatches) {
      return false;
    }

    table[SlotSaveDateKey] = saveDate;
    table[LegacySlotSaveDateKey] = saveDate;
    return true;
  }

  static string ReadSlotSaveDate(SaveData table) {
    var saveDate = ReadString(table, SlotSaveDateKey);
    if (!string.IsNullOrWhiteSpace(saveDate)) {
      return saveDate;
    }

    return ReadString(table, LegacySlotSaveDateKey);
  }

  static string ResolveFileSaveDate(string path) {
    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) {
      return FormatSlotSaveDate(File.GetLastWriteTime(path));
    }

    return FormatSlotSaveDate(DateTime.Now);
  }

  static string FormatSlotSaveDate(DateTime dateTime) {
    return dateTime.ToString(SlotSaveDateFormat);
  }

  static string ResolveDefaultEpisodeId() {
    var registry = ActiveContentRegistryRuntime.Registry;
    var episodes = registry != null ? registry.Episodes : null;
    if (episodes != null && episodes.Count > 0 && episodes[0] != null) {
      var firstEpisodeId = episodes[0].id;
      if (!string.IsNullOrWhiteSpace(firstEpisodeId)) {
        return firstEpisodeId.Trim();
      }
    }

    return DefaultEpisodeId;
  }

  static bool IsKnownEpisodeId(string episodeId) {
    if (string.IsNullOrWhiteSpace(episodeId)) {
      return false;
    }

    var normalizedEpisodeId = episodeId.Trim();
    var registry = ActiveContentRegistryRuntime.Registry;
    var episodes = registry != null ? registry.Episodes : null;
    if (episodes == null || episodes.Count <= 0) {
      return true;
    }

    for (var i = 0; i < episodes.Count; i++) {
      var episode = episodes[i];
      if (episode == null) continue;
      if (string.Equals(episode.id, normalizedEpisodeId, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static bool IsSlotSummarySave(string name) {
    return string.Equals(name, "slot", StringComparison.OrdinalIgnoreCase);
  }

  static string ReadString(SaveData table, string key) {
    if (table == null) return "";
    if (string.IsNullOrWhiteSpace(key)) return "";
    if (!table.TryGetValue(key, out var value)) return "";
    if (value == null) return "";
    return Convert.ToString(value);
  }

}

// // ******* Usage
// SaveSlotManager.SetSlot("slot2");

// // ******* Create a new table and populate it
// var table = new SaveData {
//   ["HP"] = 150,
//   ["Speed"] = 3.75f,
//   ["Name"] = "Esper Knight"
// };

// // ******* Save the table with the name "stats"
// SaveSlotManager.Save("stats", table);

// // ******* Later or in a different session: Load the table
// var loaded = SaveSlotManager.Load("stats");

// // ******* Use the loaded values
// RuntimeLog.Log("Loaded HP: " + loaded["HP"]);
// RuntimeLog.Log("Loaded Speed: " + loaded["Speed"]);
// RuntimeLog.Log("Loaded Name: " + loaded["Name"]);
// ******************************************** //
