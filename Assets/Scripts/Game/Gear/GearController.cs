using System;
using System.Collections.Generic;
using System.Linq;
using CustomInspector;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class GearController : MonoBehaviour {
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
  private GameObject[] combinedBounces;
  private SaveData gameData = new();
  private AnimationController animationController = new();
  private AnimationController effectAnimationController = new();
  private readonly Dictionary<string, AnimData> effectAnimations = new();
  private bool effectControllerInitialized;
  private string appearanceOwnerId;
  private string effectAppearanceOwnerId;
  private int appearanceRevision = 1;

  public bool IsFacingRight => animationController != null && animationController.IsFacingRight;
  public int AppearanceRevision => appearanceRevision;


  void Start() {
    ResetDebugPlaybackFlags();
    appearanceOwnerId = "player:" + gameObject.GetInstanceID().ToString();
    effectAppearanceOwnerId = effectNode != null ? "effect:" + effectNode.GetInstanceID().ToString() : "";
    combinedBounces = (HairObjects ?? Array.Empty<GameObject>()).Concat(OtherBounceGearObjects ?? Array.Empty<GameObject>()).ToArray();
    NormalizeSkinSpriteDefaultsForRuntime();
    PrimeSpriteStreamingWarmup();
    ConfigureAnimationController();
    ConfigureEffectController();
    PrimeControllerAnimationWarmup();
    HookAnimationEvents();
    if (Application.isPlaying) {
      LeanTween.reset();
      LeanTween.init(4000);
    }
    animationController.PlayAnimation(defaultAnimation, true);
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

    TickControllers(Time.deltaTime);
  }

  void TickControllers(float deltaTime) {
    if (deltaTime <= 0f) return;
    animationController?.Tick(deltaTime);
    if (effectControllerInitialized) {
      effectAnimationController.Tick(deltaTime);
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
    var warmFrames = Mathf.Max(prewarmFramesPerAnimation, 1);
    animationController?.PrimeAllAnimationStarts(warmFrames);
    if (effectControllerInitialized) {
      effectAnimationController?.PrimeAllAnimationStarts(1);
    }
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

  public void LoadGear() {
     if (!Application.isPlaying) {
      Start();
    }
    GetSavedGearState();
    RefreshGear();
    MessageBus.Send("gearReady");
  }

  void OnDestroy() {
#if UNITY_EDITOR
    if (!Application.isPlaying) {
      Selection.activeObject = null;
    }
#endif
    animationController?.Cleanup(!Application.isPlaying);
    if (effectControllerInitialized) {
      effectAnimationController.Cleanup(!Application.isPlaying);
    }
  }

  void OnDisable() {
    animationController?.Cleanup(false);
    if (effectControllerInitialized) {
      effectAnimationController.Cleanup(false);
    }
  }

  public void GetSavedGearState() {
    var loaded = SaveSlotManager.Load("equippedGear");
    if (loaded.Keys.Count == 0) return;
    foreach (var form in loaded.GetComplex<Dictionary<string, Dictionary<string, GearItem>>>("allGear")) {
      foreach (var slot in form.Value) {
        if (slot.Value == null) { continue; }
        EquippedItems.AllGearForms[EsperanzaForms.GetActive()][slot.Key] = slot.Value;
      }
    }
  }

  public void SetGearIntoSlot(string slot, GearItem gearItem) {
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
    if (Application.isPlaying) {
      TextureResidencyCache.PurgeAll();
    }
    if (GearObjects != null) {
      foreach (GameObject go in GearObjects) {
        var sn = go.GetComponent<SpriteWithNormals>();
        if (sn != null) {
          sn.SetLabelPrefix("");
          sn.SetDoNotRender(true);
        }
      }
    }
    foreach (GameObject bounceParent in combinedBounces) {
      foreach (Transform child in bounceParent.transform) {
        child.gameObject.SetActive(false);
      }
    }
  }

  public void EquipGear() {
    var activeForm = EsperanzaForms.GetActive();
    var equippedItems = EquippedItems.AllGearForms;
    foreach (KeyValuePair<string, GearItem> equip in equippedItems[activeForm]) {
      if (equip.Value == null && equip.Key != "Head") continue;
      var gearId = "";
      if (equip.Value == null && equip.Key == "Head") gearId = activeForm + "_no_Head";
      else gearId = $"{equip.Value.gearId}_{equip.Key}";
      if (!EsperanzaGearParts.ContainsKey(gearId)) Debug.LogError($"No parts found for equipped gearId: {gearId}");
      var parts = EsperanzaGearParts.gearParts[gearId];
      if (parts == null) Debug.LogError($"Null parts list returned for gearId: {gearId}");
      foreach (GameObject go in GearObjects) {
        foreach (string part in parts) {
          if (go != null && go.name.Equals(part)) {
            var spriteWithNormals = go.GetComponent<SpriteWithNormals>();
            var shaderAnimator = go.GetComponent<AllIn1AnimatorInspector>();
            if (spriteWithNormals != null) {
              var formId = equip.Value != null ? equip.Value.gearId : "";
              spriteWithNormals.SetDoNotRender(false);
              spriteWithNormals.SetIsAnimation(true);
              spriteWithNormals.SetLabelPrefix(formId);
            }
            else Debug.LogWarning($"GameObject {go.name} does not have a SpriteWithNormals component attached.");
            if (shaderAnimator != null) {
              shaderAnimator.ResetActive();
              shaderAnimator.Reset();
              var newColor = ShaderColors.myColors[equip.Value.gearColor];
              shaderAnimator.SetKeyword("GLOW_ON", true);
              shaderAnimator.AddFloatSequence("_Glow", 4f, 4f, 1f, replaceExisting: true);
              shaderAnimator.AddColorSequence("_GlowColor", newColor, newColor, 1f, replaceExisting: true);
              shaderAnimator.AddColorSequence("_Color", newColor, newColor, 1f, replaceExisting: true);
            }
            else {
              Debug.LogWarning($"GameObject {go.name} does not have a AllIn1AnimatorInspector component attached.");
            }
          }
        }
      }
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
    effectAnimationController.ForceLoop = false;
    effectAnimationController.PlayAnimation(effectKey, true, resolveInterrupts: false);
  }

  private void HandleProjectileTriggered(string projectileKey) {
    if (string.IsNullOrEmpty(projectileKey) || projectileManager == null) return;
    var spawnPosition = ResolveProjectileSpawnPosition();
    var direction = ResolveProjectileDirection();
    projectileManager.SpawnProjectile(projectileKey, spawnPosition, direction);
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
