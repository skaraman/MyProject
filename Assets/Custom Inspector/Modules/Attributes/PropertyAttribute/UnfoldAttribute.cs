using System;
using System.Diagnostics;

namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    [Conditional("UNITY_EDITOR")]
    public class UnfoldAttribute : ComparablePropertyAttribute
    {
        public UnfoldAttribute()
        {
            order = -10;
        }

        protected override object[] GetParameters() => null;
    }
}
