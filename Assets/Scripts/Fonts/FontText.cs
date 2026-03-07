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
  public string justifyX = "left";
  public string justifyY = "bottom";

  private List<GameObject> activeChars = new();
  private Stack<GameObject> charPool = new();
  private List<float> totalWidths = new();
  private List<float> lineHeights = new();
  private List<int> lineCharCounts = new(); // Track chars per line

  private int line = 1;
  private float width = 0;
  private float height = 0;
  private float actualWidth = 0;
  private float actualHeight = 0;
  private float tallest = 0;
  private string prevContent = "";

  void Update() {
    if (content != prevContent) {
      prevContent = content;
      Generate();
    }
  }

  [ForceUpdate]
  public void Generate() {
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

    for (int i = 0; i < content.Length; i++) {
      var c = content[i];

      if (c == ' ') {
        if (maxWidth > 0 && lineCharCounts[line - 1] > 0 && width + spaceWidth > maxWidth) {
          NextLine();
        }
        width += spaceWidth;
        totalWidths[line - 1] = width;
        if (actualWidth < width) actualWidth = width;
        continue;
      }

      if (c == '\n') {
        NextLine();
        continue;
      }

      var obj = GetCharFromPool();
      var fc = obj.GetComponent<FontCharacter>();
      var sr = obj.GetComponent<SpriteRenderer>();
      if (!TryGetCharacterMetrics(fc, sr, c, out var charWidth, out var charHeight)) {
        if (maxWidth > 0 && lineCharCounts[line - 1] > 0 && width + spaceWidth > maxWidth) {
          NextLine();
        }
        width += spaceWidth;
        totalWidths[line - 1] = width;
        if (actualWidth < width) actualWidth = width;
        obj.SetActive(false);
        charPool.Push(obj);
        continue;
      }

      var advanceWidth = mono > 0 ? Mathf.Max(mono, charWidth) : charWidth;
      var rightEdge = width + advanceWidth;

      // Check if character fits on current line
      if (maxWidth > 0 && lineCharCounts[line - 1] > 0 && rightEdge > maxWidth) {
        NextLine();
        rightEdge = width + advanceWidth;
      }

      // Position character at current width position
      var x = width + advanceWidth * 0.5f + offsetX;
      obj.transform.localPosition = new Vector3(x, 0, 0);

      // Update line statistics
      if (tallest < charHeight + marginY) tallest = charHeight + marginY;
      if (lineHeights[line - 1] < charHeight) lineHeights[line - 1] = charHeight;

      activeChars.Add(obj);
      lineCharCounts[line - 1]++;

      // Advance pen position; line width excludes trailing padding.
      width = rightEdge + padding;
      totalWidths[line - 1] = rightEdge;
      if (actualWidth < rightEdge) actualWidth = rightEdge;
    }

    actualHeight = 0;
    for (int i = 0; i < lineHeights.Count; i++) actualHeight += lineHeights[i];
    DoAlign();
    // if (gameObject.GetComponent<ComponentPropagator>() != null) {
    //   gameObject.GetComponent<ComponentPropagator>().ForcePropagation();
    // }
  }

  GameObject GetCharFromPool() {
    var obj = charPool.Count > 0 ? charPool.Pop() : Instantiate(characterPrefab);
    obj.transform.SetParent(transform, false);
    obj.SetActive(true);
    return obj;
  }

  void Clear() {
    foreach (var obj in activeChars) {
      obj.SetActive(false);
      charPool.Push(obj);
    }
    activeChars.Clear();
    totalWidths.Clear();
    lineHeights.Clear();
    lineCharCounts.Clear();

    var allChildren = new List<Transform>();
    for (int i = 0; i < transform.childCount; i++) {
      var child = transform.GetChild(i);
      allChildren.Add(child);
    }

    foreach (var child in allChildren) {
      var go = child.gameObject;
      if (!activeChars.Contains(go) && !charPool.Contains(go)) {
        DestroyImmediate(go);
      }
    }
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

  void DoAlign() {
    int currentLine = 0;
    float yOffset = 0;
    int charsProcessedInLine = 0;

    for (int i = 0; i < activeChars.Count; i++) {
      var obj = activeChars[i];
      var pos = obj.transform.localPosition;
      float x = pos.x;
      float y = yOffset + lineHeights[currentLine] / 2;

      // Apply horizontal justification
      if (justifyX == "center") x -= totalWidths[currentLine] / 2;
      if (justifyX == "right") x -= totalWidths[currentLine];

      // Apply vertical justification
      if (justifyY == "center") y -= actualHeight / 2;
      if (justifyY == "top") y -= actualHeight;

      obj.transform.localPosition = new Vector3(x, y + offsetY, 0);

      charsProcessedInLine++;

      // Check if we've processed all characters in current line
      if (currentLine < lineCharCounts.Count && charsProcessedInLine >= lineCharCounts[currentLine]) {
        yOffset -= lineHeights[currentLine];
        currentLine++;
        charsProcessedInLine = 0;
      }
    }
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
    charPool.Clear();

    var extra = new List<GameObject>();
    for (int i = 0; i < transform.childCount; i++) {
      var t = transform.GetChild(i).gameObject;
      if (!activeChars.Contains(t) && !charPool.Contains(t)) {
        extra.Add(t);
      }
    }
    foreach (var e in extra) DestroyImmediate(e);

    content = "";
  }
}
