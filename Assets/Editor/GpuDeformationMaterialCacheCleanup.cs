using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class GpuDeformationMaterialCacheCleanup {
  const string ManagerTypeName =
    "UnityEngine.U2D.Animation.DeformationManager, Unity.2D.Animation.Runtime";
  const string ManagerInstanceFieldName = "s_Instance";
  const string DeformationSystemsFieldName = "m_DeformationSystems";
  const string MaterialCacheFieldName = "m_KeywordEnabledMaterials";
  const string GpuSystemTypeName = "GpuDeformationSystem";
  const string GpuSkinningKeyword = "SKINNED_SPRITE";

  static readonly BindingFlags InstanceFieldFlags =
    BindingFlags.Instance |
    BindingFlags.NonPublic;

  static GpuDeformationMaterialCacheCleanup() {
    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
  }

  static void OnPlayModeStateChanged(PlayModeStateChange state) {
    if (state != PlayModeStateChange.ExitingPlayMode) return;

    ClearGpuMaterialCache();
  }

  static void ClearGpuMaterialCache() {
    var managerType = Type.GetType(ManagerTypeName);
    if (managerType == null) return;

    var managerField = managerType.GetField(
      ManagerInstanceFieldName,
      BindingFlags.Static | BindingFlags.NonPublic
    );
    if (managerField == null) return;

    var manager = managerField.GetValue(null);
    if (manager == null) return;

    var systemsField = managerType.GetField(
      DeformationSystemsFieldName,
      InstanceFieldFlags
    );
    if (systemsField == null) return;

    var systems = systemsField.GetValue(manager) as Array;
    if (systems == null) return;

    for (var i = 0; i < systems.Length; i++) {
      ClearGpuMaterialCache(systems.GetValue(i));
    }
  }

  static void ClearGpuMaterialCache(object deformationSystem) {
    if (deformationSystem == null) return;

    var systemType = deformationSystem.GetType();
    if (systemType.Name != GpuSystemTypeName) return;

    var cacheField = systemType.GetField(
      MaterialCacheFieldName,
      InstanceFieldFlags
    );
    if (cacheField == null) return;

    var cache = cacheField.GetValue(deformationSystem) as IDictionary;
    if (cache == null) return;

    foreach (var value in cache.Values) {
      var material = value as Material;
      if (material == null) continue;

      material.DisableKeyword(GpuSkinningKeyword);
    }

    cache.Clear();
  }
}
