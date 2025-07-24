#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.Linq;

public class SceneSaveVersionTracker : MonoBehaviour {
  public int version;
  public int dateParse;
  public string visibleVersion;
  public FontText destination;

  static SceneSaveVersionTracker() {
    EditorSceneManager.sceneSaved += HandleSceneSaved;
  }

  static void HandleSceneSaved(UnityEngine.SceneManagement.Scene scene) {
    var obj = FindAnyObjectByType<SceneSaveVersionTracker>();
    if (obj == null) return;

    obj.version = PlayerPrefs.GetInt("version", 0) + 1;
    obj.dateParse = int.Parse(DateTime.Now.ToString("yyyyMMdd"));
    obj.visibleVersion = new string($"{obj.dateParse}{obj.version:D4}".Replace("0", "").Reverse().ToArray());
    PlayerPrefs.SetInt("version", obj.version);
    PlayerPrefs.Save();
    if (obj.destination != null) obj.destination.content = "v. " + obj.visibleVersion;
  }
}
#endif
