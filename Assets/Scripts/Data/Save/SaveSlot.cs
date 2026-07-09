using System;
using System.Collections.Generic;
using UnityEngine;

public class SaveSlot : MonoBehaviour {
  const int SortingOrderBandSize = 128;
  const int DetailTextSortingOrder = 45;

  public string saveNumber;
  public string avatar;
  public List<string> forms = new List<string> { "Base" };
  public string playtime;
  public string level;
  public string location;
  public string episode;
  public string saveDate;

  public FontText SaveNumberText;
  public FontText PlaytimeText;
  public FontText LevelText;
  public FontText LocationText;
  public FontText EpisodeText;
  public FontText DateText;
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
    UpdateMaskedFontText(SaveNumberText, "No ." + saveNumber);
    UpdateDetailFontText(PlaytimeText, "Playtime - " + playtime);
    UpdateDetailFontText(LevelText, "Level - " + level);
    UpdateDetailFontText(LocationText, "Location - " + location);
    UpdateDetailFontText(EnsureEpisodeText(), "Episode - " + episode);
    UpdateDetailFontText(EnsureDateText(), "Date - " + saveDate);

    if (!enableDebugLogs) return;
    Debug.Log(
      "[SaveSlot] saveNumber=" + saveNumber +
      " playtime=" + playtime +
      " level=" + level +
      " location=" + location +
      " episode=" + episode +
      " saveDate=" + saveDate +
      " saveNumberText={" + DescribeTextRendererState(SaveNumberText) + "}" +
      " playtimeText={" + DescribeTextRendererState(PlaytimeText) + "}" +
      " levelText={" + DescribeTextRendererState(LevelText) + "}" +
      " locationText={" + DescribeTextRendererState(LocationText) + "}" +
      " episodeText={" + DescribeTextRendererState(EpisodeText) + "}" +
      " dateText={" + DescribeTextRendererState(DateText) + "}"
    );
  }

  FontText EnsureEpisodeText() {
    return EnsureNamedText(
      ref EpisodeText,
      "Episode",
      LocationText,
      ResolveEpisodeLocalPosition
    );
  }

  FontText EnsureDateText() {
    return EnsureNamedText(
      ref DateText,
      "Date",
      EnsureEpisodeText(),
      ResolveDateLocalPosition
    );
  }

  FontText EnsureNamedText(
    ref FontText target,
    string childName,
    FontText cloneSource,
    Func<Vector3, Vector3> resolveClonePosition
  ) {
    if (target != null) {
      return target;
    }

    target = FindFontTextByName(transform, childName);
    if (target != null) {
      return target;
    }

    if (!Application.isPlaying) return null;
    if (cloneSource == null) return null;

    var sourceTransform = cloneSource.transform;
    var parent = sourceTransform.parent;
    var textObject = Instantiate(cloneSource.gameObject, parent);
    textObject.name = childName;
    textObject.transform.localPosition = resolveClonePosition(sourceTransform.localPosition);
    textObject.transform.localRotation = sourceTransform.localRotation;
    textObject.transform.localScale = sourceTransform.localScale;
    target = textObject.GetComponent<FontText>();
    return target;
  }

  static Vector3 ResolveEpisodeLocalPosition(Vector3 sourcePosition) {
    return new Vector3(
      sourcePosition.x,
      sourcePosition.y - 0.9f,
      sourcePosition.z
    );
  }

  static Vector3 ResolveDateLocalPosition(Vector3 sourcePosition) {
    return new Vector3(
      sourcePosition.x,
      sourcePosition.y - 0.9f,
      sourcePosition.z
    );
  }

  static FontText FindFontTextByName(Transform root, string childName) {
    if (root == null) return null;
    if (string.IsNullOrWhiteSpace(childName)) return null;

    for (var i = 0; i < root.childCount; i++) {
      var child = root.GetChild(i);
      if (child == null) continue;

      if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase)) {
        var fontText = child.GetComponent<FontText>();
        if (fontText != null) {
          return fontText;
        }
      }
    }

    for (var i = 0; i < root.childCount; i++) {
      var child = root.GetChild(i);
      if (child == null) continue;

      var nested = FindFontTextByName(child, childName);
      if (nested != null) {
        return nested;
      }
    }

    return null;
  }

  static void UpdateMaskedFontText(FontText target, string value) {
    if (!target) return;
    ApplyMaskedTextRendererContract(target);
    target.content = value;
    target.Generate();
    ApplyMaskedTextRendererContract(target);
  }

  static void UpdateDetailFontText(FontText target, string value) {
    if (!target) return;
    ApplyDetailTextRendererContract(target);
    target.content = value;
    target.Generate();
    ApplyDetailTextRendererContract(target);
  }

  static void ApplyMaskedTextRendererContract(FontText target) {
    if (!target) return;

    var renderers = target.GetComponentsInChildren<SpriteRenderer>(true);
    for (var i = 0; i < renderers.Length; i++) {
      var renderer = renderers[i];
      if (renderer == null) continue;
      renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
    }
  }

  static void ApplyDetailTextRendererContract(FontText target) {
    if (!target) return;

    var renderers = target.GetComponentsInChildren<SpriteRenderer>(true);
    for (var i = 0; i < renderers.Length; i++) {
      var renderer = renderers[i];
      if (renderer == null) continue;
      renderer.maskInteraction = SpriteMaskInteraction.None;
      renderer.sortingOrder = ResolveDetailTextSortingOrder(renderer.sortingOrder);
    }
  }

  static int ResolveDetailTextSortingOrder(int currentSortingOrder) {
    var bandOffset = Mathf.FloorToInt(currentSortingOrder / (float)SortingOrderBandSize) * SortingOrderBandSize;
    return bandOffset + DetailTextSortingOrder;
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
