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
    public float volume;
    public float pitch;
  }

  sealed class ClipCacheEntry {
    public AsyncOperationHandle<AudioClip> handle;
    public AudioClip clip;
    public bool loading;
    public readonly List<PendingPlay> pending = new();
  }

  sealed class Voice {
    public AudioSource source;
    public long sequence;
  }

  public const string PlayMessage = "soundEffect.play";

  static SoundEffectPlayer runtimeInstance;

  [SerializeField] TextAsset manifestAsset;
  [SerializeField, Range(0f, 1f)] float masterVolume = 1f;
  [SerializeField, Min(1)] int maxVoices = 16;

  readonly List<Action> subscriptions = new();
  readonly List<Voice> voices = new();
  readonly Dictionary<string, ClipCacheEntry> clipCache =
    new(StringComparer.OrdinalIgnoreCase);

  Dictionary<string, SoundEffectDefinition> definitions;
  long voiceSequence;
  bool shuttingDown;

  public static void Play(string soundId) {
    Play(soundId, 1f, 1f);
  }

  public static void Play(string soundId, float volume, float pitch = 1f) {
    if (runtimeInstance == null) {
      Debug.LogWarning("[SoundEffectPlayer] Play ignored because no player is active.");
      return;
    }

    var request = new SoundEffectRequest(soundId, volume, pitch);
    runtimeInstance.RequestPlay(request);
  }

  void Awake() {
    SoundEffectManifestCatalog.TryBuildDefinitions(manifestAsset, out definitions);
  }

  void OnEnable() {
    runtimeInstance = this;
    subscriptions.Add(MessageBus.On(PlayMessage, OnPlayMessage));
  }

  void OnDisable() {
    if (ReferenceEquals(runtimeInstance, this)) {
      runtimeInstance = null;
    }

    Unsubscribe();
    StopVoices();
    ClearPendingPlays();
  }

  void OnDestroy() {
    shuttingDown = true;
    Unsubscribe();
    ReleaseLoadedClips();
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

  void RequestPlay(SoundEffectRequest request) {
    if (request == null || string.IsNullOrWhiteSpace(request.soundId)) {
      return;
    }

    var soundId = request.soundId.Trim();
    if (definitions == null || !definitions.TryGetValue(soundId, out var definition)) {
      Debug.LogWarning("[SoundEffectPlayer] Unknown sound id '" + soundId + "'.");
      return;
    }

    var pending = new PendingPlay {
      volume = ResolveVolume(definition.volume, request.volume),
      pitch = Mathf.Clamp(request.pitch, 0.1f, 3f)
    };

    RequestClip(definition.clipAddress, pending);
  }

  float ResolveVolume(float effectVolume, float requestedVolume) {
    var scaledVolume = masterVolume * effectVolume * Mathf.Max(requestedVolume, 0f);
    return Mathf.Clamp01(scaledVolume);
  }

  void RequestClip(string address, PendingPlay pending) {
    if (clipCache.TryGetValue(address, out var cached)) {
      if (cached.clip != null && !cached.loading) {
        PlayClip(cached.clip, pending);
        return;
      }

      cached.pending.Add(pending);
      return;
    }

    if (AudioClipResolver.TryLoadEditorClip(address, out var editorClip)) {
      var editorEntry = new ClipCacheEntry {
        clip = editorClip,
        loading = true
      };
      editorEntry.pending.Add(pending);
      clipCache.Add(address, editorEntry);
      PrepareClipData(address, editorEntry);
      return;
    }

    var entry = new ClipCacheEntry {
      loading = true
    };
    entry.pending.Add(pending);
    clipCache.Add(address, entry);

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
      return;
    }

    entry.handle = handle;
    handle.Completed += completedHandle => OnClipLoaded(address, entry, completedHandle);
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
      return;
    }

    for (var i = 0; i < entry.pending.Count; i++) {
      PlayClip(entry.clip, entry.pending[i]);
    }

    entry.pending.Clear();
  }

  void FailClipData(string address, ClipCacheEntry entry) {
    entry.loading = false;
    entry.pending.Clear();
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
    voice.sequence = ++voiceSequence;
    voice.source.Stop();
    voice.source.clip = clip;
    voice.source.volume = pending.volume;
    voice.source.pitch = pending.pitch;
    voice.source.Play();
  }

  Voice ResolveVoice() {
    for (var i = 0; i < voices.Count; i++) {
      if (!voices[i].source.isPlaying) {
        return voices[i];
      }
    }

    if (voices.Count < Mathf.Max(maxVoices, 1)) {
      return CreateVoice();
    }

    var oldest = voices[0];
    for (var i = 1; i < voices.Count; i++) {
      if (voices[i].sequence < oldest.sequence) {
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
      voices[i].source.Stop();
      voices[i].source.clip = null;
    }
  }

  void ClearPendingPlays() {
    foreach (var entry in clipCache.Values) {
      if (entry == null) {
        continue;
      }

      entry.pending.Clear();
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
