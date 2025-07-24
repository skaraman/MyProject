using System.IO;
using UnityEngine;

public class SaveSlotView : MonoBehaviour {
  public GameObject saveSlotPrefab;
  public GameObject saveSlotWrap;
  public MainMenuGroup mainMenuGroup;
  public GameObject loadButton;
  public int padding = 1;

  public int SavesCount { set; get; } = 0;

  void Start() {
    var path = Application.persistentDataPath + "/";
    var dirs = Directory.GetDirectories(path);
    Debug.Log($"Found {dirs.Length} directories in: {path}");
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
        throw new System.FormatException($"Invalid save folder name: {folderName}");
      }

      var go = Instantiate(saveSlotPrefab, saveSlotWrap.transform);
      var t = go.transform;
      var h = GetVisualHeight(go);
      var y = -(h + padding) * slotNumber;
      t.localPosition = new Vector3(0, y, 0);

      SaveSlotManager.SetSlot(folderName);
      var slot = go.GetComponent<SaveSlot>();
      var loaded = SaveSlotManager.Load("slot");
      slot.saveNumber = folderName;
      slot.playtime = (string)loaded["playtime"];
      slot.level = (string)loaded["level"];
      slot.location = (string)loaded["location"];
      slot.UpdateSlotInfo();
    }
  }

  float GetVisualHeight(GameObject go) {
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
      throw new System.Exception("No SpriteRenderer or SpriteMask found for height calculation.");
    }

    return maxY - minY;
  }
}
