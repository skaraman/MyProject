using System.Collections.Generic;
using UnityEngine;

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
    ["SuperBlast"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(-0.48f, -0.54f), new(-0.25f, 0.24f), new(0.46f, 0.07f), new(0.33f, -0.59f), new(0.69f, -1.00f), new(1.60f, -1.14f), new(0.80f, -2.16f), new(1.41f, -3.34f), new(1.99f, -5.06f), new(1.73f, -5.29f), new(1.08f, -5.11f), new(0.11f, -3.58f), new(-0.80f, -5.39f), new(-1.29f, -5.40f), new(-0.86f, -2.97f), new(-0.92f, -0.91f) }),
      new(0.25f, new List<Vector2> { new(-0.92f, -0.97f), new(-0.66f, -0.11f), new(0.25f, -0.30f), new(0.18f, -0.97f), new(0.48f, -1.26f), new(0.57f, -1.71f), new(0.59f, -2.28f), new(1.25f, -3.37f), new(2.31f, -5.12f), new(2.12f, -5.34f), new(1.38f, -5.20f), new(0.11f, -3.58f), new(-0.83f, -5.37f), new(-1.39f, -5.34f), new(-1.36f, -2.15f), new(-1.23f, -1.16f) }),
      new(0.11f, new List<Vector2> { new(-0.99f, -0.94f), new(-0.76f, -0.50f), new(-0.14f, -0.68f), new(-0.18f, -1.28f), new(0.09f, -1.41f), new(0.18f, -2.30f), new(0.57f, -2.65f), new(1.08f, -3.46f), new(2.05f, -5.08f), new(1.99f, -5.28f), new(1.29f, -5.12f), new(-0.18f, -3.29f), new(-0.83f, -5.37f), new(-1.39f, -5.34f), new(-1.29f, -2.66f), new(-1.75f, -1.05f) }),
      new(0.14f, new List<Vector2> { new(0.39f, -1.46f), new(0.62f, -1.05f), new(1.16f, -1.25f), new(1.16f, -1.75f), new(1.07f, -2.04f), new(1.06f, -2.46f), new(0.88f, -2.83f), new(1.38f, -3.48f), new(2.57f, -5.03f), new(2.54f, -5.32f), new(1.69f, -5.28f), new(0.40f, -3.88f), new(-0.83f, -5.37f), new(-1.39f, -5.34f), new(-0.25f, -2.63f), new(-0.17f, -1.45f) }),
      new(0.20f, new List<Vector2> { new(1.31f, -1.65f), new(1.60f, -1.17f), new(2.22f, -1.34f), new(2.00f, -1.97f), new(2.15f, -2.21f), new(2.00f, -2.56f), new(2.20f, -3.05f), new(2.78f, -3.86f), new(2.49f, -4.91f), new(2.99f, -5.35f), new(2.17f, -5.44f), new(1.28f, -3.91f), new(-0.83f, -5.37f), new(-1.39f, -5.34f), new(0.73f, -2.92f), new(0.99f, -2.08f) }),

    },



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
    },
    ["SuperBlast"] = new List<HBox> {
      new(.01f, new List<Vector2> { new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0) }),

    },
  };

  public static Dictionary<string, Dictionary<string, List<HBox>>> Esperanza = new Dictionary<string, Dictionary<string, List<HBox>>> {
    { "hurt", EsperanzaHurt }, { "hit1", EsperanzaHit1 }
  };

  public static Dictionary<string, List<HBox>> ImpHurt { get; } = new Dictionary<string, List<HBox>> {
    ["Run"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(0.12f, -0.28f), new(0.74f, -0.17f), new(1.23f, -0.27f), new(1.28f, -0.81f), new(1.10f, -1.17f), new(1.40f, -1.48f), new(2.27f, -1.98f), new(1.70f, -2.05f), new(0.18f, -2.07f), new(-0.82f, -1.75f), new(-1.18f, -2.51f), new(-1.78f, -2.14f), new(-2.01f, -1.27f), new(-1.32f, -1.28f), new(-1.23f, -0.85f), new(-0.85f, -0.56f) }),
      new(0.17f, new List<Vector2> { new(-0.02f, -0.20f), new(0.74f, -0.17f), new(1.11f, -0.28f), new(1.18f, -0.92f), new(0.88f, -1.17f), new(1.02f, -1.57f), new(1.32f, -2.35f), new(1.02f, -2.40f), new(0.18f, -2.07f), new(-0.44f, -1.74f), new(-1.09f, -2.15f), new(-1.64f, -2.29f), new(-2.01f, -1.48f), new(-1.27f, -1.19f), new(-1.14f, -0.85f), new(-0.85f, -0.56f) }),
      new(0.18f, new List<Vector2> { new(-0.14f, -0.07f), new(0.55f, -0.03f), new(1.11f, -0.18f), new(1.03f, -0.86f), new(0.65f, -1.09f), new(0.43f, -1.54f), new(0.10f, -1.72f), new(-0.08f, -1.96f), new(-0.32f, -2.27f), new(-0.61f, -2.05f), new(-0.98f, -2.11f), new(-1.47f, -1.53f), new(-1.51f, -1.33f), new(-1.11f, -1.07f), new(-1.00f, -0.63f), new(-0.59f, -0.21f) }),
      new(0.36f, new List<Vector2> { new(0.07f, -0.13f), new(0.58f, -0.15f), new(1.11f, -0.18f), new(1.19f, -0.93f), new(1.84f, -1.50f), new(1.29f, -1.71f), new(0.83f, -2.19f), new(0.46f, -2.21f), new(-0.04f, -2.20f), new(-0.50f, -2.07f), new(-0.98f, -2.11f), new(-1.47f, -1.53f), new(-1.14f, -1.27f), new(-1.11f, -1.07f), new(-1.00f, -0.63f), new(-0.45f, -0.24f) }),
      new(0.21f, new List<Vector2> { new(0.12f, -0.28f), new(0.74f, -0.17f), new(1.23f, -0.27f), new(1.28f, -0.81f), new(1.10f, -1.17f), new(1.40f, -1.48f), new(2.27f, -1.98f), new(1.70f, -2.05f), new(0.18f, -2.07f), new(-0.82f, -1.75f), new(-1.18f, -2.51f), new(-1.78f, -2.14f), new(-2.01f, -1.27f), new(-1.32f, -1.28f), new(-1.23f, -0.85f), new(-0.85f, -0.56f) }),

    },
    ["Jump"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(0.33f, -0.08f), new(1.24f, -0.21f), new(1.77f, -0.15f), new(1.71f, -0.72f), new(1.42f, -1.14f), new(1.22f, -1.70f), new(1.24f, -2.16f), new(1.32f, -2.97f), new(0.69f, -2.96f), new(0.22f, -2.80f), new(-0.25f, -2.10f), new(-0.28f, -1.53f), new(-0.39f, -1.11f), new(-0.62f, -0.73f), new(-0.67f, -0.40f), new(-0.44f, -0.15f) }),
      new(0.12f, new List<Vector2> { new(0.33f, 0.05f), new(0.73f, -0.08f), new(1.26f, -0.21f), new(1.32f, -0.50f), new(1.22f, -0.84f), new(1.02f, -1.05f), new(0.65f, -1.52f), new(0.22f, -1.94f), new(-0.11f, -2.39f), new(-0.54f, -2.35f), new(-0.72f, -2.13f), new(-1.29f, -1.42f), new(-0.67f, -1.29f), new(-0.86f, -0.62f), new(-0.65f, -0.25f), new(-0.37f, -0.02f) }),
      new(0.1f, new List<Vector2> { new(1.31f, 1.73f), new(1.81f, 1.54f), new(1.85f, 0.64f), new(1.57f, -0.05f), new(1.95f, -0.75f), new(1.19f, -1.23f), new(0.41f, -0.65f), new(0.36f, -1.43f), new(-0.06f, -2.30f), new(-0.44f, -2.35f), new(-0.86f, -2.21f), new(-1.16f, -1.43f), new(-0.72f, -1.01f), new(-0.64f, -0.30f), new(0.29f, 0.85f), new(1.04f, 1.32f) }),
      new(0.39f, new List<Vector2> { new(1.90f, 5.50f), new(2.37f, 5.28f), new(2.13f, 4.47f), new(2.27f, 4.11f), new(3.84f, 3.41f), new(3.56f, 3.14f), new(2.44f, 3.45f), new(1.65f, 3.57f), new(2.08f, 2.67f), new(0.72f, 0.89f), new(-0.21f, 1.18f), new(-0.46f, 1.60f), new(-0.66f, 2.33f), new(-0.45f, 3.23f), new(0.75f, 4.94f), new(1.38f, 5.15f) }),
      new(0.25f, new List<Vector2> { new(1.90f, 5.00f), new(2.45f, 4.85f), new(2.26f, 4.06f), new(2.21f, 3.45f), new(2.80f, 2.51f), new(2.60f, 2.24f), new(1.84f, 2.91f), new(1.29f, 2.18f), new(2.07f, 1.02f), new(1.99f, 0.52f), new(0.86f, 1.08f), new(0.48f, 1.61f), new(-0.10f, 1.82f), new(-0.02f, 2.98f), new(1.02f, 4.44f), new(1.56f, 4.63f) }),

    },
    ["Idle"] = new List<HBox> {

    },
    ["Attack"] = new List<HBox> {

    },
    ["Hurt"] = new List<HBox> {

    },
    ["Death"] = new List<HBox> {

    },


  };

  public static Dictionary<string, List<HBox>> ImpHit1 { get; } = new Dictionary<string, List<HBox>> {
    ["Run"] = new List<HBox> {
      new(0.01f, new List<Vector2> { new(0.00f, 0.00f), new(0.00f, 0.00f), new(0.00f, 0.00f), new(0.00f, 0.00f), new(0.00f, 0.00f) }),

    },
    ["Jump"] = new List<HBox> {

    },
    ["Idle"] = new List<HBox> { },
    ["Attack"] = new List<HBox> { },
    ["Hurt"] = new List<HBox> { },
    ["Death"] = new List<HBox> { },
  };

  public static Dictionary<string, Dictionary<string, List<HBox>>> Imp = new Dictionary<string, Dictionary<string, List<HBox>>> {
    { "hurt", ImpHurt }, { "hit1", ImpHit1 }
  };

  public static Dictionary<string, Dictionary<string, Dictionary<string, List<HBox>>>> Enemies { get; } = new Dictionary<string, Dictionary<string, Dictionary<string, List<HBox>>>> {
    { "Imp", Imp }
  };
}