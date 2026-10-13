using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PPTAvatarEditor
{
    // Preview edits have their own history: using these buttons never undoes a scene edit.
    internal sealed class BlendShapeEditHistory
    {
        private const int Capacity = 128;
        private readonly SkinnedMesh[] meshes;
        private readonly SkinnedMesh.BlendShape[][] shapes;
        private readonly List<Edit> edits = new List<Edit>();
        private int cursor;
        private string continuousKey;

        private sealed class Snapshot
        {
            internal float[][] Weights;
            internal bool[][] Included;
            internal bool[] AllIncluded;
        }

        private sealed class Edit
        {
            internal string Label;
            internal Snapshot Before;
            internal Snapshot After;
        }

        internal bool CanUndo => cursor > 0;
        internal bool CanRedo => cursor < edits.Count;
        internal string UndoLabel => CanUndo ? edits[cursor - 1].Label : string.Empty;
        internal string RedoLabel => CanRedo ? edits[cursor].Label : string.Empty;

        internal BlendShapeEditHistory(IEnumerable<SkinnedMesh> source)
        {
            meshes = source.Where(mesh => mesh.Renderer != null && mesh.BlendShapeCount > 0).ToArray();
            shapes = meshes.Select(mesh => mesh.Blendshapes.OrderBy(shape => shape.Id).ToArray()).ToArray();
        }

        internal void Apply(string label, Action change, string gesture = null)
        {
            var before = Capture();
            change();
            var after = Capture();
            if (Equal(before, after)) return;

            bool merge = gesture != null && gesture == continuousKey && cursor > 0 && cursor == edits.Count;
            if (cursor < edits.Count) edits.RemoveRange(cursor, edits.Count - cursor);
            if (merge) edits[cursor - 1].After = after;
            else
            {
                edits.Add(new Edit { Label = label, Before = before, After = after });
                cursor++;
                if (edits.Count > Capacity) { edits.RemoveAt(0); cursor--; }
            }
            continuousKey = gesture;
        }

        internal void EndGesture() => continuousKey = null;

        internal void Undo()
        {
            EndGesture();
            if (CanUndo) Restore(edits[--cursor].Before);
        }

        internal void Redo()
        {
            EndGesture();
            if (CanRedo) Restore(edits[cursor++].After);
        }

        private Snapshot Capture()
        {
            return new Snapshot
            {
                Weights = meshes.Select((mesh, i) => shapes[i].Select(shape =>
                    mesh.Renderer == null ? 0 : mesh.Renderer.GetBlendShapeWeight(shape.Id)).ToArray()).ToArray(),
                Included = shapes.Select(list => list.Select(shape => shape.IsContains).ToArray()).ToArray(),
                AllIncluded = meshes.Select(mesh => mesh.IsContainsAll).ToArray()
            };
        }

        private static bool Equal(Snapshot first, Snapshot second)
        {
            if (!first.AllIncluded.SequenceEqual(second.AllIncluded)) return false;
            for (int i = 0; i < first.Weights.Length; i++)
                if (!first.Weights[i].SequenceEqual(second.Weights[i])
                    || !first.Included[i].SequenceEqual(second.Included[i])) return false;
            return true;
        }

        private void Restore(Snapshot snapshot)
        {
            for (int i = 0; i < meshes.Length; i++)
            {
                var renderer = meshes[i].Renderer;
                if (renderer == null || renderer.sharedMesh != meshes[i].Mesh) continue;
                for (int j = 0; j < shapes[i].Length; j++)
                {
                    renderer.SetBlendShapeWeight(shapes[i][j].Id, snapshot.Weights[i][j]);
                    shapes[i][j].IsContains = snapshot.Included[i][j];
                }
                meshes[i].IsContainsAll = snapshot.AllIncluded[i];
            }
        }
    }
}
