using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TrashTD.Core.Grid;
using TrashTD.Core.GameLoop;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.UI
{
    /// <summary>
    /// Runtime-built in-game HUD for the stage view.
    /// Integrates with the phase loop: CardPick → Preparation → WaveActive.
    /// The creature bar shows operators in the player's deck (not draft offers).
    /// </summary>
    public class GameplayHUDUI : MonoBehaviour
    {
        private const int DeckSlotCount = 8;

        private Canvas canvas;
        private GameObject pausePanel;
        private GameObject stageInfoPanel;
        private Text waveText;
        private Text enemyText;
        private Text lpText;
        private Text phaseText;
        private Text squadCountText;
        private GameObject selectedOperatorLabelRoot;
        private Text selectedOperatorNameText;
        private Text selectedOperatorHealthText;
        private Text selectedOperatorRarityText;
        private Image selectedOperatorHealthFill;
        private GameObject placementControlsRoot;
        private Button retreatOperatorButton;
        private Button placementConfirmButton;
        private Button startWaveButton;
        private Text startWaveButtonText;
        private Button[] deckButtons;
        private Text[] deckButtonLabels;
        private Image[] deckButtonImages;
        private CardDraftSystem draftSystem;
        private WaveManager waveManager;
        private EnemyManager enemyManager;
        private GameManager gameManager;
        private PlayerDeck playerDeck;
        private GridManager gridManager;
        private OperatorManager operatorManager;
        private StageBootstrapper stageBootstrapper;

        private int selectedDeckSlot = -1;

        private void Awake()
        {
            draftSystem = FindFirstObjectByType<CardDraftSystem>();
            waveManager = FindFirstObjectByType<WaveManager>();
            enemyManager = FindFirstObjectByType<EnemyManager>();
            gameManager = FindFirstObjectByType<GameManager>();
            playerDeck = FindFirstObjectByType<PlayerDeck>();
            gridManager = FindFirstObjectByType<GridManager>();
            operatorManager = FindFirstObjectByType<OperatorManager>();
            stageBootstrapper = FindFirstObjectByType<StageBootstrapper>();

            EnsureEventSystem();
            BuildHud();
        }

        private void Start()
        {
            if (waveManager != null)
            {
                waveManager.OnWaveStarted += HandleWaveStarted;
                waveManager.OnSingleWaveFinished += HandleSingleWaveFinished;
            }

            if (enemyManager != null)
            {
                enemyManager.OnEnemySpawned += HandleEnemyCountChanged;
                enemyManager.OnEnemyDied += HandleEnemyCountChanged;
                enemyManager.OnEnemyReachedExit += HandleEnemyCountChanged;
            }

            if (gameManager != null)
            {
                gameManager.OnDPChanged += HandleDPChanged;
                gameManager.OnLifePointsChanged += HandleLPChanged;
                gameManager.OnPhaseChanged += HandlePhaseChanged;
            }

            if (playerDeck != null)
            {
                playerDeck.OnDeckChanged += HandleDeckChanged;
            }

            RefreshCounters();
            RefreshDeckSlots();
            UpdatePhaseUI();
        }

        private void Update()
        {
            RefreshCounters();
            RefreshSelectedOperatorInfo();
        }

        private void OnDestroy()
        {
            if (waveManager != null)
            {
                waveManager.OnWaveStarted -= HandleWaveStarted;
                waveManager.OnSingleWaveFinished -= HandleSingleWaveFinished;
            }

            if (enemyManager != null)
            {
                enemyManager.OnEnemySpawned -= HandleEnemyCountChanged;
                enemyManager.OnEnemyDied -= HandleEnemyCountChanged;
                enemyManager.OnEnemyReachedExit -= HandleEnemyCountChanged;
            }

            if (gameManager != null)
            {
                gameManager.OnDPChanged -= HandleDPChanged;
                gameManager.OnLifePointsChanged -= HandleLPChanged;
                gameManager.OnPhaseChanged -= HandlePhaseChanged;
            }

            if (playerDeck != null)
            {
                playerDeck.OnDeckChanged -= HandleDeckChanged;
            }
        }

        // ============================
        // HUD Construction
        // ============================

        private void BuildHud()
        {
            var canvasObject = new GameObject("GameplayHUDCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var root = canvasObject.transform;
            CreateTopBar(root);
            CreateDeckBar(root);
            CreatePausePanel(root);
            CreatePlacementControls(root);
            CreateSelectedOperatorLabel(root);
        }

        private void CreateSelectedOperatorLabel(Transform root)
        {
            selectedOperatorLabelRoot = new GameObject("SelectedOperatorName", typeof(RectTransform), typeof(Image));
            selectedOperatorLabelRoot.transform.SetParent(root, false);
            RectTransform labelRect = selectedOperatorLabelRoot.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(250f, 70f);

            Image background = selectedOperatorLabelRoot.GetComponent<Image>();
            background.color = new Color(0.025f, 0.035f, 0.045f, 0.9f);
            background.raycastTarget = false;

            selectedOperatorNameText = CreateText(selectedOperatorLabelRoot.transform, "Name", string.Empty, 16, TextAnchor.MiddleLeft);
            SetPosition(selectedOperatorNameText.GetComponent<RectTransform>(), new Vector2(10f, -7f), new Vector2(0f, 1f), new Vector2(145f, 24f), new Vector2(0f, 1f));
            selectedOperatorNameText.raycastTarget = false;

            selectedOperatorRarityText = CreateText(selectedOperatorLabelRoot.transform, "Rarity", string.Empty, 17, TextAnchor.MiddleRight);
            selectedOperatorRarityText.color = new Color(1f, 0.78f, 0.2f, 1f);
            SetPosition(selectedOperatorRarityText.GetComponent<RectTransform>(), new Vector2(-10f, -7f), new Vector2(1f, 1f), new Vector2(82f, 24f), new Vector2(1f, 1f));
            selectedOperatorRarityText.raycastTarget = false;

            selectedOperatorHealthText = CreateText(selectedOperatorLabelRoot.transform, "Health", string.Empty, 14, TextAnchor.MiddleLeft);
            SetPosition(selectedOperatorHealthText.GetComponent<RectTransform>(), new Vector2(10f, 9f), Vector2.zero, new Vector2(84f, 22f), Vector2.zero);
            selectedOperatorHealthText.raycastTarget = false;

            GameObject healthTrack = new GameObject("HealthTrack", typeof(RectTransform), typeof(Image));
            healthTrack.transform.SetParent(selectedOperatorLabelRoot.transform, false);
            Image healthTrackImage = healthTrack.GetComponent<Image>();
            healthTrackImage.color = new Color(0.15f, 0.17f, 0.19f, 1f);
            healthTrackImage.raycastTarget = false;
            SetPosition(healthTrack.GetComponent<RectTransform>(), new Vector2(100f, 13f), Vector2.zero, new Vector2(138f, 12f), Vector2.zero);

            GameObject healthFill = new GameObject("HealthFill", typeof(RectTransform), typeof(Image));
            healthFill.transform.SetParent(healthTrack.transform, false);
            selectedOperatorHealthFill = healthFill.GetComponent<Image>();
            selectedOperatorHealthFill.type = Image.Type.Filled;
            selectedOperatorHealthFill.fillMethod = Image.FillMethod.Horizontal;
            selectedOperatorHealthFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            selectedOperatorHealthFill.color = new Color(0.25f, 0.95f, 0.36f, 1f);
            selectedOperatorHealthFill.raycastTarget = false;
            RectTransform fillRect = healthFill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            retreatOperatorButton = CreateButton(root, "RetreatOperatorButton", "↶", new Vector2(44f, 44f));
            SetPosition(retreatOperatorButton.GetComponent<RectTransform>(), new Vector2(152f, 0f), new Vector2(0.5f, 0.5f));
            retreatOperatorButton.GetComponent<Image>().color = new Color(0.7f, 0.2f, 0.18f, 1f);
            SetPlacementButtonStyle(retreatOperatorButton, new Color(0.7f, 0.2f, 0.18f, 1f), 28);
            retreatOperatorButton.onClick.AddListener(RetreatSelectedOperator);

            selectedOperatorLabelRoot.SetActive(false);
            retreatOperatorButton.gameObject.SetActive(false);
        }

        private void CreatePlacementControls(Transform root)
        {
            placementControlsRoot = new GameObject("PlacementControls", typeof(RectTransform));
            placementControlsRoot.transform.SetParent(root, false);
            var panelRect = placementControlsRoot.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = Vector2.zero;

            CreateFacingButton("FaceUpButton", "↑", OperatorFacing.Up, new Vector2(0f, 58f));
            CreateFacingButton("FaceLeftButton", "←", OperatorFacing.Left, new Vector2(-58f, 0f));
            CreateFacingButton("FaceDownButton", "↓", OperatorFacing.Down, new Vector2(0f, -58f));
            CreateFacingButton("FaceRightButton", "→", OperatorFacing.Right, new Vector2(58f, 0f));

            var confirmButton = CreateButton(placementControlsRoot.transform, "ConfirmPlacementButton", "PLACE", new Vector2(84f, 34f));
            SetPlacementButtonPosition(confirmButton, new Vector2(150f, 19f));
            SetPlacementButtonStyle(confirmButton, new Color(0.12f, 0.58f, 0.28f, 1f), 15);
            confirmButton.onClick.AddListener(() => stageBootstrapper?.ConfirmOperatorPlacement());

            var cancelButton = CreateButton(placementControlsRoot.transform, "CancelPlacementButton", "CANCEL", new Vector2(84f, 34f));
            SetPlacementButtonPosition(cancelButton, new Vector2(150f, -21f));
            SetPlacementButtonStyle(cancelButton, new Color(0.65f, 0.16f, 0.18f, 1f), 14);
            cancelButton.onClick.AddListener(() => stageBootstrapper?.CancelOperatorPlacement());
            placementConfirmButton = confirmButton;
            placementControlsRoot.SetActive(false);
        }

        private void CreateFacingButton(string objectName, string label, OperatorFacing facing, Vector2 position)
        {
            var button = CreateButton(placementControlsRoot.transform, objectName, label, new Vector2(42f, 42f));
            SetPlacementButtonPosition(button, position);
            SetPlacementButtonStyle(button, new Color(0.08f, 0.1f, 0.13f, 0.96f), 22);
            button.onClick.AddListener(() => stageBootstrapper?.SetPlacementFacing(facing));
        }

        private static void SetPlacementButtonPosition(Button button, Vector2 position)
        {
            SetPosition(button.GetComponent<RectTransform>(), position, new Vector2(0.5f, 0.5f), null, new Vector2(0.5f, 0.5f));
        }

        private static void SetPlacementButtonStyle(Button button, Color backgroundColor, int fontSize)
        {
            button.GetComponent<Image>().color = backgroundColor;
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.fontSize = fontSize;
                label.raycastTarget = false;
            }
        }

        public void SetPlacementControls(bool active, bool canConfirm = true)
        {
            if (placementControlsRoot != null) placementControlsRoot.SetActive(active);
            if (placementConfirmButton != null) placementConfirmButton.interactable = canConfirm;
        }

        public void SetPlacementControlsPosition(Vector3 worldPosition)
        {
            if (placementControlsRoot == null || canvas == null) return;

            Camera gameCamera = Camera.main;
            if (gameCamera == null) return;

            Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(gameCamera, worldPosition);
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, null, out Vector2 localPosition))
            {
                placementControlsRoot.GetComponent<RectTransform>().anchoredPosition = localPosition;
            }
        }

        public void SetSelectedOperatorName(string operatorName, Vector3 worldPosition)
        {
            if (selectedOperatorLabelRoot == null || selectedOperatorNameText == null || canvas == null) return;
            if (string.IsNullOrEmpty(operatorName))
            {
                selectedOperatorLabelRoot.SetActive(false);
                retreatOperatorButton.gameObject.SetActive(false);
                return;
            }

            Camera gameCamera = Camera.main;
            if (gameCamera == null) return;

            selectedOperatorNameText.text = operatorName;
            Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(gameCamera, worldPosition);
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, null, out Vector2 localPosition))
            {
                selectedOperatorLabelRoot.GetComponent<RectTransform>().anchoredPosition = localPosition;
                selectedOperatorLabelRoot.SetActive(true);
                RectTransform retreatRect = retreatOperatorButton.GetComponent<RectTransform>();
                retreatRect.anchoredPosition = localPosition + new Vector2(152f, 0f);
                retreatOperatorButton.gameObject.SetActive(true);
            }
        }

        public void SetSelectedOperatorName(string operatorName, int currentHP, int maxHP, OperatorRarity rarity, Vector3 worldPosition)
        {
            SetSelectedOperatorName(operatorName, worldPosition);
            if (string.IsNullOrEmpty(operatorName)) return;

            float healthRatio = maxHP > 0 ? Mathf.Clamp01((float)currentHP / maxHP) : 0f;
            selectedOperatorHealthText.text = $"HP {currentHP} / {maxHP}";
            selectedOperatorRarityText.text = new string('★', Mathf.Clamp((int)rarity, 1, 5));
            selectedOperatorHealthFill.fillAmount = healthRatio;
            selectedOperatorHealthFill.color = healthRatio <= 0.3f
                ? new Color(1f, 0.2f, 0.18f, 1f)
                : healthRatio <= 0.6f
                    ? new Color(1f, 0.75f, 0.15f, 1f)
                    : new Color(0.25f, 0.95f, 0.36f, 1f);
        }

        private void RefreshSelectedOperatorInfo()
        {
            OperatorBase selectedOperator = operatorManager != null ? operatorManager.SelectedOperator : null;
            if (selectedOperator == null || !selectedOperator.IsDeployed || selectedOperator.DeployedCell == null || gridManager == null)
            {
                SetSelectedOperatorName(string.Empty, Vector3.zero);
                return;
            }

            Vector3 labelPosition = selectedOperator.DeployedCell.WorldPosition + Vector3.up * gridManager.CellSize * 0.7f;
            SetSelectedOperatorName(
                selectedOperator.Data.operatorName,
                selectedOperator.CurrentHP,
                selectedOperator.MaxHP,
                selectedOperator.CurrentRarity,
                labelPosition);
        }

        private void CreateTopBar(Transform root)
        {
            // Background strip
            stageInfoPanel = new GameObject("TopBar", typeof(RectTransform), typeof(Image));
            stageInfoPanel.transform.SetParent(root, false);
            var barRect = stageInfoPanel.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.sizeDelta = new Vector2(0f, 90f);
            barRect.anchoredPosition = Vector2.zero;
            stageInfoPanel.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.06f, 0.85f);

            // Pause button (top-left)
            var pauseButton = CreateButton(stageInfoPanel.transform, "PauseButton", "||", new Vector2(50f, 50f));
            SetPosition(pauseButton.GetComponent<RectTransform>(), new Vector2(20f, -45f), new Vector2(0f, 1f), new Vector2(50f, 50f), new Vector2(0f, 0.5f));
            pauseButton.onClick.AddListener(PauseGame);

            // Wave text - spaced cleanly to the right of pause button
            waveText = CreateText(stageInfoPanel.transform, "WaveText", "WAVE 1", 28, TextAnchor.MiddleLeft);
            SetPosition(waveText.GetComponent<RectTransform>(), new Vector2(95f, -28f), new Vector2(0f, 1f), new Vector2(220f, 32f), new Vector2(0f, 0.5f));

            // Phase text - directly under WaveText
            phaseText = CreateText(stageInfoPanel.transform, "PhaseText", "PREPARATION", 18, TextAnchor.MiddleLeft);
            phaseText.color = new Color(0.5f, 0.8f, 1f, 1f);
            SetPosition(phaseText.GetComponent<RectTransform>(), new Vector2(95f, -60f), new Vector2(0f, 1f), new Vector2(220f, 26f), new Vector2(0f, 0.5f));

            squadCountText = CreateText(stageInfoPanel.transform, "SquadCountText", "SQUAD 0/8", 18, TextAnchor.MiddleLeft);
            SetPosition(squadCountText.GetComponent<RectTransform>(), new Vector2(350f, -45f), new Vector2(0f, 1f), new Vector2(190f, 34f), new Vector2(0f, 0.5f));

            // Enemy count (center)
            enemyText = CreateText(stageInfoPanel.transform, "EnemyText", "0 ENEMIES", 24, TextAnchor.MiddleCenter);
            SetPosition(enemyText.GetComponent<RectTransform>(), new Vector2(0f, -45f), new Vector2(0.5f, 1f), new Vector2(220f, 40f), new Vector2(0.5f, 0.5f));

            // Lives Counter (top-right, 3 lives)
            lpText = CreateText(stageInfoPanel.transform, "LivesText", "♥ ♥ ♥  (3 LIVES)", 24, TextAnchor.MiddleRight);
            lpText.color = new Color(1f, 0.35f, 0.35f, 1f);
            SetPosition(lpText.GetComponent<RectTransform>(), new Vector2(-25f, -45f), new Vector2(1f, 1f), new Vector2(280f, 45f), new Vector2(1f, 0.5f));
        }

        private void CreateDeckBar(Transform root)
        {
            // Bottom bar background
            var bar = new GameObject("DeckBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(root, false);
            var barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.sizeDelta = new Vector2(0f, 170f);
            barRect.anchoredPosition = Vector2.zero;
            bar.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.06f, 0.85f);

            // Deck slots container
            var slotsContainer = new GameObject("DeckSlots", typeof(RectTransform));
            slotsContainer.transform.SetParent(bar.transform, false);
            var slotsRect = slotsContainer.GetComponent<RectTransform>();
            slotsRect.anchorMin = new Vector2(0f, 0f);
            slotsRect.anchorMax = new Vector2(1f, 1f);
            slotsRect.offsetMin = new Vector2(30f, 15f);
            slotsRect.offsetMax = new Vector2(-210f, -15f);

            var layout = slotsContainer.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            deckButtons = new Button[DeckSlotCount];
            deckButtonLabels = new Text[DeckSlotCount];
            deckButtonImages = new Image[DeckSlotCount];

            for (int i = 0; i < DeckSlotCount; i++)
            {
                int slotIndex = i;
                var slotBtn = CreateDeckSlot(slotsContainer.transform, $"DeckSlot_{i + 1}");
                deckButtons[i] = slotBtn;
                deckButtonLabels[i] = slotBtn.GetComponentInChildren<Text>();
                deckButtonImages[i] = slotBtn.GetComponent<Image>();
                slotBtn.onClick.AddListener(() => SelectDeckSlot(slotIndex));
                var dragHandler = slotBtn.gameObject.AddComponent<DeckSlotDragHandler>();
                dragHandler.Bind(
                    slotIndex,
                    () => gameManager != null && gameManager.CurrentPhase == StagePhase.Preparation && playerDeck != null &&
                        playerDeck.GetCard(slotIndex) != null && playerDeck.GetCard(slotIndex).cooldownRoundsRemaining <= 0,
                    HandleDeckCardDrag,
                    FinishDeckCardDrag);
                slotBtn.interactable = false;
            }

            // Start Wave button
            startWaveButton = CreateButton(bar.transform, "StartWaveButton", "START\nWAVE", new Vector2(160f, 100f));
            startWaveButtonText = startWaveButton.GetComponentInChildren<Text>();
            var swbRect = startWaveButton.GetComponent<RectTransform>();
            swbRect.anchorMin = new Vector2(1f, 0.5f);
            swbRect.anchorMax = new Vector2(1f, 0.5f);
            swbRect.anchoredPosition = new Vector2(-110f, 0f);
            startWaveButton.GetComponent<Image>().color = new Color(0.18f, 0.55f, 0.34f, 1f);
            startWaveButton.onClick.AddListener(StartWave);
        }

        private Button CreateDeckSlot(Transform parent, string objectName)
        {
            var button = CreateButton(parent, objectName, "", new Vector2(115f, 130f));
            var image = button.GetComponent<Image>();
            image.sprite = CreateTrapezoidSprite();
            image.color = new Color(0.06f, 0.07f, 0.10f, 1f);
            image.type = Image.Type.Simple;

            // Smaller label text for operator name
            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.fontSize = 14;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
            }

            return button;
        }

        private void CreatePausePanel(Transform root)
        {
            pausePanel = new GameObject("PausePanel", typeof(RectTransform), typeof(Image));
            pausePanel.transform.SetParent(root, false);
            var panelRect = pausePanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            pausePanel.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.06f, 0.94f);

            var title = CreateText(pausePanel.transform, "PauseTitle", "PAUSED", 54, TextAnchor.MiddleCenter);
            SetPosition(title.GetComponent<RectTransform>(), new Vector2(0f, -300f), new Vector2(0.5f, 1f), new Vector2(500f, 100f));

            var resumeButton = CreateButton(pausePanel.transform, "ResumeButton", "RESUME", new Vector2(260f, 70f));
            SetPosition(resumeButton.GetComponent<RectTransform>(), new Vector2(0f, -480f), new Vector2(0.5f, 1f));
            resumeButton.onClick.AddListener(ResumeGame);

            var exitButton = CreateButton(pausePanel.transform, "ExitButton", "EXIT TO MENU", new Vector2(260f, 70f));
            SetPosition(exitButton.GetComponent<RectTransform>(), new Vector2(0f, -580f), new Vector2(0.5f, 1f));
            exitButton.onClick.AddListener(ExitToMenu);

            pausePanel.SetActive(false);
        }

        // ============================
        // Phase Handling
        // ============================

        private void HandlePhaseChanged(StagePhase phase)
        {
            UpdatePhaseUI();
        }

        private void UpdatePhaseUI()
        {
            if (gameManager == null) return;

            StagePhase phase = gameManager.CurrentPhase;

            switch (phase)
            {
                case StagePhase.CardPick:
                    if (phaseText != null) phaseText.text = "CARD PICK";
                    startWaveButton.interactable = false;
                    if (startWaveButtonText != null) startWaveButtonText.text = "DRAFTING...";
                    break;

                case StagePhase.Preparation:
                    if (phaseText != null) phaseText.text = "PREPARATION";
                    startWaveButton.interactable = true;
                    if (startWaveButtonText != null) startWaveButtonText.text = "START\nWAVE";
                    startWaveButton.GetComponent<Image>().color = new Color(0.18f, 0.55f, 0.34f, 1f);
                    break;

                case StagePhase.WaveActive:
                    if (phaseText != null) phaseText.text = "WAVE IN PROGRESS";
                    startWaveButton.interactable = false;
                    if (startWaveButtonText != null) startWaveButtonText.text = "WAVE\nACTIVE";
                    startWaveButton.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.35f, 1f);
                    break;
            }
        }

        private void RetreatSelectedOperator()
        {
            OperatorBase selectedOperator = operatorManager != null ? operatorManager.SelectedOperator : null;
            if (selectedOperator != null)
            {
                operatorManager.RetreatOperator(selectedOperator);
            }
        }

        // ============================
        // Deck Slots
        // ============================

        private void HandleDeckChanged(IReadOnlyList<DraftCard> deck)
        {
            RefreshDeckSlots();
        }

        private void RefreshDeckSlots()
        {
            if (playerDeck == null || deckButtons == null) return;

            var deck = playerDeck.DeckSlots;

            for (int i = 0; i < DeckSlotCount; i++)
            {
                if (i < deck.Count && deck[i] != null && deck[i].operatorData != null)
                {
                    var card = deck[i];
                    string stars = new string('★', (int)card.rarity);
                    bool isReady = card.cooldownRoundsRemaining <= 0;
                    deckButtonLabels[i].text = isReady
                        ? $"{card.operatorData.operatorName}\n{stars}"
                        : $"{card.operatorData.operatorName}\nREADY IN {card.cooldownRoundsRemaining} ROUND(S)";
                    deckButtons[i].interactable = isReady;

                    // Tint based on class for visual differentiation
                    deckButtonImages[i].color = isReady
                        ? GetClassColor(card.operatorData.operatorClass)
                        : new Color(0.22f, 0.22f, 0.24f, 1f);
                    deckButtons[i].GetComponent<DeckSlotDragHandler>().SetCard(card, deckButtonImages[i].color);
                }
                else
                {
                    deckButtonLabels[i].text = "";
                    deckButtons[i].interactable = false;
                    deckButtonImages[i].color = new Color(0.06f, 0.07f, 0.10f, 1f);
                    deckButtons[i].GetComponent<DeckSlotDragHandler>().SetCard(null, deckButtonImages[i].color);
                }
            }
        }

        private void FinishDeckCardDrag(int slotIndex, Vector2 screenPosition)
        {
            if (gameManager == null || gameManager.CurrentPhase != StagePhase.Preparation || playerDeck == null) return;

            DraftCard card = playerDeck.GetCard(slotIndex);
            if (card == null) return;

            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            for (int targetIndex = 0; targetIndex < deckButtons.Length; targetIndex++)
            {
                RectTransform targetRect = deckButtons[targetIndex].GetComponent<RectTransform>();
                if (!RectTransformUtility.RectangleContainsScreenPoint(targetRect, screenPosition, uiCamera)) continue;

                stageBootstrapper?.CancelOperatorPlacementPreview(card);
                playerDeck.MoveCard(slotIndex, targetIndex);
                return;
            }

            if (gridManager == null || stageBootstrapper == null) return;
            Camera gameCamera = Camera.main;
            if (gameCamera == null) return;

            Vector3 worldPosition = gameCamera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -gameCamera.transform.position.z));
            Vector2Int gridPosition = gridManager.WorldToGridPosition(worldPosition);
            if (!gridManager.IsInBounds(gridPosition)) return;

            stageBootstrapper.BeginOperatorPlacement(card, gridPosition);
        }

        private void HandleDeckCardDrag(int slotIndex, Vector2 screenPosition)
        {
            if (playerDeck == null || gridManager == null || stageBootstrapper == null) return;

            DraftCard card = playerDeck.GetCard(slotIndex);
            if (card == null) return;

            Camera gameCamera = Camera.main;
            if (gameCamera == null) return;

            Vector3 worldPosition = gameCamera.ScreenToWorldPoint(new Vector3(
                screenPosition.x,
                screenPosition.y,
                -gameCamera.transform.position.z));
            Vector2Int gridPosition = gridManager.WorldToGridPosition(worldPosition);
            if (gridManager.IsInBounds(gridPosition))
            {
                stageBootstrapper.BeginOperatorPlacement(card, gridPosition);
            }
            else
            {
                stageBootstrapper.CancelOperatorPlacementPreview(card);
            }
        }

        private Color GetClassColor(OperatorClass opClass)
        {
            return opClass switch
            {
                OperatorClass.Guard => new Color(0.55f, 0.15f, 0.15f, 1f),     // Red-tinted
                OperatorClass.Defender => new Color(0.15f, 0.25f, 0.55f, 1f),   // Blue-tinted
                OperatorClass.Sniper => new Color(0.15f, 0.50f, 0.15f, 1f),     // Green-tinted
                OperatorClass.Caster => new Color(0.45f, 0.15f, 0.55f, 1f),     // Purple-tinted
                OperatorClass.Medic => new Color(0.45f, 0.45f, 0.15f, 1f),      // Yellow-tinted
                _ => new Color(0.06f, 0.07f, 0.10f, 1f)
            };
        }

        private void SelectDeckSlot(int index)
        {
            if (playerDeck == null) return;

            var card = playerDeck.GetCard(index);
            if (card == null || card.cooldownRoundsRemaining > 0) return;

            selectedDeckSlot = index;

            // Notify deck and draft system that this card was selected for deployment
            playerDeck.SelectCardForDeployment(index);
            if (draftSystem != null)
            {
                draftSystem.NotifyCardSelected(card);
            }

            Debug.Log($"[Deck] Selected slot {index}: {card.operatorData.operatorName} ({card.rarity}) - click or drag to a valid tile to deploy.");
        }

        // ============================
        // Actions
        // ============================

        private void StartWave()
        {
            if (gameManager == null || waveManager == null) return;
            if (gameManager.CurrentPhase != StagePhase.Preparation) return;

            // Enter wave phase
            gameManager.EnterWavePhase();

            // Set wave index and start
            waveManager.SetWaveIndex(gameManager.GetCurrentWaveIndex());
            waveManager.StartNextWave();
        }

        private void PauseGame()
        {
            if (gameManager == null) return;
            gameManager.PauseGame();
            pausePanel.SetActive(true);
        }

        private void ResumeGame()
        {
            if (gameManager == null) return;
            gameManager.ResumeGame();
            pausePanel.SetActive(false);
        }

        private void ExitToMenu()
        {
            Time.timeScale = 1f;
            MainMenuController.ReturnToStageSelectorOnLoad();
            SceneManager.LoadScene("MainMenu");
        }

        // ============================
        // Event Handlers
        // ============================

        private void HandleWaveStarted(int current, int total)
        {
            if (waveText != null) waveText.text = $"WAVE {current}/{total}";
        }

        private void HandleSingleWaveFinished()
        {
            // Wave done — GameManager.OnWaveFinished will transition back to CardPick
            if (gameManager != null)
            {
                gameManager.OnWaveFinished();
            }
        }

        private void HandleEnemyCountChanged(EnemyBase enemy)
        {
            RefreshCounters();
        }

        private void HandleDPChanged(int dp)
        {
            // DP system removed
        }

        private void HandleLPChanged(int current, int max)
        {
            UpdateLivesDisplay(current, max);
        }

        private void UpdateLivesDisplay(int current, int max)
        {
            if (lpText == null) return;
            int total = max > 0 ? max : 3;
            string hearts = "";
            for (int i = 0; i < current; i++) hearts += "♥ ";
            for (int i = current; i < total; i++) hearts += "♡ ";
            lpText.text = $"{hearts.Trim()}  ({current} {(current == 1 ? "LIFE" : "LIVES")})";
        }

        private void RefreshCounters()
        {
            if (enemyText != null && enemyManager != null)
            {
                int count = enemyManager.ActiveEnemyCount;
                enemyText.text = count == 1 ? "1 ENEMY" : $"{count} ENEMIES";
            }

            if (waveText != null && waveManager != null && waveManager.TotalWaves > 0)
            {
                waveText.text = $"WAVE {Mathf.Max(1, waveManager.CurrentWaveNumber)}/{waveManager.TotalWaves}";
            }

            if (lpText != null && gameManager != null)
            {
                UpdateLivesDisplay(gameManager.CurrentLifePoints, gameManager.MaxLifePoints);
            }

            if (squadCountText != null && operatorManager != null)
            {
                bool isFull = operatorManager.IsAtSquadLimit;
                squadCountText.text = isFull
                    ? $"SQUAD FULL {operatorManager.DeployedCount}/{operatorManager.SquadLimit}"
                    : $"SQUAD {operatorManager.DeployedCount}/{operatorManager.SquadLimit}";
                squadCountText.color = isFull ? new Color(1f, 0.28f, 0.24f) : Color.white;
            }
        }

        // ============================
        // UI Helpers
        // ============================

        private static Text CreateText(Transform parent, string objectName, string value, int fontSize, TextAnchor alignment)
        {
            var textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = Color.white;
            return text;
        }

        private static Button CreateButton(Transform parent, string objectName, string label, Vector2 size)
        {
            var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var button = buttonObject.GetComponent<Button>();
            buttonObject.GetComponent<RectTransform>().sizeDelta = size;
            buttonObject.GetComponent<Image>().color = Color.black;

            var text = CreateText(buttonObject.transform, "Label", label, 22, TextAnchor.MiddleCenter);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return button;
        }

        private static void SetPosition(RectTransform rect, Vector2 position, Vector2 anchor, Vector2? size = null, Vector2? pivot = null)
        {
            if (pivot.HasValue) rect.pivot = pivot.Value;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            if (size.HasValue) rect.sizeDelta = size.Value;
        }

        private static Sprite CreateTrapezoidSprite()
        {
            const int width = 128;
            const int height = 112;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            for (int y = 0; y < height; y++)
            {
                float inset = Mathf.Lerp(18f, 0f, y / (float)(height - 1));
                int left = Mathf.RoundToInt(inset);
                int right = width - left;
                for (int x = left; x < right; x++) pixels[y * width + x] = Color.white;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void EnsureEventSystem()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
            }
            else if (eventSystem.GetComponent<BaseInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }
    }
}
