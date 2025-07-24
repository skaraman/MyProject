using UnityEngine;
using System.Collections.Generic;

public class ReferenceListAllIn1AnimatorInspector : MonoBehaviour {
  public List<AllIn1AnimatorInspector> references = new();

  public AllIn1AnimatorInspector Get(int index) {
    if (index < 0 || index >= references.Count) return null;
    return references[index];
  }
}
