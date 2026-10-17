// Copyright (C) 2026 PanpacaTiltott. Distributed under the Zlib License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace PPTAvatarEditor.Modules
{
    [Serializable]
    public sealed class ExpressionMenuCategory
    {
        public string Id = Guid.NewGuid().ToString("N"), Name = "表情";
        public Texture2D Icon;
        public bool Expanded = true;
    }

    [Serializable]
    public sealed class ExpressionMenuFolder
    {
        public string Id = Guid.NewGuid().ToString("N"), Name = "表情", SourceKey = "";
        public string CategoryId = "";
        public Texture2D Icon;
        public bool Expanded = true;
    }

    [Serializable]
    public sealed class ExpressionMenuEntry
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public AnimationClip Clip;
        public Texture2D Icon;
        [HideInInspector] public Texture2D AutoIcon;
        public float Time;
        public string FolderId = "", SourceKey = "";
        public bool Expanded;
        public bool UseClipMetadata;
    }

    public sealed class ExpressionMenuResult
    {
        public string Folder, ControllerFolder, MenuFolder, IconFolder, PrefabFolder;
        public string Parameter;
        public GameObject Prefab;
        public AnimatorController Controller;
    }

    public static class ExpressionMenuBuilder
    {
        public const int MaxExpressions = 255;
        public const int LayerPriority = 100;
        public const string ModulePrefix = "PPAE Expression Menu ";
        public const string HubMenuName = "表情呼び出し";
        public const string PrefabName = "Expression";
        private const string MenuIconPath = "PPAE/Icon/ExpressionFolder";

        public static ExpressionMenuResult Build(VRCAvatarDescriptor avatar, IList<ExpressionMenuEntry> entries,
            string menuName, string folder, string releaseLabel = "解除", string nextLabel = "次へ")
        {
            return BuildWithCapture(avatar, entries, menuName, folder, releaseLabel, nextLabel, null, null);
        }

        public static ExpressionMenuResult BuildWithCapture(VRCAvatarDescriptor avatar, IList<ExpressionMenuEntry> entries,
            string menuName, string folder, string releaseLabel, string nextLabel, ClipCaptureSettings framing,
            Func<int, int, string, bool> cancel)
        { return BuildWithFolders(avatar, entries, menuName, folder, releaseLabel, nextLabel, framing, cancel, null); }

        public static ExpressionMenuResult BuildWithFolders(VRCAvatarDescriptor avatar, IList<ExpressionMenuEntry> entries,
            string menuName, string folder, string releaseLabel, string nextLabel, ClipCaptureSettings framing,
            Func<int, int, string, bool> cancel, IList<ExpressionMenuFolder> folders)
        { return BuildWithHierarchy(avatar, entries, menuName, folder, releaseLabel, nextLabel, framing, cancel, folders, null); }

        public static ExpressionMenuResult BuildWithHierarchy(VRCAvatarDescriptor avatar, IList<ExpressionMenuEntry> entries,
            string menuName, string folder, string releaseLabel, string nextLabel, ClipCaptureSettings framing,
            Func<int, int, string, bool> cancel, IList<ExpressionMenuFolder> folders, IList<ExpressionMenuCategory> categories)
        {
            Validate(avatar, entries, menuName);
            folder = AssetFolder(folder);
            // Asset imports can repaint open windows. Keep the requested clip, time and icon stable.
            var inputs = entries.Select(e => new ExpressionMenuEntry
            { Name = e.Name, Clip = e.Clip, Icon = e.Icon, AutoIcon = e.AutoIcon, Time = e.Time, FolderId = e.FolderId, UseClipMetadata = e.UseClipMetadata }).ToArray();
            var menuFolders = folders?.Select(f => new ExpressionMenuFolder { Id = f.Id, Name = f.Name, CategoryId = f.CategoryId, Icon = f.Icon }).ToArray();
            var menuCategories = categories?.Select(c => new ExpressionMenuCategory { Id = c.Id, Name = c.Name, Icon = c.Icon }).ToArray();
            if (menuFolders != null && (menuFolders.Any(f => string.IsNullOrWhiteSpace(f.Name) || string.IsNullOrEmpty(f.Id))
                || menuFolders.Select(f => f.Id).Distinct().Count() != menuFolders.Length
                || inputs.Any(e => !menuFolders.Any(f => f.Id == e.FolderId))))
                throw new ArgumentException("表情のフォルダ名と割り当てを確認してください。");
            if (menuCategories != null && (menuFolders == null || menuCategories.Any(c => string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrEmpty(c.Id))
                || menuCategories.Select(c => c.Id).Distinct().Count() != menuCategories.Length
                || menuFolders.Any(f => !menuCategories.Any(c => c.Id == f.CategoryId))))
                throw new ArgumentException("カテゴリ名とフォルダの割り当てを確認してください。");
            var settings = framing == null ? new ClipCaptureSettings()
                : JsonUtility.FromJson<ClipCaptureSettings>(JsonUtility.ToJson(framing));
            settings.Width = settings.Height = 256;
            var created = new List<string>();
            GameObject root = null;
            Scene temporaryScene = default;
            try
            {
                string[] assetTypes = { "Controller", "Menu", "Icons", "Prefab" };
                string key = SafeName(menuName);
                int suffix = 0;
                while (assetTypes.Any(c => Directory.Exists(folder + "/" + c + "/" + key)
                    || File.Exists(folder + "/" + c + "/" + key)
                    || File.Exists(folder + "/" + c + "/" + key + ".meta")))
                    key = SafeName(menuName) + "_" + (++suffix).ToString("D2");
                foreach (string category in assetTypes)
                {
                    string parent = folder + "/" + category;
                    EnsureFolder(parent);
                    string path = parent + "/" + key;
                    if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, key)))
                        throw new IOException("保存フォルダを作成できませんでした。 " + path);
                    created.Add(path);
                }
                var result = new ExpressionMenuResult
                {
                    Folder = folder, ControllerFolder = created[0], MenuFolder = created[1],
                    IconFolder = created[2], PrefabFolder = created[3]
                };
                var menuIcon = CopyMenuIcon(result.IconFolder);
                var capturedIcons = new Dictionary<Tuple<AnimationClip, float>, Texture2D>();
                for (int i = 0; i < inputs.Length; i++)
                {
                    if (cancel != null && cancel(i, inputs.Length, EntryName(inputs[i])))
                        throw new OperationCanceledException();
                    if (!inputs[i].UseClipMetadata && inputs[i].Icon != null && inputs[i].Icon != inputs[i].AutoIcon) continue;
                    settings.SampleTime = inputs[i].Time;
                    var captureKey = Tuple.Create(inputs[i].Clip, inputs[i].Time);
                    if (!capturedIcons.TryGetValue(captureKey, out var capturedIcon))
                    {
                        capturedIcon = ClipCaptureFiles.SaveIcon(avatar, inputs[i].Clip, settings, result.IconFolder);
                        capturedIcons.Add(captureKey, capturedIcon);
                    }
                    inputs[i].Icon = inputs[i].AutoIcon = capturedIcon;
                }
                string token = Guid.NewGuid().ToString("N");
                result.Parameter = "PPAE/Expression/" + token;
                result.Controller = AnimatorController.CreateAnimatorControllerAtPath(result.ControllerFolder + "/Expressions.controller");
                result.Controller.name = ModulePrefix + token;
                result.Controller.AddParameter(result.Parameter, AnimatorControllerParameterType.Int);
                ConfigureController(result.Controller, inputs, result.Parameter);
                var install = CreateMenus(result.MenuFolder, inputs, result.Parameter, menuName.Trim(), releaseLabel, nextLabel, menuIcon, menuFolders, menuCategories);
                temporaryScene = EditorSceneManager.NewPreviewScene();
                root = new GameObject(PrefabName);
                SceneManager.MoveGameObjectToScene(root, temporaryScene);
                var merge = root.AddComponent<ModularAvatarMergeAnimator>();
                merge.animator = result.Controller;
                merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
                merge.pathMode = MergeAnimatorPathMode.Absolute;
                merge.mergeAnimatorMode = MergeAnimatorMode.Append;
                merge.layerPriority = LayerPriority;
                merge.matchAvatarWriteDefaults = false;
                root.AddComponent<ModularAvatarParameters>().parameters.Add(new ParameterConfig
                {
                    nameOrPrefix = result.Parameter, syncType = ParameterSyncType.Int, internalParameter = true,
                    localOnly = false, saved = false, defaultValue = 0, hasExplicitDefaultValue = true
                });
                root.AddComponent<ModularAvatarMenuInstaller>().menuToAppend = install;
                EditorUtility.SetDirty(result.Controller);
                AssetDatabase.SaveAssetIfDirty(result.Controller);
                result.Prefab = PrefabUtility.SaveAsPrefabAsset(root, result.PrefabFolder + "/" + PrefabName + ".prefab");
                if (result.Prefab == null) throw new IOException("Prefabを保存できませんでした。");
                for (int i = 0; i < entries.Count; i++)
                    if (inputs[i].AutoIcon != null && inputs[i].Icon == inputs[i].AutoIcon)
                        entries[i].Icon = entries[i].AutoIcon = inputs[i].Icon;
                return result;
            }
            catch
            {
                // Roll back only this generation's directories, never earlier output or source clips.
                foreach (string path in created) AssetDatabase.DeleteAsset(path);
                throw;
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                if (temporaryScene.IsValid()) EditorSceneManager.ClosePreviewScene(temporaryScene);
            }
        }

        private static void Validate(VRCAvatarDescriptor avatar, IList<ExpressionMenuEntry> entries, string menuName)
        {
            if (avatar == null) throw new ArgumentException("アバターを選択してください。");
            if (entries == null || entries.Count == 0 || entries.Count > MaxExpressions)
                throw new ArgumentException("表情は1～255件登録してください。");
            if (string.IsNullOrWhiteSpace(menuName)) throw new ArgumentException("メニュー名を入力してください。");
            foreach (var entry in entries)
            {
                if (entry == null || entry.Clip == null || !EditorUtility.IsPersistent(entry.Clip))
                    throw new ArgumentException("保存済みのクリップを選択してください。");
                if (float.IsNaN(entry.Time) || float.IsInfinity(entry.Time) || entry.Time < 0 || entry.Time > entry.Clip.length)
                    throw new ArgumentException(entry.Clip.name + " の時間が範囲外です。");
                var bindings = AnimationUtility.GetCurveBindings(entry.Clip);
                if (bindings.Length == 0 || AnimationUtility.GetObjectReferenceCurveBindings(entry.Clip).Length != 0
                    || bindings.Any(b => b.type != typeof(SkinnedMeshRenderer) || !b.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)))
                    throw new ArgumentException(entry.Clip.name + " はシェイプキーだけのクリップを指定してください。");
                foreach (var binding in bindings)
                {
                    var target = string.IsNullOrEmpty(binding.path) ? avatar.transform : avatar.transform.Find(binding.path);
                    var renderer = target != null ? target.GetComponent<SkinnedMeshRenderer>() : null;
                    string shape = binding.propertyName.Substring(11);
                    if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.GetBlendShapeIndex(shape) < 0)
                        throw new ArgumentException(entry.Clip.name + " の対象が見つかりません。 " + binding.path + " / " + shape);
                    float value = AnimationUtility.GetEditorCurve(entry.Clip, binding).Evaluate(entry.Time);
                    if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException(entry.Clip.name + " に無効な値があります。");
                }
            }
        }

        private static void ConfigureController(AnimatorController controller, IList<ExpressionMenuEntry> entries, string parameter)
        {
            var control = controller.layers[0].stateMachine;
            control.name = "PPAE Control";
            var idle = State(control, "Idle", null);
            var activeA = State(control, "Fixed A", null);
            var activeB = State(control, "Fixed B", null);
            var release = State(control, "Release", null);
            control.defaultState = idle;
            Tracking(activeA, true); Tracking(activeB, true); Tracking(release, false);
            var start = Transition(idle, activeA);
            start.AddCondition(AnimatorConditionMode.Greater, 0, parameter);
            start.AddCondition(AnimatorConditionMode.Less, entries.Count + 1, parameter);
            foreach (var active in new[] { activeA, activeB })
            {
                Transition(active, release).AddCondition(AnimatorConditionMode.Less, 1, parameter);
                Transition(active, release).AddCondition(AnimatorConditionMode.Greater, entries.Count, parameter);
                // Re-assert tracking when an existing gesture state changes Tracking Control.
                Transition(active, active == activeA ? activeB : activeA)
                    .AddCondition(AnimatorConditionMode.Greater, 0, parameter);
            }
            Transition(release, idle).AddCondition(AnimatorConditionMode.Equals, 0, parameter);
            Transition(release, idle).AddCondition(AnimatorConditionMode.NotEqual, 0, parameter);
            for (int i = 0; i < entries.Count; i++)
            {
                controller.AddLayer("PPAE " + (i + 1).ToString("D3") + " " + EntryName(entries[i]));
                var machine = controller.layers[i + 1].stateMachine;
                var inactive = State(machine, "Inactive", null);
                // Separate zero-weight layers drop old sparse bindings when another expression is selected.
                // The motion is the original asset: no copied clips, neutral clips or injected curves.
                var fixedState = State(machine, "Fixed", entries[i].Clip);
                machine.defaultState = inactive;
                Weight(inactive, i + 1, 0); Weight(fixedState, i + 1, 1);
                string time = parameter + "/Time/" + (i + 1);
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = time, type = AnimatorControllerParameterType.Float,
                    defaultFloat = entries[i].Clip.length > 0 ? entries[i].Time / entries[i].Clip.length : 0
                });
                fixedState.timeParameter = time;
                fixedState.timeParameterActive = true;
                fixedState.speed = 0;
                Transition(inactive, fixedState).AddCondition(AnimatorConditionMode.Equals, i + 1, parameter);
                Transition(fixedState, inactive).AddCondition(AnimatorConditionMode.NotEqual, i + 1, parameter);
            }
            var layers = controller.layers;
            layers[0].name = "PPAE Control";
            layers[0].defaultWeight = 1;
            for (int i = 1; i < layers.Length; i++) layers[i].defaultWeight = 0;
            controller.layers = layers;
        }

        private static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion)
        {
            var state = machine.AddState(name);
            state.motion = motion; state.writeDefaultValues = false;
            return state;
        }
        private static AnimatorStateTransition Transition(AnimatorState source, AnimatorState destination)
        {
            var transition = source.AddTransition(destination);
            transition.hasExitTime = false; transition.duration = 0; transition.offset = 0;
            transition.hasFixedDuration = true; transition.canTransitionToSelf = false;
            return transition;
        }
        private static void Tracking(AnimatorState state, bool fixedExpression)
        {
            var tracking = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
            tracking.trackingEyes = tracking.trackingMouth = fixedExpression
                ? VRCAnimatorTrackingControl.TrackingType.Animation : VRCAnimatorTrackingControl.TrackingType.Tracking;
        }
        private static void Weight(AnimatorState state, int layer, float value)
        {
            var weight = state.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            weight.playable = VRCAnimatorLayerControl.BlendableLayer.FX;
            weight.layer = layer; weight.goalWeight = value; weight.blendDuration = 0;
        }

        private static Texture2D CopyMenuIcon(string folder)
        {
            var source = Resources.Load<Texture2D>(MenuIconPath);
            if (source == null) throw new IOException("表情メニューのアイコンが見つかりません。");
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/ExpressionFolder.png");
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path))
                throw new IOException("表情メニューのアイコンを保存できませんでした。");
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (icon == null) throw new IOException("表情メニューのアイコンを読み込めませんでした。");
            return icon;
        }

        private static VRCExpressionsMenu CreateMenus(string folder, IList<ExpressionMenuEntry> entries,
            string parameter, string groupName, string release, string next, Texture2D menuIcon,
            ExpressionMenuFolder[] folders, ExpressionMenuCategory[] categories)
        {
            var install = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            install.name = "Install menu";
            AssetDatabase.CreateAsset(install, folder + "/Install.asset");
            var definitions = folders == null
                ? Enumerable.Range(0, (entries.Count + 6) / 7).Select(i => new MenuGroup
                    { Name = groupName + (i + 1), Indices = Enumerable.Range(i * 7, Math.Min(7, entries.Count - i * 7)).ToArray() }).ToArray()
                : folders.Select(f => new MenuGroup { Name = f.Name.Trim(), CategoryId = f.CategoryId, Icon = f.Icon,
                    Indices = Enumerable.Range(0, entries.Count).Where(i => entries[i].FolderId == f.Id).ToArray() })
                    .Where(g => g.Indices.Length > 0).ToArray();
            // Categories organize the editor list; only FX folders become submenus.
            if (categories != null)
                definitions = definitions.OrderBy(g => Array.FindIndex(categories, c => c.Id == g.CategoryId)).ToArray();
            var controls = new List<VRCExpressionsMenu.Control>();
            for (int group = 0; group < definitions.Length; group++)
            {
                var definition = definitions[group];
                var page = CreateGroupPages(folder, entries, parameter, definition, group + 1, release, next, menuIcon);
                controls.Add(SubMenu(definition.Name, page, definition.Icon != null ? definition.Icon : menuIcon));
            }
            var hub = CreateIndexPages(folder, "Hub", controls, next, menuIcon);
            install.controls.Add(SubMenu(HubMenuName, hub, menuIcon));
            EditorUtility.SetDirty(install); AssetDatabase.SaveAssetIfDirty(install);
            return install;
        }

        private static VRCExpressionsMenu.Control SubMenu(string name, VRCExpressionsMenu menu, Texture2D icon)
        { return new VRCExpressionsMenu.Control { name = name, type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = menu, icon = icon }; }

        private static VRCExpressionsMenu CreateIndexPages(string folder, string stem,
            IList<VRCExpressionsMenu.Control> controls, string next, Texture2D icon)
        {
            var pages = new List<VRCExpressionsMenu>();
            int offset = 0;
            do
            {
                var page = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                string suffix = pages.Count == 0 ? "" : "_" + (pages.Count + 1).ToString("D2");
                AssetDatabase.CreateAsset(page, folder + "/" + stem + suffix + ".asset");
                int take = Math.Min(controls.Count - offset, controls.Count - offset > 8 ? 7 : 8);
                page.controls.AddRange(controls.Skip(offset).Take(take));
                if (pages.Count > 0) pages[pages.Count - 1].controls.Add(SubMenu(next, page, icon));
                pages.Add(page); offset += take;
            } while (offset < controls.Count);
            foreach (var page in pages) { EditorUtility.SetDirty(page); AssetDatabase.SaveAssetIfDirty(page); }
            return pages[0];
        }

        private sealed class MenuGroup { public string Name, CategoryId; public Texture2D Icon; public int[] Indices; }

        private static VRCExpressionsMenu CreateGroupPages(string folder, IList<ExpressionMenuEntry> entries,
            string parameter, MenuGroup group, int groupNumber, string release, string next, Texture2D icon)
        {
            VRCExpressionsMenu first = null, previous = null;
            int offset = 0, number = 0;
            while (offset < group.Indices.Length)
            {
                var page = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                page.name = group.Name;
                if (offset == 0)
                    page.controls.Add(new VRCExpressionsMenu.Control { name = release,
                        type = VRCExpressionsMenu.Control.ControlType.Button,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter }, value = 0 });
                int capacity = 8 - page.controls.Count;
                int remaining = group.Indices.Length - offset;
                int take = Math.Min(remaining, remaining > capacity ? capacity - 1 : capacity);
                foreach (int index in group.Indices.Skip(offset).Take(take))
                    page.controls.Add(new VRCExpressionsMenu.Control { name = EntryName(entries[index]), icon = entries[index].Icon,
                        type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter }, value = index + 1 });
                string suffix = number++ == 0 ? "" : "_" + number.ToString("D2");
                AssetDatabase.CreateAsset(page, folder + "/Expressions" + groupNumber.ToString("D2") + suffix + ".asset");
                if (previous != null)
                {
                    previous.controls.Add(new VRCExpressionsMenu.Control { name = next, icon = icon,
                        type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = page });
                    EditorUtility.SetDirty(previous); AssetDatabase.SaveAssetIfDirty(previous);
                }
                if (first == null) first = page;
                previous = page; offset += take;
            }
            if (previous != null) { EditorUtility.SetDirty(previous); AssetDatabase.SaveAssetIfDirty(previous); }
            return first;
        }

        public static string EntryName(ExpressionMenuEntry entry)
        { return entry.UseClipMetadata || string.IsNullOrWhiteSpace(entry.Name) ? entry.Clip.name : entry.Name.Trim(); }
        public static string SafeName(string name)
        {
            string result = string.Concat((name ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' || c == '\\' ? '_' : c)).Trim().TrimEnd('.');
            if (string.IsNullOrEmpty(result) || result == "." || result == "..") result = "Expressions";
            if (System.Text.RegularExpressions.Regex.IsMatch(result, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) result = "_" + result;
            return result;
        }
        public static string AssetFolder(string folder)
        {
            string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string assets = Path.GetFullPath(Application.dataPath);
            if (string.Equals(full, assets, StringComparison.OrdinalIgnoreCase)) return "Assets";
            if (!full.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Assets内の保存先を選択してください。");
            return "Assets/" + full.Substring(assets.Length + 1).Replace('\\', '/');
        }
        private static void EnsureFolder(string folder)
        {
            string parent = "Assets";
            foreach (string segment in folder.Split('/').Skip(1))
            {
                if (!AssetDatabase.IsValidFolder(parent + "/" + segment)) AssetDatabase.CreateFolder(parent, segment);
                parent += "/" + segment;
            }
        }
    }
}
