using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public sealed class MusicPlayer : MonoBehaviour {
  [SerializeField] TextAsset manifestAsset;
  [SerializeField, Range(0f, 1f)] float volume = 1f;

  readonly List<Action> subscriptions = new();

  Dictionary<string, MusicPlaylistDefinition> playlists;
  AudioSource audioSource;
  AsyncOperationHandle<AudioClip> activeClipHandle;
  MusicPlaylistDefinition activePlaylist;
  Coroutine audioDataReadyRoutine;
  string activeZone = "";
  int trackIndex;
  int failedTrackCount;
  int loadGeneration;
  bool hasActiveClipHandle;
  bool trackIsPlaying;
  bool waitingForBlackscreen;

  void Awake() {
    audioSource = GetComponent<AudioSource>();
    if (audioSource == null) {
      audioSource = gameObject.AddComponent<AudioSource>();
    }

    audioSource.playOnAwake = false;
    audioSource.spatialBlend = 0f;
    audioSource.ignoreListenerPause = true;
    audioSource.volume = volume;

    MusicManifestCatalog.TryBuildPlaylists(manifestAsset, out playlists);
  }

  void OnEnable() {
    subscriptions.Add(MessageBus.On("LocationUpdated", OnLocationUpdated));
    subscriptions.Add(MessageBus.On(
      SingleSceneManager.BlackscreenFullyTransparentTopic,
      OnBlackscreenFullyTransparent
    ));
    if (!ApplyZone(LocationManager.currentLocation)) {
      PlayAwakePlaylist();
    }
  }

  void OnDisable() {
    for (var i = 0; i < subscriptions.Count; i++) {
      subscriptions[i]?.Invoke();
    }

    subscriptions.Clear();
    activeZone = "";
    waitingForBlackscreen = false;
    loadGeneration++;
    StopPlayback();
  }

  void Update() {
    if (!trackIsPlaying || audioSource == null || audioSource.isPlaying) {
      return;
    }

    trackIsPlaying = false;
    trackIndex++;
    if (!activePlaylist.loop && trackIndex >= activePlaylist.tracks.Length) {
      StopPlayback();
      return;
    }

    LoadCurrentTrack();
  }

  void OnLocationUpdated(object payload) {
    ApplyZone(Convert.ToString(payload));
  }

  void OnBlackscreenFullyTransparent() {
    if (!waitingForBlackscreen || activePlaylist == null) {
      return;
    }

    waitingForBlackscreen = false;
    LoadCurrentTrack();
  }

  bool ApplyZone(string zoneId) {
    var normalizedZone = string.IsNullOrWhiteSpace(zoneId) ? "" : zoneId.Trim();
    if (string.Equals(activeZone, normalizedZone, StringComparison.OrdinalIgnoreCase)) {
      return activePlaylist != null;
    }

    activeZone = normalizedZone;
    trackIndex = 0;
    failedTrackCount = 0;
    waitingForBlackscreen = false;
    loadGeneration++;
    StopPlayback();

    if (playlists == null || !playlists.TryGetValue(activeZone, out activePlaylist)) {
      activePlaylist = null;
      return false;
    }

    if (activePlaylist.playOnAwake &&
        !SingleSceneManager.IsBlackscreenFullyTransparent) {
      waitingForBlackscreen = true;
      return true;
    }

    LoadCurrentTrack();
    return true;
  }

  void PlayAwakePlaylist() {
    if (playlists == null) {
      return;
    }

    foreach (var playlist in playlists.Values) {
      if (!playlist.playOnAwake) {
        continue;
      }

      ApplyZone(playlist.zoneId);
      return;
    }
  }

  void LoadCurrentTrack() {
    StopPlayback();
    if (activePlaylist == null || activePlaylist.tracks.Length == 0) {
      return;
    }

    if (trackIndex >= activePlaylist.tracks.Length) {
      if (!activePlaylist.loop) {
        return;
      }

      trackIndex = 0;
    }

    var address = activePlaylist.tracks[trackIndex];
    var requestGeneration = ++loadGeneration;
    if (AudioClipResolver.TryLoadEditorClip(address, out var editorClip)) {
      PrepareAudioData(editorClip, requestGeneration, address);
      return;
    }

    AsyncOperationHandle<AudioClip> handle;
    try {
      handle = Addressables.LoadAssetAsync<AudioClip>(address);
    }
    catch (Exception exception) {
      Debug.LogError(
        "[MusicPlayer] Failed to request track='" + address +
        "'. error='" + exception.Message + "'"
      );
      return;
    }

    handle.Completed += completedHandle => OnTrackLoaded(completedHandle, requestGeneration, address);
  }

  void OnTrackLoaded(
    AsyncOperationHandle<AudioClip> handle,
    int requestGeneration,
    string address
  ) {
    if (requestGeneration != loadGeneration || !isActiveAndEnabled) {
      Addressables.Release(handle);
      return;
    }

    if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null) {
      Addressables.Release(handle);
      failedTrackCount++;
      if (failedTrackCount >= activePlaylist.tracks.Length) {
        Debug.LogError("[MusicPlayer] No playable tracks for zone '" + activeZone + "'.");
        return;
      }

      trackIndex++;
      LoadCurrentTrack();
      return;
    }

    activeClipHandle = handle;
    hasActiveClipHandle = true;
    PrepareAudioData(handle.Result, requestGeneration, address);
  }

  void PrepareAudioData(AudioClip clip, int requestGeneration, string address) {
    if (clip.loadState == AudioDataLoadState.Loaded) {
      BeginPlayback(clip, address);
      return;
    }

    if (clip.loadState == AudioDataLoadState.Unloaded && !clip.LoadAudioData()) {
      HandleAudioDataFailure(address);
      return;
    }

    audioDataReadyRoutine = StartCoroutine(
      WaitForAudioData(clip, requestGeneration, address)
    );
  }

  IEnumerator WaitForAudioData(AudioClip clip, int requestGeneration, string address) {
    while (clip.loadState == AudioDataLoadState.Loading) {
      if (requestGeneration != loadGeneration) {
        yield break;
      }

      yield return null;
    }

    audioDataReadyRoutine = null;
    if (requestGeneration != loadGeneration) {
      yield break;
    }

    if (clip.loadState != AudioDataLoadState.Loaded) {
      HandleAudioDataFailure(address);
      yield break;
    }

    BeginPlayback(clip, address);
  }

  void BeginPlayback(AudioClip clip, string address) {
    failedTrackCount = 0;
    audioSource.clip = clip;
    audioSource.loop = activePlaylist.loop && activePlaylist.tracks.Length == 1;
    audioSource.Play();
    trackIsPlaying = true;
    RuntimeLog.Log(
      "[MusicPlayer] Playing zone='" + activeZone + "' track='" + address + "'."
    );
  }

  void HandleAudioDataFailure(string address) {
    StopPlayback();
    failedTrackCount++;
    if (failedTrackCount >= activePlaylist.tracks.Length) {
      Debug.LogError(
        "[MusicPlayer] Audio data failed to load for zone='" + activeZone +
        "' track='" + address + "'."
      );
      return;
    }

    trackIndex++;
    LoadCurrentTrack();
  }

  void StopPlayback() {
    trackIsPlaying = false;
    if (audioDataReadyRoutine != null) {
      StopCoroutine(audioDataReadyRoutine);
      audioDataReadyRoutine = null;
    }

    if (audioSource != null) {
      audioSource.Stop();
      audioSource.clip = null;
      audioSource.loop = false;
    }

    if (!hasActiveClipHandle) {
      return;
    }

    Addressables.Release(activeClipHandle);
    hasActiveClipHandle = false;
  }
}
