using System;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CustomVRHands
{
    // Calibrated for SK_Woman.fbx and SK_Woman_left.fbx.
    // Attach to the visual hand MODEL, never to XR Origin or a Controller root.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("XR/Woman Hand Grip")]
    public sealed class WomanHandGrip : MonoBehaviour
    {
        public enum HandSide { Left, Right }
        public HandSide hand = HandSide.Right;

        [Header("Pose")]
        [Range(0f, 1f)] public float maximumCurl = 1f;
        [Min(1f)] public float responseSpeed = 16f;
        [Tooltip("Use only if the model has been mirrored differently from the supplied FBX.")]
        public bool reversePalmDirection;

        [Header("Testing (Play mode)")]
        [Tooltip("Temporarily ignore Grip input and use Preview Curl below.")]
        public bool manualPreview;
        [Range(0f, 1f)] public float previewCurl;
        [Tooltip("Optional EDITOR fallback. Right: G. Left: Shift+G. Normally leave off and use your XR simulator.")]
        public bool editorKeyboardFallback;

#if ENABLE_INPUT_SYSTEM
        [Header("Optional Input Override")]
        [Tooltip("Optional float action. Leave empty for automatic LeftHand/RightHand grip binding. If assigned, enable it through your Input Action Manager.")]
        public InputActionReference gripAction;
        private InputAction ownedGripAction;
#endif
        [Serializable]
        public sealed class FingerPose
        {
            public Transform bone;
            public Quaternion open;
            public Quaternion closed;
        }

        [SerializeField, HideInInspector] private FingerPose[] fingers;
        [SerializeField, HideInInspector] private bool configured;
        [SerializeField, HideInInspector] private HandSide configuredHand;
        [SerializeField, HideInInspector] private bool configuredReverse;
        private Animator[] animators;
        private bool[] animatorWasEnabled;
        private float curl;
        public float CurrentGrip { get; private set; }
        public float CurrentCurl => curl;
        public bool IsConfigured => configured && fingers != null && fingers.Length == 15;
        public FingerPose[] Poses => fingers;
        private static readonly string[] FingerNames = { "index", "middle", "ring", "pinky", "thumb" };
        private static readonly float[,] CurlAngles = {
            { 58f, 72f, 42f }, { 63f, 78f, 44f },
            { 66f, 80f, 47f }, { 70f, 82f, 50f }
        };

        private void Reset()
        {
            hand = FindBone("index_01_l") != null ? HandSide.Left : HandSide.Right;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if ((!IsConfigured || configuredHand != hand || configuredReverse != reversePalmDirection)
                && !ConfigureFromOpenPose())
            {
                enabled = false;
                return;
            }
            // The existing static Take 001 must not overwrite the animated finger rotations.
            animators = GetComponentsInChildren<Animator>(true);
            animatorWasEnabled = new bool[animators.Length];
            for (int i = 0; i < animators.Length; ++i)
            {
                animatorWasEnabled[i] = animators[i].enabled;
                animators[i].enabled = false;
            }
#if ENABLE_INPUT_SYSTEM
            string usage = hand == HandSide.Left ? "LeftHand" : "RightHand";
            ownedGripAction = new InputAction("WomanHandGrip_" + usage, InputActionType.Value,
                "<XRController>{" + usage + "}/grip", expectedControlType: "Axis");
            ownedGripAction.Enable();
#endif
            curl = 0f;
            ApplyPose(0f);
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !IsConfigured) return;
            CurrentGrip = ReadGrip();
            float target = manualPreview ? previewCurl : CurrentGrip * maximumCurl;
            curl = Mathf.Lerp(curl, Mathf.Clamp01(target), 1f - Mathf.Exp(-responseSpeed * Time.unscaledDeltaTime));
            if (Mathf.Abs(curl - target) < 0.0001f) curl = target;
            ApplyPose(curl);
        }

        private float ReadGrip()
        {
            float value = 0f;
            bool readXRDevice = true;
#if ENABLE_INPUT_SYSTEM
            bool hasOverride = gripAction != null && gripAction.action != null;
            if (hasOverride)
                value = gripAction.action.enabled ? gripAction.action.ReadValue<float>() : 0f;
            else if (ownedGripAction != null)
                value = ownedGripAction.ReadValue<float>();
            // An explicitly assigned Input Action remains authoritative.
            readXRDevice = !hasOverride;
#endif
            if (readXRDevice)
            {
                var device = InputDevices.GetDeviceAtXRNode(hand == HandSide.Left ? XRNode.LeftHand : XRNode.RightHand);
                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float grip))
                    value = Mathf.Max(value, grip);
                else if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool held) && held)
                    value = 1f;
            }
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            if (editorKeyboardFallback && Keyboard.current != null)
            {
                bool shift = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
                bool thisHand = hand == HandSide.Left ? shift : !shift;
                if (thisHand && Keyboard.current.gKey.isPressed) value = 1f;
            }
#elif UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
            if (editorKeyboardFallback)
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool thisHand = hand == HandSide.Left ? shift : !shift;
                if (thisHand && Input.GetKey(KeyCode.G)) value = 1f;
            }
#endif
            return Mathf.Clamp01(value);
        }

        public void ApplyPose(float amount)
        {
            if (!IsConfigured) return;
            for (int i = 0; i < fingers.Length; ++i)
            {
                var p = fingers[i];
                if (p.bone != null) p.bone.localRotation = Quaternion.Slerp(p.open, p.closed, Mathf.Clamp01(amount));
            }
        }

        public void RestoreOpenPose()
        {
            if (fingers == null) return;
            foreach (var p in fingers)
                if (p != null && p.bone != null) p.bone.localRotation = p.open;
        }

        public bool ConfigureFromOpenPose()
        {
            // Restore our own saved open pose before rebuilding; never capture a clenched pose as neutral.
            if (IsConfigured) RestoreOpenPose();
            string suffix = hand == HandSide.Left ? "_l" : "_r";
            Transform[,] joints = new Transform[5, 3];
            for (int f = 0; f < 5; ++f)
                for (int j = 0; j < 3; ++j)
                {
                    string name = FingerNames[f] + "_0" + (j + 1) + suffix;
                    joints[f, j] = FindBone(name);
                    if (joints[f, j] == null)
                    {
                        Debug.LogError("Woman Hand Grip: missing " + name + ". Attach this component to the matching hand model, with its bone hierarchy visible (Optimize Game Objects off).", this);
                        configured = false;
                        return false;
                    }
                }
            Vector3 across = (joints[0, 0].position - joints[3, 0].position).normalized;
            Vector3 forward = (joints[1, 1].position - joints[1, 0].position).normalized;
            // Unity's coordinate handedness differs from Blender's.
            Vector3 palm = Vector3.Cross(forward, across).normalized;
            if (hand == HandSide.Left) palm = -palm;
            if (reversePalmDirection) palm = -palm;
            if (palm.sqrMagnitude < 0.5f)
            {
                Debug.LogError("Woman Hand Grip: degenerate palm geometry.", this);
                configured = false;
                return false;
            }
            // Relative measure preserves the pose under uniform scaling.
            float palmWidth = Vector3.Distance(joints[0, 0].position, joints[3, 0].position);
            var result = new FingerPose[15];
            for (int f = 0; f < 5; ++f)
                for (int j = 0; j < 3; ++j)
                {
                    Transform bone = joints[f, j];
                    Vector3 direction = j < 2
                        ? joints[f, j + 1].position - bone.position
                        : bone.position - joints[f, j - 1].position;
                    direction.Normalize();
                    Vector3 axis = Vector3.Cross(direction, palm).normalized;
                    float degrees;
                    if (f < 4) degrees = CurlAngles[f, j];
                    else if (j == 0)
                    {
                        Vector3 towardPalm = (joints[2, 0].position - bone.position + palm * palmWidth * 0.49f).normalized;
                        axis = Vector3.Cross(direction, towardPalm).normalized;
                        degrees = Vector3.Angle(direction, towardPalm) * 0.62f;
                    }
                    else degrees = j == 1 ? 30f : 35f;
                    // Rotation axes are pseudovectors: account for mirrored FBX transforms.
                    Vector3 localAxis = bone.worldToLocalMatrix.MultiplyVector(axis).normalized;
                    if (bone.localToWorldMatrix.determinant < 0f) localAxis = -localAxis;
                    result[f * 3 + j] = new FingerPose {
                        bone = bone,
                        open = bone.localRotation,
                        closed = bone.localRotation * Quaternion.AngleAxis(degrees, localAxis)
                    };
                }
            fingers = result;
            configured = true;
            configuredHand = hand;
            configuredReverse = reversePalmDirection;
            return true;
        }

        private Transform FindBone(string target)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string name = t.name;
                int colon = name.LastIndexOf(':');
                if (colon >= 0) name = name.Substring(colon + 1);
                if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase)) return t;
            }
            return null;
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            if (ownedGripAction != null)
            {
                ownedGripAction.Disable();
                ownedGripAction.Dispose();
                ownedGripAction = null;
            }
#endif
            RestoreOpenPose();
            if (animators != null && animatorWasEnabled != null)
                for (int i = 0; i < animators.Length; ++i)
                    if (animators[i] != null) animators[i].enabled = animatorWasEnabled[i];
            animators = null;
            animatorWasEnabled = null;
            curl = 0f;
            CurrentGrip = 0f;
        }
    }
}
