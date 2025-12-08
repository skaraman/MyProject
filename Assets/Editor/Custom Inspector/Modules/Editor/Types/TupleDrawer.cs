using CustomInspector.Extensions;
using CustomInspector.Helpers.Editor;
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
    public class TupleDrawer : TypedPropertyDrawer
    {
        public TupleDrawer() : base(nameof(TupleAttribute) + " can only be used on CustomInspector Tuples",
        typeof(SerializableTuple<,>),
        typeof(SerializableTuple<,,>),
        typeof(SerializableTuple<,,,>),
        typeof(SerializableTuple<,,,,>),
        typeof(SerializableTuple<,,,,,>)
        )
        { }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);

            if (!TryOnGUI(position, property, label))
                return;

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
            if (!TryGetPropertyHeight(property, label, out float fallbackHeight))
                return fallbackHeight;

            var allProps = property.GetAllVisibleProperties(true);
            if (!string.IsNullOrEmpty(label.text))
                return EditorGUIUtility.singleLineHeight + allProps.Max(_ => DrawProperties.GetPropertyHeight(_));
            else
                return allProps.Max(_ => DrawProperties.GetPropertyHeight(_));
        }
    }
}
