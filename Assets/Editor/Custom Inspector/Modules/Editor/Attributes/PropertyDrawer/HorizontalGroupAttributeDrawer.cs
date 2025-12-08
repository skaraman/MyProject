using CustomInspector.Extensions;
using CustomInspector.Helpers;
using CustomInspector.Helpers.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CustomInspector.Editor
{
    [CustomPropertyDrawer(typeof(HorizontalGroupAttribute))]
    public class HorizontalGroupAttributeDrawer : PropertyDrawer
    {
        /// <summary>
        /// Range: [0, 1]
        /// 1 means label takes whole width and field is zero width
        /// </summary>
        const float labelFieldProportion = 0.4f;

        /// <summary>
        /// Distance (in pixel) between two properties in same horizontal group
        /// </summary>
        const float propertiesSpacing = 9;
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);

            try
            {
                PropertyAttributeIdentifier id = new(property, attribute);
                if (!savedInfos.TryGetValue(id, out InspectorInfo info))
                {
                    info = new InspectorInfo(position, property, (HorizontalGroupAttribute)attribute, fieldInfo);
                    savedInfos.Add(id, info);
                }

                position = EditorGUI.IndentedRect(position);
                Rect rect = new()
                {
                    x = info.startX(position),
                    y = position.y,
                    width = info.width(position.width),
                    height = EditorGUI.GetPropertyHeight(property, label),
                };

                using (new NewIndentLevel(0))
                {
                    EditorGUI.BeginChangeCheck();
                    if (info.errorMessage == null)
                    {
                        using (new LabelWidthScope(rect.width * labelFieldProportion))
                        {
                            DrawProperties.PropertyField(rect, label, property);
                        }
                    }
                    else
                    {
                        DrawProperties.DrawPropertyWithMessage(rect, label, property, info.errorMessage, info.errorType);
                    }
                    if (EditorGUI.EndChangeCheck())
                        property.serializedObject.ApplyModifiedProperties();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            try
            {
                PropertyAttributeIdentifier id = new(property, attribute);
                GroupMember[] group = propertyGroups.GetInfo(property, attribute, fieldInfo).values;


                // Get height
                if (group[^1].id == id) //is last
                {
                    //My infos
                    storedHeights.Push((id, GetHeight()));

                    //Try-catch because pushed item MUST be popped
                    try
                    {
                        //let themselves enter in heights dict if they are bigger
                        foreach (var groupMember in group.Take(group.Length - 1)) //skip last, cuz its self
                        {
                            SerializedProperty prop = property.serializedObject.FindProperty(groupMember.id.propertyPath);
                            DrawProperties.GetPropertyHeight(prop);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }

                    //dispose variable
                    (_, float maxHeight) = storedHeights.Pop();
                    //return biggest
                    return maxHeight;
                }
                else
                {
                    //wenn der letzte gerade sucht, dann meine dazu
                    if (storedHeights.Any()
                        && storedHeights.Peek().last.propertyPath == group[^1].id.propertyPath)
                    {
                        (PropertyAttributeIdentifier last, float maxHeight) current = storedHeights.Pop();
                        storedHeights.Push((current.last, Mathf.Max(GetHeight(), current.maxHeight)));
                    }

                    return -EditorGUIUtility.standardVerticalSpacing;
                }

                float GetHeight()
                {
                    bool hasMessage;
                    if (savedInfos.TryGetValue(id, out InspectorInfo info))
                        hasMessage = info.errorMessage != null;
                    else
                        hasMessage = property.IsArrayElement() || group.Length == 1;

                    if (hasMessage)
                        return DrawProperties.GetPropertyWithMessageHeight(label, property);
                    else
                        return DrawProperties.GetPropertyHeight(label, property);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return 0;
            }
        }

        /// <summary> This is a Stack, because there can be horizontal groups inside other horizontal groups and the last is always the current interest </summary>
        readonly static Stack<(PropertyAttributeIdentifier last, float maxHeight)> storedHeights = new();

        // For performance, the property can access its informations out of Dictionary instead of recalculate each time
        readonly static Dictionary<PropertyAttributeIdentifier, InspectorInfo> savedInfos = new();
        static readonly PropInfoCache<GroupMembers> propertyGroups = new();
        public class GroupMembers : ICachedPropInfo
        {
            public GroupMember[] values;

            public void Initialize(SerializedProperty property, PropertyAttribute attribute, FieldInfo fieldInfo)
            {
                values = GetGroup(property, attribute);
            }
            public static GroupMember[] GetGroup(SerializedProperty property, PropertyAttribute attr)
            {
                if (property == null)
                    throw new NullReferenceException("property is null");

                if (property.IsArrayElement()) //arr element is not allowed
                    return new[] { new GroupMember(new PropertyAttributeIdentifier(property, attr), 1) };

                var owner = property.GetOwnerAsFinder();

                List<(SerializedProperty prop, float size)> props = new();

                bool foundMyGroup = false; //we are looking for the group the property is in

                TabAttribute currentTabAttribute = null;
                TabAttribute previousTabAttribute = null;

                var allProps = owner.GetAllVisibleProperties(true);
                if (!allProps.Any())
                    throw new GroupNotFoundException($"Could not find any properties in {owner.Name} typeof({owner.GetPropertyType()})");
                foreach (SerializedProperty prop in allProps)
                {
                    //Check is in group
                    DirtyValue dv = new DirtyValue(prop);
                    HorizontalGroupAttribute hg = dv.GetAttribute<HorizontalGroupAttribute>();

                    previousTabAttribute = currentTabAttribute;
                    currentTabAttribute = dv.GetAttribute<TabAttribute>();

                    if (hg is null)
                    {
                        if (foundMyGroup)
                            break;
                        else
                        {
                            if (prop.name == property.name)
                                throw new GroupNotFoundException($"Provided property '{property.propertyPath}' was not flagged with {nameof(HorizontalGroupAttribute)}");
                            props.Clear();
                            continue;
                        }
                    }
                    if (hg.beginNewGroup == true
                        || currentTabAttribute?.groupName != previousTabAttribute?.groupName) //also split at different tabs
                    {
                        if (foundMyGroup)
                            break;
                        else
                            props.Clear();
                    }
                    //Add
                    props.Add((prop, hg.size));
                    if (prop.name == property.name)
                        foundMyGroup = true;
                }
                if (!foundMyGroup)
                {
                    throw new GroupNotFoundException($"Provided property '{property.propertyPath}' not found in {owner.Name} typeof({owner.GetPropertyType()})");
                }

                //Test
                Debug.Assert(props.Count > 0, "Property group is empty");
                //Return
                return props.Select(_ => new GroupMember(new PropertyAttributeIdentifier(_.prop, attr), _.size)).ToArray();
            }
        }
        public readonly struct GroupMember
        {
            public readonly PropertyAttributeIdentifier id;
            public readonly float size;

            public GroupMember(PropertyAttributeIdentifier id, float size)
            {
                this.id = id;
                this.size = size;
            }
        }

        class InspectorInfo
        {
            //If error
            public string errorMessage = null;
            public MessageType errorType;

            //position
            /// <summary> Returns the position_x, if you input the original position </summary>
            public Func<Rect, float> startX;
            /// <summary> Returns the width, if you input the position.width </summary>
            public Func<float, float> width;

            public InspectorInfo(Rect position, SerializedProperty property, HorizontalGroupAttribute attribute, FieldInfo fieldInfo)
            {
                //Check if not list element
                if (property.IsArrayElement()) //is element in a list
                {
                    errorMessage = "HorizontalGroup not valid on lists";
                    errorType = MessageType.Error;
                    startX = (r) => r.x;
                    width = (w) => w;

                    return;
                }

                //Get values
                //List<(SerializedProperty prop, float size)> props = GetAllPropsInGroup(property);
                GroupMember[] group = propertyGroups.GetInfo(property, attribute, fieldInfo).values;

                //Check again
                if (group.Length == 1) //alone in group
                {
                    if (attribute.beginNewGroup)
                        errorMessage = $"{PropertyConversions.NameFormat(property.name)} is alone in a horizontal group. Maybe set \"beginNewGroup\"=false to let him join the previous group";
                    else
                        errorMessage = $"unnecessary assignment of HorizontalGroupAttribute ({PropertyConversions.NameFormat(property.name)} is alone in a horizontal group). " +
                        $"All members of the group must have the attribute and stand behind each other in the code. " +
                        $"Members cannot be in different [Tab]-attribute groups";
                    errorType = MessageType.Warning;
                    startX = (r) => r.x;
                    width = (w) => w;
                    return;
                }

                //Draw
                float customTotalWidth = group.Select(_ => _.size).Sum();
                float spacingSum = (group.Length - 1) * propertiesSpacing;
                Func<float, float> realWidth = (positionWidth) => (positionWidth - spacingSum);
                float widthMult = attribute.size / customTotalWidth;
                width = (w) => (realWidth(w) * widthMult);
                int myIndex = group.ToList().FindIndex(_ => _.id.propertyPath == property.propertyPath);

                float previousSizes = group.Take(myIndex).Sum(_ => _.size);
                startX = (r) => (r.x + realWidth(r.width) / customTotalWidth * previousSizes + myIndex * propertiesSpacing);
            }
        }
        class GroupNotFoundException : Exception
        {
            public GroupNotFoundException(string message) : base(message) { }
        }
    }
}