using UnityEngine;

namespace CustomInspector.Helpers.Editor
{
    /// <summary>
    /// GUIContent that also stores owner of the property
    /// this is for showing a label in the unity inspector
    /// </summary>
    public class LabelContent : GUIContent
    {
        public readonly string originalPropertyName = null;

        public LabelContent(string text, string tooltip, string originalPropertyName) : base(text, tooltip)
        {
            this.originalPropertyName = originalPropertyName;
        }
    }
}
