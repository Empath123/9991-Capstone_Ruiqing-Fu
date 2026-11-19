using UnityEngine;
using UnityEngine.XR;
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class VRPushDoor : MonoBehaviour
{
    [Header("Door and handle")]
    [SerializeField] private Transform door;
    [SerializeField] private BoxCollider doorPanel;
    [SerializeField] private Transform handle;
    [Tooltip("Optional grip tip. If empty, use the handle mesh bounds.")]
    [SerializeField] private Transform tip;

    [Header("Tracked palm points")]
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;
    [SerializeField, Min(0.01f)] private float handleTouchRadius = 0.065f;

    private const float LeverLimit = 60f;

    [Header("Motion (degrees)")]
    [SerializeField, Range(10f, 150f)] private float doorLimit = 95f;
    [SerializeField] private bool reverseOpeningDirection;
    [Tooltip("Distance (metres) at which a moving palm can push the unlatched door panel.")]
    [SerializeField, Min(0.001f)] private float panelPushRadius = 0.035f;

    [Header("Editor testing")]
    [Tooltip("Enable for G = right and Shift + G = left. Disable for a headset connected to the Editor.")]
    [SerializeField] private bool editorKeyboard = true;

    private Transform holdingHand;
    private Vector3 leverLocalAxis;
    private Quaternion restingHandleRotation;
    private Quaternion closedDoorRotation;
    private MeshFilter handleMesh;
    private Vector3 panelCentre;
    private float openingSign;
    private float leverDegrees;
    private float doorDegrees;
    private float angleAtGrab;
    private Vector3 radialAtGrab;
    private Vector3 previousPalm;
    private bool leftWasHeld;
    private bool rightWasHeld;
    // The latch belongs to the door, not to the hand holding the lever.
    private bool unlatched;
    private bool depressLeverOnThisGrab;
    private bool leftMustRelease, rightMustRelease;
    private Vector3? lastLeftPalm, lastRightPalm;
    private float SwingSign => reverseOpeningDirection ? -openingSign : openingSign;
#if UNITY_EDITOR
    private bool gWasHeld;
    private bool gAssignedToLeft;
#endif

    private void Start()
    {
        if (!door || !doorPanel || !handle || (!leftHand && !rightHand))
        {
            Debug.LogError("VRPushDoor: assign the door, panel collider, handle and tracked hands.", this);
            enabled = false;
            return;
        }

        handleMesh = handle.GetComponent<MeshFilter>();
        if (!tip && (!handleMesh || !handleMesh.sharedMesh))
        {
            Debug.LogError("VRPushDoor: assign Tip when the handle has no Mesh Filter.", this);
            enabled = false;
            return;
        }

        restingHandleRotation = handle.localRotation;
        closedDoorRotation = door.localRotation;
        panelCentre = door.InverseTransformPoint(doorPanel.transform.TransformPoint(doorPanel.center));
        Vector3 size = Vector3.Scale(doorPanel.size, doorPanel.transform.lossyScale);
        float x = Mathf.Abs(size.x), y = Mathf.Abs(size.y), z = Mathf.Abs(size.z);
        Vector3 thinAxis = x <= y && x <= z ? Vector3.right : y <= z ? Vector3.up : Vector3.forward;
        Vector3 axle = doorPanel.transform.TransformDirection(thinAxis);
        Vector3 gripPoint = tip ? tip.position : handle.transform.TransformPoint(handleMesh.sharedMesh.bounds.center);
        // Choose the spindle direction that lowers the free end.
        if (Vector3.Dot(Vector3.Cross(axle, gripPoint - handle.position), Vector3.down) < 0f)
            axle = -axle;
        leverLocalAxis = handle.InverseTransformDirection(axle).normalized;
    }

    private void OnDisable()
    {
        holdingHand = null;
        lastLeftPalm = lastRightPalm = null;
        depressLeverOnThisGrab = false;
        leftWasHeld = rightWasHeld = false;
#if UNITY_EDITOR
        gWasHeld = false;
#endif
        if (handle && leverDegrees != 0f)
            SetLever(0f);
    }

    private void LateUpdate()
    {
        ReadGrips(out bool leftHeld, out bool rightHeld);
        leftMustRelease &= leftHeld;
        rightMustRelease &= rightHeld;
        if (holdingHand && (!(holdingHand == leftHand ? leftHeld : rightHeld) ||
                            !holdingHand.gameObject.activeInHierarchy))
            holdingHand = null;

        if (!holdingHand)
        {
            if (leftHeld && !leftMustRelease)
                TryGrab(leftHand);
            if (!holdingHand && rightHeld && !rightMustRelease)
                TryGrab(rightHand);
        }
        leftWasHeld = leftHeld;
        rightWasHeld = rightHeld;

        Vector3? leftPalm = TrackedPalm(leftHand);
        Vector3? rightPalm = TrackedPalm(rightHand);
        if (holdingHand)
        {
            Vector3 palm = holdingHand.position;
            // A tracking jump or walking away from the handle releases the grip.
            if (Vector3.Distance(palm, previousPalm) > 0.30f)
                holdingHand = null;
            else
            {
                previousPalm = palm;
                if (unlatched || leverDegrees >= LeverLimit * 0.9f)
                    FollowSwing(palm);
                if (DistanceToLever(palm) > Mathf.Max(0.22f, handleTouchRadius * 2f))
                    holdingHand = null;
            }
        }
        else if (unlatched && doorPanel.enabled && doorPanel.gameObject.activeInHierarchy)
        {
            // Both hands may push, but simultaneous contact must not double the speed.
            float push = Mathf.Max(PanelPush(lastLeftPalm, leftPalm), PanelPush(lastRightPalm, rightPalm));
            if (push > 0f)
                SetDoorAngle(doorDegrees + push);
        }
        lastLeftPalm = leftPalm;
        lastRightPalm = rightPalm;
        float leverTarget = holdingHand && depressLeverOnThisGrab ? LeverLimit : 0f;
        SetLever(Mathf.MoveTowards(leverDegrees, leverTarget, 360f * Time.deltaTime));
    }

    private float DistanceToLever(Vector3 point)
    {
        if (!tip)
        {
            Vector3 local = handle.InverseTransformPoint(point);
            Vector3 nearest = handleMesh.sharedMesh.bounds.ClosestPoint(local);
            return Vector3.Distance(point, handle.TransformPoint(nearest));
        }
        Vector3 lever = tip.position - handle.position;
        float along = Mathf.Clamp01(Vector3.Dot(point - handle.position, lever) /
                                    Mathf.Max(lever.sqrMagnitude, 0.000001f));
        return Vector3.Distance(point, handle.position + along * lever);
    }

    private void TryGrab(Transform hand)
    {
        if (!hand || !hand.gameObject.activeInHierarchy || DistanceToLever(hand.position) > handleTouchRadius)
            return;
        Vector3 radial = Vector3.ProjectOnPlane(hand.position - door.position, door.up);
        if (radial.sqrMagnitude < 0.01f)
            return;
        // Establish the permitted side once, including across releases and re-grabs.
        if (openingSign == 0f)
        {
            Vector3 centre = door.TransformPoint(panelCentre);
            Vector3 tangent = Vector3.Cross(door.up, centre - door.position);
            openingSign = Vector3.Dot(tangent, hand.position - centre) >= 0f ? -1f : 1f;
        }
        holdingHand = hand;
        depressLeverOnThisGrab = !unlatched;
        radialAtGrab = radial;
        angleAtGrab = doorDegrees;
        previousPalm = hand.position;
    }

    private void FollowSwing(Vector3 palm)
    {
        Vector3 radial = Vector3.ProjectOnPlane(palm - door.position, door.up);
        if (radial.sqrMagnitude < 0.0001f) return;
        float swingDelta = SwingSign * Vector3.SignedAngle(radialAtGrab, radial, door.up);
        SetDoorAngle(angleAtGrab + swingDelta);
    }

    private void SetDoorAngle(float requested)
    {
        float next = Mathf.Clamp(requested, 0f, doorLimit);
        // Only snap on a closing movement, never while the first push is starting.
        if (unlatched && next < doorDegrees && next <= 0.1f)
        {
            next = 0f;
            unlatched = false;
            holdingHand = null;
            depressLeverOnThisGrab = false;
            leftMustRelease = leftWasHeld;
            rightMustRelease = rightWasHeld;
            SetLever(0f);
        }
        else if (next > 0f)
            unlatched = true;
        door.localRotation = closedDoorRotation * Quaternion.AngleAxis(SwingSign * next, Vector3.up);
        doorDegrees = next;
    }

    private static Vector3? TrackedPalm(Transform hand)
    {
        return hand && hand.gameObject.activeInHierarchy ? (Vector3?)hand.position : null;
    }

    private float PanelPush(Vector3? previous, Vector3? current)
    {
        if (!previous.HasValue || !current.HasValue) return 0f;
        Vector3 a = previous.Value, b = current.Value;
        Vector3 motion = b - a;
        if (motion.sqrMagnitude > 0.30f * 0.30f) return 0f;
        Vector3 centre = door.TransformPoint(panelCentre);
        Vector3 forward = SwingSign * Vector3.Cross(door.up, centre - door.position).normalized;
        if (Vector3.Dot(a - centre, forward) > 0.003f || Vector3.Dot(motion, forward) <= 0f)
            return 0f; // Retreating from the other face is not a push.
        if (!NearPanel(a) && !NearPanel(b)) return 0f;
        Vector3 from = Vector3.ProjectOnPlane(a - door.position, door.up);
        Vector3 to = Vector3.ProjectOnPlane(b - door.position, door.up);
        if (Mathf.Min(from.sqrMagnitude, to.sqrMagnitude) < 0.01f) return 0f;
        return Mathf.Max(0f, SwingSign * Vector3.SignedAngle(from, to, door.up));
    }

    private bool NearPanel(Vector3 point)
    {
        // Use the current Transform, including a moving hinge, without a physics-sync delay.
        Transform t = doorPanel.transform;
        Bounds box = new Bounds(doorPanel.center, doorPanel.size);
        Vector3 nearest = t.TransformPoint(box.ClosestPoint(t.InverseTransformPoint(point)));
        return (nearest - point).sqrMagnitude <= panelPushRadius * panelPushRadius;
    }

    private void SetLever(float degrees)
    {
        leverDegrees = Mathf.Clamp(degrees, 0f, LeverLimit);
        handle.localRotation = restingHandleRotation * Quaternion.AngleAxis(leverDegrees, leverLocalAxis);
    }

#if UNITY_EDITOR
    private string HandStatus(Transform hand, bool pressed)
    {
        if (!hand || !hand.gameObject.activeInHierarchy)
            return "missing / inactive";
        return $"{(pressed ? "ON" : "off")}   {DistanceToLever(hand.position) * 100f:F1} cm";
    }

    private void OnGUI()
    {
        if (!enabled || (!tip && !handleMesh)) return;
        string held = holdingHand ? holdingHand.name : "none";
        GUI.Box(new Rect(8, Screen.height - 145, 290, 78),
            $"Door grip   |   Range {handleTouchRadius * 100f:F1} cm\n" +
            $"Left: {HandStatus(leftHand, leftWasHeld)}   Right: {HandStatus(rightHand, rightWasHeld)}\nHolding: {held}");
    }
#endif

    private void ReadGrips(out bool left, out bool right)
    {
#if UNITY_EDITOR
        if (editorKeyboard)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            bool g = keyboard != null && keyboard.gKey.isPressed;
            bool shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
#else
            bool g = Input.GetKey(KeyCode.G);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
            if (g && !gWasHeld)
                gAssignedToLeft = shift;
            gWasHeld = g;
            left = g && gAssignedToLeft;
            right = g && !gAssignedToLeft;
            return;
        }
#endif
        left = ControllerGrip(XRNode.LeftHand, leftWasHeld);
        right = ControllerGrip(XRNode.RightHand, rightWasHeld);
    }

    private static bool ControllerGrip(XRNode node, bool wasHeld)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return false;
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float value))
            return value >= (wasHeld ? 0.35f : 0.65f);
        return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool pressed) && pressed;
    }
}
