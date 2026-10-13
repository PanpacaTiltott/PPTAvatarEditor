using System;
using System.Collections.Generic;
using System.Linq;

namespace PPTAvatarEditor
{
    // Categories follow the authored mesh order, independently of the list's display sort.
    internal sealed class BlendShapeCategories
    {
        private readonly Dictionary<SkinnedMesh.BlendShape, int> membership =
            new Dictionary<SkinnedMesh.BlendShape, int>();

        private readonly List<string> sectionNames = new List<string>();
        internal string[] Labels => new[] { LocalizeText.instance.langPair.allCategory }
            .Concat(sectionNames.Select(name => name.Length == 0 ? LocalizeText.instance.langPair.uncategorized : name)).ToArray();

        internal static SkinnedMesh FindBody(IEnumerable<SkinnedMesh> meshes)
            => meshes.FirstOrDefault(mesh => mesh.Obj != null
                && string.Equals(mesh.Obj.name, "Body", StringComparison.OrdinalIgnoreCase));

        internal BlendShapeCategories(IEnumerable<SkinnedMesh> meshes)
        {
            var sections = new Dictionary<SkinnedMesh.BlendShape, string>();
            foreach (var mesh in meshes)
            {
                string section = string.Empty;
                foreach (var shape in mesh.Blendshapes.OrderBy(shape => shape.Id))
                {
                    if (TryReadHeading(shape.Name, out var heading)) section = heading;
                    sections[shape] = section;
                    if (!sectionNames.Contains(section)) sectionNames.Add(section);
                }
            }

            if (sectionNames.Remove(string.Empty)) sectionNames.Add(string.Empty);
            foreach (var pair in sections) membership[pair.Key] = sectionNames.IndexOf(pair.Value) + 1;
        }

        internal bool Contains(int category, SkinnedMesh.BlendShape shape)
            => category == 0 || (membership.TryGetValue(shape, out var index) && index == category);

        // A heading is surrounded by decoration, e.g. ---EYE--- or ==口==.
        // Empty dividers return subsequent items to the unnamed section.
        internal static bool TryReadHeading(string name, out string heading)
        {
            heading = string.Empty;
            if (string.IsNullOrWhiteSpace(name)) return false;
            var text = name.Trim();
            if (text.Length < 2 || !IsDecoration(text[0]) || !IsDecoration(text[text.Length - 1])) return false;
            int start = 0;
            int end = text.Length;
            while (start < end && IsDecoration(text[start])) start++;
            while (end > start && IsDecoration(text[end - 1])) end--;
            heading = text.Substring(start, end - start).Trim();
            return true;
        }

        private static bool IsDecoration(char value)
            => value == '-' || value == '=' || value == '_' || value == '*' || value == '#';
    }
}
