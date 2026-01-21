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
  int selectedCategoryIndex;
  string selectedCategoryName = string.Empty;

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
    var categoryNames = new List<string>();
    if (originalLibrary != null) {
      categoryNames = originalLibrary.GetCategoryNames().OrderBy(n => n).ToList();
    }

    if (categoryNames.Count > 0) {
      if (selectedCategoryIndex >= categoryNames.Count) {
        selectedCategoryIndex = 0;
      }
      selectedCategoryIndex = EditorGUILayout.Popup("Target Category", selectedCategoryIndex, categoryNames.ToArray());
      selectedCategoryName = categoryNames[selectedCategoryIndex];
    } else {
      EditorGUILayout.Popup("Target Category", 0, new[] { "<no categories>" });
      selectedCategoryName = string.Empty;
    }

    selectedFolder = (DefaultAsset)EditorGUILayout.ObjectField("Scan Root Folder", selectedFolder, typeof(DefaultAsset), false);
    fileName = EditorGUILayout.TextField("Texture File Name", fileName);
    targetSubfolder = EditorGUILayout.TextField("Target Folder Name", targetSubfolder);

    EditorGUILayout.Space();
    
    if (GUILayout.Button("Overwrite Library with New Labels")) {
      if (originalLibrary == null || selectedFolder == null) {
        EditorUtility.DisplayDialog("Error", "Must assign both a SpriteLibraryAsset and folder.", "OK");
        return;
      }

      var library = originalLibrary;
      var categoryName = selectedCategoryName;
      string rootPath = AssetDatabase.GetAssetPath(selectedFolder);
      if (!AssetDatabase.IsValidFolder(rootPath)) {
        EditorUtility.DisplayDialog("Error", "Invalid folder selected.", "OK");
        return;
      }

      // Use EditorApplication.delayCall to avoid GUI layout issues
      EditorApplication.delayCall += () => {
        try {
          OverwriteLibrary(rootPath, library, categoryName);
        } catch (System.Exception e) {
          EditorUtility.ClearProgressBar();
          Debug.LogError($"Error during import: {e.Message}\n{e.StackTrace}");
          EditorUtility.DisplayDialog("Error", $"Import failed: {e.Message}", "OK");
        }
      };
    }
  }

  void OverwriteLibrary(string rootPath, SpriteLibraryAsset sourceLibrary, string targetCategoryName) {
    try {
      string originalPath = AssetDatabase.GetAssetPath(sourceLibrary);
      if (string.IsNullOrEmpty(originalPath)) {
        Debug.LogError("Could not locate path of source SpriteLibraryAsset.");
        return;
      }

      // Use selected category if provided, otherwise fall back to root folder name.
      string category = string.IsNullOrEmpty(targetCategoryName)
        ? Path.GetFileName(rootPath.TrimEnd('/', '\\'))
        : targetCategoryName;
      Debug.Log($"Category will be: '{category}'");

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
          var baseName = Path.GetFileNameWithoutExtension(fileName);
          var extension = Path.GetExtension(fileName);
          int startIndex = 0;
          var hasNumericBase = !string.IsNullOrEmpty(extension) && int.TryParse(baseName, out startIndex);

          if (hasNumericBase) {
            // Chain sequential files: 1.png, 2.png, 3.png, ...
            var chainedFiles = new List<string>();
            int currentIndex = startIndex;
            while (true) {
              var candidate = Path.Combine(folder, $"{currentIndex}{extension}");
              if (!File.Exists(candidate)) {
                break;
              }
              chainedFiles.Add(candidate);
              currentIndex++;
            }

            if (chainedFiles.Count == 0) {
              Debug.LogWarning($"File not found: {Path.Combine(folder, fileName)}");
              continue;
            }
            filesToProcess = chainedFiles.ToArray();
          } else {
            // Original behavior: look for specific filename
            var expectedFile = Path.Combine(folder, fileName);
            if (!File.Exists(expectedFile)) {
              Debug.LogWarning($"File not found: {expectedFile}");
              continue;
            }
            filesToProcess = new[] { expectedFile };
          }
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
        EditorUtility.DisplayDialog("Warning", "No sprites found matching the criteria.", "OK");
        return;
      }

      EditorUtility.DisplayProgressBar("Updating Library", "Updating category...", 0.85f);
      int spriteCount = spritesToAdd.Count;

      if (!TryOverwriteSerializedCategory(sourceLibrary, category, spritesToAdd)) {
        // Fallback for assets that do not expose SpriteLibrarySourceAsset data.
        var existingLabelsInCategory = sourceLibrary.GetCategoryLabelNames(category)?.ToList();
        if (existingLabelsInCategory != null && existingLabelsInCategory.Count > 0) {
          Debug.Log($"Removing {existingLabelsInCategory.Count} existing labels from category '{category}'");
          foreach (var lbl in existingLabelsInCategory) {
            sourceLibrary.RemoveCategoryLabel(category, lbl, false);
          }
        }

        spriteCount = 0;
        foreach (var (sprite, label) in spritesToAdd) {
          try {
            sourceLibrary.AddCategoryLabel(sprite, category, label);
            spriteCount++;
          } catch (System.Exception e) {
            Debug.LogError($"Failed to add sprite '{sprite.name}' with label '{label}': {e.Message}");
          }
        }
      }

      // VERIFY THE LIBRARY HAS DATA BEFORE SAVING
      Debug.Log("=== VERIFYING LIBRARY CONTENTS ===");
      var finalCategories = sourceLibrary.GetCategoryNames().ToList();
      Debug.Log($"Total categories in library: {finalCategories.Count}");
      foreach (var cat in finalCategories) {
        var labelCount = sourceLibrary.GetCategoryLabelNames(cat).Count();
        Debug.Log($"  Category '{cat}': {labelCount} labels");
      }
      Debug.Log("=================================");

      EditorUtility.SetDirty(sourceLibrary);
      AssetDatabase.SaveAssets();
      AssetDatabase.ImportAsset(originalPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
      AssetDatabase.Refresh();

      Debug.Log($"Successfully updated SpriteLibraryAsset at: {originalPath}");
      Debug.Log($"Added {spriteCount} sprites to category '{category}'");
      
      EditorUtility.DisplayDialog("Success", 
        $"Updated category '{category}' with {spriteCount} sprites\nAsset: {originalPath}", 
        "OK");
    } finally {
      EditorUtility.ClearProgressBar();
    }
  }

  bool TryOverwriteSerializedCategory(SpriteLibraryAsset sourceLibrary, string category, List<(Sprite sprite, string label)> spritesToAdd) {
    var so = new SerializedObject(sourceLibrary);
    so.Update();
    var libraryProp = so.FindProperty("m_Library");
    if (libraryProp == null || !libraryProp.isArray) return false;

    SerializedProperty categoryProp = null;
    for (int i = 0; i < libraryProp.arraySize; i++) {
      var element = libraryProp.GetArrayElementAtIndex(i);
      var nameProp = element.FindPropertyRelative("m_Name");
      if (nameProp != null && nameProp.stringValue == category) {
        categoryProp = element;
        break;
      }
    }

    if (categoryProp == null) {
      libraryProp.arraySize++;
      categoryProp = libraryProp.GetArrayElementAtIndex(libraryProp.arraySize - 1);
    }

    var categoryNameProp = categoryProp.FindPropertyRelative("m_Name");
    if (categoryNameProp != null) categoryNameProp.stringValue = category;
    var categoryHashProp = categoryProp.FindPropertyRelative("m_Hash");
    if (categoryHashProp != null) categoryHashProp.intValue = Animator.StringToHash(category);
    var fromMainProp = categoryProp.FindPropertyRelative("m_FromMain");
    if (fromMainProp != null) fromMainProp.intValue = 0;

    var overrideEntriesProp = categoryProp.FindPropertyRelative("m_OverrideEntries");
    var categoryListProp = categoryProp.FindPropertyRelative("m_CategoryList");
    bool useOverrides = overrideEntriesProp != null && overrideEntriesProp.isArray;
    var targetList = useOverrides ? overrideEntriesProp : categoryListProp;
    if (targetList == null || !targetList.isArray) return false;

    ClearSerializedArray(targetList);
    if (useOverrides && categoryListProp != null && categoryListProp.isArray) {
      ClearSerializedArray(categoryListProp);
    }
    if (!useOverrides && overrideEntriesProp != null && overrideEntriesProp.isArray) {
      ClearSerializedArray(overrideEntriesProp);
    }

    for (int i = 0; i < spritesToAdd.Count; i++) {
      targetList.arraySize++;
      var entry = targetList.GetArrayElementAtIndex(targetList.arraySize - 1);
      var labelNameProp = entry.FindPropertyRelative("m_Name");
      if (labelNameProp != null) labelNameProp.stringValue = spritesToAdd[i].label;
      var labelHashProp = entry.FindPropertyRelative("m_Hash");
      if (labelHashProp != null) labelHashProp.intValue = Animator.StringToHash(spritesToAdd[i].label);

      var spriteProp = entry.FindPropertyRelative("m_Sprite");
      if (spriteProp != null) spriteProp.objectReferenceValue = spritesToAdd[i].sprite;
      var fromMainEntryProp = entry.FindPropertyRelative("m_FromMain");
      if (fromMainEntryProp != null) fromMainEntryProp.intValue = 0;
      if (useOverrides) {
        var spriteOverrideProp = entry.FindPropertyRelative("m_SpriteOverride");
        if (spriteOverrideProp != null) spriteOverrideProp.objectReferenceValue = spritesToAdd[i].sprite;
      }
    }

    if (useOverrides) {
      var countProp = categoryProp.FindPropertyRelative("m_EntryOverrideCount");
      if (countProp != null) countProp.intValue = targetList.arraySize;
    }

    so.ApplyModifiedPropertiesWithoutUndo();
    return true;
  }

  void ClearSerializedArray(SerializedProperty arrayProp) {
    for (int i = arrayProp.arraySize - 1; i >= 0; i--) {
      arrayProp.DeleteArrayElementAtIndex(i);
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
