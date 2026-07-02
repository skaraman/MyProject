using System;
using System.Diagnostics;


namespace CustomInspector
{
    /// <summary>
    /// Draws an ObjectField constrained to given type (like some interface)
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    [Conditional("UNITY_EDITOR")]
    public class RequireTypeAttribute : ComparablePropertyAttribute
    {
        public System.Type requiredType { get; private set; } = null;
        public bool UseBaseGenericArg => requiredType == null;

        internal RequireTypeAttribute(bool UseBaseGenericArg)
        {
            if (!UseBaseGenericArg)
                throw new NotImplementedException("Use type parameter if UseBaseGenericArg is not wanted");
        }

        public RequireTypeAttribute(System.Type type)
        {
            requiredType = type ?? throw new ArgumentNullException("Type cannot be null");
        }

        protected override object[] GetParameters() => new object[] { requiredType?.FullName };
    }
}