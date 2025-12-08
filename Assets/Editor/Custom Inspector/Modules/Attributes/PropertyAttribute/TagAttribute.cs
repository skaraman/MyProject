using System;
using System.Diagnostics;

namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field)]
    [Conditional("UNITY_EDITOR")]
    public class TagAttribute : ComparablePropertyAttribute
    {
        protected override object[] GetParameters() => null;
    }
}