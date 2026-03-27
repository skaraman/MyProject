using System.Collections.Generic;
using CustomInspector;
using UnityEngine;


public class AllIn1AnimatorInspector : MonoBehaviour {
  const sbyte StaticEvalUnknown = 0;
  const sbyte StaticEvalDynamic = -1;
  const sbyte StaticEvalStatic = 1;

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
    [HideInInspector] public int keywordHash;
    public void CacheHash() {
      keywordHash = string.IsNullOrWhiteSpace(keyword) ? 0 : Shader.PropertyToID(keyword);
    }
  }

  [System.Serializable]
  public class FloatAnimation {
    public string prop;
    [HideInInspector] public int propHash;
    public List<Sequence<float>> sequences = new();
    public bool loop;
    public bool autoPlay = true;
    public int currentSequenceIndex = 0;
    public float timer = 0f;
    public float lastValue;
    [HideInInspector] public sbyte staticEvaluationState = StaticEvalUnknown;
    [HideInInspector] public float staticValue;
    public bool isDone;
    public void CacheHash() {
      propHash = string.IsNullOrWhiteSpace(prop) ? 0 : Shader.PropertyToID(prop);
    }
  }

  [System.Serializable]
  public class ColorAnimation {
    public string prop;
    [HideInInspector] public int propHash;
    public List<Sequence<Color>> sequences = new();
    public bool loop;
    public bool autoPlay = true;
    public int currentSequenceIndex = 0;
    public float timer = 0f;
    public Color lastValue;
    [HideInInspector] public sbyte staticEvaluationState = StaticEvalUnknown;
    [HideInInspector] public Color staticValue;
    public bool isDone;
    public void CacheHash() {
      propHash = string.IsNullOrWhiteSpace(prop) ? 0 : Shader.PropertyToID(prop);
    }
  }

  [System.Serializable]
  public class VectorAnimation {
    public string prop;
    [HideInInspector] public int propHash;
    public List<Sequence<Vector4>> sequences = new();
    public bool loop;
    public bool autoPlay = true;
    public int currentSequenceIndex = 0;
    public float timer = 0f;
    public Vector4 lastValue;
    [HideInInspector] public sbyte staticEvaluationState = StaticEvalUnknown;
    [HideInInspector] public Vector4 staticValue;
    public bool isDone;
    public void CacheHash() {
      propHash = string.IsNullOrWhiteSpace(prop) ? 0 : Shader.PropertyToID(prop);
    }
  }

  [System.Serializable]
  public class TextureAssignment {
    public string prop;
    [HideInInspector] public int propHash;
    public Sprite texture;
    public bool isAssigned = false;
    public void CacheHash() {
      propHash = string.IsNullOrWhiteSpace(prop) ? 0 : Shader.PropertyToID(prop);
    }
  }

  [Button(nameof(Refresh), label = "Refresh")][HideField] public bool _bool;
  [SerializeField] bool animateWhenNotVisible;

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
  private bool _hasStarted;
  private bool _missingRendererLogged;

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

  public void OnValidate() {
    CacheAllHashes();
    BuildDictionaries();
  }

  public void Awake() {
    _propBlock = new MaterialPropertyBlock();
    TryResolveRenderer();
    TryResolveMaterial();
    ResetActive();
    CacheAllHashes();
    BuildDictionaries();
    ResetAnimationStates();
    BuildActiveLists(true);
    SetUpdateActiveState();
    ApplyAllProperties(true);
  }

  public void ResetActive() {
    activeFloatAnimations.Clear();
    activeColorAnimations.Clear();
    activeVectorAnimations.Clear();
    SetUpdateActiveState();
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
    _hasStarted = false;
  }

  public void StartAnimations(bool restartSequences = true) {
    StartAnimationsInternal(restartSequences);
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

  void ResetAnimationStates() {
    foreach (var anim in floatAnimations) ResetAnim(anim);
    foreach (var anim in colorAnimations) ResetAnim(anim);
    foreach (var anim in vectorAnimations) ResetAnim(anim);
  }

  void ResetAnim(FloatAnimation anim) {
    anim.timer = 0f;
    anim.isDone = false;
    anim.currentSequenceIndex = 0;
    anim.lastValue = 0f;
    anim.staticEvaluationState = StaticEvalUnknown;
  }

  void ResetAnim(ColorAnimation anim) {
    anim.timer = 0f;
    anim.isDone = false;
    anim.currentSequenceIndex = 0;
    anim.lastValue = default;
    anim.staticEvaluationState = StaticEvalUnknown;
  }

  void ResetAnim(VectorAnimation anim) {
    anim.timer = 0f;
    anim.isDone = false;
    anim.currentSequenceIndex = 0;
    anim.lastValue = default;
    anim.staticEvaluationState = StaticEvalUnknown;
  }

  void StartAnimationsInternal(bool restartSequences) {
    _hasStarted = true;
    if (restartSequences) ResetAnimationStates();
    BuildActiveLists();
    SetUpdateActiveState();
    ApplyAllProperties(true);
  }

  bool ShouldActivateOnAdd(bool animAutoPlay) {
    return animAutoPlay || _hasStarted;
  }

  void BuildActiveLists(bool autoOnly = false) {
    activeFloatAnimations.Clear();
    activeColorAnimations.Clear();
    activeVectorAnimations.Clear();
    foreach (var a in floatAnimations) if (!a.isDone && (!autoOnly || a.autoPlay)) activeFloatAnimations.Add(a);
    foreach (var a in colorAnimations) if (!a.isDone && (!autoOnly || a.autoPlay)) activeColorAnimations.Add(a);
    foreach (var a in vectorAnimations) if (!a.isDone && (!autoOnly || a.autoPlay)) activeVectorAnimations.Add(a);
  }

  public void Update() {
    if (!TryResolveRenderer()) {
      SetUpdateActiveState();
      return;
    }
    if (!HasPotentialActiveAnimations()) {
      SetUpdateActiveState();
      return;
    }
    if (!ShouldAnimateThisFrame()) return;

    if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
    _renderer.GetPropertyBlock(_propBlock);
    changed = false;
    deltaTime = TimeScale.GetDeltaTime(this);

    for (int i = activeFloatAnimations.Count - 1; i >= 0; i--) {
      var anim = activeFloatAnimations[i];
      if (anim == null || anim.sequences == null || anim.sequences.Count == 0 || anim.propHash == 0) {
        activeFloatAnimations.RemoveAt(i);
        continue;
      }
      if (TryGetStaticFloatValue(anim, out var staticValue)) {
        _propBlock.SetFloat(anim.propHash, staticValue);
        anim.lastValue = staticValue;
        changed = true;
        activeFloatAnimations.RemoveAt(i);
        continue;
      }
      AnimateFloat(anim);
      if (anim.isDone && !anim.loop) activeFloatAnimations.RemoveAt(i);
    }

    for (int i = activeColorAnimations.Count - 1; i >= 0; i--) {
      var anim = activeColorAnimations[i];
      if (anim == null || anim.sequences == null || anim.sequences.Count == 0 || anim.propHash == 0) {
        activeColorAnimations.RemoveAt(i);
        continue;
      }
      if (TryGetStaticColorValue(anim, out var staticValue)) {
        _propBlock.SetColor(anim.propHash, staticValue);
        anim.lastValue = staticValue;
        changed = true;
        activeColorAnimations.RemoveAt(i);
        continue;
      }
      AnimateColor(anim);
      if (anim.isDone && !anim.loop) activeColorAnimations.RemoveAt(i);
    }

    for (int i = activeVectorAnimations.Count - 1; i >= 0; i--) {
      var anim = activeVectorAnimations[i];
      if (anim == null || anim.sequences == null || anim.sequences.Count == 0 || anim.propHash == 0) {
        activeVectorAnimations.RemoveAt(i);
        continue;
      }
      if (TryGetStaticVectorValue(anim, out var staticValue)) {
        _propBlock.SetVector(anim.propHash, staticValue);
        anim.lastValue = staticValue;
        changed = true;
        activeVectorAnimations.RemoveAt(i);
        continue;
      }
      AnimateVector(anim);
      if (anim.isDone && !anim.loop) activeVectorAnimations.RemoveAt(i);
    }

    if (changed) _renderer.SetPropertyBlock(_propBlock);
    if (!HasPotentialActiveAnimations()) SetUpdateActiveState();
  }

  bool HasPotentialActiveAnimations() {
    return activeFloatAnimations.Count > 0 || activeColorAnimations.Count > 0 || activeVectorAnimations.Count > 0;
  }

  bool ShouldAnimateThisFrame() {
    if (animateWhenNotVisible) return true;
    if (_renderer == null || !_renderer.enabled) return false;
    return _renderer.isVisible;
  }

  void SetUpdateActiveState() {
    enabled = HasPotentialActiveAnimations() && HasLocalRenderer();
  }

  bool HasLocalRenderer() {
    return TryResolveRenderer();
  }

  bool HasComponentPropagator() {
    return TryGetComponent<ComponentPropagator>(out _);
  }

  bool TryResolveRenderer() {
    if (_renderer != null) return true;
    _renderer = GetComponent<Renderer>();
    if (_renderer != null) return true;
    if (HasComponentPropagator()) return false;
    if (_missingRendererLogged) return false;
    _missingRendererLogged = true;
    Debug.LogWarning($"[AllIn1AnimatorInspector] Missing Renderer on '{name}'. Local material application is disabled until a Renderer is added.", this);
    return false;
  }

  bool TryResolveMaterial() {
    if (_material != null) return true;
    if (!TryResolveRenderer()) return false;
    _material = Application.isPlaying ? _renderer.material : _renderer.sharedMaterial;
    return _material != null;
  }

  static bool TryGetStaticFloatValue(FloatAnimation anim, out float value) {
    value = 0f;
    if (anim == null || anim.sequences == null || anim.sequences.Count == 0) return false;
    if (anim.staticEvaluationState == StaticEvalStatic) {
      value = anim.staticValue;
      return true;
    }
    if (anim.staticEvaluationState == StaticEvalDynamic) return false;

    value = anim.sequences[0].to;
    for (int i = 0; i < anim.sequences.Count; i++) {
      var seq = anim.sequences[i];
      if (Mathf.Abs(seq.to - seq.from) > 0.0001f) {
        anim.staticEvaluationState = StaticEvalDynamic;
        return false;
      }
      value = seq.to;
    }
    anim.staticValue = value;
    anim.staticEvaluationState = StaticEvalStatic;
    return true;
  }

  static bool TryGetStaticColorValue(ColorAnimation anim, out Color value) {
    value = default;
    if (anim == null || anim.sequences == null || anim.sequences.Count == 0) return false;
    if (anim.staticEvaluationState == StaticEvalStatic) {
      value = anim.staticValue;
      return true;
    }
    if (anim.staticEvaluationState == StaticEvalDynamic) return false;

    value = anim.sequences[0].to;
    for (int i = 0; i < anim.sequences.Count; i++) {
      var seq = anim.sequences[i];
      var delta = seq.to - seq.from;
      if (((Vector4)delta).sqrMagnitude > 0.0001f) {
        anim.staticEvaluationState = StaticEvalDynamic;
        return false;
      }
      value = seq.to;
    }
    anim.staticValue = value;
    anim.staticEvaluationState = StaticEvalStatic;
    return true;
  }

  static bool TryGetStaticVectorValue(VectorAnimation anim, out Vector4 value) {
    value = default;
    if (anim == null || anim.sequences == null || anim.sequences.Count == 0) return false;
    if (anim.staticEvaluationState == StaticEvalStatic) {
      value = anim.staticValue;
      return true;
    }
    if (anim.staticEvaluationState == StaticEvalDynamic) return false;

    value = anim.sequences[0].to;
    for (int i = 0; i < anim.sequences.Count; i++) {
      var seq = anim.sequences[i];
      if ((seq.to - seq.from).sqrMagnitude > 0.0001f) {
        anim.staticEvaluationState = StaticEvalDynamic;
        return false;
      }
      value = seq.to;
    }
    anim.staticValue = value;
    anim.staticEvaluationState = StaticEvalStatic;
    return true;
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

  void FinalizeStep(FloatAnimation anim) {
    anim.timer = 0f;
    anim.currentSequenceIndex++;
    if (anim.currentSequenceIndex >= anim.sequences.Count) {
      anim.currentSequenceIndex = 0;
      anim.isDone = true;
    }
  }

  void FinalizeStep(ColorAnimation anim) {
    anim.timer = 0f;
    anim.currentSequenceIndex++;
    if (anim.currentSequenceIndex >= anim.sequences.Count) {
      anim.currentSequenceIndex = 0;
      anim.isDone = true;
    }
  }

  void FinalizeStep(VectorAnimation anim) {
    anim.timer = 0f;
    anim.currentSequenceIndex++;
    if (anim.currentSequenceIndex >= anim.sequences.Count) {
      anim.currentSequenceIndex = 0;
      anim.isDone = true;
    }
  }

  public void ToggleKeywords() {
    if (!TryResolveMaterial()) return;
    foreach (var kw in keywordToggles) {
      if (string.IsNullOrEmpty(kw.keyword)) continue;
      if (kw.enabled) _material.EnableKeyword(kw.keyword);
      else _material.DisableKeyword(kw.keyword);
    }
  }

  [ForceUpdate]
  public void ApplyAllProperties(bool force) {
    if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
    if (!TryResolveRenderer()) return;
    _renderer.GetPropertyBlock(_propBlock);
    ToggleKeywords();
    foreach (var anim in floatAnimations) if (anim.sequences.Count > 0 && anim.propHash != 0) _propBlock.SetFloat(anim.propHash, anim.sequences[0].from);
    foreach (var anim in colorAnimations) if (anim.sequences.Count > 0 && anim.propHash != 0) _propBlock.SetColor(anim.propHash, anim.sequences[0].from);
    foreach (var anim in vectorAnimations) if (anim.sequences.Count > 0 && anim.propHash != 0) _propBlock.SetVector(anim.propHash, anim.sequences[0].from);
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
    keywordToggles.RemoveAll(kw => kw.keyword == keyword);
    keywordDict.Remove(keyword);
    var kw = new KeywordToggle { keyword = keyword, enabled = enabled };
    kw.CacheHash();
    keywordToggles.Add(kw);
    keywordDict[keyword] = kw;
    ToggleKeywords();
  }


  public void AddFloatSequence(string prop, float from, float to, float duration, float delay = 0f, bool loop = false, AnimationCurve easing = null, bool replaceExisting = true, bool autoPlay = true) {
    if (!floatAnimDict.TryGetValue(prop, out var anim)) {
      anim = new FloatAnimation { prop = prop, loop = loop, autoPlay = autoPlay };
      anim.CacheHash();
      floatAnimations.Add(anim);
      floatAnimDict[prop] = anim;
    }
    else if (replaceExisting) {
      anim.sequences.Clear();
      anim.currentSequenceIndex = 0;
      anim.timer = 0f;
      anim.isDone = false;
      anim.staticEvaluationState = StaticEvalUnknown;
    }
    anim.loop = loop;
    anim.autoPlay = autoPlay;
    anim.sequences.Add(new Sequence<float> { from = from, to = to, duration = duration, delay = delay, easing = easing ?? AnimationCurve.Linear(0, 0, 1, 1) });
    anim.isDone = false;
    anim.staticEvaluationState = StaticEvalUnknown;
    if (ShouldActivateOnAdd(anim.autoPlay) && !activeFloatAnimations.Contains(anim)) activeFloatAnimations.Add(anim);
    SetUpdateActiveState();
  }

  public void AddColorSequence(string prop, Color from, Color to, float duration, float delay = 0f, bool loop = false, AnimationCurve easing = null, bool replaceExisting = true, bool autoPlay = true) {
    if (!colorAnimDict.TryGetValue(prop, out var anim)) {
      anim = new ColorAnimation { prop = prop, loop = loop, autoPlay = autoPlay };
      anim.CacheHash();
      colorAnimations.Add(anim);
      colorAnimDict[prop] = anim;
    }
    else if (replaceExisting) {
      anim.sequences.Clear();
      anim.currentSequenceIndex = 0;
      anim.timer = 0f;
      anim.isDone = false;
      anim.staticEvaluationState = StaticEvalUnknown;
    }
    anim.loop = loop;
    anim.autoPlay = autoPlay;
    anim.sequences.Add(new Sequence<Color> { from = from, to = to, duration = duration, delay = delay, easing = easing ?? AnimationCurve.Linear(0, 0, 1, 1) });
    anim.isDone = false;
    anim.staticEvaluationState = StaticEvalUnknown;
    if (ShouldActivateOnAdd(anim.autoPlay) && !activeColorAnimations.Contains(anim)) activeColorAnimations.Add(anim);
    SetUpdateActiveState();
  }

  public void AddVectorSequence(string prop, Vector4 from, Vector4 to, float duration, float delay = 0f, bool loop = false, AnimationCurve easing = null, bool replaceExisting = true, bool autoPlay = true) {
    if (!vectorAnimDict.TryGetValue(prop, out var anim)) {
      anim = new VectorAnimation { prop = prop, loop = loop, autoPlay = autoPlay };
      anim.CacheHash();
      vectorAnimations.Add(anim);
      vectorAnimDict[prop] = anim;
    }
    else if (replaceExisting) {
      anim.sequences.Clear();
      anim.currentSequenceIndex = 0;
      anim.timer = 0f;
      anim.isDone = false;
      anim.staticEvaluationState = StaticEvalUnknown;
    }
    anim.loop = loop;
    anim.autoPlay = autoPlay;
    anim.sequences.Add(new Sequence<Vector4> { from = from, to = to, duration = duration, delay = delay, easing = easing ?? AnimationCurve.Linear(0, 0, 1, 1) });
    anim.isDone = false;
    anim.staticEvaluationState = StaticEvalUnknown;
    if (ShouldActivateOnAdd(anim.autoPlay) && !activeVectorAnimations.Contains(anim)) activeVectorAnimations.Add(anim);
    SetUpdateActiveState();
  }

  public void AddTextureAssignment(string prop, Sprite texture) {
    textureAssignments.RemoveAll(a => a.prop == prop);
    var assign = new TextureAssignment { prop = prop, texture = texture };
    assign.CacheHash();
    if (assign.propHash == 0 || assign.texture == null) return;
    textureAssignments.Add(assign);
    if (!TryResolveRenderer()) return;
    if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
    _renderer.GetPropertyBlock(_propBlock);
    _propBlock.SetTexture(assign.propHash, assign.texture.texture);
    _renderer.SetPropertyBlock(_propBlock);
    assign.isAssigned = true;
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
