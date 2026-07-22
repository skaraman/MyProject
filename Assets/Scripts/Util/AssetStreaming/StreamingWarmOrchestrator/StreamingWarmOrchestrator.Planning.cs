using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class StreamingWarmOrchestrator : MonoBehaviour, IStreamingWarmOrchestrator {
  enum WarmPlanSliceAction {
    Continue = 0,
    Yield = 1,
    Stop = 2
  }

  sealed class WarmPlanSliceBudget {
    readonly float maxSliceSeconds;
    readonly int maxWorkItems;
    float sliceStartedAt;
    int sliceWorkItems;

    public int YieldedFrames { get; private set; }
    public int TotalWorkItems { get; private set; }
    public float MaxObservedSliceSeconds { get; private set; }

    public WarmPlanSliceBudget(float maxSliceSeconds, int maxWorkItems) {
      this.maxSliceSeconds = Mathf.Max(maxSliceSeconds, 0.001f);
      this.maxWorkItems = Math.Max(maxWorkItems, 1);
      Reset();
    }

    public bool Consume() {
      TotalWorkItems++;
      sliceWorkItems++;
      return sliceWorkItems >= maxWorkItems || Time.realtimeSinceStartup - sliceStartedAt >= maxSliceSeconds;
    }

    public void RecordYield() {
      var elapsed = Mathf.Max(Time.realtimeSinceStartup - sliceStartedAt, 0f);
      if (elapsed > MaxObservedSliceSeconds) {
        MaxObservedSliceSeconds = elapsed;
      }
      YieldedFrames++;
    }

    public void Reset() {
      sliceStartedAt = Time.realtimeSinceStartup;
      sliceWorkItems = 0;
    }
  }

  IEnumerator BuildWarmPlanRoutine(
    WarmRequest request,
    bool includeResolvedAddressSweeps,
    bool includeStaticSeedWork,
    float deadlineAt,
    bool debugLogs
  ) {
    var budget = new WarmPlanSliceBudget(WarmPlanSliceBudgetSeconds, WarmPlanSliceWorkItemBudget);

    if (includeStaticSeedWork) {
      AddLibraries(request.extraCriticalLibraries, markCritical: true);
      AddLibraries(request.extraWarmLibraries, markCritical: false);
      CollectLabels(request.extraCriticalLabels, markCritical: true);
      CollectLabels(request.extraWarmLabels, markCritical: false);
      CollectLabels(SpriteStreamingRuntimeSettings.CriticalAddressableLabels, markCritical: true);
      CollectLabels(SpriteStreamingRuntimeSettings.WarmAddressableLabels, markCritical: false);
      CollectLabels(SpriteStreamingRuntimeSettings.WarmUiAddressableLabels, markCritical: false);
      AddDirectAddresses(request.extraCriticalAddresses, markCritical: true, markHighPriority: true);
      AddDirectAddresses(request.extraWarmAddresses, markCritical: false, markHighPriority: false);
      if (ShouldAbortWarmPlanPass(request, includeResolvedAddressSweeps, includeStaticSeedWork, budget, deadlineAt, debugLogs)) yield break;
    }

    if (request.playerController != null) {
      yield return CollectPlayerWarmPlan(
        request.playerController,
        request.playerWarmFrames,
        request.effectWarmFrames,
        request.includeEffects,
        includeResolvedAddressSweeps,
        includeStaticSeedWork,
        request.criticalPlayerEffectKeys,
        budget,
        deadlineAt
      );
      if (ShouldAbortWarmPlanPass(request, includeResolvedAddressSweeps, includeStaticSeedWork, budget, deadlineAt, debugLogs)) yield break;
    }

    if (request.criticalEnemyControllers != null && request.criticalEnemyControllers.Length > 0) {
      for (var i = 0; i < request.criticalEnemyControllers.Length; i++) {
        yield return CollectEnemyControllerWarmPlan(
          request.criticalEnemyControllers[i],
          request.enemyWarmFrames,
          request.effectWarmFrames,
          request.includeEffects,
          includeResolvedAddressSweeps,
          includeStaticSeedWork,
          markCritical: true,
          budget: budget,
          deadlineAt: deadlineAt
        );
        if (ShouldAbortWarmPlanPass(request, includeResolvedAddressSweeps, includeStaticSeedWork, budget, deadlineAt, debugLogs)) yield break;
      }
    }

    if (request.enemyControllers != null && request.enemyControllers.Length > 0) {
      for (var i = 0; i < request.enemyControllers.Length; i++) {
        yield return CollectEnemyControllerWarmPlan(
          request.enemyControllers[i],
          request.enemyWarmFrames,
          request.effectWarmFrames,
          request.includeEffects,
          includeResolvedAddressSweeps,
          includeStaticSeedWork,
          markCritical: false,
          budget: budget,
          deadlineAt: deadlineAt
        );
        if (ShouldAbortWarmPlanPass(request, includeResolvedAddressSweeps, includeStaticSeedWork, budget, deadlineAt, debugLogs)) yield break;
      }
    }

    if (request.enemyArchetypePrefabsByType != null && request.enemyArchetypePrefabsByType.Count > 0) {
      yield return CollectEnemyArchetypeWarmPlan(
        request.enemyArchetypePrefabsByType,
        request.enemyWarmFrames,
        request.effectWarmFrames,
        request.includeEffects,
        includeResolvedAddressSweeps,
        includeStaticSeedWork,
        budget,
        deadlineAt
      );
      if (ShouldAbortWarmPlanPass(request, includeResolvedAddressSweeps, includeStaticSeedWork, budget, deadlineAt, debugLogs)) yield break;
    }

    LogWarmPlanPassSummary(request, includeResolvedAddressSweeps, includeStaticSeedWork, budget, deadlineHit: false, debugLogs: debugLogs);
  }

  IEnumerator CollectPlayerWarmPlan(
    GearController controller,
    int warmFrames,
    int effectWarmFrames,
    bool includeEffects,
    bool includeResolvedAddressSweeps,
    bool includeStaticSeedWork,
    List<string> criticalPlayerEffectKeys,
    WarmPlanSliceBudget budget,
    float deadlineAt
  ) {
    if (controller == null || HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    var playerAnimationManifest = Animations.Esperanza;

    if (includeStaticSeedWork) {
      yield return AddLibrariesFromGameObjects(controller.SkinObjects, budget, deadlineAt);
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      yield return AddLibrariesFromGameObjects(controller.GearObjects, budget, deadlineAt);
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      if (includeEffects && controller.effectNode != null) {
        AddLibrary(controller.effectNode.libraryName);
      }
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    }

    if (!includeResolvedAddressSweeps) yield break;

    yield return CollectAtlasSeedAddressesForObjects(controller.SkinObjects, playerAnimationManifest, warmFrames, playerWarmAtlasSeedAddresses, budget, deadlineAt, allowInactive: true, isPlayer: true);
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    yield return CollectAtlasSeedAddressesForObjects(controller.GearObjects, playerAnimationManifest, warmFrames, playerWarmAtlasSeedAddresses, budget, deadlineAt, allowInactive: true, isPlayer: true);
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    yield return CollectAnimationStartsForObjects(controller.SkinObjects, playerAnimationManifest, warmFrames, markCritical: false, budget: budget, deadlineAt: deadlineAt, allowInactive: true, isPlayer: true);
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    yield return CollectAnimationStartsForObjects(controller.GearObjects, playerAnimationManifest, warmFrames, markCritical: false, budget: budget, deadlineAt: deadlineAt, allowInactive: true, isPlayer: true);
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    if (!includeEffects || controller.effectNode == null) yield break;

    var criticalEffectKeySet = BuildNormalizedTokenSet(criticalPlayerEffectKeys);
    if (criticalEffectKeySet == null || criticalEffectKeySet.Count <= 0) yield break;

    yield return CollectEffectStartsForTarget(
      controller.effectNode,
      Effects.Esperanza,
      effectWarmFrames,
      markCritical: false,
      budget: budget,
      deadlineAt: deadlineAt,
      allowInactive: true,
      includedEffectKeys: criticalEffectKeySet
    );
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    yield return CollectEffectStartsForTarget(
      controller.effectNode,
      Effects.Things,
      effectWarmFrames,
      markCritical: false,
      budget: budget,
      deadlineAt: deadlineAt,
      allowInactive: true,
      includedEffectKeys: criticalEffectKeySet
    );
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    yield return CollectEffectStartsForTarget(
      controller.effectNode,
      Effects.Imp,
      effectWarmFrames,
      markCritical: false,
      budget: budget,
      deadlineAt: deadlineAt,
      allowInactive: true,
      includedEffectKeys: criticalEffectKeySet
    );
  }

  IEnumerator CollectEnemyControllerWarmPlan(
    EnemyController controller,
    int warmFrames,
    int effectWarmFrames,
    bool includeEffects,
    bool includeResolvedAddressSweeps,
    bool includeStaticSeedWork,
    bool markCritical,
    WarmPlanSliceBudget budget,
    float deadlineAt
  ) {
    if (controller == null || controller.spriteObjects == null || controller.spriteObjects.Length == 0) yield break;
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    var enemyType = NormalizeToken(controller.enemyType);
    if (string.IsNullOrWhiteSpace(enemyType)) yield break;
    if (!Animations.Enemies.TryGetValue(enemyType, out var enemyAnimations) || enemyAnimations == null || enemyAnimations.Count == 0) yield break;

    if (includeStaticSeedWork) {
      yield return AddLibrariesFromGameObjects(controller.spriteObjects, budget, deadlineAt);
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      if (includeEffects && controller.effectNode != null) {
        AddLibrary(controller.effectNode.libraryName);
      }
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
    }

    if (!includeResolvedAddressSweeps) yield break;

    yield return CollectAnimationStartsForObjects(controller.spriteObjects, enemyAnimations, warmFrames, markCritical, budget, deadlineAt, allowInactive: true);
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    if (includeEffects && controller.effectNode != null) {
      yield return CollectEffectStartsForTarget(
        controller.effectNode,
        ResolveEnemyEffectAnimations(enemyType),
        effectWarmFrames,
        markCritical,
        budget,
        deadlineAt,
        allowInactive: true
      );
    }
  }

  IEnumerator CollectEnemyArchetypeWarmPlan(
    Dictionary<string, GameObject> enemyArchetypePrefabsByType,
    int warmFrames,
    int effectWarmFrames,
    bool includeEffects,
    bool includeResolvedAddressSweeps,
    bool includeStaticSeedWork,
    WarmPlanSliceBudget budget,
    float deadlineAt
  ) {
    if (enemyArchetypePrefabsByType == null || enemyArchetypePrefabsByType.Count == 0) yield break;
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    foreach (var pair in enemyArchetypePrefabsByType) {
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      var enemyType = NormalizeToken(pair.Key);
      var root = pair.Value;
      if (string.IsNullOrWhiteSpace(enemyType) || root == null) continue;
      if (!Animations.Enemies.TryGetValue(enemyType, out var enemyAnimations) || enemyAnimations == null || enemyAnimations.Count == 0) continue;

      var targets = GetCachedArchetypeTargets(root);
      var sliceAction = NoteWarmPlanWork(budget, deadlineAt);
      if (sliceAction == WarmPlanSliceAction.Stop) yield break;
      if (sliceAction == WarmPlanSliceAction.Yield) {
        budget.RecordYield();
        TextureResidencyCache.Pump();
        yield return null;
        budget.Reset();
      }

      for (var i = 0; i < targets.Length; i++) {
        if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
        var target = targets[i];
        if (!IsTargetWarmable(target, allowInactive: true)) continue;
        if (includeStaticSeedWork) {
          AddLibrary(target.libraryName);
        }
        if (!includeResolvedAddressSweeps) {
          sliceAction = NoteWarmPlanWork(budget, deadlineAt);
          if (sliceAction == WarmPlanSliceAction.Stop) yield break;
          if (sliceAction == WarmPlanSliceAction.Yield) {
            budget.RecordYield();
            TextureResidencyCache.Pump();
            yield return null;
            budget.Reset();
          }
          continue;
        }
        yield return CollectAnimationStartsForTarget(target, enemyAnimations, warmFrames, markCritical: false, budget: budget, deadlineAt: deadlineAt, allowInactive: true);
      }

      if (!includeEffects || !includeResolvedAddressSweeps) continue;
      var effectAnimations = ResolveEnemyEffectAnimations(enemyType);
      if (effectAnimations == null || effectAnimations.Count == 0) continue;
      for (var i = 0; i < targets.Length; i++) {
        if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
        var target = targets[i];
        if (!IsTargetWarmable(target, allowInactive: true)) continue;
        yield return CollectEffectStartsForTarget(target, effectAnimations, effectWarmFrames, markCritical: false, budget: budget, deadlineAt: deadlineAt, allowInactive: true);
      }
    }
  }

  IEnumerator CollectAnimationStartsForObjects(
    GameObject[] objects,
    Dictionary<string, AnimData> animations,
    int warmFrames,
    bool markCritical,
    WarmPlanSliceBudget budget,
    float deadlineAt,
    bool allowInactive = false,
    bool isPlayer = false
  ) {
    if (objects == null || objects.Length == 0) yield break;
    if (animations == null || animations.Count == 0) yield break;
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    var clampedWarmFrames = Mathf.Max(warmFrames, 1);
    for (var i = 0; i < objects.Length; i++) {
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      var go = objects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (!IsTargetWarmable(target, allowInactive)) continue;
      yield return CollectAnimationStartsForTarget(target, animations, clampedWarmFrames, markCritical, budget, deadlineAt, allowInactive, isPlayer);
    }
  }

  IEnumerator CollectAnimationStartsForTarget(
    SpriteWithNormals target,
    Dictionary<string, AnimData> animations,
    int warmFrames,
    bool markCritical,
    WarmPlanSliceBudget budget,
    float deadlineAt,
    bool allowInactive = false,
    bool isPlayer = false
  ) {
    if (!IsTargetWarmable(target, allowInactive)) yield break;
    if (animations == null || animations.Count == 0) yield break;

    if (!target.IsAnimation) {
      if (TryGetFrameAddressPairBudgeted(target, 0, out var staticPair, categoryOverride: null)) {
        AddPairAddresses(staticPair, markCritical);
      }
      yield break;
    }

    foreach (var pair in animations) {
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      var animationName = pair.Key;
      var anim = pair.Value;
      if (anim == null || string.IsNullOrWhiteSpace(animationName)) continue;
      if (isPlayer && !IsCorePlayerWarmAnimation(animationName)) continue;

      var category = ResolveAnimationCategory(animationName, anim);
      var clipStart = Mathf.Max(anim.start, 1);
      var clipEnd = Mathf.Max(anim.end, clipStart);

      var targetWarmFrames = warmFrames;
      if (isPlayer) {
        bool isLocomotion = string.Equals(animationName, "Breathe", StringComparison.Ordinal) ||
                            string.Equals(animationName, "Walk", StringComparison.Ordinal) ||
                            string.Equals(animationName, "Stance", StringComparison.Ordinal);
        if (isLocomotion) {
          targetWarmFrames = 8;
        }
      }

      var frameEnd = Mathf.Min(clipEnd, clipStart + Mathf.Max(targetWarmFrames, 1) - 1);
      for (var frame = clipStart; frame <= frameEnd; frame++) {
        if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
        if (TryGetFrameAddressPairBudgeted(target, frame, out var addressPair, category)) {
          AddPairAddresses(addressPair, markCritical);
        }

        var sliceAction = NoteWarmPlanWork(budget, deadlineAt);
        if (sliceAction == WarmPlanSliceAction.Stop) yield break;
        if (sliceAction == WarmPlanSliceAction.Yield) {
          budget.RecordYield();
          TextureResidencyCache.Pump();
          yield return null;
          budget.Reset();
        }
      }
    }
  }

  IEnumerator CollectEffectStartsForTarget(
    SpriteWithNormals target,
    Dictionary<string, EffectData> effects,
    int warmFrames,
    bool markCritical,
    WarmPlanSliceBudget budget,
    float deadlineAt,
    bool allowInactive = false,
    ISet<string> includedEffectKeys = null,
    ISet<string> excludedEffectKeys = null
  ) {
    if (!IsTargetWarmable(target, allowInactive)) yield break;
    if (effects == null || effects.Count == 0) yield break;

    foreach (var pair in effects) {
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      var effectName = pair.Key;
      var effect = pair.Value;
      if (effect == null || string.IsNullOrWhiteSpace(effectName)) continue;
      var normalizedEffectName = NormalizeToken(effectName);
      if (includedEffectKeys != null &&
          includedEffectKeys.Count > 0 &&
          !includedEffectKeys.Contains(normalizedEffectName)) {
        continue;
      }
      if (excludedEffectKeys != null && excludedEffectKeys.Contains(normalizedEffectName)) {
        continue;
      }
      var clipStart = Mathf.Max(effect.start, 1);
      var clipEnd = Mathf.Max(effect.end, clipStart);
      var frameEnd = Mathf.Min(clipEnd, clipStart + Mathf.Max(warmFrames, 1) - 1);
      for (var frame = clipStart; frame <= frameEnd; frame++) {
        if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
        if (TryGetFrameAddressPairBudgeted(target, frame, out var addressPair, effectName)) {
          AddPairAddresses(addressPair, markCritical);
        }

        var sliceAction = NoteWarmPlanWork(budget, deadlineAt);
        if (sliceAction == WarmPlanSliceAction.Stop) yield break;
        if (sliceAction == WarmPlanSliceAction.Yield) {
          budget.RecordYield();
          TextureResidencyCache.Pump();
          yield return null;
          budget.Reset();
        }
      }
    }
  }

  ISet<string> BuildNormalizedTokenSet(List<string> values) {
    if (values == null || values.Count <= 0) return null;
    var set = normalizedTokenSetScratch;
    set.Clear();
    for (var i = 0; i < values.Count; i++) {
      var normalized = NormalizeToken(values[i]);
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      set.Add(normalized);
    }
    return set.Count > 0 ? set : null;
  }

  bool TryGetFrameAddressPairBudgeted(SpriteWithNormals target, int frame, out SpriteAddressPair pair, string categoryOverride = null) {
    pair = default;
    if (target == null) return false;
    if (HasReachedWarmAddressCap()) return false;
    if (warmPlanFrameAddressProbeCount >= activeFrameAddressProbeBudget) {
      return false;
    }
    warmPlanFrameAddressProbeCount++;
    return target.TryGetFrameAddressPair(frame, out pair, categoryOverride);
  }

  void AddPairAddresses(SpriteAddressPair pair, bool markCritical) {
    if (!AddReadyAddress(pair.StreamingColorAddress, markCritical, markCritical)) return;
    AddWarmAddress(pair.StreamingNormalAddress, markHighPriority: false);
  }

  bool AddReadyAddress(string address, bool markCritical, bool markHighPriority) {
    if (!AddWarmAddress(address, markHighPriority)) return false;
    var normalized = NormalizeToken(address);
    if (string.IsNullOrWhiteSpace(normalized)) return false;
    readyAddressSet.Add(normalized);
    if (markCritical &&
        !criticalReadyAddressSet.Contains(normalized) &&
        activeCriticalReadyAddressCap > 0 &&
        criticalReadyAddressSet.Count >= activeCriticalReadyAddressCap) {
      markCritical = false;
      warmPlanDroppedCriticalReadyAddresses++;
    }
    if (markCritical) criticalReadyAddressSet.Add(normalized);
    return true;
  }

  bool AddWarmAddress(string address, bool markHighPriority) {
    var normalized = NormalizeToken(address);
    if (string.IsNullOrWhiteSpace(normalized)) return false;

    var alreadyScheduled = warmAddressSet.Contains(normalized);
    if (!alreadyScheduled && HasReachedWarmAddressCap()) {
      warmPlanDroppedAddresses++;
      return false;
    }
    if (!alreadyScheduled) {
      warmAddressSet.Add(normalized);
    }
    if (markHighPriority) {
      MarkAddressHighPriority(normalized);
    }
    return true;
  }

  void MarkAddressHighPriority(string normalizedAddress) {
    if (string.IsNullOrWhiteSpace(normalizedAddress)) return;
    if (highPriorityAddressSet.Contains(normalizedAddress)) return;
    if (activeHighPriorityAddressCap > 0 &&
        highPriorityAddressSet.Count >= activeHighPriorityAddressCap) {
      warmPlanDroppedHighPriorityAddresses++;
      return;
    }
    highPriorityAddressSet.Add(normalizedAddress);
  }

  bool HasReachedWarmAddressCap() {
    if (activeMaxRequestedAddresses <= 0) return false;
    if (warmAddressSet.Count < activeMaxRequestedAddresses) return false;
    return true;
  }

  static int CountReadyAddresses(HashSet<string> addresses, bool pumpEntries) {
    if (addresses == null || addresses.Count == 0) return 0;
    var count = 0;
    foreach (var address in addresses) {
      if (TextureResidencyCache.IsReady(address, pumpEntries)) count++;
    }
    return count;
  }

  IEnumerator CollectAtlasSeedAddressesForObjects(
    GameObject[] objects,
    Dictionary<string, AnimData> animations,
    int warmFrames,
    HashSet<string> seedSet,
    WarmPlanSliceBudget budget,
    float deadlineAt,
    bool allowInactive = false,
    bool isPlayer = false
  ) {
    if (objects == null || objects.Length == 0) yield break;
    if (animations == null || animations.Count == 0) yield break;
    if (seedSet == null) yield break;
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    var clampedWarmFrames = Mathf.Max(warmFrames, 1);
    for (var i = 0; i < objects.Length; i++) {
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      var go = objects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (!IsTargetWarmable(target, allowInactive)) continue;
      yield return CollectAtlasSeedAddressesForTarget(target, animations, clampedWarmFrames, seedSet, budget, deadlineAt, allowInactive, isPlayer);
    }
  }

  IEnumerator CollectAtlasSeedAddressesForTarget(
    SpriteWithNormals target,
    Dictionary<string, AnimData> animations,
    int warmFrames,
    HashSet<string> seedSet,
    WarmPlanSliceBudget budget,
    float deadlineAt,
    bool allowInactive = false,
    bool isPlayer = false
  ) {
    if (!IsTargetWarmable(target, allowInactive)) yield break;
    if (animations == null || animations.Count == 0 || seedSet == null) yield break;
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    if (!target.IsAnimation) {
      if (TryGetFrameAddressPairBudgeted(target, 0, out var staticPair, categoryOverride: null)) {
        AddAtlasSeedAddress(staticPair.StreamingColorAddress, seedSet);
        AddAtlasSeedAddress(staticPair.StreamingNormalAddress, seedSet);
      }
      yield break;
    }

    foreach (var pair in animations) {
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      var animationName = pair.Key;
      var anim = pair.Value;
      if (anim == null || string.IsNullOrWhiteSpace(animationName)) continue;
      if (isPlayer && !IsCorePlayerWarmAnimation(animationName)) continue;

      var targetWarmFrames = warmFrames;
      if (isPlayer) {
        bool isLocomotion = string.Equals(animationName, "Breathe", StringComparison.Ordinal) ||
                            string.Equals(animationName, "Walk", StringComparison.Ordinal) ||
                            string.Equals(animationName, "Stance", StringComparison.Ordinal);
        if (isLocomotion) {
          targetWarmFrames = 8;
        }
      }
      var requestedSamples = Mathf.Clamp(Mathf.Max(targetWarmFrames, 1), 1, 3);

      var category = ResolveAnimationCategory(animationName, anim);
      var clipStart = Mathf.Max(anim.start, 1);
      var clipEnd = Mathf.Max(anim.end, clipStart);
      var clipLength = Mathf.Max(clipEnd - clipStart + 1, 1);
      var sampleCount = Mathf.Clamp(requestedSamples, 1, clipLength);
      var sampleDenominator = Mathf.Max(sampleCount - 1, 1);
      var lastFrame = int.MinValue;

      for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++) {
        if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
        var frame = sampleCount <= 1
          ? clipStart
          : Mathf.RoundToInt(Mathf.Lerp(clipStart, clipEnd, sampleIndex / (float)sampleDenominator));
        frame = Mathf.Clamp(frame, clipStart, clipEnd);
        if (frame == lastFrame) continue;
        lastFrame = frame;

        if (TryGetFrameAddressPairBudgeted(target, frame, out var addressPair, category)) {
          AddAtlasSeedAddress(addressPair.StreamingColorAddress, seedSet);
          AddAtlasSeedAddress(addressPair.StreamingNormalAddress, seedSet);
        }

        var sliceAction = NoteWarmPlanWork(budget, deadlineAt);
        if (sliceAction == WarmPlanSliceAction.Stop) yield break;
        if (sliceAction == WarmPlanSliceAction.Yield) {
          budget.RecordYield();
          TextureResidencyCache.Pump();
          yield return null;
          budget.Reset();
        }
      }
    }
  }

  static void AddAtlasSeedAddress(string address, HashSet<string> seedSet) {
    if (seedSet == null || string.IsNullOrWhiteSpace(address)) return;
    seedSet.Add(address.Trim());
  }

  IEnumerator ExpandPlayerAtlasSeedsRoutine(WarmContext context, float deadlineAt, bool debugLogs) {
    if (!SpriteStreamingRuntimeSettings.EnableAtlasExpansionOnSliceRequest) yield break;
    if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;

    var budget = new WarmPlanSliceBudget(WarmPlanSliceBudgetSeconds, WarmPlanSliceWorkItemBudget);
    yield return ExpandAtlasSeedSet(playerWarmAtlasSeedAddresses, markHighPriority: false, budget: budget, deadlineAt: deadlineAt);
    LogAtlasSeedExpansionSummary(context, budget, deadlineHit: HasWarmPlanDeadlineElapsed(deadlineAt), debugLogs: debugLogs);
  }

  IEnumerator ExpandAtlasSeedSet(HashSet<string> seedSet, bool markHighPriority, WarmPlanSliceBudget budget, float deadlineAt) {
    if (seedSet == null || seedSet.Count <= 0) yield break;
    foreach (var seedAddress in seedSet) {
      if (HasReachedWarmAddressCap() || HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      if (string.IsNullOrWhiteSpace(seedAddress)) continue;
      AddWarmAddress(seedAddress, markHighPriority);

      var sliceAction = NoteWarmPlanWork(budget, deadlineAt);
      if (sliceAction == WarmPlanSliceAction.Stop) yield break;
      if (sliceAction == WarmPlanSliceAction.Yield) {
        budget.RecordYield();
        TextureResidencyCache.Pump();
        yield return null;
        budget.Reset();
      }
    }
  }

  IEnumerator AddLibrariesFromGameObjects(GameObject[] objects, WarmPlanSliceBudget budget, float deadlineAt) {
    if (objects == null || objects.Length == 0) yield break;
    for (var i = 0; i < objects.Length; i++) {
      if (HasWarmPlanDeadlineElapsed(deadlineAt)) yield break;
      var go = objects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (target == null) continue;
      AddLibrary(target.libraryName);

      var sliceAction = NoteWarmPlanWork(budget, deadlineAt);
      if (sliceAction == WarmPlanSliceAction.Stop) yield break;
      if (sliceAction == WarmPlanSliceAction.Yield) {
        budget.RecordYield();
        TextureResidencyCache.Pump();
        yield return null;
        budget.Reset();
      }
    }
  }

  SpriteWithNormals[] GetCachedArchetypeTargets(GameObject root) {
    if (root == null) return Array.Empty<SpriteWithNormals>();
    if (archetypeTargetCache.TryGetValue(root, out var cachedTargets) && cachedTargets != null) {
      return cachedTargets;
    }
    var targets = root.GetComponentsInChildren<SpriteWithNormals>(true);
    archetypeTargetCache[root] = targets ?? Array.Empty<SpriteWithNormals>();
    return archetypeTargetCache[root];
  }

  bool ShouldAbortWarmPlanPass(
    WarmRequest request,
    bool includeResolvedAddressSweeps,
    bool includeStaticSeedWork,
    WarmPlanSliceBudget budget,
    float deadlineAt,
    bool debugLogs
  ) {
    var deadlineHit = HasWarmPlanDeadlineElapsed(deadlineAt);
    if (!deadlineHit && !HasReachedWarmAddressCap()) return false;
    LogWarmPlanPassSummary(request, includeResolvedAddressSweeps, includeStaticSeedWork, budget, deadlineHit, debugLogs);
    return true;
  }

  void LogWarmPlanPassSummary(
    WarmRequest request,
    bool includeResolvedAddressSweeps,
    bool includeStaticSeedWork,
    WarmPlanSliceBudget budget,
    bool deadlineHit,
    bool debugLogs
  ) {
    if (!debugLogs) return;
    if (!deadlineHit && (budget == null || budget.YieldedFrames <= 0)) return;
    RuntimeLog.Log(
      "[StreamingWarmOrchestrator] Warm plan pass." +
      " context=" + request.context +
      " static_seed=" + (includeStaticSeedWork ? 1 : 0) +
      " resolved_sweep=" + (includeResolvedAddressSweeps ? 1 : 0) +
      " yielded_frames=" + (budget != null ? budget.YieldedFrames : 0) +
      " work_items=" + (budget != null ? budget.TotalWorkItems : 0) +
      " max_slice_ms=" + ((budget != null ? budget.MaxObservedSliceSeconds : 0f) * 1000f).ToString("0.0") +
      " deadline_hit=" + (deadlineHit ? 1 : 0) +
      " libraries=" + warmLibrarySet.Count +
      " labels=" + warmLabelSet.Count +
      " addresses=" + warmAddressSet.Count +
      " critical=" + criticalReadyAddressSet.Count +
      " frame_probes=" + warmPlanFrameAddressProbeCount
    );
  }

  void LogAtlasSeedExpansionSummary(WarmContext context, WarmPlanSliceBudget budget, bool deadlineHit, bool debugLogs) {
    if (!debugLogs) return;
    if (!deadlineHit && (budget == null || budget.YieldedFrames <= 0)) return;
    RuntimeLog.Log(
      "[StreamingWarmOrchestrator] Atlas seed expansion." +
      " context=" + context +
      " yielded_frames=" + (budget != null ? budget.YieldedFrames : 0) +
      " work_items=" + (budget != null ? budget.TotalWorkItems : 0) +
      " max_slice_ms=" + ((budget != null ? budget.MaxObservedSliceSeconds : 0f) * 1000f).ToString("0.0") +
      " deadline_hit=" + (deadlineHit ? 1 : 0) +
      " seed_warm=" + playerWarmAtlasSeedAddresses.Count
    );
  }

  static bool HasWarmPlanDeadlineElapsed(float deadlineAt) {
    return deadlineAt > 0f && Time.realtimeSinceStartup >= deadlineAt;
  }

  static WarmPlanSliceAction NoteWarmPlanWork(WarmPlanSliceBudget budget, float deadlineAt) {
    if (HasWarmPlanDeadlineElapsed(deadlineAt)) return WarmPlanSliceAction.Stop;
    if (budget == null) return WarmPlanSliceAction.Continue;
    if (!budget.Consume()) return WarmPlanSliceAction.Continue;
    return WarmPlanSliceAction.Yield;
  }

  void AddLibraries(List<string> libraries, bool markCritical) {
    if (libraries == null || libraries.Count <= 0) return;
    for (var i = 0; i < libraries.Count; i++) {
      AddLibrary(libraries[i], markCritical);
    }
  }

  void AddDirectAddresses(List<string> addresses, bool markCritical, bool markHighPriority) {
    if (addresses == null || addresses.Count <= 0) return;
    for (var i = 0; i < addresses.Count; i++) {
      AddReadyAddress(addresses[i], markCritical, markHighPriority);
    }
  }

  void AddLibrary(string libraryName, bool markCritical = false) {
    var normalized = NormalizeToken(libraryName);
    if (string.IsNullOrWhiteSpace(normalized)) return;
    warmLibrarySet.Add(normalized);
    if (markCritical) {
      criticalLibrarySet.Add(normalized);
    }
  }

  void CollectLabels(IReadOnlyList<string> labels, bool markCritical) {
    if (labels == null || labels.Count <= 0) return;
    for (var i = 0; i < labels.Count; i++) {
      var normalized = NormalizeToken(labels[i]);
      if (string.IsNullOrWhiteSpace(normalized)) continue;
      warmLabelSet.Add(normalized);
      if (markCritical) {
        criticalReadyLabelSet.Add(normalized);
        highPriorityLabelSet.Add(normalized);
      }
    }
  }

  static bool IsTargetWarmable(SpriteWithNormals target, bool allowInactive = false) {
    if (target == null) return false;
    if (!allowInactive && !target.isActiveAndEnabled) return false;
    if (allowInactive && !target.enabled) return false;
    if (target.DoNotRender) return false;
    return true;
  }

  static bool IsCorePlayerWarmAnimation(string animationName) {
    if (string.IsNullOrWhiteSpace(animationName)) return false;
    var keys = GearController.CorePlayerWarmAnimationKeys;
    for (var i = 0; i < keys.Length; i++) {
      if (string.Equals(animationName, keys[i], StringComparison.Ordinal)) {
        return true;
      }
    }
    return false;
  }

  static string ResolveAnimationCategory(string animationName, AnimData anim) {
    if (anim == null) return animationName ?? "";
    if (anim.To == 1) return "To";
    if (anim.To == 2) return "To2";
    return string.IsNullOrWhiteSpace(anim.category) ? animationName ?? "" : anim.category.Trim();
  }

  static Dictionary<string, EffectData> ResolveEnemyEffectAnimations(string enemyType) {
    if (string.IsNullOrWhiteSpace(enemyType)) return null;
    if (string.Equals(enemyType, "Imp", StringComparison.OrdinalIgnoreCase)) return Effects.Imp;
    return null;
  }

  static string NormalizeToken(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    return value.Trim();
  }
}
