using System;
using System.Diagnostics;

namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    [Conditional("UNITY_EDITOR")]
    public class TooltipBoxAttribute : ComparablePropertyAttribute
    {
        public readonly string content;
        public TooltipBoxAttribute(string content)
        {
            order = -6;
            this.content = content;
        }

        protected override object[] GetParameters() => new object[] { content };
    }
}
