using UnityEngine;

[RequireComponent(typeof(FontText))]
public class FPSCounter : MonoBehaviour
{
    private const float DisplayRefreshInterval = 0.25f;

    private FontText fontText;
    private float deltaTime = 0.0f;
    private float nextDisplayRefreshAt;
    private int displayedFps = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Initialize()
    {
        var camera = Camera.main;
        if (camera != null)
        {
            var fpsObj = new GameObject("FPSCounter");
            fpsObj.transform.SetParent(camera.transform, false);
            fpsObj.transform.localScale = new Vector3(0.1f, 0.1f, 1f);
            
            var fontText = fpsObj.AddComponent<FontText>();
            fontText.font = "Hand";
            fontText.justifyX = "center";
            fontText.justifyY = "top";

            // Configure SpriteRenderer for FontText to sync to its glyphs
            var spriteRenderer = fpsObj.AddComponent<SpriteRenderer>();
            spriteRenderer.sortingLayerName = "MyUI2";
            spriteRenderer.sortingOrder = 1000;

            // Load the character prefab from Resources so it works in standalone builds
            fontText.characterPrefab = Resources.Load<GameObject>("Fonts/FontCharacter");
            
            fpsObj.AddComponent<FPSCounter>();
        }
    }

    void Start()
    {
        fontText = GetComponent<FontText>();

        // Position it at the top center of the camera
        Camera cam = GetComponentInParent<Camera>();
        if (cam == null) cam = Camera.main;

        if (cam != null)
        {
            if (cam.orthographic)
            {
                float orthoSize = cam.orthographicSize;
                // Move it near the top edge
                transform.localPosition = new Vector3(0, orthoSize - 0.5f, 10f);
            }
            else
            {
                // Simple perspective approximation
                transform.localPosition = new Vector3(0, 4f, 10f);
            }
        }
    }

    void Update()
    {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        if (fontText == null ||
            deltaTime <= 0.0f ||
            Time.unscaledTime < nextDisplayRefreshAt)
        {
            return;
        }

        nextDisplayRefreshAt = Time.unscaledTime + DisplayRefreshInterval;
        int currentFps = Mathf.Max(0, Mathf.CeilToInt(1.0f / deltaTime));
        if (currentFps == displayedFps) return;

        displayedFps = currentFps;
        fontText.content = IntegerTextCache.Get(currentFps);
    }
}
