using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class SpriteAddressResolver {
  static bool reflectionInitialized;
  static Type runtimeResolverType;
  static MethodInfo tryResolveMethod;
  static MethodInfo normalizeNamePartMethod;
#if UNITY_EDITOR
  static MethodInfo tryResolveEditorMethod;
  static MethodInfo tryLoadEditorSpriteMethod;
  const string EditorManifestAssetPath = "Assets/Sprites/SpriteLibraries/SpriteIndexManifest.bytes";
  static DateTime editorManifestWriteTimeUtc;
  static readonly Dictionary<string, string> editorShardPathByNamepart = new(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string, Dictionary<string, SpriteAddressPair>> editorShardRowsByPath = new(StringComparer.OrdinalIgnoreCase);
#endif

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    reflectionInitialized = false;
    runtimeResolverType = null;
    tryResolveMethod = null;
    normalizeNamePartMethod = null;
#if UNITY_EDITOR
    tryResolveEditorMethod = null;
    tryLoadEditorSpriteMethod = null;
    editorManifestWriteTimeUtc = default;
    editorShardPathByNamepart.Clear();
    editorShardRowsByPath.Clear();
#endif
  }

  public static void Clear() {
  }

  public static bool TryResolve(SpriteLookupKey key, out SpriteAddressPair pair) {
    pair = default;
    EnsureReflectionInitialized();
#if UNITY_EDITOR
    if (!Application.isPlaying && tryResolveEditorMethod != null) {
      try {
        var editorArgs = new object[] { key, null };
        var editorResolved = (bool)tryResolveEditorMethod.Invoke(null, editorArgs);
        if (editorArgs[1] is SpriteAddressPair editorPair) {
          pair = editorPair;
        }
        if (editorResolved) return true;
      }
      catch {
      }
    }
#endif

    var runtimeResolved = false;
    if (tryResolveMethod != null) {
      try {
        var args = new object[] { key, null };
        runtimeResolved = (bool)tryResolveMethod.Invoke(null, args);
        if (args[1] is SpriteAddressPair resolvedPair) {
          pair = resolvedPair;
        }
        if (runtimeResolved) return true;
      }
      catch {
      }
    }

#if UNITY_EDITOR
    if (tryResolveEditorMethod != null) {
      try {
        var editorArgs = new object[] { key, null };
        var editorResolved = (bool)tryResolveEditorMethod.Invoke(null, editorArgs);
        if (editorArgs[1] is SpriteAddressPair editorPair) {
          pair = editorPair;
        }
        if (editorResolved) return true;
      }
      catch {
      }
    }

    if (TryResolveFromLocalManifest(key, out pair)) {
      return true;
    }
#endif

    return false;
  }

  public static string NormalizeNamePart(string value) {
    EnsureReflectionInitialized();
    if (normalizeNamePartMethod != null) {
      try {
        var normalized = normalizeNamePartMethod.Invoke(null, new object[] { value }) as string;
        if (normalized != null) return normalized;
      }
      catch {
      }
    }

    var normalizedLocal = NormalizeToken(value).Replace('\\', '/');
    if (normalizedLocal.EndsWith(".spriteLib", StringComparison.OrdinalIgnoreCase)) {
      normalizedLocal = normalizedLocal.Substring(0, normalizedLocal.Length - ".spriteLib".Length);
    }

    var root = NormalizeToken(SpriteAddressCatalogConfig.SourceRootFolder).Replace('\\', '/');
    if (normalizedLocal.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) {
      normalizedLocal = normalizedLocal.Substring(root.Length + 1);
    }
    return normalizedLocal;
  }

#if UNITY_EDITOR
  public static bool TryLoadEditorSprite(string address, out Sprite sprite) {
    sprite = null;
    EnsureReflectionInitialized();
    if (tryLoadEditorSpriteMethod != null) {
      try {
        var args = new object[] { address, null };
        var found = (bool)tryLoadEditorSpriteMethod.Invoke(null, args);
        if (args[1] is Sprite resolvedSprite) {
          sprite = resolvedSprite;
        }
        return found;
      }
      catch {
      }
    }

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

#if UNITY_EDITOR
  static bool TryResolveFromLocalManifest(SpriteLookupKey key, out SpriteAddressPair pair) {
    pair = default;
    if (!TryGetLocalShardPath(key.namepart, out var shardPath)) return false;
    if (!TryGetLocalShardRows(shardPath, out var rows)) return false;

    var exactKey = BuildLocalRowKey(key.form, key.animation, key.frame);
    if (rows.TryGetValue(exactKey, out pair)) return true;

    if (key.frame != 0) {
      var frameZeroKey = BuildLocalRowKey(key.form, key.animation, 0);
      if (rows.TryGetValue(frameZeroKey, out pair)) return true;
    }

    if (TryResolveLocalNumericFormFallback(rows, key, out pair)) return true;

    return false;
  }

  static bool TryGetLocalShardPath(string namepart, out string shardAssetPath) {
    shardAssetPath = "";
    if (!EnsureLocalManifestCache()) return false;

    var normalizedNamepart = NormalizeNamePart(namepart);
    if (string.IsNullOrWhiteSpace(normalizedNamepart)) return false;

    if (!editorShardPathByNamepart.TryGetValue(normalizedNamepart, out shardAssetPath)) {
      return false;
    }

    return !string.IsNullOrWhiteSpace(shardAssetPath);
  }

  static bool EnsureLocalManifestCache() {
    if (!File.Exists(EditorManifestAssetPath)) {
      editorManifestWriteTimeUtc = default;
      editorShardPathByNamepart.Clear();
      editorShardRowsByPath.Clear();
      return false;
    }

    var writeTime = File.GetLastWriteTimeUtc(EditorManifestAssetPath);
    if (writeTime == editorManifestWriteTimeUtc && editorShardPathByNamepart.Count > 0) {
      return true;
    }

    var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var rawLine in File.ReadLines(EditorManifestAssetPath)) {
      var line = rawLine ?? "";
      if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;

      var cols = line.Split('\t');
      if (cols.Length < 3) continue;

      var canonical = NormalizeNamePart(UnescapeLocal(cols[0]));
      var shardAssetPath = NormalizeEditorPath(UnescapeLocal(cols[2]));
      if (string.IsNullOrWhiteSpace(canonical) || string.IsNullOrWhiteSpace(shardAssetPath)) continue;

      parsed[canonical] = shardAssetPath;
    }

    AddLocalShortNameAliases(parsed);

    editorManifestWriteTimeUtc = writeTime;
    editorShardPathByNamepart.Clear();
    foreach (var pair in parsed) {
      editorShardPathByNamepart[pair.Key] = pair.Value;
    }
    editorShardRowsByPath.Clear();

    return editorShardPathByNamepart.Count > 0;
  }

  static void AddLocalShortNameAliases(Dictionary<string, string> rows) {
    if (rows == null || rows.Count == 0) return;

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

      if (!candidates.Contains(canonical)) {
        candidates.Add(canonical);
      }
    }

    foreach (var pair in aliasCandidates) {
      var alias = pair.Key;
      var candidates = pair.Value;
      if (candidates == null || candidates.Count != 1) continue;
      if (!rows.TryGetValue(candidates[0], out var canonicalPath)) continue;
      rows[alias] = canonicalPath;
    }
  }

  static bool TryGetLocalShardRows(string shardAssetPath, out Dictionary<string, SpriteAddressPair> rows) {
    rows = null;
    if (string.IsNullOrWhiteSpace(shardAssetPath)) return false;
    var normalizedPath = NormalizeEditorPath(shardAssetPath);

    if (editorShardRowsByPath.TryGetValue(normalizedPath, out var cachedRows)) {
      rows = cachedRows;
      return rows != null && rows.Count > 0;
    }

    string text = null;
    var shardAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(normalizedPath);
    if (shardAsset != null && !string.IsNullOrWhiteSpace(shardAsset.text)) {
      text = shardAsset.text;
    }
    else if (File.Exists(normalizedPath)) {
      text = File.ReadAllText(normalizedPath);
    }

    var parsedRows = new Dictionary<string, SpriteAddressPair>(StringComparer.OrdinalIgnoreCase);
    if (!string.IsNullOrWhiteSpace(text)) {
      var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
      for (var i = 0; i < lines.Length; i++) {
        var line = lines[i];
        if (string.IsNullOrWhiteSpace(line)) continue;

        var cols = line.Split('\t');
        if (cols.Length < 5) continue;

        var form = UnescapeLocal(cols[0]);
        var animation = UnescapeLocal(cols[1]);
        if (!int.TryParse(cols[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame)) continue;

        var rowKey = BuildLocalRowKey(form, animation, frame);
        parsedRows[rowKey] = new SpriteAddressPair {
          colorAddress = UnescapeLocal(cols[3]),
          normalAddress = UnescapeLocal(cols[4])
        };
      }
    }

    editorShardRowsByPath[normalizedPath] = parsedRows;
    rows = parsedRows;
    return rows.Count > 0;
  }

  static string BuildLocalRowKey(string form, string animation, int frame) {
    return NormalizeToken(form) + "|" + NormalizeToken(animation) + "|" + frame;
  }

  static bool TryResolveLocalNumericFormFallback(Dictionary<string, SpriteAddressPair> rows, SpriteLookupKey key, out SpriteAddressPair pair) {
    pair = default;
    if (rows == null || rows.Count == 0) return false;
    if (key.frame != 0) return false;

    var normalizedForm = NormalizeToken(key.form);
    if (string.IsNullOrWhiteSpace(normalizedForm)) return false;
    if (!int.TryParse(normalizedForm, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericFrame)) return false;
    if (numericFrame < 0) return false;

    var aliasKey = BuildLocalRowKey("", key.animation, numericFrame);
    return rows.TryGetValue(aliasKey, out pair);
  }

  static string UnescapeLocal(string value) {
    if (string.IsNullOrEmpty(value)) return "";
    return value
      .Replace("\\t", "\t")
      .Replace("\\n", "\n")
      .Replace("\\r", "\r")
      .Replace("\\\\", "\\");
  }

  static string NormalizeEditorPath(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Replace('\\', '/').Trim();
  }
#endif

  static void EnsureReflectionInitialized() {
    if (reflectionInitialized) return;
    reflectionInitialized = true;

    runtimeResolverType = Type.GetType("SpriteRuntimeResolver");
    if (runtimeResolverType == null) {
      var assemblies = AppDomain.CurrentDomain.GetAssemblies();
      for (var i = 0; i < assemblies.Length; i++) {
        runtimeResolverType = assemblies[i].GetType("SpriteRuntimeResolver");
        if (runtimeResolverType != null) break;
      }
    }

    if (runtimeResolverType == null) return;

    tryResolveMethod = runtimeResolverType.GetMethod("TryResolve", BindingFlags.Public | BindingFlags.Static);
    normalizeNamePartMethod = runtimeResolverType.GetMethod("NormalizeNamePart", BindingFlags.Public | BindingFlags.Static);
#if UNITY_EDITOR
    tryResolveEditorMethod = runtimeResolverType.GetMethod("TryResolveEditor", BindingFlags.Public | BindingFlags.Static);
    tryLoadEditorSpriteMethod = runtimeResolverType.GetMethod("TryLoadEditorSprite", BindingFlags.Public | BindingFlags.Static);
#endif
  }

  static string NormalizeToken(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }
}
