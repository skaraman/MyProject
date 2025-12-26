using System.Collections.Generic;

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
      { "Walk", "StanceToWalk" }, { "Sprint", "StanceToSprint" }, { "Run", "StanceToRun" }, { "PunchRight", "StanceToPunchRight" }, { "PunchLeft", "StanceToPunchLeft" }, { "KickLeft", "StanceToKickLeft" }, { "KickRight", "StanceToKickRight" }, { "Jump", "StanceToJump" }, { "Dodge", "StanceToDodge" }, { "Breathe", "StanceToBreathe" }, { "Block", "StanceToBlock" }, { "SuperBlast", "StanceToSuperBlast" }
    },
    ["SuperBlast"] = new Dictionary<string, string> {
      { "Stance", "SuperBlastToStance" }
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

  public static Dictionary<string, Dictionary<string, string>> LesserDevil { get; } = new Dictionary<string, Dictionary<string, string>> {
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
    { "Imp", Imp },
    { "LesserDevil", LesserDevil }
  };
}