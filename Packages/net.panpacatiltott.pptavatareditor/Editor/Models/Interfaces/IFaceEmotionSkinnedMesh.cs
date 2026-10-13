using System.Collections.Generic;
using UnityEngine;
using static PPTAvatarEditor.SkinnedMesh;

namespace PPTAvatarEditor
{
    public interface IFaceEmotionSkinnedMesh
    {
        SkinnedMeshRenderer Renderer { get; set; }

        Mesh Mesh { get; set; }
        GameObject Obj { get; set; }
        int BlendShapeCount { get; set; }
        bool IsOpenBlendShapes { get; set; }
        List<BlendShape> Blendshapes { get; set; }
        bool IsContainsAll { get; set; }
    }
}