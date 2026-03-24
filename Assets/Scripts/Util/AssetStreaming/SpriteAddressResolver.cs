using UnityEngine;
using Object = UnityEngine.Object;

public static class SpriteAddressResolver {
  public static bool TryResolve(SpriteLookupKey key, out SpriteAddressPair pair, Object logContext = null) {
#if UNITY_EDITOR
    if (!Application.isPlaying) {
      return SpriteRuntimeResolver.TryResolveEditor(key, out pair, logContext);
    }
#endif
    return SpriteRuntimeResolver.TryResolve(key, out pair, logContext);
  }

  public static bool IsLookupPending(SpriteLookupKey key, Object logContext = null) {
#if UNITY_EDITOR
    if (!Application.isPlaying) return false;
#endif
    return SpriteRuntimeResolver.IsLookupPending(key, logContext);
  }

  public static void InvalidateLookup(SpriteLookupKey key, bool reloadShard = false) {
#if UNITY_EDITOR
    if (!Application.isPlaying) return;
#endif
    SpriteRuntimeResolver.InvalidateLookup(key, reloadShard);
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
