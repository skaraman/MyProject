using System.Collections.Generic;

public class BounceFrame {
  public float duration;
  public float offset;

  public BounceFrame(float duration = 0.17f, float offset = 1f) {
    this.duration = duration;
    this.offset = offset;
  }
}

public static class BounceAdjustments {
  public static Dictionary<string, List<BounceFrame>> EsperHair { get; } = new Dictionary<string, List<BounceFrame>> {
    ["Dance"] = new List<BounceFrame> {
      new(.03f, -1f), new(.1f, -1f), new(.1f, -1f), // 25
      new(.01f, 1f), new(.11f, 1f), new(.15f, 1f), // 52
      new(.01f, -1f), new(.17f, -1f), new(.18f, -1f), // 88
      new(.14f, -1f), new(.17f, -1f), new(.19f, -1f), // 138
      new(.13f, -1f), new(.19f, -1f), new(.09f, -1f), // 179
      new(.15f, -1f), new(.16f, -1f), new(.07f, -1f), // 220
      new(.01f, 1f), new(.14f, 1f), new(.01f, -1f), // 239
      new(.08f, -1f), // 246
      new(.03f, -1f), new(.11f, -1f), new(.13f, -1f), //273
      new(.01f, 1f), new(.1f, 1f), new(.04f, 1f), // 288
      new(.08f, 1f), new(.01f, -1f), new(.11f, -1f), // 313
      new(.05f, -1f), new(.16f, -1f), new(.16f, -1f), // 350
      new(.06f, -1f), new(.06f, -1f), new(.19f, -1f), // 381
      new(.09f, -1f), new(.01f, 1f), new(.11f, 1f), // 405
      new(.1f, 1f), new(.01f, -1f), new(.11f, -1f), // 427
      new(.11f, -1f), new(.01f, 1f), new(.09f, 1f), // 448
      new(.06f, 1f), new(.01f, -1f), new(.07f, -1f), // 462
      new(.07f, -1f), new(.08f, -1f), new(.08f, -1f), // 485
      new(.14f, -1f), new(.04f, -1f), new(.05f, -1f), // 508
      new(.07f, -1f), new(.05f, -1f), new(.1f, -1f), // 530
      new(.1f, -1f), new(.12f, -1f), new(.07f, -1f), //
      new(.09f, -1f), new(.07f, -1f), new(.12f, -1f), //
      new(.06f, -1f) //
    },
    ["DanceToBlock"] = new List<BounceFrame> { new(.17f, -1f) },
    ["DanceToBreathe"] = new List<BounceFrame> { new(.17f, -1f) },
    ["DanceToDodge"] = new List<BounceFrame> { new(.17f, -1f) },
    ["DanceToJump"] = new List<BounceFrame> { new(.17f, -1f) },
    ["DanceToKickRight"] = new List<BounceFrame> { new(.17f, -1f) },
    ["DanceToKickLeft"] = new List<BounceFrame> { new(.17f, -1f) },
    ["DanceToPunchLeft"] = new List<BounceFrame> { new(.17f, -1f) },
    ["DanceToPunchRight"] = new List<BounceFrame> { new(.17f, -1f) },
    ["DanceToRun"] = new List<BounceFrame> { new(.17f, -1f) },
    ["DanceToSprint"] = new List<BounceFrame> { new(.17f, -1f) },
    ["DanceToWalk"] = new List<BounceFrame> { new(.17f, -1f) },


  };

  public static Dictionary<string, Dictionary<string, List<BounceFrame>>> Esperanza { get; } = new Dictionary<string, Dictionary<string, List<BounceFrame>>> {
    ["HairRight"] = EsperHair,
    ["Hair"] = EsperHair,
    ["HairBack"] = EsperHair,
    ["HairLeft"] = EsperHair,
    // ["BeltFlap"] = null,
    // ["FlapFront"] = null,
    // ["FlapRight"] = null,
    // ["FlapLeft"] = null,
    // ["Cape"] = null
  };
}
