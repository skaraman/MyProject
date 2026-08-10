
using System.Collections.Generic;

public class AnimData {
  public int start;
  public int end;
  public float duration;
  public string category;
  public bool isLocomotion;
  public bool loop;
  public int To;
  public bool pingPong;
  // Plays one forward and reverse leg; duration applies to each leg.
  public bool pingPongOnce;
  public string effect;
  public int effectFrame;
  public string projectile;
  public int projectileFrame;
}

public static class Animations {
  public static Dictionary<string, AnimData> LesserDevil { get; } = new Dictionary<string, AnimData> {
    { "Idle", new AnimData { start = 1, end = 40, duration = 1200, isLocomotion = true, loop = true } },
    { "Run", new AnimData { start = 1, end = 30, duration = 850, isLocomotion = true, loop = true } },
    { "Attack", new AnimData { start = 1, end = 28, duration = 650 } },
    { "Jump", new AnimData { start = 1, end = 40, duration = 1200 } },
    { "Hurt", new AnimData { start = 1, end = 24, duration = 400, pingPongOnce = true } },
    { "BaseDeath1", new AnimData { start = 1, end = 52, duration = 1600, category = "Death" } }
  };


  public static Dictionary<string, Dictionary<string, AnimData>> Enemies { get; } = new Dictionary<string, Dictionary<string, AnimData>> {
    { ImpData.EnemyType, ImpData.Animations },
    { "LesserDevil", LesserDevil }
  };

  public static Dictionary<string, AnimData> Esperanza { get; } = new Dictionary<string, AnimData> {
    { "Breathe", new AnimData { start = 1, end = 92, duration = 1750, isLocomotion = true, pingPong = true } },

    { "Walk", new AnimData { start = 1, end = 65, duration = 1000, isLocomotion = true, loop = true } },
    { "Run", new AnimData { start = 1, end = 45, duration = 700, isLocomotion = true, loop = true } },
    { "Sprint", new AnimData { start = 1, end = 49, duration = 500, isLocomotion = true, loop = true } },

    { "Dance", new AnimData { start = 1, end = 480, duration = 6000, loop = true } },
    { "Block", new AnimData { start = 1, end = 42, duration = 500 } },
    { "Hurt", new AnimData { start = 1, end = 17, duration = 400, pingPongOnce = true } },
    { "Stance", new AnimData { start = 1, end = 59, duration = 1000, isLocomotion = true, pingPong = true } },

    { "KickLeft", new AnimData { start = 1, end = 53, duration = 500 } },
    { "KickRight", new AnimData { start = 1, end = 31, duration = 300 } },
    { "PunchLeft", new AnimData { start = 1, end = 19, duration = 150 } },
    { "PunchRight", new AnimData { start = 1, end = 19, duration = 120 } },
    { "Blast", new AnimData { start = 1, end = 60, duration = 1000, effect = "Blast", projectile = "BlastBall", effectFrame = 1, projectileFrame = 50 } },


    { "Jump", new AnimData { start = 1, end = 41, duration = 300 } },
    { "JumpDouble", new AnimData { start = 1, end = 32, duration = 300 } },
    { "JumpFalling", new AnimData { start = 1, end = 31, duration = 175, loop = true } },
    { "JumpLanding", new AnimData { start = 1, end = 31, duration = 300, isLocomotion = true } },


  };
}
