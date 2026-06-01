#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed class MissingScriptWindow : EditorWindow {
  [MenuItem("Tools/Missing Scripts Tool")]
  static void Open() {
    GetWindow<MissingScriptWindow>("Missing Scripts");
  }

  void OnGUI() {
    EditorGUILayout.HelpBox("Legacy placeholder window. No action is required.", MessageType.Info);
  }
}
#endif
