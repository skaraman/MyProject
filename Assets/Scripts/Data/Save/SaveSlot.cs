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
    UpdateFontText(SaveNumberText, "No ." + saveNumber);
    UpdateFontText(PlaytimeText, "Playtime - " + playtime);
    UpdateFontText(LevelText, "Level - " + level);
    UpdateFontText(LocationText, "Location - " + location);

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

  static void UpdateFontText(FontText target, string value) {
    if (!target) return;
    target.content = value;
    target.Generate();
  }

  static string DescribeTextRendererState(FontText target) {
    if (!target) return "missing";

    var hostRenderer = target.GetComponent<SpriteRenderer>();
    var hostSprite = target.GetComponent<SpriteWithNormals>();
    var glyphRenderer = FindFirstGeneratedGlyphRenderer(target.transform);
    var glyphSprite = glyphRenderer != null ? glyphRenderer.GetComponent<SpriteWithNormals>() : null;
    return
      "host(" + DescribeRendererState(hostRenderer, hostSprite) + ")" +
      " glyph(" + DescribeRendererState(glyphRenderer, glyphSprite) + ")";
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

  static string DescribeRendererState(SpriteRenderer renderer, SpriteWithNormals sprite) {
    if (renderer == null) return "missing";

    var materialName = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "null";
    var sortingLayerName = SortingLayer.IDToName(renderer.sortingLayerID);
    if (string.IsNullOrWhiteSpace(sortingLayerName)) {
      sortingLayerName = renderer.sortingLayerID.ToString();
    }

    var uiTarget = sprite != null && sprite.IsUiTarget();
    var ready = sprite == null || sprite.IsFrameReady(0, out _);

    return
      "mat=" + materialName +
      " layer=" + sortingLayerName +
      " order=" + renderer.sortingOrder +
      " mask=" + renderer.maskInteraction +
      " ui=" + (uiTarget ? 1 : 0) +
      " ready=" + (ready ? 1 : 0);
  }
}
