using UnityEngine;

// Put this on id-card-005-b, next to the Rigidbody and the existing drop-return script.
// It controls the strap's appearance at its home pose; it never moves or locks the card.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(50)] // After the default-order drop reset, before the ribbon solver (100).
public sealed class IDCardStrapAntiStuck : MonoBehaviour
{
    [Tooltip("The existing physics component on lanyard-001-b, NOT the generated Runtime Ribbon.")]
    [SerializeField] private IDCardLanyardPhysics lanyardPhysics;

    [Header("Show the original flat strap near its starting pose")]
    [SerializeField, Min(0.001f)] private float returnDistance = 0.025f; // metres
    [SerializeField, Range(0f, 89f)] private float returnAngle = 8f;

    [Header("Resume the hanging simulation after lifting or tilting the card")]
    [SerializeField, Min(0.002f)] private float liftDistance = 0.055f; // metres
    [SerializeField, Range(1f, 180f)] private float liftAngle = 18f;

    private Rigidbody body;
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private bool parked;
    private bool started;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        // Match the world pose captured by PhotoDropReturn.Awake.
        homePosition = transform.position;
        homeRotation = transform.rotation;
        if (!lanyardPhysics)
            lanyardPhysics = GetComponentInChildren<IDCardLanyardPhysics>(true);
    }

    private void Start()
    {
        if (!lanyardPhysics || !lanyardPhysics.isActiveAndEnabled ||
            !lanyardPhysics.transform.IsChildOf(transform))
        {
            Debug.LogWarning("IDCardStrapAntiStuck: assign and enable the lanyard physics on this card's strap.", this);
            enabled = false;
            return;
        }
        started = true;
        CheckPose();
    }

    private void FixedUpdate() => CheckPose();
    private void LateUpdate() => CheckPose();

    private void CheckPose()
    {
        if (!started || !lanyardPhysics) return;

        float distanceSquared = (body.position - homePosition).sqrMagnitude;
        float angle = Quaternion.Angle(body.rotation, homeRotation);

        if (!parked)
        {
            if (distanceSquared > returnDistance * returnDistance || angle > returnAngle)
                return;
            if (!lanyardPhysics.enabled) return;
            // The solver's OnDisable hides its world-space ribbon and restores the original mesh.
            parked = true;
            lanyardPhysics.enabled = false;
        }
        else if (distanceSquared >= liftDistance * liftDistance || angle >= liftAngle)
        {
            // OnEnable seeds a fresh ribbon from the clip, not from last drop's particle positions.
            parked = false;
            lanyardPhysics.enabled = true;
        }
    }

    private void OnDisable()
    {
        // Only undo a disable that this component actually performed.
        if (parked && lanyardPhysics) lanyardPhysics.enabled = true;
        parked = false;
    }

    private void OnValidate()
    {
        liftDistance = Mathf.Max(returnDistance + 0.001f, liftDistance);
        liftAngle = Mathf.Max(returnAngle + 1f, liftAngle);
    }
}
