using System;
using System.Diagnostics;

namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    [Conditional("UNITY_EDITOR")]
    public class Delayed2Attribute : ComparablePropertyAttribute
    {
        protected override object[] GetParameters() => new object[] { null };
    }
}
