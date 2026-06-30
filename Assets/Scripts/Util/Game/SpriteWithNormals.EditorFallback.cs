#pragma warning disable CS0162 // Unreachable code detected
using System;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class SpriteWithNormals {
  Sprite ResolveExpectedSliceSprite(Sprite loadedSprite, string sliceAddress, string channel) {
    if (loadedSprite == null) return null;
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out _, out var expectedSpriteName)) return loadedSprite;
    if (string.Equals(loadedSprite.name, expectedSpriteName, StringComparison.Ordinal)) return loadedSprite;
    if (SpriteSliceAddressUtility.HasEquivalentNumericLabel(loadedSprite.name, expectedSpriteName)) return loadedSprite;

#if UNITY_EDITOR
    if (!ShouldAvoidBlockingEditorSpriteFallback() &&
        Application.isEditor &&
        SpriteAddressResolver.TryLoadEditorSprite(sliceAddress, out var editorSprite) &&
        editorSprite != null) {
      WarnSliceMismatchOnce(sliceAddress, channel, loadedSprite.name, expectedSpriteName, corrected: true);
      return editorSprite;
    }
#endif

    WarnSliceMismatchOnce(sliceAddress, channel, loadedSprite.name, expectedSpriteName, corrected: false);
    _localLoadedSpriteByAddress.Remove(sliceAddress ?? "");
    return null;
  }

  void WarnSliceMismatchOnce(string sliceAddress, string channel, string loadedName, string expectedName, bool corrected) {
    var key = $"{channel}|{sliceAddress}";
    if (!_sliceMismatchWarnings.Add(key)) return;

    Debug.LogWarning(
      "[SpriteWithNormals] Slice mismatch on " + gameObject.name +
      " channel=" + channel +
      " expected='" + expectedName + "'" +
      " loaded='" + (loadedName ?? "") + "'" +
      " address='" + (sliceAddress ?? "") + "'" +
       " corrected=" + (corrected ? 1 : 0)
    );
  }

  void LogTrimmedOffsetReposition(
    string stage,
    string colorSliceAddress,
    Vector3 previousLocalPosition,
    Vector3 nextLocalPosition,
    Vector3 previousAppliedOffsetLocalUnits,
    Vector3 sourceOffsetLocalUnits,
    Vector3 appliedOffsetLocalUnits,
    string reason = ""
  ) {
    if (!ShouldLogRuntimeOffsetDebug()) return;
    if (!Application.isPlaying) return;
    var moved = !ApproximatelyVector3(previousLocalPosition, nextLocalPosition);
    var offsetChanged = !ApproximatelyVector3(previousAppliedOffsetLocalUnits, appliedOffsetLocalUnits);
    if (!moved && !offsetChanged && string.IsNullOrWhiteSpace(reason)) return;

    Debug.Log(
      "[SpriteWithNormals][Offset] object='" + gameObject.name +
      "' category='" + (category ?? "") +
      "' requested_frame=" + _lastRequestedFrame +
      " stage='" + (stage ?? "") + "'" +
      " address='" + (colorSliceAddress ?? "") + "'" +
      " previous_local=" + FormatTrimmedOffsetVector(previousLocalPosition) +
      " next_local=" + FormatTrimmedOffsetVector(nextLocalPosition) +
      " base_local=" + FormatTrimmedOffsetVector(_trimmedOffsetBaseLocalPosition) +
      " previous_applied=" + FormatTrimmedOffsetVector(previousAppliedOffsetLocalUnits) +
      " source_offset=" + FormatTrimmedOffsetVector(sourceOffsetLocalUnits) +
      " applied_offset=" + FormatTrimmedOffsetVector(appliedOffsetLocalUnits) +
      (string.IsNullOrWhiteSpace(reason) ? "" : " reason='" + reason + "'")
    );
  }

  static string FormatTrimmedOffsetVector(Vector3 value) {
    return "(" + value.x.ToString("0.###") + "," + value.y.ToString("0.###") + "," + value.z.ToString("0.###") + ")";
  }

  static bool ShouldLogRuntimeOffsetDebug() {
    if (ForceDisableDebugLogsForPerfPass) return false;
    if (!Application.isPlaying) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    if (!SpriteStreamingRuntimeSettings.EnableVerboseRuntimeConsoleLogs) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static bool ShouldLogVerboseEditorFallbackDebug() {
    if (ForceDisableDebugLogsForPerfPass) return false;
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableDiagnostics) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

#if UNITY_EDITOR
  Sprite TryResolveEditorSliceFallback(string sliceAddress, string channel, bool allowDuringOverlayFallback = false) {
    if (!Application.isEditor || string.IsNullOrWhiteSpace(sliceAddress)) return null;
    if (ShouldAvoidBlockingEditorSpriteFallback() &&
        !allowDuringOverlayFallback &&
        ShouldDeferEditorSliceFallbackForAddress(sliceAddress)) {
      var deferredKey = $"{channel}|editor_fallback_deferred|{sliceAddress}";
      if (_sliceMismatchWarnings.Add(deferredKey) && ShouldLogVerboseEditorFallbackDebug()) {
        Debug.Log(
          "[SpriteWithNormals] Deferred editor slice fallback on " + gameObject.name +
          " channel=" + channel +
          " address='" + sliceAddress + "'" +
          " overlay_active=1"
        );
      }
      return null;
    }
    if (!SpriteAddressResolver.TryLoadEditorSprite(sliceAddress, out var editorSprite) || editorSprite == null) {
      if (!allowDuringOverlayFallback || !TryForceImportPendingSliceAsset(sliceAddress)) {
        return null;
      }
      if (!SpriteAddressResolver.TryLoadEditorSprite(sliceAddress, out editorSprite) || editorSprite == null) {
        return null;
      }
    }

    var key = allowDuringOverlayFallback
      ? $"{channel}|editor_fallback_after_timeout|{sliceAddress}"
      : $"{channel}|editor_fallback|{sliceAddress}";
    if (_sliceMismatchWarnings.Add(key)) {
      if (ShouldLogVerboseEditorFallbackDebug()) {
        Debug.LogWarning(
          "[SpriteWithNormals] Editor slice fallback on " + gameObject.name +
          " channel=" + channel +
          " address='" + sliceAddress + "'" +
          " after_timeout=" + (allowDuringOverlayFallback ? 1 : 0) +
          " sprite='" + editorSprite.name + "'"
        );
      }
    }

    return editorSprite;
  }

  bool TryForceImportPendingSliceAsset(string sliceAddress) {
    if (!Application.isEditor || string.IsNullOrWhiteSpace(sliceAddress)) return false;
    if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out var atlasAssetPath, out _)) return false;
    if (string.IsNullOrWhiteSpace(atlasAssetPath)) return false;

    AssetDatabase.ImportAsset(
      atlasAssetPath,
      ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate
    );
    return true;
  }
#endif

#if UNITY_EDITOR
  public static void InvalidateEditorRuntimeAtlasAvailabilityCache() {
    editorRuntimeAtlasAvailabilityByPath.Clear();
  }

  static bool IsEditorRuntimeAtlasAddressAvailable(string runtimeAddress) {
    var atlasAssetPath = runtimeAddress ?? "";
    if (SpriteSliceAddressUtility.TryParseSliceAddress(atlasAssetPath, out var parsedAtlasAssetPath, out _)) {
      atlasAssetPath = parsedAtlasAssetPath;
    }

    atlasAssetPath = atlasAssetPath.Trim();
    if (string.IsNullOrWhiteSpace(atlasAssetPath)) return false;
    if (editorRuntimeAtlasAvailabilityByPath.TryGetValue(atlasAssetPath, out var cachedAvailable)) {
      return cachedAvailable;
    }

    var available = false;
    var groupFolderPath = Path.Combine("Assets", "AddressableAssetsData", "AssetGroups");
    if (Directory.Exists(groupFolderPath)) {
      var expectedAddressLine = "m_Address: " + atlasAssetPath;
      var groupAssetPaths = Directory.GetFiles(groupFolderPath, "*.asset", SearchOption.TopDirectoryOnly);
      for (var i = 0; i < groupAssetPaths.Length; i++) {
        var groupAssetPath = groupAssetPaths[i];
        if (string.IsNullOrWhiteSpace(groupAssetPath)) continue;

        try {
          foreach (var line in File.ReadLines(groupAssetPath)) {
            if (!string.Equals(line.Trim(), expectedAddressLine, StringComparison.Ordinal)) continue;
            available = true;
            break;
          }
        }
        catch {
          continue;
        }

        if (available) break;
      }
    }

    editorRuntimeAtlasAvailabilityByPath[atlasAssetPath] = available;
    return available;
  }

  void ApplyEditorPreview(SpriteAddressPair pair, SpriteLookupKey lookupKey) {
    if (!SpriteAddressResolver.TryLoadEditorSprite(pair.colorAddress, out var colorSprite) || colorSprite == null) {
      Debug.LogError($"[SpriteWithNormals] Editor preview color sprite not found for '{pair.colorAddress}' ({lookupKey})");
      return;
    }
    SpriteAddressResolver.TryLoadEditorSprite(pair.normalAddress, out var normalSprite);
    if (!string.IsNullOrWhiteSpace(pair.normalAddress) &&
        normalSprite == null &&
        _editorPreviewNormalMissWarnings.Add(pair.normalAddress)) {
      Debug.LogWarning($"[SpriteWithNormals] Editor preview normal sprite not found for '{pair.normalAddress}' ({lookupKey})");
    }
    ApplySprites(colorSprite, normalSprite, pair.colorAddress);
  }
#endif
}
