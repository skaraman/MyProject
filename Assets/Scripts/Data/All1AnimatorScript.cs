using System.Collections.Generic;
using UnityEngine;

public class All1AnimatorScript : MonoBehaviour {
  public AllIn1AnimatorInspector animator;

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

public void AddFloatAnim(string name, string prop, float from, float to, float duration, float delay = 0f, bool loop = false, AnimationCurve easing = null) {
  var anim = new AllIn1AnimatorInspector.FloatAnimation {
    prop = prop,
    loop = loop
  };
  anim.CacheHash();
  anim.sequences = new List<AllIn1AnimatorInspector.Sequence<float>> {
    new AllIn1AnimatorInspector.Sequence<float> {
      from = from,
      to = to,
      duration = duration,
      delay = delay,
      easing = easing ?? AnimationCurve.Linear(0, 0, 1, 1)
    }
  };
  animator.floatAnimations.Add(anim);
  floatAnims[name] = new FloatAnimData { prop = prop, anim = anim };
}

  public void AddColorAnim(string name, string prop, Color from, Color to, float duration, float delay = 0f, bool loop = false, AnimationCurve easing = null) {
    var anim = new AllIn1AnimatorInspector.ColorAnimation { prop = prop, loop = loop };
    anim.CacheHash();
    anim.sequences = new List<AllIn1AnimatorInspector.Sequence<Color>> {
      new AllIn1AnimatorInspector.Sequence<Color> {
        from = from,
        to = to,
        duration = duration,
        delay = delay,
        easing = easing ?? AnimationCurve.Linear(0, 0, 1, 1)
      }
    };
    animator.colorAnimations.Add(anim);
    colorAnims[name] = new ColorAnimData { prop = prop, anim = anim };
  }

  public void AddVectorAnim(string name, string prop, Vector4 from, Vector4 to, float duration, float delay = 0f, bool loop = false, AnimationCurve easing = null) {
    var anim = new AllIn1AnimatorInspector.VectorAnimation { prop = prop, loop = loop };
    anim.CacheHash();
    anim.sequences = new List<AllIn1AnimatorInspector.Sequence<Vector4>> {
      new AllIn1AnimatorInspector.Sequence<Vector4> {
        from = from,
        to = to,
        duration = duration,
        delay = delay,
        easing = easing ?? AnimationCurve.Linear(0, 0, 1, 1)
      }
    };
    animator.vectorAnimations.Add(anim);
    vectorAnims[name] = new VectorAnimData { prop = prop, anim = anim };
  }

  public void Play(string name) {
    if (floatAnims.TryGetValue(name, out var f)) {
      ResetAnim(f.anim);
      animator.floatAnimDict[f.prop] = f.anim;
      if (!animator.floatAnimations.Contains(f.anim)) animator.floatAnimations.Add(f.anim);
      animator.activeFloatAnimations.RemoveAll(a => a.prop == f.prop);
      animator.activeFloatAnimations.Add(f.anim);
      activeFloat[name] = f;
      animator.Refresh();
      animator.ApplyAllProperties(true);
      return;
    }

    if (colorAnims.TryGetValue(name, out var c)) {
      ResetAnim(c.anim);
      animator.colorAnimDict[c.prop] = c.anim;
      if (!animator.colorAnimations.Contains(c.anim)) animator.colorAnimations.Add(c.anim);
      animator.activeColorAnimations.RemoveAll(a => a.prop == c.prop);
      animator.activeColorAnimations.Add(c.anim);
      activeColor[name] = c;
      animator.Refresh();
      animator.ApplyAllProperties(true);
      return;
    }

    if (vectorAnims.TryGetValue(name, out var v)) {
      ResetAnim(v.anim);
      animator.vectorAnimDict[v.prop] = v.anim;
      if (!animator.vectorAnimations.Contains(v.anim)) animator.vectorAnimations.Add(v.anim);
      animator.activeVectorAnimations.RemoveAll(a => a.prop == v.prop);
      animator.activeVectorAnimations.Add(v.anim);
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