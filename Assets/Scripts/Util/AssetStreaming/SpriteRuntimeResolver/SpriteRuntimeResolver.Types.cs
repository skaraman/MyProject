using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public static partial class SpriteRuntimeResolver {
  static class RuntimeConfig {
    public const string SourceRootFolder = SpriteStreamingConfig.SourceRootFolder;
    public const string ManifestAssetPath = SpriteStreamingConfig.ManifestAssetPath;
    public const string DefaultManifestAddress = SpriteStreamingConfig.DefaultManifestAddress;
  }

  struct ManifestEntry {
    public string namepart;
    public string address;
    public string assetPath;
  }

  sealed class ParsedManifestData {
    public Dictionary<string, ManifestEntry> rows;
    public Dictionary<string, List<string>> ambiguousShortNamepartMatches;
  }

  readonly struct LookupCacheKey : IEquatable<LookupCacheKey> {
    public readonly string shardKey;
    public readonly string labelPrefix;
    public readonly string category;
    public readonly int frame;

    public LookupCacheKey(string shardKey, string labelPrefix, string category, int frame) {
      this.shardKey = shardKey ?? "";
      this.labelPrefix = labelPrefix ?? "";
      this.category = category ?? "";
      this.frame = frame;
    }

    public bool Equals(LookupCacheKey other) {
      return frame == other.frame &&
             string.Equals(shardKey, other.shardKey, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(labelPrefix, other.labelPrefix, StringComparison.Ordinal) &&
             string.Equals(category, other.category, StringComparison.Ordinal);
    }

    public override bool Equals(object obj) {
      return obj is LookupCacheKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = 17;
        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(shardKey ?? "");
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(labelPrefix ?? "");
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(category ?? "");
        hash = (hash * 31) + frame;
        return hash;
      }
    }
  }

  sealed class ShardData {
    public Dictionary<string, SpriteAddressPair> rows;
    public Dictionary<string, List<string>> addressesByAtlasPath;
    public bool atlasLookupBuilt;
    // switched to realtime seconds to avoid heavy DateTime calls
    public float lastAccessTime;
  }

  sealed class ParsedShardData {
    public Dictionary<string, SpriteAddressPair> rows;
    public Dictionary<string, List<string>> addressesByAtlasPath;
  }

  static readonly Dictionary<string, ManifestEntry> manifestByNamepart = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, List<string>> ambiguousShortNamepartMatches = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, ShardData> loadedShards = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, AsyncOperationHandle<TextAsset>> shardLoads = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, Task<ParsedShardData>> shardParses = new(StringComparer.OrdinalIgnoreCase);
  static readonly List<string> pendingWarmupNameparts = new();
  static readonly HashSet<string> pendingWarmupNamepartsSet = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, float> shardLoadStartedAt = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, float> shardLoadEwmaMs = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, int> shardSlowLoadHits = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, int> shardReloadInvalidatedFrame = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, float> logCooldown = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<LookupCacheKey, SpriteAddressPair> lookupHitCache = new();
  static readonly HashSet<LookupCacheKey> lookupMissCache = new();
  // caches for expensive normalization routines
  static readonly Dictionary<string, string> tokenNormCache = new(StringComparer.Ordinal);
  static readonly Dictionary<string, string> namepartNormCache = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> atlasSiblingSeenScratch = new(StringComparer.OrdinalIgnoreCase);
#if UNITY_EDITOR
  static readonly HashSet<string> editorSpriteLoadWarnings = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, Dictionary<string, long>> editorMetaSpriteIdsByAssetPath = new(StringComparer.OrdinalIgnoreCase);
#endif

  static AsyncOperationHandle<TextAsset> manifestLoad;
  static Task<ParsedManifestData> manifestParse;
  static bool manifestLoadStarted;
  static bool manifestReady;
  static bool manifestFailed;

  struct ResolverSettings {
    public string manifestAddress;
    public int maxLoadedShards;
  }

  static ResolverSettings settings;
  static bool settingsLoaded;
  const int MaxLookupCacheEntries = 32768;
  const float SlowShardLoadThresholdMs = 80f;
  const float ShardLoadEwmaBlend = 0.35f;
  const float SlowShardWarmupBonusMs = 40f;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    if (manifestLoad.IsValid()) {
      Addressables.Release(manifestLoad);
    }

    foreach (var pair in shardLoads) {
      if (pair.Value.IsValid()) {
        Addressables.Release(pair.Value);
      }
    }

    manifestByNamepart.Clear();
    ambiguousShortNamepartMatches.Clear();
    loadedShards.Clear();
    shardLoads.Clear();
    shardParses.Clear();
    pendingWarmupNameparts.Clear();
    pendingWarmupNamepartsSet.Clear();
    shardLoadStartedAt.Clear();
    shardLoadEwmaMs.Clear();
    shardSlowLoadHits.Clear();
    shardReloadInvalidatedFrame.Clear();
    logCooldown.Clear();
    lookupHitCache.Clear();
    lookupMissCache.Clear();
    tokenNormCache.Clear();
    namepartNormCache.Clear();
    atlasSiblingSeenScratch.Clear();

#if UNITY_EDITOR
    editorSpriteLoadWarnings.Clear();
    editorMetaSpriteIdsByAssetPath.Clear();
#endif

    manifestLoadStarted = false;
    manifestReady = false;
    manifestFailed = false;
    manifestLoad = default;
    manifestParse = null;

    settingsLoaded = false;
    settings = default;
  }
}
