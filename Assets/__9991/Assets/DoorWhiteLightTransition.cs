using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Unity 6 / URP / XR. Attach to "Door 1", not the rotating leaf.
[DisallowMultipleComponent]
[AddComponentMenu("Effects/Door White Light Transition")]
public sealed class DoorWhiteLightTransition : MonoBehaviour
{
    [Header("Scene references")]
    [Tooltip("The rotating panel: Door 1 > door. Start with the door closed.")]
    [SerializeField] Transform doorLeaf;
    [Tooltip("XR Origin > Camera Offset > Main Camera")]
    [SerializeField] Camera playerCamera;
    [Tooltip("Scene name from the active Build Profile. Leave blank to preview and fade back.")]
    [SerializeField] string nextScene = "";

    [Header("White light")]
    [Range(3f, 90f)] [SerializeField] float triggerAngle = 18f;
    [Min(0.5f)] [SerializeField] float spreadSeconds = 5.8f;
    [Min(0.1f)] [SerializeField] float whiteHoldSeconds = 0.7f;
    [Range(0f, 1f)] [SerializeField] float doorwayHaze = 0.78f;

    enum Phase { Armed, Spreading, Holding, Returning, WaitForClose, Loading }
    Phase phase;
    Quaternion closed;
    Vector3 opening, foot, roomSide;
    float doorHeight, doorWidth, elapsed;
    bool ready, previewOnly;

    GameObject roomEffects, eyeEffects;
    RawImage haze, white;
    RectTransform hazeRect;
    Light spill;
    Texture2D softDisc;
    int viewLayer;

    void Reset()
    {
        doorLeaf = transform.Find("door");
        playerCamera = Camera.main;
    }

    void Start()
    {
        if (!doorLeaf) doorLeaf = transform.Find("door");
        if (!playerCamera) playerCamera = Camera.main;
        if (!doorLeaf || doorLeaf == transform || !playerCamera ||
            !doorLeaf.TryGetComponent<BoxCollider>(out var panel))
        {
            Debug.LogError("Door White Light: assign the rotating door with its Box Collider and the XR Main Camera.", this);
            enabled = false;
            return;
        }
        if (playerCamera.cullingMask == 0)
        {
            Debug.LogError("Door White Light: the assigned camera renders no layers.", this);
            enabled = false;
            return;
        }

        closed = doorLeaf.localRotation;
        // Capture the CLOSED aperture, not the moving leaf or its hinge pivot.
        opening = panel.transform.TransformPoint(panel.center);
        Bounds b = panel.bounds;
        doorHeight = Mathf.Max(0.5f, b.size.y);
        Vector3 scaled = Vector3.Scale(panel.size, panel.transform.lossyScale);
        Vector3 axis = Mathf.Abs(scaled.x) <= Mathf.Abs(scaled.z) ? Vector3.right : Vector3.forward;
        roomSide = Vector3.ProjectOnPlane(panel.transform.TransformDirection(axis), Vector3.up).normalized;
        if (roomSide.sqrMagnitude < 0.5f) roomSide = Vector3.forward;
        doorWidth = Mathf.Max(0.35f, Mathf.Max(Mathf.Abs(scaled.x), Mathf.Abs(scaled.z)));
        foot = opening - Vector3.up * (doorHeight * 0.5f);
        viewLayer = LayerMask.NameToLayer("UI");
        if (viewLayer < 0 || (playerCamera.cullingMask & (1 << viewLayer)) == 0)
        {
            for (viewLayer = 0; viewLayer < 32; viewLayer++)
                if ((playerCamera.cullingMask & (1 << viewLayer)) != 0) break;
        }
        ready = true;
    }

    static float Ease(float from, float to, float t)
    {
        float k = Mathf.InverseLerp(from, to, t);
        return k * k * (3f - 2f * k);
    }

    void LateUpdate()
    {
        if (!ready || !doorLeaf || !playerCamera) return;
        switch (phase)
        {
            case Phase.Armed:
                if (Quaternion.Angle(closed, doorLeaf.localRotation) >= triggerAngle)
                    Begin(false);
                break;
            case Phase.Spreading:
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.5f, spreadSeconds));
                Paint(t, 1f);
                if (t >= 1f) { elapsed = 0f; phase = Phase.Holding; }
                break;
            case Phase.Holding:
                elapsed += Time.unscaledDeltaTime;
                if (elapsed >= Mathf.Max(0.1f, whiteHoldSeconds)) Finish();
                break;
            case Phase.Returning:
                elapsed += Time.unscaledDeltaTime;
                float k = 1f - Ease(0f, 1.25f, elapsed);
                Paint(1f, k);
                if (k <= 0f) { Hide(); phase = Phase.WaitForClose; }
                break;
            case Phase.WaitForClose:
                if (Quaternion.Angle(closed, doorLeaf.localRotation) < triggerAngle * 0.5f)
                    phase = Phase.Armed;
                break;
        }
    }

    [ContextMenu("Preview White Light (Play Mode Only)")]
    void Preview()
    {
        if (Application.isPlaying && isActiveAndEnabled && ready &&
            (phase == Phase.Armed || phase == Phase.WaitForClose)) Begin(true);
    }

    void Begin(bool forcePreview)
    {
        if (!roomEffects) BuildEffects();
        if (Vector3.Dot(roomSide, playerCamera.transform.position - opening) < 0f)
            roomSide = -roomSide;
        previewOnly = forcePreview;
        elapsed = 0f;
        phase = Phase.Spreading;
        Paint(0f, 1f);
        roomEffects.SetActive(true);
        eyeEffects.SetActive(true);
    }

    void Paint(float t, float gain)
    {
        float expand = Ease(0f, 0.70f, t);
        // A broad, feathered patch in world space, fixed to the doorway.
        float w = doorWidth * Mathf.Lerp(1.2f, 4.8f, expand);
        float h = doorHeight * Mathf.Lerp(0.85f, 2.4f, expand);
        Vector3 center = opening + roomSide * (0.12f + expand * 0.14f);
        Vector3 toEye = playerCamera.transform.position - center;
        hazeRect.position = center;
        hazeRect.rotation = Quaternion.LookRotation(-roomSide, Vector3.up);
        hazeRect.sizeDelta = new Vector2(w, h);
        float inFront = Ease(0.03f, 0.38f, Vector3.Dot(roomSide, toEye));
        haze.color = new Color(1f, 0.995f, 0.98f,
            doorwayHaze * Ease(0f, 0.52f, t) * inFront * gain);

        // Diffuse spill on real lit room surfaces; no cone, cookie, shafts or particles.
        spill.transform.position = foot + Vector3.up * (0.26f * doorHeight)
            + roomSide * Mathf.Lerp(0.22f, 0.85f * doorHeight, expand);
        spill.range = doorHeight * Mathf.Lerp(0.85f, 2.9f, expand);
        spill.intensity = 2.1f * expand * gain;

        // This layer follows head tracking: looking away cannot reveal a hard edge.
        float veil = 0.27f * Ease(0.18f, 0.75f, t) + 0.73f * Ease(0.50f, 1f, t);
        white.color = new Color(1f, 1f, 1f, veil * gain);
    }

    void Finish()
    {
        string scene = (nextScene ?? "").Trim();
        if (!previewOnly && scene.Length > 0 && Application.CanStreamedLevelBeLoaded(scene))
        {
            phase = Phase.Loading; // Keep both eyes covered until the old scene unloads.
            if (SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single) != null) return;
        }
        if (!previewOnly && scene.Length > 0)
            Debug.LogWarning("Door White Light: cannot load '" + scene + "'. Check the active Build Profile's Scene List.", this);
        elapsed = 0f;
        phase = Phase.Returning;
    }

    void BuildEffects()
    {
        softDisc = MakeSoftDisc();
        // A scene root keeps the haze at a world-space size, independent of prefab scaling.
        roomEffects = new GameObject("Door diffuse light (runtime)");
        SceneManager.MoveGameObjectToScene(roomEffects, gameObject.scene);
        haze = MakeCanvas("Doorway haze", roomEffects.transform, 10);
        haze.texture = softDisc;
        hazeRect = (RectTransform)haze.canvas.transform;
        var lamp = new GameObject("Diffuse room spill");
        lamp.transform.SetParent(roomEffects.transform, false);
        spill = lamp.AddComponent<Light>();
        spill.type = LightType.Point;
        spill.shadows = LightShadows.None;
        spill.color = new Color(1f, 0.985f, 0.96f);
        spill.intensity = 0f;

        white = MakeCanvas("Door white veil (runtime)", playerCamera.transform, 32760);
        var screen = (RectTransform)white.canvas.transform;
        eyeEffects = screen.gameObject;
        float depth = Mathf.Max(0.01f, playerCamera.nearClipPlane) + 0.016f;
        // Size in world units; allow for wide asymmetric XR views and separation of both eyes.
        Vector3 s = playerCamera.transform.lossyScale;
        screen.localScale = new Vector3(1f / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(s.y)), 1f / Mathf.Max(0.0001f, Mathf.Abs(s.z)));
        screen.position = playerCamera.transform.position + playerCamera.transform.forward * depth;
        screen.sizeDelta = Vector2.one * Mathf.Max(1f, 8f * depth + 0.3f);
    }

    RawImage MakeCanvas(string label, Transform parent, int order)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Canvas));
        go.layer = viewLayer;
        go.transform.SetParent(parent, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = playerCamera;
        canvas.sortingOrder = order;
        var root = (RectTransform)go.transform;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;
        root.localPosition = Vector3.zero;
        var imageObject = new GameObject("Soft white", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        imageObject.layer = viewLayer;
        var rect = (RectTransform)imageObject.transform;
        rect.SetParent(go.transform, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = imageObject.GetComponent<RawImage>();
        image.raycastTarget = false;
        image.color = Color.clear;
        return image;
    }

    static Texture2D MakeSoftDisc()
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.name = "Feathered doorway haze";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = 2f * x / (n - 1f) - 1f, v = 2f * y / (n - 1f) - 1f;
            float d = u * u + v * v;
            float a = Mathf.Exp(-2.8f * d) * (1f - Ease(0.55f, 1f, d));
            pixels[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    void Hide()
    {
        if (roomEffects) roomEffects.SetActive(false);
        if (eyeEffects) eyeEffects.SetActive(false);
    }

    void OnDisable()
    {
        Hide();
        if (phase != Phase.Loading) phase = Phase.WaitForClose;
    }

    void OnDestroy()
    {
        if (eyeEffects) Destroy(eyeEffects);
        if (roomEffects) Destroy(roomEffects);
        if (softDisc) Destroy(softDisc);
    }
}
