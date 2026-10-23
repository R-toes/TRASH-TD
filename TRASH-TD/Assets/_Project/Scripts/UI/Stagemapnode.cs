using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TrashTD.Data;

namespace TrashTD.UI
{
    /// <summary>
    /// One stage marker on the stage-select map, built in code by <see cref="Build"/>: a pixel "target" (core badge
    /// with the stage number, rotating dashed ring, radar pings, lock-on ticks) and a callout card joined by a leader
    /// line (STAGE NN, map name, status). Placed by a normalized position (0-1 across the map art, origin top-left)
    /// so it stays on its location at any 16:9 resolution. Everything animates on unscaled time: a boot-in intro
    /// (<see cref="PlayIntro"/>), idle spin and pings, a lock-on while hovered or focused, and the selected state.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class StageMapNode : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        // Palette (terminal phosphor, matches MainMenuController)
        private static readonly Color Phosphor = new Color(0.22f, 0.91f, 0.50f, 1f);
        private static readonly Color PhosphorBright = new Color(0.62f, 1.00f, 0.78f, 1f);
        private static readonly Color PhosphorDim = new Color(0.22f, 0.91f, 0.50f, 0.45f);
        private static readonly Color SelectedGlow = new Color(0.98f, 1.00f, 0.65f, 1f);
        private static readonly Color LockedGrey = new Color(0.38f, 0.44f, 0.50f, 0.85f);
        private static readonly Color CoreFill = new Color(0.03f, 0.09f, 0.055f, 0.95f);
        private static readonly Color CardFill = new Color(0.010f, 0.030f, 0.020f, 0.88f);
        private static readonly Color CardFillHot = new Color(0.030f, 0.090f, 0.055f, 0.95f);
        private static readonly Color DarkInk = new Color(0.02f, 0.08f, 0.04f, 1f);
        private static readonly Color NameColor = new Color(0.90f, 0.97f, 0.92f, 1f);

        // Layout in native units (scaled by StageMapLayout through the visual root)
        private const float CoreSize = 56f;
        private const float SpinSize = 88f;
        private const float PingSize = 64f;
        private const float TickRestDistance = 54f;
        private const float TickLockDistance = 41f;
        private const float DiagonalStart = 26f;   // where the leader line leaves the ring (45°)
        private const float DiagonalLength = 28f;
        private const float FlatLength = 18f;
        private const float CardHeight = 52f;
        private const float CardPadding = 14f;

        // Intro timeline (seconds after the delay)
        private const float CoreInEnd = 0.30f;
        private const float DiagonalIn = 0.25f;
        private const float FlatIn = 0.38f;
        private const float CardIn = 0.45f;
        private const float TypeIn = 0.55f;
        private const float IntroLength = 1.1f;

        [Header("Stage")]
        [SerializeField] private StageData stage;
        [SerializeField] private int stageNumber = 1;

        [Header("Placement (0-1 over the map art, origin = top-left)")]
        [SerializeField] private Vector2 normalizedPosition = new Vector2(0.5f, 0.5f);
        [Tooltip("Clickable size of the marker in UI units at 1080p.")]
        [SerializeField] private Vector2 nativeSize = new Vector2(64f, 64f);

        private RectTransform visual;
        private Image coreRing;
        private Image coreFill;
        private Text numberText;
        private RectTransform spinRing;
        private Image spinImage;
        private readonly Image[] pings = new Image[2];
        private Image burst;
        private readonly Image[] ticks = new Image[4];
        private GameObject lockBadge;
        private CanvasGroup calloutGroup;
        private RectTransform diagonalLine;
        private RectTransform flatLine;
        private Image diagonalImage;
        private Image flatImage;
        private RectTransform card;
        private Image cardImage;
        private Image cardAccent;
        private readonly List<Image> cardBrackets = new List<Image>();
        private Text tagText;
        private Text nameText;
        private Text statusText;
        private Image statusDot;
        private string stageName = string.Empty;
        private float side = 1f; // +1: callout to the right, -1: to the left
        private Vector2 cardRestPosition;

        private Button button;
        private bool isSelected;
        private bool isLocked;
        private bool isHovered;
        private bool isFocused;
        private float hot;
        private float selected;
        private float spinAngle;
        private float introTime = IntroLength;
        private bool introBurstDone = true;
        private float burstTime = 1f;

        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

        /// <summary>Raised when the marker is clicked (never while locked).</summary>
        public event Action<StageMapNode> Clicked;

        /// <summary>Raised when the marker gains (true) or loses (false) the lock-on (hover or keyboard focus), locked or not.</summary>
        public event Action<StageMapNode, bool> HoverChanged;

        public StageData Stage => stage;
        public bool IsLocked => isLocked;
        public int StageNumber => stageNumber;
        public bool IsSelectedStage => isSelected;
        /// <summary>Position over the map art, 0-1 with the origin at the top-left.</summary>
        public Vector2 NormalizedPosition => normalizedPosition;

        // ─────────────────────────────────────────────────────
        //  Construction
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Builds the marker and its callout. <paramref name="calloutOnLeft"/> puts the card on the left of the marker
        /// (used near the right edge of the map). Needs a Button on this GameObject for clicks.
        /// </summary>
        public void Build(StageData stageData, int number, Vector2 normPos, bool locked, bool calloutOnLeft)
        {
            stage = stageData;
            stageNumber = number;
            normalizedPosition = normPos;
            isLocked = locked;
            side = calloutOnLeft ? -1f : 1f;
            stageName = stage != null && !string.IsNullOrEmpty(stage.mapName)
                ? stage.mapName.ToUpperInvariant()
                : "STAGE " + number.ToString("00");

            button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(HandleClick);

            var visualGo = new GameObject("Visual", typeof(RectTransform));
            visualGo.transform.SetParent(transform, false);
            visual = (RectTransform)visualGo.transform;
            visual.anchorMin = visual.anchorMax = visual.pivot = new Vector2(0.5f, 0.5f);
            visual.sizeDelta = nativeSize;

            BuildCallout();
            BuildMarker();
            RefreshStaticState();
        }

        private void BuildMarker()
        {
            for (int i = 0; i < pings.Length; i++)
            {
                pings[i] = CreateImage("Ping" + i, visual, GetRingSprite(32, 1, 0), PingSize);
            }
            burst = CreateImage("Burst", visual, GetRingSprite(32, 1, 0), PingSize);
            burst.color = Color.clear;

            spinImage = CreateImage("SpinRing", visual, GetRingSprite(44, 1, 16), SpinSize);
            spinRing = spinImage.rectTransform;

            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i] = CreateImage("Tick" + i, visual, null, 0f);
                ticks[i].rectTransform.sizeDelta = new Vector2(12f, 4f);
                ticks[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
            }

            coreFill = CreateImage("CoreFill", visual, GetDiscSprite(24), 48f);
            coreRing = CreateImage("CoreRing", visual, GetRingSprite(28, 3, 0), CoreSize);

            numberText = CreateText("Number", visual, stageNumber.ToString(), 20, TextAnchor.MiddleCenter);
            numberText.rectTransform.sizeDelta = new Vector2(CoreSize, CoreSize);

            // Padlock badge on the lower right of locked markers
            lockBadge = new GameObject("LockBadge", typeof(RectTransform));
            lockBadge.transform.SetParent(visual, false);
            var lockRect = (RectTransform)lockBadge.transform;
            lockRect.anchoredPosition = new Vector2(20f, -20f);
            Image lockBack = CreateImage("LockBack", lockRect, GetDiscSprite(14), 28f);
            lockBack.color = new Color(0.06f, 0.07f, 0.09f, 1f);
            Image lockIcon = CreateImage("LockIcon", lockRect, LoadUiSprite("UI/icons/icon_lock"), 18f);
            lockIcon.color = LockedGrey;
            lockIcon.preserveAspect = true;
        }

        private void BuildCallout()
        {
            var calloutGo = new GameObject("Callout", typeof(RectTransform), typeof(CanvasGroup));
            calloutGo.transform.SetParent(visual, false);
            calloutGroup = calloutGo.GetComponent<CanvasGroup>();
            calloutGroup.interactable = false;
            var callout = (RectTransform)calloutGo.transform;
            callout.sizeDelta = Vector2.zero;

            float elbowX = side * (DiagonalStart + DiagonalLength * 0.7071f);
            float elbowY = DiagonalStart + DiagonalLength * 0.7071f;

            diagonalImage = CreateImage("LeaderDiagonal", callout, null, 0f);
            diagonalLine = diagonalImage.rectTransform;
            diagonalLine.pivot = new Vector2(0f, 0.5f);
            diagonalLine.anchoredPosition = new Vector2(side * DiagonalStart, DiagonalStart);
            diagonalLine.localRotation = Quaternion.Euler(0f, 0f, side > 0f ? 45f : 135f);
            diagonalLine.sizeDelta = new Vector2(DiagonalLength, 2f);

            flatImage = CreateImage("LeaderFlat", callout, null, 0f);
            flatLine = flatImage.rectTransform;
            flatLine.pivot = new Vector2(side > 0f ? 0f : 1f, 0.5f);
            flatLine.anchoredPosition = new Vector2(elbowX, elbowY);
            flatLine.sizeDelta = new Vector2(FlatLength, 2f);

            // Card, pivoted on its inner edge so it unfolds outwards from the leader line
            cardImage = CreateImage("Card", callout, null, 0f);
            cardImage.raycastTarget = true; // clicking the card opens the stage too
            card = cardImage.rectTransform;
            card.pivot = new Vector2(side > 0f ? 0f : 1f, 0.5f);
            cardRestPosition = new Vector2(elbowX + side * FlatLength, elbowY);
            card.anchoredPosition = cardRestPosition;

            cardAccent = CreateImage("Accent", card, null, 0f);
            cardAccent.rectTransform.anchorMin = new Vector2(side > 0f ? 0f : 1f, 0f);
            cardAccent.rectTransform.anchorMax = new Vector2(side > 0f ? 0f : 1f, 1f);
            cardAccent.rectTransform.pivot = new Vector2(side > 0f ? 0f : 1f, 0.5f);
            cardAccent.rectTransform.sizeDelta = new Vector2(4f, 0f);
            cardAccent.rectTransform.anchoredPosition = Vector2.zero;

            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2, i / 2);
                cardBrackets.Add(CreateBracket(corner, new Vector2(10f, 2f)));
                cardBrackets.Add(CreateBracket(corner, new Vector2(2f, 10f)));
            }

            tagText = CreateText("Tag", card, "STAGE " + stageNumber.ToString("00"), 10, TextAnchor.UpperLeft);
            PlaceInCard(tagText.rectTransform, new Vector2(0f, 1f), new Vector2(CardPadding, -9f));

            statusText = CreateText("Status", card, "ONLINE", 9, TextAnchor.UpperRight);
            PlaceInCard(statusText.rectTransform, new Vector2(1f, 1f), new Vector2(-CardPadding + 2f, -10f));
            statusDot = CreateImage("StatusDot", card, null, 0f);
            statusDot.rectTransform.anchorMin = statusDot.rectTransform.anchorMax = new Vector2(1f, 1f);
            statusDot.rectTransform.pivot = new Vector2(1f, 1f);
            statusDot.rectTransform.sizeDelta = new Vector2(6f, 6f);

            nameText = CreateText("Name", card, stageName, 13, TextAnchor.LowerLeft);
            PlaceInCard(nameText.rectTransform, new Vector2(0f, 0f), new Vector2(CardPadding, 9f));

            // Width from the longest line (PressStart2P glyphs are as wide as the font size)
            float tagLine = tagText.preferredWidth + 16f + 14f + MeasureStatusWidth();
            float width = Mathf.Max(170f, Mathf.Max(nameText.preferredWidth, tagLine) + CardPadding * 2f + 4f);
            card.sizeDelta = new Vector2(Mathf.Ceil(width), CardHeight);
        }

        private float MeasureStatusWidth()
        {
            // Widest status word, so the card doesn't resize when the status changes
            float widest = 0f;
            foreach (string word in new[] { "ONLINE", "LOCKED", "SELECTED" })
            {
                statusText.text = word;
                widest = Mathf.Max(widest, statusText.preferredWidth);
            }
            return widest;
        }

        private Image CreateBracket(Vector2 corner, Vector2 size)
        {
            Image bar = CreateImage("CornerBracket", card, null, 0f);
            RectTransform rect = bar.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = corner;
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return bar;
        }

        private static void PlaceInCard(RectTransform rect, Vector2 corner, Vector2 offset)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = corner;
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(320f, 16f);
        }

        // ─────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────

        /// <summary>Called by StageMapLayout whenever the map art changes size.</summary>
        public void ApplyLayout(float pixelScale)
        {
            var rt = (RectTransform)transform;
            rt.anchorMin = rt.anchorMax = new Vector2(normalizedPosition.x, 1f - normalizedPosition.y);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = nativeSize * pixelScale;
            if (visual != null) visual.localScale = Vector3.one * pixelScale;
        }

        /// <summary>Boot-in: core pops, leader line draws out, card unfolds and the name types out.</summary>
        public void PlayIntro(float delay)
        {
            introTime = -Mathf.Max(0f, delay);
            introBurstDone = false;
            hot = 0f;
            Update();
        }

        public void SetSelected(bool value)
        {
            isSelected = value;
            if (value) burstTime = 0f;
            RefreshStaticState();
        }

        public void SetLocked(bool locked)
        {
            isLocked = locked;
            RefreshStaticState();
        }

        // Hovering selects the marker, so mouse and keyboard share one lock-on (like a terminal cursor);
        // HoverChanged follows the selection.
        public void OnPointerEnter(PointerEventData eventData)
        {
            isHovered = true;
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != gameObject)
            {
                EventSystem.current.SetSelectedGameObject(gameObject);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
        }

        public void OnSelect(BaseEventData eventData)
        {
            isFocused = true;
            HoverChanged?.Invoke(this, true);
        }

        public void OnDeselect(BaseEventData eventData)
        {
            isFocused = false;
            HoverChanged?.Invoke(this, false);
        }

        private void HandleClick()
        {
            if (isLocked)
            {
                GetComponent<MenuButtonFeedback>()?.Wobble();
                return;
            }

            burstTime = 0f;
            Clicked?.Invoke(this);
        }

        private void OnDisable()
        {
            isHovered = false;
            isFocused = false;
            hot = 0f;
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(HandleClick);
        }

        // ─────────────────────────────────────────────────────
        //  Animation
        // ─────────────────────────────────────────────────────

        private void RefreshStaticState()
        {
            // Locked markers stay interactable so a click can play the denied wobble (HandleClick)
            if (lockBadge != null) lockBadge.SetActive(isLocked);
            if (statusText != null) statusText.text = isLocked ? "LOCKED" : (isSelected ? "SELECTED" : "ONLINE");
            if (statusDot != null && statusText != null)
            {
                statusDot.rectTransform.anchoredPosition = new Vector2(-CardPadding + 2f - statusText.preferredWidth - 6f, -11f);
            }
        }

        private void Update()
        {
            if (visual == null) return;

            float dt = Time.unscaledDeltaTime;
            float time = Time.unscaledTime;

            if (introTime < IntroLength) introTime += dt;
            float coreK = EaseOutBack(Mathf.Clamp01(introTime / CoreInEnd));
            float spinK = Mathf.Clamp01((introTime - 0.1f) / 0.3f);
            float diagonalK = EaseOutCubic(Mathf.Clamp01((introTime - DiagonalIn) / 0.14f));
            float flatK = EaseOutCubic(Mathf.Clamp01((introTime - FlatIn) / 0.10f));
            float cardK = EaseOutCubic(Mathf.Clamp01((introTime - CardIn) / 0.22f));
            float typeK = Mathf.Clamp01((introTime - TypeIn) / 0.4f);
            if (!introBurstDone && introTime >= CoreInEnd)
            {
                introBurstDone = true;
                burstTime = 0f;
            }

            float k = 1f - Mathf.Exp(-14f * dt);
            hot = Mathf.Lerp(hot, (isHovered || isFocused) && introTime >= CardIn ? 1f : 0f, k);
            selected = Mathf.Lerp(selected, isSelected ? 1f : 0f, k);

            UpdateMarker(dt, time, coreK, spinK);
            UpdateCallout(time, diagonalK, flatK, cardK, typeK);
        }

        private void UpdateMarker(float dt, float time, float coreK, float spinK)
        {
            Color ringColor = isLocked ? LockedGrey : Color.Lerp(Color.Lerp(Phosphor, PhosphorBright, hot), SelectedGlow, selected);

            // Core: pops in, swells on lock-on, breathes while selected
            float breathe = 0.04f * selected * Mathf.Sin(time * 4f);
            float coreScale = coreK * (1f + 0.08f * hot + breathe);
            coreRing.rectTransform.localScale = Vector3.one * coreScale;
            coreFill.rectTransform.localScale = Vector3.one * coreScale;
            numberText.rectTransform.localScale = Vector3.one * coreScale;
            coreRing.color = ringColor;
            coreFill.color = isLocked ? new Color(0.06f, 0.07f, 0.09f, 0.92f) : Color.Lerp(CoreFill, Phosphor, selected);
            numberText.color = isLocked ? LockedGrey : Color.Lerp(PhosphorBright, DarkInk, selected);
            if (lockBadge.activeSelf) lockBadge.transform.localScale = Vector3.one * coreK;

            // Dashed ring: slow idle spin that speeds up on lock-on; still when locked
            spinAngle -= dt * (isLocked ? 0f : 16f + 150f * hot + 40f * selected);
            spinRing.localRotation = Quaternion.Euler(0f, 0f, spinAngle);
            Color spinColor = isLocked ? new Color(LockedGrey.r, LockedGrey.g, LockedGrey.b, 0.35f) : ringColor;
            spinColor.a *= spinK * (isLocked ? 1f : 0.45f + 0.55f * Mathf.Max(hot, selected));
            spinImage.color = spinColor;

            // Lock-on ticks slide in from the dashed ring
            float distance = Mathf.Lerp(TickRestDistance, TickLockDistance, hot);
            for (int i = 0; i < ticks.Length; i++)
            {
                float angle = i * Mathf.PI * 0.5f;
                ticks[i].rectTransform.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                Color tickColor = isLocked ? LockedGrey : ringColor;
                tickColor.a *= spinK * (0.2f + 0.8f * Mathf.Max(hot, selected * 0.6f));
                ticks[i].color = tickColor;
            }

            // Radar pings: two staggered rings; faster and brighter when selected; none when locked
            float period = Mathf.Lerp(2.4f, 1.2f, selected);
            float phase = stageNumber * 0.37f;
            for (int i = 0; i < pings.Length; i++)
            {
                float p = Mathf.Repeat((time + phase) / period + i * 0.5f, 1f);
                pings[i].rectTransform.localScale = Vector3.one * (0.9f + 1.5f * p);
                Color c = ringColor;
                c.a = isLocked ? 0f : (1f - p) * (1f - p) * (0.35f + 0.35f * selected + 0.2f * hot) * spinK;
                pings[i].color = c;
            }

            // One-shot burst on intro, click and selection
            if (burstTime < 0.5f)
            {
                burstTime += dt;
                float b = Mathf.Clamp01(burstTime / 0.5f);
                burst.rectTransform.localScale = Vector3.one * (1f + 1.8f * EaseOutCubic(b));
                Color c = isLocked ? LockedGrey : PhosphorBright;
                c.a = (1f - b) * 0.9f;
                burst.color = c;
            }
            else if (burst.color.a > 0f)
            {
                burst.color = Color.clear;
            }
        }

        private void UpdateCallout(float time, float diagonalK, float flatK, float cardK, float typeK)
        {
            Color lineColor = isLocked
                ? new Color(LockedGrey.r, LockedGrey.g, LockedGrey.b, 0.6f)
                : Color.Lerp(Color.Lerp(PhosphorDim, PhosphorBright, hot), SelectedGlow, selected);

            diagonalLine.sizeDelta = new Vector2(DiagonalLength * diagonalK, 2f);
            flatLine.sizeDelta = new Vector2(FlatLength * flatK, 2f);
            diagonalImage.color = lineColor;
            flatImage.color = lineColor;
            calloutGroup.alpha = Mathf.Clamp01(diagonalK * 4f);

            // Card unfolds outwards and nudges away from the marker on lock-on
            card.localScale = new Vector3(cardK, 1f, 1f);
            card.anchoredPosition = cardRestPosition + new Vector2(side * 4f * hot, 0f);
            cardImage.color = Color.Lerp(CardFill, CardFillHot, Mathf.Max(hot, selected * 0.6f));
            cardAccent.color = lineColor;
            foreach (Image bracket in cardBrackets) bracket.color = lineColor;

            tagText.color = isLocked ? LockedGrey : Color.Lerp(Phosphor, SelectedGlow, selected);
            nameText.color = isLocked ? new Color(0.50f, 0.55f, 0.62f, 0.75f) : Color.Lerp(NameColor, Color.white, hot);

            // Name types out during the intro
            int visible = Mathf.RoundToInt(typeK * stageName.Length);
            nameText.text = visible >= stageName.Length ? stageName : stageName.Substring(0, visible) + "_";

            statusText.color = isLocked ? LockedGrey : (isSelected ? SelectedGlow : Phosphor);
            bool dotOn = isLocked || isSelected || Mathf.Repeat(time + stageNumber * 0.21f, 1.2f) < 0.8f;
            statusDot.color = dotOn ? statusText.color : Color.clear;
        }

        private static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        // ─────────────────────────────────────────────────────
        //  Building helpers + procedural pixel sprites
        // ─────────────────────────────────────────────────────

        private static Image CreateImage(string name, Transform parent, Sprite sprite, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            if (size > 0f) image.rectTransform.sizeDelta = new Vector2(size, size);
            return image;
        }

        private static Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Sprite LoadUiSprite(string path)
        {
            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                Sprite[] all = Resources.LoadAll<Sprite>(path);
                if (all != null && all.Length > 0) sprite = all[0];
            }
            if (sprite != null && sprite.texture != null) sprite.texture.filterMode = FilterMode.Point;
            return sprite;
        }

        /// <summary>
        /// White pixel ring, drawn small and scaled up with point filtering so it matches the map's chunky pixels.
        /// <paramref name="dashes"/> &gt; 0 cuts it into that many dashes.
        /// </summary>
        private static Sprite GetRingSprite(int size, int thickness, int dashes)
        {
            string key = $"ring{size}_{thickness}_{dashes}";
            if (SpriteCache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            float center = (size - 1) * 0.5f;
            float outer = size * 0.5f;
            float inner = outer - thickness;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy) + 0.5f;
                    bool on = distance <= outer && distance > inner;
                    if (on && dashes > 0)
                    {
                        float angle = Mathf.Atan2(dy, dx) + Mathf.PI;
                        on = Mathf.FloorToInt(angle / (Mathf.PI * 2f) * dashes * 2f) % 2 == 0;
                    }
                    tex.SetPixel(x, y, on ? Color.white : Color.clear);
                }
            }
            tex.Apply();

            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            SpriteCache[key] = sprite;
            return sprite;
        }

        private static Sprite GetDiscSprite(int size)
        {
            string key = $"disc{size}";
            if (SpriteCache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    tex.SetPixel(x, y, Mathf.Sqrt(dx * dx + dy * dy) + 0.5f <= radius ? Color.white : Color.clear);
                }
            }
            tex.Apply();

            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            SpriteCache[key] = sprite;
            return sprite;
        }
    }
}
