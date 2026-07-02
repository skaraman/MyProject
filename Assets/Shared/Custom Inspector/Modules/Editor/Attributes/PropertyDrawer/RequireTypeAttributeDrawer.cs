using CustomInspector.Extensions;
using CustomInspector.Helpers.Editor;
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CustomInspector.Editor
{
    /// <summary>
    /// Draws an ObjectField constrained to given type like some interface
    /// </summary>
    [CustomPropertyDrawer(typeof(RequireTypeAttribute))]
    public class RequireTypeAttributeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            PropInfo info = cache.GetInfo(property, attribute, fieldInfo);
            if (info.ValidateLabel)
                label = PropertyValues.ValidateLabel(label, property);

            if (info.ErrorMessage != null)
            {
                DrawProperties.DrawPropertyWithMessage(position, label, property, info.ErrorMessage, MessageType.Error);
                return;
            }

            if (info.RequiredType.IsInterface)
            {
                HandleDragAndDrop(position, property, info.RequiredType);

                EditorGUI.BeginChangeCheck();

                UnityEngine.Object droppedObject = EditorGUI.ObjectField(
                    position,
                    label,
                    property.objectReferenceValue,
                    typeof(UnityEngine.Object),
                    true
                );

                if (EditorGUI.EndChangeCheck())
                    AssignIfValid(property, droppedObject, info.RequiredType);

                /*
                 * Draw an unused object field on top to make it look like the object field knows the interface as target type.
                 * It has the correct label but is broken
                 */
                _ = EditorGUI.ObjectField(position, label, property.objectReferenceValue,
                    info.RequiredType, true);
            }
            else
            {
                EditorGUI.BeginChangeCheck();

                UnityEngine.Object res = EditorGUI.ObjectField(position, label, property.objectReferenceValue,
                    info.RequiredType, true);

                if (EditorGUI.EndChangeCheck())
                {
                    property.objectReferenceValue = res;
                    _ = property.serializedObject.ApplyModifiedProperties();
                }
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            PropInfo info = cache.GetInfo(property, attribute, fieldInfo);
            if (info.ErrorMessage != null)
                return DrawProperties.GetPropertyWithMessageHeight(label, property);

            return DrawProperties.GetPropertyHeight(label, property);
        }

        private static readonly PropInfoCache<PropInfo> cache = new();

        private class PropInfo : ICachedPropInfo
        {
            public string ErrorMessage { get; private set; }
            public Type RequiredType { get; private set; }
            public bool ValidateLabel { get; private set; }
            public void Initialize(SerializedProperty property, PropertyAttribute attribute, FieldInfo fieldInfo)
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                {
                    ErrorMessage = "RequireTypeAttribute is only valid for references";
                    return;
                }
                RequireTypeAttribute requiredAttribute = (RequireTypeAttribute)attribute;
                ValidateLabel = !requiredAttribute.UseBaseGenericArg; // Do not override label, if its intentially coming from up
                if (requiredAttribute.UseBaseGenericArg)
                {
                    Type[] genericArgs = property.GetOwnerAsFinder().GetPropertyType().GetGenericArguments();
                    if (genericArgs.Length <= 0)
                    {
                        ErrorMessage = "Required type is not found";
                        return;
                    }
                    RequiredType = genericArgs[0];
                }
                else
                {
                    if (requiredAttribute.requiredType == null)
                        ErrorMessage = "Required type cannot be null";
                    RequiredType = requiredAttribute.requiredType;
                }
            }
        }

        private static void HandleDragAndDrop(Rect position, SerializedProperty property, Type requiredType)
        {
            Event currentEvent = Event.current;

            if (!position.Contains(currentEvent.mousePosition))
                return;

            if (currentEvent.type is not EventType.DragUpdated and not EventType.DragPerform)
                return;

            UnityEngine.Object draggedObject = DragAndDrop.objectReferences.Length > 0
                ? DragAndDrop.objectReferences[0]
                : null;

            bool isValid = TryGetRequiredObject(draggedObject, requiredType, out UnityEngine.Object validObject);

            DragAndDrop.visualMode = isValid
                ? DragAndDropVisualMode.Copy
                : DragAndDropVisualMode.Rejected;

            if (currentEvent.type == EventType.DragPerform && isValid)
            {
                DragAndDrop.AcceptDrag();

                property.objectReferenceValue = validObject;
                _ = property.serializedObject.ApplyModifiedProperties();
            }

            currentEvent.Use();
        }

        private static void AssignIfValid(SerializedProperty property, UnityEngine.Object droppedObject, Type requiredType)
        {
            if (droppedObject == null)
            {
                property.objectReferenceValue = null;
                _ = property.serializedObject.ApplyModifiedProperties();
                return;
            }

            if (!TryGetRequiredObject(droppedObject, requiredType, out UnityEngine.Object validObject))
                return;

            property.objectReferenceValue = validObject;
            _ = property.serializedObject.ApplyModifiedProperties();
        }

        private static bool TryGetRequiredObject(
            UnityEngine.Object droppedObject,
            Type requiredType,
            out UnityEngine.Object validObject)
        {
            validObject = null;

            if (droppedObject == null)
                return false;

            // Direct component/scriptable/object match
            if (requiredType.IsInstanceOfType(droppedObject))
            {
                validObject = droppedObject;
                return true;
            }

            // GameObject dragged from hierarchy/project
            if (droppedObject is GameObject gameObject &&
                gameObject.TryGetComponent(requiredType, out Component component))
            {
                validObject = component;
                return true;
            }

            return false;
        }
    }
}