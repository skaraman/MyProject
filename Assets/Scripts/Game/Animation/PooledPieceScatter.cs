using System.Collections.Generic;
using UnityEngine;

public sealed class PooledPieceScatter : MonoBehaviour {
  const float MinimumHorizontalScatterImpulse = 1.5f;

  readonly List<Piece> pieces = new();
  readonly List<Piece> launchScratch = new();

  Vector2 planarForceMin;
  Vector2 planarForceMax;
  float torqueMin;
  float torqueMax;
  bool launchPending;

  public bool Launch(
    Vector2 forceMin,
    Vector2 forceMax,
    float minimumTorque,
    float maximumTorque
  ) {
    pieces.Clear();
    GetComponentsInChildren<Piece>(true, pieces);
    if (pieces.Count == 0) {
      enabled = false;
      return false;
    }

    planarForceMin = forceMin;
    planarForceMax = forceMax;
    torqueMin = minimumTorque;
    torqueMax = maximumTorque;

    if (Time.inFixedTimeStep) {
      // The effect root is independent from the enemy, so it can safely wait
      // until Update even when the enemy is returned to its pool immediately.
      launchPending = true;
      enabled = true;
      return true;
    }

    LaunchNow();
    return true;
  }

  void Update() {
    if (!launchPending) {
      return;
    }

    LaunchNow();
  }

  void OnDisable() {
    launchPending = false;
  }

  void LaunchNow() {
    launchPending = false;
    launchScratch.Clear();
    launchScratch.AddRange(pieces);
    Shuffle(launchScratch);

    var count = Random.Range(1, launchScratch.Count + 1);
    for (var i = 0; i < launchScratch.Count; i++) {
      var piece = launchScratch[i];
      if (piece == null) continue;
      var shouldLaunch = i < count;
      if (shouldLaunch) {
        if (!piece.gameObject.activeSelf) piece.gameObject.SetActive(true);
        piece.ResetPiece();
        var horizontalForce = Random.Range(planarForceMin.x, planarForceMax.x);
        if (planarForceMin.x < 0f &&
            planarForceMax.x > 0f &&
            Mathf.Abs(horizontalForce) < MinimumHorizontalScatterImpulse) {
          var direction = Random.value < 0.5f ? -1f : 1f;
          var availableMagnitude = direction < 0f
            ? -planarForceMin.x
            : planarForceMax.x;
          horizontalForce =
            direction * Mathf.Min(MinimumHorizontalScatterImpulse, availableMagnitude);
        }
        var force = new Vector2(
          horizontalForce,
          Random.Range(planarForceMin.y, planarForceMax.y)
        );
        var torque = Random.Range(torqueMin, torqueMax);
        piece.Launch(force, torque);
      }
      else if (piece.gameObject.activeSelf) {
        piece.gameObject.SetActive(false);
        piece.ResetPiece();
      }
    }

    enabled = false;
  }

  static void Shuffle<T>(List<T> list) {
    for (var i = list.Count - 1; i > 0; i--) {
      var j = Random.Range(0, i + 1);
      (list[i], list[j]) = (list[j], list[i]);
    }
  }
}
