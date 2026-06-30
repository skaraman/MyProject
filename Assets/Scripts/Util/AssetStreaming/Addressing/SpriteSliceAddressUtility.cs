using System;
using System.Collections.Generic;
using System.Globalization;

public static class SpriteSliceAddressUtility {
  public static IComparer<string> NaturalStringComparer { get; } = Comparer<string>.Create(CompareNaturally);

  public static bool TryParseSliceAddress(string address, out string atlasAssetPath, out string spriteName) {
    atlasAssetPath = "";
    spriteName = "";
    if (string.IsNullOrWhiteSpace(address)) return false;

    if (!TryGetTrimmedSegmentBounds(address, 0, address.Length - 1, out var startIndex, out var totalLength)) return false;
    var endIndex = startIndex + totalLength - 1;
    if (address[endIndex] != ']') return false;

    var openBracket = address.LastIndexOf('[', endIndex - 1);
    if (openBracket <= startIndex || openBracket >= endIndex) return false;
    if (!TryGetTrimmedSegmentBounds(address, startIndex, openBracket - 1, out var atlasStart, out var atlasLength)) return false;
    if (!TryGetTrimmedSegmentBounds(address, openBracket + 1, endIndex - 1, out var spriteStart, out var spriteLength)) return false;
    if (!StartsWithRuntimeAssetPrefix(address, atlasStart, atlasLength)) return false;

    atlasAssetPath = address.Substring(atlasStart, atlasLength);
    spriteName = address.Substring(spriteStart, spriteLength);
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

  static bool TryGetTrimmedSegmentBounds(string value, int startInclusive, int endInclusive, out int startIndex, out int length) {
    startIndex = 0;
    length = 0;
    if (string.IsNullOrEmpty(value)) return false;
    if (startInclusive < 0) startInclusive = 0;
    if (endInclusive >= value.Length) endInclusive = value.Length - 1;
    if (startInclusive > endInclusive) return false;

    while (startInclusive <= endInclusive && char.IsWhiteSpace(value[startInclusive])) startInclusive++;
    while (endInclusive >= startInclusive && char.IsWhiteSpace(value[endInclusive])) endInclusive--;
    if (startInclusive > endInclusive) return false;

    startIndex = startInclusive;
    length = endInclusive - startInclusive + 1;
    return true;
  }

  static bool StartsWithRuntimeAssetPrefix(string value, int startIndex, int length) {
    return StartsWithPrefix(value, startIndex, length, "Assets/") ||
           StartsWithPrefix(value, startIndex, length, "Packages/com.skaraman.myprojectcontent/");
  }

  static bool StartsWithPrefix(string value, int startIndex, int length, string prefix) {
    if (string.IsNullOrEmpty(value)) return false;
    if (startIndex < 0 || length < prefix.Length) return false;
    if ((startIndex + length) > value.Length) return false;
    return string.Compare(value, startIndex, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0;
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
