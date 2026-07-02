using CustomInspector.Extensions;
using CustomInspector.Helpers.Editor;
using UnityEditor;
using UnityEngine;

namespace CustomInspector.Editor
{
    [CustomPropertyDrawer(typeof(InterfaceAttribute))]
    [CustomPropertyDrawer(typeof(SerializableInterface<>))]
    public class SerializableInterfaceDrawer : TypedPropertyDrawer
    {
        public SerializableInterfaceDrawer() : base(nameof(InterfaceAttribute) + " can only be used on SerializableInterface",
            typeof(SerializableInterface<>)
            )
        { }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);

            if (!TryOnGUI(position, property, label))
                return;
            SerializedProperty referenceProperty = property.FindPropertyRelative("serializedReference");
            DrawProperties.PropertyField(position, label, referenceProperty);
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!TryGetPropertyHeight(property, label, out float fallbackHeight))
                return fallbackHeight;

            return EditorGUIUtility.singleLineHeight;
        }
    }
}
