using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

internal sealed class StaleStandaloneBuildArtifactCleaner : IPreprocessBuildWithReport
{
    private static readonly Regex UnityVersionTagPattern = new(@"\d{4}\.\d+\.\d+f\d+", RegexOptions.Compiled);

    public int callbackOrder => int.MinValue;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (!IsStandaloneWindowsBuild(report.summary.platform))
        {
            return;
        }

        string outputPath = report.summary.outputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Debug.LogWarning("[BuildCleaner] Missing output path. Skipping stale standalone artifact cleanup.");
            return;
        }

        string dataFolderPath = GetDataFolderPath(outputPath);
        DeleteLegacyDataBundle(dataFolderPath, outputPath);
    }

    private static bool IsStandaloneWindowsBuild(BuildTarget platform)
    {
        return platform == BuildTarget.StandaloneWindows || platform == BuildTarget.StandaloneWindows64;
    }

    private static string GetDataFolderPath(string outputPath)
    {
        string outputDirectory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        string outputNameWithoutExtension = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(outputDirectory, outputNameWithoutExtension + "_Data");
    }

    private static void DeleteLegacyDataBundle(string dataFolderPath, string outputPath)
    {
        string legacyBundlePath = Path.Combine(dataFolderPath, "data.unity3d");
        if (!File.Exists(legacyBundlePath))
        {
            Debug.Log($"[BuildCleaner] No legacy data bundle found. outputPath={outputPath} dataFolder={dataFolderPath}");
            return;
        }

        FileInfo bundleInfo = new(legacyBundlePath);
        string headerVersion = TryReadUnityVersionTag(legacyBundlePath) ?? "<unknown>";
        Debug.Log(
            $"[BuildCleaner] Removing stale data bundle before build. path={legacyBundlePath} " +
            $"headerVersion={headerVersion} lastWriteUtc={bundleInfo.LastWriteTimeUtc:O} sizeBytes={bundleInfo.Length}");

        File.Delete(legacyBundlePath);
    }

    private static string TryReadUnityVersionTag(string filePath)
    {
        try
        {
            using FileStream stream = File.OpenRead(filePath);
            int bytesToRead = (int)Math.Min(256, stream.Length);
            byte[] buffer = new byte[bytesToRead];
            int bytesRead = stream.Read(buffer, 0, bytesToRead);
            string headerText = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            Match match = UnityVersionTagPattern.Match(headerText);
            return match.Success ? match.Value : null;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[BuildCleaner] Failed to read version tag for {filePath}. exception={exception}");
            return null;
        }
    }
}

public static class StandaloneBuildInvoker
{
    public static void BuildWindows64()
    {
        string[] enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (enabledScenes.Length == 0)
        {
            throw new BuildFailedException("[BuildInvoker] No enabled scenes were found in EditorBuildSettings.");
        }

        string outputPath = Path.GetFullPath(Path.Combine("Builds", "MyProject.exe"));
        Debug.Log($"[BuildInvoker] Starting Windows64 build. outputPath={outputPath} sceneCount={enabledScenes.Length}");

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = enabledScenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });

        Debug.Log(
            $"[BuildInvoker] Build finished. result={report.summary.result} " +
            $"outputPath={report.summary.outputPath} totalSize={report.summary.totalSize}");

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new BuildFailedException($"[BuildInvoker] Build failed. result={report.summary.result}");
        }
    }
}
