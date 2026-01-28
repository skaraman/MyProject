using System.Text;
using UnityEngine;
using UnityEngine.U2D.Animation;
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

[ExecuteAlways]
public class SpriteWithNormals : MonoBehaviour {
  public SpriteLibraryAsset colorLibrary;
  public SpriteLibraryAsset normalLibrary;
  public string category = "Breathe";
  public string labelPrefix = "";

  SpriteRenderer _renderer;
  MaterialPropertyBlock _mpb;
  StringBuilder label = new();

  void Awake() {
    _renderer = GetComponent<SpriteRenderer>();
    _mpb = new MaterialPropertyBlock();
    UpdateSpriteAndNormal(0);
  }

  void OnValidate() {
    if (colorLibrary == null || normalLibrary == null) return;
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer == null) return;
    UpdateSpriteAndNormal(0);
  }

  public void SetAnimation(string name) {
    category = name;
  }

  [ForceUpdate]
  public void ForceUpdateSpriteAndNormal() {
    UpdateSpriteAndNormal(0);
  }

  string GetLabel(int frame) {
    label.Clear();
    label.Append(labelPrefix);
    if (frame != 0 && labelPrefix != "") {
      label.Append("_").Append(frame);
    }
    else if (frame != 0 && labelPrefix == "") {
      label.Append(frame);
    }
    var result = label.ToString();
    return result;
  }

  public void UpdateSpriteAndNormal(int frame) {
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    var currentLabel = GetLabel(frame);
    if (colorLibrary == null || normalLibrary == null) {
      Debug.LogError("Sprite libraries are not assigned! " + gameObject.name + " " + gameObject.transform.parent?.name);
      return;
    }
    var colorSprite = colorLibrary.GetSprite(category, currentLabel);
    var normalSprite = normalLibrary.GetSprite(category, currentLabel);
    if (colorSprite == null) {
      //Debug.LogWarning("[SpriteWithNormals] Color sprite is null for category=" + category + " label=" + currentLabel + " on " + gameObject.name);
      return;
    }
    if (normalSprite == null) {
      Debug.LogError("Normal sprite not found for category '" + category + "' with label '" + currentLabel + "' " + gameObject.name);
    }
    _renderer.sprite = colorSprite;
    _mpb ??= new MaterialPropertyBlock();
    _renderer.GetPropertyBlock(_mpb);
    if (normalSprite != null && normalSprite.texture != null) {
      _mpb.SetTexture("_NormalMap", normalSprite.texture);
    }
    else {
      Debug.LogError("Normal sprite or its texture is missing. " + gameObject.name);
    }
    _renderer.SetPropertyBlock(_mpb);
  }

  public void FlipSprite(bool flip) {
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    _renderer.flipX = flip;
  }
}

#if UNITY_EDITOR
[CustomEditor(typeof(SpriteWithNormals))]
public class SpriteWithNormalsEditor : Editor {
  SerializedProperty colorLibraryProp;
  SerializedProperty normalLibraryProp;
  SerializedProperty categoryProp;
  SerializedProperty labelPrefixProp;

  List<string> categoryValues = new();
  string[] categoryDisplay;

  List<string> labelValues = new();
  string[] labelDisplay;

  SpriteLibraryAsset lastColorLibrary;
  string lastCategory;
  bool categoriesDirty = true;
  bool labelsDirty = true;

  void OnEnable() {
    colorLibraryProp = serializedObject.FindProperty("colorLibrary");
    normalLibraryProp = serializedObject.FindProperty("normalLibrary");
    categoryProp = serializedObject.FindProperty("category");
    labelPrefixProp = serializedObject.FindProperty("labelPrefix");
    categoriesDirty = true;
    labelsDirty = true;
  }

  public override void OnInspectorGUI() {
    serializedObject.Update();

    EditorGUILayout.PropertyField(colorLibraryProp);
    EditorGUILayout.PropertyField(normalLibraryProp);

    var t = (SpriteWithNormals)target;

    var currentLib = colorLibraryProp.objectReferenceValue as SpriteLibraryAsset;
    if (currentLib != lastColorLibrary) {
      lastColorLibrary = currentLib;
      categoriesDirty = true;
      labelsDirty = true;
    }

    var needsRefresh = false;

    EnsureCategoriesBuilt(t);
    DrawCategoryDropdown(t, ref needsRefresh);

    EnsureLabelsBuilt(t);
    DrawLabelDropdown(t, ref needsRefresh);

    serializedObject.ApplyModifiedProperties();

    if (needsRefresh) {
      t.ForceUpdateSpriteAndNormal();
    }
  }

  void EnsureCategoriesBuilt(SpriteWithNormals t) {
    if (!categoriesDirty && categoryDisplay != null && categoryDisplay.Length > 0) return;

    categoryValues.Clear();
    categoryValues.Add("");
    var lib = colorLibraryProp.objectReferenceValue as SpriteLibraryAsset;
    if (lib != null) {
      foreach (var c in lib.GetCategoryNames()) {
        if (!string.IsNullOrEmpty(c) && !categoryValues.Contains(c)) categoryValues.Add(c);
      }
    }

    if (!string.IsNullOrEmpty(t.category) && !categoryValues.Contains(t.category)) {
      categoryValues.Add(t.category);
    }

    if (categoryValues.Count == 0) {
      categoryValues.Add("Default");
    }

    var displayList = new List<string>(categoryValues.Count);
    foreach (var v in categoryValues) {
      displayList.Add(v == "" ? "\"\"" : v);
    }

    categoryDisplay = displayList.ToArray();
    categoriesDirty = false;
  }

  void DrawCategoryDropdown(SpriteWithNormals t, ref bool needsRefresh) {
    var current = categoryProp.stringValue;
    if (string.IsNullOrEmpty(current)) current = categoryValues.Count > 0 ? categoryValues[0] : "Default";

    var index = categoryValues.IndexOf(current);
    if (index < 0) index = 0;

    EditorGUI.BeginChangeCheck();
    var newIndex = EditorGUILayout.Popup("Category", index, categoryDisplay);
    if (EditorGUI.EndChangeCheck()) {
      if (newIndex >= 0 && newIndex < categoryValues.Count) {
        var newValue = categoryValues[newIndex];
        if (newValue != categoryProp.stringValue) {
          categoryProp.stringValue = newValue;
          labelPrefixProp.stringValue = "";
          lastCategory = newValue;
          labelsDirty = true;
          needsRefresh = true;
        }
      }
    }
  }

  void EnsureLabelsBuilt(SpriteWithNormals t) {
    var cat = categoryProp.stringValue;
    if (cat != lastCategory) {
      lastCategory = cat;
      labelsDirty = true;
    }

    if (!labelsDirty && labelDisplay != null && labelDisplay.Length > 0) return;

    labelValues.Clear();
    labelValues.Add("");

    var lib = colorLibraryProp.objectReferenceValue as SpriteLibraryAsset;
    if (lib != null && !string.IsNullOrEmpty(cat)) {
      foreach (var l in lib.GetCategoryLabelNames(cat)) {
        if (!string.IsNullOrEmpty(l)) labelValues.Add(l);
      }
    }

    if (!string.IsNullOrEmpty(t.labelPrefix) && !labelValues.Contains(t.labelPrefix)) {
      labelValues.Add(t.labelPrefix);
    }

    var displayList = new List<string>(labelValues.Count);
    foreach (var v in labelValues) {
      displayList.Add(v == "" ? "\"\"" : v);
    }

    labelDisplay = displayList.ToArray();
    labelsDirty = false;
  }

  void DrawLabelDropdown(SpriteWithNormals t, ref bool needsRefresh) {
    var current = labelPrefixProp.stringValue;
    if (string.IsNullOrEmpty(current)) current = "";

    var index = labelValues.IndexOf(current);
    if (index < 0) index = 0;

    EditorGUI.BeginChangeCheck();
    var newIndex = EditorGUILayout.Popup("Label Prefix", index, labelDisplay);
    if (EditorGUI.EndChangeCheck()) {
        if (newIndex >= 0 && newIndex < labelValues.Count) {
          var newValue = labelValues[newIndex];
          labelPrefixProp.stringValue = newValue;
          needsRefresh = true;
        }
      }
    }
  }
#endif
