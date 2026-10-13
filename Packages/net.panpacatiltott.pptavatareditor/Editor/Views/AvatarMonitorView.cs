using System;
using UnityEditor;
using UnityEngine;
using VRCAvatar = PPTAvatarEditor.Avatars3.VRCAvatar3;
using VRC_AvatarDescriptor = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor;

namespace PPTAvatarEditor
{
    public class AvatarMonitorView : Editor, IPPTAvatarEditorView
    {
        private LanguageKeyPair Text => LocalizeText.instance.langPair;
        private VRCAvatar avatar;
        private PPTAvatarEditorView.ToolFunc currentTool;

        private RenderTexture avatarCamTexture;
        private bool isGammaCorrection = true;
        private GameObject avatarCam = null;

        private static readonly int CAMERA_ROTATE_ANGLE = 30;

        private float camPosZ;

        private Light sceneLight;

        private Texture upDownTexture;
        private Material gammaMat;

        public enum MonitorSize
        {
            Small = 256,
            Mediam = 512,
            Large = 768,
            Custom
        }

        private float defaultZoomDist = 0.5f;
        private float faceZoomDist = 0.1f;
        private float zoomStepDist = 0.1f;

        private Color monitorBgColor = new Color(0.95f, 0.95f, 0.95f, 1);

        private MonitorSize sizeType = MonitorSize.Small;
        private int monitorSize;

        public AvatarMonitorField avatarMonitorField;

        public bool showEyePosition = false;

        public void Initialize(PPTAvatarEditorView.ToolFunc currentTool)
        {
            this.currentTool = currentTool;

            upDownTexture = Resources.Load<Texture>("PPAE/Icon/UpDown");

            avatarMonitorField = new AvatarMonitorField();

            MoveAvatarCam += avatarMonitorField.MoveAvatarCam;
        }

        public Action<bool, bool> MoveAvatarCam;

        public void Dispose()
        {
            if (avatarCam != null)
                UnityEngine.Object.DestroyImmediate(avatarCam);

            avatarMonitorField?.Dispose();
        }

        private bool faceFocus = true;

        public void SetCurrentTool(PPTAvatarEditorView.ToolFunc tool)
        {
            currentTool = tool;
            faceFocus = tool == PPTAvatarEditorView.ToolFunc.FaceEmotion;
        }

        public bool DrawGUI(GUILayoutOption[] layoutOptions)
        {
            return DrawWorkspaceGUI(Mathf.Max(300, monitorSize), Mathf.Max(300, monitorSize) + 100);
        }

        public bool DrawWorkspaceGUI(float width, float height)
        {
            bool changed = false;
            GUILayout.Label(Text.previewTitle, EditorStyles.boldLabel, GUILayout.Height(24));

            var previewRect = GUILayoutUtility.GetRect(0, Mathf.Max(80, height - 100),
                GUILayout.ExpandWidth(true), GUILayout.Height(Mathf.Max(80, height - 100)));
            changed |= avatarMonitorField.Render(previewRect, isGammaCorrection, showEyePosition);

            using (new EditorGUI.DisabledScope(!avatarMonitorField.HasAvatar))
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(24)))
                {
                    if (GUILayout.Button("<", EditorStyles.toolbarButton, GUILayout.Width(32)))
                    {
                        avatarMonitorField.RotateCamera(-CAMERA_ROTATE_ANGLE);
                        changed = true;
                    }
                    if (GUILayout.Button(Text.frontView, EditorStyles.toolbarButton, GUILayout.Width(94)))
                    {
                        avatarMonitorField.MoveAvatarCam(faceFocus);
                        changed = true;
                    }
                    if (GUILayout.Button(">", EditorStyles.toolbarButton, GUILayout.Width(32)))
                    {
                        avatarMonitorField.RotateCamera(CAMERA_ROTATE_ANGLE);
                        changed = true;
                    }
                    GUILayout.FlexibleSpace();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(Text.previewZoom, EditorStyles.miniLabel, GUILayout.Width(40));
                    using (var check = new EditorGUI.ChangeCheckScope())
                    {
                        var zoom = GUILayout.HorizontalSlider(avatarMonitorField.GetNormalizedSubOrthographicSize(), 0, 1);
                        if (check.changed)
                        {
                            avatarMonitorField.ZoomAvatarCam(zoom);
                            changed = true;
                        }
                    }
                    GUILayout.Space(10);
                    GUILayout.Label(Text.previewHeight, EditorStyles.miniLabel, GUILayout.Width(42));
                    using (var check = new EditorGUI.ChangeCheckScope())
                    {
                        var cameraHeight = GUILayout.HorizontalSlider(avatarMonitorField.GetNormalizedMonitorHeight(), 0, 1);
                        if (check.changed)
                        {
                            avatarMonitorField.MoveAvatarCamHeight(cameraHeight);
                            changed = true;
                        }
                    }
                }
            }
            GUILayout.Label(Text.previewHint, EditorStyles.centeredGreyMiniLabel);
            return changed;
        }

        public void DrawSettingsGUI()
        {
            EditorGUILayout.LabelField(Text.previewTitle, EditorStyles.boldLabel);
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                defaultZoomDist = EditorGUILayout.FloatField(LocalizeText.instance.langPair.defaultCameraDistanceLabel, defaultZoomDist);
                faceZoomDist = EditorGUILayout.FloatField(LocalizeText.instance.langPair.faceCameraDistanceLabel, faceZoomDist);
                zoomStepDist = EditorGUILayout.FloatField(LocalizeText.instance.langPair.cameraZoomStepDistanceLabel, zoomStepDist);

                if (check.changed)
                    avatarMonitorField.SetZoomParameters(defaultZoomDist, faceZoomDist, zoomStepDist);
            }

            EditorGUILayout.Space();
            isGammaCorrection = EditorGUILayout.ToggleLeft(LocalizeText.instance.langPair.gammaCorrectionLabel, isGammaCorrection);

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                monitorBgColor = EditorGUILayout.ColorField(LocalizeText.instance.langPair.monitorBackgroundColorLabel, monitorBgColor);
                if (check.changed) avatarMonitorField.SetAvatarCamBgColor(monitorBgColor);
            }
            EditorGUILayout.LabelField(Text.previewSizeHint, EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>
        /// アバターを写す用のカメラを設定する
        /// </summary>
        public VRCAvatar SetAvatarPreview(VRC_AvatarDescriptor descriptor)
        {
            var avatar = avatarMonitorField.AddAvatar(descriptor);
            avatarMonitorField.SetAvatarCamBgColor(monitorBgColor);
            this.avatar = avatar;

            return avatar;
        }

        public void LoadSettingData(SettingData settingAsset)
        {
            defaultZoomDist = settingAsset.defaultZoomDist;
            faceZoomDist = settingAsset.faceZoomDist;
            zoomStepDist = settingAsset.zoomStepDist;

            isGammaCorrection = settingAsset.isGammaCorrection;
            monitorBgColor = settingAsset.monitorBgColor;
            avatarMonitorField.SetAvatarCamBgColor(monitorBgColor);

            sizeType = settingAsset.monitorSizeType;
            if (settingAsset.monitorSizeType != MonitorSize.Custom)
            {
                monitorSize = (int)settingAsset.monitorSizeType;
            }
            else
            {
                monitorSize = (int)settingAsset.monitorSize;
            }
            avatarMonitorField.SetZoomParameters(defaultZoomDist, faceZoomDist, zoomStepDist);
        }

        public void SaveSettingData(ref SettingData settingAsset)
        {
            settingAsset.defaultZoomDist = defaultZoomDist;
            settingAsset.faceZoomDist = faceZoomDist;
            settingAsset.zoomStepDist = zoomStepDist;

            settingAsset.isGammaCorrection = isGammaCorrection;
            settingAsset.monitorBgColor = monitorBgColor;

            settingAsset.monitorSizeType = sizeType;
            if (sizeType == MonitorSize.Custom)
            {
                settingAsset.monitorSize = monitorSize;
            }
        }
    }
}
