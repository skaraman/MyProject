using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Util
{
    /// <summary>
    /// Spawns and disperses a set of prefabs randomly within a specified 2D or 3D area,
    /// enforcing minimum/maximum quantities, overlap prevention, and same-type proximity restrictions.
    /// </summary>
    [ExecuteAlways]
    public class PrefabDisperser : MonoBehaviour
    {
        public enum AreaType
        {
            Box,
            Circle,
            ColliderBounds
        }

        public enum DimensionMode
        {
            Mode3D_XZ,  // Spreads on horizontal XZ plane (Standard 3D)
            Mode3D_XYZ, // Spreads inside 3D volume
            Mode2D_XY   // Spreads on 2D XY plane (2D Games)
        }

        [Serializable]
        public class PrefabEntry
        {
            [Tooltip("The prefab GameObject to instantiate.")]
            public GameObject prefab;

            [Tooltip("Minimum guaranteed count for this specific prefab.")]
            [Min(0)] public int minCount = 1;

            [Tooltip("Maximum allowed count for this specific prefab.")]
            [Min(1)] public int maxCount = 100;

            [Tooltip("Selection weight when filling remaining random spawn slots.")]
            [Min(0.01f)] public float spawnWeight = 1.0f;

            [Header("Proximity & Collision")]
            [Tooltip("Minimum distance required between instances of THIS SAME prefab to avoid close clustering.")]
            [Min(0f)] public float sameTypeMinDistance = 4.0f;

            [Tooltip("Collision radius used for general overlap checks with other objects. If 0, auto-calculated from renderer/collider bounds.")]
            [Min(0f)] public float collisionRadius = 0f;

            [Header("Transform Randomization")]
            [Tooltip("Random uniform scale range (X = min scale, Y = max scale).")]
            public Vector2 scaleRange = new Vector2(0.9f, 1.1f);

            [Tooltip("Randomize rotation around vertical axis (Y in 3D, Z in 2D).")]
            public bool randomRotation = true;
        }

        [Header("Prefab Configurations")]
        [SerializeField] private List<PrefabEntry> prefabs = new List<PrefabEntry>();

        [Header("Spawn Quantity Limits")]
        [Tooltip("Minimum total number of objects to disperse overall.")]
        [Min(0)] [SerializeField] private int minTotalObjects = 5;

        [Tooltip("Maximum total number of objects to disperse overall.")]
        [Min(1)] [SerializeField] private int maxTotalObjects = 25;

        [Header("Area Configuration")]
        [SerializeField] private AreaType areaType = AreaType.Box;
        [SerializeField] private DimensionMode dimensionMode = DimensionMode.Mode3D_XZ;

        [Tooltip("Center of the spawn area (relative to this transform's position).")]
        [SerializeField] private Vector3 areaCenter = Vector3.zero;

        [Tooltip("Size of the box spawn area (Width, Height, Depth).")]
        [SerializeField] private Vector3 boxSize = new Vector3(20f, 0f, 20f);

        [Tooltip("Radius of the circle/sphere spawn area.")]
        [Min(0.1f)] [SerializeField] private float circleRadius = 10f;

        [Tooltip("Target collider used when Area Type is set to Collider Bounds.")]
        [SerializeField] private Collider targetCollider;

        [Header("Smart Positioning & Overlap Rules")]
        [Tooltip("Global minimum padding distance enforced between ANY two spawned objects.")]
        [Min(0f)] [SerializeField] private float globalMinDistance = 1.0f;

        [Tooltip("If space is tight, gradually relax spacing/proximity rules so all target objects can be fitted into the area.")]
        [SerializeField] private bool allowRelaxingSpacing = true;

        [Tooltip("Perform 3D/2D physics overlap checks against existing scene geometry before placing.")]
        [SerializeField] private bool usePhysicsCheck = false;

        [Tooltip("Layer mask for physics overlap checks.")]
        [SerializeField] private LayerMask physicsCheckMask = ~0;

        [Tooltip("Maximum candidate position search attempts per object before giving up.")]
        [Min(1)] [SerializeField] private int maxPlacementAttempts = 200;

        [Header("Ground Snapping (3D)")]
        [Tooltip("Raycast downwards to snap spawned 3D objects onto ground/terrain surfaces.")]
        [SerializeField] private bool snapToGround = false;

        [Tooltip("Layer mask for ground raycasting.")]
        [SerializeField] private LayerMask groundLayerMask = ~0;

        [Tooltip("Height above the spawn area to start the downward raycast.")]
        [SerializeField] private float raycastStartHeight = 50f;

        [Tooltip("Maximum downward raycast distance.")]
        [SerializeField] private float maxRaycastDistance = 100f;

        [Tooltip("Align object's UP vector to match the ground surface normal.")]
        [SerializeField] private bool alignToGroundNormal = false;

        [Tooltip("Vertical height offset applied after ground hit point.")]
        [SerializeField] private float groundOffset = 0f;

        [Header("Organization")]
        [Tooltip("Parent transform to hold spawned objects. If null, defaults to this transform.")]
        [SerializeField] private Transform parentContainer;

        [Tooltip("Automatically disperse objects when entering Play mode.")]
        [SerializeField] private bool disperseOnStart = false;

        [HideInInspector]
        [SerializeField] private List<GameObject> spawnedObjects = new List<GameObject>();

        private struct PlacedInstance
        {
            public Vector3 position;
            public float radius;
            public GameObject prefab;
        }

        private void Start()
        {
            if (Application.isPlaying && disperseOnStart)
            {
                DisperseObjects();
            }
        }

        /// <summary>
        /// Clears existing spawned objects and disperses a new random batch based on rules.
        /// </summary>
        [ContextMenu("Disperse Prefabs")]
        public void DisperseObjects()
        {
            ClearDispersedObjects();

            if (prefabs == null || prefabs.Count == 0)
            {
                Debug.LogWarning("[PrefabDisperser] No prefabs configured in list.", this);
                return;
            }

            // Build item queue according to min/max counts and weights
            List<PrefabEntry> itemsToSpawn = GenerateSpawnList();
            if (itemsToSpawn.Count == 0)
            {
                Debug.LogWarning("[PrefabDisperser] Spawn list produced 0 items to spawn.", this);
                return;
            }

            // Shuffle spawn order so one prefab type doesn't occupy all ideal spots first
            ShuffleList(itemsToSpawn);

            List<PlacedInstance> placedInstances = new List<PlacedInstance>();
            Transform container = parentContainer != null ? parentContainer : transform;

            int successfullySpawned = 0;

            foreach (PrefabEntry entry in itemsToSpawn)
            {
                if (entry.prefab == null) continue;

                float effectiveRadius = GetEffectiveRadius(entry);

                if (TryFindValidPosition(entry, effectiveRadius, placedInstances, out Vector3 spawnPos, out Quaternion spawnRot))
                {
                    GameObject newObj = InstantiateObject(entry.prefab, spawnPos, spawnRot, container);

                    // Random scale
                    if (entry.scaleRange.x > 0 && entry.scaleRange.y >= entry.scaleRange.x)
                    {
                        float randomScale = UnityEngine.Random.Range(entry.scaleRange.x, entry.scaleRange.y);
                        newObj.transform.localScale = Vector3.one * randomScale;
                    }

                    spawnedObjects.Add(newObj);
                    placedInstances.Add(new PlacedInstance
                    {
                        position = spawnPos,
                        radius = effectiveRadius,
                        prefab = entry.prefab
                    });

                    successfullySpawned++;
                }
            }

            if (successfullySpawned < itemsToSpawn.Count)
            {
                Debug.LogWarning($"[PrefabDisperser] Dispersed {successfullySpawned} / {itemsToSpawn.Count} objects. Could not place remaining {itemsToSpawn.Count - successfullySpawned} objects. Consider enabling 'allowRelaxingSpacing' or expanding the spawn area.", this);
            }
            else
            {
                Debug.Log($"[PrefabDisperser] Dispersed {successfullySpawned} / {itemsToSpawn.Count} objects successfully.", this);
            }
        }

        /// <summary>
        /// Destroys all previously spawned gameobjects.
        /// </summary>
        [ContextMenu("Clear Dispersed Objects")]
        public void ClearDispersedObjects()
        {
            for (int i = spawnedObjects.Count - 1; i >= 0; i--)
            {
                GameObject obj = spawnedObjects[i];
                if (obj != null)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        Undo.DestroyObjectImmediate(obj);
                    }
                    else
                    {
                        Destroy(obj);
                    }
#else
                    Destroy(obj);
#endif
                }
            }
            spawnedObjects.Clear();
        }

        private List<PrefabEntry> GenerateSpawnList()
        {
            List<PrefabEntry> result = new List<PrefabEntry>();
            Dictionary<PrefabEntry, int> counts = new Dictionary<PrefabEntry, int>();

            int totalMaxCap = 0;
            foreach (var entry in prefabs)
            {
                if (entry.prefab == null) continue;

                counts[entry] = 0;
                int guaranteed = Mathf.Min(entry.minCount, entry.maxCount);
                for (int i = 0; i < guaranteed; i++)
                {
                    result.Add(entry);
                    counts[entry]++;
                }
                totalMaxCap += entry.maxCount;
            }

            // Determine target total count
            int targetTotal = UnityEngine.Random.Range(minTotalObjects, maxTotalObjects + 1);

            if (totalMaxCap < targetTotal)
            {
                Debug.LogWarning($"[PrefabDisperser] Target total ({targetTotal}) exceeds combined maxCount of all prefabs ({totalMaxCap}). Total spawned will be capped at {totalMaxCap}. Increase maxCount on individual Prefab Entries if you want more objects.", this);
            }

            int currentTotal = result.Count;

            // Fill remaining slots up to targetTotal using weighted selection
            while (currentTotal < targetTotal)
            {
                List<PrefabEntry> validCandidates = new List<PrefabEntry>();
                float totalWeight = 0f;

                foreach (var entry in prefabs)
                {
                    if (entry.prefab == null) continue;
                    if (counts[entry] < entry.maxCount)
                    {
                        validCandidates.Add(entry);
                        totalWeight += Mathf.Max(0.001f, entry.spawnWeight);
                    }
                }

                if (validCandidates.Count == 0) break; // All max limits reached

                // Pick weighted candidate
                float randomVal = UnityEngine.Random.Range(0f, totalWeight);
                float accum = 0f;
                PrefabEntry selected = validCandidates[0];

                foreach (var candidate in validCandidates)
                {
                    accum += Mathf.Max(0.001f, candidate.spawnWeight);
                    if (randomVal <= accum)
                    {
                        selected = candidate;
                        break;
                    }
                }

                result.Add(selected);
                counts[selected]++;
                currentTotal++;
            }

            return result;
        }

        private bool TryFindValidPosition(
            PrefabEntry entry,
            float itemRadius,
            List<PlacedInstance> placedInstances,
            out Vector3 validPosition,
            out Quaternion validRotation)
        {
            validPosition = Vector3.zero;
            validRotation = Quaternion.identity;

            Vector3 worldCenter = transform.TransformPoint(areaCenter);

            // Relaxation steps from 1.0 (100% strict spacing) down to 0.0 (relaxed fallback)
            int steps = allowRelaxingSpacing ? 10 : 1;
            int attemptsPerStep = Mathf.Max(10, maxPlacementAttempts / steps);

            for (int step = 0; step < steps; step++)
            {
                float distanceMultiplier = steps > 1 ? 1.0f - ((float)step / (steps - 1)) : 1.0f;

                for (int attempt = 0; attempt < attemptsPerStep; attempt++)
                {
                    Vector3 rawPos = GetRandomPointInArea(worldCenter);
                    Vector3 candidatePos = rawPos;
                    Quaternion candidateRot = Quaternion.identity;

                    // Handle random rotation
                    if (entry.randomRotation)
                    {
                        if (dimensionMode == DimensionMode.Mode2D_XY)
                        {
                            candidateRot = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
                        }
                        else
                        {
                            candidateRot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                        }
                    }

                    // Handle ground snapping in 3D
                    if (dimensionMode != DimensionMode.Mode2D_XY && snapToGround)
                    {
                        Vector3 rayStart = candidatePos + Vector3.up * raycastStartHeight;
                        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, maxRaycastDistance, groundLayerMask))
                        {
                            candidatePos = hit.point + hit.normal * groundOffset;

                            if (alignToGroundNormal)
                            {
                                candidateRot = Quaternion.FromToRotation(Vector3.up, hit.normal) * candidateRot;
                            }
                        }
                        else
                        {
                            // Raycast failed (e.g. invalid height/void), reject candidate
                            continue;
                        }
                    }

                    // Smart Positioning Check 1: Minimum Distance & Overlap against previously placed objects
                    bool positionValid = true;
                    foreach (var placed in placedInstances)
                    {
                        float dist = Vector3.Distance(candidatePos, placed.position);

                        // Check same-type distance rule (relaxed by distanceMultiplier if needed)
                        if (placed.prefab == entry.prefab)
                        {
                            if (dist < entry.sameTypeMinDistance * distanceMultiplier)
                            {
                                positionValid = false;
                                break;
                            }
                        }

                        // Check general overlap / global padding (relaxed by distanceMultiplier if needed)
                        float minRequiredDist = (itemRadius + placed.radius + globalMinDistance) * distanceMultiplier;
                        if (dist < minRequiredDist)
                        {
                            positionValid = false;
                            break;
                        }
                    }

                    if (!positionValid) continue;

                    // Smart Positioning Check 2: Physics overlap check against existing scene geometry
                    if (usePhysicsCheck && distanceMultiplier > 0.1f)
                    {
                        if (dimensionMode == DimensionMode.Mode2D_XY)
                        {
                            Collider2D col2d = Physics2D.OverlapCircle(candidatePos, itemRadius * distanceMultiplier, physicsCheckMask);
                            if (col2d != null) continue;
                        }
                        else
                        {
                            Collider[] cols = Physics.OverlapSphere(candidatePos, itemRadius * distanceMultiplier, physicsCheckMask);
                            if (cols != null && cols.Length > 0) continue;
                        }
                    }

                    // Passed all checks!
                    validPosition = candidatePos;
                    validRotation = candidateRot;
                    return true;
                }
            }

            return false;
        }

        private Vector3 GetRandomPointInArea(Vector3 worldCenter)
        {
            switch (areaType)
            {
                case AreaType.Circle:
                    {
                        if (dimensionMode == DimensionMode.Mode2D_XY)
                        {
                            Vector2 randCircle = UnityEngine.Random.insideUnitCircle * circleRadius;
                            Vector3 localOffset = new Vector3(randCircle.x, randCircle.y, 0f);
                            return worldCenter + transform.rotation * localOffset;
                        }
                        else if (dimensionMode == DimensionMode.Mode3D_XYZ)
                        {
                            Vector3 randSphere = UnityEngine.Random.insideUnitSphere * circleRadius;
                            return worldCenter + transform.rotation * randSphere;
                        }
                        else // Mode3D_XZ
                        {
                            Vector2 randCircle = UnityEngine.Random.insideUnitCircle * circleRadius;
                            Vector3 localOffset = new Vector3(randCircle.x, 0f, randCircle.y);
                            return worldCenter + transform.rotation * localOffset;
                        }
                    }

                case AreaType.ColliderBounds:
                    {
                        if (targetCollider != null)
                        {
                            Bounds bounds = targetCollider.bounds;
                            float x = UnityEngine.Random.Range(bounds.min.x, bounds.max.x);
                            float y = (dimensionMode == DimensionMode.Mode3D_XYZ || dimensionMode == DimensionMode.Mode2D_XY)
                                ? UnityEngine.Random.Range(bounds.min.y, bounds.max.y)
                                : worldCenter.y;
                            float z = (dimensionMode == DimensionMode.Mode2D_XY)
                                ? worldCenter.z
                                : UnityEngine.Random.Range(bounds.min.z, bounds.max.z);

                            return new Vector3(x, y, z);
                        }
                        // Fallback to Box if targetCollider is null
                        goto case AreaType.Box;
                    }

                case AreaType.Box:
                default:
                    {
                        Vector3 halfSize = boxSize * 0.5f;
                        float x = UnityEngine.Random.Range(-halfSize.x, halfSize.x);
                        float y = (dimensionMode == DimensionMode.Mode3D_XYZ || dimensionMode == DimensionMode.Mode2D_XY)
                            ? UnityEngine.Random.Range(-halfSize.y, halfSize.y)
                            : 0f;
                        float z = (dimensionMode == DimensionMode.Mode2D_XY)
                            ? 0f
                            : UnityEngine.Random.Range(-halfSize.z, halfSize.z);

                        Vector3 localOffset = new Vector3(x, y, z);
                        return worldCenter + transform.rotation * localOffset;
                    }
            }
        }

        private float GetEffectiveRadius(PrefabEntry entry)
        {
            if (entry.collisionRadius > 0f) return entry.collisionRadius;

            if (entry.prefab != null)
            {
                // Try 3D Colliders (including children)
                var boxCol = entry.prefab.GetComponentInChildren<BoxCollider>();
                if (boxCol != null)
                {
                    Vector3 s = Vector3.Scale(boxCol.size, boxCol.transform.localScale);
                    return Mathf.Max(s.x, s.y, s.z) * 0.5f;
                }

                var sphereCol = entry.prefab.GetComponentInChildren<SphereCollider>();
                if (sphereCol != null)
                {
                    float maxScale = Mathf.Max(sphereCol.transform.localScale.x, sphereCol.transform.localScale.y, sphereCol.transform.localScale.z);
                    return sphereCol.radius * maxScale;
                }

                var capCol = entry.prefab.GetComponentInChildren<CapsuleCollider>();
                if (capCol != null)
                {
                    float maxScale = Mathf.Max(capCol.transform.localScale.x, capCol.transform.localScale.y, capCol.transform.localScale.z);
                    return Mathf.Max(capCol.radius, capCol.height * 0.5f) * maxScale;
                }

                var col = entry.prefab.GetComponentInChildren<Collider>();
                if (col != null && col.bounds.extents.sqrMagnitude > 0.0001f)
                {
                    Vector3 extents = col.bounds.extents;
                    return Mathf.Max(extents.x, extents.y, extents.z);
                }

                // Try 2D Colliders (including children)
                var boxCol2D = entry.prefab.GetComponentInChildren<BoxCollider2D>();
                if (boxCol2D != null)
                {
                    Vector2 s = Vector2.Scale(boxCol2D.size, boxCol2D.transform.localScale);
                    return Mathf.Max(s.x, s.y) * 0.5f;
                }

                var circleCol2D = entry.prefab.GetComponentInChildren<CircleCollider2D>();
                if (circleCol2D != null)
                {
                    float maxScale = Mathf.Max(circleCol2D.transform.localScale.x, circleCol2D.transform.localScale.y);
                    return circleCol2D.radius * maxScale;
                }

                var col2d = entry.prefab.GetComponentInChildren<Collider2D>();
                if (col2d != null && col2d.bounds.extents.sqrMagnitude > 0.0001f)
                {
                    Vector3 extents = col2d.bounds.extents;
                    return Mathf.Max(extents.x, extents.y);
                }

                // Try MeshFilter / Renderer (including children)
                var mf = entry.prefab.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Vector3 extents = mf.sharedMesh.bounds.extents;
                    Vector3 scaled = Vector3.Scale(extents, mf.transform.localScale);
                    return Mathf.Max(scaled.x, scaled.y, scaled.z);
                }

                var rend = entry.prefab.GetComponentInChildren<Renderer>();
                if (rend != null && rend.bounds.extents.sqrMagnitude > 0.0001f)
                {
                    Vector3 extents = rend.bounds.extents;
                    return Mathf.Max(extents.x, extents.y, extents.z);
                }
            }

            return 0.5f; // Default fallback radius
        }

        private GameObject InstantiateObject(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                GameObject instantiated = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                instantiated.transform.position = pos;
                instantiated.transform.rotation = rot;
                Undo.RegisterCreatedObjectUndo(instantiated, "Disperse Prefab");
                return instantiated;
            }
#endif
            return Instantiate(prefab, pos, rot, parent);
        }

        private void ShuffleList<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int rnd = UnityEngine.Random.Range(0, i + 1);
                T temp = list[i];
                list[i] = list[rnd];
                list[rnd] = temp;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1.0f, 0.7f);
            Vector3 worldCenter = transform.TransformPoint(areaCenter);

            Matrix4x4 origMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(worldCenter, transform.rotation, Vector3.one);

            if (areaType == AreaType.Box)
            {
                Vector3 gizmoSize = boxSize;
                if (dimensionMode == DimensionMode.Mode3D_XZ) gizmoSize.y = 0.01f;
                else if (dimensionMode == DimensionMode.Mode2D_XY) gizmoSize.z = 0.01f;

                Gizmos.DrawWireCube(Vector3.zero, gizmoSize);
            }
            else if (areaType == AreaType.Circle)
            {
                if (dimensionMode == DimensionMode.Mode2D_XY)
                {
                    int segments = 36;
                    Vector3 prevPoint = new Vector3(circleRadius, 0f, 0f);
                    for (int i = 1; i <= segments; i++)
                    {
                        float angle = (i * 360f / segments) * Mathf.Deg2Rad;
                        Vector3 nextPoint = new Vector3(Mathf.Cos(angle) * circleRadius, Mathf.Sin(angle) * circleRadius, 0f);
                        Gizmos.DrawLine(prevPoint, nextPoint);
                        prevPoint = nextPoint;
                    }
                }
                else if (dimensionMode == DimensionMode.Mode3D_XYZ)
                {
                    Gizmos.DrawWireSphere(Vector3.zero, circleRadius);
                }
                else // Mode3D_XZ
                {
                    int segments = 36;
                    Vector3 prevPoint = new Vector3(circleRadius, 0f, 0f);
                    for (int i = 1; i <= segments; i++)
                    {
                        float angle = (i * 360f / segments) * Mathf.Deg2Rad;
                        Vector3 nextPoint = new Vector3(Mathf.Cos(angle) * circleRadius, 0f, Mathf.Sin(angle) * circleRadius);
                        Gizmos.DrawLine(prevPoint, nextPoint);
                        prevPoint = nextPoint;
                    }
                }
            }

            Gizmos.matrix = origMatrix;

            if (areaType == AreaType.ColliderBounds && targetCollider != null)
            {
                Gizmos.DrawWireCube(targetCollider.bounds.center, targetCollider.bounds.size);
            }

            // Draw gizmos for spawned object positions
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            foreach (var obj in spawnedObjects)
            {
                if (obj != null)
                {
                    Gizmos.DrawWireSphere(obj.transform.position, 0.3f);
                }
            }
        }
    }
}
