using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CustomInspector;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class GearController : MonoBehaviour {
  const int MinimumPlayerWarmFramesAtStartup = 8;
  const int MinimumCoreEffectWarmFramesAtStartup = 24;
  static readonly string[] CoreCombatWarmAnimationKeys = { "Blast" };

  [Button(nameof(_TogglePause), label = "un/pause", size = Size.small)] public bool slowDown;
  [Button(nameof(ForceAnimation), label = "Play", size = Size.small)] public bool forceLoop;
  [Button(nameof(LoadGear), label = "LoadGear", size = Size.small)] public bool _bool;
  public string defaultAnimation = "Breathe";
  public float timer;

  public GameObject[] GearObjects;
  public GameObject[] HairObjects;
  public GameObject[] OtherBounceGearObjects;
  public GameObject[] SkinObjects;
  public GameObject[] HBoxObjects;
  [Header("Effects")]
  public SpriteWithNormals effectNode;
  [Header("Projectiles")]
  public ProjectileManager projectileManager;
  public Transform projectileSpawn;
  public bool useFacingDirection = true;
  public Vector2 projectileDirection = Vector2.right;
  public GameObject HairSkin;
  public Dictionary<string, Dictionary<string, GearItem>> lastGear = new Dictionary<string, Dictionary<string, GearItem>>();
  public bool needsFlip;
  [Header("Streaming Warmup")]
  [SerializeField] bool prewarmAnimationStartsOnLoad = true;
  [SerializeField, Min(1)] int prewarmFramesPerAnimation = 1;
  [SerializeField] bool queueEquippedAnimationWarmup = true;
  [SerializeField, Min(1)] int equipWarmupFrameChunk = 24;
  [SerializeField, Min(1)] int equipWarmupChunksPerFrame = 6;
  [SerializeField, Min(50)] int equipWarmupEnqueueBudgetPerFrame = 96;
  [SerializeField] bool logEquipWarmupSummary;
  private GameObject[] combinedBounces;
  private SaveData gameData = new();
  private AnimationController animationController = new();
  private AnimationController effectAnimationController = new();
  private readonly Dictionary<string, AnimData> effectAnimations = new();
  private readonly List<string> equipWarmupAddressScratch = new();
  private readonly HashSet<string> equipWarmupSeenAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<string> coreEffectWarmupAddressScratch = new();
  private readonly HashSet<string> coreEffectWarmupSeenAddressScratch = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<string> linkedEffectWarmKeyScratch = new();
  private readonly HashSet<string> linkedEffectWarmKeySeenScratch = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<string> linkedProjectileWarmKeyScratch = new();
  private readonly HashSet<string> linkedProjectileWarmKeySeenScratch = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, string> equipPartPrefixScratch = new(StringComparer.OrdinalIgnoreCase);
  private Dictionary<string, string> pendingEquipWarmupPartPrefixes;
  private Coroutine equipWarmupRoutine;
  private bool effectControllerInitialized;
  private bool effectResetToEmptyPending;
  private bool runtimeInitialized;
  private string appearanceOwnerId;
  private string effectAppearanceOwnerId;
  private string coreEffectWarmOwnerId;
  private int appearanceRevision = 1;

  public bool IsFacingRight => animationController != null && animationController.IsFacingRight;
  public int AppearanceRevision => appearanceRevision;


  void OnEnable() {
    EnsureRuntimeInitialized("enable");
    TryStartPendingEquipWarmup("enable");
  }

  void Start() {
    EnsureRuntimeInitialized("start");
  }

  static bool ShouldLogRuntimeInitDebug() {
    return Application.isEditor || Debug.isDebugBuild;
  }

  void EnsureRuntimeInitialized(string source) {
    if (runtimeInitialized) return;
    runtimeInitialized = true;
    ResetDebugPlaybackFlags();
    appearanceOwnerId = "player:" + ObjectEntityId.GetString(gameObject);
    effectAppearanceOwnerId = effectNode != null ? "effect:" + ObjectEntityId.GetString(effectNode) : "";
    coreEffectWarmOwnerId = "player_core_effects:" + ObjectEntityId.GetString(gameObject);
    combinedBounces = (HairObjects ?? Array.Empty<GameObject>()).Concat(OtherBounceGearObjects ?? Array.Empty<GameObject>()).ToArray();
    NormalizeSkinSpriteDefaultsForRuntime();
    ResolveProjectileManagerReference(source);
    PrimeSpriteStreamingWarmup();
    ConfigureAnimationController();
    ConfigureEffectController();
    PrimeControllerAnimationWarmup();
    PrimeCoreCombatEffectWarmup(source);
    HookAnimationEvents();
    if (Application.isPlaying) {
      LeanTween.reset();
      LeanTween.init(4000);
    }
    animationController.PlayAnimation(defaultAnimation, true);
    if (ShouldLogRuntimeInitDebug()) {
      Debug.Log(
        "[GearController] RuntimeInit" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " object=" + gameObject.name +
        " active=" + (gameObject.activeInHierarchy ? 1 : 0) +
        " combined_bounces=" + (combinedBounces != null ? combinedBounces.Length : 0)
      );
    }
  }

  void ResetDebugPlaybackFlags() {
    // Prevent accidentally persisted inspector debug flags from throttling runtime animation speed.
    slowDown = false;
    forceLoop = false;
  }

  void Update() {
    if (animationController == null) return;
    timer = animationController.animationTimer;
    animationController.SlowDown = slowDown;
    animationController.ForceLoop = forceLoop;
    if (effectControllerInitialized) {
      effectAnimationController.SlowDown = slowDown;
      effectAnimationController.ForceLoop = forceLoop;
    }
    if (needsFlip) {
      animationController.QueueFlip();
      needsFlip = false;
    }

    TickControllers(TimeScale.GetDeltaTime(this));
  }

  void TickControllers(float deltaTime) {
    if (deltaTime <= 0f) return;
    animationController?.Tick(deltaTime);
    if (effectControllerInitialized) {
      effectAnimationController.Tick(deltaTime);
      TryFinalizeCompletedEffectAnimation();
    }
  }

  private void ConfigureAnimationController() {
    if (animationController == null) return;
    // Prioritize skin targets first so core body animation continuity is protected under pin budgets.
    var spriteTargets = (SkinObjects ?? Array.Empty<GameObject>()).Concat(GearObjects ?? Array.Empty<GameObject>());
    animationController.Initialize(
      transform,
      spriteTargets,
      combinedBounces,
      HBoxObjects,
      Animations.Esperanza,
      Interrupts.Esperanza,
      BounceAdjustments.Esperanza,
      HBoxes.Esperanza,
      defaultAnimation,
      false,
      appearanceOwnerId,
      TextureResidencyCache.PinClass.Player
    );
  }

  private void NormalizeSkinSpriteDefaultsForRuntime() {
    if (!Application.isPlaying) return;
    NormalizeSpriteDefaultsForRuntime(SkinObjects);
    NormalizeSpriteDefaultsForRuntime(GearObjects);
  }

  static void NormalizeSpriteDefaultsForRuntime(GameObject[] objects) {
    if (objects == null || objects.Length == 0) return;

    for (var i = 0; i < objects.Length; i++) {
      var go = objects[i];
      if (go == null) continue;
      var sn = go.GetComponent<SpriteWithNormals>();
      if (sn == null) continue;

      sn.SetIsAnimation(true);
    }
  }

  private void PrimeSpriteStreamingWarmup() {
    if (!Application.isPlaying) return;

    // Ideal runtime behavior keeps this set tight: only currently visible or
    // imminent libraries. Oversized warmup sets increase startup queue pressure.
    var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    CollectLibraries(GearObjects, libraries);
    CollectLibraries(SkinObjects, libraries);
    if (effectNode != null && !string.IsNullOrWhiteSpace(effectNode.libraryName)) {
      libraries.Add(effectNode.libraryName.Trim());
    }

    SpriteRuntimeResolver.WarmupLibraries(libraries);
  }

  private void PrimeControllerAnimationWarmup() {
    if (!Application.isPlaying || !prewarmAnimationStartsOnLoad) return;
    // Keep first action inputs smooth by warming a wider startup frame window.
    var warmFrames = Mathf.Max(prewarmFramesPerAnimation, MinimumPlayerWarmFramesAtStartup);
    animationController?.PrimeAllAnimationStarts(warmFrames);
    PrimeLinkedEffectAnimationWarmup(Animations.Esperanza, "controller_startup");
  }

  static void CollectLibraries(GameObject[] objects, HashSet<string> libraries) {
    if (objects == null || libraries == null) return;
    for (var i = 0; i < objects.Length; i++) {
      var go = objects[i];
      if (go == null) continue;
      var sn = go.GetComponent<SpriteWithNormals>();
      if (sn == null || string.IsNullOrWhiteSpace(sn.libraryName)) continue;
      libraries.Add(sn.libraryName.Trim());
    }
  }

  public int CollectPersistentSkinStartupAddresses(
    List<string> outAddresses,
    HashSet<string> seenAddresses = null,
    int maxUniqueAddresses = int.MaxValue
  ) {
    if (outAddresses == null || maxUniqueAddresses <= 0) {
      return 0;
    }

    if (!runtimeInitialized) {
      return 0;
    }

    var startupWarmFrames = Mathf.Max(prewarmFramesPerAnimation, MinimumPlayerWarmFramesAtStartup);
    var startingCount = outAddresses.Count;
    CollectPersistentSkinStartupAddresses(
      SkinObjects,
      startupWarmFrames,
      outAddresses,
      seenAddresses,
      maxUniqueAddresses
    );
    return Mathf.Max(outAddresses.Count - startingCount, 0);
  }

  public int CollectPersistentEffectStartupAddresses(
    List<string> outAddresses,
    IReadOnlyList<string> animationKeys,
    HashSet<string> seenAddresses = null,
    int maxUniqueAddresses = int.MaxValue
  ) {
    if (outAddresses == null || animationKeys == null || animationKeys.Count <= 0 || maxUniqueAddresses <= 0) {
      return 0;
    }

    if (!runtimeInitialized || effectNode == null) {
      return 0;
    }

    var startingCount = outAddresses.Count;
    var warmFrames = ResolveCoreEffectWarmFramesAtStartup();
    linkedEffectWarmKeyScratch.Clear();
    linkedEffectWarmKeySeenScratch.Clear();
    AnimationLinkUtility.CollectLinkedEffectKeys(
      Animations.Esperanza,
      animationKeys,
      linkedEffectWarmKeyScratch,
      linkedEffectWarmKeySeenScratch
    );
    CollectPersistentEffectStartupAddresses(Effects.Esperanza, linkedEffectWarmKeyScratch, warmFrames, outAddresses, seenAddresses, maxUniqueAddresses);
    CollectPersistentEffectStartupAddresses(Effects.Things, linkedEffectWarmKeyScratch, warmFrames, outAddresses, seenAddresses, maxUniqueAddresses);
    CollectPersistentEffectStartupAddresses(Effects.Imp, linkedEffectWarmKeyScratch, warmFrames, outAddresses, seenAddresses, maxUniqueAddresses);
    linkedProjectileWarmKeyScratch.Clear();
    linkedProjectileWarmKeySeenScratch.Clear();
    AnimationLinkUtility.CollectLinkedProjectileKeys(
      Animations.Esperanza,
      animationKeys,
      linkedProjectileWarmKeyScratch,
      linkedProjectileWarmKeySeenScratch
    );
    if (projectileManager != null &&
        linkedProjectileWarmKeyScratch.Count > 0 &&
        outAddresses.Count < maxUniqueAddresses) {
      projectileManager.EnsurePoolsReady(linkedProjectileWarmKeyScratch);
      projectileManager.CollectPersistentStartupAddresses(
        linkedProjectileWarmKeyScratch,
        outAddresses,
        seenAddresses,
        maxUniqueAddresses,
        warmFrames
      );
    }
    linkedEffectWarmKeyScratch.Clear();
    linkedEffectWarmKeySeenScratch.Clear();
    linkedProjectileWarmKeyScratch.Clear();
    linkedProjectileWarmKeySeenScratch.Clear();
    return Mathf.Max(outAddresses.Count - startingCount, 0);
  }

  public int CollectPersistentProjectileStartupAssetAddresses(
    List<string> outAddresses,
    IReadOnlyList<string> animationKeys,
    HashSet<string> seenAddresses = null,
    int maxUniqueAddresses = int.MaxValue
  ) {
    if (outAddresses == null || animationKeys == null || animationKeys.Count <= 0 || maxUniqueAddresses <= 0) {
      return 0;
    }

    if (!runtimeInitialized) {
      return 0;
    }

    ResolveProjectileManagerReference("startup_asset_collection");
    if (projectileManager == null) {
      return 0;
    }

    var startingCount = outAddresses.Count;
    linkedProjectileWarmKeyScratch.Clear();
    linkedProjectileWarmKeySeenScratch.Clear();
    AnimationLinkUtility.CollectLinkedProjectileKeys(
      Animations.Esperanza,
      animationKeys,
      linkedProjectileWarmKeyScratch,
      linkedProjectileWarmKeySeenScratch
    );
    if (linkedProjectileWarmKeyScratch.Count > 0) {
      projectileManager.CollectPersistentStartupAssetAddresses(
        linkedProjectileWarmKeyScratch,
        outAddresses,
        seenAddresses,
        maxUniqueAddresses
      );
    }
    linkedProjectileWarmKeyScratch.Clear();
    linkedProjectileWarmKeySeenScratch.Clear();
    return Mathf.Max(outAddresses.Count - startingCount, 0);
  }

  int ResolveCoreEffectWarmFramesAtStartup() {
    var configuredFrames = Mathf.Max(SpriteStreamingRuntimeSettings.AnimationWarmupFrames, 1);
    return Mathf.Max(configuredFrames, MinimumCoreEffectWarmFramesAtStartup);
  }

  void CollectPersistentSkinStartupAddresses(
    GameObject[] skinObjects,
    int startupWarmFrames,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxUniqueAddresses
  ) {
    if (skinObjects == null || skinObjects.Length == 0 || outAddresses == null) {
      return;
    }

    for (var i = 0; i < skinObjects.Length; i++) {
      if (outAddresses.Count >= maxUniqueAddresses) {
        return;
      }

      var go = skinObjects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      CollectPersistentSkinStartupAddresses(
        target,
        startupWarmFrames,
        outAddresses,
        seenAddresses,
        maxUniqueAddresses
      );
    }
  }

  void CollectPersistentSkinStartupAddresses(
    SpriteWithNormals target,
    int startupWarmFrames,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxUniqueAddresses
  ) {
    if (target == null || outAddresses == null || maxUniqueAddresses <= 0) {
      return;
    }

    if (!target.IsAnimation) {
      target.CollectAnimationWindowAddresses(
        target.category,
        0,
        0,
        0,
        outAddresses,
        seenAddresses,
        maxUniqueAddresses
      );
      return;
    }

    var warmFrames = Mathf.Max(startupWarmFrames, 1);
    foreach (var animationPair in Animations.Esperanza) {
      if (outAddresses.Count >= maxUniqueAddresses) {
        return;
      }

      var animationName = animationPair.Key;
      var animationData = animationPair.Value;
      if (animationData == null || string.IsNullOrWhiteSpace(animationName)) {
        continue;
      }

      var category = ResolveEsperanzaAnimationCategory(animationName, animationData);
      var clipStart = Mathf.Max(animationData.start, 1);
      var clipEnd = Mathf.Min(Mathf.Max(animationData.end, clipStart), clipStart + warmFrames - 1);
      target.CollectAnimationWindowAddresses(
        category,
        clipStart,
        clipEnd,
        0,
        outAddresses,
        seenAddresses,
        maxUniqueAddresses
      );
    }
  }

  void CollectPersistentEffectStartupAddresses(
    Dictionary<string, EffectData> effects,
    IReadOnlyList<string> effectKeys,
    int warmFrames,
    List<string> outAddresses,
    HashSet<string> seenAddresses,
    int maxUniqueAddresses
  ) {
    if (effects == null || effectNode == null || outAddresses == null || maxUniqueAddresses <= 0) {
      return;
    }

    for (var i = 0; i < effectKeys.Count; i++) {
      if (outAddresses.Count >= maxUniqueAddresses) {
        return;
      }

      var effectKey = string.IsNullOrWhiteSpace(effectKeys[i]) ? "" : effectKeys[i].Trim();
      if (string.IsNullOrWhiteSpace(effectKey) || !effects.TryGetValue(effectKey, out var effectData) || effectData == null) {
        continue;
      }

      var startFrame = Mathf.Max(effectData.start, 1);
      var endFrame = Mathf.Max(startFrame, Mathf.Min(Mathf.Max(effectData.end, startFrame), startFrame + Mathf.Max(warmFrames, 1) - 1));
      effectNode.CollectAnimationWindowAddresses(
        effectKey,
        startFrame,
        endFrame,
        0,
        outAddresses,
        seenAddresses,
        maxUniqueAddresses
      );
    }
  }

  public void LoadGear() {
    EnsureRuntimeInitialized("load_gear");
    EquippedItems.EnsureKnownForms();
    GetSavedGearState();
    RefreshGear();
    PrimeEquippedAnimationStartsIfLoading();
    PrimeCoreCombatEffectWarmup("load_gear");
    MessageBus.Send("gearReady");
  }

  void OnDestroy() {
#if UNITY_EDITOR
    if (!Application.isPlaying) {
      Selection.activeObject = null;
    }
#endif
    StopEquipWarmupQueue();
    ReleaseCoreCombatEffectWarmupPins();
    animationController?.Cleanup(!Application.isPlaying);
    if (effectControllerInitialized) {
      effectAnimationController.Cleanup(!Application.isPlaying);
    }
  }

  void OnDisable() {
    StopEquipWarmupQueue();
    effectResetToEmptyPending = false;
    animationController?.Cleanup(false);
    if (effectControllerInitialized) {
      effectAnimationController.Cleanup(false);
    }
    ResetEffectVisualToEmpty();
  }

  void PrimeCoreCombatEffectWarmup(string source) {
    if (!Application.isPlaying) return;
    if (string.IsNullOrWhiteSpace(coreEffectWarmOwnerId)) return;

    coreEffectWarmupAddressScratch.Clear();
    coreEffectWarmupSeenAddressScratch.Clear();
    var maxAddresses = Mathf.Max(SpriteStreamingRuntimeSettings.PinBudgetEffectAddresses, 1);
    var collectedCount = CollectPersistentEffectStartupAddresses(
      coreEffectWarmupAddressScratch,
      CoreCombatWarmAnimationKeys,
      coreEffectWarmupSeenAddressScratch,
      maxAddresses
    );
    if (collectedCount <= 0 || coreEffectWarmupAddressScratch.Count <= 0) {
      if (ShouldLogRuntimeInitDebug()) {
        Debug.LogWarning(
          "[GearController] CoreCombatEffectWarmupSkipped" +
          " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
          " object=" + gameObject.name +
          " effect_library='" + (effectNode != null ? effectNode.libraryName : "") + "'" +
          " addresses=" + coreEffectWarmupAddressScratch.Count
        );
      }
      coreEffectWarmupAddressScratch.Clear();
      coreEffectWarmupSeenAddressScratch.Clear();
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      coreEffectWarmOwnerId,
      TextureResidencyCache.PinClass.Effect,
      coreEffectWarmupAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup
    );
    TextureResidencyCache.RequestLoadBatch(
      coreEffectWarmupAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup,
      allowAtlasExpansion: true
    );

    if (ShouldLogRuntimeInitDebug()) {
      Debug.Log(
        "[GearController] CoreCombatEffectWarmup" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " object=" + gameObject.name +
        " addresses=" + coreEffectWarmupAddressScratch.Count +
        " animations=" + CoreCombatWarmAnimationKeys.Length +
        " owner=" + coreEffectWarmOwnerId
      );
    }

    coreEffectWarmupAddressScratch.Clear();
    coreEffectWarmupSeenAddressScratch.Clear();
  }

  void ReleaseCoreCombatEffectWarmupPins() {
    if (string.IsNullOrWhiteSpace(coreEffectWarmOwnerId)) return;
    TextureResidencyCache.ReleaseOwnerPins(coreEffectWarmOwnerId);
  }

  public void GetSavedGearState() {
    EquippedItems.EnsureKnownForms();
    var loaded = SaveSlotManager.Load("equippedGear");
    if (loaded.Keys.Count == 0) return;
    foreach (var form in loaded.GetComplex<Dictionary<string, Dictionary<string, GearItem>>>("allGear")) {
      EquippedItems.EnsureForm(form.Key);
      foreach (var slot in form.Value) {
        if (!EquippedItems.AllGearForms[form.Key].ContainsKey(slot.Key)) {
          EquippedItems.AllGearForms[form.Key][slot.Key] = null;
        }
        EquippedItems.AllGearForms[form.Key][slot.Key] = EquippedItems.CloneGearItem(slot.Value);
      }
    }
  }

  public void SetGearIntoSlot(string slot, GearItem gearItem) {
    EquippedItems.EnsureForm(EsperanzaForms.GetActive());
    EquippedItems.AllGearForms[EsperanzaForms.GetActive()][slot] = gearItem;
    gameData.SetComplex("allGear", EquippedItems.AllGearForms);
    SaveSlotManager.Save("equippedGear", gameData);
    MarkAppearanceRevision();
  }

  public void RefreshGear() {
    UnequipGear();
    EquipGear();
    MarkAppearanceRevision();
  }

  public void UnequipGear() {
    StopEquipWarmupQueue();
    ReleaseRuntimeAppearancePinsForGearSwap();
    OffloadUnequippedGearResidency();
    if (GearObjects != null) {
      foreach (GameObject go in GearObjects) {
        var sn = go.GetComponent<SpriteWithNormals>();
        if (sn != null) {
          sn.SetLabelPrefix("");
          sn.SetDoNotRender(true);
        }
      }
    }
    foreach (GameObject bounceParent in combinedBounces ?? Array.Empty<GameObject>()) {
      if (bounceParent == null) continue;
      foreach (Transform child in bounceParent.transform) {
        child.gameObject.SetActive(false);
      }
    }
    animationController?.InvalidateSpriteFrameCache();
    if (effectControllerInitialized) {
      effectAnimationController?.InvalidateSpriteFrameCache();
    }
  }

  void ReleaseRuntimeAppearancePinsForGearSwap() {
    if (!Application.isPlaying) return;
    animationController?.ReleaseAppearancePins();
    if (!effectControllerInitialized) return;
    effectAnimationController?.ReleaseAppearancePins();
  }

  void OffloadUnequippedGearResidency() {
    if (!Application.isPlaying) return;
    // Gear swap lifecycle: old gear sprites are no longer pinned after release.
    // Evict unpinned completed entries now so old gear residency does not linger.
    TextureResidencyCache.EvictAllUnpinnedCompleted();
  }

  public void EquipGear() {
    var activeForm = EsperanzaForms.GetActive();
    EquippedItems.EnsureForm(activeForm);
    var equippedItems = EquippedItems.AllGearForms;
    equipPartPrefixScratch.Clear();
    foreach (KeyValuePair<string, GearItem> equip in equippedItems[activeForm]) {
      if (equip.Value == null && equip.Key != "Head") continue;
      var gearId = "";
      if (equip.Value == null && equip.Key == "Head") gearId = activeForm + "_no_Head";
      else gearId = $"{equip.Value.gearId}_{equip.Key}";
      if (!EsperanzaGearParts.gearParts.TryGetValue(gearId, out var parts) || parts == null) {
        Debug.LogError($"No parts found for equipped gearId: {gearId}");
        continue;
      }
      var formId = equip.Value != null ? equip.Value.gearId : "";
      for (var partIndex = 0; partIndex < parts.Count; partIndex++) {
        var partName = parts[partIndex];
        if (string.IsNullOrWhiteSpace(partName)) continue;
        equipPartPrefixScratch[partName] = formId;
      }
      if (GearObjects != null) {
        foreach (GameObject go in GearObjects) {
          foreach (string part in parts) {
            if (go != null && go.name.Equals(part)) {
              var spriteWithNormals = go.GetComponent<SpriteWithNormals>();
              var shaderAnimator = go.GetComponent<AllIn1AnimatorInspector>();
              if (spriteWithNormals != null) {
                spriteWithNormals.SetDoNotRender(false);
                spriteWithNormals.SetIsAnimation(true);
                spriteWithNormals.SetLabelPrefix(formId);
              }
              else Debug.LogWarning($"GameObject {go.name} does not have a SpriteWithNormals component attached.");
              if (shaderAnimator != null && equip.Value != null) {
                shaderAnimator.ResetActive();
                shaderAnimator.Reset();
                var newColor = ShaderColors.myColors[equip.Value.gearColor];
                shaderAnimator.SetKeyword("GLOW_ON", true);
                shaderAnimator.AddFloatSequence("_Glow", 4f, 4f, 1f, replaceExisting: true);
                shaderAnimator.AddColorSequence("_GlowColor", newColor, newColor, 1f, replaceExisting: true);
                shaderAnimator.AddColorSequence("_Color", newColor, newColor, 1f, replaceExisting: true);
              }
              else if (shaderAnimator == null) {
                Debug.LogWarning($"GameObject {go.name} does not have a AllIn1AnimatorInspector component attached.");
              }
            }
          }
        }
      }
      if (combinedBounces != null) {
        foreach (GameObject bounceParent in combinedBounces) {
          if (bounceParent == null) continue;
          foreach (Transform child in bounceParent.transform) {
            if (child != null && child.gameObject.name.Equals(gearId)) {
              child.gameObject.SetActive(true);
              var spriteRenderer = child.gameObject.GetComponent<SpriteRenderer>();
              var shaderAnimator = child.gameObject.GetComponent<AllIn1AnimatorInspector>();
              if (shaderAnimator != null && (equip.Value != null || gearId == activeForm + "_no_Head")) {
                var gearColor = "";
                shaderAnimator.ResetActive();
                shaderAnimator.Reset();
                if (gearId == activeForm + "_no_Head") {
                  gearColor = ShaderColors.pairs[activeForm]["primary"]["color"];
                  HairSkin.GetComponent<SpriteRenderer>().color = ShaderColors.myColors[gearColor];
                }
                else {
                  gearColor = equip.Value.gearColor;
                }
                var newColor = ShaderColors.myColors[gearColor];
                shaderAnimator.SetKeyword("GLOW_ON", true);
                shaderAnimator.AddFloatSequence("_Glow", 6f, 6f, 1f, replaceExisting: true);
                shaderAnimator.AddColorSequence("_GlowColor", newColor, newColor, 1f, replaceExisting: true);
                shaderAnimator.AddColorSequence("_Color", newColor, newColor, 1f, replaceExisting: true);
                spriteRenderer.color = newColor;
              }
            }
          }
        }
      }
    }

    animationController?.InvalidateSpriteFrameCache();
    if (effectControllerInitialized) {
      effectAnimationController?.InvalidateSpriteFrameCache();
    }
    QueueWarmupForEquippedCharacter(new Dictionary<string, string>(equipPartPrefixScratch, StringComparer.OrdinalIgnoreCase));
  }

  void QueueWarmupForEquippedCharacter(Dictionary<string, string> equippedPartPrefixes) {
    if (!Application.isPlaying || !queueEquippedAnimationWarmup) return;
    pendingEquipWarmupPartPrefixes = equippedPartPrefixes != null
      ? new Dictionary<string, string>(equippedPartPrefixes, StringComparer.OrdinalIgnoreCase)
      : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    TryStartPendingEquipWarmup("queue_request");
  }

  void TryStartPendingEquipWarmup(string source) {
    if (!Application.isPlaying || !queueEquippedAnimationWarmup) return;
    if (pendingEquipWarmupPartPrefixes == null) return;
    if (!isActiveAndEnabled || !gameObject.activeInHierarchy) {
      Debug.Log(
        "[GearController] DeferredEquipWarmup" +
        " source=" + (string.IsNullOrWhiteSpace(source) ? "-" : source.Trim()) +
        " object=" + gameObject.name +
        " enabled=" + (isActiveAndEnabled ? 1 : 0) +
        " active_self=" + (gameObject.activeSelf ? 1 : 0) +
        " active_hierarchy=" + (gameObject.activeInHierarchy ? 1 : 0)
      );
      return;
    }

    StopEquipWarmupQueue();
    equipWarmupRoutine = StartCoroutine(WarmEquippedCharacterRoutine(pendingEquipWarmupPartPrefixes));
  }

  void StopEquipWarmupQueue() {
    if (equipWarmupRoutine == null) return;
    StopCoroutine(equipWarmupRoutine);
    equipWarmupRoutine = null;
  }

  IEnumerator WarmEquippedCharacterRoutine(Dictionary<string, string> equippedPartPrefixes) {
    equipWarmupAddressScratch.Clear();
    equipWarmupSeenAddressScratch.Clear();
    var loadingOverlayActive = SpriteStreamingLoadingState.IsLoadingOverlayActive;
    var overlayWarmGateManaged = loadingOverlayActive &&
                                 StreamingWarmOrchestrator.IsWarmGateRunning;

    yield return CollectSkinWarmupAddresses(loadingOverlayActive);
    yield return CollectEquippedGearWarmupAddresses(equippedPartPrefixes, loadingOverlayActive);

    var queuedAddressCount = equipWarmupAddressScratch.Count;
    if (queuedAddressCount > 0) {
      var enqueueBudget = Mathf.Max(equipWarmupEnqueueBudgetPerFrame, 50);
      if (!loadingOverlayActive) {
        // Keep gameplay-time warmup background-friendly so it does not create trigger hitches.
        enqueueBudget = Mathf.Min(enqueueBudget, 64);
      }
      yield return TextureResidencyCache.RequestLoadBatchThrottled(
        equipWarmupAddressScratch,
        TextureResidencyCache.LoadPriority.Warmup,
        // Atlas-first preload for equipped parts so frame slices resolve from resident atlases in gameplay.
        allowAtlasExpansion: true,
        enqueueBudgetPerFrame: enqueueBudget,
        warmGateManaged: overlayWarmGateManaged
      );
    }

    if (logEquipWarmupSummary) {
      Debug.Log(
        "[GearController] EquipWarmupComplete" +
        " queued=" + queuedAddressCount +
        " unique=" + equipWarmupSeenAddressScratch.Count +
        " mapped_parts=" + (equippedPartPrefixes != null ? equippedPartPrefixes.Count : 0) +
        " overlay_active=" + (loadingOverlayActive ? 1 : 0) +
        " warm_gate_running=" + (overlayWarmGateManaged ? 1 : 0)
      );
    }

    equipWarmupAddressScratch.Clear();
    equipWarmupSeenAddressScratch.Clear();
    if (ReferenceEquals(pendingEquipWarmupPartPrefixes, equippedPartPrefixes)) {
      pendingEquipWarmupPartPrefixes = null;
    }
    equipWarmupRoutine = null;
  }

  void PrimeEquippedAnimationStartsIfLoading() {
    if (!Application.isPlaying) return;
    if (!SpriteStreamingLoadingState.IsLoadingOverlayActive) return;
    var warmFrames = Mathf.Max(prewarmFramesPerAnimation, MinimumPlayerWarmFramesAtStartup);
    animationController?.PrimeAllAnimationStarts(warmFrames);
    PrimeLinkedEffectAnimationWarmup(Animations.Esperanza, "equip_loading");
  }

  IEnumerator CollectSkinWarmupAddresses(bool overlayWarmGateActive) {
    if (SkinObjects == null || SkinObjects.Length == 0) yield break;

    for (var i = 0; i < SkinObjects.Length; i++) {
      var go = SkinObjects[i];
      if (go == null) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (target == null) continue;
      yield return CollectFullEsperanzaAnimationAddresses(target, overlayWarmGateActive);
    }
  }

  IEnumerator CollectEquippedGearWarmupAddresses(Dictionary<string, string> equippedPartPrefixes, bool overlayWarmGateActive) {
    if (GearObjects == null || GearObjects.Length == 0) yield break;
    if (equippedPartPrefixes == null || equippedPartPrefixes.Count == 0) yield break;

    for (var i = 0; i < GearObjects.Length; i++) {
      var go = GearObjects[i];
      if (go == null) continue;
      if (!equippedPartPrefixes.TryGetValue(go.name, out var mappedPrefix)) continue;
      var target = go.GetComponent<SpriteWithNormals>();
      if (target == null) continue;

      var normalizedMappedPrefix = mappedPrefix ?? "";
      if (!string.Equals(target.labelPrefix ?? "", normalizedMappedPrefix, StringComparison.OrdinalIgnoreCase)) {
        target.SetLabelPrefix(normalizedMappedPrefix);
      }

      yield return CollectFullEsperanzaAnimationAddresses(target, overlayWarmGateActive);
    }
  }

  IEnumerator CollectFullEsperanzaAnimationAddresses(SpriteWithNormals target, bool overlayWarmGateActive) {
    if (target == null) yield break;
    if (!target.IsAnimation) {
      target.CollectAnimationWindowAddresses(
        target.category,
        0,
        0,
        lookAheadFrames: 0,
        equipWarmupAddressScratch,
        equipWarmupSeenAddressScratch
      );
      yield break;
    }

    var chunkFrames = overlayWarmGateActive
      ? Mathf.Max(equipWarmupFrameChunk, 1)
      : Mathf.Clamp(equipWarmupFrameChunk, 4, 8);
    var chunksPerFrame = overlayWarmGateActive
      ? Mathf.Max(equipWarmupChunksPerFrame, 1)
      : 1;
    var chunksSinceYield = 0;

    foreach (var animationPair in Animations.Esperanza) {
      var animationName = animationPair.Key;
      var anim = animationPair.Value;
      if (anim == null || string.IsNullOrWhiteSpace(animationName)) continue;

      var category = ResolveEsperanzaAnimationCategory(animationName, anim);
      var clipStart = Mathf.Max(anim.start, 1);
      var clipEnd = Mathf.Max(anim.end, clipStart);
      for (var frameStart = clipStart; frameStart <= clipEnd; frameStart += chunkFrames) {
        var frameEnd = Mathf.Min(frameStart + chunkFrames - 1, clipEnd);
        target.CollectAnimationWindowAddresses(
          category,
          frameStart,
          frameEnd,
          lookAheadFrames: 0,
          equipWarmupAddressScratch,
          equipWarmupSeenAddressScratch
        );
        chunksSinceYield++;
        if (chunksSinceYield < chunksPerFrame) continue;
        chunksSinceYield = 0;
        yield return null;
      }
    }
  }

  static string ResolveEsperanzaAnimationCategory(string animationName, AnimData anim) {
    if (anim == null) return animationName ?? "";
    return anim.To == 1 ? "To" : anim.To == 2 ? "To2" : animationName;
  }

  public string CurrentAnimation => animationController != null ? animationController.CurrentAnimation : null;

  public void _TogglePause() {
    TogglePause();
  }

  public void TogglePause(string forcePause = null) {
    animationController?.TogglePause(forcePause);
    if (effectControllerInitialized) {
      effectAnimationController.TogglePause(forcePause);
    }
  }

  public void ForceAnimation() {
    if (animationController == null) return;
    animationController.ForceAnimation(string.IsNullOrEmpty(defaultAnimation) ? null : defaultAnimation);
  }

  public void PlayAnimation(string anim, bool forceRestart = false, bool resolveInterrupts = true) {
    if (string.IsNullOrEmpty(anim)) return;
    animationController?.PlayAnimation(anim, forceRestart, resolveInterrupts);
  }

  public AnimationController Controller => animationController;

  private void HookAnimationEvents() {
    if (animationController == null) return;
    animationController.OnEffectTriggered = HandleEffectTriggered;
    animationController.OnProjectileTriggered = HandleProjectileTriggered;
  }

  private void ConfigureEffectController() {
    if (effectNode == null) return;
    BuildEffectAnimations();
    effectAnimationController.Initialize(
      effectNode.transform,
      new[] { effectNode.gameObject },
      null,
      null,
      effectAnimations,
      new Dictionary<string, Dictionary<string, string>>(),
      null,
      new Dictionary<string, Dictionary<string, List<HBox>>>(),
      "",
      false,
      effectAppearanceOwnerId,
      TextureResidencyCache.PinClass.Effect
    );
    effectControllerInitialized = true;
    ResetEffectVisualToEmpty();
  }

  private void BuildEffectAnimations() {
    effectAnimations.Clear();
    AddEffectAnimations(Effects.Esperanza);
    AddEffectAnimations(Effects.Things);
    AddEffectAnimations(Effects.Imp);
  }

  private void AddEffectAnimations(Dictionary<string, EffectData> effects) {
    if (effects == null) return;
    foreach (var kvp in effects) {
      if (string.IsNullOrEmpty(kvp.Key) || kvp.Value == null) continue;
      effectAnimations[kvp.Key] = new AnimData {
        start = kvp.Value.start,
        end = kvp.Value.end,
        duration = kvp.Value.duration * 1000f
      };
    }
  }

  private void HandleEffectTriggered(string effectKey) {
    if (string.IsNullOrEmpty(effectKey) || effectNode == null) return;
    if (!effectControllerInitialized) {
      ConfigureEffectController();
      if (!effectControllerInitialized) return;
    }
    PrepareEffectVisualForPlayback();
    effectResetToEmptyPending = true;
    effectAnimationController.ForceLoop = false;
    effectAnimationController.PlayAnimation(effectKey, true, resolveInterrupts: false);
  }

  private void HandleProjectileTriggered(string projectileKey) {
    if (string.IsNullOrEmpty(projectileKey)) return;
    ResolveProjectileManagerReference("projectile_event");
    if (projectileManager == null) {
      Debug.LogWarning(
        "[GearController] MissingProjectileManager" +
        " object=" + gameObject.name +
        " projectile='" + projectileKey + "'"
      );
      return;
    }
    var spawnPosition = ResolveProjectileSpawnPosition();
    var direction = ResolveProjectileDirection();
    projectileManager.SpawnProjectile(projectileKey, spawnPosition, direction);
  }

  void PrepareEffectVisualForPlayback() {
    if (effectNode == null) return;
    effectNode.SetDoNotRender(false);
    if (!string.IsNullOrWhiteSpace(effectNode.labelPrefix)) {
      effectNode.SetLabelPrefix("");
    }
  }

  void TryFinalizeCompletedEffectAnimation() {
    if (!effectControllerInitialized || !effectResetToEmptyPending) return;
    if (effectAnimationController.IsPlaying) return;
    ResetEffectVisualToEmpty();
  }

  void ResetEffectVisualToEmpty() {
    if (effectNode == null) return;
    effectNode.SetDoNotRender(false);
    effectNode.SetLabelPrefix("Empty");
    effectNode.ForceUpdateSpriteAndNormal(0);
    effectNode.SetLabelPrefix("");
    effectResetToEmptyPending = false;
  }

  void ResolveProjectileManagerReference(string source) {
    if (projectileManager != null || !Application.isPlaying) return;
    projectileManager = SingleSceneManager.ResolveGameplayProjectileManager();
    if (projectileManager == null || !ShouldLogRuntimeInitDebug()) return;
    Debug.Log(
      "[GearController] ResolvedProjectileManager" +
      " source=" + NormalizeDebugValue(source) +
      " object=" + gameObject.name +
      " manager='" + projectileManager.gameObject.name + "'" +
      " path='" + GetTransformPath(projectileManager.transform) + "'"
    );
  }

  void PrimeLinkedEffectAnimationWarmup(Dictionary<string, AnimData> animationManifest, string source) {
    if (!Application.isPlaying || !effectControllerInitialized || animationManifest == null || animationManifest.Count <= 0) {
      return;
    }

    linkedEffectWarmKeyScratch.Clear();
    linkedEffectWarmKeySeenScratch.Clear();
    AnimationLinkUtility.CollectLinkedEffectKeys(
      animationManifest,
      null,
      linkedEffectWarmKeyScratch,
      linkedEffectWarmKeySeenScratch
    );
    if (linkedEffectWarmKeyScratch.Count > 0) {
      effectAnimationController.PrimeAnimationStarts(linkedEffectWarmKeyScratch, 1);
    }

    if (linkedEffectWarmKeyScratch.Count <= 0 || !ShouldLogRuntimeInitDebug()) {
      linkedEffectWarmKeyScratch.Clear();
      linkedEffectWarmKeySeenScratch.Clear();
      return;
    }

    Debug.Log(
      "[GearController] PrimedLinkedEffects" +
      " source=" + NormalizeDebugValue(source) +
      " object=" + gameObject.name +
      " count=" + linkedEffectWarmKeyScratch.Count
    );
    linkedEffectWarmKeyScratch.Clear();
    linkedEffectWarmKeySeenScratch.Clear();
  }

  static string NormalizeDebugValue(string value) {
    return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
  }

  static string GetTransformPath(Transform current) {
    if (current == null) return "";
    if (current.parent == null) return current.name;
    return GetTransformPath(current.parent) + "/" + current.name;
  }

  private Vector3 ResolveProjectileSpawnPosition() {
    if (projectileSpawn != null) return projectileSpawn.position;
    if (effectNode != null) return effectNode.transform.position;
    return transform.position;
  }

  private Vector3 ResolveProjectileDirection() {
    if (useFacingDirection) {
      return IsFacingRight ? Vector3.right : Vector3.left;
    }
    if (projectileDirection.sqrMagnitude <= 0.0001f) return Vector3.right;
    var dir = projectileDirection.normalized;
    return new Vector3(dir.x, dir.y, 0f);
  }

  void MarkAppearanceRevision() {
    if (appearanceRevision == int.MaxValue) {
      appearanceRevision = 1;
      return;
    }
    appearanceRevision++;
  }
}
