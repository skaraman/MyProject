using UnityEngine;
using System.Collections.Generic;

public class SaveSlot : MonoBehaviour
{
  public string saveNumber;
  public string avatar;
  public List<string> forms = new List<string> { "Base" };
  public string playtime;
  public string level;
  public string location;

  public FontText SaveNumberText;
  public FontText PlaytimeText;
  public FontText LevelText;
  public FontText LocationText;

  void Start() {
    UpdateSlotInfo();
  }

  [ForceUpdate]
  public void UpdateSlotInfo() {
    Transform formsParent = transform.Find("Forms");
    foreach (Transform child in formsParent) {
      bool shouldBeActive = forms.Contains(child.name);
      child.gameObject.SetActive(shouldBeActive);
    }
    if (SaveNumberText) {
      SaveNumberText.content = "No ." + saveNumber;
      SaveNumberText.Generate();
    }

    if (PlaytimeText) {
      PlaytimeText.content = "Playtime: " + playtime;
      PlaytimeText.Generate();
    }

    if (LevelText) {
      LevelText.content = "Level: " + level;
      LevelText.Generate();
    }

    if (LocationText) {
      LocationText.content = "Location: " + location;
      LocationText.Generate();
    }

    Debug.Log($"[SaveSlot] Updated text fields with saveNumber: {saveNumber}, playtime: {playtime}, level: {level}, location: {location}");
  }
}
