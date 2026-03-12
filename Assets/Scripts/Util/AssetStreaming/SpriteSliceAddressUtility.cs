using System;
using System.Collections.Generic;
using System.Globalization;

public static class SpriteSliceAddressUtility {
  public static IComparer<string> NaturalStringComparer { get; } = Comparer<string>.Create(CompareNaturally);

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

  public static int CompareNaturally(string left, string right) {
    var normalizedLeft = left ?? "";
    var normalizedRight = right ?? "";
    var leftIndex = 0;
    var rightIndex = 0;

    while (leftIndex < normalizedLeft.Length && rightIndex < normalizedRight.Length) {
      var leftChar = normalizedLeft[leftIndex];
      var rightChar = normalizedRight[rightIndex];
      if (char.IsDigit(leftChar) && char.IsDigit(rightChar)) {
        var leftDigitsStart = leftIndex;
        var rightDigitsStart = rightIndex;
        while (leftIndex < normalizedLeft.Length && char.IsDigit(normalizedLeft[leftIndex])) leftIndex++;
        while (rightIndex < normalizedRight.Length && char.IsDigit(normalizedRight[rightIndex])) rightIndex++;

        var numericComparison = CompareNumericRuns(
          normalizedLeft,
          leftDigitsStart,
          leftIndex - leftDigitsStart,
          normalizedRight,
          rightDigitsStart,
          rightIndex - rightDigitsStart);
        if (numericComparison != 0) return numericComparison;
        continue;
      }

      var leftUpper = char.ToUpperInvariant(leftChar);
      var rightUpper = char.ToUpperInvariant(rightChar);
      if (leftUpper != rightUpper) {
        return leftUpper.CompareTo(rightUpper);
      }

      leftIndex++;
      rightIndex++;
    }

    if (leftIndex < normalizedLeft.Length) return 1;
    if (rightIndex < normalizedRight.Length) return -1;
    return 0;
  }

  static bool TryNormalizeNumericToken(string value, out string numericValue) {
    numericValue = "";
    if (string.IsNullOrWhiteSpace(value)) return false;
    if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return false;
    if (parsed < 0) return false;
    numericValue = parsed.ToString(CultureInfo.InvariantCulture);
    return true;
  }

  static int CompareNumericRuns(
    string left,
    int leftStart,
    int leftLength,
    string right,
    int rightStart,
    int rightLength) {
    var trimmedLeftStart = leftStart;
    var trimmedRightStart = rightStart;
    while (trimmedLeftStart < (leftStart + leftLength) && left[trimmedLeftStart] == '0') trimmedLeftStart++;
    while (trimmedRightStart < (rightStart + rightLength) && right[trimmedRightStart] == '0') trimmedRightStart++;

    var trimmedLeftLength = (leftStart + leftLength) - trimmedLeftStart;
    var trimmedRightLength = (rightStart + rightLength) - trimmedRightStart;
    if (trimmedLeftLength != trimmedRightLength) {
      return trimmedLeftLength.CompareTo(trimmedRightLength);
    }

    for (var i = 0; i < trimmedLeftLength; i++) {
      var leftChar = left[trimmedLeftStart + i];
      var rightChar = right[trimmedRightStart + i];
      if (leftChar != rightChar) {
        return leftChar.CompareTo(rightChar);
      }
    }

    return leftLength.CompareTo(rightLength);
  }
}
