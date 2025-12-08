using CustomInspector.Extensions;
using System;
using System.Diagnostics;
using UnityEngine;

namespace CustomInspector
{
    /// <summary>
    /// Use this for fields that should be filled with a component from the same gameObject
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    [Conditional("UNITY_EDITOR")]
    public class SelfFillAttribute : ComparablePropertyAttribute
    {
        public OwnerMode mode = OwnerMode.Self;
        public readonly bool hideIfFilled;
        public SelfFillAttribute(bool hideIfFilled = false)
        {
            order = -5;

            this.hideIfFilled = hideIfFilled;
        }

        protected override object[] GetParameters() => new object[] { hideIfFilled };
    }
    public enum OwnerMode
    {
        /// <summary>
        /// Current GameObject (default mode)
        /// </summary>
        Self,
        /// <summary>
        /// Children or nested children (of children) of Transform
        /// </summary>
        Children,
        /// <summary>
        /// Transform's parent
        /// </summary>
        Parent,
        /// <summary>
        /// Transform's root (most top parent)
        /// </summary>
        Root,
        /// <summary>
        /// Transform's parent, parent of parent, ..., up to transforms root
        /// </summary>
        Parents,
        /// <summary>
        /// Only children of Transform and not nested children (of children)
        /// </summary>
        DirectChildren,
    }

    /// <summary>
    /// A helper class to test in the editor, if all fields in the inspector are filled.
    /// </summary>
    public static class SelfFill
    {
        /// <summary>
        /// Prints an Error, if any field in Class has Attribute [SelfFill] and is null (to check if everything got filled in the inspector)
        /// <para>It prints found fields' names and the transforms Hierarchies of the Monobehaviour</para>
        /// <para>This function is conditional: It wont cut performance in build</para>
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void CheckSelfFilled(this MonoBehaviour @object)
            => @object.CheckFilled(attributeType: typeof(SelfFillAttribute));

        /// <summary>
        /// Prints an Error, if any field in Class has Attribute [SelfFill] and is null (to check if everything got filled in the inspector)
        /// <para>It prints found fields' names and the given owners Hierarchies of the Monobehaviour</para>
        /// <para>This function is conditional: It wont cut performance in build</para>
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void CheckSelfFilled(this object @object, Transform owner)
            => @object.CheckFilled(owner, typeof(SelfFillAttribute));
    }
}