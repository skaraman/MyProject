using System;
using System.Diagnostics;


namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field)]
    [Conditional("UNITY_EDITOR")]
    public class CopyPasteAttribute : ComparablePropertyAttribute
    {
        public readonly bool previewClipboard;

        public CopyPasteAttribute(bool previewClipboard = true)
        {
            order = -10;
            this.previewClipboard = previewClipboard;
        }

        protected override object[] GetParameters() => new object[] { previewClipboard };
    }
}