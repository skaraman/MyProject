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
    [CustomPropertyDrawer(typeof(TabAttribute))]
    public class TabAttributeDrawer : PropertyDrawer
    {
        /// <summary> Distance between outer rect and inner rects </summary>
        const float outerSpacing = 15;
        const float toolbarHeight = 25;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = PropertyValues.ValidateLabel(label, property);

            PropInfo info = cache.GetInfo(property, attribute, fieldInfo);

            if (info.errorMessage != null)
            {
                DrawProperties.DrawPropertyWithMessage(position, label, property, info.errorMessage, MessageType.Error);
                return;
            }
            if (info.groupInfos?.errorMessage != null)
            {
                DrawProperties.DrawPropertyWithMessage(position, label, property, info.groupInfos.errorMessage, MessageType.Error);
                return;
            }

            bool isVisible = info.tabGroupIndex == GetSelected(info.tabGroupIdentifier);

            //clip not seen
            if (!isVisible && !info.isAllFirst)
                return;

            float thisBlankPropertyHeight = DrawProperties.GetPropertyHeight(label, property);
            // only the first of horizontal group should draw, because in a horizontal alignment the new drawn would override the old
            // -> because tab is always centered and they cannot draw next to each other


            if (info.groupInfos == null || info.groupInfos.isGroupFirst)
            {
                //draw tab
                using (new NewIndentLevel(0))
                {
                    //Background
                    position.height = GetPropertyHeight(property, label);

                    float halfSpacing = EditorGUIUtility.standardVerticalSpacing / 2f;
                    if (!info.isAllFirst)
                    {
                        //mit oberen verbinden
                        position.y -= halfSpacing;
                        position.height += halfSpacing;
                    }


                    //mit unterem verbinden
                    if ((info.isAllFirst && !isVisible) || !info.isTabGroupLast) //allFirst (the toolbar) has to connect all
                        position.height += halfSpacing;

                    float groupHeight = 0; // only the most to the right side property contains the max height of all
                    if (info.groupInfos != null
                        && info.groupInfos.groupLastPath != property.propertyPath) //if group has only one member, he can be first and last and then doesnt need extra height, because he is already most right 
                    {
                        SerializedProperty last = property.serializedObject.FindProperty(info.groupInfos.groupLastPath);
                        groupHeight = DrawProperties.GetPropertyHeight(last) + EditorGUIUtility.standardVerticalSpacing;
                    }
                    position.height += groupHeight;

                    EditorGUI.DrawRect(position, InternalEditorStylesConvert.DarkerBackground);

                    position.height -= groupHeight;

                    //verbindung oben ende
                    if (!info.isAllFirst)
                        position.y += halfSpacing;

                    //abstand zu oben
                    if (info.isAllFirst)
                        position.y += EditorGUIUtility.standardVerticalSpacing;

                    //sides distance
                    position = ExpandRectWidth(position, -outerSpacing);
                    //Toolbar
                    if (info.isAllFirst)
                    {
                        Rect tRect = new(position)
                        {
                            height = toolbarHeight
                        };
                        GUIContent[] guiContents = GetTabGroupNames(info.tabGroupIdentifier);
                        EditorGUI.BeginChangeCheck();
                        int res = GUI.Toolbar(tRect, GetSelected(info.tabGroupIdentifier), guiContents);
                        if (EditorGUI.EndChangeCheck())
                        {
                            // Deselect current field
                            GUI.FocusControl(null);
                            // Show new Tab
                            // Debug.Log("Tab switched");
                            SetSelected(info.tabGroupIdentifier, res);
                        }

                        position.y = tRect.y + toolbarHeight + outerSpacing + EditorGUIUtility.standardVerticalSpacing;
                    }
                }
            }
            else
                //sides distance
                position = ExpandRectWidth(position, -outerSpacing);

            //Draw Property
            if (isVisible)
            {
                position.height = thisBlankPropertyHeight;
                EditorGUI.BeginChangeCheck();
                DrawProperties.PropertyField(position, label, property);
                if (EditorGUI.EndChangeCheck())
                    property.serializedObject.ApplyModifiedProperties();
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            PropInfo info = cache.GetInfo(property, attribute, fieldInfo);

            if (info.errorMessage != null || info.groupInfos?.errorMessage != null)
                return DrawProperties.GetPropertyWithMessageHeight(label, property);

            bool isVisible = info.tabGroupIndex == GetSelected(info.tabGroupIdentifier);

            float totalHeight = isVisible ?
                        DrawProperties.GetPropertyHeight(label, property) : -EditorGUIUtility.standardVerticalSpacing;

            if (info.isAllFirst)
            {
                totalHeight += EditorGUIUtility.standardVerticalSpacing
                    + toolbarHeight
                    + outerSpacing
                    + EditorGUIUtility.standardVerticalSpacing;
            }
            if (info.isTabGroupLast && isVisible)
                totalHeight += outerSpacing;
            return totalHeight;
        }

        Rect ExpandRectWidth(Rect rect, float value)
        {
            rect.x -= value;
            rect.width += 2 * value;
            return rect;
        }


        /// <summary> all group names for each tabGroupIdentifier </summary>
        readonly static Dictionary<Type, GUIContent[]> tabGroupNames = new();
        static GUIContent[] GetTabGroupNames(Type t)
        {
            if (!tabGroupNames.TryGetValue(t, out GUIContent[] res))
            {
                var names = FindTabGroupNames(t).ToArray();
                res = names.Select(_ => StylesConvert.ToInternalIconName(_))
                           .Select(_ => InternalEditorStylesConvert.IconNameToGUIContent(_))
                           .ToArray();

                tabGroupNames.Add(t, res);
            }
            return res;
        }
        static IEnumerable<string> FindTabGroupNames(Type classType)
        {
            IEnumerable<FieldInfo> fields = classType.GetAllSerializableFields(alsoFromBases: true);
            IEnumerable<TabAttribute> allAttr = fields.Select(_ => _.GetCustomAttribute<TabAttribute>()).Where(_ => _ is not null);
            return allAttr.Select(_ => _.groupName).Distinct();
        }
        ///<summary>tabGroupIdentifier to selected group</summary>
        readonly static Dictionary<Type, int> selected = new();
        static int GetSelected(Type classType)
        {
            if (selected.TryGetValue(classType, out int value))
                return value;
            else
                return 0;
        }
        void SetSelected(Type classType, int value)
        {
            if (!selected.TryAdd(classType, value))
                selected[classType] = value;
        }

        readonly static PropInfoCache<PropInfo> cache = new();

        class PropInfo : ICachedPropInfo
        {
            /// <summary> Definies in which of the tabs he is in </summary>
            public int tabGroupIndex;
            /// <summary> Defines which classes tab group definies if it is visible </summary>
            public Type tabGroupIdentifier;
            /// <summary> Defines, if it has to draw the toolbar </summary>
            public bool isAllFirst;
            /// <summary> Defines, if it has to draw space on the end </summary>
            public bool isTabGroupLast;
            public string errorMessage = null;

            /// <summary> If not null, he is in a horizontal group </summary>
            public HorizontalGroupInfos groupInfos;

            public void Initialize(SerializedProperty property, PropertyAttribute attribute, FieldInfo fieldInfo)
            {
                TabAttribute tabAttribute = (TabAttribute)attribute;

                groupInfos = HorizontalGroupInfos.CreateNewHorizontalGroupInfos(property, attribute, fieldInfo);

                //In array/list nothing matters - just error it | other enumerable like one Transform are allowed
                if (IsListOrArray(fieldInfo.FieldType)) //is list
                {
                    errorMessage = "TabAttribute not valid on list elements." +
                        $"\nReplace field-type with {typeof(ListContainer<>).FullName} to apply all attributes to whole list instead of to single elements";
                    return;
                }

                //-
                tabGroupIdentifier = property.GetOwnerAsFinder().GetPropertyType();
                (isAllFirst, isTabGroupLast) = GetMyPosition();
                tabGroupIndex = FindTabGroupNames(tabGroupIdentifier).ToList().IndexOf(tabAttribute.groupName);
                Debug.Assert(tabGroupIndex != -1, $"No group for {fieldInfo.Name} found");


                (bool isAllFirst, bool isGroupLast) GetMyPosition()
                {
                    bool isAllFirst = false;
                    bool isGroupLast = false;

                    var allProps = PropertyValues.GetAllSerializableFields(tabGroupIdentifier, alsoFromBases: true)
                        .Select(f => (fieldInfo: f, attr: f.GetCustomAttribute<TabAttribute>()))
                        .Where(ta => ta.attr != null);

                    (FieldInfo fieldInfo, TabAttribute ta) first = allProps
                        .Where(t => !IsListOrArray(t.fieldInfo.FieldType)) // header would not be visible on list elements
                        .FirstOrDefault();
                    if (first.fieldInfo?.Name == fieldInfo.Name) // we trust on unity not allowing same same in derived classes (the same field accessed from a derived class seemed to return false on '==' - but i am not sure, so getting the name is safer)
                        isAllFirst = true;

                    var propsInMyGroup = allProps.Where(_ => _.attr.groupName == tabAttribute.groupName);

                    if (!propsInMyGroup.Any())
                    {
                        Debug.LogError($"No properties found for Tab-group '{tabAttribute.groupName}' in '{tabGroupIdentifier}'" +
                                       $"\nError at displaying {fieldInfo.Name}");
                        return (false, false);
                    }
                    var last_iG = propsInMyGroup.Last();
                    if (last_iG.fieldInfo.Name == fieldInfo.Name)
                        isGroupLast = true;

                    return (isAllFirst, isGroupLast);
                }
            }
        }
        static bool IsListOrArray(Type t) => t.IsArray //is array
                    || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>));
        class HorizontalGroupInfos
        {
            /// <summary>
            /// if he starts the horizontal alignment
            /// </summary>
            public readonly bool isGroupFirst;
            /// <summary>
            /// the path of the last one in the group
            /// </summary>
            public readonly string groupLastPath;

            public readonly string errorMessage = null;

            private HorizontalGroupInfos(bool isGroupFirst, string groupLastPath)
            { this.isGroupFirst = isGroupFirst; this.groupLastPath = groupLastPath; }

            public static HorizontalGroupInfos CreateNewHorizontalGroupInfos(SerializedProperty property, PropertyAttribute attribute, FieldInfo fieldInfo)
            {
                HorizontalGroupInfos res;

                if (fieldInfo.GetCustomAttribute<HorizontalGroupAttribute>() == null)
                {
                    res = null;
                }
                else
                {
                    HorizontalGroupAttributeDrawer.GroupMember[] group
                                = HorizontalGroupAttributeDrawer.GroupMembers.GetGroup(property, attribute);

                    Debug.Assert(group.Length > 0, "Horizontal-group could not be found");


                    bool isFirst = group[0].id.propertyPath == property.propertyPath;
                    string groupLast = group[^1].id.propertyPath;

                    res = new HorizontalGroupInfos(isFirst, groupLast);
                }

                return res;
            }
        }
    }
}