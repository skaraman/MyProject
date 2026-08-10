# Unity Project Context

<!-- unity-onboarding:generated:start -->

## Project Summary

- Project root: `D:\localDev\Unity\Esperanza\MyProject`
- Game shape: 2D, MonoBehaviour-centric single-player project using one composite build scene and runtime section switching.
- Content shape: runtime code and core assets live in `MyProject`; large authored content is supplied by the local `com.skaraman.myprojectcontent` package at `..\MyProjectContent` and loaded heavily through Addressables.
- Last analyzed: 2026-08-08
- Last analyzed commit: `288662b89a14fb8ba9374ab8b1a16dcf62ab0df4`

## Confirmed Environment

- Unity version: 6000.5.7f1, revision `017862109af0` (Unity 6.5).
- Render pipeline: URP 17.5.0 with the active `AllIn12DRendererUrpAsset` and its 2D renderer data.
- Input system: new Input System only (`activeInputHandler: 1`), package 1.20.0, generated `TestActions` wrapper.
- Networking: no first-party networking usage found. Multiplayer Center is installed but does not establish a multiplayer architecture.
- Target platforms: a macOS build profile exists; active Editor target and intended release-platform matrix are unknown without Editor data.

## Important Packages And Frameworks

| Area | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| Content | Local `com.skaraman.myprojectcontent` package (`file:../../MyProjectContent`) | Confirmed | `Packages/manifest.json`, `MyProjectContent/package.json` |
| Rendering | URP 17.5.0, custom AllIn1 Sprite Shader URP asset, 2D renderer | Confirmed | `ProjectSettings/GraphicsSettings.asset`, `Assets/Plugins/AllIn1SpriteShader/PipelineSetup/` |
| Input | Input System 1.20.0 with generated action wrapper and binding overrides | Confirmed | `ProjectSettings/ProjectSettings.asset`, `Assets/Scripts/Util/Input/TestActions.cs` |
| Asset delivery | Addressables 2.9.1 plus custom content catalogs, runtime residency, shard lookup, and warm gates | Confirmed | `Assets/AddressableAssetsData/`, `Assets/Scripts/Util/AssetStreaming/`, `Assets/Scripts/Util/ContentPacks/` |
| 2D stack | 2D Animation, Sprite, SpriteShape, Tilemap, PSD/Aseprite importers | Confirmed | `Packages/manifest.json` |
| Animation/physics | LeanTween and EZSoftBone vendor code; Unity 2D physics used throughout gameplay | Confirmed | `Assets/Plugins/LeanTween/`, `Assets/Plugins/EZhex1991/EZSoftBone/` |
| Profiling | Unity Profile Analyzer plus project profiler capture/export workflow | Confirmed | `Packages/manifest.json`, `README.md`, `Assets/Editor/` |

## Directory Structure

| Path | Purpose | Confidence | Evidence |
| --- | --- | --- | --- |
| `Assets/Scripts/` | First-party runtime code: data/save, input, audio, lighting, UI, scene orchestration, gameplay, and streaming | Confirmed | Directory and representative code inspection |
| `Assets/Editor/` | First-party Editor tools, content/addressable build tooling, profiler utilities | Confirmed | `Assets/Editor/myEditor.asmdef` |
| `Assets/Scenes/` | Composite production scene plus playground/holder scenes | Confirmed | `.unity` inventory and Build Settings YAML |
| `Assets/AddressableAssetsData/` | Addressables settings and groups | Confirmed | Directory contents and Build Settings config object |
| `Assets/Settings/` | URP/2D renderer assets and build profiles | Confirmed | Settings assets |
| `Assets/Plugins/`, `Assets/Shared/` | Vendor libraries and shared inspector/runtime utilities | Confirmed | Assembly definitions and package structure |
| `..\MyProjectContent/` | Large local package split into core, UI, environments, enemies, forms, gear, and animation content | Confirmed | Local package manifest and directory inventory |

`Assets/Resources/` exists for legacy/current content, but project guidance explicitly prohibits adding new Resources-based loading.

## Assembly Boundaries

| Assembly | Responsibility | Key references | Notes |
| --- | --- | --- | --- |
| `Project.Internal` | Main first-party runtime/game assembly | Input System, Addressables, URP 2D, `myShared`, `NewAssembly`, EZSoftBone | Large assembly. It also explicitly references Editor assemblies; player-build compatibility should be verified by the user-owned Unity build workflow. |
| `NewAssembly` | Plugin code under `Assets/Plugins` not captured by nested vendor asmdefs | Implicit/default | Defined by `Assets/Plugins/Project.External.asmdef`; filename and assembly name differ. |
| `myShared` | Shared runtime helpers | None | Small independent base assembly. |
| `myEditor` | First-party Editor and content-pipeline tooling | `Project.Internal`, Addressables Editor, URP Editor, test runners | Editor-only via `includePlatforms: [Editor]`. |
| Vendor assemblies | AllIn1 Sprite Shader, EZSoftBone, Custom Inspector, Editor Themes, Asset Usage Detector | Package-specific | Treat as imported/vendor unless a task directly targets them. |

## Scenes And Startup Flow

- Enabled build scene: `Assets/Scenes/MyCurrent.unity` only.
- Other scene assets: `Assets/Scenes/esperPlayground.unity`, `Assets/Scenes/emptyholder.unity`; neither is enabled in Build Settings.
- Likely startup: `SingleSceneManager.Start()` begins the main-menu reveal path. `StartupMainMenuRevealRoutine()` and `ApplyConfiguredMainMenuStartupMode()` activate the Main Menu section, its input map, and the main-menu location.
- Runtime flow: the same composite scene keeps Main Menu, Load, Settings, Gameplay, and Pause sections and switches them through `SingleSceneManager`; location/gameplay content is loaded and warmed through Addressables rather than conventional scene changes.

## Architecture

| Pattern | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| MonoBehaviour composition | Primary architecture (about 110 scripts declare MonoBehaviour types) | Confirmed | `Assets/Scripts/**/*.cs` inventory |
| Composite-scene state machine | `SingleSceneManager` owns section activation, input maps, fades, load/new-game flow, and warm/reveal gates | Confirmed | `SingleSceneManager.cs` and partial files |
| Partial-class feature splitting | Large systems are divided across many partial files (about 64 files contain partial classes) | Confirmed | Script inventory |
| Event bus | Static `MessageBus` supports string and typed topics; callers retain unsubscribe actions | Confirmed | `Assets/Scripts/Util/Scripting/MessageBus.cs` |
| Runtime streaming layer | Addressables-backed catalogs, resolver shards, residency caches, pooling, and warm orchestration | Confirmed | `Assets/Scripts/Util/AssetStreaming/` |
| ScriptableObject configuration | Used selectively for active content and sprite streaming configuration, not as the main gameplay architecture | Confirmed | Three `CreateAssetMenu` types under `Assets/Scripts/Util/` |
| Persistence | Custom binary primitive-key dictionary with reflection-based complex-object flattening | Confirmed | `Assets/Scripts/Data/Save/SaveLoad.cs` |
| Async model | Coroutines and Addressables handles dominate; a small amount of `Task`/async-enumerable code exists | Confirmed | scene/streaming scripts, `AsyncCoroutine.cs` |

## Coding Conventions

- Most first-party code uses the global namespace, two-space indentation, and opening braces on the declaration line.
- Types/methods/properties use PascalCase; fields usually use camelCase. Existing visibility is mixed: public scene references coexist with `[SerializeField]` private/package-private fields.
- Large cohesive classes are commonly split into `Type.Feature.cs` partial files; inspect all siblings before editing shared state.
- Modern C# features in active use include target-typed `new`, `using var`, and null-coalescing assignment.
- Performance-sensitive paths include explicit caching, pooling, warm gates, and diagnostic toggles; preserve their lifecycle and release contracts.

## Testing And Validation

- Unity Test Framework 1.7.0 is installed.
- No first-party EditMode or PlayMode test assembly was found. `myEditor` references test-runner assemblies, and the embedded Profile Analyzer package contains vendor Editor tests.
- Project instructions reserve Unity test runs, builds, and `.NET build` checks for the user. No compilation, tests, Play Mode, build, reimport, or runtime validation was performed during onboarding.
- Performance work is evidence-driven through project profiler captures/export tools; static inspection must not be represented as performance confirmation.

## Available Unity Tooling

| Capability | Status | Evidence |
| --- | --- | --- |
| Repository/configuration inspection | Available | Workspace filesystem and source control |
| Unity Editor/MCP connection, Console, scene inspection, Build Settings API, tests, Play Mode, profiler | Unavailable in this session | No Unity MCP tools or project MCP client configuration detected |

Unity work can continue from repository evidence. Editor-only facts must remain unverified unless the user later connects a Unity MCP/provider or supplies Editor output.

## Important Constraints

- Preserve the large pre-existing dirty worktree and keep future diffs narrowly scoped.
- Do not add new `Resources`-based loading.
- Do not run Unity tests/builds or `.NET build`; the user owns those validation passes.
- Treat `Library/`, `Temp/`, `Logs/`, `obj/`, UserSettings, and build output as generated/local state unless a task explicitly targets them.
- Content and runtime code form a cross-repository contract with `MyProjectContent`; inspect both sides before changing addresses, catalogs, sprite metadata, or manifests.
- Addressables handles, pooled instances, and residency pins have explicit release/cleanup lifecycles; do not bypass them casually.

## Unknowns And Risks

- Current Unity Console/compiler state and runtime behavior are unknown because the Editor was not queried.
- The active Editor build target and supported release-platform matrix are unknown; only a serialized macOS build profile was found.
- `Project.Internal` explicitly references `Unity.2D.Animation.Editor` and `UnityEditor.UI` despite being a non-Editor assembly. This is a configuration risk to check during a user-owned player build, not a confirmed failure.
- There is no first-party automated-test baseline evident from assembly definitions.
- The active scene and several core assets/scripts were already modified before onboarding; do not assume repository `HEAD` represents current authored behavior.

## Source Files Inspected

- `AGENTS.md`, `README.md`
- `ProjectSettings/ProjectVersion.txt`, `ProjectSettings/ProjectSettings.asset`, `ProjectSettings/GraphicsSettings.asset`, `ProjectSettings/QualitySettings.asset`, `ProjectSettings/EditorBuildSettings.asset`
- `Packages/manifest.json`, `Packages/packages-lock.json`, `..\MyProjectContent\package.json`
- `Assets/Settings/UniversalRP.asset`, `Assets/Settings/Renderer2D.asset`, `Assets/Settings/Build Profiles/macOS.asset`
- `Assets/Plugins/AllIn1SpriteShader/PipelineSetup/AllIn12DRendererUrpAsset.asset`
- First-party `.asmdef` files under `Assets/Scripts`, `Assets/Editor`, `Assets/Plugins`, and `Assets/Shared`
- `Assets/Scripts/SceneManager/SingleSceneManager.cs` and representative partial files
- `Assets/Scripts/Input/GameplayInput.cs`, `Assets/Scripts/Util/Input/TestActions.cs`
- `Assets/Scripts/Util/Scripting/MessageBus.cs`, `Assets/Scripts/Data/Save/SaveLoad.cs`
- Representative files and inventories under `Assets/Scripts/Util/AssetStreaming/` and `Assets/Scripts/Util/ContentPacks/`

<!-- unity-onboarding:generated:end -->
