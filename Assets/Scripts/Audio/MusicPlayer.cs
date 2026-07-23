using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class AudioPlaybackMessages {
  public const string PauseMenuOpened = "audio.pauseMenuOpened";
  public const string PauseMenuClosed = "audio.pauseMenuClosed";
}

public sealed class MusicPlayer : MonoBehaviour {
  public const string FadeOutMessage = "music.fadeOut";
  const string EpisodeRoutePrefix = "episode:";

  [SerializeField] TextAsset manifestAsset;
  [SerializeField, Range(0f, 1f)] float volume = 1f;

  readonly List<Action> subscriptions = new();

  Dictionary<string, MusicPlaylistDefinition> playlists;
  Dictionary<string, MusicPlaylistDefinition> episodePlaylists;
  AudioSource audioSource;
  AsyncOperationHandle<AudioClip> activeClipHandle;
  MusicPlaylistDefinition activePlaylist;
  Coroutine audioDataReadyRoutine;
  Coroutine fadeRoutine;
  string activeZone = "";
  int trackIndex;
  int failedTrackCount;
  int loadGeneration;
  int suspendedTimeSamples;
  bool applicationFocused = true;
  bool applicationPaused;
  bool pauseMenuOpen;
  bool pauseMenuVolumeDucked;
  float pauseMenuVolumeBeforeDucking;
  bool hasActiveClipHandle;
  bool playbackSourcePaused;
  bool playbackSuspended;
  bool trackIsPlaying;
  bool waitingForBlackscreen;
  bool episodeMusicActive;
  bool episodeMusicGameplayVisible;
  int episodeMusicRevision = -1;
  int episodeMusicRegistryVersion = -1;

  public float Volume => volume;

  public void SetVolume(float value) {
    value = Mathf.Clamp01(value);
    if (Mathf.Approximately(volume, value)) return;

    volume = value;
    RestoreVolume();
  }

  void Awake() {
    audioSource = GetComponent<AudioSource>();
    if (audioSource == null) {
      audioSource = gameObject.AddComponent<AudioSource>();
    }

    audioSource.playOnAwake = false;
    audioSource.spatialBlend = 0f;
    audioSource.ignoreListenerPause = true;
    audioSource.volume = volume;
    pauseMenuVolumeBeforeDucking = volume;

    MusicManifestCatalog.TryBuildPlaylists(
      manifestAsset,
      out playlists,
      out episodePlaylists
    );
  }

  void OnEnable() {
    applicationFocused = Application.isFocused;
    applicationPaused = false;
    pauseMenuOpen = SingleSceneManager.IsPauseMenuActive;
    pauseMenuVolumeDucked = false;
    playbackSourcePaused = false;
    playbackSuspended = !applicationFocused;
    suspendedTimeSamples = 0;

    subscriptions.Add(MessageBus.On("LocationUpdated", OnLocationUpdated));
    subscriptions.Add(MessageBus.On(FadeOutMessage, OnFadeOut));
    subscriptions.Add(MessageBus.On(AudioPlaybackMessages.PauseMenuOpened, OnPauseMenuOpened));
    subscriptions.Add(MessageBus.On(AudioPlaybackMessages.PauseMenuClosed, OnPauseMenuClosed));
    subscriptions.Add(MessageBus.On(
      SingleSceneManager.BlackscreenFullyTransparentTopic,
      OnBlackscreenFullyTransparent
    ));
    if (!ApplyZone(LocationManager.currentLocation)) {
      PlayAwakePlaylist();
    }
    ApplyPauseMenuVolumeDuck();
    UpdateEpisodeMusic();
  }

  void OnDisable() {
    for (var i = 0; i < subscriptions.Count; i++) {
      subscriptions[i]?.Invoke();
    }

    subscriptions.Clear();
    activeZone = "";
    episodeMusicActive = false;
    episodeMusicGameplayVisible = false;
    episodeMusicRevision = -1;
    episodeMusicRegistryVersion = -1;
    waitingForBlackscreen = false;
    loadGeneration++;
    RestoreVolume();
    StopPlayback();
  }

  void Update() {
    UpdateEpisodeMusic();

    if (playbackSuspended) {
      return;
    }

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

  void OnApplicationFocus(bool hasFocus) {
    applicationFocused = hasFocus;
    RefreshApplicationSuspension();
  }

  void OnApplicationPause(bool isPaused) {
    applicationPaused = isPaused;
    RefreshApplicationSuspension();
  }

  void RefreshApplicationSuspension() {
    if (!applicationFocused || applicationPaused) {
      SuspendPlayback();
      return;
    }

    ResumePlayback();
  }

  void SuspendPlayback() {
    if (playbackSuspended) {
      return;
    }

    playbackSuspended = true;
    if (!trackIsPlaying || audioSource == null || audioSource.clip == null) {
      return;
    }

    suspendedTimeSamples = audioSource.timeSamples;
    audioSource.Pause();
    playbackSourcePaused = true;
  }

  void ResumePlayback() {
    if (!playbackSuspended) {
      return;
    }

    playbackSuspended = false;
    if (!trackIsPlaying || audioSource == null || audioSource.clip == null) {
      return;
    }

    if (audioSource.clip.samples > 0) {
      suspendedTimeSamples = Mathf.Clamp(
        suspendedTimeSamples,
        0,
        audioSource.clip.samples - 1
      );
      audioSource.timeSamples = suspendedTimeSamples;
    }

    if (playbackSourcePaused) {
      playbackSourcePaused = false;
      audioSource.UnPause();
      return;
    }

    audioSource.Play();
  }

  void OnPauseMenuOpened(object payload) {
    if (pauseMenuOpen) {
      return;
    }

    pauseMenuOpen = true;
    ApplyPauseMenuVolumeDuck();
  }

  void OnPauseMenuClosed(object payload) {
    if (!pauseMenuOpen) {
      return;
    }

    pauseMenuOpen = false;
    RestorePauseMenuVolume();
  }

  void OnLocationUpdated(object payload) {
    if (episodeMusicActive) {
      return;
    }

    ApplyZone(Convert.ToString(payload));
  }

  void UpdateEpisodeMusic() {
    if (pauseMenuOpen) {
      return;
    }

    var gameplayVisible = SingleSceneManager.IsGameplayActive &&
      SingleSceneManager.IsBlackscreenFullyTransparent;
    var episodeRevision = gameplayVisible ? ContentEpisodeProgression.EpisodeRevision : -1;
    var registryVersion = gameplayVisible ? ActiveContentRegistryRuntime.ReloadVersion : -1;
    if (episodeMusicGameplayVisible == gameplayVisible &&
        episodeMusicRevision == episodeRevision &&
        episodeMusicRegistryVersion == registryVersion) {
      return;
    }

    var wasEpisodeMusicActive = episodeMusicActive;
    episodeMusicGameplayVisible = gameplayVisible;
    episodeMusicRevision = episodeRevision;
    episodeMusicRegistryVersion = registryVersion;
    if (gameplayVisible) {
      episodeMusicActive = ApplyEpisode(
        ContentEpisodeProgression.ResolveCurrentEpisodeId()
      );
      return;
    }

    episodeMusicActive = false;
    if (wasEpisodeMusicActive) {
      ApplyZone(LocationManager.currentLocation);
    }
  }

  void OnFadeOut(object payload) {
    var duration = payload is float requestedDuration
      ? requestedDuration
      : 1f;
    duration = Mathf.Max(0f, duration);

    CancelFade();
    if (duration <= 0f) {
      SetMusicVolume(0f);
      return;
    }

    fadeRoutine = StartCoroutine(FadeOutRoutine(duration));
  }

  IEnumerator FadeOutRoutine(float duration) {
    var startingVolume = GetMusicVolume();
    var elapsed = 0f;

    while (elapsed < duration) {
      elapsed += Time.unscaledDeltaTime;
      var progress = Mathf.Clamp01(elapsed / duration);
      SetMusicVolume(Mathf.Lerp(startingVolume, 0f, progress));
      yield return null;
    }

    SetMusicVolume(0f);
    fadeRoutine = null;
  }

  void CancelFade() {
    if (fadeRoutine != null) {
      StopCoroutine(fadeRoutine);
      fadeRoutine = null;
    }
  }

  void RestoreVolume() {
    CancelFade();
    SetMusicVolume(volume);
  }

  void ApplyPauseMenuVolumeDuck() {
    if (!pauseMenuOpen || pauseMenuVolumeDucked || audioSource == null) {
      return;
    }

    pauseMenuVolumeBeforeDucking = audioSource.volume;
    pauseMenuVolumeDucked = true;
    audioSource.volume = pauseMenuVolumeBeforeDucking * 0.5f;
  }

  void RestorePauseMenuVolume() {
    if (!pauseMenuVolumeDucked) {
      return;
    }

    pauseMenuVolumeDucked = false;
    if (audioSource != null) {
      audioSource.volume = pauseMenuVolumeBeforeDucking;
    }
  }

  float GetMusicVolume() {
    if (pauseMenuVolumeDucked) {
      return pauseMenuVolumeBeforeDucking;
    }

    return audioSource != null ? audioSource.volume : volume;
  }

  void SetMusicVolume(float value) {
    pauseMenuVolumeBeforeDucking = Mathf.Clamp01(value);
    if (audioSource == null) {
      return;
    }

    audioSource.volume = pauseMenuVolumeDucked
      ? pauseMenuVolumeBeforeDucking * 0.5f
      : pauseMenuVolumeBeforeDucking;
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
    return ApplyPlaylist(normalizedZone, playlists);
  }

  bool ApplyEpisode(string episodeId) {
    var normalizedEpisode = string.IsNullOrWhiteSpace(episodeId) ? "" : episodeId.Trim();
    return ApplyPlaylist(EpisodeRoutePrefix + normalizedEpisode, episodePlaylists);
  }

  bool ApplyPlaylist(
    string playlistId,
    Dictionary<string, MusicPlaylistDefinition> sourcePlaylists
  ) {
    if (string.Equals(activeZone, playlistId, StringComparison.OrdinalIgnoreCase)) {
      return activePlaylist != null;
    }

    activeZone = playlistId;
    RestoreVolume();
    trackIndex = 0;
    failedTrackCount = 0;
    waitingForBlackscreen = false;
    loadGeneration++;
    StopPlayback();

    var lookupId = playlistId.StartsWith(EpisodeRoutePrefix, StringComparison.OrdinalIgnoreCase)
      ? playlistId.Substring(EpisodeRoutePrefix.Length)
      : playlistId;
    if (sourcePlaylists == null || !sourcePlaylists.TryGetValue(lookupId, out activePlaylist)) {
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

    AsyncOperationHandle<AudioClip> handle;
    try {
      handle = Addressables.LoadAssetAsync<AudioClip>(address);
      activeClipHandle = handle;
      hasActiveClipHandle = true;
      handle.Completed += completedHandle => OnTrackLoaded(completedHandle, requestGeneration, address);
      return;
    }
    catch {
      if (AudioClipResolver.TryLoadEditorClip(address, out var editorClip)) {
        PrepareAudioData(editorClip, requestGeneration, address);
        return;
      }

      Debug.LogError(
        "[MusicPlayer] Failed to request track='" + address + "'."
      );
      return;
    }
  }

  void OnTrackLoaded(
    AsyncOperationHandle<AudioClip> handle,
    int requestGeneration,
    string address
  ) {
    if (requestGeneration != loadGeneration || !isActiveAndEnabled) {
      Addressables.Release(handle);
      hasActiveClipHandle = false;
      return;
    }

    if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null) {
      Addressables.Release(handle);
      hasActiveClipHandle = false;

      if (AudioClipResolver.TryLoadEditorClip(address, out var editorClip)) {
        PrepareAudioData(editorClip, requestGeneration, address);
        return;
      }

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
    trackIsPlaying = true;

    if (playbackSuspended) {
      playbackSourcePaused = false;
      suspendedTimeSamples = 0;
      return;
    }

    audioSource.Play();
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
    playbackSourcePaused = false;
    suspendedTimeSamples = 0;
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
