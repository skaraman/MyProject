using System.Collections.Generic;
using UnityEngine;

public static class EnemyAudioLimiter {
  const int MaxAudioEnemies = 3;
  const float ProximityRefreshSeconds = 0.1f;

  static readonly HashSet<EnemyController> activeEnemies = new();
  static readonly HashSet<EnemyController> audioEligibleEnemies = new();
  static float nextRefreshTime;
  static Transform playerTransform;
  static Camera mainCamera;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    activeEnemies.Clear();
    audioEligibleEnemies.Clear();
    nextRefreshTime = 0f;
    playerTransform = null;
    mainCamera = null;
  }

  public static void Register(EnemyController enemy) {
    if (enemy == null) return;
    activeEnemies.Add(enemy);
  }

  public static void Unregister(EnemyController enemy) {
    if (enemy == null) return;
    activeEnemies.Remove(enemy);
    audioEligibleEnemies.Remove(enemy);
  }

  public static bool IsEligibleForAudio(EnemyController enemy) {
    if (enemy == null) return false;
    if (activeEnemies.Count <= MaxAudioEnemies) return true;
    UpdateProximityIfNeeded();
    return audioEligibleEnemies.Contains(enemy);
  }

  static void UpdateProximityIfNeeded() {
    var now = Time.unscaledTime;
    if (now < nextRefreshTime) return;
    nextRefreshTime = now + ProximityRefreshSeconds;

    ResolveReferenceTransforms();
    Vector3 referencePos = GetReferencePosition();

    audioEligibleEnemies.Clear();

    EnemyController e1 = null, e2 = null, e3 = null;
    float d1 = float.MaxValue, d2 = float.MaxValue, d3 = float.MaxValue;

    foreach (var enemy in activeEnemies) {
      if (enemy == null || !enemy.isActiveAndEnabled) continue;
      float distSq = (enemy.transform.position - referencePos).sqrMagnitude;
      if (distSq < d1) {
        d3 = d2; e3 = e2;
        d2 = d1; e2 = e1;
        d1 = distSq; e1 = enemy;
      } else if (distSq < d2) {
        d3 = d2; e3 = e2;
        d2 = distSq; e2 = enemy;
      } else if (distSq < d3) {
        d3 = distSq; e3 = enemy;
      }
    }

    if (e1 != null) audioEligibleEnemies.Add(e1);
    if (e2 != null) audioEligibleEnemies.Add(e2);
    if (e3 != null) audioEligibleEnemies.Add(e3);
  }

  static void ResolveReferenceTransforms() {
    if (playerTransform == null || !playerTransform.gameObject.activeInHierarchy) {
      var player = Object.FindAnyObjectByType<GearController>();
      if (player != null) playerTransform = player.transform;
    }
    if (mainCamera == null || !mainCamera.isActiveAndEnabled) {
      mainCamera = Camera.main;
    }
  }

  static Vector3 GetReferencePosition() {
    if (playerTransform != null) return playerTransform.position;
    if (mainCamera != null) return mainCamera.transform.position;
    return Vector3.zero;
  }
}
