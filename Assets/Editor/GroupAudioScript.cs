using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using System.IO;
using System.Linq;

public class GroupAudioScript {
    [MenuItem("Tools/SoundEffects/GroupAudio")]
    public static void GroupAudio() {
        string sfxPath = "Assets/SoundEffects";
        var folders = AssetDatabase.GetSubFolders(sfxPath);
        
        string mixerPath = "Assets/SoundEffects/MainMixer.mixer";
        AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(mixerPath);
        if (mixer == null) {
            Debug.LogWarning($"[GroupAudio] Could not find an AudioMixer at {mixerPath}. Please right-click in Assets/SoundEffects, choose Create -> Audio Mixer, name it 'MainMixer', and run this again. (The script will still update the JSON manifest).");
        } else {
            Debug.Log($"[GroupAudio] Found AudioMixer: {mixer.name}. Note: Unity prevents scripts from creating Mixer Groups automatically. Please ensure you manually create these groups under Master: {string.Join(", ", folders.Select(Path.GetFileName))}");
        }

        string manifestPath = "Assets/SoundEffects/SoundEffectManifest.json";
        if (!File.Exists(manifestPath)) {
            Debug.LogError($"[GroupAudio] Could not find manifest at {manifestPath}");
            return;
        }

        string json = File.ReadAllText(manifestPath);
        var manifest = JsonUtility.FromJson<SoundEffectManifestData>(json);

        if (manifest == null || manifest.effects == null) {
            Debug.LogError("[GroupAudio] Failed to parse manifest JSON.");
            return;
        }

        bool updated = false;

        int removedCount = manifest.effects.RemoveAll(entry => !File.Exists(entry.clip));
        if (removedCount > 0) {
            updated = true;
            Debug.Log($"[GroupAudio] Removed {removedCount} entries pointing to missing files.");
        }

        foreach (var entry in manifest.effects) {
            string clipPath = entry.clip;
            if (clipPath.StartsWith(sfxPath)) {
                // e.g. "Assets/SoundEffects/UI/menumove.mp3"
                string relativePath = clipPath.Substring(sfxPath.Length + 1); // "UI/menumove.mp3"
                int slashIndex = relativePath.IndexOf('/');
                if (slashIndex > 0) {
                    string folderName = relativePath.Substring(0, slashIndex); // "UI"
                    if (entry.mixerGroup != folderName) {
                        entry.mixerGroup = folderName;
                        updated = true;
                    }
                }
            }
        }

        if (updated) {
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            AssetDatabase.Refresh();
            Debug.Log("[GroupAudio] Successfully updated SoundEffectManifest.json with new mixer groups based on folders!");
        } else {
            Debug.Log("[GroupAudio] SoundEffectManifest.json is already up to date. No changes needed.");
        }
    }
}
