using System;
using System.Diagnostics;

namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    [Conditional("UNITY_EDITOR")]
    public class FoldoutAttribute : ComparablePropertyAttribute
    {
        public FoldoutAttribute()
        {
            order = -6;
        }

        protected override object[] GetParameters() => null;
    }
}
