using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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

        // --- References ---
        private Canvas canvas;
        private GameObject overlayRoot;
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

        // --- Buttons ---
        private Button confirmButton;
        private Text confirmButtonText;
        private Button rerollButton;
        private Text rerollButtonText;
        private Button skipButton;

        // --- State ---
        private int selectedCardIndex = -1;
        private Text titleText;
        private Text deckCountText;
        private Text waveInfoText;

        // ============================
        // Lifecycle
        // ============================

        private void Awake()
        {
            draftSystem = FindFirstObjectByType<CardDraftSystem>();
            playerDeck = FindFirstObjectByType<PlayerDeck>();
            gameManager = FindFirstObjectByType<GameManager>();

            if (draftSystem != null)
            {
                draftSystem.OnCardsOffered += HandleCardsOffered;
            }
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
            else if (draftSystem != null && draftSystem.CurrentOfferedCards.Count > 0)
            {
                HandleCardsOffered(draftSystem.CurrentOfferedCards);
            }
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
        }

        // ============================
        // Phase Integration
        // ============================

        private void HandlePhaseChanged(StagePhase phase)
        {
            if (phase == StagePhase.CardPick)
            {
                ShowDraftOverlay();
            }
            else
            {
                HideDraftOverlay();
            }
        }

        private void ShowDraftOverlay()
        {
            selectedCardIndex = -1;
            overlayRoot.SetActive(true);

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

        private void HideDraftOverlay()
        {
            overlayRoot.SetActive(false);
        }

        // ============================
        // Card Display
        // ============================

        private void HandleCardsOffered(IReadOnlyList<DraftCard> cards)
        {
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

                    // Rarity stars
                    int starCount = (int)rarity;
                    rarityTexts[i].text = new string('★', starCount) + new string('☆', 5 - starCount);

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

                    // Stats (scaled by rarity)
                    hpTexts[i].text = opData.GetScaledHP(rarity).ToString();
                    atkTexts[i].text = opData.GetScaledATK(rarity).ToString();
                    defTexts[i].text = opData.GetScaledDEF(rarity).ToString();
                    resTexts[i].text = opData.GetScaledRES(rarity).ToString();
                    blockTexts[i].text = opData.blockCount.ToString();
                    rangeTexts[i].text = opData.attackRange.ToString();
                    descTexts[i].text = GetOperatorDescription(opData);
                    positionTexts[i].text = opData.position == OperatorPosition.Melee ? "MELEE" : "RANGED";

                    // Reset card highlight
                    cardBackgrounds[i].color = CARD_BG;
                }
            }

            UpdateConfirmButton();
            UpdateRerollButton();
            UpdateDeckCount();
        }

        // ============================
        // User Actions
        // ============================

        private void OnCardClicked(int index)
        {
            if (index < 0 || draftSystem == null || index >= draftSystem.CurrentOfferedCards.Count)
                return;

            selectedCardIndex = index;

            // Update card highlights
            for (int i = 0; i < 3; i++)
            {
                if (cardBackgrounds[i] != null)
                {
                    cardBackgrounds[i].color = (i == selectedCardIndex) ? CARD_SELECTED : CARD_BG;
                }
            }

            UpdateConfirmButton();
        }

        private void OnConfirmClicked()
        {
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
            if (draftSystem == null) return;

            var newOffer = draftSystem.RerollOffer();
            if (newOffer == null)
            {
                Debug.LogWarning("No rerolls remaining!");
            }

            UpdateRerollButton();
        }

        private void OnSkipClicked()
        {
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

            var bg = confirmButton.GetComponent<Image>();
            if (bg != null) bg.color = canConfirm ? CONFIRM_COLOR : CONFIRM_DISABLED;

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
        }

        private void UpdateRerollButton()
        {
            if (draftSystem == null) return;

            int remaining = draftSystem.RerollsRemaining;
            bool canReroll = remaining > 0;
            rerollButton.interactable = canReroll;

            var bg = rerollButton.GetComponent<Image>();
            if (bg != null) bg.color = canReroll ? REROLL_COLOR : REROLL_DISABLED;

            rerollButtonText.text = $"REROLL ({remaining})";
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
            overlayRoot = new GameObject("OverlayRoot", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(canvasObj.transform, false);
            var rootRect = overlayRoot.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            overlayRoot.GetComponent<Image>().color = BG_OVERLAY;

            // Title
            titleText = MakeText(overlayRoot.transform, "Title", "CHOOSE YOUR CARD", 42,
                TextAnchor.MiddleCenter, Color.white);
            PositionRT(titleText, new Vector2(0, -50f), new Vector2(0.5f, 1f), new Vector2(600f, 60f));

            // Wave info
            waveInfoText = MakeText(overlayRoot.transform, "WaveInfo", "NEXT: WAVE 1 / ?", 26,
                TextAnchor.MiddleCenter, STAT_LABEL_COLOR);
            PositionRT(waveInfoText, new Vector2(0, -105f), new Vector2(0.5f, 1f), new Vector2(400f, 40f));

            // Deck count
            deckCountText = MakeText(overlayRoot.transform, "DeckCount", "DECK 0/8", 22,
                TextAnchor.MiddleRight, Color.white);
            PositionRT(deckCountText, new Vector2(-28f, -32f), new Vector2(1f, 1f), new Vector2(300f, 48f));
            deckCountText.rectTransform.pivot = new Vector2(1f, 1f);
            deckCountText.resizeTextForBestFit = true;
            deckCountText.resizeTextMinSize = 14;
            deckCountText.resizeTextMaxSize = 22;

            // Card container (centered row of 3 cards)
            var cardContainer = new GameObject("CardContainer", typeof(RectTransform));
            cardContainer.transform.SetParent(overlayRoot.transform, false);
            var containerRect = cardContainer.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.sizeDelta = new Vector2(1050f, 550f);
            containerRect.anchoredPosition = new Vector2(0f, 20f);

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

            // Confirm button
            confirmButton = MakeButton(buttonsContainer.transform, "ConfirmButton", "SELECT A CARD",
                new Vector2(260f, 60f), CONFIRM_DISABLED);
            confirmButtonText = confirmButton.GetComponentInChildren<Text>();
            confirmButton.onClick.AddListener(OnConfirmClicked);
            confirmButton.interactable = false;

            // Skip button
            skipButton = MakeButton(buttonsContainer.transform, "SkipButton", "SKIP",
                new Vector2(140f, 60f), SKIP_COLOR);
            skipButton.onClick.AddListener(OnSkipClicked);
        }

        private void BuildCard(Transform parent, int index)
        {
            float cardWidth = 310f;
            float cardHeight = 520f;

            // Card root
            var cardObj = new GameObject($"Card_{index}", typeof(RectTransform), typeof(Image), typeof(Button));
            cardObj.transform.SetParent(parent, false);
            var cardRect = cardObj.GetComponent<RectTransform>();
            cardRect.sizeDelta = new Vector2(cardWidth, cardHeight);

            var cardImg = cardObj.GetComponent<Image>();
            cardImg.color = CARD_BG;

            var cardBtn = cardObj.GetComponent<Button>();
            int capturedIndex = index;
            cardBtn.onClick.AddListener(() => OnCardClicked(capturedIndex));

            // Set button colors for hover feedback
            var colors = cardBtn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.2f, 1f);
            colors.pressedColor = new Color(0.9f, 0.9f, 0.95f, 1f);
            cardBtn.colors = colors;

            cardPanels[index] = cardObj;
            cardBackgrounds[index] = cardImg;

            float yOffset = cardHeight * 0.5f;
            float currentY = yOffset;

            // --- Rarity stars (top) ---
            currentY -= 30f;
            rarityTexts[index] = MakeText(cardObj.transform, $"Rarity_{index}", "★★★☆☆", 22,
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
            portraitBg.GetComponent<Image>().color = PORTRAIT_BG;

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

            // --- Name ---
            currentY -= 110f;
            nameTexts[index] = MakeText(cardObj.transform, $"Name_{index}", "", 24,
                TextAnchor.MiddleCenter, Color.white);
            PositionRT(nameTexts[index], new Vector2(0, currentY), new Vector2(0.5f, 0.5f),
                new Vector2(cardWidth - 20f, 32f));
            nameTexts[index].fontStyle = FontStyle.Bold;

            // --- Class + Position ---
            currentY -= 28f;
            classTexts[index] = MakeText(cardObj.transform, $"Class_{index}", "", 16,
                TextAnchor.MiddleCenter, CLASS_COLOR);
            PositionRT(classTexts[index], new Vector2(-40f, currentY), new Vector2(0.5f, 0.5f),
                new Vector2(120f, 24f));

            positionTexts[index] = MakeText(cardObj.transform, $"Position_{index}", "", 14,
                TextAnchor.MiddleCenter, STAT_LABEL_COLOR);
            PositionRT(positionTexts[index], new Vector2(60f, currentY), new Vector2(0.5f, 0.5f),
                new Vector2(90f, 24f));

            // --- Description / Role Tags (no DP) ---
            currentY -= 26f;
            descTexts[index] = MakeText(cardObj.transform, $"Desc_{index}", "", 13,
                TextAnchor.MiddleCenter, new Color(0.7f, 0.85f, 0.95f, 1f));
            PositionRT(descTexts[index], new Vector2(0, currentY), new Vector2(0.5f, 0.5f),
                new Vector2(cardWidth - 20f, 26f));

            // --- Stats grid ---
            currentY -= 28f;
            float statStartY = currentY;
            float statLeftX = -70f;
            float statRightX = 70f;
            float statRowHeight = 24f;
            float statLabelWidth = 50f;
            float statValueWidth = 50f;

            // HP / ATK
            MakeStatRow(cardObj.transform, index, "HP", ref hpTexts[index],
                statLeftX, statStartY, statLabelWidth, statValueWidth, statRowHeight);
            MakeStatRow(cardObj.transform, index, "ATK", ref atkTexts[index],
                statRightX, statStartY, statLabelWidth, statValueWidth, statRowHeight);

            // DEF / RES
            statStartY -= statRowHeight + 4f;
            MakeStatRow(cardObj.transform, index, "DEF", ref defTexts[index],
                statLeftX, statStartY, statLabelWidth, statValueWidth, statRowHeight);
            MakeStatRow(cardObj.transform, index, "RES", ref resTexts[index],
                statRightX, statStartY, statLabelWidth, statValueWidth, statRowHeight);

            // Block / Range
            statStartY -= statRowHeight + 4f;
            MakeStatRow(cardObj.transform, index, "BLK", ref blockTexts[index],
                statLeftX, statStartY, statLabelWidth, statValueWidth, statRowHeight);
            MakeStatRow(cardObj.transform, index, "RNG", ref rangeTexts[index],
                statRightX, statStartY, statLabelWidth, statValueWidth, statRowHeight);
        }

        private void MakeStatRow(Transform parent, int cardIndex, string label,
            ref Text valueText, float centerX, float y, float labelW, float valueW, float h)
        {
            // Label
            var labelText = MakeText(parent, $"StatLabel_{label}_{cardIndex}", label, 14,
                TextAnchor.MiddleRight, STAT_LABEL_COLOR);
            PositionRT(labelText, new Vector2(centerX - valueW * 0.5f - 2f, y),
                new Vector2(0.5f, 0.5f), new Vector2(labelW, h));

            // Value
            valueText = MakeText(parent, $"StatValue_{label}_{cardIndex}", "0", 16,
                TextAnchor.MiddleLeft, STAT_VALUE_COLOR);
            PositionRT(valueText, new Vector2(centerX + labelW * 0.5f + 2f, y),
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
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

            var text = MakeText(obj.transform, "Label", label, 20, TextAnchor.MiddleCenter, Color.white);
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
                return string.Join(" • ", opData.roleTags);
            }

            return opData.operatorClass switch
            {
                OperatorClass.Guard => "Melee Combatant • Physical DPS",
                OperatorClass.Defender => "Heavy Defense • Blocks 3",
                OperatorClass.Sniper => "Ranged Sniper • High Range",
                OperatorClass.Caster => "Arts Damage • Magic Attacks",
                OperatorClass.Medic => "Combat Support • Restores HP",
                _ => "Operator"
            };
        }
    }
}
