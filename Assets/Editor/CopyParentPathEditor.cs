using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CopyParentPathEditor
{
    [MenuItem("GameObject/Copy Parent Path", false, 0)]
    public static void CopyParentPath()
    {
        var selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0) return;

        var sorted = selected
            .Where(go => go != null)
            .OrderBy(go => GetHierarchySortKey(go.transform))
            .ToList();

        if (sorted.Count == 0) return;

        var paths = sorted.Select(GetParentPath).ToArray();
        string combinedPath = string.Join(Environment.NewLine, paths);

        GUIUtility.systemCopyBuffer = combinedPath;
        EditorGUIUtility.systemCopyBuffer = combinedPath;

        if (paths.Length == 1)
        {
            Debug.Log($"Copied parent path to clipboard: \"{combinedPath}\"", sorted[0]);
        }
        else
        {
            Debug.Log($"Copied {paths.Length} parent paths to clipboard:\n{combinedPath}");
        }
    }

    [MenuItem("GameObject/Copy Parent Path", true)]
    private static bool ValidateCopyParentPath()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0 && Selection.gameObjects.Any(go => go != null);
    }

    private static string GetParentPath(GameObject go)
    {
        if (go == null) return string.Empty;

        var names = new List<string>();
        Transform current = go.transform;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        string sceneName = string.Empty;
        if (go.scene.IsValid())
        {
            sceneName = go.scene.name;
            if (string.IsNullOrEmpty(sceneName))
            {
                sceneName = go.scene.path;
                if (!string.IsNullOrEmpty(sceneName))
                {
                    sceneName = Path.GetFileNameWithoutExtension(sceneName);
                }
            }
        }

        if (string.IsNullOrEmpty(sceneName))
        {
            sceneName = "Untitled";
        }

        if (!sceneName.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) &&
            !sceneName.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
        {
            sceneName += ".scene";
        }

        names.Add(sceneName);
        names.Reverse();
        return string.Join(" -> ", names);
    }

    private static string GetHierarchySortKey(Transform transform)
    {
        if (transform == null) return string.Empty;

        var indices = new List<int>();
        Transform current = transform;
        while (current != null)
        {
            indices.Add(current.GetSiblingIndex());
            current = current.parent;
        }
        indices.Reverse();

        string sceneName = string.Empty;
        if (transform.gameObject.scene.IsValid())
        {
            sceneName = transform.gameObject.scene.name;
        }

        return $"{sceneName}/" + string.Join("/", indices.Select(i => i.ToString("D6")));
    }
}
