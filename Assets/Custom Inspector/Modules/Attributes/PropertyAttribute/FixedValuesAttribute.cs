using System;
using System.Diagnostics;

namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    [Conditional("UNITY_EDITOR")]
    public class FixedValuesAttribute : ComparablePropertyAttribute
    {
        public object[] values;
        public FixedValuesAttribute(params object[] values)
        {
            this.values = values;
        }

        protected override object[] GetParameters() => values;
    }
}
