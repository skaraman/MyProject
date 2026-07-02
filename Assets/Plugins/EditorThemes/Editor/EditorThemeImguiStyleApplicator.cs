using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace ThemesPlugin
{
    [InitializeOnLoad]
    static class EditorThemeImguiStyleApplicator
    {
        static readonly string[] ExplicitButtonStyleNames =
        {
            "button",
            "PR DropHere",
            "AC ComponentButton",
            "CN EntryBackEven",
            "CN EntryBackOdd"
        };

        static readonly string[] ProvenFlatButtonStyleNames =
        {
            "minibuttonleft",
            "minibuttonmid",
            "minibuttonright",
            "AC Button"
        };

        static readonly string[] ExplicitDropdownStyleNames =
        {
            "DropDownButton",
            "MiniPullDown",
            "MiniPopup",
            "ObjectFieldButton",
            "Popup"
        };

        static readonly Dictionary<GUIStyleState, Snapshot> Snapshots = new Dictionary<GUIStyleState, Snapshot>();
        static readonly List<Texture2D> CreatedTextures = new List<Texture2D>();
        static readonly Dictionary<string, Texture2D> TintedTextures = new Dictionary<string, Texture2D>();
        static readonly HashSet<GUIStyle> AppliedStyles = new HashSet<GUIStyle>();

        static Color buttonColor = new Color(0.35f, 0.35f, 0.35f, 1f);
        static Color dropdownColor = new Color(0.35f, 0.35f, 0.35f, 1f);
        static Color componentHeaderColor = new Color(0.28f, 0.28f, 0.28f, 1f);
        static Color textColor = Color.white;
        static bool hasTheme;

        static EditorThemeImguiStyleApplicator()
        {
            Editor.finishedDefaultHeaderGUI += OnInspectorHeaderGUI;
        }

        public static void ApplyTheme(CustomTheme theme)
        {
            RestoreSnapshots();

            if (theme == null)
            {
                hasTheme = false;
                return;
            }

            buttonColor = GetThemeColor(theme, 4, new Color(0.35f, 0.35f, 0.35f, 1f));
            dropdownColor = GetNamedColor(theme, "DropDown", GetNamedColor(theme, "MiniPopup", buttonColor));
            componentHeaderColor = GetNamedColor(theme, "IN Title", GetThemeColor(theme, 1, Lighten(buttonColor, 0.08f)));
            textColor = GetReadableTextColor(buttonColor);
            hasTheme = true;
            InternalEditorUtility.RepaintAllViews();
        }

        public static void EnsureAppliedFromOnGUI()
        {
            if (!hasTheme || Event.current == null)
            {
                return;
            }

            ApplyNow();
        }

        static void OnInspectorHeaderGUI(Editor editor)
        {
            EnsureAppliedFromOnGUI();
        }

        static void ApplyNow()
        {
            AppliedStyles.Clear();

            foreach (GUIStyle style in GetProvenFlatButtonStyles())
            {
                ReplaceStyleBackground(style, buttonColor);
            }

            foreach (GUIStyle style in GetComponentHeaderStyles())
            {
                ReplaceStyleBackground(style, componentHeaderColor);
            }

            foreach (GUIStyle style in GetButtonStyles())
            {
                TintStyle(style, buttonColor, true);
            }

            foreach (GUIStyle style in GetDropdownStyles())
            {
                TintStyle(style, dropdownColor, true);
            }
        }

        static IEnumerable<GUIStyle> GetButtonStyles()
        {
            foreach (GUIStyle style in YieldIfNotNull(GUI.skin != null ? GUI.skin.button : null))
            {
                yield return style;
            }

            foreach (GUIStyle style in YieldIfNotNull(GetEditorStyleSafely(() => EditorStyles.miniButton)))
            {
                yield return style;
            }

            foreach (GUIStyle style in YieldIfNotNull(GetEditorStyleSafely(() => EditorStyles.miniButtonLeft)))
            {
                yield return style;
            }

            foreach (GUIStyle style in YieldIfNotNull(GetEditorStyleSafely(() => EditorStyles.miniButtonMid)))
            {
                yield return style;
            }

            foreach (GUIStyle style in YieldIfNotNull(GetEditorStyleSafely(() => EditorStyles.miniButtonRight)))
            {
                yield return style;
            }

            foreach (GUIStyle style in FindNamedStyles(ExplicitButtonStyleNames))
            {
                yield return style;
            }

            foreach (GUIStyle style in GetActionButtonStyles())
            {
                yield return style;
            }

        }

        static IEnumerable<GUIStyle> GetDropdownStyles()
        {
            foreach (GUIStyle style in YieldIfNotNull(GetEditorStyleSafely(() => EditorStyles.popup)))
            {
                yield return style;
            }

            foreach (GUIStyle style in FindNamedStyles(ExplicitDropdownStyleNames))
            {
                yield return style;
            }

            foreach (GUIStyle style in GetCustomStyles())
            {
                string name = style.name ?? "";
                if (IsDropdownStyleName(name) && !IsTransparentIconStyleName(name))
                {
                    yield return style;
                }
            }

        }

        static IEnumerable<GUIStyle> GetProvenFlatButtonStyles()
        {
            foreach (GUIStyle style in FindNamedStyles(ProvenFlatButtonStyleNames))
            {
                yield return style;
            }

            foreach (GUIStyle style in GetCustomStyles())
            {
                if (IsProvenFlatButtonStyleName(style.name ?? ""))
                {
                    yield return style;
                }
            }
        }

        static IEnumerable<GUIStyle> GetActionButtonStyles()
        {
            foreach (GUIStyle style in GetCustomStyles())
            {
                string name = style.name ?? "";
                if ((IsExplicitButtonStyleName(name) || IsActionButtonStyleName(name)) && !IsTransparentIconStyleName(name))
                {
                    yield return style;
                }
            }
        }

        static IEnumerable<GUIStyle> GetComponentHeaderStyles()
        {
            foreach (GUIStyle style in FindNamedStyles(new[] { "IN Title" }))
            {
                yield return style;
            }

            foreach (GUIStyle style in GetCustomStyles())
            {
                if (string.Equals(style.name, "IN Title", StringComparison.OrdinalIgnoreCase))
                {
                    yield return style;
                }
            }
        }

        static IEnumerable<GUIStyle> YieldIfNotNull(GUIStyle style)
        {
            if (style != null)
            {
                yield return style;
            }
        }

        static IEnumerable<GUIStyle> FindNamedStyles(string[] names)
        {
            if (GUI.skin == null)
            {
                yield break;
            }

            foreach (string styleName in names)
            {
                GUIStyle style = FindStyleSafely(styleName);
                if (style != null)
                {
                    yield return style;
                }
            }
        }

        static GUIStyle FindStyleSafely(string styleName)
        {
            try
            {
                return GUI.skin.FindStyle(styleName);
            }
            catch
            {
                return null;
            }
        }

        static GUIStyle GetEditorStyleSafely(Func<GUIStyle> getStyle)
        {
            try
            {
                return getStyle();
            }
            catch
            {
                return null;
            }
        }

        static IEnumerable<GUIStyle> GetCustomStyles()
        {
            if (GUI.skin == null || GUI.skin.customStyles == null)
            {
                yield break;
            }

            foreach (GUIStyle style in GUI.skin.customStyles)
            {
                if (style != null)
                {
                    yield return style;
                }
            }
        }

        static bool IsActionButtonStyleName(string name)
        {
            return name.IndexOf("add component", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("addcomponent", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("componentbutton", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("add override", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("addoverride", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("override", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsExplicitButtonStyleName(string name)
        {
            foreach (string styleName in ExplicitButtonStyleNames)
            {
                if (string.Equals(name, styleName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsProvenFlatButtonStyleName(string name)
        {
            foreach (string styleName in ProvenFlatButtonStyleNames)
            {
                if (string.Equals(name, styleName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsDropdownStyleName(string name)
        {
            return name.IndexOf("drop", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("dropdown", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("popup", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("pulldown", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsTransparentIconStyleName(string name)
        {
            return name.IndexOf("toolbar", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("invisible", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("paneoptions", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ol plus", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ol minus", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("treeview", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void TintStyle(GUIStyle style, Color normalColor, bool allowFallbackBackground)
        {
            if (style == null || AppliedStyles.Contains(style))
            {
                return;
            }

            AppliedStyles.Add(style);
            Color hoverColor = Lighten(normalColor, 0.16f);
            Color activeColor = Darken(normalColor, 0.18f);

            TintState(style.normal, normalColor, allowFallbackBackground);
            TintState(style.hover, hoverColor, allowFallbackBackground);
            TintState(style.active, activeColor, allowFallbackBackground);
            TintState(style.focused, hoverColor, allowFallbackBackground);
            TintState(style.onNormal, normalColor, allowFallbackBackground);
            TintState(style.onHover, hoverColor, allowFallbackBackground);
            TintState(style.onActive, activeColor, allowFallbackBackground);
            TintState(style.onFocused, hoverColor, allowFallbackBackground);
        }

        static void ReplaceStyleBackground(GUIStyle style, Color normalColor)
        {
            if (style == null || AppliedStyles.Contains(style))
            {
                return;
            }

            AppliedStyles.Add(style);
            Color hoverColor = Lighten(normalColor, 0.16f);
            Color activeColor = Darken(normalColor, 0.18f);

            ReplaceStateBackground(style.normal, normalColor);
            ReplaceStateBackground(style.hover, hoverColor);
            ReplaceStateBackground(style.active, activeColor);
            ReplaceStateBackground(style.focused, hoverColor);
            ReplaceStateBackground(style.onNormal, normalColor);
            ReplaceStateBackground(style.onHover, hoverColor);
            ReplaceStateBackground(style.onActive, activeColor);
            ReplaceStateBackground(style.onFocused, hoverColor);
        }

        static void ReplaceStateBackground(GUIStyleState state, Color color)
        {
            if (state == null)
            {
                return;
            }

            if (!Snapshots.ContainsKey(state))
            {
                Snapshots.Add(state, new Snapshot(state.background, state.textColor));
            }

            state.background = MakeFlatTexture(color);
            state.textColor = textColor;
        }

        static void TintState(GUIStyleState state, Color color, bool allowFallbackBackground)
        {
            if (state == null)
            {
                return;
            }

            Snapshot snapshot;
            if (!Snapshots.TryGetValue(state, out snapshot))
            {
                snapshot = new Snapshot(state.background, state.textColor);
                Snapshots.Add(state, snapshot);
            }

            Texture2D source = snapshot.Background;
            if (source == null && allowFallbackBackground)
            {
                source = GetFallbackButtonBackground();
            }

            if (source != null)
            {
                state.background = GetTintedTexture(source, color);
            }

            state.textColor = textColor;
        }

        static Texture2D GetFallbackButtonBackground()
        {
            if (GUI.skin != null && GUI.skin.button != null && GUI.skin.button.normal != null)
            {
                return GUI.skin.button.normal.background;
            }

            return null;
        }

        static Texture2D GetTintedTexture(Texture2D source, Color color)
        {
            string key = RuntimeHelpers.GetHashCode(source) + ":" + ColorUtility.ToHtmlStringRGBA(color);
            Texture2D existing;
            if (TintedTextures.TryGetValue(key, out existing) && existing != null)
            {
                return existing;
            }

            Texture2D sourceCopy = CopyTexture(source);
            Color[] pixels = sourceCopy.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color sourcePixel = pixels[i];
                float luminance = (sourcePixel.r + sourcePixel.g + sourcePixel.b) / 3f;
                Color shadedColor = Color.Lerp(Darken(color, 0.22f), Lighten(color, 0.22f), luminance);
                pixels[i] = new Color(shadedColor.r, shadedColor.g, shadedColor.b, sourcePixel.a * color.a);
            }

            sourceCopy.SetPixels(pixels);
            sourceCopy.Apply();
            TintedTextures[key] = sourceCopy;
            return sourceCopy;
        }

        static Texture2D MakeFlatTexture(Color color)
        {
            string key = "flat:" + ColorUtility.ToHtmlStringRGBA(color);
            Texture2D existing;
            if (TintedTextures.TryGetValue(key, out existing) && existing != null)
            {
                return existing;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "EditorThemesFlat_" + ColorUtility.ToHtmlStringRGBA(color)
            };

            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply();
            CreatedTextures.Add(texture);
            TintedTextures[key] = texture;
            return texture;
        }

        static Texture2D CopyTexture(Texture2D source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture renderTexture = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);

            Graphics.Blit(source, renderTexture);
            RenderTexture.active = renderTexture;

            Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "EditorThemesTinted_" + source.name
            };

            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);
            CreatedTextures.Add(copy);
            return copy;
        }

        static void RestoreSnapshots()
        {
            foreach (KeyValuePair<GUIStyleState, Snapshot> pair in Snapshots)
            {
                if (pair.Key == null)
                {
                    continue;
                }

                pair.Key.background = pair.Value.Background;
                pair.Key.textColor = pair.Value.TextColor;
            }

            Snapshots.Clear();

            foreach (Texture2D texture in CreatedTextures)
            {
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }

            CreatedTextures.Clear();
            TintedTextures.Clear();
        }

        static Color GetThemeColor(CustomTheme theme, int groupIndex, Color fallback)
        {
            List<string> itemNames = ThemesUtility.GetColorListByInt(groupIndex);
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

        static Color GetReadableTextColor(Color background)
        {
            float luminance = (0.2126f * background.r) + (0.7152f * background.g) + (0.0722f * background.b);
            return luminance > 0.55f ? Color.black : Color.white;
        }

        struct Snapshot
        {
            public readonly Texture2D Background;
            public readonly Color TextColor;

            public Snapshot(Texture2D background, Color textColor)
            {
                Background = background;
                TextColor = textColor;
            }
        }

    }
}
