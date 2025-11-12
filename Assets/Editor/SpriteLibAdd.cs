using UnityEngine;
using UnityEditor;
using UnityEngine.U2D.Animation;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class SpriteLibraryImporterOverwrite : EditorWindow {
  SpriteLibraryAsset originalLibrary;
  DefaultAsset selectedFolder;
  string targetSubfolder = "fL";
  string fileName = "1.png";

  [MenuItem("Tools/Import Into Sprite Library (Overwrite)")]
  public static void ShowWindow() {
    var window = GetWindow<SpriteLibraryImporterOverwrite>();
    window.titleContent = new GUIContent("Overwrite Sprite Library");
    window.minSize = new Vector2(400, 150);
  }

  void OnGUI() {
    EditorGUILayout.LabelField("Example: Assets/Sprites/Characters/Ana/Run/Aqua/aa/fL/1.png", EditorStyles.helpBox);
    EditorGUILayout.Space();
    
    originalLibrary = (SpriteLibraryAsset)EditorGUILayout.ObjectField("Target SpriteLibrary", originalLibrary, typeof(SpriteLibraryAsset), false);
    selectedFolder = (DefaultAsset)EditorGUILayout.ObjectField("Scan Root Folder", selectedFolder, typeof(DefaultAsset), false);
    fileName = EditorGUILayout.TextField("Texture File Name", fileName);
    targetSubfolder = EditorGUILayout.TextField("Target Folder Name", targetSubfolder);

    EditorGUILayout.Space();
    
    if (GUILayout.Button("Overwrite Library with New Labels")) {
      if (originalLibrary == null || selectedFolder == null) {
        EditorUtility.DisplayDialog("Error", "Must assign both a SpriteLibraryAsset and folder.", "OK");
        return;
      }

      string rootPath = AssetDatabase.GetAssetPath(selectedFolder);
      if (!AssetDatabase.IsValidFolder(rootPath)) {
        EditorUtility.DisplayDialog("Error", "Invalid folder selected.", "OK");
        return;
      }

      // Use EditorApplication.delayCall to avoid GUI layout issues
      EditorApplication.delayCall += () => {
        try {
          OverwriteLibrary(rootPath, originalLibrary);
        } catch (System.Exception e) {
          EditorUtility.ClearProgressBar();
          Debug.LogError($"Error during import: {e.Message}\n{e.StackTrace}");
          EditorUtility.DisplayDialog("Error", $"Import failed: {e.Message}", "OK");
        }
      };
    }
  }

  void OverwriteLibrary(string rootPath, SpriteLibraryAsset sourceLibrary) {
    try {
      string originalPath = AssetDatabase.GetAssetPath(sourceLibrary);
      if (string.IsNullOrEmpty(originalPath)) {
        Debug.LogError("Could not locate path of source SpriteLibraryAsset.");
        return;
      }

      // Get the category name from the root folder (e.g., "Run" from ".../Ana/Run")
      string category = Path.GetFileName(rootPath.TrimEnd('/', '\\'));
      Debug.Log($"Category will be: '{category}'");

      // Create a NEW library and protect from unloading
      var newLibrary = ScriptableObject.CreateInstance<SpriteLibraryAsset>();
      newLibrary.hideFlags = HideFlags.DontUnloadUnusedAsset;

      // Copy ALL existing categories from the source library
      EditorUtility.DisplayProgressBar("Copying Library", "Copying existing categories...", 0.1f);
      var existingCategories = sourceLibrary.GetCategoryNames().ToList();
      Debug.Log($"Copying {existingCategories.Count} existing categories from source library...");
      
      foreach (var cat in existingCategories) {
          var labels = sourceLibrary.GetCategoryLabelNames(cat).ToList();
          foreach (var lbl in labels) {
              var sprite = sourceLibrary.GetSprite(cat, lbl);
              if (sprite != null) {
                  newLibrary.AddCategoryLabel(sprite, cat, lbl);
              }
          }
          Debug.Log($"  Copied category '{cat}' with {labels.Count} labels");
      }

      // Find all target folders
      var absoluteRoot = Path.Combine(Directory.GetParent(Application.dataPath).FullName, rootPath);
      
      if (!Directory.Exists(absoluteRoot)) {
        Debug.LogError($"Root directory does not exist: {absoluteRoot}");
        return;
      }

      var folders = Directory.GetDirectories(absoluteRoot, targetSubfolder, SearchOption.AllDirectories);
      Debug.Log($"Found {folders.Length} '{targetSubfolder}' folders under {rootPath}");

      // Sort folders alphabetically to process them in consistent order
      folders = folders.OrderBy(f => f).ToArray();

      // Collect all sprites to add
      var spritesToAdd = new List<(Sprite sprite, string label)>();

      for (int i = 0; i < folders.Length; i++) {
        var folder = folders[i];
        EditorUtility.DisplayProgressBar("Scanning Sprites", 
          $"Scanning {Path.GetFileName(folder)} ({i + 1}/{folders.Length})", 
          0.3f + (0.4f * i / folders.Length));

        // Find files based on fileName field
        string[] filesToProcess;
        
        // Check if fileName is just an extension (no dot, no numbers)
        bool isExtensionOnly = !fileName.Contains(".") && 
                               !char.IsDigit(fileName[0]) && 
                               fileName.Length <= 4;
        
        if (isExtensionOnly) {
          // Search for all files containing this extension
          var allFiles = Directory.GetFiles(folder);
          filesToProcess = allFiles
            .Where(f => f.Contains(fileName, System.StringComparison.OrdinalIgnoreCase))
            .ToArray();
          
          // Sort numerically by filename
          filesToProcess = filesToProcess
            .OrderBy(f => {
              var name = Path.GetFileNameWithoutExtension(f);
              return int.TryParse(name, out int num) ? num : int.MaxValue;
            })
            .ToArray();
        } else {
          // Original behavior: look for specific filename
          var expectedFile = Path.Combine(folder, fileName);
          if (!File.Exists(expectedFile)) {
            Debug.LogWarning($"File not found: {expectedFile}");
            continue;
          }
          filesToProcess = new[] { expectedFile };
        }

        // Parse folder hierarchy once for this folder
        var folderInfo = new DirectoryInfo(folder);
        var parentFolder = folderInfo.Parent;
        var grandParentFolder = parentFolder?.Parent;

        if (parentFolder == null || grandParentFolder == null) {
          Debug.LogWarning($"Could not determine folder hierarchy for: {folder}");
          continue;
        }

        var aqua = grandParentFolder.Name;
        var aa = parentFolder.Name;

        // Reset label counter for each new folder
        int labelCounter = 1;

        // Process all files in this folder
        foreach (var filePath in filesToProcess) {
          // Convert absolute path to Unity asset path
          var localAssetPath = filePath.Replace("\\", "/");
          var dataPath = Application.dataPath.Replace("\\", "/");
          
          if (localAssetPath.StartsWith(dataPath)) {
            localAssetPath = "Assets" + localAssetPath.Substring(dataPath.Length);
          }
          var sprites = LoadAllSpritesAtPath(localAssetPath);
          
          if (sprites.Count == 0) {
            Debug.LogWarning($"No sprites found at: {localAssetPath}");
            continue;
          }

          Debug.Log($"Processing: {aqua}/{aa}/{targetSubfolder}/{Path.GetFileName(filePath)} - Found {sprites.Count} sprites");

          // Add each sprite with its label
          for (int s = 0; s < sprites.Count; s++) {
            var sprite = sprites[s];
            if (sprite == null) continue;

            var label = $"{aqua}_{aa}_{labelCounter}";
            spritesToAdd.Add((sprite, label));
            labelCounter++;
          }
        }
      }

      if (spritesToAdd.Count == 0) {
        Debug.LogWarning("No sprites were found to add!");
        newLibrary.hideFlags = HideFlags.None;
        EditorUtility.UnloadUnusedAssetsImmediate();
        EditorUtility.DisplayDialog("Warning", "No sprites found matching the criteria.", "OK");
        return;
      }

      // Remove old category labels if they exist in the new library
      EditorUtility.DisplayProgressBar("Updating Library", "Removing old category...", 0.8f);
      var existingLabelsInCategory = newLibrary.GetCategoryLabelNames(category)?.ToList();
      if (existingLabelsInCategory != null && existingLabelsInCategory.Count > 0) {
        Debug.Log($"Removing {existingLabelsInCategory.Count} existing labels from category '{category}'");
        foreach (var lbl in existingLabelsInCategory) {
          newLibrary.RemoveCategoryLabel(category, lbl, false);
        }
      }

      // Add all new sprites to the new category
      EditorUtility.DisplayProgressBar("Updating Library", "Adding new sprites...", 0.9f);
      
      int spriteCount = 0;
      foreach (var (sprite, label) in spritesToAdd) {
        try {
          newLibrary.AddCategoryLabel(sprite, category, label);
          spriteCount++;
        } catch (System.Exception e) {
          Debug.LogError($"Failed to add sprite '{sprite.name}' with label '{label}': {e.Message}");
        }
      }

      // VERIFY THE LIBRARY HAS DATA BEFORE SAVING
      Debug.Log("=== VERIFYING LIBRARY CONTENTS ===");
      var finalCategories = newLibrary.GetCategoryNames().ToList();
      Debug.Log($"Total categories in library: {finalCategories.Count}");
      foreach (var cat in finalCategories) {
        var labelCount = newLibrary.GetCategoryLabelNames(cat).Count();
        Debug.Log($"  Category '{cat}': {labelCount} labels");
      }
      Debug.Log("=================================");

      // Delete the old asset and create the new one as .asset (not .spriteLib)
      AssetDatabase.DeleteAsset(originalPath);
      
      // Change extension to .asset
      string newPath = Path.ChangeExtension(originalPath, ".asset");
      
      AssetDatabase.CreateAsset(newLibrary, newPath);
      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();

      // Cleanup
      newLibrary.hideFlags = HideFlags.None;
      EditorUtility.UnloadUnusedAssetsImmediate();
      System.GC.Collect();

      Debug.Log($"✔️ Successfully created SpriteLibraryAsset at: {newPath}");
      Debug.Log($"📊 Added {spriteCount} sprites to category '{category}'");
      
      EditorUtility.DisplayDialog("Success", 
        $"Created library with {spriteCount} sprites in category '{category}'\nSaved to: {newPath}", 
        "OK");
    } finally {
      EditorUtility.ClearProgressBar();
    }
  }

  List<Sprite> LoadAllSpritesAtPath(string assetPath) {
    var results = new List<Sprite>();
    var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
    
    // Use a sorted dictionary to order sprites by their frame index
    var spriteDict = new SortedDictionary<int, Sprite>();
    
    foreach (var a in assets) {
      if (a is Sprite sprite) {
        // Parse sprite name: "SheetNum_FrameIndex" (e.g., "1_23")
        var nameParts = sprite.name.Split('_');
        if (nameParts.Length >= 2 && int.TryParse(nameParts[1], out int frameIndex)) {
          spriteDict[frameIndex] = sprite;
        } else {
          // Fallback: add without sorting if name doesn't match pattern
          results.Add(sprite);
        }
      }
    }
    
    // Add sorted sprites to results
    foreach (var kvp in spriteDict) {
      results.Add(kvp.Value);
    }
    
    return results;
  }
}