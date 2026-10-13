using UnityEngine;

namespace PPTAvatarEditor
{
    public interface IPPTAvatarEditorView
    {
        bool DrawGUI(GUILayoutOption[] layoutOptions);
        void DrawSettingsGUI();
        void LoadSettingData(SettingData settingAsset);
        void SaveSettingData(ref SettingData settingAsset);
        void Dispose();
    }
}
