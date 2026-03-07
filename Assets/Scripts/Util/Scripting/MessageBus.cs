using System;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

public static class MessageBus {
  private static Dictionary<string, Action<object>> _messageTable =
      new Dictionary<string, Action<object>>();

  // Track registered keys for efficient cleanup during scene transitions
  private static HashSet<string> registeredKeys = new HashSet<string>();
  public static bool enableSlowSubscriberDiagnostics = false;
  public static float slowSubscriberThresholdMs = 50f;

  public static Action On(string message, Action<object> callback) {
    if (_messageTable.TryGetValue(message, out var handlers)) {
      _messageTable[message] = handlers + callback;
    } else {
      _messageTable[message] = callback;
      registeredKeys.Add(message);
    }
    return () => Off(message, callback);
  }

  public static void Off(string message, Action<object> callback) {
    if (!_messageTable.TryGetValue(message, out var handlers) || handlers == null) return;
    handlers -= callback;
    if (handlers == null) {
      _messageTable.Remove(message);
      registeredKeys.Remove(message);
      return;
    }
    _messageTable[message] = handlers;
  }

  public static void Send(string message, object data = null) {
    if (!_messageTable.TryGetValue(message, out var action) || action == null) return;

    if (!ShouldLogSlowSubscriberDiagnostics()) {
      action.Invoke(data);
      return;
    }

    var invocationList = action.GetInvocationList();
    var thresholdMs = ResolveSlowSubscriberThresholdMs();
    var sendStartedAt = Stopwatch.GetTimestamp();
    var slowSubscribers = 0;

    for (var i = 0; i < invocationList.Length; i++) {
      var callback = invocationList[i] as Action<object>;
      if (callback == null) continue;

      var callbackStartedAt = Stopwatch.GetTimestamp();
      callback(data);
      var elapsedMs = ComputeElapsedMs(callbackStartedAt);
      if (elapsedMs < thresholdMs) continue;
      slowSubscribers++;

      UnityEngine.Debug.LogWarning(
        "[MessageBus][Diag] Slow subscriber message='" + message +
        "' callback='" + DescribeCallback(callback) +
        "' ms=" + elapsedMs.ToString("0.0")
      );
    }

    var sendElapsedMs = ComputeElapsedMs(sendStartedAt);
    if (sendElapsedMs < thresholdMs) return;

    UnityEngine.Debug.LogWarning(
      "[MessageBus][Diag] Slow dispatch message='" + message +
      "' ms=" + sendElapsedMs.ToString("0.0") +
      " subscribers=" + invocationList.Length +
      " slow_subscribers=" + slowSubscribers
    );
  }

  // Optional: Clear all messages (useful for scene transitions)
  public static void Clear() {
    _messageTable.Clear();
    registeredKeys.Clear();
  }

  static bool ShouldLogSlowSubscriberDiagnostics() {
    if (!enableSlowSubscriberDiagnostics) return false;
    return Application.isEditor || Debug.isDebugBuild;
  }

  static float ResolveSlowSubscriberThresholdMs() {
    return Mathf.Max(slowSubscriberThresholdMs, 1f);
  }

  static float ComputeElapsedMs(long startedAtTicks) {
    var deltaTicks = Stopwatch.GetTimestamp() - startedAtTicks;
    return Mathf.Max((float)(deltaTicks * 1000.0 / Stopwatch.Frequency), 0f);
  }

  static string DescribeCallback(Action<object> callback) {
    if (callback == null) return "(null)";
    var methodName = callback.Method != null ? callback.Method.Name : "(unknown)";
    var targetType = callback.Target != null ? callback.Target.GetType().Name : "(static)";
    return targetType + "." + methodName;
  }
}
