using UnityEngine;

[DisallowMultipleComponent]
public class SceneTimeScaleTarget : MonoBehaviour {
  [Min(0)] public int layerIndex = 1;

  public int LayerIndex => Mathf.Max(layerIndex, 0);

  void OnValidate() {
    if (layerIndex < 0) {
      layerIndex = 0;
    }
  }
}
