using System;
using System.Diagnostics;

namespace CustomInspector
{
    /// <summary>
    /// Removes or adds an indentlevel from current indentLevel
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    [Conditional("UNITY_EDITOR")]
    public class IndentAttribute : ComparablePropertyAttribute
    {
        public readonly int additionalIndentLevel;
        public IndentAttribute(int additionalIndentLevel = 1)
        {
            order = -10;

            this.additionalIndentLevel = additionalIndentLevel;
        }

        protected override object[] GetParameters() => new object[] { additionalIndentLevel };
    }
}
