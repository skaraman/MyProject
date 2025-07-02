#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;


[InitializeOnLoad]
public static class ComponentFieldWatcher
{
  static Dictionary<UnityEngine.Object, Dictionary<FieldInfo, object>> editModeCache = new();
  static Dictionary<UnityEngine.Object, Dictionary<FieldInfo, object>> playModeOriginalCache = new();
  static Dictionary<UnityEngine.Object, Dictionary<FieldInfo, object>> currentCache = new();
  static bool isInitialized = false;

  static ComponentFieldWatcher()
  {
    EditorApplication.update += OnEditorUpdate;
    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

    if (!isInitialized)
    {
      InitializeCache();
      isInitialized = true;
    }
  }

  static void OnPlayModeStateChanged(PlayModeStateChange state)
  {
    switch (state)
    {
      case PlayModeStateChange.ExitingEditMode:
        StoreOriginalValues();
        break;
      case PlayModeStateChange.EnteredPlayMode:
        InitializeRuntimeCache();
        break;
      case PlayModeStateChange.ExitingPlayMode:
        RevertPlayModeChanges();
        break;
      case PlayModeStateChange.EnteredEditMode:
        RestoreEditModeCache();
        break;
    }
  }

  static void OnEditorUpdate()
  {
    if (!Application.isPlaying)
    //   MonitorPlayModeChanges();
    // else
      MonitorEditModeChanges();
  }

  static void InitializeCache()
  {
    editModeCache.Clear();
    currentCache.Clear();

    MonitorAllEditMode<MonoBehaviour>(UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None));
  }

  static void StoreOriginalValues()
  {
    playModeOriginalCache.Clear();
    var allMonoBehaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
    foreach (var obj in allMonoBehaviours)
    {
      if (ShouldMonitorObject(obj))
        playModeOriginalCache[obj] = GetFieldSnapshot(obj);
    }
  }

  static void InitializeRuntimeCache()
  {
    currentCache.Clear();
    var allMonoBehaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
    foreach (var obj in allMonoBehaviours)
    {
      if (ShouldMonitorObject(obj))
        currentCache[obj] = GetFieldSnapshot(obj);
    }
  }

  static void MonitorPlayModeChanges()
  {
    var allMonoBehaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
    MonitorAllPlayMode<MonoBehaviour>(allMonoBehaviours);
  }

  static void MonitorEditModeChanges()
  {
    var allMonoBehaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
    MonitorAllEditMode<MonoBehaviour>(allMonoBehaviours);
  }

  static void MonitorAllEditMode<T>(T[] objects) where T : UnityEngine.Object
  {
    foreach (var obj in objects)
    {
      if (!ShouldMonitorObject(obj)) continue;
      if (!editModeCache.ContainsKey(obj))
        editModeCache[obj] = GetFieldSnapshot(obj);

      var current = GetFieldSnapshot(obj);
      var previous = editModeCache[obj];
      bool anyChanged = false;

      foreach (var field in current.Keys)
      {
        if (!previous.ContainsKey(field))
        {
          previous[field] = current[field];
          continue;
        }
        if (!Equals(current[field], previous[field])) {
          Debug.Log($"[Play Mode] {obj.GetType().Name}.{field.Name} changed from {previous[field]} to {current[field]}");
          previous[field] = current[field];

          var reactMethod = obj.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(m => m.GetCustomAttribute<ForceUpdateAttribute>() != null);

          if (reactMethod != null) reactMethod.Invoke(obj, null);
          anyChanged = true;
        }
      }

      if (anyChanged)
      {
        EditorUtility.SetDirty(obj);
        SceneView.RepaintAll();
      }
    }
  }

  static void MonitorAllPlayMode<T>(T[] objects) where T : UnityEngine.Object
  {
    foreach (var obj in objects)
    {
      if (!ShouldMonitorObject(obj)) continue;
      if (!currentCache.ContainsKey(obj))
        currentCache[obj] = GetFieldSnapshot(obj);

      var current = GetFieldSnapshot(obj);
      var previous = currentCache[obj];
      bool anyChanged = false;

      foreach (var field in current.Keys)
      {
        if (!previous.ContainsKey(field))
        {
          previous[field] = current[field];
          continue;
        }
        if (!Equals(current[field], previous[field]))
        {
          Debug.Log($"[Play Mode] {obj.GetType().Name}.{field.Name} changed from {previous[field]} to {current[field]}");
          previous[field] = current[field];

          var so = new SerializedObject(obj);
          so.ApplyModifiedProperties();
          EditorUtility.SetDirty(obj);

          var reactMethod = obj.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(m => m.GetCustomAttribute<ForceUpdateAttribute>() != null);

          if (reactMethod != null) reactMethod.Invoke(obj, null);
          anyChanged = true;
        }
      }

      if (anyChanged)
      {
        SceneView.RepaintAll();
      }
    }
  }

  static void RevertPlayModeChanges()
  {
    foreach (var objEntry in playModeOriginalCache)
    {
      var obj = objEntry.Key;
      if (obj == null) continue;

      var originalValues = objEntry.Value;
      //var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

      foreach (var fieldEntry in originalValues)
      {
        var field = fieldEntry.Key;
        var originalValue = fieldEntry.Value;
        try
        {
          field.SetValue(obj, originalValue);
        }
        catch (Exception e)
        {
          Debug.LogWarning($"Failed to revert field {field.Name} on {obj.GetType().Name}: {e.Message}");
        }
      }
    }
    SceneView.RepaintAll();
    Debug.Log("[Watcher] Reverted all play mode changes");
  }

  static void RestoreEditModeCache()
  {
    currentCache.Clear();
    var allMonoBehaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
    foreach (var obj in allMonoBehaviours)
    {
      if (ShouldMonitorObject(obj))
        editModeCache[obj] = GetFieldSnapshot(obj);
    }
  }

  static bool ShouldMonitorObject(UnityEngine.Object obj)
  {
    if (obj == null) return false;
    if (EditorUtility.IsPersistent(obj)) return false;
    if (obj.hideFlags.HasFlag(HideFlags.HideAndDontSave)) return false;
    if (obj is ComponentPropagator) return false;
    return true;
  }

  static Dictionary<FieldInfo, object> GetFieldSnapshot(UnityEngine.Object obj)
  {
    var snapshot = new Dictionary<FieldInfo, object>();
    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    foreach (var field in obj.GetType().GetFields(flags))
    {
      if (field.IsPublic || field.IsDefined(typeof(SerializeField), true))
      {
        try
        {
          var value = field.GetValue(obj);
          snapshot[field] = value;
        }
        catch (Exception e)
        {
          Debug.LogWarning($"Failed to get value for field {field.Name} on {obj.GetType().Name}: {e.Message}");
        }
      }
    }
    return snapshot;
  }

  static void CleanupCache()
  {
    var keysToRemove = new List<UnityEngine.Object>();
    foreach (var key in editModeCache.Keys)
    {
      if (key == null)
        keysToRemove.Add(key);
    }
    foreach (var key in keysToRemove)
    {
      editModeCache.Remove(key);
      currentCache.Remove(key);
      playModeOriginalCache.Remove(key);
    }
  }
}
#endif