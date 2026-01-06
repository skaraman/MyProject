using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using CustomInspector;

public class ComponentPropagator : MonoBehaviour {
  [Serializable]
  public class ComponentToggle {
    public Component component;
    public bool propagate;
  }

  [SerializeField] public List<ComponentToggle> components = new();
  [Button(nameof(ForcePropagation), label = "Refresh")][HideField] public bool _bool;

  int _fieldsSkippedCulling;
  int _propertiesSkippedCulling;

  void Start() {
    ForcePropagation();
  }

  public void ForcePropagation() {
    RefreshComponentList();
    ApplyPropagation();
  }

  void RefreshComponentList() {
    var current = new List<ComponentToggle>();
    foreach (var c in GetComponents<Component>())
    {
      if (c is Transform || c is ComponentPropagator) continue;
      var existing = components.Find(e => e.component == c);
      if (existing != null) current.Add(existing);
      else current.Add(new ComponentToggle { component = c, propagate = false });
    }
    components = current;
  }

  void ApplyPropagation() {
    _fieldsSkippedCulling = 0;
    _propertiesSkippedCulling = 0;
    foreach (var toggle in components) {
      if (!toggle.propagate || toggle.component == null) continue;
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
        if (type.Name.Contains("AllIn1AnimatorInspector")) {
          CopyAllIn1AnimatorLists(toggle.component, target);
          InvokeForceUpdate(target);
          target.GetComponent<AllIn1AnimatorInspector>().Refresh();
          continue;
        }
        CopyFields(toggle.component, target);
        CopyProperties(toggle.component, target);
        InvokeForceUpdate(target);
      }
    }
    Debug.Log($"[ComponentPropagator] Skips due to culling-related members fields={_fieldsSkippedCulling} properties={_propertiesSkippedCulling}");
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
    var fields = source.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
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
    var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
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

  void CopyAllIn1AnimatorLists(object source, object target)  {
    var s = source as AllIn1AnimatorInspector;
    var t = target as AllIn1AnimatorInspector;
    if (s == null || t == null) return;
    t.keywordToggles = new List<AllIn1AnimatorInspector.KeywordToggle>();
    foreach (var item in s.keywordToggles)    {
      t.keywordToggles.Add(new AllIn1AnimatorInspector.KeywordToggle {
        keyword = item.keyword,
        enabled = item.enabled,
        keywordHash = item.keywordHash
      });
    }
    t.floatAnimations = new List<AllIn1AnimatorInspector.FloatAnimation>();
    foreach (var item in s.floatAnimations) {
      var copy = new AllIn1AnimatorInspector.FloatAnimation {
        prop = item.prop,
        propHash = item.propHash,
        loop = item.loop,
        currentSequenceIndex = 0,
        timer = 0f,
        isDone = false
      };
      copy.sequences = new List<AllIn1AnimatorInspector.Sequence<float>>(item.sequences);
      t.floatAnimations.Add(copy);
    }
    t.colorAnimations = new List<AllIn1AnimatorInspector.ColorAnimation>();
    foreach (var item in s.colorAnimations) {
      var copy = new AllIn1AnimatorInspector.ColorAnimation  {
        prop = item.prop,
        propHash = item.propHash,
        loop = item.loop,
        currentSequenceIndex = 0,
        timer = 0f,
        isDone = false
      };
      copy.sequences = new List<AllIn1AnimatorInspector.Sequence<Color>>(item.sequences);
      t.colorAnimations.Add(copy);
    }
    t.vectorAnimations = new List<AllIn1AnimatorInspector.VectorAnimation>();
    foreach (var item in s.vectorAnimations)  {
      var copy = new AllIn1AnimatorInspector.VectorAnimation {
        prop = item.prop,
        propHash = item.propHash,
        loop = item.loop,
        currentSequenceIndex = 0,
        timer = 0f,
        isDone = false
      };
      copy.sequences = new List<AllIn1AnimatorInspector.Sequence<Vector4>>(item.sequences);
      t.vectorAnimations.Add(copy);
    }
    t.textureAssignments = new List<AllIn1AnimatorInspector.TextureAssignment>();
    foreach (var item in s.textureAssignments) {
      t.textureAssignments.Add(new AllIn1AnimatorInspector.TextureAssignment  {
        prop = item.prop,
        propHash = item.propHash,
        texture = item.texture,
        isAssigned = item.isAssigned
      });
    }
  }

  void InvokeForceUpdate(object target) {
    var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    foreach (var method in methods){
      if (method.GetCustomAttribute(typeof(ForceUpdateAttribute)) != null) {
        try { method.Invoke(target, null); } catch { }
        break;
      }
    }
  }
}
