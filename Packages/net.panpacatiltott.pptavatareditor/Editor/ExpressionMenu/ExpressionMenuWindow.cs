// Copyright (C) 2026 PanpacaTiltott. Distributed under the Zlib License.
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace PPTAvatarEditor.Modules
{
    public sealed class ExpressionMenuWindow : EditorWindow
    {
        [SerializeField] private VRCAvatarDescriptor avatar;
        [SerializeField] private string menuName = "PPAE";
        [SerializeField] private string folder = "";
        [SerializeField] private List<ExpressionMenuEntry> entries = new List<ExpressionMenuEntry>();
        [SerializeField] private GameObject generatedPrefab;
        private Vector2 scroll;
        private string error;
        private static int Language { get { var code = EditorSetting.instance.Data.language ?? "JA"; return code.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 1 : code.StartsWith("ko", StringComparison.OrdinalIgnoreCase) ? 2 : 0; } }
        private static string L(string ja, string en, string ko) => Language == 1 ? en : Language == 2 ? ko : ja;

        [MenuItem("PPAE/Expression Menu", false, 12)]
        public static void Open()
        {
            var window = GetWindow<ExpressionMenuWindow>();
            if (Selection.activeGameObject != null)
                window.SetAvatar(Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>());
            if (window.avatar == null)
            {
                var selected = Resources.FindObjectsOfTypeAll<PPTAvatarEditorView>().FirstOrDefault(w => w.TargetAvatarDescriptor != null);
                if (selected != null) window.SetAvatar(selected.TargetAvatarDescriptor);
            }
            window.AddSelection();
            window.Show();
        }
        private void OnEnable()
        {
            minSize = new Vector2(540, 340);
            titleContent = new GUIContent(L("表情呼び出し", "Expression Menu", "표정 메뉴"));
        }
        public void SetAvatar(VRCAvatarDescriptor value)
        {
            if (value == null || value == avatar) return;
            avatar = value; generatedPrefab = null;
            folder = "Assets/PPAE/" + ExpressionMenuBuilder.SafeName(avatar.name) + "/ExpressionMenus";
        }
        private void AddSelection()
        {
            foreach (var clip in Selection.objects.OfType<AnimationClip>())
                if (entries.All(e => e.Clip != clip)) entries.Add(new ExpressionMenuEntry { Clip = clip, Name = clip.name });
        }
        private void OnGUI()
        {
            titleContent.text = L("表情呼び出し", "Expression Menu", "표정 메뉴");
            EditorGUILayout.Space(6);
            var nextAvatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(L("アバター", "Avatar", "아바타"), avatar, typeof(VRCAvatarDescriptor), true);
            if (nextAvatar != avatar) { if (nextAvatar == null) avatar = null; else SetAvatar(nextAvatar); }
            menuName = EditorGUILayout.TextField(L("メニュー名", "Menu name", "메뉴 이름"), menuName);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(L("保存先", "Folder", "저장 폴더"));
                EditorGUILayout.SelectableLabel(folder, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button(L("選択", "Select", "선택"), GUILayout.Width(60)))
                {
                    string chosen = EditorUtility.OpenFolderPanel(L("保存先", "Folder", "저장 폴더"), Application.dataPath, "");
                    if (!string.IsNullOrEmpty(chosen))
                        try { folder = ExpressionMenuBuilder.AssetFolder(chosen); error = null; }
                        catch (Exception ex) { error = ex.Message; }
                }
            }
            EditorGUILayout.Space(5);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(L("表情", "Expressions", "표정") + "  " + entries.Count, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(entries.Count >= ExpressionMenuBuilder.MaxExpressions))
                    if (GUILayout.Button(L("クリップを追加", "Add clip", "클립 추가"), EditorStyles.toolbarButton))
                        entries.Add(new ExpressionMenuEntry());
                if (GUILayout.Button(L("選択中を追加", "Add selected", "선택 항목 추가"), EditorStyles.toolbarButton)) AddSelection();
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            int remove = -1, moveFrom = -1, moveTo = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label((i + 1).ToString(), GUILayout.Width(28));
                        var clip = (AnimationClip)EditorGUILayout.ObjectField(entry.Clip, typeof(AnimationClip), false);
                        if (clip != entry.Clip) { entry.Clip = clip; entry.Time = 0; if (clip != null) entry.Name = clip.name; }
                        using (new EditorGUI.DisabledScope(i == 0))
                            if (GUILayout.Button("▲", GUILayout.Width(25))) { moveFrom = i; moveTo = i - 1; }
                        using (new EditorGUI.DisabledScope(i == entries.Count - 1))
                            if (GUILayout.Button("▼", GUILayout.Width(25))) { moveFrom = i; moveTo = i + 1; }
                        if (GUILayout.Button(L("削除", "Remove", "삭제"), GUILayout.Width(65))) remove = i;
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(L("表示名", "Name", "표시 이름"), GUILayout.Width(65));
                        entry.Name = EditorGUILayout.TextField(entry.Name);
                        GUILayout.Label(L("アイコン", "Icon", "아이콘"), GUILayout.Width(55));
                        entry.Icon = (Texture2D)EditorGUILayout.ObjectField(entry.Icon, typeof(Texture2D), false, GUILayout.Width(125));
                    }
                    if (entry.Clip != null && entry.Clip.length > 1f / 30f)
                        entry.Time = EditorGUILayout.Slider(L("固定する時間", "Sample time", "고정 시점"), entry.Time, 0, entry.Clip.length);
                }
            }
            if (remove >= 0) entries.RemoveAt(remove);
            if (moveFrom >= 0)
            { var moved = entries[moveFrom]; entries.RemoveAt(moveFrom); entries.Insert(moveTo, moved); }
            EditorGUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            bool valid = avatar != null && entries.Count > 0 && entries.Count <= ExpressionMenuBuilder.MaxExpressions
                && entries.All(e => e.Clip != null) && !string.IsNullOrWhiteSpace(menuName) && !EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!valid))
                    if (GUILayout.Button(L("Prefabを生成", "Generate prefab", "프리팹 생성"), GUILayout.Height(28))) Generate();
                using (new EditorGUI.DisabledScope(generatedPrefab == null || avatar == null || EditorUtility.IsPersistent(avatar) || EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button(L("アバターに追加", "Add to avatar", "아바타에 추가"), GUILayout.Height(28))) Install();
            }
            if (generatedPrefab != null)
                EditorGUILayout.ObjectField(L("生成したPrefab", "Generated prefab", "생성한 프리팹"), generatedPrefab, typeof(GameObject), false);
            EditorGUILayout.Space(5);
        }
        private void Generate()
        {
            try
            {
                generatedPrefab = ExpressionMenuBuilder.Build(avatar, entries, menuName, folder,
                    L("解除", "Release", "해제"), L("次へ", "More", "다음")).Prefab;
                error = null;
                EditorGUIUtility.PingObject(generatedPrefab);
            }
            catch (Exception ex) { error = ex.Message; }
        }
        public GameObject Install()
        {
            if (avatar == null || generatedPrefab == null || EditorUtility.IsPersistent(avatar)
                || EditorApplication.isPlayingOrWillChangePlaymode) return null;
            // Keep one selector per avatar; multiple independent tracking owners cannot restore each other.
            if (avatar.GetComponentsInChildren<ModularAvatarMergeAnimator>(true)
                .Any(m => m.animator != null && m.animator.name.StartsWith(ExpressionMenuBuilder.ModulePrefix, StringComparison.Ordinal)))
            {
                error = L("既存のPPAE表情呼び出しPrefabを外してください。", "Remove the existing PPAE expression menu prefab first.", "기존 PPAE 표정 메뉴 프리팹을 제거하세요.");
                return null;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(generatedPrefab, avatar.transform);
            Undo.RegisterCreatedObjectUndo(instance, L("表情呼び出しを追加", "Add expression menu", "표정 메뉴 추가"));
            Selection.activeGameObject = instance;
            error = null;
            return instance;
        }
    }
}
