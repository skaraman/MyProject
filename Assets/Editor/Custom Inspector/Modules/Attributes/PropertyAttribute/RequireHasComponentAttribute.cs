using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CustomInspector
{
    /// <summary>
    /// Allows only values, that have specific Components attached to the same GameObject
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    [Conditional("UNITY_EDITOR")]
    public class RequireHasComponentAttribute : ComparablePropertyAttribute
    {
        public IReadOnlyList<Type> RequiredComponents => requiredComponents;
        readonly List<Type> requiredComponents;

        public RequireHasComponentAttribute(Type requiredComponent)
        {
            order = -10;
            this.requiredComponents = new() { requiredComponent };
        }

        public RequireHasComponentAttribute(Type requiredComponent1, Type requiredComponent2)
        : this(requiredComponent1)
        {
            requiredComponents.Add(requiredComponent2);
        }

        public RequireHasComponentAttribute(Type requiredComponent1, Type requiredComponent2, Type requiredComponent3, params Type[] moreRequiredComponents)
        : this(requiredComponent1, requiredComponent2)
        {
            requiredComponents.Add(requiredComponent3);
            foreach (var item in moreRequiredComponents)
                requiredComponents.Add(item);
        }

        protected override object[] GetParameters() => requiredComponents.Select(type => type.FullName).ToArray();
    }
}
