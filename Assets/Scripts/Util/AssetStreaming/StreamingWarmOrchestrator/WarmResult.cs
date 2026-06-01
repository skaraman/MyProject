using System;

public readonly struct WarmResult {
  public readonly WarmContext context;
  public readonly bool completedWithinTimeout;
  public readonly bool reachedReadyThreshold;
  public readonly bool playerCriticalReady;
  public readonly float readyRatio;
  public readonly int readyCount;
  public readonly int totalCount;
  public readonly int criticalReadyCount;
  public readonly int criticalTotalCount;
  public readonly int requestedAddressCount;
  public readonly float elapsedMs;
  public readonly bool hardTimeoutBypassUsed;
  public readonly string failureReason;

  public WarmResult(
    WarmContext context,
    bool completedWithinTimeout,
    bool reachedReadyThreshold,
    bool playerCriticalReady,
    float readyRatio,
    int readyCount,
    int totalCount,
    int criticalReadyCount,
    int criticalTotalCount,
    int requestedAddressCount,
    float elapsedMs,
    bool hardTimeoutBypassUsed,
    string failureReason
  ) {
    this.context = context;
    this.completedWithinTimeout = completedWithinTimeout;
    this.reachedReadyThreshold = reachedReadyThreshold;
    this.playerCriticalReady = playerCriticalReady;
    this.readyRatio = readyRatio;
    this.readyCount = readyCount;
    this.totalCount = totalCount;
    this.criticalReadyCount = criticalReadyCount;
    this.criticalTotalCount = criticalTotalCount;
    this.requestedAddressCount = requestedAddressCount;
    this.elapsedMs = elapsedMs;
    this.hardTimeoutBypassUsed = hardTimeoutBypassUsed;
    this.failureReason = string.IsNullOrWhiteSpace(failureReason) ? "" : failureReason.Trim();
  }
}
