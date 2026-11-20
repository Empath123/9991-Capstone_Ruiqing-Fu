using UnityEngine;
using UnityEngine.Rendering;

// Add to the stationary "Door 1", NOT to "door" or "handle".
// Unity 6 / URP. Start the scene with the leaf closed.
[DisallowMultipleComponent, DefaultExecutionOrder(10000)]
[AddComponentMenu("Effects/Door 1 Glow")]
public sealed class Door1Glow : MonoBehaviour
{
    [Tooltip("转动的门板。原模型会自动找到 Door 1 下面的 door。")]
    [SerializeField] Transform doorLeaf;
    [Tooltip("添加组件时自动填写，不需要创建材质。")]
    [SerializeField] Shader unlitShader;

    const float SoftSeam = 4f, SoftLevel = 0.65f, SoftRadius = 14f;
    const float SoftHalo = 0.9f, SoftPeriod = 3.8f, SoftDepth = 0.32f;

    [Header("Closed | 门缝")]
    [Tooltip("四边淡蓝色细线的实际宽度（毫米）。")]
    [Range(1f, 24f)] public float seamWidthMm = SoftSeam;
    [Tooltip("只调关门时的亮度；0 关闭门框光。")]
    [Range(0f, 2f)] public float closedBrightness = SoftLevel;
    [Tooltip("从四条边向外渐隐的距离（厘米）。增大范围不会提高峰值亮度。")]
    [Range(4f, 36f)] public float haloRadiusCm = SoftRadius;
    [Tooltip("只控制柔和的蓝色光晕，不影响打开后的门洞。")]
    [Range(0f, 3f)] public float haloStrength = SoftHalo;
    [Tooltip("一次由亮变暗再变亮所需的秒数。")]
    [Range(1.5f, 9f)] public float pulseSeconds = SoftPeriod;
    [Tooltip("0 为常亮；越大，明暗差越明显。")]
    [Range(0f, 0.85f)] public float pulseDepth = SoftDepth;
    [Tooltip("底边高出地面的毫米数，避免地毯遮住底边。无地面碰撞体时以门板底部为准。")]
    [Range(1f, 50f)] public float thresholdLiftMm = 3f;
    [Header("Open | 门洞")]
    [Range(0.5f, 6f)] public float openBrightness = 2.4f;
    [Range(15f, 100f)] public float fullBrightnessAngle = 65f;
    [Range(0f, 5f)] public float floorSpill = 1.2f;
    [Tooltip("只有地面的光打向另一间房时才勾选。")]
    public bool flipFloorLight;

    const string ShaderName = "Universal Render Pipeline/Unlit";
    static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    Transform anchor;
    Bounds panel;
    Light spill;
    readonly RaycastHit[] floorHits = new RaycastHit[12];
    MeshFilter frameFilter;
    Surface opening, seam, halo;
    Texture2D gradient, haloTexture;
    Material openingMat, seamMat, haloMat;
    float closedFade;
    Quaternion closedPose, lastPose;
    Vector3 lastScale, lastMountPosition;
    Quaternion lastMountRotation;
    bool dirty = true;

    void Reset()
    {
        doorLeaf = transform.Find("door");
        unlitShader = Shader.Find(ShaderName); // Serialized reference also travels into the build.
    }
    void OnValidate() { dirty = true; }

#if UNITY_EDITOR
    // Existing prefab instances keep their previous Inspector values on a script update.
    [ContextMenu("Apply Soft Outward Glow")]
    void ApplySoftOutwardGlow()
    {
        UnityEditor.Undo.RecordObject(this, "Apply closed door glow");
        seamWidthMm = SoftSeam; closedBrightness = SoftLevel;
        haloRadiusCm = SoftRadius; haloStrength = SoftHalo;
        pulseSeconds = SoftPeriod; pulseDepth = SoftDepth;
        dirty = true;
        if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this))
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    void Awake()
    {
        if (!doorLeaf) doorLeaf = transform.Find("door");
        if (!doorLeaf || doorLeaf.parent != transform)
        {
            Debug.LogError("Door1Glow：请挂在 Door 1 根物体上，Door Leaf 选择其子物体 door（不是 handle 或 door frame）。", this);
            enabled = false; return;
        }
        MeshFilter filter = doorLeaf.GetComponent<MeshFilter>();
        BoxCollider box = doorLeaf.GetComponent<BoxCollider>();
        if (filter && filter.sharedMesh) panel = filter.sharedMesh.bounds;
        else if (box) panel = new Bounds(box.center, box.size);
        else
        {
            Debug.LogError("Door1Glow：Door Leaf 需要是带 Mesh Filter 或 Box Collider 的门板。", this);
            enabled = false; return;
        }
        if (!unlitShader) unlitShader = Shader.Find(ShaderName);
        if (!unlitShader || !unlitShader.isSupported)
        {
            Debug.LogError("Door1Glow：未找到可用的 Universal Render Pipeline/Unlit Shader。", this);
            enabled = false; return;
        }

        closedPose = doorLeaf.localRotation;
        anchor = Child("Door glow (runtime)", transform);
        anchor.localPosition = doorLeaf.localPosition;
        anchor.localRotation = closedPose;
        anchor.localScale = doorLeaf.localScale;
        gradient = MakeGradient();
        openingMat = new Material(unlitShader) { name = "Door light (runtime)" };
        openingMat.SetTexture(BaseMap, gradient);
        haloTexture = MakeHaloTexture();
        seamMat = MakeHaloMaterial(unlitShader, Texture2D.whiteTexture, "Four fine seams");
        haloMat = MakeHaloMaterial(unlitShader, haloTexture, "Outward blue falloff");
        opening = new Surface(Child("Light in opening", anchor), openingMat, 8);
        seam = new Surface(Child("Closed perimeter", anchor), seamMat, 8);
        halo = new Surface(Child("Outward glow - wall and floor", anchor), haloMat, 16);
        Transform jamb = transform.Find("door frame");
        if (jamb) frameFilter = jamb.GetComponent<MeshFilter>();
        spill = Child("Floor spill", anchor).gameObject.AddComponent<Light>();
        spill.type = LightType.Spot;
        spill.shadows = LightShadows.None;
        spill.color = new Color(0.56f, 0.81f, 1f);
        spill.spotAngle = 103f;
        spill.innerSpotAngle = 46f;
        spill.enabled = false;
        Refresh();
        if (!isActiveAndEnabled) anchor.gameObject.SetActive(false);
    }

    Transform Child(string label, Transform parent)
    {
        var go = new GameObject(label) { layer = doorLeaf.gameObject.layer };
        go.transform.SetParent(parent, false);
        return go.transform;
    }
    void OnEnable() { dirty = true; }
    void OnDisable() { if (anchor) anchor.gameObject.SetActive(false); }
    void LateUpdate() { if (anchor) Refresh(); }

    void Refresh()
    {
        bool visible = doorLeaf && doorLeaf.gameObject.activeInHierarchy;
        if (anchor.gameObject.activeSelf != visible) { anchor.gameObject.SetActive(visible); dirty = true; }
        if (!visible) return;
        Quaternion pose = doorLeaf.localRotation;
        Vector3 scale = anchor.lossyScale;
        Vector3 mountPosition = anchor.position; Quaternion mountRotation = anchor.rotation;
        bool rebuild = dirty || !scale.Equals(lastScale) || !mountPosition.Equals(lastMountPosition)
                     || !mountRotation.Equals(lastMountRotation);
        if (!rebuild && pose.Equals(lastPose))
        {
            AnimateClosed(); // Time still changes when the closed leaf does not.
            return;
        }
        lastPose = pose; lastScale = scale; dirty = false;
        lastMountPosition = mountPosition; lastMountRotation = mountRotation;

        float angle = Quaternion.Angle(closedPose, pose);
        float lit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(angle / Mathf.Max(1f, fullBrightnessAngle)));
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 12f, angle));
        float latch = Mathf.Abs(panel.min.x) > Mathf.Abs(panel.max.x) ? panel.min.x : panel.max.x;
        float hinge = latch == panel.min.x ? panel.max.x : panel.min.x;
        DrawSeams(fade, rebuild);
        AnimateClosed();

        bool open = angle > 0.2f;
        opening.renderer.enabled = open;
        if (!open) { spill.enabled = false; return; }

        Vector3 up = Vector3.up * panel.size.y;
        Vector3 h = new Vector3(hinge, panel.min.y, panel.center.z);
        Vector3 f = new Vector3(latch, panel.min.y, panel.center.z);
        // The actual, opaque leaf is one wall of this inward-facing light pocket.
        // The frame plane is visible from the swing side; the far return is visible
        // through the gap from the opposite side. Real depth works independently per XR eye.
        Matrix4x4 intoFrame = anchor.worldToLocalMatrix * doorLeaf.localToWorldMatrix;
        Vector3 end = intoFrame.MultiplyPoint3x4(f);
        Vector3 inside = (h + f + end) / 3f + up * 0.5f;
        float swingSide = end.z >= f.z ? 1f : -1f;
        // Recess the backing beyond even the hinge corner that sticks through the
        // frame when the leaf travels past 90 degrees. Four short returns seal it.
        float rz = Mathf.Abs(intoFrame.m20) * panel.extents.x + Mathf.Abs(intoFrame.m21) * panel.extents.y
                 + Mathf.Abs(intoFrame.m22) * panel.extents.z;
        float cz = intoFrame.MultiplyPoint3x4(panel.center).z;
        float bias = 0.002f / Mathf.Max(0.0001f, anchor.TransformVector(Vector3.forward).magnitude);
        Vector3 hb = h, fb = f;
        hb.z = fb.z = swingSide > 0f ? Mathf.Min(panel.min.z, cz - rz) - bias
                                     : Mathf.Max(panel.max.z, cz + rz) + bias;
        opening.Face(0, hb, fb, fb + up, hb + up, inside);
        opening.Face(1, f, end, end + up, f + up, inside);
        opening.Face(2, h + up, f + up, end + up, end + up, inside);
        opening.Face(3, h, f, end, end, inside);
        opening.Face(4, f, fb, fb + up, f + up, inside);
        opening.Face(5, h, hb, hb + up, h + up, inside);
        opening.Face(6, h + up, hb + up, fb + up, f + up, inside);
        opening.Face(7, h, hb, fb, f, inside);
        opening.Upload();
        float v = Mathf.Lerp(0.68f, openBrightness, lit);
        openingMat.SetColor(BaseColor, new Color(v, v, v, 1f));

        spill.enabled = floorSpill * lit > 0.008f;
        if (!spill.enabled) return;
        Vector3 intoRoom = anchor.forward * (flipFloorLight ? swingSide : -swingSide);
        Vector3 origin = anchor.TransformPoint(new Vector3(Mathf.Lerp(latch, panel.center.x, 0.48f + 0.52f * lit),
            panel.min.y + panel.size.y * 0.60f, panel.center.z));
        float depth = anchor.TransformVector(Vector3.forward * panel.extents.z).magnitude;
        spill.transform.SetPositionAndRotation(origin + intoRoom * (depth + 0.035f),
            Quaternion.LookRotation(intoRoom - anchor.up * 0.83f, anchor.up));
        spill.range = Mathf.Max(1f, anchor.TransformVector(up).magnitude * 1.65f);
        spill.intensity = floorSpill * lit;
    }

    void DrawSeams(float fade, bool rebuild)
    {
        closedFade = fade;
        seam.renderer.enabled = fade > 0.001f && closedBrightness > 0f;
        halo.renderer.enabled = seam.renderer.enabled && haloStrength > 0f;
        // Opening the leaf or animating the light need not rebuild the frame or probe the floor.
        if (!rebuild) return;

        float mx = 0.001f / Mathf.Max(0.0001f, anchor.TransformVector(Vector3.right).magnitude);
        float my = 0.001f / Mathf.Max(0.0001f, anchor.TransformVector(Vector3.up).magnitude);
        float mz = 0.001f / Mathf.Max(0.0001f, anchor.TransformVector(Vector3.forward).magnitude);
        float sx = Mathf.Min(Mathf.Max(1f, seamWidthMm) * mx, panel.size.x * 0.04f);
        float sy = Mathf.Min(Mathf.Max(1f, seamWidthMm) * my, panel.size.y * 0.04f);
        float radius = 10f * Mathf.Max(1f, haloRadiusCm); // cm to mm
        float left = panel.min.x - sx, right = panel.max.x + sx, top = panel.max.y + sy;
        float near = panel.min.z, far = panel.max.z;
        // Find the front and rear trim faces even if the door and frame have different FBX scales.
        if (frameFilter && frameFilter.sharedMesh)
        {
            Bounds b = frameFilter.sharedMesh.bounds;
            Matrix4x4 m = anchor.worldToLocalMatrix * frameFilter.transform.localToWorldMatrix;
            float c = m.MultiplyPoint3x4(b.center).z;
            float e = Mathf.Abs(m.m20) * b.extents.x + Mathf.Abs(m.m21) * b.extents.y
                    + Mathf.Abs(m.m22) * b.extents.z;
            near = Mathf.Min(near, c - e);
            far = Mathf.Max(far, c + e);
        }

        for (int i = 0; i < 2; ++i)
        {
            float side = i == 0 ? -1f : 1f;
            float z = (i == 0 ? near : far) + side * 2.5f * mz;
            float bottom = FloorY(panel.center.x, z + side * 70f * mz, my);
            Vector3 bl = new Vector3(left, bottom, z), br = new Vector3(right, bottom, z);
            Vector3 tl = new Vector3(left, top, z), tr = new Vector3(right, top, z);
            Vector3 ix = Vector3.right * sx, iy = Vector3.up * sy;
            Vector3 ibl = bl + ix + iy, ibr = br - ix + iy;
            Vector3 itl = tl + ix - iy, itr = tr - ix - iy;
            Vector3 facing = bl + Vector3.forward * side;
            // Four mitred core strips. Adjacent pieces share an edge, not an area.
            int k = i * 4;
            seam.Face(k,     bl, tl, itl, ibl, facing);
            seam.Face(k + 1, tl, tr, itr, itl, facing);
            seam.Face(k + 2, tr, br, ibr, itr, facing);
            seam.Face(k + 3, br, bl, ibl, ibr, facing);

            Vector3 dx = Vector3.right * (radius * mx), dy = Vector3.up * (radius * my);
            Vector3 outward = Vector3.forward * (side * radius * mz);
            Vector3 above = bl + Vector3.up;
            int h = i * 8;
            // One-sided straight bands; no card extends over the door panel.
            GlowTile(h,     bl, -dx, tl - bl, facing, false);  // left -> left
            GlowTile(h + 1, br,  dx, tr - br, facing, false);  // right -> right
            GlowTile(h + 2, tl,  dy, tr - tl, facing, false);  // top -> up
            // A single radial corner fills each gap without two additive bands overlapping.
            GlowTile(h + 3, tl, -dx, dy, facing, true);
            GlowTile(h + 4, tr,  dx, dy, facing, true);
            // Unfold the lower halo onto the floor instead of drawing it underneath the floor.
            GlowTile(h + 5, bl, outward, br - bl, above, false);
            GlowTile(h + 6, bl, -dx, outward, above, true);
            GlowTile(h + 7, br,  dx, outward, above, true);
        }
        seam.Upload(); halo.Upload();
    }

    float FloorY(float x, float z, float my)
    {
        float baseY = panel.min.y;
        Vector3 up = anchor.TransformVector(Vector3.up).normalized;
        Vector3 p = anchor.TransformPoint(new Vector3(x, baseY, z));
        int count = Physics.RaycastNonAlloc(p + up * 0.20f, -up, floorHits, 0.45f,
                                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float y = baseY, best = float.PositiveInfinity;
        for (int i = 0; i < count; ++i)
        {
            RaycastHit hit = floorHits[i];
            if (!hit.collider || hit.collider.transform.IsChildOf(transform) || Vector3.Dot(hit.normal, up) < 0.75f)
                continue;
            float delta = Mathf.Abs(Vector3.Dot(hit.point - p, up));
            if (delta >= best) continue;
            best = delta;
            y = anchor.InverseTransformPoint(hit.point).y;
        }
        return y + Mathf.Max(1f, thresholdLiftMm) * my;
    }

    void GlowTile(int i, Vector3 origin, Vector3 away, Vector3 along, Vector3 facing, bool radialCorner)
    {
        bool reversed = halo.Face(i, origin, origin + away, origin + away + along, origin + along, facing);
        // UV (0,0) is the seam. A straight band samples the first row; a corner samples the quarter disc.
        halo.MapQuad(i, reversed, Vector2.zero, Vector2.right,
                     radialCorner ? Vector2.one : Vector2.right, radialCorner ? Vector2.up : Vector2.zero);
    }

    void AnimateClosed()
    {
        if (!seam.renderer.enabled) return;
        double period = Mathf.Max(1.5f, pulseSeconds);
        float cycle = (float)((Time.timeAsDouble % period) / period);
        float wave = 0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * cycle);
        float breath = Mathf.Lerp(1f - Mathf.Clamp(pulseDepth, 0f, 0.85f), 1f, wave) * closedFade;
        // Gentle saturation also tames scenes that retain older, much brighter serialized settings.
        float level = Mathf.Max(0f, closedBrightness);
        float core = (0.95f * level / (1f + 0.8f * level)) * breath;
        seamMat.SetColor(BaseColor, new Color(0.48f * core, 0.76f * core, 0.90f * core, 1f));
        if (halo.renderer.enabled)
        {
            float h = level * Mathf.Max(0f, haloStrength);
            float glow = (0.75f * h / (1f + 1.25f * h)) * breath;
            haloMat.SetColor(BaseColor, new Color(glow, glow, glow, 1f));
        }
    }

    static Material MakeHaloMaterial(Shader shader, Texture2D tex, string label)
    {
        var m = new Material(shader) { name = label + " (runtime)" };
        m.SetTexture(BaseMap, tex);
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f); // URP Transparent / Additive
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
        if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.Zero);
        if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0f);  // Test normal scene depth, do not hide the handle or VR hands.
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetShaderPassEnabled("ShadowCaster", false); m.SetShaderPassEnabled("DepthOnly", false);
        m.SetShaderPassEnabled("DepthNormals", false); m.SetShaderPassEnabled("DepthNormalsOnly", false);
        return m;
    }

    static Texture2D MakeHaloTexture()
    {
        const int n = 96;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, true) {
            name = "Outward quarter falloff", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        var pixels = new Color32[n * n];
        for (int y = 0; y < n; ++y)
        for (int x = 0; x < n; ++x)
        {
            float r = Mathf.Sqrt(x * x + y * y) / (n - 1f);
            float feather = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.60f, 1f, r));
            float a = Mathf.Sqrt(Mathf.Exp(-2.45f * r) * feather);
            // SrcAlpha + One: RGB and alpha both tend to zero, without a rectangular fringe.
            pixels[y * n + x] = new Color(0.29f * a, 0.55f * a, 0.79f * a, a);
        }
        tex.SetPixels32(pixels); tex.Apply(true,true);
        return tex;
    }

    static Texture2D MakeGradient()
    {
        const int n = 96;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, true) {
            name = "Doorway sky (runtime)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        var pixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = Mathf.Min(x, n - 1 - x) / (n - 1f), dy = Mathf.Min(y, n - 1 - y) / (n - 1f);
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(dx, dy * 2f) / 0.16f));
            pixels[y * n + x] = Color.Lerp(new Color(0.065f, 0.37f, 0.85f), new Color(0.94f, 0.98f, 1f), t);
        }
        tex.SetPixels32(pixels); tex.Apply(true, true);
        return tex;
    }

    // A handful of reusable quads, no primitive colliders, no per-frame allocations.
    sealed class Surface
    {
        public readonly MeshRenderer renderer;
        public readonly Mesh mesh;
        readonly Vector3[] vertices, normals;
        readonly Vector2[] uv;
        bool changedUV;
        public Surface(Transform parent, Material mat, int faces)
        {
            vertices = new Vector3[4 * faces]; normals = new Vector3[4 * faces];
            uv = new Vector2[4 * faces]; var tri = new int[6 * faces];
            for (int i = 0; i < faces; i++)
            {
                int v = 4 * i, k = 6 * i;
                uv[v] = Vector2.zero; uv[v+1] = Vector2.right; uv[v+2] = Vector2.one; uv[v+3] = Vector2.up;
                tri[k] = v; tri[k+1] = v+1; tri[k+2] = v+2;
                tri[k+3] = v; tri[k+4] = v+2; tri[k+5] = v+3;
            }
            mesh = new Mesh { name = parent.name }; mesh.MarkDynamic();
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = tri;
            parent.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = parent.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat; renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        public bool Face(int i, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            int j = i * 4;
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            bool reversed = Vector3.Dot(n, facing - a) < 0f;
            if (reversed)
            {
                // Reverse the winding without transposing the texture.
                vertices[j] = b; vertices[j+1] = a; vertices[j+2] = d; vertices[j+3] = c; n = -n;
            }
            else { vertices[j] = a; vertices[j+1] = b; vertices[j+2] = c; vertices[j+3] = d; }
            normals[j] = normals[j+1] = normals[j+2] = normals[j+3] = n;
            return reversed;
        }
        public void MapQuad(int i, bool reversed, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int j = i * 4;
            uv[j] = reversed ? b : a; uv[j+1] = reversed ? a : b;
            uv[j+2] = reversed ? d : c; uv[j+3] = reversed ? c : d;
            changedUV = true;
        }
        public void Upload()
        {
            mesh.vertices = vertices; mesh.normals = normals;
            if (changedUV) { mesh.uv = uv; changedUV = false; }
            mesh.RecalculateBounds();
        }
    }

    void OnDestroy()
    {
        if (anchor) Destroy(anchor.gameObject);
        if (opening != null) Destroy(opening.mesh);
        if (seam != null) Destroy(seam.mesh);
        if (halo != null) Destroy(halo.mesh);
        if (openingMat) Destroy(openingMat);
        if (seamMat) Destroy(seamMat);
        if (haloMat) Destroy(haloMat);
        if (gradient) Destroy(gradient);
        if (haloTexture) Destroy(haloTexture);
    }
}
