using System;
using UnityEngine;

// Door 1 / Unity 6. No custom shader, external texture or scene helper required.
// The single embedded atlas preserves the two approved glitch shapes.
[DisallowMultipleComponent]
public sealed class DoorFlash : MonoBehaviour
{
    [Header("References (same fields as the previous version)")]
    [Tooltip("The Box Collider on the moving door leaf, not the frame or handle.")]
    public BoxCollider doorPanel;
    public AudioSource letterSource;
    public AudioClip newLetter;

    [Header("Two independent signals")]
    [Tooltip("Rest between bursts in seconds: X = upper band, Y = lower band.")]
    public Vector2 quietSeconds = new Vector2(2.2f, 4.2f);
    [Tooltip("Approximate internal flickers per second. Each fragment also has its own rate and phase.")]
    [Range(0.4f, 2.4f)] public float upperFragmentHz = 1.85f;
    [Range(0.4f, 2.4f)] public float lowerFragmentHz = 1.15f;
    [Tooltip("Tempo for both independent signals. 1 = original; 1.14 = 14% faster. Existing band frequency settings are preserved.")]
    [Range(0.75f, 1.35f)] public float tempoMultiplier = 1.14f;
    [Tooltip("Scales the height around each approved band centre. The lower band also expands slightly sideways, but never beyond the door.")]
    [Range(0.85f, 1.65f)] public float bandSpread = 1.40f;
    [Tooltip("Additional shift from the approved lower position, as a fraction of the door width. Clipped at the door edge.")]
    [Range(0f, 0.15f)] public float lowerRightShift = 0.035f;
    [Range(0f, 1f)] public float brightness = 0.62f;
    [Tooltip("Dim stationary pattern; no pulsing or movement.")]
    public bool reducedFlash;
    [Tooltip("Offset from the collider surface in metres.")]
    [Range(0.001f, 0.05f)] public float surfaceGap = 0.007f;

    const int SideCount = 2, Pieces = 16, Tiles = 2 * Pieces;
    const float CanvasSize = 512f;
    readonly Canvas[] sides = new Canvas[SideCount];
    readonly CanvasRenderer[] draws = new CanvasRenderer[SideCount];
    readonly Vector3[] vertices = new Vector3[4 * Tiles];
    readonly Color32[] colors = new Color32[4 * Tiles];
    readonly Vector2[] uv = new Vector2[4 * Tiles];
    Texture2D atlas;
    Mesh mesh;
    bool consumed, running, heard, loadFailed;
    float elapsed, lastPitch;
    int lastSample;
    double lastDsp;

    void Start()
    {
        // Prewarm away from the audio-end frame. The face overlays start hidden.
        if (doorPanel) Prepare();
    }

    void Update()
    {
        if (!consumed) WatchLetter();
        if (!running) return;
        RenderSignals(elapsed, reducedFlash);
        elapsed += Time.deltaTime;
    }

    void WatchLetter()
    {
        if (!letterSource || !newLetter || !letterSource.isActiveAndEnabled)
        {
            heard = false;
            return;
        }
        if (AudioListener.pause && !letterSource.ignoreListenerPause) return;
        bool same = letterSource.clip == newLetter;
        double now = AudioSettings.dspTime;
        if (same && letterSource.isPlaying)
        {
            heard = !letterSource.loop && letterSource.pitch > 0f;
            lastSample = letterSource.timeSamples;
            lastPitch = letterSource.pitch;
            lastDsp = now;
            return;
        }
        if (!heard) return;
        heard = false;
        // Pause keeps the sample cursor; early Stop must not count as a completed letter.
        if (same && letterSource.timeSamples > 0 && letterSource.timeSamples < newLetter.samples - 1) return;
        AudioSettings.GetDSPBufferSize(out int buffer, out int count);
        double allowance = (double)buffer * count / Mathf.Max(1, AudioSettings.outputSampleRate);
        double left = (double)(newLetter.samples - lastSample) / newLetter.frequency;
        if (left <= (now - lastDsp + allowance) * lastPitch) StartGlitch();
    }

    // If the audio is driven by Timeline/PlayOneShot, invoke this on its actual end event.
    [ContextMenu("Preview Glitch (Play Mode)")]
    public void StartGlitch()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || running) return;
        if (!doorPanel)
        {
            Debug.LogWarning("DoorFlash: assign the moving door's Box Collider to Door Panel.", this);
            return;
        }
        if (!Prepare()) return;
        heard = false;
        consumed = running = true;
        elapsed = 0f;
    }

    [ContextMenu("Stop Glitch (Play Mode)")]
    public void StopGlitch()
    {
        if (!Application.isPlaying) return;
        running = heard = false;
        consumed = true;
        Hide();
    }

    // The timing and layout below are also used to render the accompanying concept video.
    struct Patch { public float x0, y0, x1, y1, u0, v0, u1, v1, alpha; }
    // SIGNAL MODEL BEGIN
    static float Noise(int key)
    {
        uint h = (uint)key;
        unchecked { h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16; }
        return (h & 0xffffu) / 65535f;
    }

    static float Ease01(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    static float Duration(int band) { return band == 0 ? 1.30f : 1.68f; }

    static float BandTime(float seconds, int band, float rest, float tempo, out int cycle)
    {
        float t = seconds * Mathf.Clamp(tempo, 0.75f, 1.35f) - (band == 0 ? 0f : 0.38f);
        float period = Duration(band) + Mathf.Max(0.9f, rest);
        cycle = Mathf.Max(0, Mathf.FloorToInt(t / period));
        float jitter = cycle == 0 ? 0f : Noise(718 + band * 891 + cycle * 17) * 0.26f;
        return t - cycle * period - jitter;
    }

    // The seam between neighbouring pieces is in a different place on every row.
    static float XEdge(int band, int row, int edge)
    {
        if (edge == 0) return 0f;
        if (edge == 4) return 1f;
        return edge * 0.25f + (Noise(201 + band * 71 + row * 11 + edge) - 0.5f) * 0.18f;
    }

    static float YEdge(int row)
    {
        return row == 0 ? 0f : row == 1 ? 0.19f : row == 2 ? 0.48f : row == 3 ? 0.74f : 1f;
    }

    static Patch Sample(int band, int part, float t, int cycle, float hz,
                        float shift, float spread, float gain, bool still)
    {
        Patch p = new Patch();
        int row = part / 4, col = part % 4, key = 500 + band * 193 + part * 9;
        float u0 = XEdge(band, row, col), u1 = XEdge(band, row, col + 1);
        float v0 = YEdge(row), v1 = YEdge(row + 1);
        float dx = 0f;
        float alpha = 0.36f;
        if (!still)
        {
            float lead = (band == 0 ? col : 3 - col) * 0.031f + Noise(key + 1) * 0.10f;
            float tail = 0.035f + Noise(key + 3) * 0.18f;
            float env = Ease01((t - lead) / 0.115f) * Ease01((Duration(band) - tail - t) / 0.14f);
            if (env <= 0f) return p;
            // Independent oscillator per piece, per band. No global on/off sine.
            float rate = Mathf.Clamp(hz, 0.4f, 2.4f) * (0.80f + 0.27f * Noise(key + 4));
            float phase = (t - lead) * rate + Noise(key + 5) + 0.19f * Noise(key + 216 + cycle * 9);
            phase -= Mathf.Floor(phase);
            float away = Mathf.Abs(phase - 0.50f);
            float lit = Ease01((away - 0.052f) / 0.115f);
            alpha = env * lit * (0.90f + 0.10f * Noise(key + 6));
            dx = (2f * Noise(key + 712 + cycle * 3) - 1f) * 0.018f *
                 (1f - Ease01((away - 0.095f) / 0.18f));
        }
        // Coordinates from top-left of the slab as viewed from either face.
        // Expand around the two original vertical centres, leaving the handle clear.
        // Fit the enlarged art to the available slab so detached edge streaks remain visible.
        spread = Mathf.Clamp(spread, 0.85f, 1.65f);
        float baseLeft = band == 0 ? 0f : 0.065f + Mathf.Clamp(shift, 0f, 0.15f);
        float padX = 0.08f * (spread - 1f);
        float left = Mathf.Max(0f, baseLeft - padX);
        float right = Mathf.Min(1f, 1f + padX);
        float width = right - left;
        left += dx;
        float height = (band == 0 ? 0.174f : 0.180f) * spread;
        float top = (band == 0 ? 0.293f : 0.686f) - height * 0.5f;
        p.x0 = Mathf.Max(0f, left + width * u0);
        p.x1 = Mathf.Min(1f, left + width * u1);
        if (p.x1 <= p.x0) return p;
        p.y0 = top + height * v0;
        p.y1 = top + height * v1;
        // Crop the UVs as well as the geometry. No smearing or wrap around the jamb.
        p.u0 = Mathf.Clamp01((p.x0 - left) / width);
        p.u1 = Mathf.Clamp01((p.x1 - left) / width);
        p.v0 = v0; p.v1 = v1;
        p.alpha = Mathf.Clamp01(alpha * gain);
        return p;
    }
    // SIGNAL MODEL END

    bool Prepare()
    {
        if (mesh) return true;
        if (loadFailed) return false;
        atlas = new Texture2D(2, 2, TextureFormat.RGBA32, true, false)
        {
            name = "Door flash atlas (runtime)",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 1
        };
        if (!ImageConversion.LoadImage(atlas, Convert.FromBase64String(AtlasPng), true))
        {
            loadFailed = true;
            Debug.LogError("DoorFlash: embedded texture could not be decoded. Copy the complete .cs file.", this);
            return false;
        }
        mesh = new Mesh { name = "Door flash fragments (runtime)" };
        mesh.MarkDynamic();
        int[] tris = new int[6 * Tiles];
        for (int i = 0; i < Tiles; i++)
        {
            int v = 4 * i, j = 6 * i;
            tris[j] = v; tris[j + 1] = v + 1; tris[j + 2] = v + 2;
            tris[j + 3] = v + 2; tris[j + 4] = v + 3; tris[j + 5] = v;
        }
        mesh.vertices = vertices; mesh.uv = uv; mesh.colors32 = colors; mesh.triangles = tris;
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(CanvasSize, CanvasSize, 1f));

        Transform tr = doorPanel.transform;
        Vector3 sz = doorPanel.size;
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
        int n = 0;
        for (int i = 1; i < 3; i++)
            if (tr.TransformVector(axes[i] * sz[i]).sqrMagnitude <
                tr.TransformVector(axes[n] * sz[n]).sqrMagnitude) n = i;
        int a = (n + 1) % 3, b = (n + 2) % 3;
        int up = Mathf.Abs(tr.TransformVector(axes[a]).normalized.y) >=
                 Mathf.Abs(tr.TransformVector(axes[b]).normalized.y) ? a : b;
        int wide = 3 - n - up;
        Vector3 upAxis = axes[up] * (tr.TransformVector(axes[up]).y < 0f ? -1f : 1f);
        float gap = surfaceGap / Mathf.Max(0.000001f, tr.TransformVector(axes[n]).magnitude);
        for (int i = 0; i < SideCount; i++)
        {
            float sign = i == 0 ? 1f : -1f;
            var go = new GameObject("Door flash face (runtime)", typeof(RectTransform), typeof(Canvas));
            go.layer = tr.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(tr, false);
            sides[i] = go.GetComponent<Canvas>();
            sides[i].renderMode = RenderMode.WorldSpace;
            rt.localPosition = doorPanel.center + sign * axes[n] * (0.5f * sz[n] + gap);
            Quaternion basis = Quaternion.LookRotation(-sign * axes[n], upAxis);
            rt.localRotation = basis;
            Vector3 forward = tr.TransformVector(-sign * axes[n]).normalized;
            Vector3 expectedRight = Vector3.Cross(tr.TransformVector(upAxis).normalized, forward);
            float hand = Mathf.Sign(Vector3.Dot(tr.TransformVector(basis * Vector3.right), expectedRight));
            rt.localScale = new Vector3(hand * sz[wide] / CanvasSize, sz[up] / CanvasSize, 1f);
            rt.sizeDelta = Vector2.one * CanvasSize;
            var child = new GameObject("Bands", typeof(RectTransform), typeof(CanvasRenderer));
            child.layer = go.layer;
            var childRect = (RectTransform)child.transform;
            childRect.SetParent(rt, false);
            childRect.anchorMin = Vector2.zero; childRect.anchorMax = Vector2.one;
            childRect.offsetMin = childRect.offsetMax = Vector2.zero;
            draws[i] = child.GetComponent<CanvasRenderer>();
            draws[i].materialCount = 1;
            draws[i].SetMaterial(Canvas.GetDefaultCanvasMaterial(), 0);
            draws[i].SetTexture(atlas);
            draws[i].cull = true;
        }
        return true;
    }

    void RenderSignals(float seconds, bool still)
    {
        bool visible = false;
        float gain = Mathf.Clamp01(brightness) * (1f / 0.72f);
        for (int band = 0; band < 2; band++)
        {
            float rest = band == 0 ? quietSeconds.x : quietSeconds.y;
            float hz = band == 0 ? upperFragmentHz : lowerFragmentHz;
            float t = BandTime(seconds, band, rest, tempoMultiplier, out int cycle);
            for (int part = 0; part < Pieces; part++)
            {
                Patch p = Sample(band, part, t, cycle, hz, lowerRightShift, bandSpread, gain, still);
                byte opacity = (byte)Mathf.RoundToInt(255f * p.alpha);
                int v = 4 * (band * Pieces + part);
                colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = new Color32(255, 255, 255, opacity);
                if (opacity == 0) continue;
                visible = true;
                float l = (p.x0 - 0.5f) * CanvasSize, r = (p.x1 - 0.5f) * CanvasSize;
                float top = (0.5f - p.y0) * CanvasSize, bot = (0.5f - p.y1) * CanvasSize;
                vertices[v] = new Vector3(l, bot, 0); vertices[v + 1] = new Vector3(l, top, 0);
                vertices[v + 2] = new Vector3(r, top, 0); vertices[v + 3] = new Vector3(r, bot, 0);
                float atlasY = band == 0 ? 4f : 132f;
                float vt = 1f - (atlasY + 120f * p.v0) / 256f;
                float vb = 1f - (atlasY + 120f * p.v1) / 256f;
                uv[v] = new Vector2(p.u0, vb); uv[v + 1] = new Vector2(p.u0, vt);
                uv[v + 2] = new Vector2(p.u1, vt); uv[v + 3] = new Vector2(p.u1, vb);
            }
        }
        if (!visible) { Hide(); return; }
        mesh.vertices = vertices; mesh.colors32 = colors; mesh.uv = uv;
        for (int i = 0; i < SideCount; i++)
        {
            if (draws[i].cull) draws[i].cull = false;
            draws[i].SetMesh(mesh);
        }
    }

    void Hide() { foreach (CanvasRenderer cr in draws) if (cr && !cr.cull) cr.cull = true; }
    void OnDisable() { heard = false; Hide(); }
    void OnDestroy()
    {
        foreach (Canvas cv in sides) if (cv) Destroy(cv.gameObject);
        if (mesh) Destroy(mesh);
        if (atlas) Destroy(atlas);
    }

    // Two authored RGBA bands. Edge gaps are transparent, not merely dim:
    // the larger silhouette breaks into short scanlines with the original colours.
    const string AtlasPng =
        "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";
}
