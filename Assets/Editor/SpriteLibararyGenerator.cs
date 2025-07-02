using UnityEngine;
using UnityEditor;
using UnityEngine.U2D.Animation;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class SpriteLibraryBatchGenerator : EditorWindow
{
    private DefaultAsset selectedFolder;
    private string specifiedBodyPart;
    private string outputFolder = "";
    private bool isNormalMaps = false;

    [MenuItem("Tools/Generate Sprite Library for Body Part")]
    public static void ShowWindow()
        => GetWindow<SpriteLibraryBatchGenerator>("Sprite Library Generator");

    void OnGUI()
    {
        GUILayout.Label("Sprite Library Generator", EditorStyles.boldLabel);
        selectedFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "Root Folder", selectedFolder, typeof(DefaultAsset), false);
        specifiedBodyPart = EditorGUILayout.TextField("Body Part", specifiedBodyPart);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
        isNormalMaps = EditorGUILayout.Toggle("NormalMaps?", isNormalMaps);

        if (GUILayout.Button("Generate .asset Library"))
        {
            if (selectedFolder == null)
            {
                Debug.LogError("Please select a valid root folder.");
                return;
            }

            string rootPath = AssetDatabase.GetAssetPath(selectedFolder).Replace('\\','/');
            if (!Directory.Exists(rootPath))
            {
                Debug.LogError("The selected folder path does not exist!");
                return;
            }

            if (string.IsNullOrEmpty(specifiedBodyPart))
            {
                Debug.LogError("Please specify a body part to process.");
                return;
            }

            GenerateLibraryForBodyPart(rootPath, specifiedBodyPart);
        }
    }

    void GenerateLibraryForBodyPart(string rootPath, string bodyPart)
    {
        // Find all sheets for the specified body part
        var typeImage = isNormalMaps ? "*.jpg" : "*.png";
        var allFiles = Directory.GetFiles(rootPath, typeImage, SearchOption.AllDirectories);
        var matchingFiles = new List<string>();

        foreach (var file in allFiles)
        {
            string rel = Path.GetRelativePath(rootPath, file).Replace('\\','/');
            var parts = rel.Split('/');
            if (parts.Length == 5 && parts[3] == bodyPart)
                matchingFiles.Add(file);
        }

        if (matchingFiles.Count == 0)
        {
            Debug.LogError($"No sprite sheets found for body part '{bodyPart}'.");
            return;
        }

        // Sort sheets by numeric sheet number ascending
        matchingFiles = matchingFiles
            .OrderBy(path => {
                var name = Path.GetFileNameWithoutExtension(path);
                return int.TryParse(name, out int n) ? n : int.MaxValue;
            })
            .ToList();

        // Create the SpriteLibraryAsset and protect it from unloading
        var lib = ScriptableObject.CreateInstance<SpriteLibraryAsset>();
        lib.hideFlags = HideFlags.DontUnloadUnusedAsset;

        // Populate library in sorted sheet order
        int count = 0;
        foreach (var path in matchingFiles)
        {
            string rel = Path.GetRelativePath(rootPath, path).Replace('\\','/');
            var parts = rel.Split('/');
            AddSpritesToLibrary(lib, path, parts);
            count++;

            // Update user on progress
            EditorUtility.DisplayProgressBar(
                "Generating Library",
                $"Sheet {count} of {matchingFiles.Count}",
                count / (float)matchingFiles.Count);
        }
        EditorUtility.ClearProgressBar();

        // Save as .asset
        string saveDir = Path.Combine(rootPath, "../SpriteLibraries/" + outputFolder);
        Directory.CreateDirectory(saveDir);
        string savePath = Path.Combine(saveDir, $"{bodyPart + (isNormalMaps ? "_N" : "")}_Library.asset").Replace('\\','/');

        if (lib != null)
        {
            AssetDatabase.CreateAsset(lib, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"✔️ SpriteLibraryAsset saved at: {savePath}");
        }
        else
        {
            Debug.LogError("Failed to create SpriteLibraryAsset: asset was null.");
        }

        // Cleanup and allow unloading
        lib.hideFlags = HideFlags.None;
        EditorUtility.UnloadUnusedAssetsImmediate();
        System.GC.Collect();
    }

    void AddSpritesToLibrary(SpriteLibraryAsset lib, string sheetPath, string[] parts)
    {
        if (lib == null) return;

        // parts: [0]=Category, [1]=Label1, [2]=Label2, [3]=BodyPart, [4]=Filename.png
        string category    = parts[0];
        string labelPrefix = $"{parts[1]}_{parts[2]}";
        string sheetNum    = Path.GetFileNameWithoutExtension(parts[4]);

        // Collect and sort sprites by their frame index (from name: "{sheetNum}_{frameIndex}")
        var assets = AssetDatabase.LoadAllAssetsAtPath(sheetPath);
        var spriteDict = new SortedDictionary<int, Sprite>();

        foreach (var asset in assets)
        {
            if (asset is Sprite sprite)
            {
                var nameParts = sprite.name.Split('_');
                if (nameParts.Length >= 2 && int.TryParse(nameParts[1], out int frameIndex))
                {
                    spriteDict[frameIndex] = sprite;
                }
            }
        }

        // Add sprites in ascending frame order
        foreach (var kvp in spriteDict)
        {
            string label = $"{labelPrefix}_{sheetNum}_{kvp.Key}";
            lib.AddCategoryLabel(kvp.Value, category, label);
        }
    }
}
