using System;
using UnityEngine;
using UnityEngine.UI;
using TrashTD.UI;

namespace TrashTD.Audio
{
    /// <summary>
    /// Runtime-built music and SFX settings panel shared by menu and gameplay.
    /// </summary>
    public sealed class AudioSettingsUI : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.035f, 0.045f, 0.065f, 0.98f);
        private static readonly Color ButtonColor = new Color(0.10f, 0.13f, 0.18f, 1f);
        private static readonly Color AccentColor = new Color(0.18f, 0.82f, 0.45f, 1f);

        private GameObject panel;
        private Slider musicSlider;
        private Slider sfxSlider;
        public Button SettingsButton { get; private set; }

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
            panelRect.sizeDelta = new Vector2(520f, 330f);
            panel.GetComponent<Image>().color = PanelColor;
            Outline panelOutline = panel.AddComponent<Outline>();
            panelOutline.effectColor = new Color(0.18f, 0.82f, 0.45f, 0.28f);
            panelOutline.effectDistance = new Vector2(2f, -2f);

            Text title = CreateText(panel.transform, "AUDIO SETTINGS", 28, TextAnchor.MiddleCenter);
            title.color = AccentColor;
            SetPosition(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -45f), new Vector2(460f, 42f));

            CreateTextAndSlider(panel.transform, "MUSIC", 0.45f, out musicSlider);
            SetPosition(musicSlider.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(65f, 30f), new Vector2(285f, 24f));
            CreateTextAndSlider(panel.transform, "SFX", 0.45f, out sfxSlider);
            SetPosition(sfxSlider.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(65f, -40f), new Vector2(285f, 24f));

            Button closeButton = CreateButton(panel.transform, "CloseButton", "CLOSE", new Vector2(170f, 46f));
            SetPosition(closeButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 25f), new Vector2(170f, 46f));
            closeButton.onClick.AddListener(Hide);
            AttachFeedback(closeButton, SfxId.UiBack);

            musicSlider.onValueChanged.AddListener(value => AudioManager.Instance?.SetMusicVolume(value));
            sfxSlider.onValueChanged.AddListener(value => AudioManager.Instance?.SetSfxVolume(value));
            panel.SetActive(false);
        }

        private void CreateTextAndSlider(Transform parent, string label, float defaultValue, out Slider slider)
        {
            Text text = CreateText(parent, label, 18, TextAnchor.MiddleLeft);
            SetPosition(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-145f, label == "MUSIC" ? 30f : -40f), new Vector2(110f, 30f));
            text.color = new Color(0.65f, 0.72f, 0.82f, 1f);

            GameObject sliderObject = new GameObject(label + "Slider", typeof(RectTransform), typeof(Slider));
            sliderObject.transform.SetParent(parent, false);
            slider = sliderObject.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = AudioManager.Instance != null
                ? (label == "MUSIC" ? AudioManager.Instance.MusicVolume : AudioManager.Instance.SfxVolume)
                : defaultValue;
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
        }

        private void Show()
        {
            if (panel == null) return;
            musicSlider.value = AudioManager.Instance != null ? AudioManager.Instance.MusicVolume : musicSlider.value;
            sfxSlider.value = AudioManager.Instance != null ? AudioManager.Instance.SfxVolume : sfxSlider.value;
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
            buttonObject.GetComponent<Image>().color = ButtonColor;
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
