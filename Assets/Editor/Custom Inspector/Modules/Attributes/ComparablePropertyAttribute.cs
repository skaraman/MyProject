using System;
using System.Collections;
using UnityEngine;

namespace CustomInspector
{
    public abstract class ComparablePropertyAttribute : PropertyAttribute
    {
        /// <summary>
        /// All memebrs that are important for the reliable hash
        /// </summary>
        /// <returns></returns>
        protected abstract object[] GetParameters();

        public int GetReliableHash()
        {
            var paras = GetParameters();
            if ((paras?.Length ?? 0) == 0)
                return -1;

            var hash = new HashCode();
            foreach (var value in paras)
            {
                if (value is IEnumerable e)
                {
                    foreach (var v in e)
                        Add(v);
                }
                else
                    Add(value);

                void Add(object newHashObject)
                {
                    if (newHashObject == null)
                    {
                        hash.Add(-1);
                        return;
                    }

                    // Special method that returns same hash for different runs
                    if (newHashObject is string s)
                    {
                        hash.Add(StringComparer.Ordinal.GetHashCode(s));
                        return;
                    }

                    // Check if simple GetHashCode is now enough
                    Type t = newHashObject.GetType();
                    if (!t.IsPrimitive && !t.IsEnum)
                        Debug.LogError($"Type {newHashObject.GetType().Name} is invalid: Hash is not reliable and will not return the same values for new instances.");
                    hash.Add(newHashObject);
                }
            }
            return hash.ToHashCode();
        }
    }
}
