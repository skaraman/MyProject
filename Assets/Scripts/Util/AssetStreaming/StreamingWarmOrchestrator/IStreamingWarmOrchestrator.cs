using System;

public interface IStreamingWarmOrchestrator {
  bool IsRunning { get; }
  void Run(WarmRequest request, Action<WarmResult> onComplete = null);
  void Cancel();
}
