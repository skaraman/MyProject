using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class FontCharacter : MonoBehaviour {
  const string DefaultFontLibraryName = "UI/Fonts";
  const string DamageTierLibraryName = EndlessNumberSuffixMap.SpriteLibraryName;
  const string DamageTierCategory = EndlessNumberSuffixMap.SpriteCategory;

  struct GlyphRendererState {
    public bool enabled;
    public int sortingLayerId;
    public int sortingOrder;
    public SpriteMaskInteraction maskInteraction;
    public bool flipX;
    public bool flipY;
  }

  static readonly Dictionary<char, string> cacheBank = new Dictionary<char, string> { // char.ToString() is too expensive, this is faster
    // Lowercase letters a-z
    {'a', "a"}, {'b', "b"}, {'c', "c"}, {'d', "d"}, {'e', "e"}, {'f', "f"}, {'g', "g"}, {'h', "h"},
    {'i', "i"}, {'j', "j"}, {'k', "k"}, {'l', "l"}, {'m', "m"}, {'n', "n"}, {'o', "o"}, {'p', "p"},
    {'q', "q"}, {'r', "r"}, {'s', "s"}, {'t', "t"}, {'u', "u"}, {'v', "v"}, {'w', "w"}, {'x', "x"},
    {'y', "y"}, {'z', "z"},

    // Uppercase letters A-Z
    {'A', "A"}, {'B', "B"}, {'C', "C"}, {'D', "D"}, {'E', "E"}, {'F', "F"}, {'G', "G"}, {'H', "H"},
    {'I', "I"}, {'J', "J"}, {'K', "K"}, {'L', "L"}, {'M', "M"}, {'N', "N"}, {'O', "O"}, {'P', "P"},
    {'Q', "Q"}, {'R', "R"}, {'S', "S"}, {'T', "T"}, {'U', "U"}, {'V', "V"}, {'W', "W"}, {'X', "X"},
    {'Y', "Y"}, {'Z', "Z"},

    // Numbers 0-9
    {'0', "0"}, {'1', "1"}, {'2', "2"}, {'3', "3"}, {'4', "4"}, {'5', "5"}, {'6', "6"}, {'7', "7"},
    {'8', "8"}, {'9', "9"},

    // Common punctuation and symbols
    {' ', " "}, {'!', "!"}, {'"', "\""}, {'#', "#"}, {'$', "$"}, {'%', "%"}, {'&', "&"}, {'\'', "'"},
    {'(', "("}, {')', ")"}, {'*', "*"}, {'+', "+"}, {',', ","}, {'-', "-"}, {'.', "."}, {'/', "/"},
    {':', ":"}, {';', ";"}, {'<', "<"}, {'=', "="}, {'>', ">"}, {'?', "?"}, {'@', "@"}, {'[', "["},
    {'\\', "\\"}, {']', "]"}, {'^', "^"}, {'_', "_"}, {'`', "`"}, {'{', "{"}, {'|', "|"}, {'}', "}"},
    {'~', "~"}
  };

  public SpriteResolver spriteResolver;
  SpriteWithNormals spriteWithNormals;
  SpriteRenderer spriteRenderer;
  FontText parentFontText;
  char? loadedCharacter;
  string loadedFont;

  public char character { set; get; } = 'T';
  public string font { set; get; } = "Hand";
  bool canRenderCurrentGlyph = true;
  bool waitingForGlyphReadyRetry;
  SpriteColdLoadState glyphLoadState = SpriteColdLoadState.Ready;
  public bool CanRenderCurrentGlyph => canRenderCurrentGlyph;

  public bool IsReadyAndMatches(char c, string f) {
    return loadedCharacter == c &&
           string.Equals(loadedFont, f, System.StringComparison.Ordinal) &&
           glyphLoadState.IsCommitReady() &&
           spriteRenderer != null &&
           spriteRenderer.sprite != null;
  }

  public void SetVisibility(bool visible) {
    CacheDependencies();
    if (spriteWithNormals != null) {
      spriteWithNormals.SetExternalVisualSuppressed(!visible);
    }
    if (spriteRenderer != null) {
      spriteRenderer.enabled = visible;
    }
  }

  void Reset() {
    CacheDependencies();
  }

  void OnDisable() {
    CancelInvoke(nameof(RetryUpdateSprite));
    waitingForGlyphReadyRetry = false;
    CacheDependencies();
    if (spriteWithNormals != null) {
      spriteWithNormals.SetExternalVisualSuppressed(false);
    }
  }

  [ForceUpdate]
  public void UpdateSprite() {
    CacheDependencies();
    if (IsReadyAndMatches(character, font)) {
      return;
    }
    if (!TryGetGlyphLabel(out var label)) {
      UpdateRenderReadiness(SpriteColdLoadState.Missing);
      return;
    }

    // If the glyph is changing, immediately clear the stale sprite so the old
    // character is never visible during async load of the new one.
    var glyphChanged = loadedCharacter != character ||
                       !string.Equals(loadedFont, font, System.StringComparison.Ordinal);
    if (glyphChanged && spriteRenderer != null) {
      spriteRenderer.sprite = null;
    }

    var rendererState = CaptureRendererState();
    ApplySpriteWithNormals(label);
    ApplySpriteResolver(label);
    RestoreRendererState(rendererState);
    UpdateRenderReadiness(ResolveGlyphColdLoadState());
  }

  bool TryGetGlyphLabel(out string label) {
    if (EndlessNumberSuffixMap.TryGetSpriteToken(character, out var spriteToken)) {
      label = EndlessNumberSuffixMap.GetSpriteLabel(spriteToken);
      return true;
    }
    if (cacheBank.TryGetValue(character, out label)) return true;
    label = null;
    Debug.LogWarning($"[FontCharacter] Missing glyph mapping for '{character}' on {gameObject.name}");
    return false;
  }

  void CacheDependencies() {
    if (spriteResolver == null) {
      spriteResolver = GetComponent<SpriteResolver>();
    }

    if (spriteWithNormals == null) {
      spriteWithNormals = GetComponent<SpriteWithNormals>();
    }

    if (spriteRenderer == null) {
      spriteRenderer = GetComponent<SpriteRenderer>();
    }

    if (parentFontText == null) {
      parentFontText = GetComponentInParent<FontText>();
    }

  }

  GlyphRendererState CaptureRendererState() {
    if (spriteRenderer == null) {
      return default;
    }

    return new GlyphRendererState {
      enabled = spriteRenderer.enabled,
      sortingLayerId = spriteRenderer.sortingLayerID,
      sortingOrder = spriteRenderer.sortingOrder,
      maskInteraction = spriteRenderer.maskInteraction,
      flipX = spriteRenderer.flipX,
      flipY = spriteRenderer.flipY
    };
  }

  void RestoreRendererState(GlyphRendererState state) {
    if (spriteRenderer == null) return;

    spriteRenderer.enabled = state.enabled;
    spriteRenderer.sortingLayerID = state.sortingLayerId;
    spriteRenderer.sortingOrder = state.sortingOrder;
    spriteRenderer.maskInteraction = state.maskInteraction;
    spriteRenderer.flipX = state.flipX;
    spriteRenderer.flipY = state.flipY;
  }

  void ApplySpriteWithNormals(string label) {
    if (spriteWithNormals == null) return;

    var isDamageTierGlyph = EndlessNumberSuffixMap.TryGetSpriteToken(character, out _);
    var targetLibraryName = isDamageTierGlyph ? DamageTierLibraryName : DefaultFontLibraryName;
    var targetCategory = isDamageTierGlyph ? DamageTierCategory : font;
    var needsRefresh = false;
    if (!string.Equals(spriteWithNormals.libraryName, targetLibraryName, System.StringComparison.Ordinal)) {
      spriteWithNormals.SetLibraryName(targetLibraryName);
      needsRefresh = true;
    }

    if (!string.Equals(spriteWithNormals.category, targetCategory, System.StringComparison.Ordinal)) {
      spriteWithNormals.SetAnimation(targetCategory);
      needsRefresh = true;
    }

    if (!string.Equals(spriteWithNormals.labelPrefix, label, System.StringComparison.Ordinal)) {
      spriteWithNormals.SetLabelPrefix(label);
      needsRefresh = true;
    }

    if (spriteWithNormals.IsAnimation) {
      spriteWithNormals.SetIsAnimation(false);
      needsRefresh = true;
    }

    if (spriteWithNormals.DoNotRender) {
      spriteWithNormals.SetDoNotRender(false);
      needsRefresh = true;
    }

    if (needsRefresh || (spriteRenderer != null && spriteRenderer.sprite == null)) {
      spriteWithNormals.ForceUpdateSpriteAndNormal(0);
    }
  }

  void ApplySpriteResolver(string label) {
    if (spriteResolver == null) return;
    if (EndlessNumberSuffixMap.TryGetSpriteToken(character, out _)) return;
    spriteResolver.SetCategoryAndLabel(font, label);
  }

  SpriteColdLoadState ResolveGlyphColdLoadState() {
    if (spriteRenderer == null || spriteRenderer.sprite == null) {
      return SpriteColdLoadState.Pending;
    }

    if (!Application.isPlaying || spriteWithNormals == null) {
      return SpriteColdLoadState.Ready;
    }

    return spriteWithNormals.GetFrameColdLoadState(0, out _);
  }

  void UpdateRenderReadiness(SpriteColdLoadState nextState) {
    var previousState = glyphLoadState;
    glyphLoadState = nextState;
    var glyphReady = nextState.IsCommitReady();

    var matchesLoaded = loadedCharacter == character && string.Equals(loadedFont, font, System.StringComparison.Ordinal);
    if (glyphReady) {
      loadedCharacter = character;
      loadedFont = font;
    } else if (!matchesLoaded) {
      loadedCharacter = null;
      loadedFont = null;
    }

    var canKeepVisibleWhilePending =
      nextState == SpriteColdLoadState.Pending &&
      spriteRenderer != null &&
      spriteRenderer.sprite != null &&
      loadedCharacter == character &&
      string.Equals(loadedFont, font, System.StringComparison.Ordinal);
    var nextCanRender = glyphReady || canKeepVisibleWhilePending;
    var readinessChanged = canRenderCurrentGlyph != nextCanRender;
    var glyphBecameReady = !previousState.IsCommitReady() && glyphReady;
    canRenderCurrentGlyph = nextCanRender;

    if (nextState == SpriteColdLoadState.Pending) {
      QueueGlyphReadyRetry();
    }
    else if (glyphReady) {
      CancelInvoke(nameof(RetryUpdateSprite));
      waitingForGlyphReadyRetry = false;
    }
    else {
      CancelInvoke(nameof(RetryUpdateSprite));
      waitingForGlyphReadyRetry = false;
    }

    if (!readinessChanged && !glyphBecameReady) {
      return;
    }

    if (parentFontText != null) {
      if (glyphBecameReady) {
        parentFontText.NotifyGlyphMetricsReady();
      }
      parentFontText.RefreshGlyphVisibility();
    }
  }

  void QueueGlyphReadyRetry() {
    if (!Application.isPlaying || !enabled || !gameObject.activeInHierarchy) {
      return;
    }
    if (waitingForGlyphReadyRetry) {
      return;
    }

    waitingForGlyphReadyRetry = true;
    Invoke(nameof(RetryUpdateSprite), 0.05f);
  }

  void RetryUpdateSprite() {
    waitingForGlyphReadyRetry = false;
    UpdateSprite();
  }
}

