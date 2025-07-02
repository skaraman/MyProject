using CustomInspector.Extensions;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CustomInspector.Editor
{
    [CustomPropertyDrawer(typeof(TupleAttribute))]
    [CustomPropertyDrawer(typeof(SerializableTuple<,>))]
    [CustomPropertyDrawer(typeof(SerializableTuple<,,>))]
    [CustomPropertyDrawer(typeof(SerializableTuple<,,,>))]
    [CustomPropertyDrawer(typeof(SerializableTuple<,,,,>))]
    [CustomPropertyDrawer(typeof(SerializableTuple<,,,,,>))]
    public class TupleDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);

            using (new NewIndentLevel(EditorGUI.indentLevel))
            {
                if (!string.IsNullOrEmpty(label.text))
                {
                    position.height = EditorGUIUtility.singleLineHeight;
                    EditorGUI.LabelField(position, label);
                    position.y += position.height;
                    EditorGUI.indentLevel++;
                }
                position = EditorGUI.IndentedRect(position);
                using (new NewIndentLevel(0))
                {
                    EditorGUI.BeginChangeCheck();
                    foreach (SerializedProperty prop in property.GetAllVisibleProperties(true))
                    {
                        DrawProperties.PropertyFieldWithoutLabel(position, prop);
                    }
                    if (EditorGUI.EndChangeCheck())
                        property.serializedObject.ApplyModifiedProperties();
                }
            }
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var allProps = property.GetAllVisibleProperties(true);
            if (!string.IsNullOrEmpty(label.text))
                return EditorGUIUtility.singleLineHeight + allProps.Max(_ => DrawProperties.GetPropertyHeight(_));
            else
                return allProps.Max(_ => DrawProperties.GetPropertyHeight(_));
        }
    }
}
