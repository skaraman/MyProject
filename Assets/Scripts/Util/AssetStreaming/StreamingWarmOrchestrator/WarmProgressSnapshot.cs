public readonly struct WarmProgressSnapshot {
  public readonly WarmContext context;
  public readonly int readyCount;
  public readonly int totalCount;
  public readonly int criticalReadyCount;
  public readonly int criticalTotalCount;
  public readonly float readyRatio;
  public readonly bool softTimedOut;
  public readonly bool criticalReady;

  public WarmProgressSnapshot(
    WarmContext context,
    int readyCount,
    int totalCount,
    int criticalReadyCount,
    int criticalTotalCount,
    float readyRatio,
    bool softTimedOut,
    bool criticalReady
  ) {
    this.context = context;
    this.readyCount = readyCount;
    this.totalCount = totalCount;
    this.criticalReadyCount = criticalReadyCount;
    this.criticalTotalCount = criticalTotalCount;
    this.readyRatio = readyRatio;
    this.softTimedOut = softTimedOut;
    this.criticalReady = criticalReady;
  }
}
