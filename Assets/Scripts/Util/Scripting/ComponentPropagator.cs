using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using CustomInspector;

public class ComponentPropagator : MonoBehaviour {
  public bool propagateOnce = true;
  [Serializable]
  public class ComponentToggle {
    public Component component;
    public bool propagate;
  }

  private bool hasPropagated;
  [SerializeField] bool verboseLogging;
  [SerializeField] float initialDelaySeconds = .5f;
  [SerializeField] int initialDelayFrames = 0;

  [SerializeField] public List<ComponentToggle> components = new();
  [Button(nameof(ForcePropagation), label = "Refresh")][HideField] public bool _bool;

  int _fieldsSkippedCulling;
  int _propertiesSkippedCulling;

  private static readonly Dictionary<Type, FieldInfo[]> _cachedFields = new();
  private static readonly Dictionary<Type, PropertyInfo[]> _cachedProperties = new();
  private static readonly Dictionary<Type, MethodInfo> _cachedForceUpdateMethods = new();
  private readonly List<Component> _scratchComponents = new();
  private readonly List<ComponentToggle> _scratchToggles = new();
  private readonly List<AllIn1AnimatorInspector> _scratchAnimators = new();
  private readonly List<SpriteRenderer> _scratchSpriteRenderers = new();

  void OnEnable() {
    if (propagateOnce && hasPropagated) {
      return;
    }
    StartPropagationDelay();
  }

  void StartPropagationDelay() {
    StopAllCoroutines();
    StartCoroutine(PropagationRoutine());
  }

  private WaitForSeconds _cachedWaitForSeconds;
  private WaitForFixedUpdate _cachedWaitForFixedUpdate;

  IEnumerator PropagationRoutine() {
    for (int i = 0; i < initialDelayFrames; i++) {
      yield return null; // wait a frame so dynamically-created children appear
    }
    if (initialDelaySeconds > 0f) {
      if (_cachedWaitForSeconds == null) {
        _cachedWaitForSeconds = new WaitForSeconds(initialDelaySeconds);
      }
      yield return _cachedWaitForSeconds;
    }
    if (_cachedWaitForFixedUpdate == null) {
      _cachedWaitForFixedUpdate = new WaitForFixedUpdate();
    }
    yield return _cachedWaitForFixedUpdate;

    while (isActiveAndEnabled) {
      ForcePropagation();
      if (propagateOnce) {
        yield break;
      }
      yield return _cachedWaitForFixedUpdate;
    }
  }

  public void ForcePropagation() {
    RefreshComponentList();
    ApplyPropagation();
    hasPropagated = true;
    if (!propagateOnce) {
      return;
    }

    StopAllCoroutines();
  }

  void RefreshComponentList() {
    GetComponents(_scratchComponents);
    _scratchToggles.Clear();
    for (int i = 0; i < _scratchComponents.Count; i++) {
      var c = _scratchComponents[i];
      if (c is Transform || c is ComponentPropagator) continue;
      var existing = FindExistingToggle(c);
      if (existing != null) _scratchToggles.Add(existing);
      else _scratchToggles.Add(new ComponentToggle { component = c, propagate = false });
    }
    components.Clear();
    components.AddRange(_scratchToggles);
  }

  ComponentToggle FindExistingToggle(Component component) {
    for (var i = 0; i < components.Count; i++) {
      var toggle = components[i];
      if (toggle != null && toggle.component == component) {
        return toggle;
      }
    }
    return null;
  }

  void ApplyPropagation() {
    _fieldsSkippedCulling = 0;
    _propertiesSkippedCulling = 0;
    foreach (var toggle in components) {
      if (!toggle.propagate || toggle.component == null) continue;
      if (toggle.component is AllIn1AnimatorInspector sourceAnimator) {
        _scratchAnimators.Clear();
        GetComponentsInChildren(true, _scratchAnimators);
        for (var i = 0; i < _scratchAnimators.Count; i++) {
          var targetAnimator = _scratchAnimators[i];
          if (targetAnimator == null || targetAnimator == sourceAnimator) continue;
          targetAnimator.CopyConfigurationFrom(sourceAnimator);
        }
        continue;
      }
      if (toggle.component is SpriteRenderer sourceSpriteRenderer) {
        PropagateSpriteRenderer(sourceSpriteRenderer);
        continue;
      }

      var type = toggle.component.GetType();
      var children = GetComponentsInChildren(type, true);
      foreach (var target in children) {
        if (target == toggle.component) continue;
        if (target is Renderer renderer) {
          renderer.enabled = true;
          if (renderer.bounds.size.magnitude < 0.1f) {
            renderer.bounds = new Bounds(renderer.transform.position, Vector3.one * 100f);
          }
        }
        CopyFields(toggle.component, target);
        CopyProperties(toggle.component, target);
        InvokeForceUpdate(target);
      }
    }
    if (verboseLogging) {
      RuntimeLog.Log($"[ComponentPropagator] Skips due to culling-related members fields={_fieldsSkippedCulling} properties={_propertiesSkippedCulling}");
    }
  }

  void PropagateSpriteRenderer(SpriteRenderer source) {
    _scratchSpriteRenderers.Clear();
    GetComponentsInChildren(true, _scratchSpriteRenderers);
    for (var i = 0; i < _scratchSpriteRenderers.Count; i++) {
      var target = _scratchSpriteRenderers[i];
      if (target == null || target == source) continue;

      target.enabled = true;
      if (target.bounds.size.magnitude < 0.1f) {
        target.bounds = new Bounds(target.transform.position, Vector3.one * 100f);
      }

      // Sprite/material identity remains authored per glyph. Only copy the
      // renderer state the propagator is used to synchronize.
      target.color = source.color;
      target.flipX = source.flipX;
      target.flipY = source.flipY;
      target.sortingLayerID = source.sortingLayerID;
      target.sortingOrder = source.sortingOrder;
      target.maskInteraction = source.maskInteraction;
      target.drawMode = source.drawMode;
      target.size = source.size;
      target.tileMode = source.tileMode;
      target.adaptiveModeThreshold = source.adaptiveModeThreshold;
      target.spriteSortPoint = source.spriteSortPoint;
    }
  }

  bool IsCullingRelatedName(string nLower) {
    if (nLower.Contains("cull")) return true;
    if (nLower.Contains("occlusion")) return true;
    if (nLower.Contains("forcerenderingoff")) return true;
    if (nLower.Contains("renderinglayermask")) return true;
    if (nLower.Contains("shadow")) return true;
    if (nLower.Contains("probe")) return true;
    if (nLower == "isvisible") return true;
    if (nLower == "bounds") return true;
    if (nLower == "cameracullingmask") return true;
    if (nLower == "cullingmask") return true;
    return false;
  }

  void CopyFields(object source, object target) {
    var type = source.GetType();
    if (!_cachedFields.TryGetValue(type, out var fields)) {
      fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
      _cachedFields[type] = fields;
    }
    foreach (var f in fields) {
      if (f.IsInitOnly || f.IsLiteral) continue;
      var n = f.Name;
      var name = n.ToLower();
      if (n is "m_Sprite" or "sprite" or "m_Materials" or "m_Material" or "m_IsPartOfStaticBatch" or "m_BoundingVolume" or "m_StaticBatchRoot" or "m_CullingMask") {
        _fieldsSkippedCulling++;
        continue;
      }
      if (name.Contains("material") || name.Contains("shader") || name.Contains("texture") || name == "sprite") continue;
      if (f.FieldType == typeof(Material) || f.FieldType == typeof(Shader) || f.FieldType == typeof(Texture) || f.FieldType == typeof(Texture2D)) continue;
      if (IsCullingRelatedName(name)) {
        _fieldsSkippedCulling++;
        continue;
      }
      bool isSpriteRenderer = source is SpriteRenderer;
      if (isSpriteRenderer) {
        if (n is "m_Color" or "m_FlipX" or "m_FlipY" or "m_SortingLayerID" or "m_SortingOrder" or "m_MaskInteraction")  {
          try  {
            var val = f.GetValue(source);
            f.SetValue(target, val);
          }
          catch { }
          continue;
        }
      }
      if (!isSpriteRenderer || (!name.Contains("material") && !name.Contains("shader") && !name.Contains("texture") && name != "sprite")) {
        try {
          var val = f.GetValue(source);
          if (f.FieldType.Name.ToLower().Contains("sprite") && val == null) continue;
          f.SetValue(target, val);
        }
        catch { }
      }
    }
  }

  void CopyProperties(object source, object target)  {
    var type = source.GetType();
    if (!_cachedProperties.TryGetValue(type, out var props)) {
      props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
      _cachedProperties[type] = props;
    }
    foreach (var p in props)    {
      if (!p.CanRead || !p.CanWrite) continue;
      var n = p.Name;
      var name = n.ToLower();
      if (n is "sprite" or "material" or "sharedMaterial") continue;
      if (name == "enabled")      {
        _propertiesSkippedCulling++;
        continue;
      }
      if (name.Contains("material") || name.Contains("shader") || name.Contains("texture") || name == "sprite") continue;
      if (p.PropertyType == typeof(Material) || p.PropertyType == typeof(Shader) || p.PropertyType == typeof(Texture) || p.PropertyType == typeof(Texture2D)) continue;
      if (IsCullingRelatedName(name))      {
        _propertiesSkippedCulling++;
        continue;
      }
      bool isSpriteRenderer = source is SpriteRenderer;
      if (isSpriteRenderer)      {
        if (n is "color" or "flipX" or "flipY" or "sortingLayerID" or "sortingLayerName" or "sortingOrder" or "maskInteraction") {
          try{
            var val = p.GetValue(source);
            p.SetValue(target, val);
          }
          catch { }
          continue;
        }
      }
      if (!isSpriteRenderer || (!name.Contains("material") && !name.Contains("shader") && !name.Contains("texture") && name != "sprite")){
        try  {
          var val = p.GetValue(source);
          if (p.PropertyType.Name.ToLower().Contains("sprite") && val == null) continue;
          p.SetValue(target, val);
        }
        catch { }
      }
    }
  }

  void InvokeForceUpdate(object target) {
    var type = target.GetType();
    if (!_cachedForceUpdateMethods.TryGetValue(type, out var forceUpdateMethod)) {
      var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
      foreach (var method in methods){
        if (method.GetCustomAttribute(typeof(ForceUpdateAttribute)) != null) {
          forceUpdateMethod = method;
          break;
        }
      }
      _cachedForceUpdateMethods[type] = forceUpdateMethod;
    }
    if (forceUpdateMethod != null) {
      try { forceUpdateMethod.Invoke(target, null); } catch { }
    }
  }
}
