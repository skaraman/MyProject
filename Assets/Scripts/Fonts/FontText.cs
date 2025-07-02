using UnityEngine;
using System.Collections.Generic;

public class FontText : MonoBehaviour {
  public GameObject characterPrefab;
  public string font = "Hand";
  public string content = "hello world";
  public float size = 1;
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
  private List<RectData> rects = new();
  private List<float> totalWidths = new();
  private Dictionary<char, float> charWidthCache = new();
  private Dictionary<char, string> charLabelCache = new();

  private int line = 1;
  private float width = 0;
  private float height = 0;
  private float actualWidth = 0;
  private float actualHeight = 0;
  private float tallest = 0;

  public struct RectData {
    public GameObject obj;
    public int line;
    public float x, y, w, h;
  }

  [ForceUpdate]
  public void Generate() {
    Clear();
    width = 0;
    height = 0;
    line = 1;
    totalWidths.Add(0);
    actualWidth = 0;
    actualHeight = 0;
    tallest = 0;

    for (int i = 0; i < content.Length; i++) {
      var c = content[i];

      if (c == ' ') {
        float space = mono > 0 ? mono : Mathf.Max(10, size * 2);
        if (maxWidth > 0 && width + space > maxWidth) NextLine();
        width += space;
        totalWidths[line - 1] = width;
        continue;
      }

      if (c == '\n') {
        NextLine();
        continue;
      }

      var obj = GetCharFromPool();
      var fc = obj.GetComponent<FontCharacter>();
      fc.font = font;
      fc.character = c;
      fc.Invoke("UpdateSprite", 0);

      var sr = obj.GetComponent<SpriteRenderer>();
      var sw = GetCharWidth(c, sr);
      var sh = sr.sprite.bounds.size.y * obj.transform.localScale.y;

      var cwr = sw * size * 0.1f;
      var chr = sh * size * 0.1f;

      if (maxWidth > 0 && width + cwr + padding > maxWidth) NextLine();

      var x = width + cwr / 2 + offsetX;
      var y = height + chr / 2 + offsetY;

      obj.transform.localPosition = new Vector3(x, y, 0);
      var w = cwr + marginX;
      var h = chr + marginY;

      if (tallest < h) tallest = h;

      rects.Add(new RectData {
        obj = obj,
        line = line,
        x = x,
        y = y,
        w = w,
        h = h
      });

      activeChars.Add(obj);
      if (mono > 0) width += mono / 2;
      else width += cwr + padding;

      totalWidths[line - 1] = width;
      if (actualWidth < width) actualWidth = width;
    }

    actualHeight += tallest;
    DoAlign();
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
    rects.Clear();
    totalWidths.Clear();
  }

  float GetCharWidth(char c, SpriteRenderer sr) {
    if (charWidthCache.TryGetValue(c, out var cached)) return cached;
    var w = sr.sprite.bounds.size.x * sr.transform.localScale.x;
    charWidthCache[c] = w;
    return w;
  }

  void DoAlign() {
    foreach (var r in rects) {
      var x = r.x;
      var y = r.y;

      if (justifyX == "center") x -= totalWidths[r.line - 1] / 2;
      if (justifyX == "right") x -= totalWidths[r.line - 1];

      if (justifyY == "center") y -= actualHeight / 2;
      if (justifyY == "top") y -= actualHeight;

      r.obj.transform.localPosition = new Vector3(x, y, 0);
    }
  }

  void NextLine() {
    height -= tallest;
    line += 1;
    totalWidths.Add(0);
    actualHeight += tallest;
    if (actualWidth < width) actualWidth = width;
    width = 0;
    tallest = 0;
  }
}
