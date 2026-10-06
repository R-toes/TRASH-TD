using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Left: 3 portrait-focused cards (portrait, name, rarity stars).
    /// Right: a detail panel for the selected card (name, class / position / damage-type chips,
    /// stats and passive/ability text).
    /// Player selects a card, then clicks Confirm to add it to their deck.
    /// Includes a Reroll button (up to 3 rerolls per stage).
    /// Integrates with GameManager phase system: shown during CardPick phase.
    /// Visual polish: fade-in, cards dealt in one by one, rarity colors, selection feedback,
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
        private Image[] portraitBgImages = new Image[3];
        private Image[] portraitImages = new Image[3];
        private Text[] nameTexts = new Text[3];
        private Text[] rarityTexts = new Text[3];

        // --- Card polish ---
        private CanvasGroup[] cardGroups = new CanvasGroup[3];
        private MenuButtonFeedback[] cardFeedbacks = new MenuButtonFeedback[3];
        private Image[] cardAccents = new Image[3];
        private Outline[] portraitFrames = new Outline[3];
        private Text[] portraitFallbackTexts = new Text[3];
        private Text[] selectHintTexts = new Text[3];

        // --- Detail panel ---
        private sealed class Chip
        {
            public Image bg;
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

            if (hoverClip == null) hoverClip = AudioManager.Instance?.GetClip(SfxId.UiHover);
            if (clickClip == null) clickClip = AudioManager.Instance?.GetClip(SfxId.UiClick);
            if (confirmClip == null) confirmClip = AudioManager.Instance?.GetClip(SfxId.UiConfirm);
            if (rerollClip == null) rerollClip = AudioManager.Instance?.GetClip(SfxId.UiClick);

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
                    int starCount = Mathf.Clamp((int)card.rarity, 1, 5);
                    Color rarityColor = GetRarityColor(starCount);

                    // Name + rarity (the only text on the card)
                    nameTexts[i].text = opData.operatorName;
                    rarityTexts[i].text = BuildStarString(starCount);
                    rarityTexts[i].color = rarityColor;

                    // Rarity tint: top stripe, portrait backdrop/frame and hover/selection glow
                    cardAccents[i].color = rarityColor;
                    portraitBgImages[i].color = Color.Lerp(PORTRAIT_BG, rarityColor, 0.10f);
                    portraitFrames[i].effectColor = new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.6f);
                    cardFeedbacks[i].glowColor = new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.95f);

                    SetPortrait(i, opData);
                }
            }

            ApplySelectionVisuals();
            UpdateDetailsPanel(false);
            StartDeal();

            UpdateConfirmButton();
            UpdateRerollButton();
            UpdateDeckCount();
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

            if (index < 0 || draftSystem == null || index >= draftSystem.CurrentOfferedCards.Count)
                return;

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

            // Card container (row of 3 cards, left side of the screen)
            var cardContainer = new GameObject("CardContainer", typeof(RectTransform));
            cardContainer.transform.SetParent(overlayRoot.transform, false);
            var containerRect = cardContainer.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.sizeDelta = new Vector2(CardWidth * 3f + CardSpacing * 2f, CardHeight);
            containerRect.anchoredPosition = new Vector2(ColumnCenterX, ContentCenterY);

            var hlg = cardContainer.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = CardSpacing;
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

            // Detail panel (right side of the screen)
            BuildDetailsPanel(overlayRoot.transform);

            // Reroll / Skip under the cards
            var buttonsContainer = new GameObject("ButtonsContainer", typeof(RectTransform));
            buttonsContainer.transform.SetParent(overlayRoot.transform, false);
            var buttonsRect = buttonsContainer.GetComponent<RectTransform>();
            buttonsRect.anchorMin = new Vector2(0.5f, 0f);
            buttonsRect.anchorMax = new Vector2(0.5f, 0f);
            buttonsRect.sizeDelta = new Vector2(420f, 64f);
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
                new Vector2(220f, 60f), REROLL_COLOR);
            rerollButtonText = rerollButton.GetComponentInChildren<Text>();
            rerollButton.onClick.AddListener(OnRerollClicked);
            rerollFeedback = AttachFeedback(rerollButton, new Color(1f, 0.85f, 0.35f, 1f), 1.05f);

            // Skip button
            skipButton = MakeButton(buttonsContainer.transform, "SkipButton", "SKIP",
                new Vector2(140f, 60f), SKIP_COLOR);
            skipButton.onClick.AddListener(OnSkipClicked);
            skipFeedback = AttachFeedback(skipButton, Color.white, 1.05f);

            // Confirm button (under the detail panel, where the decision is made)
            confirmButton = MakeButton(overlayRoot.transform, "ConfirmButton", "SELECT A CARD",
                new Vector2(PanelWidth, 64f), CONFIRM_DISABLED);
            PositionRT(confirmButton, new Vector2(PanelCenterX, 120f), new Vector2(0.5f, 0f), new Vector2(PanelWidth, 64f));
            confirmButtonText = confirmButton.GetComponentInChildren<Text>();
            confirmButtonText.fontSize = 20;
            confirmButton.onClick.AddListener(OnConfirmClicked);
            confirmButton.interactable = false;
            confirmFeedback = AttachFeedback(confirmButton, ACCENT_BRIGHT, 1.03f);

            // Keyboard hint
            var hintText = MakeText(overlayRoot.transform, "KeyHints", "1 2 3  SELECT     ENTER  CONFIRM     R  REROLL", 12,
                TextAnchor.MiddleCenter, new Color(STAT_LABEL_COLOR.r, STAT_LABEL_COLOR.g, STAT_LABEL_COLOR.b, 0.55f));
            PositionRT(hintText, new Vector2(0f, 28f), new Vector2(0.5f, 0f), new Vector2(700f, 24f));

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

        private void BuildCard(Transform parent, int index)
        {
            // Card root
            var cardObj = new GameObject($"Card_{index}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup));
            cardObj.transform.SetParent(parent, false);
            var cardRect = cardObj.GetComponent<RectTransform>();
            cardRect.sizeDelta = new Vector2(CardWidth, CardHeight);

            var cardImg = cardObj.GetComponent<Image>();
            cardImg.color = CARD_BG;

            var cardBtn = cardObj.GetComponent<Button>();
            int capturedIndex = index;
            cardBtn.onClick.AddListener(() => OnCardClicked(capturedIndex));

            // Hover / press / selection visuals come from MenuButtonFeedback (replaces the color tint).
            cardFeedbacks[index] = AttachFeedback(cardBtn, STAR_COLOR, 1.03f);
            cardGroups[index] = cardObj.GetComponent<CanvasGroup>();

            cardPanels[index] = cardObj;
            cardBackgrounds[index] = cardImg;

            // Rarity-colored accent stripe along the top edge
            var accent = new GameObject($"Accent_{index}", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(cardObj.transform, false);
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
            portraitBg.transform.SetParent(cardObj.transform, false);
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

            // --- Name (bottom) ---
            nameTexts[index] = MakeText(cardObj.transform, $"Name_{index}", "", 26,
                TextAnchor.MiddleCenter, Color.white);
            nameTexts[index].fontStyle = FontStyle.Bold;
            nameTexts[index].resizeTextForBestFit = true;
            nameTexts[index].resizeTextMinSize = 14;
            nameTexts[index].resizeTextMaxSize = 26;
            StretchTopRT(nameTexts[index], 14f, 14f, 484f, 44f);

            // --- Rarity stars (bottom) ---
            rarityTexts[index] = MakeText(cardObj.transform, $"Rarity_{index}", "", 28,
                TextAnchor.MiddleCenter, STAR_COLOR);
            StretchTopRT(rarityTexts[index], 14f, 14f, 530f, 40f);

            // --- Select hint / selected badge ---
            selectHintTexts[index] = MakeText(cardObj.transform, $"SelectHint_{index}", "CLICK TO SELECT", 12,
                TextAnchor.MiddleCenter, STAT_LABEL_COLOR);
            selectHintTexts[index].fontStyle = FontStyle.Bold;
            StretchTopRT(selectHintTexts[index], 14f, 14f, 596f, 24f);
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

            var panelImage = panel.GetComponent<Image>();
            panelImage.color = PANEL_BG;
            panelImage.raycastTarget = false;

            var border = panel.AddComponent<Outline>();
            border.effectColor = new Color(1f, 1f, 1f, 0.06f);
            border.effectDistance = new Vector2(2f, -2f);

            // Rarity-colored stripe along the top edge
            var accent = new GameObject("DetailAccent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(panel.transform, false);
            var accentRect = accent.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.sizeDelta = new Vector2(0f, 4f);
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
            detailClassChip = CreateChip(content, "ClassChip", margin, 114f, chipWidth, 34f);
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

            // Passive / ability (hidden when the operator has none)
            detailPassiveLabel = MakeText(content, "PassiveLabel", "ABILITY", 11, TextAnchor.MiddleLeft, STAT_LABEL_COLOR);
            detailPassiveLabel.fontStyle = FontStyle.Bold;
            StretchTopRT(detailPassiveLabel, margin, margin, 450f, 18f);

            detailPassiveText = MakeText(content, "PassiveText", "", 15, TextAnchor.UpperLeft,
                new Color(0.88f, 0.92f, 0.96f, 1f));
            detailPassiveText.resizeTextForBestFit = true;
            detailPassiveText.resizeTextMinSize = 11;
            detailPassiveText.resizeTextMaxSize = 15;
            StretchTopRT(detailPassiveText, margin, margin, 474f, 134f);

            detailContentRoot.SetActive(false);
        }

        private Chip CreateChip(Transform parent, string name, float x, float y, float width, float height)
        {
            var chip = new Chip();

            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            PlaceTopLeft(obj.transform, x, y, width, height);

            chip.bg = obj.GetComponent<Image>();
            chip.bg.color = TILE_BG;
            chip.bg.raycastTarget = false;

            chip.outline = obj.AddComponent<Outline>();
            chip.outline.effectDistance = new Vector2(1.5f, -1.5f);
            chip.outline.effectColor = new Color(1f, 1f, 1f, 0.2f);

            chip.text = MakeText(obj.transform, "Label", "", 13, TextAnchor.MiddleCenter, Color.white);
            chip.text.fontStyle = FontStyle.Bold;
            chip.text.resizeTextForBestFit = true;
            chip.text.resizeTextMinSize = 9;
            chip.text.resizeTextMaxSize = 13;
            StretchFill(chip.text.rectTransform);

            return chip;
        }

        private Text CreateStatTile(Transform parent, string label, Color accent, float x, float y, float width, float height)
        {
            var tile = new GameObject($"Stat_{label}", typeof(RectTransform), typeof(Image));
            tile.transform.SetParent(parent, false);
            PlaceTopLeft(tile.transform, x, y, width, height);
            var tileImage = tile.GetComponent<Image>();
            tileImage.color = TILE_BG;
            tileImage.raycastTarget = false;

            // Colored bar on the left edge
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(tile.transform, false);
            var barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.sizeDelta = new Vector2(4f, 0f);
            barRect.anchoredPosition = Vector2.zero;
            var barImage = bar.GetComponent<Image>();
            barImage.color = accent;
            barImage.raycastTarget = false;

            var labelText = MakeText(tile.transform, "Label", label, 11, TextAnchor.MiddleLeft, STAT_LABEL_COLOR);
            labelText.fontStyle = FontStyle.Bold;
            PlaceTopLeft(labelText.transform, 18f, 8f, width - 28f, 18f);

            var valueText = MakeText(tile.transform, "Value", "0", 26, TextAnchor.MiddleLeft, STAT_VALUE_COLOR);
            valueText.fontStyle = FontStyle.Bold;
            PlaceTopLeft(valueText.transform, 18f, 26f, width - 28f, 32f);

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
                _ => "Operator"
            };
        }
    }
}