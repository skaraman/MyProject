using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class SpriteRuntimeResolver {
  const string LegacyFormUiRoot = "FormUIs/";
  const string LegacyFormUiSuffix = "UI";
  static readonly string[] LegacyFormUiForms = {
    "Aqua",
    "Base",
    "Bolt",
    "Cold",
    "Dark",
    "Fire"
  };
  static readonly LegacyFormUiAlias[] LegacyFormUiAliases = {
    new LegacyFormUiAlias("HealthBar/HealthBarUI", "HealthBar"),
    new LegacyFormUiAlias("Dialog/DialogEsper", "Dialog"),
    new LegacyFormUiAlias("Dialog/DialogUI", "Dialog")
  };

  readonly struct LegacyFormUiAlias {
    public readonly string namepart;
    public readonly string runtimeCategory;

    public LegacyFormUiAlias(string namepart, string runtimeCategory) {
      this.namepart = namepart ?? "";
      this.runtimeCategory = runtimeCategory ?? "";
    }
  }

  static bool IsLegacyFormUiNamepart(string normalizedNamepart) {
    return TryGetLegacyFormUiAlias(normalizedNamepart, out _);
  }

  static bool TryGetLegacyFormUiAlias(string normalizedNamepart, out LegacyFormUiAlias alias) {
    alias = default;
    if (string.IsNullOrWhiteSpace(normalizedNamepart)) return false;

    for (var i = 0; i < LegacyFormUiAliases.Length; i++) {
      var candidate = LegacyFormUiAliases[i];
      if (!string.Equals(candidate.namepart, normalizedNamepart, StringComparison.OrdinalIgnoreCase)) continue;
      alias = candidate;
      return true;
    }

    return false;
  }

  static bool TryBuildLegacyFormUiKey(SpriteLookupKey key, out SpriteLookupKey aliasKey) {
    aliasKey = default;

    var normalizedNamepart = NormalizeNamePart(key.namepart);
    if (!TryGetLegacyFormUiAlias(normalizedNamepart, out var alias)) return false;

    var form = NormalizeToken(key.labelPrefix);
    var label = NormalizeToken(key.category);
    if (string.IsNullOrWhiteSpace(form)) return false;
    if (string.IsNullOrWhiteSpace(label)) return false;
    if (string.IsNullOrWhiteSpace(alias.runtimeCategory)) return false;
    if (string.Equals(label, alias.runtimeCategory, StringComparison.OrdinalIgnoreCase)) return false;

    var aliasNamepart = BuildLegacyFormUiNamepart(form);
    if (string.IsNullOrWhiteSpace(aliasNamepart)) return false;

    aliasKey = new SpriteLookupKey(
      aliasNamepart,
      label,
      alias.runtimeCategory,
      key.frame
    );
    return true;
  }

  static string BuildLegacyFormUiNamepart(string form) {
    var normalizedForm = NormalizeToken(form);
    if (string.IsNullOrWhiteSpace(normalizedForm)) return "";
    return LegacyFormUiRoot + normalizedForm + LegacyFormUiSuffix;
  }

  static void QueuePendingWarmupNamepart(string normalizedNamepart) {
    if (string.IsNullOrWhiteSpace(normalizedNamepart)) return;
    if (!pendingWarmupNamepartsSet.Add(normalizedNamepart)) return;
    pendingWarmupNameparts.Add(normalizedNamepart);
  }

  static void QueueLegacyFormUiAliasWarmups(string normalizedNamepart) {
    if (!IsLegacyFormUiNamepart(normalizedNamepart)) return;

    for (var i = 0; i < LegacyFormUiForms.Length; i++) {
      var aliasNamepart = BuildLegacyFormUiNamepart(LegacyFormUiForms[i]);
      QueuePendingWarmupNamepart(aliasNamepart);
    }
  }

  static void AddLegacyFormUiAliasWarmups(List<string> warmups, string normalizedNamepart) {
    if (warmups == null) return;
    if (!IsLegacyFormUiNamepart(normalizedNamepart)) return;

    for (var i = 0; i < LegacyFormUiForms.Length; i++) {
      var aliasNamepart = BuildLegacyFormUiNamepart(LegacyFormUiForms[i]);
      if (string.IsNullOrWhiteSpace(aliasNamepart)) continue;
      warmups.Add(aliasNamepart);
    }
  }

  static bool AreLegacyFormUiAliasShardsReady() {
    for (var i = 0; i < LegacyFormUiForms.Length; i++) {
      var aliasNamepart = BuildLegacyFormUiNamepart(LegacyFormUiForms[i]);
      if (IsNamepartShardReady(aliasNamepart)) continue;
      return false;
    }

    return true;
  }

  static bool IsNamepartShardReady(string normalizedNamepart) {
    if (string.IsNullOrWhiteSpace(normalizedNamepart)) return true;
    if (!TryGetManifestEntryForNamepart(manifestByNamepart, normalizedNamepart, out var entry)) return true;

    var shardKey = string.IsNullOrWhiteSpace(entry.namepart) ? normalizedNamepart : entry.namepart;
    if (loadedShards.ContainsKey(shardKey)) return true;

    if (shardParses.TryGetValue(shardKey, out var shardParseTask) &&
        shardParseTask.IsCompleted &&
        TryGetShard(shardKey, entry, out _)) {
      return true;
    }

    return false;
  }

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

  static bool TryBuildGearSplitNamepart(string normalizedNamepart, string labelPrefix, out string splitNamepart) {
    splitNamepart = "";

    var prefixToken = BuildGearSplitToken(labelPrefix);
    if (string.IsNullOrWhiteSpace(prefixToken)) return false;
    if (string.IsNullOrWhiteSpace(normalizedNamepart)) return false;

    var slash = normalizedNamepart.LastIndexOf('/');
    var folder = slash >= 0 ? normalizedNamepart.Substring(0, slash) : "";
    var leaf = slash >= 0 ? normalizedNamepart.Substring(slash + 1) : normalizedNamepart;
    if (string.IsNullOrWhiteSpace(leaf)) return false;
    if (!leaf.StartsWith("Gear", StringComparison.OrdinalIgnoreCase)) return false;
    if (leaf.EndsWith("_split", StringComparison.OrdinalIgnoreCase)) return false;

    var partName = leaf.Substring("Gear".Length);
    var partToken = BuildGearSplitPartToken(partName);
    if (string.IsNullOrWhiteSpace(partToken)) return false;

    var splitLeaf = leaf + "_split/" + prefixToken + "_" + partToken;
    splitNamepart = string.IsNullOrWhiteSpace(folder) ? splitLeaf : folder + "/" + splitLeaf;
    return true;
  }

  static string BuildGearSplitToken(string value) {
    var normalized = NormalizeToken(value);
    if (string.IsNullOrWhiteSpace(normalized)) return "";

    var builder = new System.Text.StringBuilder(normalized.Length);
    var previousWasUnderscore = false;

    for (var i = 0; i < normalized.Length; i++) {
      var ch = normalized[i];
      if (char.IsWhiteSpace(ch) || ch == '-' || ch == '/') {
        if (!previousWasUnderscore && builder.Length > 0) {
          builder.Append('_');
          previousWasUnderscore = true;
        }
        continue;
      }

      if (ch == '_') {
        if (!previousWasUnderscore && builder.Length > 0) {
          builder.Append('_');
          previousWasUnderscore = true;
        }
        continue;
      }

      builder.Append(char.ToLowerInvariant(ch));
      previousWasUnderscore = false;
    }

    return builder.ToString().Trim('_');
  }

  static string BuildGearSplitPartToken(string value) {
    var normalized = NormalizeToken(value);
    if (string.IsNullOrWhiteSpace(normalized)) return "";

    var builder = new System.Text.StringBuilder(normalized.Length + 8);
    for (var i = 0; i < normalized.Length; i++) {
      var ch = normalized[i];
      if (char.IsWhiteSpace(ch) || ch == '-' || ch == '/' || ch == '_') {
        AppendGearSplitUnderscore(builder);
        continue;
      }

      if (char.IsUpper(ch)) {
        if (builder.Length > 0) {
          AppendGearSplitUnderscore(builder);
        }
        builder.Append(char.ToLowerInvariant(ch));
        continue;
      }

      builder.Append(char.ToLowerInvariant(ch));
    }

    return builder.ToString().Trim('_');
  }

  static void AppendGearSplitUnderscore(System.Text.StringBuilder builder) {
    if (builder == null || builder.Length <= 0) return;
    if (builder[builder.Length - 1] == '_') return;
    builder.Append('_');
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
