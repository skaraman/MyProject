#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed partial class TrimmedAtlasExporterWindow {
  void DrawAnalysisPreview() {
    EditorGUILayout.Space();
    EditorGUILayout.LabelField("Slice Preview", EditorStyles.boldLabel);

    if (!TryGetSourcePath(out var sourcePath, false)) {
      EditorGUILayout.HelpBox("Select a source atlas to analyze slices and offsets.", MessageType.None);
      return;
    }

    if (!HasFreshAnalysis(sourcePath)) {
      EditorGUILayout.HelpBox("Click 'Analyze Slice Offsets' to browse slices and inspect their x/y offsets.", MessageType.None);
      return;
    }

    if (analyzedAtlas == null || analyzedAtlas.sprites == null || analyzedAtlas.sprites.Count <= 0) {
      EditorGUILayout.HelpBox("No slice data is available for this atlas.", MessageType.Warning);
      return;
    }

    EditorGUILayout.HelpBox(
      "Exact Offset is the value to use for exact reconstruction. Weighted Offset is the visible-pixel center of mass and is included for comparison only.",
      MessageType.None);
    EditorGUILayout.LabelField(
      "Summary",
      analyzedAtlas.columns + "x" + analyzedAtlas.rows +
      " cells, empty=" + analyzedAtlas.emptyCellCount +
      ", packed=" + analyzedAtlas.atlasWidth + "x" + analyzedAtlas.atlasHeight +
      ", packed_area_pct=" + analyzedAtlas.packedAreaPctOfSource.ToString("0.00"));

    hideEmptySlices = EditorGUILayout.Toggle("Hide Empty Slices", hideEmptySlices);
    selectedSliceIndex = Mathf.Clamp(selectedSliceIndex < 0 ? 0 : selectedSliceIndex, 0, analyzedAtlas.sprites.Count - 1);
    if (hideEmptySlices && analyzedAtlas.sprites[selectedSliceIndex].empty) {
      selectedSliceIndex = FindFirstVisibleSliceIndex();
    }

    selectedSliceIndex = EditorGUILayout.IntSlider("Selected Slice", selectedSliceIndex + 1, 1, analyzedAtlas.sprites.Count) - 1;
    if (hideEmptySlices && analyzedAtlas.sprites[selectedSliceIndex].empty) {
      selectedSliceIndex = FindFirstVisibleSliceIndex();
    }

    var selected = analyzedAtlas.sprites[selectedSliceIndex];
    using (new EditorGUILayout.VerticalScope("box")) {
      EditorGUILayout.LabelField(selected.name + " (slice " + (selected.index + 1) + ")", EditorStyles.boldLabel);
      EditorGUILayout.LabelField("Exact Offset", FormatPoint(selected.offsetFromCellCenterPx));
      EditorGUILayout.LabelField("Weighted Offset", FormatPoint(selected.weightedCenterOffsetPx));
      EditorGUILayout.LabelField("Trim Rect In Cell", FormatRect(selected.trimRectInCell));
      EditorGUILayout.LabelField("Packed Rect", FormatRect(selected.packedRect));

      var previewRow = GUILayoutUtility.GetRect(10f, 210f, GUILayout.ExpandWidth(true));
      var panelWidth = Mathf.Max(120f, (previewRow.width - 16f) * 0.5f);
      var leftRect = new Rect(previewRow.x, previewRow.y, panelWidth, previewRow.height);
      var rightRect = new Rect(previewRow.x + panelWidth + 16f, previewRow.y, panelWidth, previewRow.height);
      DrawTexturePreview(leftRect, "Source Cell", selected.sourceCell, selected.empty, fitToContent: false);
      DrawTexturePreview(rightRect, "Trimmed Crop", BuildAtlasRect(selected.sourceCell, selected.trimRectInCell), selected.empty, fitToContent: true);
    }

    EditorGUILayout.Space();
    EditorGUILayout.LabelField("Offsets", EditorStyles.boldLabel);
    using (var scroll = new EditorGUILayout.ScrollViewScope(sliceListScrollPosition, GUILayout.Height(280f))) {
      sliceListScrollPosition = scroll.scrollPosition;
      for (var i = 0; i < analyzedAtlas.sprites.Count; i++) {
        var sprite = analyzedAtlas.sprites[i];
        if (hideEmptySlices && sprite.empty) continue;

        using (new EditorGUILayout.HorizontalScope("box")) {
          GUILayout.Label(selectedSliceIndex == i ? ">" : "", GUILayout.Width(10f));
          EditorGUILayout.LabelField(sprite.name, GUILayout.Width(180f));
          EditorGUILayout.LabelField(FormatPoint(sprite.offsetFromCellCenterPx), GUILayout.Width(150f));
          EditorGUILayout.LabelField(sprite.empty ? "Empty" : (sprite.trimRectInCell.width + "x" + sprite.trimRectInCell.height), GUILayout.Width(70f));
          if (GUILayout.Button("View", GUILayout.Width(60f))) {
            selectedSliceIndex = i;
          }
        }
      }
    }
  }

  int FindFirstVisibleSliceIndex() {
    if (analyzedAtlas == null || analyzedAtlas.sprites == null || analyzedAtlas.sprites.Count <= 0) return 0;
    for (var i = 0; i < analyzedAtlas.sprites.Count; i++) {
      if (hideEmptySlices && analyzedAtlas.sprites[i].empty) continue;
      return i;
    }

    return 0;
  }

  void DrawTexturePreview(Rect rect, string title, PixelRect atlasRect, bool empty, bool fitToContent) {
    var titleRect = new Rect(rect.x, rect.y, rect.width, 18f);
    var imageRect = new Rect(rect.x, rect.y + 20f, rect.width, rect.height - 20f);
    GUI.Label(titleRect, title, EditorStyles.miniBoldLabel);
    EditorGUI.DrawRect(imageRect, new Color(0.14f, 0.14f, 0.14f, 1f));

    if (empty) {
      GUI.Label(imageRect, "Empty", EditorStyles.centeredGreyMiniLabel);
      DrawOutline(imageRect, Color.gray, 1f);
      return;
    }

    var drawRect = fitToContent ? FitRectInside(imageRect, atlasRect.width, atlasRect.height, 8f) : imageRect;
    DrawTextureRegion(drawRect, atlasRect);
    DrawOutline(drawRect, Color.gray, 1f);
  }

  void DrawTextureRegion(Rect rect, PixelRect atlasRect) {
    if (Event.current.type != EventType.Repaint) return;
    if (analyzedPreviewTexture == null || analyzedAtlas == null) return;

    var uv = new Rect(
      atlasRect.x / (float)analyzedAtlas.sourceWidth,
      atlasRect.y / (float)analyzedAtlas.sourceHeight,
      atlasRect.width / (float)analyzedAtlas.sourceWidth,
      atlasRect.height / (float)analyzedAtlas.sourceHeight);
    GUI.DrawTextureWithTexCoords(rect, analyzedPreviewTexture, uv, true);
  }

  void DrawOutline(Rect rect, Color color, float thickness) {
    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
    EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
    EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
    EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
  }

  Rect FitRectInside(Rect container, int contentWidth, int contentHeight, float inset) {
    var inner = new Rect(
      container.x + inset,
      container.y + inset,
      Mathf.Max(1f, container.width - (inset * 2f)),
      Mathf.Max(1f, container.height - (inset * 2f)));
    if (contentWidth <= 0 || contentHeight <= 0) return inner;

    var scale = Mathf.Min(inner.width / contentWidth, inner.height / contentHeight);
    var width = contentWidth * scale;
    var height = contentHeight * scale;
    return new Rect(
      inner.x + ((inner.width - width) * 0.5f),
      inner.y + ((inner.height - height) * 0.5f),
      width,
      height);
  }

  static PixelRect BuildAtlasRect(PixelRect sourceCell, PixelRect trimRectInCell) {
    return new PixelRect(
      sourceCell.x + trimRectInCell.x,
      sourceCell.y + trimRectInCell.y,
      trimRectInCell.width,
      trimRectInCell.height);
  }

  static string FormatPoint(PixelPoint point) {
    return "x=" + point.x.ToString("0.###") + ", y=" + point.y.ToString("0.###");
  }

  static string FormatRect(PixelRect rect) {
    return "x=" + rect.x + ", y=" + rect.y + ", w=" + rect.width + ", h=" + rect.height;
  }
}
#endif
