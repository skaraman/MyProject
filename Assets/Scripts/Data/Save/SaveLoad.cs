using System.IO;
using System.Collections.Generic;
using UnityEngine;

public class SaveData : Dictionary<string, object> {
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
      } else if (value is float f) {
        writer.Write((byte)2);
        writer.Write(f);
      } else if (value is string s) {
        writer.Write((byte)3);
        writer.Write(s);
      } else {
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
        default: Debug.LogWarning("Unsupported type byte: " + type); break;
      }
      table[key] = value;
    }
    return table;
  }
}


public static class SaveSlotManager {
  static string _slot = "1";
  public static string slot {
    get => _slot;
    set => _slot = value;
  }

  public static void SetSlot(string newSlot) {
    slot = newSlot;
  }

  public static void Save(string name, SaveData table) {
    var path = Application.persistentDataPath + "/" + slot + "/" + name + ".sav";
    table.Save(path);
  }

  public static SaveData Load(string name) {
    var path = Application.persistentDataPath + "/" + slot + "/" + name + ".sav";
    return SaveData.Load(path);
  }

  public static void Delete(int deleteSlot) {
    var path = Application.persistentDataPath + "/" + deleteSlot + "/";
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