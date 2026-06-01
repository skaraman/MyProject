using CustomInspector.Extensions;
using UnityEditor;
using UnityEngine;

namespace CustomInspector.Editor
{
    [CustomPropertyDrawer(typeof(AssetsOnlyAttribute))]
    public class AssetsOnlyAttributeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);

            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                DrawProperties.DrawPropertyWithMessage(position, label, property,
                                "SceneObjectsOnlyAttribute only supports ObjectReferences", MessageType.Error);
                return;
            }

            string tooltipMessage = "You cannot fill sceneObjects in here";
            label.tooltip = (string.IsNullOrEmpty(label.tooltip)) ? tooltipMessage : $"{label.tooltip}\n{tooltipMessage}";

            EditorGUI.BeginChangeCheck();
            var res = EditorGUI.ObjectField(position: position, label: label, obj: property.objectReferenceValue, objType: fieldInfo.FieldType, allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck())
            {
                property.objectReferenceValue = res;
                property.serializedObject.ApplyModifiedProperties();
            }
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.propertyType == SerializedPropertyType.ObjectReference)
                return DrawProperties.GetPropertyHeight(label, property);
            else
                return DrawProperties.GetPropertyWithMessageHeight(label, property);
        }
    }
}