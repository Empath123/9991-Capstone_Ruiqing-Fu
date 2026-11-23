using UnityEngine;
using UnityEngine.XR;
using Unity.XR.CoreUtils;

// Put on the XR Origin root. This component never plays or stops any audio.
[RequireComponent(typeof(XROrigin)), DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class LetterSeatToStand : MonoBehaviour
{
    public AudioSource letterSource;
    public AudioClip newLetter;
    [Tooltip("The dedicated Locomotion child (never Camera Offset, a controller, or XR Origin).")]
    public GameObject locomotionRoot;
    [Tooltip("Eye height above the XR Origin while seated, in metres. Adjustable live before standing.")]
    [Min(0.4f)] public float seatedEyeHeight = 1.12f;
    [Min(0.5f)] public float standingEyeHeight = 1.70f;
    [Min(0.4f)] public float riseSeconds = 1.8f;
    [Range(0f, 0.06f)] public float leanForward = 0.025f;

    Transform head, offset, pivot;
    Vector3 seated;
    float elapsed, lastPitch;
    int lastSample;
    double lastDSP;
    bool ready, calibrated, observed, rising, done, trackedBefore;
    float oldOffsetY, appliedSeatHeight;

    void Awake()
    {
        var origin = GetComponent<XROrigin>();
        head = origin.Camera ? origin.Camera.transform : null;
        offset = origin.CameraFloorOffsetObject ? origin.CameraFloorOffsetObject.transform : null;
        if (!locomotionRoot && transform.Find("Locomotion"))
            locomotionRoot = transform.Find("Locomotion").gameObject;
        if (!head || !offset || offset.parent != transform || !head.IsChildOf(offset) ||
            !locomotionRoot || locomotionRoot.transform == transform ||
            !locomotionRoot.transform.IsChildOf(transform) || head.IsChildOf(locomotionRoot.transform) ||
            !letterSource || !newLetter)
        {
            Debug.LogError("LetterSeatToStand: check Letter Source, New Letter, the dedicated Locomotion child and XR Origin/Camera Offset hierarchy.", this);
            enabled = false;
            return;
        }
        // Keep the original tracked objects and their local poses intact.
        pivot = new GameObject("Seat Motion (runtime)").transform;
        pivot.SetParent(transform, false);
        offset.SetParent(pivot, false);
        locomotionRoot.SetActive(false);
        ready = true;
    }

    void LateUpdate()
    {
        if (!ready || done) return;
        if (!rising)
        {
            var dev = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool tracked = dev.TryGetFeatureValue(CommonUsages.isTracked, out bool value) && value;
            // Align once, and again if tracking starts late or the XR origin changes mode.
            // Do not continuously cancel the player's own head movements.
            if (!calibrated || (tracked && !trackedBefore) || Mathf.Abs(offset.localPosition.y - oldOffsetY) > 0.05f)
            {
                float measured = transform.InverseTransformPoint(head.position).y;
                seated = pivot.localPosition + Vector3.up * (seatedEyeHeight - measured);
                pivot.localPosition = seated;
                calibrated = true;
            }
            else if (!Mathf.Approximately(seatedEyeHeight, appliedSeatHeight))
            {
                // Apply only the edited difference, keeping physical head lean intact.
                seated.y += seatedEyeHeight - appliedSeatHeight;
                pivot.localPosition = seated;
            }
            appliedSeatHeight = seatedEyeHeight;
            trackedBefore = tracked;
            oldOffsetY = offset.localPosition.y;
            WatchLetter();
            return;
        }

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / Mathf.Max(0.4f, riseSeconds));
        // Shift weight forward first, then rise, then ease back upright. No forced head rotation.
        float load = Smooth(t / 0.22f);
        float straighten = Smooth((t - 0.24f) / 0.76f);
        float rise = Smooth((t - 0.14f) / 0.86f);
        float lean = load * (1f - straighten);
        float up = Mathf.Max(0f, standingEyeHeight - appliedSeatHeight) * rise - 0.012f * lean;
        pivot.localPosition = seated + Vector3.forward * (leanForward * lean) + Vector3.up * up;
        if (t >= 1f)
        {
            done = true;
            locomotionRoot.SetActive(true);
        }
    }

    static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

    void WatchLetter()
    {
        if (!letterSource || AudioListener.pause) return;
        if (letterSource.clip == newLetter && letterSource.isPlaying)
        {
            observed = !letterSource.loop;
            lastSample = letterSource.timeSamples;
            lastPitch = letterSource.pitch;
            lastDSP = AudioSettings.dspTime;
            return;
        }
        if (!observed) return;
        observed = false;
        // Pause normally keeps a nonzero playhead. Stop/end resets it; only accept a
        // transition from the end region, allowing one polling frame and an audio buffer.
        if (letterSource.clip == newLetter && letterSource.timeSamples > 0 &&
            letterSource.timeSamples < newLetter.samples - 1) return;
        double remaining = (newLetter.samples - lastSample) / (double)newLetter.frequency;
        double window = (AudioSettings.dspTime - lastDSP + 0.09) * Mathf.Max(0f, lastPitch);
        if (remaining <= window) LetterFinished();
    }

    // Optional exact completion signal for a Timeline, PlayOneShot or custom audio sequencer.
    public void LetterFinished()
    {
        if (ready && calibrated && !rising && !done) rising = true;
    }
}
