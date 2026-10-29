using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TrashTD.Audio;
using TrashTD.Data;

namespace TrashTD.UI
{
    public enum TutorialDialogueLayout
    {
        Gameplay, // Big, leaning in from the bottom-right corner; the task strip sits on the HUD's bottom-right panel
        Compact,  // Bottom-left and small, under the draft screen's card column; stays on screen
        StageMap  // As Gameplay; the task strip sits above the stage map's QA SANDBOX button
    }

    /// <summary>
    /// Coalesce's side of the tutorial: a big pixel-scaled Coalesce leaning in from the bottom-right corner, close
    /// to the camera (feet and shell past the screen edges), with a terminal-style speech bubble beside the head.
    /// Lines type out with a voice blip per character, and Coalesce's sheet animates only while text is typing.
    ///
    /// Coalesce is only on screen while talking: it slides in from the right for a line and back out when the
    /// bubble hides. Each line starts with a small pose (<see cref="TutorialEmote"/>). A task line types out,
    /// holds a moment, then Coalesce leaves and a pinned ▶ TASK strip keeps the instruction up. The compact (draft)
    /// layout and lines with choices keep Coalesce on screen.
    ///
    /// Talk lines dim the screen and catch every click: a click (or Space) finishes the line, the next one
    /// advances (<see cref="Advanced"/>). Task lines leave the game clickable and wait for the player. With the
    /// spotlight on (stage map), the dim is darker and leaves a circle of normal brightness around whatever is
    /// being pointed at. A pulsing hazard-yellow frame can point at any UI element on any canvas. Unscaled time
    /// throughout.
    /// </summary>
    public sealed class TutorialDialogueUI : MonoBehaviour
    {
        private const int CanvasSortingOrder = 250; // Above the HUD (100) and the draft (200), under results (300)
        private const int AboveResultsSortingOrder = 310; // Talking over the defeat screen
        private const float CharsPerSecond = 42f;
        private const int BlipEveryChars = 2;
        private const float BlipVolume = 0.35f;
        private const float DimAlpha = 0.28f;
        private const float SpotlightDimAlpha = 0.62f;
        private const float DimFadeSpeed = 2.5f; // Alpha per second
        private const float SpeakingFrameSpeedup = 1.25f; // Talking frames run a little faster than the idle sheet

        private static readonly Color Phosphor = new Color(0.22f, 0.91f, 0.50f, 1f);
        private static readonly Color PhosphorDim = new Color(0.22f, 0.91f, 0.50f, 0.45f);
        private static readonly Color PanelColor = new Color(0.012f, 0.034f, 0.022f, 0.96f);
        private static readonly Color HeaderColor = new Color(0.030f, 0.090f, 0.055f, 1f);
        private static readonly Color BodyColor = new Color(0.80f, 0.95f, 0.85f, 1f);
        private static readonly Color HazardYellow = new Color(0.98f, 0.80f, 0.18f, 1f);
        private static readonly Color HazardInk = new Color(0.07f, 0.06f, 0.03f, 1f);

        // Default (gameplay and stage map): the 32 px sheet at 20x, its bottom-right corner pushed past the screen's
        // so the feet and shell are cut off. Coalesce faces left, into the screen; the bubble sits left of the head
        // with its tail at the snout. Coalesce + bubble cover ~950x430 of 1920x1080 (~20% of the screen).
        private const float SpriteSize = 640f;
        private const float SpriteSunk = 150f;     // Below the bottom edge
        private const float SpriteOverhang = 150f; // Past the right edge
        private const float FeetPivotY = 0.15f;    // Emotes rotate and squash about the feet
        private const float BubbleWidth = 540f;
        private const float BubbleHeight = 230f;
        private const float BubbleRight = 410f;    // From the right edge: just left of the snout
        private const float BubbleBottom = 190f;
        private const float TailY = 80f;           // Up the bubble's right edge: level with the snout
        // Compact: bottom-left, under the draft screen's card column
        private const float CompactSpriteSize = 160f;
        private const float CompactBubbleWidth = 660f;
        private const float CompactBubbleHeight = 170f;
        // The task strip stands where Coalesce used to: on the HUD's bottom-right panel (~331 px tall). At 16:9 the
        // tutorial grid's empty right columns start at x ~1421, so it stays off the lane.
        private const float TaskStripWidth = 470f;
        private const float TaskStripHeight = 96f;
        private const float GameplayRight = 24f;
        private const float GameplayTaskBottom = 345f;
        private const float GameplaySkipTop = 104f; // Under the HUD's top bar (90 px)
        // Stage map: QA SANDBOX and the ESC hint fill the bottom-right corner up to y ~118; the stage progress
        // readout sits top-right down to y ~-74
        private const float StageMapRight = 60f;
        private const float StageMapTaskBottom = 130f;
        private const float StageMapSkipTop = 86f;

        private const float SlideSeconds = 0.32f;
        private const float TaskHoldBase = 1.4f;     // A typed task line stays this long (plus per character)...
        private const float TaskHoldPerChar = 0.01f; // ...before Coalesce leaves and the task strip takes over
        private const float StripFadeSeconds = 0.2f;
        private const float SpotPadding = 46f;
        private const float SpotMinRadius = 80f;
        private const float SpotIrisStartRadius = 1300f; // A new spotlight closes in from this radius
        private const float SpotFollowSharpness = 9f;

        private Canvas canvas;
        private RectTransform root;
        private Button catcher;
        private Image dim;
        private RectTransform spotlight;
        private readonly List<Image> spotParts = new List<Image>(); // Hole, left, right, top, bottom
        private RectTransform presenter;
        private RectTransform speakerRig;
        private RectTransform speaker;
        private Image speakerImage;
        private RectTransform bubble;
        private Text nameText;
        private Text tagText;
        private Image tagPlate;
        private Text bodyText;
        private Text promptText;
        private RectTransform tail;
        private CanvasGroup taskStrip;
        private RectTransform taskStripRect;
        private Text taskText;
        private Vector2 taskStripHome;
        private Button skipButton;
        private RectTransform choiceRow;
        private Button primaryChoice;
        private Button secondaryChoice;
        private Action primaryAction;
        private Action secondaryAction;
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

        private float slide = 1f; // 0 = Coalesce and the bubble on screen, 1 = off it
        private float slideDistance;
        private float slideSign = 1f; // Off to the right (+1) or, in the compact layout, the left (-1)
        private float taskHold;
        private TutorialEmote emote;
        private bool emotePending;
        private float emoteTime = -1f;
        private float dimAlpha;
        private bool spotlightEnabled;
        private bool spotPlaced;
        private Vector2 spotCenter;
        private float spotRadius;

        private static AudioClip synthBlip;
        private static Sprite spotHoleSprite;

        /// <summary>A click (or Space) on a finished talk line.</summary>
        public event Action Advanced;
        /// <summary>The player confirmed SKIP TUTORIAL.</summary>
        public event Action SkipConfirmed;

        public bool IsTyping => bubbleVisible && typedChars < fullText.Length;
        /// <summary>A talk line or the skip prompt is up: the director keeps the game frozen.</summary>
        public bool IsBlockingGame => (bubbleVisible && !isTask) || IsSkipConfirmOpen;
        public bool IsSkipConfirmOpen => skipConfirm != null && skipConfirm.activeSelf;

        private bool HasChoices => primaryAction != null || secondaryAction != null;
        /// <summary>Coalesce can hand a task line over to the task strip and leave (not in the draft or with choices).</summary>
        private bool CanLeaveOnTask => layout != TutorialDialogueLayout.Compact && !HasChoices;
        private bool IsHandingOverTask => bubbleVisible && isTask && CanLeaveOnTask && !IsTyping &&
                                          taskHold >= TaskHoldBase + fullText.Length * TaskHoldPerChar;
        private bool IsPresenterIn => slide <= 0f;

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

        /// <summary>
        /// Starts a line: Coalesce slides in if needed, strikes the pose, then types it. A talk line dims the screen
        /// and waits for a click; a task line doesn't, and hands over to the task strip once read.
        /// </summary>
        public void Say(string text, bool task, TutorialEmote pose = TutorialEmote.Nod)
        {
            fullText = string.IsNullOrEmpty(text) ? string.Empty : text.Trim();
            typedChars = 0f;
            lastBlipChar = 0;
            isTask = task;
            bubbleVisible = !string.IsNullOrEmpty(fullText);
            taskHold = 0f;
            emote = pose;
            emotePending = bubbleVisible;

            bubble.gameObject.SetActive(bubbleVisible);
            HideChoices();
            bodyText.text = string.Empty;
            promptText.text = string.Empty;
            tagText.text = task ? "▶ TASK" : "// COMMS";
            tagText.color = task ? HazardInk : PhosphorDim;
            tagPlate.color = task ? HazardYellow : Color.clear;
            if (task) taskText.text = fullText;
            ApplyBlocking();
        }

        /// <summary>
        /// Two buttons under the current line (e.g. TRY AGAIN / SKIP TUTORIAL), shown once it has typed out. The
        /// SKIP TUTORIAL tag hides meanwhile, and Coalesce stays on screen; the next Say clears them.
        /// </summary>
        public void ShowChoices(string primaryLabel, Action onPrimary, string secondaryLabel, Action onSecondary)
        {
            primaryAction = onPrimary;
            secondaryAction = onSecondary;
            primaryChoice.GetComponentInChildren<Text>().text = primaryLabel;
            secondaryChoice.GetComponentInChildren<Text>().text = secondaryLabel;
            skipButton.gameObject.SetActive(false);
            bodyText.rectTransform.offsetMin = new Vector2(18f, 66f);
        }

        private void HideChoices()
        {
            primaryAction = null;
            secondaryAction = null;
            choiceRow.gameObject.SetActive(false);
            skipButton.gameObject.SetActive(true);
            bodyText.rectTransform.offsetMin = new Vector2(18f, 30f);
        }

        /// <summary>Draws Coalesce above the results screen (the tutorial's defeat).</summary>
        public void BringAboveResults()
        {
            canvas.sortingOrder = AboveResultsSortingOrder;
        }

        /// <summary>Ends the line: Coalesce slides off screen with the bubble (e.g. while a wave runs).</summary>
        public void HideBubble()
        {
            bubbleVisible = false;
            fullText = string.Empty;
            emotePending = false;
            ApplyBlocking();
        }

        /// <summary>A new layout snaps Coalesce off screen; the next line slides in from that layout's side.</summary>
        public void SetLayout(TutorialDialogueLayout value)
        {
            if (layout == value) return;
            layout = value;
            slide = 1f;
            ApplyLayout();
        }

        /// <summary>Darker dim on talk lines, with a circle of normal brightness around the pointed-at element.</summary>
        public void SetSpotlight(bool enabled)
        {
            spotlightEnabled = enabled;
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
        //  Update: sliding, typing, animation, input, pointer, dim
        // ─────────────────────────────────────────────────────

        private void Update()
        {
            if (!root.gameObject.activeSelf) return;
            float dt = Time.unscaledDeltaTime;

            UpdatePresenter(dt);
            UpdateTyping(dt);
            AnimateSpeaker(dt);
            UpdateEmote(dt);

            if (!IsSkipConfirmOpen && bubbleVisible && !isTask && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                HandleClick();
            }

            UpdatePointer();
            UpdateDim(dt);
            UpdateTaskStrip(dt);
        }

        /// <summary>Slides Coalesce and the bubble in while there's a line to say, and out otherwise.</summary>
        private void UpdatePresenter(float dt)
        {
            bool wanted = bubbleVisible && !IsHandingOverTask;
            slide = Mathf.MoveTowards(slide, wanted ? 0f : 1f, dt / SlideSeconds);
            // Squared: eases out on the way in, eases in on the way out
            presenter.anchoredPosition = new Vector2(slideSign * slideDistance * slide * slide, 0f);

            if (!bubbleVisible && slide >= 1f && bubble.gameObject.activeSelf) bubble.gameObject.SetActive(false);
        }

        private void UpdateTyping(float dt)
        {
            if (!bubbleVisible) return;

            // Nothing types until Coalesce is in place; the pose plays as the line starts
            if (!IsPresenterIn) return;
            if (emotePending)
            {
                emotePending = false;
                emoteTime = 0f;
            }

            if (typedChars < fullText.Length)
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
            else
            {
                bodyText.text = fullText;
                if (HasChoices != choiceRow.gameObject.activeSelf) choiceRow.gameObject.SetActive(HasChoices);
                bool blink = Mathf.Repeat(Time.unscaledTime, 0.9f) < 0.6f;
                promptText.text = !isTask && blink ? "▼" : string.Empty;
                if (isTask) taskHold += dt;
            }
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
            if (!TryGetTargetBounds(pointTarget, out Vector2 min, out Vector2 max))
            {
                if (pointer.gameObject.activeSelf) pointer.gameObject.SetActive(false);
                return;
            }

            float grow = 10f + 4f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f));
            pointer.anchoredPosition = (min + max) * 0.5f;
            pointer.sizeDelta = max - min + Vector2.one * grow * 2f;
            if (!pointer.gameObject.activeSelf) pointer.gameObject.SetActive(true);

            float alpha = Mathf.Lerp(0.55f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f));
            foreach (Image part in pointerParts) part.color = new Color(HazardYellow.r, HazardYellow.g, HazardYellow.b, alpha);
        }

        /// <summary>A UI element's (on any canvas) bounds in this canvas's root space; false if it isn't showing.</summary>
        private bool TryGetTargetBounds(RectTransform target, out Vector2 min, out Vector2 max)
        {
            min = max = Vector2.zero;
            if (target == null || !target.gameObject.activeInHierarchy) return false;

            Canvas targetCanvas = target.GetComponentInParent<Canvas>();
            if (targetCanvas == null) return false;
            targetCanvas = targetCanvas.rootCanvas;
            Camera targetCamera = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCanvas.worldCamera;
            Camera ownCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector3 corner in corners)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(targetCamera, corner);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, ownCamera, out Vector2 local);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
            return true;
        }

        private void ApplyBlocking()
        {
            catcher.gameObject.SetActive(bubbleVisible && !isTask);
        }

        // ─────────────────────────────────────────────────────
        //  Dim and spotlight
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Talk lines fade the dim in. With the spotlight on and something pointed at, the dim is drawn as a ring of
        /// five pieces (a soft round hole plus four bars around it) that closes in on the target and follows it.
        /// </summary>
        private void UpdateDim(float dt)
        {
            bool block = bubbleVisible && !isTask;
            float target = !block ? 0f : spotlightEnabled ? SpotlightDimAlpha : DimAlpha;
            dimAlpha = Mathf.MoveTowards(dimAlpha, target, dt * DimFadeSpeed);

            bool spot = spotlightEnabled && dimAlpha > 0f && TryGetTargetBounds(pointTarget, out Vector2 min, out Vector2 max) && PlaceSpot(min, max, dt);
            if (!spot) spotPlaced = false;

            dim.color = new Color(0f, 0f, 0f, spot ? 0f : dimAlpha);
            if (spotlight.gameObject.activeSelf != spot) spotlight.gameObject.SetActive(spot);
            if (!spot) return;
            foreach (Image part in spotParts) part.color = new Color(0f, 0f, 0f, dimAlpha);
        }

        private bool PlaceSpot(Vector2 min, Vector2 max, float dt)
        {
            Vector2 center = (min + max) * 0.5f;
            float radius = Mathf.Max(SpotMinRadius, Mathf.Max(max.x - min.x, max.y - min.y) * 0.5f + SpotPadding);
            if (!spotPlaced)
            {
                spotPlaced = true;
                spotCenter = center;
                spotRadius = SpotIrisStartRadius;
            }
            float follow = 1f - Mathf.Exp(-SpotFollowSharpness * dt);
            spotCenter = Vector2.Lerp(spotCenter, center, follow);
            spotRadius = Mathf.Lerp(spotRadius, radius, follow);

            Rect area = root.rect;
            float c = spotCenter.x, m = spotCenter.y, r = spotRadius;
            SetSpotPart(0, spotCenter, new Vector2(r * 2f, r * 2f));
            SetSpotPart(1, new Vector2((area.xMin + c - r) * 0.5f, area.center.y), new Vector2(c - r - area.xMin, area.height));
            SetSpotPart(2, new Vector2((c + r + area.xMax) * 0.5f, area.center.y), new Vector2(area.xMax - c - r, area.height));
            SetSpotPart(3, new Vector2(c, (m + r + area.yMax) * 0.5f), new Vector2(r * 2f, area.yMax - m - r));
            SetSpotPart(4, new Vector2(c, (area.yMin + m - r) * 0.5f), new Vector2(r * 2f, m - r - area.yMin));
            return true;
        }

        private void SetSpotPart(int index, Vector2 center, Vector2 size)
        {
            RectTransform rect = spotParts[index].rectTransform;
            rect.anchoredPosition = center;
            rect.sizeDelta = Vector2.Max(size, Vector2.zero);
        }

        /// <summary>White outside a soft-edged circle, clear inside; tinted by the dim's colour.</summary>
        private static Sprite GetSpotHoleSprite()
        {
            if (spotHoleSprite != null) return spotHoleSprite;

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TutorialSpotHole",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
                    byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, distance)));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            spotHoleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            return spotHoleSprite;
        }

        // ─────────────────────────────────────────────────────
        //  Emotes
        // ─────────────────────────────────────────────────────

        private void UpdateEmote(float dt)
        {
            Vector2 offset = Vector2.zero;
            float angle = 0f;
            Vector2 scale = Vector2.one;
            if (emoteTime >= 0f)
            {
                emoteTime += dt;
                if (!Pose(emote, emoteTime, ref offset, ref angle, ref scale)) emoteTime = -1f;
            }

            // Poses are authored at full size, leaning toward the bubble: left of Coalesce, or right in the compact layout
            float toward = layout == TutorialDialogueLayout.Compact ? 1f : -1f;
            float unit = speakerRig.sizeDelta.y / SpriteSize;
            speaker.anchoredPosition = new Vector2(offset.x * toward, offset.y) * unit;
            speaker.localRotation = Quaternion.Euler(0f, 0f, -angle * toward);
            speaker.localScale = new Vector3(scale.x, scale.y, 1f);
        }

        /// <summary>
        /// The pose <paramref name="t"/> seconds into an emote: offset (x toward the bubble), angle (positive leans
        /// toward the bubble) and squash. False once the emote is over.
        /// </summary>
        private static bool Pose(TutorialEmote pose, float t, ref Vector2 offset, ref float angle, ref Vector2 scale)
        {
            switch (pose)
            {
                case TutorialEmote.Happy:
                {
                    // One hop, stretched in the air, squashed on landing
                    if (t >= 0.6f) return false;
                    if (t < 0.42f)
                    {
                        float h = Mathf.Sin(Mathf.PI * t / 0.42f);
                        offset = new Vector2(0f, 40f * h);
                        angle = 3f * Mathf.Sin(2f * Mathf.PI * t / 0.42f);
                        scale = new Vector2(1f - 0.04f * h, 1f + 0.05f * h);
                    }
                    else
                    {
                        float s = Mathf.Sin(Mathf.PI * (t - 0.42f) / 0.18f);
                        scale = new Vector2(1f + 0.07f * s, 1f - 0.07f * s);
                    }
                    return true;
                }
                case TutorialEmote.Curious:
                {
                    // Tilts the head toward the bubble and holds it
                    if (t >= 0.85f) return false;
                    float w = Envelope(t, 0.18f, 0.55f, 0.85f);
                    offset = new Vector2(10f * w, 6f * w);
                    angle = 10f * w;
                    return true;
                }
                case TutorialEmote.Worried:
                {
                    // Shrinks back from the bubble with a shiver
                    if (t >= 0.7f) return false;
                    float w = Envelope(t, 0.12f, 0.45f, 0.7f);
                    offset = new Vector2(-14f * w + 3f * Mathf.Sin(t * 70f) * w, -8f * w);
                    angle = -6f * w;
                    scale = new Vector2(1f + 0.04f * w, 1f - 0.06f * w);
                    return true;
                }
                case TutorialEmote.Excited:
                {
                    // Two quick hops, rocking each way
                    if (t >= 0.8f) return false;
                    float h = Mathf.Sin(Mathf.PI * Mathf.Repeat(t, 0.4f) / 0.4f);
                    offset = new Vector2(0f, 30f * h);
                    angle = (t < 0.4f ? 4f : -4f) * h;
                    scale = new Vector2(1f - 0.05f * h, 1f + 0.07f * h);
                    return true;
                }
                case TutorialEmote.Point:
                {
                    // Leans in toward the screen
                    if (t >= 0.6f) return false;
                    float w = Envelope(t, 0.15f, 0.35f, 0.6f);
                    offset = new Vector2(28f * w, 4f * w);
                    angle = 7f * w;
                    return true;
                }
                default:
                {
                    // Nod: a small dip toward the bubble
                    if (t >= 0.45f) return false;
                    float b = Mathf.Sin(Mathf.PI * t / 0.45f);
                    offset = new Vector2(6f * b, -16f * b);
                    angle = 4f * b;
                    return true;
                }
            }
        }

        /// <summary>0 → 1 by <paramref name="rise"/>, held until <paramref name="hold"/>, back to 0 by <paramref name="end"/>.</summary>
        private static float Envelope(float t, float rise, float hold, float end)
        {
            if (t < rise) return Mathf.SmoothStep(0f, 1f, t / rise);
            if (t < hold) return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, (t - hold) / (end - hold));
        }

        // ─────────────────────────────────────────────────────
        //  Task strip
        // ─────────────────────────────────────────────────────

        /// <summary>Once Coalesce has handed a task line over, the strip keeps it on screen until the next line.</summary>
        private void UpdateTaskStrip(float dt)
        {
            taskStrip.alpha = Mathf.MoveTowards(taskStrip.alpha, IsHandingOverTask ? 1f : 0f, dt / StripFadeSeconds);
            bool active = taskStrip.alpha > 0f;
            if (taskStrip.gameObject.activeSelf != active) taskStrip.gameObject.SetActive(active);
            taskStripRect.anchoredPosition = taskStripHome + new Vector2(0f, -12f * (1f - taskStrip.alpha));
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
            BuildSpotlight();

            // Talk lines catch every click on the screen
            Image catcherImage = CreateImage(root, "ClickCatcher", Color.clear);
            Stretch(catcherImage.rectTransform);
            catcher = catcherImage.gameObject.AddComponent<Button>();
            catcher.transition = Selectable.Transition.None;
            catcher.navigation = new Navigation { mode = Navigation.Mode.None };
            catcher.onClick.AddListener(HandleClick);

            // Coalesce and the bubble slide in and out together
            presenter = CreateRect("Presenter", root);
            Stretch(presenter);

            frames = speakerFrames != null && speakerFrames.Length > 0 ? speakerFrames : null;
            frameDuration = Mathf.Max(0.06f, speakerFrameDuration);
            speakerRig = CreateRect("CoalesceRig", presenter);
            speakerImage = CreateImage(speakerRig, "Coalesce", frames != null ? Color.white : Color.clear);
            speakerImage.sprite = frames != null ? frames[0] : null;
            speakerImage.preserveAspect = true;
            speakerImage.raycastTarget = false;
            speaker = speakerImage.rectTransform;
            Stretch(speaker);
            speaker.pivot = new Vector2(0.5f, FeetPivotY);

            BuildBubble(speakerName);
            BuildTaskStrip();
            pointer = BuildPointer(root); // Above Coalesce, so nothing hides it
            BuildSkip();

            ApplyLayout();
            HideBubble();
            HideChoices();
            PointAt(null);
            presenter.anchoredPosition = new Vector2(slideSign * slideDistance, 0f);
            bubble.gameObject.SetActive(false);
        }

        private void BuildSpotlight()
        {
            spotlight = CreateRect("Spotlight", root);
            Stretch(spotlight);

            Image hole = CreateImage(spotlight, "Hole", Color.clear);
            hole.sprite = GetSpotHoleSprite();
            spotParts.Add(hole);
            spotParts.Add(CreateImage(spotlight, "Left", Color.clear));
            spotParts.Add(CreateImage(spotlight, "Right", Color.clear));
            spotParts.Add(CreateImage(spotlight, "Top", Color.clear));
            spotParts.Add(CreateImage(spotlight, "Bottom", Color.clear));
            foreach (Image part in spotParts)
            {
                part.raycastTarget = false;
                RectTransform rect = part.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
            }
            spotlight.gameObject.SetActive(false);
        }

        private void BuildBubble(string speakerName)
        {
            Image panel = CreateImage(presenter, "SpeechBubble", PanelColor);
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

            nameText = CreateText(headerRect, "Name", "◆ " + (string.IsNullOrEmpty(speakerName) ? "COALESCE" : speakerName.ToUpperInvariant()), 14, TextAnchor.MiddleLeft, Phosphor);
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

            bodyText = CreateText(bubble, "Body", string.Empty, 17, TextAnchor.UpperLeft, BodyColor);
            bodyText.lineSpacing = 1.4f;
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Truncate;
            bodyText.resizeTextForBestFit = true;
            bodyText.resizeTextMinSize = 10;
            bodyText.resizeTextMaxSize = 17;
            Stretch(bodyText.rectTransform, 18f, 18f, 48f, 30f);

            promptText = CreateText(bubble, "Prompt", string.Empty, 13, TextAnchor.LowerRight, Phosphor);
            Stretch(promptText.rectTransform, 18f, 12f, 18f, 8f);

            // Choice row along the bubble's bottom edge
            choiceRow = CreateRect("Choices", bubble);
            choiceRow.anchorMin = new Vector2(0f, 0f);
            choiceRow.anchorMax = new Vector2(1f, 0f);
            choiceRow.pivot = new Vector2(0.5f, 0f);
            choiceRow.offsetMin = new Vector2(18f, 14f);
            choiceRow.offsetMax = new Vector2(-18f, 56f);

            primaryChoice = CreateButton(choiceRow, "PrimaryChoice", string.Empty, HazardInk, HazardYellow);
            RectTransform primaryRect = primaryChoice.GetComponent<RectTransform>();
            primaryRect.anchorMin = new Vector2(0f, 0f);
            primaryRect.anchorMax = new Vector2(0.5f, 1f);
            primaryRect.offsetMin = Vector2.zero;
            primaryRect.offsetMax = new Vector2(-6f, 0f);
            primaryChoice.onClick.AddListener(() => Choose(primaryAction));

            secondaryChoice = CreateButton(choiceRow, "SecondaryChoice", string.Empty, Phosphor, HeaderColor);
            RectTransform secondaryRect = secondaryChoice.GetComponent<RectTransform>();
            secondaryRect.anchorMin = new Vector2(0.5f, 0f);
            secondaryRect.anchorMax = new Vector2(1f, 1f);
            secondaryRect.offsetMin = new Vector2(6f, 0f);
            secondaryRect.offsetMax = Vector2.zero;
            secondaryChoice.onClick.AddListener(() => Choose(secondaryAction));
        }

        private void Choose(Action action)
        {
            if (action == null) return;
            AudioManager.Instance?.PlaySfx(SfxId.UiConfirm);
            Action chosen = action;
            HideChoices();
            chosen();
        }

        private void BuildTaskStrip()
        {
            Image panel = CreateImage(root, "TaskStrip", PanelColor);
            panel.raycastTarget = false;
            taskStripRect = panel.rectTransform;
            taskStrip = panel.gameObject.AddComponent<CanvasGroup>();
            taskStrip.blocksRaycasts = false;
            taskStrip.interactable = false;
            taskStrip.alpha = 0f;
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = PhosphorDim;
            outline.effectDistance = new Vector2(2f, -2f);
            AddCornerBrackets(taskStripRect, 12f, 3f, HazardYellow, -4f);

            // Hazard stripe down the left edge
            Image stripe = CreateImage(taskStripRect, "Stripe", HazardYellow);
            stripe.raycastTarget = false;
            RectTransform stripeRect = stripe.rectTransform;
            stripeRect.anchorMin = new Vector2(0f, 0f);
            stripeRect.anchorMax = new Vector2(0f, 1f);
            stripeRect.pivot = new Vector2(0f, 0.5f);
            stripeRect.sizeDelta = new Vector2(6f, 0f);
            stripeRect.anchoredPosition = Vector2.zero;

            Text tag = CreateText(taskStripRect, "Tag", "▶ TASK", 11, TextAnchor.UpperLeft, HazardYellow);
            Stretch(tag.rectTransform, 20f, 16f, 10f, TaskStripHeight - 28f);

            taskText = CreateText(taskStripRect, "Body", string.Empty, 14, TextAnchor.UpperLeft, BodyColor);
            taskText.lineSpacing = 1.3f;
            taskText.horizontalOverflow = HorizontalWrapMode.Wrap;
            taskText.verticalOverflow = VerticalWrapMode.Truncate;
            taskText.resizeTextForBestFit = true;
            taskText.resizeTextMinSize = 10;
            taskText.resizeTextMaxSize = 14;
            Stretch(taskText.rectTransform, 20f, 16f, 30f, 10f);

            taskStrip.gameObject.SetActive(false);
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
            RectTransform skipRect = skipButton.GetComponent<RectTransform>();
            if (layout == TutorialDialogueLayout.Compact)
            {
                // Bottom-left: Coalesce, bubble to the right, skip above Coalesce; slides off to the left
                float size = CompactSpriteSize;
                Anchor(speakerRig, new Vector2(0f, 0f), new Vector2(40f, 24f), new Vector2(size, size));
                Anchor(bubble, new Vector2(0f, 0f), new Vector2(40f + size + 22f, 24f), new Vector2(CompactBubbleWidth, CompactBubbleHeight));
                tail.anchorMin = tail.anchorMax = new Vector2(0f, 0f);
                tail.anchoredPosition = new Vector2(0f, size * 0.5f);
                Anchor(skipRect, new Vector2(0f, 0f), new Vector2(40f, 24f + size + 10f), new Vector2(176f, 32f));
                slideSign = -1f;
                slideDistance = 40f + size + 22f + CompactBubbleWidth + 40f;
                return;
            }

            // Bottom-right, close to the camera: bubble left of the head; slides off to the right. Skip and the task
            // strip stay put while Coalesce comes and goes.
            bool map = layout == TutorialDialogueLayout.StageMap;
            float right = map ? StageMapRight : GameplayRight;
            Anchor(speakerRig, new Vector2(1f, 0f), new Vector2(SpriteOverhang, -SpriteSunk), new Vector2(SpriteSize, SpriteSize));
            Anchor(bubble, new Vector2(1f, 0f), new Vector2(-BubbleRight, BubbleBottom), new Vector2(BubbleWidth, BubbleHeight));
            tail.anchorMin = tail.anchorMax = new Vector2(1f, 0f);
            tail.anchoredPosition = new Vector2(0f, TailY);
            Anchor(skipRect, new Vector2(1f, 1f), new Vector2(-right, -(map ? StageMapSkipTop : GameplaySkipTop)), new Vector2(176f, 32f));
            Anchor(taskStripRect, new Vector2(1f, 0f), new Vector2(-right, map ? StageMapTaskBottom : GameplayTaskBottom), new Vector2(TaskStripWidth, TaskStripHeight));
            taskStripHome = taskStripRect.anchoredPosition;
            slideSign = 1f;
            slideDistance = BubbleRight + BubbleWidth + 60f;
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
