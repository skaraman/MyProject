using UnityEngine;
using UnityEditor;
using System.IO;

namespace ThemesPlugin
{
    public class CreateThemeWindow : EditorWindow
    {
        enum UnityTheme { FullDark, FullLight, Dark, Light, Both }

        UnityTheme unityTheme = UnityTheme.Dark;
        string themeName = "New Theme";

        [MenuItem("Themes/Create Theme")]
        public static void ShowWindow()
        {
            ThemeSettings.ShowWindow();
            CreateThemeWindow window = GetWindow<CreateThemeWindow>("Create Theme");
            window.minSize = new Vector2(380f, 260f);
            window.Show();
        }

        void OnEnable()
        {
            minSize = new Vector2(380f, 260f);
        }

        void OnGUI()
        {
            EditorThemeImguiStyleApplicator.EnsureAppliedFromOnGUI();
            DrawHeader();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Theme name", EditorStyles.boldLabel);
            GUI.SetNextControlName("ThemeName");
            themeName = EditorGUILayout.TextField(themeName);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Starting point", EditorStyles.boldLabel);
            unityTheme = (UnityTheme)EditorGUILayout.EnumPopup(unityTheme);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(GetPresetTitle(unityTheme), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(GetPresetDescription(unityTheme), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            DrawFooter();

            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return)
            {
                CreateTheme();
                e.Use();
            }
        }

        void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Create Theme", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Choose a preset, give it a clear name, then fine-tune the palette in the editor.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Cancel", GUILayout.Width(92f)))
            {
                Close();
            }

            if (GUILayout.Button("Create", GUILayout.Width(112f)))
            {
                CreateTheme();
            }

            EditorGUILayout.EndHorizontal();
        }

        void CreateTheme()
        {
            string trimmedName = themeName.Trim();
            if (string.IsNullOrEmpty(trimmedName))
            {
                EditorUtility.DisplayDialog("Theme name required", "Enter a name before creating a theme.", "OK");
                GUI.FocusControl("ThemeName");
                return;
            }

            string path = ThemesUtility.GetPathForTheme(trimmedName);
            if (File.Exists(path) &&
                !EditorUtility.DisplayDialog(
                    "Theme already exists",
                    "Replace the existing theme named " + trimmedName + "?",
                    "Replace",
                    "Cancel"))
            {
                return;
            }

            CustomTheme theme = FetchTheme(GetPresetFileName(unityTheme), trimmedName);
            ThemesUtility.SaveJsonFileForTheme(theme);
            ThemesUtility.OpenEditTheme(theme);
            Close();
        }

        CustomTheme FetchTheme(string presetName, string name)
        {
            CustomTheme customTheme = ThemesUtility.GetCustomThemeFromJson(ThemesUtility.PresetsPath + presetName + ".json");
            customTheme.Name = name;
            return customTheme;
        }

        string GetPresetFileName(UnityTheme theme)
        {
            switch (theme)
            {
                case UnityTheme.FullDark:
                    return "FullDark";
                case UnityTheme.FullLight:
                    return "FullLight";
                case UnityTheme.Light:
                    return "Light";
                case UnityTheme.Both:
                    return "Both";
                default:
                    return "Dark";
            }
        }

        string GetPresetTitle(UnityTheme theme)
        {
            switch (theme)
            {
                case UnityTheme.FullDark:
                    return "Full dark";
                case UnityTheme.FullLight:
                    return "Full light";
                case UnityTheme.Light:
                    return "Light";
                case UnityTheme.Both:
                    return "Dark and light";
                default:
                    return "Dark";
            }
        }

        string GetPresetDescription(UnityTheme theme)
        {
            switch (theme)
            {
                case UnityTheme.FullDark:
                    return "Broad dark preset with a larger set of Unity editor selectors.";
                case UnityTheme.FullLight:
                    return "Broad light preset with a larger set of Unity editor selectors.";
                case UnityTheme.Light:
                    return "Compact light preset for the most visible editor surfaces.";
                case UnityTheme.Both:
                    return "Compact preset that can be used with either Unity editor skin.";
                default:
                    return "Compact dark preset for the most visible editor surfaces.";
            }
        }
    }
}
