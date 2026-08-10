using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class SingleSceneManager {
  void OnAbilityLoadoutChangedForPersistentPins(string formName) {
    var activeForm = EsperanzaForms.GetActive();
    if (!string.Equals(formName, activeForm, StringComparison.OrdinalIgnoreCase)) return;
    RefreshPersistentPlayerBaselineAtlasPins("ability_loadout_changed");
  }

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
      RuntimeLog.Log(
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

    RuntimeLog.Log(
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
    var contentVersion = ActiveContentRegistryRuntime.ReloadVersion;
    if (persistentFontAtlasContentVersion == contentVersion &&
        persistentFontAtlasPinCount > 0 &&
        TextureResidencyCache.GetOwnerPinCount(PersistentFontAtlasPinOwnerId) >= persistentFontAtlasPinCount) {
      return;
    }

    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();

    for (var i = 0; i < PersistentFontAtlasNames.Length; i++) {
      var fontName = PersistentFontAtlasNames[i];
      if (string.IsNullOrWhiteSpace(fontName)) continue;
      AddPersistentAtlasAddress(ResolveFontAtlasAddress(fontName));
    }

    if (persistentAtlasAddressScratch.Count <= 0) {
      TextureResidencyCache.ReleaseOwnerPins(PersistentFontAtlasPinOwnerId);
      persistentFontAtlasContentVersion = contentVersion;
      persistentFontAtlasPinCount = 0;
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
    persistentFontAtlasContentVersion = contentVersion;
    persistentFontAtlasPinCount = persistentAtlasAddressScratch.Count;

    if (ShouldLogLoadingProgressDebug()) {
      RuntimeLog.Log(
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
      TextureResidencyCache.ReleaseOwnerPins(PersistentPlayerAppearanceAtlasPinOwnerId);
      persistentPlayerAppearanceAtlasAddresses.Clear();
      persistentPlayerAppearanceContentVersion = ActiveContentRegistryRuntime.ReloadVersion;
      persistentPlayerAppearancePlanEvaluated = false;
      return;
    }

    persistentPlayerAppearancePlanEvaluated = false;
    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
    var pinBudget = Math.Max(SpriteStreamingRuntimeSettings.PinBudgetPlayerAddresses, 1);
    // The full persistent plan is only useful while it fits in the owner pin
    // budget. Stop at the first address that exceeds it; otherwise a large
    // animation graph is exhaustively resolved just to take the same dynamic
    // fallback path below.
    var collectionLimit = pinBudget < int.MaxValue ? pinBudget + 1 : int.MaxValue;
    var collectedCount = player.CollectPersistentSkinStartupAddresses(
      persistentAtlasAddressScratch,
      persistentAtlasSeenAddressScratch,
      collectionLimit
    );
    if (collectedCount <= 0 || persistentAtlasAddressScratch.Count <= 0) {
      TextureResidencyCache.ReleaseOwnerPins(PersistentPlayerAppearanceAtlasPinOwnerId);
      player.SetSceneAppearanceAtlasPinsManaged(false);
      persistentPlayerAppearanceAtlasAddresses.Clear();
      persistentPlayerAppearanceContentVersion = ActiveContentRegistryRuntime.ReloadVersion;
      persistentAtlasAddressScratch.Clear();
      persistentAtlasSeenAddressScratch.Clear();
      return;
    }
    persistentPlayerAppearancePlanEvaluated = true;

    var completePlanFitsPinBudget = persistentAtlasAddressScratch.Count <= pinBudget;
    if (completePlanFitsPinBudget) {
      TextureResidencyCache.UpdateOwnerPins(
        PersistentPlayerAppearanceAtlasPinOwnerId,
        TextureResidencyCache.PinClass.Player,
        persistentAtlasAddressScratch,
        TextureResidencyCache.LoadPriority.Warmup
      );
      var persistentPinCount = TextureResidencyCache.GetOwnerPinCount(
        PersistentPlayerAppearanceAtlasPinOwnerId
      );
      var completePinCoverage = persistentPinCount >= persistentAtlasAddressScratch.Count;
      player.SetSceneAppearanceAtlasPinsManaged(
        completePinCoverage,
        PersistentPlayerAppearanceAtlasPinOwnerId,
        persistentAtlasAddressScratch.Count
      );
      if (!completePinCoverage) {
        TextureResidencyCache.ReleaseOwnerPins(PersistentPlayerAppearanceAtlasPinOwnerId);
        Debug.LogWarning(
          "[SingleSceneManager] Complete character atlas plan could not retain full pin coverage." +
          " atlases=" + persistentAtlasAddressScratch.Count +
          " pinned=" + persistentPinCount
        );
      }
      completePlanFitsPinBudget = completePinCoverage;
    }
    else {
      TextureResidencyCache.ReleaseOwnerPins(PersistentPlayerAppearanceAtlasPinOwnerId);
      player.SetSceneAppearanceAtlasPinsManaged(false);
      Debug.LogWarning(
        "[SingleSceneManager] Complete character atlas plan exceeds the player pin budget." +
        " atlases=" + persistentAtlasAddressScratch.Count +
        " budget=" + pinBudget
      );
    }
    TextureResidencyCache.RequestLoadBatch(
      persistentAtlasAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup,
      allowAtlasExpansion: false,
      warmGateManaged: SpriteStreamingLoadingState.IsLoadingOverlayActive &&
                       StreamingWarmOrchestrator.IsWarmGateRunning
    );
    persistentPlayerAppearanceAtlasAddresses.Clear();
    if (completePlanFitsPinBudget) {
      persistentPlayerAppearanceAtlasAddresses.AddRange(persistentAtlasAddressScratch);
    }
    persistentPlayerAppearanceContentVersion = ActiveContentRegistryRuntime.ReloadVersion;
    QueuePersistentAtlasMetadataWarmup(persistentAtlasAddressScratch);

    if (ShouldLogLoadingProgressDebug()) {
      RuntimeLog.Log(
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
      TextureResidencyCache.ReleaseOwnerPins(PersistentPlayerEffectAtlasPinOwnerId);
      persistentPlayerEffectAtlasAddresses.Clear();
      persistentPlayerEffectContentVersion = ActiveContentRegistryRuntime.ReloadVersion;
      persistentPlayerEffectPlanEvaluated = false;
      return;
    }

    persistentPlayerEffectPlanEvaluated = false;
    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
    var maxPinnedAddresses = Math.Max(SpriteStreamingRuntimeSettings.PinBudgetEffectAddresses, 1);
    // As with the player skin plan, only collect enough to determine whether
    // complete pin coverage is possible.
    var collectionLimit = maxPinnedAddresses < int.MaxValue ? maxPinnedAddresses + 1 : int.MaxValue;
    var collectedCount = player.CollectPersistentEffectStartupAddresses(
      persistentAtlasAddressScratch,
      persistentAtlasSeenAddressScratch,
      collectionLimit
    );
    if (collectedCount <= 0 || persistentAtlasAddressScratch.Count <= 0) {
      TextureResidencyCache.ReleaseOwnerPins(PersistentPlayerEffectAtlasPinOwnerId);
      persistentPlayerEffectAtlasAddresses.Clear();
      persistentPlayerEffectContentVersion = ActiveContentRegistryRuntime.ReloadVersion;
      persistentAtlasAddressScratch.Clear();
      persistentAtlasSeenAddressScratch.Clear();
      return;
    }
    persistentPlayerEffectPlanEvaluated = true;

    var completePlanFitsPinBudget = persistentAtlasAddressScratch.Count <= maxPinnedAddresses;
    if (completePlanFitsPinBudget) {
      TextureResidencyCache.UpdateOwnerPins(
        PersistentPlayerEffectAtlasPinOwnerId,
        TextureResidencyCache.PinClass.Effect,
        persistentAtlasAddressScratch,
        TextureResidencyCache.LoadPriority.Warmup
      );
      var persistentPinCount = TextureResidencyCache.GetOwnerPinCount(
        PersistentPlayerEffectAtlasPinOwnerId
      );
      completePlanFitsPinBudget = persistentPinCount >= persistentAtlasAddressScratch.Count;
    }
    if (!completePlanFitsPinBudget) {
      TextureResidencyCache.ReleaseOwnerPins(PersistentPlayerEffectAtlasPinOwnerId);
      Debug.LogWarning(
        "[SingleSceneManager] Complete player effect atlas plan exceeds available pin coverage." +
        " atlases=" + persistentAtlasAddressScratch.Count +
        " budget=" + maxPinnedAddresses
      );
    }
    TextureResidencyCache.RequestLoadBatch(
      persistentAtlasAddressScratch,
      TextureResidencyCache.LoadPriority.Warmup,
      allowAtlasExpansion: false,
      warmGateManaged: SpriteStreamingLoadingState.IsLoadingOverlayActive &&
                       StreamingWarmOrchestrator.IsWarmGateRunning
    );
    persistentPlayerEffectAtlasAddresses.Clear();
    if (completePlanFitsPinBudget) {
      persistentPlayerEffectAtlasAddresses.AddRange(persistentAtlasAddressScratch);
    }
    persistentPlayerEffectContentVersion = ActiveContentRegistryRuntime.ReloadVersion;
    QueuePersistentAtlasMetadataWarmup(persistentAtlasAddressScratch);

    if (ShouldLogLoadingProgressDebug()) {
      RuntimeLog.Log(
        "[SingleSceneManager][PersistentAtlasPins] source='" + (source ?? "") + "'" +
        " class=player_effects" +
        " addresses=" + persistentAtlasAddressScratch.Count +
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

    var contentVersion = ActiveContentRegistryRuntime.ReloadVersion;
    if (!TryResolveEsperanzaExpressionAtlasAddress(".png", out var atlasAddress)) {
      TextureResidencyCache.ReleaseOwnerPins(PersistentPlayerExpressionAtlasPinOwnerId);
      persistentPlayerExpressionContentVersion = contentVersion;
      persistentPlayerExpressionRefreshPending = ActiveContentRegistryRuntime.HasActiveExternalContent();
      persistentAtlasAddressScratch.Clear();
      persistentAtlasSeenAddressScratch.Clear();
      return;
    }

    AddPersistentAtlasAddress(atlasAddress);
    if (persistentAtlasAddressScratch.Count <= 0) {
      TextureResidencyCache.ReleaseOwnerPins(PersistentPlayerExpressionAtlasPinOwnerId);
      persistentPlayerExpressionContentVersion = contentVersion;
      persistentPlayerExpressionRefreshPending = false;
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
    persistentPlayerExpressionContentVersion = contentVersion;
    persistentPlayerExpressionRefreshPending = false;

    if (ShouldLogLoadingProgressDebug()) {
      RuntimeLog.Log(
        "[SingleSceneManager][PersistentAtlasPins] source='" + (source ?? "") + "'" +
        " class=player_expressions" +
        " addresses=" + persistentAtlasAddressScratch.Count
      );
    }

    persistentAtlasAddressScratch.Clear();
    persistentAtlasSeenAddressScratch.Clear();
  }

  void TickPersistentPlayerExpressionAtlasPins() {
    if (!persistentPlayerExpressionRefreshPending) return;

    var contentVersion = ActiveContentRegistryRuntime.ReloadVersion;
    if (persistentPlayerExpressionContentVersion == contentVersion) return;

    RefreshPersistentPlayerExpressionAtlasPins("content_pack_ready");
  }

  void RefreshPersistentPlayerBaselineAtlasPins(string source) {
    RefreshPersistentPlayerSkinAtlasPins(source);
    RefreshPersistentPlayerEffectAtlasPins(source);
    RefreshPersistentPlayerExpressionAtlasPins(source);
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

  static bool TryResolveEsperanzaExpressionAtlasAddress(string extension, out string address) {
    address = "";
    var normalizedExtension = string.IsNullOrWhiteSpace(extension) ? "" : extension.Trim();
    if (string.IsNullOrWhiteSpace(normalizedExtension)) {
      return false;
    }

    var activeForm = EsperanzaForms.GetActive();
    var sourceAssetPath = SpriteStreamingConfig.BuildEsperanzaExpressionAtlasSourcePath(
      activeForm,
      normalizedExtension
    );
    if (string.IsNullOrWhiteSpace(sourceAssetPath)) {
      return false;
    }

    if (!ActiveContentRegistryRuntime.HasActiveExternalContent()) {
      address = sourceAssetPath;
      return true;
    }

    var uiPackId = "UI" + activeForm;
    if (!ContentPackCatalogLoader.IsPackReady(uiPackId)) {
      return false;
    }

    return ContentPackCatalogLoader.TryResolveExportedAddress(
      sourceAssetPath,
      new[] { uiPackId },
      out address
    );
  }

  void PrimeLoadingTextRuntimeAssets(string source) {
    var atlasAddress = ResolveLoadingTextFontAtlasAddress();
    if (string.IsNullOrWhiteSpace(atlasAddress)) {
      return;
    }
    var contentVersion = ActiveContentRegistryRuntime.ReloadVersion;
    if (loadingTextFontContentVersion == contentVersion &&
        string.Equals(loadingTextFontPinnedAddress, atlasAddress, StringComparison.OrdinalIgnoreCase) &&
        TextureResidencyCache.GetOwnerPinCount(LoadingTextFontPinOwnerId) > 0) {
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
    loadingTextFontContentVersion = contentVersion;
    loadingTextFontPinnedAddress = atlasAddress;

    if (ShouldLogLoadingProgressDebug()) {
      RuntimeLog.Log(
        "[SingleSceneManager][LoadingTextWarmup] source='" + (source ?? "") + "'" +
        " font='" + (loadingText != null ? loadingText.font : "") + "'" +
        " atlas='" + atlasAddress + "'"
      );
    }

    loadingTextRuntimeAddressScratch.Clear();
  }
}
