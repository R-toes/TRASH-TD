using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TrashTD.Core.GameLoop;
using TrashTD.Data;
using TrashTD.Systems;

namespace TrashTD.UI
{
    /// <summary>
    /// Runtime-built fullscreen card draft overlay.
    /// Shows 3 cards with operator portrait, name, class, rarity stars, and stats.
    /// Player selects a card, then clicks Confirm to add it to their deck.
    /// Includes a Reroll button (up to 3 rerolls per stage).
    /// Integrates with GameManager phase system: shown during CardPick phase.
    /// Visual polish: fade-in, cards dealt in one by one, rarity/class colors, selection feedback,
    /// hover/press feedback (MenuButtonFeedback), keyboard shortcuts and optional UI sounds.
    /// </summary>
    public class CardDraftOverlayUI : MonoBehaviour
    {
        // --- Colors ---
        private static readonly Color BG_OVERLAY = new Color(0.02f, 0.03f, 0.05f, 0.92f);
        private static readonly Color CARD_BG = new Color(0.08f, 0.10f, 0.14f, 1f);
        private static readonly Color CARD_SELECTED = new Color(0.15f, 0.25f, 0.45f, 1f);
        private static readonly Color CARD_HOVER = new Color(0.12f, 0.16f, 0.24f, 1f);
        private static readonly Color PORTRAIT_BG = new Color(0.06f, 0.07f, 0.10f, 1f);
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

        [Header("UI Sounds (optional)")]
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioClip clickClip;
        [SerializeField] private AudioClip confirmClip;
        [SerializeField] private AudioClip rerollClip;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.7f;

        // --- References ---
        private Canvas canvas;
        private GameObject overlayRoot;
        private CanvasGroup overlayGroup;
        private CardDraftSystem draftSystem;
        private PlayerDeck playerDeck;
        private GameManager gameManager;

        // --- Card UI ---
        private GameObject[] cardPanels = new GameObject[3];
        private Image[] cardBackgrounds = new Image[3];
        private Image[] portraitImages = new Image[3];
        private Text[] nameTexts = new Text[3];
        private Text[] classTexts = new Text[3];
        private Text[] rarityTexts = new Text[3];
        private Text[] hpTexts = new Text[3];
        private Text[] atkTexts = new Text[3];
        private Text[] defTexts = new Text[3];
        private Text[] resTexts = new Text[3];
        private Text[] blockTexts = new Text[3];
        private Text[] rangeTexts = new Text[3];
        private Text[] descTexts = new Text[3];
        private Text[] positionTexts = new Text[3];

        // --- Card polish ---
        private CanvasGroup[] cardGroups = new CanvasGroup[3];
        private MenuButtonFeedback[] cardFeedbacks = new MenuButtonFeedback[3];
        private Image[] cardAccents = new Image[3];
        private Outline[] portraitFrames = new Outline[3];
        private Text[] portraitFallbackTexts = new Text[3];
        private Text[] selectHintTexts = new Text[3];

        // --- Buttons ---
        private Button confirmButton;
        private Text confirmButtonText;
        private MenuButtonFeedback confirmFeedback;
        private Button rerollButton;
        private Text rerollButtonText;
        private MenuButtonFeedback rerollFeedback;
        private Button skipButton;
        private MenuButtonFeedback skipFeedback;

        // --- State ---
        private int selectedCardIndex = -1;
        private Text titleText;
        private Text deckCountText;
        private Text waveInfoText;
        private bool overlayVisible;

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
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

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
            draftSystem = FindFirstObjectByType<CardDraftSystem>();
            playerDeck = FindFirstObjectByType<PlayerDeck>();
            gameManager = FindFirstObjectByType<GameManager>();

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

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
            if (!overlayVisible || overlayRoot == null) return;

            // Confirm button breathes while a card can be confirmed
            if (confirmFeedback != null && confirmButton != null)
            {
                confirmFeedback.restScale = confirmButton.interactable
                    ? 1f + 0.025f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f))
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

            if (draftSystem != null)
            {
                draftSystem.OnCardsOffered -= HandleCardsOffered;
            }

            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        }

        private void HandleKeyboardShortcuts()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;

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
        }

        private void SelectCardByKey(int index)
        {
            if (index < 0 || index >= cardPanels.Length || cardPanels[index] == null || !cardPanels[index].activeSelf) return;

            PlaySfx(clickClip, 1f);
            cardFeedbacks[index].Punch();
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

            selectedCardIndex = -1;

            if (hideRoutine != null)
            {
                StopCoroutine(hideRoutine);
                hideRoutine = null;
            }

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
            if (overlayRoot == null || !overlayRoot.activeSelf) return;

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
            overlayVisible = false;
            if (hideRoutine != null)
            {
                StopCoroutine(hideRoutine);
                hideRoutine = null;
            }

            if (overlayRoot != null) overlayRoot.SetActive(false);
        }

        // ============================
        // Card Display
        // ============================

        private void HandleCardsOffered(IReadOnlyList<DraftCard> cards)
        {
            // The offer event can fire before the overlay has been built.
            if (overlayRoot == null) return;

            selectedCardIndex = -1;

            for (int i = 0; i < 3; i++)
            {
                bool hasCard = i < cards.Count && cards[i] != null && cards[i].operatorData != null;
                cardPanels[i].SetActive(hasCard);

                if (hasCard)
                {
                    var card = cards[i];
                    var opData = card.operatorData;
                    var rarity = card.rarity;

                    // Name
                    nameTexts[i].text = opData.operatorName;

                    // Class
                    classTexts[i].text = opData.operatorClass.ToString().ToUpper();
                    Color classColor = GetClassColor(opData.operatorClass);
                    classTexts[i].color = Color.Lerp(classColor, Color.white, 0.45f);
                    cardAccents[i].color = classColor;

                    int starCount = (int)rarity;
                    rarityTexts[i].text = new string('★', starCount) + new string('☆', 5 - starCount);

                    // Rarity tint: stars, portrait frame and hover/selection glow
                    Color rarityColor = GetRarityColor(starCount);
                    rarityTexts[i].color = rarityColor;
                    portraitFrames[i].effectColor = new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.6f);
                    cardFeedbacks[i].glowColor = new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.95f);

                    // Portrait
                    if (portraitImages[i] != null)
                    {
                        if (opData.portrait != null)
                        {
                            portraitImages[i].sprite = opData.portrait;
                            portraitImages[i].color = Color.white;
                        }
                        else
                        {
                            portraitImages[i].sprite = null;
                            portraitImages[i].color = PORTRAIT_BG;
                        }
                    }

                    // Placeholder initial when there is no portrait art
                    string initial = string.IsNullOrEmpty(opData.operatorName) ? "?" : opData.operatorName.Substring(0, 1).ToUpper();
                    portraitFallbackTexts[i].text = initial;
                    portraitFallbackTexts[i].enabled = opData.portrait == null;

                    // Stats (scaled by rarity)
                    hpTexts[i].text = opData.GetScaledHP(rarity).ToString();
                    atkTexts[i].text = opData.GetScaledATK(rarity).ToString();
                    defTexts[i].text = opData.GetScaledDEF(rarity).ToString();
                    resTexts[i].text = opData.GetScaledRES(rarity).ToString();
                    blockTexts[i].text = opData.blockCount.ToString();
                    rangeTexts[i].text = opData.attackRange.ToString();
                    descTexts[i].text = GetOperatorDescription(opData);
                    positionTexts[i].text = opData.position == OperatorPosition.Melee ? "MELEE" : "RANGED";
                }
            }

            ApplySelectionVisuals();
            StartDeal();

            UpdateConfirmButton();
            UpdateRerollButton();
            UpdateDeckCount();
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
                cardFeedbacks[i].restScale = selected ? 1.04f : (anySelected ? 0.97f : 1f);

                selectHintTexts[i].text = selected ? "✓ SELECTED" : "CLICK TO SELECT";
                selectHintTexts[i].color = selected ? ACCENT_BRIGHT : new Color(STAT_LABEL_COLOR.r, STAT_LABEL_COLOR.g, STAT_LABEL_COLOR.b, 0.6f);
            }
        }

        // ============================
        // User Actions
        // ============================

        private void OnCardClicked(int index)
        {
            ClearUiSelection();

            if (index < 0 || draftSystem == null || index >= draftSystem.CurrentOfferedCards.Count)
                return;

            selectedCardIndex = index;

            // Update card highlights
            ApplySelectionVisuals();

            UpdateConfirmButton();
        }

        private void OnConfirmClicked()
        {
            ClearUiSelection();

            if (selectedCardIndex < 0 || draftSystem == null || playerDeck == null)
                return;

            if (playerDeck.IsFull)
            {
                Debug.LogWarning("PlayerDeck: Deck is full! Cannot add more cards.");
                return;
            }

            // Select the card from the draft
            if (draftSystem.SelectCard(selectedCardIndex, out DraftCard selectedCard))
            {
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

            if (draftSystem == null) return;

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
            bool canConfirm = selectedCardIndex >= 0 && playerDeck != null && !playerDeck.IsFull;
            confirmButton.interactable = canConfirm;

            // Button tint transition is disabled (feedback component drives color), so show state here.
            confirmFeedback.SetBaseColor(canConfirm ? CONFIRM_COLOR : CONFIRM_DISABLED);
            confirmFeedback.glowAlways = canConfirm;

            if (playerDeck != null && playerDeck.IsFull)
            {
                confirmButtonText.text = "DECK FULL";
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

        private void UpdateRerollButton()
        {
            if (draftSystem == null) return;

            int remaining = draftSystem.RerollsRemaining;
            bool canReroll = remaining > 0;
            rerollButton.interactable = canReroll;

            rerollFeedback.SetBaseColor(canReroll ? REROLL_COLOR : REROLL_DISABLED);

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
                var waveManager = FindFirstObjectByType<WaveManager>();
                if (waveManager != null)
                {
                    int nextWave = gameManager.GetCurrentWaveIndex() + 1;
                    waveInfoText.text = $"NEXT: WAVE {nextWave} / {waveManager.TotalWaves}";
                }
            }
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
            float total = duration + 0.3f;
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

        private void StartDeal()
        {
            if (!isActiveAndEnabled) return;

            if (dealRoutine != null) StopCoroutine(dealRoutine);
            dealRoutine = StartCoroutine(DealCardsRoutine());
        }

        // Cards are "dealt" with scale, fade and a slight rotation. Position is left alone
        // because the HorizontalLayoutGroup owns it.
        private IEnumerator DealCardsRoutine()
        {
            const float duration = 0.42f;
            const float stagger = 0.1f;
            float total = duration + stagger * 2f;

            for (int i = 0; i < 3; i++) ApplyDeal(i, 0f);

            float t = 0f;
            while (t < total)
            {
                t += Time.unscaledDeltaTime;
                for (int i = 0; i < 3; i++)
                {
                    ApplyDeal(i, Mathf.Clamp01((t - i * stagger) / duration));
                }

                yield return null;
            }

            for (int i = 0; i < 3; i++) ApplyDeal(i, 1f);
        }

        private void ApplyDeal(int index, float k)
        {
            if (cardPanels[index] == null) return;

            float eased = EaseOutCubic(k);
            cardGroups[index].alpha = Mathf.Clamp01(k * 1.5f);
            cardFeedbacks[index].introScale = Mathf.LerpUnclamped(0.82f, 1f, EaseOutBack(k));
            cardPanels[index].transform.localRotation = Quaternion.Euler(0f, 0f, (1f - eased) * (index - 1) * -7f);
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
            if (clip == null || sfxSource == null) return;

            sfxSource.pitch = pitch;
            sfxSource.PlayOneShot(clip, sfxVolume);
        }

        private static void ClearUiSelection()
        {
            // Prevents Enter/Space from re-triggering the button that was just clicked.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private static Color GetRarityColor(int stars)
        {
            switch (stars)
            {
                case 1: return new Color(0.66f, 0.69f, 0.74f, 1f);
                case 2: return new Color(0.40f, 0.85f, 0.50f, 1f);
                case 3: return new Color(0.40f, 0.70f, 1.00f, 1f);
                case 4: return new Color(0.75f, 0.50f, 1.00f, 1f);
                default: return new Color(1.00f, 0.82f, 0.25f, 1f);
            }
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

            // Title
            titleText = MakeText(overlayRoot.transform, "Title", "CHOOSE YOUR CARD", 36,
                TextAnchor.MiddleCenter, Color.white);
            titleText.fontStyle = FontStyle.Bold;
            PositionRT(titleText, new Vector2(0, -48f), new Vector2(0.5f, 1f), new Vector2(700f, 60f));
            var titleShadow = titleText.gameObject.AddComponent<Shadow>();
            titleShadow.effectColor = new Color(ACCENT_COLOR.r, ACCENT_COLOR.g, ACCENT_COLOR.b, 0.35f);
            titleShadow.effectDistance = new Vector2(0f, -4f);

            // Wave info
            waveInfoText = MakeText(overlayRoot.transform, "WaveInfo", "NEXT: WAVE 1 / ?", 18,
                TextAnchor.MiddleCenter, STAT_LABEL_COLOR);
            PositionRT(waveInfoText, new Vector2(0, -104f), new Vector2(0.5f, 1f), new Vector2(500f, 40f));

            // Divider under the header
            var divider = new GameObject("HeaderDivider", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(overlayRoot.transform, false);
            var dividerImage = divider.GetComponent<Image>();
            dividerImage.color = new Color(ACCENT_COLOR.r, ACCENT_COLOR.g, ACCENT_COLOR.b, 0.4f);
            dividerImage.raycastTarget = false;
            PositionRT(dividerImage, new Vector2(0, -134f), new Vector2(0.5f, 1f), new Vector2(240f, 2f));

            // Deck count
            deckCountText = MakeText(overlayRoot.transform, "DeckCount", "DECK 0/8", 16,
                TextAnchor.MiddleRight, Color.white);
            PositionRT(deckCountText, new Vector2(-28f, -32f), new Vector2(1f, 1f), new Vector2(300f, 48f));
            deckCountText.rectTransform.pivot = new Vector2(1f, 1f);
            deckCountText.resizeTextForBestFit = true;
            deckCountText.resizeTextMinSize = 12;
            deckCountText.resizeTextMaxSize = 16;

            // Card container (centered row of 3 cards)
            var cardContainer = new GameObject("CardContainer", typeof(RectTransform));
            cardContainer.transform.SetParent(overlayRoot.transform, false);
            var containerRect = cardContainer.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.sizeDelta = new Vector2(1050f, 570f);
            containerRect.anchoredPosition = new Vector2(0f, 5f);

            var hlg = cardContainer.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 30f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            // Build 3 cards
            for (int i = 0; i < 3; i++)
            {
                BuildCard(cardContainer.transform, i);
            }

            // Bottom buttons container
            var buttonsContainer = new GameObject("ButtonsContainer", typeof(RectTransform));
            buttonsContainer.transform.SetParent(overlayRoot.transform, false);
            var buttonsRect = buttonsContainer.GetComponent<RectTransform>();
            buttonsRect.anchorMin = new Vector2(0.5f, 0f);
            buttonsRect.anchorMax = new Vector2(0.5f, 0f);
            buttonsRect.sizeDelta = new Vector2(700f, 70f);
            buttonsRect.anchoredPosition = new Vector2(0f, 80f);

            var btnHlg = buttonsContainer.AddComponent<HorizontalLayoutGroup>();
            btnHlg.spacing = 20f;
            btnHlg.childAlignment = TextAnchor.MiddleCenter;
            btnHlg.childControlWidth = false;
            btnHlg.childControlHeight = false;
            btnHlg.childForceExpandWidth = false;
            btnHlg.childForceExpandHeight = false;

            // Reroll button
            rerollButton = MakeButton(buttonsContainer.transform, "RerollButton", "REROLL (3)",
                new Vector2(200f, 60f), REROLL_COLOR);
            rerollButtonText = rerollButton.GetComponentInChildren<Text>();
            rerollButton.onClick.AddListener(OnRerollClicked);
            rerollFeedback = AttachFeedback(rerollButton, new Color(1f, 0.85f, 0.35f, 1f), 1.05f);

            // Confirm button
            confirmButton = MakeButton(buttonsContainer.transform, "ConfirmButton", "SELECT A CARD",
                new Vector2(260f, 60f), CONFIRM_DISABLED);
            confirmButtonText = confirmButton.GetComponentInChildren<Text>();
            confirmButton.onClick.AddListener(OnConfirmClicked);
            confirmButton.interactable = false;
            confirmFeedback = AttachFeedback(confirmButton, ACCENT_BRIGHT, 1.05f);

            // Skip button
            skipButton = MakeButton(buttonsContainer.transform, "SkipButton", "SKIP",
                new Vector2(140f, 60f), SKIP_COLOR);
            skipButton.onClick.AddListener(OnSkipClicked);
            skipFeedback = AttachFeedback(skipButton, Color.white, 1.05f);

            // Keyboard hint
            var hintText = MakeText(overlayRoot.transform, "KeyHints", "1 2 3  SELECT     ENTER  CONFIRM     R  REROLL", 12,
                TextAnchor.MiddleCenter, new Color(STAT_LABEL_COLOR.r, STAT_LABEL_COLOR.g, STAT_LABEL_COLOR.b, 0.55f));
            PositionRT(hintText, new Vector2(0f, 28f), new Vector2(0.5f, 0f), new Vector2(700f, 24f));

            // Header/footer entrance data (positions captured after layout above)
            introRects = new[]
            {
                titleText.rectTransform,
                waveInfoText.rectTransform,
                dividerImage.rectTransform,
                deckCountText.rectTransform,
                buttonsRect,
                hintText.rectTransform
            };
            introTargets = new Vector2[introRects.Length];
            for (int i = 0; i < introRects.Length; i++) introTargets[i] = introRects[i].anchoredPosition;
            introOffsets = new[]
            {
                new Vector2(0f, 40f), new Vector2(0f, 40f), new Vector2(0f, 40f),
                new Vector2(0f, 40f), new Vector2(0f, -60f), new Vector2(0f, -30f)
            };
            introDelays = new[] { 0f, 0.06f, 0.1f, 0.1f, 0.25f, 0.3f };
        }

        private void BuildCard(Transform parent, int index)
        {
            float cardWidth = 310f;
            float cardHeight = 540f;

            // Card root
            var cardObj = new GameObject($"Card_{index}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup));
            cardObj.transform.SetParent(parent, false);
            var cardRect = cardObj.GetComponent<RectTransform>();
            cardRect.sizeDelta = new Vector2(cardWidth, cardHeight);

            var cardImg = cardObj.GetComponent<Image>();
            cardImg.color = CARD_BG;

            var cardBtn = cardObj.GetComponent<Button>();
            int capturedIndex = index;
            cardBtn.onClick.AddListener(() => OnCardClicked(capturedIndex));

            // Hover / press / selection visuals come from MenuButtonFeedback (replaces the color tint).
            cardFeedbacks[index] = AttachFeedback(cardBtn, STAR_COLOR, 1.04f);
            cardGroups[index] = cardObj.GetComponent<CanvasGroup>();

            cardPanels[index] = cardObj;
            cardBackgrounds[index] = cardImg;

            // Class-colored accent stripe along the top edge
            var accent = new GameObject($"Accent_{index}", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(cardObj.transform, false);
            var accentRect = accent.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.sizeDelta = new Vector2(0f, 5f);
            cardAccents[index] = accent.GetComponent<Image>();
            cardAccents[index].color = CLASS_COLOR;
            cardAccents[index].raycastTarget = false;

            float yOffset = cardHeight * 0.5f;
            float currentY = yOffset;

            // --- Rarity stars (top) ---
            currentY -= 30f;
            rarityTexts[index] = MakeText(cardObj.transform, $"Rarity_{index}", "★★★☆☆", 18,
                TextAnchor.MiddleCenter, STAR_COLOR);
            PositionRT(rarityTexts[index], new Vector2(0, currentY), new Vector2(0.5f, 0.5f),
                new Vector2(cardWidth - 20f, 30f));

            // --- Portrait area ---
            currentY -= 130f;
            var portraitBg = new GameObject($"PortraitBg_{index}", typeof(RectTransform), typeof(Image));
            portraitBg.transform.SetParent(cardObj.transform, false);
            var portraitBgRect = portraitBg.GetComponent<RectTransform>();
            portraitBgRect.anchorMin = new Vector2(0.5f, 0.5f);
            portraitBgRect.anchorMax = new Vector2(0.5f, 0.5f);
            portraitBgRect.sizeDelta = new Vector2(160f, 160f);
            portraitBgRect.anchoredPosition = new Vector2(0, currentY);
            var portraitBgImage = portraitBg.GetComponent<Image>();
            portraitBgImage.color = PORTRAIT_BG;
            portraitBgImage.raycastTarget = false;

            // Rarity-colored frame around the portrait
            portraitFrames[index] = portraitBg.AddComponent<Outline>();
            portraitFrames[index].effectDistance = new Vector2(2f, -2f);
            portraitFrames[index].effectColor = new Color(1f, 1f, 1f, 0.15f);

            // Placeholder initial (shown when the operator has no portrait art)
            portraitFallbackTexts[index] = MakeText(portraitBg.transform, $"PortraitFallback_{index}", "?", 72,
                TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.12f));
            portraitFallbackTexts[index].fontStyle = FontStyle.Bold;
            var fallbackRect = portraitFallbackTexts[index].rectTransform;
            fallbackRect.anchorMin = Vector2.zero;
            fallbackRect.anchorMax = Vector2.one;
            fallbackRect.offsetMin = Vector2.zero;
            fallbackRect.offsetMax = Vector2.zero;

            var portraitObj = new GameObject($"Portrait_{index}", typeof(RectTransform), typeof(Image));
            portraitObj.transform.SetParent(portraitBg.transform, false);
            var portraitRect = portraitObj.GetComponent<RectTransform>();
            portraitRect.anchorMin = Vector2.zero;
            portraitRect.anchorMax = Vector2.one;
            portraitRect.offsetMin = new Vector2(8f, 8f);
            portraitRect.offsetMax = new Vector2(-8f, -8f);
            portraitImages[index] = portraitObj.GetComponent<Image>();
            portraitImages[index].preserveAspect = true;
            portraitImages[index].color = PORTRAIT_BG;
            portraitImages[index].raycastTarget = false;

            // --- Name ---
            currentY -= 115f;
            nameTexts[index] = MakeText(cardObj.transform, $"Name_{index}", "", 20,
                TextAnchor.MiddleCenter, Color.white);
            PositionRT(nameTexts[index], new Vector2(0, currentY), new Vector2(0.5f, 0.5f),
                new Vector2(cardWidth - 24f, 34f));
            nameTexts[index].fontStyle = FontStyle.Bold;

            // --- Class + Position ---
            currentY -= 45f;
            classTexts[index] = MakeText(cardObj.transform, $"Class_{index}", "", 12,
                TextAnchor.MiddleCenter, CLASS_COLOR);
            PositionRT(classTexts[index], new Vector2(-75f, currentY), new Vector2(0.5f, 0.5f),
                new Vector2(130f, 28f));

            positionTexts[index] = MakeText(cardObj.transform, $"Position_{index}", "", 12,
                TextAnchor.MiddleCenter, STAT_LABEL_COLOR);
            PositionRT(positionTexts[index], new Vector2(75f, currentY), new Vector2(0.5f, 0.5f),
                new Vector2(130f, 28f));

            // --- Description / Role Tags (no DP) ---
            currentY -= 44f;
            descTexts[index] = MakeText(cardObj.transform, $"Desc_{index}", "", 11,
                TextAnchor.MiddleCenter, new Color(0.7f, 0.85f, 0.95f, 1f));
            PositionRT(descTexts[index], new Vector2(0, currentY), new Vector2(0.5f, 0.5f),
                new Vector2(cardWidth - 28f, 34f));

            // --- Stats grid ---
            currentY -= 44f;
            float statStartY = currentY;
            float statLeftX = -75f;
            float statRightX = 75f;
            float statRowHeight = 28f;
            float statLabelWidth = 46f;
            float statValueWidth = 46f;

            // HP / ATK
            MakeStatRow(cardObj.transform, index, "HP", ref hpTexts[index],
                statLeftX, statStartY, statLabelWidth, statValueWidth, statRowHeight);
            MakeStatRow(cardObj.transform, index, "ATK", ref atkTexts[index],
                statRightX, statStartY, statLabelWidth, statValueWidth, statRowHeight);

            // DEF / RES
            statStartY -= statRowHeight + 7f;
            MakeStatRow(cardObj.transform, index, "DEF", ref defTexts[index],
                statLeftX, statStartY, statLabelWidth, statValueWidth, statRowHeight);
            MakeStatRow(cardObj.transform, index, "RES", ref resTexts[index],
                statRightX, statStartY, statLabelWidth, statValueWidth, statRowHeight);

            // Block / Range
            statStartY -= statRowHeight + 7f;
            MakeStatRow(cardObj.transform, index, "BLK", ref blockTexts[index],
                statLeftX, statStartY, statLabelWidth, statValueWidth, statRowHeight);
            MakeStatRow(cardObj.transform, index, "RNG", ref rangeTexts[index],
                statRightX, statStartY, statLabelWidth, statValueWidth, statRowHeight);

            // --- Select hint / selected badge (bottom) ---
            selectHintTexts[index] = MakeText(cardObj.transform, $"SelectHint_{index}", "CLICK TO SELECT", 12,
                TextAnchor.MiddleCenter, STAT_LABEL_COLOR);
            selectHintTexts[index].fontStyle = FontStyle.Bold;
            PositionRT(selectHintTexts[index], new Vector2(0, -(cardHeight * 0.5f) + 26f), new Vector2(0.5f, 0.5f),
                new Vector2(cardWidth - 20f, 26f));
        }

        private void MakeStatRow(Transform parent, int cardIndex, string label,
            ref Text valueText, float centerX, float y, float labelW, float valueW, float h)
        {
            // Label
            var labelText = MakeText(parent, $"StatLabel_{label}_{cardIndex}", label, 11,
                TextAnchor.MiddleRight, STAT_LABEL_COLOR);
            PositionRT(labelText, new Vector2(centerX - 28f, y),
                new Vector2(0.5f, 0.5f), new Vector2(labelW, h));

            // Value
            valueText = MakeText(parent, $"StatValue_{label}_{cardIndex}", "0", 13,
                TextAnchor.MiddleLeft, STAT_VALUE_COLOR);
            PositionRT(valueText, new Vector2(centerX + 28f, y),
                new Vector2(0.5f, 0.5f), new Vector2(valueW, h));
            valueText.fontStyle = FontStyle.Bold;
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
            Vector2 size, Color bgColor)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<RectTransform>().sizeDelta = size;
            obj.GetComponent<Image>().color = bgColor;

            var text = MakeText(obj.transform, "Label", label, 16, TextAnchor.MiddleCenter, Color.white);
            text.fontStyle = FontStyle.Bold;
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text.raycastTarget = false;

            return obj.GetComponent<Button>();
        }

        private static void PositionRT(Component comp, Vector2 anchoredPos, Vector2 anchor, Vector2 size)
        {
            var rt = comp.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
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
                _ => "Operator"
            };
        }
    }
}