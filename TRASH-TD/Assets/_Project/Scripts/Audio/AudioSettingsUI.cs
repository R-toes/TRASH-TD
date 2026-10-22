using System;
using UnityEngine;
using UnityEngine.UI;
using TrashTD.UI;

namespace TrashTD.Audio
{
    /// <summary>
    /// Runtime-built settings panel shared by menu and gameplay: Audio (music, SFX) and Graphics
    /// (CRT style, brightness; see GameGraphicsSettings). The class keeps its original name because
    /// several screens create it.
    /// </summary>
    public sealed class AudioSettingsUI : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.035f, 0.045f, 0.065f, 0.98f);
        private static readonly Color ButtonColor = new Color(0.10f, 0.13f, 0.18f, 1f);
        private static readonly Color AccentColor = new Color(0.18f, 0.82f, 0.45f, 1f);
        private static readonly Color LabelColor = new Color(0.65f, 0.72f, 0.82f, 1f);
        private static readonly Color HintColor = new Color(0.50f, 0.58f, 0.68f, 1f);

        private const float LabelX = -145f;
        private const float SliderX = 65f;
        private const float SliderWidth = 285f;

        private GameObject panel;
        private Slider musicSlider;
        private Slider sfxSlider;
        private Slider brightnessSlider;
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
            SettingsButton = CreateButton(buttonParent, "AudioSettingsButton", "⚙  SETTINGS", new Vector2(200f, 48f));
            styleButton?.Invoke(SettingsButton);
            positionButton?.Invoke(SettingsButton.GetComponent<RectTransform>());
            SettingsButton.onClick.AddListener(Show);

            Transform canvasRoot = transform.parent;
            panel = new GameObject("AudioSettingsPanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            panel.transform.SetParent(canvasRoot, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(580f, 580f);
            var panelImg = panel.GetComponent<Image>();
            var panelSprite = Resources.Load<Sprite>("UI/panels/panel_dark");
            if (panelSprite == null)
            {
                var all = Resources.LoadAll<Sprite>("UI/panels/panel_dark");
                if (all != null && all.Length > 0) panelSprite = all[0];
            }
            if (panelSprite != null)
            {
                panelImg.sprite = panelSprite;
                panelImg.type = Image.Type.Sliced;
                panelImg.color = Color.white;
            }
            else
            {
                panelImg.color = PanelColor;
            }
            Outline panelOutline = panel.AddComponent<Outline>();
            panelOutline.effectColor = new Color(0.18f, 0.82f, 0.45f, 0.28f);
            panelOutline.effectDistance = new Vector2(2f, -2f);

            Text title = CreateText(panel.transform, "SETTINGS", 28, TextAnchor.MiddleCenter);
            title.color = AccentColor;
            SetPosition(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(460f, 42f));

            // ── Audio ──
            CreateSectionHeader(panel.transform, "AUDIO", 195f);

            float musicValue = AudioManager.Instance != null ? AudioManager.Instance.MusicVolume : 0.45f;
            musicSlider = CreateTextAndSlider(panel.transform, "MUSIC", 150f, 0f, 1f, musicValue, SliderWidth);
            float sfxValue = AudioManager.Instance != null ? AudioManager.Instance.SfxVolume : 0.45f;
            sfxSlider = CreateTextAndSlider(panel.transform, "SFX", 100f, 0f, 1f, sfxValue, SliderWidth);

            Image divider = CreateImage(panel.transform, "SectionDivider", new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.25f));
            SetPosition(divider.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(500f, 2f));

            // ── Graphics ──
            CreateSectionHeader(panel.transform, "GRAPHICS", 30f);

            Text styleLabel = CreateText(panel.transform, "CRT STYLE", 18, TextAnchor.MiddleLeft);
            styleLabel.color = LabelColor;
            SetPosition(styleLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(LabelX - 30f, -15f), new Vector2(170f, 30f));

            fullTvButton = CreateStyleButton("CrtFullTvButton", "FULL TV", -25f, CrtStyle.FullTv);
            classicButton = CreateStyleButton("CrtClassicButton", "CLASSIC", 95f, CrtStyle.Classic);
            offButton = CreateStyleButton("CrtOffButton", "OFF", 215f, CrtStyle.Off);

            crtStyleHint = CreateText(panel.transform, string.Empty, 12, TextAnchor.MiddleCenter);
            crtStyleHint.fontStyle = FontStyle.Normal;
            crtStyleHint.color = HintColor;
            SetPosition(crtStyleHint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -58f), new Vector2(520f, 36f));

            brightnessSlider = CreateTextAndSlider(panel.transform, "BRIGHTNESS", -110f,
                GameGraphicsSettings.MinBrightness, GameGraphicsSettings.MaxBrightness, GameGraphicsSettings.Brightness, SliderWidth - 70f);
            brightnessValueText = CreateText(panel.transform, string.Empty, 16, TextAnchor.MiddleRight);
            brightnessValueText.color = LabelColor;
            SetPosition(brightnessValueText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(SliderX + SliderWidth * 0.5f - 25f, -110f), new Vector2(70f, 30f));

            Button closeButton = CreateButton(panel.transform, "CloseButton", "CLOSE", new Vector2(170f, 46f));
            SetPosition(closeButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 25f), new Vector2(170f, 46f));
            closeButton.onClick.AddListener(Hide);
            AttachFeedback(closeButton, SfxId.UiBack);

            musicSlider.onValueChanged.AddListener(value => AudioManager.Instance?.SetMusicVolume(value));
            sfxSlider.onValueChanged.AddListener(value => AudioManager.Instance?.SetSfxVolume(value));
            brightnessSlider.onValueChanged.AddListener(value =>
            {
                GameGraphicsSettings.Brightness = value;
                RefreshGraphicsControls();
            });
            RefreshGraphicsControls();
            panel.SetActive(false);
        }

        private Button CreateStyleButton(string objectName, string label, float x, CrtStyle style)
        {
            const float width = 108f;
            Button button = CreateButton(panel.transform, objectName, label, new Vector2(width, 42f));
            SetPosition(button.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, -15f), new Vector2(width, 42f));
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

            if (brightnessValueText != null)
            {
                brightnessValueText.text = Mathf.RoundToInt(GameGraphicsSettings.Brightness * 100f) + "%";
            }
        }

        private static void SetToggleVisual(Button button, bool selected)
        {
            if (button == null) return;
            MenuButtonFeedback feedback = button.GetComponent<MenuButtonFeedback>();
            Color tint = selected ? AccentColor : Color.white;
            if (feedback != null)
            {
                feedback.SetBaseColor(tint);
                feedback.glowAlways = selected;
            }
            else
            {
                button.GetComponent<Image>().color = tint;
            }

            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = selected ? new Color(0.02f, 0.08f, 0.04f, 1f) : LabelColor;
        }

        private static void CreateSectionHeader(Transform parent, string label, float y)
        {
            Text header = CreateText(parent, label, 14, TextAnchor.MiddleLeft);
            header.color = AccentColor;
            SetPosition(header.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-40f, y), new Vector2(420f, 24f));
        }

        private static Slider CreateTextAndSlider(Transform parent, string label, float y, float min, float max, float value, float width)
        {
            Text text = CreateText(parent, label, 18, TextAnchor.MiddleLeft);
            SetPosition(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(LabelX - 30f, y), new Vector2(170f, 30f));
            text.color = LabelColor;

            GameObject sliderObject = new GameObject(label + "Slider", typeof(RectTransform), typeof(Slider));
            sliderObject.transform.SetParent(parent, false);
            Slider slider = sliderObject.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.direction = Slider.Direction.LeftToRight;
            slider.transition = Selectable.Transition.None;

            Image background = CreateImage(sliderObject.transform, "Background", new Color(0.12f, 0.15f, 0.20f, 1f));
            SetStretch(background.rectTransform);
            Image fill = CreateImage(sliderObject.transform, "Fill", AccentColor);
            SetStretch(fill.rectTransform);
            slider.fillRect = fill.rectTransform;

            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(sliderObject.transform, false);
            Image handleImage = handle.GetComponent<Image>();
            handleImage.color = Color.white;
            SetPosition(handle.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(22f, 32f));
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handleImage;

            // All sliders share a left edge; narrower ones leave room for a value readout on the right
            float left = SliderX - SliderWidth * 0.5f;
            SetPosition(sliderObject.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(left + width * 0.5f, y), new Vector2(width, 24f));
            return slider;
        }

        private void Show()
        {
            if (panel == null) return;
            musicSlider.value = AudioManager.Instance != null ? AudioManager.Instance.MusicVolume : musicSlider.value;
            sfxSlider.value = AudioManager.Instance != null ? AudioManager.Instance.SfxVolume : sfxSlider.value;
            brightnessSlider.SetValueWithoutNotify(GameGraphicsSettings.Brightness);
            RefreshGraphicsControls();
            panel.SetActive(true);
        }

        private void Hide()
        {
            if (panel != null) panel.SetActive(false);
        }

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
            Text text = CreateText(buttonObject.transform, label, 16, TextAnchor.MiddleCenter);
            SetStretch(text.rectTransform);
            return buttonObject.GetComponent<Button>();
        }

        private static Text CreateText(Transform parent, string content, int size, TextAnchor alignment)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.text = content;
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = Color.white;
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
            outline.effectColor = new Color(0.50f, 0.58f, 0.68f, 0f);
            feedback.glowOutline = outline;
            feedback.glowColor = new Color(0.50f, 0.58f, 0.68f, 0.9f);
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            imageObject.GetComponent<Image>().color = color;
            return imageObject.GetComponent<Image>();
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
