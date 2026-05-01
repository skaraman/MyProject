using System;
using UnityEngine;

[Serializable]
public struct SpriteAddressPair {
  public string colorAddress;
  public string normalAddress;
  public string colorAtlasAddress;
  public string colorSpriteName;
  public string normalAtlasAddress;
  public string normalSpriteName;

  public bool HasColor => !string.IsNullOrWhiteSpace(RuntimeColorAddress);
  public bool HasNormal => !string.IsNullOrWhiteSpace(RuntimeNormalAddress);
  public string StreamingColorAddress => !string.IsNullOrWhiteSpace(colorAddress) ? colorAddress : RuntimeColorAddress;
  public string StreamingNormalAddress => !string.IsNullOrWhiteSpace(normalAddress) ? normalAddress : RuntimeNormalAddress;
  public string RuntimeColorAddress => !string.IsNullOrWhiteSpace(colorAtlasAddress) ? colorAtlasAddress : Normalize(colorAddress);
  public string RuntimeNormalAddress => !string.IsNullOrWhiteSpace(normalAtlasAddress) ? normalAtlasAddress : Normalize(normalAddress);

  public static SpriteAddressPair Create(string colorAddress, string normalAddress) {
    var pair = new SpriteAddressPair {
      colorAddress = Normalize(colorAddress),
      normalAddress = Normalize(normalAddress)
    };
    PopulateRuntimeRef(pair.colorAddress, out pair.colorAtlasAddress, out pair.colorSpriteName);
    PopulateRuntimeRef(pair.normalAddress, out pair.normalAtlasAddress, out pair.normalSpriteName);
    return pair;
  }

  static void PopulateRuntimeRef(string rawAddress, out string atlasAddress, out string spriteName) {
    atlasAddress = "";
    spriteName = "";
    if (string.IsNullOrWhiteSpace(rawAddress)) return;
    if (SpriteSliceAddressUtility.TryParseSliceAddress(rawAddress, out atlasAddress, out spriteName)) {
      atlasAddress = Normalize(atlasAddress);
      spriteName = Normalize(spriteName);
      return;
    }
    atlasAddress = Normalize(rawAddress);
  }

  static string Normalize(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }
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
