using UnityEngine;
using System;
using System.Collections.Generic;

public static class ShaderColors {

  public static Dictionary<string, Color> myColors { get; } = new Dictionary<string, Color> {
    ["Yellow"] = new Vector4(1f, .95f, 0f, 1f),
    ["Brown"] = new Vector4(.4f, .2f, .15f, 1f),
    ["Green"] = new Vector4(.15f, 1f, 0f, 1f),
    ["Grey"] = new Vector4(.5f, .5f, .5f, 1f),
    ["Red"] = new Vector4(1f, .05f, 0f, 1f),
    ["Blue"] = new Vector4(0f, .52f, 1f, 1f),
    ["Darkblue"] = new Vector4(.1f, .15f, .55f, 1f),
    ["Lightblue"] = new Vector4(.15f, .5f, .9f, 1f),
    ["Shineblue"] = new Vector4(.66f, .7f, .9f, 1f),
    ["Purple"] = new Vector4(.45f, 0f, .7f, 1f),
    ["Darkpurple"] = new Vector4(.1f, 0f, .1f, 1f)
  };

  public static Dictionary<string, Dictionary<string, Dictionary<string, string>>> pairs { get; } = new Dictionary<string, Dictionary<string, Dictionary<string, string>>> {
    ["Base"] = new Dictionary<string, Dictionary<string, string>> {
      ["primary"] = new Dictionary<string, string> { { "stroke", "Yellow" }, { "color", "Brown" } },
      ["secondary"] = new Dictionary<string, string> { { "stroke", "Brown" }, { "color", "Yellow" } }
    },
    ["Bolt"] = new Dictionary<string, Dictionary<string, string>> {
      ["primary"] = new Dictionary<string, string> { { "stroke", "Green" }, { "color", "Grey" } },
      ["secondary"] = new Dictionary<string, string> { { "stroke", "Grey" }, { "color", "Green" } }
    },
    ["Fire"] = new Dictionary<string, Dictionary<string, string>> {
      ["primary"] = new Dictionary<string, string> { { "stroke", "Red" }, { "color", "Yellow" } },
      ["secondary"] = new Dictionary<string, string> { { "stroke", "Yellow" }, { "color", "Red" } }
    },
    ["Cold"] = new Dictionary<string, Dictionary<string, string>> {
      ["primary"] = new Dictionary<string, string> { { "stroke", "Blue" }, { "color", "Darkblue" } },
      ["secondary"] = new Dictionary<string, string> { { "stroke", "Darkblue" }, { "color", "Blue" } }
    },
    ["Aqua"] = new Dictionary<string, Dictionary<string, string>> {
      ["primary"] = new Dictionary<string, string> { { "stroke", "Shineblue" }, { "color", "Lightblue" } },
      ["secondary"] = new Dictionary<string, string> { { "stroke", "Lightblue" }, { "color", "Shineblue" } }
    },
    ["Dark"] = new Dictionary<string, Dictionary<string, string>> {
      ["primary"] = new Dictionary<string, string> { { "stroke", "Purple" }, { "color", "Darkpurple" } },
      ["secondary"] = new Dictionary<string, string> { { "stroke", "Darkpurple" }, { "color", "Purple" } }
    },
  };
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


