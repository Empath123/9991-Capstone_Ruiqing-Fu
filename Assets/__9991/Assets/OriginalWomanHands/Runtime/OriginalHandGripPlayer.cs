using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OriginalWomanHands
{
    // Samples the authored FBX Close clip. Does not calculate poses, mirror a
    // mesh, change skin weights or rotate the wrist/controller at runtime.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    [DefaultExecutionOrder(110)]
    public sealed class OriginalHandGripPlayer : MonoBehaviour
    {
        public enum HandSide { Left, Right }
        public HandSide hand;
        public AnimationClip closeClip;
        [Range(0f, 1f)] public float maximumGrip = 1f;
        [Min(0.01f)] public float responseSeconds = 0.07f;
        [Header("Test in Play mode")]
        public bool manualPreview;
        [Range(0f, 1f)] public float previewGrip;
#if ENABLE_INPUT_SYSTEM
        [Header("Optional input override (normally leave empty)")]
        [Tooltip("A float Grip action for this hand, already enabled by Input Action Manager.")]
        public InputActionReference gripAction;
        private InputAction ownedGrip;
#endif
        public float InputGrip { get; private set; }
        public float DisplayedGrip { get; private set; }
        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private Animator animator;
        private AnimatorCullingMode oldCulling;
        private bool oldRootMotion;
        private bool oldEnabled;
        private bool animatorSaved;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (closeClip == null || closeClip.length <= 0f)
            {
                Debug.LogError("OriginalHandGripPlayer: assign the Close clip from this hand's FBX.", this);
                enabled = false;
                return;
            }
            animator = GetComponent<Animator>();
            oldCulling = animator.cullingMode;
            oldRootMotion = animator.applyRootMotion;
            oldEnabled = animator.enabled;
            animatorSaved = true;
            animator.enabled = true;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create(name + " Grip");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, closeClip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetSpeed(0d);
            var output = AnimationPlayableOutput.Create(graph, "Finger animation", animator);
            output.SetSourcePlayable(playable);
            graph.Play();
#if ENABLE_INPUT_SYSTEM
            ownedGrip = new InputAction("OriginalHandGrip_" + hand, InputActionType.Value,
                hand == HandSide.Left ? "<XRController>{LeftHand}/grip" : "<XRController>{RightHand}/grip",
                expectedControlType: "Axis");
            ownedGrip.Enable();
#endif
            DisplayedGrip = 0f;
            Sample(0f);
        }

        private void LateUpdate()
        {
            if (!graph.IsValid()) return;
            InputGrip = ReadGrip();
            float target = Mathf.Clamp01(manualPreview ? previewGrip : InputGrip) * maximumGrip;
            float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.01f, responseSeconds));
            DisplayedGrip = Mathf.Lerp(DisplayedGrip, target, blend);
            if (Mathf.Abs(DisplayedGrip - target) < 0.0001f) DisplayedGrip = target;
            Sample(DisplayedGrip);
        }

        private float ReadGrip()
        {
#if ENABLE_INPUT_SYSTEM
            if (gripAction != null && gripAction.action != null)
                return Mathf.Clamp01(gripAction.action.ReadValue<float>());
            // Uses the same simulated/real left and right XR devices as XRI.
            // No hard-coded G key: the simulator chooses the target hand.
            if (ownedGrip != null && ownedGrip.controls.Count > 0)
                return Mathf.Clamp01(ownedGrip.ReadValue<float>());
#endif
            var node = hand == HandSide.Left ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand;
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float value))
                return Mathf.Clamp01(value);
            return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool pressed)
                && pressed ? 1f : 0f;
        }

        private void Sample(float amount)
        {
            // Prevent a looping imported clip from wrapping the last pose to zero.
            double time = Mathf.Clamp01(amount) * closeClip.length;
            playable.SetTime(time);
            playable.SetDone(false);
            graph.Evaluate(0f);
        }

        private void OnDisable()
        {
            if (graph.IsValid())
            {
                Sample(0f);
                graph.Destroy();
            }
#if ENABLE_INPUT_SYSTEM
            ownedGrip?.Disable();
            ownedGrip?.Dispose();
            ownedGrip = null;
#endif
            if (animatorSaved && animator != null)
            {
                animator.cullingMode = oldCulling;
                animator.applyRootMotion = oldRootMotion;
                animator.enabled = oldEnabled;
            }
            animatorSaved = false;
        }
    }
}
