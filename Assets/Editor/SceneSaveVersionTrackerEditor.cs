using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class SceneSaveVersionTrackerEditor
{
  static SceneSaveVersionTrackerEditor()
  {
    EditorSceneManager.sceneSaved += HandleSceneSaved;
  }

  static void HandleSceneSaved(UnityEngine.SceneManagement.Scene scene)
  {
    var obj = UnityEngine.Object.FindAnyObjectByType<SceneSaveVersionTracker>();
    if (obj == null) return;

    obj.version = PlayerPrefs.GetInt("version", 0) + 1;
    obj.dateParse = int.Parse(DateTime.Now.ToString("yyyyMMdd"));
    obj.visibleVersion = new string($"{obj.dateParse}{obj.version:D4}".Replace("0", "").Reverse().ToArray());
    PlayerPrefs.SetInt("version", obj.version);
    PlayerPrefs.Save();
    if (obj.destination != null) obj.destination.content = "v. " + obj.visibleVersion;
  }
}
