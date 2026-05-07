# TODO

## Loading Pipeline Optimization (2026-05-02)

### Immediate: Play Mode Speed
- [x] Reduce `startWarmRequiredRatio` from 0.97 → 0.10 (critical assets only)
- [x] Increase `startWarmTimeoutSeconds` from 2s → 4s (accommodate disk I/O)
- [ ] Verify heartbeat gaps ≤2s in Play mode with reduced pack set
- [ ] Confirm no pop-in after reveal with lazy loading enabled

### Content Pack Validation
- [ ] Run `Start New Game` with `Core + Form_Base + equipped Gear_* + Slice_DomeCity_Imp_Base`
- [ ] Verify gameplay unlock without hidden dependency on full project tree
- [ ] Validate load order: `player → location → enemies → ui → dialog`
- [ ] Capture Editor.log and analyze `[SingleSceneManager][LoadTiming]` for dominant blockers

### Warm Gate Tuning
- [ ] Adjust environment cache membership (hot vs cold slots)
- [ ] Validate reveal-critical assets: player first frame, BG, FG/Static, spawn enemies, UI shell, dialog shell
- [ ] Confirm non-critical sprites defer to post-reveal lazy loading

### Slice Expansion Preparation
- [x] Define next slice pack IDs (after `Slice_DomeCity_Imp_Base`)
- [x] Decide Core ownership: player prefab, base atlases, common UI, fonts, projectiles, effects
- [x] Categorize assets as `Core`, `Slice`, or `Derived from location prefab`
- [x] Define default external-build start location

### Pipeline Automation
- [x] Add next slice pack definitions to ContentPackPipeline
- [x] Export/import snapshots for new locations and dialog sets
- [x] Validate enemy stats + animations + projectile references for staged packs
- [x] Validate dialog portrait libraries resolve from `speakerId`
- [x] Stage active packs → audit → rebuild runtime index → build Addressables
- [ ] Run reduced-pack smoke test and capture logs

### Memory & Streaming Policy
- [x] Define weakest supported memory tier (handheld/mobile budget)
- [x] Define reveal-critical vs post-reveal streaming policy per slice
- [x] Decide if slices can depend on other slices or only Core
- [x] Configure environment hot-cache residency rules

## Content Migration Debt

### Ownership Resolution
- [ ] Decide whether Esperanza alternate forms stay in Core or become opt-in packs
- [ ] Decide enemy ownership: globally available vs location-owned
- [ ] Define content naming rules for episodes, locations, enemies, dialogs
- [ ] Turn `Slice_Homebase_Placeholder` into real slice
- [ ] Turn `Slice_SunkenCave_Placeholder` into real slice

### Validation Gaps
- [x] Add validation: every staged location has data, dialog, prefab path, enemy archetype
- [x] Add validation: every dialog snapshot has speaker chains with valid seen progression keys
- [x] Add validation: runtime projectile keys point to staged/core-owned prefabs
- [ ] Add automated checks: mainmenu never enters gameplay environment-cache rotation
