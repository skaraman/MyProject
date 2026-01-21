using System;
using System.Diagnostics;

namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    [Conditional("UNITY_EDITOR")]
    public class RichTextAttribute : ComparablePropertyAttribute
    {
        public RichTextAttribute()
        {
            order = -1; // right before [Multiline]-attribute and [TextArea]-attribute
        }

        protected override object[] GetParameters() => null;
    }
}
