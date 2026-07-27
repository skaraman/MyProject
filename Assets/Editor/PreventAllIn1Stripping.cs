using UnityEditor;
using UnityEngine;
using System.IO;

[InitializeOnLoad]
public class PreventAllIn1Stripping
{
    static PreventAllIn1Stripping()
    {
        EditorApplication.delayCall += EnsureDummyMaterials;
    }

    static void EnsureDummyMaterials()
    {
        string dir = "Assets/Resources";
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }

        string[] shadersToKeep = new string[] {
            "AllIn1SpriteShader",
            "AllIn1SpriteShaderUiMask",
            "AllIn1SpriteShaderLit",
            "AllIn1SpriteShaderLitTransparent"
        };

        foreach (string shaderName in shadersToKeep)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader != null)
            {
                string matPath = "Assets/Resources/AllIn1Dummy_" + shaderName + ".mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(matPath) == null)
                {
                    Material mat = new Material(shader);
                    // Enable keywords used dynamically in the game
                    mat.EnableKeyword("GREYSCALE_ON");
                    mat.EnableKeyword("OUTBASE_ON");
                    mat.EnableKeyword("GLOWLIGHT_ON");
                    mat.EnableKeyword("FADE_ON");
                    mat.EnableKeyword("HOLO_ON");
                    mat.EnableKeyword("GLITCH_ON");
                    AssetDatabase.CreateAsset(mat, matPath);
                    Debug.Log("Created " + matPath + " to prevent shader variant stripping.");
                }
            }
        }
        AssetDatabase.SaveAssets();
    }
}
