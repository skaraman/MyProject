using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

#if UNITY_EDITOR
[CanEditMultipleObjects]
[CustomEditor(typeof(SpriteWithNormals))]
public class SpriteWithNormalsEditor : Editor {
  readonly struct DropdownOption {
    public readonly string label;
    public readonly string value;
    public DropdownOption(string label, string value) { this.label = label ?? ""; this.value = value ?? ""; }
  }

  sealed class ShardSelectionOptions {
    public readonly List<string> categories;
    public readonly Dictionary<string, List<string>> labelPrefixesByCategory;
    public ShardSelectionOptions(List<string> categories, Dictionary<string, List<string>> labelPrefixesByCategory) {
      this.categories = categories ?? new List<string>();
      this.labelPrefixesByCategory = labelPrefixesByCategory ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    }
  }

  static class SpriteIndexInspectorData {
    static readonly Dictionary<string, string> shardPathByLibrary = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, ShardSelectionOptions> shardOptionsByPath = new(StringComparer.OrdinalIgnoreCase);
    static string manifestTextCache;
    static string loadIssue;

    public static void Invalidate() {
      manifestTextCache = null;
      loadIssue = "";
      shardPathByLibrary.Clear();
      shardOptionsByPath.Clear();
    }

    public static string GetLoadIssue() { EnsureManifestParsed(); return loadIssue; }

    public static List<string> GetLibraries() {
      EnsureManifestParsed();
      var libraries = new List<string>(shardPathByLibrary.Keys);
      libraries.Sort(StringComparer.OrdinalIgnoreCase);
      return libraries;
    }

    public static List<string> GetCategories(string libraryName) {
      if (!TryGetShardOptions(libraryName, out var options)) return new List<string>();
      return new List<string>(options.categories);
    }

    public static List<string> GetLabelPrefixes(string libraryName, string category) {
      if (!TryGetShardOptions(libraryName, out var options)) return new List<string>();
      var normalizedCategory = NormalizeToken(category);
      if (string.IsNullOrWhiteSpace(normalizedCategory)) return new List<string>();
      return options.labelPrefixesByCategory.TryGetValue(normalizedCategory, out var prefixes) && prefixes != null
        ? new List<string>(prefixes)
        : new List<string>();
    }

    static bool TryGetShardOptions(string libraryName, out ShardSelectionOptions options) {
      options = null;
      EnsureManifestParsed();
      var normalizedLibrary = NormalizeToken(libraryName);
      if (!TryResolveLibraryShardPath(normalizedLibrary, out var shardPath)) return false;
      var normalizedShardPath = NormalizeToken(shardPath);
      if (string.IsNullOrWhiteSpace(normalizedShardPath)) return false;
      if (shardOptionsByPath.TryGetValue(normalizedShardPath, out options)) return options != null;
      options = ParseShardOptions(normalizedShardPath);
      shardOptionsByPath[normalizedShardPath] = options;
      return options != null;
    }

    static bool TryResolveLibraryShardPath(string requestedLibrary, out string shardPath) {
      shardPath = "";
      if (string.IsNullOrWhiteSpace(requestedLibrary)) return false;

      if (shardPathByLibrary.TryGetValue(requestedLibrary, out shardPath) && !string.IsNullOrWhiteSpace(shardPath)) {
        return true;
      }

      var slash = requestedLibrary.LastIndexOf('/');
      var leafName = slash >= 0 && slash < requestedLibrary.Length - 1
        ? NormalizeToken(requestedLibrary.Substring(slash + 1))
        : NormalizeToken(requestedLibrary);
      if (string.IsNullOrWhiteSpace(leafName)) return false;

      var suffix = "/" + leafName;
      string matchedLibrary = "";
      foreach (var pair in shardPathByLibrary) {
        if (!string.Equals(pair.Key, leafName, StringComparison.OrdinalIgnoreCase) &&
            !pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
          continue;
        }

        if (!string.IsNullOrWhiteSpace(matchedLibrary) &&
            !string.Equals(matchedLibrary, pair.Key, StringComparison.OrdinalIgnoreCase)) {
          return false;
        }

        matchedLibrary = pair.Key;
        shardPath = pair.Value;
      }

      return !string.IsNullOrWhiteSpace(shardPath);
    }

    static void EnsureManifestParsed() {
      var manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(SpriteStreamingConfig.ManifestAssetPath);
      var manifestText = manifestAsset?.text ?? "";
      if (string.Equals(manifestText, manifestTextCache, StringComparison.Ordinal)) return;

      manifestTextCache = manifestText;
      loadIssue = "";
      shardPathByLibrary.Clear();
      shardOptionsByPath.Clear();

      if (string.IsNullOrWhiteSpace(manifestText)) {
        loadIssue = $"Sprite index manifest is missing or empty at '{SpriteStreamingConfig.ManifestAssetPath}'. Run Tools > Sprite Streaming > 4) Rebuild Runtime Index.";
        return;
      }

      foreach (var rawLine in manifestText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
        var line = rawLine.TrimStart('\uFEFF');
        if (line.StartsWith("#", StringComparison.Ordinal)) continue;
        var cols = line.Split('\t');
        if (cols.Length < 3) continue;
        var lib = NormalizeToken(Unescape(cols[0]));
        var shard = NormalizeToken(Unescape(cols[2]));
        if (!string.IsNullOrWhiteSpace(lib) && !string.IsNullOrWhiteSpace(shard)) shardPathByLibrary[lib] = shard;
      }

      if (shardPathByLibrary.Count == 0)
        loadIssue = $"Sprite index manifest was found but contains no library rows at '{SpriteStreamingConfig.ManifestAssetPath}'. Rebuild the runtime index.";
    }

    static ShardSelectionOptions ParseShardOptions(string shardPath) {
      var shardAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(shardPath);
      if (shardAsset == null || string.IsNullOrWhiteSpace(shardAsset.text)) return null;

      var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var labelPrefixSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

      foreach (var rawLine in shardAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
        var line = rawLine.TrimStart('\uFEFF');
        if (line.StartsWith("#", StringComparison.Ordinal)) continue;
        var cols = line.Split('\t');
        if (cols.Length < 2) continue;
        var labelPrefix = NormalizeToken(Unescape(cols[0]));
        var cat = NormalizeToken(Unescape(cols[1]));
        if (string.IsNullOrWhiteSpace(cat)) continue;
        categories.Add(cat);
        if (!labelPrefixSets.TryGetValue(cat, out var set)) labelPrefixSets[cat] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(labelPrefix);
      }

      var orderedCategories = new List<string>(categories);
      orderedCategories.Sort(StringComparer.OrdinalIgnoreCase);
      var orderedPrefixes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
      foreach (var kvp in labelPrefixSets) {
        var ordered = new List<string>(kvp.Value);
        ordered.Sort(StringComparer.Ordinal);
        orderedPrefixes[kvp.Key] = ordered;
      }

      return new ShardSelectionOptions(orderedCategories, orderedPrefixes);
    }

    static string NormalizeToken(string value) {
      if (string.IsNullOrWhiteSpace(value)) return "";
      var trimmed = value.Trim();
      if (trimmed.Length >= 2) {
        var first = trimmed[0]; var last = trimmed[trimmed.Length - 1];
        if (first == '"' && last == '"') trimmed = trimmed.Substring(1, trimmed.Length - 2);
        else if (first == '\'' && last == '\'') trimmed = trimmed.Substring(1, trimmed.Length - 2).Replace("''", "'");
      }
      return string.IsNullOrWhiteSpace(trimmed) ? "" : trimmed.Trim();
    }

    static string Unescape(string value) =>
      string.IsNullOrEmpty(value) ? "" : value.Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\\", "\\");
  }

  SerializedProperty libraryNameProperty;
  SerializedProperty labelPrefixProperty;
  SerializedProperty categoryProperty;
  SerializedProperty isAnimationProperty;
  SerializedProperty doNotRenderProperty;
  SerializedProperty useTrimmedAtlasOffsetProperty;
  SerializedProperty shaderMarginPixelsXProperty;
  SerializedProperty shaderMarginPixelsYProperty;

  void OnEnable() {
    libraryNameProperty = serializedObject.FindProperty(nameof(SpriteWithNormals.libraryName));
    labelPrefixProperty = serializedObject.FindProperty(nameof(SpriteWithNormals.labelPrefix));
    categoryProperty = serializedObject.FindProperty(nameof(SpriteWithNormals.category));
    isAnimationProperty = serializedObject.FindProperty("isAnimation");
    doNotRenderProperty = serializedObject.FindProperty("doNotRender");
    useTrimmedAtlasOffsetProperty = serializedObject.FindProperty("useTrimmedAtlasOffset");
    shaderMarginPixelsXProperty = serializedObject.FindProperty("shaderMarginPixelsX");
    shaderMarginPixelsYProperty = serializedObject.FindProperty("shaderMarginPixelsY");
  }

  public override void OnInspectorGUI() {
    serializedObject.Update();

    var changed = DrawDropdown(libraryNameProperty, "Library Name", BuildLibraryOptions());
    changed |= DrawDropdown(categoryProperty, "Category", BuildCategoryOptions(libraryNameProperty.stringValue));
    changed |= DrawDropdown(labelPrefixProperty, "Label Prefix", BuildLabelPrefixOptions(libraryNameProperty.stringValue, categoryProperty.stringValue));
    EditorGUILayout.PropertyField(isAnimationProperty, new GUIContent("Is Animation", "When enabled, frame input drives this label prefix as an animation loop."));
    EditorGUILayout.PropertyField(doNotRenderProperty, new GUIContent("Do Not Render", "When enabled, this component keeps the SpriteRenderer disabled."));
    EditorGUI.BeginChangeCheck();
    EditorGUILayout.PropertyField(useTrimmedAtlasOffsetProperty, new GUIContent("Use Trimmed Atlas Offset", "When enabled, this component reads sibling atlas offset metadata and repositions the sprite to match its original slot."));
    var trimmedOffsetToggleChanged = EditorGUI.EndChangeCheck();
    EditorGUI.BeginChangeCheck();
    EditorGUILayout.PropertyField(shaderMarginPixelsXProperty, new GUIContent("Shader Margin X", "Transparent padding, in source pixels, added on the left and right before rendering."));
    EditorGUILayout.PropertyField(shaderMarginPixelsYProperty, new GUIContent("Shader Margin Y", "Transparent padding, in source pixels, added on the top and bottom before rendering."));
    var shaderMarginChanged = EditorGUI.EndChangeCheck();

    var loadIssue = SpriteIndexInspectorData.GetLoadIssue();
    if (!string.IsNullOrWhiteSpace(loadIssue)) EditorGUILayout.HelpBox(loadIssue, MessageType.Warning);

    var serializedChanged = serializedObject.ApplyModifiedProperties();
    if (serializedChanged || changed) MarkTargetsDirty(targets);
    if (trimmedOffsetToggleChanged || shaderMarginChanged) {
      foreach (var obj in targets) {
        var t = obj as SpriteWithNormals;
        if (t == null) continue;
        if (shaderMarginChanged) {
          t.RefreshShaderMarginPadding();
        } else {
          var refreshFrame = t.IsAnimation ? Mathf.Max(t.LastRequestedFrame, 1) : 0;
          t.ForceUpdateSpriteAndNormal(refreshFrame);
        }
      }
    }

    EditorGUILayout.BeginHorizontal();
    if (!Application.isPlaying && GUILayout.Button("Refresh Sprite + Normal")) {
      foreach (var obj in targets) {
        var t = obj as SpriteWithNormals;
        if (t == null) continue;
        t.ForceUpdateSpriteAndNormal(t.IsAnimation ? 1 : 0);
        EditorUtility.SetDirty(t);
        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
      }
    }
    if (GUILayout.Button("Reload Index", GUILayout.Width(104f))) SpriteIndexInspectorData.Invalidate();
    EditorGUILayout.EndHorizontal();
  }

  static List<DropdownOption> BuildLibraryOptions() {
    var options = new List<DropdownOption> { new("(Empty)", "") };
    foreach (var lib in SpriteIndexInspectorData.GetLibraries())
      if (!string.IsNullOrWhiteSpace(lib)) options.Add(new(lib, lib));
    return options;
  }

  static List<DropdownOption> BuildCategoryOptions(string libraryName) {
    var options = new List<DropdownOption> { new("(Empty)", "") };
    foreach (var cat in SpriteIndexInspectorData.GetCategories(libraryName))
      if (!string.IsNullOrWhiteSpace(cat)) options.Add(new(cat, cat));
    return options;
  }

  static List<DropdownOption> BuildLabelPrefixOptions(string libraryName, string category) {
    var options = new List<DropdownOption> { new("(No Prefix)", "") };
    foreach (var prefix in SpriteIndexInspectorData.GetLabelPrefixes(libraryName, category))
      if (!string.IsNullOrWhiteSpace(prefix)) options.Add(new(prefix, prefix));
    return options;
  }

  static bool DrawDropdown(SerializedProperty property, string label, List<DropdownOption> options) {
    if (property == null) return false;
    if (options == null || options.Count == 0) { EditorGUILayout.PropertyField(property, new GUIContent(label)); return false; }

    var propertyValue = property.stringValue ?? "";
    var localOptions = options;
    var currentIndex = IndexOfValue(localOptions, propertyValue);
    if (currentIndex < 0) {
      localOptions = new List<DropdownOption>(options.Count + 1) {
        new(string.IsNullOrWhiteSpace(propertyValue) ? "(Empty)" : propertyValue + " (current)", propertyValue)
      };
      localOptions.AddRange(options);
      currentIndex = 0;
    }

    var display = new string[localOptions.Count];
    for (var i = 0; i < localOptions.Count; i++) display[i] = localOptions[i].label;

    var previousMixedState = EditorGUI.showMixedValue;
    EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
    EditorGUI.BeginChangeCheck();
    var selectedIndex = EditorGUILayout.Popup(new GUIContent(label), currentIndex, display);
    var popupChanged = EditorGUI.EndChangeCheck();
    EditorGUI.showMixedValue = previousMixedState;

    if (selectedIndex < 0 || selectedIndex >= localOptions.Count) selectedIndex = currentIndex;
    var selectedValue = localOptions[selectedIndex].value ?? "";
    if (!popupChanged && string.Equals(propertyValue, selectedValue, StringComparison.Ordinal)) return false;
    property.stringValue = selectedValue;
    return true;
  }

  static int IndexOfValue(List<DropdownOption> options, string value) {
    var target = value ?? "";
    for (var i = 0; i < options.Count; i++)
      if (string.Equals(options[i].value, target, StringComparison.Ordinal)) return i;
    return -1;
  }

  static void MarkTargetsDirty(UnityEngine.Object[] objectTargets) {
    foreach (var obj in objectTargets) {
      var t = obj as SpriteWithNormals;
      if (t == null) continue;
      EditorUtility.SetDirty(t);
      PrefabUtility.RecordPrefabInstancePropertyModifications(t);
      var scene = t.gameObject.scene;
      if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
    }
  }
}
#endif
