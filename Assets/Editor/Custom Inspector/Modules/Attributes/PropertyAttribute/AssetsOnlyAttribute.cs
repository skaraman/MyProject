using System;
using System.Diagnostics;


namespace CustomInspector
{
    /// <summary>
    /// Put this on a gameobject to forbit to fill sceneObjects
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    [Conditional("UNITY_EDITOR")]
    public class AssetsOnlyAttribute : ComparablePropertyAttribute
    {
        public AssetsOnlyAttribute()
        {
        }

        protected override object[] GetParameters() => null;
    }
}