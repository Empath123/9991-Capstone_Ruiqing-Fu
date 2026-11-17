using UnityEngine;
using UnityEngine.XR;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRFeature = UnityEngine.XR.CommonUsages;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// One-axis laptop lid controlled by a nearby VR hand's position.
/// Input System Grip supports XR Interaction Simulator and hardware.
/// Fully opening the lid locks it for this scene instance and switches black to white.
/// Replace the old .cs IN PLACE and keep its .meta file to preserve scene references.
/// In the Editor, Editor Keyboard Grab overrides grip: HOLD G = RIGHT hand.
/// Disable that option for the simulator's own Grip controls. Builds ignore it.
/// The hinge's rotation at Awake is the CLOSED reference pose.
/// Do not combine with a HingeJoint, XRGrabInteractable or rotation animation.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class VRLaptopLid : MonoBehaviour
{
    [Header("Hinge / 转轴")]
    [Tooltip("Empty object placed at the physical hinge. Defaults to this Transform.")]
    public Transform hinge;
    [Tooltip("Axis in the CLOSED hinge's local space. Reverse its sign if opening is backwards.")]
    public Vector3 localHingeAxis = Vector3.left;
    [Range(1f, 175f)] public float maximumAngle = 115f;
    [Range(0f, 175f)] public float initialAngle;
    [Tooltip("Maximum following speed. Degrees per second.")]
    [Min(1f)] public float followSpeed = 540f;

    [Header("Screen / 屏幕")]
    [Tooltip("Black screen: drag the separate Screen object here. Hidden once fully opened.")]
    public GameObject screenOffContent;
    [Tooltip("White screen: drag the separate Screen Open object here. Shown only after the lid locks at Maximum Angle.")]
    public GameObject screenOnContent;
    [Tooltip("Optional light. Turns on when the lid locks fully open.")]
    public Light screenGlow;

    [Header("Grab zone / 抓取区域")]
    [Tooltip("BoxCollider on the lid's FRONT edge, away from its hinge. Can be a trigger.")]
    public BoxCollider grabZone;
    [Tooltip("Allowed distance from the hand Transform to the grab box, in world metres.")]
    [Min(0f)] public float grabDistance = 0.15f;
    [Tooltip("Release when the hand moves this far from the original point on the lid.")]
    [Min(0.05f)] public float releaseDistance = 0.40f;
    [Tooltip("Ignore angular samples very close to the hinge axis.")]
    [Min(0.001f)] public float minimumHingeRadius = 0.025f;

    [Header("Tracked hands / 跟踪手部")]
    [Tooltip("Actual tracked left hand/controller Transform, not the player camera.")]
    public Transform leftHand;
    public Transform rightHand;
    [Tooltip("On: read Input System Grip with hardware XR fallback. Off: use Begin/EndGrab events. Editor Keyboard Grab overrides this in the Editor.")]
    public bool readControllerGrip = true;
    [Range(0f, 1f)] public float gripPressThreshold = 0.65f;
    [Range(0f, 1f)] public float gripReleaseThreshold = 0.35f;

    [Header("Editor test / 编辑器测试")]
    [Tooltip("Editor only: HOLD G to grab with Right Hand; release G to let go. Overrides both simulated grips. Turn off to use simulator Grip or external events. Ignored in builds.")]
    public bool editorKeyboardGrab = true;
    [Tooltip("Show grip, distance and status in Game view while running in the Editor.")]
    public bool showDebugOverlay = true;

    [Header("Runtime / 运行状态")]
    [SerializeField] private float leftGripValue;
    [SerializeField] private float rightGripValue;
    [Tooltip("World metres from controller Transform to Grab Zone. -1 = unavailable.")]
    [SerializeField] private float leftDistanceMetres = -1f;
    [SerializeField] private float rightDistanceMetres = -1f;
    [SerializeField] private string grabStatus = "Not running";
    [SerializeField, Range(0f, 175f)] private float currentAngle;
    public float CurrentAngle => currentAngle;
    public bool IsHeld => heldBy != 0;
    public bool IsLockedOpen => lockedOpen;

    // A one-way runtime latch. Re-enabling the component does not unlock it.
    private bool lockedOpen;
    private float lockedAngle;
    private const float LockAngleTolerance = 0.01f;

    private Quaternion closedLocalRotation;
    private Vector3 closedLocalPosition;
    private Transform referenceParent;
    private Vector3 axis;
    private bool ready;
    private bool inputFocused = true;
    private int heldBy; // 0: none, 1: left, 2: right
    private bool blockLeftUntilRelease, blockRightUntilRelease;
    private bool externalLeftGrip, externalRightGrip;
    private bool controllerLeftGrip, controllerRightGrip;
    private XRInputDevice leftDevice, rightDevice;
#if ENABLE_INPUT_SYSTEM
    private InputAction leftGripAction, rightGripAction;
#endif
    private Vector3 previousDirection, grabbedLocalPoint;
    private bool hasPreviousDirection;
    private float angleAtGrab, accumulatedHandAngle;

    private void Awake()
    {
        if (hinge == null) hinge = transform;
        axis = localHingeAxis.sqrMagnitude > 0.000001f
            ? localHingeAxis.normalized : Vector3.right;
        referenceParent = hinge.parent;
        closedLocalRotation = hinge.localRotation;
        closedLocalPosition = hinge.localPosition;
        lockedOpen = false;
        ValidateScreenReferences();
        currentAngle = Mathf.Clamp(initialAngle, 0f, maximumAngle);
        ready = true;
        ApplyAngle();

        if (grabZone == null)
            Debug.LogWarning("VRLaptopLid: assign a BoxCollider to Grab Zone before grabbing.", this);
        else if (grabZone.transform != hinge && !grabZone.transform.IsChildOf(hinge))
            Debug.LogWarning("VRLaptopLid: Grab Zone should be a child of Hinge so it follows the lid.", this);
        if (leftHand == null && rightHand == null)
            Debug.LogWarning("VRLaptopLid: assign at least one tracked hand Transform.", this);
    }

    private void OnEnable()
    {
        inputFocused = true;
        blockLeftUntilRelease = blockRightUntilRelease = false;
#if ENABLE_INPUT_SYSTEM
        if (leftGripAction == null) leftGripAction = CreateGripAction("LeftHand");
        if (rightGripAction == null) rightGripAction = CreateGripAction("RightHand");
        leftGripAction.Enable();
        rightGripAction.Enable();
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static InputAction CreateGripAction(string usage)
    {
        // XRSimulatedController derives from XRController. No InputAction asset wiring needed.
        var action = new InputAction("LaptopGrip_" + usage, InputActionType.Value);
        string prefix = "<XRController>{" + usage + "}/";
        action.AddBinding(prefix + "grip");
        action.AddBinding(prefix + "gripPressed");
        action.AddBinding(prefix + "gripButton");
        return action;
    }
#endif

    private void LateUpdate()
    {
        if (!ready || hinge == null) return;
        leftDistanceMetres = HandDistance(leftHand);
        rightDistanceMetres = HandDistance(rightHand);
        // Run before grip/focus handling: neither hand nor Inspector angle edits can unlock it.
        if (lockedOpen)
        {
            ApplyAngle();
            return;
        }
        // Keep angle application alive when Game view loses focus to Inspector.
        if (!inputFocused)
        {
            CancelGrab();
            leftGripValue = rightGripValue = 0f;
            grabStatus = "Click Game view / 点击 Game 窗口";
            ApplyAngle();
            return;
        }
        if (hinge.parent != referenceParent)
        {
            Debug.LogError("VRLaptopLid: do not reparent Hinge at runtime. Move its parent instead.", this);
            enabled = false;
            return;
        }

        bool leftPressed, rightPressed;
#if UNITY_EDITOR
        if (editorKeyboardGrab)
        {
            bool gHeld = false;
#if ENABLE_INPUT_SYSTEM
            gHeld = Keyboard.current != null && Keyboard.current.gKey.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            gHeld = Input.GetKey(KeyCode.G);
#endif
            // Exclusive override: releasing G always releases, even if simulator Grip is latched.
            leftGripValue = 0f;
            rightGripValue = gHeld ? 1f : 0f;
            leftPressed = false;
            rightPressed = gHeld;
        }
        else
#endif
        {
            leftGripValue = readControllerGrip ? ReadGripValue(true) : (externalLeftGrip ? 1f : 0f);
            rightGripValue = readControllerGrip ? ReadGripValue(false) : (externalRightGrip ? 1f : 0f);
            controllerLeftGrip = leftGripValue >= (controllerLeftGrip ? gripReleaseThreshold : gripPressThreshold);
            controllerRightGrip = rightGripValue >= (controllerRightGrip ? gripReleaseThreshold : gripPressThreshold);
            leftPressed = controllerLeftGrip;
            rightPressed = controllerRightGrip;
        }
        if (!leftPressed) blockLeftUntilRelease = false;
        if (!rightPressed) blockRightUntilRelease = false;
        leftPressed &= HandActive(leftHand);
        rightPressed &= HandActive(rightHand);

        if ((heldBy == 1 && !leftPressed) || (heldBy == 2 && !rightPressed)) Release();

        if (grabZone == null || !grabZone.enabled || !grabZone.gameObject.activeInHierarchy)
        {
            Release();
            grabStatus = "Assign/enable Grab Zone / 检查抓取框";
        }
        else if (heldBy == 0)
        {
            grabStatus = "Ready: move hand to box and hold grip / 靠近后握持";
            if (!HandActive(leftHand) && !HandActive(rightHand))
                grabStatus = "Assign/enable hands / 检查手部引用";
#if UNITY_EDITOR
            else if (editorKeyboardGrab && !HandActive(rightHand))
                grabStatus = "Assign/enable Right Hand / 检查右手引用";
#endif
            // Retry while held: a grip press before reaching the box is now accepted.
            if (rightPressed && !blockRightUntilRelease) TryGrab(2, rightHand);
            if (heldBy == 0 && leftPressed && !blockLeftUntilRelease) TryGrab(1, leftHand);
            if (heldBy == 0 && ((leftPressed && blockLeftUntilRelease) || (rightPressed && blockRightUntilRelease)))
                grabStatus = "Release grip then grab again / 松开后重新抓取";
        }

        if (heldBy != 0) FollowHand(heldBy == 1 ? leftHand : rightHand);
        ApplyAngle();
    }

    private float ReadGripValue(bool left)
    {
#if ENABLE_INPUT_SYSTEM
        InputAction action = left ? leftGripAction : rightGripAction;
        if (action != null && action.enabled && action.controls.Count > 0)
            return Mathf.Clamp01(action.ReadValue<float>());
#endif
        if (left) return ReadHardwareGrip(XRNode.LeftHand, ref leftDevice);
        return ReadHardwareGrip(XRNode.RightHand, ref rightDevice);
    }

    private static float ReadHardwareGrip(XRNode node, ref XRInputDevice device)
    {
        if (!device.isValid) device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid ||
            (device.TryGetFeatureValue(XRFeature.isTracked, out bool tracked) && !tracked)) return 0f;
        if (device.TryGetFeatureValue(XRFeature.grip, out float value)) return Mathf.Clamp01(value);
        if (device.TryGetFeatureValue(XRFeature.gripButton, out bool button)) return button ? 1f : 0f;
        return 0f;
    }

    private float HandDistance(Transform hand)
    {
        if (!HandActive(hand) || grabZone == null) return -1f;
        return Vector3.Distance(hand.position, ClosestPointOnBox(grabZone, hand.position));
    }

    private static bool HandActive(Transform hand)
    {
        return hand != null && hand.gameObject.activeInHierarchy;
    }

    private void TryGrab(int side, Transform hand)
    {
        if (lockedOpen) return;
        if (!HandActive(hand) || grabZone == null || !grabZone.enabled ||
            !grabZone.gameObject.activeInHierarchy) return;
        Vector3 contact = ClosestPointOnBox(grabZone, hand.position);
        if ((contact - hand.position).sqrMagnitude > grabDistance * grabDistance)
        {
            grabStatus = "Grip received; hand too far / 收到握持，请靠近抓取框";
            return;
        }
        if (!TryHandDirection(hand.position, out Vector3 direction))
        {
            grabStatus = "Too close to hinge axis / 请抓远离转轴的一边";
            return;
        }
        grabStatus = side == 2 ? "Right hand grabbed / 右手已抓住" : "Left hand grabbed / 左手已抓住";

        heldBy = side;
        grabbedLocalPoint = hinge.InverseTransformPoint(contact);
        angleAtGrab = currentAngle;
        accumulatedHandAngle = 0f;
        previousDirection = direction;
        hasPreviousDirection = true;
    }

    private void FollowHand(Transform hand)
    {
        if (lockedOpen) return;
        if (!HandActive(hand)) { Release(); return; }
        Vector3 attachedPoint = hinge.TransformPoint(grabbedLocalPoint);
        if ((hand.position - attachedPoint).sqrMagnitude > releaseDistance * releaseDistance)
        {
            BreakGrab();
            return;
        }
        if (!TryHandDirection(hand.position, out Vector3 direction))
        {
            hasPreviousDirection = false;
            return;
        }
        if (!hasPreviousDirection)
        {
            // Rebase after passing close to the axis to avoid a 180-degree flip.
            previousDirection = direction;
            angleAtGrab = currentAngle;
            accumulatedHandAngle = 0f;
            hasPreviousDirection = true;
            return;
        }
        float delta = Vector3.SignedAngle(previousDirection, direction, axis);
        previousDirection = direction;
        if (Mathf.Abs(delta) > 75f) // tracking jump / teleport in a single frame
        {
            BreakGrab();
            return;
        }
        accumulatedHandAngle = Mathf.Clamp(accumulatedHandAngle + delta,
            -angleAtGrab, maximumAngle - angleAtGrab);
        float target = Mathf.Clamp(angleAtGrab + accumulatedHandAngle, 0f, maximumAngle);
        currentAngle = Mathf.MoveTowards(currentAngle, target, followSpeed * Time.deltaTime);
    }

    private bool TryHandDirection(Vector3 worldPoint, out Vector3 direction)
    {
        // Measure radius in world metres, independent of imported model scale.
        Vector3 worldAxis = hinge.TransformDirection(axis).normalized;
        Vector3 radial = Vector3.ProjectOnPlane(worldPoint - hinge.position, worldAxis);
        if (radial.sqrMagnitude < minimumHingeRadius * minimumHingeRadius)
        {
            direction = Vector3.zero;
            return false;
        }

        // Remove the parent's movement and scale, then use a fixed CLOSED frame.
        // Using the rotating lid's current frame here would feed rotation back into itself.
        Vector3 parentPoint = referenceParent != null
            ? referenceParent.InverseTransformPoint(worldPoint) : worldPoint;
        Vector3 closedFramePoint = Quaternion.Inverse(closedLocalRotation) *
            (parentPoint - closedLocalPosition);
        direction = Vector3.ProjectOnPlane(closedFramePoint, axis);
        if (direction.sqrMagnitude < 0.00000001f) return false;
        direction.Normalize();
        return true;
    }

    private static Vector3 ClosestPointOnBox(BoxCollider box, Vector3 worldPoint)
    {
        // Geometry query avoids stale Physics poses after changing the lid Transform.
        Vector3 local = box.transform.InverseTransformPoint(worldPoint) - box.center;
        Vector3 half = box.size * 0.5f;
        local.x = Mathf.Clamp(local.x, -half.x, half.x);
        local.y = Mathf.Clamp(local.y, -half.y, half.y);
        local.z = Mathf.Clamp(local.z, -half.z, half.z);
        return box.transform.TransformPoint(box.center + local);
    }

    private void ApplyAngle()
    {
        if (!lockedOpen)
        {
            currentAngle = Mathf.Clamp(currentAngle, 0f, maximumAngle);
            if (currentAngle >= maximumAngle - LockAngleTolerance)
            {
                lockedAngle = maximumAngle;
                lockedOpen = true;
                CancelGrab();
                controllerLeftGrip = controllerRightGrip = false;
                leftGripValue = rightGripValue = 0f;
            }
        }
        if (lockedOpen)
        {
            // Freeze the reached angle even if Maximum Angle / Current Angle is edited.
            currentAngle = lockedAngle;
            grabStatus = "Locked open; white screen / 已完全打开并锁定，白屏开启";
        }
        hinge.localPosition = closedLocalPosition;
        hinge.localRotation = closedLocalRotation * Quaternion.AngleAxis(currentAngle, axis);

        // Never render the black and white surfaces together (avoids overlapping meshes).
        if (screenOffContent != null && screenOffContent.activeSelf == lockedOpen)
            screenOffContent.SetActive(!lockedOpen);
        if (screenOnContent != null && screenOnContent.activeSelf != lockedOpen)
            screenOnContent.SetActive(lockedOpen);
        if (screenGlow != null && screenGlow.enabled != lockedOpen)
            screenGlow.enabled = lockedOpen;
    }

    private void ValidateScreenReferences()
    {
        // Deactivating a parent of the script or hinge would stop the entire interaction.
        if (IsUnsafeScreenReference(screenOffContent))
        {
            Debug.LogWarning("VRLaptopLid: Screen Off Content must be the black screen surface, not Pivot or a parent object.", this);
            screenOffContent = null;
        }
        if (IsUnsafeScreenReference(screenOnContent))
        {
            Debug.LogWarning("VRLaptopLid: Screen On Content must be the white screen surface, not Pivot or a parent object.", this);
            screenOnContent = null;
        }
        if (screenOffContent != null && screenOnContent != null &&
            (screenOffContent.transform.IsChildOf(screenOnContent.transform) ||
             screenOnContent.transform.IsChildOf(screenOffContent.transform)))
        {
            Debug.LogWarning("VRLaptopLid: Screen and Screen Open must be two separate sibling objects, not the same object or nested inside each other.", this);
            screenOffContent = screenOnContent = null;
        }
        if (screenOffContent == null || screenOnContent == null)
            Debug.LogWarning("VRLaptopLid: assign Screen Off Content = Screen, and Screen On Content = Screen Open.", this);
    }

    private bool IsUnsafeScreenReference(GameObject screenObject)
    {
        return screenObject != null &&
            (transform.IsChildOf(screenObject.transform) || hinge.IsChildOf(screenObject.transform));
    }

    private void Release()
    {
        heldBy = 0;
        hasPreviousDirection = false;
        // Keep the visible angle exactly where it was when the hand released.
    }

    private void BreakGrab()
    {
        if (heldBy == 1) blockLeftUntilRelease = true;
        if (heldBy == 2) blockRightUntilRelease = true;
        Release();
        grabStatus = "Release grip then grab again / 松开后重新抓取";
    }

    // Event adapters: set Read Controller Grip AND Editor Keyboard Grab OFF.
    // Bind these no-argument methods to pinch-start / pinch-end UnityEvents.
    public void BeginLeftGrab() { if (!lockedOpen) externalLeftGrip = true; }
    public void EndLeftGrab() { externalLeftGrip = false; }
    public void BeginRightGrab() { if (!lockedOpen) externalRightGrip = true; }
    public void EndRightGrab() { externalRightGrip = false; }
    public void CancelGrab()
    {
        externalLeftGrip = externalRightGrip = false;
        Release();
    }

    public void SetAngle(float degrees)
    {
        if (!Application.isPlaying || !ready || hinge == null) return;
        if (lockedOpen) { ApplyAngle(); return; }
        BreakGrab();
        currentAngle = Mathf.Clamp(degrees, 0f, maximumAngle);
        ApplyAngle();
    }

    [ContextMenu("Test/Open Lid (Play Mode)")]
    public void OpenLid() { SetAngle(maximumAngle); }
    [ContextMenu("Test/Close Lid (Play Mode)")]
    public void CloseLid() { SetAngle(0f); }

    private void OnDisable()
    {
        CancelGrab();
        controllerLeftGrip = controllerRightGrip = false;
#if ENABLE_INPUT_SYSTEM
        leftGripAction?.Disable();
        rightGripAction?.Disable();
#endif
    }

    private void OnDestroy()
    {
#if ENABLE_INPUT_SYSTEM
        leftGripAction?.Dispose();
        rightGripAction?.Dispose();
#endif
    }

    private void OnApplicationFocus(bool focused)
    {
        inputFocused = focused;
        if (!focused) CancelGrab();
    }

    private void OnValidate()
    {
        maximumAngle = Mathf.Clamp(maximumAngle, 1f, 175f);
        initialAngle = Mathf.Clamp(initialAngle, 0f, maximumAngle);
        followSpeed = Mathf.Max(1f, followSpeed);
        grabDistance = Mathf.Max(0f, grabDistance);
        releaseDistance = Mathf.Max(grabDistance + 0.02f, releaseDistance);
        minimumHingeRadius = Mathf.Max(0.001f, minimumHingeRadius);
        gripPressThreshold = Mathf.Clamp(gripPressThreshold, 0.05f, 1f);
        gripReleaseThreshold = Mathf.Clamp(gripReleaseThreshold, 0f, gripPressThreshold - 0.01f);
    }

#if UNITY_EDITOR
    private void OnGUI()
    {
        if (!Application.isPlaying || !showDebugOverlay) return;
        float width = Mathf.Min(360f, Mathf.Max(40f, Screen.width - 20f));
        Rect area = new Rect(Mathf.Max(10f, Screen.width - width - 10f),
            Mathf.Max(10f, Screen.height - 140f), width, 130f);
        GUI.Box(area, GUIContent.none);
        GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 5f, area.width - 16f, area.height - 10f));
        GUILayout.Label(lockedOpen ? "Laptop: LOCKED OPEN" :
            (editorKeyboardGrab ? "Laptop: HOLD G = RIGHT grab" : "Laptop: controller Grip / events"));
        GUILayout.Label("Right grip: " + rightGripValue.ToString("F2") + " | Distance: " +
            (rightDistanceMetres < 0f ? "unavailable" : rightDistanceMetres.ToString("F3") + " m"));
        GUILayout.Label("Grab limit: " + grabDistance.ToString("F3") + " m | Angle: " + currentAngle.ToString("F1"));
        GUILayout.Label(lockedOpen ? "Screen: WHITE | Grabbing disabled" :
            "Screen: BLACK | Locks at " + maximumAngle.ToString("F1") + " deg");
        int divider = grabStatus.IndexOf(" / ", System.StringComparison.Ordinal);
        GUILayout.Label(divider >= 0 ? grabStatus.Substring(0, divider) : grabStatus);
        GUILayout.EndArea();
    }
#endif

    private void OnDrawGizmosSelected()
    {
        Transform pivot = hinge != null ? hinge : transform;
        Vector3 drawAxis = localHingeAxis.sqrMagnitude > 0.000001f
            ? localHingeAxis.normalized : Vector3.right;
        Gizmos.color = Color.cyan;
        Vector3 line = pivot.TransformDirection(drawAxis) * 0.20f;
        Gizmos.DrawLine(pivot.position - line, pivot.position + line);
        Gizmos.DrawWireSphere(pivot.position, 0.012f);
        if (grabZone != null)
        {
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = grabZone.transform.localToWorldMatrix;
            Gizmos.color = IsHeld ? Color.green : Color.yellow;
            Gizmos.DrawWireCube(grabZone.center, grabZone.size);
            Gizmos.matrix = oldMatrix;
            if (rightHand != null)
            {
                Gizmos.DrawWireSphere(rightHand.position, 0.012f);
                Gizmos.DrawLine(rightHand.position, ClosestPointOnBox(grabZone, rightHand.position));
            }
        }
    }
}
