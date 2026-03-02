using System;
using UnityEngine;

[Serializable]
public struct SpriteAddressPair {
  public string colorAddress;
  public string normalAddress;

  public bool HasColor => !string.IsNullOrWhiteSpace(colorAddress);
}

public readonly struct SpriteLookupKey {
  public readonly string libraryName;
  public readonly string labelPrefix;
  public readonly string category;
  public readonly int frame;

  public string namepart => libraryName; // backwards compatibility
  public string LibraryName => libraryName;

  public SpriteLookupKey(string libraryName, string labelPrefix, string category, int frame) {
    this.libraryName = Normalize(libraryName);
    this.labelPrefix = Normalize(labelPrefix);
    this.category = Normalize(category);
    this.frame = frame;
  }

  static string Normalize(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  public override string ToString() {
    return "libraryName='" + libraryName + "' labelPrefix='" + labelPrefix + "' category='" + category + "' frame=" + frame;
  }
}
