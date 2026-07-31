using System;
using System.Collections.Generic;
using CustomInspector;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DestructionManager : MonoBehaviour {
  readonly struct EmbeddedPiecesPoolKey : IEquatable<EmbeddedPiecesPoolKey> {
    public readonly SceneHandle sceneHandle;
    readonly string enemyType;
    readonly int pieceCount;

    public EmbeddedPiecesPoolKey(
      SceneHandle sceneHandle,
      string enemyType,
      int pieceCount
    ) {
      this.sceneHandle = sceneHandle;
      this.enemyType = enemyType;
      this.pieceCount = pieceCount;
    }

    public bool Equals(EmbeddedPiecesPoolKey other) {
      return sceneHandle == other.sceneHandle &&
             pieceCount == other.pieceCount &&
             string.Equals(enemyType, other.enemyType, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object obj) {
      return obj is EmbeddedPiecesPoolKey other && Equals(other);
    }

    public override int GetHashCode() {
      unchecked {
        var hash = sceneHandle.GetHashCode();
        hash = (hash * 397) ^ pieceCount;
        hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(enemyType ?? "");
        return hash;
      }
    }
  }

  const int PiecesPoolSize = 5;
  const float PiecesContainerLifetimeSeconds = 10f;

  static readonly Dictionary<EmbeddedPiecesPoolKey, GameObject> embeddedPoolTemplates = new();
  static readonly List<EmbeddedPiecesPoolKey> embeddedPoolKeyScratch = new();

  [Button(nameof(LaunchRandom), label = "Play", size = Size.small)] public bool what;
  [Tooltip("Optional standalone PIECES prefab. When omitted, the embedded PIECES hierarchy is used as the pooled template.")]
  public GameObject piecesPrefab;
  private Transform embeddedPiecesRoot;
  private Transform piecesRoot;
  [Tooltip("Ground-plane impulse minimum: X scatters sideways and Y adds slight visual depth.")]
  public Vector2 planarForceMin = new(-6f, -1.25f);
  [Tooltip("Ground-plane impulse maximum: X scatters sideways and Y adds slight visual depth.")]
  public Vector2 planarForceMax = new(6f, 1.25f);
  public float torqueMin = -8f;
  public float torqueMax = 8f;
  public List<Piece> pieces = new();

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetEmbeddedPoolTemplates() {
    SceneManager.sceneUnloaded -= OnSceneUnloaded;
    embeddedPoolTemplates.Clear();
    embeddedPoolKeyScratch.Clear();
    SceneManager.sceneUnloaded += OnSceneUnloaded;
  }

  static void OnSceneUnloaded(Scene scene) {
    embeddedPoolKeyScratch.Clear();
    foreach (var pair in embeddedPoolTemplates) {
      if (pair.Key.sceneHandle == scene.handle) {
        embeddedPoolKeyScratch.Add(pair.Key);
      }
    }

    for (var i = 0; i < embeddedPoolKeyScratch.Count; i++) {
      embeddedPoolTemplates.Remove(embeddedPoolKeyScratch[i]);
    }
    embeddedPoolKeyScratch.Clear();
  }

  void Awake() {
    embeddedPiecesRoot = transform.FindDirectChild("PIECES");
    piecesRoot = embeddedPiecesRoot;
    if (piecesRoot != null) {
      CollectPiecesFromChildren();
    }
  }

  void Start() {
    // Pre-warm during location setup. Embedded PIECES are also treated as a
    // shared template so the launched debris survives pooled enemy despawn.
    ResolvePiecesPool();
  }

  public void CollectPiecesFromChildren() {
    pieces.Clear();
    if (piecesRoot == null) {
      return;
    }

    var found = piecesRoot.GetComponentsInChildren<Piece>(true);
    for (var i = 0; i < found.Length; i++) {
      pieces.Add(found[i]);
    }
  }

  public void LaunchRandom() {
    var pool = ResolvePiecesPool();
    if (pool == null) {
      return;
    }

    var useEmbeddedRoot = piecesPrefab == null && embeddedPiecesRoot != null;
    var spawnPosition = useEmbeddedRoot ? embeddedPiecesRoot.position : transform.position;
    var spawnRotation = useEmbeddedRoot ? embeddedPiecesRoot.rotation : transform.rotation;
    var spawnScale = useEmbeddedRoot
      ? embeddedPiecesRoot.lossyScale
      : Vector3.Scale(piecesPrefab.transform.localScale, transform.lossyScale);
    var instance = pool.Spawn(spawnPosition, spawnRotation);
    if (instance == null) {
      return;
    }
    instance.transform.localScale = spawnScale;

    var launcher = instance.GetComponent<PooledPieceScatter>();
    if (launcher == null) {
      launcher = instance.AddComponent<PooledPieceScatter>();
    }

    if (!launcher.Launch(
          planarForceMin,
          planarForceMax,
          torqueMin,
          torqueMax
        )) {
      pool.Despawn(instance);
      return;
    }

    pool.DespawnAfter(instance, PiecesContainerLifetimeSeconds);
  }

  Pool ResolvePiecesPool() {
    var poolPrefab = ResolvePiecesPoolPrefab();
    return poolPrefab != null
      ? Pool.GetShared(poolPrefab, null, PiecesPoolSize)
      : null;
  }

  GameObject ResolvePiecesPoolPrefab() {
    if (piecesPrefab != null) {
      return piecesPrefab;
    }
    if (embeddedPiecesRoot == null) {
      return null;
    }

    var enemyInfo = GetComponent<EnemyInfo>();
    var enemyType = enemyInfo != null && !string.IsNullOrWhiteSpace(enemyInfo.enemyType)
      ? enemyInfo.enemyType.Trim()
      : gameObject.name;
    var key = new EmbeddedPiecesPoolKey(
      gameObject.scene.handle,
      enemyType,
      embeddedPiecesRoot.childCount
    );
    if (embeddedPoolTemplates.TryGetValue(key, out var template) && template != null) {
      return template;
    }

    template = Instantiate(embeddedPiecesRoot.gameObject);
    template.name = enemyType + " PIECES Pool Template";
    template.SetActive(false);
    embeddedPoolTemplates[key] = template;
    return template;
  }
}
