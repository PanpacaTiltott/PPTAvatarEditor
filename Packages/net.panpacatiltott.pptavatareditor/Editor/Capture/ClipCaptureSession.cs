// Copyright (C) 2026 PanpacaTiltott. Distributed under the Zlib License.
using System;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace PPTAvatarEditor
{
    [Serializable]
    public sealed class ClipCaptureSettings
    {
        public int Width = 1024, Height = 1024;
        public bool Transparent = true;
        public Color Background = Color.white;
        public float SampleTime, Yaw, Pitch, VerticalOffset, HorizontalOffset;
        public float Zoom = 1, FieldOfView = 30;

        public void Validate()
        {
            if (Width < 64 || Height < 64 || Width > 4096 || Height > 4096)
                throw new ArgumentException("PNG size must be between 64 and 4096 px.");
            foreach (float value in new[] { SampleTime, Yaw, Pitch, VerticalOffset, HorizontalOffset, Zoom, FieldOfView,
                Background.r, Background.g, Background.b, Background.a })
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("Invalid capture setting.");
            if (SampleTime < 0 || Zoom < .1f || Zoom > 5 || FieldOfView < 10 || FieldOfView > 80)
                throw new ArgumentException("Invalid camera or sample time.");
        }

        public void ResetCamera()
        { Yaw = Pitch = VerticalOffset = HorizontalOffset = 0; Zoom = 1; FieldOfView = 30; }
    }

    // A session owns a single clip and a private preview scene. Each batch entry starts from the source pose.
    public sealed class ClipCaptureSession : IDisposable
    {
        private PreviewRenderUtility utility;
        private FxClipPreview playback;
        private Vector3 focus;
        private float radius;
        public GameObject Clone { get; private set; }
        public float FaceRadius => radius;

        public ClipCaptureSession(VRCAvatarDescriptor source, AnimationClip clip)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            utility = new PreviewRenderUtility();
            try
            {
                var holder = new GameObject("PPAE clip capture") { hideFlags = HideFlags.HideAndDontSave };
                holder.SetActive(false);
                utility.AddSingleGO(holder);
                Clone = Object.Instantiate(source.gameObject, holder.transform, false);
                Clone.name = source.name;
                Clone.SetActive(false);
                Clone.transform.localPosition = Vector3.zero;
                Clone.transform.localRotation = Quaternion.identity;
                Clone.transform.localScale = source.transform.lossyScale;
                foreach (var item in Clone.GetComponentsInChildren<Transform>(true))
                    item.gameObject.hideFlags = HideFlags.HideAndDontSave;
                foreach (var behaviour in Clone.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                foreach (var body in Clone.GetComponentsInChildren<Rigidbody>(true))
                { body.isKinematic = true; body.detectCollisions = false; }
                foreach (var particle in Clone.GetComponentsInChildren<ParticleSystem>(true))
                    particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                foreach (var renderer in Clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    renderer.updateWhenOffscreen = true;

                var animator = source.GetComponent<Animator>();
                var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
                var previewHead = head != null
                    ? Clone.transform.Find(AnimationUtility.CalculateTransformPath(head, source.transform)) : null;
                var viewpoint = Clone.transform.TransformPoint(source.ViewPosition);
                float headHeight = previewHead != null ? Mathf.Abs(previewHead.position.y) : Mathf.Abs(viewpoint.y);
                if (headHeight < .01f)
                {
                    var renderers = Clone.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length > 0)
                    {
                        var bounds = renderers[0].bounds;
                        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                        viewpoint = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * .85f, bounds.center.z);
                        headHeight = Mathf.Abs(viewpoint.y);
                    }
                }
                float height = Mathf.Max(headHeight * 1.2f, .2f);
                radius = Mathf.Clamp(height * .16f, .025f, 2);
                focus = previewHead != null ? previewHead.position + Vector3.up * height * .035f : viewpoint;

                Clone.SetActive(true);
                holder.SetActive(true);
                playback = new FxClipPreview(Clone, clip != null ? new[] { clip } : new AnimationClip[0]);
                playback.Select(clip);
                utility.camera.clearFlags = CameraClearFlags.SolidColor;
                utility.camera.nearClipPlane = .005f;
                utility.camera.farClipPlane = Mathf.Max(100, height * 20);
                utility.camera.allowHDR = false;
                utility.camera.allowMSAA = false;
                utility.ambientColor = new Color(.65f, .65f, .65f);
                utility.lights[0].intensity = 1.1f;
                utility.lights[0].transform.rotation = Quaternion.Euler(35, 165, 0);
                utility.lights[1].intensity = .55f;
                utility.lights[1].transform.rotation = Quaternion.Euler(10, -25, 0);
            }
            catch { Dispose(); throw; }
        }

        public Texture2D Capture(ClipCaptureSettings settings, int width, int height)
        {
            if (utility == null) throw new ObjectDisposedException(nameof(ClipCaptureSession));
            settings.Validate();
            if (width < 1 || height < 1 || width > 4096 || height > 4096) throw new ArgumentOutOfRangeException(nameof(width));
            playback.Sample(settings.SampleTime);
            var camera = utility.camera;
            camera.fieldOfView = settings.FieldOfView;
            camera.backgroundColor = settings.Transparent ? Color.clear
                : new Color(settings.Background.r, settings.Background.g, settings.Background.b, 1);
            var direction = Quaternion.Euler(settings.Pitch, settings.Yaw, 0) * Vector3.forward;
            var rotation = Quaternion.LookRotation(-direction, Vector3.up);
            var center = focus + Vector3.up * settings.VerticalOffset
                + rotation * Vector3.right * settings.HorizontalOffset;
            float distance = radius / Mathf.Tan(settings.FieldOfView * Mathf.Deg2Rad * .5f) / settings.Zoom;
            camera.transform.SetPositionAndRotation(center + direction * distance, rotation);
            var previous = RenderTexture.active;
            Texture2D image = null;
            bool opened = false;
            try
            {
                // BeginStaticPreview/EndStaticPreview returns RGB24 in Unity 2022.3 and loses alpha.
                // Divide by the UI scale so the PNG dimensions do not depend on monitor DPI.
                float dpi = EditorGUIUtility.pixelsPerPoint;
                utility.BeginPreview(new Rect(0, 0, width / dpi, height / dpi), GUIStyle.none);
                opened = true;
                camera.aspect = (float)width / height;
                camera.pixelRect = new Rect(0, 0, width, height);
                utility.Render(true, false);
                RenderTexture.active = camera.targetTexture;
                image = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                { name = "PPAE capture", hideFlags = HideFlags.HideAndDontSave };
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                // PreviewRenderUtility renders to a non-sRGB target; PNG stores display-space RGB.
                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                {
                    var pixels = image.GetPixels();
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
                    image.SetPixels(pixels);
                }
                image.Apply(false, false);
                return image;
            }
            catch { if (image != null) Object.DestroyImmediate(image); throw; }
            finally
            {
                if (opened) utility.EndPreview();
                RenderTexture.active = previous;
            }
        }

        public void Dispose()
        {
            playback?.Dispose(); playback = null;
            utility?.Cleanup(); utility = null;
            Clone = null;
        }
    }
}
