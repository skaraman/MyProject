using UnityEngine;
using System.Collections.Generic;

public class DestructiblePiece : MonoBehaviour
{
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;

    private float floorY;
    private bool hasSettled = false;

    [Header("Settings")]
    public float fallDistance = 0.5f;
    public float bounceDampening = 0.4f; // How much velocity is retained on bounce
    public float minBounceVelocity = 1.0f; // Velocity threshold to settle
    public float lifeTime = 50.0f; // Time before fading starts
    public float fadeTime = 2.0f; // How long the fade out lasts

    private GameObject shadowObj;
    private SpriteRenderer shadowRenderer;
    private Vector3 initialShadowScale;

    private Transform originalParent;
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private Vector3 initialLocalScale;
    private Color initialColor = Color.white;
    private bool stateCaptured = false;

    private float cleanupTimer;
    private bool isFading;
    private bool flightActive;
    private float startPieceAlpha;
    private float startShadowAlpha;


    private static GameObject shadowPrefab;
    private static Pool shadowPool;
    private const int ShadowPoolCapacity = 128;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void InitPool()
    {
        if (shadowPrefab != null)
        {
            Destroy(shadowPrefab);
            shadowPrefab = null;
        }
        shadowPool = null;
    }

    public static void EnsureShadowPool()
    {
        if (shadowPrefab == null)
        {
            shadowPrefab = new GameObject("DestructiblePiece_ShadowPrefab");
            shadowPrefab.SetActive(false);
            var sr = shadowPrefab.AddComponent<SpriteRenderer>();
            sr.color = new Color(0, 0, 0, 0.4f);
            Object.DontDestroyOnLoad(shadowPrefab);
        }

        if (shadowPool == null)
        {
            shadowPool = Pool.GetShared(shadowPrefab, null, ShadowPoolCapacity);
        }
    }

    void Awake()
    {
        CaptureState();
    }

    void CaptureState()
    {
        if (stateCaptured) return;
        originalParent = transform.parent;
        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
        initialLocalScale = transform.localScale;

        spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        if (spriteRenderer != null)
        {
            initialColor = spriteRenderer.color;
        }
        stateCaptured = true;
    }

    void OnEnable()
    {
        hasSettled = false;
        cleanupTimer = 0f;
        isFading = false;

        if (spriteRenderer != null)
        {
            spriteRenderer.color = initialColor;
        }
    }

    void Start()
    {
        CacheComponents();
        if (!flightActive)
        {
            enabled = false;
        }
    }

    public void BeginFlight()
    {
        CaptureState();
        CacheComponents();
        flightActive = true;
        hasSettled = false;
        cleanupTimer = 0f;
        isFading = false;

        if (spriteRenderer != null)
        {
            spriteRenderer.color = initialColor;
        }

        // Find the root destructible to figure out where the "ground" is
        Destructible parentDestructible = originalParent != null ? originalParent.GetComponentInParent<Destructible>() : null;
        float groundY = transform.position.y; // fallback

        if (parentDestructible != null)
        {
            groundY = parentDestructible.transform.position.y;
        }

        // Set the fake floor position based on the ground level
        floorY = groundY - Random.Range(fallDistance * 0.5f, fallDistance * 1.5f);

        CreateShadow();
        enabled = true;
    }

    void CacheComponents()
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
    }

    void CreateShadow()
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null) return;

        if (shadowObj != null && shadowPool != null)
        {
            shadowPool.Despawn(shadowObj);
            shadowObj = null;
        }

        EnsureShadowPool();

        shadowObj = shadowPool.Spawn(new Vector3(transform.position.x, floorY, transform.position.z), Quaternion.identity);

        shadowRenderer = shadowObj.GetComponent<SpriteRenderer>();
        // Use the exact same sprite as the piece, but tinted black
        shadowRenderer.sprite = spriteRenderer.sprite;

        // Match the sorting layer, but render just behind the piece
        shadowRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
        shadowRenderer.sortingOrder = spriteRenderer.sortingOrder - 1;

        // Flatten the sprite so it looks like a shadow laying on the floor
        shadowObj.transform.localScale = new Vector3(transform.localScale.x, transform.localScale.y * 0.3f, transform.localScale.z);
        initialShadowScale = shadowObj.transform.localScale;
    }

    void Update()
    {
        if (!flightActive)
        {
            enabled = false;
            return;
        }

        if (!hasSettled)
        {
            HandleYSorting();
        }

        // Only process physics/shadows if we haven't settled AND we are actively dynamic (launched)
        if (!hasSettled && rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            UpdateShadow();

            // Check if we hit our fake floor
            if (transform.position.y <= floorY && rb.linearVelocity.y <= 0f)
            {
                // If we hit the floor with enough downward velocity, bounce
                if (Mathf.Abs(rb.linearVelocity.y) > minBounceVelocity)
                {
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, -rb.linearVelocity.y * bounceDampening);
                    // Push the piece slightly above the floor so it doesn't get stuck underground
                    transform.position = new Vector3(transform.position.x, floorY + 0.01f, transform.position.z);
                }
                else
                {
                    // Settle onto the floor completely
                    hasSettled = true;
                    rb.gravityScale = 0f;
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);

                    // High drag simulates ground friction so it slides to a stop when kicked
                    rb.linearDamping = 5f;
                    rb.angularDamping = 5f;

                    // Piece is resting, start the timer to despawn it
                    cleanupTimer = lifeTime;
                }
            }
        }
        else if (hasSettled)
        {
            if (cleanupTimer > 0f && !isFading)
            {
                cleanupTimer -= Time.deltaTime;
                if (cleanupTimer <= 0f)
                {
                    isFading = true;
                    cleanupTimer = fadeTime;
                    startPieceAlpha = spriteRenderer != null ? spriteRenderer.color.a : 1f;
                    startShadowAlpha = shadowRenderer != null ? shadowRenderer.color.a : 0f;
                }
            }
            else if (isFading)
            {
                cleanupTimer -= Time.deltaTime;
                float normalizedTime = 1f - Mathf.Clamp01(cleanupTimer / fadeTime);

                if (spriteRenderer != null)
                {
                    Color pieceColor = spriteRenderer.color;
                    pieceColor.a = Mathf.Lerp(startPieceAlpha, 0f, normalizedTime);
                    spriteRenderer.color = pieceColor;
                }

                if (shadowRenderer != null)
                {
                    Color shadowColor = shadowRenderer.color;
                    shadowColor.a = Mathf.Lerp(startShadowAlpha, 0f, normalizedTime);
                    shadowRenderer.color = shadowColor;
                }

                if (cleanupTimer <= 0f)
                {
                    RecyclePiece();
                }
            }
        }
    }

    void HandleYSorting()
    {
        if (spriteRenderer != null)
        {
            // Dynamic Y-Sorting: Lower on screen = renders in front
            spriteRenderer.sortingOrder = Mathf.RoundToInt(-transform.position.y * 100);

            if (shadowRenderer != null)
            {
                shadowRenderer.sortingOrder = spriteRenderer.sortingOrder - 1;
            }
        }
    }

    void UpdateShadow()
    {
        if (shadowObj != null)
        {
            // Keep the shadow pinned to the floorY, but follow the piece's X position
            shadowObj.transform.position = new Vector3(transform.position.x, floorY, transform.position.z);

            // Calculate how high the piece is currently flying above its floor
            float height = Mathf.Max(0, transform.position.y - floorY);

            // Shrink the shadow slightly as the piece flies higher
            float scaleMultiplier = Mathf.Clamp01(1f - (height * 0.2f));
            shadowObj.transform.localScale = initialShadowScale * scaleMultiplier;

            // Fade the shadow out slightly as the piece flies higher
            Color c = shadowRenderer.color;
            c.a = Mathf.Clamp01(0.4f - (height * 0.1f));
            shadowRenderer.color = c;
        }
    }

    void RecyclePiece()
    {
        flightActive = false;

        // Cleanup objects from the scene
        if (shadowObj != null && shadowPool != null)
        {
            shadowPool.Despawn(shadowObj);
            shadowObj = null;
        }

        if (originalParent != null)
        {
            transform.SetParent(originalParent);
            transform.localPosition = initialLocalPosition;
            transform.localRotation = initialLocalRotation;
            transform.localScale = initialLocalScale;

            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.simulated = false;
                rb.bodyType = RigidbodyType2D.Kinematic;
            }

            enabled = false;
            gameObject.SetActive(false);
            if (spriteRenderer != null)
            {
                spriteRenderer.color = initialColor;
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        // Safety net: if the piece gets destroyed early (e.g. level ends, or hit by an explosion),
        // make sure we don't leave an orphaned shadow floating around.
        if (shadowObj != null && shadowPool != null)
        {
            shadowPool.Despawn(shadowObj);
            shadowObj = null;
        }
    }

}
