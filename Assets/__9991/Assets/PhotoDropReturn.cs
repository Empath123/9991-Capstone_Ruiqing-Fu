using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Unity 6 / XR Interaction Toolkit 3.x. Attach beside Photo Frame XR Grab.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class PhotoDropReturn : MonoBehaviour
{
    [Tooltip("Desk and allowed props. Each entry includes its child colliders.")]
    [SerializeField] private Transform[] allowedSurfaces = new Transform[0];

    [Tooltip("Optional: XR Origin or separate hand collider roots. These do not count as landings.")]
    [SerializeField] private Transform[] ignoredRoots = new Transform[0];

    private Rigidbody body;
    private XRBaseInteractable grab;
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private bool watchingDrop;
    private bool resetPending;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        grab = GetComponent<XRBaseInteractable>();
        homePosition = transform.position;
        homeRotation = transform.rotation;
    }

    private void OnEnable()
    {
        if (grab == null) return;
        grab.firstSelectEntered.AddListener(OnGrab);
        grab.lastSelectExited.AddListener(OnRelease);
    }

    private void OnDisable()
    {
        if (grab != null)
        {
            grab.firstSelectEntered.RemoveListener(OnGrab);
            grab.lastSelectExited.RemoveListener(OnRelease);
        }
        watchingDrop = resetPending = false;
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        watchingDrop = resetPending = false;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        watchingDrop = true;
        body.WakeUp();
    }

    private void OnCollisionEnter(Collision hit) => CheckDrop(hit);
    private void OnCollisionStay(Collision hit) => CheckDrop(hit);

    private void CheckDrop(Collision hit)
    {
        if (!isActiveAndEnabled || !watchingDrop || grab == null || grab.isSelected) return;
        Transform other = hit.collider.transform;
        if (IsUnder(other, ignoredRoots) || IsUnder(other, allowedSurfaces)) return;
        resetPending = true;
    }

    private static bool IsUnder(Transform other, Transform[] roots)
    {
        foreach (Transform root in roots)
            if (root != null && (other == root || other.IsChildOf(root))) return true;
        return false;
    }

    private void FixedUpdate()
    {
        if (!resetPending || grab == null || grab.isSelected) return;
        watchingDrop = resetPending = false;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.position = homePosition;
        body.rotation = homeRotation;
    }
}
