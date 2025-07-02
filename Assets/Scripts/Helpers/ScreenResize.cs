using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class ResolutionChangeDetector : MonoBehaviour {
  public Vector2 referenceResolution = new Vector2(1920, 1080);
  public List<GameObject> resizeObjects;
  public int currentWidth { get; set; }
  public int currentHeight { get; set; }
  private bool init = true;
  private float timer = 0f;
  public Camera cam;

  private void Start() {
    // currentWidth = cam.pixelWidth;
    // currentHeight = cam.pixelHeight;
  }

  private void Update() {
    if (transform.localPosition != cam.transform.localPosition) {
      transform.localPosition = cam.transform.localPosition;
    }

    if (init || cam.pixelWidth != currentWidth || cam.pixelHeight != currentHeight) {
      timer += Time.deltaTime;
      if (timer > 1f) {
        timer = 0f;
        ApplyResolutionScaling();
    //     currentWidth = cam.pixelWidth;
    //     currentHeight = cam.pixelHeight;
      }
    }
  }

  public void ApplyResolutionScaling() {
    init = false;
    // var scaleFactor = new Vector2(cam.pixelWidth / referenceResolution.x, cam.pixelHeight / referenceResolution.y);
    // //var uniformScale = Mathf.Min(scaleFactor.x, scaleFactor.y);
    // Debug.Log($"[ResolutionChangeDetector] Screen: {cam.pixelWidth}x{cam.pixelHeight} | Scale: {scaleFactor.x},{scaleFactor.y}");
    for (int i = 0; i < resizeObjects.Count; i++)
    {
      var go = resizeObjects[i];
      // if (go == null) continue;
      // var local = go.transform.localScale;
      // var scaled = new Vector3(scaleFactor.x, scaleFactor.y, local.z);
      // go.transform.localScale = scaled;
      go.GetComponent<UIPosition>().SetTransform();
    }
  }

#if UNITY_EDITOR
  void OnValidate() {
    Start();
  }
#endif
}