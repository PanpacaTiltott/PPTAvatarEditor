// Copyright (C) 2026 PanpacaTiltott. Distributed under the Zlib License.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PPTAvatarEditor
{
    internal sealed class FxClipPreview : IDisposable
    {
        private readonly GameObject root;
        private readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
        private readonly HashSet<EditorCurveBinding> floatBindings = new HashSet<EditorCurveBinding>();
        private readonly HashSet<EditorCurveBinding> objectBindings = new HashSet<EditorCurveBinding>();
        private AnimationClip defaults, sample;
        public AnimationClip Clip { get; private set; }
        public float Time { get; private set; }

        public FxClipPreview(GameObject root, IEnumerable<AnimationClip> clips)
        {
            this.root = root;
            try
            {
                // Every material used by a sampled clip belongs to this preview.
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(CloneMaterial).ToArray();
                defaults = new AnimationClip { name = "PPAE FX preview defaults", hideFlags = HideFlags.HideAndDontSave };
                CaptureDefaults(clips);
            }
            catch { Dispose(); throw; }
        }

        private void CaptureDefaults(IEnumerable<AnimationClip> clips)
        {
            var sources = clips.Where(c => c != null).Distinct().ToArray();
            foreach (var binding in sources.SelectMany(AnimationUtility.GetCurveBindings).Distinct())
                if (!floatBindings.Contains(binding) && AnimationUtility.GetFloatValue(root, binding, out float value))
                {
                    AnimationUtility.SetEditorCurve(defaults, binding, AnimationCurve.Constant(0, 0, value));
                    floatBindings.Add(binding);
                }
            foreach (var binding in sources.SelectMany(AnimationUtility.GetObjectReferenceCurveBindings).Distinct())
                if (!objectBindings.Contains(binding) && AnimationUtility.GetObjectReferenceValue(root, binding, out Object value))
                {
                    AnimationUtility.SetObjectReferenceCurve(defaults, binding,
                        new[] { new ObjectReferenceKeyframe { time = 0, value = value } });
                    objectBindings.Add(binding);
                }
        }

        private Material CloneMaterial(Material source)
        {
            if (source == null) return null;
            if (!materials.TryGetValue(source, out var copy))
            {
                copy = new Material(source) { name = source.name, hideFlags = HideFlags.HideAndDontSave };
                materials.Add(source, copy);
            }
            return copy;
        }

        public void Select(AnimationClip clip)
        {
            if (sample != null) Object.DestroyImmediate(sample);
            sample = null; Clip = clip;
            if (clip != null)
            {
                // Capture newly assigned bindings from the restored avatar, not the previous expression.
                SampleClip(defaults, 0);
                CaptureDefaults(new[] { clip });
                sample = Object.Instantiate(clip);
                sample.hideFlags = HideFlags.HideAndDontSave;
                AnimationUtility.SetAnimationEvents(sample, new AnimationEvent[0]);
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(sample))
                {
                    var keys = AnimationUtility.GetObjectReferenceCurve(sample, binding);
                    for (int i = 0; i < keys.Length; i++)
                        if (keys[i].value is Material material) keys[i].value = CloneMaterial(material);
                    AnimationUtility.SetObjectReferenceCurve(sample, binding, keys);
                }
            }
            Sample(0);
        }

        public void Sample(float time)
        {
            if (root == null) return;
            Time = Clip != null ? Mathf.Clamp(time, 0, Clip.length) : 0;
            SampleClip(defaults, 0);
            if (sample != null) SampleClip(sample, Time);
        }

        private void SampleClip(AnimationClip clip, float time)
        {
            var animator = root.GetComponent<Animator>();
            var avatar = animator != null ? animator.avatar : null;
            bool propertyOnly = avatar != null && avatar.isHuman && !clip.humanMotion;
            try
            {
                // Sample FX properties without making Mecanim reset the clone's Humanoid body pose.
                if (propertyOnly) animator.avatar = null;
                clip.SampleAnimation(root, time);
            }
            finally
            {
                if (propertyOnly && animator != null) animator.avatar = avatar;
            }
        }

        public void Dispose()
        {
            if (sample != null) Object.DestroyImmediate(sample);
            if (defaults != null) Object.DestroyImmediate(defaults);
            foreach (var material in materials.Values) if (material != null) Object.DestroyImmediate(material);
            materials.Clear(); sample = defaults = null; Clip = null;
        }
    }
}
