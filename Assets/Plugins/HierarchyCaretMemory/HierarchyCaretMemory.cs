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

public static partial class HierarchyCaretMemory
{
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
  private static MethodInfo instanceIdToObjectMethod;
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
}
#endif
