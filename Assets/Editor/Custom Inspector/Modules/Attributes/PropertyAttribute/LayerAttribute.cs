using System;
using System.Diagnostics;

namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field)]
    [Conditional("UNITY_EDITOR")]
    public class LayerAttribute : ComparablePropertyAttribute
    {
        public readonly string requiredName = null;
        public LayerAttribute(string requiredName = null)
        {
            this.requiredName = requiredName;
        }

        protected override object[] GetParameters() => new object[] { requiredName };
    }
}