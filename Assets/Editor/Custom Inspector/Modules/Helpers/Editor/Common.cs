using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CustomInspector.Helpers
{
    public static class Common
    {
        /// <summary>
        /// The width of the scrollbar that appears when using GUI.ScrollViewScope
        /// </summary>
        public const float scrollbarThickness = 15;
        /// <summary>
        /// Gets the first item that matches the predicate
        /// </summary>
        /// <returns>If any item was found</returns>
        public static bool TryGetFirst<T>(this IEnumerable<T> list, Func<T, bool> predicate, out T match)
        {
            foreach (T item in list)
            {
                if (predicate(item))
                {
                    match = item;
                    return true;
                }
            }
            match = default;
            return false;
        }
        public static IEnumerable<Transform> GetDirectChildren(this Transform parent)
        {
            foreach (Transform child in parent)
            {
                yield return child;
            }
        }
        public static IEnumerable<Transform> GetAllChildren(this Transform parent)
        {
            foreach (Transform child in parent)
            {
                yield return child;
                foreach (Transform innerChild in child.GetAllChildren())
                    yield return innerChild;
            }
        }

        public static IEnumerable<Transform> GetAllParents(this Transform child)
        {
            while (child.parent != null)
            {
                yield return child.parent;
                child = child.parent;
            }
        }

        public static T GetComponentInDirectChildren<T>(this Transform parent) where T : Component
        {
            foreach (Transform child in parent)
            {
                if (child.TryGetComponent<T>(out T result))
                    return result;
            }
            return default(T);
        }

        public static string GetFullPath(GameObject gameObject) => GetFullPath(gameObject.transform);

        public static string GetFullPath(Transform child)
        {
            if (child.parent == null)
                return child.gameObject.scene.name + "." + child.name;
            else
                return GetFullPath(child.parent) + "." + child.name;
        }

        public static string GetFullPath(SerializedObject serializedObject)
        {
            if (serializedObject.targetObject is GameObject gob)
                return GetFullPath(gob);
            else if (serializedObject.targetObject is Component comp)
                return GetFullPath(comp.gameObject);
            else
                return serializedObject.targetObject.name;
        }

        public static string GetFullPath(SerializedProperty property) => GetFullPath(property.serializedObject) + "." + property.propertyPath;

        /// <summary>
        /// Preserve percentage from one range A to another range B
        /// </summary>
        public static float FromRangeToRange(float value, float minA, float maxA, float minB, float maxB)
        {
            if (minA == maxA)
            {
                if (minB == maxB)
                    return value + (minB - minA); //just shift the number
                else //just get a point in the middle
                    return (minB + maxB) / 2f;
            }
            float percentage = (value - minA) / (maxA - minA);
            return percentage * (maxB - minB) + minB;
        }

        public static bool SequenceEqual(this IList list1, IList list2)
        {
            if (list1.Count != list2.Count)
                return false;

            try
            {
                for (int i = 0; i < list1.Count; i++)
                {
                    if (list1[i] == null)
                    {
                        if (list2[i] != null)
                            return false;
                    }
                    else if (!list1[i].Equals(list2[i]))
                        return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
            return true;
        }

        public static bool IsVectorType(SerializedPropertyType propertyType)
        {
            return propertyType switch
            {
                SerializedPropertyType.Vector2 => true,
                SerializedPropertyType.Vector2Int => true,
                SerializedPropertyType.Vector3 => true,
                SerializedPropertyType.Vector3Int => true,
                SerializedPropertyType.Vector4 => true,
                _ => false
            };
        }
    }
}
