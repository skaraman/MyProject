using System.Collections.Generic;
using UnityEngine;

public class ReferenceListGameObject : MonoBehaviour {
  public List<GameObject> references = new();

  public GameObject Get(int index) {
    if (index < 0 || index >= references.Count) return null;
    return references[index];
  }
}
