using System;
using UnityEngine;
using UnityEngine.UI;
using TrashTD.UI;

namespace TrashTD.Audio
{
    /// <summary>
    /// Runtime-built settings panel shared by menu and gameplay: Audio (music, SFX) and Graphics
    /// (CRT style, brightness; see GameGraphicsSettings). The class keeps its original name because
    /// several screens create it. Styled as the green terminal used by the main menu and stage selector.
    /// </summary>
    public sealed class AudioSettingsUI : MonoBehaviour
    {
        // Terminal palette (matches MainMenuController)
        private static readonly Color ButtonColor = new Color(0.10f, 0.13f, 0.18f, 1f);
        private static readonly Color AccentColor = new Color(0.18f, 0.82f, 0.45f, 1f);
        private static readonly Color AccentBright = new Color(0.32f, 1.00f, 0.64f, 1f);
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.62f);
        private static readonly Color CardColor = new Color(0.016f, 0.042f, 0.028f, 0.98f);
        private static readonly Color HeaderColor = new Color(0.030f, 0.090f, 0.055f, 1f);
        private static readonly Color TerminalButton = new Color(0.035f, 0.090f, 0.060f, 1f);
        private static readonly Color TrackColor = new Color(0.020f, 0.060f, 0.040f, 1f);
        private static readonly Color LineColor = new Color(0.22f, 0.91f, 0.50f, 0.45f);
        private static readonly Color LabelColor = new Color(0.45f, 0.78f, 0.56f, 1f);
        private static readonly Color HintColor = new Color(0.45f, 0.78f, 0.56f, 0.7f);
        private static readonly Color SelectedLabel = new Color(0.02f, 0.10f, 0.05f, 1f);

        // Layout, in card pixels from the top-left. PressStart2P glyphs are as wide as the font size,
        // so every text box is sized from its longest string (e.g. "BRIGHTNESS" at 14 px = 140 px).
        private const float CardWidth = 760f;
        private const float CardHeight = 500f;
        private const float HeaderHeight = 56f;
        private const float Padding = 36f;
        private const float LabelWidth = 220f;
        private const float ControlX = Padding + LabelWidth + 14f;      // 270
        private const float ValueWidth = 72f;
        private const float ControlWidth = CardWidth - Padding - ValueWidth - 12f - ControlX; // slider width
        private const float SegmentWidth = (CardWidth - Padding - ControlX - 2f * 12f) / 3f;
        private const int LabelSize = 14;
        private const float ShowDuration = 0.18f;

        private GameObject panel;
        private RectTransform card;
        private CanvasGroup panelGroup;
        private float showTime = ShowDuration;
        private Slider musicSlider;
        private Slider sfxSlider;
        private Slider brightnessSlider;
        private Text musicValueText;
        private Text sfxValueText;
        private Text brightnessValueText;
        private Button fullTvButton;
        private Button classicButton;
        private Button offButton;
        private Text crtStyleHint;
        public Button SettingsButton { get; private set; }
        public bool IsOpen => panel != null && panel.activeSelf;
        public void Close() => Hide();

        public static AudioSettingsUI Create(
            Transform canvasRoot,
            Transform buttonParent,
            Action<Button> styleButton,
            Action<RectTransform> positionButton)
        {
            if (canvasRoot == null || buttonParent == null) return null;

            GameObject root = new GameObject("AudioSettingsUI", typeof(RectTransform));
            root.transform.SetParent(canvasRoot, false);
            var settings = root.AddComponent<AudioSettingsUI>();
            settings.Build(buttonParent, styleButton, positionButton);
            return settings;
        }

        private void Build(Transform buttonParent, Action<Button> styleButton, Action<RectTransform> positionButton)
        {
            SettingsButton = CreateButton(buttonParent, "AudioSettingsButton", "SETTINGS", new Vector2(200f, 48f));
            styleButton?.Invoke(SettingsButton);
            positionButton?.Invoke(SettingsButton.GetComponent<RectTransform>());
            SettingsButton.onClick.AddListener(Show);

            // Full-screen dimmer (blocks clicks to whatever is underneath) holding the card
            Transform canvasRoot = transform.parent;
            panel = new GameObject("AudioSettingsPanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            panel.transform.SetParent(canvasRoot, false);
            SetStretch(panel.GetComponent<RectTransform>());
            panel.GetComponent<Image>().color = DimColor;
            panelGroup = panel.GetComponent<CanvasGroup>();

            Image cardImage = CreateImage(panel.transform, "SettingsCard", CardColor);
            card = cardImage.rectTransform;
            SetPosition(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardWidth, CardHeight));
            cardImage.raycastTarget = true;
            Outline cardOutline = cardImage.gameObject.AddComponent<Outline>();
            cardOutline.effectColor = LineColor;
            cardOutline.effectDistance = new Vector2(2f, -2f);
            AddCornerBrackets(card, 24f, 3f, AccentBright, -7f);

            // Header bar
            Image header = CreateImage(card, "Header", HeaderColor);
            PlaceTopLeft(header.rectTransform, 0f, 0f, CardWidth, HeaderHeight);
            Image headerLine = CreateImage(card, "HeaderLine", LineColor);
            PlaceTopLeft(headerLine.rectTransform, 0f, HeaderHeight, CardWidth, 2f);

            Text title = CreateText(header.transform, "SETTINGS", 20, TextAnchor.MiddleLeft, AccentBright);
            PlaceTopLeft(title.rectTransform, Padding - 8f, 0f, 400f, HeaderHeight);
            Text escHint = CreateText(header.transform, "[ESC] CLOSE", 11, TextAnchor.MiddleRight, HintColor);
            PlaceTopLeft(escHint.rectTransform, CardWidth - Padding + 8f - 300f, 0f, 300f, HeaderHeight);

            // ── Audio ──
            CreateSectionHeader("AUDIO", 88f);
            float musicValue = AudioManager.Instance != null ? AudioManager.Instance.MusicVolume : 0.45f;
            musicSlider = CreateSliderRow("MUSIC", 130f, 0f, 1f, musicValue, out musicValueText);
            float sfxValue = AudioManager.Instance != null ? AudioManager.Instance.SfxVolume : 0.45f;
            sfxSlider = CreateSliderRow("SFX", 176f, 0f, 1f, sfxValue, out sfxValueText);

            // ── Graphics ──
            CreateSectionHeader("GRAPHICS", 228f);
            CreateRowLabel("CRT STYLE", 272f);
            fullTvButton = CreateStyleButton("CrtFullTvButton", "FULL TV", 0, CrtStyle.FullTv);
            classicButton = CreateStyleButton("CrtClassicButton", "CLASSIC", 1, CrtStyle.Classic);
            offButton = CreateStyleButton("CrtOffButton", "OFF", 2, CrtStyle.Off);

            crtStyleHint = CreateText(card, string.Empty, 11, TextAnchor.UpperLeft, HintColor);
            crtStyleHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            crtStyleHint.verticalOverflow = VerticalWrapMode.Overflow;
            crtStyleHint.lineSpacing = 1.4f;
            PlaceTopLeft(crtStyleHint.rectTransform, ControlX, 302f, CardWidth - Padding - ControlX, 40f);

            brightnessSlider = CreateSliderRow("BRIGHTNESS", 372f,
                GameGraphicsSettings.MinBrightness, GameGraphicsSettings.MaxBrightness, GameGraphicsSettings.Brightness,
                out brightnessValueText);

            // Footer
            Image footerLine = CreateImage(card, "FooterLine", new Color(LineColor.r, LineColor.g, LineColor.b, 0.25f));
            PlaceTopLeft(footerLine.rectTransform, Padding, 412f, CardWidth - 2f * Padding, 2f);
            Button closeButton = CreateTerminalButton(card, "CloseButton", "CLOSE", 16);
            PlaceTopLeft(closeButton.GetComponent<RectTransform>(), (CardWidth - 220f) * 0.5f, 432f, 220f, 48f);
            AddCornerBrackets(closeButton.GetComponent<RectTransform>(), 10f, 2f, LineColor);
            closeButton.onClick.AddListener(Hide);
            AttachFeedback(closeButton, SfxId.UiBack);

            musicSlider.onValueChanged.AddListener(value =>
            {
                AudioManager.Instance?.SetMusicVolume(value);
                RefreshValueTexts();
            });
            sfxSlider.onValueChanged.AddListener(value =>
            {
                AudioManager.Instance?.SetSfxVolume(value);
                RefreshValueTexts();
            });
            brightnessSlider.onValueChanged.AddListener(value =>
            {
                GameGraphicsSettings.Brightness = value;
                RefreshGraphicsControls();
            });
            RefreshGraphicsControls();
            panel.SetActive(false);
        }

        private void Update()
        {
            // Quick pop-in on unscaled time (the panel opens while the game is paused)
            if (panel == null || !panel.activeSelf || showTime >= ShowDuration) return;

            showTime += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(showTime / ShowDuration);
            float eased = 1f - (1f - k) * (1f - k) * (1f - k);
            panelGroup.alpha = eased;
            card.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, eased);
        }

        private Button CreateStyleButton(string objectName, string label, int index, CrtStyle style)
        {
            Button button = CreateTerminalButton(card, objectName, label, 13);
            PlaceTopLeft(button.GetComponent<RectTransform>(), ControlX + index * (SegmentWidth + 12f), 252f, SegmentWidth, 40f);
            button.onClick.AddListener(() => SetCrtStyle(style));
            AttachFeedback(button, SfxId.UiClick);
            return button;
        }

        private void SetCrtStyle(CrtStyle style)
        {
            GameGraphicsSettings.CrtStyle = style;
            RefreshGraphicsControls();
        }

        private void RefreshGraphicsControls()
        {
            CrtStyle style = GameGraphicsSettings.CrtStyle;
            SetToggleVisual(fullTvButton, style == CrtStyle.FullTv);
            SetToggleVisual(classicButton, style == CrtStyle.Classic);
            SetToggleVisual(offButton, style == CrtStyle.Off);

            if (crtStyleHint != null)
            {
                crtStyleHint.text = style switch
                {
                    CrtStyle.Classic => "A lighter, crisper old TV: soft scanlines and a faint RGB tint.",
                    CrtStyle.Off => "No CRT effect. Brightness still applies.",
                    _ => "A full old TV: strong scanlines, RGB phosphors and glow."
                };
            }

            RefreshValueTexts();
        }

        private void RefreshValueTexts()
        {
            if (musicValueText != null) musicValueText.text = Percent(musicSlider.value);
            if (sfxValueText != null) sfxValueText.text = Percent(sfxSlider.value);
            if (brightnessValueText != null) brightnessValueText.text = Percent(GameGraphicsSettings.Brightness);
        }

        private static string Percent(float value) => Mathf.RoundToInt(value * 100f) + "%";

        private static void SetToggleVisual(Button button, bool selected)
        {
            if (button == null) return;
            MenuButtonFeedback feedback = button.GetComponent<MenuButtonFeedback>();
            Color fill = selected ? AccentColor : TerminalButton;
            if (feedback != null)
            {
                feedback.SetBaseColor(fill);
                feedback.glowAlways = selected;
            }
            else
            {
                button.GetComponent<Image>().color = fill;
            }

            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = selected ? SelectedLabel : LabelColor;
        }

        private void CreateSectionHeader(string label, float y)
        {
            Text header = CreateText(card, "// " + label, 12, TextAnchor.MiddleLeft, AccentColor);
            PlaceTopLeft(header.rectTransform, Padding, y - 12f, 200f, 24f);

            float lineX = Padding + header.preferredWidth + 16f;
            Image line = CreateImage(card, label + "Line", new Color(LineColor.r, LineColor.g, LineColor.b, 0.3f));
            PlaceTopLeft(line.rectTransform, lineX, y - 1f, CardWidth - Padding - lineX, 2f);
        }

        private void CreateRowLabel(string label, float centerY)
        {
            Text text = CreateText(card, label, LabelSize, TextAnchor.MiddleLeft, LabelColor);
            PlaceTopLeft(text.rectTransform, Padding + 4f, centerY - 15f, LabelWidth, 30f);
        }

        /// <summary>Label, slider (thin track, bright handle) and a percentage readout on one row.</summary>
        private Slider CreateSliderRow(string label, float centerY, float min, float max, float value, out Text valueText)
        {
            CreateRowLabel(label, centerY);

            GameObject sliderObject = new GameObject(label + "Slider", typeof(RectTransform), typeof(Image), typeof(Slider));
            sliderObject.transform.SetParent(card, false);
            PlaceTopLeft(sliderObject.GetComponent<RectTransform>(), ControlX, centerY - 14f, ControlWidth, 28f);

            // Invisible hit area over the whole row height, so pressing anywhere on the bar jumps the handle
            // there (Slider moves to the pointer on press) and dragging continues from that point.
            Image hitArea = sliderObject.GetComponent<Image>();
            hitArea.color = new Color(0f, 0f, 0f, 0f);
            hitArea.raycastTarget = true;

            // The slider only drives x anchors, so the y anchors set here give the track and fill their height
            Image track = CreateImage(sliderObject.transform, "Track", TrackColor);
            track.rectTransform.anchorMin = new Vector2(0f, 0.3f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.7f);
            track.rectTransform.offsetMin = Vector2.zero;
            track.rectTransform.offsetMax = Vector2.zero;
            Outline trackOutline = track.gameObject.AddComponent<Outline>();
            trackOutline.effectColor = new Color(LineColor.r, LineColor.g, LineColor.b, 0.35f);
            trackOutline.effectDistance = new Vector2(1f, -1f);

            Image fill = CreateImage(sliderObject.transform, "Fill", AccentColor);
            fill.rectTransform.anchorMin = new Vector2(0f, 0.3f);
            fill.rectTransform.anchorMax = new Vector2(1f, 0.7f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;

            Image handle = CreateImage(sliderObject.transform, "Handle", AccentBright);
            handle.raycastTarget = true;
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0f, 1f);
            handle.rectTransform.sizeDelta = new Vector2(14f, 0f);
            Outline handleOutline = handle.gameObject.AddComponent<Outline>();
            handleOutline.effectColor = new Color(0f, 0f, 0f, 0.6f);
            handleOutline.effectDistance = new Vector2(2f, -2f);

            Slider slider = sliderObject.GetComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.transition = Selectable.Transition.None;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(value);

            valueText = CreateText(card, Percent(value), LabelSize, TextAnchor.MiddleRight, AccentBright);
            PlaceTopLeft(valueText.rectTransform, CardWidth - Padding - ValueWidth, centerY - 15f, ValueWidth, 30f);
            return slider;
        }

        private void Show()
        {
            if (panel == null) return;
            musicSlider.SetValueWithoutNotify(AudioManager.Instance != null ? AudioManager.Instance.MusicVolume : musicSlider.value);
            sfxSlider.SetValueWithoutNotify(AudioManager.Instance != null ? AudioManager.Instance.SfxVolume : sfxSlider.value);
            brightnessSlider.SetValueWithoutNotify(GameGraphicsSettings.Brightness);
            RefreshGraphicsControls();

            panel.transform.SetAsLastSibling();
            panel.SetActive(true);
            showTime = 0f;
            panelGroup.alpha = 0f;
            card.localScale = Vector3.one * 0.94f;
        }

        private void Hide()
        {
            if (panel != null) panel.SetActive(false);
        }

        /// <summary>The button that opens the panel; the creating screen restyles and positions it.</summary>
        private static Button CreateButton(Transform parent, string name, string label, Vector2 size)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<RectTransform>().sizeDelta = size;
            var image = buttonObject.GetComponent<Image>();
            var sprite = Resources.Load<Sprite>("UI/buttons/btn_dark_normal");
            if (sprite == null)
            {
                var all = Resources.LoadAll<Sprite>("UI/buttons/btn_dark_normal");
                if (all != null && all.Length > 0) sprite = all[0];
            }
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                image.color = Color.white;
            }
            else
            {
                image.color = ButtonColor;
            }
            Text text = CreateText(buttonObject.transform, label, 16, TextAnchor.MiddleCenter, Color.white);
            text.fontStyle = FontStyle.Bold;
            SetStretch(text.rectTransform);
            return buttonObject.GetComponent<Button>();
        }

        /// <summary>Flat terminal button: dark green fill, centred label (MenuButtonFeedback adds hover/glow).</summary>
        private static Button CreateTerminalButton(Transform parent, string name, string label, int fontSize)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = TerminalButton;
            Text text = CreateText(buttonObject.transform, label, fontSize, TextAnchor.MiddleCenter, LabelColor);
            SetStretch(text.rectTransform);
            return buttonObject.GetComponent<Button>();
        }

        private static Text CreateText(Transform parent, string content, int size, TextAnchor alignment, Color color)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
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

        private static void AttachFeedback(Button button, SfxId clickSound)
        {
            button.transition = Selectable.Transition.None;
            MenuButtonFeedback feedback = button.gameObject.AddComponent<MenuButtonFeedback>();
            feedback.hoverScale = 1.04f;
            feedback.respondToFocus = false;
            feedback.Hovered = () => AudioManager.Instance?.PlaySfx(SfxId.UiHover, 0.7f, UnityEngine.Random.Range(0.97f, 1.03f));
            feedback.Clicked = () => AudioManager.Instance?.PlaySfx(clickSound);

            Image image = button.GetComponent<Image>();
            if (image == null) return;

            Outline outline = image.gameObject.AddComponent<Outline>();
            outline.useGraphicAlpha = false;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.effectColor = new Color(AccentBright.r, AccentBright.g, AccentBright.b, 0f);
            feedback.glowOutline = outline;
            feedback.glowColor = new Color(AccentBright.r, AccentBright.g, AccentBright.b, 0.9f);
        }

        /// <summary>Four L-shaped terminal corner brackets; a negative inset pushes them outside the rect.</summary>
        private static void AddCornerBrackets(RectTransform target, float length, float thickness, Color color, float inset = 0f)
        {
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2, i / 2);
                var inward = new Vector2(corner.x < 0.5f ? 1f : -1f, corner.y < 0.5f ? 1f : -1f);
                CreateBracketBar(target, corner, inward * inset, new Vector2(length, thickness), color);
                CreateBracketBar(target, corner, inward * inset, new Vector2(thickness, length), color);
            }
        }

        private static void CreateBracketBar(RectTransform parent, Vector2 corner, Vector2 offset, Vector2 size, Color color)
        {
            Image bar = CreateImage(parent, "CornerBracket", color);
            RectTransform rect = bar.rectTransform;
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Places a rect by its top-left corner, in pixels from the top-left of its parent.</summary>
        private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetPosition(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
