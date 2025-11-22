using System;
using System.Collections.Generic;
using UnityEngine;

public static class EsperanzaGearParts {
  public static Dictionary<string, List<string>> gearParts { get; } = new Dictionary<string, List<string>> {
    { "Aqua_no_Head", new List<string> { "Hair" } },
    { "Aqua_aa_Chest", new List<string> { "Torso" } },
    { "Aqua_aa_Legs", new List<string> { "Pelvis", "ThighRight", "ThighLeft" } },
    { "Aqua_ab_Arms", new List<string> { "ArmRight", "ArmLeft", "ForearmLeft", "ForearmRight" } },
    { "Aqua_ab_Chest", new List<string> { "Torso" } },
    { "Aqua_ab_Feet", new List<string> { "FootRight", "FootLeft", "CalfLeft", "CalfRight" } },
    { "Aqua_ab_Head", new List<string> { "Head", "Hair" } },
    { "Aqua_ab_Legs", new List<string> { "Pelvis", "ThighRight", "ThighLeft" } },
    { "Aqua_ab_Shoulders", new List<string> { "Cape" } },
    { "Aqua_ac_Arms", new List<string> { "HandRight", "HandLeft", "ForearmLeft", "ForearmRight" } },
    { "Aqua_ac_Chest", new List<string> { "Torso", "ArmRight", "ArmLeft", "Neck" } },
    { "Aqua_ac_Feet", new List<string> { "FootRight", "FootLeft", "CalfLeft", "CalfRight" } },
    { "Aqua_ac_Head", new List<string> { "Head", "Hair" } },
    { "Aqua_ac_Legs", new List<string> { "Pelvis", "ThighRight", "ThighLeft" } },

    { "Base_no_Head", new List<string> { "Hair", "haB" } },
    { "Base_aa_Feet", new List<string> { "FootLeft", "CalfLeft", "FootRight", "CalfRight" } },
    { "Base_aa_Legs", new List<string> { "ThighRight", "ThighLeft", "Pelvis" } },
    { "Base_aa_Chest", new List<string> { "ArmRight", "ArmLeft", "Torso" } },
    { "Base_ab_Arms", new List<string> { "HandRight", "HandLeft", "ForearmLeft", "ForearmRight" } },
    { "Base_ab_Chest", new List<string> { "Torso", "ArmRight", "ArmLeft", "Neck" } },
    { "Base_ab_Feet", new List<string> { "FootRight", "FootLeft", "CalfLeft", "CalfRight" } },
    { "Base_ab_Head", new List<string> { "Hair" } },
    { "Base_ab_Legs", new List<string> { "Pelvis", "ThighRight", "ThighLeft" } },
    { "Base_ac_Arms", new List<string> { "HandRight", "HandLeft", "ForearmLeft", "ForearmRight" } },
    { "Base_ac_Chest", new List<string> { "Torso", "ArmRight", "ArmLeft" } },
    { "Base_ac_Feet", new List<string> { "FootRight", "FootLeft" } },
    { "Base_ac_Head", new List<string> { "haB", "Hair", "HairLeft" } },
    { "Base_ac_Legs", new List<string> { "Pelvis", "ThighRight", "ThighLeft", "CalfLeft", "CalfRight" } },
    { "Base_ad_Arms", new List<string> { "ArmRight", "ArmLeft", "ForearmLeft", "ForearmRight" } },
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
    { "Bolt_ab_Arms", new List<string> { "ForearmLeft", "ForearmRight" } },
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

public static class Animations {
  public static Dictionary<string, AnimData> Esperanza { get; } = new Dictionary<string, AnimData> {
    { "Breathe", new AnimData { start = 1, end = 92, duration = 1750, pingPong = true } },
    { "Walk", new AnimData { start = 1, end = 65, duration = 1000, loop = true } },
    { "Run", new AnimData { start = 1, end = 45, duration = 700, loop = true } },
    { "Sprint", new AnimData { start = 1, end = 49, duration = 500, loop = true } },
    { "Dance", new AnimData { start = 1, end = 480, duration = 6000, loop = true } },
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
    { "BreatheToWalk", new AnimData { start = 134, end = 144, duration = 175, To = true } },

    { "DanceToBreathe", new AnimData { start = 145, end = 157, duration = 175, To = true } },
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

  public static Dictionary<string, AnimData> Imp { get; } = new Dictionary<string, AnimData> {
    { "Idle", new AnimData { start = 1, end = 46, duration = 1200, loop = true } },
    { "Run", new AnimData { start = 1, end = 46, duration = 1000, loop = true } },
    { "Attack", new AnimData { start = 1, end = 32, duration = 600 } },
    { "Jump", new AnimData { start = 1, end = 196, duration = 1750 } },
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
}

public class HBox {
  public List<Vector2> points = new List<Vector2>();
  public float d;

  public HBox(float d, List<Vector2> points) {
    this.points = points;
    this.d = d;
  }
}

public static class HBoxes {
  public static Dictionary<string, List<HBox>> EsperanzaHurt { get; } = new Dictionary<string, List<HBox>> {
    ["Breathe"] = new List<HBox> {
      new(0.01f, new List<Vector2>{ new(-0.48f, -0.54f), new(-0.25f, 0.24f), new(0.46f, 0.07f), new(0.33f, -0.59f), new(0.69f, -1.00f), new(0.62f, -1.37f), new(1.08f, -2.71f), new(0.71f, -2.88f), new(0.17f, -4.74f), new(0.70f, -5.03f), new(0.68f, -5.26f), new(-0.03f, -5.20f), new(-0.49f, -5.31f), new(-0.49f, -4.79f), new(-0.72f, -2.69f), new(-0.86f, -1.05f)})
    },

    ["Walk"] = new List<HBox> {
      new(.01f, new List<Vector2> {new(0.19f, -0.36f), new(0.38f, 0.22f), new(1.05f, 0.06f), new(0.89f, -0.65f), new(1.17f, -1.05f), new(0.94f, -1.59f), new(0.83f, -2.25f), new(1.02f, -3.60f), new(0.19f, -4.61f), new(0.67f, -5.24f), new(-0.31f, -5.19f), new(-0.47f, -4.71f), new(-0.08f, -3.63f), new(-0.38f, -2.49f), new(-0.27f, -1.43f), new(-0.32f, -0.81f) }),
      new(.29f, new List<Vector2> { new(0.10f, -0.34f), new(0.28f, 0.22f), new(0.91f, -0.07f), new(0.72f, -0.77f), new(0.89f, -1.18f), new(0.88f, -1.59f), new(1.56f, -2.44f), new(0.91f, -3.69f), new(1.68f, -5.22f), new(0.67f, -5.24f), new(-1.11f, -5.30f), new(-1.73f, -4.53f), new(-0.68f, -3.66f), new(-0.70f, -2.61f), new(-0.65f, -1.61f), new(-0.47f, -0.83f) }),
      new(.17f, new List<Vector2> { new(0.13f, -0.47f), new(0.36f, 0.22f), new(1.02f, -0.10f), new(0.82f, -0.73f), new(1.11f, -1.11f), new(0.92f, -1.73f), new(1.48f, -2.32f), new(1.20f, -3.79f), new(1.73f, -5.23f), new(0.70f, -5.15f), new(-0.64f, -5.36f), new(-1.37f, -4.80f), new(-0.51f, -3.78f), new(-0.31f, -2.78f), new(-0.32f, -2.19f), new(-0.20f, -0.87f) }),
      new(.24f, new List<Vector2> { new(0.18f, -0.45f), new(0.31f, 0.25f), new(1.03f, -0.02f), new(0.88f, -0.61f), new(1.08f, -1.03f), new(0.94f, -1.80f), new(1.54f, -2.40f), new(1.29f, -2.62f), new(1.18f, -3.62f), new(1.66f, -5.26f), new(-0.31f, -5.19f), new(-0.62f, -5.44f), new(-1.27f, -4.78f), new(-0.37f, -2.67f), new(-0.35f, -1.37f), new(-0.07f, -0.67f) }),
    },

    ["Run"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(-0.01f, -0.43f), new(0.28f, 0.24f), new(0.98f, 0.07f), new(0.85f, -0.82f), new(0.95f, -1.24f), new(1.24f, -2.17f), new(0.61f, -2.48f), new(0.77f, -3.68f), new(-0.20f, -4.37f), new(-0.02f, -4.87f), new(-0.73f, -5.14f), new(-1.32f, -4.66f), new(-0.78f, -3.41f), new(-0.52f, -2.60f), new(-0.47f, -1.75f), new(-0.52f, -0.86f) }),
      new(0.19f, new List<Vector2> {new(-0.10f, -0.45f), new(0.28f, 0.20f), new(0.98f, -0.06f), new(0.84f, -0.79f), new(1.24f, -0.97f), new(1.94f, -0.76f), new(0.75f, -2.28f), new(1.17f, -3.41f), new(1.66f, -5.08f), new(0.58f, -4.86f), new(0.21f, -3.63f), new(-2.29f, -3.80f), new(-2.35f, -2.87f), new(-0.83f, -3.03f), new(-1.21f, -2.19f), new(-1.14f, -0.93f) }),
      new(0.18f, new List<Vector2> { new(0.06f, -0.43f), new(0.28f, 0.20f), new(0.98f, -0.06f), new(0.75f, -0.77f), new(0.92f, -1.21f), new(0.93f, -1.88f), new(0.55f, -2.69f), new(0.43f, -3.92f), new(0.25f, -5.36f), new(-0.65f, -5.13f), new(-0.67f, -4.88f), new(-0.73f, -4.68f), new(-1.30f, -3.86f), new(-0.44f, -3.21f), new(-0.61f, -2.37f), new(-0.25f, -1.10f) }),
      new(0.2f, new List<Vector2> { new(0.06f, -0.48f), new(0.27f, 0.16f), new(0.94f, 0.08f), new(0.75f, -0.72f), new(1.58f, -0.01f), new(1.93f, -0.25f), new(0.54f, -2.33f), new(1.11f, -3.49f), new(2.16f, -4.79f), new(1.21f, -5.14f), new(0.26f, -3.74f), new(-2.16f, -3.93f), new(-2.10f, -3.06f), new(-0.74f, -2.82f), new(-1.32f, -1.99f), new(-1.28f, -0.47f) } ),
      new(.2f, new List<Vector2> { new(0.13f, -0.44f), new(0.31f, 0.16f), new(0.93f, -0.09f), new(0.73f, -0.72f), new(0.96f, -1.25f), new(0.87f, -1.97f), new(0.54f, -2.33f), new(0.39f, -3.62f), new(0.05f, -4.67f), new(0.35f, -5.15f), new(-0.70f, -4.79f), new(-1.30f, -3.91f), new(-0.40f, -3.23f), new(-0.55f, -2.50f), new(-0.39f, -1.90f), new(-0.30f, -0.76f) })
    },

    ["Sprint"] = new List<HBox> {
      new(0.02f, new List<Vector2> { new(0.46f, -1.77f), new(0.80f, -1.18f), new(1.29f, -1.54f), new(1.00f, -2.14f), new(1.15f, -2.64f), new(0.32f, -2.81f), new(0.43f, -3.60f), new(0.29f, -4.83f), new(0.76f, -5.10f), new(-0.25f, -5.17f), new(-0.67f, -4.17f), new(-2.70f, -4.31f), new(-2.34f, -3.42f), new(-1.27f, -3.48f), new(-1.02f, -2.65f), new(0.11f, -1.62f) }),
      new(0.1f, new List<Vector2> { new(0.46f, -1.87f), new(0.69f, -1.30f), new(1.27f, -1.73f), new(1.03f, -2.31f), new(0.81f, -2.77f), new(1.41f, -3.05f), new(0.64f, -3.71f), new(-0.52f, -4.21f), new(-0.53f, -4.69f), new(-0.99f, -5.32f), new(-1.52f, -5.05f), new(-1.93f, -4.80f), new(-1.10f, -3.67f), new(-0.94f, -2.83f), new(-0.43f, -2.00f), new(0.02f, -1.75f) }),
      new(0.08f, new List<Vector2> { new(0.44f, -1.95f), new(0.59f, -1.34f), new(1.23f, -1.62f), new(0.98f, -2.23f), new(0.77f, -2.60f), new(1.58f, -2.52f), new(0.85f, -3.61f), new(0.39f, -4.40f), new(0.74f, -4.88f), new(-1.00f, -4.54f), new(-2.36f, -4.90f), new(-2.58f, -4.06f), new(-1.43f, -3.53f), new(-1.04f, -2.78f), new(-0.65f, -2.30f), new(-0.01f, -1.83f) }),
      new(0.09f, new List<Vector2> {  new(0.41f, -1.90f), new(0.59f, -1.34f), new(1.15f, -1.55f), new(0.95f, -2.19f), new(0.69f, -2.70f), new(0.88f, -3.32f), new(0.12f, -3.31f), new(0.12f, -4.02f), new(0.21f, -5.22f), new(-0.73f, -5.07f), new(-2.28f, -4.09f), new(-1.94f, -3.23f), new(-1.14f, -3.27f), new(-1.10f, -2.70f), new(-0.80f, -1.73f), new(-0.25f, -1.52f) }),
      new(0.1f, new List<Vector2> { new(0.27f, -1.55f), new(0.59f, -1.12f), new(1.12f, -1.44f), new(0.98f, -1.97f), new(0.71f, -2.49f), new(0.27f, -2.92f), new(0.44f, -3.63f), new(-0.20f, -4.52f), new(0.04f, -5.13f), new(-0.80f, -4.74f), new(-0.98f, -4.48f), new(-2.39f, -5.12f), new(-2.62f, -4.29f), new(-1.45f, -3.85f), new(-0.92f, -2.56f), new(-0.28f, -1.69f) }),
      new(0.1f, new List<Vector2> { new(0.44f, -1.58f), new(0.71f, -1.17f), new(1.21f, -1.42f), new(1.10f, -2.08f), new(1.13f, -2.57f), new(0.23f, -2.90f), new(0.57f, -3.39f), new(0.48f, -4.65f), new(0.94f, -5.09f), new(-0.12f, -5.04f), new(-0.30f, -4.06f), new(-2.82f, -4.40f), new(-2.74f, -3.56f), new(-1.50f, -3.35f), new(-0.84f, -2.35f), new(-0.06f, -1.69f) })

    },
    ["Dance"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(-0.19f, -0.38f), new(-0.23f, 0.30f), new(0.48f, 0.37f), new(0.55f, -0.37f), new(0.81f, -0.88f), new(0.75f, -2.45f), new(0.51f, -2.75f), new(0.38f, -3.82f), new(0.28f, -4.65f), new(0.18f, -4.97f), new(-0.24f, -4.98f), new(-0.55f, -4.91f), new(-0.52f, -4.48f), new(-0.52f, -3.36f), new(-0.67f, -2.11f), new(-0.62f, -0.52f) }),
      new(0.54f, new List<Vector2> { new(-0.58f, -0.84f), new(-0.53f, -0.11f), new(0.23f, -0.12f), new(0.36f, -0.76f), new(0.71f, -1.04f), new(0.65f, -2.15f), new(0.45f, -2.61f), new(0.23f, -3.55f), new(0.03f, -4.36f), new(-0.09f, -4.97f), new(-0.38f, -5.09f), new(-0.74f, -5.00f), new(-0.83f, -4.52f), new(-0.79f, -3.31f), new(-1.60f, -2.58f), new(-0.87f, -1.66f) }),
      new(0.61f, new List<Vector2> { new(-0.32f, -0.58f), new(-0.25f, 0.24f), new(0.46f, 0.07f), new(0.41f, -0.59f), new(0.69f, -1.00f), new(0.73f, -1.47f), new(1.08f, -2.40f), new(0.59f, -2.66f), new(0.42f, -3.55f), new(0.50f, -4.48f), new(0.50f, -4.89f), new(-0.17f, -4.91f), new(-0.97f, -4.71f), new(-0.78f, -3.67f), new(-0.67f, -2.43f), new(-0.86f, -1.05f) }),
      new(1.46f, new List<Vector2> { new(-0.52f, -0.85f), new(-0.55f, -0.20f), new(-0.03f, -0.19f), new(0.10f, -0.71f), new(0.64f, -1.05f), new(0.81f, -1.55f), new(1.68f, -1.48f), new(1.76f, -1.75f), new(0.66f, -1.99f), new(0.44f, -2.70f), new(0.15f, -4.65f), new(-0.17f, -4.91f), new(-0.75f, -4.71f), new(-0.72f, -2.83f), new(-1.69f, -1.59f), new(-1.05f, -1.31f) }),
      new(0.17f, new List<Vector2> { new(-0.41f, -0.72f), new(-0.44f, -0.11f), new(0.15f, -0.03f), new(0.34f, -0.71f), new(0.64f, -1.05f), new(0.61f, -1.57f), new(0.54f, -1.90f), new(0.47f, -2.26f), new(0.49f, -2.76f), new(0.40f, -3.41f), new(0.17f, -4.84f), new(-0.17f, -4.91f), new(-0.75f, -4.71f), new(-0.80f, -2.52f), new(-0.69f, -1.97f), new(-0.91f, -1.11f)}),
      new(0.44f, new List<Vector2> { new(-0.41f, -0.72f), new(-0.42f, -0.09f), new(0.28f, -0.05f), new(0.36f, -0.71f), new(0.81f, -1.22f), new(1.62f, -1.35f), new(0.51f, -1.79f), new(0.52f, -2.47f), new(0.49f, -3.37f), new(0.29f, -4.80f), new(-0.11f, -4.91f), new(-0.52f, -4.76f), new(-0.66f, -2.66f), new(-0.57f, -1.86f), new(-1.49f, -1.30f), new(-0.77f, -1.09f) }),
      new(0.4f, new List<Vector2> { new(-0.62f, -0.17f), new(-0.42f, -0.09f), new(0.28f, -0.05f), new(0.61f, -0.55f), new(1.53f, -0.36f), new(1.25f, -1.26f), new(0.51f, -1.79f), new(0.52f, -2.47f), new(0.34f, -3.60f), new(0.33f, -4.96f), new(-0.20f, -4.97f), new(-0.69f, -4.84f), new(-0.66f, -2.66f), new(-0.57f, -1.86f), new(-1.10f, -1.44f), new(-0.94f, -0.17f) }),
      new(0.31f, new List<Vector2> { new(-0.34f, -0.59f), new(-0.28f, 0.05f), new(0.28f, -0.05f), new(0.45f, -0.54f), new(0.98f, -0.84f), new(1.17f, -1.55f), new(1.05f, -2.43f), new(0.72f, -2.63f), new(0.60f, -3.56f), new(0.44f, -5.05f), new(-0.16f, -5.07f), new(-0.66f, -5.01f), new(-0.68f, -3.70f), new(-0.66f, -2.50f), new(-1.24f, -1.97f), new(-0.84f, -0.89f) }),
      new(0.31f, new List<Vector2> { new(-0.34f, -0.59f), new(-0.28f, 0.05f), new(0.28f, -0.05f), new(0.45f, -0.54f), new(0.98f, -0.84f), new(1.17f, -1.55f), new(1.05f, -2.43f), new(0.72f, -2.63f), new(0.60f, -3.56f), new(0.44f, -5.05f), new(-0.16f, -5.07f), new(-0.66f, -5.01f), new(-0.68f, -3.70f), new(-0.66f, -2.50f), new(-1.24f, -1.97f), new(-0.84f, -0.89f) }),
      new(0.57f, new List<Vector2> { new(-0.39f, -0.19f), new(-0.27f, 0.60f), new(0.43f, 0.52f), new(0.43f, -0.32f), new(0.91f, -0.84f), new(1.12f, -1.53f), new(0.62f, -2.01f), new(0.59f, -2.77f), new(0.73f, -3.65f), new(0.73f, -5.21f), new(-0.28f, -5.19f), new(-1.34f, -5.19f), new(-1.19f, -3.77f), new(-0.85f, -2.17f), new(-1.27f, -0.95f), new(-0.79f, -0.45f) }),
      new(0.19f, new List<Vector2> { new(-0.39f, -0.19f), new(-0.27f, 0.60f), new(0.43f, 0.52f), new(0.43f, -0.32f), new(1.38f, -0.42f), new(1.36f, -1.33f), new(0.65f, -1.68f), new(0.59f, -2.77f), new(0.73f, -3.65f), new(0.88f, -5.31f), new(-0.28f, -5.19f), new(-1.22f, -4.95f), new(-0.92f, -2.36f), new(-2.34f, -1.37f), new(-1.27f, -0.95f), new(-0.79f, -0.45f) }),
      new(0.18f, new List<Vector2> { new(-0.57f, 0.49f), new(-0.27f, 0.60f), new(0.43f, 0.52f), new(0.43f, -0.32f), new(2.24f, 0.11f), new(2.50f, -0.36f), new(0.72f, -1.33f), new(0.59f, -1.91f), new(0.70f, -3.13f), new(0.85f, -5.13f), new(-0.35f, -5.21f), new(-1.19f, -5.04f), new(-0.70f, -2.30f), new(-1.44f, -0.79f), new(-1.82f, 0.54f), new(-1.14f, 0.59f) }),
      new(0.57f, new List<Vector2> { new(-0.66f, -0.20f), new(-0.30f, 0.33f), new(0.37f, 0.20f), new(0.43f, -0.32f), new(1.19f, -0.26f), new(1.58f, -0.45f), new(1.13f, -1.54f), new(0.67f, -1.65f), new(0.61f, -3.33f), new(0.73f, -5.01f), new(-0.14f, -5.00f), new(-0.93f, -4.80f), new(-0.67f, -2.40f), new(-1.18f, -1.43f), new(-1.04f, -0.91f), new(-0.94f, -0.38f) }),
      new(0.22f, new List<Vector2> { new(-0.27f, -0.35f), new(-0.19f, 0.37f), new(0.48f, 0.36f), new(0.43f, -0.41f), new(0.81f, -1.05f), new(0.72f, -2.22f), new(0.83f, -2.58f), new(0.56f, -2.93f), new(0.47f, -4.25f), new(0.35f, -4.90f), new(-0.14f, -4.94f), new(-0.60f, -4.90f), new(-0.69f, -3.60f), new(-0.87f, -2.51f), new(-0.75f, -1.83f), new(-0.79f, -0.80f) }),

    },

    ["Block"] = new List<HBox> {
      new(0.22f, new List<Vector2> { new(-0.30f, -0.92f), new(-0.25f, -0.54f), new(0.40f, -0.55f), new(0.28f, -1.18f), new(0.76f, -1.65f), new(1.22f, -2.67f), new(1.28f, -2.93f), new(1.21f, -3.38f), new(1.54f, -4.57f), new(1.99f, -5.11f), new(-0.31f, -5.15f), new(-1.40f, -5.26f), new(-1.57f, -4.72f), new(-0.93f, -2.65f), new(-1.46f, -1.38f), new(-0.96f, -0.83f) })
    },

    ["Dodge"] = new List<HBox> {
      new(0.02f, new List<Vector2> { new(-0.06f, -0.82f), new(0.16f, -0.46f), new(0.90f, -0.56f), new(0.65f, -1.23f), new(0.90f, -1.57f), new(1.21f, -1.70f), new(1.09f, -2.15f), new(1.25f, -3.35f), new(1.71f, -4.58f), new(1.77f, -4.83f), new(0.71f, -4.83f), new(-1.04f, -5.21f), new(-1.10f, -4.94f), new(-0.76f, -3.58f), new(-0.81f, -2.53f), new(-0.90f, -1.43f) })

    },
    ["Stance"] = new List<HBox> {
      new(0.02f, new List<Vector2> { new(-0.64f, -0.89f), new(-0.55f, -0.26f), new(0.07f, -0.39f), new(0.04f, -1.00f), new(1.23f, -1.10f), new(1.33f, -1.30f), new(0.17f, -2.28f), new(0.66f, -3.35f), new(1.18f, -4.86f), new(1.05f, -5.05f), new(0.18f, -4.96f), new(-1.57f, -5.33f), new(-1.85f, -4.92f), new(-1.21f, -3.46f), new(-1.04f, -2.25f), new(-1.08f, -1.03f) })
    },

    ["Jump"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(-0.31f, -1.07f), new(-0.07f, -0.41f), new(0.54f, -0.52f), new(0.40f, -1.11f), new(0.78f, -1.40f), new(0.77f, -2.06f), new(1.42f, -2.68f), new(0.71f, -2.97f), new(1.00f, -5.12f), new(0.86f, -5.20f), new(-0.11f, -4.93f), new(-0.93f, -5.36f), new(-1.19f, -5.19f), new(-0.61f, -3.67f), new(-1.14f, -2.21f), new(-0.68f, -1.42f) }),
      new(0.05f, new List<Vector2> {new(-0.18f, -1.83f), new(0.03f, -1.15f), new(0.58f, -1.37f), new(0.58f, -1.90f), new(0.90f, -2.91f), new(1.52f, -3.14f), new(1.19f, -3.55f), new(1.02f, -4.11f), new(1.21f, -4.79f), new(0.76f, -5.04f), new(0.27f, -5.19f), new(-0.61f, -5.38f), new(-1.09f, -5.31f), new(-0.63f, -4.21f), new(-1.21f, -3.11f), new(-0.56f, -2.03f) }),
      new(0.08f, new List<Vector2> {new(-0.27f, -0.98f), new(-0.12f, -0.35f), new(0.46f, -0.40f), new(0.44f, -1.06f), new(0.74f, -1.66f), new(1.31f, -1.50f), new(0.62f, -2.46f), new(0.64f, -3.56f), new(0.34f, -4.45f), new(0.31f, -5.05f), new(0.18f, -5.23f), new(-0.16f, -5.14f), new(-0.24f, -4.11f), new(-0.48f, -2.76f), new(-0.92f, -2.00f), new(-0.81f, -1.38f) }),
      new(0.16f, new List<Vector2> { new(-0.08f, -1.47f), new(0.06f, -1.07f), new(0.73f, -1.28f), new(0.61f, -1.81f), new(0.70f, -2.26f), new(1.14f, -2.53f), new(0.66f, -2.86f), new(0.66f, -3.29f), new(0.57f, -4.10f), new(-0.13f, -4.82f), new(-0.12f, -5.23f), new(-0.46f, -5.19f), new(-0.67f, -4.06f), new(-0.89f, -2.92f), new(-1.04f, -2.10f), new(-0.64f, -1.59f) }),
      new(0.1f, new List<Vector2> {new(-0.02f, -1.63f), new(0.31f, -1.27f), new(0.89f, -1.64f), new(0.64f, -2.15f), new(0.61f, -2.44f), new(0.92f, -2.53f), new(0.76f, -2.92f), new(0.77f, -3.19f), new(0.80f, -3.60f), new(-0.24f, -4.31f), new(-0.10f, -4.68f), new(-0.62f, -4.72f), new(-0.88f, -4.00f), new(-0.97f, -2.99f), new(-1.01f, -2.03f), new(-0.51f, -1.69f) })

    },
    ["JumpDouble"] = new List<HBox> {
      new(0.01f, new List<Vector2> {new(-0.37f, -0.82f), new(-0.26f, -0.28f), new(0.40f, -0.51f), new(0.26f, -1.03f), new(0.48f, -1.44f), new(0.88f, -1.85f), new(0.88f, -2.78f), new(0.16f, -3.80f), new(0.23f, -4.14f), new(-0.01f, -4.22f), new(-0.15f, -5.17f), new(-0.55f, -5.17f), new(-0.67f, -4.36f), new(-0.70f, -3.54f), new(-1.33f, -1.49f), new(-0.84f, -0.89f)}),
      new(0.3f, new List<Vector2> { new(-0.64f, -0.08f), new(-0.35f, -0.29f), new(0.07f, -0.44f), new(0.59f, -0.40f), new(0.57f, -1.45f), new(-0.02f, -2.07f), new(0.22f, -2.68f), new(0.41f, -3.62f), new(0.36f, -4.02f), new(-0.40f, -4.69f), new(-0.43f, -4.96f), new(-0.66f, -4.96f), new(-1.01f, -4.13f), new(-1.09f, -2.79f), new(-1.33f, -1.13f), new(-0.95f, -0.24f) }),
    },
    ["JumpFalling"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(-0.85f, 0.37f), new(-0.36f, 0.21f), new(0.08f, 0.14f), new(0.42f, 0.16f), new(0.85f, -1.04f), new(0.03f, -1.98f), new(0.28f, -2.81f), new(0.39f, -3.85f), new(0.29f, -4.19f), new(0.03f, -4.38f), new(-0.33f, -5.09f), new(-0.66f, -5.12f), new(-0.97f, -4.11f), new(-0.83f, -3.35f), new(-1.09f, -2.53f), new(-1.17f, -0.96f)}),
    },
    ["JumpLanding"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(-0.70f, 0.41f), new(-0.26f, 0.33f), new(0.09f, 0.26f), new(0.48f, 0.27f), new(0.73f, -0.89f), new(-0.03f, -1.73f), new(0.29f, -2.50f), new(0.40f, -3.59f), new(0.29f, -3.97f), new(-0.31f, -4.66f), new(-0.32f, -4.93f), new(-0.57f, -4.96f), new(-0.85f, -4.21f), new(-0.74f, -3.36f), new(-1.02f, -2.49f), new(-1.11f, -0.43f) }),
      new(0.34f, new List<Vector2> { new(-0.53f, -1.64f), new(-0.40f, -1.09f), new(0.33f, -1.12f), new(0.26f, -1.83f), new(0.49f, -2.34f), new(1.34f, -3.10f), new(0.66f, -3.52f), new(0.82f, -4.06f), new(0.82f, -4.83f), new(0.13f, -5.09f), new(-0.44f, -4.92f), new(-1.19f, -5.35f), new(-1.52f, -5.20f), new(-0.89f, -4.07f), new(-1.65f, -3.54f), new(-0.90f, -1.93f) }),
      new(0.15f, new List<Vector2> { new(-0.59f, -1.00f), new(-0.53f, -0.45f), new(0.07f, -0.39f), new(0.06f, -1.01f), new(0.67f, -2.44f), new(1.05f, -2.81f), new(0.48f, -3.08f), new(0.48f, -4.08f), new(0.82f, -4.99f), new(0.28f, -5.19f), new(-0.44f, -4.92f), new(-1.10f, -5.22f), new(-1.52f, -5.24f), new(-1.19f, -3.75f), new(-1.76f, -2.57f), new(-0.99f, -1.23f) })
    },
    ["KickLeft"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(-0.19f, -0.86f), new(0.13f, -0.31f), new(0.73f, -0.51f), new(0.43f, -1.05f), new(1.38f, -1.21f), new(1.66f, -1.42f), new(0.67f, -3.04f), new(1.21f, -3.85f), new(0.81f, -4.93f), new(0.46f, -5.03f), new(-0.39f, -4.62f), new(-1.37f, -4.94f), new(-1.58f, -4.65f), new(-1.04f, -3.57f), new(-0.66f, -2.55f), new(-0.61f, -1.05f) }),
      new(0.15f, new List<Vector2> { new(-0.51f, -1.19f), new(-0.73f, -0.55f), new(-0.04f, -0.40f), new(0.33f, 0.10f), new(0.73f, -0.06f), new(1.04f, -0.72f), new(0.73f, -2.44f), new(2.07f, -3.29f), new(2.43f, -5.12f), new(2.09f, -5.24f), new(0.69f, -4.53f), new(-1.37f, -4.94f), new(-1.85f, -4.28f), new(-0.65f, -3.60f), new(-0.19f, -2.57f), new(-0.72f, -1.68f) }),
      new(0.14f, new List<Vector2> { new(-0.73f, -0.55f), new(-0.79f, 0.08f), new(-0.10f, -0.03f), new(-0.06f, -0.60f), new(2.06f, -0.06f), new(2.15f, -0.48f), new(0.81f, -1.51f), new(3.35f, -1.58f), new(2.89f, -2.20f), new(0.79f, -2.61f), new(0.63f, -5.14f), new(-0.06f, -5.22f), new(-0.15f, -4.58f), new(-0.19f, -2.27f), new(-1.05f, -1.72f), new(-1.42f, -1.02f) }),
      new(0.08f, new List<Vector2> { new(-1.50f, -0.64f), new(-1.22f, -0.10f), new(-0.66f, -0.22f), new(-0.19f, -0.44f), new(-0.07f, -1.52f), new(1.05f, -1.04f), new(2.80f, -1.95f), new(2.82f, -2.17f), new(1.01f, -1.80f), new(0.04f, -2.44f), new(-0.31f, -5.18f), new(-0.94f, -5.37f), new(-0.87f, -4.39f), new(-0.98f, -2.14f), new(-1.63f, -1.57f), new(-1.87f, -1.06f) }),
      new(0.12f, new List<Vector2> { new(-1.30f, -0.63f), new(-1.18f, -0.01f), new(-0.51f, -0.07f), new(-0.63f, -0.82f), new(1.27f, -1.01f), new(0.02f, -1.96f), new(0.94f, -2.88f), new(1.19f, -4.79f), new(0.48f, -4.33f), new(-0.27f, -3.13f), new(-1.13f, -5.25f), new(-1.65f, -5.35f), new(-1.38f, -3.77f), new(-1.06f, -2.20f), new(-1.69f, -1.54f), new(-1.82f, -0.93f) })
    },
    ["KickRight"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(0.33f, -0.46f), new(0.53f, -0.02f), new(1.19f, -0.17f), new(1.08f, -0.67f), new(2.33f, -0.19f), new(0.81f, -2.07f), new(1.86f, -3.19f), new(2.17f, -4.82f), new(1.45f, -4.84f), new(0.56f, -3.25f), new(-1.02f, -5.24f), new(-1.51f, -5.27f), new(-1.45f, -4.62f), new(-0.48f, -3.10f), new(-0.06f, -2.24f), new(0.01f, -1.17f) }),
      new(0.09f, new List<Vector2> { new(-0.60f, -0.65f), new(-0.45f, -0.05f), new(0.14f, -0.20f), new(0.20f, -0.68f), new(1.43f, -0.11f), new(0.16f, -2.05f), new(0.94f, -2.88f), new(0.60f, -4.57f), new(1.01f, -4.91f), new(0.28f, -5.03f), new(0.16f, -4.31f), new(-0.05f, -3.46f), new(-0.74f, -2.82f), new(-1.10f, -1.93f), new(-1.03f, -1.27f), new(-0.81f, -0.85f) }),
      new(0.09f, new List<Vector2> { new(-0.86f, -0.82f), new(-0.76f, -0.23f), new(-0.11f, -0.40f), new(-0.17f, -1.04f), new(0.12f, -1.64f), new(0.87f, -2.10f), new(2.23f, -2.55f), new(2.22f, -2.95f), new(1.22f, -2.98f), new(-0.10f, -3.31f), new(-0.10f, -5.00f), new(-0.88f, -5.00f), new(-0.99f, -3.73f), new(-1.03f, -2.48f), new(-1.37f, -1.91f), new(-1.28f, -1.23f) }),
      new(0.11f, new List<Vector2> { new(-0.16f, -0.57f), new(0.02f, -0.08f), new(0.56f, -0.34f), new(0.48f, -1.03f), new(1.53f, -1.87f), new(1.19f, -2.16f), new(0.53f, -2.42f), new(0.96f, -3.58f), new(0.71f, -4.40f), new(0.85f, -4.88f), new(0.35f, -4.73f), new(-0.33f, -4.74f), new(-1.16f, -4.87f), new(-0.85f, -2.80f), new(-0.62f, -1.97f), new(-0.66f, -1.06f) })
    },
    ["PunchLeft"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(0.44f, -0.66f), new(0.55f, -0.08f), new(1.21f, -0.25f), new(1.18f, -0.68f), new(1.92f, -0.76f), new(1.53f, -1.92f), new(0.81f, -2.23f), new(1.57f, -3.31f), new(1.33f, -4.61f), new(1.71f, -4.94f), new(0.10f, -4.78f), new(-1.40f, -5.32f), new(-1.61f, -4.91f), new(-0.80f, -3.79f), new(-0.09f, -1.94f), new(-0.08f, -1.01f) }),
      new(0.13f, new List<Vector2> { new(0.40f, -0.75f), new(0.63f, -0.14f), new(1.29f, -0.43f), new(1.35f, -0.88f), new(2.74f, -0.82f), new(1.28f, -1.51f), new(1.04f, -2.31f), new(1.84f, -3.29f), new(2.14f, -4.62f), new(2.61f, -4.90f), new(0.10f, -4.78f), new(-1.43f, -5.25f), new(-1.64f, -4.80f), new(-0.65f, -3.72f), new(-0.02f, -1.85f), new(-0.08f, -0.96f) }),
      new(0.05f, new List<Vector2> { new(0.29f, -0.70f), new(0.44f, -0.24f), new(0.98f, -0.34f), new(1.19f, -0.88f), new(2.34f, -0.82f), new(1.28f, -1.51f), new(0.77f, -2.23f), new(1.58f, -3.10f), new(1.65f, -4.51f), new(2.18f, -4.87f), new(0.10f, -4.78f), new(-1.43f, -5.25f), new(-1.64f, -4.80f), new(-0.70f, -3.70f), new(-0.15f, -1.88f), new(-0.21f, -1.02f) })
    },
    ["PunchRight"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(0.66f, -0.57f), new(0.72f, -0.06f), new(1.39f, -0.20f), new(1.81f, -0.71f), new(1.46f, -1.70f), new(1.05f, -1.75f), new(1.12f, -2.57f), new(1.81f, -3.07f), new(1.97f, -4.43f), new(2.60f, -4.77f), new(0.31f, -4.77f), new(-1.33f, -5.26f), new(-1.55f, -4.80f), new(-0.48f, -3.71f), new(0.09f, -2.06f), new(0.06f, -0.84f) }),
      new(0.06f, new List<Vector2> { new(0.58f, -0.65f), new(0.78f, -0.01f), new(1.35f, -0.17f), new(1.34f, -0.80f), new(2.84f, -0.71f), new(2.79f, -1.10f), new(1.34f, -1.41f), new(0.97f, -2.40f), new(1.70f, -3.11f), new(2.26f, -4.82f), new(1.33f, -4.84f), new(-1.02f, -5.26f), new(-1.43f, -4.83f), new(-0.34f, -3.86f), new(-0.09f, -2.58f), new(-0.04f, -1.06f) }),
      new(0.08f, new List<Vector2> { new(0.61f, -0.70f), new(0.88f, -0.07f), new(1.47f, -0.25f), new(1.37f, -0.90f), new(2.72f, -0.88f), new(2.74f, -1.29f), new(1.45f, -1.53f), new(1.34f, -2.60f), new(1.85f, -3.08f), new(2.20f, -4.79f), new(1.41f, -4.85f), new(-1.18f, -5.27f), new(-1.49f, -4.73f), new(-0.46f, -3.83f), new(-0.04f, -2.38f), new(0.22f, -0.95f) })
    },

    //     ["WalkToSprint"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["WalkToRun"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["WalkToPunchRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["WalkToPunchLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["WalkToKickLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["WalkToKickRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["WalkToJump"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["WalkToDodge"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["WalkToBreathe"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["WalkToBlock"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },

    //     ["RunToSprint"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["RunToWalk"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["RunToPunchRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["RunToPunchLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["RunToKickLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["RunToKickRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["RunToJump"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["RunToDodge"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["RunToBreathe"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["RunToBlock"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },

    //     ["SprintToWalk"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["SprintToRun"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["SprintToPunchRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["SprintToPunchLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["SprintToKickLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["SprintToKickRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["SprintToJump"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["SprintToDodge"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["SprintToBreathe"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["SprintToBlock"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },

    //     ["StanceToWalk"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["StanceToSprint"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["StanceToRun"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["StanceToPunchRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["StanceToPunchLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["StanceToKickLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["StanceToKickRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["StanceToJump"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["StanceToDodge"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["StanceToBreathe"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["StanceToBlock"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },

    //     ["BlockToStance"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["DodgeToStance"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },

    //     ["BreatheToBlock"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["BreatheToDance"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["BreatheToDodge"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["BreatheToJump"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["BreatheToKickLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["BreatheToKickRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["BreatheToPunchLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["BreatheToPunchRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["BreatheToRun"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["BreatheToSprint"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["BreatheToWalk"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },

    //     ["DanceToBreathe"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["DanceToBlock"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["DanceToDodge"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["DanceToJump"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["DanceToKickRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["DanceToKickLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["DanceToPunchLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["DanceToPunchRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["DanceToRun"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["DanceToSprint"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["DanceToWalk"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },

    //     ["JumpToJumpDouble"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["JumpToJumpFalling"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["JumpToJumpLanding"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["JumpDoubleToJumpFalling"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["JumpDoubleToJumpLanding"] = new List<HBox> { new(0.22f, 0, 0, 0, 0) },

    //     ["JumpFallingToJumpLanding"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["JumpLandingToStance"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },

    //     ["KickLeftToKickRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["KickLeftToPunchLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["KickLeftToPunchRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["KickLeftToStance"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },

    //     ["KickRightToKickLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["KickRightToPunchLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["KickRightToPunchRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["KickRightToStance"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },

    //     ["PunchLeftToPunchRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["PunchLeftToKickRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["PunchLeftToKickLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["PunchLeftToStance"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["PunchRightToPunchLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["PunchRightToKickLeft"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["PunchRightToKickRight"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },
    //     ["PunchRightToStance"] = new List<HBox> {  new(0.01f, new List<Vector2> {}), },

  };

  public static Dictionary<string, List<HBox>> EsperanzaHit1 { get; } = new Dictionary<string, List<HBox>> {
    ["Breathe"] = new List<HBox> {
      new(.01f, new List<Vector2>{ new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)})
    },
    ["Walk"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)})
    },
    ["Run"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)})
    },
    ["Sprint"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)})
    },
    ["Dance"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)})
    },
    ["Stance"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)})
    },
    ["Sprint"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)})
    },
    ["Jump"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)})
    },
    ["JumpDouble"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)})
    },
    ["JumpFalling"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)})
    },
    ["JumpLanding"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)})
    },
    ["PunchLeft"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0) }),
      new(.08f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0) }),
      new(.01f, new List<Vector2> { new(1.98f, -0.60f), new(1.89f, -0.99f), new(2.18f, -1.06f), new(2.32f, -0.84f), new(2.21f, -0.60f) }),
      new(.035f, new List<Vector2> { new(2.40f, -0.68f), new(2.45f, -1.04f), new(2.73f, -1.03f), new(2.92f, -0.91f), new(2.76f, -0.58f) })
    },
    ["PunchRight"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)}),
      new(.02f, new List<Vector2> { new(1.53f, -0.67f), new(1.59f, -1.00f), new(1.81f, -1.08f), new(2.07f, -0.77f), new(1.85f, -0.53f) }),
      new(.02f, new List<Vector2> { new(2.35f, -0.73f), new(2.39f, -1.13f), new(2.79f, -1.11f), new(2.92f, -0.77f), new(2.71f, -0.63f) }),
    },
    ["KickLeft"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0) }),
      new(.26f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0) }),
      new(.01f, new List<Vector2> { new(2.19f, -1.97f), new(1.88f, -2.10f), new(2.09f, -2.58f), new(2.39f, -2.54f), new(2.57f, -2.19f) }),
      new(.02f, new List<Vector2> { new(3.07f, -1.41f), new(2.51f, -1.62f), new(2.41f, -2.04f), new(2.79f, -2.10f), new(3.43f, -1.62f) }),
      new(.05f, new List<Vector2> { new(3.14f, -1.07f), new(2.26f, -0.93f), new(2.22f, -1.60f), new(2.86f, -1.48f), new(3.27f, -1.34f) }),
      new(.08f, new List<Vector2> { new(1.75f, -2.93f), new(1.75f, -2.93f), new(1.75f, -2.93f), new(1.75f, -2.93f), new(1.75f, -2.93f) })
    },
    ["KickRight"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0) }),
      new(.12f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0) }),
      new(.01f, new List<Vector2> { new(1.94f, -2.90f), new(1.36f, -2.71f), new(1.03f, -3.06f), new(1.26f, -3.27f), new(1.68f, -3.31f) }),
      new(.14f, new List<Vector2> { new(1.06f, -4.33f), new(0.71f, -4.02f), new(0.33f, -4.40f), new(0.69f, -4.78f), new(1.16f, -4.69f) }),
      new(.01f, new List<Vector2> { new(0.50f, -4.90f), new(0.50f, -4.90f), new(0.50f, -4.90f), new(0.50f, -4.90f), new(0.50f, -4.90f) })
    }
  };

  public static Dictionary<string, Dictionary<string, List<HBox>>> Esperanza = new Dictionary<string, Dictionary<string, List<HBox>>> {
    { "hurt", EsperanzaHurt }, { "hit1", EsperanzaHit1 }
  };

  public static Dictionary<string, List<HBox>> ImpHurt { get; } = new Dictionary<string, List<HBox>> {
    ["Run"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(-0.01f, -0.43f), new(0.28f, 0.24f), new(0.98f, 0.07f), new(0.85f, -0.82f), new(0.95f, -1.24f), new(1.24f, -2.17f), new(0.61f, -2.48f), new(0.77f, -3.68f), new(-0.20f, -4.37f), new(-0.02f, -4.87f), new(-0.73f, -5.14f), new(-1.32f, -4.66f), new(-0.78f, -3.41f), new(-0.52f, -2.60f), new(-0.47f, -1.75f), new(-0.52f, -0.86f) }),
      new(0.19f, new List<Vector2> {new(-0.10f, -0.45f), new(0.28f, 0.20f), new(0.98f, -0.06f), new(0.84f, -0.79f), new(1.24f, -0.97f), new(1.94f, -0.76f), new(0.75f, -2.28f), new(1.17f, -3.41f), new(1.66f, -5.08f), new(0.58f, -4.86f), new(0.21f, -3.63f), new(-2.29f, -3.80f), new(-2.35f, -2.87f), new(-0.83f, -3.03f), new(-1.21f, -2.19f), new(-1.14f, -0.93f) }),
      new(0.18f, new List<Vector2> { new(0.06f, -0.43f), new(0.28f, 0.20f), new(0.98f, -0.06f), new(0.75f, -0.77f), new(0.92f, -1.21f), new(0.93f, -1.88f), new(0.55f, -2.69f), new(0.43f, -3.92f), new(0.25f, -5.36f), new(-0.65f, -5.13f), new(-0.67f, -4.88f), new(-0.73f, -4.68f), new(-1.30f, -3.86f), new(-0.44f, -3.21f), new(-0.61f, -2.37f), new(-0.25f, -1.10f) }),
      new(0.2f, new List<Vector2> { new(0.06f, -0.48f), new(0.27f, 0.16f), new(0.94f, 0.08f), new(0.75f, -0.72f), new(1.58f, -0.01f), new(1.93f, -0.25f), new(0.54f, -2.33f), new(1.11f, -3.49f), new(2.16f, -4.79f), new(1.21f, -5.14f), new(0.26f, -3.74f), new(-2.16f, -3.93f), new(-2.10f, -3.06f), new(-0.74f, -2.82f), new(-1.32f, -1.99f), new(-1.28f, -0.47f) } ),
      new(.2f, new List<Vector2> { new(0.13f, -0.44f), new(0.31f, 0.16f), new(0.93f, -0.09f), new(0.73f, -0.72f), new(0.96f, -1.25f), new(0.87f, -1.97f), new(0.54f, -2.33f), new(0.39f, -3.62f), new(0.05f, -4.67f), new(0.35f, -5.15f), new(-0.70f, -4.79f), new(-1.30f, -3.91f), new(-0.40f, -3.23f), new(-0.55f, -2.50f), new(-0.39f, -1.90f), new(-0.30f, -0.76f) })
    },
    ["Jump"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(-0.31f, -1.07f), new(-0.07f, -0.41f), new(0.54f, -0.52f), new(0.40f, -1.11f), new(0.78f, -1.40f), new(0.77f, -2.06f), new(1.42f, -2.68f), new(0.71f, -2.97f), new(1.00f, -5.12f), new(0.86f, -5.20f), new(-0.11f, -4.93f), new(-0.93f, -5.36f), new(-1.19f, -5.19f), new(-0.61f, -3.67f), new(-1.14f, -2.21f), new(-0.68f, -1.42f) }),
      new(0.05f, new List<Vector2> {new(-0.18f, -1.83f), new(0.03f, -1.15f), new(0.58f, -1.37f), new(0.58f, -1.90f), new(0.90f, -2.91f), new(1.52f, -3.14f), new(1.19f, -3.55f), new(1.02f, -4.11f), new(1.21f, -4.79f), new(0.76f, -5.04f), new(0.27f, -5.19f), new(-0.61f, -5.38f), new(-1.09f, -5.31f), new(-0.63f, -4.21f), new(-1.21f, -3.11f), new(-0.56f, -2.03f) }),
      new(0.08f, new List<Vector2> {new(-0.27f, -0.98f), new(-0.12f, -0.35f), new(0.46f, -0.40f), new(0.44f, -1.06f), new(0.74f, -1.66f), new(1.31f, -1.50f), new(0.62f, -2.46f), new(0.64f, -3.56f), new(0.34f, -4.45f), new(0.31f, -5.05f), new(0.18f, -5.23f), new(-0.16f, -5.14f), new(-0.24f, -4.11f), new(-0.48f, -2.76f), new(-0.92f, -2.00f), new(-0.81f, -1.38f) }),
      new(0.16f, new List<Vector2> { new(-0.08f, -1.47f), new(0.06f, -1.07f), new(0.73f, -1.28f), new(0.61f, -1.81f), new(0.70f, -2.26f), new(1.14f, -2.53f), new(0.66f, -2.86f), new(0.66f, -3.29f), new(0.57f, -4.10f), new(-0.13f, -4.82f), new(-0.12f, -5.23f), new(-0.46f, -5.19f), new(-0.67f, -4.06f), new(-0.89f, -2.92f), new(-1.04f, -2.10f), new(-0.64f, -1.59f) }),
      new(0.1f, new List<Vector2> {new(-0.02f, -1.63f), new(0.31f, -1.27f), new(0.89f, -1.64f), new(0.64f, -2.15f), new(0.61f, -2.44f), new(0.92f, -2.53f), new(0.76f, -2.92f), new(0.77f, -3.19f), new(0.80f, -3.60f), new(-0.24f, -4.31f), new(-0.10f, -4.68f), new(-0.62f, -4.72f), new(-0.88f, -4.00f), new(-0.97f, -2.99f), new(-1.01f, -2.03f), new(-0.51f, -1.69f) })
    },
    ["Idle"] = new List<HBox> {},
    ["Attack"] = new List<HBox> {},
    ["Hurt"] = new List<HBox> {},
    ["Death"] = new List<HBox> {},
    

  };

  public static Dictionary<string, List<HBox>> ImpHit1 { get; } = new Dictionary<string, List<HBox>> {
   ["Run"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(-0.01f, -0.43f), new(0.28f, 0.24f), new(0.98f, 0.07f), new(0.85f, -0.82f), new(0.95f, -1.24f), new(1.24f, -2.17f), new(0.61f, -2.48f), new(0.77f, -3.68f), new(-0.20f, -4.37f), new(-0.02f, -4.87f), new(-0.73f, -5.14f), new(-1.32f, -4.66f), new(-0.78f, -3.41f), new(-0.52f, -2.60f), new(-0.47f, -1.75f), new(-0.52f, -0.86f) }),
      new(0.19f, new List<Vector2> {new(-0.10f, -0.45f), new(0.28f, 0.20f), new(0.98f, -0.06f), new(0.84f, -0.79f), new(1.24f, -0.97f), new(1.94f, -0.76f), new(0.75f, -2.28f), new(1.17f, -3.41f), new(1.66f, -5.08f), new(0.58f, -4.86f), new(0.21f, -3.63f), new(-2.29f, -3.80f), new(-2.35f, -2.87f), new(-0.83f, -3.03f), new(-1.21f, -2.19f), new(-1.14f, -0.93f) }),
      new(0.18f, new List<Vector2> { new(0.06f, -0.43f), new(0.28f, 0.20f), new(0.98f, -0.06f), new(0.75f, -0.77f), new(0.92f, -1.21f), new(0.93f, -1.88f), new(0.55f, -2.69f), new(0.43f, -3.92f), new(0.25f, -5.36f), new(-0.65f, -5.13f), new(-0.67f, -4.88f), new(-0.73f, -4.68f), new(-1.30f, -3.86f), new(-0.44f, -3.21f), new(-0.61f, -2.37f), new(-0.25f, -1.10f) }),
      new(0.2f, new List<Vector2> { new(0.06f, -0.48f), new(0.27f, 0.16f), new(0.94f, 0.08f), new(0.75f, -0.72f), new(1.58f, -0.01f), new(1.93f, -0.25f), new(0.54f, -2.33f), new(1.11f, -3.49f), new(2.16f, -4.79f), new(1.21f, -5.14f), new(0.26f, -3.74f), new(-2.16f, -3.93f), new(-2.10f, -3.06f), new(-0.74f, -2.82f), new(-1.32f, -1.99f), new(-1.28f, -0.47f) } ),
      new(.2f, new List<Vector2> { new(0.13f, -0.44f), new(0.31f, 0.16f), new(0.93f, -0.09f), new(0.73f, -0.72f), new(0.96f, -1.25f), new(0.87f, -1.97f), new(0.54f, -2.33f), new(0.39f, -3.62f), new(0.05f, -4.67f), new(0.35f, -5.15f), new(-0.70f, -4.79f), new(-1.30f, -3.91f), new(-0.40f, -3.23f), new(-0.55f, -2.50f), new(-0.39f, -1.90f), new(-0.30f, -0.76f) })
    },
    ["Jump"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(-0.31f, -1.07f), new(-0.07f, -0.41f), new(0.54f, -0.52f), new(0.40f, -1.11f), new(0.78f, -1.40f), new(0.77f, -2.06f), new(1.42f, -2.68f), new(0.71f, -2.97f), new(1.00f, -5.12f), new(0.86f, -5.20f), new(-0.11f, -4.93f), new(-0.93f, -5.36f), new(-1.19f, -5.19f), new(-0.61f, -3.67f), new(-1.14f, -2.21f), new(-0.68f, -1.42f) }),
      new(0.05f, new List<Vector2> {new(-0.18f, -1.83f), new(0.03f, -1.15f), new(0.58f, -1.37f), new(0.58f, -1.90f), new(0.90f, -2.91f), new(1.52f, -3.14f), new(1.19f, -3.55f), new(1.02f, -4.11f), new(1.21f, -4.79f), new(0.76f, -5.04f), new(0.27f, -5.19f), new(-0.61f, -5.38f), new(-1.09f, -5.31f), new(-0.63f, -4.21f), new(-1.21f, -3.11f), new(-0.56f, -2.03f) }),
      new(0.08f, new List<Vector2> {new(-0.27f, -0.98f), new(-0.12f, -0.35f), new(0.46f, -0.40f), new(0.44f, -1.06f), new(0.74f, -1.66f), new(1.31f, -1.50f), new(0.62f, -2.46f), new(0.64f, -3.56f), new(0.34f, -4.45f), new(0.31f, -5.05f), new(0.18f, -5.23f), new(-0.16f, -5.14f), new(-0.24f, -4.11f), new(-0.48f, -2.76f), new(-0.92f, -2.00f), new(-0.81f, -1.38f) }),
      new(0.16f, new List<Vector2> { new(-0.08f, -1.47f), new(0.06f, -1.07f), new(0.73f, -1.28f), new(0.61f, -1.81f), new(0.70f, -2.26f), new(1.14f, -2.53f), new(0.66f, -2.86f), new(0.66f, -3.29f), new(0.57f, -4.10f), new(-0.13f, -4.82f), new(-0.12f, -5.23f), new(-0.46f, -5.19f), new(-0.67f, -4.06f), new(-0.89f, -2.92f), new(-1.04f, -2.10f), new(-0.64f, -1.59f) }),
      new(0.1f, new List<Vector2> {new(-0.02f, -1.63f), new(0.31f, -1.27f), new(0.89f, -1.64f), new(0.64f, -2.15f), new(0.61f, -2.44f), new(0.92f, -2.53f), new(0.76f, -2.92f), new(0.77f, -3.19f), new(0.80f, -3.60f), new(-0.24f, -4.31f), new(-0.10f, -4.68f), new(-0.62f, -4.72f), new(-0.88f, -4.00f), new(-0.97f, -2.99f), new(-1.01f, -2.03f), new(-0.51f, -1.69f) })
    },
    ["Idle"] = new List<HBox> {},
    ["Attack"] = new List<HBox> {},
    ["Hurt"] = new List<HBox> {},
    ["Death"] = new List<HBox> {},
  };

  public static Dictionary<string, Dictionary<string, List<HBox>>> Imp = new Dictionary<string, Dictionary<string, List<HBox>>> {
    { "hurt", ImpHurt }, { "hit1", ImpHit1 }
  };

  public static Dictionary<string, Dictionary<string, Dictionary<string, List<HBox>>>> Enemies { get; } = new Dictionary<string, Dictionary<string, Dictionary<string, List<HBox>>>> {
    { "Imp", Imp }
  };
}

public class BounceFrame {
  public float x;
  public float y;
  public float offset;
  public float duration;

  public BounceFrame(float x, float y, float duration = 0.17f, float offset = 1) {
    this.x = x;
    this.y = y;
    this.offset = offset;
    this.duration = duration;
  }
}

public static class BounceAdjustments {
  public static Dictionary<string, List<BounceFrame>> EsperHair { get; } = new Dictionary<string, List<BounceFrame>> {
    ["Breathe"] = new List<BounceFrame> { new(0, 0) },
    ["BreatheToWalk"] = new List<BounceFrame> { new(.6f, 0) },
    ["BreatheToRun"] = new List<BounceFrame> { new(.4f, -.05f) },
    ["BreatheToSprint"] = new List<BounceFrame> { new(.81f, -1.33f) },
    ["BreatheToBlock"] = new List<BounceFrame> { new(-.13f, -.67f) },
    ["BreatheToDance"] = new List<BounceFrame> { new(.49f, .12f) },
    ["BreatheToDodge"] = new List<BounceFrame> { new(.33f, -.61f) },
    ["BreatheToJump"] = new List<BounceFrame> { new(.12f, -.61f) },
    ["BreatheToKickLeft"] = new List<BounceFrame> { new(.67f, -.35f) },
    ["BreatheToKickRight"] = new List<BounceFrame> { new(-.17f, -.45f) },
    ["BreatheToPunchLeft"] = new List<BounceFrame> { new(.77f, -.27f) },
    ["BreatheToPunchRight"] = new List<BounceFrame> { new(.92f, -.23f) },

    ["Walk"] = new List<BounceFrame> { new(0.63f, 0.01f, .01f), new(.4f, -.05f, .4f), new(.66f, -.05f, .6f) },
    ["WalkToBreathe"] = new List<BounceFrame> { new(0, 0) },
    ["WalkToRun"] = new List<BounceFrame> { new(.5f, -.03f) },
    ["WalkToSprint"] = new List<BounceFrame> { new(.7f, -1.2f) },
    ["WalkToPunchRight"] = new List<BounceFrame> { new(.91f, -.16f) },
    ["WalkToPunchLeft"] = new List<BounceFrame> { new(.77f, -.33f) },
    ["WalkToKickLeft"] = new List<BounceFrame> { new(.2f, -.4f) },
    ["WalkToKickRight"] = new List<BounceFrame> { new(-.81f, -.33f) },
    ["WalkToJump"] = new List<BounceFrame> { new(.11f, -.74f) },
    ["WalkToDodge"] = new List<BounceFrame> { new(.33f, -.58f) },
    ["WalkToBlock"] = new List<BounceFrame> { new(.04f, -.59f) },

    ["Run"] = new List<BounceFrame> { new(.44f, -.04f, .17f), new(.46f, -.09f, .17f), new(.44f, -.01f, .17f), new(.46f, -.09f, .17f) },
    ["RunToBreathe"] = new List<BounceFrame> { new(0, 0) },
    ["RunToWalk"] = new List<BounceFrame> { new(.5f, -.03f) },
    ["RunToSprint"] = new List<BounceFrame> { new(.7f, -1.2f) },
    ["RunToPunchRight"] = new List<BounceFrame> { new(.83f, -.2f) },
    ["RunToPunchLeft"] = new List<BounceFrame> { new(.84f, -.32f) },
    ["RunToKickLeft"] = new List<BounceFrame> { new(.15f, -.36f) },
    ["RunToKickRight"] = new List<BounceFrame> { new(.7f, -.27f) },
    ["RunToJump"] = new List<BounceFrame> { new(.06f, -.62f) },
    ["RunToDodge"] = new List<BounceFrame> { new(.3f, -.66f) },
    ["RunToBlock"] = new List<BounceFrame> { new(-.18f, -.52f) },

    ["Sprint"] = new List<BounceFrame> { new(.83f, -1.36f, .01f, 1), new(.85f, -1.6f, .05f, 1),
        new(.74f, -1.69f, .09f, 1), new(.66f, -1.51f, .11f, 1), new(.77f, -1.62f, .06f, 1),
        new(.66f, -1.36f, .07f, 1), new(.77f, -1.5f, .06f, 1), new(.83f, -1.36f, .03f, 1) },
    ["SprintToBreathe"] = new List<BounceFrame> { new(0, 0) },
    ["SprintToWalk"] = new List<BounceFrame> { new(.6f, -.06f) },
    ["SprintToRun"] = new List<BounceFrame> { new(.4f, -.15f) },
    ["SprintToPunchRight"] = new List<BounceFrame> { new(.85f, -.22f) },
    ["SprintToPunchLeft"] = new List<BounceFrame> { new(.73f, -.34f) },
    ["SprintToKickLeft"] = new List<BounceFrame> { new(.16f, -.37f) },
    ["SprintToKickRight"] = new List<BounceFrame> { new(.74f, -.3f) },
    ["SprintToJump"] = new List<BounceFrame> { new(.04f, -.59f) },
    ["SprintToDodge"] = new List<BounceFrame> { new(.32f, -.57f) },
    ["SprintToBlock"] = new List<BounceFrame> { new(.39f, -.98f) },

    ["Dance"] = new List<BounceFrame> {
      new(.26f, .25f, .03f, -1), new(.44f, -.04f, .1f, -1), new(.42f, -.14f, .1f, -1), // 25
      new(.03f, -.09f, .01f, 1), new(-.24f, .35f, .11f, 1), new(-.47f, -.23f, .15f, 1), // 52
      new(-.03f, -.24f, .01f, -1), new(.15f, .27f, .17f, -1), new(.29f, -.3f, .18f, -1), // 88
      new(.03f, .29f, .14f, -1), new(-.11f, -.24f, .17f, -1), new(.16f, .16f, .19f, -1), // 138
      new(.28f, -.04f, .13f, -1), new(.1f, -.12f, .19f, -1), new(.23f, 0, .09f, -1), // 179
      new(.24f, -.15f, .15f, -1), new(.08f, -.17f, .16f, -1), new(.21f, -.15f, .07f, -1), // 220
      new(.02f, -.21f, .01f, 1), new(-.05f, -.28f, .14f, 1), new(.2f, -.21f, .01f, -1), // 239
      new(.02f, -.06f, .08f, -1), // 246
      new(.07f, -.05f, .03f, -1), new(-.12f, -.33f, .11f, -1), new(.02f, -.1f, .13f, -1), //273
      new(-.38f, -.05f, .01f, 1), new(-.15f, -.07f, .1f, 1), new(-.11f, -.21f, .04f, 1), // 288
      new(-.24f, -.01f, .08f, 1), new(.01f, -.08f, .01f, -1), new(0, -.14f, .11f, -1), // 313
      new(.12f, -.09f, .05f, -1), new(.19f, -.3f, .16f, -1), new(-.01f, -.03f, .16f, -1), // 350
      new(-.14f, 0.01f, .06f, -1), new(-.16f, -.18f, .06f, -1), new(.03f, .12f, .19f, -1), // 381
      new(.19f, -.08f, .09f, -1), new(-.04f, -.26f, .01f, 1), new(-.02f, -.22f, .11f, 1), // 405
      new(-.09f, .05f, .1f, 1), new(.11f, .01f, .01f, -1), new(.14f, -.02f, .11f, -1), // 427
      new(.28f, .12f, .11f, -1), new(-.02f, .14f, .01f, 1), new(-.04f, 0, .09f, 1), // 448
      new(-.04f, 0, .06f, 1), new(.14f, .1f, .01f, -1), new(-.01f, .23f, .07f, -1), // 462
      new(-.07f, .14f, .07f, -1), new(.09f, .37f, .08f, -1), new(.26f, .22f, .08f, -1), // 485
      new(.06f, .42f, .14f, -1), new(.12f, .3f, .04f, -1), new(-.07f, .23f, .05f, -1), // 508
      new(.08f, .28f, .07f, -1), new(.17f, .17f, .05f, -1), new(.09f, .26f, .1f, -1), // 530
      new(-.02f, .14f, .1f, -1), new(.1f, .06f, .12f, -1), new(.23f, .01f, .07f, -1), // 
      new(.12f, .11f, .09f, -1), new(0, -.02f, .07f, -1), new(.023f, -.13f, .12f, -1), //
      new(.16f, .19f, .06f, -1) // 
    },
    ["DanceToBlock"] = new List<BounceFrame> { new(.167f, -.66f, .17f, -1) },
    ["DanceToBreathe"] = new List<BounceFrame> { new(.1f, -.04f, .17f, -1) },
    ["DanceToDodge"] = new List<BounceFrame> { new(.55f, -.43f, .17f, -1) },
    ["DanceToJump"] = new List<BounceFrame> { new(.34f, -.57f, .17f, -1) },
    ["DanceToKickRight"] = new List<BounceFrame> { new(.83f, .07f, .17f, -1) },
    ["DanceToKickLeft"] = new List<BounceFrame> { new(.44f, -.34f, .17f, -1) },
    ["DanceToPunchLeft"] = new List<BounceFrame> { new(1.0f, -.33f, .17f, -1) },
    ["DanceToPunchRight"] = new List<BounceFrame> { new(1.07f, -.15f, .17f, -1) },
    ["DanceToRun"] = new List<BounceFrame> { new(.63f, -.03f, .17f, -1) },
    ["DanceToSprint"] = new List<BounceFrame> { new(.97f, -1.34f, .17f, -1) },
    ["DanceToWalk"] = new List<BounceFrame> { new(.84f, -.01f, .17f, -1) },

    ["Dodge"] = new List<BounceFrame> { new(.31f, -.56f, .01f, 1), new(.42f, -.74f, .24f, 1) },
    ["DodgeToStance"] = new List<BounceFrame> { new(-.32f, -.41f) },

    ["Block"] = new List<BounceFrame> { new(.16f, .19f, .02f, 1) },
    ["BlockToStance"] = new List<BounceFrame> { new(-.18f, -.59f) },

    ["Stance"] = new List<BounceFrame> { new(-.3f, -.43f) },
    ["StanceToBlock"] = new List<BounceFrame> { new(-.08f, -.59f) },
    ["StanceToWalk"] = new List<BounceFrame> { new(.69f, 0f) },
    ["StanceToSprint"] = new List<BounceFrame> { new(.81f, -1.38f) },
    ["StanceToRun"] = new List<BounceFrame> { new(-.46f, -.07f) },
    ["StanceToPunchRight"] = new List<BounceFrame> { new(.87f, -.15f) },
    ["StanceToPunchLeft"] = new List<BounceFrame> { new(.82f, -.38f) },
    ["StanceToKickLeft"] = new List<BounceFrame> { new(.27f, -.26f) },
    ["StanceToKickRight"] = new List<BounceFrame> { new(.73f, -.23f) },
    ["StanceToJump"] = new List<BounceFrame> { new(.1f, -.55f) },
    ["StanceToDodge"] = new List<BounceFrame> { new(.35f, -.59f) },
    ["StanceToBreathe"] = new List<BounceFrame> { new(.03f, .03f) },

    ["Jump"] = new List<BounceFrame> { new(.08f, -.72f, .01f, 1), new(.16f,-1.32f, .06f, 1),
    new(.03f, -.53f, .06f, 1), new(.2f, -1.21f, .13f, 1), new(.46f, -1.6f, .17f, 1)  },
    ["JumpDouble"] = new List<BounceFrame> { new(-.01f, -.54f, .01f, 1), new(.14f, -1.25f, .18f, 1),
      new(.13f, -1.23f, .09f, 1), new(-.28f, -.75f, .02f, 1)  },
    ["JumpFalling"] = new List<BounceFrame> { new(-.38f, -.62f, .01f, 1), new(-.32f, -.48f, .05f, 1),
      new(-.32f, -.48f, .09f, 1),  },
    ["JumpLanding"] = new List<BounceFrame> { new(-.22f, -.48f, .01f, 1), new(-.39f, -.7f, .2f, 1),
    new(-.14f, -1.33f, .14f, 1), new(-.27f, -.69f,  .15f, 1), new(-.32f, -.58f, .02f, 1) },
    ["JumpToJumpDouble"] = new List<BounceFrame> { new(.02f, -.58f) },
    ["JumpToJumpFalling"] = new List<BounceFrame> { new(-.34f, -.6f) },
    ["JumpToJumpLanding"] = new List<BounceFrame> { new(-.15f, -.45f) },
    ["JumpDoubleToJumpFalling"] = new List<BounceFrame> { new(-.34f, -.61f) },
    ["JumpDoubleToJumpLanding"] = new List<BounceFrame> { new(-.2f, -.49f) },
    ["JumpFallingToJumpLanding"] = new List<BounceFrame> { new(-.17f, -.45f) },
    ["JumpLandingToStance"] = new List<BounceFrame> { new(-.31f, -.45f) },

    ["KickLeft"] = new List<BounceFrame> { new(.2f, -.44f, .01f, 1), new(.32f, -.64f, .06f, 1),
      new(-.06f, -.47f, .07f, 1), new(-.66f, -.67f, .08f, 1), new(-.29f, -.39f, .04f, 1),
      new(-.71f, -.14f, .04f, 1), new(-1.19f, -.47f, .05f, 1), new(-1.16f, -.21f, .03f, 1),
      new(-1, -.11f, .13f,1) },
    ["KickRight"] = new List<BounceFrame> { new(.42f, -.29f, .01f, 1), new(-.49f, -.35f, .13f, 1),
      new(-.63f, -.71f, .1f, 1), new(.17f, -.3f, .06f, 1)},
    ["PunchLeft"] = new List<BounceFrame> { new(.86f, -.31f, .01f, 1), new(1.15f, -.26f, .05f, 1),
      new(.67f, -.41f, .09f, 1), new(.62f, -.4f, .02f, 1) },
    ["PunchRight"] = new List<BounceFrame> { new(.95f, -.17f, .01f, 1), new(.97f, -.12f, .02f, 1),
      new(.99f, -.14f, .02f, 1), new(.91f, -.21f, .04f, 1), new(1.1f, -.34f, .05f, 1),
      new(1.08f, -.33f, .01f, 1)  },

    ["KickLeftToKickRight"] = new List<BounceFrame> { new(.74f, -.26f) },
    ["KickLeftToPunchLeft"] = new List<BounceFrame> { new(.78f, -.26f) },
    ["KickLeftToPunchRight"] = new List<BounceFrame> { new(1f, -.14f) },
    ["KickLeftToStance"] = new List<BounceFrame> { new(-.31f, -.5f) },
    ["KickRightToKickLeft"] = new List<BounceFrame> { new(.28f, -.38f) },
    ["KickRightToPunchLeft"] = new List<BounceFrame> { new(.82f, -.28f) },
    ["KickRightToPunchRight"] = new List<BounceFrame> { new(.94f, -.23f) },
    ["KickRightToStance"] = new List<BounceFrame> { new(-.31f, -.47f) },

    ["PunchLeftToPunchRight"] = new List<BounceFrame> { new(.92f, -.2f) },
    ["PunchLeftToKickRight"] = new List<BounceFrame> { new(.74f, -.21f) },
    ["PunchLeftToKickLeft"] = new List<BounceFrame> { new(.42f, -.35f) },
    ["PunchLeftToStance"] = new List<BounceFrame> { new(-.26f, -.45f) },
    ["PunchRightToPunchLeft"] = new List<BounceFrame> { new(.81f, -.3f) },
    ["PunchRightToKickLeft"] = new List<BounceFrame> { new(.18f, -.39f) },
    ["PunchRightToKickRight"] = new List<BounceFrame> { new(.75f, -.28f) },
    ["PunchRightToStance"] = new List<BounceFrame> { new(-.33f, -.42f) }
  };

  public static Dictionary<string, Dictionary<string, List<BounceFrame>>> Esperanza { get; } = new Dictionary<string, Dictionary<string, List<BounceFrame>>> {
    ["HairRight"] = EsperHair,
    ["Hair"] = EsperHair,
    ["HairBack"] = EsperHair,
    ["HairLeft"] = EsperHair,
    ["BeltFlap"] = EsperHair,
    ["FlapFront"] = EsperHair,
    ["FlapRight"] = EsperHair,
    ["FlapLeft"] = EsperHair,
    ["Cape"] = EsperHair
  };
}

public static class Interrupts {
  public static Dictionary<string, Dictionary<string, string>> Esperanza { get; } = new Dictionary<string, Dictionary<string, string>> {
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
      { "Block", "DanceToBlock" }, { "Dodge", "DanceToDodge" }, { "Jump", "DanceToJump" }, { "KickLeft", "DanceToKickLeft" }, { "KickRight", "DanceToKickRight" }, { "PunchLeft", "DanceToPunchLeft" }, { "PunchRight", "DanceToPunchRight" }, { "Walk", "DanceToWalk" }, { "Run", "DanceToRun" }, { "Sprint", "DanceToSprint" }
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
      { "Breathe", "SprintToBreathe" }, { "Walk", "SprintToWalk" }, { "Run", "SprintToRun" }, { "PunchRight", "SprintToPunchRight" }, { "PunchLeft", "SprintToPunchLeft" }, { "KickLeft", "SprintToKickLeft" }, { "KickRight", "SprintToKickRight" }, { "Jump", "SprintToJump" }, { "Dodge", "SprintToDodge" }, { "Block", "SprintToBlock" },
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
        { "Block", "DanceToBlock" }, { "Dodge", "DanceToDodge" }, { "Jump", "DanceToJump" }, { "KickRight", "DanceToKickRight" }, { "KickLeft", "DanceToKickLeft" }, { "PunchLeft", "DanceToPunchLeft" }, { "PunchRight", "DanceToPunchRight" }, { "Run", "DanceToRun" }, { "Sprint", "DanceToSprint" }, { "Walk", "DanceToWalk" },
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

  public static Dictionary<string, Dictionary<string, string>> Imp { get; } = new Dictionary<string, Dictionary<string, string>> {
    ["Idle"] = new Dictionary<string, string> {
      { "Run", "Run" }, { "Attack", "Attack" }, { "Hurt", "Hurt" }, { "Jump", "Jump" }, { "Death", "Death" }
    },
    ["Run"] = new Dictionary<string, string> {
      { "Idle", "Idle" }, { "Attack", "Attack" }, { "Hurt", "Hurt" }, { "Jump", "Jump" }, { "Death", "Death" }
    },
    ["Attack"] = new Dictionary<string, string> {
      { "Hurt", "Hurt" }, { "Death", "Death" }
    },
    ["Hurt"] = new Dictionary<string, string> {
      {"Hurt", "Hurt"}, { "Death", "Death" }
    },
    ["Jump"] = new Dictionary<string, string> {
      { "Hurt", "Hurt" },
    },
    ["Death"] = new Dictionary<string, string>()
  };

  public static Dictionary<string, Dictionary<string, Dictionary<string, string>>> Enemies { get; } = new Dictionary<string, Dictionary<string, Dictionary<string, string>>> {
    { "Imp", Imp }
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
    { "AFT", "After Effect" }, { "EVD", "Evade" }, { "CLN", "Cleanse" }, { "FEAR", "Fear" }, { "SPEC", "Spectral" }, { "PEN", "Penetration" },
    { "MVSP", "Movement Speed" }, { "RK", "Right Kick" }, { "LK", "Left Kick" }, { "RP", "Right Punch" }, { "LP", "Left Punch" },
    { "BK", "Block" }, { "DO", "Dodge" }, { "JP", "Jump" }, { "SP", "Super Punch" }, { "SK", "Super Kick" }, { "SH", "Shock" },
    { "CL", "Chain Lighting" }, { "ST", "Static" }, { "LB", "Lightning Bolt" }, { "ID", "Instant Dodge" }, { "DD", "Double Dodge" },
    { "DJ", "Double Jump" }, { "TB", "Thunder Bolt" }, { "OR", "Orbit" }, { "FT", "Flamethrower" }, { "BW", "Burning Wall" }, { "BZ", "Blaze" },
    { "PL", "Pyre Light" }, { "FS", "Flame Shield" }, { "BD", "Burning Dodge" }, { "FW", "Flame Wings" }, { "MT", "Meteor" }, { "FI", "Fissure" },
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
      ["suffix"] = new List<string> { "Fortitude", "Vitality", "Mettle", "Withstanding", "Forbearance", "Endurance", },
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
      ["suffix"] = new List<string> { "Snapping", },
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
    ["CLN"] = new Dictionary<string, List<string>> {
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

  public static void UnlockForm(string v) {
    if (Unlocked.ContainsKey(v)) {
      Unlocked[v] = 1;
    }
  }
}

public static class Inventory {
  public static List<GearItem> Gear { set; get; }
  public static List<ConsumableItem> Consumables { set; get; }
  public static List<QuestItem> Quest { set; get; }
  public static List<GemItem> Gems { set; get; }
}

public static class EquippedItems {
  public static Dictionary<string, GearItem> Base { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Regular Top", gearId = "Base_aa", gearColor = "Brown", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Regular Bottoms", gearId = "Base_aa", gearColor = "Brown", boosts = new List<BoostEntry>() } },
    { "Feet", new GearItem { type = "Normal", name = "Regular Boots", gearId = "Base_aa", gearColor = "Brown", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null },
    { "Ring1", null }, { "Ring2", null }, { "Ring3", null }, { "Ring4", null }, { "Ring5", null },
    { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };

  public static Dictionary<string, GearItem> Aqua { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Wetsuit Top", gearId = "Aqua_aa", gearColor = "LightBlue", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Wetsuit Bottoms", gearId = "Aqua_aa", gearColor = "LightBlue", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Feet", null }, { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null },
    { "Ring1", null }, { "Ring2", null }, { "Ring3", null }, { "Ring4", null }, { "Ring5", null },
    { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };

  public static Dictionary<string, GearItem> Bolt { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Anti-Static Top", gearId = "Bolt_aa", gearColor = "Grey", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Anti-Static Pants", gearId = "Bolt_aa", gearColor = "Grey", boosts = new List<BoostEntry>() } },
    { "Feet", new GearItem { type = "Normal", name = "Anti-static Boots", gearId = "Bolt_aa", gearColor = "Grey", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null },
    { "Ring1", null }, { "Ring2", null }, { "Ring3", null }, { "Ring4", null }, { "Ring5", null },
    { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };

  public static Dictionary<string, GearItem> Cold { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Warm Top", gearId = "Cold_aa", gearColor = "DarkBlue", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Warm Bottoms", gearId = "Cold_aa", gearColor = "DarkBlue", boosts  = new List<BoostEntry>() } },
    { "Feet", new GearItem { type = "Normal", name = "Warm Footies", gearId = "Cold_aa", gearColor = "DarkBlue", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null },
    { "Ring1", null }, { "Ring2", null }, { "Ring3", null }, { "Ring4", null }, { "Ring5", null },
    { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };

  public static Dictionary<string, GearItem> Fire { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Sheer Top", gearId = "Fire_aa", gearColor = "Yellow", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Skimmies", gearId = "Fire_aa", gearColor = "Yellow", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Feet", null }, { "Shoulders", null }, { "Arms", null }, { "Belt", null }, { "Zemi", null },
    { "Ring1", null }, { "Ring2", null }, { "Ring3", null }, { "Ring4", null }, { "Ring5", null },
    { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };

  public static Dictionary<string, GearItem> Dark { set; get; } = new Dictionary<string, GearItem> {
    { "Chest", new GearItem { type = "Normal", name = "Void Shirt", gearId = "Dark_aa", gearColor = "DarkPurple", boosts = new List<BoostEntry>() } },
    { "Legs", new GearItem { type = "Normal", name = "Void Pants", gearId = "Dark_aa", gearColor = "DarkPurple", boosts = new List<BoostEntry>() } },
    { "Feet", new GearItem { type = "Normal", name = "Void Footies", gearId = "Dark_aa", gearColor = "DarkPurple", boosts = new List<BoostEntry>() } },
    { "Arms", new GearItem { type = "Normal", name = "Void Gloves", gearId = "Dark_aa", gearColor = "DarkPurple", boosts = new List<BoostEntry>() } },
    { "Head", null }, { "Shoulders", null }, { "Belt", null }, { "Zemi", null },
    { "Ring1", null }, { "Ring2", null }, { "Ring3", null }, { "Ring4", null }, { "Ring5", null },
    { "Ring6", null }, { "Ring7", null }, { "Ring8", null }, { "Ring9", null },
    { "Ring10", null }, { "Ring11", null }, { "Ring12", null }
  };

  public static Dictionary<string, Dictionary<string, GearItem>> AllGearForms { get; } = new Dictionary<string, Dictionary<string, GearItem>> {
    { "Base", Base }, { "Aqua", Aqua }, { "Bolt", Bolt }, { "Cold", Cold }, { "Fire", Fire }, { "Dark", Dark }
  };

}

public static class AllStatValues {
  public static Dictionary<string, float> Esperanza { set; get; } = new Dictionary<string, float> {
    { "DMG", 0 }, { "DCHC", 0 }, { "HP", 0 }, { "AS", 0 }, { "NRGRG", 0 }, { "CDMG", 0 }, { "NRG", 0 }, { "HPRG", 0 }, { "ARM", 0 }, { "HEAL", 0 },
    { "CCHC", 0 }, { "LDMG", 0 }, { "LCHC", 0 }, { "DDMG", 0 }, { "BONUS", 0 }, { "MVSP", 0 }, { "AKSP", 0 }, { "CDST", 0 }, { "LDSC", 0 }, { "FDMG", 0 },
    { "AREA", 0 }, { "DUR", 0 }, { "AFT", 0 }, { "EVD", 0 }, { "CLN", 0 }, { "FEAR", 0 }, { "SPEC", 0 }, { "PEN", 0 }
  };

  public static Dictionary<string, float> Imp { set; get; } = new Dictionary<string, float> {
    { "DMG", 1 }, { "HP", 10 }, { "AS", 1 }, { "HPRG", 0 }, { "ARM", 0 },  { "BONUS", 0 }, { "MVSP", 1 }, { "AKSP", 1 }, { "CDST", 1 }, { "EVD", 0 }, { "FEAR", 0 }, { "SPEC", 0 }, { "PEN", 0 }
  };

  public static Dictionary<string, Dictionary<string, float>> Enemies { get; } = new Dictionary<string, Dictionary<string, float>> {
    { "Imp", Imp }
  };

}

public class LocationInfo {
  public string name;
  public List<string> enemies;
  public int maxEnemies;
  public float spawnInterval;
  public int finalKillCount;

  public LocationInfo(string name, List<string> enemies, int maxEnemies, float spawnInterval, int finalKillCount) {
    this.name = name;
    this.enemies = enemies;
    this.maxEnemies = maxEnemies;
    this.spawnInterval = spawnInterval;
    this.finalKillCount = finalKillCount;
  }

}

public static class LocationEnemyData {
  public static Dictionary<string, LocationInfo> zones { get; } = new Dictionary<string, LocationInfo> {
    { "DomeCity", new LocationInfo("DomeCity", new List<string> { "Imp" }, 1, 2.0f, 3) },
  };

  public static Dictionary<string, int> totalKills { get; } = new Dictionary<string, int> {
    { "Imp", 0 },
  };
}


