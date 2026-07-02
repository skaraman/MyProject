using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Globalization;
using UnityEditorInternal;
using UnityEditor;
namespace ThemesPlugin
{ 
    public static class ThemesUtility
    {
        readonly public static string CustomThemesPath = Application.dataPath + "/EditorThemes/Editor/Themes/";
        readonly public static string UssFilePath = Application.dataPath + "/EditorThemes/Editor/StyleSheets/Extensions/";
        readonly public static string PresetsPath = Application.dataPath + "/EditorThemes/Editor/CreatePresets/";
        readonly public static string Version = "v0.67";
        readonly public static string Enc = ".json";
        readonly public static string DefaultThemeName = "Default";
        readonly public static string LegacyDefaultThemeName = "_deafault";
        const string CurrentThemeEditorPrefsKey = "EditorThemesPlugin.CurrentThemePath";

        public static string currentTheme;
        

        public static Color HtmlToRgb(string s)
        {
            Color c = Color.black;
            ColorUtility.TryParseHtmlString(s, out c);
            return c;
        }

        public static void OpenEditTheme(CustomTheme ct)
        {
            EditThemeWindow window = (EditThemeWindow)EditorWindow.GetWindow(typeof(EditThemeWindow), false, "Edit Theme");
            window.SetTheme(ct);
            window.Show();
        }
        public static CustomTheme GetCustomThemeFromJson(string Path)
        {
            string json = File.ReadAllText(Path);

            CustomTheme theme = JsonUtility.FromJson<CustomTheme>(json);
            NormalizeTheme(theme);
            return theme;
        }

        public static string GetPathForTheme(string Name)
        {
            if (Name == DefaultThemeName)
            {
                string defaultPath = CustomThemesPath + DefaultThemeName + Enc;
                if (File.Exists(defaultPath))
                {
                    return defaultPath;
                }

                return CustomThemesPath + LegacyDefaultThemeName + Enc;
            }

            return CustomThemesPath + Name + Enc;
        }

        public static string GetDisplayNameForThemeName(string Name)
        {
            if (Name == LegacyDefaultThemeName)
            {
                return DefaultThemeName;
            }

            return Name;
        }

        public static string GetDisplayNameForThemePath(string Path)
        {
            if (string.IsNullOrEmpty(Path))
            {
                return "None";
            }

            return GetDisplayNameForThemeName(System.IO.Path.GetFileNameWithoutExtension(Path));
        }
        public static void DeleteFileWithMeta(string Path)
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
                File.Delete(Path + ".meta");
            }
            else Debug.LogWarning("Path: " + Path + " does not exist");
            
        }

        public static string GenerateUssString(CustomTheme c)
        {
            string ussText = "";
            ussText += "/* ========== Editor Themes Plugin ==========*/";
            ussText += "\n";
            ussText += "/*            Auto Generated Code            */";
            ussText += "\n";
            ussText += "/*@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@*/";
            ussText += "\n";
            ussText += "/*"+ Version + "*/";

            if (c.Items == null)
            {
                return ussText;
            }

            foreach (CustomTheme.UIItem I in c.Items)
            {
                if (I == null)
                {
                    continue;
                }

                ussText += UssBlock(I.Name, I.Color);
                if (IsButtonOrDropdownSelector(I.Name) && !HasPseudoState(I.Name))
                {
                    ussText += UssControlPseudoStateBlocks(I.Name, I.Color, 0.14f, 0.16f);
                }
            }

            ussText += GenerateSupplementalControlUss(c);

            return ussText;
        }

        static readonly HashSet<string> RawUssSelectors = new HashSet<string>
        {
            "Button",
            "ToolbarButton",
            "DropdownField",
            "PopupField",
            "EnumField",
            "ObjectField"
        };

        static readonly string[] SupplementalButtonSelectors =
        {
            "Button",
            ".unity-button"
        };

        static readonly string[] SupplementalDropdownSelectors =
        {
            ".unity-base-popup-field",
            ".unity-base-popup-field__input",
            ".unity-popup-field",
            ".unity-popup-field__input",
            ".unity-dropdown-field",
            ".unity-dropdown-field__input",
            ".unity-enum-field",
            ".unity-enum-field__input",
            ".unity-object-field",
            ".unity-object-field__input",
            ".unity-object-field__selector",
            ".unity-base-field__input",
            ".unity-base-popup-field .unity-base-field__input",
            ".unity-property-field .unity-base-popup-field__input",
            ".unity-property-field .unity-enum-field__input",
            ".unity-property-field .unity-object-field__input"
        };


        

        public static string UssBlock(string Name, Color Color)
        {
            if (Name == null)
            {
                return "";
            }

            string selectorName = Name.Trim();
            if (string.IsNullOrEmpty(selectorName))
            {
                return "";
            }

            Color32 color32 = Color;
            //Debug.Log(color32);
            string a = Color.a.ToString(CultureInfo.InvariantCulture);

            string Colors = "rgba(" + color32.r + ", " + color32.g + ", " + color32.b + ", " + a + ")";// Generate colors for later

            string s = "\n" + "\n";//add two empty lines

            string selector = GetUssSelector(selectorName);
            s += selector + "\n";//add name
            s += "{" + "\n" + "\t" + "background-color: " + Colors + ";" + "\n";

            if (IsButtonOrDropdownSelector(selectorName))
            {
                s += "\t" + "-unity-background-image-tint-color: " + Colors + ";" + "\n";
                s += "\t" + "border-top-color: " + Colors + ";" + "\n";
                s += "\t" + "border-right-color: " + Colors + ";" + "\n";
                s += "\t" + "border-bottom-color: " + Colors + ";" + "\n";
                s += "\t" + "border-left-color: " + Colors + ";" + "\n";
            }

            s += "}";//add color

            return s;
        }

        static string GenerateSupplementalControlUss(CustomTheme theme)
        {
            Color buttonColor = GetThemeColor(theme, 4, new Color(0.35f, 0.35f, 0.35f, 1f));
            Color dropdownColor = GetNamedColor(theme, "DropDown", GetNamedColor(theme, "MiniPopup", buttonColor));
            string uss = "";

            uss += "\n\n/* Supplemental UI Toolkit controls */";
            foreach (string selector in SupplementalButtonSelectors)
            {
                uss += UssControlStateBlocks(selector, buttonColor, 0.16f, 0.18f);
            }

            foreach (string selector in SupplementalDropdownSelectors)
            {
                uss += UssControlStateBlocks(selector, dropdownColor, 0.14f, 0.16f);
            }

            return uss;
        }

        static string UssControlStateBlocks(string selector, Color normalColor, float hoverAmount, float activeAmount)
        {
            string uss = UssBlock(selector, normalColor);
            uss += UssControlPseudoStateBlocks(selector, normalColor, hoverAmount, activeAmount);
            return uss;
        }

        static string UssControlPseudoStateBlocks(string selector, Color normalColor, float hoverAmount, float activeAmount)
        {
            string uss = "";
            uss += UssBlock(selector + ":hover", Lighten(normalColor, hoverAmount));
            uss += UssBlock(selector + ":focus", Lighten(normalColor, hoverAmount));
            uss += UssBlock(selector + ":active", Darken(normalColor, activeAmount));
            uss += UssBlock(selector + ":checked", Darken(normalColor, activeAmount));
            return uss;
        }

        static string GetUssSelector(string selectorName)
        {
            if (selectorName.StartsWith(".") || selectorName.StartsWith("#") || RawUssSelectors.Contains(selectorName))
            {
                return selectorName;
            }

            return "." + selectorName;
        }

        static bool IsButtonOrDropdownSelector(string selectorName)
        {
            string lowerName = selectorName.ToLowerInvariant();
            return lowerName.Contains("button")
                || lowerName.Contains("dropdown")
                || lowerName.Contains("popup")
                || lowerName == "button"
                || lowerName == "dropdown"
                || lowerName.Contains("dropdownfield")
                || lowerName.Contains("popupfield")
                || lowerName.Contains("enumfield")
                || lowerName.Contains("objectfield")
                || lowerName.Contains("unity-base-field")
                || lowerName.Contains("unity-base-popup-field")
                || lowerName.Contains("unity-popup-field")
                || lowerName.Contains("unity-dropdown-field")
                || lowerName.Contains("unity-enum-field")
                || lowerName.Contains("unity-object-field");
        }

        static bool HasPseudoState(string selectorName)
        {
            return selectorName.Contains(":");
        }

        static Color GetThemeColor(CustomTheme theme, int groupIndex, Color fallback)
        {
            List<string> itemNames = GetColorListByInt(groupIndex);
            List<Color> colors = new List<Color>();

            foreach (string itemName in itemNames)
            {
                CustomTheme.UIItem item = GetItemByName(theme, itemName);
                if (item != null)
                {
                    colors.Add(item.Color);
                }
            }

            if (colors.Count == 0)
            {
                return fallback;
            }

            return Average(colors);
        }

        static Color GetNamedColor(CustomTheme theme, string itemName, Color fallback)
        {
            CustomTheme.UIItem item = GetItemByName(theme, itemName);
            return item != null ? item.Color : fallback;
        }

        static CustomTheme.UIItem GetItemByName(CustomTheme theme, string itemName)
        {
            if (theme == null || theme.Items == null)
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

        static Color Average(List<Color> colors)
        {
            float r = 0f;
            float g = 0f;
            float b = 0f;
            float a = 0f;

            foreach (Color color in colors)
            {
                r += color.r;
                g += color.g;
                b += color.b;
                a += color.a;
            }

            return new Color(r / colors.Count, g / colors.Count, b / colors.Count, a / colors.Count);
        }

        static Color Lighten(Color color, float amount)
        {
            return new Color(
                Mathf.Clamp01(color.r + amount),
                Mathf.Clamp01(color.g + amount),
                Mathf.Clamp01(color.b + amount),
                color.a);
        }

        static Color Darken(Color color, float amount)
        {
            return new Color(
                Mathf.Clamp01(color.r - amount),
                Mathf.Clamp01(color.g - amount),
                Mathf.Clamp01(color.b - amount),
                color.a);
        }

        public static void SaveJsonFileForTheme(CustomTheme t)
        {

            NormalizeTheme(t);
            t.Version = Version;
            string NewJson = JsonUtility.ToJson(t);


            string Path = GetPathForTheme(t.Name);
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }

            File.WriteAllText(Path, NewJson);
            LoadUssFileForTheme(t.Name);

        }
        public static void LoadUssFileForTheme(string Name)
        {
            LoadUssFileForThemeUsingPath(ThemesUtility.GetPathForTheme(Name));
        }
        public static void LoadUssFileForThemeUsingPath(string Path)
        {

            CustomTheme t = ThemesUtility.GetCustomThemeFromJson(Path);

            if ((EditorGUIUtility.isProSkin && t.unityTheme == CustomTheme.UnityTheme.Light) || (!EditorGUIUtility.isProSkin && t.unityTheme == CustomTheme.UnityTheme.Dark))
            {
                InternalEditorUtility.SwitchSkinAndRepaintAllViews();

            }

            string ussText = ThemesUtility.GenerateUssString(t);
            WriteUss(ussText);
            EditorThemeImguiStyleApplicator.ApplyTheme(t);

            currentTheme = Path;
            EditorPrefs.SetString(CurrentThemeEditorPrefsKey, Path);
        }

        public static void LoadLastTheme()
        {
            string path = EditorPrefs.GetString(CurrentThemeEditorPrefsKey, "");
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                LoadUssFileForThemeUsingPath(path);
            }
        }

        public static void NormalizeTheme(CustomTheme theme)
        {
            if (theme == null || theme.Items == null)
            {
                return;
            }

            foreach (CustomTheme.UIItem item in theme.Items)
            {
                if (item != null && item.Name != null)
                {
                    item.Name = item.Name.Trim();
                }
            }
        }


        public static void WriteUss(string ussText)
        {
            string Path = UssFilePath + "/dark.uss";
            DeleteFileWithMeta(Path);

            File.WriteAllText(Path, ussText);


            string Path2 = Application.dataPath + "/EditorThemes/Editor/StyleSheets/Extensions/light.uss";
            DeleteFileWithMeta(Path2);
            
            File.WriteAllText(Path2, ussText);


            AssetDatabase.Refresh();

        }


        public static List<string> GetColorListByInt(int i)
        {
            List<string> colorList = new List<string>();


            switch (i)
            {
                case 0://base
                    colorList.Add("TabWindowBackground");
                    colorList.Add("ScrollViewAlt");
                    colorList.Add("label");
                    colorList.Add("ProjectBrowserTopBarBg");
                    colorList.Add("ProjectBrowserBottomBarBg");
                    break;
                case 1://accent
                    colorList.Add("dockHeader");
                    colorList.Add("TV LineBold");
                    colorList.Add("IN Title");

                    break;
                case 2://secondary
                    colorList.Add("ToolbarDropDownToogleRight");
                    colorList.Add("ToolbarPopupLeft");
                    colorList.Add("ToolbarPopup");
                    colorList.Add("toolbarbutton");
                    colorList.Add("PreToolbar");
                    colorList.Add("AppToolbar");
                    colorList.Add("GameViewBackground");
                    colorList.Add("CN EntryInfoSmall");
                    colorList.Add("Toolbar");
                    colorList.Add("toolbarbutton");
                    colorList.Add("toolbarbuttonRight");

                    colorList.Add("ProjectBrowserIconAreaBg");

                    //colorList.Add("dragTab");//this is the currently clicked tab and has to be a different color than the other tabs
                    break;
                case 3://Tab
                    //colorList.Add("dragtab first");
                    colorList.Add("dragtab-label");//changing this color has overriten dragTab and dragtab first so removed
                    break;
                case 4://button

                    colorList.Add("AppCommandLeft");
                    colorList.Add("AppCommandMid");
                    colorList.Add("AppCommand");
                    colorList.Add("AppToolbarButtonLeft");
                    colorList.Add("AppToolbarButtonRight");
                    colorList.Add("DropDown");
                    colorList.Add("Button");
                    colorList.Add(".unity-button");
                    colorList.Add(".unity-base-popup-field__input");
                    colorList.Add(".unity-popup-field__input");
                    colorList.Add(".unity-enum-field__input");
                    colorList.Add(".unity-object-field__input");
                    colorList.Add(".unity-object-field__selector");
                    break;
                case 5:
                    colorList.Add("SceneTopBarBg");
                    colorList.Add("MiniPopup");
                    colorList.Add("TV Selection");
                    colorList.Add("ExposablePopupMenu");
                    colorList.Add("minibutton");
                    colorList.Add("ToolbarSearchTextField");
                    colorList.Add(".unity-base-field__input");
                    colorList.Add(".unity-search-field__search-button");
                    colorList.Add(".unity-search-field__cancel-button");
                    break;


            }
            return colorList;

        }


    }

}
