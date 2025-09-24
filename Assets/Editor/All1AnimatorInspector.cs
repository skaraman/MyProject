using System.Collections.Generic;
using UnityEngine;
using CustomInspector;

[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class AllIn1AnimatorInspector : MonoBehaviour {
  [System.Serializable]
  public class Sequence<T> {
    public T from;
    public T to;
    public float duration = 1f;
    public float delay = 0f;
    public AnimationCurve easing = AnimationCurve.Linear(0, 0, 1, 1);
  }

  [System.Serializable]
  public class KeywordToggle {
    public string keyword;
    public bool enabled;
    public int keywordHash;
    public void CacheHash() {
      keywordHash = Shader.PropertyToID(keyword);
    }
  }

  [System.Serializable]
  public class FloatAnimation {
    public string prop;
    public int propHash;
    public List<Sequence<float>> sequences = new();
    public bool loop;
    public int currentSequenceIndex = 0;
    public float timer = 0f;
    public float lastValue;
    public bool isDone;
    public void CacheHash() {
      propHash = Shader.PropertyToID(prop);
    }
  }

  [System.Serializable]
  public class ColorAnimation {
    public string prop;
    public int propHash;
    public List<Sequence<Color>> sequences = new();
    public bool loop;
    public int currentSequenceIndex = 0;
    public float timer = 0f;
    public Color lastValue;
    public bool isDone;
    public void CacheHash() {
      propHash = Shader.PropertyToID(prop);
    }
  }

  [System.Serializable]
  public class VectorAnimation {
    public string prop;
    public int propHash;
    public List<Sequence<Vector4>> sequences = new();
    public bool loop;
    public int currentSequenceIndex = 0;
    public float timer = 0f;
    public Vector4 lastValue;
    public bool isDone;
    public void CacheHash() {
      propHash = Shader.PropertyToID(prop);
    }
  }

  [System.Serializable]
  public class TextureAssignment {
    public string prop;
    public int propHash;
    public Sprite texture;
    public bool isAssigned = false;
    public void CacheHash() {
      propHash = Shader.PropertyToID(prop);
    }
  }

  [Button(nameof(Refresh), label = "Refresh")][HideField] public bool _bool;

  public List<KeywordToggle> keywordToggles = new();
  public List<FloatAnimation> floatAnimations = new();
  public List<ColorAnimation> colorAnimations = new();
  public List<VectorAnimation> vectorAnimations = new();
  public List<TextureAssignment> textureAssignments = new();

  private Renderer _renderer;
  private MaterialPropertyBlock _propBlock;
  private Material _material;
  private bool changed;
  private float deltaTime;

  public List<FloatAnimation> activeFloatAnimations = new();
  public List<ColorAnimation> activeColorAnimations = new();
  public List<VectorAnimation> activeVectorAnimations = new();

  public Dictionary<string, FloatAnimation> floatAnimDict = new();
  public Dictionary<string, ColorAnimation> colorAnimDict = new();
  public Dictionary<string, VectorAnimation> vectorAnimDict = new();
  public Dictionary<string, KeywordToggle> keywordDict = new();

  public void Refresh() {
    ApplyAllProperties(true);
  }

  public void Awake() {
    _renderer = GetComponent<Renderer>();
    _propBlock = new MaterialPropertyBlock();
    _material = Application.isPlaying ? _renderer.material : _renderer.sharedMaterial;
    ResetActive();
    CacheAllHashes();
    BuildDictionaries();
    BuildActiveLists();
    ApplyAllProperties(true);
  }

  public void ResetActive() {
    activeFloatAnimations.Clear();
    activeColorAnimations.Clear();
    activeVectorAnimations.Clear();
  }

  public void Reset() {
    floatAnimations.Clear();
    colorAnimations.Clear();
    vectorAnimations.Clear();
    keywordToggles.Clear();
    textureAssignments.Clear();
    floatAnimDict.Clear();
    colorAnimDict.Clear();
    vectorAnimDict.Clear();
    keywordDict.Clear();
  }

  void CacheAllHashes() {
    foreach (var a in floatAnimations) a.CacheHash();
    foreach (var a in colorAnimations) a.CacheHash();
    foreach (var a in vectorAnimations) a.CacheHash();
    foreach (var a in textureAssignments) a.CacheHash();
    foreach (var a in keywordToggles) a.CacheHash();
  }

  void BuildDictionaries() {
    floatAnimDict.Clear();
    colorAnimDict.Clear();
    vectorAnimDict.Clear();
    keywordDict.Clear();
    foreach (var a in floatAnimations) floatAnimDict[a.prop] = a;
    foreach (var a in colorAnimations) colorAnimDict[a.prop] = a;
    foreach (var a in vectorAnimations) vectorAnimDict[a.prop] = a;
    foreach (var a in keywordToggles) keywordDict[a.keyword] = a;
  }

  void BuildActiveLists() {
    activeFloatAnimations.Clear();
    activeColorAnimations.Clear();
    activeVectorAnimations.Clear();
    foreach (var a in floatAnimations) if (!a.isDone) activeFloatAnimations.Add(a);
    foreach (var a in colorAnimations) if (!a.isDone) activeColorAnimations.Add(a);
    foreach (var a in vectorAnimations) if (!a.isDone) activeVectorAnimations.Add(a);
  }

  public void Update() {
    if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
    if (_renderer == null)  _renderer = GetComponent<Renderer>();
    _renderer.GetPropertyBlock(_propBlock);
    changed = false;
    deltaTime = Time.deltaTime;
    for (int i = activeFloatAnimations.Count - 1; i >= 0; i--) AnimateFloat(activeFloatAnimations[i]);
    for (int i = activeColorAnimations.Count - 1; i >= 0; i--) AnimateColor(activeColorAnimations[i]);
    for (int i = activeVectorAnimations.Count - 1; i >= 0; i--) AnimateVector(activeVectorAnimations[i]);

    if (changed) _renderer.SetPropertyBlock(_propBlock);
  }

  void AnimateFloat(FloatAnimation anim) {
    if (anim.sequences.Count == 0) return;
    if (anim.isDone && anim.loop) {
      anim.isDone = false;
      anim.currentSequenceIndex = 0;
    }
    else if (anim.isDone && anim.loop == false) {
      return;
    }
    var seq = anim.sequences[anim.currentSequenceIndex];
    anim.timer += deltaTime;
    var effectiveTime = anim.timer - seq.delay;
    if (effectiveTime < 0f) return;
    float t = Mathf.Clamp01(effectiveTime / seq.duration);
    float eased = seq.easing.Evaluate(t);
    float value = seq.from + (seq.to - seq.from) * eased;
    if (Mathf.Abs(value - anim.lastValue) > 0.001f) {
      _propBlock.SetFloat(anim.propHash, value);
      anim.lastValue = value;
      changed = true;
    }
    if (effectiveTime >= seq.duration) FinalizeStep(anim);
  }

  void AnimateColor(ColorAnimation anim) {
    if (anim.sequences.Count == 0) return;
    if (anim.isDone && anim.loop) {
      anim.isDone = false;
      anim.currentSequenceIndex = 0;
    }
    else if (anim.isDone && anim.loop == false) {
      return;
    }
    var seq = anim.sequences[anim.currentSequenceIndex];
    anim.timer += deltaTime;
    var effectiveTime = anim.timer - seq.delay;
    if (effectiveTime < 0f) return;
    float t = Mathf.Clamp01(effectiveTime / seq.duration);
    float eased = seq.easing.Evaluate(t);
    var value = Color.LerpUnclamped(seq.from, seq.to, eased);
    if (((Vector4)(value - anim.lastValue)).sqrMagnitude > 0.0001f) {
      _propBlock.SetColor(anim.propHash, value);
      anim.lastValue = value;
      changed = true;
    }
    if (effectiveTime >= seq.duration) FinalizeStep(anim);
  }

  void AnimateVector(VectorAnimation anim) {
    if (anim.sequences.Count == 0) return;
    if (anim.isDone && anim.loop) {
      anim.isDone = false;
      anim.currentSequenceIndex = 0;
    }
    else if (anim.isDone && anim.loop == false) {
      return;
    }
    var seq = anim.sequences[anim.currentSequenceIndex];
    anim.timer += deltaTime;
    var effectiveTime = anim.timer - seq.delay;
    if (effectiveTime < 0f) return;
    float t = Mathf.Clamp01(effectiveTime / seq.duration);
    float eased = seq.easing.Evaluate(t);
    var value = Vector4.LerpUnclamped(seq.from, seq.to, eased);
    if ((value - anim.lastValue).sqrMagnitude > 0.0001f) {
      _propBlock.SetVector(anim.propHash, value);
      anim.lastValue = value;
      changed = true;
    }
    if (effectiveTime >= seq.duration) FinalizeStep(anim);
  }

  void FinalizeStep(dynamic anim) {
    anim.timer = 0f;
    anim.currentSequenceIndex++;
    if (anim.currentSequenceIndex >= anim.sequences.Count) {
      anim.currentSequenceIndex = 0;
      anim.isDone = true;
    }
  }

  public void ToggleKeywords() {
    if (_material == null) {
      _renderer = GetComponent<Renderer>();
      _material = Application.isPlaying ? _renderer.material : _renderer.sharedMaterial;
    }
    foreach (var kw in keywordToggles) {
      if (string.IsNullOrEmpty(kw.keyword)) continue;
      if (kw.enabled) _material.EnableKeyword(kw.keyword);
      else _material.DisableKeyword(kw.keyword);
    }
  }

  [ForceUpdate]
  public void ApplyAllProperties(bool force) {
    if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
    if (_renderer == null)  _renderer = GetComponent<Renderer>();
    _renderer.GetPropertyBlock(_propBlock);
    ToggleKeywords();
    foreach (var anim in floatAnimations) if (anim.sequences.Count > 0) _propBlock.SetFloat(anim.propHash, anim.sequences[0].from);
    foreach (var anim in colorAnimations) if (anim.sequences.Count > 0) _propBlock.SetColor(anim.propHash, anim.sequences[0].from);
    foreach (var anim in vectorAnimations) if (anim.sequences.Count > 0) _propBlock.SetVector(anim.propHash, anim.sequences[0].from);
    foreach (var assign in textureAssignments) {
      if ((!assign.isAssigned || force) && !string.IsNullOrEmpty(assign.prop) && assign.texture != null) {
        _propBlock.SetTexture(assign.propHash, assign.texture.texture);
        assign.isAssigned = true;
      }
    }
    textureAssignments.Clear();
    _renderer.SetPropertyBlock(_propBlock);
  }

  public void SetKeyword(string keyword, bool enabled) {
    if (keywordDict.TryGetValue(keyword, out var existing)) {
      existing.enabled = enabled;
    }
    else {
      var kw = new KeywordToggle { keyword = keyword, enabled = enabled };
      kw.CacheHash();
      keywordToggles.Add(kw);
      keywordDict[keyword] = kw;
    }
    ToggleKeywords();
  }


  public void AddFloatSequence(string prop, float from, float to, float duration, float delay = 0f, bool loop = false, AnimationCurve easing = null) {
    if (!floatAnimDict.TryGetValue(prop, out var anim)) {
      anim = new FloatAnimation { prop = prop, loop = loop };
      anim.CacheHash();
      floatAnimations.Add(anim);
      floatAnimDict[prop] = anim;
    }
    anim.sequences.Add(new Sequence<float> { from = from, to = to, duration = duration, delay = delay, easing = easing ?? AnimationCurve.Linear(0, 0, 1, 1) });
    anim.isDone = false;
    if (!activeFloatAnimations.Contains(anim)) activeFloatAnimations.Add(anim);
  }

  public void AddColorSequence(string prop, Color from, Color to, float duration, float delay = 0f, bool loop = false, AnimationCurve easing = null) {
    if (!colorAnimDict.TryGetValue(prop, out var anim)) {
      anim = new ColorAnimation { prop = prop, loop = loop };
      anim.CacheHash();
      colorAnimations.Add(anim);
      colorAnimDict[prop] = anim;
    }
    anim.sequences.Add(new Sequence<Color> { from = from, to = to, duration = duration, delay = delay, easing = easing ?? AnimationCurve.Linear(0, 0, 1, 1) });
    anim.isDone = false;
    if (!activeColorAnimations.Contains(anim)) activeColorAnimations.Add(anim);
  }

  public void AddVectorSequence(string prop, Vector4 from, Vector4 to, float duration, float delay = 0f, bool loop = false, AnimationCurve easing = null) {
    if (!vectorAnimDict.TryGetValue(prop, out var anim)) {
      anim = new VectorAnimation { prop = prop, loop = loop };
      anim.CacheHash();
      vectorAnimations.Add(anim);
      vectorAnimDict[prop] = anim;
    }
    anim.sequences.Add(new Sequence<Vector4> { from = from, to = to, duration = duration, delay = delay, easing = easing ?? AnimationCurve.Linear(0, 0, 1, 1) });
    anim.isDone = false;
    if (!activeVectorAnimations.Contains(anim)) activeVectorAnimations.Add(anim);
  }

  public void AddTextureAssignment(string prop, Sprite texture) {
    var assign = new TextureAssignment { prop = prop, texture = texture };
    assign.CacheHash();
    _propBlock.SetTexture(assign.propHash, assign.texture.texture);
    textureAssignments.RemoveAll(a => a.prop == prop);
  }

  public void RemoveFloat(string prop) {
    FloatAnimation anim;
    anim = floatAnimDict[prop];
    floatAnimations.Remove(anim);
    floatAnimDict.Remove(prop);
  }

  public void RemoveColor(string prop) {
    ColorAnimation anim;
    anim = colorAnimDict[prop];
    colorAnimations.Remove(anim);
    colorAnimDict.Remove(prop);
  }

  public void RemoveVector(string prop) {
    VectorAnimation anim;
    anim = vectorAnimDict[prop];
    vectorAnimations.Remove(anim);
    vectorAnimDict.Remove(prop);
  }
}
