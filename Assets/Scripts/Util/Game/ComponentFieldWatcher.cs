#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

[InitializeOnLoad]
public static class ComponentFieldWatcher {
  static Dictionary<UnityEngine.Object, Dictionary<FieldInfo, object>> editModeCache = new();
  static Dictionary<UnityEngine.Object, Dictionary<FieldInfo, object>> playModeOriginalCache = new();
  static Dictionary<UnityEngine.Object, Dictionary<FieldInfo, object>> currentCache = new();
  static bool isInitialized = false;

  static ComponentFieldWatcher() {
    EditorApplication.update += OnEditorUpdate;
    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

    if (!isInitialized) {
      InitializeCache();
      isInitialized = true;
    }
  }

  static void OnPlayModeStateChanged(PlayModeStateChange state) {
    switch (state) {
      case PlayModeStateChange.ExitingEditMode:
        StoreOriginalValues();
        break;
      case PlayModeStateChange.EnteredEditMode:
        RestoreEditModeCache();
        break;
    }
  }

  static void OnEditorUpdate() {
    if (!Application.isPlaying)
      //   MonitorPlayModeChanges();
      // else
      MonitorEditModeChanges();
  }

  static void InitializeCache() {
    editModeCache.Clear();
    currentCache.Clear();

    MonitorAllEditMode<MonoBehaviour>(UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None));
  }

  static void StoreOriginalValues() {
    playModeOriginalCache.Clear();
    var allMonoBehaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
    foreach (var obj in allMonoBehaviours) {
      if (ShouldMonitorObject(obj))
        playModeOriginalCache[obj] = GetFieldSnapshot(obj);
    }
  }

  static void MonitorEditModeChanges() {
    var allMonoBehaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
    MonitorAllEditMode<MonoBehaviour>(allMonoBehaviours);
  }

  static void MonitorAllEditMode<T>(T[] objects) where T : UnityEngine.Object {
    foreach (var obj in objects) {
      if (!ShouldMonitorObject(obj)) continue;
      if (!editModeCache.ContainsKey(obj))
        editModeCache[obj] = GetFieldSnapshot(obj);
      var current = GetFieldSnapshot(obj);
      var previous = editModeCache[obj];
      bool anyChanged = false;
      foreach (var field in current.Keys) {
        if (!previous.ContainsKey(field)) {
          previous[field] = current[field];
          continue;
        }
        if (!Equals(current[field], previous[field])) {
          //Debug.Log($"[Play Mode] {obj.GetType().Name}.{field.Name} changed from {previous[field]} to {current[field]}");
          previous[field] = current[field];
          var reactMethod = obj.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(m => m.GetCustomAttribute<ForceUpdateAttribute>() != null);
          if (reactMethod != null) {
            var parameters = reactMethod.GetParameters();
            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++) {
              var paramType = parameters[i].ParameterType;
              if (paramType.IsValueType && Nullable.GetUnderlyingType(paramType) == null) return;
            }
            reactMethod.Invoke(obj, args);
          }
          anyChanged = true;
        }
      }
      if (anyChanged) {
        EditorUtility.SetDirty(obj);
        SceneView.RepaintAll();
      }
    }
  }

  static void RestoreEditModeCache() {
    currentCache.Clear();
    var allMonoBehaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
    foreach (var obj in allMonoBehaviours) {
      if (ShouldMonitorObject(obj))
        editModeCache[obj] = GetFieldSnapshot(obj);
    }
  }

  static bool ShouldMonitorObject(UnityEngine.Object obj) {
    if (obj == null) return false;
    if (EditorUtility.IsPersistent(obj)) return false;
    if (obj.hideFlags.HasFlag(HideFlags.HideAndDontSave)) return false;
    if (obj is ComponentPropagator || obj is AllIn1AnimatorInspector) return false;
    return true;
  }

  static Dictionary<FieldInfo, object> GetFieldSnapshot(UnityEngine.Object obj) {
    var snapshot = new Dictionary<FieldInfo, object>();
    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    foreach (var field in obj.GetType().GetFields(flags)) {
      if (field.IsPublic || field.IsDefined(typeof(SerializeField), true)) {
        try {
          var value = field.GetValue(obj);
          snapshot[field] = value;
        }
        catch (Exception e) {
          Debug.LogWarning($"Failed to get value for field {field.Name} on {obj.GetType().Name}: {e.Message}");
        }
      }
    }
    return snapshot;
  }

  static void CleanupCache() {
    var keysToRemove = new List<UnityEngine.Object>();
    foreach (var key in editModeCache.Keys) {
      if (key == null)
        keysToRemove.Add(key);
    }
    foreach (var key in keysToRemove) {
      editModeCache.Remove(key);
      currentCache.Remove(key);
    }
  }
}
#endif