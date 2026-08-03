using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class PiecePool
{
    public string poolName = "Piece Pool";
    public Collider2D collider;
    public List<GameObject> pieces = new List<GameObject>();
}

public class Destructible : MonoBehaviour
{
    private int lastProcessedHitFrame = -1;
    private ulong lastProcessedHitSourceId;
    private CharacterState characterState;
    private DestructibleHitPieceParticles hitPieceParticles;

    [Header("Colliders")]
    public List<Collider2D> colliders = new List<Collider2D>();

    // Hidden legacy collider references for backward compatibility
    [HideInInspector] public BoxCollider2D leftCollider;
    [HideInInspector] public BoxCollider2D rightCollider;

    [Header("Automatic Setup")]
    [Tooltip("Assign a parent object here. All its children will be auto-assigned to the pool of the collider they sit under.")]
    public Transform piecesParent;

    [Tooltip("If true, automatically detects and populates piece pools on Start based on collider positions.")]
    public bool autoPopulateOnStart = true;

    [Tooltip("Check this to print out the name of every single object that touches this rock to the console.")]
    public bool debugCollisions = false;

    [Header("Piece Pools (Auto-Populated)")]
    public List<PiecePool> piecePools = new List<PiecePool>();

    // Hidden legacy piece pools for backward compatibility
    [HideInInspector] public List<GameObject> leftPieces = new List<GameObject>();
    [HideInInspector] public List<GameObject> rightPieces = new List<GameObject>();

    [Header("Launch Settings")]
    public float minLaunchForce = 5f;
    public float maxLaunchForce = 15f;
    public int minDepth = 1;
    public int maxDepth = 3;

    private void Start()
    {
        SyncLegacyColliders();

        // FIX: In Unity 2D, Kinematic bodies (like projectiles and enemies) DO NOT trigger collisions
        // with Static bodies (like this rock) by default! We MUST make this rock a Kinematic body
        // with "Use Full Kinematic Contacts" enabled to force Unity to register their hitboxes.
        if (!TryGetComponent(out Rigidbody2D rootRb))
        {
            rootRb = gameObject.AddComponent<Rigidbody2D>();
        }
        rootRb.bodyType = RigidbodyType2D.Kinematic;
        rootRb.useFullKinematicContacts = true;
        // Ensure it doesn't move or fall
        rootRb.linearVelocity = Vector2.zero;
        rootRb.angularVelocity = 0f;

        // Add a forwarder to our child colliders so they pass hit events up to this script
        foreach (Collider2D col in colliders)
        {
            if (col == null) continue;

            if (col.gameObject != this.gameObject)
            {
                if (!col.TryGetComponent(out DestructibleHitboxForwarder fwd))
                {
                    fwd = col.gameObject.AddComponent<DestructibleHitboxForwarder>();
                }
                fwd.destructible = this;
                fwd.myCollider = col;
            }

            // Listen to HurtBox2D OnHit to avoid physics race conditions with fast projectiles
            if (col.TryGetComponent(out HurtBox2D hb))
            {
                Collider2D targetCol = col;
                hb.OnHit.AddListener((hitBox) => OnHurtBoxHit(targetCol, hitBox));
            }
        }

        // Auto populate piece pools if requested or if piecePools is currently empty
        if (autoPopulateOnStart || IsPoolsEmpty())
        {
            AutoPopulatePiecePools();
        }

        // Shuffle each pool so pieces pop off in random order
        foreach (var pool in piecePools)
        {
            if (pool != null && pool.pieces != null)
            {
                ShuffleList(pool.pieces);
            }
        }

        SyncLegacyPieces();

        if (!TryGetComponent(out hitPieceParticles))
        {
            hitPieceParticles = gameObject.AddComponent<DestructibleHitPieceParticles>();
        }
        hitPieceParticles.Initialize(piecesParent);

        // Ensure the global shadow pool is pre-warmed so pieces don't create it mid-combat
        DestructiblePiece.EnsureShadowPool();

        // Pre-initialize all pieces with their required components so we don't
        // incur AddComponent and Awake/Start allocations during combat!
        PreInitializePieces();

        // Find all child rigidbodies and set them to inactive initially
        Rigidbody2D[] rbs = GetComponentsInChildren<Rigidbody2D>(true);
        foreach (Rigidbody2D childRb in rbs)
        {
            if (childRb.gameObject != this.gameObject)
            {
                childRb.simulated = false;
                childRb.bodyType = RigidbodyType2D.Kinematic;
            }
        }
    }

    private void PreInitializePieces()
    {
        if (piecePools == null) return;
        foreach (var pool in piecePools)
        {
            if (pool.pieces == null) continue;
            foreach (var piece in pool.pieces)
            {
                if (piece == null) continue;

                if (!piece.TryGetComponent(out DestructiblePiece pieceBehaviour))
                {
                    pieceBehaviour = piece.AddComponent<DestructiblePiece>();
                }
                pieceBehaviour.enabled = false;

                if (!piece.TryGetComponent(out Rigidbody2D pieceRb))
                {
                    pieceRb = piece.AddComponent<Rigidbody2D>();
                }

                pieceRb.bodyType = RigidbodyType2D.Kinematic;
                pieceRb.simulated = false;
            }
        }
    }

    [ContextMenu("Auto Populate Piece Pools")]
    public void AutoPopulatePiecePools()
    {
        SyncLegacyColliders();

        if (colliders == null || colliders.Count == 0)
        {
            // Fallback: try to locate colliders on child objects if list is empty
            Collider2D[] childCols = GetComponentsInChildren<Collider2D>();
            foreach (var col in childCols)
            {
                if (col != null && !colliders.Contains(col))
                {
                    colliders.Add(col);
                }
            }
        }

        if (colliders == null || colliders.Count == 0)
        {
            Debug.LogWarning($"[Destructible] {gameObject.name}: No colliders assigned for piece pool auto-population.");
            return;
        }

        // Rebuild piecePools list based on active colliders
        piecePools.Clear();
        for (int i = 0; i < colliders.Count; i++)
        {
            Collider2D col = colliders[i];
            if (col == null) continue;

            string poolLabel = string.IsNullOrEmpty(col.gameObject.name) ? $"Pool {i + 1}" : $"Pool ({col.gameObject.name})";
            piecePools.Add(new PiecePool
            {
                poolName = poolLabel,
                collider = col,
                pieces = new List<GameObject>()
            });
        }

        if (piecePools.Count == 0) return;

        // Gather candidate piece GameObjects
        List<GameObject> piecesToAssign = new List<GameObject>();
        if (piecesParent != null)
        {
            foreach (Transform child in piecesParent)
            {
                if (child != null && !IsColliderObject(child.gameObject))
                {
                    piecesToAssign.Add(child.gameObject);
                }
            }
        }
        else
        {
            foreach (Transform child in transform)
            {
                if (child != null && child.gameObject != gameObject && !IsColliderObject(child.gameObject))
                {
                    piecesToAssign.Add(child.gameObject);
                }
            }
        }

        // Detect which collider pool each piece belongs to based on spatial overlap
        foreach (GameObject piece in piecesToAssign)
        {
            PiecePool bestPool = FindBestPoolForPiece(piece);
            if (bestPool != null)
            {
                bestPool.pieces.Add(piece);
            }
        }

        SyncLegacyPieces();
    }

    private bool IsColliderObject(GameObject obj)
    {
        if (obj == null) return false;
        foreach (var pool in piecePools)
        {
            if (pool.collider != null && pool.collider.gameObject == obj) return true;
        }
        foreach (var col in colliders)
        {
            if (col != null && col.gameObject == obj) return true;
        }
        return false;
    }

    private PiecePool FindBestPoolForPiece(GameObject piece)
    {
        if (piece == null || piecePools == null || piecePools.Count == 0) return null;

        Vector3 piecePos = piece.transform.position;
        Bounds pieceBounds = new Bounds(piecePos, Vector3.zero);
        bool hasBounds = false;

        SpriteRenderer sr = piece.GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            pieceBounds = sr.bounds;
            piecePos = pieceBounds.center;
            hasBounds = true;
        }
        else
        {
            Collider2D col2d = piece.GetComponent<Collider2D>();
            if (col2d != null)
            {
                pieceBounds = col2d.bounds;
                piecePos = pieceBounds.center;
                hasBounds = true;
            }
        }

        PiecePool bestPool = null;
        float bestDistance = float.MaxValue;
        bool foundDirectOverlap = false;

        foreach (var pool in piecePools)
        {
            if (pool.collider == null) continue;
            Collider2D c = pool.collider;

            // 1. Direct point overlap check inside collider shape
            bool isInside = c.OverlapPoint(piecePos);
            if (isInside)
            {
                float distToCenter = Vector2.Distance(piecePos, c.bounds.center);
                if (!foundDirectOverlap || distToCenter < bestDistance)
                {
                    foundDirectOverlap = true;
                    bestDistance = distToCenter;
                    bestPool = pool;
                }
                continue;
            }

            if (foundDirectOverlap) continue;

            // 2. Bounds intersection check
            if (c.bounds.Contains(piecePos) || (hasBounds && c.bounds.Intersects(pieceBounds)))
            {
                float distToCenter = Vector2.Distance(piecePos, c.bounds.center);
                if (distToCenter < bestDistance)
                {
                    bestDistance = distToCenter;
                    bestPool = pool;
                }
                continue;
            }

            // 3. Distance from piece position to closest point on collider
            Vector2 closestPoint = c.bounds.ClosestPoint(piecePos);
            float distToClosest = Vector2.Distance(piecePos, closestPoint);
            if (distToClosest < bestDistance)
            {
                bestDistance = distToClosest;
                bestPool = pool;
            }
        }

        return bestPool;
    }

    public void ProcessCollision(Collision2D collision, Collider2D receiverCollider)
    {
        GlobalCollisionCooldown.TryApply(collision);
        if (debugCollisions) Debug.Log($"[Destructible] OnCollisionEnter2D from: {collision.collider.gameObject.name} on {(receiverCollider != null ? receiverCollider.name : "root")}");

        // Prioritize actual HitBox2D components using static cache
        bool isHitBox = HitBox2D.TryGet(collision.collider, out var hitBox);
        if (!isHitBox)
        {
            string colliderName = collision.collider.gameObject.name;
            bool isFallbackHit =
                colliderName.IndexOf("feettouch", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                colliderName.IndexOf("hit", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isFallbackHit)
            {
                if (debugCollisions) Debug.Log($"[Destructible] Rejected {colliderName}: Not a HitBox2D and failed name filter.");
                return;
            }
        }

        PiecePool targetPool = GetPoolForReceiver(receiverCollider, collision.collider.transform.position);
        if (targetPool != null && TryBeginHit(hitBox, collision.collider))
        {
            PlayBrokenPieceHitParticles(
                targetPool.collider,
                collision.collider.transform.position
            );
            PlayHitEffect(targetPool.collider, hitBox);
            GrantEsperHitFormXp(hitBox);
            HandleCollision(targetPool);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (colliders.Contains(collision.otherCollider))
        {
            ProcessCollision(collision, collision.otherCollider);
        }
    }

    public void ProcessTrigger(Collider2D collider, Collider2D receiverCollider)
    {
        if (debugCollisions) Debug.Log($"[Destructible] OnTriggerEnter2D from: {collider.gameObject.name} on {(receiverCollider != null ? receiverCollider.name : "root")}");

        bool isHitBox = HitBox2D.TryGet(collider, out var hitBox);
        if (!isHitBox)
        {
            string colliderName = collider.gameObject.name;
            bool isFallbackHit =
                colliderName.IndexOf("feettouch", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                colliderName.IndexOf("hit", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isFallbackHit)
            {
                if (debugCollisions) Debug.Log($"[Destructible] Rejected {colliderName}: Not a HitBox2D and failed name filter.");
                return;
            }
        }

        PiecePool targetPool = GetPoolForReceiver(receiverCollider, collider.transform.position);
        if (targetPool != null && TryBeginHit(hitBox, collider))
        {
            PlayBrokenPieceHitParticles(
                targetPool.collider,
                collider.transform.position
            );
            PlayHitEffect(targetPool.collider, hitBox);
            GrantEsperHitFormXp(hitBox);
            HandleCollision(targetPool);
        }
    }

    private void OnTriggerEnter2D(Collider2D collider)
    {
        ProcessTrigger(collider, null);
    }

    private void OnHurtBoxHit(Collider2D targetCollider, HitBox2D hitBox)
    {
        if (hitBox == null) return;
        if (debugCollisions) Debug.Log($"[Destructible] OnHurtBoxHit on {(targetCollider != null ? targetCollider.name : "unknown")} triggered directly by {hitBox.gameObject.name}");
        PiecePool targetPool = GetPoolForReceiver(targetCollider, hitBox.transform.position);
        if (targetPool != null && TryBeginHit(hitBox, null))
        {
            PlayBrokenPieceHitParticles(
                targetCollider,
                hitBox.transform.position
            );
            PlayHitEffect(targetCollider, hitBox);
            GrantEsperHitFormXp(hitBox);
            HandleCollision(targetPool);
        }
    }

    private static void PlayHitEffect(Collider2D targetCollider, HitBox2D hitBox)
    {
        if (hitBox != null &&
            HurtBox2D.TryResolve(targetCollider, out HurtBox2D hurtBox))
        {
            HitEmphasisBurst.Play(hurtBox, hitBox);
        }
    }

    private void PlayBrokenPieceHitParticles(
        Collider2D targetCollider,
        Vector3 sourcePosition
    )
    {
        if (hitPieceParticles == null)
        {
            return;
        }

        var impactPosition = targetCollider != null
            ? (Vector3)targetCollider.ClosestPoint(sourcePosition)
            : transform.position;
        impactPosition.z = transform.position.z;

        var impactDirection = (Vector2)(impactPosition - sourcePosition);
        hitPieceParticles.Play(impactPosition, impactDirection);
    }

    private void GrantEsperHitFormXp(HitBox2D hitBox)
    {
        if (hitBox == null ||
            hitBox.IsEnemyOwned ||
            hitBox.ActorOwner == null ||
            hitBox.ActorOwner.GetComponent<GearController>() == null)
        {
            return;
        }

        if (characterState == null)
        {
            characterState = SingleSceneManager.ResolveGameplayCharacterState();
        }

        characterState?.GrantActiveFormXp(1, "destructible_hit");
    }

    private bool TryBeginHit(HitBox2D hitBox, Collider2D sourceCollider)
    {
        ulong sourceId = hitBox != null
            ? ObjectEntityId.GetRawValue(hitBox)
            : sourceCollider != null
                ? ObjectEntityId.GetRawValue(sourceCollider)
                : 0UL;
        int frame = Time.frameCount;
        if (lastProcessedHitFrame == frame &&
            lastProcessedHitSourceId == sourceId)
        {
            return false;
        }

        lastProcessedHitFrame = frame;
        lastProcessedHitSourceId = sourceId;
        return true;
    }

    private PiecePool GetPoolForReceiver(Collider2D receiver, Vector3 hitPosition)
    {
        if (piecePools == null || piecePools.Count == 0) return null;

        // 1. Check direct match on receiver collider
        if (receiver != null)
        {
            foreach (var pool in piecePools)
            {
                if (pool.collider == receiver && pool.pieces != null && pool.pieces.Count > 0)
                {
                    return pool;
                }
            }
        }

        // 2. Fallback: find non-empty pool whose collider center is closest to hitPosition
        PiecePool closestPool = null;
        float minDistance = float.MaxValue;

        foreach (var pool in piecePools)
        {
            if (pool.pieces != null && pool.pieces.Count > 0)
            {
                if (pool.collider != null)
                {
                    float dist = Vector2.Distance(hitPosition, pool.collider.bounds.center);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestPool = pool;
                    }
                }
                else if (closestPool == null)
                {
                    closestPool = pool;
                }
            }
        }

        return closestPool;
    }

    private void ShuffleList(List<GameObject> list)
    {
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            GameObject temp = list[i];
            int randomIndex = Random.Range(i, list.Count);
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }

    private void HandleCollision(PiecePool pool)
    {
        if (pool == null || pool.pieces == null || pool.pieces.Count == 0) return;

        GameObject startPiece = pool.pieces[0];
        RemovePieceFromAllPools(startPiece);

        if (startPiece != null)
        {
            LaunchPiece(startPiece);

            int depth = Random.Range(minDepth, maxDepth + 1);
            GameObject currentPiece = startPiece;

            for (int i = 1; i < depth; i++)
            {
                ConnectedPieces node = currentPiece.GetComponent<ConnectedPieces>();
                if (node != null)
                {
                    if (node.pair != null) node.pair.SetActive(false);

                    GameObject nextPiece = null;
                    if (node.attachedPiece1 != null && node.attachedPiece2 != null)
                    {
                        nextPiece = Random.value > 0.5f ? node.attachedPiece1 : node.attachedPiece2;
                    }
                    else if (node.attachedPiece1 != null)
                    {
                        nextPiece = node.attachedPiece1;
                    }
                    else if (node.attachedPiece2 != null)
                    {
                        nextPiece = node.attachedPiece2;
                    }

                    if (nextPiece != null)
                    {
                        LaunchPiece(nextPiece);
                        currentPiece = nextPiece;
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            ReorganizePools();
        }
    }

    private static readonly List<GameObject> reorganizeScratch = new List<GameObject>(32);

    private void ReorganizePools()
    {
        if (piecePools == null || piecePools.Count == 0) return;

        // Gather all remaining unlaunched pieces across all pools
        reorganizeScratch.Clear();
        foreach (var pool in piecePools)
        {
            if (pool.pieces != null)
            {
                foreach (var piece in pool.pieces)
                {
                    if (piece != null && !reorganizeScratch.Contains(piece))
                    {
                        reorganizeScratch.Add(piece);
                    }
                }
                pool.pieces.Clear();
            }
        }

        if (reorganizeScratch.Count == 0) return;

        // Evenly and randomly redistribute remaining pieces among collider pools
        int poolCount = piecePools.Count;
        for (int i = 0; i < reorganizeScratch.Count; i++)
        {
            int poolIndex = Random.Range(0, poolCount);
            piecePools[poolIndex].pieces.Add(reorganizeScratch[i]);
        }

        foreach (var pool in piecePools)
        {
            if (pool.pieces != null)
            {
                ShuffleList(pool.pieces);
            }
        }

        SyncLegacyPieces();
    }

    private void RemovePieceFromAllPools(GameObject pieceObj)
    {
        if (pieceObj == null) return;
        if (piecePools != null)
        {
            foreach (var pool in piecePools)
            {
                if (pool.pieces != null)
                {
                    pool.pieces.Remove(pieceObj);
                }
            }
        }
        leftPieces.Remove(pieceObj);
        rightPieces.Remove(pieceObj);
    }

    private void LaunchPiece(GameObject pieceObj)
    {
        if (pieceObj == null) return;

        RemovePieceFromAllPools(pieceObj);

        DestructiblePiece pieceBehaviour = pieceObj.GetComponent<DestructiblePiece>();
        if (pieceBehaviour != null)
        {
            pieceBehaviour.BeginFlight();
        }

        // Break the piece out of the Rock's hierarchy
        pieceObj.transform.SetParent(null, true);

        Rigidbody2D rb = pieceObj.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.simulated = true;
            rb.gravityScale = 1f;
            rb.linearDamping = 0f;
            rb.angularDamping = 1.5f;

            float horizontalDirection = Random.Range(-1f, 1f);
            if (Mathf.Abs(horizontalDirection) < 0.5f)
            {
                horizontalDirection = (Random.value < 0.5f ? -1f : 1f) * 0.5f;
            }
            Vector2 forceDirection = new Vector2(
                horizontalDirection,
                Random.Range(-0.03f, 0.08f)
            ).normalized;
            float forceMagnitude = Random.Range(minLaunchForce, maxLaunchForce);

            rb.AddForce(forceDirection * forceMagnitude, ForceMode2D.Impulse);
            // Keep the rotation readable and restrained so the break-up feels
            // cinematic instead of like a high-speed physics burst.
            rb.AddTorque(Random.Range(-1f, 1f), ForceMode2D.Impulse);
        }
        
        pieceObj.SetActive(true);
    }

    private bool IsPoolsEmpty()
    {
        if (piecePools == null || piecePools.Count == 0) return true;
        foreach (var pool in piecePools)
        {
            if (pool.pieces != null && pool.pieces.Count > 0) return false;
        }
        return true;
    }

    private void SyncLegacyColliders()
    {
        if (colliders == null) colliders = new List<Collider2D>();

        if (leftCollider != null && !colliders.Contains(leftCollider))
        {
            colliders.Add(leftCollider);
        }
        if (rightCollider != null && !colliders.Contains(rightCollider))
        {
            colliders.Add(rightCollider);
        }
    }

    private void SyncLegacyPieces()
    {
        leftPieces.Clear();
        rightPieces.Clear();

        if (piecePools != null)
        {
            if (piecePools.Count > 0 && piecePools[0].pieces != null)
            {
                leftPieces.AddRange(piecePools[0].pieces);
            }
            if (piecePools.Count > 1 && piecePools[1].pieces != null)
            {
                rightPieces.AddRange(piecePools[1].pieces);
            }
        }
    }
}

public class DestructibleHitboxForwarder : MonoBehaviour
{
    public Destructible destructible;
    public Collider2D myCollider;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (destructible != null) destructible.ProcessTrigger(collision, myCollider);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (destructible != null) destructible.ProcessCollision(collision, myCollider);
    }
}
