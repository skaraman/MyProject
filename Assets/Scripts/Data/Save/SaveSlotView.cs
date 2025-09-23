using System;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using UnityEngine;
using NUnit.Framework.Internal;

public class SaveSlotView : MonoBehaviour {
  public GameObject saveSlotPrefab;
  public GameObject saveSlotWrap;
  public MainMenuInput mainMenuGroup;
  public LoadMenuInput loadMenuGroup;
  public GameObject loadButton;
  public int padding = 1;

  public int SavesCount { set; get; } = 0;

  private float initialY = 5.34f;
  private List<Action> actions = new();

  void Start() {
    actions.Add(MessageBus.On("openLoadMenu", o => ArrangeSlots()));

    var path = Application.persistentDataPath + "/";
    var dirs = Directory.GetDirectories(path);
    // Sort directories numerically by folder name
    Array.Sort(dirs, (x, y) => {
        var xName = Path.GetFileName(x);
        var yName = Path.GetFileName(y);
        
        if (int.TryParse(xName, out int xNum) && int.TryParse(yName, out int yNum))
        {
            return xNum.CompareTo(yNum);
        }
        
        // Fallback to string comparison if parsing fails
        return string.Compare(xName, yName, StringComparison.Ordinal);
    });
    //Debug.Log($"Found {dirs.Length} directories in: {path}");
    SavesCount = dirs.Length;
    var shader = loadButton.GetComponent<ReferenceListAllIn1AnimatorInspector>().Get(0);

    if (SavesCount > 0) {
      mainMenuGroup.buttons.Insert(1, loadButton);
      shader.SetKeyword("GREYSCALE_ON", false);
    } else {
      shader.SetKeyword("GREYSCALE_ON", true);
    }

    for (int i = 0; i < SavesCount; i++) {
      var dir = dirs[i];
      var folderName = Path.GetFileName(dir);
      if (!int.TryParse(folderName, out var slotNumber)) {
        throw new FormatException($"Invalid save folder name: {folderName}");
      }

      var go = Instantiate(saveSlotPrefab, saveSlotWrap.transform);
      var t = go.transform;
      t.localPosition = new Vector3(-.11f, initialY, -0.01f * i);
      t.localScale = new Vector3(1.85f, 1.85f, 1.85f);

      var slot = go.GetComponent<SaveSlot>();
      SaveSlotManager.SetSlot(slotNumber);
      var loaded = SaveSlotManager.Load("slot");
      slot.saveNumber = folderName;
      var hours = loaded["playtimeHours"];
      var minutes = loaded["playtimeMinutes"];
      var seconds = loaded["playtimeSeconds"];
      if ((int)hours < 10) hours = $"0{hours}";
      if ((int)minutes < 10) minutes = $"0{minutes}";
      if ((int)seconds < 10) seconds = $"0{seconds}";
      slot.playtime = $"{hours}:{minutes}:{seconds}";
      slot.level = Convert.ToString(loaded["level"]);
      slot.location = Convert.ToString(loaded["location"]);
      slot.UpdateSlotInfo();
      loadMenuGroup.buttons.Add(go);
      
      var propagators = go.GetComponentsInChildren<ComponentPropagator>();
      for (int j = 0; j < propagators.Length; j++){
        propagators[j].ForcePropagation(); // Call this manually
      }
    }

    SaveSlotManager.SetSlot(SavesCount + 1);
    //Debug.Log($"Slot set {SaveSlotManager.slot}");
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
