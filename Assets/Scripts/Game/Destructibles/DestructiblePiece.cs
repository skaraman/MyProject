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
    public float lifeTime = 8.0f; // Total time from launch before fading starts
    public float fadeTime = 1.0f; // How long the fade out lasts

    private GameObject shadowObj;
    private SpriteRenderer shadowRenderer;
    private Vector3 initialShadowScale;

    private Transform originalParent;
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private Vector3 initialLocalScale;
    private Color initialColor = Color.white;
    private int initialSortingOrder;
    private bool stateCaptured = false;

    private float cleanupTimer;
    private bool isFading;
    private bool flightActive;
    private bool shaderFadeActive;
    private float startPieceAlpha;
    private float startShadowAlpha;
    private float lastSortedDepthY = float.MinValue;
    private int sortingOrderJitter;
    private Material originalSharedMaterial;
    private Material fadeMaterialVariant;
    private MaterialPropertyBlock fadePropertyBlock;

    private const float SortingUnitsPerWorldUnit = 100f;
    private const int SortingOrderJitter = 3;
    // Rigidbody2D angular velocity is degrees per second. Keep fragments
    // below one rotation per second for a readable cinematic scatter.
    private const float MaximumCinematicAngularVelocity = 240f;
    private const string FadeKeyword = "FADE_ON";
    private const float FadeVisibleAmount = -0.1f;
    private const float FadeHiddenAmount = 1f;
    private static readonly int FadeAmountPropertyId = Shader.PropertyToID("_FadeAmount");

    private static GameObject shadowPrefab;
    private static Pool shadowPool;
    private const int ShadowPoolCapacity = 128;
    private sealed class FadeMaterialEntry
    {
        public Material material;
        public int activeUsers;
    }
    private static readonly Dictionary<Material, FadeMaterialEntry> FadeMaterialVariants = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void InitPool()
    {
        if (shadowPrefab != null)
        {
            Destroy(shadowPrefab);
            shadowPrefab = null;
        }
        shadowPool = null;

        foreach (FadeMaterialEntry entry in FadeMaterialVariants.Values)
        {
            if (entry.material != null)
            {
                Destroy(entry.material);
            }
        }
        FadeMaterialVariants.Clear();
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
            initialSortingOrder = spriteRenderer.sortingOrder;
        }
        stateCaptured = true;
    }

    void OnEnable()
    {
        // BeginFlight can run while a pooled piece is inactive. Do not wipe the
        // launch timer when SetActive(true) invokes OnEnable immediately after.
        if (flightActive)
        {
            return;
        }

        hasSettled = false;
        cleanupTimer = 0f;
        isFading = false;
        lastSortedDepthY = float.MinValue;

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
        cleanupTimer = Mathf.Max(0f, lifeTime);
        isFading = false;
        shaderFadeActive = false;

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
        sortingOrderJitter = Random.Range(-SortingOrderJitter, SortingOrderJitter + 1);
        lastSortedDepthY = float.MinValue;

        CreateShadow();
        HandlePileSorting(force: true);
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

        if (shadowObj != null)
        {
            ReleaseShadow();
        }

        EnsureShadowPool();

        shadowObj = shadowPool.Spawn(new Vector3(transform.position.x, floorY, transform.position.z), Quaternion.identity);
        if (shadowObj == null)
        {
            return;
        }

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

        HandlePileSorting(force: false);
        if (hasSettled)
        {
            UpdateGroundedShadow();
        }

        UpdateCleanup();
        if (!flightActive || isFading)
        {
            return;
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
                    transform.position = new Vector3(transform.position.x, floorY, transform.position.z);
                    rb.gravityScale = 0f;
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);

                    // High drag simulates ground friction so it slides to a stop when kicked
                    rb.linearDamping = 5f;
                    rb.angularDamping = 5f;
                }
            }
        }
    }

    void FixedUpdate()
    {
        if (!flightActive || isFading || rb == null || !rb.simulated)
        {
            return;
        }

        rb.angularVelocity = Mathf.Clamp(
            rb.angularVelocity,
            -MaximumCinematicAngularVelocity,
            MaximumCinematicAngularVelocity
        );
    }

    void UpdateCleanup()
    {
        cleanupTimer -= Time.deltaTime;

        if (!isFading)
        {
            if (cleanupTimer > 0f)
            {
                return;
            }

            isFading = true;
            cleanupTimer = Mathf.Max(0f, fadeTime);
            startPieceAlpha = spriteRenderer != null ? spriteRenderer.color.a : 1f;
            startShadowAlpha = shadowRenderer != null ? shadowRenderer.color.a : 0f;
            shaderFadeActive = BeginPieceShaderFade();

            // Once cleanup starts, remove the fragment from the physics scene.
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.simulated = false;
            }
        }

        if (cleanupTimer <= 0f)
        {
            ApplyPieceFade(1f);
            ApplyShadowFade(1f);
            RetirePiece();
            return;
        }

        float normalizedTime = 1f - Mathf.Clamp01(cleanupTimer / fadeTime);
        ApplyPieceFade(normalizedTime);
        ApplyShadowFade(normalizedTime);
    }

    void ApplyPieceFade(float normalizedTime)
    {
        normalizedTime = Mathf.Clamp01(normalizedTime);
        if (shaderFadeActive)
        {
            SetShaderFadeAmount(Mathf.Lerp(FadeVisibleAmount, FadeHiddenAmount, normalizedTime));
            return;
        }

        // All authored destructible materials use AllIn1SpriteShader. Retain an
        // alpha fallback so an incorrectly assigned material still disappears.
        if (spriteRenderer != null)
        {
            Color pieceColor = spriteRenderer.color;
            pieceColor.a = Mathf.Lerp(startPieceAlpha, 0f, normalizedTime);
            spriteRenderer.color = pieceColor;
        }
    }

    void ApplyShadowFade(float normalizedTime)
    {
        if (shadowRenderer != null)
        {
            Color shadowColor = shadowRenderer.color;
            shadowColor.a = Mathf.Lerp(startShadowAlpha, 0f, Mathf.Clamp01(normalizedTime));
            shadowRenderer.color = shadowColor;
        }
    }

    bool BeginPieceShaderFade()
    {
        if (spriteRenderer == null)
        {
            return false;
        }

        Material sourceMaterial = spriteRenderer.sharedMaterial;
        if (sourceMaterial == null || !sourceMaterial.HasProperty(FadeAmountPropertyId))
        {
            return false;
        }

        // FADE_ON is a local material keyword. Use a shared, short-lived variant
        // so enabling it cannot change every SpriteRenderer using WorldProps.
        originalSharedMaterial = sourceMaterial;
        fadeMaterialVariant = AcquireFadeMaterialVariant(sourceMaterial);
        if (fadeMaterialVariant == null)
        {
            originalSharedMaterial = null;
            return false;
        }

        spriteRenderer.sharedMaterial = fadeMaterialVariant;
        SetShaderFadeAmount(FadeVisibleAmount);
        return true;
    }

    static Material AcquireFadeMaterialVariant(Material sourceMaterial)
    {
        if (!FadeMaterialVariants.TryGetValue(sourceMaterial, out FadeMaterialEntry entry) ||
            entry.material == null)
        {
            entry = new FadeMaterialEntry
            {
                material = new Material(sourceMaterial)
            };
            entry.material.EnableKeyword(FadeKeyword);
            FadeMaterialVariants[sourceMaterial] = entry;
        }

        entry.activeUsers++;
        return entry.material;
    }

    static void ReleaseFadeMaterialVariant(Material sourceMaterial, Material variant)
    {
        if (ReferenceEquals(sourceMaterial, null) ||
            !FadeMaterialVariants.TryGetValue(sourceMaterial, out FadeMaterialEntry entry) ||
            entry.material != variant)
        {
            return;
        }

        entry.activeUsers = Mathf.Max(0, entry.activeUsers - 1);
        if (entry.activeUsers > 0)
        {
            return;
        }

        FadeMaterialVariants.Remove(sourceMaterial);
        if (entry.material != null)
        {
            Destroy(entry.material);
        }
    }

    void SetShaderFadeAmount(float amount)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        fadePropertyBlock ??= new MaterialPropertyBlock();
        spriteRenderer.GetPropertyBlock(fadePropertyBlock);
        fadePropertyBlock.SetFloat(FadeAmountPropertyId, amount);
        spriteRenderer.SetPropertyBlock(fadePropertyBlock);
    }

    void ReleasePieceShaderFade()
    {
        shaderFadeActive = false;

        if (spriteRenderer != null &&
            fadeMaterialVariant != null &&
            spriteRenderer.sharedMaterial == fadeMaterialVariant)
        {
            spriteRenderer.sharedMaterial = originalSharedMaterial;
        }

        ReleaseFadeMaterialVariant(originalSharedMaterial, fadeMaterialVariant);

        fadeMaterialVariant = null;
        originalSharedMaterial = null;
        fadePropertyBlock = null;
    }

    void HandlePileSorting(bool force)
    {
        if (spriteRenderer != null)
        {
            // Airborne fragments sort at their landing depth; settled pieces
            // follow their actual Y so collisions can rearrange the pile.
            float depthY = hasSettled ? transform.position.y : floorY;
            if (!force && Mathf.Abs(depthY - lastSortedDepthY) < 0.005f)
            {
                return;
            }

            lastSortedDepthY = depthY;
            spriteRenderer.sortingOrder =
                Mathf.RoundToInt(-depthY * SortingUnitsPerWorldUnit) + sortingOrderJitter;

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

    void UpdateGroundedShadow()
    {
        if (shadowObj == null)
        {
            return;
        }

        shadowObj.transform.position = new Vector3(
            transform.position.x,
            transform.position.y,
            transform.position.z
        );
        shadowObj.transform.localScale = initialShadowScale;
    }

    void RetirePiece()
    {
        flightActive = false;

        ReleaseShadow();

        if (originalParent != null)
        {
            transform.SetParent(originalParent);
            transform.localPosition = initialLocalPosition;
            transform.localRotation = initialLocalRotation;
            transform.localScale = initialLocalScale;
        }

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
            spriteRenderer.sortingOrder = initialSortingOrder;
        }

        ReleasePieceShaderFade();

        // LaunchPiece removes this object from every destructible pool, so it
        // cannot be reused. Do not leave its components and sprite references
        // parked under the destructible for the rest of the location lifetime.
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        // Safety net: if the piece gets destroyed early (e.g. level ends, or hit by an explosion),
        // make sure we don't leave an orphaned shadow floating around.
        ReleaseShadow();
        ReleasePieceShaderFade();
    }

    void ReleaseShadow()
    {
        if (shadowObj == null)
        {
            shadowRenderer = null;
            return;
        }

        if (shadowRenderer == null)
        {
            shadowRenderer = shadowObj.GetComponent<SpriteRenderer>();
        }

        if (shadowRenderer != null)
        {
            // The shadow pool can outlive staged location content. Clear the
            // fragment reference so inactive pooled renderers do not retain its
            // sprite texture after the piece has been recycled.
            shadowRenderer.sprite = null;
            shadowRenderer.color = new Color(0f, 0f, 0f, 0.4f);
        }

        if (shadowPool != null)
        {
            shadowPool.Despawn(shadowObj);
        }
        else
        {
            Destroy(shadowObj);
        }

        shadowObj = null;
        shadowRenderer = null;
    }

}
