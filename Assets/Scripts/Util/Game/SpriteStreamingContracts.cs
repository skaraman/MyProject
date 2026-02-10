using System;
using System.Collections.Generic;
using UnityEngine;

public static class SpriteStreamingConfig {
  public const string SourceRootFolder = "Assets/Sprites/SpriteLibraries";
  public const string RuntimeIndexFolder = "Assets/Sprites/SpriteLibraries/RuntimeIndex";
  public const string ManifestAssetPath = "Assets/Sprites/SpriteLibraries/SpriteIndexManifest.bytes";
  public const string IncludeAssetPath = "Assets/Sprites/SpriteLibraries/SpriteStreamingInclude.asset";
  public const string SettingsAssetPath = "Assets/Resources/SpriteStreamingSettings.asset";

  public const string TextureAddressablesGroupName = "SpriteTextures";
  public const string IndexAddressablesGroupName = "SpriteRuntimeIndex";
  public const string DefaultManifestAddress = "SpriteRuntimeIndex/Manifest";
}

[CreateAssetMenu(fileName = "SpriteIndexManifest", menuName = "Sprite Streaming/Index Manifest")]
public class SpriteIndexManifest : ScriptableObject {
  [Serializable]
  public class ShardEntry {
    public string namepart;
    public string address;
    public string assetPath;
    public int rowCount;
    public string contentHash;
  }

  public string manifestHash;
  public List<ShardEntry> shards = new();
}

[CreateAssetMenu(fileName = "SpriteStreamingSettings", menuName = "Sprite Streaming/Settings")]
public class SpriteStreamingSettings : ScriptableObject {
  [Header("Addressables")]
  public string manifestAddress = SpriteStreamingConfig.DefaultManifestAddress;

  [Header("Shard Cache")]
  [Min(1)] public int maxLoadedShards = 48;

  [Header("Texture Residency Budget (MB)")]
  [Min(64)] public int softTextureBudgetMb = 1024;
  [Min(64)] public int hardTextureBudgetMb = 1536;

  public long SoftTextureBudgetBytes {
    get { return Math.Max(softTextureBudgetMb, 64) * 1024L * 1024L; }
  }

  public long HardTextureBudgetBytes {
    get { return Math.Max(Math.Max(hardTextureBudgetMb, softTextureBudgetMb), 64) * 1024L * 1024L; }
  }
}

[CreateAssetMenu(fileName = "SpriteStreamingInclude", menuName = "Sprite Streaming/Include")]
public class SpriteStreamingInclude : ScriptableObject {
  public List<string> nameparts = new();
}
