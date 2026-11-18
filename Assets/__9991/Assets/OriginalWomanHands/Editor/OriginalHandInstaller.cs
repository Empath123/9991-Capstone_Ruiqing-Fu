using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using OriginalWomanHands;

namespace OriginalWomanHandsEditor
{
    public static class OriginalHandInstaller
    {
        private static string Root => System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath("c268d6ea77275cf0844441f4284f5ed8"))).Replace((char)92, '/');

        [MenuItem("Tools/Original Woman Hands/Install On Selected XR Origin")]
        private static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Stop Play mode before installing hands.");
                return;
            }
            var selection = Selection.activeGameObject;
            if (selection == null)
            {
                Debug.LogError("Select XR Origin (XR Rig) in the Hierarchy first.");
                return;
            }
            var all = selection.GetComponentsInChildren<Transform>(true);
            var left = all.FirstOrDefault(t => t.name == "Left Controller");
            var right = all.FirstOrDefault(t => t.name == "Right Controller");
            if (left == null || right == null)
            {
                Debug.LogError("Select the XR Origin containing Left Controller and Right Controller. Neither hand was changed.");
                return;
            }
            // Validate BOTH imported assets before changing either scene hand.
            var lp = Load("Left"); var rp = Load("Right");
            if (lp.model == null || rp.model == null || lp.clip == null || rp.clip == null)
            {
                Debug.LogError("FBX or Close clip is missing. Wait for import to finish. Keep the Models folder beside Editor.");
                return;
            }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install original animated hands");
            try
            {
                Make(left, lp.model, lp.clip, OriginalHandGripPlayer.HandSide.Left);
                Make(right, rp.model, rp.clip, OriginalHandGripPlayer.HandSide.Right);
                Undo.CollapseUndoOperations(group);
                Debug.Log("Original animated hands installed. Old hand visuals were disabled and kept as backups. Save the scene, then test Grip. Ctrl+Z undoes installation.");
            }
            catch (Exception ex)
            {
                Undo.RevertAllDownToGroup(group);
                Debug.LogException(ex);
            }
        }

        private static (GameObject model, AnimationClip clip) Load(string side)
        {
            string path = Root + "/Models/Woman_" + side + "_Original_Grab.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>();
            var clip = clips.FirstOrDefault(c => c.name == "Close")
                ?? clips.FirstOrDefault(c => !c.name.StartsWith("__preview__") && c.name.Contains("Close"));
            return (model, clip);
        }

        private static Material FallbackMaterial()
        {
            string path = Root + "/PlainSkin.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No supported skin shader found.");
            material = new Material(shader) { name = "Plain Skin (replace with your original material)" };
            material.color = new Color(0.73f, 0.49f, 0.40f, 1f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.3f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Transform Bone(Transform model, string name)
        {
            return model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        }

        private static bool IsHandVisual(Transform child)
        {
            return child.GetComponentsInChildren<Transform>(true).Any(t =>
                t.name == "index_01_l" || t.name == "index_01_r");
        }

        private static void Make(Transform controller, GameObject asset, AnimationClip clip,
            OriginalHandGripPlayer.HandSide side)
        {
            var previous = new List<Transform>();
            foreach (Transform child in controller)
                if (IsHandVisual(child)) previous.Add(child);
            // Prefer the active original/repaired model's material; no texture
            // files were supplied with the FBXs, so reuse the project's material.
            Material[] materials = null;
            foreach (var old in previous.OrderByDescending(t => t.gameObject.activeSelf))
            {
                var skin = old.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (skin != null && skin.sharedMaterials.Any(m => m != null))
                { materials = skin.sharedMaterials; break; }
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, controller);
            Undo.RegisterCreatedObjectUndo(go, "Create animated original hand");
            go.name = side + " Hand Original Animated";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            string suffix = side == OriginalHandGripPlayer.HandSide.Left ? "_l" : "_r";
            var index = Bone(go.transform, "index_01" + suffix);
            var pinky = Bone(go.transform, "pinky_01" + suffix);
            var middle = Bone(go.transform, "middle_01" + suffix);
            var middle2 = Bone(go.transform, "middle_02" + suffix);
            var wrist = Bone(go.transform, "hand" + suffix);
            if (index == null || pinky == null || middle == null || middle2 == null || wrist == null)
                throw new InvalidOperationException("The imported hand hierarchy is incomplete; installation was undone.");
            Vector3 forward = (middle2.position - middle.position).normalized;
            Vector3 across = (index.position - pinky.position).normalized;
            Vector3 palm = Vector3.Cross(forward, across).normalized;
            if (side == OriginalHandGripPlayer.HandSide.Left) palm = -palm;
            // Align each hand anatomically, independently of the source FBX axes:
            // fingers forward (+Z), back of hand up (+Y), palm centered on grip.
            Quaternion frame = Quaternion.LookRotation(forward, -palm);
            go.transform.rotation = controller.rotation * Quaternion.Inverse(frame) * go.transform.rotation;
            Vector3 palmCenter = Vector3.Lerp(wrist.position, middle.position, 0.5f);
            go.transform.position += controller.position - palmCenter;
            foreach (var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (materials != null && materials.Length == skin.sharedMaterials.Length)
                    skin.sharedMaterials = materials;
                else
                    skin.sharedMaterials = Enumerable.Repeat(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("a26cac00a732ad547881ffa1bbb8de49")) ?? FallbackMaterial(), skin.sharedMaterials.Length).ToArray();
                // Preserve all four original influences on mobile quality levels.
                skin.quality = SkinQuality.Bone4;
                var bounds = skin.localBounds;
                bounds.Expand(bounds.size.magnitude * 0.5f);
                skin.localBounds = bounds;
            }
            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = Undo.AddComponent<Animator>(go);
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var player = Undo.AddComponent<OriginalHandGripPlayer>(go);
            player.hand = side;
            player.closeClip = clip;
            foreach (var old in previous)
            {
                Undo.RecordObject(old.gameObject, "Disable old hand visual");
                old.gameObject.SetActive(false);
            }
            var visualName = side + " Controller Visual";
            foreach (Transform child in controller)
            {
                if (child.name != visualName) continue;
                Undo.RecordObject(child.gameObject, "Hide controller visual");
                child.gameObject.SetActive(false);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            EditorUtility.SetDirty(go);
        }
    }

    [CustomEditor(typeof(OriginalHandGripPlayer)), CanEditMultipleObjects]
    public sealed class OriginalHandGripPlayerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("原模型动画：Left / Right 必须对应左右手。运行后可勾 Manual Preview，拖 Preview Grip 测试张开到握紧。测试完取消勾选。", MessageType.Info);
            if (Application.isPlaying && targets.Length == 1)
            {
                var player = (OriginalHandGripPlayer)target;
                EditorGUILayout.LabelField("Input Grip", player.InputGrip.ToString("F2"));
                EditorGUILayout.LabelField("Displayed Grip", player.DisplayedGrip.ToString("F2"));
                Repaint();
            }
        }
    }
}
