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

  void OnValidate() {
    RefreshComponentList();
    ApplyPropagation();
  }

  [ForceUpdate]
  void RefreshComponentList() {
    var current = new List<ComponentToggle>();
    foreach (var c in GetComponents<Component>()) {
      if (c is Transform || c is ComponentPropagator) continue;
      if (!components.Exists(e => e.component == c)) {
        current.Add(new ComponentToggle { component = c, propagate = false });
      } else {
        current.Add(components.Find(e => e.component == c));
      }
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
        CopyFields(toggle.component, target);
        CopyProperties(toggle.component, target);
      }
    }
  }

  void CopyFields(object source, object target) {
    var fields = source.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    foreach (var f in fields) {
      if (f.IsInitOnly || f.IsLiteral) continue;
      if (f.Name == "m_Materials" || f.Name == "m_Material" || f.FieldType == typeof(Material) || f.FieldType == typeof(Shader)) continue;
      if (typeof(Behaviour).IsAssignableFrom(source.GetType()) && f.Name == "m_Enabled") continue;
      try { f.SetValue(target, f.GetValue(source)); } catch {}
    }
  }

  void CopyProperties(object source, object target) {
    var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
    foreach (var p in props) {
      if (!p.CanRead || !p.CanWrite) continue;
      if (p.Name == "name" || p.Name == "material" || p.Name == "sharedMaterial") continue;
      if (p.PropertyType == typeof(Material) || p.PropertyType == typeof(Shader)) continue;
      if (typeof(Behaviour).IsAssignableFrom(source.GetType()) && p.Name == "enabled") continue;
      if (source is Renderer && p.Name == "material") continue;
      try { p.SetValue(target, p.GetValue(source)); } catch {}
    }
  }

}
