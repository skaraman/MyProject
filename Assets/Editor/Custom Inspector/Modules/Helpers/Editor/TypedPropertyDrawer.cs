using CustomInspector.Extensions;
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CustomInspector.Helpers.Editor
{
    public class TypedPropertyDrawer : PropertyDrawer
    {
        readonly string errorMessage;
        readonly Type[] allowedGenericTypes;
        protected TypedPropertyDrawer(string errorMessage, params Type[] allowedTypes)
        {
            if (allowedTypes == null
                || !allowedTypes.Any())
                throw new ArgumentException(nameof(allowedTypes) + " is null or emtpy");

            this.errorMessage = errorMessage;
            this.allowedGenericTypes = allowedTypes;
        }
        protected bool TryOnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property == null)
                return false;

            //Check type
            var info = cache.GetInfo(property, attribute, fieldInfo);
            if (!allowedGenericTypes.Contains(info.GenericType))
            {
                DrawProperties.DrawPropertyWithMessage(position, label, property, errorMessage, MessageType.Error);
                return false;
            }

            // Valid type
            return true;
        }
        protected bool TryGetPropertyHeight(SerializedProperty property, GUIContent label, out float fallbackHeight)
        {
            fallbackHeight = -1;
            if (property == null)
                return false;

            //Check type
            var info = cache.GetInfo(property, attribute, fieldInfo);
            if (!allowedGenericTypes.Contains(info.GenericType))
            {
                fallbackHeight = DrawProperties.GetPropertyWithMessageHeight(label, property);
                return false;
            }

            // Valid type
            return true;
        }

        private readonly static PropInfoCache<PropInfo> cache = new();
        private class PropInfo : ICachedPropInfo
        {
            public Type GenericType { get; private set; }
            public void Initialize(SerializedProperty property, PropertyAttribute attribute, System.Reflection.FieldInfo fieldInfo)
            {
                Type fieldType = DirtyValue.GetType(property);
                if (fieldType.IsGenericType)
                    GenericType = fieldType.GetGenericTypeDefinition();
                else
                    GenericType = fieldType;
            }
        }
    }
}
