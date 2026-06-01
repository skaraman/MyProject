using CustomInspector.Extensions;
using System;
using UnityEditor;
using UnityEngine;

namespace CustomInspector.Editor
{
    [CustomPropertyDrawer(typeof(CopyPasteAttribute))]
    public class CopyPasteAttributeDrawer : PropertyDrawer
    {
        const float buttonsSpaceEnd = 15;
        const float buttonsSpaceStart = 2;
        const float buttonWidth = 80;

        /// <summary>
        /// 'pasted' says, somewhere something was pasted. property.isExpanded says it was pasted here. Since 'pasted' is resetted to false every unity restart/recompile, we can use this to reset property.isExpanded too
        /// </summary>
        static bool pasted = false;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);

            //Check if type is supported
            if (!IsTypeSupported(property.propertyType))
            {
                DrawProperties.DrawPropertyWithMessage(position, label, property, $"Clipboard doesnt support {property.propertyType}", MessageType.Error);
                return;
            }

            Rect rect = new(position)
            {
                height = DrawProperties.GetPropertyHeight(label, property)
            };

            //Draw property
            if (property.isExpanded && pasted)
            {
                label.text += " (pasted)";
            }
            if (property.isExpanded && !pasted)
            {
                property.isExpanded = false;
                property.serializedObject.ApplyModifiedProperties();
            }
            EditorGUI.BeginChangeCheck();
            DrawProperties.PropertyField(rect, property: property, label: label, includeChildren: true);
            if (EditorGUI.EndChangeCheck())
            {
                property.isExpanded = pasted = false;
                property.serializedObject.ApplyModifiedProperties();
            }

            //Draw copy paste buttons
            {
                rect.y += rect.height + buttonsSpaceStart;
                rect.height = EditorGUIUtility.singleLineHeight;
                GUIContent copyLabel = new("Copy", "Copy value to your pc's clipboard");
                float copyWidth = Math.Min(position.width / 2, GUI.skin.label.CalcSize(copyLabel).x + buttonWidth);

                //Paste Button
                object value = null;
                try
                {
                    value = property.ParseString(GUIUtility.systemCopyBuffer);
                }
                catch { }

                if (value is not null)
                {
                    GUIContent pasteLabel = new GUIContent("Paste", $"Paste value from clipboard into field\nClipboard:\n{value}");

                    if (((CopyPasteAttribute)attribute).previewClipboard)
                    {
                        string preview = value.ToString();
                        float additionalLength = GUI.skin.label.CalcSize(new GUIContent(" (...)")).x;
                        float availableSpace = position.width - copyWidth;
                        if (GUI.skin.label.CalcSize(pasteLabel).x + additionalLength + buttonWidth + 15 < availableSpace) //+15, weil da soll auch was stehen und nicht nur (...) stehen können
                        {
                            pasteLabel.text += " (";
                            int i = 0;
                            for (; i < preview.Length
                                    && GUI.skin.label.CalcSize(pasteLabel).x + additionalLength + buttonWidth < availableSpace;
                                 i++)
                            {
                                if (preview[i] == '\n')
                                    pasteLabel.text += "\\n";
                                else
                                    pasteLabel.text += preview[i];
                            }
                            if (i < preview.Length)
                                pasteLabel.text += "...";
                            pasteLabel.text += ")";
                            rect.width = GUI.skin.label.CalcSize(pasteLabel).x + buttonWidth;
                        }
                        else
                        {
                            rect.width = position.width / 2;
                        }
                    }

                    rect.x = position.x + position.width - rect.width;

                    if (GUI.Button(rect, pasteLabel))
                    {
                        property.SetValue(value);
                        property.isExpanded = pasted = true;
                        property.serializedObject.ApplyModifiedProperties();
                    }
                }
                else
                {
                    GUIContent pasteLabel = new GUIContent("Paste", $"Current clipboard is not valid\nClipboard:\n{GUIUtility.systemCopyBuffer}");
                    rect.width = Math.Min(position.width / 2, GUI.skin.label.CalcSize(pasteLabel).x + buttonWidth);
                    rect.x = position.x + position.width - rect.width;
                    //parsing try failed
                    using (new EditorGUI.DisabledScope(true))
                        GUI.Button(rect, new GUIContent(pasteLabel));
                }

                //Copy Button
                rect.width = copyWidth;
                rect.x -= rect.width;
                try
                {
                    value = property.GetValue();
                }
                catch (NotSupportedException) { }

                if (value is not null)
                {
                    if (GUI.Button(rect, copyLabel))
                    {
                        GUIUtility.systemCopyBuffer = property.GetValue().ToString();
                        property.isExpanded = pasted = false;
                    }
                }
                else
                {
                    //parsing try failed
                    using (new EditorGUI.DisabledScope(true))
                        GUI.Button(rect, new GUIContent("Copy", $"property doesnt support copy"));
                }

            }
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!IsTypeSupported(property.propertyType))
                return DrawProperties.GetPropertyWithMessageHeight(label, property);
            else
                return DrawProperties.GetPropertyHeight(label, property)
                    + buttonsSpaceStart + EditorGUIUtility.singleLineHeight + buttonsSpaceEnd;
        }
        bool IsTypeSupported(SerializedPropertyType type)
        {
            switch (type)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.Float:
                case SerializedPropertyType.String:
                case SerializedPropertyType.Color:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3Int:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.RectInt:
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.BoundsInt:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.Quaternion:
                    return true;
                default:
                    return false;
            }
        }
    }
}