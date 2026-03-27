#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

readonly struct AllIn1ShaderPropertyOption {
  public readonly string value;
  public readonly string label;
  public readonly string conditionExpression;
  public readonly bool hasSourceMatch;

  public AllIn1ShaderPropertyOption(string value, string label, string conditionExpression, bool hasSourceMatch) {
    this.value = value ?? "";
    this.label = label ?? "";
    this.conditionExpression = conditionExpression ?? "";
    this.hasSourceMatch = hasSourceMatch;
  }
}

enum AllIn1ShaderPropertyKind {
  Float,
  Color,
  Vector,
  Texture
}

sealed class AllIn1ShaderPropertyCache {
  public readonly List<AllIn1ShaderPropertyOption> floatOptions = new();
  public readonly List<AllIn1ShaderPropertyOption> colorOptions = new();
  public readonly List<AllIn1ShaderPropertyOption> vectorOptions = new();
  public readonly List<AllIn1ShaderPropertyOption> textureOptions = new();
}

sealed class AllIn1ShaderSourceMetadata {
  public readonly HashSet<string> unconditionalProperties = new(StringComparer.Ordinal);
  public readonly Dictionary<string, HashSet<string>> conditionalExpressionsByProperty = new(StringComparer.Ordinal);
}

sealed class AllIn1ConditionalFrame {
  public string currentCondition = "";
  public string priorRelevantConditions = "";
}

static class AllIn1ShaderPropertyOptions {
  const string ShaderRootPath = "Assets/Plugins/AllIn1SpriteShader/Shaders";
  static readonly string[] canonicalFallbackShaderPaths = {
    "Assets/Plugins/AllIn1SpriteShader/Shaders/AllIn1SpriteShader.shader",
    "Assets/Plugins/AllIn1SpriteShader/Shaders/AllIn1Urp2dRenderer.shader"
  };

  static readonly Dictionary<string, AllIn1ShaderPropertyCache> cacheByShaderId = new(StringComparer.Ordinal);
  static readonly Dictionary<string, AllIn1ShaderSourceMetadata> sourceMetadataByShaderPath = new(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> collectedShaderIds = new(StringComparer.Ordinal);
  static readonly List<Shader> collectedShaders = new();
  static readonly HashSet<string> collectedPropertyNames = new(StringComparer.Ordinal);
  static readonly HashSet<string> knownKeywordSet = new(AllIn1ShaderKeywords.Keywords, StringComparer.Ordinal);
  static readonly Regex includeRegex = new(@"^\s*#include(?:_with_pragmas)?\s+""([^""]+)""", RegexOptions.Compiled);
  static readonly Regex shaderFeatureKeywordRegex = new(@"\b[A-Z][A-Z0-9_]*_ON\b", RegexOptions.Compiled);
  static readonly Regex shaderPropertyRegex = new(@"(?<![A-Za-z0-9])_[A-Za-z0-9_]+", RegexOptions.Compiled);
  static string[] fallbackShaderGuids;

  static AllIn1ShaderPropertyOptions() {
    EditorApplication.projectChanged += ClearCaches;
  }

  public static List<AllIn1ShaderPropertyOption> GetOptions(SerializedObject serializedObject, AllIn1ShaderPropertyKind kind) {
    collectedShaderIds.Clear();
    collectedShaders.Clear();
    CollectRendererShaders(serializedObject != null ? serializedObject.targetObjects : null);
    if (collectedShaders.Count == 0) CollectFallbackShaders();

    var configuredKeywords = GetConfiguredKeywords(serializedObject);
    var options = new List<AllIn1ShaderPropertyOption>();
    collectedPropertyNames.Clear();
    for (var i = 0; i < collectedShaders.Count; i++) {
      var shader = collectedShaders[i];
      if (shader == null) continue;
      var shaderOptions = GetCachedOptions(shader, kind);
      for (var j = 0; j < shaderOptions.Count; j++) {
        var option = shaderOptions[j];
        if (!ShouldIncludeOption(option, configuredKeywords)) continue;
        if (!collectedPropertyNames.Add(option.value)) continue;
        options.Add(option);
      }
    }
    return options;
  }

  static void ClearCaches() {
    cacheByShaderId.Clear();
    sourceMetadataByShaderPath.Clear();
    collectedShaderIds.Clear();
    collectedShaders.Clear();
    collectedPropertyNames.Clear();
    knownKeywordSet.Clear();
    for (var i = 0; i < AllIn1ShaderKeywords.Keywords.Length; i++) {
      var keyword = AllIn1ShaderKeywords.Keywords[i];
      if (string.IsNullOrWhiteSpace(keyword)) continue;
      knownKeywordSet.Add(keyword);
    }
    fallbackShaderGuids = null;
  }

  static void CollectRendererShaders(UnityEngine.Object[] targets) {
    if (targets == null) return;
    for (var i = 0; i < targets.Length; i++) {
      var animator = targets[i] as AllIn1AnimatorInspector;
      if (animator == null) continue;
      var renderer = animator.GetComponent<Renderer>();
      if (renderer == null) continue;
      var materials = renderer.sharedMaterials;
      if (materials == null) continue;
      for (var j = 0; j < materials.Length; j++) AddShader(materials[j] != null ? materials[j].shader : null);
    }
  }

  static void CollectFallbackShaders() {
    for (var i = 0; i < canonicalFallbackShaderPaths.Length; i++) {
      var shaderPath = canonicalFallbackShaderPaths[i];
      if (string.IsNullOrWhiteSpace(shaderPath)) continue;
      AddShader(AssetDatabase.LoadAssetAtPath<Shader>(shaderPath));
    }

    if (collectedShaders.Count > 0) return;

    fallbackShaderGuids ??= AssetDatabase.FindAssets("t:Shader", new[] { ShaderRootPath });
    for (var i = 0; i < fallbackShaderGuids.Length; i++) {
      var shaderPath = AssetDatabase.GUIDToAssetPath(fallbackShaderGuids[i]);
      if (string.IsNullOrWhiteSpace(shaderPath)) continue;
      AddShader(AssetDatabase.LoadAssetAtPath<Shader>(shaderPath));
    }
  }

  static void AddShader(Shader shader) {
    if (shader == null) return;
    var shaderId = GetShaderCacheKey(shader);
    if (!collectedShaderIds.Add(shaderId)) return;
    collectedShaders.Add(shader);
  }

  static List<AllIn1ShaderPropertyOption> GetCachedOptions(Shader shader, AllIn1ShaderPropertyKind kind) {
    var shaderId = GetShaderCacheKey(shader);
    if (!cacheByShaderId.TryGetValue(shaderId, out var cache)) {
      cache = BuildCache(shader);
      cacheByShaderId[shaderId] = cache;
    }

    return kind switch {
      AllIn1ShaderPropertyKind.Float => cache.floatOptions,
      AllIn1ShaderPropertyKind.Color => cache.colorOptions,
      AllIn1ShaderPropertyKind.Vector => cache.vectorOptions,
      AllIn1ShaderPropertyKind.Texture => cache.textureOptions,
      _ => cache.floatOptions
    };
  }

  static string GetShaderCacheKey(Shader shader) {
    if (shader == null) return "";
    var shaderPath = AssetDatabase.GetAssetPath(shader);
    if (!string.IsNullOrWhiteSpace(shaderPath)) return shaderPath;
    return shader.name ?? "";
  }

  static AllIn1ShaderPropertyCache BuildCache(Shader shader) {
    var cache = new AllIn1ShaderPropertyCache();
    if (shader == null) return cache;

    var shaderPath = AssetDatabase.GetAssetPath(shader);
    var sourceMetadata = GetSourceMetadata(shaderPath);
    var propertyCount = shader.GetPropertyCount();
    for (var i = 0; i < propertyCount; i++) {
      var propertyName = shader.GetPropertyName(i);
      if (string.IsNullOrWhiteSpace(propertyName)) continue;
      var option = new AllIn1ShaderPropertyOption(
        propertyName,
        BuildLabel(shader, i, propertyName),
        BuildConditionExpression(sourceMetadata, propertyName),
        HasSourceMatch(sourceMetadata, propertyName)
      );
      switch (shader.GetPropertyType(i)) {
        case ShaderPropertyType.Color:
          cache.colorOptions.Add(option);
          break;
        case ShaderPropertyType.Vector:
          cache.vectorOptions.Add(option);
          break;
        case ShaderPropertyType.Texture:
          cache.textureOptions.Add(option);
          break;
        case ShaderPropertyType.Float:
        case ShaderPropertyType.Range:
          cache.floatOptions.Add(option);
          break;
#if UNITY_2021_2_OR_NEWER
        case ShaderPropertyType.Int:
          cache.floatOptions.Add(option);
          break;
#endif
      }
    }

    return cache;
  }

  static AllIn1ShaderSourceMetadata GetSourceMetadata(string shaderPath) {
    var normalizedPath = NormalizeAssetPath(shaderPath);
    if (string.IsNullOrWhiteSpace(normalizedPath)) return new AllIn1ShaderSourceMetadata();
    if (sourceMetadataByShaderPath.TryGetValue(normalizedPath, out var cached)) return cached;

    var metadata = new AllIn1ShaderSourceMetadata();
    ParseShaderSourceFile(normalizedPath, metadata, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    sourceMetadataByShaderPath[normalizedPath] = metadata;
    return metadata;
  }

  static void ParseShaderSourceFile(string assetPath, AllIn1ShaderSourceMetadata metadata, HashSet<string> activePaths) {
    var normalizedPath = NormalizeAssetPath(assetPath);
    if (string.IsNullOrWhiteSpace(normalizedPath)) return;
    if (!activePaths.Add(normalizedPath)) return;

    try {
      var fullPath = Path.GetFullPath(normalizedPath);
      if (!File.Exists(fullPath)) return;

      var lines = File.ReadAllLines(fullPath);
      var frames = new Stack<AllIn1ConditionalFrame>();
      var braceDepth = 0;
      var pendingPropertiesBlock = false;
      var insidePropertiesBlock = false;
      var propertiesBraceDepth = 0;
      var currentDirectory = Path.GetDirectoryName(fullPath) ?? "";

      for (var i = 0; i < lines.Length; i++) {
        var rawLine = StripLineComment(lines[i]);
        var trimmed = rawLine.Trim();
        if (trimmed.Length == 0) continue;

        if (TryGetIncludePath(trimmed, currentDirectory, out var includeAssetPath)) {
          ParseShaderSourceFile(includeAssetPath, metadata, activePaths);
        }

        RegisterShaderFeatureKeywords(trimmed);
        if (TryHandleConditionalDirective(trimmed, frames)) continue;

        if (!insidePropertiesBlock && IsPropertiesDeclaration(trimmed)) {
          pendingPropertiesBlock = true;
        }

        if (pendingPropertiesBlock || insidePropertiesBlock) {
          UpdatePropertiesBlockState(rawLine, ref pendingPropertiesBlock, ref insidePropertiesBlock, ref propertiesBraceDepth);
          if (insidePropertiesBlock || pendingPropertiesBlock) continue;
        }

        var currentCondition = GetCurrentCondition(frames);
        // URP keeps many keyword-gated declarations in top-level include files rather than
        // inside function bodies, so record those lines when a relevant keyword condition is active.
        if (braceDepth > 0 || !string.IsNullOrWhiteSpace(currentCondition)) {
          RecordLineUsage(trimmed, currentCondition, metadata);
        }

        braceDepth += CountChar(rawLine, '{');
        braceDepth -= CountChar(rawLine, '}');
        if (braceDepth < 0) braceDepth = 0;
      }
    }
    finally {
      activePaths.Remove(normalizedPath);
    }
  }

  static string BuildLabel(Shader shader, int propertyIndex, string propertyName) {
    var displayName = propertyName;
    try {
      var description = shader.GetPropertyDescription(propertyIndex);
      if (!string.IsNullOrWhiteSpace(description) && !string.Equals(description, propertyName, StringComparison.Ordinal)) {
        displayName = $"{description} ({propertyName})";
      }
    }
    catch {
      return propertyName;
    }

    return displayName;
  }

  static string BuildConditionExpression(AllIn1ShaderSourceMetadata metadata, string propertyName) {
    if (metadata == null || string.IsNullOrWhiteSpace(propertyName)) return "";
    if (metadata.unconditionalProperties.Contains(propertyName)) return "";
    if (!metadata.conditionalExpressionsByProperty.TryGetValue(propertyName, out var expressions) || expressions == null || expressions.Count == 0) return "";

    var builder = new StringBuilder();
    var first = true;
    foreach (var expression in expressions) {
      if (string.IsNullOrWhiteSpace(expression)) return "";
      if (!first) builder.Append(" || ");
      builder.Append('(').Append(expression).Append(')');
      first = false;
    }
    return builder.ToString();
  }

  static bool HasSourceMatch(AllIn1ShaderSourceMetadata metadata, string propertyName) {
    if (metadata == null || string.IsNullOrWhiteSpace(propertyName)) return false;
    if (metadata.unconditionalProperties.Contains(propertyName)) return true;
    return metadata.conditionalExpressionsByProperty.ContainsKey(propertyName);
  }

  static bool ShouldIncludeOption(AllIn1ShaderPropertyOption option, HashSet<string> configuredKeywords) {
    if (!option.hasSourceMatch) return false;
    if (string.IsNullOrWhiteSpace(option.conditionExpression)) return true;
    return EvaluateCondition(option.conditionExpression, configuredKeywords);
  }

  static HashSet<string> GetConfiguredKeywords(SerializedObject serializedObject) {
    var configuredKeywords = new HashSet<string>(StringComparer.Ordinal);
    if (serializedObject == null) return configuredKeywords;

    var keywordTogglesProperty = serializedObject.FindProperty("keywordToggles");
    if (keywordTogglesProperty == null || !keywordTogglesProperty.isArray) return configuredKeywords;

    for (var i = 0; i < keywordTogglesProperty.arraySize; i++) {
      var item = keywordTogglesProperty.GetArrayElementAtIndex(i);
      if (item == null) continue;
      var keywordProperty = item.FindPropertyRelative("keyword");
      if (keywordProperty == null) continue;
      var keyword = keywordProperty.stringValue ?? "";
      if (string.IsNullOrWhiteSpace(keyword)) continue;
      configuredKeywords.Add(keyword);
    }

    return configuredKeywords;
  }

  static bool TryHandleConditionalDirective(string trimmedLine, Stack<AllIn1ConditionalFrame> frames) {
    if (!trimmedLine.StartsWith("#", StringComparison.Ordinal)) return false;

    if (trimmedLine.StartsWith("#if ", StringComparison.Ordinal)) {
      var expression = NormalizeRelevantConditionExpression(trimmedLine.Substring(4));
      frames.Push(new AllIn1ConditionalFrame {
        currentCondition = expression,
        priorRelevantConditions = expression
      });
      return true;
    }

    if (trimmedLine.StartsWith("#ifdef ", StringComparison.Ordinal)) {
      var expression = NormalizeRelevantConditionExpression(trimmedLine.Substring(7));
      frames.Push(new AllIn1ConditionalFrame {
        currentCondition = expression,
        priorRelevantConditions = expression
      });
      return true;
    }

    if (trimmedLine.StartsWith("#ifndef ", StringComparison.Ordinal)) {
      var expression = NormalizeRelevantConditionExpression("!(" + trimmedLine.Substring(8).Trim() + ")");
      frames.Push(new AllIn1ConditionalFrame {
        currentCondition = expression,
        priorRelevantConditions = expression
      });
      return true;
    }

    if (trimmedLine.StartsWith("#elif ", StringComparison.Ordinal)) {
      if (frames.Count == 0) return true;
      var frame = frames.Peek();
      var branchExpression = NormalizeRelevantConditionExpression(trimmedLine.Substring(6));
      frame.currentCondition = CombineBranchCondition(branchExpression, frame.priorRelevantConditions);
      frame.priorRelevantConditions = CombineOr(frame.priorRelevantConditions, branchExpression);
      return true;
    }

    if (string.Equals(trimmedLine, "#else", StringComparison.Ordinal)) {
      if (frames.Count == 0) return true;
      var frame = frames.Peek();
      frame.currentCondition = Negate(frame.priorRelevantConditions);
      return true;
    }

    if (string.Equals(trimmedLine, "#endif", StringComparison.Ordinal)) {
      if (frames.Count > 0) frames.Pop();
      return true;
    }

    return true;
  }

  static string CombineBranchCondition(string branchExpression, string priorRelevantConditions) {
    if (string.IsNullOrWhiteSpace(branchExpression)) return "";
    var negatedPrior = Negate(priorRelevantConditions);
    return CombineAnd(negatedPrior, branchExpression);
  }

  static string GetCurrentCondition(Stack<AllIn1ConditionalFrame> frames) {
    if (frames == null || frames.Count == 0) return "";

    string combined = "";
    foreach (var frame in frames) {
      if (frame == null || string.IsNullOrWhiteSpace(frame.currentCondition)) continue;
      combined = CombineAnd(combined, frame.currentCondition);
    }
    return combined;
  }

  static void RecordLineUsage(string trimmedLine, string currentCondition, AllIn1ShaderSourceMetadata metadata) {
    if (metadata == null || string.IsNullOrWhiteSpace(trimmedLine)) return;

    var matches = shaderPropertyRegex.Matches(trimmedLine);
    if (matches.Count == 0) return;

    for (var i = 0; i < matches.Count; i++) {
      var propertyName = matches[i].Value ?? "";
      if (string.IsNullOrWhiteSpace(propertyName)) continue;
      if (string.IsNullOrWhiteSpace(currentCondition)) {
        metadata.unconditionalProperties.Add(propertyName);
        metadata.conditionalExpressionsByProperty.Remove(propertyName);
        continue;
      }

      if (metadata.unconditionalProperties.Contains(propertyName)) continue;
      if (!metadata.conditionalExpressionsByProperty.TryGetValue(propertyName, out var expressions)) {
        expressions = new HashSet<string>(StringComparer.Ordinal);
        metadata.conditionalExpressionsByProperty[propertyName] = expressions;
      }
      expressions.Add(currentCondition);
    }
  }

  static bool IsPropertiesDeclaration(string trimmedLine) {
    if (string.IsNullOrWhiteSpace(trimmedLine)) return false;
    if (string.Equals(trimmedLine, "Properties", StringComparison.Ordinal)) return true;
    return trimmedLine.StartsWith("Properties", StringComparison.Ordinal) &&
           (trimmedLine.Length == "Properties".Length || char.IsWhiteSpace(trimmedLine["Properties".Length]) || trimmedLine["Properties".Length] == '{');
  }

  static void UpdatePropertiesBlockState(string line, ref bool pendingPropertiesBlock, ref bool insidePropertiesBlock, ref int propertiesBraceDepth) {
    var openCount = CountChar(line, '{');
    var closeCount = CountChar(line, '}');

    if (pendingPropertiesBlock) {
      if (openCount > 0) {
        pendingPropertiesBlock = false;
        insidePropertiesBlock = true;
        propertiesBraceDepth += openCount - closeCount;
        if (propertiesBraceDepth <= 0) {
          insidePropertiesBlock = false;
          propertiesBraceDepth = 0;
        }
      }
      return;
    }

    if (!insidePropertiesBlock) return;

    propertiesBraceDepth += openCount - closeCount;
    if (propertiesBraceDepth <= 0) {
      insidePropertiesBlock = false;
      propertiesBraceDepth = 0;
    }
  }

  static int CountChar(string value, char target) {
    if (string.IsNullOrEmpty(value)) return 0;
    var count = 0;
    for (var i = 0; i < value.Length; i++) {
      if (value[i] == target) count++;
    }
    return count;
  }

  static string StripLineComment(string line) {
    if (string.IsNullOrEmpty(line)) return "";
    var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
    if (commentIndex < 0) return line;
    return line.Substring(0, commentIndex);
  }

  static bool TryGetIncludePath(string trimmedLine, string currentDirectory, out string includeAssetPath) {
    includeAssetPath = "";
    var match = includeRegex.Match(trimmedLine);
    if (!match.Success) return false;

    var includeValue = match.Groups[1].Value ?? "";
    if (string.IsNullOrWhiteSpace(includeValue) || includeValue.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return false;

    var combined = Path.GetFullPath(Path.Combine(currentDirectory ?? "", includeValue.Replace('/', Path.DirectorySeparatorChar)));
    var projectRoot = Path.GetFullPath(".");
    if (!combined.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)) return false;

    includeAssetPath = NormalizeAssetPath(combined.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    return !string.IsNullOrWhiteSpace(includeAssetPath);
  }

  static void RegisterShaderFeatureKeywords(string trimmedLine) {
    if (string.IsNullOrWhiteSpace(trimmedLine)) return;
    if (!trimmedLine.StartsWith("#pragma", StringComparison.Ordinal)) return;
    if (trimmedLine.IndexOf("shader_feature", StringComparison.Ordinal) < 0) return;

    var matches = shaderFeatureKeywordRegex.Matches(trimmedLine);
    for (var i = 0; i < matches.Count; i++) {
      var keyword = matches[i].Value ?? "";
      if (string.IsNullOrWhiteSpace(keyword)) continue;
      knownKeywordSet.Add(keyword);
    }
  }

  static string NormalizeAssetPath(string path) {
    if (string.IsNullOrWhiteSpace(path)) return "";
    return path.Replace('\\', '/');
  }

  static string NormalizeRelevantConditionExpression(string expression) {
    if (string.IsNullOrWhiteSpace(expression)) return "";
    var normalized = Regex.Replace(expression, @"defined\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)", "$1");
    var matches = Regex.Matches(normalized, @"\b[A-Za-z_][A-Za-z0-9_]*\b");
    var hasKnownKeyword = false;
    for (var i = 0; i < matches.Count; i++) {
      if (!knownKeywordSet.Contains(matches[i].Value)) continue;
      hasKnownKeyword = true;
      break;
    }
    if (!hasKnownKeyword) return "";
    return normalized.Trim();
  }

  static string CombineAnd(string left, string right) {
    if (string.IsNullOrWhiteSpace(left)) return right ?? "";
    if (string.IsNullOrWhiteSpace(right)) return left ?? "";
    return "(" + left + ") && (" + right + ")";
  }

  static string CombineOr(string left, string right) {
    if (string.IsNullOrWhiteSpace(left)) return right ?? "";
    if (string.IsNullOrWhiteSpace(right)) return left ?? "";
    return "(" + left + ") || (" + right + ")";
  }

  static string Negate(string expression) {
    if (string.IsNullOrWhiteSpace(expression)) return "";
    return "!(" + expression + ")";
  }

  static bool EvaluateCondition(string expression, HashSet<string> configuredKeywords) {
    if (string.IsNullOrWhiteSpace(expression)) return true;
    var parser = new KeywordConditionParser(expression, configuredKeywords);
    return parser.Parse();
  }

  sealed class KeywordConditionParser {
    readonly string expression;
    readonly HashSet<string> configuredKeywords;
    int index;

    public KeywordConditionParser(string expression, HashSet<string> configuredKeywords) {
      this.expression = expression ?? "";
      this.configuredKeywords = configuredKeywords ?? new HashSet<string>(StringComparer.Ordinal);
    }

    public bool Parse() {
      index = 0;
      var result = ParseOr();
      SkipWhitespace();
      return index >= expression.Length && result;
    }

    bool ParseOr() {
      var value = ParseAnd();
      while (true) {
        SkipWhitespace();
        if (!Match("||")) return value;
        var right = ParseAnd();
        value = value || right;
      }
    }

    bool ParseAnd() {
      var value = ParseUnary();
      while (true) {
        SkipWhitespace();
        if (!Match("&&")) return value;
        var right = ParseUnary();
        value = value && right;
      }
    }

    bool ParseUnary() {
      SkipWhitespace();
      if (Match("!")) return !ParseUnary();
      return ParsePrimary();
    }

    bool ParsePrimary() {
      SkipWhitespace();
      if (Match("(")) {
        var value = ParseOr();
        Match(")");
        return value;
      }

      var identifier = ReadIdentifier();
      if (string.IsNullOrWhiteSpace(identifier)) return false;
      return configuredKeywords.Contains(identifier);
    }

    void SkipWhitespace() {
      while (index < expression.Length && char.IsWhiteSpace(expression[index])) index++;
    }

    bool Match(string value) {
      if (string.IsNullOrEmpty(value)) return false;
      if (index + value.Length > expression.Length) return false;
      if (!string.Equals(expression.Substring(index, value.Length), value, StringComparison.Ordinal)) return false;
      index += value.Length;
      return true;
    }

    string ReadIdentifier() {
      SkipWhitespace();
      if (index >= expression.Length) return "";
      if (!(char.IsLetter(expression[index]) || expression[index] == '_')) return "";

      var start = index;
      index++;
      while (index < expression.Length) {
        var c = expression[index];
        if (!(char.IsLetterOrDigit(c) || c == '_')) break;
        index++;
      }
      return expression.Substring(start, index - start);
    }
  }
}

abstract class AllIn1AnimatorPropDrawerBase : PropertyDrawer {
  protected abstract AllIn1ShaderPropertyKind PropertyKind { get; }

  public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
    EditorGUI.BeginProperty(position, label, property);

    var propProperty = property.FindPropertyRelative("prop");
    var propHashProperty = property.FindPropertyRelative("propHash");

    var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
    property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, BuildFoldoutLabel(label, propProperty), true);
    if (property.isExpanded) {
      EditorGUI.indentLevel++;
      var y = line.yMax + EditorGUIUtility.standardVerticalSpacing;
      y = DrawPropDropdown(property, propProperty, propHashProperty, position.x, position.width, y);
      DrawRemainingFields(property, position.x, position.width, ref y);
      EditorGUI.indentLevel--;
    }

    EditorGUI.EndProperty();
  }

  public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
    var height = EditorGUIUtility.singleLineHeight;
    if (!property.isExpanded) return height;

    var propProperty = property.FindPropertyRelative("prop");
    height += EditorGUIUtility.standardVerticalSpacing + GetPropFieldHeight(propProperty);

    var iterator = property.Copy();
    var endProperty = iterator.GetEndProperty();
    var enterChildren = true;
    while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProperty)) {
      enterChildren = false;
      if (ShouldSkipProperty(iterator)) continue;
      height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(iterator, true);
    }

    return height;
  }

  static GUIContent BuildFoldoutLabel(GUIContent label, SerializedProperty propProperty) {
    var prop = propProperty?.stringValue ?? "";
    if (string.IsNullOrWhiteSpace(prop)) return label;
    return new GUIContent($"{label.text} [{prop}]", label.tooltip);
  }

  float DrawPropDropdown(SerializedProperty rootProperty, SerializedProperty propProperty, SerializedProperty propHashProperty, float x, float width, float y) {
    var height = GetPropFieldHeight(propProperty);
    var rect = new Rect(x, y, width, height);
    var changed = DrawDropdown(rect, propProperty, new GUIContent(propProperty.displayName), AllIn1ShaderPropertyOptions.GetOptions(rootProperty.serializedObject, PropertyKind));
    if (changed || propHashProperty != null) SyncPropHash(propProperty, propHashProperty);
    return y + height + EditorGUIUtility.standardVerticalSpacing;
  }

  static float GetPropFieldHeight(SerializedProperty propProperty) {
    return propProperty == null
      ? EditorGUIUtility.singleLineHeight
      : EditorGUI.GetPropertyHeight(propProperty, true);
  }

  static void DrawRemainingFields(SerializedProperty property, float x, float width, ref float y) {
    var iterator = property.Copy();
    var endProperty = iterator.GetEndProperty();
    var enterChildren = true;
    while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProperty)) {
      enterChildren = false;
      if (ShouldSkipProperty(iterator)) continue;
      var height = EditorGUI.GetPropertyHeight(iterator, true);
      var rect = new Rect(x, y, width, height);
      EditorGUI.PropertyField(rect, iterator, true);
      y += height + EditorGUIUtility.standardVerticalSpacing;
    }
  }

  static bool ShouldSkipProperty(SerializedProperty property) {
    return property.name == "prop" || property.name == "propHash";
  }

  static bool DrawDropdown(Rect position, SerializedProperty property, GUIContent label, List<AllIn1ShaderPropertyOption> options) {
    if (property == null) return false;
    if (options == null || options.Count == 0) {
      EditorGUI.PropertyField(position, property, label, true);
      return false;
    }

    var currentValue = property.stringValue ?? "";
    var localOptions = options;
    var currentIndex = IndexOfValue(localOptions, currentValue);
    if (currentIndex < 0) {
      localOptions = new List<AllIn1ShaderPropertyOption>(options.Count + 1) {
        new(currentValue, string.IsNullOrWhiteSpace(currentValue) ? "(Empty)" : $"{currentValue} (current)", "", true)
      };
      localOptions.AddRange(options);
      currentIndex = 0;
    }

    var displayOptions = new string[localOptions.Count];
    for (var i = 0; i < localOptions.Count; i++) displayOptions[i] = localOptions[i].label;

    var previousMixedValue = EditorGUI.showMixedValue;
    EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
    EditorGUI.BeginChangeCheck();
    var selectedIndex = EditorGUI.Popup(position, label.text, currentIndex, displayOptions);
    var changed = EditorGUI.EndChangeCheck();
    EditorGUI.showMixedValue = previousMixedValue;

    if (selectedIndex < 0 || selectedIndex >= localOptions.Count) selectedIndex = currentIndex;
    if (!changed) return false;

    property.stringValue = localOptions[selectedIndex].value ?? "";
    return true;
  }

  static int IndexOfValue(List<AllIn1ShaderPropertyOption> options, string value) {
    var target = value ?? "";
    for (var i = 0; i < options.Count; i++) {
      if (string.Equals(options[i].value, target, StringComparison.Ordinal)) return i;
    }
    return -1;
  }

  static void SyncPropHash(SerializedProperty propProperty, SerializedProperty propHashProperty) {
    if (propProperty == null || propHashProperty == null) return;
    var prop = propProperty.stringValue ?? "";
    var expectedHash = string.IsNullOrWhiteSpace(prop) ? 0 : Shader.PropertyToID(prop);
    if (propHashProperty.intValue == expectedHash && !propHashProperty.hasMultipleDifferentValues) return;
    propHashProperty.intValue = expectedHash;
  }
}

[CustomPropertyDrawer(typeof(AllIn1AnimatorInspector.FloatAnimation))]
class AllIn1FloatAnimationDrawer : AllIn1AnimatorPropDrawerBase {
  protected override AllIn1ShaderPropertyKind PropertyKind => AllIn1ShaderPropertyKind.Float;
}

[CustomPropertyDrawer(typeof(AllIn1AnimatorInspector.ColorAnimation))]
class AllIn1ColorAnimationDrawer : AllIn1AnimatorPropDrawerBase {
  protected override AllIn1ShaderPropertyKind PropertyKind => AllIn1ShaderPropertyKind.Color;
}

[CustomPropertyDrawer(typeof(AllIn1AnimatorInspector.VectorAnimation))]
class AllIn1VectorAnimationDrawer : AllIn1AnimatorPropDrawerBase {
  protected override AllIn1ShaderPropertyKind PropertyKind => AllIn1ShaderPropertyKind.Vector;
}

[CustomPropertyDrawer(typeof(AllIn1AnimatorInspector.TextureAssignment))]
class AllIn1TextureAssignmentDrawer : AllIn1AnimatorPropDrawerBase {
  protected override AllIn1ShaderPropertyKind PropertyKind => AllIn1ShaderPropertyKind.Texture;
}
#endif
