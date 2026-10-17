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
    private static object Build(Type builder, VRCAvatarDescriptor avatar, AnimationClip[] clips, string name, string folder,
        Texture2D icon = null, float time = 0)
    {
        var entryType = builder.Assembly.GetType(builder.Namespace + ".ExpressionMenuEntry");
        var entries = Array.CreateInstance(entryType, clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            var entry = System.Activator.CreateInstance(entryType);
            entryType.GetField("Clip").SetValue(entry, clips[i]);
            entryType.GetField("Name").SetValue(entry, "Expression " + (i + 1));
            entryType.GetField("Icon").SetValue(entry, icon);
            entryType.GetField("Time").SetValue(entry, time);
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
        IDisposable generatedAssets = null;
        VRC_AnimatorTrackingControl.Initialize += InitTracking;
        VRC_AnimatorLayerControl.Initialize += InitLayer;
        try
        {
            if (!Application.isPlaying) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            rootFolder = AssetDatabase.GenerateUniqueAssetPath("Assets/ExpressionMenuVerification");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(rootFolder));
            generatedAssets = new OverrideTemporaryDirectoryScope(rootFolder + "/MergedAssets");
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
            generatedAssets?.Dispose();
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
        var generated = controller.layers.SelectMany(l => l.stateMachine.states).Select(s => s.state.motion).OfType<AnimationClip>()
            .Where(c => AnimationUtility.GetCurveBindings(c).Length > 0).ToArray();
        Check(generated.Length == 2 && generated.Contains(eye) && generated.Contains(mouthClip), "States reference the exact original clip assets");
        Check(generated.All(c => AnimationUtility.GetCurveBindings(c).All(b => b.path == "Body")), "Unrelated clothing mesh is excluded");
        Check(!AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)).OfType<AnimationClip>().Any()
            && new[] { "ControllerFolder", "MenuFolder", "IconFolder", "PrefabFolder" }
                .All(f => !Directory.GetFiles(Field<string>(result, f), "*.anim", SearchOption.AllDirectories).Any()),
            "No copied, empty or neutral animation clips are generated");
        foreach (string category in new[] { "Controller", "Menu", "Icons", "Prefab" })
            Check(AssetDatabase.IsValidFolder(rootFolder + "/" + category + "/Test"), category + " output is separated by asset type");
        var autoIcons = Controls(prefab.GetComponent<ModularAvatarMenuInstaller>().menuToAppend)
            .Where(c => c.type == VRCExpressionsMenu.Control.ControlType.Toggle).Select(c => c.icon).ToArray();
        Check(autoIcons.All(i => i != null && i.width == 256 && i.height == 256
            && AssetDatabase.GetAssetPath(i).StartsWith(Field<string>(result, "IconFolder"))), "Creation automatically captures every expression icon at 256px");
        var second = Build(builder, avatar, new[] { eye }, "Test", rootFolder);
        Check(Field<string>(second, "PrefabFolder") != Field<string>(result, "PrefabFolder") && Field<string>(second, "Parameter") != parameter, "Repeated generation never overwrites prior files");
        var many = Build(builder, avatar, Enumerable.Repeat(eye, 16).ToArray(), "表情", rootFolder, autoIcons[0]);
        var installMenu = Field<GameObject>(many, "Prefab").GetComponent<ModularAvatarMenuInstaller>().menuToAppend;
        var controls = Controls(installMenu).ToArray();
        Check(controls.Count(c => c.type == VRCExpressionsMenu.Control.ControlType.Toggle) == 16
            && controls.Where(c => c.subMenu != null).All(c => c.subMenu.controls.Count <= 8), "Menu pagination retains every expression and respects eight slots");
        Check(installMenu.controls.Count == 1 && installMenu.controls[0].name == "表情呼び出し"
            && installMenu.controls[0].type == VRCExpressionsMenu.Control.ControlType.SubMenu
            && AssetDatabase.GetAssetPath(installMenu.controls[0].subMenu) == Field<string>(many, "MenuFolder") + "/Hub.asset",
            "Avatar root receives exactly one expression hub backed by its own menu asset");
        var hub = installMenu.controls[0].subMenu;
        Check(hub != null && hub.controls.Select(c => c.name).SequenceEqual(new[] { "表情1", "表情2", "表情3" })
            && hub.controls.All(c => c.subMenu != null && c.subMenu.controls.All(s => s.subMenu == null)),
            "Expression groups and their clips are nested under the expression hub");
        var overflow = Build(builder, avatar, Enumerable.Repeat(eye, 63).ToArray(), "Overflow", rootFolder, autoIcons[0]);
        var overflowMenu = Field<GameObject>(overflow, "Prefab").GetComponent<ModularAvatarMenuInstaller>().menuToAppend;
        Check(overflowMenu.controls.Count <= 8 && Controls(overflowMenu).Count(c => c.type == VRCExpressionsMenu.Control.ControlType.Toggle) == 63
            && Controls(overflowMenu).Where(c => c.subMenu != null).All(c => c.subMenu.controls.Count <= 8), "More than eight expression groups retain all clips through index pagination");
        var ramp = Clip("Timed_" + builder.Namespace.Replace('.', '_'), "Eye", 0);
        AnimationUtility.SetEditorCurve(ramp, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Eye"),
            AnimationCurve.Linear(0, 0, 1, 100));
        var timed = Build(builder, avatar, new[] { ramp }, "Timed", rootFolder, autoIcons[0], .5f);
        var timedController = Field<AnimatorController>(timed, "Controller");
        var timedState = timedController.layers[1].stateMachine.states.Single(s => s.state.motion != null).state;
        Check(timedState.motion == ramp && timedState.timeParameterActive && timedController.parameters.Single(p => p.name == timedState.timeParameter).defaultFloat == .5f,
            "Fixed time is stored on the controller without editing the source curve");
        var entryType = builder.Assembly.GetType(builder.Namespace + ".ExpressionMenuEntry");
        var cancelEntries = Array.CreateInstance(entryType, 2);
        for (int i = 0; i < 2; i++)
        {
            var entry = System.Activator.CreateInstance(entryType);
            entryType.GetField("Clip").SetValue(entry, eye); cancelEntries.SetValue(entry, i);
        }
        bool cancelled = false;
        try
        {
            builder.GetMethod("BuildWithCapture").Invoke(null, new object[] { avatar, cancelEntries, "Cancelled", rootFolder, "解除", "次へ", null,
                new Func<int, int, string, bool>((index, total, name) => index == 1) });
        }
        catch (TargetInvocationException ex) { cancelled = ex.InnerException is OperationCanceledException; }
        Check(cancelled && new[] { "Controller", "Menu", "Icons", "Prefab" }.All(c => !AssetDatabase.IsValidFolder(rootFolder + "/" + c + "/Cancelled"))
            && prefab != null, "Cancelling icon generation removes only the new typed directories and preserves earlier modules");
        bool invalid = false;
        var unsupported = new AnimationClip();
        unsupported.SetCurve("Body", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0, 1, 1));
        string unsupportedPath = rootFolder + "/Unsupported.anim";
        AssetDatabase.CreateAsset(unsupported, unsupportedPath);
        try { Build(builder, avatar, new[] { unsupported }, "Rejected", rootFolder); } catch (TargetInvocationException e) { invalid = e.InnerException is ArgumentException; }
        AssetDatabase.DeleteAsset(unsupportedPath);
        Check(invalid && !AssetDatabase.IsValidFolder(rootFolder + "/Rejected"), "Unsupported curves rejected without partial files");
        Check(avatar.gameObject.scene.isDirty == sceneDirtyBefore, "Generating modules preserves scene dirty state");
        Check(hashes.All(pair => Hash(pair.Key) == pair.Value), "Generating modules preserves source FX, menu, parameters and clips");
        if (!Application.isPlaying)
        {
            VerifyMergedHub(avatar, Field<GameObject>(many, "Prefab"), autoIcons[0], hashes);
            VerifyPageBoundariesAndBudget(builder, avatar, eye, autoIcons[0]);
            VerifyEditableFolders(builder, avatar, eye, mouthClip, autoIcons[0]);
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
        Check(Mathf.Approximately(body.GetBlendShapeWeight(0), 75) && Mathf.Approximately(body.GetBlendShapeWeight(1), 22), "First expression overrides its bindings and leaves unrecorded shapes on the original FX");
        Check(eyes == VRC_AnimatorTrackingControl.TrackingType.Animation && mouth == VRC_AnimatorTrackingControl.TrackingType.Animation, "Fixed state disables mouth and eye tracking through actual SDK callbacks");
        int events = trackingEvents;
        running.SetInteger("Gesture", 1); Tick();
        Check(Mathf.Approximately(body.GetBlendShapeWeight(0), 75) && Mathf.Approximately(clothing.GetBlendShapeWeight(0), 42), "Gesture changes keep the face fixed while unrelated clothing still animates");
        Check(trackingEvents > events && eyes == VRC_AnimatorTrackingControl.TrackingType.Animation && mouth == VRC_AnimatorTrackingControl.TrackingType.Animation, "Tracking lock reasserts after a competing original gesture state");
        running.SetInteger(mergedParameter, 2); Tick();
        Check(Mathf.Approximately(body.GetBlendShapeWeight(0), 33) && Mathf.Approximately(body.GetBlendShapeWeight(1), 64), "Switching expressions drops the previous sparse clip and follows the current original FX");
        running.SetInteger(mergedParameter, 0); Tick();
        Check(Mathf.Approximately(body.GetBlendShapeWeight(0), 33) && Mathf.Approximately(body.GetBlendShapeWeight(1), 44), "Release restores the currently selected original gesture");
        Check(eyes == VRC_AnimatorTrackingControl.TrackingType.Tracking && mouth == VRC_AnimatorTrackingControl.TrackingType.Tracking, "Release reenables eye and mouth tracking");
        running.SetInteger(mergedParameter, 1); Tick();
        Check(Mathf.Approximately(body.GetBlendShapeWeight(0), 75) && eyes == VRC_AnimatorTrackingControl.TrackingType.Animation, "Selecting again after release reactivates the module");
        running.SetInteger(mergedParameter, 99); Tick();
        Check(Mathf.Approximately(body.GetBlendShapeWeight(0), 33) && eyes == VRC_AnimatorTrackingControl.TrackingType.Tracking, "Invalid selector values safely release the module");
        running = null;
        Object.DestroyImmediate(clone);
        var timedClone = Object.Instantiate(avatar.gameObject);
        running = timedClone.GetComponent<Animator>(); running.enabled = true;
        running.runtimeAnimatorController = timedController; running.Rebind(); Tick();
        running.SetInteger(Field<string>(timed, "Parameter"), 1); Tick(60);
        Check(Mathf.Abs(timedClone.transform.Find("Body").GetComponent<SkinnedMeshRenderer>().GetBlendShapeWeight(0) - 50) < .01f,
            "Play Mode holds the original animated clip at the requested sample time");
        running = null; Object.DestroyImmediate(timedClone);
        Check(hashes.All(pair => Hash(pair.Key) == pair.Value), "Building and evaluating the clone leaves original files unchanged");
    }

    private static void VerifyEditableFolders(Type builder, VRCAvatarDescriptor avatar, AnimationClip eye, AnimationClip mouthClip, Texture2D icon)
    {
        var clone = Object.Instantiate(avatar.gameObject);
        var type = builder.Assembly.GetType(builder.Namespace + ".ExpressionMenuWindow");
        var window = (EditorWindow)ScriptableObject.CreateInstance(type);
        try
        {
            clone.name = "Split FX fixture";
            var descriptor = clone.GetComponent<VRCAvatarDescriptor>();
            string token = builder.Namespace.Replace('.', '_');
            var fx = AnimatorController.CreateAnimatorControllerAtPath(rootFolder + "/Split_" + token + ".controller");
            var layers = fx.layers; layers[0].name = "Left Hand"; fx.layers = layers;
            fx.layers[0].stateMachine.AddState("Left expression").motion = eye;
            fx.AddLayer("Right Hand"); fx.layers[1].stateMachine.AddState("Right expression").motion = eye;
            fx.AddLayer("Different expressions"); fx.layers[2].stateMachine.AddState("Other expression").motion = mouthClip;
            foreach (string auto in new[] { "LipSync", "Blink", "Viseme", "まばたき" })
            { fx.AddLayer(auto); fx.layers[fx.layers.Length - 1].stateMachine.AddState("Default").motion = eye; }
            var blink = Object.Instantiate(eye); blink.name = "Auto_Blink";
            AssetDatabase.CreateAsset(blink, rootFolder + "/Blink_" + token + ".anim"); blink.name = "Auto_Blink";
            fx.layers[0].stateMachine.AddState("Automatic eyes").motion = blink;
            fx.layers[0].stateMachine.AddStateMachine("Viseme").AddState("A").motion = mouthClip;
            EditorUtility.SetDirty(fx); AssetDatabase.SaveAssets();
            descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX, animatorController = fx } };
            type.GetMethod("SetAvatar").Invoke(window, new object[] { descriptor });
            var entries = (IList)type.GetProperty("Entries").GetValue(window);
            var folders = (IList)type.GetProperty("Folders").GetValue(window);
            var categories = (IList)type.GetProperty("Categories").GetValue(window);
            Check(folders.Count == 3 && entries.Count == 3 && folders.Cast<object>().Select(f => Field<string>(f, "Name"))
                .SequenceEqual(new[] { "Left Hand", "Right Hand", "Different expressions" }),
                "Left Hand and Right Hand remain separate even when their clip sets are identical; differing layers are also preserved");
            Check(entries.Cast<object>().All(e => Field<AnimationClip>(e, "Clip") != blink), "Automatic-control layers, nested states and clip names stay excluded");
            string left = Field<string>(folders[0], "Id"), right = Field<string>(folders[1], "Id"), other = Field<string>(folders[2], "Id");
            type.GetMethod("RemoveFolder").Invoke(window, new object[] { other });
            type.GetMethod("ImportFx").Invoke(window, null);
            Check(folders.Count == 2 && entries.Count == 2 && entries.Cast<object>().All(e => Field<AnimationClip>(e, "Clip") == eye),
                "Deleting an FX folder removes its clips immediately and FX import does not restore it");
            type.GetMethod("RenameFolder").Invoke(window, new object[] { left, "Smile" });
            string favorite = (string)type.GetMethod("AddFolder").Invoke(window, new object[] { "Favorites" });
            string firstEntry = Field<string>(entries[0], "Id");
            type.GetMethod("DropTreeItem").Invoke(window, new object[] { "Clip", firstEntry, "Folder", favorite, false });
            type.GetMethod("DropTreeItem").Invoke(window, new object[] { "Folder", favorite, "Folder", left, false });
            string faceCategory = Field<string>(categories[0], "Id");
            type.GetMethod("RenameCategory").Invoke(window, new object[] { faceCategory, "Face" });
            string customCategory = (string)type.GetMethod("AddCategory").Invoke(window, new object[] { "Custom" });
            type.GetMethod("DropTreeItem").Invoke(window, new object[] { "Folder", favorite, "Category", customCategory, true });
            type.GetMethod("DropTreeItem").Invoke(window, new object[] { "Category", customCategory, "Category", faceCategory, false });
            type.GetMethod("ImportFx").Invoke(window, null);
            Check(entries.Count == 2 && folders.Count == 3 && Field<string>(categories[0], "Id") == customCategory
                && Field<string>(entries[0], "FolderId") == favorite && folders.Cast<object>().Any(f => Field<string>(f, "Name") == "Smile"),
                "Drag moves and renamed folders survive FX import without duplicates");
            foreach (var entry in entries) { entry.GetType().GetField("Icon").SetValue(entry, icon); entry.GetType().GetField("Name").SetValue(entry, "Old manual label"); }
            type.GetField("folder", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, rootFolder + "/Custom_" + token);
            var result = type.GetMethod("CreateAndInstall").Invoke(window, new object[] { false });
            var prefab = Field<GameObject>(result, "Prefab");
            var root = prefab.GetComponent<ModularAvatarMenuInstaller>().menuToAppend;
            Check(prefab.name == "Expression" && AssetDatabase.GetAssetPath(prefab).EndsWith("/Expression.prefab")
                && clone.transform.Cast<Transform>().Any(t => t.name == "Expression"), "Expression is used for the prefab file, root and installed child");
            var hub = root.controls.Single().subMenu;
            Check(hub.controls.Select(c => c.name).SequenceEqual(new[] { "Favorites", "Right Hand" })
                && hub.controls.All(c => c.subMenu.controls.All(child => child.subMenu == null))
                && !Directory.GetFiles(Field<string>(result, "MenuFolder"), "Category*.asset").Any(),
                "Hub opens FX folders directly, with category order but no extra category submenu");
            var toggles = Controls(hub).Where(c => c.type == VRCExpressionsMenu.Control.ControlType.Toggle).ToArray();
            Check(toggles.All(c => c.name == eye.name && c.icon != null && c.icon != icon) && toggles.Select(c => c.value).SequenceEqual(new[] { 1f, 2f }),
                "Menu names and photos derive from the animation rather than old manual fields");
            Check(toggles[0].icon == toggles[1].icon && Directory.GetFiles(Field<string>(result, "IconFolder"), "*.png").Length == 2,
                "Duplicate left/right references reuse one captured PNG plus the parent icon");
            Check(root.controls[0].icon != toggles[0].icon && hub.controls.All(c => c.icon == root.controls[0].icon), "Parent menus retain their separate folder icon");
            var builtClone = Object.Instantiate(clone);
            try
            {
                AvatarProcessor.ProcessAvatar(builtClone);
                var builtHub = builtClone.GetComponent<VRCAvatarDescriptor>().expressionsMenu.controls.Single(c => c.name == "表情呼び出し").subMenu;
                Check(builtHub.controls.Select(c => c.name).SequenceEqual(new[] { "Favorites", "Right Hand" })
                    && Controls(builtHub).Count(c => c.type == VRCExpressionsMenu.Control.ControlType.Toggle) == 2,
                    "Modular Avatar preserves the three-level hub/folder/animation menu");
            }
            finally { Object.DestroyImmediate(builtClone); }
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            type.GetMethod("RemoveCategory").Invoke(window, new object[] { customCategory });
            Check(entries.Count == 1 && folders.Cast<object>().All(f => Field<string>(f, "Id") != favorite),
                "Category deletion removes its folders and clips in one operation without moving them elsewhere");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            categories = (IList)type.GetProperty("Categories").GetValue(window);
            folders = (IList)type.GetProperty("Folders").GetValue(window);
            entries = (IList)type.GetProperty("Entries").GetValue(window);
            Check(entries.Count == 2 && folders.Cast<object>().Any(f => Field<string>(f, "Id") == favorite)
                && categories.Cast<object>().Any(c => Field<string>(c, "Id") == customCategory), "One Undo restores the deleted category and its entire subtree");
            type.GetMethod("RemoveEntry").Invoke(window, new object[] { 1 });
            type.GetMethod("ImportFx").Invoke(window, null);
            Check(entries.Count == 1, "Deleted automatic clips stay deleted on import");
            var entryType = entries[0].GetType(); entries.Clear();
            for (int i = 0; i < 18; i++)
            {
                var item = System.Activator.CreateInstance(entryType);
                entryType.GetField("Clip").SetValue(item, eye); entryType.GetField("FolderId").SetValue(item, favorite); entries.Add(item);
            }
            var paged = type.GetMethod("CreateAndInstall").Invoke(window, new object[] { false });
            var menu = Field<GameObject>(paged, "Prefab").GetComponent<ModularAvatarMenuInstaller>().menuToAppend;
            Check(Controls(menu).Count(c => c.type == VRCExpressionsMenu.Control.ControlType.Toggle) == 18
                && Controls(menu).Where(c => c.subMenu != null).All(c => c.subMenu.controls.Count <= 8), "Large folders paginate without losing clips");
            Check(Controls(menu).Count(c => c.name == "解除") == 1, "A paged FX folder has Release on its first page only");
            foreach (var item in categories.Cast<object>().ToArray()) type.GetMethod("RemoveCategory").Invoke(window, new object[] { Field<string>(item, "Id") });
            type.GetMethod("EnsureFolders", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
            Check(entries.Count == 0 && folders.Count == 0 && categories.Count == 0, "Deleting the last category does not create a replacement folder or move any clips");
            string manualFolder = (string)type.GetMethod("AddFolder").Invoke(window, new object[] { "Manual" });
            var manual = System.Activator.CreateInstance(entryType); entryType.GetField("Clip").SetValue(manual, blink);
            entryType.GetField("FolderId").SetValue(manual, manualFolder); entries.Add(manual);
            type.GetMethod("CreateAndInstall").Invoke(window, new object[] { false });
            Check(true, "Explicitly added Blink clips can still be generated");
            entries.Clear(); folders.Clear(); categories.Clear();
            for (int c = 0; c < 10; c++)
            {
                string category = (string)type.GetMethod("AddCategory").Invoke(window, new object[] { "Category " + c });
                for (int f = 0; f < (c == 0 ? 10 : 1); f++)
                {
                    string itemFolder = (string)type.GetMethod("AddFolder").Invoke(window, new object[] { "Folder " + c + "/" + f });
                    type.GetMethod("MoveFolderToCategory").Invoke(window, new object[] { itemFolder, category });
                    var item = System.Activator.CreateInstance(entryType);
                    entryType.GetField("Clip").SetValue(item, eye); entryType.GetField("FolderId").SetValue(item, itemFolder); entries.Add(item);
                }
            }
            var overflow = type.GetMethod("CreateAndInstall").Invoke(window, new object[] { false });
            var overflowMenu = Field<GameObject>(overflow, "Prefab").GetComponent<ModularAvatarMenuInstaller>().menuToAppend;
            Check(Controls(overflowMenu).Count(c => c.type == VRCExpressionsMenu.Control.ControlType.Toggle) == 19
                && Controls(overflowMenu).Where(c => c.subMenu != null).All(c => c.subMenu.controls.Count <= 8)
                && !Controls(overflowMenu).Any(c => c.name.StartsWith("Category ")),
                "Many categories flatten into a paged FX folder index without extra category submenus");
        }
        finally { Object.DestroyImmediate(window); Object.DestroyImmediate(clone); Undo.ClearAll(); }
    }

    private static void VerifyPageBoundariesAndBudget(Type builder, VRCAvatarDescriptor avatar, AnimationClip clip, Texture2D icon)
    {
        var entryType = builder.Assembly.GetType(builder.Namespace + ".ExpressionMenuEntry");
        var folderType = builder.Assembly.GetType(builder.Namespace + ".ExpressionMenuFolder");
        foreach (int count in new[] { 1, 7, 8, 14, 15, 18, 255 })
        {
            var definition = System.Activator.CreateInstance(folderType);
            folderType.GetField("Name").SetValue(definition, "Left Hand");
            string id = Field<string>(definition, "Id");
            var folders = Array.CreateInstance(folderType, 1); folders.SetValue(definition, 0);
            var entries = Array.CreateInstance(entryType, count);
            for (int i = 0; i < count; i++)
            {
                var entry = System.Activator.CreateInstance(entryType);
                entryType.GetField("Clip").SetValue(entry, clip);
                entryType.GetField("Icon").SetValue(entry, icon);
                entryType.GetField("FolderId").SetValue(entry, id);
                entries.SetValue(entry, i);
            }
            var result = builder.GetMethod("BuildWithFolders").Invoke(null, new object[]
                { avatar, entries, "Budget" + count, rootFolder, "解除", "次へ", null, null, folders });
            var prefab = Field<GameObject>(result, "Prefab");
            var install = prefab.GetComponent<ModularAvatarMenuInstaller>().menuToAppend;
            var page = install.controls.Single().subMenu.controls.Single().subMenu;
            var pages = new List<VRCExpressionsMenu>();
            while (page != null)
            {
                pages.Add(page);
                page = page.controls.SingleOrDefault(c => c.type == VRCExpressionsMenu.Control.ControlType.SubMenu)?.subMenu;
            }
            Check(pages[0].controls.Count(c => c.name == "解除") == 1
                && pages.Skip(1).All(p => p.controls.All(c => c.name != "解除"))
                && pages.All(p => p.controls.Count <= 8), count + " clips: Release appears only on the first page, all pages fit eight slots");
            var selections = pages.SelectMany(p => p.controls).Where(c => c.type == VRCExpressionsMenu.Control.ControlType.Toggle).ToArray();
            Check(selections.Length == count && selections.Select(c => c.value).SequenceEqual(Enumerable.Range(1, count).Select(i => (float)i))
                && pages.Take(pages.Count - 1).All(p => p.controls.Count == 8), count + " clips: page slots are filled without duplicate or missing selections");
            if (count != 18 && count != 255) continue;
            var clone = Object.Instantiate(avatar.gameObject);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, clone.transform);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                AvatarProcessor.ProcessAvatar(clone);
                CheckParameterBudget(avatar, clone.GetComponent<VRCAvatarDescriptor>(), count + " expressions");
            }
            finally { Object.DestroyImmediate(clone); }
        }
    }

    private static void CheckParameterBudget(VRCAvatarDescriptor source, VRCAvatarDescriptor built, string label)
    {
        var originalNames = new HashSet<string>(source.expressionParameters.parameters.Select(p => p.name));
        var added = built.expressionParameters.parameters.Where(p => !string.IsNullOrEmpty(p.name) && !originalNames.Contains(p.name)).ToArray();
        int before = source.expressionParameters.CalcTotalCost(), after = built.expressionParameters.CalcTotalCost();
        Check(added.Length == 1 && added[0].networkSynced && !added[0].saved
            && added[0].valueType == VRCExpressionParameters.ValueType.Int && after - before == 8,
            label + ": MA output adds exactly one synced Int and 8 bits (" + before + " -> " + after + ")");
        Check(built.expressionParameters.parameters.All(p => p.name == null || !p.name.Contains("/Time/")),
            label + ": fixed-time Animator parameters never enter Expression Parameters");
    }

    private static void VerifyMergedHub(VRCAvatarDescriptor avatar, GameObject prefab, Texture2D icon, Dictionary<string, string> hashes)
    {
        var clone = Object.Instantiate(avatar.gameObject);
        try
        {
            clone.name = "Merged hub fixture";
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, clone.transform);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            AvatarProcessor.ProcessAvatar(clone);
            var built = clone.GetComponent<VRCAvatarDescriptor>();
            var root = built.expressionsMenu;
            Check(root.controls.Count == 2 && root.controls.Any(c => c.name == "Existing item")
                && root.controls.Count(c => c.name == "表情呼び出し") == 1,
                "MA merge preserves the existing root menu and adds only one expression hub");
            var hub = root.controls.Single(c => c.name == "表情呼び出し").subMenu;
            Check(hub != null && hub.controls.Select(c => c.name).SequenceEqual(new[] { "表情1", "表情2", "表情3" }),
                "MA merge keeps all expression groups beneath the hub");
            var toggles = Controls(hub).Where(c => c.type == VRCExpressionsMenu.Control.ControlType.Toggle).ToArray();
            // MA compresses PNG icons into build textures, so compare their rendered pixels rather than object identity.
            bool selectionsValid = toggles.Length == 16 && toggles.All(c => c.icon != null && c.icon == toggles[0].icon)
                && IconMatches(toggles[0].icon, icon)
                && toggles.Select(c => c.value).OrderBy(v => v).SequenceEqual(Enumerable.Range(1, 16).Select(v => (float)v))
                && toggles.Select(c => c.parameter.name).Distinct().Count() == 1
                && built.expressionParameters.parameters.Any(p => p.name == toggles[0].parameter.name);
            Check(selectionsValid, "MA merge preserves clip selectors, icons and the remapped expression parameter"
                + (selectionsValid ? "" : ": controls=" + toggles.Length
                    + "; icons=" + string.Join(",", toggles.Select(c => c.icon == icon ? "same" : AssetDatabase.GetAssetPath(c.icon)))
                    + "; values=" + string.Join(",", toggles.Select(c => c.value))
                    + "; menu parameters=" + string.Join(",", toggles.Select(c => c.parameter.name).Distinct())
                    + "; avatar parameters=" + string.Join(",", built.expressionParameters.parameters.Select(p => p.name))));
            CheckParameterBudget(avatar, built, "16 expressions across multiple folders");
            Check(hashes.All(pair => Hash(pair.Key) == pair.Value), "Hub integration leaves original avatar assets unchanged");
        }
        finally { Object.DestroyImmediate(clone); }
    }

    private static bool IconMatches(Texture2D actual, Texture2D expected)
    {
        if (actual == expected) return true;
        if (actual == null || actual.width != expected.width || actual.height != expected.height) return false;
        var a = IconPixels(actual); var b = IconPixels(expected);
        double error = 0;
        for (int i = 0; i < a.Length; i++)
            error += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g)
                + Mathf.Abs(a[i].b - b[i].b) + Mathf.Abs(a[i].a - b[i].a);
        return error / (a.Length * 4) < .02;
    }

    private static Color[] IconPixels(Texture2D texture)
    {
        var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(texture, target); RenderTexture.active = target;
            copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); copy.Apply();
            return copy.GetPixels();
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(copy); }
    }

    private static void VerifyWindow(Type builder, VRCAvatarDescriptor avatar, GameObject prefab)
    {
        var type = builder.Assembly.GetType(builder.Namespace + ".ExpressionMenuWindow");
        var window = (EditorWindow)ScriptableObject.CreateInstance(type);
        try
        {
            type.GetMethod("SetAvatar").Invoke(window, new object[] { avatar });
            var folder = (string)type.GetField("folder", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            Check(folder == "Assets/PPAE/" + avatar.name, "Window uses the avatar-specific PPAE default folder");
            type.GetField("generatedPrefab", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, prefab);
            int before = avatar.transform.childCount;
            var instance = (GameObject)type.GetMethod("Install").Invoke(window, null);
            Check(instance != null && instance.transform.parent == avatar.transform && PrefabUtility.IsPartOfPrefabInstance(instance), "Window installs a connected prefab under the selected avatar");
            Check(type.GetMethod("Install").Invoke(window, null) == (object)instance && avatar.transform.childCount == before + 1, "Repeated install is idempotent and never adds a second tracking owner");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Check(avatar.transform.childCount == before, "Undo removes only the newly installed module");
            Undo.PerformRedo();
            Check(avatar.transform.childCount == before + 1, "Redo reinstalls the generated module");
            Undo.PerformUndo(); Undo.ClearAll();
            window.position = new Rect(20, 20, 900, 560);
            window.Show();
            window.SendEvent(new Event { type = EventType.Layout });
            window.SendEvent(new Event { type = EventType.Repaint });
            Check(window.titleContent.text.Length > 0, "Generator window lays out and repaints");
        }
        finally { window.Close(); }
    }
}
