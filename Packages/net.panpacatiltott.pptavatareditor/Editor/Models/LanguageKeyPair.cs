using UnityEngine;

namespace PPTAvatarEditor
{
    [CreateAssetMenu(menuName = "PPAE/LanguageData")]
    public class LanguageKeyPair : ScriptableObject
    {
        [Header("General")]
        public string reset;
        public string open;
        public string close;
        public string edit;
        public string selectFolder;
        public string selectFolderDialogMessageText;
        public string exclusions;
        public string select;
        public string ok;
        public string cancel;
        public string add;
        public string remove;
        public string autoFix;
        public string toggleAll;

        [Header("VRCAvatarEditorGUI")]
        public string avatarLabel;
        public string reloadAvatarButtonText;
        public string toolInfoButtonText;
        public string settingButtonText;
        public string resetPoseButtonText;
        public string uploadAvatarButtonText;

        [Header("Tab")]
        public string avatarInfoTitle;
        public string faceEmotionTitle;
        public string probeAnchorTitle;
        public string boundsTitle;
        public string shaderTitle;

        [Header("AnimationsGUI")]
        public string standingTabText;
        public string sittingTabText;
        public string emoteButtonText;
        public string faceAndHandButtonText;
        public string standingTitle;
        public string sittingTitle;
        public string noAvatarMessageText;
        public string noCustomStandingAnimsMessageText;
        public string noCustomSittingAnimsMessageText;
        public string createCustomStandingAnimsButtonText;
        public string createCustomSittingAnimsButtonText;
        public string setToSameAsCustomStandingAnimsButtonText;
        public string existMissingPathMessageText;
        public string failAutoFixMissingPathMessageText;

        public string haveNoAnimationClipMessageText;

        [Header("AvatarInfoGUI")]
        public string genderLabel;
        public string uploadStatusLabel;
        public string newAvatarText;
        public string uploadedAvatarText;
        public string customStandingAnimsLabel;
        public string customSittingAnimsLabel;
        public string triangleCountLabel;
        public string heightLabel;
        public string faceMeshLabel;
        public string viewPositionLabel;
        public string lipSyncWarningMessageText;
        public string lipSyncTypeLabel;
        public string lipSyncBlendShapesLabel;
        public string lipSyncBlendShapesAutoDetectButtonText;

        [Header("FaceEmotionGUI")]
        public string loadAnimationButtonText;
        public string setToDefaultButtonText;
        public string resetToDefaultButtonText;
        public string toggleAllLabel;
        public string minButtonText;
        public string maxButtonText;
        public string animClipFileNameLabel;
        public string animClipSaveFolderLabel;
        public string animationOverrideLabel;
        public string handPoseAnimClipLabel;
        public string createAnimFileButtonText;
        public string setToDefaultDialogTitleText;
        public string setToDefaultDialogMessageText;

        public string createOnlyMessageText;
        public string handPoseChangeFailedMessageText;
        public string missingGestureControllerMessageText;
        public string createGestureControllerText;
        public string setLeftAndRightHandLayerText;
        public string missingFxControllerMessageText;
        public string createFxControllerText;

        [Header("ProbeAnchorGUI")]
        public string setToSkinnedMeshRendererLabel;
        public string setToMeshRendererLabel;
        public string targetPositionLabel;
        public string rendererListLabel;
        public string probeAnchorMessageText;
        public string setProbeAnchorButtonText;

        [Header("BoundsGUI")]
        public string resetToBoundsToPrefabButtonText;
        public string childObjectsLabel;
        public string setBoundsButtonText;

        [Header("ShaderGUI")]
        public string duplicateSelectedButtonText;
        public string optimizeSelectedButtonText;
        public string useInspectorMessageText;
        public string replaceShaderButtonText;
        public string noSelectionText;
        public string multipleText;

        [Header("AppInfoGUI")]
        public string versionLabel;
        public string openOnlineManualButtonText;
        public string functionsLabel;
        public string readmeLabel;
        public string licenseLabel;
        public string usingSoftwareLicenseLabel;

        [Header("SettingGUI")]
        public string settingPageMessageText;
        public string saveSettingButtonText;
        public string changeDefaultSettingButtonText;
        // AvatarMonitor
        public string defaultCameraDistanceLabel;
        public string faceCameraDistanceLabel;
        public string cameraZoomStepDistanceLabel;
        public string gammaCorrectionLabel;
        public string monitorBackgroundColorLabel;
        public string monitorSizeTypeLabel;
        public string monitorSizeLabel;
        // FaceEmotionCreator
        public string sortTypeLabel;
        public string blendShapeExclusionsLabel;
        public string usePreviousAnimationOnHandAnimationLabel;
        // Other
        public string otherLabel;
        public string layoutTypeLabel;
        public string languageLabel;

        [Header("PPAE Workspace")]
        public string allCategory;
        public string uncategorized;
        public string undoPreview;
        public string redoPreview;
        public string undoHint;
        public string redoHint;
        public string clipName;
        public string saveOptions;
        public string assetsFolderOnly;
        public string itemCount;
        public string selectAll;
        public string selectAllHint;
        public string selectForExport;
        public string noMatchingShapes;
        public string includeInClip;
        public string previewTitle;
        public string frontView;
        public string previewZoom;
        public string previewHeight;
        public string previewHint;
        public string previewSizeHint;
        public string assignClipToFx;
        public string standaloneClipHint;
        public string fxLayerTitle;
        public string layerLabel;
        public string stateLabel;
        public string saveAndAssign;
        public string saveClip;
        public string clipSaved;
        public string chooseAvatar;
        public string avatarActions;
        public string back;
        public string avatarPrompt;
        public string layoutHint;
        public string propertiesLabel;
        public string loadProperties;
        public string loadAnimationTitle;
        public string infoDescription;
        public string infoEditingTitle;
        public string infoEditingGuide;
        public string infoFxGuide;
        public string infoHistoryGuide;
        public string infoSavingGuide;
        public string infoContact;
        public string openContact;
        public string sourceCode;
        public string infoLicenseTitle;
        public string infoLicenseNotice;
        public string upstreamRepository;
        public string originalLicense;
        public string thirdPartyLicense;
        public string readmeTitle;
        public string sortOriginal;
        public string sortAlphabetical;
        public string autoDetect;
        public string avatarSettings;
    }
}
