#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public sealed class EsperanzaGearGroupAtlasWindow : EditorWindow {
  const string DefaultOutputSubfolder = "__GroupedGearAtlases";
  const string SkinGroupKey = "Skin";
  const string SkinFormName = "Skin";
  const string SkinVariantName = "All";
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
    public string groupKey;
    public string category;
    public string form;
    public string variant;
    public string partCode;
    public string fileBase;
    public string sourceKind;
    public int sourceAtlasCount;
    public int pageIndex;
    public int atlasWidth;
    public int atlasHeight;
    public int padding;
    public List<string> sourceCategories = new();
    public List<GroupedAtlasSpriteMetadata> sprites = new();
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

  sealed class PendingGroupedAtlasImport {
    public GroupCandidate candidate;
    public string sourceAtlasAssetPath;
    public string atlasAssetPath;
    public string metadataAssetPath;
    public AtlasPage page;
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

  sealed class GroupedSpriteReplacementIndex {
    public Dictionary<LibraryEntryKey, Sprite> spritesByKey = new();
    public Dictionary<LibraryEntryScopeKey, Dictionary<string, Sprite>> labelsByScope = new();
    public List<CleanupPlan> cleanupPlans = new();
    public int metadataFileCount;
    public int indexedSpriteCount;
  }

  DefaultAsset sourceFolder;
  string outputSubfolderName = DefaultOutputSubfolder;
  DefaultAsset rebindSourceFolder;
  DefaultAsset rebindSpriteLibraryFolder;
  string rebindOutputSubfolderName = DefaultOutputSubfolder;
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

  [MenuItem("Tools/Sprite Streaming/Group Esperanza Gear Atlases")]
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
      "Choose the grouped atlas source root, the grouped output subfolder to read, and the target sprite-library folder to update. Exports combine matching animation folders by form + variant + part, and grouped slice names include the animation prefix such as 'Breathe_1_11'.",
      MessageType.None);
    rebindSourceFolder = (DefaultAsset)EditorGUILayout.ObjectField("Grouped Source Folder", rebindSourceFolder, typeof(DefaultAsset), false);
    rebindOutputSubfolderName = EditorGUILayout.DelayedTextField("Grouped Output Subfolder", rebindOutputSubfolderName ?? "");
    rebindSpriteLibraryFolder = (DefaultAsset)EditorGUILayout.ObjectField("Sprite Library Folder", rebindSpriteLibraryFolder, typeof(DefaultAsset), false);
    if (rebindSourceFolder != null && string.IsNullOrWhiteSpace(ResolveRebindSourceFolderPath())) {
      EditorGUILayout.HelpBox("Grouped Source Folder must be a project folder asset.", MessageType.Warning);
    }
    if (rebindSpriteLibraryFolder != null && string.IsNullOrWhiteSpace(ResolveRebindSpriteLibraryFolderPath())) {
      EditorGUILayout.HelpBox("Sprite Library Folder must be a project folder asset.", MessageType.Warning);
    }

    using (new EditorGUI.DisabledScope(
      rebindSourceFolder == null ||
      rebindSpriteLibraryFolder == null ||
      string.IsNullOrWhiteSpace(ResolveRebindSourceFolderPath()) ||
      string.IsNullOrWhiteSpace(ResolveRebindSpriteLibraryFolderPath()) ||
      string.IsNullOrWhiteSpace(GetSanitizedRebindOutputSubfolderName()))) {
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
    }
    finally {
      if (deferredWritePhaseStarted) {
        EndDeferredGroupedAtlasWritePhase(sourceFolderPath, pendingImports.Count, failureLogs.Count);
      }
    }

    if (pendingImports.Count > 0) {
      var pendingSpriteCount = CountPendingImportSprites(pendingImports);
      var maxPendingPageSpriteCount = CountMaxPendingImportPageSprites(pendingImports);
      Debug.Log(
        "[GearGroupAtlas] Final import phase started." +
        " source='" + sourceFolderPath + "'" +
        " pending_imports=" + pendingImports.Count +
        " pending_sprites=" + pendingSpriteCount +
        " max_page_sprites=" + maxPendingPageSpriteCount);
    }

    var finalizedCandidates = pendingImports.Count > 0
      ? FinalizeWrittenGroupedAtlasImports(pendingImports, failureLogs, out exportedPageCount)
      : new List<GroupCandidate>();
    exportedCandidateCount = finalizedCandidates.Count;

    var cleanupSummary = finalizedCandidates.Count > 0
      ? CleanupExportedSourceAssets(sourceFolderPath, finalizedCandidates)
      : new ExportCleanupSummary();

    Debug.Log(
      "[GearGroupAtlas] Export complete." +
      " source='" + sourceFolderPath + "'" +
      " exported_candidates=" + exportedCandidateCount +
      " exported_pages=" + exportedPageCount +
      " deleted_source_assets=" + cleanupSummary.deletedAssetCount +
      " deleted_source_folders=" + cleanupSummary.deletedFolderCount +
      " failures=" + failureLogs.Count);

    for (var i = 0; i < failureLogs.Count; i++) {
      Debug.LogWarning("[GearGroupAtlas] " + failureLogs[i]);
    }

    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();
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

  static int CountPendingImportSprites(List<PendingGroupedAtlasImport> pendingImports) {
    if (pendingImports == null || pendingImports.Count <= 0) return 0;

    var total = 0;
    for (var i = 0; i < pendingImports.Count; i++) {
      total += pendingImports[i]?.page?.items?.Count ?? 0;
    }

    return total;
  }

  static int CountMaxPendingImportPageSprites(List<PendingGroupedAtlasImport> pendingImports) {
    if (pendingImports == null || pendingImports.Count <= 0) return 0;

    var max = 0;
    for (var i = 0; i < pendingImports.Count; i++) {
      var count = pendingImports[i]?.page?.items?.Count ?? 0;
      if (count > max) max = count;
    }

    return max;
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

      if (!TryWriteMetadata(page.colorAtlasPath, candidate, page, false, out var metadataAssetPath, out error)) {
        return false;
      }
      candidatePendingImports.Add(new PendingGroupedAtlasImport {
        candidate = candidate,
        sourceAtlasAssetPath = representativeSourceAtlasPath,
        atlasAssetPath = page.colorAtlasPath,
        metadataAssetPath = metadataAssetPath,
        page = page
      });

      if (ExportNormalAtlases) {
        if (!TryWritePageTexture(page.normalAtlasPath, page, true, out error)) {
          return false;
        }

        if (!TryWriteMetadata(page.normalAtlasPath, candidate, page, true, out var normalMetadataAssetPath, out error)) {
          return false;
        }
        candidatePendingImports.Add(new PendingGroupedAtlasImport {
          candidate = candidate,
          sourceAtlasAssetPath = representativeSourceAtlasPath,
          atlasAssetPath = page.normalAtlasPath,
          metadataAssetPath = normalMetadataAssetPath,
          page = page
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
    var orderedIncomingItems = incomingItems
      .OrderByDescending(item => item.Height)
      .ThenByDescending(item => item.Width)
      .ThenBy(item => item.outputSpriteName, StringComparer.Ordinal)
      .ToList();

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

  List<GroupCandidate> FinalizeWrittenGroupedAtlasImports(
    List<PendingGroupedAtlasImport> pendingImports,
    List<string> failureLogs,
    out int finalizedPageCount) {
    finalizedPageCount = 0;
    var finalizedCandidates = new List<GroupCandidate>();
    if (pendingImports == null || pendingImports.Count <= 0) return finalizedCandidates;

    AssetDatabase.Refresh();
    var atlasAssetPathsToReimport = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var pendingImportsByAtlasPath = new Dictionary<string, PendingGroupedAtlasImport>(StringComparer.OrdinalIgnoreCase);
    var totalPendingSpriteCount = CountPendingImportSprites(pendingImports);
    var maxPendingPageSpriteCount = CountMaxPendingImportPageSprites(pendingImports);
    Debug.Log(
      "[GearGroupAtlas] Finalize grouped atlas textures." +
      " pending_imports=" + pendingImports.Count +
      " pending_sprites=" + totalPendingSpriteCount +
      " max_page_sprites=" + maxPendingPageSpriteCount);

    var requiredPageCounts = new Dictionary<GroupCandidate, int>();
    var finalizedPageCounts = new Dictionary<GroupCandidate, int>();
    for (var i = 0; i < pendingImports.Count; i++) {
      var pendingImport = pendingImports[i];
      if (pendingImport != null && !string.IsNullOrWhiteSpace(pendingImport.atlasAssetPath)) {
        pendingImportsByAtlasPath[pendingImport.atlasAssetPath] = pendingImport;
      }
      if (pendingImport?.candidate == null) continue;
      requiredPageCounts[pendingImport.candidate] = requiredPageCounts.TryGetValue(pendingImport.candidate, out var count) ? count + 1 : 1;
    }

    try {
      for (var i = 0; i < pendingImports.Count; i++) {
        var pendingImport = pendingImports[i];
        if (pendingImport == null || string.IsNullOrWhiteSpace(pendingImport.atlasAssetPath)) continue;

        var pageSpriteCount = pendingImport.page?.items?.Count ?? 0;
        EditorUtility.DisplayProgressBar(
          "Gear Group Atlases",
          "Finalizing atlas " + (i + 1) + "/" + pendingImports.Count + " (" + pageSpriteCount + " sprites)\n" + pendingImport.atlasAssetPath,
          (float)(i + 1) / pendingImports.Count);
        if (i == 0 || ((i + 1) % 8) == 0 || i == pendingImports.Count - 1) {
          Debug.Log(
            "[GearGroupAtlas] Finalize progress." +
            " index=" + (i + 1) + "/" + pendingImports.Count +
            " page_sprites=" + pageSpriteCount +
            " atlas='" + pendingImport.atlasAssetPath + "'");
        }

        if (!TryFinalizeGroupedAtlasTexture(
              pendingImport.sourceAtlasAssetPath,
              pendingImport.atlasAssetPath,
              pendingImport.page,
              out var requiresReimport,
              out var error)) {
          AddFailureLog(failureLogs, pendingImport.atlasAssetPath, error);
          continue;
        }

        if (requiresReimport) {
          atlasAssetPathsToReimport.Add(pendingImport.atlasAssetPath);
        }

        TrimmedAtlasExporterWindow.EnsureMetadataAddressable(pendingImport.metadataAssetPath, saveAssets: false);
        TrimmedSpriteOffsetResolver.InvalidateAtlas(pendingImport.atlasAssetPath);
        finalizedPageCount++;

        if (pendingImport.candidate == null) continue;
        finalizedPageCounts[pendingImport.candidate] = finalizedPageCounts.TryGetValue(pendingImport.candidate, out var count) ? count + 1 : 1;
      }
    }
    finally {
      EditorUtility.ClearProgressBar();
    }

    if (atlasAssetPathsToReimport.Count > 0) {
      var failedReimportAtlasPaths = ReimportFinalizedGroupedAtlasTextures(atlasAssetPathsToReimport, failureLogs);
      foreach (var failedAtlasPath in failedReimportAtlasPaths) {
        if (!pendingImportsByAtlasPath.TryGetValue(failedAtlasPath, out var failedPendingImport) ||
            failedPendingImport?.candidate == null) {
          continue;
        }

        DecrementFinalizedPageCount(finalizedPageCounts, failedPendingImport.candidate, ref finalizedPageCount);
        Debug.LogWarning(
          "[GearGroupAtlas] Skipping source cleanup for candidate page because grouped atlas reimport failed." +
          " group='" + BuildCandidateLabel(failedPendingImport.candidate) + "'" +
          " atlas='" + failedAtlasPath + "'");
      }
    }

    if (finalizedPageCount > 0) {
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();
    }

    foreach (var pair in requiredPageCounts) {
      if (!finalizedPageCounts.TryGetValue(pair.Key, out var count) || count != pair.Value) continue;
      finalizedCandidates.Add(pair.Key);
    }

    return finalizedCandidates;
  }

  static void DecrementFinalizedPageCount(
    Dictionary<GroupCandidate, int> finalizedPageCounts,
    GroupCandidate candidate,
    ref int finalizedPageCount) {
    if (finalizedPageCounts == null || candidate == null) return;
    if (!finalizedPageCounts.TryGetValue(candidate, out var count) || count <= 0) return;

    if (count == 1) {
      finalizedPageCounts.Remove(candidate);
    }
    else {
      finalizedPageCounts[candidate] = count - 1;
    }

    if (finalizedPageCount > 0) {
      finalizedPageCount--;
    }
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
    AddCleanupAssetPath(sourceAssetPaths, Path.ChangeExtension(normalizedAssetPath, ".json"));
  }

  static void AddCleanupAssetPath(HashSet<string> sourceAssetPaths, string assetPath) {
    var normalizedAssetPath = NormalizePath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedAssetPath)) return;
    sourceAssetPaths.Add(normalizedAssetPath);
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
      .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
      .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
      .ToList();
    if (metadataFullPaths.Count <= 0) return true;

    for (var i = 0; i < metadataFullPaths.Count; i++) {
      var metadataFullPath = metadataFullPaths[i];
      if (!TryConvertFullPathToAssetPath(metadataFullPath, out var metadataAssetPath)) continue;

      GroupedAtlasMetadataPayload payload;
      try {
        payload = JsonUtility.FromJson<GroupedAtlasMetadataPayload>(File.ReadAllText(metadataFullPath));
      }
      catch (Exception ex) {
        error = "Failed to read grouped atlas metadata '" + metadataAssetPath + "': " + ex.Message;
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
      .OrderBy(sprite => sprite.name, StringComparer.Ordinal)
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
      orderedSprites = sprites
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

    var ordered = items
      .OrderByDescending(item => item.Height)
      .ThenByDescending(item => item.Width)
      .ThenBy(item => item.outputSpriteName, StringComparer.Ordinal)
      .ToList();

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

  bool TryWriteMetadata(string atlasAssetPath, GroupCandidate candidate, AtlasPage page, bool isNormalMetadata, out string metadataAssetPath, out string error) {
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

    var payload = new GroupedAtlasMetadataPayload {
      groupKey = IsSkinCandidate(candidate) ? SkinGroupKey : BuildOutputFilePrefix(candidate),
      category = sourceCategories.FirstOrDefault() ?? candidate.sourceCategories.FirstOrDefault() ?? "",
      form = candidate.form,
      variant = candidate.variant,
      partCode = candidate.partCode,
      fileBase = "",
      sourceKind = isNormalMetadata ? "normal" : "color",
      sourceAtlasCount = sourceAtlasPaths.Count > 0 ? sourceAtlasPaths.Count : candidate.sourceAtlases?.Count ?? 0,
      pageIndex = page.pageIndex,
      atlasWidth = page.width,
      atlasHeight = page.height,
      padding = padding
    };
    if (sourceCategories.Count > 0) {
      payload.sourceCategories.AddRange(sourceCategories.OrderBy(category => category, StringComparer.OrdinalIgnoreCase));
    }
    else if (candidate.sourceCategories != null && candidate.sourceCategories.Count > 0) {
      payload.sourceCategories.AddRange(candidate.sourceCategories);
    }

    for (var i = 0; i < page.items.Count; i++) {
      var item = page.items[i];
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
      metadataAssetPath = Path.ChangeExtension(atlasAssetPath, ".json").Replace("\\", "/");
      var metadataFullPath = Path.GetFullPath(metadataAssetPath);
      Directory.CreateDirectory(Path.GetDirectoryName(metadataFullPath) ?? "");
      File.WriteAllText(metadataFullPath, JsonUtility.ToJson(payload, true));
      return true;
    }
    catch (Exception ex) {
      error = ex.Message;
      return false;
    }
  }

  static HashSet<string> ReimportFinalizedGroupedAtlasTextures(HashSet<string> atlasAssetPaths, List<string> failureLogs) {
    var failedAtlasPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (atlasAssetPaths == null || atlasAssetPaths.Count <= 0) return failedAtlasPaths;

    var orderedAssetPaths = atlasAssetPaths
      .Where(path => !string.IsNullOrWhiteSpace(path))
      .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
      .ToList();
    if (orderedAssetPaths.Count <= 0) return failedAtlasPaths;

    Debug.Log("[GearGroupAtlas] Final grouped atlas reimport started. atlases=" + orderedAssetPaths.Count);
    for (var i = 0; i < orderedAssetPaths.Count; i++) {
      var atlasPath = orderedAssetPaths[i];
      try {
        AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
      }
      catch (Exception ex) {
        failedAtlasPaths.Add(atlasPath);
        AddFailureLog(failureLogs, atlasPath, "Final grouped atlas reimport failed: " + ex.Message);
        Debug.LogWarning(
          "[GearGroupAtlas] Final grouped atlas reimport failed." +
          " atlas='" + atlasPath + "'" +
          " error='" + ex.Message + "'");
      }
    }

    Debug.Log(
      "[GearGroupAtlas] Final grouped atlas reimport completed." +
      " atlases=" + orderedAssetPaths.Count +
      " failures=" + failedAtlasPaths.Count);
    return failedAtlasPaths;
  }

  bool TryFinalizeGroupedAtlasTexture(
    string sourceAtlasAssetPath,
    string exportedAtlasAssetPath,
    AtlasPage page,
    out bool requiresReimport,
    out string error) {
    requiresReimport = false;
    error = "";
    if (page?.items == null) {
      error = "Missing atlas page data for final import '" + exportedAtlasAssetPath + "'.";
      return false;
    }
    var importer = AssetImporter.GetAtPath(exportedAtlasAssetPath) as TextureImporter;
    if (importer == null) {
      error = "Texture importer is unavailable for '" + exportedAtlasAssetPath + "'.";
      return false;
    }

    var importerChanged = SpriteStreamingTextureImportPolicy.Apply(importer, true);
    importerChanged |= TrimmedAtlasExporterWindow.CopySourceImporterSettings(sourceAtlasAssetPath, importer);
    if (!importer.alphaIsTransparency) {
      importer.alphaIsTransparency = true;
      importerChanged = true;
    }

    var factory = new SpriteDataProviderFactories();
    factory.Init();
    var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer) as ISpriteEditorDataProvider;
    if (dataProvider == null) {
      error = "Sprite data provider is unavailable for '" + exportedAtlasAssetPath + "'.";
      return false;
    }

    dataProvider.InitSpriteEditorDataProvider();

    var rects = new List<SpriteRect>(page.items.Count);
    for (var i = 0; i < page.items.Count; i++) {
      var item = page.items[i];
      rects.Add(new SpriteRect {
        name = item.outputSpriteName,
        rect = new Rect(item.packedRect.x, item.packedRect.y, item.packedRect.width, item.packedRect.height),
        alignment = (int)SpriteAlignment.Center,
        pivot = new Vector2(0.5f, 0.5f),
        border = Vector4.zero
      });
    }

    dataProvider.SetSpriteRects(rects.ToArray());
    if (dataProvider.HasDataProvider(typeof(ISpriteNameFileIdDataProvider))) {
      var nameFileIdProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
      var pairs = new List<SpriteNameFileIdPair>(rects.Count);
      for (var i = 0; i < rects.Count; i++) {
        pairs.Add(new SpriteNameFileIdPair(rects[i].name, GUID.Generate()));
      }

      nameFileIdProvider.SetNameFileIdPairs(pairs);
    }

    dataProvider.Apply();
    requiresReimport = importerChanged || rects.Count > 0;
    if (requiresReimport) {
      AssetDatabase.WriteImportSettingsIfDirty(exportedAtlasAssetPath);
    }

    return true;
  }

  void RebindGroupedSpriteLibraries() {
    if (!TryGetRebindSourceFolderPath(out var sourceFolderPath, true)) return;

    var sanitizedOutputSubfolder = GetSanitizedRebindOutputSubfolderName();
    if (string.IsNullOrWhiteSpace(sanitizedOutputSubfolder)) {
      EditorUtility.DisplayDialog("Invalid Output Subfolder", "Provide a valid output subfolder name for grouped atlas exports.", "OK");
      return;
    }

    if (!TryGetRebindSpriteLibraryFolderPath(out var libraryFolderPath, true)) return;

    if (!TryBuildGroupedSpriteReplacementIndex(sourceFolderPath, sanitizedOutputSubfolder, out var replacementIndex, out var error)) {
      EditorUtility.DisplayDialog("Rebind Failed", error, "OK");
      return;
    }

    Debug.Log(
      "[GearGroupAtlas] Prepared rebind index." +
      " source='" + sourceFolderPath + "'" +
      " output_subfolder='" + sanitizedOutputSubfolder + "'" +
      " library_root='" + libraryFolderPath + "'" +
      " metadata_files=" + replacementIndex.metadataFileCount +
      " indexed_sprites=" + replacementIndex.spritesByKey.Count);

    RebindSpriteLibraries(
      libraryFolderPath,
      replacementIndex,
      out var touchedLibraries,
      out var reboundEntries,
      out var processedLibraryKinds);
    var cleanupPlans = replacementIndex.cleanupPlans
      .Where(plan => plan != null && processedLibraryKinds.Contains(plan.isSkinLibrary))
      .ToList();
    var deletedAssets = reboundEntries > 0 && cleanupPlans.Count > 0 ? CleanupStaleOutputs(cleanupPlans) : 0;

    Debug.Log(
      "[GearGroupAtlas] Rebind complete." +
      " source='" + sourceFolderPath + "'" +
      " output_subfolder='" + sanitizedOutputSubfolder + "'" +
      " library_root='" + libraryFolderPath + "'" +
      " metadata_files=" + replacementIndex.metadataFileCount +
      " indexed_sprites=" + replacementIndex.spritesByKey.Count +
      " libraries=" + touchedLibraries +
      " rebound_entries=" + reboundEntries +
      " cleaned_assets=" + deletedAssets);

    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();
  }

  bool TryBuildGroupedSpriteReplacementIndex(
    string sourceFolderPath,
    string sanitizedOutputSubfolder,
    out GroupedSpriteReplacementIndex replacementIndex,
    out string error) {
    replacementIndex = new GroupedSpriteReplacementIndex();
    error = "";

    var normalizedSourceFolderPath = NormalizePath(sourceFolderPath).TrimEnd('/');
    var normalizedOutputSubfolder = (sanitizedOutputSubfolder ?? "").Trim().Trim('/');
    var groupedOutputRoot = NormalizePath(normalizedSourceFolderPath + "/" + normalizedOutputSubfolder).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(normalizedSourceFolderPath) || string.IsNullOrWhiteSpace(normalizedOutputSubfolder) || string.IsNullOrWhiteSpace(groupedOutputRoot)) {
      error = "Missing source folder or grouped atlas output subfolder.";
      return false;
    }

    var sourceFolderFullPath = Path.GetFullPath(normalizedSourceFolderPath);
    if (!Directory.Exists(sourceFolderFullPath)) {
      error = "Grouped atlas source folder does not exist on disk: " + sourceFolderFullPath;
      return false;
    }

    var cleanupPlansByKey = new Dictionary<string, CleanupPlan>(StringComparer.OrdinalIgnoreCase);
    var metadataFullPaths = Directory.GetFiles(sourceFolderFullPath, "*.json", SearchOption.AllDirectories);
    Array.Sort(metadataFullPaths, StringComparer.OrdinalIgnoreCase);

    for (var metadataIndex = 0; metadataIndex < metadataFullPaths.Length; metadataIndex++) {
      var metadataFullPath = metadataFullPaths[metadataIndex];
      if (!TryConvertFullPathToAssetPath(metadataFullPath, out var metadataAssetPath)) continue;

      metadataAssetPath = NormalizePath(metadataAssetPath);
      if (!metadataAssetPath.StartsWith(groupedOutputRoot + "/", StringComparison.OrdinalIgnoreCase)) continue;

      GroupedAtlasMetadataPayload payload;
      try {
        payload = JsonUtility.FromJson<GroupedAtlasMetadataPayload>(File.ReadAllText(metadataFullPath));
      }
      catch (Exception ex) {
        error = "Failed to read grouped atlas metadata '" + metadataAssetPath + "': " + ex.Message;
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

      var spritesByName = BuildSpriteLookupByName(atlasAssetPath);
      if (spritesByName.Count <= 0) {
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
        cleanupPlan.keepAssetPaths.Add(metadataAssetPath);
      }

      for (var spriteIndex = 0; spriteIndex < payload.sprites.Count; spriteIndex++) {
        var groupedSprite = payload.sprites[spriteIndex];
        if (groupedSprite == null || string.IsNullOrWhiteSpace(groupedSprite.name)) continue;
        if (!spritesByName.TryGetValue(groupedSprite.name, out var replacementSprite) || replacementSprite == null) continue;

        var sourceCategory = string.IsNullOrWhiteSpace(groupedSprite.sourceCategory)
          ? (payload.category ?? "").Trim()
          : groupedSprite.sourceCategory.Trim();
        if (string.IsNullOrWhiteSpace(sourceCategory)) continue;

        var partCode = string.IsNullOrWhiteSpace(groupedSprite.sourcePartCode)
          ? (payload.partCode ?? "").Trim()
          : groupedSprite.sourcePartCode.Trim();
        if (string.IsNullOrWhiteSpace(partCode) && !TryExtractPartCode(groupedSprite.name, out partCode)) continue;

        var label = BuildLibraryEntryLabel(payload, groupedSprite);
        if (string.IsNullOrWhiteSpace(label)) continue;

        var key = new LibraryEntryKey(isNormalAtlas, isSkinLibrary, sourceCategory, partCode, label);
        TryAddGroupedSpriteReplacement(
          replacementIndex,
          key,
          replacementSprite,
          atlasAssetPath,
          groupedSprite.name,
          groupedSprite.sourceSpriteName,
          sourceCategory,
          payload.form,
          payload.variant);
      }
    }

    replacementIndex.cleanupPlans = cleanupPlansByKey.Values
      .OrderBy(plan => plan.folderPath, StringComparer.OrdinalIgnoreCase)
      .ThenBy(plan => plan.filePrefix, StringComparer.OrdinalIgnoreCase)
      .ToList();

    if (replacementIndex.metadataFileCount <= 0) {
      error = "No grouped atlas metadata was found under '" + normalizedSourceFolderPath + "' for output subfolder '" + sanitizedOutputSubfolder + "'.";
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
    Sprite replacementSprite,
    string atlasAssetPath,
    string groupedSpriteName,
    string sourceSpriteName,
    string sourceCategory,
    string form,
    string variant) {
    if (replacementIndex == null || replacementSprite == null) return;

    if (replacementIndex.spritesByKey.TryGetValue(key, out var existing) && existing != null && existing != replacementSprite) {
      Debug.LogWarning(
        "[GearGroupAtlas] Duplicate grouped sprite replacement key." +
        " category='" + key.scopeKey.category + "'" +
        " part='" + key.scopeKey.partCode + "'" +
        " label='" + key.label + "'" +
        " normal=" + key.scopeKey.isNormal +
        " skin=" + key.scopeKey.isSkinLibrary +
        " source_category='" + (sourceCategory ?? "") + "'" +
        " form='" + (form ?? "") + "'" +
        " variant='" + (variant ?? "") + "'" +
        " source_sprite='" + (sourceSpriteName ?? "") + "'" +
        " existing='" + AssetDatabase.GetAssetPath(existing) + "[" + existing.name + "]'" +
        " incoming='" + atlasAssetPath + "[" + groupedSpriteName + "]'");
      return;
    }

    replacementIndex.spritesByKey[key] = replacementSprite;
    if (!replacementIndex.labelsByScope.TryGetValue(key.scopeKey, out var replacementsByLabel) || replacementsByLabel == null) {
      replacementsByLabel = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
      replacementIndex.labelsByScope[key.scopeKey] = replacementsByLabel;
    }

    replacementsByLabel[key.label] = replacementSprite;
    replacementIndex.indexedSpriteCount = replacementIndex.spritesByKey.Count;
  }

  void RebindSpriteLibraries(
    string libraryFolderPath,
    GroupedSpriteReplacementIndex replacementIndex,
    out int touchedLibraries,
    out int reboundEntries,
    out HashSet<bool> processedLibraryKinds) {
    touchedLibraries = 0;
    reboundEntries = 0;
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
    var savedLibraryPaths = new List<string>();

    for (var libraryIndex = 0; libraryIndex < libraryPaths.Length; libraryIndex++) {
      var libraryFullAssetPath = libraryPaths[libraryIndex];
      if (!TryConvertFullPathToAssetPath(libraryFullAssetPath, out var libraryPath)) continue;

      libraryPath = NormalizePath(libraryPath);
      if (!TryParseSpriteLibraryDescriptor(libraryPath, out var partCode, out var isNormalLibrary, out var isSkinLibrary)) continue;
      if (isNormalLibrary) continue;
      parsedLibraryCount++;
      processedLibraryKinds.Add(isSkinLibrary);

      var libraryAsset = LoadSpriteLibrarySourceAsset(libraryPath);
      if (libraryAsset == null) {
        loadFailedCount++;
        if (skippedLibraryLogCount < 8) {
          Debug.LogWarning("[GearGroupAtlas] Rebind skipped sprite library because the asset could not be loaded. path='" + libraryPath + "'");
          skippedLibraryLogCount++;
        }
        continue;
      }

      var serializedObject = new SerializedObject(libraryAsset);
      serializedObject.UpdateIfRequiredOrScript();
      var libraryProperty = serializedObject.FindProperty("m_Library");
      if (libraryProperty == null || !libraryProperty.isArray) {
        missingLibraryPropertyCount++;
        if (skippedLibraryLogCount < 8) {
          Debug.LogWarning(
            "[GearGroupAtlas] Rebind skipped sprite library because 'm_Library' was not available." +
            " path='" + libraryPath + "'" +
            " type='" + libraryAsset.GetType().FullName + "'");
          skippedLibraryLogCount++;
        }
        continue;
      }

      var libraryChanged = false;
      var libraryReboundEntries = 0;
      var libraryMatchedCategoryCount = 0;
      for (var categoryIndex = 0; categoryIndex < libraryProperty.arraySize; categoryIndex++) {
        var categoryProperty = libraryProperty.GetArrayElementAtIndex(categoryIndex);
        var categoryName = categoryProperty.FindPropertyRelative("m_Name")?.stringValue ?? "";
        var scopeKey = new LibraryEntryScopeKey(isNormalLibrary, isSkinLibrary, categoryName, partCode);
        if (!replacementIndex.labelsByScope.TryGetValue(scopeKey, out var replacementsByLabel) || replacementsByLabel == null || replacementsByLabel.Count <= 0) {
          continue;
        }

        libraryMatchedCategoryCount++;
        matchedCategoryCount++;

        var overrideEntriesProperty = categoryProperty.FindPropertyRelative("m_OverrideEntries");
        if (overrideEntriesProperty == null || !overrideEntriesProperty.isArray) continue;

        for (var entryIndex = 0; entryIndex < overrideEntriesProperty.arraySize; entryIndex++) {
          var entryProperty = overrideEntriesProperty.GetArrayElementAtIndex(entryIndex);
          var entryName = entryProperty.FindPropertyRelative("m_Name")?.stringValue ?? "";
          if (!TryResolveLabelReplacement(replacementsByLabel, entryName, out var replacementSprite) || replacementSprite == null) continue;

          var spriteProperty = entryProperty.FindPropertyRelative("m_Sprite");
          var spriteOverrideProperty = entryProperty.FindPropertyRelative("m_SpriteOverride");
          var currentSprite = ResolveSpriteReference(spriteProperty, spriteOverrideProperty);
          if (currentSprite == replacementSprite) continue;

          if (spriteProperty != null) spriteProperty.objectReferenceValue = replacementSprite;
          if (spriteOverrideProperty != null) spriteOverrideProperty.objectReferenceValue = replacementSprite;
          libraryChanged = true;
          libraryReboundEntries++;
        }
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

      if (!libraryChanged) continue;

      serializedObject.ApplyModifiedPropertiesWithoutUndo();
      SaveSpriteLibrarySourceAsset(libraryAsset, libraryPath);
      savedLibraryPaths.Add(libraryPath);
      touchedLibraries++;
      reboundEntries += libraryReboundEntries;
    }

    if (touchedLibraries > 0) {
      for (var savedLibraryIndex = 0; savedLibraryIndex < savedLibraryPaths.Count; savedLibraryIndex++) {
        AssetDatabase.ImportAsset(savedLibraryPaths[savedLibraryIndex], ImportAssetOptions.ForceUpdate);
      }
    }
    else {
      Debug.LogWarning(
        "[GearGroupAtlas] Rebind updated no sprite libraries." +
        " library_files=" + libraryPaths.Length +
        " parsed_libraries=" + parsedLibraryCount +
        " matched_categories=" + matchedCategoryCount +
        " load_failures=" + loadFailedCount +
        " missing_library_property=" + missingLibraryPropertyCount);
    }
  }

  static UnityEngine.Object LoadSpriteLibrarySourceAsset(string libraryPath) {
    var loadedObjects = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(libraryPath);
    if (loadedObjects == null || loadedObjects.Length <= 0) return null;

    for (var assetIndex = 0; assetIndex < loadedObjects.Length; assetIndex++) {
      var candidate = loadedObjects[assetIndex];
      if (candidate == null) continue;
      if (string.Equals(candidate.GetType().FullName, "UnityEngine.U2D.Animation.SpriteLibrarySourceAsset", StringComparison.Ordinal)) {
        return candidate;
      }
    }

    Debug.LogWarning("[GearGroupAtlas] Serialized sprite library source asset was not found. path='" + libraryPath + "' loaded_objects=" + loadedObjects.Length);
    return null;
  }

  static void SaveSpriteLibrarySourceAsset(UnityEngine.Object libraryAsset, string libraryPath) {
    if (libraryAsset == null || string.IsNullOrWhiteSpace(libraryPath)) return;
    UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new[] { libraryAsset }, libraryPath, true);
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

  static bool TryResolveLabelReplacement(Dictionary<string, Sprite> replacementsByLabel, string label, out Sprite replacementSprite) {
    replacementSprite = null;
    var normalizedLabel = label ?? "";
    if (string.IsNullOrWhiteSpace(normalizedLabel) || replacementsByLabel == null || replacementsByLabel.Count <= 0) {
      return false;
    }

    if (replacementsByLabel.TryGetValue(normalizedLabel, out replacementSprite) && replacementSprite != null) {
      return true;
    }

    foreach (var pair in replacementsByLabel) {
      if (!SpriteSliceAddressUtility.HasEquivalentNumericLabel(pair.Key, normalizedLabel)) continue;
      replacementSprite = pair.Value;
      return replacementSprite != null;
    }

    return false;
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

  static Dictionary<string, Sprite> BuildSpriteLookupByName(string atlasAssetPath) {
    var result = new Dictionary<string, Sprite>(StringComparer.Ordinal);
    var sprites = AssetDatabase.LoadAllAssetsAtPath(atlasAssetPath).OfType<Sprite>();
    foreach (var sprite in sprites) {
      if (sprite == null || string.IsNullOrWhiteSpace(sprite.name)) continue;
      result[sprite.name] = sprite;
    }

    return result;
  }

  static Sprite ResolveSpriteReference(SerializedProperty spriteProperty, SerializedProperty spriteOverrideProperty) {
    var sprite = spriteProperty != null ? spriteProperty.objectReferenceValue as Sprite : null;
    if (sprite != null) return sprite;
    return spriteOverrideProperty != null ? spriteOverrideProperty.objectReferenceValue as Sprite : null;
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
        cleanupPlan.keepAssetPaths.Add(Path.ChangeExtension(page.colorAtlasPath, ".json").Replace("\\", "/"));
      }

      if (!includeNormalAtlases || string.IsNullOrWhiteSpace(page.normalAtlasPath)) continue;
      cleanupPlan.keepAssetPaths.Add(page.normalAtlasPath);
      cleanupPlan.keepAssetPaths.Add(Path.ChangeExtension(page.normalAtlasPath, ".json").Replace("\\", "/"));
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
    var shallowSkippedCount = 0;
    var parseRejectedCount = 0;

    var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { sourceFolderPath });
    for (var i = 0; i < textureGuids.Length; i++) {
      var assetPath = NormalizePath(AssetDatabase.GUIDToAssetPath(textureGuids[i]));
      if (!IsSupportedColorAtlas(assetPath)) continue;
      if (IsGeneratedNormalAtlasAssetPath(assetPath)) continue;
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
      " output_skipped=" + outputSkippedCount +
      " shallow_skipped=" + shallowSkippedCount +
      " parse_rejected=" + parseRejectedCount +
      " matched=" + candidates.Sum(candidate => candidate?.sourceAtlases?.Count ?? 0));
    return candidates;
  }

  static int CompareCandidates(GroupCandidate left, GroupCandidate right) {
    var formCompare = string.Compare(left?.form, right?.form, StringComparison.OrdinalIgnoreCase);
    if (formCompare != 0) return formCompare;

    var variantCompare = string.Compare(left?.variant, right?.variant, StringComparison.OrdinalIgnoreCase);
    if (variantCompare != 0) return variantCompare;

    var partCompare = string.Compare(left?.partCode, right?.partCode, StringComparison.OrdinalIgnoreCase);
    if (partCompare != 0) return partCompare;

    return string.Compare(BuildCandidateAnimationSummary(left), BuildCandidateAnimationSummary(right), StringComparison.OrdinalIgnoreCase);
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
      .OrderBy(record => record.category, StringComparer.OrdinalIgnoreCase)
      .ThenBy(record => record.fileBase, StringComparer.OrdinalIgnoreCase)
      .ThenBy(record => record.atlasPath, StringComparer.OrdinalIgnoreCase)
      .ToList();

    candidate.sourceCategories = candidate.sourceAtlases
      .Select(record => (record.category ?? "").Trim())
      .Where(category => !string.IsNullOrWhiteSpace(category))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
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

    if (TryParseWrappedDescriptorSourceAtlasPath(relativeSegments, out category, out form, out variant, out partCode, out isSkin)) {
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
      EditorUtility.DisplayDialog("Invalid Grouped Source Folder", "Select a project folder that contains the grouped atlas outputs to rebind from.", "OK");
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

  string GetSanitizedOutputSubfolderName() {
    if (string.IsNullOrWhiteSpace(outputSubfolderName)) return "";
    return SanitizeSubfolderName(outputSubfolderName);
  }

  string GetSanitizedRebindOutputSubfolderName() {
    if (string.IsNullOrWhiteSpace(rebindOutputSubfolderName)) return "";
    return SanitizeSubfolderName(rebindOutputSubfolderName);
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
