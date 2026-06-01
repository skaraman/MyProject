using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static partial class TextureResidencyCache {
  static bool TryConsumeAtlasExpansionBudget() {
    var frame = Time.frameCount;
    if (atlasExpansionFrame != frame) {
      atlasExpansionFrame = frame;
      atlasExpansionCountThisFrame = 0;
    }
    var loadingContextActive = StreamingWarmOrchestrator.IsWarmGateRunning || SpriteStreamingLoadingState.IsLoadingOverlayActive;
    var maxPerFrame = loadingContextActive ? AtlasExpansionMaxPerFrameLoading : AtlasExpansionMaxPerFrame;
    if (ShouldUseStrictSerialLoadingDebounce()) {
      maxPerFrame = StrictSerialLoadingBudgetPerFrame;
    }
    if (atlasExpansionCountThisFrame >= maxPerFrame) return false;
    atlasExpansionCountThisFrame++;
    return true;
  }

  static bool TryConsumeAtlasExpansionAddressBudget() {
    var frame = Time.frameCount;
    if (atlasExpansionAddressBudgetFrame != frame) {
      atlasExpansionAddressBudgetFrame = frame;
      atlasExpansionAddressesQueuedThisFrame = 0;
    }
    var loadingContextActive = StreamingWarmOrchestrator.IsWarmGateRunning || SpriteStreamingLoadingState.IsLoadingOverlayActive;
    var maxAddressesPerFrame = loadingContextActive ? AtlasExpansionMaxAddressesPerFrameLoading : AtlasExpansionMaxAddressesPerFrame;
    if (ShouldUseStrictSerialLoadingDebounce()) {
      maxAddressesPerFrame = StrictSerialLoadingBudgetPerFrame;
    }
    if (atlasExpansionAddressesQueuedThisFrame >= maxAddressesPerFrame) return false;
    atlasExpansionAddressesQueuedThisFrame++;
    return true;
  }

  static void RecordRequestForFrame(bool isAcquire, string sourceTag) {
    if (!ShouldLogRequestFrameDiagnostics()) return;
    EnsureRequestDiagFrameCurrent();
    if (isAcquire) requestDiagAcquireCalls++;
    else requestDiagWarmupCalls++;
    RecordRequestDiagSource(sourceTag);
  }

  static void RecordQueueAddForFrame() {
    if (!ShouldLogRequestFrameDiagnostics()) return;
    EnsureRequestDiagFrameCurrent();
    requestDiagQueueAdds++;
  }

  static void RecordNewEntryForFrame() {
    if (!ShouldLogRequestFrameDiagnostics()) return;
    EnsureRequestDiagFrameCurrent();
    requestDiagNewEntries++;
  }

  static void RecordLoadCompleteLatency(float latencyMs) {
    const int window = 64;
    loadCompleteLatencyRollingCount = Math.Min(loadCompleteLatencyRollingCount + 1, window);
    var alpha = 1f / loadCompleteLatencyRollingCount;
    loadCompleteLatencyRollingAvgMs += alpha * (latencyMs - loadCompleteLatencyRollingAvgMs);
  }

  static void RecordPumpForFrame(float pumpMs, int startedLoads) {
    if (!ShouldLogRequestFrameDiagnostics()) return;
    EnsureRequestDiagFrameCurrent();
    requestDiagPumpCalls++;
    requestDiagPumpTotalMs += Mathf.Max(pumpMs, 0f);
    requestDiagStartedLoads += Math.Max(startedLoads, 0);
  }

  static void EnsureRequestDiagFrameCurrent() {
    var frame = Time.frameCount;
    if (requestDiagFrame == frame) return;
    FlushRequestDiagFrame();
    requestDiagFrame = frame;
    requestDiagAcquireCalls = 0;
    requestDiagWarmupCalls = 0;
    requestDiagQueueAdds = 0;
    requestDiagNewEntries = 0;
    requestDiagPumpCalls = 0;
    requestDiagStartedLoads = 0;
    requestDiagPumpTotalMs = 0f;
    requestDiagSourceCounts.Clear();
  }

  static void FlushRequestDiagFrame() {
    if (!ShouldLogRequestFrameDiagnostics()) return;
    if (requestDiagFrame < 0) return;

    var requestTotal = requestDiagAcquireCalls + requestDiagWarmupCalls;
    var shouldReport = requestTotal >= RequestDiagRequestThreshold ||
      requestDiagQueueAdds >= RequestDiagQueueAddsThreshold ||
      requestDiagNewEntries >= RequestDiagNewEntriesThreshold ||
      requestDiagPumpTotalMs >= RequestDiagPumpMsThreshold;
    if (!shouldReport) return;

    var topSources = BuildTopRequestDiagSources(maxSources: 5);
    Debug.LogWarning(
      "[TextureResidencyCache][RequestDiag] frame=" + requestDiagFrame +
      " requests=" + requestTotal +
      " acquire=" + requestDiagAcquireCalls +
      " warmup=" + requestDiagWarmupCalls +
      " queue_adds=" + requestDiagQueueAdds +
      " new_entries=" + requestDiagNewEntries +
      " pump_calls=" + requestDiagPumpCalls +
      " pump_ms=" + requestDiagPumpTotalMs.ToString("0.0") +
      " started_loads=" + requestDiagStartedLoads +
      " top_sources=" + topSources
    );

  }

  static void RecordRequestDiagSource(string sourceTag) {
    var normalized = string.IsNullOrWhiteSpace(sourceTag) ? "(unknown)" : sourceTag.Trim();
    if (requestDiagSourceCounts.TryGetValue(normalized, out var existing)) {
      requestDiagSourceCounts[normalized] = existing + 1;
      return;
    }
    requestDiagSourceCounts[normalized] = 1;
  }

  static string BuildTopRequestDiagSources(int maxSources) {
    if (requestDiagSourceCounts.Count <= 0) return "(none)";
    maxSources = Math.Max(maxSources, 1);
    var topSources = requestDiagTopSourcesScratch;
    topSources.Clear();
    foreach (var pair in requestDiagSourceCounts) {
      if (topSources.Count < maxSources) {
        topSources.Add(pair);
        continue;
      }
      var weakestIndex = 0;
      for (var i = 1; i < topSources.Count; i++) {
        if (topSources[i].Value < topSources[weakestIndex].Value) weakestIndex = i;
      }
      if (pair.Value <= topSources[weakestIndex].Value) continue;
      topSources[weakestIndex] = pair;
    }

    topSources.Sort((left, right) => right.Value.CompareTo(left.Value));
    var builder = requestDiagTopSourcesBuilder;
    builder.Clear();
    for (var i = 0; i < topSources.Count; i++) {
      if (i > 0) builder.Append(", ");
      builder.Append(topSources[i].Key).Append('=').Append(topSources[i].Value);
    }
    var result = builder.ToString();
    topSources.Clear();
    builder.Clear();
    return result;
  }

  static string BuildRequestDiagSourceTag(string callerMemberName, string callerFilePath, int callerLineNumber) {
    var member = string.IsNullOrWhiteSpace(callerMemberName) ? "(unknown)" : callerMemberName.Trim();
    var file = string.IsNullOrWhiteSpace(callerFilePath) ? "" : Path.GetFileName(callerFilePath);
    if (string.IsNullOrWhiteSpace(file)) return member;
    var line = Math.Max(callerLineNumber, 0);
    return file + ":" + line + "/" + member;
  }

  static string ResolveAtlasExpansionContext() {
    if (StreamingWarmOrchestrator.IsWarmGateRunning) return "warm_gate";
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive) return "loading_overlay";
    return "live";
  }

  static int ResolveOverlayStartAllowance(int maxStarts) {
    if (!IsLoadingScreenStreamingContextActive()) {
      overlayStartTokens = 0f;
      overlayStartTokenLastRefillAt = -1f;
      return int.MaxValue;
    }

    // Token bucket pacing keeps overlay load starts smooth even when Pump() is
    // called multiple times in one frame or after a long stall.
    RefillOverlayStartTokens();
    var remainingFrameStarts = Math.Max(maxStarts - startedLoadsThisFrame, 0);
    var availableTokens = Mathf.FloorToInt(overlayStartTokens);
    return Mathf.Clamp(Math.Min(availableTokens, remainingFrameStarts), 0, remainingFrameStarts);
  }

  static void ConsumeOverlayStartToken() {
    if (!IsLoadingScreenStreamingContextActive()) return;
    overlayStartTokens = Mathf.Max(overlayStartTokens - 1f, 0f);
  }

  static void RefillOverlayStartTokens() {
    if (!IsLoadingScreenStreamingContextActive()) {
      overlayStartTokens = 0f;
      overlayStartTokenLastRefillAt = -1f;
      return;
    }

    var now = Time.realtimeSinceStartup;
    var burstCap = ResolveOverlayStartBurstCap();
    if (overlayStartTokenLastRefillAt < 0f) {
      overlayStartTokenLastRefillAt = now;
      overlayStartTokens = burstCap;
      return;
    }

    var elapsed = Mathf.Max(now - overlayStartTokenLastRefillAt, 0f);
    overlayStartTokenLastRefillAt = now;
    overlayStartTokens = Mathf.Min(
      overlayStartTokens + (elapsed * ResolveOverlayStartRatePerSecond()),
      burstCap
    );
  }

  static float ResolveOverlayStartRatePerSecond() {
    if (ShouldUseStrictSerialLoadingDebounce()) {
      return StrictSerialLoadingBudgetPerFrame;
    }
    return Application.isMobilePlatform
      ? MobileOverlayStartRatePerSecond
      : DesktopOverlayStartRatePerSecond;
  }

  static int ResolveOverlayStartBurstCap() {
    if (ShouldUseStrictSerialLoadingDebounce()) {
      return StrictSerialLoadingBudgetPerFrame;
    }
    return Application.isMobilePlatform
      ? MobileOverlayStartBurstCap
      : DesktopOverlayStartBurstCap;
  }

  static float ResolveCompletionFollowupDeadline(float startedAt) {
    if (!IsCompletionFollowupDeadlineActive()) return float.PositiveInfinity;
    return startedAt + (ResolveCompletionFollowupBudgetMs() / 1000f);
  }

  static bool HasCompletionFollowupBudgetRemaining(float deadlineAt) {
    return float.IsPositiveInfinity(deadlineAt) || Time.realtimeSinceStartup < deadlineAt;
  }

  static bool IsCompletionFollowupDeadlineActive() {
    return IsLoadingScreenStreamingContextActive() ||
      queuedEntryCount > 0 ||
      inFlightLoads > 0 ||
      deferredRequests.Count > 0;
  }

  static float ResolveCompletionFollowupBudgetMs() {
    if (IsLoadingScreenStreamingContextActive()) {
      return CompletionFollowupOverlayBudgetMs;
    }
    if (queuedEntryCount > 0 || inFlightLoads > 0 || deferredRequests.Count > 0) {
      return CompletionFollowupLoadingBudgetMs;
    }
    return float.PositiveInfinity;
  }

  static bool IsLoadingScreenStreamingContextActive() {
    return StreamingWarmOrchestrator.IsWarmGateRunning || SpriteStreamingLoadingState.IsLoadingOverlayActive;
  }

  static bool IsProtectedLoadingScreenStreamingContextActive() {
    return StreamingWarmOrchestrator.IsWarmGateRunning || SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive;
  }

  static bool ShouldUseStrictSerialLoadingDebounce() {
    if (!EnableStrictSerialLoadingDebounce) return false;
    if (!IsLoadingScreenStreamingContextActive()) return false;
    var memoryMb = Math.Max(SystemInfo.systemMemorySize, 0);
    return memoryMb > 0 && memoryMb <= 4096;
  }

  static void MaybeLogLoadingContextMode(int maxStarts, int maxInFlightLoads) {
    if (!IsLoadingScreenStreamingContextActive()) {
      loadingContextModeLogged = false;
      loadingContextModeReason = "";
      return;
    }
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return;
    if (!Application.isEditor && !Debug.isDebugBuild) return;

    var reason = string.IsNullOrWhiteSpace(SpriteStreamingLoadingState.ActiveReason)
      ? (StreamingWarmOrchestrator.IsWarmGateRunning ? "warm_gate" : "loading_overlay")
      : SpriteStreamingLoadingState.ActiveReason.Trim();
    if (loadingContextModeLogged &&
        string.Equals(loadingContextModeReason, reason, StringComparison.OrdinalIgnoreCase)) {
      return;
    }

    loadingContextModeLogged = true;
    loadingContextModeReason = reason;
    Debug.Log(
      "[TextureResidencyCache][OverlayMode] reason='" + reason + "'" +
      " serial_mode=" + (ShouldUseStrictSerialLoadingDebounce() ? 1 : 0) +
      " max_starts=" + Math.Max(maxStarts, 0) +
      " max_in_flight=" + Math.Max(maxInFlightLoads, 0) +
      " start_rate_s=" + ResolveOverlayStartRatePerSecond().ToString("0.0") +
      " burst_cap=" + ResolveOverlayStartBurstCap()
    );
  }

  static bool ShouldLogRequestFrameDiagnostics() {
    if (!enableRequestFrameDiagnostics) return false;
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!IsLoadingScreenStreamingContextActive()) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static bool ShouldLogAtlasExpansion() {
    if (!SpriteStreamingRuntimeSettings.EnableAtlasExpansionLogs) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static bool ShouldLogLoadCompletionDiagnostics() {
    if (!enableLoadCompletionDiagnostics) return false;
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!IsLoadingScreenStreamingContextActive()) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static bool ShouldMeasureLoadStartCosts() {
    if (!enableLoadStartDiagnostics) return false;
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!IsLoadingScreenStreamingContextActive()) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static float ResolveLoadStartSlowThresholdMs() {
    return Mathf.Max(loadStartSlowThresholdMs, 1f);
  }

  static void MaybeLogSlowLoadStart(string phase, string address, float startedAt, int locationCount) {
    if (!ShouldMeasureLoadStartCosts()) return;
    var elapsedMs = ComputeElapsedMs(startedAt);
    if (elapsedMs < ResolveLoadStartSlowThresholdMs()) return;
    Debug.LogWarning(
      "[TextureResidencyCache][LoadStartDiag] phase=" + (string.IsNullOrWhiteSpace(phase) ? "unknown" : phase.Trim()) +
      " start_ms=" + elapsedMs.ToString("0.0") +
      " queued=" + queuedEntryCount +
      " in_flight=" + inFlightLoads +
      " deferred=" + deferredRequests.Count +
      " locations=" + Math.Max(locationCount, 0) +
      " address='" + (address ?? "") + "'"
    );
  }

  static float ResolveLoadCompletionSlowThresholdMs() {
    return Mathf.Max(loadCompletionSlowStepThresholdMs, 1f);
  }

  static float ResolveLoadCompletionFrameSlowThresholdMs() {
    return Mathf.Max(ResolveLoadCompletionSlowThresholdMs() * 4f, 100f);
  }

  static bool IsCompletionPressureActive() {
    return Time.frameCount <= completionPressureUntilFrame;
  }

  static void UpdateCompletionPressureFromCosts(float totalMs, float registerMs, float maintainMs) {
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive) return;
    var slowThresholdMs = ResolveLoadCompletionSlowThresholdMs();
    if (totalMs < slowThresholdMs && registerMs < slowThresholdMs && maintainMs < slowThresholdMs) return;
    completionPressureUntilFrame = Math.Max(completionPressureUntilFrame, Time.frameCount + CompletionPressureCooldownFrames);
  }

  static float ComputeElapsedMs(float startedAt) {
    return Mathf.Max((Time.realtimeSinceStartup - startedAt) * 1000f, 0f);
  }

  static void RecordLoadCompletionFrameCost(float totalMs, float registerMs, float maintainMs, string address) {
    if (!ShouldLogLoadCompletionDiagnostics()) return;

    var frame = Time.frameCount;
    if (loadCompletionDiagFrame != frame) {
      loadCompletionDiagFrame = frame;
      loadCompletionDiagFrameTotalMs = 0f;
      loadCompletionDiagFrameRegisterMs = 0f;
      loadCompletionDiagFrameMaintainMs = 0f;
      loadCompletionDiagFrameCount = 0;
      loadCompletionDiagFrameReported = false;
    }

    loadCompletionDiagFrameTotalMs += Mathf.Max(totalMs, 0f);
    loadCompletionDiagFrameRegisterMs += Mathf.Max(registerMs, 0f);
    loadCompletionDiagFrameMaintainMs += Mathf.Max(maintainMs, 0f);
    loadCompletionDiagFrameCount++;

    if (loadCompletionDiagFrameReported) return;
    var thresholdMs = ResolveLoadCompletionFrameSlowThresholdMs();
    if (loadCompletionDiagFrameTotalMs < thresholdMs) return;

    loadCompletionDiagFrameReported = true;
    Debug.LogWarning(
      "[TextureResidencyCache][CompletionDiag] frame=" + frame +
      " total_ms=" + loadCompletionDiagFrameTotalMs.ToString("0.0") +
      " register_ms=" + loadCompletionDiagFrameRegisterMs.ToString("0.0") +
      " maintain_ms=" + loadCompletionDiagFrameMaintainMs.ToString("0.0") +
      " steps=" + loadCompletionDiagFrameCount +
      " queued=" + queuedEntryCount +
      " in_flight=" + inFlightLoads +
      " deferred=" + deferredRequests.Count +
      " address='" + (address ?? "") + "'"
    );

  }

  static bool ShouldLogLoadingScreenAddressableLoad() {
    if (!SpriteStreamingRuntimeSettings.EnableLoadingScreenLogs) return false;
    if (!SpriteStreamingRuntimeSettings.EnableAddressableLoadLogs) return false;
    var duringWarmGate = StreamingWarmOrchestrator.IsWarmGateRunning;
    var duringOverlay = SpriteStreamingRuntimeSettings.LogAddressableLoadsOutsideWarmGate &&
      SpriteStreamingLoadingState.IsLoadingOverlayActive;
    if (!duringWarmGate && !duringOverlay) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

}
