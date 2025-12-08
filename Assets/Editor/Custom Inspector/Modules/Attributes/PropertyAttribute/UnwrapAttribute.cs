using System;
using System.Diagnostics;


namespace CustomInspector
{
    /// <summary>
    /// Shows the seriaziled fields of the class instead of if wrapped with a foldout
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    [Conditional("UNITY_EDITOR")]
    public class UnwrapAttribute : ComparablePropertyAttribute
    {
        /// <summary>
        /// Whether to insert the class name in front of each unwrapped fields label/name in the inspector
        /// </summary>
        public bool applyName = false;

        protected override object[] GetParameters() => null;
    }
}