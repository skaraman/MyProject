using CustomInspector.Extensions;
using CustomInspector.Helpers.Editor;
using System;
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

            Type type = DirtyValue.GetType(property);
            var referenceProperty = property.FindPropertyRelative("serializedReference");
            Debug.Assert(referenceProperty != null);

            EditorGUI.BeginChangeCheck();
            var res = EditorGUI.ObjectField(position, label, referenceProperty.objectReferenceValue, type.GetGenericArguments()[0], true);
            if (EditorGUI.EndChangeCheck())
            {
                referenceProperty.objectReferenceValue = res;
                referenceProperty.serializedObject.ApplyModifiedProperties();
            }
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!TryGetPropertyHeight(property, label, out float fallbackHeight))
                return fallbackHeight;

            return EditorGUIUtility.singleLineHeight;
        }
    }
}
