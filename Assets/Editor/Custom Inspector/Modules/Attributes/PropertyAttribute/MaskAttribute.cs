using System;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace CustomInspector
{
    [AttributeUsage(AttributeTargets.Field)]
    [Conditional("UNITY_EDITOR")]
    public class MaskAttribute : ComparablePropertyAttribute
    {
        public readonly int bitsAmount = 3;
        public readonly string[] bitNames = null;

        /// <summary>
        /// Used without parameters for enums that should be displayed as mask
        /// </summary>
        public MaskAttribute() { }
        /// <summary>
        /// bitsAmount is only used for integers and not enums
        /// </summary>
        public MaskAttribute(int bitsAmount)
        {
            if (bitsAmount <= 0)
                Debug.LogWarning($"Bitsamount on {nameof(MaskAttribute)} should not be negative");
            this.bitsAmount = bitsAmount;
        }
        /// <summary>
        /// Used to label the single bits. The bitAmount equals the amount of names given
        /// </summary>
        public MaskAttribute(params string[] bitNames)
        {
            this.bitNames = bitNames;
            this.bitsAmount = bitNames.Length;
        }

        protected override object[] GetParameters() => new object[] { bitsAmount, bitNames };
    }
}