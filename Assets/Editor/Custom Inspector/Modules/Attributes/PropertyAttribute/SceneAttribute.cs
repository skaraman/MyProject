using System;
using System.Diagnostics;

namespace CustomInspector
{

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false)]
    [Conditional("UNITY_EDITOR")]
    public class SceneAttribute : ComparablePropertyAttribute
    {
        public readonly bool useFullPath;
        public SceneAttribute(bool useFullPath = false)
        {
            this.useFullPath = useFullPath;
        }

        protected override object[] GetParameters() => new object[] { useFullPath };
    }
}
