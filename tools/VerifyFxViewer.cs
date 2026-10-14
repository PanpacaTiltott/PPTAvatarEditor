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
    private static AnimationClip eye, mouth, swap, newExpression;
    private static AnimatorController controller;
    private static Dictionary<string, string> hashes;
    private static AnimatorOverrideController overrides;
    private static AnimatorOverrideController savedOverrides;
    private static AnimatorState eyeState, emptyState;
    private static BlendTree blend;
    private static GameObject oldClone;
    private static T Keep<T>(T value) where T : Object { objects.Add(value); return value; }
    private static void Check(bool result, string name) { (result ? report.passed : report.failed).Add(prefix + name); }
    private static void Call(string name, params object[] args) { window.GetType().GetMethod(name).Invoke(window, args); }
    private static bool Replace(int index, AnimationClip clip)
    { return (bool)window.GetType().GetMethod("ReplaceClip").Invoke(window, new object[] { index, clip }); }
    private static AnimationClip ListedClip(int index)
    {
        var catalog = window.GetType().GetField("catalog", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
        var entries = (System.Collections.IList)catalog.GetType().GetField("Entries").GetValue(catalog);
        return (AnimationClip)entries[index].GetType().GetField("Clip").GetValue(entries[index]);
    }
    private static void UndoChange() { Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); }
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
            var animator = root.AddComponent<Animator>();
            animator.avatar = BuildHumanoid(root);
            animator.enabled = false;
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
            newExpression = new AnimationClip { name = "New expression" };
            newExpression.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth", AnimationCurve.Constant(0, 1, 55));
            newExpression.SetCurve("Accessory", typeof(Transform), "m_LocalScale.x", AnimationCurve.Constant(0, 1, 2));
            newExpression.SetCurve("Hips", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Constant(0, 1, .6f));
            AssetDatabase.CreateAsset(newExpression, folder + "/New.anim");
            controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            var firstLayer = controller.layers[0]; firstLayer.name = "Face";
            controller.layers = new[] { firstLayer };
            eyeState = firstLayer.stateMachine.AddState("Eye"); eyeState.motion = eye;
            firstLayer.stateMachine.AddState("Mouth").motion = mouth;
            var nested = firstLayer.stateMachine.AddStateMachine("Nested");
            blend = new BlendTree { name = "Expressions" }; AssetDatabase.AddObjectToAsset(blend, controller);
            blend.children = new[] { new ChildMotion { motion = swap, threshold = .25f, timeScale = .75f, mirror = true } };
            nested.AddState("Blend").motion = blend;
            controller.AddLayer("Other"); emptyState = controller.layers[1].stateMachine.AddState("Empty");
            controller.layers[1].stateMachine.AddState("Shared eye").motion = eye;
            savedOverrides = new AnimatorOverrideController(controller) { name = "Saved overrides" };
            savedOverrides[eye] = mouth;
            AssetDatabase.CreateAsset(savedOverrides, folder + "/FX.overrideController");
            avatar.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer
                { type = VRCAvatarDescriptor.AnimLayerType.FX, animatorController = controller } };
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, folder + "/Fixture.unity");
            hashes = Directory.GetFiles(folder).Where(f => !f.EndsWith(".meta")).ToDictionary(f => f.Replace('\\', '/'), Hash);
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
                CheckHumanoidPose("Confirm preserves the Humanoid hips and head position");
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
                CheckHumanoidPose("Switching FX clips preserves the Humanoid pose");
                Call("ConfirmClip", eye);
                stage = 2; next = EditorApplication.timeSinceStartup + .2; return;
            }
            if (stage == 2)
            {
                Check(Read<float>("PreviewTime") > .05f, "Animated clips advance during playback");
                Call("SetPreviewTime", .35f); stage = 3; next = EditorApplication.timeSinceStartup + .2; return;
            }
            Check(Mathf.Abs(Read<float>("PreviewTime") - .35f) < .001f, "Scrubbing pauses playback");
            CheckHumanoidPose("Playback and scrubbing preserve the Humanoid pose");
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
            Check(!Replace(0, newExpression), "Unsaved controllers cannot receive asset replacements");
            layers[0].animatorController = controller; avatar.baseAnimationLayers = layers;

            VerifyAssignments();

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

    private static Avatar BuildHumanoid(GameObject root)
    {
        var human = new List<HumanBone>();
        Transform Bone(HumanBodyBones id, Transform parent, Vector3 position)
        {
            var item = new GameObject(id.ToString()).transform;
            item.SetParent(parent, false); item.localPosition = position;
            human.Add(new HumanBone { boneName = item.name, humanName = HumanTrait.BoneName[(int)id],
                limit = new HumanLimit { useDefaultValues = true } });
            return item;
        }
        var hips = Bone(HumanBodyBones.Hips, root.transform, new Vector3(0, .48f, 0));
        var spine = Bone(HumanBodyBones.Spine, hips, new Vector3(0, .1f, 0));
        var chest = Bone(HumanBodyBones.Chest, spine, new Vector3(0, .06f, 0));
        var neck = Bone(HumanBodyBones.Neck, chest, new Vector3(0, .05f, 0));
        Bone(HumanBodyBones.Head, neck, new Vector3(0, .03f, 0));
        for (int side = 0; side < 2; side++)
        {
            float sign = side == 0 ? 1 : -1;
            var leg = Bone(side == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg, hips, new Vector3(sign * .04f, -.02f, 0));
            var knee = Bone(side == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, leg, new Vector3(0, -.22f, .01f));
            Bone(side == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot, knee, new Vector3(0, -.22f, .03f));
            var arm = Bone(side == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, chest, new Vector3(sign * .1f, .02f, 0));
            var elbow = Bone(side == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, arm, new Vector3(sign * .15f, 0, 0));
            Bone(side == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, elbow, new Vector3(sign * .12f, 0, 0));
        }
        var result = AvatarBuilder.BuildHumanAvatar(root, new HumanDescription
        {
            human = human.ToArray(),
            skeleton = root.GetComponentsInChildren<Transform>().Select(t => new SkeletonBone
                { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
            upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f,
            armStretch = .05f, legStretch = .05f, feetSpacing = 0, hasTranslationDoF = false
        });
        if (!result.isValid || !result.isHuman) throw new Exception("Invalid Humanoid fixture.");
        result.name = "FX Viewer humanoid";
        AssetDatabase.CreateAsset(result, folder + "/Humanoid.asset");
        return result;
    }

    private static void CheckHumanoidPose(string label)
    {
        var source = avatar.GetComponent<Animator>();
        var clone = Read<GameObject>("PreviewRoot");
        bool match = new[] { HumanBodyBones.Hips, HumanBodyBones.Head }.All(id =>
        {
            var bone = source.GetBoneTransform(id);
            var path = AnimationUtility.CalculateTransformPath(bone, avatar.transform);
            return Vector3.Distance(clone.transform.Find(path).localPosition, bone.localPosition) < .0001f
                && Vector3.Distance(clone.transform.InverseTransformPoint(clone.transform.Find(path).position),
                    avatar.transform.InverseTransformPoint(bone.position)) < .0001f;
        });
        Check(match, label);
    }

    private static void VerifyAssignments()
    {
        Undo.ClearAll();
        Call("SelectAvatar", avatar); Call("ConfirmClip", eye); Call("SetPreviewTime", .5f);
        window.SendEvent(new Event { type = EventType.Layout });
        var clone = Read<GameObject>("PreviewRoot");
        var renderer = clone.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
        var clipField = Read<Rect>("FirstConfirmRect");
        var dropPoint = new Vector2(clipField.xMin - 30, clipField.center.y);
        DragAndDrop.PrepareStartDrag();
        DragAndDrop.objectReferences = new Object[] { newExpression };
        window.SendEvent(new Event { type = EventType.DragUpdated, mousePosition = dropPoint });
        window.SendEvent(new Event { type = EventType.DragPerform, mousePosition = dropPoint });
        DragAndDrop.objectReferences = new Object[0];
        Check(eyeState.motion == newExpression, "The enabled clip field accepts a dragged animation");
        Check(Read<AnimationClip>("SelectedClip") == newExpression && ListedClip(0) == newExpression,
            "Replacing the selected state refreshes the list and preview");
        Check(Read<GameObject>("PreviewRoot") == clone && renderer.GetBlendShapeWeight(0) == 7
            && renderer.GetBlendShapeWeight(1) == 55 && clone.transform.Find("Accessory").localScale.x == 2,
            "New clip bindings sample correctly without rebuilding the camera or avatar");
        Check(Mathf.Abs(clone.transform.Find("Hips").localPosition.y - .6f) < .0001f,
            "Explicit transform curves still move a Humanoid bone when requested by the clip");
        string newGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(newExpression));
        Check(File.ReadAllText(AssetDatabase.GetAssetPath(controller)).Contains(newGuid), "The state replacement is saved to the controller file");
        UndoChange();
        Check(eyeState.motion == eye && Read<AnimationClip>("SelectedClip") == eye && ListedClip(0) == eye,
            "Undo restores the state, list and selected preview");
        Check(clone.transform.Find("Accessory").localScale.x == 1 && renderer.GetBlendShapeWeight(1) == 11,
            "Undo restores properties first introduced by a new clip");
        CheckHumanoidPose("Undo restores the Humanoid pose after an explicitly animated bone");
        Undo.PerformRedo();
        Check(eyeState.motion == newExpression && Read<AnimationClip>("SelectedClip") == newExpression,
            "Redo reapplies the state and selected preview");
        Check(Replace(0, null) && eyeState.motion == null && Read<AnimationClip>("SelectedClip") == null
            && clone.transform.Find("Accessory").localScale.x == 1, "Clearing a state clears its preview and restores defaults");
        UndoChange(); Check(eyeState.motion == newExpression && Read<AnimationClip>("SelectedClip") == newExpression,
            "Clearing a state can be undone");
        Undo.PerformUndo();

        Call("ConfirmClip", swap);
        var originalChild = blend.children[0];
        Check(Replace(2, newExpression) && blend.children[0].motion == newExpression
            && Read<AnimationClip>("SelectedClip") == newExpression, "Nested Blend Tree children can be replaced and previewed");
        Check(blend.children[0].threshold == originalChild.threshold && blend.children[0].timeScale == originalChild.timeScale
            && blend.children[0].mirror == originalChild.mirror,
            "Replacing a Blend Tree child preserves its blend settings");
        UndoChange(); Check(blend.children[0].motion == swap && Read<AnimationClip>("SelectedClip") == swap,
            "Undo restores a Blend Tree child and its selected preview");
        Check(Replace(3, newExpression) && emptyState.motion == newExpression, "Empty FX states accept clips");
        UndoChange(); Check(emptyState.motion == null, "Undo returns an empty FX state to None");
        Check(!Replace(-1, eye) && !Replace(999, eye), "Invalid target indices leave the controller unchanged");
        AssetDatabase.SaveAssets();
        Check(Hash(AssetDatabase.GetAssetPath(controller)) == hashes[AssetDatabase.GetAssetPath(controller)],
            "Undo restores the complete controller file, including unrelated settings");

        var layers = avatar.baseAnimationLayers; layers[0].animatorController = savedOverrides; avatar.baseAnimationLayers = layers;
        Call("SelectAvatar", avatar); Call("ConfirmClip", mouth);
        Check(Replace(0, newExpression) && savedOverrides[eye] == newExpression && eyeState.motion == eye,
            "Override replacement edits the override asset without changing the base controller");
        Check(ListedClip(0) == newExpression && ListedClip(4) == newExpression && Read<AnimationClip>("SelectedClip") == newExpression,
            "Shared override entries and the selected preview update together");
        Check(File.ReadAllText(AssetDatabase.GetAssetPath(savedOverrides)).Contains(newGuid), "Override replacements are saved to disk");
        UndoChange(); Check(savedOverrides[eye] == mouth && Read<AnimationClip>("SelectedClip") == mouth,
            "Undo restores the override and selected preview");
        Undo.PerformRedo(); Check(savedOverrides[eye] == newExpression && ListedClip(4) == newExpression,
            "Redo restores shared override entries");
        Check(Replace(0, null) && Read<AnimationClip>("SelectedClip") == eye && ListedClip(4) == eye,
            "Clearing an override restores the original base clip in every row");
        UndoChange(); Undo.PerformUndo();
        Check(!Replace(3, newExpression) && emptyState.motion == null, "An override cannot assign a clip to an empty base state");
        layers[0].animatorController = controller; avatar.baseAnimationLayers = layers;
        AssetDatabase.SaveAssets();
        Check(hashes.All(h => Hash(h.Key) == h.Value), "All fixture assets return byte-for-byte to their original contents after Undo");
        Check(body.GetBlendShapeWeight(0) == 7 && body.GetBlendShapeWeight(1) == 11
            && avatar.transform.Find("Accessory").localScale == Vector3.one && !avatar.gameObject.scene.isDirty,
            "Replacing clips never applies preview values to the source avatar or scene");
        Undo.ClearAll();
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
