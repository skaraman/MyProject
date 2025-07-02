using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

public static class DebugHelper {
  public static void LogObject(object obj) {
    string result = FormatObject(obj, 0);
    Debug.Log(result);
  }

  private static string FormatObject(object obj, int indent) {
    if (obj == null)
      return Indent(indent) + "null";

    Type type = obj.GetType();

    if (type.IsPrimitive || obj is string || obj is decimal) {
      return Indent(indent) + obj.ToString();
    }

    if (obj is IDictionary dictionary) {
      StringBuilder sb = new StringBuilder();
      sb.AppendLine(Indent(indent) + "Dictionary {");
      foreach (var key in dictionary.Keys) {
        sb.Append(Indent(indent + 1) + key + " : ");
        sb.AppendLine(FormatObject(dictionary[key], indent + 1));
      }
      sb.AppendLine(Indent(indent) + "}");
      return sb.ToString();
    }

    if (obj is IEnumerable enumerable && !(obj is string)) {
      StringBuilder sb = new StringBuilder();
      sb.AppendLine(Indent(indent) + "List [");
      foreach (var item in enumerable) {
        sb.AppendLine(FormatObject(item, indent + 1));
      }
      sb.AppendLine(Indent(indent) + "]");
      return sb.ToString();
    }

    StringBuilder objectBuilder = new StringBuilder();
    objectBuilder.AppendLine(Indent(indent) + type.Name + " {");

    foreach (PropertyInfo prop in type.GetProperties()) {
      try {
        object value = prop.GetValue(obj, null);
        objectBuilder.AppendLine(Indent(indent + 1) + prop.Name + " = " + FormatObject(value, indent + 1));
      }
      catch (Exception ex) {
        objectBuilder.AppendLine(Indent(indent + 1) + prop.Name + " = <Error: " + ex.Message + ">");
      }
    }

    foreach (FieldInfo field in type.GetFields()) {
      try {
        object value = field.GetValue(obj);
        objectBuilder.AppendLine(Indent(indent + 1) + field.Name + " = " + FormatObject(value, indent + 1));
      }
      catch (Exception ex) {
        objectBuilder.AppendLine(Indent(indent + 1) + field.Name + " = <Error: " + ex.Message + ">");
      }
    }

    objectBuilder.AppendLine(Indent(indent) + "}");
    return objectBuilder.ToString();
  }
  private static string Indent(int indent) {
    return new string(' ', indent * 2);
  }
}
