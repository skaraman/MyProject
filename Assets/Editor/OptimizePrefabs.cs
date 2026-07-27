using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class OptimizePrefabs {
    [MenuItem("Tools/Optimize/Remove Redundant SortingGroups")]
    public static void CleanSortingGroups() {
        // Run on all selected prefabs or GameObjects
        var selected = Selection.gameObjects;
        if (selected.Length == 0) {
            Debug.LogWarning("Please select at least one Prefab or GameObject in the Project or Hierarchy view to optimize.");
            return;
        }

        int totalRemoved = 0;

        foreach (var go in selected) {
            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (string.IsNullOrEmpty(assetPath)) {
                assetPath = AssetDatabase.GetAssetPath(go);
            }
            bool isPrefab = !string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".prefab");

            GameObject target = go;
            if (isPrefab) {
                target = PrefabUtility.LoadPrefabContents(assetPath);
            }

            var sgs = target.GetComponentsInChildren<SortingGroup>(true);
            int removedForThis = 0;

            foreach (var sg in sgs) {
                // If this SortingGroup only groups one Renderer, it's redundant.
                // We check all renderers in this SortingGroup (that are not grouped by a child SortingGroup)
                var renderers = sg.GetComponentsInChildren<Renderer>(true);
                
                // Count renderers that are directly affected by THIS SortingGroup
                int effectiveRenderers = 0;
                foreach (var r in renderers) {
                    var parentSG = r.GetComponentInParent<SortingGroup>();
                    if (parentSG == sg) {
                        effectiveRenderers++;
                    }
                }

                if (effectiveRenderers <= 1) {
                    // It's redundant! We can destroy it.
                    Object.DestroyImmediate(sg, true);
                    removedForThis++;
                }
            }

            if (isPrefab) {
                if (removedForThis > 0) {
                    PrefabUtility.SaveAsPrefabAsset(target, assetPath);
                    Debug.Log($"Optimized Prefab at {assetPath}: Removed {removedForThis} redundant SortingGroups.");
                }
                PrefabUtility.UnloadPrefabContents(target);
            } else {
                if (removedForThis > 0) {
                    Debug.Log($"Optimized GameObject {go.name}: Removed {removedForThis} redundant SortingGroups.");
                }
            }

            totalRemoved += removedForThis;
        }

        Debug.Log($"Optimization Complete. Total redundant SortingGroups removed: {totalRemoved}");
    }
}
