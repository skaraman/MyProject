using System.Collections.Generic;

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

    ["Block"] = new List<BounceFrame> { new(-.07f, -.7f, .02f, 1) },
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
    ["PunchRightToStance"] = new List<BounceFrame> { new(-.33f, -.42f) },

    ["SuperBlast"] = new List<BounceFrame> { new(-0.1f, -0.2f, .02f), new(-.13f, -.3f, .18f), new(-.66f, -.73f, .15f), new(1.21f, -1.48f, .23f), new(1.71f, -1.43f, .16f), new(1.7f, -1.31f, .26f) },
    ["SuperBlastToKickLeft"] = new List<BounceFrame> { new(.3f, -.52f) },
    ["SuperBlastToKickRight"] = new List<BounceFrame> { new(.81f, -.37f) },
    ["SuperBlastToPunchLeft"] = new List<BounceFrame> { new(.84f, -.45f) },
    ["SuperBlastToPunchRight"] = new List<BounceFrame> { new(.99f, -.36f) },
    ["SuperBlastToStance"] = new List<BounceFrame> { new(-.14f, -.56f) },
    ["DanceToSuperBlast"] = new List<BounceFrame> { new(-.04f, -.09f) },
    ["PunchLeftToSuperBlast"] = new List<BounceFrame> { new(-.02f, -.26f) },
    ["PunchRightToSuperBlast"] = new List<BounceFrame> { new(.04f, -.21f) },
    ["KickLeftToSuperBlast"] = new List<BounceFrame> { new(-.15f, -.2f) },
    ["KickRightToSuperBlast"] = new List<BounceFrame> { new(-.07f, -.21f) },
    ["RunToSuperBlast"] = new List<BounceFrame> { new(-.03f, -.021f) },
    ["SprintToSuperBlast"] = new List<BounceFrame> { new(-.04f, -.28f) },
    ["StanceToSuperBlast"] = new List<BounceFrame> { new(-.06f, -.28f) },
    ["WalkToSuperBlast"] = new List<BounceFrame> { new(-.02f, -.23f) },

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
