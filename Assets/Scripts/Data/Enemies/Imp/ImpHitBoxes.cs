using System.Collections.Generic;
using UnityEngine;

public static partial class ImpData {
  public static Dictionary<string, List<HBox>> HurtHitBoxes { get; } = new() {
    ["Run"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(0.12f, -0.28f), new(0.74f, -0.17f), new(1.23f, -0.27f), new(1.28f, -0.81f), new(1.10f, -1.17f), new(1.40f, -1.48f), new(2.27f, -1.98f), new(1.70f, -2.05f), new(0.18f, -2.07f), new(-0.82f, -1.75f), new(-1.18f, -2.51f), new(-1.78f, -2.14f), new(-2.01f, -1.27f), new(-1.32f, -1.28f), new(-1.23f, -0.85f), new(-0.85f, -0.56f) }),
      new(0.17f, new List<Vector2> { new(-0.02f, -0.20f), new(0.74f, -0.17f), new(1.11f, -0.28f), new(1.18f, -0.92f), new(0.88f, -1.17f), new(1.02f, -1.57f), new(1.32f, -2.35f), new(1.02f, -2.40f), new(0.18f, -2.07f), new(-0.44f, -1.74f), new(-1.09f, -2.15f), new(-1.64f, -2.29f), new(-2.01f, -1.48f), new(-1.27f, -1.19f), new(-1.14f, -0.85f), new(-0.85f, -0.56f) }),
      new(0.18f, new List<Vector2> { new(-0.14f, -0.07f), new(0.55f, -0.03f), new(1.11f, -0.18f), new(1.03f, -0.86f), new(0.65f, -1.09f), new(0.43f, -1.54f), new(0.10f, -1.72f), new(-0.08f, -1.96f), new(-0.32f, -2.27f), new(-0.61f, -2.05f), new(-0.98f, -2.11f), new(-1.47f, -1.53f), new(-1.51f, -1.33f), new(-1.11f, -1.07f), new(-1.00f, -0.63f), new(-0.59f, -0.21f) }),
      new(0.36f, new List<Vector2> { new(0.07f, -0.13f), new(0.58f, -0.15f), new(1.11f, -0.18f), new(1.19f, -0.93f), new(1.84f, -1.50f), new(1.29f, -1.71f), new(0.83f, -2.19f), new(0.46f, -2.21f), new(-0.04f, -2.20f), new(-0.50f, -2.07f), new(-0.98f, -2.11f), new(-1.47f, -1.53f), new(-1.14f, -1.27f), new(-1.11f, -1.07f), new(-1.00f, -0.63f), new(-0.45f, -0.24f) }),
      new(0.21f, new List<Vector2> { new(0.12f, -0.28f), new(0.74f, -0.17f), new(1.23f, -0.27f), new(1.28f, -0.81f), new(1.10f, -1.17f), new(1.40f, -1.48f), new(2.27f, -1.98f), new(1.70f, -2.05f), new(0.18f, -2.07f), new(-0.82f, -1.75f), new(-1.18f, -2.51f), new(-1.78f, -2.14f), new(-2.01f, -1.27f), new(-1.32f, -1.28f), new(-1.23f, -0.85f), new(-0.85f, -0.56f) })
    },
    [JumpAnimation] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(0.33f, -0.08f), new(1.24f, -0.21f), new(1.77f, -0.15f), new(1.71f, -0.72f), new(1.42f, -1.14f), new(1.22f, -1.70f), new(1.24f, -2.16f), new(1.32f, -2.97f), new(0.69f, -2.96f), new(0.22f, -2.80f), new(-0.25f, -2.10f), new(-0.28f, -1.53f), new(-0.39f, -1.11f), new(-0.62f, -0.73f), new(-0.67f, -0.40f), new(-0.44f, -0.15f) }),
      new(0.12f, new List<Vector2> { new(0.33f, 0.05f), new(0.73f, -0.08f), new(1.26f, -0.21f), new(1.32f, -0.50f), new(1.22f, -0.84f), new(1.02f, -1.05f), new(0.65f, -1.52f), new(0.22f, -1.94f), new(-0.11f, -2.39f), new(-0.54f, -2.35f), new(-0.72f, -2.13f), new(-1.29f, -1.42f), new(-0.67f, -1.29f), new(-0.86f, -0.62f), new(-0.65f, -0.25f), new(-0.37f, -0.02f) }),
      new(0.1f, new List<Vector2> { new(1.31f, 1.73f), new(1.81f, 1.54f), new(1.85f, 0.64f), new(1.57f, -0.05f), new(1.95f, -0.75f), new(1.19f, -1.23f), new(0.41f, -0.65f), new(0.36f, -1.43f), new(-0.06f, -2.30f), new(-0.44f, -2.35f), new(-0.86f, -2.21f), new(-1.16f, -1.43f), new(-0.72f, -1.01f), new(-0.64f, -0.30f), new(0.29f, 0.85f), new(1.04f, 1.32f) }),
      new(0.39f, new List<Vector2> { new(1.90f, 5.50f), new(2.37f, 5.28f), new(2.13f, 4.47f), new(2.27f, 4.11f), new(3.84f, 3.41f), new(3.56f, 3.14f), new(2.44f, 3.45f), new(1.65f, 3.57f), new(2.08f, 2.67f), new(0.72f, 0.89f), new(-0.21f, 1.18f), new(-0.46f, 1.60f), new(-0.66f, 2.33f), new(-0.45f, 3.23f), new(0.75f, 4.94f), new(1.38f, 5.15f) }),
      new(0.25f, new List<Vector2> { new(1.90f, 5.00f), new(2.45f, 4.85f), new(2.26f, 4.06f), new(2.21f, 3.45f), new(2.80f, 2.51f), new(2.60f, 2.24f), new(1.84f, 2.91f), new(1.29f, 2.18f), new(2.07f, 1.02f), new(1.99f, 0.52f), new(0.86f, 1.08f), new(0.48f, 1.61f), new(-0.10f, 1.82f), new(-0.02f, 2.98f), new(1.02f, 4.44f), new(1.56f, 4.63f) })
    },
    ["Hurt"] = new List<HBox>(),
    ["Death_Base_1"] = new List<HBox>()
  };

  public static Dictionary<string, List<HBox>> OffensiveHitBoxes { get; } = new() {
    [JumpAnimation] = new List<HBox> {
      new(JumpDurationSeconds * 0.8f, NeutralAttackPath()),
      new(JumpDurationSeconds * 0.02f, new List<Vector2> {
        new(-1.4f, -0.25f),
        new(1.4f, -0.25f),
        new(1.6f, -1.8f),
        new(0f, -3.4f),
        new(-1.6f, -1.8f)
      }),
      new(JumpDurationSeconds * 0.18f, NeutralAttackPath())
    }
  };

  public static Dictionary<string, Dictionary<string, List<HBox>>> HitBoxes { get; } = new() {
    ["hurt"] = HurtHitBoxes,
    ["hit1"] = OffensiveHitBoxes
  };

  static List<Vector2> NeutralAttackPath() {
    return new List<Vector2> {
      Vector2.zero,
      Vector2.zero,
      Vector2.zero,
      Vector2.zero,
      Vector2.zero
    };
  }
}
