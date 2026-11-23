using System.Collections.Generic;
using UnityEngine;

// Unity 6 / URP. Attach to an always-active empty object in the Office scene.
[DisallowMultipleComponent]
public sealed class LetterAtmosphere : MonoBehaviour
{
    public AudioSource letterSource;
    public AudioClip letterClip;
    [Tooltip("Drag individual lights or their parent objects here.")]
    public GameObject[] lightRoots;
    public Material cityMaterial;
    public Color finalRed = new Color(0.95f, 0.04f, 0.04f, 1f);

    readonly Dictionary<Light, float> lights = new Dictionary<Light, float>();
    readonly List<Renderer> changed = new List<Renderer>();
    Material copy;
    string colorKey;
    Color original, emission;
    bool tintEmission, started, finished;

    void Update()
    {
        if (finished || !letterSource || !letterClip ||
            letterSource.clip != letterClip || !letterSource.isPlaying) return;

        if (!started)
        {
            started = true;
            if (lightRoots != null)
                foreach (var root in lightRoots)
                    if (root)
                        foreach (var lamp in root.GetComponentsInChildren<Light>(true))
                            lights[lamp] = lamp.intensity;
            if (lights.Count == 0)
                Debug.LogWarning("LetterAtmosphere: no Light components found under Light Roots.", this);
            PrepareCity();
        }

        // Reach the endpoint just before playback stops: isPlaying is false after the last sample.
        float progress = (float)letterSource.timeSamples / Mathf.Max(1, letterClip.samples);
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.98f));
        finished = progress >= 0.98f;
        foreach (var entry in lights)
        {
            if (!entry.Key) continue;
            entry.Key.intensity = entry.Value * (1f - t);
            if (finished) entry.Key.enabled = false;
        }

        if (!copy) return;
        Color target = finalRed;
        target.a = original.a;
        copy.SetColor(colorKey, Color.Lerp(original, target, t));
        if (tintEmission)
        {
            Color glow = finalRed * emission.maxColorComponent;
            glow.a = emission.a;
            copy.SetColor("_EmissionColor", Color.Lerp(emission, glow, t));
        }
    }

    void PrepareCity()
    {
        if (!cityMaterial) { Debug.LogWarning("Assign City Material.", this); return; }
        colorKey = cityMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
        if (!cityMaterial.HasProperty(colorKey)) { Debug.LogWarning("No base color on city material.", this); return; }
        copy = new Material(cityMaterial);
        original = copy.GetColor(colorKey);
        tintEmission = copy.IsKeywordEnabled("_EMISSION") && copy.HasProperty("_EmissionColor");
        if (tintEmission) emission = copy.GetColor("_EmissionColor");

        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var slots = r.sharedMaterials;
                bool matched = false;
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] == cityMaterial) { slots[i] = copy; matched = true; }
                if (matched) { r.sharedMaterials = slots; changed.Add(r); }
            }
        if (changed.Count == 0)
            Debug.LogWarning("LetterAtmosphere: no renderer in this scene uses City Material.", this);
    }

    void OnDestroy()
    {
        if (!copy) return;
        foreach (var r in changed)
        {
            if (!r) continue;
            var slots = r.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == copy) slots[i] = cityMaterial;
            r.sharedMaterials = slots;
        }
        Destroy(copy);
    }
}
