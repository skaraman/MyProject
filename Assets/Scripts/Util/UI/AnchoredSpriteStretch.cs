using CustomInspector;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class AnchoredSpriteStretch : MonoBehaviour {
  public enum HorizontalAnchor {
    Left,
    Center,
    Right
  }

  public enum VerticalAnchor {
    Bottom,
    Middle,
    Top
  }

  public Vector2 maxSizePixels = new(100f, 10f);
  public Vector2 stretchPercent = new(100f, 100f);
  public Vector4 borderPixels;
  public Vector2 originLocalPosition;
  public HorizontalAnchor anchorX = HorizontalAnchor.Left;
  public VerticalAnchor anchorY = VerticalAnchor.Middle;
  public bool enableDebugLogs;
  [Button(nameof(SetOriginFromCurrentTransform), label = "Set Origin", size = Size.small)][HideField] public bool setOriginButton;

  SpriteRenderer targetRenderer;
  Sprite sourceSprite;
  Sprite slicedSprite;
  Vector4 slicedSpriteBorderPixels;
  Vector3 lastResolvedPosition;
  bool hasResolvedPosition;
#if UNITY_EDITOR
  bool refreshQueuedInEditor;
#endif

  void Reset() {
    CacheRenderer();
    ClampInputs();
    originLocalPosition = ResolveCurrentOrigin();
    RefreshStretch();
  }

  void OnEnable() {
    RefreshStretch();
  }

  void OnValidate() {
    ClampInputs();
    QueueRefreshStretch();
  }

  void LateUpdate() {
    RefreshStretch();
  }

  [ForceUpdate]
  public void RefreshStretch() {
#if UNITY_EDITOR
    if (UnityEditor.BuildPipeline.isBuildingPlayer) return;
#endif
    ClampInputs();
    if (!TryGetRenderer(out var renderer)) return;

    var sprite = ResolveSourceSprite(renderer);
    if (sprite == null) return;

    var useNineSlice = HasBorderPixels();
    var targetSizePixels = ResolveTargetSizePixels(sprite, useNineSlice);
    var resolvedScale = new Vector3(transform.localScale.x, transform.localScale.y, transform.localScale.z);
    Vector3 resolvedPosition;

    if (useNineSlice) {
      if (!TryApplyNineSliceVisualState(renderer, sprite, targetSizePixels, out var targetSizeUnits)) return;
      resolvedScale.x = 1f;
      resolvedScale.y = 1f;
      var targetLocalPosition = ResolveNineSliceTargetLocalPosition(renderer, sprite, targetSizeUnits);
      resolvedPosition = new Vector3(targetLocalPosition.x, targetLocalPosition.y, transform.localPosition.z);
    } else {
      ApplySimpleVisualState(renderer, sprite);
      var targetScale = ResolveTargetScale(sprite, targetSizePixels);
      var targetLocalPosition = ResolveSimpleTargetLocalPosition(renderer, sprite, targetScale);
      resolvedScale.x = targetScale.x;
      resolvedScale.y = targetScale.y;
      resolvedPosition = new Vector3(targetLocalPosition.x, targetLocalPosition.y, transform.localPosition.z);
    }

    if (ShouldKeepManualEditorPosition(resolvedPosition)) {
      ApplyScaleOnly(resolvedScale);
      return;
    }

    if (AreClose(transform.localScale, resolvedScale) &&
        AreClose(transform.localPosition, resolvedPosition)) {
      CacheResolvedPosition(resolvedPosition);
      return;
    }

    ApplyTransform(resolvedScale, resolvedPosition);
    LogAppliedValues(sprite, targetSizePixels, resolvedScale, resolvedPosition);
  }

  void QueueRefreshStretch() {
#if UNITY_EDITOR
    if (Application.isPlaying) {
      RefreshStretch();
      return;
    }

    if (refreshQueuedInEditor) return;
    refreshQueuedInEditor = true;
    EditorApplication.delayCall += HandleDeferredEditorRefreshStretch;
#else
    RefreshStretch();
#endif
  }

#if UNITY_EDITOR
  void HandleDeferredEditorRefreshStretch() {
    refreshQueuedInEditor = false;
    if (this == null || gameObject == null) return;
    RefreshStretch();
  }
#endif

  public void SetOriginFromCurrentTransform() {
    ClampInputs();
    originLocalPosition = ResolveCurrentOrigin();
    RefreshStretch();
#if UNITY_EDITOR
    if (!Application.isPlaying) {
      UnityEditor.EditorUtility.SetDirty(this);
      UnityEditor.EditorUtility.SetDirty(transform);
      UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
      UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
    }
#endif
    if (!enableDebugLogs) return;
    RuntimeLog.Log(
      "[AnchoredSpriteStretch] Set origin from transform on " + gameObject.name +
      " origin=" + originLocalPosition +
      " local_pos=" + transform.localPosition
    );
  }

  void CacheRenderer() {
    if (targetRenderer == null) {
      targetRenderer = GetComponent<SpriteRenderer>();
    }
  }

  bool TryGetRenderer(out SpriteRenderer renderer) {
    CacheRenderer();
    renderer = targetRenderer;
    return renderer != null;
  }

  void ClampInputs() {
    maxSizePixels.x = Mathf.Max(0f, maxSizePixels.x);
    maxSizePixels.y = Mathf.Max(0f, maxSizePixels.y);
    stretchPercent.x = Mathf.Max(0f, stretchPercent.x);
    stretchPercent.y = Mathf.Max(0f, stretchPercent.y);
    borderPixels.x = Mathf.Max(0f, borderPixels.x);
    borderPixels.y = Mathf.Max(0f, borderPixels.y);
    borderPixels.z = Mathf.Max(0f, borderPixels.z);
    borderPixels.w = Mathf.Max(0f, borderPixels.w);
  }

  Vector2 ResolveCurrentOrigin() {
    var localPosition = transform.localPosition;
    if (!TryGetRenderer(out var renderer)) {
      return new Vector2(localPosition.x, localPosition.y);
    }

    var sprite = ResolveSourceSprite(renderer);
    if (sprite == null) {
      return new Vector2(localPosition.x, localPosition.y);
    }

    var useNineSlice = HasBorderPixels();
    var targetSizePixels = ResolveTargetSizePixels(sprite, useNineSlice);
    Vector2 anchorOffset;

    if (useNineSlice) {
      var targetSizeUnits = ResolveTargetSizeUnits(sprite, targetSizePixels);
      anchorOffset = ResolveNineSliceAnchorOffset(renderer, sprite, targetSizeUnits);
    } else {
      var targetScale = ResolveTargetScale(sprite, targetSizePixels);
      anchorOffset = ResolveSimpleAnchorOffset(renderer, sprite, targetScale);
    }

    return new Vector2(localPosition.x, localPosition.y) - anchorOffset;
  }

  Vector2 ResolveTargetSizePixels(Sprite sprite, bool useNineSlice) {
    var targetSizePixels = new Vector2(
      maxSizePixels.x * stretchPercent.x * 0.01f,
      maxSizePixels.y * stretchPercent.y * 0.01f
    );

    if (!useNineSlice || sprite == null) return targetSizePixels;

    var resolvedBorderPixels = ResolveRequestedBorderPixels(sprite);
    targetSizePixels.x = Mathf.Max(targetSizePixels.x, resolvedBorderPixels.x + resolvedBorderPixels.z);
    targetSizePixels.y = Mathf.Max(targetSizePixels.y, resolvedBorderPixels.y + resolvedBorderPixels.w);
    return targetSizePixels;
  }

  static Vector2 ResolveTargetScale(Sprite sprite, Vector2 targetSizePixels) {
    if (sprite == null) return Vector2.zero;

    var pixelsPerUnit = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 100f;
    var targetSizeUnits = targetSizePixels / pixelsPerUnit;
    var sourceSizeUnits = sprite.bounds.size;

    return new Vector2(
      ResolveAxisScale(sourceSizeUnits.x, targetSizeUnits.x),
      ResolveAxisScale(sourceSizeUnits.y, targetSizeUnits.y)
    );
  }

  static float ResolveAxisScale(float sourceSizeUnits, float targetSizeUnits) {
    if (Mathf.Abs(sourceSizeUnits) <= 0.0001f) return 0f;
    return targetSizeUnits / sourceSizeUnits;
  }

  Vector2 ResolveSimpleTargetLocalPosition(SpriteRenderer renderer, Sprite sprite, Vector2 targetScale) {
    return originLocalPosition + ResolveSimpleAnchorOffset(renderer, sprite, targetScale);
  }

  // Keep the selected anchor pinned at originLocalPosition regardless of pivot or flip.
  Vector2 ResolveSimpleAnchorOffset(SpriteRenderer renderer, Sprite sprite, Vector2 targetScale) {
    var spriteBounds = sprite.bounds;
    var scaledMin = new Vector2(
      ResolveScaledAxisMin(spriteBounds.min.x, spriteBounds.max.x, targetScale.x, renderer.flipX),
      ResolveScaledAxisMin(spriteBounds.min.y, spriteBounds.max.y, targetScale.y, renderer.flipY)
    );
    var scaledMax = new Vector2(
      ResolveScaledAxisMax(spriteBounds.min.x, spriteBounds.max.x, targetScale.x, renderer.flipX),
      ResolveScaledAxisMax(spriteBounds.min.y, spriteBounds.max.y, targetScale.y, renderer.flipY)
    );

    var anchorPoint = new Vector2(
      ResolveHorizontalAnchorPoint(scaledMin.x, scaledMax.x),
      ResolveVerticalAnchorPoint(scaledMin.y, scaledMax.y)
    );

    return -anchorPoint;
  }

  Vector2 ResolveNineSliceTargetLocalPosition(SpriteRenderer renderer, Sprite sprite, Vector2 targetSizeUnits) {
    return originLocalPosition + ResolveNineSliceAnchorOffset(renderer, sprite, targetSizeUnits);
  }

  Vector2 ResolveNineSliceAnchorOffset(SpriteRenderer renderer, Sprite sprite, Vector2 targetSizeUnits) {
    var pivotNormalized = ResolvePivotNormalized(sprite);
    var scaledMin = new Vector2(
      ResolveScaledAxisMin(-pivotNormalized.x, 1f - pivotNormalized.x, targetSizeUnits.x, renderer.flipX),
      ResolveScaledAxisMin(-pivotNormalized.y, 1f - pivotNormalized.y, targetSizeUnits.y, renderer.flipY)
    );
    var scaledMax = new Vector2(
      ResolveScaledAxisMax(-pivotNormalized.x, 1f - pivotNormalized.x, targetSizeUnits.x, renderer.flipX),
      ResolveScaledAxisMax(-pivotNormalized.y, 1f - pivotNormalized.y, targetSizeUnits.y, renderer.flipY)
    );

    var anchorPoint = new Vector2(
      ResolveHorizontalAnchorPoint(scaledMin.x, scaledMax.x),
      ResolveVerticalAnchorPoint(scaledMin.y, scaledMax.y)
    );

    return -anchorPoint;
  }

  static float ResolveScaledAxisMin(float axisMin, float axisMax, float scale, bool flipped) {
    return flipped ? -axisMax * scale : axisMin * scale;
  }

  static float ResolveScaledAxisMax(float axisMin, float axisMax, float scale, bool flipped) {
    return flipped ? -axisMin * scale : axisMax * scale;
  }

  float ResolveHorizontalAnchorPoint(float axisMin, float axisMax) {
    if (anchorX == HorizontalAnchor.Left) return axisMin;
    if (anchorX == HorizontalAnchor.Right) return axisMax;
    return (axisMin + axisMax) * 0.5f;
  }

  float ResolveVerticalAnchorPoint(float axisMin, float axisMax) {
    if (anchorY == VerticalAnchor.Bottom) return axisMin;
    if (anchorY == VerticalAnchor.Top) return axisMax;
    return (axisMin + axisMax) * 0.5f;
  }

  void ApplyTransform(Vector3 resolvedScale, Vector3 resolvedPosition) {
    transform.localScale = resolvedScale;
    transform.localPosition = resolvedPosition;
    CacheResolvedPosition(resolvedPosition);
  }

  void ApplyScaleOnly(Vector3 resolvedScale) {
    if (AreClose(transform.localScale, resolvedScale)) return;
    transform.localScale = resolvedScale;
  }

  bool ShouldKeepManualEditorPosition(Vector3 resolvedPosition) {
    if (Application.isPlaying || !hasResolvedPosition) return false;

    var currentPosition = transform.localPosition;
    return !AreClose(currentPosition, resolvedPosition) &&
           !AreClose(currentPosition, lastResolvedPosition);
  }

  void CacheResolvedPosition(Vector3 resolvedPosition) {
    lastResolvedPosition = resolvedPosition;
    hasResolvedPosition = true;
  }

  Sprite ResolveSourceSprite(SpriteRenderer renderer) {
    if (renderer == null) return null;

    var currentSprite = renderer.sprite;
    if (currentSprite == null) {
      sourceSprite = null;
      return null;
    }

    if (slicedSprite != null && currentSprite == slicedSprite && sourceSprite != null) {
      return sourceSprite;
    }

    sourceSprite = currentSprite;
    return sourceSprite;
  }

  bool HasBorderPixels() {
    return borderPixels.x > 0f ||
           borderPixels.y > 0f ||
           borderPixels.z > 0f ||
           borderPixels.w > 0f;
  }

  bool TryApplyNineSliceVisualState(SpriteRenderer renderer, Sprite sprite, Vector2 targetSizePixels, out Vector2 targetSizeUnits) {
    targetSizeUnits = ResolveTargetSizeUnits(sprite, targetSizePixels);
    var resolvedBorderPixels = ResolveRequestedBorderPixels(sprite);
    var resolvedSprite = EnsureSlicedSprite(sprite, resolvedBorderPixels);
    if (resolvedSprite == null) return false;

    if (renderer.sprite != resolvedSprite) {
      renderer.sprite = resolvedSprite;
    }
    if (renderer.drawMode != SpriteDrawMode.Sliced) {
      renderer.drawMode = SpriteDrawMode.Sliced;
    }
    if (!AreClose(renderer.size, targetSizeUnits)) {
      renderer.size = targetSizeUnits;
    }
    return true;
  }

  void ApplySimpleVisualState(SpriteRenderer renderer, Sprite sprite) {
    if (renderer.sprite != sprite) {
      renderer.sprite = sprite;
    }
    if (renderer.drawMode != SpriteDrawMode.Simple) {
      renderer.drawMode = SpriteDrawMode.Simple;
    }
  }

  Vector4 ResolveRequestedBorderPixels(Sprite sprite) {
    if (sprite == null) return Vector4.zero;

    var resolvedBorderPixels = new Vector4(
      Mathf.Max(sprite.border.x, borderPixels.x),
      Mathf.Max(sprite.border.y, borderPixels.y),
      Mathf.Max(sprite.border.z, borderPixels.z),
      Mathf.Max(sprite.border.w, borderPixels.w)
    );

    return NormalizeBorderPixels(resolvedBorderPixels, sprite.rect.size);
  }

  static Vector4 NormalizeBorderPixels(Vector4 requestedBorderPixels, Vector2 spriteRectSize) {
    var normalizedBorderPixels = requestedBorderPixels;
    normalizedBorderPixels.x = Mathf.Min(normalizedBorderPixels.x, spriteRectSize.x);
    normalizedBorderPixels.z = Mathf.Min(normalizedBorderPixels.z, spriteRectSize.x);
    normalizedBorderPixels.y = Mathf.Min(normalizedBorderPixels.y, spriteRectSize.y);
    normalizedBorderPixels.w = Mathf.Min(normalizedBorderPixels.w, spriteRectSize.y);

    var maxHorizontal = Mathf.Max(spriteRectSize.x, 0f);
    var horizontalSum = normalizedBorderPixels.x + normalizedBorderPixels.z;
    if (horizontalSum > maxHorizontal && horizontalSum > 0f) {
      var ratio = maxHorizontal / horizontalSum;
      normalizedBorderPixels.x *= ratio;
      normalizedBorderPixels.z *= ratio;
    }

    var maxVertical = Mathf.Max(spriteRectSize.y, 0f);
    var verticalSum = normalizedBorderPixels.y + normalizedBorderPixels.w;
    if (verticalSum > maxVertical && verticalSum > 0f) {
      var ratio = maxVertical / verticalSum;
      normalizedBorderPixels.y *= ratio;
      normalizedBorderPixels.w *= ratio;
    }

    return normalizedBorderPixels;
  }

  Sprite EnsureSlicedSprite(Sprite sprite, Vector4 resolvedBorderPixels) {
    if (sprite == null) return null;
    if (slicedSprite != null &&
        sourceSprite == sprite &&
        AreClose(slicedSpriteBorderPixels, resolvedBorderPixels)) {
      return slicedSprite;
    }

    DestroySlicedSprite();

    var spriteRect = sprite.rect;
    if (spriteRect.width <= 0f || spriteRect.height <= 0f) return null;

    var pivotNormalized = ResolvePivotNormalized(sprite);
    slicedSprite = Sprite.Create(
      sprite.texture,
      spriteRect,
      pivotNormalized,
      sprite.pixelsPerUnit,
      0u,
      SpriteMeshType.FullRect,
      resolvedBorderPixels
    );

    if (slicedSprite == null) return null;

    slicedSprite.name = sprite.name + "__anchoredSliced";
    slicedSprite.hideFlags = HideFlags.HideAndDontSave;
    slicedSpriteBorderPixels = resolvedBorderPixels;
    return slicedSprite;
  }

  void DestroySlicedSprite() {
    if (slicedSprite == null) return;

    if (Application.isPlaying) {
      Destroy(slicedSprite);
    } else {
      DestroyImmediate(slicedSprite);
    }

    slicedSprite = null;
    slicedSpriteBorderPixels = Vector4.zero;
  }

  static Vector2 ResolveTargetSizeUnits(Sprite sprite, Vector2 targetSizePixels) {
    if (sprite == null) return Vector2.zero;
    var pixelsPerUnit = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 100f;
    return targetSizePixels / pixelsPerUnit;
  }

  static Vector2 ResolvePivotNormalized(Sprite sprite) {
    if (sprite == null) return new Vector2(0.5f, 0.5f);
    var spriteRect = sprite.rect;
    if (spriteRect.width <= 0f || spriteRect.height <= 0f) return new Vector2(0.5f, 0.5f);
    return new Vector2(
      sprite.pivot.x / spriteRect.width,
      sprite.pivot.y / spriteRect.height
    );
  }

  void LogAppliedValues(Sprite sprite, Vector2 targetSizePixels, Vector3 resolvedScale, Vector3 resolvedPosition) {
    if (!enableDebugLogs) return;

    RuntimeLog.Log(
      "[AnchoredSpriteStretch] Applied on " + gameObject.name +
      " sprite='" + sprite.name + "'" +
      " max_px=" + maxSizePixels +
      " stretch_pct=" + stretchPercent +
      " border_px=" + borderPixels +
      " target_px=" + targetSizePixels +
      " origin=" + originLocalPosition +
      " anchor_x=" + anchorX +
      " anchor_y=" + anchorY +
      " scale=" + resolvedScale +
      " local_pos=" + resolvedPosition
    );
  }

  static bool AreClose(Vector3 a, Vector3 b) {
    return Mathf.Abs(a.x - b.x) <= 0.0001f &&
           Mathf.Abs(a.y - b.y) <= 0.0001f &&
           Mathf.Abs(a.z - b.z) <= 0.0001f;
  }

  static bool AreClose(Vector2 a, Vector2 b) {
    return Mathf.Abs(a.x - b.x) <= 0.0001f &&
           Mathf.Abs(a.y - b.y) <= 0.0001f;
  }

  static bool AreClose(Vector4 a, Vector4 b) {
    return Mathf.Abs(a.x - b.x) <= 0.0001f &&
           Mathf.Abs(a.y - b.y) <= 0.0001f &&
           Mathf.Abs(a.z - b.z) <= 0.0001f &&
           Mathf.Abs(a.w - b.w) <= 0.0001f;
  }

  void OnDestroy() {
    DestroySlicedSprite();
  }
}
