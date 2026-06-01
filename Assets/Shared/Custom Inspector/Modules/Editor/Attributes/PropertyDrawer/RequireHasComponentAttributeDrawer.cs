using CustomInspector.Extensions;
using CustomInspector.Helpers.Editor;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CustomInspector.Editor
{
    [CustomPropertyDrawer(typeof(RequireHasComponentAttribute))]
    public class RequireHasComponentAttributeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);
            PropInfo propInfo = cache.GetInfo(property, attribute, fieldInfo);
            if (!string.IsNullOrEmpty(propInfo.ErrorMessage))
            {
                DrawProperties.DrawPropertyWithMessage(position,
                                                        label,
                                                        property,
                                                        propInfo.ErrorMessage,
                                                        MessageType.Error);
                return;
            }

            // propInfo validated that property is Component or GameObject
            Object value = property.GetValue() as Object;
            GameObject gob = propInfo.GetReferenceGameobject(value);
            if (gob != null)
            {
                // Check missing components
                foreach (Type req in propInfo.RequiredComponents)
                {
                    if (!gob.TryGetComponent(req, out Component _))
                    {
                        DrawProperties.DrawPropertyWithMessage(position, label, property,
                            errorMessage: $"[RequireHasComponent] '{gob.name}' has no '{req}' attached",
                            type: MessageType.Error);
                        return;
                    }
                }
            }

            // Everything is valid
            DrawProperties.PropertyField(position, label, property);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            PropInfo propInfo = cache.GetInfo(property, attribute, fieldInfo);
            if (!string.IsNullOrEmpty(propInfo.ErrorMessage))
            {
                return DrawProperties.GetPropertyWithMessageHeight(label, property);
            }

            // propInfo validated that property is Component or GameObject
            Object value = property.GetValue() as Object;
            GameObject gob = propInfo.GetReferenceGameobject(value);
            if (gob != null)
            {
                // Check missing components
                foreach (Type req in propInfo.RequiredComponents)
                {
                    if (!gob.TryGetComponent(req, out Component _))
                    {
                        return DrawProperties.GetPropertyWithMessageHeight(label, property);
                    }
                }
            }

            // Everything is valid
            return DrawProperties.GetPropertyHeight(label, property);
        }

        static readonly PropInfoCache<PropInfo> cache = new();

        class PropInfo : ICachedPropInfo
        {
            public string ErrorMessage { get; private set; }
            public IReadOnlyList<Type> RequiredComponents { get; private set; }


            public void Initialize(SerializedProperty property, PropertyAttribute attribute, FieldInfo fieldInfo)
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                {
                    ErrorMessage = "[RequireHasComponent] is only valid for references";
                    return;
                }
                Type propType = DirtyValue.GetType(property);
                if (propType != typeof(GameObject)
                    && !typeof(Component).IsAssignableFrom(propType))
                {
                    ErrorMessage = "[RequireHasComponent] is only valid on Components or GameObjects";
                    return;
                }
                RequiredComponents = ((RequireHasComponentAttribute)attribute).RequiredComponents;
                foreach (var comp in RequiredComponents)
                {
                    if (!typeof(Component).IsAssignableFrom(comp))
                    {
                        ErrorMessage = $"[RequireHasComponent]: {comp.Name} is not of type Component";
                        return;
                    }
                }
            }

            public GameObject GetReferenceGameobject(Object value)
            {
                if (value == null)
                    return null;

                GameObject gameObject = null;
                if (value is GameObject gob)
                    gameObject = gob;
                else
                {
                    if (value is Component comp)
                        gameObject = comp.gameObject;
                    else
                        Debug.LogError("Expected value to be of type 'Component'");
                }

                if (gameObject == null)
                {
                    ErrorMessage = "GameObject of value could not be found. Contact support for further help."; // This error should occur never, because every gameobject or component have a gameobject
                    return null;
                }

                return gameObject;
            }
        }
    }
}
