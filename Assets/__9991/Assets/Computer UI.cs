using UnityEngine;
#if UNITY_EDITOR
using System;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[DisallowMultipleComponent]
public sealed class US519ScreenUI : MonoBehaviour
{
    public Texture2D computerUI;

#if UNITY_EDITOR
    internal void ApplyDesktop()
    {
        var dst = GetComponent<MeshFilter>();
        var display = GetComponent<MeshRenderer>();

        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            !gameObject.scene.IsValid() || EditorUtility.IsPersistent(gameObject) ||
            !name.Replace(" ", "").Equals("ScreenOpen", StringComparison.OrdinalIgnoreCase) ||
            !dst || !display)
            throw new Exception("请退出 Play 模式，把脚本挂在 Hierarchy 中的 Screen Open 上。");

        if (!computerUI || !AssetDatabase.Contains(computerUI))
            throw new Exception("请把 Project 中的 Computer UI.png 拖进 Computer UI 栏。");

        // 取原 FBX 的完整屏幕，不使用以前生成的缺角覆盖层。
        string fbx = AssetDatabase.GUIDToAssetPath("ae2ca6ce863038548985da252de9c49f");
        if (string.IsNullOrEmpty(fbx) && dst.sharedMesh)
            fbx = AssetDatabase.GetAssetPath(dst.sharedMesh);

        if (string.IsNullOrEmpty(fbx) || !fbx.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
        {
            var ids = AssetDatabase.FindAssets("US519 t:Model", new[] { "Assets" });
            fbx = ids.Length == 1 ? AssetDatabase.GUIDToAssetPath(ids[0]) : "";
        }

        var model = string.IsNullOrEmpty(fbx) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (!model) throw new Exception("找不到原始 US519-FBX。请保留原笔记本模型。");

        var parts = model.GetComponentsInChildren<MeshFilter>(true);
        MeshFilter Part(string id) => parts.FirstOrDefault(p => p.sharedMesh &&
            (p.name == "MacBook_Pro_14_2021_" + id ||
             p.sharedMesh.name == "MacBook_Pro_14_2021_" + id));

        var panel = Part("005");
        var top = Part("003");
        var keyboard = Part("013");
        if (!panel || !top || !keyboard)
            throw new Exception("当前 FBX 不是这份 US519 原模型，未修改屏幕。");

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (!shader || !shader.isSupported)
            throw new Exception("找不到可用的 URP/Unlit 着色器。");

        const string dir = "Assets/ScreenOpenGenerated";
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets", "ScreenOpenGenerated");

        string key = Hash128.Compute("US519-v1/" + AssetDatabase.GetAssetDependencyHash(fbx) + "/" +
            AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(computerUI))).ToString();
        string path = dir + "/Desktop_" + key + ".asset";

        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        Material mat;
        if (!mesh)
        {
            mesh = MakeScreen(panel, top, keyboard);
            mat = new Material(shader) { name = "Computer UI" };
            mat.SetTexture("_BaseMap", computerUI);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Cull", 0f);

            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.AddObjectToAsset(mat, mesh);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
        }
        else
        {
            mat = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().FirstOrDefault();
            if (!mat) throw new Exception("生成的屏幕资源缺少材质：" + path);
        }

        Undo.RecordObjects(new UnityEngine.Object[] { dst, display }, "Apply Screen Open UI");
        dst.sharedMesh = mesh;
        display.sharedMaterials = new[] { mat };
        SaveOverride(dst);
        SaveOverride(display);

        // 只隐藏旧版桌面覆盖层，不删除子物体，也不改变机盖开关状态。
        foreach (Transform child in transform)
        {
            if (child.name != "Desktop UI" && child.name != "Desktop UI Full") continue;
            var old = child.GetComponent<MeshRenderer>();
            if (!old || !old.enabled) continue;
            Undo.RecordObject(old, "Hide old screen overlay");
            old.enabled = false;
            SaveOverride(old);
        }

        EditorSceneManager.MarkSceneDirty(gameObject.scene);
        SceneView.RepaintAll();
    }

    static void SaveOverride(UnityEngine.Object obj)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(obj))
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
    }

    static Mesh MakeScreen(MeshFilter panel, MeshFilter top, MeshFilter keyboard)
    {
        Mesh src = panel.sharedMesh;
        Vector3[] vertices;
        int[] triangles;

        using (var snapshot = MeshUtility.AcquireReadOnlyMeshData(src))
        {
            var data = snapshot[0];
            if (data.subMeshCount != 1 || data.GetSubMesh(0).topology != MeshTopology.Triangles)
                throw new Exception("原屏幕不是预期的单个三角网格。");

            using (var v = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
            using (var t = new NativeArray<int>(data.GetSubMesh(0).indexCount, Allocator.Temp))
            {
                data.GetVertices(v);
                data.GetIndices(t, 0);
                vertices = v.ToArray();
                triangles = t.ToArray();
            }
        }

        Vector3 Center(MeshFilter f) => f.transform.TransformPoint(f.sharedMesh.bounds.center);
        var center = Center(panel);
        var p = vertices.Select(v => panel.transform.TransformPoint(v) - center).ToArray();

        // 最大面仅用于判断屏幕朝向；下面仍然保留全部三角面。
        Vector3 front = Vector3.zero;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var a = p[triangles[i]];
            var n = Vector3.Cross(p[triangles[i + 1]] - a, p[triangles[i + 2]] - a);
            if (n.sqrMagnitude > front.sqrMagnitude) front = n;
        }

        if (front.sqrMagnitude == 0) throw new Exception("屏幕没有有效三角面。");
        front /= front.magnitude;
        if (Vector3.Dot(front, Center(keyboard) - center) < 0) front = -front;

        var up = Vector3.ProjectOnPlane(Center(top) - center, front).normalized;
        var right = Vector3.Cross(front, up).normalized;
        var uv = p.Select(v => new Vector2(Vector3.Dot(v, right), Vector3.Dot(v, up))).ToArray();

        float l = uv.Min(v => v.x), r = uv.Max(v => v.x);
        float b = uv.Min(v => v.y), h = uv.Max(v => v.y);
        if (r <= l || h <= b) throw new Exception("无法识别屏幕的宽或高。");

        for (int i = 0; i < uv.Length; i++)
            uv[i] = new Vector2(Mathf.InverseLerp(l, r, uv[i].x),
                                Mathf.InverseLerp(b, h, uv[i].y));

        var result = new Mesh { name = "Screen Open UI", indexFormat = src.indexFormat };
        result.vertices = vertices;
        result.triangles = triangles;
        result.uv = uv;
        result.RecalculateNormals();
        result.bounds = src.bounds;
        return result;
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(US519ScreenUI))]
public sealed class US519ScreenUIInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (!GUILayout.Button("应用到 Screen Open")) return;

        try { ((US519ScreenUI)target).ApplyDesktop(); }
        catch (Exception e) { EditorUtility.DisplayDialog("Screen Open", e.Message, "OK"); }
    }
}
#endif