using CustomInspector.Extensions;
using CustomInspector.Helpers.Editor;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CustomInspector.Editor
{

    [CustomPropertyDrawer(typeof(ShowPropertyAttribute))]
    public class ShowPropertyAttributeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);

            ShowPropertyAttribute sm = (ShowPropertyAttribute)attribute;

            var owner = property.GetOwnerAsFinder();
            SerializedProperty prop = owner.FindPropertyRelative(sm.getPropertyPath);

            if (prop == null)
            {
                DrawProperties.DrawPropertyWithMessage(position, label, property, $"Property {sm.getPropertyPath} on {owner.Name} not found", MessageType.Error);
                return;
            }

            Rect propRect;
            using (new HideFieldAttributeDrawer.GlobalDisable())
            {
                //Draw
                GUIContent propLabel = PropertyValues.ValidateLabel(null, prop);
                if (sm.label != null)
                    propLabel.text = sm.label;
                if (sm.tooltip != null)
                    propLabel.text = sm.tooltip + propLabel.text;

                propRect = new(position)
                {
                    height = GetPropertyRawHeight(propLabel, prop, attribute, fieldInfo)
                };
                using (new EditorGUI.DisabledScope(sm.isReadonly))
                {
                    EditorGUI.BeginChangeCheck();
                    if (sm.removePreviousAttributes)
                        PropertyFieldRaw(propRect, propLabel, prop, attribute, fieldInfo);
                    else
                        DrawProperties.PropertyField(propRect, propLabel, prop);
                    if (EditorGUI.EndChangeCheck())
                        prop.serializedObject.ApplyModifiedProperties();
                }
            }

            //other
            propRect.y += propRect.height + EditorGUIUtility.standardVerticalSpacing;
            propRect.height = position.height - propRect.height - EditorGUIUtility.standardVerticalSpacing;

            EditorGUI.BeginChangeCheck();
            DrawProperties.PropertyField(propRect, label, property);
            if (EditorGUI.EndChangeCheck())
                property.serializedObject.ApplyModifiedProperties();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            ShowPropertyAttribute sm = (ShowPropertyAttribute)attribute;

            SerializedProperty prop = property.GetOwnerAsFinder().FindPropertyRelative(sm.getPropertyPath);
            if (prop == null)
                return DrawProperties.GetPropertyWithMessageHeight(label, property);

            float baseHeight = DrawProperties.GetPropertyHeight(label, property);
            float showedPropHeight;
            using (new HideFieldAttributeDrawer.GlobalDisable())
            {
                if (sm.removePreviousAttributes)
                    showedPropHeight = GetPropertyRawHeight(label, prop, attribute, fieldInfo);
                else
                    showedPropHeight = DrawProperties.GetPropertyHeight(label, prop);
            }

            return baseHeight
                + EditorGUIUtility.standardVerticalSpacing
                + showedPropHeight;
        }

        static readonly PropInfoCache<PropInfo> genericsCache = new();
        class PropInfo : ICachedPropInfo
        {
            public string ErrorMessage { get; private set; }
            public string[] ChildrenPaths { get; private set; }

            public PropInfo() { }
            public void Initialize(SerializedProperty property, PropertyAttribute attribute, FieldInfo fieldInfo)
            {
                Debug.Assert(property.propertyType == SerializedPropertyType.Generic, "This cache is meant for generics");
                ChildrenPaths = property.GetAllVisibleProperties(true).Select(_ => _.propertyPath).ToArray();
            }
        }

        /// <summary>
        /// Draw property without any other attributes evaluated
        /// </summary>
        public static void PropertyFieldRaw(Rect position, GUIContent label, SerializedProperty property,
            PropertyAttribute attribute, FieldInfo fieldInfo)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    property.intValue = EditorGUI.IntField(position, label, property.intValue);
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = EditorGUI.Toggle(position, label, property.boolValue);
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = EditorGUI.FloatField(position, label, property.floatValue);
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = EditorGUI.TextField(position, label, property.stringValue);
                    break;
                case SerializedPropertyType.Color:
                    property.colorValue = EditorGUI.ColorField(position, label, property.colorValue);
                    break;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = EditorGUI.ObjectField(position, label, property.objectReferenceValue, typeof(UnityEngine.Object), true);
                    break;
                case SerializedPropertyType.LayerMask:
                    property.intValue = EditorGUI.LayerField(position, label, property.intValue);
                    break;
                case SerializedPropertyType.Enum:
                    property.enumValueIndex = EditorGUI.Popup(position, label.text, property.enumValueIndex, property.enumDisplayNames);
                    break;
                case SerializedPropertyType.Vector2:
                    property.vector2Value = EditorGUI.Vector2Field(position, label.text, property.vector2Value);
                    break;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = EditorGUI.Vector3Field(position, label.text, property.vector3Value);
                    break;
                case SerializedPropertyType.Vector4:
                    property.vector4Value = EditorGUI.Vector4Field(position, label.text, property.vector4Value);
                    break;
                case SerializedPropertyType.Rect:
                    property.rectValue = EditorGUI.RectField(position, label, property.rectValue);
                    break;
                case SerializedPropertyType.ArraySize:
                    property.intValue = EditorGUI.IntField(position, label, property.intValue);
                    break;
                case SerializedPropertyType.Character:
                    property.stringValue = EditorGUI.TextField(position, label, property.stringValue);
                    break;
                case SerializedPropertyType.AnimationCurve:
                    property.animationCurveValue = EditorGUI.CurveField(position, label, property.animationCurveValue);
                    break;
                case SerializedPropertyType.Bounds:
                    property.boundsValue = EditorGUI.BoundsField(position, label, property.boundsValue);
                    break;
                // case SerializedPropertyType.Gradient:
                //     Missing
                //     break;
                case SerializedPropertyType.Quaternion:
                    property.quaternionValue = Quaternion.Euler(EditorGUI.Vector3Field(position, label.text, property.quaternionValue.eulerAngles));
                    break;
                case SerializedPropertyType.ExposedReference:
                    property.exposedReferenceValue = EditorGUI.ObjectField(position, label, property.exposedReferenceValue, typeof(UnityEngine.Object), true);
                    break;
                case SerializedPropertyType.FixedBufferSize:
                    property.intValue = EditorGUI.IntField(position, label, property.intValue);
                    break;
                case SerializedPropertyType.Vector2Int:
                    property.vector2IntValue = EditorGUI.Vector2IntField(position, label.text, property.vector2IntValue);
                    break;
                case SerializedPropertyType.Vector3Int:
                    property.vector3IntValue = EditorGUI.Vector3IntField(position, label.text, property.vector3IntValue);
                    break;
                case SerializedPropertyType.RectInt:
                    property.rectIntValue = EditorGUI.RectIntField(position, label, property.rectIntValue);
                    break;
                case SerializedPropertyType.BoundsInt:
                    property.boundsIntValue = EditorGUI.BoundsIntField(position, label, property.boundsIntValue);
                    break;
                case SerializedPropertyType.ManagedReference:
                    EditorGUI.PropertyField(position, property, label, true);
                    break;
                // case SerializedPropertyType.Hash128:
                //     // Missing
                //     break;
#if UNITY_6000_0_OR_NEWER
                case SerializedPropertyType.RenderingLayerMask:
                    property.intValue = EditorGUI.LayerField(position, label, property.intValue);
                    break;
#endif
                default:
                    DrawProperties.DrawPropertyWithMessage(position, label, property,
                        $"PropertyFieldRaw does not support propertyType '{property.propertyType}'", MessageType.Error);
                    break;
                case SerializedPropertyType.Generic:
                    {
                        // Draw label
                        PropInfo info = genericsCache.GetInfo(property, attribute, fieldInfo);
                        Rect rect = new(position);
                        rect.height = EditorGUIUtility.singleLineHeight;
                        property.isExpanded = EditorGUI.Foldout(rect, property.isExpanded, label);
                        rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
                        if (property.isExpanded)
                        {
                            // Draw children
                            using (new EditorGUI.IndentLevelScope(1))
                            {
                                using (new HideFieldAttributeDrawer.GlobalDisable(false))
                                {
                                    foreach (var childPath in info.ChildrenPaths)
                                    {
                                        SerializedProperty childProp = property.serializedObject.FindProperty(childPath);
                                        GUIContent childLabel = PropertyValues.ValidateLabel(null, childProp);
                                        rect.height = DrawProperties.GetPropertyHeight(childLabel, childProp);
                                        DrawProperties.PropertyField(rect, childLabel, childProp);
                                        rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
                                    }
                                }
                            }
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Height of property if it would have no other attributes
        /// </summary>
        public static float GetPropertyRawHeight(GUIContent label, SerializedProperty property,
                PropertyAttribute attribute, FieldInfo fieldInfo)
        {
            if (property.propertyType != SerializedPropertyType.Generic || !property.isExpanded)
            {
                return DrawProperties.GetPropertyHeight(property.propertyType, label);
            }
            else
            {
                PropInfo info = genericsCache.GetInfo(property, attribute, fieldInfo);
                IEnumerable<SerializedProperty> props = info.ChildrenPaths.Select(x => property.serializedObject.FindProperty(x));
                return EditorGUIUtility.singleLineHeight
                    + props.Select(x => DrawProperties.GetPropertyHeight(x) + EditorGUIUtility.standardVerticalSpacing).Sum();
            }
        }
    }
}