#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class HierarchyCaretMemory
{
  [Serializable]
  private sealed class StoredExpandedState
  {
    public string[] expandedGlobalObjectIds = Array.Empty<string>();
  }

  private const string PREFS_KEY_PREFIX = "MyProject.HierarchyCaretMemory.v1.";
  private const double POLL_INTERVAL_SECONDS = 0.35d;
  private const double SAVE_GUARD_AFTER_CONTEXT_CHANGE_SECONDS = 1.0d;

  private static Type sceneHierarchyWindowType;
  private static PropertyInfo lastInteractedHierarchyWindowProperty;
  private static FieldInfo sceneHierarchyField;
  private static PropertyInfo sceneHierarchyProperty;
  private static MethodInfo getExpandedIdsMethod;
  private static MethodInfo setExpandedIdsMethod;
  private static Type resolvedAccessOwnerType;
  private static bool useWindowDirectAccess;
  private static MethodInfo entityIdToObjectMethod;
  private static object lastHierarchyWindowInstance;
  private static Type newHierarchyWindowTypeCache;
  private static PropertyInfo newHierarchyViewProperty;
  private static MethodInfo newHierarchyViewGetStateMethod;
  private static MethodInfo newHierarchyViewSetStateMethod;
  private static Type newHierarchyStateType;
  private static FieldInfo newHierarchyStateViewModelStateField;
  private static FieldInfo newHierarchyStateValidContentField;
  private static Type newHierarchyContentType;
  private static object newHierarchyContentAllValue;
  private static object newHierarchyContentViewModelStateValue;
  private static double lastNewHierarchySaveTime;
  private static string lastNewHierarchySavedState = string.Empty;

  private static Type fallbackAccessorWindowType;
  private static bool fallbackAccessorUseSceneHierarchyRoot;
  private static MemberInfo[] fallbackAccessorPathToOwner = Array.Empty<MemberInfo>();
  private static MemberInfo fallbackAccessorExpandedIdsMember;
  private static double fallbackAccessorNextResolveTime;

  private static bool reflectionReady;
  private static double nextReflectionResolveTime;
  private static bool missingApiLogged;
  private static double lastPollTime;
  private static double saveGuardUntilTime;
  private static bool restorePending;
  private static string currentContextKey = string.Empty;
  private static string lastSavedFingerprint = string.Empty;
  private static bool autoSaveSuppressed;
  private static string autoSaveSuppressedContextKey = string.Empty;
  private static bool loggedHierarchyIdTruncationWarning;

  static HierarchyCaretMemory()
  {
    reflectionReady = ResolveReflection();

    EditorApplication.update += OnEditorUpdate;
    EditorSceneManager.sceneOpened += OnSceneOpened;
    EditorSceneManager.sceneClosed += OnSceneClosed;
    EditorSceneManager.newSceneCreated += OnNewSceneCreated;
    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    AssemblyReloadEvents.beforeAssemblyReload += SaveCurrentState;
    EditorApplication.quitting += SaveCurrentState;

    HandleContextChange(force: true);
  }

  [MenuItem("Tools/Hierarchy Caret Memory/Save")]
  private static void MenuSaveState()
  {
    if (EditorApplication.isPlayingOrWillChangePlaymode)
    {
      return;
    }

    SyncCurrentContextKey();
    SetAutoSaveSuppressed(false);
    SaveCurrentState();
    Debug.Log("[HierarchyCaretMemory] Saved hierarchy caret state.");
  }

  [MenuItem("Tools/Hierarchy Caret Memory/Load")]
  private static void MenuLoadState()
  {
    if (EditorApplication.isPlayingOrWillChangePlaymode)
    {
      return;
    }

    SyncCurrentContextKey();
    SetAutoSaveSuppressed(false);
    restorePending = true;
    saveGuardUntilTime = EditorApplication.timeSinceStartup + SAVE_GUARD_AFTER_CONTEXT_CHANGE_SECONDS;
    TryRestoreState();

    if (restorePending)
    {
      Debug.LogWarning($"[HierarchyCaretMemory] Load did not complete. {BuildApiDiagnosticSummary()}");
      return;
    }

    Debug.Log("[HierarchyCaretMemory] Loaded hierarchy caret state.");
  }

  [MenuItem("Tools/Hierarchy Caret Memory/Expand All (Do Not Save)")]
  private static void MenuExpandAllNoSave()
  {
    if (EditorApplication.isPlayingOrWillChangePlaymode)
    {
      return;
    }

    SyncCurrentContextKey();
    SetAutoSaveSuppressed(true);
    if (!TrySetAllExpanded(expand: true))
    {
      Debug.LogWarning($"[HierarchyCaretMemory] Expand All failed. {BuildApiDiagnosticSummary()}");
      return;
    }

    Debug.Log("[HierarchyCaretMemory] Expanded all. Auto-save is suppressed for this scene context.");
  }

  [MenuItem("Tools/Hierarchy Caret Memory/Collapse All (Do Not Save)")]
  private static void MenuCollapseAllNoSave()
  {
    if (EditorApplication.isPlayingOrWillChangePlaymode)
    {
      return;
    }

    SyncCurrentContextKey();
    SetAutoSaveSuppressed(true);
    if (!TrySetAllExpanded(expand: false))
    {
      Debug.LogWarning($"[HierarchyCaretMemory] Collapse All failed. {BuildApiDiagnosticSummary()}");
      return;
    }

    Debug.Log("[HierarchyCaretMemory] Collapsed all. Auto-save is suppressed for this scene context.");
  }

  [MenuItem("Tools/Hierarchy Caret Memory/Save", true)]
  [MenuItem("Tools/Hierarchy Caret Memory/Load", true)]
  [MenuItem("Tools/Hierarchy Caret Memory/Expand All (Do Not Save)", true)]
  [MenuItem("Tools/Hierarchy Caret Memory/Collapse All (Do Not Save)", true)]
  private static bool ValidateMenuItems()
  {
    return !EditorApplication.isPlayingOrWillChangePlaymode;
  }

  private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
  {
    HandleContextChange(force: true);
  }

  private static void OnSceneClosed(Scene scene)
  {
    HandleContextChange(force: true);
  }

  private static void OnNewSceneCreated(Scene scene, NewSceneSetup setup, NewSceneMode mode)
  {
    HandleContextChange(force: true);
  }

  private static void OnPlayModeStateChanged(PlayModeStateChange state)
  {
    if (state == PlayModeStateChange.ExitingEditMode)
    {
      SaveCurrentState();
    }

    if (state == PlayModeStateChange.EnteredEditMode)
    {
      HandleContextChange(force: true);
    }
  }

  private static void OnEditorUpdate()
  {
    if (EditorApplication.isPlayingOrWillChangePlaymode)
    {
      return;
    }

    var newContextKey = BuildContextKey();
    if (!string.Equals(newContextKey, currentContextKey, StringComparison.Ordinal))
    {
      HandleContextChange(force: false);
    }

    var now = EditorApplication.timeSinceStartup;
    if (now - lastPollTime < POLL_INTERVAL_SECONDS)
    {
      return;
    }

    lastPollTime = now;

    if (restorePending)
    {
      TryRestoreState();
      if (restorePending)
      {
        return;
      }
    }

    if (now < saveGuardUntilTime)
    {
      return;
    }

    SaveCurrentState();
  }

  private static void HandleContextChange(bool force)
  {
    var newContextKey = BuildContextKey();
    if (!force && string.Equals(newContextKey, currentContextKey, StringComparison.Ordinal))
    {
      return;
    }

    currentContextKey = newContextKey;
    lastSavedFingerprint = string.Empty;
    lastNewHierarchySavedState = string.Empty;
    autoSaveSuppressed = false;
    autoSaveSuppressedContextKey = string.Empty;
    restorePending = true;
    saveGuardUntilTime = EditorApplication.timeSinceStartup + SAVE_GUARD_AFTER_CONTEXT_CHANGE_SECONDS;
  }

  private static void TryRestoreState()
  {
    if (TryUseNewHierarchyPersistence(restore: true))
    {
      restorePending = false;
      return;
    }

    if (!TryGetExpandedInstanceIds(out _))
    {
      return;
    }

    var storedState = LoadState(currentContextKey);
    if (storedState == null || storedState.expandedGlobalObjectIds == null)
    {
      restorePending = false;
      return;
    }

    var resolvedIds = ResolveInstanceIds(storedState.expandedGlobalObjectIds);
    if (!TrySetExpandedInstanceIds(resolvedIds))
    {
      return;
    }

    lastSavedFingerprint = BuildFingerprint(resolvedIds);
    restorePending = false;
  }

  private static void SaveCurrentState()
  {
    if (EditorApplication.isPlaying)
    {
      return;
    }

    if (IsAutoSaveSuppressedForCurrentContext())
    {
      return;
    }

    if (TryUseNewHierarchyPersistence(restore: false))
    {
      return;
    }

    if (!TryGetExpandedInstanceIds(out var expandedInstanceIds))
    {
      return;
    }

    var fingerprint = BuildFingerprint(expandedInstanceIds);
    if (string.Equals(fingerprint, lastSavedFingerprint, StringComparison.Ordinal))
    {
      return;
    }

    var globalIdStrings = ConvertToGlobalObjectIdStrings(expandedInstanceIds);
    var payload = new StoredExpandedState
    {
      expandedGlobalObjectIds = globalIdStrings.ToArray()
    };

    EditorPrefs.SetString(GetPrefsKey(currentContextKey), JsonUtility.ToJson(payload));
    lastSavedFingerprint = fingerprint;
  }

  private static void SyncCurrentContextKey()
  {
    var contextKey = BuildContextKey();
    if (!string.Equals(contextKey, currentContextKey, StringComparison.Ordinal))
    {
      currentContextKey = contextKey;
      lastSavedFingerprint = string.Empty;
      lastNewHierarchySavedState = string.Empty;
      autoSaveSuppressed = false;
      autoSaveSuppressedContextKey = string.Empty;
    }
  }

  private static bool IsAutoSaveSuppressedForCurrentContext()
  {
    return autoSaveSuppressed &&
           string.Equals(autoSaveSuppressedContextKey, currentContextKey, StringComparison.Ordinal);
  }

  private static void SetAutoSaveSuppressed(bool suppressed)
  {
    autoSaveSuppressed = suppressed;
    autoSaveSuppressedContextKey = suppressed ? currentContextKey : string.Empty;
  }

  private static StoredExpandedState LoadState(string contextKey)
  {
    var prefKey = GetPrefsKey(contextKey);
    if (!EditorPrefs.HasKey(prefKey))
    {
      return null;
    }

    var json = EditorPrefs.GetString(prefKey);
    if (string.IsNullOrEmpty(json))
    {
      return null;
    }

    try
    {
      return JsonUtility.FromJson<StoredExpandedState>(json);
    }
    catch (Exception ex)
    {
      Debug.LogWarning($"[HierarchyCaretMemory] Failed to parse stored hierarchy state: {ex.Message}");
      return null;
    }
  }

  private static string BuildContextKey()
  {
    var parts = new List<string>(SceneManager.sceneCount);
    for (var i = 0; i < SceneManager.sceneCount; i++)
    {
      var scene = SceneManager.GetSceneAt(i);
      if (!scene.IsValid() || !scene.isLoaded)
      {
        continue;
      }

      if (!string.IsNullOrEmpty(scene.path))
      {
        var guid = AssetDatabase.AssetPathToGUID(scene.path);
        parts.Add(string.IsNullOrEmpty(guid) ? scene.path : guid);
      }
      else
      {
        parts.Add($"UNTITLED:{scene.name}");
      }
    }

    parts.Sort(StringComparer.Ordinal);
    return parts.Count == 0 ? "NO_SCENE" : string.Join("|", parts);
  }

  private static string GetPrefsKey(string contextKey)
  {
    return PREFS_KEY_PREFIX + contextKey;
  }

  private static string BuildFingerprint(IReadOnlyList<int> ids)
  {
    if (ids == null || ids.Count == 0)
    {
      return string.Empty;
    }

    return string.Join(",", ids);
  }

  private static List<string> ConvertToGlobalObjectIdStrings(IReadOnlyList<int> instanceIds)
  {
    var result = new List<string>(instanceIds.Count);
    for (var i = 0; i < instanceIds.Count; i++)
    {
      if (!TryInstanceIdToObject(instanceIds[i], out var target))
      {
        continue;
      }

      if (target == null)
      {
        continue;
      }

      if (target is Component component)
      {
        target = component.gameObject;
      }

      if (!(target is GameObject))
      {
        continue;
      }

      var globalId = GlobalObjectId.GetGlobalObjectIdSlow(target);
      result.Add(globalId.ToString());
    }

    return result;
  }

  private static int[] ResolveInstanceIds(IEnumerable<string> globalIdStrings)
  {
    var ids = new List<int>();
    foreach (var globalIdString in globalIdStrings)
    {
      if (string.IsNullOrEmpty(globalIdString))
      {
        continue;
      }

      if (!GlobalObjectId.TryParse(globalIdString, out var globalId))
      {
        continue;
      }

      var target = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId);
      if (target == null)
      {
        continue;
      }

      if (target is Component component)
      {
        target = component.gameObject;
      }

      if (TryGetHierarchyObjectId(target, out var hierarchyObjectId))
      {
        ids.Add(hierarchyObjectId);
      }
    }

    return ids.ToArray();
  }

  private static bool TryGetHierarchyObjectId(UnityEngine.Object target, out int hierarchyObjectId)
  {
    hierarchyObjectId = 0;
    if (target == null)
    {
      return false;
    }

#if UNITY_6000_3_OR_NEWER
    var entityId = target.GetEntityId();
    var rawEntityId = UnityEngine.EntityId.ToULong(entityId);
    hierarchyObjectId = unchecked((int)rawEntityId);

    if (!loggedHierarchyIdTruncationWarning && (rawEntityId >> 32) != 0UL)
    {
      loggedHierarchyIdTruncationWarning = true;
      Debug.LogWarning($"[HierarchyCaretMemory] Truncated EntityId while resolving hierarchy state. target={target.name} type={target.GetType().Name} entityId={rawEntityId}");
    }

    return true;
#else
#pragma warning disable CS0618
    hierarchyObjectId = target.GetInstanceID();
#pragma warning restore CS0618
    return true;
#endif
  }

  private static bool ResolveReflection(Type runtimeWindowType = null)
  {
    ResolveEntityIdToObjectMethod();

    var windowType = runtimeWindowType ??
                     sceneHierarchyWindowType ??
                     FindType("UnityEditor.SceneHierarchyWindow") ??
                     FindTypeBySimpleName("SceneHierarchyWindow") ??
                     FindLikelyHierarchyWindowType();

    if (windowType == null)
    {
      return false;
    }

    if (!TryResolveExpandedAccessForWindowType(windowType))
    {
      return false;
    }

    sceneHierarchyWindowType = windowType;
    missingApiLogged = false;
    return true;
  }

  private static bool TryResolveExpandedAccessForWindowType(Type windowType)
  {
    if (windowType == null)
    {
      return false;
    }

    const BindingFlags allFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    sceneHierarchyField = null;
    sceneHierarchyProperty = null;
    resolvedAccessOwnerType = null;
    getExpandedIdsMethod = null;
    setExpandedIdsMethod = null;
    useWindowDirectAccess = false;

    lastInteractedHierarchyWindowProperty = windowType.GetProperty("lastInteractedHierarchyWindow", allFlags);
    sceneHierarchyField = windowType.GetField("m_SceneHierarchy", allFlags);
    sceneHierarchyProperty = windowType.GetProperty("sceneHierarchy", allFlags);

    if (sceneHierarchyField == null)
    {
      foreach (var field in windowType.GetFields(allFlags))
      {
        if (field.FieldType.Name.IndexOf("SceneHierarchy", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          sceneHierarchyField = field;
          break;
        }
      }
    }

    if (sceneHierarchyProperty == null)
    {
      foreach (var property in windowType.GetProperties(allFlags))
      {
        if (property.PropertyType.Name.IndexOf("SceneHierarchy", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          sceneHierarchyProperty = property;
          break;
        }
      }
    }

    var sceneHierarchyType = sceneHierarchyField != null ? sceneHierarchyField.FieldType : sceneHierarchyProperty?.PropertyType;
    if (sceneHierarchyType != null &&
        TryResolveExpandedMethods(sceneHierarchyType, out var nestedGetMethod, out var nestedSetMethod))
    {
      getExpandedIdsMethod = nestedGetMethod;
      setExpandedIdsMethod = nestedSetMethod;
      resolvedAccessOwnerType = sceneHierarchyType;
      useWindowDirectAccess = false;
      return true;
    }

    if (TryResolveExpandedMethods(windowType, out var directGetMethod, out var directSetMethod))
    {
      getExpandedIdsMethod = directGetMethod;
      setExpandedIdsMethod = directSetMethod;
      resolvedAccessOwnerType = windowType;
      useWindowDirectAccess = true;
      return true;
    }

    return false;
  }

  private static bool TryResolveExpandedMethods(Type targetType, out MethodInfo getMethod, out MethodInfo setMethod)
  {
    getMethod = null;
    setMethod = null;
    if (targetType == null)
    {
      return false;
    }

    const BindingFlags allFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    var methods = targetType.GetMethods(allFlags);

    foreach (var method in methods)
    {
      if (!string.Equals(method.Name, "GetExpandedIDs", StringComparison.Ordinal))
      {
        continue;
      }

      if (AreParametersInvokableWithDefaults(method.GetParameters()))
      {
        if (IsExpandedIdReturnType(method.ReturnType))
        {
          getMethod = method;
          break;
        }
      }
    }

    if (getMethod == null)
    {
      foreach (var method in methods)
      {
        if (method.Name.IndexOf("GetExpanded", StringComparison.OrdinalIgnoreCase) < 0)
        {
          continue;
        }

        if (AreParametersInvokableWithDefaults(method.GetParameters()))
        {
          if (IsExpandedIdReturnType(method.ReturnType))
          {
            getMethod = method;
            break;
          }
        }
      }
    }

    foreach (var method in methods)
    {
      if (!string.Equals(method.Name, "SetExpandedIDs", StringComparison.Ordinal))
      {
        continue;
      }

      var parameters = method.GetParameters();
      if (parameters.Length > 0 && IsExpandedIdParameterType(parameters[0].ParameterType))
      {
        setMethod = method;
        break;
      }
    }

    if (setMethod == null)
    {
      foreach (var method in methods)
      {
        if (method.Name.IndexOf("SetExpanded", StringComparison.OrdinalIgnoreCase) < 0)
        {
          continue;
        }

        var parameters = method.GetParameters();
        if (parameters.Length > 0 && IsExpandedIdParameterType(parameters[0].ParameterType))
        {
          setMethod = method;
          break;
        }
      }
    }

    if (setMethod == null)
    {
      // Unity 6 SceneHierarchy/SceneHierarchyWindow commonly use SetExpanded(int id, bool expanded)
      // instead of SetExpandedIDs(int[] ids).
      foreach (var method in methods)
      {
        if (!string.Equals(method.Name, "SetExpandedRecursive", StringComparison.Ordinal) &&
            !string.Equals(method.Name, "SetExpanded", StringComparison.Ordinal))
        {
          continue;
        }

        if (IsSingleExpandedSetterMethod(method))
        {
          setMethod = method;
          break;
        }
      }
    }

    if (setMethod == null)
    {
      foreach (var method in methods)
      {
        if (method.Name.IndexOf("SetExpanded", StringComparison.OrdinalIgnoreCase) < 0)
        {
          continue;
        }

        if (IsSingleExpandedSetterMethod(method))
        {
          setMethod = method;
          break;
        }
      }
    }

    return getMethod != null && setMethod != null;
  }

  private static bool IsExpandedIdParameterType(Type parameterType)
  {
    if (parameterType == typeof(int[]))
    {
      return true;
    }

    if (typeof(IEnumerable).IsAssignableFrom(parameterType))
    {
      return true;
    }

    if (parameterType.IsGenericType)
    {
      var genericArgs = parameterType.GetGenericArguments();
      if (genericArgs.Length == 1 && genericArgs[0] == typeof(int))
      {
        return true;
      }
    }

    return false;
  }

  private static bool IsSingleExpandedSetterMethod(MethodInfo method)
  {
    if (method == null)
    {
      return false;
    }

    var parameters = method.GetParameters();
    if (parameters.Length < 2)
    {
      return false;
    }

    return parameters[0].ParameterType == typeof(int) &&
           parameters[1].ParameterType == typeof(bool);
  }

  private static bool IsExpandedIdReturnType(Type returnType)
  {
    if (returnType == typeof(int[]))
    {
      return true;
    }

    if (typeof(IEnumerable).IsAssignableFrom(returnType))
    {
      return true;
    }

    if (returnType.IsGenericType)
    {
      var genericArgs = returnType.GetGenericArguments();
      if (genericArgs.Length == 1 && genericArgs[0] == typeof(int))
      {
        return true;
      }
    }

    return false;
  }

  private static Type FindLikelyHierarchyWindowType()
  {
    var editorWindowType = typeof(EditorWindow);
    Type bestType = null;
    var bestScore = int.MinValue;
    var assemblies = AppDomain.CurrentDomain.GetAssemblies();

    for (var i = 0; i < assemblies.Length; i++)
    {
      Type[] types;
      try
      {
        types = assemblies[i].GetTypes();
      }
      catch (ReflectionTypeLoadException ex)
      {
        types = ex.Types;
      }

      for (var j = 0; j < types.Length; j++)
      {
        var type = types[j];
        if (type == null || !editorWindowType.IsAssignableFrom(type))
        {
          continue;
        }

        var score = 0;
        if (type.Name.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          score += 6;
        }

        if (type.Name.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          score += 3;
        }

        if (type.GetField("m_SceneHierarchy", BindingFlags.Instance | BindingFlags.NonPublic) != null)
        {
          score += 8;
        }

        if (type.GetProperty("sceneHierarchy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
        {
          score += 8;
        }

        if (score > bestScore)
        {
          bestScore = score;
          bestType = type;
        }
      }
    }

    return bestScore > 0 ? bestType : null;
  }

  private static Type FindType(string fullName)
  {
    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
    for (var i = 0; i < assemblies.Length; i++)
    {
      var type = assemblies[i].GetType(fullName);
      if (type != null)
      {
        return type;
      }
    }

    return null;
  }

  private static Type FindTypeBySimpleName(string typeName)
  {
    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
    for (var i = 0; i < assemblies.Length; i++)
    {
      Type[] types;
      try
      {
        types = assemblies[i].GetTypes();
      }
      catch (ReflectionTypeLoadException ex)
      {
        types = ex.Types;
      }

      for (var j = 0; j < types.Length; j++)
      {
        var type = types[j];
        if (type == null)
        {
          continue;
        }

        if (string.Equals(type.Name, typeName, StringComparison.Ordinal))
        {
          return type;
        }
      }
    }

    return null;
  }

  private static void ResolveEntityIdToObjectMethod()
  {
    if (entityIdToObjectMethod != null)
    {
      return;
    }

    const BindingFlags allFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    foreach (var method in typeof(EditorUtility).GetMethods(allFlags))
    {
      if (!string.Equals(method.Name, "EntityIdToObject", StringComparison.Ordinal))
      {
        continue;
      }

      var parameters = method.GetParameters();
      if (parameters.Length == 1)
      {
        entityIdToObjectMethod = method;
        return;
      }
    }
  }

  private static bool TryInstanceIdToObject(int instanceId, out UnityEngine.Object target)
  {
    target = null;

    ResolveEntityIdToObjectMethod();
    if (entityIdToObjectMethod != null)
    {
      var parameterType = entityIdToObjectMethod.GetParameters()[0].ParameterType;
      if (TryCreateEntityIdArgument(parameterType, instanceId, out var arg))
      {
        try
        {
          target = entityIdToObjectMethod.Invoke(null, new[] { arg }) as UnityEngine.Object;
        }
        catch
        {
          // Ignore and fallback below.
        }
      }
    }

    if (target != null)
    {
      return true;
    }

#pragma warning disable CS0618
    target = EditorUtility.InstanceIDToObject(instanceId);
#pragma warning restore CS0618
    return target != null;
  }

  private static bool TryCreateEntityIdArgument(Type parameterType, int instanceId, out object arg)
  {
    arg = null;
    if (parameterType == typeof(int))
    {
      arg = instanceId;
      return true;
    }

    if (parameterType == typeof(long))
    {
      arg = (long)instanceId;
      return true;
    }

    if (parameterType == typeof(uint))
    {
      arg = (uint)instanceId;
      return true;
    }

    if (parameterType == typeof(ulong))
    {
      arg = (ulong)(uint)instanceId;
      return true;
    }

    if (parameterType.IsEnum)
    {
      arg = Enum.ToObject(parameterType, instanceId);
      return true;
    }

    if (!parameterType.IsValueType)
    {
      return false;
    }

    try
    {
      arg = Activator.CreateInstance(parameterType);
    }
    catch
    {
      return false;
    }

    const BindingFlags allFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    foreach (var field in parameterType.GetFields(allFlags))
    {
      if (field.IsStatic)
      {
        continue;
      }

      if (field.FieldType == typeof(int))
      {
        field.SetValue(arg, instanceId);
        return true;
      }

      if (field.FieldType == typeof(long))
      {
        field.SetValue(arg, (long)instanceId);
        return true;
      }
    }

    foreach (var property in parameterType.GetProperties(allFlags))
    {
      if (!property.CanWrite)
      {
        continue;
      }

      if (property.PropertyType == typeof(int))
      {
        property.SetValue(arg, instanceId, null);
        return true;
      }

      if (property.PropertyType == typeof(long))
      {
        property.SetValue(arg, (long)instanceId, null);
        return true;
      }
    }

    return true;
  }

  private static bool TryGetSceneHierarchy(out object sceneHierarchy)
  {
    sceneHierarchy = null;

    if (!TryGetHierarchyWindow(out var hierarchyWindow))
    {
      return false;
    }

    lastHierarchyWindowInstance = hierarchyWindow;
    var runtimeWindowType = hierarchyWindow.GetType();
    var shouldResolveReflection = sceneHierarchyWindowType != runtimeWindowType ||
                                  (!reflectionReady && EditorApplication.timeSinceStartup >= nextReflectionResolveTime);
    if (shouldResolveReflection)
    {
      reflectionReady = ResolveReflection(runtimeWindowType);
      if (reflectionReady)
      {
        nextReflectionResolveTime = 0;
      }
      else
      {
        sceneHierarchyWindowType = runtimeWindowType;
        nextReflectionResolveTime = EditorApplication.timeSinceStartup + 2.0d;
      }
    }

    if (reflectionReady && (useWindowDirectAccess || resolvedAccessOwnerType == runtimeWindowType))
    {
      sceneHierarchy = hierarchyWindow;
      return true;
    }

    if (reflectionReady && sceneHierarchyField != null)
    {
      sceneHierarchy = sceneHierarchyField.GetValue(hierarchyWindow);
    }

    if (reflectionReady && sceneHierarchy == null && sceneHierarchyProperty != null)
    {
      sceneHierarchy = sceneHierarchyProperty.GetValue(hierarchyWindow, null);
    }

    if (sceneHierarchy != null)
    {
      return true;
    }

    sceneHierarchy = hierarchyWindow;
    return true;
  }

  private static bool TryGetHierarchyWindow(out object hierarchyWindow)
  {
    hierarchyWindow = null;

    if (lastInteractedHierarchyWindowProperty != null)
    {
      try
      {
        hierarchyWindow = lastInteractedHierarchyWindowProperty.GetValue(null, null);
      }
      catch
      {
        hierarchyWindow = null;
      }

      if (hierarchyWindow != null)
      {
        return true;
      }
    }

    if (sceneHierarchyWindowType != null)
    {
      var typedWindows = Resources.FindObjectsOfTypeAll(sceneHierarchyWindowType);
      if (typedWindows != null && typedWindows.Length > 0)
      {
        hierarchyWindow = typedWindows[0];
        return true;
      }
    }

    var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
    EditorWindow bestWindow = null;
    var bestScore = int.MinValue;
    const BindingFlags allFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    for (var i = 0; i < windows.Length; i++)
    {
      var window = windows[i];
      if (window == null)
      {
        continue;
      }

      var type = window.GetType();
      var score = 0;
      if (type.Name.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        score += 8;
      }

      if (type.Name.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        score += 4;
      }

      var title = window.titleContent != null ? window.titleContent.text : string.Empty;
      if (!string.IsNullOrEmpty(title) && title.IndexOf("Hierarchy", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        score += 6;
      }

      if (type.GetField("m_SceneHierarchy", allFlags) != null || type.GetProperty("sceneHierarchy", allFlags) != null)
      {
        score += 10;
      }

      if (score > bestScore)
      {
        bestScore = score;
        bestWindow = window;
      }
    }

    if (bestWindow == null || bestScore <= 0)
    {
      return false;
    }

    hierarchyWindow = bestWindow;
    return true;
  }

  private static bool TryGetExpandedInstanceIds(out int[] ids)
  {
    ids = Array.Empty<int>();
    if (!TryGetSceneHierarchy(out var sceneHierarchy))
    {
      return false;
    }

    if (getExpandedIdsMethod != null)
    {
      try
      {
        var args = BuildInvocationArgs(getExpandedIdsMethod.GetParameters(), 0, null);
        var raw = getExpandedIdsMethod.Invoke(sceneHierarchy, args);
        if (TryConvertExpandedIds(raw, out ids))
        {
          missingApiLogged = false;
          return true;
        }
      }
      catch
      {
        // Ignore and fallback below.
      }
    }

    if (TryGetExpandedInstanceIdsFallback(sceneHierarchy, out ids))
    {
      missingApiLogged = false;
      return true;
    }

    if (!missingApiLogged)
    {
      Debug.LogWarning($"[HierarchyCaretMemory] Unity internal hierarchy APIs were not found; foldout memory is disabled. {BuildApiDiagnosticSummary()}");
      missingApiLogged = true;
    }

    return false;
  }

  private static bool TrySetExpandedInstanceIds(int[] ids)
  {
    if (!TryGetSceneHierarchy(out var sceneHierarchy))
    {
      return false;
    }

    if (setExpandedIdsMethod != null)
    {
      try
      {
        var parameters = setExpandedIdsMethod.GetParameters();
        if (parameters.Length > 0 && IsExpandedIdParameterType(parameters[0].ParameterType))
        {
          var args = new object[parameters.Length];
          for (var i = 0; i < parameters.Length; i++)
          {
            if (i == 0)
            {
              args[i] = ConvertExpandedIdsArgument(parameters[i].ParameterType, ids);
              continue;
            }

            if (parameters[i].HasDefaultValue)
            {
              args[i] = parameters[i].DefaultValue;
              continue;
            }

            args[i] = GetDefaultValue(parameters[i].ParameterType);
          }

          setExpandedIdsMethod.Invoke(sceneHierarchy, args);
          EditorApplication.RepaintHierarchyWindow();
          missingApiLogged = false;
          return true;
        }

        if (IsSingleExpandedSetterMethod(setExpandedIdsMethod) &&
            TryApplyExpandedIdsViaSingleSetter(sceneHierarchy, setExpandedIdsMethod, ids))
        {
          EditorApplication.RepaintHierarchyWindow();
          missingApiLogged = false;
          return true;
        }
      }
      catch
      {
        // Ignore and fallback below.
      }
    }

    if (TrySetExpandedInstanceIdsFallback(sceneHierarchy, ids))
    {
      EditorApplication.RepaintHierarchyWindow();
      missingApiLogged = false;
      return true;
    }

    if (!missingApiLogged)
    {
      Debug.LogWarning($"[HierarchyCaretMemory] Unity internal hierarchy APIs were not found; foldout memory is disabled. {BuildApiDiagnosticSummary()}");
      missingApiLogged = true;
    }

    return false;
  }

  private static bool TrySetAllExpanded(bool expand)
  {
    if (!TryGetSceneHierarchy(out var sceneHierarchy))
    {
      return false;
    }

    var actionName = expand ? "ExpandAll" : "CollapseAll";
    if (TryInvokeAnyNoArgMethod(sceneHierarchy, actionName))
    {
      EditorApplication.RepaintHierarchyWindow();
      return true;
    }

    if (lastHierarchyWindowInstance != null)
    {
      if (TryInvokeAnyNoArgMethod(lastHierarchyWindowInstance, actionName))
      {
        EditorApplication.RepaintHierarchyWindow();
        return true;
      }

      if (TryGetPropertyValueByName(lastHierarchyWindowInstance, "View", out var view) &&
          TryInvokeAnyNoArgMethod(view, actionName))
      {
        EditorApplication.RepaintHierarchyWindow();
        return true;
      }
    }

    // Fallback: collapse by clearing all expanded ids.
    if (!expand && TrySetExpandedInstanceIds(Array.Empty<int>()))
    {
      EditorApplication.RepaintHierarchyWindow();
      return true;
    }

    return false;
  }

  private static bool TryInvokeAnyNoArgMethod(object target, params string[] methodNames)
  {
    if (target == null || methodNames == null || methodNames.Length == 0)
    {
      return false;
    }

    const BindingFlags allFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    var targetType = target.GetType();

    for (var i = 0; i < methodNames.Length; i++)
    {
      var methodName = methodNames[i];
      if (string.IsNullOrEmpty(methodName))
      {
        continue;
      }

      var exactMethod = targetType.GetMethod(methodName, allFlags, null, Type.EmptyTypes, null);
      if (TryInvokeNoArgMethod(target, exactMethod))
      {
        return true;
      }
    }

    var methods = targetType.GetMethods(allFlags);
    for (var i = 0; i < methods.Length; i++)
    {
      var method = methods[i];
      if (method == null || method.GetParameters().Length != 0)
      {
        continue;
      }

      for (var j = 0; j < methodNames.Length; j++)
      {
        var methodName = methodNames[j];
        if (string.IsNullOrEmpty(methodName))
        {
          continue;
        }

        if (!string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        if (TryInvokeNoArgMethod(target, method))
        {
          return true;
        }
      }
    }

    return false;
  }

  private static bool TryInvokeNoArgMethod(object target, MethodInfo method)
  {
    if (target == null || method == null)
    {
      return false;
    }

    try
    {
      method.Invoke(target, null);
      return true;
    }
    catch
    {
      return false;
    }
  }

  private static bool TryGetPropertyValueByName(object target, string propertyName, out object value)
  {
    value = null;
    if (target == null || string.IsNullOrEmpty(propertyName))
    {
      return false;
    }

    const BindingFlags allFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    var property = target.GetType().GetProperty(propertyName, allFlags);
    if (property == null || !property.CanRead || property.GetIndexParameters().Length > 0)
    {
      return false;
    }

    try
    {
      value = property.GetValue(target, null);
      return value != null;
    }
    catch
    {
      value = null;
      return false;
    }
  }

  private static bool TryApplyExpandedIdsViaSingleSetter(object sceneHierarchy, MethodInfo setter, int[] targetExpandedIds)
  {
    if (sceneHierarchy == null || setter == null || !IsSingleExpandedSetterMethod(setter))
    {
      return false;
    }

    var targetSet = new HashSet<int>(targetExpandedIds ?? Array.Empty<int>());
    var hadInvocationFailure = false;

    if (TryGetExpandedIdsViaResolvedGetter(sceneHierarchy, out var currentExpandedIds))
    {
      for (var i = 0; i < currentExpandedIds.Length; i++)
      {
        var id = currentExpandedIds[i];
        if (targetSet.Contains(id))
        {
          continue;
        }

        if (TryInvokeSingleExpandedSetter(sceneHierarchy, setter, id, false))
        {
          continue;
        }

        hadInvocationFailure = true;
      }
    }

    foreach (var id in targetSet)
    {
      if (TryInvokeSingleExpandedSetter(sceneHierarchy, setter, id, true))
      {
        continue;
      }

      hadInvocationFailure = true;
    }

    return !hadInvocationFailure;
  }

  private static bool TryGetExpandedIdsViaResolvedGetter(object sceneHierarchy, out int[] ids)
  {
    ids = Array.Empty<int>();
    if (sceneHierarchy == null || getExpandedIdsMethod == null)
    {
      return false;
    }

    try
    {
      var args = BuildInvocationArgs(getExpandedIdsMethod.GetParameters(), 0, null);
      var raw = getExpandedIdsMethod.Invoke(sceneHierarchy, args);
      return TryConvertExpandedIds(raw, out ids);
    }
    catch
    {
      return false;
    }
  }

  private static bool TryInvokeSingleExpandedSetter(object sceneHierarchy, MethodInfo setter, int id, bool expanded)
  {
    if (sceneHierarchy == null || setter == null || !IsSingleExpandedSetterMethod(setter))
    {
      return false;
    }

    try
    {
      var parameters = setter.GetParameters();
      var args = new object[parameters.Length];
      args[0] = id;
      args[1] = expanded;

      for (var i = 2; i < parameters.Length; i++)
      {
        if (parameters[i].HasDefaultValue)
        {
          args[i] = parameters[i].DefaultValue;
          continue;
        }

        args[i] = GetDefaultValue(parameters[i].ParameterType);
      }

      setter.Invoke(sceneHierarchy, args);
      return true;
    }
    catch
    {
      return false;
    }
  }

  private static object ConvertExpandedIdsArgument(Type parameterType, int[] ids)
  {
    if (parameterType == null)
    {
      return new List<int>(ids);
    }

    if (parameterType == typeof(int[]))
    {
      return ids;
    }

    var list = new List<int>(ids);
    if (parameterType.IsAssignableFrom(typeof(List<int>)))
    {
      return list;
    }

    if (parameterType.IsAssignableFrom(typeof(int[])))
    {
      return ids;
    }

    if (!parameterType.IsInterface && !parameterType.IsAbstract)
    {
      try
      {
        var instance = Activator.CreateInstance(parameterType);
        if (instance is IList<int> genericList)
        {
          for (var i = 0; i < ids.Length; i++)
          {
            genericList.Add(ids[i]);
          }

          return genericList;
        }

        if (instance is IList nonGenericList)
        {
          for (var i = 0; i < ids.Length; i++)
          {
            nonGenericList.Add(ids[i]);
          }

          return nonGenericList;
        }
      }
      catch
      {
        // Ignore and fallback below.
      }
    }

    return list;
  }

  private static bool AreParametersInvokableWithDefaults(ParameterInfo[] parameters)
  {
    if (parameters == null || parameters.Length == 0)
    {
      return true;
    }

    if (parameters.Length > 3)
    {
      return false;
    }

    for (var i = 0; i < parameters.Length; i++)
    {
      var parameter = parameters[i];
      if (parameter.IsOptional || parameter.HasDefaultValue)
      {
        continue;
      }

      if (parameter.ParameterType.IsByRef)
      {
        return false;
      }

      // Allow non-optional params if we can provide a default value safely.
      if (parameter.ParameterType.IsValueType || !parameter.ParameterType.IsPointer)
      {
        continue;
      }

      return false;
    }

    return true;
  }

  private static object[] BuildInvocationArgs(ParameterInfo[] parameters, int expandedIdsIndex, int[] expandedIds)
  {
    if (parameters == null || parameters.Length == 0)
    {
      return null;
    }

    var args = new object[parameters.Length];
    for (var i = 0; i < parameters.Length; i++)
    {
      if (i == expandedIdsIndex && expandedIds != null)
      {
        args[i] = ConvertExpandedIdsArgument(parameters[i].ParameterType, expandedIds);
        continue;
      }

      if (parameters[i].HasDefaultValue)
      {
        args[i] = parameters[i].DefaultValue;
        continue;
      }

      args[i] = GetDefaultValue(parameters[i].ParameterType);
    }

    return args;
  }

  private static bool TryUseNewHierarchyPersistence(bool restore)
  {
    if (!TryGetHierarchyWindow(out var hierarchyWindow) || hierarchyWindow == null)
    {
      return false;
    }

    var windowType = hierarchyWindow.GetType();
    if (!IsNewHierarchyWindowType(windowType))
    {
      return false;
    }

    EnsureNewHierarchyWindowMethods(windowType);
    if (newHierarchyViewProperty == null ||
        newHierarchyViewGetStateMethod == null ||
        newHierarchyViewSetStateMethod == null ||
        newHierarchyStateViewModelStateField == null)
    {
      return false;
    }

    var prefKey = GetNewHierarchyPrefsKey(currentContextKey);

    if (restore)
    {
      if (!EditorPrefs.HasKey(prefKey))
      {
        missingApiLogged = false;
        lastNewHierarchySavedState = string.Empty;
        return true;
      }

      var encoded = EditorPrefs.GetString(prefKey, string.Empty);
      var payload = DecodeHierarchyStatePayload(encoded);
      if (payload == null)
      {
        payload = Array.Empty<byte>();
      }

      if (!TrySetNewHierarchyViewModelState(hierarchyWindow, payload))
      {
        return false;
      }

      missingApiLogged = false;
      lastNewHierarchySavedState = encoded;
      EditorApplication.RepaintHierarchyWindow();
      return true;
    }

    var now = EditorApplication.timeSinceStartup;
    if (now - lastNewHierarchySaveTime < 0.8d)
    {
      return true;
    }

    if (!TryGetNewHierarchyViewModelState(hierarchyWindow, out var payloadBytes))
    {
      return false;
    }

    var encodedPayload = EncodeHierarchyStatePayload(payloadBytes);
    if (!string.Equals(encodedPayload, lastNewHierarchySavedState, StringComparison.Ordinal))
    {
      EditorPrefs.SetString(prefKey, encodedPayload);
      lastNewHierarchySavedState = encodedPayload;
    }

    lastNewHierarchySaveTime = now;
    missingApiLogged = false;
    return true;
  }

  private static bool IsNewHierarchyWindowType(Type windowType)
  {
    if (windowType == null)
    {
      return false;
    }

    var fullName = windowType.FullName ?? string.Empty;
    return fullName.IndexOf("Unity.Hierarchy.Editor.HierarchyWindow", StringComparison.Ordinal) >= 0;
  }

  private static void EnsureNewHierarchyWindowMethods(Type windowType)
  {
    if (newHierarchyWindowTypeCache == windowType)
    {
      return;
    }

    newHierarchyWindowTypeCache = windowType;
    const BindingFlags allFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    newHierarchyViewProperty = windowType.GetProperty("View", allFlags);
    newHierarchyViewGetStateMethod = null;
    newHierarchyViewSetStateMethod = null;
    newHierarchyStateType = null;
    newHierarchyStateViewModelStateField = null;
    newHierarchyStateValidContentField = null;
    newHierarchyContentType = null;
    newHierarchyContentAllValue = null;
    newHierarchyContentViewModelStateValue = null;

    if (newHierarchyViewProperty == null)
    {
      return;
    }

    var hierarchyViewType = newHierarchyViewProperty.PropertyType;
    foreach (var method in hierarchyViewType.GetMethods(allFlags))
    {
      if (newHierarchyViewGetStateMethod == null &&
          string.Equals(method.Name, "GetState", StringComparison.Ordinal))
      {
        var parameters = method.GetParameters();
        if (parameters.Length == 1)
        {
          newHierarchyViewGetStateMethod = method;
          newHierarchyContentType = parameters[0].ParameterType;
        }
      }

      if (newHierarchyViewSetStateMethod == null &&
          string.Equals(method.Name, "SetState", StringComparison.Ordinal))
      {
        var parameters = method.GetParameters();
        if (parameters.Length == 1)
        {
          newHierarchyViewSetStateMethod = method;
          newHierarchyStateType = parameters[0].ParameterType;
        }
      }
    }

    if (newHierarchyStateType == null && newHierarchyViewGetStateMethod != null)
    {
      newHierarchyStateType = newHierarchyViewGetStateMethod.ReturnType;
    }

    if (newHierarchyStateType == null)
    {
      return;
    }

    newHierarchyStateViewModelStateField = newHierarchyStateType.GetField("ViewModelState", allFlags);
    newHierarchyStateValidContentField = newHierarchyStateType.GetField("ValidContent", allFlags);

    if (newHierarchyContentType == null && newHierarchyStateValidContentField != null)
    {
      newHierarchyContentType = newHierarchyStateValidContentField.FieldType;
    }

    if (newHierarchyContentType != null && newHierarchyContentType.IsEnum)
    {
      if (TryGetEnumValueIgnoreCase(newHierarchyContentType, "All", out var allValue))
      {
        newHierarchyContentAllValue = allValue;
      }

      if (TryGetEnumValueIgnoreCase(newHierarchyContentType, "ViewModelState", out var viewModelStateValue))
      {
        newHierarchyContentViewModelStateValue = viewModelStateValue;
      }

      if (newHierarchyContentAllValue == null)
      {
        newHierarchyContentAllValue = newHierarchyContentViewModelStateValue;
      }
    }
  }

  private static string GetNewHierarchyPrefsKey(string contextKey)
  {
    return GetPrefsKey("NHVM:" + contextKey);
  }

  private static bool TryGetNewHierarchyViewModelState(object hierarchyWindow, out byte[] payload)
  {
    payload = Array.Empty<byte>();
    if (hierarchyWindow == null ||
        newHierarchyViewProperty == null ||
        newHierarchyViewGetStateMethod == null ||
        newHierarchyStateViewModelStateField == null)
    {
      return false;
    }

    try
    {
      var view = newHierarchyViewProperty.GetValue(hierarchyWindow, null);
      if (view == null)
      {
        return false;
      }

      var state = newHierarchyViewGetStateMethod.Invoke(view, BuildNewHierarchyGetStateArgs());
      if (state == null)
      {
        return false;
      }

      payload = newHierarchyStateViewModelStateField.GetValue(state) as byte[] ?? Array.Empty<byte>();
      return true;
    }
    catch
    {
      return false;
    }
  }

  private static bool TrySetNewHierarchyViewModelState(object hierarchyWindow, byte[] payload)
  {
    if (hierarchyWindow == null ||
        newHierarchyViewProperty == null ||
        newHierarchyViewGetStateMethod == null ||
        newHierarchyViewSetStateMethod == null ||
        newHierarchyStateViewModelStateField == null)
    {
      return false;
    }

    try
    {
      var view = newHierarchyViewProperty.GetValue(hierarchyWindow, null);
      if (view == null)
      {
        return false;
      }

      var state = newHierarchyViewGetStateMethod.Invoke(view, BuildNewHierarchyGetStateArgs());
      if (state == null && newHierarchyStateType != null)
      {
        state = Activator.CreateInstance(newHierarchyStateType);
      }

      if (state == null)
      {
        return false;
      }

      newHierarchyStateViewModelStateField.SetValue(state, payload ?? Array.Empty<byte>());
      if (newHierarchyStateValidContentField != null)
      {
        ApplyNewHierarchyValidContent(state);
      }

      newHierarchyViewSetStateMethod.Invoke(view, new[] { state });
      return true;
    }
    catch
    {
      return false;
    }
  }

  private static object[] BuildNewHierarchyGetStateArgs()
  {
    if (newHierarchyViewGetStateMethod == null)
    {
      return null;
    }

    var parameters = newHierarchyViewGetStateMethod.GetParameters();
    if (parameters.Length == 0)
    {
      return null;
    }

    var args = new object[parameters.Length];
    args[0] = newHierarchyContentAllValue ??
              newHierarchyContentViewModelStateValue ??
              GetDefaultValue(parameters[0].ParameterType);

    for (var i = 1; i < parameters.Length; i++)
    {
      if (parameters[i].HasDefaultValue)
      {
        args[i] = parameters[i].DefaultValue;
        continue;
      }

      args[i] = GetDefaultValue(parameters[i].ParameterType);
    }

    return args;
  }

  private static void ApplyNewHierarchyValidContent(object state)
  {
    if (state == null || newHierarchyStateValidContentField == null)
    {
      return;
    }

    var targetType = newHierarchyStateValidContentField.FieldType;
    if (targetType == null || !targetType.IsEnum)
    {
      return;
    }

    try
    {
      var current = newHierarchyStateValidContentField.GetValue(state);
      if (current == null)
      {
        if (newHierarchyContentAllValue != null)
        {
          newHierarchyStateValidContentField.SetValue(state, newHierarchyContentAllValue);
        }
        else if (newHierarchyContentViewModelStateValue != null)
        {
          newHierarchyStateValidContentField.SetValue(state, newHierarchyContentViewModelStateValue);
        }

        return;
      }

      if (newHierarchyContentViewModelStateValue == null)
      {
        if (newHierarchyContentAllValue != null)
        {
          newHierarchyStateValidContentField.SetValue(state, newHierarchyContentAllValue);
        }

        return;
      }

      var currentBits = Convert.ToInt64(current);
      var viewModelBits = Convert.ToInt64(newHierarchyContentViewModelStateValue);
      var merged = currentBits | viewModelBits;
      newHierarchyStateValidContentField.SetValue(state, Enum.ToObject(targetType, merged));
    }
    catch
    {
      // Ignore; state will still be applied without altering ValidContent.
    }
  }

  private static bool TryGetEnumValueIgnoreCase(Type enumType, string name, out object value)
  {
    value = null;
    if (enumType == null || !enumType.IsEnum || string.IsNullOrEmpty(name))
    {
      return false;
    }

    var names = Enum.GetNames(enumType);
    for (var i = 0; i < names.Length; i++)
    {
      if (!string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      value = Enum.Parse(enumType, names[i], ignoreCase: false);
      return true;
    }

    return false;
  }

  private static string EncodeHierarchyStatePayload(byte[] payload)
  {
    if (payload == null || payload.Length == 0)
    {
      return string.Empty;
    }

    return Convert.ToBase64String(payload);
  }

  private static byte[] DecodeHierarchyStatePayload(string encodedPayload)
  {
    if (string.IsNullOrEmpty(encodedPayload))
    {
      return Array.Empty<byte>();
    }

    try
    {
      return Convert.FromBase64String(encodedPayload);
    }
    catch
    {
      return null;
    }
  }

  private static string BuildApiDiagnosticSummary()
  {
    var windowTypeName = lastHierarchyWindowInstance != null ? lastHierarchyWindowInstance.GetType().FullName : "null";
    var ownerTypeName = resolvedAccessOwnerType != null ? resolvedAccessOwnerType.FullName : "null";
    var getMethodName = getExpandedIdsMethod != null ? getExpandedIdsMethod.Name : "null";
    var setMethodName = setExpandedIdsMethod != null ? setExpandedIdsMethod.Name : "null";
    var newHierarchyTypeName = newHierarchyWindowTypeCache != null ? newHierarchyWindowTypeCache.FullName : "null";
    var hasNewView = newHierarchyViewProperty != null;
    var hasNewGetState = newHierarchyViewGetStateMethod != null;
    var hasNewSetState = newHierarchyViewSetStateMethod != null;
    var hasNewViewModelField = newHierarchyStateViewModelStateField != null;

    return $"windowType={windowTypeName}; ownerType={ownerTypeName}; get={getMethodName}; set={setMethodName}; newHierarchyType={newHierarchyTypeName}; newView={hasNewView}; newGetState={hasNewGetState}; newSetState={hasNewSetState}; newViewModelState={hasNewViewModelField}";
  }

  private static bool TryGetExpandedInstanceIdsFallback(object sceneHierarchy, out int[] ids)
  {
    if (TryGetExpandedInstanceIdsFallbackCore(sceneHierarchy, out ids))
    {
      return true;
    }

    InvalidateFallbackAccessor();
    return TryGetExpandedInstanceIdsFallbackCore(sceneHierarchy, out ids);
  }

  private static bool TryGetExpandedInstanceIdsFallbackCore(object sceneHierarchy, out int[] ids)
  {
    ids = Array.Empty<int>();
    if (!EnsureFallbackExpandedIdsAccessor(sceneHierarchy))
    {
      return false;
    }

    var root = fallbackAccessorUseSceneHierarchyRoot ? sceneHierarchy : lastHierarchyWindowInstance;
    if (!TryTraversePath(root, fallbackAccessorPathToOwner, out var owner) || owner == null)
    {
      return false;
    }

    if (!TryGetMemberValue(owner, fallbackAccessorExpandedIdsMember, out var raw))
    {
      return false;
    }

    return TryConvertExpandedIds(raw, out ids);
  }

  private static bool TrySetExpandedInstanceIdsFallback(object sceneHierarchy, int[] ids)
  {
    if (TrySetExpandedInstanceIdsFallbackCore(sceneHierarchy, ids))
    {
      return true;
    }

    InvalidateFallbackAccessor();
    return TrySetExpandedInstanceIdsFallbackCore(sceneHierarchy, ids);
  }

  private static bool TrySetExpandedInstanceIdsFallbackCore(object sceneHierarchy, int[] ids)
  {
    if (!EnsureFallbackExpandedIdsAccessor(sceneHierarchy))
    {
      return false;
    }

    var root = fallbackAccessorUseSceneHierarchyRoot ? sceneHierarchy : lastHierarchyWindowInstance;
    if (!TryTraversePath(root, fallbackAccessorPathToOwner, out var owner) || owner == null)
    {
      return false;
    }

    return TrySetExpandedIdsOnMember(owner, fallbackAccessorExpandedIdsMember, ids);
  }

  private static bool EnsureFallbackExpandedIdsAccessor(object sceneHierarchy)
  {
    var hierarchyWindow = lastHierarchyWindowInstance;
    if (hierarchyWindow == null)
    {
      return false;
    }

    var windowType = hierarchyWindow.GetType();
    if (fallbackAccessorWindowType == windowType && fallbackAccessorExpandedIdsMember != null)
    {
      return true;
    }

    if (fallbackAccessorWindowType == windowType &&
        fallbackAccessorExpandedIdsMember == null &&
        EditorApplication.timeSinceStartup < fallbackAccessorNextResolveTime)
    {
      return false;
    }

    InvalidateFallbackAccessor();
    fallbackAccessorWindowType = windowType;
    fallbackAccessorNextResolveTime = EditorApplication.timeSinceStartup + 2.0d;

    if (TryResolveExpandedIdsMemberPath(hierarchyWindow, out var windowPath, out var windowMember))
    {
      fallbackAccessorUseSceneHierarchyRoot = false;
      fallbackAccessorPathToOwner = windowPath;
      fallbackAccessorExpandedIdsMember = windowMember;
      fallbackAccessorNextResolveTime = 0;
      return true;
    }

    if (sceneHierarchy != null &&
        !ReferenceEquals(sceneHierarchy, hierarchyWindow) &&
        TryResolveExpandedIdsMemberPath(sceneHierarchy, out var hierarchyPath, out var hierarchyMember))
    {
      fallbackAccessorUseSceneHierarchyRoot = true;
      fallbackAccessorPathToOwner = hierarchyPath;
      fallbackAccessorExpandedIdsMember = hierarchyMember;
      fallbackAccessorNextResolveTime = 0;
      return true;
    }

    return false;
  }

  private static void InvalidateFallbackAccessor()
  {
    fallbackAccessorWindowType = null;
    fallbackAccessorUseSceneHierarchyRoot = false;
    fallbackAccessorPathToOwner = Array.Empty<MemberInfo>();
    fallbackAccessorExpandedIdsMember = null;
    fallbackAccessorNextResolveTime = 0;
  }

  private static bool TryResolveExpandedIdsMemberPath(object root, out MemberInfo[] pathToOwner, out MemberInfo expandedMember)
  {
    pathToOwner = Array.Empty<MemberInfo>();
    expandedMember = null;

    if (root == null)
    {
      return false;
    }

    const int maxDepth = 8;
    const int maxNodes = 2500;
    var visitedNodes = 0;
    var queue = new Queue<ReflectionPathNode>();
    var visited = new HashSet<object>(ReferenceIdentityComparer.Instance);
    queue.Enqueue(new ReflectionPathNode(root, Array.Empty<MemberInfo>(), 0));
    visited.Add(root);

    while (queue.Count > 0)
    {
      var node = queue.Dequeue();
      visitedNodes++;
      if (visitedNodes > maxNodes)
      {
        break;
      }

      if (TryFindExpandedIdsMember(node.Instance.GetType(), out var candidateExpandedMember))
      {
        if (TryGetMemberValue(node.Instance, candidateExpandedMember, out var raw))
        {
          if (TryConvertExpandedIds(raw, out _) ||
              (raw == null && IsExpandedIdsMemberType(GetMemberType(candidateExpandedMember))))
          {
            pathToOwner = node.Path;
            expandedMember = candidateExpandedMember;
            return true;
          }
        }
        else if (IsExpandedIdsMemberType(GetMemberType(candidateExpandedMember)))
        {
          pathToOwner = node.Path;
          expandedMember = candidateExpandedMember;
          return true;
        }
      }

      if (node.Depth >= maxDepth)
      {
        continue;
      }

      foreach (var childMember in EnumerateChildMembers(node.Instance.GetType()))
      {
        if (!TryGetMemberValue(node.Instance, childMember, out var child) || child == null)
        {
          continue;
        }

        if (child is string)
        {
          continue;
        }

        var childType = child.GetType();
        if (childType.IsPrimitive || childType.IsEnum)
        {
          continue;
        }

        if (!visited.Add(child))
        {
          continue;
        }

        queue.Enqueue(new ReflectionPathNode(child, AppendPath(node.Path, childMember), node.Depth + 1));
      }
    }

    return false;
  }

  private static bool TryFindExpandedIdsMember(Type type, out MemberInfo member)
  {
    member = null;
    if (type == null)
    {
      return false;
    }

    const BindingFlags allFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    var exactNames = new[] { "expandedIDs", "expandedIds", "m_ExpandedIDs", "m_ExpandedIds" };

    foreach (var exactName in exactNames)
    {
      var field = type.GetField(exactName, allFlags);
      if (field != null && IsExpandedIdsMemberType(field.FieldType))
      {
        member = field;
        return true;
      }
    }

    foreach (var exactName in exactNames)
    {
      var property = type.GetProperty(exactName, allFlags);
      if (property != null &&
          property.GetIndexParameters().Length == 0 &&
          IsSafePropertyRead(property) &&
          IsExpandedIdsMemberType(property.PropertyType))
      {
        member = property;
        return true;
      }
    }

    foreach (var field in type.GetFields(allFlags))
    {
      if (field.IsStatic)
      {
        continue;
      }

      if (field.Name.IndexOf("expanded", StringComparison.OrdinalIgnoreCase) >= 0 &&
          IsExpandedIdsMemberType(field.FieldType))
      {
        member = field;
        return true;
      }
    }

    return false;
  }

  private static IEnumerable<MemberInfo> EnumerateChildMembers(Type type)
  {
    if (type == null)
    {
      yield break;
    }

    const BindingFlags allFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    foreach (var field in type.GetFields(allFlags))
    {
      if (field.IsStatic || !ShouldTraverseMember(field.Name, field.FieldType))
      {
        continue;
      }

      yield return field;
    }

    foreach (var property in type.GetProperties(allFlags))
    {
      if (!IsSafePropertyRead(property))
      {
        continue;
      }

      if (!ShouldTraverseMember(property.Name, property.PropertyType))
      {
        continue;
      }

      yield return property;
    }
  }

  private static bool ShouldTraverseMember(string memberName, Type memberType)
  {
    if (memberType == null)
    {
      return false;
    }

    if (memberType.IsPrimitive || memberType.IsEnum || memberType == typeof(string))
    {
      return false;
    }

    if (typeof(Delegate).IsAssignableFrom(memberType))
    {
      return false;
    }

    if (ContainsHierarchyKeyword(memberName) || ContainsHierarchyKeyword(memberType.Name))
    {
      return true;
    }

    if (!string.IsNullOrEmpty(memberType.Namespace) &&
        memberType.Namespace.StartsWith("UnityEditor", StringComparison.Ordinal))
    {
      return true;
    }

    if (!string.IsNullOrEmpty(memberType.Namespace) &&
        memberType.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal) &&
        ContainsHierarchyKeyword(memberName))
    {
      return true;
    }

    return false;
  }

  private static bool ContainsHierarchyKeyword(string value)
  {
    if (string.IsNullOrEmpty(value))
    {
      return false;
    }

    return value.IndexOf("hierarchy", StringComparison.OrdinalIgnoreCase) >= 0 ||
           value.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
           value.IndexOf("state", StringComparison.OrdinalIgnoreCase) >= 0 ||
           value.IndexOf("expanded", StringComparison.OrdinalIgnoreCase) >= 0 ||
           value.IndexOf("scene", StringComparison.OrdinalIgnoreCase) >= 0 ||
           value.IndexOf("view", StringComparison.OrdinalIgnoreCase) >= 0 ||
           value.IndexOf("data", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static bool IsExpandedIdsMemberType(Type memberType)
  {
    if (memberType == null)
    {
      return false;
    }

    if (memberType == typeof(int[]))
    {
      return true;
    }

    if (memberType.IsArray && memberType.GetElementType() == typeof(int))
    {
      return true;
    }

    if (memberType.IsGenericType)
    {
      var genericArgs = memberType.GetGenericArguments();
      if (genericArgs.Length == 1 && genericArgs[0] == typeof(int))
      {
        return true;
      }
    }

    return typeof(IEnumerable).IsAssignableFrom(memberType);
  }

  private static bool TryTraversePath(object root, MemberInfo[] path, out object value)
  {
    value = root;
    if (value == null)
    {
      return false;
    }

    if (path == null || path.Length == 0)
    {
      return true;
    }

    for (var i = 0; i < path.Length; i++)
    {
      if (!TryGetMemberValue(value, path[i], out var next) || next == null)
      {
        return false;
      }

      value = next;
    }

    return true;
  }

  private static MemberInfo[] AppendPath(MemberInfo[] path, MemberInfo nextMember)
  {
    if (path == null)
    {
      path = Array.Empty<MemberInfo>();
    }

    var result = new MemberInfo[path.Length + 1];
    if (path.Length > 0)
    {
      Array.Copy(path, result, path.Length);
    }

    result[path.Length] = nextMember;
    return result;
  }

  private static bool TryGetMemberValue(object instance, MemberInfo member, out object value)
  {
    value = null;
    if (instance == null || member == null)
    {
      return false;
    }

    try
    {
      switch (member)
      {
        case FieldInfo field:
          value = field.GetValue(instance);
          return true;
        case PropertyInfo property:
          if (!IsSafePropertyRead(property))
          {
            return false;
          }

          if (property.GetIndexParameters().Length > 0 || !property.CanRead)
          {
            return false;
          }

          value = property.GetValue(instance, null);
          return true;
        default:
          return false;
      }
    }
    catch
    {
      return false;
    }
  }

  private static bool TrySetMemberValue(object instance, MemberInfo member, object value)
  {
    if (instance == null || member == null)
    {
      return false;
    }

    try
    {
      switch (member)
      {
        case FieldInfo field:
          if (field.IsInitOnly || field.IsLiteral)
          {
            return false;
          }

          field.SetValue(instance, value);
          return true;
        case PropertyInfo property:
          if (!property.CanWrite || property.GetIndexParameters().Length > 0)
          {
            return false;
          }

          property.SetValue(instance, value, null);
          return true;
        default:
          return false;
      }
    }
    catch
    {
      return false;
    }
  }

  private static bool TryConvertExpandedIds(object raw, out int[] ids)
  {
    ids = Array.Empty<int>();
    if (raw == null)
    {
      return false;
    }

    switch (raw)
    {
      case int[] typedArray:
        ids = typedArray;
        return true;
      case IList<int> typedList:
      {
        ids = new int[typedList.Count];
        typedList.CopyTo(ids, 0);
        return true;
      }
      case IEnumerable enumerable:
      {
        var buffer = new List<int>();
        foreach (var value in enumerable)
        {
          if (value == null)
          {
            continue;
          }

          if (value is int intValue)
          {
            buffer.Add(intValue);
            continue;
          }

          try
          {
            buffer.Add(Convert.ToInt32(value));
          }
          catch
          {
            return false;
          }
        }

        ids = buffer.ToArray();
        return true;
      }
      default:
        return false;
    }
  }

  private static bool TrySetExpandedIdsOnMember(object owner, MemberInfo expandedMember, int[] ids)
  {
    if (owner == null || expandedMember == null)
    {
      return false;
    }

    var memberType = GetMemberType(expandedMember);
    var converted = ConvertExpandedIdsArgument(memberType, ids);
    if (TrySetMemberValue(owner, expandedMember, converted))
    {
      return true;
    }

    if (!TryGetMemberValue(owner, expandedMember, out var current) || current == null)
    {
      return false;
    }

    if (current is IList<int> genericList)
    {
      genericList.Clear();
      for (var i = 0; i < ids.Length; i++)
      {
        genericList.Add(ids[i]);
      }

      return true;
    }

    if (current is IList nonGenericList)
    {
      nonGenericList.Clear();
      for (var i = 0; i < ids.Length; i++)
      {
        nonGenericList.Add(ids[i]);
      }

      return true;
    }

    return false;
  }

  private static Type GetMemberType(MemberInfo member)
  {
    switch (member)
    {
      case FieldInfo field:
        return field.FieldType;
      case PropertyInfo property:
        return property.PropertyType;
      default:
        return null;
    }
  }

  private static bool IsSafePropertyRead(PropertyInfo property)
  {
    if (property == null)
    {
      return false;
    }

    var getter = property.GetGetMethod(true);
    if (getter == null || getter.IsStatic || property.GetIndexParameters().Length > 0)
    {
      return false;
    }

    var name = property.Name ?? string.Empty;
    if (name.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("glyph", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("font", StringComparison.OrdinalIgnoreCase) >= 0)
    {
      return false;
    }

    if (ContainsHierarchyKeyword(name))
    {
      return true;
    }

    var declaringNs = property.DeclaringType != null ? property.DeclaringType.Namespace : string.Empty;
    if (!string.IsNullOrEmpty(declaringNs) &&
        declaringNs.StartsWith("UnityEditor", StringComparison.Ordinal))
    {
      return true;
    }

    return false;
  }

  private static object GetDefaultValue(Type type)
  {
    if (type == null)
    {
      return null;
    }

    if (type.IsByRef)
    {
      type = type.GetElementType();
      if (type == null)
      {
        return null;
      }
    }

    if (type.IsPointer)
    {
      return null;
    }

    return type.IsValueType ? Activator.CreateInstance(type) : null;
  }

  private sealed class ReflectionPathNode
  {
    public readonly object Instance;
    public readonly MemberInfo[] Path;
    public readonly int Depth;

    public ReflectionPathNode(object instance, MemberInfo[] path, int depth)
    {
      Instance = instance;
      Path = path;
      Depth = depth;
    }
  }

  private sealed class ReferenceIdentityComparer : IEqualityComparer<object>
  {
    public static readonly ReferenceIdentityComparer Instance = new ReferenceIdentityComparer();

    public new bool Equals(object x, object y)
    {
      return ReferenceEquals(x, y);
    }

    public int GetHashCode(object obj)
    {
      return obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
    }
  }
}
#endif
