using System;
using System.Diagnostics;

namespace CustomInspector
{

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false)]
    [Conditional("UNITY_EDITOR")]
    public class AnimatorParameterAttribute : ComparablePropertyAttribute
    {
        /// <summary>
        /// Path to an Animator or to an AnimatorController
        /// </summary>
        public readonly string animatorPath;

        public AnimatorParameterAttribute(string animatorPath)
        {
            this.animatorPath = animatorPath;
        }

        protected override object[] GetParameters() => new object[] { animatorPath };
    }
}
