using System;
using System.Diagnostics;


namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    [Conditional("UNITY_EDITOR")]
    public class GUIColorAttribute : ComparablePropertyAttribute
    {
        public readonly string colorString = null;
        public readonly FixedColor? fixedColor = null;
        public readonly bool colorWholeUI;

        protected override object[] GetParameters()
        => fixedColor.HasValue ?
            new object[] { fixedColor, colorWholeUI }
            : new object[] { colorString, colorWholeUI };

        public GUIColorAttribute(string color = "(0.9, 0.0, 0, 1)", bool colorWholeUI = false)
        {
            order = -10;
            this.colorString = color;
            this.colorWholeUI = colorWholeUI;
        }
        public GUIColorAttribute(FixedColor fixedColor, bool colorWholeUI = false)
        {
            order = -10;
            this.fixedColor = fixedColor;
            this.colorWholeUI = colorWholeUI;
        }
    }
}
