using System;
using System.Collections.Generic;
using UnityEngine;

public static class ShaderColors {
  public const string PrimaryGroup = "primary";
  public const string SecondaryGroup = "secondary";
  public const string OutlineValue = "outline";
  public const string ColorValue = "color";

  public static Dictionary<string, Color> myColors { get; } = new Dictionary<string, Color> {
    ["Yellow"] = new Vector4(1f, .95f, 0f, 1f),
    ["Brown"] = new Vector4(.4f, .2f, .15f, 1f),
    ["Green"] = new Vector4(.15f, 1f, 0f, 1f),
    ["Grey"] = new Vector4(.5f, .5f, .5f, 1f),
    ["Red"] = new Vector4(1f, .05f, 0f, 1f),
    ["Blue"] = new Vector4(0f, .52f, 1f, 1f),
    ["DarkBlue"] = new Vector4(.1f, .15f, .55f, 1f),
    ["LightBlue"] = new Vector4(.15f, .5f, .9f, 1f),
    ["ShineBlue"] = new Vector4(.66f, .7f, .9f, 1f),
    ["Purple"] = new Vector4(.45f, 0f, .7f, 1f),
    ["DarkPurple"] = new Vector4(.1f, 0f, .1f, 1f)
  };

  public static Dictionary<string, Dictionary<string, Dictionary<string, string>>> pairs { get; } = new Dictionary<string, Dictionary<string, Dictionary<string, string>>> {
    ["Base"] = new Dictionary<string, Dictionary<string, string>> {
      [PrimaryGroup] = new Dictionary<string, string> { { OutlineValue, "Yellow" }, { ColorValue, "Brown" } },
      [SecondaryGroup] = new Dictionary<string, string> { { OutlineValue, "Brown" }, { ColorValue, "Yellow" } }
    },
    ["Bolt"] = new Dictionary<string, Dictionary<string, string>> {
      [PrimaryGroup] = new Dictionary<string, string> { { OutlineValue, "Green" }, { ColorValue, "Grey" } },
      [SecondaryGroup] = new Dictionary<string, string> { { OutlineValue, "Grey" }, { ColorValue, "Green" } }
    },
    ["Fire"] = new Dictionary<string, Dictionary<string, string>> {
      [PrimaryGroup] = new Dictionary<string, string> { { OutlineValue, "Red" }, { ColorValue, "Yellow" } },
      [SecondaryGroup] = new Dictionary<string, string> { { OutlineValue, "Yellow" }, { ColorValue, "Red" } }
    },
    ["Cold"] = new Dictionary<string, Dictionary<string, string>> {
      [PrimaryGroup] = new Dictionary<string, string> { { OutlineValue, "Blue" }, { ColorValue, "DarkBlue" } },
      [SecondaryGroup] = new Dictionary<string, string> { { OutlineValue, "DarkBlue" }, { ColorValue, "Blue" } }
    },
    ["Aqua"] = new Dictionary<string, Dictionary<string, string>> {
      [PrimaryGroup] = new Dictionary<string, string> { { OutlineValue, "ShineBlue" }, { ColorValue, "LightBlue" } },
      [SecondaryGroup] = new Dictionary<string, string> { { OutlineValue, "LightBlue" }, { ColorValue, "ShineBlue" } }
    },
    ["Dark"] = new Dictionary<string, Dictionary<string, string>> {
      [PrimaryGroup] = new Dictionary<string, string> { { OutlineValue, "Purple" }, { ColorValue, "DarkPurple" } },
      [SecondaryGroup] = new Dictionary<string, string> { { OutlineValue, "DarkPurple" }, { ColorValue, "Purple" } }
    },
  };

  public static bool TryGetNamedColor(string colorName, out Color color) {
    color = Color.white;
    if (string.IsNullOrWhiteSpace(colorName)) {
      return false;
    }

    return myColors.TryGetValue(colorName.Trim(), out color);
  }

  public static bool TryGetFormColor(
    string formName,
    string groupName,
    out Color color,
    out string colorName
  ) {
    return TryGetFormGroupValue(formName, groupName, ColorValue, out color, out colorName);
  }

  public static bool TryGetFormOutlineColor(
    string formName,
    string groupName,
    out Color color,
    out string colorName
  ) {
    return TryGetFormGroupValue(formName, groupName, OutlineValue, out color, out colorName);
  }

  public static bool TryGetFormPalette(
    string formName,
    string groupName,
    out Color color,
    out Color outlineColor,
    out string colorName,
    out string outlineColorName
  ) {
    var hasColor = TryGetFormColor(formName, groupName, out color, out colorName);
    var hasOutlineColor = TryGetFormOutlineColor(
      formName,
      groupName,
      out outlineColor,
      out outlineColorName
    );
    return hasColor && hasOutlineColor;
  }

  static bool TryGetFormGroupValue(
    string formName,
    string groupName,
    string valueName,
    out Color color,
    out string colorName
  ) {
    color = Color.white;
    colorName = null;
    var resolvedForm = EsperanzaForms.ResolveFormKey(formName);
    if (string.IsNullOrWhiteSpace(resolvedForm) ||
        string.IsNullOrWhiteSpace(groupName) ||
        string.IsNullOrWhiteSpace(valueName)) {
      return false;
    }

    if (!pairs.TryGetValue(resolvedForm, out var formGroups) || formGroups == null) {
      return false;
    }
    if (!formGroups.TryGetValue(groupName.Trim(), out var groupValues) || groupValues == null) {
      return false;
    }
    if (!groupValues.TryGetValue(valueName.Trim(), out colorName) || string.IsNullOrWhiteSpace(colorName)) {
      return false;
    }

    return TryGetNamedColor(colorName, out color);
  }

  public static bool TryGetActiveFormColor(
    string groupName,
    out Color color,
    out string colorName
  ) {
    return TryGetFormColor(EsperanzaForms.GetActive(), groupName, out color, out colorName);
  }
}

// public class Gold : MonoBehaviour
// { 

// }

// public class Gems : MonoBehaviour
// {
//   public Sprite Amber;
//   public Sprite Emerald;
//   public Sprite Opal;
//   public Sprite Ruby;
//   public Sprite Sapphire;
//   public Sprite Amethyst;
// }

