using UnityEditor;
using UnityEngine;
using Util;

namespace EditorScripts
{
    [CustomEditor(typeof(PrefabDisperser))]
    public class PrefabDisperserEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PrefabDisperser disperser = (PrefabDisperser)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Editor Actions", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
            if (GUILayout.Button("Disperse / Generate Prefabs", GUILayout.Height(30)))
            {
                disperser.DisperseObjects();
            }

            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("Clear Dispersed Objects", GUILayout.Height(25)))
            {
                disperser.ClearDispersedObjects();
            }

            GUI.backgroundColor = Color.white;
        }
    }
}
