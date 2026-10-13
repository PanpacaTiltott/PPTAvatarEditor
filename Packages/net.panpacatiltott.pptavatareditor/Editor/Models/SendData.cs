using System.Collections.Generic;
using UnityEditor;

namespace PPTAvatarEditor
{
    public class SendData : ScriptableSingleton<SendData>
    {
        public string filePath;
        public List<AnimParam> loadingProperties;
    }
}