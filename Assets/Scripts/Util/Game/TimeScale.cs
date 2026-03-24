using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class TimeScale {
  sealed class EmptyEnumerator : IEnumerator {
    public static readonly EmptyEnumerator Instance = new();

    public object Current => null;

    public bool MoveNext() {
      return false;
    }

    public void Reset() { }
  }

  sealed class ScaledWaitInstruction : CustomYieldInstruction {
    readonly Stack<ScaledWaitInstruction> pool;
    float endTime;
    Transform context;
    bool released;

    public ScaledWaitInstruction(Stack<ScaledWaitInstruction> ownerPool) {
      pool = ownerPool;
    }

    public void Initialize(float seconds, Transform waitContext) {
      context = waitContext;
      endTime = GetNow(waitContext) + Mathf.Max(seconds, 0f);
      released = false;
    }

    public override bool keepWaiting {
      get {
        if (released) return false;
        if (context == null || GetNow(context) >= endTime) {
          Release();
          return false;
        }
        return true;
      }
    }

    void Release() {
      if (released) return;

      released = true;
      context = null;
      endTime = 0f;
      pool.Push(this);
    }
  }

  static readonly Dictionary<int, float> FallbackFactors = new() { { 1, 1f } };
  static readonly Stack<ScaledWaitInstruction> ScaledWaitPool = new();

  // Legacy compatibility surface. New layer ownership comes from SceneTimeScaleTarget.
  public static Dictionary<int, float> Factors {
    get {
      var snapshot = new Dictionary<int, float>(Mathf.Max(FallbackFactors.Count, 1));
      GetLayerFactorsNonAlloc(snapshot);
      return snapshot;
    }
    set {
      var sanitized = new Dictionary<int, float>();
      if (value != null) {
        foreach (var pair in value) {
          sanitized[Mathf.Max(pair.Key, 0)] = Mathf.Max(pair.Value, 0f);
        }
      }
      if (sanitized.Count <= 0) {
        sanitized[1] = 1f;
      }

      if (TryGetManager(out var manager)) {
        manager.ApplyLayerFactors(sanitized, "legacy_factors_set");
        return;
      }

      FallbackFactors.Clear();
      foreach (var pair in sanitized) {
        FallbackFactors[pair.Key] = pair.Value;
      }
    }
  }

  public static void GetLayerFactorsNonAlloc(Dictionary<int, float> destination) {
    if (destination == null) return;

    if (TryGetManager(out var manager)) {
      manager.CopyLayerFactorSnapshot(destination);
      return;
    }

    destination.Clear();
    foreach (var pair in FallbackFactors) {
      destination[pair.Key] = pair.Value;
    }
    if (destination.Count <= 0) {
      destination[1] = 1f;
    }
  }

  public static float GetLayerFactor(int layerIndex) {
    if (TryGetManager(out var manager)) {
      return manager.GetLayerFactor(layerIndex);
    }
    return FallbackFactors.TryGetValue(Mathf.Max(layerIndex, 0), out var factor) ? factor : 1f;
  }

  public static void SetLayerFactor(int layerIndex, float factor, string reason = null) {
    if (TryGetManager(out var manager)) {
      manager.SetLayerFactor(layerIndex, factor, reason);
      return;
    }
    FallbackFactors[Mathf.Max(layerIndex, 0)] = Mathf.Max(factor, 0f);
  }

  public static void SetSceneMultiplier(float multiplier, string reason = null) {
    if (!TryGetManager(out var manager)) return;
    manager.SetSceneMultiplier(multiplier, reason);
  }

  public static float GetSceneMultiplier() {
    return TryGetManager(out var manager) ? manager.SceneMultiplier : 1f;
  }

  public static float GetEffectiveFactor(Component context) {
    return context != null ? GetEffectiveFactor(context.transform) : 1f;
  }

  public static float GetEffectiveFactor(Transform context) {
    if (!TryGetManager(out var manager) || context == null) return 1f;
    return manager.GetEffectiveFactor(context);
  }

  public static float GetEffectiveFactor(SceneTimeScaleTarget target) {
    if (!TryGetManager(out var manager) || target == null) return 1f;
    return manager.GetEffectiveFactor(target);
  }

  public static float GetDeltaTime(Component context) {
    if (!TryGetManager(out var manager) || context == null) return Time.deltaTime;
    return Time.deltaTime * manager.GetEffectiveFactor(context);
  }

  public static float GetDeltaTime(Transform context) {
    if (!TryGetManager(out var manager) || context == null) return Time.deltaTime;
    return Time.deltaTime * manager.GetEffectiveFactor(context);
  }

  public static float GetDeltaTime(SceneTimeScaleTarget target) {
    if (!TryGetManager(out var manager) || target == null) return Time.deltaTime;
    return Time.deltaTime * manager.GetEffectiveFactor(target);
  }

  public static float GetFixedDeltaTime(Component context) {
    if (!TryGetManager(out var manager) || context == null) return Time.fixedDeltaTime;
    return Time.fixedDeltaTime * manager.GetEffectiveFactor(context);
  }

  public static float GetFixedDeltaTime(Transform context) {
    if (!TryGetManager(out var manager) || context == null) return Time.fixedDeltaTime;
    return Time.fixedDeltaTime * manager.GetEffectiveFactor(context);
  }

  public static float GetFixedDeltaTime(SceneTimeScaleTarget target) {
    if (!TryGetManager(out var manager) || target == null) return Time.fixedDeltaTime;
    return Time.fixedDeltaTime * manager.GetEffectiveFactor(target);
  }

  public static float GetNow(Component context) {
    if (!TryGetManager(out var manager) || context == null) return Time.time;
    return manager.GetNow(context);
  }

  public static float GetNow(Transform context) {
    if (!TryGetManager(out var manager) || context == null) return Time.time;
    return manager.GetNow(context);
  }

  public static float GetNow(SceneTimeScaleTarget target) {
    if (!TryGetManager(out var manager) || target == null) return Time.time;
    return manager.GetNow(target);
  }

  public static IEnumerator WaitForSecondsScaled(float seconds, Component context) {
    return CreateScaledWaitInstruction(seconds, context != null ? context.transform : null);
  }

  public static IEnumerator WaitForSecondsScaled(float seconds, Transform context) {
    return CreateScaledWaitInstruction(seconds, context);
  }

  public static IEnumerator WaitForSecondsScaled(float seconds, SceneTimeScaleTarget target) {
    return CreateScaledWaitInstruction(seconds, target != null ? target.transform : null);
  }

  public static LTDescr RegisterTween(Component context, LTDescr descr, float baseDuration) {
    if (!TryGetManager(out var manager) || descr == null) return descr;
    manager.RegisterTween(context != null ? context.transform : null, descr, baseDuration);
    return descr;
  }

  public static LTDescr RegisterTween(Transform context, LTDescr descr, float baseDuration) {
    if (!TryGetManager(out var manager) || descr == null) return descr;
    manager.RegisterTween(context, descr, baseDuration);
    return descr;
  }

  public static LTDescr RegisterTween(Component context, LTDescr descr, float baseDuration, float baseSpeed) {
    if (!TryGetManager(out var manager) || descr == null) return descr;
    manager.RegisterTween(context != null ? context.transform : null, descr, baseDuration, baseSpeed);
    return descr;
  }

  public static LTDescr RegisterTween(Transform context, LTDescr descr, float baseDuration, float baseSpeed) {
    if (!TryGetManager(out var manager) || descr == null) return descr;
    manager.RegisterTween(context, descr, baseDuration, baseSpeed);
    return descr;
  }

  public static void UnregisterTween(int tweenId) {
    if (!TryGetManager(out var manager) || tweenId < 0) return;
    manager.UnregisterTween(tweenId);
  }

  public static void UnregisterTweens(GameObject owner) {
    if (!TryGetManager(out var manager) || owner == null) return;
    manager.UnregisterTweens(owner);
  }

  static IEnumerator CreateScaledWaitInstruction(float seconds, Transform context) {
    if (seconds <= 0f) return EmptyEnumerator.Instance;

    var instruction = ScaledWaitPool.Count > 0
      ? ScaledWaitPool.Pop()
      : new ScaledWaitInstruction(ScaledWaitPool);

    instruction.Initialize(seconds, context);
    return instruction;
  }

  static bool TryGetManager(out SceneTimeScaleManager manager) {
    manager = SceneTimeScaleManager.Instance;
    return manager != null;
  }
}
