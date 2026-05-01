#if false
using System.Collections.Generic;
using UnityEngine;

namespace AddressableSpriteCacheAssetStreaming {

public static partial class TextureResidencyCache {

  public static void UpdateOwnerPins(string ownerId, PinClass pinClass, List<string> addresses, LoadPriority priority = LoadPriority.Warmup) {
    var normalizedOwnerId = NormalizeOwnerId(ownerId);
    if (string.IsNullOrWhiteSpace(normalizedOwnerId)) return;
    if (addresses == null || addresses.Count == 0) {
      ReleaseOwnerPins(normalizedOwnerId);
      return;
    }
    desiredOwnerAddressScratch.Clear();
    foreach (var addr in addresses) {
      var normalized = NormalizeAddress(addr);
      if (!string.IsNullOrWhiteSpace(normalized)) desiredOwnerAddressScratch.Add(normalized);
    }
    if (desiredOwnerAddressScratch.Count == 0) {
      desiredOwnerAddressScratch.Clear();
      ReleaseOwnerPins(normalizedOwnerId);
      return;
    }
    if (ownerPins.TryGetValue(normalizedOwnerId, out var existingState) && existingState != null &&
        AddressesMatchExistingLeases(existingState.leases, desiredOwnerAddressScratch)) {
      existingState.pinClass = pinClass;
      existingState.lastRefreshTicks = DateTime.UtcNow.Ticks;
      desiredOwnerAddressScratch.Clear();
      return;
    }
    ownerPinMutationDepth++;
    try {
      if (!ownerPins.TryGetValue(normalizedOwnerId, out var state) || state == null) {
        state = new OwnerPinState { ownerId = normalizedOwnerId, pinClass = pinClass, lastRefreshTicks = DateTime.UtcNow.Ticks };
        ownerPins[normalizedOwnerId] = state;
      }
      state.pinClass = pinClass;
      state.lastRefreshTicks = DateTime.UtcNow.Ticks;
      var classBudget = GetPinClassBudget(pinClass);
      var classBudgetHit = false;
      var classBudgetDropped = 0;
      ownerReleaseAddressScratch.Clear();
      foreach (var pair in state.leases) {
        if (!desiredOwnerAddressScratch.Contains(pair.Key)) ownerReleaseAddressScratch.Add(pair.Key);
      }
      foreach (var addr in ownerReleaseAddressScratch) {
        if (state.leases.TryGetValue(addr, out var lease)) {
          lease?.Release();
          state.leases.Remove(addr);
        }
      }
      if (classBudget > 0 && state.leases.Count > classBudget) {
        classBudgetHit = true;
        var overflow = state.leases.Count - classBudget;
        ownerReleaseAddressScratch.Clear();
        foreach (var key in state.leases.Keys) ownerReleaseAddressScratch.Add(key);
        for (var i = 0; i < ownerReleaseAddressScratch.Count && overflow > 0; i++) {
          if (state.leases.TryGetValue(ownerReleaseAddressScratch[i], out var trimLease)) {
            trimLease?.Release();
            state.leases.Remove(ownerReleaseAddressScratch[i]);
            overflow--;
            classBudgetDropped++;
          }
        }
      }
      foreach (var desiredAddress in desiredOwnerAddressScratch) {
        if (state.leases.ContainsKey(desiredAddress)) continue;
        if (!EnsurePinClassBudgetCapacity(pinClass, normalizedOwnerId, classBudget)) {
          classBudgetHit = true;
          classBudgetDropped++;
          continue;
        }
        var lease = AcquireAsyncNormalized(desiredAddress, desiredAddress, priority, false, "UpdateOwnerPins", false);
        if (lease != null) state.leases[desiredAddress] = lease;
      }
      if (state.leases.Count == 0) ownerPins.Remove(normalizedOwnerId);
      if (classBudgetHit) {
        pinClassBudgetHitCount++;
        pinClassBudgetDroppedAddresses += Math.Max(classBudgetDropped, 0);
        SpriteStreamingDiagnostics.RecordPinBudgetPressure(1, classBudgetDropped);
      }
    }
    finally {
      ownerReleaseAddressScratch.Clear();
      desiredOwnerAddressScratch.Clear();
      ownerPinMutationDepth = Math.Max(ownerPinMutationDepth - 1, 0);
    }
    Pump();
    MaintainBudget();
    RecordPinStateIfEnabled();
  }

  public static void ReleaseOwnerPins(string ownerId) {
    var normalizedOwnerId = NormalizeOwnerId(ownerId);
    if (string.IsNullOrWhiteSpace(normalizedOwnerId)) return;
    if (!ownerPins.TryGetValue(normalizedOwnerId, out var state) || state == null) return;
    ownerPinMutationDepth++;
    try {
      foreach (var lease in state.leases.Values) lease?.Release();
      state.leases.Clear();
      ownerPins.Remove(normalizedOwnerId);
    }
    finally {
      ownerPinMutationDepth = Math.Max(ownerPinMutationDepth - 1, 0);
    }
    MaintainBudget();
    RecordPinStateIfEnabled();
  }

  public static PinSnapshot GetPinSnapshot() {
    var pinnedOwnerCount = 0, pinnedAddressCount = 0, pinnedPlayerAddresses = 0, pinnedEnemyAddresses = 0, pinnedUiAddresses = 0, pinnedEffectAddresses = 0;
    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state == null) continue;
      var addressCount = state.leases.Count;
      if (addressCount <= 0) continue;
      pinnedOwnerCount++;
      pinnedAddressCount += addressCount;
      switch (state.pinClass) {
        case PinClass.Player: pinnedPlayerAddresses += addressCount; break;
        case PinClass.Enemy: pinnedEnemyAddresses += addressCount; break;
        case PinClass.UI: pinnedUiAddresses += addressCount; break;
        case PinClass.Effect: pinnedEffectAddresses += addressCount; break;
      }
    }
    return new PinSnapshot(pinnedOwnerCount, pinnedAddressCount, pinnedPlayerAddresses, pinnedEnemyAddresses, pinnedUiAddresses, pinnedEffectAddresses,
      Math.Max(pinDemotions, 0), Math.Max(pinClassBudgetHitCount, 0), Math.Max(pinClassBudgetDroppedAddresses, 0));
  }

  static int GetPinClassBudget(PinClass pinClass) {
    switch (pinClass) {
      case PinClass.Player: return Math.Max(SpriteStreamingRuntimeSettings.PinBudgetPlayerAddresses, 1);
      case PinClass.Enemy: return Math.Max(SpriteStreamingRuntimeSettings.PinBudgetEnemyAddresses, 1);
      case PinClass.UI: return Math.Max(SpriteStreamingRuntimeSettings.PinBudgetUiAddresses, 1);
      case PinClass.Effect: return Math.Max(SpriteStreamingRuntimeSettings.PinBudgetEffectAddresses, 1);
      case PinClass.WarmGate: return int.MaxValue;
      default: return int.MaxValue;
    }
  }

  static int CountPinnedAddressesForClass(PinClass pinClass) {
    var count = 0;
    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state != null && state.pinClass == pinClass) count += Math.Max(state.leases.Count, 0);
    }
    return count;
  }

  static bool EnsurePinClassBudgetCapacity(PinClass pinClass, string protectedOwnerId, int classBudget) {
    if (classBudget <= 0) return true;
    var used = CountPinnedAddressesForClass(pinClass);
    if (used < classBudget) return true;
    while (used >= classBudget) {
      if (!TryReleaseOldestLeaseFromClass(pinClass, protectedOwnerId)) return false;
      used--;
    }
    return true;
  }

  static bool TryReleaseOldestLeaseFromClass(PinClass pinClass, string protectedOwnerId) {
    OwnerPinState ownerCandidate = null;
    string leaseKey = null;
    long oldestTicks = long.MaxValue;
    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state == null || state.pinClass != pinClass || state.leases == null || state.leases.Count <= 0) continue;
      if (string.Equals(state.ownerId, protectedOwnerId, StringComparison.OrdinalIgnoreCase)) continue;
      if (state.lastRefreshTicks > oldestTicks) continue;
      foreach (var leasePair in state.leases) {
        leaseKey = leasePair.Key;
        break;
      }
      if (!string.IsNullOrWhiteSpace(leaseKey)) {
        oldestTicks = state.lastRefreshTicks;
        ownerCandidate = state;
      }
    }
    if (ownerCandidate == null || string.IsNullOrWhiteSpace(leaseKey)) return false;
    if (!ownerCandidate.leases.TryGetValue(leaseKey, out var lease) || lease == null) return false;
    lease.Release();
    ownerCandidate.leases.Remove(leaseKey);
    if (ownerCandidate.leases.Count <= 0) ownerPins.Remove(ownerCandidate.ownerId);
    return true;
  }

  static bool AddressesMatchExistingLeases(Dictionary<string, Lease> existingLeases, HashSet<string> normalizedAddresses) {
    if (existingLeases == null || normalizedAddresses == null || existingLeases.Count != normalizedAddresses.Count) return false;
    foreach (var address in normalizedAddresses) {
      if (!existingLeases.ContainsKey(address)) return false;
    }
    return true;
  }

  static void RecordPinStateIfEnabled() {
    if (!SpriteStreamingDiagnostics.Enabled) return;
    SpriteStreamingDiagnostics.RecordPinState(GetPinSnapshot());
  }

}
}
#endif

