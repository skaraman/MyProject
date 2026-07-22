using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class MainMenuTitleFlyOff : MonoBehaviour {
  public const string PlayMessage = "mainMenu.title.flyOff";

  static readonly string[] LetterNames = {
    "E1",
    "S",
    "P",
    "E2",
    "R",
    "A1",
    "N",
    "Z",
    "A2"
  };

  [SerializeField, Min(0.1f)] float minimumDuration = 1.15f;
  [SerializeField, Min(0.1f)] float maximumDuration = 1.65f;
  [SerializeField, Min(0f)] float maximumStartDelay = 0.18f;
  [SerializeField, Min(0f)] float minimumTravelDistance = 28f;
  [SerializeField, Min(0f)] float maximumTravelDistance = 38f;
  [SerializeField, Min(0f)] float minimumCurveDistance = 4f;
  [SerializeField, Min(0f)] float maximumCurveDistance = 9f;
  [SerializeField, Min(0f)] float minimumSpin = 20f;
  [SerializeField, Min(0f)] float maximumSpin = 85f;

  readonly List<Letter> letters = new();
  Action playMessageOff;
  float startedAt;
  bool isPlaying;

  sealed class Letter {
    public Transform transform;
    public TransformWrapper wrapper;
    public Vector3 basePosition;
    public Vector3 baseRotation;
    public Vector3 startPosition;
    public Vector3 startRotation;
    public Vector3 firstControlPoint;
    public Vector3 secondControlPoint;
    public Vector3 endPosition;
    public float delay;
    public float duration;
    public float spin;
  }

  void Awake() {
    CacheLetters();
  }

  void OnEnable() {
    if (letters.Count == 0) {
      CacheLetters();
    }

    RestoreLetters();
    playMessageOff = MessageBus.On(PlayMessage, Play);
  }

  void OnDisable() {
    playMessageOff?.Invoke();
    playMessageOff = null;
    isPlaying = false;
    RestoreLetters();
  }

  void LateUpdate() {
    if (!isPlaying) {
      return;
    }

    var elapsed = Time.realtimeSinceStartup - startedAt;

    for (var i = 0; i < letters.Count; i++) {
      var letter = letters[i];
      var letterElapsed = elapsed - letter.delay;

      if (letterElapsed < 0f) {
        ApplyPose(letter, letter.startPosition, letter.startRotation);
        continue;
      }

      var progress = Mathf.Clamp01(letterElapsed / letter.duration);
      var easedProgress = Mathf.SmoothStep(0f, 1f, progress);
      var position = CubicBezier(
        letter.startPosition,
        letter.firstControlPoint,
        letter.secondControlPoint,
        letter.endPosition,
        easedProgress
      );
      var rotation = letter.startRotation;
      rotation.z += letter.spin * easedProgress;

      ApplyPose(letter, position, rotation);
    }
  }

  void CacheLetters() {
    letters.Clear();

    for (var i = 0; i < LetterNames.Length; i++) {
      var letterTransform = transform.Find(LetterNames[i]);
      if (letterTransform == null) {
        continue;
      }

      var letter = new Letter();
      letter.transform = letterTransform;
      letter.wrapper = letterTransform.GetComponent<TransformWrapper>();
      letter.basePosition = letterTransform.localPosition;
      letter.baseRotation = letterTransform.localEulerAngles;
      letters.Add(letter);
    }
  }

  void Play(object _) {
    if (isPlaying) {
      return;
    }

    for (var i = 0; i < letters.Count; i++) {
      ConfigureTrajectory(letters[i]);
    }

    startedAt = Time.realtimeSinceStartup;
    isPlaying = letters.Count > 0;
  }

  void ConfigureTrajectory(Letter letter) {
    var direction = UnityEngine.Random.insideUnitCircle;
    if (direction.sqrMagnitude < 0.01f) {
      direction = Vector2.up;
    }
    direction.Normalize();

    var minimumDistance = Mathf.Max(minimumTravelDistance, 0f);
    var maximumDistance = Mathf.Max(maximumTravelDistance, minimumDistance);
    var distance = UnityEngine.Random.Range(minimumDistance, maximumDistance);

    var minimumCurve = Mathf.Max(minimumCurveDistance, 0f);
    var maximumCurve = Mathf.Max(maximumCurveDistance, minimumCurve);
    var curveDistance = UnityEngine.Random.Range(minimumCurve, maximumCurve);
    var curveDirection = UnityEngine.Random.value < 0.5f ? -1f : 1f;
    var perpendicular = new Vector2(-direction.y, direction.x);

    letter.startPosition = letter.transform.localPosition;
    letter.startRotation = letter.transform.localEulerAngles;
    letter.endPosition = letter.startPosition;
    letter.endPosition.x += direction.x * distance;
    letter.endPosition.y += direction.y * distance;
    letter.firstControlPoint = letter.startPosition;
    letter.firstControlPoint.x += direction.x * distance * 0.22f;
    letter.firstControlPoint.y += direction.y * distance * 0.22f;
    letter.firstControlPoint.x += perpendicular.x * curveDistance * curveDirection;
    letter.firstControlPoint.y += perpendicular.y * curveDistance * curveDirection;

    var secondCurveDistance = curveDistance * 0.75f;
    letter.secondControlPoint = letter.startPosition;
    letter.secondControlPoint.x += direction.x * distance * 0.72f;
    letter.secondControlPoint.y += direction.y * distance * 0.72f;
    letter.secondControlPoint.x += perpendicular.x * secondCurveDistance * curveDirection;
    letter.secondControlPoint.y += perpendicular.y * secondCurveDistance * curveDirection;

    var minimumTime = Mathf.Max(minimumDuration, 0.1f);
    var maximumTime = Mathf.Max(maximumDuration, minimumTime);
    letter.duration = UnityEngine.Random.Range(minimumTime, maximumTime);
    letter.delay = UnityEngine.Random.Range(0f, Mathf.Max(maximumStartDelay, 0f));

    var minimumRotation = Mathf.Max(minimumSpin, 0f);
    var maximumRotation = Mathf.Max(maximumSpin, minimumRotation);
    var spinDirection = UnityEngine.Random.value < 0.5f ? -1f : 1f;
    letter.spin = UnityEngine.Random.Range(minimumRotation, maximumRotation);
    letter.spin *= spinDirection;
  }

  void RestoreLetters() {
    for (var i = 0; i < letters.Count; i++) {
      var letter = letters[i];
      ApplyPose(letter, letter.basePosition, letter.baseRotation);
    }
  }

  static Vector3 CubicBezier(
    Vector3 start,
    Vector3 firstControl,
    Vector3 secondControl,
    Vector3 end,
    float progress
  ) {
    var inverse = 1f - progress;
    var inverseSquared = inverse * inverse;
    var progressSquared = progress * progress;
    var startWeight = inverseSquared * inverse;
    var firstControlWeight = 3f * inverseSquared * progress;
    var secondControlWeight = 3f * inverse * progressSquared;
    var endWeight = progressSquared * progress;

    var startContribution = start * startWeight;
    var firstControlContribution = firstControl * firstControlWeight;
    var secondControlContribution = secondControl * secondControlWeight;
    var endContribution = end * endWeight;
    var position = startContribution + firstControlContribution;
    position += secondControlContribution;
    position += endContribution;
    return position;
  }

  static void ApplyPose(Letter letter, Vector3 position, Vector3 rotation) {
    if (letter.wrapper != null) {
      letter.wrapper.x = position.x;
      letter.wrapper.y = position.y;
      letter.wrapper.z = position.z;
      letter.wrapper.rx = rotation.x;
      letter.wrapper.ry = rotation.y;
      letter.wrapper.rz = rotation.z;
    }

    letter.transform.localPosition = position;
    letter.transform.localRotation = Quaternion.Euler(rotation);
  }
}
