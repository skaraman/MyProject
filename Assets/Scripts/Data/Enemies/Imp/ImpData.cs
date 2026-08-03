using System.Collections.Generic;

public static partial class ImpData {
  public const string EnemyType = "Imp";
  public const string JumpAnimation = "Jump";
  public const float JumpMaximumDistance = 10f;
  public const float JumpDurationSeconds = 1f;
  public const float JumpDurationMilliseconds = JumpDurationSeconds * 1000f;

  public static IReadOnlyList<EnemyAttackDefinition> Attacks { get; } =
    new EnemyAttackDefinition[] {
      new(
        JumpAnimation,
        minimumDistance: 0f,
        maximumDistance: JumpMaximumDistance,
        landAtTargetSnapshot: true
      )
    };

  public static DemonStatDefinition Stats { get; } = new(
    baseStats: new Dictionary<string, float> {
      ["DMG"] = 1f,
      ["HP"] = 10f,
      ["AKSP"] = 1f,
      ["HPRG"] = 0f,
      ["ARM"] = 0f,
      ["BNS"] = 0f,
      ["MVSP"] = 1f,
      ["CDST"] = JumpMaximumDistance,
      ["AREA"] = 0f,
      ["EVD"] = 0f,
      ["FEAR"] = 0f,
      ["SPEC"] = 0f,
      ["PEN"] = 1f
    },
    growthPerLevel: new Dictionary<string, float> {
      ["DMG"] = 0f,
      ["HP"] = 0f,
      ["AKSP"] = 0f,
      ["HPRG"] = 0f,
      ["ARM"] = 0f,
      ["BNS"] = 0f,
      ["MVSP"] = 0f,
      ["CDST"] = 0f,
      ["AREA"] = 0f,
      ["EVD"] = 0f,
      ["FEAR"] = 0f,
      ["SPEC"] = 0f,
      ["PEN"] = 0f
    }
  );

  public static Dictionary<string, AnimData> Animations { get; } = new() {
    ["Run"] = new AnimData {
      start = 1,
      end = 46,
      duration = 1000,
      isLocomotion = true,
      loop = true
    },
    [JumpAnimation] = new AnimData {
      start = 1,
      end = 195,
      duration = JumpDurationMilliseconds
    },
    ["Hurt"] = new AnimData {
      start = 1,
      end = 60,
      duration = 175,
      pingPongOnce = true
    },
    ["Death_Base_1"] = new AnimData {
      start = 1,
      end = 74,
      duration = 1500,
      category = "Death_Base_1"
    }
  };

  public static Dictionary<string, Dictionary<string, string>> Interrupts { get; } = new() {
    ["Run"] = new Dictionary<string, string> {
      [JumpAnimation] = JumpAnimation,
      ["Hurt"] = "Hurt",
      ["Death_Base_1"] = "Death_Base_1"
    },
    [JumpAnimation] = new Dictionary<string, string> {
      ["Hurt"] = "Hurt",
      ["Death_Base_1"] = "Death_Base_1"
    },
    ["Hurt"] = new Dictionary<string, string> {
      ["Hurt"] = "Hurt",
      ["Death_Base_1"] = "Death_Base_1"
    },
    ["Death_Base_1"] = new Dictionary<string, string>()
  };
}
