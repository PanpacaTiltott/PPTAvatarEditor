// Copyright (C) 2026 PanpacaTiltott. Distributed under the Zlib License.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace PPTAvatarEditor
{
    internal sealed class FxViewerCatalog
    {
        internal sealed class Entry
        {
            public int Layer;
            public string Label;
            public AnimatorState State;
            public BlendTree Tree;
            public int ChildIndex;
            public AnimationClip OriginalClip;
            public AnimationClip Clip;
        }

        public readonly List<Entry> Entries = new List<Entry>();
        public string[] Layers { get; }
        private readonly AnimatorOverrideController overrides;

        public FxViewerCatalog(VRCAvatarDescriptor avatar)
        {
            var runtime = avatar.baseAnimationLayers?
                .FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            overrides = runtime as AnimatorOverrideController;
            var controller = (overrides != null ? overrides.runtimeAnimatorController : runtime) as AnimatorController;
            Layers = controller != null ? controller.layers.Select(l => l.name).ToArray() : new string[0];
            if (controller == null) return;
            for (int i = 0; i < controller.layers.Length; i++)
                ReadStates(controller.layers[i].stateMachine, i, "", new HashSet<AnimatorStateMachine>());
        }

        private void ReadStates(AnimatorStateMachine machine, int layer, string path, HashSet<AnimatorStateMachine> visited)
        {
            if (machine == null || !visited.Add(machine)) return;
            foreach (var child in machine.states)
                if (child.state != null)
                    ReadMotion(child.state.motion, child.state, null, -1, layer, path + child.state.name, new HashSet<BlendTree>());
            foreach (var child in machine.stateMachines)
                if (child.stateMachine != null)
                    ReadStates(child.stateMachine, layer, path + child.stateMachine.name + "/", visited);
        }

        private void ReadMotion(Motion motion, AnimatorState state, BlendTree parent, int childIndex,
            int layer, string label, HashSet<BlendTree> ancestors)
        {
            if (motion is BlendTree tree)
            {
                if (!ancestors.Add(tree)) return;
                for (int i = 0; i < tree.children.Length; i++)
                {
                    var child = tree.children[i].motion;
                    ReadMotion(child, state, tree, i, layer, label + "/" + (child != null ? child.name : (i + 1).ToString()), ancestors);
                }
                ancestors.Remove(tree);
            }
            else
            {
                var clip = motion as AnimationClip;
                var replacement = overrides != null && clip != null ? overrides[clip] : null;
                Entries.Add(new Entry { Layer = layer, Label = label, State = state, Tree = parent,
                    ChildIndex = childIndex, OriginalClip = clip, Clip = replacement != null ? replacement : clip });
            }
        }

        public bool CanAssign(Entry entry)
        {
            if (entry == null) return false;
            UnityEngine.Object target = overrides != null ? (UnityEngine.Object)overrides
                : entry.Tree != null ? entry.Tree : (UnityEngine.Object)entry.State;
            return target != null && AssetDatabase.GetAssetPath(target).StartsWith("Assets/", StringComparison.Ordinal)
                && AssetDatabase.IsOpenForEdit(target) && (overrides == null || entry.OriginalClip != null);
        }

        public void Assign(Entry entry, AnimationClip clip)
        {
            if (!CanAssign(entry)) throw new InvalidOperationException("FX target is not writable.");
            UnityEngine.Object target = overrides != null ? (UnityEngine.Object)overrides
                : entry.Tree != null ? entry.Tree : (UnityEngine.Object)entry.State;
            Undo.RecordObject(target, LocalizeText.instance.langPair.replaceFxClip);
            if (overrides != null) overrides[entry.OriginalClip] = clip;
            else if (entry.Tree != null)
            {
                var children = entry.Tree.children;
                var child = children[entry.ChildIndex];
                child.motion = clip;
                children[entry.ChildIndex] = child;
                entry.Tree.children = children;
            }
            else entry.State.motion = clip;
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
        }
    }
}
