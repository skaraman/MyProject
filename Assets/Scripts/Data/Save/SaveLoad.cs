using System.IO;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;


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
        Debug.LogWarning("Unsupported value type: " + value?.GetType());
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
    // Check if object was null
    if (ContainsKey($"{prefix}_null") && (bool)this[$"{prefix}_null"]) {
      return default(T);
    }

    var type = typeof(T);
    var obj = constructor();

    // Handle Dictionary<string, TValue>
    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
        type.GetGenericArguments()[0] == typeof(string)) {

      var valueType = type.GetGenericArguments()[1];
      var dict = obj as System.Collections.IDictionary;

      if (ContainsKey($"{prefix}_count")) {
        var keys = this.Keys.Where(k => k.StartsWith($"{prefix}_") && k != $"{prefix}_count")
          .Select(k => k.Substring($"{prefix}_".Length))
          .Where(k => !k.Contains("_") || IsComplexKey(k, prefix))
          .Distinct();

        foreach (var key in keys) {
          var value = UnflattenValue($"{prefix}_{key}", valueType);
          if (value != null || !valueType.IsValueType) {
            dict[key] = value;
          }
        }
      }
      return obj;
    }

    // Handle Lists
    if (typeof(System.Collections.IList).IsAssignableFrom(type) && type.IsGenericType) {
      var list = obj as System.Collections.IList;
      var elementType = type.GetGenericArguments()[0];

      if (ContainsKey($"{prefix}_count")) {
        int count = (int)this[$"{prefix}_count"];
        for (int i = 0; i < count; i++) {
          var value = UnflattenValue($"{prefix}_{i}", elementType);
          list.Add(value);
        }
      }
      return obj;
    }

    // Handle custom objects
    var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanRead && p.CanWrite);
    var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

    foreach (var prop in properties) {
      try {
        var value = UnflattenValue($"{prefix}_{prop.Name}", prop.PropertyType);
        if (value != null || !prop.PropertyType.IsValueType) {
          prop.SetValue(obj, value);
        }
      }
      catch (Exception e) {
        Debug.LogWarning($"Failed to unflatten property {prop.Name}: {e.Message}");
      }
    }

    foreach (var field in fields) {
      try {
        var value = UnflattenValue($"{prefix}_{field.Name}", field.FieldType);
        if (value != null || !field.FieldType.IsValueType) {
          field.SetValue(obj, value);
        }
      }
      catch (Exception e) {
        Debug.LogWarning($"Failed to unflatten field {field.Name}: {e.Message}");
      }
    }

    return obj;
  }

  private object UnflattenValue(string prefix, Type targetType) {
    if (IsPrimitive(targetType)) {
      return ContainsKey(prefix) ? Convert.ChangeType(this[prefix], targetType) : GetDefaultValue(targetType);
    }

    if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>)) {
      var method = typeof(SaveData).GetMethod(nameof(UnflattenObject), BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string), typeof(Func<>).MakeGenericType(targetType) }, null);
      var genericMethod = method.MakeGenericMethod(targetType);
      var constructor = Expression.Lambda(Expression.New(targetType)).Compile();
      return genericMethod.Invoke(this, new object[] { prefix, constructor });
    }

    if (typeof(System.Collections.IList).IsAssignableFrom(targetType) && targetType.IsGenericType) {
      var method = typeof(SaveData).GetMethod(nameof(UnflattenObject), BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null);
      var genericMethod = method.MakeGenericMethod(targetType);
      return genericMethod.Invoke(this, new object[] { prefix });
    }

    // Custom objects
    if (targetType.GetConstructor(Type.EmptyTypes) != null) {
      var method = typeof(SaveData).GetMethod(nameof(UnflattenObject), BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null);
      var genericMethod = method.MakeGenericMethod(targetType);
      return genericMethod.Invoke(this, new object[] { prefix });
    }

    return GetDefaultValue(targetType);
  }

  private bool IsComplexKey(string key, string prefix) {
    var remaining = key;
    var parts = remaining.Split('_');
    return parts.Length == 1; // Simple key like "Base", not nested like "Base_SomeProperty"
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
    var keysToRemove = Keys.Where(k => k.StartsWith(prefix)).ToList();
    foreach (var key in keysToRemove) {
      Remove(key);
    }
  }

  /// <summary>
  /// Check if any keys exist with the specified prefix
  /// </summary>
  public bool HasPrefix(string prefix) {
    return Keys.Any(k => k.StartsWith(prefix));
  }

  /// <summary>
  /// Get all keys with the specified prefix
  /// </summary>
  public IEnumerable<string> GetKeysWithPrefix(string prefix) {
    return Keys.Where(k => k.StartsWith(prefix));
  }
  #endregion
}

// Storing a mix of simple and complex data
//saveData["score"] = 500;  // Simple int
//saveData.SetComplex("player", new Player { Name = "Bob", Health = 80 });  // Complex

// Retrieving
//int score = saveData.GetComplex<int>("score");  // Simple (500)
//Player player = saveData.GetComplex<Player>("player");  // Complex (Name="Bob", Health=80)


public static class SaveSlotManager {
  static int _slot = 1;
  public static int slot {
    get => _slot;
    set => _slot = value;
  }

  public static void SetSlot(int newSlot) {
    slot = newSlot;
  }

  public static void Save(string name, SaveData table) {
    var path = Application.persistentDataPath + $"/{slot}/{name}.sav";
    table.Save(path);
  }

  public static SaveData Load(string name) {
    var path = Application.persistentDataPath + $"/{slot}/{name}.sav";
    return SaveData.Load(path);
  }

  public static void Delete(int deleteSlot) {
    var path = Application.persistentDataPath + $"/{deleteSlot}/";
    var dir = Path.GetDirectoryName(path);
    if (Directory.Exists(dir)) Directory.Delete(dir, true);
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
// Debug.Log("Loaded HP: " + loaded["HP"]);
// Debug.Log("Loaded Speed: " + loaded["Speed"]);
// Debug.Log("Loaded Name: " + loaded["Name"]);
// ******************************************** //