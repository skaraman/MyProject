using System;
using System.Collections.Generic;
using UnityEngine;

public partial class GearController {
  public void LoadGear(bool publishReady = true) {
    EnsureRuntimeInitialized("load_gear");
    EquippedItems.EnsureKnownForms();
    GetSavedGearState();
    RuntimeContentPackResolver.ConfigureForCurrentRuntimeState("load_gear");
    RefreshGear();
    QueueStartupAppearanceWarmup("load_gear", pauseUntilReady: !equippedStartupWarmupCompleted);
    PrimeEquippedAnimationStartsIfLoading();
    PrimeCoreCombatEffectWarmup("load_gear");
    if (publishReady) {
      MessageBus.Send(CharacterMessageTopics.GearReady, (string)null);
    }
  }

  public void GetSavedGearState() {
    EquippedItems.EnsureKnownForms();
    var loaded = SaveSlotManager.Load(SaveKeys.EquippedGear);
    if (loaded == null || !loaded.HasPrefix(SaveKeys.AllGear)) return;

    var loadedForms = loaded.GetComplex<Dictionary<string, Dictionary<string, GearItem>>>(SaveKeys.AllGear);
    EquippedItems.ApplySavedGearForms(EquippedItems.AllGearForms, loadedForms);
  }

  public void SetGearIntoSlot(string slot, GearItem gearItem) {
    EquippedItems.EnsureForm(EsperanzaForms.GetActive());
    EquippedItems.AllGearForms[EsperanzaForms.GetActive()][slot] = gearItem;
    gameData.SetComplex(SaveKeys.AllGear, EquippedItems.AllGearForms);
    SaveSlotManager.Save(SaveKeys.EquippedGear, gameData);
    RuntimeContentPackResolver.ConfigureForCurrentRuntimeState("gear_slot_change");
    MarkAppearanceRevision();
  }

  public void RefreshGear() {
    UnequipGear();
    EquipGear();
    MarkAppearanceRevision();
  }

  public void UnequipGear() {
    StopEquipWarmupQueue();
    ReleaseRuntimeAppearancePinsForGearSwap();
    OffloadUnequippedGearResidency();
    if (GearObjects != null) {
      foreach (GameObject go in GearObjects) {
        var sn = go.GetComponent<SpriteWithNormals>();
        if (sn != null) {
          sn.SetLabelPrefix("");
          sn.SetDoNotRender(true);
        }
      }
    }
    foreach (GameObject bounceParent in combinedBounces ?? Array.Empty<GameObject>()) {
      if (bounceParent == null) continue;
      foreach (Transform child in bounceParent.transform) {
        child.gameObject.SetActive(false);
      }
    }
    animationController?.InvalidateSpriteFrameCache();
    if (effectControllerInitialized) {
      effectAnimationController?.InvalidateSpriteFrameCache();
    }
  }

  void ReleaseRuntimeAppearancePinsForGearSwap() {
    if (!Application.isPlaying) return;
    animationController?.ReleaseAppearancePins();
    if (!effectControllerInitialized) return;
    effectAnimationController?.ReleaseAppearancePins();
  }

  void OffloadUnequippedGearResidency() {
    if (!Application.isPlaying) return;
    // Gear swap lifecycle: old gear sprites are no longer pinned after release.
    // Evict unpinned completed entries now so old gear residency does not linger.
    TextureResidencyCache.EvictAllUnpinnedCompleted();
  }

  public void EquipGear() {
    var activeForm = EsperanzaForms.GetActive();
    EquippedItems.EnsureForm(activeForm);
    var equippedItems = EquippedItems.AllGearForms;
    equipPartPrefixScratch.Clear();
    foreach (KeyValuePair<string, GearItem> equip in equippedItems[activeForm]) {
      if (equip.Value == null && equip.Key != "Head") continue;
      var gearId = "";
      if (equip.Value == null && equip.Key == "Head") gearId = activeForm + "_no_Head";
      else gearId = $"{equip.Value.gearId}_{equip.Key}";
      if (!EsperanzaGearParts.gearParts.TryGetValue(gearId, out var parts) || parts == null) {
        Debug.LogError($"No parts found for equipped gearId: {gearId}");
        continue;
      }
      var formId = equip.Value != null ? equip.Value.gearId : "";
      for (var partIndex = 0; partIndex < parts.Count; partIndex++) {
        var partName = parts[partIndex];
        if (string.IsNullOrWhiteSpace(partName)) continue;
        equipPartPrefixScratch[partName] = formId;
      }
      if (GearObjects != null) {
        foreach (GameObject go in GearObjects) {
          foreach (string part in parts) {
            if (go != null && go.name.Equals(part)) {
              var spriteWithNormals = go.GetComponent<SpriteWithNormals>();
              var shaderAnimator = go.GetComponent<AllIn1AnimatorInspector>();
              if (spriteWithNormals != null) {
                spriteWithNormals.SetDoNotRender(false);
                spriteWithNormals.SetIsAnimation(true);
                spriteWithNormals.SetLabelPrefix(formId);
              }
              else Debug.LogWarning($"GameObject {go.name} does not have a SpriteWithNormals component attached.");
              if (shaderAnimator != null && equip.Value != null) {
                shaderAnimator.ResetActive();
                shaderAnimator.Reset();
                var newColor = ShaderColors.myColors[equip.Value.gearColor];
                shaderAnimator.SetKeyword("GLOW_ON", true);
                shaderAnimator.AddFloatSequence("_Glow", 4f, 4f, 1f, replaceExisting: true);
                shaderAnimator.AddColorSequence("_GlowColor", newColor, newColor, 1f, replaceExisting: true);
                shaderAnimator.AddColorSequence("_Color", newColor, newColor, 1f, replaceExisting: true);
              }
              else if (shaderAnimator == null) {
                Debug.LogWarning($"GameObject {go.name} does not have a AllIn1AnimatorInspector component attached.");
              }
            }
          }
        }
      }
      if (combinedBounces != null) {
        foreach (GameObject bounceParent in combinedBounces) {
          if (bounceParent == null) continue;
          foreach (Transform child in bounceParent.transform) {
            if (child != null && child.gameObject.name.Equals(gearId)) {
              child.gameObject.SetActive(true);
              var spriteRenderer = child.gameObject.GetComponent<SpriteRenderer>();
              var shaderAnimator = child.gameObject.GetComponent<AllIn1AnimatorInspector>();
              if (shaderAnimator != null && (equip.Value != null || gearId == activeForm + "_no_Head")) {
                var gearColor = "";
                shaderAnimator.ResetActive();
                shaderAnimator.Reset();
                if (gearId == activeForm + "_no_Head") {
                  gearColor = ShaderColors.pairs[activeForm]["primary"]["color"];
                  HairSkin.GetComponent<SpriteRenderer>().color = ShaderColors.myColors[gearColor];
                }
                else {
                  gearColor = equip.Value.gearColor;
                }
                var newColor = ShaderColors.myColors[gearColor];
                shaderAnimator.SetKeyword("GLOW_ON", true);
                shaderAnimator.AddFloatSequence("_Glow", 6f, 6f, 1f, replaceExisting: true);
                shaderAnimator.AddColorSequence("_GlowColor", newColor, newColor, 1f, replaceExisting: true);
                shaderAnimator.AddColorSequence("_Color", newColor, newColor, 1f, replaceExisting: true);
                spriteRenderer.color = newColor;
              }
            }
          }
        }
      }
    }

    animationController?.InvalidateSpriteFrameCache();
    if (effectControllerInitialized) {
      effectAnimationController?.InvalidateSpriteFrameCache();
    }
    QueueWarmupForEquippedCharacter(new Dictionary<string, string>(equipPartPrefixScratch, StringComparer.OrdinalIgnoreCase));
  }

  private void NormalizeSkinSpriteDefaultsForRuntime() {
    if (!Application.isPlaying) return;
    var bodyMaterial = ResolveEsperanzaBodyMaterial();
    var fixedSkinMaterials = NormalizeSpriteDefaultsForRuntime(SkinObjects, bodyMaterial, EsperanzaBodyMaterialName);
    if (fixedSkinMaterials > 0 && ShouldLogRuntimeInitDebug()) {
      Debug.Log(
        "[GearController] Normalized skin renderer materials" +
        " object=" + gameObject.name +
        " fixed=" + fixedSkinMaterials +
        " material=" + (bodyMaterial != null ? bodyMaterial.name : "null") +
        " shader=" + ResolveShaderName(bodyMaterial)
      );
    }

    var gearMaterial = ResolveEsperanzaGearMaterial();
    var fixedGearMaterials = NormalizeSpriteDefaultsForRuntime(GearObjects, gearMaterial, EsperanzaGearMaterialName);
    if (fixedGearMaterials > 0 && ShouldLogRuntimeInitDebug()) {
      Debug.Log(
        "[GearController] Normalized gear renderer materials" +
        " object=" + gameObject.name +
        " fixed=" + fixedGearMaterials +
        " material=" + (gearMaterial != null ? gearMaterial.name : "null") +
        " shader=" + ResolveShaderName(gearMaterial)
      );
    }

    var hairMaterial = ResolveEsperanzaHairMaterial();
    var fixedHairMaterials = NormalizeSpriteDefaultsForRuntime(HairObjects, hairMaterial, EsperanzaHairMaterialName);
    if (fixedHairMaterials > 0 && ShouldLogRuntimeInitDebug()) {
      Debug.Log(
        "[GearController] Normalized hair renderer materials" +
        " object=" + gameObject.name +
        " fixed=" + fixedHairMaterials +
        " material=" + (hairMaterial != null ? hairMaterial.name : "null") +
        " shader=" + ResolveShaderName(hairMaterial)
      );
    }

    if (HairSkin != null && hairMaterial != null) {
      if (NormalizeRendererMaterialForRuntime(HairSkin, hairMaterial, EsperanzaHairMaterialName)) {
        if (ShouldLogRuntimeInitDebug()) {
          Debug.Log(
            "[GearController] Normalized HairSkin material" +
            " object=" + gameObject.name +
            " material=" + hairMaterial.name +
            " shader=" + ResolveShaderName(hairMaterial)
          );
        }
      }
    }
  }

  int NormalizeSpriteDefaultsForRuntime(GameObject[] objects, Material targetMaterial, string expectedMaterialName) {
    if (objects == null || objects.Length == 0) return 0;

    var fixedMaterials = 0;
    var scratchList = new List<SpriteRenderer>();
    for (var i = 0; i < objects.Length; i++) {
      var root = objects[i];
      if (root == null) continue;

      scratchList.Clear();
      root.GetComponentsInChildren(true, scratchList);
      for (var j = 0; j < scratchList.Count; j++) {
        var sr = scratchList[j];
        if (sr == null) continue;
        var go = sr.gameObject;
        var sn = go.GetComponent<SpriteWithNormals>();
        if (targetMaterial != null && NormalizeRendererMaterialForRuntime(go, targetMaterial, expectedMaterialName)) {
          fixedMaterials++;
        }

        if (sn != null) {
          sn.SetIsAnimation(true);
        }
      }
    }

    return fixedMaterials;
  }

  static bool IsDefaultMaterial(Material material) {
    if (material == null) return true;
    var name = material.name;
    if (string.IsNullOrWhiteSpace(name)) return true;
    var normalized = name.Trim();
    if (normalized.EndsWith(" (Instance)", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(0, normalized.Length - " (Instance)".Length).TrimEnd();
    }
    return string.Equals(normalized, "Sprites-Default", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(normalized, "Mesh2D-Lit-Default", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(normalized, "Sprite-Lit-Default", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(normalized, "Default-Material", StringComparison.OrdinalIgnoreCase);
  }

  bool NormalizeRendererMaterialForRuntime(GameObject go, Material targetMaterial, string expectedMaterialName) {
    if (go == null || targetMaterial == null) return false;
    var spriteRenderer = go.GetComponent<SpriteRenderer>();
    if (spriteRenderer == null) return false;
    if (IsExpectedMaterial(spriteRenderer.sharedMaterial, expectedMaterialName)) return false;
    if (!IsDefaultMaterial(spriteRenderer.sharedMaterial)) return false;

    var shaderAnimator = go.GetComponent<AllIn1AnimatorInspector>();
    if (shaderAnimator != null) {
      shaderAnimator.UseMaterialBase(targetMaterial);
    }
    else {
      spriteRenderer.sharedMaterial = targetMaterial;
    }

    return true;
  }

  Material ResolveEsperanzaGearMaterial() {
    if (IsExpectedGearMaterial(esperanzaGearMaterial)) {
      return esperanzaGearMaterial;
    }

    var assignedMat = FindAssignedMaterialFromObjects(GearObjects, EsperanzaGearMaterialName);
    if (assignedMat != null) {
      esperanzaGearMaterial = assignedMat;
      return esperanzaGearMaterial;
    }

    var activeAddress = ActiveContentRegistryRuntime.ResolveActiveContentAssetPath(GameplayCoreAssetPaths.EsperanzaGearMaterialAssetPath);
    if (!runtimeGearMaterialWarningLogged) {
      runtimeGearMaterialWarningLogged = true;
      Debug.LogWarning(
        "[GearController] Esperanza gear material unresolved" +
        " object=" + gameObject.name +
        " active_address='" + activeAddress + "'" +
        " fallback_address='" + GameplayCoreAssetPaths.EsperanzaGearMaterialAssetPath + "'"
      );
    }

    return null;
  }

  Material ResolveEsperanzaHairMaterial() {
    if (IsExpectedHairMaterial(esperanzaHairMaterial)) {
      return esperanzaHairMaterial;
    }

    var assignedMat = FindAssignedHairMaterial();
    if (assignedMat != null) {
      esperanzaHairMaterial = assignedMat;
      return esperanzaHairMaterial;
    }

    var activeAddress = ActiveContentRegistryRuntime.ResolveActiveContentAssetPath(GameplayCoreAssetPaths.EsperanzaHairMaterialAssetPath);
    if (!runtimeHairMaterialWarningLogged) {
      runtimeHairMaterialWarningLogged = true;
      Debug.LogWarning(
        "[GearController] Esperanza hair material unresolved" +
        " object=" + gameObject.name +
        " active_address='" + activeAddress + "'" +
        " fallback_address='" + GameplayCoreAssetPaths.EsperanzaHairMaterialAssetPath + "'"
      );
    }

    return null;
  }

  Material FindAssignedMaterialFromObjects(GameObject[] objects, string expectedMaterialName) {
    if (objects == null || objects.Length == 0) return null;
    var scratchList = new List<SpriteRenderer>();
    for (var i = 0; i < objects.Length; i++) {
      var root = objects[i];
      if (root == null) continue;
      scratchList.Clear();
      root.GetComponentsInChildren(true, scratchList);
      for (var j = 0; j < scratchList.Count; j++) {
        var sr = scratchList[j];
        if (sr == null) continue;
        var mat = sr.sharedMaterial;
        if (IsExpectedMaterial(mat, expectedMaterialName)) {
          return mat;
        }
      }
    }
    return null;
  }

  Material FindAssignedHairMaterial() {
    var mat = FindAssignedMaterialFromObjects(HairObjects, EsperanzaHairMaterialName);
    if (mat != null) return mat;
    if (HairSkin != null) {
      var sr = HairSkin.GetComponent<SpriteRenderer>();
      if (sr != null && IsExpectedMaterial(sr.sharedMaterial, EsperanzaHairMaterialName)) {
        return sr.sharedMaterial;
      }
    }
    return null;
  }

  Material ResolveEsperanzaBodyMaterial() {
    if (IsExpectedBodyMaterial(esperanzaBodyMaterial)) {
      return esperanzaBodyMaterial;
    }

    var assignedMat = FindAssignedMaterialFromObjects(SkinObjects, EsperanzaBodyMaterialName);
    if (assignedMat != null) {
      esperanzaBodyMaterial = assignedMat;
      return esperanzaBodyMaterial;
    }

    var activeAddress = ActiveContentRegistryRuntime.ResolveActiveContentAssetPath(GameplayCoreAssetPaths.EsperanzaBodyMaterialAssetPath);
    if (!runtimeBodyMaterialWarningLogged) {
      runtimeBodyMaterialWarningLogged = true;
      Debug.LogWarning(
        "[GearController] Esperanza body material unresolved" +
        " object=" + gameObject.name +
        " active_address='" + activeAddress + "'" +
        " fallback_address='" + GameplayCoreAssetPaths.EsperanzaBodyMaterialAssetPath + "'"
      );
    }

    return null;
  }

  bool IsExpectedBodyMaterial(Material material) {
    return IsExpectedMaterial(material, EsperanzaBodyMaterialName);
  }

  bool IsExpectedGearMaterial(Material material) {
    return IsExpectedMaterial(material, EsperanzaGearMaterialName);
  }

  bool IsExpectedHairMaterial(Material material) {
    return IsExpectedMaterial(material, EsperanzaHairMaterialName);
  }

  bool IsExpectedMaterial(Material material, string expectedName) {
    if (material == null) return false;
    if (string.IsNullOrWhiteSpace(expectedName)) return false;
    var materialName = material.name;
    if (string.IsNullOrWhiteSpace(materialName)) return false;
    var normalized = materialName.Trim();
    if (normalized.EndsWith(" (Instance)", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(0, normalized.Length - " (Instance)".Length).TrimEnd();
    }
    if (!string.Equals(normalized, expectedName, StringComparison.OrdinalIgnoreCase)) return false;
    var shader = material.shader;
    return shader != null && string.Equals(shader.name, EsperanzaGearShaderName, StringComparison.Ordinal);
  }

  static string ResolveShaderName(Material material) {
    if (material == null || material.shader == null) return "null";
    return material.shader.name;
  }

  void ReleaseRuntimeGearMaterialHandle() { }
}
