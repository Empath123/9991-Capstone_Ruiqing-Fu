using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Unity 6 / URP. Put this on an always-active object, e.g. XR Origin > Mohu.
[DisallowMultipleComponent]
public sealed class LetterShockBlur : MonoBehaviour
{
    [Header("Email narration")]
    [Tooltip("The AudioSource on Email Content, not the heartbeat or music source.")]
    [SerializeField] private AudioSource letterSource;
    [Tooltip("The actual New Letter AudioClip from the Project window.")]
    [SerializeField] private AudioClip newLetter;

    [Tooltip("Seconds after narration starts before the first blur pulse.")]
    [Min(0f)][SerializeField] private float startDelaySeconds = 30f;

    [Header("Repeating defocus")]
    [Range(0f, 1f)][SerializeField] private float blurStrength = 0.85f;
    [Tooltip("Seconds to gradually lose focus at the start of each pulse.")]
    [Min(0f)][SerializeField] private float fadeInSeconds = 0.65f;
    [Min(0f)][SerializeField] private float peakHoldSeconds = 0.25f;
    [Min(0.05f)][SerializeField] private float refocusSeconds = 0.65f;
    [Tooltip("Seconds fully clear between pulses. The cycle has no time limit.")]
    [Min(0.1f)][SerializeField] private float clearSeconds = 1.4f;

    private Volume volume;
    private VolumeProfile profile;
    private DepthOfField focus;
    private bool countdownStarted, running;
    private double blurDue, start;
    private float lastStrength = -1f;

    private void Update()
    {
        if (!running)
        {
            if (!countdownStarted && letterSource && newLetter &&
                letterSource.isActiveAndEnabled && letterSource.clip == newLetter &&
                letterSource.isPlaying && letterSource.timeSamples > 0)
                OnLetterStarted();

            if (!countdownStarted || Time.unscaledTimeAsDouble < blurDue) return;
            BeginDefocus();
        }

        float k = Mathf.Clamp01(blurStrength) * Envelope(Time.unscaledTimeAsDouble - start);
        if (k == lastStrength) return;
        ApplyBlur(k);
        lastStrength = k;
    }

    private float Envelope(double elapsed)
    {
        float rise = Mathf.Max(0f, fadeInSeconds);
        float hold = Mathf.Max(0f, peakHoldSeconds);
        float fall = Mathf.Max(0.05f, refocusSeconds);
        float gap = Mathf.Max(0.1f, clearSeconds);
        float t = (float)(elapsed % ((double)rise + hold + fall + gap));
        if (t < rise) return Mathf.SmoothStep(0f, 1f, t / rise);
        t -= rise;
        if (t < hold) return 1f;
        t -= hold;
        if (t < fall) return Mathf.SmoothStep(1f, 0f, t / fall);
        return 0f; // ApplyBlur releases our Volume for the whole clear interval.
    }

    // Optional UnityEvent for players that use PlayOneShot. Invoke when reading starts.
    public void OnLetterStarted()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || countdownStarted || running) return;
        countdownStarted = true;
        blurDue = Time.unscaledTimeAsDouble + Mathf.Max(0f, startDelaySeconds);
    }

    // Retain existing UnityEvent bindings, but finishing the clip no longer triggers blur.
    public void OnLetterFinished() { }

    [ContextMenu("Preview Shock Blur (Play Mode)")]
    private void Preview()
    {
        if (Application.isPlaying && isActiveAndEnabled) BeginDefocus();
    }

    private void BeginDefocus()
    {
        if (running) return;
        if (!volume)
        {
            // Do not modify a shared scene Volume, a profile asset, or the XR camera.
            var child = new GameObject("Letter shock blur (runtime)");
            child.SetActive(false);
            child.layer = gameObject.layer;
            child.transform.SetParent(transform, false);
            volume = child.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000f;
            volume.weight = 0f;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;
            focus = profile.Add<DepthOfField>();
            focus.mode.Override(DepthOfFieldMode.Gaussian);
            focus.gaussianStart.Override(0.12f);
            focus.gaussianEnd.Override(0.7f);
            focus.gaussianMaxRadius.Override(1f);
            child.SetActive(true);
        }
        start = Time.unscaledTimeAsDouble;
        running = true;
    }

    private void ApplyBlur(float k)
    {
        if (k <= 0.001f) { volume.weight = 0f; return; }
        // Pull the sharp plane closer as focus is lost. Very near hands stay clearer.
        float inv = 1f - k;
        float near = 0.12f + 0.38f * inv * inv * inv / k;
        focus.gaussianStart.value = near;
        focus.gaussianEnd.value = near + 0.36f / k;
        focus.gaussianMaxRadius.value = Mathf.Lerp(0.5f, 1.35f, k);
        volume.weight = 1f;
    }

    private void OnDisable()
    {
        // Suspend only this override; if re-enabled, rejoin the repeating cycle.
        lastStrength = -1f;
        if (volume) volume.weight = 0f;
    }
    private void OnDestroy()
    {
        if (volume) Destroy(volume.gameObject);
        if (focus) Destroy(focus);
        if (profile) Destroy(profile);
    }
}
