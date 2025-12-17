using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class SpriteLibraryBulkImporterDuplicate : EditorWindow {
  public enum ImageType { Png, Jpg }

  SpriteLibraryAsset targetLibrary;
  DefaultAsset scanRootFolder;

  ImageType imageType = ImageType.Png;
  string categoryName = "";
  string labelPrefix = "";
  bool useFirstSubfolderAsPrefix = true;
  bool autoFindNextIndex = true;
  int startingN = 1;

  [MenuItem("Tools/Sprite Library/Bulk Import (Duplicate .asset)")]
  public static void ShowWindow() {
    var w = GetWindow<SpriteLibraryBulkImporterDuplicate>();
    w.titleContent = new GUIContent("SpriteLib Import (Dup)");
    w.minSize = new Vector2(560, 280);
  }

  void OnGUI() {
    EditorGUILayout.Space();
    targetLibrary = (SpriteLibraryAsset)EditorGUILayout.ObjectField("SpriteLibraryAsset", targetLibrary, typeof(SpriteLibraryAsset), false);
    scanRootFolder = (DefaultAsset)EditorGUILayout.ObjectField("Scan Root Folder", scanRootFolder, typeof(DefaultAsset), false);

    EditorGUILayout.Space();
    imageType = (ImageType)EditorGUILayout.EnumPopup("Image Type", imageType);
    categoryName = EditorGUILayout.TextField("Category Name", categoryName);
    labelPrefix = EditorGUILayout.TextField("Base Label Prefix (can be empty)", labelPrefix);
    useFirstSubfolderAsPrefix = EditorGUILayout.ToggleLeft("Use 1st Subfolder Name as Prefix (BasePrefix_Subfolder)", useFirstSubfolderAsPrefix);

    EditorGUILayout.Space();
    autoFindNextIndex = EditorGUILayout.ToggleLeft("Auto Find Next N From Existing Labels", autoFindNextIndex);
    using (new EditorGUI.DisabledScope(autoFindNextIndex)) {
      startingN = EditorGUILayout.IntField("Starting N", Mathf.Max(1, startingN));
    }

    EditorGUILayout.Space();
    if (GUILayout.Button("Import (Create Duplicate .asset)")) {
      if (targetLibrary == null || scanRootFolder == null) {
        EditorUtility.DisplayDialog("Error", "Assign a SpriteLibraryAsset and a folder.", "OK");
        return;
      }
      if (string.IsNullOrWhiteSpace(categoryName)) {
        EditorUtility.DisplayDialog("Error", "Category Name is required.", "OK");
        return;
      }
      var rootPath = AssetDatabase.GetAssetPath(scanRootFolder);
      if (!AssetDatabase.IsValidFolder(rootPath)) {
        EditorUtility.DisplayDialog("Error", "Invalid folder selected.", "OK");
        return;
      }

      var cat = categoryName.Trim();
      var prefix = (labelPrefix ?? "").Trim();
      var type = imageType;
      var auto = autoFindNextIndex;
      var start = Mathf.Max(1, startingN);
      var useSub = useFirstSubfolderAsPrefix;

      EditorApplication.delayCall += () => {
        try {
          DuplicateAndImport(rootPath, targetLibrary, cat, prefix, type, useSub, auto, start);
        }
        catch (System.Exception e) {
          EditorUtility.ClearProgressBar();
          Debug.LogError($"Import failed: {e.Message}\n{e.StackTrace}");
          EditorUtility.DisplayDialog("Error", $"Import failed: {e.Message}", "OK");
        }
      };
    }
  }

  static void DuplicateAndImport(string rootAssetPath, SpriteLibraryAsset sourceLibrary, string category, string basePrefix, ImageType type, bool useSubfolderPrefix, bool autoNext, int startN) {
    try {
      var originalPath = AssetDatabase.GetAssetPath(sourceLibrary);
      if (string.IsNullOrEmpty(originalPath)) {
        EditorUtility.DisplayDialog("Error", "Could not locate path of SpriteLibraryAsset.", "OK");
        return;
      }

      var newPath = Path.ChangeExtension(originalPath, ".asset");
      if (string.IsNullOrEmpty(newPath)) newPath = originalPath;

      var newLibrary = ScriptableObject.CreateInstance<SpriteLibraryAsset>();
      newLibrary.hideFlags = HideFlags.DontUnloadUnusedAsset;

      EditorUtility.DisplayProgressBar("Copying Library", "Copying existing categories...", 0.05f);
      var cats = new List<string>();
      try { cats = sourceLibrary.GetCategoryNames().ToList(); } catch { }

      for (int i = 0; i < cats.Count; i++) {
        var c = cats[i];
        var labels = new List<string>();
        try { labels = sourceLibrary.GetCategoryLabelNames(c).ToList(); } catch { }
        for (int j = 0; j < labels.Count; j++) {
          var l = labels[j];
          var sp = sourceLibrary.GetSprite(c, l);
          if (sp != null) newLibrary.AddCategoryLabel(sp, c, l);
        }
      }

      var absRoot = Path.Combine(Directory.GetParent(Application.dataPath).FullName, rootAssetPath);
      if (!Directory.Exists(absRoot)) {
        newLibrary.hideFlags = HideFlags.None;
        EditorUtility.UnloadUnusedAssetsImmediate();
        EditorUtility.DisplayDialog("Error", $"Folder not found:\n{absRoot}", "OK");
        return;
      }

      var exts = type == ImageType.Png ? new HashSet<string> { ".png" } : new HashSet<string> { ".jpg", ".jpeg" };
      var files = Directory.GetFiles(absRoot, "*.*", SearchOption.AllDirectories)
        .Where(p => exts.Contains(Path.GetExtension(p).ToLowerInvariant()))
        .OrderBy(p => p)
        .ToArray();

      if (files.Length == 0) {
        newLibrary.hideFlags = HideFlags.None;
        EditorUtility.UnloadUnusedAssetsImmediate();
        EditorUtility.DisplayDialog("Nothing Found", $"No {type} files found under:\n{rootAssetPath}", "OK");
        return;
      }

      var existingLabels = new HashSet<string>();
      try {
        foreach (var l in newLibrary.GetCategoryLabelNames(category)) existingLabels.Add(l);
      }
      catch { }

      var nextNByPrefix = new Dictionary<string, int>();
      var added = 0;
      var filesNoSprites = 0;

      for (int i = 0; i < files.Length; i++) {
        var file = files[i];
        EditorUtility.DisplayProgressBar("Importing", $"{i + 1}/{files.Length} {Path.GetFileName(file)}", 0.15f + (0.8f * (i + 1) / files.Length));

        var assetPath = ToAssetPath(file);
        if (string.IsNullOrEmpty(assetPath)) continue;

        var sprites = LoadSprites(assetPath);
        if (sprites.Count == 0) {
          filesNoSprites++;
          continue;
        }

        var sub = useSubfolderPrefix ? GetFirstSubfolderName(absRoot, file) : "";
        var effectivePrefix = BuildEffectivePrefix(basePrefix, sub, useSubfolderPrefix);

        if (!nextNByPrefix.TryGetValue(effectivePrefix, out var n)) {
          n = Mathf.Max(1, startN);
          if (autoNext) {
            var max = TryFindMaxIndex(existingLabels, effectivePrefix);
            if (max >= 0) n = max + 1;
          }
          nextNByPrefix[effectivePrefix] = n;
        }

        for (int s = 0; s < sprites.Count; s++) {
          var sp = sprites[s];
          if (sp == null) continue;

          n = nextNByPrefix[effectivePrefix];
          var label = BuildLabel(effectivePrefix, n);
          while (existingLabels.Contains(label)) {
            n++;
            label = BuildLabel(effectivePrefix, n);
          }

          newLibrary.AddCategoryLabel(sp, category, label);
          existingLabels.Add(label);

          n++;
          nextNByPrefix[effectivePrefix] = n;
          added++;
        }
      }

      EditorUtility.DisplayProgressBar("Saving", "Replacing asset...", 0.98f);

      AssetDatabase.DeleteAsset(originalPath);
      if (originalPath != newPath) AssetDatabase.DeleteAsset(newPath);

      AssetDatabase.CreateAsset(newLibrary, newPath);
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();

      newLibrary.hideFlags = HideFlags.None;
      EditorUtility.UnloadUnusedAssetsImmediate();
      System.GC.Collect();

      var msg = $"Created SpriteLibraryAsset:\n{newPath}\n\nCategory: {category}\nAdded labels: {added}";
      if (filesNoSprites > 0) msg += $"\nFiles with no sprites: {filesNoSprites}";
      EditorUtility.DisplayDialog("Done", msg, "OK");
      Debug.Log($"[SpriteLibraryBulkImporterDuplicate] {msg}");
    }
    finally {
      EditorUtility.ClearProgressBar();
    }
  }

  static string BuildEffectivePrefix(string basePrefix, string sub, bool useSubfolderPrefix) {
    var bp = (basePrefix ?? "").Trim();
    if (!useSubfolderPrefix) return bp;
    if (string.IsNullOrEmpty(sub)) return bp;
    if (string.IsNullOrEmpty(bp)) return sub;
    return $"{bp}_{sub}";
  }


  static string GetFirstSubfolderName(string absRoot, string absFile) {
    var rel = Path.GetRelativePath(absRoot, absFile).Replace("\\", "/");
    var parts = rel.Split('/');
    if (parts.Length <= 1) return "";
    return parts[0];
  }

  static string BuildLabel(string prefix, int n) {
    if (string.IsNullOrEmpty(prefix)) return n.ToString();
    return $"{prefix}_{n}";
  }

  static int TryFindMaxIndex(HashSet<string> labels, string prefix) {
    var max = -1;

    if (string.IsNullOrEmpty(prefix)) {
      foreach (var l in labels) {
        if (int.TryParse(l, out var v) && v > max) max = v;
      }
      return max;
    }

    var head = prefix + "_";
    foreach (var l in labels) {
      if (!l.StartsWith(head)) continue;
      var tail = l.Substring(head.Length);
      if (int.TryParse(tail, out var v) && v > max) max = v;
    }
    return max;
  }

  static string ToAssetPath(string absFilePath) {
    var p = absFilePath.Replace("\\", "/");
    var data = Application.dataPath.Replace("\\", "/");
    if (!p.StartsWith(data)) return null;
    return "Assets" + p.Substring(data.Length);
  }

  static List<Sprite> LoadSprites(string assetPath) {
    var results = new List<Sprite>();
    var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
    if (assets == null || assets.Length == 0) return results;

    var dict = new SortedDictionary<int, Sprite>();
    for (int i = 0; i < assets.Length; i++) {
      if (assets[i] is not Sprite sp) continue;

      var parts = sp.name.Split('_');
      if (parts.Length >= 2 && int.TryParse(parts[^1], out var idx)) dict[idx] = sp;
      else results.Add(sp);
    }

    foreach (var kv in dict) results.Add(kv.Value);
    return results;
  }
}
