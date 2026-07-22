using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DayNightCycle2D))]
public class DayNightCycle2DEditor : Editor {
  public override void OnInspectorGUI() {
    serializedObject.Update();

    var script = (DayNightCycle2D)target;

    DrawDefaultInspector();

    EditorGUILayout.Space(10);
    EditorGUILayout.LabelField("Time Preview & Quick Controls", EditorStyles.boldLabel);

    var h = script.Hour;
    var m = script.Minute;
    var timeStr = $"{h:D2}:{m:D2}";
    var stateStr = script.IsDay ? "Daytime ☀️" : "Nighttime 🌙";

    EditorGUILayout.HelpBox($"Current Time: {timeStr} ({stateStr})\nCycle Progress: {(script.NormalizedTime * 100f):F1}%\nCycle Duration: {script.CycleDurationMinutes:F1} mins (1 min = 1 in-game hour)", MessageType.Info);

    EditorGUILayout.BeginHorizontal();
    if (GUILayout.Button("Dawn (06:00)")) {
      Undo.RecordObject(script, "Set Dawn Time");
      script.SetTime(6f);
      EditorUtility.SetDirty(script);
      RepaintViews();
    }
    if (GUILayout.Button("Noon (12:00)")) {
      Undo.RecordObject(script, "Set Noon Time");
      script.SetTime(12f);
      EditorUtility.SetDirty(script);
      RepaintViews();
    }
    if (GUILayout.Button("Dusk (18:00)")) {
      Undo.RecordObject(script, "Set Dusk Time");
      script.SetTime(18f);
      EditorUtility.SetDirty(script);
      RepaintViews();
    }
    if (GUILayout.Button("Midnight (00:00)")) {
      Undo.RecordObject(script, "Set Midnight Time");
      script.SetTime(0f);
      EditorUtility.SetDirty(script);
      RepaintViews();
    }
    EditorGUILayout.EndHorizontal();

    EditorGUILayout.BeginHorizontal();
    if (script.IsPaused) {
      if (GUILayout.Button("▶ Resume Time")) {
        Undo.RecordObject(script, "Resume Time");
        script.Resume();
        EditorUtility.SetDirty(script);
      }
    } else {
      if (GUILayout.Button("⏸ Pause Time")) {
        Undo.RecordObject(script, "Pause Time");
        script.Pause();
        EditorUtility.SetDirty(script);
      }
    }

    if (GUILayout.Button("+1 Hour")) {
      Undo.RecordObject(script, "Advance 1 Hour");
      script.SetTime(script.CurrentHour + 1f);
      EditorUtility.SetDirty(script);
      RepaintViews();
    }
    if (GUILayout.Button("-1 Hour")) {
      Undo.RecordObject(script, "Rewind 1 Hour");
      script.SetTime(script.CurrentHour - 1f);
      EditorUtility.SetDirty(script);
      RepaintViews();
    }
    EditorGUILayout.EndHorizontal();

    serializedObject.ApplyModifiedProperties();
  }

  static void RepaintViews() {
    SceneView.RepaintAll();
    EditorApplication.QueuePlayerLoopUpdate();
  }
}
