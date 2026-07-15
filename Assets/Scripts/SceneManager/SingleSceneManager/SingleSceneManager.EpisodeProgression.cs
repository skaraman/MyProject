using UnityEngine;

public partial class SingleSceneManager {
  void OnEnemyDefeatedForEpisodeProgress(object payload) {
    var defeatedEvent = payload as EnemyDefeatedEvent;
    if (defeatedEvent == null) return;
    if (!ContentEpisodeProgression.TryAdvanceForEnemyDefeated(defeatedEvent, "enemy_defeated")) return;

    MessageBus.Send(ContentEpisodeProgression.ObjectivesCompletedTopic);
    RuntimeContentPackResolver.ConfigureForCurrentRuntimeState("episode_objective_complete");
    RefreshEpisodeProgressionLocation("objective_complete");
  }

  void AdvanceEpisodeSlice(string source) {
    if (!ContentEpisodeProgression.AdvanceToNextSlice(source)) return;

    RuntimeContentPackResolver.ConfigureForCurrentRuntimeState("episode_advance:" + (source ?? ""));
    RefreshEpisodeProgressionLocation(source);
  }

  void RefreshEpisodeProgressionLocation(string source) {
    var current = LocationEnemyData.NormalizeLocationId(LocationManager.currentLocation);
    if (!IsGameplayLocation(current)) return;

    RequestLocationLoad(current);
    if (!ShouldLogLoadFlowDebug()) return;

    RuntimeLog.Log(
      "[SingleSceneManager][EpisodeProgression] refreshed_location" +
      " source=" + ResolveLoadFlowValue(source) +
      " location=" + ResolveLoadFlowValue(current)
    );
  }
}
