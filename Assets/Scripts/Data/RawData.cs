using System;
using System.Collections.Generic;

public static class TimeScaleController {
  public static Dictionary<int, float> TimeScaleFactor = new Dictionary<int, float> { { 1, 1f }, { 2, 1f }, { 3, 1f }, { 4, 1f } };
}
public static class EsperanzaGearParts {
  public static Dictionary<string, List<string>> gearParts { get; } = new Dictionary<string, List<string>> {
    { "Aqua_no_Head", new List<string> { "Hair" } },
    { "Aqua_aa_Chest", new List<string> { "Torso" } },
    { "Aqua_aa_Legs", new List<string> { "Pelvis", "ThighRight", "ThighLeft" } },
    { "Aqua_ab_Arms", new List<string> { "ArmRight", "ArmLeft", "ForearmLeft", "ForearmRigh" } },
    { "Aqua_ab_Chest", new List<string> { "Torso" } },
    { "Aqua_ab_Feet", new List<string> { "FootRight", "FootLeft", "CalfLeft", "CalfRight" } },
    { "Aqua_ab_Head", new List<string> { "Head", "Hair" } },
    { "Aqua_ab_Legs", new List<string> { "Pelvis", "ThighRight", "ThighLeft" } },
    { "Aqua_ab_Shoulders", new List<string> { "Cape" } },
    { "Aqua_ac_Arms", new List<string> { "HandRight", "HandLeft", "ForearmLeft", "ForearmRigh" } },
    { "Aqua_ac_Chest", new List<string> { "Torso", "ArmRight", "ArmLeft", "Neck" } },
    { "Aqua_ac_Feet", new List<string> { "FootRight", "FootLeft", "CalfLeft", "CalfRight" } },
    { "Aqua_ac_Head", new List<string> { "Head", "Hair" } },
    { "Aqua_ac_Legs", new List<string> { "Pelvis", "ThighRight", "ThighLeft" } },

    { "Base_no_Head", new List<string> { "Hair", "haB" } },
    { "Base_aa_Feet", new List<string> { "FootLeft", "CalfLeft", "FootRight", "CalfRight" } },
    { "Base_aa_Legs", new List<string> { "ThighRight", "ThighLeft", "Pelvis" } },
    { "Base_aa_Chest", new List<string> { "ArmRight", "ArmLeft", "Torso" } },
    { "Base_ab_Arms", new List<string> { "HandRight", "HandLeft", "ForearmLeft", "ForearmRigh" } },
    { "Base_ab_Chest", new List<string> { "Torso", "ArmRight", "ArmLeft", "Neck" } },
    { "Base_ab_Feet", new List<string> { "FootRight", "FootLeft", "CalfLeft", "CalfRight" } },
    { "Base_ab_Head", new List<string> { "Hair" } },
    { "Base_ab_Legs", new List<string> { "Pelvis", "ThighRight", "ThighLeft" } },
    { "Base_ac_Arms", new List<string> { "HandRight", "HandLeft", "ForearmLeft", "ForearmRigh" } },
    { "Base_ac_Chest", new List<string> { "Torso", "ArmRight", "ArmLeft" } },
    { "Base_ac_Feet", new List<string> { "FootRight", "FootLeft" } },
    { "Base_ac_Head", new List<string> { "haB", "Hair", "HairLeft" } },
    { "Base_ac_Legs", new List<string> { "Pelvis", "ThighRight", "ThighLeft", "CalfLeft", "CalfRight" } },
    { "Base_ad_Arms", new List<string> { "ArmRight", "ArmLeft", "ForearmLeft", "ForearmRigh" } },
    { "Base_ad_Belt", new List<string> { "Belt", "FlapFront", "FlapLeft", "FlapRight" } },
    { "Base_ad_Chest", new List<string> { "Torso" } },
    { "Base_ad_Feet", new List<string> { "FootRight", "FootLeft", "CalfLeft", "CalfRight" } },
    { "Base_ad_Head", new List<string> { "Head", "Hair", "HairLeft" } },
    { "Base_ad_Legs", new List<string> { "Pelvis" } },
    { "Base_ad_Shoulders", new List<string> { "Neck" } },

    { "Bolt_no_Head", new List<string> { "Hair", "HairLeft" } },
    { "Bolt_aa_Chest", new List<string> { "Torso" } },
    { "Bolt_aa_Feet", new List<string> { "FootRight", "FootLeft" } },
    { "Bolt_aa_Legs", new List<string> { "Pelvis", "ThighRight", "ThighLeft", "CalfLeft", "CalfRight" } },
    { "Bolt_ab_Arms", new List<string> { "ForearmLeft", "ForearmRigh" } },
    { "Bolt_ab_Belt", new List<string> { "Belt", "FlapLeft", "FlapRight" } },
    { "Bolt_ab_Chest", new List<string> { "Torso" } },
    { "Bolt_ab_Feet", new List<string> { "FootRight", "FootLeft", "CalfLeft", "CalfRight" } },
    { "Bolt_ab_Head", new List<string> { "Head", "Hair", "HairLeft" } },
    { "Bolt_ab_Legs", new List<string> { "Pelvis", "ThighLeft", "ThighRight" } },
    { "Bolt_ab_Shoulders", new List<string> { "ShoulderLeft", "ShoulderRight" } },
    { "Bolt_ac_Arms", new List<string> { "ForearmLeft", "ForearmRight", "HandRight", "HandLeft" } },
    { "Bolt_ac_Belt", new List<string> { "Belt" } },
    { "Bolt_ac_Chest", new List<string> { "Torso", "FlapLeft", "FlapRight", "ArmRight", "ArmLeft" } },
    { "Bolt_ac_Feet", new List<string> { "FootRight", "FootLeft", "CalfLeft", "CalfRight" } },
    { "Bolt_ac_Head", new List<string> { "Head", "Hair", "HairLeft" } },
    { "Bolt_ac_Legs", new List<string> { "Pelvis", "ThighLeft", "ThighRight" } },
    { "Bolt_ac_Shoulders", new List<string> { "ShoulderLeft", "ShoulderRight" } },

    { "Cold_no_Head", new List<string> { "Hair", "haB", "HairLeft" } },
    { "Cold_aa_Chest", new List<string> { "Torso", "ForearmLeft", "ForearmRight", "ArmRight", "ArmLeft" } },
    { "Cold_aa_Feet", new List<string> { "FootRight", "FootLeft" } },
    { "Cold_aa_Legs", new List<string> { "Pelvis", "ThighRight", "ThighLeft", "CalfLeft", "CalfRight" } },
    { "Cold_ab_Arms", new List<string> { "ForearmLeft", "ForearmRight", "ArmRight", "ArmLeft" } },
    { "Cold_ab_Belt", new List<string> { "Belt", "FlapFront", "FlapLeft", "FlapRight" } },
    { "Cold_ab_Chest", new List<string> { "Torso" } },
    { "Cold_ab_Feet", new List<string> { "FootRight", "FootLeft" } },
    { "Cold_ab_Head", new List<string> { "Head", "HeadBack", "Hair", "HairLeft" } },
    { "Cold_ab_Legs", new List<string> { "Pelvis", "ThighLeft", "ThighRight", "CalfRight", "CalfLeft" } },
    { "Cold_ab_Shoulders", new List<string> { "ShoulderLeft", "ShoulderRight" } },
    { "Cold_ac_Arms", new List<string> { "ForearmLeft", "ForearmRight", "HandRight", "HandLeft" } },
    { "Cold_ac_Belt", new List<string> { "Belt", "bf" } },
    { "Cold_ac_Chest", new List<string> { "Torso", "Neck" , "ArmRight", "ArmLeft" } },
    { "Cold_ac_Feet", new List<string> { "FootRight", "FootLeft" } },
    { "Cold_ac_Head", new List<string> { "Head", "HeadBack", "haB" } },
    { "Cold_ac_Legs", new List<string> { "Pelvis", "ThighLeft", "ThighRight", "CalfRight", "CalfLeft", "FlapFront" } },
    { "Cold_ac_Shoulders", new List<string> { "ShoulderLeft", "ShoulderRight" } },

    { "Dark_no_Head", new List<string> { "Hair", "HairRight", "HairLeft" } },
    { "Dark_aa_Arms", new List<string> { "HandRight", "HandLeft" } },
    { "Dark_aa_Chest", new List<string> { "Torso", "Neck", "ForearmLeft", "ForearmRight", "ArmRight", "ArmLeft" } },
    { "Dark_aa_Feet", new List<string> { "FootRight", "FootLeft" } },
    { "Dark_aa_Legs", new List<string> { "Pelvis", "ThighRight", "ThighLeft", "CalfLeft", "CalfRight" } },
    { "Dark_ab_Arms", new List<string> { "ForearmLeft", "ForearmRight", "HandRight", "HandLeft", "ArmRight", "ArmLeft" } },
    { "Dark_ab_Belt", new List<string> { "Belt" } },
    { "Dark_ab_Chest", new List<string> { "Torso", "Neck" } },
    { "Dark_ab_Feet", new List<string> { "FootRight", "FootLeft", "CalfRight", "CalfLeft" } },
    { "Dark_ab_Head", new List<string> { "Head", "Hair", "HairLeft" } },
    { "Dark_ab_Legs", new List<string> { "Pelvis", "ThighLeft", "ThighRight" } },
    { "Dark_ab_Shoulders", new List<string> { "ShoulderLeft", "ShoulderRight", "Cape" } },
    { "Dark_ac_Arms", new List<string> { "ForearmLeft", "ForearmRight", "HandRight", "HandLeft", "ArmRight", "ArmLeft" } },
    { "Dark_ac_Chest", new List<string> { "Torso", "Neck" } },
    { "Dark_ac_Feet", new List<string> { "FootRight", "FootLeft", "CalfRight", "CalfLeft" } },
    { "Dark_ac_Head", new List<string> { "Head", "Hair", "HairLeft", "HairRight" } },
    { "Dark_ac_Legs", new List<string> { "Pelvis", "ThighLeft", "ThighRight" } },
    { "Dark_ac_Shoulders", new List<string> { "ShoulderLeft", "ShoulderRight" } },

    { "Fire_no_Head", new List<string> { "Hair" } },
    { "Fire_aa_Chest", new List<string> { "Torso", "Neck", "ForearmLeft", "ForearmRight", "ArmRight", "ArmLeft" } },
    { "Fire_aa_Legs", new List<string> { "Pelvis" } },
    { "Fire_ab_Arms", new List<string> { "ForearmLeft", "ForearmRight", "ArmRight", "ArmLeft" } },
    { "Fire_ab_Belt", new List<string> { "Cape", "FlapLeft", "FlapRight" } },
    { "Fire_ab_Chest", new List<string> { "Torso", "Neck" } },
    { "Fire_ab_Feet", new List<string> { "FootRight", "FootLeft", "CalfRight", "CalfLeft" } },
    { "Fire_ab_Head", new List<string> { "Head", "Hair" } },
    { "Fire_ab_Legs", new List<string> { "Pelvis", "ThighLeft", "ThighRight" } },
    { "Fire_ab_Shoulders", new List<string> { "ShoulderLeft", "ShoulderRight", "Cape" } },
    { "Fire_ac_Arms", new List<string> { "ForearmLeft", "ForearmRight", "ArmRight", "ArmLeft" } },
    { "Fire_ac_Chest", new List<string> { "Torso", "Neck" } },
    { "Fire_ac_Feet", new List<string> { "FootRight", "FootLeft", "CalfRight", "CalfLeft" } },
    { "Fire_ac_Head", new List<string> { "Head", "HeadBack", "Hair", "HairLeft" } },
    { "Fire_ac_Legs", new List<string> { "Belt", "Pelvis", "ThighLeft", "ThighRight" } },
    { "Fire_ac_Shoulders", new List<string> { "ShoulderLeft", "ShoulderRight" } },
  };
  public static bool ContainsKey(string v) {
    return gearParts.ContainsKey(v);
  }
}
public class AnimData {
  public int start; public int end; public float duration; public bool loop; public bool To; public bool pingPong;
}
public static class EsperanzaAnimations {
  public static Dictionary<string, AnimData> animations { get; } = new Dictionary<string, AnimData> {
    { "Breathe", new AnimData { start = 1, end = 92, duration = 1750, pingPong = true } },
    { "Walk", new AnimData { start = 1, end = 65, duration = 1000, loop = true } },
    { "Run", new AnimData { start = 1, end = 45, duration = 700, loop = true } },
    { "Sprint", new AnimData { start = 1, end = 49, duration = 500, loop = true } },
    { "Dance", new AnimData { start = 1, end = 599, duration = 6000, loop = true } },
    { "Block", new AnimData { start = 1, end = 42, duration = 500 } },
    { "Dodge", new AnimData { start = 1, end = 58, duration = 250 } },
    { "Stance", new AnimData { start = 1, end = 59, duration = 1000,pingPong = true } },
    { "Jump", new AnimData { start = 1, end = 41, duration = 400 } },
    { "JumpDouble", new AnimData { start = 1, end = 32, duration = 300 } },
    { "JumpFalling", new AnimData { start = 1, end = 31, duration = 175, loop = true } },
    { "JumpLanding", new AnimData { start = 1, end = 31, duration = 500 } },
    { "KickLeft", new AnimData { start = 1, end = 53, duration = 500 } },
    { "KickRight", new AnimData { start = 1, end = 31, duration = 300 } },
    { "PunchLeft", new AnimData { start = 1, end = 19, duration = 190 } },
    { "PunchRight", new AnimData { start = 1, end = 19, duration = 150 } },

    { "BlockToStance", new AnimData { start = 1, end = 13, duration = 175, To = true } },

    { "BreatheToBlock", new AnimData { start = 14, end = 25, duration = 175, To = true } },
    { "BreatheToDance", new AnimData { start = 26, end = 37, duration = 175, To = true } },
    { "BreatheToDodge", new AnimData { start = 38, end = 49, duration = 175, To = true } },
    { "BreatheToJump", new AnimData { start = 50,  end = 61, duration = 175, To = true } },
    { "BreatheToKickLeft", new AnimData { start = 62, end = 73, duration = 175, To = true } },
    { "BreatheToKickRight", new AnimData { start = 74, end = 85, duration = 175, To = true } },
    { "BreatheToPunchLeft", new AnimData { start = 86, end = 97, duration = 175, To = true } },
    { "BreatheToPunchRight", new AnimData { start = 101, end = 109, duration = 175, To = true } },
    { "BreatheToRun", new AnimData { start = 110, end = 121, duration = 175, To = true } },
    { "BreatheToSprint", new AnimData { start = 122, end = 133, duration = 175, To = true } },
    { "BreatheToWalk", new AnimData { start = 134, end = 145, duration = 175, To = true } },

    { "DanceToBreathe", new AnimData { start = 146, end = 157, duration = 175, To = true } },
    { "DanceToBlock", new AnimData { start = 158, end = 169, duration = 175, To = true } },
    { "DanceToDodge", new AnimData { start = 170, end = 181, duration = 175, To = true } },
    { "DanceToJump", new AnimData { start = 182, end = 193, duration = 175, To = true } },
    { "DanceToKickRight", new AnimData { start = 194, end = 200, duration = 175, To = true } },
    { "DanceToKickLeft", new AnimData { start = 206, end = 217, duration = 175, To = true } },
    { "DanceToPunchLeft", new AnimData { start = 218, end = 229, duration = 175, To = true } },
    { "DanceToPunchRight", new AnimData { start = 230, end = 241, duration = 175, To = true } },
    { "DanceToRun", new AnimData { start = 242, end = 253, duration = 175, To = true } },
    { "DanceToSprint", new AnimData { start = 254, end = 265, duration = 175, To = true } },
    { "DanceToWalk", new AnimData { start = 266, end = 277, duration = 175, To = true } },

    { "DodgeToStance", new AnimData { start = 278, end = 289, duration = 175, To = true } },

    { "JumpToJumpDouble", new AnimData { start = 290, end = 300, duration = 175, To = true } },
    { "JumpToJumpFalling", new AnimData { start = 302, end = 313, duration = 175, To = true } },
    { "JumpToJumpLanding", new AnimData { start = 314, end = 325, duration = 175, To = true } },

    { "JumpDoubleToJumpFalling", new AnimData { start = 326, end = 337, duration = 175, To = true } },
    { "JumpDoubleToJumpLanding", new AnimData { start = 338, end = 349, duration = 175, To = true } },

    { "JumpFallingToJumpLanding", new AnimData { start = 350, end = 361, duration = 175, To = true } },

    { "JumpLandingToStance", new AnimData { start = 362, end = 373, duration = 175, To = true } },

    { "KickLeftToKickRight", new AnimData { start = 374, end = 385, duration = 175, To = true } },
    { "KickLeftToPunchLeft", new AnimData { start = 386, end = 397, duration = 175, To = true } },
    { "KickLeftToPunchRight", new AnimData { start = 401, end = 409, duration = 175, To = true } },
    { "KickLeftToStance", new AnimData { start = 410, end = 421, duration = 175, To = true } },

    { "KickRightToKickLeft", new AnimData { start = 422, end = 433, duration = 175, To = true } },
    { "KickRightToPunchLeft", new AnimData { start = 434, end = 445, duration = 175, To = true } },
    { "KickRightToPunchRight", new AnimData { start = 446, end = 457, duration = 175, To = true } },
    { "KickRightToStance", new AnimData { start = 458, end = 469, duration = 175, To = true } },

    { "PunchLeftToPunchRight", new AnimData { start = 470, end = 481, duration = 175, To = true } },
    { "PunchLeftToKickRight", new AnimData { start = 482, end = 493, duration = 175, To = true } },
    { "PunchLeftToKickLeft", new AnimData { start = 494, end = 500, duration = 175, To = true } },
    { "PunchLeftToStance", new AnimData { start = 506, end = 517, duration = 175, To = true } },
    { "PunchRightToPunchLeft", new AnimData { start = 518, end = 529, duration = 175, To = true } },
    { "PunchRightToKickLeft", new AnimData { start = 530, end = 541, duration = 175, To = true } },
    { "PunchRightToKickRight", new AnimData { start = 542, end = 553, duration = 175, To = true } },
    { "PunchRightToStance", new AnimData { start = 554, end = 565, duration = 175, To = true } },

    { "RunToSprint", new AnimData { start = 566, end = 577, duration = 175, To = true } },
    { "RunToWalk", new AnimData { start = 578, end = 589, duration = 175, To = true } },
    { "RunToPunchRight", new AnimData { start = 590, end = 600, duration = 175, To = true } },
    { "RunToPunchLeft", new AnimData { start = 602, end = 613, duration = 175, To = true } },
    { "RunToKickLeft", new AnimData { start = 614, end = 625, duration = 175, To = true } },
    { "RunToKickRight", new AnimData { start = 626, end = 637, duration = 175, To = true } },
    { "RunToJump", new AnimData { start = 638, end = 649, duration = 175, To = true } },
    { "RunToDodge", new AnimData { start = 650, end = 661, duration = 175, To = true } },
    { "RunToBreathe", new AnimData { start = 662, end = 673, duration = 175, To = true } },
    { "RunToBlock", new AnimData { start = 674, end = 685, duration = 175, To = true } },

    { "SprintToWalk", new AnimData { start = 686, end = 697, duration = 175, To = true } },
    { "SprintToRun", new AnimData { start = 701, end = 709, duration = 175, To = true } },
    { "SprintToPunchRight", new AnimData { start = 710, end = 721, duration = 175, To = true } },
    { "SprintToPunchLeft", new AnimData { start = 722, end = 733, duration = 175, To = true } },
    { "SprintToKickLeft", new AnimData { start = 734, end = 745, duration = 175, To = true } },
    { "SprintToKickRight", new AnimData { start = 746, end = 757, duration = 175, To = true } },
    { "SprintToJump", new AnimData { start = 758, end = 769, duration = 175, To = true } },
    { "SprintToDodge", new AnimData { start = 770, end = 781, duration = 175, To = true } },
    { "SprintToBreathe", new AnimData { start = 782, end = 793, duration = 175, To = true } },
    { "SprintToBlock", new AnimData { start = 794, end = 800, duration = 175, To = true } },

    { "StanceToWalk", new AnimData { start = 806, end = 817, duration = 175, To = true } },
    { "StanceToSprint", new AnimData { start = 818, end = 829, duration = 175, To = true } },
    { "StanceToRun", new AnimData { start = 830, end = 841, duration = 175, To = true } },
    { "StanceToPunchRight", new AnimData { start = 842 , end = 853, duration = 175, To = true } },
    { "StanceToPunchLeft", new AnimData { start = 854, end = 865, duration = 175, To = true } },
    { "StanceToKickLeft", new AnimData { start = 866, end = 877, duration = 175, To = true } },
    { "StanceToKickRight", new AnimData { start = 878, end = 889, duration = 175, To = true } },
    { "StanceToJump", new AnimData { start = 890, end = 900, duration = 175, To = true } },
    { "StanceToDodge", new AnimData { start = 902, end = 913, duration = 175, To = true } },
    { "StanceToBreathe", new AnimData { start = 914, end = 925, duration = 175, To = true } },
    { "StanceToBlock", new AnimData { start = 926, end = 937, duration = 175, To = true } },

    { "WalkToSprint", new AnimData { start = 938, end = 949, duration = 175, To = true } },
    { "WalkToRun", new AnimData { start = 950, end = 961, duration = 175, To = true } },
    { "WalkToPunchRight", new AnimData { start = 962, end = 973, duration = 175, To = true } },
    { "WalkToPunchLeft", new AnimData { start = 974, end = 985, duration = 175, To = true } },
    { "WalkToKickLeft", new AnimData { start = 986, end = 997, duration = 175, To = true } },
    { "WalkToKickRight", new AnimData { start = 1001, end = 1009, duration = 175, To = true } },
    { "WalkToJump", new AnimData { start = 1010, end = 1021, duration = 175, To = true } },
    { "WalkToDodge", new AnimData { start = 1022, end = 1033, duration = 175, To = true } },
    { "WalkToBreathe", new AnimData { start = 1034, end = 1045, duration = 175, To = true } },
    { "WalkToBlock", new AnimData { start = 1046, end = 1056, duration = 175, To = true } },
  };
}
public static class Interupts {
  public static Dictionary<string, Dictionary<string, string>> interupts { get; } = new Dictionary<string, Dictionary<string, string>> {
    ["Breathe"] = new Dictionary<string, string> {
      { "Block", "BreatheToBlock" }, { "Dance", "BreatheToDance" }, { "Dodge", "BreatheToDodge" }, { "Jump", "BreatheToJump" }, { "KickLeft", "BreatheToKickLeft" }, { "KickRight", "BreatheToKickRight" }, { "PunchLeft", "BreatheToPunchLeft" }, { "PunchRight", "BreatheToPunchRight" }, { "Walk", "BreatheToWalk" }, { "Run", "BreatheToRun" }, { "Sprint", "BreatheToSprint" }
    },
    ["BreatheToWalk"] = new Dictionary<string, string> {
      { "Breathe", "WalkToBreathe" }, { "Run", "WalkToRun" }, { "Sprint", "WalkToSprint" }, { "Block", "WalkToBlock" }, { "Dodge", "WalkToDodge" }, { "Jump", "WalkToJump" }, { "PunchRight", "WalkToPunchRight" }, { "PunchLeft", "WalkToPunchLeft" }, { "KickLeft", "WalkToKickLeft" }, { "KickRight", "WalkToKickRight" },
    },
    ["BreatheToRun"] = new Dictionary<string, string> {
      { "Breathe", "RunToBreathe" }, { "Walk", "RunToWalk" }, { "Sprint", "RunToSprint" }, { "Block", "RunToBlock" }, { "Dodge", "RunToDodge" }, { "Jump", "RunToJump" }, { "PunchRight", "RunToPunchRight" }, { "PunchLeft", "RunToPunchLeft" }, { "KickLeft", "RunToKickLeft" }, { "KickRight", "RunToKickRight" },
    },
    ["BreatheToSprint"] = new Dictionary<string, string> {
      { "Breathe", "SprintToBreathe" }, { "Walk", "SprintToWalk" }, { "Run", "SprintToRun" }, { "Block", "SprintToBlock" }, { "Dodge", "SprintToDodge" }, { "Jump", "SprintToJump" }, { "PunchRight", "SprintToPunchRight" }, { "PunchLeft", "SprintToPunchLeft" }, { "KickLeft", "SprintToKickLeft" }, { "KickRight", "SprintToKickRight" },
    },
    ["BreatheToDance"] = new Dictionary<string, string> {
      { "Block", "DanceToBlock" }, { "Dance", "DanceToDance" }, { "Dodge", "DanceToDodge" }, { "Jump", "DanceToJump" }, { "KickLeft", "DanceToKickLeft" }, { "KickRight", "DanceToKickRight" }, { "PunchLeft", "DanceToPunchLeft" }, { "PunchRight", "DanceToPunchRight" }, { "Walk", "DanceToWalk" }, { "Run", "DanceToRun" }, { "Sprint", "DanceToSprint" }
    },
    ["Walk"] = new Dictionary<string, string> {
      { "Breathe", "WalkToBreathe" }, { "Run", "WalkToRun" }, { "Sprint", "WalkToSprint" }, { "PunchRight", "WalkToPunchRight" }, { "PunchLeft", "WalkToPunchLeft" }, { "KickLeft", "WalkToKickLeft" }, { "KickRight", "WalkToKickRight" }, { "Jump", "WalkToJump" }, { "Dodge", "WalkToDodge" }, { "Block", "WalkToBlock" }
    },
    ["WalkToBreathe"] = new Dictionary<string, string> {
      { "Run", "BreatheToRun" }, { "Walk", "BreatheToWalk" }, { "Sprint", "BreatheToSprint" }, { "Block", "BreatheToBlock" }, { "Dodge", "BreatheToDodge" }, { "Jump", "BreatheToJump" }, { "PunchRight", "BreatheToPunchRight" }, { "PunchLeft", "BreatheToPunchLeft" }, { "KickLeft", "BreatheToKickLeft" }, { "KickRight", "BreatheToKickRight" },
    },
    ["WalkToRun"] = new Dictionary<string, string> {
      { "Breathe", "RunToBreathe" }, { "Walk", "RunToWalk" }, { "Sprint", "RunToSprint" }, { "Block", "RunToBlock" }, { "Dodge", "RunToDodge" }, { "Jump", "RunToJump" }, { "PunchRight", "RunToPunchRight" }, { "PunchLeft", "RunToPunchLeft" }, { "KickLeft", "RunToKickLeft" }, { "KickRight", "RunToKickRight" },
    },
    ["WalkToSprint"] = new Dictionary<string, string> {
      { "Breathe", "SprintToBreathe" }, { "Walk", "SprintToWalk" }, { "Run", "SprintToRun" }, { "Block", "SprintToBlock" }, { "Dodge", "SprintToDodge" }, { "Jump", "SprintToJump" }, { "PunchRight", "SprintToPunchRight" }, { "PunchLeft", "SprintToPunchLeft" }, { "KickLeft", "SprintToKickLeft" }, { "KickRight", "SprintToKickRight" },
    },
    ["Run"] = new Dictionary<string, string> {
      { "Breathe", "RunToBreathe" }, { "Walk", "RunToWalk" }, { "Sprint", "RunToSprint" }, { "PunchRight", "RunToPunchRight" }, { "PunchLeft", "RunToPunchLeft" }, { "KickLeft", "RunToKickLeft" }, { "KickRight", "RunToKickRight" }, { "Jump", "RunToJump" }, { "Dodge", "RunToDodge" }, { "Block", "RunToBlock" }
    },
    ["RunToBreathe"] = new Dictionary<string, string> {
      { "Run", "BreatheToRun" }, { "Walk", "BreatheToWalk" }, { "Sprint", "BreatheToSprint" }, { "Block", "BreatheToBlock" }, { "Dodge", "BreatheToDodge" }, { "Jump", "BreatheToJump" }, { "PunchRight", "BreatheToPunchRight" }, { "PunchLeft", "BreatheToPunchLeft" }, { "KickLeft", "BreatheToKickLeft" }, { "KickRight", "BreatheToKickRight" },
    },
    ["RunToWalk"] = new Dictionary<string, string> {
      { "Breathe", "WalkToBreathe" }, { "Run", "WalkToRun" }, { "Sprint", "WalkToSprint" }, { "Block", "WalkToBlock" }, { "Dodge", "WalkToDodge" }, { "Jump", "WalkToJump" }, { "PunchRight", "WalkToPunchRight" }, { "PunchLeft", "WalkToPunchLeft" }, { "KickLeft", "WalkToKickLeft" }, { "KickRight", "WalkToKickRight" },
    },
    ["RunToSprint"] = new Dictionary<string, string> {
      { "Breathe", "SprintToBreathe" }, { "Walk", "SprintToWalk" }, { "Run", "SprintToRun" }, { "Block", "SprintToBlock" }, { "Dodge", "SprintToDodge" }, { "Jump", "SprintToJump" }, { "PunchRight", "SprintToPunchRight" }, { "PunchLeft", "SprintToPunchLeft" }, { "KickLeft", "SprintToKickLeft" }, { "KickRight", "SprintToKickRight" },
    },
    ["Sprint"] = new Dictionary<string, string> {
      { "Breathe", "SprintToBreathe" }, { "Walk", "SprintToWalk" }, { "Run", "SprintToRun" }, { "PunchRight", "" }, { "PunchLeft", "" }, { "KickLeft", "" }, { "KickRight", "" }, { "Jump", "" }, { "Dodge", "" }, { "Block", "" },
    },
    ["SprintToBreathe"] = new Dictionary<string, string> {
      { "Run", "BreatheToRun" }, { "Walk", "BreatheToWalk" }, { "Sprint", "BreatheToSprint" }, { "Block", "BreatheToBlock" }, { "Dodge", "BreatheToDodge" }, { "Jump", "BreatheToJump" }, { "PunchRight", "BreatheToPunchRight" }, { "PunchLeft", "BreatheToPunchLeft" }, { "KickLeft", "BreatheToKickLeft" }, { "KickRight", "BreatheToKickRight" },
    },
    ["SprintToWalk"] = new Dictionary<string, string> {
      { "Breathe", "WalkToBreathe" }, { "Run", "WalkToRun" }, { "Sprint", "WalkToSprint" }, { "Block", "WalkToBlock" }, { "Dodge", "WalkToDodge" }, { "Jump", "WalkToJump" }, { "PunchRight", "WalkToPunchRight" }, { "PunchLeft", "WalkToPunchLeft" }, { "KickLeft", "WalkToKickLeft" }, { "KickRight", "WalkToKickRight" },
    },
    ["SprintToRun"] = new Dictionary<string, string> {
      { "Breathe", "RunToBreathe" }, { "Walk", "RunToWalk" }, { "Sprint", "RunToSprint" }, { "Block", "RunToBlock" }, { "Dodge", "RunToDodge" }, { "Jump", "RunToJump" }, { "PunchRight", "RunToPunchRight" }, { "PunchLeft", "RunToPunchLeft" }, { "KickLeft", "RunToKickLeft" }, { "KickRight", "RunToKickRight" },
    },
    ["Dance"] = new Dictionary<string, string> {
        { "Block", "" }, { "Dodge", "" }, { "Jump", "" }, { "KickRight", "" }, { "KickLeft", "" }, { "PunchLeft", "" }, { "PunchRight", "" }, { "Run", "" }, { "Sprint", "" }, { "Walk", "" },
    },
    ["DanceToBreathe"] = new Dictionary<string, string> {
      { "Block", "BreatheToBlock" }, { "Dance", "BreatheToDance" }, { "Dodge", "BreatheToDodge" }, { "Jump", "BreatheToJump" }, { "KickLeft", "BreatheToKickLeft" }, { "KickRight", "BreatheToKickRight" }, { "PunchLeft", "BreatheToPunchLeft" }, { "PunchRight", "BreatheToPunchRight" }, { "Walk", "BreatheToWalk" }, { "Run", "BreatheToRun" }, { "Sprint", "BreatheToSprint" }
    },
    ["Block"] = new Dictionary<string, string> {
      { "Stance","BlockToStance" }
    },
    ["Dodge"] = new Dictionary<string, string> {
      { "Stance", "DodgeToStance" },
    },
    ["Jump"] = new Dictionary<string, string> {
      { "JumpDouble", "JumpToJumpDouble" }, { "JumpFalling", "JumpToJumpFalling" }, { "JumpLanding", "JumpToJumpLanding" },
    },
    ["JumpDouble"] = new Dictionary<string, string> {
      { "JumpFalling", "JumpDoubleToJumpFalling" }, { "JumpLanding", "JumpDoubleToJumpLanding" }
    },
    ["JumpFalling"] = new Dictionary<string, string> {
      { "JumpLanding", "JumpFallingToJumpLanding" }
    },
    ["JumpLanding"] = new Dictionary<string, string> {
      { "Stance", "JumpLandingToStance" },
    },
    ["KickLeft"] = new Dictionary<string, string> {
      { "KickRight", "KickLeftToKickRight" }, { "PunchLeft", "KickLeftToPunchLeft" }, { "PunchRight", "KickLeftToPunchRight" }, { "Stance", "KickLeftToStance" },
    },
    ["KickRight"] = new Dictionary<string, string> {
      { "KickLeft", "KickRightToKickLeft" }, { "PunchLeft", "KickRightToPunchLeft" }, { "PunchRight", "KickRightToPunchRight" }, { "Stance", "KickRightToStance" }
    },
    ["PunchLeft"] = new Dictionary<string, string> {
      { "PunchRight", "PunchLeftToPunchRight" }, { "KickRight", "PunchLeftToKickRight" }, { "KickLeft", "PunchLeftToKickLeft" }, { "Stance", "PunchLeftToStance" }
    },
    ["PunchRight"] = new Dictionary<string, string> {
      { "PunchLeft", "PunchRightToPunchLeft" }, { "KickLeft", "PunchRightToKickLeft" }, { "KickRight", "PunchRightToKickRight" }, { "Stance", "PunchRightToStance" }
    },
    ["Stance"] = new Dictionary<string, string> {
      { "Walk", "StanceToWalk" }, { "Sprint", "StanceToSprint" }, { "Run", "StanceToRun" }, { "PunchRight", "StanceToPunchRight" }, { "PunchLeft", "StanceToPunchLeft" }, { "KickLeft", "StanceToKickLeft" }, { "KickRight", "StanceToKickRight" }, { "Jump", "StanceToJump" }, { "Dodge", "StanceToDodge" }, { "Breathe", "StanceToBreathe" }, { "Block", "StanceToBlock" }
    }
  };
}
public static class BounceAdjustments {
  public static Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, float>>>> adjustments { get; } = new Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, float>>>> {
    ["Base_no_Head"] = new Dictionary<string, Dictionary<string, Dictionary<string, float>>> {
      ["Hair"] = new Dictionary<string, Dictionary<string, float>> {
        ["Breathe"] = new Dictionary<string, float> { { "x", 0 }, { "y", 0 }, { "offset", 0.1f } },
        ["BreatheToWalk"] = new Dictionary<string, float> { { "x", -3 }, { "y", -22 }, { "offset", -25 } },
        ["BreatheToRun"] = new Dictionary<string, float> { { "x", .3f }, { "y", 0 }, { "offset", -.5f } },
        ["BreatheToSprint"] = new Dictionary<string, float> { { "x", 11 }, { "y", -40 }, { "offset", -35 } },

        ["Walk"] = new Dictionary<string, float> { { "x", 0 }, { "y", -25 }, { "offset", -40 } },
        ["WalkToBreathe"] = new Dictionary<string, float> { { "x", -7 }, { "y", -23 }, { "offset", -20 } },
        ["WalkToRun"] = new Dictionary<string, float> { { "y", -23 }, { "x", 2 }, { "offset", -40 } },
        ["WalkToSprint"] = new Dictionary<string, float> { { "y", -55 }, { "x", 5 }, { "offset", -45 } },

        ["Run"] = new Dictionary<string, float> { { "x", .45f }, { "y", 0 }, { "offset", -1 } },
        ["RunToBreathe"] = new Dictionary<string, float> { { "y", 0 }, { "x", .3f }, { "offset", -.5f } },
        ["RunToWalk"] = new Dictionary<string, float> { { "y", -23 }, { "offset", -35 } },
        ["RunToSprint"] = new Dictionary<string, float> { { "y", -50 }, { "x", 5 }, { "offset", -45 } },

        ["Sprint"] = new Dictionary<string, float> { { "y", -75 }, { "x", 10 }, { "offset", -57 } },
        ["SprintToBreathe"] = new Dictionary<string, float> { { "x", -3 }, { "y", -45 }, { "offset", -35 } },
        ["SprintToWalk"] = new Dictionary<string, float> { { "x", 5 }, { "y", -48 }, { "offset", -45 } },
        ["SprintToRun"] = new Dictionary<string, float> { { "x", 3 }, { "y", -48 }, { "offset", -40 } },
      },
      ["HairBack"] = new Dictionary<string, Dictionary<string, float>> {
        ["Breathe"] = new Dictionary<string, float> { { "x", 0 }, { "y", 0 }, { "offset", 0.1f } },
        ["BreatheToWalk"] = new Dictionary<string, float> { { "x", -3 }, { "y", -22 }, { "offset", -25 } },
        ["BreatheToRun"] = new Dictionary<string, float> { { "x", .3f }, { "y", 0 }, { "offset", -.5f } },
        ["BreatheToSprint"] = new Dictionary<string, float> { { "x", 11 }, { "y", -40 }, { "offset", -35 } },

        ["Walk"] = new Dictionary<string, float> { { "x", 0 }, { "y", -25 }, { "offset", -40 } },
        ["WalkToBreathe"] = new Dictionary<string, float> { { "x", -7 }, { "y", -23 }, { "offset", -20 } },
        ["WalkToRun"] = new Dictionary<string, float> { { "y", -23 }, { "x", 2 }, { "offset", -40 } },
        ["WalkToSprint"] = new Dictionary<string, float> { { "y", -55 }, { "x", 5 }, { "offset", -45 } },

        ["Run"] = new Dictionary<string, float> { { "x", .45f }, { "y", 0 }, { "offset", -1 } },
        ["RunToBreathe"] = new Dictionary<string, float> { { "y", 0 }, { "x", .3f }, { "offset", -.5f } },
        ["RunToWalk"] = new Dictionary<string, float> { { "y", -23 }, { "offset", -35 } },
        ["RunToSprint"] = new Dictionary<string, float> { { "y", -50 }, { "x", 5 }, { "offset", -45 } },

        ["Sprint"] = new Dictionary<string, float> { { "y", -75 }, { "x", 10 }, { "offset", -60 } },
        ["SprintToBreathe"] = new Dictionary<string, float> { { "x", -3 }, { "y", -45 }, { "offset", -35 } },
        ["SprintToWalk"] = new Dictionary<string, float> { { "x", 5 }, { "y", -48 }, { "offset", -45 } },
        ["SprintToRun"] = new Dictionary<string, float> { { "x", 3 }, { "y", -48 }, { "offset", -40 } },
      }
    },
    ["Bolt_no_Head"] = new Dictionary<string, Dictionary<string, Dictionary<string, float>>> {
      ["Hair"] = new Dictionary<string, Dictionary<string, float>> {
        ["Breathe"] = new Dictionary<string, float> { { "x", -16 }, { "y", -25 }, { "offset", -1 } },
        ["BreatheToWalk"] = new Dictionary<string, float> { { "x", -3 }, { "y", -22 }, { "offset", -25 } },
        ["BreatheToRun"] = new Dictionary<string, float> { { "x", -3 }, { "y", -22 }, { "offset", -12 } },
        ["BreatheToSprint"] = new Dictionary<string, float> { { "x", 11 }, { "y", -40 }, { "offset", -35 } },
        ["Walk"] = new Dictionary<string, float> { { "x", 0 }, { "y", -25 }, { "offset", -40 } },
        ["WalkToBreathe"] = new Dictionary<string, float> { { "x", -7 }, { "y", -23 }, { "offset", -20 } },
        ["WalkToRun"] = new Dictionary<string, float> { { "y", -23 }, { "x", 2 }, { "offset", -40 } },
        ["WalkToSprint"] = new Dictionary<string, float> { { "y", -55 }, { "x", 5 }, { "offset", -45 } },
        ["Run"] = new Dictionary<string, float> { { "x", -3 }, { "y", -25 }, { "offset", -30 } },
        ["RunToBreathe"] = new Dictionary<string, float> { { "y", -22 }, { "x", -3 }, { "offset", -20 } },
        ["RunToWalk"] = new Dictionary<string, float> { { "y", -23 }, { "offset", -35 } },
        ["RunToSprint"] = new Dictionary<string, float> { { "y", -50 }, { "x", 5 }, { "offset", -45 } },
        ["Sprint"] = new Dictionary<string, float> { { "y", -75 }, { "x", 10 }, { "offset", -57 } },
        ["SprintToBreathe"] = new Dictionary<string, float> { { "x", -3 }, { "y", -45 }, { "offset", -35 } },
        ["SprintToWalk"] = new Dictionary<string, float> { { "x", 5 }, { "y", -48 }, { "offset", -45 } },
        ["SprintToRun"] = new Dictionary<string, float> { { "x", 3 }, { "y", -48 }, { "offset", -40 } },
      },
      ["HairLeft"] = new Dictionary<string, Dictionary<string, float>> {
        ["Breathe"] = new Dictionary<string, float> { { "x", -16 }, { "y", -25 }, { "offset", -1 } },
        ["BreatheToWalk"] = new Dictionary<string, float> { { "x", -3 }, { "y", -22 }, { "offset", -25 } },
        ["BreatheToRun"] = new Dictionary<string, float> { { "x", -3 }, { "y", -22 }, { "offset", -12 } },
        ["BreatheToSprint"] = new Dictionary<string, float> { { "x", 11 }, { "y", -40 }, { "offset", -35 } },
        ["Walk"] = new Dictionary<string, float> { { "x", 0 }, { "y", -25 }, { "offset", -40 } },
        ["WalkToBreathe"] = new Dictionary<string, float> { { "x", -7 }, { "y", -23 }, { "offset", -20 } },
        ["WalkToRun"] = new Dictionary<string, float> { { "y", -23 }, { "x", 2 }, { "offset", -40 } },
        ["WalkToSprint"] = new Dictionary<string, float> { { "y", -55 }, { "x", 5 }, { "offset", -45 } },
        ["Run"] = new Dictionary<string, float> { { "x", -3 }, { "y", -25 }, { "offset", -30 } },
        ["RunToBreathe"] = new Dictionary<string, float> { { "y", -22 }, { "x", -3 }, { "offset", -20 } },
        ["RunToWalk"] = new Dictionary<string, float> { { "y", -23 }, { "offset", -35 } },
        ["RunToSprint"] = new Dictionary<string, float> { { "y", -50 }, { "x", 5 }, { "offset", -45 } },
        ["Sprint"] = new Dictionary<string, float> { { "y", -75 }, { "x", 10 }, { "offset", -57 } },
        ["SprintToBreathe"] = new Dictionary<string, float> { { "x", -3 }, { "y", -45 }, { "offset", -35 } },
        ["SprintToWalk"] = new Dictionary<string, float> { { "x", 5 }, { "y", -48 }, { "offset", -45 } },
        ["SprintToRun"] = new Dictionary<string, float> { { "x", 3 }, { "y", -48 }, { "offset", -40 } },
      }
    }
  };
}
public static class Abbreviations {
  public static Dictionary<string, string> all { get; } = new Dictionary<string, string> {
    { "STR", "Strength" }, { "DEX", "Dexterity" }, { "END", "Endurance" }, { "INT", "Intelligence" }, { "LCK", "Luck" }, { "AMP", "Amperage" },
    { "VLT", "Voltage" }, { "PYR", "Pyro" }, { "EMB", "Ember" }, { "CHL", "Chill" }, { "ICI", "Icicle" }, { "VAP", "Vapor" }, { "MOI", "Moist" },
    { "UMB", "Umbral" }, { "VOI", "Void" }, { "ABY", "Abyss" }, { "ECL", "Eclipse" }, { "HP", "Health Points" },
    { "HPRG", "Health Point Regeneration" }, { "ARM", "Armor" }, { "DMG", "Damage" }, { "AKSP", "Attack Speed" }, { "NRG", "Energy" },
    { "NRGRG", "Energy Regeneration" }, { "DCHC", "Direct Chance" }, { "DDMG", "Direct Damage" }, { "CCHC", "Critical Chance" },
    { "CDMG", "Critical Damage" }, { "LCHC", "Lucky Chance" }, { "LDMG", "Lucky Damage" }, { "HEAL", "Healing" }, { "BNS", "Bonus" },
    { "CDST", "Closing Distance" }, { "LDSC", "Lightning Discharge" }, { "FDMG", "Flame Damage" }, { "AREA", "Area" }, { "DUR", "Duration" },
    { "AFT", "After Effect" }, { "EVD", "Evade" }, { "CLN", "Cleanse" }, { "FEAR", "Fear" }, { "SPEC", "Specral" }, { "PEN", "Penetration" },
    { "MVSP", "Movement Speed" }, { "RK", "Right Kick" }, { "LK", "Left Kick" }, { "RP", "Right Punch" }, { "LP", "Left Punch" },
    { "BK", "Block" }, { "DO", "Dodge" }, { "JP", "Jump" }, { "SP", "Super Punch" }, { "SK", "Super Kick" }, { "SH", "Shock" },
    { "CL", "Chain Lighting" }, { "ST", "Static" }, { "LB", "Lightning Bolt" }, { "ID", "Instant Dodge" }, { "DD", "Double Dodge" },
    { "DJ", "Double Jump" }, { "TB", "Thunder Bolt" }, { "OR", "Orbit" }, { "FT", "Flamethrower" }, { "BW", "Burning Wall" }, { "BZ", "Blaze" },
    { "PL", "Pyre Light" }, { "FS", "Flame Shield" }, { "BD", "Burning Dodge" }, { "FW", "Flame Wings" }, { "MT", "Meteor" }, { "FI", "Fizzure" },
    { "FC", "Frost Cloud" }, { "IB", "Ice Blast" }, { "IT", "Iceclitite" }, { "IM", "Iceclimite" }, { "IS", "Ice Shield" }, { "SL", "Slide" },
    { "FF", "Frost Float" }, { "AV", "Avalanche" }, { "BL", "Blizzard" }, { "WB", "Water Blast" }, { "CH", "Crushing Hydro" },
    { "WS", "Water Sphere" }, { "PD", "Pressure Deluge" }, { "BB", "Bubble" }, { "VD", "Vapor Dash" }, { "DV", "Diving Vortex" },
    { "RN", "Rain Needles" }, { "TS", "Tsunami Strike" }, { "RP", "Rip" }, { "TR", "Tear" }, { "RW", "Raging Whisper" }, { "SE", "Seethe" },
    { "CK", "Corrupt Kinesis" }, { "SW", "Shadow Walk" }, { "AC", "Abyssal Call" }, { "SS", "Soul Siphon" }, { "SI", "Soul Infection" }
  };
  public static Dictionary<string, List<string>> structure { get; } = new Dictionary<string, List<string>> {
    ["Major"] = new List<string> { "STR", "DEX", "END", "INT", "LCK", "AMP", "VLT", "PYR", "EMB", "CHL", "ICI", "VAP", "MOI", "UMB", "VOI", "ABY", "ECL", },
    ["Minor"] = new List<string> { "HP", "HPRG", "ARM", "DMG", "AKSP", "NRG", "NRGRG", "DCHC", "DDMG", "CCHC", "CDMG", "LCHC", "LDMG", "HEAL", "BNS", "CDST", "LDSC", "FDMG", "AREA", "DUR", "AFT", "EVD", "CLN", "FEAR", "SPEC", "PEN", "MVSP" },
    ["Ability"] = new List<string> { "RK", "LK", "RP", "LP", "BK", "DO", "JP", "SP", "SK", "SH", "CL", "ST", "LB", "ID", "DD", "DJ", "TB", "OR", "FT", "BW", "BZ", "PL", "FS", "BD", "FW", "MT", "FI", "FC", "IB", "IT", "IM", "IS", "SL", "FF", "AV", "BL", "WB", "CH", "WS", "PD", "BB", "VD", "DV", "RN", "TS", "RP", "TR", "RW", "SE", "CK", "SW", "AC", "SS", "SI" },
  };
  public static Dictionary<string, Dictionary<string, List<string>>> FormMajorMinor { get; } = new Dictionary<string, Dictionary<string, List<string>>> {
    ["Base"] = new Dictionary<string, List<string>> {
      ["STR"] = new List<string> { "HP", "DMG", "DCHC", },
      ["DEX"] = new List<string> { "AS", "NRGRG", "CDMG", },
      ["END"] = new List<string> { "NRG", "HPRG", "ARM", },
      ["INT"] = new List<string> { "HEAL", "CCHC", "LDMG", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BONUS", }
    },
    ["Bolt"] = new Dictionary<string, List<string>> {
      ["DEX"] = new List<string> { "DMG", "MVSP", "AKSP", },
      ["END"] = new List<string> { "NRG", "NRGRG", "HP", },
      ["AMP"] = new List<string> { "CDST", "HPRG", "ARM", },
      ["VLT"] = new List<string> { "LDSC", "CCHC", "HEAL", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BONUS", }
    },
    ["Fire"] = new Dictionary<string, List<string>> {
      ["STR"] = new List<string> { "DMG", "DCHC", "HPRG", },
      ["END"] = new List<string> { "NRG", "NRGRG", "HP", },
      ["PYR"] = new List<string> { "FDMG", "AKSP", "ARM", },
      ["EMB"] = new List<string> { "AREA", "CCHC", "HEAL", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BONUS", }
    },
    ["Cold"] = new Dictionary<string, List<string>> {
      ["END"] = new List<string> { "DMG", "NRGRG", "NRG", },
      ["INT"] = new List<string> { "HEAL", "CCHC", "LDMG", },
      ["CHL"] = new List<string> { "DUR", "HP", "ARM", },
      ["ICI"] = new List<string> { "AFT", "CCHC", "HEAL", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BONUS", }
    },
    ["Aqua"] = new Dictionary<string, List<string>> {
      ["INT"] = new List<string> { "DMG", "HEAL", "CCHC", },
      ["DEX"] = new List<string> { "AKSP", "NRGRG", "CDMG", },
      ["VAP"] = new List<string> { "EVD", "NRG", "ARM", },
      ["MOI"] = new List<string> { "CLN", "CCHC", "HP", },
      ["LCK"] = new List<string> { "LCHC", "DDMG", "BONUS", }
    },
    ["Dark"] = new Dictionary<string, List<string>> {
      ["UMB"] = new List<string> { "DMG", "NRG", "FEAR", },
      ["VOI"] = new List<string> { "SPEC", "AKSP", "CCHC", },
      ["ABY"] = new List<string> { "PEN", "ARM", "LDMG", },
      ["ECL"] = new List<string> { "EVD", "NRGRG", "AREA", },
      ["LCK"] = new List<string> { "LCHC", "CDMG", "BONUS", }
    }
  };
}
public static class GearNames {
  public static Dictionary<string, Dictionary<string, List<string>>> names { get; } = new Dictionary<string, Dictionary<string, List<string>>> {
    ["STR"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Durable", "Firm", "Stable", "Tough", },
      ["suffix"] = new List<string> { "Courage", "Clout", "Power", "Vigor", "Brawn", "Force", "Strength", }
    },
    ["DEX"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Fast", "Tactical", "Proficient", "Handy", },
      ["suffix"] = new List<string> { "Artistry", "Finesse", "Knack", "Nimbleness", "Readiness", "Dexterity", },
    },
    ["END"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Tenacious", "Resolute", "Tolerant", "Continuing", },
      ["suffix"] = new List<string> { "Fortitude", "Vitality", "Mettle", "Withstanding", "Forebearance", "Endurance", },
    },
    ["INT"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Brilliant", "Clever", "Alert", "Bright", "Savvy" },
      ["suffix"] = new List<string> { "Ingenuity", "Understanding", "Wit", "Comprehension", "Savvy", "Intelligence", },
    },
    ["LCK"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Fortunate", "Godsend", "Victorious", },
      ["suffix"] = new List<string> { "Advantage", "Blessing", "Opportunity", "Prosperity", "Serendipity", "Luck", },
    },
    ["AMP"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Extended", },
      ["suffix"] = new List<string> { "Amplitude", },
    },
    ["VLT"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Surging", },
      ["suffix"] = new List<string> { "Voltage", },
    },
    ["PYR"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Mindfire", },
      ["suffix"] = new List<string> { "Pyrokinesis", },
    },
    ["EMB"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Emblazed", },
      ["suffix"] = new List<string> { "Emblaze", },
    },
    ["CHL"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Chilling", },
      ["suffix"] = new List<string> { "Chill", },
    },
    ["ICI"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Frozen", },
      ["suffix"] = new List<string> { "Icicle", },
    },
    ["VAP"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Wispy", },
      ["suffix"] = new List<string> { "Vapor", },
    },
    ["MOI"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Wet", },
      ["suffix"] = new List<string> { "Moisture", },
    },
    ["UMB"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Dark", },
      ["suffix"] = new List<string> { "Umbral", },
    },
    ["OID"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Empty", },
      ["suffix"] = new List<string> { "Void", },
    },
    ["ABY"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Abysmal", },
      ["suffix"] = new List<string> { "Dread", },
    },
    ["ECL"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Shadowy", },
      ["suffix"] = new List<string> { "Eclipse", },
    },
    ["HP"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Healthy", },
      ["suffix"] = new List<string> { "Health", },
    },
    ["HPRG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Regenerative", },
      ["suffix"] = new List<string> { "Regeneration", },
    },
    ["ARM"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Protective", },
      ["suffix"] = new List<string> { "Protection", },
    },
    ["DMG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Damaging", },
      ["suffix"] = new List<string> { "Damage", },
    },
    ["AKSP"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Speedy", },
      ["suffix"] = new List<string> { "Speed", },
    },
    ["NRG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Energetic", },
      ["suffix"] = new List<string> { "Energy", },
    },
    ["NRGRG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Restorative", },
      ["suffix"] = new List<string> { "Restoration", },
    },
    ["DCHC"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Direct", },
      ["suffix"] = new List<string> { "Precision", },
    },
    ["DDMG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Destructive", },
      ["suffix"] = new List<string> { "Destruction", },
    },
    ["CCHC"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Critical", },
      ["suffix"] = new List<string> { "Priority", },
    },
    ["CDMG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Potent", },
      ["suffix"] = new List<string> { "Pain", },
    },
    ["LCHC"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Lucky", },
      ["suffix"] = new List<string> { "Luck", },
    },
    ["LDMG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Boon", },
      ["suffix"] = new List<string> { "Windfall", },
    },
    ["HEAL"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Mending", },
      ["suffix"] = new List<string> { "Healing", },
    },
    ["BONUS"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Bonus", },
      ["suffix"] = new List<string> { "Bonus", },
    },
    ["CDST"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Zipping", },
      ["suffix"] = new List<string> { "Snaping", },
    },
    ["LDSC"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Static", },
      ["suffix"] = new List<string> { "Discharge", },
    },
    ["FDMG"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Flaming", },
      ["suffix"] = new List<string> { "Flame", },
    },
    ["AREA"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Mighty", },
      ["suffix"] = new List<string> { "Area", },
    },
    ["DUR"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Lasting", },
      ["suffix"] = new List<string> { "Duration", },
    },
    ["AFT"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Residual", },
      ["suffix"] = new List<string> { "Effect", },
    },
    ["EVD"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Evasive", },
      ["suffix"] = new List<string> { "Evasion", },
    },
    ["CLNS"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Cleansing", },
      ["suffix"] = new List<string> { "Cleaning", },
    },
    ["FEAR"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Fearful", },
      ["suffix"] = new List<string> { "Fear", },
    },
    ["SPEC"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Spectral", },
      ["suffix"] = new List<string> { "Specter", },
    },
    ["PEN"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Penetrating", },
      ["suffix"] = new List<string> { "Penetration", },
    },
    ["MVSP"] = new Dictionary<string, List<string>> {
      ["prefix"] = new List<string> { "Swift", },
      ["suffix"] = new List<string> { "Swiftness", },
    }
  };
}
public static class FormStatIncreases {
  public static Dictionary<string, Dictionary<string, Dictionary<string, float>>> increases { get; } = new Dictionary<string, Dictionary<string, Dictionary<string, float>>> {
    ["Base"] = new Dictionary<string, Dictionary<string, float>> {
      ["STR"] = new Dictionary<string, float> { ["HP"] = 1000, ["DMG"] = 1, ["DCHC"] = 0.1f },
      ["DEX"] = new Dictionary<string, float> { ["AS"] = 0.01f, ["NRGRG"] = 0.01f, ["CDMG"] = 1 },
      ["END"] = new Dictionary<string, float> { ["NRG"] = 10, ["HPRG"] = 0.01f, ["ARM"] = 1 },
      ["INT"] = new Dictionary<string, float> { ["HEAL"] = 1, ["CCHC"] = 0.1f, ["LDMG"] = 1 },
      ["LCK"] = new Dictionary<string, float> { ["LCHC"] = 0.1f, ["DDMG"] = 1, ["BONUS"] = 1 }
    },
    ["Bolt"] = new Dictionary<string, Dictionary<string, float>> {
      ["DEX"] = new Dictionary<string, float> { ["DMG"] = 1, ["MVSP"] = 0.1f, ["AKSP"] = 0.01f },
      ["END"] = new Dictionary<string, float> { ["NRG"] = 1, ["NRGRG"] = 0.01f, ["HP"] = 10 },
      ["AMP"] = new Dictionary<string, float> { ["CDST"] = 1, ["HPRG"] = 0.01f, ["ARM"] = 1 },
      ["VLT"] = new Dictionary<string, float> { ["LDSC"] = 1, ["CCHC"] = 0.1f, ["HEAL"] = 1 },
      ["LCK"] = new Dictionary<string, float> { ["LCHC"] = 0.1f, ["DDMG"] = 1, ["BONUS"] = 1 }
    },
    ["Fire"] = new Dictionary<string, Dictionary<string, float>> {
      ["STR"] = new Dictionary<string, float> { ["DMG"] = 1, ["DCHC"] = .1f, ["HPRG"] = .01f },
      ["END"] = new Dictionary<string, float> { ["NRG"] = 1, ["NRGRG"] = .01f, ["HP"] = 10 },
      ["PYR"] = new Dictionary<string, float> { ["FDMG"] = 1, ["AKSP"] = .01f, ["ARM"] = 1 },
      ["EMB"] = new Dictionary<string, float> { ["AREA"] = 1, ["CCHC"] = .1f, ["HEAL"] = 1 },
      ["LCK"] = new Dictionary<string, float> { ["LCHC"] = .1f, ["DDMG"] = 1, ["BONUS"] = 1 }
    },
    ["Cold"] = new Dictionary<string, Dictionary<string, float>> {
      ["END"] = new Dictionary<string, float> { ["DMG"] = 1, ["NRGRG"] = .01f, ["NRG"] = 1 },
      ["INT"] = new Dictionary<string, float> { ["HEAL"] = 1, ["CCHC"] = .1f, ["LDMG"] = 1 },
      ["CHL"] = new Dictionary<string, float> { ["DUR"] = 1, ["HP"] = 10, ["ARM"] = 1 },
      ["ICI"] = new Dictionary<string, float> { ["AFT"] = .1f, ["CCHC"] = .1f, ["HEAL"] = 1 },
      ["LCK"] = new Dictionary<string, float> { ["LCHC"] = .1f, ["DDMG"] = 1, ["BONUS"] = 1 }
    },
    ["Aqua"] = new Dictionary<string, Dictionary<string, float>> {
      ["INT"] = new Dictionary<string, float> { ["DMG"] = 1, ["HEAL"] = 1, ["CCHC"] = .1f },
      ["DEX"] = new Dictionary<string, float> { ["AKSP"] = .01f, ["NRGRG"] = .01f, ["CDMG"] = 1 },
      ["VAP"] = new Dictionary<string, float> { ["EVD"] = .1f, ["NRG"] = 1, ["ARM"] = 1 },
      ["MOI"] = new Dictionary<string, float> { ["CLN"] = .01f, ["CCHC"] = .1f, ["HP"] = 10 },
      ["LCK"] = new Dictionary<string, float> { ["LCHC"] = .1f, ["DDMG"] = 1, ["BONUS"] = 1 }
    },
    ["Dark"] = new Dictionary<string, Dictionary<string, float>> {
      ["UMB"] = new Dictionary<string, float> { ["DMG"] = 1, ["NRG"] = 1, ["FEAR"] = 1 },
      ["VOI"] = new Dictionary<string, float> { ["SPEC"] = 0.1f, ["AKSP"] = .01f, ["CCHC"] = .1f },
      ["ABY"] = new Dictionary<string, float> { ["PEN"] = 1, ["ARM"] = 1, ["LDMG"] = 1 },
      ["ECL"] = new Dictionary<string, float> { ["EVD"] = 1, ["NRGRG"] = .01f, ["AREA"] = 1 },
      ["LCK"] = new Dictionary<string, float> { ["LCHC"] = .1f, ["CDMG"] = 1, ["BONUS"] = 1 }
    }
  };
}
public static class AllStatValues {
  public static Dictionary<string, float> allStats { set; get; } = new Dictionary<string, float> {
    { "DMG", 0 }, { "DCHC", 0 }, { "HP", 0 }, { "AS", 0 }, { "NRGRG", 0 },
    { "CDMG", 0 }, { "NRG", 0 }, { "HPRG", 0 }, { "ARM", 0 }, { "HEAL", 0 },
    { "CCHC", 0 }, { "LDMG", 0 }, { "LCHC", 0 }, { "DDMG", 0 }, { "BONUS", 0 },
    { "MVSP", 0 }, { "AKSP", 0 }, { "CDST", 0 }, { "LDSC", 0 }, { "FDMG", 0 },
    { "AREA", 0 }, { "DUR", 0 }, { "AFT", 0 }, { "EVD", 0 }, { "CLN", 0 },
    { "FEAR", 0 }, { "SPEC", 0 }, { "PEN", 0 }
  };
}
public static class FormStatsValues {
  public static Dictionary<string, Dictionary<string, int>> values { set; get; } = new Dictionary<string, Dictionary<string, int>> {
    ["Base"] = new Dictionary<string, int> { ["STR"] = 1, ["DEX"] = 1, ["END"] = 1, ["INT"] = 1, ["LCK"] = 1 },
    ["Bolt"] = new Dictionary<string, int> { ["DEX"] = 0, ["END"] = 0, ["AMP"] = 0, ["VLT"] = 0, ["LCK"] = 0 },
    ["Fire"] = new Dictionary<string, int> { ["STR"] = 0, ["END"] = 0, ["PYR"] = 0, ["EMB"] = 0, ["LCK"] = 0 },
    ["Cold"] = new Dictionary<string, int> { ["END"] = 0, ["INT"] = 0, ["CHL"] = 0, ["ICI"] = 0, ["LCK"] = 0 },
    ["Aqua"] = new Dictionary<string, int> { ["INT"] = 0, ["DEX"] = 0, ["VAP"] = 0, ["MOI"] = 0, ["LCK"] = 0 },
    ["Dark"] = new Dictionary<string, int> { ["UMB"] = 0, ["VOI"] = 0, ["ABY"] = 0, ["ECL"] = 0, ["LCK"] = 0 }
  };
}
public static class EsperanzaForms {
  public static Dictionary<string, int> Active { get; set; } = new Dictionary<string, int> { { "Base", 1 }, { "Bolt", 0 }, { "Cold", 0 }, { "Fire", 0 }, { "Aqua", 0 }, { "Dark", 0 } };
  public static Dictionary<string, int> Unlocked { get; set; } = new Dictionary<string, int> { { "Base", 1 }, { "Bolt", 0 }, { "Aqua", 0 }, { "Cold", 0 }, { "Fire", 0 }, { "Dark", 0 } };

  public static void SetActive(string v) {
    foreach (var item in Active) {
      if (item.Key == v) { Unlocked[item.Key] = 1; }
      else { Unlocked[item.Key] = 0; }
    }
  }
  public static string GetActive() {
    var v = "";
    foreach (var item in Active) {
      if (item.Value == 1) {
        v = item.Key;
      }
    }
    return v;
  }
}
public static class Inventory {
  public static List<GearItem> Gear { set; get; }
  public static List<ConsumeableItem> Consumables { set; get; }
  public static List<QuestItem> Quest { set; get; }
  public static List<GemItem> Gems { set; get; }

}
public static class EquippedItems {
  public static Dictionary<string, GearItem> Base { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Regular Top", gearId = "Base_aa", gearColor = "Brown", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Regular Bottoms", gearId = "Base_aa", gearColor = "Brown", boosts = new List<BoostEntry>() } },
    { "Feet", new GearItem { type = "Normal", name = "Regular Boots", gearId = "Base_aa", gearColor = "Brown", boosts = new List<BoostEntry>() } },
    { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null }, { "Head", null }, { "Ring1", null }, { "Ring2", null },
    { "Ring3", null }, { "Ring4", null }, { "Ring5", null }, { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };
  public static Dictionary<string, GearItem> Aqua { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Wetsuit Top", gearId = "Aqua_aa", gearColor = "Lightblue", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Wetsuit Bottoms", gearId = "Aqua_aa", gearColor = "Lightblue", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Feet", null }, { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null }, { "Ring1", null },
    { "Ring2", null }, { "Ring3", null }, { "Ring4", null }, { "Ring5", null }, { "Ring6", null }, { "Ring7", null }, { "Ring8", null },
    { "Ring9", null }, { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };
  public static Dictionary<string, GearItem> Bolt { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Anti-Static Top", gearId = "Bolt_aa", gearColor = "Grey", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Anti-Static Pants", gearId = "Bolt_aa", gearColor = "Grey", boosts = new List<BoostEntry>() } },
    { "Feet", new GearItem { type = "Normal", name = "Anti-static Boots", gearId = "Bolt_aa", gearColor = "Grey", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null }, { "Ring1", null }, { "Ring2", null },
    { "Ring3", null }, { "Ring4", null }, { "Ring5", null }, { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };
  public static Dictionary<string, GearItem> Cold { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Warm Top", gearId = "Cold_aa", gearColor = "Darkblue", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Warm Bottoms", gearId = "Cold_aa", gearColor = "Darkblue", boosts  = new List<BoostEntry>() } },
    { "Feet", new GearItem { type = "Normal", name = "Warm Footies", gearId = "Cold_aa", gearColor = "Darkblue", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null }, { "Ring1", null }, { "Ring2", null },
    { "Ring3", null }, { "Ring4", null }, { "Ring5", null }, { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };
  public static Dictionary<string, GearItem> Fire { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Sheer Top", gearId = "Fire_aa", gearColor = "Yellow", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Skimmies", gearId = "Fire_aa", gearColor = "Yellow", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Feet", null }, { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null }, { "Ring1", null },
    { "Ring2", null }, { "Ring3", null }, { "Ring4", null }, { "Ring5", null }, { "Ring6", null }, { "Ring7", null }, { "Ring8", null },
    { "Ring9", null }, { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };
  public static Dictionary<string, GearItem> Dark { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Void Shirt", gearId = "Dark_aa", gearColor = "Darkpurple", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Void Pants", gearId = "Dark_aa", gearColor = "Darkpurple", boosts = new List<BoostEntry>() } },
    { "Feet", new GearItem { type = "Normal", name = "Void Footies", gearId = "Dark_aa", gearColor = "Darkpurple", boosts = new List<BoostEntry>() } },
    { "Arms", new GearItem { type = "Normal", name = "Void Gloves", gearId = "Dark_aa", gearColor = "Darkpurple", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Shoulders", null }, { "Belt", null }, { "Zemi", null }, { "Ring1", null }, { "Ring2", null }, { "Ring3", null },
    { "Ring4", null }, { "Ring5", null }, { "Ring6", null }, { "Ring7", null }, { "Ring8", null },
    { "Ring9", null }, { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };
  public static Dictionary<string, Dictionary<string, GearItem>> AllGearForms { get; } = new Dictionary<string, Dictionary<string, GearItem>> {
    { "Base", Base }, { "Aqua", Aqua }, { "Bolt", Bolt }, { "Cold", Cold }, { "Fire", Fire }, { "Dark", Dark }
  };

}