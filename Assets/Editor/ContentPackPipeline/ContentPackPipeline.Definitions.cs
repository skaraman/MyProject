#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

public static partial class ContentPackPipeline {
  public const string DefaultExternalRoot = @"d:\localDev\Unity\MyProjectContent";
  public const string CorePackId = "Core";
  public const string BaseFormPackId = "Form_Base";
  public const string SlicePackId = "Slice_DomeCity_Imp_Base";
  public const string HomebaseSlicePackId = "Slice_Homebase_Placeholder";
  public const string SunkenCaveSlicePackId = "Slice_SunkenCave_Placeholder";
  public const string EpisodePackId = "Episode_01";
  public const string LegacySlicePackId = "Slice_DomeCity_Imp";

  public const string SelectionAssetPath = "Assets/Editor/ContentPackSelection.asset";
  public const string ActiveRegistryAssetPath = "Assets/Resources/ActiveContentRegistry.asset";
  public const string StageRootAssetPath = "Assets/ContentStage";
  public const string StageCoreAssetPath = "Assets/ContentStage/Core";
  public const string StageFormsAssetPath = "Assets/ContentStage/Forms";
  public const string StageGearsAssetPath = "Assets/ContentStage/Gears";
  public const string StageSlicesAssetPath = "Assets/ContentStage/Slices";
  const string EsperanzaGroupedGearRoot = "Assets/Sprites/Characters/Esperanza/GroupedGearAtlases";

  const string ManifestFileName = "ContentPackManifest.json";
  const string PackDataFolderName = "_PackData";
  const string EsperanzaSnapshotFileName = "esperanza_base_snapshot.json";
  const string DomeCityLocationSnapshotFileName = "location_DomeCity.json";
  const string DomeCityDialogSnapshotFileName = "dialog_DomeCity.json";

  static readonly Regex GuidRegex = new(@"guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);
  static readonly Regex LibraryNameRegex = new(@"^\s*libraryName:\s*(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
  static readonly Regex PortraitLibraryNameRegex = new(@"^\s*portraitLibraryName:\s*(.*?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
  static readonly Regex LocalIncludeRegex = new(@"^\s*#include(?:_with_pragmas)?\s+""([^""]+)""", RegexOptions.Compiled | RegexOptions.Multiline);

  static readonly HashSet<string> IgnoredDependencyExtensions = new(StringComparer.OrdinalIgnoreCase) {
    ".cs",
    ".asmdef",
    ".asmref",
    ".dll",
    ".rsp",
    ".mdb",
    ".pdb"
  };

  static readonly HashSet<string> TextRewriteExtensions = new(StringComparer.OrdinalIgnoreCase) {
    ".asset",
    ".anim",
    ".controller",
    ".guiskin",
    ".inputactions",
    ".json",
    ".mask",
    ".mat",
    ".meta",
    ".overridecontroller",
    ".playable",
    ".prefab",
    ".shadergraph",
    ".shadersubgraph",
    ".spritelib",
    ".txt",
    ".unity",
    ".uss",
    ".uxml"
  };

  static readonly HashSet<string> LocalIncludeExtensions = new(StringComparer.OrdinalIgnoreCase) {
    ".cginc",
    ".compute",
    ".hlsl",
    ".shader"
  };

  [Serializable]
  sealed class ContentPackManifestJson {
    public string packId;
    public string kind;
    public List<string> dependencies = new();
    public List<string> ownedRoots = new();
    public List<string> ownedLocations = new();
    public List<string> ownedEnemyTypes = new();
    public List<string> dialogIds = new();
    public string exportedFromProject;
    public string sourceRevision;
  }

  [Serializable]
  sealed class ExportedLocationJson {
    public string locationId;
    public string name;
    public List<string> enemies = new();
    public int maxEnemies;
    public float spawnInterval;
    public string prefabAssetPath;
    public Vector3 localPosition;
    public Vector3 localEulerAngles;
    public Vector3 localScale = Vector3.one;
    public List<ExportedLocationObjectiveJson> objectives = new();
  }

  [Serializable]
  sealed class ExportedLocationObjectiveJson {
    public int type;
    public string description;
    public int targetCount;
    public float targetSeconds;
  }

  [Serializable]
  sealed class ExportedDialogJson {
    public string locationId;
    public List<ExportedDialogSpeakerJson> speakers = new();
  }

  [Serializable]
  sealed class ExportedDialogSpeakerJson {
    public string speakerId;
    public string speakerName;
    public string portraitLibraryName;
    public int speakerSide;
    public List<ExportedDialogLineJson> lines = new();
  }

  [Serializable]
  sealed class ExportedDialogLineJson {
    public int lineNumber;
    public string text;
    public string emotion;
    public string trigger;
    public string speakerId;
    public string speakerName;
    public int speaker;
    public string avatarForm;
    public int otherType;
    public string portraitLibraryName;
    public string locationId;
  }

  [Serializable]
  sealed class ExportedEsperanzaSnapshotJson {
    public string generatedAtUtc;
    public List<ExportedSourceFileJson> sourceFiles = new();
  }

  [Serializable]
  sealed class ExportedSourceFileJson {
    public string assetPath;
    public string sha256;
    public string text;
  }

  sealed class PackDefinition {
    public string packId;
    public string kind;
    public string externalRootPath;
    public string stageAssetRoot;
    public bool stageForRuntime = true;
    public List<string> seedRoots = new();
    public List<string> manualLibraryNames = new();
    public List<string> assetDependencies = new();
    public List<string> requiredPackIds = new();
    public List<string> ownedRoots = new();
    public List<string> ownedLocations = new();
    public List<string> ownedEnemyTypes = new();
    public List<string> dialogIds = new();
    public string defaultLocationId = "";
    public string snapshotRelativePath = "";
    public string dialogSnapshotRelativePath = "";
  }

  sealed class AssignedAsset {
    public string assetPath;
    public string originalGuid;
    public string newGuid;
    public string packId;
    public string externalAssetPath;
    public string stageAssetPath;
  }

  public enum TransitionPipelineMode {
    Smart = 0,
    Clean = 1
  }

  sealed class ExportSyncStats {
    public int packDirectoriesCreated;
    public int packDirectoriesRecreated;
    public int assetPayloadsWritten;
    public int assetPayloadsSkipped;
    public int metaPayloadsWritten;
    public int metaPayloadsSkipped;
    public int generatedFilesWritten;
    public int manifestsWritten;
  }

  sealed class OwnershipAnalysisReport {
    public string authoritativeExternalRoot;
    public int legacyGeneratedReferenceCount;
    public int spriteDuplicateCount;
    public int ownershipViolationCount;
    public int placeholderExemptionCount;
    public int stagedProjectTreeDependencyCount;
    public int stagedCodeDependencyCount;
    public readonly List<string> coreFindings = new();
    public readonly List<string> formFindings = new();
    public readonly List<string> gearFindings = new();
    public readonly List<string> sliceFindings = new();
    public readonly List<string> episodeFindings = new();
    public readonly List<string> legacyFindings = new();
    public readonly List<string> unknownFindings = new();
    public readonly List<string> placeholderFindings = new();
    public readonly List<string> stagedDependencyLeaks = new();
    public readonly List<string> stagedCodeDependencies = new();
  }

  sealed class TransitionRunSummary {
    public readonly TransitionPipelineMode mode;
    public readonly ExportSyncStats export = new();
    public OwnershipAnalysisReport analysis;
    public bool stageCompleted;
    public bool auditCompleted;
    public bool runtimeIndexCompleted;
    public bool addressablesCompleted;
    public bool unifiedImportCompleted;
    public bool hotsetCompleted;

    public TransitionRunSummary(TransitionPipelineMode mode) {
      this.mode = mode;
    }
  }
}
#endif
