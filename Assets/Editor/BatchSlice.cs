using UnityEngine;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEditor.U2D;
using System.IO;
using System.Linq;
using System.Collections.Generic;

public class OptimizedBatchSpriteProcessor : EditorWindow
{
    private int gridX = 10;
    private int gridY = 10;
    private bool processFolders = false;
    private DefaultAsset textureFolder;
    private bool showProgressBar = true;

    [MenuItem("Tools/Optimized Batch Sprite Slicer")]
    public static void ShowWindow()
    {
        GetWindow<OptimizedBatchSpriteProcessor>("Optimized Sprite Slicer");
    }

    private void OnGUI()
    {
        GUILayout.Label("Optimized Batch Sprite Grid Slicer", EditorStyles.boldLabel);

        gridX = EditorGUILayout.DelayedIntField("Grid Width", gridX);
        gridY = EditorGUILayout.DelayedIntField("Grid Height", gridY);
        gridX = Mathf.Max(1, gridX);
        gridY = Mathf.Max(1, gridY);

        GUILayout.Space(10);
        processFolders = EditorGUILayout.Toggle("Process Entire Folder", processFolders);

        if (processFolders)
            textureFolder = (DefaultAsset)EditorGUILayout.ObjectField("Texture Folder", textureFolder, typeof(DefaultAsset), false);
        else
            EditorGUILayout.LabelField("Or select textures in Project view");

        showProgressBar = EditorGUILayout.Toggle("Show Progress Bar", showProgressBar);
        GUILayout.Space(10);

        if (GUILayout.Button("Process Sprites"))
            EditorApplication.delayCall += ProcessSprites; // Avoid UI freeze
    }

    private void ProcessSprites()
    {
        if (processFolders)
        {
            if (textureFolder == null)
            {
                EditorUtility.DisplayDialog("No Folder Selected", "Please assign a folder containing textures.", "OK");
                return;
            }
            ProcessFolder(textureFolder);
        }
        else
        {
            ProcessSelection();
        }
    }

    private void ProcessSelection()
    {
        Texture2D[] textures = Selection.GetFiltered<Texture2D>(SelectionMode.Assets);
        if (textures.Length == 0)
        {
            EditorUtility.DisplayDialog("No Textures Selected", "Select Texture2D assets.", "OK");
            return;
        }

        BatchProcess(textures.Select(t => AssetDatabase.GetAssetPath(t)), textures.Length);
    }

    private void ProcessFolder(DefaultAsset folderAsset)
    {
        string folderPath = AssetDatabase.GetAssetPath(folderAsset);
        string[] exts = { ".png", ".jpg", ".jpeg", ".psd", ".tga" };
        List<string> files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => exts.Contains(Path.GetExtension(f).ToLower()))
            .ToList();

        if (files.Count == 0)
        {
            EditorUtility.DisplayDialog("No Textures Found", "No supported textures in folder.", "OK");
            return;
        }

        BatchProcess(files, files.Count);
    }

    private void BatchProcess(IEnumerable<string> assetPaths, int total)
    {
        int processed = 0;
        int i = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string path in assetPaths)
            {
                if (showProgressBar && EditorUtility.DisplayCancelableProgressBar(
                    "Slicing Sprites", $"{Path.GetFileName(path)} ({++i}/{total})", (float)i / total))
                {
                    Debug.LogWarning("Batch slicing canceled by user.");
                    break;
                }

                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null && ProcessTextureWithDataProvider(tex))
                    processed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            if (showProgressBar) EditorUtility.ClearProgressBar();
        }

        EditorUtility.DisplayDialog("Process Complete", $"Processed {processed} of {total} textures.", "OK");
    }

    private bool ProcessTextureWithDataProvider(Texture2D texture)
    {
        string path = AssetDatabase.GetAssetPath(texture);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return false;

        Debug.Log($"Processing: {path}");

        // Configure importer for multiple sprites
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.SaveAndReimport();

        // Set up the data-provider factory & get the sprite editor provider
        SpriteDataProviderFactories factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer) as ISpriteEditorDataProvider;
        if (dataProvider == null) return false;

        dataProvider.InitSpriteEditorDataProvider();

        // Build and assign all SpriteRects
        int w = texture.width, h = texture.height;
        float cw = w / (float)gridX, ch = h / (float)gridY;
        List<SpriteRect> rects = new List<SpriteRect>(gridX * gridY);

        for (int idx = 0; idx < gridX * gridY; idx++)
        {
            int x = idx % gridX, y = idx / gridX;
            rects.Add(new SpriteRect
            {
                name = $"{texture.name}_{idx + 1}",
                rect = new Rect(x * cw, y * ch, cw, ch),
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                border = Vector4.zero
            });
        }

        dataProvider.SetSpriteRects(rects.ToArray());

        // Unity 2021.2+ only: register unique name→FileId mappings
        if (dataProvider.HasDataProvider(typeof(ISpriteNameFileIdDataProvider)))
        {
            ISpriteNameFileIdDataProvider nfProv = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            List<SpriteNameFileIdPair> pairs = nfProv.GetNameFileIdPairs().ToList();
            foreach (SpriteRect r in rects)
                pairs.Add(new SpriteNameFileIdPair(r.name, GUID.Generate()));

            nfProv.SetNameFileIdPairs(pairs);
        }

        // Apply & reimport
        dataProvider.Apply();
        importer.SaveAndReimport();

        return true;
    }
}
