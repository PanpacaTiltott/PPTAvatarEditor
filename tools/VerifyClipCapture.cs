// Run in a disposable Unity 2022.3 project with a graphics device. Never run against a production scene.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

public static class VerifyClipCapture
{
    [Serializable] private sealed class Report { public List<string> passed = new List<string>(); public List<string> failed = new List<string>(); }
    private static readonly Report report = new Report();
    private const string Output = "../capture-results";
    private static string prefix, rootFolder;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool value, string label) { (value ? report.passed : report.failed).Add(prefix + label); File.AppendAllText(Output + "/progress.txt", (value ? "PASS " : "FAIL ") + prefix + label + "\n"); }
    private static object Call(Type type, string name, params object[] args) => type.GetMethod(name).Invoke(null, args);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name).SetValue(target, value);
    private static object Get(object target, string name) => target.GetType().GetField(name).GetValue(target);
    private static bool Fails(Action action) { try { action(); return false; } catch { return true; } }

    public static void RunBatch()
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/progress.txt", "");
        Application.logMessageReceived += Log;
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            rootFolder = "Assets/CaptureVerification_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(rootFolder));
            foreach (string ns in new[] { "Panpaca.PPAE", "PPTAvatarEditor" })
            {
                prefix = ns + ": ";
                var session = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(ns + ".ClipCaptureSession")).FirstOrDefault(t => t != null);
                Check(session != null, "capture assembly is available");
                if (session != null) Verify(session);
            }
        }
        catch (Exception ex) { report.failed.Add(ex.ToString()); }
        finally
        {
            Application.logMessageReceived -= Log;
            File.WriteAllText(Output + "/results.json", JsonUtility.ToJson(report, true));
            Debug.Log("Clip capture: " + report.passed.Count + " passed; " + report.failed.Count + " failed.");
            EditorApplication.Exit(report.failed.Count == 0 ? 0 : 1);
        }
    }

    private static void Log(string text, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Assert || text.Contains("Invalid GUILayout")
            || text.Contains("Getting control") || text.Contains("leaking the Preview scene"))
            report.failed.Add(prefix + "Unity: " + text);
    }

    private static void Verify(Type sessionType)
    {
        string ns = sessionType.Namespace;
        var settingsType = sessionType.Assembly.GetType(ns + ".ClipCaptureSettings");
        var files = sessionType.Assembly.GetType(ns + ".ClipCaptureFiles");
        var settings = Activator.CreateInstance(settingsType);
        Set(settings, "Width", 192); Set(settings, "Height", 128);
        var root = new GameObject("Capture Test Avatar");
        var avatar = root.AddComponent<VRCAvatarDescriptor>(); avatar.ViewPosition = new Vector3(0, 1, 0);
        var body = Mesh(root, "Body"); var clothing = Mesh(root, "Clothing");
        clothing.transform.localPosition = new Vector3(2, 0, 0);
        avatar.VisemeSkinnedMesh = body;
        body.SetBlendShapeWeight(0, 12);
        var faceClip = new AnimationClip { name = "Same name" };
        faceClip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Expression", AnimationCurve.Linear(0, 0, 1, 100));
        var neutral = new AnimationClip { name = "Same name" };
        neutral.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Expression", AnimationCurve.Constant(0, 1, 0));
        var clothClip = new AnimationClip { name = "Clothing only" };
        clothClip.SetCurve("Clothing", typeof(SkinnedMeshRenderer), "blendShape.Expression", AnimationCurve.Constant(0, 1, 100));
        var mixed = Object.Instantiate(faceClip); mixed.name = "Mixed";
        mixed.SetCurve("Body", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 1));
        var blank = new AnimationClip { name = "Empty" };
        string tag = ns.Replace(".", "_");
        foreach (var item in new[] { faceClip, neutral, clothClip, mixed, blank })
            AssetDatabase.CreateAsset(item, AssetDatabase.GenerateUniqueAssetPath(rootFolder + "/" + tag + "_" + item.name + ".anim"));
        faceClip.name = neutral.name = "Same name";
        EditorUtility.SetDirty(faceClip); EditorUtility.SetDirty(neutral);
        AssetDatabase.SaveAssets();
        var controller = AnimatorController.CreateAnimatorControllerAtPath(rootFolder + "/" + tag + ".controller");
        var sm = controller.layers[0].stateMachine;
        sm.AddState("Happy").motion = faceClip;
        sm.AddState("Duplicate").motion = faceClip;
        sm.AddState("Outfit").motion = clothClip;
        sm.AddState("Mixed").motion = mixed;
        sm.AddState("Blank").motion = blank;
        var nested = sm.AddStateMachine("Nested");
        var tree = new BlendTree { name = "Face tree" }; AssetDatabase.AddObjectToAsset(tree, controller); tree.AddChild(neutral);
        nested.AddState("Neutral").motion = tree;
        avatar.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer
            { type = VRCAvatarDescriptor.AnimLayerType.FX, animatorController = controller } };
        var scene = root.scene;
        EditorSceneManager.SaveScene(scene, rootFolder + "/" + tag + ".unity");
        string beforeSource = EditorJsonUtility.ToJson(avatar);
        var sourceMaterial = body.sharedMaterial;
        string beforeMaterial = EditorJsonUtility.ToJson(sourceMaterial);
        int previewScenes = EditorSceneManager.previewSceneCount;
        var targets = (AnimationClip[])Call(files, "ExpressionClips", avatar);
        Check(targets.SequenceEqual(new[] { faceClip, neutral }), "FX detects face clips across nested BlendTrees and deduplicates; excludes clothing/mixed/empty");
        var overrides = new AnimatorOverrideController(controller);
        overrides[faceClip] = neutral;
        avatar.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer
            { type = VRCAvatarDescriptor.AnimLayerType.FX, animatorController = overrides } };
        Check(((AnimationClip[])Call(files, "ExpressionClips", avatar)).SequenceEqual(new[] { neutral }), "FX uses effective override clips");
        avatar.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer
            { type = VRCAvatarDescriptor.AnimLayerType.FX, animatorController = controller } };
        Object.DestroyImmediate(overrides);

        object session = Activator.CreateInstance(sessionType, new object[] { avatar, faceClip });
        try
        {
            Set(settings, "SampleTime", .5f);
            var png = (Texture2D)sessionType.GetMethod("Capture").Invoke(session, new[] { settings, (object)192, 128 });
            try
            {
                Check(png.width == 192 && png.height == 128 && png.format == TextureFormat.RGBA32, "exact non-square RGBA output");
                var pixels = png.GetPixels32();
                Check(pixels.Any(p => p.a == 0) && pixels.Any(p => p.a > 250), "PNG contains transparent background and visible mesh");
                File.WriteAllBytes(Output + "/" + tag + "_transparent.png", png.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(png); }
            var clone = (GameObject)sessionType.GetProperty("Clone").GetValue(session);
            Check(Mathf.Abs(clone.transform.Find("Body").GetComponent<SkinnedMeshRenderer>().GetBlendShapeWeight(0) - 50) < .01,
                "sample time is evaluated on the capture clone");
            Set(settings, "Transparent", false); Set(settings, "Background", Color.green);
            png = (Texture2D)sessionType.GetMethod("Capture").Invoke(session, new[] { settings, (object)192, 128 });
            try { Check(png.GetPixel(0, 0).g > .95f && png.GetPixel(0, 0).a > .99f, "solid background is opaque and uses chosen color"); }
            finally { Object.DestroyImmediate(png); }
        }
        finally { ((IDisposable)session).Dispose(); }
        Set(settings, "Transparent", true);
        string folder = rootFolder + "/" + tag + "_images";
        var result = Call(files, "Save", avatar, new[] { faceClip, neutral, faceClip }, settings, folder, null);
        var paths = (List<string>)Get(result, "Paths"); var errors = (List<string>)Get(result, "Errors");
        Check(paths.Count == 2 && errors.Count == 0 && paths.Distinct().Count() == 2, "batch writes one PNG per unique clip, including identical clip names");
        if (paths.Count == 2)
        {
            Check(!File.ReadAllBytes(paths[0]).SequenceEqual(File.ReadAllBytes(paths[1])), "batch expressions produce different pixels without pose carryover");
            byte[] before = File.ReadAllBytes(paths[0]);
            var second = Call(files, "Save", avatar, new[] { faceClip }, settings, folder, null);
            var newPaths = (List<string>)Get(second, "Paths");
            Check(newPaths.Count == 1 && newPaths[0] != paths[0] && File.ReadAllBytes(paths[0]).SequenceEqual(before), "repeat captures preserve existing PNG files");
            var icons = (IDictionary)Call(files, "FindIcons", avatar, new[] { faceClip, neutral });
            Check(icons.Count == 2 && AssetDatabase.GetAssetPath((Texture2D)icons[faceClip]) == newPaths[0]
                && AssetDatabase.GetAssetPath((Texture2D)icons[neutral]) == paths[1], "icon lookup matches clip GUID/local ID and newest capture");
        }
        var cancelled = Call(files, "Save", avatar, new[] { faceClip, neutral }, settings, folder,
            new Func<int, int, AnimationClip, bool>((i, total, clip) => i == 1));
        Check((bool)Get(cancelled, "Cancelled") && ((List<string>)Get(cancelled, "Paths")).Count == 1, "cancel preserves completed files and stops before next clip");
        Check(Fails(() => Call(files, "Save", avatar, new[] { faceClip }, settings, "../outside", null)), "output outside Assets is rejected");
        Set(settings, "Width", 9000);
        Check(Fails(() => Call(files, "Save", avatar, new[] { faceClip }, settings, folder, null)), "invalid dimensions are rejected before writing");
        Set(settings, "Width", 192);
        Check((string)Call(files, "SafeName", "CON") == "_CON" && (string)Call(files, "SafeName", "../a:b") == ".._a_b", "output names are sanitized including Windows device names");
        Check((string)Call(files, "DefaultFolder", "Avatar") == "Assets/PPAE/Avatar/Pictures", "avatar-specific default output folder");
        var clipFiles = sessionType.Assembly.GetType(ns + ".ClipFiles") ?? sessionType.Assembly.GetType(ns + ".Utilities.ExpressionAssetUtility");
        string animationFolder = (string)clipFiles.GetMethod("DefaultFolder", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { "Avatar" });
        Check(animationFolder.TrimEnd('/') == "Assets/PPAE/Avatar/Animation", "new editor clips default to the separate Animation folder");
        Check(body.GetBlendShapeWeight(0) == 12 && EditorJsonUtility.ToJson(avatar) == beforeSource
            && EditorJsonUtility.ToJson(sourceMaterial) == beforeMaterial, "source avatar, shape values, and material unchanged");
        Check(!scene.isDirty, "capture leaves the saved source scene clean");
        Check(EditorSceneManager.previewSceneCount == previewScenes, "preview scenes are disposed after batch and cancellation");
        VerifyWindow(sessionType.Assembly.GetType(ns + ".ClipCaptureWindow"), avatar, new[] { faceClip, neutral }, settings);
        var latestIcons = (IDictionary)Call(files, "FindIcons", avatar, new[] { faceClip, neutral });
        VerifyMenu(ns, avatar, faceClip, neutral, (Texture2D)latestIcons[faceClip]);
        VerifyActual(sessionType, settingsType, ns);
        Object.DestroyImmediate(root);
    }

    private static SkinnedMeshRenderer Mesh(GameObject root, string name)
    {
        var go = new GameObject(name); go.transform.SetParent(root.transform, false);
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-.10f, .9f, 0), new Vector3(.10f, .9f, 0), new Vector3(.10f, 1.1f, 0), new Vector3(-.10f, 1.1f, 0) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 };
        mesh.AddBlendShapeFrame("Expression", 100, new[] { Vector3.left * .08f, Vector3.right * .08f, Vector3.right * .08f, Vector3.left * .08f }, new Vector3[4], new Vector3[4]);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var smr = go.AddComponent<SkinnedMeshRenderer>(); smr.sharedMesh = mesh;
        smr.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.red };
        return smr;
    }

    private static void VerifyMenu(string ns, VRCAvatarDescriptor avatar, AnimationClip first, AnimationClip second, Texture2D image)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(ns + ".Modules.ExpressionMenuWindow")).FirstOrDefault(t => t != null);
        if (type == null) { Check(false, "Expression Menu optional assembly present for integration test"); return; }
        var window = (EditorWindow)ScriptableObject.CreateInstance(type);
        try
        {
            type.GetMethod("SetAvatar").Invoke(window, new object[] { avatar });
            var entries = (IList)type.GetField("entries", Private).GetValue(window);
            Check(entries.Count == 2, "Expression Menu auto-registers FX face clips on avatar selection");
            var entry = entries.Cast<object>().First(e => (AnimationClip)Get(e, "Clip") == first);
            Check((Texture2D)Get(entry, "Icon") == image, "Expression Menu assigns captured PNG automatically");
            Check(Mathf.Abs((float)Get(entry, "Time") - .5f) < .001f, "auto icon also imports its captured expression time");
            Set(entry, "Time", .25f);
            type.GetMethod("RefreshIcons").Invoke(window, null);
            Check(Mathf.Abs((float)Get(entry, "Time") - .25f) < .001f, "refreshing an unchanged icon preserves manually edited sample time");
            var manualIcon = new Texture2D(4, 4); Set(entry, "Icon", manualIcon); Set(entry, "Name", "Manual");
            type.GetMethod("ImportFx").Invoke(window, null);
            Check(entries.Count == 2 && (Texture2D)Get(entry, "Icon") == image && (string)Get(entry, "Name") == first.name,
                "FX reload derives clip names and images from their animation without duplicates");
            Set(entry, "Icon", image); Object.DestroyImmediate(manualIcon);
            var files = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(ns + ".ClipCaptureFiles")).First(t => t != null);
            var settingsType = files.Assembly.GetType(ns + ".ClipCaptureSettings");
            var settings = Activator.CreateInstance(settingsType);
            Set(settings, "Width", 64); Set(settings, "Height", 64); Set(settings, "SampleTime", .75f);
            Call(files, "Save", avatar, new[] { first }, settings, rootFolder + "/" + ns.Replace(".", "_") + "_late", null);
            var refreshed = (Texture2D)Get(entry, "Icon");
            Check(refreshed != image && Mathf.Abs((float)Get(entry, "Time") - .75f) < .001,
                "new capture updates open Expression Menu icon and fixed time");
            image = refreshed;
            type.GetField("folder", Private).SetValue(window, rootFolder + "/" + ns.Replace(".", "_") + "_Menu");
            var framing = type.GetField("framing", Private).GetValue(window);
            Set(framing, "FieldOfView", 47f); Set(framing, "Yaw", 20f);
            int childCount = avatar.transform.childCount;
            var result = type.GetMethod("CreateAndInstall").Invoke(window, new object[] { false });
            var prefab = (GameObject)type.GetField("generatedPrefab", Private).GetValue(window);
            Check(prefab != null, "FX-derived entries generate a prefab");
            Check(prefab != null && prefab.name == "Expression" && AssetDatabase.GetAssetPath(prefab).EndsWith("/Expression.prefab"),
                "Generated prefab root and asset are named Expression");
            if (prefab != null)
            {
                string dir = (string)Get(result, "MenuFolder");
                var menus = AssetDatabase.FindAssets("t:VRCExpressionsMenu", new[] { dir })
                    .Select(g => AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(AssetDatabase.GUIDToAssetPath(g)));
                var captured = (Texture2D)Get(entry, "Icon");
                Check(captured != image && captured.width == 256 && captured.height == 256
                    && menus.Any(menu => menu != null && menu.controls.Any(c => c.icon == captured)),
                    "generation automatically captures and assigns fresh 256px icons");
                Check(Mathf.Abs((float)Call(files, "CapturedTime", captured) - .75f) < .001f,
                    "automatic icon uses exactly the controller's selected clip time");
                var sessionType = files.Assembly.GetType(ns + ".ClipCaptureSession");
                var cameraSettings = JsonUtility.FromJson(JsonUtility.ToJson(framing), settingsType);
                Set(cameraSettings, "SampleTime", (float)Get(entry, "Time"));
                using (var captureSession = (IDisposable)Activator.CreateInstance(sessionType, new object[] { avatar, first }))
                {
                    var expected = (Texture2D)sessionType.GetMethod("Capture").Invoke(captureSession, new[] { cameraSettings, (object)256, 256 });
                    Check(File.ReadAllBytes(AssetDatabase.GetAssetPath(captured)).SequenceEqual(expected.EncodeToPNG()),
                        "Automatic icon matches the menu preview camera including its non-default field of view");
                    Object.DestroyImmediate(expected);
                }
                var installMenu = menus.Single(menu => menu.name == "Install");
                var hubIcon = installMenu.controls[0].icon;
                Check(hubIcon != null && hubIcon != captured && AssetDatabase.GetAssetPath(hubIcon).EndsWith("/ExpressionFolder.png")
                    && installMenu.controls[0].subMenu.controls.All(c => c.icon == hubIcon),
                    "Hub and expression folders use the dedicated folder icon rather than an avatar photo");
                var instance = avatar.transform.Cast<Transform>().Single(t => PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == prefab).gameObject;
                Check(avatar.transform.childCount == childCount + 1 && instance != null, "generation immediately installs the connected prefab inside the avatar");
                Check(instance.name == "Expression", "Installed avatar child is named Expression");
                var again = type.GetMethod("CreateAndInstall").Invoke(window, new object[] { false });
                var nextPrefab = (GameObject)Get(again, "Prefab");
                Check(avatar.transform.childCount == childCount + 1
                    && avatar.transform.Cast<Transform>().Any(t => PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == nextPrefab),
                    "regeneration replaces the prior standard module without duplicate tracking owners");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(avatar.transform.childCount == childCount + 1
                    && avatar.transform.Cast<Transform>().Any(t => PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == prefab),
                    "Undo of regeneration restores the previous connected prefab");
                Undo.PerformUndo();
                Check(avatar.transform.childCount == childCount, "Undo of first generation removes only the added module");
                Undo.ClearAll();
            }
            window.ShowUtility();
            framing = type.GetField("framing", Private).GetValue(window);
            foreach (var size in new[] { new Vector2(900, 560), new Vector2(1280, 800) })
            {
                window.position = new Rect(20, 20, size.x, size.y);
                type.GetMethod("SelectEntry").Invoke(window, new object[] { 1 });
                window.SendEvent(new Event { type = EventType.Layout });
                window.SendEvent(new Event { type = EventType.Repaint });
                var previewRect = (Rect)type.GetProperty("PreviewRect").GetValue(window);
                var firstRect = (Rect)type.GetProperty("FirstConfirmRect").GetValue(window);
                Check(previewRect.width > 100 && firstRect.width > 30 && firstRect.x > previewRect.xMax
                    && firstRect.yMin > 75 && firstRect.yMax < size.y, "menu preview and entry button are visibly separated at " + size);
                window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = firstRect.center });
                window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = firstRect.center });
                Check((AnimationClip)type.GetProperty("SelectedClip").GetValue(window) == first, "menu Preview button selects the corresponding expression at " + size);
                window.SendEvent(new Event { type = EventType.Layout });
                window.SendEvent(new Event { type = EventType.Repaint });
                Check(type.GetField("preview", Private).GetValue(window) is Texture2D
                    && string.IsNullOrEmpty((string)type.GetField("error", Private).GetValue(window))
                    && string.IsNullOrEmpty((string)type.GetField("previewError", Private).GetValue(window)),
                    "menu preview renders the selected expression without UI errors at " + size);
                var fovRect = (Rect)type.GetProperty("FieldOfViewRect").GetValue(window);
                Check(fovRect.width > 150 && fovRect.yMin > previewRect.yMax && fovRect.yMax < size.y - 40,
                    "Menu field-of-view control fits below the preview at " + size);
                Set(framing, "FieldOfView", 30f);
                var point = new Vector2(fovRect.x + 90 + (fovRect.width - 145) * .8f, fovRect.center.y);
                window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = point });
                window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = point });
                window.SendEvent(new Event { type = EventType.Layout });
                window.SendEvent(new Event { type = EventType.Repaint });
                Check((float)Get(framing, "FieldOfView") > 40 && type.GetField("preview", Private).GetValue(window) is Texture2D,
                    "Dragging the menu FOV control updates camera and preview at " + size);

            }
            VerifyTreeDrag(type, window);
            framing = type.GetField("framing", Private).GetValue(window);
            settingsType.GetMethod("ResetCamera").Invoke(framing, null);
            Check((float)Get(framing, "FieldOfView") == 30, "Menu camera reset restores the default 30 degree field of view");
        }
        finally { window.Close(); }
    }

    private static void VerifyTreeDrag(Type type, EditorWindow window)
    {
        Func<object[]> rows = () =>
        {
            window.SendEvent(new Event { type = EventType.Layout });
            window.SendEvent(new Event { type = EventType.Repaint });
            return ((IEnumerable)type.GetField("treeRows", Private).GetValue(window)).Cast<object>().ToArray();
        };
        Func<IList> entries = () => (IList)type.GetProperty("Entries").GetValue(window);
        Func<IList> folders = () => (IList)type.GetProperty("Folders").GetValue(window);
        Func<IList> categories = () => (IList)type.GetProperty("Categories").GetValue(window);
        Action<string, string, bool> drag = (sourceId, targetId, after) =>
        {
            var painted = rows();
            var source = painted.Single(r => (string)Get(r, "Id") == sourceId);
            var target = painted.Single(r => (string)Get(r, "Id") == targetId);
            var handle = (Rect)Get(source, "Handle"); var rect = (Rect)Get(target, "Rect");
            Vector2 drop = new Vector2(rect.center.x, after ? rect.yMax - 3 : rect.y + 3);
            Check(handle.width >= 10 && rect.width >= 100, "Tree drag handle and target row have visible geometry");
            window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = handle.center });
            window.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = handle.center + new Vector2(8, 8), delta = new Vector2(8, 8) });
            window.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = drop });
            window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = drop });
            rows();
        };
        string first = (string)Get(entries()[0], "Id"), second = (string)Get(entries()[1], "Id");
        string initialCategory = (string)Get(categories()[0], "Id");
        string targetFolder = (string)type.GetMethod("AddFolder").Invoke(window, new object[] { "Drop here" });
        string targetCategory = (string)type.GetMethod("AddCategory").Invoke(window, new object[] { "Dragged category" });
        drag(second, targetFolder, true);
        Check((string)Get(entries().Cast<object>().Single(e => (string)Get(e, "Id") == second), "FolderId") == targetFolder,
            "Dragging a clip handle onto an empty folder moves it to that folder");
        drag(first, second, true);
        var order = entries().Cast<object>().Where(e => (string)Get(e, "FolderId") == targetFolder).Select(e => (string)Get(e, "Id")).ToArray();
        Check(order.SequenceEqual(new[] { second, first }), "Dropping beneath another clip reorders clips and crosses folders");
        drag(targetFolder, targetCategory, true);
        Check((string)Get(folders().Cast<object>().Single(f => (string)Get(f, "Id") == targetFolder), "CategoryId") == targetCategory,
            "Dragging an FX folder onto a category moves the entire folder");
        drag(targetCategory, initialCategory, false);
        Check((string)Get(categories()[0], "Id") == targetCategory, "Category handles reorder categories by drag and drop");
        var folderRow = rows().Single(r => (string)Get(r, "Id") == targetFolder);
        var folderRect = (Rect)Get(folderRow, "Rect");
        var delete = new Vector2(folderRect.xMax - 24, folderRect.center.y);
        Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
        window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = delete });
        window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = delete });
        Check(entries().Count == 0 && folders().Cast<object>().All(f => (string)Get(f, "Id") != targetFolder),
            "The folder's Delete button removes the folder and clips with one click");
        Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); rows();
        Check(entries().Count == 2 && folders().Cast<object>().Any(f => (string)Get(f, "Id") == targetFolder),
            "Undo restores one-click folder deletion and its clip ordering");
    }

    private static void VerifyWindow(Type type, VRCAvatarDescriptor avatar, AnimationClip[] clips, object settings)
    {
        var window = (EditorWindow)ScriptableObject.CreateInstance(type);
        try
        {
            type.GetMethod("SetAvatar").Invoke(window, new object[] { avatar });
            type.GetMethod("AddClips").Invoke(window, new object[] { clips });
            type.GetMethod("AddClips").Invoke(window, new object[] { clips });
            Check(((IEnumerable)type.GetProperty("Clips").GetValue(window)).Cast<object>().Count() == 2, "capture window deduplicates clip additions");
            type.GetMethod("AddFromFx").Invoke(window, new object[] { -1 });
            Check((int)type.GetProperty("LastAddedCount").GetValue(window) > 0
                && ((IEnumerable)type.GetProperty("Clips").GetValue(window)).Cast<object>().Count() > 2,
                "Add from FX adds remaining clips and reports their count");
            type.GetField("settings", Private).SetValue(window, settings);
            window.ShowUtility();
            foreach (var size in new[] { new Vector2(800, 520), new Vector2(1200, 800) })
            {
                window.position = new Rect(20, 20, size.x, size.y);
                type.GetMethod("SelectClip").Invoke(window, new object[] { 1 });
                type.GetField("listScroll", Private).SetValue(window, Vector2.zero);
                window.SendEvent(new Event { type = EventType.Layout });
                window.SendEvent(new Event { type = EventType.Repaint });
                var preview = (Rect)type.GetProperty("PreviewRect").GetValue(window);
                var list = (Rect)type.GetProperty("ClipListRect").GetValue(window);
                Debug.Log(prefix + "Capture geometry at " + size + ": preview " + preview + "; list " + list);
                Check(preview.width > 100 && preview.height > 100 && preview.xMax <= list.x
                    && list.height >= 100 && list.yMax < size.y, "capture window layout at " + size);
                var buttons = (Rect)type.GetProperty("CaptureButtonsRect").GetValue(window);
                Check(buttons.height >= 24 && buttons.yMax <= size.y && size.y - buttons.yMax <= 20
                    && list.height >= size.y - 260, "Capture list uses spare height and buttons sit near the bottom at " + size);
                Check(string.IsNullOrEmpty((string)type.GetField("error", Private).GetValue(window)), "capture preview renders without UI error at " + size);
                var firstRect = (Rect)type.GetProperty("FirstClipRect").GetValue(window);
                Check(firstRect.width > 100 && firstRect.height > 15 && list.Contains(firstRect.center),
                    "actual first clip row is visible inside the capture list at " + size);
                window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = firstRect.center });
                window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = firstRect.center });
                Check((AnimationClip)type.GetProperty("SelectedClip").GetValue(window) == clips[0],
                    "clicking the visible capture row changes the selected clip at " + size);
            }
        }
        finally { window.Close(); }
    }

    private static void VerifyActual(Type sessionType, Type settingsType, string ns)
    {
        const string folder = "Assets/CaptureAvatar/";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "Kipfel_Mobile.fbx");
        if (model == null) return; // Optional licensed local fixture, never distributed with the test.
        var source = Object.Instantiate(model);
        try
        {
            var avatar = source.AddComponent<VRCAvatarDescriptor>();
            var animator = source.GetComponent<Animator>(); animator.enabled = false;
            Check(animator.isHuman, "actual Kipfel model is Humanoid");
            var material = new Material(Shader.Find("Unlit/Texture"))
            { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "kipfel_mobile.png") };
            foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => material).ToArray();
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            avatar.ViewPosition = source.transform.InverseTransformPoint(head.position);
            avatar.VisemeSkinnedMesh = source.GetComponentsInChildren<SkinnedMeshRenderer>().First();
            var settings = Activator.CreateInstance(settingsType);
            foreach (string file in new[] { "kipfel_facial_happy.anim", "kipfel_facial_unhappy.anim" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(folder + file);
                object session = Activator.CreateInstance(sessionType, new object[] { avatar, clip });
                try
                {
                    var texture = (Texture2D)sessionType.GetMethod("Capture").Invoke(session, new[] { settings, (object)512, 512 });
                    File.WriteAllBytes(Output + "/" + ns.Replace(".", "_") + "_" + file + ".png", texture.EncodeToPNG());
                    Check(texture.GetPixels32().Count(p => p.a > 0) > 10000, "actual avatar is visible in " + file);
                    Object.DestroyImmediate(texture);
                    var clone = (GameObject)sessionType.GetProperty("Clone").GetValue(session);
                    var clonedHead = clone.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
                    Check(Mathf.Abs(clone.transform.InverseTransformPoint(clonedHead.position).y - avatar.ViewPosition.y) < .0001,
                        "Humanoid head height preserved for " + file);
                    int matched = 0;
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(b => b.propertyName.StartsWith("blendShape.")))
                        if (AnimationUtility.GetFloatValue(clone, binding, out float value)
                            && Mathf.Abs(value - AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0)) < .01f) matched++;
                    Check(matched > 0, "actual expression curves sampled for " + file);
                }
                finally { ((IDisposable)session).Dispose(); }
            }
            Object.DestroyImmediate(material);
        }
        finally { Object.DestroyImmediate(source); }
    }
}
