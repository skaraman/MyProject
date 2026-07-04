#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class SpriteLibraryRebindWindow : EditorWindow {
  DefaultAsset spriteSheetFolder;
  Object spriteLibraryAsset;
  Vector2 scrollPosition;

  [MenuItem("Tools/Authoring/Sprite Library Rebinding")]
  static void ShowWindow() {
    GetWindow<SpriteLibraryRebindWindow>("Sprite Library Rebinding");
  }

  void OnGUI() {
    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

    EditorGUILayout.LabelField("Rebind One Sprite Library", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
      "Select one sprite library asset and one folder of grouped sprite sheets. The folder must contain the grouped PNG assets plus their JSON metadata. Rebinding matches library categories and labels against the sliced replacement sprites found there.",
      MessageType.Info);

    spriteLibraryAsset = EditorGUILayout.ObjectField(
      "Sprite Library",
      spriteLibraryAsset,
      typeof(Object),
      false);
    spriteSheetFolder = (DefaultAsset)EditorGUILayout.ObjectField(
      "Sprite Sheet Folder",
      spriteSheetFolder,
      typeof(DefaultAsset),
      false);

    if (spriteLibraryAsset != null && string.IsNullOrWhiteSpace(ResolveSpriteLibraryPath())) {
      EditorGUILayout.HelpBox("Sprite Library must be a .spriteLib or .spriteSheetLib asset.", MessageType.Warning);
    }

    if (spriteSheetFolder != null && string.IsNullOrWhiteSpace(ResolveSpriteSheetFolderPath())) {
      EditorGUILayout.HelpBox("Sprite Sheet Folder must be a project folder asset.", MessageType.Warning);
    }

    using (new EditorGUI.DisabledScope(
      string.IsNullOrWhiteSpace(ResolveSpriteLibraryPath()) ||
      string.IsNullOrWhiteSpace(ResolveSpriteSheetFolderPath()))) {
      if (GUILayout.Button("Rebind Sprite Library")) {
        RebindSpriteLibrary();
      }
    }

    EditorGUILayout.EndScrollView();
  }

  static string NormalizePath(string assetPath) {
    if (string.IsNullOrWhiteSpace(assetPath)) return "";
    return assetPath.Replace("\\", "/").Trim();
  }

  string ResolveSpriteLibraryPath() {
    if (spriteLibraryAsset == null) return "";

    var assetPath = NormalizePath(AssetDatabase.GetAssetPath(spriteLibraryAsset));
    if (string.IsNullOrWhiteSpace(assetPath)) return "";

    var extension = Path.GetExtension(assetPath);
    if (string.Equals(extension, ".spriteLib", System.StringComparison.OrdinalIgnoreCase)) return assetPath;
    if (string.Equals(extension, ".spriteSheetLib", System.StringComparison.OrdinalIgnoreCase)) return assetPath;
    return "";
  }

  string ResolveSpriteSheetFolderPath() {
    if (spriteSheetFolder == null) return "";

    var folderPath = NormalizePath(AssetDatabase.GetAssetPath(spriteSheetFolder));
    return AssetDatabase.IsValidFolder(folderPath) ? folderPath : "";
  }

  void RebindSpriteLibrary() {
    var libraryPath = ResolveSpriteLibraryPath();
    if (string.IsNullOrWhiteSpace(libraryPath)) {
      EditorUtility.DisplayDialog("Invalid Sprite Library", "Select one sprite library asset to update.", "OK");
      return;
    }

    var sourceFolderPath = ResolveSpriteSheetFolderPath();
    if (string.IsNullOrWhiteSpace(sourceFolderPath)) {
      EditorUtility.DisplayDialog("Invalid Sprite Sheet Folder", "Select a project folder that contains the replacement grouped sprite sheets and JSON metadata.", "OK");
      return;
    }

    if (!GroupAtlasWindow.TryRunSpriteLibraryRebind(sourceFolderPath, libraryPath, out var error)) {
      EditorUtility.DisplayDialog("Rebind Failed", error, "OK");
    }
  }
}
#endif
