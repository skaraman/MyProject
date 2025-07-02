using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class UIPosition : MonoBehaviour {
  [SerializeField] private string _x = "0%";
  public string x {
    get => _x;
    set { _x = value; SetTransform(); }
  }

  [SerializeField] private string _y = "0%";
  public string y {
    get => _y;
    set { _y = value; SetTransform(); }
  }

  [SerializeField] private string _width = "100%";
  public string width {
    get => _width;
    set { _width = value; SetTransform(); }
  }

  [SerializeField] private string _height = "100%";
  public string height {
    get => _height;
    set { _height = value; SetTransform(); }
  }

  [SerializeField] private string _rotation = "0";
  public string rotation {
    get => _rotation;
    set { _rotation = value; SetTransform(); }
  }

  public ResolutionChangeDetector resolution;

  public Vector4 GetParsedRect() {
    var camWidth = resolution.cam.pixelWidth;
    var camHeight = resolution.cam.pixelHeight;

    var px = ParsePercent(_x, camWidth);
    var py = ParsePercent(_y, camHeight);
    var w = ParsePercent(_width, camWidth);
    var h = ParsePercent(_height, camHeight);

    return new Vector4(px, py, w, h);
  }

  public float GetParsedRotation() {
    if (_rotation.Contains("%")) {
      var trimmed = _rotation.Trim().Replace("%", "");
      if (!float.TryParse(trimmed, out float parsed)) {
        Debug.LogWarning($"Could not parse rotation percentage: {_rotation}");
        return 0f;
      }
      return 360f * (parsed / 100f); // 45% = 45% of 360° = 162°
    }
    else {
      return float.Parse(_rotation);
    }
  }

  public float ParsePercent(string value, float relativeTo) {
    if (string.IsNullOrEmpty(value)) return 0;
    if (!value.Contains("%")) return float.Parse(value);
    var trimmed = value.Trim().Replace("%", "");
    if (!float.TryParse(trimmed, out float parsed)) return 0f;
    return relativeTo * (parsed / 100f);
  }

  public void SetTransform() {
    var newTran = GetParsedRect(); // x, y, width, height in screen pixels
    var newRot = GetParsedRotation();
    var sr = GetComponent<SpriteRenderer>();
    var spriteSize = new Vector3();
    var ppu = 0f;
    if (sr == null) {
      spriteSize = new Vector3(1, 1);
      ppu = 1000f;
    }
    else {
      spriteSize = sr.sprite.bounds.size;
      ppu = sr.sprite.pixelsPerUnit;
    }
    // Calculate screen-to-world Z offset for proper perspective projection
    float zDistance = Mathf.Abs(resolution.cam.transform.position.z - transform.position.z);

    // Flip Y axis if using top-left origin (optional)
    float screenY = resolution.cam.pixelHeight - newTran.y;

    // Convert screen point to world point at proper depth
    var screenPoint = new Vector3(newTran.x, screenY, zDistance);
    var worldPos = resolution.cam.ScreenToWorldPoint(screenPoint);
    transform.position = new Vector3(worldPos.x, worldPos.y, transform.position.z);

    // Set scale based on desired pixel width/height
    float widthUnits = newTran.z / ppu;
    float heightUnits = newTran.w / ppu;
    transform.localScale = new Vector3(widthUnits / spriteSize.x, heightUnits / spriteSize.y, transform.localScale.z);

    // Set Z rotation only
    var rot = transform.localEulerAngles;
    rot.z = newRot;
    transform.localEulerAngles = rot;
  }

#if UNITY_EDITOR
  void OnValidate() {
    SetTransform();
  }
#endif
}
