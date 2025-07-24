using System;
using System.Collections.Generic;

public static class MessageBus {
  private static Dictionary<string, Action<object>> _messageTable =
      new Dictionary<string, Action<object>>();

  public static Action On(string message, Action<object> callback) {
    if (_messageTable.ContainsKey(message)) {
      _messageTable[message] += callback;
    }
    else {
      _messageTable[message] = callback;
    }
    return () => Off(message, callback);
  }

  public static void Off(string message, Action<object> callback) {
    if (_messageTable.ContainsKey(message)) {
      _messageTable[message] -= callback;
      if (_messageTable[message] == null) {
        _messageTable.Remove(message);
      }
    }
  }

  public static void Send(string message, object data = null) {
    if (_messageTable.ContainsKey(message)) {
      _messageTable[message]?.Invoke(data);
    }
  }
}