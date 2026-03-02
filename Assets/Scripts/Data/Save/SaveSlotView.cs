using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SaveSlotView : MonoBehaviour {
  const string SaveFileName = "slot.sav";

  public GameObject saveSlotPrefab;
  public GameObject saveSlotWrap;
  public MainMenuInput mainMenuGroup;
  public LoadMenuInput loadMenuGroup;
  public GameObject loadButton;
  public int padding = 1;

  public int SavesCount { set; get; } = 0;

  private float initialY = 5.34f;
  private readonly List<Action> actions = new();

  void Start() {
    actions.Add(MessageBus.On("openLoadMenu", o => ArrangeSlots()));
    ConfigureLoadButtonState(false);

    var slotDirectories = FindSlotDirectories();
    SavesCount = slotDirectories.Count;
    ConfigureLoadButtonState(SavesCount > 0);

    BuildSlotItems(slotDirectories);

    SaveSlotManager.SetSlot(SavesCount + 1);
    //Debug.Log($"Slot set {SaveSlotManager.slot}");
  }

  void OnDestroy() {
    for (int i = 0; i < actions.Count; i++) {
      actions[i].Invoke();
    }
    actions.Clear();
  }

  List<string> FindSlotDirectories() {
    var sortedSlots = new SortedDictionary<int, string>();
    CollectSlotDirectories(Path.Combine(Application.persistentDataPath, "Saves"), sortedSlots);
    CollectSlotDirectories(Application.persistentDataPath, sortedSlots);

    var ordered = new List<string>(sortedSlots.Count);
    foreach (var pair in sortedSlots) {
      ordered.Add(pair.Value);
    }
    return ordered;
  }

  void CollectSlotDirectories(string rootPath, SortedDictionary<int, string> slotsByNumber) {
    if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return;

    var directories = Directory.GetDirectories(rootPath);
    for (int i = 0; i < directories.Length; i++) {
      var directory = directories[i];
      var folderName = Path.GetFileName(directory);

      if (!int.TryParse(folderName, out var slotNumber) || slotNumber <= 0) continue;
      if (!File.Exists(Path.Combine(directory, SaveFileName))) continue;
      if (slotsByNumber.ContainsKey(slotNumber)) continue;

      slotsByNumber.Add(slotNumber, directory);
    }
  }

  void BuildSlotItems(List<string> slotDirectories) {
    if (saveSlotPrefab == null || saveSlotWrap == null || loadMenuGroup == null) return;

    loadMenuGroup.buttons.Clear();

    for (int i = 0; i < slotDirectories.Count; i++) {
      var directory = slotDirectories[i];
      var folderName = Path.GetFileName(directory);
      if (!int.TryParse(folderName, out var slotNumber)) continue;

      var go = Instantiate(saveSlotPrefab, saveSlotWrap.transform);
      var transformRef = go.transform;
      transformRef.localPosition = new Vector3(-0.11f, initialY, -0.01f * i);
      transformRef.localScale = new Vector3(1.85f, 1.85f, 1.85f);

      var slot = go.GetComponent<SaveSlot>();
      if (slot != null) {
        var loaded = LoadSlotData(slotNumber, directory);
        slot.saveNumber = folderName;
        slot.playtime = FormatPlaytime(loaded);
        slot.level = Convert.ToString(GetValueOrDefault(loaded, "level", "-"));
        slot.location = Convert.ToString(GetValueOrDefault(loaded, "location", "-"));
        slot.UpdateSlotInfo();
      }
      else {
        Debug.LogWarning("[SaveSlotView] Save slot prefab is missing SaveSlot component.");
      }

      loadMenuGroup.buttons.Add(go);

      var propagators = go.GetComponentsInChildren<ComponentPropagator>();
      for (int j = 0; j < propagators.Length; j++) {
        propagators[j].ForcePropagation();
      }
    }
  }

  SaveData LoadSlotData(int slotNumber, string directoryPath) {
    SaveSlotManager.SetSlot(slotNumber);
    var fromSlotManager = SaveSlotManager.Load("slot");
    if (fromSlotManager != null && fromSlotManager.Count > 0) {
      return fromSlotManager;
    }

    return SaveData.Load(Path.Combine(directoryPath, SaveFileName));
  }

  void ConfigureLoadButtonState(bool hasSaves) {
    if (mainMenuGroup != null) {
      mainMenuGroup.SetLoadButtonState(loadButton, hasSaves);
    }

    if (loadButton == null) return;

    var shaderList = loadButton.GetComponent<ReferenceListAllIn1AnimatorInspector>();
    var shader = shaderList != null ? shaderList.Get(0) : null;
    if (shader != null) {
      shader.SetKeyword("GREYSCALE_ON", !hasSaves);
    }

    var collider = loadButton.GetComponent<Collider2D>();
    if (collider != null) {
      collider.enabled = hasSaves;
    }
  }

  string FormatPlaytime(SaveData loaded) {
    var hours = CoerceInt(GetValueOrDefault(loaded, "playtimeHours", 0));
    var minutes = CoerceInt(GetValueOrDefault(loaded, "playtimeMinutes", 0));
    var seconds = CoerceInt(GetValueOrDefault(loaded, "playtimeSeconds", 0));
    return $"{hours:00}:{minutes:00}:{seconds:00}";
  }

  object GetValueOrDefault(SaveData loaded, string key, object fallbackValue) {
    if (loaded == null) return fallbackValue;
    if (!loaded.TryGetValue(key, out var value)) return fallbackValue;
    return value;
  }

  int CoerceInt(object value) {
    if (value is int i) return i;
    if (value is float f) return Mathf.RoundToInt(f);
    if (value is double d) return Mathf.RoundToInt((float)d);

    if (int.TryParse(Convert.ToString(value), out var parsed)) {
      return parsed;
    }
    return 0;
  }

  IEnumerator ArrangeSlotsCoroutine() {
    yield return new WaitForSeconds(.1f);
    for (var i = 0; i < loadMenuGroup.buttons.Count; i++) {
      var item = loadMenuGroup.buttons[i];
      item.transform.localPosition = new Vector3(-.11f, initialY - ((i * 8) + padding), -0.01f * i);
    }
  }

  void ArrangeSlots() {
    StartCoroutine(ArrangeSlotsCoroutine());
  }

  public float GetVisualHeight() {
    var go = gameObject;
    var renderers = go.GetComponentsInChildren<SpriteRenderer>();
    var masks = go.GetComponentsInChildren<SpriteMask>();

    float minY = float.MaxValue;
    float maxY = float.MinValue;

    foreach (var r in renderers) {
      var b = r.bounds;
      minY = Mathf.Min(minY, b.min.y);
      maxY = Mathf.Max(maxY, b.max.y);
    }

    foreach (var m in masks) {
      var b = m.bounds;
      minY = Mathf.Min(minY, b.min.y);
      maxY = Mathf.Max(maxY, b.max.y);
    }

    if (minY == float.MaxValue || maxY == float.MinValue) {
      throw new Exception("No SpriteRenderer or SpriteMask found for height calculation.");
    }

    return maxY - minY;
  }
}
