// Copyright (C) 2026 PanpacaTiltott. Distributed under the Zlib License.
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace PPTAvatarEditor
{
    public sealed class FxViewerWindow : EditorWindow
    {
        [SerializeField] private VRCAvatarDescriptor avatar;
        [SerializeField] private float previewFraction = .5f;
        private AvatarMonitorView preview;
        private FxViewerCatalog catalog;
        private FxViewerCatalog.Entry selectedEntry;
        private FxClipPreview playback;
        private Vector2 scroll;
        private int layer, dragMode;
        private bool playing;
        private double lastTick;
        public GameObject PreviewRoot => preview?.avatarMonitorField?.avatar?.Descriptor?.gameObject;
        public AnimationClip SelectedClip => playback?.Clip;
        public float PreviewTime => playback?.Time ?? 0;
        public Rect PreviewPanelRect { get; private set; }
        public Rect FxPanelRect { get; private set; }
        public Rect FirstConfirmRect { get; private set; }
        private static string T(string key)
        {
            var text = LocalizeText.instance.langPair;
            switch (key)
            {
                case "avatar": return text.avatarLabel;
                case "reload": return text.reloadAvatarButtonText;
                case "preview": return text.previewTitle;
                case "fx": return text.fxLayerTitle;
                case "layer": return text.layerLabel;
                case "allLayers": return text.allFxLayers;
                case "confirmPreview": return text.confirmPreview;
                case "playPreview": return text.playPreview;
                case "pausePreview": return text.pausePreview;
                default: return text.avatarPrompt;
            }
        }

        [MenuItem("PPAE/FX Viewer", false, 11)]
        public static void Open()
        {
            var window = GetWindow<FxViewerWindow>();
            window.Show();
            if (window.avatar == null && Selection.activeGameObject != null)
                window.SelectAvatar(Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>());
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("FX Viewer");
            LocalizeText.instance.LoadLanguage(EditorSetting.instance.Data.language);
            minSize = new Vector2(780, 440);
            AssemblyReloadEvents.beforeAssemblyReload += DisposePreview;
            EditorApplication.playModeStateChanged += PlayStateChanged;
            EditorApplication.update += Tick;
            EditorApplication.delayCall += RestoreAvatar;
            Undo.undoRedoPerformed += RefreshCatalog;
        }

        private void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= DisposePreview;
            EditorApplication.playModeStateChanged -= PlayStateChanged;
            EditorApplication.update -= Tick;
            EditorApplication.delayCall -= RestoreAvatar;
            Undo.undoRedoPerformed -= RefreshCatalog;
            DisposePreview();
        }

        private void RestoreAvatar()
        {
            if (this != null && PreviewRoot == null && avatar != null && !EditorApplication.isPlayingOrWillChangePlaymode)
                SelectAvatar(avatar);
        }

        private void PlayStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) DisposePreview();
            if (state == PlayModeStateChange.EnteredEditMode) RestoreAvatar();
            Repaint();
        }

        private void DisposePreview()
        {
            playing = false;
            playback?.Dispose(); playback = null;
            if (preview != null) { preview.Dispose(); DestroyImmediate(preview); }
            preview = null;
            catalog = null;
            selectedEntry = null;
        }

        public void SelectAvatar(VRCAvatarDescriptor value)
        {
            DisposePreview(); avatar = value; scroll = Vector2.zero;
            if (value != null && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                try
                {
                    preview = CreateInstance<AvatarMonitorView>();
                    preview.Initialize(PPTAvatarEditorView.ToolFunc.FaceEmotion);
                    preview.LoadSettingData(EditorSetting.instance.Data);
                    preview.SetAvatarPreview(value);
                    preview.avatarMonitorField.MoveAvatarCam(true);
                    foreach (var item in PreviewRoot.GetComponentsInChildren<Transform>(true))
                        item.gameObject.hideFlags = HideFlags.HideAndDontSave;
                    foreach (var behaviour in PreviewRoot.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                    foreach (var body in PreviewRoot.GetComponentsInChildren<Rigidbody>(true))
                    { body.isKinematic = true; body.detectCollisions = false; }
                    foreach (var particle in PreviewRoot.GetComponentsInChildren<ParticleSystem>(true))
                        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    catalog = new FxViewerCatalog(value);
                    playback = new FxClipPreview(PreviewRoot, catalog.Entries.Select(e => e.Clip));
                    int face = Array.FindIndex(catalog.Layers, name => name.IndexOf("face", StringComparison.OrdinalIgnoreCase) >= 0);
                    layer = face >= 0 ? face : -1;
                }
                catch (Exception error) { DisposePreview(); Debug.LogException(error); ShowNotification(new GUIContent(error.Message)); }
            }
            Repaint();
        }

        public void ConfirmClip(AnimationClip clip)
        {
            if (catalog == null || clip == null) return;
            ConfirmEntry(catalog.Entries.FirstOrDefault(e => e.Clip == clip));
        }

        private void ConfirmEntry(FxViewerCatalog.Entry entry)
        {
            if (playback == null || entry?.Clip == null) return;
            selectedEntry = entry;
            playback.Select(entry.Clip);
            playing = entry.Clip.length > .1f;
            lastTick = EditorApplication.timeSinceStartup;
            Repaint();
        }

        public bool ReplaceClip(int entryIndex, AnimationClip clip)
        {
            if (catalog == null || EditorApplication.isPlayingOrWillChangePlaymode
                || entryIndex < 0 || entryIndex >= catalog.Entries.Count) return false;
            var entry = catalog.Entries[entryIndex];
            if (!catalog.CanAssign(entry)) return false;
            Undo.IncrementCurrentGroup();
            catalog.Assign(entry, clip);
            RefreshCatalog();
            return true;
        }

        private void RefreshCatalog()
        {
            if (avatar == null || playback == null || PreviewRoot == null) return;
            var previous = selectedEntry;
            catalog = new FxViewerCatalog(avatar);
            layer = Mathf.Clamp(layer, -1, catalog.Layers.Length - 1);
            selectedEntry = previous == null ? null : catalog.Entries.FirstOrDefault(e =>
                e.Layer == previous.Layer && e.State == previous.State
                && e.Tree == previous.Tree && e.ChildIndex == previous.ChildIndex);
            if (SelectedClip != selectedEntry?.Clip)
            {
                playing = false;
                playback.Select(selectedEntry?.Clip);
            }
            Repaint();
        }

        public void SetPreviewTime(float time)
        {
            playing = false; playback?.Sample(time); Repaint();
        }

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (playing && SelectedClip != null && SelectedClip.length > 0 && PreviewRoot != null)
            {
                playback.Sample(Mathf.Repeat(PreviewTime + (float)(now - lastTick), SelectedClip.length));
                Repaint();
            }
            lastTick = now;
        }

        private void OnGUI()
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            using (new GUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(24)))
            {
                GUILayout.Label(T("avatar"), GUILayout.Width(50));
                EditorGUI.BeginChangeCheck();
                var chosen = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(avatar, typeof(VRCAvatarDescriptor), true);
                if (EditorGUI.EndChangeCheck()) SelectAvatar(chosen);
                using (new EditorGUI.DisabledScope(avatar == null))
                    if (GUILayout.Button(T("reload"), EditorStyles.toolbarButton)) SelectAvatar(avatar);
            }
            if (PreviewRoot == null)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    GUI.Label(new Rect(12, 40, position.width - 24, 24), T("selectAvatar"), EditorStyles.boldLabel);
                return;
            }

            var work = new Rect(6, 30, position.width - 12, position.height - 36);
            float width = Mathf.Clamp(work.width * previewFraction, 260, work.width - 340);
            PreviewPanelRect = new Rect(work.x, work.y, width, work.height);
            var divider = new Rect(PreviewPanelRect.xMax, work.y, 6, work.height);
            FxPanelRect = new Rect(divider.xMax, work.y, work.xMax - divider.xMax, work.height);
            Resize(divider, work);
            GUI.Box(PreviewPanelRect, GUIContent.none, EditorStyles.helpBox);
            GUI.Box(FxPanelRect, GUIContent.none, EditorStyles.helpBox);
            DrawPreviewPanel(PreviewPanelRect);
            DrawFx(new Rect(FxPanelRect.x + 5, FxPanelRect.y + 5, FxPanelRect.width - 10, FxPanelRect.height - 10));
            if (GUI.changed) Repaint();
        }

        private void DrawPreviewPanel(Rect panel)
        {
            GUILayout.BeginArea(new Rect(panel.x + 4, panel.y + 4, panel.width - 8, panel.height - 31));
            if (preview.DrawWorkspaceGUI(panel.width - 8, panel.height - 31)) Repaint();
            GUILayout.EndArea();
            DrawPlayback(new Rect(panel.x + 5, panel.yMax - 25, panel.width - 10, 20));
        }

        private void DrawPlayback(Rect rect)
        {
            using (new EditorGUI.DisabledScope(SelectedClip == null || SelectedClip.length <= 0))
            {
                if (GUI.Button(new Rect(rect.x, rect.y, 58, 20), T(playing ? "pausePreview" : "playPreview"), EditorStyles.toolbarButton))
                { playing = !playing; lastTick = EditorApplication.timeSinceStartup; }
                float length = SelectedClip != null ? SelectedClip.length : 0;
                EditorGUI.BeginChangeCheck();
                float time = GUI.HorizontalSlider(new Rect(rect.x + 65, rect.y + 5, Mathf.Max(30, rect.width - 152), 15), PreviewTime, 0, Mathf.Max(length, .001f));
                if (EditorGUI.EndChangeCheck()) SetPreviewTime(time);
                GUI.Label(new Rect(rect.xMax - 80, rect.y, 80, 20), PreviewTime.ToString("0.00") + " / " + length.ToString("0.00"), EditorStyles.miniLabel);
            }
        }

        private void DrawFx(Rect rect)
        {
            GUI.Label(new Rect(rect.x, rect.y, rect.width, 19), T("fx"), EditorStyles.boldLabel);
            var labels = new[] { T("allLayers") }.Concat(catalog.Layers).ToArray();
            GUI.Label(new Rect(rect.x, rect.y + 23, 64, 19), T("layer"));
            EditorGUI.BeginChangeCheck();
            layer = EditorGUI.Popup(new Rect(rect.x + 64, rect.y + 23, rect.width - 64, 19), Mathf.Clamp(layer + 1, 0, labels.Length - 1), labels) - 1;
            if (EditorGUI.EndChangeCheck()) scroll = Vector2.zero;
            var entries = catalog.Entries.Where(e => layer < 0 || e.Layer == layer).ToArray();
            var viewport = new Rect(rect.x, rect.y + 47, rect.width, Mathf.Max(20, rect.height - 47));
            float width = viewport.width - 17;
            FirstConfirmRect = Rect.zero;
            scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0, 0, width, entries.Length * 24));
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                var row = new Rect(0, i * 24, width, 22);
                if (entry == selectedEntry && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(row, new Color(.25f, .5f, .8f, .25f));
                string label = layer < 0 ? catalog.Layers[entry.Layer] + "/" + entry.Label : entry.Label;
                float nameWidth = width * .4f;
                GUI.Label(new Rect(1, row.y, nameWidth - 4, 20), new GUIContent(label, label));
                using (new EditorGUI.DisabledScope(!catalog.CanAssign(entry)))
                {
                    EditorGUI.BeginChangeCheck();
                    var clip = (AnimationClip)EditorGUI.ObjectField(new Rect(nameWidth, row.y, width - nameWidth - 60, 20),
                        entry.Clip, typeof(AnimationClip), false);
                    if (EditorGUI.EndChangeCheck()) ReplaceClip(catalog.Entries.IndexOf(entry), clip);
                }
                var button = new Rect(width - 56, row.y, 56, 20);
                if (i == 0) FirstConfirmRect = new Rect(viewport.position + button.position - scroll, button.size);
                using (new EditorGUI.DisabledScope(entry.Clip == null))
                    if (GUI.Button(button, T("confirmPreview"))) ConfirmEntry(entry);
            }
            GUI.EndScrollView();
        }

        private void Resize(Rect divider, Rect work)
        {
            EditorGUIUtility.AddCursorRect(divider, MouseCursor.ResizeHorizontal);
            int control = GUIUtility.GetControlID(841925, FocusType.Passive, divider);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && divider.Contains(e.mousePosition))
            { GUIUtility.hotControl = control; dragMode = 1; e.Use(); }
            if (GUIUtility.hotControl != control || dragMode != 1) return;
            if (e.type == EventType.MouseDrag)
            { previewFraction = Mathf.Clamp((e.mousePosition.x - work.x) / work.width, .2f, .75f); e.Use(); Repaint(); }
            if (e.type == EventType.MouseUp) { GUIUtility.hotControl = 0; dragMode = 0; e.Use(); }
        }

    }
}
