using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class UnitySearchCacheGuard {
  const long MaxSearchCacheBytes = 2L * 1024L * 1024L * 1024L;
  const string SearchCacheRelativePath = "Library/Search";
  const string CrashFolderName = "Crash";

  static UnitySearchCacheGuard() {
    RunStartupCheck();
    EditorApplication.delayCall += RunStartupCheck;
  }

  [MenuItem("Tools/Project Hygiene/Purge Unity Search Cache")]
  public static void PurgeSearchCacheMenu() {
    MoveSearchCache("manual");
  }

  static void RunStartupCheck() {
    if (!TryGetSearchCacheSize(out var totalBytes, out var largestBytes)) return;
    if (totalBytes < MaxSearchCacheBytes && largestBytes < MaxSearchCacheBytes) return;
    MoveSearchCache("size_guard total_gb=" + ToGb(totalBytes) + " largest_gb=" + ToGb(largestBytes));
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

  static void MoveSearchCache(string reason) {
    var searchPath = GetSearchCachePath();
    if (!Directory.Exists(searchPath)) return;

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
    }
    catch (Exception ex) {
      Debug.LogWarning("[UnitySearchCacheGuard] Unable to move Library/Search: " + ex.Message);
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
