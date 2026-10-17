using System;
using System.IO;
using System.Linq;
using UnityEditor;

namespace PPTAvatarEditor.Utilities
{
    internal static class ExpressionAssetUtility
    {
        internal static string DefaultFolder(string avatarName)
        {
            var name = new string((avatarName ?? string.Empty).Select(c =>
                char.IsControl(c) || "<>:\"/\\|?*".IndexOf(c) >= 0 ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
            if (name.Length == 0) name = "Avatar";
            var stem = name.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL"
                || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT"))
                    && stem[3] >= '1' && stem[3] <= '9')) name = "_" + name;
            return "Assets/PPAE/" + name + "/Animation/";
        }

        internal static string EnsureFolder(string folder)
        {
            var path = (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            var parts = path.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets"
                || parts.Any(part => string.IsNullOrWhiteSpace(part) || part == "." || part == ".."))
                throw new ArgumentException("クリップの保存先は、このプロジェクトの Assets 内を指定してください。", nameof(folder));
            string current = "Assets";
            foreach (var part in parts.Skip(1))
            {
                var next = current + "/" + part;
                if (!AssetDatabase.IsValidFolder(next) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, part)))
                    throw new IOException("保存フォルダーを作成できませんでした: " + next);
                current = next;
            }
            return current;
        }
    }
}
