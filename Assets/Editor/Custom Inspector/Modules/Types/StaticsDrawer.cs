using System;
using UnityEngine;

namespace CustomInspector
{
    /// <summary>
    /// Only valid for StaticsDrawer! Used to fix overriding of other attributes
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public class StaticsDrawerAttribute : ComparablePropertyAttribute
    {
        public readonly StaticMembersSearchType searchType;
        public StaticsDrawerAttribute(StaticMembersSearchType searchType = StaticMembersSearchType.Instance)
        {
            this.searchType = searchType;
        }

        protected override object[] GetParameters() => new object[] { searchType };
    }

    /// <summary>
    /// This will display in the inspector all your static values
    /// </summary>
    [System.Serializable]
    public class StaticsDrawer
    {
        [MessageBox("You are overriding the default PropertyDrawer of StaticsDrawer." +
        "\nPlease add the [" + nameof(StaticsDrawerAttribute) + "] to the " + nameof(StaticsDrawer), MessageBoxType.Error)]
        [SerializeField, HideField] bool b;
    }

    public enum StaticMembersSearchType
    {
        /// <summary>
        /// Only members within the current class.
        /// Including privates.
        /// </summary>
        Instance,
        /// <summary>
        /// Inherited members on top of all "Instance" members (members from current class).
        /// </summary>
        FlattenHierarchy,
        /// <summary>
        /// All members in current and in all base classes.
        /// Including privates.
        /// </summary>
        AlsoInBases
    }
}
