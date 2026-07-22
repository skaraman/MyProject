using UnityEngine;
using UnityEngine.InputSystem;

public class SettingsAudioPageController : MonoBehaviour {
  const string SettingsTitle = "Settings";
  const string AudioTitle = "Audio";

  GameObject buttons;
  GameObject audioPage;
  GameObject audioButton;
  GameObject closeButton;
  GameObject sfxSlider;
  Transform sfxHandle;
  BoxCollider2D sfxSliderCollider;
  FontText sfxPercentage;
  SoundEffectPlayer soundEffectPlayer;
  GameObject musicSlider;
  Transform musicHandle;
  BoxCollider2D musicSliderCollider;
  FontText musicPercentage;
  MusicPlayer musicPlayer;
  FontText title;
  AnimateFields gearAnimation;
  AnimateFields titleAnimation;
  bool isAudioPageOpen;
  bool isDraggingSfxSlider;
  bool isDraggingMusicSlider;
  bool resetHeaderOnNextLateUpdate;

  void Awake() {
    ResolveReferences();
    ShowButtons(resetHeader: true);
  }

  void OnEnable() {
    ResolveReferences();
    ShowButtons(resetHeader: true);
    resetHeaderOnNextLateUpdate = true;
  }

  void LateUpdate() {
    if (!resetHeaderOnNextLateUpdate) return;

    resetHeaderOnNextLateUpdate = false;
    ShowButtons(resetHeader: true);
  }

  void Update() {
    if (!isDraggingSfxSlider && !isDraggingMusicSlider) return;

    var mouse = Mouse.current;
    if (mouse == null || !mouse.leftButton.isPressed) {
      isDraggingSfxSlider = false;
      isDraggingMusicSlider = false;
      return;
    }

    var screenPosition = mouse.position.ReadValue();
    if (isDraggingSfxSlider) {
      UpdateSfxVolumeFromMouse(screenPosition);
    }
    if (isDraggingMusicSlider) {
      UpdateMusicVolumeFromMouse(screenPosition);
    }
  }

  void OnDisable() {
    isDraggingSfxSlider = false;
    isDraggingMusicSlider = false;
  }

  public bool TryHandleClick(GameObject target) {
    if (IsTargetOrChildOf(target, audioButton)) {
      ShowAudioPage();
      return true;
    }

    if (isAudioPageOpen && IsTargetOrChildOf(target, closeButton)) {
      ShowButtons(resetHeader: false);
      return true;
    }

    if (isAudioPageOpen && IsTargetOrChildOf(target, sfxSlider)) {
      var mouse = Mouse.current;
      if (mouse == null) return true;

      isDraggingSfxSlider = true;
      isDraggingMusicSlider = false;
      UpdateSfxVolumeFromMouse(mouse.position.ReadValue());
      return true;
    }

    if (isAudioPageOpen && IsTargetOrChildOf(target, musicSlider)) {
      var mouse = Mouse.current;
      if (mouse == null) return true;

      isDraggingSfxSlider = false;
      isDraggingMusicSlider = true;
      UpdateMusicVolumeFromMouse(mouse.position.ReadValue());
      return true;
    }

    return false;
  }

  public bool TryReturnToButtons() {
    if (!isAudioPageOpen) return false;

    ShowButtons(resetHeader: false);
    return true;
  }

  void ShowAudioPage() {
    if (isAudioPageOpen) return;

    MessageBus.Send(SoundEffectPlayer.PlayMessage, SoundEffectPlayer.MenuSelectSoundId);
    isAudioPageOpen = true;
    SetTitle(AudioTitle);
    SetActive(buttons, false);
    SetActive(audioPage, true);
    SyncSfxControl();
    SyncMusicControl();
    gearAnimation?.Play();
    titleAnimation?.Play();
  }

  void ShowButtons(bool resetHeader) {
    var wasAudioPageOpen = isAudioPageOpen;
    isAudioPageOpen = false;
    isDraggingSfxSlider = false;
    isDraggingMusicSlider = false;
    SetTitle(SettingsTitle);
    SetActive(audioPage, false);
    SetActive(buttons, true);

    if (resetHeader) {
      gearAnimation?.Stop();
      titleAnimation?.Stop();
      return;
    }

    if (wasAudioPageOpen) {
      MessageBus.Send(SoundEffectPlayer.PlayMessage, SoundEffectPlayer.MenuSelectSoundId);
      gearAnimation?.PlayReverse();
      titleAnimation?.PlayReverse();
    }
  }

  void ResolveReferences() {
    var root = transform;
    buttons ??= FindDirectChild(root, "Buttons");
    audioPage ??= FindDirectChild(root, "AudioPage");
    closeButton ??= FindDirectChild(root, "Close");

    if (buttons != null && audioButton == null) {
      audioButton = FindDirectChild(buttons.transform, "Audio");
    }

    var sfx = audioPage != null ? FindDirectChild(audioPage.transform, "SFX") : null;
    if (sfx != null) {
      sfxSlider ??= FindDirectChild(sfx.transform, "slider");
      var percentage = FindDirectChild(sfx.transform, "percentage");
      sfxPercentage ??= percentage != null
        ? percentage.GetComponentInChildren<FontText>(includeInactive: true)
        : null;
    }

    if (sfxSlider != null) {
      sfxHandle ??= FindDirectChild(sfxSlider.transform, "handle")?.transform;
      sfxSliderCollider ??= sfxSlider.GetComponent<BoxCollider2D>();
    }

    soundEffectPlayer ??= FindAnyObjectByType<SoundEffectPlayer>();

    var music = audioPage != null ? FindDirectChild(audioPage.transform, "MUSIC") : null;
    if (music != null) {
      musicSlider ??= FindDirectChild(music.transform, "slider");
      var percentage = FindDirectChild(music.transform, "percentage");
      musicPercentage ??= percentage != null
        ? percentage.GetComponentInChildren<FontText>(includeInactive: true)
        : null;
    }

    if (musicSlider != null) {
      musicHandle ??= FindDirectChild(musicSlider.transform, "handle")?.transform;
      musicSliderCollider ??= musicSlider.GetComponent<BoxCollider2D>();
    }

    musicPlayer ??= FindAnyObjectByType<MusicPlayer>();

    var ui = FindDirectChild(root, "UI");
    if (ui == null) return;

    var gear = FindDirectChild(ui.transform, "Gear");
    var header = FindDirectChild(ui.transform, "text");
    if (gear != null) {
      gearAnimation ??= gear.GetComponent<AnimateFields>();
    }
    if (header != null) {
      title ??= header.GetComponent<FontText>();
      titleAnimation ??= header.GetComponent<AnimateFields>();
    }
  }

  void SyncSfxControl() {
    ResolveReferences();
    if (soundEffectPlayer == null) return;

    ApplySfxVolume(soundEffectPlayer.MasterVolume);
  }

  void UpdateSfxVolumeFromMouse(Vector2 screenPosition) {
    if (!TryGetSfxSliderValue(screenPosition, out var volume)) return;

    ApplySfxVolume(volume);
  }

  bool TryGetSfxSliderValue(Vector2 screenPosition, out float volume) {
    return TryGetSliderValue(sfxSlider, sfxSliderCollider, screenPosition, out volume);
  }

  void SyncMusicControl() {
    ResolveReferences();
    if (musicPlayer == null) return;

    ApplyMusicVolume(musicPlayer.Volume);
  }

  void UpdateMusicVolumeFromMouse(Vector2 screenPosition) {
    if (!TryGetMusicSliderValue(screenPosition, out var volume)) return;

    ApplyMusicVolume(volume);
  }

  bool TryGetMusicSliderValue(Vector2 screenPosition, out float volume) {
    return TryGetSliderValue(musicSlider, musicSliderCollider, screenPosition, out volume);
  }

  bool TryGetSliderValue(
    GameObject slider,
    BoxCollider2D sliderCollider,
    Vector2 screenPosition,
    out float volume
  ) {
    volume = 0f;
    if (slider == null || sliderCollider == null) return false;

    var camera = Camera.main;
    if (camera == null) return false;

    var ray = camera.ScreenPointToRay(screenPosition);
    var plane = new Plane(slider.transform.forward, slider.transform.position);
    if (!plane.Raycast(ray, out var distance)) return false;

    var localPoint = slider.transform.InverseTransformPoint(ray.GetPoint(distance));
    var minimum = sliderCollider.offset.x - sliderCollider.size.x * 0.5f;
    var maximum = sliderCollider.offset.x + sliderCollider.size.x * 0.5f;
    volume = Mathf.InverseLerp(minimum, maximum, localPoint.x);
    return true;
  }

  void ApplySfxVolume(float volume) {
    ResolveReferences();
    volume = Mathf.Clamp01(volume);
    soundEffectPlayer?.SetMasterVolume(volume);
    ApplySliderVisual(sfxHandle, sfxSliderCollider, sfxPercentage, volume);
  }

  void ApplyMusicVolume(float volume) {
    ResolveReferences();
    volume = Mathf.Clamp01(volume);
    musicPlayer?.SetVolume(volume);
    ApplySliderVisual(musicHandle, musicSliderCollider, musicPercentage, volume);
  }

  void ApplySliderVisual(
    Transform handle,
    BoxCollider2D sliderCollider,
    FontText percentage,
    float volume
  ) {
    if (sliderCollider != null && handle != null) {
      var position = handle.localPosition;
      var minimum = sliderCollider.offset.x - sliderCollider.size.x * 0.5f;
      var maximum = sliderCollider.offset.x + sliderCollider.size.x * 0.5f;
      position.x = Mathf.Lerp(minimum, maximum, volume);
      handle.localPosition = position;
    }

    var value = Mathf.RoundToInt(volume * 100f).ToString() + "%";
    if (percentage != null && percentage.content != value) {
      percentage.content = value;
      percentage.Generate();
    }
  }

  void SetTitle(string value) {
    if (title == null || title.content == value) return;

    title.content = value;
    title.Generate();
  }

  static void SetActive(GameObject target, bool active) {
    if (target != null && target.activeSelf != active) {
      target.SetActive(active);
    }
  }

  static bool IsTargetOrChildOf(GameObject target, GameObject container) {
    return target != null &&
           container != null &&
           (target == container || target.transform.IsChildOf(container.transform));
  }

  static GameObject FindDirectChild(Transform parent, string name) {
    if (parent == null) return null;

    for (var i = 0; i < parent.childCount; i++) {
      var child = parent.GetChild(i);
      if (child.name == name) return child.gameObject;
    }

    return null;
  }
}
