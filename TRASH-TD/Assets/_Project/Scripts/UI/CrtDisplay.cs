using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace TrashTD.UI
{
    /// <summary>
    /// Shows the whole game on an old CRT TV. Created automatically before the first scene and kept across
    /// scene loads. Every frame, before rendering:
    ///   - the scene's cameras render into one screen-sized picture texture instead of the screen;
    ///   - every Screen Space - Overlay canvas becomes Screen Space - Camera on a UI camera that is stacked on
    ///     the scene camera (URP camera stack), so the HUD/menus land in the same picture above the world,
    ///     keeping their sorting orders;
    ///   - the picture is drawn full-screen through the CRT shader (CrtScreen.Television settings).
    /// Because the glass is curved, pointer positions are bent the same way: UI raycasts through
    /// CrtGraphicRaycaster, and gameplay code converts mouse positions with <see cref="ScreenToPicture"/>
    /// before using them with a camera.
    /// </summary>
    [DefaultExecutionOrder(10000)] // after everything else's LateUpdate, so this frame's new canvases are caught
    public sealed class CrtDisplay : MonoBehaviour, ICrtPointerMapper
    {
        private static CrtDisplay instance;

        // The UI camera sits far from any scene content; canvases are placed in front of it
        private static readonly Vector3 UiCameraPosition = new Vector3(20000f, 0f, 0f);
        private const float CanvasPlaneDistance = 10f;

        private CrtScreen.Settings settings = CrtScreen.Television;
        private Material material;
        private RenderTexture picture;
        private Camera screenCamera;   // only clears the real screen so the presenter has a frame to draw on
        private Camera uiCamera;       // renders the converted canvases on top of the scene camera
        private Camera stackedOn;
        private Canvas presentCanvas;
        private RawImage presentImage;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject("CrtDisplay");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<CrtDisplay>();
        }

        /// <summary>True while the game is being shown through the CRT.</summary>
        public static bool IsActive => instance != null && instance.isActiveAndEnabled && instance.picture != null;

        /// <summary>
        /// Converts a real screen position (mouse, PointerEventData) to the matching position in the game
        /// picture, which is what cameras (ScreenToWorldPoint) and canvases expect. Unchanged when inactive.
        /// </summary>
        public static Vector2 ScreenToPicture(Vector2 screenPosition)
        {
            if (!IsActive) return screenPosition;
            Vector2 size = new Vector2(instance.picture.width, instance.picture.height);
            Vector2 uv = new Vector2(screenPosition.x / Screen.width, screenPosition.y / Screen.height);
            return Vector2.Scale(CrtScreen.Distort(uv, instance.settings.curvature), size);
        }

        /// <summary>The CRT's own full-screen canvas; skip it when looking for a canvas to build UI on.</summary>
        public static bool IsDisplayCanvas(Canvas canvas) => instance != null && canvas != null && canvas == instance.presentCanvas;

        /// <summary>First root canvas that game UI can be parented to (never the CRT's own canvas).</summary>
        public static Canvas FindGameCanvas()
        {
            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.isRootCanvas && !IsDisplayCanvas(canvas) && canvas.renderMode != RenderMode.WorldSpace)
                    return canvas;
            }
            return null;
        }

        public bool IsMappingPointer => isActiveAndEnabled && picture != null;

        public bool TryMapScreenPoint(Vector2 screenPosition, out Vector2 picturePosition)
        {
            picturePosition = default;
            if (!IsMappingPointer) return false;

            Vector2 uv = new Vector2(screenPosition.x / Screen.width, screenPosition.y / Screen.height);
            Vector2 pictureUv = CrtScreen.Distort(uv, settings.curvature);
            if (!CrtScreen.IsOnScreen(pictureUv, settings.cornerRadius)) return false;

            picturePosition = new Vector2(pictureUv.x * picture.width, pictureUv.y * picture.height);
            return true;
        }

        private void Awake()
        {
            material = CrtScreen.CreateMaterial("CRT (Display)", this);
            CrtScreen.ApplySettings(material, settings);

            screenCamera = CreateCamera("CrtScreenCamera", new Vector3(-30000f, 0f, 0f));
            screenCamera.cullingMask = 0;
            screenCamera.clearFlags = CameraClearFlags.SolidColor;
            screenCamera.backgroundColor = Color.black;
            screenCamera.depth = -1000f;

            uiCamera = CreateCamera("CrtUiCamera", UiCameraPosition);
            uiCamera.clearFlags = CameraClearFlags.Depth;
            uiCamera.cullingMask = ~0;

            var canvasObject = new GameObject("CrtDisplayCanvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            presentCanvas = canvasObject.GetComponent<Canvas>();
            presentCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            presentCanvas.sortingOrder = short.MaxValue;

            var imageObject = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
            imageObject.transform.SetParent(canvasObject.transform, false);
            var imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
            presentImage = imageObject.GetComponent<RawImage>();
            presentImage.raycastTarget = false;
            presentImage.material = material;

            if (material == null)
            {
                // Without the shader there is nothing to add; leave the game rendering normally
                enabled = false;
                presentCanvas.enabled = false;
                screenCamera.enabled = false;
                uiCamera.enabled = false;
            }
        }

        private Camera CreateCamera(string cameraName, Vector3 position)
        {
            var go = new GameObject(cameraName);
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            Camera cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.allowMSAA = false;
            return cam;
        }

        private void OnDestroy()
        {
            if (picture != null)
            {
                picture.Release();
                Destroy(picture);
            }
            if (material != null) Destroy(material);
            if (instance == this) instance = null;
        }

        private void LateUpdate()
        {
            SyncPicture();
            RedirectCameras();
            ConvertCanvases();

            presentImage.texture = picture;
            CrtScreen.UpdateFrame(material, settings, picture.width, picture.height);
        }

        private void SyncPicture()
        {
            int width = Mathf.Max(16, Screen.width);
            int height = Mathf.Max(16, Screen.height);
            if (picture != null && picture.width == width && picture.height == height) return;

            if (picture != null)
            {
                foreach (Camera cam in Camera.allCameras)
                {
                    if (cam.targetTexture == picture) cam.targetTexture = null;
                }
                picture.Release();
                Destroy(picture);
            }

            picture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "CRT_Picture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            picture.Create();
        }

        /// <summary>
        /// Points the scene cameras at the picture and stacks the UI camera on the scene's main camera.
        /// With no scene camera, the UI camera renders the picture on its own.
        /// </summary>
        private void RedirectCameras()
        {
            Camera baseCamera = null;
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == screenCamera || cam == uiCamera) continue;
                if (cam.targetTexture != null && cam.targetTexture != picture) continue; // e.g. a CrtScreen monitor

                UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
                if (data != null && data.renderType == CameraRenderType.Overlay) continue;

                cam.targetTexture = picture;
                if (baseCamera == null || cam == Camera.main) baseCamera = cam;
            }

            UniversalAdditionalCameraData uiData = uiCamera.GetUniversalAdditionalCameraData();
            if (baseCamera == null)
            {
                if (uiData.renderType != CameraRenderType.Base) uiData.renderType = CameraRenderType.Base;
                uiCamera.clearFlags = CameraClearFlags.SolidColor;
                uiCamera.backgroundColor = Color.black;
                uiCamera.targetTexture = picture;
                stackedOn = null;
                return;
            }

            if (uiData.renderType != CameraRenderType.Overlay)
            {
                uiCamera.targetTexture = null;
                uiData.renderType = CameraRenderType.Overlay;
            }

            if (stackedOn != baseCamera)
            {
                UniversalAdditionalCameraData baseData = baseCamera.GetUniversalAdditionalCameraData();
                if (!baseData.cameraStack.Contains(uiCamera)) baseData.cameraStack.Add(uiCamera);
                stackedOn = baseCamera;
            }
        }

        /// <summary>
        /// Moves every overlay canvas into the picture (keeping its sorting), then bends the raycasts of every
        /// canvas drawn there, nested ones with their own raycaster included (e.g. the environment briefing).
        /// </summary>
        private void ConvertCanvases()
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (Canvas canvas in canvases)
            {
                if (canvas == presentCanvas || !canvas.isRootCanvas) continue;
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = uiCamera;
                canvas.planeDistance = CanvasPlaneDistance;
            }

            foreach (Canvas canvas in canvases)
            {
                if (canvas == null || canvas == presentCanvas) continue;
                Canvas root = canvas.rootCanvas;
                if (root.renderMode != RenderMode.ScreenSpaceCamera || root.worldCamera != uiCamera) continue;

                GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster == null || raycaster is CrtGraphicRaycaster) continue;

                bool ignoreReversed = raycaster.ignoreReversedGraphics;
                GraphicRaycaster.BlockingObjects blocking = raycaster.blockingObjects;
                DestroyImmediate(raycaster);

                var bent = canvas.gameObject.AddComponent<CrtGraphicRaycaster>();
                bent.ignoreReversedGraphics = ignoreReversed;
                bent.blockingObjects = blocking;
                bent.Owner = this;
            }
        }
    }
}
