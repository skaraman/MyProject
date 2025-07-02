using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class TextureHeightFinderWindow : EditorWindow
{
    private int targetHeight = 1920;
    private DefaultAsset folderAsset;
    private bool showProgressBar = true;
    private bool checkWidth = false;
    private int targetWidth = 1080;
    private Vector2 scrollPosition;
    private List<Texture2D> foundTextures = new List<Texture2D>();
    private GUIStyle headerStyle;

    [MenuItem("Tools/Find Textures by Size")]
    public static void ShowWindow()
    {
        GetWindow<TextureHeightFinderWindow>("Find Smaller Textures");
    }

    private void OnEnable()
    {
        headerStyle = new GUIStyle();
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.fontSize = 13;
        headerStyle.margin = new RectOffset(0, 0, 10, 5);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Find Textures Smaller Than Target Size", headerStyle);
        EditorGUI.BeginChangeCheck();

        // Height input
        targetHeight = EditorGUILayout.IntField("Max Height", targetHeight);
        
        // Width option
        checkWidth = EditorGUILayout.Toggle("Also Check Width", checkWidth);
        if (checkWidth)
        {
            targetWidth = EditorGUILayout.IntField("Max Width", targetWidth);
        }
        
        // Progress bar option
        showProgressBar = EditorGUILayout.Toggle("Show Progress Bar", showProgressBar);

        // Folder picker (accepts any folder from Project view)
        folderAsset = EditorGUILayout.ObjectField(
            new GUIContent("Search Folder", "Drag a folder here or leave blank for whole project"),
            folderAsset,
            typeof(DefaultAsset),
            false) as DefaultAsset;

        GUILayout.Space(10);

        // Search buttons
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("Search Entire Project"))
        {
            FindTexturesBySize(null);
        }
        
        if (GUILayout.Button("Search Selected Folder"))
        {
            if (folderAsset != null)
            {
                FindTexturesBySize(folderAsset);
            }
            else
            {
                EditorUtility.DisplayDialog("No Folder Selected", "Please select a folder first.", "OK");
            }
        }

        if (GUILayout.Button("Search Selected Textures"))
        {
            ProcessSelection();
        }
        
        EditorGUILayout.EndHorizontal();
        
        // Display results if we have any
        if (foundTextures.Count > 0)
        {
            GUILayout.Space(15);
            EditorGUILayout.LabelField($"Found {foundTextures.Count} smaller texture(s)", headerStyle);
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            
            foreach (var texture in foundTextures)
            {
                if (texture == null) continue;
                
                EditorGUILayout.BeginHorizontal();
                
                // Preview texture
                GUILayout.Box(texture, GUILayout.Width(64), GUILayout.Height(64));
                
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(texture.name);
                EditorGUILayout.LabelField($"Size: {texture.width}x{texture.height}");
                string path = AssetDatabase.GetAssetPath(texture);
                EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
                
                if (GUILayout.Button("Select in Project", GUILayout.Width(120)))
                {
                    Selection.activeObject = texture;
                    EditorGUIUtility.PingObject(texture);
                }
                
                EditorGUILayout.EndVertical();
                
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(5);
            }
            
            EditorGUILayout.EndScrollView();
        }
    }

    private void ProcessSelection()
    {
        Texture2D[] textures = Selection.GetFiltered<Texture2D>(SelectionMode.Assets);
        if (textures.Length == 0)
        {
            EditorUtility.DisplayDialog("No Textures Selected", "Please select Texture2D assets in the Project view.", "OK");
            return;
        }

        BatchProcess(textures.Select(t => AssetDatabase.GetAssetPath(t)), textures.Length);
    }

    private void FindTexturesBySize(DefaultAsset folder)
    {
        string folderPath = folder != null ? AssetDatabase.GetAssetPath(folder) : null;
        
        // Validate folder if one was provided
        if (folder != null && (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath)))
        {
            Debug.LogError($"Selected asset is not a valid folder: {folderPath}");
            EditorUtility.DisplayDialog("Invalid Folder", "The selected asset is not a valid folder.", "OK");
            return;
        }

        // Process the search
        if (folder != null)
        {
            // Search the specific folder
            ProcessFolder(folder);
        }
        else
        {
            // Search the entire project
            string[] guids = AssetDatabase.FindAssets("t:Texture2D");
            BatchProcess(guids.Select(guid => AssetDatabase.GUIDToAssetPath(guid)), guids.Length);
        }
    }

    private void ProcessFolder(DefaultAsset folderAsset)
    {
        string folderPath = AssetDatabase.GetAssetPath(folderAsset);
        
        // Check if it's a valid folder
        if (!Directory.Exists(folderPath))
        {
            EditorUtility.DisplayDialog("Invalid Folder", "The selected folder does not exist.", "OK");
            return;
        }

        // Find all texture files in the folder
        string[] exts = { ".png", ".jpg", ".jpeg", ".psd", ".tga", ".tif", ".tiff", ".exr", ".hdr" };
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
        foundTextures.Clear();
        int processed = 0;
        int i = 0;

        foreach (string path in assetPaths)
        {
            if (showProgressBar && EditorUtility.DisplayCancelableProgressBar(
                "Finding Textures", $"Checking {Path.GetFileName(path)} ({++i}/{total})", (float)i / total))
            {
                Debug.LogWarning("Texture search canceled by user.");
                break;
            }

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
            {
                bool isSmallerThanTarget;
                
                if (checkWidth)
                {
                    // Check both dimensions are smaller than targets
                    isSmallerThanTarget = tex.height < targetHeight && tex.width < targetWidth;
                }
                else
                {
                    // Check only height is smaller than target
                    isSmallerThanTarget = tex.height < targetHeight;
                }
                
                if (isSmallerThanTarget)
                {
                    foundTextures.Add(tex);
                    processed++;
                    Debug.Log($"Found smaller texture: {path} (Size: {tex.width}x{tex.height})");
                }
            }
        }

        if (showProgressBar) EditorUtility.ClearProgressBar();
        
        string dimensionsChecked = checkWidth ? 
            $"height < {targetHeight} and width < {targetWidth}" : 
            $"height < {targetHeight}";
            
        Debug.Log($"Search complete. Found {processed} texture(s) with {dimensionsChecked}.");
        
        Repaint(); // Force window update to show results
    }
}