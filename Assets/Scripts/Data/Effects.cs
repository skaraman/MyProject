
using System.Collections.Generic;

public class EffectData {
  public int start; public int end; public float duration;
}

public static class Effects {
  // **** Esperanza Effects
  public static Dictionary<string, EffectData> Esperanza { get; } = new Dictionary<string, EffectData> {
    ["SuperBlast"] = new EffectData { start = 1, end = 90, duration = 1f }
  };

  //**** Things
  public static Dictionary<string, EffectData> Things { get; } = new Dictionary<string, EffectData> {
    ["SuperBlastBall"] = new EffectData { start = 1, end = 1, duration = 1f },
    ["FireballBall"] = new EffectData { start = 1, end = 90, duration = 1.5f }
  };


  // **** Enemies Effects
  public static Dictionary<string, EffectData> Imp { get; } = new Dictionary<string, EffectData> {
    ["Fireball"] = new EffectData { start = 1, end = 60, duration = 1f }
  };
};