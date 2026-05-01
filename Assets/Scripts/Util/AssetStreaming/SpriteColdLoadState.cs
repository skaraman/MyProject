public enum SpriteColdLoadState {
  Ready = 0,
  Pending = 1,
  Missing = 2,
  ExplicitEmpty = 3
}

public static class SpriteColdLoadStateUtility {
  public static bool IsCommitReady(this SpriteColdLoadState state) {
    return state == SpriteColdLoadState.Ready || state == SpriteColdLoadState.ExplicitEmpty;
  }
}
