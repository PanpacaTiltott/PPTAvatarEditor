using System.Linq;
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using PPTAvatarEditor.Base;
using VRCAvatar = PPTAvatarEditor.Avatars3.VRCAvatar3;
using AnimationsGUI = PPTAvatarEditor.Avatars3.AnimationsView3;
using PPTAvatarEditor.Utilities;

namespace PPTAvatarEditor.Avatars3
{
    public class FaceEmotionView3 : FaceEmotionViewBase
    {
        private int selectedStateIndex = 0;
        private bool setLeftAndRight = true;
        private bool assignToFxState;

        private ChildAnimatorState[] states = Array.Empty<ChildAnimatorState>();

        private void RefreshStates()
        {
            states = Array.Empty<ChildAnimatorState>();
            if (editAvatar?.FxController == null || editAvatar.FxController.layers.Length == 0) return;
            editAvatar.TargetFxLayerIndex = Mathf.Clamp(editAvatar.TargetFxLayerIndex, 0, editAvatar.FxController.layers.Length - 1);
            states = VRCAvatarAnimationUtility.GetStates(editAvatar.FxController.layers[editAvatar.TargetFxLayerIndex]);
            selectedStateIndex = Mathf.Clamp(selectedStateIndex, 0, Mathf.Max(0, states.Length - 1));
        }

        protected override void DrawCreatedAnimationSettingsGUI()
        {
            base.DrawCreatedAnimationSettingsGUI();

            assignToFxState = EditorGUILayout.ToggleLeft(Text.assignClipToFx, assignToFxState);
            if (!assignToFxState)
            {
                EditorGUILayout.HelpBox(Text.standaloneClipHint, MessageType.None);
                return;
            }

            RefreshStates();

            string[] stateNames;

            if (editAvatar.FxController != null && editAvatar.FxController.layers.Length > 0)
            {
                states = VRCAvatarAnimationUtility.GetStates(editAvatar.FxController.layers[editAvatar.TargetFxLayerIndex]);
                stateNames = states.Select((s, i) => $"{i + 1}:{s.state.name}").ToArray();

                EditorGUILayout.LabelField(Text.layerLabel, editAvatar.FxController.layers[editAvatar.TargetFxLayerIndex].name);

                // Stateがないとき、自動設定できない
                if (states.Any())
                {
                    using (var check = new EditorGUI.ChangeCheckScope())
                    {
                        selectedStateIndex = EditorGUILayout.Popup(
                            Text.stateLabel,
                            selectedStateIndex,
                            stateNames);

                        if (check.changed)
                        {
                            // TODO: 手のアニメーションファイルを変更する？
                        }
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(LocalizeText.instance.langPair.createOnlyMessageText, MessageType.Info);
                }

                if (editAvatar.GestureController != null)
                {
                    ChildAnimatorState handState = default;
                    if (states.Any())
                    {
                        var targetLayer = editAvatar.GestureController.layers
                                            .Where(l => l.name == editAvatar.FxController.layers[editAvatar.TargetFxLayerIndex].name)
                                            .SingleOrDefault();

                        if (targetLayer != null)
                        {
                            handState = targetLayer
                                            .stateMachine.states
                                            .Where(s => !(s.state.motion is BlendTree) &&
                                                        s.state.name == states[selectedStateIndex].state.name)
                                            .SingleOrDefault();
                        }
                    }

                    // LayerまたはStateが見つからない時はGestureまわりは利用できない
                    if (handState.state != null)
                    {
                        handPoseAnim = handState.state.motion as AnimationClip;
                        using (var check = new EditorGUI.ChangeCheckScope())
                        {
                            handPoseAnim = GatoGUILayout.ObjectField(
                                LocalizeText.instance.langPair.handPoseAnimClipLabel,
                                handPoseAnim);

                            if (check.changed)
                            {
                                handState.state.motion = handPoseAnim;
                                EditorUtility.SetDirty(editAvatar.GestureController);
                            }
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(LocalizeText.instance.langPair.handPoseChangeFailedMessageText, MessageType.Info);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(LocalizeText.instance.langPair.missingGestureControllerMessageText, MessageType.Warning);

                    GatoGUILayout.Button(
                        LocalizeText.instance.langPair.createGestureControllerText,
                        () =>
                        {
                            AnimationsGUI.CreateGestureController(originalAvatar, editAvatar);
                            parentWindow.OnToolChanged();
                        });
                }

                setLeftAndRight = EditorGUILayout.ToggleLeft(LocalizeText.instance.langPair.setLeftAndRightHandLayerText, setLeftAndRight);
            }
            else
            {
                EditorGUILayout.HelpBox(LocalizeText.instance.langPair.missingFxControllerMessageText, MessageType.Error);

                GatoGUILayout.Button(
                    LocalizeText.instance.langPair.createFxControllerText,
                    () =>
                    {
                        AnimationsGUI.CreatePlayableLayerController(originalAvatar, editAvatar);
                        parentWindow.OnToolChanged();
                    });
            }
        }

        protected override void DrawCreateButtonGUI()
        {
            RefreshStates();
            var validName = !string.IsNullOrWhiteSpace(animName)
                && animName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
            // Like upstream, an empty selection can be saved as an empty clip.
            using (new EditorGUI.DisabledScope(!validName || (assignToFxState && states.Length == 0)))
            {
                if (GUILayout.Button(assignToFxState ? Text.saveAndAssign : Text.saveClip, GUILayout.Width(128), GUILayout.Height(24)))
                {
                    var targetState = assignToFxState && states.Length > 0 ? states[selectedStateIndex].state : null;
                    OnCreateButtonClicked(originalAvatar, editAvatar, animName, targetState, setLeftAndRight);
                    parentWindow.ShowNotification(new GUIContent(Text.clipSaved));
                }
            }
        }

        public override void OnLoadedAnimationProperties()
        {
            base.OnLoadedAnimationProperties();
            ChangeSaveAnimationState();
        }

        public override void OnSetToDefaultButtonClick(VRCAvatar editAvatar, VRCAvatar originalAvatar)
        {
            base.OnSetToDefaultButtonClick(editAvatar, originalAvatar);

            // TODO: WriteDefaultsオフのときに対応しないといけない
            //// DefaultFaceレイヤーを使っているときにそのAnimationファイルも更新する
            //if (originalAvatar.FxController.layers.Any(l => l.name == VRCAvatarConstants.RESET_LAYER_NAME))
            //{
            //    var defaultLayer = originalAvatar.FxController.layers
            //                            .SingleOrDefault(l => l.name == VRCAvatarConstants.RESET_LAYER_NAME);

            //    if (defaultLayer == null)
            //    {
            //        Debug.LogError("Not Found Default Face Layer");
            //        return;
            //    }

            //    var targetState = defaultLayer.stateMachine.states
            //                        .SingleOrDefault(s => s.state.name == VRCAvatarConstants.RESET_FACE_STATE_NAME);

            //    if (targetState.state == null)
            //    {
            //        Debug.LogError("Not Found Default Face State");
            //        return;
            //    }

            //    var defaultFaceAnimation = FaceEmotion.CreateBlendShapeAnimationClip(
            //                                    VRCAvatarConstants.DEFAULT_FACE_ANIMATION_NAME,
            //                                    originalAvatar.AnimSavedFolderPath,
            //                                    editAvatar);

            //    targetState.state.motion = defaultFaceAnimation;
            //    EditorUtility.SetDirty(originalAvatar.FxController);
            //}
        }

        public override void ChangeSaveAnimationState()
        {
            ChangeSaveAnimationState("", 0, null);
        }

        public void OnCreateButtonClicked(VRCAvatar originalAvatar, VRCAvatar editAvatar, string animName, AnimatorState state = null, bool setLeftAndRight = true)
        {
            var controller = originalAvatar.FxController;

            var createdAnimClip = faceEmotion.CreateBlendShapeAnimationClip(
                                    animName,
                                    originalAvatar.AnimSavedFolderPath,
                                    editAvatar);

            // Stateがない場合は作成のみ
            if (state != null)
            {
                state.motion = createdAnimClip;
                EditorUtility.SetDirty(controller);

                // 可能であればもう一方の手も同じAnimationClipを設定する
                if (setLeftAndRight)
                {
                    var layer = editAvatar.FxController.layers[editAvatar.TargetFxLayerIndex];
                    var targetLayer = VRCAvatarAnimationUtility.DetectCorrespondenceLayer(layer, editAvatar.FxController);

                    if (targetLayer != null)
                    {
                        var targetStateName = state.name;
                        var targetState = targetLayer.stateMachine.states
                                            .Where(s => s.state.name == targetStateName)
                                            .SingleOrDefault();

                        if (targetState.state != null)
                        {
                            targetState.state.motion = createdAnimClip;
                            EditorUtility.SetDirty(controller);
                        }
                    }
                }
            }
            originalAvatar.FxController = controller;
            editAvatar.FxController = controller;
        }

        public void ChangeSaveAnimationState(
                string animName = "",
                int selectStateIndex = 0,
                AnimationClip handPoseAnim = null)
        {
            this.animName = string.IsNullOrWhiteSpace(animName) ? DEFAULT_ANIM_NAME : animName;
            selectedStateIndex = selectStateIndex;
            // TODO: 一時対処
            this.selectedHandAnim = HandPose.HandPoseType.NoSelection;
            if (handPoseAnim is null)
                handPoseAnim = HandPose.GetHandAnimationClip(this.selectedHandAnim);
            this.handPoseAnim = handPoseAnim;
        }

        private void ChangeSelectionHandAnimation()
        {
            if (usePreviousAnimationOnHandAnimation)
            {
                // TODO: 以前のアニメーションの取得
            }
            else
            {
                handPoseAnim = HandPose.GetHandAnimationClip(selectedHandAnim);
            }
        }

        private void SetupForNoUseWriteDefaultIfNeeded(AnimatorController controller, VRCAvatar originalAvatar, VRCAvatar editAvatar)
        {
            if (!VRCAvatarAnimationUtility.UseWriteDefaults(controller))
            {
                if (!VRCAvatarAnimationUtility.ExistLayer(controller, VRCAvatarConstants.RESET_LAYER_NAME))
                {
                    VRCAvatarAnimationUtility.AddDefaultFaceLayer(controller, originalAvatar, editAvatar);
                    editAvatar.TargetFxLayerIndex++;
                }

                // Idleステートに何かしらが入っていないとバグるので対策
                var fxLeftHandIdleState = VRCAvatarAnimationUtility.GetFXLayerIdleState(editAvatar.FxController, VRCAvatarAnimationUtility.HandType.LEFT);
                var fxRightHandIdleState = VRCAvatarAnimationUtility.GetFXLayerIdleState(editAvatar.FxController, VRCAvatarAnimationUtility.HandType.RIGHT);
                if (fxLeftHandIdleState.state.motion == null || fxRightHandIdleState.state.motion == null)
                {
                    var emptyAnimation = VRCAvatarAnimationUtility.GetOrCreateEmptyAnimation(originalAvatar);
                    if (fxLeftHandIdleState.state.motion == null)
                    {
                        fxLeftHandIdleState.state.motion = emptyAnimation;
                    }
                    if (fxRightHandIdleState.state.motion == null)
                    {
                        fxRightHandIdleState.state.motion = emptyAnimation;
                    }
                    EditorUtility.SetDirty(originalAvatar.FxController);
                    EditorUtility.SetDirty(editAvatar.FxController);
                }
            }
        }
    }
}
