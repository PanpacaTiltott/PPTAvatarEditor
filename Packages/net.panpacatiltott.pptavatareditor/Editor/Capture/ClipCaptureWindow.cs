// Copyright (C) 2026 PanpacaTiltott. Distributed under the Zlib License.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace PPTAvatarEditor
{
    public sealed class ClipCaptureWindow : EditorWindow
    {
        [SerializeField] private VRCAvatarDescriptor avatar;
        [SerializeField] private List<AnimationClip> clips = new List<AnimationClip>();
        [SerializeField] private ClipCaptureSettings settings = new ClipCaptureSettings();
        [SerializeField] private int selected;
        [SerializeField] private string folder = "";
        private ClipCaptureSession session;
        private Texture2D image;
        private Vector2 listScroll;
        private bool dirty = true;
        private int drag;
        private string error, lastPath;
        public AnimationClip SelectedClip => selected >= 0 && selected < clips.Count ? clips[selected] : null;
        public VRCAvatarDescriptor TargetAvatarDescriptor => avatar;
        public IReadOnlyList<AnimationClip> Clips => clips;
        public Rect PreviewRect { get; private set; }
        public Rect ClipListRect { get; private set; }
        public Rect FirstClipRect { get; private set; }
        public Rect CaptureButtonsRect { get; private set; }
        public Rect SamplePositionRect { get; private set; }
        public int LastAddedCount { get; private set; }
        private static int Language { get { var code = EditorSetting.instance.Data.language ?? "JA"; return code.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 1 : code.StartsWith("ko", StringComparison.OrdinalIgnoreCase) ? 2 : 0; } }
        private static string L(string ja, string en, string ko) => Language == 1 ? en : Language == 2 ? ko : ja;
        internal static string CaptureLabel => L("撮影", "Capture", "촬영");

        [MenuItem("PPAE/Clip Capture", false, 13)]
        public static void Open()
        {
            var window = GetWindow<ClipCaptureWindow>();
            if (Selection.activeGameObject != null)
                window.SetAvatar(Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>());
            if (window.avatar == null)
            {
                var editor = Resources.FindObjectsOfTypeAll<PPTAvatarEditorView>().FirstOrDefault(w => w.TargetAvatarDescriptor != null);
                if (editor != null) window.SetAvatar(editor.TargetAvatarDescriptor);
            }
            window.AddClips(Selection.objects.OfType<AnimationClip>());
            window.Show();
        }

        public static void OpenFor(VRCAvatarDescriptor value, AnimationClip clip, float time)
        {
            var window = GetWindow<ClipCaptureWindow>();
            window.SetAvatar(value);
            if (clip != null) { window.AddClips(new[] { clip }); window.SelectClip(window.clips.IndexOf(clip)); }
            window.settings.SampleTime = Mathf.Max(0, time);
            window.dirty = true; window.Show(); window.Repaint();
        }

        public static void OpenClips(VRCAvatarDescriptor value, IEnumerable<AnimationClip> values)
        {
            var window = GetWindow<ClipCaptureWindow>();
            window.SetAvatar(value); window.AddClips(values); window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(800, 520);
            titleContent = new GUIContent(L("クリップ撮影", "Clip Capture", "클립 촬영"));
            AssemblyReloadEvents.beforeAssemblyReload += DisposePreview;
            EditorApplication.playModeStateChanged += PlayStateChanged;
            dirty = true;
        }

        private void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= DisposePreview;
            EditorApplication.playModeStateChanged -= PlayStateChanged;
            DisposePreview();
        }

        private void PlayStateChanged(PlayModeStateChange state)
        { DisposePreview(); Repaint(); }

        private void DisposePreview()
        {
            if (image != null) DestroyImmediate(image);
            image = null; session?.Dispose(); session = null; dirty = true;
        }

        public void SetAvatar(VRCAvatarDescriptor value)
        {
            if (avatar == value) return;
            DisposePreview();
            avatar = value; error = null; lastPath = null;
            folder = value != null ? ClipCaptureFiles.DefaultFolder(value.name) : "";
            settings.ResetCamera();
        }

        public void AddClips(IEnumerable<AnimationClip> values)
        {
            int previousCount = clips.Count;
            foreach (var clip in values)
                if (clip != null && !clips.Contains(clip)) clips.Add(clip);
            LastAddedCount = clips.Count - previousCount;
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, clips.Count - 1));
            if (LastAddedCount > 0)
            {
                selected = previousCount;
                listScroll.y = selected * 27;
                ShowNotification(new GUIContent(LastAddedCount + L(" 件追加", " clips added", "개 추가")));
            }
            DisposePreview(); Repaint();
        }

        public void SelectClip(int index)
        { selected = Mathf.Clamp(index, 0, Mathf.Max(0, clips.Count - 1)); DisposePreview(); Repaint(); }

        private void OnGUI()
        {
            titleContent.text = L("クリップ撮影", "Clip Capture", "클립 촬영");
            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUI.DisabledScope(playing))
            {
                var header = new Rect(6, 5, position.width - 12, 20);
                var nextAvatar = (VRCAvatarDescriptor)EditorGUI.ObjectField(header, L("アバター", "Avatar", "아바타"),
                    avatar, typeof(VRCAvatarDescriptor), true);
                if (nextAvatar != avatar) SetAvatar(nextAvatar);
                float rightWidth = Mathf.Clamp(position.width * .46f, 365, 540);
                float split = position.width - rightWidth - 12;
                GUILayout.BeginArea(new Rect(6, 32, split - 6, position.height - 38));
                DrawPreviewPanel(playing, split - 6, position.height - 38);
                GUILayout.EndArea();
                GUILayout.BeginArea(new Rect(split + 6, 32, rightWidth, position.height - 38));
                DrawClipPanel(rightWidth, position.height - 38);
                GUILayout.EndArea();
            }
        }

        private void DrawPreviewPanel(bool playing, float width, float height)
        {
            float availableHeight = Mathf.Max(80, height - 110);
            float aspect = (float)settings.Width / Mathf.Max(1, settings.Height);
            float imageWidth = Mathf.Min(width, availableHeight * aspect);
            float imageHeight = imageWidth / aspect;
            var region = GUILayoutUtility.GetRect(width, availableHeight);
            var rect = new Rect(region.x + (width - imageWidth) * .5f, region.y + (availableHeight - imageHeight) * .5f, imageWidth, imageHeight);
            PreviewRect = new Rect(rect.x + 6, rect.y + 32, rect.width, rect.height);
            DrawChecker(rect);
            HandleCamera(rect);
            if (Event.current.type == EventType.Repaint && dirty && avatar != null && !playing)
            {
                try
                {
                    if (session == null) session = new ClipCaptureSession(avatar, SelectedClip);
                    if (image != null) DestroyImmediate(image);
                    image = null;
                    float scale = Mathf.Min(1, 768f / Mathf.Max(settings.Width, settings.Height));
                    image = session.Capture(settings, Mathf.Max(1, Mathf.RoundToInt(settings.Width * scale)),
                        Mathf.Max(1, Mathf.RoundToInt(settings.Height * scale)));
                    error = null;
                }
                catch (Exception ex) { error = ex.Message; }
                dirty = false;
            }
            if (image != null) GUI.DrawTexture(rect, image, ScaleMode.StretchToFill, true);
            GUILayout.Label(SelectedClip != null ? SelectedClip.name : "", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(18));
            EditorGUI.BeginChangeCheck();
            settings.Zoom = EditorGUILayout.Slider(L("ズーム", "Zoom", "확대"), settings.Zoom, .1f, 5);
            float extent = session != null ? session.FaceRadius * 3 : 1;
            settings.VerticalOffset = EditorGUILayout.Slider(L("高さ", "Height", "높이"), settings.VerticalOffset, -extent, extent);
            using (new EditorGUILayout.HorizontalScope())
            {
                settings.FieldOfView = EditorGUILayout.Slider(L("画角", "Field of view", "화각"), settings.FieldOfView, 10, 80);
                if (GUILayout.Button(L("リセット", "Reset", "초기화"), GUILayout.Width(72))) { settings.ResetCamera(); dirty = true; }
            }
            if (EditorGUI.EndChangeCheck()) { dirty = true; Repaint(); }
        }

        private void DrawClipPanel(float width, float height)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(L("クリップ", "Clips", "클립") + "  " + clips.Count, GUILayout.Width(85));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(L("選択中を追加", "Add selected", "선택 항목 추가"), EditorStyles.toolbarButton))
                    AddClips(Selection.objects.OfType<AnimationClip>());
                using (new EditorGUI.DisabledScope(avatar == null))
                    if (GUILayout.Button(L("FXから追加", "Add from FX", "FX에서 추가"), EditorStyles.toolbarDropDown)) ShowFxMenu();
            }
            var add = (AnimationClip)EditorGUILayout.ObjectField(L("追加", "Add", "추가"), null, typeof(AnimationClip), false);
            if (add != null) AddClips(new[] { add });
            // Nested BeginArea inside the right-hand BeginArea can hide its children.
            // Keep this list in the same layout tree and reserve its height explicitly.
            float line = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            float footer = line * (settings.Transparent ? 4 : 5) + 34;
            if (!string.IsNullOrEmpty(lastPath)) footer += line;
            if (!string.IsNullOrEmpty(error)) footer += EditorStyles.helpBox.CalcHeight(new GUIContent(error), width) + 4;
            listScroll = EditorGUILayout.BeginScrollView(listScroll, GUI.skin.box,
                GUILayout.Height(Mathf.Max(100, height - 44 - footer)));
            int remove = -1;
            FirstClipRect = default;
            for (int i = 0; i < clips.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool pressed = GUILayout.Toggle(i == selected, clips[i] != null ? clips[i].name : "—", "Button",
                        GUILayout.Height(23));
                    if (i == 0 && Event.current.type == EventType.Repaint)
                    {
                        var row = GUILayoutUtility.GetLastRect();
                        FirstClipRect = new Rect(GUIUtility.GUIToScreenPoint(row.position) - position.position, row.size);
                    }
                    if (pressed && selected != i) SelectClip(i);
                    if (GUILayout.Button(L("削除", "Remove", "삭제"), GUILayout.Width(64), GUILayout.Height(23))) remove = i;
                }
            }
            EditorGUILayout.EndScrollView();
            var listRect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
                ClipListRect = new Rect(GUIUtility.GUIToScreenPoint(listRect.position) - position.position, listRect.size);
            if (remove >= 0)
            {
                clips.RemoveAt(remove);
                if (remove < selected) selected--;
                SelectClip(selected);
            }
            HandleDrop(listRect);
            EditorGUI.BeginChangeCheck();
            float sampleTime = DrawSamplePosition(L("撮影位置", "Capture position", "촬영 위치"), SelectedClip,
                settings.SampleTime, out Rect samplePositionRect);
            if (sampleTime != settings.SampleTime) { settings.SampleTime = sampleTime; dirty = true; Repaint(); }
            if (Event.current.type == EventType.Repaint)
                SamplePositionRect = new Rect(GUIUtility.GUIToScreenPoint(samplePositionRect.position) - position.position, samplePositionRect.size);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(L("PNGサイズ", "PNG size", "PNG 크기"));
                settings.Width = Mathf.Clamp(EditorGUILayout.DelayedIntField(settings.Width), 64, 4096);
                GUILayout.Label("×", GUILayout.Width(12));
                settings.Height = Mathf.Clamp(EditorGUILayout.DelayedIntField(settings.Height), 64, 4096);
            }
            settings.Transparent = EditorGUILayout.Toggle(L("背景を透過", "Transparent", "투명 배경"), settings.Transparent);
            if (!settings.Transparent)
                settings.Background = EditorGUILayout.ColorField(new GUIContent(L("背景色", "Background", "배경색")), settings.Background, true, false, false);
            if (EditorGUI.EndChangeCheck()) { dirty = true; Repaint(); }
            using (new EditorGUILayout.HorizontalScope())
            {
                folder = EditorGUILayout.TextField(L("保存先", "Folder", "저장 폴더"), folder);
                if (GUILayout.Button(L("選択", "Select", "선택"), GUILayout.Width(55)))
                {
                    string chosen = EditorUtility.OpenFolderPanel(L("保存先", "Folder", "저장 폴더"), Application.dataPath, "");
                    if (!string.IsNullOrEmpty(chosen))
                        try { folder = ClipCaptureFiles.AssetFolder(chosen); error = null; }
                        catch { error = L("Assets内のフォルダを選択してください。", "Choose a folder inside Assets.", "Assets 안의 폴더를 선택하세요."); }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(avatar == null || SelectedClip == null))
                    if (GUILayout.Button(L("選択クリップを撮影", "Capture selected", "선택 클립 촬영"), GUILayout.Height(28)))
                        Save(new[] { SelectedClip });
                using (new EditorGUI.DisabledScope(avatar == null || !clips.Any(c => c != null)))
                    if (GUILayout.Button(L("すべて撮影", "Capture all", "모두 촬영"), GUILayout.Height(28))) Save(clips);
            }
            if (Event.current.type == EventType.Repaint)
            {
                var rect = GUILayoutUtility.GetLastRect();
                CaptureButtonsRect = new Rect(GUIUtility.GUIToScreenPoint(rect.position) - position.position, rect.size);
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (!string.IsNullOrEmpty(lastPath) && GUILayout.Button(L("保存した画像を表示", "Show saved image", "저장한 이미지 표시")))
            { Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(lastPath); EditorGUIUtility.PingObject(Selection.activeObject); }
        }

        public static float DrawSamplePosition(string label, AnimationClip clip, float time, out Rect sliderRect)
        {
            Rect rect = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), new GUIContent(label));
            string start = L("始点", "Start", "시작"), end = L("終端", "End", "끝");
            float startWidth = EditorStyles.miniLabel.CalcSize(new GUIContent(start)).x + 6;
            float endWidth = EditorStyles.miniLabel.CalcSize(new GUIContent(end)).x + 6;
            sliderRect = new Rect(rect.x + startWidth, rect.y, Mathf.Max(1, rect.width - startWidth - endWidth), rect.height);
            float length = clip != null ? Mathf.Max(0, clip.length) : 0;
            using (new EditorGUI.DisabledScope(length <= 0))
            {
                EditorGUI.LabelField(new Rect(rect.x, rect.y, startWidth, rect.height), start, EditorStyles.miniLabel);
                EditorGUI.LabelField(new Rect(sliderRect.xMax + 6, rect.y, endWidth - 6, rect.height), end, EditorStyles.miniLabel);
                float clampedTime = Mathf.Clamp(time, 0, length);
                float progress = length > 0 ? clampedTime / length : 0;
                float next = GUI.HorizontalSlider(sliderRect, progress, 0, 1);
                return next == progress ? clampedTime : next * length;
            }
        }

        private void ShowFxMenu()
        {
            var catalog = new FxViewerCatalog(avatar);
            var menu = new GenericMenu();
            if (catalog.Entries.Any(e => e.Clip != null))
                menu.AddItem(new GUIContent(L("全レイヤー", "All layers", "모든 레이어")), false,
                    () => AddFromFx(-1));
            else menu.AddDisabledItem(new GUIContent(L("全レイヤー", "All layers", "모든 레이어")));
            for (int i = 0; i < catalog.Layers.Length; i++)
            {
                int layer = i;
                menu.AddItem(new GUIContent((i + 1) + "  " + catalog.Layers[i].Replace("/", "／")), false,
                    () => AddFromFx(layer));
            }
            menu.ShowAsContext();
        }

        public void AddFromFx(int layer)
        {
            if (avatar == null) return;
            var catalog = new FxViewerCatalog(avatar);
            AddClips(catalog.Entries.Where(e => layer < 0 || e.Layer == layer).Select(e => e.Clip));
        }

        private void HandleDrop(Rect rect)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition) || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)) return;
            var dropped = DragAndDrop.objectReferences.OfType<AnimationClip>().ToArray();
            DragAndDrop.visualMode = dropped.Length > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (e.type == EventType.DragPerform && dropped.Length > 0) { DragAndDrop.AcceptDrag(); AddClips(dropped); }
            e.Use();
        }

        private void HandleCamera(Rect rect)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Pan);
            if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            { settings.Zoom = Mathf.Clamp(settings.Zoom * Mathf.Exp(-e.delta.y * .06f), .1f, 5); dirty = true; e.Use(); Repaint(); }
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && e.button < 2)
            { drag = e.button + 1; GUIUtility.hotControl = id; e.Use(); }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id)
            {
                if (drag == 1) { settings.Yaw += e.delta.x * .4f; settings.Pitch = Mathf.Clamp(settings.Pitch + e.delta.y * .3f, -80, 80); }
                else
                {
                    float scale = (session != null ? session.FaceRadius : .2f) * 2 / Mathf.Max(1, rect.height) / settings.Zoom;
                    settings.HorizontalOffset -= e.delta.x * scale; settings.VerticalOffset += e.delta.y * scale;
                }
                dirty = true; e.Use(); Repaint();
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; drag = 0; e.Use(); }
        }

        private static void DrawChecker(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            for (float y = rect.y; y < rect.yMax; y += 16)
                for (float x = rect.x; x < rect.xMax; x += 16)
                {
                    float value = ((int)((x - rect.x) / 16) + (int)((y - rect.y) / 16)) % 2 == 0 ? .26f : .32f;
                    EditorGUI.DrawRect(new Rect(x, y, Mathf.Min(16, rect.xMax - x), Mathf.Min(16, rect.yMax - y)), new Color(value, value, value));
                }
        }

        private void Save(IEnumerable<AnimationClip> targets)
        {
            try
            {
                var result = ClipCaptureFiles.Save(avatar, targets, settings, folder,
                    (index, count, clip) => EditorUtility.DisplayCancelableProgressBar(L("クリップ撮影", "Clip Capture", "클립 촬영"),
                        clip.name, (float)index / count));
                error = result.Errors.Count > 0 ? string.Join("\n", result.Errors) : null;
                lastPath = result.Paths.LastOrDefault() ?? lastPath;
                ShowNotification(new GUIContent(result.Paths.Count + L(" 枚保存", " PNG saved", "장 저장")
                    + (result.Cancelled ? L("（中断）", " (cancelled)", " (중단)") : "")));
            }
            catch (Exception ex) { error = ex.Message; }
            finally { EditorUtility.ClearProgressBar(); Repaint(); }
        }
    }
}
