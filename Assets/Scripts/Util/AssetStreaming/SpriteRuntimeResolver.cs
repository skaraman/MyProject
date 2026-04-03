using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class SpriteRuntimeResolver {
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
  static Task<Dictionary<string, ManifestEntry>> manifestParse;
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
      if (!loadedShards.ContainsKey(shardKey)) return false;
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
    if (normalized.EndsWith(".spriteLib", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(0, normalized.Length - ".spriteLib".Length);
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

#if UNITY_EDITOR
  static bool TryEnsureEditorManifestReady() {
    if (!Application.isEditor || !Application.isPlaying) return false;
    if (manifestLoadStarted || manifestParse != null) return false;

    var manifestAsset = LoadEditorManifestAsset();
    if (manifestAsset == null || string.IsNullOrWhiteSpace(manifestAsset.text)) return false;

    var parsed = ParseManifestRows(manifestAsset.text);
    if (parsed.Count <= 0) return false;

    manifestByNamepart.Clear();
    lookupHitCache.Clear();
    lookupMissCache.Clear();
    foreach (var pair in parsed) {
      manifestByNamepart[pair.Key] = pair.Value;
    }

    manifestReady = true;
    manifestFailed = false;
    DrainPendingWarmups();
    return true;
  }

  public static bool TryResolveEditor(SpriteLookupKey key, out SpriteAddressPair pair, Object logContext = null) {
    pair = default;
    var manifestAsset = LoadEditorManifestAsset();
    if (manifestAsset != null && !string.IsNullOrWhiteSpace(manifestAsset.text)) {
      var manifestRows = ParseManifestRows(manifestAsset.text);
      if (manifestRows.Count > 0) {
        var normalizedNamepart = NormalizeNamePart(key.namepart);
        if (TryGetManifestEntryForNamepart(manifestRows, normalizedNamepart, out var shardEntry, logContext) &&
            !string.IsNullOrWhiteSpace(shardEntry.assetPath)) {
          var shardAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(shardEntry.assetPath);
          if (shardAsset != null && !string.IsNullOrWhiteSpace(shardAsset.text)) {
            var parsedShard = ParseShardRows(shardAsset.text);
            var rows = parsedShard.rows;
            if (rows != null && rows.Count > 0) {
              var exactKey = BuildRowKey(key.labelPrefix, key.category, key.frame);
              if (rows.TryGetValue(exactKey, out pair)) return true;

              if (key.frame != 0) {
                var frameZeroKey = BuildRowKey(key.labelPrefix, key.category, 0);
                if (rows.TryGetValue(frameZeroKey, out pair)) return true;
              }

              if (TryResolveNumericFormFallback(rows, key, out pair)) return true;
            }
          }
        }
      }
    }

    return false;
  }

  static TextAsset LoadEditorManifestAsset() {
    return AssetDatabase.LoadAssetAtPath<TextAsset>(RuntimeConfig.ManifestAssetPath);
  }

  public static bool TryLoadEditorSprite(string address, out Sprite sprite) {
    sprite = null;
    if (string.IsNullOrWhiteSpace(address)) return false;

    var normalizedAddress = NormalizeToken(address);
    var assetPath = normalizedAddress;
    var spriteName = "";

    var bracketIndex = normalizedAddress.LastIndexOf('[');
    if (bracketIndex > 0 && normalizedAddress.EndsWith("]", StringComparison.Ordinal)) {
      assetPath = normalizedAddress.Substring(0, bracketIndex);
      spriteName = normalizedAddress.Substring(bracketIndex + 1, normalizedAddress.Length - bracketIndex - 2);
    }

    assetPath = CollapseSlashes(assetPath.Replace('\\', '/'));
    if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return false;

    var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
    if (TryMatchEditorSprite(assets, spriteName, out sprite)) return true;

    var representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
    if (TryMatchEditorSprite(representations, spriteName, out sprite)) return true;
    if (TryMatchEditorSpriteByMetaFileId(assetPath, spriteName, assets, representations, out sprite)) return true;

    if (editorSpriteLoadWarnings.Add(normalizedAddress)) {
      Debug.LogWarning(
        "[SpriteRuntimeResolver] Editor sprite load miss asset='" + assetPath +
        "' sprite='" + spriteName +
        "' assets_count=" + (assets?.Length ?? 0) +
        " representations_count=" + (representations?.Length ?? 0)
      );
    }

    return false;
  }
#endif

#if UNITY_EDITOR
  static bool TryMatchEditorSprite(UnityEngine.Object[] assets, string spriteName, out Sprite sprite) {
    sprite = null;
    if (assets == null || assets.Length == 0) return false;

    Sprite numericMatch = null;
    for (var i = 0; i < assets.Length; i++) {
      var candidate = assets[i] as Sprite;
      if (candidate == null) continue;
      if (string.IsNullOrEmpty(spriteName) || string.Equals(candidate.name, spriteName, StringComparison.Ordinal)) {
        sprite = candidate;
        return true;
      }

      if (!SpriteSliceAddressUtility.HasEquivalentNumericLabel(candidate.name, spriteName)) continue;
      if (numericMatch != null && numericMatch != candidate) {
        numericMatch = null;
        break;
      }
      numericMatch = candidate;
    }

    if (numericMatch == null) return false;
    sprite = numericMatch;
    return true;
  }

  static bool TryMatchEditorSpriteByMetaFileId(
    string assetPath,
    string spriteName,
    UnityEngine.Object[] assets,
    UnityEngine.Object[] representations,
    out Sprite sprite) {
    sprite = null;
    if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(spriteName)) return false;
    if (!TryResolveEditorSpriteLocalFileId(assetPath, spriteName, out var localFileId)) return false;

    if (TryMatchEditorSpriteByLocalFileId(assets, localFileId, out sprite)) return true;
    if (TryMatchEditorSpriteByLocalFileId(representations, localFileId, out sprite)) return true;
    return false;
  }

  static bool TryMatchEditorSpriteByLocalFileId(UnityEngine.Object[] assets, long localFileId, out Sprite sprite) {
    sprite = null;
    if (assets == null || assets.Length == 0) return false;

    for (var i = 0; i < assets.Length; i++) {
      var candidate = assets[i] as Sprite;
      if (candidate == null) continue;
      if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out _, out long candidateLocalFileId)) continue;
      if (candidateLocalFileId != localFileId) continue;
      sprite = candidate;
      return true;
    }

    return false;
  }

  static bool TryResolveEditorSpriteLocalFileId(string assetPath, string spriteName, out long localFileId) {
    localFileId = 0;
    if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(spriteName)) return false;

    if (!editorMetaSpriteIdsByAssetPath.TryGetValue(assetPath, out var spriteIdsByName)) {
      spriteIdsByName = BuildEditorMetaSpriteIdMap(assetPath);
      editorMetaSpriteIdsByAssetPath[assetPath] = spriteIdsByName;
    }

    return spriteIdsByName != null && spriteIdsByName.TryGetValue(spriteName, out localFileId);
  }

  static Dictionary<string, long> BuildEditorMetaSpriteIdMap(string assetPath) {
    var spriteIdsByName = new Dictionary<string, long>(StringComparer.Ordinal);
    if (string.IsNullOrWhiteSpace(assetPath)) return spriteIdsByName;

    var metaPath = assetPath + ".meta";
    if (!File.Exists(metaPath)) return spriteIdsByName;

    var inTable = false;
    var tableIndent = -1;
    foreach (var rawLine in File.ReadLines(metaPath)) {
      var line = rawLine ?? "";
      var trimmed = line.Trim();
      var indent = line.Length - line.TrimStart().Length;

      if (!inTable) {
        if (trimmed.StartsWith("nameFileIdTable:", StringComparison.Ordinal)) {
          inTable = true;
          tableIndent = indent;
        }
        continue;
      }

      if (string.IsNullOrWhiteSpace(trimmed)) continue;

      if (indent <= tableIndent) {
        inTable = false;
        if (trimmed.StartsWith("nameFileIdTable:", StringComparison.Ordinal)) {
          inTable = true;
          tableIndent = indent;
        }
        continue;
      }

      var separatorIndex = trimmed.IndexOf(':');
      if (separatorIndex <= 0 || separatorIndex >= trimmed.Length - 1) continue;

      var name = DecodeEditorMetaScalar(trimmed.Substring(0, separatorIndex));
      if (string.IsNullOrWhiteSpace(name)) continue;

      var idText = trimmed.Substring(separatorIndex + 1).Trim();
      if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId)) continue;
      spriteIdsByName[name] = parsedId;
    }

    return spriteIdsByName;
  }

  static string DecodeEditorMetaScalar(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    var trimmed = value.Trim();
    if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'') {
      return trimmed.Substring(1, trimmed.Length - 2).Replace("''", "'");
    }

    return trimmed;
  }
#endif

  static bool TryResolveNumericFormFallback(Dictionary<string, SpriteAddressPair> rows, SpriteLookupKey key, out SpriteAddressPair pair) {
    pair = default;
    if (rows == null || rows.Count == 0) return false;
    if (key.frame != 0) return false;

    var normalizedLabelPrefix = NormalizeToken(key.labelPrefix);
    if (string.IsNullOrWhiteSpace(normalizedLabelPrefix)) return false;
    if (!int.TryParse(normalizedLabelPrefix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericFrame)) return false;
    if (numericFrame < 0) return false;

    var aliasKey = BuildRowKey("", key.category, numericFrame);
    return rows.TryGetValue(aliasKey, out pair);
  }

  static bool TryGetManifestEntryForNamepart(
    Dictionary<string, ManifestEntry> rows,
    string requestedNamepart,
    out ManifestEntry entry,
    Object logContext = null
  ) {
    entry = default;
    if (rows == null || rows.Count == 0) return false;
    if (string.IsNullOrWhiteSpace(requestedNamepart)) return false;

    if (rows.TryGetValue(requestedNamepart, out entry)) return true;

    if (TryResolveMovedFullPathAlias(rows, requestedNamepart, out entry, logContext)) return true;

    if (requestedNamepart.IndexOf('/') >= 0) return false;

    if (ambiguousShortNamepartMatches.TryGetValue(requestedNamepart, out var ambiguousMatches) &&
        ambiguousMatches != null &&
        ambiguousMatches.Count > 1) {
      RateLimitedWarning(
        BuildContextualWarningKey("shortkey-ambiguous-request:" + requestedNamepart, logContext),
        AppendWarningContext("[SpriteRuntimeResolver] " + BuildShortKeyAmbiguityError(requestedNamepart, ambiguousMatches), logContext),
        logContext
      );
    }

    return false;
  }

  static bool TryResolveMovedFullPathAlias(
    Dictionary<string, ManifestEntry> rows,
    string requestedNamepart,
    out ManifestEntry entry,
    Object logContext = null
  ) {
    entry = default;
    if (rows == null || rows.Count == 0 || string.IsNullOrWhiteSpace(requestedNamepart)) return false;

    var slash = requestedNamepart.LastIndexOf('/');
    if (slash < 0 || slash >= requestedNamepart.Length - 1) return false;

    var leafName = NormalizeNamePart(requestedNamepart.Substring(slash + 1));
    if (string.IsNullOrWhiteSpace(leafName)) return false;

    if (ambiguousShortNamepartMatches.TryGetValue(leafName, out var ambiguousMatches) &&
        ambiguousMatches != null &&
        ambiguousMatches.Count > 1) {
      RateLimitedWarning(
        BuildContextualWarningKey("moved-fullpath-ambiguous-request:" + requestedNamepart, logContext),
        AppendWarningContext("[SpriteRuntimeResolver] " + BuildShortKeyAmbiguityError(leafName, ambiguousMatches), logContext),
        logContext
      );
      return false;
    }

    if (!rows.TryGetValue(leafName, out entry)) return false;

    var canonicalNamepart = string.IsNullOrWhiteSpace(entry.namepart) ? leafName : entry.namepart;
    if (!string.Equals(canonicalNamepart, requestedNamepart, StringComparison.OrdinalIgnoreCase)) {
      RateLimitedWarning(
        BuildContextualWarningKey("moved-fullpath-remap:" + requestedNamepart, logContext),
        AppendWarningContext("[SpriteRuntimeResolver] Remapped missing namepart '" + requestedNamepart +
        "' to '" + canonicalNamepart +
        "' by unique short name '" + leafName + "'.", logContext),
        logContext
      );
    }

    return true;
  }

  static bool EnsureManifestReady() {
    if (manifestReady) return true;
    if (manifestFailed) return false;
#if UNITY_EDITOR
    if (TryEnsureEditorManifestReady()) return true;
#endif
    if (manifestParse != null) {
      if (!manifestParse.IsCompleted) return false;

      if (manifestParse.IsFaulted || manifestParse.IsCanceled) {
        manifestFailed = true;
        pendingWarmupNameparts.Clear();
        pendingWarmupNamepartsSet.Clear();
        RateLimitedLog("manifest:parse", "[SpriteRuntimeResolver] Failed to parse sprite index manifest.");
        manifestParse = null;
        return false;
      }

      manifestByNamepart.Clear();
      lookupHitCache.Clear();
      lookupMissCache.Clear();
      var parsed = manifestParse.Result;
      foreach (var pair in parsed) {
        manifestByNamepart[pair.Key] = pair.Value;
      }

      manifestParse = null;
      manifestReady = true;
      DrainPendingWarmups();
      return true;
    }

    if (!manifestLoadStarted) {
      manifestLoadStarted = true;
      var cfg = GetSettings();
      var manifestAddress = !string.IsNullOrWhiteSpace(cfg.manifestAddress)
        ? cfg.manifestAddress.Trim()
        : RuntimeConfig.DefaultManifestAddress;

      manifestLoad = Addressables.LoadAssetAsync<TextAsset>(manifestAddress);
      manifestLoad.Completed += operation => {
        if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null || string.IsNullOrWhiteSpace(operation.Result.text)) {
          manifestFailed = true;
          pendingWarmupNameparts.Clear();
          pendingWarmupNamepartsSet.Clear();
          RateLimitedLog("manifest:" + manifestAddress, "[SpriteRuntimeResolver] Failed to load sprite index manifest at address '" + manifestAddress + "'.");
          if (operation.IsValid()) {
            Addressables.Release(operation);
          }
          return;
        }

        var manifestText = operation.Result.text ?? "";
        manifestParse = Task.Run(() => ParseManifestRows(manifestText, allowUnityLogging: false));
        if (operation.IsValid()) {
          Addressables.Release(operation);
        }
      };
    }

    return manifestReady;
  }

  static bool TryGetShard(string namepart, ManifestEntry entry, out ShardData shard) {
    if (loadedShards.TryGetValue(namepart, out shard)) {
      shard.lastAccessTime = Time.realtimeSinceStartup;
      return true;
    }

    if (shardParses.TryGetValue(namepart, out var shardParseTask)) {
      if (!shardParseTask.IsCompleted) return false;

      shardParses.Remove(namepart);
      if (shardParseTask.IsFaulted || shardParseTask.IsCanceled) {
        RateLimitedLog("shardparse:" + namepart, "[SpriteRuntimeResolver] Failed to parse shard rows for '" + namepart + "'.");
        return false;
      }

      var parsedShard = shardParseTask.Result;
      loadedShards[namepart] = new ShardData {
        rows = parsedShard?.rows ?? new Dictionary<string, SpriteAddressPair>(StringComparer.Ordinal),
        addressesByAtlasPath = parsedShard?.addressesByAtlasPath,
        atlasLookupBuilt = parsedShard?.addressesByAtlasPath != null,
        lastAccessTime = Time.realtimeSinceStartup
      };
      EnforceShardBudget();
      shard = loadedShards[namepart];
      return true;
    }

    if (shardLoads.ContainsKey(namepart)) return false;
    if (string.IsNullOrWhiteSpace(entry.address)) return false;

    StartShardLoad(namepart, entry);
    return false;
  }

  static void StartShardLoad(string namepart, ManifestEntry entry) {
    if (string.IsNullOrWhiteSpace(namepart)) return;
    if (loadedShards.ContainsKey(namepart)) return;
    if (shardLoads.ContainsKey(namepart)) return;
    if (shardParses.ContainsKey(namepart)) return;
    if (string.IsNullOrWhiteSpace(entry.address)) return;

    var shardAddress = entry.address.Trim();
    shardLoadStartedAt[namepart] = Time.realtimeSinceStartup;
    var load = Addressables.LoadAssetAsync<TextAsset>(shardAddress);
    shardLoads[namepart] = load;
    load.Completed += operation => {
      shardLoads.Remove(namepart);
      RecordShardLoadLatency(namepart);

      if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null) {
        RateLimitedLog("shardload:" + namepart, "[SpriteRuntimeResolver] Failed to load shard for '" + namepart + "' at address '" + shardAddress + "'.");
        if (operation.IsValid()) {
          Addressables.Release(operation);
        }
        return;
      }

      var shardText = operation.Result.text ?? "";
      shardParses[namepart] = Task.Run(() => ParseShardRows(shardText, allowUnityLogging: false));

      if (operation.IsValid()) {
        Addressables.Release(operation);
      }
    };
  }

  static void TryStartShardWarmup(string normalizedNamepart) {
    if (string.IsNullOrWhiteSpace(normalizedNamepart)) return;
    if (!manifestReady) {
      if (pendingWarmupNamepartsSet.Add(normalizedNamepart)) {
        pendingWarmupNameparts.Add(normalizedNamepart);
      }
      return;
    }
    if (!TryGetManifestEntryForNamepart(manifestByNamepart, normalizedNamepart, out var shardEntry)) return;

    var shardKey = string.IsNullOrWhiteSpace(shardEntry.namepart) ? normalizedNamepart : shardEntry.namepart;
    StartShardLoad(shardKey, shardEntry);
  }

  static void DrainPendingWarmups() {
    if (!manifestReady || pendingWarmupNameparts.Count == 0) return;

    if (pendingWarmupNameparts.Count > 1) {
      SortWarmupsByObservedLoadCost(pendingWarmupNameparts);
    }
    for (var i = 0; i < pendingWarmupNameparts.Count; i++) {
      TryStartShardWarmup(pendingWarmupNameparts[i]);
    }
    pendingWarmupNameparts.Clear();
    pendingWarmupNamepartsSet.Clear();
  }

  static void SortWarmupsByObservedLoadCost(List<string> warmups) {
    if (warmups == null || warmups.Count <= 1) return;
    warmups.Sort((a, b) => {
      var scoreA = ResolveWarmupPriorityScore(a);
      var scoreB = ResolveWarmupPriorityScore(b);
      var cmp = scoreB.CompareTo(scoreA); // slower shards first
      if (cmp != 0) return cmp;
      return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    });
  }

  static float ResolveWarmupPriorityScore(string normalizedNamepart) {
    if (string.IsNullOrWhiteSpace(normalizedNamepart)) return 0f;
    var shardKey = normalizedNamepart;
    if (manifestReady && TryGetManifestEntryForNamepart(manifestByNamepart, normalizedNamepart, out var shardEntry)) {
      shardKey = string.IsNullOrWhiteSpace(shardEntry.namepart) ? normalizedNamepart : shardEntry.namepart;
    }

    shardLoadEwmaMs.TryGetValue(shardKey, out var ewmaMs);
    shardSlowLoadHits.TryGetValue(shardKey, out var slowHits);
    return ewmaMs + (slowHits * SlowShardWarmupBonusMs);
  }

  static void RecordShardLoadLatency(string shardKey) {
    if (string.IsNullOrWhiteSpace(shardKey)) return;
    if (!shardLoadStartedAt.TryGetValue(shardKey, out var startedAt)) return;
    shardLoadStartedAt.Remove(shardKey);

    var loadMs = Mathf.Max((Time.realtimeSinceStartup - startedAt) * 1000f, 0f);
    shardLoadEwmaMs.TryGetValue(shardKey, out var previousEwmaMs);
    var ewmaMs = previousEwmaMs > 0f
      ? Mathf.Lerp(previousEwmaMs, loadMs, ShardLoadEwmaBlend)
      : loadMs;
    shardLoadEwmaMs[shardKey] = ewmaMs;

    shardSlowLoadHits.TryGetValue(shardKey, out var slowHits);
    if (loadMs >= SlowShardLoadThresholdMs) {
      slowHits = Mathf.Min(slowHits + 1, 8);
    }
    else if (slowHits > 0) {
      slowHits--;
    }
    shardSlowLoadHits[shardKey] = slowHits;
  }

  static void EnforceShardBudget() {
    var cfg = GetSettings();
    var maxLoaded = Math.Max(cfg.maxLoadedShards, 1);

    while (loadedShards.Count > maxLoaded) {
      string oldestKey = null;
      float oldestTime = float.MaxValue;

      foreach (var pair in loadedShards) {
        if (pair.Value == null) continue;
        if (pair.Value.lastAccessTime >= oldestTime) continue;
        oldestTime = pair.Value.lastAccessTime;
        oldestKey = pair.Key;
      }

      if (oldestKey == null) break;
      loadedShards.Remove(oldestKey);
    }
  }

  static void EnsureShardAtlasLookup(ShardData shard) {
    if (shard == null) return;
    if (shard.atlasLookupBuilt && shard.addressesByAtlasPath != null) return;

    shard.atlasLookupBuilt = true;
    shard.addressesByAtlasPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    if (shard.rows == null || shard.rows.Count <= 0) return;

    foreach (var pair in shard.rows) {
      AddAddressToAtlasLookup(shard.addressesByAtlasPath, pair.Value.colorAtlasAddress, pair.Value.colorAddress);
      AddAddressToAtlasLookup(shard.addressesByAtlasPath, pair.Value.normalAtlasAddress, pair.Value.normalAddress);
    }
  }

  static void AddAddressToAtlasLookup(Dictionary<string, List<string>> atlasMap, string atlasAssetPath, string sliceAddress) {
    if (atlasMap == null || string.IsNullOrWhiteSpace(sliceAddress)) return;
    var normalizedAtlasPath = atlasAssetPath;
    var normalizedSliceAddress = sliceAddress;
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) {
      if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out var parsedAtlasAssetPath, out _)) return;
      normalizedAtlasPath = NormalizeToken(parsedAtlasAssetPath);
      normalizedSliceAddress = NormalizeToken(sliceAddress);
    }
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath) || string.IsNullOrWhiteSpace(normalizedSliceAddress)) return;

    if (!atlasMap.TryGetValue(normalizedAtlasPath, out var addresses) || addresses == null) {
      addresses = new List<string>();
      atlasMap[normalizedAtlasPath] = addresses;
    }

    // TODO: O(n) linear scan for dedup. For large atlases this scales poorly.
    // Replace the per-atlas List<string> with a HashSet<string> (OrdinalIgnoreCase) for O(1) contains,
    // or maintain a parallel HashSet alongside the list if ordered iteration is required.
    for (var i = 0; i < addresses.Count; i++) {
      if (string.Equals(addresses[i], normalizedSliceAddress, StringComparison.OrdinalIgnoreCase)) return;
    }
    addresses.Add(normalizedSliceAddress);
  }

  static void RemoveAddressFromAtlasLookup(Dictionary<string, List<string>> atlasMap, string atlasAssetPath, string sliceAddress) {
    if (atlasMap == null || string.IsNullOrWhiteSpace(sliceAddress)) return;
    var normalizedAtlasPath = atlasAssetPath;
    var normalizedSliceAddress = sliceAddress;
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath)) {
      if (!SpriteSliceAddressUtility.TryParseSliceAddress(sliceAddress, out var parsedAtlasAssetPath, out _)) return;
      normalizedAtlasPath = NormalizeToken(parsedAtlasAssetPath);
      normalizedSliceAddress = NormalizeToken(sliceAddress);
    }
    if (string.IsNullOrWhiteSpace(normalizedAtlasPath) || string.IsNullOrWhiteSpace(normalizedSliceAddress)) return;
    if (!atlasMap.TryGetValue(normalizedAtlasPath, out var addresses) || addresses == null) return;

    for (var i = addresses.Count - 1; i >= 0; i--) {
      if (!string.Equals(addresses[i], normalizedSliceAddress, StringComparison.OrdinalIgnoreCase)) continue;
      addresses.RemoveAt(i);
      break;
    }

    if (addresses.Count <= 0) {
      atlasMap.Remove(normalizedAtlasPath);
    }
  }

  static void CacheLookupHit(LookupCacheKey key, SpriteAddressPair pair) {
    if (lookupHitCache.Count + lookupMissCache.Count >= MaxLookupCacheEntries) {
      lookupHitCache.Clear();
      lookupMissCache.Clear();
    }
    lookupMissCache.Remove(key);
    lookupHitCache[key] = pair;
  }

  static void CacheLookupMiss(LookupCacheKey key) {
    if (lookupHitCache.Count + lookupMissCache.Count >= MaxLookupCacheEntries) {
      lookupHitCache.Clear();
      lookupMissCache.Clear();
    }
    if (lookupHitCache.ContainsKey(key)) return;
    lookupMissCache.Add(key);
  }

  static void InvalidateLookupCacheEntry(LookupCacheKey key) {
    lookupHitCache.Remove(key);
    lookupMissCache.Remove(key);
  }

  static string ResolveShardKey(string normalizedNamepart) {
    if (string.IsNullOrWhiteSpace(normalizedNamepart)) return "";
    if ((manifestReady || EnsureManifestReady()) &&
        TryGetManifestEntryForNamepart(manifestByNamepart, normalizedNamepart, out var shardEntry)) {
      return string.IsNullOrWhiteSpace(shardEntry.namepart) ? normalizedNamepart : shardEntry.namepart;
    }
    return normalizedNamepart;
  }

  static Dictionary<string, ManifestEntry> ParseManifestRows(string text, bool allowUnityLogging = true) {
    var rows = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
    if (string.IsNullOrWhiteSpace(text)) return rows;

    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < lines.Length; i++) {
      var line = lines[i].TrimStart('\uFEFF');
      if (line.StartsWith("#", StringComparison.Ordinal)) continue;

      var cols = line.Split('\t');
      if (cols.Length < 3) continue;

      var normalizedNamepart = NormalizeNamePart(Unescape(cols[0]));
      var address = NormalizeToken(Unescape(cols[1]));
      var assetPath = NormalizeToken(Unescape(cols[2]));
      if (string.IsNullOrWhiteSpace(normalizedNamepart) || string.IsNullOrWhiteSpace(address)) continue;

      if (allowUnityLogging && rows.ContainsKey(normalizedNamepart)) {
        RateLimitedWarning(
          "manifest-duplicate:" + normalizedNamepart,
          "[SpriteRuntimeResolver] Duplicate manifest row for namepart '" + normalizedNamepart + "'. Last row wins."
        );
      }

      rows[normalizedNamepart] = new ManifestEntry {
        namepart = normalizedNamepart,
        address = address,
        assetPath = assetPath
      };
    }

    AddShortNamepartAliases(rows);
    return rows;
  }

  static void AddShortNamepartAliases(Dictionary<string, ManifestEntry> rows) {
    if (rows.Count == 0) return;
    ambiguousShortNamepartMatches.Clear();

    var canonicalKeys = new List<string>(rows.Keys);
    var aliasCandidates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < canonicalKeys.Count; i++) {
      var canonical = canonicalKeys[i];
      if (string.IsNullOrWhiteSpace(canonical)) continue;
      var slash = canonical.LastIndexOf('/');
      if (slash < 0 || slash >= canonical.Length - 1) continue;

      var alias = NormalizeNamePart(canonical.Substring(slash + 1));
      if (string.IsNullOrWhiteSpace(alias)) continue;
      if (rows.ContainsKey(alias)) continue;

      if (!aliasCandidates.TryGetValue(alias, out var candidates)) {
        candidates = new List<string>();
        aliasCandidates[alias] = candidates;
      }
      if (!ContainsIgnoreCase(candidates, canonical)) {
        candidates.Add(canonical);
      }
    }

    foreach (var pair in aliasCandidates) {
      var alias = pair.Key;
      var candidates = pair.Value;
      if (candidates == null || candidates.Count == 0) continue;

      candidates.Sort(StringComparer.OrdinalIgnoreCase);
      if (candidates.Count == 1) {
        if (!rows.TryGetValue(candidates[0], out var canonicalEntry)) continue;
        rows[alias] = canonicalEntry;
        continue;
      }

      ambiguousShortNamepartMatches[alias] = new List<string>(candidates);
    }
  }

  static bool ContainsIgnoreCase(List<string> values, string candidate) {
    if (values == null || string.IsNullOrWhiteSpace(candidate)) return false;
    for (var i = 0; i < values.Count; i++) {
      if (string.Equals(values[i], candidate, StringComparison.OrdinalIgnoreCase)) return true;
    }
    return false;
  }

  static string BuildShortKeyAmbiguityError(string shortKey, List<string> matches) {
    if (matches == null || matches.Count == 0) {
      return "Short name '" + shortKey + "' appears in multiple places.";
    }

    var folders = new List<string>();
    for (var i = 0; i < matches.Count; i++) {
      var match = matches[i];
      if (string.IsNullOrWhiteSpace(match)) continue;
      var slash = match.LastIndexOf('/');
      var folder = slash > 0 ? match.Substring(0, slash) : "(root)";
      if (!ContainsIgnoreCase(folders, folder)) {
        folders.Add(folder);
      }
    }

    folders.Sort(StringComparer.OrdinalIgnoreCase);
    var folderPhrase = JoinAsEnglishList(folders);
    var matchPhrase = "'" + string.Join("', '", matches) + "'";
    var canonicalOptions = string.Join(", ", matches);

    return "Short name '" + shortKey + "' appears in multiple places (" + folderPhrase + "). Matches " + matchPhrase + ". Use one of these canonical full-path nameparts: " + canonicalOptions + ".";
  }

  static string JoinAsEnglishList(List<string> values) {
    if (values == null || values.Count == 0) return "(none)";
    if (values.Count == 1) return values[0];
    if (values.Count == 2) return values[0] + " and " + values[1];
    return string.Join(", ", values.GetRange(0, values.Count - 1)) + ", and " + values[values.Count - 1];
  }

  static ParsedShardData ParseShardRows(string text, bool allowUnityLogging = true) {
    var parsedShard = new ParsedShardData {
      rows = new Dictionary<string, SpriteAddressPair>(StringComparer.Ordinal),
      addressesByAtlasPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
    };
    if (string.IsNullOrWhiteSpace(text)) return parsedShard;

    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < lines.Length; i++) {
      var line = lines[i].TrimStart('\uFEFF');
      if (line.StartsWith("#", StringComparison.Ordinal)) continue;

      var cols = line.Split('\t');
      if (cols.Length < 5) continue;

      var form = NormalizeToken(Unescape(cols[0]));
      var animation = NormalizeToken(Unescape(cols[1]));
      if (!int.TryParse(cols[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame)) continue;

      var key = BuildRowKey(form, animation, frame);
      if (allowUnityLogging && parsedShard.rows.ContainsKey(key)) {
        RateLimitedWarning(
          "shard-duplicate:" + key,
          "[SpriteRuntimeResolver] Duplicate shard row for key '" + key + "'. Last row wins."
        );
      }
      if (parsedShard.rows.TryGetValue(key, out var previousPair)) {
        RemoveAddressFromAtlasLookup(parsedShard.addressesByAtlasPath, previousPair.colorAtlasAddress, previousPair.colorAddress);
        RemoveAddressFromAtlasLookup(parsedShard.addressesByAtlasPath, previousPair.normalAtlasAddress, previousPair.normalAddress);
      }

      var spritePair = SpriteAddressPair.Create(
        NormalizeToken(Unescape(cols[3])),
        NormalizeToken(Unescape(cols[4]))
      );
      parsedShard.rows[key] = spritePair;
      AddAddressToAtlasLookup(parsedShard.addressesByAtlasPath, spritePair.colorAtlasAddress, spritePair.colorAddress);
      AddAddressToAtlasLookup(parsedShard.addressesByAtlasPath, spritePair.normalAtlasAddress, spritePair.normalAddress);
    }

    return parsedShard;
  }

  // TODO: both methods update the cooldown but never emit the message — all resolver diagnostics
  // are silently dropped. Add Debug.Log(message) / Debug.LogWarning(message) after the cooldown update.
  static void RateLimitedLog(string key, string message) {
    var now = Time.realtimeSinceStartup;
    if (logCooldown.TryGetValue(key, out var last) && now - last < 5f) return;
    logCooldown[key] = now;
    Debug.Log(message);
  }

  static void RateLimitedWarning(string key, string message, Object logContext = null) {
    var now = Time.realtimeSinceStartup;
    if (logCooldown.TryGetValue(key, out var last) && now - last < 5f) return;
    logCooldown[key] = now;
    Debug.LogWarning(message, logContext);
  }

  static string BuildContextualWarningKey(string key, Object logContext) {
    if (logContext == null) return key ?? "";
    return (key ?? "") + "|ctx:" + ObjectEntityId.GetRawValue(logContext);
  }

  static string AppendWarningContext(string message, Object logContext) {
    if (logContext == null) return message ?? "";
    return (message ?? "") + " gameobject='" + logContext.name + "'";
  }

  static string BuildRowKey(string form, string animation, int frame) {
    return NormalizeToken(form) + "|" + NormalizeToken(animation) + "|" + frame;
  }

  static ResolverSettings GetSettings() {
    if (settingsLoaded) return settings;
    settingsLoaded = true;
    settings = new ResolverSettings {
      manifestAddress = RuntimeConfig.DefaultManifestAddress,
      maxLoadedShards = 48
    };

    var settingsAsset = Resources.Load<SpriteStreamingSettings>("SpriteStreamingSettings");
    if (settingsAsset != null) {
      settings.manifestAddress = string.IsNullOrWhiteSpace(settingsAsset.manifestAddress)
        ? RuntimeConfig.DefaultManifestAddress
        : settingsAsset.manifestAddress;
      settings.maxLoadedShards = Math.Max(settingsAsset.maxLoadedShards, 1);
    }

    return settings;
  }

  static string NormalizeToken(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    if (tokenNormCache.TryGetValue(value, out var cached)) return cached;

    var trimmed = value.Trim();
    if (trimmed.Length >= 2) {
      var first = trimmed[0];
      var last = trimmed[trimmed.Length - 1];
      if ((first == '"' && last == '"') || (first == '\'' && last == '\'')) {
        trimmed = trimmed.Substring(1, trimmed.Length - 2);
        if (first == '\'') {
          trimmed = trimmed.Replace("''", "'");
        }
      }
    }

    var result = string.IsNullOrWhiteSpace(trimmed) ? "" : trimmed.Trim();
    tokenNormCache[value] = result;
    return result;
  }

  static string Unescape(string value) {
    if (string.IsNullOrEmpty(value)) return "";
    return value
      .Replace("\\t", "\t")
      .Replace("\\n", "\n")
      .Replace("\\r", "\r")
      .Replace("\\\\", "\\");
  }

  static string CollapseSlashes(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    if (value.IndexOf("//", StringComparison.Ordinal) < 0) return value;

    var sb = new System.Text.StringBuilder(value.Length);
    var previousWasSlash = false;
    for (var i = 0; i < value.Length; i++) {
      var ch = value[i];
      if (ch == '/') {
        if (previousWasSlash) continue;
        previousWasSlash = true;
      }
      else {
        previousWasSlash = false;
      }
      sb.Append(ch);
    }

    return sb.ToString();
  }
}
