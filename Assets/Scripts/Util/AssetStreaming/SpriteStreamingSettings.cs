using System;
using UnityEngine;

[CreateAssetMenu(fileName = "SpriteStreamingSettings", menuName = "Sprite Streaming/Settings")]
public class SpriteStreamingSettings : ScriptableObject {
  [Header("Addressables")]
  public string manifestAddress = SpriteStreamingConfig.DefaultManifestAddress;

  [Header("Shard Cache")]
  [Min(1)] public int maxLoadedShards = 48;

  [Header("Texture Residency Budget (MB)")]
  [Min(64)] public int softTextureBudgetMb = 1024;
  [Min(64)] public int hardTextureBudgetMb = 1536;

  [Header("Runtime Streaming")]
  [Min(1)] public int maxAddressableStartsPerFrame = 8;
  [Min(1)] public int animationWarmupFrames = 24;
  [Min(0)] public int animationSwitchGateMs = 120;
  public bool keepLoadedSpritesForSession = true;
  public bool enableStreamingDiagnostics = false;
  [Header("Loading Screen Logs")]
  public bool enableLoadingScreenLogs = true;
  public bool enableAddressableLoadLogs = true;
  public bool logAddressableLoadsOutsideWarmGate = true;
  [Min(100)] public int loadingProgressLogIntervalMs = 500;
  [Header("Loading Overlay Performance")]
  [Min(1)] public int loadingOverlayMaxAddressableStartsPerFrame = 16;
  [Header("Atlas Expansion")]
  public bool enableAtlasExpansionOnSliceRequest = true;
  [Min(1)] public int atlasExpansionMaxSiblingAddresses = 256;
  public bool enableAtlasExpansionLogs = true;

  [Header("Appearance Set Streaming")]
  public bool enableAppearanceSetStreaming = true;
  public bool enablePinnedHotset = true;
  [Min(1)] public int pinWindowFrames = 24;
  [Min(0)] public int pinPredictedNextAnimations = 4;
  [Min(1)] public int pinRefreshFrameBucketSize = 8;
  [Min(32)] public int maxPinnedAddressesPerOwner = 1024;
  [Header("Pin Class Budgets (Addresses)")]
  [Min(1)] public int pinBudgetPlayerAddresses = 1024;
  [Min(1)] public int pinBudgetEnemyAddresses = 4096;
  [Min(1)] public int pinBudgetUiAddresses = 1024;
  [Min(1)] public int pinBudgetEffectAddresses = 1024;
  public bool pinAllSpawnedEnemies = true;
  public bool pinAllUi = true;
  [Min(16)] public int uiPinRefreshMs = 250;
  [Min(1)] public int pinDemoteBatchSize = 32;

  public long SoftTextureBudgetBytes {
    get { return Math.Max(softTextureBudgetMb, 64) * 1024L * 1024L; }
  }

  public long HardTextureBudgetBytes {
    get { return Math.Max(Math.Max(hardTextureBudgetMb, softTextureBudgetMb), 64) * 1024L * 1024L; }
  }

  public float AnimationSwitchGateSeconds {
    get { return Math.Max(animationSwitchGateMs, 0) / 1000f; }
  }
}

public static class SpriteStreamingRuntimeSettings {
  static bool loaded;
  static SpriteStreamingSettings cached;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    loaded = false;
    cached = null;
  }

  public static SpriteStreamingSettings Asset {
    get {
      if (loaded) return cached;
      loaded = true;
      cached = Resources.Load<SpriteStreamingSettings>("SpriteStreamingSettings");
      return cached;
    }
  }

  public static int MaxAddressableStartsPerFrame {
    get { return GetIntSetting(Asset != null ? Asset.maxAddressableStartsPerFrame : 8, 8, 1); }
  }

  public static int AnimationWarmupFrames {
    get { return GetIntSetting(Asset != null ? Asset.animationWarmupFrames : 24, 24, 1); }
  }

  public static int AnimationSwitchGateMs {
    get { return GetIntSetting(Asset != null ? Asset.animationSwitchGateMs : 120, 120, 0); }
  }

  public static bool EnableDiagnostics {
    get { return Asset != null && Asset.enableStreamingDiagnostics; }
  }

  public static bool KeepLoadedSpritesForSession {
    get { return GetBoolSetting(Asset == null || Asset.keepLoadedSpritesForSession, true); }
  }

  public static bool EnableLoadingScreenLogs {
    get { return GetBoolSetting(Asset != null && Asset.enableLoadingScreenLogs, true); }
  }

  public static bool EnableAddressableLoadLogs {
    get { return GetBoolSetting(Asset != null && Asset.enableAddressableLoadLogs, true); }
  }

  public static bool LogAddressableLoadsOutsideWarmGate {
    get { return GetBoolSetting(Asset != null && Asset.logAddressableLoadsOutsideWarmGate, true); }
  }

  public static int LoadingProgressLogIntervalMs {
    get { return GetIntSetting(Asset != null ? Asset.loadingProgressLogIntervalMs : 500, 500, 100); }
  }

  public static int LoadingOverlayMaxAddressableStartsPerFrame {
    get {
      var configured = Asset != null ? Asset.loadingOverlayMaxAddressableStartsPerFrame : 16;
      return GetIntSetting(configured, 16, 1);
    }
  }

  public static bool EnableAtlasExpansionOnSliceRequest {
    get { return GetBoolSetting(Asset != null && Asset.enableAtlasExpansionOnSliceRequest, true); }
  }

  public static int AtlasExpansionMaxSiblingAddresses {
    get { return GetIntSetting(Asset != null ? Asset.atlasExpansionMaxSiblingAddresses : 256, 256, 1); }
  }

  public static bool EnableAtlasExpansionLogs {
    get { return GetBoolSetting(Asset != null && Asset.enableAtlasExpansionLogs, true); }
  }

  public static bool EnableAppearanceSetStreaming {
    get { return GetBoolSetting(Asset != null && Asset.enableAppearanceSetStreaming, true); }
  }

  public static bool EnablePinnedHotset {
    get { return GetBoolSetting(Asset != null && Asset.enablePinnedHotset, true); }
  }

  public static int PinWindowFrames {
    get { return GetIntSetting(Asset != null ? Asset.pinWindowFrames : 24, 24, 1); }
  }

  public static int PinPredictedNextAnimations {
    get { return GetIntSetting(Asset != null ? Asset.pinPredictedNextAnimations : 4, 4, 0); }
  }

  public static int PinRefreshFrameBucketSize {
    get {
      var configured = Asset != null ? Asset.pinRefreshFrameBucketSize : 8;
      return GetPositiveOrFallback(configured, 8);
    }
  }

  public static int MaxPinnedAddressesPerOwner {
    get {
      var configured = Asset != null ? Asset.maxPinnedAddressesPerOwner : 1024;
      return GetIntSetting(GetPositiveOrFallback(configured, 1024), 1024, 32);
    }
  }

  public static bool PinAllSpawnedEnemies {
    get { return GetBoolSetting(Asset != null && Asset.pinAllSpawnedEnemies, true); }
  }

  public static bool PinAllUi {
    get { return GetBoolSetting(Asset != null && Asset.pinAllUi, true); }
  }

  public static int UiPinRefreshMs {
    get { return GetIntSetting(Asset != null ? Asset.uiPinRefreshMs : 250, 250, 16); }
  }

  public static int PinDemoteBatchSize {
    get { return GetIntSetting(Asset != null ? Asset.pinDemoteBatchSize : 32, 32, 1); }
  }

  public static int PinBudgetPlayerAddresses {
    get { return GetIntSetting(Asset != null ? Asset.pinBudgetPlayerAddresses : 1024, 1024, 1); }
  }

  public static int PinBudgetEnemyAddresses {
    get { return GetIntSetting(Asset != null ? Asset.pinBudgetEnemyAddresses : 4096, 4096, 1); }
  }

  public static int PinBudgetUiAddresses {
    get { return GetIntSetting(Asset != null ? Asset.pinBudgetUiAddresses : 1024, 1024, 1); }
  }

  public static int PinBudgetEffectAddresses {
    get { return GetIntSetting(Asset != null ? Asset.pinBudgetEffectAddresses : 1024, 1024, 1); }
  }

  static int GetIntSetting(int configuredOrDefault, int fallbackWhenMissing, int minValue) {
    var value = Asset != null ? configuredOrDefault : fallbackWhenMissing;
    return Math.Max(value, minValue);
  }

  static int GetPositiveOrFallback(int value, int fallback) {
    return value > 0 ? value : fallback;
  }

  static bool GetBoolSetting(bool configuredWhenAssetPresent, bool fallbackWhenMissing) {
    return Asset != null ? configuredWhenAssetPresent : fallbackWhenMissing;
  }
}
