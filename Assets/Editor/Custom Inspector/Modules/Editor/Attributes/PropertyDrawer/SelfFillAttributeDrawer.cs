using CustomInspector.Extensions;
using CustomInspector.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CustomInspector.Editor
{
    [CustomPropertyDrawer(typeof(SelfFillAttribute))]
    public class SelfFillAttributeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);

            //start

            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                DrawProperties.DrawPropertyWithMessage(position, label, property, $"SelfFill: Type '{property.propertyType}' not supported. Use this attribute on objects (interfaces with RequireType or components)", MessageType.Error, disabled: true);
            }
            else
            {
                label.text += " (auto-filled)";
                string tooltipMessage = "SelfFill: This field will be automatically filled with the first matching component on this gameObject";
                label.tooltip = (string.IsNullOrEmpty(label.tooltip)) ? tooltipMessage : $"{label.tooltip}\n{tooltipMessage}";

                void SetNull()
                {
                    if (property.objectReferenceValue != null)
                    {
                        property.objectReferenceValue = null;
                        property.serializedObject.ApplyModifiedProperties();
                    }
                }

                Component component = property.serializedObject.targetObject as Component;
                if (component == null)
                {
                    if (property.serializedObject.targetObject is ScriptableObject)
                        DrawProperties.DrawPropertyWithMessage(position, label, property, $"SelfFillAttribute for ScriptableObjects not supported", MessageType.Error, disabled: true);
                    else
                        DrawProperties.DrawPropertyWithMessage(position, label, property, $"SelfFillAttribute for '{property.serializedObject.targetObject.GetType()}' not supported", MessageType.Error, disabled: true);
                    return;
                }
                SelfFillAttribute sa = (SelfFillAttribute)attribute;

                IEnumerable<Transform> targets;
                switch (sa.mode)
                {
                    case OwnerMode.Self:
                        targets = new Transform[] { component.transform };
                        break;
                    case OwnerMode.Root:
                        targets = new Transform[] { component.transform.root };
                        break;
                    case OwnerMode.Parent:
                        if (component.transform.parent == null)
                        {
                            SetNull();
                            DrawProperties.DrawPropertyWithMessage(position, label, property, $"SelfFill: Mode '{nameof(OwnerMode.Parent)}' invalid for objects without parent", MessageType.Error, disabled: true);
                            return;
                        }
                        targets = new Transform[] { component.transform.parent };
                        break;
                    case OwnerMode.Parents:
                        if (component.transform.parent == null)
                        {
                            property.objectReferenceValue = null;
                            property.serializedObject.ApplyModifiedProperties();
                            DrawProperties.DrawPropertyWithMessage(position, label, property, $"SelfFill: Mode '{nameof(OwnerMode.Parents)}' invalid for objects without parent", MessageType.Error, disabled: true);
                            return;
                        }
                        targets = component.transform.GetAllParents();
                        break;
                    case OwnerMode.Children:
                        if (component.transform.childCount <= 0)
                        {
                            SetNull();
                            DrawProperties.DrawPropertyWithMessage(position, label, property, $"SelfFill: Mode '{nameof(OwnerMode.Children)}' invalid for objects without children", MessageType.Error, disabled: true);
                            return;
                        }
                        targets = component.transform.GetAllChildren();
                        break;
                    case OwnerMode.DirectChildren:
                        if (component.transform.childCount <= 0)
                        {
                            SetNull();
                            DrawProperties.DrawPropertyWithMessage(position, label, property, $"SelfFill: Mode '{nameof(OwnerMode.DirectChildren)}' invalid for objects without children", MessageType.Error, disabled: true);
                            return;
                        }
                        targets = component.transform.GetDirectChildren();
                        break;
                    default:
                        throw new NotImplementedException(sa.mode.ToString());
                };



                //Check if empty
                if (property.objectReferenceValue == null)
                {
                    if (Application.isPlaying)
                        Debug.LogError("SelfFill: Value was not set outside of play-mode hence it won't be set consistently.\nPlease open the inspector outside of play-mode at least once to fill the null-value.");

                    RequireTypeAttribute requiredInterface = fieldInfo.GetCustomAttribute<RequireTypeAttribute>(); // support for 'require custom type' - like interface

                    if (fieldInfo.FieldType == typeof(GameObject))
                    {
                        property.objectReferenceValue = targets.First().gameObject;
                        property.serializedObject.ApplyModifiedProperties();
                    }
                    else if (requiredInterface != null)
                    {
                        property.objectReferenceValue = targets.Select(t => t.GetComponent(requiredInterface.requiredType)).FirstOrDefault(c => c != null);

                        //Check if not found
                        if (property.objectReferenceValue != null)
                            property.serializedObject.ApplyModifiedProperties();
                        else
                        {
                            DrawProperties.DrawPropertyWithMessage(position, label, property, $"SelfFill: No component with interface '{requiredInterface.requiredType}' found on '{sa.mode}'", MessageType.Error, disabled: true);
                            return;
                        }
                    }
                    else if (fieldInfo.FieldType == typeof(Component) || fieldInfo.FieldType.IsSubclassOf(typeof(Component)))
                    {
                        property.objectReferenceValue = targets.Select(t => t.GetComponent(fieldInfo.FieldType)).FirstOrDefault(c => c != null);

                        //Check if not found
                        if (property.objectReferenceValue != null)
                            property.serializedObject.ApplyModifiedProperties();
                        else
                        {
                            DrawProperties.DrawPropertyWithMessage(position, label, property, $"SelfFill: No '{fieldInfo.FieldType}' component found on '{sa.mode}'", MessageType.Error, disabled: true);
                            return;
                        }
                    }
                    else
                    {
                        DrawProperties.DrawPropertyWithMessage(position, label, property, $"SelfFill: Type '{fieldInfo.FieldType}' not supported. Use component-types (no Assets) or interfaces (with the [RequireType]-attribute)", MessageType.Error, disabled: true);
                        return;
                    }
                }
                else //property.objectReferenceValue != null
                {
                    //Check if valid (invalid fills when for example you copy the script to other objects)
                    if (property.objectReferenceValue is GameObject g)
                    {
                        if (!targets.Contains(g.transform)) //c.gameObject != gob
                        {
                            if (Application.isPlaying)
                                Debug.LogError($"SelfFill: GameObject reference value on '{PropertyConversions.NameFormat(property.name)}' deleted. SelfFillAttribute only valid for gameObject on '{sa.mode}'. Location: '{property.serializedObject.targetObject}'");
                            property.objectReferenceValue = null;
                            property.serializedObject.ApplyModifiedProperties();
                        }
                    }
                    else if (property.objectReferenceValue is Component c)
                    {
                        RequireTypeAttribute requiredInterface = fieldInfo.GetCustomAttribute<RequireTypeAttribute>();

                        //should have right type
                        if (requiredInterface != null && !requiredInterface.requiredType.IsAssignableFrom(c.GetType()))
                        {
                            Debug.LogWarning($"SelfFill: Value on '{PropertyConversions.NameFormat(property.name)}' had wrong type. '{requiredInterface.requiredType}' is not assignable from {c.GetType()}.\nValue set to null");
                            property.objectReferenceValue = null;
                            property.serializedObject.ApplyModifiedProperties();
                        }
                        //should be a component on allowed
                        else if (!targets.Contains(c.transform)) //c.gameObject != gob
                        {
                            if (Application.isPlaying)
                                Debug.LogError($"SelfFill: ObjectReferenceValue on '{PropertyConversions.NameFormat(property.name)}' deleted. SelfFillAttribute only valid for components on '{sa.mode}'. Location: '{property.serializedObject.targetObject}'");
                            property.objectReferenceValue = null;
                            property.serializedObject.ApplyModifiedProperties();
                        }
                    }
                    else //like an asset
                    {
                        if (Application.isPlaying)
                            Debug.LogError($"SelfFill: Reference on '{PropertyConversions.NameFormat(property.name)}' deleted, because selffillattribute only supports components. Location: '{property.serializedObject.targetObject}'");
                        else
                            Debug.LogWarning($"SelfFill: Value on '{PropertyConversions.NameFormat(property.name)}' discarded, because selffillattribute only supports components");
                        property.objectReferenceValue = null;
                        property.serializedObject.ApplyModifiedProperties();
                    }
                }
                //Display
                if (!sa.hideIfFilled)
                {
                    DrawProperties.DisabledPropertyField(position, label, property);
                }
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference
                || property.serializedObject.targetObject is not Component component)
            {
                return DrawProperties.GetPropertyWithMessageHeight(label, property);
            }
            SelfFillAttribute sa = (SelfFillAttribute)attribute;
            if (sa.mode == OwnerMode.Parent && component.transform.parent == null
                || sa.mode == OwnerMode.Parents && component.transform.parent == null
                || sa.mode == OwnerMode.Children && component.transform.childCount <= 0
                || sa.mode == OwnerMode.DirectChildren && component.transform.childCount <= 0)
            {
                return DrawProperties.GetPropertyWithMessageHeight(label, property);
            }
            if (property.objectReferenceValue == null)
            {
                return DrawProperties.GetPropertyWithMessageHeight(label, property);
            }
            else
            {
                if (!sa.hideIfFilled)
                    return DrawProperties.GetPropertyHeight(label, property);
                else
                    return -EditorGUIUtility.standardVerticalSpacing;
            }
        }
    }
}