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
  static readonly List<SpriteWithNormals> refreshTargets = new(128);
  static readonly HashSet<SpriteWithNormals> refreshTargetSet = new();
  static bool updateRegistered;
  static readonly Action updateCallback = Tick;
  static float nextRefreshTime;
  static int refreshTargetIndex;
  static int refreshMaxPinAddresses;
  static bool refreshInProgress;
  const int LoadingOverlayUiPinAddressCap = 128;
  // A large virtualized UI can register hundreds of sprite targets at once.
  // Spread pin acquisition across frames so opening it cannot stall the main thread.
  const int UiPinTargetsPerFrame = 8;

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
    refreshTargets.Clear();
    refreshTargetSet.Clear();
    updateRegistered = false;
    nextRefreshTime = 0f;
    refreshTargetIndex = 0;
    refreshMaxPinAddresses = 0;
    refreshInProgress = false;
  }

  public static void Register(SpriteWithNormals target) {
    if (target == null) return;
    if (!target.IsUiTarget()) return;
    if (registeredTargets.Add(target)) {
      ownerIdsByTarget[target] = BuildOwnerId(target);
      if (refreshInProgress && refreshTargetSet.Add(target)) {
        refreshTargets.Add(target);
      }
    }
    EnsureUpdateRegistration();
  }

  public static void Unregister(SpriteWithNormals target) {
    if (ReferenceEquals(target, null)) return;
    registeredTargets.Remove(target);
    refreshTargetSet.Remove(target);
    if (!ownerIdsByTarget.TryGetValue(target, out var ownerId)) {
      ownerId = BuildOwnerId(target);
    }
    ownerIdsByTarget.Remove(target);
    ReleaseOwner(ownerId);
  }

  static void EnsureUpdateRegistration() {
    if (!Application.isPlaying) return;
    if (updateRegistered) return;
    updateRegistered = true;
    RuntimeUpdateHub.Register(
      500,
      "RuntimeUpdateHub.SpriteUiPins",
      updateCallback
    );
  }

  internal static void Tick() {
    if (!Application.isPlaying) return;

    var streamingEnabled = SpriteStreamingRuntimeSettings.EnableAppearanceSetStreaming &&
                          SpriteStreamingRuntimeSettings.EnablePinnedHotset &&
                          SpriteStreamingRuntimeSettings.PinAllUi;

    if (!streamingEnabled) {
      ResetRefreshState();
      ReleaseAllKnownOwners();
      return;
    }

    var refreshSeconds = Mathf.Max(SpriteStreamingRuntimeSettings.UiPinRefreshMs, 16) / 1000f;
    var maxPinAddresses = SpriteStreamingRuntimeSettings.MaxPinnedAddressesPerOwner;
    if (SpriteStreamingLoadingState.IsLoadingOverlayActive || StreamingWarmOrchestrator.IsWarmGateRunning) {
      maxPinAddresses = Mathf.Max(Mathf.Min(maxPinAddresses, LoadingOverlayUiPinAddressCap), 32);
    }
    var now = Time.unscaledTime;
    if (!refreshInProgress) {
      if (now < nextRefreshTime) return;
      BeginRefresh(maxPinAddresses);
    }

    ProcessRefreshTargets();
    if (!refreshInProgress) {
      nextRefreshTime = Time.unscaledTime + refreshSeconds;
    }
  }

  static void BeginRefresh(int maxPinAddresses) {
    activeOwnerIds.Clear();
    staleTargets.Clear();
    refreshTargets.Clear();
    refreshTargetSet.Clear();

    foreach (var target in registeredTargets) {
      refreshTargets.Add(target);
      refreshTargetSet.Add(target);
    }

    refreshTargetIndex = 0;
    refreshMaxPinAddresses = maxPinAddresses;
    refreshInProgress = true;
  }

  static void ProcessRefreshTargets() {
    var processedCount = 0;
    while (processedCount < UiPinTargetsPerFrame && refreshTargetIndex < refreshTargets.Count) {
      var target = refreshTargets[refreshTargetIndex++];
      ProcessTarget(target, refreshMaxPinAddresses);
      processedCount++;
    }

    if (refreshTargetIndex < refreshTargets.Count) return;

    FinishRefresh();
  }

  static void ProcessTarget(SpriteWithNormals target, int maxPinAddresses) {
    if (target == null) {
      staleTargets.Add(target);
      return;
    }

    if (!registeredTargets.Contains(target)) return;

    ownerIdsByTarget.TryGetValue(target, out var ownerId);
    if (string.IsNullOrWhiteSpace(ownerId)) {
      ownerId = BuildOwnerId(target);
      ownerIdsByTarget[target] = ownerId;
    }
    if (string.IsNullOrWhiteSpace(ownerId)) return;

    if (!target.isActiveAndEnabled || !target.IsUiTarget() || target.DoNotRender) {
      ReleaseOwner(ownerId);
      return;
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
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(ownerId, TextureResidencyCache.PinClass.UI, addressBuffer, TextureResidencyCache.LoadPriority.Warmup);
    activeOwnerIds.Add(ownerId);
    knownOwnerIds.Add(ownerId);
  }

  static void FinishRefresh() {
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

    ResetRefreshState();
  }

  static void ResetRefreshState() {
    refreshTargets.Clear();
    refreshTargetSet.Clear();
    staleTargets.Clear();
    refreshTargetIndex = 0;
    refreshMaxPinAddresses = 0;
    refreshInProgress = false;
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
