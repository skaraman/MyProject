using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class SpriteRuntimeResolver {
  static class RuntimeConfig {
    public const string SourceRootFolder = "Assets/Sprites/SpriteLibraries";
    public const string ManifestAssetPath = "Assets/Sprites/SpriteLibraries/SpriteIndexManifest.bytes";
    public const string DefaultManifestAddress = "SpriteRuntimeIndex/Manifest";
  }

  struct ManifestEntry {
    public string namepart;
    public string address;
    public string assetPath;
  }

  sealed class ShardData {
    public Dictionary<string, SpriteAddressPair> rows;
    public long lastAccessTicks;
  }

  static readonly Dictionary<string, ManifestEntry> manifestByNamepart = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, ShardData> loadedShards = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, AsyncOperationHandle<TextAsset>> shardLoads = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, float> logCooldown = new(StringComparer.OrdinalIgnoreCase);

  static AsyncOperationHandle<TextAsset> manifestLoad;
  static bool manifestLoadStarted;
  static bool manifestReady;
  static bool manifestFailed;
#if UNITY_EDITOR
  readonly struct EditorSpriteRef {
    public readonly string guid;
    public readonly long fileId;

    public EditorSpriteRef(string guid, long fileId) {
      this.guid = guid;
      this.fileId = fileId;
    }
  }

  static readonly Regex editorSpriteRefRegex = new(@"^\s*m_Sprite(?:Override)?: \{fileID:\s*([^,]+), guid:\s*([0-9a-fA-F]{32}),", RegexOptions.Compiled);
  static readonly Dictionary<string, string> editorLibraryPathsByKey = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, Dictionary<string, EditorSpriteRef>> editorRowsByLibraryPath = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, Dictionary<long, string>> editorAddressMapByGuid = new(StringComparer.OrdinalIgnoreCase);
  static bool editorLibraryCacheInitialized;
  static bool editorManifestRebuildAttempted;
#endif

  struct ResolverSettings {
    public string manifestAddress;
    public int maxLoadedShards;
  }

  static ResolverSettings settings;
  static bool settingsLoaded;

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
    loadedShards.Clear();
    shardLoads.Clear();
    logCooldown.Clear();

    manifestLoadStarted = false;
    manifestReady = false;
    manifestFailed = false;
    manifestLoad = default;
#if UNITY_EDITOR
    editorManifestRebuildAttempted = false;
    editorLibraryCacheInitialized = false;
    editorLibraryPathsByKey.Clear();
    editorRowsByLibraryPath.Clear();
    editorAddressMapByGuid.Clear();
#endif

    settingsLoaded = false;
    settings = default;
  }

  public static bool TryResolve(SpriteLookupKey key, out SpriteAddressPair pair) {
    pair = default;
    if (!EnsureManifestReady()) return false;

    var normalizedNamepart = NormalizeNamePart(key.namepart);
    if (string.IsNullOrEmpty(normalizedNamepart)) return false;
    if (!manifestByNamepart.TryGetValue(normalizedNamepart, out var shardEntry)) return false;

    if (!TryGetShard(normalizedNamepart, shardEntry, out var shard)) return false;

    var exactKey = BuildRowKey(key.form, key.animation, key.frame);
    if (shard.rows.TryGetValue(exactKey, out pair)) {
      shard.lastAccessTicks = DateTime.UtcNow.Ticks;
      return true;
    }

    if (key.frame != 0) {
      var frameZeroKey = BuildRowKey(key.form, key.animation, 0);
      if (shard.rows.TryGetValue(frameZeroKey, out pair)) {
        shard.lastAccessTicks = DateTime.UtcNow.Ticks;
        return true;
      }
    }

    if (TryResolveNumericFormFallback(shard.rows, key, out pair)) {
      shard.lastAccessTicks = DateTime.UtcNow.Ticks;
      return true;
    }

    return false;
  }

  public static string NormalizeNamePart(string value) {
    var normalized = NormalizeToken(value).Replace('\\', '/');
    if (normalized.EndsWith(".spriteLib", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(0, normalized.Length - ".spriteLib".Length);
    }

    var root = NormalizeToken(RuntimeConfig.SourceRootFolder).Replace('\\', '/');
    if (normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(root.Length + 1);
    }

    return normalized;
  }

#if UNITY_EDITOR
  public static bool TryResolveEditor(SpriteLookupKey key, out SpriteAddressPair pair) {
    pair = default;
    var manifestAsset = LoadEditorManifestAsset();
    if (manifestAsset != null && !string.IsNullOrWhiteSpace(manifestAsset.text)) {
      var manifestRows = ParseManifestRows(manifestAsset.text);
      if (manifestRows.Count > 0) {
        var normalizedNamepart = NormalizeNamePart(key.namepart);
        if (!string.IsNullOrEmpty(normalizedNamepart) &&
            manifestRows.TryGetValue(normalizedNamepart, out var shardEntry) &&
            !string.IsNullOrWhiteSpace(shardEntry.assetPath)) {
          var shardAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(shardEntry.assetPath);
          if (shardAsset != null && !string.IsNullOrWhiteSpace(shardAsset.text)) {
            var rows = ParseShardRows(shardAsset.text);
            if (rows != null && rows.Count > 0) {
              var exactKey = BuildRowKey(key.form, key.animation, key.frame);
              if (rows.TryGetValue(exactKey, out pair)) return true;

              if (key.frame != 0) {
                var frameZeroKey = BuildRowKey(key.form, key.animation, 0);
                if (rows.TryGetValue(frameZeroKey, out pair)) return true;
              }

              if (TryResolveNumericFormFallback(rows, key, out pair)) return true;
            }
          }
        }
      }
    }

    return TryResolveDirectFromSpriteLibraries(key, out pair);
  }

  static TextAsset LoadEditorManifestAsset() {
    var manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(RuntimeConfig.ManifestAssetPath);
    if (manifestAsset != null && !string.IsNullOrWhiteSpace(manifestAsset.text)) {
      return manifestAsset;
    }

    if (editorManifestRebuildAttempted) return manifestAsset;
    editorManifestRebuildAttempted = true;
    TryRebuildRuntimeIndexInEditor();

    manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(RuntimeConfig.ManifestAssetPath);
    return manifestAsset;
  }

  static void TryRebuildRuntimeIndexInEditor() {
    Type builderType = null;
    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
    for (var i = 0; i < assemblies.Length; i++) {
      builderType = assemblies[i].GetType("SpriteIndexBuilder");
      if (builderType != null) break;
    }

    if (builderType == null) return;
    var rebuildMethod = builderType.GetMethod("RebuildRuntimeIndex", BindingFlags.Public | BindingFlags.Static);
    if (rebuildMethod == null) return;

    try {
      rebuildMethod.Invoke(null, new object[] { false, false });
      AssetDatabase.Refresh();
    }
    catch {
    }
  }

  static bool TryResolveDirectFromSpriteLibraries(SpriteLookupKey key, out SpriteAddressPair pair) {
    pair = default;
    EnsureEditorLibraryCache();

    var canonicalNamepart = ResolveEditorCanonicalNamepart(key.namepart, out var ambiguityError);
    if (!string.IsNullOrWhiteSpace(ambiguityError)) {
      RateLimitedLog("shortkey-ambiguous-editor:" + NormalizeNamePart(key.namepart), "[SpriteRuntimeResolver] " + ambiguityError);
      return false;
    }

    if (string.IsNullOrWhiteSpace(canonicalNamepart)) return false;
    if (!editorLibraryPathsByKey.TryGetValue(canonicalNamepart, out var colorLibraryPath)) return false;

    var normalNamepart = canonicalNamepart + "N";
    if (!editorLibraryPathsByKey.TryGetValue(normalNamepart, out var normalLibraryPath)) {
      RateLimitedLog(
        "missing-normal-library-editor:" + canonicalNamepart,
        "[SpriteRuntimeResolver] Missing normal library '" + normalNamepart + "' for '" + canonicalNamepart + "'."
      );
      return false;
    }

    var colorRows = ParseLibraryRowsEditor(colorLibraryPath);
    var normalRows = ParseLibraryRowsEditor(normalLibraryPath);
    if (colorRows == null || normalRows == null || colorRows.Count == 0 || normalRows.Count == 0) return false;

    if (TryResolveDirectAtFrame(colorRows, normalRows, key, key.frame, out pair)) return true;
    if (key.frame != 0 && TryResolveDirectAtFrame(colorRows, normalRows, key, 0, out pair)) return true;

    return false;
  }

  static bool TryResolveDirectAtFrame(
    Dictionary<string, EditorSpriteRef> colorRows,
    Dictionary<string, EditorSpriteRef> normalRows,
    SpriteLookupKey key,
    int frame,
    out SpriteAddressPair pair
  ) {
    pair = default;
    var animation = NormalizeToken(key.animation);
    if (string.IsNullOrWhiteSpace(animation)) return false;

    var labels = BuildLabelCandidates(key.form, frame);
    for (var i = 0; i < labels.Count; i++) {
      var label = labels[i];
      var rowKey = animation + "\u001f" + label;
      if (!colorRows.TryGetValue(rowKey, out var colorRef)) continue;
      if (!normalRows.TryGetValue(rowKey, out var normalRef)) continue;

      var colorAddress = ResolveEditorSpriteAddress(colorRef);
      var normalAddress = ResolveEditorSpriteAddress(normalRef);
      if (string.IsNullOrWhiteSpace(colorAddress) || string.IsNullOrWhiteSpace(normalAddress)) continue;

      pair = new SpriteAddressPair {
        colorAddress = colorAddress,
        normalAddress = normalAddress
      };
      return true;
    }

    return false;
  }

  static bool TryResolveNumericFormFallback(Dictionary<string, SpriteAddressPair> rows, SpriteLookupKey key, out SpriteAddressPair pair) {
    pair = default;
    if (rows == null || rows.Count == 0) return false;
    if (key.frame != 0) return false;

    var normalizedForm = NormalizeToken(key.form);
    if (string.IsNullOrWhiteSpace(normalizedForm)) return false;
    if (!int.TryParse(normalizedForm, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericFrame)) return false;
    if (numericFrame < 0) return false;

    var aliasKey = BuildRowKey("", key.animation, numericFrame);
    return rows.TryGetValue(aliasKey, out pair);
  }

  static List<string> BuildLabelCandidates(string form, int frame) {
    var labels = new List<string>(3);
    var normalizedForm = NormalizeToken(form);

    if (frame == 0) {
      AddUniqueLabel(labels, normalizedForm);
      if (!string.IsNullOrWhiteSpace(normalizedForm)) {
        AddUniqueLabel(labels, normalizedForm + "_0");
      }
      else {
        AddUniqueLabel(labels, "0");
      }
      return labels;
    }

    var frameText = frame.ToString(CultureInfo.InvariantCulture);
    if (!string.IsNullOrWhiteSpace(normalizedForm)) {
      AddUniqueLabel(labels, normalizedForm + "_" + frameText);
    }
    else {
      AddUniqueLabel(labels, frameText);
    }

    return labels;
  }

  static void AddUniqueLabel(List<string> labels, string value) {
    if (labels == null || string.IsNullOrWhiteSpace(value)) return;
    for (var i = 0; i < labels.Count; i++) {
      if (string.Equals(labels[i], value, StringComparison.OrdinalIgnoreCase)) return;
    }
    labels.Add(value);
  }

  static string ResolveEditorCanonicalNamepart(string requestedNamepart, out string ambiguityError) {
    ambiguityError = "";
    var normalizedRequested = NormalizeNamePart(requestedNamepart);
    if (string.IsNullOrWhiteSpace(normalizedRequested)) return "";

    if (editorLibraryPathsByKey.ContainsKey(normalizedRequested)) return normalizedRequested;

    var suffix = "/" + normalizedRequested;
    var matches = new List<string>();
    foreach (var key in editorLibraryPathsByKey.Keys) {
      if (key.EndsWith("N", StringComparison.OrdinalIgnoreCase)) continue;
      if (!key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
      matches.Add(key);
    }

    if (matches.Count == 1) return matches[0];
    if (matches.Count <= 1) return "";

    matches.Sort(StringComparer.OrdinalIgnoreCase);
    ambiguityError = BuildShortKeyAmbiguityError(normalizedRequested, matches);
    return "";
  }

  static void EnsureEditorLibraryCache() {
    if (editorLibraryCacheInitialized) return;
    editorLibraryCacheInitialized = true;
    editorLibraryPathsByKey.Clear();

    var root = NormalizeEditorPath(RuntimeConfig.SourceRootFolder);
    if (!Directory.Exists(root)) return;

    var files = Directory.GetFiles(root, "*.spriteLib", SearchOption.AllDirectories);
    Array.Sort(files, StringComparer.Ordinal);

    for (var i = 0; i < files.Length; i++) {
      var path = NormalizeEditorPath(files[i]);
      var relative = path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
        ? path.Substring(root.Length + 1)
        : path;
      var key = RemoveSpriteLibExtension(relative);
      editorLibraryPathsByKey[key] = path;
    }
  }

  static Dictionary<string, EditorSpriteRef> ParseLibraryRowsEditor(string path) {
    var normalizedPath = NormalizeEditorPath(path);
    if (editorRowsByLibraryPath.TryGetValue(normalizedPath, out var cachedRows)) {
      return cachedRows;
    }

    var rows = new Dictionary<string, EditorSpriteRef>(StringComparer.OrdinalIgnoreCase);
    if (!File.Exists(normalizedPath)) {
      editorRowsByLibraryPath[normalizedPath] = rows;
      return rows;
    }

    string currentCategory = null;
    var insideOverrideEntries = false;
    string currentLabel = null;
    EditorSpriteRef? currentSpriteRef = null;

    void FlushLabel() {
      if (string.IsNullOrWhiteSpace(currentCategory) || string.IsNullOrWhiteSpace(currentLabel) || !currentSpriteRef.HasValue) {
        currentLabel = null;
        currentSpriteRef = null;
        return;
      }

      rows[currentCategory + "\u001f" + currentLabel] = currentSpriteRef.Value;
      currentLabel = null;
      currentSpriteRef = null;
    }

    foreach (var rawLine in File.ReadLines(normalizedPath)) {
      var line = rawLine ?? "";

      if (line.StartsWith("  - m_Name:", StringComparison.Ordinal)) {
        FlushLabel();
        currentCategory = DecodeYamlScalar(line.Substring("  - m_Name:".Length));
        insideOverrideEntries = false;
        continue;
      }

      if (line.StartsWith("    m_OverrideEntries:", StringComparison.Ordinal)) {
        insideOverrideEntries = true;
        continue;
      }

      if (!insideOverrideEntries) continue;

      if (line.StartsWith("    - m_Name:", StringComparison.Ordinal)) {
        FlushLabel();
        currentLabel = DecodeYamlScalar(line.Substring("    - m_Name:".Length));
        currentSpriteRef = null;
        continue;
      }

      if (string.IsNullOrWhiteSpace(currentLabel)) continue;

      var spriteMatch = editorSpriteRefRegex.Match(line);
      if (!spriteMatch.Success) continue;

      if (!long.TryParse(spriteMatch.Groups[1].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileId)) continue;
      var guid = spriteMatch.Groups[2].Value.Trim();
      currentSpriteRef = new EditorSpriteRef(guid, fileId);
    }

    FlushLabel();
    editorRowsByLibraryPath[normalizedPath] = rows;
    return rows;
  }

  static string ResolveEditorSpriteAddress(EditorSpriteRef spriteRef) {
    if (string.IsNullOrWhiteSpace(spriteRef.guid)) return "";

    if (!editorAddressMapByGuid.TryGetValue(spriteRef.guid, out var byFileId)) {
      byFileId = BuildEditorAddressMapForGuid(spriteRef.guid);
      editorAddressMapByGuid[spriteRef.guid] = byFileId;
    }

    if (byFileId.TryGetValue(spriteRef.fileId, out var address)) return address;

    var targetUnsigned = unchecked((ulong)spriteRef.fileId);
    foreach (var pair in byFileId) {
      if (unchecked((ulong)pair.Key) != targetUnsigned) continue;
      return pair.Value;
    }

    return "";
  }

  static Dictionary<long, string> BuildEditorAddressMapForGuid(string guid) {
    var map = new Dictionary<long, string>();
    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
    if (string.IsNullOrWhiteSpace(assetPath)) return map;

    var metaPath = assetPath + ".meta";
    if (!File.Exists(metaPath)) return map;
    if (!TryParseInternalIdToNameTable(metaPath, map)) return map;
    TryParseNameFileIdTable(metaPath, map);

    var keys = new List<long>(map.Keys);
    for (var i = 0; i < keys.Count; i++) {
      var localId = keys[i];
      var spriteName = map[localId];
      map[localId] = assetPath + "[" + spriteName + "]";
    }

    return map;
  }

  static bool TryParseInternalIdToNameTable(string metaPath, Dictionary<long, string> target) {
    if (target == null || string.IsNullOrWhiteSpace(metaPath) || !File.Exists(metaPath)) return false;

    var inTable = false;
    var hasPendingId = false;
    long pendingId = 0;

    foreach (var rawLine in File.ReadLines(metaPath)) {
      var line = rawLine ?? "";
      var trimmed = line.Trim();

      if (!inTable) {
        if (trimmed.StartsWith("internalIDToNameTable:", StringComparison.Ordinal)) {
          inTable = true;
        }
        continue;
      }

      if (trimmed.StartsWith("externalObjects:", StringComparison.Ordinal) ||
          trimmed.StartsWith("serializedVersion:", StringComparison.Ordinal)) {
        break;
      }

      if (trimmed.StartsWith("213:", StringComparison.Ordinal)) {
        var idValue = trimmed.Substring("213:".Length).Trim();
        if (long.TryParse(idValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId)) {
          pendingId = parsedId;
          hasPendingId = true;
        }
        else {
          hasPendingId = false;
        }
        continue;
      }

      if (!hasPendingId) continue;
      if (!trimmed.StartsWith("second:", StringComparison.Ordinal)) continue;

      var spriteName = DecodeYamlScalar(trimmed.Substring("second:".Length));
      if (!string.IsNullOrWhiteSpace(spriteName)) {
        target[pendingId] = spriteName;
      }
      hasPendingId = false;
    }

    return true;
  }

  static bool TryParseNameFileIdTable(string metaPath, Dictionary<long, string> target) {
    if (target == null || string.IsNullOrWhiteSpace(metaPath) || !File.Exists(metaPath)) return false;

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

      var spriteName = DecodeYamlScalar(trimmed.Substring(0, separatorIndex));
      if (string.IsNullOrWhiteSpace(spriteName)) continue;

      var idValue = trimmed.Substring(separatorIndex + 1).Trim();
      if (!long.TryParse(idValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileId)) continue;

      target[fileId] = spriteName;
    }

    return true;
  }

  static string DecodeYamlScalar(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    var trimmed = value.Trim();
    if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') {
      trimmed = trimmed.Substring(1, trimmed.Length - 2);
    }
    return trimmed;
  }

  static string RemoveSpriteLibExtension(string value) {
    var normalized = NormalizeEditorPath(value);
    return normalized.EndsWith(".spriteLib", StringComparison.OrdinalIgnoreCase)
      ? normalized.Substring(0, normalized.Length - ".spriteLib".Length)
      : normalized;
  }

  static string NormalizeEditorPath(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Replace('\\', '/').Trim();
  }

  public static bool TryLoadEditorSprite(string address, out Sprite sprite) {
    sprite = null;
    if (string.IsNullOrWhiteSpace(address)) return false;

    var normalizedAddress = address.Trim();
    var assetPath = normalizedAddress;
    var spriteName = "";

    var bracketIndex = normalizedAddress.LastIndexOf('[');
    if (bracketIndex > 0 && normalizedAddress.EndsWith("]", StringComparison.Ordinal)) {
      assetPath = normalizedAddress.Substring(0, bracketIndex);
      spriteName = normalizedAddress.Substring(bracketIndex + 1, normalizedAddress.Length - bracketIndex - 2);
    }

    assetPath = assetPath.Replace('\\', '/');
    if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return false;

    var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
    for (var i = 0; i < assets.Length; i++) {
      var candidate = assets[i] as Sprite;
      if (candidate == null) continue;
      if (!string.IsNullOrEmpty(spriteName) && !string.Equals(candidate.name, spriteName, StringComparison.Ordinal)) continue;
      sprite = candidate;
      return true;
    }

    return false;
  }
#endif

  static bool EnsureManifestReady() {
    if (manifestReady) return true;
    if (manifestFailed) return false;

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
          RateLimitedLog("manifest:" + manifestAddress, "[SpriteRuntimeResolver] Failed to load sprite index manifest at address '" + manifestAddress + "'.");
          if (operation.IsValid()) {
            Addressables.Release(operation);
          }
          return;
        }

        manifestByNamepart.Clear();
        var parsed = ParseManifestRows(operation.Result.text);
        foreach (var pair in parsed) {
          manifestByNamepart[pair.Key] = pair.Value;
        }

        manifestReady = true;
        if (operation.IsValid()) {
          Addressables.Release(operation);
        }
      };
    }

    return manifestReady;
  }

  static bool TryGetShard(string namepart, ManifestEntry entry, out ShardData shard) {
    if (loadedShards.TryGetValue(namepart, out shard)) {
      shard.lastAccessTicks = DateTime.UtcNow.Ticks;
      return true;
    }

    if (shardLoads.ContainsKey(namepart)) return false;
    if (string.IsNullOrWhiteSpace(entry.address)) return false;

    var shardAddress = entry.address.Trim();
    var load = Addressables.LoadAssetAsync<TextAsset>(shardAddress);
    shardLoads[namepart] = load;
    load.Completed += operation => {
      shardLoads.Remove(namepart);

      if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null) {
        RateLimitedLog("shardload:" + namepart, "[SpriteRuntimeResolver] Failed to load shard for '" + namepart + "' at address '" + shardAddress + "'.");
        if (operation.IsValid()) {
          Addressables.Release(operation);
        }
        return;
      }

      var parsedRows = ParseShardRows(operation.Result.text);
      loadedShards[namepart] = new ShardData {
        rows = parsedRows,
        lastAccessTicks = DateTime.UtcNow.Ticks
      };

      EnforceShardBudget();

      if (operation.IsValid()) {
        Addressables.Release(operation);
      }
    };

    return false;
  }

  static void EnforceShardBudget() {
    var cfg = GetSettings();
    var maxLoaded = Math.Max(cfg.maxLoadedShards, 1);

    while (loadedShards.Count > maxLoaded) {
      string oldestKey = null;
      long oldestTicks = long.MaxValue;

      foreach (var pair in loadedShards) {
        if (pair.Value == null) continue;
        if (pair.Value.lastAccessTicks >= oldestTicks) continue;
        oldestTicks = pair.Value.lastAccessTicks;
        oldestKey = pair.Key;
      }

      if (oldestKey == null) break;
      loadedShards.Remove(oldestKey);
    }
  }

  static Dictionary<string, ManifestEntry> ParseManifestRows(string text) {
    var rows = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
    if (string.IsNullOrWhiteSpace(text)) return rows;

    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < lines.Length; i++) {
      var line = lines[i];
      if (line.StartsWith("#", StringComparison.Ordinal)) continue;

      var cols = line.Split('\t');
      if (cols.Length < 3) continue;

      var normalizedNamepart = NormalizeNamePart(Unescape(cols[0]));
      var address = Unescape(cols[1]);
      var assetPath = Unescape(cols[2]);
      if (string.IsNullOrWhiteSpace(normalizedNamepart) || string.IsNullOrWhiteSpace(address)) continue;

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

      RateLimitedLog(
        "shortkey-ambiguous:" + alias,
        "[SpriteRuntimeResolver] " + BuildShortKeyAmbiguityError(alias, candidates)
      );
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

    return "Short name '" + shortKey + "' appears in multiple places (" + folderPhrase + "). Matches " + matchPhrase + ". Use canonical full-path namepart.";
  }

  static string JoinAsEnglishList(List<string> values) {
    if (values == null || values.Count == 0) return "(none)";
    if (values.Count == 1) return values[0];
    if (values.Count == 2) return values[0] + " and " + values[1];
    return string.Join(", ", values.GetRange(0, values.Count - 1)) + ", and " + values[values.Count - 1];
  }

  static Dictionary<string, SpriteAddressPair> ParseShardRows(string text) {
    var rows = new Dictionary<string, SpriteAddressPair>(StringComparer.OrdinalIgnoreCase);
    if (string.IsNullOrWhiteSpace(text)) return rows;

    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < lines.Length; i++) {
      var line = lines[i];
      var cols = line.Split('\t');
      if (cols.Length < 5) continue;

      var form = Unescape(cols[0]);
      var animation = Unescape(cols[1]);
      if (!int.TryParse(cols[2], out var frame)) continue;

      var key = BuildRowKey(form, animation, frame);
      rows[key] = new SpriteAddressPair {
        colorAddress = Unescape(cols[3]),
        normalAddress = Unescape(cols[4])
      };
    }

    return rows;
  }

  static void RateLimitedLog(string key, string message) {
    var now = Time.realtimeSinceStartup;
    if (logCooldown.TryGetValue(key, out var last) && now - last < 5f) return;
    logCooldown[key] = now;
    Debug.LogError(message);
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

    var settingsAsset = Resources.Load("SpriteStreamingSettings") as ScriptableObject;
    if (settingsAsset != null) {
      settings.manifestAddress = ReadStringSetting(settingsAsset, "manifestAddress", RuntimeConfig.DefaultManifestAddress);
      settings.maxLoadedShards = Math.Max(ReadIntSetting(settingsAsset, "maxLoadedShards", 48), 1);
    }

    return settings;
  }

  static int ReadIntSetting(ScriptableObject settingsAsset, string memberName, int fallback) {
    if (settingsAsset == null || string.IsNullOrWhiteSpace(memberName)) return fallback;

    var type = settingsAsset.GetType();
    var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (field != null && field.FieldType == typeof(int)) {
      return (int)field.GetValue(settingsAsset);
    }

    var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (property != null && property.PropertyType == typeof(int) && property.CanRead) {
      return (int)property.GetValue(settingsAsset);
    }

    return fallback;
  }

  static string ReadStringSetting(ScriptableObject settingsAsset, string memberName, string fallback) {
    if (settingsAsset == null || string.IsNullOrWhiteSpace(memberName)) return fallback;

    var type = settingsAsset.GetType();
    var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (field != null && field.FieldType == typeof(string)) {
      var value = (string)field.GetValue(settingsAsset);
      return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (property != null && property.PropertyType == typeof(string) && property.CanRead) {
      var value = (string)property.GetValue(settingsAsset);
      return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    return fallback;
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  static string Unescape(string value) {
    if (string.IsNullOrEmpty(value)) return "";
    return value
      .Replace("\\t", "\t")
      .Replace("\\n", "\n")
      .Replace("\\r", "\r")
      .Replace("\\\\", "\\");
  }
}
