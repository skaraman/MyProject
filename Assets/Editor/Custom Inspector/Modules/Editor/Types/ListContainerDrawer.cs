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

            if (property.propertyType == SerializedPropertyType.ManagedReference)
            {
                DrawProperties.DrawMessageField(position, new($"ListContainer does not support {nameof(SerializedPropertyType.ManagedReference)}." +
                    $"\nRemove the [{nameof(SerializeReference)}]-attribute or use the default List instead."), MessageType.Error);
                return;
            }

            SerializedProperty v = property.FindPropertyRelative("values");
            if (v == null)
            {
                DrawProperties.DrawPropertyWithMessage(position, label, property,
                    $"List values of type '{fieldInfo.FieldType.GetGenericArguments()[0].Name}' are not serializable.\n" +
                     "Note: Make sure to add the [Serializable]-attribute to your custom classes.", MessageType.Error,
                    includeChildren: false, disabled: true);
                return;
            }
            EditorGUI.BeginChangeCheck();
            DrawProperties.PropertyField(position, label, v);
            if (EditorGUI.EndChangeCheck())
                v.serializedObject.ApplyModifiedProperties();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!TryGetPropertyHeight(property, label, out float fallbackHeight))
                return fallbackHeight;

            if (property.propertyType == SerializedPropertyType.ManagedReference)
                return DrawProperties.messageBoxHeight + EditorGUIUtility.standardVerticalSpacing;

            var v = property.FindPropertyRelative("values");
            if (v == null)
                return DrawProperties.GetPropertyWithMessageHeight(label, property, includeChildren: false);
            return DrawProperties.GetPropertyHeight(label, v);
        }
    }
}
