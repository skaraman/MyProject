#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static partial class HierarchyCaretMemory
{
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
    const System.Reflection.BindingFlags allFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
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
}
#endif
