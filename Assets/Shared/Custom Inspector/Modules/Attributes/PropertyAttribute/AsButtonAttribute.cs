using System;
using System.Diagnostics;

namespace CustomInspector
{
    public enum InspectorButtonState { isPressed, isSelected, notSelected, }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false)]
    [Conditional("UNITY_EDITOR")]
    public class AsButtonAttribute : ComparablePropertyAttribute
    {
        public readonly bool staysPressed;

        public Size size = Size.medium;
        public FixedColor selectedColor = FixedColor.PressedBlue;
        public string label = null;
        public string tooltip = "";

        public AsButtonAttribute(bool staysPressed = true)
        {
            this.staysPressed = staysPressed;
        }

        protected override object[] GetParameters() => new object[] { staysPressed };
    }
}
