using System.Collections.Generic;
using UnityEngine;

public readonly struct WarmRequest {
  public readonly WarmContext context;
  public readonly GearController playerController;
  public readonly EnemyController[] criticalEnemyControllers;
  public readonly EnemyController[] enemyControllers;
  public readonly Dictionary<string, GameObject> enemyArchetypePrefabsByType;
  public readonly float timeoutSeconds;
  public readonly float requiredReadyRatio;
  public readonly int playerWarmFrames;
  public readonly int enemyWarmFrames;
  public readonly int effectWarmFrames;
  public readonly int maxRequestedAddresses;
  public readonly bool includeEffects;
  public readonly List<string> extraCriticalLibraries;
  public readonly List<string> extraCriticalAddresses;
  public readonly List<string> extraWarmLibraries;
  public readonly List<string> extraWarmAddresses;
  public readonly List<string> extraCriticalAssetAddresses;
  public readonly List<string> extraWarmAssetAddresses;
  public readonly float hardTimeoutSeconds;
  public readonly bool allowHardTimeoutBypass;
  public readonly string idempotencyToken;
  public readonly bool skipIfTokenAlreadyWarm;
  public readonly List<string> extraCriticalLabels;
  public readonly List<string> extraWarmLabels;
  public readonly List<string> extraCriticalAssetLabels;
  public readonly List<string> extraWarmAssetLabels;
  public readonly List<string> criticalPlayerEffectKeys;
  public readonly bool allowCriticalReadySoftTimeout;

  public WarmRequest(
    WarmContext context,
    GearController playerController = null,
    EnemyController[] criticalEnemyControllers = null,
    EnemyController[] enemyControllers = null,
    Dictionary<string, GameObject> enemyArchetypePrefabsByType = null,
    float timeoutSeconds = 2.5f,
    float requiredReadyRatio = 0.95f,
    int playerWarmFrames = 4,
    int enemyWarmFrames = 2,
    int effectWarmFrames = 1,
    int maxRequestedAddresses = 131072,
    bool includeEffects = true,
    List<string> extraCriticalLibraries = null,
    List<string> extraCriticalAddresses = null,
    List<string> extraWarmLibraries = null,
    List<string> extraWarmAddresses = null,
    List<string> extraCriticalAssetAddresses = null,
    List<string> extraWarmAssetAddresses = null,
    float hardTimeoutSeconds = 6.0f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = false,
    List<string> extraCriticalLabels = null,
    List<string> extraWarmLabels = null,
    List<string> extraCriticalAssetLabels = null,
    List<string> extraWarmAssetLabels = null,
    List<string> criticalPlayerEffectKeys = null,
    bool allowCriticalReadySoftTimeout = true
  ) {
    this.context = context;
    this.playerController = playerController;
    this.criticalEnemyControllers = criticalEnemyControllers;
    this.enemyControllers = enemyControllers;
    this.enemyArchetypePrefabsByType = enemyArchetypePrefabsByType;
    this.timeoutSeconds = timeoutSeconds;
    this.requiredReadyRatio = requiredReadyRatio;
    this.playerWarmFrames = playerWarmFrames;
    this.enemyWarmFrames = enemyWarmFrames;
    this.effectWarmFrames = effectWarmFrames;
    this.maxRequestedAddresses = maxRequestedAddresses;
    this.includeEffects = includeEffects;
    this.extraCriticalLibraries = extraCriticalLibraries;
    this.extraCriticalAddresses = extraCriticalAddresses;
    this.extraWarmLibraries = extraWarmLibraries;
    this.extraWarmAddresses = extraWarmAddresses;
    this.extraCriticalAssetAddresses = extraCriticalAssetAddresses;
    this.extraWarmAssetAddresses = extraWarmAssetAddresses;
    this.hardTimeoutSeconds = hardTimeoutSeconds;
    this.allowHardTimeoutBypass = allowHardTimeoutBypass;
    this.idempotencyToken = idempotencyToken;
    this.skipIfTokenAlreadyWarm = skipIfTokenAlreadyWarm;
    this.extraCriticalLabels = extraCriticalLabels;
    this.extraWarmLabels = extraWarmLabels;
    this.extraCriticalAssetLabels = extraCriticalAssetLabels;
    this.extraWarmAssetLabels = extraWarmAssetLabels;
    this.criticalPlayerEffectKeys = criticalPlayerEffectKeys;
    this.allowCriticalReadySoftTimeout = allowCriticalReadySoftTimeout;
  }

  public static WarmRequest CreateStartGame(
    GearController playerController,
    EnemyController[] criticalEnemyControllers = null,
    EnemyController[] enemyControllers = null,
    Dictionary<string, GameObject> enemyArchetypePrefabsByType = null,
    float timeoutSeconds = 3.0f,
    float requiredReadyRatio = 0.95f,
    List<string> extraCriticalLibraries = null,
    List<string> extraCriticalAddresses = null,
    List<string> extraWarmLibraries = null,
    List<string> extraWarmAddresses = null,
    List<string> extraCriticalAssetAddresses = null,
    List<string> extraWarmAssetAddresses = null,
    float hardTimeoutSeconds = 6.0f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = false,
    List<string> extraCriticalLabels = null,
    List<string> extraWarmLabels = null,
    List<string> extraCriticalAssetLabels = null,
    List<string> extraWarmAssetLabels = null,
    List<string> criticalPlayerEffectKeys = null,
    bool allowCriticalReadySoftTimeout = false
  ) {
    return new WarmRequest(
      context: WarmContext.StartGame,
      playerController: playerController,
      criticalEnemyControllers: criticalEnemyControllers,
      enemyControllers: enemyControllers,
      enemyArchetypePrefabsByType: enemyArchetypePrefabsByType,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: requiredReadyRatio,
      playerWarmFrames: SpriteStreamingRuntimeSettings.LoadingPlayerWarmFrames,
      enemyWarmFrames: SpriteStreamingRuntimeSettings.LoadingEnemyWarmFrames,
      effectWarmFrames: SpriteStreamingRuntimeSettings.LoadingEffectWarmFrames,
      maxRequestedAddresses: 262144,
      includeEffects: true,
      extraCriticalLibraries: extraCriticalLibraries,
      extraCriticalAddresses: extraCriticalAddresses,
      extraWarmLibraries: extraWarmLibraries,
      extraWarmAddresses: extraWarmAddresses,
      extraCriticalAssetAddresses: extraCriticalAssetAddresses,
      extraWarmAssetAddresses: extraWarmAssetAddresses,
      hardTimeoutSeconds: hardTimeoutSeconds,
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: idempotencyToken,
      skipIfTokenAlreadyWarm: skipIfTokenAlreadyWarm,
      extraCriticalLabels: extraCriticalLabels,
      extraWarmLabels: extraWarmLabels,
      extraCriticalAssetLabels: extraCriticalAssetLabels,
      extraWarmAssetLabels: extraWarmAssetLabels,
      criticalPlayerEffectKeys: criticalPlayerEffectKeys,
      allowCriticalReadySoftTimeout: allowCriticalReadySoftTimeout
    );
  }

  public static WarmRequest CreateLoadSave(
    GearController playerController,
    EnemyController[] criticalEnemyControllers = null,
    EnemyController[] enemyControllers = null,
    Dictionary<string, GameObject> enemyArchetypePrefabsByType = null,
    float timeoutSeconds = 3.5f,
    float requiredReadyRatio = 0.95f,
    List<string> extraCriticalLibraries = null,
    List<string> extraCriticalAddresses = null,
    List<string> extraWarmLibraries = null,
    List<string> extraWarmAddresses = null,
    List<string> extraCriticalAssetAddresses = null,
    List<string> extraWarmAssetAddresses = null,
    float hardTimeoutSeconds = 6.5f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = false,
    List<string> extraCriticalLabels = null,
    List<string> extraWarmLabels = null,
    List<string> extraCriticalAssetLabels = null,
    List<string> extraWarmAssetLabels = null,
    List<string> criticalPlayerEffectKeys = null,
    bool allowCriticalReadySoftTimeout = false
  ) {
    return new WarmRequest(
      context: WarmContext.LoadSave,
      playerController: playerController,
      criticalEnemyControllers: criticalEnemyControllers,
      enemyControllers: enemyControllers,
      enemyArchetypePrefabsByType: enemyArchetypePrefabsByType,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: requiredReadyRatio,
      playerWarmFrames: SpriteStreamingRuntimeSettings.LoadingPlayerWarmFrames,
      enemyWarmFrames: SpriteStreamingRuntimeSettings.LoadingEnemyWarmFrames,
      effectWarmFrames: SpriteStreamingRuntimeSettings.LoadingEffectWarmFrames,
      maxRequestedAddresses: 262144,
      includeEffects: true,
      extraCriticalLibraries: extraCriticalLibraries,
      extraCriticalAddresses: extraCriticalAddresses,
      extraWarmLibraries: extraWarmLibraries,
      extraWarmAddresses: extraWarmAddresses,
      extraCriticalAssetAddresses: extraCriticalAssetAddresses,
      extraWarmAssetAddresses: extraWarmAssetAddresses,
      hardTimeoutSeconds: hardTimeoutSeconds,
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: idempotencyToken,
      skipIfTokenAlreadyWarm: skipIfTokenAlreadyWarm,
      extraCriticalLabels: extraCriticalLabels,
      extraWarmLabels: extraWarmLabels,
      extraCriticalAssetLabels: extraCriticalAssetLabels,
      extraWarmAssetLabels: extraWarmAssetLabels,
      criticalPlayerEffectKeys: criticalPlayerEffectKeys,
      allowCriticalReadySoftTimeout: allowCriticalReadySoftTimeout
    );
  }

  public static WarmRequest CreateGearApplyReturn(
    GearController playerController,
    float timeoutSeconds = 2.0f,
    float requiredReadyRatio = 0.95f,
    List<string> extraCriticalLibraries = null,
    List<string> extraCriticalAddresses = null,
    List<string> extraWarmLibraries = null,
    List<string> extraWarmAddresses = null,
    List<string> extraCriticalAssetAddresses = null,
    List<string> extraWarmAssetAddresses = null,
    float hardTimeoutSeconds = 4.5f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = false,
    List<string> extraCriticalLabels = null,
    List<string> extraWarmLabels = null,
    List<string> extraCriticalAssetLabels = null,
    List<string> extraWarmAssetLabels = null,
    List<string> criticalPlayerEffectKeys = null,
    bool allowCriticalReadySoftTimeout = false
  ) {
    return new WarmRequest(
      context: WarmContext.GearApplyReturn,
      playerController: playerController,
      criticalEnemyControllers: null,
      enemyControllers: null,
      enemyArchetypePrefabsByType: null,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: requiredReadyRatio,
      playerWarmFrames: 1,
      enemyWarmFrames: 0,
      effectWarmFrames: 1,
      maxRequestedAddresses: 131072,
      includeEffects: true,
      extraCriticalLibraries: extraCriticalLibraries,
      extraCriticalAddresses: extraCriticalAddresses,
      extraWarmLibraries: extraWarmLibraries,
      extraWarmAddresses: extraWarmAddresses,
      extraCriticalAssetAddresses: extraCriticalAssetAddresses,
      extraWarmAssetAddresses: extraWarmAssetAddresses,
      hardTimeoutSeconds: hardTimeoutSeconds,
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: idempotencyToken,
      skipIfTokenAlreadyWarm: skipIfTokenAlreadyWarm,
      extraCriticalLabels: extraCriticalLabels,
      extraWarmLabels: extraWarmLabels,
      extraCriticalAssetLabels: extraCriticalAssetLabels,
      extraWarmAssetLabels: extraWarmAssetLabels,
      criticalPlayerEffectKeys: criticalPlayerEffectKeys,
      allowCriticalReadySoftTimeout: allowCriticalReadySoftTimeout
    );
  }

  public static WarmRequest CreateEnemyWaveSpawn(
    Dictionary<string, GameObject> enemyArchetypePrefabsByType,
    float timeoutSeconds = 2.0f,
    float requiredReadyRatio = 0.95f,
    int enemyWarmFrames = 2,
    List<string> extraCriticalLibraries = null,
    List<string> extraCriticalAddresses = null,
    List<string> extraWarmLibraries = null,
    List<string> extraWarmAddresses = null,
    List<string> extraCriticalAssetAddresses = null,
    List<string> extraWarmAssetAddresses = null,
    float hardTimeoutSeconds = 4.5f,
    bool allowHardTimeoutBypass = true,
    string idempotencyToken = "",
    bool skipIfTokenAlreadyWarm = true,
    List<string> extraCriticalLabels = null,
    List<string> extraWarmLabels = null,
    List<string> extraCriticalAssetLabels = null,
    List<string> extraWarmAssetLabels = null,
    List<string> criticalPlayerEffectKeys = null,
    bool allowCriticalReadySoftTimeout = true
  ) {
    return new WarmRequest(
      context: WarmContext.EnemyWaveSpawn,
      playerController: null,
      criticalEnemyControllers: null,
      enemyControllers: null,
      enemyArchetypePrefabsByType: enemyArchetypePrefabsByType,
      timeoutSeconds: timeoutSeconds,
      requiredReadyRatio: requiredReadyRatio,
      playerWarmFrames: 0,
      enemyWarmFrames: enemyWarmFrames,
      effectWarmFrames: 2,
      maxRequestedAddresses: 262144,
      includeEffects: true,
      extraCriticalLibraries: extraCriticalLibraries,
      extraCriticalAddresses: extraCriticalAddresses,
      extraWarmLibraries: extraWarmLibraries,
      extraWarmAddresses: extraWarmAddresses,
      extraCriticalAssetAddresses: extraCriticalAssetAddresses,
      extraWarmAssetAddresses: extraWarmAssetAddresses,
      hardTimeoutSeconds: hardTimeoutSeconds,
      allowHardTimeoutBypass: allowHardTimeoutBypass,
      idempotencyToken: idempotencyToken,
      skipIfTokenAlreadyWarm: skipIfTokenAlreadyWarm,
      extraCriticalLabels: extraCriticalLabels,
      extraWarmLabels: extraWarmLabels,
      extraCriticalAssetLabels: extraCriticalAssetLabels,
      extraWarmAssetLabels: extraWarmAssetLabels,
      criticalPlayerEffectKeys: criticalPlayerEffectKeys,
      allowCriticalReadySoftTimeout: allowCriticalReadySoftTimeout
    );
  }
}
