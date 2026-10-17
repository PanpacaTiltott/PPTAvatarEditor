// Copyright (C) 2026 PanpacaTiltott. Distributed under the Zlib License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace PPTAvatarEditor
{
    public sealed class ExpressionClipGroup
    {
        public string Key, Name;
        public AnimationClip[] Clips;
        public readonly List<int> Layers = new List<int>();
    }

    public sealed class ClipCaptureResult
    {
        public readonly List<string> Paths = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public bool Cancelled;
    }

    public static class ClipCaptureFiles
    {
        private const string MetadataPrefix = "PPAE.ClipCapture/1\n";
        [Serializable] private sealed class CaptureMetadata
        {
            public string ClipGuid, AvatarKey;
            public long ClipLocalId, CreatedUtcTicks;
            public float SampleTime;
        }
        public static event Action CapturesSaved;

        public static ExpressionClipGroup[] ExpressionGroups(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return new ExpressionClipGroup[0];
            var eligible = new HashSet<AnimationClip>(ExpressionClips(avatar));
            var catalog = new FxViewerCatalog(avatar);
            var controller = GetFxController(avatar);
            var groups = new List<ExpressionClipGroup>();
            foreach (var layer in catalog.Entries.GroupBy(e => e.Layer))
            {
                if (IsAutomaticControlName(catalog.Layers[layer.Key])) continue;
                var clips = layer.Where(e => !IsAutomaticControlName(e.Label)).Select(e => e.Clip)
                    .Where(c => c != null && eligible.Contains(c) && !IsAutomaticControlName(c.name)).Distinct().ToArray();
                if (clips.Length == 0) continue;
                // Keep the FX layer identity, even when both hands reference the same clips.
                string key = LayerKey(controller, layer.Key);
                var group = new ExpressionClipGroup { Key = key, Name = catalog.Layers[layer.Key], Clips = clips };
                group.Layers.Add(layer.Key); groups.Add(group);
            }
            return groups.ToArray();
        }

        public static AnimationClip[] ExpressionClips(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return new AnimationClip[0];
            var meshes = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var faces = meshes.Where(r => r == avatar.VisemeSkinnedMesh || r.name.Equals("Body", StringComparison.OrdinalIgnoreCase)
                || r == avatar.customEyeLookSettings.eyelidsSkinnedMesh).ToArray();
            if (faces.Length == 0)
                faces = meshes.Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0)
                    .OrderByDescending(r => r.sharedMesh.blendShapeCount).Take(1).ToArray();
            var paths = new HashSet<string>(faces.Select(r => AnimationUtility.CalculateTransformPath(r.transform, avatar.transform)));
            var catalog = new FxViewerCatalog(avatar);
            return catalog.Entries.Where(e => !IsAutomaticControlName(catalog.Layers[e.Layer]) && !IsAutomaticControlName(e.Label))
                .Select(e => e.Clip).Where(c => c != null && !IsAutomaticControlName(c.name)).Distinct().Where(clip =>
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                // The menu generator accepts shape-only clips. Exclude clothing-only and mixed-property FX.
                return AnimationUtility.GetObjectReferenceCurveBindings(clip).Length == 0 && bindings.Length > 0
                    && bindings.All(b => b.type == typeof(SkinnedMeshRenderer)
                        && b.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                    && bindings.Any(b => paths.Contains(b.path));
            }).ToArray();
        }

        // Only automatic discovery uses this filter; explicitly added clips remain available.
        public static bool IsAutomaticControlName(string name)
        {
            string compact = Regex.Replace(name ?? "", @"[\s_\-./\[\]()]+", "").ToLowerInvariant();
            return new[] { "lipsync", "viseme", "blink", "eyelook", "eyetracking", "まばたき", "瞬き", "瞬目", "口パク", "リップシンク" }
                .Any(compact.Contains);
        }

        public static string ClipSourceKey(string layerKey, AnimationClip clip)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long id);
            return layerKey + "/" + guid + ":" + id;
        }

        private static string LayerKey(AnimatorController controller, int layer)
        {
            return controller != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(controller.layers[layer].stateMachine,
                out string guid, out long id) ? guid + ":" + id : "layer:" + layer;
        }

        private static AnimatorController GetFxController(VRCAvatarDescriptor avatar)
        {
            var runtime = avatar != null ? avatar.baseAnimationLayers?.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController : null;
            return (runtime is AnimatorOverrideController overrides ? overrides.runtimeAnimatorController : runtime) as AnimatorController;
        }

        public static HashSet<string> ExcludedAutomaticSources(VRCAvatarDescriptor avatar)
        {
            var catalog = new FxViewerCatalog(avatar);
            var controller = GetFxController(avatar);
            var result = new HashSet<string>();
            foreach (var entry in catalog.Entries.Where(e => e.Clip != null))
                if (IsAutomaticControlName(catalog.Layers[entry.Layer]) || IsAutomaticControlName(entry.Label) || IsAutomaticControlName(entry.Clip.name))
                    result.Add(ClipSourceKey(LayerKey(controller, entry.Layer), entry.Clip));
            return result;
        }

        private static string AvatarKey(VRCAvatarDescriptor avatar)
        {
            var prefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(avatar.gameObject);
            return avatar.name + "|" + (prefab != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)) : "");
        }

        // Match GUID + local ID, not a filename: different clips may have the same name.
        public static Dictionary<AnimationClip, Texture2D> FindIcons(VRCAvatarDescriptor avatar, IEnumerable<AnimationClip> clips)
        {
            var found = new Dictionary<AnimationClip, Texture2D>();
            if (avatar == null) return found;
            var keys = new Dictionary<string, AnimationClip>();
            foreach (var clip in clips.Where(c => c != null).Distinct())
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long id) && !string.IsNullOrEmpty(guid))
                    keys[guid + "/" + id] = clip;
            if (keys.Count == 0) return found;
            string avatarKey = AvatarKey(avatar);
            var newest = new Dictionary<AnimationClip, long>();
            foreach (var textureGuid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(textureGuid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                string data = importer != null ? importer.userData : null;
                if (data == null || !data.StartsWith(MetadataPrefix, StringComparison.Ordinal)) continue;
                CaptureMetadata metadata;
                try { metadata = JsonUtility.FromJson<CaptureMetadata>(data.Substring(MetadataPrefix.Length)); }
                catch { continue; }
                if (metadata == null || metadata.AvatarKey != avatarKey
                    || !keys.TryGetValue(metadata.ClipGuid + "/" + metadata.ClipLocalId, out var clip)) continue;
                if (newest.TryGetValue(clip, out long time) && time >= metadata.CreatedUtcTicks) continue;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null) { found[clip] = texture; newest[clip] = metadata.CreatedUtcTicks; }
            }
            return found;
        }

        public static float CapturedTime(Texture2D texture)
        {
            if (texture == null) return 0;
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
            string data = importer != null ? importer.userData : null;
            if (data == null || !data.StartsWith(MetadataPrefix, StringComparison.Ordinal)) return 0;
            try { return JsonUtility.FromJson<CaptureMetadata>(data.Substring(MetadataPrefix.Length)).SampleTime; }
            catch { return 0; }
        }

        public static string SafeName(string value)
        {
            var name = Regex.Replace(value ?? "", @"[<>:""/\\|?*\x00-\x1f]", "_").Trim().TrimEnd('.');
            if (string.IsNullOrEmpty(name)) name = "expression";
            if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)) name = "_" + name;
            return name.Length > 100 ? name.Substring(0, 100).TrimEnd('.') : name;
        }

        public static string DefaultFolder(string avatarName) => "Assets/PPAE/" + SafeName(avatarName) + "/Pictures";

        public static string AssetFolder(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Choose a folder inside Assets.");
            string full = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string assets = Path.GetFullPath(Application.dataPath);
            if (string.Equals(full, assets, StringComparison.OrdinalIgnoreCase)) return "Assets";
            if (!full.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a folder inside Assets.");
            return "Assets/" + full.Substring(assets.Length + 1).Replace('\\', '/');
        }

        public static ClipCaptureResult Save(VRCAvatarDescriptor avatar, IEnumerable<AnimationClip> clips,
            ClipCaptureSettings settings, string folder, Func<int, int, AnimationClip, bool> cancel = null)
        { return SaveCore(avatar, clips, settings, folder, cancel, true); }

        public static Texture2D SaveIcon(VRCAvatarDescriptor avatar, AnimationClip clip, ClipCaptureSettings settings, string folder)
        {
            var result = SaveCore(avatar, new[] { clip }, settings, folder, null, false);
            if (result.Errors.Count > 0 || result.Paths.Count != 1)
                throw new IOException(string.Join("\n", result.Errors));
            return AssetDatabase.LoadAssetAtPath<Texture2D>(result.Paths[0]);
        }

        private static ClipCaptureResult SaveCore(VRCAvatarDescriptor avatar, IEnumerable<AnimationClip> clips,
            ClipCaptureSettings settings, string folder, Func<int, int, AnimationClip, bool> cancel, bool notify)
        {
            if (avatar == null) throw new ArgumentNullException(nameof(avatar));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            var targets = clips?.Where(c => c != null).Distinct().ToArray() ?? new AnimationClip[0];
            if (targets.Length == 0) throw new ArgumentException("Select an animation clip.");
            folder = AssetFolder(folder);
            var result = new ClipCaptureResult();
            for (int i = 0; i < targets.Length; i++)
            {
                if (cancel != null && cancel(i, targets.Length, targets[i])) { result.Cancelled = true; break; }
                Texture2D image = null;
                try
                {
                    using (var session = new ClipCaptureSession(avatar, targets[i]))
                        image = session.Capture(settings, settings.Width, settings.Height);
                    var bytes = image.EncodeToPNG();
                    if (bytes == null || bytes.Length == 0) throw new IOException("PNG encoding failed.");
                    Directory.CreateDirectory(folder);
                    string path = WriteNew(folder, SafeName(targets[i].name), bytes);
                    result.Paths.Add(path);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null)
                    {
                        importer.textureType = TextureImporterType.Default;
                        importer.alphaSource = TextureImporterAlphaSource.FromInput;
                        importer.alphaIsTransparency = true;
                        importer.mipmapEnabled = false;
                        importer.sRGBTexture = true;
                        importer.maxTextureSize = 4096;
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(targets[i], out string clipGuid, out long clipId);
                        importer.userData = MetadataPrefix + JsonUtility.ToJson(new CaptureMetadata
                        {
                            ClipGuid = clipGuid, ClipLocalId = clipId, AvatarKey = AvatarKey(avatar),
                            CreatedUtcTicks = DateTime.UtcNow.Ticks, SampleTime = Mathf.Min(settings.SampleTime, targets[i].length)
                        });
                        importer.SaveAndReimport();
                    }
                }
                catch (Exception ex) { result.Errors.Add(targets[i].name + ": " + ex.Message); }
                finally { if (image != null) UnityEngine.Object.DestroyImmediate(image); }
            }
            if (notify && result.Paths.Count > 0) CapturesSaved?.Invoke();
            return result;
        }

        private static string WriteNew(string folder, string name, byte[] png)
        {
            for (int index = 0; ; index++)
            {
                string path = folder + "/" + name + (index == 0 ? "" : "_" + index.ToString("00")) + ".png";
                if (File.Exists(path) || File.Exists(path + ".meta") || Directory.Exists(path)) continue;
                FileStream stream;
                try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
                catch (IOException) when (File.Exists(path)) { continue; }
                try { using (stream) stream.Write(png, 0, png.Length); return path; }
                catch { File.Delete(path); throw; } // Only the new file reserved by this call.
            }
        }
    }
}
