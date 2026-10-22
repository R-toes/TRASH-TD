using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TrashTD.UI
{
    /// <summary>
    /// Shows a UI hierarchy through the old-TV CRT shader (Resources/Shaders/CrtScreen). The hierarchy lives on
    /// an offscreen Screen Space - Camera canvas that its own camera renders into a RenderTexture; this object's
    /// RawImage draws that texture with the CRT material. Parent UI to <see cref="Content"/>.
    /// When interactive, pointer positions are bent through the same curvature (CrtGraphicRaycaster) so clicks
    /// land on what is drawn under the cursor. The camera and canvas only run while this object is enabled.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class CrtScreen : MonoBehaviour, ICrtPointerMapper
    {
        [System.Serializable]
        public struct Settings
        {
            public float curvature;
            public float cornerRadius;
            public float sharpness;
            public float scanlineStrength;
            /// <summary>Output pixels per scanline; 0 = pick from the picture height (about 270 lines).</summary>
            public float scanlinePeriod;
            public float beamMin;
            public float beamMax;
            /// <summary>0 = aperture grille, 1 = slot mask.</summary>
            public float maskType;
            public float maskStrength;
            public float maskDark;
            public float maskLight;
            public float chromatic;
            public float bloom;
            public float vignette;
            public float noise;
            public float flicker;
            public float rollStrength;
            public float rollSpeed;
            public float jitter;
            public float glare;
            public float brightness;
            /// <summary>1 = ignore the picture's alpha (full-screen presenter over an uncleared backbuffer).</summary>
            public float opaque;
        }

        /// <summary>
        /// The whole game as an old TV (CrtDisplay): slight curve, ~270 scanlines, a full-strength slot mask
        /// and halation, which is what gives the pixel art its glowing RGB phosphor look.
        /// </summary>
        public static Settings Television => new Settings
        {
            curvature = 0.06f,
            cornerRadius = 0.03f,
            sharpness = 0.5f,
            scanlineStrength = 0.85f,
            scanlinePeriod = 0f,
            beamMin = 0.20f,
            beamMax = 0.42f,
            maskType = 1f,
            maskStrength = 1f,
            maskDark = 0.55f,
            maskLight = 1.45f,
            chromatic = 0.0012f,
            bloom = 0.35f,
            vignette = 0.6f,
            noise = 0.03f,
            flicker = 0.015f,
            rollStrength = 0.035f,
            rollSpeed = 0.08f,
            jitter = 0.0003f,
            glare = 0.04f,
            brightness = 1.3f,
            opaque = 1f
        };

        /// <summary>
        /// Small bulging monitor inside the TV (stage preview): stronger curve and fringing, but no scanlines or
        /// mask of its own (the TV supplies them; two line patterns would beat into moire).
        /// </summary>
        public static Settings Monitor => new Settings
        {
            curvature = 0.18f,
            cornerRadius = 0.08f,
            sharpness = 0.7f,
            scanlineStrength = 0f,
            scanlinePeriod = 3f,
            beamMin = 0.3f,
            beamMax = 0.45f,
            maskType = 0f,
            maskStrength = 0f,
            maskDark = 1f,
            maskLight = 1f,
            chromatic = 0.003f,
            bloom = 0.4f,
            vignette = 1.1f,
            noise = 0.03f,
            flicker = 0.02f,
            rollStrength = 0.07f,
            rollSpeed = 0.15f,
            jitter = 0.0008f,
            glare = 0.10f,
            brightness = 1.1f,
            opaque = 0f
        };

        internal const string ShaderResourcePath = "Shaders/CrtScreen";
        internal const string ShaderName = "TrashTD/UI/CRT";
        private static int instanceCount;

        private static readonly int CurvatureId = Shader.PropertyToID("_Curvature");
        private static readonly int CornerRadiusId = Shader.PropertyToID("_CornerRadius");
        private static readonly int SharpnessId = Shader.PropertyToID("_Sharpness");
        private static readonly int ScanlineStrengthId = Shader.PropertyToID("_ScanlineStrength");
        private static readonly int ScanlinePeriodId = Shader.PropertyToID("_ScanlinePeriod");
        private static readonly int BeamMinId = Shader.PropertyToID("_BeamMin");
        private static readonly int BeamMaxId = Shader.PropertyToID("_BeamMax");
        private static readonly int MaskTypeId = Shader.PropertyToID("_MaskType");
        private static readonly int MaskStrengthId = Shader.PropertyToID("_MaskStrength");
        private static readonly int MaskDarkId = Shader.PropertyToID("_MaskDark");
        private static readonly int MaskLightId = Shader.PropertyToID("_MaskLight");
        private static readonly int MaskScaleId = Shader.PropertyToID("_MaskScale");
        private static readonly int ChromaticId = Shader.PropertyToID("_Chromatic");
        private static readonly int BloomId = Shader.PropertyToID("_Bloom");
        private static readonly int VignetteId = Shader.PropertyToID("_Vignette");
        private static readonly int NoiseId = Shader.PropertyToID("_Noise");
        private static readonly int FlickerId = Shader.PropertyToID("_Flicker");
        private static readonly int RollStrengthId = Shader.PropertyToID("_RollStrength");
        private static readonly int RollSpeedId = Shader.PropertyToID("_RollSpeed");
        private static readonly int JitterId = Shader.PropertyToID("_Jitter");
        private static readonly int GlareId = Shader.PropertyToID("_Glare");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int OpaqueId = Shader.PropertyToID("_Opaque");
        private static readonly int TimeId = Shader.PropertyToID("_CrtTime");
        private static readonly int ResolutionId = Shader.PropertyToID("_CrtResolution");

        /// <summary>Creates a CRT material, or null if the shader is missing (logs a warning).</summary>
        internal static Material CreateMaterial(string materialName, Object context)
        {
            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null) shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"CRT shader '{ShaderName}' not found; showing the picture without CRT effects.", context);
                return null;
            }
            return new Material(shader) { name = materialName };
        }

        /// <summary>Writes the settings to a CRT material.</summary>
        internal static void ApplySettings(Material material, Settings settings)
        {
            if (material == null) return;
            material.SetFloat(CurvatureId, settings.curvature);
            material.SetFloat(CornerRadiusId, Mathf.Max(0.001f, settings.cornerRadius));
            material.SetFloat(SharpnessId, settings.sharpness);
            material.SetFloat(ScanlineStrengthId, settings.scanlineStrength);
            material.SetFloat(BeamMinId, settings.beamMin);
            material.SetFloat(BeamMaxId, settings.beamMax);
            material.SetFloat(MaskTypeId, settings.maskType);
            material.SetFloat(MaskStrengthId, settings.maskStrength);
            material.SetFloat(MaskDarkId, settings.maskDark);
            material.SetFloat(MaskLightId, settings.maskLight);
            material.SetFloat(ChromaticId, settings.chromatic);
            material.SetFloat(BloomId, settings.bloom);
            material.SetFloat(VignetteId, settings.vignette);
            material.SetFloat(NoiseId, settings.noise);
            material.SetFloat(FlickerId, settings.flicker);
            material.SetFloat(RollStrengthId, settings.rollStrength);
            material.SetFloat(RollSpeedId, settings.rollSpeed);
            material.SetFloat(JitterId, settings.jitter);
            material.SetFloat(GlareId, settings.glare);
            material.SetFloat(BrightnessId, settings.brightness);
            material.SetFloat(OpaqueId, settings.opaque);
        }

        /// <summary>
        /// Per-frame values: time, picture size, and the scanline / mask sizes that depend on it
        /// (about 270 scanlines and one phosphor per pixel at 1080p, scaled up on bigger screens).
        /// </summary>
        internal static void UpdateFrame(Material material, Settings settings, int width, int height)
        {
            if (material == null) return;
            material.SetFloat(TimeId, Time.unscaledTime);
            material.SetVector(ResolutionId, new Vector4(width, height, 0f, 0f));
            float period = settings.scanlinePeriod > 0f
                ? settings.scanlinePeriod
                : Mathf.Clamp(Mathf.Round(height / 270f), 2f, 8f);
            material.SetFloat(ScanlinePeriodId, period);
            material.SetFloat(MaskScaleId, Mathf.Max(1f, Mathf.Round(height / 1080f)));
        }

        /// <summary>A display point at uv shows the picture at Distort(uv). Mirrors Distort in the shader.</summary>
        internal static Vector2 Distort(Vector2 uv, float curvature)
        {
            Vector2 cc = uv - new Vector2(0.5f, 0.5f);
            return new Vector2(0.5f, 0.5f) + cc * (1f + curvature * Vector2.Dot(cc, cc));
        }

        /// <summary>Inside the curved, rounded screen. Mirrors ScreenMask in the shader.</summary>
        internal static bool IsOnScreen(Vector2 pictureUv, float cornerRadius)
        {
            if (pictureUv.x < 0f || pictureUv.x > 1f || pictureUv.y < 0f || pictureUv.y > 1f) return false;
            float r = Mathf.Max(cornerRadius, 0.001f);
            Vector2 q = new Vector2(Mathf.Abs(pictureUv.x - 0.5f), Mathf.Abs(pictureUv.y - 0.5f)) * 2f;
            Vector2 d = new Vector2(Mathf.Max(q.x - (1f - r), 0f), Mathf.Max(q.y - (1f - r), 0f));
            return d.magnitude / r < 1f;
        }

        private RectTransform rectTransform;
        private RawImage display;
        private Material material;
        private Camera renderCamera;
        private Canvas offscreenCanvas;
        private CanvasScaler offscreenScaler;
        private RenderTexture renderTexture;
        private RectTransform content;
        private Settings settings;

        /// <summary>Root of the UI shown on this screen (stretched over the whole picture).</summary>
        public RectTransform Content => content;

        /// <summary>Adds a CRT screen to <paramref name="displayRect"/>, which must not already have a Graphic.</summary>
        public static CrtScreen Create(RectTransform displayRect, Settings settings, bool interactive)
        {
            var screen = displayRect.gameObject.AddComponent<CrtScreen>();
            screen.Setup(settings, interactive);
            return screen;
        }

        private void Setup(Settings crtSettings, bool interactive)
        {
            rectTransform = (RectTransform)transform;
            settings = crtSettings;

            display = GetComponent<RawImage>();
            if (display == null) display = gameObject.AddComponent<RawImage>();
            display.raycastTarget = false;
            display.color = Color.white;

            material = CreateMaterial("CRT (" + name + ")", this);
            if (material != null)
            {
                display.material = material;
                ApplySettings(material, settings);
            }

            // Far away from the scene and from other CRT screens so no other camera sees this canvas
            int index = instanceCount++;
            var cameraObject = new GameObject("CrtCamera_" + name);
            cameraObject.transform.position = new Vector3(-10000f - index * 1000f, 0f, 0f);
            renderCamera = cameraObject.AddComponent<Camera>();
            renderCamera.orthographic = true;
            renderCamera.orthographicSize = 5f;
            renderCamera.clearFlags = CameraClearFlags.SolidColor;
            renderCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            renderCamera.nearClipPlane = 0.1f;
            renderCamera.farClipPlane = 100f;
            renderCamera.depth = -100f;
            renderCamera.allowHDR = false;
            renderCamera.allowMSAA = false;

            var canvasObject = new GameObject("CrtCanvas_" + name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            offscreenCanvas = canvasObject.GetComponent<Canvas>();
            offscreenCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            offscreenCanvas.worldCamera = renderCamera;
            offscreenCanvas.planeDistance = 10f;
            offscreenScaler = canvasObject.GetComponent<CanvasScaler>();
            offscreenScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            if (interactive)
            {
                canvasObject.AddComponent<CrtGraphicRaycaster>().Owner = this;
            }

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(canvasObject.transform, false);
            content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            SyncRenderTarget();
            SetRendering(isActiveAndEnabled);
        }

        public void SetSettings(Settings crtSettings)
        {
            settings = crtSettings;
            ApplySettings(material, settings);
        }

        private void OnEnable() => SetRendering(true);

        private void OnDisable() => SetRendering(false);

        private void SetRendering(bool active)
        {
            if (renderCamera != null) renderCamera.enabled = active;
            if (offscreenCanvas != null) offscreenCanvas.enabled = active;
        }

        private void LateUpdate()
        {
            SyncRenderTarget();
            if (renderTexture != null) UpdateFrame(material, settings, renderTexture.width, renderTexture.height);
        }

        private void OnDestroy()
        {
            if (renderCamera != null) Destroy(renderCamera.gameObject);
            if (offscreenCanvas != null) Destroy(offscreenCanvas.gameObject);
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }
            if (material != null) Destroy(material);
        }

        /// <summary>Keeps the RenderTexture at the display's pixel size and the offscreen UI at the same scale.</summary>
        private void SyncRenderTarget()
        {
            if (rectTransform == null || renderCamera == null) return;

            Canvas displayCanvas = display.canvas;
            float scale = displayCanvas != null ? displayCanvas.rootCanvas.scaleFactor : 1f;
            Vector2 pixelSize = rectTransform.rect.size * scale;
            int width = Mathf.Max(16, Mathf.RoundToInt(pixelSize.x));
            int height = Mathf.Max(16, Mathf.RoundToInt(pixelSize.y));

            if (offscreenScaler != null && !Mathf.Approximately(offscreenScaler.scaleFactor, scale))
            {
                offscreenScaler.scaleFactor = scale;
            }

            if (renderTexture != null && renderTexture.width == width && renderTexture.height == height) return;

            if (renderTexture != null)
            {
                renderCamera.targetTexture = null;
                renderTexture.Release();
                Destroy(renderTexture);
            }

            renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "CRT_" + name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            renderTexture.Create();
            renderCamera.targetTexture = renderTexture;
            display.texture = renderTexture;
            UpdateFrame(material, settings, width, height);
        }

        // ─────────────────────────────────────────────────────
        //  Pointer mapping (mirrors Distort / ScreenMask in CrtScreen.shader)
        // ─────────────────────────────────────────────────────

        public bool IsMappingPointer => isActiveAndEnabled;

        /// <summary>
        /// Converts a real screen position to the matching pixel on this screen's RenderTexture.
        /// False when the point is outside the display or on the bezel.
        /// </summary>
        public bool TryMapScreenPoint(Vector2 screenPosition, out Vector2 texturePosition)
        {
            texturePosition = default;
            if (renderTexture == null || display == null) return false;

            Canvas displayCanvas = display.canvas;
            Camera uiCamera = displayCanvas != null && displayCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? displayCanvas.worldCamera
                : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPosition, uiCamera, out Vector2 local))
                return false;

            Rect rect = rectTransform.rect;
            Vector2 uv = new Vector2((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
            Vector2 pictureUv = Distort(uv, settings.curvature);
            if (!IsOnScreen(pictureUv, settings.cornerRadius)) return false;

            texturePosition = new Vector2(pictureUv.x * renderTexture.width, pictureUv.y * renderTexture.height);
            return true;
        }
    }

    /// <summary>Something that shows a CRT picture and can map real screen points onto it.</summary>
    public interface ICrtPointerMapper
    {
        bool IsMappingPointer { get; }

        /// <summary>Real screen position → pixel in the CRT's picture. False on the bezel / outside.</summary>
        bool TryMapScreenPoint(Vector2 screenPosition, out Vector2 picturePosition);
    }

    /// <summary>
    /// Raycaster for a canvas drawn inside a CRT picture: bends the pointer through the CRT's curvature into
    /// picture pixels before the normal uGUI raycast, then restores it.
    /// </summary>
    public sealed class CrtGraphicRaycaster : GraphicRaycaster
    {
        public ICrtPointerMapper Owner { get; set; }

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            if (Owner == null || !Owner.IsMappingPointer) return;
            if (!Owner.TryMapScreenPoint(eventData.position, out Vector2 texturePosition)) return;

            Vector2 original = eventData.position;
            eventData.position = texturePosition;
            try
            {
                base.Raycast(eventData, resultAppendList);
            }
            finally
            {
                eventData.position = original;
            }
        }
    }
}
