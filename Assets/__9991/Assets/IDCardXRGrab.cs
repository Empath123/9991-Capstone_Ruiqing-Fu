using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Unity 6 + XR Interaction Toolkit 3.3.
/// Attach to the id-card-005-b root. This IS the grab interactable;
/// do not add a separate XR Grab Interactable to the same object.
/// Uses the controller's existing XRI Select input (normally Grip).
/// The card and lanyard move together as one rigid object.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
[AddComponentMenu("XR/ID Card XR Grab")]
public sealed class IDCardXRGrab : XRGrabInteractable
{
    [SerializeField, HideInInspector]
    private bool defaultsApplied;

    // Runs when this component is first added in the Unity Editor.
#if UNITY_EDITOR
    protected override void Reset()
    {
        base.Reset();
        ApplyCardDefaults();
        FitBoxToCard();
    }
#endif

    protected override void Awake()
    {
        // Also supports components added at runtime. Configure everything
        // before XRI caches the Rigidbody and registers the interactable.
        if (!defaultsApplied)
        {
            ApplyCardDefaults();
            FitBoxToCard();
        }

        base.Awake();
    }

    private void ApplyCardDefaults()
    {
        Rigidbody body = GetComponent<Rigidbody>();
        body.mass = 0.05f;
        body.isKinematic = false;
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.None;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.maxAngularVelocity = 20f;

        BoxCollider box = GetComponent<BoxCollider>();
        box.enabled = true;
        box.isTrigger = false;

        colliders.Clear();
        colliders.Add(box);

        movementType = MovementType.VelocityTracking;
        useDynamicAttach = true;
        matchAttachPosition = true;
        matchAttachRotation = true;
        snapToColliderVolume = true;
        attachEaseInTime = 0.15f;

        trackPosition = true;
        trackRotation = true;
        trackScale = false;
        smoothPosition = false;
        smoothRotation = false;
        addDefaultGrabTransformers = true;

        // Release the card gently; gravity makes it drop onto the surface.
        throwOnDetach = false;
        defaultsApplied = true;
    }

    // Re-run from this component's three-dot menu if the model changes.
    // Manual Box Collider edits are otherwise preserved on entering Play.
    [ContextMenu("Fit Box Collider To Card")]
    private void FitBoxToCard()
    {
        if (Application.isPlaying && isSelected)
            return;

        Bounds combined = default;
        bool foundMesh = false;

        foreach (Renderer visual in GetComponentsInChildren<Renderer>())
        {
            if (!visual.enabled ||
                (!(visual is MeshRenderer) && !(visual is SkinnedMeshRenderer)))
                continue;

            // Convert each local bounding-box corner to the root's space.
            // This works even when the root or child meshes are rotated.
            Bounds local = visual.localBounds;
            Matrix4x4 toRoot = transform.worldToLocalMatrix *
                                 visual.transform.localToWorldMatrix;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f,
                                (i & 2) == 0 ? -1f : 1f,
                                (i & 4) == 0 ? -1f : 1f));
                Vector3 point = toRoot.MultiplyPoint3x4(corner);

                if (!foundMesh)
                {
                    combined = new Bounds(point, Vector3.zero);
                    foundMesh = true;
                }
                else
                {
                    combined.Encapsulate(point);
                }
            }
        }

        if (!foundMesh)
        {
            Debug.LogWarning("IDCardXRGrab: No visible mesh found. " +
                "Set the Box Collider Center and Size manually.", this);
            return;
        }

        // A thin card still needs some collider thickness (minimum 4 mm
        // in world space). The collider is a simple box, not a cloth model.
        Vector3 scale = transform.lossyScale;
        Vector3 size = combined.size;
        size.x = Mathf.Max(size.x, 0.004f / Mathf.Max(Mathf.Abs(scale.x), 0.0001f));
        size.y = Mathf.Max(size.y, 0.004f / Mathf.Max(Mathf.Abs(scale.y), 0.0001f));
        size.z = Mathf.Max(size.z, 0.004f / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));

        BoxCollider box = GetComponent<BoxCollider>();
        box.center = combined.center;
        box.size = size;
    }
}
