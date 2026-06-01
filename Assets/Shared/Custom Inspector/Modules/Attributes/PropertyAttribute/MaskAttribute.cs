using System;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field)]
    [Conditional("UNITY_EDITOR")]
    public class MaskAttribute : ComparablePropertyAttribute
    {
        /// <summary>
        /// Shows the requested names for each displayed bit.
        /// Never null, but members can be null.
        /// </summary>
        public readonly string[] bitNames = null;

        /// <summary>
        /// Used without parameters for enums that should be displayed as mask
        /// </summary>
        public MaskAttribute()
            : this(3) // show 3 bits by default
        {
            bitNames = new string[3];
        }
        /// <summary>
        /// bitsAmount is only used for integers and not enums
        /// </summary>
        public MaskAttribute(int bitsAmount)
        {
            if (bitsAmount <= 0)
                Debug.LogWarning($"Bitsamount on {nameof(MaskAttribute)} should not be negative");
            bitNames = new string[bitsAmount];
        }
        /// <summary>
        /// Used to label the single bits. The bitAmount equals the amount of names given
        /// </summary>
        public MaskAttribute(params string[] bitNames)
        {
            this.bitNames = bitNames;
        }

        protected override object[] GetParameters() => bitNames;
    }
}