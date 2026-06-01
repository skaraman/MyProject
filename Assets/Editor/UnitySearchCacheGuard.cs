using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class UnitySearchCacheGuard {
  const long MaxSearchCacheBytes = 2L * 1024L * 1024L * 1024L;
  const double StartupWatchSeconds = 120d;
  const string SearchCacheRelativePath = "Library/Search";
  const string CrashFolderName = "Crash";
  static readonly string[] RequiredSearchCacheFiles = {
    "propertyDatabase.db.st"
  };
  static double watchUntil;
  static double nextWatchAt;

  static bool s_PurgeScheduled = false;

  static UnitySearchCacheGuard() {
    watchUntil = EditorApplication.timeSinceStartup + StartupWatchSeconds;
    RunStartupCheck();
    EditorApplication.delayCall += RunStartupCheck;
    EditorApplication.update += WatchStartupSearchCache;
  }

  public static void PurgeSearchCacheMenu() {
    TryGetSearchCacheSize(out var totalBytes, out var largestBytes);
    MoveSearchCache("manual", totalBytes, largestBytes, isMenuTriggered: true);
  }

  static void RunStartupCheck() {
    if (s_PurgeScheduled) return;
    if (!TryGetSearchCacheSize(out var totalBytes, out var largestBytes)) return;
    if (HasMissingRequiredFiles()) {
      MoveSearchCache("missing_required_file");
      return;
    }
    if (totalBytes < MaxSearchCacheBytes && largestBytes < MaxSearchCacheBytes) return;
    MoveSearchCache("size_guard total_gb=" + ToGb(totalBytes) + " largest_gb=" + ToGb(largestBytes), totalBytes, largestBytes);
  }

  static void WatchStartupSearchCache() {
    if (EditorApplication.timeSinceStartup < nextWatchAt) return;
    nextWatchAt = EditorApplication.timeSinceStartup + 1d;
    RunStartupCheck();
    if (EditorApplication.timeSinceStartup < watchUntil) return;
    EditorApplication.update -= WatchStartupSearchCache;
  }

  static bool HasMissingRequiredFiles() {
    var searchPath = GetSearchCachePath();
    if (!Directory.Exists(searchPath)) return false;

    foreach (var relativePath in RequiredSearchCacheFiles) {
      var fullPath = Path.Combine(searchPath, relativePath);
      if (!File.Exists(fullPath)) {
        Debug.LogWarning("[UnitySearchCacheGuard] Missing generated Unity Search cache file path='" + fullPath + "'");
        return true;
      }
    }

    return false;
  }

  static bool TryGetSearchCacheSize(out long totalBytes, out long largestBytes) {
    totalBytes = 0;
    largestBytes = 0;
    var searchPath = GetSearchCachePath();
    if (!Directory.Exists(searchPath)) return false;

    try {
      foreach (var filePath in Directory.EnumerateFiles(searchPath, "*", SearchOption.AllDirectories)) {
        var length = new FileInfo(filePath).Length;
        totalBytes += length;
        if (length > largestBytes) largestBytes = length;
      }
      return true;
    }
    catch (Exception ex) {
      Debug.LogWarning("[UnitySearchCacheGuard] Unable to measure Library/Search: " + ex.Message);
      return false;
    }
  }

  static void MoveSearchCache(string reason, long totalBytes, long largestBytes, bool isMenuTriggered = false) {
    var searchPath = GetSearchCachePath();
    if (!Directory.Exists(searchPath)) {
      if (isMenuTriggered) {
        EditorUtility.DisplayDialog("Purge Search Cache", "No Unity Search cache was found to purge, or it is empty.", "OK");
      }
      return;
    }

    try {
      var projectRoot = GetProjectRoot();
      var crashRoot = Path.Combine(projectRoot, CrashFolderName);
      Directory.CreateDirectory(crashRoot);
      var destination = Path.Combine(
        crashRoot,
        "UnitySearchCache_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
      );
      Directory.Move(searchPath, destination);
      Debug.Log(
        "[UnitySearchCacheGuard] Moved generated Unity Search cache reason=" + reason +
        " from='" + searchPath +
        "' to='" + destination + "'"
      );
      if (isMenuTriggered) {
        EditorUtility.DisplayDialog("Purge Search Cache", "Unity Search cache has been successfully purged.", "OK");
      }
    }
    catch (Exception) {
      if (isMenuTriggered) {
        ScheduleCleanOnExit(reason, totalBytes, largestBytes);
        EditorUtility.DisplayDialog(
          "Purge Search Cache",
          "Unity Search cache is currently locked by the Editor.\n\nA background purge has been scheduled to run automatically as soon as you close the Editor.",
          "OK"
        );
      }
      else {
        if (!s_PurgeScheduled) {
          s_PurgeScheduled = true;
          EditorApplication.quitting += () => ScheduleCleanOnExit(reason, totalBytes, largestBytes);
          Debug.Log(
            "[UnitySearchCacheGuard] Search cache is locked by the Editor. Scheduled silent background purge on exit. " +
            "Reason: " + reason + " (total=" + ToGb(totalBytes) + "GB, largest=" + ToGb(largestBytes) + "GB)"
          );
        }
      }
    }
  }

  static void ScheduleCleanOnExit(string reason, long totalBytes, long largestBytes) {
    try {
      var projectRoot = GetProjectRoot();
      var searchPath = GetSearchCachePath();
      var crashRoot = Path.Combine(projectRoot, CrashFolderName);
      var destination = Path.Combine(
        crashRoot,
        "UnitySearchCache_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
      );

      int pid = System.Diagnostics.Process.GetCurrentProcess().Id;

      string psCommand = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"" +
        $"Start-Sleep -Seconds 1; " +
        $"$proc = Get-Process -Id {pid} -ErrorAction SilentlyContinue; " +
        $"if ($proc) {{ $proc.WaitForExit(15000) }}; " +
        $"if (Test-Path '{searchPath}') {{ " +
        $"  New-Item -ItemType Directory -Force -Path '{crashRoot}' | Out-Null; " +
        $"  Move-Item -Path '{searchPath}' -Destination '{destination}' -Force -ErrorAction SilentlyContinue; " +
        $"}}\"";

      var psi = new System.Diagnostics.ProcessStartInfo {
        FileName = "powershell.exe",
        Arguments = psCommand,
        CreateNoWindow = true,
        UseShellExecute = false
      };

      System.Diagnostics.Process.Start(psi);
    }
    catch (Exception ex) {
      Debug.LogWarning("[UnitySearchCacheGuard] Failed to schedule background exit-time purge: " + ex.Message);
    }
  }

  static string GetSearchCachePath() {
    return Path.Combine(GetProjectRoot(), SearchCacheRelativePath.Replace('/', Path.DirectorySeparatorChar));
  }

  static string GetProjectRoot() {
    return Directory.GetParent(Application.dataPath).FullName;
  }

  static string ToGb(long bytes) {
    return (bytes / (1024d * 1024d * 1024d)).ToString("0.00");
  }
}
