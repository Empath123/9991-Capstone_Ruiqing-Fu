using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

/// <summary>挂在笔记本的 Screen Open 上。纸团和光标使用同一张桌面的 UV 坐标。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(300)] // LaptopMouseXR (100) → OpenMailOnHover (200) → 纸团
public sealed class DesktopPaperGame : MonoBehaviour
{
    [Tooltip("拖入邮件脚本的 Opened Mail 对象。留空时使用 Screen Open/Email Content。")]
    [SerializeField] private GameObject emailContent;
    [Tooltip("留空时使用 Screen Open 下运行时生成的 Mouse Cursor (Runtime)。如手动指定，请选择光标尖端。")]
    [SerializeField] private Transform cursorTip;
    [Tooltip("通常留空。只在桌面使用其他网格时，拖入显示桌面图片的 MeshFilter。")]
    [SerializeField] private MeshFilter desktopSurface;
    [Tooltip("回收站桶口在桌面图片中的位置。左下角为 (0,0)，右上角为 (1,1)。")]
    [SerializeField] private Vector2 binOpening = new Vector2(0.0523f, 0.5292f);
    [Tooltip("纸团直径占屏幕高度的比例。")]
    [SerializeField, Range(0.03f, 0.10f)] private float paperSize = 0.063f;

    private const float SpawnWindow = 15f;
    private const int MaxPapers = 10;

    private sealed class Paper
    {
        public Vector2 start;
        public float size, angle, spin, flightDuration;
        public float launchTime = -1f;
    }

    private readonly List<Paper> papers = new List<Paper>(MaxPapers);
    private readonly List<Vector3> vertices = new List<Vector3>(MaxPapers * 48);
    private readonly List<Vector2> textureUV = new List<Vector2>(MaxPapers * 48);
    private readonly List<int> triangles = new List<int>(MaxPapers * 48 * 3);
    private Vector2[] outline, paperUV;
    private MeshRenderer display, drawing;
    private SpriteRenderer emailIcon;
    private Mesh paperMesh;
    private Material paperMaterial;
    private Texture2D paperImage;
    private Vector3 origin, across, up, uCoordinate, vCoordinate;
    private float aspect, stopAt, nextAt;
    private int spawned;
    private bool screenLit, spawningStopped, mailOpened, hadTip;
    private Vector2 lastTip;

    private void Awake()
    {
        if (!emailContent)
        {
            Transform body = transform.Find("Email Content");
            if (body) emailContent = body.gameObject;
        }
        if (!emailContent)
        {
            Debug.LogError("DesktopPaperGame：请把邮件正文（OpenMailOnHover 的 Opened Mail）拖入 Email Content。", this);
            enabled = false;
            return;
        }

        Transform icon = transform.Find("Email");
        if (icon) emailIcon = icon.GetComponent<SpriteRenderer>();

        if (!desktopSurface)
        {
            // 兼容旧版子物体桌面和新版直接显示桌面图片的 Screen Open。
            foreach (string childName in new[] { "Desktop UI Full", "Desktop UI" })
            {
                Transform child = transform.Find(childName);
                if (!child || !child.gameObject.activeSelf) continue;
                MeshRenderer mr = child.GetComponent<MeshRenderer>();
                MeshFilter mf = child.GetComponent<MeshFilter>();
                if (mr && mr.enabled && !mr.forceRenderingOff && mf && mf.sharedMesh)
                {
                    desktopSurface = mf;
                    break;
                }
            }
            if (!desktopSurface) desktopSurface = GetComponent<MeshFilter>();
        }
        display = desktopSurface ? desktopSurface.GetComponent<MeshRenderer>() : null;
        if (!display || !display.sharedMaterial || !ReadScreenPlane())
        {
            Debug.LogError("DesktopPaperGame：Desktop Surface 需要显示桌面贴图的、可读取且带 UV 的屏幕网格。", this);
            enabled = false;
            return;
        }
        CreatePaperLayer();
    }

    private void LateUpdate()
    {
        if (mailOpened) return;
        if (emailContent && emailContent.activeInHierarchy)
        {
            mailOpened = true;
            ClearDesktop(); // 包括飞到一半的纸团，同一帧隐藏。
            return;
        }

        if (!display.enabled || display.forceRenderingOff || !display.gameObject.activeInHierarchy)
        {
            if (screenLit) ClearDesktop();
            return;
        }
        float now = Time.time; // 与 OpenMailOnHover 的 15 秒使用同一个时钟。
        if (!screenLit)
        {
            screenLit = true;
            stopAt = now + SpawnWindow;
            nextAt = now;
            spawned = 0;
            spawningStopped = false;
        }
        if (now >= stopAt || (emailIcon && emailIcon.gameObject.activeInHierarchy &&
            emailIcon.enabled && !emailIcon.forceRenderingOff && emailIcon.sprite && emailIcon.color.a > 0f))
            spawningStopped = true;

        bool changed = false;
        bool hasTip = ReadTip(out Vector2 tip);
        for (int i = papers.Count - 1; i >= 0; i--)
        {
            Paper p = papers[i];
            if (p.launchTime < 0f && hasTip && CrossesPaper(p, hadTip ? lastTip : tip, tip))
                p.launchTime = now;
            if (p.launchTime < 0f) continue;
            changed = true;
            // 留一小段时间在桶口内画出最后一帧，低帧率时也看得见落点。
            if (now - p.launchTime >= p.flightDuration + 0.05f) papers.RemoveAt(i);
        }
        lastTip = tip;
        hadTip = hasTip;

        if (!spawningStopped && spawned < MaxPapers && now >= nextAt)
        {
            if (SpawnPaper())
            {
                spawned++;
                changed = true;
                nextAt = now + Random.Range(1.25f, 1.60f);
            }
            else nextAt = now + 0.15f;
        }
        if (changed) Draw(now);
    }

    private bool SpawnPaper()
    {
        float size = paperSize * Random.Range(0.85f, 1.14f);
        for (int attempt = 0; attempt < 24; attempt++)
        {
            // 避开左侧图标、底部任务栏，以及右下角的邮件通知。
            Vector2 pos = new Vector2(Random.Range(0.155f, 0.93f), Random.Range(0.16f, 0.85f));
            if (pos.x > 0.66f && pos.y < 0.36f) continue;
            bool overlaps = false;
            foreach (Paper other in papers)
            {
                Vector2 d = pos - other.start;
                d.x *= aspect;
                if (other.launchTime < 0f && d.magnitude < (size + other.size) * 0.60f)
                {
                    overlaps = true;
                    break;
                }
            }
            if (overlaps) continue;
            papers.Add(new Paper {
                start = pos, size = size, angle = Random.Range(-32f, 32f),
                spin = Random.Range(80f, 150f) * (Random.value < 0.5f ? -1f : 1f),
                flightDuration = Mathf.Lerp(0.48f, 0.82f, Mathf.Abs(pos.x - binOpening.x))
            });
            return true;
        }
        return false;
    }

    // 从不共线的三个桌面 UV 还原屏幕平面。保留原 FBX 的轴心、缩放和盖子转角。
    private bool ReadScreenPlane()
    {
        Mesh mesh = desktopSurface.sharedMesh;
        if (!mesh || !mesh.isReadable) return false;
        Vector3[] v = mesh.vertices;
        Vector2[] uv = mesh.uv;
        if (uv.Length == 0 || uv.Length != v.Length) return false;
        int[] indices = mesh.triangles;
        float best = 1e-8f;
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            Vector2 s = uv[b] - uv[a], t = uv[c] - uv[a];
            float det = Cross(s, t);
            if (Mathf.Abs(det) <= best) continue;
            best = Mathf.Abs(det);
            across = ((v[b] - v[a]) * t.y - (v[c] - v[a]) * s.y) / det;
            up = ((v[c] - v[a]) * s.x - (v[b] - v[a]) * t.x) / det;
            origin = v[a] - across * uv[a].x - up * uv[a].y;
        }

        Transform panel = desktopSurface.transform;
        Vector3 worldRight = panel.TransformVector(across), worldUp = panel.TransformVector(up);
        Vector3 front = Vector3.Cross(worldUp.normalized, worldRight.normalized).normalized;
        if (front.sqrMagnitude < 0.5f) return false;
        aspect = worldRight.magnitude / worldUp.magnitude;
        Vector3 localFront = panel.InverseTransformVector(front);
        float determinant = Vector3.Dot(across, Vector3.Cross(up, localFront));
        if (Mathf.Abs(determinant) < 1e-12f) return false;
        uCoordinate = Vector3.Cross(up, localFront) / determinant;
        vCoordinate = Vector3.Cross(localFront, across) / determinant;
        float depth = 0f;
        foreach (Vector3 vertex in v)
            depth = Mathf.Max(depth, Vector3.Dot(panel.TransformVector(vertex - origin), front));
        // 与 LaptopMouseXR 同向，但深度偏移小于光标，箭头仍在纸团前面。
        origin += localFront * (depth + 0.45f * Mathf.Max(0.00015f, worldUp.magnitude * 0.0008f));
        return true;
    }

    private bool ReadTip(out Vector2 tip)
    {
        tip = Vector2.zero;
        if (!cursorTip) cursorTip = transform.Find("Mouse Cursor (Runtime)");
        if (!cursorTip || !cursorTip.gameObject.activeInHierarchy) return false;
        Vector3 d = desktopSurface.transform.InverseTransformPoint(cursorTip.position) - origin;
        tip = new Vector2(Vector3.Dot(d, uCoordinate), Vector3.Dot(d, vCoordinate));
        return true;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private bool CrossesPaper(Paper p, Vector2 from, Vector2 to)
    {
        // 转到纸团自身的二维坐标，沿前后两帧的箭头轨迹检测不规则的可见轮廓。
        float radians = p.angle * Mathf.Deg2Rad;
        float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
        Vector2 InPaper(Vector2 point)
        {
            Vector2 d = point - p.start;
            d.x *= aspect;
            return new Vector2(d.x * c + d.y * s, d.y * c - d.x * s) / p.size;
        }
        Vector2 start = InPaper(from), end = InPaper(to);
        if (Mathf.Min(start.x, end.x) > 0.51f || Mathf.Max(start.x, end.x) < -0.51f ||
            Mathf.Min(start.y, end.y) > 0.51f || Mathf.Max(start.y, end.y) < -0.51f) return false;

        Vector2 path = end - start;
        bool inside = false;
        Vector2 a = outline[outline.Length - 1];
        foreach (Vector2 b in outline)
        {
            Vector2 edge = b - a;
            float det = Cross(path, edge);
            if (Mathf.Abs(det) > 1e-7f)
            {
                float alongPath = Cross(a - start, edge) / det;
                float alongEdge = Cross(a - start, path) / det;
                if (alongPath >= 0f && alongPath <= 1f && alongEdge >= 0f && alongEdge <= 1f)
                    return true;
            }
            if ((a.y > end.y) != (b.y > end.y) && end.x < a.x + (end.y - a.y) * edge.x / edge.y)
                inside = !inside;
            a = b;
        }
        return inside;
    }

    private void Draw(float now)
    {
        drawing.enabled = papers.Count > 0;
        if (!drawing.enabled) return;
        vertices.Clear();
        textureUV.Clear();
        triangles.Clear();
        foreach (Paper p in papers)
        {
            float t = p.launchTime < 0f ? 0f : Mathf.Clamp01((now - p.launchTime) / p.flightDuration);
            // 最后落在桶口内侧，纸团缩小后才消失。
            Vector2 landing = binOpening - new Vector2(0f, 0.016f);
            Vector2 uv = Vector2.Lerp(p.start, landing, t);
            float lift = Mathf.Min(0.20f, Mathf.Max(0f, 0.94f - Mathf.Max(p.start.y, landing.y)));
            uv.y += 4f * lift * t * (1f - t);
            float size = p.size * (1f - 0.91f * t * t);
            float angle = (p.angle + p.spin * t) * Mathf.Deg2Rad;
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            Vector3 centre = origin + across * uv.x + up * uv.y;
            Vector3 x = (across * (c / aspect) + up * s) * size;
            Vector3 y = (up * c - across * (s / aspect)) * size;
            int first = vertices.Count;
            foreach (Vector2 point in outline) vertices.Add(centre + x * point.x + y * point.y);
            textureUV.AddRange(paperUV);
            foreach (int index in PaperTriangles) triangles.Add(first + index);
        }
        paperMesh.Clear();
        paperMesh.SetVertices(vertices);
        paperMesh.SetUVs(0, textureUV);
        paperMesh.SetTriangles(triangles, 0, true);
    }

    private void CreatePaperLayer()
    {
        // 直接用参考图的纸团区域作为贴图。网格沿纸边裁切，图中的白底不参与绘制。
        paperImage = new Texture2D(2, 2, TextureFormat.RGBA32, true);
        paperImage.name = "Desktop crumpled paper";
        ImageConversion.LoadImage(paperImage, Convert.FromBase64String(PaperPng), true);
        paperImage.wrapMode = TextureWrapMode.Clamp;
        paperImage.filterMode = FilterMode.Trilinear;
        outline = new Vector2[PaperPixels.Length];
        paperUV = new Vector2[PaperPixels.Length];
        for (int i = 0; i < PaperPixels.Length; i++)
        {
            Vector2 pixel = PaperPixels[i];
            outline[i] = new Vector2(pixel.x - 115f, 129f - pixel.y) / 148f;
            paperUV[i] = new Vector2((pixel.x + 0.5f) / paperImage.width,
                                     1f - (pixel.y + 0.5f) / paperImage.height);
        }
        paperMaterial = new Material(display.sharedMaterial) { name = "Desktop paper (runtime)" };
        string map = paperMaterial.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
        paperMaterial.SetTexture(map, paperImage);
        paperMaterial.SetTextureScale(map, Vector2.one);
        paperMaterial.SetTextureOffset(map, Vector2.zero);
        if (paperMaterial.HasProperty("_BaseColor")) paperMaterial.SetColor("_BaseColor", Color.white);
        if (paperMaterial.HasProperty("_Color")) paperMaterial.SetColor("_Color", Color.white);
        if (paperMaterial.HasProperty("_Cull")) paperMaterial.SetFloat("_Cull", 0f);

        var layer = new GameObject("Desktop paper (Runtime)");
        layer.layer = display.gameObject.layer;
        layer.transform.SetParent(desktopSurface.transform, false);
        paperMesh = new Mesh { name = "Desktop paper silhouettes" };
        paperMesh.MarkDynamic();
        layer.AddComponent<MeshFilter>().sharedMesh = paperMesh;
        drawing = layer.AddComponent<MeshRenderer>();
        drawing.sharedMaterial = paperMaterial;
        drawing.sortingLayerID = display.sortingLayerID;
        drawing.shadowCastingMode = ShadowCastingMode.Off;
        drawing.receiveShadows = false;
        drawing.enabled = false;
    }

    private void ClearDesktop()
    {
        papers.Clear();
        if (drawing) drawing.enabled = false;
        hadTip = false;
        screenLit = false;
    }

    private void OnDisable() => ClearDesktop();

    private void OnDestroy()
    {
        if (drawing) Destroy(drawing.gameObject);
        if (paperMesh) Destroy(paperMesh);
        if (paperMaterial) Destroy(paperMaterial);
        if (paperImage) Destroy(paperImage);
    }

    #region Paper illustration
    // Pixel coordinates on the supplied reference, clockwise from the top paper fold.
    private static readonly Vector2[] PaperPixels = {
        new Vector2(81, 58), new Vector2(91, 57), new Vector2(97, 59), new Vector2(105, 69),
        new Vector2(110, 68), new Vector2(116, 67), new Vector2(123, 74), new Vector2(134, 70),
        new Vector2(142, 71), new Vector2(149, 74), new Vector2(171, 73), new Vector2(171, 84),
        new Vector2(168, 94), new Vector2(165, 100), new Vector2(177, 113), new Vector2(188, 127),
        new Vector2(185, 137), new Vector2(185, 150), new Vector2(182, 166), new Vector2(177, 183),
        new Vector2(162, 188), new Vector2(148, 191), new Vector2(140, 188), new Vector2(133, 191),
        new Vector2(128, 197), new Vector2(114, 197), new Vector2(96, 199), new Vector2(82, 201),
        new Vector2(71, 195), new Vector2(56, 183), new Vector2(42, 170), new Vector2(51, 160),
        new Vector2(50, 147), new Vector2(50, 141), new Vector2(45, 132), new Vector2(45, 118),
        new Vector2(50, 112), new Vector2(46, 108), new Vector2(53, 101), new Vector2(56, 91),
        new Vector2(48, 87), new Vector2(50, 78), new Vector2(62, 73), new Vector2(74, 77),
        new Vector2(78, 68),
    };
    private static readonly int[] PaperTriangles = {
        44, 0, 1, 44, 1, 2, 44, 2, 3, 3, 4, 5, 3, 5, 6, 44, 3, 6,
        6, 7, 8, 6, 8, 9, 9, 10, 11, 6, 9, 11, 44, 6, 11, 44, 11, 12,
        44, 12, 13, 44, 13, 14, 44, 14, 15, 44, 15, 16, 44, 16, 17, 44, 17, 18,
        44, 18, 19, 44, 19, 20, 44, 20, 21, 44, 21, 22, 44, 22, 23, 44, 23, 24,
        44, 24, 25, 44, 25, 26, 44, 26, 27, 44, 27, 28, 44, 28, 29, 29, 30, 31,
        44, 29, 31, 44, 31, 32, 44, 32, 33, 33, 34, 35, 33, 35, 36, 36, 37, 38,
        33, 36, 38, 33, 38, 39, 39, 40, 41, 39, 41, 42, 33, 39, 42, 33, 42, 43,
        33, 43, 44,
    };

    // 原图作为单文件资源内嵌；只渲染上面轮廓以内的像素。
    private const string PaperPng = @"
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
";
    #endregion
}
