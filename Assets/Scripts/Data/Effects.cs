
using System.Collections.Generic;

public class EffectData {
  public int start; public int end; public float duration;
}

public static class Effects {
  public static Dictionary<string, EffectData> Esperanza { get; } = new Dictionary<string, EffectData> {
    ["SuperBlast"] = new EffectData { start = 1, end = 120, duration = 2f }
  };
};