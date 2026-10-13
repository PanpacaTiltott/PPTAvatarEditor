using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using PPTAvatarEditor.Utilities;
using VRCAvatar = PPTAvatarEditor.Avatars3.VRCAvatar3;
using VRC.SDK3.Avatars.Components;
using VRC_AvatarDescriptor = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor;
using AnimationsGUI = PPTAvatarEditor.Avatars3.AnimationsView3;
using FaceEmotionGUI = PPTAvatarEditor.Avatars3.FaceEmotionView3;

// Copyright (c) 2019 gatosyocora

namespace PPTAvatarEditor
{
    public class PPTAvatarEditorView : EditorWindow
    {
        private const string CONTACT_ID = "PanpacaTiltott";
        private const string REPOSITORY_URL = "https://github.com/PanpacaTiltott/PPTAvatarEditor";
        private const string UPSTREAM_URL = "https://github.com/gatosyocora/VRCAvatarEditor";
        internal const string BASE_TOOL_VERSION = "VRCAvatarEditor 0.8.0";
        private LanguageKeyPair Text => LocalizeText.instance.langPair;

        private AvatarMonitorView avatarMonitorGUI;
        public AnimationsGUI animationsGUI;
        private AvatarInfoView avatarInfoGUI;
        private FaceEmotionGUI faceEmotionGUI;
        private ProbeAnchorView probeAnchorGUI;
        private MeshBoundsView meshBoundsGUI;
        private ShaderView shaderGUI;
        private IPPTAvatarEditorView selectedToolGUI;
        private Dictionary<ToolFunc, IPPTAvatarEditorView> toolGUIs = new Dictionary<ToolFunc, IPPTAvatarEditorView>();

        private FaceEmotion faceEmotion;

        private bool needRepaint = false;

        [SerializeField] private VRC_AvatarDescriptor _targetAvatarDescriptor;
        public VRC_AvatarDescriptor TargetAvatarDescriptor
        {
            get => _targetAvatarDescriptor;
            set
            {
                if (_targetAvatarDescriptor != value)
                {
                    _targetAvatarDescriptor = value;
                    OnChangedAvatar();
                }
            }
        }

        private VRCAvatar edittingAvatar = null;
        private VRCAvatar originalAvatar = null;

        private string editorFolderPath;

        private string saveFolder;

        public enum ToolFunc
        {
            AvatarInfo,
            FaceEmotion,
            ProbeAnchor,
            Bounds,
            Shader,
        }

        private ToolFunc _currentTool = ToolFunc.FaceEmotion;
        [SerializeField] private float previewRatio = 0.36f;
        [SerializeField] private float fxPanelHeight;
        [SerializeField] private int workspaceLayoutVersion;
        private bool resizingPreview;
        private bool resizingFxPanel;
        private bool isShowingAvatarSettings;
        private Vector2 toolScroll;
        internal Rect PreviewPanelRect { get; private set; }
        internal Rect FxDividerRect { get; private set; }
        internal Rect ToolContentRect { get; private set; }
        internal Rect FxPanelRect { get; private set; }
        internal Rect[] AvatarSettingsSectionRects { get; } = new Rect[4];
        public ToolFunc CurrentTool
        {
            get
            {
                return _currentTool;
            }
            set
            {
                _currentTool = value;
                OnToolChanged();
            }
        }

        #region Shader Variable
        #endregion

        #region ToolInfo Variable

        private bool isShowingToolInfo = false;
        private const string LICENSE_FILE_NAME = "LICENSE.txt";
        private const string USING_SOFTWARE_FILE_NAME = "USING_SOFTWARE_LICENSES.txt";
        private string licenseText;
        private string usingSoftwareLicenseText;
        private bool isShowingLicense = false;
        private bool isShowingUsingSoftwareLicense = false;

        #endregion

        #region Setting Variable

        private bool isShowingSetting = false;

        public enum LayoutType
        {
            Default,
            Half,
        }

        #endregion

        #region Changeable Parameters from Setting

        private LayoutType layoutType = LayoutType.Default;
        private string language = "JA";

        #endregion


        [MenuItem("PPAE/Editor")]
        public static void Create()
        {
            var window = GetWindow<PPTAvatarEditorView>("PPAE");
            window.minSize = new Vector2(900f, 540f);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("PPAE");
            minSize = new Vector2(900f, 540f);
            if (workspaceLayoutVersion < 2)
            {
                previewRatio = 0.36f;
                workspaceLayoutVersion = 2;
            }
            if (workspaceLayoutVersion < 4)
            {
                fxPanelHeight = 0;
                workspaceLayoutVersion = 4;
            }
            edittingAvatar = new VRCAvatar();

            editorFolderPath = Path.GetDirectoryName(
                                    AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this)));
            editorFolderPath = editorFolderPath.Substring(0, editorFolderPath.LastIndexOf(Path.DirectorySeparatorChar) + 1);

            saveFolder = "Assets/";


            licenseText = FileUtility.GetFileTexts(editorFolderPath + LICENSE_FILE_NAME)
                + "\n\n" + FileUtility.GetFileTexts(editorFolderPath + "MODIFICATIONS_LICENSE.txt");
            usingSoftwareLicenseText = FileUtility.GetFileTexts(editorFolderPath + USING_SOFTWARE_FILE_NAME);

            avatarMonitorGUI = ScriptableObject.CreateInstance<AvatarMonitorView>();
            animationsGUI = ScriptableObject.CreateInstance<AnimationsGUI>();
            avatarInfoGUI = ScriptableObject.CreateInstance<AvatarInfoView>();
            faceEmotionGUI = ScriptableObject.CreateInstance<FaceEmotionGUI>();
            probeAnchorGUI = ScriptableObject.CreateInstance<ProbeAnchorView>();
            meshBoundsGUI = ScriptableObject.CreateInstance<MeshBoundsView>();
            shaderGUI = ScriptableObject.CreateInstance<ShaderView>();

            faceEmotion = new FaceEmotion();

            toolGUIs.Clear();
            toolGUIs.Add(ToolFunc.AvatarInfo, avatarInfoGUI);
            toolGUIs.Add(ToolFunc.FaceEmotion, faceEmotionGUI);
            toolGUIs.Add(ToolFunc.ProbeAnchor, probeAnchorGUI);
            toolGUIs.Add(ToolFunc.Bounds, meshBoundsGUI);
            toolGUIs.Add(ToolFunc.Shader, shaderGUI);

            avatarMonitorGUI.Initialize(CurrentTool);
            animationsGUI.Initialize(edittingAvatar, originalAvatar, saveFolder, this, faceEmotionGUI);
            avatarInfoGUI.Initialize(originalAvatar, edittingAvatar, avatarMonitorGUI);
            probeAnchorGUI.Initialize(originalAvatar);

            selectedToolGUI = faceEmotionGUI;
            CurrentTool = ToolFunc.FaceEmotion;

            (layoutType, language) = EditorSetting.instance.LoadSettingDataFromScriptableObject(
                                            editorFolderPath, language,
                                            avatarMonitorGUI, faceEmotionGUI);

            // Windowを開いたときにオブジェクトが選択されていればそれをアバターとして設定する
            if (TargetAvatarDescriptor != null)
            {
                OnChangedAvatar();
            }
            else if (Selection.gameObjects.Length == 1)
            {
                var selectionTransform = Selection.gameObjects.Single().transform;
                while (selectionTransform != null)
                {
                    TargetAvatarDescriptor = selectionTransform.GetComponent<VRC_AvatarDescriptor>();
                    if (TargetAvatarDescriptor != null)
                    {
                        break;
                    }
                    selectionTransform = selectionTransform.parent;
                }
            }

            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            if (faceEmotionGUI != null) faceEmotionGUI.Dispose();
            if (avatarMonitorGUI != null) avatarMonitorGUI.Dispose();
            foreach (var editor in new Editor[] { avatarMonitorGUI, animationsGUI, avatarInfoGUI,
                faceEmotionGUI, probeAnchorGUI, meshBoundsGUI, shaderGUI })
            {
                if (editor != null) DestroyImmediate(editor);
            }
            toolGUIs.Clear();
        }

        private void OnGUI()
        {
            if (LocalizeText.instance.langPair == null)
            {
                DrawNowLoading();
                return;
            }

            if (!isShowingToolInfo && !isShowingSetting && !isShowingAvatarSettings && CurrentTool == ToolFunc.FaceEmotion)
                faceEmotionGUI.HandleHistoryShortcuts();
            DrawWorkspaceHeader();

            var content = new Rect(6, 30, Mathf.Max(1, position.width - 12), Mathf.Max(1, position.height - 36));
            if (isShowingToolInfo || isShowingSetting || isShowingAvatarSettings)
            {
                GUILayout.BeginArea(content);
                using (var scroll = new EditorGUILayout.ScrollViewScope(toolScroll))
                {
                    toolScroll = scroll.scrollPosition;
                    if (isShowingToolInfo) ToolInfoGUI();
                    else if (isShowingAvatarSettings) AvatarSettingsGUI();
                    else SettingGUI();
                }
                GUILayout.EndArea();
                return;
            }

            var previewWidth = Mathf.Clamp((content.width - 6) * previewRatio, 280, Mathf.Max(280, content.width - 426));
            var defaultPreviewHeight = Mathf.Min(previewWidth + 96, content.height * 0.7f);
            var fxHeight = Mathf.Clamp(fxPanelHeight > 0 ? fxPanelHeight : content.height - defaultPreviewHeight - 6,
                124, Mathf.Max(124, content.height - 206));
            PreviewPanelRect = new Rect(content.x, content.y, previewWidth, content.height - fxHeight - 6);
            FxDividerRect = new Rect(content.x, PreviewPanelRect.yMax, previewWidth, 6);
            FxPanelRect = new Rect(content.x, FxDividerRect.yMax, previewWidth, fxHeight);
            var divider = new Rect(PreviewPanelRect.xMax, content.y, 6, content.height);
            var editorRect = new Rect(divider.xMax, content.y, content.xMax - divider.xMax, content.height);

            DrawPreviewDivider(divider, content);
            DrawFxDivider(FxDividerRect, content);

            GUILayout.BeginArea(PreviewPanelRect, EditorStyles.helpBox);
            needRepaint = avatarMonitorGUI.DrawWorkspaceGUI(PreviewPanelRect.width - 10, PreviewPanelRect.height - 10);
            GUILayout.EndArea();

            GUILayout.BeginArea(FxPanelRect, EditorStyles.helpBox);
            animationsGUI.DrawWorkspaceGUI(FxPanelRect.width - 10, FxPanelRect.height - 10);
            GUILayout.EndArea();

            GUI.Box(editorRect, GUIContent.none, EditorStyles.helpBox);
            ToolContentRect = new Rect(editorRect.x + 4, editorRect.y + 4, editorRect.width - 8, editorRect.height - 8);
            GUILayout.BeginArea(ToolContentRect);
            if (edittingAvatar.Descriptor == null)
            {
                EditorGUILayout.HelpBox(Text.avatarPrompt, MessageType.Info);
            }
            else
            {
                faceEmotionGUI.DrawGUI(new[] { GUILayout.Height(ToolContentRect.height), GUILayout.ExpandWidth(true) });
            }
            GUILayout.EndArea();

            if (needRepaint || GUI.changed) Repaint();
        }

        private void DrawWorkspaceHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(24)))
            {
                GUILayout.Label("PPAE", EditorStyles.boldLabel, GUILayout.Width(44));
                GUILayout.Label(Text.avatarLabel, GUILayout.Width(EditorStyles.label.CalcSize(new GUIContent(Text.avatarLabel)).x));
                TargetAvatarDescriptor = (VRC_AvatarDescriptor)EditorGUILayout.ObjectField(
                    TargetAvatarDescriptor, typeof(VRC_AvatarDescriptor), true, GUILayout.MinWidth(140));
                using (new EditorGUI.DisabledScope(TargetAvatarDescriptor == null))
                {
                    if (GUILayout.Button(LocalizeText.instance.langPair.reloadAvatarButtonText,
                        EditorStyles.toolbarButton, ToolbarWidth(Text.reloadAvatarButtonText))) OnChangedAvatar();
                    if (GUILayout.Button(isShowingAvatarSettings ? Text.back : Text.avatarSettings,
                        EditorStyles.toolbarButton, ToolbarWidth(Text.avatarSettings)))
                        SetWorkspacePage(avatarSettings: !isShowingAvatarSettings);
                }

                if (GUILayout.Button(Text.avatarActions, EditorStyles.toolbarDropDown, ToolbarWidth(Text.avatarActions, 22)))
                {
                    var menu = new GenericMenu();
                    if (edittingAvatar.Descriptor != null)
                    {
                        menu.AddItem(new GUIContent(LocalizeText.instance.langPair.resetPoseButtonText), false, () =>
                        {
                            HumanoidPose.ResetPose(edittingAvatar.Descriptor.gameObject);
                            HumanoidPose.ResetPose(originalAvatar.Descriptor.gameObject);
                            Repaint();
                        });
                        menu.AddItem(new GUIContent(LocalizeText.instance.langPair.uploadAvatarButtonText),
                            false, () => VRCSDKUtility.UploadAvatar());
                    }
                    else
                    {
                        menu.AddDisabledItem(new GUIContent(Text.chooseAvatar));
                    }
                    menu.DropDown(GUILayoutUtility.GetLastRect());
                }

                if (GUILayout.Button(isShowingToolInfo ? Text.back : Text.toolInfoButtonText,
                    EditorStyles.toolbarButton, ToolbarWidth(Text.toolInfoButtonText)))
                {
                    SetWorkspacePage(info: !isShowingToolInfo);
                }
                if (GUILayout.Button(isShowingSetting ? Text.back : Text.settingButtonText,
                    EditorStyles.toolbarButton, ToolbarWidth(Text.settingButtonText)))
                {
                    SetWorkspacePage(settings: !isShowingSetting);
                }
            }
        }

        private static GUILayoutOption ToolbarWidth(string text, float padding = 8)
            => GUILayout.Width(Mathf.Max(54, EditorStyles.toolbarButton.CalcSize(new GUIContent(text)).x + padding));

        private void SetWorkspacePage(bool avatarSettings = false, bool info = false, bool settings = false)
        {
            if (isShowingSetting && !settings && edittingAvatar?.Descriptor != null)
                EditorSetting.instance.ApplySettingsToEditorGUI(edittingAvatar, faceEmotionGUI);
            isShowingAvatarSettings = avatarSettings;
            isShowingToolInfo = info;
            isShowingSetting = settings;
            toolScroll = Vector2.zero;
            if (avatarSettings && edittingAvatar?.Descriptor != null)
                shaderGUI.Initialize(edittingAvatar, originalAvatar);
            SceneView.RepaintAll();
            Repaint();
        }

        private void DrawFxDivider(Rect divider, Rect content)
        {
            EditorGUIUtility.AddCursorRect(divider, MouseCursor.ResizeVertical);
            EditorGUI.DrawRect(new Rect(divider.x, divider.center.y, divider.width, 1), new Color(0.1f, 0.1f, 0.1f));
            var control = GUIUtility.GetControlID("PPAEPreviewFxDivider".GetHashCode(), FocusType.Passive, divider);
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && divider.Contains(evt.mousePosition))
            {
                GUIUtility.hotControl = control;
                resizingFxPanel = true;
                evt.Use();
            }
            else if (resizingFxPanel && GUIUtility.hotControl == control && evt.type == EventType.MouseDrag)
            {
                fxPanelHeight = Mathf.Clamp(content.yMax - evt.mousePosition.y - 3, 124, Mathf.Max(124, content.height - 206));
                evt.Use();
                Repaint();
            }
            else if (resizingFxPanel && evt.type == EventType.MouseUp)
            {
                resizingFxPanel = false;
                GUIUtility.hotControl = 0;
                evt.Use();
            }
        }

        private void DrawPreviewDivider(Rect divider, Rect content)
        {
            EditorGUIUtility.AddCursorRect(divider, MouseCursor.ResizeHorizontal);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(divider.x + 2, divider.y, 2, divider.height),
                    EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.6f, 0.6f, 0.6f));

            var control = GUIUtility.GetControlID(FocusType.Passive);
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && divider.Contains(evt.mousePosition))
            {
                resizingPreview = true;
                GUIUtility.hotControl = control;
                evt.Use();
            }
            else if (resizingPreview && GUIUtility.hotControl == control && evt.type == EventType.MouseDrag)
            {
                previewRatio = Mathf.Clamp((evt.mousePosition.x - content.x) / (content.width - 6), 0.25f, 0.72f);
                Repaint();
                evt.Use();
            }
            else if (resizingPreview && evt.type == EventType.MouseUp)
            {
                resizingPreview = false;
                GUIUtility.hotControl = 0;
                evt.Use();
            }
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (isShowingAvatarSettings || CurrentTool == ToolFunc.Bounds)
            {
                if (meshBoundsGUI != null)
                {
                    meshBoundsGUI.DrawBoundsGizmo();
                }
            }

            if (SceneView.lastActiveSceneView != null && (isShowingAvatarSettings || CurrentTool == ToolFunc.Bounds))
                SceneView.lastActiveSceneView.Repaint();

        }

        private void ToolInfoGUI()
        {
            EditorGUILayout.LabelField("PPTAvatarEditor", new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 }, GUILayout.Height(28));
            EditorGUILayout.LabelField(Text.versionLabel, GetToolVersion());
            EditorGUILayout.Space(12);

            EditorGUILayout.LabelField(Text.infoContact, EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel("@" + CONTACT_ID, GUILayout.Width(180), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button(Text.openContact, GUILayout.Width(90))) Application.OpenURL("https://x.com/" + CONTACT_ID);
                if (GUILayout.Button(Text.sourceCode, GUILayout.Width(110))) Application.OpenURL(REPOSITORY_URL);
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.Space(12);

            EditorGUILayout.LabelField(Text.infoLicenseTitle, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(string.Format(Text.infoLicenseNotice, BASE_TOOL_VERSION), MessageType.None);
            if (GUILayout.Button(Text.upstreamRepository, GUILayout.Width(290))) Application.OpenURL(UPSTREAM_URL);
            EditorGUILayout.Space(8);

            isShowingLicense = EditorGUILayout.Foldout(isShowingLicense, Text.originalLicense, true);
            if (isShowingLicense) DrawInfoDocument(licenseText);
            isShowingUsingSoftwareLicense = EditorGUILayout.Foldout(isShowingUsingSoftwareLicense, Text.thirdPartyLicense, true);
            if (isShowingUsingSoftwareLicense) DrawInfoDocument(usingSoftwareLicenseText);
        }

        private void DrawInfoDocument(string text)
        {
            var style = new GUIStyle(EditorStyles.wordWrappedLabel);
            var height = style.CalcHeight(new GUIContent(text), Mathf.Max(250, position.width - 50));
            EditorGUILayout.SelectableLabel(text, style, GUILayout.Height(height + 12));
        }

        private void SettingGUI()
        {

            EditorGUILayout.HelpBox(LocalizeText.instance.langPair.settingPageMessageText, MessageType.Info);

            avatarMonitorGUI.DrawSettingsGUI();

            EditorGUILayout.Space();

            faceEmotionGUI.DrawSettingsGUI();

            EditorGUILayout.Space();

            EditorGUILayout.LabelField(LocalizeText.instance.langPair.otherLabel, EditorStyles.boldLabel);

            EditorGUILayout.LabelField(Text.layoutTypeLabel, Text.layoutHint);

            var languagePacks = LocalizeText.instance.langs;
            var index = Array.IndexOf(languagePacks, language);
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                index = EditorGUILayout.Popup(LocalizeText.instance.langPair.languageLabel, index, languagePacks);
                if (check.changed)
                {
                    SetLanguage(languagePacks[index]);
                }
            }

            EditorGUILayout.Space();

            GatoGUILayout.Button(
                LocalizeText.instance.langPair.saveSettingButtonText,
                () => {
                    EditorSetting.instance.SaveSettingDataToScriptableObject(
                                            layoutType, language,
                                            avatarMonitorGUI, faceEmotionGUI);
                });

            GatoGUILayout.Button(
                LocalizeText.instance.langPair.changeDefaultSettingButtonText,
                () => {
                    EditorSetting.instance.DeleteMySettingData();
                    (layoutType, language) = EditorSetting.instance.LoadSettingDataFromScriptableObject(
                                                editorFolderPath, language,
                                                avatarMonitorGUI, faceEmotionGUI);
                });
        }

        internal void SetLanguage(string selectedLanguage)
        {
            LocalizeText.instance.LoadLanguage(selectedLanguage);
            language = LocalizeText.instance.langPair.name;
            Repaint();
        }

        private void AvatarSettingsGUI()
        {
            EditorGUILayout.LabelField(Text.avatarSettings, EditorStyles.boldLabel);
            EditorGUILayout.Space(8);
            if (edittingAvatar?.Descriptor == null) return;
            DrawAvatarSettingsSection(avatarInfoGUI, 0, null);
            EditorGUILayout.Space(16);
            DrawAvatarSettingsSection(probeAnchorGUI, 1, new[] { GUILayout.MaxHeight(240) });
            EditorGUILayout.Space(16);
            DrawAvatarSettingsSection(meshBoundsGUI, 2, null);
            EditorGUILayout.Space(16);
            DrawAvatarSettingsSection(shaderGUI, 3, new[] { GUILayout.Height(240) });
        }

        private void DrawAvatarSettingsSection(IPPTAvatarEditorView view, int index, GUILayoutOption[] options)
        {
            using (new EditorGUILayout.VerticalScope()) view.DrawGUI(options);
            if (Event.current.type == EventType.Repaint) AvatarSettingsSectionRects[index] = GUILayoutUtility.GetLastRect();
        }

        public void OnToolChanged()
        {
            selectedToolGUI = toolGUIs[CurrentTool];
            avatarMonitorGUI.SetCurrentTool(CurrentTool);
            if (edittingAvatar == null || edittingAvatar.Descriptor == null) return;

            if (CurrentTool == ToolFunc.FaceEmotion)
            {
                faceEmotionGUI.Initialize(faceEmotion, edittingAvatar, originalAvatar, saveFolder, this, animationsGUI);

                UpdateExclusitionBlendShapes();

                avatarMonitorGUI.MoveAvatarCam(true, false);
            }
            else
            {
                avatarMonitorGUI.MoveAvatarCam(false, false);
            }

            if (CurrentTool == ToolFunc.Shader)
            {
                shaderGUI.Initialize(edittingAvatar, originalAvatar);
            }

            avatarMonitorGUI.showEyePosition = false;
        }

        private void OnChangedAvatar()
        {
            if (TargetAvatarDescriptor == null)
            {
                isShowingAvatarSettings = false;
                edittingAvatar = new VRCAvatar();
                originalAvatar = null;
                avatarMonitorGUI.avatarMonitorField.ClearAvatar();
                Repaint();
                return;
            }

            edittingAvatar = avatarMonitorGUI.SetAvatarPreview(TargetAvatarDescriptor);
            originalAvatar = new VRCAvatar(TargetAvatarDescriptor);
            EditorSetting.instance.ApplySettingsToEditorGUI(edittingAvatar, faceEmotionGUI);

            animationsGUI.Initialize(edittingAvatar, originalAvatar, saveFolder, this, faceEmotionGUI);
            avatarInfoGUI.Initialize(originalAvatar, edittingAvatar, avatarMonitorGUI);
            meshBoundsGUI.Initialize(originalAvatar);
            probeAnchorGUI.Initialize(originalAvatar);
            if (isShowingAvatarSettings) shaderGUI.Initialize(edittingAvatar, originalAvatar);

            CurrentTool = ToolFunc.FaceEmotion;
        }

        public void OpenSubWindow()
        {
            GetWindow<AnimationLoaderView>(Text.loadAnimationTitle, typeof(PPTAvatarEditorView));
        }

        // TODO: NowLoadingをもう少しいい感じにする
        private void DrawNowLoading()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    var style = new GUIStyle(GUI.skin.label)
                    {
                        wordWrap = true
                    };
                    EditorGUILayout.LabelField("Now Loading...", style);
                    GUILayout.FlexibleSpace();
                }
                GUILayout.FlexibleSpace();
            }
        }

        public void UpdateExclusitionBlendShapes()
        {
            var exclusionsBlendShapes = VRCAvatarMeshUtility.GetExclusionBlendShapes(edittingAvatar, faceEmotionGUI.blendshapeExclusions);

            if (edittingAvatar.SkinnedMeshList != null)
            {
                for (int i = 0; i < edittingAvatar.SkinnedMeshList.Count; i++)
                {
                    if (edittingAvatar.SkinnedMeshList[i].BlendShapeCount <= 0) continue;

                    if (edittingAvatar.LipSyncShapeKeyNames != null &&
                        edittingAvatar.LipSyncShapeKeyNames.Count > 0)
                    {
                        edittingAvatar.SkinnedMeshList[i].SetExclusionBlendShapesByContains(exclusionsBlendShapes);
                    }
                }
            }
        }

        private static string GetToolVersion()
        {
            var jsonTextPath = AssetDatabase.GUIDToAssetPath("b3389eab9d8c32d60f7dc74da68beca3");
            var jsonText = File.ReadAllText(jsonTextPath);
            var packagejson = JsonUtility.FromJson<PackageJson>(jsonText);
            return packagejson.version;
        }

        [Serializable]
        public class PackageJson
        {
            public string version;
        }
    }
}
