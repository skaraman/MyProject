using System;
using UnityEngine;

[Serializable]
public struct SpriteAddressPair {
  public string colorAddress;
  public string normalAddress;
  public string specularAddress;
  public string colorAtlasAddress;
  public string colorSpriteName;
  public string normalAtlasAddress;
  public string normalSpriteName;
  public string specularAtlasAddress;
  public string specularSpriteName;

  public bool HasColor => !string.IsNullOrWhiteSpace(RuntimeColorAddress);
  public bool HasNormal => !string.IsNullOrWhiteSpace(RuntimeNormalAddress);
  public bool HasSpecular => !string.IsNullOrWhiteSpace(RuntimeSpecularAddress);
  public string StreamingColorAddress => !string.IsNullOrWhiteSpace(colorAddress) ? colorAddress : RuntimeColorAddress;
  public string StreamingNormalAddress => !string.IsNullOrWhiteSpace(normalAddress) ? normalAddress : RuntimeNormalAddress;
  public string StreamingSpecularAddress => !string.IsNullOrWhiteSpace(specularAddress) ? specularAddress : RuntimeSpecularAddress;
  public string RuntimeColorAddress => !string.IsNullOrWhiteSpace(colorAtlasAddress) ? colorAtlasAddress : Normalize(colorAddress);
  public string RuntimeNormalAddress => !string.IsNullOrWhiteSpace(normalAtlasAddress) ? normalAtlasAddress : Normalize(normalAddress);
  public string RuntimeSpecularAddress => !string.IsNullOrWhiteSpace(specularAtlasAddress) ? specularAtlasAddress : Normalize(specularAddress);

  public static SpriteAddressPair Create(string colorAddress, string normalAddress, string specularAddress = "") {
    var pair = new SpriteAddressPair {
      colorAddress = Normalize(colorAddress),
      normalAddress = Normalize(normalAddress),
      specularAddress = Normalize(specularAddress)
    };
    PopulateRuntimeRef(pair.colorAddress, out pair.colorAtlasAddress, out pair.colorSpriteName);
    PopulateRuntimeRef(pair.normalAddress, out pair.normalAtlasAddress, out pair.normalSpriteName);
    PopulateRuntimeRef(pair.specularAddress, out pair.specularAtlasAddress, out pair.specularSpriteName);
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

public readonly struct SpriteLookupKey : System.IEquatable<SpriteLookupKey> {
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
    return string.IsNullOrWhiteSpace(value) ? "" : value;
  }

  public override string ToString() {
    return "libraryName='" + libraryName + "' labelPrefix='" + labelPrefix + "' category='" + category + "' frame=" + frame;
  }

  public bool Equals(SpriteLookupKey other) {
    return frame == other.frame &&
           string.Equals(libraryName, other.libraryName, System.StringComparison.OrdinalIgnoreCase) &&
           string.Equals(labelPrefix, other.labelPrefix, System.StringComparison.Ordinal) &&
           string.Equals(category, other.category, System.StringComparison.Ordinal);
  }

  public override bool Equals(object obj) {
    return obj is SpriteLookupKey other && Equals(other);
  }

  public override int GetHashCode() {
    unchecked {
      var hashCode = (libraryName != null ? System.StringComparer.OrdinalIgnoreCase.GetHashCode(libraryName) : 0);
      hashCode = (hashCode * 397) ^ (labelPrefix != null ? labelPrefix.GetHashCode() : 0);
      hashCode = (hashCode * 397) ^ (category != null ? category.GetHashCode() : 0);
      hashCode = (hashCode * 397) ^ frame;
      return hashCode;
    }
  }
}
