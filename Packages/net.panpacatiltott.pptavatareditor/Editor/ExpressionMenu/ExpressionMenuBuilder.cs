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
    public sealed class ExpressionMenuEntry
    {
        public string Name = "";
        public AnimationClip Clip;
        public Texture2D Icon;
        public float Time;
    }

    public sealed class ExpressionMenuResult
    {
        public string Folder;
        public string Parameter;
        public GameObject Prefab;
        public AnimatorController Controller;
    }

    // Generates an independent MA module without editing source assets or the avatar descriptor.
    public static class ExpressionMenuBuilder
    {
        public const int MaxExpressions = 255;
        public const int LayerPriority = 100;
        public const string ModulePrefix = "PPAE Expression Menu ";

        public static ExpressionMenuResult Build(VRCAvatarDescriptor avatar, IList<ExpressionMenuEntry> entries,
            string menuName, string folder, string releaseLabel = "解除", string nextLabel = "次へ")
        {
            if (avatar == null) throw new ArgumentException("アバターを選択してください。");
            if (entries == null || entries.Count == 0 || entries.Count > MaxExpressions)
                throw new ArgumentException("表情は1～255件登録してください。");
            if (string.IsNullOrWhiteSpace(menuName)) throw new ArgumentException("メニュー名を入力してください。");
            folder = AssetFolder(folder);
            // Validate every input before creating files. Non-shape animation requires explicit handling.
            var renderers = new Dictionary<string, SkinnedMeshRenderer>();
            var values = new List<Dictionary<EditorCurveBinding, float>>();
            foreach (var entry in entries)
            {
                if (entry == null || entry.Clip == null) throw new ArgumentException("クリップを選択してください。");
                if (float.IsNaN(entry.Time) || float.IsInfinity(entry.Time) || entry.Time < 0 || entry.Time > entry.Clip.length)
                    throw new ArgumentException(entry.Clip.name + " の時間が範囲外です。");
                var bindings = AnimationUtility.GetCurveBindings(entry.Clip);
                if (bindings.Length == 0 || AnimationUtility.GetObjectReferenceCurveBindings(entry.Clip).Length != 0
                    || bindings.Any(b => b.type != typeof(SkinnedMeshRenderer) || !b.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)))
                    throw new ArgumentException(entry.Clip.name + " はシェイプキーだけのクリップを指定してください。");
                var snapshot = new Dictionary<EditorCurveBinding, float>();
                foreach (var binding in bindings)
                {
                    var target = string.IsNullOrEmpty(binding.path) ? avatar.transform : avatar.transform.Find(binding.path);
                    var renderer = target != null ? target.GetComponent<SkinnedMeshRenderer>() : null;
                    string shape = binding.propertyName.Substring(11);
                    if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.GetBlendShapeIndex(shape) < 0)
                        throw new ArgumentException(entry.Clip.name + " の対象が見つかりません。 " + binding.path + " / " + shape);
                    renderers[binding.path] = renderer;
                    float value = AnimationUtility.GetEditorCurve(entry.Clip, binding).Evaluate(entry.Time);
                    if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException(entry.Clip.name + " に無効な値があります。");
                    snapshot[binding] = value;
                }
                values.Add(snapshot);
            }
            var defaults = new Dictionary<EditorCurveBinding, float>();
            foreach (var pair in renderers)
                for (int i = 0; i < pair.Value.sharedMesh.blendShapeCount; i++)
                    defaults[ShapeBinding(pair.Key, pair.Value.sharedMesh.GetBlendShapeName(i))] = pair.Value.GetBlendShapeWeight(i);
            AddTrackingDefaults(avatar, defaults);
            EnsureFolder(folder);
            string output = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + SafeName(menuName));
            AssetDatabase.CreateFolder(folder, Path.GetFileName(output));
            GameObject root = null;
            Scene temporaryScene = default;
            try
            {
                string token = Guid.NewGuid().ToString("N");
                string parameter = "PPAE/Expression/" + token;
                var controller = AnimatorController.CreateAnimatorControllerAtPath(output + "/Expressions.controller");
                controller.name = ModulePrefix + token;
                controller.AddParameter(parameter, AnimatorControllerParameterType.Int);
                var clips = new List<AnimationClip>();
                for (int i = 0; i < entries.Count; i++)
                {
                    var clip = new AnimationClip { name = EntryName(entries[i]), frameRate = 60 };
                    foreach (var pair in defaults)
                    {
                        float value = values[i].TryGetValue(pair.Key, out float selected) ? selected : pair.Value;
                        AnimationUtility.SetEditorCurve(clip, pair.Key, AnimationCurve.Constant(0, 1f / 60f, value));
                    }
                    AssetDatabase.CreateAsset(clip, output + "/" + (i + 1).ToString("D3") + "_" + SafeName(clip.name) + ".anim");
                    clips.Add(clip);
                }
                var empty = new AnimationClip { name = "No curves", frameRate = 60 };
                AssetDatabase.AddObjectToAsset(empty, controller);
                ConfigureController(controller, clips, parameter, empty);
                var menu = CreateMenus(output, entries, parameter, releaseLabel, nextLabel);
                var install = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                install.name = "Install menu";
                install.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = menuName.Trim(), type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = menu
                });
                AssetDatabase.CreateAsset(install, output + "/Install.asset");
                temporaryScene = EditorSceneManager.NewPreviewScene();
                root = new GameObject(ModulePrefix + menuName.Trim());
                SceneManager.MoveGameObjectToScene(root, temporaryScene);
                var merge = root.AddComponent<ModularAvatarMergeAnimator>();
                merge.animator = controller;
                merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
                merge.pathMode = MergeAnimatorPathMode.Absolute;
                merge.mergeAnimatorMode = MergeAnimatorMode.Append;
                merge.layerPriority = LayerPriority;
                // Every active state writes the same bindings; the expression layer has explicit weights.
                merge.matchAvatarWriteDefaults = false;
                root.AddComponent<ModularAvatarParameters>().parameters.Add(new ParameterConfig
                {
                    nameOrPrefix = parameter, syncType = ParameterSyncType.Int, internalParameter = true,
                    localOnly = false, saved = false, defaultValue = 0, hasExplicitDefaultValue = true
                });
                root.AddComponent<ModularAvatarMenuInstaller>().menuToAppend = install;
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, output + "/ExpressionMenu.prefab");
                if (prefab == null) throw new IOException("Prefabを保存できませんでした。");
                return new ExpressionMenuResult { Folder = output, Parameter = parameter, Prefab = prefab, Controller = controller };
            }
            catch
            {
                // Only this invocation's newly allocated folder is rolled back.
                AssetDatabase.DeleteAsset(output);
                throw;
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                if (temporaryScene.IsValid()) EditorSceneManager.ClosePreviewScene(temporaryScene);
            }
        }

        private static void ConfigureController(AnimatorController controller, List<AnimationClip> clips, string parameter, AnimationClip empty)
        {
            var control = controller.layers[0].stateMachine;
            control.name = "PPAE Control";
            var idle = State(control, "Idle", empty);
            var activeA = State(control, "Fixed A", empty);
            var activeB = State(control, "Fixed B", empty);
            var release = State(control, "Release", empty);
            control.defaultState = idle;
            Tracking(activeA, true); Tracking(activeB, true); Tracking(release, false);
            Weight(activeA, 1); Weight(activeB, 1); Weight(release, 0);
            var start = Transition(idle, activeA);
            start.AddCondition(AnimatorConditionMode.Greater, 0, parameter);
            start.AddCondition(AnimatorConditionMode.Less, clips.Count + 1, parameter);
            foreach (var active in new[] { activeA, activeB })
            {
                Transition(active, release).AddCondition(AnimatorConditionMode.Less, 1, parameter);
                Transition(active, release).AddCondition(AnimatorConditionMode.Greater, clips.Count, parameter);
                // Re-assert tracking if an existing gesture state changes Tracking Control while fixed.
                Transition(active, active == activeA ? activeB : activeA)
                    .AddCondition(AnimatorConditionMode.Greater, 0, parameter);
            }
            // Empty motions have no progressing normalized time. Explicit conditions avoid Exit Time stalls.
            Transition(release, idle).AddCondition(AnimatorConditionMode.Equals, 0, parameter);
            Transition(release, idle).AddCondition(AnimatorConditionMode.NotEqual, 0, parameter);
            controller.AddLayer("PPAE Expressions");
            var layers = controller.layers;
            layers[0].name = "PPAE Control";
            layers[0].defaultWeight = 1;
            layers[1].defaultWeight = 0;
            controller.layers = layers;
            var expressions = layers[1].stateMachine;
            var inactive = State(expressions, "Inactive", empty);
            expressions.defaultState = inactive;
            var off = expressions.AddAnyStateTransition(inactive);
            Configure(off); off.AddCondition(AnimatorConditionMode.Less, 1, parameter);
            for (int i = 0; i < clips.Count; i++)
            {
                var state = State(expressions, (i + 1).ToString("D3") + " " + clips[i].name, clips[i]);
                var select = expressions.AddAnyStateTransition(state);
                Configure(select); select.AddCondition(AnimatorConditionMode.Equals, i + 1, parameter);
            }
        }

        private static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion)
        {
            var state = machine.AddState(name);
            state.motion = motion; state.writeDefaultValues = false;
            return state;
        }
        private static AnimatorStateTransition Transition(AnimatorState source, AnimatorState destination)
        {
            var transition = source.AddTransition(destination); Configure(transition); return transition;
        }
        private static void Configure(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false; transition.duration = 0; transition.offset = 0;
            transition.hasFixedDuration = true; transition.canTransitionToSelf = false;
        }
        private static void Tracking(AnimatorState state, bool fixedExpression)
        {
            var tracking = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
            tracking.trackingEyes = tracking.trackingMouth = fixedExpression
                ? VRCAnimatorTrackingControl.TrackingType.Animation : VRCAnimatorTrackingControl.TrackingType.Tracking;
        }
        private static void Weight(AnimatorState state, float value)
        {
            var weight = state.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            weight.playable = VRCAnimatorLayerControl.BlendableLayer.FX;
            weight.layer = 1; weight.goalWeight = value; weight.blendDuration = 0;
        }

        private static VRCExpressionsMenu CreateMenus(string folder, IList<ExpressionMenuEntry> entries, string parameter, string release, string next)
        {
            var pages = new List<VRCExpressionsMenu>();
            int index = 0;
            while (index < entries.Count)
            {
                var page = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                page.name = "Expressions " + (pages.Count + 1);
                page.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = release, type = VRCExpressionsMenu.Control.ControlType.Button,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter }, value = 0
                });
                int count = Math.Min(entries.Count - index, entries.Count - index > 7 ? 6 : 7);
                for (int j = 0; j < count; j++, index++)
                    page.controls.Add(new VRCExpressionsMenu.Control
                    {
                        name = EntryName(entries[index]), icon = entries[index].Icon,
                        type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter }, value = index + 1
                    });
                AssetDatabase.CreateAsset(page, folder + "/Menu" + (pages.Count + 1).ToString("D2") + ".asset");
                if (pages.Count != 0)
                {
                    var previous = pages[pages.Count - 1];
                    previous.controls.Add(new VRCExpressionsMenu.Control
                    { name = next, type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = page });
                    EditorUtility.SetDirty(previous); AssetDatabase.SaveAssetIfDirty(previous);
                }
                pages.Add(page);
            }
            return pages[0];
        }

        private static void AddTrackingDefaults(VRCAvatarDescriptor avatar, Dictionary<EditorCurveBinding, float> values)
        {
            var mouth = avatar.VisemeSkinnedMesh;
            if (mouth != null && mouth.sharedMesh != null)
                foreach (string name in (avatar.VisemeBlendShapes ?? Array.Empty<string>()).Concat(new[] { avatar.MouthOpenBlendShapeName }))
                    AddShape(avatar, mouth, name, values);
            var eyes = avatar.customEyeLookSettings;
            if (eyes.eyelidsSkinnedMesh != null && eyes.eyelidsSkinnedMesh.sharedMesh != null && eyes.eyelidsBlendshapes != null)
                foreach (int index in eyes.eyelidsBlendshapes)
                    if (index >= 0 && index < eyes.eyelidsSkinnedMesh.sharedMesh.blendShapeCount)
                        AddShape(avatar, eyes.eyelidsSkinnedMesh, eyes.eyelidsSkinnedMesh.sharedMesh.GetBlendShapeName(index), values);
            // Author the closed-jaw rotation for avatars whose lip sync is bone-driven.
            if (avatar.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBone && avatar.lipSyncJawBone != null)
            {
                var jaw = avatar.lipSyncJawBone;
                if (!jaw.IsChildOf(avatar.transform)) throw new ArgumentException("顎ボーンがアバター外にあります。");
                string path = AnimationUtility.CalculateTransformPath(jaw, avatar.transform);
                var rotation = avatar.lipSyncJawClosed;
                for (int i = 0; i < 4; i++)
                    values[EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + "xyzw"[i])] = rotation[i];
            }
        }
        private static void AddShape(VRCAvatarDescriptor avatar, SkinnedMeshRenderer mesh, string name, Dictionary<EditorCurveBinding, float> values)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!mesh.transform.IsChildOf(avatar.transform)) throw new ArgumentException("表情メッシュがアバター外にあります。");
            int index = mesh.sharedMesh.GetBlendShapeIndex(name);
            if (index < 0) return;
            var binding = ShapeBinding(AnimationUtility.CalculateTransformPath(mesh.transform, avatar.transform), name);
            if (!values.ContainsKey(binding)) values[binding] = mesh.GetBlendShapeWeight(index);
        }
        private static EditorCurveBinding ShapeBinding(string path, string shape)
        { return EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape); }
        public static string EntryName(ExpressionMenuEntry entry)
        { return string.IsNullOrWhiteSpace(entry.Name) ? entry.Clip.name : entry.Name.Trim(); }
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
