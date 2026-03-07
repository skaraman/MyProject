using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SpriteStreamingSettings", menuName = "Sprite Streaming/Settings")]
public class SpriteStreamingSettings : ScriptableObject {
  [Header("Addressables")]
  public string manifestAddress = SpriteStreamingConfig.DefaultManifestAddress;
  [Header("Warm Labels")]
  // first‑contact animation labels (player start frame, nearby enemy spawn/idle/locomotion,
  // etc). If these are left blank warm gates will fall back on broad warm lists and
  // may miss entry frames, leading to first‑play hitches.  Designers should populate
  // at least the critical list; a warning is emitted in the editor when empty.
  public List<string> criticalAddressableLabels = new();
  public List<string> warmAddressableLabels = new();
  public List<string> warmUiAddressableLabels = new();

  [Header("Shard Cache")]
  [Min(1)] public int maxLoadedShards = 48;

  [Header("Texture Residency Budget (MB)")]
  [Min(64)] public int softTextureBudgetMb = 1024;
  [Min(64)] public int hardTextureBudgetMb = 1536;

  [Header("Runtime Streaming")]
  // these values should ideally be driven by first-frame latency telemetry.  telemetry
  // hooks exist below; in their absence hardcoded defaults are used.
  // Gameplay remains conservative; the loading overlay gets its own more aggressive budget.
  [Min(1)] public int maxAddressableStartsPerFrame = 8;
  [Min(1)] public int animationWarmupFrames = 24;
  [Min(0)] public int animationSwitchGateMs = 150;
  public bool keepLoadedSpritesForSession = true;
  public bool enableStreamingDiagnostics = false;
  [Header("Loading Screen Logs")]
  public bool enableLoadingScreenLogs = true;
  public bool enableAddressableLoadLogs = true;
  public bool logAddressableLoadsOutsideWarmGate = true;
  [Min(100)] public int loadingProgressLogIntervalMs = 500;
  [Header("Loading Overlay Performance")]
  // amount of addressable start slots available when the loading overlay is visible.  this
  // value should be high enough to recover from warm-gate misses but not so large that the
  // main thread spikes excessively; a simple validation check runs in the editor.
  [Min(1)] public int loadingOverlayMaxAddressableStartsPerFrame = 24;
  [Header("Platform Start Presets")]
  public bool usePlatformAddressableStartPresets = true;
  // TODO: default is 8 but the runtime comment on this field says "~16 for smoother desktop play"
  // and the non-preset fallback is also 16. When usePlatformAddressableStartPresets = true (the default),
  // desktop gameplay gets 8 starts/frame — half the intended rate. Raise to 16 to match intent,
  // or remove the platform-preset path and rely on maxAddressableStartsPerFrame directly.
  [Min(1)] public int desktopMaxAddressableStartsPerFrame = 8;
  [Min(1)] public int desktopLoadingOverlayMaxAddressableStartsPerFrame = 24;
  [Min(1)] public int mobileMaxAddressableStartsPerFrame = 6;
  [Min(1)] public int mobileLoadingOverlayMaxAddressableStartsPerFrame = 12;
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
  [Min(32)] public int maxPinnedAddressesPerOwner = 960;
  [Header("Pin Class Budgets (Addresses)")]
  [Min(1)] public int pinBudgetPlayerAddresses = 1536;
  [Min(1)] public int pinBudgetEnemyAddresses = 3072;
  [Min(1)] public int pinBudgetUiAddresses = 1024;
  [Min(1)] public int pinBudgetEffectAddresses = 1536;
  public bool pinAllSpawnedEnemies = false;
  [Header("Enemy Residency")]
  public bool enableDynamicEnemyResidency = true;
  public bool pinVisibleEnemiesBeyondDistance = true;
  [Min(0f)] public float enemyPinNearDistance = 25f;
  [Min(0f)] public float enemyPinReleaseDistance = 40f;
  [Min(1)] public int enemyResidencyRefreshFrameInterval = 8;
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
    get {
      if (Asset != null && Asset.usePlatformAddressableStartPresets) {
        var useMobilePreset = ShouldUseMobileAddressablePreset();
        var configured = useMobilePreset
          ? Asset.mobileMaxAddressableStartsPerFrame
          : Asset.desktopMaxAddressableStartsPerFrame; // Gameplay stays conservative; overlay uses a separate higher cap.
        var fallback = useMobilePreset ? 6 : 8;
        return GetIntSetting(configured, fallback, 1);
      }
      return GetIntSetting(Asset != null ? Asset.maxAddressableStartsPerFrame : 8, 8, 1);
    }
  }

  public static int AnimationWarmupFrames {
    get {
      var baseFrames = GetIntSetting(Asset != null ? Asset.animationWarmupFrames : 24, 24, 1);
      return StreamingTelemetry.ApplyWarmupFrameHint(baseFrames);
    }
  }

  public static int AnimationSwitchGateMs {
    get { return GetIntSetting(Asset != null ? Asset.animationSwitchGateMs : 150, 150, 0); }
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
      // overlay budget should never be lower than the base maximum, since that would make
      // warm‑gate misses un-recoverable; clamp accordingly.
      var baseValue = MaxAddressableStartsPerFrame;
      int result;
      if (Asset != null && Asset.usePlatformAddressableStartPresets) {
        var useMobilePreset = ShouldUseMobileAddressablePreset();
        var configuredPreset = useMobilePreset ?
          Asset.mobileLoadingOverlayMaxAddressableStartsPerFrame :
          Asset.desktopLoadingOverlayMaxAddressableStartsPerFrame;
        var fallbackPreset = useMobilePreset ? 12 : 24;
        result = GetIntSetting(configuredPreset, fallbackPreset, 1);
      } else {
        var configured = Asset != null ? Asset.loadingOverlayMaxAddressableStartsPerFrame : 24;
        result = GetIntSetting(configured, 24, 1);
      }
      return Math.Max(result, baseValue);
    }
  }

  public static IReadOnlyList<string> CriticalAddressableLabels {
    get { return Asset != null && Asset.criticalAddressableLabels != null ? Asset.criticalAddressableLabels : Array.Empty<string>(); }
  }

  public static IReadOnlyList<string> WarmAddressableLabels {
    get { return Asset != null && Asset.warmAddressableLabels != null ? Asset.warmAddressableLabels : Array.Empty<string>(); }
  }

  public static IReadOnlyList<string> WarmUiAddressableLabels {
    get { return Asset != null && Asset.warmUiAddressableLabels != null ? Asset.warmUiAddressableLabels : Array.Empty<string>(); }
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
      var configured = Asset != null ? Asset.maxPinnedAddressesPerOwner : 960;
      return GetIntSetting(GetPositiveOrFallback(configured, 960), 960, 32);
    }
  }

  public static bool PinAllSpawnedEnemies {
    get { return GetBoolSetting(Asset != null && Asset.pinAllSpawnedEnemies, false); }
  }

  public static bool EnableDynamicEnemyResidency {
    get { return GetBoolSetting(Asset != null && Asset.enableDynamicEnemyResidency, true); }
  }

  public static bool PinVisibleEnemiesBeyondDistance {
    get { return GetBoolSetting(Asset == null || Asset.pinVisibleEnemiesBeyondDistance, true); }
  }

  public static float EnemyPinNearDistance {
    get { return Mathf.Max(Asset != null ? Asset.enemyPinNearDistance : 25f, 0f); }
  }

  public static float EnemyPinReleaseDistance {
    get {
      var configured = Mathf.Max(Asset != null ? Asset.enemyPinReleaseDistance : 40f, 0f);
      return Mathf.Max(configured, EnemyPinNearDistance);
    }
  }

  public static int EnemyResidencyRefreshFrameInterval {
    get { return GetIntSetting(Asset != null ? Asset.enemyResidencyRefreshFrameInterval : 8, 8, 1); }
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
    get { return GetIntSetting(Asset != null ? Asset.pinBudgetPlayerAddresses : 1536, 1536, 1); }
  }

  public static int PinBudgetEnemyAddresses {
    get { return GetIntSetting(Asset != null ? Asset.pinBudgetEnemyAddresses : 3072, 3072, 1); }
  }

  public static int PinBudgetUiAddresses {
    get { return GetIntSetting(Asset != null ? Asset.pinBudgetUiAddresses : 1024, 1024, 1); }
  }

  public static int PinBudgetEffectAddresses {
    get { return GetIntSetting(Asset != null ? Asset.pinBudgetEffectAddresses : 1536, 1536, 1); }
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

  static bool ShouldUseMobileAddressablePreset() {
    return Application.isMobilePlatform;
  }
}

// telemetry helpers referenced by SpriteStreamingSettings properties.  kept simple and
// isolated so they can eventually be moved to a proper telemetry assembly when one
// exists.
public static class StreamingTelemetry {
  // Returns the observed first-frame latency (ms) or negative if not available.
  public static float FirstFrameLatencyMs {
    get {
      // TODO: hook into real telemetry system; stub returns -1 until implemented.
      return -1f;
    }
  }

  // Apply hints derived from telemetry to the base warmup frame count.
  public static int ApplyWarmupFrameHint(int baseFrames) {
    var latency = FirstFrameLatencyMs;
    if (latency > 0f) {
      // simple heuristic: one extra frame per 16ms of latency, capped moderately
      var extra = Mathf.CeilToInt(latency / 16f);
      return Mathf.Clamp(baseFrames + extra, 1, 120);
    }
    return baseFrames;
  }
}
