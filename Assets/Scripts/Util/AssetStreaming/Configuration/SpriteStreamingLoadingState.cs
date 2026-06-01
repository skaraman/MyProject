using UnityEngine;

public static class SpriteStreamingLoadingState {
  static int activeOverlayCount;
  static int protectedOverlayCount;
  static string activeReason = "";

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetOnDomainReload() {
    activeOverlayCount = 0;
    protectedOverlayCount = 0;
    activeReason = "";
  }

  public static bool IsLoadingOverlayActive => activeOverlayCount > 0;
  public static bool IsProtectedLoadingOverlayActive => protectedOverlayCount > 0;
  public static string ActiveReason => activeReason;

  public static void BeginLoadingOverlay(string reason) {
    activeOverlayCount = Mathf.Max(activeOverlayCount + 1, 0);
    protectedOverlayCount = Mathf.Max(protectedOverlayCount + 1, 0);
    activeReason = string.IsNullOrWhiteSpace(reason) ? activeReason : reason.Trim();
  }

  public static void ReleaseOverlayProtection() {
    if (protectedOverlayCount > 0) {
      protectedOverlayCount--;
    }
    if (protectedOverlayCount > activeOverlayCount) {
      protectedOverlayCount = activeOverlayCount;
    }
  }

  public static void EndLoadingOverlay(string reason = null) {
    if (activeOverlayCount > 0) {
      activeOverlayCount--;
    }
    if (protectedOverlayCount > activeOverlayCount) {
      protectedOverlayCount = activeOverlayCount;
    }
    if (activeOverlayCount <= 0) {
      activeOverlayCount = 0;
      protectedOverlayCount = 0;
      activeReason = "";
      return;
    }

    if (!string.IsNullOrWhiteSpace(reason)) {
      activeReason = reason.Trim();
    }
  }

  public static void ForceClearLoadingOverlay() {
    activeOverlayCount = 0;
    protectedOverlayCount = 0;
    activeReason = "";
  }
}
