using System;
using UnityEngine;

public partial class SpriteWithNormals {
  void ApplySprites(Sprite colorSprite, Sprite normalSprite, string colorSliceAddress) {
    var renderedColorSprite = ResolveRenderedSprite(colorSprite, useNormalFill: false);
    if (_renderer.sprite != renderedColorSprite) {
      _renderer.sprite = renderedColorSprite;
    }
    ApplyConfiguredTrimmedOffset(colorSprite, colorSliceAddress);
    var renderedNormalSprite = ResolveRenderedSprite(normalSprite, useNormalFill: true);
    var normalTexture = renderedNormalSprite != null ? renderedNormalSprite.texture : GetFallbackNormalTexture();
    var normalTextureId = ObjectEntityId.GetRawValue(normalTexture);
    if (normalTextureId == _lastAppliedNormalTextureId) return;

    _mpb ??= new MaterialPropertyBlock();
    _renderer.GetPropertyBlock(_mpb);
    if (normalTexture != null) _mpb.SetTexture(NormalMapPropertyId, normalTexture);
    _renderer.SetPropertyBlock(_mpb);
    _lastAppliedNormalTextureId = normalTextureId;
  }

  void ClearRenderedSprites() {
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer != null) ApplySprites(null, null, "");
  }

  void CompleteColorResolveFailure(
    string reason,
    string colorSliceAddress,
    ref TextureResidencyCache.Lease colorLease,
    ref TextureResidencyCache.Lease normalLease
  ) {
    var keepCurrent = HasRenderedSprite();
    ClearPendingState();
    if (!keepCurrent) ClearRenderedSprites();
    if (ShouldLogFetch) {
      LogSpriteFetch(
        reason,
        "address='" + (colorSliceAddress ?? "") + "' keep_current=" + (keepCurrent ? 1 : 0)
      );
    }
    ReleaseLease(ref colorLease);
    ReleaseLease(ref normalLease);
    TryStartDeferredRequest();
  }

  bool HasRenderedSprite() {
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    return _renderer != null && _renderer.sprite != null;
  }

  Sprite ResolveRenderedSprite(Sprite sourceSprite, bool useNormalFill) {
    if (sourceSprite == null) return null;
    if (!TryGetShaderMarginPixels(out var marginX, out var marginY)) return sourceSprite;

    var key = new PaddedSpriteCacheKey(ObjectEntityId.GetRawValue(sourceSprite), marginX, marginY, useNormalFill);
    if (_generatedPaddedSprites.TryGetValue(key, out var cachedSprite) && cachedSprite != null) {
      return cachedSprite;
    }

    if (!TryCreatePaddedSprite(sourceSprite, marginX, marginY, useNormalFill, out var paddedSprite)) {
      return sourceSprite;
    }

    CacheGeneratedPaddedSprite(key, paddedSprite);
    return paddedSprite;
  }

  bool TryGetShaderMarginPixels(out int marginX, out int marginY) {
    marginX = Mathf.Max(0, Mathf.CeilToInt(shaderMarginPixelsX));
    marginY = Mathf.Max(0, Mathf.CeilToInt(shaderMarginPixelsY));
    return marginX > 0 || marginY > 0;
  }

  bool TryCreatePaddedSprite(Sprite sourceSprite, int marginX, int marginY, bool useNormalFill, out Sprite paddedSprite) {
    paddedSprite = null;
    if (sourceSprite == null || sourceSprite.texture == null) return false;

    var textureRect = sourceSprite.textureRect;
    var sourceX = Mathf.RoundToInt(textureRect.x);
    var sourceY = Mathf.RoundToInt(textureRect.y);
    var sourceWidth = Mathf.RoundToInt(textureRect.width);
    var sourceHeight = Mathf.RoundToInt(textureRect.height);
    if (sourceWidth <= 0 || sourceHeight <= 0) return false;

    var paddedWidth = sourceWidth + (marginX * 2);
    var paddedHeight = sourceHeight + (marginY * 2);
    if (paddedWidth <= 0 || paddedHeight <= 0) return false;

    Color[] sourcePixels;
    try {
      sourcePixels = sourceSprite.texture.GetPixels(sourceX, sourceY, sourceWidth, sourceHeight);
    }
    catch (Exception ex) {
      WarnPaddingCreationFailureOnce(sourceSprite, marginX, marginY, useNormalFill, ex);
      return false;
    }

    var paddedTexture = new Texture2D(paddedWidth, paddedHeight, TextureFormat.RGBA32, false, useNormalFill) {
      filterMode = sourceSprite.texture.filterMode,
      wrapMode = TextureWrapMode.Clamp,
      name = sourceSprite.name + "__paddingTexture"
    };
    paddedTexture.hideFlags = HideFlags.HideAndDontSave;

    var fillColor = useNormalFill ? FlatNormalPaddingColor : TransparentPaddingColor;
    var fillPixels = BuildFillPixels(paddedWidth, paddedHeight, fillColor);
    paddedTexture.SetPixels32(fillPixels);
    paddedTexture.SetPixels(marginX, marginY, sourceWidth, sourceHeight, sourcePixels);
    paddedTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

    var paddedPivot = new Vector2(sourceSprite.pivot.x + marginX, sourceSprite.pivot.y + marginY);
    var paddedBorder = sourceSprite.border + new Vector4(marginX, marginY, marginX, marginY);
    paddedSprite = Sprite.Create(
      paddedTexture,
      new Rect(0f, 0f, paddedWidth, paddedHeight),
      new Vector2(
        paddedPivot.x / paddedWidth,
        paddedPivot.y / paddedHeight),
      sourceSprite.pixelsPerUnit,
      0u,
      SpriteMeshType.FullRect,
      paddedBorder
    );

    if (paddedSprite == null) {
      DestroyGeneratedObject(paddedTexture);
      return false;
    }

    paddedSprite.name = sourceSprite.name + "__paddingSprite";
    paddedSprite.hideFlags = HideFlags.HideAndDontSave;
    _generatedPaddedTextures.Add(paddedTexture);
    _generatedPaddedSpriteAssets.Add(paddedSprite);
    return true;
  }

  static Color32[] BuildFillPixels(int width, int height, Color32 fillColor) {
    var pixelCount = Mathf.Max(0, width * height);
    var pixels = new Color32[pixelCount];
    for (var i = 0; i < pixelCount; i++) pixels[i] = fillColor;
    return pixels;
  }

  void CacheGeneratedPaddedSprite(PaddedSpriteCacheKey key, Sprite paddedSprite) {
    if (_generatedPaddedSprites.Count >= MaxGeneratedPaddedSpriteCacheEntries &&
        !_generatedPaddedSprites.ContainsKey(key)) {
      ClearGeneratedPaddedSprites();
    }

    _generatedPaddedSprites[key] = paddedSprite;
  }

  void ClearGeneratedPaddedSprites() {
    _generatedPaddedSprites.Clear();
    _paddingCreationWarnings.Clear();

    for (var i = 0; i < _generatedPaddedSpriteAssets.Count; i++) {
      var sprite = _generatedPaddedSpriteAssets[i];
      DestroyGeneratedObject(sprite);
    }
    _generatedPaddedSpriteAssets.Clear();

    for (var i = 0; i < _generatedPaddedTextures.Count; i++) {
      var texture = _generatedPaddedTextures[i];
      DestroyGeneratedObject(texture);
    }
    _generatedPaddedTextures.Clear();
  }

  static void DestroyGeneratedObject(UnityEngine.Object obj) {
    if (obj == null) return;
    if (Application.isPlaying) {
      Destroy(obj);
      return;
    }
    DestroyImmediate(obj);
  }

  void WarnPaddingCreationFailureOnce(Sprite sourceSprite, int marginX, int marginY, bool useNormalFill, Exception ex) {
    var key = new PaddedSpriteCacheKey(ObjectEntityId.GetRawValue(sourceSprite), marginX, marginY, useNormalFill);
    if (!_paddingCreationWarnings.Add(key)) return;

    Debug.LogWarning(
      "[SpriteWithNormals] Failed to create shader padding for '" + name +
      "' sprite='" + sourceSprite.name +
      "' margin_x=" + marginX +
      " margin_y=" + marginY +
      " normal_fill=" + (useNormalFill ? 1 : 0) +
      " reason='" + ex.Message + "'"
    );
  }

  void ApplyConfiguredTrimmedOffset(Sprite colorSprite, string colorSliceAddress) {
    _appliedColorSliceAddress = colorSliceAddress ?? "";
    if (!useTrimmedAtlasOffset || colorSprite == null || string.IsNullOrWhiteSpace(_appliedColorSliceAddress)) {
      ResetAppliedTrimmedOffsetState(clearSliceAddress: colorSprite == null || !useTrimmedAtlasOffset);
      return;
    }

    var metadataState = ResolveTrimmedOffsetState(_appliedColorSliceAddress, colorSprite, out var offsetLocalUnits);
    if (metadataState == SpriteColdLoadState.Pending) {
      return;
    }

    if (metadataState == SpriteColdLoadState.Missing) {
      ClearAppliedTrimmedOffset("offset_unresolved", _appliedColorSliceAddress);
      return;
    }

    ApplyTrimmedOffsetLocal(offsetLocalUnits, colorSprite, _appliedColorSliceAddress);
  }

  SpriteColdLoadState ResolveTrimmedOffsetState(string colorSliceAddress, Sprite colorSprite, out Vector3 offsetLocalUnits) {
    offsetLocalUnits = Vector3.zero;
    if (colorSprite == null || string.IsNullOrWhiteSpace(colorSliceAddress)) return SpriteColdLoadState.Missing;
    var metadataState = GetTrimmedMetadataState(colorSliceAddress, requestIfNeeded: true);
    if (!metadataState.IsCommitReady()) return metadataState;
    var flipX = _renderer != null && _renderer.flipX;
    var flipY = _renderer != null && _renderer.flipY;
    if (!TrimmedSpriteOffsetResolver.TryGetExactLocalOffset(
      colorSliceAddress,
      colorSprite,
      out offsetLocalUnits,
      flipX,
      flipY,
      OnTrimmedOffsetMetadataReady)) {
      return SpriteColdLoadState.Missing;
    }

    return SpriteColdLoadState.Ready;
  }

  void ApplyTrimmedOffsetLocal(Vector3 offsetLocalUnits, Sprite colorSprite, string colorSliceAddress) {
    var previousLocalPosition = transform.localPosition;
    var previousAppliedOffsetLocalUnits = _lastAppliedTrimmedOffsetLocalUnits;
    var appliedPositionOffsetLocalUnits = ScaleTrimmedOffsetForTransform(offsetLocalUnits);
    var baseLocalPosition = ResolveTrimmedOffsetBaseLocalPosition(offsetLocalUnits, appliedPositionOffsetLocalUnits);
    var nextLocalPosition = baseLocalPosition + appliedPositionOffsetLocalUnits;
    transform.localPosition = nextLocalPosition;
    _lastAppliedTrimmedOffsetLocalUnits = appliedPositionOffsetLocalUnits;
    _hasAppliedTrimmedOffset = true;
    PersistTrimmedOffsetState();
    LogTrimmedOffsetReposition(
      "offset_applied",
      colorSliceAddress,
      previousLocalPosition,
      nextLocalPosition,
      previousAppliedOffsetLocalUnits,
      offsetLocalUnits,
      appliedPositionOffsetLocalUnits
    );
    if (ShouldLogApply) {
      LogSpriteApply(
        "offset_applied",
        colorSprite,
        null,
        "address='" + (colorSliceAddress ?? "") + "'" +
        " source_offset=(" + offsetLocalUnits.x.ToString("0.###") + "," + offsetLocalUnits.y.ToString("0.###") + ")" +
        " applied_offset=(" + appliedPositionOffsetLocalUnits.x.ToString("0.###") + "," + appliedPositionOffsetLocalUnits.y.ToString("0.###") + ")"
      );
    }
  }

  void ClearAppliedTrimmedOffset(string reason = "", string colorSliceAddress = "", bool shouldLog = true) {
    var previousLocalPosition = transform.localPosition;
    var previousAppliedOffsetLocalUnits = _lastAppliedTrimmedOffsetLocalUnits;
    var nextLocalPosition = ResolveClearedTrimmedOffsetLocalPosition(previousLocalPosition);
    transform.localPosition = nextLocalPosition;
    _lastAppliedTrimmedOffsetLocalUnits = Vector3.zero;
    _hasAppliedTrimmedOffset = false;
    PersistTrimmedOffsetState();
    if (!shouldLog) return;
    LogTrimmedOffsetReposition(
      "offset_cleared",
      colorSliceAddress,
      previousLocalPosition,
      nextLocalPosition,
      previousAppliedOffsetLocalUnits,
      Vector3.zero,
      Vector3.zero,
      reason
    );
  }

  void ResetAppliedTrimmedOffsetState(bool clearSliceAddress = true) {
    ClearAppliedTrimmedOffset(shouldLog: false);
    if (clearSliceAddress) {
      _appliedColorSliceAddress = "";
    }
  }

  Vector3 ResolveClearedTrimmedOffsetLocalPosition(Vector3 currentLocalPosition) {
    if (_hasTrimmedOffsetBaseLocalPosition) return _trimmedOffsetBaseLocalPosition;
    if (_hasAppliedTrimmedOffset) return currentLocalPosition - _lastAppliedTrimmedOffsetLocalUnits;
    return currentLocalPosition;
  }

  void RefreshTrimmedOffsetForCurrentSprite() {
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    var currentSprite = _renderer != null ? _renderer.sprite : null;
    ApplyConfiguredTrimmedOffset(currentSprite, _appliedColorSliceAddress);
  }

  void OnTrimmedOffsetMetadataReady() {
    if (!useTrimmedAtlasOffset) return;
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer == null || _renderer.sprite == null) return;
    if (string.IsNullOrWhiteSpace(_appliedColorSliceAddress)) return;
    if (ShouldLogRuntimeOffsetDebug()) {
      Debug.Log(
        "[SpriteWithNormals][Offset] object='" + gameObject.name +
        "' category='" + (category ?? "") +
        "' requested_frame=" + _lastRequestedFrame +
        " stage='metadata_ready'" +
        " address='" + _appliedColorSliceAddress + "'" +
        " current_local=" + FormatTrimmedOffsetVector(transform.localPosition)
      );
    }
    ApplyConfiguredTrimmedOffset(_renderer.sprite, _appliedColorSliceAddress);
  }

  void SyncSerializedTrimmedOffsetState() {
    NormalizeSerializedTrimmedOffsetState();
    _lastAppliedTrimmedOffsetLocalUnits = serializedAppliedTrimmedOffsetLocalUnits;
    _hasAppliedTrimmedOffset = serializedHasAppliedTrimmedOffset;
    _trimmedOffsetBaseLocalPosition = serializedTrimmedOffsetBaseLocalPosition;
    _hasTrimmedOffsetBaseLocalPosition = serializedHasTrimmedOffsetBaseLocalPosition;
  }

  void NormalizeSerializedTrimmedOffsetState() {
    var hadAppliedState = serializedHasAppliedTrimmedOffset;
    var hadBaseState = serializedHasTrimmedOffsetBaseLocalPosition;
    if (!hadAppliedState && !hadBaseState) return;

    var currentLocalPosition = transform.localPosition;
    var serializedAppliedOffset = serializedAppliedTrimmedOffsetLocalUnits;
    var serializedBasePosition = hadBaseState
      ? serializedTrimmedOffsetBaseLocalPosition
      : currentLocalPosition - serializedAppliedOffset;
    var expectedAppliedPosition = serializedBasePosition + serializedAppliedOffset;
    var normalizedBasePosition = LooksLikeDuplicatedTrimmedOffsetBase(serializedBasePosition, serializedAppliedOffset)
      ? Vector3.zero
      : serializedBasePosition;

    var restoredBasePosition = false;
    if (hadAppliedState && ApproximatelyVector3(currentLocalPosition, expectedAppliedPosition)) {
      if (!ApproximatelyVector3(currentLocalPosition, normalizedBasePosition)) {
        transform.localPosition = normalizedBasePosition;
        restoredBasePosition = true;
      }
    }

    if (restoredBasePosition && ShouldLogRuntimeOffsetDebug()) {
      Debug.Log(
        "[SpriteWithNormals] Normalized persisted trimmed offset state for '" + name +
        "'. current=(" + currentLocalPosition.x.ToString("0.###") + "," + currentLocalPosition.y.ToString("0.###") +
        ") base=(" + serializedBasePosition.x.ToString("0.###") + "," + serializedBasePosition.y.ToString("0.###") +
        ") normalized_base=(" + normalizedBasePosition.x.ToString("0.###") + "," + normalizedBasePosition.y.ToString("0.###") +
        ") applied=(" + serializedAppliedOffset.x.ToString("0.###") + "," + serializedAppliedOffset.y.ToString("0.###") + ")");
    }

    serializedAppliedTrimmedOffsetLocalUnits = Vector3.zero;
    serializedHasAppliedTrimmedOffset = false;
    serializedTrimmedOffsetBaseLocalPosition = Vector3.zero;
    serializedHasTrimmedOffsetBaseLocalPosition = false;

#if UNITY_EDITOR
    if (!Application.isPlaying && restoredBasePosition) {
      UnityEditor.EditorUtility.SetDirty(transform);
      UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
  }

  static bool LooksLikeDuplicatedTrimmedOffsetBase(Vector3 serializedBasePosition, Vector3 serializedAppliedOffset) {
    return ComponentsLookDuplicated(serializedBasePosition.x, serializedAppliedOffset.x) &&
           ComponentsLookDuplicated(serializedBasePosition.y, serializedAppliedOffset.y) &&
           ComponentsLookDuplicated(serializedBasePosition.z, serializedAppliedOffset.z);
  }

  static bool ComponentsLookDuplicated(float baseValue, float appliedValue) {
    var maxAbs = Mathf.Max(Mathf.Abs(baseValue), Mathf.Abs(appliedValue));
    if (maxAbs <= 0.0005f) return true;
    if (Mathf.Sign(baseValue) != Mathf.Sign(appliedValue)) return false;
    return Mathf.Abs(baseValue - appliedValue) <= Mathf.Max(0.1f, maxAbs * 0.1f);
  }

  void PersistTrimmedOffsetState() {
    if (!ShouldPersistTrimmedOffsetState()) {
      if (serializedHasAppliedTrimmedOffset || serializedHasTrimmedOffsetBaseLocalPosition ||
          serializedAppliedTrimmedOffsetLocalUnits != Vector3.zero ||
          serializedTrimmedOffsetBaseLocalPosition != Vector3.zero) {
        serializedAppliedTrimmedOffsetLocalUnits = Vector3.zero;
        serializedHasAppliedTrimmedOffset = false;
        serializedTrimmedOffsetBaseLocalPosition = Vector3.zero;
        serializedHasTrimmedOffsetBaseLocalPosition = false;
      }
      return;
    }

    serializedAppliedTrimmedOffsetLocalUnits = _lastAppliedTrimmedOffsetLocalUnits;
    serializedHasAppliedTrimmedOffset = _hasAppliedTrimmedOffset;
    serializedTrimmedOffsetBaseLocalPosition = _trimmedOffsetBaseLocalPosition;
    serializedHasTrimmedOffsetBaseLocalPosition = _hasTrimmedOffsetBaseLocalPosition;
  }

  static bool ShouldPersistTrimmedOffsetState() {
    return !Application.isPlaying;
  }

  Vector3 ResolveTrimmedOffsetBaseLocalPosition(Vector3 sourceOffsetLocalUnits, Vector3 appliedOffsetLocalUnits) {
    if (_hasTrimmedOffsetBaseLocalPosition) {
      return _trimmedOffsetBaseLocalPosition;
    }

    var currentLocalPosition = transform.localPosition;
    var baseLocalPosition = InferTrimmedOffsetBaseLocalPosition(currentLocalPosition, sourceOffsetLocalUnits, appliedOffsetLocalUnits);
    _trimmedOffsetBaseLocalPosition = baseLocalPosition;
    _hasTrimmedOffsetBaseLocalPosition = true;
    PersistTrimmedOffsetState();
    return baseLocalPosition;
  }

  Vector3 InferTrimmedOffsetBaseLocalPosition(Vector3 currentLocalPosition, Vector3 sourceOffsetLocalUnits, Vector3 appliedOffsetLocalUnits) {
    if (ApproximatelyVector3(currentLocalPosition, Vector3.zero)) {
      return Vector3.zero;
    }

    if (ApproximatelyVector3(currentLocalPosition, sourceOffsetLocalUnits) ||
        ApproximatelyVector3(currentLocalPosition, sourceOffsetLocalUnits * 2f) ||
        ApproximatelyVector3(currentLocalPosition, appliedOffsetLocalUnits) ||
        ApproximatelyVector3(currentLocalPosition, appliedOffsetLocalUnits * 2f)) {
      Debug.Log(
        "[SpriteWithNormals] Normalized edit-mode trimmed offset baseline for '" + name +
        "'. current=(" + currentLocalPosition.x.ToString("0.###") + "," + currentLocalPosition.y.ToString("0.###") +
        ") source_offset=(" + sourceOffsetLocalUnits.x.ToString("0.###") + "," + sourceOffsetLocalUnits.y.ToString("0.###") +
        ") applied_offset=(" + appliedOffsetLocalUnits.x.ToString("0.###") + "," + appliedOffsetLocalUnits.y.ToString("0.###") + ")");
      return Vector3.zero;
    }

    return _hasAppliedTrimmedOffset
      ? currentLocalPosition - _lastAppliedTrimmedOffsetLocalUnits
      : currentLocalPosition;
  }

  static bool ApproximatelyVector3(Vector3 left, Vector3 right, float epsilon = 0.0015f) {
    return Mathf.Abs(left.x - right.x) <= epsilon &&
           Mathf.Abs(left.y - right.y) <= epsilon &&
           Mathf.Abs(left.z - right.z) <= epsilon;
  }

  Vector3 ScaleTrimmedOffsetForTransform(Vector3 offsetLocalUnits) {
    var localScale = transform.localScale;
    var scaleX = Mathf.Abs(localScale.x);
    var scaleY = Mathf.Abs(localScale.y);
    if (scaleX <= 0f) scaleX = 1f;
    if (scaleY <= 0f) scaleY = 1f;
    return new Vector3(offsetLocalUnits.x * scaleX, offsetLocalUnits.y * scaleY, offsetLocalUnits.z);
  }

}
