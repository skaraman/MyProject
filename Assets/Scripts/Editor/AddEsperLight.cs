using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class AddEsperLightEditorScript {
    [MenuItem("Tools/Add Esper Local Light To Prefab")]
    public static void AddLight() {
        string prefabPath = "Assets/Prefabs/Characters/ESPER.prefab";
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        
        if (prefabRoot != null) {
            // Check if it already exists to prevent duplicates
            if (prefabRoot.transform.Find("EsperLocalLight") == null) {
                GameObject lightObj = new GameObject("EsperLocalLight");
                lightObj.transform.SetParent(prefabRoot.transform);
                lightObj.transform.localPosition = new Vector3(0, -1.78f, 0);
                
                var light2D = lightObj.AddComponent<Light2D>();
                light2D.lightType = Light2D.LightType.Point;
                light2D.intensity = 0.36f;
                light2D.pointLightOuterRadius = 2.87f;
                light2D.pointLightInnerRadius = 0f;
                light2D.pointLightOuterAngle = 360f;
                light2D.pointLightInnerAngle = 0f;
                light2D.falloffIntensity = 0.572f;
                light2D.color = new Color(1f, 0.95f, 0.85f);

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                Debug.Log("Successfully added EsperLocalLight to ESPER.prefab. You can safely delete this script now.");
            } else {
                Debug.Log("EsperLocalLight already exists on the ESPER prefab.");
            }
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        } else {
            Debug.LogError("Failed to load ESPER prefab at " + prefabPath);
        }
    }
}
