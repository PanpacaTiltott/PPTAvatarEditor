using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using PPTAvatarEditor.Base;
using PPTAvatarEditor.Utilities;
using VRCAvatar = PPTAvatarEditor.Avatars3.VRCAvatar3;
using VRC.SDK3.Avatars.Components;

namespace PPTAvatarEditor.Avatars3
{
    public class AnimationsView3 : AnimationsViewBase
    {
        private VRCAvatar editAvatar;
        private VRCAvatar originalAvatar;

        public static readonly string[] EMOTIONSTATES = { "Idle", "Fist", "Open", "Point", "Peace", "RockNRoll", "Gun", "Thumbs up" };

        private AnimatorController fxController;

        private const int PLAYABLE_GESTURE_LAYER_INDEX = 2;
        private const int PLAYABLE_FX_LAYER_INDEX = 4;

        public void Initialize(VRCAvatar editAvatar,
                               VRCAvatar originalAvatar,
                               string saveFolderPath,
                               PPTAvatarEditorView vrcAvatarEditorGUI,
                               FaceEmotionView3 faceEmotionGUI)
        {
            this.editAvatar = editAvatar;
            this.originalAvatar = originalAvatar;
            this.vrcAvatarEditorGUI = vrcAvatarEditorGUI;
            this.faceEmotionGUI = faceEmotionGUI;

            Initialize(saveFolderPath);

            if (editAvatar != null && editAvatar.FxController != null)
            {
                // TODO: AnimationClipのバリデーション機能
            }
        }

        private Vector2 animationScroll;
        private const float StateRowHeight = 22f;
        private const float ListHeaderHeight = 48f;
        internal Rect LayerSelectionRect { get; private set; }
        internal Rect StateListRect { get; private set; }
        internal float StateContentHeight { get; private set; }
        public override bool DrawGUI(GUILayoutOption[] layoutOptions)
        {
            return DrawWorkspaceGUI(350, 300);
        }

        public bool DrawWorkspaceGUI(float width, float height)
        {
            fxController = originalAvatar?.FxController;
            using (new EditorGUILayout.VerticalScope(GUILayout.Height(height)))
            {
                GUILayout.Label(LocalizeText.instance.langPair.fxLayerTitle, EditorStyles.boldLabel);
                if (editAvatar?.Descriptor == null)
                {
                    EditorGUILayout.HelpBox(LocalizeText.instance.langPair.chooseAvatar, MessageType.Info);
                    return false;
                }
                if (fxController == null || fxController.layers.Length == 0)
                {
                    EditorGUILayout.HelpBox(LocalizeText.instance.langPair.missingFxControllerMessageText, MessageType.Info);
                    if (GUILayout.Button(LocalizeText.instance.langPair.createFxControllerText))
                    {
                        CreatePlayableLayerController(originalAvatar, editAvatar);
                        vrcAvatarEditorGUI.OnToolChanged();
                    }
                    return false;
                }

                var layerNames = fxController.layers.Select(l => l.name).ToArray();
                editAvatar.TargetFxLayerIndex = Mathf.Clamp(editAvatar.TargetFxLayerIndex, 0, layerNames.Length - 1);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(LocalizeText.instance.langPair.layerLabel, GUILayout.Width(60));
                    using (var check = new EditorGUI.ChangeCheckScope())
                    {
                        var selected = EditorGUILayout.Popup(editAvatar.TargetFxLayerIndex, layerNames);
                        if (check.changed)
                        {
                            editAvatar.TargetFxLayerIndex = selected;
                            animationScroll = Vector2.zero;
                            faceEmotionGUI.ChangeSaveAnimationState();
                        }
                    }
                }
                if (Event.current.type == EventType.Repaint) LayerSelectionRect = GUILayoutUtility.GetLastRect();

                var states = VRCAvatarAnimationUtility.GetStates(fxController.layers[editAvatar.TargetFxLayerIndex]);
                using (var scroll = new EditorGUILayout.ScrollViewScope(animationScroll,
                    GUILayout.Height(Mathf.Max(36, height - ListHeaderHeight))))
                {
                    animationScroll = scroll.scrollPosition;
                    if (states.Length == 0) EditorGUILayout.HelpBox(LocalizeText.instance.langPair.haveNoAnimationClipMessageText, MessageType.Info);
                    for (int i = 0; i < states.Length; i++)
                    {
                        var state = states[i].state;
                        using (new EditorGUILayout.HorizontalScope(GUILayout.Height(StateRowHeight)))
                        {
                            GUILayout.Label(new GUIContent($"{i + 1}  {state.name}", state.name),
                                GUILayout.Width(Mathf.Clamp(width * 0.34f, 100, 230)));
                            if (state.motion is BlendTree)
                            {
                                using (new EditorGUI.DisabledScope(true))
                                {
                                    EditorGUILayout.ObjectField(state.motion, typeof(BlendTree), false, GUILayout.MinWidth(60));
                                    GUILayout.Button(LocalizeText.instance.langPair.edit, GUILayout.Width(42));
                                }
                                continue;
                            }
                            var clip = state.motion as AnimationClip;
                            using (var check = new EditorGUI.ChangeCheckScope())
                            {
                                var replacement = (AnimationClip)EditorGUILayout.ObjectField(clip, typeof(AnimationClip), false, GUILayout.MinWidth(60));
                                if (check.changed)
                                {
                                    Undo.RecordObject(state, "Change FX animation");
                                    state.motion = replacement;
                                    EditorUtility.SetDirty(fxController);
                                }
                            }
                            using (new EditorGUI.DisabledScope(clip == null || clip.name.StartsWith(VRCAvatarConstants.OFFICIAL_ANIMATION_PREFIX)))
                            {
                                if (GUILayout.Button(LocalizeText.instance.langPair.edit, GUILayout.Width(42))) LoadStateForEditing(i);
                            }
                        }
                    }
                    if (Event.current.type == EventType.Repaint)
                        StateContentHeight = states.Length == 0 ? 0 : GUILayoutUtility.GetLastRect().yMax;
                }
                if (Event.current.type == EventType.Repaint) StateListRect = GUILayoutUtility.GetLastRect();
            }
            return false;
        }

        public bool LoadStateForEditing(int stateIndex)
        {
            var controller = originalAvatar?.FxController;
            if (controller == null || editAvatar == null || editAvatar.TargetFxLayerIndex < 0
                || editAvatar.TargetFxLayerIndex >= controller.layers.Length) return false;
            var states = VRCAvatarAnimationUtility.GetStates(controller.layers[editAvatar.TargetFxLayerIndex]);
            if (stateIndex < 0 || stateIndex >= states.Length || !(states[stateIndex].state.motion is AnimationClip clip)) return false;
            if (vrcAvatarEditorGUI.CurrentTool != PPTAvatarEditorView.ToolFunc.FaceEmotion)
                vrcAvatarEditorGUI.CurrentTool = PPTAvatarEditorView.ToolFunc.FaceEmotion;
            faceEmotionGUI.LoadPreviewAnimation(clip);
            ((FaceEmotionView3)faceEmotionGUI).ChangeSaveAnimationState(clip.name, stateIndex, clip);
            vrcAvatarEditorGUI.Repaint();
            return true;
        }

        private static AnimatorController InstantiateFxController(string newFilePath)
        {
            string path = VRCSDKUtility.GetVRCSDKFilePath("Animation/Controllers/vrc_AvatarV3HandsLayer.controller");

            newFilePath = AssetDatabase.GenerateUniqueAssetPath(newFilePath);
            AssetDatabase.CopyAsset(path, newFilePath);
            var controller = AssetDatabase.LoadAssetAtPath(newFilePath, typeof(AnimatorController)) as AnimatorController;

            return controller;
        }

        private static void EnableCustomPlayableLayers(VRCAvatar avatar)
        {
            var baseLayerTypes = new VRCAvatarDescriptor.AnimLayerType[]
            {
                VRCAvatarDescriptor.AnimLayerType.Base,
                VRCAvatarDescriptor.AnimLayerType.Additive,
                VRCAvatarDescriptor.AnimLayerType.Gesture,
                VRCAvatarDescriptor.AnimLayerType.Action,
                VRCAvatarDescriptor.AnimLayerType.FX
            };

            var specialLayerTypes = new VRCAvatarDescriptor.AnimLayerType[]
            {
                VRCAvatarDescriptor.AnimLayerType.Sitting,
                VRCAvatarDescriptor.AnimLayerType.TPose,
                VRCAvatarDescriptor.AnimLayerType.IKPose
            };

            avatar.Descriptor.customizeAnimationLayers = true;
            avatar.Descriptor.baseAnimationLayers = baseLayerTypes
                                                        .Select(type => new VRCAvatarDescriptor.CustomAnimLayer
                                                        {
                                                            type = type
                                                        })
                                                        .ToArray();
            avatar.Descriptor.specialAnimationLayers = specialLayerTypes
                                                        .Select(type => new VRCAvatarDescriptor.CustomAnimLayer
                                                        {
                                                            type = type
                                                        })
                                                        .ToArray();
        }

        public static void CreateGestureController(VRCAvatar originalAvatar, VRCAvatar editAvatar)
        {
            if (!originalAvatar.Descriptor.customizeAnimationLayers)
            {
                EnableCustomPlayableLayers(originalAvatar);
                EnableCustomPlayableLayers(editAvatar);
            }

            if (originalAvatar.GestureController is null)
            {
                string saveFolderPath;
                if (originalAvatar.FxController != null)
                {
                    saveFolderPath = Path.GetDirectoryName(AssetDatabase.GetAssetPath(originalAvatar.FxController));
                }
                else
                {
                    saveFolderPath = "Assets/" + originalAvatar.Animator.gameObject.name + "/";
                }

                var fileName = $"Gesture_HandsLayer_{ originalAvatar.Animator.gameObject.name}.controller";
                var createdGestureController = InstantiateFxController(Path.Combine(saveFolderPath, fileName));

                originalAvatar.Descriptor.baseAnimationLayers[PLAYABLE_GESTURE_LAYER_INDEX].isDefault = false;
                editAvatar.Descriptor.baseAnimationLayers[PLAYABLE_GESTURE_LAYER_INDEX].isDefault = false;
                originalAvatar.Descriptor.baseAnimationLayers[PLAYABLE_GESTURE_LAYER_INDEX].animatorController = createdGestureController;
                editAvatar.Descriptor.baseAnimationLayers[PLAYABLE_GESTURE_LAYER_INDEX].animatorController = createdGestureController;
            }

            originalAvatar.LoadAvatarInfo();
            editAvatar.LoadAvatarInfo();
        }

        public static void CreatePlayableLayerController(VRCAvatar originalAvatar, VRCAvatar editAvatar)
        {
            var fileName = $"Fx_HandsLayer_{ originalAvatar.Animator.gameObject.name}.controller";
            var saveFolderPath = "Assets/" + originalAvatar.Animator.gameObject.name + "/";
            var fullFolderPath = Path.GetFullPath(saveFolderPath);
            if (!Directory.Exists(fullFolderPath))
            {
                Directory.CreateDirectory(fullFolderPath);
                AssetDatabase.Refresh();
            }
            var createdFxController = InstantiateFxController(Path.Combine(saveFolderPath, fileName));

            // まばたき防止機構をつける
            SetNoBlink(createdFxController);

            // WriteDefaultsがオフによる不具合を防止する機構をつける
            if (!VRCAvatarAnimationUtility.UseWriteDefaults(createdFxController))
            {
                if (!VRCAvatarAnimationUtility.ExistLayer(createdFxController, VRCAvatarConstants.RESET_LAYER_NAME))
                {
                    VRCAvatarAnimationUtility.AddDefaultFaceLayer(createdFxController, originalAvatar, editAvatar);
                    editAvatar.TargetFxLayerIndex++;
                }
            }

            // FXレイヤーの不具合で一番上のレイヤーのAvatarMaskが設定されているとハンドアニメーションがうまくいかなくなるので対策
            createdFxController.layers = createdFxController.layers.Select((x, index) =>
            {
                if (index == 0) x.avatarMask = null;
                return x;
            }).ToArray();

            if (!originalAvatar.Descriptor.customizeAnimationLayers)
            {
                EnableCustomPlayableLayers(originalAvatar);
                EnableCustomPlayableLayers(editAvatar);
            }
            originalAvatar.Descriptor.baseAnimationLayers[PLAYABLE_FX_LAYER_INDEX].isDefault = false;
            originalAvatar.Descriptor.baseAnimationLayers[PLAYABLE_FX_LAYER_INDEX].animatorController = createdFxController;
            editAvatar.Descriptor.baseAnimationLayers[PLAYABLE_FX_LAYER_INDEX].isDefault = false;
            editAvatar.Descriptor.baseAnimationLayers[PLAYABLE_FX_LAYER_INDEX].animatorController = createdFxController;

            if (originalAvatar.GestureController is null)
            {
                fileName = $"Gesture_HandsLayer_{ originalAvatar.Animator.gameObject.name}.controller";
                var createdGestureController = InstantiateFxController(Path.Combine(saveFolderPath, fileName));

                originalAvatar.Descriptor.baseAnimationLayers[PLAYABLE_GESTURE_LAYER_INDEX].isDefault = false;
                originalAvatar.Descriptor.baseAnimationLayers[PLAYABLE_GESTURE_LAYER_INDEX].animatorController = createdGestureController;
                editAvatar.Descriptor.baseAnimationLayers[PLAYABLE_GESTURE_LAYER_INDEX].isDefault = false;
                editAvatar.Descriptor.baseAnimationLayers[PLAYABLE_GESTURE_LAYER_INDEX].animatorController = createdGestureController;
            }

            originalAvatar.LoadAvatarInfo();
            editAvatar.LoadAvatarInfo();
        }

        private static void SetNoBlink(AnimatorController fxController)
        {
            var layers = fxController.layers.Where(
                            l => l.name == VRCAvatarConstants.FX_LEFT_HAND_LAYER_NAME ||
                            l.name == VRCAvatarConstants.FX_RIGHT_HAND_LAYER_NAME);
            foreach (var layer in layers)
            {
                var states = layer.stateMachine.states;

                foreach (var state in states)
                {
                    var stateName = state.state.name;
                    if (!EMOTIONSTATES.Contains(stateName)) continue;

                    var control = state.state.AddStateMachineBehaviour(typeof(VRCAnimatorTrackingControl)) as VRCAnimatorTrackingControl;

                    if (stateName == VRCAvatarConstants.IDLE_STATE_NAME)
                    {
                        control.trackingEyes = VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.Tracking;
                    }
                    else
                    {
                        control.trackingEyes = VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.Animation;
                    }
                }
            }
        }
    }
}
