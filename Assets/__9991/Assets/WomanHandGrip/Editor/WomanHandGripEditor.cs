using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CustomVRHands.Editor
{
    [CustomEditor(typeof(WomanHandGrip))]
    [CanEditMultipleObjects]
    public sealed class WomanHandGripEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (targets.Length > 1)
            {
                EditorGUILayout.HelpBox("已多选。修复模型请用 Tools > VR Hands > Replace Selected With Repaired Models。逐只选择可预览手型。", MessageType.Info);
                return;
            }
            var hand = (WomanHandGrip)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("放在 Left/Right Hand Model 上。骨骼自动识别，Grip 自动绑定。Play 后勾选 Manual Preview 可单独测试手型。", MessageType.Info);
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Grip Input", hand.CurrentGrip.ToString("0.00"));
                EditorGUILayout.LabelField("Finger Curl", hand.CurrentCurl.ToString("0.00"));
                Repaint();
            }
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Configure / 识别骨骼并设置握拳")) Configure(hand);
                if (GUILayout.Button("Export .anim Clips / 导出动画片段")) Export(hand);
            }
            if (hand.IsConfigured) EditorGUILayout.HelpBox("已配置 15 根手指骨骼。", MessageType.None);
        }

        [MenuItem("Tools/VR Hands/Set Up Selected Hand Models")]
        private static void SetUpSelected()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("VR Hands", "请先停止 Play，再配置并保存场景。", "OK");
                return;
            }
            int count = 0;
            foreach (var root in Selection.gameObjects)
            {
                if (EditorUtility.IsPersistent(root)) continue;
                bool left = HasBone(root, "index_01_l");
                bool right = HasBone(root, "index_01_r");
                if (left == right)
                {
                    Debug.LogWarning("请只选中一个手模型（Left Hand Model 或 Right Hand Model），不要选整个 XR Origin。", root);
                    continue;
                }
                var driver = root.GetComponent<WomanHandGrip>();
                if (driver == null) driver = Undo.AddComponent<WomanHandGrip>(root);
                Undo.RecordObject(driver, "Set up VR hand");
                driver.hand = left ? WomanHandGrip.HandSide.Left : WomanHandGrip.HandSide.Right;
                if (Configure(driver)) ++count;
            }
            EditorUtility.DisplayDialog("VR Hands", count > 0
                ? "已配置 " + count + " 只手。保存场景后 Play 测试。"
                : "请在 Hierarchy 选中 Left Hand Model 和 Right Hand Model，然后再次运行此菜单。", "OK");
        }

        [MenuItem("Tools/VR Hands/Replace Selected With Repaired Models")]
        private static void ReplaceSelected()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("VR Hands", "请先停止 Play。", "OK");
                return;
            }
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Replace hand visuals with repaired meshes");
            var replacements = new List<GameObject>();
            foreach (var old in Selection.gameObjects)
            {
                if (EditorUtility.IsPersistent(old)) continue;
                bool left = HasBone(old, "index_01_l");
                bool right = HasBone(old, "index_01_r");
                if (left == right) continue;
                string assetName = "WomanHand_" + (left ? "Left" : "Right") + "_Repaired";
                string[] guids = AssetDatabase.FindAssets(assetName + " t:Model");
                string path = null;
                foreach (string guid in guids)
                {
                    string candidate = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(candidate) == assetName) { path = candidate; break; }
                }
                var asset = path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) { Debug.LogError("Missing repaired FBX: " + assetName); continue; }
                var fresh = (GameObject)PrefabUtility.InstantiatePrefab(asset, old.transform.parent);
                if (fresh == null) continue;
                Undo.RegisterCreatedObjectUndo(fresh, "Create repaired hand");
                fresh.name = left ? "Left Hand Model Repaired" : "Right Hand Model Repaired";
                fresh.layer = old.layer;
                fresh.transform.localPosition = old.transform.localPosition;
                fresh.transform.localRotation = old.transform.localRotation;
                fresh.transform.localScale = old.transform.localScale;
                fresh.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
                var oldSkin = old.GetComponentInChildren<SkinnedMeshRenderer>(true);
                foreach (var skin in fresh.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Undo.RecordObject(skin, "Transfer hand materials");
                    if (oldSkin != null) skin.sharedMaterials = oldSkin.sharedMaterials;
                    skin.quality = SkinQuality.Bone4;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
                }
                var driver = Undo.AddComponent<WomanHandGrip>(fresh);
                driver.hand = left ? WomanHandGrip.HandSide.Left : WomanHandGrip.HandSide.Right;
                var oldDriver = old.GetComponent<WomanHandGrip>();
                if (oldDriver != null)
                {
                    driver.maximumCurl = oldDriver.maximumCurl;
                    driver.responseSpeed = oldDriver.responseSpeed;
                    driver.editorKeyboardFallback = oldDriver.editorKeyboardFallback;
#if ENABLE_INPUT_SYSTEM
                    driver.gripAction = oldDriver.gripAction;
#endif
                }
                if (!Configure(driver)) { Undo.DestroyObjectImmediate(fresh); continue; }
                PrefabUtility.RecordPrefabInstancePropertyModifications(fresh.transform);
                Undo.RecordObject(old, "Keep original hand as inactive backup");
                old.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(old);
                EditorSceneManager.MarkSceneDirty(fresh.scene);
                replacements.Add(fresh);
            }
            Undo.CollapseUndoOperations(group);
            if (replacements.Count > 0) Selection.objects = replacements.ToArray();
            EditorUtility.DisplayDialog("VR Hands", replacements.Count > 0
                ? "已替换 " + replacements.Count + " 只手的显示模型。旧模型保留并停用。保存场景，逐只选择新模型测试 Manual Preview。"
                : "请在 Hierarchy 选择两只手模型本身，不要选择 Controller 或 XR Origin。", "OK");
        }

        private static bool HasBone(GameObject root, string target)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                int colon = n.LastIndexOf(':');
                if (colon >= 0) n = n.Substring(colon + 1);
                if (string.Equals(n, target, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool Configure(WomanHandGrip hand)
        {
            Undo.RecordObject(hand, "Configure hand pose");
            Undo.RecordObjects(hand.GetComponentsInChildren<Transform>(true), "Restore open hand");
            if (!hand.ConfigureFromOpenPose()) return false;
            EditorUtility.SetDirty(hand);
            PrefabUtility.RecordPrefabInstancePropertyModifications(hand);
            if (hand.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(hand.gameObject.scene);
            return true;
        }

        private static void Export(WomanHandGrip hand)
        {
            if (!hand.IsConfigured && !Configure(hand)) return;
            const string parent = "Assets/WomanHandGrip";
            if (!AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder("Assets", "WomanHandGrip");
            string folder = AssetDatabase.GenerateUniqueAssetPath(parent + "/" + hand.hand + "_Animations");
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            MakeClip(hand, folder, "Open", 0f, 0f, 1f / 30f);
            MakeClip(hand, folder, "Grip", hand.maximumCurl, hand.maximumCurl, 1f / 30f);
            MakeClip(hand, folder, "Close", 0f, hand.maximumCurl, 0.25f);
            MakeClip(hand, folder, "Release", hand.maximumCurl, 0f, 0.25f);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folder));
            Debug.Log("手部动画已导出到 " + folder + "。路径相对于当前 Hand Model；程序握拳不需要额外 Animator Controller。", hand);
        }

        private static void MakeClip(WomanHandGrip hand, string folder, string name, float from, float to, float duration)
        {
            var clip = new AnimationClip { name = hand.hand + "_" + name, frameRate = 60f, legacy = false };
            int samples = Mathf.Max(2, Mathf.CeilToInt(duration * 60f) + 1);
            foreach (var pose in hand.Poses)
            {
                if (pose.bone == null) continue;
                var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                for (int i = 0; i < samples; ++i)
                {
                    float t = i / (float)(samples - 1);
                    float smooth = t * t * (3f - 2f * t);
                    Quaternion q = Quaternion.Slerp(pose.open, pose.closed, Mathf.Lerp(from, to, smooth));
                    float[] values = { q.x, q.y, q.z, q.w };
                    for (int c = 0; c < 4; ++c) curves[c].AddKey(t * duration, values[c]);
                }
                string path = AnimationUtility.CalculateTransformPath(pose.bone, hand.transform);
                string[] properties = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
                for (int c = 0; c < 4; ++c)
                {
                    for (int k = 0; k < curves[c].length; ++k)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curves[c], k, AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyRightTangentMode(curves[c], k, AnimationUtility.TangentMode.Linear);
                    }
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), properties[c]), curves[c]);
                }
            }
            clip.EnsureQuaternionContinuity();
            AssetDatabase.CreateAsset(clip, folder + "/" + clip.name + ".anim");
        }
    }
}
