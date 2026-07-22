using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class EndlessNumber :
  IComparable<EndlessNumber>,
  IEquatable<EndlessNumber>,
  ISerializationCallbackReceiver {
  const double Thousand = 1000d;
  const long InsignificantGroupDistance = 108L;

  // Numeric value = mantissa * (1000 ^ thousandsGroup).
  [SerializeField] double mantissa;
  [SerializeField] long thousandsGroup;

  // Public setters keep this compatible with the project's reflection-based SaveData serializer.
  // Values are normalized before arithmetic, comparison, or display.
  public double Mantissa {
    get => mantissa;
    set => mantissa = value;
  }

  public long ThousandsGroup {
    get => thousandsGroup;
    set => thousandsGroup = value;
  }

  public bool IsZero => mantissa == 0d;

  public int Sign {
    get {
      GetNormalizedComponents(this, out var normalizedMantissa, out _);
      return Math.Sign(normalizedMantissa);
    }
  }

  public bool IsPositive => Sign > 0;
  public bool IsNegative => Sign < 0;

  public string TierId {
    get {
      GetNormalizedComponents(this, out _, out var group);
      return EndlessNumberSuffixMap.GetTierId(group);
    }
  }

  public string Suffix => TierId;

  public string CompactSuffix {
    get {
      GetNormalizedComponents(this, out _, out var group);
      return EndlessNumberSuffixMap.GetCompactSuffix(group);
    }
  }

  public int[] TierTokens {
    get {
      GetNormalizedComponents(this, out _, out var group);
      return EndlessNumberSuffixMap.GetTierTokens(group);
    }
  }

  public EndlessNumber() {
  }

  public EndlessNumber(double value) {
    Set(value);
  }

  public EndlessNumber(double valueMantissa, long valueThousandsGroup) {
    Set(valueMantissa, valueThousandsGroup);
  }

  public static EndlessNumber FromDouble(double value) {
    return new EndlessNumber(value);
  }

  public static EndlessNumber Pow(double baseValue, long exponent) {
    if (double.IsNaN(baseValue) ||
        double.IsInfinity(baseValue) ||
        baseValue < 0d) {
      throw new ArgumentOutOfRangeException(
        nameof(baseValue),
        "EndlessNumber.Pow requires a finite, non-negative base."
      );
    }
    if (exponent < 0L) {
      throw new ArgumentOutOfRangeException(
        nameof(exponent),
        "EndlessNumber.Pow requires a non-negative exponent."
      );
    }

    var result = new EndlessNumber(1d);
    var factor = new EndlessNumber(baseValue);
    var remainingExponent = exponent;
    while (remainingExponent > 0L) {
      if ((remainingExponent & 1L) != 0L) {
        result.MultiplyInPlace(factor);
      }

      remainingExponent >>= 1;
      if (remainingExponent > 0L) {
        factor.MultiplyInPlace(factor.Copy());
      }
    }

    return result;
  }

  public static EndlessNumber FromTierTokens(double valueMantissa, params int[] tierTokens) {
    if (!EndlessNumberSuffixMap.TryGetThousandsGroup(tierTokens, out var valueGroup)) {
      throw new ArgumentException("Endless-number tier tokens must each be between 1 and 24.", nameof(tierTokens));
    }

    return new EndlessNumber(valueMantissa, valueGroup);
  }

  public EndlessNumber Copy() {
    GetNormalizedComponents(this, out var normalizedMantissa, out var normalizedGroup);
    return new EndlessNumber(normalizedMantissa, normalizedGroup);
  }

  public EndlessNumber Set(double value) {
    return Set(value, 0L);
  }

  public EndlessNumber Set(EndlessNumber value) {
    if (value == null) {
      throw new ArgumentNullException(nameof(value));
    }

    GetNormalizedComponents(value, out var valueMantissa, out var valueGroup);
    return Set(valueMantissa, valueGroup);
  }

  public EndlessNumber Set(double valueMantissa, long valueThousandsGroup) {
    NormalizeComponents(ref valueMantissa, ref valueThousandsGroup, throwOnInvalid: true);
    mantissa = valueMantissa;
    thousandsGroup = valueThousandsGroup;
    return this;
  }

  public EndlessNumber SetTierTokens(double valueMantissa, IReadOnlyList<int> tierTokens) {
    if (!EndlessNumberSuffixMap.TryGetThousandsGroup(tierTokens, out var valueGroup)) {
      throw new ArgumentException("Endless-number tier tokens must each be between 1 and 24.", nameof(tierTokens));
    }

    return Set(valueMantissa, valueGroup);
  }

  public EndlessNumber AddInPlace(double value) {
    var valueGroup = 0L;
    NormalizeComponents(ref value, ref valueGroup, throwOnInvalid: true);
    return AddComponentsInPlace(value, valueGroup);
  }

  public EndlessNumber AddInPlace(EndlessNumber value) {
    if (value == null) {
      throw new ArgumentNullException(nameof(value));
    }

    GetNormalizedComponents(value, out var valueMantissa, out var valueGroup);
    return AddComponentsInPlace(valueMantissa, valueGroup);
  }

  public EndlessNumber SubtractInPlace(double value) {
    return AddInPlace(-value);
  }

  public EndlessNumber SubtractInPlace(EndlessNumber value) {
    if (value == null) {
      throw new ArgumentNullException(nameof(value));
    }

    GetNormalizedComponents(value, out var valueMantissa, out var valueGroup);
    return AddComponentsInPlace(-valueMantissa, valueGroup);
  }

  public EndlessNumber MultiplyInPlace(double value) {
    var valueGroup = 0L;
    NormalizeComponents(ref value, ref valueGroup, throwOnInvalid: true);
    return MultiplyComponentsInPlace(value, valueGroup);
  }

  public EndlessNumber MultiplyInPlace(EndlessNumber value) {
    if (value == null) {
      throw new ArgumentNullException(nameof(value));
    }

    GetNormalizedComponents(value, out var valueMantissa, out var valueGroup);
    return MultiplyComponentsInPlace(valueMantissa, valueGroup);
  }

  public string ToDisplayString() {
    GetNormalizedComponents(this, out var normalizedMantissa, out var normalizedGroup);
    return FormatNormalized(normalizedMantissa, normalizedGroup, useSpriteGlyphs: false);
  }

  public string ToGlyphString() {
    GetNormalizedComponents(this, out var normalizedMantissa, out var normalizedGroup);
    return FormatNormalized(normalizedMantissa, normalizedGroup, useSpriteGlyphs: true);
  }

  public static string Format(double value) {
    var group = 0L;
    NormalizeComponents(ref value, ref group, throwOnInvalid: true);
    return FormatNormalized(value, group, useSpriteGlyphs: false);
  }

  public static string FormatGlyphs(double value) {
    var group = 0L;
    NormalizeComponents(ref value, ref group, throwOnInvalid: true);
    return FormatNormalized(value, group, useSpriteGlyphs: true);
  }

  public bool TryToDouble(out double value) {
    GetNormalizedComponents(this, out var normalizedMantissa, out var normalizedGroup);
    value = normalizedMantissa * Math.Pow(Thousand, normalizedGroup);
    if (!double.IsInfinity(value)) {
      return true;
    }

    value = 0d;
    return false;
  }

  public double RatioTo(EndlessNumber denominator) {
    if (denominator == null) {
      throw new ArgumentNullException(nameof(denominator));
    }

    GetNormalizedComponents(this, out var numeratorMantissa, out var numeratorGroup);
    GetNormalizedComponents(denominator, out var denominatorMantissa, out var denominatorGroup);
    if (denominatorMantissa == 0d) {
      throw new DivideByZeroException("Cannot calculate an EndlessNumber ratio with a zero denominator.");
    }
    if (numeratorMantissa == 0d) {
      return 0d;
    }

    var groupDistance = numeratorGroup - denominatorGroup;
    var sign = Math.Sign(numeratorMantissa) * Math.Sign(denominatorMantissa);
    if (groupDistance > 100L) {
      return sign * double.MaxValue;
    }
    if (groupDistance < -100L) {
      return 0d;
    }

    return (numeratorMantissa / denominatorMantissa) * Math.Pow(Thousand, groupDistance);
  }

  public float ToSingleClamped() {
    if (!TryToDouble(out var value) || Math.Abs(value) > float.MaxValue) {
      return Sign < 0 ? -float.MaxValue : float.MaxValue;
    }

    return (float)value;
  }

  public int ToInt32Clamped() {
    if (!TryToDouble(out var value)) {
      return Sign < 0 ? int.MinValue : int.MaxValue;
    }
    if (value >= int.MaxValue) {
      return int.MaxValue;
    }
    if (value <= int.MinValue) {
      return int.MinValue;
    }

    return (int)Math.Round(value, MidpointRounding.AwayFromZero);
  }

  public static EndlessNumber Min(EndlessNumber left, EndlessNumber right) {
    RequireValue(left, nameof(left));
    RequireValue(right, nameof(right));
    return left.CompareTo(right) <= 0 ? left.Copy() : right.Copy();
  }

  public static EndlessNumber Max(EndlessNumber left, EndlessNumber right) {
    RequireValue(left, nameof(left));
    RequireValue(right, nameof(right));
    return left.CompareTo(right) >= 0 ? left.Copy() : right.Copy();
  }

  public int CompareTo(EndlessNumber other) {
    if (other == null) {
      return 1;
    }

    GetNormalizedComponents(this, out var leftMantissa, out var leftGroup);
    GetNormalizedComponents(other, out var rightMantissa, out var rightGroup);

    var leftSign = Math.Sign(leftMantissa);
    var rightSign = Math.Sign(rightMantissa);
    if (leftSign != rightSign) {
      return leftSign.CompareTo(rightSign);
    }
    if (leftSign == 0) {
      return 0;
    }

    var magnitudeComparison = leftGroup.CompareTo(rightGroup);
    if (magnitudeComparison == 0) {
      magnitudeComparison = Math.Abs(leftMantissa).CompareTo(Math.Abs(rightMantissa));
    }

    return leftSign > 0 ? magnitudeComparison : -magnitudeComparison;
  }

  public bool Equals(EndlessNumber other) {
    return other != null && CompareTo(other) == 0;
  }

  public override bool Equals(object obj) {
    return obj is EndlessNumber other && Equals(other);
  }

  public override int GetHashCode() {
    GetNormalizedComponents(this, out var normalizedMantissa, out var normalizedGroup);
    unchecked {
      return (normalizedMantissa.GetHashCode() * 397) ^ normalizedGroup.GetHashCode();
    }
  }

  public override string ToString() {
    return ToDisplayString();
  }

  public static EndlessNumber operator +(EndlessNumber left, EndlessNumber right) {
    RequireValue(left, nameof(left));
    return left.Copy().AddInPlace(right);
  }

  public static EndlessNumber operator -(EndlessNumber left, EndlessNumber right) {
    RequireValue(left, nameof(left));
    return left.Copy().SubtractInPlace(right);
  }

  public static EndlessNumber operator *(EndlessNumber left, EndlessNumber right) {
    RequireValue(left, nameof(left));
    return left.Copy().MultiplyInPlace(right);
  }

  public static EndlessNumber operator *(EndlessNumber left, double right) {
    RequireValue(left, nameof(left));
    return left.Copy().MultiplyInPlace(right);
  }

  public static EndlessNumber operator *(double left, EndlessNumber right) {
    RequireValue(right, nameof(right));
    return right.Copy().MultiplyInPlace(left);
  }

  public static bool operator <(EndlessNumber left, EndlessNumber right) {
    RequireValue(left, nameof(left));
    return left.CompareTo(right) < 0;
  }

  public static bool operator >(EndlessNumber left, EndlessNumber right) {
    RequireValue(left, nameof(left));
    return left.CompareTo(right) > 0;
  }

  public static bool operator <=(EndlessNumber left, EndlessNumber right) {
    RequireValue(left, nameof(left));
    return left.CompareTo(right) <= 0;
  }

  public static bool operator >=(EndlessNumber left, EndlessNumber right) {
    RequireValue(left, nameof(left));
    return left.CompareTo(right) >= 0;
  }

  public static bool operator ==(EndlessNumber left, EndlessNumber right) {
    if (ReferenceEquals(left, right)) {
      return true;
    }
    if (left is null || right is null) {
      return false;
    }

    return left.Equals(right);
  }

  public static bool operator !=(EndlessNumber left, EndlessNumber right) {
    return !(left == right);
  }

  public void OnBeforeSerialize() {
  }

  public void OnAfterDeserialize() {
    NormalizeComponents(ref mantissa, ref thousandsGroup, throwOnInvalid: false);
  }

  EndlessNumber AddComponentsInPlace(double valueMantissa, long valueGroup) {
    GetNormalizedComponents(this, out var currentMantissa, out var currentGroup);
    if (currentMantissa == 0d) {
      return Set(valueMantissa, valueGroup);
    }
    if (valueMantissa == 0d) {
      return Set(currentMantissa, currentGroup);
    }

    var resultGroup = Math.Max(currentGroup, valueGroup);
    currentMantissa = ScaleToGroup(currentMantissa, currentGroup, resultGroup);
    valueMantissa = ScaleToGroup(valueMantissa, valueGroup, resultGroup);
    return Set(currentMantissa + valueMantissa, resultGroup);
  }

  EndlessNumber MultiplyComponentsInPlace(double valueMantissa, long valueGroup) {
    GetNormalizedComponents(this, out var currentMantissa, out var currentGroup);
    if (currentMantissa == 0d || valueMantissa == 0d) {
      return Set(0d);
    }

    var resultGroup = checked(currentGroup + valueGroup);
    return Set(currentMantissa * valueMantissa, resultGroup);
  }

  static double ScaleToGroup(double valueMantissa, long valueGroup, long targetGroup) {
    var distance = targetGroup - valueGroup;
    if (distance >= InsignificantGroupDistance) {
      return 0d;
    }

    return valueMantissa / Math.Pow(Thousand, distance);
  }

  static string FormatNormalized(double valueMantissa, long valueGroup, bool useSpriteGlyphs) {
    if (valueMantissa == 0d) {
      return "0";
    }

    // Keep 0 through 9,999 un-abbreviated. Rounding is checked first so 9,999.5
    // becomes 10 plus tier token 1 instead of leaking a fifth numeric digit.
    if (valueGroup == 0L || (valueGroup == 1L && Math.Abs(valueMantissa) < 10d)) {
      var unscaledValue = valueGroup == 0L
        ? valueMantissa
        : valueMantissa * Thousand;
      var roundedWholeValue = Math.Round(unscaledValue, MidpointRounding.AwayFromZero);
      if (Math.Abs(roundedWholeValue) < 10000d) {
        return roundedWholeValue.ToString("0", CultureInfo.InvariantCulture);
      }

      valueMantissa = roundedWholeValue / Thousand;
      valueGroup = 1L;
    }

    var roundedMantissa = Math.Round(valueMantissa, 1, MidpointRounding.AwayFromZero);
    if (Math.Abs(roundedMantissa) >= Thousand) {
      roundedMantissa /= Thousand;
      valueGroup = checked(valueGroup + 1L);
    }

    var suffix = useSpriteGlyphs
      ? EndlessNumberSuffixMap.GetGlyphSuffix(valueGroup)
      : "[" + EndlessNumberSuffixMap.GetTierId(valueGroup) + "]";
    return roundedMantissa.ToString("0.#", CultureInfo.InvariantCulture) + suffix;
  }

  static void GetNormalizedComponents(
    EndlessNumber value,
    out double normalizedMantissa,
    out long normalizedGroup
  ) {
    normalizedMantissa = value.mantissa;
    normalizedGroup = value.thousandsGroup;
    NormalizeComponents(ref normalizedMantissa, ref normalizedGroup, throwOnInvalid: true);
  }

  static void NormalizeComponents(
    ref double valueMantissa,
    ref long valueGroup,
    bool throwOnInvalid
  ) {
    if (double.IsNaN(valueMantissa) || double.IsInfinity(valueMantissa) || valueGroup < 0L) {
      if (throwOnInvalid) {
        throw new ArgumentOutOfRangeException(
          nameof(valueMantissa),
          "EndlessNumber requires a finite mantissa and a non-negative thousands group."
        );
      }

      valueMantissa = 0d;
      valueGroup = 0L;
      return;
    }

    if (valueMantissa == 0d) {
      valueGroup = 0L;
      return;
    }

    while (Math.Abs(valueMantissa) >= Thousand) {
      valueMantissa /= Thousand;
      valueGroup = checked(valueGroup + 1L);
    }

    while (valueGroup > 0L && Math.Abs(valueMantissa) < 1d) {
      valueMantissa *= Thousand;
      valueGroup--;
    }
  }

  static void RequireValue(EndlessNumber value, string parameterName) {
    if (value == null) {
      throw new ArgumentNullException(parameterName);
    }
  }
}

public static class EndlessNumberSuffixMap {
  // Bijective base 24: 24 = [24], 25 = [1,1], 48 = [1,24], 49 = [2,1].
  public const int TokenRadix = 24;
  public const string SpriteLibraryName = "UI/DamageNumbers";
  public const string SpriteCategory = "Tiers";
  const char FirstSpriteGlyphCharacter = '\uE000';
  const int MaximumTokenSequenceLength = 16;

  public static string GetTierId(long thousandsGroup) {
    ValidateGroup(thousandsGroup);
    var tokens = GetTierTokens(thousandsGroup);
    if (tokens.Length == 0) {
      return "";
    }

    var builder = new StringBuilder(tokens.Length * 3);
    for (var tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++) {
      if (tokenIndex > 0) {
        builder.Append('/');
      }
      builder.Append(tokens[tokenIndex]);
    }

    return builder.ToString();
  }

  public static string GetCompactSuffix(long thousandsGroup) {
    ValidateGroup(thousandsGroup);
    var tokens = GetTierTokens(thousandsGroup);
    if (tokens.Length == 0) {
      return "";
    }

    var builder = new StringBuilder(tokens.Length * 2);
    for (var tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++) {
      builder.Append(tokens[tokenIndex]);
    }

    return builder.ToString();
  }

  public static string GetGlyphSuffix(long thousandsGroup) {
    ValidateGroup(thousandsGroup);
    if (thousandsGroup == 0L) {
      return "";
    }

    var tokenBuffer = new int[MaximumTokenSequenceLength];
    var firstTokenIndex = WriteTierTokens(thousandsGroup, tokenBuffer);
    var glyphs = new char[tokenBuffer.Length - firstTokenIndex];
    for (var tokenIndex = firstTokenIndex; tokenIndex < tokenBuffer.Length; tokenIndex++) {
      glyphs[tokenIndex - firstTokenIndex] = GetGlyphCharacter(tokenBuffer[tokenIndex]);
    }

    return new string(glyphs);
  }

  public static int[] GetTierTokens(long thousandsGroup) {
    ValidateGroup(thousandsGroup);
    if (thousandsGroup == 0L) {
      return Array.Empty<int>();
    }

    var tokenBuffer = new int[MaximumTokenSequenceLength];
    var firstTokenIndex = WriteTierTokens(thousandsGroup, tokenBuffer);
    var tokens = new int[tokenBuffer.Length - firstTokenIndex];
    Array.Copy(tokenBuffer, firstTokenIndex, tokens, 0, tokens.Length);
    return tokens;
  }

  public static bool TryGetThousandsGroup(
    IReadOnlyList<int> tierTokens,
    out long thousandsGroup
  ) {
    thousandsGroup = 0L;
    if (tierTokens == null || tierTokens.Count == 0) {
      return true;
    }

    for (var tokenIndex = 0; tokenIndex < tierTokens.Count; tokenIndex++) {
      var token = tierTokens[tokenIndex];
      if (token < 1 || token > TokenRadix) {
        thousandsGroup = 0L;
        return false;
      }
      if (thousandsGroup > (long.MaxValue - token) / TokenRadix) {
        thousandsGroup = 0L;
        return false;
      }

      thousandsGroup = thousandsGroup * TokenRadix + token;
    }

    return true;
  }

  public static bool TryGetSpriteToken(char glyphCharacter, out int token) {
    token = glyphCharacter - FirstSpriteGlyphCharacter + 1;
    if (token >= 1 && token <= TokenRadix) {
      return true;
    }

    token = 0;
    return false;
  }

  public static char GetGlyphCharacter(int token) {
    if (token < 1 || token > TokenRadix) {
      throw new ArgumentOutOfRangeException(
        nameof(token),
        "Endless-number sprite tokens must be between 1 and 24."
      );
    }

    return (char)(FirstSpriteGlyphCharacter + token - 1);
  }

  static int WriteTierTokens(long thousandsGroup, int[] tokenBuffer) {
    var value = thousandsGroup;
    var writeIndex = tokenBuffer.Length;
    while (value > 0L) {
      value--;
      tokenBuffer[--writeIndex] = (int)(value % TokenRadix) + 1;
      value /= TokenRadix;
    }

    return writeIndex;
  }

  static void ValidateGroup(long thousandsGroup) {
    if (thousandsGroup < 0L) {
      throw new ArgumentOutOfRangeException(
        nameof(thousandsGroup),
        "Endless-number suffix groups cannot be negative."
      );
    }
  }
}
