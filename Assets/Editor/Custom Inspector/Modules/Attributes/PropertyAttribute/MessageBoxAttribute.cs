using System;
using System.Diagnostics;

namespace CustomInspector
{
    public enum MessageBoxType { None, Info, Warning, Error }
    /// <summary>
    /// Draw a message box in the inspector. You can do it instead of the field or additionally
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    [Conditional("UNITY_EDITOR")]
    public class MessageBoxAttribute : ComparablePropertyAttribute
    {
        public readonly string content;
        public readonly MessageBoxType type;

        public MessageBoxAttribute(string content, MessageBoxType type = MessageBoxType.Error)
        {
            order = -10;

            this.content = content;
            this.type = type;
        }

        protected override object[] GetParameters() => new object[] { content, type };
    }
}