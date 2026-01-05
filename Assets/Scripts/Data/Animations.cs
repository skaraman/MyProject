
using System.Collections.Generic;

public class AnimData {
  public int start;
  public int end;
  public float duration;
  public bool loop;
  public int To;
  public bool pingPong;
  public string effect;
  public int effectFrame;
  public string projectile;
  public int projectileFrame;
}

public static class Animations {

  public static Dictionary<string, AnimData> Imp { get; } = new Dictionary<string, AnimData> {
    { "Idle", new AnimData { start = 1, end = 46, duration = 1200, loop = true } },
    { "Run", new AnimData { start = 1, end = 46, duration = 1000, loop = true } },
    { "Attack", new AnimData { start = 1, end = 32, duration = 600 } },
    { "Jump", new AnimData { start = 2, end = 196, duration = 1750 } },
    { "Hurt", new AnimData { start = 1, end = 60, duration = 175 } },
    { "Death", new AnimData { start = 1, end = 74, duration = 1500 } }
  };

  public static Dictionary<string, AnimData> LesserDevil { get; } = new Dictionary<string, AnimData> {
    { "Idle", new AnimData { start = 1, end = 40, duration = 1200, loop = true } },
    { "Run", new AnimData { start = 1, end = 30, duration = 850, loop = true } },
    { "Attack", new AnimData { start = 1, end = 28, duration = 650 } },
    { "Jump", new AnimData { start = 1, end = 40, duration = 1200 } },
    { "Hurt", new AnimData { start = 1, end = 24, duration = 400 } },
    { "Death", new AnimData { start = 1, end = 52, duration = 1600 } }
  };


  public static Dictionary<string, Dictionary<string, AnimData>> Enemies { get; } = new Dictionary<string, Dictionary<string, AnimData>> {
    { "Imp", Imp },
    { "LesserDevil", LesserDevil }
  };

  public static Dictionary<string, AnimData> Esperanza { get; } = new Dictionary<string, AnimData> {
    { "Breathe", new AnimData { start = 1, end = 92, duration = 1750, pingPong = true } },
    { "Walk", new AnimData { start = 1, end = 65, duration = 1000, loop = true } },
    { "Run", new AnimData { start = 1, end = 45, duration = 700, loop = true } },
    { "Sprint", new AnimData { start = 1, end = 49, duration = 500, loop = true } },
    { "Dance", new AnimData { start = 1, end = 480, duration = 6000, loop = true } },
    { "Block", new AnimData { start = 1, end = 42, duration = 500 } },
    { "Dodge", new AnimData { start = 1, end = 58, duration = 250 } },
    { "Stance", new AnimData { start = 1, end = 59, duration = 1000, pingPong = true } },


    { "KickLeft", new AnimData { start = 1, end = 53, duration = 500 } },
    { "KickRight", new AnimData { start = 1, end = 31, duration = 300 } },
    { "PunchLeft", new AnimData { start = 1, end = 19, duration = 150 } },
    { "PunchRight", new AnimData { start = 1, end = 19, duration = 120 } },
    { "SuperBlast", new AnimData { start = 1, end = 60, duration = 1000, effect = "SuperBlast", projectile = "SuperBlastBall", effectFrame = 1, projectileFrame = 60 } },

    { "SuperBlastToKickLeft", new AnimData { start = 222, end = 238, duration = 175, To = 2 } },
    { "SuperBlastToKickRight", new AnimData { start = 205, end = 221, duration = 175, To = 2 } },
    { "SuperBlastToPunchLeft", new AnimData { start = 171, end = 187, duration = 175, To = 2 } },
    { "SuperBlastToPunchRight", new AnimData { start = 188, end = 204, duration = 175, To = 2 } },
    { "SuperBlastToStance", new AnimData { start = 154, end = 170, duration = 175, To = 2 } },

    { "DanceToSuperBlast", new AnimData { start = 137, end = 153, duration = 175, To = 2 } },
    { "PunchLeftToSuperBlast", new AnimData { start = 69, end = 85, duration = 175, To = 2 } },
    { "PunchRightToSuperBlast", new AnimData { start = 86, end = 102, duration = 175, To = 2 } },
    { "KickLeftToSuperBlast", new AnimData { start = 120, end = 136, duration = 175, To = 2 } },
    { "KickRightToSuperBlast", new AnimData { start = 103, end = 119, duration = 175, To = 2 } },
    { "RunToSuperBlast", new AnimData { start = 52, end = 68, duration = 175, To = 2 } },
    { "SprintToSuperBlast", new AnimData { start = 35, end = 51, duration = 175, To = 2 } },
    { "StanceToSuperBlast", new AnimData { start = 18, end = 34, duration = 175, To = 2 } },
    { "WalkToSuperBlast", new AnimData { start = 1, end = 17, duration = 175, To = 2 } },

    { "BlockToStance", new AnimData { start = 1, end = 13, duration = 175, To = 1 } },

    { "BreatheToBlock", new AnimData { start = 14, end = 25, duration = 175, To = 1 } },
    { "BreatheToDance", new AnimData { start = 26, end = 37, duration = 175, To = 1 } },
    { "BreatheToDodge", new AnimData { start = 38, end = 49, duration = 175, To = 1 } },
    { "BreatheToJump", new AnimData { start = 50,  end = 61, duration = 175, To = 1 } },
    { "BreatheToKickLeft", new AnimData { start = 62, end = 73, duration = 175, To = 1 } },
    { "BreatheToKickRight", new AnimData { start = 74, end = 85, duration = 175, To = 1 } },
    { "BreatheToPunchLeft", new AnimData { start = 86, end = 97, duration = 175, To = 1 } },
    { "BreatheToPunchRight", new AnimData { start = 101, end = 109, duration = 175, To = 1 } },
    { "BreatheToRun", new AnimData { start = 110, end = 121, duration = 175, To = 1 } },
    { "BreatheToSprint", new AnimData { start = 122, end = 133, duration = 175, To = 1 } },
    { "BreatheToWalk", new AnimData { start = 134, end = 144, duration = 175, To = 1 } },

    { "DanceToBreathe", new AnimData { start = 145, end = 157, duration = 175, To = 1 } },
    { "DanceToBlock", new AnimData { start = 158, end = 169, duration = 175, To = 1 } },
    { "DanceToDodge", new AnimData { start = 170, end = 181, duration = 175, To = 1 } },
    { "DanceToJump", new AnimData { start = 182, end = 193, duration = 175, To = 1 } },
    { "DanceToKickRight", new AnimData { start = 194, end = 200, duration = 175, To = 1 } },
    { "DanceToKickLeft", new AnimData { start = 206, end = 217, duration = 175, To = 1 } },
    { "DanceToPunchLeft", new AnimData { start = 218, end = 229, duration = 175, To = 1 } },
    { "DanceToPunchRight", new AnimData { start = 230, end = 241, duration = 175, To = 1 } },
    { "DanceToRun", new AnimData { start = 242, end = 253, duration = 175, To = 1 } },
    { "DanceToSprint", new AnimData { start = 254, end = 265, duration = 175, To = 1 } },
    { "DanceToWalk", new AnimData { start = 266, end = 277, duration = 175, To = 1 } },

    { "DodgeToStance", new AnimData { start = 278, end = 289, duration = 175, To = 1 } },

    { "Jump", new AnimData { start = 1, end = 41, duration = 300 } },
    { "JumpDouble", new AnimData { start = 1, end = 32, duration = 300 } },
    { "JumpFalling", new AnimData { start = 1, end = 31, duration = 175, loop = true } },
    { "JumpLanding", new AnimData { start = 1, end = 31, duration = 300 } },
    { "JumpToJumpDouble", new AnimData { start = 290, end = 300, duration = 175, To = 1 } },
    { "JumpToJumpFalling", new AnimData { start = 302, end = 313, duration = 175, To = 1 } },
    { "JumpToJumpLanding", new AnimData { start = 314, end = 325, duration = 175, To = 1 } },
    { "JumpDoubleToJumpFalling", new AnimData { start = 326, end = 337, duration = 175, To = 1 } },
    { "JumpDoubleToJumpLanding", new AnimData { start = 338, end = 349, duration = 175, To = 1 } },
    { "JumpFallingToJumpLanding", new AnimData { start = 350, end = 361, duration = 175, To = 1 } },
    { "JumpLandingToStance", new AnimData { start = 362, end = 373, duration = 175, To = 1 } },

    { "KickLeftToKickRight", new AnimData { start = 374, end = 385, duration = 175, To = 1 } },
    { "KickLeftToPunchLeft", new AnimData { start = 386, end = 397, duration = 175, To = 1 } },
    { "KickLeftToPunchRight", new AnimData { start = 401, end = 409, duration = 175, To = 1 } },
    { "KickLeftToStance", new AnimData { start = 410, end = 421, duration = 175, To = 1 } },
    { "KickRightToKickLeft", new AnimData { start = 422, end = 433, duration = 175, To = 1 } },
    { "KickRightToPunchLeft", new AnimData { start = 434, end = 445, duration = 175, To = 1 } },
    { "KickRightToPunchRight", new AnimData { start = 446, end = 457, duration = 175, To = 1 } },
    { "KickRightToStance", new AnimData { start = 458, end = 469, duration = 175, To = 1 } },
    { "PunchLeftToPunchRight", new AnimData { start = 470, end = 481, duration = 175, To = 1 } },
    { "PunchLeftToKickRight", new AnimData { start = 482, end = 493, duration = 175, To = 1 } },
    { "PunchLeftToKickLeft", new AnimData { start = 494, end = 500, duration = 175, To = 1 } },
    { "PunchLeftToStance", new AnimData { start = 506, end = 517, duration = 175, To = 1 } },
    { "PunchRightToPunchLeft", new AnimData { start = 518, end = 529, duration = 175, To = 1 } },
    { "PunchRightToKickLeft", new AnimData { start = 530, end = 541, duration = 175, To = 1 } },
    { "PunchRightToKickRight", new AnimData { start = 542, end = 553, duration = 175, To = 1 } },
    { "PunchRightToStance", new AnimData { start = 554, end = 565, duration = 175, To = 1 } },

    { "RunToSprint", new AnimData { start = 566, end = 577, duration = 175, To = 1 } },
    { "RunToWalk", new AnimData { start = 578, end = 589, duration = 175, To = 1 } },
    { "RunToPunchRight", new AnimData { start = 590, end = 600, duration = 175, To = 1 } },
    { "RunToPunchLeft", new AnimData { start = 602, end = 613, duration = 175, To = 1 } },
    { "RunToKickLeft", new AnimData { start = 614, end = 625, duration = 175, To = 1 } },
    { "RunToKickRight", new AnimData { start = 626, end = 637, duration = 175, To = 1 } },
    { "RunToJump", new AnimData { start = 638, end = 649, duration = 175, To = 1 } },
    { "RunToDodge", new AnimData { start = 650, end = 661, duration = 175, To = 1 } },
    { "RunToBreathe", new AnimData { start = 662, end = 673, duration = 175, To = 1 } },
    { "RunToBlock", new AnimData { start = 674, end = 685, duration = 175, To = 1 } },

    { "SprintToWalk", new AnimData { start = 686, end = 697, duration = 175, To = 1 } },
    { "SprintToRun", new AnimData { start = 701, end = 709, duration = 175, To = 1 } },
    { "SprintToPunchRight", new AnimData { start = 710, end = 721, duration = 175, To = 1 } },
    { "SprintToPunchLeft", new AnimData { start = 722, end = 733, duration = 175, To = 1 } },
    { "SprintToKickLeft", new AnimData { start = 734, end = 745, duration = 175, To = 1 } },
    { "SprintToKickRight", new AnimData { start = 746, end = 757, duration = 175, To = 1 } },
    { "SprintToJump", new AnimData { start = 758, end = 769, duration = 175, To = 1 } },
    { "SprintToDodge", new AnimData { start = 770, end = 781, duration = 175, To = 1 } },
    { "SprintToBreathe", new AnimData { start = 782, end = 793, duration = 175, To = 1 } },
    { "SprintToBlock", new AnimData { start = 794, end = 800, duration = 175, To = 1 } },

    { "StanceToWalk", new AnimData { start = 806, end = 817, duration = 175, To = 1 } },
    { "StanceToSprint", new AnimData { start = 818, end = 829, duration = 175, To = 1 } },
    { "StanceToRun", new AnimData { start = 830, end = 841, duration = 175, To = 1 } },
    { "StanceToPunchRight", new AnimData { start = 842 , end = 853, duration = 100, To = 1 } },
    { "StanceToPunchLeft", new AnimData { start = 854, end = 865, duration = 100, To = 1 } },
    { "StanceToKickLeft", new AnimData { start = 866, end = 877, duration = 100, To = 1 } },
    { "StanceToKickRight", new AnimData { start = 878, end = 889, duration = 100, To = 1 } },
    { "StanceToJump", new AnimData { start = 890, end = 900, duration = 175, To = 1 } },
    { "StanceToDodge", new AnimData { start = 902, end = 913, duration = 175, To = 1 } },
    { "StanceToBreathe", new AnimData { start = 914, end = 925, duration = 175, To = 1 } },
    { "StanceToBlock", new AnimData { start = 926, end = 937, duration = 175, To = 1 } },

    { "WalkToSprint", new AnimData { start = 938, end = 949, duration = 175, To = 1 } },
    { "WalkToRun", new AnimData { start = 950, end = 961, duration = 175, To = 1 } },
    { "WalkToPunchRight", new AnimData { start = 962, end = 973, duration = 175, To = 1 } },
    { "WalkToPunchLeft", new AnimData { start = 974, end = 985, duration = 175, To = 1 } },
    { "WalkToKickLeft", new AnimData { start = 986, end = 997, duration = 175, To = 1 } },
    { "WalkToKickRight", new AnimData { start = 1001, end = 1009, duration = 175, To = 1 } },
    { "WalkToJump", new AnimData { start = 1010, end = 1021, duration = 175, To = 1 } },
    { "WalkToDodge", new AnimData { start = 1022, end = 1033, duration = 175, To = 1 } },
    { "WalkToBreathe", new AnimData { start = 1034, end = 1045, duration = 175, To = 1 } },
    { "WalkToBlock", new AnimData { start = 1046, end = 1056, duration = 175, To = 1 } },
  };
}