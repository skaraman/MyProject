using UnityEngine;

public static class AudioClipResolver {
  public static bool TryLoadEditorClip(string address, out AudioClip clip) {
    clip = null;
#if UNITY_EDITOR
    if (string.IsNullOrWhiteSpace(address)) {
      return false;
    }

    var assetPath = address.Replace("\\", "/").Trim();
    clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
    return clip != null;
#else
    return false;
#endif
  }
}
