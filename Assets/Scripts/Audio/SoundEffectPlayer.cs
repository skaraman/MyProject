using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

[Serializable]
public sealed class SoundEffectRequest {
  public string soundId;
  public float volume = 1f;
  public float pitch = 1f;

  public SoundEffectRequest(string soundId, float volume = 1f, float pitch = 1f) {
    this.soundId = soundId;
    this.volume = volume;
    this.pitch = pitch;
  }
}

public sealed class SoundEffectPlayer : MonoBehaviour {
  sealed class PendingPlay {
    public long sequence;
    public float effectVolume;
    public float requestedVolume;
    public float pitch;
    public bool canPlayDuringPauseMenu;
  }

  sealed class PendingLoop {
    public LoopState loop;
    public int version;
  }

  sealed class ClipCacheEntry {
    public AsyncOperationHandle<AudioClip> handle;
    public AudioClip clip;
    public bool loading;
    public readonly List<PendingPlay> pending = new();
    public readonly List<PendingLoop> pendingLoops = new();
  }

  sealed class LoopState {
    public string soundId;
    public string address;
    public float effectVolume;
    public float requestedVolume;
    public float pitch;
    public float playChance;
    public float nextPlayTime;
    public bool previousIntermittentAttemptPlayed;
    public float playStartTime;
    public float fadeInEndTime;
    public float fadeOutStartTime;
    public float fadeOutEndTime;
    public bool stopping;
    public bool playbackSuspended;
    public bool sourcePausedForSuspension;
    public float playbackSuspendedAt;
    public AudioSource source;
    public int version;
  }

  sealed class Voice {
    public AudioSource source;
    public long sequence;
    public float effectVolume;
    public float requestedVolume;
    public float playStartTime;
    public float fadeInEndTime;
    public float fadeOutStartTime;
    public float fadeOutEndTime;
    public bool canPlayDuringPauseMenu;
    public bool playbackSuspended;
    public float playbackSuspendedAt;
  }

  public const string PlayMessage = "soundEffect.play";
  public const string MenuSelectSoundId = "menu.select";
  public const string EsperanzaHurt1SoundId = "esperanza.hurt1";
  public const string EsperanzaHurt2SoundId = "esperanza.hurt2";
  const string Episode1_1AmbienceSoundId = "ambience.episode1.1";
  const string Episode1_1AmbienceLoopId = "episode.ambience";
  const string Episode1_1Id = "Episode1.1";
  const float GameplayCriticalPreloadTimeoutSeconds = 2f;

  static SoundEffectPlayer runtimeInstance;

  [SerializeField] TextAsset manifestAsset;
  [SerializeField, Range(0f, 1f)] float masterVolume = 1f;
  [SerializeField, Min(0f)] float fadeDuration = 0.1f;
  [SerializeField, Min(1)] int maxVoices = 16;

  readonly List<Action> subscriptions = new();
  readonly List<Voice> voices = new();
  readonly Dictionary<string, ClipCacheEntry> clipCache =
    new(StringComparer.OrdinalIgnoreCase);
  readonly Dictionary<string, LoopState> loops =
    new(StringComparer.Ordinal);

  Dictionary<string, SoundEffectDefinition> definitions;
  long voiceSequence;
  bool shuttingDown;
  bool applicationFocused = true;
  bool applicationPaused;
  bool pauseMenuOpen;
  bool episodeAmbienceGameplayVisible;
  int episodeAmbienceRevision = -1;
  int episodeAmbienceRegistryVersion = -1;

  public float MasterVolume => masterVolume;

  bool IsApplicationSuspended => !applicationFocused || applicationPaused;
  bool IsPlaybackSuspended => pauseMenuOpen || IsApplicationSuspended;

  public static long Play(string soundId) {
    return Play(soundId, 1f, 1f);
  }

  public static long Play(string soundId, float volume, float pitch = 1f) {
    if (runtimeInstance == null) {
      Debug.LogWarning("[SoundEffectPlayer] Play ignored because no player is active.");
      return 0;
    }

    var request = new SoundEffectRequest(soundId, volume, pitch);
    return runtimeInstance.RequestPlay(request);
  }

  public static void Stop(long sequence) {
    if (runtimeInstance != null) {
      runtimeInstance.StopSequenceInternal(sequence);
    }
  }

  public static IEnumerator PreloadGameplayCriticalClips() {
    var player = runtimeInstance;
    if (player == null || !player.isActiveAndEnabled) {
      yield break;
    }

    var hurt1 = player.GetOrRequestDefinedClip(EsperanzaHurt1SoundId);
    var hurt2 = player.GetOrRequestDefinedClip(EsperanzaHurt2SoundId);
    var deadline = Time.realtimeSinceStartup + GameplayCriticalPreloadTimeoutSeconds;
    while (ReferenceEquals(runtimeInstance, player) &&
           player.isActiveAndEnabled &&
           Time.realtimeSinceStartup < deadline &&
           ((hurt1 != null && hurt1.loading) ||
            (hurt2 != null && hurt2.loading))) {
      yield return null;
    }
  }

  public static bool SetLoop(
    string loopId,
    string soundId,
    float volume = 1f,
    float pitch = 1f
  ) {
    if (runtimeInstance == null) {
      return false;
    }

    if (string.IsNullOrWhiteSpace(soundId)) {
      runtimeInstance.StopLoopInternal(loopId);
      return true;
    }

    return runtimeInstance.RequestLoop(loopId, soundId, 1f, volume, pitch);
  }

  public static bool SetIntermittentLoop(
    string loopId,
    string soundId,
    float playChance,
    float volume = 1f,
    float pitch = 1f
  ) {
    if (runtimeInstance == null) {
      return false;
    }

    if (string.IsNullOrWhiteSpace(soundId)) {
      runtimeInstance.StopLoopInternal(loopId);
      return true;
    }

    return runtimeInstance.RequestLoop(loopId, soundId, playChance, volume, pitch);
  }

  public static void StopLoop(string loopId) {
    runtimeInstance?.StopLoopInternal(loopId);
  }

  public void SetMasterVolume(float value) {
    masterVolume = Mathf.Clamp01(value);

    for (var i = 0; i < voices.Count; i++) {
      var voice = voices[i];
      if (voice?.source == null) continue;

      ApplyVoiceSettings(voice);
    }

    foreach (var loop in loops.Values) {
      if (loop?.source == null) continue;

      ApplyLoopSettings(loop);
    }
  }

  void Awake() {
    SoundEffectManifestCatalog.TryBuildDefinitions(manifestAsset, out definitions);
  }

  void Update() {
    if (IsPlaybackSuspended) {
      return;
    }

    UpdateEpisodeAmbience();

    for (var i = 0; i < voices.Count; i++) {
      ApplyVoiceSettings(voices[i]);
    }

    foreach (var loop in loops.Values) {
      UpdateLoopFade(loop);
      TryPlayIntermittentLoop(loop);
    }
  }

  void OnEnable() {
    runtimeInstance = this;
    applicationFocused = Application.isFocused;
    applicationPaused = false;
    pauseMenuOpen = SingleSceneManager.IsPauseMenuActive;
    subscriptions.Add(MessageBus.On(PlayMessage, OnPlayMessage));
    subscriptions.Add(MessageBus.On(AudioPlaybackMessages.PauseMenuOpened, OnPauseMenuOpened));
    subscriptions.Add(MessageBus.On(AudioPlaybackMessages.PauseMenuClosed, OnPauseMenuClosed));
    if (!IsPlaybackSuspended) {
      UpdateEpisodeAmbience();
    }
  }

  void OnDisable() {
    if (ReferenceEquals(runtimeInstance, this)) {
      runtimeInstance = null;
    }

    Unsubscribe();
    episodeAmbienceGameplayVisible = false;
    episodeAmbienceRevision = -1;
    episodeAmbienceRegistryVersion = -1;
    StopVoices();
    StopLoops();
    ClearPendingPlays();
  }

  void OnPauseMenuOpened(object payload) {
    if (pauseMenuOpen) {
      return;
    }

    pauseMenuOpen = true;
    ClearPendingPlays();
    RefreshActiveSourceSuspension();
  }

  void OnPauseMenuClosed(object payload) {
    if (!pauseMenuOpen) {
      return;
    }

    pauseMenuOpen = false;
    RefreshActiveSourceSuspension();
  }

  void OnApplicationFocus(bool hasFocus) {
    applicationFocused = hasFocus;
    RefreshActiveSourceSuspension();
  }

  void OnApplicationPause(bool isPaused) {
    applicationPaused = isPaused;
    RefreshActiveSourceSuspension();
  }

  void RefreshActiveSourceSuspension() {
    for (var i = 0; i < voices.Count; i++) {
      var voice = voices[i];
      RefreshVoiceSuspension(
        voice,
        IsApplicationSuspended || (pauseMenuOpen && !voice.canPlayDuringPauseMenu)
      );
    }

    foreach (var loop in loops.Values) {
      RefreshLoopSuspension(loop);
    }
  }

  void RefreshVoiceSuspension(Voice voice, bool shouldSuspend) {
    if (voice?.source == null) {
      return;
    }

    if (shouldSuspend) {
      if (!voice.playbackSuspended && voice.source.isPlaying) {
        voice.source.Pause();
        voice.playbackSuspended = true;
        voice.playbackSuspendedAt = Time.unscaledTime;
      }
      return;
    }

    if (!voice.playbackSuspended) {
      return;
    }

    var suspendedDuration = Mathf.Max(
      Time.unscaledTime - voice.playbackSuspendedAt,
      0f
    );
    voice.playbackSuspended = false;
    ShiftFadeTimes(voice, suspendedDuration);
    if (voice.source.clip != null) {
      voice.source.UnPause();
    }
  }

  void RefreshLoopSuspension(LoopState loop) {
    if (loop?.source == null) {
      return;
    }

    if (IsPlaybackSuspended) {
      if (!loop.playbackSuspended &&
          (loop.source.isPlaying || loop.nextPlayTime > 0f)) {
        loop.sourcePausedForSuspension = loop.source.isPlaying;
        if (loop.sourcePausedForSuspension) {
          loop.source.Pause();
        }
        loop.playbackSuspended = true;
        loop.playbackSuspendedAt = Time.unscaledTime;
      }
      return;
    }

    if (!loop.playbackSuspended) {
      return;
    }

    var suspendedDuration = Mathf.Max(
      Time.unscaledTime - loop.playbackSuspendedAt,
      0f
    );
    loop.playbackSuspended = false;
    if (loop.nextPlayTime > 0f) {
      loop.nextPlayTime += suspendedDuration;
    }
    ShiftFadeTimes(loop, suspendedDuration);
    if (loop.sourcePausedForSuspension && loop.source.clip != null) {
      loop.source.UnPause();
    }
    loop.sourcePausedForSuspension = false;
  }

  static void ShiftFadeTimes(Voice voice, float duration) {
    voice.playStartTime += duration;
    voice.fadeInEndTime += duration;
    voice.fadeOutStartTime += duration;
    voice.fadeOutEndTime += duration;
  }

  static void ShiftFadeTimes(LoopState loop, float duration) {
    loop.playStartTime += duration;
    loop.fadeInEndTime += duration;
    loop.fadeOutStartTime += duration;
    loop.fadeOutEndTime += duration;
  }

  void OnDestroy() {
    shuttingDown = true;
    Unsubscribe();
    ReleaseLoadedClips();
  }

  void UpdateEpisodeAmbience() {
    var gameplayVisible = SingleSceneManager.IsGameplayActive &&
      SingleSceneManager.IsBlackscreenFullyTransparent;
    var episodeRevision = gameplayVisible ? ContentEpisodeProgression.EpisodeRevision : -1;
    var registryVersion = gameplayVisible ? ActiveContentRegistryRuntime.ReloadVersion : -1;
    if (episodeAmbienceGameplayVisible == gameplayVisible &&
        episodeAmbienceRevision == episodeRevision &&
        episodeAmbienceRegistryVersion == registryVersion) {
      return;
    }

    episodeAmbienceGameplayVisible = gameplayVisible;
    episodeAmbienceRevision = episodeRevision;
    episodeAmbienceRegistryVersion = registryVersion;
    var soundId = gameplayVisible && string.Equals(
      ContentEpisodeProgression.ResolveCurrentEpisodeId(),
      Episode1_1Id,
      StringComparison.OrdinalIgnoreCase
    )
      ? Episode1_1AmbienceSoundId
      : null;
    SetLoop(Episode1_1AmbienceLoopId, soundId);
  }

  void Unsubscribe() {
    for (var i = 0; i < subscriptions.Count; i++) {
      subscriptions[i]?.Invoke();
    }

    subscriptions.Clear();
  }

  void OnPlayMessage(object payload) {
    if (payload is SoundEffectRequest request) {
      RequestPlay(request);
      return;
    }

    if (payload is string soundId) {
      RequestPlay(new SoundEffectRequest(soundId));
      return;
    }

    Debug.LogError("[SoundEffectPlayer] Invalid soundEffect.play payload.");
  }

  long RequestPlay(SoundEffectRequest request) {
    if (request == null || string.IsNullOrWhiteSpace(request.soundId)) {
      return 0;
    }

    var soundId = request.soundId.Trim();
    var canPlayDuringPauseMenu = string.Equals(
      soundId,
      "menu.move",
      StringComparison.OrdinalIgnoreCase
    );
    if (IsApplicationSuspended || (pauseMenuOpen && !canPlayDuringPauseMenu)) {
      return 0;
    }

    if (definitions == null || !definitions.TryGetValue(soundId, out var definition)) {
      Debug.LogWarning("[SoundEffectPlayer] Unknown sound id '" + soundId + "'.");
      return 0;
    }

    var seq = ++voiceSequence;
    var pending = new PendingPlay {
      sequence = seq,
      effectVolume = definition.volume,
      requestedVolume = Mathf.Max(request.volume, 0f),
      pitch = Mathf.Clamp(request.pitch, 0.1f, 3f),
      canPlayDuringPauseMenu = canPlayDuringPauseMenu
    };

    RequestClip(definition.clipAddress, pending);
    return seq;
  }

  float ResolveVolume(float effectVolume, float requestedVolume) {
    var scaledVolume = masterVolume * effectVolume * Mathf.Max(requestedVolume, 0f);
    return Mathf.Clamp01(scaledVolume);
  }

  void RequestClip(string address, PendingPlay pending) {
    var entry = GetOrRequestClip(address);
    if (entry == null) {
      return;
    }

    if (entry.clip != null && !entry.loading) {
      PlayClip(entry.clip, pending);
      return;
    }

    entry.pending.Add(pending);
  }

  ClipCacheEntry GetOrRequestDefinedClip(string soundId) {
    if (definitions == null ||
        !definitions.TryGetValue(soundId, out var definition)) {
      return null;
    }

    return GetOrRequestClip(definition.clipAddress);
  }

  ClipCacheEntry GetOrRequestClip(string address) {
    if (clipCache.TryGetValue(address, out var cached)) {
      return cached;
    }

    var entry = new ClipCacheEntry {
      loading = true
    };
    clipCache.Add(address, entry);

    if (AudioClipResolver.TryLoadEditorClip(address, out var editorClip)) {
      entry.clip = editorClip;
      PrepareClipData(address, entry);
      if (!clipCache.TryGetValue(address, out var preparedEntry)) {
        return null;
      }

      if (!ReferenceEquals(preparedEntry, entry)) {
        return null;
      }

      return entry;
    }

    AsyncOperationHandle<AudioClip> handle;
    try {
      handle = Addressables.LoadAssetAsync<AudioClip>(address);
    }
    catch (Exception exception) {
      clipCache.Remove(address);
      Debug.LogError(
        "[SoundEffectPlayer] Failed to request clip='" + address +
        "'. error='" + exception.Message + "'"
      );
      return null;
    }

    entry.handle = handle;
    handle.Completed += completedHandle => OnClipLoaded(address, entry, completedHandle);
    if (!clipCache.TryGetValue(address, out var activeEntry)) {
      return null;
    }

    if (!ReferenceEquals(activeEntry, entry)) {
      return null;
    }

    return entry;
  }

  void OnClipLoaded(
    string address,
    ClipCacheEntry entry,
    AsyncOperationHandle<AudioClip> handle
  ) {
    if (shuttingDown) {
      Addressables.Release(handle);
      return;
    }

    if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null) {
      Addressables.Release(handle);
      entry.pending.Clear();
      entry.pendingLoops.Clear();
      clipCache.Remove(address);
      Debug.LogError("[SoundEffectPlayer] Failed to load clip '" + address + "'.");
      return;
    }

    entry.clip = handle.Result;
    PrepareClipData(address, entry);
  }

  void PrepareClipData(string address, ClipCacheEntry entry) {
    if (entry.clip.loadState == AudioDataLoadState.Loaded) {
      CompleteClipLoad(entry);
      return;
    }

    if (entry.clip.loadState == AudioDataLoadState.Unloaded &&
        !entry.clip.LoadAudioData()) {
      FailClipData(address, entry);
      return;
    }

    StartCoroutine(WaitForClipData(address, entry));
  }

  IEnumerator WaitForClipData(string address, ClipCacheEntry entry) {
    while (entry.clip.loadState == AudioDataLoadState.Loading) {
      yield return null;
    }

    if (entry.clip.loadState != AudioDataLoadState.Loaded) {
      FailClipData(address, entry);
      yield break;
    }

    CompleteClipLoad(entry);
  }

  void CompleteClipLoad(ClipCacheEntry entry) {
    entry.loading = false;
    if (!isActiveAndEnabled) {
      entry.pending.Clear();
      entry.pendingLoops.Clear();
      return;
    }

    for (var i = 0; i < entry.pending.Count; i++) {
      var pending = entry.pending[i];
      if (IsApplicationSuspended || (pauseMenuOpen && !pending.canPlayDuringPauseMenu)) {
        continue;
      }

      PlayClip(entry.clip, pending);
    }

    entry.pending.Clear();
    if (pauseMenuOpen) {
      entry.pendingLoops.Clear();
      return;
    }

    for (var i = 0; i < entry.pendingLoops.Count; i++) {
      StartPendingLoop(entry.clip, entry.pendingLoops[i]);
    }

    entry.pendingLoops.Clear();
  }

  void FailClipData(string address, ClipCacheEntry entry) {
    entry.loading = false;
    entry.pending.Clear();
    entry.pendingLoops.Clear();
    clipCache.Remove(address);
    if (entry.handle.IsValid()) {
      Addressables.Release(entry.handle);
    }

    Debug.LogError(
      "[SoundEffectPlayer] Audio data failed to load for clip '" + address + "'."
    );
  }

  void PlayClip(AudioClip clip, PendingPlay pending) {
    var voice = ResolveVoice();
    if (voice == null) {
      return;
    }

    voice.sequence = pending.sequence > 0 ? pending.sequence : ++voiceSequence;
    voice.canPlayDuringPauseMenu = pending.canPlayDuringPauseMenu;
    voice.playbackSuspended = false;
    voice.effectVolume = pending.effectVolume;
    voice.requestedVolume = pending.requestedVolume;
    voice.source.Stop();
    voice.source.clip = clip;
    voice.source.pitch = pending.pitch;
    if (pauseMenuOpen && pending.canPlayDuringPauseMenu) {
      voice.source.volume = ResolveVolume(voice.effectVolume, voice.requestedVolume);
    }
    else {
      BeginVoiceFade(voice);
    }
    voice.source.Play();
  }

  bool RequestLoop(
    string loopId,
    string soundId,
    float playChance,
    float volume,
    float pitch
  ) {
    if (IsPlaybackSuspended || string.IsNullOrWhiteSpace(loopId)) {
      return false;
    }

    var normalizedSoundId = soundId.Trim();
    if (definitions == null || !definitions.TryGetValue(normalizedSoundId, out var definition)) {
      Debug.LogWarning("[SoundEffectPlayer] Unknown sound id '" + normalizedSoundId + "'.");
      return false;
    }

    var normalizedPlayChance = Mathf.Clamp01(playChance);
    var normalizedLoopId = loopId.Trim();
    if (!loops.TryGetValue(normalizedLoopId, out var loop)) {
      loop = CreateLoop();
      loops.Add(normalizedLoopId, loop);
    }

    loop.effectVolume = definition.volume;
    loop.requestedVolume = Mathf.Max(volume, 0f);
    loop.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
    ApplyLoopSettings(loop);

    var sameSound = string.Equals(
      loop.soundId,
      normalizedSoundId,
      StringComparison.OrdinalIgnoreCase
    );
    var samePlayChance = Mathf.Approximately(loop.playChance, normalizedPlayChance);
    loop.playChance = normalizedPlayChance;
    if (sameSound && samePlayChance) {
      return true;
    }

    loop.version++;
    loop.soundId = normalizedSoundId;
    loop.address = definition.clipAddress;
    loop.stopping = false;
    loop.previousIntermittentAttemptPlayed = false;
    loop.source.Stop();
    loop.nextPlayTime = 0f;
    if (sameSound && loop.source.clip != null) {
      PlayLoopClip(loop, loop.source.clip);
      return true;
    }

    loop.source.clip = null;
    RequestLoopClip(loop);
    return true;
  }

  void RequestLoopClip(LoopState loop) {
    var entry = GetOrRequestClip(loop.address);
    if (entry == null) {
      return;
    }

    if (entry.clip != null && !entry.loading) {
      PlayLoopClip(loop, entry.clip);
      return;
    }

    entry.pendingLoops.Add(new PendingLoop {
      loop = loop,
      version = loop.version
    });
  }

  void StartPendingLoop(AudioClip clip, PendingLoop pending) {
    if (pending == null || pending.loop == null) {
      return;
    }

    if (pending.loop.version != pending.version) {
      return;
    }

    if (string.IsNullOrWhiteSpace(pending.loop.soundId)) {
      return;
    }

    PlayLoopClip(pending.loop, clip);
  }

  void PlayLoopClip(LoopState loop, AudioClip clip) {
    loop.source.clip = clip;
    loop.source.loop = Mathf.Approximately(loop.playChance, 1f);
    if (loop.source.loop) {
      BeginLoopPlayback(loop);
      return;
    }

    loop.nextPlayTime = Time.unscaledTime;
    TryPlayIntermittentLoop(loop);
  }

  void TryPlayIntermittentLoop(LoopState loop) {
    if (IsPlaybackSuspended ||
        loop == null ||
        string.IsNullOrWhiteSpace(loop.soundId) ||
        loop.source == null ||
        loop.source.clip == null ||
        Mathf.Approximately(loop.playChance, 1f) ||
        Time.unscaledTime < loop.nextPlayTime) {
      return;
    }

    var interval = Mathf.Max(loop.source.clip.length / loop.pitch, 0.01f);
    loop.nextPlayTime = Time.unscaledTime + interval;
    var passedRoll = UnityEngine.Random.value < loop.playChance;
    if (!passedRoll || loop.previousIntermittentAttemptPlayed) {
      loop.previousIntermittentAttemptPlayed = false;
      loop.source.Stop();
      return;
    }

    loop.previousIntermittentAttemptPlayed = true;
    BeginLoopPlayback(loop);
  }

  void ApplyLoopSettings(LoopState loop) {
    if (loop == null || loop.source == null) {
      return;
    }

    loop.source.volume = ResolveVolume(loop.effectVolume, loop.requestedVolume) *
      ResolveFadeMultiplier(
        loop.playStartTime,
        loop.fadeInEndTime,
        loop.fadeOutStartTime,
        loop.fadeOutEndTime
      );
    loop.source.pitch = loop.pitch;
  }

  void ApplyVoiceSettings(Voice voice) {
    if (voice == null || voice.source == null || !voice.source.isPlaying) {
      return;
    }

    voice.source.volume = ResolveVolume(voice.effectVolume, voice.requestedVolume) *
      ResolveFadeMultiplier(
        voice.playStartTime,
        voice.fadeInEndTime,
        voice.fadeOutStartTime,
        voice.fadeOutEndTime
      );
  }

  void BeginVoiceFade(Voice voice) {
    var playbackDuration = ResolvePlaybackDuration(voice.source.clip, voice.source.pitch);
    var rampDuration = ResolveRampDuration(playbackDuration);
    var now = Time.unscaledTime;
    voice.playStartTime = now;
    voice.fadeInEndTime = now + rampDuration;
    voice.fadeOutStartTime = now + playbackDuration - rampDuration;
    voice.fadeOutEndTime = now + playbackDuration;
    voice.source.volume = 0f;
  }

  void BeginLoopPlayback(LoopState loop) {
    var playbackDuration = ResolvePlaybackDuration(loop.source.clip, loop.source.pitch);
    var rampDuration = ResolveRampDuration(playbackDuration);
    var now = Time.unscaledTime;
    loop.stopping = false;
    loop.playbackSuspended = false;
    loop.sourcePausedForSuspension = false;
    loop.playStartTime = now;
    loop.fadeInEndTime = now + rampDuration;
    loop.fadeOutStartTime = loop.source.loop
      ? float.PositiveInfinity
      : now + playbackDuration - rampDuration;
    loop.fadeOutEndTime = loop.source.loop
      ? float.PositiveInfinity
      : now + playbackDuration;
    loop.source.volume = 0f;
    loop.source.Play();
    RefreshLoopSuspension(loop);
  }

  void UpdateLoopFade(LoopState loop) {
    if (loop == null || loop.source == null) {
      return;
    }

    if (loop.stopping &&
        (!loop.source.isPlaying || Time.unscaledTime >= loop.fadeOutEndTime)) {
      StopLoopSource(loop);
      return;
    }

    if (!loop.source.isPlaying) {
      return;
    }

    ApplyLoopSettings(loop);
  }

  float ResolveFadeMultiplier(
    float playStartTime,
    float fadeInEndTime,
    float fadeOutStartTime,
    float fadeOutEndTime
  ) {
    var now = Time.unscaledTime;
    var multiplier = 1f;
    if (fadeInEndTime > playStartTime) {
      multiplier = Mathf.Min(
        multiplier,
        Mathf.InverseLerp(playStartTime, fadeInEndTime, now)
      );
    }

    if (now >= fadeOutStartTime && fadeOutEndTime > fadeOutStartTime) {
      multiplier = Mathf.Min(
        multiplier,
        1f - Mathf.InverseLerp(fadeOutStartTime, fadeOutEndTime, now)
      );
    }

    return multiplier;
  }

  float ResolvePlaybackDuration(AudioClip clip, float pitch) {
    if (clip == null) {
      return 0f;
    }

    return Mathf.Max(clip.length / Mathf.Max(pitch, 0.1f), 0.01f);
  }

  float ResolveRampDuration(float playbackDuration) {
    return Mathf.Min(Mathf.Max(fadeDuration, 0f), playbackDuration * 0.5f);
  }

  LoopState CreateLoop() {
    var source = gameObject.AddComponent<AudioSource>();
    source.playOnAwake = false;
    source.loop = false;
    source.spatialBlend = 0f;
    source.ignoreListenerPause = true;

    return new LoopState {
      source = source
    };
  }

  void StopLoopInternal(string loopId) {
    if (string.IsNullOrWhiteSpace(loopId)) {
      return;
    }

    var normalizedLoopId = loopId.Trim();
    if (!loops.TryGetValue(normalizedLoopId, out var loop)) {
      return;
    }

    if (string.IsNullOrEmpty(loop.soundId)) {
      return;
    }

    loop.version++;
    loop.soundId = null;
    loop.address = null;
    loop.nextPlayTime = 0f;
    loop.previousIntermittentAttemptPlayed = false;
    if (loop.source == null || !loop.source.isPlaying) {
      StopLoopSource(loop);
      return;
    }

    loop.stopping = true;
    loop.fadeOutStartTime = Time.unscaledTime;
    loop.fadeOutEndTime = loop.fadeOutStartTime +
      ResolveRampDuration(ResolvePlaybackDuration(loop.source.clip, loop.source.pitch));
    ApplyLoopSettings(loop);
  }

  void StopLoopSource(LoopState loop) {
    loop.stopping = false;
    loop.playbackSuspended = false;
    loop.sourcePausedForSuspension = false;
    if (loop.source == null) {
      return;
    }

    loop.source.Stop();
    loop.source.clip = null;
  }

  void StopSequenceInternal(long sequence) {
    if (sequence == 0) return;

    for (var i = 0; i < voices.Count; i++) {
      if (voices[i].sequence == sequence) {
        voices[i].source.Stop();
        return;
      }
    }

    foreach (var entry in clipCache.Values) {
      for (var i = 0; i < entry.pending.Count; i++) {
        if (entry.pending[i].sequence == sequence) {
          entry.pending.RemoveAt(i);
          return;
        }
      }
    }
  }

  Voice ResolveVoice() {
    for (var i = 0; i < voices.Count; i++) {
      if (!voices[i].source.isPlaying && !voices[i].playbackSuspended) {
        return voices[i];
      }
    }

    if (voices.Count < Mathf.Max(maxVoices, 1)) {
      return CreateVoice();
    }

    Voice oldest = null;
    for (var i = 0; i < voices.Count; i++) {
      if (voices[i].playbackSuspended) {
        continue;
      }

      if (oldest == null || voices[i].sequence < oldest.sequence) {
        oldest = voices[i];
      }
    }

    return oldest;
  }

  Voice CreateVoice() {
    var source = gameObject.AddComponent<AudioSource>();
    source.playOnAwake = false;
    source.loop = false;
    source.spatialBlend = 0f;
    source.ignoreListenerPause = true;

    var voice = new Voice {
      source = source
    };
    voices.Add(voice);
    return voice;
  }

  void StopVoices() {
    for (var i = 0; i < voices.Count; i++) {
      voices[i].playbackSuspended = false;
      voices[i].source.Stop();
      voices[i].source.clip = null;
    }
  }

  void StopLoops() {
    foreach (var loop in loops.Values) {
      loop.version++;
      loop.soundId = null;
      loop.address = null;
      loop.nextPlayTime = 0f;
      loop.previousIntermittentAttemptPlayed = false;
      loop.stopping = false;
      loop.playbackSuspended = false;
      loop.sourcePausedForSuspension = false;
      loop.source.Stop();
      loop.source.clip = null;
    }
  }

  void ClearPendingPlays() {
    foreach (var entry in clipCache.Values) {
      if (entry == null) {
        continue;
      }

      entry.pending.Clear();
      entry.pendingLoops.Clear();
    }
  }

  void ReleaseLoadedClips() {
    foreach (var entry in clipCache.Values) {
      if (entry == null || entry.clip == null || !entry.handle.IsValid()) {
        continue;
      }

      Addressables.Release(entry.handle);
    }

    clipCache.Clear();
  }
}
