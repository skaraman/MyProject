using System;
using System.Collections.Generic;
using UnityEngine;

public class All1AnimatorScript : MonoBehaviour {
  private AllIn1AnimatorInspector animator;

  class FloatAnimData {
    public string prop;
    public AllIn1AnimatorInspector.FloatAnimation anim;
  }

  class ColorAnimData {
    public string prop;
    public AllIn1AnimatorInspector.ColorAnimation anim;
  }

  class VectorAnimData {
    public string prop;
    public AllIn1AnimatorInspector.VectorAnimation anim;
  }

  Dictionary<string, FloatAnimData> floatAnims = new();
  Dictionary<string, ColorAnimData> colorAnims = new();
  Dictionary<string, VectorAnimData> vectorAnims = new();

  Dictionary<string, FloatAnimData> activeFloat = new();
  Dictionary<string, ColorAnimData> activeColor = new();
  Dictionary<string, VectorAnimData> activeVector = new();

  bool EnsureAnimator() {
    if (animator != null) return true;
    animator = GetComponent<AllIn1AnimatorInspector>();
    if (animator == null) {
      Debug.LogWarning($"{nameof(AllIn1AnimatorInspector)} component missing on {name}", this);
      return false;
    }
    return true;
  }

  public void Start() {
    EnsureAnimator();
  }

  public void StartInspectorAnimations(bool restartSequences = true) {
    if (!EnsureAnimator()) return;
    animator.StartAnimations(restartSequences);
  }

  public void AddFloatAnim(string name, string prop, float from, float to, float duration, float delay = 0f, bool loop = false, AnimationCurve easing = null, bool autoPlay = true) {
    if (!EnsureAnimator()) return;
    var data = GetFloatAnimData(name, prop, loop, autoPlay);
    data.anim.sequences.Clear();
    data.anim.sequences.Add(new AllIn1AnimatorInspector.Sequence<float> {
      from = from,
      to = to,
      duration = duration,
      delay = delay,
      easing = easing ?? AnimationCurve.Linear(0, 0, 1, 1)
    });
    ResetAnim(data.anim);
    DedupFloat(data);
  }

  public void AddColorAnim(string name, string prop, Color from, Color to, float duration, float delay = 0f, bool loop = false, AnimationCurve easing = null, bool autoPlay = true) {
    if (!EnsureAnimator()) return;
    var data = GetColorAnimData(name, prop, loop, autoPlay);
    data.anim.sequences.Clear();
    data.anim.sequences.Add(new AllIn1AnimatorInspector.Sequence<Color> {
      from = from,
      to = to,
      duration = duration,
      delay = delay,
      easing = easing ?? AnimationCurve.Linear(0, 0, 1, 1)
    });
    ResetAnim(data.anim);
    DedupColor(data);
  }

  public void AddVectorAnim(string name, string prop, Vector4 from, Vector4 to, float duration, float delay = 0f, bool loop = false, AnimationCurve easing = null, bool autoPlay = true) {
    if (!EnsureAnimator()) return;
    var data = GetVectorAnimData(name, prop, loop, autoPlay);
    data.anim.sequences.Clear();
    data.anim.sequences.Add(new AllIn1AnimatorInspector.Sequence<Vector4> {
      from = from,
      to = to,
      duration = duration,
      delay = delay,
      easing = easing ?? AnimationCurve.Linear(0, 0, 1, 1)
    });
    ResetAnim(data.anim);
    DedupVector(data);
  }

  public void Play(string name) {
    if (!EnsureAnimator()) return;
    if (floatAnims.TryGetValue(name, out var f)) {
      ResetAnim(f.anim);
      DedupFloat(f);
      animator.activeFloatAnimations.RemoveAll(a => a.prop == f.prop);
      animator.activeFloatAnimations.Add(f.anim);
      animator.enabled = true;
      activeFloat[name] = f;
      animator.Refresh();
      animator.ApplyAllProperties(true);
      return;
    }

    if (colorAnims.TryGetValue(name, out var c)) {
      ResetAnim(c.anim);
      DedupColor(c);
      animator.activeColorAnimations.RemoveAll(a => a.prop == c.prop);
      animator.activeColorAnimations.Add(c.anim);
      animator.enabled = true;
      activeColor[name] = c;
      animator.Refresh();
      animator.ApplyAllProperties(true);
      return;
    }

    if (vectorAnims.TryGetValue(name, out var v)) {
      ResetAnim(v.anim);
      DedupVector(v);
      animator.activeVectorAnimations.RemoveAll(a => a.prop == v.prop);
      animator.activeVectorAnimations.Add(v.anim);
      animator.enabled = true;
      activeVector[name] = v;
      animator.Refresh();
      animator.ApplyAllProperties(true);
    }
  }

  public void Stop(string name) {
    if (activeFloat.TryGetValue(name, out var f)) {
      f.anim.isDone = true;
      ResetAnim(f.anim);
      activeFloat.Remove(name);
    }
    if (activeColor.TryGetValue(name, out var c)) {
      c.anim.isDone = true;
      ResetAnim(c.anim);
      activeColor.Remove(name);
    }
    if (activeVector.TryGetValue(name, out var v)) {
      v.anim.isDone = true;
      ResetAnim(v.anim);
      activeVector.Remove(name);
    }
  }

  public void Pause(string name) {
    if (activeFloat.TryGetValue(name, out var f)) f.anim.isDone = true;
    if (activeColor.TryGetValue(name, out var c)) c.anim.isDone = true;
    if (activeVector.TryGetValue(name, out var v)) v.anim.isDone = true;
  }

  public void Restart(string name) {
    if (!EnsureAnimator()) return;
    if (activeFloat.TryGetValue(name, out var f)) {
      ResetAnim(f.anim);
      if (!animator.activeFloatAnimations.Contains(f.anim)) animator.activeFloatAnimations.Add(f.anim);
    }
    if (activeColor.TryGetValue(name, out var c)) {
      ResetAnim(c.anim);
      if (!animator.activeColorAnimations.Contains(c.anim)) animator.activeColorAnimations.Add(c.anim);
    }
    if (activeVector.TryGetValue(name, out var v)) {
      ResetAnim(v.anim);
      if (!animator.activeVectorAnimations.Contains(v.anim)) animator.activeVectorAnimations.Add(v.anim);
    }
  }

  FloatAnimData GetFloatAnimData(string name, string prop, bool loop, bool autoPlay) {
    if (!floatAnims.TryGetValue(name, out var data)) data = new FloatAnimData();
    data.prop = prop;
    if (data.anim == null) {
      data.anim = new AllIn1AnimatorInspector.FloatAnimation { prop = prop, loop = loop, autoPlay = autoPlay };
      data.anim.CacheHash();
    }
    else {
      data.anim.prop = prop;
      data.anim.loop = loop;
      data.anim.autoPlay = autoPlay;
      data.anim.CacheHash();
    }
    floatAnims[name] = data;
    if (activeFloat.ContainsKey(name)) activeFloat[name] = data;
    return data;
  }

  ColorAnimData GetColorAnimData(string name, string prop, bool loop, bool autoPlay) {
    if (!colorAnims.TryGetValue(name, out var data)) data = new ColorAnimData();
    data.prop = prop;
    if (data.anim == null) {
      data.anim = new AllIn1AnimatorInspector.ColorAnimation { prop = prop, loop = loop, autoPlay = autoPlay };
      data.anim.CacheHash();
    }
    else {
      data.anim.prop = prop;
      data.anim.loop = loop;
      data.anim.autoPlay = autoPlay;
      data.anim.CacheHash();
    }
    colorAnims[name] = data;
    if (activeColor.ContainsKey(name)) activeColor[name] = data;
    return data;
  }

  VectorAnimData GetVectorAnimData(string name, string prop, bool loop, bool autoPlay) {
    if (!vectorAnims.TryGetValue(name, out var data)) data = new VectorAnimData();
    data.prop = prop;
    if (data.anim == null) {
      data.anim = new AllIn1AnimatorInspector.VectorAnimation { prop = prop, loop = loop, autoPlay = autoPlay };
      data.anim.CacheHash();
    }
    else {
      data.anim.prop = prop;
      data.anim.loop = loop;
      data.anim.autoPlay = autoPlay;
      data.anim.CacheHash();
    }
    vectorAnims[name] = data;
    if (activeVector.ContainsKey(name)) activeVector[name] = data;
    return data;
  }

  void DedupFloat(FloatAnimData data) {
    if (!EnsureAnimator()) return;
    animator.floatAnimations.RemoveAll(a => a.prop == data.prop && a != data.anim);
    animator.floatAnimDict[data.prop] = data.anim;
    if (!animator.floatAnimations.Contains(data.anim)) animator.floatAnimations.Add(data.anim);
  }

  void DedupColor(ColorAnimData data) {
    if (!EnsureAnimator()) return;
    animator.colorAnimations.RemoveAll(a => a.prop == data.prop && a != data.anim);
    animator.colorAnimDict[data.prop] = data.anim;
    if (!animator.colorAnimations.Contains(data.anim)) animator.colorAnimations.Add(data.anim);
  }

  void DedupVector(VectorAnimData data) {
    if (!EnsureAnimator()) return;
    animator.vectorAnimations.RemoveAll(a => a.prop == data.prop && a != data.anim);
    animator.vectorAnimDict[data.prop] = data.anim;
    if (!animator.vectorAnimations.Contains(data.anim)) animator.vectorAnimations.Add(data.anim);
  }

  void ResetAnim(AllIn1AnimatorInspector.FloatAnimation a) {
    a.timer = 0;
    a.isDone = false;
    a.currentSequenceIndex = 0;
  }

  void ResetAnim(AllIn1AnimatorInspector.ColorAnimation a) {
    a.timer = 0;
    a.isDone = false;
    a.currentSequenceIndex = 0;
  }

  void ResetAnim(AllIn1AnimatorInspector.VectorAnimation a) {
    a.timer = 0;
    a.isDone = false;
    a.currentSequenceIndex = 0;
  }
}

// var controller = gameObject.GetComponent<All1AnimatorScript>();
// controller.AddFloatAnim("glowWide", "_GlowAmount", 1f, 10f, 2f, 0f, true);
// controller.AddFloatAnim("glowPulse", "_GlowAmount", 5f, 6f, 0.5f, 0f, true, AnimationCurve.EaseInOut(0, 0, 1, 1));

// controller.Play("glowWide");
// controller.Play("glowPulse");
// controller.Stop("glowPulse");
// controller.Restart("glowWide");
