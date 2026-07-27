#pragma warning disable CS0162 // Unreachable code detected
using System;
using UnityEngine;

public partial class SpriteWithNormals {
  bool ShouldLogFetch => !ForceDisableDebugLogsForPerfPass && enableDebugSpriteFetchLogs && Application.isPlaying;
  bool ShouldLogApply => !ForceDisableDebugLogsForPerfPass && enableDebugSpriteApplyLogs && Application.isPlaying;

  void SyncRendererVisibility() {
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer != null) _renderer.enabled = !doNotRender && !externalVisualSuppressed;
  }

  [System.Diagnostics.Conditional("ENABLE_RUNTIME_DEBUG_LOGS")]
  void LogSpriteFetch(string stage, string details = "") {
    if (ForceDisableDebugLogsForPerfPass) return;
    if (!enableDebugSpriteFetchLogs || !Application.isPlaying) return;
    var normalizedStage = stage ?? "";
    var mode = normalizedStage.StartsWith("use_", StringComparison.Ordinal)
      ? "use"
      : normalizedStage.StartsWith("fetch_", StringComparison.Ordinal)
        ? "fetch"
        : "state";
    RuntimeLog.Log(
      "[SpriteWithNormals][Fetch] object='" + gameObject.name +
      "' category='" + (category ?? "") +
      "' requested_frame=" + _lastRequestedFrame +
      " request_version=" + _requestVersion +
      " mode='" + mode + "'" +
      " stage='" + normalizedStage + "'" +
      (string.IsNullOrWhiteSpace(details) ? "" : " " + details)
    );
  }

  [System.Diagnostics.Conditional("ENABLE_RUNTIME_DEBUG_LOGS")]
  void LogSpriteApply(string stage, Sprite colorSprite, Sprite normalSprite, string details = "") {
    if (ForceDisableDebugLogsForPerfPass) return;
    if (!enableDebugSpriteApplyLogs || !Application.isPlaying) return;
    RuntimeLog.Log(
      "[SpriteWithNormals][Apply] object='" + gameObject.name +
      "' category='" + (category ?? "") +
      "' requested_frame=" + _lastRequestedFrame +
      " stage='" + (stage ?? "") + "'" +
      " color='" + (colorSprite != null ? colorSprite.name : "") +
      "' normal='" + (normalSprite != null ? normalSprite.name : "") + "'" +
      (string.IsNullOrWhiteSpace(details) ? "" : " " + details)
    );
  }

  void ReportResolveError(SpriteLookupKey lookupKey) {
    if (_hasLastResolveError &&
        string.Equals(_lastResolveErrorLibraryName, lookupKey.libraryName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(_lastResolveErrorLabelPrefix, lookupKey.labelPrefix, StringComparison.Ordinal) &&
        string.Equals(_lastResolveErrorCategory, lookupKey.category, StringComparison.Ordinal)) return;

    _hasLastResolveError = true;
    _lastResolveErrorLibraryName = lookupKey.libraryName ?? "";
    _lastResolveErrorLabelPrefix = lookupKey.labelPrefix ?? "";
    _lastResolveErrorCategory = lookupKey.category ?? "";
    if (!s_ReportedResolveErrorKeys.Add(lookupKey)) return;
    Debug.LogError($"[SpriteWithNormals] No sprite mapping found for {lookupKey} on {gameObject.name}");
  }

  bool TryResolvePair(SpriteLookupKey key, out SpriteAddressPair pair) => SpriteAddressResolver.TryResolve(key, out pair, gameObject);
  bool IsResolvePending(SpriteLookupKey key) => SpriteAddressResolver.IsLookupPending(key, gameObject);
}
