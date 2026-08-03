using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(900)]
[DisallowMultipleComponent]
public sealed class ProjectedSpriteShadowCaster2D : MonoBehaviour {
  sealed class ProxyBinding {
    public SpriteWithNormals SourceComponent;
    public SpriteRenderer SourceRenderer;
    public SpriteRenderer ProxyRenderer;
    public Transform ProxyTransform;
    public MaterialPropertyBlock PropertyBlock;

    public Sprite LastSprite;
    public Color LastColor;
    public bool LastFlipX;
    public bool LastFlipY;
    public SpriteDrawMode LastDrawMode;
    public Vector2 LastSize;
    public SpriteTileMode LastTileMode;
    public SpriteSortPoint LastSpriteSortPoint;
    public int LastSortingLayerID;
    public int LastSortingOrder;
    public Vector3 LastPosition;
    public Quaternion LastRotation;
    public Vector3 LastScale;
    public Vector2 LastGroundPosition;
    public float LastVerticalDisplacement;
    public SceneLighting2D.ShadowProjection LastProjection;
  }

  const string ShadowShaderResourcePath = "Shaders/ProjectedSpriteShadow";
  const string ShadowShaderName = "Esperanza/ProjectedSpriteShadow";
  const string GlowShaderResourcePath = "Shaders/ProjectedSpriteGlow";
  const string GlowShaderName = "Esperanza/ProjectedSpriteGlow";

  static readonly int GroundPointPropertyId = Shader.PropertyToID("_GroundPoint");
  static readonly int ProjectionLengthPropertyId = Shader.PropertyToID("_ProjectionLength");
  static readonly int ShadowColorPropertyId = Shader.PropertyToID("_ShadowColor");
  static readonly int ShadowDirectionPropertyId = Shader.PropertyToID("_ShadowDirection");
  static readonly int SourceLocalToWorldPropertyId = Shader.PropertyToID("_SourceLocalToWorld");
  static readonly int StencilComparisonPropertyId = Shader.PropertyToID("_StencilComp");
  static readonly int StencilReferencePropertyId = Shader.PropertyToID("_StencilRef");
  static bool missingShaderLogged;

  [SerializeField] Transform groundAnchor;
  [SerializeField] bool castSunShadow = true;
  [SerializeField] bool castNearestLocalShadow = true;
  [SerializeField] Color shadowColor = Color.black;
  [SerializeField] int shadowGroupOrderOffset;
  [SerializeField] int localShadowOrderOffset = 1;
  [SerializeField, Min(0.05f)] float localLightReselectSeconds = 0.12f;
  [SerializeField] bool isGlowMode = false;

  public bool CastSunShadow { get => castSunShadow; set => castSunShadow = value; }
  public bool CastNearestLocalShadow { get => castNearestLocalShadow; set => castNearestLocalShadow = value; }
  public bool IsGlowMode { get => isGlowMode; set => isGlowMode = value; }

  readonly List<SpriteWithNormals> configuredSources = new();
  readonly HashSet<SpriteWithNormals> configuredSourceSet = new();
  readonly List<SpriteWithNormals> supplementalSourceBuffer = new();
  readonly List<ProxyBinding> sunBindings = new();
  readonly List<ProxyBinding> localBindings = new();

  SceneLighting2D boundLightingManager;
  GameObject shadowRootObject;
  Transform sunSlotRoot;
  Transform localSlotRoot;
  SortingGroup sunSortingGroup;
  SortingGroup localSortingGroup;
  SortingGroup sourceSortingGroup;
  Material sunMaterial;
  Material localMaterial;
  bool started;
  bool proxiesDirty = true;
  bool groundYLocked;
  ulong selectedLocalLightId;
  int sunStencilReference;
  int localStencilReference;
  float lockedGroundY;
  float nextLocalLightSelectionTime;

  public static ProjectedSpriteShadowCaster2D Ensure(
    GameObject characterRoot,
    AnimationController animationController,
    GameObject[] supplementalSources = null
  ) {
    if (!Application.isPlaying || characterRoot == null || animationController == null) {
      return null;
    }

    var caster = characterRoot.GetComponent<ProjectedSpriteShadowCaster2D>();
    if (caster == null) {
      caster = characterRoot.AddComponent<ProjectedSpriteShadowCaster2D>();
    }

    caster.ConfigureSources(animationController, supplementalSources);
    return caster;
  }

  void Awake() {
    ResolveGroundAnchor();
    sourceSortingGroup = GetComponent<SortingGroup>();
  }

  void Start() {
    started = true;
    if (proxiesDirty || shadowRootObject == null) {
      TryRebuildProxies();
    }
  }

  void OnEnable() {
    if (started && (proxiesDirty || shadowRootObject == null)) {
      TryRebuildProxies();
    }

    AcquireStencilReferences();
  }

  void OnDisable() {
    SetBindingsEnabled(sunBindings, false);
    SetBindingsEnabled(localBindings, false);
    ReleaseStencilReferences();
    selectedLocalLightId = 0;
  }

  void OnDestroy() {
    DestroyProxyState();
  }

  void OnValidate() {
    localLightReselectSeconds = Mathf.Max(0.05f, localLightReselectSeconds);
  }

  public void ConfigureSources(
    AnimationController animationController,
    GameObject[] supplementalSources = null
  ) {
    configuredSources.Clear();
    animationController?.CopySpriteTargetsTo(configuredSources);
    RemoveDisabledShadowSources();
    AddSupplementalSources(supplementalSources);
    proxiesDirty = true;

    if (started && isActiveAndEnabled) {
      TryRebuildProxies();
    }
  }

  void RemoveDisabledShadowSources() {
    for (var i = configuredSources.Count - 1; i >= 0; i--) {
      var source = configuredSources[i];
      if (source == null || !source.CastsProjectedShadow) {
        configuredSources.RemoveAt(i);
      }
    }
  }

  public bool PrepareProxyHierarchyForActivation() {
    if (!Application.isPlaying || isActiveAndEnabled || SceneLighting2D.Current == null) {
      return false;
    }

    TryRebuildProxies(allowBeforeStart: true);
    return !proxiesDirty && shadowRootObject != null;
  }

  public void LockGroundY() {
    ResolveGroundAnchor();
    var groundPosition = ResolveCurrentGroundPosition();
    lockedGroundY = groundPosition.y;
    groundYLocked = true;
  }

  public void UnlockGroundY() {
    groundYLocked = false;
  }

  public Vector2 GroundPosition {
    get {
      ResolveGroundAnchor();
      return ResolveCurrentGroundPosition();
    }
  }

  void AddSupplementalSources(GameObject[] supplementalSources) {
    configuredSourceSet.Clear();
    for (var i = 0; i < configuredSources.Count; i++) {
      var source = configuredSources[i];
      if (source != null) {
        configuredSourceSet.Add(source);
      }
    }

    if (supplementalSources == null) {
      return;
    }

    for (var i = 0; i < supplementalSources.Length; i++) {
      var sourceObject = supplementalSources[i];
      if (sourceObject == null) {
        continue;
      }

      supplementalSourceBuffer.Clear();
      sourceObject.GetComponentsInChildren(true, supplementalSourceBuffer);
      for (var sourceIndex = 0;
           sourceIndex < supplementalSourceBuffer.Count;
           sourceIndex++) {
        var source = supplementalSourceBuffer[sourceIndex];
        if (source == null ||
            !source.CastsProjectedShadow ||
            !configuredSourceSet.Add(source)) {
          continue;
        }

        configuredSources.Add(source);
      }
    }

    supplementalSourceBuffer.Clear();
  }

  void LateUpdate() {
    var lightingManager = SceneLighting2D.Current;
    if (lightingManager == null) {
      SetBindingsEnabled(sunBindings, false);
      SetBindingsEnabled(localBindings, false);
      ReleaseStencilReferences();
      return;
    }

    if (boundLightingManager != lightingManager || shadowRootObject == null) {
      proxiesDirty = true;
    }
    if (proxiesDirty) {
      TryRebuildProxies();
    }
    if (shadowRootObject == null) {
      return;
    }

    AcquireStencilReferences();
    ResolveGroundAnchor();
    SyncShadowSortingOrder();
    var groundPosition = ResolveGroundPosition();
    var verticalDisplacement = ResolveGroundVerticalDisplacement();
    var casterSortingLayerId = ResolveCasterSortingLayerId();
    var receiverPosition = (Vector2)transform.position;
    receiverPosition.y -= verticalDisplacement;

    SyncCelestialShadow(
      lightingManager,
      groundPosition,
      verticalDisplacement,
      casterSortingLayerId
    );
    SyncLocalShadow(
      lightingManager,
      receiverPosition,
      groundPosition,
      verticalDisplacement,
      casterSortingLayerId
    );
  }

  void SyncCelestialShadow(
    SceneLighting2D lightingManager,
    Vector2 groundPosition,
    float verticalDisplacement,
    int casterSortingLayerId
  ) {
    if (!castSunShadow) {
      SetBindingsEnabled(sunBindings, false);
      return;
    }
    if (!lightingManager.TryGetCelestialShadow(
      groundPosition,
      casterSortingLayerId,
      out var projection
    )) {
      SetBindingsEnabled(sunBindings, false);
      return;
    }

    ApplyProjection(sunMaterial, groundPosition, projection, ResolveGlowTintColor());
    SyncBindings(
      sunBindings,
      true,
      groundPosition,
      verticalDisplacement,
      projection
    );
  }

  void SyncLocalShadow(
    SceneLighting2D lightingManager,
    Vector2 receiverPosition,
    Vector2 groundPosition,
    float verticalDisplacement,
    int casterSortingLayerId
  ) {
    if (!castNearestLocalShadow) {
      selectedLocalLightId = 0;
      SetBindingsEnabled(localBindings, false);
      return;
    }

    var now = TimeScale.GetNow(this);
    if (selectedLocalLightId == 0 || now >= nextLocalLightSelectionTime) {
      selectedLocalLightId = lightingManager.SelectNearestLocalLight(
        receiverPosition,
        casterSortingLayerId,
        selectedLocalLightId
      );
      nextLocalLightSelectionTime = now + localLightReselectSeconds;
    }

    if (!lightingManager.TryGetLocalShadow(
      selectedLocalLightId,
      receiverPosition,
      groundPosition,
      casterSortingLayerId,
      out var projection)) {
      selectedLocalLightId = 0;
      SetBindingsEnabled(localBindings, false);
      return;
    }

    ApplyProjection(localMaterial, groundPosition, projection, ResolveGlowTintColor());
    SyncBindings(
      localBindings,
      true,
      groundPosition,
      verticalDisplacement,
      projection
    );
  }

  void TryRebuildProxies(bool allowBeforeStart = false) {
    if ((!started && !allowBeforeStart) || !Application.isPlaying) {
      return;
    }

    var lightingManager = SceneLighting2D.Current;
    if (lightingManager == null) {
      return;
    }

    DestroyProxyState();
    boundLightingManager = lightingManager;
    proxiesDirty = false;
    if (configuredSources.Count == 0) {
      return;
    }

    var shaderPath = isGlowMode ? GlowShaderResourcePath : ShadowShaderResourcePath;
    var shaderName = isGlowMode ? GlowShaderName : ShadowShaderName;

    var shader = Resources.Load<Shader>(shaderPath);
    if (shader == null) {
      shader = Shader.Find(shaderName);
    }
    if (shader == null) {
      LogMissingShaderOnce();
      return;
    }

    CreateShadowRoot(lightingManager);
    sunMaterial = CreateShadowMaterial(shader, "Sun", out sunStencilReference);
    localMaterial = CreateShadowMaterial(shader, "Local", out localStencilReference);

    for (var i = 0; i < configuredSources.Count; i++) {
      var sourceComponent = configuredSources[i];
      if (sourceComponent == null) {
        continue;
      }

      var sourceRenderer = sourceComponent.GetComponent<SpriteRenderer>();
      if (sourceRenderer == null) {
        continue;
      }

      sunBindings.Add(CreateBinding(
        sourceComponent,
        sourceRenderer,
        sunMaterial,
        sunSlotRoot,
        "Sun"
      ));
      localBindings.Add(CreateBinding(
        sourceComponent,
        sourceRenderer,
        localMaterial,
        localSlotRoot,
        "Local"
      ));
    }

    if (isActiveAndEnabled) {
      AcquireStencilReferences();
    }
  }

  void CreateShadowRoot(SceneLighting2D lightingManager) {
    shadowRootObject = new GameObject("__ProjectedShadow_" + gameObject.name);
    shadowRootObject.hideFlags = HideFlags.DontSave;
    shadowRootObject.transform.SetParent(lightingManager.ShadowRoot, false);

    sunSortingGroup = CreateShadowSlot(lightingManager, "Sun", out sunSlotRoot);
    localSortingGroup = CreateShadowSlot(lightingManager, "Local", out localSlotRoot);
    SyncShadowSortingOrder();
  }

  SortingGroup CreateShadowSlot(
    SceneLighting2D lightingManager,
    string slotName,
    out Transform slotRoot
  ) {
    var slotObject = new GameObject("__" + slotName);
    slotObject.hideFlags = HideFlags.DontSave;
    slotRoot = slotObject.transform;
    slotRoot.SetParent(shadowRootObject.transform, false);

    var sortingGroup = slotObject.AddComponent<SortingGroup>();
    sortingGroup.sortingLayerID = lightingManager.ShadowSortingLayerId;
    return sortingGroup;
  }

  Material CreateShadowMaterial(
    Shader shader,
    string slotName,
    out int stencilReference
  ) {
    var material = new Material(shader);
    material.name = gameObject.name + " Projected " + slotName + " Shadow";
    material.hideFlags = HideFlags.DontSave;
    stencilReference = 0;
    ApplyStencilReference(material, stencilReference);
    return material;
  }

  static void ApplyStencilReference(Material material, int stencilReference) {
    if (material == null) {
      return;
    }

    material.SetInt(StencilReferencePropertyId, stencilReference);
    var comparison = stencilReference > 0
      ? CompareFunction.NotEqual
      : CompareFunction.Always;
    material.SetInt(StencilComparisonPropertyId, (int)comparison);
  }

  void AcquireStencilReferences() {
    var sunNeedsStencil = castSunShadow && sunBindings.Count > 1;
    SyncStencilReference(
      sunMaterial,
      sunNeedsStencil,
      ref sunStencilReference
    );

    var localNeedsStencil = castNearestLocalShadow && localBindings.Count > 1;
    SyncStencilReference(
      localMaterial,
      localNeedsStencil,
      ref localStencilReference
    );
  }

  static void SyncStencilReference(
    Material material,
    bool shouldOwnReference,
    ref int stencilReference
  ) {
    if (!shouldOwnReference) {
      if (stencilReference == 0) {
        return;
      }

      SceneLighting2D.ReleaseShadowStencilReference(stencilReference);
      stencilReference = 0;
      ApplyStencilReference(material, stencilReference);
      return;
    }
    if (material == null || stencilReference > 0) {
      return;
    }

    stencilReference = SceneLighting2D.ReserveShadowStencilReference();
    ApplyStencilReference(material, stencilReference);
  }

  void ReleaseStencilReferences() {
    SyncStencilReference(sunMaterial, false, ref sunStencilReference);
    SyncStencilReference(localMaterial, false, ref localStencilReference);
  }

  ProxyBinding CreateBinding(
    SpriteWithNormals sourceComponent,
    SpriteRenderer sourceRenderer,
    Material material,
    Transform slotRoot,
    string slotName
  ) {
    var proxyObject = new GameObject("__" + slotName + "_" + sourceRenderer.gameObject.name);
    proxyObject.hideFlags = HideFlags.DontSave;
    proxyObject.layer = sourceRenderer.gameObject.layer;
    proxyObject.transform.SetParent(slotRoot, false);

    var proxyRenderer = proxyObject.AddComponent<SpriteRenderer>();
    proxyRenderer.sharedMaterial = material;
    proxyRenderer.shadowCastingMode = ShadowCastingMode.Off;
    proxyRenderer.receiveShadows = false;
    proxyRenderer.maskInteraction = SpriteMaskInteraction.None;
    proxyRenderer.enabled = false;

    return new ProxyBinding {
      SourceComponent = sourceComponent,
      SourceRenderer = sourceRenderer,
      ProxyRenderer = proxyRenderer,
      ProxyTransform = proxyObject.transform,
      PropertyBlock = new MaterialPropertyBlock()
    };
  }

  void SyncBindings(
    List<ProxyBinding> bindings,
    bool layerEnabled,
    Vector2 groundPosition,
    float verticalDisplacement,
    SceneLighting2D.ShadowProjection projection
  ) {
    for (var i = 0; i < bindings.Count; i++) {
      SyncBinding(
        bindings[i],
        layerEnabled,
        groundPosition,
        verticalDisplacement,
        projection
      );
    }
  }

  void SyncBinding(
    ProxyBinding binding,
    bool layerEnabled,
    Vector2 groundPosition,
    float verticalDisplacement,
    SceneLighting2D.ShadowProjection projection
  ) {
    var sourceComponent = binding.SourceComponent;
    var sourceRenderer = binding.SourceRenderer;
    var proxyRenderer = binding.ProxyRenderer;
    if (sourceComponent == null || sourceRenderer == null || proxyRenderer == null) {
      if (proxyRenderer != null) {
        proxyRenderer.enabled = false;
      }
      return;
    }

    var shouldRender = layerEnabled &&
                       sourceComponent.isActiveAndEnabled &&
                       !sourceComponent.DoNotRender &&
                       sourceRenderer.enabled &&
                       sourceRenderer.sprite != null &&
                       sourceRenderer.gameObject.activeInHierarchy;
    if (!shouldRender) {
      if (proxyRenderer.enabled) {
        proxyRenderer.enabled = false;
      }
      return;
    }

    if (!proxyRenderer.enabled) {
      proxyRenderer.enabled = true;
    }

    var sourceTransform = sourceRenderer.transform;
    var currentSprite = sourceRenderer.sprite;
    var currentColor = sourceRenderer.color;
    var currentFlipX = sourceRenderer.flipX;
    var currentFlipY = sourceRenderer.flipY;
    var currentDrawMode = sourceRenderer.drawMode;
    var currentSize = sourceRenderer.size;
    var currentTileMode = sourceRenderer.tileMode;
    var currentSpriteSortPoint = sourceRenderer.spriteSortPoint;
    var currentSortingLayerID = boundLightingManager.ShadowSortingLayerId;
    var currentSortingOrder = sourceRenderer.sortingOrder;

    var currentPosition = sourceTransform.position;
    var currentRotation = sourceTransform.rotation;
    var currentScale = sourceTransform.lossyScale;

    bool visualsChanged = binding.LastSprite != currentSprite ||
                          binding.LastColor != currentColor ||
                          binding.LastFlipX != currentFlipX ||
                          binding.LastFlipY != currentFlipY ||
                          binding.LastDrawMode != currentDrawMode ||
                          binding.LastSize != currentSize ||
                          binding.LastTileMode != currentTileMode ||
                          binding.LastSpriteSortPoint != currentSpriteSortPoint ||
                          binding.LastSortingLayerID != currentSortingLayerID ||
                          binding.LastSortingOrder != currentSortingOrder;

    bool transformChanged = binding.LastPosition != currentPosition ||
                            binding.LastRotation != currentRotation ||
                            binding.LastScale != currentScale;

    bool projectionChanged = binding.LastGroundPosition != groundPosition ||
                             binding.LastVerticalDisplacement != verticalDisplacement ||
                             binding.LastProjection.Length != projection.Length ||
                             binding.LastProjection.Direction != projection.Direction ||
                             binding.LastProjection.Opacity != projection.Opacity;

    if (!visualsChanged && !transformChanged && !projectionChanged) {
      return;
    }

    if (visualsChanged) {
      binding.LastSprite = currentSprite;
      binding.LastColor = currentColor;
      binding.LastFlipX = currentFlipX;
      binding.LastFlipY = currentFlipY;
      binding.LastDrawMode = currentDrawMode;
      binding.LastSize = currentSize;
      binding.LastTileMode = currentTileMode;
      binding.LastSpriteSortPoint = currentSpriteSortPoint;
      binding.LastSortingLayerID = currentSortingLayerID;
      binding.LastSortingOrder = currentSortingOrder;

      proxyRenderer.sprite = currentSprite;
      proxyRenderer.color = currentColor;
      proxyRenderer.flipX = currentFlipX;
      proxyRenderer.flipY = currentFlipY;
      proxyRenderer.drawMode = currentDrawMode;
      proxyRenderer.size = currentSize;
      proxyRenderer.tileMode = currentTileMode;
      proxyRenderer.spriteSortPoint = currentSpriteSortPoint;
      proxyRenderer.sortingLayerID = currentSortingLayerID;
      proxyRenderer.sortingOrder = currentSortingOrder;
    }

    if (transformChanged) {
      binding.LastPosition = currentPosition;
      binding.LastRotation = currentRotation;
      binding.LastScale = currentScale;
      SyncProxyTransform(binding.ProxyTransform, sourceTransform);
    }

    if (transformChanged || projectionChanged) {
      binding.LastGroundPosition = groundPosition;
      binding.LastVerticalDisplacement = verticalDisplacement;
      binding.LastProjection = projection;

      var sourceLocalToWorld = sourceTransform.localToWorldMatrix;
      sourceLocalToWorld.m13 -= verticalDisplacement;
      binding.PropertyBlock.SetMatrix(
        SourceLocalToWorldPropertyId,
        sourceLocalToWorld
      );
      proxyRenderer.SetPropertyBlock(binding.PropertyBlock);
    }

    if (visualsChanged || transformChanged || projectionChanged) {
      var sourceBounds = sourceRenderer.bounds;
      var sourceBoundsCenter = sourceBounds.center;
      sourceBoundsCenter.y -= verticalDisplacement;
      sourceBounds.center = sourceBoundsCenter;
      SyncProxyBounds(
        proxyRenderer,
        binding.ProxyTransform,
        sourceBounds,
        groundPosition,
        projection
      );
    }
  }

  static void SyncProxyTransform(Transform proxyTransform, Transform sourceTransform) {
    proxyTransform.SetPositionAndRotation(sourceTransform.position, sourceTransform.rotation);

    var parentScale = proxyTransform.parent != null
      ? proxyTransform.parent.lossyScale
      : Vector3.one;
    var sourceScale = sourceTransform.lossyScale;
    proxyTransform.localScale = new Vector3(
      DivideScale(sourceScale.x, parentScale.x),
      DivideScale(sourceScale.y, parentScale.y),
      DivideScale(sourceScale.z, parentScale.z)
    );
  }

  static float DivideScale(float value, float divisor) {
    if (Mathf.Abs(divisor) <= 0.0001f) {
      return value;
    }
    return value / divisor;
  }

  static void SyncProxyBounds(
    SpriteRenderer proxyRenderer,
    Transform proxyTransform,
    Bounds sourceWorldBounds,
    Vector2 groundPosition,
    SceneLighting2D.ShadowProjection projection
  ) {
    var inverseTransform = proxyTransform.worldToLocalMatrix;
    var minimum = sourceWorldBounds.min;
    var maximum = sourceWorldBounds.max;
    var firstWorldPoint = ProjectWorldPoint(
      new Vector3(minimum.x, minimum.y, minimum.z),
      groundPosition,
      projection
    );
    var firstLocalPoint = inverseTransform.MultiplyPoint3x4(firstWorldPoint);
    var localBounds = new Bounds(firstLocalPoint, Vector3.zero);

    EncapsulateProjectedBound(
      ref localBounds,
      inverseTransform,
      new Vector3(minimum.x, minimum.y, maximum.z),
      groundPosition,
      projection
    );
    EncapsulateProjectedBound(
      ref localBounds,
      inverseTransform,
      new Vector3(maximum.x, minimum.y, minimum.z),
      groundPosition,
      projection
    );
    EncapsulateProjectedBound(
      ref localBounds,
      inverseTransform,
      new Vector3(maximum.x, minimum.y, maximum.z),
      groundPosition,
      projection
    );
    EncapsulateProjectedBound(
      ref localBounds,
      inverseTransform,
      new Vector3(minimum.x, maximum.y, minimum.z),
      groundPosition,
      projection
    );
    EncapsulateProjectedBound(
      ref localBounds,
      inverseTransform,
      new Vector3(minimum.x, maximum.y, maximum.z),
      groundPosition,
      projection
    );
    EncapsulateProjectedBound(
      ref localBounds,
      inverseTransform,
      new Vector3(maximum.x, maximum.y, minimum.z),
      groundPosition,
      projection
    );
    EncapsulateProjectedBound(
      ref localBounds,
      inverseTransform,
      new Vector3(maximum.x, maximum.y, maximum.z),
      groundPosition,
      projection
    );

    if (groundPosition.y > minimum.y && groundPosition.y < maximum.y) {
      EncapsulateProjectedBound(
        ref localBounds,
        inverseTransform,
        new Vector3(minimum.x, groundPosition.y, minimum.z),
        groundPosition,
        projection
      );
      EncapsulateProjectedBound(
        ref localBounds,
        inverseTransform,
        new Vector3(maximum.x, groundPosition.y, maximum.z),
        groundPosition,
        projection
      );
    }

    proxyRenderer.localBounds = localBounds;
  }

  static void EncapsulateProjectedBound(
    ref Bounds localBounds,
    Matrix4x4 inverseTransform,
    Vector3 worldPoint,
    Vector2 groundPosition,
    SceneLighting2D.ShadowProjection projection
  ) {
    var projectedWorldPoint = ProjectWorldPoint(
      worldPoint,
      groundPosition,
      projection
    );
    var localPoint = inverseTransform.MultiplyPoint3x4(projectedWorldPoint);
    localBounds.Encapsulate(localPoint);
  }

  static Vector3 ProjectWorldPoint(
    Vector3 worldPoint,
    Vector2 groundPosition,
    SceneLighting2D.ShadowProjection projection
  ) {
    var height = Mathf.Max(worldPoint.y - groundPosition.y, 0f);
    var projectedPoint = worldPoint;
    projectedPoint.x += projection.Direction.x * height * projection.Length;
    projectedPoint.y = groundPosition.y;
    projectedPoint.y += projection.Direction.y * height * projection.Length;
    return projectedPoint;
  }

  Color ResolveGlowTintColor() {
    if (!isGlowMode) return shadowColor;
    if (configuredSources.Count == 0 || configuredSources[0] == null) return shadowColor;
    var renderer = configuredSources[0].GetComponent<SpriteRenderer>();
    if (renderer == null) return shadowColor;
    
    Color tint = renderer.color;
    if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_Color")) {
        tint *= renderer.sharedMaterial.GetColor("_Color");
    }
    return tint;
  }

  void ApplyProjection(
    Material material,
    Vector2 groundPosition,
    SceneLighting2D.ShadowProjection projection,
    Color currentTint
  ) {
    if (material == null) {
      return;
    }

    var resolvedColor = isGlowMode ? currentTint : shadowColor;
    resolvedColor.a *= projection.Opacity;
    material.SetColor(ShadowColorPropertyId, resolvedColor);
    material.SetVector(
      GroundPointPropertyId,
      new Vector4(groundPosition.x, groundPosition.y, 0f, 0f)
    );
    material.SetVector(
      ShadowDirectionPropertyId,
      new Vector4(projection.Direction.x, projection.Direction.y, 0f, 0f)
    );
    material.SetFloat(ProjectionLengthPropertyId, projection.Length);
  }

  void SyncShadowSortingOrder() {
    if (boundLightingManager == null) {
      return;
    }
    if (sunSortingGroup == null || localSortingGroup == null) {
      return;
    }

    var baseOrder = boundLightingManager.ShadowSortingOrder;
    baseOrder += shadowGroupOrderOffset;
    var sortingLayerId = boundLightingManager.ShadowSortingLayerId;
    var localOrder = baseOrder + localShadowOrderOffset;
    if (sunSortingGroup.sortingLayerID != sortingLayerId) {
      sunSortingGroup.sortingLayerID = sortingLayerId;
    }
    if (localSortingGroup.sortingLayerID != sortingLayerId) {
      localSortingGroup.sortingLayerID = sortingLayerId;
    }
    if (sunSortingGroup.sortingOrder != baseOrder) {
      sunSortingGroup.sortingOrder = baseOrder;
    }
    if (localSortingGroup.sortingOrder != localOrder) {
      localSortingGroup.sortingOrder = localOrder;
    }
  }

  int ResolveCasterSortingLayerId() {
    if (sourceSortingGroup != null) {
      return sourceSortingGroup.sortingLayerID;
    }
    if (configuredSources.Count == 0 || configuredSources[0] == null) {
      return 0;
    }

    var renderer = configuredSources[0].GetComponent<SpriteRenderer>();
    return renderer != null ? renderer.sortingLayerID : 0;
  }

  void ResolveGroundAnchor() {
    if (groundAnchor != null) {
      return;
    }

    var zPoint = GetComponentInChildren<Zpoint>(true);
    groundAnchor = zPoint != null ? zPoint.transform : transform;
  }

  Vector2 ResolveGroundPosition() {
    var groundPosition = ResolveCurrentGroundPosition();
    if (groundYLocked) {
      groundPosition.y = lockedGroundY;
    }
    return groundPosition;
  }

  Vector2 ResolveCurrentGroundPosition() {
    return groundAnchor != null
      ? (Vector2)groundAnchor.position
      : (Vector2)transform.position;
  }

  float ResolveGroundVerticalDisplacement() {
    if (!groundYLocked) {
      return 0f;
    }

    var currentGroundPosition = ResolveCurrentGroundPosition();
    return currentGroundPosition.y - lockedGroundY;
  }

  static void SetBindingsEnabled(List<ProxyBinding> bindings, bool enabled) {
    for (var i = 0; i < bindings.Count; i++) {
      var renderer = bindings[i].ProxyRenderer;
      if (renderer != null && renderer.enabled != enabled) {
        renderer.enabled = enabled;
      }
    }
  }

  void DestroyProxyState() {
    SetBindingsEnabled(sunBindings, false);
    SetBindingsEnabled(localBindings, false);
    if (shadowRootObject != null) {
      shadowRootObject.SetActive(false);
    }

    sunBindings.Clear();
    localBindings.Clear();
    ReleaseStencilReferences();

    if (shadowRootObject != null) {
      Destroy(shadowRootObject);
    }
    if (sunMaterial != null) {
      Destroy(sunMaterial);
    }
    if (localMaterial != null) {
      Destroy(localMaterial);
    }

    shadowRootObject = null;
    sunSlotRoot = null;
    localSlotRoot = null;
    sunSortingGroup = null;
    localSortingGroup = null;
    sunMaterial = null;
    localMaterial = null;
    boundLightingManager = null;
    selectedLocalLightId = 0;
  }

  static void LogMissingShaderOnce() {
    if (missingShaderLogged) {
      return;
    }

    missingShaderLogged = true;
    Debug.LogError(
      "[ProjectedSpriteShadowCaster2D] Missing shader '" + ShadowShaderName + "'."
    );
  }
}
