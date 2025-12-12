using System;
using System.Collections.Generic;

public static class MessageBus {
  private static Dictionary<string, Action<object>> _messageTable =
      new Dictionary<string, Action<object>>();

  // Track registered keys for efficient cleanup during scene transitions
  private static HashSet<string> registeredKeys = new HashSet<string>();

  public static Action On(string message, Action<object> callback) {
    if (_messageTable.ContainsKey(message)) {
      _messageTable[message] += callback;
    }
    else {
      _messageTable[message] = callback;
      registeredKeys.Add(message);
    }
    return () => Off(message, callback);
  }

  public static void Off(string message, Action<object> callback) {
    if (_messageTable.ContainsKey(message)) {
      _messageTable[message] -= callback;
      if (_messageTable[message] == null) {
        _messageTable.Remove(message);
        registeredKeys.Remove(message);
      }
    }
  }

  public static void Send(string message, object data = null) {
    if (_messageTable.TryGetValue(message, out var action)) {
      action?.Invoke(data);
    }
  }

  // Optional: Clear all messages (useful for scene transitions)
  public static void Clear() {
    _messageTable.Clear();
    registeredKeys.Clear();
  }
}