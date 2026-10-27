using System;
using UnityEngine;
using UnityEngine.UI;
using TrashTD.Core.GameLoop;
using TrashTD.Data;

namespace TrashTD.UI
{
    /// <summary>
    /// Gameplay "surveillance feed" dressing over the stage view, in the style of the main menu's camera viewfinder.
    /// It covers the playable area between the HUD bars (top bar 90 px, deck bar 170 px at 1080):
    ///   - viewfinder corner brackets and a soft vignette;
    ///   - top-left: blinking REC + "CAM 03 // RAILYARD DRAIN";
    ///   - top-right: a live date/time stamp and the mission clock (T+ mm:ss);
    ///   - bottom-left: signal bars and the zoom level (from GameplayCameraController).
    /// <see cref="PlayInterference"/> (lives lost) tears the feed with noise bars, flashes the brackets red, drops the
    /// signal and swaps the caption to SIGNAL INTERFERENCE for a moment. Built by GameplayHUDUI as the first child
    /// of its canvas, so every other HUD element draws over it. Never takes clicks. Unscaled time.
    /// </summary>
    public sealed class SurveillanceOverlayUI : MonoBehaviour
    {
        private static readonly Color FeedGreen = new Color(0.45f, 0.95f, 0.62f, 0.85f);
        private static readonly Color LineGreen = new Color(0.22f, 0.91f, 0.50f, 0.55f);
        private static readonly Color DimGreen = new Color(0.45f, 0.78f, 0.56f, 0.6f);
        private static readonly Color RecRed = new Color(1f, 0.25f, 0.25f, 1f);
        private static readonly Color AlertRed = new Color(1f, 0.3f, 0.3f, 0.95f);

        private const float TopInset = 90f;      // top HUD bar
        private const float BottomInset = 170f;  // deck bar
        private const float Margin = 18f;
        private const int SignalBars = 5;
        private const float InterferenceDuration = 0.55f;

        private RectTransform area;
        private Image[] bracketBars;
        private Image recDot;
        private Text recLabel;
        private Text captionText;
        private Text clockText;
        private Text missionText;
        private Text zoomText;
        private Image[] signalBars;
        private RawImage[] tearBars;
        private string caption = string.Empty;
        private float interferenceTime = -1f;
        private float alertTextTime = -1f;

        public static SurveillanceOverlayUI Create(Transform canvasRoot)
        {
            var root = new GameObject("SurveillanceOverlay", typeof(RectTransform));
            root.transform.SetParent(canvasRoot, false);
            root.transform.SetAsFirstSibling();
            var overlay = root.AddComponent<SurveillanceOverlayUI>();
            overlay.Build();
            return overlay;
        }

        /// <summary>Feed caption, e.g. "CAM 03 // RAILYARD DRAIN" (stage number from the id, map name in caps).</summary>
        public void SetStage(StageData stage)
        {
            if (stage == null) return;
            int number = 0;
            if (!string.IsNullOrEmpty(stage.stageId))
            {
                int underscore = stage.stageId.LastIndexOf('_');
                if (underscore >= 0) int.TryParse(stage.stageId.Substring(underscore + 1), out number);
            }
            string mapName = string.IsNullOrEmpty(stage.mapName) ? "UNKNOWN SECTOR" : stage.mapName.ToUpperInvariant();
            caption = (number > 0 ? $"CAM {number:00}" : "CAM --") + $"  //  {mapName}";
            if (alertTextTime < 0f) captionText.text = caption;
        }

        /// <summary>Lives lost: tear the feed, flash the brackets red, drop the signal, show SIGNAL INTERFERENCE.</summary>
        public void PlayInterference()
        {
            interferenceTime = 0f;
            alertTextTime = 0f;
            captionText.text = "!! SIGNAL INTERFERENCE !!";
            captionText.color = AlertRed;
        }

        private void Build()
        {
            var rootRect = (RectTransform)transform;
            Stretch(rootRect);

            area = CreateRect(transform, "FeedArea");
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(0f, BottomInset);
            area.offsetMax = new Vector2(0f, -TopInset);

            // Soft vignette
            Image vignette = CreateImage(area, "Vignette", new Color(0f, 0f, 0f, 0.35f));
            vignette.sprite = CreateVignetteSprite();
            Stretch(vignette.rectTransform);

            // Noise tear bars (hidden until interference)
            tearBars = new RawImage[6];
            for (int i = 0; i < tearBars.Length; i++)
            {
                var bar = new GameObject("TearBar", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                bar.transform.SetParent(area, false);
                bar.texture = GetNoiseTexture();
                bar.raycastTarget = false;
                bar.rectTransform.anchorMin = new Vector2(0f, 0f);
                bar.rectTransform.anchorMax = new Vector2(1f, 0f);
                bar.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                bar.gameObject.SetActive(false);
                tearBars[i] = bar;
            }

            // Viewfinder brackets, inset from the playable area
            var frame = CreateRect(area, "Viewfinder");
            Stretch(frame);
            frame.offsetMin = new Vector2(Margin, Margin);
            frame.offsetMax = new Vector2(-Margin, -Margin);
            bracketBars = AddCornerBrackets(frame, 54f, 3f, LineGreen);

            // Top-left: REC + feed caption
            recDot = CreateImage(frame, "RecDot", RecRed);
            PlaceCorner(recDot.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -22f), new Vector2(12f, 12f));
            recLabel = CreateText(frame, "REC", 14, TextAnchor.MiddleLeft, RecRed);
            PlaceCorner(recLabel.rectTransform, new Vector2(0f, 1f), new Vector2(42f, -16f), new Vector2(60f, 24f));
            captionText = CreateText(frame, string.Empty, 12, TextAnchor.MiddleLeft, FeedGreen);
            PlaceCorner(captionText.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -44f), new Vector2(700f, 20f));

            // Top-right: date/time stamp + mission clock
            clockText = CreateText(frame, string.Empty, 12, TextAnchor.MiddleRight, FeedGreen);
            PlaceCorner(clockText.rectTransform, new Vector2(1f, 1f), new Vector2(-22f, -16f), new Vector2(420f, 20f));
            missionText = CreateText(frame, string.Empty, 12, TextAnchor.MiddleRight, DimGreen);
            PlaceCorner(missionText.rectTransform, new Vector2(1f, 1f), new Vector2(-22f, -40f), new Vector2(420f, 20f));

            // Bottom-left: signal bars + zoom
            signalBars = new Image[SignalBars];
            Text signalLabel = CreateText(frame, "SIGNAL", 10, TextAnchor.MiddleLeft, DimGreen);
            PlaceCorner(signalLabel.rectTransform, new Vector2(0f, 0f), new Vector2(22f, 40f), new Vector2(80f, 16f));
            for (int i = 0; i < SignalBars; i++)
            {
                Image bar = CreateImage(frame, "SignalBar", FeedGreen);
                bar.rectTransform.anchorMin = bar.rectTransform.anchorMax = Vector2.zero;
                bar.rectTransform.pivot = new Vector2(0f, 0f);
                float height = 4f + i * 3f;
                bar.rectTransform.sizeDelta = new Vector2(5f, height);
                bar.rectTransform.anchoredPosition = new Vector2(92f + i * 8f, 33f);
                signalBars[i] = bar;
            }
            zoomText = CreateText(frame, string.Empty, 10, TextAnchor.MiddleLeft, DimGreen);
            PlaceCorner(zoomText.rectTransform, new Vector2(0f, 0f), new Vector2(22f, 16f), new Vector2(360f, 16f));
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float time = Time.unscaledTime;

            // REC blinks once a second
            bool recOn = Mathf.Repeat(time, 1f) < 0.6f;
            recDot.color = new Color(RecRed.r, RecRed.g, RecRed.b, recOn ? 1f : 0.2f);

            DateTime now = DateTime.Now;
            clockText.text = now.ToString("yyyy-MM-dd  HH:mm:ss");
            GameManager manager = GameManager.Instance;
            int mission = manager != null ? Mathf.FloorToInt(manager.ElapsedTime) : 0;
            missionText.text = $"T+ {mission / 3600:00}:{mission / 60 % 60:00}:{mission % 60:00}";

            GameplayCameraController cameraController = GameplayCameraController.Instance;
            float zoom = cameraController != null ? cameraController.Zoom : 1f;
            zoomText.text = $"ZOOM {zoom:0.00}x   [SCROLL]";

            UpdateInterference(dt);
        }

        private void UpdateInterference(float dt)
        {
            float signal = 1f;
            float redFlash = 0f;

            if (interferenceTime >= 0f)
            {
                interferenceTime += dt;
                float k = Mathf.Clamp01(interferenceTime / InterferenceDuration);
                redFlash = 1f - k;
                signal = Mathf.Lerp(0.2f, 1f, k);

                float areaHeight = area.rect.height;
                for (int i = 0; i < tearBars.Length; i++)
                {
                    RawImage bar = tearBars[i];
                    bool show = k < 1f && UnityEngine.Random.value < 0.75f;
                    bar.gameObject.SetActive(show);
                    if (!show) continue;
                    float height = UnityEngine.Random.Range(6f, 34f);
                    bar.rectTransform.sizeDelta = new Vector2(0f, height);
                    bar.rectTransform.anchoredPosition = new Vector2(UnityEngine.Random.Range(-30f, 30f), UnityEngine.Random.Range(0f, areaHeight));
                    bar.uvRect = new Rect(UnityEngine.Random.value, UnityEngine.Random.value, 12f, height / 64f);
                    bar.color = new Color(1f, 1f, 1f, 0.35f * (1f - k));
                }

                if (k >= 1f) interferenceTime = -1f;
            }

            if (alertTextTime >= 0f)
            {
                alertTextTime += dt;
                captionText.enabled = Mathf.Repeat(alertTextTime * 6f, 1f) < 0.7f;
                if (alertTextTime > 1.3f)
                {
                    alertTextTime = -1f;
                    captionText.enabled = true;
                    captionText.text = caption;
                    captionText.color = FeedGreen;
                }
            }

            // Brackets flash red, signal bars drop and recover
            Color bracketColor = Color.Lerp(LineGreen, AlertRed, redFlash);
            foreach (Image bar in bracketBars) bar.color = bracketColor;
            int lit = Mathf.Clamp(Mathf.RoundToInt(signal * SignalBars), 1, SignalBars);
            for (int i = 0; i < signalBars.Length; i++)
            {
                Color on = redFlash > 0.3f ? AlertRed : FeedGreen;
                signalBars[i].color = i < lit ? on : new Color(1f, 1f, 1f, 0.12f);
            }
        }

        // ─────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────

        private static Image[] AddCornerBrackets(RectTransform target, float length, float thickness, Color color)
        {
            var bars = new Image[8];
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2, i / 2);
                for (int j = 0; j < 2; j++)
                {
                    Image bar = CreateImage(target, "CornerBracket", color);
                    RectTransform rect = bar.rectTransform;
                    rect.anchorMin = corner;
                    rect.anchorMax = corner;
                    rect.pivot = corner;
                    rect.sizeDelta = j == 0 ? new Vector2(length, thickness) : new Vector2(thickness, length);
                    rect.anchoredPosition = Vector2.zero;
                    bars[i * 2 + j] = bar;
                }
            }
            return bars;
        }

        private static void PlaceCorner(RectTransform rect, Vector2 corner, Vector2 position, Vector2 size)
        {
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static Sprite vignetteSprite;

        /// <summary>Transparent centre fading to opaque corners (tinted by the image colour).</summary>
        private static Sprite CreateVignetteSprite()
        {
            if (vignetteSprite != null) return vignetteSprite;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 0.8f);
                    float a = Mathf.Clamp01((d - 0.55f) / 0.6f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            vignetteSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            return vignetteSprite;
        }

        private static Texture2D noiseTexture;

        private static Texture2D GetNoiseTexture()
        {
            if (noiseTexture != null) return noiseTexture;
            const int size = 64;
            noiseTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            var pixels = new Color32[size * size];
            var random = new System.Random(4242);
            for (int i = 0; i < pixels.Length; i++)
            {
                byte v = (byte)random.Next(60, 256);
                pixels[i] = new Color32(v, v, v, (byte)(random.Next(0, 2) == 0 ? 255 : 0));
            }
            noiseTexture.SetPixels32(pixels);
            noiseTexture.Apply();
            return noiseTexture;
        }

        private static Text CreateText(Transform parent, string content, int size, TextAnchor alignment, Color color)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.text = content;
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            rectObject.transform.SetParent(parent, false);
            return rectObject.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
