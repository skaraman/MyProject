using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public class SceneTimeScaleManager : MonoBehaviour {
  [Serializable]
  public struct LayerFactorEntry {
    [Min(0)] public int layerIndex;
    [Min(0f)] public float factor;
  }

  struct TweenRegistration {
    public int tweenId;
    public int layerIndex;
    public ulong ownerEntityId;
    public float baseDuration;
    public float baseSpeed;
    public bool pausedByTimeScale;
  }

  struct FrozenRigidbody2DState {
    public Vector2 linearVelocity;
    public float angularVelocity;
    public bool wasSleeping;
  }

  struct FrozenAnimatorState {
    public float speed;
    public bool enabled;
  }

  const float PauseRescanIntervalSeconds = 0.25f;
  const int TweenRegistrationWarmCapacity = 256;
  const int TweenOwnerWarmCapacity = 64;
  const int TweenIdsPerOwnerWarmCapacity = 8;

  static SceneTimeScaleManager instance;
  static bool instanceLookupAttempted;
  static int stateVersion;
  static readonly List<GameObject> sceneRootGameObjects = new();

  [SerializeField] Transform sceneObjectsRoot;
  [SerializeField] List<LayerFactorEntry> layerFactors = new() {
    new LayerFactorEntry {
      layerIndex = 1,
      factor = 1f
    }
  };
  [SerializeField, Min(0f)] float sceneMultiplier = 1f;
  [SerializeField] bool enableDebugLogs;

  readonly Dictionary<int, float> layerFactorLookup = new();
  readonly Dictionary<int, float> layerClockLookup = new();
  readonly HashSet<int> observedLayers = new();
  readonly Dictionary<int, TweenRegistration> tweenRegistrationsById = new(TweenRegistrationWarmCapacity);
  readonly Dictionary<ulong, List<int>> tweenIdsByOwner = new(TweenOwnerWarmCapacity);
  readonly Stack<List<int>> tweenIdListPool = new(TweenOwnerWarmCapacity);
  readonly HashSet<ulong> warnedExternalTweenOwners = new();
  readonly Dictionary<Rigidbody2D, FrozenRigidbody2DState> frozenRigidbodies2D = new();
  readonly Dictionary<Animator, FrozenAnimatorState> frozenAnimators = new();
  readonly List<TweenRegistration> tweenRegistrationBuffer = new(TweenRegistrationWarmCapacity);
  readonly List<Rigidbody2D> managedRigidbodies2DBuffer = new();
  readonly List<Animator> managedAnimatorsBuffer = new();
  bool warnedMissingSceneRoot;
  bool managedPauseStateApplied;
  float nextPauseRescanAt = -1f;
  int tweenIdListCreatedCount;

  internal static int StateVersion => stateVersion;

  public static SceneTimeScaleManager Instance {
    get {
      if (instance != null) return instance;
      if (instanceLookupAttempted) return null;

      instanceLookupAttempted = true;
      instance = FindAnyObjectByType<SceneTimeScaleManager>();
      if (instance == null && Application.isPlaying) {
        TryBootstrapSceneRootManager();
        if (instance == null) {
          instance = FindAnyObjectByType<SceneTimeScaleManager>();
        }
      }
      return instance;
    }
  }

  public float SceneMultiplier => Mathf.Max(sceneMultiplier, 0f);

  public void PrepareRuntimeCaches() {
    while (tweenIdListCreatedCount < TweenOwnerWarmCapacity) {
      tweenIdListPool.Push(CreateTweenIdList());
    }
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetStaticState() {
    instance = null;
    instanceLookupAttempted = false;
    stateVersion = 0;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  static void RegisterSceneCallbacks() {
    SceneManager.sceneLoaded -= OnSceneLoaded;
    SceneManager.sceneLoaded += OnSceneLoaded;
    SceneManager.sceneUnloaded -= OnSceneUnloaded;
    SceneManager.sceneUnloaded += OnSceneUnloaded;
    SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    SceneManager.activeSceneChanged += OnActiveSceneChanged;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void BootstrapAfterSceneLoad() {
    InvalidateInstanceLookup();
    TryBootstrapSceneRootManager();
  }

  void Awake() {
    InitializeRuntimeState();
  }

  void OnEnable() {
    InitializeRuntimeState();
  }

  void OnDisable() {
    if (instance == this) {
      InvalidateInstanceLookup();
    }
  }

  void Update() {
    AdvanceLayerClocks(Time.deltaTime);
    MaintainManagedPauseState();
  }

  void Reset() {
    sceneObjectsRoot = transform;
    EnsureDefaultLayer();
  }

  void OnValidate() {
    sceneMultiplier = Mathf.Max(sceneMultiplier, 0f);
    EnsureDefaultLayer();
    RebuildLayerLookups();
  }

  public Dictionary<int, float> GetLayerFactorSnapshot() {
    var snapshot = new Dictionary<int, float>(Mathf.Max(layerFactorLookup.Count, 1));
    CopyLayerFactorSnapshot(snapshot);
    return snapshot;
  }

  public void CopyLayerFactorSnapshot(Dictionary<int, float> destination) {
    if (destination == null) return;

    destination.Clear();
    foreach (var pair in layerFactorLookup) {
      destination[pair.Key] = pair.Value;
    }
    if (destination.Count <= 0) {
      destination[1] = 1f;
    }
  }

  public void ApplyLayerFactors(Dictionary<int, float> factors, string reason = null) {
    layerFactors.Clear();
    if (factors != null) {
      foreach (var pair in factors) {
        layerFactors.Add(new LayerFactorEntry {
          layerIndex = Mathf.Max(pair.Key, 0),
          factor = Mathf.Max(pair.Value, 0f)
        });
      }
    }
    EnsureDefaultLayer();
    RebuildLayerLookups();
    LogDebug(
      "[SceneTimeScaleManager] Applied layer factor snapshot" +
      " count=" + layerFactors.Count +
      " reason=" + FormatReason(reason)
    );
    RefreshAllTweenScales();
  }

  public float GetLayerFactor(int layerIndex) {
    EnsureObservedLayer(layerIndex);
    return layerFactorLookup.TryGetValue(Mathf.Max(layerIndex, 0), out var factor) ? factor : 1f;
  }

  public void SetLayerFactor(int layerIndex, float factor, string reason = null) {
    var sanitizedLayer = Mathf.Max(layerIndex, 0);
    var sanitizedFactor = Mathf.Max(factor, 0f);
    var previous = GetLayerFactor(sanitizedLayer);
    if (Mathf.Approximately(previous, sanitizedFactor)) return;

    layerFactorLookup[sanitizedLayer] = sanitizedFactor;
    UpsertSerializedLayerEntry(sanitizedLayer, sanitizedFactor);
    EnsureObservedLayer(sanitizedLayer);
    LogDebug(
      "[SceneTimeScaleManager] Layer factor changed" +
      " layer=" + sanitizedLayer +
      " previous=" + previous +
      " next=" + sanitizedFactor +
      " effective=" + GetEffectiveFactor(sanitizedLayer) +
      " reason=" + FormatReason(reason)
    );
    RefreshTweenScalesForLayer(sanitizedLayer);
  }

  public void SetSceneMultiplier(float multiplier, string reason = null) {
    var sanitized = Mathf.Max(multiplier, 0f);
    if (Mathf.Approximately(sceneMultiplier, sanitized)) return;
    var previous = sceneMultiplier;
    sceneMultiplier = sanitized;
    LogDebug(
      "[SceneTimeScaleManager] Scene multiplier changed" +
      " previous=" + previous +
      " next=" + sceneMultiplier +
      " reason=" + FormatReason(reason)
    );
    RefreshAllTweenScales();
    SyncManagedPauseState(forceRescan: true, reason);
  }

  public float GetEffectiveFactor(Component context) {
    return context != null ? GetEffectiveFactor(context.transform) : 1f;
  }

  public float GetEffectiveFactor(SceneTimeScaleTarget target) {
    return target != null ? GetEffectiveFactor(target.transform) : 1f;
  }

  public float GetEffectiveFactor(Transform context) {
    if (!TryResolveLayerContext(context, out _, out var layerIndex)) return 1f;
    return GetEffectiveFactor(layerIndex);
  }

  public float GetNow(Component context) {
    return context != null ? GetNow(context.transform) : Time.time;
  }

  public float GetNow(SceneTimeScaleTarget target) {
    return target != null ? GetNow(target.transform) : Time.time;
  }

  public float GetNow(Transform context) {
    if (!TryResolveLayerContext(context, out _, out var layerIndex)) return Time.time;
    EnsureObservedLayer(layerIndex);
    return layerClockLookup.TryGetValue(layerIndex, out var now) ? now : Time.time;
  }

  public void RegisterTween(Transform context, LTDescr descr, float baseDuration, float baseSpeed = -1f) {
    if (descr == null) return;
    if (!TryResolveLayerContext(context, out _, out var layerIndex)) {
      var owner = descr.trans != null ? descr.trans.gameObject : null;
      if (owner != null && warnedExternalTweenOwners.Add(ObjectEntityId.GetRawValue(owner))) {
        Debug.LogWarning(
          "[SceneTimeScaleManager] Skipping tween registration outside managed root" +
          " owner='" + owner.name + "'" +
          " tween_id=" + descr.id
        );
      }
      return;
    }

    var tweenId = descr.id;
    var ownerEntityId = descr.trans != null ? ObjectEntityId.GetRawValue(descr.trans.gameObject) : 0UL;
    var registration = new TweenRegistration {
      tweenId = tweenId,
      layerIndex = layerIndex,
      ownerEntityId = ownerEntityId,
      baseDuration = Mathf.Max(baseDuration, 0f),
      baseSpeed = Mathf.Max(baseSpeed, 0f)
    };

    if (ownerEntityId != 0UL) {
      if (!tweenIdsByOwner.TryGetValue(ownerEntityId, out var list)) {
        list = AcquireTweenIdList();
        tweenIdsByOwner[ownerEntityId] = list;
      }
      if (!list.Contains(tweenId)) {
        list.Add(tweenId);
      }
    }

    EnsureObservedLayer(layerIndex);
    ApplyTweenScale(ref registration, descr);
    tweenRegistrationsById[tweenId] = registration;
  }

  public void UnregisterTween(int tweenId) {
    if (!tweenRegistrationsById.TryGetValue(tweenId, out var registration)) return;
    tweenRegistrationsById.Remove(tweenId);
    if (registration.ownerEntityId != 0UL &&
        tweenIdsByOwner.TryGetValue(registration.ownerEntityId, out var list)) {
      list.Remove(tweenId);
      if (list.Count <= 0) {
        tweenIdsByOwner.Remove(registration.ownerEntityId);
        ReleaseTweenIdList(list);
      }
    }
  }

  public void UnregisterTweens(GameObject owner) {
    if (owner == null) return;
    var ownerId = ObjectEntityId.GetRawValue(owner);
    if (!tweenIdsByOwner.TryGetValue(ownerId, out var list)) return;
    for (var i = list.Count - 1; i >= 0; i--) {
      tweenRegistrationsById.Remove(list[i]);
    }
    tweenIdsByOwner.Remove(ownerId);
    ReleaseTweenIdList(list);
  }

  void InitializeRuntimeState() {
    if (instance != null && instance != this) {
      return;
    }

    instance = this;
    instanceLookupAttempted = true;
    ResolveSceneObjectsRoot();
    EnsureDefaultLayer();
    RebuildLayerLookups();
    EnsureObservedLayer(1);
    IncrementStateVersion();
  }

  void MaintainManagedPauseState() {
    SyncManagedPauseState(forceRescan: false, "update");
  }

  void AdvanceLayerClocks(float deltaTime) {
    if (deltaTime <= 0f || observedLayers.Count <= 0) return;
    foreach (var layerIndex in observedLayers) {
      layerClockLookup[layerIndex] += deltaTime * GetEffectiveFactor(layerIndex);
    }
  }

  void EnsureDefaultLayer() {
    if (layerFactors == null) {
      layerFactors = new List<LayerFactorEntry>();
    }

    for (var i = 0; i < layerFactors.Count; i++) {
      var entry = layerFactors[i];
      entry.layerIndex = Mathf.Max(entry.layerIndex, 0);
      entry.factor = Mathf.Max(entry.factor, 0f);
      layerFactors[i] = entry;
      if (entry.layerIndex == 1) {
        return;
      }
    }

    layerFactors.Add(new LayerFactorEntry {
      layerIndex = 1,
      factor = 1f
    });
  }

  void RebuildLayerLookups() {
    layerFactorLookup.Clear();
    if (layerFactors == null) return;

    for (var i = 0; i < layerFactors.Count; i++) {
      var entry = layerFactors[i];
      var layerIndex = Mathf.Max(entry.layerIndex, 0);
      var factor = Mathf.Max(entry.factor, 0f);
      layerFactorLookup[layerIndex] = factor;
    }

    if (layerFactorLookup.Count <= 0) {
      layerFactorLookup[1] = 1f;
    }
  }

  void ResolveSceneObjectsRoot() {
    if (sceneObjectsRoot != null) return;

    var previousRoot = sceneObjectsRoot;
    if (string.Equals(gameObject.name, "SCENEOBJECTS", StringComparison.Ordinal)) {
      sceneObjectsRoot = transform;
      if (sceneObjectsRoot != previousRoot) {
        IncrementStateVersion();
      }
      return;
    }

    var resolved = FindSceneObjectsRoot();
    if (resolved != null) {
      sceneObjectsRoot = resolved;
      warnedMissingSceneRoot = false;
      if (sceneObjectsRoot != previousRoot) {
        IncrementStateVersion();
      }
      return;
    }

    if (!warnedMissingSceneRoot) {
      warnedMissingSceneRoot = true;
      Debug.LogWarning("[SceneTimeScaleManager] Could not resolve SCENEOBJECTS root.");
    }
  }

  void SyncManagedPauseState(bool forceRescan, string reason) {
    ResolveSceneObjectsRoot();
    var shouldFreezeManagedScene = SceneMultiplier <= 0.0001f;
    if (!shouldFreezeManagedScene) {
      if (managedPauseStateApplied) {
        RestoreManagedPauseState(reason);
      }
      return;
    }

    if (sceneObjectsRoot == null) return;

    var now = Time.unscaledTime;
    if (!forceRescan &&
        managedPauseStateApplied &&
        nextPauseRescanAt >= 0f &&
        now < nextPauseRescanAt) {
      return;
    }

    FreezeManagedPauseState(reason, forceRescan || !managedPauseStateApplied);
    managedPauseStateApplied = true;
    nextPauseRescanAt = now + PauseRescanIntervalSeconds;
  }

  struct LayerCacheEntry {
    public EntityId instanceId;
    public int frameCount;
    public int layerIndex;
    public SceneTimeScaleTarget ownerTarget;
  }

  readonly LayerCacheEntry[] _layerCache = new LayerCacheEntry[2048];

  internal bool TryResolveLayerContext(Transform context, out SceneTimeScaleTarget ownerTarget, out int layerIndex) {
    ownerTarget = null;
    layerIndex = 1;
    ResolveSceneObjectsRoot();
    if (context == null || sceneObjectsRoot == null) return false;

    EntityId instanceId = context.GetEntityId();
    int cacheIndex = (instanceId.GetHashCode() & 0x7FFFFFFF) % _layerCache.Length;
    var entry = _layerCache[cacheIndex];

    if (entry.instanceId.Equals(instanceId) && entry.frameCount == Time.frameCount) {
      ownerTarget = entry.ownerTarget;
      layerIndex = entry.layerIndex;
      EnsureObservedLayer(layerIndex);
      return true;
    }

    if (context != sceneObjectsRoot && !context.IsChildOf(sceneObjectsRoot)) return false;

    var current = context;
    while (current != null) {
      if (current.TryGetComponent<SceneTimeScaleTarget>(out ownerTarget)) {
        layerIndex = ownerTarget.LayerIndex;
        EnsureObservedLayer(layerIndex);
        _layerCache[cacheIndex] = new LayerCacheEntry {
          instanceId = instanceId,
          frameCount = Time.frameCount,
          layerIndex = layerIndex,
          ownerTarget = ownerTarget
        };
        return true;
      }
      if (current == sceneObjectsRoot) break;
      current = current.parent;
    }

    EnsureObservedLayer(layerIndex);
    _layerCache[cacheIndex] = new LayerCacheEntry {
      instanceId = instanceId,
      frameCount = Time.frameCount,
      layerIndex = layerIndex,
      ownerTarget = ownerTarget
    };
    return true;
  }

  bool TryResolveLayerIndex(Transform context, out int layerIndex) {
    return TryResolveLayerContext(context, out _, out layerIndex);
  }

  void EnsureObservedLayer(int layerIndex) {
    var sanitized = Mathf.Max(layerIndex, 0);
    observedLayers.Add(sanitized);
    if (!layerClockLookup.ContainsKey(sanitized)) {
      layerClockLookup[sanitized] = Time.time;
    }
  }

  void FreezeManagedPauseState(string reason, bool enteringPause) {
    if (sceneObjectsRoot == null) return;

    var newlyFrozenRigidbodies = FreezeManagedRigidbodies2D();
    var newlyFrozenAnimators = FreezeManagedAnimators();
    if (!enteringPause && newlyFrozenRigidbodies <= 0 && newlyFrozenAnimators <= 0) return;

    LogDebug(
      "[SceneTimeScaleManager] Applied managed pause freeze" +
      " root='" + sceneObjectsRoot.name + "'" +
      " rigidbody2d_count=" + frozenRigidbodies2D.Count +
      " animator_count=" + frozenAnimators.Count +
      " newly_frozen_rigidbody2d=" + newlyFrozenRigidbodies +
      " newly_frozen_animator=" + newlyFrozenAnimators +
      " reason=" + FormatReason(reason)
    );
  }

  int FreezeManagedRigidbodies2D() {
    if (sceneObjectsRoot == null) return 0;

    managedRigidbodies2DBuffer.Clear();
    sceneObjectsRoot.GetComponentsInChildren(true, managedRigidbodies2DBuffer);
    var newlyFrozenCount = 0;
    for (var i = 0; i < managedRigidbodies2DBuffer.Count; i++) {
      var body = managedRigidbodies2DBuffer[i];
      if (body == null) continue;

      if (!frozenRigidbodies2D.ContainsKey(body)) {
        frozenRigidbodies2D[body] = new FrozenRigidbody2DState {
          linearVelocity = body.linearVelocity,
          angularVelocity = body.angularVelocity,
          wasSleeping = body.IsSleeping()
        };
        newlyFrozenCount++;
      }

      if (body.linearVelocity != Vector2.zero) {
        body.linearVelocity = Vector2.zero;
      }
      if (!Mathf.Approximately(body.angularVelocity, 0f)) {
        body.angularVelocity = 0f;
      }
      if (!body.IsSleeping()) {
        body.Sleep();
      }
    }

    managedRigidbodies2DBuffer.Clear();
    return newlyFrozenCount;
  }

  int FreezeManagedAnimators() {
    if (sceneObjectsRoot == null) return 0;

    managedAnimatorsBuffer.Clear();
    sceneObjectsRoot.GetComponentsInChildren(true, managedAnimatorsBuffer);
    var newlyFrozenCount = 0;
    for (var i = 0; i < managedAnimatorsBuffer.Count; i++) {
      var animator = managedAnimatorsBuffer[i];
      if (animator == null) continue;

      if (!frozenAnimators.ContainsKey(animator)) {
        frozenAnimators[animator] = new FrozenAnimatorState {
          speed = animator.speed,
          enabled = animator.enabled
        };
        newlyFrozenCount++;
      }

      if (!Mathf.Approximately(animator.speed, 0f)) {
        animator.speed = 0f;
      }
    }

    managedAnimatorsBuffer.Clear();
    return newlyFrozenCount;
  }

  void RestoreManagedPauseState(string reason) {
    var restoredRigidbodies = 0;
    foreach (var pair in frozenRigidbodies2D) {
      var body = pair.Key;
      if (body == null) continue;

      var state = pair.Value;
      body.linearVelocity = state.linearVelocity;
      body.angularVelocity = state.angularVelocity;
      if (state.wasSleeping) {
        body.Sleep();
      }
      else {
        body.WakeUp();
      }
      restoredRigidbodies++;
    }

    var restoredAnimators = 0;
    foreach (var pair in frozenAnimators) {
      var animator = pair.Key;
      if (animator == null) continue;

      var state = pair.Value;
      animator.enabled = state.enabled;
      animator.speed = state.speed;
      restoredAnimators++;
    }

    frozenRigidbodies2D.Clear();
    frozenAnimators.Clear();
    managedPauseStateApplied = false;
    nextPauseRescanAt = -1f;

    LogDebug(
      "[SceneTimeScaleManager] Restored managed pause freeze" +
      " rigidbody2d_count=" + restoredRigidbodies +
      " animator_count=" + restoredAnimators +
      " reason=" + FormatReason(reason)
    );
  }

  float GetEffectiveFactor(int layerIndex) {
    return SceneMultiplier * GetLayerFactor(layerIndex);
  }

  internal float GetEffectiveFactorForLayer(int layerIndex) {
    return GetEffectiveFactor(layerIndex);
  }

  void RefreshAllTweenScales() {
    RefreshTweenScales(layerIndex: 0, filterByLayer: false);
  }

  void RefreshTweenScalesForLayer(int layerIndex) {
    RefreshTweenScales(layerIndex, filterByLayer: true);
  }

  void RefreshTweenScales(int layerIndex, bool filterByLayer) {
    if (tweenRegistrationsById.Count <= 0) return;

    tweenRegistrationBuffer.Clear();
    foreach (var registration in tweenRegistrationsById.Values) {
      if (filterByLayer && registration.layerIndex != layerIndex) continue;
      tweenRegistrationBuffer.Add(registration);
    }

    for (var i = 0; i < tweenRegistrationBuffer.Count; i++) {
      var registration = tweenRegistrationBuffer[i];
      var descr = LeanTween.descr(registration.tweenId);
      if (descr == null) {
        UnregisterTween(registration.tweenId);
        continue;
      }
      ApplyTweenScale(ref registration, descr);
      tweenRegistrationsById[registration.tweenId] = registration;
    }

    tweenRegistrationBuffer.Clear();
  }

  void ApplyTweenScale(ref TweenRegistration registration, LTDescr descr) {
    if (descr == null) return;

    var factor = GetEffectiveFactor(registration.layerIndex);
    if (factor <= 0f) {
      if (Mathf.Abs(descr.direction) > 0.0001f) {
        descr.pause();
        registration.pausedByTimeScale = true;
      }
      return;
    }

    if (registration.baseSpeed > 0f) {
      descr.setSpeed(Mathf.Max(registration.baseSpeed * factor, 0.0001f));
    }
    else if (registration.baseDuration > 0f) {
      descr.setTime(Mathf.Max(registration.baseDuration / factor, 0.0001f));
    }

    if (registration.pausedByTimeScale && Mathf.Abs(descr.direction) <= 0.0001f) {
      descr.resume();
      registration.pausedByTimeScale = false;
    }
  }

  List<int> AcquireTweenIdList() {
    if (tweenIdListPool.Count > 0) {
      return tweenIdListPool.Pop();
    }

    return CreateTweenIdList();
  }

  List<int> CreateTweenIdList() {
    tweenIdListCreatedCount += 1;
    return new List<int>(TweenIdsPerOwnerWarmCapacity);
  }

  void ReleaseTweenIdList(List<int> list) {
    if (list == null) {
      return;
    }

    list.Clear();
    tweenIdListPool.Push(list);
  }

  void UpsertSerializedLayerEntry(int layerIndex, float factor) {
    for (var i = 0; i < layerFactors.Count; i++) {
      if (layerFactors[i].layerIndex != layerIndex) continue;
      layerFactors[i] = new LayerFactorEntry {
        layerIndex = layerIndex,
        factor = factor
      };
      return;
    }

    layerFactors.Add(new LayerFactorEntry {
      layerIndex = layerIndex,
      factor = factor
    });
  }

  static void TryBootstrapSceneRootManager() {
    if (!Application.isPlaying) return;
    if (instance != null || FindAnyObjectByType<SceneTimeScaleManager>() != null) return;

    var root = FindSceneObjectsRoot();
    if (root == null) return;

    var manager = root.GetComponent<SceneTimeScaleManager>();
    if (manager == null) {
      manager = root.gameObject.AddComponent<SceneTimeScaleManager>();
    }

    manager.sceneObjectsRoot = root;
    var target = root.GetComponent<SceneTimeScaleTarget>();
    if (target == null) {
      target = root.gameObject.AddComponent<SceneTimeScaleTarget>();
      target.layerIndex = 1;
    }
  }

  static Transform FindSceneObjectsRoot() {
    for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++) {
      var scene = SceneManager.GetSceneAt(sceneIndex);
      if (!scene.IsValid() || !scene.isLoaded) continue;
      sceneRootGameObjects.Clear();
      scene.GetRootGameObjects(sceneRootGameObjects);
      for (var i = 0; i < sceneRootGameObjects.Count; i++) {
        var found = FindSceneObjectsRootRecursive(sceneRootGameObjects[i].transform);
        if (found != null) {
          sceneRootGameObjects.Clear();
          return found;
        }
      }
    }
    sceneRootGameObjects.Clear();
    return null;
  }

  static Transform FindSceneObjectsRootRecursive(Transform current) {
    if (current == null) return null;
    if (string.Equals(current.name, "SCENEOBJECTS", StringComparison.Ordinal)) {
      return current;
    }

    for (var i = 0; i < current.childCount; i++) {
      var found = FindSceneObjectsRootRecursive(current.GetChild(i));
      if (found != null) {
        return found;
      }
    }
    return null;
  }

  void LogDebug(string message) {
    if (!enableDebugLogs) return;
    RuntimeLog.Log(message);
  }

  static string FormatReason(string reason) {
    return string.IsNullOrWhiteSpace(reason) ? "-" : reason.Trim();
  }

  static void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
    InvalidateInstanceLookup();
  }

  static void OnSceneUnloaded(Scene scene) {
    InvalidateInstanceLookup();
  }

  static void OnActiveSceneChanged(Scene previous, Scene next) {
    InvalidateInstanceLookup();
  }

  static void InvalidateInstanceLookup() {
    instance = null;
    instanceLookupAttempted = false;
    IncrementStateVersion();
  }

  static void IncrementStateVersion() {
    unchecked {
      stateVersion++;
    }
  }
}
