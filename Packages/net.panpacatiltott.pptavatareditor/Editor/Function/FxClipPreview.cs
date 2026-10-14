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
                var sources = clips.Where(c => c != null).Distinct().ToArray();
                foreach (var binding in sources.SelectMany(AnimationUtility.GetCurveBindings).Distinct())
                    if (AnimationUtility.GetFloatValue(root, binding, out float value))
                        AnimationUtility.SetEditorCurve(defaults, binding, AnimationCurve.Constant(0, 0, value));
                foreach (var binding in sources.SelectMany(AnimationUtility.GetObjectReferenceCurveBindings).Distinct())
                    if (AnimationUtility.GetObjectReferenceValue(root, binding, out Object value))
                        AnimationUtility.SetObjectReferenceCurve(defaults, binding,
                            new[] { new ObjectReferenceKeyframe { time = 0, value = value } });
            }
            catch { Dispose(); throw; }
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
                sample = Object.Instantiate(clip);
                sample.hideFlags = HideFlags.HideAndDontSave;
                sample.events = new AnimationEvent[0];
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
            defaults.SampleAnimation(root, 0);
            if (sample != null) sample.SampleAnimation(root, Time);
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
