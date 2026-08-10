using System.Collections.Generic;
using EZhex1991.EZSoftBone;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tree-only foliage feedback. It is attached only to destructibles that have
/// the authored sibling holders named Leaves and Leafs.
/// </summary>
[DisallowMultipleComponent]
public sealed class TreeLeafHitReaction : MonoBehaviour {
  const string LeavesName = "Leaves";
  const string LeafsName = "Leafs";
  const int MinimumFallingLeavesPerHit = 2;
  const int MaximumFallingLeavesPerHit = 4;
  const int FallingLeafPoolSize = 12;
  const float FoliagePositionSpring = 55f;
  const float FoliagePositionDamping = 8f;
  const float FoliageRotationSpring = 45f;
  const float FoliageRotationDamping = 7f;

  sealed class LeafCluster {
    public Transform root;
    public EZSoftBone softBone;
    public Vector3 restLocalPosition;
    public Quaternion restLocalRotation;
    public Vector2 positionOffset;
    public Vector2 positionVelocity;
    public float angle;
    public float angularVelocity;
    public float positionSpring;
    public float positionDamping;
    public float rotationSpring;
    public float rotationDamping;
    public float positionLimit;
    public float rotationLimit;
    public float kickScale;
  }

  readonly List<LeafCluster> leafClusters = new(8);
  readonly HashSet<GameObject> canopySupportPieces = new();
  readonly HashSet<GameObject> launchedCanopyPieces = new();
  readonly Dictionary<GameObject, Pool> fallingLeafPools = new();
  readonly List<HurtBox2D> subscribedHurtBoxes = new(4);

  [Header("Falling Leaf Spawns")]
  [Tooltip("One of these prefabs is selected at random for each falling leaf.")]
  public List<GameObject> fallingLeafPrefabs = new();

  Transform leaves;
  Transform leafs;
  Transform piecesParent;
  Bounds leavesBounds;
  int lastReactionFrame = -1;
  ulong lastReactionSourceId;
  Vector3 leavesRestLocalPosition;
  Quaternion leavesRestLocalRotation;
  Vector2 foliageOffset;
  Vector2 foliageVelocity;
  float foliageAngle;
  float foliageAngularVelocity;
  bool warnedMissingFallingLeafPrefabs;
  bool canopyRemoved;
  Transform groundMarker;
  SpriteRenderer[] canopyRenderers;

  void Awake() {
    InitializeFromHierarchy();
  }

  void OnEnable() {
    InitializeFromHierarchy();
    SubscribeToHurtBoxes();
  }

  void OnDisable() {
    UnsubscribeFromHurtBoxes();
    RestoreLeavesTransform();
  }

  public static TreeLeafHitReaction TryAttach(Destructible destructible, Transform brokenPieces) {
    if (destructible == null || brokenPieces == null) {
      return null;
    }

    var leaves = FindDirectChild(destructible.transform, LeavesName);
    if (leaves == null) {
      return null;
    }
    var leafs = GetOrCreateLeafsRoot(destructible.transform);

    if (!destructible.TryGetComponent(out TreeLeafHitReaction reaction)) {
      reaction = destructible.gameObject.AddComponent<TreeLeafHitReaction>();
    }
    reaction.Initialize(leaves, leafs, brokenPieces);
    return reaction;
  }

  void Initialize(Transform leavesRoot, Transform leafsRoot, Transform brokenPiecesRoot) {
    if (leaves != null) {
      return;
    }

    leaves = leavesRoot;
    leafs = leafsRoot;
    piecesParent = brokenPiecesRoot;
    leavesRestLocalPosition = leaves.localPosition;
    leavesRestLocalRotation = leaves.localRotation;
    CacheLeafClusters();
    canopyRenderers = leaves.GetComponentsInChildren<SpriteRenderer>(true);

    var rawPieces = FindDirectChild(transform, "RawPieces");
    if (rawPieces != null) {
      groundMarker = FindDirectChild(rawPieces, "zp");
    }

    CacheCanopySupportPieces();
    PrewarmFallingLeafPools();
  }

  public void PlayHit(Vector3 hitSourcePosition, Object hitSource = null) {
    if (canopyRemoved || leaves == null || !leaves.gameObject.activeInHierarchy) {
      return;
    }

    var sourceId = hitSource != null ? ObjectEntityId.GetRawValue(hitSource) : 0UL;
    if (lastReactionFrame == Time.frameCount && lastReactionSourceId == sourceId) {
      return;
    }
    lastReactionFrame = Time.frameCount;
    lastReactionSourceId = sourceId;

    var awayFromHit = leaves.position - hitSourcePosition;
    awayFromHit.y = Mathf.Abs(awayFromHit.y) * 0.25f + 0.15f;
    if (awayFromHit.sqrMagnitude < 0.0001f) {
      awayFromHit = Vector3.up;
    }
    awayFromHit.Normalize();

    KickFoliageRoot(awayFromHit);

    for (var i = 0; i < leafClusters.Count; i++) {
      ApplyLeafHit(leafClusters[i], hitSourcePosition, awayFromHit);
    }

    var count = Random.Range(MinimumFallingLeavesPerHit, MaximumFallingLeavesPerHit + 1);
    if (!HasFallingLeafPrefab()) {
      if (!warnedMissingFallingLeafPrefabs) {
        warnedMissingFallingLeafPrefabs = true;
        Debug.LogWarning(
          "[TreeLeafHitReaction] This Tree instance has no falling-leaf prefab references. " +
          "Refresh the location Addressables content so it uses the current Tree.prefab.",
          this
        );
      }
      return;
    }

    for (var i = 0; i < count; i++) {
      SpawnFallingLeaf(awayFromHit);
    }
  }

  void LateUpdate() {
    if (leaves == null || canopyRemoved || !leaves.gameObject.activeInHierarchy) {
      return;
    }

    var deltaTime = Mathf.Min(Time.deltaTime, 0.05f);
    if (deltaTime <= 0f) {
      return;
    }

    foliageVelocity -= foliageOffset * (FoliagePositionSpring * deltaTime);
    foliageVelocity *= Mathf.Exp(-FoliagePositionDamping * deltaTime);
    foliageOffset += foliageVelocity * deltaTime;
    foliageOffset = Vector2.ClampMagnitude(foliageOffset, 0.22f);

    foliageAngularVelocity -= foliageAngle * (FoliageRotationSpring * deltaTime);
    foliageAngularVelocity *= Mathf.Exp(-FoliageRotationDamping * deltaTime);
    foliageAngle += foliageAngularVelocity * deltaTime;
    foliageAngle = Mathf.Clamp(foliageAngle, -10f, 10f);

    leaves.localPosition = leavesRestLocalPosition + new Vector3(foliageOffset.x, foliageOffset.y, 0f);
    leaves.localRotation = leavesRestLocalRotation * Quaternion.Euler(0f, 0f, foliageAngle);
    UpdateLeafClusters(deltaTime);
  }

  void ApplyLeafHit(LeafCluster leaf, Vector3 hitSourcePosition, Vector3 fallbackDirection) {
    if (leaf == null || leaf.root == null) {
      return;
    }

    var awayFromHit = leaf.root.position - hitSourcePosition;
    awayFromHit.y = Mathf.Abs(awayFromHit.y) * 0.35f + 0.15f;
    if (awayFromHit.sqrMagnitude < 0.0001f) {
      awayFromHit = fallbackDirection;
    }
    awayFromHit.Normalize();

    var impulseDirection = Quaternion.Euler(0f, 0f, Random.Range(-55f, 55f)) * awayFromHit;
    if (leaf.softBone != null && leaf.softBone.isActiveAndEnabled) {
      leaf.softBone.AddImpulse(impulseDirection * Random.Range(34f, 54f));
    }

    var localDirection = leaves.InverseTransformDirection(impulseDirection);
    localDirection.z = 0f;
    if (localDirection.sqrMagnitude < 0.0001f) {
      localDirection = Vector3.up;
    }
    localDirection.Normalize();

    var horizontalDirection = Mathf.Sign(localDirection.x);
    if (Mathf.Approximately(horizontalDirection, 0f)) {
      horizontalDirection = Random.value < 0.5f ? -1f : 1f;
    }

    leaf.positionVelocity += new Vector2(
      horizontalDirection * Random.Range(1.05f, 1.85f),
      Random.Range(0.4f, 0.95f)
    ) * leaf.kickScale;
    leaf.angularVelocity +=
      -horizontalDirection * Random.Range(45f, 82f) * leaf.kickScale +
      Random.Range(-18f, 18f);
  }

  void UpdateLeafClusters(float deltaTime) {
    for (var i = 0; i < leafClusters.Count; i++) {
      var leaf = leafClusters[i];
      if (leaf == null || leaf.root == null) {
        continue;
      }

      leaf.positionVelocity -= leaf.positionOffset * (leaf.positionSpring * deltaTime);
      leaf.positionVelocity *= Mathf.Exp(-leaf.positionDamping * deltaTime);
      leaf.positionOffset += leaf.positionVelocity * deltaTime;
      leaf.positionOffset = Vector2.ClampMagnitude(leaf.positionOffset, leaf.positionLimit);

      leaf.angularVelocity -= leaf.angle * (leaf.rotationSpring * deltaTime);
      leaf.angularVelocity *= Mathf.Exp(-leaf.rotationDamping * deltaTime);
      leaf.angle += leaf.angularVelocity * deltaTime;
      leaf.angle = Mathf.Clamp(leaf.angle, -leaf.rotationLimit, leaf.rotationLimit);

      leaf.root.localPosition = leaf.restLocalPosition +
        new Vector3(leaf.positionOffset.x, leaf.positionOffset.y, 0f);
      leaf.root.localRotation = leaf.restLocalRotation * Quaternion.Euler(0f, 0f, leaf.angle);
    }
  }

  void KickFoliageRoot(Vector3 awayFromHit) {
    var localDirection = transform.InverseTransformDirection(awayFromHit);
    var horizontalDirection = Mathf.Sign(localDirection.x);
    if (Mathf.Approximately(horizontalDirection, 0f)) {
      horizontalDirection = Random.value < 0.5f ? -1f : 1f;
    }

    foliageVelocity += new Vector2(horizontalDirection * Random.Range(1.8f, 2.5f), Random.Range(1.25f, 1.75f));
    foliageAngularVelocity += -horizontalDirection * Random.Range(65f, 90f);
  }

  void RestoreLeavesTransform() {
    if (leaves == null) {
      return;
    }

    leaves.localPosition = leavesRestLocalPosition;
    leaves.localRotation = leavesRestLocalRotation;
    foliageOffset = Vector2.zero;
    foliageVelocity = Vector2.zero;
    foliageAngle = 0f;
    foliageAngularVelocity = 0f;

    for (var i = 0; i < leafClusters.Count; i++) {
      var leaf = leafClusters[i];
      if (leaf == null || leaf.root == null) {
        continue;
      }

      leaf.root.localPosition = leaf.restLocalPosition;
      leaf.root.localRotation = leaf.restLocalRotation;
      leaf.positionOffset = Vector2.zero;
      leaf.positionVelocity = Vector2.zero;
      leaf.angle = 0f;
      leaf.angularVelocity = 0f;
    }
  }

  void CacheLeafClusters() {
    leafClusters.Clear();
    for (var i = 0; i < leaves.childCount; i++) {
      var child = leaves.GetChild(i);
      if (child == null) {
        continue;
      }

      var renderer = child.GetComponentInChildren<SpriteRenderer>(true);
      var softBone = child.GetComponentInChildren<EZSoftBone>(true);
      if (renderer == null && softBone == null) {
        continue;
      }

      leafClusters.Add(new LeafCluster {
        root = child,
        softBone = softBone,
        restLocalPosition = child.localPosition,
        restLocalRotation = child.localRotation,
        positionSpring = Random.Range(28f, 40f),
        positionDamping = Random.Range(5f, 7.5f),
        rotationSpring = Random.Range(24f, 36f),
        rotationDamping = Random.Range(4.5f, 6.5f),
        positionLimit = Random.Range(0.11f, 0.18f),
        rotationLimit = Random.Range(7f, 13f),
        kickScale = Random.Range(0.82f, 1.18f)
      });
    }
  }

  void InitializeFromHierarchy() {
    if (leaves != null) {
      return;
    }

    var destructible = GetComponent<Destructible>();
    var brokenPieces = destructible != null ? destructible.piecesParent : null;
    if (brokenPieces == null) {
      brokenPieces = FindDirectChild(transform, "BrokenPieces");
    }

    var leavesRoot = FindDirectChild(transform, LeavesName);
    var leafsRoot = leavesRoot != null ? GetOrCreateLeafsRoot(transform) : null;
    if (leavesRoot != null && leafsRoot != null && brokenPieces != null) {
      Initialize(leavesRoot, leafsRoot, brokenPieces);
    }
  }

  void SubscribeToHurtBoxes() {
    if (subscribedHurtBoxes.Count > 0) {
      return;
    }

    var hurtBoxes = GetComponentsInChildren<HurtBox2D>(true);
    for (var i = 0; i < hurtBoxes.Length; i++) {
      var hurtBox = hurtBoxes[i];
      if (hurtBox == null) continue;
      hurtBox.OnHit.AddListener(OnRegisteredHurtBoxHit);
      subscribedHurtBoxes.Add(hurtBox);
    }
  }

  void UnsubscribeFromHurtBoxes() {
    for (var i = 0; i < subscribedHurtBoxes.Count; i++) {
      var hurtBox = subscribedHurtBoxes[i];
      if (hurtBox != null) {
        hurtBox.OnHit.RemoveListener(OnRegisteredHurtBoxHit);
      }
    }
    subscribedHurtBoxes.Clear();
  }

  void OnRegisteredHurtBoxHit(HitBox2D hitBox) {
    if (hitBox != null) {
      InitializeFromHierarchy();
      PlayHit(hitBox.transform.position, hitBox);
    }
  }

  public void OnPieceLaunched(GameObject piece) {
    if (canopyRemoved || piece == null || !canopySupportPieces.Contains(piece)) {
      return;
    }

    launchedCanopyPieces.Add(piece);
    if (launchedCanopyPieces.Count < canopySupportPieces.Count) {
      return;
    }

    canopyRemoved = true;
    if (leaves != null) {
      leaves.gameObject.SetActive(false);
    }
  }

  void CacheCanopySupportPieces() {
    if (leaves == null || piecesParent == null) {
      return;
    }

    var renderers = canopyRenderers;
    if (renderers == null || renderers.Length == 0) {
      return;
    }

    leavesBounds = renderers[0].bounds;
    for (var i = 1; i < renderers.Length; i++) {
      leavesBounds.Encapsulate(renderers[i].bounds);
    }

    var minimumSupportY = leavesBounds.min.y;
    for (var i = 0; i < piecesParent.childCount; i++) {
      var piece = piecesParent.GetChild(i).gameObject;
      var pieceRenderer = piece.GetComponentInChildren<SpriteRenderer>(true);
      if (pieceRenderer != null && pieceRenderer.bounds.max.y >= minimumSupportY) {
        canopySupportPieces.Add(piece);
      }
    }
  }

  void SpawnFallingLeaf(Vector3 awayFromHit) {
    var prefab = ChooseFallingLeafPrefab();
    if (prefab == null || leafs == null) {
      return;
    }

    var bounds = new Bounds(leaves.position, Vector3.zero);
    if (canopyRenderers != null && canopyRenderers.Length > 0) {
      bounds = canopyRenderers[0].bounds;
      for (var i = 1; i < canopyRenderers.Length; i++) {
        if (canopyRenderers[i] != null) {
          bounds.Encapsulate(canopyRenderers[i].bounds);
        }
      }
    }

    var position = new Vector3(
      Random.Range(bounds.min.x, bounds.max.x),
      Random.Range(bounds.center.y, bounds.max.y),
      0f
    );
    var pool = GetFallingLeafPool(prefab);
    var leaf = pool?.Spawn(position, Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
    if (leaf == null) {
      return;
    }
    leaf.name = "Falling Leaf";
    if (!leaf.TryGetComponent(out TreeFallingLeaf falling)) {
      falling = leaf.AddComponent<TreeFallingLeaf>();
    }

    var groundY = groundMarker != null 
        ? groundMarker.position.y 
        : transform.position.y - Random.Range(0.05f, 0.2f);

    falling.Initialize(
      awayFromHit,
      groundY,
      pool
    );
    pool.DespawnAfter(leaf, TreeFallingLeaf.MaximumLifetimeSeconds);
  }

  void PrewarmFallingLeafPools() {
    if (fallingLeafPrefabs == null) {
      return;
    }

    for (var i = 0; i < fallingLeafPrefabs.Count; i++) {
      var prefab = fallingLeafPrefabs[i];
      if (prefab != null) {
        GetFallingLeafPool(prefab);
      }
    }
  }

  GameObject ChooseFallingLeafPrefab() {
    if (fallingLeafPrefabs == null || fallingLeafPrefabs.Count == 0) {
      return null;
    }

    var firstValidIndex = -1;
    for (var i = 0; i < fallingLeafPrefabs.Count; i++) {
      if (fallingLeafPrefabs[i] != null) {
        firstValidIndex = i;
        break;
      }
    }
    if (firstValidIndex < 0) {
      return null;
    }

    for (var i = 0; i < fallingLeafPrefabs.Count; i++) {
      var prefab = fallingLeafPrefabs[Random.Range(0, fallingLeafPrefabs.Count)];
      if (prefab != null) {
        return prefab;
      }
    }
    return fallingLeafPrefabs[firstValidIndex];
  }

  bool HasFallingLeafPrefab() {
    if (fallingLeafPrefabs == null) {
      return false;
    }

    for (var i = 0; i < fallingLeafPrefabs.Count; i++) {
      if (fallingLeafPrefabs[i] != null) {
        return true;
      }
    }
    return false;
  }

  Pool GetFallingLeafPool(GameObject prefab) {
    if (prefab == null) {
      return null;
    }
    if (fallingLeafPools.TryGetValue(prefab, out var pool)) {
      return pool;
    }

    pool = new Pool();
    pool.Initialize(
      prefab,
      leafs,
      FallingLeafPoolSize,
      onInstanceCreated: instance => {
        if (!instance.TryGetComponent<TreeFallingLeaf>(out _)) {
          instance.AddComponent<TreeFallingLeaf>();
        }
      }
    );
    fallingLeafPools.Add(prefab, pool);
    return pool;
  }

  static Transform FindDirectChild(Transform parent, string childName) {
    for (var i = 0; i < parent.childCount; i++) {
      var child = parent.GetChild(i);
      if (child.name == childName) {
        return child;
      }
    }
    return null;
  }

  static Transform GetOrCreateLeafsRoot(Transform parent) {
    var existing = FindDirectChild(parent, LeafsName);
    if (existing != null) {
      return existing;
    }

    var container = new GameObject(LeafsName);
    container.layer = parent.gameObject.layer;
    container.transform.SetParent(parent, false);
    return container.transform;
  }
}

public sealed class TreeFallingLeaf : MonoBehaviour {
  public const float MaximumLifetimeSeconds = 8f;
  const float Gravity = 6.5f;
  const float GroundFadeSeconds = 0.65f;

  SpriteRenderer[] renderers;
  Pool ownerPool;
  Vector3 velocity;
  float groundY;
  float age;
  float landedAge;
  float angularSpeed;
  bool landed;
  float flutterPhase;
  float flutterSpeed;
  float swayPhase;
  float swaySpeed;
  float swayAmplitude;

  public void Initialize(
    Vector3 awayFromHit,
    float targetGroundY,
    Pool pool
  ) {
    renderers = GetComponentsInChildren<SpriteRenderer>(true);
    ownerPool = pool;
    groundY = targetGroundY;
    velocity = awayFromHit * Random.Range(0.8f, 1.35f);
    velocity.y += Random.Range(0.35f, 0.8f);
    velocity.z = 0f;
    angularSpeed = Random.Range(-120f, 120f);
    age = 0f;
    landedAge = 0f;
    landed = false;
    flutterPhase = Random.Range(0f, Mathf.PI * 2f);
    flutterSpeed = Random.Range(4f, 8f);
    swayPhase = Random.Range(0f, Mathf.PI * 2f);
    swaySpeed = Random.Range(1.5f, 3.5f);
    swayAmplitude = Random.Range(2.5f, 4.5f);
    for (var i = 0; i < renderers.Length; i++) {
      var renderer = renderers[i];
      if (renderer == null) continue;
      var color = renderer.color;
      color.a = 1f;
      renderer.color = color;
    }
  }

  void Update() {
    if (renderers == null) return;

    var deltaTime = Time.deltaTime;
    age += deltaTime;

    if (!landed) {
      velocity.x = Mathf.Lerp(velocity.x, 0f, deltaTime * 1.5f);
      var swayForce = Mathf.Sin(age * swaySpeed + swayPhase) * swayAmplitude;
      velocity.x += swayForce * deltaTime;

      var effectiveGravity = Gravity;
      var flutter = Mathf.Sin(age * flutterSpeed + flutterPhase);
      if (flutter > 0f) {
        effectiveGravity = Mathf.Lerp(Gravity, Gravity * 0.05f, flutter);
      }
      velocity.y -= effectiveGravity * deltaTime;
      velocity.y = Mathf.Max(velocity.y, -3.5f);

      transform.position += velocity * deltaTime;
      transform.Rotate(0f, 0f, (angularSpeed + velocity.x * -35f) * deltaTime);

      if (transform.position.y <= groundY) {
        var position = transform.position;
        position.y = groundY;
        transform.position = position;
        landed = true;
        landedAge = 0f;
      }
    } else {
      landedAge += deltaTime;
    }

    var visibility = landed
      ? 1f - Mathf.Clamp01(landedAge / GroundFadeSeconds)
      : 1f;
    for (var i = 0; i < renderers.Length; i++) {
      var renderer = renderers[i];
      if (renderer == null) continue;
      var color = renderer.color;
      color.a = visibility;
      renderer.color = color;
    }

    if ((landed && landedAge >= GroundFadeSeconds) || age >= MaximumLifetimeSeconds) {
      ownerPool?.Despawn(gameObject);
    }
  }
}
