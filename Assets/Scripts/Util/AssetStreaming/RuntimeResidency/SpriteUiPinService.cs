using System;
using System.Collections.Generic;
using UnityEngine;

public static class SpriteUiPinService {
  static readonly HashSet<SpriteWithNormals> registeredTargets = new();
  static readonly Dictionary<SpriteWithNormals, string> ownerIdsByTarget = new();
  static readonly HashSet<string> knownOwnerIds = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> activeOwnerIds = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<SpriteWithNormals> staleTargets = new();
  static readonly List<string> staleOwnerIds = new();
  static readonly List<string> addressBuffer = new(128);
  static readonly HashSet<string> addressSetBuffer = new(StringComparer.OrdinalIgnoreCase);
  static SpriteUiPinServiceRunner runner;
  static float nextRefreshTime;
  const int LoadingOverlayUiPinAddressCap = 128;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    registeredTargets.Clear();
    ownerIdsByTarget.Clear();
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
    if (registeredTargets.Add(target)) {
      ownerIdsByTarget[target] = BuildOwnerId(target);
    }
    EnsureRunner();
  }

  public static void Unregister(SpriteWithNormals target) {
    if (ReferenceEquals(target, null)) return;
    registeredTargets.Remove(target);
    if (!ownerIdsByTarget.TryGetValue(target, out var ownerId)) {
      ownerId = BuildOwnerId(target);
    }
    ownerIdsByTarget.Remove(target);
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

    var streamingEnabled = SpriteStreamingRuntimeSettings.EnableAppearanceSetStreaming &&
                          SpriteStreamingRuntimeSettings.EnablePinnedHotset &&
                          SpriteStreamingRuntimeSettings.PinAllUi;

    if (!streamingEnabled) {
      ReleaseAllKnownOwners();
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
      ownerIdsByTarget.TryGetValue(target, out var ownerId);
      if (target == null) {
        staleTargets.Add(target);
        continue;
      }

      if (string.IsNullOrWhiteSpace(ownerId)) {
        ownerId = BuildOwnerId(target);
        ownerIdsByTarget[target] = ownerId;
      }
      if (string.IsNullOrWhiteSpace(ownerId)) continue;
      if (!target.isActiveAndEnabled || !target.IsUiTarget() || target.DoNotRender) {
        ReleaseOwner(ownerId);
        staleTargets.Add(target);
        continue;
      }

      addressBuffer.Clear();
      addressSetBuffer.Clear();
      if (target.IsAnimation) {
        var startFrame = Mathf.Max(target.LastRequestedFrame, 1);
        var lookAhead = Mathf.Max(SpriteStreamingRuntimeSettings.PinWindowFrames - 1, 0);
        target.CollectAnimationAtlasAddresses(
          categoryOverride: null,
          startFrame: startFrame,
          endFrame: startFrame + lookAhead,
          outAddresses: addressBuffer,
          seenAddresses: addressSetBuffer,
          maxUniqueAddresses: maxPinAddresses
        );
      }
      else if (target.TryGetFrameAddressPair(0, out var pair)) {
        AddAddress(addressBuffer, pair.StreamingColorAddress, addressSetBuffer);
        AddAddress(addressBuffer, pair.StreamingNormalAddress, addressSetBuffer);
      }

      if (addressBuffer.Count <= 0) {
        ReleaseOwner(ownerId);
        continue;
      }

      TextureResidencyCache.UpdateOwnerPins(ownerId, TextureResidencyCache.PinClass.UI, addressBuffer, TextureResidencyCache.LoadPriority.Warmup);
      activeOwnerIds.Add(ownerId);
      knownOwnerIds.Add(ownerId);
    }

    for (var i = 0; i < staleTargets.Count; i++) {
      var staleTarget = staleTargets[i];
      registeredTargets.Remove(staleTarget);
      ownerIdsByTarget.Remove(staleTarget);
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

  }

  static void ReleaseAllKnownOwners() {
    if (knownOwnerIds.Count <= 0) return;
    staleOwnerIds.Clear();
    foreach (var ownerId in knownOwnerIds) {
      staleOwnerIds.Add(ownerId);
    }
    for (var i = 0; i < staleOwnerIds.Count; i++) {
      TextureResidencyCache.ReleaseOwnerPins(staleOwnerIds[i]);
    }
    staleOwnerIds.Clear();
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

}

sealed class SpriteUiPinServiceRunner : MonoBehaviour {
  void Update() {
    SpriteUiPinService.Tick();
  }
}
