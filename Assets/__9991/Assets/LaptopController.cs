using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// Put this on the GrabZone whose BoxCollider is assigned to VRLaptopLid.grabZone.
/// Unlocks once per scene after the photo and the work card have each been picked up and released.
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
[DefaultExecutionOrder(-300)]
public sealed class LaptopGrabGate : MonoBehaviour
{
    [Tooltip("拖入合照 Paint_01（挂有 PhotoFrameXRGrab 的场景物体）。")]
    [SerializeField] private XRGrabInteractable photo;

    [Tooltip("拖入工作证 id-card-005-b（挂有 IDCardXRGrab 的场景物体）。")]
    [SerializeField] private XRGrabInteractable workCard;

    private BoxCollider grabBox;
    private XRGrabInteractable boundPhoto, boundCard;
    private bool photoDone, cardDone;

    private void Awake()
    {
        grabBox = GetComponent<BoxCollider>();
        grabBox.enabled = false; // Keep this GameObject active to receive both release events.
        photoDone = cardDone = false;
    }

    private void OnEnable()
    {
        if (photo == null || workCard == null || photo.gameObject == workCard.gameObject)
        {
            grabBox.enabled = false;
            Debug.LogWarning("LaptopGrabGate：Photo 和 Work Card 必须分别拖入两个不同的抓取物体。", this);
            return;
        }

        boundPhoto = photo;
        boundCard = workCard;
        boundPhoto.lastSelectExited.AddListener(OnPhotoReleased);
        boundCard.lastSelectExited.AddListener(OnCardReleased);
        grabBox.enabled = photoDone && cardDone;
    }

    private static bool PlayerReleased(SelectExitEventArgs e)
    {
        // A disabled object or an automatic socket releasing an item isn't a player interaction.
        return e != null && !e.isCanceled && e.interactorObject != null
            && !(e.interactorObject is XRSocketInteractor);
    }

    private void OnPhotoReleased(SelectExitEventArgs e)
    {
        if (!PlayerReleased(e)) return;
        photoDone = true;
        grabBox.enabled = photoDone && cardDone;
    }

    private void OnCardReleased(SelectExitEventArgs e)
    {
        if (!PlayerReleased(e)) return;
        cardDone = true;
        grabBox.enabled = photoDone && cardDone;
    }

    private void OnDisable()
    {
        if (boundPhoto != null) boundPhoto.lastSelectExited.RemoveListener(OnPhotoReleased);
        if (boundCard != null) boundCard.lastSelectExited.RemoveListener(OnCardReleased);
        boundPhoto = boundCard = null;
        if (grabBox != null) grabBox.enabled = false;
    }
}
