using System;
using System.Collections.Generic;
using CustomInspector;
using UnityEngine;

public static class IntegerTextCache {
  const int CachedValueLimit = 4096;
  static readonly string[] values = new string[CachedValueLimit + 1];
  static readonly string[] slashPrefixedValues = new string[CachedValueLimit + 1];
  static int highestWarmedValue = -1;

  public static void Warm(int maxInclusive) {
    var warmLimit = Mathf.Clamp(maxInclusive, 0, CachedValueLimit);
    if (warmLimit <= highestWarmedValue) return;

    for (var value = highestWarmedValue + 1; value <= warmLimit; value++) {
      if (values[value] == null) {
        values[value] = value.ToString();
      }
    }
    highestWarmedValue = warmLimit;
  }

  public static string Get(int value) {
    if (value <= -10000 || value >= 10000) {
      return EndlessNumber.FormatGlyphs(value);
    }
    if (value < 0 || value > CachedValueLimit) {
      return value.ToString();
    }

    var cachedValue = values[value];
    if (cachedValue != null) {
      return cachedValue;
    }

    cachedValue = value.ToString();
    values[value] = cachedValue;
    return cachedValue;
  }

  public static string GetSlashPrefixed(int value) {
    if (value <= -10000 || value >= 10000) {
      return "/" + EndlessNumber.FormatGlyphs(value);
    }
    if (value < 0 || value > CachedValueLimit) {
      return "/" + value;
    }

    var cachedValue = slashPrefixedValues[value];
    if (cachedValue != null) {
      return cachedValue;
    }

    cachedValue = "/" + Get(value);
    slashPrefixedValues[value] = cachedValue;
    return cachedValue;
  }
}

public class FontText : MonoBehaviour {
  const string MainColorProperty = "_Color";
  const string OutlineColorProperty = "_OutlineColor";

  readonly struct GlyphMetricCacheKey : IEquatable<GlyphMetricCacheKey> {
    public readonly string font;
    public readonly char character;

    public GlyphMetricCacheKey(string font, char character) {
      this.font = font ?? "";
      this.character = character;
    }

    public bool Equals(GlyphMetricCacheKey other) {
      return character == other.character &&
             string.Equals(font, other.font, StringComparison.Ordinal);
    }

    public override bool Equals(object obj) {
      return obj is GlyphMetricCacheKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        return (StringComparer.Ordinal.GetHashCode(font ?? "") * 397) ^ character.GetHashCode();
      }
    }
  }

  static readonly Dictionary<GlyphMetricCacheKey, Vector2> glyphMetricsByFont = new();
  static readonly Dictionary<string, float> glyphHeightByFont = new(StringComparer.Ordinal);

  public GameObject characterPrefab;
  [SerializeField, Min(0)] int prewarmCharacterCapacity;
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
  [SerializeField] Material glyphMaterialOverride;
  [FixedValues("left", "center", "right")] public string justifyX = "left";
  [FixedValues("bottom", "center", "top")] public string justifyY = "bottom";
  [FixedValues("auto", "up", "down")] public string lineDirection = "auto";

  private List<GameObject> activeChars = new();
  private HashSet<GameObject> activeCharsSet = new();
  private List<SpriteRenderer> activeCharRenderers = new();
  private List<FontCharacter> activeCharCharacters = new();
  private List<int> activeCharSourceIndices = new();
  private Stack<GameObject> charPool = new();
  private HashSet<GameObject> charPoolSet = new();
  private List<float> totalWidths = new();
  private List<float> lineHeights = new();
  private List<int> lineCharCounts = new(); // Track chars per line
  private readonly List<Transform> childScratch = new();
  private readonly List<GameObject> gameObjectScratch = new();
  private readonly List<AllIn1AnimatorInspector> shaderAnimatorScratch = new();
  private bool hasShaderColors;
  private Color shaderMainColor = Color.white;
  private Color shaderOutlineColor = Color.black;

  private bool _checkedPrefabHasFontCharacter;
  private bool _prefabHasFontCharacter;
  private bool _checkedPrefabHasSpriteWithNormals;
  private bool _prefabHasSpriteWithNormals;

  bool PrefabHasFontCharacter() {
    if (!_checkedPrefabHasFontCharacter) {
      _checkedPrefabHasFontCharacter = true;
      _prefabHasFontCharacter = characterPrefab != null && characterPrefab.GetComponent<FontCharacter>() != null;
    }
    return _prefabHasFontCharacter;
  }

  bool PrefabHasSpriteWithNormals() {
    if (!_checkedPrefabHasSpriteWithNormals) {
      _checkedPrefabHasSpriteWithNormals = true;
      _prefabHasSpriteWithNormals =
        characterPrefab != null && characterPrefab.GetComponent<SpriteWithNormals>() != null;
    }
    return _prefabHasSpriteWithNormals;
  }

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
  private bool pendingRegenerate;
  private bool isGenerating;
  private bool glyphHierarchyChanged;
  private bool existingGlyphChildrenAdopted;

  void OnEnable() {
    if (characterPrefab == null) return;
    var adoptedExistingGlyphs = AdoptExistingGlyphChildren();
    EnsureGlyphCapacity(prewarmCharacterCapacity);
    // Authored glyphs already existed in the hierarchy, so capacity warmup did
    // not mark them as new. Propagate once after Generate reactivates them.
    if (adoptedExistingGlyphs) {
      glyphHierarchyChanged = true;
    }
    ApplyShaderColorsToHierarchy();
    Generate();
  }

  bool AdoptExistingGlyphChildren() {
    if (existingGlyphChildrenAdopted) return false;
    existingGlyphChildrenAdopted = true;

    var adoptedAnyGlyph = false;
    var requiresFontCharacter = PrefabHasFontCharacter();
    var requiresSpriteWithNormals = PrefabHasSpriteWithNormals();
    for (var i = 0; i < transform.childCount; i++) {
      var child = transform.GetChild(i);
      if (child == null) continue;

      var childObject = child.gameObject;
      if (activeCharsSet.Contains(childObject) || charPoolSet.Contains(childObject)) {
        continue;
      }

      var isCompatibleGlyph = requiresFontCharacter
        ? childObject.GetComponent<FontCharacter>() != null
        : childObject.GetComponent<SpriteRenderer>() != null;
      if (isCompatibleGlyph &&
          requiresSpriteWithNormals &&
          childObject.GetComponent<SpriteWithNormals>() == null) {
        isCompatibleGlyph = false;
      }
      if (!isCompatibleGlyph) continue;

      childObject.SetActive(false);
      charPool.Push(childObject);
      charPoolSet.Add(childObject);
      adoptedAnyGlyph = true;
    }
    return adoptedAnyGlyph;
  }

  public void EnsureGlyphCapacity(int requiredCapacity) {
    if (characterPrefab == null) {
      return;
    }

    requiredCapacity = Mathf.Max(requiredCapacity, 0);
    var currentCapacity = activeChars.Count + charPool.Count;
    while (currentCapacity < requiredCapacity) {
      var characterObject = Instantiate(characterPrefab, transform, false);
      characterObject.SetActive(false);
      charPool.Push(characterObject);
      charPoolSet.Add(characterObject);
      glyphHierarchyChanged = true;
      currentCapacity += 1;
    }

    if (activeChars.Capacity < requiredCapacity) {
      activeChars.Capacity = requiredCapacity;
      activeCharRenderers.Capacity = requiredCapacity;
      activeCharCharacters.Capacity = requiredCapacity;
      activeCharSourceIndices.Capacity = requiredCapacity;
    }
    PropagateGlyphHierarchyIfChanged();
  }

  void Update() {
    if (pendingRegenerate || content != prevContent) {
      pendingRegenerate = false;
      prevContent = content;
      Generate();
    }
  }

  [ForceUpdate]
  public void Generate() {
    if (characterPrefab == null) return;
    if (isGenerating) {
      pendingRegenerate = true;
      return;
    }

    isGenerating = true;
    try {
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
    PropagateGlyphHierarchyIfChanged();
    }
    finally {
      isGenerating = false;
    }
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
    GameObject obj;
    if (charPool.Count > 0) {
      obj = charPool.Pop();
      charPoolSet.Remove(obj);
    }
    else {
      obj = Instantiate(characterPrefab);
      glyphHierarchyChanged = true;
    }
    obj.transform.SetParent(transform, false);
    obj.transform.SetAsLastSibling();
    obj.SetActive(true);
    var glyphRenderer = obj.GetComponent<SpriteRenderer>();
    if (glyphMaterialOverride != null && glyphRenderer != null && glyphRenderer.sharedMaterial != glyphMaterialOverride) {
      glyphRenderer.sharedMaterial = glyphMaterialOverride;
    }
    ApplyShaderColors(obj.GetComponent<AllIn1AnimatorInspector>());
    activeCharsSet.Add(obj);
    return obj;
  }

  public void SetShaderColors(Color mainColor, Color outlineColor) {
    shaderMainColor = mainColor;
    shaderOutlineColor = outlineColor;
    hasShaderColors = true;

    if (!isActiveAndEnabled || !gameObject.activeInHierarchy) {
      return;
    }

    ApplyShaderColorsToHierarchy();
  }

  void ApplyShaderColorsToHierarchy() {
    if (!hasShaderColors) {
      return;
    }

    shaderAnimatorScratch.Clear();
    GetComponentsInChildren(true, shaderAnimatorScratch);
    for (var i = 0; i < shaderAnimatorScratch.Count; i++) {
      ApplyShaderColors(shaderAnimatorScratch[i]);
    }
    shaderAnimatorScratch.Clear();
  }

  void ApplyShaderColors(AllIn1AnimatorInspector animator) {
    if (!hasShaderColors || animator == null) {
      return;
    }

    animator.AddColorSequence(
      MainColorProperty,
      shaderMainColor,
      shaderMainColor,
      1f,
      replaceExisting: true
    );
    animator.AddColorSequence(
      OutlineColorProperty,
      shaderOutlineColor,
      shaderOutlineColor,
      1f,
      replaceExisting: true
    );
    animator.Refresh();
  }

  void RecycleChar(GameObject obj) {
    if (obj == null) return;
    activeCharsSet.Remove(obj);
    obj.SetActive(false);
    charPool.Push(obj);
    charPoolSet.Add(obj);
  }

  void Clear() {
    foreach (var obj in activeChars) {
      obj.SetActive(false);
      charPool.Push(obj);
      charPoolSet.Add(obj);
    }
    activeChars.Clear();
    activeCharsSet.Clear();
    activeCharRenderers.Clear();
    activeCharCharacters.Clear();
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
      if (!activeCharsSet.Contains(go) && !charPoolSet.Contains(go)) {
        DestroyImmediate(go);
        glyphHierarchyChanged = true;
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

    if (!fc.IsReadyAndMatches(c, font)) {
      return TryGetCachedGlyphMetrics(c, out charWidth, out charHeight);
    }

    var sprite = sr.sprite;
    if (sprite == null) {
      return TryGetCachedGlyphMetrics(c, out charWidth, out charHeight);
    }

    var scale = sr.transform.localScale;
    charWidth = Mathf.Abs(sprite.bounds.size.x * scale.x);
    charHeight = Mathf.Abs(sprite.bounds.size.y * scale.y);
    CacheGlyphMetrics(c, charWidth, charHeight);
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

    GameObject measureObject = null;
    FontCharacter fc = null;
    SpriteRenderer sr = null;

    var simulatedWidth = 0f;
    var displayWidth = 0f;
    while (endIndex < content.Length && content[endIndex] != ' ' && content[endIndex] != '\n') {
      var character = content[endIndex];
      var hasMetrics = TryGetCachedGlyphMetrics(character, out var charWidth, out _);
      if (!hasMetrics) {
        if (measureObject == null) {
          measureObject = GetCharFromPool();
          measureObject.SetActive(false);
          fc = PrefabHasFontCharacter() ? measureObject.GetComponent<FontCharacter>() : null;
          sr = measureObject.GetComponent<SpriteRenderer>();
          SyncGlyphRendererState(sr);
        }
        hasMetrics = TryGetCharacterMetrics(fc, sr, character, out charWidth, out _);
      }
      if (hasMetrics) {
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

    if (measureObject != null) {
      RecycleChar(measureObject);
    }
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
    var fc = PrefabHasFontCharacter() ? obj.GetComponent<FontCharacter>() : null;
    var sr = obj.GetComponent<SpriteRenderer>();
    SyncGlyphRendererState(sr);
    if (!TryGetCharacterMetrics(fc, sr, c, out var charWidth, out var charHeight)) {
      charWidth = ResolveFallbackGlyphWidth();
      charHeight = ResolveFallbackGlyphHeight();
    }

    var advanceWidth = mono > 0 ? Mathf.Max(mono, charWidth) : charWidth;
    var rightEdge = width + advanceWidth;
    var x = width + advanceWidth * 0.5f + offsetX;
    obj.transform.localPosition = new Vector3(x, 0, 0);

    if (tallest < charHeight + marginY) tallest = charHeight + marginY;
    if (lineHeights[line - 1] < charHeight) lineHeights[line - 1] = charHeight;

    activeChars.Add(obj);
    activeCharRenderers.Add(sr);
    activeCharCharacters.Add(fc);
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

  private bool searchedHostRenderer;
  private bool searchedComponentPropagator;

  void CacheRuntimeReferences() {
    if (!searchedHostRenderer) {
      searchedHostRenderer = true;
      cachedHostRenderer = GetComponent<SpriteRenderer>();
    }

    if (!searchedComponentPropagator) {
      searchedComponentPropagator = true;
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

  void PropagateGlyphHierarchyIfChanged() {
    if (!glyphHierarchyChanged) return;

    CacheRuntimeReferences();
    if (cachedComponentPropagator == null) {
      glyphHierarchyChanged = false;
      return;
    }
    cachedComponentPropagator.ForcePropagation();
    glyphHierarchyChanged = false;
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

  public void RefreshGlyphVisibility() {
    ApplyVisibleCharacterCountToGlyphs();
  }

  public void NotifyGlyphMetricsReady() {
    if (!isGenerating) {
      pendingRegenerate = true;
    }
    ApplyVisibleCharacterCountToGlyphs();
  }

  void ApplyVisibleCharacterCountToGlyphs() {
    var revealAll = visibleContentCharacterCount < 0;
    for (var i = 0; i < activeCharRenderers.Count; i++) {
      var glyphRenderer = activeCharRenderers[i];
      if (glyphRenderer == null) continue;
      var glyphCharacter = i < activeCharCharacters.Count ? activeCharCharacters[i] : null;
      var canRenderGlyph = glyphCharacter == null || glyphCharacter.CanRenderCurrentGlyph;
      var shouldBeVisible = (revealAll ||
        (i < activeCharSourceIndices.Count && activeCharSourceIndices[i] < visibleContentCharacterCount)) &&
        canRenderGlyph;
      if (glyphCharacter != null) {
        glyphCharacter.SetVisibility(shouldBeVisible);
      } else {
        if (glyphRenderer.enabled != shouldBeVisible) {
          glyphRenderer.enabled = shouldBeVisible;
        }
      }
    }
  }

  void CacheGlyphMetrics(char character, float charWidth, float charHeight) {
    if (charWidth > 0f) {
      glyphMetricsByFont[new GlyphMetricCacheKey(font, character)] = new Vector2(charWidth, charHeight);
    }

    if (charHeight > 0f) {
      glyphHeightByFont[font ?? ""] = Mathf.Max(ResolveFallbackGlyphHeight(), charHeight);
    }
  }

  bool TryGetCachedGlyphMetrics(char character, out float charWidth, out float charHeight) {
    if (glyphMetricsByFont.TryGetValue(new GlyphMetricCacheKey(font, character), out var metrics)) {
      charWidth = metrics.x;
      charHeight = metrics.y;
      return charWidth > 0f;
    }

    charWidth = 0f;
    charHeight = 0f;
    return false;
  }

  float ResolveFallbackGlyphWidth() {
    if (mono > 0f) {
      return mono;
    }

    return Mathf.Max(spaceWidth, 0.5f);
  }

  float ResolveFallbackGlyphHeight() {
    if (glyphHeightByFont.TryGetValue(font ?? "", out var cachedHeight) && cachedHeight > 0f) {
      return cachedHeight;
    }

    return 0f;
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
    foreach (var obj in charPool) DestroyImmediate(obj);
    if (activeChars.Count > 0 || charPool.Count > 0) {
      glyphHierarchyChanged = true;
    }
    activeChars.Clear();
    activeCharsSet.Clear();
    activeCharRenderers.Clear();
    activeCharCharacters.Clear();
    activeCharSourceIndices.Clear();
    charPool.Clear();
    charPoolSet.Clear();
    existingGlyphChildrenAdopted = false;

    gameObjectScratch.Clear();
    for (int i = 0; i < transform.childCount; i++) {
      var t = transform.GetChild(i).gameObject;
      if (!activeCharsSet.Contains(t) && !charPoolSet.Contains(t)) {
        gameObjectScratch.Add(t);
        glyphHierarchyChanged = true;
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
