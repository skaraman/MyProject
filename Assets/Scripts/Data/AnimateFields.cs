using System;
using System.Collections.Generic;
using UnityEngine;
using CustomInspector;
using System.Reflection;

[ExecuteAlways]
[Serializable]
public class AnimateFields : MonoBehaviour {
  [Button(nameof(Play), label = "Play", size = Size.small)]
  [Button(nameof(Stop), label = "Stop", size = Size.small)]
  [Button(nameof(Restart), label = "Restart", size = Size.small)]
  [Button(nameof(Reset), label = "Reset", size = Size.small)][HideField] public bool _bool1;
  [FixedValues("To", "By")] public string type;
  public Component target;
  public string trigger;
  public bool loop = true;
  public List<SequenceStep> sequence = new();
  public bool paused = true;
  public Action callback;
  public int timeScaleIndex = 1;

  private Action triggerOff;
  private float timer;
  private int sequenceIt = 0;
  private List<IFieldAnimation> fieldAnimations = new();
  private bool typeIsBy;
  private int sequenceCount;
  private bool hasValidTarget;
  private float currentTimeScale;
  private float stepDuration;
  private AnimationCurve currentEasing;
  public SerializableSortedDictionary<string, string> fromValues = new();

  public interface IFieldAnimation {
    void Update(float t, float eased);
  }

  public class FloatFieldAnimation : IFieldAnimation {
    public Action<float> setter;
    public float from;
    public float to;
    public void Update(float t, float eased) {
      setter(from + (to - from) * eased);
    }
  }

  public class IntFieldAnimation : IFieldAnimation {
    public Action<int> setter;
    public int from;
    public int to;
    public void Update(float t, float eased) {
      setter(from + Mathf.RoundToInt((to - from) * eased));
    }
  }

  [Serializable]
  public class SequenceStep {
    public SerializableSortedDictionary<string, string> props = new();
    public float duration = 1f;
    public AnimationCurve easing = AnimationCurve.Linear(0, 0, 1, 1);
    public float randomDuration = 0f;
    public SerializableSortedDictionary<string, string> randomProps = new();
  }

  void Start() {
    if (!Application.isPlaying) return;
    if (!string.IsNullOrEmpty(trigger)) {
      triggerOff = MessageBus.On(trigger, (o) => Play());
    } else {
      Play();
    }
  }

  void GenerateAnimationFromStep(SequenceStep step, bool isFirstStep) {
    fieldAnimations.Clear();
    if (!hasValidTarget) return;
    stepDuration = step.duration + UnityEngine.Random.Range(0f, step.randomDuration);
    currentEasing = step.easing;
    var typeRef = target.GetType();

    foreach (var kvp in step.props) {
      var key = kvp.key;
      var strVal = kvp.value;
      var field = typeRef.GetField(key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
      var prop = field == null ? typeRef.GetProperty(key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) : null;
      object baseVal = null;

      if (isFirstStep && fromValues.TryGetValue(key, out var fromOverride)) {
        if (float.TryParse(fromOverride, out var fVal)) baseVal = fVal;
        else if (int.TryParse(fromOverride, out var iVal)) baseVal = iVal;
        else baseVal = fromOverride;
      } else {
        if (field != null) baseVal = field.GetValue(target);
        else if (prop != null && prop.CanRead && prop.CanWrite) baseVal = prop.GetValue(target);
      }

      step.randomProps.TryGetValue(key, out var randomStr);
      float.TryParse(randomStr, out var randomRange);

      if (baseVal is float f1 && float.TryParse(strVal, out var f2)) {
        var randomized = f2 + UnityEngine.Random.Range(0f, randomRange);
        var from = f1;
        var to = typeIsBy ? f1 + randomized : randomized;
        var setter = field != null ?
          new Action<float>(v => field.SetValue(target, v)) :
          new Action<float>(v => prop.SetValue(target, v));
        fieldAnimations.Add(new FloatFieldAnimation {
          from = from,
          to = to,
          setter = setter
        });
      } else if (baseVal is int i1 && int.TryParse(strVal, out var i2)) {
        var randomized = i2 + Mathf.FloorToInt(UnityEngine.Random.Range(0f, randomRange));
        var from = i1;
        var to = typeIsBy ? i1 + randomized : randomized;
        var setter = field != null ?
          new Action<int>(v => field.SetValue(target, v)) :
          new Action<int>(v => prop.SetValue(target, v));
        fieldAnimations.Add(new IntFieldAnimation {
          from = from,
          to = to,
          setter = setter
        });
      }
    }
  }

  public void Restart() {
    timer = 0f;
    sequenceIt = 0;
    var typeRef = target.GetType();
    foreach (var kvp in fromValues) {
      var key = kvp.key;
      var val = kvp.value;
      var field = typeRef.GetField(key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
      var prop = field == null ? typeRef.GetProperty(key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) : null;
      object parsed = val;
      if (float.TryParse(val, out var f)) parsed = f;
      else if (int.TryParse(val, out var i)) parsed = i;
      if (field != null) field.SetValue(target, parsed);
      else if (prop != null && prop.CanRead && prop.CanWrite) prop.SetValue(target, parsed);
    }
  }

  public void Reset() {
    if (!hasValidTarget) return;
    var typeRef = target.GetType();
    fromValues.Clear();
    foreach (var step in sequence) {
      foreach (var kvp in step.props) {
        var key = kvp.key;
        if (fromValues.ContainsKey(key)) continue;
        var field = typeRef.GetField(key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var prop = field == null ? typeRef.GetProperty(key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) : null;
        object val = field != null ? field.GetValue(target) : prop != null && prop.CanRead && prop.CanWrite ? prop.GetValue(target) : null;
        if (val is float || val is int || val is string) fromValues[key] = val.ToString();
      }
    }

  #if UNITY_EDITOR
    if (!Application.isPlaying && fromValues.Count == 0) {
      fromValues[" "] = " "; // force Unity to serialize it
    }
  #endif
  }

  void OnDestroy() {
    triggerOff?.Invoke();
    triggerOff = null;
  }

  public void Play() {
    Restart();
    paused = false;
    typeIsBy = type == "By";
    sequenceCount = sequence.Count;
    hasValidTarget = target != null;
    currentTimeScale = TimeScale.Factors[timeScaleIndex];
    if (sequenceCount > 0) GenerateAnimationFromStep(sequence[sequenceIt], true);
  }

  public void Stop() {
    paused = true;
    Restart();
  }

  void Update() {
    if (paused || !hasValidTarget || sequenceCount == 0) return;
    timer += Time.deltaTime * currentTimeScale;
    if (stepDuration <= 0f) {
      ProcessStepComplete();
      return;
    }
    float t = timer / stepDuration;
    float eased = currentEasing.Evaluate(t);
    var count = fieldAnimations.Count;
    for (int i = 0; i < count; i++) {
      fieldAnimations[i].Update(t, eased);
    }
    if (t >= 1f) {
      ProcessStepComplete();
    }
  }

  void ProcessStepComplete() {
    timer = 0f;
    sequenceIt++;
    if (sequenceIt >= sequenceCount) {
      callback?.Invoke();
      if (loop) {
        Play();
      } else {
        Stop();
      }
    } else {
      GenerateAnimationFromStep(sequence[sequenceIt], false);
    }
  }
}
