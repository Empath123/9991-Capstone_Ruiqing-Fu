using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables; // XR Interaction Toolkit 3.x

// Attach to the scene instance of Mouse, NOT to Screen Open or to a mouse submesh.
[DisallowMultipleComponent, RequireComponent(typeof(XRSimpleInteractable))]
[DefaultExecutionOrder(100)]
public sealed class LaptopMouseXR : MonoBehaviour
{
    [Tooltip("Drag the laptop's Screen Open (Mesh Renderer) here.")]
    public MeshRenderer screenOpen;
    [Tooltip("Optional: require this texture to be on the screen before enabling the mouse.")]
    public Texture2D desktopImage;
    [Tooltip("Optional stationary mouse-pad axes: red X = right, blue Z = toward the laptop.")]
    public Transform padAxes;
    [Min(0.01f)] public float travelWidth = 0.14f;
    [Min(0.01f)] public float travelDepth = 0.09f;
    [Range(0.02f, 0.10f)] public float cursorHeight = 0.052f;
    public Vector2 CursorUV { get; private set; } = new Vector2(.5f, .5f);

    XRSimpleInteractable grip;
    Transform hand;
    Vector3 lastHand, initialMouse, uvOrigin, uvRight, uvUp;
    Vector2 pad, tipMin, tipMax;
    GameObject arrow;
    Mesh arrowMesh;
    Material ink, paper;
    bool initialized;
    static readonly int Map = Shader.PropertyToID("_BaseMap");
    static readonly int Tint = Shader.PropertyToID("_BaseColor");

    // Pixel silhouette. Each uninterrupted strip is a quad; only the arrow is drawn.
    static readonly string[] Shape = {
        "#", "##", "###", "####", "#####", "######", "#######", "########",
        "#########", "##########", "###########", "############", "#############",
        "########", "####.####", "###..####", "##....####", "#.....####",
        ".......####", ".......###", "........#"
    };

    void Awake()
    {
        grip = GetComponent<XRSimpleInteractable>();
        if (!screenOpen) { Fail("把 Hierarchy 中的 Screen Open 拖入 Screen Open 栏。"); return; }
        if (GetComponent<XRGrabInteractable>())
        { Fail("Mouse 上已有 XR Grab Interactable。此脚本使用 XR Simple Interactable，不能同时让两个组件控制鼠标。"); return; }
        if (padAxes && padAxes.IsChildOf(transform))
        { Fail("Pad Axes 应是固定在桌面上的物体，不是 Mouse 本身或它的子物体。"); return; }
        initialMouse = transform.position;
        // The supplied model has non-convex MeshColliders, not convex volumes.
        grip.distanceCalculationMode=XRBaseInteractable.DistanceCalculationMode.ColliderPosition;
    }

    void OnEnable() { if (grip) grip.selectExited.AddListener(Released); }
    void Released(SelectExitEventArgs _) { hand=null; }

    void LateUpdate()
    {
        // The old lid controller remains the authority for screen power.
        var material = screenOpen ? screenOpen.sharedMaterial : null;
        bool on = screenOpen && screenOpen.enabled && screenOpen.gameObject.activeInHierarchy
            && !screenOpen.forceRenderingOff && material && material.HasProperty(Map)
            && (!desktopImage || material.GetTexture(Map) == desktopImage);
        if (!on) { if (arrow && arrow.activeSelf) arrow.SetActive(false); hand = null; return; }
        if (!initialized && !BuildArrow(material)) return;
        if (!arrow.activeSelf) arrow.SetActive(true);

        var who = grip.isActiveAndEnabled && grip.interactorsSelecting.Count != 0
            ? grip.interactorsSelecting[0].transform : null;
        if (!who) { hand = null; return; }
        if (who != hand) { hand = who; lastHand = who.position; return; } // no grab snap
        Vector3 delta = who.position - lastHand;
        lastHand = who.position;
        Vector3 normal = padAxes ? padAxes.up : Vector3.up;
        Vector3 right = Vector3.ProjectOnPlane(padAxes ? padAxes.right :
            screenOpen.transform.TransformVector(uvRight), normal).normalized;
        if (right.sqrMagnitude < .5f) return;
        Vector3 forward = Vector3.Cross(right, normal).normalized;
        float w = Mathf.Max(.01f, travelWidth), h = Mathf.Max(.01f, travelDepth);
        pad.x = Mathf.Clamp(pad.x + Vector3.Dot(delta, right), -w*.5f, w*.5f);
        pad.y = Mathf.Clamp(pad.y + Vector3.Dot(delta, forward), -h*.5f, h*.5f);
        transform.position = initialMouse + right*pad.x + forward*pad.y;
        CursorUV = new Vector2(Mathf.Lerp(tipMin.x, tipMax.x, .5f+pad.x/w),
                               Mathf.Lerp(tipMin.y, tipMax.y, .5f+pad.y/h));
        PlaceArrow();
    }

    bool BuildArrow(Material source)
    {
        var mf = screenOpen.GetComponent<MeshFilter>();
        var mesh = mf ? mf.sharedMesh : null;
        if (!mesh || !mesh.isReadable) return Fail("Screen Open 需要可读取的桌面网格。请先用 US519ScreenUI 点击「应用到 Screen Open」。");
        Vector3[] v = mesh.vertices; Vector2[] uv = mesh.uv; int[] tr = mesh.triangles;
        if (uv.Length != v.Length || uv.Length == 0) return Fail("屏幕没有桌面 UV。请先应用电脑 UI。");
        // Recover the local image plane from three well-separated UVs. No assumption
        // about the FBX pivot, the axis of the lid, or the size of the laptop.
        float best = 0;
        for (int i = 0; i < tr.Length; i += 3)
        {
            int a=tr[i], b=tr[i+1], c=tr[i+2];
            Vector2 s=uv[b]-uv[a], t=uv[c]-uv[a];
            float d=s.x*t.y-s.y*t.x;
            if (Mathf.Abs(d) <= best) continue;
            best=Mathf.Abs(d);
            uvRight=((v[b]-v[a])*t.y-(v[c]-v[a])*s.y)/d;
            uvUp=((v[c]-v[a])*s.x-(v[b]-v[a])*t.x)/d;
            uvOrigin=v[a]-uvRight*uv[a].x-uvUp*uv[a].y;
        }
        if (best < 1e-8f) return Fail("屏幕 UV 无效。请选择已显示桌面图的 Screen Open。");
        Transform panel=screenOpen.transform;
        var u=panel.TransformVector(uvRight); var up=panel.TransformVector(uvUp);
        var front=Vector3.Cross(up.normalized,u.normalized).normalized;
        if (front.sqrMagnitude < .5f) return Fail("屏幕坐标轴无效，请检查缩放。");
        float depth=0;
        foreach (var p in v) depth=Mathf.Max(depth,Vector3.Dot(panel.TransformVector(p-uvOrigin),front));
        uvOrigin+=panel.InverseTransformVector(front*(depth+Mathf.Max(.00015f,up.magnitude*.0008f)));

        float dy=Mathf.Clamp(cursorHeight,.02f,.1f)/Shape.Length, dx=dy*up.magnitude/u.magnitude;
        // Keep the complete arrow inside the US519 display, including its rounded corners.
        const float margin=.035f;
        tipMin=new Vector2(margin,margin+Shape.Length*dy);
        tipMax=new Vector2(1f-margin-13*dx,1f-margin);
        CursorUV=(tipMin+tipMax)*.5f;
        var points=new List<Vector3>(); var black=new List<int>(); var white=new List<int>();
        bool Filled(int x,int y) => y>=0 && y<Shape.Length && x>=0 && x<Shape[y].Length && Shape[y][x]=='#';
        bool Inside(int x,int y) => Filled(x-1,y)&&Filled(x+1,y)&&Filled(x,y-1)&&Filled(x,y+1);
        Vector3 P(int x,int y) => uvRight*(x*dx)-uvUp*(y*dy);
        for (int y=0;y<Shape.Length;y++)
        for (int x=0;x<Shape[y].Length;)
        {
            if (!Filled(x,y)) { x++; continue; }
            int start=x++; bool inner=Inside(start,y);
            while (Filled(x,y)&&Inside(x,y)==inner) x++;
            int k=points.Count;
            points.Add(P(start,y)); points.Add(P(x,y)); points.Add(P(x,y+1)); points.Add(P(start,y+1));
            (inner?white:black).AddRange(new[]{k,k+1,k+2,k,k+2,k+3});
        }
        arrowMesh=new Mesh {name="Laptop cursor"}; arrowMesh.SetVertices(points);
        arrowMesh.subMeshCount=2; arrowMesh.SetTriangles(black,0); arrowMesh.SetTriangles(white,1);
        arrowMesh.RecalculateNormals(); arrowMesh.RecalculateBounds();
        ink=Solid(source,Color.black); paper=Solid(source,Color.white);
        arrow=new GameObject("Mouse Cursor (Runtime)"); arrow.layer=screenOpen.gameObject.layer;
        arrow.transform.SetParent(panel,false);
        arrow.AddComponent<MeshFilter>().sharedMesh=arrowMesh;
        var r=arrow.AddComponent<MeshRenderer>(); r.sharedMaterials=new[]{ink,paper};
        r.shadowCastingMode=ShadowCastingMode.Off; r.receiveShadows=false;
        initialized=true; PlaceArrow(); return true;
    }

    static Material Solid(Material source,Color color)
    {
        var m=new Material(source);
        m.SetTexture(Map,Texture2D.whiteTexture); m.SetColor(Tint,color);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull",0);
        return m;
    }
    void PlaceArrow() => arrow.transform.localPosition=uvOrigin+uvRight*CursorUV.x+uvUp*CursorUV.y;
    bool Fail(string text) { Debug.LogError("[LaptopMouseXR] "+text,this); enabled=false; return false; }
    void OnDisable()
    {
        if (grip) grip.selectExited.RemoveListener(Released);
        hand=null; if (arrow) arrow.SetActive(false);
    }
    void OnDestroy()
    {
        if (arrow) Destroy(arrow);
        if (arrowMesh) Destroy(arrowMesh);
        if (ink) Destroy(ink);
        if (paper) Destroy(paper);
    }
}
