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
  static Dictionary<UnityEngine.Object, Dictionary<FieldInfo, object>> playModeInspectorCache = new();
  static bool isInitialized = false;
  static double lastUpdateTime = 0;
  static readonly double UPDATE_INTERVAL = 0.1; // Check every 100ms

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
      case PlayModeStateChange.EnteredPlayMode:
        InitializePlayModeInspectorCache();
        break;
      case PlayModeStateChange.EnteredEditMode:
        RestoreEditModeCache();
        playModeInspectorCache.Clear();
        break;
    }
  }

  static void OnEditorUpdate() {
    if (EditorApplication.timeSinceStartup - lastUpdateTime < UPDATE_INTERVAL)
      return;
    
    lastUpdateTime = EditorApplication.timeSinceStartup;

    if (!Application.isPlaying) {
      MonitorEditModeChanges();
    } else {
     // MonitorPlayModeInspectorChanges();
    }
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

  static void InitializePlayModeInspectorCache() {
    playModeInspectorCache.Clear();
    var allMonoBehaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
    foreach (var obj in allMonoBehaviours) {
      if (ShouldMonitorObject(obj))
        playModeInspectorCache[obj] = GetFieldSnapshot(obj);
    }
  }

  static void MonitorPlayModeInspectorChanges() {
    var allMonoBehaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
    
    foreach (var obj in allMonoBehaviours) {
      if (!ShouldMonitorObject(obj)) continue;
      
      // Initialize cache for new objects
      if (!playModeInspectorCache.ContainsKey(obj)) {
        playModeInspectorCache[obj] = GetFieldSnapshot(obj);
        continue;
      }

      var current = GetFieldSnapshot(obj);
      var previous = playModeInspectorCache[obj];
      bool anyChanged = false;

      foreach (var field in current.Keys) {
        if (!previous.ContainsKey(field)) {
          previous[field] = current[field];
          continue;
        }

        if (!Equals(current[field], previous[field])) {
          // Check if this is likely an inspector change vs script change
          if (IsLikelyInspectorChange(obj, field, previous[field], current[field])) {
            Debug.Log($"[Inspector Change] {obj.name}.{obj.GetType().Name}.{field.Name} changed from {previous[field]} to {current[field]}");
            
            // Invoke ALL ForceUpdate methods
            InvokeAllForceUpdateMethods(obj);
            anyChanged = true;
          }
          
          previous[field] = current[field];
        }
      }

      if (anyChanged) {
        EditorUtility.SetDirty(obj);
        SceneView.RepaintAll();
      }
    }

    // Clean up destroyed objects
    CleanupPlayModeCache();
  }

  // Heuristic to determine if a change likely came from inspector vs script
  static bool IsLikelyInspectorChange(UnityEngine.Object obj, FieldInfo field, object oldValue, object newValue) {
    // If the inspector is currently focused and the object is selected, more likely inspector change
    if (Selection.activeObject == obj || (obj is Component comp && Selection.activeGameObject == comp.gameObject)) {
      // Additional heuristics can be added here:
      
      // 1. Check if we're in a frame where no FixedUpdate/Update would typically run
      // (This is a simple approximation - you might want more sophisticated detection)
      
      // 2. Type-specific heuristics
      if (field.FieldType == typeof(bool)) {
        // Boolean toggles are very common inspector changes
        return true;
      }
      
      if (field.FieldType == typeof(float) || field.FieldType == typeof(int)) {
        // Numeric changes when object is selected are likely inspector changes
        return true;
      }
      
      if (field.FieldType == typeof(string)) {
        // String changes are typically inspector changes
        return true;
      }
      
      if (field.FieldType.IsSubclassOf(typeof(UnityEngine.Object))) {
        // Reference changes (drag & drop) are typically inspector changes
        return true;
      }
      
      if (field.FieldType == typeof(Vector3) || field.FieldType == typeof(Vector2) || 
          field.FieldType == typeof(Color) || field.FieldType == typeof(Quaternion)) {
        // These are commonly changed via inspector
        return true;
      }
      
      return true; // Default to true if object is selected
    }
    
    return false; // Likely a script change if object is not selected
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
          Debug.Log($"[Edit Mode] {obj.name}.{obj.GetType().Name}.{field.Name} changed from {previous[field]} to {current[field]}");
          previous[field] = current[field];
          
          // Invoke ALL ForceUpdate methods
          InvokeAllForceUpdateMethods(obj);
          anyChanged = true;
        }
      }
      if (anyChanged) {
        EditorUtility.SetDirty(obj);
        SceneView.RepaintAll();
      }
    }
  }

  static void InvokeAllForceUpdateMethods(UnityEngine.Object obj) {
    var forceUpdateMethods = obj.GetType()
      .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
      .Where(m => m.GetCustomAttribute<ForceUpdateAttribute>() != null)
      .ToArray();
    
    foreach (var method in forceUpdateMethods) {
      var parameters = method.GetParameters();
      var args = new object[parameters.Length];
      
      // Initialize default values for parameters
      for (int i = 0; i < parameters.Length; i++) {
        var paramType = parameters[i].ParameterType;
        if (paramType.IsValueType && Nullable.GetUnderlyingType(paramType) == null) {
          args[i] = Activator.CreateInstance(paramType);
        }
        // Reference types will remain null, which is fine for most cases
      }
      
      try {
        method.Invoke(obj, args);
        Debug.Log($"Invoked ForceUpdate method: {obj.GetType().Name}.{method.Name}()");
      } catch (Exception e) {
        Debug.LogWarning($"Failed to invoke ForceUpdate method {method.Name} on {obj.GetType().Name}: {e.Message}");
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
    CleanupCacheDictionary(editModeCache);
    CleanupCacheDictionary(currentCache);
    CleanupCacheDictionary(playModeOriginalCache);
  }

  static void CleanupPlayModeCache() {
    CleanupCacheDictionary(playModeInspectorCache);
  }

  static void CleanupCacheDictionary(Dictionary<UnityEngine.Object, Dictionary<FieldInfo, object>> cache) {
    var keysToRemove = new List<UnityEngine.Object>();
    foreach (var key in cache.Keys) {
      if (key == null)
        keysToRemove.Add(key);
    }
    foreach (var key in keysToRemove) {
      cache.Remove(key);
    }
  }
}
#endif