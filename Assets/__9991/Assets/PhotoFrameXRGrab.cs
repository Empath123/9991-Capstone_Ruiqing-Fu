using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
public sealed class PhotoFrameXRGrab : XRGrabInteractable
{
    protected override void Awake()
    {
        // Paint_01 already has a fitted box. Keep its center and size.
        var frameBox = GetComponent<BoxCollider>();
        colliders.Clear();
        colliders.Add(frameBox);

        useDynamicAttach = true;
        matchAttachPosition = true;
        matchAttachRotation = true;
        snapToColliderVolume = true;
        trackPosition = true;
        trackRotation = true;
        trackScale = false;
        movementType = MovementType.VelocityTracking;
        throwOnDetach = false;

        var body = GetComponent<Rigidbody>();
        body.isKinematic = false;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        base.Awake();

        // Preserve the placed pose until picked up or disturbed.
        body.Sleep();
    }
}
