using System.IO;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class SaveData : Dictionary<string, object> {
  public void Save(string path) {
    using var writer = new BinaryWriter(File.Open(path, FileMode.Create));
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
  public static string slot
  {
    get => _slot;
    set => _slot = value;
  }

  public static void SetSlot(string newSlot) {
    slot = newSlot;
  }

  public static void Save(string name, SaveData t) {
    var path = Application.persistentDataPath + "/" + slot + "/" + name + ".sav";
    t.Save(path);
  }

  public static SaveData Load(string name) {
    var path = Application.persistentDataPath + "/" + slot + "/" + name + ".sav";
    return SaveData.Load(path);
  }
}

// Usage
// SaveSlotManager.SetSlot("slot2");
// // Create a new table and populate it
// var table = new SaveData {
//   ["HP"] = 150,
//   ["Speed"] = 3.75f,
//   ["Name"] = "Esper Knight"
// };
// // Save the table with the name "stats"
// SaveSlotManager.Save("stats", table);
// // Later or in a different session: Load the table
// var loaded = SaveSlotManager.Load("stats");
// // Use the loaded values
// Debug.Log("Loaded HP: " + loaded["HP"]);
// Debug.Log("Loaded Speed: " + loaded["Speed"]);
// Debug.Log("Loaded Name: " + loaded["Name"]);
// ******************************************************************************************* //
public class SaveSlotView : MonoBehaviour {
  public GameObject saveSlotPrefab;
  public GameObject saveSlotWrap;
  public int padding = 1;

  public int savesCount { set; get; } = 0;

  void Start() {
    var path = Application.persistentDataPath + "/";
    var dirs = Directory.GetDirectories(path);
    Debug.Log($"Found {dirs.Length} directories in: {path}");
    savesCount = dirs.Length;
    for (int i = 0; i < dirs.Length; i++) {
      var dir = dirs[i];
      Debug.Log($"Directory: {dir}");

      var slotNumber = int.Parse(name);
      var go = Instantiate(saveSlotPrefab, saveSlotWrap.transform);
      var t = go.transform as RectTransform;
      var h = t.rect.height;
      var y = -(h + padding) * slotNumber;
      t.anchoredPosition = new Vector2(0, y);

      var slot = go.GetComponent<SaveSlot>();
      var loaded = SaveSlotManager.Load("slot");
      slot.saveNumber = name;
      slot.playtime = (string)loaded["playtime"];
      slot.level = (string)loaded["level"];
      slot.location = (string)loaded["location"];
      slot.UpdateSlotInfo();
    }
  }
  

}

