using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TrashTD.Audio;
using TrashTD.Core.GameLoop;
using TrashTD.Data;
using TrashTD.Systems;

namespace TrashTD.UI
{
    /// <summary>
    /// Runtime-built fullscreen card draft overlay.
    /// Left: 3 portrait-focused cards (portrait, name, rarity stars). Cards are dealt face-down
    /// after a shuffle animation and only flip to their front when the player clicks them.
    /// Right: a detail panel for the selected card (name, class / position / damage-type chips,
    /// stats and passive/ability text).
    /// Player selects a card, then clicks Confirm to add it to their deck.
    /// Includes a Reroll button (up to 3 rerolls per stage); a reroll flips the cards back,
    /// shuffles and deals a fresh face-down hand.
    /// Integrates with GameManager phase system: shown during CardPick phase.
    /// Visual polish: fade-in, shuffle + deal, flip reveal, rarity colors, selection feedback,
    /// hover/press feedback (MenuButtonFeedback), keyboard shortcuts and optional UI sounds.
    /// </summary>
    public class CardDraftOverlayUI : MonoBehaviour
    {
        // --- Colors ---
        private static readonly Color BG_OVERLAY = new Color(0.02f, 0.03f, 0.05f, 1f);
        private static readonly Color CARD_BG = new Color(0.08f, 0.10f, 0.14f, 1f);
        private static readonly Color CARD_SELECTED = new Color(0.15f, 0.25f, 0.45f, 1f);
        private static readonly Color CARD_HOVER = new Color(0.12f, 0.16f, 0.24f, 1f);
        private static readonly Color PORTRAIT_BG = new Color(0.06f, 0.07f, 0.10f, 1f);
        private static readonly Color PANEL_BG = new Color(0.045f, 0.055f, 0.075f, 0.96f);
        private static readonly Color TILE_BG = new Color(0.07f, 0.085f, 0.115f, 1f);
        private static readonly Color CONFIRM_COLOR = new Color(0.18f, 0.55f, 0.34f, 1f);
        private static readonly Color CONFIRM_DISABLED = new Color(0.15f, 0.15f, 0.18f, 1f);
        private static readonly Color REROLL_COLOR = new Color(0.55f, 0.45f, 0.15f, 1f);
        private static readonly Color REROLL_DISABLED = new Color(0.20f, 0.18f, 0.12f, 1f);
        private static readonly Color STAR_COLOR = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color STAT_LABEL_COLOR = new Color(0.6f, 0.65f, 0.7f, 1f);
        private static readonly Color STAT_VALUE_COLOR = new Color(0.95f, 0.95f, 0.98f, 1f);
        private static readonly Color CLASS_COLOR = new Color(0.5f, 0.7f, 0.9f, 1f);
        private static readonly Color DECK_FULL_COLOR = new Color(0.8f, 0.3f, 0.3f, 1f);
        private static readonly Color SKIP_COLOR = new Color(0.5f, 0.5f, 0.55f, 1f);
        private static readonly Color ACCENT_COLOR = new Color(0.18f, 0.82f, 0.45f, 1f);
        private static readonly Color ACCENT_BRIGHT = new Color(0.30f, 0.95f, 0.58f, 1f);
        private static readonly Color MELEE_COLOR = new Color(0.95f, 0.60f, 0.25f, 1f);
        private static readonly Color RANGED_COLOR = new Color(0.35f, 0.78f, 0.92f, 1f);
        private static readonly Color BACK_BG = new Color(0.045f, 0.06f, 0.09f, 1f);

        // Range preview tiles use the same colours as the in-game placement preview (StageBootstrapper).
        private static readonly Color RANGE_TILE_COLOR = new Color(0.15f, 1f, 0.28f, 1f);
        private static readonly Color SELF_TILE_COLOR = new Color(0.15f, 0.85f, 1f, 1f);
        private static readonly Color EMPTY_TILE_COLOR = new Color(1f, 1f, 1f, 0.22f);

        // --- Layout (reference resolution 1920x1080, positions relative to screen center) ---
        private const float CardWidth = 360f;
        private const float CardHeight = 640f;
        private const float CardSpacing = 30f;
        private const float PortraitMargin = 16f;
        private const float PortraitTop = 18f;
        private const float PortraitHeight = 452f;
        private const float PortraitPadding = 12f;
        private const float ColumnCenterX = -285f;   // card column (left side)
        private const float PanelCenterX = 650f;     // detail panel (right side)
        private const float PanelWidth = 500f;
        private const float ContentCenterY = 10f;

        // Range preview box (bottom-left of the detail panel)
        private const float RangeBoxWidth = 156f;
        private const float RangeBoxHeight = 140f;
        private const float RangeBoxPadding = 10f;
        private const float RangeTileGap = 2f;
        private const float MaxRangeTileSize = 28f;

        // --- Shuffle / flip timings (seconds) ---
        private const float StackScale = 0.88f;       // cards shrink a little while stacked
        private const float GatherTime = 0.32f;       // cards slide into a stack
        private const float GatherFlipTime = 0.24f;  // revealed cards flip back during the gather
        private const float RiffleOutTime = 0.13f;
        private const float RiffleInTime = 0.15f;
        private const float RiffleSpread = 200f;
        private const float DealTime = 0.42f;
        private const float DealStagger = 0.11f;
        private const float FlipHalfOut = 0.11f;
        private const float FlipHalfIn = 0.22f;

        // Pixel-art portraits are scaled by whole numbers (when they fit) inside this area.
        private static readonly Vector2 PortraitFitArea = new Vector2(
            CardWidth - PortraitMargin * 2f - PortraitPadding * 2f,
            PortraitHeight - PortraitPadding * 2f);

        // Optional data members probed on OperatorData by name (see FindStringMember).
        // Add your own field names here if they differ.
        private static readonly string[] PassiveMemberNames =
        {
            "passiveDescription", "passiveText", "passive", "abilityDescription", "abilityText",
            "ability", "traitDescription", "trait", "skillDescription", "description", "shortDescription"
        };

        private static readonly string[] DamageTypeMemberNames =
        {
            "damageType", "attackType", "damageKind", "attackDamageType"
        };

        [Header("UI Sounds (optional)")]
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioClip clickClip;
        [SerializeField] private AudioClip confirmClip;
        [SerializeField] private AudioClip rerollClip;
        [SerializeField] private AudioClip shuffleClip;
        [SerializeField] private AudioClip dealClip;
        [SerializeField] private AudioClip flipClip;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.7f;

        [Header("Card Flip")]
        [Tooltip("If on, clicking a face-down card flips it AND selects it. If off, the first click only reveals it and a second click selects it.")]
        [SerializeField] private bool selectOnReveal = true;

        // --- References ---
        private Canvas canvas;
        private GameObject overlayRoot;
        private CanvasGroup overlayGroup;
        private CardDraftSystem draftSystem;
        private PlayerDeck playerDeck;
        private GameManager gameManager;
        private GameplayHUDUI gameplayHudUI;
        private bool waitingForEnvironmentBriefing;

        // --- Card UI ---
        private GameObject[] cardPanels = new GameObject[3];
        private RectTransform[] cardRects = new RectTransform[3];
        private Button[] cardButtonsArr = new Button[3];
        private Image[] cardBackgrounds = new Image[3];
        private Image[] portraitBgImages = new Image[3];
        private Image[] portraitImages = new Image[3];
        private Text[] nameTexts = new Text[3];
        private Text[] rarityTexts = new Text[3];

        // --- Card polish ---
        private CanvasGroup[] cardGroups = new CanvasGroup[3];
        private MenuButtonFeedback[] cardFeedbacks = new MenuButtonFeedback[3];
        private Image[] cardAccents = new Image[3];
        private Outline[] portraitFrames = new Outline[3];
        private Image[] classBadgeBackgrounds = new Image[3];
        private Image[] classBadgeIcons = new Image[3];
        private Text[] portraitFallbackTexts = new Text[3];
        private Text[] selectHintTexts = new Text[3];

        // --- Card faces (front = the operator card, back = face-down side) ---
        private GameObject[] cardFronts = new GameObject[3];
        private GameObject[] cardBacks = new GameObject[3];
        private Image[] cardFlashes = new Image[3];
        private Color[] cardRarityColors = new Color[3];
        private Vector2[] cardSlots = new Vector2[3];
        private bool[] cardRevealed = new bool[3];
        private Coroutine[] flipRoutines = new Coroutine[3];
        private readonly List<DraftCard> pendingOffer = new List<DraftCard>();
        private bool isShuffling;
        private bool freshDraft;     // true until the first offer of a draft has been applied
        private bool offerApplied;   // false while a shuffle is still waiting to fill the cards

        // --- Detail panel ---
        private sealed class Chip
        {
            public Image bg;
            public Image icon;
            public Outline outline;
            public Text text;
        }

        private RectTransform detailsPanelRect;
        private Image detailAccent;
        private GameObject detailContentRoot;
        private RectTransform detailContentRect;
        private CanvasGroup detailContentGroup;
        private GameObject detailEmptyRoot;
        private Text detailNameText;
        private Text detailStarsText;
        private Chip detailClassChip;
        private Chip detailPositionChip;
        private Chip detailDamageChip;
        private Text detailRoleText;
        private Text detailHpText;
        private Text detailAtkText;
        private Text detailDefText;
        private Text detailResText;
        private Text detailBlockText;
        private Text detailIntervalText;
        private Text detailPassiveLabel;
        private Text detailPassiveText;
        private RectTransform detailRangeGrid;
        private Sprite rangeTileSprite;
        private Coroutine detailsRoutine;

        // --- Buttons ---
        private Button confirmButton;
        private Text confirmButtonText;
        private MenuButtonFeedback confirmFeedback;
        private Button rerollButton;
        private Text rerollButtonText;
        private MenuButtonFeedback rerollFeedback;
        private Button skipButton;
        private MenuButtonFeedback skipFeedback;
#if UNITY_EDITOR || DEBUG
        private Button developerPickButton;
        private GameObject developerPickerRoot;
        private Transform developerPickerList;
        private bool developerPickerPopulated;
#endif

        // --- State ---
        private int selectedCardIndex = -1;
        private Text titleText;
        private Text deckCountText;
        private Text waveInfoText;
        private bool overlayVisible;

        // --- Minimize (view the map while drafting) ---
        private bool minimized;
        private GameObject restoreBar;
        private bool pickConfirmed; // one card per draft; reset when the next draft opens

        /// <summary>True while the draft is minimized to the map & deck view, where operators can be deployed.</summary>
        public bool IsMinimized => minimized;

        // --- Animation ---
        private RectTransform[] introRects;
        private Vector2[] introTargets;
        private Vector2[] introOffsets;
        private float[] introDelays;
        private Coroutine introRoutine;
        private Coroutine dealRoutine;
        private Coroutine hideRoutine;
        private AudioSource sfxSource;

        public static void HideAllForMenuTransition()
        {
            CardDraftOverlayUI[] overlays = FindObjectsByType<CardDraftOverlayUI>(
                FindObjectsInactive.Include);

            foreach (CardDraftOverlayUI overlay in overlays)
            {
                overlay.HideDraftOverlayImmediately();
            }
        }

        // ============================
        // Lifecycle
        // ============================

        private void Awake()
        {
            draftSystem = FindAnyObjectByType<CardDraftSystem>();
            playerDeck = FindAnyObjectByType<PlayerDeck>();
            gameManager = FindAnyObjectByType<GameManager>();
            gameplayHudUI = FindAnyObjectByType<GameplayHUDUI>();
            if (gameplayHudUI != null)
                gameplayHudUI.EnvironmentBriefingDismissed += HandleEnvironmentBriefingDismissed;

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            if (hoverClip == null) hoverClip = AudioManager.Instance?.GetClip(SfxId.UiHover);
            if (clickClip == null) clickClip = AudioManager.Instance?.GetClip(SfxId.UiClick);
            if (confirmClip == null) confirmClip = AudioManager.Instance?.GetClip(SfxId.UiConfirm);
            if (rerollClip == null) rerollClip = AudioManager.Instance?.GetClip(SfxId.UiReroll) ?? clickClip;
            // Card animation sounds fall back to the hover blip the overlay used before they had their own files.
            if (shuffleClip == null) shuffleClip = AudioManager.Instance?.GetClip(SfxId.UiCardShuffle) ?? hoverClip;
            if (dealClip == null) dealClip = AudioManager.Instance?.GetClip(SfxId.UiCardDeal) ?? hoverClip;
            if (flipClip == null) flipClip = AudioManager.Instance?.GetClip(SfxId.UiCardFlip) ?? hoverClip;

            if (draftSystem != null)
            {
                draftSystem.OnCardsOffered += HandleCardsOffered;
            }

            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        }

        private void Start()
        {
            BuildOverlay();
            overlayRoot.SetActive(false);

            if (playerDeck != null)
            {
                playerDeck.OnDeckChanged += HandleDeckChanged;
            }

            if (gameManager != null)
            {
                gameManager.OnPhaseChanged += HandlePhaseChanged;

                if (gameManager.CurrentState == GamePlayState.Playing && gameManager.CurrentPhase == StagePhase.CardPick)
                {
                    ShowDraftOverlay();
                }
            }
        }

        private void Update()
        {
            if (overlayRoot == null) return;

            // This canvas sorts above the HUD's pause menu, so hide it (restore bar included) while paused.
            // Otherwise the draft stays clickable during pause, and a confirm adds the card without leaving Card Pick.
            bool paused = IsGamePaused();
            if (canvas != null && canvas.enabled == paused) canvas.enabled = !paused;
            if (paused) return;

            if (minimized)
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard != null && keyboard.mKey.wasPressedThisFrame) SetMinimized(false);
                return;
            }

            if (!overlayVisible) return;

            // Confirm button breathes while a card can be confirmed
            if (confirmFeedback != null && confirmButton != null)
            {
                confirmFeedback.restScale = confirmButton.interactable
                    ? 1f + 0.02f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f))
                    : 1f;
            }

            HandleKeyboardShortcuts();
        }

        private void OnDestroy()
        {
            if (gameManager != null)
            {
                gameManager.OnPhaseChanged -= HandlePhaseChanged;
            }

            if (playerDeck != null)
            {
                playerDeck.OnDeckChanged -= HandleDeckChanged;
            }

            if (draftSystem != null)
            {
                draftSystem.OnCardsOffered -= HandleCardsOffered;
            }

            if (gameplayHudUI != null)
                gameplayHudUI.EnvironmentBriefingDismissed -= HandleEnvironmentBriefingDismissed;

            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        }

#if UNITY_EDITOR || DEBUG
        public bool IsDeveloperPickerOpen => developerPickerRoot != null && developerPickerRoot.activeSelf;
        public void CloseDeveloperPicker()
        {
            if (developerPickerRoot != null) developerPickerRoot.SetActive(false);
        }
#endif

        private void HandleKeyboardShortcuts()
        {
            if (gameplayHudUI != null && gameplayHudUI.IsEnvironmentBriefingActive) return;

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;

#if UNITY_EDITOR || DEBUG
            if (developerPickerRoot != null && developerPickerRoot.activeSelf)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    developerPickerRoot.SetActive(false);
                    gameplayHudUI?.ConsumeEscapeKey();
                }
                return;
            }
#endif

            if (gameplayHudUI != null && gameplayHudUI.IsPaused) return;

            if (keyboard.digit1Key.wasPressedThisFrame) SelectCardByKey(0);
            if (keyboard.digit2Key.wasPressedThisFrame) SelectCardByKey(1);
            if (keyboard.digit3Key.wasPressedThisFrame) SelectCardByKey(2);

            if ((keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) && confirmButton.interactable)
            {
                OnConfirmClicked();
            }

            if (keyboard.rKey.wasPressedThisFrame && rerollButton.interactable)
            {
                OnRerollClicked();
            }

            if (keyboard.mKey.wasPressedThisFrame)
            {
                SetMinimized(true);
            }
        }

        private void SelectCardByKey(int index)
        {
            if (isShuffling) return;
            if (index < 0 || index >= cardPanels.Length || cardPanels[index] == null || !cardPanels[index].activeSelf) return;

            PlaySfx(clickClip, 1f);
            if (cardRevealed[index]) cardFeedbacks[index].Punch();
            OnCardClicked(index);
        }

        // ============================
        // Phase Integration
        // ============================

        private void HandlePhaseChanged(StagePhase phase)
        {
            if (IsActiveGameplayCardPick())
            {
                ShowDraftOverlay();
            }
            else if (gameManager == null || SceneManager.GetActiveScene() != gameObject.scene)
            {
                HideDraftOverlayImmediately();
            }
            else
            {
                HideDraftOverlay();
            }
        }

        private void HandleEnvironmentBriefingDismissed()
        {
            if (!waitingForEnvironmentBriefing || !IsActiveGameplayCardPick()) return;
            waitingForEnvironmentBriefing = false;
            ShowDraftOverlay();
        }

        private void HandleActiveSceneChanged(Scene previousScene, Scene activeScene)
        {
            if (activeScene != gameObject.scene)
            {
                HideDraftOverlayImmediately();
            }
        }

        private void ShowDraftOverlay()
        {
            if (!IsActiveGameplayCardPick())
            {
                HideDraftOverlayImmediately();
                return;
            }

            if (gameplayHudUI != null && gameplayHudUI.ShouldBlockDraftOverlay)
            {
                waitingForEnvironmentBriefing = true;
                HideDraftOverlayImmediately();
                return;
            }

            waitingForEnvironmentBriefing = false;
            pickConfirmed = false;
            ResetMinimized();

#if UNITY_EDITOR || DEBUG
            if (developerPickerRoot != null) developerPickerRoot.SetActive(false);
#endif
            selectedCardIndex = -1;

            if (hideRoutine != null)
            {
                StopCoroutine(hideRoutine);
                hideRoutine = null;
            }

            // Start every draft with the cards stacked face-down; the shuffle deals them out.
            ResetCardsForNewDraft();

            overlayRoot.SetActive(true);
            overlayVisible = true;
            PlayIntro();

            // Generate a fresh draft offer and immediately populate the card UI
            if (draftSystem != null)
            {
                var offer = draftSystem.GenerateDraftOffer();
                HandleCardsOffered(offer);
            }

            UpdateConfirmButton();
            UpdateDeckCount();
            UpdateWaveInfo();
        }

        private bool IsGamePaused()
        {
            return (gameplayHudUI != null && gameplayHudUI.IsPaused)
                || (gameManager != null && gameManager.CurrentState == GamePlayState.Paused);
        }

        private bool IsActiveGameplayCardPick()
        {
            return gameManager != null
                && gameManager.CurrentState == GamePlayState.Playing
                && gameManager.CurrentPhase == StagePhase.CardPick
                && SceneManager.GetActiveScene() == gameObject.scene
                && gameManager.gameObject.scene == gameObject.scene;
        }

        private void HideDraftOverlay()
        {
            // A minimized overlay is already inactive; still close it so its keys stop working.
            ResetMinimized();
            if (overlayRoot == null || !overlayRoot.activeSelf)
            {
                overlayVisible = false;
                return;
            }

            overlayVisible = false;

            if (!isActiveAndEnabled)
            {
                overlayRoot.SetActive(false);
                return;
            }

            if (hideRoutine != null) StopCoroutine(hideRoutine);
            hideRoutine = StartCoroutine(HideRoutine());
        }

        private void HideDraftOverlayImmediately()
        {
            ResetMinimized();
            overlayVisible = false;
            if (hideRoutine != null)
            {
                StopCoroutine(hideRoutine);
                hideRoutine = null;
            }

            if (overlayRoot != null) overlayRoot.SetActive(false);
        }

        public void HideForSandbox()
        {
            HideDraftOverlayImmediately();
        }

        // ============================
        // Card Display
        // ============================

        private void HandleCardsOffered(IReadOnlyList<DraftCard> cards)
        {
            // The offer event can fire before the overlay has been built.
            if (overlayRoot == null) return;

            selectedCardIndex = -1;

            // The new offer is stored and only written onto the cards once they are face-down
            // (see ShuffleDealRoutine), so a reroll never reveals the new hand early.
            pendingOffer.Clear();
            for (int i = 0; i < cards.Count; i++) pendingOffer.Add(cards[i]);

            ApplySelectionVisuals();
            UpdateDetailsPanel(false);
            StartShuffleDeal();

            UpdateConfirmButton();
            UpdateRerollButton();
            UpdateDeckCount();
        }

        /// <summary>Writes the pending offer onto the three cards (they must be face-down).</summary>
        private void ApplyOfferToCards()
        {
            for (int i = 0; i < 3; i++)
            {
                DraftCard card = i < pendingOffer.Count ? pendingOffer[i] : null;
                bool hasCard = card != null && card.operatorData != null;
                cardPanels[i].SetActive(hasCard);
                cardRevealed[i] = false;
                SetFaceVisual(i, false);

                if (!hasCard) continue;

                var opData = card.operatorData;
                int starCount = Mathf.Clamp((int)card.rarity, 1, 5);
                Color rarityColor = GetRarityColor(starCount);
                cardRarityColors[i] = rarityColor;

                // Name + rarity (the only text on the card)
                nameTexts[i].text = opData.operatorName;
                rarityTexts[i].text = BuildStarString(starCount);
                rarityTexts[i].color = rarityColor;

                // Rarity tint: top stripe, portrait backdrop/frame
                cardAccents[i].color = rarityColor;
                portraitBgImages[i].color = Color.Lerp(PORTRAIT_BG, rarityColor, 0.10f);
                portraitFrames[i].effectColor = new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.6f);
                classBadgeBackgrounds[i].color = GetClassColor(opData.operatorClass);
                classBadgeIcons[i].sprite = GameplayHUDUI.GetClassIconSprite(opData.operatorClass);

                // The hover glow stays neutral while face-down so it can't give away the rarity.
                cardFeedbacks[i].glowColor = new Color(ACCENT_COLOR.r, ACCENT_COLOR.g, ACCENT_COLOR.b, 0.95f);

                SetPortrait(i, opData);
            }

            RecomputeSlots();
        }

        /// <summary>Slot position of each visible card, centered as a row (replaces the layout group).</summary>
        private void RecomputeSlots()
        {
            int count = 0;
            for (int i = 0; i < 3; i++) if (cardPanels[i].activeSelf) count++;

            int rank = 0;
            for (int i = 0; i < 3; i++)
            {
                if (!cardPanels[i].activeSelf) continue;
                cardSlots[i] = new Vector2((rank - (count - 1) * 0.5f) * (CardWidth + CardSpacing), 0f);
                rank++;
            }
        }

        /// <summary>
        /// Shows the portrait as large as the frame allows while staying centered.
        /// Small pixel-art sprites are scaled by whole numbers (when they fit) so they stay crisp.
        /// </summary>
        private void SetPortrait(int index, OperatorData opData)
        {
            Image image = portraitImages[index];
            Sprite sprite = opData.portrait;

            string initial = string.IsNullOrEmpty(opData.operatorName) ? "?" : opData.operatorName.Substring(0, 1).ToUpper();
            portraitFallbackTexts[index].text = initial;
            portraitFallbackTexts[index].enabled = sprite == null;

            if (sprite == null)
            {
                image.sprite = null;
                image.enabled = false;
                return;
            }

            // Pixel art should not be bilinear-smoothed when scaled up.
            // (For a permanent fix, set the sprite's Filter Mode to Point in its import settings.)
            if (sprite.texture != null && sprite.texture.filterMode != FilterMode.Point)
            {
                sprite.texture.filterMode = FilterMode.Point;
            }

            image.enabled = true;
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = false;

            Vector2 pixelSize = sprite.rect.size;
            float fit = Mathf.Min(PortraitFitArea.x / pixelSize.x, PortraitFitArea.y / pixelSize.y);
            float scale = fit >= 1f ? Mathf.Floor(fit) : fit;
            image.rectTransform.sizeDelta = pixelSize * scale;
        }

        private void ApplySelectionVisuals()
        {
            bool anySelected = selectedCardIndex >= 0;

            for (int i = 0; i < 3; i++)
            {
                if (cardFeedbacks[i] == null) continue;

                bool selected = i == selectedCardIndex;
                Color baseColor = selected
                    ? CARD_SELECTED
                    : (anySelected ? Color.Lerp(CARD_BG, Color.black, 0.3f) : CARD_BG);

                cardFeedbacks[i].SetBaseColor(baseColor);
                cardFeedbacks[i].glowAlways = selected;
                cardFeedbacks[i].restScale = selected ? 1.03f : (anySelected ? 0.97f : 1f);

                selectHintTexts[i].text = selected ? "✓ SELECTED" : "CLICK TO SELECT";
                selectHintTexts[i].color = selected ? ACCENT_BRIGHT : new Color(STAT_LABEL_COLOR.r, STAT_LABEL_COLOR.g, STAT_LABEL_COLOR.b, 0.6f);
            }
        }

        // ============================
        // Card Faces: shuffle, deal and flip
        // ============================

        private void SetFaceVisual(int index, bool faceUp)
        {
            if (cardFronts[index] != null) cardFronts[index].SetActive(faceUp);
            if (cardBacks[index] != null) cardBacks[index].SetActive(!faceUp);
        }

        private void SetCardsInteractable(bool value)
        {
            for (int i = 0; i < 3; i++)
            {
                if (cardButtonsArr[i] != null) cardButtonsArr[i].interactable = value;
            }
        }

        private void SetCardPose(int index, Vector2 position, float rotationZ)
        {
            cardRects[index].anchoredPosition = position;
            cardRects[index].localRotation = Quaternion.Euler(0f, 0f, rotationZ);
        }

        private static Vector2 StackPosition(int orderIndex, int count)
        {
            float offset = (orderIndex - (count - 1) * 0.5f) * 4f;
            return new Vector2(offset, -offset);
        }

        private void ApplySiblingOrder(List<int> bottomToTop)
        {
            for (int a = 0; a < bottomToTop.Count; a++) cardRects[bottomToTop[a]].SetAsLastSibling();
        }

        /// <summary>Puts every card face-down on one stack in the middle, hidden until the shuffle starts.</summary>
        private void ResetCardsForNewDraft()
        {
            if (dealRoutine != null)
            {
                StopCoroutine(dealRoutine);
                dealRoutine = null;
            }

            StopAllFlips();

            freshDraft = true;
            offerApplied = false;
            isShuffling = true;

            for (int i = 0; i < 3; i++)
            {
                if (cardPanels[i] == null) continue;

                cardPanels[i].SetActive(true);
                cardRevealed[i] = false;
                SetFaceVisual(i, false);
                cardRects[i].anchoredPosition = Vector2.zero;
                cardRects[i].localRotation = Quaternion.identity;
                cardFeedbacks[i].introScale = StackScale;
                cardFeedbacks[i].horizontalScale = 1f;
                cardGroups[i].alpha = 0f;
            }

            SetCardsInteractable(false);
        }

        private void StartShuffleDeal()
        {
            StopAllFlips();

            if (dealRoutine != null)
            {
                StopCoroutine(dealRoutine);
                dealRoutine = null;
            }

            if (!isActiveAndEnabled)
            {
                // Can't animate: fill the cards and leave them dealt face-down.
                ApplyOfferToCards();
                offerApplied = true;
                freshDraft = false;
                SettleCards();
                return;
            }

            isShuffling = true;
            offerApplied = false;
            SetCardsInteractable(false);
            dealRoutine = StartCoroutine(ShuffleDealRoutine());
        }

        /// <summary>
        /// gather (flip back any revealed cards and slide them into a stack) -> fill with the new offer ->
        /// riffle shuffle -> deal back out, still face-down.
        /// </summary>
        private IEnumerator ShuffleDealRoutine()
        {
            bool fresh = freshDraft;

            var order = new List<int>(3); // draw order, bottom -> top
            for (int i = 0; i < 3; i++) if (cardPanels[i].activeSelf) order.Add(i);

            // --- 1. Gather into a stack (skipped for the first deal of a draft) ---
            if (!fresh && order.Count > 0)
            {
                var startPos = new Vector2[3];
                var wasRevealed = new bool[3];
                var flippedBack = new bool[3];
                for (int a = 0; a < order.Count; a++)
                {
                    int i = order[a];
                    startPos[i] = cardRects[i].anchoredPosition;
                    wasRevealed[i] = cardRevealed[i];
                }

                float t = 0f;
                while (t < GatherTime)
                {
                    t += Time.unscaledDeltaTime;
                    float k = EaseInOutCubic(Mathf.Clamp01(t / GatherTime));

                    for (int a = 0; a < order.Count; a++)
                    {
                        int i = order[a];
                        cardRects[i].anchoredPosition = Vector2.Lerp(startPos[i], StackPosition(a, order.Count), k);
                        cardFeedbacks[i].introScale = Mathf.Lerp(1f, StackScale, k);

                        if (wasRevealed[i])
                        {
                            float fk = Mathf.Clamp01(t / GatherFlipTime);
                            cardFeedbacks[i].horizontalScale = Mathf.Max(0.001f, Mathf.Abs(Mathf.Cos(fk * Mathf.PI)));
                            if (fk >= 0.5f && !flippedBack[i])
                            {
                                flippedBack[i] = true;
                                SetFaceVisual(i, false);
                            }
                        }
                    }

                    yield return null;
                }

                for (int a = 0; a < order.Count; a++)
                {
                    int i = order[a];
                    cardFeedbacks[i].horizontalScale = 1f;
                    SetFaceVisual(i, false);
                }
            }

            // --- 2. Every card is face-down: now it is safe to write the new offer onto them ---
            ApplyOfferToCards();
            offerApplied = true;
            freshDraft = false;

            order.Clear();
            for (int i = 0; i < 3; i++) if (cardPanels[i].activeSelf) order.Add(i);
            int n = order.Count;

            for (int a = 0; a < n; a++)
            {
                int i = order[a];
                SetCardPose(i, StackPosition(a, n), 0f);
                cardFeedbacks[i].introScale = StackScale;
                cardFeedbacks[i].horizontalScale = 1f;
            }

            ApplySiblingOrder(order);

            if (fresh)
            {
                float fade = 0f;
                while (fade < 0.18f)
                {
                    fade += Time.unscaledDeltaTime;
                    float alpha = Mathf.Clamp01(fade / 0.18f);
                    for (int a = 0; a < n; a++) cardGroups[order[a]].alpha = alpha;
                    yield return null;
                }
            }

            for (int a = 0; a < n; a++) cardGroups[order[a]].alpha = 1f;

            // --- 3. Riffle shuffle: the top and bottom cards swing out and trade places ---
            int cycles = fresh ? 2 : 3;
            if (n >= 2)
            {
                for (int c = 0; c < cycles; c++)
                {
                    int topCard = order[n - 1];
                    int bottomCard = order[0];
                    PlaySfx(shuffleClip, 0.85f + 0.12f * c);

                    float t = 0f;
                    while (t < RiffleOutTime)
                    {
                        t += Time.unscaledDeltaTime;
                        float k = EaseOutCubic(Mathf.Clamp01(t / RiffleOutTime));
                        SetCardPose(topCard, StackPosition(n - 1, n) + new Vector2(-RiffleSpread * k, 14f * k), 8f * k);
                        SetCardPose(bottomCard, StackPosition(0, n) + new Vector2(RiffleSpread * k, -14f * k), -8f * k);
                        yield return null;
                    }

                    // Trade depth while they are out of the stack
                    order[0] = topCard;
                    order[n - 1] = bottomCard;
                    ApplySiblingOrder(order);

                    Vector2 topOut = cardRects[topCard].anchoredPosition;
                    Vector2 bottomOut = cardRects[bottomCard].anchoredPosition;
                    t = 0f;
                    while (t < RiffleInTime)
                    {
                        t += Time.unscaledDeltaTime;
                        float k = EaseInOutCubic(Mathf.Clamp01(t / RiffleInTime));
                        SetCardPose(topCard, Vector2.Lerp(topOut, StackPosition(0, n), k), Mathf.Lerp(8f, 0f, k));
                        SetCardPose(bottomCard, Vector2.Lerp(bottomOut, StackPosition(n - 1, n), k), Mathf.Lerp(-8f, 0f, k));
                        yield return null;
                    }

                    SetCardPose(topCard, StackPosition(0, n), 0f);
                    SetCardPose(bottomCard, StackPosition(n - 1, n), 0f);
                }
            }

            // --- 4. Deal back out, top of the stack first, still face-down ---
            var stackStart = new Vector2[3];
            var dealSequence = new List<int>(n);
            for (int a = 0; a < n; a++) stackStart[order[a]] = StackPosition(a, n);
            for (int a = n - 1; a >= 0; a--) dealSequence.Add(order[a]);

            var soundPlayed = new bool[n];
            float total = DealTime + DealStagger * Mathf.Max(0, n - 1);
            float td = 0f;
            while (td < total)
            {
                td += Time.unscaledDeltaTime;

                for (int a = 0; a < n; a++)
                {
                    int i = dealSequence[a];
                    float k = Mathf.Clamp01((td - a * DealStagger) / DealTime);
                    float eased = EaseOutCubic(k);

                    Vector2 position = Vector2.Lerp(stackStart[i], cardSlots[i], eased);
                    position.y += Mathf.Sin(k * Mathf.PI) * 36f;
                    float side = Mathf.Clamp(cardSlots[i].x / (CardWidth + CardSpacing), -1f, 1f);
                    SetCardPose(i, position, (1f - eased) * side * -6f);
                    cardFeedbacks[i].introScale = Mathf.Lerp(StackScale, 1f, eased);

                    if (!soundPlayed[a] && k > 0f)
                    {
                        soundPlayed[a] = true;
                        PlaySfx(dealClip, 1.05f + 0.05f * a);
                    }
                }

                yield return null;
            }

            SettleCards();
        }

        /// <summary>Puts every card in its slot at rest, in whichever face (up/down) it should show.</summary>
        private void SettleCards()
        {
            isShuffling = false;

            for (int i = 0; i < 3; i++)
            {
                if (cardRects[i] == null) continue;

                cardRects[i].anchoredPosition = cardSlots[i];
                cardRects[i].localRotation = Quaternion.identity;
                cardFeedbacks[i].introScale = 1f;
                cardFeedbacks[i].horizontalScale = 1f;
                cardGroups[i].alpha = 1f;
                SetFaceVisual(i, cardRevealed[i]);
                cardRects[i].SetSiblingIndex(i);
            }

            SetCardsInteractable(true);
            UpdateRerollButton();
        }

        private void RevealCard(int index)
        {
            if (cardRevealed[index]) return;
            cardRevealed[index] = true;

            if (!isActiveAndEnabled)
            {
                SetFaceVisual(index, true);
                cardFeedbacks[index].glowColor = RarityGlow(index);
                return;
            }

            if (flipRoutines[index] != null) StopCoroutine(flipRoutines[index]);
            flipRoutines[index] = StartCoroutine(FlipRoutine(index));
        }

        private IEnumerator FlipRoutine(int index)
        {
            MenuButtonFeedback feedback = cardFeedbacks[index];
            // The 1.3 pitch turns the hover blip fallback into a flick; a real flip sound plays as recorded.
            PlaySfx(flipClip, flipClip == hoverClip ? 1.3f : UnityEngine.Random.Range(0.97f, 1.03f));

            // Half 1: the back squeezes away
            float t = 0f;
            while (t < FlipHalfOut)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / FlipHalfOut);
                feedback.horizontalScale = Mathf.Max(0.001f, 1f - EaseInCubic(k));
                feedback.introScale = 1f + 0.06f * k;
                yield return null;
            }

            // Swap to the front and let the rarity color show through
            feedback.horizontalScale = 0.001f;
            SetFaceVisual(index, true);
            feedback.glowColor = RarityGlow(index);
            Color rarityColor = cardRarityColors[index];

            // Half 2: the front springs open, with a short flash in the rarity color
            t = 0f;
            while (t < FlipHalfIn)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / FlipHalfIn);
                feedback.horizontalScale = Mathf.Max(0.001f, Mathf.LerpUnclamped(0.001f, 1f, EaseOutBack(k)));
                feedback.introScale = 1.06f - 0.06f * k;
                cardFlashes[index].color = new Color(rarityColor.r, rarityColor.g, rarityColor.b, (1f - k) * 0.4f);
                yield return null;
            }

            feedback.horizontalScale = 1f;
            feedback.introScale = 1f;
            cardFlashes[index].color = new Color(1f, 1f, 1f, 0f);
            flipRoutines[index] = null;
        }

        private void StopAllFlips()
        {
            for (int i = 0; i < 3; i++)
            {
                if (flipRoutines[i] != null)
                {
                    StopCoroutine(flipRoutines[i]);
                    flipRoutines[i] = null;
                }

                if (cardFeedbacks[i] != null)
                {
                    cardFeedbacks[i].horizontalScale = 1f;
                    cardFeedbacks[i].introScale = 1f;
                }

                if (cardFlashes[i] != null) cardFlashes[i].color = new Color(1f, 1f, 1f, 0f);
                if (cardRevealed[i]) SetFaceVisual(i, true);
            }
        }

        private Color RarityGlow(int index)
        {
            Color c = cardRarityColors[index];
            return new Color(c.r, c.g, c.b, 0.95f);
        }

        // ============================
        // Detail Panel
        // ============================

        private void UpdateDetailsPanel(bool animate)
        {
            if (detailContentRoot == null) return;

            DraftCard card = null;
            if (selectedCardIndex >= 0 && draftSystem != null && selectedCardIndex < draftSystem.CurrentOfferedCards.Count)
            {
                card = draftSystem.CurrentOfferedCards[selectedCardIndex];
            }

            bool hasCard = card != null && card.operatorData != null;
            detailContentRoot.SetActive(hasCard);
            detailEmptyRoot.SetActive(!hasCard);

            if (!hasCard)
            {
                detailAccent.color = new Color(ACCENT_COLOR.r, ACCENT_COLOR.g, ACCENT_COLOR.b, 0.35f);
                return;
            }

            OperatorData op = card.operatorData;
            int stars = Mathf.Clamp((int)card.rarity, 1, 5);
            Color rarityColor = GetRarityColor(stars);

            detailAccent.color = rarityColor;
            detailNameText.text = op.operatorName;
            detailStarsText.text = BuildStarString(stars);
            detailStarsText.color = rarityColor;

            // Type chips: class / position / damage type
            SetChip(detailClassChip, op.operatorClass.ToString().ToUpper(), GetClassColor(op.operatorClass));
            detailClassChip.icon.sprite = GameplayHUDUI.GetClassIconSprite(op.operatorClass);
            bool isMelee = op.position == OperatorPosition.Melee;
            SetChip(detailPositionChip, isMelee ? "MELEE" : "RANGED", isMelee ? MELEE_COLOR : RANGED_COLOR);
            GetDamageType(op, out string damageLabel, out Color damageColor);
            SetChip(detailDamageChip, damageLabel, damageColor);

            detailRoleText.text = GetOperatorDescription(op);

            // Stats (scaled by rarity)
            detailHpText.text = op.GetScaledHP(card.rarity).ToString();
            detailAtkText.text = op.GetScaledATK(card.rarity).ToString();
            detailDefText.text = op.GetScaledDEF(card.rarity).ToString();
            detailResText.text = op.GetScaledRES(card.rarity).ToString();
            detailBlockText.text = op.blockCount.ToString();
            detailIntervalText.text = op.attackInterval.ToString("0.##") + "s";

            // Attack range pattern
            UpdateRangePreview(op);

            // Passive / ability text (only shown when the operator has one)
            string passive = FindStringMember(op, PassiveMemberNames);
            bool hasPassive = !string.IsNullOrWhiteSpace(passive);
            detailPassiveLabel.gameObject.SetActive(hasPassive);
            detailPassiveText.gameObject.SetActive(hasPassive);
            if (hasPassive)
            {
                detailPassiveLabel.color = new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.9f);
                detailPassiveText.text = passive.Trim();
            }

            if (animate) PlayDetailsAnimation();
        }

        private static void SetChip(Chip chip, string label, Color color)
        {
            chip.text.text = label;
            chip.text.color = Color.Lerp(color, Color.white, 0.35f);
            chip.bg.color = Color.Lerp(TILE_BG, color, 0.22f);
            chip.outline.effectColor = new Color(color.r, color.g, color.b, 0.55f);
        }

        private static void GetDamageType(OperatorData op, out string label, out Color color)
        {
            // Uses a damage-type member on OperatorData if one exists (see DamageTypeMemberNames),
            // otherwise falls back to a guess based on the operator's class.
            string raw = FindStringMember(op, DamageTypeMemberNames);
            string upper = !string.IsNullOrEmpty(raw)
                ? raw.ToUpperInvariant()
                : (op.operatorClass == OperatorClass.Caster ? "ARTS" : op.operatorClass == OperatorClass.Medic ? "HEAL" : "PHYSICAL");

            if (upper.Contains("PHYS"))
            {
                label = "PHYSICAL";
                color = new Color(0.90f, 0.45f, 0.35f, 1f);
            }
            else if (upper.Contains("ART") || upper.Contains("MAGIC"))
            {
                label = "ARTS";
                color = new Color(0.70f, 0.50f, 1.00f, 1f);
            }
            else if (upper.Contains("HEAL"))
            {
                label = "HEAL";
                color = new Color(0.40f, 0.85f, 0.50f, 1f);
            }
            else
            {
                label = upper;
                color = STAT_LABEL_COLOR;
            }
        }

        /// <summary>
        /// Looks for a string/enum field or property by name (first match wins).
        /// Lets the panel pick up optional OperatorData members without hard-coding their names.
        /// </summary>
        private static string FindStringMember(object target, string[] memberNames)
        {
            if (target == null) return null;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            Type type = target.GetType();

            foreach (string memberName in memberNames)
            {
                object value = null;

                FieldInfo field = type.GetField(memberName, flags);
                if (field != null)
                {
                    value = field.GetValue(target);
                }
                else
                {
                    PropertyInfo property = type.GetProperty(memberName, flags);
                    if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                    {
                        value = property.GetValue(target);
                    }
                }

                if (value is string text && !string.IsNullOrWhiteSpace(text)) return text;
                if (value != null && value.GetType().IsEnum) return value.ToString();
            }

            return null;
        }

        private void PlayDetailsAnimation()
        {
            if (!isActiveAndEnabled) return;

            if (detailsRoutine != null) StopCoroutine(detailsRoutine);
            detailsRoutine = StartCoroutine(DetailsRoutine());
        }

        private IEnumerator DetailsRoutine()
        {
            const float duration = 0.22f;

            detailContentGroup.alpha = 0f;
            detailContentRect.anchoredPosition = new Vector2(24f, 0f);

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = EaseOutCubic(Mathf.Clamp01(t / duration));
                detailContentGroup.alpha = k;
                detailContentRect.anchoredPosition = new Vector2(24f * (1f - k), 0f);
                yield return null;
            }

            detailContentGroup.alpha = 1f;
            detailContentRect.anchoredPosition = Vector2.zero;
        }

        // ============================
        // User Actions
        // ============================

        private void OnCardClicked(int index)
        {
            ClearUiSelection();

            // Cards can't be touched while they are being shuffled and dealt.
            if (isShuffling) return;

            if (index < 0 || draftSystem == null || index >= draftSystem.CurrentOfferedCards.Count)
                return;

            // A face-down card flips over first.
            if (!cardRevealed[index])
            {
                RevealCard(index);
                if (!selectOnReveal) return;
            }

            bool changed = selectedCardIndex != index;
            selectedCardIndex = index;

            // Update card highlights + detail panel
            ApplySelectionVisuals();
            UpdateDetailsPanel(changed);

            UpdateConfirmButton();
        }

        private void OnConfirmClicked()
        {
            ClearUiSelection();

            // Only one pick per draft, and only while the draft can actually end (not paused):
            // EnterPreparationPhase does nothing outside Playing, which used to leave the offer up for another pick.
            if (pickConfirmed || !IsActiveGameplayCardPick())
                return;

            if (selectedCardIndex < 0 || draftSystem == null || playerDeck == null)
                return;

            if (!playerDeck.CanAcceptCard(GetSelectedOfferedCard()))
            {
                Debug.LogWarning("PlayerDeck: Deck is full! Cannot add more cards.");
                return;
            }

            // Select the card from the draft
            if (draftSystem.SelectCard(selectedCardIndex, out DraftCard selectedCard))
            {
                pickConfirmed = true;
                // Add to player deck
                playerDeck.AddCard(selectedCard);

                // Also register with the rarity upgrade system
                // (already handled inside CardDraftSystem.SelectCard)

                PlaySfx(confirmClip, 1f);
                Debug.Log($"<color=cyan>[Draft] Added {selectedCard.operatorData.operatorName} ({selectedCard.rarity}) to deck!</color>");
            }

            // Transition to preparation phase
            if (gameManager != null)
            {
                gameManager.EnterPreparationPhase();
            }
        }

        private void OnRerollClicked()
        {
            ClearUiSelection();

            if (draftSystem == null || !IsActiveGameplayCardPick()) return;

            // Wait for the current shuffle to finish before shuffling again.
            if (isShuffling) return;

            var newOffer = draftSystem.RerollOffer();
            if (newOffer == null)
            {
                Debug.LogWarning("No rerolls remaining!");
            }
            else
            {
                PlaySfx(rerollClip, 1f);
            }

            UpdateRerollButton();
        }

        private void OnSkipClicked()
        {
            ClearUiSelection();
            if (!IsActiveGameplayCardPick()) return;

            // Skip card pick and go straight to preparation
            if (gameManager != null)
            {
                gameManager.EnterPreparationPhase();
            }
        }

        // ============================
        // UI State Updates
        // ============================

        private void UpdateConfirmButton()
        {
            DraftCard selectedCard = GetSelectedOfferedCard();
            bool canConfirm = selectedCard != null && playerDeck != null && playerDeck.CanAcceptCard(selectedCard);
            confirmButton.interactable = canConfirm;

            GameplayHUDUI.ApplyButtonContainer(confirmButton, canConfirm ? GameplayHUDUI.ButtonContainerType.Primary : GameplayHUDUI.ButtonContainerType.Dark);
            confirmFeedback.SetBaseColor(Color.white);
            confirmFeedback.glowAlways = canConfirm;

            var checkIcon = confirmButton.transform.Find("Icon")?.GetComponent<Image>();
            if (checkIcon != null)
            {
                checkIcon.color = canConfirm ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            }

            if (playerDeck != null && playerDeck.IsFull && canConfirm)
            {
                // A full deck still takes a card that merges on arrival.
                confirmButtonText.text = "CONFIRM: MERGES";
            }
            else if (playerDeck != null && playerDeck.IsFull)
            {
                confirmButtonText.text = "DECK FULL: DISCARD IN MAP & DECK";
            }
            else if (selectedCardIndex < 0)
            {
                confirmButtonText.text = "SELECT A CARD";
            }
            else
            {
                confirmButtonText.text = "CONFIRM CARD";
            }

            confirmButtonText.color = canConfirm ? Color.white : new Color(1f, 1f, 1f, 0.55f);
        }

        private DraftCard GetSelectedOfferedCard()
        {
            if (draftSystem == null || selectedCardIndex < 0 || selectedCardIndex >= draftSystem.CurrentOfferedCards.Count)
                return null;
            return draftSystem.CurrentOfferedCards[selectedCardIndex];
        }

        private void UpdateRerollButton()
        {
            if (draftSystem == null) return;

            int remaining = draftSystem.RerollsRemaining;
            bool canReroll = remaining > 0 && !isShuffling;
            rerollButton.interactable = canReroll;

            GameplayHUDUI.ApplyButtonContainer(rerollButton, canReroll ? GameplayHUDUI.ButtonContainerType.Warning : GameplayHUDUI.ButtonContainerType.Dark);
            rerollFeedback.SetBaseColor(Color.white);

            rerollButtonText.text = $"REROLL ({remaining})";
            rerollButtonText.color = canReroll ? Color.white : new Color(1f, 1f, 1f, 0.45f);
        }

        private void UpdateDeckCount()
        {
            if (deckCountText != null && playerDeck != null)
            {
                deckCountText.text = $"DECK {playerDeck.CardCount}/{PlayerDeck.MAX_DECK_SIZE}";
                deckCountText.color = playerDeck.IsFull ? DECK_FULL_COLOR : Color.white;
            }
        }

        private void UpdateWaveInfo()
        {
            if (waveInfoText != null && gameManager != null)
            {
                var waveManager = FindAnyObjectByType<WaveManager>();
                if (waveManager != null)
                {
                    int nextWave = gameManager.GetCurrentWaveIndex() + 1;
                    waveInfoText.text = $"NEXT: WAVE {nextWave} / {waveManager.TotalWaves}";
                }
            }
        }

        // ============================
        // Minimize (view the map)
        // ============================

        /// <summary>
        /// Hides the draft screen so the player can look at the stage, leaving a small bar to come back.
        /// The offer and the current selection are kept.
        /// </summary>
        private void SetMinimized(bool value)
        {
            if (overlayRoot == null || minimized == value) return;
            if (value && !overlayVisible) return;
            if (IsGamePaused()) return;

            ClearUiSelection();
            minimized = value;
            if (value)
            {
                FinishAnimations();
                overlayRoot.SetActive(false);
            }
            else
            {
                overlayRoot.SetActive(true);
                overlayGroup.alpha = 1f;
                overlayGroup.interactable = true;
                overlayGroup.blocksRaycasts = true;
                UpdateConfirmButton();
                UpdateDeckCount();
            }

            if (restoreBar != null) restoreBar.SetActive(value);
            PlayClick();
        }

        private void ResetMinimized()
        {
            minimized = false;
            if (restoreBar != null) restoreBar.SetActive(false);
        }

        /// <summary>Jumps intro/shuffle/flip animations to their end state so a restored overlay is laid out correctly.</summary>
        private void FinishAnimations()
        {
            if (introRoutine != null)
            {
                StopCoroutine(introRoutine);
                introRoutine = null;
                for (int i = 0; i < introRects.Length; i++) introRects[i].anchoredPosition = introTargets[i];
            }

            StopAllFlips();

            if (dealRoutine != null)
            {
                StopCoroutine(dealRoutine);
                dealRoutine = null;

                if (!offerApplied)
                {
                    ApplyOfferToCards();
                    offerApplied = true;
                    freshDraft = false;
                }

                SettleCards();
            }
        }

        /// <summary>Keeps the confirm button and deck count current while the deck is edited from the map view.</summary>
        private void HandleDeckChanged(IReadOnlyList<DraftCard> _)
        {
            if (!overlayVisible) return;
            UpdateConfirmButton();
            UpdateDeckCount();
        }

        // ============================
        // Animation
        // ============================

        private void PlayIntro()
        {
            if (!isActiveAndEnabled) return;

            if (introRoutine != null) StopCoroutine(introRoutine);
            introRoutine = StartCoroutine(IntroRoutine());
        }

        private IEnumerator IntroRoutine()
        {
            overlayGroup.alpha = 0f;
            overlayGroup.interactable = true;
            overlayGroup.blocksRaycasts = true;

            const float duration = 0.45f;
            float total = duration + 0.35f;
            float t = 0f;

            while (t < total)
            {
                t += Time.unscaledDeltaTime;
                overlayGroup.alpha = Mathf.Clamp01(t / 0.25f);

                for (int i = 0; i < introRects.Length; i++)
                {
                    float k = EaseOutCubic(Mathf.Clamp01((t - introDelays[i]) / duration));
                    introRects[i].anchoredPosition = introTargets[i] + introOffsets[i] * (1f - k);
                }

                yield return null;
            }

            overlayGroup.alpha = 1f;
            for (int i = 0; i < introRects.Length; i++) introRects[i].anchoredPosition = introTargets[i];
        }

        private IEnumerator HideRoutine()
        {
            if (introRoutine != null) { StopCoroutine(introRoutine); introRoutine = null; }
            if (dealRoutine != null) { StopCoroutine(dealRoutine); dealRoutine = null; }

            overlayGroup.interactable = false;
            overlayGroup.blocksRaycasts = false;

            const float duration = 0.18f;
            float from = overlayGroup.alpha;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                overlayGroup.alpha = Mathf.Lerp(from, 0f, Mathf.Clamp01(t / duration));
                yield return null;
            }

            overlayGroup.alpha = 0f;
            overlayRoot.SetActive(false);
            hideRoutine = null;
        }

        private static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        private static float EaseInCubic(float t)
        {
            return t * t * t;
        }

        private static float EaseInOutCubic(float t)
        {
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        // ============================
        // Feedback helpers
        // ============================

        private MenuButtonFeedback AttachFeedback(Button button, Color glow, float hoverScale)
        {
            // The feedback component drives the visuals, so disable Unity's color tint transition.
            button.transition = Selectable.Transition.None;

            var feedback = button.GetComponent<MenuButtonFeedback>();
            if (feedback == null) feedback = button.gameObject.AddComponent<MenuButtonFeedback>();

            feedback.hoverScale = hoverScale;
            feedback.respondToFocus = false; // clicked buttons stay "selected"; don't keep them lit
            feedback.Hovered = PlayHover;
            feedback.Clicked = PlayClick;

            var image = button.GetComponent<Image>();
            if (image != null)
            {
                var outline = image.GetComponent<Outline>();
                if (outline == null) outline = image.gameObject.AddComponent<Outline>();
                outline.useGraphicAlpha = false;
                outline.effectDistance = new Vector2(2f, -2f);
                outline.effectColor = new Color(glow.r, glow.g, glow.b, 0f);

                feedback.glowOutline = outline;
                feedback.glowColor = new Color(glow.r, glow.g, glow.b, 0.9f);
            }

            return feedback;
        }

        private void PlayHover() { PlaySfx(hoverClip, UnityEngine.Random.Range(0.97f, 1.03f)); }
        private void PlayClick() { PlaySfx(clickClip, 1f); }

        private void PlaySfx(AudioClip clip, float pitch)
        {
            AudioManager.Instance?.PlaySfx(clip, sfxVolume, pitch);
        }

        private static void ClearUiSelection()
        {
            // Prevents Enter/Space from re-triggering the button that was just clicked.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>1★ green, 2★ blue, 3★ gold, 4★ purple, 5★ red.</summary>
        private static Color GetRarityColor(int stars)
        {
            switch (stars)
            {
                case 1: return new Color(0.35f, 0.85f, 0.45f, 1f);
                case 2: return new Color(0.35f, 0.65f, 1.00f, 1f);
                case 3: return new Color(1.00f, 0.82f, 0.25f, 1f);
                case 4: return new Color(0.75f, 0.45f, 1.00f, 1f);
                default: return new Color(1.00f, 0.30f, 0.30f, 1f);
            }
        }

        private static string BuildStarString(int stars)
        {
            stars = Mathf.Clamp(stars, 0, 5);
            return new string('★', stars) + "<color=#3C424D>" + new string('☆', 5 - stars) + "</color>";
        }

        private static Color GetClassColor(OperatorClass opClass)
        {
            switch (opClass)
            {
                case OperatorClass.Guard: return new Color(0.80f, 0.28f, 0.28f, 1f);
                case OperatorClass.Defender: return new Color(0.30f, 0.45f, 0.85f, 1f);
                case OperatorClass.Sniper: return new Color(0.30f, 0.75f, 0.30f, 1f);
                case OperatorClass.Caster: return new Color(0.70f, 0.30f, 0.85f, 1f);
                case OperatorClass.Medic: return new Color(0.80f, 0.78f, 0.30f, 1f);
                case OperatorClass.Specialist: return new Color(0.20f, 0.75f, 0.70f, 1f);
                default: return CLASS_COLOR;
            }
        }

        // ============================
        // UI Construction
        // ============================

        private void BuildOverlay()
        {
            // Canvas
            var canvasObj = new GameObject("CardDraftOverlayCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200; // Above gameplay HUD

            var scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Root overlay panel (fullscreen dark background)
            overlayRoot = new GameObject("OverlayRoot", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            overlayRoot.transform.SetParent(canvasObj.transform, false);
            var rootRect = overlayRoot.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            overlayRoot.GetComponent<Image>().color = BG_OVERLAY;
            overlayGroup = overlayRoot.GetComponent<CanvasGroup>();

            // Top accent stripe
            var stripe = new GameObject("AccentStripe", typeof(RectTransform), typeof(Image));
            stripe.transform.SetParent(overlayRoot.transform, false);
            var stripeRect = stripe.GetComponent<RectTransform>();
            stripeRect.anchorMin = new Vector2(0f, 1f);
            stripeRect.anchorMax = new Vector2(1f, 1f);
            stripeRect.pivot = new Vector2(0.5f, 1f);
            stripeRect.sizeDelta = new Vector2(0f, 4f);
            var stripeImage = stripe.GetComponent<Image>();
            stripeImage.color = ACCENT_COLOR;
            stripeImage.raycastTarget = false;

            // Title (centered above the card column)
            titleText = MakeText(overlayRoot.transform, "Title", "CHOOSE YOUR CARD", 36,
                TextAnchor.MiddleCenter, Color.white);
            titleText.fontStyle = FontStyle.Bold;
            PositionRT(titleText, new Vector2(ColumnCenterX, -48f), new Vector2(0.5f, 1f), new Vector2(700f, 60f));
            var titleShadow = titleText.gameObject.AddComponent<Shadow>();
            titleShadow.effectColor = new Color(ACCENT_COLOR.r, ACCENT_COLOR.g, ACCENT_COLOR.b, 0.35f);
            titleShadow.effectDistance = new Vector2(0f, -4f);

            // Wave info
            waveInfoText = MakeText(overlayRoot.transform, "WaveInfo", "NEXT: WAVE 1 / ?", 18,
                TextAnchor.MiddleCenter, STAT_LABEL_COLOR);
            PositionRT(waveInfoText, new Vector2(ColumnCenterX, -104f), new Vector2(0.5f, 1f), new Vector2(500f, 40f));

            var waveIconSprite = GameplayHUDUI.LoadIconSprite("icon_wave");
            if (waveIconSprite != null)
            {
                var waveIconObj = new GameObject("WaveHeaderIcon", typeof(RectTransform), typeof(Image));
                waveIconObj.transform.SetParent(overlayRoot.transform, false);
                var waveIconImg = waveIconObj.GetComponent<Image>();
                waveIconImg.sprite = waveIconSprite;
                waveIconImg.color = STAT_LABEL_COLOR;
                waveIconImg.preserveAspect = true;
                waveIconImg.raycastTarget = false;
                PositionRT(waveIconImg, new Vector2(ColumnCenterX - 130f, -104f), new Vector2(0.5f, 1f), new Vector2(22f, 22f));
            }

            // Divider under the header
            var divider = new GameObject("HeaderDivider", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(overlayRoot.transform, false);
            var dividerImage = divider.GetComponent<Image>();
            dividerImage.color = new Color(ACCENT_COLOR.r, ACCENT_COLOR.g, ACCENT_COLOR.b, 0.4f);
            dividerImage.raycastTarget = false;
            PositionRT(dividerImage, new Vector2(ColumnCenterX, -134f), new Vector2(0.5f, 1f), new Vector2(240f, 2f));

            // Deck count
            deckCountText = MakeText(overlayRoot.transform, "DeckCount", "DECK 0/8", 16,
                TextAnchor.MiddleRight, Color.white);
            PositionRT(deckCountText, new Vector2(-28f, -32f), new Vector2(1f, 1f), new Vector2(300f, 48f));
            deckCountText.rectTransform.pivot = new Vector2(1f, 1f);
            deckCountText.resizeTextForBestFit = true;
            deckCountText.resizeTextMinSize = 12;
            deckCountText.resizeTextMaxSize = 16;

            var squadIconSprite = GameplayHUDUI.LoadIconSprite("icon_squad");
            if (squadIconSprite != null)
            {
                var squadIconObj = new GameObject("DeckHeaderIcon", typeof(RectTransform), typeof(Image));
                squadIconObj.transform.SetParent(overlayRoot.transform, false);
                var squadIconImg = squadIconObj.GetComponent<Image>();
                squadIconImg.sprite = squadIconSprite;
                squadIconImg.color = Color.white;
                squadIconImg.preserveAspect = true;
                squadIconImg.raycastTarget = false;
                PositionRT(squadIconImg, new Vector2(-155f, -32f), new Vector2(1f, 1f), new Vector2(24f, 24f));
                squadIconObj.GetComponent<RectTransform>().pivot = new Vector2(1f, 1f);
            }

            // Card container (left side of the screen). Cards are positioned manually
            // (not by a layout group) so the shuffle/deal animation can move them freely.
            var cardContainer = new GameObject("CardContainer", typeof(RectTransform));
            cardContainer.transform.SetParent(overlayRoot.transform, false);
            var containerRect = cardContainer.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.sizeDelta = new Vector2(CardWidth * 3f + CardSpacing * 2f, CardHeight);
            containerRect.anchoredPosition = new Vector2(ColumnCenterX, ContentCenterY);

            // Build 3 cards
            for (int i = 0; i < 3; i++)
            {
                BuildCard(cardContainer.transform, i);
            }

            // Detail panel (right side of the screen)
            BuildDetailsPanel(overlayRoot.transform);

            // Reroll / Skip under the cards
            var buttonsContainer = new GameObject("ButtonsContainer", typeof(RectTransform));
            buttonsContainer.transform.SetParent(overlayRoot.transform, false);
            var buttonsRect = buttonsContainer.GetComponent<RectTransform>();
            buttonsRect.anchorMin = new Vector2(0.5f, 0f);
            buttonsRect.anchorMax = new Vector2(0.5f, 0f);
#if UNITY_EDITOR || DEBUG
            buttonsRect.sizeDelta = new Vector2(640f, 64f);
#else
            buttonsRect.sizeDelta = new Vector2(420f, 64f);
#endif
            buttonsRect.anchoredPosition = new Vector2(ColumnCenterX, 120f);

            var btnHlg = buttonsContainer.AddComponent<HorizontalLayoutGroup>();
            btnHlg.spacing = 20f;
            btnHlg.childAlignment = TextAnchor.MiddleCenter;
            btnHlg.childControlWidth = false;
            btnHlg.childControlHeight = false;
            btnHlg.childForceExpandWidth = false;
            btnHlg.childForceExpandHeight = false;

            // Reroll button
            rerollButton = MakeButton(buttonsContainer.transform, "RerollButton", "REROLL (3)",
                new Vector2(220f, 60f), GameplayHUDUI.ButtonContainerType.Warning);
            rerollButtonText = rerollButton.GetComponentInChildren<Text>();
            rerollButton.onClick.AddListener(OnRerollClicked);
            rerollFeedback = AttachFeedback(rerollButton, new Color(1f, 0.85f, 0.35f, 1f), 1.05f);

            // Skip button
            skipButton = MakeButton(buttonsContainer.transform, "SkipButton", "SKIP",
                new Vector2(140f, 60f), GameplayHUDUI.ButtonContainerType.Dark, "icon_close");
            skipButton.onClick.AddListener(OnSkipClicked);
            skipFeedback = AttachFeedback(skipButton, Color.white, 1.05f);

#if UNITY_EDITOR || DEBUG
            developerPickButton = MakeButton(overlayRoot.transform, "DeveloperPickButton", "D",
                new Vector2(48f, 48f), GameplayHUDUI.ButtonContainerType.Secondary);
            PositionRT(developerPickButton, new Vector2(28f, -32f), new Vector2(0f, 1f), new Vector2(48f, 48f));
            developerPickButton.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            developerPickButton.onClick.AddListener(ShowDeveloperPicker);
            AttachFeedback(developerPickButton, new Color(0.75f, 0.55f, 1f, 1f), 1.05f);
            BuildDeveloperPicker();
#endif

            // Confirm button (under the detail panel, where the decision is made)
            confirmButton = MakeButton(overlayRoot.transform, "ConfirmButton", "SELECT A CARD",
                new Vector2(PanelWidth, 64f), GameplayHUDUI.ButtonContainerType.Dark, "icon_check");
            PositionRT(confirmButton, new Vector2(PanelCenterX, 120f), new Vector2(0.5f, 0f), new Vector2(PanelWidth, 64f));
            confirmButtonText = confirmButton.GetComponentInChildren<Text>();
            confirmButtonText.fontSize = 20;
            confirmButton.onClick.AddListener(OnConfirmClicked);
            confirmButton.interactable = false;
            confirmFeedback = AttachFeedback(confirmButton, ACCENT_BRIGHT, 1.03f);

            // View map & deck controls
            BuildMinimizeControls(canvasObj.transform, buttonsContainer.transform);

            // Keyboard hint
            var hintText = MakeText(overlayRoot.transform, "KeyHints", "1 2 3  FLIP / SELECT     ENTER  CONFIRM     R  REROLL     M  MINIMIZE", 12,
                TextAnchor.MiddleCenter, new Color(STAT_LABEL_COLOR.r, STAT_LABEL_COLOR.g, STAT_LABEL_COLOR.b, 0.55f));
            PositionRT(hintText, new Vector2(0f, 28f), new Vector2(0.5f, 0f), new Vector2(900f, 24f));

            // Header/footer/panel entrance data (positions captured after layout above)
            introRects = new[]
            {
                titleText.rectTransform,
                waveInfoText.rectTransform,
                dividerImage.rectTransform,
                deckCountText.rectTransform,
                detailsPanelRect,
                buttonsRect,
                confirmButton.GetComponent<RectTransform>(),
                hintText.rectTransform
            };
            introTargets = new Vector2[introRects.Length];
            for (int i = 0; i < introRects.Length; i++) introTargets[i] = introRects[i].anchoredPosition;
            introOffsets = new[]
            {
                new Vector2(0f, 40f), new Vector2(0f, 40f), new Vector2(0f, 40f), new Vector2(0f, 40f),
                new Vector2(90f, 0f), new Vector2(0f, -60f), new Vector2(0f, -60f), new Vector2(0f, -30f)
            };
            introDelays = new[] { 0f, 0.06f, 0.1f, 0.1f, 0.15f, 0.25f, 0.3f, 0.35f };
        }

        /// <summary>
        /// Minimize and restore controls for viewing the map and editing the deck while drafting.
        /// While minimized, the gameplay HUD's deck bar works as in Preparation: cards can be rearranged, discarded
        /// and deployed (StageBootstrapper.IsDeploymentAllowed), freeing deck slots for the card being picked.
        /// </summary>
        private void BuildMinimizeControls(Transform canvasRoot, Transform buttonsContainer)
        {
            Button minimize;
#if UNITY_EDITOR || DEBUG
            minimize = MakeButton(buttonsContainer, "MinimizeDraftButton", "MINIMIZE",
                new Vector2(200f, 60f), GameplayHUDUI.ButtonContainerType.Dark, "icon_minus");
#else
            minimize = MakeButton(overlayRoot.transform, "MinimizeDraftButton", "MINIMIZE",
                new Vector2(200f, 48f), GameplayHUDUI.ButtonContainerType.Dark, "icon_minus");
            PositionRT(minimize, new Vector2(28f, -32f), new Vector2(0f, 1f), new Vector2(200f, 48f));
            minimize.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
#endif
            minimize.onClick.AddListener(() => StartCoroutine(MinimizeAfterClick()));
            AttachFeedback(minimize, Color.white, 1.05f);

            Button back = MakeButton(canvasRoot, "BackToCardPickButton", "RESUME", new Vector2(200f, 48f), GameplayHUDUI.ButtonContainerType.Primary);
            PositionRT(back, new Vector2(0f, -24f), new Vector2(0.5f, 1f), new Vector2(200f, 48f));
            back.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
            back.onClick.AddListener(() => SetMinimized(false));
            MenuButtonFeedback backFeedback = AttachFeedback(back, ACCENT_BRIGHT, 1.04f);
            backFeedback.glowAlways = true;

            restoreBar = back.gameObject;
            restoreBar.SetActive(false);
        }

        private IEnumerator MinimizeAfterClick()
        {
            yield return new WaitForEndOfFrame();
            SetMinimized(true);
        }

#if UNITY_EDITOR || DEBUG
        private void BuildDeveloperPicker()
        {
            developerPickerRoot = new GameObject(
                "DeveloperOperatorPicker",
                typeof(RectTransform),
                typeof(Image),
                typeof(CanvasGroup));
            developerPickerRoot.transform.SetParent(overlayRoot.transform, false);
            var rootRect = developerPickerRoot.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            developerPickerRoot.GetComponent<Image>().color = new Color(0.01f, 0.015f, 0.025f, 0.92f);

            var panel = new GameObject("PickerPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(developerPickerRoot.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(760f, 820f);
            GameplayHUDUI.ApplyPanelContainer(panel, GameplayHUDUI.PanelContainerType.Metal);

            var panelBorder = panel.AddComponent<Outline>();
            panelBorder.effectColor = new Color(0.82f, 0.65f, 1f, 0.35f);
            panelBorder.effectDistance = new Vector2(2f, -2f);

            var title = MakeText(panel.transform, "Title", "DEVELOPER PICKER", 26,
                TextAnchor.MiddleCenter, new Color(0.82f, 0.65f, 1f, 1f));
            title.fontStyle = FontStyle.Bold;
            var titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -20f);
            titleRect.sizeDelta = new Vector2(480f, 38f);

            var closeButton = MakeButton(panel.transform, "CloseButton", "CLOSE",
                new Vector2(110f, 40f), GameplayHUDUI.ButtonContainerType.Danger, "icon_close");
            var closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-20f, -18f);
            closeRect.sizeDelta = new Vector2(110f, 40f);
            closeButton.onClick.AddListener(() => developerPickerRoot.SetActive(false));
            AttachFeedback(closeButton, Color.white, 1.04f);

            var hint = MakeText(panel.transform, "Hint", "Choose an operator from this stage's draft pool.",
                14, TextAnchor.MiddleCenter, STAT_LABEL_COLOR);
            var hintRect = hint.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0.5f, 1f);
            hintRect.anchorMax = new Vector2(0.5f, 1f);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.anchoredPosition = new Vector2(0f, -60f);
            hintRect.sizeDelta = new Vector2(680f, 26f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(panel.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = new Vector2(0f, 0f);
            viewportRect.anchorMax = new Vector2(1f, 1f);
            viewportRect.offsetMin = new Vector2(20f, 20f);
            viewportRect.offsetMax = new Vector2(-20f, -96f);
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);
            viewport.GetComponent<Mask>().showMaskGraphic = true;

            var scrollRect = panel.AddComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 35f;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            developerPickerList = content.transform;
            scrollRect.content = contentRect;
            developerPickerRoot.SetActive(false);
        }

        private void ShowDeveloperPicker()
        {
            if (draftSystem == null || developerPickerRoot == null) return;

            if (developerPickerPopulated)
            {
                developerPickerRoot.SetActive(true);
                return;
            }

            IReadOnlyList<OperatorData> operators = draftSystem.AvailableOperatorPool;
            int addedCount = 0;
            if (operators != null)
            {
                var sortedOperators = operators
                    .Where(op => op != null)
                    .OrderBy(op => op.operatorClass)
                    .ThenBy(op => (int)op.baseRarity)
                    .ThenBy(op => op.operatorName)
                    .ToList();

                for (int i = 0; i < sortedOperators.Count; i++)
                {
                    OperatorData opData = sortedOperators[i];
                    int stars = Mathf.Clamp((int)opData.baseRarity, 1, 5);

                    Button option = MakeButton(
                        developerPickerList,
                        $"Operator_{addedCount}",
                        $"{opData.operatorName}  |  {opData.operatorClass}  |  {new string('★', stars)}",
                        new Vector2(680f, 64f),
                        CARD_BG);
                    option.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
                    option.onClick.AddListener(() => SelectDeveloperOperator(opData));
                    AttachFeedback(option, GetRarityColor(stars), 1.02f);
                    addedCount++;
                }
            }

            if (addedCount == 0)
            {
                Debug.LogError("CardDraftOverlayUI: Developer picker has no operators in the active draft pool.");
                var emptyText = MakeText(developerPickerList, "EmptyPool",
                    "No operators are available in the active draft pool.", 16, TextAnchor.MiddleCenter, Color.white);
                emptyText.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
            }

            developerPickerPopulated = true;
            developerPickerRoot.SetActive(true);
        }

#if UNITY_EDITOR || DEBUG
        public void OpenDeveloperOperatorPicker()
        {
            ShowDeveloperPicker();
        }
#endif

        private void SelectDeveloperOperator(OperatorData operatorData)
        {
            if (draftSystem == null || !draftSystem.SetDeveloperOffer(operatorData))
            {
                Debug.LogError("CardDraftOverlayUI: Could not set the requested developer draft offer.");
                return;
            }

            developerPickerRoot.SetActive(false);
            selectedCardIndex = -1;
            UpdateConfirmButton();
        }
#endif

        private void BuildCard(Transform parent, int index)
        {
            // Card root. Anchored to the container's center; its position is driven by
            // the shuffle/deal animation (cardSlots) rather than a layout group.
            var cardObj = new GameObject($"Card_{index}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup));
            cardObj.transform.SetParent(parent, false);
            var cardRect = cardObj.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(CardWidth, CardHeight);
            cardRect.anchoredPosition = Vector2.zero;
            cardRects[index] = cardRect;

            var cardImg = cardObj.GetComponent<Image>();
            cardImg.color = CARD_BG;

            var cardBtn = cardObj.GetComponent<Button>();
            int capturedIndex = index;
            cardBtn.onClick.AddListener(() => OnCardClicked(capturedIndex));
            cardButtonsArr[index] = cardBtn;

            // Hover / press / selection visuals come from MenuButtonFeedback (replaces the color tint).
            cardFeedbacks[index] = AttachFeedback(cardBtn, ACCENT_COLOR, 1.03f);
            cardGroups[index] = cardObj.GetComponent<CanvasGroup>();

            cardPanels[index] = cardObj;
            cardBackgrounds[index] = cardImg;

            // The front of the card (portrait, name, stars) lives under one container so the
            // whole face can be shown/hidden when the card flips.
            var front = new GameObject($"Front_{index}", typeof(RectTransform));
            front.transform.SetParent(cardObj.transform, false);
            StretchFill(front.GetComponent<RectTransform>());
            cardFronts[index] = front;

            // Rarity-colored accent stripe along the top edge
            var accent = new GameObject($"Accent_{index}", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(front.transform, false);
            var accentRect = accent.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.sizeDelta = new Vector2(0f, 5f);
            cardAccents[index] = accent.GetComponent<Image>();
            cardAccents[index].color = STAR_COLOR;
            cardAccents[index].raycastTarget = false;

            // --- Portrait frame: takes up most of the card ---
            var portraitBg = new GameObject($"PortraitBg_{index}", typeof(RectTransform), typeof(Image));
            portraitBg.transform.SetParent(front.transform, false);
            StretchTopRT(portraitBg.transform, PortraitMargin, PortraitMargin, PortraitTop, PortraitHeight);
            portraitBgImages[index] = portraitBg.GetComponent<Image>();
            portraitBgImages[index].color = PORTRAIT_BG;
            portraitBgImages[index].raycastTarget = false;

            // Rarity-colored frame around the portrait
            portraitFrames[index] = portraitBg.AddComponent<Outline>();
            portraitFrames[index].effectDistance = new Vector2(2f, -2f);
            portraitFrames[index].effectColor = new Color(1f, 1f, 1f, 0.15f);

            // Placeholder initial (shown when the operator has no portrait art)
            portraitFallbackTexts[index] = MakeText(portraitBg.transform, $"PortraitFallback_{index}", "?", 140,
                TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.10f));
            portraitFallbackTexts[index].fontStyle = FontStyle.Bold;
            var fallbackRect = portraitFallbackTexts[index].rectTransform;
            fallbackRect.anchorMin = Vector2.zero;
            fallbackRect.anchorMax = Vector2.one;
            fallbackRect.offsetMin = Vector2.zero;
            fallbackRect.offsetMax = Vector2.zero;

            // Portrait sprite: centered in the frame, size set per sprite in SetPortrait()
            var portraitObj = new GameObject($"Portrait_{index}", typeof(RectTransform), typeof(Image));
            portraitObj.transform.SetParent(portraitBg.transform, false);
            var portraitRect = portraitObj.GetComponent<RectTransform>();
            portraitRect.anchorMin = new Vector2(0.5f, 0.5f);
            portraitRect.anchorMax = new Vector2(0.5f, 0.5f);
            portraitRect.pivot = new Vector2(0.5f, 0.5f);
            portraitRect.anchoredPosition = Vector2.zero;
            portraitRect.sizeDelta = PortraitFitArea;
            portraitImages[index] = portraitObj.GetComponent<Image>();
            portraitImages[index].preserveAspect = false;
            portraitImages[index].color = Color.white;
            portraitImages[index].raycastTarget = false;

            var classBadge = new GameObject($"ClassBadge_{index}", typeof(RectTransform), typeof(Image), typeof(Outline));
            classBadge.transform.SetParent(portraitBg.transform, false);
            RectTransform classBadgeRect = classBadge.GetComponent<RectTransform>();
            classBadgeRect.anchorMin = new Vector2(0f, 1f);
            classBadgeRect.anchorMax = new Vector2(0f, 1f);
            classBadgeRect.pivot = new Vector2(0f, 1f);
            classBadgeRect.anchoredPosition = new Vector2(10f, -10f);
            classBadgeRect.sizeDelta = new Vector2(50f, 50f);
            classBadgeBackgrounds[index] = classBadge.GetComponent<Image>();
            classBadgeBackgrounds[index].raycastTarget = false;
            Outline badgeOutline = classBadge.GetComponent<Outline>();
            badgeOutline.effectColor = new Color(0.02f, 0.025f, 0.035f, 0.95f);
            badgeOutline.effectDistance = new Vector2(2f, -2f);
            badgeOutline.useGraphicAlpha = false;

            var classIconObject = new GameObject($"ClassIcon_{index}", typeof(RectTransform), typeof(Image));
            classIconObject.transform.SetParent(classBadge.transform, false);
            classBadgeIcons[index] = classIconObject.GetComponent<Image>();
            classBadgeIcons[index].preserveAspect = true;
            classBadgeIcons[index].raycastTarget = false;
            RectTransform classIconRect = classBadgeIcons[index].rectTransform;
            classIconRect.anchorMin = Vector2.zero;
            classIconRect.anchorMax = Vector2.one;
            classIconRect.offsetMin = new Vector2(8f, 8f);
            classIconRect.offsetMax = new Vector2(-8f, -8f);

            // --- Name (bottom) ---
            nameTexts[index] = MakeText(front.transform, $"Name_{index}", "", 26,
                TextAnchor.MiddleCenter, Color.white);
            nameTexts[index].fontStyle = FontStyle.Bold;
            nameTexts[index].resizeTextForBestFit = true;
            nameTexts[index].resizeTextMinSize = 14;
            nameTexts[index].resizeTextMaxSize = 26;
            StretchTopRT(nameTexts[index], 14f, 14f, 484f, 44f);

            // --- Rarity stars (bottom) ---
            rarityTexts[index] = MakeText(front.transform, $"Rarity_{index}", "", 28,
                TextAnchor.MiddleCenter, STAR_COLOR);
            StretchTopRT(rarityTexts[index], 14f, 14f, 530f, 40f);

            // --- Select hint / selected badge ---
            selectHintTexts[index] = MakeText(front.transform, $"SelectHint_{index}", "CLICK TO SELECT", 12,
                TextAnchor.MiddleCenter, STAT_LABEL_COLOR);
            selectHintTexts[index].fontStyle = FontStyle.Bold;
            StretchTopRT(selectHintTexts[index], 14f, 14f, 596f, 24f);

            // --- The back of the card (shown until the player clicks it) ---
            BuildCardBack(cardObj.transform, index);

            // Brief color flash that plays when the card turns over
            var flash = new GameObject($"Flash_{index}", typeof(RectTransform), typeof(Image));
            flash.transform.SetParent(cardObj.transform, false);
            StretchFill(flash.GetComponent<RectTransform>());
            cardFlashes[index] = flash.GetComponent<Image>();
            cardFlashes[index].color = new Color(1f, 1f, 1f, 0f);
            cardFlashes[index].raycastTarget = false;

            // Cards start face-down
            cardFronts[index].SetActive(false);
        }

        /// <summary>Card back: dark face, double frame, a diamond emblem and a "click to reveal" hint.</summary>
        private void BuildCardBack(Transform card, int index)
        {
            var back = new GameObject($"Back_{index}", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(card, false);
            StretchFill(back.GetComponent<RectTransform>());
            var backImage = back.GetComponent<Image>();
            backImage.color = BACK_BG;
            backImage.raycastTarget = false;
            cardBacks[index] = back;

            Color frameColor = new Color(ACCENT_COLOR.r, ACCENT_COLOR.g, ACCENT_COLOR.b, 0.55f);
            Color innerFrameColor = new Color(ACCENT_COLOR.r, ACCENT_COLOR.g, ACCENT_COLOR.b, 0.22f);
            CreateFrame(back.transform, 14f, 3f, frameColor);
            CreateFrame(back.transform, 26f, 1f, innerFrameColor);

            // Diamond emblem
            var diamond = new GameObject("Emblem", typeof(RectTransform), typeof(Image));
            diamond.transform.SetParent(back.transform, false);
            var diamondRect = diamond.GetComponent<RectTransform>();
            diamondRect.anchorMin = new Vector2(0.5f, 0.5f);
            diamondRect.anchorMax = new Vector2(0.5f, 0.5f);
            diamondRect.pivot = new Vector2(0.5f, 0.5f);
            diamondRect.sizeDelta = new Vector2(190f, 190f);
            diamondRect.anchoredPosition = new Vector2(0f, 10f);
            diamondRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var diamondImage = diamond.GetComponent<Image>();
            diamondImage.color = new Color(ACCENT_COLOR.r, ACCENT_COLOR.g, ACCENT_COLOR.b, 0.10f);
            diamondImage.raycastTarget = false;
            CreateFrame(diamond.transform, 0f, 3f, frameColor);

            var emblemText = MakeText(back.transform, "BackEmblem", "TD", 88, TextAnchor.MiddleCenter,
                new Color(ACCENT_BRIGHT.r, ACCENT_BRIGHT.g, ACCENT_BRIGHT.b, 0.9f));
            emblemText.fontStyle = FontStyle.Bold;
            PositionRT(emblemText, new Vector2(0f, 10f), new Vector2(0.5f, 0.5f), new Vector2(260f, 120f));

            // Small diamonds in the corners of the inner frame
            for (int c = 0; c < 4; c++)
            {
                bool right = (c & 1) == 1;
                bool top = (c & 2) == 2;
                var corner = new GameObject($"Corner_{c}", typeof(RectTransform), typeof(Image));
                corner.transform.SetParent(back.transform, false);
                var cornerRect = corner.GetComponent<RectTransform>();
                Vector2 anchor = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
                cornerRect.anchorMin = anchor;
                cornerRect.anchorMax = anchor;
                cornerRect.pivot = new Vector2(0.5f, 0.5f);
                cornerRect.sizeDelta = new Vector2(12f, 12f);
                cornerRect.anchoredPosition = new Vector2(right ? -26f : 26f, top ? -26f : 26f);
                cornerRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                var cornerImage = corner.GetComponent<Image>();
                cornerImage.color = frameColor;
                cornerImage.raycastTarget = false;
            }

            // Hotkey label near the top and the reveal hint near the bottom
            var keyText = MakeText(back.transform, "BackKey", $"[ {index + 1} ]", 14, TextAnchor.MiddleCenter,
                new Color(STAT_LABEL_COLOR.r, STAT_LABEL_COLOR.g, STAT_LABEL_COLOR.b, 0.6f));
            keyText.fontStyle = FontStyle.Bold;
            StretchTopRT(keyText, 14f, 14f, 44f, 24f);

            var hintText = MakeText(back.transform, "BackHint", "CLICK TO REVEAL", 14, TextAnchor.MiddleCenter,
                new Color(ACCENT_BRIGHT.r, ACCENT_BRIGHT.g, ACCENT_BRIGHT.b, 0.8f));
            hintText.fontStyle = FontStyle.Bold;
            StretchTopRT(hintText, 14f, 14f, CardHeight - 74f, 26f);
        }

        /// <summary>Hollow rectangle made of four thin strips, inset from the edges of its parent.</summary>
        private static void CreateFrame(Transform parent, float inset, float thickness, Color color)
        {
            for (int side = 0; side < 4; side++)
            {
                var strip = new GameObject($"Frame_{side}", typeof(RectTransform), typeof(Image));
                strip.transform.SetParent(parent, false);
                var rect = strip.GetComponent<RectTransform>();

                switch (side)
                {
                    case 0: // top
                        rect.anchorMin = new Vector2(0f, 1f);
                        rect.anchorMax = new Vector2(1f, 1f);
                        rect.offsetMin = new Vector2(inset, -(inset + thickness));
                        rect.offsetMax = new Vector2(-inset, -inset);
                        break;
                    case 1: // bottom
                        rect.anchorMin = new Vector2(0f, 0f);
                        rect.anchorMax = new Vector2(1f, 0f);
                        rect.offsetMin = new Vector2(inset, inset);
                        rect.offsetMax = new Vector2(-inset, inset + thickness);
                        break;
                    case 2: // left
                        rect.anchorMin = new Vector2(0f, 0f);
                        rect.anchorMax = new Vector2(0f, 1f);
                        rect.offsetMin = new Vector2(inset, inset);
                        rect.offsetMax = new Vector2(inset + thickness, -inset);
                        break;
                    default: // right
                        rect.anchorMin = new Vector2(1f, 0f);
                        rect.anchorMax = new Vector2(1f, 1f);
                        rect.offsetMin = new Vector2(-(inset + thickness), inset);
                        rect.offsetMax = new Vector2(-inset, -inset);
                        break;
                }

                var image = strip.GetComponent<Image>();
                image.color = color;
                image.raycastTarget = false;
            }
        }

        private void BuildDetailsPanel(Transform parent)
        {
            var panel = new GameObject("DetailsPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            detailsPanelRect = panel.GetComponent<RectTransform>();
            detailsPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
            detailsPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
            detailsPanelRect.pivot = new Vector2(0.5f, 0.5f);
            detailsPanelRect.sizeDelta = new Vector2(PanelWidth, CardHeight);
            detailsPanelRect.anchoredPosition = new Vector2(PanelCenterX, ContentCenterY);

            GameplayHUDUI.ApplyPanelContainer(panel, GameplayHUDUI.PanelContainerType.Metal);

            var border = panel.AddComponent<Outline>();
            border.effectColor = new Color(1f, 1f, 1f, 0.08f);
            border.effectDistance = new Vector2(2f, -2f);

            // Rarity-colored stripe along the top edge
            var accent = new GameObject("DetailAccent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(panel.transform, false);
            var accentRect = accent.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.sizeDelta = new Vector2(-32f, 4f);
            accentRect.anchoredPosition = new Vector2(0f, -14f);
            detailAccent = accent.GetComponent<Image>();
            detailAccent.color = new Color(ACCENT_COLOR.r, ACCENT_COLOR.g, ACCENT_COLOR.b, 0.35f);
            detailAccent.raycastTarget = false;

            // --- Placeholder (nothing selected) ---
            detailEmptyRoot = new GameObject("Empty", typeof(RectTransform));
            detailEmptyRoot.transform.SetParent(panel.transform, false);
            StretchFill(detailEmptyRoot.GetComponent<RectTransform>());

            var emptyTitle = MakeText(detailEmptyRoot.transform, "EmptyTitle", "SELECT A CARD", 24,
                TextAnchor.MiddleCenter, new Color(STAT_LABEL_COLOR.r, STAT_LABEL_COLOR.g, STAT_LABEL_COLOR.b, 0.8f));
            emptyTitle.fontStyle = FontStyle.Bold;
            PositionRT(emptyTitle, new Vector2(0f, 14f), new Vector2(0.5f, 0.5f), new Vector2(PanelWidth - 60f, 40f));

            var emptySub = MakeText(detailEmptyRoot.transform, "EmptySubtitle", "to see its full details here", 14,
                TextAnchor.MiddleCenter, new Color(STAT_LABEL_COLOR.r, STAT_LABEL_COLOR.g, STAT_LABEL_COLOR.b, 0.5f));
            PositionRT(emptySub, new Vector2(0f, -22f), new Vector2(0.5f, 0.5f), new Vector2(PanelWidth - 60f, 28f));

            // --- Content (operator selected) ---
            detailContentRoot = new GameObject("Content", typeof(RectTransform), typeof(CanvasGroup));
            detailContentRoot.transform.SetParent(panel.transform, false);
            detailContentRect = detailContentRoot.GetComponent<RectTransform>();
            StretchFill(detailContentRect);
            detailContentGroup = detailContentRoot.GetComponent<CanvasGroup>();
            Transform content = detailContentRoot.transform;

            const float margin = 28f;
            float innerWidth = PanelWidth - margin * 2f;

            // Name + stars
            detailNameText = MakeText(content, "DetailName", "", 32, TextAnchor.MiddleLeft, Color.white);
            detailNameText.fontStyle = FontStyle.Bold;
            detailNameText.resizeTextForBestFit = true;
            detailNameText.resizeTextMinSize = 18;
            detailNameText.resizeTextMaxSize = 32;
            StretchTopRT(detailNameText, margin, margin, 28f, 42f);

            detailStarsText = MakeText(content, "DetailStars", "", 22, TextAnchor.MiddleLeft, STAR_COLOR);
            StretchTopRT(detailStarsText, margin, margin, 72f, 28f);

            // Type chips (class / position / damage type)
            float chipWidth = (innerWidth - 24f) / 3f;
            detailClassChip = CreateChip(content, "ClassChip", margin, 114f, chipWidth, 34f, true);
            detailPositionChip = CreateChip(content, "PositionChip", margin + chipWidth + 12f, 114f, chipWidth, 34f);
            detailDamageChip = CreateChip(content, "DamageChip", margin + (chipWidth + 12f) * 2f, 114f, chipWidth, 34f);

            // Role line (role tags / class summary)
            detailRoleText = MakeText(content, "DetailRole", "", 12, TextAnchor.MiddleLeft,
                new Color(0.70f, 0.85f, 0.95f, 0.85f));
            detailRoleText.resizeTextForBestFit = true;
            detailRoleText.resizeTextMinSize = 9;
            detailRoleText.resizeTextMaxSize = 12;
            StretchTopRT(detailRoleText, margin, margin, 156f, 22f);

            CreateDivider(content, 190f, margin);

            // Stats
            var statsLabel = MakeText(content, "StatsLabel", "STATS", 11, TextAnchor.MiddleLeft, STAT_LABEL_COLOR);
            statsLabel.fontStyle = FontStyle.Bold;
            StretchTopRT(statsLabel, margin, margin, 202f, 18f);

            float tileWidth = (innerWidth - 12f) / 2f;
            float tileHeight = 62f;
            float rowStep = tileHeight + 8f;
            float gridTop = 226f;
            float rightX = margin + tileWidth + 12f;

            detailHpText = CreateStatTile(content, "HP", new Color(0.35f, 0.85f, 0.45f, 1f), margin, gridTop, tileWidth, tileHeight);
            detailAtkText = CreateStatTile(content, "ATK", new Color(0.95f, 0.40f, 0.35f, 1f), rightX, gridTop, tileWidth, tileHeight);
            detailDefText = CreateStatTile(content, "DEF", new Color(0.35f, 0.60f, 1.00f, 1f), margin, gridTop + rowStep, tileWidth, tileHeight);
            detailResText = CreateStatTile(content, "RES", new Color(0.70f, 0.45f, 1.00f, 1f), rightX, gridTop + rowStep, tileWidth, tileHeight);
            detailBlockText = CreateStatTile(content, "BLOCK", new Color(1.00f, 0.80f, 0.30f, 1f), margin, gridTop + rowStep * 2f, tileWidth, tileHeight);
            detailIntervalText = CreateStatTile(content, "ATK SPD", new Color(0.40f, 0.85f, 0.90f, 1f), rightX, gridTop + rowStep * 2f, tileWidth, tileHeight);

            // Bottom section: range preview (left) + ability (right)
            const float sectionLabelTop = 446f;
            const float sectionBodyTop = 470f;
            CreateDivider(content, 436f, margin);
            BuildRangePreview(content, margin, sectionLabelTop, sectionBodyTop);

            float abilityX = margin + RangeBoxWidth + 16f;

            // Passive / ability (hidden when the operator has none)
            detailPassiveLabel = MakeText(content, "PassiveLabel", "ABILITY", 11, TextAnchor.MiddleLeft, STAT_LABEL_COLOR);
            detailPassiveLabel.fontStyle = FontStyle.Bold;
            StretchTopRT(detailPassiveLabel, abilityX, margin, sectionLabelTop, 18f);

            detailPassiveText = MakeText(content, "PassiveText", "", 15, TextAnchor.UpperLeft,
                new Color(0.88f, 0.92f, 0.96f, 1f));
            detailPassiveText.resizeTextForBestFit = true;
            detailPassiveText.resizeTextMinSize = 11;
            detailPassiveText.resizeTextMaxSize = 15;
            StretchTopRT(detailPassiveText, abilityX, margin, sectionBodyTop, RangeBoxHeight);

            detailContentRoot.SetActive(false);
        }

        private void BuildRangePreview(Transform parent, float x, float labelTop, float boxTop)
        {
            var rangeLabel = MakeText(parent, "RangeLabel", "RANGE", 11, TextAnchor.MiddleLeft, STAT_LABEL_COLOR);
            rangeLabel.fontStyle = FontStyle.Bold;
            PlaceTopLeft(rangeLabel.transform, x, labelTop, RangeBoxWidth, 18f);

            // Facing hint: the diagram is drawn facing right, the default placement facing.
            var facingText = MakeText(parent, "RangeFacing", "FACING", 10, TextAnchor.MiddleRight,
                new Color(STAT_LABEL_COLOR.r, STAT_LABEL_COLOR.g, STAT_LABEL_COLOR.b, 0.7f));
            PlaceTopLeft(facingText.transform, x, labelTop, RangeBoxWidth - 16f, 18f);

            Sprite arrowSprite = LoadUiSprite("UI/minis/mini_arrow_right", Vector4.zero);
            if (arrowSprite != null)
            {
                var arrow = new GameObject("RangeFacingArrow", typeof(RectTransform), typeof(Image));
                arrow.transform.SetParent(parent, false);
                PlaceTopLeft(arrow.transform, x + RangeBoxWidth - 12f, labelTop + 4f, 10f, 10f);
                var arrowImage = arrow.GetComponent<Image>();
                arrowImage.sprite = arrowSprite;
                arrowImage.preserveAspect = true;
                arrowImage.color = SELF_TILE_COLOR;
                arrowImage.raycastTarget = false;
            }

            var box = new GameObject("RangeBox", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(parent, false);
            PlaceTopLeft(box.transform, x, boxTop, RangeBoxWidth, RangeBoxHeight);
            GameplayHUDUI.ApplyPanelContainer(box, GameplayHUDUI.PanelContainerType.Dark);
            box.GetComponent<Image>().raycastTarget = false;

            var grid = new GameObject("RangeGrid", typeof(RectTransform));
            grid.transform.SetParent(box.transform, false);
            detailRangeGrid = grid.GetComponent<RectTransform>();
            detailRangeGrid.anchorMin = new Vector2(0.5f, 0.5f);
            detailRangeGrid.anchorMax = new Vector2(0.5f, 0.5f);
            detailRangeGrid.pivot = new Vector2(0.5f, 0.5f);
            detailRangeGrid.sizeDelta = Vector2.zero;
            detailRangeGrid.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// Draws <see cref="OperatorData.rangePattern"/> — the same offsets combat uses via
        /// GridManager.GetCellsInRange — as a tile grid facing right.
        /// </summary>
        private void UpdateRangePreview(OperatorData op)
        {
            if (detailRangeGrid == null) return;

            for (int i = detailRangeGrid.childCount - 1; i >= 0; i--)
            {
                Destroy(detailRangeGrid.GetChild(i).gameObject);
            }

            var rangeCells = new HashSet<Vector2Int>();
            if (op.rangePattern != null)
            {
                foreach (Vector2Int offset in op.rangePattern) rangeCells.Add(offset);
            }

            int minX = 0, maxX = 0, minY = 0, maxY = 0;
            foreach (Vector2Int cell in rangeCells)
            {
                minX = Mathf.Min(minX, cell.x);
                maxX = Mathf.Max(maxX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxY = Mathf.Max(maxY, cell.y);
            }

            // Pad small patterns to at least 3x3 so the operator's own tile has context around it.
            while (maxX - minX + 1 < 3) { if (-minX <= maxX) minX--; else maxX++; }
            while (maxY - minY + 1 < 3) { if (-minY <= maxY) minY--; else maxY++; }

            int cols = maxX - minX + 1;
            int rows = maxY - minY + 1;
            float availableWidth = RangeBoxWidth - RangeBoxPadding * 2f;
            float availableHeight = RangeBoxHeight - RangeBoxPadding * 2f;
            float tileSize = Mathf.Floor(Mathf.Min(MaxRangeTileSize,
                (availableWidth - RangeTileGap * (cols - 1)) / cols,
                (availableHeight - RangeTileGap * (rows - 1)) / rows));
            float step = tileSize + RangeTileGap;
            Sprite classIcon = GameplayHUDUI.GetClassIconSprite(op.operatorClass);

            for (int gy = minY; gy <= maxY; gy++)
            {
                for (int gx = minX; gx <= maxX; gx++)
                {
                    var cell = new Vector2Int(gx, gy);
                    var position = new Vector2(
                        Mathf.Round((gx - minX - (cols - 1) * 0.5f) * step),
                        Mathf.Round((gy - minY - (rows - 1) * 0.5f) * step));
                    bool isSelf = cell == Vector2Int.zero;
                    bool inRange = rangeCells.Contains(cell);
                    Color frame = isSelf ? SELF_TILE_COLOR : inRange ? RANGE_TILE_COLOR : EMPTY_TILE_COLOR;
                    CreateRangeTile(position, tileSize, frame, inRange, isSelf ? classIcon : null);
                }
            }
        }

        private void CreateRangeTile(Vector2 position, float size, Color frameColor, bool filled, Sprite icon)
        {
            if (rangeTileSprite == null)
            {
                rangeTileSprite = LoadUiSprite("UI/controls/checkbox_off", new Vector4(3f, 3f, 3f, 3f));
            }

            var tile = new GameObject("RangeTile", typeof(RectTransform), typeof(Image));
            tile.transform.SetParent(detailRangeGrid, false);
            var tileRect = tile.GetComponent<RectTransform>();
            tileRect.sizeDelta = new Vector2(size, size);
            tileRect.anchoredPosition = position;

            var frame = tile.GetComponent<Image>();
            frame.raycastTarget = false;
            if (rangeTileSprite != null)
            {
                frame.sprite = rangeTileSprite;
                frame.type = Image.Type.Sliced;
                frame.color = frameColor;
            }
            else
            {
                frame.color = new Color(frameColor.r, frameColor.g, frameColor.b, frameColor.a * 0.5f);
            }

            float inset = Mathf.Min(3f, size * 0.2f);
            if (filled)
            {
                var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                fill.transform.SetParent(tile.transform, false);
                var fillRect = fill.GetComponent<RectTransform>();
                StretchFill(fillRect);
                fillRect.offsetMin = new Vector2(inset, inset);
                fillRect.offsetMax = new Vector2(-inset, -inset);
                var fillImage = fill.GetComponent<Image>();
                fillImage.color = new Color(RANGE_TILE_COLOR.r, RANGE_TILE_COLOR.g, RANGE_TILE_COLOR.b, 0.3f);
                fillImage.raycastTarget = false;
            }

            if (icon != null)
            {
                var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconObject.transform.SetParent(tile.transform, false);
                var iconRect = iconObject.GetComponent<RectTransform>();
                StretchFill(iconRect);
                float iconInset = inset + 1f;
                iconRect.offsetMin = new Vector2(iconInset, iconInset);
                iconRect.offsetMax = new Vector2(-iconInset, -iconInset);
                var iconImage = iconObject.GetComponent<Image>();
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
            }
        }

        /// <summary>
        /// Loads a UI-kit sprite (single or multiple sprite mode) with Point filtering, adding a
        /// 9-slice border when the imported sprite has none.
        /// </summary>
        private static Sprite LoadUiSprite(string path, Vector4 border)
        {
            Sprite source = Resources.Load<Sprite>(path);
            if (source == null)
            {
                Sprite[] all = Resources.LoadAll<Sprite>(path);
                if (all != null && all.Length > 0) source = all[0];
            }
            if (source == null || source.texture == null) return source;

            source.texture.filterMode = FilterMode.Point;
            if (border == Vector4.zero || source.border != Vector4.zero) return source;

            return Sprite.Create(source.texture, source.rect, new Vector2(0.5f, 0.5f),
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
        }

        private Chip CreateChip(Transform parent, string name, float x, float y, float width, float height, bool showIcon = false)
        {
            var chip = new Chip();

            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            PlaceTopLeft(obj.transform, x, y, width, height);

            GameplayHUDUI.ApplyPanelContainer(obj, GameplayHUDUI.PanelContainerType.Dark);
            chip.bg = obj.GetComponent<Image>();
            chip.bg.raycastTarget = false;

            chip.outline = obj.AddComponent<Outline>();
            chip.outline.effectDistance = new Vector2(1.5f, -1.5f);
            chip.outline.effectColor = new Color(1f, 1f, 1f, 0.15f);

            if (showIcon)
            {
                var iconObject = new GameObject("ClassIcon", typeof(RectTransform), typeof(Image));
                iconObject.transform.SetParent(obj.transform, false);
                chip.icon = iconObject.GetComponent<Image>();
                chip.icon.preserveAspect = true;
                chip.icon.raycastTarget = false;
                RectTransform iconRect = chip.icon.rectTransform;
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(8f, 0f);
                iconRect.sizeDelta = new Vector2(22f, 22f);
            }

            chip.text = MakeText(obj.transform, "Label", "", 13, TextAnchor.MiddleCenter, Color.white);
            chip.text.fontStyle = FontStyle.Bold;
            chip.text.resizeTextForBestFit = true;
            chip.text.resizeTextMinSize = 9;
            chip.text.resizeTextMaxSize = 13;
            if (showIcon)
            {
                RectTransform textRect = chip.text.rectTransform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(32f, 0f);
                textRect.offsetMax = new Vector2(-4f, 0f);
            }
            else
            {
                StretchFill(chip.text.rectTransform);
            }

            return chip;
        }

        private Text CreateStatTile(Transform parent, string label, Color accent, float x, float y, float width, float height)
        {
            var tile = new GameObject($"Stat_{label}", typeof(RectTransform), typeof(Image));
            tile.transform.SetParent(parent, false);
            PlaceTopLeft(tile.transform, x, y, width, height);

            GameplayHUDUI.ApplyPanelContainer(tile, GameplayHUDUI.PanelContainerType.Dark);
            var tileImage = tile.GetComponent<Image>();
            tileImage.raycastTarget = false;

            // Colored accent bar on the left edge inside the panel border
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(tile.transform, false);
            var barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.sizeDelta = new Vector2(4f, -16f);
            barRect.anchoredPosition = new Vector2(10f, 0f);
            var barImage = bar.GetComponent<Image>();
            barImage.color = accent;
            barImage.raycastTarget = false;

            var labelText = MakeText(tile.transform, "Label", label, 11, TextAnchor.MiddleLeft, STAT_LABEL_COLOR);
            labelText.fontStyle = FontStyle.Bold;
            PlaceTopLeft(labelText.transform, 22f, 8f, width - 30f, 18f);

            var valueText = MakeText(tile.transform, "Value", "0", 26, TextAnchor.MiddleLeft, STAT_VALUE_COLOR);
            valueText.fontStyle = FontStyle.Bold;
            PlaceTopLeft(valueText.transform, 22f, 26f, width - 30f, 32f);

            return valueText;
        }

        private static void CreateDivider(Transform parent, float top, float margin)
        {
            var divider = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(parent, false);
            StretchTopRT(divider.transform, margin, margin, top, 2f);
            var image = divider.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.08f);
            image.raycastTarget = false;
        }

        // ============================
        // UI Helpers
        // ============================

        private static Text MakeText(Transform parent, string name, string value,
            int fontSize, TextAnchor alignment, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            var text = obj.GetComponent<Text>();
            text.text = value;
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private static Button MakeButton(Transform parent, string name, string label,
            Vector2 size, GameplayHUDUI.ButtonContainerType containerType, string iconName = null)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<RectTransform>().sizeDelta = size;

            var button = obj.GetComponent<Button>();
            GameplayHUDUI.ApplyButtonContainer(button, containerType);

            float iconOffset = 0f;
            if (!string.IsNullOrEmpty(iconName))
            {
                var iconSprite = GameplayHUDUI.LoadIconSprite(iconName);
                if (iconSprite != null)
                {
                    var iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                    iconObj.transform.SetParent(obj.transform, false);
                    var iconImg = iconObj.GetComponent<Image>();
                    iconImg.sprite = iconSprite;
                    iconImg.color = Color.white;
                    iconImg.preserveAspect = true;
                    iconImg.raycastTarget = false;

                    var iconRect = iconObj.GetComponent<RectTransform>();
                    iconRect.anchorMin = new Vector2(0f, 0.5f);
                    iconRect.anchorMax = new Vector2(0f, 0.5f);
                    iconRect.pivot = new Vector2(0f, 0.5f);
                    iconRect.anchoredPosition = new Vector2(16f, 0f);
                    iconRect.sizeDelta = new Vector2(24f, 24f);
                    iconOffset = 28f;
                }
            }

            var text = MakeText(obj.transform, "Label", label, 16, TextAnchor.MiddleCenter, Color.white);
            text.fontStyle = FontStyle.Bold;
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(iconOffset, 0f);
            textRect.offsetMax = Vector2.zero;
            text.raycastTarget = false;

            return button;
        }

        private static Button MakeButton(Transform parent, string name, string label,
            Vector2 size, Color bgColor)
        {
            var container = (bgColor.r > 0.4f && bgColor.g < 0.3f && bgColor.b < 0.3f) ? GameplayHUDUI.ButtonContainerType.Danger :
                            (bgColor.g > 0.4f && bgColor.g > bgColor.r) ? GameplayHUDUI.ButtonContainerType.Primary :
                            GameplayHUDUI.ButtonContainerType.Dark;
            return MakeButton(parent, name, label, size, container);
        }

        private static void PositionRT(Component comp, Vector2 anchoredPos, Vector2 anchor, Vector2 size)
        {
            var rt = comp.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Full-width strip hanging from the top edge of its parent.</summary>
        private static void StretchTopRT(Component comp, float left, float right, float top, float height)
        {
            var rt = comp.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -(top + height));
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Fixed-size box positioned from the top-left corner of its parent.</summary>
        private static void PlaceTopLeft(Component comp, float x, float y, float width, float height)
        {
            var rt = comp.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static string GetOperatorDescription(OperatorData opData)
        {
            if (opData == null) return "";
            if (opData.roleTags != null && opData.roleTags.Length > 0)
            {
                return string.Join(" / ", opData.roleTags);
            }

            return opData.operatorClass switch
            {
                OperatorClass.Guard => "Melee Combatant / Physical DPS",
                OperatorClass.Defender => "Heavy Defense / Blocks 3",
                OperatorClass.Sniper => "Ranged Sniper / High Range",
                OperatorClass.Caster => "Arts Damage / Magic Attacks",
                OperatorClass.Medic => "Combat Support / Restores HP",
                OperatorClass.Specialist => "Specialist / Fast Redeploy & Mimic",
                _ => "Operator"
            };
        }
    }
}