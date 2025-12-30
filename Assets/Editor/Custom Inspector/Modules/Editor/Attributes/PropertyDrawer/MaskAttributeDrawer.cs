using CustomInspector.Extensions;
using CustomInspector.Helpers;
using CustomInspector.Helpers.Editor;
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;


namespace CustomInspector.Editor
{
    [CustomPropertyDrawer(typeof(MaskAttribute))]
    public class MaskAttributeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);
            PropInfo info = cache.GetInfo(property, attribute, fieldInfo);

            if (info.ErrorMessage != null)
            {
                DrawProperties.DrawPropertyWithMessage(position, label, property, info.ErrorMessage, MessageType.Error);
                return;
            }

            position.height = EditorGUIUtility.singleLineHeight;

            if (property.propertyType == SerializedPropertyType.Enum)
            {
                EditorGUI.BeginChangeCheck();
                Enum res = EditorGUI.EnumFlagsField(position, label, (Enum)Enum.ToObject(fieldInfo.FieldType, property.intValue));
                if (EditorGUI.EndChangeCheck())
                {
                    property.intValue = (int)Convert.ChangeType(res, typeof(int));
                    property.serializedObject.ApplyModifiedProperties();
                }
            }
            else
            {
                int mask = info.GetMask(property);
                EditorGUI.BeginChangeCheck();
                mask = DrawBitMaskField(position, label, mask, info.BitNames);
                if (EditorGUI.EndChangeCheck())
                {
                    info.SaveMask(property, mask);
                    property.serializedObject.ApplyModifiedProperties();
                }
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            PropInfo info = cache.GetInfo(property, attribute, fieldInfo);
            if (info.ErrorMessage != null)
                return DrawProperties.GetPropertyWithMessageHeight(label, property);

            return EditorGUIUtility.singleLineHeight;
        }

        private static int DrawBitMaskField(Rect position, GUIContent label, int mask, string[] bitNames)
        {
            Rect labelRect = new(position)
            {
                width = EditorGUIUtility.labelWidth,
            };
            EditorGUI.LabelField(labelRect, label);

            Rect toggleRect = new(position)
            {
                x = labelRect.x + labelRect.width,
                width = EditorGUIUtility.singleLineHeight,
            };

            using (new NewIndentLevel(0))
            {
                Debug.Assert(bitNames != null, "BitNames must not be null, since it defines the amount of bits.");
                for (int i = 0; i < bitNames.Length; i++)
                {
                    string bitName = bitNames[i];
                    if (!string.IsNullOrEmpty(bitName))
                    {
                        GUIContent bitLabelGuiC = new(bitName, $"Bit {i}");
                        Rect bitLabelR = new(toggleRect)
                        {
                            width = GUI.skin.label.CalcSize(bitLabelGuiC).x,
                        };
                        EditorGUI.LabelField(bitLabelR, bitLabelGuiC);
                        toggleRect.x += bitLabelR.width + EditorGUIUtility.standardVerticalSpacing;
                    }

                    bool res = EditorGUI.Toggle(toggleRect, (mask & (1 << i)) != 0);
                    if (res)
                    {
                        mask |= 1 << i;
                    }
                    else
                    {
                        mask &= ~(1 << i);
                    }

                    toggleRect.x += toggleRect.width + EditorGUIUtility.standardVerticalSpacing;
                    //if out of view
                    if (toggleRect.x > position.x + position.width)
                        break;
                }
            }

            return mask;
        }


        readonly static PropInfoCache<PropInfo> cache = new();

        class PropInfo : ICachedPropInfo
        {
            public string ErrorMessage { get; private set; }
            public string[] BitNames { get; private set; }
            public Func<SerializedProperty, int> GetMask { get; private set; }
            public Action<SerializedProperty, int> SaveMask { get; private set; }

            public PropInfo() { }
            public void Initialize(SerializedProperty property, PropertyAttribute attribute, FieldInfo fieldInfo)
            {
                MaskAttribute maskAttr = (MaskAttribute)attribute;

#pragma warning disable IDE0350 // Use implicitly typed lambda
                if (property.propertyType == SerializedPropertyType.Integer)
                {
                    BitNames = maskAttr.bitNames;
                    GetMask = property => property.intValue;
                    SaveMask = (SerializedProperty property, int mask) => property.intValue = mask;
                    return;
                }
                else if (property.propertyType == SerializedPropertyType.Enum)
                {
                    BitNames = maskAttr.bitNames;
                    GetMask = property => throw new ArgumentException("Enums dont need to work with masks. Use EnumFlagsField instead.");
                    SaveMask = (SerializedProperty property, int mask) => throw new ArgumentException("Enums dont need to work with masks. Use EnumFlagsField instead.");
                    return;
                }
                else if (Common.IsVectorType(property.propertyType))
                {
                    string[] requestedBitNames = maskAttr.bitNames ?? new string[0];

                    if (property.propertyType == SerializedPropertyType.Vector2)
                    {
                        BitNames = new string[] { " X", " Y" }; // Default labels
                        GetMask = property =>
                        {
                            Vector2 v2Value = property.vector2Value;
                            int mask = 0;
                            if (v2Value.x != 0)
                                mask |= (1 << 0);
                            if (v2Value.y != 0)
                                mask |= (1 << 1);
                            return mask;
                        };
                        SaveMask = (SerializedProperty property, int mask) =>
                        {
                            Vector2 v2Value = Vector2.zero;
                            if ((mask & (1 << 0)) != 0)
                                v2Value.x = 1;
                            if ((mask & (1 << 1)) != 0)
                                v2Value.y = 1;
                            property.vector2Value = v2Value;
                        };
                    }
                    else if (property.propertyType == SerializedPropertyType.Vector2Int)
                    {
                        BitNames = new string[] { " X", " Y" }; // Default labels
                        GetMask = property =>
                        {
                            Vector2Int v2Value = property.vector2IntValue;
                            int mask = 0;
                            if (v2Value.x != 0)
                                mask |= (1 << 0);
                            if (v2Value.y != 0)
                                mask |= (1 << 1);
                            return mask;
                        };
                        SaveMask = (SerializedProperty property, int mask) =>
                        {
                            Vector2Int v2Value = Vector2Int.zero;
                            if ((mask & (1 << 0)) != 0)
                                v2Value.x = 1;
                            if ((mask & (1 << 1)) != 0)
                                v2Value.y = 1;
                            property.vector2IntValue = v2Value;
                        };
                    }
                    else if (property.propertyType == SerializedPropertyType.Vector3)
                    {
                        BitNames = new string[] { " X", " Y", " Z" }; // Default labels
                        GetMask = property =>
                        {
                            Vector3 v3Value = property.vector3Value;
                            int mask = 0;
                            if (v3Value.x != 0)
                                mask |= (1 << 0);
                            if (v3Value.y != 0)
                                mask |= (1 << 1);
                            if (v3Value.z != 0)
                                mask |= (1 << 2);
                            return mask;
                        };
                        SaveMask = (SerializedProperty property, int mask) =>
                        {
                            Vector3 v3Value = Vector3.zero;
                            if ((mask & (1 << 0)) != 0)
                                v3Value.x = 1;
                            if ((mask & (1 << 1)) != 0)
                                v3Value.y = 1;
                            if ((mask & (1 << 2)) != 0)
                                v3Value.z = 1;
                            property.vector3Value = v3Value;
                        };
                    }
                    else if (property.propertyType == SerializedPropertyType.Vector3Int)
                    {
                        BitNames = new string[] { " X", " Y", " Z" }; // Default labels
                        GetMask = property =>
                        {
                            Vector3Int v3Value = property.vector3IntValue;
                            int mask = 0;
                            if (v3Value.x != 0)
                                mask |= (1 << 0);
                            if (v3Value.y != 0)
                                mask |= (1 << 1);
                            if (v3Value.z != 0)
                                mask |= (1 << 2);
                            return mask;
                        };
                        SaveMask = (SerializedProperty property, int mask) =>
                        {
                            Vector3Int v3Value = Vector3Int.zero;
                            if ((mask & (1 << 0)) != 0)
                                v3Value.x = 1;
                            if ((mask & (1 << 1)) != 0)
                                v3Value.y = 1;
                            if ((mask & (1 << 2)) != 0)
                                v3Value.z = 1;
                            property.vector3IntValue = v3Value;
                        };
                    }
                    else if (property.propertyType == SerializedPropertyType.Vector4)
                    {
                        BitNames = new string[] { " X", " Y", " Z", " W" }; // Default labels
                        GetMask = property =>
                        {
                            Vector4 v4Value = property.vector4Value;
                            int mask = 0;
                            if (v4Value.x != 0)
                                mask |= (1 << 0);
                            if (v4Value.y != 0)
                                mask |= (1 << 1);
                            if (v4Value.z != 0)
                                mask |= (1 << 2);
                            if (v4Value.w != 0)
                                mask |= (1 << 3);
                            return mask;
                        };
                        SaveMask = (SerializedProperty property, int mask) =>
                        {
                            Vector4 v4Value = Vector4.zero;
                            if ((mask & (1 << 0)) != 0)
                                v4Value.x = 1;
                            if ((mask & (1 << 1)) != 0)
                                v4Value.y = 1;
                            if ((mask & (1 << 2)) != 0)
                                v4Value.z = 1;
                            if ((mask & (1 << 3)) != 0)
                                v4Value.w = 1;
                            property.vector4Value = v4Value;
                        };
                    }

                    // Apply requested names from attribute parameter
                    int lengthToCopy = Math.Min(requestedBitNames.Length, BitNames.Length);
                    for (int i = 0; i < lengthToCopy; i++)
                    {
                        if (requestedBitNames[i] != null)
                            BitNames[i] = requestedBitNames[i];
                    }
                    return;
                }
                else
                {
                    ErrorMessage = "MaskAttribute only supports integers, enums and vectors";
                    return;
                }

                throw new NotImplementedException();
#pragma warning restore IDE0350 // Use implicitly typed lambda
            }
        }
    }
}