using System.Collections.Generic;

public static class Interrupts {
  public static Dictionary<string, string[]> Esperanza { get; } = new() {
    ["Breathe"] = new[] {
      "Block", "Dance", "Dodge", "Jump", "KickLeft", "KickRight", "PunchLeft", "PunchRight", "Walk", "Run", "Sprint"
    },
    ["Walk"] = new[] {
      "Breathe", "Run", "Sprint", "PunchRight", "PunchLeft", "KickLeft", "KickRight", "Jump", "Dodge", "Block"
    },
    ["Run"] = new[] {
      "Breathe", "Walk", "Sprint", "PunchRight", "PunchLeft", "KickLeft", "KickRight", "Jump", "Dodge", "Block"
    },
    ["Sprint"] = new[] {
      "Breathe", "Walk", "Run", "PunchRight", "PunchLeft", "KickLeft", "KickRight", "Jump", "Dodge", "Block"
    },
    ["Dance"] = new[] {
        "Block", "Dodge", "Jump", "KickRight", "KickLeft", "PunchLeft", "PunchRight", "Run", "Sprint", "Walk"
    },
    ["Block"] = new[] {
      "Stance"
    },
    ["Dodge"] = new[] {
      "Stance"
    },
    ["Jump"] = new[] {
      "JumpDouble", "JumpFalling", "JumpLanding"
    },
    ["JumpDouble"] = new[] {
      "JumpFalling", "JumpLanding"
    },
    ["JumpFalling"] = new[] {
      "JumpLanding"
    },
    ["JumpLanding"] = new[] {
      "Stance"
    },
    ["KickLeft"] = new[] {
      "KickRight", "PunchLeft", "PunchRight", "Stance"
    },
    ["KickRight"] = new[] {
      "KickLeft", "PunchLeft", "PunchRight", "Stance"
    },
    ["PunchLeft"] = new[] {
      "PunchRight", "KickRight", "KickLeft", "Stance"
    },
    ["PunchRight"] = new[] {
      "PunchLeft", "KickLeft", "KickRight", "Stance"
    },
    ["Stance"] = new[] {
      "Walk", "Sprint", "Run", "PunchRight", "PunchLeft", "KickLeft", "KickRight", "Jump", "Dodge", "Breathe", "Block", "Blast"
    },
    ["Blast"] = new[] {
      "Stance"
    }
  };

  public static Dictionary<string, Dictionary<string, string[]>> Enemies { get; } = new() {
    { ImpData.EnemyType, ImpData.Interrupts }
  };
}
