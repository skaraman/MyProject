using System.Collections.Generic;
using UnityEngine;

public sealed class ObjectiveListView : MonoBehaviour {
  [SerializeField] GameObject objectivePrefab;
  [SerializeField, Min(1)] int prewarmCapacity = 8;
  [SerializeField] Vector3 firstLocalPosition = new(17.49f, 5.25f, 0f);
  [SerializeField] Vector3 entrySpacing = new(0f, -1.5f, 0f);

  readonly List<GameObject> entries = new(8);
  int observedRegistryVersion = -1;
  int observedProgressRevision = -1;
  int observedSaveSlot = -1;

  void OnEnable() {
    ResolveEntries();
    EnsureEntryCount(prewarmCapacity);
    Refresh();
  }

  void Update() {
    var registryVersion = ActiveContentRegistryRuntime.ReloadVersion;
    var progressRevision = ContentEpisodeProgression.EpisodeRevision;
    var saveSlot = SaveSlotManager.slot;
    if (observedRegistryVersion == registryVersion &&
        observedProgressRevision == progressRevision &&
        observedSaveSlot == saveSlot) {
      return;
    }
    Refresh();
  }

  void Refresh() {
    observedRegistryVersion = ActiveContentRegistryRuntime.ReloadVersion;
    observedProgressRevision = ContentEpisodeProgression.EpisodeRevision;
    observedSaveSlot = SaveSlotManager.slot;

    if (objectivePrefab == null) return;

    var objectives = ContentEpisodeProgression.ResolveCurrentEpisodeObjectives();
    EnsureEntryCount(Mathf.Max(objectives.Count, prewarmCapacity));
    for (var i = 0; i < objectives.Count; i++) {
      var objective = objectives[i];
      PopulateEntry(
        entries[i],
        objective,
        ContentEpisodeProgression.IsCurrentEpisodeObjectiveComplete(objective)
      );
    }
    for (var i = objectives.Count; i < entries.Count; i++) {
      if (entries[i] != null && entries[i].activeSelf) {
        entries[i].SetActive(false);
      }
    }
  }

  void ResolveEntries() {
    entries.Clear();
    for (var i = 0; i < transform.childCount; i++) {
      entries.Add(transform.GetChild(i).gameObject);
    }
  }

  void EnsureEntryCount(int requiredCount) {
    if (objectivePrefab == null) return;

    while (entries.Count < requiredCount) {
      var index = entries.Count;
      var entry = Instantiate(objectivePrefab, transform, false);
      entry.name = "Objective_" + (index + 1);
      entry.transform.localPosition = firstLocalPosition + entrySpacing * index;
      entry.SetActive(false);
      entries.Add(entry);
    }
  }

  static void PopulateEntry(
    GameObject entry,
    ContentObjectiveDefinition objective,
    bool isComplete
  ) {
    if (entry == null) return;
    if (objective == null) {
      entry.SetActive(false);
      return;
    }

    var text = entry.GetComponentInChildren<FontText>(includeInactive: true);
    if (text != null) {
      var content = isComplete ? "Complete!" : objective.description ?? "";
      var contentChanged = text.content != content;
      text.content = content;
      if (entry.activeSelf && contentChanged) {
        text.Generate();
      }
    }
    if (!entry.activeSelf) {
      entry.SetActive(true);
    }
  }
}
