using System;
using System.Diagnostics;


namespace CustomInspector
{
    /// <summary>
    /// Fields in the inspector will be accessed through a getter and setter
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    [Conditional("UNITY_EDITOR")]
    public class GetSetAttribute : ComparablePropertyAttribute
    {
        public readonly string getmethodPath;
        public readonly string setmethodPath;

        public string label = null;
        public string tooltip = null;

        /// <param name="getmethodPath">The name of the method to display.</param>
        /// <param name="setmethodPath">The name of the method that gets called on changes with inspector input as parameter</param>
        public GetSetAttribute(string getmethodPath, string setmethodPath)
        {
            order = -10;
            this.getmethodPath = getmethodPath;
            this.setmethodPath = setmethodPath;
        }

        protected override object[] GetParameters() => new object[] { getmethodPath, setmethodPath };
    }
}