#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ContentPackSelection", menuName = "Content Packs/Selection")]
public sealed class ContentPackSelection : ScriptableObject {
  [SerializeField] bool externalContentEnabled = true;
  [SerializeField] string externalRoot = ContentPackPipeline.DefaultExternalRoot;
  [SerializeField] List<string> activePackIds = new();

  public bool ExternalContentEnabled => externalContentEnabled;
  public string ExternalRoot => string.IsNullOrWhiteSpace(externalRoot) ? ContentPackPipeline.DefaultExternalRoot : externalRoot.Trim();
  public IReadOnlyList<string> ActivePackIds => activePackIds;

  public bool EnsureDefaults() {
    var changed = false;
    if (string.IsNullOrWhiteSpace(externalRoot)) {
      externalRoot = ContentPackPipeline.DefaultExternalRoot;
      changed = true;
    }

    if (activePackIds == null) {
      activePackIds = new List<string>();
      changed = true;
    }

    var normalizedPackIds = new List<string>(activePackIds.Count);
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < activePackIds.Count; i++) {
      var normalized = NormalizePackId(activePackIds[i]);
      if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized)) continue;
      normalizedPackIds.Add(normalized);
    }

    if (normalizedPackIds.Count != activePackIds.Count) {
      activePackIds.Clear();
      activePackIds.AddRange(normalizedPackIds);
      changed = true;
    }
    else {
      for (var i = 0; i < normalizedPackIds.Count; i++) {
        if (string.Equals(activePackIds[i], normalizedPackIds[i], StringComparison.Ordinal)) continue;
        activePackIds[i] = normalizedPackIds[i];
        changed = true;
      }
    }

    return changed;
  }

  public bool SetActivePackIds(IEnumerable<string> packIds) {
    var previousPackIds = activePackIds != null
      ? new List<string>(activePackIds)
      : new List<string>();
    if (activePackIds == null) {
      activePackIds = new List<string>();
    }

    activePackIds.Clear();
    if (packIds != null) {
      foreach (var packId in packIds) {
        var normalized = NormalizePackId(packId);
        if (string.IsNullOrWhiteSpace(normalized)) {
          continue;
        }
        activePackIds.Add(normalized);
      }
    }

    var defaultsChanged = EnsureDefaults();
    return defaultsChanged || !ArePackIdListsEqual(previousPackIds, activePackIds);
  }

  public List<string> GetNormalizedActivePackIds() {
    EnsureDefaults();

    var result = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < activePackIds.Count; i++) {
      var normalized = NormalizePackId(activePackIds[i]);
      if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized)) continue;
      result.Add(normalized);
    }

    if (!externalContentEnabled) {
      result.Clear();
    }

    return result;
  }

  static string NormalizePackId(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  static bool ArePackIdListsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) {
    if (ReferenceEquals(left, right)) {
      return true;
    }
    if (left == null || right == null || left.Count != right.Count) {
      return false;
    }

    for (var i = 0; i < left.Count; i++) {
      if (!string.Equals(left[i], right[i], StringComparison.Ordinal)) {
        return false;
      }
    }

    return true;
  }
}
#endif
