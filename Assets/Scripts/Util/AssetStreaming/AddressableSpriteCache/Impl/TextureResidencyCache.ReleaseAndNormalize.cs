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
  const int MaxOwnerAddressCacheEntries = 65536;
  static void RegisterTextureContribution(CacheEntry entry) {
    if (entry == null || entry.hasTextureRegistration) return;
    if (!entry.isDone || !entry.isSuccess || entry.primarySprite == null) return;
    entry.registeredTextureIds.Clear();
    RegisterTextureForEntry(entry, entry.primarySprite);
    entry.hasTextureRegistration = entry.registeredTextureIds.Count > 0;
  }

  static void UnregisterTextureContribution(CacheEntry entry) {
    if (entry == null || !entry.hasTextureRegistration) return;
    entry.hasTextureRegistration = false;
    foreach (var textureId in entry.registeredTextureIds) {
      if (!textureRefCounts.TryGetValue(textureId, out var refs) || refs <= 0) continue;

      refs--;
      if (refs > 0) {
        textureRefCounts[textureId] = refs;
        continue;
      }

      textureRefCounts.Remove(textureId);
      if (!textureBytesById.TryGetValue(textureId, out var bytes)) continue;
      textureBytesById.Remove(textureId);
      residentBytes -= bytes;
      if (residentBytes < 0) residentBytes = 0;
    }
    entry.registeredTextureIds.Clear();
  }

  static void RegisterTextureForEntry(CacheEntry entry, Sprite sprite) {
    if (entry == null || sprite == null) return;
    var texture = sprite.texture;
    if (texture == null) return;

    var textureId = ObjectEntityId.GetRawValue(texture);
    if (!entry.registeredTextureIds.Add(textureId)) return;

    if (!textureRefCounts.TryGetValue(textureId, out var refs) || refs <= 0) {
      textureRefCounts[textureId] = 1;
      var bytes = EstimateTextureBytes(texture);
      textureBytesById[textureId] = bytes;
      residentBytes += bytes;
      return;
    }

    textureRefCounts[textureId] = refs + 1;
  }

  static long EstimateTextureBytes(Texture texture) {
    if (texture == null) return 0;
    var width = Math.Max(texture.width, 1);
    var height = Math.Max(texture.height, 1);
    return width * height * 4L;
  }

  static void Evict(string key, CacheEntry entry) {
    if (entry == null || entry.isEvicted) return;
    if (cache.TryGetValue(key, out var current) && !ReferenceEquals(current, entry)) return;
    if (entry.isQueued && sessionExpectedTotal > 0 && sessionGeneration > 0 && sessionTotalScheduled > 0) {
      sessionTotalScheduled--;
    }
    cache.Remove(key);
    RecordAssetTraceRelease(entry, "evict");
    entry.isEvicted = true;
    ClearQueuedFlag(entry);
    UnregisterTextureContribution(entry);
    ReleaseHandle(entry);
    SpriteStreamingDiagnostics.RecordQueueState(queuedEntryCount, inFlightLoads);
  }

  static void ReleaseHandle(CacheEntry entry) {
    if (entry == null) return;
    MarkInFlightComplete(entry);
    GeneratedAtlasSpriteSynthesisUtility.DestroySprites(entry.generatedSprites);
    if (entry.handle.IsValid()) {
      Addressables.Release(entry.handle);
    }
    if (entry.groupedSingleSpriteHandle.IsValid()) {
      Addressables.Release(entry.groupedSingleSpriteHandle);
    }
    if (entry.groupedAtlasTextureHandle.IsValid()) {
      Addressables.Release(entry.groupedAtlasTextureHandle);
    }
    if (entry.groupedMetadataHandle.IsValid()) {
      Addressables.Release(entry.groupedMetadataHandle);
    }
    if (entry.metadataAtlasTextureHandle.IsValid()) {
      Addressables.Release(entry.metadataAtlasTextureHandle);
    }
    if (entry.metadataAtlasMetadataHandle.IsValid()) {
      Addressables.Release(entry.metadataAtlasMetadataHandle);
    }
    if (entry.exactSliceSupplementHandles != null) {
      for (var i = 0; i < entry.exactSliceSupplementHandles.Count; i++) {
        var supplementHandle = entry.exactSliceSupplementHandles[i];
        if (supplementHandle.IsValid()) {
          Addressables.Release(supplementHandle);
        }
      }
      entry.exactSliceSupplementHandles.Clear();
    }
    ReleaseLocationHandle(entry);

    entry.handle = default;
    entry.groupedSingleSpriteHandle = default;
    entry.groupedAtlasTextureHandle = default;
    entry.groupedMetadataHandle = default;
    entry.metadataAtlasTextureHandle = default;
    entry.metadataAtlasMetadataHandle = default;
    ClearPendingAssetLoadStart(entry);
    if (entry.pendingExactSliceSupplementAddresses != null) {
      entry.pendingExactSliceSupplementAddresses.Clear();
    }
    if (entry.failedExactSliceSupplementAddresses != null) {
      entry.failedExactSliceSupplementAddresses.Clear();
    }
    entry.primarySprite = null;
    if (entry.spritesByName != null) {
      entry.spritesByName.Clear();
    }
    entry.spriteMapMaterialized = false;
    entry.deferredSpriteMapMaterialization = false;
    entry.generatedSpriteSetComplete = false;
    entry.isDone = false;
    entry.isSuccess = false;
    entry.hasTextureRegistration = false;
    if (entry.registeredTextureIds != null) {
      entry.registeredTextureIds.Clear();
    }
    entry.loadStarted = false;
    entry.countedInFlight = false;
    entry.atlasFallbackToDirect = false;
    entry.atlasDirectFallbackAttempted = false;
    entry.requestedSpriteNameHint = "";
    entry.requestedSpriteNameConflict = false;
    entry.parsedGroupedMetadata = null;
    entry.parsedMetadataAtlasMetadata = null;
    entry.groupedPayloadsByName = null;
    entry.metadataPayloadsByName = null;
    ClearPendingLoadFinalize(entry);
    ClearQueuedFlag(entry);
  }

  static CacheSettings GetSettings() {
    if (settingsLoaded) return settings;
    settingsLoaded = true;
    settings = new CacheSettings {
      softTextureBudgetBytes = 1024L * 1024L * 1024L,
      hardTextureBudgetBytes = 1536L * 1024L * 1024L,
      maxAddressableStartsPerFrame = 16,
      loadingOverlayMaxAddressableStartsPerFrame = 24
    };

    var settingsAsset = SpriteStreamingRuntimeSettings.Asset;
    if (settingsAsset != null) {
      settings.softTextureBudgetBytes = settingsAsset.SoftTextureBudgetBytes;
      settings.hardTextureBudgetBytes = settingsAsset.HardTextureBudgetBytes;
      settings.maxAddressableStartsPerFrame = Math.Max(SpriteStreamingRuntimeSettings.MaxAddressableStartsPerFrame, 1);
    }

    var overlayConfigured = SpriteStreamingRuntimeSettings.LoadingOverlayMaxAddressableStartsPerFrame;

    if (overlayConfigured <= 0) overlayConfigured = 24;

    settings.loadingOverlayMaxAddressableStartsPerFrame = Math.Max(
      overlayConfigured,
      settings.maxAddressableStartsPerFrame
    );

    return settings;
  }

  static bool TryGetSpriteFromEntryWithoutMaterialization(CacheEntry entry, string spriteName, out Sprite sprite) {
    sprite = null;
    if (entry == null || !entry.isDone || !entry.isSuccess) return false;
    if (string.IsNullOrWhiteSpace(spriteName)) {
      sprite = entry.primarySprite;
      return sprite != null;
    }

    var normalizedName = spriteName.Trim();
    if (entry.spritesByName.TryGetValue(normalizedName, out sprite) && sprite != null) {
      return true;
    }

    if (TryCreateSpriteLazily(entry, normalizedName, out sprite)) {
      return true;
    }

    if (!entry.spriteMapMaterialized) return false;
    if (!SpriteSliceAddressUtility.CanUseNumericLabelFallback(normalizedName)) return false;
    if (!SpriteSliceAddressUtility.TryExtractNumericLabelValue(normalizedName, out var numericLabelValue)) return false;
    return TryGetSpriteByNumericLabelWithoutMaterialization(entry, numericLabelValue, out sprite);
  }

  static bool TryGetSpriteByNumericLabelWithoutMaterialization(CacheEntry entry, string numericLabelValue, out Sprite sprite) {
    sprite = null;
    if (entry == null || string.IsNullOrWhiteSpace(numericLabelValue)) return false;

    if (TryCreateSpriteByNumericLabelLazily(entry, numericLabelValue, out sprite)) {
      return true;
    }

    Sprite match = null;
    foreach (var pair in entry.spritesByName) {
      if (!SpriteSliceAddressUtility.TryExtractNumericLabelValue(pair.Key, out var candidateNumericValue)) continue;
      if (!string.Equals(candidateNumericValue, numericLabelValue, StringComparison.Ordinal)) continue;
      if (match != null && match != pair.Value) {
        return false;
      }

      match = pair.Value;
    }

    sprite = match;
    return sprite != null;
  }

  static bool TryGetSpriteFromEntry(CacheEntry entry, string spriteName, out Sprite sprite) {
    sprite = null;
    if (entry == null || !entry.isDone || !entry.isSuccess) return false;
    if (string.IsNullOrWhiteSpace(spriteName)) {
      sprite = entry.primarySprite;
      return sprite != null;
    }
    var normalizedName = spriteName.Trim();
    if (entry.spritesByName.TryGetValue(normalizedName, out sprite) && sprite != null) {
      return true;
    }

    if (TryCreateSpriteLazily(entry, normalizedName, out sprite)) {
      return true;
    }

    if (!entry.spriteMapMaterialized && TryEnsureEntrySpriteMapMaterialized(entry)) {
      if (entry.spritesByName.TryGetValue(normalizedName, out sprite) && sprite != null) {
        return true;
      }
    }

    if (!SpriteSliceAddressUtility.CanUseNumericLabelFallback(normalizedName)) return false;
    if (!SpriteSliceAddressUtility.TryExtractNumericLabelValue(normalizedName, out var numericLabelValue)) return false;

    if (TryCreateSpriteByNumericLabelLazily(entry, numericLabelValue, out sprite)) {
      return true;
    }

    return TryGetSpriteByNumericLabel(entry, numericLabelValue, out sprite);
  }

  static bool TryGetSpriteByNumericLabel(CacheEntry entry, string numericLabelValue, out Sprite sprite) {
    sprite = null;
    if (entry == null) return false;
    if (entry.spritesByName == null || entry.spritesByName.Count <= 0) {
      if (!TryEnsureEntrySpriteMapMaterialized(entry)) return false;
    }
    if (string.IsNullOrWhiteSpace(numericLabelValue)) return false;

    Sprite match = null;
    foreach (var pair in entry.spritesByName) {
      if (!SpriteSliceAddressUtility.TryExtractNumericLabelValue(pair.Key, out var candidateNumericValue)) continue;
      if (!string.Equals(candidateNumericValue, numericLabelValue, StringComparison.Ordinal)) continue;

      if (match != null && match != pair.Value) {
        sprite = null;
        return false;
      }

      match = pair.Value;
    }

    sprite = match;
    return sprite != null;
  }

  static bool TryCreateSpriteLazily(CacheEntry entry, string spriteName, out Sprite sprite) {
    sprite = null;
    if (entry == null || string.IsNullOrWhiteSpace(spriteName)) return false;

    var normalizedName = spriteName.Trim();
    if (entry.parsedGroupedMetadata != null && entry.groupedAtlasTextureHandle.IsValid()) {
      var texture = entry.groupedAtlasTextureHandle.Result;
      if (texture == null) return false;
      var payload = FindSpritePayloadByName(
        entry.groupedPayloadsByName,
        entry.parsedGroupedMetadata.sprites,
        normalizedName
      );
      if (payload != null) {
        var pixelsPerUnit = entry.parsedGroupedMetadata.spritePixelsPerUnit > 0f ? entry.parsedGroupedMetadata.spritePixelsPerUnit : 100f;
        var meshType = GeneratedAtlasSpriteSynthesisUtility.ResolveMeshType(entry.parsedGroupedMetadata.spriteMeshType, SpriteMeshType.FullRect);
        sprite = GeneratedAtlasSpriteSynthesisUtility.CreateSpriteFromPayload(texture, payload, pixelsPerUnit, meshType);
        if (sprite != null) {
          entry.spritesByName[normalizedName] = sprite;
          entry.generatedSprites.Add(sprite);
          return true;
        }
      }
    }

    if (entry.parsedMetadataAtlasMetadata != null && entry.metadataAtlasTextureHandle.IsValid()) {
      var texture = entry.metadataAtlasTextureHandle.Result;
      if (texture == null) return false;
      var payload = FindSpritePayloadByName(
        entry.metadataPayloadsByName,
        entry.parsedMetadataAtlasMetadata.sprites,
        normalizedName
      );
      if (payload != null) {
        var pixelsPerUnit = entry.parsedMetadataAtlasMetadata.spritePixelsPerUnit > 0f ? entry.parsedMetadataAtlasMetadata.spritePixelsPerUnit : 100f;
        var meshType = GeneratedAtlasSpriteSynthesisUtility.ResolveMeshType(entry.parsedMetadataAtlasMetadata.spriteMeshType, SpriteMeshType.FullRect);
        sprite = GeneratedAtlasSpriteSynthesisUtility.CreateSpriteFromPayload(texture, payload, pixelsPerUnit, meshType);
        if (sprite != null) {
          entry.spritesByName[normalizedName] = sprite;
          entry.generatedSprites.Add(sprite);
          return true;
        }
      }
    }

    return false;
  }

  static GeneratedAtlasSpriteSynthesisUtility.AtlasSpriteImportPayload FindSpritePayloadByName(
    Dictionary<string, GeneratedAtlasSpriteSynthesisUtility.AtlasSpriteImportPayload> payloadsByName,
    List<GeneratedAtlasSpriteSynthesisUtility.AtlasSpriteImportPayload> sprites,
    string normalizedName
  ) {
    if (string.IsNullOrWhiteSpace(normalizedName)) return null;
    if (payloadsByName != null && payloadsByName.TryGetValue(normalizedName, out var payload)) {
      return payload;
    }
    if (sprites == null) return null;

    for (var i = 0; i < sprites.Count; i++) {
      var candidate = sprites[i];
      if (candidate == null) continue;
      var candidateName = candidate.name ?? "";
      if (!string.Equals(candidateName.Trim(), normalizedName, StringComparison.Ordinal)) continue;
      return candidate;
    }

    return null;
  }

  static Dictionary<string, GeneratedAtlasSpriteSynthesisUtility.AtlasSpriteImportPayload> BuildSpritePayloadLookup(
    List<GeneratedAtlasSpriteSynthesisUtility.AtlasSpriteImportPayload> sprites
  ) {
    var capacity = sprites != null ? sprites.Count : 0;
    var payloadsByName = new Dictionary<string, GeneratedAtlasSpriteSynthesisUtility.AtlasSpriteImportPayload>(
      capacity,
      StringComparer.Ordinal
    );
    if (sprites == null) return payloadsByName;

    for (var i = 0; i < sprites.Count; i++) {
      var payload = sprites[i];
      if (payload == null || string.IsNullOrWhiteSpace(payload.name)) continue;
      var normalizedName = payload.name.Trim();
      if (payloadsByName.ContainsKey(normalizedName)) continue;
      payloadsByName[normalizedName] = payload;
    }

    return payloadsByName;
  }

  static bool TryCreateSpriteByNumericLabelLazily(CacheEntry entry, string numericLabelValue, out Sprite sprite) {
    sprite = null;
    if (entry == null || string.IsNullOrWhiteSpace(numericLabelValue)) return false;

    if (entry.parsedGroupedMetadata != null && entry.groupedAtlasTextureHandle.IsValid()) {
      var texture = entry.groupedAtlasTextureHandle.Result;
      if (texture == null) return false;

      GeneratedAtlasSpriteSynthesisUtility.AtlasSpriteImportPayload match = null;
      for (var i = 0; i < entry.parsedGroupedMetadata.sprites.Count; i++) {
        var s = entry.parsedGroupedMetadata.sprites[i];
        if (s == null || string.IsNullOrWhiteSpace(s.name)) continue;
        if (!SpriteSliceAddressUtility.TryExtractNumericLabelValue(s.name, out var candidateNumericValue)) continue;
        if (!string.Equals(candidateNumericValue, numericLabelValue, StringComparison.Ordinal)) continue;

        if (match != null && match != s) {
          return false;
        }
        match = s;
      }

      if (match != null) {
        var normalizedName = match.name.Trim();
        if (entry.spritesByName.TryGetValue(normalizedName, out sprite) && sprite != null) {
          return true;
        }
        var pixelsPerUnit = entry.parsedGroupedMetadata.spritePixelsPerUnit > 0f ? entry.parsedGroupedMetadata.spritePixelsPerUnit : 100f;
        var meshType = GeneratedAtlasSpriteSynthesisUtility.ResolveMeshType(entry.parsedGroupedMetadata.spriteMeshType, SpriteMeshType.FullRect);
        sprite = GeneratedAtlasSpriteSynthesisUtility.CreateSpriteFromPayload(texture, match, pixelsPerUnit, meshType);
        if (sprite != null) {
          entry.spritesByName[normalizedName] = sprite;
          entry.generatedSprites.Add(sprite);
          return true;
        }
      }
    }

    if (entry.parsedMetadataAtlasMetadata != null && entry.metadataAtlasTextureHandle.IsValid()) {
      var texture = entry.metadataAtlasTextureHandle.Result;
      if (texture == null) return false;

      GeneratedAtlasSpriteSynthesisUtility.AtlasSpriteImportPayload match = null;
      for (var i = 0; i < entry.parsedMetadataAtlasMetadata.sprites.Count; i++) {
        var s = entry.parsedMetadataAtlasMetadata.sprites[i];
        if (s == null || string.IsNullOrWhiteSpace(s.name)) continue;
        if (!SpriteSliceAddressUtility.TryExtractNumericLabelValue(s.name, out var candidateNumericValue)) continue;
        if (!string.Equals(candidateNumericValue, numericLabelValue, StringComparison.Ordinal)) continue;

        if (match != null && match != s) {
          return false;
        }
        match = s;
      }

      if (match != null) {
        var normalizedName = match.name.Trim();
        if (entry.spritesByName.TryGetValue(normalizedName, out sprite) && sprite != null) {
          return true;
        }
        var pixelsPerUnit = entry.parsedMetadataAtlasMetadata.spritePixelsPerUnit > 0f ? entry.parsedMetadataAtlasMetadata.spritePixelsPerUnit : 100f;
        var meshType = GeneratedAtlasSpriteSynthesisUtility.ResolveMeshType(entry.parsedMetadataAtlasMetadata.spriteMeshType, SpriteMeshType.FullRect);
        sprite = GeneratedAtlasSpriteSynthesisUtility.CreateSpriteFromPayload(texture, match, pixelsPerUnit, meshType);
        if (sprite != null) {
          entry.spritesByName[normalizedName] = sprite;
          entry.generatedSprites.Add(sprite);
          return true;
        }
      }
    }

    return false;
  }

  static bool TryResolveRequestOwnerAddress(
    string requestedAddress,
    out string ownerAddress,
    out string spriteName,
    out string requestStrategy
  ) {
    ownerAddress = "";
    spriteName = "";
    requestStrategy = "direct_only";
    if (string.IsNullOrWhiteSpace(requestedAddress)) return false;

    if (_ownerAddressCache.TryGetValue(requestedAddress, out var cached)) {
      ownerAddress = cached.OwnerAddress;
      spriteName = cached.SpriteName;
      requestStrategy = cached.RequestStrategy;
      return cached.IsValid;
    }

    var isValid = TryResolveRequestOwnerAddressInternal(requestedAddress, out ownerAddress, out spriteName, out requestStrategy);
    var result = new OwnerAddressResult {
      IsValid = isValid,
      OwnerAddress = ownerAddress,
      SpriteName = spriteName,
      RequestStrategy = requestStrategy
    };
    if (_ownerAddressCache.TryAdd(requestedAddress, result)) {
      System.Threading.Interlocked.Increment(ref _ownerAddressCacheEntryCount);
      _ownerAddressCacheInsertionOrder.Enqueue(requestedAddress);
      TrimOwnerAddressCache();
    }
    return isValid;
  }

  static readonly System.Collections.Concurrent.ConcurrentDictionary<string, OwnerAddressResult> _ownerAddressCache = new();
  static readonly System.Collections.Concurrent.ConcurrentQueue<string> _ownerAddressCacheInsertionOrder = new();
  static int _ownerAddressCacheEntryCount;

  static void ResetOwnerAddressCache() {
    _ownerAddressCache.Clear();
    while (_ownerAddressCacheInsertionOrder.TryDequeue(out _)) { }
    System.Threading.Volatile.Write(ref _ownerAddressCacheEntryCount, 0);
  }

  static void TrimOwnerAddressCache() {
    while (System.Threading.Volatile.Read(ref _ownerAddressCacheEntryCount) > MaxOwnerAddressCacheEntries &&
           _ownerAddressCacheInsertionOrder.TryDequeue(out var oldestAddress)) {
      if (_ownerAddressCache.TryRemove(oldestAddress, out _)) {
        System.Threading.Interlocked.Decrement(ref _ownerAddressCacheEntryCount);
      }
    }
  }

  struct OwnerAddressResult {
    public bool IsValid;
    public string OwnerAddress;
    public string SpriteName;
    public string RequestStrategy;
  }

  static bool TryResolveRequestOwnerAddressInternal(
    string requestedAddress,
    out string ownerAddress,
    out string spriteName,
    out string requestStrategy
  ) {
    ownerAddress = "";
    spriteName = "";
    requestStrategy = "direct_only";
    if (string.IsNullOrWhiteSpace(requestedAddress)) return false;

    var normalizedRequestedAddress = requestedAddress.Trim();
    if (SpriteSliceAddressUtility.TryParseSliceAddress(normalizedRequestedAddress, out var atlasAssetPath, out var parsedSpriteName)) {
      var normalizedAtlasAddress = string.IsNullOrWhiteSpace(atlasAssetPath) ? "" : atlasAssetPath.Trim();
      var atlasOwnedRequest = !string.IsNullOrWhiteSpace(normalizedAtlasAddress);
      ownerAddress = atlasOwnedRequest ? normalizedAtlasAddress : normalizedRequestedAddress;
      spriteName = string.IsNullOrWhiteSpace(parsedSpriteName) ? "" : parsedSpriteName.Trim();
      requestStrategy = atlasOwnedRequest ? "atlas_backed" : "direct_only";
      return !string.IsNullOrWhiteSpace(ownerAddress);
    }

    ownerAddress = normalizedRequestedAddress;
    if (SupportsAtlasOwnedRequestPath(ownerAddress) ||
        ShouldUseMetadataDrivenAtlasLoad(ownerAddress) ||
        GeneratedAtlasBuildSurrogateUtility.ShouldUseImportedSpriteSubassets(ownerAddress)) {
      requestStrategy = "atlas_backed";
    }
    return true;
  }

  static string ResolveRequestOwnerAddress(string requestedAddress) {
    return TryResolveRequestOwnerAddress(requestedAddress, out var ownerAddress, out _, out _)
      ? ownerAddress
      : "";
  }

  static string NormalizeRawAddress(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    var normalized = value.Trim();
    if (SpriteSliceAddressUtility.TryParseSliceAddress(normalized, out var atlasAssetPath, out _)) {
      return string.IsNullOrWhiteSpace(atlasAssetPath) ? "" : atlasAssetPath.Trim();
    }
    return normalized;
  }

  static string NormalizeAddress(string value) {
    return ResolveRequestOwnerAddress(value);
  }

  static string NormalizePinLeaseAddress(string value) {
    return NormalizeAddress(value);
  }

  static string NormalizeOwnerId(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  static void TrackEntryRequestContext(CacheEntry entry, string requestedAddress) {
    if (entry == null) return;
    if (!TryResolveRequestOwnerAddress(requestedAddress, out var ownerAddress, out _, out var requestStrategy)) {
      entry.requestStrategy = "direct_only";
      entry.lastRequestedAddress = string.IsNullOrWhiteSpace(requestedAddress) ? "" : requestedAddress.Trim();
      return;
    }

    entry.requestStrategy = string.IsNullOrWhiteSpace(requestStrategy) ? "direct_only" : requestStrategy;
    entry.lastRequestedAddress = string.IsNullOrWhiteSpace(requestedAddress) ? ownerAddress : requestedAddress.Trim();
  }

  static int GetPinClassBudget(PinClass pinClass) {
    switch (pinClass) {
      case PinClass.Player:
        return Math.Max(SpriteStreamingRuntimeSettings.PinBudgetPlayerAddresses, 1);
      case PinClass.Enemy:
        return Math.Max(SpriteStreamingRuntimeSettings.PinBudgetEnemyAddresses, 1);
      case PinClass.UI:
        return Math.Max(SpriteStreamingRuntimeSettings.PinBudgetUiAddresses, 1);
      case PinClass.Effect:
        return Math.Max(SpriteStreamingRuntimeSettings.PinBudgetEffectAddresses, 1);
      case PinClass.WarmGate:
        // Warm-gate owner pins are capped by caller-provided address lists.
        // Keep class budget effectively unbounded so first-load resident sets
        // are not trimmed before gameplay unlock.
        return int.MaxValue;
      default:
        return int.MaxValue;
    }
  }

  static int CountPinnedAddressesForClass(PinClass pinClass) {
    var count = 0;
    foreach (var pair in ownerPins) {
      var state = pair.Value;
      if (state == null) continue;
      if (state.pinClass != pinClass) continue;
      count += Math.Max(state.leases.Count, 0);
    }
    return count;
  }

  static bool EnsurePinClassBudgetCapacity(
    PinClass pinClass,
    string protectedOwnerId,
    int classBudget,
    ref int used
  ) {
    if (classBudget <= 0 || classBudget == int.MaxValue) return true;
    var normalizedProtectedOwner = NormalizeOwnerId(protectedOwnerId);

    if (used < classBudget) return true;

    while (used >= classBudget) {
      if (!TryReleaseOldestLeaseFromClass(pinClass, normalizedProtectedOwner)) {
        return false;
      }
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
      if (state == null) continue;
      if (state.pinClass != pinClass) continue;
      if (state.leases == null || state.leases.Count <= 0) continue;
      if (string.Equals(state.ownerId, protectedOwnerId, StringComparison.OrdinalIgnoreCase)) continue;
      if (state.lastRefreshTicks > oldestTicks) continue;

      foreach (var leasePair in state.leases) {
        leaseKey = leasePair.Key;
        break;
      }
      if (string.IsNullOrWhiteSpace(leaseKey)) continue;
      oldestTicks = state.lastRefreshTicks;
      ownerCandidate = state;
    }

    if (ownerCandidate == null || string.IsNullOrWhiteSpace(leaseKey)) return false;
    if (!ownerCandidate.leases.TryGetValue(leaseKey, out var lease) || lease == null) return false;
    lease.Release();
    ownerCandidate.leases.Remove(leaseKey);
    if (ownerCandidate.leases.Count <= 0) {
      ownerPins.Remove(ownerCandidate.ownerId);
    }
    return true;
  }

  // Accepts a pre-normalized address set; caller is responsible for normalization.
  static bool AddressesMatchExistingLeases(Dictionary<string, Lease> existingLeases, HashSet<string> normalizedAddresses) {
    if (existingLeases == null || normalizedAddresses == null) return false;
    if (existingLeases.Count != normalizedAddresses.Count) return false;

    foreach (var address in normalizedAddresses) {
      if (!existingLeases.ContainsKey(address)) return false;
    }

    return true;
  }


}
