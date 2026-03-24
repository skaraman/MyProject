using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using CustomInspector;
using UnityEngine;

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
  public SerializableSortedDictionary<string, string> fromValues = new();

  [Serializable]
  public class SequenceStep {
    public SerializableSortedDictionary<string, string> props = new();
    public float duration = 1f;
    public AnimationCurve easing = AnimationCurve.Linear(0, 0, 1, 1);
    public float randomDuration;
    public SerializableSortedDictionary<string, string> randomProps = new();
  }

  abstract class MemberAccessorBase {
    protected MemberAccessorBase(string key, Type valueType) {
      Key = key;
      ValueType = valueType;
    }

    public string Key { get; }
    public Type ValueType { get; }
    public virtual bool SupportsAnimation => false;
    public abstract object GetBoxed(Component component);
    public abstract void SetBoxed(Component component, object value);
  }

  sealed class FloatMemberAccessor : MemberAccessorBase {
    readonly Func<Component, float> getter;
    readonly Action<Component, float> setter;

    public FloatMemberAccessor(string key, Func<Component, float> getter, Action<Component, float> setter)
      : base(key, typeof(float)) {
      this.getter = getter;
      this.setter = setter;
    }

    public override bool SupportsAnimation => getter != null && setter != null;

    public float Get(Component component) {
      return getter(component);
    }

    public void Set(Component component, float value) {
      setter(component, value);
    }

    public override object GetBoxed(Component component) {
      return getter(component);
    }

    public override void SetBoxed(Component component, object value) {
      if (value is float floatValue) {
        setter(component, floatValue);
        return;
      }
      if (value is int intValue) {
        setter(component, intValue);
      }
    }
  }

  sealed class IntMemberAccessor : MemberAccessorBase {
    readonly Func<Component, int> getter;
    readonly Action<Component, int> setter;

    public IntMemberAccessor(string key, Func<Component, int> getter, Action<Component, int> setter)
      : base(key, typeof(int)) {
      this.getter = getter;
      this.setter = setter;
    }

    public override bool SupportsAnimation => getter != null && setter != null;

    public int Get(Component component) {
      return getter(component);
    }

    public void Set(Component component, int value) {
      setter(component, value);
    }

    public override object GetBoxed(Component component) {
      return getter(component);
    }

    public override void SetBoxed(Component component, object value) {
      if (value is int intValue) {
        setter(component, intValue);
        return;
      }
      if (value is float floatValue) {
        setter(component, Mathf.RoundToInt(floatValue));
      }
    }
  }

  sealed class ReflectionMemberAccessor : MemberAccessorBase {
    readonly FieldInfo field;
    readonly PropertyInfo property;

    public ReflectionMemberAccessor(string key, FieldInfo field)
      : base(key, field != null ? field.FieldType : typeof(object)) {
      this.field = field;
    }

    public ReflectionMemberAccessor(string key, PropertyInfo property)
      : base(key, property != null ? property.PropertyType : typeof(object)) {
      this.property = property;
    }

    public override object GetBoxed(Component component) {
      if (field != null) {
        return field.GetValue(component);
      }
      if (property != null && property.CanRead) {
        return property.GetValue(component);
      }
      return null;
    }

    public override void SetBoxed(Component component, object value) {
      if (field != null) {
        field.SetValue(component, value);
        return;
      }
      if (property != null && property.CanWrite) {
        property.SetValue(component, value);
      }
    }
  }

  abstract class RuntimeBindingBase {
    public abstract void EnterStep(bool useFromOverride);
    public abstract void Apply(float eased);
  }

  abstract class FromValueBindingBase {
    public abstract void Apply();
  }

  sealed class FloatRuntimeBinding : RuntimeBindingBase {
    readonly Component component;
    readonly FloatMemberAccessor accessor;
    readonly bool isBy;
    readonly float configuredValue;
    readonly float randomRange;
    readonly bool hasFromOverride;
    readonly float fromOverride;
    float from;
    float to;

    public FloatRuntimeBinding(
      Component component,
      FloatMemberAccessor accessor,
      bool isBy,
      float configuredValue,
      float randomRange,
      bool hasFromOverride,
      float fromOverride
    ) {
      this.component = component;
      this.accessor = accessor;
      this.isBy = isBy;
      this.configuredValue = configuredValue;
      this.randomRange = Mathf.Max(randomRange, 0f);
      this.hasFromOverride = hasFromOverride;
      this.fromOverride = fromOverride;
    }

    public override void EnterStep(bool useFromOverride) {
      from = useFromOverride && hasFromOverride ? fromOverride : accessor.Get(component);
      var randomized = configuredValue;
      if (randomRange > 0f) {
        randomized += UnityEngine.Random.Range(0f, randomRange);
      }
      to = isBy ? from + randomized : randomized;
    }

    public override void Apply(float eased) {
      accessor.Set(component, from + (to - from) * eased);
    }
  }

  sealed class IntRuntimeBinding : RuntimeBindingBase {
    readonly Component component;
    readonly IntMemberAccessor accessor;
    readonly bool isBy;
    readonly int configuredValue;
    readonly float randomRange;
    readonly bool hasFromOverride;
    readonly int fromOverride;
    int from;
    int to;

    public IntRuntimeBinding(
      Component component,
      IntMemberAccessor accessor,
      bool isBy,
      int configuredValue,
      float randomRange,
      bool hasFromOverride,
      int fromOverride
    ) {
      this.component = component;
      this.accessor = accessor;
      this.isBy = isBy;
      this.configuredValue = configuredValue;
      this.randomRange = Mathf.Max(randomRange, 0f);
      this.hasFromOverride = hasFromOverride;
      this.fromOverride = fromOverride;
    }

    public override void EnterStep(bool useFromOverride) {
      from = useFromOverride && hasFromOverride ? fromOverride : accessor.Get(component);
      var randomized = configuredValue;
      if (randomRange > 0f) {
        randomized += Mathf.FloorToInt(UnityEngine.Random.Range(0f, randomRange));
      }
      to = isBy ? from + randomized : randomized;
    }

    public override void Apply(float eased) {
      accessor.Set(component, from + Mathf.RoundToInt((to - from) * eased));
    }
  }

  sealed class FloatFromValueBinding : FromValueBindingBase {
    readonly Component component;
    readonly FloatMemberAccessor accessor;
    readonly float value;

    public FloatFromValueBinding(Component component, FloatMemberAccessor accessor, float value) {
      this.component = component;
      this.accessor = accessor;
      this.value = value;
    }

    public override void Apply() {
      accessor.Set(component, value);
    }
  }

  sealed class IntFromValueBinding : FromValueBindingBase {
    readonly Component component;
    readonly IntMemberAccessor accessor;
    readonly int value;

    public IntFromValueBinding(Component component, IntMemberAccessor accessor, int value) {
      this.component = component;
      this.accessor = accessor;
      this.value = value;
    }

    public override void Apply() {
      accessor.Set(component, value);
    }
  }

  sealed class BoxedFromValueBinding : FromValueBindingBase {
    readonly Component component;
    readonly MemberAccessorBase accessor;
    readonly object value;

    public BoxedFromValueBinding(Component component, MemberAccessorBase accessor, object value) {
      this.component = component;
      this.accessor = accessor;
      this.value = value;
    }

    public override void Apply() {
      accessor.SetBoxed(component, value);
    }
  }

  sealed class RuntimeStep {
    public RuntimeBindingBase[] bindings = Array.Empty<RuntimeBindingBase>();
    public float duration;
    public float randomDuration;
    public AnimationCurve easing;
  }

  static readonly AnimationCurve DefaultEasing = AnimationCurve.Linear(0f, 0f, 1f, 1f);

  Action triggerOff;
  float timer;
  int sequenceIt;
  bool typeIsBy;
  int sequenceCount;
  bool hasValidTarget;
  float stepDuration;
  AnimationCurve currentEasing;
  bool runtimeCacheValid;
  Component cachedRuntimeTarget;
  Type cachedTargetType;
  string cachedTypeMode;
  int cachedSequenceCount = -1;
  RuntimeStep[] runtimeSteps = Array.Empty<RuntimeStep>();
  readonly List<FromValueBindingBase> fromValueBindings = new();
  readonly Dictionary<string, MemberAccessorBase> memberCache = new();
  readonly HashSet<string> warnedUnsupportedMembers = new();

  void Start() {
    if (!Application.isPlaying) return;

    if (!string.IsNullOrEmpty(trigger)) {
      triggerOff = MessageBus.On(trigger, _ => Play());
    }
    else {
      Play();
    }

    SetRuntimeUpdateState();
  }

  void OnValidate() {
    runtimeCacheValid = false;
    hasValidTarget = target != null;
    sequenceCount = sequence != null ? sequence.Count : 0;
    SetRuntimeUpdateState();
  }

  void OnDestroy() {
    triggerOff?.Invoke();
    triggerOff = null;
    runtimeSteps = Array.Empty<RuntimeStep>();
    fromValueBindings.Clear();
    memberCache.Clear();
    warnedUnsupportedMembers.Clear();
  }

  public void Restart() {
    EnsureRuntimeCache();
    ResetPlaybackState();
    SetRuntimeUpdateState();
  }

  public void Reset() {
    if (target == null) {
      hasValidTarget = false;
      return;
    }

    EnsureRuntimeCache(forceRebuild: true);
    fromValues.Clear();

    var stepCount = sequence != null ? sequence.Count : 0;
    for (var stepIndex = 0; stepIndex < stepCount; stepIndex++) {
      var step = sequence[stepIndex];
      if (step == null || step.props == null) continue;

      foreach (var pair in step.props) {
        var key = pair.key;
        if (string.IsNullOrEmpty(key) || fromValues.ContainsKey(key)) continue;

        var accessor = GetCachedMember(key);
        if (accessor == null) continue;

        var value = accessor.GetBoxed(target);
        if (value is float || value is int || value is string) {
          fromValues[key] = value.ToString();
        }
      }
    }

#if UNITY_EDITOR
    if (!Application.isPlaying && fromValues.Count == 0) {
      fromValues[" "] = " ";
    }
#endif

    runtimeCacheValid = false;
  }

  public void Play() {
    EnsureRuntimeCache();
    ResetPlaybackState();
    paused = false;
    SetRuntimeUpdateState();
  }

  public void Stop() {
    paused = true;
    EnsureRuntimeCache();
    ResetPlaybackState();
    SetRuntimeUpdateState();
  }

  void Update() {
    if (paused || sequenceCount <= 0 || !hasValidTarget) {
      hasValidTarget = target != null;
      SetRuntimeUpdateState();
      return;
    }

    timer += target != null ? TimeScale.GetDeltaTime(target.transform) : TimeScale.GetDeltaTime(this);

    if (stepDuration <= 0f) {
      ApplyCurrentStep(1f);
      ProcessStepComplete();
      return;
    }

    var normalized = Mathf.Clamp01(timer / stepDuration);
    var easing = currentEasing != null ? currentEasing : DefaultEasing;
    var eased = easing.Evaluate(normalized);
    ApplyCurrentStep(eased);

    if (normalized >= 1f) {
      ProcessStepComplete();
    }
  }

  void EnsureRuntimeCache(bool forceRebuild = false) {
    var nextTargetType = target != null ? target.GetType() : null;
    var nextSequenceCount = sequence != null ? sequence.Count : 0;
    if (!forceRebuild &&
        runtimeCacheValid &&
        cachedRuntimeTarget == target &&
        cachedTargetType == nextTargetType &&
        cachedSequenceCount == nextSequenceCount &&
        string.Equals(cachedTypeMode, type, StringComparison.Ordinal)) {
      hasValidTarget = target != null;
      sequenceCount = runtimeSteps.Length;
      typeIsBy = string.Equals(type, "By", StringComparison.Ordinal);
      return;
    }

    RebuildRuntimeCache(nextTargetType, nextSequenceCount);
  }

  void RebuildRuntimeCache(Type nextTargetType, int nextSequenceCount) {
    cachedRuntimeTarget = target;
    cachedTargetType = nextTargetType;
    cachedTypeMode = type;
    cachedSequenceCount = nextSequenceCount;
    runtimeCacheValid = true;
    hasValidTarget = target != null;
    typeIsBy = string.Equals(type, "By", StringComparison.Ordinal);
    memberCache.Clear();
    warnedUnsupportedMembers.Clear();
    fromValueBindings.Clear();
    runtimeSteps = Array.Empty<RuntimeStep>();
    sequenceCount = nextSequenceCount;

    if (!hasValidTarget || nextTargetType == null || nextSequenceCount <= 0) {
      return;
    }

    BuildFromValueBindings();

    var builtSteps = new RuntimeStep[nextSequenceCount];
    for (var stepIndex = 0; stepIndex < nextSequenceCount; stepIndex++) {
      builtSteps[stepIndex] = BuildRuntimeStep(sequence[stepIndex]);
    }

    runtimeSteps = builtSteps;
    sequenceCount = runtimeSteps.Length;
  }

  RuntimeStep BuildRuntimeStep(SequenceStep step) {
    var runtimeStep = new RuntimeStep {
      duration = step != null ? Mathf.Max(step.duration, 0f) : 0f,
      randomDuration = step != null ? Mathf.Max(step.randomDuration, 0f) : 0f,
      easing = step != null && step.easing != null ? step.easing : DefaultEasing
    };
    if (step == null || step.props == null || target == null) {
      return runtimeStep;
    }

    var bindings = new List<RuntimeBindingBase>();
    foreach (var pair in step.props) {
      var key = pair.key;
      if (string.IsNullOrEmpty(key)) continue;

      var accessor = GetCachedMember(key);
      if (accessor == null) continue;

      var randomRange = GetParsedRandomRange(step.randomProps, key);
      if (accessor is FloatMemberAccessor floatAccessor) {
        if (!float.TryParse(pair.value, out var targetValue)) continue;
        var hasFromOverride = TryGetFloatFromOverride(key, out var fromOverride);
        bindings.Add(new FloatRuntimeBinding(
          target,
          floatAccessor,
          typeIsBy,
          targetValue,
          randomRange,
          hasFromOverride,
          fromOverride
        ));
        continue;
      }

      if (accessor is IntMemberAccessor intAccessor) {
        if (!int.TryParse(pair.value, out var targetValue)) continue;
        var hasFromOverride = TryGetIntFromOverride(key, out var fromOverride);
        bindings.Add(new IntRuntimeBinding(
          target,
          intAccessor,
          typeIsBy,
          targetValue,
          randomRange,
          hasFromOverride,
          fromOverride
        ));
        continue;
      }

      WarnUnsupportedMember(accessor);
    }

    runtimeStep.bindings = bindings.Count > 0 ? bindings.ToArray() : Array.Empty<RuntimeBindingBase>();
    return runtimeStep;
  }

  void BuildFromValueBindings() {
    if (fromValues == null || target == null) return;

    foreach (var pair in fromValues) {
      var key = pair.key;
      if (string.IsNullOrEmpty(key)) continue;

      var accessor = GetCachedMember(key);
      if (accessor == null) continue;

      if (accessor is FloatMemberAccessor floatAccessor) {
        if (float.TryParse(pair.value, out var floatValue)) {
          fromValueBindings.Add(new FloatFromValueBinding(target, floatAccessor, floatValue));
        }
        continue;
      }

      if (accessor is IntMemberAccessor intAccessor) {
        if (int.TryParse(pair.value, out var intValue)) {
          fromValueBindings.Add(new IntFromValueBinding(target, intAccessor, intValue));
        }
        continue;
      }

      if (accessor.ValueType == typeof(string)) {
        fromValueBindings.Add(new BoxedFromValueBinding(target, accessor, pair.value));
        continue;
      }

      WarnUnsupportedMember(accessor);
    }
  }

  MemberAccessorBase GetCachedMember(string key) {
    if (target == null || string.IsNullOrEmpty(key)) return null;

    var targetType = target.GetType();
    if (cachedTargetType != targetType) {
      cachedTargetType = targetType;
      memberCache.Clear();
      warnedUnsupportedMembers.Clear();
    }

    if (memberCache.TryGetValue(key, out var accessor)) {
      return accessor;
    }

    accessor = CreateMemberAccessor(targetType, key);
    memberCache[key] = accessor;
    return accessor;
  }

  MemberAccessorBase CreateMemberAccessor(Type targetType, string key) {
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    var field = targetType.GetField(key, Flags);
    if (field != null) {
      if (field.FieldType == typeof(float)) {
        var getter = CreateFloatGetter(field);
        var setter = CreateFloatSetter(field);
        if (getter != null && setter != null) {
          return new FloatMemberAccessor(key, getter, setter);
        }
      }

      if (field.FieldType == typeof(int)) {
        var getter = CreateIntGetter(field);
        var setter = CreateIntSetter(field);
        if (getter != null && setter != null) {
          return new IntMemberAccessor(key, getter, setter);
        }
      }

      return new ReflectionMemberAccessor(key, field);
    }

    var property = targetType.GetProperty(key, Flags);
    if (property == null || property.GetIndexParameters().Length > 0) {
      return null;
    }

    if (property.PropertyType == typeof(float) && property.CanRead && property.CanWrite) {
      var getter = CreateFloatGetter(property);
      var setter = CreateFloatSetter(property);
      if (getter != null && setter != null) {
        return new FloatMemberAccessor(key, getter, setter);
      }
    }

    if (property.PropertyType == typeof(int) && property.CanRead && property.CanWrite) {
      var getter = CreateIntGetter(property);
      var setter = CreateIntSetter(property);
      if (getter != null && setter != null) {
        return new IntMemberAccessor(key, getter, setter);
      }
    }

    if (property.CanRead && property.CanWrite) {
      return new ReflectionMemberAccessor(key, property);
    }

    return null;
  }

  static Func<Component, float> CreateFloatGetter(FieldInfo field) {
    try {
      var componentParameter = Expression.Parameter(typeof(Component), "component");
      var targetExpression = Expression.Convert(componentParameter, field.DeclaringType);
      var fieldExpression = Expression.Field(targetExpression, field);
      return Expression.Lambda<Func<Component, float>>(fieldExpression, componentParameter).Compile();
    }
    catch {
      return null;
    }
  }

  static Action<Component, float> CreateFloatSetter(FieldInfo field) {
    try {
      var componentParameter = Expression.Parameter(typeof(Component), "component");
      var valueParameter = Expression.Parameter(typeof(float), "value");
      var targetExpression = Expression.Convert(componentParameter, field.DeclaringType);
      var fieldExpression = Expression.Field(targetExpression, field);
      var assignExpression = Expression.Assign(fieldExpression, valueParameter);
      return Expression.Lambda<Action<Component, float>>(assignExpression, componentParameter, valueParameter).Compile();
    }
    catch {
      return null;
    }
  }

  static Func<Component, int> CreateIntGetter(FieldInfo field) {
    try {
      var componentParameter = Expression.Parameter(typeof(Component), "component");
      var targetExpression = Expression.Convert(componentParameter, field.DeclaringType);
      var fieldExpression = Expression.Field(targetExpression, field);
      return Expression.Lambda<Func<Component, int>>(fieldExpression, componentParameter).Compile();
    }
    catch {
      return null;
    }
  }

  static Action<Component, int> CreateIntSetter(FieldInfo field) {
    try {
      var componentParameter = Expression.Parameter(typeof(Component), "component");
      var valueParameter = Expression.Parameter(typeof(int), "value");
      var targetExpression = Expression.Convert(componentParameter, field.DeclaringType);
      var fieldExpression = Expression.Field(targetExpression, field);
      var assignExpression = Expression.Assign(fieldExpression, valueParameter);
      return Expression.Lambda<Action<Component, int>>(assignExpression, componentParameter, valueParameter).Compile();
    }
    catch {
      return null;
    }
  }

  static Func<Component, float> CreateFloatGetter(PropertyInfo property) {
    var getter = property.GetGetMethod(true);
    if (getter == null) return null;

    try {
      var componentParameter = Expression.Parameter(typeof(Component), "component");
      var targetExpression = Expression.Convert(componentParameter, property.DeclaringType);
      var callExpression = Expression.Call(targetExpression, getter);
      return Expression.Lambda<Func<Component, float>>(callExpression, componentParameter).Compile();
    }
    catch {
      return null;
    }
  }

  static Action<Component, float> CreateFloatSetter(PropertyInfo property) {
    var setter = property.GetSetMethod(true);
    if (setter == null) return null;

    try {
      var componentParameter = Expression.Parameter(typeof(Component), "component");
      var valueParameter = Expression.Parameter(typeof(float), "value");
      var targetExpression = Expression.Convert(componentParameter, property.DeclaringType);
      var callExpression = Expression.Call(targetExpression, setter, valueParameter);
      return Expression.Lambda<Action<Component, float>>(callExpression, componentParameter, valueParameter).Compile();
    }
    catch {
      return null;
    }
  }

  static Func<Component, int> CreateIntGetter(PropertyInfo property) {
    var getter = property.GetGetMethod(true);
    if (getter == null) return null;

    try {
      var componentParameter = Expression.Parameter(typeof(Component), "component");
      var targetExpression = Expression.Convert(componentParameter, property.DeclaringType);
      var callExpression = Expression.Call(targetExpression, getter);
      return Expression.Lambda<Func<Component, int>>(callExpression, componentParameter).Compile();
    }
    catch {
      return null;
    }
  }

  static Action<Component, int> CreateIntSetter(PropertyInfo property) {
    var setter = property.GetSetMethod(true);
    if (setter == null) return null;

    try {
      var componentParameter = Expression.Parameter(typeof(Component), "component");
      var valueParameter = Expression.Parameter(typeof(int), "value");
      var targetExpression = Expression.Convert(componentParameter, property.DeclaringType);
      var callExpression = Expression.Call(targetExpression, setter, valueParameter);
      return Expression.Lambda<Action<Component, int>>(callExpression, componentParameter, valueParameter).Compile();
    }
    catch {
      return null;
    }
  }

  void ResetPlaybackState() {
    timer = 0f;
    sequenceIt = 0;
    ApplyFromValueBindings();
    EnterCurrentStep(isFirstStep: true);
  }

  void ApplyFromValueBindings() {
    for (var i = 0; i < fromValueBindings.Count; i++) {
      fromValueBindings[i].Apply();
    }
  }

  void EnterCurrentStep(bool isFirstStep) {
    timer = 0f;
    if (runtimeSteps == null || sequenceIt < 0 || sequenceIt >= runtimeSteps.Length) {
      stepDuration = 0f;
      currentEasing = DefaultEasing;
      return;
    }

    var step = runtimeSteps[sequenceIt];
    stepDuration = step.duration;
    if (step.randomDuration > 0f) {
      stepDuration += UnityEngine.Random.Range(0f, step.randomDuration);
    }
    currentEasing = step.easing != null ? step.easing : DefaultEasing;

    var bindings = step.bindings;
    for (var i = 0; i < bindings.Length; i++) {
      bindings[i].EnterStep(isFirstStep);
    }
  }

  void ApplyCurrentStep(float eased) {
    if (runtimeSteps == null || sequenceIt < 0 || sequenceIt >= runtimeSteps.Length) return;

    var bindings = runtimeSteps[sequenceIt].bindings;
    for (var i = 0; i < bindings.Length; i++) {
      bindings[i].Apply(eased);
    }
  }

  void ProcessStepComplete() {
    timer = 0f;
    sequenceIt++;
    if (sequenceIt >= sequenceCount) {
      callback?.Invoke();
      if (loop) {
        ResetPlaybackState();
      }
      else {
        Stop();
      }
      return;
    }

    EnterCurrentStep(isFirstStep: false);
  }

  bool TryGetFloatFromOverride(string key, out float value) {
    value = 0f;
    return fromValues != null &&
           fromValues.TryGetValue(key, out var textValue) &&
           float.TryParse(textValue, out value);
  }

  bool TryGetIntFromOverride(string key, out int value) {
    value = 0;
    return fromValues != null &&
           fromValues.TryGetValue(key, out var textValue) &&
           int.TryParse(textValue, out value);
  }

  static float GetParsedRandomRange(SerializableSortedDictionary<string, string> randomProps, string key) {
    if (randomProps == null || !randomProps.TryGetValue(key, out var textValue)) return 0f;
    return float.TryParse(textValue, out var value) ? Mathf.Max(value, 0f) : 0f;
  }

  void WarnUnsupportedMember(MemberAccessorBase accessor) {
    if (accessor == null || string.IsNullOrEmpty(accessor.Key)) return;
    if (!warnedUnsupportedMembers.Add(accessor.Key)) return;

    Debug.LogWarning(
      "[AnimateFields] Skipping unsupported animated member" +
      " target='" + (target != null ? target.GetType().Name : "(null)") + "'" +
      " member='" + accessor.Key + "'" +
      " type='" + accessor.ValueType.Name + "'",
      this
    );
  }

  void SetRuntimeUpdateState() {
    if (!Application.isPlaying) return;
    enabled = !paused && hasValidTarget && sequenceCount > 0;
  }
}
