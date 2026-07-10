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
  private bool launchPending;

  void Awake() {
    piecesRoot = transform.FindDirectChild("PIECES");
    CollectPiecesFromChildren();
  }

  void Update() {
    if (!launchPending) return;
    launchPending = false;
    LaunchRandomInternal();
  }

  public void CollectPiecesFromChildren() {
    pieces.Clear();
    if (piecesRoot == null) {
      return;
    }

    var found = piecesRoot.GetComponentsInChildren<Piece>(true);
    for (var i = 0; i < found.Length; i++) {
      pieces.Add(found[i]);
    }
  }

  public void LaunchRandom() {
    if (pieces.Count == 0) return;
    if (Time.inFixedTimeStep) {
      // Avoid enabling/launching bodies mid-physics step.
      launchPending = true;
      return;
    }
    LaunchRandomInternal();
  }

  void LaunchRandomInternal() {
    if (pieces.Count == 0) return;
    var active = new List<Piece>(pieces);
    Shuffle(active);
    var count = Random.Range(1, active.Count + 1);
    for (var i = 0; i < active.Count; i++) {
      var p = active[i];
      if (p == null) continue;
      var shouldLaunch = i < count;
      if (shouldLaunch) {
        if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
        p.ResetPiece();
        var f = new Vector2(Random.Range(planarForceMin.x, planarForceMax.x), Random.Range(planarForceMin.y, planarForceMax.y));
        var t = Random.Range(torqueMin, torqueMax);
        p.Launch(f, t);
      }
      else if (p.gameObject.activeSelf) {
        p.gameObject.SetActive(false);
        p.ResetPiece();
      }
    }
  }

  static void Shuffle<T>(List<T> list) {
    for (var i = list.Count - 1; i > 0; i--) {
      var j = Random.Range(0, i + 1);
      (list[i], list[j]) = (list[j], list[i]);
    }
  }
}
