using UnityEngine;
using System.Collections;

public class MaterialSmoothSwap : MonoBehaviour {
  [SerializeField] private Material material1;      // First material (initial)
  [SerializeField] private Material material2;      // Second material (target)
  [SerializeField] private float blendDuration = 1f; // Duration of the blend in seconds
  [SerializeField] private AnimationCurve blendCurve = AnimationCurve.Linear(0, 0, 1, 1);
  // ^ You can adjust this curve in Inspector for easing (Linear by default)

  private Renderer _renderer;
  private bool _isBlending = false;
  private bool _usingSecondMat = false;  // Tracks which material is currently active

  void Start() {
    _renderer = GetComponent<Renderer>();
    if (!_renderer) {
      Debug.LogError("MaterialSmoothSwap script needs a Renderer (SpriteRenderer/MeshRenderer) on the same GameObject.");
      return;
    }
    // Ensure the initial material is applied
    _renderer.material = material1;
    _usingSecondMat = false;

    // Subscribe to MessageBus event (if a MessageBus system exists)
    // MessageBus.On("swap", TriggerMaterialBlend);  // Example subscription
  }

  /// <summary>Triggers the smooth transition between material1 and material2.</summary>
  public void TriggerMaterialBlend() {
    if (_isBlending || material1 == null || material2 == null) return; // ignore if already blending or not set
                                                                       // Determine source and target based on current state:
    Material sourceMat = _usingSecondMat ? material2 : material1;
    Material targetMat = _usingSecondMat ? material1 : material2;
    // Start the blending coroutine
    StartCoroutine(BlendMaterialsRoutine(sourceMat, targetMat));
  }

  private IEnumerator BlendMaterialsRoutine(Material fromMat, Material toMat) {
    _isBlending = true;
    float elapsed = 0f;
    // Cache the material instance we're modifying (the renderer's own material instance)
    Material runtimeMat = _renderer.material;

    // Loop until blendDuration is reached
    while (elapsed < blendDuration) {
      elapsed += Time.deltaTime;
      float t = Mathf.Clamp01(elapsed / blendDuration);
      // Apply easing using the curve (if provided)
      float curveT = blendCurve.Evaluate(t);
      // Lerp the material properties from 'fromMat' to 'toMat'
      runtimeMat.Lerp(fromMat, toMat, curveT);
      yield return null;  // wait for next frame
    }

    // Ensure final state is exactly the target
    _renderer.material.Lerp(fromMat, toMat, 1f);
    // Update state
    _usingSecondMat = !_usingSecondMat;
    _isBlending = false;
  }
}
