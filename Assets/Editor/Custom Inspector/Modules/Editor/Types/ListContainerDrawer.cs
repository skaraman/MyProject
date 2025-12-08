using CustomInspector.Extensions;
using CustomInspector.Helpers.Editor;
using UnityEditor;
using UnityEngine;

namespace CustomInspector.Editor
{
    [CustomPropertyDrawer(typeof(ArrayContainer<>))]
    [CustomPropertyDrawer(typeof(ArrayContainerAttribute))]
    [CustomPropertyDrawer(typeof(ListContainer<>))]
    [CustomPropertyDrawer(typeof(ListContainerAttribute))]
    public class ListContainerDrawer : TypedPropertyDrawer
    {
        public ListContainerDrawer() : base(nameof(ListContainerAttribute) + " and " + nameof(ArrayContainerAttribute)
        + " can only be used on ArrayContainer and ListContainer",
            typeof(ArrayContainer<>),
            typeof(ListContainer<>)
            )
        { }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);

            if (!TryOnGUI(position, property, label))
                return;

            SerializedProperty v = property.FindPropertyRelative("values");
            Debug.Assert(v != null, "Values not found in ListContainer");
            EditorGUI.BeginChangeCheck();
            DrawProperties.PropertyField(position, label, v);
            if (EditorGUI.EndChangeCheck())
                v.serializedObject.ApplyModifiedProperties();
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!TryGetPropertyHeight(property, label, out float fallbackHeight))
                return fallbackHeight;

            var v = property.FindPropertyRelative("values");
            Debug.Assert(v != null, "Values not found in ListContainer");
            return DrawProperties.GetPropertyHeight(label, v);
        }
    }
}
