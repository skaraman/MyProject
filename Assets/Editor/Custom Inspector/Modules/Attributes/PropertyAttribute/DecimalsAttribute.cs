using System;
using System.Diagnostics;

namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    [Conditional("UNITY_EDITOR")]
    public class DecimalsAttribute : ComparablePropertyAttribute
    {
        public readonly int amount;
        public DecimalsAttribute(int amount)
        {
            order = -10;
            this.amount = amount;
        }

        protected override object[] GetParameters() => new object[] { amount };
    }
}