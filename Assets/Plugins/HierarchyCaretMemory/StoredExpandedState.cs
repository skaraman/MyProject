#if UNITY_EDITOR
using System;

public static partial class HierarchyCaretMemory
{
  [Serializable]
  private sealed class StoredExpandedState
  {
    public string[] expandedGlobalObjectIds = Array.Empty<string>();
  }
}
#endif
