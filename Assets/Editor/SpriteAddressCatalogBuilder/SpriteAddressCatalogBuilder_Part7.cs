#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEngine;

public static partial class SpriteIndexBuilder {

  // ─── Active texture index ────────────────────────────────────────────────

  static void BuildActiveTextureGuidIndex(BuildState state) {
    if (state == null || state.activeTextureGuidIndexBuilt) return;
    state.activeTextureGuidIndexBuilt = true;

    var textureRoots = ContentPackPipeline.GetTextureSearchRoots();
    var scannedRootCount = 0;
    for (var rootIndex = 0; rootIndex < textureRoots.Count; rootIndex++) {
      var root = NormalizePath(textureRoots[rootIndex]);
      var physicalRoot = ContentPackPipeline.GetPhysicalPath(root);
      if (string.IsNullOrWhiteSpace(physicalRoot) || !Directory.Exists(physicalRoot)) continue;
      scannedRootCount++;

      var metaFiles = Directory.GetFiles(physicalRoot, "*.meta", SearchOption.AllDirectories);
      Array.Sort(metaFiles, StringComparer.Ordinal);
      for (var i = 0; i < metaFiles.Length; i++) {
        var physicalMetaPath = NormalizePath(metaFiles[i]);
        var projectMetaPath = ContentPackPipeline.ToProjectAssetPath(physicalMetaPath);
        AddActiveTextureGuidIndexEntry(state, projectMetaPath, physicalMetaPath);
      }
    }

    if (state.logResult) {
      Debug.Log(
        "[SpriteIndexBuilder] Active texture GUID index built." +
        " roots=" + scannedRootCount +
        " texture_guids=" + state.activeTextureAssetPathByGuid.Count +
        " sprite_guid_maps=" + state.activeSpriteAddressByFileIdByGuid.Count +
        " sprite_file_ids=" + state.activeSpriteAddressesByFileId.Count
      );
    }
  }

  static void AddActiveTextureGuidIndexEntry(BuildState state, string projectMetaPath, string physicalMetaPath) {
    if (state == null || string.IsNullOrWhiteSpace(projectMetaPath)) return;
    if (!projectMetaPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return;

    var assetPath = projectMetaPath.Substring(0, projectMetaPath.Length - ".meta".Length);
    if (!IsActiveRuntimeTextureAssetPath(assetPath)) return;

    var guid = ReadGuidFromMeta(physicalMetaPath);
    if (string.IsNullOrWhiteSpace(guid)) return;
    if (!state.activeTextureAssetPathByGuid.ContainsKey(guid)) {
      state.activeTextureAssetPathByGuid[guid] = assetPath;
    }

    var spriteNamesByFileId = ReadSpriteNamesByFileIdFromMeta(physicalMetaPath);
    Dictionary<long, string> addressByFileId = null;
    if (spriteNamesByFileId.Count > 0 && !state.activeSpriteAddressByFileIdByGuid.TryGetValue(guid, out addressByFileId)) {
      addressByFileId = new Dictionary<long, string>();
      state.activeSpriteAddressByFileIdByGuid[guid] = addressByFileId;
    }

    foreach (var pair in spriteNamesByFileId) {
      var address = SpriteSliceAddressUtility.BuildSliceAddress(assetPath, pair.Value);
      if (!string.IsNullOrWhiteSpace(address) && addressByFileId != null) {
        addressByFileId[pair.Key] = address;
      }
      AddActiveSpriteAddress(state, pair.Key, address);
    }
  }

  static string ReadGuidFromMeta(string metaPath) {
    if (string.IsNullOrWhiteSpace(metaPath) || !File.Exists(metaPath)) return "";

    try {
      var text = File.ReadAllText(metaPath);
      var idx = text.IndexOf("guid:", StringComparison.Ordinal);
      if (idx < 0) return "";

      var start = idx + "guid:".Length;
      var end = text.IndexOf('\n', start);
      if (end < 0) end = text.Length;

      return text.Substring(start, end - start).Trim();
    }
    catch {
      return "";
    }
  }

  // ─── Addressable group management ────────────────────────────────────────

  static Dictionary<long, string> ReadSpriteNamesByFileIdFromMeta(string metaPath) {
    var result = new Dictionary<long, string>();
    if (string.IsNullOrWhiteSpace(metaPath) || !File.Exists(metaPath)) return result;

    try {
      var insideNameFileIdTable = false;
      foreach (var rawLine in File.ReadLines(metaPath)) {
        var line = rawLine ?? "";
        var trimmed = line.Trim();
        if (!insideNameFileIdTable) {
          if (string.Equals(trimmed, "nameFileIdTable:", StringComparison.Ordinal)) {
            insideNameFileIdTable = true;
          }
          continue;
        }

        if (trimmed.Length == 0) continue;
        if (!line.StartsWith("      ", StringComparison.Ordinal)) break;

        var separator = trimmed.LastIndexOf(':');
        if (separator <= 0 || separator >= trimmed.Length - 1) continue;

        var spriteName = DecodeScalar(trimmed.Substring(0, separator));
        var fileIdText = trimmed.Substring(separator + 1).Trim();
        if (string.IsNullOrWhiteSpace(spriteName)) continue;
        if (!long.TryParse(fileIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileId)) continue;

        result[fileId] = spriteName;
      }
    }
    catch {
      return result;
    }

    return result;
  }

  static void AddActiveSpriteAddress(BuildState state, long fileId, string address) {
    if (state == null || string.IsNullOrWhiteSpace(address)) return;
    if (!state.activeSpriteAddressesByFileId.TryGetValue(fileId, out var addresses)) {
      addresses = new List<string>();
      state.activeSpriteAddressesByFileId[fileId] = addresses;
    }

    for (var i = 0; i < addresses.Count; i++) {
      if (string.Equals(addresses[i], address, StringComparison.OrdinalIgnoreCase)) return;
    }
    addresses.Add(address);
  }

  static AddressableAssetGroup EnsureAddressableGroup(
    AddressableAssetSettings settings,
    string groupName,
    string contextLabel,
    bool logResult,
    out int schemaRepairs,
    bool includeInBuild = true
  ) {
    schemaRepairs = 0;
    var group = settings.FindGroup(groupName);
    if (group == null) {
      group = settings.CreateGroup(groupName, false, false, false, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
      if (logResult) Debug.Log("[SpriteIndexBuilder] [" + contextLabel + "] Created Addressables group '" + groupName + "'.");
      schemaRepairs++;
    }

    var schema = group.GetSchema<BundledAssetGroupSchema>();
    if (schema == null) {
      schema = group.AddSchema<BundledAssetGroupSchema>();
      schemaRepairs++;
    }

    if (schema.IncludeInBuild != includeInBuild) {
      schema.IncludeInBuild = includeInBuild;
      EditorUtility.SetDirty(schema);
      schemaRepairs++;
    }

    return group;
  }

  static bool ValidateAddressableGroupsPreflight(BuildState state, string contextLabel, bool failOnError) {
    if (state.indexGroup == null) {
      var msg = "Index Addressables group could not be created or found.";
      Debug.LogError("[SpriteIndexBuilder] [" + contextLabel + "] " + msg);
      if (failOnError) throw new BuildFailedException(msg);
      return false;
    }
    return true;
  }

  static void EnsureAddressableEntry(
    AddressableAssetSettings settings,
    AddressableAssetGroup group,
    string assetPath,
    string address,
    string bundleLabel = "",
    bool isAtlasMetadata = false
  ) {
    var normalizedPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedPath) || group == null) return;
    var normalizedAddress = NormalizePath(address);
    if (string.IsNullOrWhiteSpace(normalizedAddress)) return;
    var guid = AssetDatabase.AssetPathToGUID(normalizedPath);
    if (string.IsNullOrWhiteSpace(guid)) return;

    var removedStaleEntryCount = RemoveStaleEntriesWithAddress(settings, guid, normalizedAddress);
    var existing = settings.FindAssetEntry(guid);
    if (existing == null) {
      existing = settings.CreateOrMoveEntry(guid, group, false, false);
    }
    else if (existing.parentGroup != group) {
      settings.MoveEntry(existing, group, false, false);
    }

    if (existing != null && !string.Equals(existing.address, normalizedAddress, StringComparison.Ordinal)) {
      existing.SetAddress(normalizedAddress);
      EditorUtility.SetDirty(settings);
    }

    if (removedStaleEntryCount > 0) {
      Debug.LogWarning(
        "[SpriteIndexBuilder] [" + group.Name + "] Removed stale Addressables entries sharing address '" + normalizedAddress + "'" +
        " removed=" + removedStaleEntryCount
      );
      EditorUtility.SetDirty(settings);
    }

    ApplySyntheticTextureBundleLabel(settings, existing, bundleLabel);
    ApplyAtlasMetadataLabel(settings, existing, isAtlasMetadata);
  }

  static int RemoveStaleEntriesWithAddress(
    AddressableAssetSettings settings,
    string activeGuid,
    string address
  ) {
    if (settings == null || string.IsNullOrWhiteSpace(activeGuid) || string.IsNullOrWhiteSpace(address)) {
      return 0;
    }

    var normalizedAddress = NormalizePath(address);
    var staleGuids = new List<string>();
    var groups = settings.groups;
    if (groups == null) return 0;

    for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++) {
      var candidateGroup = groups[groupIndex];
      if (candidateGroup == null || candidateGroup.entries == null) continue;

      foreach (var entry in candidateGroup.entries) {
        if (entry == null) continue;
        if (string.Equals(entry.guid, activeGuid, StringComparison.OrdinalIgnoreCase)) continue;
        if (!string.Equals(NormalizePath(entry.address), normalizedAddress, StringComparison.Ordinal)) continue;
        staleGuids.Add(entry.guid);
      }
    }

    for (var i = 0; i < staleGuids.Count; i++) {
      settings.RemoveAssetEntry(staleGuids[i], false);
    }

    return staleGuids.Count;
  }

  // ─── Active texture entries ───────────────────────────────────────────────

  static void EnsureActiveStageTextureEntries(BuildState state) {
    if (state == null) return;
    var settings = state.addressables;
    if (settings == null) return;

    AddActiveAtlasMetadataEntries(state);

    foreach (var assetPath in state.activeTextureAssetPaths) {
      var normalizedPath = NormalizePath(assetPath);
      var guid = AssetDatabase.AssetPathToGUID(normalizedPath);
      if (string.IsNullOrWhiteSpace(guid)) continue;
      var address = normalizedPath;
      var bundleLabel = BuildSyntheticTextureBundleLabel(normalizedPath);

      var packId = ResolveContentPackRootName(normalizedPath);
      var groupName = string.IsNullOrEmpty(packId)
        ? BuilderConfig.TextureAddressablesGroupName
        : BuilderConfig.ManagedTextureGroupPrefix + packId;
      var includeInBuild =
        string.IsNullOrEmpty(packId) ||
        string.Equals(packId, ContentPackPipeline.CorePackId, StringComparison.OrdinalIgnoreCase);

      if (!state.textureGroupsByName.TryGetValue(groupName, out var targetGroup)) {
        targetGroup = EnsureAddressableGroup(
          settings,
          groupName,
          state.contextLabel,
          state.logResult,
          out var repairs,
          includeInBuild
        );
        state.schemaRepairs += repairs;
        state.textureGroupsByName[groupName] = targetGroup;
      }

      var isAtlasMetadata = state.activeAtlasMetadataAssetPaths.Contains(normalizedPath);
      EnsureAddressableEntry(
        settings,
        targetGroup,
        normalizedPath,
        address,
        bundleLabel,
        isAtlasMetadata
      );
      if (!string.IsNullOrWhiteSpace(bundleLabel)) {
        state.syntheticTextureLabelAssignments++;
        if (!state.syntheticTextureLabelCounts.ContainsKey(bundleLabel)) {
          state.syntheticTextureLabelCounts[bundleLabel] = 0;
        }
        state.syntheticTextureLabelCounts[bundleLabel]++;
      }
      state.activeTextureGroupNames.Add(targetGroup.Name);
    }
  }

  static void AddActiveAtlasMetadataEntries(BuildState state) {
    var metadataPaths = new List<string>();

    foreach (var textureAssetPath in state.activeTextureAssetPaths) {
      var metadataAssetPath = NormalizePath(Path.ChangeExtension(textureAssetPath, ".json"));
      if (string.IsNullOrWhiteSpace(metadataAssetPath)) continue;

      var metadataGuid = AssetDatabase.AssetPathToGUID(metadataAssetPath);
      if (string.IsNullOrWhiteSpace(metadataGuid)) continue;

      metadataPaths.Add(metadataAssetPath);
      state.activeAtlasMetadataAssetPaths.Add(metadataAssetPath);
    }

    for (var i = 0; i < metadataPaths.Count; i++) {
      state.activeTextureAssetPaths.Add(metadataPaths[i]);
    }
  }

  static void ApplySyntheticTextureBundleLabel(
    AddressableAssetSettings settings,
    AddressableAssetEntry entry,
    string bundleLabel
  ) {
    if (settings == null || entry == null || string.IsNullOrWhiteSpace(bundleLabel)) return;

    settings.AddLabel(bundleLabel, false);

    var labelsToRemove = new List<string>();
    if (entry.labels != null) {
      foreach (var label in entry.labels) {
        if (string.IsNullOrWhiteSpace(label)) continue;
        if (!label.StartsWith(BuilderConfig.SyntheticTextureLabelPrefix, StringComparison.Ordinal)) continue;
        if (string.Equals(label, bundleLabel, StringComparison.Ordinal)) continue;
        labelsToRemove.Add(label);
      }
    }

    for (var i = 0; i < labelsToRemove.Count; i++) {
      entry.SetLabel(labelsToRemove[i], false, true, false);
    }

    if (entry.labels == null || !entry.labels.Contains(bundleLabel)) {
      entry.SetLabel(bundleLabel, true, true, false);
      EditorUtility.SetDirty(settings);
    }
  }

  static void ApplyAtlasMetadataLabel(
    AddressableAssetSettings settings,
    AddressableAssetEntry entry,
    bool isAtlasMetadata
  ) {
    if (!isAtlasMetadata || settings == null || entry == null) return;

    var label = SpriteStreamingConfig.AtlasMetadataAddressablesLabel;
    settings.AddLabel(label, false);
    if (entry.labels != null && entry.labels.Contains(label)) return;

    entry.SetLabel(label, true, true, false);
    EditorUtility.SetDirty(settings);
  }

  static string BuildSyntheticTextureBundleLabel(string assetPath) {
    var normalizedPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedPath)) return "";

    var packId = ResolveContentPackRootName(normalizedPath);
    if (string.IsNullOrWhiteSpace(packId)) {
      packId = ResolveProjectTextureRootName(normalizedPath);
    }

    if (string.IsNullOrWhiteSpace(packId)) return "";
    return BuilderConfig.SyntheticTextureLabelPrefix + SanitizeAddressablesLabel(packId);
  }

  static string ResolveContentPackRootName(string assetPath) {
    var prefix = "Packages/com.skaraman.myprojectcontent/";
    if (!assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";

    var remainder = assetPath.Substring(prefix.Length);
    var segments = remainder.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length <= 0) return "";

    if ((string.Equals(segments[0], "Forms", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(segments[0], "Gears", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(segments[0], "Slices", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(segments[0], "Episodes", StringComparison.OrdinalIgnoreCase)) &&
        segments.Length > 1) {
      return segments[1];
    }

    return segments[0];
  }

  static string ResolveProjectTextureRootName(string assetPath) {
    var prefix = "Assets/Sprites/";
    if (!assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";

    var remainder = assetPath.Substring(prefix.Length);
    var slash = remainder.IndexOf('/');
    var root = slash > 0 ? remainder.Substring(0, slash) : remainder;
    return "Project_" + root;
  }

  static string SanitizeAddressablesLabel(string value) {
    var sb = new StringBuilder();
    for (var i = 0; i < value.Length; i++) {
      var c = value[i];
      if (char.IsLetterOrDigit(c)) {
        sb.Append(c);
        continue;
      }

      sb.Append('_');
    }

    return sb.ToString();
  }

  // ─── Cleanup ──────────────────────────────────────────────────────────────

  static void AddRuntimePinnedTextureEntries(BuildState state) {
    var forms = EsperanzaForms.KnownForms;
    for (var i = 0; i < forms.Count; i++) {
      AddRuntimePinnedTextureEntry(
        state,
        SpriteStreamingConfig.BuildEsperanzaExpressionAtlasSourcePath(forms[i], ".png")
      );
    }
  }

  static void AddRuntimePinnedTextureEntry(BuildState state, string sourceAssetPath) {
    if (state == null) return;

    var normalizedSourcePath = NormalizePath(sourceAssetPath);
    if (!IsRuntimeTextureAssetPath(normalizedSourcePath)) return;

    var addedStagePath = false;
    var relativeSpritesPath = GetRelativeSpritesPath(normalizedSourcePath);
    if (!string.IsNullOrWhiteSpace(relativeSpritesPath)) {
      var textureRoots = ContentPackPipeline.GetTextureSearchRoots();
      for (var i = 0; i < textureRoots.Count; i++) {
        var textureRoot = NormalizePath(textureRoots[i]).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(textureRoot)) continue;

        var stagedAssetPath = NormalizePath(textureRoot + "/" + relativeSpritesPath);
        if (AddTextureAssetPathIfPresent(state, stagedAssetPath)) {
          addedStagePath = true;
        }
      }
    }

    if (!addedStagePath) {
      AddTextureAssetPathIfPresent(state, normalizedSourcePath);
    }
  }

  static string GetRelativeSpritesPath(string assetPath) {
    var normalizedPath = NormalizePath(assetPath);
    const string ProjectSpritesRoot = "Assets/Sprites/";

    if (!normalizedPath.StartsWith(ProjectSpritesRoot, StringComparison.OrdinalIgnoreCase)) {
      return "";
    }

    return normalizedPath.Substring(ProjectSpritesRoot.Length);
  }

  static bool AddTextureAssetPathIfPresent(BuildState state, string assetPath) {
    if (state == null) return false;

    var normalizedPath = NormalizePath(assetPath);
    if (!IsRuntimeTextureAssetPath(normalizedPath)) return false;

    var guid = AssetDatabase.AssetPathToGUID(normalizedPath);
    if (string.IsNullOrWhiteSpace(guid)) return false;

    state.activeTextureAssetPaths.Add(normalizedPath);
    return true;
  }

  static void CleanupStaleTextureEntries(BuildState state) {
    if (state == null) return;
    var settings = state.addressables;
    if (settings == null) return;

    var groupsToCleanup = new List<AddressableAssetGroup>();
    var defaultGroup = settings.FindGroup(BuilderConfig.TextureAddressablesGroupName);
    if (defaultGroup != null) {
      groupsToCleanup.Add(defaultGroup);
    }

    if (settings.groups != null) {
      for (var i = 0; i < settings.groups.Count; i++) {
        var g = settings.groups[i];
        if (g != null && g.Name.StartsWith(BuilderConfig.ManagedTextureGroupPrefix, StringComparison.OrdinalIgnoreCase)) {
          groupsToCleanup.Add(g);
        }
      }
    }

    var removedAny = false;
    for (var gIndex = 0; gIndex < groupsToCleanup.Count; gIndex++) {
      var group = groupsToCleanup[gIndex];
      var toRemove = new List<AddressableAssetEntry>();
      foreach (var entry in group.entries) {
        if (entry == null) continue;
        var path = NormalizePath(entry.AssetPath);
        if (!state.activeTextureAssetPaths.Contains(path)) {
          toRemove.Add(entry);
        }
      }
      for (var i = 0; i < toRemove.Count; i++) {
        settings.RemoveAssetEntry(toRemove[i].guid, false);
        removedAny = true;
      }
    }

    if (removedAny) {
      EditorUtility.SetDirty(settings);
    }
  }

  static void CleanupStaleShardAssets(HashSet<string> activeShardPaths) {
    var folder = NormalizePath(BuilderConfig.RuntimeIndexFolder);
    if (!Directory.Exists(folder)) return;

    CleanupStaleShardAssetsByExtension(folder, "*.txt", activeShardPaths);
    CleanupStaleShardAssetsByExtension(folder, "*.bytes", activeShardPaths);
  }

  static void CleanupStaleShardAssetsByExtension(
    string folder,
    string pattern,
    HashSet<string> activeShardPaths
  ) {
    if (string.IsNullOrWhiteSpace(folder) ||
        string.IsNullOrWhiteSpace(pattern) ||
        activeShardPaths == null) {
      return;
    }

    var allFiles = Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly);
    for (var i = 0; i < allFiles.Length; i++) {
      var path = NormalizePath(allFiles[i]);
      if (activeShardPaths.Contains(path)) {
        continue;
      }

      AssetDatabase.DeleteAsset(path);
    }
  }

  static void CleanupStaleIndexEntries(
    BuildState state,
    AddressableAssetGroup indexGroup,
    HashSet<string> activeShardPaths,
    string manifestAssetPath,
    HashSet<string> activeAtlasMetadataPaths
  ) {
    if (indexGroup == null || state == null) return;
    var settings = state.addressables;
    var toRemove = new List<AddressableAssetEntry>();

    foreach (var entry in indexGroup.entries) {
      if (entry == null) continue;
      var path = NormalizePath(entry.AssetPath);
      if (string.Equals(path, NormalizePath(manifestAssetPath), StringComparison.OrdinalIgnoreCase)) continue;
      if (activeShardPaths.Contains(path)) continue;
      if (activeAtlasMetadataPaths.Contains(path)) continue;
      toRemove.Add(entry);
    }
    for (var i = 0; i < toRemove.Count; i++) {
      settings.RemoveAssetEntry(toRemove[i].guid, false);
    }
    if (toRemove.Count > 0) EditorUtility.SetDirty(settings);
  }

  // ─── Shard / manifest writers ─────────────────────────────────────────────

  static string BuildShardAssetPath(string libraryName) {
    var safeName = libraryName.Replace('/', '_').Replace('\\', '_');
    return BuilderConfig.RuntimeIndexFolder + "/Shard_" + safeName + ".txt";
  }

  static string BuildShardBody(List<ShardRow> rows) {
    var sb = new StringBuilder();
    for (var i = 0; i < rows.Count; i++) {
      var row = rows[i];
      sb.Append(row.labelPrefix).Append('\t')
        .Append(row.category).Append('\t')
        .Append(row.frame.ToString(CultureInfo.InvariantCulture)).Append('\t')
        .Append(row.colorAddress).Append('\t')
        .Append(row.normalAddress).Append('\n');
    }
    return sb.ToString();
  }

  static void WriteIfChanged(string assetPath, string content) {
    EnsureFolderExists(Path.GetDirectoryName(assetPath));
    if (File.Exists(assetPath)) {
      var existing = File.ReadAllText(assetPath, Encoding.UTF8);
      if (string.Equals(existing, content, StringComparison.Ordinal)) return;
    }
    File.WriteAllText(assetPath, content, Encoding.UTF8);
    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
  }

  static void WriteManifestTextAsset(string assetPath, List<ManifestRow> entries) {
    var sb = new StringBuilder();
    for (var i = 0; i < entries.Count; i++) {
      var row = entries[i];
      sb.Append(row.libraryName).Append('\t')
        .Append(row.address).Append('\t')
        .Append(row.assetPath).Append('\t')
        .Append(row.rowCount.ToString(CultureInfo.InvariantCulture)).Append('\t')
        .Append(row.contentHash).Append('\n');
    }
    WriteIfChanged(assetPath, sb.ToString());
  }

  static string ComputeHash(string content) {
    using var md5 = MD5.Create();
    var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(content ?? ""));
    var sb = new StringBuilder(bytes.Length * 2);
    for (var i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
    return sb.ToString();
  }

  // ─── Logging summaries ────────────────────────────────────────────────────

  static void LogRuntimeIndexSummary(string contextLabel, bool success, int libraryNameCount, int shardCount, int schemaRepairs, int errorCount) {
    var status = success ? "success" : "failed";
    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "] Runtime index rebuild " + status + "." +
      " libraries=" + libraryNameCount +
      " shards=" + shardCount +
      " schemaRepairs=" + schemaRepairs +
      " errors=" + errorCount
    );
  }

  static void LogSkippedColorRowSummary(string contextLabel, BuildState state) {
    if (state.skippedColorRowCount <= 0 && state.skippedColorLibraryCount <= 0) return;
    var sb = new StringBuilder();
    sb.Append("[SpriteIndexBuilder] [").Append(contextLabel).Append("] Skipped rows/libraries:")
      .Append(" skippedRows=").Append(state.skippedColorRowCount)
      .Append(" skippedLibraries=").Append(state.skippedColorLibraryCount);
    if (state.skippedColorLibrarySummaries.Count > 0) {
      var shown = Math.Min(5, state.skippedColorLibrarySummaries.Count);
      sb.Append(" examples=[").Append(string.Join("; ", state.skippedColorLibrarySummaries.Take(shown))).Append("]");
      if (state.skippedColorLibrarySummaries.Count > shown) sb.Append(" and ").Append(state.skippedColorLibrarySummaries.Count - shown).Append(" more");
    }
    Debug.LogWarning(sb.ToString());
  }

  static void TrackSkippedColorLibrarySummary(BuildState state, string libraryName, string requestedLibraryName, int skippedCount, int totalCount) {
    var summary = libraryName + " (" + skippedCount + "/" + totalCount + " rows skipped)";
    if (state.skippedColorLibrarySummaries.Count < 20) {
      state.skippedColorLibrarySummaries.Add(summary);
    }
  }

  static bool ShouldLogLibraryDiagnostics(string libraryName, string requestedLibraryName) => true;

  static void LogSyntheticTextureLabelSummary(string contextLabel, BuildState state) {
    if (state.syntheticTextureLabelAssignments <= 0) return;
    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "] Synthetic texture label assignments:" +
      " assigned=" + state.syntheticTextureLabelAssignments +
      " uniqueLabels=" + state.syntheticTextureLabelCounts.Count
    );
  }

  static void LogAtlasMetadataSummary(string contextLabel, BuildState state) {
    if (state.activeAtlasMetadataAssetPaths.Count <= 0) return;
    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "] Atlas metadata:" +
      " activeMetadataAssets=" + state.activeAtlasMetadataAssetPaths.Count
    );
  }

  static void LogNormalAddressSummary(string contextLabel, BuildState state) {
    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "] Normal address stats:" +
      " missingLibrary=" + state.missingNormalLibraryCount +
      " autoDerived=" + state.autoDerivedNormalAddressCount +
      " missingAddress=" + state.missingNormalAddressCount
    );
  }

  static void LogLocalIdSupplementSummary(string contextLabel, BuildState state) {
    if (state.supplementedLocalIdAtlasCount <= 0) return;
    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "] Local ID supplement:" +
      " atlases=" + state.supplementedLocalIdAtlasCount +
      " sprites=" + state.supplementedLocalIdCount +
      " skippedHeavyAtlases=" + state.skippedHeavyLocalIdSupplementAtlasCount +
      " skippedHeavySprites=" + state.skippedHeavyLocalIdSupplementSpriteCount
    );
  }

  static void LogGroupedAtlasBuildSurrogateSummary(string contextLabel, BuildState state) {
    if (state.groupedAtlasSurrogateCount <= 0) return;
    Debug.Log(
      "[SpriteIndexBuilder] [" + contextLabel + "] Grouped atlas surrogates:" +
      " surrogates=" + state.groupedAtlasSurrogateCount +
      " copies=" + state.groupedAtlasSurrogateCopyCount
    );
  }

  // ─── Folder / asset utilities ─────────────────────────────────────────────

  static void EnsureFolderExists(string folderPath) {
    var normalized = NormalizePath(folderPath ?? "");
    if (string.IsNullOrWhiteSpace(normalized) || AssetDatabase.IsValidFolder(normalized)) return;
    var parent = Path.GetDirectoryName(normalized);
    if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(NormalizePath(parent))) {
      EnsureFolderExists(parent);
    }
    var leaf = Path.GetFileName(normalized);
    if (!string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(leaf)) {
      AssetDatabase.CreateFolder(NormalizePath(parent), leaf);
    }
  }

  static string RemoveExtension(string path) {
    if (string.IsNullOrWhiteSpace(path)) return path;
    var dot = path.LastIndexOf('.');
    var slash = path.LastIndexOf('/');
    return dot > slash ? path.Substring(0, dot) : path;
  }

  // ─── Regex-free serialized-file parsers ──────────────────────────────────

  static void ParseLabel(string label, out string labelPrefix, out int frame) {
    labelPrefix = label;
    frame = 0;
    if (string.IsNullOrWhiteSpace(label)) return;

    if (int.TryParse(label.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericFrame) &&
        numericFrame >= 0) {
      labelPrefix = "";
      frame = numericFrame;
      return;
    }

    // High performance regex-free parsing of suffix indices (e.g. Hero_Walk_12)
    var lastUnderscore = label.LastIndexOf('_');
    if (lastUnderscore >= 0) {
      var suffix = label.Substring(lastUnderscore + 1);
      if (int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFrame) && parsedFrame >= 0) {
        labelPrefix = label.Substring(0, lastUnderscore);
        frame = parsedFrame;
      }
    }
  }

  static bool TryReadScalar(string trimmedLine, string key, out string value) {
    value = "";
    var prefix = key + ": ";
    if (!trimmedLine.StartsWith(prefix, StringComparison.Ordinal)) return false;
    value = DecodeScalar(trimmedLine.Substring(prefix.Length));
    return true;
  }

  static bool TryReadFileIdReference(string trimmedLine, string key, out string fileId) {
    fileId = "";
    var prefix = key + ": {fileID: ";
    if (!trimmedLine.StartsWith(prefix, StringComparison.Ordinal)) return false;
    var rest = trimmedLine.Substring(prefix.Length);
    var end = rest.IndexOf('}');
    if (end < 0) end = rest.IndexOf(',');
    fileId = end > 0 ? rest.Substring(0, end).Trim() : rest.Trim();
    return !string.IsNullOrWhiteSpace(fileId);
  }

  // High performance regex-free header parser
  static bool TryReadSerializedObjectHeader(string line, out int classId, out string fileId) {
    classId = 0;
    fileId = "";
    if (string.IsNullOrEmpty(line) || !line.StartsWith("--- !u!", StringComparison.Ordinal)) return false;

    var spaceIdx = line.IndexOf(' ', 7);
    if (spaceIdx < 0) return false;

    var classStr = line.Substring(7, spaceIdx - 7);
    if (!int.TryParse(classStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out classId)) return false;

    var ampIdx = line.IndexOf('&', spaceIdx);
    if (ampIdx < 0) return false;

    fileId = line.Substring(ampIdx + 1).Trim();
    return !string.IsNullOrEmpty(fileId);
  }

  static string DecodeScalar(string raw) {
    if (string.IsNullOrEmpty(raw)) return "";
    var trimmed = raw.Trim();
    if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'') {
      return trimmed.Substring(1, trimmed.Length - 2).Replace("''", "'");
    }
    if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') {
      return trimmed.Substring(1, trimmed.Length - 2);
    }
    return trimmed;
  }

}
#endif
