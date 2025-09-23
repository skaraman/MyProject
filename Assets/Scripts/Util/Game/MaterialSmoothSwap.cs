using UnityEngine;
using System.Collections;

public class MaterialSmoothSwap : MonoBehaviour {
  [SerializeField] private Material material1;
  [SerializeField] private Material material2;
  [SerializeField] private float blendDuration = 1f; 
  [SerializeField] private AnimationCurve blendCurve = AnimationCurve.Linear(0, 0, 1, 1);

  private Renderer _renderer;
  private bool _isBlending = false;
  private bool _usingSecondMat = false;

  void Start() {
    _renderer = GetComponent<Renderer>();
    _renderer.material = material1;
    _usingSecondMat = false;

    // MessageBus.On("swap", TriggerMaterialBlend);  // Example subscription
  }

  public void TriggerMaterialBlend() {
    if (_isBlending || material1 == null || material2 == null) return; 
                                                                      
    Material sourceMat = _usingSecondMat ? material2 : material1;
    Material targetMat = _usingSecondMat ? material1 : material2;
    StartCoroutine(BlendMaterialsRoutine(sourceMat, targetMat));
  }

  private IEnumerator BlendMaterialsRoutine(Material fromMat, Material toMat) {
    _isBlending = true;
    float elapsed = 0f;
    Material runtimeMat = _renderer.material;
    while (elapsed < blendDuration) {
      elapsed += Time.deltaTime;
      float t = Mathf.Clamp01(elapsed / blendDuration);
      float curveT = blendCurve.Evaluate(t);
      runtimeMat.Lerp(fromMat, toMat, curveT);
      yield return null; 
    }

    _renderer.material.Lerp(fromMat, toMat, 1f);
    _usingSecondMat = !_usingSecondMat;
    _isBlending = false;
  }
}
