using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;
using PPTAvatarEditor.Interfaces;
using VRCAvatar = PPTAvatarEditor.Avatars3.VRCAvatar3;
using AnimationsGUI = PPTAvatarEditor.Avatars3.AnimationsView3;

namespace PPTAvatarEditor.Base
{
    public abstract class FaceEmotionViewBase : Editor, IPPTAvatarEditorView
    {
        protected IFaceEmotion faceEmotion;

        protected VRCAvatar editAvatar;
        protected VRCAvatar originalAvatar;
        protected PPTAvatarEditorView parentWindow;
        protected AnimationsGUI animationsGUI;
        protected LanguageKeyPair Text => LocalizeText.instance.langPair;

        protected static readonly string DEFAULT_ANIM_NAME = "faceAnim";
        protected HandPose.HandPoseType selectedHandAnim = HandPose.HandPoseType.NoSelection;

        protected Vector2 scrollPos = Vector2.zero;
        private Vector2 saveOptionsScroll;
        private string search = string.Empty;
        private int category;
        private bool nonzeroOnly;
        private bool showSaveOptions = true;
        private BlendShapeCategories categories;
        private SkinnedMesh categoryMesh;
        private BlendShapeEditHistory history;
        internal Rect BlendShapeViewportRect { get; private set; }
        internal Rect SavePanelRect { get; private set; }
        internal string[] CategoryLabels => categories?.Labels ?? new[] { Text.allCategory };
        public bool CanUndo => history != null && history.CanUndo;
        public bool CanRedo => history != null && history.CanRedo;

        public enum SortType
        {
            UnSort,
            AToZ,
        }

        public SortType selectedSortType = SortType.UnSort;
        public List<string> blendshapeExclusions = new List<string> { "vrc.v_", "vrc.blink_", "vrc.lowerlid_", "vrc.owerlid_", "mmd" };

        protected bool isOpeningBlendShapeExclusionList = false;

        protected string animName;

        protected AnimationClip handPoseAnim;

        protected bool usePreviousAnimationOnHandAnimation;

        public void Initialize(IFaceEmotion faceEmotion, VRCAvatar editAvatar, VRCAvatar originalAvatar, string saveFolderPath, EditorWindow window, AnimationsGUI animationsGUI)
        {
            if (this.editAvatar != editAvatar || history == null)
            {
                history = new BlendShapeEditHistory(editAvatar.SkinnedMeshList);
                categoryMesh = BlendShapeCategories.FindBody(editAvatar.SkinnedMeshList);
                categories = new BlendShapeCategories(categoryMesh == null
                    ? Enumerable.Empty<SkinnedMesh>() : new[] { categoryMesh });
                foreach (var mesh in editAvatar.SkinnedMeshList) mesh.IsOpenBlendShapes = mesh == categoryMesh;
                category = 0;
                search = string.Empty;
                scrollPos = Vector2.zero;
                showSaveOptions = true;
            }
            this.faceEmotion = faceEmotion;
            this.editAvatar = editAvatar;
            this.originalAvatar = originalAvatar;
            animName = DEFAULT_ANIM_NAME;
            this.parentWindow = window as PPTAvatarEditorView;
            this.animationsGUI = animationsGUI;
        }

        public virtual bool DrawGUI(GUILayoutOption[] layoutOptions)
        {
            if (editAvatar == null || editAvatar.Descriptor == null) return false;
            using (new EditorGUILayout.VerticalScope(layoutOptions ?? new[] { GUILayout.ExpandHeight(true) }))
            {
                DrawFunctionButtons();
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    using (new EditorGUI.DisabledScope(!CanUndo))
                        if (GUILayout.Button(new GUIContent(Text.undoPreview, Text.undoHint + ": " + history.UndoLabel),
                            EditorStyles.toolbarButton, GUILayout.Width(64))) UndoPreviewEdit();
                    using (new EditorGUI.DisabledScope(!CanRedo))
                        if (GUILayout.Button(new GUIContent(Text.redoPreview, Text.redoHint + ": " + history.RedoLabel),
                            EditorStyles.toolbarButton, GUILayout.Width(64))) RedoPreviewEdit();
                    search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                    if (GUILayout.Button(new GUIContent("×", Text.clearSearch), EditorStyles.toolbarButton, GUILayout.Width(24)))
                    {
                        search = string.Empty;
                        GUI.FocusControl(null);
                    }
                    nonzeroOnly = GUILayout.Toggle(nonzeroOnly, new GUIContent(Text.nonzeroOnly, Text.nonzeroHint),
                        EditorStyles.toolbarButton, GUILayout.Width(66));
                }
                var categoryHeight = DrawCategories();

                // A ScrollView's MinHeight can also constrain its maximum in IMGUI.
                // Give the list the actual remaining height instead of relying on stretch.
                var optionsHeight = showSaveOptions ? Mathf.Min(180, parentWindow.position.height * 0.28f) : 0;
                var listHeight = Mathf.Max(80, parentWindow.ToolContentRect.height - 94 - categoryHeight - optionsHeight);
                if (editAvatar.SkinnedMeshList != null) BlendShapeListGUI(listHeight);

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(Text.clipName, GUILayout.Width(80));
                        animName = EditorGUILayout.TextField(animName);
                        DrawCreateButtonGUI();
                    }
                    showSaveOptions = EditorGUILayout.Foldout(showSaveOptions, Text.saveOptions, true);
                    if (showSaveOptions)
                    {
                        using (var scroll = new EditorGUILayout.ScrollViewScope(saveOptionsScroll,
                            GUILayout.Height(Mathf.Min(180, parentWindow.position.height * 0.28f))))
                        {
                            saveOptionsScroll = scroll.scrollPosition;
                            DrawCreatedAnimationSettingsGUI();
                        }
                    }
                }
                if (Event.current.type == EventType.Repaint) SavePanelRect = GUILayoutUtility.GetLastRect();
            }
            if (GUIUtility.hotControl == 0) history.EndGesture();
            return false;
        }

        private float DrawCategories()
        {
            var labels = CategoryLabels;
            int columns = Mathf.Max(1, Mathf.FloorToInt(parentWindow.ToolContentRect.width / 100));
            int rows = Mathf.CeilToInt((float)labels.Length / columns);
            float height = rows > 3 ? 22 : rows * 22;
            // Many authored sections remain reachable without pushing the editor off-screen.
            int next = rows > 3
                ? EditorGUILayout.Popup(category, labels, GUILayout.Height(height))
                : GUILayout.SelectionGrid(category, labels, columns, EditorStyles.toolbarButton, GUILayout.Height(height));
            if (category != next) { category = next; scrollPos = Vector2.zero; }
            return height;
        }

        public void UndoPreviewEdit()
        {
            history?.Undo();
            parentWindow?.Repaint();
        }

        public void RedoPreviewEdit()
        {
            history?.Redo();
            parentWindow?.Repaint();
        }

        internal void HandleHistoryShortcuts()
        {
            var evt = Event.current;
            if (evt.type != EventType.KeyDown || EditorGUIUtility.editingTextField
                || (!evt.control && !evt.command) || evt.alt) return;
            if (evt.keyCode == KeyCode.Z)
            {
                if (evt.shift) RedoPreviewEdit(); else UndoPreviewEdit();
                evt.Use();
            }
            else if (evt.keyCode == KeyCode.Y) { RedoPreviewEdit(); evt.Use(); }
        }

        private void EditPreview(string label, Action change, string gesture = null)
        {
            if (history == null) change();
            else history.Apply(label, change, gesture);
            parentWindow?.Repaint();
        }

        public void ResetPreviewToDefault()
        {
            EditPreview(Text.resetToDefaultButtonText, () => faceEmotion.ResetToDefaultFaceEmotion(editAvatar));
            ChangeSaveAnimationState();
        }

        public void LoadPreviewAnimation(AnimationClip clip)
        {
            if (clip != null)
                EditPreview(Text.loadAnimationButtonText + ": " + clip.name, () => faceEmotion.ApplyAnimationProperties(clip, editAvatar));
        }

        protected void DrawFunctionButtons()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {

                GatoGUILayout.Button(
                    LocalizeText.instance.langPair.loadAnimationButtonText,
                    () => {
                        faceEmotion.LoadAnimationProperties(this, parentWindow);
                    },
                    editAvatar.Descriptor != null);

                GatoGUILayout.Button(
                    LocalizeText.instance.langPair.setToDefaultButtonText,
                    () => {
                        if (EditorUtility.DisplayDialog(
                                LocalizeText.instance.langPair.setToDefaultDialogTitleText,
                                LocalizeText.instance.langPair.setToDefaultDialogMessageText,
                                LocalizeText.instance.langPair.ok, LocalizeText.instance.langPair.cancel))
                        {
                            OnSetToDefaultButtonClick(editAvatar, originalAvatar);
                        }
                    },
                    editAvatar.Descriptor != null);

                GatoGUILayout.Button(
                    LocalizeText.instance.langPair.resetToDefaultButtonText,
                    () => {
                        ResetPreviewToDefault();
                    },
                    editAvatar.Descriptor != null);
            }
        }

        protected virtual void DrawCreatedAnimationSettingsGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent(originalAvatar.AnimSavedFolderPath, originalAvatar.AnimSavedFolderPath), EditorStyles.miniLabel);

                GatoGUILayout.Button(
                    LocalizeText.instance.langPair.selectFolder,
                    () => {
                        var folder = EditorUtility.OpenFolderPanel(LocalizeText.instance.langPair.selectFolderDialogMessageText, originalAvatar.AnimSavedFolderPath, string.Empty);
                        if (string.IsNullOrEmpty(folder)) return;
                        var relative = FileUtil.GetProjectRelativePath(folder).Replace('\\', '/');
                        if (relative != "Assets" && !relative.StartsWith("Assets/", StringComparison.Ordinal))
                        {
                            EditorUtility.DisplayDialog("PPAE", Text.assetsFolderOnly, Text.ok);
                            return;
                        }
                        originalAvatar.AnimSavedFolderPath = relative.TrimEnd('/') + "/";
                        parentWindow.animationsGUI.UpdateSaveFolderPath(originalAvatar.AnimSavedFolderPath);
                    },
                    true,
                    GUILayout.Width(100));
            }

            EditorGUILayout.Space();
        }

        protected abstract void DrawCreateButtonGUI();

        public void DrawSettingsGUI()
        {
            EditorGUILayout.LabelField(Text.faceEmotionTitle, EditorStyles.boldLabel);

            selectedSortType = (SortType)EditorGUILayout.Popup(Text.sortTypeLabel, (int)selectedSortType,
                new[] { Text.sortOriginal, Text.sortAlphabetical });

            isOpeningBlendShapeExclusionList = EditorGUILayout.Foldout(isOpeningBlendShapeExclusionList, LocalizeText.instance.langPair.blendShapeExclusionsLabel);
            if (isOpeningBlendShapeExclusionList)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    for (int i = 0; i < blendshapeExclusions.Count; i++)
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            blendshapeExclusions[i] = EditorGUILayout.TextField(blendshapeExclusions[i]);
                            if (GUILayout.Button(LocalizeText.instance.langPair.remove))
                                blendshapeExclusions.RemoveAt(i);
                        }
                    }
                }

                using (new GUILayout.HorizontalScope())
                {
                    GatoGUILayout.Button(
                        LocalizeText.instance.langPair.add,
                        () => {
                            blendshapeExclusions.Add(string.Empty);
                        });
                }
            }

            usePreviousAnimationOnHandAnimation = EditorGUILayout.ToggleLeft(LocalizeText.instance.langPair.usePreviousAnimationOnHandAnimationLabel, usePreviousAnimationOnHandAnimation);
        }

        public virtual void OnSetToDefaultButtonClick(VRCAvatar editAvatar, VRCAvatar originalAvatar)
        {
            faceEmotion.SetToDefaultFaceEmotion(editAvatar, originalAvatar);
        }

        public abstract void ChangeSaveAnimationState();

        public void LoadSettingData(SettingData settingAsset)
        {
            selectedSortType = settingAsset.selectedSortType;
            blendshapeExclusions = new List<string>(settingAsset.blendshapeExclusions);
            usePreviousAnimationOnHandAnimation = settingAsset.usePreviousAnimationOnHandAnimation;
        }

        public void SaveSettingData(ref SettingData settingAsset)
        {
            settingAsset.selectedSortType = selectedSortType;
            settingAsset.blendshapeExclusions = new List<string>(blendshapeExclusions);
            settingAsset.usePreviousAnimationOnHandAnimation = usePreviousAnimationOnHandAnimation;
        }

        public void Dispose()
        {
            if (faceEmotion != null && editAvatar != null && editAvatar.Descriptor != null)
            {
                faceEmotion.ResetToDefaultFaceEmotion(editAvatar);
            }
            history = null;
        }

        protected void BlendShapeListGUI(float height)
        {
            using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollPos, GUILayout.Height(height)))
            {
                scrollPos = scrollView.scrollPosition;
                int resultCount = 0;
                foreach (IFaceEmotionSkinnedMesh mesh in editAvatar.SkinnedMeshList)
                {
                    if (mesh.Renderer == null || mesh.BlendShapeCount <= 0) continue;
                    var visible = mesh.Blendshapes.Where(shape => MatchesFilter(mesh, shape)).ToList();
                    if (visible.Count == 0) continue;
                    resultCount += visible.Count;

                    using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                    {
                        mesh.IsOpenBlendShapes = EditorGUILayout.Foldout(mesh.IsOpenBlendShapes, mesh.Obj.name, true);
                        GUILayout.Label(string.Format(Text.itemCount, visible.Count), EditorStyles.miniLabel, GUILayout.Width(58));
                        using (var check = new EditorGUI.ChangeCheckScope())
                        {
                            var all = GUILayout.Toggle(mesh.IsContainsAll,
                                new GUIContent(Text.selectAll, Text.selectAllHint),
                                GUILayout.Width(88));
                            if (check.changed) EditPreview(Text.selectForExport, () =>
                            {
                                mesh.IsContainsAll = all;
                                faceEmotion.SetContainsAll(all, mesh.Blendshapes);
                            });
                        }
                    }
                    if (!mesh.IsOpenBlendShapes) continue;
                    for (int row = 0; row < visible.Count; row++) DrawBlendShapeRow(mesh, visible[row], row);
                }
                if (resultCount == 0)
                    EditorGUILayout.HelpBox(Text.noMatchingShapes, MessageType.Info);
            }
            if (Event.current.type == EventType.Repaint) BlendShapeViewportRect = GUILayoutUtility.GetLastRect();
        }

        private bool MatchesFilter(IFaceEmotionSkinnedMesh mesh, SkinnedMesh.BlendShape shape)
        {
            if (shape.IsExclusion) return false;
            if (mesh == categoryMesh && categories != null && !categories.Contains(category, shape)) return false;
            if (nonzeroOnly && Mathf.Abs(mesh.Renderer.GetBlendShapeWeight(shape.Id)) < 0.001f) return false;
            if (string.IsNullOrWhiteSpace(search)) return true;
            return search.Split(new[] { ' ', '\u3000' }, StringSplitOptions.RemoveEmptyEntries)
                .All(term => shape.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawBlendShapeRow(IFaceEmotionSkinnedMesh mesh, SkinnedMesh.BlendShape shape, int row)
        {
            var rect = GUILayoutUtility.GetRect(0, 23, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && row % 2 == 0)
                EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                    ? new Color(1, 1, 1, 0.025f) : new Color(0, 0, 0, 0.025f));

            rect.y += 2;
            rect.height = EditorGUIUtility.singleLineHeight;
            var toggleRect = new Rect(rect.x + 3, rect.y, 18, rect.height);
            var included = EditorGUI.Toggle(toggleRect,
                new GUIContent(string.Empty, Text.includeInClip), shape.IsContains);
            if (included != shape.IsContains) OnBlendShapeToggleClicked(shape, included);
            var labelWidth = Mathf.Clamp(rect.width * 0.38f, 110, 250);
            var labelRect = new Rect(toggleRect.xMax + 3, rect.y, labelWidth, rect.height);
            GUI.Label(labelRect, new GUIContent(shape.Name, shape.Name), EditorStyles.label);

            var maxRect = new Rect(rect.xMax - 32, rect.y, 30, rect.height);
            var minRect = new Rect(maxRect.x - 26, rect.y, 24, rect.height);
            var valueRect = new Rect(minRect.x - 49, rect.y, 46, rect.height);
            var sliderRect = new Rect(labelRect.xMax + 6, rect.y + 1,
                Mathf.Max(20, valueRect.x - labelRect.xMax - 12), rect.height);
            var value = mesh.Renderer.GetBlendShapeWeight(shape.Id);
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                value = GUI.HorizontalSlider(sliderRect, value, 0, 100);
                value = EditorGUI.DelayedFloatField(valueRect, value);
                if (GUI.Button(minRect, "0", EditorStyles.miniButtonLeft)) value = 0;
                if (GUI.Button(maxRect, "100", EditorStyles.miniButtonRight)) value = 100;
                if (check.changed || !Mathf.Approximately(value, mesh.Renderer.GetBlendShapeWeight(shape.Id)))
                    OnBlendShapeSliderChanged(mesh, shape, Mathf.Clamp(value, 0, 100));
            }
        }

        public virtual void OnLoadedAnimationProperties()
        {
            EditPreview(Text.loadAnimationButtonText, () =>
                faceEmotion.ApplyAnimationProperties(ScriptableSingleton<SendData>.instance.loadingProperties, editAvatar));
        }

        public void OnBlendShapeSelectToggleButtonClicked(IFaceEmotion faceEmotion, IFaceEmotionSkinnedMesh skinnedMesh)
        {
            EditPreview(Text.selectForExport, () => faceEmotion.SetContainsAll(skinnedMesh.IsContainsAll, skinnedMesh.Blendshapes));
        }

        public void OnBlendShapeToggleClicked(SkinnedMesh.BlendShape blendShape, bool value)
        {
            EditPreview(Text.selectForExport, () => blendShape.IsContains = value);
        }

        public void OnBlendShapeSliderChanged(IFaceEmotionSkinnedMesh skinnedMesh, SkinnedMesh.BlendShape blendShape, float value)
        {
            var gesture = Event.current != null && GUIUtility.hotControl != 0
                ? skinnedMesh.Renderer.GetInstanceID() + ":" + blendShape.Id + ":" + GUIUtility.hotControl : null;
            EditPreview(blendShape.Name, () => skinnedMesh.Renderer.SetBlendShapeWeight(blendShape.Id, value), gesture);
        }

        public void OnBlendShapeMinButtonClicked(IFaceEmotion faceEmotion, IFaceEmotionSkinnedMesh skinnedMesh, SkinnedMesh.BlendShape blendShape)
        {
            EditPreview(blendShape.Name, () => faceEmotion.SetBlendShapeMinValue(skinnedMesh.Renderer, blendShape.Id));
        }

        public void OnBlendShapeMaxButtonClicked(IFaceEmotion faceEmotion, IFaceEmotionSkinnedMesh skinnedMesh, SkinnedMesh.BlendShape blendShape)
        {
            EditPreview(blendShape.Name, () => faceEmotion.SetBlendShapeMaxValue(skinnedMesh.Renderer, blendShape.Id));
        }
    }
}
