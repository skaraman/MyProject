using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

namespace ThemesPlugin
{
    public class ThemeSettings : EditorWindow
    {
        const float SwatchSize = 16f;

        readonly List<CustomTheme> darkThemes = new List<CustomTheme>();
        readonly List<CustomTheme> lightThemes = new List<CustomTheme>();
        readonly List<CustomTheme> bothThemes = new List<CustomTheme>();

        Vector2 scrollPosition;
        string searchText = "";

        [MenuItem("Themes/Select Themes")]
        public static void ShowWindow()
        {
            ThemeSettings window = GetWindow<ThemeSettings>("Editor Themes");
            window.minSize = new Vector2(420f, 420f);
            window.Show();
        }

        void OnEnable()
        {
            minSize = new Vector2(420f, 420f);
            RefreshThemeLists();
        }

        void OnFocus()
        {
            RefreshThemeLists();
        }

        void OnGUI()
        {
            EditorThemeImguiStyleApplicator.EnsureAppliedFromOnGUI();
            DrawHeader();
            DrawToolbar();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawThemeGroup("Dark & Light", bothThemes);
            DrawThemeGroup("Dark", darkThemes);
            DrawThemeGroup("Light", lightThemes);
            EditorGUILayout.EndScrollView();
        }

        void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Editor Themes", EditorStyles.largeLabel);

            string currentTheme = ThemesUtility.GetDisplayNameForThemePath(ThemesUtility.currentTheme);
            EditorGUILayout.LabelField("Current theme", currentTheme);
            EditorGUILayout.EndVertical();
        }

        void DrawToolbar()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();

            searchText = EditorGUILayout.TextField(searchText, EditorStyles.toolbarSearchField);

            if (GUILayout.Button("Create", EditorStyles.miniButtonLeft, GUILayout.Width(78f)))
            {
                CreateThemeWindow.ShowWindow();
            }

            if (GUILayout.Button("Import", EditorStyles.miniButtonMid, GUILayout.Width(78f)))
            {
                ImportTheme();
            }

            if (GUILayout.Button("Refresh", EditorStyles.miniButtonRight, GUILayout.Width(78f)))
            {
                RefreshThemeLists();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
        }

        void DrawThemeGroup(string title, List<CustomTheme> themes)
        {
            List<CustomTheme> filteredThemes = GetFilteredThemes(themes);

            EditorGUILayout.Space(6f);
            DrawSectionHeader(title, filteredThemes.Count);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (filteredThemes.Count == 0)
            {
                EditorGUILayout.LabelField("No themes found.", EditorStyles.miniLabel);
            }
            else
            {
                foreach (CustomTheme theme in filteredThemes)
                {
                    DrawThemeItem(theme);
                }
            }

            EditorGUILayout.EndVertical();
        }

        void DrawSectionHeader(string title, int count)
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.largeLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(count.ToString(), EditorStyles.miniLabel, GUILayout.Width(28f));
            EditorGUILayout.EndHorizontal();
            DrawDivider();
        }

        void DrawThemeItem(CustomTheme theme)
        {
            string name = theme.Name;
            string displayName = ThemesUtility.GetDisplayNameForThemeName(name);
            bool isCurrent = IsCurrentTheme(name);

            EditorGUILayout.BeginHorizontal();

            DrawPalettePreview(theme);

            EditorGUILayout.LabelField(displayName, EditorStyles.boldLabel);
            if (isCurrent)
            {
                GUILayout.Label("Active", EditorStyles.miniButton, GUILayout.Width(52f));
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(isCurrent ? "Reapply" : "Apply", GUILayout.Width(70f)))
            {
                ThemesUtility.LoadUssFileForTheme(name);
                Repaint();
            }

            using (new EditorGUI.DisabledScope(theme.IsUnEditable))
            {
                if (GUILayout.Button("Edit", GUILayout.Width(56f)))
                {
                    ThemesUtility.OpenEditTheme(theme);
                }
            }

            using (new EditorGUI.DisabledScope(theme.IsUnDeletable))
            {
                if (GUILayout.Button("Delete", GUILayout.Width(64f)))
                {
                    DeleteTheme(theme, displayName);
                }
            }

            EditorGUILayout.EndHorizontal();
            DrawDivider();
        }

        void DrawPalettePreview(CustomTheme theme)
        {
            Rect paletteRect = GUILayoutUtility.GetRect((SwatchSize + 2f) * 6f, SwatchSize, GUILayout.Width((SwatchSize + 2f) * 6f));

            for (int i = 0; i < 6; i++)
            {
                Rect swatchRect = new Rect(paletteRect.x + i * (SwatchSize + 2f), paletteRect.y, SwatchSize, SwatchSize);
                EditorGUI.DrawRect(swatchRect, GetAverageColor(theme, i));
            }
        }

        void DrawDivider()
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 1f);
            EditorGUI.DrawRect(rect, new Color(0.24f, 0.24f, 0.24f, 0.35f));
        }

        List<CustomTheme> GetFilteredThemes(List<CustomTheme> themes)
        {
            List<CustomTheme> filteredThemes = new List<CustomTheme>();
            string query = searchText.Trim().ToLowerInvariant();

            foreach (CustomTheme theme in themes)
            {
                string displayName = ThemesUtility.GetDisplayNameForThemeName(theme.Name);
                if (string.IsNullOrEmpty(query) || displayName.ToLowerInvariant().Contains(query))
                {
                    filteredThemes.Add(theme);
                }
            }

            return filteredThemes;
        }

        void RefreshThemeLists()
        {
            darkThemes.Clear();
            lightThemes.Clear();
            bothThemes.Clear();

            if (!Directory.Exists(ThemesUtility.CustomThemesPath))
            {
                return;
            }

            foreach (string path in Directory.GetFiles(ThemesUtility.CustomThemesPath, "*" + ThemesUtility.Enc))
            {
                CustomTheme theme = ThemesUtility.GetCustomThemeFromJson(path);
                switch (theme.unityTheme)
                {
                    case CustomTheme.UnityTheme.Dark:
                        darkThemes.Add(theme);
                        break;
                    case CustomTheme.UnityTheme.Light:
                        lightThemes.Add(theme);
                        break;
                    case CustomTheme.UnityTheme.Both:
                        bothThemes.Add(theme);
                        break;
                }
            }

            darkThemes.Sort(CompareThemes);
            lightThemes.Sort(CompareThemes);
            bothThemes.Sort(CompareThemes);
        }

        int CompareThemes(CustomTheme left, CustomTheme right)
        {
            return string.Compare(
                ThemesUtility.GetDisplayNameForThemeName(left.Name),
                ThemesUtility.GetDisplayNameForThemeName(right.Name),
                System.StringComparison.OrdinalIgnoreCase);
        }

        bool IsCurrentTheme(string themeName)
        {
            if (string.IsNullOrEmpty(ThemesUtility.currentTheme))
            {
                return false;
            }

            return ThemesUtility.GetPathForTheme(themeName) == ThemesUtility.currentTheme;
        }

        void DeleteTheme(CustomTheme theme, string displayName)
        {
            if (!EditorUtility.DisplayDialog(
                    "Delete " + displayName + "?",
                    "Permanently delete this theme. This cannot be undone.",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            ThemesUtility.DeleteFileWithMeta(ThemesUtility.GetPathForTheme(theme.Name));
            ThemesUtility.LoadUssFileForTheme(ThemesUtility.DefaultThemeName);
            RefreshThemeLists();
        }

        void ImportTheme()
        {
            string sourcePath = EditorUtility.OpenFilePanel("Import Theme", "", "json");
            if (string.IsNullOrEmpty(sourcePath))
            {
                return;
            }

            CustomTheme theme;
            try
            {
                theme = ThemesUtility.GetCustomThemeFromJson(sourcePath);
            }
            catch (System.Exception exception)
            {
                EditorUtility.DisplayDialog("Import failed", "This file could not be read as an Editor Themes theme.\n\n" + exception.Message, "OK");
                return;
            }

            if (theme == null || string.IsNullOrEmpty(theme.Name))
            {
                EditorUtility.DisplayDialog("Import failed", "This theme file does not contain a valid theme name.", "OK");
                return;
            }

            if (!IsValidThemeFileName(theme.Name))
            {
                EditorUtility.DisplayDialog("Import failed", "This theme name contains characters that cannot be used in a file name.", "OK");
                return;
            }

            Directory.CreateDirectory(ThemesUtility.CustomThemesPath);

            string destinationPath = ThemesUtility.GetPathForTheme(theme.Name);
            string displayName = ThemesUtility.GetDisplayNameForThemeName(theme.Name);
            if (Path.GetFullPath(sourcePath) == Path.GetFullPath(destinationPath))
            {
                RefreshThemeLists();
                return;
            }

            if (File.Exists(destinationPath) &&
                !EditorUtility.DisplayDialog(
                    "Replace " + displayName + "?",
                    "A theme with this name already exists.",
                    "Replace",
                    "Cancel"))
            {
                return;
            }

            try
            {
                File.Copy(sourcePath, destinationPath, true);
                AssetDatabase.Refresh();
                RefreshThemeLists();
            }
            catch (System.Exception exception)
            {
                EditorUtility.DisplayDialog("Import failed", exception.Message, "OK");
            }
        }

        bool IsValidThemeFileName(string themeName)
        {
            return themeName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        Color GetAverageColor(CustomTheme theme, int groupIndex)
        {
            List<string> itemNames = ThemesUtility.GetColorListByInt(groupIndex);
            float r = 0f;
            float g = 0f;
            float b = 0f;
            float a = 0f;
            int count = 0;

            foreach (string itemName in itemNames)
            {
                CustomTheme.UIItem item = GetItemByName(theme, itemName);
                if (item == null)
                {
                    continue;
                }

                r += item.Color.r;
                g += item.Color.g;
                b += item.Color.b;
                a += item.Color.a;
                count++;
            }

            if (count == 0)
            {
                return new Color(0.25f, 0.25f, 0.25f, 1f);
            }

            return new Color(r / count, g / count, b / count, a / count);
        }

        CustomTheme.UIItem GetItemByName(CustomTheme theme, string itemName)
        {
            if (theme.Items == null)
            {
                return null;
            }

            foreach (CustomTheme.UIItem item in theme.Items)
            {
                if (item != null && item.Name == itemName)
                {
                    return item;
                }
            }

            return null;
        }
    }
}
