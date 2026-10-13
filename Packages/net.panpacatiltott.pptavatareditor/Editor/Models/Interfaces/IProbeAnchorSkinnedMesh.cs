using UnityEngine;

namespace PPTAvatarEditor
{
    public interface IProbeAnchorSkinnedMesh
    {
        SkinnedMeshRenderer Renderer { get; set; }

        GameObject Obj { get; set; }
    }
}