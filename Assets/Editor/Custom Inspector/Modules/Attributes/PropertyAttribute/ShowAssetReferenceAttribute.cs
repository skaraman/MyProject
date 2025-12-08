using System;
using System.Diagnostics;

namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    [Conditional("UNITY_EDITOR")]
    public class ShowAssetReferenceAttribute : ComparablePropertyAttribute
    {
        public readonly string fileName = null;

        public ShowAssetReferenceAttribute(string fileName = null)
        {
            this.fileName = fileName;
        }

        protected override object[] GetParameters() => new object[] { fileName };
    }
}
