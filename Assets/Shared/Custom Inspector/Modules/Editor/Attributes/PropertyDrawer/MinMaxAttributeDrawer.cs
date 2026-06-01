using CustomInspector.Extensions;
using CustomInspector.Helpers.Editor;
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CustomInspector.Editor
{
    public abstract class MinMaxAttributeDrawer : PropertyDrawer
    {
        public abstract long CapLong(long value, double cap);
        public abstract ulong CapULong(ulong value, double cap);
        public abstract double CapDecimal(double value, double cap);


        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);

            var info = PropInfo.GetInfo(property, attribute, fieldInfo);
            IMinMaxAttribute mm = (IMinMaxAttribute)attribute;

            if (!string.IsNullOrEmpty(info.ErrorMessage))
            {
                DrawProperties.DrawPropertyWithMessage(position, label, property, info.ErrorMessage, MessageType.Error);
                return;
            }

            EditorGUI.BeginChangeCheck();
            DrawProperties.PropertyField(position, label, property);

            // do the capping
            if (mm.DependsOnOtherProperty() || EditorGUI.EndChangeCheck())
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        if (fieldInfo.FieldType == typeof(long))
                            property.longValue = CapLong(property.longValue, info.Cap[0]);
                        else if (fieldInfo.FieldType == typeof(ulong))
#if UNITY_2022_1_OR_NEWER
                            property.ulongValue = CapULong(property.ulongValue, info.Cap[0]);
                        else if (fieldInfo.FieldType == typeof(uint))
                            property.uintValue = (uint)CapULong(property.uintValue, info.Cap[0]);
#else
                            property.longValue = (long)CapULong((ulong)property.longValue, info.Cap[0]);
                        else if (fieldInfo.FieldType == typeof(uint))
                            property.intValue = (int)CapULong((ulong)property.intValue, info.Cap[0]);
#endif
                        else
                            property.intValue = (int)CapLong(property.intValue, info.Cap[0]);
                        break;
                    case SerializedPropertyType.Float:
                        if (fieldInfo.FieldType == typeof(double))
                            property.doubleValue = CapDecimal(property.doubleValue, info.Cap[0]);
                        else
                            property.floatValue = (float)CapDecimal(property.floatValue, info.Cap[0]);
                        break;

                    case SerializedPropertyType.Character:
                        property.intValue = (int)CapLong(property.intValue, info.Cap[0]);
                        break;

                    case SerializedPropertyType.Vector2Int:
                        Vector2Int v2i = property.vector2IntValue;
                        property.vector2IntValue = new Vector2Int((int)CapLong(v2i.x, info.Cap[0]), (int)CapLong(v2i.y, info.Cap[1]));
                        break;
                    case SerializedPropertyType.Vector2:
                        Vector2 v2 = property.vector2Value;
                        property.vector2Value = new Vector2((float)CapDecimal(v2.x, info.Cap[0]), (float)CapDecimal(v2.y, info.Cap[1]));
                        break;

                    case SerializedPropertyType.Vector3Int:
                        Vector3Int v3i = property.vector3IntValue;
                        property.vector3IntValue = new Vector3Int((int)CapLong(v3i.x, info.Cap[0]), (int)CapLong(v3i.y, info.Cap[1]), (int)CapLong(v3i.z, info.Cap[2]));
                        break;
                    case SerializedPropertyType.Vector3:
                        Vector3 v3 = property.vector3Value;
                        property.vector3Value = new Vector3((float)CapDecimal(v3.x, info.Cap[0]), (float)CapDecimal(v3.y, info.Cap[1]), (float)CapDecimal(v3.z, info.Cap[2]));
                        break;

                    case SerializedPropertyType.Vector4:
                        Vector4 v4 = property.vector4Value;
                        property.vector4Value = new Vector4((float)CapDecimal(v4.x, info.Cap[0]), (float)CapDecimal(v4.y, info.Cap[1]), (float)CapDecimal(v4.z, info.Cap[2]), (float)CapDecimal(v4.w, info.Cap[3]));
                        break;

                    default:
                        Debug.LogError($"Min/Max attribute is not valid for {property.serializedObject.targetObject.name}.{property.propertyPath}.\nIt seems to not be a number (or vector)");
                        break;
                }
                property.serializedObject.ApplyModifiedProperties();
            }
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {

            var info = PropInfo.GetInfo(property, attribute, fieldInfo);

            if (string.IsNullOrEmpty(info.ErrorMessage))
                return DrawProperties.GetPropertyHeight(label, property);
            else
                return DrawProperties.GetPropertyWithMessageHeight(label, property);
        }
        // not saved as cache because values always change, so this is actually no cache :)
        //readonly ExactPropInfoCache<PropInfo> cache = new();

        class PropInfo : ICachedPropInfo
        {
            /// <summary>
            /// The min or max values (component-wise)
            /// </summary>
            public double[] Cap { get; private set; }
            public string ErrorMessage { get; private set; }
            public void Initialize(SerializedProperty property, PropertyAttribute attribute, FieldInfo fieldInfo)
            {
                var mm = (IMinMaxAttribute)attribute;
                if (string.IsNullOrEmpty(mm.CapPath))
                {
                    SetFloatingCap(mm.CapValue);
                }
                else
                {
                    object max_Value = DirtyValue.GetOwner(property).FindRelative(mm.CapPath).GetValue();
                    if (max_Value is System.Single s)
                    {
                        SetFloatingCap(s);
                    }
                    else if (max_Value is System.Double d)
                    {
                        SetFloatingCap(d);
                    }
                    else if (max_Value is Vector2 v2)
                    {
                        Cap = new double[] { v2.x, v2.y };
                    }
                    else if (max_Value is Vector2Int v2i)
                    {
                        Cap = new double[] { v2i.x, v2i.y };
                    }
                    else if (max_Value is Vector3 v3)
                    {
                        Cap = new double[] { v3.x, v3.y, v3.z };
                    }
                    else if (max_Value is Vector3Int v3i)
                    {
                        Cap = new double[] { v3i.x, v3i.y, v3i.z };
                    }
                    else if (max_Value is Vector4 v4)
                    {
                        Cap = new double[] { v4.x, v4.y, v4.z, v4.w };
                    }
                    else
                    {

                        try
                        {
                            float casted = System.Convert.ToSingle(max_Value); //maybe it is provided as string like "7"
                            SetFloatingCap(casted);
                        }
                        catch (Exception e)
                        {
                            ErrorMessage = $"{max_Value.GetType()} could not be read as a number (or vector).\n" + e.Message;
                        }
                    }
                }

                void SetFloatingCap(double cap)
                {
                    if (property.propertyType == SerializedPropertyType.Integer
                      || property.propertyType == SerializedPropertyType.Float
                      || property.propertyType == SerializedPropertyType.Character)
                    {
                        Cap = new[] { cap };
                    }
                    else if (property.propertyType == SerializedPropertyType.Vector2
                      || property.propertyType == SerializedPropertyType.Vector2Int)
                    {
                        Cap = new[] { cap, cap };
                    }
                    else if (property.propertyType == SerializedPropertyType.Vector3
                      || property.propertyType == SerializedPropertyType.Vector3Int)
                    {
                        Cap = new[] { cap, cap, cap };
                    }
                    else if (property.propertyType == SerializedPropertyType.Vector4)
                    {
                        Cap = new[] { cap, cap, cap, cap };
                    }
                    else
                    {
                        ErrorMessage = $"Property '{PropertyConversions.NameFormat(property.name)}' cannot be capped, because it could not be read as a number (or vector)";
                    }
                }
            }
            public static PropInfo GetInfo(SerializedProperty property, PropertyAttribute attribute, FieldInfo fieldInfo)
            {
                PropInfo pi = new();
                pi.Initialize(property, attribute, fieldInfo);
                return pi;
            }
        }
    }

    [CustomPropertyDrawer(typeof(MaxAttribute))]
    public class MaxAttributeDrawer : MinMaxAttributeDrawer
    {
        public override long CapLong(long value, double cap) => Math.Min(value, (long)cap);
        public override ulong CapULong(ulong value, double cap) => Math.Min(value, (ulong)cap);
        public override double CapDecimal(double value, double cap) => Math.Min(value, cap);
    }


    [CustomPropertyDrawer(typeof(Min2Attribute))]
    public class Min2AttributeDrawer : MinMaxAttributeDrawer
    {
        public override long CapLong(long value, double cap) => Math.Max(value, (long)cap);
        public override ulong CapULong(ulong value, double cap) => Math.Max(value, (ulong)cap);
        public override double CapDecimal(double value, double cap) => Math.Max(value, cap);
    }
}

