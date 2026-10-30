using UnityEngine;
using UnityEngine.UI;

namespace TrashTD.UI
{
    /// <summary>
    /// Shows the whole game on an old CRT TV. Created automatically before the first scene and kept across
    /// scene loads. Every frame, before rendering:
    ///   - the scene's cameras render into a screen-sized game texture instead of the screen;
    ///   - every Screen Space - Overlay canvas becomes Screen Space - Camera on a dedicated UI camera that
    ///     renders into its own transparent UI texture (sorting orders keep working);
    ///   - the CRT shader (CrtScreen.Television settings) layers the UI texture over the game texture and draws
    ///     the result full-screen, on a screen camera that renders last so the frame is never a frame behind.
    /// Separate textures instead of a URP camera stack: each camera is a plain base camera drawing to its own
    /// target, which renders reliably with the 2D Renderer.
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

        private static readonly int UiTexId = Shader.PropertyToID("_UiTex");
        private static readonly int HasUiId = Shader.PropertyToID("_HasUi");

        private CrtScreen.Settings settings = CrtScreen.Television;

        /// <summary>
        /// The whole game through the chosen CRT style: Full TV (Television), Classic (the first, crisper
        /// version) or Off (passthrough). Full TV eases off below 1080p so small text stays readable.
        /// The player's brightness applies on top in every style.
        /// </summary>
        private static CrtScreen.Settings GetActiveSettings(int pictureHeight)
        {
            CrtScreen.Settings baseSettings = GameGraphicsSettings.CrtStyle switch
            {
                CrtStyle.Classic => CrtScreen.ClassicTelevision,
                CrtStyle.Off => CrtScreen.Clean,
                _ => CrtScreen.Television
            };
            baseSettings.opaque = 1f; // full-screen presenter: never show the backbuffer through the picture
            return baseSettings.ForPictureHeight(pictureHeight).WithBrightnessScale(GameGraphicsSettings.Brightness);
        }

        private void ApplyGraphicsSettings()
        {
            settings = GetActiveSettings(picture != null ? picture.height : Mathf.Max(16, Screen.height));
            CrtScreen.ApplySettings(material, settings);
        }
        private Material material;
        private RenderTexture picture;   // the game world (scene cameras)
        private RenderTexture uiPicture; // the converted canvases, transparent where there is no UI
        private Camera screenCamera;     // renders last to the real screen; the CRT canvas is drawn on it
        private Camera uiCamera;         // renders the converted canvases into uiPicture
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
            foreach (Canvas canvas in FindObjectsByType<Canvas>())
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
            ApplyGraphicsSettings();
            GameGraphicsSettings.Changed += ApplyGraphicsSettings;

            // Renders after every other camera, so the CRT shows this frame's game and UI textures
            screenCamera = CreateCamera("CrtScreenCamera", new Vector3(-30000f, 0f, 0f));
            screenCamera.cullingMask = 0;
            screenCamera.clearFlags = CameraClearFlags.SolidColor;
            screenCamera.backgroundColor = Color.black;
            screenCamera.depth = 1000f;

            uiCamera = CreateCamera("CrtUiCamera", UiCameraPosition);
            uiCamera.clearFlags = CameraClearFlags.SolidColor;
            uiCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            uiCamera.cullingMask = ~0;
            uiCamera.depth = 900f;

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
            ReleaseTexture(ref picture);
            ReleaseTexture(ref uiPicture);
            if (material != null) Destroy(material);
            GameGraphicsSettings.Changed -= ApplyGraphicsSettings;
            if (instance == this) instance = null;
        }

        private void LateUpdate()
        {
            SyncTextures();
            bool hasSceneCamera = RedirectCameras();
            ConvertCanvases();

            // No scene camera (e.g. mid scene change): show black rather than a stale frame
            if (!hasSceneCamera)
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = picture;
                GL.Clear(true, true, Color.black);
                RenderTexture.active = previous;
            }

            presentImage.texture = picture;
            if (material != null)
            {
                material.SetTexture(UiTexId, uiPicture);
                material.SetFloat(HasUiId, 1f);
            }
            CrtScreen.UpdateFrame(material, settings, picture.width, picture.height);
        }

        private void SyncTextures()
        {
            int width = Mathf.Max(16, Screen.width);
            int height = Mathf.Max(16, Screen.height);
            if (picture != null && picture.width == width && picture.height == height) return;

            foreach (Camera cam in Camera.allCameras)
            {
                if (picture != null && cam.targetTexture == picture) cam.targetTexture = null;
                if (uiPicture != null && cam.targetTexture == uiPicture) cam.targetTexture = null;
            }
            ReleaseTexture(ref picture);
            ReleaseTexture(ref uiPicture);

            picture = CreateTexture("CRT_Game", width, height);
            uiPicture = CreateTexture("CRT_UI", width, height);
            uiCamera.targetTexture = uiPicture;
            ApplyGraphicsSettings(); // Full TV depends on the resolution
        }

        private static RenderTexture CreateTexture(string textureName, int width, int height)
        {
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = textureName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.Create();
            return texture;
        }

        private static void ReleaseTexture(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            Destroy(texture);
            texture = null;
        }

        /// <summary>Points every scene camera that draws to the screen at the game texture.</summary>
        private bool RedirectCameras()
        {
            bool found = false;
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == screenCamera || cam == uiCamera) continue;
                if (cam.targetTexture != null && cam.targetTexture != picture) continue; // e.g. a CrtScreen monitor

                if (cam.targetTexture != picture) cam.targetTexture = picture;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// Moves every overlay canvas into the picture (keeping its sorting), then bends the raycasts of every
        /// canvas drawn there, nested ones with their own raycaster included (e.g. the environment briefing).
        /// </summary>
        private void ConvertCanvases()
        {
            Canvas[] canvases = FindObjectsByType<Canvas>();
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
