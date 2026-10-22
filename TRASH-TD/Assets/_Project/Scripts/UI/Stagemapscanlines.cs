using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace TrashTD.UI
{
    /// <summary>
    /// Animated CRT overlay for the Pip-Boy stage map. Put it on a RawImage that stretches over the map art
    /// (same object as before). It builds its extra layers by itself at runtime:
    ///   - scanlines: one dark line pattern per source-art pixel row (matches the CRT preview), slowly crawling + flickering
    ///   - rolling refresh band: a soft bright bar that sweeps top-to-bottom, like the bar in the preview but moving
    ///   - vignette: darkened corners
    ///   - brightness flicker: very faint whole-screen shimmer
    /// All timing uses unscaled time, so it keeps animating while the menu is paused.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public class StageMapScanlines : MonoBehaviour
    {
        [Header("Scanlines")]
        [Tooltip("Number of pixel rows in the source art (270 for the 480x270 map).")]
        [SerializeField] private int sourceRows = 270;
        [FormerlySerializedAs("strength")]
        [SerializeField, Range(0f, 1.5f)] private float scanlineStrength = 1f;
        [Tooltip("How fast the scanlines crawl, in source-pixel rows per second. 0 = static.")]
        [SerializeField] private float crawlRowsPerSecond = 0.3f;
        [Tooltip("How much the scanline intensity wavers (0 = steady).")]
        [SerializeField, Range(0f, 0.5f)] private float scanlineFlicker = 0.10f;

        [Header("Rolling Refresh Band")]
        [SerializeField] private bool rollingBand = true;
        [Tooltip("Seconds for the band to sweep from the top of the map to the bottom.")]
        [SerializeField] private float bandPeriod = 7f;
        [Tooltip("Band height as a fraction of the map height.")]
        [SerializeField, Range(0.05f, 1f)] private float bandHeight = 0.32f;
        [SerializeField, Range(0f, 0.3f)] private float bandStrength = 0.08f;
        [SerializeField] private Color bandTint = new Color(0.55f, 1f, 0.55f, 1f);

        [Header("Screen")]
        [SerializeField, Range(0f, 1f)] private float vignetteStrength = 0.42f;
        [Tooltip("Faint whole-screen brightness shimmer (0 = off).")]
        [SerializeField, Range(0f, 0.2f)] private float brightnessFlicker = 0.035f;
        [SerializeField] private float flickerSpeed = 9f;

        /// <summary>
        /// Source-art pixel rows the scanlines cover (4 screen rows each). The default suits a full-screen map;
        /// smaller overlays should use about their height / 4 to avoid moire.
        /// </summary>
        public int SourceRows
        {
            get => sourceRows;
            set => sourceRows = Mathf.Max(1, value);
        }

        private RawImage scanlines;
        private RawImage flickerLayer;
        private RawImage vignetteLayer;
        private RawImage bandLayer;
        private RectTransform bandRect;
        private Texture2D scanTex;
        private Texture2D vignetteTex;
        private Texture2D bandTex;

        private void Awake()
        {
            scanlines = GetComponent<RawImage>();
            scanlines.raycastTarget = false;

            BuildTextures();
            scanlines.texture = scanTex;
            scanlines.uvRect = new Rect(0f, 0f, 1f, sourceRows);

            BuildLayers();
        }

        private void Update()
        {
            float t = Time.unscaledTime;

            // scanlines: slow crawl + flicker (Perlin noise = smooth, not random static)
            scanlines.uvRect = new Rect(0f, (t * crawlRowsPerSecond) % 1f, 1f, sourceRows);
            float n = Mathf.PerlinNoise(t * flickerSpeed, 0.37f);
            scanlines.color = new Color(1f, 1f, 1f, 1f - scanlineFlicker * n);

            // faint brightness shimmer
            if (brightnessFlicker > 0f)
            {
                float f = Mathf.PerlinNoise(t * flickerSpeed * 0.7f, 4.2f);
                flickerLayer.color = new Color(0f, 0f, 0f, brightnessFlicker * f);
            }

            // rolling refresh band: top -> bottom, then loops
            if (rollingBand && bandPeriod > 0.01f)
            {
                float p = (t % bandPeriod) / bandPeriod;
                float centre = Mathf.Lerp(1f + bandHeight * 0.5f, -bandHeight * 0.5f, p);
                bandRect.anchorMin = new Vector2(0f, centre - bandHeight * 0.5f);
                bandRect.anchorMax = new Vector2(1f, centre + bandHeight * 0.5f);
            }
        }

        private void OnDestroy()
        {
            if (scanTex != null) Destroy(scanTex);
            if (vignetteTex != null) Destroy(vignetteTex);
            if (bandTex != null) Destroy(bandTex);
        }

        // ----- setup -----

        private void BuildTextures()
        {
            // scanlines: 4 rows per source pixel (bright, slightly dim, dim, dark) - alpha of black
            float[] a = { 0f, 0.03f, 0.12f, 0.30f };
            scanTex = NewTexture(1, 4, TextureWrapMode.Repeat, FilterMode.Point);
            for (int i = 0; i < 4; i++)
                scanTex.SetPixel(0, i, new Color(0f, 0f, 0f, Mathf.Clamp01(a[i] * scanlineStrength)));
            scanTex.Apply();

            // vignette: radial darkening toward the corners
            const int size = 64;
            vignetteTex = NewTexture(size, size, TextureWrapMode.Clamp, FilterMode.Bilinear);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = ((x + 0.5f) / size) * 2f - 1f;
                    float v = ((y + 0.5f) / size) * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float alpha = vignetteStrength * Mathf.Pow(Mathf.Clamp01(r - 0.4f), 1.5f);
                    vignetteTex.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
                }
            }
            vignetteTex.Apply();

            // rolling band: soft vertical gaussian
            const int rows = 128;
            bandTex = NewTexture(1, rows, TextureWrapMode.Clamp, FilterMode.Bilinear);
            for (int i = 0; i < rows; i++)
            {
                float t = ((i + 0.5f) / rows) * 2f - 1f;
                bandTex.SetPixel(0, i, new Color(1f, 1f, 1f, Mathf.Exp(-t * t * 3f)));
            }
            bandTex.Apply();
        }

        private void BuildLayers()
        {
            // container clips the moving band to the map's rect
            var container = new GameObject("CRT_Layers", typeof(RectTransform), typeof(RectMask2D));
            var crt = (RectTransform)container.transform;
            crt.SetParent(transform, false);
            Stretch(crt);

            flickerLayer = CreateLayer("Flicker", crt, Texture2D.whiteTexture);
            flickerLayer.color = new Color(0f, 0f, 0f, 0f);
            flickerLayer.gameObject.SetActive(brightnessFlicker > 0f);

            vignetteLayer = CreateLayer("Vignette", crt, vignetteTex);
            vignetteLayer.gameObject.SetActive(vignetteStrength > 0f);

            bandLayer = CreateLayer("RefreshBand", crt, bandTex);
            bandLayer.color = new Color(bandTint.r, bandTint.g, bandTint.b, bandStrength);
            bandRect = bandLayer.rectTransform;
            bandLayer.gameObject.SetActive(rollingBand);
        }

        private static RawImage CreateLayer(string layerName, RectTransform parent, Texture texture)
        {
            var go = new GameObject(layerName, typeof(RectTransform), typeof(RawImage));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt);

            var img = go.GetComponent<RawImage>();
            img.texture = texture;
            img.raycastTarget = false;
            return img;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Texture2D NewTexture(int w, int h, TextureWrapMode wrap, FilterMode filter)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = wrap,
                filterMode = filter,
                hideFlags = HideFlags.HideAndDontSave
            };
        }
    }
}