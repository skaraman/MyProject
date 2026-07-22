using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared, pooled fighting-game style emphasis lines for successful combat hits.
/// The burst is generated from a one-pixel runtime sprite, so it needs no authored VFX asset.
/// </summary>
[DisallowMultipleComponent]
public sealed class HitEmphasisBurst : MonoBehaviour {
  const int BurstCapacity = 24;
  const int LinesPerBurst = 9;
  const int SortingOrderOffset = 100;
  const float BurstDurationSeconds = 0.22f;

  static readonly Color EsperHitAccent = new(1f, 0.18f, 0.12f, 1f);
  static readonly Color EnemyHitAccent = new(1f, 0.74f, 0.18f, 1f);

  sealed class BurstSlot {
    public SpriteRenderer[] renderers;
    public Vector2[] directions;
    public float[] startDistances;
    public float[] travelDistances;
    public float[] lengths;
    public float[] widths;
    public Color[] colors;
    public Transform timeContext;
    public Vector3 origin;
    public float elapsed;
    public bool active;
  }

  static HitEmphasisBurst instance;
  static uint burstSequence;

  readonly List<SpriteRenderer> rendererScratch = new(64);
  BurstSlot[] slots;
  Sprite whiteSprite;
  int nextSlot;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetRuntimeState() {
    instance = null;
    burstSequence = 0;
  }

  public static void Play(HurtBox2D hurtBox, HitBox2D hitBox) {
    if (!Application.isPlaying ||
        hurtBox == null ||
        hitBox == null ||
        !hurtBox.gameObject.activeInHierarchy) {
      return;
    }

    var manager = ResolveOrCreate(hurtBox.transform);
    manager?.Spawn(hurtBox, hitBox);
  }

  static HitEmphasisBurst ResolveOrCreate(Transform timeContext) {
    if (instance != null) {
      return instance;
    }

    var runner = new GameObject("__Hit Emphasis Burst VFX");
    var targetScene = timeContext.gameObject.scene;
    if (targetScene.IsValid() && targetScene.isLoaded) {
      SceneManager.MoveGameObjectToScene(runner, targetScene);
    }

    instance = runner.AddComponent<HitEmphasisBurst>();
    return instance;
  }

  void Awake() {
    if (instance != null && instance != this) {
      Destroy(gameObject);
      return;
    }

    instance = this;
    slots = new BurstSlot[BurstCapacity];
    whiteSprite = CreateWhiteSprite();
  }

  void OnDestroy() {
    if (whiteSprite != null) {
      Destroy(whiteSprite);
      whiteSprite = null;
    }
    if (instance == this) {
      instance = null;
    }
  }

  static Sprite CreateWhiteSprite() {
    var texture = Texture2D.whiteTexture;
    var sprite = Sprite.Create(
      texture,
      new Rect(0f, 0f, texture.width, texture.height),
      new Vector2(0.5f, 0.5f),
      texture.width,
      0u,
      SpriteMeshType.FullRect
    );
    sprite.name = "Runtime Hit Emphasis Line";
    sprite.hideFlags = HideFlags.HideAndDontSave;
    return sprite;
  }

  void Spawn(HurtBox2D hurtBox, HitBox2D hitBox) {
    if (!enabled || slots == null || whiteSprite == null) {
      return;
    }

    ResolveImpact(hurtBox, hitBox, out var origin, out var impactDirection, out var radius);
    ResolveSorting(hurtBox.transform, origin, out var sortingLayerId, out var sortingOrder);
    var actor = ResolveActor(hurtBox.transform);
    var renderingLayer = actor != null ? actor.gameObject.layer : hurtBox.gameObject.layer;

    var slot = AcquireSlot();
    Hide(slot);

    slot.timeContext = hurtBox.transform;
    slot.origin = origin;
    slot.elapsed = 0f;
    slot.active = true;

    var baseAngle = Mathf.Atan2(impactDirection.y, impactDirection.x) * Mathf.Rad2Deg;
    var accent = hitBox.IsEnemyOwned ? EsperHitAccent : EnemyHitAccent;
    var randomState = CreateRandomState(hurtBox, hitBox);

    for (var i = 0; i < LinesPerBurst; i++) {
      var angle = baseAngle + (360f * i / LinesPerBurst) + RandomRange(ref randomState, -11f, 11f);
      var direction = new Vector2(
        Mathf.Cos(angle * Mathf.Deg2Rad),
        Mathf.Sin(angle * Mathf.Deg2Rad)
      );

      slot.directions[i] = direction;
      slot.startDistances[i] = radius * RandomRange(ref randomState, 0.08f, 0.24f);
      slot.travelDistances[i] = radius * RandomRange(ref randomState, 0.72f, 1.18f);
      slot.lengths[i] = radius * RandomRange(ref randomState, 0.62f, 1.22f);
      slot.widths[i] = Mathf.Clamp(
        radius * RandomRange(ref randomState, 0.055f, 0.1f),
        0.018f,
        0.075f
      );
      slot.colors[i] = i % 3 == 0 ? accent : Color.white;

      var line = slot.renderers[i];
      line.gameObject.layer = renderingLayer;
      line.sortingLayerID = sortingLayerId;
      line.sortingOrder = sortingOrder + (i % 2);
      line.enabled = true;
    }

    UpdateSlot(slot, 0f);
  }

  BurstSlot AcquireSlot() {
    for (var offset = 0; offset < slots.Length; offset++) {
      var index = (nextSlot + offset) % slots.Length;
      if (slots[index] == null) {
        slots[index] = CreateSlot(index);
        nextSlot = (index + 1) % slots.Length;
        return slots[index];
      }
      if (!slots[index].active) {
        nextSlot = (index + 1) % slots.Length;
        return slots[index];
      }
    }

    var recycled = slots[nextSlot];
    nextSlot = (nextSlot + 1) % slots.Length;
    return recycled;
  }

  BurstSlot CreateSlot(int slotIndex) {
    var slot = new BurstSlot {
      renderers = new SpriteRenderer[LinesPerBurst],
      directions = new Vector2[LinesPerBurst],
      startDistances = new float[LinesPerBurst],
      travelDistances = new float[LinesPerBurst],
      lengths = new float[LinesPerBurst],
      widths = new float[LinesPerBurst],
      colors = new Color[LinesPerBurst]
    };

    for (var i = 0; i < LinesPerBurst; i++) {
      var lineObject = new GameObject("Burst " + slotIndex + " Line " + i);
      lineObject.transform.SetParent(transform, false);
      var renderer = lineObject.AddComponent<SpriteRenderer>();
      renderer.sprite = whiteSprite;
      renderer.enabled = false;
      slot.renderers[i] = renderer;
    }

    return slot;
  }

  void Update() {
    if (slots == null) {
      return;
    }

    for (var i = 0; i < slots.Length; i++) {
      var slot = slots[i];
      if (slot == null || !slot.active) {
        continue;
      }

      var deltaTime = TimeScale.GetDeltaTime(slot.timeContext);
      if (deltaTime <= 0f) {
        continue;
      }

      slot.elapsed += deltaTime;
      var progress = Mathf.Clamp01(slot.elapsed / BurstDurationSeconds);
      UpdateSlot(slot, progress);
      if (progress >= 1f) {
        Hide(slot);
      }
    }
  }

  static void UpdateSlot(BurstSlot slot, float progress) {
    var outwardEase = 1f - Mathf.Pow(1f - progress, 3f);
    var popProgress = Mathf.Clamp01(progress / 0.18f);
    var popEase = 1f - Mathf.Pow(1f - popProgress, 3f);
    var fadeProgress = Mathf.InverseLerp(0.28f, 1f, progress);
    var fade = 1f - fadeProgress * fadeProgress * (3f - 2f * fadeProgress);
    var lengthScale = popEase * Mathf.Lerp(1f, 0.42f, progress);
    var widthScale = Mathf.Lerp(1f, 0.34f, progress);

    for (var i = 0; i < slot.renderers.Length; i++) {
      var line = slot.renderers[i];
      var direction = slot.directions[i];
      var distance = slot.startDistances[i] + slot.travelDistances[i] * outwardEase;
      var position = slot.origin + (Vector3)(direction * distance);
      var angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

      line.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
      line.transform.localScale = new Vector3(
        Mathf.Max(0.0001f, slot.lengths[i] * lengthScale),
        Mathf.Max(0.0001f, slot.widths[i] * widthScale),
        1f
      );

      var color = slot.colors[i];
      color.a = fade;
      line.color = color;
    }
  }

  static void Hide(BurstSlot slot) {
    if (slot == null) {
      return;
    }

    slot.active = false;
    slot.timeContext = null;
    if (slot.renderers == null) {
      return;
    }

    for (var i = 0; i < slot.renderers.Length; i++) {
      if (slot.renderers[i] != null) {
        slot.renderers[i].enabled = false;
      }
    }
  }

  static void ResolveImpact(
    HurtBox2D hurtBox,
    HitBox2D hitBox,
    out Vector3 origin,
    out Vector2 direction,
    out float radius
  ) {
    var hurtCollider = hurtBox.GetComponent<Collider2D>();
    var hitCollider = hitBox.GetComponent<Collider2D>();
    var hurtCenter = hurtCollider != null
      ? hurtCollider.bounds.center
      : hurtBox.transform.position;
    var hitCenter = hitCollider != null
      ? hitCollider.bounds.center
      : hitBox.transform.position;
    var sourcePosition = hitBox.ActorOwner != null
      ? hitBox.ActorOwner.position
      : hitCenter;

    direction = (Vector2)(hurtCenter - sourcePosition);
    if (direction.sqrMagnitude < 0.0001f) {
      direction = (Vector2)(hurtCenter - hitCenter);
    }
    if (direction.sqrMagnitude < 0.0001f) {
      var horizontalSign = hitBox.transform.lossyScale.x < 0f ? -1f : 1f;
      direction = hitBox.transform.right * horizontalSign;
    }
    direction.Normalize();

    origin = hurtCollider != null
      ? hurtCollider.ClosestPoint(hitCenter)
      : hurtCenter;
    origin.z = hurtCenter.z;

    var targetSize = hurtCollider != null
      ? Mathf.Max(hurtCollider.bounds.size.x, hurtCollider.bounds.size.y)
      : 1f;
    radius = Mathf.Clamp(targetSize * 0.34f, 0.22f, 0.82f);
  }

  void ResolveSorting(
    Transform hurtTransform,
    Vector3 origin,
    out int sortingLayerId,
    out int sortingOrder
  ) {
    var actor = ResolveActor(hurtTransform);
    var sortingGroup = actor != null ? actor.GetComponent<SortingGroup>() : null;
    if (sortingGroup != null) {
      sortingLayerId = sortingGroup.sortingLayerID;
      sortingOrder = sortingGroup.sortingOrder + SortingOrderOffset;
      return;
    }

    rendererScratch.Clear();
    actor?.GetComponentsInChildren(false, rendererScratch);
    SpriteRenderer foremostRenderer = null;
    for (var i = 0; i < rendererScratch.Count; i++) {
      var candidate = rendererScratch[i];
      if (candidate == null || !candidate.enabled || candidate.sprite == null) {
        continue;
      }
      if (foremostRenderer == null || candidate.sortingOrder > foremostRenderer.sortingOrder) {
        foremostRenderer = candidate;
      }
    }

    if (foremostRenderer != null) {
      sortingLayerId = foremostRenderer.sortingLayerID;
      sortingOrder = foremostRenderer.sortingOrder + SortingOrderOffset;
      return;
    }

    sortingLayerId = SortingLayer.NameToID("GameFG");
    var mainCamera = Camera.main;
    sortingOrder = mainCamera != null
      ? -(int)mainCamera.WorldToScreenPoint(origin).y + SortingOrderOffset
      : SortingOrderOffset;
  }

  static Transform ResolveActor(Transform source) {
    var enemyController = source.GetComponentInParent<EnemyController>();
    if (enemyController != null) {
      return enemyController.transform;
    }

    var enemyInfo = source.GetComponentInParent<EnemyInfo>();
    if (enemyInfo != null) {
      return enemyInfo.transform;
    }

    var gearController = source.GetComponentInParent<GearController>();
    if (gearController != null) {
      return gearController.transform;
    }

    return source;
  }

  static uint CreateRandomState(HurtBox2D hurtBox, HitBox2D hitBox) {
    burstSequence++;
    var state = burstSequence * 747796405u + (uint)Time.frameCount * 2891336453u;
    state ^= (uint)hurtBox.GetEntityId().GetHashCode();
    state ^= (uint)hitBox.GetEntityId().GetHashCode() * 277803737u;
    return state != 0u ? state : 1u;
  }

  static float RandomRange(ref uint state, float minimum, float maximum) {
    state ^= state << 13;
    state ^= state >> 17;
    state ^= state << 5;
    var normalized = (state & 0x00FFFFFFu) / 16777215f;
    return Mathf.Lerp(minimum, maximum, normalized);
  }
}
