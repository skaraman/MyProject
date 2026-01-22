using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
#if UNITY_6000_3_OR_NEWER
using EntityId = UnityEngine.EntityId;
#endif

namespace BrunoMikoski.SceneHierarchyKeeper {
  public static class UnityHierarchyTools {
    private const string UNITY_EDITOR_SCENE_HIERARCHY_WINDOW_TYPE_NAME = "UnityEditor.SceneHierarchyWindow";
    private const string EXPAND_TREE_VIEW_ITEM_METHOD_NAME = "ExpandTreeViewItem";
    private const string GET_EXPANDED_I_DS_METHOD_NAME = "GetExpandedIDs";
    private const string SCENE_HIERARCHY_PROPERTY_NAME = "sceneHierarchy";

    private static Type cachedSceneHierarchyWindowType;
    private static Type SceneHierarchyWindowType {
      get {
        if (cachedSceneHierarchyWindowType == null) {
                    // UnityCsReference: Editor/Mono/SceneHierarchyWindow.cs
          cachedSceneHierarchyWindowType =
              typeof(EditorWindow).Assembly.GetType(UNITY_EDITOR_SCENE_HIERARCHY_WINDOW_TYPE_NAME);
        }

        return cachedSceneHierarchyWindowType;
      }
    }

    private static EditorWindow cachedHierarchyWindow;
    internal static EditorWindow HierarchyWindow {
      get {
        if (cachedHierarchyWindow == null) {
          Object[] allWindows = Resources.FindObjectsOfTypeAll(SceneHierarchyWindowType);
          if (allWindows.Length > 0)
            cachedHierarchyWindow = (EditorWindow)allWindows[0];
        }
        return cachedHierarchyWindow;
      }
    }

    private static MethodInfo cachedSetExpandedMethodInfo;
    private static MethodInfo SetExpandedMethodInfo {
      get {
        if (cachedSetExpandedMethodInfo == null) {
          cachedSetExpandedMethodInfo = SceneHierarchyProperty.GetType().GetMethod(
              EXPAND_TREE_VIEW_ITEM_METHOD_NAME, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        return cachedSetExpandedMethodInfo;
      }
    }

    private static object cachedSceneHierarchyProperty;
    private static object SceneHierarchyProperty {
      get {
        if (cachedSceneHierarchyProperty == null) {
          cachedSceneHierarchyProperty = SceneHierarchyWindowType.GetProperty(SCENE_HIERARCHY_PROPERTY_NAME)
              .GetValue(HierarchyWindow);
        }

        return cachedSceneHierarchyProperty;
      }
    }

    private static MethodInfo cachedGetExpandedIDsMethodInfo;
    private static MethodInfo GetExpandedIDsMethodInfo {
      get {
        if (cachedGetExpandedIDsMethodInfo == null) {
          cachedGetExpandedIDsMethodInfo = SceneHierarchyWindowType.GetMethod(GET_EXPANDED_I_DS_METHOD_NAME,
              BindingFlags.NonPublic | BindingFlags.Instance);
        }
        return cachedGetExpandedIDsMethodInfo;
      }
    }



    internal static void SetExpanded(int id, bool isExpanded) {
      if (SetExpandedMethodInfo == null || SceneHierarchyProperty == null)
        return;

#if UNITY_6000_3_OR_NEWER
      ParameterInfo[] parameters = SetExpandedMethodInfo.GetParameters();
      if (parameters.Length > 0 && parameters[0].ParameterType == typeof(EntityId)) {
        EntityId entityId = ConvertInstanceIdToEntityId(id);
        SetExpandedMethodInfo.Invoke(SceneHierarchyProperty, new object[] { entityId, isExpanded });
        return;
      }
#endif

      SetExpandedMethodInfo.Invoke(SceneHierarchyProperty, new object[] { id, isExpanded });
    }

    public static bool IsHierarchyWindowOpen() {
      return HierarchyWindow != null;
    }

    public static int[] GetExpandedItems() {
      if (GetExpandedIDsMethodInfo == null || HierarchyWindow == null)
        return Array.Empty<int>();

      object result = GetExpandedIDsMethodInfo.Invoke(HierarchyWindow, null);

      if (result is int[] intIds)
        return intIds;

#if UNITY_6000_3_OR_NEWER
      if (result is EntityId[] entityIds)
        return ConvertEntityIdsToInstanceIds(entityIds);
#endif

      return Array.Empty<int>();
    }

#if UNITY_6000_3_OR_NEWER
    private static ConstructorInfo cachedEntityIdFromIntCtor;
    private static ConstructorInfo cachedEntityIdFromLongCtor;
    private static ConstructorInfo cachedEntityIdFromULongCtor;
    private static bool cachedEntityIdCtorsSearched;

    private static EntityId ConvertInstanceIdToEntityId(int id) {
      Object obj = EditorUtility.InstanceIDToObject(id);
      if (obj != null)
        return obj.GetEntityId();

      Scene scene = FindSceneByHandle(id);
      if (scene.IsValid() && !string.IsNullOrEmpty(scene.path)) {
        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);
        if (sceneAsset != null)
          return sceneAsset.GetEntityId();
      }

      return CreateEntityIdFromRawId(id);
    }

    private static Scene FindSceneByHandle(int handle) {
      int sceneCount = SceneManager.sceneCount;
      for (int i = 0; i < sceneCount; i++) {
        Scene scene = SceneManager.GetSceneAt(i);
        if (scene.handle == handle)
          return scene;
      }

      return default;
    }

    private static EntityId CreateEntityIdFromRawId(int id) {
      EnsureEntityIdCtorsCached();

      if (cachedEntityIdFromIntCtor != null)
        return (EntityId)cachedEntityIdFromIntCtor.Invoke(new object[] { id });

      if (cachedEntityIdFromLongCtor != null)
        return (EntityId)cachedEntityIdFromLongCtor.Invoke(new object[] { (long)id });

      if (cachedEntityIdFromULongCtor != null)
        return (EntityId)cachedEntityIdFromULongCtor.Invoke(new object[] { (ulong)(uint)id });

      return default;
    }

    private static void EnsureEntityIdCtorsCached() {
      if (cachedEntityIdCtorsSearched)
        return;

      cachedEntityIdCtorsSearched = true;
      Type entityIdType = typeof(EntityId);

      cachedEntityIdFromIntCtor = entityIdType.GetConstructor(new[] { typeof(int) });
      cachedEntityIdFromLongCtor = entityIdType.GetConstructor(new[] { typeof(long) });
      cachedEntityIdFromULongCtor = entityIdType.GetConstructor(new[] { typeof(ulong) });
    }

    private static int[] ConvertEntityIdsToInstanceIds(EntityId[] entityIds) {
      if (entityIds == null || entityIds.Length == 0)
        return Array.Empty<int>();

      List<int> instanceIds = new List<int>(entityIds.Length);
      for (int i = 0; i < entityIds.Length; i++) {
        Object obj = EditorUtility.EntityIdToObject(entityIds[i]);
        if (obj != null)
          instanceIds.Add(obj.GetInstanceID());
      }

      return instanceIds.ToArray();
    }
#endif
  }
}
