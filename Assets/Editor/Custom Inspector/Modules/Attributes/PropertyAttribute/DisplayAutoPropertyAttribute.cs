using System;
using System.Diagnostics;
using UnityEngine;


namespace CustomInspector
{
    /// <summary>
    /// Displays an AutoProperty (no serializing or saving supported)
    /// Obsolete: Look at the documentation for the alternative usage that supports serializing and saving
    /// </summary>
    [Obsolete("Use [field: <attribute>] instead to directly work with the field")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    [Conditional("UNITY_EDITOR")]
    public class DisplayAutoPropertyAttribute : PropertyAttribute
    {
        public readonly string propertyPath;
        public readonly bool allowChange;

        public string label = null;
        public string tooltip = null;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="propertyPath">Name of the auto-property</param>
        /// <param name="allowChange">Allow to change the value (while playing)</param>
        public DisplayAutoPropertyAttribute(string propertyPath, bool allowChange = true)
        {
            order = -10;
            this.propertyPath = propertyPath;
            this.allowChange = allowChange;
        }
    }
}