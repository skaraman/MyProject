using System;
using System.Collections.Generic;
using CustomInspector;
using UnityEngine;

public class FontText : MonoBehaviour {
  public GameObject characterPrefab;
  [Button(nameof(Reset), label = "Reset Text", size = Size.small)]
  [Button(nameof(Generate), label = "Force Text", size = Size.small)]

  [FixedValues("Hand", "Plate", "Walkway", "Vamp")] public string font = "Hand";
  public string content = "";
  public float spaceWidth = 1;
  public float padding = 0;
  public float mono = 0;
  public float maxWidth = -1;
  public float marginX = 0;
  public float marginY = 0;
  public float offsetX = 0;
  public float offsetY = 0;
  [FixedValues("left", "center", "right")] public string justifyX = "left";
  [FixedValues("bottom", "center", "top")] public string justifyY = "bottom";
  [FixedValues("auto", "up", "down")] public string lineDirection = "auto";

  private List<GameObject> activeChars = new();
  private List<SpriteRenderer> activeCharRenderers = new();
  private List<int> activeCharSourceIndices = new();
  private Stack<GameObject> charPool = new();
  private List<float> totalWidths = new();
  private List<float> lineHeights = new();
  private List<int> lineCharCounts = new(); // Track chars per line
  private readonly List<Transform> childScratch = new();
  private readonly List<GameObject> gameObjectScratch = new();

  private int line = 1;
  private float width = 0;
  private float height = 0;
  private float actualWidth = 0;
  private float actualHeight = 0;
  private float tallest = 0;
  private int visibleContentCharacterCount = -1;
  private string prevContent = "";
  private SpriteRenderer cachedHostRenderer;
  private ComponentPropagator cachedComponentPropagator;

  void OnEnable() {
    if (characterPrefab == null) return;
    Generate();
  }

  void Update() {
    if (content != prevContent) {
      prevContent = content;
      Generate();
    }
  }

  [ForceUpdate]
  public void Generate() {
    if (characterPrefab == null) return;

    CacheRuntimeReferences();
    Clear();
    width = 0;
    height = 0;
    line = 1;
    totalWidths.Add(0);
    lineHeights.Add(0);
    lineCharCounts.Add(0);
    actualWidth = 0;
    actualHeight = 0;
    tallest = 0;

    for (var i = 0; i < content.Length;) {
      var c = content[i];

      if (c == '\n') {
        NextLine();
        i++;
        continue;
      }

      if (c == ' ') {
        i = ProcessSpaces(i);
        continue;
      }

      i = ProcessWord(i);
    }

    actualHeight = 0;
    for (int i = 0; i < lineHeights.Count; i++) actualHeight += lineHeights[i];
    DoAlign();
    SyncActiveGlyphRendererStates();
    ApplyVisibleCharacterCountToGlyphs();
    prevContent = content;
    ForceGlyphPropagation();
  }

  int ProcessSpaces(int startIndex) {
    var endIndex = startIndex;
    while (endIndex < content.Length && content[endIndex] == ' ') {
      endIndex++;
    }

    var spacesWidth = (endIndex - startIndex) * spaceWidth;
    if (spacesWidth <= 0f) {
      return endIndex;
    }

    var nextWordWidth = MeasureNextWordWidth(endIndex);
    if (lineCharCounts[line - 1] > 0 &&
        nextWordWidth > 0f &&
        maxWidth > 0f &&
        width + spacesWidth + nextWordWidth > maxWidth) {
      NextLine();
      return endIndex;
    }

    AddSpaceWidth(spacesWidth);
    return endIndex;
  }

  int ProcessWord(int startIndex) {
    var wordWidth = MeasureWordWidth(startIndex, out var endIndex);
    if (ShouldWrapWord(wordWidth)) {
      NextLine();
    }

    for (var i = startIndex; i < endIndex; i++) {
      EmitCharacter(i, content[i]);
    }

    return endIndex;
  }

  GameObject GetCharFromPool() {
    var obj = charPool.Count > 0 ? charPool.Pop() : Instantiate(characterPrefab);
    obj.transform.SetParent(transform, false);
    obj.SetActive(true);
    return obj;
  }

  void RecycleChar(GameObject obj) {
    if (obj == null) return;
    obj.SetActive(false);
    charPool.Push(obj);
  }

  void Clear() {
    foreach (var obj in activeChars) {
      obj.SetActive(false);
      charPool.Push(obj);
    }
    activeChars.Clear();
    activeCharRenderers.Clear();
    activeCharSourceIndices.Clear();
    totalWidths.Clear();
    lineHeights.Clear();
    lineCharCounts.Clear();

    childScratch.Clear();
    for (int i = 0; i < transform.childCount; i++) {
      var child = transform.GetChild(i);
      childScratch.Add(child);
    }

    for (var i = 0; i < childScratch.Count; i++) {
      var child = childScratch[i];
      var go = child.gameObject;
      if (!activeChars.Contains(go) && !charPool.Contains(go)) {
        DestroyImmediate(go);
      }
    }
    childScratch.Clear();
  }

  bool TryGetCharacterMetrics(FontCharacter fc, SpriteRenderer sr, char c, out float charWidth, out float charHeight) {
    charWidth = 0;
    charHeight = 0;
    if (fc == null || sr == null) return false;

    fc.CancelInvoke("UpdateSprite");
    fc.font = font;
    fc.character = c;
    fc.UpdateSprite();

    var sprite = sr.sprite;
    if (sprite == null) return false;

    var scale = sr.transform.localScale;
    charWidth = Mathf.Abs(sprite.bounds.size.x * scale.x);
    charHeight = Mathf.Abs(sprite.bounds.size.y * scale.y);
    return true;
  }

  float MeasureNextWordWidth(int startIndex) {
    if (startIndex < 0 || startIndex >= content.Length) {
      return 0f;
    }

    if (content[startIndex] == ' ' || content[startIndex] == '\n') {
      return 0f;
    }

    return MeasureWordWidth(startIndex, out _);
  }

  float MeasureWordWidth(int startIndex, out int endIndex) {
    endIndex = startIndex;
    if (startIndex < 0 || startIndex >= content.Length) {
      return 0f;
    }

    if (content[startIndex] == ' ' || content[startIndex] == '\n') {
      return 0f;
    }

    var measureObject = GetCharFromPool();
    measureObject.SetActive(false);

    var fc = measureObject.GetComponent<FontCharacter>();
    var sr = measureObject.GetComponent<SpriteRenderer>();
    SyncGlyphRendererState(sr);

    var simulatedWidth = 0f;
    var displayWidth = 0f;
    while (endIndex < content.Length && content[endIndex] != ' ' && content[endIndex] != '\n') {
      if (TryGetCharacterMetrics(fc, sr, content[endIndex], out var charWidth, out _)) {
        var advanceWidth = mono > 0 ? Mathf.Max(mono, charWidth) : charWidth;
        var rightEdge = simulatedWidth + advanceWidth;
        simulatedWidth = rightEdge + padding;
        displayWidth = rightEdge;
      } else {
        simulatedWidth += spaceWidth;
        displayWidth = simulatedWidth;
      }
      endIndex++;
    }

    RecycleChar(measureObject);
    return displayWidth;
  }

  bool ShouldWrapWord(float wordWidth) {
    return maxWidth > 0f &&
      wordWidth > 0f &&
      lineCharCounts[line - 1] > 0 &&
      width + wordWidth > maxWidth;
  }

  void EmitCharacter(int contentIndex, char c) {
    var obj = GetCharFromPool();
    var fc = obj.GetComponent<FontCharacter>();
    var sr = obj.GetComponent<SpriteRenderer>();
    SyncGlyphRendererState(sr);
    if (!TryGetCharacterMetrics(fc, sr, c, out var charWidth, out var charHeight)) {
      AddSpaceWidth(spaceWidth);
      RecycleChar(obj);
      return;
    }

    var advanceWidth = mono > 0 ? Mathf.Max(mono, charWidth) : charWidth;
    var rightEdge = width + advanceWidth;
    var x = width + advanceWidth * 0.5f + offsetX;
    obj.transform.localPosition = new Vector3(x, 0, 0);

    if (tallest < charHeight + marginY) tallest = charHeight + marginY;
    if (lineHeights[line - 1] < charHeight) lineHeights[line - 1] = charHeight;

    activeChars.Add(obj);
    activeCharRenderers.Add(sr);
    activeCharSourceIndices.Add(contentIndex);
    lineCharCounts[line - 1]++;

    width = rightEdge + padding;
    totalWidths[line - 1] = rightEdge;
    if (actualWidth < rightEdge) actualWidth = rightEdge;
  }

  void AddSpaceWidth(float additionalWidth) {
    if (additionalWidth <= 0f) {
      return;
    }

    if (maxWidth > 0f && lineCharCounts[line - 1] > 0 && width + additionalWidth > maxWidth) {
      NextLine();
    }

    width += additionalWidth;
    totalWidths[line - 1] = width;
    if (actualWidth < width) actualWidth = width;
  }

  void CacheRuntimeReferences() {
    if (cachedHostRenderer == null) {
      cachedHostRenderer = GetComponent<SpriteRenderer>();
    }

    if (cachedComponentPropagator == null) {
      cachedComponentPropagator = GetComponent<ComponentPropagator>();
    }
  }

  void SyncGlyphRendererState(SpriteRenderer glyphRenderer) {
    if (glyphRenderer == null) return;

    CacheRuntimeReferences();
    if (cachedHostRenderer == null) return;

    // Preserve the glyph prefab's own material and animator-driven shader state.
    glyphRenderer.sortingLayerID = cachedHostRenderer.sortingLayerID;
    glyphRenderer.sortingOrder = cachedHostRenderer.sortingOrder;
    glyphRenderer.maskInteraction = cachedHostRenderer.maskInteraction;
    glyphRenderer.flipX = cachedHostRenderer.flipX;
    glyphRenderer.flipY = cachedHostRenderer.flipY;
  }

  void ForceGlyphPropagation() {
    CacheRuntimeReferences();
    if (cachedComponentPropagator == null) return;
    cachedComponentPropagator.ForcePropagation();
  }

  void SyncActiveGlyphRendererStates() {
    for (var i = 0; i < activeCharRenderers.Count; i++) {
      SyncGlyphRendererState(activeCharRenderers[i]);
    }
  }

  public void SetVisibleCharacterCount(int count) {
    visibleContentCharacterCount = count;
    ApplyVisibleCharacterCountToGlyphs();
  }

  void ApplyVisibleCharacterCountToGlyphs() {
    var revealAll = visibleContentCharacterCount < 0;
    for (var i = 0; i < activeCharRenderers.Count; i++) {
      var glyphRenderer = activeCharRenderers[i];
      if (glyphRenderer == null) continue;
      var shouldBeVisible = revealAll ||
        (i < activeCharSourceIndices.Count && activeCharSourceIndices[i] < visibleContentCharacterCount);
      if (glyphRenderer.enabled != shouldBeVisible) {
        glyphRenderer.enabled = shouldBeVisible;
      }
    }
  }

  void DoAlign() {
    var currentLine = 0;
    var lineAnchor = ResolveInitialLineAnchor();
    var charsProcessedInLine = 0;

    for (var i = 0; i < activeChars.Count; i++) {
      SkipEmptyLines(ref currentLine, ref lineAnchor);
      var obj = activeChars[i];
      var pos = obj.transform.localPosition;
      var x = pos.x;
      var y = ResolveLineCenterY(currentLine, lineAnchor);

      if (justifyX == "center") x -= totalWidths[currentLine] / 2;
      if (justifyX == "right") x -= totalWidths[currentLine];

      obj.transform.localPosition = new Vector3(x, y + offsetY, 0);

      charsProcessedInLine++;
      if (currentLine < lineCharCounts.Count && charsProcessedInLine >= lineCharCounts[currentLine]) {
        lineAnchor = AdvanceLineAnchor(lineAnchor, currentLine);
        currentLine++;
        charsProcessedInLine = 0;
      }
    }
  }

  void SkipEmptyLines(ref int currentLine, ref float lineAnchor) {
    while (currentLine < lineCharCounts.Count && lineCharCounts[currentLine] == 0) {
      lineAnchor = AdvanceLineAnchor(lineAnchor, currentLine);
      currentLine++;
    }
  }

  float ResolveInitialLineAnchor() {
    return IsLineDirectionUp() ? ResolveBlockBottomEdgeY() : ResolveBlockTopEdgeY();
  }

  float ResolveLineCenterY(int lineIndex, float lineAnchor) {
    if (lineIndex < 0 || lineIndex >= lineHeights.Count) {
      return 0f;
    }

    if (IsLineDirectionUp()) {
      return lineAnchor + lineHeights[lineIndex] * 0.5f;
    }

    return lineAnchor - lineHeights[lineIndex] * 0.5f;
  }

  float AdvanceLineAnchor(float currentAnchor, int currentLine) {
    if (currentLine < 0 || currentLine >= lineHeights.Count) {
      return currentAnchor;
    }

    if (IsLineDirectionUp()) {
      return currentAnchor + lineHeights[currentLine];
    }

    return currentAnchor - lineHeights[currentLine];
  }

  float ResolveBlockTopEdgeY() {
    if (string.Equals(justifyY, "center", StringComparison.OrdinalIgnoreCase)) {
      return actualHeight * 0.5f;
    }

    if (string.Equals(justifyY, "bottom", StringComparison.OrdinalIgnoreCase)) {
      return actualHeight;
    }

    return 0f;
  }

  float ResolveBlockBottomEdgeY() {
    if (string.Equals(justifyY, "center", StringComparison.OrdinalIgnoreCase)) {
      return -actualHeight * 0.5f;
    }

    if (string.Equals(justifyY, "top", StringComparison.OrdinalIgnoreCase)) {
      return -actualHeight;
    }

    return 0f;
  }

  bool IsLineDirectionUp() {
    return string.Equals(ResolveLineDirection(), "up", StringComparison.OrdinalIgnoreCase);
  }

  string ResolveLineDirection() {
    if (string.Equals(lineDirection, "up", StringComparison.OrdinalIgnoreCase)) {
      return "up";
    }

    if (string.Equals(lineDirection, "down", StringComparison.OrdinalIgnoreCase)) {
      return "down";
    }

    return string.Equals(justifyY, "bottom", StringComparison.OrdinalIgnoreCase) ? "up" : "down";
  }

  void NextLine() {
    height -= tallest;
    var currentLineIndex = line - 1;
    if (currentLineIndex >= 0 && currentLineIndex < totalWidths.Count && actualWidth < totalWidths[currentLineIndex]) {
      actualWidth = totalWidths[currentLineIndex];
    }
    line += 1;
    totalWidths.Add(0);
    lineHeights.Add(0);
    lineCharCounts.Add(0);
    width = 0;
    tallest = 0;
  }

  public void Reset() {
    foreach (var obj in activeChars) DestroyImmediate(obj);
    activeChars.Clear();
    activeCharRenderers.Clear();
    activeCharSourceIndices.Clear();
    charPool.Clear();

    gameObjectScratch.Clear();
    for (int i = 0; i < transform.childCount; i++) {
      var t = transform.GetChild(i).gameObject;
      if (!activeChars.Contains(t) && !charPool.Contains(t)) {
        gameObjectScratch.Add(t);
      }
    }
    for (var i = 0; i < gameObjectScratch.Count; i++) {
      DestroyImmediate(gameObjectScratch[i]);
    }
    gameObjectScratch.Clear();

    content = "";
    prevContent = content;
    visibleContentCharacterCount = -1;
  }
}
