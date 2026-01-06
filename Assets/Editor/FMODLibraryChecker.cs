using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Editor utility to check for missing FMOD release libraries that can cause build crashes.
/// </summary>
public class FMODLibraryChecker : EditorWindow
{
    private static readonly string[] RequiredWindowsLibs = new string[]
    {
        "Assets/Plugins/FMOD/platforms/win/lib/x86_64/fmod.dll",
        "Assets/Plugins/FMOD/platforms/win/lib/x86_64/fmodstudio.dll",
        "Assets/Plugins/FMOD/platforms/win/lib/x86/fmod.dll",
        "Assets/Plugins/FMOD/platforms/win/lib/x86/fmodstudio.dll"
    };

    private static readonly string[] RequiredLinuxLibs = new string[]
    {
        "Assets/Plugins/FMOD/platforms/linux/lib/x86_64/libfmod.so",
        "Assets/Plugins/FMOD/platforms/linux/lib/x86_64/libfmodstudio.so"
    };

    [MenuItem("Tools/FMOD/Check Release Libraries")]
    public static void CheckFMODLibraries()
    {
        List<string> missingLibraries = new List<string>();

        // Check if FMOD folder exists
        if (!Directory.Exists("Assets/Plugins/FMOD"))
        {
            Debug.Log("FMOD plugin folder not found. If you're not using FMOD, this is OK.");
            return;
        }

        // Check Windows libraries
        foreach (string lib in RequiredWindowsLibs)
        {
            if (!File.Exists(lib))
            {
                missingLibraries.Add(lib);
            }
        }

        // Check Linux libraries
        foreach (string lib in RequiredLinuxLibs)
        {
            if (!File.Exists(lib))
            {
                missingLibraries.Add(lib);
            }
        }

        if (missingLibraries.Count > 0)
        {
            Debug.LogError("=== MISSING FMOD RELEASE LIBRARIES ===");
            Debug.LogError("The following FMOD release libraries are missing. This will cause build crashes!");
            Debug.LogError("See FMOD_BUILD_FIX.md for instructions on how to fix this.");
            Debug.LogError("");
            
            foreach (string lib in missingLibraries)
            {
                Debug.LogError($"  - {lib}");
            }
            
            Debug.LogError("");
            Debug.LogError("You need to download FMOD Engine libraries from https://www.fmod.com/download");
            Debug.LogError("Only the development/logging libraries (fmodstudioL.dll/libfmodstudioL.so) are present.");
            Debug.LogError("Builds require release versions (fmod.dll, fmodstudio.dll, libfmod.so, libfmodstudio.so).");
            
            EditorUtility.DisplayDialog(
                "Missing FMOD Libraries",
                $"Found {missingLibraries.Count} missing FMOD release libraries.\n\n" +
                "This will cause build crashes!\n\n" +
                "Check the Console for details and see FMOD_BUILD_FIX.md for instructions.",
                "OK"
            );
        }
        else
        {
            Debug.Log("✓ All required FMOD release libraries are present.");
            EditorUtility.DisplayDialog(
                "FMOD Libraries OK",
                "All required FMOD release libraries are present.",
                "OK"
            );
        }
    }

    [InitializeOnLoadMethod]
    private static void CheckOnLoad()
    {
        // Automatically check on editor load (only show warnings, not dialogs)
        if (!Directory.Exists("Assets/Plugins/FMOD"))
        {
            return;
        }

        List<string> missingLibraries = new List<string>();

        foreach (string lib in RequiredWindowsLibs)
        {
            if (!File.Exists(lib))
            {
                missingLibraries.Add(lib);
            }
        }

        foreach (string lib in RequiredLinuxLibs)
        {
            if (!File.Exists(lib))
            {
                missingLibraries.Add(lib);
            }
        }

        if (missingLibraries.Count > 0)
        {
            Debug.LogWarning($"[FMOD] Missing {missingLibraries.Count} release libraries. " +
                           "This may cause build crashes. Run 'Tools > FMOD > Check Release Libraries' for details.");
        }
    }
}
