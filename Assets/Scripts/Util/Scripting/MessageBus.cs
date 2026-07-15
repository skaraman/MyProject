using System;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

public readonly struct MessageTopic {
  internal string Name { get; }

  public MessageTopic(string name) {
    Name = name;
  }
}

public readonly struct MessageTopic<T> {
  internal string Name { get; }

  public MessageTopic(string name) {
    Name = name;
  }
}

public static class CharacterMessageTopics {
  public static readonly MessageTopic LoadGame = new("loadGame");
  public static readonly MessageTopic<string> DialogStateReady = new("dialogStateReady");
  public static readonly MessageTopic<string> FormChanged = new("formChanged");
  public static readonly MessageTopic<string> GearReady = new("gearReady");
  public static readonly MessageTopic<string> FormProgressChanged = new("formProgressChanged");
  public static readonly MessageTopic<string> FormStatsChanged = new("formStatsChanged");
  public static readonly MessageTopic<string> AbilityProgressChanged = new("abilityProgressChanged");
  public static readonly MessageTopic<string> AbilityLoadoutChanged = new("abilityLoadoutChanged");
}

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

  public static Action On(MessageTopic topic, Action callback) {
    if (callback == null) {
      throw new ArgumentNullException(nameof(callback));
    }

    return On(topic.Name, _ => callback());
  }

  public static Action On<T>(MessageTopic<T> topic, Action<T> callback) {
    if (callback == null) {
      throw new ArgumentNullException(nameof(callback));
    }

    return On(topic.Name, payload => DispatchTypedPayload(topic.Name, payload, callback));
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

  public static void Send(MessageTopic topic) {
    Send(topic.Name);
  }

  public static void Send<T>(MessageTopic<T> topic, T data) {
    Send(topic.Name, data);
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

  static void DispatchTypedPayload<T>(string message, object payload, Action<T> callback) {
    if (payload == null) {
      callback(default);
      return;
    }

    if (payload is T typedPayload) {
      callback(typedPayload);
      return;
    }

    Debug.LogError(
      "[MessageBus] Invalid payload type message='" + message +
      "' expected='" + typeof(T).Name +
      "' actual='" + payload.GetType().Name + "'"
    );
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
