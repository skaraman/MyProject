#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static partial class HierarchyCaretMemory
{
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

  private static void ResolveInstanceIdToObjectMethod()
  {
    if (instanceIdToObjectMethod != null)
    {
      return;
    }

    const BindingFlags allFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    foreach (var method in typeof(EditorUtility).GetMethods(allFlags))
    {
      if (!string.Equals(method.Name, "InstanceIDToObject", StringComparison.Ordinal))
      {
        continue;
      }

      var parameters = method.GetParameters();
      if (parameters.Length != 1)
      {
        continue;
      }

      if (parameters[0].ParameterType != typeof(int))
      {
        continue;
      }

      instanceIdToObjectMethod = method;
      return;
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

    ResolveInstanceIdToObjectMethod();
    if (instanceIdToObjectMethod == null)
    {
      return false;
    }

    try
    {
      target = instanceIdToObjectMethod.Invoke(null, new object[] { instanceId }) as UnityEngine.Object;
    }
    catch
    {
      target = null;
    }

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
}
#endif
