using System.Collections.Generic;
using UnityEngine;

public class SaveSlot : MonoBehaviour {
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
  [SerializeField] bool enableDebugLogs;

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
      PlaytimeText.content = "Playtime - " + playtime;
      PlaytimeText.Generate();
    }

    if (LevelText) {
      LevelText.content = "Level - " + level;
      LevelText.Generate();
    }

    if (LocationText) {
      LocationText.content = "Location - " + location;
      LocationText.Generate();
    }

    if (!enableDebugLogs) return;
    Debug.Log(
      "[SaveSlot] saveNumber=" + saveNumber +
      " playtime=" + playtime +
      " level=" + level +
      " location=" + location +
      " saveNumberText={" + DescribeTextRendererState(SaveNumberText) + "}" +
      " playtimeText={" + DescribeTextRendererState(PlaytimeText) + "}" +
      " levelText={" + DescribeTextRendererState(LevelText) + "}" +
      " locationText={" + DescribeTextRendererState(LocationText) + "}"
    );
  }

  static string DescribeTextRendererState(FontText target) {
    if (!target) return "missing";

    var hostRenderer = target.GetComponent<SpriteRenderer>();
    var glyphRenderer = FindFirstGeneratedGlyphRenderer(target.transform);
    return "host(" + DescribeRendererState(hostRenderer) + ") glyph(" + DescribeRendererState(glyphRenderer) + ")";
  }

  static SpriteRenderer FindFirstGeneratedGlyphRenderer(Transform root) {
    if (root == null) return null;

    for (var i = 0; i < root.childCount; i++) {
      var child = root.GetChild(i);
      if (child == null || !child.gameObject.activeSelf) continue;

      var glyphRenderer = child.GetComponent<SpriteRenderer>();
      if (glyphRenderer != null) return glyphRenderer;
    }

    return null;
  }

  static string DescribeRendererState(SpriteRenderer renderer) {
    if (renderer == null) return "missing";

    var materialName = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "null";
    var sortingLayerName = SortingLayer.IDToName(renderer.sortingLayerID);
    if (string.IsNullOrWhiteSpace(sortingLayerName)) {
      sortingLayerName = renderer.sortingLayerID.ToString();
    }

    return
      "mat=" + materialName +
      " layer=" + sortingLayerName +
      " order=" + renderer.sortingOrder +
      " mask=" + renderer.maskInteraction;
  }
}
