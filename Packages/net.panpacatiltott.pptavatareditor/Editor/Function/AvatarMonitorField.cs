using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRCAvatar = PPTAvatarEditor.Avatars3.VRCAvatar3;
using VRC.SDK3.Avatars.Components;
using VRC_AvatarDescriptor = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor;

namespace PPTAvatarEditor
{
    public class AvatarMonitorField : IDisposable
    {
        private Scene scene;
        private GameObject cameraObj;
        private GameObject avatarObj;
        private GameObject lightObj;
        private GameObject viewPositionObj;
        private Camera camera;
        private RenderTexture renderTexture;
        private Material textureMat;

        private VRC_AvatarDescriptor descriptor;

        public VRCAvatar avatar { get; private set; }
        private SkinnedMeshRenderer faceMesh;

        private Rect rect;

        private float mainOrthographicSize;
        private float subOrthographicSize;
        private float defaultOrthographicSize = 0.5f;
        private float faceOrthographicSize = 0.1f;
        private float orthographicsStep = 0.1f;

        public AvatarMonitorField()
        {
            this.textureMat = CreateGammaMaterial();

            this.mainOrthographicSize = defaultOrthographicSize;
            this.subOrthographicSize = 0f;

            scene = EditorSceneManager.NewPreviewScene();

            cameraObj = CreateCameraObj();
            AddGameObject(cameraObj);
            SetAvatarCamBgColor(Color.black);

            lightObj = CreateLightObj();
            AddGameObject(lightObj);

            viewPositionObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            viewPositionObj.name = "ViewPosition";
            viewPositionObj.transform.localScale = Vector3.one * 0.03f;
            AddGameObject(viewPositionObj);
            viewPositionObj.SetActive(false);
        }


        public void Initinalize(VRCAvatar avatar)
        {

        }

        public bool HasAvatar => avatarObj != null && descriptor != null;

        public bool Render(Rect previewRect, bool isGammaCorrection = true, bool showViewPosition = false)
        {
            rect = previewRect;
            if (rect.width < 1 || rect.height < 1) return false;
            if (!HasAvatar)
            {
                EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
                GUI.Label(rect, "Avatar を選択すると、ここにプレビューを表示します。",
                    EditorStyles.centeredGreyMiniLabel);
                return false;
            }

            bool changed = false;
            var control = GUIUtility.GetControlID("PPAEPreview".GetHashCode(), FocusType.Passive, rect);
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                GUIUtility.hotControl = control;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == control)
            {
                RotateAvatarCam(evt.delta);
                changed = true;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
            }
            else if (evt.type == EventType.ScrollWheel && rect.Contains(evt.mousePosition))
            {
                ZoomAvatarCam(evt.delta);
                changed = true;
                evt.Use();
            }

            if (evt.type == EventType.Repaint)
            {
                var scale = Mathf.Min(EditorGUIUtility.pixelsPerPoint, 2048f / Mathf.Max(rect.width, rect.height));
                ResizeMonitor(Mathf.Max(32, Mathf.CeilToInt(rect.width * scale)),
                    Mathf.Max(32, Mathf.CeilToInt(rect.height * scale)));
                viewPositionObj.SetActive(showViewPosition);
                if (showViewPosition)
                    viewPositionObj.transform.position = descriptor.transform.TransformPoint(descriptor.ViewPosition);

                var oldAllowPipes = Unsupported.useScriptableRenderPipeline;
                var oldActiveTexture = RenderTexture.active;
                try
                {
                    Unsupported.useScriptableRenderPipeline = false;
                    camera.aspect = rect.width / rect.height;
                    camera.Render();
                }
                finally
                {
                    Unsupported.useScriptableRenderPipeline = oldAllowPipes;
                    RenderTexture.active = oldActiveTexture;
                }

                if (textureMat == null) textureMat = CreateGammaMaterial();
                Graphics.DrawTexture(rect, renderTexture, isGammaCorrection ? textureMat : null);
            }
            return changed;
        }

        public void ClearAvatar()
        {
            if (avatarObj != null) UnityEngine.Object.DestroyImmediate(avatarObj);
            avatarObj = null;
            descriptor = null;
            avatar = null;
        }

        public void AddGameObject(GameObject obj)
        {
            SceneManager.MoveGameObjectToScene(obj, scene);
        }

        private GameObject CreateCameraObj()
        {
            var cameraObj = new GameObject("Camera", typeof(Camera));
            camera = cameraObj.GetComponent<Camera>();
            camera.cameraType = CameraType.Preview;
            camera.orthographic = true;
            camera.orthographicSize = defaultOrthographicSize;
            camera.forceIntoRenderTexture = true;
            camera.scene = scene;
            camera.enabled = false;
            camera.nearClipPlane = 0.01f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            return cameraObj;
        }

        private GameObject CreateLightObj()
        {
            var lightObj = new GameObject("Directional Light", typeof(Light));
            lightObj.transform.rotation = Quaternion.Euler(50, -30, 0);
            var light = lightObj.GetComponent<Light>();
            light.type = LightType.Directional;
            return lightObj;
        }

        public VRCAvatar AddAvatar(VRC_AvatarDescriptor descriptor)
        {
            if (avatarObj != null)
                UnityEngine.Object.DestroyImmediate(avatarObj);

            var newAvatarObj = GameObject.Instantiate(descriptor.gameObject);
            newAvatarObj.name = descriptor.gameObject.name;
            newAvatarObj.transform.position = Vector3.zero;
            newAvatarObj.transform.rotation = Quaternion.identity;
            newAvatarObj.SetActive(true);
            AddGameObject(newAvatarObj);
            this.avatarObj = newAvatarObj;
            newAvatarObj.transform.position = new Vector3(0, 0, 0);
            this.descriptor = newAvatarObj.GetComponent<VRC_AvatarDescriptor>();
            avatar = new VRCAvatar(this.descriptor);
            ResetCameraTransform();

            return avatar;
        }

        public void SetAvatarCamBgColor(Color col)
        {
            camera.backgroundColor = col;
        }

        /// <summary>
        /// AvatarCamを回転させる
        /// </summary>
        /// <param name="delta"></param>
        private void RotateAvatarCam(Vector2 delta)
        {
            if (camera == null || avatarObj == null || delta == Vector2.zero) return;
            camera.transform.RotateAround(avatarObj.transform.position, Vector3.up, delta.x);
            //camera.transform.RotateAround(avatarObj.transform.position, Vector3.right, delta.y);
        }

        /// <summary>
        /// AvatarCamをズームさせる(マウスホイール)
        /// </summary>
        /// <param name="delta"></param>
        private void ZoomAvatarCam(Vector2 delta)
        {
            if (camera == null || Mathf.Approximately(delta.y, 0)) return;
            mainOrthographicSize = Mathf.Clamp(camera.orthographicSize * Mathf.Exp(delta.y * 0.06f), 0.005f, 50f);
            subOrthographicSize = 0;
            camera.orthographicSize = mainOrthographicSize;
        }

        /// <summary>
        /// AvatarCamをズームさせる（スライダー）
        /// </summary>
        /// <param name="delta"></param>
        public void ZoomAvatarCam(float level)
        {
            if (camera == null) return;
            var newSubOrthographicsSize = ZoomRange * Mathf.Clamp01(level);
            camera.orthographicSize = Mathf.Max(0.005f, mainOrthographicSize - newSubOrthographicsSize);
            subOrthographicSize = newSubOrthographicsSize;
        }

        /// <summary>
        /// AvatarCamの位置を動かす
        /// </summary>
        public void MoveAvatarCam(bool setToFace, bool lookZoom = false)
        {
            if (camera == null || descriptor == null) return;

            // 顔にあわせる
            if (setToFace)
            {
                mainOrthographicSize = faceOrthographicSize;
            }
            else
            {
                mainOrthographicSize = defaultOrthographicSize;
            }
            camera.transform.position = new Vector3(0, descriptor.ViewPosition.y, 1);
            cameraObj.transform.rotation = Quaternion.Euler(0, 180, 0);

            if (!lookZoom)
            {
                subOrthographicSize = 0f;
                camera.orthographicSize = mainOrthographicSize;
            }
        }

        public void ResetCameraTransform()
        {
            if (cameraObj == null || descriptor == null) return;
            cameraObj.transform.position = new Vector3(0, descriptor.ViewPosition.y, 1);
            cameraObj.transform.rotation = Quaternion.Euler(0, 180, 0);
            mainOrthographicSize = defaultOrthographicSize;
            subOrthographicSize = 0f;
            camera.orthographicSize = mainOrthographicSize;
        }

        /// <summary>
        /// AvatarCamの高さを変える
        /// </summary>
        /// <param name="value"></param>
        public void MoveAvatarCamHeight(float value)
        {
            if (camera == null || descriptor == null) return;
            var y = Mathf.Lerp(0, descriptor.ViewPosition.y * 1.1f, value);
            var nowCamPos = camera.transform.position;
            camera.transform.position = new Vector3(nowCamPos.x, y, nowCamPos.z);
        }

        public void RotateCamera(int angleY)
        {
            if (camera == null || avatarObj == null) return;
            camera.transform.RotateAround(avatarObj.transform.position, Vector3.up, angleY);
        }

        /// <summary>
        /// DirectionalLightを回転させる
        /// </summary>
        /// <param name="delta"></param>
        public void RotateLight(Vector2 delta)
        {
            if (lightObj == null) return;

            lightObj.transform.Rotate(Vector3.up, -delta.x);
        }

        private Material CreateGammaMaterial()
        {
            Shader gammaShader = Resources.Load<Shader>("PPAE/Gamma");
            return gammaShader != null ? new Material(gammaShader) { hideFlags = HideFlags.HideAndDontSave } : null;
        }

        private void ResizeMonitor(int width, int height)
        {
            if (renderTexture != null && renderTexture.width == width && renderTexture.height == height) return;
            camera.targetTexture = null;
            if (renderTexture != null)
            {
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
            renderTexture = new RenderTexture(width, height, 24) { hideFlags = HideFlags.HideAndDontSave };
            camera.targetTexture = renderTexture;
        }

        public float GetNormalizedMonitorHeight()
        {
            if (cameraObj == null || descriptor == null) return 1f;
            return cameraObj.transform.position.y / (descriptor.ViewPosition.y * 1.1f);
        }

        public float GetNormalizedSubOrthographicSize()
        {
            return ZoomRange > 0 ? Mathf.Clamp01(subOrthographicSize / ZoomRange) : 0;
        }

        private float ZoomRange => Mathf.Min(Mathf.Max(0.001f, orthographicsStep), mainOrthographicSize * 0.85f);

        public void SetZoomParameters(float defaultZoomValue, float faceZoomValue, float zoomStepValue)
        {
            defaultOrthographicSize = Mathf.Max(0.01f, defaultZoomValue);
            faceOrthographicSize = Mathf.Max(0.01f, faceZoomValue);
            orthographicsStep = Mathf.Max(0.001f, zoomStepValue);
        }

        public void Dispose()
        {
            if (camera != null) camera.targetTexture = null;
            if (renderTexture != null)
            {
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
                renderTexture = null;
            }

            if (cameraObj != null)
            {
                UnityEngine.Object.DestroyImmediate(cameraObj);
            }
            if (avatarObj != null)
            {
                UnityEngine.Object.DestroyImmediate(avatarObj);
            }
            if (lightObj != null)
            {
                UnityEngine.Object.DestroyImmediate(lightObj);
            }

            if (textureMat != null) UnityEngine.Object.DestroyImmediate(textureMat);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
