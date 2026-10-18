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
        [SerializeField] private string menuName = "表情";
        [SerializeField] private string folder = "";
        [SerializeField] private List<ExpressionMenuEntry> entries = new List<ExpressionMenuEntry>();
        [SerializeField] private List<ExpressionMenuFolder> folders = new List<ExpressionMenuFolder>();
        [SerializeField] private List<ExpressionMenuCategory> categories = new List<ExpressionMenuCategory>();
        [SerializeField] private List<string> removedSources = new List<string>();
        [SerializeField] private List<string> removedFolders = new List<string>();
        [SerializeField] private string selectedFolder = "";
        [SerializeField] private GameObject generatedPrefab;
        [SerializeField] private ClipCaptureSettings framing = new ClipCaptureSettings();
        [SerializeField] private int selected;
        [SerializeField] private int layoutVersion;
        private ClipCaptureSession session;
        private Texture2D preview;
        private Vector2 scroll;
        private bool dirty = true;
        private int drag;
        private string error, previewError;
        private Action pendingTreeAction;
        public Rect PreviewRect { get; private set; }
        public Rect FirstConfirmRect { get; private set; }
        public Rect FieldOfViewRect { get; private set; }
        public Rect SamplePositionRect { get; private set; }
        public Rect TreeViewportRect { get; private set; }
        public AnimationClip SelectedClip => selected >= 0 && selected < entries.Count ? entries[selected].Clip : null;
        public ExpressionMenuResult GeneratedResult { get; private set; }
        public IReadOnlyList<ExpressionMenuFolder> Folders => folders;
        public IReadOnlyList<ExpressionMenuCategory> Categories => categories;
        public IReadOnlyList<ExpressionMenuEntry> Entries => entries;
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
                var editor = Resources.FindObjectsOfTypeAll<PPTAvatarEditorView>().FirstOrDefault(w => w.TargetAvatarDescriptor != null);
                if (editor != null) window.SetAvatar(editor.TargetAvatarDescriptor);
            }
            window.AddSelection();
            window.Show();
        }
        private void OnEnable()
        {
            minSize = new Vector2(900, 560);
            titleContent = new GUIContent(L("表情呼び出し", "Expression Menu", "표정 메뉴"));
            if (layoutVersion == 0)
            {
                if (menuName == "PPAE" || menuName == "表情") menuName = L("表情", "Expressions", "표정");
                if (avatar != null && folder == DefaultFolder(avatar) + "/ExpressionMenus") folder = DefaultFolder(avatar);
                layoutVersion = 1;
            }
            ClipCaptureFiles.CapturesSaved += RefreshIcons;
            Undo.undoRedoPerformed += ChangedFolders;
            EnsureFolders();
            AssemblyReloadEvents.beforeAssemblyReload += DisposePreview;
            EditorApplication.playModeStateChanged += PlayStateChanged;
            EditorApplication.projectChanged += SourceAssetsChanged;
            if (layoutVersion < 2)
            {
                layoutVersion = 2;
                EditorApplication.delayCall += RefreshImportedLayout;
            }
            dirty = true;
        }
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= ChangedFolders;
            ClipCaptureFiles.CapturesSaved -= RefreshIcons;
            AssemblyReloadEvents.beforeAssemblyReload -= DisposePreview;
            EditorApplication.playModeStateChanged -= PlayStateChanged;
            EditorApplication.projectChanged -= SourceAssetsChanged;
            EditorApplication.delayCall -= RefreshImportedLayout;
            DisposePreview();
        }
        private void PlayStateChanged(PlayModeStateChange state) { DisposePreview(); Repaint(); }
        private void SourceAssetsChanged() { DisposePreview(); Repaint(); }
        private void RefreshImportedLayout() { if (this != null && avatar != null) ImportFx(); }
        private void DisposePreview()
        {
            if (preview != null) DestroyImmediate(preview);
            preview = null; session?.Dispose(); session = null; previewError = null; dirty = true;
        }
        private static string DefaultFolder(VRCAvatarDescriptor value)
        { return "Assets/PPAE/" + ExpressionMenuBuilder.SafeName(value.name); }
        public void SetAvatar(VRCAvatarDescriptor value)
        {
            if (value == avatar) return;
            DisposePreview();
            avatar = value; generatedPrefab = null; GeneratedResult = null; error = null;
            entries.Clear(); folders.Clear(); categories.Clear(); removedSources.Clear(); removedFolders.Clear(); selectedFolder = ""; selected = 0; scroll = Vector2.zero;
            folder = avatar != null ? DefaultFolder(avatar) : "";
            framing.ResetCamera();
            ImportFx();
        }
        private void AddSelection()
        {
            RecordChange();
            foreach (var clip in Selection.objects.OfType<AnimationClip>())
                if (entries.Count < ExpressionMenuBuilder.MaxExpressions && entries.All(e => e.Clip != clip))
                    entries.Add(new ExpressionMenuEntry { Clip = clip, Name = clip.name, FolderId = CurrentFolderId() });
            RefreshIcons(); DisposePreview();
        }
        public void ImportFx()
        {
            if (avatar == null) return;
            Undo.RecordObject(this, L("FXの表情を追加", "Add FX expressions", "FX 표정 추가"));
            EnsureCategories();
            var excluded = ClipCaptureFiles.ExcludedAutomaticSources(avatar);
            var emptied = new HashSet<string>(entries.Where(e => excluded.Contains(e.SourceKey)).Select(e => e.FolderId));
            entries.RemoveAll(e => excluded.Contains(e.SourceKey));
            folders.RemoveAll(f => emptied.Contains(f.Id) && !entries.Any(e => e.FolderId == f.Id));
            foreach (var group in ClipCaptureFiles.ExpressionGroups(avatar))
            {
                if (removedFolders.Contains(group.Key)) continue;
                ExpressionMenuFolder target = folders.FirstOrDefault(f => f.SourceKey == group.Key);
                foreach (var clip in group.Clips)
                {
                    if (entries.Count >= ExpressionMenuBuilder.MaxExpressions) break;
                    string key = ClipCaptureFiles.ClipSourceKey(group.Key, clip);
                    if (removedSources.Contains(key) || entries.Any(e => e.SourceKey == key)) continue;
                    var manual = entries.FirstOrDefault(e => e.Clip == clip && string.IsNullOrEmpty(e.SourceKey));
                    if (manual != null) { manual.SourceKey = key; continue; }
                    if (target == null)
                    {
                        target = new ExpressionMenuFolder { Name = group.Name, SourceKey = group.Key, CategoryId = categories[0].Id };
                        folders.Add(target);
                    }
                    entries.Add(new ExpressionMenuEntry { Clip = clip, Name = clip.name, FolderId = target.Id, SourceKey = key });
                }
            }
            EnsureFolders();
            RefreshIcons(); DisposePreview();
        }

        private void EnsureFolders()
        {
            if (entries.Count > 0 || folders.Count > 0) EnsureCategories();
            if (folders.Count == 0 && entries.Count > 0) folders.Add(new ExpressionMenuFolder { Name = L("表情", "Expressions", "표정"), CategoryId = categories[0].Id });
            foreach (var item in folders)
                if (!categories.Any(c => c.Id == item.CategoryId)) item.CategoryId = categories[0].Id;
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Id)) entry.Id = Guid.NewGuid().ToString("N");
                entry.UseClipMetadata = true;
                if (!folders.Any(f => f.Id == entry.FolderId)) entry.FolderId = folders[0].Id;
            }
            if (!folders.Any(f => f.Id == selectedFolder)) selectedFolder = "";
        }
        private void EnsureCategories()
        { if (categories.Count == 0) categories.Add(new ExpressionMenuCategory { Name = L("表情", "Expressions", "표정") }); }
        private void RecordChange() { Undo.RecordObject(this, L("表情メニューを編集", "Edit expression menu", "표정 메뉴 편집")); }
        public string AddCategory(string name)
        {
            RecordChange(); var item = new ExpressionMenuCategory { Name = name };
            categories.Add(item); Repaint(); return item.Id;
        }
        public void RenameCategory(string id, string name)
        { RecordChange(); categories.First(c => c.Id == id).Name = name; Repaint(); }
        public void MoveCategory(string id, int direction)
        {
            int index = categories.FindIndex(c => c.Id == id);
            if (index < 0) return;
            RecordChange(); var item = categories[index]; categories.RemoveAt(index);
            categories.Insert(Mathf.Clamp(index + direction, 0, categories.Count), item); Repaint();
        }
        public void MoveFolderToCategory(string id, string categoryId)
        {
            if (!categories.Any(c => c.Id == categoryId)) throw new ArgumentException("Unknown expression category.");
            RecordChange(); folders.First(f => f.Id == id).CategoryId = categoryId; Repaint();
        }
        public void RemoveCategory(string id)
        {
            RecordChange();
            foreach (var item in folders.Where(f => f.CategoryId == id).ToArray()) DeleteFolderContents(item);
            categories.RemoveAll(c => c.Id == id); SelectEntry(selected);
        }
        private string CurrentFolderId()
        {
            EnsureFolders();
            if (folders.Count == 0) AddFolder(L("表情", "Expressions", "표정"));
            return string.IsNullOrEmpty(selectedFolder) ? folders[0].Id : selectedFolder;
        }
        private void ChangedFolders() { EnsureFolders(); DisposePreview(); Repaint(); }
        public string AddFolder(string name)
        {
            Undo.RecordObject(this, L("フォルダを追加", "Add folder", "폴더 추가"));
            EnsureCategories();
            var item = new ExpressionMenuFolder { Name = name, CategoryId = folders.FirstOrDefault(f => f.Id == selectedFolder)?.CategoryId ?? categories[0].Id };
            folders.Add(item); selectedFolder = item.Id; Repaint(); return item.Id;
        }
        public void RenameFolder(string id, string name)
        {
            var item = folders.First(f => f.Id == id);
            Undo.RecordObject(this, L("フォルダ名を変更", "Rename folder", "폴더 이름 변경"));
            item.Name = name; Repaint();
        }
        public void MoveFolder(string id, int direction)
        {
            int index = folders.FindIndex(f => f.Id == id);
            if (index < 0) return;
            int destination = Mathf.Clamp(index + direction, 0, folders.Count - 1);
            Undo.RecordObject(this, L("フォルダを並べ替え", "Reorder folders", "폴더 순서 변경"));
            var item = folders[index]; folders.RemoveAt(index); folders.Insert(destination, item); Repaint();
        }
        public void MoveEntryToFolder(int index, string id)
        {
            if (!folders.Any(f => f.Id == id)) throw new ArgumentException("Unknown expression folder.");
            Undo.RecordObject(this, L("表情を移動", "Move expression", "표정 이동"));
            entries[index].FolderId = id; Repaint();
        }
        public void RemoveFolder(string id)
        {
            Undo.RecordObject(this, L("フォルダを削除", "Remove folder", "폴더 삭제"));
            var item = folders.FirstOrDefault(f => f.Id == id);
            if (item == null) return;
            DeleteFolderContents(item); SelectEntry(selected);
        }
        private void DeleteFolderContents(ExpressionMenuFolder item)
        {
            if (!string.IsNullOrEmpty(item.SourceKey) && !removedFolders.Contains(item.SourceKey)) removedFolders.Add(item.SourceKey);
            foreach (var entry in entries.Where(e => e.FolderId == item.Id))
                if (!string.IsNullOrEmpty(entry.SourceKey) && !removedSources.Contains(entry.SourceKey)) removedSources.Add(entry.SourceKey);
            entries.RemoveAll(e => e.FolderId == item.Id); folders.Remove(item);
            if (selectedFolder == item.Id) selectedFolder = "";
        }
        public void SelectEntry(int index)
        { selected = Mathf.Clamp(index, 0, Mathf.Max(0, entries.Count - 1)); DisposePreview(); Repaint(); }
        public void RemoveEntry(int index)
        {
            RecordChange();
            if (!string.IsNullOrEmpty(entries[index].SourceKey)) removedSources.Add(entries[index].SourceKey);
            entries.RemoveAt(index); SelectEntry(index < selected ? selected - 1 : selected);
        }
        public void RefreshIcons()
        {
            if (avatar == null) return;
            var icons = ClipCaptureFiles.FindIcons(avatar, entries.Select(e => e.Clip));
            foreach (var entry in entries)
            {
                entry.UseClipMetadata = true;
                entry.Name = entry.Clip != null ? entry.Clip.name : "";
                if (entry.Clip != null && icons.TryGetValue(entry.Clip, out var icon))
                {
                    if (entry.AutoIcon != icon) entry.Time = ClipCaptureFiles.CapturedTime(icon);
                    entry.Icon = entry.AutoIcon = icon;
                }
                else entry.Icon = entry.AutoIcon = null;
            }
            dirty = true; Repaint();
        }
        private void OnGUI()
        {
            titleContent.text = L("表情呼び出し", "Expression Menu", "표정 메뉴");
            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUI.DisabledScope(playing))
            {
                GUILayout.BeginArea(new Rect(6, 5, position.width - 12, 70));
                var nextAvatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(L("アバター", "Avatar", "아바타"), avatar, typeof(VRCAvatarDescriptor), true);
                if (nextAvatar != avatar) SetAvatar(nextAvatar);
                menuName = EditorGUILayout.TextField(L("セット名", "Set name", "세트 이름"), menuName);
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
                GUILayout.EndArea();
                float left = Mathf.Clamp(position.width * .36f, 300, 520);
                float bottom = !string.IsNullOrEmpty(error) || !string.IsNullOrEmpty(previewError) ? 108 : generatedPrefab != null ? 68 : 44;
                var previewArea = new Rect(6, 80, left, position.height - 86 - bottom);
                GUILayout.BeginArea(previewArea);
                DrawPreview(previewArea, playing);
                GUILayout.EndArea();
                GUILayout.BeginArea(new Rect(left + 14, 80, position.width - left - 20, position.height - 86 - bottom));
                DrawEntries();
                GUILayout.EndArea();
                GUILayout.BeginArea(new Rect(6, position.height - bottom, position.width - 12, bottom - 4));
                if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
                else if (!string.IsNullOrEmpty(previewError)) EditorGUILayout.HelpBox(previewError, MessageType.Error);
                bool valid = avatar != null && !EditorUtility.IsPersistent(avatar)
                    && entries.Count > 0 && entries.Count <= ExpressionMenuBuilder.MaxExpressions
                    && entries.All(e => e.Clip != null) && !string.IsNullOrWhiteSpace(menuName)
                    && folders.All(f => !string.IsNullOrWhiteSpace(f.Name)) && categories.All(c => !string.IsNullOrWhiteSpace(c.Name));
                using (new EditorGUI.DisabledScope(!valid))
                    if (GUILayout.Button(L("生成してアバターに追加", "Generate and add to avatar", "생성 후 아바타에 추가"), GUILayout.Height(30))) Generate();
                if (generatedPrefab != null)
                    EditorGUILayout.ObjectField(L("Prefab", "Prefab", "프리팹"), generatedPrefab, typeof(GameObject), false);
                GUILayout.EndArea();
            }
        }

        private enum TreeKind { Category, Folder, Clip }
        private sealed class TreeDrag { public ExpressionMenuWindow Owner; public TreeKind Kind; public string Id; }
        private sealed class TreeRow
        {
            public TreeKind Kind;
            public string Id;
            public Rect Rect, Handle;
        }
        private TreeDrag pendingDrag;
        private Vector2 dragOrigin;
        private bool treeDragging;
        private int treeDragControl;
        private Rect dropIndicator;
        private readonly List<TreeRow> treeRows = new List<TreeRow>();

        private static Rect WindowRect(Rect rect)
        { return new Rect(GUIUtility.GUIToScreenPoint(rect.position), rect.size); }
        private Rect LocalWindowRect(Rect rect)
        { var value = WindowRect(rect); value.position -= position.position; return value; }

        private void DrawEntries()
        {
            EnsureFolders();
            treeRows.Clear();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(L("表情呼び出し", "Expression Menu", "표정 메뉴"), EditorStyles.boldLabel);
                GUILayout.Label(entries.Count.ToString(), EditorStyles.miniLabel, GUILayout.Width(28));
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(avatar == null))
                    if (GUILayout.Button(L("FXから追加", "Add from FX", "FX에서 추가"), EditorStyles.toolbarButton)) ImportFx();
                if (GUILayout.Button(L("カテゴリを追加", "Add category", "카테고리 추가"), EditorStyles.toolbarButton))
                    pendingTreeAction = () => AddCategory(L("新しいカテゴリ", "New category", "새 카테고리"));
            }
            FirstConfirmRect = default;
            var e = Event.current;
            if (e.rawType == EventType.MouseDrag && treeDragging)
            {
                Vector2 pointer = GUIUtility.GUIToScreenPoint(e.mousePosition) - position.position;
                if (TreeViewportRect.Contains(pointer))
                {
                    if (pointer.y < TreeViewportRect.yMin + 20) scroll.y = Mathf.Max(0, scroll.y - 14);
                    else if (pointer.y > TreeViewportRect.yMax - 20) scroll.y += 14;
                    Repaint();
                }
            }
            scroll = EditorGUILayout.BeginScrollView(scroll, GUI.skin.box);
            foreach (var category in categories.ToArray())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (var header = new EditorGUILayout.HorizontalScope())
                    {
                        Rect handle = DrawHandle(TreeKind.Category, category.Id, category.Name);
                        category.Expanded = TreeFoldout(category.Expanded, L("メニュー名", "Menu name", "메뉴 이름"), 90);
                        string name = EditorGUILayout.TextField(category.Name);
                        if (name != category.Name) RenameCategory(category.Id, name);
                        if (GUILayout.Button(new GUIContent("+", L("FXフォルダを追加", "Add FX folder", "FX 폴더 추가")), GUILayout.Width(24)))
                            pendingTreeAction = () => { string id = AddFolder(L("新しいフォルダ", "New folder", "새 폴더")); MoveFolderToCategory(id, category.Id); category.Expanded = true; };
                        if (DeleteButton(L("カテゴリと中の項目を削除", "Remove category and its contents", "카테고리와 내용 삭제")))
                            pendingTreeAction = () => RemoveCategory(category.Id);
                        RegisterRow(TreeKind.Category, category.Id, header.rect, handle);
                        HandleTreeDrop(header.rect, TreeKind.Category, category.Id);
                    }
                    if (category.Expanded)
                        foreach (var item in folders.Where(f => f.CategoryId == category.Id).ToArray()) DrawFolder(item);
                }
            }
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) TreeViewportRect = LocalWindowRect(GUILayoutUtility.GetLastRect());
            HandleInternalTreeDrag();
            if (Event.current.type == EventType.Repaint && dropIndicator.width > 0 && TreeViewportRect.Overlaps(dropIndicator))
            {
                Rect local = dropIndicator;
                local.position = GUIUtility.ScreenToGUIPoint(dropIndicator.position + position.position);
                EditorGUI.DrawRect(local, new Color(.3f, .65f, 1));
            }
            if (Event.current.rawType == EventType.DragExited) { dropIndicator = default; Repaint(); }
            if (pendingTreeAction != null)
            {
                var action = pendingTreeAction; pendingTreeAction = null; action(); Repaint();
            }
        }

        private static bool TreeFoldout(bool expanded, string label, float width)
        { return EditorGUI.Foldout(GUILayoutUtility.GetRect(width, EditorGUIUtility.singleLineHeight, GUILayout.Width(width)), expanded, label, true); }
        private static bool DeleteButton(string tooltip)
        { return GUILayout.Button(new GUIContent(L("削除", "Delete", "삭제"), tooltip), GUILayout.Width(48)); }

        private void DrawFolder(ExpressionMenuFolder item)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var visible = entries.Where(e => e.FolderId == item.Id).ToArray();
                    using (var header = new EditorGUILayout.HorizontalScope())
                    {
                        Rect handle = DrawHandle(TreeKind.Folder, item.Id, item.Name);
                        item.Expanded = TreeFoldout(item.Expanded, L("メニュー名", "Menu name", "메뉴 이름"), 90);
                        string name = EditorGUILayout.TextField(item.Name);
                        if (name != item.Name) RenameFolder(item.Id, name);
                        GUILayout.Label(visible.Length.ToString(), EditorStyles.miniLabel, GUILayout.Width(22));
                        using (new EditorGUI.DisabledScope(entries.Count >= ExpressionMenuBuilder.MaxExpressions))
                            if (GUILayout.Button(new GUIContent("+", L("クリップを追加", "Add clip", "클립 추가")), GUILayout.Width(24)))
                                pendingTreeAction = () => { RecordChange(); entries.Add(new ExpressionMenuEntry { FolderId = item.Id, UseClipMetadata = true }); selectedFolder = item.Id; item.Expanded = true; };
                        if (DeleteButton(L("FXフォルダと中のクリップを削除", "Remove FX folder and its clips", "FX 폴더와 클립 삭제")))
                            pendingTreeAction = () => RemoveFolder(item.Id);
                        RegisterRow(TreeKind.Folder, item.Id, header.rect, handle);
                        HandleTreeDrop(header.rect, TreeKind.Folder, item.Id);
                    }
                    if (item.Expanded)
                        foreach (var entry in visible) DrawEntry(entry);
                }
            }
        }

        private void DrawEntry(ExpressionMenuEntry entry)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12);
                using (new EditorGUILayout.VerticalScope())
                {
                    using (var row = new EditorGUILayout.HorizontalScope())
                    {
                        Rect handle = DrawHandle(TreeKind.Clip, entry.Id, entry.Clip != null ? entry.Clip.name : "Clip");
                        var clip = (AnimationClip)EditorGUILayout.ObjectField(entry.Clip, typeof(AnimationClip), false);
                        if (clip != entry.Clip) ChangeClip(entry, clip);
                        var old = GUI.backgroundColor;
                        if (selected == entries.IndexOf(entry)) GUI.backgroundColor = new Color(.5f, .75f, 1);
                        if (GUILayout.Button(L("確認", "Preview", "확인"), GUILayout.Width(56))) SelectEntry(entries.IndexOf(entry));
                        GUI.backgroundColor = old;
                        if (FirstConfirmRect.width == 0 && Event.current.type == EventType.Repaint)
                            FirstConfirmRect = LocalWindowRect(GUILayoutUtility.GetLastRect());
                        if (DeleteButton(L("クリップを一覧から削除", "Remove clip from list", "목록에서 클립 삭제")))
                            pendingTreeAction = () => RemoveEntry(entries.IndexOf(entry));
                        RegisterRow(TreeKind.Clip, entry.Id, row.rect, handle);
                        HandleTreeDrop(row.rect, TreeKind.Clip, entry.Id);
                    }
                    if (entry.Clip != null && selected == entries.IndexOf(entry))
                    {
                        float oldWidth = EditorGUIUtility.labelWidth;
                        EditorGUIUtility.labelWidth = Language == 1 ? 104 : 68;
                        float time = ClipCaptureWindow.DrawSamplePosition(L("固定位置", "Hold position", "고정 위치"),
                            entry.Clip, entry.Time, out Rect samplePositionRect);
                        if (time != entry.Time) { RecordChange(); entry.Time = time; dirty = true; Repaint(); }
                        if (Event.current.type == EventType.Repaint) SamplePositionRect = LocalWindowRect(samplePositionRect);
                        EditorGUIUtility.labelWidth = oldWidth;
                    }
                }
            }
        }

        private void ChangeClip(ExpressionMenuEntry entry, AnimationClip clip)
        {
            RecordChange();
            if (!string.IsNullOrEmpty(entry.SourceKey)) removedSources.Add(entry.SourceKey);
            entry.SourceKey = ""; entry.Icon = entry.AutoIcon = null; entry.Clip = clip; entry.Time = 0;
            entry.Name = clip != null ? clip.name : ""; entry.UseClipMetadata = true;
            RefreshIcons(); DisposePreview();
        }

        private Rect DrawHandle(TreeKind kind, string id, string name)
        {
            Rect rect = GUILayoutUtility.GetRect(14, 20, GUILayout.Width(14), GUILayout.Height(20));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Pan);
            if (Event.current.type == EventType.Repaint)
                for (int i = 0; i < 3; i++) EditorGUI.DrawRect(new Rect(rect.x + 3, rect.center.y - 3 + i * 3, 8, 1), new Color(.48f, .48f, .48f));
            int control = GUIUtility.GetControlID(id.GetHashCode(), FocusType.Passive, rect);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                pendingDrag = new TreeDrag { Owner = this, Kind = kind, Id = id };
                dragOrigin = GUIUtility.GUIToScreenPoint(e.mousePosition);
                treeDragging = false; treeDragControl = control;
                GUIUtility.hotControl = control; e.Use();
            }
            return rect;
        }

        private void HandleInternalTreeDrag()
        {
            if (pendingDrag == null) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            { EndTreeDrag(); e.Use(); return; }
            if (e.type != EventType.MouseDrag && e.rawType != EventType.MouseUp) return;
            Vector2 screen = GUIUtility.GUIToScreenPoint(e.mousePosition);
            if (e.type == EventType.MouseDrag && Vector2.Distance(dragOrigin, screen) > 4) treeDragging = true;
            Vector2 pointer = screen - position.position;
            var source = pendingDrag;
            var target = TreeViewportRect.Contains(pointer) ? treeRows.FirstOrDefault(row => row.Rect.Contains(pointer)) : null;
            bool allowed = treeDragging && target != null && target.Id != source.Id && (source.Kind == target.Kind
                || source.Kind == TreeKind.Folder && target.Kind == TreeKind.Category
                || source.Kind == TreeKind.Clip && target.Kind == TreeKind.Folder);
            bool after = allowed && pointer.y >= target.Rect.center.y;
            dropIndicator = default;
            if (allowed)
                dropIndicator = new Rect(target.Rect.x, source.Kind == target.Kind && !after ? target.Rect.y : target.Rect.yMax,
                    target.Rect.width, 2);
            if (e.rawType == EventType.MouseUp)
            {
                if (allowed) pendingTreeAction = () => DropTreeItem(source.Kind.ToString(), source.Id, target.Kind.ToString(), target.Id, after);
                EndTreeDrag();
            }
            e.Use(); Repaint();
        }

        private void EndTreeDrag()
        {
            if (GUIUtility.hotControl == treeDragControl) GUIUtility.hotControl = 0;
            pendingDrag = null; treeDragging = false; treeDragControl = 0; dropIndicator = default;
        }
        private void RegisterRow(TreeKind kind, string id, Rect rect, Rect handle)
        { treeRows.Add(new TreeRow { Kind = kind, Id = id, Rect = LocalWindowRect(rect), Handle = LocalWindowRect(handle) }); }

        private void HandleTreeDrop(Rect rect, TreeKind targetKind, string targetId)
        {
            var e = Event.current;
            if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !rect.Contains(e.mousePosition)) return;
            var external = DragAndDrop.objectReferences.OfType<AnimationClip>().ToArray();
            bool allowed = external.Length > 0 && (targetKind == TreeKind.Folder || targetKind == TreeKind.Clip);
            DragAndDrop.visualMode = allowed ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            bool after = e.mousePosition.y >= rect.center.y;
            if (allowed)
            {
                var cue = rect;
                cue.y = targetKind == TreeKind.Clip && !after ? rect.y : rect.yMax;
                cue.height = 2; dropIndicator = LocalWindowRect(cue);
                if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    pendingTreeAction = () => DropExternalClips(external, targetKind, targetId, after);
                    dropIndicator = default;
                }
            }
            else dropIndicator = default;
            Repaint(); e.Use();
        }

        public void DropTreeItem(string kind, string id, string targetKind, string targetId, bool after)
        {
            var sourceType = (TreeKind)Enum.Parse(typeof(TreeKind), kind);
            var targetType = (TreeKind)Enum.Parse(typeof(TreeKind), targetKind);
            if (id == targetId) return;
            var current = selected >= 0 && selected < entries.Count ? entries[selected] : null;
            RecordChange();
            if (sourceType == TreeKind.Category && targetType == TreeKind.Category)
            {
                var source = categories.First(c => c.Id == id); var target = categories.First(c => c.Id == targetId);
                categories.Remove(source); categories.Insert(categories.IndexOf(target) + (after ? 1 : 0), source);
            }
            else if (sourceType == TreeKind.Folder && (targetType == TreeKind.Folder || targetType == TreeKind.Category))
            {
                var source = folders.First(f => f.Id == id);
                var target = targetType == TreeKind.Folder ? folders.First(f => f.Id == targetId) : null;
                source.CategoryId = target != null ? target.CategoryId : targetId;
                var category = categories.First(c => c.Id == source.CategoryId); category.Expanded = true;
                folders.Remove(source);
                int destination = target != null ? folders.IndexOf(target) + (after ? 1 : 0) : folders.FindLastIndex(f => f.CategoryId == source.CategoryId) + 1;
                folders.Insert(destination, source);
            }
            else if (sourceType == TreeKind.Clip && (targetType == TreeKind.Clip || targetType == TreeKind.Folder))
            {
                var source = entries.First(item => item.Id == id);
                var target = targetType == TreeKind.Clip ? entries.First(item => item.Id == targetId) : null;
                source.FolderId = target != null ? target.FolderId : targetId;
                folders.First(f => f.Id == source.FolderId).Expanded = true;
                entries.Remove(source);
                int destination = target != null ? entries.IndexOf(target) + (after ? 1 : 0) : entries.FindLastIndex(item => item.FolderId == source.FolderId) + 1;
                entries.Insert(destination, source);
            }
            selected = current != null ? entries.IndexOf(current) : 0; Repaint();
        }

        private void DropExternalClips(AnimationClip[] clips, TreeKind kind, string targetId, bool after)
        {
            var target = kind == TreeKind.Clip ? entries.First(e => e.Id == targetId) : null;
            string folderId = target != null ? target.FolderId : targetId;
            RecordChange();
            int destination = target != null ? entries.IndexOf(target) + (after ? 1 : 0) : entries.FindLastIndex(e => e.FolderId == folderId) + 1;
            foreach (var clip in clips)
            {
                if (entries.Count >= ExpressionMenuBuilder.MaxExpressions) break;
                entries.Insert(destination++, new ExpressionMenuEntry { Clip = clip, Name = clip.name, FolderId = folderId, UseClipMetadata = true });
            }
            folders.First(f => f.Id == folderId).Expanded = true;
            RefreshIcons(); Repaint();
        }

        private void DrawPreview(Rect area, bool playing)
        {
            float side = Mathf.Max(100, Mathf.Min(area.width, area.height - 120));
            var region = GUILayoutUtility.GetRect(area.width, Mathf.Max(side, area.height - 120));
            var rect = new Rect((area.width - side) * .5f, region.y + (region.height - side) * .5f, side, side);
            PreviewRect = new Rect(rect.position + area.position, rect.size);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, new Color(.18f, .18f, .18f));
            HandleCamera(rect);
            if (dirty && Event.current.type == EventType.Repaint && avatar != null && !playing)
            {
                try
                {
                    if (session == null) session = new ClipCaptureSession(avatar, SelectedClip);
                    framing.SampleTime = selected >= 0 && selected < entries.Count ? entries[selected].Time : 0;
                    if (preview != null) DestroyImmediate(preview);
                    preview = null;
                    preview = session.Capture(framing, 512, 512);
                    previewError = null;
                }
                catch (Exception ex) { previewError = ex.Message; }
                dirty = false;
            }
            if (preview != null) GUI.DrawTexture(rect, preview, ScaleMode.StretchToFill, true);
            GUILayout.Label(SelectedClip != null ? SelectedClip.name : "", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(18));
            EditorGUI.BeginChangeCheck();
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Language == 1 ? 90 : 68;
            framing.Zoom = EditorGUILayout.Slider(L("ズーム", "Zoom", "확대"), framing.Zoom, .1f, 5);
            float extent = session != null ? session.FaceRadius * 3 : 1;
            framing.VerticalOffset = EditorGUILayout.Slider(L("高さ", "Height", "높이"), framing.VerticalOffset, -extent, extent);
            framing.FieldOfView = EditorGUILayout.Slider(L("画角", "Field of view", "화각"), framing.FieldOfView, 10, 80);
            var fieldOfViewRect = GUILayoutUtility.GetLastRect();
            FieldOfViewRect = new Rect(fieldOfViewRect.position + area.position, fieldOfViewRect.size);
            using (new EditorGUILayout.HorizontalScope())
            {
                framing.Transparent = EditorGUILayout.ToggleLeft(L("背景を透過", "Transparent", "투명 배경"), framing.Transparent, GUILayout.Width(110));
                using (new EditorGUI.DisabledScope(framing.Transparent))
                    framing.Background = EditorGUILayout.ColorField(GUIContent.none, framing.Background, false, false, false);
                if (GUILayout.Button(L("リセット", "Reset", "초기화"), GUILayout.Width(64))) { framing.ResetCamera(); dirty = true; }
            }
            EditorGUIUtility.labelWidth = oldLabelWidth;
            if (EditorGUI.EndChangeCheck()) { dirty = true; Repaint(); }
        }
        private void HandleCamera(Rect rect)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            { framing.Zoom = Mathf.Clamp(framing.Zoom * Mathf.Exp(-e.delta.y * .06f), .1f, 5); dirty = true; e.Use(); Repaint(); }
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && e.button <= 2)
            { drag = e.button + 1; GUIUtility.hotControl = id; e.Use(); }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id)
            {
                if (drag == 1) { framing.Yaw += e.delta.x * .4f; framing.Pitch = Mathf.Clamp(framing.Pitch + e.delta.y * .4f, -85, 85); }
                else
                {
                    float scale = (session != null ? session.FaceRadius : .2f) * 2 / Mathf.Max(100, rect.height) / framing.Zoom;
                    framing.HorizontalOffset -= e.delta.x * scale; framing.VerticalOffset += e.delta.y * scale;
                }
                dirty = true; e.Use(); Repaint();
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            { GUIUtility.hotControl = 0; drag = 0; e.Use(); }
        }

        private void Generate()
        {
            try { CreateAndInstall(true); error = null; EditorGUIUtility.PingObject(generatedPrefab); }
            catch (OperationCanceledException) { error = null; }
            catch (Exception ex) { error = ex.Message; }
            finally { EditorUtility.ClearProgressBar(); Repaint(); }
        }
        public ExpressionMenuResult CreateAndInstall(bool showProgress = false)
        {
            ValidateInstallTarget();
            EnsureFolders();
            RefreshIcons();
            var result = ExpressionMenuBuilder.BuildWithHierarchy(avatar, entries, menuName, folder,
                L("解除", "Release", "해제"), L("次へ", "More", "다음"), framing,
                showProgress ? (Func<int, int, string, bool>)((index, count, name) =>
                    EditorUtility.DisplayCancelableProgressBar(L("アイコン撮影", "Capture icons", "아이콘 촬영"), name, (float)index / count)) : null, folders, categories);
            generatedPrefab = result.Prefab; GeneratedResult = result;
            Install();
            return result;
        }
        private GameObject ExistingModule()
        {
            var modules = avatar.GetComponentsInChildren<ModularAvatarMergeAnimator>(true)
                .Where(m => m.animator != null && m.animator.name.StartsWith(ExpressionMenuBuilder.ModulePrefix, StringComparison.Ordinal)).ToArray();
            if (modules.Length > 1)
                throw new InvalidOperationException(L("既存のPPAE表情Prefabを1つにしてください。", "Keep one PPAE expression prefab.", "기존 PPAE 표정 프리팹을 하나만 남겨 주세요."));
            return modules.Length == 1 ? modules[0].gameObject : null;
        }
        private GameObject ValidateInstallTarget()
        {
            if (avatar == null || EditorUtility.IsPersistent(avatar) || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(L("シーン内のアバターを選択してください。", "Select an avatar in the scene.", "씬 안의 아바타를 선택하세요."));
            var existing = ExistingModule();
            if (existing == null) return null;
            if (existing.transform.parent != avatar.transform || existing.transform.childCount != 0
                || existing.GetComponents<Component>().Any(c => !(c is Transform) && !(c is ModularAvatarMergeAnimator)
                    && !(c is ModularAvatarParameters) && !(c is ModularAvatarMenuInstaller)))
                throw new InvalidOperationException(L("追加編集されたPPAE表情Prefabをアバターから外してください。",
                    "Remove the customized PPAE expression prefab from the avatar.", "추가 편집된 PPAE 표정 프리팹을 아바타에서 제거하세요."));
            return existing;
        }
        public GameObject Install()
        {
            var previous = ValidateInstallTarget();
            if (generatedPrefab == null) return null;
            if (previous != null && PrefabUtility.GetCorrespondingObjectFromSource(previous) == generatedPrefab) return previous;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            string label = L("表情呼び出しを追加", "Add expression menu", "표정 메뉴 추가");
            Undo.SetCurrentGroupName(label);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(generatedPrefab, avatar.transform);
            if (instance == null) throw new InvalidOperationException(L("Prefabを追加できませんでした。", "Could not add the prefab.", "프리팹을 추가하지 못했습니다."));
            Undo.RegisterCreatedObjectUndo(instance, label);
            if (previous != null)
            {
                instance.transform.localPosition = previous.transform.localPosition;
                instance.transform.localRotation = previous.transform.localRotation;
                instance.transform.localScale = previous.transform.localScale;
                instance.transform.SetSiblingIndex(previous.transform.GetSiblingIndex());
                Undo.DestroyObjectImmediate(previous);
            }
            Undo.CollapseUndoOperations(group);
            error = null;
            return instance;
        }
    }
}
