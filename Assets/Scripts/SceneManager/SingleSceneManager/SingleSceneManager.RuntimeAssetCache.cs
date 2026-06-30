using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class SingleSceneManager {
  IEnumerator StartupMainMenuRevealRoutine() {
    PrepareLoadingScreenCarrier();
    SetLoadingBlackscreenHold(true);
    ForceBlackscreenVisible(true);
    QueueMenuRuntimeAssetWarmup("startup_main_menu", includeLocationProfile: false);
    yield return PrewarmLoadMenuForStartupRoutine();
    for (var i = 0; i < 10; i++) {
      yield return null;
    }
    BeginStartupMainMenuReveal();
    startupMainMenuRevealRoutine = null;
  }

  IEnumerator PrewarmLoadMenuForStartupRoutine() {
    if (loadMenuStartupPrewarmed) yield break;

    if (LoadMenu == null) {
      loadMenuStartupPrewarmed = true;
      yield break;
    }

    var restoreSection = ResolveCurrentSection();
    if (restoreSection == Section.None) {
      restoreSection = Section.MainMenu;
    }

    var startedAt = Time.realtimeSinceStartup;
    QueueMenuRuntimeAssetWarmup("startup_load_menu_prewarm", includeLocationProfile: false);
    if (ShouldLogLoadFlowDebug()) {
      Debug.Log(
        "[SingleSceneManager][LoadMenuPrewarm] stage=begin" +
        " restore_section=" + restoreSection +
        " saves=" + (saveSlotView != null ? saveSlotView.SavesCount : -1) +
        " active_input_map=" + activeInputMap
      );
    }

    _SwitchMap("none");
    ApplySectionActivation(Section.LoadMenu);

    for (var i = 0; i < startupLoadMenuPrewarmFrames; i++) {
      yield return null;
    }

    ApplySectionActivation(restoreSection);
    ApplyInputForSection(restoreSection);
    loadMenuStartupPrewarmed = true;

    if (!ShouldLogLoadFlowDebug()) yield break;

    Debug.Log(
      "[SingleSceneManager][LoadMenuPrewarm] stage=complete" +
      " restore_section=" + restoreSection +
      " saves=" + (saveSlotView != null ? saveSlotView.SavesCount : -1) +
      " warm_frames=" + startupLoadMenuPrewarmFrames +
      " elapsed_ms=" + ((Time.realtimeSinceStartup - startedAt) * 1000f).ToString("0.0")
    );
  }

  void QueueMenuRuntimeAssetWarmup(string source, bool includeLocationProfile = true) {
    var globalLabelCount = CountWarmSources(SpriteStreamingRuntimeSettings.WarmUiRuntimeAssetLabels);
    if (globalLabelCount > 0) {
      RuntimeAssetCache.QueueWarmup(
        addresses: null,
        labels: SpriteStreamingRuntimeSettings.WarmUiRuntimeAssetLabels,
        scope: RuntimeAssetResidencyScope.GlobalUi,
        reason: source + ":global_ui"
      );
    }
  }

  static int CountWarmSources(IEnumerable<string> values) {
    if (values == null) return 0;
    var count = 0;
    foreach (var value in values) {
      if (!string.IsNullOrWhiteSpace(value)) count++;
    }
    return count;
  }

  void AddPersistentAtlasAddress(string address) {
    var normalized = string.IsNullOrWhiteSpace(address) ? "" : address.Trim();
    if (string.IsNullOrWhiteSpace(normalized)) {
      return;
    }

    if (!persistentAtlasSeenAddressScratch.Add(normalized)) {
      return;
    }

    persistentAtlasAddressScratch.Add(normalized);
  }

  void QueuePersistentAtlasMetadataWarmup(IList<string> addresses) {
    if (addresses == null || addresses.Count <= 0) {
      return;
    }

    for (var i = 0; i < addresses.Count; i++) {
      TrimmedSpriteOffsetResolver.RegisterWarmupMetadataCandidate(addresses[i]);
    }

    TrimmedSpriteOffsetResolver.QueueWarmupAtlasMetadataBatch(addresses, 0, addresses.Count);
  }

  void PrimePersistentFontAtlasPins(string source) {
    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();

    for (var i = 0; i < PersistentFontAtlasNames.Length; i++) {
      var fontName = PersistentFontAtlasNames[i];
      if (string.IsNullOrWhiteSpace(fontName)) continue;
      AddPersistentAtlasAddress(ResolveFontAtlasAddress(fontName));
    }

    if (persistentAtlasAddressScratch.Count <= 0) {
      persistentAtlasSeenAddressScratch.Clear();
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      PersistentFontAtlasPinOwnerId,
      TextureResidencyCache.PinClass.UI,
      persistentAtlasAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup
    );
    QueuePersistentAtlasMetadataWarmup(persistentAtlasAddressScratch);

    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][PersistentAtlasPins] source='" + (source ?? "") + "'" +
        " class=ui_fonts" +
        " addresses=" + persistentAtlasAddressScratch.Count
      );
    }

    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
  }

  void RefreshPersistentPlayerSkinAtlasPins(string source) {
    var player = ResolvePlayerGearController();
    if (player == null) {
      return;
    }

    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
    var maxPinnedAddresses = Math.Max(SpriteStreamingRuntimeSettings.PinBudgetPlayerAddresses, 1);
    var collectedCount = player.CollectPersistentSkinStartupAddresses(
      persistentAtlasAddressScratch,
      persistentAtlasSeenAddressScratch,
      maxPinnedAddresses
    );
    if (collectedCount <= 0 || persistentAtlasAddressScratch.Count <= 0) {
      persistentAtlasAddressScratch.Clear();
      persistentAtlasSeenAddressScratch.Clear();
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      PersistentPlayerSkinAtlasPinOwnerId,
      TextureResidencyCache.PinClass.Player,
      persistentAtlasAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup
    );
    QueuePersistentAtlasMetadataWarmup(persistentAtlasAddressScratch);

    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][PersistentAtlasPins] source='" + (source ?? "") + "'" +
        " class=player_skin" +
        " addresses=" + persistentAtlasAddressScratch.Count +
        " active_form='" + EsperanzaForms.GetActive() + "'" +
        " player='" + player.gameObject.name + "'"
      );
    }

    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
  }

  void RefreshPersistentPlayerEffectAtlasPins(string source) {
    var player = ResolvePlayerGearController();
    if (player == null) {
      return;
    }

    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
    var maxPinnedAddresses = Math.Max(SpriteStreamingRuntimeSettings.PinBudgetPlayerAddresses, 1);
    var collectedCount = player.CollectPersistentEffectStartupAddresses(
      persistentAtlasAddressScratch,
      CorePlayerWarmAnimationKeys,
      persistentAtlasSeenAddressScratch,
      maxPinnedAddresses
    );
    if (collectedCount <= 0 || persistentAtlasAddressScratch.Count <= 0) {
      persistentAtlasAddressScratch.Clear();
      persistentAtlasSeenAddressScratch.Clear();
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      PersistentPlayerEffectAtlasPinOwnerId,
      TextureResidencyCache.PinClass.Effect,
      persistentAtlasAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup
    );
    QueuePersistentAtlasMetadataWarmup(persistentAtlasAddressScratch);

      if (ShouldLogLoadingProgressDebug()) {
        Debug.Log(
          "[SingleSceneManager][PersistentAtlasPins] source='" + (source ?? "") + "'" +
          " class=player_effects" +
          " addresses=" + persistentAtlasAddressScratch.Count +
          " warm_animations=" + CorePlayerWarmAnimationKeys.Length +
          " projectile_manager=" + (player.projectileManager != null ? 1 : 0) +
          " player='" + player.gameObject.name + "'"
        );
      }

    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
  }

  void RefreshPersistentPlayerExpressionAtlasPins(string source) {
    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();

    AddPersistentAtlasAddress(ResolveEsperanzaExpressionAtlasAddress(".png"));
    if (persistentAtlasAddressScratch.Count <= 0) {
      persistentAtlasAddressScratch.Clear();
      persistentAtlasSeenAddressScratch.Clear();
      return;
    }

    TextureResidencyCache.UpdateOwnerPins(
      PersistentPlayerExpressionAtlasPinOwnerId,
      TextureResidencyCache.PinClass.UI,
      persistentAtlasAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup
    );
    QueuePersistentAtlasMetadataWarmup(persistentAtlasAddressScratch);

    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][PersistentAtlasPins] source='" + (source ?? "") + "'" +
        " class=player_expressions" +
        " addresses=" + persistentAtlasAddressScratch.Count
      );
    }

    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
  }

  void RefreshPersistentPlayerBaselineAtlasPins(string source) {
    RefreshPersistentPlayerSkinAtlasPins(source);
    RefreshPersistentPlayerEffectAtlasPins(source);
    RefreshPersistentPlayerExpressionAtlasPins(source);
  }

  int ResolveEnvironmentHotCacheAddressBudget() {
    var ownerCap = Math.Max(SpriteStreamingRuntimeSettings.MaxPinnedAddressesPerOwner, 128);
    return Math.Max(Math.Min(ownerCap, 4096), 128);
  }

  void CollectEnvironmentHotCacheSources(string locationId) {
    // Environment hot cache removed per performance goal.
  }

  void ApplyEnvironmentHotCacheSlot(string ownerId, string slotName, string locationId, string source) {
    if (!IsGameplayLocation(locationId)) {
      TextureResidencyCache.ReleaseOwnerPins(ownerId);
      return;
    }

    CollectEnvironmentHotCacheSources(locationId);
    if (environmentCacheLibraryScratch.Count > 0) {
      SpriteRuntimeResolver.WarmupLibraries(environmentCacheLibraryScratch);
    }

    if (environmentCacheAddressScratch.Count > 0) {
      TextureResidencyCache.UpdateOwnerPins(
        ownerId,
        TextureResidencyCache.PinClass.WarmGate,
        environmentCacheAddressScratch,
        TextureResidencyCache.LoadPriority.Warmup
      );
      QueuePersistentAtlasMetadataWarmup(environmentCacheAddressScratch);
    }
    else {
      TextureResidencyCache.ReleaseOwnerPins(ownerId);
    }

    if (!ShouldLogLoadingProgressDebug()) return;

    Debug.Log(
      "[SingleSceneManager][EnvironmentCache] stage=apply_slot" +
      " source='" + (source ?? "") + "'" +
      " slot=" + ResolveLoadFlowValue(slotName) +
      " location=" + ResolveLoadFlowValue(locationId) +
      " libraries=" + environmentCacheLibraryScratch.Count +
      " addresses=" + environmentCacheAddressScratch.Count +
      " asset_addresses=" + environmentCacheAssetAddressScratch.Count +
      " asset_labels=" + environmentCacheAssetLabelScratch.Count +
      " slot_budget=" + ResolveEnvironmentHotCacheAddressBudget()
    );
  }

  void RefreshEnvironmentHotCacheSlots(string source) {
    ApplyEnvironmentHotCacheSlot(
      CurrentEnvironmentPinOwnerId,
      "current",
      currentEnvironmentCacheLocationId,
      source
    );
    ApplyEnvironmentHotCacheSlot(
      PreviousEnvironmentPinOwnerId,
      "previous",
      previousEnvironmentCacheLocationId,
      source
    );

    if (!ShouldLogLoadingProgressDebug()) return;

    Debug.Log(
      "[SingleSceneManager][EnvironmentCache] stage=refresh_slots" +
      " source='" + (source ?? "") + "'" +
      " current=" + ResolveLoadFlowValue(currentEnvironmentCacheLocationId) +
      " previous=" + ResolveLoadFlowValue(previousEnvironmentCacheLocationId) +
      " slots=" + EnvironmentHotCacheSlotCount
    );
  }

  void TrackEnvironmentHotCacheLocation(string locationId, string source) {
    var normalized = LocationEnemyData.NormalizeLocationId(locationId);
    if (!IsGameplayLocation(normalized)) {
      return;
    }

    if (string.Equals(currentEnvironmentCacheLocationId, normalized, StringComparison.OrdinalIgnoreCase)) {
      RefreshEnvironmentHotCacheSlots(source + "_refresh");
      return;
    }

    if (string.Equals(previousEnvironmentCacheLocationId, normalized, StringComparison.OrdinalIgnoreCase)) {
      var displacedCurrent = currentEnvironmentCacheLocationId;
      currentEnvironmentCacheLocationId = normalized;
      previousEnvironmentCacheLocationId = displacedCurrent;
      RefreshEnvironmentHotCacheSlots(source + "_promote_previous");
      return;
    }

    previousEnvironmentCacheLocationId = currentEnvironmentCacheLocationId;
    currentEnvironmentCacheLocationId = normalized;
    RefreshEnvironmentHotCacheSlots(source + "_rotate");
  }

  string ResolveLoadingTextFontAtlasAddress() {
    if (loadingText == null) {
      return "";
    }

    var fontName = string.IsNullOrWhiteSpace(loadingText.font)
      ? ""
      : loadingText.font.Trim();
    if (string.IsNullOrWhiteSpace(fontName)) {
      return "";
    }

    return ResolveFontAtlasAddress(fontName);
  }

  static string ResolveFontAtlasAddress(string fontName) {
    var normalizedFontName = string.IsNullOrWhiteSpace(fontName) ? "" : fontName.Trim();
    if (string.IsNullOrWhiteSpace(normalizedFontName)) {
      return "";
    }

    var sourceAssetPath = "Assets/Sprites/Fonts/" + normalizedFontName + "/atlas.png";
    return ActiveContentRegistryRuntime.ResolveCoreAssetPath(sourceAssetPath);
  }

  static string ResolveEsperanzaExpressionAtlasAddress(string extension) {
    var normalizedExtension = string.IsNullOrWhiteSpace(extension) ? "" : extension.Trim();
    if (string.IsNullOrWhiteSpace(normalizedExtension)) {
      return "";
    }

    var sourceAssetPath = "Assets/Sprites/Characters/Esperanza/Expressions/Base/atlas" + normalizedExtension;
    return ActiveContentRegistryRuntime.ResolveCoreAssetPath(sourceAssetPath);
  }

  void PrimeLoadingTextRuntimeAssets(string source) {
    var atlasAddress = ResolveLoadingTextFontAtlasAddress();
    if (string.IsNullOrWhiteSpace(atlasAddress)) {
      return;
    }

    loadingTextRuntimeAddressScratch.Clear();
    loadingTextRuntimeAddressScratch.Add(atlasAddress);
    TextureResidencyCache.UpdateOwnerPins(
      LoadingTextFontPinOwnerId,
      TextureResidencyCache.PinClass.UI,
      loadingTextRuntimeAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup
    );
    TrimmedSpriteOffsetResolver.RegisterWarmupMetadataCandidate(atlasAddress);
    TrimmedSpriteOffsetResolver.QueueWarmupAtlasMetadataBatch(
      loadingTextRuntimeAddressScratch,
      0,
      loadingTextRuntimeAddressScratch.Count
    );

    if (ShouldLogLoadingProgressDebug()) {
      Debug.Log(
        "[SingleSceneManager][LoadingTextWarmup] source='" + (source ?? "") + "'" +
        " font='" + (loadingText != null ? loadingText.font : "") + "'" +
        " atlas='" + atlasAddress + "'"
      );
    }

    loadingTextRuntimeAddressScratch.Clear();
  }
}
