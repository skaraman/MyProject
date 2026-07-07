using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SaveSlotView : MonoBehaviour {
  const string SaveFileName = "slot.sav";
  const int SortingOrderBandSize = 128;

  public GameObject saveSlotPrefab;
  public GameObject saveSlotWrap;
  public MainMenuInput mainMenuGroup;
  public LoadMenuInput loadMenuGroup;
  public GameObject loadButton;
  public int padding = 1;

  public int SavesCount { set; get; } = 0;

  private float initialY = 5.34f;
  private int knownMaxSlotNumber;
  private readonly List<Action> actions = new();

  void Start() {
    actions.Add(MessageBus.On("openLoadMenu", o => ArrangeSlots()));
    actions.Add(MessageBus.On("loadMenu.deleteConfirmed", RebuildSlots));
    ConfigureLoadButtonState(false);
    RebuildSlots();
  }

  void OnDestroy() {
    for (int i = 0; i < actions.Count; i++) {
      actions[i].Invoke();
    }
    actions.Clear();
  }

  SortedDictionary<int, string> FindSlotDirectories() {
    var sortedSlots = new SortedDictionary<int, string>();
    CollectSlotDirectories(Path.Combine(Application.persistentDataPath, "Saves"), sortedSlots);
    CollectSlotDirectories(Application.persistentDataPath, sortedSlots);
    return sortedSlots;
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

  public void RebuildSlots(object payload = null) {
    var deletedSlotNumber = CoercePositiveInt(payload);
    if (deletedSlotNumber > 0) {
      knownMaxSlotNumber = Mathf.Max(knownMaxSlotNumber, deletedSlotNumber);
    }

    var slotDirectories = FindSlotDirectories();
    TrackKnownSlotNumbers(slotDirectories);

    SavesCount = slotDirectories.Count;
    var renderedSlotCount = ResolveRenderedSlotCount(slotDirectories);
    ConfigureLoadButtonState(renderedSlotCount > 0);
    BuildSlotItems(slotDirectories, renderedSlotCount);
    SaveSlotManager.SetSlot(ResolveNextNewSlotNumber(slotDirectories));

    if (deletedSlotNumber > 0 && loadMenuGroup != null) {
      loadMenuGroup.SetActiveSlotNumber(deletedSlotNumber);
    }
  }

  void BuildSlotItems(SortedDictionary<int, string> slotDirectories, int renderedSlotCount) {
    if (saveSlotPrefab == null || saveSlotWrap == null || loadMenuGroup == null) return;

    ClearSlotItems();
    loadMenuGroup.buttons.Clear();
    loadMenuGroup.ResetSelection();

    for (int slotNumber = 1; slotNumber <= renderedSlotCount; slotNumber++) {
      var slotIndex = slotNumber - 1;
      slotDirectories.TryGetValue(slotNumber, out var directory);
      var go = Instantiate(saveSlotPrefab, saveSlotWrap.transform);
      var transformRef = go.transform;
      transformRef.localPosition = ResolveSlotLocalPosition(slotIndex);
      transformRef.localScale = new Vector3(1.85f, 1.85f, 1.85f);
      ApplySlotSortingBand(go, slotIndex);

      var slot = go.GetComponent<SaveSlot>();
      if (slot != null) {
        PopulateSlot(slot, slotNumber, directory);
      }
      else {
        Debug.LogWarning("[SaveSlotView] Save slot prefab is missing SaveSlot component.");
      }

      loadMenuGroup.buttons.Add(go);
    }
  }

  void ClearSlotItems() {
    if (saveSlotWrap == null) return;

    var wrapTransform = saveSlotWrap.transform;
    for (int i = wrapTransform.childCount - 1; i >= 0; i--) {
      var child = wrapTransform.GetChild(i).gameObject;
      child.SetActive(false);
      Destroy(child);
    }
  }

  void PopulateSlot(SaveSlot slot, int slotNumber, string directory) {
    slot.saveNumber = slotNumber.ToString();

    if (!string.IsNullOrWhiteSpace(directory)) {
      var loaded = LoadSlotData(slotNumber, directory);
      slot.playtime = FormatPlaytime(loaded);
      slot.level = Convert.ToString(GetValueOrDefault(loaded, "level", "-"));
      slot.location = Convert.ToString(GetValueOrDefault(loaded, "location", "-"));
      slot.episode = SaveSlotManager.ResolveSlotEpisodeId(loaded);
      slot.UpdateSlotInfo();
      return;
    }

    slot.forms.Clear();
    slot.playtime = "-";
    slot.level = "-";
    slot.location = "-";
    slot.episode = "-";
    slot.UpdateSlotInfo();
  }

  void TrackKnownSlotNumbers(SortedDictionary<int, string> slotDirectories) {
    foreach (var pair in slotDirectories) {
      knownMaxSlotNumber = Mathf.Max(knownMaxSlotNumber, pair.Key);
    }
  }

  int ResolveRenderedSlotCount(SortedDictionary<int, string> slotDirectories) {
    TrackKnownSlotNumbers(slotDirectories);
    return knownMaxSlotNumber;
  }

  int ResolveNextNewSlotNumber(SortedDictionary<int, string> slotDirectories) {
    for (int slotNumber = 1; slotNumber <= knownMaxSlotNumber; slotNumber++) {
      if (!slotDirectories.ContainsKey(slotNumber)) {
        return slotNumber;
      }
    }

    return knownMaxSlotNumber + 1;
  }

  void ApplySlotSortingBand(GameObject slotObject, int slotIndex) {
    if (slotObject == null) return;

    var sortingOffset = ResolveSlotSortingOffset(slotIndex);
    OffsetChildRendererSorting(slotObject, sortingOffset);
    OffsetChildMaskSorting(slotObject, sortingOffset);
  }

  static int ResolveSlotSortingOffset(int slotIndex) {
    if (slotIndex <= 0) return 0;
    return slotIndex * SortingOrderBandSize;
  }

  static void OffsetChildRendererSorting(GameObject root, int sortingOffset) {
    if (root == null || sortingOffset == 0) return;

    var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
    for (var i = 0; i < renderers.Length; i++) {
      var renderer = renderers[i];
      if (renderer == null) continue;
      renderer.sortingOrder += sortingOffset;
    }
  }

  static void OffsetChildMaskSorting(GameObject root, int sortingOffset) {
    if (root == null || sortingOffset == 0) return;

    var masks = root.GetComponentsInChildren<SpriteMask>(true);
    for (var i = 0; i < masks.Length; i++) {
      var mask = masks[i];
      if (mask == null) continue;
      mask.sortingOrder += sortingOffset;
      mask.frontSortingOrder += sortingOffset;
      mask.backSortingOrder += sortingOffset;
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
    ArrangeSlotsImmediate();
  }

  Vector3 ResolveSlotLocalPosition(int slotIndex) {
    return new Vector3(-.11f, initialY - ((slotIndex * 8) + padding), -0.01f * slotIndex);
  }

  void ArrangeSlotsImmediate() {
    for (var i = 0; i < loadMenuGroup.buttons.Count; i++) {
      var item = loadMenuGroup.buttons[i];
      item.transform.localPosition = ResolveSlotLocalPosition(i);
    }
  }

  void ArrangeSlots() {
    StartCoroutine(ArrangeSlotsCoroutine());
  }

  static int CoercePositiveInt(object value) {
    if (value is int i) return Mathf.Max(i, 0);
    if (value is string s && int.TryParse(s, out var parsed)) {
      return Mathf.Max(parsed, 0);
    }
    return 0;
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
