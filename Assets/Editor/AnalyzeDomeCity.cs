using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class AnalyzeDomeCity {
    public static void Run() {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Locations/DomeCity.prefab");
        if (prefab == null) {
            Debug.LogError("Could not load prefab");
            return;
        }
        var sgs = prefab.GetComponentsInChildren<SortingGroup>(true);
        int redundantCount = 0;
        foreach (var sg in sgs) {
            var renderers = sg.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length <= 1) {
                redundantCount++;
            }
        }
        Debug.Log($"Found {sgs.Length} SortingGroups. {redundantCount} are redundant (<=1 renderer).");
    }
}
