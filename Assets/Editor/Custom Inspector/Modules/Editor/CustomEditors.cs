using UnityEditor;
using UnityEngine;

namespace CustomInspector.Editor
{
    /// <summary>
    /// Note, that this Editors should not override Editors made for your specific class
    /// Please RESTART UNITY, if your Editor is not used (in the cache) yet!
    /// 
    /// These classes are an workaround for some Bugs in some Unity versions with its new inspector drawing, like:
    ///     - Properties shown in the inspector cannot be fully hidden and will always keep some space in the unity inspector
    ///     - Properties drawn in the same line can prevent clicking properties in same line.
    ///     (these errors occur only is some unity versions)
    /// With DrawDefaultInspector() Unity uses its reliable drawing system.
    /// These class can be deleted or commented out, if you write your own editor for the MonoBehaviour class or all Scriptables
    /// </summary>
    [CanEditMultipleObjects]
    [CustomEditor(typeof(MonoBehaviour), editorForChildClasses: true, isFallback = true)]
    public class MonoBehaviourEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
        }
    }
    [CanEditMultipleObjects]
    [CustomEditor(typeof(ScriptableObject), editorForChildClasses: true, isFallback = true)]
    public class ScriptableObjectEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
        }
    }
}
