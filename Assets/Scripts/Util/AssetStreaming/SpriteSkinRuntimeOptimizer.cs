using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

static class SpriteSkinRuntimeOptimizer {
  const string SpriteSkinTypeFullName = "UnityEngine.U2D.Animation.SpriteSkin";
  static readonly string[] CandidateAssemblyQualifiedNames = {
    "UnityEngine.U2D.Animation.SpriteSkin, Unity.2D.Animation.Runtime",
    "UnityEngine.U2D.Animation.SpriteSkin, Unity.2D.Animation"
  };

  static Type spriteSkinType;
  static PropertyInfo alwaysUpdateProperty;
  static bool initialized;
  static bool sceneHookRegistered;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetState() {
    if (sceneHookRegistered) {
      SceneManager.sceneLoaded -= OnSceneLoaded;
      sceneHookRegistered = false;
    }
    initialized = false;
    spriteSkinType = null;
    alwaysUpdateProperty = null;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void ApplyAfterSceneLoad() {
    EnsureInitialized();
    ApplyAlwaysUpdateOverride();
  }

  static void EnsureInitialized() {
    if (initialized) return;
    initialized = true;
    spriteSkinType = ResolveSpriteSkinType();
    if (spriteSkinType != null) {
      alwaysUpdateProperty = spriteSkinType.GetProperty(
        "alwaysUpdate",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
      );
    }
    if (!sceneHookRegistered) {
      SceneManager.sceneLoaded += OnSceneLoaded;
      sceneHookRegistered = true;
    }
  }

  static void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
    ApplyAlwaysUpdateOverride();
  }

  static void ApplyAlwaysUpdateOverride() {
    if (!Application.isPlaying) return;
    if (spriteSkinType == null || alwaysUpdateProperty == null || !alwaysUpdateProperty.CanRead || !alwaysUpdateProperty.CanWrite) return;

    var objects = Resources.FindObjectsOfTypeAll(spriteSkinType);
    for (var i = 0; i < objects.Length; i++) {
      var component = objects[i] as Component;
      if (component == null || !component.gameObject.scene.IsValid()) continue;

      if (alwaysUpdateProperty.GetValue(component, null) is bool alwaysUpdate && alwaysUpdate) {
        alwaysUpdateProperty.SetValue(component, false, null);
      }
    }
  }

  static Type ResolveSpriteSkinType() {
    for (var i = 0; i < CandidateAssemblyQualifiedNames.Length; i++) {
      var resolved = Type.GetType(CandidateAssemblyQualifiedNames[i]);
      if (resolved != null) return resolved;
    }

    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
    for (var i = 0; i < assemblies.Length; i++) {
      var resolved = assemblies[i].GetType(SpriteSkinTypeFullName);
      if (resolved != null) return resolved;
    }

    return null;
  }
}
