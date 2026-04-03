#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class EsperanzaGearGroupAtlasWindow : EditorWindow {
  const string DefaultOutputSubfolder = "GroupedGearAtlases";
  const string SkinGroupKey = "Skin";
  const string SkinFormName = "Skin";
  const string SkinVariantName = "All";
  const int DuplicateRebindWarningSampleLimit = 8;
  static bool ExportNormalAtlases => false;
  static readonly Dictionary<string, string> PartCodeByToken = new(StringComparer.OrdinalIgnoreCase) {
    { "ArmLeft", "aL" },
    { "ArmRight", "aR" },
    { "Belt", "b" },
    { "CalfLeft", "cL" },
    { "CalfRight", "cR" },
    { "Cape", "c" },
    { "Eyes", "e" },
    { "FootLeft", "fL" },
    { "FootRight", "fR" },
    { "ForearmLeft", "faL" },
    { "ForearmRight", "faR" },
    { "FlapFront", "fl" },
    { "FlapLeft", "flL" },
    { "FlapRight", "flR" },
    { "Hair", "ha" },
    { "HairLeft", "haL" },
    { "HairRight", "haR" },
    { "HandLeft", "hL" },
    { "HandRight", "hR" },
    { "Head", "h" },
    { "HeadBack", "hB" },
    { "Neck", "n" },
    { "Pelvis", "p" },
    { "ShoulderLeft", "sL" },
    { "ShoulderRight", "sR" },
    { "ThighLeft", "tL" },
    { "ThighRight", "tR" },
    { "Torso", "t" }
  };

  [Serializable]
  sealed class GroupedAtlasMetadataPayload {
    public string metadataKind = "grouped";
    public string groupKey;
    public string category;
    public string form;
    public string variant;
    public string partCode;
    public string fileBase;
    public string sourceKind;
    public string representativeSourceAtlasAssetPath;
    public float spritePixelsPerUnit = 100f;
    public int spriteMeshType = (int)SpriteMeshType.Tight;
    public int sourceAtlasCount;
    public int pageIndex;
    public int atlasWidth;
    public int atlasHeight;
    public int padding;
    public List<string> sourceCategories = new();
    public List<GroupedAtlasSpriteMetadata> sprites = new();
  }

  [Serializable]
  sealed class GroupedAtlasRuntimePayload {
    public string metadataKind = "grouped";
    public float spritePixelsPerUnit = 100f;
    public int spriteMeshType = (int)SpriteMeshType.Tight;
    public List<GroupedAtlasRuntimeSpriteMetadata> sprites = new();
  }

  [Serializable]
  sealed class GroupedAtlasRuntimeSpriteMetadata {
    public string name;
    public bool empty;
    public PixelRect packedRect;
  }

  [Serializable]
  sealed class GroupedAtlasSpriteMetadata {
    public string name;
    public bool empty;
    public string sourceCategory;
    public string sourceAtlasAssetPath;
    public string sourceSpriteName;
    public string sourcePartCode;
    public PixelRect trimRectInSourceSprite;
    public PixelRect packedRect;
    public PixelPoint offsetFromCellCenterPx;
  }

  [Serializable]
  sealed class ExistingTrimmedAtlasMetadataPayload {
    public string metadataKind;
    public string coordinateOrigin;
    public List<ExistingTrimmedAtlasSpriteMetadata> sprites = new();
  }

  [Serializable]
  sealed class ExistingTrimmedAtlasSpriteMetadata {
    public string name;
    public bool empty;
    public PixelRect trimRectInCell;
    public PixelRect packedRect;
    public PixelPoint offsetFromCellCenterPx;
  }

  [Serializable]
  struct PixelRect {
    public int x;
    public int y;
    public int width;
    public int height;

    public PixelRect(int x, int y, int width, int height) {
      this.x = x;
      this.y = y;
      this.width = width;
      this.height = height;
    }
  }

  [Serializable]
  struct PixelPoint {
    public float x;
    public float y;

    public PixelPoint(float x, float y) {
      this.x = x;
      this.y = y;
    }
  }

  sealed class SourceAtlasRecord {
    public string category;
    public string form;
    public string variant;
    public string partCode;
    public string atlasPath;
    public string normalAtlasPath;
    public string fileBase;
  }

  sealed class GroupCandidate {
    public string form;
    public string variant;
    public string partCode;
    public bool isSkin;
    public List<SourceAtlasRecord> sourceAtlases = new();
    public List<string> sourceCategories = new();
    public int normalAtlasCount;
  }

  sealed class LoadedAtlas {
    public string atlasPath;
    public Texture2D texture;
    public Color32[] pixels;
    public List<Sprite> orderedSprites = new();
    public Dictionary<string, Sprite> spritesByName = new(StringComparer.Ordinal);
    public Dictionary<string, ExistingTrimmedAtlasSpriteMetadata> trimmedSourceMetadataByName = new(StringComparer.Ordinal);
  }

  sealed class PackedSpriteBuildItem {
    public string outputSpriteName;
    public string sourceCategory;
    public string colorSourceAtlasPath;
    public string normalSourceAtlasPath;
    public string sourceSpriteName;
    public string sourcePartCode;
    public bool empty;
    public PixelRect trimRectInSourceSprite;
    public PixelRect packedRect;
    public PixelPoint offsetFromCellCenterPx;
    public Color32[] colorPixels;
    public Color32[] normalPixels;
    public int pageIndex;
    public bool inheritedTrimMetadata;

    public int Width => Math.Max(1, trimRectInSourceSprite.width);
    public int Height => Math.Max(1, trimRectInSourceSprite.height);
  }

  sealed class AtlasPage {
    public int pageIndex;
    public int width;
    public int height;
    public List<PackedSpriteBuildItem> items = new();
    public string colorAtlasPath;
    public string normalAtlasPath;
  }

  sealed class CleanupPlan {
    public string folderPath;
    public string filePrefix;
    public bool isSkinLibrary;
    public HashSet<string> keepAssetPaths = new(StringComparer.OrdinalIgnoreCase);
  }

  sealed class ExportCleanupSummary {
    public int deletedAssetCount;
    public int deletedFolderCount;
  }

  readonly struct SpriteAssetReference : IEquatable<SpriteAssetReference> {
    public readonly string guid;
    public readonly long localFileId;
    public readonly string assetPath;
    public readonly string spriteName;

    public SpriteAssetReference(string guid, long localFileId, string assetPath, string spriteName) {
      this.guid = guid ?? "";
      this.localFileId = localFileId;
      this.assetPath = assetPath ?? "";
      this.spriteName = spriteName ?? "";
    }

    public bool IsValid => !string.IsNullOrWhiteSpace(guid);

    public bool Equals(SpriteAssetReference other) {
      return localFileId == other.localFileId &&
             string.Equals(guid, other.guid, StringComparison.Ordinal) &&
             string.Equals(spriteName, other.spriteName, StringComparison.Ordinal);
    }

    public override bool Equals(object obj) {
      return obj is SpriteAssetReference other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = 17;
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(guid ?? "");
        hash = (hash * 31) + localFileId.GetHashCode();
        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(spriteName ?? "");
        return hash;
      }
    }
  }

  sealed class PendingGroupedAtlasImport {
    public GroupCandidate candidate;
  }

  readonly struct LibraryEntryScopeKey : IEquatable<LibraryEntryScopeKey> {
    public readonly bool isNormal;
    public readonly bool isSkinLibrary;
    public readonly string category;
    public readonly string partCode;

    public LibraryEntryScopeKey(bool isNormal, bool isSkinLibrary, string category, string partCode) {
      this.isNormal = isNormal;
      this.isSkinLibrary = isSkinLibrary;
      this.category = category ?? "";
      this.partCode = partCode ?? "";
    }

    public bool Equals(LibraryEntryScopeKey other) {
      return isNormal == other.isNormal &&
             isSkinLibrary == other.isSkinLibrary &&
             string.Equals(category, other.category, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(partCode, other.partCode, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object obj) {
      return obj is LibraryEntryScopeKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = 17;
        hash = (hash * 31) + isNormal.GetHashCode();
        hash = (hash * 31) + isSkinLibrary.GetHashCode();
        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(category ?? "");
        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(partCode ?? "");
        return hash;
      }
    }
  }

  readonly struct LibraryEntryKey : IEquatable<LibraryEntryKey> {
    public readonly LibraryEntryScopeKey scopeKey;
    public readonly string label;

    public LibraryEntryKey(bool isNormal, bool isSkinLibrary, string category, string partCode, string label) {
      scopeKey = new LibraryEntryScopeKey(isNormal, isSkinLibrary, category, partCode);
      this.label = label ?? "";
    }

    public bool Equals(LibraryEntryKey other) {
      return scopeKey.Equals(other.scopeKey) &&
             string.Equals(label, other.label, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object obj) {
      return obj is LibraryEntryKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = scopeKey.GetHashCode();
        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(label ?? "");
        return hash;
      }
    }
  }

  readonly struct LibraryEntrySequenceKey : IEquatable<LibraryEntrySequenceKey> {
    public readonly LibraryEntryScopeKey scopeKey;
    public readonly string labelPrefix;

    public LibraryEntrySequenceKey(bool isNormal, bool isSkinLibrary, string category, string partCode, string labelPrefix) {
      scopeKey = new LibraryEntryScopeKey(isNormal, isSkinLibrary, category, partCode);
      this.labelPrefix = labelPrefix ?? "";
    }

    public bool Equals(LibraryEntrySequenceKey other) {
      return scopeKey.Equals(other.scopeKey) &&
             string.Equals(labelPrefix, other.labelPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object obj) {
      return obj is LibraryEntrySequenceKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = scopeKey.GetHashCode();
        hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(labelPrefix ?? "");
        return hash;
      }
    }
  }

  sealed class RebindLabelCleanupPlan {
    public HashSet<string> expectedLabels = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ownedLabelPrefixes = new(StringComparer.OrdinalIgnoreCase);
    public bool deleteNumericLabels;
  }

  sealed class GroupedSpriteReplacementIndex {
    public Dictionary<LibraryEntryKey, SpriteAssetReference> spritesByKey = new();
    public Dictionary<LibraryEntryScopeKey, Dictionary<string, SpriteAssetReference>> labelsByScope = new();
    public Dictionary<LibraryEntryScopeKey, RebindLabelCleanupPlan> cleanupByScope = new();
    public List<CleanupPlan> cleanupPlans = new();
    public int metadataFileCount;
    public int indexedSpriteCount;
    public int duplicateKeyCount;
    public int filledSliceGapCount;
    public List<string> duplicateKeySamples = new();
  }

  sealed class SpriteLibraryCategoryPlan {
    public Dictionary<string, SpriteAssetReference> replacementsByLabel;
    public RebindLabelCleanupPlan cleanupPlan;
  }

  sealed class PendingGroupedSpriteReplacement {
    public SpriteAssetReference replacementSprite;
    public string atlasAssetPath;
    public string groupedSpriteName;
    public string sourceAtlasAssetPath;
    public string sourceSpriteName;
    public string sourceCategory;
    public string form;
    public string variant;
  }

  DefaultAsset sourceFolder;
  string outputSubfolderName = DefaultOutputSubfolder;
  DefaultAsset rebindSourceFolder;
  DefaultAsset rebindSpriteLibraryFolder;
  int maxAtlasSize = 2048;
  int maxSpritesPerAtlasPage = 1024;
  int padding = 1;
  int alphaThreshold = 1;
  bool treatNearWhiteAsEmpty;
  int nearWhiteThreshold = 250;
  Vector2 scrollPosition;
  Vector2 resultsScrollPosition;
  string analyzedSourceFolderPath = "";
  List<GroupCandidate> scannedCandidates = new();

  [MenuItem("Tools/Sprite Streaming/Authoring/Group Esperanza Gear Atlases")]
  static void ShowWindow() {
    GetWindow<EsperanzaGearGroupAtlasWindow>("Gear + Skin Atlases");
  }

  void OnGUI() {
    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

    EditorGUILayout.LabelField("Group Esperanza Gear + Skin Atlases", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "Export and sprite-library rebinding are separate workflows. Export builds grouped atlases plus JSON metadata. Rebind uses those grouped outputs to update matching sprite-library entries later.",
      MessageType.Info);

    EditorGUILayout.LabelField("Export Grouped Atlases", EditorStyles.boldLabel);
    EditorGUI.BeginChangeCheck();
    sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField("Source Folder", sourceFolder, typeof(DefaultAsset), false);
    if (EditorGUI.EndChangeCheck()) {
      InvalidateScan();
    }

    EditorGUI.BeginChangeCheck();
    outputSubfolderName = EditorGUILayout.DelayedTextField("Output Subfolder", outputSubfolderName ?? "");
    if (EditorGUI.EndChangeCheck()) {
      InvalidateScan();
    }
    maxAtlasSize = Mathf.Clamp(EditorGUILayout.DelayedIntField("Max Atlas Size", maxAtlasSize), 64, 2048);
    maxSpritesPerAtlasPage = Mathf.Clamp(EditorGUILayout.DelayedIntField("Max Sprites Per Page", maxSpritesPerAtlasPage), 64, 4096);
    padding = Mathf.Clamp(EditorGUILayout.DelayedIntField("Packing Padding", padding), 0, 64);
    alphaThreshold = Mathf.Clamp(EditorGUILayout.IntSlider("Alpha Threshold", alphaThreshold, 0, 255), 0, 255);
    treatNearWhiteAsEmpty = EditorGUILayout.Toggle("Treat Near-White As Empty", treatNearWhiteAsEmpty);
    using (new EditorGUI.DisabledScope(!treatNearWhiteAsEmpty)) {
      nearWhiteThreshold = Mathf.Clamp(EditorGUILayout.IntSlider("Near-White Threshold", nearWhiteThreshold, 0, 255), 0, 255);
    }
    EditorGUILayout.HelpBox("Grouped export packs color PNG atlases only. '_N' normal atlas outputs are skipped.", MessageType.None);

    using (new EditorGUI.DisabledScope(sourceFolder == null)) {
      using (new EditorGUILayout.HorizontalScope()) {
        if (GUILayout.Button("Analyze Folder")) {
          AnalyzeFolder();
        }

        if (GUILayout.Button("Export Grouped Atlases")) {
          ExportGroupedAtlases();
        }
      }
    }

    EditorGUILayout.Space();
    EditorGUILayout.LabelField("Rebind Sprite Libraries", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "Choose the grouped atlas folder to scan and the target sprite-library folder to update. The rebind pass scans the selected folder recursively for grouped atlas metadata and matches those slices back into the Gear and Skin sprite libraries.",
      MessageType.None);
    rebindSourceFolder = (DefaultAsset)EditorGUILayout.ObjectField("Grouped Atlas Folder", rebindSourceFolder, typeof(DefaultAsset), false);
    rebindSpriteLibraryFolder = (DefaultAsset)EditorGUILayout.ObjectField("Sprite Library Folder", rebindSpriteLibraryFolder, typeof(DefaultAsset), false);
    if (rebindSourceFolder != null && string.IsNullOrWhiteSpace(ResolveRebindSourceFolderPath())) {
      EditorGUILayout.HelpBox("Grouped Atlas Folder must be a project folder asset.", MessageType.Warning);
    }
    if (rebindSpriteLibraryFolder != null && string.IsNullOrWhiteSpace(ResolveRebindSpriteLibraryFolderPath())) {
      EditorGUILayout.HelpBox("Sprite Library Folder must be a project folder asset.", MessageType.Warning);
    }

    using (new EditorGUI.DisabledScope(
      rebindSourceFolder == null ||
      rebindSpriteLibraryFolder == null ||
      string.IsNullOrWhiteSpace(ResolveRebindSourceFolderPath()) ||
      string.IsNullOrWhiteSpace(ResolveRebindSpriteLibraryFolderPath()))) {
      if (GUILayout.Button("Rebind Sprite Libraries")) {
        RebindGroupedSpriteLibraries();
      }
    }

    DrawScanResults();

    EditorGUILayout.EndScrollView();
  }

  void AnalyzeFolder() {
    if (!TryGetSourceFolderPath(out var sourceFolderPath, true)) return;
    var sanitizedOutputSubfolder = GetSanitizedOutputSubfolderName();
    if (string.IsNullOrWhiteSpace(sanitizedOutputSubfolder)) {
      EditorUtility.DisplayDialog("Invalid Output Subfolder", "Provide a valid output subfolder name for grouped atlas exports.", "OK");
      return;
    }

    scannedCandidates = CollectGroupCandidates(sourceFolderPath, sanitizedOutputSubfolder);
    analyzedSourceFolderPath = sourceFolderPath;
    var totalAtlasCount = scannedCandidates.Sum(candidate => candidate.sourceAtlases.Count);
    var skinCandidateCount = scannedCandidates.Count(IsSkinCandidate);
    Debug.Log(
      "[GearGroupAtlas] Scan complete." +
      " source='" + sourceFolderPath + "'" +
      " candidates=" + scannedCandidates.Count +
      " gear_candidates=" + (scannedCandidates.Count - skinCandidateCount) +
      " skin_candidates=" + skinCandidateCount +
      " matched_atlases=" + totalAtlasCount);
  }

  void ExportGroupedAtlases() {
    if (!EnsureScanAvailable(out var sourceFolderPath)) return;

    var sanitizedOutputSubfolder = GetSanitizedOutputSubfolderName();
    if (string.IsNullOrWhiteSpace(sanitizedOutputSubfolder)) {
      EditorUtility.DisplayDialog("Invalid Output Subfolder", "Provide a valid output subfolder name for grouped atlas exports.", "OK");
      return;
    }

    var failureLogs = new List<string>();
    var pendingImports = new List<PendingGroupedAtlasImport>();
    var exportedCandidateCount = 0;
    var exportedPageCount = 0;
    var cleanupSummary = new ExportCleanupSummary();
    var deferredWritePhaseStarted = false;

    try {
      BeginDeferredGroupedAtlasWritePhase(sourceFolderPath, scannedCandidates.Count);
      deferredWritePhaseStarted = true;

      for (var i = 0; i < scannedCandidates.Count; i++) {
        var candidate = scannedCandidates[i];
        if (!TryExportCandidate(sourceFolderPath, candidate, sanitizedOutputSubfolder, pendingImports, out var pageCount, out var error)) {
          AddFailureLog(failureLogs, BuildCandidateLabel(candidate), error);
          continue;
        }

        exportedPageCount += pageCount;
      }

      if (pendingImports.Count > 0) {
        var exportedCandidates = CollectWrittenGroupedAtlasCandidates(pendingImports);
        exportedCandidateCount = exportedCandidates.Count;
        cleanupSummary = CleanupExportedSourceAssets(sourceFolderPath, exportedCandidates);
      }
    }
    finally {
      if (deferredWritePhaseStarted) {
        EndDeferredGroupedAtlasWritePhase(sourceFolderPath, pendingImports.Count, failureLogs.Count);
      }
    }

    Debug.Log(
      "[GearGroupAtlas] Export complete." +
      " source='" + sourceFolderPath + "'" +
      " exported_candidates=" + exportedCandidateCount +
      " exported_pages=" + exportedPageCount +
      " deleted_source_assets=" + cleanupSummary.deletedAssetCount +
      " deleted_source_folders=" + cleanupSummary.deletedFolderCount +
      " failures=" + failureLogs.Count +
      " deferred_import=True");

    for (var i = 0; i < failureLogs.Count; i++) {
      Debug.LogWarning("[GearGroupAtlas] " + failureLogs[i]);
    }
  }

  static void BeginDeferredGroupedAtlasWritePhase(string sourceFolderPath, int candidateCount) {
    AssetDatabase.StartAssetEditing();
    Debug.Log(
      "[GearGroupAtlas] Deferred import write phase started." +
      " source='" + sourceFolderPath + "'" +
      " candidates=" + candidateCount);
  }

  static void EndDeferredGroupedAtlasWritePhase(string sourceFolderPath, int pendingImportCount, int failureCount) {
    AssetDatabase.StopAssetEditing();
    Debug.Log(
      "[GearGroupAtlas] Deferred import write phase completed." +
      " source='" + sourceFolderPath + "'" +
      " pending_imports=" + pendingImportCount +
      " failures=" + failureCount);
  }

  static List<GroupCandidate> CollectWrittenGroupedAtlasCandidates(List<PendingGroupedAtlasImport> pendingImports) {
    var candidates = new List<GroupCandidate>();
    if (pendingImports == null || pendingImports.Count <= 0) return candidates;

    var seenCandidates = new HashSet<GroupCandidate>();
    for (var i = 0; i < pendingImports.Count; i++) {
      var candidate = pendingImports[i]?.candidate;
      if (candidate == null || !seenCandidates.Add(candidate)) continue;
      candidates.Add(candidate);
    }

    return candidates;
  }

  void DrawScanResults() {
    EditorGUILayout.Space();
    EditorGUILayout.LabelField("Scan Results", EditorStyles.boldLabel);

    if (!TryGetSourceFolderPath(out var sourceFolderPath, false)) {
      EditorGUILayout.HelpBox("Select a source folder that contains sliced Esperanza gear or skin atlases.", MessageType.None);
      return;
    }

    if (!HasFreshScan(sourceFolderPath)) {
      EditorGUILayout.HelpBox("Click 'Analyze Folder' to preview the grouped atlas candidates that this tool will export.", MessageType.None);
      return;
    }

    if (scannedCandidates == null || scannedCandidates.Count <= 0) {
      EditorGUILayout.HelpBox("No matching grouped gear or skin atlas candidates were found in the selected folder.", MessageType.Warning);
      return;
    }

    var totalAtlasCount = scannedCandidates.Sum(candidate => candidate.sourceAtlases.Count);
    var skinCandidateCount = scannedCandidates.Count(IsSkinCandidate);
    EditorGUILayout.LabelField(
      "Summary",
      "candidates=" + scannedCandidates.Count +
      ", skin_candidates=" + skinCandidateCount +
      ", matched_atlases=" + totalAtlasCount);

    using (var scroll = new EditorGUILayout.ScrollViewScope(resultsScrollPosition, GUILayout.Height(320f))) {
      resultsScrollPosition = scroll.scrollPosition;
      for (var i = 0; i < scannedCandidates.Count; i++) {
        var candidate = scannedCandidates[i];
        using (new EditorGUILayout.VerticalScope("box")) {
          EditorGUILayout.LabelField(BuildCandidateLabel(candidate), EditorStyles.boldLabel);
          EditorGUILayout.LabelField("Source Atlases", candidate.sourceAtlases.Count.ToString(CultureInfo.InvariantCulture));
          EditorGUILayout.LabelField(
            "Animations",
            candidate.sourceCategories.Count > 0 ? string.Join(", ", candidate.sourceCategories) : "(none)");
          EditorGUILayout.LabelField("Packed Format", "PNG");
        }
      }
    }
  }

  bool TryExportCandidate(
    string sourceRootPath,
    GroupCandidate candidate,
    string sanitizedOutputSubfolder,
    List<PendingGroupedAtlasImport> pendingImports,
    out int exportedPageCount,
    out string error) {
    exportedPageCount = 0;
    error = "";
    if (candidate == null) {
      error = "Missing group candidate data.";
      return false;
    }

    if (!TryBuildPackedItems(candidate, out var items, out var representativeSourceAtlasPath, out error)) {
      return false;
    }
    var inheritedTrimMetadataCount = items.Count(item => item != null && item.inheritedTrimMetadata);

    var outputFolderPath = BuildCandidateOutputFolderPath(sourceRootPath, candidate, sanitizedOutputSubfolder);
    if (string.IsNullOrWhiteSpace(outputFolderPath)) {
      error = "Could not resolve an output folder for group '" + BuildCandidateLabel(candidate) + "'.";
      return false;
    }

    Directory.CreateDirectory(Path.GetFullPath(outputFolderPath));
    if (!TryBuildCandidatePages(outputFolderPath, candidate, items, out var pages, out var reusedPageCount, out error)) {
      return false;
    }

    var candidatePendingImports = new List<PendingGroupedAtlasImport>();

    for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++) {
      var page = pages[pageIndex];
      page.colorAtlasPath = BuildPageAtlasAssetPath(outputFolderPath, candidate, page.pageIndex, false);
      if (ExportNormalAtlases) {
        page.normalAtlasPath = BuildPageAtlasAssetPath(outputFolderPath, candidate, page.pageIndex, true);
      }
    }

    CleanupStaleCandidateOutputs(outputFolderPath, candidate, pages, ExportNormalAtlases);

    for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++) {
      var page = pages[pageIndex];

      if (!TryWritePageTexture(page.colorAtlasPath, page, false, out error)) {
        return false;
      }

      if (!TryWriteMetadata(page.colorAtlasPath, candidate, representativeSourceAtlasPath, page, false, out _, out error)) {
        return false;
      }
      candidatePendingImports.Add(new PendingGroupedAtlasImport {
        candidate = candidate
      });

      if (ExportNormalAtlases) {
        if (!TryWritePageTexture(page.normalAtlasPath, page, true, out error)) {
          return false;
        }

        if (!TryWriteMetadata(page.normalAtlasPath, candidate, representativeSourceAtlasPath, page, true, out _, out error)) {
          return false;
        }
        candidatePendingImports.Add(new PendingGroupedAtlasImport {
          candidate = candidate
        });
      }
    }

    if (pendingImports != null && candidatePendingImports.Count > 0) {
      pendingImports.AddRange(candidatePendingImports);
    }

    exportedPageCount = pages.Count;
    Debug.Log(
      "[GearGroupAtlas] Exported group." +
      " group='" + BuildCandidateLabel(candidate) + "'" +
      " kind='" + (IsSkinCandidate(candidate) ? "skin" : "gear") + "'" +
      " reused_pages=" + reusedPageCount +
      " pages=" + pages.Count +
      " sprites=" + items.Count +
      " inherited_trim_metadata=" + inheritedTrimMetadataCount +
      " source_atlases=" + candidate.sourceAtlases.Count +
      " animations=" + candidate.sourceCategories.Count);
    return true;
  }

  bool TryBuildCandidatePages(
    string outputFolderPath,
    GroupCandidate candidate,
    List<PackedSpriteBuildItem> incomingItems,
    out List<AtlasPage> pages,
    out int reusedPageCount,
    out string error) {
    pages = new List<AtlasPage>();
    reusedPageCount = 0;
    error = "";
    if (incomingItems == null || incomingItems.Count <= 0) {
      error = "No grouped sprite items were available for packing.";
      return false;
    }

    if (!TryLoadExistingGroupedPages(outputFolderPath, candidate, incomingItems, out var existingPages, out error)) {
      return false;
    }

    reusedPageCount = existingPages.Count;
    var remainingItems = new List<PackedSpriteBuildItem>();
    var orderedIncomingItems = BuildGroupedPackSequence(incomingItems);

    for (var i = 0; i < orderedIncomingItems.Count; i++) {
      var item = orderedIncomingItems[i];
      if (!TryPlaceItemIntoExistingPages(existingPages, item)) {
        remainingItems.Add(item);
      }
    }

    existingPages = existingPages
      .Where(page => page != null && page.items != null && page.items.Count > 0)
      .OrderBy(page => page.pageIndex)
      .ToList();

    for (var i = 0; i < existingPages.Count; i++) {
      RefreshPageBounds(existingPages[i], preserveExistingSize: true);
    }

    if (remainingItems.Count > 0) {
      if (!TryPackItemsIntoPages(remainingItems, out var newPages, out error)) {
        return false;
      }

      var pageIndexOffset = existingPages.Count > 0 ? existingPages.Max(page => page.pageIndex) + 1 : 0;
      for (var pageIndex = 0; pageIndex < newPages.Count; pageIndex++) {
        var page = newPages[pageIndex];
        if (page == null) continue;
        page.pageIndex += pageIndexOffset;
        for (var itemIndex = 0; itemIndex < page.items.Count; itemIndex++) {
          if (page.items[itemIndex] == null) continue;
          page.items[itemIndex].pageIndex = page.pageIndex;
        }

        existingPages.Add(page);
      }
    }

    pages = existingPages
      .OrderBy(page => page.pageIndex)
      .ToList();
    Debug.Log(
      "[GearGroupAtlas] Prepared candidate pages." +
      " group='" + BuildCandidateLabel(candidate) + "'" +
      " incoming_sprites=" + incomingItems.Count +
      " reused_pages=" + reusedPageCount +
      " output_pages=" + pages.Count +
      " new_page_sprites=" + remainingItems.Count);
    return pages.Count > 0;
  }

  ExportCleanupSummary CleanupExportedSourceAssets(string sourceRootPath, List<GroupCandidate> exportedCandidates) {
    var summary = new ExportCleanupSummary();
    if (exportedCandidates == null || exportedCandidates.Count <= 0) return summary;

    var sourceAssetPaths = CollectExportedSourceAssetPaths(exportedCandidates);
    if (sourceAssetPaths.Count <= 0) return summary;

    summary.deletedAssetCount = DeleteSourceAssets(sourceAssetPaths);
    summary.deletedFolderCount = DeleteEmptySourceFolders(sourceRootPath, sourceAssetPaths);
    Debug.Log(
      "[GearGroupAtlas] Cleaned exported source assets." +
      " source_root='" + sourceRootPath + "'" +
      " scheduled_assets=" + sourceAssetPaths.Count +
      " deleted_assets=" + summary.deletedAssetCount +
      " deleted_folders=" + summary.deletedFolderCount);
    return summary;
  }

  static HashSet<string> CollectExportedSourceAssetPaths(List<GroupCandidate> exportedCandidates) {
    var sourceAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (exportedCandidates == null) return sourceAssetPaths;

    for (var candidateIndex = 0; candidateIndex < exportedCandidates.Count; candidateIndex++) {
      var candidate = exportedCandidates[candidateIndex];
      if (candidate?.sourceAtlases == null) continue;

      foreach (var record in candidate.sourceAtlases) {
        if (record == null) continue;

        AddCleanupAssetPath(sourceAssetPaths, record.atlasPath);
        AddCleanupMetadataAssetPath(sourceAssetPaths, record.atlasPath);
        AddCleanupAssetPath(sourceAssetPaths, record.normalAtlasPath);
        AddCleanupMetadataAssetPath(sourceAssetPaths, record.normalAtlasPath);
      }
    }

    return sourceAssetPaths;
  }

  static void AddCleanupMetadataAssetPath(HashSet<string> sourceAssetPaths, string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return;
    AddMetadataAssetPaths(sourceAssetPaths, normalizedAssetPath);
  }

  static void AddCleanupAssetPath(HashSet<string> sourceAssetPaths, string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return;
    sourceAssetPaths.Add(normalizedAssetPath);
  }

  static void AddMetadataAssetPaths(ICollection<string> assetPaths, string atlasAssetPath) {
    if (assetPaths == null) return;
    var runtimeMetadataAssetPath = BuildRuntimeMetadataAssetPath(atlasAssetPath);
    if (!string.IsNullOrWhiteSpace(runtimeMetadataAssetPath)) {
      assetPaths.Add(runtimeMetadataAssetPath);
    }

    var editorMetadataAssetPath = BuildEditorMetadataAssetPath(atlasAssetPath);
    if (!string.IsNullOrWhiteSpace(editorMetadataAssetPath)) {
      assetPaths.Add(editorMetadataAssetPath);
    }
  }

  static int DeleteSourceAssets(HashSet<string> sourceAssetPaths) {
    if (sourceAssetPaths == null || sourceAssetPaths.Count <= 0) return 0;

    var deletedAssetCount = 0;
    var orderedAssetPaths = sourceAssetPaths
      .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
      .ToList();

    for (var assetIndex = 0; assetIndex < orderedAssetPaths.Count; assetIndex++) {
      var assetPath = orderedAssetPaths[assetIndex];
      if (string.IsNullOrWhiteSpace(assetPath)) continue;
      if (!File.Exists(Path.GetFullPath(assetPath))) continue;
      if (!AssetDatabase.DeleteAsset(assetPath)) {
        Debug.LogWarning("[GearGroupAtlas] Failed to delete exported source asset. asset='" + assetPath + "'");
        continue;
      }

      deletedAssetCount++;
    }

    return deletedAssetCount;
  }

  static int DeleteEmptySourceFolders(string sourceRootPath, HashSet<string> sourceAssetPaths) {
    var sourceFolderPaths = CollectSourceFolderPathsForCleanup(sourceRootPath, sourceAssetPaths);
    if (sourceFolderPaths.Count <= 0) return 0;

    var deletedFolderCount = 0;
    var orderedFolderPaths = sourceFolderPaths
      .OrderByDescending(path => path.Count(c => c == '/'))
      .ThenByDescending(path => path.Length)
      .ToList();

    for (var folderIndex = 0; folderIndex < orderedFolderPaths.Count; folderIndex++) {
      var folderPath = orderedFolderPaths[folderIndex];
      if (!AssetDatabase.IsValidFolder(folderPath)) continue;
      if (!IsFolderEmptyForCleanup(folderPath)) continue;
      if (!AssetDatabase.DeleteAsset(folderPath)) {
        Debug.LogWarning("[GearGroupAtlas] Failed to delete empty source folder. folder='" + folderPath + "'");
        continue;
      }

      deletedFolderCount++;
    }

    return deletedFolderCount;
  }

  static HashSet<string> CollectSourceFolderPathsForCleanup(string sourceRootPath, HashSet<string> sourceAssetPaths) {
    var folderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var normalizedSourceRootPath = NormalizePath(sourceRootPath).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(normalizedSourceRootPath) || sourceAssetPaths == null) return folderPaths;

    foreach (var assetPath in sourceAssetPaths) {
      var currentFolderPath = NormalizePath(Path.GetDirectoryName(assetPath));
      while (!string.IsNullOrWhiteSpace(currentFolderPath) &&
             currentFolderPath.StartsWith(normalizedSourceRootPath + "/", StringComparison.OrdinalIgnoreCase)) {
        folderPaths.Add(currentFolderPath);
        currentFolderPath = NormalizePath(Path.GetDirectoryName(currentFolderPath));
      }
    }

    return folderPaths;
  }

  static bool IsFolderEmptyForCleanup(string folderPath) {
    var fullFolderPath = Path.GetFullPath(folderPath);
    if (!Directory.Exists(fullFolderPath)) return false;

    var entries = Directory.GetFileSystemEntries(fullFolderPath, "*", SearchOption.TopDirectoryOnly);
    for (var entryIndex = 0; entryIndex < entries.Length; entryIndex++) {
      var entryName = Path.GetFileName(entries[entryIndex]);
      if (string.IsNullOrWhiteSpace(entryName)) continue;
      if (entryName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
      return false;
    }

    return true;
  }

  bool TryBuildPackedItems(
    GroupCandidate candidate,
    out List<PackedSpriteBuildItem> items,
    out string representativeSourceAtlasPath,
    out string error) {
    items = new List<PackedSpriteBuildItem>();
    representativeSourceAtlasPath = "";
    error = "";

    var loadedAtlases = new Dictionary<string, LoadedAtlas>(StringComparer.OrdinalIgnoreCase);
    try {
      var orderedRecords = candidate.sourceAtlases ?? new List<SourceAtlasRecord>();
      for (var i = 0; i < orderedRecords.Count; i++) {
        var record = orderedRecords[i];
        if (record == null) continue;

        if (string.IsNullOrWhiteSpace(representativeSourceAtlasPath)) {
          representativeSourceAtlasPath = record.atlasPath;
        }

        if (!TryGetOrLoadAtlas(record.atlasPath, loadedAtlases, out var colorAtlas, out error)) {
          return false;
        }

        LoadedAtlas normalAtlas = null;
        if (ExportNormalAtlases && !string.IsNullOrWhiteSpace(record.normalAtlasPath)) {
          if (!TryGetOrLoadAtlas(record.normalAtlasPath, loadedAtlases, out normalAtlas, out error)) {
            return false;
          }
        }

        if (colorAtlas.orderedSprites.Count <= 0) {
          error = "Atlas '" + record.atlasPath + "' has no sliced sprites.";
          return false;
        }

        for (var spriteIndex = 0; spriteIndex < colorAtlas.orderedSprites.Count; spriteIndex++) {
          var colorSprite = colorAtlas.orderedSprites[spriteIndex];
          if (colorSprite == null) continue;

          var normalSprite = ExportNormalAtlases ? FindMatchingSprite(normalAtlas, colorSprite.name) : null;
          if (!TryAnalyzeSourceSprite(record, colorAtlas, normalAtlas, colorSprite, normalSprite, out var item, out error)) {
            return false;
          }

          items.Add(item);
        }
      }
    }
    finally {
      foreach (var atlas in loadedAtlases.Values) {
        if (atlas?.texture != null) {
          DestroyImmediate(atlas.texture);
        }
      }
    }

    if (items.Count <= 0) {
      error = "No matching sliced source atlas sprites were found for '" + BuildCandidateLabel(candidate) + "'.";
      return false;
    }

    var disambiguatedCount = EnsureUniqueOutputSpriteNames(items);
    if (disambiguatedCount > 0) {
      Debug.LogWarning(
        "[GearGroupAtlas] Disambiguated duplicate grouped sprite names." +
        " group='" + BuildCandidateLabel(candidate) + "'" +
        " renamed=" + disambiguatedCount);
    }

    return true;
  }

  bool TryLoadExistingGroupedPages(
    string outputFolderPath,
    GroupCandidate candidate,
    List<PackedSpriteBuildItem> incomingItems,
    out List<AtlasPage> pages,
    out string error) {
    pages = new List<AtlasPage>();
    error = "";
    if (string.IsNullOrWhiteSpace(outputFolderPath)) return true;

    var fullOutputFolderPath = Path.GetFullPath(outputFolderPath);
    if (!Directory.Exists(fullOutputFolderPath)) return true;

    var incomingItemNames = new HashSet<string>(StringComparer.Ordinal);
    if (incomingItems != null) {
      for (var i = 0; i < incomingItems.Count; i++) {
        var itemName = incomingItems[i]?.outputSpriteName;
        if (string.IsNullOrWhiteSpace(itemName)) continue;
        incomingItemNames.Add(itemName);
      }
    }

    var filePrefix = BuildOutputFilePrefix(candidate) + "_p";
    var metadataFullPaths = Directory.GetFiles(fullOutputFolderPath, "*.json", SearchOption.TopDirectoryOnly)
      .Where(path => !TrimmedAtlasExporterWindow.IsEditorMetadataAssetPath(path))
      .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
      .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
      .ToList();
    if (metadataFullPaths.Count <= 0) return true;

    for (var i = 0; i < metadataFullPaths.Count; i++) {
      var metadataFullPath = metadataFullPaths[i];
      if (!TryConvertFullPathToAssetPath(metadataFullPath, out var metadataAssetPath)) continue;

      if (!TryLoadGroupedMetadataPayload(metadataAssetPath, out var payload, out error)) {
        error = "Failed to read grouped atlas metadata '" + metadataAssetPath + "': " + error;
        return false;
      }

      if (payload == null ||
          !string.Equals(payload.sourceKind, "color", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      var atlasAssetPath = NormalizePath(Path.ChangeExtension(metadataAssetPath, ".png"));
      if (!TrimmedAtlasExporterWindow.TryLoadTextureFromDisk(atlasAssetPath, out var texture, out error)) {
        return false;
      }

      try {
        var pixels = texture.GetPixels32();
        var page = new AtlasPage {
          pageIndex = payload.pageIndex >= 0 ? payload.pageIndex : pages.Count,
          width = payload.atlasWidth > 0 ? payload.atlasWidth : texture.width,
          height = payload.atlasHeight > 0 ? payload.atlasHeight : texture.height
        };

        var metadataSprites = payload.sprites ?? new List<GroupedAtlasSpriteMetadata>();
        for (var spriteIndex = 0; spriteIndex < metadataSprites.Count; spriteIndex++) {
          var spriteMetadata = metadataSprites[spriteIndex];
          if (spriteMetadata == null || string.IsNullOrWhiteSpace(spriteMetadata.name)) continue;
          if (incomingItemNames.Contains(spriteMetadata.name)) continue;
          if (!TryBuildExistingPackedItem(spriteMetadata, pixels, texture.width, page.pageIndex, out var item, out error)) {
            return false;
          }

          page.items.Add(item);
        }

        if (page.items.Count > 0) {
          pages.Add(page);
        }
      }
      finally {
        DestroyImmediate(texture);
      }
    }

    return true;
  }

  bool TryBuildExistingPackedItem(
    GroupedAtlasSpriteMetadata spriteMetadata,
    Color32[] atlasPixels,
    int atlasWidth,
    int pageIndex,
    out PackedSpriteBuildItem item,
    out string error) {
    item = null;
    error = "";
    if (spriteMetadata == null) {
      error = "Missing grouped atlas sprite metadata.";
      return false;
    }

    var packedRect = spriteMetadata.packedRect;
    if (packedRect.width <= 0 || packedRect.height <= 0) {
      error = "Grouped sprite '" + (spriteMetadata.name ?? "") + "' has an invalid packed rect.";
      return false;
    }

    var pixels = CopyPackedPixels(atlasPixels, atlasWidth, packedRect, out error);
    if (pixels == null) return false;

    var normalizedSourceAtlasPath = NormalizePath(spriteMetadata.sourceAtlasAssetPath);
    item = new PackedSpriteBuildItem {
      outputSpriteName = spriteMetadata.name,
      sourceCategory = spriteMetadata.sourceCategory,
      colorSourceAtlasPath = normalizedSourceAtlasPath,
      normalSourceAtlasPath = normalizedSourceAtlasPath,
      sourceSpriteName = spriteMetadata.sourceSpriteName,
      sourcePartCode = spriteMetadata.sourcePartCode,
      empty = spriteMetadata.empty,
      trimRectInSourceSprite = spriteMetadata.trimRectInSourceSprite,
      packedRect = packedRect,
      offsetFromCellCenterPx = spriteMetadata.offsetFromCellCenterPx,
      colorPixels = pixels,
      pageIndex = pageIndex
    };
    return true;
  }

  bool TryGetOrLoadAtlas(
    string atlasPath,
    Dictionary<string, LoadedAtlas> cache,
    out LoadedAtlas loadedAtlas,
    out string error) {
    error = "";
    var normalizedAtlasPath = NormalizePath(atlasPath);
    if (cache.TryGetValue(normalizedAtlasPath, out loadedAtlas) && loadedAtlas != null) {
      return true;
    }

    if (!TrimmedAtlasExporterWindow.TryLoadTextureFromDisk(normalizedAtlasPath, out var texture, out error)) {
      loadedAtlas = null;
      return false;
    }

    var sprites = AssetDatabase.LoadAllAssetsAtPath(normalizedAtlasPath)
      .OfType<Sprite>()
      .OrderBy(sprite => sprite.name, SpriteSliceAddressUtility.NaturalStringComparer)
      .ToList();
    if (sprites.Count <= 0) {
      DestroyImmediate(texture);
      loadedAtlas = null;
      error = "Atlas '" + normalizedAtlasPath + "' is not sliced into sprites.";
      return false;
    }

    loadedAtlas = new LoadedAtlas {
      atlasPath = normalizedAtlasPath,
      texture = texture,
      pixels = texture.GetPixels32(),
      orderedSprites = sprites,
      trimmedSourceMetadataByName = LoadTrimmedSourceMetadataByName(normalizedAtlasPath)
    };

    for (var i = 0; i < sprites.Count; i++) {
      var sprite = sprites[i];
      if (sprite == null || string.IsNullOrWhiteSpace(sprite.name)) continue;
      loadedAtlas.spritesByName[sprite.name] = sprite;
    }

    cache[normalizedAtlasPath] = loadedAtlas;
    return true;
  }

  bool TryAnalyzeSourceSprite(
    SourceAtlasRecord record,
    LoadedAtlas colorAtlas,
    LoadedAtlas normalAtlas,
    Sprite colorSprite,
    Sprite normalSprite,
    out PackedSpriteBuildItem item,
    out string error) {
    item = null;
    error = "";
    if (record == null || colorAtlas == null || colorSprite == null) {
      error = "Missing atlas data while analyzing grouped sprite.";
      return false;
    }

    if (TryBuildItemFromTrimmedSourceMetadata(record, colorAtlas, normalAtlas, colorSprite, normalSprite, out item, out error)) {
      return true;
    }

    var sourceRect = ToPixelRect(colorSprite.rect);
    AnalyzeTrimmedSprite(colorAtlas, sourceRect, out var trimRect, out var offsetPx, out var colorTrimPixels, out var empty);
    item = new PackedSpriteBuildItem {
      outputSpriteName = BuildGroupedSpriteName(record.partCode, record.category, colorSprite.name),
      sourceCategory = record.category,
      colorSourceAtlasPath = NormalizePath(record.atlasPath),
      normalSourceAtlasPath = NormalizePath(!string.IsNullOrWhiteSpace(record.normalAtlasPath) ? record.normalAtlasPath : record.atlasPath),
      sourceSpriteName = colorSprite.name,
      sourcePartCode = record.partCode,
      empty = empty,
      trimRectInSourceSprite = trimRect,
      offsetFromCellCenterPx = offsetPx,
      colorPixels = colorTrimPixels
    };

    if (ExportNormalAtlases) {
      item.normalPixels = BuildNormalTrimPixels(colorTrimPixels, trimRect, sourceRect, normalAtlas, normalSprite);
    }

    return true;
  }

  bool TryBuildItemFromTrimmedSourceMetadata(
    SourceAtlasRecord record,
    LoadedAtlas colorAtlas,
    LoadedAtlas normalAtlas,
    Sprite colorSprite,
    Sprite normalSprite,
    out PackedSpriteBuildItem item,
    out string error) {
    item = null;
    error = "";
    if (record == null || colorAtlas == null || colorSprite == null) return false;
    if (!TryGetTrimmedSourceMetadata(colorAtlas, colorSprite.name, out var sourceMetadata)) return false;

    var sourcePackedRect = ResolveSourcePackedRect(colorSprite, sourceMetadata);
    var colorPixels = CopyPackedPixels(colorAtlas.pixels, colorAtlas.texture.width, sourcePackedRect, out var copyError);
    if (colorPixels == null) {
      Debug.LogWarning(
        "[GearGroupAtlas] Failed to reuse source trim metadata; falling back to pixel analysis." +
        " atlas='" + colorAtlas.atlasPath + "'" +
        " sprite='" + colorSprite.name + "'" +
        " error='" + copyError + "'");
      return false;
    }

    item = new PackedSpriteBuildItem {
      outputSpriteName = BuildGroupedSpriteName(record.partCode, record.category, colorSprite.name),
      sourceCategory = record.category,
      colorSourceAtlasPath = NormalizePath(record.atlasPath),
      normalSourceAtlasPath = NormalizePath(!string.IsNullOrWhiteSpace(record.normalAtlasPath) ? record.normalAtlasPath : record.atlasPath),
      sourceSpriteName = colorSprite.name,
      sourcePartCode = record.partCode,
      empty = sourceMetadata.empty,
      trimRectInSourceSprite = BuildInheritedTrimRect(sourceMetadata, sourcePackedRect),
      offsetFromCellCenterPx = sourceMetadata.offsetFromCellCenterPx,
      colorPixels = colorPixels,
      inheritedTrimMetadata = true
    };

    if (ExportNormalAtlases) {
      item.normalPixels = BuildNormalPixelsFromPackedSourceMetadata(colorPixels, normalAtlas, normalSprite);
    }

    return true;
  }

  static bool TryGetTrimmedSourceMetadata(
    LoadedAtlas atlas,
    string spriteName,
    out ExistingTrimmedAtlasSpriteMetadata sourceMetadata) {
    sourceMetadata = null;
    if (atlas?.trimmedSourceMetadataByName == null || string.IsNullOrWhiteSpace(spriteName)) return false;
    return atlas.trimmedSourceMetadataByName.TryGetValue(spriteName, out sourceMetadata) && sourceMetadata != null;
  }

  static PixelRect ResolveSourcePackedRect(Sprite sprite, ExistingTrimmedAtlasSpriteMetadata sourceMetadata) {
    var fallbackRect = sprite != null ? ToPixelRect(sprite.rect) : default;
    if (sourceMetadata == null) return fallbackRect;
    return sourceMetadata.packedRect.width > 0 && sourceMetadata.packedRect.height > 0
      ? sourceMetadata.packedRect
      : fallbackRect;
  }

  static PixelRect BuildInheritedTrimRect(ExistingTrimmedAtlasSpriteMetadata sourceMetadata, PixelRect sourcePackedRect) {
    var trimRect = sourceMetadata != null ? sourceMetadata.trimRectInCell : default;
    return new PixelRect(
      trimRect.x,
      trimRect.y,
      Math.Max(1, sourcePackedRect.width),
      Math.Max(1, sourcePackedRect.height));
  }

  Color32[] BuildNormalPixelsFromPackedSourceMetadata(Color32[] colorTrimPixels, LoadedAtlas normalAtlas, Sprite normalSprite) {
    if (colorTrimPixels == null || colorTrimPixels.Length <= 0) {
      return new[] { new Color32(128, 128, 255, 0) };
    }

    if (normalAtlas == null || normalSprite == null) {
      return BuildNeutralNormalPixels(colorTrimPixels);
    }

    TryGetTrimmedSourceMetadata(normalAtlas, normalSprite.name, out var normalMetadata);
    var normalPackedRect = ResolveSourcePackedRect(normalSprite, normalMetadata);
    var rawNormalPixels = CopyPackedPixels(normalAtlas.pixels, normalAtlas.texture.width, normalPackedRect, out var error);
    if (rawNormalPixels == null || rawNormalPixels.Length != colorTrimPixels.Length) {
      if (!string.IsNullOrWhiteSpace(error)) {
        Debug.LogWarning(
          "[GearGroupAtlas] Failed to reuse normal source trim metadata." +
          " atlas='" + normalAtlas.atlasPath + "'" +
          " sprite='" + normalSprite.name + "'" +
          " error='" + error + "'");
      }
      return BuildNeutralNormalPixels(colorTrimPixels);
    }

    var output = new Color32[rawNormalPixels.Length];
    for (var i = 0; i < rawNormalPixels.Length; i++) {
      var source = rawNormalPixels[i];
      output[i] = new Color32(source.r, source.g, source.b, colorTrimPixels[i].a);
    }

    return output;
  }

  void AnalyzeTrimmedSprite(
    LoadedAtlas atlas,
    PixelRect sourceRect,
    out PixelRect trimRect,
    out PixelPoint offsetPx,
    out Color32[] trimmedPixels,
    out bool empty) {
    var minX = sourceRect.width;
    var minY = sourceRect.height;
    var maxX = -1;
    var maxY = -1;
    empty = true;

    for (var localY = 0; localY < sourceRect.height; localY++) {
      for (var localX = 0; localX < sourceRect.width; localX++) {
        var atlasX = sourceRect.x + localX;
        var atlasY = sourceRect.y + localY;
        var color = atlas.pixels[(atlasY * atlas.texture.width) + atlasX];
        if (!IsVisible(color)) continue;
        if (localX < minX) minX = localX;
        if (localY < minY) minY = localY;
        if (localX > maxX) maxX = localX;
        if (localY > maxY) maxY = localY;
        empty = false;
      }
    }

    if (empty) {
      trimRect = new PixelRect(0, 0, 1, 1);
      offsetPx = new PixelPoint(0f, 0f);
      trimmedPixels = new[] { new Color32(0, 0, 0, 0) };
      return;
    }

    trimRect = new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    offsetPx = new PixelPoint(
      (float)Math.Round((minX + (trimRect.width * 0.5f)) - (sourceRect.width * 0.5f), 3),
      (float)Math.Round((minY + (trimRect.height * 0.5f)) - (sourceRect.height * 0.5f), 3));
    trimmedPixels = CopyTrimmedPixels(atlas.pixels, atlas.texture.width, sourceRect, trimRect);
  }

  Color32[] BuildNormalTrimPixels(
    Color32[] colorTrimPixels,
    PixelRect trimRect,
    PixelRect colorSourceRect,
    LoadedAtlas normalAtlas,
    Sprite normalSprite) {
    if (colorTrimPixels == null || colorTrimPixels.Length <= 0) {
      return new[] { new Color32(128, 128, 255, 0) };
    }

    if (normalAtlas == null || normalSprite == null) {
      return BuildNeutralNormalPixels(colorTrimPixels);
    }

    var normalSourceRect = ToPixelRect(normalSprite.rect);
    if (normalSourceRect.width != colorSourceRect.width || normalSourceRect.height != colorSourceRect.height) {
      Debug.LogWarning(
        "[GearGroupAtlas] Normal sprite rect mismatch." +
        " color='" + colorSourceRect.width + "x" + colorSourceRect.height + "'" +
        " normal='" + normalSourceRect.width + "x" + normalSourceRect.height + "'" +
        " atlas='" + normalAtlas.atlasPath + "'" +
        " sprite='" + normalSprite.name + "'");
      return BuildNeutralNormalPixels(colorTrimPixels);
    }

    var rawNormalPixels = CopyTrimmedPixels(normalAtlas.pixels, normalAtlas.texture.width, normalSourceRect, trimRect);
    if (rawNormalPixels.Length != colorTrimPixels.Length) {
      return BuildNeutralNormalPixels(colorTrimPixels);
    }

    var output = new Color32[rawNormalPixels.Length];
    for (var i = 0; i < rawNormalPixels.Length; i++) {
      var source = rawNormalPixels[i];
      output[i] = new Color32(source.r, source.g, source.b, colorTrimPixels[i].a);
    }

    return output;
  }

  static Color32[] BuildNeutralNormalPixels(Color32[] colorTrimPixels) {
    if (colorTrimPixels == null || colorTrimPixels.Length <= 0) {
      return new[] { new Color32(128, 128, 255, 0) };
    }

    var output = new Color32[colorTrimPixels.Length];
    for (var i = 0; i < colorTrimPixels.Length; i++) {
      output[i] = new Color32(128, 128, 255, colorTrimPixels[i].a);
    }

    return output;
  }

  static Dictionary<string, ExistingTrimmedAtlasSpriteMetadata> LoadTrimmedSourceMetadataByName(string atlasAssetPath) {
    var metadataByName = new Dictionary<string, ExistingTrimmedAtlasSpriteMetadata>(StringComparer.Ordinal);
    var metadataAssetPath = BuildRuntimeMetadataAssetPath(atlasAssetPath);
    if (string.IsNullOrWhiteSpace(metadataAssetPath)) return metadataByName;

    var metadataFullPath = ResolveExistingTrimmedMetadataReadPath(metadataAssetPath);
    if (string.IsNullOrWhiteSpace(metadataFullPath)) return metadataByName;

    ExistingTrimmedAtlasMetadataPayload payload;
    try {
      payload = JsonUtility.FromJson<ExistingTrimmedAtlasMetadataPayload>(File.ReadAllText(metadataFullPath));
    }
    catch {
      return metadataByName;
    }

    if (payload == null || payload.sprites == null) return metadataByName;
    if (!string.IsNullOrWhiteSpace(payload.metadataKind) &&
        !string.Equals(payload.metadataKind, "trimmed", StringComparison.OrdinalIgnoreCase)) {
      return metadataByName;
    }

    for (var i = 0; i < payload.sprites.Count; i++) {
      var spriteMetadata = payload.sprites[i];
      if (spriteMetadata == null || string.IsNullOrWhiteSpace(spriteMetadata.name)) continue;
      metadataByName[spriteMetadata.name] = spriteMetadata;
    }

    return metadataByName;
  }

  Color32[] CopyPackedPixels(Color32[] sourcePixels, int atlasWidth, PixelRect packedRect, out string error) {
    error = "";
    if (sourcePixels == null || sourcePixels.Length <= 0) {
      error = "Missing grouped atlas pixels.";
      return null;
    }

    if (atlasWidth <= 0 || packedRect.width <= 0 || packedRect.height <= 0) {
      error = "Invalid grouped atlas packed rect.";
      return null;
    }

    var atlasHeight = sourcePixels.Length / atlasWidth;
    if (packedRect.x < 0 ||
        packedRect.y < 0 ||
        packedRect.x + packedRect.width > atlasWidth ||
        packedRect.y + packedRect.height > atlasHeight) {
      error = "Grouped atlas packed rect exceeds texture bounds.";
      return null;
    }

    return CopyTrimmedPixels(sourcePixels, atlasWidth, packedRect, new PixelRect(0, 0, packedRect.width, packedRect.height));
  }

  bool TryPlaceItemIntoExistingPages(List<AtlasPage> pages, PackedSpriteBuildItem item) {
    if (pages == null || item == null) return false;

    for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++) {
      var page = pages[pageIndex];
      if (page == null) continue;
      if (TryPlaceItemIntoExistingPage(page, item)) {
        return true;
      }
    }

    return false;
  }

  bool TryPlaceItemIntoExistingPage(AtlasPage page, PackedSpriteBuildItem item) {
    if (page == null || item == null) return false;

    var candidateXs = new SortedSet<int> { padding };
    var candidateYs = new SortedSet<int> { padding };
    for (var i = 0; i < page.items.Count; i++) {
      var existingItem = page.items[i];
      if (existingItem == null) continue;
      candidateXs.Add(existingItem.packedRect.x);
      candidateXs.Add(existingItem.packedRect.x + existingItem.packedRect.width + padding);
      candidateYs.Add(existingItem.packedRect.y);
      candidateYs.Add(existingItem.packedRect.y + existingItem.packedRect.height + padding);
    }

    foreach (var y in candidateYs) {
      foreach (var x in candidateXs) {
        if (!CanPlaceItemAt(page, item, x, y)) continue;
        item.packedRect = new PixelRect(x, y, item.Width, item.Height);
        item.pageIndex = page.pageIndex;
        page.items.Add(item);
        return true;
      }
    }

    return false;
  }

  bool CanPlaceItemAt(AtlasPage page, PackedSpriteBuildItem item, int x, int y) {
    if (page == null || item == null) return false;
    if (x < padding || y < padding) return false;
    if (x + item.Width + padding > maxAtlasSize) return false;
    if (y + item.Height + padding > maxAtlasSize) return false;

    var newOccupiedRect = new PixelRect(x, y, item.Width + padding, item.Height + padding);
    for (var i = 0; i < page.items.Count; i++) {
      var existingItem = page.items[i];
      if (existingItem == null) continue;
      var occupiedRect = new PixelRect(
        existingItem.packedRect.x,
        existingItem.packedRect.y,
        existingItem.packedRect.width + padding,
        existingItem.packedRect.height + padding);
      if (DoPixelRectsOverlap(occupiedRect, newOccupiedRect)) {
        return false;
      }
    }

    return true;
  }

  static bool DoPixelRectsOverlap(PixelRect left, PixelRect right) {
    return left.x < right.x + right.width &&
           left.x + left.width > right.x &&
           left.y < right.y + right.height &&
           left.y + left.height > right.y;
  }

  bool TryPackItemsIntoPages(List<PackedSpriteBuildItem> items, out List<AtlasPage> pages, out string error) {
    pages = new List<AtlasPage>();
    error = "";
    if (items == null || items.Count <= 0) {
      error = "No grouped sprite items were available for packing.";
      return false;
    }

    var ordered = BuildGroupedPackSequence(items);

    var currentPage = new AtlasPage { pageIndex = 0 };
    var x = padding;
    var y = padding;
    var rowHeight = 0;
    var usedWidth = 0;

    for (var i = 0; i < ordered.Count; i++) {
      var item = ordered[i];
      if (currentPage.items.Count > 0 && currentPage.items.Count >= maxSpritesPerAtlasPage) {
        CommitPage(pages, ref currentPage, ref x, ref y, ref rowHeight, ref usedWidth);
      }

      if (item.Width + (padding * 2) > maxAtlasSize || item.Height + (padding * 2) > maxAtlasSize) {
        error = "Sprite '" + item.outputSpriteName + "' exceeds the configured max atlas size " + maxAtlasSize + ".";
        return false;
      }

      if (x > padding && x + item.Width + padding > maxAtlasSize) {
        y += rowHeight + padding;
        x = padding;
        rowHeight = 0;
      }

      if (y + item.Height + padding > maxAtlasSize) {
        CommitPage(pages, ref currentPage, ref x, ref y, ref rowHeight, ref usedWidth);
      }

      if (y + item.Height + padding > maxAtlasSize) {
        error = "Sprite '" + item.outputSpriteName + "' could not fit inside a fresh atlas page.";
        return false;
      }

      item.pageIndex = currentPage.pageIndex;
      item.packedRect = new PixelRect(x, y, item.Width, item.Height);
      currentPage.items.Add(item);

      x += item.Width + padding;
      if (item.Height > rowHeight) rowHeight = item.Height;
      if (x > usedWidth) usedWidth = x;
    }

    FinalizePage(currentPage, usedWidth, y, rowHeight);
    pages.Add(currentPage);
    return true;
  }

  void CommitPage(
    List<AtlasPage> pages,
    ref AtlasPage currentPage,
    ref int x,
    ref int y,
    ref int rowHeight,
    ref int usedWidth) {
    if (pages == null || currentPage == null) return;

    FinalizePage(currentPage, usedWidth, y, rowHeight);
    pages.Add(currentPage);
    currentPage = new AtlasPage { pageIndex = pages.Count };
    x = padding;
    y = padding;
    rowHeight = 0;
    usedWidth = 0;
  }

  void FinalizePage(AtlasPage page, int usedWidth, int y, int rowHeight) {
    if (page == null) return;
    page.width = Mathf.Max(1, usedWidth);
    page.height = Mathf.Max(1, y + rowHeight + padding);
  }

  void RefreshPageBounds(AtlasPage page, bool preserveExistingSize) {
    if (page == null) return;

    var minWidth = preserveExistingSize ? Mathf.Max(1, page.width) : 1;
    var minHeight = preserveExistingSize ? Mathf.Max(1, page.height) : 1;
    var usedWidth = minWidth;
    var usedHeight = minHeight;

    for (var i = 0; i < page.items.Count; i++) {
      var item = page.items[i];
      if (item == null) continue;
      usedWidth = Math.Max(usedWidth, item.packedRect.x + item.packedRect.width + padding);
      usedHeight = Math.Max(usedHeight, item.packedRect.y + item.packedRect.height + padding);
    }

    page.width = Mathf.Clamp(usedWidth, 1, maxAtlasSize);
    page.height = Mathf.Clamp(usedHeight, 1, maxAtlasSize);
  }

  bool TryWritePageTexture(string atlasAssetPath, AtlasPage page, bool isNormalAtlas, out string error) {
    error = "";
    var texture = BuildPageTexture(page, isNormalAtlas);
    try {
      var fullPath = Path.GetFullPath(atlasAssetPath);
      Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? "");
      File.WriteAllBytes(fullPath, texture.EncodeToPNG());
      return true;
    }
    catch (Exception ex) {
      error = ex.Message;
      return false;
    }
    finally {
      DestroyImmediate(texture);
    }
  }

  Texture2D BuildPageTexture(AtlasPage page, bool isNormalAtlas) {
    var texture = new Texture2D(page.width, page.height, TextureFormat.RGBA32, false);
    texture.filterMode = FilterMode.Point;
    texture.wrapMode = TextureWrapMode.Clamp;
    var background = isNormalAtlas ? new Color32(128, 128, 255, 0) : new Color32(0, 0, 0, 0);
    texture.SetPixels32(CreateFilledPixels(page.width * page.height, background));

    for (var i = 0; i < page.items.Count; i++) {
      var item = page.items[i];
      var pixels = isNormalAtlas ? item.normalPixels : item.colorPixels;
      texture.SetPixels32(item.packedRect.x, item.packedRect.y, item.packedRect.width, item.packedRect.height, pixels);
    }

    texture.Apply(false, false);
    return texture;
  }

  static Color32[] CreateFilledPixels(int count, Color32 color) {
    var pixels = new Color32[Math.Max(1, count)];
    for (var i = 0; i < pixels.Length; i++) {
      pixels[i] = color;
    }

    return pixels;
  }

  bool TryWriteMetadata(
    string atlasAssetPath,
    GroupCandidate candidate,
    string representativeSourceAtlasAssetPath,
    AtlasPage page,
    bool isNormalMetadata,
    out string metadataAssetPath,
    out string error) {
    metadataAssetPath = "";
    error = "";
    var sourceCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var sourceAtlasPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (page?.items != null) {
      for (var i = 0; i < page.items.Count; i++) {
        var item = page.items[i];
        if (item == null) continue;
        if (!string.IsNullOrWhiteSpace(item.sourceCategory)) {
          sourceCategories.Add(item.sourceCategory.Trim());
        }

        var sourceAtlasPath = NormalizePath(isNormalMetadata ? item.normalSourceAtlasPath : item.colorSourceAtlasPath);
        if (!string.IsNullOrWhiteSpace(sourceAtlasPath)) {
          sourceAtlasPaths.Add(sourceAtlasPath);
        }
      }
    }

    var orderedSourceCategories = sourceCategories
      .OrderBy(category => category, SpriteSliceAddressUtility.NaturalStringComparer)
      .ToList();
    var primarySourceCategory = orderedSourceCategories.FirstOrDefault() ?? candidate.sourceCategories.FirstOrDefault() ?? "";

    var payload = new GroupedAtlasMetadataPayload {
      groupKey = IsSkinCandidate(candidate) ? SkinGroupKey : BuildOutputFilePrefix(candidate),
      category = primarySourceCategory,
      form = candidate.form,
      variant = candidate.variant,
      partCode = candidate.partCode,
      fileBase = "",
      sourceKind = isNormalMetadata ? "normal" : "color",
      representativeSourceAtlasAssetPath = NormalizePath(representativeSourceAtlasAssetPath),
      sourceAtlasCount = sourceAtlasPaths.Count > 0 ? sourceAtlasPaths.Count : candidate.sourceAtlases?.Count ?? 0,
      pageIndex = page.pageIndex,
      atlasWidth = page.width,
      atlasHeight = page.height,
      padding = padding
    };
    TrimmedAtlasExporterWindow.GetSourceImporterSnapshot(representativeSourceAtlasAssetPath, out payload.spritePixelsPerUnit, out payload.spriteMeshType);
    if (orderedSourceCategories.Count > 0) {
      payload.sourceCategories.AddRange(orderedSourceCategories);
    }
    else if (candidate.sourceCategories != null && candidate.sourceCategories.Count > 0) {
      payload.sourceCategories.AddRange(candidate.sourceCategories);
    }

    var orderedMetadataItems = page.items
      .Where(item => item != null)
      .OrderBy(item => item.sourceCategory, SpriteSliceAddressUtility.NaturalStringComparer)
      .ThenBy(item => item.sourceSpriteName, SpriteSliceAddressUtility.NaturalStringComparer)
      .ThenBy(item => item.outputSpriteName, SpriteSliceAddressUtility.NaturalStringComparer)
      .ToList();
    for (var i = 0; i < orderedMetadataItems.Count; i++) {
      var item = orderedMetadataItems[i];
      payload.sprites.Add(new GroupedAtlasSpriteMetadata {
        name = item.outputSpriteName,
        empty = item.empty,
        sourceCategory = item.sourceCategory,
        sourceAtlasAssetPath = isNormalMetadata ? item.normalSourceAtlasPath : item.colorSourceAtlasPath,
        sourceSpriteName = item.sourceSpriteName,
        sourcePartCode = item.sourcePartCode,
        trimRectInSourceSprite = item.trimRectInSourceSprite,
        packedRect = item.packedRect,
        offsetFromCellCenterPx = item.offsetFromCellCenterPx
      });
    }

    try {
      metadataAssetPath = BuildRuntimeMetadataAssetPath(atlasAssetPath);
      WriteJsonPayload(metadataAssetPath, JsonUtility.ToJson(BuildRuntimeGroupedMetadata(payload), true));
      WriteJsonPayload(BuildEditorMetadataAssetPath(atlasAssetPath), JsonUtility.ToJson(payload, true));
      return true;
    }
    catch (Exception ex) {
      error = ex.Message;
      return false;
    }
  }

  void RebindGroupedSpriteLibraries() {
    if (!TryGetRebindSourceFolderPath(out var sourceFolderPath, true)) return;

    if (!TryGetRebindSpriteLibraryFolderPath(out var libraryFolderPath, true)) return;

    var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
    var buildIndexStopwatch = System.Diagnostics.Stopwatch.StartNew();
    if (!TryBuildGroupedSpriteReplacementIndex(sourceFolderPath, out var replacementIndex, out var error)) {
      EditorUtility.DisplayDialog("Rebind Failed", error, "OK");
      return;
    }
    buildIndexStopwatch.Stop();

    Debug.Log(
      "[GearGroupAtlas] Prepared rebind index." +
      " grouped_root='" + sourceFolderPath + "'" +
      " library_root='" + libraryFolderPath + "'" +
      " metadata_files=" + replacementIndex.metadataFileCount +
      " indexed_sprites=" + replacementIndex.spritesByKey.Count +
      " duplicate_keys=" + replacementIndex.duplicateKeyCount +
      " filled_slice_gaps=" + replacementIndex.filledSliceGapCount +
      " build_ms=" + buildIndexStopwatch.ElapsedMilliseconds);

    LogGroupedSpriteReplacementDuplicateSummary(replacementIndex);

    var rebindStopwatch = new System.Diagnostics.Stopwatch();
    var cleanupStopwatch = new System.Diagnostics.Stopwatch();
    var deletedAssets = 0;
    var touchedLibraries = 0;
    var reboundEntries = 0;
    var deletedLabels = 0;
    var createdCategories = 0;
    var createdLabels = 0;
    var unchangedEntries = 0;
    var processedLibraryKinds = new HashSet<bool>();
    var assetEditingStarted = false;
    try {
      AssetDatabase.StartAssetEditing();
      assetEditingStarted = true;

      rebindStopwatch.Start();
      RebindSpriteLibraries(
        libraryFolderPath,
        replacementIndex,
        out touchedLibraries,
        out reboundEntries,
        out deletedLabels,
        out createdCategories,
        out createdLabels,
        out unchangedEntries,
        out processedLibraryKinds);
      rebindStopwatch.Stop();

      cleanupStopwatch.Start();
      var cleanupPlans = replacementIndex.cleanupPlans
        .Where(plan => plan != null && processedLibraryKinds.Contains(plan.isSkinLibrary))
        .ToList();
      deletedAssets = reboundEntries > 0 && cleanupPlans.Count > 0 ? CleanupStaleOutputs(cleanupPlans) : 0;
      cleanupStopwatch.Stop();
    }
    finally {
      if (assetEditingStarted) {
        AssetDatabase.StopAssetEditing();
      }
    }

    totalStopwatch.Stop();
    Debug.Log(
      "[GearGroupAtlas] Rebind complete." +
      " grouped_root='" + sourceFolderPath + "'" +
      " library_root='" + libraryFolderPath + "'" +
      " metadata_files=" + replacementIndex.metadataFileCount +
      " indexed_sprites=" + replacementIndex.spritesByKey.Count +
      " duplicate_keys=" + replacementIndex.duplicateKeyCount +
      " filled_slice_gaps=" + replacementIndex.filledSliceGapCount +
      " libraries=" + touchedLibraries +
      " rebound_entries=" + reboundEntries +
      " unchanged_entries=" + unchangedEntries +
      " deleted_labels=" + deletedLabels +
      " created_categories=" + createdCategories +
      " created_labels=" + createdLabels +
      " cleaned_assets=" + deletedAssets +
      " build_ms=" + buildIndexStopwatch.ElapsedMilliseconds +
      " rebind_ms=" + rebindStopwatch.ElapsedMilliseconds +
      " cleanup_ms=" + cleanupStopwatch.ElapsedMilliseconds +
      " total_ms=" + totalStopwatch.ElapsedMilliseconds +
      " deferred_import=True");
  }

  static void LogGroupedSpriteReplacementDuplicateSummary(GroupedSpriteReplacementIndex replacementIndex) {
    if (replacementIndex == null || replacementIndex.duplicateKeyCount <= 0) return;

    var summary =
      "[GearGroupAtlas] Duplicate grouped sprite replacement keys were collapsed." +
      " duplicates=" + replacementIndex.duplicateKeyCount +
      " sampled=" + replacementIndex.duplicateKeySamples.Count;
    if (replacementIndex.duplicateKeySamples.Count <= 0) {
      Debug.LogWarning(summary);
      return;
    }

    Debug.LogWarning(
      summary + "\n" +
      string.Join(
        "\n",
        replacementIndex.duplicateKeySamples.Select(sample => "  " + sample)));
  }

  bool TryBuildGroupedSpriteReplacementIndex(
    string sourceFolderPath,
    out GroupedSpriteReplacementIndex replacementIndex,
    out string error) {
    replacementIndex = new GroupedSpriteReplacementIndex();
    error = "";

    var groupedOutputRoot = NormalizePath(sourceFolderPath).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(groupedOutputRoot)) {
      error = "Missing grouped atlas folder.";
      return false;
    }

    var sourceFolderFullPath = Path.GetFullPath(groupedOutputRoot);
    if (!Directory.Exists(sourceFolderFullPath)) {
      error = "Grouped atlas folder does not exist on disk: " + sourceFolderFullPath;
      return false;
    }

    var cleanupPlansByKey = new Dictionary<string, CleanupPlan>(StringComparer.OrdinalIgnoreCase);
    var sequencedPendingReplacementsByKey = new Dictionary<LibraryEntrySequenceKey, List<PendingGroupedSpriteReplacement>>();
    var directPendingReplacementsByKey = new Dictionary<LibraryEntryKey, List<PendingGroupedSpriteReplacement>>();
    var metadataFullPaths = Directory.GetFiles(sourceFolderFullPath, "*.json", SearchOption.AllDirectories)
      .Where(path => !TrimmedAtlasExporterWindow.IsEditorMetadataAssetPath(path))
      .ToArray();
    Array.Sort(metadataFullPaths, StringComparer.OrdinalIgnoreCase);

    for (var metadataIndex = 0; metadataIndex < metadataFullPaths.Length; metadataIndex++) {
      var metadataFullPath = metadataFullPaths[metadataIndex];
      if (!TryConvertFullPathToAssetPath(metadataFullPath, out var metadataAssetPath)) continue;

      metadataAssetPath = NormalizePath(metadataAssetPath);
      if (!metadataAssetPath.StartsWith(groupedOutputRoot + "/", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(metadataAssetPath, groupedOutputRoot, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      if (!TryLoadGroupedMetadataPayload(metadataAssetPath, out var payload, out error)) {
        error = "Failed to read grouped atlas metadata '" + metadataAssetPath + "': " + error;
        return false;
      }

      if (payload == null || payload.sprites == null || payload.sprites.Count <= 0) continue;
      var isNormalAtlas = string.Equals(payload.sourceKind, "normal", StringComparison.OrdinalIgnoreCase);
      if (isNormalAtlas) continue;

      var atlasAssetPath = NormalizePath(Path.ChangeExtension(metadataAssetPath, ".png"));
      if (!File.Exists(Path.GetFullPath(atlasAssetPath))) {
        error = "Grouped atlas texture is missing for metadata '" + metadataAssetPath + "'. Expected '" + atlasAssetPath + "'.";
        return false;
      }

      var spriteReferencesByName = BuildSpriteReferenceLookupByName(atlasAssetPath);
      if (spriteReferencesByName.Count <= 0) {
        error = "Grouped atlas '" + atlasAssetPath + "' has no sliced sprites to rebind.";
        return false;
      }

      var isSkinLibrary = IsSkinGroupKey(payload.groupKey);
      replacementIndex.metadataFileCount++;
      if (TryBuildCleanupPlanKey(atlasAssetPath, out var cleanupFolderPath, out var cleanupFilePrefix)) {
        var cleanupKey = cleanupFolderPath + "|" + cleanupFilePrefix;
        if (!cleanupPlansByKey.TryGetValue(cleanupKey, out var cleanupPlan) || cleanupPlan == null) {
          cleanupPlan = new CleanupPlan {
            folderPath = cleanupFolderPath,
            filePrefix = cleanupFilePrefix,
            isSkinLibrary = isSkinLibrary
          };
          cleanupPlansByKey[cleanupKey] = cleanupPlan;
        }

        cleanupPlan.keepAssetPaths.Add(atlasAssetPath);
        AddMetadataAssetPaths(cleanupPlan.keepAssetPaths, atlasAssetPath);
      }

      for (var spriteIndex = 0; spriteIndex < payload.sprites.Count; spriteIndex++) {
        var groupedSprite = payload.sprites[spriteIndex];
        if (groupedSprite == null || string.IsNullOrWhiteSpace(groupedSprite.name)) continue;
        if (!spriteReferencesByName.TryGetValue(groupedSprite.name, out var replacementSprite) || !replacementSprite.IsValid) continue;

        var sourceCategory = string.IsNullOrWhiteSpace(groupedSprite.sourceCategory)
          ? (payload.category ?? "").Trim()
          : groupedSprite.sourceCategory.Trim();
        if (string.IsNullOrWhiteSpace(sourceCategory)) continue;

        var partCode = string.IsNullOrWhiteSpace(groupedSprite.sourcePartCode)
          ? (payload.partCode ?? "").Trim()
          : groupedSprite.sourcePartCode.Trim();
        if (string.IsNullOrWhiteSpace(partCode) && !TryExtractPartCode(groupedSprite.name, out partCode)) continue;

        var pendingReplacement = new PendingGroupedSpriteReplacement {
          replacementSprite = replacementSprite,
          atlasAssetPath = atlasAssetPath,
          groupedSpriteName = groupedSprite.name,
          sourceAtlasAssetPath = NormalizePath(groupedSprite.sourceAtlasAssetPath),
          sourceSpriteName = ResolveGroupedSpriteSourceSortName(groupedSprite),
          sourceCategory = sourceCategory,
          form = payload.form,
          variant = payload.variant
        };

        if (TryBuildLibraryEntrySequenceKey(
              payload,
              groupedSprite,
              isNormalAtlas,
              isSkinLibrary,
              sourceCategory,
              partCode,
              out var sequenceKey)) {
          if (!sequencedPendingReplacementsByKey.TryGetValue(sequenceKey, out var groupedPendingReplacements) || groupedPendingReplacements == null) {
            groupedPendingReplacements = new List<PendingGroupedSpriteReplacement>();
            sequencedPendingReplacementsByKey[sequenceKey] = groupedPendingReplacements;
          }

          groupedPendingReplacements.Add(pendingReplacement);
          continue;
        }

        var directLabel = BuildLibraryEntryLabel(payload, groupedSprite);
        if (string.IsNullOrWhiteSpace(directLabel)) continue;

        var directKey = new LibraryEntryKey(isNormalAtlas, isSkinLibrary, sourceCategory, partCode, directLabel);
        if (!directPendingReplacementsByKey.TryGetValue(directKey, out var directPendingReplacements) || directPendingReplacements == null) {
          directPendingReplacements = new List<PendingGroupedSpriteReplacement>();
          directPendingReplacementsByKey[directKey] = directPendingReplacements;
        }

        directPendingReplacements.Add(pendingReplacement);
      }
    }

    foreach (var pendingPair in sequencedPendingReplacementsByKey) {
      var sequenceKey = pendingPair.Key;
      var pendingGroup = pendingPair.Value;
      if (pendingGroup == null || pendingGroup.Count <= 0) continue;
      if (pendingGroup.Count > 1) {
        pendingGroup.Sort(ComparePendingGroupedSpriteReplacements);
      }

      var expandedPendingGroup = ExpandPendingGroupedSpriteSequenceBySourceSlices(
        pendingGroup,
        replacementIndex);
      for (var replacementIndexPosition = 0; replacementIndexPosition < expandedPendingGroup.Count; replacementIndexPosition++) {
        var pendingReplacement = expandedPendingGroup[replacementIndexPosition];
        if (pendingReplacement == null) continue;

        var label = BuildSequencedLibraryEntryLabel(sequenceKey, replacementIndexPosition + 1);
        if (string.IsNullOrWhiteSpace(label)) continue;

        var libraryKey = new LibraryEntryKey(
          sequenceKey.scopeKey.isNormal,
          sequenceKey.scopeKey.isSkinLibrary,
          sequenceKey.scopeKey.category,
          sequenceKey.scopeKey.partCode,
          label);

        TryAddGroupedSpriteReplacement(
          replacementIndex,
          libraryKey,
          pendingReplacement.replacementSprite,
          pendingReplacement.atlasAssetPath,
          pendingReplacement.groupedSpriteName,
          pendingReplacement.sourceSpriteName,
          pendingReplacement.sourceCategory,
          pendingReplacement.form,
          pendingReplacement.variant);

        RegisterOwnedRebindLabel(replacementIndex, libraryKey.scopeKey, label);
      }
    }

    foreach (var pendingPair in directPendingReplacementsByKey) {
      var directKey = pendingPair.Key;
      var pendingGroup = pendingPair.Value;
      if (pendingGroup == null || pendingGroup.Count <= 0) continue;
      if (pendingGroup.Count > 1) {
        pendingGroup.Sort(ComparePendingGroupedSpriteReplacements);
      }

      for (var replacementIndexPosition = 0; replacementIndexPosition < pendingGroup.Count; replacementIndexPosition++) {
        var pendingReplacement = pendingGroup[replacementIndexPosition];
        if (pendingReplacement == null) continue;

        TryAddGroupedSpriteReplacement(
          replacementIndex,
          directKey,
          pendingReplacement.replacementSprite,
          pendingReplacement.atlasAssetPath,
          pendingReplacement.groupedSpriteName,
          pendingReplacement.sourceSpriteName,
          pendingReplacement.sourceCategory,
          pendingReplacement.form,
          pendingReplacement.variant);

        RegisterOwnedRebindLabel(replacementIndex, directKey.scopeKey, directKey.label);
        break;
      }
    }

    replacementIndex.cleanupPlans = cleanupPlansByKey.Values
      .OrderBy(plan => plan.folderPath, StringComparer.OrdinalIgnoreCase)
      .ThenBy(plan => plan.filePrefix, StringComparer.OrdinalIgnoreCase)
      .ToList();

    if (replacementIndex.metadataFileCount <= 0) {
      error = "No grouped atlas metadata was found under '" + groupedOutputRoot + "'.";
      return false;
    }

    if (replacementIndex.spritesByKey.Count <= 0) {
      error = "Grouped atlas metadata was found, but no replacement sprites could be indexed for rebinding.";
      return false;
    }

    return true;
  }

  static void TryAddGroupedSpriteReplacement(
    GroupedSpriteReplacementIndex replacementIndex,
    LibraryEntryKey key,
    SpriteAssetReference replacementSprite,
    string atlasAssetPath,
    string groupedSpriteName,
    string sourceSpriteName,
    string sourceCategory,
    string form,
    string variant) {
    if (replacementIndex == null || !replacementSprite.IsValid) return;

    if (replacementIndex.spritesByKey.TryGetValue(key, out var existing) && existing.IsValid && !existing.Equals(replacementSprite)) {
      replacementIndex.duplicateKeyCount++;
      if (replacementIndex.duplicateKeySamples.Count < DuplicateRebindWarningSampleLimit) {
        replacementIndex.duplicateKeySamples.Add(
          "category='" + key.scopeKey.category + "'" +
          " part='" + key.scopeKey.partCode + "'" +
          " label='" + key.label + "'" +
          " normal=" + key.scopeKey.isNormal +
          " skin=" + key.scopeKey.isSkinLibrary +
          " source_category='" + (sourceCategory ?? "") + "'" +
          " form='" + (form ?? "") + "'" +
          " variant='" + (variant ?? "") + "'" +
          " source_sprite='" + (sourceSpriteName ?? "") + "'" +
          " existing='" + existing.assetPath + "[" + existing.spriteName + "]'" +
          " incoming='" + atlasAssetPath + "[" + groupedSpriteName + "]'");
      }
      return;
    }

    replacementIndex.spritesByKey[key] = replacementSprite;
    if (!replacementIndex.labelsByScope.TryGetValue(key.scopeKey, out var replacementsByLabel) || replacementsByLabel == null) {
      replacementsByLabel = new Dictionary<string, SpriteAssetReference>(StringComparer.OrdinalIgnoreCase);
      replacementIndex.labelsByScope[key.scopeKey] = replacementsByLabel;
    }

    replacementsByLabel[key.label] = replacementSprite;
    replacementIndex.indexedSpriteCount = replacementIndex.spritesByKey.Count;
  }

  static bool TryBuildLibraryEntrySequenceKey(
    GroupedAtlasMetadataPayload payload,
    GroupedAtlasSpriteMetadata sprite,
    bool isNormalAtlas,
    bool isSkinLibrary,
    string sourceCategory,
    string partCode,
    out LibraryEntrySequenceKey sequenceKey) {
    sequenceKey = default;

    if (isSkinLibrary) {
      sequenceKey = new LibraryEntrySequenceKey(isNormalAtlas, true, sourceCategory, partCode, "");
      return true;
    }

    var labelPrefix = BuildGearLabelPrefix(payload?.form, payload?.variant);
    if (string.IsNullOrWhiteSpace(labelPrefix)) {
      var fallbackLabel = BuildLibraryEntryLabel(payload, sprite);
      if (!TryExtractRebindLabelPrefix(fallbackLabel, out labelPrefix)) {
        return false;
      }
    }

    sequenceKey = new LibraryEntrySequenceKey(isNormalAtlas, false, sourceCategory, partCode, labelPrefix);
    return true;
  }

  static string BuildSequencedLibraryEntryLabel(LibraryEntrySequenceKey sequenceKey, int labelIndex) {
    if (labelIndex <= 0) return "";

    var indexText = labelIndex.ToString(CultureInfo.InvariantCulture);
    if (sequenceKey.scopeKey.isSkinLibrary || string.IsNullOrWhiteSpace(sequenceKey.labelPrefix)) {
      return indexText;
    }

    return sequenceKey.labelPrefix + "_" + indexText;
  }

  static void RegisterOwnedRebindLabel(GroupedSpriteReplacementIndex replacementIndex, LibraryEntryScopeKey scopeKey, string label) {
    if (replacementIndex == null || string.IsNullOrWhiteSpace(label)) return;

    if (!replacementIndex.cleanupByScope.TryGetValue(scopeKey, out var cleanupPlan) || cleanupPlan == null) {
      cleanupPlan = new RebindLabelCleanupPlan();
      replacementIndex.cleanupByScope[scopeKey] = cleanupPlan;
    }

    cleanupPlan.expectedLabels.Add(label);
    if (scopeKey.isSkinLibrary) {
      cleanupPlan.deleteNumericLabels = true;
      return;
    }

    if (TryExtractRebindLabelPrefix(label, out var labelPrefix)) {
      cleanupPlan.ownedLabelPrefixes.Add(labelPrefix);
    }
  }

  static bool ShouldDeleteMissingRebindLabel(RebindLabelCleanupPlan cleanupPlan, string entryName) {
    if (cleanupPlan == null || string.IsNullOrWhiteSpace(entryName)) return false;
    if (cleanupPlan.expectedLabels.Contains(entryName)) return false;

    if (cleanupPlan.deleteNumericLabels &&
        int.TryParse(entryName.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) {
      return true;
    }

    return TryExtractRebindLabelPrefix(entryName, out var labelPrefix) &&
           cleanupPlan.ownedLabelPrefixes.Contains(labelPrefix);
  }

  static bool TryExtractRebindLabelPrefix(string label, out string labelPrefix) {
    labelPrefix = "";
    if (string.IsNullOrWhiteSpace(label)) return false;

    var normalizedLabel = label.Trim();
    var separatorIndex = normalizedLabel.LastIndexOf('_');
    if (separatorIndex <= 0 || separatorIndex >= normalizedLabel.Length - 1) return false;
    if (!int.TryParse(normalizedLabel.Substring(separatorIndex + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) {
      return false;
    }

    labelPrefix = normalizedLabel.Substring(0, separatorIndex).Trim();
    return !string.IsNullOrWhiteSpace(labelPrefix);
  }

  void RebindSpriteLibraries(
    string libraryFolderPath,
    GroupedSpriteReplacementIndex replacementIndex,
    out int touchedLibraries,
    out int reboundEntries,
    out int deletedLabels,
    out int createdCategories,
    out int createdLabels,
    out int unchangedEntries,
    out HashSet<bool> processedLibraryKinds) {
    touchedLibraries = 0;
    reboundEntries = 0;
    deletedLabels = 0;
    createdCategories = 0;
    createdLabels = 0;
    unchangedEntries = 0;
    processedLibraryKinds = new HashSet<bool>();
    if (replacementIndex == null || replacementIndex.labelsByScope.Count <= 0) return;

    var libraryFullPath = Path.GetFullPath(libraryFolderPath);
    if (!Directory.Exists(libraryFullPath)) return;

    var libraryPaths = Directory.GetFiles(libraryFullPath, "*.spriteLib", SearchOption.AllDirectories);
    Array.Sort(libraryPaths, StringComparer.OrdinalIgnoreCase);
    var parsedLibraryCount = 0;
    var loadFailedCount = 0;
    var missingLibraryPropertyCount = 0;
    var matchedCategoryCount = 0;
    var skippedLibraryLogCount = 0;
    var failedLibraryCount = 0;

    for (var libraryIndex = 0; libraryIndex < libraryPaths.Length; libraryIndex++) {
      var libraryFullAssetPath = libraryPaths[libraryIndex];
      if (!TryConvertFullPathToAssetPath(libraryFullAssetPath, out var libraryPath)) continue;

      libraryPath = NormalizePath(libraryPath);
      if (!TryParseSpriteLibraryDescriptor(libraryPath, out var partCode, out var isNormalLibrary, out var isSkinLibrary)) continue;
      if (isNormalLibrary) continue;
      parsedLibraryCount++;
      processedLibraryKinds.Add(isSkinLibrary);

      if (!TryRebindSpriteLibraryText(
            libraryPath,
            partCode,
            isNormalLibrary,
            isSkinLibrary,
            replacementIndex,
            out var libraryChanged,
            out var libraryReboundEntries,
            out var libraryDeletedLabels,
            out var libraryCreatedCategories,
            out var libraryCreatedLabels,
            out var libraryUnchangedEntries,
            out var libraryMatchedCategoryCount,
            out var rebindError)) {
        loadFailedCount++;
        failedLibraryCount++;
        if (skippedLibraryLogCount < 8) {
          Debug.LogWarning(
            "[GearGroupAtlas] Rebind skipped sprite library because it could not be rewritten." +
            " path='" + libraryPath + "'" +
            " error='" + rebindError + "'");
          skippedLibraryLogCount++;
        }
        continue;
      }

      if (libraryMatchedCategoryCount <= 0 && skippedLibraryLogCount < 8) {
        Debug.Log(
          "[GearGroupAtlas] Rebind found no matching categories for sprite library." +
          " path='" + libraryPath + "'" +
          " part='" + partCode + "'" +
          " normal=" + isNormalLibrary +
          " skin=" + isSkinLibrary);
        skippedLibraryLogCount++;
      }

      matchedCategoryCount += libraryMatchedCategoryCount;
      unchangedEntries += libraryUnchangedEntries;

      if (libraryMatchedCategoryCount > 0 || libraryChanged) {
        Debug.Log(
          "[GearGroupAtlas] Rebind processed sprite library." +
          " index=" + (libraryIndex + 1) + "/" + libraryPaths.Length +
          " path='" + libraryPath + "'" +
          " matched_categories=" + libraryMatchedCategoryCount +
          " rebound_entries=" + libraryReboundEntries +
          " unchanged_entries=" + libraryUnchangedEntries +
          " deleted_labels=" + libraryDeletedLabels +
          " created_categories=" + libraryCreatedCategories +
          " created_labels=" + libraryCreatedLabels +
          " changed=" + libraryChanged);
      }

      if (!libraryChanged) continue;

      touchedLibraries++;
      reboundEntries += libraryReboundEntries;
      deletedLabels += libraryDeletedLabels;
      createdCategories += libraryCreatedCategories;
      createdLabels += libraryCreatedLabels;
    }

    if (touchedLibraries <= 0) {
      Debug.LogWarning(
        "[GearGroupAtlas] Rebind updated no sprite libraries." +
        " library_files=" + libraryPaths.Length +
        " parsed_libraries=" + parsedLibraryCount +
        " matched_categories=" + matchedCategoryCount +
        " unchanged_entries=" + unchangedEntries +
        " failed_libraries=" + failedLibraryCount +
        " load_failures=" + loadFailedCount +
        " missing_library_property=" + missingLibraryPropertyCount);
    }
  }

  bool TryRebindSpriteLibraryText(
    string libraryPath,
    string partCode,
    bool isNormalLibrary,
    bool isSkinLibrary,
    GroupedSpriteReplacementIndex replacementIndex,
    out bool libraryChanged,
    out int libraryReboundEntries,
    out int libraryDeletedLabels,
    out int libraryCreatedCategories,
    out int libraryCreatedLabels,
    out int libraryUnchangedEntries,
    out int libraryMatchedCategoryCount,
    out string error) {
    libraryChanged = false;
    libraryReboundEntries = 0;
    libraryDeletedLabels = 0;
    libraryCreatedCategories = 0;
    libraryCreatedLabels = 0;
    libraryUnchangedEntries = 0;
    libraryMatchedCategoryCount = 0;
    error = "";
    if (replacementIndex == null) return true;

    var libraryFullPath = Path.GetFullPath(libraryPath);
    if (!File.Exists(libraryFullPath)) {
      error = "Sprite library file does not exist on disk.";
      return false;
    }

    string originalText;
    try {
      originalText = File.ReadAllText(libraryFullPath);
    }
    catch (Exception ex) {
      error = ex.Message;
      return false;
    }

    var lineEnding = originalText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    var deleteGearSkinLabels =
      !isSkinLibrary &&
      originalText.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) >= 0;
    var categoryPlansByName = BuildSpriteLibraryCategoryPlans(
      replacementIndex,
      isNormalLibrary,
      isSkinLibrary,
      partCode);
    if (categoryPlansByName.Count <= 0 && !deleteGearSkinLabels) {
      return true;
    }

    var lines = SplitTextIntoLines(originalText);
    var existingCategoryNames = CollectSpriteLibraryCategoryNames(lines);

    var rewritten = new System.Text.StringBuilder(originalText.Length + 256);
    var insideLibrary = false;
    var insideOverrideEntries = false;
    var currentCategoryAllowsEntryRewrite = false;
    var currentCategorySawOverrideEntries = false;
    var appendedMissingCategories = false;
    var pendingCategoryHashRewrite = false;
    var pendingCategoryHashName = "";
    SpriteLibraryCategoryPlan currentCategoryPlan = null;
    RebindLabelCleanupPlan currentCategoryCleanupPlan = null;
    HashSet<string> currentCategoryRetainedLabels = null;
    var seenCategoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++) {
      var line = lines[lineIndex];
      if (!insideLibrary) {
        AppendLine(rewritten, line, lineEnding);
        if (string.Equals(line, "  m_Library:", StringComparison.Ordinal)) {
          insideLibrary = true;
        }
        continue;
      }

      if (IsSpriteLibraryListBoundary(line)) {
        FinalizeSpriteLibraryCategoryRewrite(
          currentCategoryPlan,
          currentCategoryRetainedLabels,
          currentCategorySawOverrideEntries,
          rewritten,
          lineEnding,
          ref libraryChanged,
          ref libraryCreatedLabels);
        currentCategoryPlan = null;
        currentCategoryCleanupPlan = null;
        currentCategoryRetainedLabels = null;
        currentCategoryAllowsEntryRewrite = false;
        currentCategorySawOverrideEntries = false;
        pendingCategoryHashRewrite = false;
        pendingCategoryHashName = "";
        insideOverrideEntries = false;

        if (!appendedMissingCategories) {
          AppendMissingSpriteLibraryCategories(
            categoryPlansByName,
            seenCategoryNames,
            rewritten,
            lineEnding,
            ref libraryChanged,
            ref libraryMatchedCategoryCount,
            ref libraryCreatedCategories,
            ref libraryCreatedLabels);
          appendedMissingCategories = true;
        }

        insideLibrary = false;
        AppendLine(rewritten, line, lineEnding);
        continue;
      }

      if (line.StartsWith("  - m_Name: ", StringComparison.Ordinal)) {
        var categoryName = line.Substring("  - m_Name: ".Length).Trim();
        var resolvedCategoryName = categoryName;
        var resolvedCategoryPlan = (SpriteLibraryCategoryPlan)null;
        FinalizeSpriteLibraryCategoryRewrite(
          currentCategoryPlan,
          currentCategoryRetainedLabels,
          currentCategorySawOverrideEntries,
          rewritten,
          lineEnding,
          ref libraryChanged,
          ref libraryCreatedLabels);
        TryResolveSpriteLibraryCategoryPlan(
          categoryPlansByName,
          categoryName,
          out resolvedCategoryName,
          out resolvedCategoryPlan);
        var useCleanupOnlyRewrite =
          resolvedCategoryPlan != null &&
          !string.Equals(categoryName, resolvedCategoryName, StringComparison.OrdinalIgnoreCase) &&
          ContainsEquivalentSpriteLibraryCategory(existingCategoryNames, resolvedCategoryName);
        seenCategoryNames.Add(resolvedCategoryName);
        currentCategoryPlan = useCleanupOnlyRewrite ? null : resolvedCategoryPlan;
        currentCategoryCleanupPlan = resolvedCategoryPlan?.cleanupPlan;
        currentCategoryRetainedLabels = currentCategoryPlan != null || currentCategoryCleanupPlan != null
          ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
          : null;
        currentCategoryAllowsEntryRewrite =
          currentCategoryPlan != null ||
          currentCategoryCleanupPlan != null ||
          deleteGearSkinLabels;
        currentCategorySawOverrideEntries = false;
        pendingCategoryHashRewrite =
          !useCleanupOnlyRewrite &&
          !string.Equals(categoryName, resolvedCategoryName, StringComparison.Ordinal) &&
          !string.IsNullOrWhiteSpace(resolvedCategoryName);
        pendingCategoryHashName = pendingCategoryHashRewrite ? resolvedCategoryName : "";
        insideOverrideEntries = false;
        if (resolvedCategoryPlan != null) {
          libraryMatchedCategoryCount++;
        }

        if (pendingCategoryHashRewrite) {
          line = "  - m_Name: " + resolvedCategoryName;
          libraryChanged = true;
        }

        AppendLine(rewritten, line, lineEnding);
        continue;
      }

      if (pendingCategoryHashRewrite && line.StartsWith("    m_Hash: ", StringComparison.Ordinal)) {
        var rewrittenHashLine =
          "    m_Hash: " + GetSpriteLibraryStringHash(pendingCategoryHashName).ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(line, rewrittenHashLine, StringComparison.Ordinal)) {
          line = rewrittenHashLine;
          libraryChanged = true;
        }

        pendingCategoryHashRewrite = false;
        pendingCategoryHashName = "";
        AppendLine(rewritten, line, lineEnding);
        continue;
      }

      if (currentCategoryAllowsEntryRewrite && string.Equals(line, "    m_OverrideEntries:", StringComparison.Ordinal)) {
        insideOverrideEntries = true;
        currentCategorySawOverrideEntries = true;
        AppendLine(rewritten, line, lineEnding);
        continue;
      }

      if (currentCategoryAllowsEntryRewrite && insideOverrideEntries && line.StartsWith("    - m_Name: ", StringComparison.Ordinal)) {
        var entryBlockEnd = FindSpriteLibraryEntryBlockEnd(lines, lineIndex + 1);
        RewriteSpriteLibraryEntryBlock(
          lines,
          lineIndex,
          entryBlockEnd,
          currentCategoryPlan?.replacementsByLabel,
          currentCategoryCleanupPlan,
          deleteGearSkinLabels,
          currentCategoryRetainedLabels,
          rewritten,
          lineEnding,
          ref libraryChanged,
          ref libraryReboundEntries,
          ref libraryDeletedLabels,
          ref libraryUnchangedEntries);
        lineIndex = entryBlockEnd - 1;
        continue;
      }

      AppendLine(rewritten, line, lineEnding);
    }

    if (insideLibrary) {
      FinalizeSpriteLibraryCategoryRewrite(
        currentCategoryPlan,
        currentCategoryRetainedLabels,
        currentCategorySawOverrideEntries,
        rewritten,
        lineEnding,
        ref libraryChanged,
        ref libraryCreatedLabels);
      if (!appendedMissingCategories) {
        AppendMissingSpriteLibraryCategories(
          categoryPlansByName,
          seenCategoryNames,
          rewritten,
          lineEnding,
          ref libraryChanged,
          ref libraryMatchedCategoryCount,
          ref libraryCreatedCategories,
          ref libraryCreatedLabels);
      }
    }

    if (!libraryChanged) return true;

    try {
      File.WriteAllText(libraryFullPath, rewritten.ToString());
      return true;
    }
    catch (Exception ex) {
      error = ex.Message;
      return false;
    }
  }

  static void RewriteSpriteLibraryEntryBlock(
    string[] lines,
    int startIndex,
    int endIndex,
    Dictionary<string, SpriteAssetReference> replacementsByLabel,
    RebindLabelCleanupPlan cleanupPlan,
    bool deleteGearSkinLabels,
    HashSet<string> retainedLabels,
    System.Text.StringBuilder output,
    string lineEnding,
    ref bool libraryChanged,
    ref int libraryReboundEntries,
    ref int libraryDeletedLabels,
    ref int libraryUnchangedEntries) {
    if (lines == null || output == null || startIndex < 0 || endIndex <= startIndex) return;

    var entryLine = lines[startIndex];
    var entryName = entryLine.StartsWith("    - m_Name: ", StringComparison.Ordinal)
      ? entryLine.Substring("    - m_Name: ".Length).Trim()
      : "";

    if (ShouldDeleteGearSkinLabel(deleteGearSkinLabels, entryName)) {
      libraryChanged = true;
      libraryDeletedLabels++;
      return;
    }

    if (!TryResolveLabelReplacement(replacementsByLabel, entryName, out var replacementSprite) || !replacementSprite.IsValid) {
      if (!ShouldDeleteMissingRebindLabel(cleanupPlan, entryName)) {
        retainedLabels?.Add(entryName);
        AppendLineRange(output, lines, startIndex, endIndex, lineEnding);
        return;
      }

      libraryChanged = true;
      libraryDeletedLabels++;
      return;
    }

    var entryChanged = false;
    var sawSprite = false;
    var sawSpriteOverride = false;
    for (var lineIndex = startIndex; lineIndex < endIndex; lineIndex++) {
      var line = lines[lineIndex];
      if (line.StartsWith("      m_Sprite: ", StringComparison.Ordinal)) {
        var rewrittenLine = BuildSpriteLibrarySpriteReferenceLine("m_Sprite", replacementSprite);
        if (!string.Equals(line, rewrittenLine, StringComparison.Ordinal)) {
          entryChanged = true;
          line = rewrittenLine;
        }

        sawSprite = true;
      }
      else if (line.StartsWith("      m_SpriteOverride: ", StringComparison.Ordinal)) {
        var rewrittenLine = BuildSpriteLibrarySpriteReferenceLine("m_SpriteOverride", replacementSprite);
        if (!string.Equals(line, rewrittenLine, StringComparison.Ordinal)) {
          entryChanged = true;
          line = rewrittenLine;
        }

        sawSpriteOverride = true;
      }

      AppendLine(output, line, lineEnding);
    }

    retainedLabels?.Add(entryName);
    if (entryChanged || !sawSprite || !sawSpriteOverride) {
      libraryChanged = true;
      libraryReboundEntries++;
      return;
    }

    libraryUnchangedEntries++;
  }

  static int FindSpriteLibraryEntryBlockEnd(string[] lines, int startIndex) {
    if (lines == null) return startIndex;

    for (var lineIndex = startIndex; lineIndex < lines.Length; lineIndex++) {
      var line = lines[lineIndex];
      if (line.StartsWith("    - m_Name: ", StringComparison.Ordinal) ||
          line.StartsWith("  - m_Name: ", StringComparison.Ordinal)) {
        return lineIndex;
      }
    }

    return lines.Length;
  }

  static void AppendLineRange(System.Text.StringBuilder output, string[] lines, int startIndex, int endIndex, string lineEnding) {
    if (output == null || lines == null) return;
    for (var lineIndex = startIndex; lineIndex < endIndex && lineIndex < lines.Length; lineIndex++) {
      AppendLine(output, lines[lineIndex], lineEnding);
    }
  }

  static void AppendLine(System.Text.StringBuilder output, string line, string lineEnding) {
    if (output == null) return;
    output.Append(line ?? "");
    output.Append(lineEnding);
  }

  static string[] SplitTextIntoLines(string text) {
    if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

    var normalizedText = text
      .Replace("\r\n", "\n")
      .Replace('\r', '\n');
    if (normalizedText.EndsWith("\n", StringComparison.Ordinal)) {
      normalizedText = normalizedText.Substring(0, normalizedText.Length - 1);
    }

    return normalizedText.Length > 0
      ? normalizedText.Split('\n')
      : Array.Empty<string>();
  }

  static Dictionary<string, SpriteLibraryCategoryPlan> BuildSpriteLibraryCategoryPlans(
    GroupedSpriteReplacementIndex replacementIndex,
    bool isNormalLibrary,
    bool isSkinLibrary,
    string partCode) {
    var plansByName = new Dictionary<string, SpriteLibraryCategoryPlan>(StringComparer.OrdinalIgnoreCase);
    if (replacementIndex == null || replacementIndex.labelsByScope.Count <= 0 || string.IsNullOrWhiteSpace(partCode)) {
      return plansByName;
    }

    foreach (var pair in replacementIndex.labelsByScope) {
      var scopeKey = pair.Key;
      if (scopeKey.isNormal != isNormalLibrary ||
          scopeKey.isSkinLibrary != isSkinLibrary ||
          !string.Equals(scopeKey.partCode, partCode, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      var replacementsByLabel = pair.Value;
      if (replacementsByLabel == null || replacementsByLabel.Count <= 0) continue;

      var categoryPlan = new SpriteLibraryCategoryPlan {
        replacementsByLabel = replacementsByLabel
      };
      replacementIndex.cleanupByScope.TryGetValue(scopeKey, out var cleanupPlan);
      categoryPlan.cleanupPlan = cleanupPlan;
      plansByName[scopeKey.category] = categoryPlan;
    }

    return plansByName;
  }

  static HashSet<string> CollectSpriteLibraryCategoryNames(string[] lines) {
    var categoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (lines == null || lines.Length <= 0) return categoryNames;

    for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++) {
      var line = lines[lineIndex];
      if (!line.StartsWith("  - m_Name: ", StringComparison.Ordinal)) continue;
      var categoryName = line.Substring("  - m_Name: ".Length).Trim();
      if (string.IsNullOrWhiteSpace(categoryName)) continue;
      categoryNames.Add(categoryName);
    }

    return categoryNames;
  }

  static bool ContainsEquivalentSpriteLibraryCategory(HashSet<string> existingCategoryNames, string categoryName) {
    if (existingCategoryNames == null || existingCategoryNames.Count <= 0 || string.IsNullOrWhiteSpace(categoryName)) {
      return false;
    }

    foreach (var existingCategoryName in existingCategoryNames) {
      if (string.Equals(existingCategoryName, categoryName, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  static bool TryResolveSpriteLibraryCategoryPlan(
    Dictionary<string, SpriteLibraryCategoryPlan> categoryPlansByName,
    string categoryName,
    out string resolvedCategoryName,
    out SpriteLibraryCategoryPlan plan) {
    resolvedCategoryName = categoryName ?? "";
    plan = null;
    if (categoryPlansByName == null || categoryPlansByName.Count <= 0 || string.IsNullOrWhiteSpace(categoryName)) {
      return false;
    }

    if (categoryPlansByName.TryGetValue(categoryName, out plan) && plan != null) {
      resolvedCategoryName = categoryName;
      return true;
    }

    var normalizedCategoryName = NormalizeSpriteLibraryCategoryName(categoryName);
    if (string.IsNullOrWhiteSpace(normalizedCategoryName)) return false;

    foreach (var pair in categoryPlansByName) {
      if (pair.Value == null) continue;
      if (!string.Equals(
            NormalizeSpriteLibraryCategoryName(pair.Key),
            normalizedCategoryName,
            StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      resolvedCategoryName = pair.Key;
      plan = pair.Value;
      return true;
    }

    return false;
  }

  static string NormalizeSpriteLibraryCategoryName(string categoryName) {
    if (string.IsNullOrWhiteSpace(categoryName)) return "";

    var normalizedCategoryName = categoryName.Trim();
    if (string.Equals(normalizedCategoryName, "SuperBlast", StringComparison.OrdinalIgnoreCase)) {
      return "Blast";
    }

    return normalizedCategoryName;
  }

  static void FinalizeSpriteLibraryCategoryRewrite(
    SpriteLibraryCategoryPlan categoryPlan,
    HashSet<string> retainedLabels,
    bool sawOverrideEntries,
    System.Text.StringBuilder output,
    string lineEnding,
    ref bool libraryChanged,
    ref int libraryCreatedLabels) {
    if (categoryPlan == null || output == null) return;

    var createdLabelCount = AppendMissingSpriteLibraryEntries(
      categoryPlan.replacementsByLabel,
      retainedLabels,
      output,
      lineEnding,
      !sawOverrideEntries);
    if (createdLabelCount <= 0) return;

    libraryChanged = true;
    libraryCreatedLabels += createdLabelCount;
  }

  static void AppendMissingSpriteLibraryCategories(
    Dictionary<string, SpriteLibraryCategoryPlan> categoryPlansByName,
    HashSet<string> seenCategoryNames,
    System.Text.StringBuilder output,
    string lineEnding,
    ref bool libraryChanged,
    ref int libraryMatchedCategoryCount,
    ref int libraryCreatedCategories,
    ref int libraryCreatedLabels) {
    if (categoryPlansByName == null || categoryPlansByName.Count <= 0 || output == null) return;

    var missingCategoryNames = new List<string>();
    foreach (var pair in categoryPlansByName) {
      if (pair.Value?.replacementsByLabel == null || pair.Value.replacementsByLabel.Count <= 0) continue;
      if (seenCategoryNames != null && seenCategoryNames.Contains(pair.Key)) continue;
      missingCategoryNames.Add(pair.Key);
    }

    missingCategoryNames.Sort(CompareSpriteLibraryNames);
    for (var categoryIndex = 0; categoryIndex < missingCategoryNames.Count; categoryIndex++) {
      var categoryName = missingCategoryNames[categoryIndex];
      if (!categoryPlansByName.TryGetValue(categoryName, out var categoryPlan) ||
          categoryPlan?.replacementsByLabel == null ||
          categoryPlan.replacementsByLabel.Count <= 0) {
        continue;
      }

      AppendSpriteLibraryCategoryBlock(
        output,
        categoryName,
        categoryPlan.replacementsByLabel,
        lineEnding,
        ref libraryCreatedLabels);
      libraryChanged = true;
      libraryMatchedCategoryCount++;
      libraryCreatedCategories++;
    }
  }

  static void AppendSpriteLibraryCategoryBlock(
    System.Text.StringBuilder output,
    string categoryName,
    Dictionary<string, SpriteAssetReference> replacementsByLabel,
    string lineEnding,
    ref int createdLabels) {
    if (output == null ||
        string.IsNullOrWhiteSpace(categoryName) ||
        replacementsByLabel == null ||
        replacementsByLabel.Count <= 0) {
      return;
    }

    AppendLine(output, "  - m_Name: " + categoryName, lineEnding);
    AppendLine(
      output,
      "    m_Hash: " + GetSpriteLibraryStringHash(categoryName).ToString(CultureInfo.InvariantCulture),
      lineEnding);
    AppendLine(output, "    m_CategoryList: []", lineEnding);
    AppendLine(output, "    m_OverrideEntries:", lineEnding);
    createdLabels += AppendMissingSpriteLibraryEntries(
      replacementsByLabel,
      null,
      output,
      lineEnding,
      false);
  }

  static int AppendMissingSpriteLibraryEntries(
    Dictionary<string, SpriteAssetReference> replacementsByLabel,
    HashSet<string> existingLabels,
    System.Text.StringBuilder output,
    string lineEnding,
    bool includeOverrideEntriesHeader) {
    if (replacementsByLabel == null || replacementsByLabel.Count <= 0 || output == null) return 0;

    var missingLabels = CollectMissingSpriteLibraryLabels(replacementsByLabel, existingLabels);
    if (missingLabels.Count <= 0) return 0;

    if (includeOverrideEntriesHeader) {
      AppendLine(output, "    m_OverrideEntries:", lineEnding);
    }

    for (var labelIndex = 0; labelIndex < missingLabels.Count; labelIndex++) {
      var label = missingLabels[labelIndex];
      if (!replacementsByLabel.TryGetValue(label, out var replacementSprite) || !replacementSprite.IsValid) continue;
      AppendSpriteLibraryEntryBlock(output, label, replacementSprite, lineEnding);
    }

    return missingLabels.Count;
  }

  static List<string> CollectMissingSpriteLibraryLabels(
    Dictionary<string, SpriteAssetReference> replacementsByLabel,
    HashSet<string> existingLabels) {
    var missingLabels = new List<string>();
    if (replacementsByLabel == null || replacementsByLabel.Count <= 0) return missingLabels;

    foreach (var pair in replacementsByLabel) {
      if (!pair.Value.IsValid) continue;
      if (ContainsEquivalentSpriteLibraryLabel(existingLabels, pair.Key)) continue;
      missingLabels.Add(pair.Key);
    }

    missingLabels.Sort(CompareSpriteLibraryNames);
    return missingLabels;
  }

  static bool ContainsEquivalentSpriteLibraryLabel(HashSet<string> existingLabels, string label) {
    if (existingLabels == null || existingLabels.Count <= 0 || string.IsNullOrWhiteSpace(label)) return false;
    if (existingLabels.Contains(label)) return true;

    foreach (var existingLabel in existingLabels) {
      if (SpriteSliceAddressUtility.HasEquivalentNumericLabel(existingLabel, label)) {
        return true;
      }
    }

    return false;
  }

  static void AppendSpriteLibraryEntryBlock(
    System.Text.StringBuilder output,
    string entryName,
    SpriteAssetReference replacementSprite,
    string lineEnding) {
    if (output == null || string.IsNullOrWhiteSpace(entryName) || !replacementSprite.IsValid) return;

    AppendLine(output, "    - m_Name: " + entryName, lineEnding);
    AppendLine(
      output,
      "      m_Hash: " + GetSpriteLibraryStringHash(entryName).ToString(CultureInfo.InvariantCulture),
      lineEnding);
    AppendLine(output, BuildSpriteLibrarySpriteReferenceLine("m_Sprite", replacementSprite), lineEnding);
    AppendLine(output, "      m_FromMain: 0", lineEnding);
    AppendLine(output, BuildSpriteLibrarySpriteReferenceLine("m_SpriteOverride", replacementSprite), lineEnding);
  }

  static bool IsSpriteLibraryListBoundary(string line) {
    if (string.IsNullOrEmpty(line)) return false;

    return line.StartsWith("  ", StringComparison.Ordinal) &&
           !line.StartsWith("  - ", StringComparison.Ordinal) &&
           !line.StartsWith("    ", StringComparison.Ordinal);
  }

  static bool ShouldDeleteGearSkinLabel(bool deleteGearSkinLabels, string entryName) {
    return deleteGearSkinLabels &&
           !string.IsNullOrWhiteSpace(entryName) &&
           entryName.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  static int CompareSpriteLibraryNames(string left, string right) {
    var normalizedLeft = left ?? "";
    var normalizedRight = right ?? "";
    var naturalCompare = SpriteSliceAddressUtility.CompareNaturally(normalizedLeft, normalizedRight);
    if (naturalCompare != 0) return naturalCompare;

    return StringComparer.OrdinalIgnoreCase.Compare(normalizedLeft, normalizedRight);
  }

  static int GetSpriteLibraryStringHash(string value) {
    const int bit30Mask = 0x3FFFFFFF;
    return Animator.StringToHash(value ?? "") & bit30Mask;
  }

  static string BuildSpriteLibrarySpriteReferenceLine(string propertyName, SpriteAssetReference spriteReference) {
    return "      " + propertyName + ": {fileID: " +
           spriteReference.localFileId.ToString(CultureInfo.InvariantCulture) +
           ", guid: " + spriteReference.guid +
           ", type: 3}";
  }

  static bool TryParseSpriteLibraryDescriptor(string libraryPath, out string partCode, out bool isNormalLibrary, out bool isSkinLibrary) {
    partCode = "";
    isNormalLibrary = false;
    isSkinLibrary = false;

    var fileName = Path.GetFileNameWithoutExtension(libraryPath ?? "");
    if (string.IsNullOrWhiteSpace(fileName)) return false;

    isNormalLibrary = fileName.EndsWith("N", StringComparison.OrdinalIgnoreCase);
    var coreName = isNormalLibrary ? fileName.Substring(0, fileName.Length - 1) : fileName;
    string token;
    if (coreName.StartsWith("Skin", StringComparison.OrdinalIgnoreCase)) {
      isSkinLibrary = true;
      token = coreName.Substring("Skin".Length);
    }
    else if (coreName.StartsWith("Gear", StringComparison.OrdinalIgnoreCase)) {
      token = coreName.Substring("Gear".Length);
    }
    else {
      return false;
    }

    partCode = ResolvePartCode(token);
    return !string.IsNullOrWhiteSpace(partCode);
  }

  static bool TryResolveLabelReplacement(Dictionary<string, SpriteAssetReference> replacementsByLabel, string label, out SpriteAssetReference replacementSprite) {
    replacementSprite = default;
    var normalizedLabel = label ?? "";
    if (string.IsNullOrWhiteSpace(normalizedLabel) || replacementsByLabel == null || replacementsByLabel.Count <= 0) {
      return false;
    }

    if (replacementsByLabel.TryGetValue(normalizedLabel, out replacementSprite) && replacementSprite.IsValid) {
      return true;
    }

    foreach (var pair in replacementsByLabel) {
      if (!SpriteSliceAddressUtility.HasEquivalentNumericLabel(pair.Key, normalizedLabel)) continue;
      replacementSprite = pair.Value;
      return replacementSprite.IsValid;
    }

    return false;
  }

  static int ComparePendingGroupedSpriteReplacements(PendingGroupedSpriteReplacement left, PendingGroupedSpriteReplacement right) {
    var sourceAtlasCompare = SpriteSliceAddressUtility.CompareNaturally(left?.sourceAtlasAssetPath, right?.sourceAtlasAssetPath);
    if (sourceAtlasCompare != 0) return sourceAtlasCompare;

    var sourceSpriteCompare = SpriteSliceAddressUtility.CompareNaturally(left?.sourceSpriteName, right?.sourceSpriteName);
    if (sourceSpriteCompare != 0) return sourceSpriteCompare;

    var groupedNameCompare = SpriteSliceAddressUtility.CompareNaturally(left?.groupedSpriteName, right?.groupedSpriteName);
    if (groupedNameCompare != 0) return groupedNameCompare;

    return SpriteSliceAddressUtility.CompareNaturally(left?.atlasAssetPath, right?.atlasAssetPath);
  }

  static List<PendingGroupedSpriteReplacement> ExpandPendingGroupedSpriteSequenceBySourceSlices(
    List<PendingGroupedSpriteReplacement> pendingGroup,
    GroupedSpriteReplacementIndex replacementIndex) {
    var expandedSequence = new List<PendingGroupedSpriteReplacement>();
    if (pendingGroup == null || pendingGroup.Count <= 0) return expandedSequence;

    for (var pendingIndex = 0; pendingIndex < pendingGroup.Count; pendingIndex++) {
      var current = pendingGroup[pendingIndex];
      if (current == null) continue;

      expandedSequence.Add(current);
      if (pendingIndex >= pendingGroup.Count - 1) continue;

      var next = pendingGroup[pendingIndex + 1];
      if (!TryBuildPendingGroupedSpriteGapRange(current, next, out var gapStartInclusive, out var gapEndInclusive)) {
        continue;
      }

      for (var missingSliceNumber = gapStartInclusive; missingSliceNumber <= gapEndInclusive; missingSliceNumber++) {
        expandedSequence.Add(BuildFilledSliceGapReplacement(current, next, missingSliceNumber));
        if (replacementIndex != null) {
          replacementIndex.filledSliceGapCount++;
        }
      }
    }

    return expandedSequence;
  }

  static bool TryBuildPendingGroupedSpriteGapRange(
    PendingGroupedSpriteReplacement current,
    PendingGroupedSpriteReplacement next,
    out int gapStartInclusive,
    out int gapEndInclusive) {
    gapStartInclusive = 0;
    gapEndInclusive = -1;
    if (current == null || next == null) return false;
    if (!string.Equals(current.sourceAtlasAssetPath, next.sourceAtlasAssetPath, StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    if (!TryExtractPendingGroupedSpriteSliceNumber(current, out var currentSliceNumber) ||
        !TryExtractPendingGroupedSpriteSliceNumber(next, out var nextSliceNumber)) {
      return false;
    }

    if (nextSliceNumber <= currentSliceNumber + 1) return false;
    gapStartInclusive = currentSliceNumber + 1;
    gapEndInclusive = nextSliceNumber - 1;
    return true;
  }

  static PendingGroupedSpriteReplacement BuildFilledSliceGapReplacement(
    PendingGroupedSpriteReplacement left,
    PendingGroupedSpriteReplacement right,
    int missingSliceNumber) {
    if (left == null) return right;
    if (right == null) return left;

    if (!TryExtractPendingGroupedSpriteSliceNumber(left, out var leftSliceNumber) ||
        !TryExtractPendingGroupedSpriteSliceNumber(right, out var rightSliceNumber)) {
      return left;
    }

    var distanceToLeft = Math.Abs(missingSliceNumber - leftSliceNumber);
    var distanceToRight = Math.Abs(rightSliceNumber - missingSliceNumber);
    return distanceToLeft <= distanceToRight ? left : right;
  }

  static bool TryExtractPendingGroupedSpriteSliceNumber(PendingGroupedSpriteReplacement pendingReplacement, out int sliceNumber) {
    sliceNumber = 0;
    if (pendingReplacement == null) return false;
    if (!SpriteSliceAddressUtility.TryExtractNumericLabelValue(pendingReplacement.sourceSpriteName, out var numericLabelValue)) {
      return false;
    }

    return int.TryParse(numericLabelValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out sliceNumber) &&
           sliceNumber > 0;
  }

  static string ResolveGroupedSpriteSourceSortName(GroupedAtlasSpriteMetadata sprite) {
    var sourceSpriteName = sprite?.sourceSpriteName ?? "";
    if (!string.IsNullOrWhiteSpace(sourceSpriteName)) {
      return sourceSpriteName.Trim();
    }

    return TryExtractSourceSpriteName(sprite?.name, out sourceSpriteName)
      ? sourceSpriteName.Trim()
      : (sprite?.name ?? "").Trim();
  }

  static List<PackedSpriteBuildItem> BuildGroupedPackSequence(IEnumerable<PackedSpriteBuildItem> items) {
    if (items == null) return new List<PackedSpriteBuildItem>();

    var ordered = new List<PackedSpriteBuildItem>();
    foreach (var item in items) {
      if (item == null) continue;
      ordered.Add(item);
    }

    return ordered;
  }

  static string BuildLibraryEntryLabel(GroupedAtlasMetadataPayload payload, GroupedAtlasSpriteMetadata sprite) {
    var sourceSpriteName = sprite?.sourceSpriteName ?? "";
    if (string.IsNullOrWhiteSpace(sourceSpriteName) && !TryExtractSourceSpriteName(sprite?.name, out sourceSpriteName)) {
      return "";
    }

    sourceSpriteName = sourceSpriteName.Trim();
    if (string.IsNullOrWhiteSpace(sourceSpriteName)) return "";

    if (IsSkinGroupKey(payload?.groupKey)) {
      if (SpriteSliceAddressUtility.TryExtractNumericLabelValue(sourceSpriteName, out var numericSkinLabel)) {
        return numericSkinLabel;
      }

      return sourceSpriteName;
    }

    var gearLabelPrefix = BuildGearLabelPrefix(payload?.form, payload?.variant);
    if (string.IsNullOrWhiteSpace(gearLabelPrefix)) {
      return sourceSpriteName;
    }

    if (sourceSpriteName.StartsWith(gearLabelPrefix + "_", StringComparison.OrdinalIgnoreCase)) {
      return sourceSpriteName;
    }

    if (SpriteSliceAddressUtility.TryExtractNumericLabelValue(sourceSpriteName, out var numericGearLabel)) {
      return gearLabelPrefix + "_" + numericGearLabel;
    }

    return gearLabelPrefix + "_" + sourceSpriteName;
  }

  static string BuildGearLabelPrefix(string form, string variant) {
    var normalizedForm = (form ?? "").Trim();
    var normalizedVariant = (variant ?? "").Trim();
    if (string.IsNullOrWhiteSpace(normalizedForm) || string.IsNullOrWhiteSpace(normalizedVariant)) {
      return "";
    }

    return normalizedForm + "_" + normalizedVariant;
  }

  static bool TryExtractPartCode(string groupedSpriteName, out string partCode) {
    partCode = "";
    if (string.IsNullOrWhiteSpace(groupedSpriteName)) return false;

    var separatorIndex = groupedSpriteName.IndexOf("__", StringComparison.Ordinal);
    if (separatorIndex <= 0) return false;
    partCode = groupedSpriteName.Substring(0, separatorIndex).Trim();
    return !string.IsNullOrWhiteSpace(partCode);
  }

  static bool TryExtractSourceSpriteName(string groupedSpriteName, out string sourceSpriteName) {
    sourceSpriteName = "";
    if (string.IsNullOrWhiteSpace(groupedSpriteName)) return false;

    var separatorIndex = groupedSpriteName.IndexOf("__", StringComparison.Ordinal);
    if (separatorIndex < 0 || separatorIndex >= groupedSpriteName.Length - 2) return false;
    sourceSpriteName = groupedSpriteName.Substring(separatorIndex + 2).Trim();
    return !string.IsNullOrWhiteSpace(sourceSpriteName);
  }

  static Dictionary<string, SpriteAssetReference> BuildSpriteReferenceLookupByName(string atlasAssetPath) {
    var result = new Dictionary<string, SpriteAssetReference>(StringComparer.Ordinal);
    var sprites = AssetDatabase.LoadAllAssetsAtPath(atlasAssetPath).OfType<Sprite>();
    foreach (var sprite in sprites) {
      if (sprite == null || string.IsNullOrWhiteSpace(sprite.name)) continue;
      if (!TryGetSpriteAssetReference(sprite, out var spriteReference)) continue;
      result[sprite.name] = spriteReference;
    }

    return result;
  }

  static bool TryGetSpriteAssetReference(Sprite sprite, out SpriteAssetReference spriteReference) {
    spriteReference = default;
    if (sprite == null) return false;
    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out var guid, out long localFileId)) {
      return false;
    }

    spriteReference = new SpriteAssetReference(
      guid,
      localFileId,
      NormalizePath(AssetDatabase.GetAssetPath(sprite)),
      sprite.name);
    return spriteReference.IsValid;
  }

  static bool TryBuildCleanupPlanKey(string atlasAssetPath, out string folderPath, out string filePrefix) {
    folderPath = NormalizePath(Path.GetDirectoryName(atlasAssetPath));
    filePrefix = "";

    var fileName = Path.GetFileNameWithoutExtension(atlasAssetPath ?? "");
    if (string.IsNullOrWhiteSpace(fileName)) return false;
    if (fileName.EndsWith("_N", StringComparison.OrdinalIgnoreCase)) {
      fileName = fileName.Substring(0, fileName.Length - 2);
    }

    var pageMarkerIndex = fileName.LastIndexOf("_p", StringComparison.OrdinalIgnoreCase);
    if (pageMarkerIndex <= 0 || pageMarkerIndex >= fileName.Length - 2) return false;
    if (!int.TryParse(fileName.Substring(pageMarkerIndex + 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) {
      return false;
    }

    filePrefix = fileName.Substring(0, pageMarkerIndex);
    return !string.IsNullOrWhiteSpace(folderPath) && !string.IsNullOrWhiteSpace(filePrefix);
  }

  int CleanupStaleOutputs(List<CleanupPlan> cleanupPlans) {
    if (cleanupPlans == null || cleanupPlans.Count <= 0) return 0;

    var deletedAssets = 0;
    for (var i = 0; i < cleanupPlans.Count; i++) {
      var plan = cleanupPlans[i];
      if (plan == null || string.IsNullOrWhiteSpace(plan.folderPath) || string.IsNullOrWhiteSpace(plan.filePrefix)) continue;

      var fullFolderPath = Path.GetFullPath(plan.folderPath);
      if (!Directory.Exists(fullFolderPath)) continue;

      var files = Directory.GetFiles(fullFolderPath, "*", SearchOption.TopDirectoryOnly);
      for (var fileIndex = 0; fileIndex < files.Length; fileIndex++) {
        if (!TryConvertFullPathToAssetPath(files[fileIndex], out var assetPath)) continue;
        var extension = Path.GetExtension(assetPath);
        if (!IsCleanupCandidateExtension(extension)) continue;

        var fileName = Path.GetFileNameWithoutExtension(assetPath);
        if (!fileName.StartsWith(plan.filePrefix + "_p", StringComparison.OrdinalIgnoreCase)) continue;
        if (plan.keepAssetPaths.Contains(assetPath)) continue;
        if (!AssetDatabase.DeleteAsset(assetPath)) continue;
        deletedAssets++;
      }
    }

    return deletedAssets;
  }

  void CleanupStaleCandidateOutputs(string outputFolderPath, GroupCandidate candidate, List<AtlasPage> pages, bool includeNormalAtlases) {
    if (string.IsNullOrWhiteSpace(outputFolderPath) || candidate == null || pages == null) return;

    var cleanupPlan = new CleanupPlan {
      folderPath = outputFolderPath,
      filePrefix = BuildOutputFilePrefix(candidate),
      isSkinLibrary = IsSkinCandidate(candidate)
    };

    for (var i = 0; i < pages.Count; i++) {
      var page = pages[i];
      if (page == null) continue;

      if (!string.IsNullOrWhiteSpace(page.colorAtlasPath)) {
        cleanupPlan.keepAssetPaths.Add(page.colorAtlasPath);
        AddMetadataAssetPaths(cleanupPlan.keepAssetPaths, page.colorAtlasPath);
      }

      if (!includeNormalAtlases || string.IsNullOrWhiteSpace(page.normalAtlasPath)) continue;
      cleanupPlan.keepAssetPaths.Add(page.normalAtlasPath);
      AddMetadataAssetPaths(cleanupPlan.keepAssetPaths, page.normalAtlasPath);
    }

    var deletedCount = CleanupStaleOutputs(new List<CleanupPlan> { cleanupPlan });
    if (deletedCount > 0) {
      Debug.Log(
        "[GearGroupAtlas] Deleted stale output assets before overwrite." +
        " group='" + BuildCandidateLabel(candidate) + "'" +
        " deleted_assets=" + deletedCount);
    }
  }

  static bool IsCleanupCandidateExtension(string extension) {
    return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);
  }

  static bool IsSupportedCandidateRelativePath(string sourceFolderPath, string assetPath) {
    return TryGetSourceRelativeDirectorySegments(sourceFolderPath, assetPath, out var relativeSegments, out _) &&
           relativeSegments.Length >= 3;
  }

  List<GroupCandidate> CollectGroupCandidates(string sourceFolderPath, string sanitizedOutputSubfolder) {
    var candidatesByKey = new Dictionary<string, GroupCandidate>(StringComparer.OrdinalIgnoreCase);
    var outputSkippedCount = 0;
    var ignoredFolderSkippedCount = 0;
    var shallowSkippedCount = 0;
    var parseRejectedCount = 0;

    var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { sourceFolderPath });
    for (var i = 0; i < textureGuids.Length; i++) {
      var assetPath = NormalizePath(AssetDatabase.GUIDToAssetPath(textureGuids[i]));
      if (!IsSupportedColorAtlas(assetPath)) continue;
      if (IsGeneratedNormalAtlasAssetPath(assetPath)) continue;
      if (SpriteAtlasSourceFilter.HasIgnoredFolderInPath(assetPath)) {
        ignoredFolderSkippedCount++;
        continue;
      }
      if (ShouldSkipOutputAsset(assetPath, sanitizedOutputSubfolder)) {
        outputSkippedCount++;
        continue;
      }

      if (!IsSupportedCandidateRelativePath(sourceFolderPath, assetPath)) {
        shallowSkippedCount++;
        continue;
      }

      if (!TryParseSourceAtlasPath(sourceFolderPath, assetPath, out var category, out var form, out var variant, out var partCode, out var fileBase, out var isSkin)) {
        parseRejectedCount++;
        if (parseRejectedCount <= 10) {
          Debug.LogWarning("[GearGroupAtlas] Rejected candidate path. asset='" + assetPath + "'");
        }
        continue;
      }

      var record = new SourceAtlasRecord {
        category = category,
        form = form,
        variant = variant,
        partCode = partCode,
        atlasPath = assetPath,
        normalAtlasPath = "",
        fileBase = fileBase
      };

      var candidateKey = BuildCandidateKey(record, isSkin);
      if (!candidatesByKey.TryGetValue(candidateKey, out var candidate) || candidate == null) {
        candidate = new GroupCandidate {
          form = form,
          variant = variant,
          partCode = partCode,
          isSkin = isSkin
        };
        candidatesByKey[candidateKey] = candidate;
      }

      candidate.sourceAtlases.Add(record);
    }

    var candidates = candidatesByKey.Values.ToList();
    for (var i = 0; i < candidates.Count; i++) {
      var candidate = candidates[i];
      FinalizeCandidate(candidate);
    }

    candidates.Sort(CompareCandidates);
    Debug.Log(
      "[GearGroupAtlas] Candidate path scan." +
      " source='" + sourceFolderPath + "'" +
      " textures=" + textureGuids.Length +
      " ignored_folder_skipped=" + ignoredFolderSkippedCount +
      " ignored_folders='" + SpriteAtlasSourceFilter.IgnoredFolderSummary + "'" +
      " output_skipped=" + outputSkippedCount +
      " shallow_skipped=" + shallowSkippedCount +
      " parse_rejected=" + parseRejectedCount +
      " matched=" + candidates.Sum(candidate => candidate?.sourceAtlases?.Count ?? 0));
    return candidates;
  }

  static int CompareCandidates(GroupCandidate left, GroupCandidate right) {
    var formCompare = SpriteSliceAddressUtility.CompareNaturally(left?.form, right?.form);
    if (formCompare != 0) return formCompare;

    var variantCompare = SpriteSliceAddressUtility.CompareNaturally(left?.variant, right?.variant);
    if (variantCompare != 0) return variantCompare;

    var partCompare = SpriteSliceAddressUtility.CompareNaturally(left?.partCode, right?.partCode);
    if (partCompare != 0) return partCompare;

    return SpriteSliceAddressUtility.CompareNaturally(BuildCandidateAnimationSummary(left), BuildCandidateAnimationSummary(right));
  }

  static string BuildCandidateKey(SourceAtlasRecord record, bool isSkin) {
    if (record == null) return "";
    if (isSkin) {
      return SkinGroupKey + "|" + (record.partCode ?? "");
    }

    return (record.form ?? "") + "|" + (record.variant ?? "") + "|" + (record.partCode ?? "");
  }

  static string BuildCandidateAnimationSummary(GroupCandidate candidate) {
    if (candidate?.sourceCategories == null || candidate.sourceCategories.Count <= 0) return "";
    return string.Join("|", candidate.sourceCategories);
  }

  static void FinalizeCandidate(GroupCandidate candidate) {
    if (candidate == null) return;

    if (candidate.sourceAtlases == null) {
      candidate.sourceAtlases = new List<SourceAtlasRecord>();
    }

    candidate.sourceAtlases = candidate.sourceAtlases
      .Where(record => record != null)
      .OrderBy(record => record.category, SpriteSliceAddressUtility.NaturalStringComparer)
      .ThenBy(record => record.fileBase, SpriteSliceAddressUtility.NaturalStringComparer)
      .ThenBy(record => record.atlasPath, SpriteSliceAddressUtility.NaturalStringComparer)
      .ToList();

    candidate.sourceCategories = candidate.sourceAtlases
      .Select(record => (record.category ?? "").Trim())
      .Where(category => !string.IsNullOrWhiteSpace(category))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(category => category, SpriteSliceAddressUtility.NaturalStringComparer)
      .ToList();

    candidate.normalAtlasCount = candidate.sourceAtlases.Count(record => !string.IsNullOrWhiteSpace(record.normalAtlasPath));
  }

  static string ResolvePartCode(string token) {
    if (string.IsNullOrWhiteSpace(token)) return "";
    if (PartCodeByToken.TryGetValue(token.Trim(), out var mappedPartCode) && !string.IsNullOrWhiteSpace(mappedPartCode)) {
      return mappedPartCode;
    }

    return token.Trim();
  }

  static bool IsKnownPartCodeToken(string token) {
    var resolvedPartCode = ResolvePartCode(token);
    if (string.IsNullOrWhiteSpace(resolvedPartCode)) return false;
    return PartCodeByToken.Values.Contains(resolvedPartCode, StringComparer.OrdinalIgnoreCase);
  }

  static bool TryParseWrappedDescriptorToken(string token, out string form, out string variant, out bool isSkin) {
    form = "";
    variant = "";
    isSkin = false;
    if (string.IsNullOrWhiteSpace(token)) return false;

    var normalizedToken = token.Trim();
    if (string.Equals(normalizedToken, SkinFormName, StringComparison.OrdinalIgnoreCase)) {
      form = SkinFormName;
      variant = SkinVariantName;
      isSkin = true;
      return true;
    }

    var separatorIndex = normalizedToken.IndexOf('_');
    if (separatorIndex <= 0 || separatorIndex >= normalizedToken.Length - 1) return false;

    form = normalizedToken.Substring(0, separatorIndex).Trim();
    variant = normalizedToken.Substring(separatorIndex + 1).Trim();
    return !string.IsNullOrWhiteSpace(form) && !string.IsNullOrWhiteSpace(variant);
  }

  static bool TryGetSourceRelativeDirectorySegments(
    string sourceFolderPath,
    string assetPath,
    out string[] relativeSegments,
    out string fileBase) {
    relativeSegments = Array.Empty<string>();
    fileBase = "";

    var normalizedSourceFolderPath = NormalizePath(sourceFolderPath).TrimEnd('/');
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedSourceFolderPath) || string.IsNullOrWhiteSpace(normalizedAssetPath)) return false;
    if (!normalizedAssetPath.StartsWith(normalizedSourceFolderPath + "/", StringComparison.OrdinalIgnoreCase)) return false;

    var relativePath = normalizedAssetPath.Substring(normalizedSourceFolderPath.Length + 1);
    var pathSegments = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    if (pathSegments.Length < 2) return false;

    fileBase = Path.GetFileNameWithoutExtension(pathSegments[pathSegments.Length - 1]);
    if (string.IsNullOrWhiteSpace(fileBase)) return false;

    relativeSegments = pathSegments
      .Take(pathSegments.Length - 1)
      .Select(segment => (segment ?? "").Trim())
      .Where(segment => !string.IsNullOrWhiteSpace(segment))
      .ToArray();
    return relativeSegments.Length > 0;
  }

  static bool TryResolveNearestCategoryToken(string[] relativeSegments, int clusterStartIndex, out string category) {
    category = "";
    if (relativeSegments == null || clusterStartIndex <= 0) return false;

    for (var i = clusterStartIndex - 1; i >= 0; i--) {
      var token = (relativeSegments[i] ?? "").Trim();
      if (string.IsNullOrWhiteSpace(token)) continue;
      category = token;
      return true;
    }

    return false;
  }

  static bool TryResolveAdjacentPartCode(string[] relativeSegments, int descriptorIndex, out int partIndex, out string partCode) {
    partIndex = -1;
    partCode = "";
    if (relativeSegments == null || descriptorIndex < 0 || descriptorIndex >= relativeSegments.Length) return false;

    var candidates = new List<(int index, string token)>();
    TryAddAdjacentPartCandidate(relativeSegments, descriptorIndex - 1, candidates);
    TryAddAdjacentPartCandidate(relativeSegments, descriptorIndex + 1, candidates);
    if (candidates.Count <= 0) return false;

    var orderedCandidates = candidates
      .OrderByDescending(candidate => IsKnownPartCodeToken(candidate.token))
      .ThenByDescending(candidate => candidate.index)
      .ToList();
    var resolvedPartCode = ResolvePartCode(orderedCandidates[0].token);
    if (string.IsNullOrWhiteSpace(resolvedPartCode)) return false;

    partIndex = orderedCandidates[0].index;
    partCode = resolvedPartCode;
    return true;
  }

  static void TryAddAdjacentPartCandidate(string[] relativeSegments, int index, List<(int index, string token)> candidates) {
    if (relativeSegments == null || candidates == null) return;
    if (index < 0 || index >= relativeSegments.Length) return;

    var token = (relativeSegments[index] ?? "").Trim();
    if (string.IsNullOrWhiteSpace(token)) return;
    if (TryParseWrappedDescriptorToken(token, out _, out _, out _)) return;
    candidates.Add((index, token));
  }

  static bool TryParseWrappedDescriptorSourceAtlasPath(
    string[] relativeSegments,
    out string category,
    out string form,
    out string variant,
    out string partCode,
    out bool isSkin) {
    category = "";
    form = "";
    variant = "";
    partCode = "";
    isSkin = false;
    if (relativeSegments == null || relativeSegments.Length < 3) return false;

    for (var descriptorIndex = relativeSegments.Length - 1; descriptorIndex >= 0; descriptorIndex--) {
      if (!TryParseWrappedDescriptorToken(relativeSegments[descriptorIndex], out form, out variant, out isSkin)) continue;
      if (!TryResolveAdjacentPartCode(relativeSegments, descriptorIndex, out var partIndex, out partCode)) continue;
      if (!TryResolveNearestCategoryToken(relativeSegments, Math.Min(descriptorIndex, partIndex), out category)) continue;
      return !string.IsNullOrWhiteSpace(category) &&
             !string.IsNullOrWhiteSpace(form) &&
             !string.IsNullOrWhiteSpace(variant) &&
             !string.IsNullOrWhiteSpace(partCode);
    }

    return false;
  }

  static bool TryParseTo2TransitionSourceAtlasPath(
    string[] relativeSegments,
    out string category,
    out string form,
    out string variant,
    out string partCode,
    out bool isSkin) {
    category = "";
    form = "";
    variant = "";
    partCode = "";
    isSkin = false;
    if (relativeSegments == null || relativeSegments.Length < 3 || relativeSegments.Length > 4) return false;

    var leadingCategory = (relativeSegments[0] ?? "").Trim();
    if (!string.Equals(leadingCategory, "To2", StringComparison.OrdinalIgnoreCase)) return false;
    if (!TryParseWrappedDescriptorToken(relativeSegments[relativeSegments.Length - 1], out form, out variant, out isSkin) || isSkin) {
      return false;
    }

    partCode = ResolvePartCode(relativeSegments[relativeSegments.Length - 2]);
    if (string.IsNullOrWhiteSpace(partCode)) return false;

    category = "To2";
    return !string.IsNullOrWhiteSpace(form) && !string.IsNullOrWhiteSpace(variant);
  }

  static bool TryParseDirectGearSourceAtlasPath(
    string[] relativeSegments,
    out string category,
    out string form,
    out string variant,
    out string partCode,
    out bool isSkin) {
    category = "";
    form = "";
    variant = "";
    partCode = "";
    isSkin = false;
    if (relativeSegments == null || relativeSegments.Length < 4) return false;

    partCode = ResolvePartCode(relativeSegments[relativeSegments.Length - 1]);
    variant = (relativeSegments[relativeSegments.Length - 2] ?? "").Trim();
    form = (relativeSegments[relativeSegments.Length - 3] ?? "").Trim();
    if (string.IsNullOrWhiteSpace(partCode) ||
        string.IsNullOrWhiteSpace(form) ||
        string.IsNullOrWhiteSpace(variant) ||
        TryParseWrappedDescriptorToken(form, out _, out _, out _)) {
      return false;
    }

    if (!TryResolveNearestCategoryToken(relativeSegments, relativeSegments.Length - 3, out category)) {
      return false;
    }

    return !string.IsNullOrWhiteSpace(category);
  }

  static bool TryParseImplicitSkinSourceAtlasPath(
    string[] relativeSegments,
    out string category,
    out string form,
    out string variant,
    out string partCode,
    out bool isSkin) {
    category = "";
    form = "";
    variant = "";
    partCode = "";
    isSkin = false;
    if (relativeSegments == null || relativeSegments.Length != 3) return false;

    partCode = ResolvePartCode(relativeSegments[relativeSegments.Length - 1]);
    if (!string.Equals(partCode, "e", StringComparison.OrdinalIgnoreCase)) return false;

    category = (relativeSegments[0] ?? "").Trim();
    var sourceForm = (relativeSegments[1] ?? "").Trim();
    if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(sourceForm)) return false;
    if (TryParseWrappedDescriptorToken(sourceForm, out _, out _, out _)) return false;

    form = SkinFormName;
    variant = SkinVariantName;
    isSkin = true;
    return true;
  }

  static bool TryParseSourceAtlasPath(
    string sourceFolderPath,
    string assetPath,
    out string category,
    out string form,
    out string variant,
    out string partCode,
    out string fileBase,
    out bool isSkin) {
    category = "";
    form = "";
    variant = "";
    partCode = "";
    fileBase = "";
    isSkin = false;

    if (!TryGetSourceRelativeDirectorySegments(sourceFolderPath, assetPath, out var relativeSegments, out fileBase)) {
      return false;
    }

    if (TryParseTo2TransitionSourceAtlasPath(relativeSegments, out category, out form, out variant, out partCode, out isSkin)) {
      return !string.IsNullOrWhiteSpace(fileBase);
    }

    if (TryParseWrappedDescriptorSourceAtlasPath(relativeSegments, out category, out form, out variant, out partCode, out isSkin)) {
      return !string.IsNullOrWhiteSpace(fileBase);
    }

    if (TryParseImplicitSkinSourceAtlasPath(relativeSegments, out category, out form, out variant, out partCode, out isSkin)) {
      return !string.IsNullOrWhiteSpace(fileBase);
    }

    if (TryParseDirectGearSourceAtlasPath(relativeSegments, out category, out form, out variant, out partCode, out isSkin)) {
      return !string.IsNullOrWhiteSpace(fileBase);
    }
    return false;
  }

  static bool IsSupportedColorAtlas(string assetPath) {
    return string.Equals(Path.GetExtension(assetPath), ".png", StringComparison.OrdinalIgnoreCase);
  }

  static bool IsGeneratedNormalAtlasAssetPath(string assetPath) {
    var fileName = Path.GetFileNameWithoutExtension(assetPath ?? "");
    return fileName.EndsWith("_N", StringComparison.OrdinalIgnoreCase);
  }

  static bool ShouldSkipOutputAsset(string assetPath, string sanitizedOutputSubfolder) {
    if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(sanitizedOutputSubfolder)) return false;
    var marker = "/" + sanitizedOutputSubfolder.Trim('/') + "/";
    return assetPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
  }

  static Sprite FindMatchingSprite(LoadedAtlas atlas, string spriteName) {
    if (atlas == null || string.IsNullOrWhiteSpace(spriteName)) return null;
    if (atlas.spritesByName.TryGetValue(spriteName, out var exact) && exact != null) {
      return exact;
    }

    foreach (var pair in atlas.spritesByName) {
      if (!SpriteSliceAddressUtility.HasEquivalentNumericLabel(pair.Key, spriteName)) continue;
      return pair.Value;
    }

    return null;
  }

  static PixelRect ToPixelRect(Rect rect) {
    return new PixelRect(
      Mathf.RoundToInt(rect.x),
      Mathf.RoundToInt(rect.y),
      Mathf.RoundToInt(rect.width),
      Mathf.RoundToInt(rect.height));
  }

  bool IsVisible(Color32 color) {
    if (color.a <= alphaThreshold) return false;
    if (!treatNearWhiteAsEmpty) return true;
    return color.r < nearWhiteThreshold || color.g < nearWhiteThreshold || color.b < nearWhiteThreshold;
  }

  static Color32[] CopyTrimmedPixels(Color32[] sourcePixels, int atlasWidth, PixelRect sourceRect, PixelRect trimRect) {
    var trimmedPixels = new Color32[Math.Max(1, trimRect.width * trimRect.height)];
    var dst = 0;
    for (var y = 0; y < trimRect.height; y++) {
      var srcY = sourceRect.y + trimRect.y + y;
      for (var x = 0; x < trimRect.width; x++) {
        var srcX = sourceRect.x + trimRect.x + x;
        trimmedPixels[dst++] = sourcePixels[(srcY * atlasWidth) + srcX];
      }
    }

    return trimmedPixels;
  }

  bool EnsureScanAvailable(out string sourceFolderPath) {
    sourceFolderPath = "";
    if (!TryGetSourceFolderPath(out sourceFolderPath, true)) return false;
    if (HasFreshScan(sourceFolderPath) && scannedCandidates != null) return true;

    AnalyzeFolder();
    return HasFreshScan(sourceFolderPath);
  }

  bool HasFreshScan(string sourceFolderPath) {
    return scannedCandidates != null &&
           string.Equals(analyzedSourceFolderPath, sourceFolderPath, StringComparison.OrdinalIgnoreCase);
  }

  void InvalidateScan() {
    scannedCandidates = new List<GroupCandidate>();
    analyzedSourceFolderPath = "";
  }

  bool TryGetSourceFolderPath(out string sourceFolderPath, bool showDialog) {
    sourceFolderPath = "";
    if (sourceFolder == null) {
      if (showDialog) EditorUtility.DisplayDialog("Missing Source Folder", "Select a source folder first.", "OK");
      return false;
    }

    sourceFolderPath = NormalizePath(AssetDatabase.GetAssetPath(sourceFolder));
    if (AssetDatabase.IsValidFolder(sourceFolderPath)) return true;
    if (showDialog) EditorUtility.DisplayDialog("Invalid Source Folder", "Could not resolve the selected folder to a project asset path.", "OK");
    return false;
  }

  string ResolveRebindSourceFolderPath() {
    if (rebindSourceFolder == null) return "";
    var folderPath = NormalizePath(AssetDatabase.GetAssetPath(rebindSourceFolder));
    return AssetDatabase.IsValidFolder(folderPath) ? folderPath : "";
  }

  bool TryGetRebindSourceFolderPath(out string sourceFolderPath, bool showDialog) {
    sourceFolderPath = ResolveRebindSourceFolderPath();
    if (!string.IsNullOrWhiteSpace(sourceFolderPath)) return true;
    if (showDialog) {
      EditorUtility.DisplayDialog("Invalid Grouped Atlas Folder", "Select a project folder that contains the grouped atlas outputs to rebind from.", "OK");
    }

    return false;
  }

  string ResolveRebindSpriteLibraryFolderPath() {
    if (rebindSpriteLibraryFolder == null) return "";
    var folderPath = NormalizePath(AssetDatabase.GetAssetPath(rebindSpriteLibraryFolder));
    return AssetDatabase.IsValidFolder(folderPath) ? folderPath : "";
  }

  bool TryGetRebindSpriteLibraryFolderPath(out string libraryFolderPath, bool showDialog) {
    libraryFolderPath = ResolveRebindSpriteLibraryFolderPath();
    if (!string.IsNullOrWhiteSpace(libraryFolderPath)) return true;
    if (showDialog) {
      EditorUtility.DisplayDialog("Invalid Sprite Library Folder", "Select a project folder that contains the target Gear or Skin sprite libraries.", "OK");
    }

    return false;
  }

  string BuildCandidateOutputFolderPath(string sourceRootPath, GroupCandidate candidate, string sanitizedOutputSubfolder) {
    if (candidate == null || candidate.sourceAtlases == null || candidate.sourceAtlases.Count <= 0) {
      return "";
    }

    var normalizedSourceRootPath = NormalizePath(sourceRootPath).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(normalizedSourceRootPath) || string.IsNullOrWhiteSpace(sanitizedOutputSubfolder)) {
      return "";
    }

    var outputFolderPath = normalizedSourceRootPath + "/" + sanitizedOutputSubfolder.Trim('/');
    if (IsSkinCandidate(candidate)) {
      return NormalizePath(outputFolderPath + "/" + SkinFormName + "/" + candidate.partCode);
    }

    return NormalizePath(outputFolderPath + "/" + candidate.form + "/" + candidate.variant + "/" + candidate.partCode);
  }

  string BuildPageAtlasAssetPath(string outputFolderPath, GroupCandidate candidate, int pageIndex, bool isNormalAtlas) {
    var fileName = BuildOutputFilePrefix(candidate) + "_p" + (pageIndex + 1).ToString(CultureInfo.InvariantCulture) + (isNormalAtlas ? "_N" : "") + ".png";
    return NormalizePath(outputFolderPath.TrimEnd('/') + "/" + fileName);
  }

  static string BuildOutputFilePrefix(GroupCandidate candidate) {
    if (candidate == null) return "Grouped";
    if (IsSkinCandidate(candidate)) {
      return SkinGroupKey + "_" + (candidate.partCode ?? "part");
    }

    return (candidate.form ?? "Form") + "_" + (candidate.variant ?? "Variant") + "_" + (candidate.partCode ?? "part");
  }

  static string BuildGroupedSpriteName(string partCode, string sourceCategory, string sourceSpriteName) {
    var normalizedCategory = string.IsNullOrWhiteSpace(sourceCategory) ? "Anim" : sourceCategory.Trim();
    var normalizedSpriteName = string.IsNullOrWhiteSpace(sourceSpriteName) ? "sprite" : sourceSpriteName.Trim();
    if (normalizedSpriteName.StartsWith(normalizedCategory + "_", StringComparison.OrdinalIgnoreCase)) {
      return (partCode ?? "part") + "__" + normalizedSpriteName;
    }

    return (partCode ?? "part") + "__" + normalizedCategory + "_" + normalizedSpriteName;
  }

  static int EnsureUniqueOutputSpriteNames(List<PackedSpriteBuildItem> items) {
    if (items == null || items.Count <= 1) return 0;

    var duplicateGroups = items
      .Where(item => item != null && !string.IsNullOrWhiteSpace(item.outputSpriteName))
      .GroupBy(item => item.outputSpriteName, StringComparer.Ordinal)
      .Where(group => group.Count() > 1)
      .OrderBy(group => group.Key, SpriteSliceAddressUtility.NaturalStringComparer)
      .ToList();
    if (duplicateGroups.Count <= 0) return 0;

    var duplicateBaseNames = new HashSet<string>(duplicateGroups.Select(group => group.Key), StringComparer.Ordinal);
    var reservedNames = new HashSet<string>(
      items
        .Where(item => item != null && !string.IsNullOrWhiteSpace(item.outputSpriteName) && !duplicateBaseNames.Contains(item.outputSpriteName))
        .Select(item => item.outputSpriteName),
      StringComparer.Ordinal);

    var renamedCount = 0;
    for (var groupIndex = 0; groupIndex < duplicateGroups.Count; groupIndex++) {
      var group = duplicateGroups[groupIndex];
      var orderedItems = group
        .OrderBy(item => item.colorSourceAtlasPath, SpriteSliceAddressUtility.NaturalStringComparer)
        .ThenBy(item => item.sourceSpriteName, SpriteSliceAddressUtility.NaturalStringComparer)
        .ToList();

      for (var itemIndex = 0; itemIndex < orderedItems.Count; itemIndex++) {
        var item = orderedItems[itemIndex];
        if (item == null) continue;

        var candidateName = group.Key + "__" + BuildOutputSpriteDisambiguationSuffix(item);
        if (reservedNames.Contains(candidateName)) {
          var suffixIndex = 2;
          var disambiguatedBaseName = candidateName;
          while (reservedNames.Contains(candidateName)) {
            candidateName = disambiguatedBaseName + "_" + suffixIndex.ToString(CultureInfo.InvariantCulture);
            suffixIndex++;
          }
        }

        item.outputSpriteName = candidateName;
        reservedNames.Add(candidateName);
        renamedCount++;
      }
    }

    return renamedCount;
  }

  static string BuildOutputSpriteDisambiguationSuffix(PackedSpriteBuildItem item) {
    var normalizedAtlasPath = NormalizePath(item?.colorSourceAtlasPath);
    if (!string.IsNullOrWhiteSpace(normalizedAtlasPath)) {
      var guid = AssetDatabase.AssetPathToGUID(normalizedAtlasPath);
      if (!string.IsNullOrWhiteSpace(guid)) {
        return guid.Substring(0, Math.Min(8, guid.Length));
      }

      var fileBase = Path.GetFileNameWithoutExtension(normalizedAtlasPath);
      var sanitizedFileBase = SanitizeSpriteNameToken(fileBase);
      if (!string.IsNullOrWhiteSpace(sanitizedFileBase)) {
        return sanitizedFileBase;
      }
    }

    return "dup";
  }

  static string SanitizeSpriteNameToken(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";

    var buffer = new char[value.Length];
    var count = 0;
    for (var i = 0; i < value.Length; i++) {
      var c = value[i];
      if (char.IsLetterOrDigit(c)) {
        buffer[count++] = c;
        continue;
      }

      if (count > 0 && buffer[count - 1] == '_') continue;
      buffer[count++] = '_';
    }

    return new string(buffer, 0, count).Trim('_');
  }

  string GetSanitizedOutputSubfolderName() {
    if (string.IsNullOrWhiteSpace(outputSubfolderName)) return "";
    return SanitizeSubfolderName(outputSubfolderName);
  }

  static string SanitizeSubfolderName(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";

    var invalidChars = Path.GetInvalidFileNameChars();
    var sanitizedChars = new char[value.Length];
    var count = 0;
    for (var i = 0; i < value.Length; i++) {
      var c = value[i];
      sanitizedChars[count++] = Array.IndexOf(invalidChars, c) >= 0 ? '_' : c;
    }

    return new string(sanitizedChars, 0, count).Trim().Trim('_');
  }

  static string BuildCandidateLabel(GroupCandidate candidate) {
    if (candidate == null) return "<invalid>";
    if (IsSkinCandidate(candidate)) {
      return SkinFormName + "/" + candidate.partCode + " :: " + BuildCandidateAnimationSummary(candidate);
    }
    return candidate.form + "/" + candidate.variant + "/" + candidate.partCode + " :: " + BuildCandidateAnimationSummary(candidate);
  }

  static bool IsSkinCandidate(GroupCandidate candidate) {
    return candidate != null && candidate.isSkin;
  }

  static bool IsSkinGroupKey(string groupKey) {
    return string.Equals(groupKey, SkinGroupKey, StringComparison.OrdinalIgnoreCase);
  }

  static void AddFailureLog(List<string> failureLogs, string context, string error) {
    if (failureLogs == null || failureLogs.Count >= 30) return;
    failureLogs.Add((context ?? "<unknown>") + " :: " + (string.IsNullOrWhiteSpace(error) ? "Unknown export failure." : error));
  }

  static string BuildRuntimeMetadataAssetPath(string atlasAssetPath) {
    return TrimmedAtlasExporterWindow.BuildRuntimeMetadataAssetPath(atlasAssetPath);
  }

  static string BuildEditorMetadataAssetPath(string atlasAssetPath) {
    return TrimmedAtlasExporterWindow.BuildEditorMetadataAssetPath(atlasAssetPath);
  }

  static string ResolveExistingTrimmedMetadataReadPath(string runtimeMetadataAssetPath) {
    return ResolveMetadataReadFullPath(runtimeMetadataAssetPath);
  }

  static string ResolveMetadataReadFullPath(string runtimeMetadataAssetPath) {
    var normalizedRuntimeMetadataAssetPath = TrimmedAtlasExporterWindow.ResolveRuntimeMetadataAssetPath(runtimeMetadataAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedRuntimeMetadataAssetPath)) return "";

    var editorMetadataAssetPath = TrimmedAtlasExporterWindow.BuildEditorMetadataAssetPathFromRuntimeMetadata(normalizedRuntimeMetadataAssetPath);
    if (TryGetExistingMetadataFullPath(editorMetadataAssetPath, out var editorMetadataFullPath)) {
      return editorMetadataFullPath;
    }

    return TryGetExistingMetadataFullPath(normalizedRuntimeMetadataAssetPath, out var runtimeMetadataFullPath)
      ? runtimeMetadataFullPath
      : "";
  }

  static bool TryGetExistingMetadataFullPath(string metadataAssetPath, out string metadataFullPath) {
    metadataFullPath = "";
    var normalizedMetadataAssetPath = NormalizePath(metadataAssetPath);
    if (string.IsNullOrWhiteSpace(normalizedMetadataAssetPath)) return false;

    var candidateFullPath = Path.GetFullPath(normalizedMetadataAssetPath);
    if (!File.Exists(candidateFullPath)) return false;

    metadataFullPath = candidateFullPath;
    return true;
  }

  static bool TryReadMetadataJson(string runtimeMetadataAssetPath, out string jsonText, out string error) {
    jsonText = "";
    error = "";

    var metadataFullPath = ResolveMetadataReadFullPath(runtimeMetadataAssetPath);
    if (string.IsNullOrWhiteSpace(metadataFullPath)) {
      error = "Metadata file not found.";
      return false;
    }

    try {
      jsonText = File.ReadAllText(metadataFullPath);
      return true;
    }
    catch (Exception ex) {
      error = ex.Message;
      return false;
    }
  }

  static bool TryLoadGroupedMetadataPayload(string runtimeMetadataAssetPath, out GroupedAtlasMetadataPayload payload, out string error) {
    payload = null;
    if (!TryReadMetadataJson(runtimeMetadataAssetPath, out var jsonText, out error)) return false;

    try {
      payload = JsonUtility.FromJson<GroupedAtlasMetadataPayload>(jsonText);
    }
    catch (Exception ex) {
      error = ex.Message;
      return false;
    }

    if (payload == null) {
      error = "Grouped metadata payload was empty.";
      return false;
    }

    if (payload.sprites == null) {
      payload.sprites = new List<GroupedAtlasSpriteMetadata>();
    }

    if (payload.sourceCategories == null) {
      payload.sourceCategories = new List<string>();
    }
    return true;
  }

  static GroupedAtlasRuntimePayload BuildRuntimeGroupedMetadata(GroupedAtlasMetadataPayload payload) {
    var runtimePayload = new GroupedAtlasRuntimePayload {
      metadataKind = payload?.metadataKind ?? "grouped",
      spritePixelsPerUnit = payload?.spritePixelsPerUnit ?? 100f,
      spriteMeshType = payload?.spriteMeshType ?? (int)SpriteMeshType.Tight
    };
    if (payload?.sprites == null || payload.sprites.Count <= 0) return runtimePayload;

    runtimePayload.sprites.Capacity = payload.sprites.Count;
    for (var i = 0; i < payload.sprites.Count; i++) {
      var sprite = payload.sprites[i];
      if (sprite == null || string.IsNullOrWhiteSpace(sprite.name)) continue;
      runtimePayload.sprites.Add(new GroupedAtlasRuntimeSpriteMetadata {
        name = sprite.name,
        empty = sprite.empty,
        packedRect = sprite.packedRect
      });
    }

    return runtimePayload;
  }

  static void WriteJsonPayload(string assetPath, string jsonText) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return;

    var fullPath = Path.GetFullPath(normalizedAssetPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? "");
    File.WriteAllText(fullPath, jsonText ?? "");
  }

  static string NormalizePath(string assetPath) {
    var normalized = TrimmedAtlasExporterWindow.NormalizeAssetPath(assetPath);
    return string.IsNullOrWhiteSpace(normalized) ? "" : normalized.Replace("\\", "/");
  }

  static bool TryConvertFullPathToAssetPath(string fullPath, out string assetPath) {
    assetPath = "";
    if (string.IsNullOrWhiteSpace(fullPath)) return false;

    var normalizedInput = fullPath.Replace("\\", "/").Trim();
    if (normalizedInput.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) {
      assetPath = normalizedInput;
      return true;
    }

    var projectRoot = Directory.GetCurrentDirectory().Replace("\\", "/").TrimEnd('/');
    if (normalizedInput.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase)) {
      assetPath = normalizedInput.Substring(projectRoot.Length + 1);
      return assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
    }

    return false;
  }
}
#endif
