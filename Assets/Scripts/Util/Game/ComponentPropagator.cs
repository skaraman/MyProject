using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[ExecuteAlways]
public class ComponentPropagator : MonoBehaviour {
  [Serializable]
  public class ComponentToggle {
    public Component component;
    public bool propagate;
  }

  [SerializeField] public List<ComponentToggle> components = new();
  int lastChildCount = -1;

  void OnEnable() {
    lastChildCount = transform.childCount;
    ApplyPropagation();
  }

  void OnValidate() {
  #if UNITY_EDITOR
    RefreshComponentList();
    UnityEditor.EditorApplication.delayCall += () => {
      if (this != null && gameObject != null) ApplyPropagation();
    };
  #endif
  }

  void Update() {
    var current = transform.childCount;
    if (current != lastChildCount) {
      Debug.Log($"[ComponentPropagator] Children changed: {lastChildCount} -> {current}");
      lastChildCount = current;
      ApplyPropagation();
    }
  }

  void RefreshComponentList() {
    var current = new List<ComponentToggle>();
    foreach (var c in GetComponents<Component>()) {
      if (c is Transform || c is ComponentPropagator) continue;
      var existing = components.Find(e => e.component == c);
      if (existing != null) current.Add(existing);
      else current.Add(new ComponentToggle { component = c, propagate = false });
    }
    components = current;
  }

  [ForceUpdate]
  void ApplyPropagation() {
    foreach (var toggle in components) {
      if (!toggle.propagate || toggle.component == null) continue;
      var type = toggle.component.GetType();
      var children = GetComponentsInChildren(type, true);
      foreach (var target in children) {
        if (target == toggle.component) continue;
        if (type.Name.Contains("AllIn1AnimatorInspector")) {
          CopyAllIn1AnimatorLists(toggle.component, target);
          InvokeForceUpdate(target);
          continue;
        }
        CopyFields(toggle.component, target);
        CopyProperties(toggle.component, target);
        InvokeForceUpdate(target);
      }
    }
  }

  void CopyFields(object source, object target) {
    var fields = source.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    foreach (var f in fields) {
      var name = f.Name.ToLower();
      if (f.IsInitOnly || f.IsLiteral) continue;
      if (name.Contains("material") || name.Contains("shader") || name.Contains("texture") || name == "sprite") continue;
      if (f.FieldType == typeof(Material) || f.FieldType == typeof(Shader) || f.FieldType == typeof(Texture) || f.FieldType == typeof(Texture2D)) continue;
      if (name == "m_enabled") continue;
      try {
        var val = f.GetValue(source);
        if (f.FieldType.Name.ToLower().Contains("sprite") && val == null) continue;
        f.SetValue(target, val);
      } catch {}
    }
  }

  void CopyProperties(object source, object target) {
    var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
    foreach (var p in props) {
      var name = p.Name.ToLower();
      if (!p.CanRead || !p.CanWrite) continue;
      if (name == "enabled") continue;
      if (name.Contains("material") || name.Contains("shader") || name.Contains("texture") || name == "sprite") continue;
      if (p.PropertyType == typeof(Material) || p.PropertyType == typeof(Shader) || p.PropertyType == typeof(Texture) || p.PropertyType == typeof(Texture2D)) continue;
      try {
        var val = p.GetValue(source);
        if (p.PropertyType.Name.ToLower().Contains("sprite") && val == null) continue;
        p.SetValue(target, val);
      } catch {}
    }
  }

  void CopyAllIn1AnimatorLists(object source, object target) {
    var s = source as AllIn1AnimatorInspector;
    var t = target as AllIn1AnimatorInspector;
    if (s == null || t == null) return;

    t.keywordToggles = new List<AllIn1AnimatorInspector.KeywordToggle>();
    foreach (var item in s.keywordToggles) {
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
      var copy = new AllIn1AnimatorInspector.ColorAnimation {
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
    foreach (var item in s.vectorAnimations) {
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
      t.textureAssignments.Add(new AllIn1AnimatorInspector.TextureAssignment {
        prop = item.prop,
        propHash = item.propHash,
        texture = item.texture,
        isAssigned = item.isAssigned
      });
    }
  }

  void InvokeForceUpdate(object target) {
    var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    foreach (var method in methods) {
      if (method.GetCustomAttribute(typeof(ForceUpdateAttribute)) != null) {
        try { method.Invoke(target, null); } catch {}
        break;
      }
    }
  }
}
