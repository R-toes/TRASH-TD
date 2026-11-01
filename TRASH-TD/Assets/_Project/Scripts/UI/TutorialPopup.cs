using System;
using UnityEngine;
using UnityEngine.UI;
using TrashTD.Audio;
using TrashTD.Data;
using TrashTD.Systems;

namespace TrashTD.UI
{
    /// <summary>
    /// Full-screen windows the tutorial opens (TutorialAction.ChooseControls / CooldownBriefing). Each has its own
    /// overlay canvas at sorting order 300, like the environment briefing: above Coalesce (250) and the draft (200).
    /// The window blocks clicks to the game; TutorialDirector hides it while the pause menu is open.
    /// </summary>
    public sealed class TutorialPopup
    {
        // Control picker: the settings panel's green terminal
        private static readonly Color TerminalCard = new Color(0.016f, 0.042f, 0.028f, 0.98f);
        private static readonly Color TerminalHeader = new Color(0.030f, 0.090f, 0.055f, 1f);
        private static readonly Color TerminalLine = new Color(0.22f, 0.91f, 0.50f, 0.45f);
        private static readonly Color TerminalBright = new Color(0.32f, 1.00f, 0.64f, 1f);
        private static readonly Color TerminalLabel = new Color(0.45f, 0.78f, 0.56f, 1f);
        private static readonly Color TerminalButton = new Color(0.035f, 0.090f, 0.060f, 1f);
        private static readonly Color TerminalAccent = new Color(0.18f, 0.82f, 0.45f, 1f);

        // Cooldown briefing: the HUD's modal briefing
        private static readonly Color WarnColor = new Color(1f, 0.45f, 0.35f, 1f);
        private static readonly Color BodyColor = new Color(0.86f, 0.9f, 0.95f, 1f);
        private static readonly Color GoldColor = new Color(1f, 0.82f, 0.3f, 1f);
        private static readonly Color ReadyColor = new Color(0.35f, 0.95f, 0.55f, 1f);

        public GameObject Root { get; private set; }
        public bool IsOpen => Root != null;

        public void SetShown(bool shown)
        {
            if (Root != null && Root.activeSelf != shown) Root.SetActive(shown);
        }

        public void Close()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null;
        }

        // ─────────────────────────────────────────────────────
        //  Control picker
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// The three placement schemes side by side with their diagrams. CONFIRM stays off until one is picked
        /// (the current scheme is pre-picked only if the player has chosen before); confirming saves it.
        /// </summary>
        public static TutorialPopup ShowControlPicker(string subtitle, Action<PlacementControlScheme> onConfirmed)
        {
            var window = new TutorialPopup();
            RectTransform card = window.CreateOverlay("TutorialControlPicker", new Vector2(900f, 560f), TerminalCard);
            Outline cardOutline = card.gameObject.AddComponent<Outline>();
            cardOutline.effectColor = TerminalLine;
            cardOutline.effectDistance = new Vector2(2f, -2f);

            Image header = MakeImage(card, "Header", TerminalHeader, new Vector2(0f, 0f), new Vector2(900f, 56f), true);
            MakeText(header.transform, "CONTROL STYLE", 20, TerminalBright, TextAnchor.MiddleLeft, new Vector2(32f, 0f), new Vector2(600f, 56f), true);
            MakeImage(card, "HeaderLine", TerminalLine, new Vector2(0f, 56f), new Vector2(900f, 2f), true);

            string line = string.IsNullOrWhiteSpace(subtitle) ? "How do you want to place operators? Pick one." : subtitle;
            Text subtitleText = MakeText(card, line, 11, TerminalLabel, TextAnchor.MiddleCenter, new Vector2(40f, 72f), new Vector2(820f, 30f), true);
            subtitleText.horizontalOverflow = HorizontalWrapMode.Wrap;

            var options = new PlacementSchemeOption[3];
            PlacementControlScheme? picked = ControlSettings.HasChosen ? ControlSettings.Scheme : (PlacementControlScheme?)null;

            Button confirm = MakeTerminalButton(card, "CONFIRM", new Vector2((900f - 240f) * 0.5f, 492f), new Vector2(240f, 48f));
            void Refresh()
            {
                foreach (PlacementSchemeOption option in options) option.SetSelected(picked.HasValue && option.Scheme == picked.Value);
                confirm.interactable = picked.HasValue;
                confirm.GetComponent<Image>().color = picked.HasValue ? TerminalAccent : TerminalButton;
                confirm.GetComponentInChildren<Text>().color = picked.HasValue ? new Color(0.02f, 0.1f, 0.05f, 1f) : TerminalLabel;
            }

            Vector2 optionSize = new Vector2(250f, 300f);
            float gap = (900f - 2f * 40f - 3f * optionSize.x) * 0.5f;
            for (int i = 0; i < options.Length; i++)
            {
                PlacementControlScheme scheme = (PlacementControlScheme)i;
                PlacementSchemeOption option = PlacementSchemeOption.Create(card, scheme, optionSize);
                PlaceTopLeft(option.Button.GetComponent<RectTransform>(), 40f + i * (optionSize.x + gap), 112f, optionSize.x, optionSize.y);
                option.Button.onClick.AddListener(() =>
                {
                    picked = scheme;
                    AudioManager.Instance?.PlaySfx(SfxId.UiClick);
                    Refresh();
                });
                options[i] = option;
            }

            MakeText(card, "You can change this any time in SETTINGS > CONTROLS.", 9, TerminalLabel, TextAnchor.MiddleCenter,
                new Vector2(40f, 424f), new Vector2(820f, 20f), true);
            MakeText(card, "KEYS: WASD / ARROWS FACE  |  ENTER PLACES  |  ESC / RIGHT-CLICK CANCELS", 9,
                new Color(TerminalLabel.r, TerminalLabel.g, TerminalLabel.b, 0.7f), TextAnchor.MiddleCenter,
                new Vector2(40f, 448f), new Vector2(820f, 20f), true);

            confirm.onClick.AddListener(() =>
            {
                if (!picked.HasValue) return;
                AudioManager.Instance?.PlaySfx(SfxId.UiClick);
                ControlSettings.Scheme = picked.Value;
                window.Close();
                onConfirmed?.Invoke(picked.Value);
            });
            Refresh();
            return window;
        }

        // ─────────────────────────────────────────────────────
        //  Cooldown briefing
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// What a redeploy cooldown is: a deck card showing its countdown badge, then one card per class with its
        /// wait (OperatorManager.GetRetreatCooldownRounds), and the free Preparation recall. UNDERSTOOD closes it.
        /// </summary>
        public static TutorialPopup ShowCooldownBriefing(string description, Action onDismissed)
        {
            var window = new TutorialPopup();
            RectTransform card = window.CreateOverlay("TutorialCooldownBriefing", new Vector2(900f, 600f), Color.white);
            GameplayHUDUI.ApplyPanelContainer(card.gameObject, GameplayHUDUI.PanelContainerType.Modal);
            Outline cardOutline = card.gameObject.AddComponent<Outline>();
            cardOutline.effectColor = new Color(WarnColor.r, WarnColor.g, WarnColor.b, 0.8f);
            cardOutline.effectDistance = new Vector2(3f, -3f);

            Text title = MakeText(card, "REDEPLOY COOLDOWN", 28, WarnColor, TextAnchor.MiddleCenter, new Vector2(0f, 30f), new Vector2(900f, 44f), true);
            title.fontStyle = FontStyle.Bold;

            string body = string.IsNullOrWhiteSpace(description)
                ? "A retreated or knocked-out operator's card goes back to your deck, but it has to sit out a few waves before it can be deployed again."
                : description;
            Text bodyText = MakeText(card, body, 14, BodyColor, TextAnchor.MiddleCenter, new Vector2(60f, 84f), new Vector2(780f, 66f), true);
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.lineSpacing = 1.3f;

            // Example card: a dimmed Defender with its countdown badge
            BuildExampleCard(card, new Vector2(90f, 166f));
            Text exampleText = MakeText(card,
                "<color=#FFD24D>The badge counts the waves left.</color>\nIt drops by 1 every time a wave ends. At 0 the card lights up and can be deployed again.",
                13, BodyColor, TextAnchor.MiddleLeft, new Vector2(230f, 166f), new Vector2(600f, 120f), true);
            exampleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            exampleText.lineSpacing = 1.35f;
            exampleText.supportRichText = true;

            // One card per class with its wait, shortest first
            var classes = (OperatorClass[])Enum.GetValues(typeof(OperatorClass));
            Array.Sort(classes, (a, b) =>
            {
                int byWait = OperatorManager.GetRetreatCooldownRounds(a).CompareTo(OperatorManager.GetRetreatCooldownRounds(b));
                return byWait != 0 ? byWait : a.CompareTo(b);
            });
            const float classWidth = 124f;
            const float classGap = 10f;
            float startX = (900f - classes.Length * classWidth - (classes.Length - 1) * classGap) * 0.5f;
            for (int i = 0; i < classes.Length; i++)
                BuildClassCard(card, classes[i], new Vector2(startX + i * (classWidth + classGap), 312f), new Vector2(classWidth, 120f));

            Text refund = MakeText(card,
                "Changed your mind? Recalling an operator during preparation, before it has fought, is free: no cooldown.",
                12, new Color(BodyColor.r, BodyColor.g, BodyColor.b, 0.8f), TextAnchor.MiddleCenter, new Vector2(60f, 446f), new Vector2(780f, 40f), true);
            refund.horizontalOverflow = HorizontalWrapMode.Wrap;

            var dismissObject = new GameObject("UnderstoodButton", typeof(RectTransform), typeof(Image), typeof(Button));
            dismissObject.transform.SetParent(card, false);
            PlaceTopLeft(dismissObject.GetComponent<RectTransform>(), (900f - 250f) * 0.5f, 508f, 250f, 58f);
            Button dismiss = dismissObject.GetComponent<Button>();
            GameplayHUDUI.ApplyButtonContainer(dismiss, GameplayHUDUI.ButtonContainerType.Primary);
            MakeText(dismissObject.transform, "UNDERSTOOD", 18, Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(250f, 58f), true).fontStyle = FontStyle.Bold;
            dismiss.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlaySfx(SfxId.UiClick);
                window.Close();
                onDismissed?.Invoke();
            });
            return window;
        }

        private static void BuildExampleCard(RectTransform parent, Vector2 topLeft)
        {
            Image frame = MakeImage(parent, "ExampleCard", new Color(0.1f, 0.12f, 0.16f, 1f), topLeft, new Vector2(110f, 120f), true);
            Outline outline = frame.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.25f);
            outline.effectDistance = new Vector2(2f, -2f);

            Image portrait = MakeImage(frame.transform, "Portrait", new Color(1f, 1f, 1f, 0.35f), new Vector2(23f, 22f), new Vector2(64f, 64f), true);
            portrait.sprite = GameplayHUDUI.GetClassIconSprite(OperatorClass.Defender);
            portrait.preserveAspect = true;
            MakeText(frame.transform, "ON COOLDOWN", 8, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter, new Vector2(0f, 94f), new Vector2(110f, 18f), true);

            Image badge = MakeImage(frame.transform, "Badge", GoldColor, new Vector2(78f, -10f), new Vector2(40f, 40f), true);
            Outline badgeOutline = badge.gameObject.AddComponent<Outline>();
            badgeOutline.effectColor = new Color(0f, 0f, 0f, 0.7f);
            badgeOutline.effectDistance = new Vector2(2f, -2f);
            Text count = MakeText(badge.transform, "2", 22, new Color(0.15f, 0.1f, 0.02f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(40f, 40f), true);
            count.fontStyle = FontStyle.Bold;
        }

        private static void BuildClassCard(RectTransform parent, OperatorClass operatorClass, Vector2 topLeft, Vector2 size)
        {
            int waves = OperatorManager.GetRetreatCooldownRounds(operatorClass);
            Color accent = waves <= 0 ? ReadyColor : GoldColor;

            Image cardImage = MakeImage(parent, operatorClass + "Card", new Color(0.06f, 0.08f, 0.11f, 0.95f), topLeft, size, true);
            Outline outline = cardImage.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.6f);
            outline.effectDistance = new Vector2(2f, -2f);

            Image icon = MakeImage(cardImage.transform, "Icon", Color.white, new Vector2((size.x - 36f) * 0.5f, 10f), new Vector2(36f, 36f), true);
            icon.sprite = GameplayHUDUI.GetClassIconSprite(operatorClass);
            icon.preserveAspect = true;

            MakeText(cardImage.transform, operatorClass.ToString().ToUpperInvariant(), 10, BodyColor, TextAnchor.MiddleCenter,
                new Vector2(0f, 52f), new Vector2(size.x, 18f), true);
            string wait = waves <= 0 ? "NO WAIT" : waves == 1 ? "1 WAVE" : waves + " WAVES";
            Text waitText = MakeText(cardImage.transform, wait, 13, accent, TextAnchor.MiddleCenter, new Vector2(0f, 80f), new Vector2(size.x, 24f), true);
            waitText.fontStyle = FontStyle.Bold;
        }

        // ─────────────────────────────────────────────────────
        //  Building blocks
        // ─────────────────────────────────────────────────────

        /// <summary>An overlay canvas with a dimmer that catches every click, holding a centred card; returns the card.</summary>
        private RectTransform CreateOverlay(string name, Vector2 cardSize, Color cardColor)
        {
            Root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = Root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            CanvasScaler scaler = Root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Image dimmer = MakeImage(Root.transform, "Dimmer", new Color(0.01f, 0.015f, 0.025f, 0.82f), Vector2.zero, Vector2.zero, false);
            dimmer.raycastTarget = true;
            RectTransform dimRect = dimmer.rectTransform;
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero;
            dimRect.offsetMax = Vector2.zero;

            Image cardImage = MakeImage(Root.transform, "Card", cardColor, Vector2.zero, cardSize, false);
            cardImage.raycastTarget = true;
            return cardImage.rectTransform;
        }

        private static Button MakeTerminalButton(RectTransform parent, string label, Vector2 topLeft, Vector2 size)
        {
            var obj = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<Image>().color = TerminalButton;
            PlaceTopLeft(obj.GetComponent<RectTransform>(), topLeft.x, topLeft.y, size.x, size.y);
            Outline outline = obj.AddComponent<Outline>();
            outline.effectColor = TerminalLine;
            outline.effectDistance = new Vector2(2f, -2f);
            Button button = obj.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            MakeText(obj.transform, label, 16, TerminalLabel, TextAnchor.MiddleCenter, Vector2.zero, size, true);
            return button;
        }

        /// <summary>An image placed by its top-left corner (topLeft = true) or centred on its parent.</summary>
        private static Image MakeImage(Transform parent, string name, Color color, Vector2 position, Vector2 size, bool topLeft)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            Image image = obj.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (topLeft) PlaceTopLeft(image.rectTransform, position.x, position.y, size.x, size.y);
            else
            {
                image.rectTransform.anchoredPosition = position;
                image.rectTransform.sizeDelta = size;
            }
            return image;
        }

        private static Text MakeText(Transform parent, string value, int size, Color color, TextAnchor alignment, Vector2 position, Vector2 box, bool topLeft)
        {
            var obj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            Text text = obj.GetComponent<Text>();
            text.text = value;
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;
            if (topLeft) PlaceTopLeft(text.rectTransform, position.x, position.y, box.x, box.y);
            else
            {
                text.rectTransform.anchoredPosition = position;
                text.rectTransform.sizeDelta = box;
            }
            return text;
        }

        private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
