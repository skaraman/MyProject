#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed partial class GroupAtlasWindow : EditorWindow {
  const string DefaultOutputSubfolder = "GroupedGearAtlases";
  const string SkinGroupKey = "Skin";
  const string SkinFormName = "Skin";
  const string SkinVariantName = "All";
  const int DuplicateRebindWarningSampleLimit = 8;
  const int DefaultMaxAtlasSize = 2048;
  const int DefaultMaxSpritesPerAtlasPage = 1024;
  const int DefaultPadding = 0;
  const int DefaultAlphaThreshold = 1;
  const int DefaultNearWhiteThreshold = 250;
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

  DefaultAsset outputFolder;
  string outputName = "";
  List<Texture2D> sourceAtlases = new();
  bool showAutoFindSourceAtlases;
  string autoFindSourceAtlasKey = "";
  DefaultAsset autoFindSourceAtlasFolder;
  string autoFindSourceRootPath = "";
  DefaultAsset rebindSourceFolder;
  DefaultAsset rebindSpriteLibraryFolder;
  int maxAtlasSize = DefaultMaxAtlasSize;
  int maxSpritesPerAtlasPage = DefaultMaxSpritesPerAtlasPage;
  int padding = DefaultPadding;
  int alphaThreshold = DefaultAlphaThreshold;
  bool treatNearWhiteAsEmpty;
  int nearWhiteThreshold = DefaultNearWhiteThreshold;
  Vector2 scrollPosition;
  Vector2 resultsScrollPosition;
  string analyzedSelectionSignature = "";
  string analyzedSourceRootPath = "";
  List<GroupCandidate> scannedCandidates = new();

  [MenuItem("Tools/Authoring/Group Atlases")]
  static void ShowWindow() {
    GetWindow<GroupAtlasWindow>("Group Atlases");
  }

  void OnGUI() {
    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

    EditorGUILayout.LabelField("Group Esperanza Gear + Skin Atlases", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "Export and sprite-library rebinding are separate workflows. Export builds grouped atlases plus JSON metadata. Rebind uses those grouped outputs to update matching sprite-library entries later.",
      MessageType.Info);
    AtlasAuthoringLog.VerboseLoggingEnabled = EditorGUILayout.Toggle("Verbose Logging", AtlasAuthoringLog.VerboseLoggingEnabled);
    if (GUILayout.Button("Reset Tool")) {
      ResetWindowState();
    }

    EditorGUILayout.LabelField("Export Grouped Atlases", EditorStyles.boldLabel);
    EditorGUI.BeginChangeCheck();
    outputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", outputFolder, typeof(DefaultAsset), false);
    if (EditorGUI.EndChangeCheck()) {
      InvalidateScan();
    }

    EditorGUI.BeginChangeCheck();
    outputName = EditorGUILayout.DelayedTextField("Output Name", outputName ?? "");
    if (EditorGUI.EndChangeCheck()) {
      InvalidateScan();
    }
    DrawSourceAtlasSelection();
    maxAtlasSize = Mathf.Clamp(EditorGUILayout.DelayedIntField("Max Atlas Size", maxAtlasSize), 64, 2048);
    maxSpritesPerAtlasPage = Mathf.Clamp(EditorGUILayout.DelayedIntField("Max Sprites Per Page", maxSpritesPerAtlasPage), 64, 4096);
    padding = Mathf.Clamp(EditorGUILayout.DelayedIntField("Packing Padding", padding), 0, 64);
    alphaThreshold = Mathf.Clamp(EditorGUILayout.IntSlider("Alpha Threshold", alphaThreshold, 0, 255), 0, 255);
    treatNearWhiteAsEmpty = EditorGUILayout.Toggle("Treat Near-White As Empty", treatNearWhiteAsEmpty);
    using (new EditorGUI.DisabledScope(!treatNearWhiteAsEmpty)) {
      nearWhiteThreshold = Mathf.Clamp(EditorGUILayout.IntSlider("Near-White Threshold", nearWhiteThreshold, 0, 255), 0, 255);
    }
    EditorGUILayout.HelpBox("Grouped export packs color PNG atlases only. '_N' normal atlas outputs are skipped.", MessageType.None);

    using (new EditorGUI.DisabledScope(!HasValidSourceAtlasSelection() || outputFolder == null)) {
      using (new EditorGUILayout.HorizontalScope()) {
        if (GUILayout.Button("Analyze Selection")) {
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

  void DrawSourceAtlasSelection() {
    EditorGUILayout.LabelField("Source Atlases", EditorStyles.boldLabel);

    using (new EditorGUILayout.HorizontalScope()) {
      if (GUILayout.Button("Add Selected")) {
        AddSelectedSourceAtlases();
      }

      if (GUILayout.Button("Add Slot")) {
        sourceAtlases.Add(null);
        autoFindSourceRootPath = "";
        InvalidateScan();
      }

      if (GUILayout.Button("Auto Find")) {
        showAutoFindSourceAtlases = !showAutoFindSourceAtlases;
      }
    }

    if (showAutoFindSourceAtlases) {
      DrawAutoFindSourceAtlases();
    }

    if (sourceAtlases.Count <= 0) {
      EditorGUILayout.HelpBox("Add the sliced source atlas PNG assets to pack into this grouped output.", MessageType.None);
      return;
    }

    for (var i = 0; i < sourceAtlases.Count; i++) {
      using (new EditorGUILayout.VerticalScope()) {
        using (new EditorGUILayout.HorizontalScope()) {
          var atlas = sourceAtlases[i];
          var atlasLabel = "Atlas " + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

          if (atlas == null) {
            EditorGUI.BeginChangeCheck();
            sourceAtlases[i] = (Texture2D)EditorGUILayout.ObjectField(
              atlasLabel,
              sourceAtlases[i],
              typeof(Texture2D),
              false);
            if (EditorGUI.EndChangeCheck()) {
              autoFindSourceRootPath = "";
              InvalidateScan();
            }
          }
          else {
            var atlasPath = NormalizePath(AssetDatabase.GetAssetPath(atlas));
            EditorGUILayout.PrefixLabel(atlasLabel);
            EditorGUILayout.SelectableLabel(
              atlasPath,
              EditorStyles.textField,
              GUILayout.Height(EditorGUIUtility.singleLineHeight));
          }

          if (GUILayout.Button("Remove", GUILayout.Width(72f))) {
            sourceAtlases.RemoveAt(i);
            if (sourceAtlases.Count <= 0) {
              autoFindSourceRootPath = "";
            }
            InvalidateScan();
            GUIUtility.ExitGUI();
          }
        }

        var trackedCategory = ResolveTrackedSourceCategory(sourceAtlases[i]);
        if (!string.IsNullOrWhiteSpace(trackedCategory)) {
          EditorGUILayout.LabelField("Tracked *", trackedCategory);
        }
      }
    }
  }

  void DrawAutoFindSourceAtlases() {
    using (new EditorGUILayout.VerticalScope("box")) {
      autoFindSourceAtlasKey = EditorGUILayout.DelayedTextField("Find Key", autoFindSourceAtlasKey ?? "");
      autoFindSourceAtlasFolder = (DefaultAsset)EditorGUILayout.ObjectField(
        "Folder",
        autoFindSourceAtlasFolder,
        typeof(DefaultAsset),
        false);

      using (new EditorGUILayout.HorizontalScope()) {
        if (GUILayout.Button("Find")) {
          AddAutoFoundSourceAtlases();
        }

        if (GUILayout.Button("Close")) {
          showAutoFindSourceAtlases = false;
        }
      }
    }
  }

  void DrawScanResults() {
    EditorGUILayout.Space();
    EditorGUILayout.LabelField("Selection Results", EditorStyles.boldLabel);

    if (!HasValidSourceAtlasSelection()) {
      EditorGUILayout.HelpBox("Add one or more sliced Esperanza atlas PNG assets.", MessageType.None);
      return;
    }

    if (!HasFreshScan()) {
      EditorGUILayout.HelpBox("Click 'Analyze Selection' to preview the grouped atlas batch that this tool will export.", MessageType.None);
      return;
    }

    if (scannedCandidates == null || scannedCandidates.Count <= 0) {
      EditorGUILayout.HelpBox("No valid grouped atlas batch was built from the selected source atlases.", MessageType.Warning);
      return;
    }

    var totalAtlasCount = 0;
    var skinCandidateCount = 0;
    for (var i = 0; i < scannedCandidates.Count; i++) {
      var candidate = scannedCandidates[i];
      if (candidate == null) {
        continue;
      }

      totalAtlasCount += candidate.sourceAtlases?.Count ?? 0;
      if (IsSkinCandidate(candidate)) {
        skinCandidateCount++;
      }
    }
    EditorGUILayout.LabelField(
      "Summary",
      "batches=" + scannedCandidates.Count +
      ", skin_candidates=" + skinCandidateCount +
      ", matched_atlases=" + totalAtlasCount);

    using (var scroll = new EditorGUILayout.ScrollViewScope(resultsScrollPosition, GUILayout.Height(320f))) {
      resultsScrollPosition = scroll.scrollPosition;
      for (var i = 0; i < scannedCandidates.Count; i++) {
        var candidate = scannedCandidates[i];
        using (new EditorGUILayout.VerticalScope("box")) {
          EditorGUILayout.LabelField(BuildCandidateLabel(candidate), EditorStyles.boldLabel);
          EditorGUILayout.LabelField("Output Name", candidate.outputName ?? "");
          EditorGUILayout.LabelField("Source Atlases", candidate.sourceAtlases.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
          EditorGUILayout.LabelField(
            "Animations",
            candidate.sourceCategories.Count > 0 ? string.Join(", ", candidate.sourceCategories) : "(none)");
          EditorGUILayout.LabelField("Packed Format", "PNG");
        }
      }
    }
  }

  bool EnsureScanAvailable(out string sourceRootPath) {
    sourceRootPath = "";
    if (!TryGetOutputFolderPath(out _, true)) return false;
    if (!HasValidSourceAtlasSelection()) {
      EditorUtility.DisplayDialog("Missing Source Atlases", "Add at least one source atlas first.", "OK");
      return false;
    }
    if (HasFreshScan() && scannedCandidates != null) {
      sourceRootPath = analyzedSourceRootPath;
      return true;
    }

    AnalyzeFolder();
    sourceRootPath = analyzedSourceRootPath;
    return HasFreshScan();
  }

  bool HasFreshScan() {
    return scannedCandidates != null &&
           string.Equals(analyzedSelectionSignature, BuildSelectionSignature(), StringComparison.Ordinal);
  }

  void InvalidateScan() {
    scannedCandidates = new List<GroupCandidate>();
    analyzedSelectionSignature = "";
    analyzedSourceRootPath = "";
  }

  void ResetWindowState() {
    outputFolder = null;
    outputName = "";
    sourceAtlases = new List<Texture2D>();
    showAutoFindSourceAtlases = false;
    autoFindSourceAtlasKey = "";
    autoFindSourceAtlasFolder = null;
    autoFindSourceRootPath = "";
    rebindSourceFolder = null;
    rebindSpriteLibraryFolder = null;
    maxAtlasSize = DefaultMaxAtlasSize;
    maxSpritesPerAtlasPage = DefaultMaxSpritesPerAtlasPage;
    padding = DefaultPadding;
    alphaThreshold = DefaultAlphaThreshold;
    treatNearWhiteAsEmpty = false;
    nearWhiteThreshold = DefaultNearWhiteThreshold;
    scrollPosition = Vector2.zero;
    resultsScrollPosition = Vector2.zero;
    InvalidateScan();
  }

  bool TryGetOutputFolderPath(out string outputFolderPath, bool showDialog) {
    outputFolderPath = "";
    if (outputFolder == null) {
      if (showDialog) EditorUtility.DisplayDialog("Missing Output Folder", "Select an output folder first.", "OK");
      return false;
    }

    outputFolderPath = NormalizePath(AssetDatabase.GetAssetPath(outputFolder));
    if (AssetDatabase.IsValidFolder(outputFolderPath)) return true;
    if (showDialog) EditorUtility.DisplayDialog("Invalid Output Folder", "Could not resolve the selected output folder to a project asset path.", "OK");
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

  string GetSanitizedOutputName() {
    if (string.IsNullOrWhiteSpace(outputName)) return "";
    return SanitizeSubfolderName(outputName);
  }

  static string SanitizeSubfolderName(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";

    var invalidChars = System.IO.Path.GetInvalidFileNameChars();
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
    var outputLabel = string.IsNullOrWhiteSpace(candidate.outputName) ? "<unnamed>" : candidate.outputName;
    if (IsSkinCandidate(candidate)) {
      return outputLabel + " :: " + SkinFormName + "/" + candidate.partCode + " :: " + BuildCandidateAnimationSummary(candidate);
    }
    return outputLabel + " :: " + candidate.form + "/" + candidate.variant + "/" + candidate.partCode + " :: " + BuildCandidateAnimationSummary(candidate);
  }

  static bool IsSkinCandidate(GroupCandidate candidate) {
    return candidate != null && candidate.isSkin;
  }

  static bool IsSkinGroupKey(string groupKey) {
    return string.Equals(groupKey, SkinGroupKey, StringComparison.OrdinalIgnoreCase);
  }

  bool HasValidSourceAtlasSelection() {
    for (var i = 0; i < sourceAtlases.Count; i++) {
      if (sourceAtlases[i] != null) return true;
    }

    return false;
  }

  void AddSelectedSourceAtlases() {
    var selectedObjects = Selection.objects;
    if (selectedObjects == null || selectedObjects.Length <= 0) return;

    var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    CollectExistingSourceAtlasPaths(existingPaths);
    autoFindSourceRootPath = "";

    var addedAny = false;
    for (var i = 0; i < selectedObjects.Length; i++) {
      var texture = selectedObjects[i] as Texture2D;
      if (texture == null) continue;
      if (TryAddSourceAtlas(texture, existingPaths)) {
        addedAny = true;
      }
    }

    if (addedAny) {
      InvalidateScan();
    }
  }

  void AddAutoFoundSourceAtlases() {
    if (!TryBuildAutoFindFolderPaths(out var rootFolderPath, out var folderPaths, out var error)) {
      EditorUtility.DisplayDialog("Auto Find Failed", error, "OK");
      return;
    }

    var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    CollectExistingSourceAtlasPaths(existingPaths);

    var addedCount = 0;
    var jsonCount = 0;
    for (var folderIndex = 0; folderIndex < folderPaths.Count; folderIndex++) {
      var folderPath = folderPaths[folderIndex];
      var assetGuids = AssetDatabase.FindAssets("", new[] { folderPath });
      for (var i = 0; i < assetGuids.Length; i++) {
        var assetPath = NormalizePath(AssetDatabase.GUIDToAssetPath(assetGuids[i]));
        if (string.IsNullOrWhiteSpace(assetPath)) continue;

        var extension = System.IO.Path.GetExtension(assetPath);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)) {
          jsonCount++;
          continue;
        }

        if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)) continue;
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (TryAddSourceAtlas(texture, existingPaths, rootFolderPath)) {
          addedCount++;
        }
      }
    }

    if (addedCount > 0) {
      autoFindSourceRootPath = rootFolderPath;
      InvalidateScan();
    }

    AtlasAuthoringLog.Verbose(
      "[GearGroupAtlas] Auto Find complete." +
      " key='" + autoFindSourceAtlasKey + "'" +
      " root='" + rootFolderPath + "'" +
      " folders=" + folderPaths.Count +
      " added_png=" + addedCount +
      " found_json=" + jsonCount);
  }

  bool TryBuildAutoFindFolderPaths(out string rootFolderPath, out List<string> folderPaths, out string error) {
    rootFolderPath = "";
    folderPaths = new List<string>();
    error = "";

    rootFolderPath = autoFindSourceAtlasFolder == null
      ? ""
      : NormalizePath(AssetDatabase.GetAssetPath(autoFindSourceAtlasFolder));
    if (string.IsNullOrWhiteSpace(rootFolderPath) || !AssetDatabase.IsValidFolder(rootFolderPath)) {
      error = "Select a project folder first.";
      return false;
    }

    var hierarchies = BuildAutoFindHierarchies(autoFindSourceAtlasKey);
    if (hierarchies.Count <= 0) {
      error = "Enter a key like Aqua_aa_t or Skin_t.";
      return false;
    }

    for (var hierarchyIndex = 0; hierarchyIndex < hierarchies.Count; hierarchyIndex++) {
      AddAutoFindFolderPath(folderPaths, rootFolderPath, hierarchies[hierarchyIndex]);
    }

    var childFolders = AssetDatabase.GetSubFolders(rootFolderPath);
    for (var i = 0; i < childFolders.Length; i++) {
      var childFolderPath = NormalizePath(childFolders[i]);
      for (var hierarchyIndex = 0; hierarchyIndex < hierarchies.Count; hierarchyIndex++) {
        AddAutoFindFolderPath(folderPaths, childFolderPath, hierarchies[hierarchyIndex]);
      }
    }

    if (folderPaths.Count > 0) {
      return true;
    }

    error = "No matching folders found under: " + rootFolderPath;
    return false;
  }

  static List<string> BuildAutoFindHierarchies(string key) {
    var hierarchies = new List<string>();
    if (string.IsNullOrWhiteSpace(key)) return hierarchies;

    var tokens = BuildAutoFindTokens(key);
    if (tokens.Count <= 0) return hierarchies;

    AddAutoFindHierarchy(hierarchies, string.Join("/", tokens));
    AddAutoFindAlternateHierarchies(hierarchies, tokens);
    return hierarchies;
  }

  static void AddAutoFindFolderPath(List<string> folderPaths, string rootFolderPath, string hierarchy) {
    if (folderPaths == null) return;
    if (string.IsNullOrWhiteSpace(rootFolderPath)) return;
    if (string.IsNullOrWhiteSpace(hierarchy)) return;

    var folderPath = NormalizePath(rootFolderPath.TrimEnd('/') + "/" + hierarchy);
    if (!AssetDatabase.IsValidFolder(folderPath)) return;
    for (var i = 0; i < folderPaths.Count; i++) {
      if (string.Equals(folderPaths[i], folderPath, StringComparison.OrdinalIgnoreCase)) {
        return;
      }
    }

    folderPaths.Add(folderPath);
  }

  static void AddAutoFindAlternateHierarchies(List<string> hierarchies, List<string> tokens) {
    if (hierarchies == null || tokens == null) return;

    if (tokens.Count >= 3) {
      var descriptor = tokens[0] + "_" + tokens[1];
      var part = tokens[2];
      AddAutoFindHierarchy(hierarchies, part + "/" + descriptor);
    }

    if (tokens.Count == 2 &&
        string.Equals(tokens[0], SkinFormName, StringComparison.OrdinalIgnoreCase)) {
      AddAutoFindHierarchy(hierarchies, tokens[1] + "/" + tokens[0]);
      AddAutoFindHierarchy(hierarchies, tokens[1]);
    }
  }

  static void AddAutoFindHierarchy(List<string> hierarchies, string hierarchy) {
    if (hierarchies == null) return;
    if (string.IsNullOrWhiteSpace(hierarchy)) return;

    var normalizedHierarchy = NormalizePath(hierarchy).Trim('/');
    if (string.IsNullOrWhiteSpace(normalizedHierarchy)) return;
    for (var i = 0; i < hierarchies.Count; i++) {
      if (string.Equals(hierarchies[i], normalizedHierarchy, StringComparison.OrdinalIgnoreCase)) {
        return;
      }
    }

    hierarchies.Add(normalizedHierarchy);
  }

  static List<string> BuildAutoFindTokens(string key) {
    if (string.IsNullOrWhiteSpace(key)) return new List<string>();

    var tokens = key.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
    var result = new List<string>(tokens.Length);
    for (var i = 0; i < tokens.Length; i++) {
      var token = SanitizeSubfolderName((tokens[i] ?? "").Trim());
      if (!string.IsNullOrWhiteSpace(token)) {
        result.Add(token);
      }
    }

    return result;
  }

  void CollectExistingSourceAtlasPaths(HashSet<string> existingPaths) {
    if (existingPaths == null) return;

    for (var i = 0; i < sourceAtlases.Count; i++) {
      var atlas = sourceAtlases[i];
      if (atlas == null) continue;
      var atlasPath = NormalizePath(AssetDatabase.GetAssetPath(atlas));
      if (!string.IsNullOrWhiteSpace(atlasPath)) {
        existingPaths.Add(atlasPath);
      }
    }
  }

  bool TryAddSourceAtlas(Texture2D texture, HashSet<string> existingPaths, string sourceRootPath = "") {
    if (texture == null || existingPaths == null) return false;

    var texturePath = NormalizePath(AssetDatabase.GetAssetPath(texture));
    if (string.IsNullOrWhiteSpace(texturePath)) return false;
    if (!IsSupportedColorAtlas(texturePath)) return false;
    if (IsGeneratedNormalAtlasAssetPath(texturePath)) return false;
    if (!string.IsNullOrWhiteSpace(sourceRootPath) &&
        SpriteAtlasSourceFilter.HasIgnoredSubfolderInPath(sourceRootPath, texturePath)) return false;
    if (!existingPaths.Add(texturePath)) return false;

    sourceAtlases.Add(texture);
    return true;
  }

  string ResolveTrackedSourceCategory(Texture2D texture) {
    if (texture == null) return "";

    var texturePath = NormalizePath(AssetDatabase.GetAssetPath(texture));
    if (string.IsNullOrWhiteSpace(texturePath)) return "";

    var rootPath = NormalizePath(autoFindSourceRootPath).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(rootPath)) return "";

    if (!texturePath.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase)) {
      return "";
    }

    var relativePath = texturePath.Substring(rootPath.Length + 1);
    var segments = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length > 3) {
      return segments[0];
    }

    return System.IO.Path.GetFileName(rootPath);
  }

  string BuildSelectionSignature() {
    var sanitizedOutputName = GetSanitizedOutputName();
    var outputFolderPath = outputFolder == null ? "" : NormalizePath(AssetDatabase.GetAssetPath(outputFolder));
    var atlasPaths = new List<string>();
    for (var i = 0; i < sourceAtlases.Count; i++) {
      var atlas = sourceAtlases[i];
      if (atlas == null) continue;
      var atlasPath = NormalizePath(AssetDatabase.GetAssetPath(atlas));
      if (string.IsNullOrWhiteSpace(atlasPath)) continue;
      atlasPaths.Add(atlasPath);
    }

    atlasPaths.Sort(StringComparer.OrdinalIgnoreCase);
    return outputFolderPath + "|" + sanitizedOutputName + "|" + string.Join("|", atlasPaths);
  }
}
#endif
