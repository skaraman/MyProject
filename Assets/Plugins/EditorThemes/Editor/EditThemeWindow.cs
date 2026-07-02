using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

namespace ThemesPlugin
{
    public class EditThemeWindow : EditorWindow
    {
        static readonly string[] ViewNames = { "Palette", "Advanced" };
        static readonly string[] ColorGroupNames =
        {
            "Base",
            "Accent",
            "Secondary base",
            "Tabs",
            "Command bar",
            "Additional"
        };

        public static CustomTheme ct;

        readonly List<Color> simpleColors = new List<Color>();
        readonly List<Color> lastSimpleColors = new List<Color>();

        string themeName;
        string selectorSearch = "";
        Vector2 scrollPosition;
        int viewIndex;
        bool rHold;
        bool ctrlHold;

        void OnEnable()
        {
            minSize = new Vector2(480f, 520f);
        }

        public void SetTheme(CustomTheme theme)
        {
            ct = theme;
            if (ct == null)
            {
                return;
            }

            ResetSimpleColorsFromTheme();
            themeName = ct.Name;
            Repaint();
        }

        void OnDestroy()
        {
            ct = null;
        }

        void Awake()
        {
            if (ct == null)
            {
                return;
            }

            ResetSimpleColorsFromTheme();
            themeName = ct.Name;
        }

        void OnGUI()
        {
            EditorThemeImguiStyleApplicator.EnsureAppliedFromOnGUI();

            if (ct == null)
            {
                Close();
                return;
            }

            HandleRegenerateShortcut();
            DrawHeader();

            viewIndex = GUILayout.Toolbar(viewIndex, ViewNames);
            EditorGUILayout.Space(6f);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            if (viewIndex == 0)
            {
                DrawPaletteView();
            }
            else
            {
                DrawAdvancedView();
            }
            EditorGUILayout.EndScrollView();

            DrawFooter();
        }

        void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Edit Theme", EditorStyles.largeLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6f);
            DrawSectionHeader("Details");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Name", GUILayout.Width(72f));
            themeName = EditorGUILayout.TextField(themeName);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Unity skin", GUILayout.Width(72f));
            ct.unityTheme = (CustomTheme.UnityTheme)EditorGUILayout.EnumPopup(ct.unityTheme);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        void DrawPaletteView()
        {
            DrawSectionHeader("Palette");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            for (int i = 0; i < simpleColors.Count; i++)
            {
                DrawColorGroup(i);
            }

            EditorGUILayout.EndVertical();

            for (int i = 0; i < simpleColors.Count; i++)
            {
                if (simpleColors[i] != lastSimpleColors[i])
                {
                    EditColor(i, simpleColors[i]);
                }
            }
        }

        void DrawColorGroup(int index)
        {
            EditorGUILayout.BeginHorizontal();

            Rect swatchRect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.Width(18f), GUILayout.Height(18f));
            EditorGUI.DrawRect(swatchRect, simpleColors[index]);

            EditorGUILayout.LabelField(ColorGroupNames[index], EditorStyles.boldLabel, GUILayout.MinWidth(120f));
            EditorGUILayout.LabelField(ThemesUtility.GetColorListByInt(index).Count + " selectors", EditorStyles.miniLabel, GUILayout.Width(82f));

            EditorGUI.BeginChangeCheck();
            Color newColor = EditorGUILayout.ColorField(simpleColors[index], GUILayout.MinWidth(140f));
            if (EditorGUI.EndChangeCheck())
            {
                simpleColors[index] = newColor;
            }

            EditorGUILayout.EndHorizontal();
            DrawDivider();
        }

        void DrawAdvancedView()
        {
            DrawSectionHeader("Selectors");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            selectorSearch = EditorGUILayout.TextField(selectorSearch, EditorStyles.toolbarSearchField);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(GetSelectorCountText(), EditorStyles.miniLabel, GUILayout.Width(112f));
            EditorGUILayout.EndHorizontal();

            if (ct.Items == null)
            {
                ct.Items = new List<CustomTheme.UIItem>();
            }

            List<CustomTheme.UIItem> itemsClone = new List<CustomTheme.UIItem>(ct.Items);
            int visibleCount = 0;
            foreach (CustomTheme.UIItem item in itemsClone)
            {
                if (!MatchesSearch(item))
                {
                    continue;
                }

                visibleCount++;
                DrawSelectorItem(item);
            }

            EditorGUILayout.EndVertical();

            if (visibleCount == 0)
            {
                EditorGUILayout.HelpBox("No selectors match the current search.", MessageType.Info);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Add selector", GUILayout.Width(120f)))
            {
                CustomTheme.UIItem item = new CustomTheme.UIItem();
                item.Name = "NewSelector";
                item.Color = simpleColors.Count > 0 ? simpleColors[0] : Color.gray;
                ct.Items.Add(item);
            }

            using (new EditorGUI.DisabledScope(ct.Items.Count == 0))
            {
                if (GUILayout.Button("Remove last", GUILayout.Width(120f)))
                {
                    ct.Items.RemoveAt(ct.Items.Count - 1);
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        void DrawSelectorItem(CustomTheme.UIItem item)
        {
            EditorGUILayout.BeginHorizontal();
            item.Name = EditorGUILayout.TextField(item.Name);

            if (GUILayout.Button("Delete", GUILayout.Width(64f)))
            {
                ct.Items.Remove(item);
                EditorGUILayout.EndHorizontal();
                return;
            }

            item.Color = EditorGUILayout.ColorField(item.Color, GUILayout.Width(160f));
            EditorGUILayout.EndHorizontal();
            DrawDivider();
        }

        void DrawFooter()
        {
            EditorGUILayout.Space(6f);
            DrawSectionHeader("Actions");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Regenerate selectors", GUILayout.Width(148f)))
            {
                RegenerateTheme();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Clone", GUILayout.Width(92f)))
            {
                CloneTheme();
            }

            if (GUILayout.Button("Export", GUILayout.Width(92f)))
            {
                ExportTheme();
            }

            if (GUILayout.Button("Save", GUILayout.Width(92f)))
            {
                SaveTheme();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        void DrawSectionHeader(string title)
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.largeLabel);
            EditorGUILayout.EndHorizontal();
            DrawDivider();
        }

        void DrawDivider()
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 1f);
            EditorGUI.DrawRect(rect, new Color(0.24f, 0.24f, 0.24f, 0.35f));
        }

        string GetSelectorCountText()
        {
            if (ct.Items == null)
            {
                return "0 selectors";
            }

            return ct.Items.Count + " selectors";
        }

        void HandleRegenerateShortcut()
        {
            bool regenerate = false;
            Event e = Event.current;

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.R)
                {
                    rHold = true;
                }

                if (e.keyCode == KeyCode.LeftControl || e.keyCode == KeyCode.RightControl)
                {
                    ctrlHold = true;
                }
            }

            if (e.type == EventType.KeyUp)
            {
                if (e.keyCode == KeyCode.R)
                {
                    rHold = false;
                }

                if (e.keyCode == KeyCode.LeftControl || e.keyCode == KeyCode.RightControl)
                {
                    ctrlHold = false;
                }
            }

            if (rHold && ctrlHold)
            {
                regenerate = true;
                rHold = false;
                ctrlHold = false;
                e.Use();
            }

            if (regenerate)
            {
                RegenerateTheme();
            }
        }

        void RegenerateTheme()
        {
            if (!EditorUtility.DisplayDialog(
                    "Regenerate selectors?",
                    "This rebuilds the selector list from the current palette. Clone the theme first if you want to keep all hand-tuned selector values.",
                    "Regenerate",
                    "Cancel"))
            {
                return;
            }

            ct.Items = new List<CustomTheme.UIItem>();
            for (int i = 0; i < simpleColors.Count; i++)
            {
                foreach (string selectorName in ThemesUtility.GetColorListByInt(i))
                {
                    CustomTheme.UIItem item = new CustomTheme.UIItem();
                    item.Name = selectorName;
                    item.Color = simpleColors[i];
                    ct.Items.Add(item);
                }
            }

            ResetSimpleColorsFromTheme();
        }

        void SaveTheme()
        {
            string trimmedName = themeName.Trim();
            if (string.IsNullOrEmpty(trimmedName))
            {
                EditorUtility.DisplayDialog("Theme name required", "Enter a name before saving this theme.", "OK");
                return;
            }

            if (ct.Name != trimmedName)
            {
                ThemesUtility.DeleteFileWithMeta(ThemesUtility.GetPathForTheme(ct.Name));
            }

            ct.Name = trimmedName;
            ThemesUtility.SaveJsonFileForTheme(ct);
            themeName = ct.Name;
        }

        void CloneTheme()
        {
            string trimmedName = themeName.Trim();
            if (string.IsNullOrEmpty(trimmedName))
            {
                trimmedName = ct.Name;
            }

            ct.Name = trimmedName + " - copy";
            themeName = ct.Name;
            ThemesUtility.SaveJsonFileForTheme(ct);
        }

        void ExportTheme()
        {
            string trimmedName = themeName.Trim();
            if (string.IsNullOrEmpty(trimmedName))
            {
                EditorUtility.DisplayDialog("Theme name required", "Enter a name before exporting this theme.", "OK");
                return;
            }

            ct.Name = trimmedName;
            ThemesUtility.NormalizeTheme(ct);
            ct.Version = ThemesUtility.Version;

            string path = EditorUtility.SaveFilePanel("Export Theme", "", GetSafeFileName(trimmedName), "json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (!string.Equals(Path.GetExtension(path), ThemesUtility.Enc, System.StringComparison.OrdinalIgnoreCase))
            {
                path += ThemesUtility.Enc;
            }

            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(ct));
            }
            catch (System.Exception exception)
            {
                EditorUtility.DisplayDialog("Export failed", exception.Message, "OK");
            }
        }

        string GetSafeFileName(string fileName)
        {
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar.ToString(), "");
            }

            return string.IsNullOrEmpty(fileName) ? "Theme" : fileName;
        }

        bool MatchesSearch(CustomTheme.UIItem item)
        {
            string query = selectorSearch.Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(query) || (item.Name != null && item.Name.ToLowerInvariant().Contains(query));
        }

        CustomTheme.UIItem GetItemByName(string itemName)
        {
            if (ct.Items == null)
            {
                return null;
            }

            foreach (CustomTheme.UIItem item in ct.Items)
            {
                if (item != null && item.Name == itemName)
                {
                    return item;
                }
            }

            return null;
        }

        void ResetSimpleColorsFromTheme()
        {
            simpleColors.Clear();
            lastSimpleColors.Clear();

            simpleColors.AddRange(CreateAverageColors());
            lastSimpleColors.AddRange(simpleColors);
        }

        List<Color> CreateAverageColors()
        {
            List<Color> colors = new List<Color>();

            for (int i = 0; i < 6; i++)
            {
                List<string> colorObjects = ThemesUtility.GetColorListByInt(i);
                List<Color> allColors = new List<Color>();

                foreach (string itemName in colorObjects)
                {
                    CustomTheme.UIItem item = GetItemByName(itemName);
                    if (item != null)
                    {
                        allColors.Add(item.Color);
                    }
                }

                colors.Add(allColors.Count > 0 ? GetAverage(allColors) : ThemesUtility.HtmlToRgb("#9A7B6E"));
            }

            return colors;
        }

        void EditColor(int index, Color newColor)
        {
            List<string> edit = ThemesUtility.GetColorListByInt(index);

            foreach (string itemName in edit)
            {
                CustomTheme.UIItem item = GetItemByName(itemName);
                if (item != null)
                {
                    item.Color = newColor;
                }
            }

            lastSimpleColors[index] = simpleColors[index];
        }

        Color GetAverage(List<Color> colors)
        {
            float r = 0f;
            float g = 0f;
            float b = 0f;
            float a = 0f;
            int count = colors.Count;

            foreach (Color color in colors)
            {
                r += color.r;
                g += color.g;
                b += color.b;
                a += color.a;
            }

            return new Color(r / count, g / count, b / count, a / count);
        }
    }
}
