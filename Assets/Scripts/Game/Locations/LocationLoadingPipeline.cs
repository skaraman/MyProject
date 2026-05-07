using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles runtime enemy loading pipeline for locations.
/// Resolves archetype prefabs and triggers warm streaming requests.
/// </summary>
public class LocationLoadingPipeline : MonoBehaviour {
  [SerializeField] Transform locationRoot;

  public void RequestLoad(string resolvedId) {
    var archetypes = BuildArchetypePrefabsByType(resolvedId);
    if (archetypes.Count <= 0) return;

    StreamingWarmOrchestrator.Instance.Run(
      WarmRequest.CreateEnemyWaveSpawn(archetypes),
      _ => {}
    );
  }

  Dictionary<string, GameObject> BuildArchetypePrefabsByType(string locationId) {
    var map = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
    if (!LocationEnemyData.TryGetLocation(locationId, out var info)) return map;
    if (info.enemies == null || info.enemies.Count <= 0) return map;

    foreach (var enemyName in info.enemies) {
      var normalized = NormalizeToken(enemyName);
      map[normalized] = null;
    }

    Debug.Log($"[LocationLoadingPipeline] Built archetypes for location '{locationId}': count={map.Count}", this);
    return map;
  }

  static string NormalizeToken(string token) {
    var clean = System.Text.RegularExpressions.Regex.Replace(token, "[^a-zA-Z0-9_\\s]", " ");
    if (string.IsNullOrEmpty(clean)) return "";
    return clean.Trim();
  }
}
