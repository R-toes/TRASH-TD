using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TrashTD.Audio;

namespace TrashTD.UI
{
    public enum TutorialDialogueLayout
    {
        Gameplay, // Bottom-right, standing on the HUD's bottom-right panel
        Compact,  // Bottom-left and small, under the draft screen's card column
        StageMap  // Bottom-right, just above the stage map's QA SANDBOX button
    }

    /// <summary>
    /// Coalesce's side of the tutorial: a big pixel-scaled Coalesce in the bottom-right corner (above the HUD's
    /// bottom-right panel) with a terminal-style speech bubble over it. Lines type out with a voice blip per
    /// character, and Coalesce's sheet animates only while text is typing.
    ///
    /// Talk lines dim the screen and catch every click: a click (or Space) finishes the line, the next one
    /// advances (<see cref="Advanced"/>). Task lines leave the game clickable and wait for the player.
    /// Layouts: gameplay, compact (keeps the draft screen's cards and details panel clear) and stage map.
    /// A pulsing hazard-yellow frame can point at any UI element on any canvas. Unscaled time throughout.
    /// </summary>
    public sealed class TutorialDialogueUI : MonoBehaviour
    {
        private const int CanvasSortingOrder = 250; // Above the HUD (100) and the draft (200), under results (300)
        private const float CharsPerSecond = 42f;
        private const int BlipEveryChars = 2;
        private const float BlipVolume = 0.35f;
        private const float DimAlpha = 0.28f;
        private const float SpeakingFrameSpeedup = 1.25f; // Talking frames run a little faster than the idle sheet

        private static readonly Color Phosphor = new Color(0.22f, 0.91f, 0.50f, 1f);
        private static readonly Color PhosphorDim = new Color(0.22f, 0.91f, 0.50f, 0.45f);
        private static readonly Color PanelColor = new Color(0.012f, 0.034f, 0.022f, 0.96f);
        private static readonly Color HeaderColor = new Color(0.030f, 0.090f, 0.055f, 1f);
        private static readonly Color BodyColor = new Color(0.80f, 0.95f, 0.85f, 1f);
        private static readonly Color HazardYellow = new Color(0.98f, 0.80f, 0.18f, 1f);
        private static readonly Color HazardInk = new Color(0.07f, 0.06f, 0.03f, 1f);

        // Default: Coalesce stands on the HUD's bottom-right panel (~331 px tall) with the bubble above. At 16:9
        // the tutorial grid's empty right columns start at x ~1421, so Coalesce and the bubble stay off the lane.
        private const float DefaultSpriteSize = 256f;
        private const float DefaultRight = 24f;
        private const float DefaultBottom = 345f;
        private const float DefaultBubbleWidth = 470f;
        private const float BubbleHeight = 214f;
        // Stage map: the QA SANDBOX button and ESC hint fill the bottom-right corner up to y ~118
        private const float StageMapBottom = 130f;
        // Compact: bottom-left, under the draft screen's card column
        private const float CompactSpriteSize = 160f;
        private const float CompactBubbleWidth = 660f;
        private const float CompactBubbleHeight = 170f;

        private Canvas canvas;
        private RectTransform root;
        private Button catcher;
        private Image dim;
        private RectTransform speaker;
        private Image speakerImage;
        private RectTransform bubble;
        private Text nameText;
        private Text tagText;
        private Image tagPlate;
        private Text bodyText;
        private Text promptText;
        private RectTransform tail;
        private Button skipButton;
        private RectTransform pointer;
        private readonly List<Image> pointerParts = new List<Image>();
        private GameObject skipConfirm;

        private Sprite[] frames;
        private float frameDuration = 0.15f;
        private float frameTimer;
        private int frameIndex;

        private string fullText = string.Empty;
        private float typedChars;
        private int lastBlipChar;
        private bool isTask;
        private bool bubbleVisible;
        private TutorialDialogueLayout layout = TutorialDialogueLayout.Gameplay;
        private RectTransform pointTarget;

        private static AudioClip synthBlip;

        /// <summary>A click (or Space) on a finished talk line.</summary>
        public event Action Advanced;
        /// <summary>The player confirmed SKIP TUTORIAL.</summary>
        public event Action SkipConfirmed;

        public bool IsTyping => bubbleVisible && typedChars < fullText.Length;
        /// <summary>A talk line or the skip prompt is up: the director keeps the game frozen.</summary>
        public bool IsBlockingGame => (bubbleVisible && !isTask) || IsSkipConfirmOpen;
        public bool IsSkipConfirmOpen => skipConfirm != null && skipConfirm.activeSelf;

        public static TutorialDialogueUI Create(string speakerName, Sprite[] speakerFrames, float speakerFrameDuration)
        {
            var go = new GameObject("TutorialDialogueCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var ui = go.AddComponent<TutorialDialogueUI>();
            ui.Build(speakerName, speakerFrames, speakerFrameDuration);
            return ui;
        }

        // ─────────────────────────────────────────────────────
        //  Public control
        // ─────────────────────────────────────────────────────

        /// <summary>Starts typing a line. A talk line dims the screen and waits for a click; a task line doesn't.</summary>
        public void Say(string text, bool task)
        {
            fullText = string.IsNullOrEmpty(text) ? string.Empty : text.Trim();
            typedChars = 0f;
            lastBlipChar = 0;
            isTask = task;
            bubbleVisible = !string.IsNullOrEmpty(fullText);

            bubble.gameObject.SetActive(bubbleVisible);
            bodyText.text = string.Empty;
            promptText.text = string.Empty;
            tagText.text = task ? "▶ TASK" : "// COMMS";
            tagText.color = task ? HazardInk : PhosphorDim;
            tagPlate.color = task ? HazardYellow : Color.clear;
            ApplyBlocking();
        }

        /// <summary>Hides the bubble; Coalesce stays, idle (e.g. while a wave runs).</summary>
        public void HideBubble()
        {
            bubbleVisible = false;
            fullText = string.Empty;
            bubble.gameObject.SetActive(false);
            ApplyBlocking();
        }

        public void SetLayout(TutorialDialogueLayout value)
        {
            if (layout == value) return;
            layout = value;
            ApplyLayout();
        }

        /// <summary>Frames this UI element (on any canvas) with pulsing brackets; null clears.</summary>
        public void PointAt(RectTransform target)
        {
            pointTarget = target;
            pointer.gameObject.SetActive(target != null);
        }

        /// <summary>Hides everything (e.g. under the pause menu) without losing the current line.</summary>
        public void SetShown(bool shown)
        {
            if (root.gameObject.activeSelf != shown) root.gameObject.SetActive(shown);
        }

        // ─────────────────────────────────────────────────────
        //  Update: typing, animation, input, pointer
        // ─────────────────────────────────────────────────────

        private void Update()
        {
            if (!root.gameObject.activeSelf) return;
            float dt = Time.unscaledDeltaTime;

            if (bubbleVisible && typedChars < fullText.Length)
            {
                typedChars = Mathf.Min(fullText.Length, typedChars + CharsPerSecond * dt);
                int shown = Mathf.FloorToInt(typedChars);
                bodyText.text = fullText.Substring(0, shown);
                if (shown - lastBlipChar >= BlipEveryChars)
                {
                    lastBlipChar = shown;
                    if (!char.IsWhiteSpace(fullText[shown - 1])) PlayBlip();
                }
            }
            else if (bubbleVisible)
            {
                bodyText.text = fullText;
                bool blink = Mathf.Repeat(Time.unscaledTime, 0.9f) < 0.6f;
                promptText.text = !isTask && blink ? "▼" : string.Empty;
            }

            AnimateSpeaker(dt);

            if (!IsSkipConfirmOpen && bubbleVisible && !isTask && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                HandleClick();
            }

            UpdatePointer();
        }

        private void AnimateSpeaker(float dt)
        {
            if (frames == null || frames.Length == 0) return;

            if (IsTyping && frames.Length > 1)
            {
                frameTimer += dt;
                float speakingFrame = frameDuration / SpeakingFrameSpeedup;
                if (frameTimer >= speakingFrame)
                {
                    frameTimer -= speakingFrame;
                    frameIndex = (frameIndex + 1) % frames.Length;
                    speakerImage.sprite = frames[frameIndex];
                }
            }
            else if (frameIndex != 0)
            {
                frameIndex = 0;
                frameTimer = 0f;
                speakerImage.sprite = frames[0];
            }
        }

        private void HandleClick()
        {
            if (!bubbleVisible || isTask) return;

            if (IsTyping)
            {
                typedChars = fullText.Length;
                bodyText.text = fullText;
                return;
            }

            AudioManager.Instance?.PlaySfx(SfxId.UiClick, 0.5f);
            Advanced?.Invoke();
        }

        private void UpdatePointer()
        {
            if (pointTarget == null || !pointTarget.gameObject.activeInHierarchy)
            {
                if (pointer.gameObject.activeSelf) pointer.gameObject.SetActive(false);
                return;
            }

            Canvas targetCanvas = pointTarget.GetComponentInParent<Canvas>();
            if (targetCanvas == null) return;
            targetCanvas = targetCanvas.rootCanvas;
            Camera targetCamera = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCanvas.worldCamera;
            Camera ownCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

            var corners = new Vector3[4];
            pointTarget.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector3 corner in corners)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(targetCamera, corner);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, ownCamera, out Vector2 local);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }

            float grow = 10f + 4f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f));
            pointer.anchoredPosition = (min + max) * 0.5f;
            pointer.sizeDelta = max - min + Vector2.one * grow * 2f;
            if (!pointer.gameObject.activeSelf) pointer.gameObject.SetActive(true);

            float alpha = Mathf.Lerp(0.55f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f));
            foreach (Image part in pointerParts) part.color = new Color(HazardYellow.r, HazardYellow.g, HazardYellow.b, alpha);
        }

        private void ApplyBlocking()
        {
            bool block = bubbleVisible && !isTask;
            catcher.gameObject.SetActive(block);
            dim.color = new Color(0f, 0f, 0f, block ? DimAlpha : 0f);
        }

        // ─────────────────────────────────────────────────────
        //  Voice
        // ─────────────────────────────────────────────────────

        private static void PlayBlip()
        {
            AudioManager audio = AudioManager.Instance;
            if (audio == null) return;

            // A file at SFX/UI/TutorialVoice replaces the synth. Played as a clip: the per-sound repeat
            // throttle would swallow most of the blips.
            AudioClip clip = audio.GetClip(SfxId.TutorialVoice);
            if (clip == null) clip = GetSynthBlip();
            audio.PlaySfx(clip, BlipVolume, UnityEngine.Random.Range(0.92f, 1.1f));
        }

        /// <summary>A soft 45 ms square-ish blip with a fast attack and decay; Coalesce's placeholder voice.</summary>
        private static AudioClip GetSynthBlip()
        {
            if (synthBlip != null) return synthBlip;

            const int sampleRate = 44100;
            int samples = Mathf.RoundToInt(sampleRate * 0.045f);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Min(1f, i / (sampleRate * 0.004f)) * Mathf.Exp(-t * 55f);
                float tone = Mathf.Sin(2f * Mathf.PI * 660f * t) * 0.65f + Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 330f * t)) * 0.18f;
                data[i] = tone * envelope * 0.6f;
            }

            synthBlip = AudioClip.Create("CoalesceVoiceBlip", samples, 1, sampleRate, false);
            synthBlip.SetData(data, 0);
            return synthBlip;
        }

        // ─────────────────────────────────────────────────────
        //  Skip
        // ─────────────────────────────────────────────────────

        private void OpenSkipConfirm()
        {
            AudioManager.Instance?.PlaySfx(SfxId.UiClick);
            skipConfirm.SetActive(true);
        }

        private void CloseSkipConfirm()
        {
            AudioManager.Instance?.PlaySfx(SfxId.UiBack);
            skipConfirm.SetActive(false);
        }

        // ─────────────────────────────────────────────────────
        //  Build
        // ─────────────────────────────────────────────────────

        private void Build(string speakerName, Sprite[] speakerFrames, float speakerFrameDuration)
        {
            canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            root = CreateRect("Root", transform);
            Stretch(root);

            dim = CreateImage(root, "Dim", Color.clear);
            Stretch(dim.rectTransform);
            dim.raycastTarget = false;

            // Talk lines catch every click on the screen
            Image catcherImage = CreateImage(root, "ClickCatcher", Color.clear);
            Stretch(catcherImage.rectTransform);
            catcher = catcherImage.gameObject.AddComponent<Button>();
            catcher.transition = Selectable.Transition.None;
            catcher.navigation = new Navigation { mode = Navigation.Mode.None };
            catcher.onClick.AddListener(HandleClick);

            pointer = BuildPointer(root);

            frames = speakerFrames != null && speakerFrames.Length > 0 ? speakerFrames : null;
            frameDuration = Mathf.Max(0.06f, speakerFrameDuration);
            speakerImage = CreateImage(root, "Coalesce", frames != null ? Color.white : Color.clear);
            speakerImage.sprite = frames != null ? frames[0] : null;
            speakerImage.preserveAspect = true;
            speakerImage.raycastTarget = false;
            speaker = speakerImage.rectTransform;

            BuildBubble(speakerName);
            BuildSkip();

            ApplyLayout();
            HideBubble();
            PointAt(null);
        }

        private void BuildBubble(string speakerName)
        {
            Image panel = CreateImage(root, "SpeechBubble", PanelColor);
            panel.raycastTarget = false;
            bubble = panel.rectTransform;
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = PhosphorDim;
            outline.effectDistance = new Vector2(2f, -2f);
            AddCornerBrackets(bubble, 16f, 3f, Phosphor, -5f);

            // Tail: a small rotated square on the edge facing Coalesce
            Image tailImage = CreateImage(bubble, "Tail", PanelColor);
            tailImage.raycastTarget = false;
            tail = tailImage.rectTransform;
            tail.sizeDelta = new Vector2(22f, 22f);
            tail.localRotation = Quaternion.Euler(0f, 0f, 45f);

            Image header = CreateImage(bubble, "Header", HeaderColor);
            header.raycastTarget = false;
            RectTransform headerRect = header.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 34f);
            headerRect.anchoredPosition = Vector2.zero;

            nameText = CreateText(headerRect, "Name", "◆ " + (string.IsNullOrEmpty(speakerName) ? "COALESCE" : speakerName.ToUpperInvariant()), 13, TextAnchor.MiddleLeft, Phosphor);
            Stretch(nameText.rectTransform, 16f, 140f, 0f, 0f);

            tagPlate = CreateImage(headerRect, "TagPlate", Color.clear);
            tagPlate.raycastTarget = false;
            RectTransform plate = tagPlate.rectTransform;
            plate.anchorMin = plate.anchorMax = new Vector2(1f, 0.5f);
            plate.pivot = new Vector2(1f, 0.5f);
            plate.sizeDelta = new Vector2(118f, 24f);
            plate.anchoredPosition = new Vector2(-10f, 0f);
            tagText = CreateText(plate, "Tag", string.Empty, 11, TextAnchor.MiddleCenter, PhosphorDim);
            Stretch(tagText.rectTransform);

            bodyText = CreateText(bubble, "Body", string.Empty, 15, TextAnchor.UpperLeft, BodyColor);
            bodyText.lineSpacing = 1.45f;
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Truncate;
            bodyText.resizeTextForBestFit = true;
            bodyText.resizeTextMinSize = 10;
            bodyText.resizeTextMaxSize = 15;
            Stretch(bodyText.rectTransform, 18f, 18f, 48f, 30f);

            promptText = CreateText(bubble, "Prompt", string.Empty, 13, TextAnchor.LowerRight, Phosphor);
            Stretch(promptText.rectTransform, 18f, 12f, 18f, 8f);
        }

        private void BuildSkip()
        {
            Image plate = CreateImage(root, "SkipTutorialButton", new Color(0.02f, 0.05f, 0.035f, 0.92f));
            skipButton = plate.gameObject.AddComponent<Button>();
            skipButton.targetGraphic = plate;
            skipButton.navigation = new Navigation { mode = Navigation.Mode.None };
            skipButton.onClick.AddListener(OpenSkipConfirm);
            var outline = plate.gameObject.AddComponent<Outline>();
            outline.effectColor = PhosphorDim;
            outline.effectDistance = new Vector2(1f, -1f);
            Text label = CreateText(plate.rectTransform, "Label", "SKIP TUTORIAL ▶▶", 10, TextAnchor.MiddleCenter, PhosphorDim);
            Stretch(label.rectTransform);
            plate.rectTransform.sizeDelta = new Vector2(176f, 32f);

            // Confirm prompt, centred, above everything else on this canvas
            Image shade = CreateImage(root, "SkipConfirm", new Color(0f, 0f, 0f, 0.6f));
            Stretch(shade.rectTransform);
            skipConfirm = shade.gameObject;

            Image card = CreateImage(shade.rectTransform, "Card", PanelColor);
            RectTransform cardRect = card.rectTransform;
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(620f, 250f);
            var cardOutline = card.gameObject.AddComponent<Outline>();
            cardOutline.effectColor = PhosphorDim;
            cardOutline.effectDistance = new Vector2(2f, -2f);
            AddCornerBrackets(cardRect, 18f, 3f, HazardYellow, -6f);

            Text title = CreateText(cardRect, "Title", "SKIP TUTORIAL?", 18, TextAnchor.MiddleCenter, HazardYellow);
            Stretch(title.rectTransform, 20f, 20f, 26f, 190f);
            Text body = CreateText(cardRect, "Body", "You can replay it any time from STAGE 00 on the map.", 12, TextAnchor.MiddleCenter, BodyColor);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.lineSpacing = 1.4f;
            Stretch(body.rectTransform, 40f, 40f, 76f, 104f);

            Button stay = CreateButton(cardRect, "StayButton", "KEEP LEARNING", Phosphor, HeaderColor);
            Place(stay.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(-140f, 28f), new Vector2(240f, 52f));
            stay.onClick.AddListener(CloseSkipConfirm);

            Button skip = CreateButton(cardRect, "ConfirmSkipButton", "SKIP", HazardInk, HazardYellow);
            Place(skip.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(140f, 28f), new Vector2(240f, 52f));
            skip.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlaySfx(SfxId.UiConfirm);
                skipConfirm.SetActive(false);
                SkipConfirmed?.Invoke();
            });

            skipConfirm.SetActive(false);
        }

        private RectTransform BuildPointer(RectTransform parent)
        {
            RectTransform frame = CreateRect("Pointer", parent);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            const float length = 26f;
            const float thickness = 5f;
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2, i / 2);
                pointerParts.Add(CreateBar(frame, corner, new Vector2(length, thickness)));
                pointerParts.Add(CreateBar(frame, corner, new Vector2(thickness, length)));
            }
            return frame;

            Image CreateBar(RectTransform owner, Vector2 corner, Vector2 size)
            {
                Image bar = CreateImage(owner, "PointerBracket", HazardYellow);
                bar.raycastTarget = false;
                Place(bar.rectTransform, corner, Vector2.zero, size);
                bar.rectTransform.pivot = corner;
                return bar;
            }
        }

        private void ApplyLayout()
        {
            bool compact = layout == TutorialDialogueLayout.Compact;
            float size = compact ? CompactSpriteSize : DefaultSpriteSize;
            float bottom = layout == TutorialDialogueLayout.StageMap ? StageMapBottom : DefaultBottom;
            if (compact)
            {
                // Bottom-left: Coalesce, bubble to the right, skip above Coalesce
                Anchor(speaker, new Vector2(0f, 0f), new Vector2(40f, 24f), new Vector2(size, size));
                Anchor(bubble, new Vector2(0f, 0f), new Vector2(40f + size + 22f, 24f), new Vector2(CompactBubbleWidth, CompactBubbleHeight));
                tail.anchorMin = tail.anchorMax = new Vector2(0f, 0f);
                tail.anchoredPosition = new Vector2(0f, size * 0.5f);
                Anchor(skipButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(40f, 24f + size + 10f), new Vector2(176f, 32f));
            }
            else
            {
                // Bottom-right: bubble above Coalesce, skip to Coalesce's left
                Anchor(speaker, new Vector2(1f, 0f), new Vector2(-DefaultRight, bottom), new Vector2(size, size));
                Anchor(bubble, new Vector2(1f, 0f), new Vector2(-DefaultRight, bottom + size + 18f), new Vector2(DefaultBubbleWidth, BubbleHeight));
                tail.anchorMin = tail.anchorMax = new Vector2(1f, 0f);
                tail.anchoredPosition = new Vector2(-size * 0.5f, 0f);
                Anchor(skipButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-(DefaultRight + size + 12f), bottom), new Vector2(176f, 32f));
            }
        }

        // ─────────────────────────────────────────────────────
        //  UI helpers
        // ─────────────────────────────────────────────────────

        /// <summary>Anchors and pivots a rect at the same screen corner, offset from it.</summary>
        private static void Anchor(RectTransform rect, Vector2 corner, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = corner;
            rect.pivot = corner;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(Transform parent, string name, string content, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            UIFontHelper.Apply(text);
            text.text = content;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, Color labelColor, Color plateColor)
        {
            Image plate = CreateImage(parent, name, plateColor);
            var button = plate.gameObject.AddComponent<Button>();
            button.targetGraphic = plate;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var outline = plate.gameObject.AddComponent<Outline>();
            outline.effectColor = PhosphorDim;
            outline.effectDistance = new Vector2(1f, -1f);
            Text text = CreateText(plate.rectTransform, "Label", label, 13, TextAnchor.MiddleCenter, labelColor);
            Stretch(text.rectTransform);
            return button;
        }

        private static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Four L-shaped terminal corner brackets; a negative inset pushes them outside the rect.</summary>
        private static void AddCornerBrackets(RectTransform target, float length, float thickness, Color color, float inset)
        {
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2, i / 2);
                var inward = new Vector2(corner.x < 0.5f ? 1f : -1f, corner.y < 0.5f ? 1f : -1f);
                AddBracketBar(target, corner, inward * inset, new Vector2(length, thickness), color);
                AddBracketBar(target, corner, inward * inset, new Vector2(thickness, length), color);
            }
        }

        private static void AddBracketBar(RectTransform parent, Vector2 corner, Vector2 offset, Vector2 size, Color color)
        {
            Image bar = CreateImage(parent, "CornerBracket", color);
            bar.raycastTarget = false;
            RectTransform rect = bar.rectTransform;
            rect.anchorMin = rect.anchorMax = corner;
            rect.pivot = corner;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }
    }
}
