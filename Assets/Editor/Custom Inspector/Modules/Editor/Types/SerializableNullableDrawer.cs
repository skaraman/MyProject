using CustomInspector.Extensions;
using CustomInspector.Helpers.Editor;
using UnityEditor;
using UnityEngine;

namespace CustomInspector.Editor
{
    [CustomPropertyDrawer(typeof(SerializableNullable<>))]
    [CustomPropertyDrawer(typeof(NullableAttribute))]
    public class SerializableNullableDrawer : TypedPropertyDrawer
    {
        public SerializableNullableDrawer() : base(nameof(NullableAttribute) + " can only be used on SerializableNullable",
            typeof(SerializableNullable<>)
            )
        { }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);

            if (!TryOnGUI(position, property, label))
                return;

            SerializedProperty hasValueProp = property.FindPropertyRelative("hasValue");
            SerializedProperty valueProp = property.FindPropertyRelative("value");

            position.width -= EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            //Draw property
            bool hasValue = hasValueProp.boolValue;
            if (hasValue)
                DrawProperties.PropertyField(position, label, valueProp);
            else
                EditorGUI.LabelField(position, label, new GUIContent("null"));

            // button
            using (new NewIndentLevel(0))
            {
                Rect buttonRect = new(position)
                {
                    x = position.x + position.width + EditorGUIUtility.standardVerticalSpacing,
                    width = EditorGUIUtility.singleLineHeight,
                };
                if (GUI.Button(buttonRect, hasValue ? "-" : "+"))
                {
                    hasValueProp.boolValue ^= true;
                    property.serializedObject.ApplyModifiedProperties();
                }
            }
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!TryGetPropertyHeight(property, label, out float fallbackHeight))
                return fallbackHeight;

            SerializedProperty hasValueProp = property.FindPropertyRelative("hasValue");
            SerializedProperty valueProp = property.FindPropertyRelative("value");

            if (hasValueProp.boolValue)
                return EditorGUI.GetPropertyHeight(valueProp, label);
            else
                return EditorGUIUtility.singleLineHeight;
        }
    }
}
