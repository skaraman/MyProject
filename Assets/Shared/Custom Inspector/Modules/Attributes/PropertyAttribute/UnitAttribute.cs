using System;
using System.Diagnostics;

namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    [Conditional("UNITY_EDITOR")]
    public class UnitAttribute : ComparablePropertyAttribute
    {
        public readonly string unitName;
        public UnitAttribute(string unitName)
        {
            order = -6;
            this.unitName = unitName;
        }

        protected override object[] GetParameters() => new object[] { unitName };
    }
}
