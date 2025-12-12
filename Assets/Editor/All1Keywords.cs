#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class AllIn1ShaderKeywords {
  public static string[] Keywords = new[] {
    "GLOWLIGHT_ON",
    "GLOW_ON",
    "FADE_ON",
    "OUTBASE_ON",
    "ONLYOUTLINE_ON",
    "GRADIENT_ON",
    "GRADIENT2COL_ON",
    "RADIALGRADIENT_ON",
    "COLORSWAP_ON",
    "HSV_ON",
    "CHANGECOLOR_ON",
    "CHANGECOLOR2_ON",
    "CHANGECOLOR3_ON",
    "COLORRAMP_ON",
    "GRADIENTCOLORRAMP_ON",
    "HITEFFECT_ON",
    "NEGATIVE_ON",
    "PIXELATE_ON",
    "GREYSCALE_ON",
    "POSTERIZE_ON",
    "BLUR_ON",
    "MOTIONBLUR_ON",
    "GHOST_ON",
    "ALPHAOUTLINE_ON",
    "INNEROUTLINE_ON",
    "ONLYINNEROUTLINE_ON",
    "HOLOGRAM_ON",
    "CHROMABERR_ON",
    "GLITCH_ON",
    "FLICKER_ON",
    "SHADOW_ON",
    "SHINE_ON",
    "CONTRAST_ON",
    "OVERLAY_ON",
    "OVERLAYMULT_ON",
    "ALPHACUTOFF_ON",
    "ALPHAROUND_ON",
    "DOODLE_ON",
    "WIND_ON",
    "WAVEUV_ON",
    "ROUNDWAVEUV_ON",
    "RECTSIZE_ON",
    "OFFSETUV_ON",
    "CLIPPING_ON",
    "RADIALCLIPPING_ON",
    "TEXTURESCROLL_ON",
    "ZOOMUV_ON",
    "DISTORT_ON",
    "WARP_ON",
    "TWISTUV_ON",
    "ROTATEUV_ON",
    "POLARUV_ON",
    "FISHEYE_ON",
    "PINCH_ON",
    "SHAKEUV_ON",
    "GLOWTEX_ON",
    "OUTTEX_ON",
    "OUTDIST_ON",
    "OUTBASE8DIR_ON",
    "OUTBASEPIXELPERF_ON",
    "COLORRAMPOUTLINE_ON",
    "GREYSCALEOUTLINE_ON",
    "POSTERIZEOUTLINE_ON",
    "BLURISHD_ON",
    "MANUALWIND_ON",
    "ATLAS_ON",
    "PREMULTIPLYALPHA_ON",
    "BILBOARD_ON",
    "BILBOARDY_ON"
  };
}

[CustomPropertyDrawer(typeof(AllIn1AnimatorInspector.KeywordToggle))]
public class AllIn1KeywordToggleDrawer : PropertyDrawer {
  public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
    EditorGUI.BeginProperty(position, label, property);
    var keywordProp = property.FindPropertyRelative("keyword");
    var enabledProp = property.FindPropertyRelative("enabled");
    var line = position;
    line.height = EditorGUIUtility.singleLineHeight;
    var left = new Rect(line.x, line.y, line.width * 0.7f, line.height);
    var right = new Rect(line.x + left.width + 4f, line.y, line.width - left.width - 4f, line.height);
    var options = AllIn1ShaderKeywords.Keywords;
    if (options == null || options.Length == 0) options = new[] { "" };
    var current = keywordProp.stringValue;
    if (string.IsNullOrEmpty(current)) current = options[0];
    var index = System.Array.IndexOf(options, current);
    if (index < 0) index = 0;
    EditorGUI.BeginChangeCheck();
    index = EditorGUI.Popup(left, index, options);
    if (EditorGUI.EndChangeCheck()) {
      if (index >= 0 && index < options.Length) keywordProp.stringValue = options[index];
    }
    enabledProp.boolValue = EditorGUI.Toggle(right, enabledProp.boolValue);
    EditorGUI.EndProperty();
  }
}
#endif
