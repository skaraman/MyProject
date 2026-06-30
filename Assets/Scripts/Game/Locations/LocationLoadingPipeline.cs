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
    if (SpriteStreamingLoadingState.IsProtectedLoadingOverlayActive) {
      Debug.Log($"[LocationLoadingPipeline] Deferring load request for '{resolvedId}' because protected loading overlay is active.");
      return;
    }

    var archetypes = BuildArchetypePrefabsByType(resolvedId);

    // Filter out null prefabs
    var realArchetypes = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in archetypes) {
      if (pair.Value != null) {
        realArchetypes[pair.Key] = pair.Value;
      }
    }

    if (realArchetypes.Count <= 0) {
      Debug.LogWarning($"[LocationLoadingPipeline] No valid archetype prefabs resolved for location '{resolvedId}'. Skipping warm gate.");
      return;
    }

    var orchestrator = StreamingWarmOrchestrator.Instance;
    if (orchestrator != null && StreamingWarmOrchestrator.IsWarmGateRunning) {
      var progressContext = "-";
      if (StreamingWarmOrchestrator.TryGetActiveProgress(out var progress)) {
        progressContext = "'" + progress.context + "'";
      }
      Debug.LogWarning(
        $"[LocationLoadingPipeline] Warm orchestrator already running, deferring enemy wave warmup for '{resolvedId}'. " +
        $"context={progressContext}, " +
        $"queued={TextureResidencyCache.GetQueueSnapshot().queuedCount}, in_flight={TextureResidencyCache.GetQueueSnapshot().inFlightCount}."
      );
      return;
    }

    StreamingWarmOrchestrator.Instance.Run(
      WarmRequest.CreateEnemyWaveSpawn(realArchetypes),
      _ => {}
    );
  }

  Dictionary<string, GameObject> BuildArchetypePrefabsByType(string locationId) {
    var map = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
    
    // First, try to resolve from active Spawner in the scene if available
    var spawner = FindAnyObjectByType<Spawner>();
    if (spawner != null) {
      var spawnerMap = spawner.BuildCurrentLocationArchetypeMapForWarmup();
      if (spawnerMap != null) {
        foreach (var pair in spawnerMap) {
          if (pair.Value != null) {
            map[pair.Key] = pair.Value;
          }
        }
      }
    }



    Debug.Log($"[LocationLoadingPipeline] Built archetypes for location '{locationId}': count={map.Count}", this);
    return map;
  }

  static string NormalizeToken(string token) {
    if (string.IsNullOrEmpty(token)) return "";
    var needsCleaning = false;
    for (var i = 0; i < token.Length; i++) {
      var c = token[i];
      if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || char.IsWhiteSpace(c))) {
        needsCleaning = true;
        break;
      }
    }

    if (!needsCleaning) {
      return token.Trim();
    }

    var chars = new char[token.Length];
    for (var i = 0; i < token.Length; i++) {
      var c = token[i];
      if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || char.IsWhiteSpace(c)) {
        chars[i] = c;
      }
      else {
        chars[i] = ' ';
      }
    }
    return new string(chars).Trim();
  }
}
