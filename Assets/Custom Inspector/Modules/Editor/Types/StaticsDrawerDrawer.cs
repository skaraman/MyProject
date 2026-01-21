using CustomInspector.Extensions;
using CustomInspector.Helpers.Editor;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;


namespace CustomInspector.Editor
{
    //Draws all static properties of class. But only editable while playing! (because they cannot get serialized)
    [CustomPropertyDrawer(typeof(StaticsDrawer))]
    [CustomPropertyDrawer(typeof(StaticsDrawerAttribute))]
    public class StaticPropertyDrawerDrawer : TypedPropertyDrawer
    {
        public StaticPropertyDrawerDrawer() : base(nameof(StaticsDrawerAttribute) + " can only be used on " + nameof(StaticsDrawer),
        typeof(StaticsDrawer)
        )
        { }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);

            if (!TryOnGUI(position, property, label))
                return;

            position.height = EditorGUIUtility.singleLineHeight;

            GUIContent header = new("Static Fields:");
            if (!Application.isPlaying)
                header.tooltip = "You cannot change these values while not playing, because static variables doesnt get serialized";

            StaticFieldsInfo info = propertyInfos.GetInfo(property, attribute, fieldInfo);
            DirtyValue owner = DirtyValue.GetOwner(property);
            FieldInfo[] fields = info.FieldInfos;

            if (fields.Length <= 0)
            {
                EditorGUI.LabelField(position, header, EditorStyles.boldLabel);
                EditorGUI.LabelField(position, new GUIContent(" "), new GUIContent("(no static fields found)"));
            }
            else
            {
                EditorGUI.LabelField(position, header, EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope(1))
                {
                    using (new EditorGUI.DisabledScope(!Application.isPlaying)) //changes doesnt get serialized and so on not saved anyways
                    {
                        foreach (FieldInfo field in fields)
                        {
                            position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

                            DirtyValue value = owner.FindRelative(field.Name);

                            GUIContent content = new GUIContent(PropertyConversions.NameFormat(field.Name));
                            if (!Application.isPlaying)
                                content.tooltip = "You cannot change this value while not playing, because static variables doesnt get serialized";

                            EditorGUI.BeginChangeCheck();
                            object res = DrawProperties.DrawField(position, content, value.GetValue(), field.FieldType);
                            if (EditorGUI.EndChangeCheck())
                                value.SetValue(res);
                        }
                    }
                    ;
                }
            }

        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!TryGetPropertyHeight(property, label, out float fallbackHeight))
                return fallbackHeight;

            StaticFieldsInfo info = propertyInfos.GetInfo(property, attribute, fieldInfo);
            FieldInfo[] fields = info.FieldInfos;

            return EditorGUIUtility.singleLineHeight //leadline
                + fields.Length * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
        }
        static readonly PropInfoCache<StaticFieldsInfo> propertyInfos = new();
        class StaticFieldsInfo : ICachedPropInfo
        {
            public FieldInfo[] FieldInfos { get; private set; }
            public void Initialize(SerializedProperty property, PropertyAttribute attribute, FieldInfo fieldInfo)
            {
                BindingFlags bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

                FieldInfos = GetFields();

                FieldInfo[] GetFields()
                {
                    Type fieldType = DirtyValue.GetOwner(property).Type;
                    if (fieldType == null)
                        return new FieldInfo[0];

                    if (attribute != null && attribute is StaticsDrawerAttribute attr)
                    {
                        if (attr.searchType == StaticMembersSearchType.FlattenHierarchy)
                            bindingFlags |= BindingFlags.FlattenHierarchy;
                        else if (attr.searchType == StaticMembersSearchType.AlsoInBases)
                        {
                            // Full search
                            Type type = fieldType;
                            List<FieldInfo> fields = new();
                            while (type != null)
                            {
                                fields.AddRange(type.GetFields(bindingFlags));
                                // Debug.Log("type=" + type);
                                type = type.BaseType;
                            }
                            // Debug.Log(string.Join(", ", fields));
                            return fields.ToArray();
                        }
                    }

                    return fieldType.GetFields(bindingFlags);
                }
            }
        }
    }
}