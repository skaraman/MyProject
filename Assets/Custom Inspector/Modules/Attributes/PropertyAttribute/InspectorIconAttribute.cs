using System;
using System.Diagnostics;

namespace CustomInspector
{

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    [Conditional("UNITY_EDITOR")]
    public class InspectorIconAttribute : ComparablePropertyAttribute
    {
        public readonly InspectorIcon icon;
        public readonly bool appendAtEnd;
        public InspectorIconAttribute(InspectorIcon icon, bool appendAtEnd = false)
        {
            order = -10;
            this.icon = icon;
            this.appendAtEnd = appendAtEnd;
        }

        protected override object[] GetParameters() => new object[] { icon, appendAtEnd };
    }
}
