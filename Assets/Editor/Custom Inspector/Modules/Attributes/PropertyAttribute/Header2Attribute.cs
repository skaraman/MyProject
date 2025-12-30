using System;
using System.Diagnostics;
using UnityEngine;

namespace CustomInspector {
  [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
  [Conditional("UNITY_EDITOR")]
  public class Header2Attribute : ComparablePropertyAttribute {
    public readonly string content;

    public bool bold = true;
    public bool underlined = false;
    public int upperSpacing = 12;
    public string tooltip = null;
    public byte fontSize = 12;
    public TextAlignment alignment = TextAlignment.Left;

    public Header2Attribute(string content) {
      order = -10;
      this.content = content;
    }

    protected override object[] GetParameters() => new object[] { content, underlined };
  }
}
