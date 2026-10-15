// Run only in a disposable Unity project containing the SDK, Modular Avatar and the editions under test.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using Object = UnityEngine.Object;

public static class VerifyExpressionMenu
{
    [Serializable] private class Report { public List<string> passed = new List<string>(); public List<string> failed = new List<string>(); }
    private static readonly Report report = new Report();
    private static string rootFolder, prefix;
    private static Animator running;
    private static VRC_AnimatorTrackingControl.TrackingType eyes, mouth;
    private static int trackingEvents;
    private static void Check(bool result, string label) { (result ? report.passed : report.failed).Add(prefix + label); }
    private static string Hash(string path) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }
    private static void InitTracking(VRC_AnimatorTrackingControl control) { control.ApplySettings += ApplyTracking; }
    private static void InitLayer(VRC_AnimatorLayerControl control) { control.ApplySettings += ApplyLayer; }
    private static void ApplyTracking(VRC_AnimatorTrackingControl control, Animator animator)
    {
        if (animator != running) return;
        trackingEvents++;
        if (control.trackingEyes != VRC_AnimatorTrackingControl.TrackingType.NoChange) eyes = control.trackingEyes;
        if (control.trackingMouth != VRC_AnimatorTrackingControl.TrackingType.NoChange) mouth = control.trackingMouth;
    }
    private static void ApplyLayer(VRC_AnimatorLayerControl control, Animator animator)
    { if (animator == running) animator.SetLayerWeight(control.layer, control.goalWeight); }
    private static void Tick(int frames = 8) { for (int i = 0; i < frames; i++) running.Update(1f / 60); }
    private static AnimationClip Clip(string name, string shape, float value)
    {
        var clip = new AnimationClip { name = name, frameRate = 60 };
        clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + shape, AnimationCurve.Constant(0, .1f, value));
        AssetDatabase.CreateAsset(clip, rootFolder + "/" + name + ".anim");
        return clip;
    }
    private static SkinnedMeshRenderer Mesh(GameObject root, string name, string[] shapes)
    {
        var obj = new GameObject(name); obj.transform.SetParent(root.transform, false);
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        foreach (var shape in shapes)
            mesh.AddBlendShapeFrame(shape, 100, new[] { Vector3.zero, Vector3.zero, Vector3.forward * .1f }, new Vector3[3], new Vector3[3]);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, rootFolder + "/" + name + ".asset");
        var renderer = obj.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh; return renderer;
    }
    private static object Build(Type builder, VRCAvatarDescriptor avatar, AnimationClip[] clips, string name, string folder)
    {
        var entryType = builder.Assembly.GetType(builder.Namespace + ".ExpressionMenuEntry");
        var entries = Array.CreateInstance(entryType, clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            var entry = System.Activator.CreateInstance(entryType);
            entryType.GetField("Clip").SetValue(entry, clips[i]);
            entryType.GetField("Name").SetValue(entry, "Expression " + (i + 1));
            entries.SetValue(entry, i);
        }
        return builder.GetMethod("Build").Invoke(null, new object[] { avatar, entries, name, folder, "解除", "次へ" });
    }
    private static T Field<T>(object value, string name) { return (T)value.GetType().GetField(name).GetValue(value); }
    private static IEnumerable<VRCExpressionsMenu.Control> Controls(VRCExpressionsMenu menu)
    {
        foreach (var control in menu.controls)
        {
            yield return control;
            if (control.subMenu != null) foreach (var child in Controls(control.subMenu)) yield return child;
        }
    }
    public static void RunPlayMode()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool("PPAE.ExpressionMenu.PlayVerification", true);
        EditorApplication.update += AwaitPlayMode;
        EditorApplication.isPlaying = true;
    }
    [InitializeOnLoadMethod]
    private static void ResumePlayVerification()
    {
        if (SessionState.GetBool("PPAE.ExpressionMenu.PlayVerification", false))
            EditorApplication.update += AwaitPlayMode;
    }
    private static void AwaitPlayMode()
    {
        if (!Application.isPlaying) return;
        EditorApplication.update -= AwaitPlayMode;
        if (!SessionState.GetBool("PPAE.ExpressionMenu.PlayVerification", false)) return;
        SessionState.SetBool("PPAE.ExpressionMenu.PlayVerification", false);
        RunBatch();
    }
    public static void RunBatch()
    {
        VRC_AnimatorTrackingControl.Initialize += InitTracking;
        VRC_AnimatorLayerControl.Initialize += InitLayer;
        try
        {
            if (!Application.isPlaying) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            rootFolder = AssetDatabase.GenerateUniqueAssetPath("Assets/ExpressionMenuVerification");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(rootFolder));
            var avatarRoot = new GameObject("Module fixture");
            var animator = avatarRoot.AddComponent<Animator>(); animator.enabled = false;
            var avatar = avatarRoot.AddComponent<VRCAvatarDescriptor>();
            var body = Mesh(avatarRoot, "Body", new[] { "Eye", "Mouth", "Blink", "Viseme", "FaceWidth" });
            body.SetBlendShapeWeight(4, 12);
            var clothing = Mesh(avatarRoot, "Clothing", new[] { "Sleeve" }); clothing.SetBlendShapeWeight(0, 23);
            avatar.VisemeSkinnedMesh = body; avatar.VisemeBlendShapes = new[] { "Viseme" };
            avatar.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
            var eyeSettings = avatar.customEyeLookSettings;
            eyeSettings.eyelidsSkinnedMesh = body; eyeSettings.eyelidsBlendshapes = new[] { 2, -1, -1 };
            avatar.customEyeLookSettings = eyeSettings;
            var eyeClip = Clip("Eye", "Eye", 75);
            var mouthClip = Clip("Mouth", "Mouth", 64);
            var originalClip = Clip("Original", "Eye", 11);
            originalClip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Mouth", AnimationCurve.Constant(0, .1f, 22));
            originalClip.SetCurve("Clothing", typeof(SkinnedMeshRenderer), "blendShape.Sleeve", AnimationCurve.Constant(0, .1f, 23));
            var otherClip = Clip("Gesture", "Eye", 33);
            otherClip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Mouth", AnimationCurve.Constant(0, .1f, 44));
            otherClip.SetCurve("Clothing", typeof(SkinnedMeshRenderer), "blendShape.Sleeve", AnimationCurve.Constant(0, .1f, 42));
            var original = AnimatorController.CreateAnimatorControllerAtPath(rootFolder + "/Original.controller");
            original.AddParameter("Gesture", AnimatorControllerParameterType.Int);
            var machine = original.layers[0].stateMachine;
            var normal = machine.AddState("Original face"); normal.motion = originalClip; normal.writeDefaultValues = false;
            var gesture = machine.AddState("Other gesture"); gesture.motion = otherClip; gesture.writeDefaultValues = false;
            var competing = gesture.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
            competing.trackingEyes = competing.trackingMouth = VRCAnimatorTrackingControl.TrackingType.Tracking;
            var change = normal.AddTransition(gesture); change.hasExitTime = false; change.duration = 0;
            change.AddCondition(AnimatorConditionMode.Equals, 1, "Gesture");
            var back = gesture.AddTransition(normal); back.hasExitTime = false; back.duration = 0;
            back.AddCondition(AnimatorConditionMode.Equals, 0, "Gesture");
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls.Add(new VRCExpressionsMenu.Control { name = "Existing item", type = VRCExpressionsMenu.Control.ControlType.Button });
            AssetDatabase.CreateAsset(menu, rootFolder + "/OriginalMenu.asset");
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            parameters.parameters = new[] { new VRCExpressionParameters.Parameter { name = "Existing", valueType = VRCExpressionParameters.ValueType.Bool } };
            AssetDatabase.CreateAsset(parameters, rootFolder + "/OriginalParameters.asset");
            avatar.customExpressions = true; avatar.expressionsMenu = menu; avatar.expressionParameters = parameters;
            avatar.customizeAnimationLayers = true;
            avatar.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = original } };
            AssetDatabase.SaveAssets();
            if (!Application.isPlaying) EditorSceneManager.SaveScene(avatarRoot.scene, rootFolder + "/Original.unity");
            var hashes = Directory.GetFiles(rootFolder).Where(p => !p.EndsWith(".meta")).ToDictionary(p => p, Hash);
            var builders = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                .Where(t => t.Name == "ExpressionMenuBuilder" && t.Namespace.EndsWith(".Modules")).ToArray();
            if (builders.Length == 0) throw new Exception("No generator assemblies compiled.");
            foreach (var builder in builders)
            {
                prefix = builder.Namespace + ": ";
                try { VerifyEdition(builder, avatar, eyeClip, mouthClip, hashes); }
                catch (Exception error) { report.failed.Add(prefix + error); }
            }
        }
        catch (Exception error) { report.failed.Add(error.ToString()); }
        finally
        {
            running = null;
            VRC_AnimatorTrackingControl.Initialize -= InitTracking;
            VRC_AnimatorLayerControl.Initialize -= InitLayer;
            if (!Application.isPlaying) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (rootFolder != null) AssetDatabase.DeleteAsset(rootFolder);
            string output = Path.GetFullPath("../expression-menu-results");
            Directory.CreateDirectory(output);
            File.WriteAllText(output + (Application.isPlaying ? "/play-mode.json" : "/edit-mode.json"), JsonUtility.ToJson(report, true));
            Debug.Log("Expression menu: " + report.passed.Count + " passed, " + report.failed.Count + " failed.");
            EditorApplication.Exit(report.failed.Count == 0 ? 0 : 1);
        }
    }
    private static void VerifyEdition(Type builder, VRCAvatarDescriptor avatar, AnimationClip eye, AnimationClip mouthClip, Dictionary<string,string> hashes)
    {
        bool sceneDirtyBefore = avatar.gameObject.scene.isDirty;
        var result = Build(builder, avatar, new[] { eye, mouthClip }, "Test", rootFolder);
        var prefab = Field<GameObject>(result, "Prefab");
        var controller = Field<AnimatorController>(result, "Controller");
        string parameter = Field<string>(result, "Parameter");
        var merge = prefab.GetComponent<ModularAvatarMergeAnimator>();
        Check(merge.mergeAnimatorMode == MergeAnimatorMode.Append && merge.pathMode == MergeAnimatorPathMode.Absolute
            && merge.layerType == VRCAvatarDescriptor.AnimLayerType.FX, "Module appends FX with avatar-root paths");
        var config = prefab.GetComponent<ModularAvatarParameters>().parameters.Single();
        Check(config.internalParameter && !config.saved && !config.localOnly && config.syncType == ParameterSyncType.Int
            && config.defaultValue == 0, "One private synced int; fixed expressions are not saved across loads");
        Check(controller.layers[1].defaultWeight == 0, "Expression layer starts with no influence");
        Check(controller.layers[0].stateMachine.defaultState.behaviours.Length == 0, "Idle does not force other tracking owners");
        Check(Controls(prefab.GetComponent<ModularAvatarMenuInstaller>().menuToAppend)
            .Count(c => c.type == VRCExpressionsMenu.Control.ControlType.Toggle) == 2, "Two mutually exclusive expression controls generated");
        var generated = controller.layers[1].stateMachine.states.Select(s => s.state.motion).OfType<AnimationClip>()
            .Where(c => AnimationUtility.GetCurveBindings(c).Length > 0).ToArray();
        Check(generated.Length == 2 && generated.All(c => AnimationUtility.GetCurveBindings(c).Length == 5), "Complete facial snapshot includes blink and lip sync shapes");
        Check(generated.All(c => AnimationUtility.GetCurveBindings(c).All(b => b.path == "Body")), "Unrelated clothing mesh is excluded");
        Check(generated.All(c => AssetDatabase.GetAssetPath(c) != AssetDatabase.GetAssetPath(eye)), "Source clips are copied, not edited");
        var second = Build(builder, avatar, new[] { eye }, "Test", rootFolder);
        Check(Field<string>(second, "Folder") != Field<string>(result, "Folder") && Field<string>(second, "Parameter") != parameter, "Repeated generation never overwrites prior files");
        var many = Build(builder, avatar, Enumerable.Repeat(eye, 16).ToArray(), "Paged", rootFolder);
        var installMenu = Field<GameObject>(many, "Prefab").GetComponent<ModularAvatarMenuInstaller>().menuToAppend;
        var controls = Controls(installMenu).ToArray();
        Check(controls.Count(c => c.type == VRCExpressionsMenu.Control.ControlType.Toggle) == 16
            && controls.Where(c => c.subMenu != null).All(c => c.subMenu.controls.Count <= 8), "Menu pagination retains every expression and respects eight slots");
        bool invalid = false;
        var unsupported = new AnimationClip();
        unsupported.SetCurve("Body", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0, 1, 1));
        try { Build(builder, avatar, new[] { unsupported }, "Rejected", rootFolder); } catch (TargetInvocationException e) { invalid = e.InnerException is ArgumentException; }
        Object.DestroyImmediate(unsupported);
        Check(invalid && !AssetDatabase.IsValidFolder(rootFolder + "/Rejected"), "Unsupported curves rejected without partial files");
        Check(avatar.gameObject.scene.isDirty == sceneDirtyBefore, "Generating modules preserves scene dirty state");
        Check(hashes.All(pair => Hash(pair.Key) == pair.Value), "Generating modules preserves source FX, menu, parameters and clips");
        if (!Application.isPlaying)
        {
            VerifyWindow(builder, avatar, prefab);
            return;
        }

        // Run the actual MA/NDMF merge on a disposable avatar clone.
        var clone = Object.Instantiate(avatar.gameObject);
        clone.name = "Merged fixture";
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, clone.transform);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        AvatarProcessor.ProcessAvatar(clone);
        var built = clone.GetComponent<VRCAvatarDescriptor>();
        var merged = (AnimatorController)built.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
        Check(merged.layers.Any(l => l.stateMachine.states.Any(s => s.state.name == "Original face")), "MA build retains original FX states");
        Check(Controls(built.expressionsMenu).Any(c => c.name == "Existing item"), "MA build retains original expression menu");
        Check(built.expressionParameters.parameters.Any(p => p.name == "Existing"), "MA build retains original parameters");
        var selection = Controls(built.expressionsMenu).First(c => c.type == VRCExpressionsMenu.Control.ControlType.Toggle);
        string mergedParameter = selection.parameter.name;
        Check(built.expressionParameters.parameters.Any(p => p.name == mergedParameter), "MA remaps menu and animator to the same synced parameter");
        running = clone.GetComponent<Animator>(); running.enabled = true; running.runtimeAnimatorController = merged;
        running.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        eyes = mouth = VRC_AnimatorTrackingControl.TrackingType.Tracking; trackingEvents = 0;
        running.Rebind(); Tick();
        var body = clone.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
        var clothing = clone.transform.Find("Clothing").GetComponent<SkinnedMeshRenderer>();
        Check(Mathf.Approximately(body.GetBlendShapeWeight(0), 11), "Off uses the original expression");
        running.SetInteger(mergedParameter, 1); Tick();
        Debug.Log(prefix + "fixed evaluation parameter=" + running.GetInteger(mergedParameter) + " eye=" + body.GetBlendShapeWeight(0)
            + " events=" + trackingEvents + " layers=" + string.Join(", ", Enumerable.Range(0, running.layerCount)
                .Select(i => running.GetLayerName(i) + "=" + running.GetLayerWeight(i) + "/" + running.GetCurrentAnimatorStateInfo(i).shortNameHash)));
        Check(Mathf.Approximately(body.GetBlendShapeWeight(0), 75) && Mathf.Approximately(body.GetBlendShapeWeight(1), 0), "First expression overrides original shapes without blending");
        Check(eyes == VRC_AnimatorTrackingControl.TrackingType.Animation && mouth == VRC_AnimatorTrackingControl.TrackingType.Animation, "Fixed state disables mouth and eye tracking through actual SDK callbacks");
        int events = trackingEvents;
        running.SetInteger("Gesture", 1); Tick();
        Check(Mathf.Approximately(body.GetBlendShapeWeight(0), 75) && Mathf.Approximately(clothing.GetBlendShapeWeight(0), 42), "Gesture changes keep the face fixed while unrelated clothing still animates");
        Check(trackingEvents > events && eyes == VRC_AnimatorTrackingControl.TrackingType.Animation && mouth == VRC_AnimatorTrackingControl.TrackingType.Animation, "Tracking lock reasserts after a competing original gesture state");
        running.SetInteger(mergedParameter, 2); Tick();
        Check(Mathf.Approximately(body.GetBlendShapeWeight(0), 0) && Mathf.Approximately(body.GetBlendShapeWeight(1), 64), "Switching expressions clears values from the previous expression");
        running.SetInteger(mergedParameter, 0); Tick();
        Check(Mathf.Approximately(body.GetBlendShapeWeight(0), 33) && Mathf.Approximately(body.GetBlendShapeWeight(1), 44), "Release restores the currently selected original gesture");
        Check(eyes == VRC_AnimatorTrackingControl.TrackingType.Tracking && mouth == VRC_AnimatorTrackingControl.TrackingType.Tracking, "Release reenables eye and mouth tracking");
        running.SetInteger(mergedParameter, 1); Tick();
        Check(Mathf.Approximately(body.GetBlendShapeWeight(0), 75) && eyes == VRC_AnimatorTrackingControl.TrackingType.Animation, "Selecting again after release reactivates the module");
        running.SetInteger(mergedParameter, 99); Tick();
        Check(Mathf.Approximately(body.GetBlendShapeWeight(0), 33) && eyes == VRC_AnimatorTrackingControl.TrackingType.Tracking, "Invalid selector values safely release the module");
        running = null;
        Object.DestroyImmediate(clone);
        Check(hashes.All(pair => Hash(pair.Key) == pair.Value), "Building and evaluating the clone leaves original files unchanged");
    }

    private static void VerifyWindow(Type builder, VRCAvatarDescriptor avatar, GameObject prefab)
    {
        var type = builder.Assembly.GetType(builder.Namespace + ".ExpressionMenuWindow");
        var window = (EditorWindow)ScriptableObject.CreateInstance(type);
        try
        {
            type.GetMethod("SetAvatar").Invoke(window, new object[] { avatar });
            var folder = (string)type.GetField("folder", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            Check(folder.StartsWith("Assets/PPAE/" + avatar.name + "/"), "Window uses the avatar-specific PPAE default folder");
            type.GetField("generatedPrefab", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, prefab);
            int before = avatar.transform.childCount;
            var instance = (GameObject)type.GetMethod("Install").Invoke(window, null);
            Check(instance != null && instance.transform.parent == avatar.transform && PrefabUtility.IsPartOfPrefabInstance(instance), "Window installs a connected prefab under the selected avatar");
            Check(type.GetMethod("Install").Invoke(window, null) == null && avatar.transform.childCount == before + 1, "Window rejects a second PPAE tracking owner");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Check(avatar.transform.childCount == before, "Undo removes only the newly installed module");
            Undo.PerformRedo();
            Check(avatar.transform.childCount == before + 1, "Redo reinstalls the generated module");
            Undo.PerformUndo(); Undo.ClearAll();
            window.position = new Rect(20, 20, 620, 480);
            window.Show();
            window.SendEvent(new Event { type = EventType.Layout });
            window.SendEvent(new Event { type = EventType.Repaint });
            Check(window.titleContent.text.Length > 0, "Generator window lays out and repaints");
        }
        finally { window.Close(); }
    }
}
