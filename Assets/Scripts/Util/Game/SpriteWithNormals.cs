using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class SpriteWithNormals : MonoBehaviour {

  [FormerlySerializedAs("colorKey")]
  public string namepart = "";

  [FormerlySerializedAs("labelPrefix")]
  public string form = "";

  [FormerlySerializedAs("category")]
  public new string animation = "Breathe";

  SpriteRenderer _renderer;
  MaterialPropertyBlock _mpb;
  int _lastRequestedFrame;
  int _lastExternalRequestFrame = int.MinValue;
  bool _isInternalTickRequest;

  const int ExternalDriverHoldFrames = 2;

  int _requestVersion;
  string _targetColorAddress = "";
  string _targetNormalAddress = "";
  string _pendingColorAddress = "";
  string _pendingNormalAddress = "";
  string _lastResolveError = "";
  bool _hasDeferredRequest;
  SpriteAddressPair _deferredRequest;

  Coroutine _pendingLoadRoutine;
  TextureResidencyCache.Lease _pendingColorLease;
  TextureResidencyCache.Lease _pendingNormalLease;
  TextureResidencyCache.Lease _activeColorLease;
  TextureResidencyCache.Lease _activeNormalLease;

  void Awake() {
    _renderer = GetComponent<SpriteRenderer>();
    _mpb = new MaterialPropertyBlock();
  }

  void Update() {
    if (!Application.isPlaying) return;
    if (!enabled || !gameObject.activeInHierarchy) return;
    if (Time.frameCount - _lastExternalRequestFrame <= ExternalDriverHoldFrames) return;
    _isInternalTickRequest = true;
    try {
      UpdateSpriteAndNormal(_lastRequestedFrame);
    }
    finally {
      _isInternalTickRequest = false;
    }
  }

  void OnDisable() {
    if (!Application.isPlaying) return;
    CancelPendingRequest();
    ReleaseActiveLeases();
    TextureResidencyCache.PurgeUnpinned();
  }

  void OnDestroy() {
    CancelPendingRequest();
    ReleaseActiveLeases();
    TextureResidencyCache.PurgeUnpinned();
  }

  public void SetAnimation(string value) {
    animation = value;
  }

  public void SetNamePart(string value) {
    namepart = value;
  }

  public void SetForm(string value) {
    form = value;
  }

  public void ForceUpdateSpriteAndNormal() {
    _targetColorAddress = "";
    _targetNormalAddress = "";
    UpdateSpriteAndNormal(_lastRequestedFrame);
  }

  public void UpdateSpriteAndNormal(int frame) {
    _lastRequestedFrame = frame;
    if (Application.isPlaying && !_isInternalTickRequest) {
      _lastExternalRequestFrame = Time.frameCount;
    }
    if (Application.isPlaying && (!enabled || !gameObject.activeInHierarchy)) return;

    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer == null) return;

    var lookupKey = new SpriteLookupKey(namepart, form, animation, frame);
    if (!TryResolvePair(lookupKey, out var pair)) {
      ReportResolveError(lookupKey);
      return;
    }
    _lastResolveError = "";

    if (pair.colorAddress == _targetColorAddress && pair.normalAddress == _targetNormalAddress) {
      return;
    }

    _targetColorAddress = pair.colorAddress ?? "";
    _targetNormalAddress = pair.normalAddress ?? "";

    if (Application.isPlaying) {
      QueueRuntimeLoad(pair);
      return;
    }

#if UNITY_EDITOR
    ApplyEditorPreview(pair, lookupKey);
#endif
  }

  public void FlipSprite(bool flip) {
    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer == null) return;
    _renderer.flipX = flip;
  }

  void QueueRuntimeLoad(SpriteAddressPair pair) {
    if (_pendingLoadRoutine != null) {
      if (AddressEquals(_pendingColorAddress, pair.colorAddress) && AddressEquals(_pendingNormalAddress, pair.normalAddress)) {
        return;
      }
      _deferredRequest = pair;
      _hasDeferredRequest = true;
      return;
    }

    StartRuntimeLoad(pair);
  }

  void StartRuntimeLoad(SpriteAddressPair pair) {
    CancelPendingRequest();
    _pendingColorAddress = pair.colorAddress ?? "";
    _pendingNormalAddress = pair.normalAddress ?? "";

    var colorLease = TextureResidencyCache.AcquireAsync(pair.colorAddress);
    if (colorLease == null) {
      Debug.LogError("[SpriteWithNormals] Failed to request color address '" + pair.colorAddress + "' on " + gameObject.name);
      _pendingColorAddress = "";
      _pendingNormalAddress = "";
      TryStartDeferredRequest();
      return;
    }

    var normalLease = string.IsNullOrWhiteSpace(pair.normalAddress) ? null : TextureResidencyCache.AcquireAsync(pair.normalAddress);
    if (!string.IsNullOrWhiteSpace(pair.normalAddress) && normalLease == null) {
      Debug.LogError("[SpriteWithNormals] Failed to request normal address '" + pair.normalAddress + "' on " + gameObject.name);
    }

    _pendingColorLease = colorLease;
    _pendingNormalLease = normalLease;
    _requestVersion++;
    var localVersion = _requestVersion;
    _pendingLoadRoutine = StartCoroutine(ApplyLoadedSprites(localVersion, colorLease, normalLease, pair));
  }

  IEnumerator ApplyLoadedSprites(int requestVersion, TextureResidencyCache.Lease colorLease, TextureResidencyCache.Lease normalLease, SpriteAddressPair pair) {
    while ((colorLease != null && !colorLease.IsDone) || (normalLease != null && !normalLease.IsDone)) {
      yield return null;
    }

    if (requestVersion != _requestVersion) {
      ReleaseLease(ref colorLease);
      ReleaseLease(ref normalLease);
      yield break;
    }

    ClearPendingState();

    var colorSprite = colorLease != null && colorLease.IsSuccess ? colorLease.Sprite : null;
    if (colorSprite == null) {
      Debug.LogError("[SpriteWithNormals] Failed to load color sprite '" + pair.colorAddress + "' on " + gameObject.name);
      ReleaseLease(ref colorLease);
      ReleaseLease(ref normalLease);
      TryStartDeferredRequest();
      yield break;
    }

    var normalSprite = normalLease != null && normalLease.IsSuccess ? normalLease.Sprite : null;
    if (normalLease != null && normalSprite == null) {
      Debug.LogError("[SpriteWithNormals] Failed to load normal sprite '" + pair.normalAddress + "' on " + gameObject.name);
    }

    if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    if (_renderer != null) {
      ApplySprites(colorSprite, normalSprite);
    }

    ReleaseActiveLeases();
    _activeColorLease = colorLease;
    _activeNormalLease = normalLease;
    TryStartDeferredRequest();
  }

  void ApplySprites(Sprite colorSprite, Sprite normalSprite) {
    _renderer.sprite = colorSprite;
    _mpb ??= new MaterialPropertyBlock();
    _renderer.GetPropertyBlock(_mpb);

    if (normalSprite != null && normalSprite.texture != null) {
      _mpb.SetTexture("_NormalMap", normalSprite.texture);
    }

    _renderer.SetPropertyBlock(_mpb);
  }

  void CancelPendingRequest() {
    if (_pendingLoadRoutine != null) {
      StopCoroutine(_pendingLoadRoutine);
      _pendingLoadRoutine = null;
    }
    ReleaseLease(ref _pendingColorLease);
    ReleaseLease(ref _pendingNormalLease);
    _pendingColorAddress = "";
    _pendingNormalAddress = "";
    _hasDeferredRequest = false;
    _deferredRequest = default;
    _requestVersion++;
  }

  void ReleaseActiveLeases() {
    ReleaseLease(ref _activeColorLease);
    ReleaseLease(ref _activeNormalLease);
  }

  static void ReleaseLease(ref TextureResidencyCache.Lease lease) {
    if (lease == null) return;
    lease.Release();
    lease = null;
  }

  void ClearPendingState() {
    _pendingLoadRoutine = null;
    _pendingColorLease = null;
    _pendingNormalLease = null;
    _pendingColorAddress = "";
    _pendingNormalAddress = "";
  }

  void TryStartDeferredRequest() {
    if (!_hasDeferredRequest) return;
    var deferred = _deferredRequest;
    _hasDeferredRequest = false;
    _deferredRequest = default;
    QueueRuntimeLoad(deferred);
  }

  static bool AddressEquals(string left, string right) {
    return string.Equals(NormalizeAddress(left), NormalizeAddress(right), System.StringComparison.OrdinalIgnoreCase);
  }

  static string NormalizeAddress(string value) {
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
  }

  void ReportResolveError(SpriteLookupKey lookupKey) {
    var error = lookupKey.ToString();
    if (error == _lastResolveError) return;
    _lastResolveError = error;
    Debug.LogError("[SpriteWithNormals] No sprite mapping found for " + error + " on " + gameObject.name);
  }

  static bool TryResolvePair(SpriteLookupKey lookupKey, out SpriteAddressPair pair) {
    return SpriteAddressResolver.TryResolve(lookupKey, out pair);
  }

#if UNITY_EDITOR
  void ApplyEditorPreview(SpriteAddressPair pair, SpriteLookupKey lookupKey) {
    if (!SpriteAddressResolver.TryLoadEditorSprite(pair.colorAddress, out var colorSprite) || colorSprite == null) {
      Debug.LogError("[SpriteWithNormals] Editor preview color sprite not found for '" + pair.colorAddress + "' (" + lookupKey + ")");
      return;
    }

    Sprite normalSprite = null;
    if (!string.IsNullOrWhiteSpace(pair.normalAddress) &&
        !SpriteAddressResolver.TryLoadEditorSprite(pair.normalAddress, out normalSprite)) {
      Debug.LogError("[SpriteWithNormals] Editor preview normal sprite not found for '" + pair.normalAddress + "' (" + lookupKey + ")");
    }

    ApplySprites(colorSprite, normalSprite);
  }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(SpriteWithNormals))]
public class SpriteWithNormalsEditor : Editor {
  SerializedProperty namepartProp;
  SerializedProperty formProp;
  SerializedProperty animationProp;

  void OnEnable() {
    namepartProp = serializedObject.FindProperty("namepart");
    formProp = serializedObject.FindProperty("form");
    animationProp = serializedObject.FindProperty("animation");
  }

  public override void OnInspectorGUI() {
    serializedObject.Update();

    namepartProp.stringValue = EditorGUILayout.DelayedTextField("Name Part", namepartProp.stringValue);
    formProp.stringValue = EditorGUILayout.DelayedTextField("Form", formProp.stringValue);
    animationProp.stringValue = EditorGUILayout.DelayedTextField("Animation", animationProp.stringValue);

    serializedObject.ApplyModifiedProperties();

    var targetComponent = (SpriteWithNormals)target;
    if (!Application.isPlaying && GUILayout.Button("Refresh Sprite + Normal")) {
      targetComponent.ForceUpdateSpriteAndNormal();
      EditorUtility.SetDirty(targetComponent);
    }
  }
}
#endif
