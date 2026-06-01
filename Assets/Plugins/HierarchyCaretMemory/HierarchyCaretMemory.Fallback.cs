#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static partial class HierarchyCaretMemory
{
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
      return obj == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
  }
}
#endif
