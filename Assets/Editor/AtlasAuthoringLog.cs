#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

static class AtlasAuthoringLog {
  const string VerboseLoggingKey = "Esperanza.AtlasAuthoring.VerboseLogging";
  const int DefaultSampleLimit = 3;

  public static bool VerboseLoggingEnabled {
    get => EditorPrefs.GetBool(VerboseLoggingKey, false);
    set => EditorPrefs.SetBool(VerboseLoggingKey, value);
  }

  public static void Info(string message) {
    Debug.Log(message);
  }

  public static void Verbose(string message) {
    if (!VerboseLoggingEnabled) return;
    Debug.Log(message);
  }

  public static void Warning(string message) {
    Debug.LogWarning(message);
  }

  public static void VerboseWarning(string message) {
    if (!VerboseLoggingEnabled) return;
    Debug.LogWarning(message);
  }

  public static void FailureSummary(string prefix, List<string> failures, int sampleLimit = DefaultSampleLimit) {
    if (failures == null || failures.Count <= 0) return;

    if (VerboseLoggingEnabled) {
      for (var i = 0; i < failures.Count; i++) {
        Warning(prefix + " " + failures[i]);
      }
      return;
    }

    Warning(BuildSummaryMessage(prefix, "Failures recorded.", failures, sampleLimit));
  }

  public static void WarningWithSamples(string summary, List<string> samples, int sampleLimit = DefaultSampleLimit) {
    if (string.IsNullOrWhiteSpace(summary)) return;
    if (samples == null || samples.Count <= 0) {
      Warning(summary);
      return;
    }

    if (VerboseLoggingEnabled) {
      Warning(summary + "\n" + string.Join("\n", samples));
      return;
    }

    Warning(BuildSummaryMessage(summary, "", samples, sampleLimit));
  }

  static string BuildSummaryMessage(string prefix, string summary, List<string> samples, int sampleLimit) {
    var builder = new StringBuilder();
    builder.Append(prefix);
    if (!string.IsNullOrWhiteSpace(summary)) {
      builder.Append(' ');
      builder.Append(summary);
    }

    var sampledCount = Mathf.Clamp(sampleLimit, 0, samples?.Count ?? 0);
    var suppressedCount = (samples?.Count ?? 0) - sampledCount;
    builder.Append(" count=");
    builder.Append(samples?.Count ?? 0);
    builder.Append(" sampled=");
    builder.Append(sampledCount);
    builder.Append(" suppressed=");
    builder.Append(Mathf.Max(0, suppressedCount));

    for (var i = 0; i < sampledCount; i++) {
      builder.Append('\n');
      builder.Append(samples[i]);
    }

    return builder.ToString();
  }
}
#endif
