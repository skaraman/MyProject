using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class AllIn1ShaderAnimator : MonoBehaviour {
  public enum LoopType { None, Loop, PingPong }

  [System.Serializable]
  public class Sequence<T> {
    public T from;
    public T to;
    public float duration = 1f;
    public float delay = 0f;
  }

  [System.Serializable]
  public class KeywordToggle {
    public string keyword;
    public bool enabled;
  }

  [System.Serializable]
  public class FloatAnimation {
    public string prop;
    public List<Sequence<float>> sequences = new();
    public LoopType loopType;
    public int currentSequenceIndex = 0;
    public float timer = 0f;
    public bool reverse = false;
    public float lastValue;
    public bool isDone;
  }

  [System.Serializable]
  public class ColorAnimation {
    public string prop;
    public List<Sequence<Color>> sequences = new();
    public LoopType loopType;
    public int currentSequenceIndex = 0;
    public float timer = 0f;
    public bool reverse = false;
    public Color lastValue;
    public bool isDone;
  }

  [System.Serializable]
  public class VectorAnimation {
    public string prop;
    public List<Sequence<Vector4>> sequences = new();
    public LoopType loopType;
    public int currentSequenceIndex = 0;
    public float timer = 0f;
    public bool reverse = false;
    public Vector4 lastValue;
    public bool isDone;
  }

  [System.Serializable]
  public class TextureAssignment {
    public string prop;
    public Texture texture;
    public bool isAssigned;
  }

  [SerializeField] bool refreshKeywords;
  public List<KeywordToggle> keywordToggles = new();
  public List<FloatAnimation> floatAnimations = new();
  public List<ColorAnimation> colorAnimations = new();
  public List<VectorAnimation> vectorAnimations = new();
  public List<TextureAssignment> textureAssignments = new();

  private Renderer _renderer;
  private MaterialPropertyBlock _propBlock;
  private Material _material;
  private bool changed;

  public void Awake() {
    _renderer = GetComponent<Renderer>();
    _propBlock = new MaterialPropertyBlock();
    _material = Application.isPlaying ? _renderer.material : _renderer.sharedMaterial;
  }

  public void Update() {
    if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
    _renderer.GetPropertyBlock(_propBlock);
    changed = false;

    for (int i = 0; i < floatAnimations.Count; i++) if (!floatAnimations[i].isDone) AnimateFloat(floatAnimations[i]);
    for (int i = 0; i < colorAnimations.Count; i++) if (!colorAnimations[i].isDone) AnimateColor(colorAnimations[i]);
    for (int i = 0; i < vectorAnimations.Count; i++) if (!vectorAnimations[i].isDone) AnimateVector(vectorAnimations[i]);

    foreach (var tex in textureAssignments) {
      if (!string.IsNullOrEmpty(tex.prop) && tex.texture != null && !tex.isAssigned) {
        _propBlock.SetTexture(tex.prop, tex.texture);
        tex.isAssigned = true;
        changed = true;
      }
    }

    if (changed) _renderer.SetPropertyBlock(_propBlock);
  }

  void AnimateFloat(FloatAnimation anim) {
    if (anim.sequences.Count == 0) return;
    var seq = anim.sequences[anim.currentSequenceIndex];
    anim.timer += Time.deltaTime;
    var effectiveTime = anim.timer - seq.delay;
    if (effectiveTime < 0f) return;
    float t = Mathf.Clamp01(effectiveTime / seq.duration);
    if (anim.reverse) t = 1f - t;
    float value = Mathf.Lerp(seq.from, seq.to, t);
    if (value != anim.lastValue) {
      _propBlock.SetFloat(anim.prop, value);
      anim.lastValue = value;
      changed = true;
    }
    FinalizeFloatStep(anim, effectiveTime);
  }

  void AnimateColor(ColorAnimation anim) {
    if (anim.sequences.Count == 0) return;
    var seq = anim.sequences[anim.currentSequenceIndex];
    anim.timer += Time.deltaTime;
    var effectiveTime = anim.timer - seq.delay;
    if (effectiveTime < 0f) return;
    float t = Mathf.Clamp01(effectiveTime / seq.duration);
    if (anim.reverse) t = 1f - t;
    var value = Color.Lerp(seq.from, seq.to, t);
    if (value != anim.lastValue) {
      _propBlock.SetColor(anim.prop, value);
      anim.lastValue = value;
      changed = true;
    }
    FinalizeColorStep(anim, effectiveTime);
  }

  void AnimateVector(VectorAnimation anim) {
    if (anim.sequences.Count == 0) return;
    var seq = anim.sequences[anim.currentSequenceIndex];
    anim.timer += Time.deltaTime;
    var effectiveTime = anim.timer - seq.delay;
    if (effectiveTime < 0f) return;
    float t = Mathf.Clamp01(effectiveTime / seq.duration);
    if (anim.reverse) t = 1f - t;
    var value = Vector4.Lerp(seq.from, seq.to, t);
    if (value != anim.lastValue) {
      _propBlock.SetVector(anim.prop, value);
      anim.lastValue = value;
      changed = true;
    }
    FinalizeVectorStep(anim, effectiveTime);
  }

  void FinalizeFloatStep(FloatAnimation anim, float effectiveTime) {
    if (effectiveTime < anim.sequences[anim.currentSequenceIndex].duration) return;
    anim.timer = 0f;
    if (!anim.reverse && anim.loopType == LoopType.PingPong) {
      anim.reverse = true;
    }
    else {
      anim.reverse = false;
      anim.currentSequenceIndex++;
      if (anim.currentSequenceIndex >= anim.sequences.Count) {
        switch (anim.loopType) {
          case LoopType.Loop:
            anim.currentSequenceIndex = 0;
            break;
          case LoopType.PingPong:
            anim.reverse = true;
            anim.currentSequenceIndex = anim.sequences.Count - 1;
            break;
          default:
            anim.isDone = true;
            break;
        }
      }
    }
  }

  void FinalizeColorStep(ColorAnimation anim, float effectiveTime) {
    if (effectiveTime < anim.sequences[anim.currentSequenceIndex].duration) return;
    anim.timer = 0f;
    if (!anim.reverse && anim.loopType == LoopType.PingPong) {
      anim.reverse = true;
    }
    else {
      anim.reverse = false;
      anim.currentSequenceIndex++;
      if (anim.currentSequenceIndex >= anim.sequences.Count) {
        switch (anim.loopType) {
          case LoopType.Loop:
            anim.currentSequenceIndex = 0;
            break;
          case LoopType.PingPong:
            anim.reverse = true;
            anim.currentSequenceIndex = anim.sequences.Count - 1;
            break;
          default:
            anim.isDone = true;
            break;
        }
      }
    }
  }

  void FinalizeVectorStep(VectorAnimation anim, float effectiveTime) {
    if (effectiveTime < anim.sequences[anim.currentSequenceIndex].duration) return;
    anim.timer = 0f;
    if (!anim.reverse && anim.loopType == LoopType.PingPong) {
      anim.reverse = true;
    }
    else {
      anim.reverse = false;
      anim.currentSequenceIndex++;
      if (anim.currentSequenceIndex >= anim.sequences.Count) {
        switch (anim.loopType) {
          case LoopType.Loop:
            anim.currentSequenceIndex = 0;
            break;
          case LoopType.PingPong:
            anim.reverse = true;
            anim.currentSequenceIndex = anim.sequences.Count - 1;
            break;
          default:
            anim.isDone = true;
            break;
        }
      }
    }
  }

  public void ToggleKeywords() {
    if (_material == null) {
      _renderer = GetComponent<Renderer>();
      _material = Application.isPlaying ? _renderer.material : _renderer.sharedMaterial;
      Debug.Log($"[ToggleKeywords] _material was null, reinitialized: {_material != null}");
    }

    foreach (var kw in keywordToggles) {
      if (string.IsNullOrEmpty(kw.keyword)) continue;
      if (kw.enabled) _material.EnableKeyword(kw.keyword);
      else _material.DisableKeyword(kw.keyword);
    }
  }

  public void ApplyAllProperties() {
    if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
    _renderer.GetPropertyBlock(_propBlock);
    ToggleKeywords();
    foreach (var anim in floatAnimations) {
      if (anim.sequences.Count > 0) _propBlock.SetFloat(anim.prop, anim.sequences[0].from);
    }
    foreach (var anim in colorAnimations) {
      if (anim.sequences.Count > 0) _propBlock.SetColor(anim.prop, anim.sequences[0].from);
    }
    foreach (var anim in vectorAnimations) {
      if (anim.sequences.Count > 0) _propBlock.SetVector(anim.prop, anim.sequences[0].from);
    }
    foreach (var assign in textureAssignments) {
      if (!assign.isAssigned && !string.IsNullOrEmpty(assign.prop) && assign.texture != null) {
        _propBlock.SetTexture(assign.prop, assign.texture);
        assign.isAssigned = true;
      }
    }
    _renderer.SetPropertyBlock(_propBlock);
  }

  public void SetKeyword(string keyword, bool enabled) {
    var existing = keywordToggles.Find(k => k.keyword == keyword);
    if (existing != null) {
      existing.enabled = enabled;
    }
    else {
      keywordToggles.Add(new KeywordToggle { keyword = keyword, enabled = enabled });
    }
    ToggleKeywords();
  }

  public void AddFloatSequence(string prop, float from, float to, float duration, float delay = 0f, LoopType loop = LoopType.None) {
    var anim = floatAnimations.Find(a => a.prop == prop);
    if (anim == null) {
      anim = new FloatAnimation { prop = prop, loopType = loop };
      floatAnimations.Add(anim);
    }
    anim.sequences.Add(new Sequence<float> { from = from, to = to, duration = duration, delay = delay });
    anim.isDone = false;
  }

  public void AddColorSequence(string prop, Color from, Color to, float duration, float delay = 0f, LoopType loop = LoopType.None) {
    var anim = colorAnimations.Find(a => a.prop == prop);
    if (anim == null) {
      anim = new ColorAnimation { prop = prop, loopType = loop };
      colorAnimations.Add(anim);
    }
    anim.sequences.Add(new Sequence<Color> { from = from, to = to, duration = duration, delay = delay });
    anim.isDone = false;
  }

  public void AddVectorSequence(string prop, Vector4 from, Vector4 to, float duration, float delay = 0f, LoopType loop = LoopType.None) {
    var anim = vectorAnimations.Find(a => a.prop == prop);
    if (anim == null) {
      anim = new VectorAnimation { prop = prop, loopType = loop };
      vectorAnimations.Add(anim);
    }
    anim.sequences.Add(new Sequence<Vector4> { from = from, to = to, duration = duration, delay = delay });
    anim.isDone = false;
  }
}
