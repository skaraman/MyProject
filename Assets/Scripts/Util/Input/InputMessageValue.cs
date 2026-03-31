using UnityEngine;

public static class InputMessageValue {
  // InputProcessor normalizes new Input System callbacks into primitive payloads for MessageBus.
  public static bool IsPressed(object payload, bool defaultWhenNull = true) {
    if (payload == null) return defaultWhenNull;
    if (payload is bool b) return b;
    if (payload is float f) return f > 0.5f;
    if (payload is double d) return d > 0.5d;
    if (payload is int i) return i != 0;
    if (payload is Vector2 v) return v.sqrMagnitude > 0.25f;
    if (payload is Vector3 v3) return v3.sqrMagnitude > 0.25f;
    return false;
  }

  public static float CoerceFloat(object payload) {
    if (payload is float f) return f;
    if (payload is double d) return (float)d;
    if (payload is int i) return i;
    if (payload is bool b) return b ? 1f : 0f;
    return 0f;
  }
}
