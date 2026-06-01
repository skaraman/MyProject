#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class EsperanzaGearGroupAtlasWindow : EditorWindow {
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

  [MenuItem("Tools/Authoring/Group Esperanza Gear Atlases")]
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
          EditorGUILayout.LabelField("Source Atlases", candidate.sourceAtlases.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
          EditorGUILayout.LabelField(
            "Animations",
            candidate.sourceCategories.Count > 0 ? string.Join(", ", candidate.sourceCategories) : "(none)");
          EditorGUILayout.LabelField("Packed Format", "PNG");
        }
      }
    }
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

  string GetSanitizedOutputSubfolderName() {
    if (string.IsNullOrWhiteSpace(outputSubfolderName)) return "";
    return SanitizeSubfolderName(outputSubfolderName);
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
}
#endif
