using System.Collections.Generic;
using CustomInspector;
using UnityEngine;

public class DestructionManager : MonoBehaviour {
  [Button(nameof(LaunchRandom), label = "Play", size = Size.small)] public bool what;
  private Transform piecesRoot;
  public Vector2 planarForceMin = new(-1f, 3f);
  public Vector2 planarForceMax = new(1f, 5f);
  public float torqueMin = -1;
  public float torqueMax = 1;
  public List<Piece> pieces = new();

  void Awake() {
    piecesRoot = transform.FindDirectChild("PIECES");
    CollectPiecesFromChildren();
  }

  public void CollectPiecesFromChildren() {
    pieces.Clear();
    var found = piecesRoot.GetComponentsInChildren<Piece>(true);
    for (var i = 0; i < found.Length; i++) {
      pieces.Add(found[i]);
    }
  }

  public void LaunchRandom() {
    if (pieces.Count == 0) return;
    var active = new List<Piece>();
    for (var i = 0; i < pieces.Count; i++) {
      pieces[i].gameObject.SetActive(false);
      active.Add(pieces[i]);
    }
    var count = Random.Range(1, piecesRoot.childCount);
    Shuffle(active);
    for (var i = 0; i < active.Count; i++) {
      active[i].ResetPiece();
    }
    for (var i = 0; i < count; i++) {
      var p = active[i];
      p.gameObject.SetActive(true);
      var f = new Vector2(Random.Range(planarForceMin.x, planarForceMax.x), Random.Range(planarForceMin.y, planarForceMax.y));
      var t = Random.Range(torqueMin, torqueMax);
      p.Launch(f, t);
    }
  }

  static void Shuffle<T>(List<T> list) {
    for (var i = list.Count - 1; i > 0; i--) {
      var j = Random.Range(0, i + 1);
      (list[i], list[j]) = (list[j], list[i]);
    }
  }
}
