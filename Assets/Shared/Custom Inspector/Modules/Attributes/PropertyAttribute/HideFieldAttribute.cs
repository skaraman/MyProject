using System;
using System.Diagnostics;

namespace CustomInspector
{
    /// <summary>
    /// Hides the field in the inspector but not attributes attached to it
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    [Conditional("UNITY_EDITOR")]
    public class HideFieldAttribute : ComparablePropertyAttribute
    {
        public HideFieldAttribute()
        {
            order = 1;
        }

        protected override object[] GetParameters() => null;
    }
}

