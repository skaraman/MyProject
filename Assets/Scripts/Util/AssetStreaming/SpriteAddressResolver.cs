using UnityEngine;

public static class SpriteAddressResolver {
  public static bool TryResolve(SpriteLookupKey key, out SpriteAddressPair pair) {
#if UNITY_EDITOR
    if (!Application.isPlaying) {
      return SpriteRuntimeResolver.TryResolveEditor(key, out pair);
    }
#endif
    return SpriteRuntimeResolver.TryResolve(key, out pair);
  }

  public static bool IsLookupPending(SpriteLookupKey key) {
#if UNITY_EDITOR
    if (!Application.isPlaying) return false;
#endif
    return SpriteRuntimeResolver.IsLookupPending(key);
  }

  public static string NormalizeNamePart(string value) {
    return SpriteRuntimeResolver.NormalizeNamePart(value);
  }

#if UNITY_EDITOR
  public static bool TryLoadEditorSprite(string address, out Sprite sprite) {
    return SpriteRuntimeResolver.TryLoadEditorSprite(address, out sprite);
  }
#endif
}
