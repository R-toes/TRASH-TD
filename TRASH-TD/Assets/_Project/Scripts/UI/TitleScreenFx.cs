using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TrashTD.UI
{
    /// <summary>
    /// Effects for the main menu's title screen, built and driven by MainMenuController. Everything runs on
    /// unscaled time.
    ///   - Logo: a pulsing green glow behind the emblem and an occasional short glitch of the title (red / cyan
    ///     ghosts, jitter, flicker).
    ///   - Intro (once per game session): the logo with a blinking PRESS ANY KEY, before the menu rows appear.
    ///   - Menu rows: a pair of slide bars that glide to whichever row is highlighted.
    /// </summary>
    public sealed class TitleScreenFx : MonoBehaviour
    {
        private static readonly Color Accent = new Color(0.18f, 0.82f, 0.45f, 1f);
        private static readonly Color AccentBright = new Color(0.32f, 1.00f, 0.64f, 1f);

        private const float GlitchMinGap = 6.5f;
        private const float GlitchMaxGap = 10f;
        private const float GlitchLength = 0.28f;
        private const float GlitchStep = 0.045f;

        private static bool introSeen;

        /// <summary>True once the "press any key" intro has played (or was skipped) this session.</summary>
        public static bool IntroSeen => introSeen;

        public static void MarkIntroSeen() => introSeen = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => introSeen = false; // Play Mode may skip the domain reload

        // Logo effects
        private bool logoBuilt;
        private RectTransform titleRect;
        private Image titleImage;
        private Image glow;
        private Image ghostRed;
        private Image ghostCyan;
        private float glitchGap;
        private float glitchLeft;
        private float glitchStepLeft;
        private Vector2 titleBase;

        // Slide bars
        private RectTransform barLeft;
        private RectTransform barRight;
        private Image barLeftImage;
        private Image barRightImage;
        private float barHalfWidth;
        private float barY;
        private float barHeight;
        private float barAlpha;

        // Intro
        private Text promptText;
        private bool promptVisible;

        private static Sprite glowSprite;

        // ─────────────────────────────────────────────────────
        //  Logo: glow, glitch
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Adds the glow (behind the emblem) and the two glitch ghosts (just behind the title) to the logo, once its
        /// emblem and title exist.
        /// </summary>
        public void BuildLogoEffects(RectTransform logoRoot, RectTransform title, Image titleGraphic, float emblemSize)
        {
            if (logoRoot == null || title == null || titleGraphic == null) return;

            titleRect = title;
            titleImage = titleGraphic;
            titleBase = title.anchoredPosition;

            glow = NewImage(logoRoot, "EmblemGlow", GetGlowSprite(), Color.clear);
            Centre(glow.rectTransform, new Vector2(emblemSize * 1.35f, emblemSize * 1.35f));
            glow.transform.SetAsFirstSibling();

            // Ghosts sit just behind the title, with its exact geometry
            ghostRed = NewGhost(logoRoot, "TitleGhostRed", title);
            ghostCyan = NewGhost(logoRoot, "TitleGhostCyan", title);
            int titleIndex = title.GetSiblingIndex();
            ghostRed.transform.SetSiblingIndex(titleIndex);
            ghostCyan.transform.SetSiblingIndex(titleIndex + 1);

            glitchGap = UnityEngine.Random.Range(GlitchMinGap, GlitchMaxGap);
            logoBuilt = true;
        }

        private static void Centre(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
        }

        private static Image NewImage(Transform parent, string objectName, Sprite sprite, Color color)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Image NewGhost(Transform parent, string objectName, RectTransform like)
        {
            Image ghost = NewImage(parent, objectName, null, Color.clear);
            RectTransform rt = ghost.rectTransform;
            rt.anchorMin = like.anchorMin;
            rt.anchorMax = like.anchorMax;
            rt.pivot = like.pivot;
            rt.sizeDelta = like.sizeDelta;
            rt.anchoredPosition = like.anchoredPosition;
            ghost.preserveAspect = true;
            return ghost;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (logoBuilt) UpdateLogo(dt, Time.unscaledTime);
            UpdateSlideBarVisuals(dt);
            UpdatePrompt();
        }

        private void UpdateLogo(float dt, float time)
        {
            // Glow: a slow breath
            if (glow != null)
            {
                float breath = 0.5f + 0.5f * Mathf.Sin(time * 1.6f);
                glow.color = new Color(Accent.r, Accent.g, Accent.b, 0.08f + 0.13f * breath);
                glow.rectTransform.localScale = Vector3.one * (1f + 0.03f * breath);
            }

            UpdateGlitch(dt);
        }

        private void UpdateGlitch(float dt)
        {
            if (titleImage == null) return;

            if (glitchLeft <= 0f)
            {
                glitchGap -= dt;
                if (glitchGap <= 0f)
                {
                    glitchLeft = GlitchLength;
                    glitchStepLeft = 0f;
                }
                else
                {
                    SetGlitch(0f, 0f, 1f, 0f);
                    return;
                }
            }

            glitchLeft -= dt;
            if (glitchLeft <= 0f)
            {
                glitchGap = UnityEngine.Random.Range(GlitchMinGap, GlitchMaxGap);
                SetGlitch(0f, 0f, 1f, 0f);
                return;
            }

            glitchStepLeft -= dt;
            if (glitchStepLeft <= 0f)
            {
                glitchStepLeft = GlitchStep;
                float split = UnityEngine.Random.Range(3f, 8f);
                float jitter = UnityEngine.Random.Range(-5f, 5f);
                float alpha = UnityEngine.Random.value < 0.25f ? 0.5f : 1f; // the odd dropped frame
                SetGlitch(jitter, split, alpha, 0.6f);
            }
        }

        private void SetGlitch(float jitterX, float split, float titleAlpha, float ghostAlpha)
        {
            titleRect.anchoredPosition = titleBase + new Vector2(jitterX, 0f);
            titleImage.color = new Color(1f, 1f, 1f, titleAlpha);

            Sprite sprite = titleImage.sprite;
            ghostRed.sprite = sprite;
            ghostCyan.sprite = sprite;
            ghostRed.color = new Color(1f, 0.25f, 0.25f, ghostAlpha);
            ghostCyan.color = new Color(0.25f, 0.92f, 1f, ghostAlpha);
            ghostRed.rectTransform.anchoredPosition = titleBase + new Vector2(jitterX - split, UnityEngine.Random.Range(-1.5f, 1.5f) * (ghostAlpha > 0f ? 1f : 0f));
            ghostCyan.rectTransform.anchoredPosition = titleBase + new Vector2(jitterX + split, UnityEngine.Random.Range(-1.5f, 1.5f) * (ghostAlpha > 0f ? 1f : 0f));
        }

        // ─────────────────────────────────────────────────────
        //  Slide bars beside the menu rows
        // ─────────────────────────────────────────────────────

        /// <summary>Two thin vertical bars flanking the menu column, <paramref name="columnHalfWidth"/> from its centre.</summary>
        public void BuildSlideBars(Transform panel, float columnHalfWidth)
        {
            barHalfWidth = columnHalfWidth + 14f;
            barLeftImage = NewImage(panel, "MenuSlideBarLeft", null, Color.clear);
            barRightImage = NewImage(panel, "MenuSlideBarRight", null, Color.clear);
            barLeft = barLeftImage.rectTransform;
            barRight = barRightImage.rectTransform;
            foreach (RectTransform bar in new[] { barLeft, barRight })
            {
                bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 1f);
                bar.pivot = new Vector2(0.5f, 1f);
            }
        }

        private RectTransform barTarget;
        private float barTargetAmount;

        /// <summary>The row the bars should rest beside (null or ~0 amount hides them). Call every frame.</summary>
        public void SetSlideTarget(RectTransform row, float amount)
        {
            barTarget = row;
            barTargetAmount = row != null ? amount : 0f;
        }

        private void UpdateSlideBarVisuals(float dt)
        {
            if (barLeft == null) return;

            bool show = barTarget != null && barTargetAmount > 0.15f;
            if (show)
            {
                float targetY = barTarget.anchoredPosition.y;
                float targetH = barTarget.sizeDelta.y;
                if (barAlpha < 0.02f)
                {
                    // Appearing: start at the row instead of gliding in from wherever the bars last were
                    barY = targetY;
                    barHeight = targetH;
                }
                float k = 1f - Mathf.Exp(-20f * dt);
                barY = Mathf.Lerp(barY, targetY, k);
                barHeight = Mathf.Lerp(barHeight, targetH, k);
            }

            barAlpha = Mathf.MoveTowards(barAlpha, show ? 1f : 0f, dt * 8f);
            Color color = new Color(AccentBright.r, AccentBright.g, AccentBright.b, barAlpha);
            barLeftImage.color = color;
            barRightImage.color = color;
            barLeft.anchoredPosition = new Vector2(-barHalfWidth, barY);
            barRight.anchoredPosition = new Vector2(barHalfWidth, barY);
            barLeft.sizeDelta = new Vector2(4f, barHeight);
            barRight.sizeDelta = new Vector2(4f, barHeight);
        }

        // ─────────────────────────────────────────────────────
        //  Intro: "press any key"
        // ─────────────────────────────────────────────────────

        /// <summary>True on the frame any key, mouse button or pad button goes down.</summary>
        public static bool AnyInputPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) return true;

            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) return true;

            Gamepad pad = Gamepad.current;
            return pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame);
        }

        /// <summary>Shows a blinking PRESS ANY KEY, centred under the logo at <paramref name="top"/> canvas units from the top.</summary>
        public void ShowPrompt(Transform panel, float top)
        {
            if (promptText == null)
            {
                var go = new GameObject("PressAnyKey", typeof(RectTransform), typeof(Text));
                go.transform.SetParent(panel, false);
                promptText = go.GetComponent<Text>();
                promptText.font = UIFontHelper.GetPixelFont();
                promptText.fontSize = 40;
                promptText.alignment = TextAnchor.MiddleCenter;
                promptText.horizontalOverflow = HorizontalWrapMode.Overflow;
                promptText.verticalOverflow = VerticalWrapMode.Overflow;
                promptText.raycastTarget = false;
                promptText.text = "- PRESS ANY KEY -";
                var rect = promptText.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(1000f, 64f);
                rect.anchoredPosition = new Vector2(0f, -top);
            }

            promptVisible = true;
            promptText.gameObject.SetActive(true);
        }

        public void HidePrompt()
        {
            promptVisible = false;
            if (promptText != null) promptText.gameObject.SetActive(false);
        }

        private void UpdatePrompt()
        {
            if (!promptVisible || promptText == null) return;

            // Slow, soft blink rather than an on/off flash
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
            promptText.color = new Color(AccentBright.r, AccentBright.g, AccentBright.b, Mathf.Lerp(0.25f, 1f, pulse));
        }

        // ─────────────────────────────────────────────────────
        //  Procedural sprites
        // ─────────────────────────────────────────────────────

        private static Sprite GetGlowSprite()
        {
            if (glowSprite != null) return glowSprite;

            const int size = 96;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            float centre = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre)) / centre;
                    float alpha = d >= 1f ? 0f : Mathf.Pow(1f - d, 1.8f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            glowSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            return glowSprite;
        }

    }
}
