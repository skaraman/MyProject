using UnityEditor;
using UnityEngine;

public class TestScript {
    [MenuItem("Tools/Test Search Roots")]
    public static void Test() {
        foreach (var root in ContentPackPipeline.GetSpriteLibrarySearchRoots()) {
            Debug.Log("Root: " + root);
        }
    }
}
