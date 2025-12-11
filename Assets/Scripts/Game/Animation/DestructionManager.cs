using System.Collections;
using System.Collections.Generic;
using CustomInspector;
using UnityEngine;

public class DestructionManager : MonoBehaviour
{
  [Button(nameof(ForceAnimation), label = "Play", size = Size.small)] public bool forceLoop;

  public GameObject piecesParent;
  public float impulseMin = 2f;
  public float impulseMax = 4f;
  public float selfIgnoreDuration = 0.25f;
  public float postBounceDisableDelay = 0.75f;
  public string wallTag = "Wall";
  public float bounceImpulseMin = 0.5f;
  public float bounceImpulseMax = 1.2f;
  public bool logLaunches = true;
  [Header("Launch Tuning")]
  [Range(0f, 1f)] public float launchHorizontalMin = 0.35f;
  [Range(0f, 1f)] public float launchUpwardMin = 0.2f;
  [Range(0f, 1f)] public float launchUpwardMax = 0.55f;
  [Range(0f, 2f)] public float launchForceScale = 0.8f;

  readonly List<Piece> pieces = new();
  readonly List<Piece> shuffleBuffer = new();
  readonly HashSet<Collider2D> pieceColliders = new();
  WaitForSeconds cachedSelfIgnoreWait;
  float cachedSelfIgnoreDuration = -1f;

  void Awake()
  {
    BuildCache();
  }

  void OnValidate()
  {
    BuildCache();
  }

  void BuildCache()
  {
    pieces.Clear();
    pieceColliders.Clear();

    if (!piecesParent)
    {
      return;
    }

    var foundPieces = piecesParent.GetComponentsInChildren<Piece>(true);
    for (var i = 0; i < foundPieces.Length; i++)
    {
      var piece = foundPieces[i];
      if (!piece) continue;
      if (!piece.InitializeForDestruction(this))
      {
        continue;
      }
      pieces.Add(piece);
      if (piece.Collider != null)
      {
        pieceColliders.Add(piece.Collider);
      }
    }
  }

  void ForceAnimation()
  {
    FireRandomPieces(0);
  }

  public void FireRandomPieces(int count)
  {
    if (pieces.Count == 0)
    {
      return;
    }
    shuffleBuffer.Clear();
    shuffleBuffer.AddRange(pieces);
    if (count <= 0)
    {
      count = UnityEngine.Random.Range(1, shuffleBuffer.Count + 1);
    }
    if (count > shuffleBuffer.Count)
    {
      count = shuffleBuffer.Count;
    }

    ShufflePieces(shuffleBuffer);

    if (logLaunches)
    {
      Debug.Log($"[DestructionManager] Firing {count} pieces");
    }
    for (var i = 0; i < count; i++)
    {
      ActivateAndImpulse(shuffleBuffer[i]);
    }
  }

  static void ShufflePieces(List<Piece> data)
  {
    for (var i = 0; i < data.Count; i++)
    {
      var j = UnityEngine.Random.Range(i, data.Count);
      (data[i], data[j]) = (data[j], data[i]);
    }
  }

  void ActivateAndImpulse(Piece piece)
  {
    if (!IsPieceReady(piece)) return;
    piece.StopDisableRoutine();
    piece.ResetTransformToOrigin();
    piece.ResetForLaunch(0f, 0f);
    ApplyBurstImpulse(piece);
    SetupBounceData(piece);
  }

  void ApplyBurstImpulse(Piece piece)
  {
    var dir = GetLaunchDirection();
    var mag = UnityEngine.Random.Range(impulseMin, impulseMax) * launchForceScale;
    var body = piece?.Body;
    if (body == null) return;
    body.AddForce(dir * mag, ForceMode2D.Impulse);
    body.AddTorque(UnityEngine.Random.Range(-mag, mag), ForceMode2D.Impulse);
  }

  Vector2 GetLaunchDirection()
  {
    var horiz = UnityEngine.Random.Range(launchHorizontalMin, 1f);
    horiz *= UnityEngine.Random.value < 0.5f ? -1f : 1f;
    var upwardMin = Mathf.Min(launchUpwardMin, launchUpwardMax);
    var upwardMax = Mathf.Max(launchUpwardMin, launchUpwardMax);
    var vert = UnityEngine.Random.Range(upwardMin, upwardMax);
    var dir = new Vector2(horiz, vert);
    if (dir.sqrMagnitude < 0.001f)
    {
      return Vector2.up;
    }
    return dir.normalized;
  }

  void SetupBounceData(Piece piece)
  {
    piece.SetupBounceDetection();
  }

  void FixedUpdate()
  {
    MonitorBounces();
  }

  void MonitorBounces()
  {
    for (var i = 0; i < pieces.Count; i++)
    {
      var piece = pieces[i];
      if (!IsPieceActive(piece) || piece.HasBounced)
      {
        continue;
      }

      if (piece.ShouldRegisterBounce())
      {
        RegisterBounce(piece);
      }
    }
  }

  bool IsPieceReady(Piece piece)
  {
    return piece != null && piece.IsReadyForDestruction();
  }

  bool IsPieceActive(Piece piece)
  {
    return piece != null && piece.IsSimulatingPhysics;
  }

  void RegisterBounce(Piece piece)
  {
    piece.RegisterBounce(bounceImpulseMin, bounceImpulseMax, postBounceDisableDelay, "fakeBounce");
  }

  public void HandlePieceCollision(Piece source, Collision2D collision)
  {
    if (source == null || collision == null) return;
    var otherCol = collision.collider;
    if (!otherCol) return;
    if (otherCol.CompareTag(wallTag))
    {
      return;
    }

    if (!pieceColliders.Contains(otherCol)) return;
    var selfCol = source.Collider;
    if (!selfCol) return;
    if (selfCol == otherCol) return;

    StartCoroutine(IgnorePairTemporarily(selfCol, otherCol, GetSelfIgnoreWait()));
  }

  IEnumerator IgnorePairTemporarily(Collider2D a, Collider2D b, WaitForSeconds wait)
  {
    if (!a || !b) yield break;
    Physics2D.IgnoreCollision(a, b, true);
    yield return wait;
    if (a && b)
    {
      Physics2D.IgnoreCollision(a, b, false);
    }
  }

  WaitForSeconds GetSelfIgnoreWait()
  {
    if (cachedSelfIgnoreWait == null || !Mathf.Approximately(cachedSelfIgnoreDuration, selfIgnoreDuration))
    {
      cachedSelfIgnoreDuration = selfIgnoreDuration;
      cachedSelfIgnoreWait = new WaitForSeconds(selfIgnoreDuration);
    }
    return cachedSelfIgnoreWait;
  }
}
