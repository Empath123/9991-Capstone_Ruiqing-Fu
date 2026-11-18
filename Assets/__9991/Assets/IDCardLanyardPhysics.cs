using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unity 6. Attach to lanyard-001-b and assign an Anchor at the silver clip.
/// Generates a double-sided ribbon loop at runtime and uses ordinary Physics
/// queries for collisions. No Cloth component, bones or readable source mesh needed.
/// Distances are in world metres. Original rendering is restored on disable.
/// This is a visual, one-way simulation: it does not pull the card or push objects.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class IDCardLanyardPhysics : MonoBehaviour
{
    [Header("Connection")]
    [Tooltip("Create an empty child of the card root at the strap/clip connection.")]
    public Transform anchor;
    [Tooltip("Optional. Defaults to the material on this original strap.")]
    public Material strapMaterial;

    [Header("Initial loop - metres")]
    [Min(0.04f)] public float loopReach = 0.24f;
    [Min(0.01f)] public float loopHalfWidth = 0.035f;
    [Min(0.003f)] public float strapWidth = 0.018f;
    [Tooltip("Direction away from the clip, in Anchor local space. Blue gizmo.")]
    public Vector3 initialDirection = Vector3.back;
    [Tooltip("Normal of the initial flat loop, in Anchor local space.")]
    public Vector3 initialNormal = Vector3.up;
    [Range(16, 80)] public int segments = 40;

    [Header("Motion")]
    [Range(0f, 10f)] public float damping = 2f;
    [Range(0f, 0.2f)] public float bendStiffness = 0.015f;
    [Range(2, 8)] public int substeps = 3;
    [Range(4, 16)] public int solverIterations = 6;

    [Header("Collision")]
    public LayerMask collisionLayers = ~0;
    [Tooltip("Radius of the collision samples along both edges of the ribbon.")]
    [Range(0.001f, 0.01f)] public float collisionRadius = 0.002f;
    [Range(0f, 1f)] public float surfaceFriction = 0.65f;
    [Tooltip("Optional exclusions, for example controller colliders.")]
    public Collider[] ignoreColliders = Array.Empty<Collider>();

    private struct Link
    {
        public int a, b;
        public float length;
        public bool bend;
    }

    private Vector3[] positions, velocities, stepStart, safePositions, contacts;
    private Vector3[] pinOffsets, meshVertices;
    private bool[] pinned;
    private Link[] links;
    private int pointCount, sampleCount;
    private Vector3 lastAnchorPosition;
    private Quaternion lastAnchorRotation;
    private GameObject runtimeObject;
    private Mesh ribbonMesh;
    private SphereCollider probe;
    private Renderer originalRenderer;
    private bool originalRendererEnabled;
    private Cloth originalCloth;
    private bool originalClothEnabled;
    private Collider[] originalColliders;
    private bool[] originalColliderStates;
    private readonly HashSet<Collider> ignored = new HashSet<Collider>();
    private Collider[] overlaps = new Collider[16];
    private RaycastHit[] hits = new RaycastHit[16];
    private bool initialized;
    private const float Skin = 0.0003f;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (!anchor)
        {
            Debug.LogError("IDCardLanyardPhysics: Assign Anchor at the silver clip, then enable this component.", this);
            enabled = false;
            return;
        }

        originalRenderer = GetComponent<Renderer>();
        Material material = strapMaterial ? strapMaterial :
            (originalRenderer ? originalRenderer.sharedMaterial : null);
        if (!material)
        {
            Debug.LogError("IDCardLanyardPhysics: Assign the blue strap material.", this);
            enabled = false;
            return;
        }

        // Disable only the original strap's components, not card/hook components.
        originalCloth = GetComponent<Cloth>();
        originalClothEnabled = originalCloth && originalCloth.enabled;
        if (originalCloth) originalCloth.enabled = false;
        originalRendererEnabled = originalRenderer && originalRenderer.enabled;
        if (originalRenderer) originalRenderer.enabled = false;
        originalColliders = GetComponents<Collider>();
        originalColliderStates = new bool[originalColliders.Length];
        ignored.Clear();
        for (int i = 0; i < originalColliders.Length; i++)
        {
            originalColliderStates[i] = originalColliders[i].enabled;
            originalColliders[i].enabled = false;
            ignored.Add(originalColliders[i]);
        }
        if (ignoreColliders != null)
            foreach (Collider collider in ignoreColliders)
                if (collider) ignored.Add(collider);

        // Identity transform: vertices and collision radii use world metres,
        // independent of imported FBX scale or the moving card's transform.
        runtimeObject = new GameObject(name + " Runtime Ribbon");
        runtimeObject.hideFlags = HideFlags.DontSave;
        runtimeObject.layer = gameObject.layer;
        ribbonMesh = new Mesh { name = "Simulated ID card strap" };
        ribbonMesh.MarkDynamic();
        runtimeObject.AddComponent<MeshFilter>().sharedMesh = ribbonMesh;
        runtimeObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        probe = runtimeObject.AddComponent<SphereCollider>();
        probe.radius = collisionRadius;
        probe.enabled = false; // Used only as a shape for ComputePenetration.
        ignored.Add(probe);

        BuildRibbon();
        lastAnchorPosition = anchor.position;
        lastAnchorRotation = anchor.rotation;
        initialized = true;
        UpdateMesh();
    }

    private void GetBasis(out Vector3 forward, out Vector3 normal, out Vector3 right)
    {
        forward = initialDirection.sqrMagnitude > 0.001f ? initialDirection.normalized : Vector3.back;
        normal = Vector3.ProjectOnPlane(initialNormal, forward);
        if (normal.sqrMagnitude < 0.001f)
            normal = Vector3.ProjectOnPlane(Mathf.Abs(forward.y) < 0.9f ? Vector3.up : Vector3.right, forward);
        normal.Normalize();
        right = Vector3.Cross(normal, forward).normalized;
    }

    private Vector3 LoopPoint(float t, Vector3 forward, Vector3 right)
    {
        float reach = Mathf.Max(loopReach, 0.04f);
        float halfWidth = Mathf.Max(loopHalfWidth, strapWidth);
        // Two ends meet at the clip with a small separation.
        return forward * (reach * Mathf.Sin(Mathf.PI * t)) +
            right * (halfWidth * Mathf.Sin(2f * Mathf.PI * t) + (t - 0.5f) * 0.012f);
    }

    private void BuildRibbon()
    {
        sampleCount = Mathf.Clamp(segments, 16, 80) + 1;
        pointCount = sampleCount * 2;
        positions = new Vector3[pointCount];
        velocities = new Vector3[pointCount];
        stepStart = new Vector3[pointCount];
        safePositions = new Vector3[pointCount];
        contacts = new Vector3[pointCount];
        pinOffsets = new Vector3[pointCount];
        pinned = new bool[pointCount];
        meshVertices = new Vector3[pointCount * 2];
        Vector2[] uv = new Vector2[meshVertices.Length];
        GetBasis(out Vector3 forward, out Vector3 normal, out Vector3 right);
        var constraints = new List<Link>();

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / (sampleCount - 1);
            Vector3 center = LoopPoint(t, forward, right);
            float reach = Mathf.Max(loopReach, 0.04f);
            float halfWidth = Mathf.Max(loopHalfWidth, strapWidth);
            Vector3 tangent = forward * (reach * Mathf.PI * Mathf.Cos(Mathf.PI * t)) +
                right * (halfWidth * 2f * Mathf.PI * Mathf.Cos(2f * Mathf.PI * t) + 0.012f);
            Vector3 across = Vector3.Cross(normal, tangent).normalized;
            for (int side = 0; side < 2; side++)
            {
                int index = i * 2 + side;
                pinOffsets[index] = center + across * ((side - 0.5f) * strapWidth);
                positions[index] = anchor.position + anchor.rotation * pinOffsets[index];
                pinned[index] = i == 0 || i == sampleCount - 1;
                uv[index] = new Vector2(side, t * 4f);
                uv[index + pointCount] = uv[index];
            }
        }
        for (int i = 0; i < sampleCount; i++)
        {
            int a = i * 2;
            AddLink(constraints, a, a + 1, false); // Width.
            if (i + 1 < sampleCount)
            {
                AddLink(constraints, a, a + 2, false);
                AddLink(constraints, a + 1, a + 3, false);
                AddLink(constraints, a, a + 3, false); // Resist shearing.
                AddLink(constraints, a + 1, a + 2, false);
            }
            if (i + 2 < sampleCount)
            {
                AddLink(constraints, a, a + 4, true);
                AddLink(constraints, a + 1, a + 5, true);
            }
        }
        links = constraints.ToArray();
        int[] triangles = new int[(sampleCount - 1) * 12];
        for (int i = 0; i < sampleCount - 1; i++)
        {
            int a = i * 2;
            int k = i * 12;
            triangles[k] = a; triangles[k + 1] = a + 2; triangles[k + 2] = a + 1;
            triangles[k + 3] = a + 1; triangles[k + 4] = a + 2; triangles[k + 5] = a + 3;
            for (int j = 0; j < 6; j += 3)
            {
                triangles[k + 6 + j] = triangles[k + j] + pointCount;
                triangles[k + 7 + j] = triangles[k + j + 2] + pointCount;
                triangles[k + 8 + j] = triangles[k + j + 1] + pointCount;
            }
        }
        ribbonMesh.vertices = meshVertices;
        ribbonMesh.uv = uv;
        ribbonMesh.triangles = triangles;
    }

    private void AddLink(List<Link> list, int a, int b, bool bend)
    {
        list.Add(new Link { a = a, b = b,
            length = Vector3.Distance(positions[a], positions[b]), bend = bend });
    }

    private void FixedUpdate()
    {
        if (!initialized || !anchor) return;
        // Include rotation when deciding how finely to subdivide hand movement.
        Vector3 targetPosition = anchor.position;
        Quaternion targetRotation = anchor.rotation;
        float movement = Vector3.Distance(lastAnchorPosition, targetPosition) +
            Quaternion.Angle(lastAnchorRotation, targetRotation) * Mathf.Deg2Rad * strapWidth;
        int steps = Mathf.Clamp(Mathf.Max(substeps, Mathf.CeilToInt(movement / 0.015f)), 2, 16);
        float dt = Time.fixedDeltaTime / steps;
        probe.radius = collisionRadius;
        for (int step = 1; step <= steps; step++)
        {
            float fraction = (float)step / steps;
            Simulate(dt, Vector3.Lerp(lastAnchorPosition, targetPosition, fraction),
                Quaternion.Slerp(lastAnchorRotation, targetRotation, fraction));
        }
        lastAnchorPosition = targetPosition;
        lastAnchorRotation = targetRotation;
    }

    private void Simulate(float dt, Vector3 pinPosition, Quaternion pinRotation)
    {
        float decay = Mathf.Exp(-Mathf.Max(0f, damping) * dt);
        for (int i = 0; i < pointCount; i++)
        {
            stepStart[i] = positions[i];
            contacts[i] = Vector3.zero;
            if (pinned[i])
            {
                positions[i] = pinPosition + pinRotation * pinOffsets[i];
                continue;
            }
            velocities[i] = velocities[i] * decay + Physics.gravity * dt;
            positions[i] = MoveWithCollision(positions[i], positions[i] + velocities[i] * dt, ref contacts[i]);
        }

        for (int pass = 0; pass < solverIterations; pass++)
        {
            Array.Copy(positions, safePositions, pointCount);
            // Alternate constraint order to reduce directional bias.
            for (int k = 0; k < links.Length; k++)
            {
                Link link = links[(pass & 1) == 0 ? k : links.Length - 1 - k];
                float wa = pinned[link.a] ? 0f : 1f;
                float wb = pinned[link.b] ? 0f : 1f;
                if (wa + wb == 0f) continue;
                Vector3 delta = positions[link.b] - positions[link.a];
                float length = delta.magnitude;
                if (length < 0.000001f) continue;
                float stiffness = link.bend ? bendStiffness : 1f;
                Vector3 correction = delta * ((length - link.length) / length * stiffness / (wa + wb));
                positions[link.a] += correction * wa;
                positions[link.b] -= correction * wb;
            }
            // Constraints themselves can move points through thin surfaces.
            // Sweep those corrections too, and finish each pass with collision.
            for (int i = 0; i < pointCount; i++)
                if (!pinned[i])
                    positions[i] = MoveWithCollision(safePositions[i], positions[i], ref contacts[i]);
        }
        for (int i = 0; i < pointCount; i++)
        {
            if (pinned[i]) continue;
            Vector3 velocity = (positions[i] - stepStart[i]) / dt;
            if (contacts[i].sqrMagnitude > 0.01f)
            {
                Vector3 normal = contacts[i].normalized;
                float inward = Vector3.Dot(velocity, normal);
                if (inward < 0f) velocity -= normal * inward;
                Vector3 tangent = Vector3.ProjectOnPlane(velocity, normal);
                velocity -= tangent * surfaceFriction;
            }
            velocities[i] = Vector3.ClampMagnitude(velocity, 6f);
        }
    }

    private bool IsObstacle(Collider collider)
    {
        return collider && collider.enabled && !collider.isTrigger && !ignored.Contains(collider);
    }

    private Vector3 MoveWithCollision(Vector3 from, Vector3 to, ref Vector3 contact)
    {
        // Resolve pre-existing overlap before sweeping (casts alone cannot do this).
        Vector3 start = PushOut(from, ref contact);
        Vector3 remaining = to - from;
        for (int slide = 0; slide < 2 && remaining.sqrMagnitude > 0.000000001f; slide++)
        {
            float distance = remaining.magnitude;
            Vector3 direction = remaining / distance;
            int count;
            while (true)
            {
                count = Physics.SphereCastNonAlloc(start, collisionRadius, direction,
                    hits, distance + Skin, collisionLayers, QueryTriggerInteraction.Ignore);
                if (count < hits.Length) break;
                Array.Resize(ref hits, hits.Length * 2);
            }
            int closest = -1;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (!IsObstacle(hits[i].collider) || hits[i].distance >= nearest) continue;
                closest = i;
                nearest = hits[i].distance;
            }
            if (closest < 0)
            {
                start += remaining;
                break;
            }
            Vector3 normal = hits[closest].normal;
            contact += normal;
            Vector3 travel = direction * Mathf.Clamp(nearest - Skin, 0f, distance);
            start += travel;
            remaining -= travel;
            float intoSurface = Vector3.Dot(remaining, normal);
            if (intoSurface < 0f) remaining -= normal * intoSurface;
        }
        return PushOut(start, ref contact);
    }

    private Vector3 PushOut(Vector3 point, ref Vector3 contact)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            int count;
            while (true)
            {
                count = Physics.OverlapSphereNonAlloc(point, collisionRadius + Skin,
                    overlaps, collisionLayers, QueryTriggerInteraction.Ignore);
                if (count < overlaps.Length) break;
                Array.Resize(ref overlaps, overlaps.Length * 2);
            }
            bool moved = false;
            for (int i = 0; i < count; i++)
            {
                Collider obstacle = overlaps[i];
                if (!IsObstacle(obstacle)) continue;
                if (Physics.ComputePenetration(probe, point, Quaternion.identity,
                    obstacle, obstacle.transform.position, obstacle.transform.rotation,
                    out Vector3 normal, out float depth))
                {
                    point += normal * (depth + Skin);
                    contact += normal;
                    moved = true;
                }
            }
            if (!moved) break;
        }
        return point;
    }

    private void LateUpdate()
    {
        if (initialized) UpdateMesh();
    }

    private void UpdateMesh()
    {
        for (int i = 0; i < pointCount; i++)
        {
            meshVertices[i] = positions[i];
            meshVertices[i + pointCount] = positions[i];
        }
        ribbonMesh.vertices = meshVertices;
        ribbonMesh.RecalculateNormals();
        ribbonMesh.RecalculateBounds();
    }

    private void OnDisable()
    {
        if (!initialized) return;
        initialized = false;
        if (runtimeObject) runtimeObject.SetActive(false);
        if (originalRenderer) originalRenderer.enabled = originalRendererEnabled;
        if (originalCloth) originalCloth.enabled = originalClothEnabled;
        for (int i = 0; i < originalColliders.Length; i++)
            if (originalColliders[i]) originalColliders[i].enabled = originalColliderStates[i];
        if (runtimeObject) Destroy(runtimeObject);
        if (ribbonMesh) Destroy(ribbonMesh);
        ignored.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        if (!anchor || Application.isPlaying) return;
        GetBasis(out Vector3 forward, out Vector3 normal, out Vector3 right);
        Gizmos.color = Color.cyan;
        Vector3 previous = anchor.position + anchor.rotation * LoopPoint(0f, forward, right);
        for (int i = 1; i <= 40; i++)
        {
            Vector3 point = anchor.position + anchor.rotation * LoopPoint(i / 40f, forward, right);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(anchor.position, 0.006f);
    }
}
