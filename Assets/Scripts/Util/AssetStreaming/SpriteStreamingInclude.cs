using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SpriteStreamingInclude", menuName = "Sprite Streaming/Include")]
public class SpriteStreamingInclude : ScriptableObject {
  public List<string> libraryNames = new();
}
