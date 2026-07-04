using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class SpriteRuntimeResolver {
  public static void WarmupLibraries(IEnumerable<string> nameparts) {
#if UNITY_EDITOR
    if (!Application.isPlaying) return;
#endif
    var manifestReadyNow = EnsureManifestReady();
    List<string> immediateWarmups = null;
    if (manifestFailed) {
      pendingWarmupNameparts.Clear();
      pendingWarmupNamepartsSet.Clear();
      return;
    }
    if (nameparts == null) {
      if (manifestReadyNow) {
        DrainPendingWarmups();
      }
      return;
    }

    foreach (var namepart in nameparts) {
      var normalizedNamepart = NormalizeNamePart(namepart);
      if (string.IsNullOrWhiteSpace(normalizedNamepart)) continue;
      if (!manifestReadyNow) {
        if (pendingWarmupNamepartsSet.Add(normalizedNamepart)) {
          pendingWarmupNameparts.Add(normalizedNamepart);
        }
        continue;
      }

      if (immediateWarmups == null) {
        immediateWarmups = new List<string>();
      }
      immediateWarmups.Add(normalizedNamepart);
    }

    if (immediateWarmups != null && immediateWarmups.Count > 1) {
      SortWarmupsByObservedLoadCost(immediateWarmups);
    }
    if (immediateWarmups != null) {
      for (var i = 0; i < immediateWarmups.Count; i++) {
        TryStartShardWarmup(immediateWarmups[i]);
      }
    }

    if (manifestReadyNow) {
      DrainPendingWarmups();
    }
  }

  public static bool IsWarmupIdle() {
#if UNITY_EDITOR
    if (!Application.isPlaying) return true;
#endif
    // Advance manifest load/parse state so pending warmups can drain even when
    // no runtime resolve calls are happening this frame.
    if (!manifestReady && !manifestFailed) {
      EnsureManifestReady();
    }

    if (manifestFailed) {
      if (pendingWarmupNameparts.Count > 0) {
        pendingWarmupNameparts.Clear();
        pendingWarmupNamepartsSet.Clear();
      }
      return true;
    }
    if (manifestParse != null && !manifestParse.IsCompleted) return false;
    if (manifestLoadStarted && !manifestReady && !manifestFailed && manifestLoad.IsValid() && !manifestLoad.IsDone) return false;
    if (manifestReady && pendingWarmupNameparts.Count > 0) {
      DrainPendingWarmups();
    }
    if (pendingWarmupNameparts.Count > 0) return false;
    if (shardLoads.Count > 0) return false;

    foreach (var pair in shardParses) {
      var parseTask = pair.Value;
      if (parseTask != null && !parseTask.IsCompleted) return false;
    }

    return true;
  }

  public static bool AreShardsReady(IEnumerable<string> nameparts) {
#if UNITY_EDITOR
    if (!Application.isPlaying) return true;
#endif
    if (!manifestReady) return false;
    if (nameparts == null) return true;

    foreach (var namepart in nameparts) {
      var normalized = NormalizeNamePart(namepart);
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      if (!TryGetManifestEntryForNamepart(manifestByNamepart, normalized, out var entry)) continue;

      var shardKey = string.IsNullOrWhiteSpace(entry.namepart) ? normalized : entry.namepart;
      if (!loadedShards.ContainsKey(shardKey)) {
        if (shardParses.TryGetValue(shardKey, out var shardParseTask) && shardParseTask.IsCompleted) {
          if (TryGetShard(shardKey, entry, out _)) {
            continue;
          }
        }
        return false;
      }
    }
    return true;
  }

  public static bool TryResolve(SpriteLookupKey key, out SpriteAddressPair pair, Object logContext = null) {
    pair = default;
#if UNITY_EDITOR
    if (!Application.isPlaying) return false;
#endif
    if (!EnsureManifestReady()) return false;

    var normalizedNamepart = NormalizeNamePart(key.namepart);
    if (!TryGetManifestEntryForNamepart(manifestByNamepart, normalizedNamepart, out var shardEntry, logContext)) return false;

    var shardKey = string.IsNullOrWhiteSpace(shardEntry.namepart) ? normalizedNamepart : shardEntry.namepart;
    var cacheKey = new LookupCacheKey(shardKey, key.labelPrefix, key.category, key.frame);
    if (lookupHitCache.TryGetValue(cacheKey, out pair)) {
      if (loadedShards.TryGetValue(shardKey, out var loadedShard) && loadedShard != null) {
        loadedShard.lastAccessTime = Time.realtimeSinceStartup;
      }
      return true;
    }
    if (lookupMissCache.Contains(cacheKey)) return false;

    if (!TryGetShard(shardKey, shardEntry, out var shard)) return false;

    var exactKey = BuildRowKey(key.labelPrefix, key.category, key.frame);
    if (shard.rows.TryGetValue(exactKey, out pair)) {
      shard.lastAccessTime = Time.realtimeSinceStartup;
      CacheLookupHit(cacheKey, pair);
      return true;
    }

    if (key.frame != 0) {
      var frameZeroKey = BuildRowKey(key.labelPrefix, key.category, 0);
      if (shard.rows.TryGetValue(frameZeroKey, out pair)) {
        shard.lastAccessTime = Time.realtimeSinceStartup;
        CacheLookupHit(cacheKey, pair);
        return true;
      }
    }

    if (TryResolveNumericFormFallback(shard.rows, key, out pair)) {
      shard.lastAccessTime = Time.realtimeSinceStartup;
      CacheLookupHit(cacheKey, pair);
      return true;
    }

    CacheLookupMiss(cacheKey);
    return false;
  }

  public static bool IsLookupPending(SpriteLookupKey key, Object logContext = null) {
#if UNITY_EDITOR
    if (!Application.isPlaying) return false;
#endif
    if (manifestFailed) return false;
    if (!manifestReady) {
      EnsureManifestReady();
      if (!manifestReady) return !manifestFailed;
    }

    var normalizedNamepart = NormalizeNamePart(key.namepart);
    if (!TryGetManifestEntryForNamepart(manifestByNamepart, normalizedNamepart, out var shardEntry, logContext)) return false;

    var shardKey = string.IsNullOrWhiteSpace(shardEntry.namepart) ? normalizedNamepart : shardEntry.namepart;
    if (loadedShards.ContainsKey(shardKey)) return false;

    if (shardParses.TryGetValue(shardKey, out var shardParseTask)) {
      return !shardParseTask.IsCompleted;
    }

    if (shardLoads.ContainsKey(shardKey)) return true;
    if (string.IsNullOrWhiteSpace(shardEntry.address)) return false;

    StartShardLoad(shardKey, shardEntry);
    return true;
  }

  public static void InvalidateLookup(SpriteLookupKey key, bool reloadShard = false) {
#if UNITY_EDITOR
    if (!Application.isPlaying) return;
#endif
    var normalizedNamepart = NormalizeNamePart(key.namepart);
    if (string.IsNullOrWhiteSpace(normalizedNamepart)) return;

    var shardKey = ResolveShardKey(normalizedNamepart);
    InvalidateLookupCacheEntry(new LookupCacheKey(shardKey, key.labelPrefix, key.category, key.frame));
    if (key.frame != 0) {
      InvalidateLookupCacheEntry(new LookupCacheKey(shardKey, key.labelPrefix, key.category, 0));
    }

    if (!reloadShard) return;
    var currentFrame = Time.frameCount;
    if (shardReloadInvalidatedFrame.TryGetValue(shardKey, out var invalidatedFrame) && invalidatedFrame == currentFrame) {
      return;
    }
    shardReloadInvalidatedFrame[shardKey] = currentFrame;
    loadedShards.Remove(shardKey);
  }

  public static bool TryCollectAtlasSiblingAddresses(
    string sliceAddress,
    List<string> outAddresses,
    int maxAddresses = 1024,
    HashSet<string> seenAddresses = null
  ) {
#if UNITY_EDITOR
    if (!Application.isPlaying) return false;
#endif
    if (outAddresses == null) return false;
    if (maxAddresses <= 0) maxAddresses = 1;

    var atlasAssetPath = "";
    if (SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out var parsedAtlasAssetPath, out _)) {
      atlasAssetPath = parsedAtlasAssetPath;
    }
    else {
      atlasAssetPath = NormalizeToken(sliceAddress);
    }
    var normalizedAtlasPath = NormalizeToken(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) return false;

    var found = false;
    var activeSeenAddresses = seenAddresses ?? atlasSiblingSeenScratch;
    if (seenAddresses == null) {
      activeSeenAddresses.Clear();
      for (var i = 0; i < outAddresses.Count; i++) {
        var existing = NormalizeToken(outAddresses[i]);
        if (!string.IsNullOrWhiteSpace(existing)) activeSeenAddresses.Add(existing);
      }
    }

    foreach (var pair in loadedShards) {
      var shard = pair.Value;
      if (shard == null || shard.rows == null || shard.rows.Count <= 0) continue;
      EnsureShardAtlasLookup(shard);
      if (shard.addressesByAtlasPath == null || shard.addressesByAtlasPath.Count <= 0) continue;
      if (!shard.addressesByAtlasPath.TryGetValue(normalizedAtlasPath, out var siblings) || siblings == null || siblings.Count <= 0) continue;

      shard.lastAccessTime = Time.realtimeSinceStartup;
      found = true;
      for (var i = 0; i < siblings.Count; i++) {
        if (outAddresses.Count >= maxAddresses) return true;
        var candidate = NormalizeToken(siblings[i]);
        if (string.IsNullOrWhiteSpace(candidate)) continue;
        if (!activeSeenAddresses.Add(candidate)) continue;
        outAddresses.Add(candidate);
      }
    }

    return found;
  }

  public static string NormalizeNamePart(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    if (namepartNormCache.TryGetValue(value, out var cached)) return cached;

    var normalized = NormalizeToken(value).Replace('\\', '/');
    normalized = CollapseSlashes(normalized).Trim('/');
    if (normalized.EndsWith(SpriteStreamingConfig.CustomSpriteLibraryExtension, StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(0, normalized.Length - SpriteStreamingConfig.CustomSpriteLibraryExtension.Length);
    }
    else if (normalized.EndsWith(SpriteStreamingConfig.LegacySpriteLibraryExtension, StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(0, normalized.Length - SpriteStreamingConfig.LegacySpriteLibraryExtension.Length);
    }

    var root = NormalizeToken(RuntimeConfig.SourceRootFolder).Replace('\\', '/');
    root = CollapseSlashes(root).Trim('/');
    if (string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase)) {
      namepartNormCache[value] = "";
      return "";
    }

    if (!string.IsNullOrWhiteSpace(root) &&
        normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(root.Length + 1);
    }

    namepartNormCache[value] = normalized;
    return normalized;
  }
}
