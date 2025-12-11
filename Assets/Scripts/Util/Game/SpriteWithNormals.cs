using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.U2D.Animation;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class SpriteWithNormals : MonoBehaviour
{
  public SpriteLibraryAsset colorLibrary;
  public SpriteLibraryAsset normalLibrary;
  public string category = "Breathe";
  public string labelPrefix = "";

  SpriteRenderer _renderer;
  MaterialPropertyBlock _mpb;
  StringBuilder label = new();

  void Awake()
  {
    _renderer = GetComponent<SpriteRenderer>();
    _mpb = new MaterialPropertyBlock();
    UpdateSpriteAndNormal(0);
  }

  public void SetAnimation(string name)
  {
    category = name;
  }

  [ForceUpdate]
  public void ForceUpdateSpriteAndNormal()
  {
    UpdateSpriteAndNormal(0);
  }

  string GetLabel(int frame)
  {
    label.Clear();
    label.Append(labelPrefix);
    if (frame != 0 && labelPrefix != "")
    {
      label.Append("_").Append(frame);
    }
    else if (frame != 0 && labelPrefix == "")
    {
      label.Append(frame);
    }
    var result = label.ToString();
    return result;
  }

  public void UpdateSpriteAndNormal(int frame)
  {
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    var currentLabel = GetLabel(frame);
    if (colorLibrary == null || normalLibrary == null)
    {
      Debug.LogError("Sprite libraries are not assigned! " + gameObject.name + " " + gameObject.transform.parent?.name);
      return;
    }
    var colorSprite = colorLibrary.GetSprite(category, currentLabel);
    var normalSprite = normalLibrary.GetSprite(category, currentLabel);
    if (colorSprite == null)
    {
      //Debug.LogWarning("[SpriteWithNormals] Color sprite is null for category=" + category + " label=" + currentLabel + " on " + gameObject.name);
      return;
    }
    if (normalSprite == null)
    {
      Debug.LogError("Normal sprite not found for category '" + category + "' with label '" + currentLabel + "' " + gameObject.name);
    }
    _renderer.sprite = colorSprite;
    _mpb ??= new MaterialPropertyBlock();
    _renderer.GetPropertyBlock(_mpb);
    if (normalSprite != null && normalSprite.texture != null)
    {
      _mpb.SetTexture("_NormalMap", normalSprite.texture);
    }
    else
    {
      Debug.LogError("Normal sprite or its texture is missing. " + gameObject.name);
    }
    _renderer.SetPropertyBlock(_mpb);
  }

  public void FlipSprite(bool flip)
  {
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

    EnsureCategoriesBuilt(t);
    DrawCategoryDropdown(t);

    EnsureLabelsBuilt(t);
    DrawLabelDropdown(t);

    serializedObject.ApplyModifiedProperties();
  }

  void EnsureCategoriesBuilt(SpriteWithNormals t) {
    if (!categoriesDirty && categoryDisplay != null && categoryDisplay.Length > 0) return;

    categoryValues.Clear();
    var lib = colorLibraryProp.objectReferenceValue as SpriteLibraryAsset;
    if (lib != null) {
      foreach (var c in lib.GetCategoryNames()) {
        if (!string.IsNullOrEmpty(c)) categoryValues.Add(c);
      }
    }

    if (categoryValues.Count == 0) {
      if (!string.IsNullOrEmpty(t.category)) {
        categoryValues.Add(t.category);
      } else {
        categoryValues.Add("Default");
      }
    }

    categoryDisplay = categoryValues.ToArray();
    categoriesDirty = false;
  }

  void DrawCategoryDropdown(SpriteWithNormals t) {
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

    var displayList = new List<string>();
    foreach (var v in labelValues) {
      if (v == "") displayList.Add("EmptyString");
      else displayList.Add(v);
    }

    labelDisplay = displayList.ToArray();
    labelsDirty = false;
  }

  void DrawLabelDropdown(SpriteWithNormals t) {
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
      }
    }
  }
}
#endif
