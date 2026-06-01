using System;
using System.Collections.Generic;
using UnityEngine;

public static class SpriteUiPinService {
  static readonly HashSet<SpriteWithNormals> registeredTargets = new();
  static readonly HashSet<string> knownOwnerIds = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> activeOwnerIds = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<SpriteWithNormals> staleTargets = new();
  static readonly List<string> staleOwnerIds = new();
  static readonly List<string> addressBuffer = new(128);
  static readonly HashSet<string> addressSetBuffer = new(StringComparer.OrdinalIgnoreCase);
  static SpriteUiPinServiceRunner runner;
  static float nextRefreshTime;
  static bool enableUiPinDiagnostics = true;
  static float uiPinSlowStepThresholdMs = 50f;
  const int LoadingOverlayUiPinAddressCap = 128;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    registeredTargets.Clear();
    knownOwnerIds.Clear();
    activeOwnerIds.Clear();
    staleTargets.Clear();
    staleOwnerIds.Clear();
    addressBuffer.Clear();
    addressSetBuffer.Clear();
    runner = null;
    nextRefreshTime = 0f;
  }

  public static void Register(SpriteWithNormals target) {
    if (target == null) return;
    if (!target.IsUiTarget()) return;
    registeredTargets.Add(target);
    EnsureRunner();
  }

  public static void Unregister(SpriteWithNormals target) {
    if (target == null) return;
    registeredTargets.Remove(target);
    var ownerId = BuildOwnerId(target);
    ReleaseOwner(ownerId);
  }

  static void EnsureRunner() {
    if (!Application.isPlaying) return;
    if (runner != null) return;

    var go = new GameObject("SpriteUiPinServiceRunner") { hideFlags = HideFlags.HideAndDontSave };
    UnityEngine.Object.DontDestroyOnLoad(go);
    runner = go.AddComponent<SpriteUiPinServiceRunner>();
  }

  internal static void Tick() {
    if (!Application.isPlaying) return;
    var diagnosticsEnabled = ShouldLogUiPinDiagnostics();
    var tickStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
    var processedTargets = 0;
    var slowTargetCount = 0;

    var streamingEnabled = SpriteStreamingRuntimeSettings.EnableAppearanceSetStreaming &&
                          SpriteStreamingRuntimeSettings.EnablePinnedHotset &&
                          SpriteStreamingRuntimeSettings.PinAllUi;

    if (!streamingEnabled) {
      var releaseStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
      ReleaseAllKnownOwners();
      if (diagnosticsEnabled) {
        var releaseMs = ComputeElapsedMs(releaseStartedAt);
        if (releaseMs >= ResolveUiPinSlowThresholdMs()) {

        }
      }
      return;
    }

    var refreshSeconds = Mathf.Max(SpriteStreamingRuntimeSettings.UiPinRefreshMs, 16) / 1000f;
    var maxPinAddresses = SpriteStreamingRuntimeSettings.MaxPinnedAddressesPerOwner;
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      maxPinAddresses = Mathf.Max(Mathf.Min(maxPinAddresses, LoadingOverlayUiPinAddressCap), 32);
    }
    var now = Time.unscaledTime;
    if (now < nextRefreshTime) return;
    nextRefreshTime = now + refreshSeconds;

    activeOwnerIds.Clear();
    staleTargets.Clear();

    foreach (var target in registeredTargets) {
      var targetStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
      var collectMs = 0f;
      var updatePinsMs = 0f;
      if (target == null) {
        staleTargets.Add(target);
        continue;
      }

      var ownerId = BuildOwnerId(target);
      if (string.IsNullOrWhiteSpace(ownerId)) continue;
      processedTargets++;
      if (!target.isActiveAndEnabled || !target.IsUiTarget() || target.DoNotRender) {
        ReleaseOwner(ownerId);
        staleTargets.Add(target);
        continue;
      }

      addressBuffer.Clear();
      addressSetBuffer.Clear();
      if (target.IsAnimation) {
        var collectStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
        var startFrame = Mathf.Max(target.LastRequestedFrame, 1);
        var lookAhead = Mathf.Max(SpriteStreamingRuntimeSettings.PinWindowFrames - 1, 0);
        target.CollectAnimationWindowAddresses(
          categoryOverride: null,
          startFrame: startFrame,
          endFrame: startFrame,
          lookAheadFrames: lookAhead,
          outAddresses: addressBuffer,
          seenAddresses: addressSetBuffer,
          maxUniqueAddresses: maxPinAddresses
        );
        if (diagnosticsEnabled) {
          collectMs = ComputeElapsedMs(collectStartedAt);
        }
      }
      else if (target.TryGetFrameAddressPair(0, out var pair)) {
        AddAddress(addressBuffer, pair.StreamingColorAddress, addressSetBuffer);
        AddAddress(addressBuffer, pair.StreamingNormalAddress, addressSetBuffer);
      }

      if (addressBuffer.Count <= 0) {
        ReleaseOwner(ownerId);
        continue;
      }

      var updatePinsStartedAt = diagnosticsEnabled ? Time.realtimeSinceStartup : 0f;
      TextureResidencyCache.UpdateOwnerPins(ownerId, TextureResidencyCache.PinClass.UI, addressBuffer, TextureResidencyCache.LoadPriority.Warmup);
      if (diagnosticsEnabled) {
        updatePinsMs = ComputeElapsedMs(updatePinsStartedAt);
      }
      activeOwnerIds.Add(ownerId);
      knownOwnerIds.Add(ownerId);

      if (diagnosticsEnabled) {
        var targetMs = ComputeElapsedMs(targetStartedAt);
        var thresholdMs = ResolveUiPinSlowThresholdMs();
        if (targetMs >= thresholdMs || collectMs >= thresholdMs || updatePinsMs >= thresholdMs) {
          slowTargetCount++;

        }
      }
    }

    for (var i = 0; i < staleTargets.Count; i++) {
      registeredTargets.Remove(staleTargets[i]);
    }

    staleOwnerIds.Clear();
    foreach (var ownerId in knownOwnerIds) {
      if (activeOwnerIds.Contains(ownerId)) continue;
      staleOwnerIds.Add(ownerId);
    }

    for (var i = 0; i < staleOwnerIds.Count; i++) {
      ReleaseOwner(staleOwnerIds[i]);
    }
    staleOwnerIds.Clear();

    if (diagnosticsEnabled) {
      var tickMs = ComputeElapsedMs(tickStartedAt);
      if (tickMs >= ResolveUiPinSlowThresholdMs()) {

      }
    }
  }

  static void ReleaseAllKnownOwners() {
    if (knownOwnerIds.Count <= 0) return;
    var owners = new List<string>(knownOwnerIds);
    for (var i = 0; i < owners.Count; i++) {
      TextureResidencyCache.ReleaseOwnerPins(owners[i]);
    }
    knownOwnerIds.Clear();
    activeOwnerIds.Clear();
  }

  static string BuildOwnerId(SpriteWithNormals target) {
    if (target == null) return "";
    return "ui:" + ObjectEntityId.GetString(target);
  }

  static void ReleaseOwner(string ownerId) {
    if (string.IsNullOrWhiteSpace(ownerId)) return;
    TextureResidencyCache.ReleaseOwnerPins(ownerId);
    knownOwnerIds.Remove(ownerId);
    activeOwnerIds.Remove(ownerId);
  }

  static void AddAddress(List<string> addresses, string address, HashSet<string> seenAddresses = null) {
    var normalized = string.IsNullOrWhiteSpace(address) ? "" : address.Trim();
    if (string.IsNullOrWhiteSpace(normalized)) return;
    if (seenAddresses != null) {
      if (!seenAddresses.Add(normalized)) return;
      addresses.Add(normalized);
      return;
    }
    for (var i = 0; i < addresses.Count; i++) {
      if (string.Equals(addresses[i], normalized, StringComparison.OrdinalIgnoreCase)) return;
    }
    addresses.Add(normalized);
  }

  static bool ShouldLogUiPinDiagnostics() {
    if (!enableUiPinDiagnostics) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static float ResolveUiPinSlowThresholdMs() {
    return Mathf.Max(uiPinSlowStepThresholdMs, 1f);
  }

  static float ComputeElapsedMs(float startedAt) {
    return Mathf.Max((Time.realtimeSinceStartup - startedAt) * 1000f, 0f);
  }
}

sealed class SpriteUiPinServiceRunner : MonoBehaviour {
  void Update() {
    SpriteUiPinService.Tick();
  }
}
