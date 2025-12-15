using UnityEngine;

public static class TransformExtensions {
  public static Transform FindDirectChild(this Transform transform, string name) {
    for (int i = 0; i < transform.childCount; i++)
      if (transform.GetChild(i).name == name)
        return transform.GetChild(i);
    return null;
  }
}