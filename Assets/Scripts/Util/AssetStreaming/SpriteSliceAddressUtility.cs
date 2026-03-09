using System;
using System.Globalization;

public static class SpriteSliceAddressUtility {
  public static bool TryParseSliceAddress(string address, out string atlasAssetPath, out string spriteName) {
    atlasAssetPath = "";
    spriteName = "";
    if (string.IsNullOrWhiteSpace(address)) return false;

    var normalized = address.Trim();
    var closeBracket = normalized.LastIndexOf(']');
    if (closeBracket <= 0 || closeBracket != normalized.Length - 1) return false;

    var openBracket = normalized.LastIndexOf('[', closeBracket - 1);
    if (openBracket <= 0 || openBracket >= closeBracket - 1) return false;

    var parsedPath = normalized.Substring(0, openBracket).Trim();
    var parsedSprite = normalized.Substring(openBracket + 1, closeBracket - openBracket - 1).Trim();
    if (string.IsNullOrWhiteSpace(parsedPath) || string.IsNullOrWhiteSpace(parsedSprite)) return false;
    if (!parsedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return false;

    atlasAssetPath = parsedPath;
    spriteName = parsedSprite;
    return true;
  }

  public static string BuildSliceAddress(string atlasAssetPath, string spriteName) {
    var normalizedPath = string.IsNullOrWhiteSpace(atlasAssetPath) ? "" : atlasAssetPath.Trim();
    var normalizedSprite = string.IsNullOrWhiteSpace(spriteName) ? "" : spriteName.Trim();
    if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedSprite)) return "";
    return normalizedPath + "[" + normalizedSprite + "]";
  }

  public static bool TryExtractNumericLabelValue(string value, out string numericValue) {
    numericValue = "";
    if (string.IsNullOrWhiteSpace(value)) return false;

    var normalized = value.Trim();
    if (TryNormalizeNumericToken(normalized, out numericValue)) return true;

    var underscoreIndex = normalized.LastIndexOf('_');
    if (underscoreIndex < 0 || underscoreIndex >= normalized.Length - 1) return false;
    return TryNormalizeNumericToken(normalized.Substring(underscoreIndex + 1), out numericValue);
  }

  public static bool HasEquivalentNumericLabel(string left, string right) {
    if (!TryExtractNumericLabelValue(left, out var leftNumeric)) return false;
    if (!TryExtractNumericLabelValue(right, out var rightNumeric)) return false;
    return string.Equals(leftNumeric, rightNumeric, StringComparison.Ordinal);
  }

  static bool TryNormalizeNumericToken(string value, out string numericValue) {
    numericValue = "";
    if (string.IsNullOrWhiteSpace(value)) return false;
    if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return false;
    if (parsed < 0) return false;
    numericValue = parsed.ToString(CultureInfo.InvariantCulture);
    return true;
  }
}
