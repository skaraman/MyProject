using UnityEditor;

namespace ThemesPlugin
{
    [InitializeOnLoad]
    static class EditorThemeStartup
    {
        static EditorThemeStartup()
        {
            EditorApplication.delayCall += ThemesUtility.LoadLastTheme;
        }
    }
}
