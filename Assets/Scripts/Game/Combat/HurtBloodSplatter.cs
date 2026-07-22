using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared, pooled two-part blood reaction for actor-level Hurt animations.
/// Airborne sprays arc toward the actor's ground plane with a projected shadow;
/// landed decals remain until their scene unloads or the oldest decal is recycled.
/// </summary>
[DisallowMultipleComponent]
public sealed class HurtBloodSplatter : MonoBehaviour {
  static readonly string[] SprayAssetPaths = {
    "Assets/Sprites/Effects/Blood/BloodSpray.png",
    "Assets/Sprites/Effects/Blood/BloodSpray2.png",
    "Assets/Sprites/Effects/Blood/BloodSpray3.png"
  };
  static readonly string[] PuddleAssetPaths = {
    "Assets/Sprites/Effects/Blood/BloodPuddle.png",
    "Assets/Sprites/Effects/Blood/BloodPuddle2.png",
    "Assets/Sprites/Effects/Blood/BloodPuddle3.png"
  };
  const string HurtAnimation = "Hurt";
  const int AirborneCapacity = 24;
  const int PersistentDecalCapacity = 64;
  const float BloodSpawnChance = 0.5f;
  const float MinimumBloodScale = 0.5f;
  const float Gravity = 11.5f;
  const float DecalSettleDuration = 0.18f;

  sealed class AirborneSlot {
    public SpriteRenderer spray;
    public SpriteRenderer shadow;
    public Transform timeContext;
    public Vector3 position;
    public Vector2 velocity;
    public float groundY;
    public float baseScale;
    public float decalScale;
    public float rotationSpeed;
    public int variantIndex;
    public int sortingLayerId;
    public int groundSortingOrder;
    public bool active;
  }

  sealed class DecalSlot {
    public SpriteRenderer renderer;
    public Transform timeContext;
    public Vector3 settledScale;
    public float settleElapsed;
    public bool settling;
  }

  readonly struct PendingPlay {
    public readonly Transform actor;
    public readonly bool isFacingRight;

    public PendingPlay(Transform actor, bool isFacingRight) {
      this.actor = actor;
      this.isFacingRight = isFacingRight;
    }
  }

  static HurtBloodSplatter instance;
  static bool missingSpriteWarningLogged;

  readonly List<SpriteRenderer> rendererScratch = new(64);
  readonly List<PendingPlay> pendingPlays = new(AirborneCapacity);
  AirborneSlot[] airborneSlots;
  DecalSlot[] decalSlots;
  Sprite[] spraySprites;
  Sprite[] puddleSprites;
  TextureResidencyCache.Lease[] sprayLeases;
  TextureResidencyCache.Lease[] puddleLeases;
  int nextAirborneSlot;
  int nextDecalSlot;
  bool spritesReady;
  bool spriteLoadFailed;

  public static IReadOnlyList<string> BloodSprayAssetPaths => SprayAssetPaths;
  public static IReadOnlyList<string> BloodPuddleAssetPaths => PuddleAssetPaths;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetRuntimeState() {
    instance = null;
    missingSpriteWarningLogged = false;
  }

  /// <summary>
  /// Returns true when the supplied name is the shared actor-level Hurt animation.
  /// Kept here so player and enemy playback facades use the same contract.
  /// </summary>
  public static bool IsHurtAnimation(string animationName) {
    return string.Equals(animationName, HurtAnimation, System.StringComparison.OrdinalIgnoreCase);
  }

  public static void Play(Transform actor, bool isFacingRight) {
    if (!Application.isPlaying || actor == null || !actor.gameObject.activeInHierarchy) {
      return;
    }
    if (Random.value >= BloodSpawnChance) {
      return;
    }

    var manager = ResolveOrCreate(actor);
    manager?.QueueOrSpawn(actor, isFacingRight);
  }

  static HurtBloodSplatter ResolveOrCreate(Transform actor) {
    if (instance != null) {
      return instance;
    }

    var runner = new GameObject("__Hurt Blood Splatter VFX");
    var actorScene = actor.gameObject.scene;
    if (actorScene.IsValid() && actorScene.isLoaded) {
      SceneManager.MoveGameObjectToScene(runner, actorScene);
    }

    instance = runner.AddComponent<HurtBloodSplatter>();
    return instance;
  }

  void Awake() {
    if (instance != null && instance != this) {
      Destroy(gameObject);
      return;
    }

    instance = this;
    spraySprites = new Sprite[SprayAssetPaths.Length];
    puddleSprites = new Sprite[PuddleAssetPaths.Length];
    sprayLeases = new TextureResidencyCache.Lease[SprayAssetPaths.Length];
    puddleLeases = new TextureResidencyCache.Lease[PuddleAssetPaths.Length];
    BeginLoadingSprites(SprayAssetPaths, sprayLeases);
    BeginLoadingSprites(PuddleAssetPaths, puddleLeases);
    CreatePools();
  }

  static void BeginLoadingSprites(
    IReadOnlyList<string> assetPaths,
    TextureResidencyCache.Lease[] destination
  ) {
    for (var i = 0; i < assetPaths.Count; i++) {
      destination[i] = TextureResidencyCache.AcquireAsync(
        assetPaths[i],
        TextureResidencyCache.LoadPriority.Immediate
      );
    }
  }

  void OnDestroy() {
    ReleaseSpriteLeases(sprayLeases);
    ReleaseSpriteLeases(puddleLeases);
    if (instance == this) {
      instance = null;
    }
  }

  static void ReleaseSpriteLeases(TextureResidencyCache.Lease[] leases) {
    if (leases == null) return;
    for (var i = 0; i < leases.Length; i++) {
      leases[i]?.Release();
      leases[i] = null;
    }
  }

  void CreatePools() {
    airborneSlots = new AirborneSlot[AirborneCapacity];
    decalSlots = new DecalSlot[PersistentDecalCapacity];
  }

  SpriteRenderer CreateRenderer(string objectName, Sprite sprite) {
    var child = new GameObject(objectName);
    child.transform.SetParent(transform, false);
    var renderer = child.AddComponent<SpriteRenderer>();
    renderer.sprite = sprite;
    renderer.enabled = false;
    return renderer;
  }

  void QueueOrSpawn(Transform actor, bool isFacingRight) {
    if (spritesReady) {
      Spawn(actor, isFacingRight);
      return;
    }
    if (spriteLoadFailed) {
      return;
    }

    if (pendingPlays.Count >= AirborneCapacity) {
      pendingPlays.RemoveAt(0);
    }
    pendingPlays.Add(new PendingPlay(actor, isFacingRight));
  }

  void Spawn(Transform actor, bool isFacingRight) {
    if (!enabled || airborneSlots == null || airborneSlots.Length == 0) {
      return;
    }

    var groundPosition = ResolveGroundPosition(actor);
    var origin = ResolveImpactOrigin(actor);
    origin.y = Mathf.Max(origin.y, groundPosition.y + 0.55f);
    origin.z = groundPosition.z;

    ResolveGroundSorting(actor, groundPosition, out var sortingLayerId, out var groundSortingOrder);

    var direction = ResolveSprayDirection(actor, isFacingRight);
    var variantIndex = Random.Range(0, spraySprites.Length);
    var slot = AcquireAirborneSlot();
    HideAirborne(slot);

    slot.timeContext = actor;
    slot.position = origin;
    slot.velocity = new Vector2(
      direction * Random.Range(2.2f, 3.6f),
      Random.Range(2.8f, 4.6f)
    );
    slot.groundY = groundPosition.y + 0.02f;
    slot.baseScale = Random.Range(MinimumBloodScale, 0.72f);
    slot.decalScale = Random.Range(MinimumBloodScale, 0.78f);
    slot.rotationSpeed = direction * Random.Range(55f, 110f);
    slot.variantIndex = variantIndex;
    slot.sortingLayerId = sortingLayerId;
    slot.groundSortingOrder = groundSortingOrder;
    slot.active = true;

    ConfigureAirborneRenderer(slot.spray, sortingLayerId, groundSortingOrder + 2);
    slot.spray.sprite = spraySprites[variantIndex];
    slot.spray.flipX = direction < 0f;
    slot.spray.transform.SetPositionAndRotation(
      origin,
      Quaternion.Euler(0f, 0f, Random.Range(-12f, 12f))
    );
    slot.spray.transform.localScale = ClampBloodScale(new Vector3(
      slot.baseScale * Random.Range(0.88f, 1.16f),
      slot.baseScale * Random.Range(0.82f, 1.12f),
      1f
    ));
    slot.spray.color = Color.white;
    slot.spray.enabled = true;

    ConfigureAirborneRenderer(slot.shadow, sortingLayerId, groundSortingOrder - 2);
    slot.shadow.sprite = puddleSprites[variantIndex];
    slot.shadow.color = new Color(0f, 0f, 0f, 0.18f);
    slot.shadow.enabled = true;
    UpdateShadow(slot);
  }

  AirborneSlot AcquireAirborneSlot() {
    for (var offset = 0; offset < airborneSlots.Length; offset++) {
      var index = (nextAirborneSlot + offset) % airborneSlots.Length;
      var existing = airborneSlots[index];
      if (existing == null || existing.active) {
        continue;
      }

      nextAirborneSlot = (index + 1) % airborneSlots.Length;
      return existing;
    }

    for (var offset = 0; offset < airborneSlots.Length; offset++) {
      var index = (nextAirborneSlot + offset) % airborneSlots.Length;
      if (airborneSlots[index] != null) {
        continue;
      }

      var created = new AirborneSlot {
        spray = CreateRenderer("Air Spray " + index, spraySprites[0]),
        shadow = CreateRenderer("Air Shadow " + index, puddleSprites[0])
      };
      airborneSlots[index] = created;
      nextAirborneSlot = (index + 1) % airborneSlots.Length;
      return created;
    }

    var recycled = airborneSlots[nextAirborneSlot];
    nextAirborneSlot = (nextAirborneSlot + 1) % airborneSlots.Length;
    return recycled;
  }

  static void ConfigureAirborneRenderer(
    SpriteRenderer renderer,
    int sortingLayerId,
    int sortingOrder
  ) {
    renderer.sortingLayerID = sortingLayerId;
    renderer.sortingOrder = sortingOrder;
  }

  void Update() {
    UpdateSpriteLoading();
    UpdateAirborneSlots();
    UpdateSettlingDecals();
  }

  void UpdateSpriteLoading() {
    if (spritesReady || spriteLoadFailed) {
      return;
    }

    TextureResidencyCache.PumpOncePerFrame();
    if (!AreAllLeasesDone(sprayLeases) || !AreAllLeasesDone(puddleLeases)) {
      return;
    }

    var allSpritesLoaded = CopyLoadedSprites(sprayLeases, spraySprites);
    allSpritesLoaded &= CopyLoadedSprites(puddleLeases, puddleSprites);
    if (!allSpritesLoaded || spraySprites.Length != puddleSprites.Length) {
      spriteLoadFailed = true;
      pendingPlays.Clear();
      if (!missingSpriteWarningLogged) {
        missingSpriteWarningLogged = true;
        Debug.LogWarning(
          "[HurtBloodSplatter] One or more Addressable sprites are missing under " +
          "'Assets/Sprites/Effects/Blood'. Expected " + spraySprites.Length +
          " matched spray/puddle sets."
        );
      }
      return;
    }

    spritesReady = true;
    for (var i = 0; i < pendingPlays.Count; i++) {
      var pending = pendingPlays[i];
      if (pending.actor == null || !pending.actor.gameObject.activeInHierarchy) {
        continue;
      }
      Spawn(pending.actor, pending.isFacingRight);
    }
    pendingPlays.Clear();
  }

  static bool AreAllLeasesDone(TextureResidencyCache.Lease[] leases) {
    if (leases == null || leases.Length == 0) return false;
    for (var i = 0; i < leases.Length; i++) {
      if (leases[i] == null || !leases[i].IsDone) return false;
    }
    return true;
  }

  static bool CopyLoadedSprites(
    IReadOnlyList<TextureResidencyCache.Lease> leases,
    Sprite[] destination
  ) {
    if (leases == null || destination == null || leases.Count != destination.Length) {
      return false;
    }

    var allLoaded = true;
    for (var i = 0; i < leases.Count; i++) {
      destination[i] = leases[i]?.Sprite;
      allLoaded &= destination[i] != null;
    }
    return allLoaded;
  }

  void UpdateAirborneSlots() {
    if (airborneSlots == null) {
      return;
    }

    for (var i = 0; i < airborneSlots.Length; i++) {
      var slot = airborneSlots[i];
      if (slot == null || !slot.active) {
        continue;
      }

      var deltaTime = TimeScale.GetDeltaTime(slot.timeContext);
      if (deltaTime <= 0f) {
        continue;
      }

      slot.velocity.y -= Gravity * deltaTime;
      slot.position += (Vector3)(slot.velocity * deltaTime);
      slot.spray.transform.position = slot.position;
      slot.spray.transform.Rotate(0f, 0f, slot.rotationSpeed * deltaTime);
      UpdateShadow(slot);

      if (slot.velocity.y >= 0f || slot.position.y > slot.groundY) {
        continue;
      }

      var landingPosition = new Vector3(slot.position.x, slot.groundY, slot.position.z);
      SpawnGroundDecal(
        landingPosition,
        slot.decalScale,
        slot.sortingLayerId,
        slot.groundSortingOrder - 1,
        slot.timeContext,
        puddleSprites[slot.variantIndex]
      );
      HideAirborne(slot);
    }
  }

  static void UpdateShadow(AirborneSlot slot) {
    var height = Mathf.Max(0f, slot.position.y - slot.groundY);
    var heightRatio = Mathf.Clamp01(height / 4f);
    var perspectiveScale = Mathf.Lerp(0.82f, 0.38f, heightRatio);
    var shadowScale = slot.baseScale * perspectiveScale;
    slot.shadow.transform.position = new Vector3(
      slot.position.x,
      slot.groundY,
      slot.position.z
    );
    slot.shadow.transform.localScale = new Vector3(shadowScale, shadowScale * 0.28f, 1f);
    slot.shadow.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.2f, 0.07f, heightRatio));
  }

  static void HideAirborne(AirborneSlot slot) {
    if (slot == null) {
      return;
    }

    slot.active = false;
    slot.timeContext = null;
    if (slot.spray != null) {
      slot.spray.enabled = false;
    }
    if (slot.shadow != null) {
      slot.shadow.enabled = false;
    }
  }

  void SpawnGroundDecal(
    Vector3 position,
    float baseScale,
    int sortingLayerId,
    int sortingOrder,
    Transform timeContext,
    Sprite decalSprite
  ) {
    var slotIndex = nextDecalSlot;
    var slot = decalSlots[slotIndex];
    if (slot == null) {
      slot = new DecalSlot {
        renderer = CreateRenderer("Ground Decal " + slotIndex, decalSprite)
      };
      decalSlots[slotIndex] = slot;
    }
    nextDecalSlot = (nextDecalSlot + 1) % decalSlots.Length;

    var width = Mathf.Max(MinimumBloodScale, baseScale * Random.Range(0.85f, 1.2f));
    slot.timeContext = timeContext;
    slot.settledScale = ClampBloodScale(new Vector3(
      width,
      width * Random.Range(0.72f, 0.92f),
      1f
    ));
    slot.settleElapsed = 0f;
    slot.settling = true;

    slot.renderer.sortingLayerID = sortingLayerId;
    slot.renderer.sortingOrder = sortingOrder;
    slot.renderer.sprite = decalSprite;
    slot.renderer.color = new Color(1f, 1f, 1f, Random.Range(0.86f, 1f));
    slot.renderer.transform.SetPositionAndRotation(
      position,
      Quaternion.Euler(0f, 0f, Random.Range(0f, 360f))
    );
    slot.renderer.transform.localScale = ClampBloodScale(slot.settledScale * 0.08f);
    slot.renderer.enabled = true;
  }

  void UpdateSettlingDecals() {
    if (decalSlots == null) {
      return;
    }

    for (var i = 0; i < decalSlots.Length; i++) {
      var slot = decalSlots[i];
      if (slot == null || !slot.settling) {
        continue;
      }

      var deltaTime = TimeScale.GetDeltaTime(slot.timeContext);
      if (deltaTime <= 0f) {
        continue;
      }

      slot.settleElapsed += deltaTime;
      var progress = Mathf.Clamp01(slot.settleElapsed / DecalSettleDuration);
      var eased = 1f - Mathf.Pow(1f - progress, 3f);
      var overshoot = Mathf.Sin(progress * Mathf.PI) * 0.1f;
      slot.renderer.transform.localScale = ClampBloodScale(
        slot.settledScale * (eased + overshoot)
      );

      if (progress < 1f) {
        continue;
      }

      slot.renderer.transform.localScale = slot.settledScale;
      slot.timeContext = null;
      slot.settling = false;
    }
  }

  static Vector3 ClampBloodScale(Vector3 scale) {
    scale.x = Mathf.Max(scale.x, MinimumBloodScale);
    scale.y = Mathf.Max(scale.y, MinimumBloodScale);
    return scale;
  }

  Vector3 ResolveImpactOrigin(Transform actor) {
    var hurtBox = actor.GetComponentInChildren<HurtBox2D>(includeInactive: true);
    if (hurtBox != null) {
      var hurtCollider = hurtBox.GetComponent<Collider2D>();
      if (hurtCollider != null) {
        return hurtCollider.bounds.center;
      }
    }

    rendererScratch.Clear();
    actor.GetComponentsInChildren<SpriteRenderer>(false, rendererScratch);
    var foundBounds = false;
    var bounds = new Bounds(actor.position, Vector3.zero);
    for (var i = 0; i < rendererScratch.Count; i++) {
      var renderer = rendererScratch[i];
      if (renderer == null || !renderer.enabled || renderer.sprite == null) {
        continue;
      }

      if (!foundBounds) {
        bounds = renderer.bounds;
        foundBounds = true;
      }
      else {
        bounds.Encapsulate(renderer.bounds);
      }
    }

    return foundBounds ? bounds.center : actor.position;
  }

  static float ResolveSprayDirection(Transform actor, bool isFacingRight) {
    var hurtBox = actor.GetComponentInChildren<HurtBox2D>(includeInactive: true);
    if (hurtBox != null &&
        hurtBox.LastHitFrame == Time.frameCount &&
        hurtBox.LastHitBox != null) {
      var source = hurtBox.LastHitBox.ActorOwner != null
        ? hurtBox.LastHitBox.ActorOwner.position
        : hurtBox.LastHitBox.transform.position;
      var horizontalSeparation = actor.position.x - source.x;
      if (Mathf.Abs(horizontalSeparation) > 0.05f) {
        return Mathf.Sign(horizontalSeparation);
      }
    }

    // When Hurt is triggered without hit context, spray behind the actor.
    return isFacingRight ? -1f : 1f;
  }

  static Vector3 ResolveGroundPosition(Transform actor) {
    var shadowCaster = actor.GetComponent<ProjectedSpriteShadowCaster2D>();
    if (shadowCaster != null) {
      var ground = shadowCaster.GroundPosition;
      return new Vector3(ground.x, ground.y, actor.position.z);
    }

    var zPoint = actor.GetComponentInChildren<Zpoint>(includeInactive: true);
    return zPoint != null ? zPoint.transform.position : actor.position;
  }

  static void ResolveGroundSorting(
    Transform actor,
    Vector3 groundPosition,
    out int sortingLayerId,
    out int sortingOrder
  ) {
    var sortingGroup = actor.GetComponent<SortingGroup>();
    sortingLayerId = sortingGroup != null
      ? sortingGroup.sortingLayerID
      : SortingLayer.NameToID("GameFG");

    var mainCamera = Camera.main;
    sortingOrder = mainCamera != null
      ? -(int)mainCamera.WorldToScreenPoint(groundPosition).y
      : sortingGroup != null ? sortingGroup.sortingOrder : 0;
  }
}
