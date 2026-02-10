#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class SpriteIndexBuilder {
  static readonly Regex spriteRefRegex = new(@"^\s*m_Sprite(?:Override)?: \{fileID:\s*([^,]+), guid:\s*([0-9a-fA-F]{32}),", RegexOptions.Compiled);
  static readonly Regex guidRegex = new(@"guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);
  static readonly Regex labelFrameRegex = new(@"^(.*)_(\d+)$", RegexOptions.Compiled);

  static class BuilderConfig {
    public const string SourceRootFolder = "Assets/Sprites/SpriteLibraries";
    public const string RuntimeIndexFolder = "Assets/Sprites/SpriteLibraries/RuntimeIndex";
    public const string ManifestAssetPath = "Assets/Sprites/SpriteLibraries/SpriteIndexManifest.bytes";
    public const string IncludeAssetPath = "Assets/Sprites/SpriteLibraries/SpriteStreamingInclude.asset";
    public const string SettingsAssetPath = "Assets/Resources/SpriteStreamingSettings.asset";
    public const string TextureAddressablesGroupName = "SpriteTextures";
    public const string IndexAddressablesGroupName = "SpriteRuntimeIndex";
    public const string DefaultManifestAddress = "SpriteRuntimeIndex/Manifest";
    public const string SpriteWithNormalsScriptPath = "Assets/Scripts/Util/Game/SpriteWithNormals.cs";
  }

  readonly struct SpriteRef {
    public readonly string guid;
    public readonly long fileId;

    public SpriteRef(string guid, long fileId) {
      this.guid = guid;
      this.fileId = fileId;
    }
  }

  readonly struct ShardRow {
    public readonly string form;
    public readonly string animation;
    public readonly int frame;
    public readonly string colorAddress;
    public readonly string normalAddress;

    public ShardRow(string form, string animation, int frame, string colorAddress, string normalAddress) {
      this.form = form;
      this.animation = animation;
      this.frame = frame;
      this.colorAddress = colorAddress;
      this.normalAddress = normalAddress;
    }
  }

  readonly struct ManifestRow {
    public readonly string namepart;
    public readonly string address;
    public readonly string assetPath;
    public readonly int rowCount;
    public readonly string contentHash;

    public ManifestRow(string namepart, string address, string assetPath, int rowCount, string contentHash) {
      this.namepart = namepart;
      this.address = address;
      this.assetPath = assetPath;
      this.rowCount = rowCount;
      this.contentHash = contentHash;
    }
  }

  sealed class BuildState {
    public readonly AddressableAssetSettings addressables;
    public readonly AddressableAssetGroup textureGroup;
    public readonly AddressableAssetGroup indexGroup;
    public readonly List<string> errors = new();
    public readonly Dictionary<string, Dictionary<long, string>> addressCacheByGuid = new(StringComparer.OrdinalIgnoreCase);

    public BuildState(AddressableAssetSettings addressables, AddressableAssetGroup textureGroup, AddressableAssetGroup indexGroup) {
      this.addressables = addressables;
      this.textureGroup = textureGroup;
      this.indexGroup = indexGroup;
    }
  }

  readonly struct StreamingSettingsBinding {
    public readonly ScriptableObject asset;
    public readonly string manifestAddress;

    public StreamingSettingsBinding(ScriptableObject asset, string manifestAddress) {
      this.asset = asset;
      this.manifestAddress = manifestAddress;
    }
  }

  [MenuItem("Tools/Sprite Streaming/Rebuild Runtime Index")]
  public static void RebuildRuntimeIndexMenu() {
    RebuildRuntimeIndex(logResult: true, failOnError: false);
  }

  [MenuItem("Tools/Sprite Libraries/Rebuild Address Catalog")]
  public static void LegacyMenuAlias() {
    RebuildRuntimeIndex(logResult: true, failOnError: false);
  }

  public static bool RebuildRuntimeIndex(bool logResult, bool failOnError) {
    var addressableSettings = AddressableAssetSettingsDefaultObject.GetSettings(true);
    if (addressableSettings == null) {
      Debug.LogError("[SpriteIndexBuilder] Addressables settings were not found.");
      if (failOnError) throw new BuildFailedException("Addressables settings were not found.");
      return false;
    }

    EnsureFolderExists(Path.GetDirectoryName(BuilderConfig.SettingsAssetPath));
    EnsureFolderExists(BuilderConfig.RuntimeIndexFolder);

    var streamingSettings = EnsureStreamingSettingsAsset();
    var includeAsset = EnsureIncludeAsset();
    var manifestAssetPath = EnsureManifestAssetPath();

    var textureGroup = EnsureAddressableGroup(addressableSettings, BuilderConfig.TextureAddressablesGroupName);
    var indexGroup = EnsureAddressableGroup(addressableSettings, BuilderConfig.IndexAddressablesGroupName);
    var state = new BuildState(addressableSettings, textureGroup, indexGroup);

    RemoveDeprecatedPreloadedCatalog(state.errors);

    var librariesByKey = DiscoverLibraryPaths();
    ReportDuplicateShortNameAmbiguities(librariesByKey);
    var guidToNamepart = DiscoverGuidToNamepart(librariesByKey);
    var requestedNameparts = CollectRequestedNameparts(guidToNamepart, includeAsset);

    var shardAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var manifestEntries = new List<ManifestRow>();

    var orderedNameparts = requestedNameparts.ToList();
    orderedNameparts.Sort(StringComparer.Ordinal);

    for (var i = 0; i < orderedNameparts.Count; i++) {
      var requestedNamepart = orderedNameparts[i];
      var namepart = ResolveCanonicalNamepart(requestedNamepart, librariesByKey, state.errors);
      if (string.IsNullOrWhiteSpace(namepart)) {
        state.errors.Add("Missing color library for requested namepart '" + requestedNamepart + "'.");
        continue;
      }

      if (!librariesByKey.TryGetValue(namepart, out var colorLibraryPath)) {
        state.errors.Add("Missing color library for namepart '" + namepart + "' (requested '" + requestedNamepart + "').");
        continue;
      }

      var normalNamepart = namepart + "N";
      if (!librariesByKey.TryGetValue(normalNamepart, out var normalLibraryPath)) {
        state.errors.Add("Missing normal library '" + normalNamepart + "' for namepart '" + namepart + "' (requested '" + requestedNamepart + "').");
        continue;
      }

      var colorRows = ParseLibraryRows(colorLibraryPath, state.errors);
      var normalRows = ParseLibraryRows(normalLibraryPath, state.errors);
      if (colorRows.Count == 0) {
        state.errors.Add("Color library '" + colorLibraryPath + "' produced zero rows.");
        continue;
      }

      var shardRows = new List<ShardRow>(colorRows.Count);
      foreach (var pair in colorRows) {
        if (!normalRows.TryGetValue(pair.Key, out var normalRef)) {
          state.errors.Add("Missing normal row for '" + namepart + "' entry '" + pair.Key + "'.");
          continue;
        }

        var separator = pair.Key.IndexOf('\u001f');
        if (separator <= 0 || separator >= pair.Key.Length - 1) {
          state.errors.Add("Invalid row key '" + pair.Key + "' in '" + colorLibraryPath + "'.");
          continue;
        }

        var animation = pair.Key.Substring(0, separator);
        var label = pair.Key.Substring(separator + 1);
        ParseLabel(label, out var form, out var frame);

        var colorAddress = ResolveSpriteAddress(state, pair.Value, namepart + "/" + animation + ":" + label + " (color)");
        var normalAddress = ResolveSpriteAddress(state, normalRef, normalNamepart + "/" + animation + ":" + label + " (normal)");
        if (string.IsNullOrWhiteSpace(colorAddress) || string.IsNullOrWhiteSpace(normalAddress)) continue;

        shardRows.Add(new ShardRow(form, animation, frame, colorAddress, normalAddress));
      }

      if (shardRows.Count == 0) {
        state.errors.Add("Namepart '" + namepart + "' has zero valid color/normal pairs (requested '" + requestedNamepart + "').");
        continue;
      }

      shardRows.Sort((left, right) => {
        var byAnimation = string.Compare(left.animation, right.animation, StringComparison.Ordinal);
        if (byAnimation != 0) return byAnimation;
        var byForm = string.Compare(left.form, right.form, StringComparison.Ordinal);
        if (byForm != 0) return byForm;
        return left.frame.CompareTo(right.frame);
      });

      var shardBody = BuildShardBody(shardRows);
      var shardPath = BuildShardAssetPath(namepart);
      WriteIfChanged(shardPath, shardBody);
      shardAssetPaths.Add(NormalizePath(shardPath));

      var shardAddress = "SpriteRuntimeIndex/Shard/" + namepart;
      EnsureAddressableEntry(addressableSettings, indexGroup, shardPath, shardAddress);

      manifestEntries.Add(new ManifestRow(
        namepart,
        shardAddress,
        shardPath,
        shardRows.Count,
        ComputeHash(shardBody)
      ));
    }

    CleanupStaleShardAssets(shardAssetPaths);

    manifestEntries.Sort((left, right) => string.Compare(left.namepart, right.namepart, StringComparison.Ordinal));
    WriteManifestTextAsset(manifestAssetPath, manifestEntries);

    var manifestAddress = string.IsNullOrWhiteSpace(streamingSettings.manifestAddress)
      ? BuilderConfig.DefaultManifestAddress
      : streamingSettings.manifestAddress.Trim();
    EnsureAddressableEntry(addressableSettings, indexGroup, BuilderConfig.ManifestAssetPath, manifestAddress);

    if (streamingSettings.asset != null) {
      EditorUtility.SetDirty(streamingSettings.asset);
    }
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    if (state.errors.Count > 0) {
      var limitedErrors = state.errors.Take(50).ToList();
      for (var i = 0; i < limitedErrors.Count; i++) {
        Debug.LogError("[SpriteIndexBuilder] " + limitedErrors[i]);
      }
      if (state.errors.Count > limitedErrors.Count) {
        Debug.LogError("[SpriteIndexBuilder] Additional errors omitted: " + (state.errors.Count - limitedErrors.Count));
      }

      if (failOnError) {
        throw new BuildFailedException("Sprite runtime index generation failed with " + state.errors.Count + " errors.");
      }
      return false;
    }

    if (logResult) {
      Debug.Log("[SpriteIndexBuilder] Rebuilt runtime index. nameparts=" + manifestEntries.Count + " shards=" + shardAssetPaths.Count);
    }

    return true;
  }

  static StreamingSettingsBinding EnsureStreamingSettingsAsset() {
    var settingsType = FindTypeByName("SpriteStreamingSettings");
    if (settingsType == null || !typeof(ScriptableObject).IsAssignableFrom(settingsType)) {
      return new StreamingSettingsBinding(null, BuilderConfig.DefaultManifestAddress);
    }

    var asset = AssetDatabase.LoadAssetAtPath(BuilderConfig.SettingsAssetPath, settingsType) as ScriptableObject;
    if (asset == null) {
      asset = ScriptableObject.CreateInstance(settingsType);
      if (asset == null) {
        return new StreamingSettingsBinding(null, BuilderConfig.DefaultManifestAddress);
      }
      AssetDatabase.CreateAsset(asset, BuilderConfig.SettingsAssetPath);
      AssetDatabase.SaveAssets();
    }

    var manifestAddress = ReadStringMember(asset, "manifestAddress", BuilderConfig.DefaultManifestAddress);
    return new StreamingSettingsBinding(asset, manifestAddress);
  }

  static ScriptableObject EnsureIncludeAsset() {
    var includeType = FindTypeByName("SpriteStreamingInclude");
    if (includeType == null || !typeof(ScriptableObject).IsAssignableFrom(includeType)) return null;

    var asset = AssetDatabase.LoadAssetAtPath(BuilderConfig.IncludeAssetPath, includeType) as ScriptableObject;
    if (asset != null) return asset;

    EnsureFolderExists(Path.GetDirectoryName(BuilderConfig.IncludeAssetPath));
    asset = ScriptableObject.CreateInstance(includeType);
    if (asset == null) return null;
    AssetDatabase.CreateAsset(asset, BuilderConfig.IncludeAssetPath);
    AssetDatabase.SaveAssets();
    return asset;
  }

  static string EnsureManifestAssetPath() {
    EnsureFolderExists(Path.GetDirectoryName(BuilderConfig.ManifestAssetPath));
    return BuilderConfig.ManifestAssetPath;
  }

  static string ResolveCanonicalNamepart(string requestedNamepart, Dictionary<string, string> librariesByKey, List<string> errors) {
    var normalizedRequested = SpriteAddressResolver.NormalizeNamePart(requestedNamepart);
    if (string.IsNullOrWhiteSpace(normalizedRequested)) return "";

    if (librariesByKey.ContainsKey(normalizedRequested)) {
      return normalizedRequested;
    }

    var suffix = "/" + normalizedRequested;
    var matches = new List<string>();

    foreach (var key in librariesByKey.Keys) {
      if (key.EndsWith("N", StringComparison.OrdinalIgnoreCase)) continue;
      if (!key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
      matches.Add(key);
    }

    if (matches.Count == 1) return matches[0];

    if (matches.Count > 1) {
      matches.Sort(StringComparer.OrdinalIgnoreCase);
      var ambiguityError = BuildShortKeyAmbiguityError(normalizedRequested, matches);
      if (!ContainsIgnoreCase(errors, ambiguityError)) {
        errors.Add(ambiguityError);
      }
      return "";
    }

    return "";
  }

  static string BuildShortKeyAmbiguityError(string shortKey, List<string> matches) {
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
    return string.Join(", ", values.Take(values.Count - 1)) + ", and " + values[values.Count - 1];
  }

  static bool ContainsIgnoreCase(List<string> values, string candidate) {
    if (values == null || string.IsNullOrWhiteSpace(candidate)) return false;
    for (var i = 0; i < values.Count; i++) {
      if (string.Equals(values[i], candidate, StringComparison.OrdinalIgnoreCase)) return true;
    }
    return false;
  }

  static void ReportDuplicateShortNameAmbiguities(Dictionary<string, string> librariesByKey) {
    if (librariesByKey == null || librariesByKey.Count == 0) return;

    var shortNameCandidates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    var reported = new List<string>();
    foreach (var key in librariesByKey.Keys) {
      if (string.IsNullOrWhiteSpace(key)) continue;
      if (key.EndsWith("N", StringComparison.OrdinalIgnoreCase)) continue;

      var slash = key.LastIndexOf('/');
      if (slash < 0 || slash >= key.Length - 1) continue;

      var shortName = SpriteAddressResolver.NormalizeNamePart(key.Substring(slash + 1));
      if (string.IsNullOrWhiteSpace(shortName)) continue;

      if (!shortNameCandidates.TryGetValue(shortName, out var matches)) {
        matches = new List<string>();
        shortNameCandidates[shortName] = matches;
      }

      if (!ContainsIgnoreCase(matches, key)) {
        matches.Add(key);
      }
    }

    foreach (var pair in shortNameCandidates) {
      var shortName = pair.Key;
      var matches = pair.Value;
      if (matches == null || matches.Count <= 1) continue;

      matches.Sort(StringComparer.OrdinalIgnoreCase);
      var ambiguityError = BuildShortKeyAmbiguityError(shortName, matches);
      if (!ContainsIgnoreCase(reported, ambiguityError)) {
        reported.Add(ambiguityError);
        Debug.LogError("[SpriteIndexBuilder] " + ambiguityError);
      }
    }
  }

  static Dictionary<string, string> DiscoverLibraryPaths() {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var root = NormalizePath(BuilderConfig.SourceRootFolder);
    if (!Directory.Exists(root)) return result;

    var files = Directory.GetFiles(root, "*.spriteLib", SearchOption.AllDirectories);
    Array.Sort(files, StringComparer.Ordinal);

    for (var i = 0; i < files.Length; i++) {
      var path = NormalizePath(files[i]);
      var relative = path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
        ? path.Substring(root.Length + 1)
        : path;
      var key = RemoveExtension(relative);
      result[key] = path;
    }

    return result;
  }

  static Dictionary<string, string> DiscoverGuidToNamepart(Dictionary<string, string> librariesByKey) {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in librariesByKey) {
      if (pair.Key.EndsWith("N", StringComparison.OrdinalIgnoreCase)) continue;
      var guid = AssetDatabase.AssetPathToGUID(pair.Value);
      if (string.IsNullOrWhiteSpace(guid)) continue;
      result[guid] = pair.Key;
    }
    return result;
  }

  static HashSet<string> CollectRequestedNameparts(Dictionary<string, string> guidToNamepart, ScriptableObject includeAsset) {
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var assetsRoot = NormalizePath("Assets");

    CollectNamepartsFromFiles(assetsRoot, "*.unity", guidToNamepart, result);
    CollectNamepartsFromFiles(assetsRoot, "*.prefab", guidToNamepart, result);

    if (includeAsset != null) {
      var so = new SerializedObject(includeAsset);
      var includeList = so.FindProperty("nameparts");
      if (includeList != null && includeList.isArray) {
        for (var i = 0; i < includeList.arraySize; i++) {
          var entry = includeList.GetArrayElementAtIndex(i);
          if (entry == null || entry.propertyType != SerializedPropertyType.String) continue;
          var normalized = SpriteAddressResolver.NormalizeNamePart(entry.stringValue);
          if (!string.IsNullOrWhiteSpace(normalized)) {
            result.Add(normalized);
          }
        }
      }
    }

    return result;
  }

  static void CollectNamepartsFromFiles(string rootPath, string pattern, Dictionary<string, string> guidToNamepart, HashSet<string> target) {
    if (!Directory.Exists(rootPath)) return;
    var files = Directory.GetFiles(rootPath, pattern, SearchOption.AllDirectories);
    Array.Sort(files, StringComparer.Ordinal);

    for (var i = 0; i < files.Length; i++) {
      CollectNamepartsFromSerializedFile(NormalizePath(files[i]), guidToNamepart, target);
    }
  }

  static void CollectNamepartsFromSerializedFile(string path, Dictionary<string, string> guidToNamepart, HashSet<string> target) {
    if (!File.Exists(path)) return;

    var spriteWithNormalsGuid = AssetDatabase.AssetPathToGUID(BuilderConfig.SpriteWithNormalsScriptPath);
    var insideMonoBehaviour = false;
    var insideSpriteWithNormals = false;
    var pendingNamepart = "";
    var pendingColorKey = "";
    var pendingColorLibraryGuid = "";

    void BeginSpriteWithNormalsBlock() {
      insideSpriteWithNormals = true;
      pendingNamepart = "";
      pendingColorKey = "";
      pendingColorLibraryGuid = "";
    }

    void FlushPending() {
      if (!insideSpriteWithNormals) return;
      insideSpriteWithNormals = false;

      var resolved = pendingNamepart;
      if (string.IsNullOrWhiteSpace(resolved)) resolved = pendingColorKey;
      if (string.IsNullOrWhiteSpace(resolved) &&
          !string.IsNullOrWhiteSpace(pendingColorLibraryGuid) &&
          guidToNamepart.TryGetValue(pendingColorLibraryGuid, out var mappedNamepart)) {
        resolved = mappedNamepart;
      }

      var normalized = SpriteAddressResolver.NormalizeNamePart(resolved);
      if (!string.IsNullOrWhiteSpace(normalized)) {
        target.Add(normalized);
      }

      pendingNamepart = "";
      pendingColorKey = "";
      pendingColorLibraryGuid = "";
    }

    foreach (var rawLine in File.ReadLines(path)) {
      var line = rawLine ?? "";
      if (line.StartsWith("--- !u!", StringComparison.Ordinal)) {
        FlushPending();
        insideMonoBehaviour = line.StartsWith("--- !u!114", StringComparison.Ordinal);
      }

      if (!insideMonoBehaviour) continue;

      var trimmed = line.Trim();

      if (!insideSpriteWithNormals &&
          !string.IsNullOrWhiteSpace(spriteWithNormalsGuid) &&
          trimmed.StartsWith("m_Script:", StringComparison.Ordinal)) {
        var scriptGuidMatch = guidRegex.Match(trimmed);
        if (scriptGuidMatch.Success &&
            string.Equals(scriptGuidMatch.Groups[1].Value, spriteWithNormalsGuid, StringComparison.OrdinalIgnoreCase)) {
          BeginSpriteWithNormalsBlock();
          continue;
        }
      }

      if (!insideSpriteWithNormals &&
          trimmed.StartsWith("m_EditorClassIdentifier:", StringComparison.Ordinal) &&
          trimmed.Contains("SpriteWithNormals", StringComparison.Ordinal)) {
        BeginSpriteWithNormalsBlock();
        continue;
      }

      if (!insideSpriteWithNormals) continue;

      if (TryReadScalar(trimmed, "namepart", out var namepartValue)) {
        pendingNamepart = namepartValue;
        continue;
      }

      if (TryReadScalar(trimmed, "colorKey", out var colorKeyValue)) {
        pendingColorKey = colorKeyValue;
        continue;
      }

      if (trimmed.StartsWith("colorLibrary:", StringComparison.Ordinal)) {
        var guidMatch = guidRegex.Match(trimmed);
        if (guidMatch.Success) {
          pendingColorLibraryGuid = guidMatch.Groups[1].Value;
        }
      }
    }

    FlushPending();
  }

  static Dictionary<string, SpriteRef> ParseLibraryRows(string path, List<string> errors) {
    var rows = new Dictionary<string, SpriteRef>(StringComparer.OrdinalIgnoreCase);
    if (!File.Exists(path)) {
      errors.Add("Missing sprite library file '" + path + "'.");
      return rows;
    }

    string currentCategory = null;
    bool insideOverrideEntries = false;
    string currentLabel = null;
    SpriteRef? currentSpriteRef = null;

    void FlushLabel() {
      if (string.IsNullOrWhiteSpace(currentCategory) || string.IsNullOrWhiteSpace(currentLabel)) {
        currentLabel = null;
        currentSpriteRef = null;
        return;
      }

      if (!currentSpriteRef.HasValue) {
        currentLabel = null;
        currentSpriteRef = null;
        return;
      }

      var key = currentCategory + "\u001f" + currentLabel;
      rows[key] = currentSpriteRef.Value;
      currentLabel = null;
      currentSpriteRef = null;
    }

    foreach (var rawLine in File.ReadLines(path)) {
      var line = rawLine ?? "";

      if (line.StartsWith("  - m_Name:", StringComparison.Ordinal)) {
        FlushLabel();
        currentCategory = DecodeScalar(line.Substring("  - m_Name:".Length));
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
        currentLabel = DecodeScalar(line.Substring("    - m_Name:".Length));
        currentSpriteRef = null;
        continue;
      }

      if (string.IsNullOrWhiteSpace(currentLabel)) continue;

      var spriteMatch = spriteRefRegex.Match(line);
      if (!spriteMatch.Success) continue;

      if (!long.TryParse(spriteMatch.Groups[1].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileId)) {
        continue;
      }

      var guid = spriteMatch.Groups[2].Value.Trim();
      currentSpriteRef = new SpriteRef(guid, fileId);
    }

    FlushLabel();
    return rows;
  }

  static string ResolveSpriteAddress(BuildState state, SpriteRef spriteRef, string context) {
    if (string.IsNullOrWhiteSpace(spriteRef.guid)) {
      state.errors.Add("Missing GUID while resolving " + context + ".");
      return "";
    }

    if (!state.addressCacheByGuid.TryGetValue(spriteRef.guid, out var byFileId)) {
      byFileId = BuildAddressMapForGuid(state, spriteRef.guid);
      state.addressCacheByGuid[spriteRef.guid] = byFileId;
    }

    if (byFileId.TryGetValue(spriteRef.fileId, out var address)) return address;

    var targetUnsigned = unchecked((ulong)spriteRef.fileId);
    foreach (var pair in byFileId) {
      if (unchecked((ulong)pair.Key) != targetUnsigned) continue;
      return pair.Value;
    }

    var fallbackAddress = TryResolveSpriteAddressFromContext(byFileId, context);
    if (!string.IsNullOrWhiteSpace(fallbackAddress)) return fallbackAddress;

    state.errors.Add("Could not resolve sprite fileID '" + spriteRef.fileId + "' for GUID '" + spriteRef.guid + "' (" + context + ").");
    return "";
  }

  static string TryResolveSpriteAddressFromContext(Dictionary<long, string> byFileId, string context) {
    if (byFileId == null || byFileId.Count == 0) return "";

    var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in byFileId) {
      var spriteName = ExtractSpriteNameFromAddress(pair.Value);
      if (string.IsNullOrWhiteSpace(spriteName)) continue;
      byName[spriteName] = pair.Value;
    }

    if (byName.Count == 0) return "";
    if (byName.Count == 1) return byName.Values.FirstOrDefault() ?? "";

    var label = ExtractLabelFromContext(context);
    if (string.IsNullOrWhiteSpace(label)) return "";

    if (byName.TryGetValue(label, out var exact)) return exact;

    ParseLabel(label, out var form, out var frame);
    if (frame > 0) {
      var frameText = frame.ToString(CultureInfo.InvariantCulture);

      if (byName.TryGetValue("1_" + frameText, out var oneFrame)) return oneFrame;
      if (byName.TryGetValue(frameText, out var numericFrame)) return numericFrame;
      if (!string.IsNullOrWhiteSpace(form) && byName.TryGetValue(form + "_" + frameText, out var formFrame)) return formFrame;

      var suffix = "_" + frameText;
      string singleSuffixMatch = null;
      foreach (var pair in byName) {
        if (pair.Key.Equals(frameText, StringComparison.OrdinalIgnoreCase) ||
            pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
          if (singleSuffixMatch != null) {
            singleSuffixMatch = null;
            break;
          }
          singleSuffixMatch = pair.Value;
        }
      }

      if (!string.IsNullOrWhiteSpace(singleSuffixMatch)) return singleSuffixMatch;
    }

    return "";
  }

  static string ExtractLabelFromContext(string context) {
    if (string.IsNullOrWhiteSpace(context)) return "";
    var colon = context.LastIndexOf(':');
    if (colon < 0 || colon >= context.Length - 1) return "";

    var suffixStart = context.LastIndexOf(" (", StringComparison.Ordinal);
    if (suffixStart <= colon) suffixStart = context.Length;

    var label = context.Substring(colon + 1, suffixStart - colon - 1);
    return string.IsNullOrWhiteSpace(label) ? "" : label.Trim();
  }

  static string ExtractSpriteNameFromAddress(string address) {
    if (string.IsNullOrWhiteSpace(address)) return "";
    var close = address.LastIndexOf(']');
    if (close <= 0 || close != address.Length - 1) return "";
    var open = address.LastIndexOf('[', close - 1);
    if (open < 0 || open >= close - 1) return "";
    return address.Substring(open + 1, close - open - 1);
  }

  static Dictionary<long, string> BuildAddressMapForGuid(BuildState state, string guid) {
    var map = new Dictionary<long, string>();
    var path = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));
    if (string.IsNullOrWhiteSpace(path)) {
      state.errors.Add("GUID '" + guid + "' does not map to an asset path.");
      return map;
    }

    EnsureAddressableEntry(state.addressables, state.textureGroup, path, path);
    var metaPath = path + ".meta";
    if (!File.Exists(metaPath)) {
      state.errors.Add("Meta file was not found for GUID '" + guid + "' at path '" + metaPath + "'.");
      return map;
    }

    if (!TryParseSpriteSheetInternalIdTable(metaPath, map, out var parseError)) {
      state.errors.Add("Failed to parse spriteSheet.sprites table for GUID '" + guid + "' at path '" + metaPath + "'" +
                       (string.IsNullOrWhiteSpace(parseError) ? "." : ": " + parseError));
      return map;
    }

    if (!TryParseNameFileIdTable(metaPath, map, out var nameTableError)) {
      state.errors.Add("Failed to parse nameFileIdTable for GUID '" + guid + "' at path '" + metaPath + "'" +
                       (string.IsNullOrWhiteSpace(nameTableError) ? "." : ": " + nameTableError));
      return map;
    }

    var keys = map.Keys.ToList();
    for (var i = 0; i < keys.Count; i++) {
      var localId = keys[i];
      var spriteName = map[localId];
      map[localId] = path + "[" + spriteName + "]";
    }

    if (map.Count == 0) {
      state.errors.Add("No sprite sub-assets found for GUID '" + guid + "' at path '" + path + "'.");
    }

    return map;
  }

  static bool TryParseSpriteSheetInternalIdTable(string metaPath, Dictionary<long, string> target, out string error) {
    error = "";
    if (target == null) {
      error = "Target dictionary is null.";
      return false;
    }

    var insideSpriteSheet = false;
    var spriteSheetIndent = -1;
    var insideSpritesList = false;
    var spritesListIndent = -1;
    var insideSpriteEntry = false;
    var spriteEntryIndent = -1;
    string currentSpriteName = "";

    void BeginSpriteEntry(int indent, string trimmedLine) {
      insideSpriteEntry = true;
      spriteEntryIndent = indent;
      currentSpriteName = "";

      if (!trimmedLine.StartsWith("- ", StringComparison.Ordinal)) return;
      var remainder = trimmedLine.Substring(2).TrimStart();
      if (TryReadScalar(remainder, "name", out var parsedName) && !string.IsNullOrWhiteSpace(parsedName)) {
        currentSpriteName = parsedName;
      }
    }

    foreach (var rawLine in File.ReadLines(metaPath)) {
      var line = rawLine ?? "";
      var trimmed = line.Trim();
      var indent = line.Length - line.TrimStart().Length;

      if (!insideSpriteSheet) {
        if (trimmed.StartsWith("spriteSheet:", StringComparison.Ordinal)) {
          insideSpriteSheet = true;
          spriteSheetIndent = indent;
        }
        continue;
      }

      if (!string.IsNullOrWhiteSpace(trimmed) && indent <= spriteSheetIndent) {
        insideSpriteSheet = false;
        insideSpritesList = false;
        insideSpriteEntry = false;
        currentSpriteName = "";

        if (trimmed.StartsWith("spriteSheet:", StringComparison.Ordinal)) {
          insideSpriteSheet = true;
          spriteSheetIndent = indent;
        }
        continue;
      }

      if (!insideSpritesList) {
        if (trimmed.StartsWith("sprites:", StringComparison.Ordinal)) {
          insideSpritesList = true;
          spritesListIndent = indent;
        }
        continue;
      }

      if (!string.IsNullOrWhiteSpace(trimmed) &&
          (indent < spritesListIndent || (indent == spritesListIndent && !trimmed.StartsWith("- ", StringComparison.Ordinal)))) {
        insideSpritesList = false;
        insideSpriteEntry = false;
        currentSpriteName = "";
        continue;
      }

      if (trimmed.StartsWith("- ", StringComparison.Ordinal)) {
        BeginSpriteEntry(indent, trimmed);
        continue;
      }

      if (!insideSpriteEntry) continue;

      if (!string.IsNullOrWhiteSpace(trimmed) && indent <= spriteEntryIndent) {
        insideSpriteEntry = false;
        currentSpriteName = "";
        if (trimmed.StartsWith("- ", StringComparison.Ordinal)) {
          BeginSpriteEntry(indent, trimmed);
        }
        continue;
      }

      if (TryReadScalar(trimmed, "name", out var spriteNameValue) && !string.IsNullOrWhiteSpace(spriteNameValue)) {
        currentSpriteName = spriteNameValue;
        continue;
      }

      if (string.IsNullOrWhiteSpace(currentSpriteName)) continue;
      if (!trimmed.StartsWith("internalID:", StringComparison.Ordinal)) continue;

      var idValue = trimmed.Substring("internalID:".Length).Trim();
      if (long.TryParse(idValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId)) {
        target[parsedId] = currentSpriteName;
      }
    }

    return true;
  }

  static bool TryParseNameFileIdTable(string metaPath, Dictionary<long, string> target, out string error) {
    error = "";
    if (target == null) {
      error = "Target dictionary is null.";
      return false;
    }

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

      var spriteName = DecodeScalar(trimmed.Substring(0, separatorIndex));
      if (string.IsNullOrWhiteSpace(spriteName)) continue;

      var idValue = trimmed.Substring(separatorIndex + 1).Trim();
      if (!long.TryParse(idValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileId)) continue;

      target[fileId] = spriteName;
    }

    return true;
  }

  static string BuildShardBody(List<ShardRow> rows) {
    var sb = new StringBuilder(rows.Count * 48);
    for (var i = 0; i < rows.Count; i++) {
      var row = rows[i];
      sb
        .Append(Escape(row.form)).Append('\t')
        .Append(Escape(row.animation)).Append('\t')
        .Append(row.frame.ToString(CultureInfo.InvariantCulture)).Append('\t')
        .Append(Escape(row.colorAddress)).Append('\t')
        .Append(Escape(row.normalAddress))
        .Append('\n');
    }
    return sb.ToString();
  }

  static string BuildShardAssetPath(string namepart) {
    var safe = namepart.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
    var hash = ComputeHash(namepart).Substring(0, 12);
    return NormalizePath(BuilderConfig.RuntimeIndexFolder + "/" + safe + "_" + hash + ".bytes");
  }

  static void CleanupStaleShardAssets(HashSet<string> activePaths) {
    var folder = NormalizePath(BuilderConfig.RuntimeIndexFolder);
    if (!AssetDatabase.IsValidFolder(folder)) return;

    var bytesFiles = Directory.GetFiles(folder, "*.bytes", SearchOption.TopDirectoryOnly);
    for (var i = 0; i < bytesFiles.Length; i++) {
      var assetPath = NormalizePath(bytesFiles[i]);
      if (activePaths.Contains(assetPath)) continue;
      AssetDatabase.DeleteAsset(assetPath);
    }
  }

  static void RemoveDeprecatedPreloadedCatalog(List<string> errors) {
    var preloaded = PlayerSettings.GetPreloadedAssets() ?? Array.Empty<UnityEngine.Object>();
    if (preloaded.Length == 0) return;

    var deprecated = AssetDatabase.LoadMainAssetAtPath("Assets/Sprites/SpriteLibraries/SpriteLibraryCatalog.asset");
    if (deprecated == null) return;

    var kept = new List<UnityEngine.Object>(preloaded.Length);
    var removed = false;

    for (var i = 0; i < preloaded.Length; i++) {
      var candidate = preloaded[i];
      if (candidate == null) continue;
      if (ReferenceEquals(candidate, deprecated)) {
        removed = true;
        continue;
      }
      kept.Add(candidate);
    }

    if (!removed) return;

    PlayerSettings.SetPreloadedAssets(kept.ToArray());
    var verify = PlayerSettings.GetPreloadedAssets() ?? Array.Empty<UnityEngine.Object>();
    for (var i = 0; i < verify.Length; i++) {
      if (!ReferenceEquals(verify[i], deprecated)) continue;
      errors.Add("Deprecated preloaded asset 'SpriteLibraryCatalog.asset' is still present in Player Settings.");
      return;
    }
  }

  static AddressableAssetGroup EnsureAddressableGroup(AddressableAssetSettings settings, string groupName) {
    var group = settings.FindGroup(groupName);
    if (group != null) return group;

    group = settings.CreateGroup(
      groupName,
      false,
      false,
      false,
      null,
      typeof(BundledAssetGroupSchema),
      typeof(ContentUpdateGroupSchema)
    );

    return group ?? settings.DefaultGroup;
  }

  static void EnsureAddressableEntry(AddressableAssetSettings settings, AddressableAssetGroup group, string assetPath, string address) {
    var guid = AssetDatabase.AssetPathToGUID(assetPath);
    if (string.IsNullOrWhiteSpace(guid)) return;

    var entry = settings.FindAssetEntry(guid);
    if (entry == null || entry.parentGroup != group) {
      entry = settings.CreateOrMoveEntry(guid, group, false, false);
    }
    if (entry == null) return;

    if (!string.Equals(entry.address, address, StringComparison.Ordinal)) {
      entry.SetAddress(address, false);
    }
  }

  static bool TryReadScalar(string trimmedLine, string fieldName, out string value) {
    var prefix = fieldName + ":";
    if (!trimmedLine.StartsWith(prefix, StringComparison.Ordinal)) {
      value = "";
      return false;
    }

    value = DecodeScalar(trimmedLine.Substring(prefix.Length));
    return true;
  }

  static string DecodeScalar(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    var trimmed = value.Trim();
    if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') {
      trimmed = trimmed.Substring(1, trimmed.Length - 2);
    }
    return trimmed;
  }

  static void ParseLabel(string label, out string form, out int frame) {
    form = label ?? "";
    frame = 0;
    if (string.IsNullOrWhiteSpace(label)) return;

    if (int.TryParse(label, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericOnly)) {
      frame = numericOnly;
      form = "";
      return;
    }

    var match = labelFrameRegex.Match(label);
    if (!match.Success) return;

    form = match.Groups[1].Value;
    if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out frame)) {
      frame = 0;
    }
  }

  static void WriteIfChanged(string path, string contents) {
    path = NormalizePath(path);
    EnsureFolderExists(Path.GetDirectoryName(path));

    if (File.Exists(path)) {
      var existing = File.ReadAllText(path);
      if (string.Equals(existing, contents, StringComparison.Ordinal)) return;
    }

    File.WriteAllText(path, contents, new UTF8Encoding(false));
  }

  static string Escape(string value) {
    if (string.IsNullOrEmpty(value)) return "";
    return value
      .Replace("\\", "\\\\")
      .Replace("\t", "\\t")
      .Replace("\r", "\\r")
      .Replace("\n", "\\n");
  }

  static string ComputeManifestHash(List<ManifestRow> entries) {
    var sb = new StringBuilder(entries.Count * 48);
    for (var i = 0; i < entries.Count; i++) {
      var entry = entries[i];
      sb.Append(entry.namepart).Append('|').Append(entry.rowCount).Append('|').Append(entry.contentHash).Append('\n');
    }
    return ComputeHash(sb.ToString());
  }

  static string ComputeHash(string value) {
    var bytes = Encoding.UTF8.GetBytes(value ?? "");
    byte[] hashBytes;
    using (var sha = SHA256.Create()) {
      hashBytes = sha.ComputeHash(bytes);
    }
    var sb = new StringBuilder(hashBytes.Length * 2);
    for (var i = 0; i < hashBytes.Length; i++) {
      sb.Append(hashBytes[i].ToString("x2", CultureInfo.InvariantCulture));
    }
    return sb.ToString();
  }

  static string RemoveExtension(string value) {
    var normalized = NormalizePath(value);
    return normalized.EndsWith(".spriteLib", StringComparison.OrdinalIgnoreCase)
      ? normalized.Substring(0, normalized.Length - ".spriteLib".Length)
      : normalized;
  }

  static string NormalizePath(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Replace('\\', '/').Trim();
  }

  static void EnsureFolderExists(string folderPath) {
    if (string.IsNullOrWhiteSpace(folderPath)) return;
    var normalized = NormalizePath(folderPath);
    if (AssetDatabase.IsValidFolder(normalized)) return;

    var parts = normalized.Split('/');
    if (parts.Length == 0) return;

    var current = parts[0];
    for (var i = 1; i < parts.Length; i++) {
      var next = current + "/" + parts[i];
      if (!AssetDatabase.IsValidFolder(next)) {
        AssetDatabase.CreateFolder(current, parts[i]);
      }
      current = next;
    }
  }

  static Type FindTypeByName(string typeName) {
    if (string.IsNullOrWhiteSpace(typeName)) return null;

    var direct = Type.GetType(typeName);
    if (direct != null) return direct;

    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
    for (var i = 0; i < assemblies.Length; i++) {
      var resolved = assemblies[i].GetType(typeName);
      if (resolved != null) return resolved;
    }

    return null;
  }

  static string ReadStringMember(object target, string memberName, string fallback) {
    if (target == null || string.IsNullOrWhiteSpace(memberName)) return fallback;

    var type = target.GetType();
    var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (field != null && field.FieldType == typeof(string)) {
      var value = field.GetValue(target) as string;
      if (!string.IsNullOrWhiteSpace(value)) return value;
    }

    var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (property != null && property.PropertyType == typeof(string) && property.CanRead) {
      var value = property.GetValue(target, null) as string;
      if (!string.IsNullOrWhiteSpace(value)) return value;
    }

    return fallback;
  }

  static void WriteManifestTextAsset(string manifestAssetPath, List<ManifestRow> rows) {
    var sb = new StringBuilder(rows.Count * 72);
    sb.Append("#hash\t").Append(ComputeManifestHash(rows)).Append('\n');

    for (var i = 0; i < rows.Count; i++) {
      var row = rows[i];
      sb
        .Append(Escape(row.namepart)).Append('\t')
        .Append(Escape(row.address)).Append('\t')
        .Append(Escape(row.assetPath)).Append('\t')
        .Append(row.rowCount.ToString(CultureInfo.InvariantCulture)).Append('\t')
        .Append(Escape(row.contentHash))
        .Append('\n');
    }

    WriteIfChanged(manifestAssetPath, sb.ToString());
  }
}

public class SpriteIndexBuildProcessor : IPreprocessBuildWithReport {
  public int callbackOrder => 0;

  public void OnPreprocessBuild(BuildReport report) {
    SpriteIndexBuilder.RebuildRuntimeIndex(logResult: true, failOnError: true);
  }
}

[InitializeOnLoad]
static class SpriteStreamingStartupGuard {
  static SpriteStreamingStartupGuard() {
    EditorApplication.delayCall += WarnIfDeprecatedPreloadExists;
  }

  static void WarnIfDeprecatedPreloadExists() {
    var deprecated = AssetDatabase.LoadMainAssetAtPath("Assets/Sprites/SpriteLibraries/SpriteLibraryCatalog.asset");
    if (deprecated == null) return;

    var preloaded = PlayerSettings.GetPreloadedAssets() ?? Array.Empty<UnityEngine.Object>();
    for (var i = 0; i < preloaded.Length; i++) {
      if (!ReferenceEquals(preloaded[i], deprecated)) continue;
      Debug.LogError("[SpriteIndexBuilder] Deprecated preloaded asset 'SpriteLibraryCatalog.asset' is still configured. Remove it to avoid memory spikes.");
      return;
    }
  }
}
#endif
