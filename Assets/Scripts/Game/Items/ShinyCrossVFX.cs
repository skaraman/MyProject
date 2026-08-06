using UnityEngine;

public class ShinyCrossVFX : MonoBehaviour {
    [Header("Visual Settings")]
    public Color baseColor = new Color(1f, 0.95f, 0.6f, 1f);
    [Range(0f, 1f)] public float minOpacity = 0.5f;
    [Range(0f, 1f)] public float maxOpacity = 1.0f;
    
    [Header("Size Settings")]
    public float baseWidth = 0.8f;
    public float baseThickness = 0.15f;
    public float sizePulseAmount = 0.4f;
    
    [Header("Animation Settings")]
    public float pulseSpeed = 5f;
    public float rotationSpeed = 30f;

    [Header("Material")]
    public Material crossMaterial;

    private SpriteRenderer horizontal;
    private SpriteRenderer vertical;
    private float timeOffset;
    
    void Start() {
        timeOffset = Random.value * 10f;
        
        // Create a simple 1x1 white texture programmatically
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        
        // Fallback to Sprites/Default if no material is assigned in the inspector
        Material mat = crossMaterial != null ? crossMaterial : new Material(Shader.Find("Sprites/Default"));
        
        horizontal = CreateLine(sprite, mat, "HorizontalGlow");
        vertical = CreateLine(sprite, mat, "VerticalGlow");
    }
    
    SpriteRenderer CreateLine(Sprite sprite, Material mat, string name) {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.material = mat;
        
        // Explicitly set the layer and order as requested
        sr.sortingLayerName = "MyUI";
        sr.sortingOrder = 100;
        
        return sr;
    }
    
    void Update() {
        if (horizontal == null || vertical == null) return;
        
        float t = Time.time * pulseSpeed + timeOffset;
        float pulse = (Mathf.Sin(t) + 1f) * 0.5f; // 0 to 1
        
        float width = baseWidth + (pulse * sizePulseAmount);
        // The thickness gets slightly smaller as it stretches to look natural
        float thickness = baseThickness - (pulse * sizePulseAmount * 0.125f);
        
        horizontal.transform.localScale = new Vector3(width, thickness, 1f);
        vertical.transform.localScale = new Vector3(thickness, width, 1f);
        
        Color glowColor = baseColor;
        glowColor.a = Mathf.Lerp(minOpacity, maxOpacity, pulse);
        
        horizontal.color = glowColor;
        vertical.color = glowColor;
        
        // Slowly rotate the cross
        transform.Rotate(0, 0, rotationSpeed * Time.deltaTime);
    }
}
