using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEditorInternal;
using UnityEditor;
namespace ThemesPlugin
{ 
    public static class ThemesUtility
    {
        readonly public static string CustomThemesPath = Application.dataPath + "/EditorThemes/Editor/Themes/";
        readonly public static string UssFilePath = Application.dataPath + "/EditorThemes/Editor/StyleSheets/Extensions/";
        readonly public static string PresetsPath = Application.dataPath + "/EditorThemes/Editor/CreatePresets/";
        readonly public static string Version = "v0.65";
        readonly public static string Enc = ".json";

        public static string currentTheme;
        

        public static Color HtmlToRgb(string s)
        {
            Color c = Color.black;
            ColorUtility.TryParseHtmlString(s, out c);
            return c;
        }

        public static void OpenEditTheme(CustomTheme ct)
        {
            EditThemeWindow.ct = ct;
            EditThemeWindow window = (EditThemeWindow)EditorWindow.GetWindow(typeof(EditThemeWindow), false, "Edit Theme");
           
            window.Show();
        }
        public static CustomTheme GetCustomThemeFromJson(string Path)
        {
            string json = File.ReadAllText(Path);
            
            return JsonUtility.FromJson<CustomTheme>(json);
        }

        public static string GetPathForTheme(string Name)
        {
            return CustomThemesPath + Name + Enc;
        }
        public static void DeleteFileWithMeta(string Path)
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
                File.Delete(Path + ".meta");
            }
            else Debug.LogWarning("Path: " + Path + " does not exsit");
            
        }

        public static string GenerateUssString(CustomTheme c)
        {
            c.Items = SanitizeThemeItems(c.Items, c.Name);

            string ussText = "";
            ussText += "/* ========== Editor Themes Plugin ==========*/";
            ussText += "\n";
            ussText += "/*            Auto Generated Code            */";
            ussText += "\n";
            ussText += "/*@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@*/";
            ussText += "\n";
            ussText += "/*"+ Version + "*/";

            foreach (CustomTheme.UIItem I in c.Items)
            {
                ussText += UssBlock(I.Name, I.Color);
            }

            return ussText;
        }

        static List<CustomTheme.UIItem> SanitizeThemeItems(List<CustomTheme.UIItem> items, string themeName)
        {
            List<CustomTheme.UIItem> sanitizedItems = new List<CustomTheme.UIItem>();
            int removedCount = 0;

            if (items == null)
            {
                return sanitizedItems;
            }

            foreach (CustomTheme.UIItem item in items)
            {
                if (item == null || IsUnsupportedSelectorName(item.Name))
                {
                    removedCount++;
                    continue;
                }

                item.Name = item.Name.Trim();
                sanitizedItems.Add(item);
            }

            if (removedCount > 0)
            {
                Debug.Log("[EditorThemes] Removed unsupported selector entries from theme '" + themeName + "'. removed_count=" + removedCount);
            }

            return sanitizedItems;
        }

        static bool IsUnsupportedSelectorName(string selectorName)
        {
            if (string.IsNullOrWhiteSpace(selectorName))
            {
                return true;
            }

            return selectorName.Trim() == "ToolbarSearchTextField";
        }

        public static string UssBlock(string Name, Color Color)
        {
            Name = Name.Trim();
            Color32 color32 = Color;
            //Debug.Log(color32);
            string a = Color.a + "";
            a = a.Replace(",", ".");

            string Colors = "rgba(" + color32.r + ", " + color32.g + ", " + color32.b + ", " + a + ")";// Generate colors for later

            string s = "\n" + "\n";//add two empty lines

            s += "." + Name + "\n";//add name
            s += "{" + "\n" + "\t" + "background-color: " + Colors + ";" + "\n" + "}";//add color

            return s;
        }

        public static void SaveJsonFileForTheme(CustomTheme t)
        {
            t.Items = SanitizeThemeItems(t.Items, t.Name);

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

            currentTheme = Path;
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

                    break;
                case 2://secondery
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

                    //colorList.Add("dragTab");//this is the currently clicked tab  has to be a diffrent color than the other tabs
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
                    break;
                case 5:
                    colorList.Add("SceneTopBarBg");
                    colorList.Add("MiniPopup");
                    colorList.Add("TV Selection");
                    colorList.Add("ExposablePopupMenu");
                    colorList.Add("minibutton");
                    break;


            }
            return colorList;

        }


    }

}
