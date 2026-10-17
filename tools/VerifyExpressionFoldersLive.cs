// Read-only verification of loaded scene avatars. Not distributed in the package.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[InitializeOnLoad]
public static class VerifyExpressionFoldersLive
{
    private const string Request = "Library/PPAEMenuBudget.request";
    [Serializable] private sealed class Report
    {
        public string unity;
        public List<string> passed = new List<string>(), failed = new List<string>();
        public List<AvatarReport> avatars = new List<AvatarReport>();
    }
    [Serializable] private sealed class AvatarReport
    {
        public string edition, avatar, scene;
        public int excludedSources;
        public int baseExpressionBits, ppaeModuleCount, ppaeAdditionalSyncedBits;
        public int[] ppaeAnimatorParameters;
        public List<FolderReport> folders = new List<FolderReport>();
    }
    [Serializable] private sealed class FolderReport
    {
        public string name;
        public int[] layers;
        public string[] clips;
    }
    static VerifyExpressionFoldersLive() { EditorApplication.update += Poll; }
    private static object Get(object value, string field) => value.GetType().GetField(field).GetValue(value);
    private static string Hash(string path) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }
    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string output = File.ReadAllText(Request).Trim(); File.Delete(Request);
        var report = new Report { unity = Application.unityVersion };
        try
        {
            var avatars = Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>().Where(a => !EditorUtility.IsPersistent(a)
                && a.gameObject.scene.IsValid() && a.gameObject.scene.path.StartsWith("Assets/", StringComparison.Ordinal)
                && !EditorSceneManager.IsPreviewScene(a.gameObject.scene)).ToArray();
            if (avatars.Length == 0) throw new Exception("No loaded user-scene avatars found.");
            foreach (var avatar in avatars)
            {
                bool dirty = avatar.gameObject.scene.isDirty;
                string descriptorBefore = EditorJsonUtility.ToJson(avatar);
                var weights = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null)
                    .ToDictionary(r => r, r => Enumerable.Range(0, r.sharedMesh.blendShapeCount).Select(r.GetBlendShapeWeight).ToArray());
                var filesToCheck = new HashSet<string>();
                foreach (string ns in new[] { "Panpaca.PPAE", "PPTAvatarEditor" })
                {
                    var files = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(ns + ".ClipCaptureFiles")).First(t => t != null);
                    var groups = ((IEnumerable)files.GetMethod("ExpressionGroups").Invoke(null, new object[] { avatar })).Cast<object>().ToArray();
                    var row = new AvatarReport { edition = ns, avatar = avatar.name, scene = avatar.gameObject.scene.path };
                    report.avatars.Add(row);
                    row.baseExpressionBits = avatar.expressionParameters != null ? avatar.expressionParameters.CalcTotalCost() : 0;
                    var moduleConfigs = avatar.GetComponentsInChildren<Component>(true)
                        .Where(c => c != null && c.GetType().FullName == "nadena.dev.modular_avatar.core.ModularAvatarParameters")
                        .Select(c => new { Component = c, Configs = ((IEnumerable)Get(c, "parameters")).Cast<object>().ToArray() })
                        .Where(c => c.Configs.Any(p => ((string)Get(p, "nameOrPrefix") ?? "").StartsWith("PPAE/Expression/", StringComparison.Ordinal))).ToArray();
                    row.ppaeModuleCount = moduleConfigs.Length;
                    row.ppaeAdditionalSyncedBits = moduleConfigs.Sum(c => c.Configs.Sum(p => (bool)Get(p, "localOnly") ? 0
                        : Get(p, "syncType").ToString() == "Bool" ? 1 : Get(p, "syncType").ToString() == "NotSynced" ? 0 : 8));
                    row.ppaeAnimatorParameters = moduleConfigs.Select(c => c.Component.GetComponents<Component>()
                        .FirstOrDefault(a => a != null && a.GetType().FullName == "nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator"))
                        .Where(c => c != null).Select(c => Get(c, "animator") as UnityEditor.Animations.AnimatorController)
                        .Where(c => c != null).Select(c => c.parameters.Length).ToArray();
                    bool lean = row.ppaeModuleCount <= 1 && moduleConfigs.All(c => c.Configs.Length == 1
                        && Get(c.Configs[0], "syncType").ToString() == "Int" && !(bool)Get(c.Configs[0], "localOnly"));
                    (lean ? report.passed : report.failed).Add(ns + ": " + avatar.name + " installed PPAE modules=" + row.ppaeModuleCount
                        + ", PPAE synced bits=" + row.ppaeAdditionalSyncedBits + ", base descriptor bits=" + row.baseExpressionBits);
                    row.excludedSources = ((IEnumerable)files.GetMethod("ExcludedAutomaticSources").Invoke(null, new object[] { avatar })).Cast<object>().Count();
                    foreach (var group in groups)
                    {
                        var clips = (AnimationClip[])Get(group, "Clips");
                        row.folders.Add(new FolderReport { name = (string)Get(group, "Name"), layers = ((IEnumerable<int>)Get(group, "Layers")).ToArray(),
                            clips = clips.Select(c => AssetDatabase.GetAssetPath(c)).ToArray() });
                        foreach (var clip in clips) filesToCheck.Add(AssetDatabase.GetAssetPath(clip));
                    }
                    (row.folders.All(f => f.layers.Length == 1) ? report.passed : report.failed)
                        .Add(ns + ": " + avatar.name + " every imported folder represents exactly one source FX layer");
                    if (avatar.name.IndexOf("Chocolat", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var left = row.folders.FirstOrDefault(f => f.name.Equals("Left Hand", StringComparison.OrdinalIgnoreCase));
                        var right = row.folders.FirstOrDefault(f => f.name.Equals("Right Hand", StringComparison.OrdinalIgnoreCase));
                        bool separate = left != null && right != null && left.clips.Length > 0 && right.clips.Length > 0
                            && !left.layers.Intersect(right.layers).Any();
                        (separate ? report.passed : report.failed).Add(ns + ": Chocolat Left Hand and Right Hand both appear as separate non-empty folders");
                    }
                    foreach (var layer in avatar.baseAnimationLayers)
                        if (layer.animatorController != null) filesToCheck.Add(AssetDatabase.GetAssetPath(layer.animatorController));
                    var hashes = filesToCheck.Where(File.Exists).ToDictionary(p => p, Hash);
                    var windowType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(ns + ".Modules.ExpressionMenuWindow")).First(t => t != null);
                    var window = (EditorWindow)ScriptableObject.CreateInstance(windowType);
                    try
                    {
                        windowType.GetMethod("SetAvatar").Invoke(window, new object[] { avatar });
                        var entries = ((IEnumerable)windowType.GetProperty("Entries").GetValue(window)).Cast<object>().ToArray();
                        var folders = ((IEnumerable)windowType.GetProperty("Folders").GetValue(window)).Cast<object>().ToArray();
                        var categories = ((IEnumerable)windowType.GetProperty("Categories").GetValue(window)).Cast<object>().ToArray();
                        bool categorized = categories.Length > 0 && folders.All(f => categories.Any(c => (string)Get(c, "Id") == (string)Get(f, "CategoryId")));
                        (categorized ? report.passed : report.failed).Add(ns + ": " + avatar.name + " every FX folder belongs to a UI category");
                        bool mapped = groups.All(g => folders.Any(f => (string)Get(f, "SourceKey") == (string)Get(g, "Key")
                            && entries.Where(e => (string)Get(e, "FolderId") == (string)Get(f, "Id")).Select(e => (AnimationClip)Get(e, "Clip"))
                                .SequenceEqual((AnimationClip[])Get(g, "Clips"))));
                        bool complete = entries.Length == groups.Sum(g => ((AnimationClip[])Get(g, "Clips")).Length);
                        (mapped && complete ? report.passed : report.failed).Add(ns + ": " + avatar.name + " FX folders match imported clips (" + groups.Length + " folders, " + entries.Length + " entries)");
                        windowType.GetMethod("ImportFx").Invoke(window, null);
                        int after = ((IEnumerable)windowType.GetProperty("Entries").GetValue(window)).Cast<object>().Count();
                        (after == entries.Length ? report.passed : report.failed).Add(ns + ": " + avatar.name + " reimport does not duplicate clips");
                    }
                    finally { Undo.ClearUndo(window); UnityEngine.Object.DestroyImmediate(window); }
                    (hashes.All(p => Hash(p.Key) == p.Value) ? report.passed : report.failed).Add(ns + ": " + avatar.name + " source FX/clip files unchanged");
                }
                bool unchanged = descriptorBefore == EditorJsonUtility.ToJson(avatar) && dirty == avatar.gameObject.scene.isDirty
                    && weights.All(p => p.Value.SequenceEqual(Enumerable.Range(0, p.Value.Length).Select(p.Key.GetBlendShapeWeight)));
                (unchanged ? report.passed : report.failed).Add(avatar.name + " descriptor, shape weights and scene dirty state unchanged");
            }
        }
        catch (Exception ex) { report.failed.Add(ex.ToString()); }
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(report, true));
        Debug.Log("PPAE live FX folder verification: " + report.passed.Count + " passed, " + report.failed.Count + " failed.");
    }
}
