// Run only in a disposable Unity project. This file is not shipped in the package.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

public static class VerifyFxViewer
{
    [Serializable] private sealed class Report
    {
        public List<string> passed = new List<string>();
        public List<string> failed = new List<string>();
    }
    private static readonly Report report = new Report();
    private static readonly List<Object> objects = new List<Object>();
    private static EditorWindow window;
    private static Type[] targets;
    private static int targetIndex, stage;
    private static double next;
    private static string folder, prefix;
    private static VRCAvatarDescriptor avatar;
    private static SkinnedMeshRenderer body;
    private static Material material, replacement;
    private static AnimationClip eye, mouth, swap;
    private static AnimatorController controller;
    private static Dictionary<string, string> hashes;
    private static AnimatorOverrideController overrides;
    private static GameObject oldClone;
    private static T Keep<T>(T value) where T : Object { objects.Add(value); return value; }
    private static void Check(bool result, string name) { (result ? report.passed : report.failed).Add(prefix + name); }
    private static void Call(string name, params object[] args) { window.GetType().GetMethod(name).Invoke(window, args); }
    private static T Read<T>(string name) { return (T)window.GetType().GetProperty(name).GetValue(window); }
    private static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }

    public static void RunBatch()
    {
        try
        {
            targets = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                .Where(t => t.FullName == "Panpaca.PPAE.FxViewerWindow" || t.FullName == "PPTAvatarEditor.FxViewerWindow").ToArray();
            if (targets.Length == 0) throw new Exception("No FX Viewer assembly found.");
            folder = AssetDatabase.GenerateUniqueAssetPath("Assets/FxViewerVerification");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("FX Viewer fixture");
            root.AddComponent<Animator>();
            avatar = root.AddComponent<VRCAvatarDescriptor>();
            var child = new GameObject("Body"); child.transform.SetParent(root.transform);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            foreach (string name in new[] { "eye", "mouth" })
                mesh.AddBlendShapeFrame(name, 100, new[] { Vector3.zero, Vector3.zero, Vector3.forward * .1f }, new Vector3[3], new Vector3[3]);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, folder + "/Body.asset");
            body = child.AddComponent<SkinnedMeshRenderer>(); body.sharedMesh = mesh;
            material = new Material(Shader.Find("Standard")) { color = new Color(.2f, .4f, .6f) };
            replacement = new Material(Shader.Find("Standard")) { color = Color.yellow };
            AssetDatabase.CreateAsset(material, folder + "/Original.mat"); AssetDatabase.CreateAsset(replacement, folder + "/Replacement.mat");
            body.sharedMaterial = material; body.SetBlendShapeWeight(0, 7); body.SetBlendShapeWeight(1, 11);
            new GameObject("Accessory").transform.SetParent(root.transform);
            avatar.VisemeSkinnedMesh = body; avatar.ViewPosition = new Vector3(0, .6f, 0);
            eye = new AnimationClip { name = "Eye animation" };
            eye.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eye", AnimationCurve.Linear(0, 10, 1, 90));
            mouth = new AnimationClip { name = "Mouth animation" };
            mouth.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth", AnimationCurve.Constant(0, 1, 60));
            mouth.SetCurve("Accessory", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 0));
            mouth.SetCurve("Body", typeof(SkinnedMeshRenderer), "material._Color.r", AnimationCurve.Constant(0, 1, .9f));
            swap = new AnimationClip { name = "Material animation" };
            AnimationUtility.SetObjectReferenceCurve(swap,
                EditorCurveBinding.PPtrCurve("Body", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]"),
                new[] { new ObjectReferenceKeyframe { time = 0, value = replacement } });
            AssetDatabase.CreateAsset(eye, folder + "/Eye.anim"); AssetDatabase.CreateAsset(mouth, folder + "/Mouth.anim");
            AssetDatabase.CreateAsset(swap, folder + "/Swap.anim");
            controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            var firstLayer = controller.layers[0]; firstLayer.name = "Face";
            controller.layers = new[] { firstLayer };
            firstLayer.stateMachine.AddState("Eye").motion = eye;
            firstLayer.stateMachine.AddState("Mouth").motion = mouth;
            var nested = firstLayer.stateMachine.AddStateMachine("Nested");
            var tree = new BlendTree { name = "Expressions" }; AssetDatabase.AddObjectToAsset(tree, controller);
            tree.children = new[] { new ChildMotion { motion = swap } };
            nested.AddState("Blend").motion = tree;
            controller.AddLayer("Other"); controller.layers[1].stateMachine.AddState("Empty");
            avatar.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer
                { type = VRCAvatarDescriptor.AnimLayerType.FX, animatorController = controller } };
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, folder + "/Fixture.unity");
            hashes = Directory.GetFiles(folder).Where(f => !f.EndsWith(".meta")).ToDictionary(f => f, Hash);
            BeginTarget();
            EditorApplication.update += Step;
        }
        catch (Exception error) { report.failed.Add(error.ToString()); Finish(); }
    }

    private static void BeginTarget()
    {
        prefix = targets[targetIndex].Namespace + ": ";
        window = (EditorWindow)ScriptableObject.CreateInstance(targets[targetIndex]);
        window.position = new Rect(20, 50, 1100, 720);
        Call("SelectAvatar", avatar); window.ShowUtility();
        stage = 0; next = EditorApplication.timeSinceStartup + 1;
    }

    private static void Step()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            window.SendEvent(new Event { type = EventType.Layout });
            var left = Read<Rect>("PreviewPanelRect"); var right = Read<Rect>("FxPanelRect");
            if (stage <= 1)
            {
                Check(left.width >= 260 && left.height >= 400 && right.width >= 330 && left.xMax < right.x,
                    "Preview left / FX right at " + window.position.size);
                if (stage == 0) { window.position = new Rect(20, 50, 780, 440); stage++; next += .5; return; }
                var button = Read<Rect>("FirstConfirmRect");
                window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = button.center });
                window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = button.center });
                Check(Read<AnimationClip>("SelectedClip") == eye, "Confirm button loads its row into this viewer");
                Call("SetPreviewTime", .5f);
                var clone = Read<GameObject>("PreviewRoot");
                var renderer = clone.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                Check(Mathf.Abs(renderer.GetBlendShapeWeight(0) - 50) < .01f, "Clip sampling evaluates the chosen time");
                Call("ConfirmClip", mouth); Call("SetPreviewTime", .5f);
                Check(renderer.GetBlendShapeWeight(0) == 7 && renderer.GetBlendShapeWeight(1) == 60,
                    "Switching clips restores unkeyed shapes to the avatar defaults");
                Check(!clone.transform.Find("Accessory").gameObject.activeSelf, "FX object activation is visible in the preview");
                Check(renderer.sharedMaterial != material && Mathf.Abs(material.color.r - .2f) < .01f,
                    "Animated materials are isolated from source assets");
                Call("ConfirmClip", swap); Call("SetPreviewTime", 0f);
                Check(renderer.sharedMaterial != replacement && renderer.sharedMaterial.color == replacement.color,
                    "Nested Blend Tree clips and material reference curves can be previewed");
                Check(clone.transform.Find("Accessory").gameObject.activeSelf && renderer.GetBlendShapeWeight(1) == 11,
                    "Switching clips restores active flags and sparse shape values");
                Call("ConfirmClip", eye);
                stage = 2; next = EditorApplication.timeSinceStartup + .2; return;
            }
            if (stage == 2)
            {
                Check(Read<float>("PreviewTime") > .05f, "Animated clips advance during playback");
                Call("SetPreviewTime", .35f); stage = 3; next = EditorApplication.timeSinceStartup + .2; return;
            }
            Check(Mathf.Abs(Read<float>("PreviewTime") - .35f) < .001f, "Scrubbing pauses playback");
            Check(body.GetBlendShapeWeight(0) == 7 && body.GetBlendShapeWeight(1) == 11
                && body.sharedMaterial == material && avatar.transform.Find("Accessory").gameObject.activeSelf,
                "Source avatar values, material references and activation are unchanged");
            Check(!avatar.gameObject.scene.isDirty, "The source scene remains clean");
            Check(hashes.All(h => Hash(h.Key) == h.Value), "Clips, materials and FX controller files are unchanged");

            overrides = Keep(new AnimatorOverrideController(controller));
            overrides[eye] = mouth;
            var layers = avatar.baseAnimationLayers;
            layers[0].animatorController = overrides; avatar.baseAnimationLayers = layers;
            oldClone = Read<GameObject>("PreviewRoot");
            Call("SelectAvatar", avatar); Call("ConfirmClip", mouth); Call("SetPreviewTime", 0f);
            Check(oldClone == null && Read<GameObject>("PreviewRoot") != null, "Reload releases the previous preview clone");
            Check(Read<AnimationClip>("SelectedClip") == mouth, "Animator override clips are available for confirmation");
            layers[0].animatorController = controller; avatar.baseAnimationLayers = layers;

            oldClone = Read<GameObject>("PreviewRoot"); Call("SelectAvatar", new object[] { null });
            Check(oldClone == null && Read<GameObject>("PreviewRoot") == null, "Clearing the avatar disposes the preview");
            Call("SelectAvatar", avatar); oldClone = Read<GameObject>("PreviewRoot");
            window.Close(); window = null;
            Check(oldClone == null, "Closing the viewer disposes its preview clone");
            var menu = targets[targetIndex].GetMethod("Open").GetCustomAttributesData().Single(a => a.AttributeType == typeof(MenuItem));
            Check(((string)menu.ConstructorArguments[0].Value).EndsWith("FX Viewer"), "The viewer has its own menu entry");
            targetIndex++;
            if (targetIndex < targets.Length) BeginTarget(); else Finish();
        }
        catch (Exception error) { report.failed.Add(prefix + error); Finish(); }
    }

    private static void Finish()
    {
        EditorApplication.update -= Step;
        if (window != null) window.Close();
        foreach (var obj in objects) if (obj != null) Object.DestroyImmediate(obj);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        string output = Path.GetFullPath("../fx-viewer-results");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "verification.json"), JsonUtility.ToJson(report, true));
        Debug.Log("FX Viewer verification: " + report.passed.Count + " passed, " + report.failed.Count + " failed.");
        EditorApplication.Exit(report.failed.Count == 0 ? 0 : 1);
    }
}
