using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class SpriteRuntimeResolver {
  static bool TryResolveNumericFormFallback(Dictionary<string, SpriteAddressPair> rows, SpriteLookupKey key, out SpriteAddressPair pair) {
    pair = default;
    if (rows == null || rows.Count == 0) return false;
    if (key.frame != 0) return false;

    var normalizedLabelPrefix = NormalizeToken(key.labelPrefix);
    if (string.IsNullOrWhiteSpace(normalizedLabelPrefix)) return false;
    if (!int.TryParse(normalizedLabelPrefix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericFrame)) return false;
    if (numericFrame < 0) return false;

    var aliasKey = BuildRowKey("", key.category, numericFrame);
    return rows.TryGetValue(aliasKey, out pair);
  }

  // TODO: both methods update the cooldown but never emit the message — all resolver diagnostics
  // are silently dropped. Add Debug.Log(message) / Debug.LogWarning(message) after the cooldown update.
  static void RateLimitedLog(string key, string message) {
    var now = Time.realtimeSinceStartup;
    if (logCooldown.TryGetValue(key, out var last) && now - last < 5f) return;
    logCooldown[key] = now;
    Debug.Log(message);
  }

  static void RateLimitedWarning(string key, string message, Object logContext = null) {
    var now = Time.realtimeSinceStartup;
    if (logCooldown.TryGetValue(key, out var last) && now - last < 5f) return;
    logCooldown[key] = now;
    Debug.LogWarning(message, logContext);
  }

  static string BuildContextualWarningKey(string key, Object logContext) {
    if (logContext == null) return key ?? "";
    return (key ?? "") + "|ctx:" + ObjectEntityId.GetRawValue(logContext);
  }

  static string AppendWarningContext(string message, Object logContext) {
    if (logContext == null) return message ?? "";
    return (message ?? "") + " gameobject='" + logContext.name + "'";
  }

  static string BuildRowKey(string form, string animation, int frame) {
    return NormalizeToken(form) + "|" + NormalizeToken(animation) + "|" + frame;
  }

  static ResolverSettings GetSettings() {
    if (settingsLoaded) return settings;
    settingsLoaded = true;
    settings = new ResolverSettings {
      manifestAddress = RuntimeConfig.DefaultManifestAddress,
      maxLoadedShards = 48
    };

    var settingsAsset = Resources.Load<SpriteStreamingSettings>("SpriteStreamingSettings");
    if (settingsAsset != null) {
      settings.manifestAddress = string.IsNullOrWhiteSpace(settingsAsset.manifestAddress)
        ? RuntimeConfig.DefaultManifestAddress
        : settingsAsset.manifestAddress;
      settings.maxLoadedShards = Math.Max(settingsAsset.maxLoadedShards, 1);
    }

    return settings;
  }

  static string NormalizeToken(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    if (tokenNormCache.TryGetValue(value, out var cached)) return cached;

    var trimmed = value.Trim();
    if (trimmed.Length >= 2) {
      var first = trimmed[0];
      var last = trimmed[trimmed.Length - 1];
      if ((first == '"' && last == '"') || (first == '\'' && last == '\'')) {
        trimmed = trimmed.Substring(1, trimmed.Length - 2);
        if (first == '\'') {
          trimmed = trimmed.Replace("''", "'");
        }
      }
    }

    var result = string.IsNullOrWhiteSpace(trimmed) ? "" : trimmed.Trim();
    tokenNormCache[value] = result;
    return result;
  }

  static string Unescape(string value) {
    if (string.IsNullOrEmpty(value)) return "";
    return value
      .Replace("\\t", "\t")
      .Replace("\\n", "\n")
      .Replace("\\r", "\r")
      .Replace("\\\\", "\\");
  }

  static string CollapseSlashes(string value) {
    if (string.IsNullOrWhiteSpace(value)) return "";
    if (value.IndexOf("//", StringComparison.Ordinal) < 0) return value;

    var sb = new System.Text.StringBuilder(value.Length);
    var previousWasSlash = false;
    for (var i = 0; i < value.Length; i++) {
      var ch = value[i];
      if (ch == '/') {
        if (previousWasSlash) continue;
        previousWasSlash = true;
      }
      else {
        previousWasSlash = false;
      }
      sb.Append(ch);
    }

    return sb.ToString();
  }
}
