using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public static class AsyncCoroutine {
  static Dictionary<object, Func<CancellationToken, IAsyncEnumerable<object>>> routines = new();
  static Dictionary<object, CancellationTokenSource> active = new();

  public static void Create(object key, Func<CancellationToken, IAsyncEnumerable<object>> func) {
    routines[key] = func;
  }

  public static async void Run(object key) {
    if (!routines.ContainsKey(key)) return;
    Cancel(key);
    var cts = new CancellationTokenSource();
    active[key] = cts;
    try {
      await foreach (var _ in routines[key].Invoke(cts.Token).WithCancellation(cts.Token))
        await Task.Yield();
    }
    catch (OperationCanceledException) {
      Debug.Log($"[AsyncCoroutine] Canceled: {key}");
    }
    finally {
      if (active.ContainsKey(key) && active[key] == cts)
        active.Remove(key);
    }
  }

  public static void Cancel(object key) {
    if (!active.ContainsKey(key)) return;
    active[key].Cancel();
    active[key].Dispose();
    active.Remove(key);
  }

  public static void CancelAll() {
    foreach (var cts in active.Values)
      cts.Cancel();
    foreach (var cts in active.Values)
      cts.Dispose();
    active.Clear();
  }
  
  public static async void RunAfterDelay(float seconds, Action callback) {
    var ms = (int)(seconds * 1000);
    await Task.Delay(ms);
    callback?.Invoke();
  }
}
// Example Usage
//  void Start() {
//   AsyncCoroutine.Create("fade", async token => {
//     float t = 0;
//     var renderer = GetComponent<Renderer>();
//     var from = Color.white;
//     var to = Color.red;
//     while (t < 1f) {
//       if (token.IsCancellationRequested) yield break;
//       renderer.material.color = Color.Lerp(from, to, t);
//       t += Time.deltaTime;
//       yield return null;
//     }
//     renderer.material.color = to;
//   });

//   AsyncCoroutine.Run("fade");
//  }