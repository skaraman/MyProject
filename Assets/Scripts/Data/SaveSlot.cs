using System.ComponentModel;
using UnityEngine;
using System.Collections;
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
  public FontText PlayTimeText;
  public FontText Level;
  public FontText Location;

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

    if (PlayTimeText) {
      PlayTimeText.content = "Playtime: " + playtime;
      PlayTimeText.Generate();
    }

    if (Level) {
      Level.content = "Level: " + level;
      Level.Generate();
    }

    if (Location) {
      Location.content = "Location: " + location;
      Location.Generate();
    }

    Debug.Log($"[SaveSlot] Updated text fields with saveNumber: {saveNumber}, playtime: {playtime}, level: {level}, location: {location}");
  }
}
