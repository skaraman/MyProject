using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CustomInspector.Helpers
{
    /// <summary>
    /// Identifies a property on a component.
    /// Two properties on different components cannot be differentiated!
    /// Used to save errors or other information on specific properties for performance through different OnGUI's.
    /// </summary>
    public class PropertyAttributeIdentifier
    {
        public readonly Type targetObjectType;
        public readonly string propertyPath;
        public readonly int attributeHash;

        public PropertyAttributeIdentifier(SerializedProperty property, PropertyAttribute attribute)
        {
            this.targetObjectType = property.serializedObject.targetObject.GetType();
            this.propertyPath = property.propertyPath;
            if (attribute == null)
                this.attributeHash = 0;
            else if (attribute is ComparablePropertyAttribute comparableAttr)
                this.attributeHash = comparableAttr.GetReliableHash();
            else
                throw new ArgumentException($"Attribute {attribute.GetType()} must derive from {nameof(ComparablePropertyAttribute)} to create an identifier.");
        }
        public PropertyAttributeIdentifier(Type targetObject, string fullPath, int attributeHash)
        {
            this.targetObjectType = targetObject;
            this.propertyPath = fullPath;
            this.attributeHash = attributeHash;
        }



        public override bool Equals(object obj)
        {
            if (obj is PropertyAttributeIdentifier identifier)
            {
                return EqualityComparer<Type>.Default.Equals(targetObjectType, identifier.targetObjectType) &&
                        propertyPath == identifier.propertyPath &&
                        attributeHash == identifier.attributeHash;
            }
            return false;
        }
        public override int GetHashCode() => HashCode.Combine(targetObjectType, propertyPath, attributeHash);

        public static bool operator ==(PropertyAttributeIdentifier i1, PropertyAttributeIdentifier i2) => i1?.Equals(i2) ?? i2 == null;
        public static bool operator !=(PropertyAttributeIdentifier i1, PropertyAttributeIdentifier i2) => (!i1?.Equals(i2)) ?? i2 != null;
    }

    /// <summary>
    /// Identifies properties.
    /// Differentiates also between properties on different components
    /// </summary>
    class ExactPropertyIdentifier
    {
        readonly SerializedObject targetObject;
        readonly string propertyPath;
        readonly int attributeHash;

        public ExactPropertyIdentifier(SerializedProperty property, PropertyAttribute attribute)
        {
            targetObject = property.serializedObject;
            propertyPath = property.propertyPath;
            if (attribute is ComparablePropertyAttribute comparableAttr)
                this.attributeHash = comparableAttr.GetReliableHash();
            else
                throw new ArgumentException($"Attribute {attribute.GetType()} must derive from {nameof(ComparablePropertyAttribute)} to create an identifier.");
        }

        public override bool Equals(object o)
        {
            if (o is ExactPropertyIdentifier other)
            {
                return targetObject == other.targetObject
                    && propertyPath == other.propertyPath
                    && attributeHash == other.attributeHash;
            }
            else return false;
        }
        public override int GetHashCode() => HashCode.Combine(targetObject, propertyPath, attributeHash);

        public static bool operator ==(ExactPropertyIdentifier i1, ExactPropertyIdentifier i2) => i1?.Equals(i2) ?? i2 == null;
        public static bool operator !=(ExactPropertyIdentifier i1, ExactPropertyIdentifier i2) => (!i1?.Equals(i2)) ?? i2 != null;
    }
}
