using UnityEngine;

public static class ObjectEntityId {
  public static ulong GetRawValue(Object obj) {
    if (obj == null) return 0UL;
#if UNITY_6000_3_OR_NEWER
    return EntityId.ToULong(obj.GetEntityId());
#else
#pragma warning disable CS0618
    return unchecked((ulong)(uint)obj.GetInstanceID());
#pragma warning restore CS0618
#endif
  }

  public static string GetString(Object obj) {
    return GetRawValue(obj).ToString();
  }

  public static int GetModulo(Object obj, int modulo) {
    if (obj == null || modulo <= 0) return 0;
    return (int)(GetRawValue(obj) % (ulong)modulo);
  }
}
