using System;
using UnityEngine;

public class Button : MonoBehaviour {

  public string clickMessage;
  public string hoverMessage;
  public string releaseMessage;

  void OnMouseDown() {
    Debug.Log($"OnMouseDown: Sending {clickMessage}");
    MessageBus.Send(clickMessage);
  }

  void OnMouseEnter() {
    Debug.Log($"OnMouseEnter: Sending {hoverMessage}");
    MessageBus.Send(hoverMessage);
  }

  void OnMouseExit() {
    Debug.Log("OnMouseExit: Cancel hover if needed");
  }

  void OnMouseUp() {
    Debug.Log($"OnMouseUp: Sending {releaseMessage}");
    MessageBus.Send(releaseMessage);
  }
}

public class GenericButton : MonoBehaviour {
  private Action offHover;
  public SpriteRenderer sR;
  void Start() {
    offHover = MessageBus.On("genericHover", (o) => GenericHover());  
  }

  void GenericHover() {
    ColorUtility.TryParseHtmlString("#FF69B4", out var color);
    sR.color = color;
  }
}