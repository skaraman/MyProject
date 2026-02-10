using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Profiling;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class TextureResidencyCache {
  internal sealed class CacheEntry {
    public string address;
    public AsyncOperationHandle<Sprite> handle;
    public Sprite sprite;
    public int pinCount;
    public bool isDone;
    public bool isSuccess;
    public bool isEvicted;
    public long lastAccessTicks;
  }

  public sealed class Lease {
    CacheEntry _entry;
    bool _released;

    internal Lease(CacheEntry entry) {
      _entry = entry;
    }

    public bool IsDone => _entry == null || _entry.isDone;
    public bool IsSuccess => _entry != null && _entry.isDone && _entry.isSuccess && _entry.sprite != null;
    public Sprite Sprite => IsSuccess ? _entry.sprite : null;
    public string Address => _entry != null ? _entry.address : "";

    public void Release() {
      if (_released) return;
      _released = true;
      if (_entry != null) {
        ReleaseInternal(_entry);
      }
      _entry = null;
    }
  }

  struct CacheSettings {
    public long softTextureBudgetBytes;
    public long hardTextureBudgetBytes;
  }

  static readonly Dictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);
  static CacheSettings settings;
  static bool settingsLoaded;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    settingsLoaded = false;
    settings = default;
    PurgeAll();
  }

  public static int LoadedEntryCount => cache.Count;

  public static long EstimatedResidentBytes {
    get { return CalculateResidentBytes(); }
  }

  public static Lease AcquireAsync(string address) {
    var normalizedAddress = NormalizeAddress(address);
    if (string.IsNullOrEmpty(normalizedAddress)) return null;

    if (!cache.TryGetValue(normalizedAddress, out var entry)) {
      entry = CreateEntry(normalizedAddress);
      cache[normalizedAddress] = entry;
    }
    else if (entry.isDone && !entry.isSuccess) {
      Evict(normalizedAddress, entry);
      entry = CreateEntry(normalizedAddress);
      cache[normalizedAddress] = entry;
    }

    entry.pinCount++;
    entry.lastAccessTicks = DateTime.UtcNow.Ticks;
    MaintainBudget();
    return new Lease(entry);
  }

  public static void Release(Lease lease) {
    if (lease == null) return;
    lease.Release();
    MaintainBudget();
  }

  public static void PurgeUnpinned() {
    var keys = new List<string>();
    foreach (var pair in cache) {
      var entry = pair.Value;
      if (entry.pinCount > 0) continue;
      if (!entry.isDone) continue;
      keys.Add(pair.Key);
    }

    for (var i = 0; i < keys.Count; i++) {
      if (!cache.TryGetValue(keys[i], out var entry)) continue;
      Evict(keys[i], entry);
    }
  }

  public static void PurgeAll() {
    foreach (var pair in cache) {
      pair.Value.isEvicted = true;
      ReleaseHandle(pair.Value);
    }
    cache.Clear();
  }

  static CacheEntry CreateEntry(string address) {
    var entry = new CacheEntry {
      address = address,
      pinCount = 0,
      isDone = false,
      isSuccess = false,
      isEvicted = false,
      lastAccessTicks = DateTime.UtcNow.Ticks
    };

    entry.handle = Addressables.LoadAssetAsync<Sprite>(address);
    entry.handle.Completed += op => {
      if (entry.isEvicted) return;

      entry.isDone = true;
      entry.isSuccess = op.Status == AsyncOperationStatus.Succeeded && op.Result != null;
      entry.sprite = entry.isSuccess ? op.Result : null;
      entry.lastAccessTicks = DateTime.UtcNow.Ticks;

      if (!entry.isSuccess) {
        Debug.LogError("[TextureResidencyCache] Failed to load sprite address '" + address + "'.");
      }

      MaintainBudget();
    };

    return entry;
  }

  static void ReleaseInternal(CacheEntry entry) {
    if (entry == null) return;
    if (entry.pinCount > 0) {
      entry.pinCount--;
    }
    entry.lastAccessTicks = DateTime.UtcNow.Ticks;
  }

  static void MaintainBudget() {
    var cfg = GetSettings();
    var softBytes = cfg.softTextureBudgetBytes;
    var hardBytes = cfg.hardTextureBudgetBytes;
    if (hardBytes < softBytes) hardBytes = softBytes;

    var residentBytes = CalculateResidentBytes();
    var targetBytes = residentBytes > hardBytes ? softBytes : softBytes;
    if (residentBytes <= targetBytes) return;

    while (residentBytes > targetBytes) {
      if (!TryEvictOldestUnpinned()) break;
      residentBytes = CalculateResidentBytes();
    }
  }

  static bool TryEvictOldestUnpinned() {
    string oldestKey = null;
    CacheEntry oldestEntry = null;

    foreach (var pair in cache) {
      var candidate = pair.Value;
      if (candidate.pinCount > 0) continue;
      if (!candidate.isDone) continue;
      if (oldestEntry == null || candidate.lastAccessTicks < oldestEntry.lastAccessTicks) {
        oldestEntry = candidate;
        oldestKey = pair.Key;
      }
    }

    if (oldestEntry == null || oldestKey == null) return false;
    Evict(oldestKey, oldestEntry);
    return true;
  }

  static long CalculateResidentBytes() {
    if (cache.Count == 0) return 0;

    var seenTextures = new HashSet<int>();
    long total = 0;

    foreach (var pair in cache) {
      var entry = pair.Value;
      if (!entry.isDone || !entry.isSuccess || entry.sprite == null) continue;
      var texture = entry.sprite.texture;
      if (texture == null) continue;

      var textureId = texture.GetInstanceID();
      if (!seenTextures.Add(textureId)) continue;

      var bytes = Profiler.GetRuntimeMemorySizeLong(texture);
      if (bytes <= 0) {
        bytes = EstimateTextureBytes(texture);
      }
      total += bytes;
    }

    return total;
  }

  static long EstimateTextureBytes(Texture texture) {
    if (texture == null) return 0;
    var width = Math.Max(texture.width, 1);
    var height = Math.Max(texture.height, 1);
    return width * height * 4L;
  }

  static void Evict(string key, CacheEntry entry) {
    if (entry == null) return;
    if (entry.isEvicted) return;
    if (cache.TryGetValue(key, out var current) && !ReferenceEquals(current, entry)) return;
    cache.Remove(key);
    entry.isEvicted = true;
    ReleaseHandle(entry);
  }

  static void ReleaseHandle(CacheEntry entry) {
    if (entry == null) return;
    if (entry.handle.IsValid()) {
      Addressables.Release(entry.handle);
    }
    entry.handle = default;
    entry.sprite = null;
    entry.isDone = false;
    entry.isSuccess = false;
  }

  static CacheSettings GetSettings() {
    if (settingsLoaded) return settings;
    settingsLoaded = true;
    settings = new CacheSettings {
      softTextureBudgetBytes = 1024L * 1024L * 1024L,
      hardTextureBudgetBytes = 1536L * 1024L * 1024L
    };

    var settingsAsset = Resources.Load("SpriteStreamingSettings") as ScriptableObject;
    if (settingsAsset != null) {
      var softMb = ReadIntSetting(settingsAsset, "softTextureBudgetMb", 1024);
      var hardMb = ReadIntSetting(settingsAsset, "hardTextureBudgetMb", 1536);
      if (hardMb < softMb) hardMb = softMb;
      settings.softTextureBudgetBytes = Math.Max(softMb, 64) * 1024L * 1024L;
      settings.hardTextureBudgetBytes = Math.Max(hardMb, 64) * 1024L * 1024L;
    }

    return settings;
  }

  static int ReadIntSetting(ScriptableObject settingsAsset, string memberName, int fallback) {
    if (settingsAsset == null || string.IsNullOrWhiteSpace(memberName)) return fallback;

    var type = settingsAsset.GetType();
    var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (field != null && field.FieldType == typeof(int)) {
      return (int)field.GetValue(settingsAsset);
    }

    var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (property != null && property.PropertyType == typeof(int) && property.CanRead) {
      return (int)property.GetValue(settingsAsset);
    }

    return fallback;
  }

  static string NormalizeAddress(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }
}
