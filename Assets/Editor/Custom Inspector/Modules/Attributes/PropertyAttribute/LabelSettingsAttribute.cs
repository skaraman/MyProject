using System;
using System.Diagnostics;

namespace CustomInspector
{
    /// <summary>
    /// Change the variable label in the unity inspector
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    [Conditional("UNITY_EDITOR")]
    public class LabelSettingsAttribute : ComparablePropertyAttribute
    {
        public readonly LabelStyle style;
        public readonly string newName = null;

        private LabelSettingsAttribute()
        {
            order = -5;
        }
        public LabelSettingsAttribute(LabelStyle style) : this()
        {
            this.style = style;
        }
        public LabelSettingsAttribute(string newName, LabelStyle style = LabelStyle.FullSpacing) : this()
        {
            this.newName = newName;
            this.style = style;
        }

        protected override object[] GetParameters() => new object[] { newName, style };
    }
}