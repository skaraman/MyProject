using System;
using System.Collections.Generic;
using UnityEngine;

public static class SpriteAddressCatalogConfig {
  public const string SourceRootFolder = "Assets/Sprites/SpriteLibraries";
  public const string CatalogAssetPath = "Assets/Sprites/SpriteLibraries/SpriteAddressCatalog.asset";
  public const string AddressablesGroupName = "SpriteTextures";
}

[Serializable]
public struct SpriteAddressPair {
  public string colorAddress;
  public string normalAddress;

  public bool HasColor => !string.IsNullOrWhiteSpace(colorAddress);
}

public readonly struct SpriteLookupKey {
  public readonly string namepart;
  public readonly string form;
  public readonly string animation;
  public readonly int frame;

  public SpriteLookupKey(string namepart, string form, string animation, int frame) {
    this.namepart = Normalize(namepart);
    this.form = Normalize(form);
    this.animation = Normalize(animation);
    this.frame = frame;
  }

  static string Normalize(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  public override string ToString() {
    return "namepart='" + namepart + "' form='" + form + "' animation='" + animation + "' frame=" + frame;
  }
}

[CreateAssetMenu(fileName = "SpriteAddressCatalog", menuName = "Sprite Libraries/Address Catalog")]
public class SpriteAddressCatalog : ScriptableObject {
  [Serializable]
  public class Entry {
    public string namepart;
    public string form;
    public string animation;
    public int frame;
    public string colorAddress;
    public string normalAddress;
  }

  public List<Entry> entries = new();
}
