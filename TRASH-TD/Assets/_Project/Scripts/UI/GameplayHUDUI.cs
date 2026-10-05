using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TrashTD.Audio;
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
    /// Visual polish: animated intro, phase banners, wave progress, life-loss flash,
    /// hover/press feedback (MenuButtonFeedback) and optional UI sounds.
    /// </summary>
    public class GameplayHUDUI : MonoBehaviour
    {
        private const int DeckSlotCount = 8;

        // ── Palette ──────────────────────────────────────────
        private static readonly Color AccentColor = new Color(0.18f, 0.82f, 0.45f, 1f);
        private static readonly Color DarkButton = new Color(0.10f, 0.12f, 0.16f, 1f);
        private static readonly Color BarColor = new Color(0.03f, 0.04f, 0.06f, 0.88f);
        private static readonly Color DraftColor = new Color(1f, 0.78f, 0.25f, 1f);
        private static readonly Color PrepColor = new Color(0.5f, 0.8f, 1f, 1f);
        private static readonly Color WaveColor = new Color(1f, 0.45f, 0.35f, 1f);
        private static readonly Color LifeColor = new Color(1f, 0.35f, 0.35f, 1f);
        private static readonly Color TextDim = new Color(0.50f, 0.58f, 0.68f, 1f);
        private static readonly Color WaitingColor = new Color(0.3f, 0.3f, 0.35f, 1f);
        private static readonly Color ReadyColor = new Color(0.18f, 0.55f, 0.34f, 1f);

        [Header("UI Sounds (optional)")]
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioClip clickClip;
        [SerializeField] private AudioClip waveStartClip;
        [SerializeField] private AudioClip lifeLostClip;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.7f;

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
        private Text selectedOperatorSkillDescriptionText;
        private Text selectedOperatorHealthText;
        private Text selectedOperatorRarityText;
        private Image selectedOperatorHealthFill;
        private RectTransform selectedOperatorHealthFillRect;
        private GameObject selectedOperatorUpgradeBadge;
        private UpgradeArrowGraphic selectedOperatorUpgradeBadgeGraphic;
        private GameObject placementControlsRoot;
        private Button retreatOperatorButton;
        private Button placementConfirmButton;
        private Button startWaveButton;
        private Text startWaveButtonText;
        private Button[] deckButtons;
        private Text[] deckButtonLabels;
        private Image[] deckButtonImages;
        private GameObject[] deckUpgradeBadges;
        private UpgradeArrowGraphic[] deckUpgradeBadgeLabels;
        private CardDraftSystem draftSystem;
        private WaveManager waveManager;
        private EnemyManager enemyManager;
        private GameManager gameManager;
        private PlayerDeck playerDeck;
        private GridManager gridManager;
        private OperatorManager operatorManager;
        private StageBootstrapper stageBootstrapper;

        private int selectedDeckSlot = -1;

        // Feedback components
        private MenuButtonFeedback[] deckFeedbacks;
        private MenuButtonFeedback startWaveFeedback;
        private MenuButtonFeedback retreatFeedback;
        private MenuButtonFeedback confirmFeedback;

        // Layout references used by animations
        private RectTransform topBarRect;
        private RectTransform deckBarRect;
        private RectTransform waveProgressFillRect;
        private Image waveProgressFill;
        private Image dangerFlashImage;
        private CanvasGroup phaseBannerGroup;
        private RectTransform phaseBannerContent;
        private Text phaseBannerTitle;
        private Text phaseBannerSubtitle;
        private Image[] phaseBannerLines;
        private CanvasGroup pauseGroup;
        private RectTransform pauseCard;

        // Animation state
        private AudioSource sfxSource;
        private Coroutine bannerRoutine;
        private Coroutine pauseRoutine;
        private Color phaseColorTarget = PrepColor;
        private StagePhase lastPhase;
        private bool hasLastPhase;
        private float waveProgressDisplayed;
        private int lastSquadCount = -1;
        private int lastLives = -1;
        private float dangerFlashTime = -1f;
        private float lifeFlashTime = -1f;
        private bool selectedLabelShown;
        private float labelPopTime = -1f;
        private bool snapHealthFill;
        private float selectedHealthTarget;
        private float selectedHealthDisplayed;
        private float placementPopTime = -1f;

        private sealed class PunchState
        {
            public Transform target;
            public float time;
        }

        private readonly List<PunchState> punches = new List<PunchState>();

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

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            if (hoverClip == null) hoverClip = AudioManager.Instance?.GetClip(SfxId.UiHover);
            if (clickClip == null) clickClip = AudioManager.Instance?.GetClip(SfxId.UiClick);
            if (waveStartClip == null) waveStartClip = AudioManager.Instance?.GetClip(SfxId.GameplayWaveStart);
            if (lifeLostClip == null) lifeLostClip = AudioManager.Instance?.GetClip(SfxId.GameplayLifeLost);

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
            UpdatePhaseUI(false);

            StartCoroutine(IntroRoutine());
        }

        private void Update()
        {
            RefreshCounters();
            RefreshSelectedOperatorInfo();
            UpdateAnimations(Time.unscaledDeltaTime, Time.unscaledTime);
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
            CreateDangerFlash(root);
            CreateTopBar(root);
            CreateDeckBar(root);
            CreatePhaseBanner(root);
            CreatePlacementControls(root);
            CreateSelectedOperatorLabel(root);
            CreatePausePanel(root); // last, so it renders above everything else
        }

        private void CreateDangerFlash(Transform root)
        {
            var flash = new GameObject("DangerFlash", typeof(RectTransform), typeof(Image));
            flash.transform.SetParent(root, false);
            Stretch(flash.GetComponent<RectTransform>());

            dangerFlashImage = flash.GetComponent<Image>();
            dangerFlashImage.sprite = CreateVignetteSprite();
            dangerFlashImage.color = new Color(1f, 0.1f, 0.1f, 0f);
            dangerFlashImage.raycastTarget = false;
        }

        private void CreateSelectedOperatorLabel(Transform root)
        {
            selectedOperatorLabelRoot = new GameObject("SelectedOperatorName", typeof(RectTransform), typeof(Image));
            selectedOperatorLabelRoot.transform.SetParent(root, false);
            RectTransform labelRect = selectedOperatorLabelRoot.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(250f, 84f);

            Image background = selectedOperatorLabelRoot.GetComponent<Image>();
            background.color = new Color(0.025f, 0.035f, 0.045f, 0.92f);
            background.raycastTarget = false;

            var labelBorder = selectedOperatorLabelRoot.AddComponent<Outline>();
            labelBorder.effectColor = new Color(1f, 1f, 1f, 0.10f);
            labelBorder.effectDistance = new Vector2(1.5f, -1.5f);

            selectedOperatorNameText = CreateText(selectedOperatorLabelRoot.transform, "Name", string.Empty, 16, TextAnchor.MiddleLeft);
            SetPosition(selectedOperatorNameText.GetComponent<RectTransform>(), new Vector2(10f, -5f), new Vector2(0f, 1f), new Vector2(190f, 22f), new Vector2(0f, 1f));
            selectedOperatorNameText.raycastTarget = false;

            selectedOperatorSkillDescriptionText = CreateText(
                selectedOperatorLabelRoot.transform,
                "SkillDescription",
                string.Empty,
                13,
                TextAnchor.UpperLeft);
            selectedOperatorSkillDescriptionText.fontStyle = FontStyle.Normal;
            selectedOperatorSkillDescriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            selectedOperatorSkillDescriptionText.verticalOverflow = VerticalWrapMode.Truncate;
            selectedOperatorSkillDescriptionText.color = new Color(0.68f, 0.88f, 1f, 1f);
            selectedOperatorSkillDescriptionText.raycastTarget = false;
            SetPosition(
                selectedOperatorSkillDescriptionText.GetComponent<RectTransform>(),
                new Vector2(10f, -48f),
                new Vector2(0f, 1f),
                new Vector2(280f, 58f),
                new Vector2(0f, 1f));
            selectedOperatorSkillDescriptionText.gameObject.SetActive(false);

            selectedOperatorRarityText = CreateText(selectedOperatorLabelRoot.transform, "Rarity", string.Empty, 17, TextAnchor.MiddleRight);
            selectedOperatorRarityText.color = new Color(1f, 0.78f, 0.2f, 1f);
            SetPosition(selectedOperatorRarityText.GetComponent<RectTransform>(), new Vector2(-10f, -5f), new Vector2(1f, 1f), new Vector2(82f, 22f), new Vector2(1f, 1f));
            selectedOperatorRarityText.raycastTarget = false;

            selectedOperatorUpgradeBadge = CreateUpgradeBadge(selectedOperatorLabelRoot.transform, "SelectedOperatorUpgradeBadge", new Vector2(38f, 18f), new Vector2(10f, -29f));
            selectedOperatorUpgradeBadgeGraphic = selectedOperatorUpgradeBadge.GetComponentInChildren<UpgradeArrowGraphic>();

            selectedOperatorHealthText = CreateText(selectedOperatorLabelRoot.transform, "Health", string.Empty, 14, TextAnchor.MiddleLeft);
            SetPosition(selectedOperatorHealthText.GetComponent<RectTransform>(), new Vector2(10f, 3f), Vector2.zero, new Vector2(84f, 20f), Vector2.zero);
            selectedOperatorHealthText.raycastTarget = false;

            GameObject healthTrack = new GameObject("HealthTrack", typeof(RectTransform), typeof(Image));
            healthTrack.transform.SetParent(selectedOperatorLabelRoot.transform, false);
            Image healthTrackImage = healthTrack.GetComponent<Image>();
            healthTrackImage.color = new Color(0.15f, 0.17f, 0.19f, 1f);
            healthTrackImage.raycastTarget = false;
            SetPosition(healthTrack.GetComponent<RectTransform>(), new Vector2(100f, 7f), Vector2.zero, new Vector2(138f, 12f), Vector2.zero);

            // The fill is width-driven (anchors) because an Image set to "Filled" with no sprite
            // ignores fillAmount and always draws full.
            GameObject healthFill = new GameObject("HealthFill", typeof(RectTransform), typeof(Image));
            healthFill.transform.SetParent(healthTrack.transform, false);
            selectedOperatorHealthFill = healthFill.GetComponent<Image>();
            selectedOperatorHealthFill.color = new Color(0.25f, 0.95f, 0.36f, 1f);
            selectedOperatorHealthFill.raycastTarget = false;
            selectedOperatorHealthFillRect = healthFill.GetComponent<RectTransform>();
            selectedOperatorHealthFillRect.anchorMin = Vector2.zero;
            selectedOperatorHealthFillRect.anchorMax = Vector2.one;
            selectedOperatorHealthFillRect.offsetMin = Vector2.zero;
            selectedOperatorHealthFillRect.offsetMax = Vector2.zero;

            retreatOperatorButton = CreateButton(root, "RetreatOperatorButton", "↶", new Vector2(44f, 44f));
            SetPosition(retreatOperatorButton.GetComponent<RectTransform>(), new Vector2(185f, 0f), new Vector2(0.5f, 0.5f));
            retreatOperatorButton.GetComponent<Image>().color = new Color(0.7f, 0.2f, 0.18f, 1f);
            SetPlacementButtonStyle(retreatOperatorButton, new Color(0.7f, 0.2f, 0.18f, 1f), 28);
            retreatOperatorButton.onClick.AddListener(RetreatSelectedOperator);
            retreatFeedback = AttachFeedback(retreatOperatorButton, new Color(1f, 0.45f, 0.4f, 1f), 1.1f);

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
            confirmFeedback = AttachFeedback(confirmButton, AccentColor, 1.08f);

            var cancelButton = CreateButton(placementControlsRoot.transform, "CancelPlacementButton", "CANCEL", new Vector2(84f, 34f));
            SetPlacementButtonPosition(cancelButton, new Vector2(150f, -21f));
            SetPlacementButtonStyle(cancelButton, new Color(0.65f, 0.16f, 0.18f, 1f), 14);
            cancelButton.onClick.AddListener(() => stageBootstrapper?.CancelOperatorPlacement());
            AttachFeedback(cancelButton, new Color(1f, 0.4f, 0.4f, 1f), 1.08f);

            placementConfirmButton = confirmButton;
            placementControlsRoot.SetActive(false);
        }

        private void CreateFacingButton(string objectName, string label, OperatorFacing facing, Vector2 position)
        {
            var button = CreateButton(placementControlsRoot.transform, objectName, label, new Vector2(42f, 42f));
            SetPlacementButtonPosition(button, position);
            SetPlacementButtonStyle(button, new Color(0.08f, 0.1f, 0.13f, 0.96f), 22);
            button.onClick.AddListener(() => stageBootstrapper?.SetPlacementFacing(facing));
            AttachFeedback(button, PrepColor, 1.12f);
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
            if (placementControlsRoot != null)
            {
                bool wasActive = placementControlsRoot.activeSelf;
                placementControlsRoot.SetActive(active);

                if (active && !wasActive)
                {
                    placementPopTime = 0f;
                    placementControlsRoot.transform.localScale = Vector3.one * 0.7f;
                }
            }

            if (placementConfirmButton != null)
            {
                placementConfirmButton.interactable = canConfirm;

                // Transition is disabled on HUD buttons, so show the disabled state ourselves.
                if (confirmFeedback != null)
                {
                    confirmFeedback.SetBaseColor(canConfirm ? new Color(0.12f, 0.58f, 0.28f, 1f) : new Color(0.2f, 0.22f, 0.25f, 0.8f));
                }
            }
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
                selectedLabelShown = false;
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
                retreatRect.anchoredPosition = localPosition + new Vector2(185f, 0f);
                retreatOperatorButton.gameObject.SetActive(true);

                if (!selectedLabelShown)
                {
                    selectedLabelShown = true;
                    labelPopTime = 0f;
                    snapHealthFill = true;
                    ApplyLabelPop(0f);
                }
            }
        }

        public void SetSelectedOperatorName(
            string operatorName,
            int currentHP,
            int maxHP,
            OperatorRarity rarity,
            OperatorRarity baseRarity,
            string skillDescription,
            Vector3 worldPosition)
        {
            SetSelectedOperatorName(operatorName, worldPosition);
            if (string.IsNullOrEmpty(operatorName)) return;

            bool hasSkillDescription = !string.IsNullOrWhiteSpace(skillDescription);
            selectedOperatorLabelRoot.GetComponent<RectTransform>().sizeDelta = hasSkillDescription
                ? new Vector2(300f, 156f)
                : new Vector2(250f, 84f);
            selectedOperatorSkillDescriptionText.text = skillDescription;
            selectedOperatorSkillDescriptionText.gameObject.SetActive(hasSkillDescription);

            int upgradeLevels = Mathf.Clamp((int)rarity - (int)baseRarity, 0, 2);
            selectedOperatorUpgradeBadgeGraphic.SetArrowCount(upgradeLevels);
            selectedOperatorUpgradeBadge.SetActive(upgradeLevels > 0);
            float healthRatio = maxHP > 0 ? Mathf.Clamp01((float)currentHP / maxHP) : 0f;
            selectedOperatorHealthText.text = $"HP {currentHP} / {maxHP}";
            selectedOperatorRarityText.text = new string('★', Mathf.Clamp((int)rarity, 1, 5));

            selectedHealthTarget = healthRatio;
            if (snapHealthFill)
            {
                selectedHealthDisplayed = healthRatio;
                snapHealthFill = false;
                ApplyHealthFill();
            }

            selectedOperatorHealthFill.color = healthRatio <= 0.3f
                ? new Color(1f, 0.2f, 0.18f, 1f)
                : healthRatio <= 0.6f
                    ? new Color(1f, 0.75f, 0.15f, 1f)
                    : new Color(0.25f, 0.95f, 0.36f, 1f);
        }

        private void ApplyHealthFill()
        {
            if (selectedOperatorHealthFillRect == null) return;
            selectedOperatorHealthFillRect.anchorMax = new Vector2(Mathf.Clamp01(selectedHealthDisplayed), 1f);
        }

        private void ApplyLabelPop(float k)
        {
            float s = Mathf.LerpUnclamped(0.8f, 1f, EaseOutBack(k));
            if (selectedOperatorLabelRoot != null) selectedOperatorLabelRoot.transform.localScale = new Vector3(s, s, 1f);
            if (retreatFeedback != null) retreatFeedback.introScale = s;
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
                selectedOperator.Data.baseRarity,
                selectedOperator.Data.skillDescription,
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
            stageInfoPanel.GetComponent<Image>().color = BarColor;
            topBarRect = barRect;

            // Wave progress strip along the bottom edge of the bar (color follows the phase)
            var track = new GameObject("WaveProgressTrack", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(stageInfoPanel.transform, false);
            var trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(0f, 0f);
            trackRect.anchorMax = new Vector2(1f, 0f);
            trackRect.pivot = new Vector2(0.5f, 0f);
            trackRect.sizeDelta = new Vector2(0f, 4f);
            trackRect.anchoredPosition = Vector2.zero;
            var trackImage = track.GetComponent<Image>();
            trackImage.color = new Color(1f, 1f, 1f, 0.07f);
            trackImage.raycastTarget = false;

            var fill = new GameObject("WaveProgressFill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            waveProgressFillRect = fill.GetComponent<RectTransform>();
            waveProgressFillRect.anchorMin = Vector2.zero;
            waveProgressFillRect.anchorMax = new Vector2(0f, 1f);
            waveProgressFillRect.offsetMin = Vector2.zero;
            waveProgressFillRect.offsetMax = Vector2.zero;
            waveProgressFill = fill.GetComponent<Image>();
            waveProgressFill.color = PrepColor;
            waveProgressFill.raycastTarget = false;

            // Pause button (top-left)
            var pauseButton = CreateButton(stageInfoPanel.transform, "PauseButton", "||", new Vector2(50f, 50f));
            SetPosition(pauseButton.GetComponent<RectTransform>(), new Vector2(20f, -45f), new Vector2(0f, 1f), new Vector2(50f, 50f), new Vector2(0f, 0.5f));
            pauseButton.onClick.AddListener(PauseGame);
            StyleButton(pauseButton, DarkButton, Color.white, 1.08f);

            // Wave text - spaced cleanly to the right of pause button
            waveText = CreateText(stageInfoPanel.transform, "WaveText", "WAVE 1", 28, TextAnchor.MiddleLeft);
            SetPosition(waveText.GetComponent<RectTransform>(), new Vector2(95f, -28f), new Vector2(0f, 1f), new Vector2(220f, 32f), new Vector2(0f, 0.5f));

            // Phase text - directly under WaveText
            phaseText = CreateText(stageInfoPanel.transform, "PhaseText", "PREPARATION", 18, TextAnchor.MiddleLeft);
            phaseText.color = PrepColor;
            SetPosition(phaseText.GetComponent<RectTransform>(), new Vector2(95f, -60f), new Vector2(0f, 1f), new Vector2(220f, 26f), new Vector2(0f, 0.5f));

            squadCountText = CreateText(stageInfoPanel.transform, "SquadCountText", "SQUAD 0/8", 18, TextAnchor.MiddleLeft);
            SetPosition(squadCountText.GetComponent<RectTransform>(), new Vector2(350f, -45f), new Vector2(0f, 1f), new Vector2(190f, 34f), new Vector2(0f, 0.5f));

            // Enemy count (center)
            enemyText = CreateText(stageInfoPanel.transform, "EnemyText", "0 ENEMIES", 24, TextAnchor.MiddleCenter);
            SetPosition(enemyText.GetComponent<RectTransform>(), new Vector2(0f, -45f), new Vector2(0.5f, 1f), new Vector2(220f, 40f), new Vector2(0.5f, 0.5f));

            // Lives Counter (top-right, 3 lives)
            lpText = CreateText(stageInfoPanel.transform, "LivesText", "♥ ♥ ♥  (3 LIVES)", 24, TextAnchor.MiddleRight);
            lpText.color = LifeColor;
            SetPosition(lpText.GetComponent<RectTransform>(), new Vector2(-25f, -45f), new Vector2(1f, 1f), new Vector2(280f, 45f), new Vector2(1f, 0.5f));

            // Texts and progress strip should never eat clicks
            waveText.raycastTarget = false;
            phaseText.raycastTarget = false;
            squadCountText.raycastTarget = false;
            enemyText.raycastTarget = false;
            lpText.raycastTarget = false;
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
            bar.GetComponent<Image>().color = BarColor;
            deckBarRect = barRect;

            // Thin accent line along the top edge
            var edge = new GameObject("DeckBarEdge", typeof(RectTransform), typeof(Image));
            edge.transform.SetParent(bar.transform, false);
            var edgeRect = edge.GetComponent<RectTransform>();
            edgeRect.anchorMin = new Vector2(0f, 1f);
            edgeRect.anchorMax = new Vector2(1f, 1f);
            edgeRect.pivot = new Vector2(0.5f, 1f);
            edgeRect.sizeDelta = new Vector2(0f, 2f);
            edgeRect.anchoredPosition = Vector2.zero;
            var edgeImage = edge.GetComponent<Image>();
            edgeImage.color = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.45f);
            edgeImage.raycastTarget = false;

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
            deckUpgradeBadges = new GameObject[DeckSlotCount];
            deckUpgradeBadgeLabels = new UpgradeArrowGraphic[DeckSlotCount];
            deckFeedbacks = new MenuButtonFeedback[DeckSlotCount];

            for (int i = 0; i < DeckSlotCount; i++)
            {
                int slotIndex = i;
                var slotBtn = CreateDeckSlot(slotsContainer.transform, $"DeckSlot_{i + 1}");
                deckButtons[i] = slotBtn;
                deckButtonLabels[i] = slotBtn.GetComponentInChildren<Text>();
                deckButtonImages[i] = slotBtn.GetComponent<Image>();
                deckUpgradeBadges[i] = CreateUpgradeBadge(slotBtn.transform, $"DeckUpgradeBadge_{i + 1}", new Vector2(34f, 18f), new Vector2(-5f, -5f));
                deckUpgradeBadgeLabels[i] = deckUpgradeBadges[i].GetComponentInChildren<UpgradeArrowGraphic>();
                slotBtn.onClick.AddListener(() => SelectDeckSlot(slotIndex));
                var dragHandler = slotBtn.gameObject.AddComponent<DeckSlotDragHandler>();
                dragHandler.Bind(
                    slotIndex,
                    () => gameManager != null && gameManager.CurrentPhase == StagePhase.Preparation && playerDeck != null &&
                        playerDeck.GetCard(slotIndex) != null && playerDeck.GetCard(slotIndex).cooldownRoundsRemaining <= 0,
                    HandleDeckCardDrag,
                    FinishDeckCardDrag);

                // No press-shrink on deck slots so it doesn't fight with dragging.
                deckFeedbacks[i] = AttachFeedback(slotBtn, Color.white, 1.04f);
                deckFeedbacks[i].pressScale = 1f;

                slotBtn.interactable = false;
            }

            // Start Wave button
            startWaveButton = CreateButton(bar.transform, "StartWaveButton", "START\nWAVE", new Vector2(160f, 100f));
            startWaveButtonText = startWaveButton.GetComponentInChildren<Text>();
            var swbRect = startWaveButton.GetComponent<RectTransform>();
            swbRect.anchorMin = new Vector2(1f, 0.5f);
            swbRect.anchorMax = new Vector2(1f, 0.5f);
            swbRect.anchoredPosition = new Vector2(-110f, 0f);
            startWaveButton.GetComponent<Image>().color = ReadyColor;
            startWaveButton.onClick.AddListener(StartWave);
            startWaveFeedback = AttachFeedback(startWaveButton, AccentColor, 1.06f, () => PlaySfx(clickClip, 1.1f));
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

        private GameObject CreateUpgradeBadge(Transform parent, string objectName, Vector2 size, Vector2 position)
        {
            GameObject badge = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(parent, false);
            RectTransform badgeRect = badge.GetComponent<RectTransform>();
            badgeRect.anchorMin = Vector2.one;
            badgeRect.anchorMax = Vector2.one;
            badgeRect.pivot = Vector2.one;
            badgeRect.sizeDelta = size;
            badgeRect.anchoredPosition = position;

            Image background = badge.GetComponent<Image>();
            background.color = new Color(1f, 0.78f, 0.2f, 1f);
            background.raycastTarget = false;

            GameObject arrows = new GameObject("Arrows", typeof(RectTransform), typeof(CanvasRenderer));
            arrows.transform.SetParent(badge.transform, false);
            RectTransform arrowsRect = arrows.GetComponent<RectTransform>();
            arrowsRect.anchorMin = Vector2.zero;
            arrowsRect.anchorMax = Vector2.one;
            arrowsRect.offsetMin = new Vector2(2f, 1f);
            arrowsRect.offsetMax = new Vector2(-2f, -1f);
            arrows.AddComponent<UpgradeArrowGraphic>().color = new Color(0.12f, 0.1f, 0.04f, 1f);

            badge.SetActive(false);
            return badge;
        }

        private void CreatePhaseBanner(Transform root)
        {
            var bannerRoot = new GameObject("PhaseBanner", typeof(RectTransform), typeof(CanvasGroup));
            bannerRoot.transform.SetParent(root, false);
            var bannerRect = bannerRoot.GetComponent<RectTransform>();
            bannerRect.anchorMin = new Vector2(0f, 0.62f);
            bannerRect.anchorMax = new Vector2(1f, 0.62f);
            bannerRect.pivot = new Vector2(0.5f, 0.5f);
            bannerRect.sizeDelta = new Vector2(0f, 130f);
            bannerRect.anchoredPosition = Vector2.zero;

            phaseBannerGroup = bannerRoot.GetComponent<CanvasGroup>();
            phaseBannerGroup.alpha = 0f;
            phaseBannerGroup.interactable = false;
            phaseBannerGroup.blocksRaycasts = false;

            var band = new GameObject("Band", typeof(RectTransform), typeof(Image));
            band.transform.SetParent(bannerRoot.transform, false);
            Stretch(band.GetComponent<RectTransform>());
            var bandImage = band.GetComponent<Image>();
            bandImage.color = new Color(0.02f, 0.03f, 0.05f, 0.78f);
            bandImage.raycastTarget = false;

            phaseBannerLines = new Image[2];
            for (int i = 0; i < 2; i++)
            {
                var line = new GameObject(i == 0 ? "TopLine" : "BottomLine", typeof(RectTransform), typeof(Image));
                line.transform.SetParent(bannerRoot.transform, false);
                var lineRect = line.GetComponent<RectTransform>();
                lineRect.anchorMin = new Vector2(0f, i == 0 ? 1f : 0f);
                lineRect.anchorMax = new Vector2(1f, i == 0 ? 1f : 0f);
                lineRect.pivot = new Vector2(0.5f, i == 0 ? 1f : 0f);
                lineRect.sizeDelta = new Vector2(0f, 3f);
                lineRect.anchoredPosition = Vector2.zero;
                phaseBannerLines[i] = line.GetComponent<Image>();
                phaseBannerLines[i].raycastTarget = false;
            }

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(bannerRoot.transform, false);
            phaseBannerContent = content.GetComponent<RectTransform>();
            Stretch(phaseBannerContent);

            phaseBannerTitle = CreateText(content.transform, "BannerTitle", "", 56, TextAnchor.MiddleCenter);
            SetPosition(phaseBannerTitle.GetComponent<RectTransform>(), new Vector2(0f, 16f), new Vector2(0.5f, 0.5f), new Vector2(1200f, 70f), new Vector2(0.5f, 0.5f));
            phaseBannerTitle.raycastTarget = false;

            phaseBannerSubtitle = CreateText(content.transform, "BannerSubtitle", "", 20, TextAnchor.MiddleCenter);
            phaseBannerSubtitle.color = TextDim;
            SetPosition(phaseBannerSubtitle.GetComponent<RectTransform>(), new Vector2(0f, -36f), new Vector2(0.5f, 0.5f), new Vector2(1200f, 30f), new Vector2(0.5f, 0.5f));
            phaseBannerSubtitle.raycastTarget = false;
        }

        private void CreatePausePanel(Transform root)
        {
            pausePanel = new GameObject("PausePanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            pausePanel.transform.SetParent(root, false);
            Stretch(pausePanel.GetComponent<RectTransform>());
            pausePanel.GetComponent<Image>().color = new Color(0.01f, 0.012f, 0.02f, 0.86f);

            pauseGroup = pausePanel.GetComponent<CanvasGroup>();
            pauseGroup.alpha = 0f;

            // Centered card
            var card = new GameObject("PauseCard", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(pausePanel.transform, false);
            pauseCard = card.GetComponent<RectTransform>();
            SetPosition(pauseCard, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(520f, 430f), new Vector2(0.5f, 0.5f));
            card.GetComponent<Image>().color = new Color(0.045f, 0.055f, 0.075f, 0.98f);

            var border = card.AddComponent<Outline>();
            border.effectColor = new Color(1f, 1f, 1f, 0.06f);
            border.effectDistance = new Vector2(2f, -2f);

            var accent = new GameObject("PauseAccent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(card.transform, false);
            var accentRect = accent.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.sizeDelta = new Vector2(0f, 4f);
            var accentImage = accent.GetComponent<Image>();
            accentImage.color = AccentColor;
            accentImage.raycastTarget = false;

            var title = CreateText(card.transform, "PauseTitle", "PAUSED", 54, TextAnchor.MiddleCenter);
            SetPosition(title.GetComponent<RectTransform>(), new Vector2(0f, -90f), new Vector2(0.5f, 1f), new Vector2(460f, 80f), new Vector2(0.5f, 0.5f));
            title.color = AccentColor;
            title.raycastTarget = false;

            var subtitle = CreateText(card.transform, "PauseSubtitle", "THE OPERATION WAITS", 14, TextAnchor.MiddleCenter);
            SetPosition(subtitle.GetComponent<RectTransform>(), new Vector2(0f, -148f), new Vector2(0.5f, 1f), new Vector2(460f, 26f), new Vector2(0.5f, 0.5f));
            subtitle.color = TextDim;
            subtitle.raycastTarget = false;

            var resumeButton = CreateButton(card.transform, "ResumeButton", "RESUME", new Vector2(340f, 70f));
            SetPosition(resumeButton.GetComponent<RectTransform>(), new Vector2(0f, -250f), new Vector2(0.5f, 1f), null, new Vector2(0.5f, 0.5f));
            resumeButton.onClick.AddListener(ResumeGame);
            StyleButton(resumeButton, AccentColor, new Color(0.4f, 1f, 0.65f, 1f), 1.05f);

            var exitButton = CreateButton(card.transform, "ExitButton", "EXIT TO MENU", new Vector2(340f, 60f));
            SetPosition(exitButton.GetComponent<RectTransform>(), new Vector2(0f, -336f), new Vector2(0.5f, 1f), null, new Vector2(0.5f, 0.5f));
            exitButton.onClick.AddListener(ExitToMenu);
            StyleButton(exitButton, new Color(0.45f, 0.14f, 0.16f, 1f), new Color(1f, 0.4f, 0.4f, 1f), 1.05f);

            pausePanel.SetActive(false);
        }

        // ============================
        // Phase Handling
        // ============================

        private void HandlePhaseChanged(StagePhase phase)
        {
            UpdatePhaseUI(true);
        }

        private void UpdatePhaseUI(bool announce)
        {
            if (gameManager == null) return;

            StagePhase phase = gameManager.CurrentPhase;

            switch (phase)
            {
                case StagePhase.CardPick:
                    if (phaseText != null) phaseText.text = "CARD PICK";
                    phaseColorTarget = DraftColor;
                    startWaveButton.interactable = false;
                    if (startWaveButtonText != null) startWaveButtonText.text = "DRAFTING...";
                    startWaveFeedback.SetBaseColor(WaitingColor);
                    startWaveFeedback.glowAlways = false;
                    break;

                case StagePhase.Preparation:
                    if (phaseText != null) phaseText.text = "PREPARATION";
                    phaseColorTarget = PrepColor;
                    startWaveButton.interactable = true;
                    if (startWaveButtonText != null) startWaveButtonText.text = "START\nWAVE";
                    startWaveFeedback.SetBaseColor(ReadyColor);
                    startWaveFeedback.glowAlways = true;
                    break;

                case StagePhase.WaveActive:
                    if (phaseText != null) phaseText.text = "WAVE IN PROGRESS";
                    phaseColorTarget = WaveColor;
                    startWaveButton.interactable = false;
                    if (startWaveButtonText != null) startWaveButtonText.text = "WAVE\nACTIVE";
                    startWaveFeedback.SetBaseColor(WaitingColor);
                    startWaveFeedback.glowAlways = false;
                    break;
            }

            bool phaseChanged = !hasLastPhase || phase != lastPhase;
            if (phaseChanged)
            {
                // A new phase clears any leftover deck selection highlight.
                selectedDeckSlot = -1;
                UpdateDeckSelectionVisuals();

                // The draft screen already fills the view, so only announce the other phases.
                if (announce)
                {
                    if (phase == StagePhase.Preparation)
                    {
                        ShowPhaseBanner("PREPARATION", "Deploy your squad", PrepColor);
                    }
                    else if (phase == StagePhase.WaveActive)
                    {
                        ShowPhaseBanner("WAVE IN PROGRESS", "Hold the line", WaveColor);
                        PlaySfx(waveStartClip, 1f);
                    }
                }
            }

            lastPhase = phase;
            hasLastPhase = true;
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

                    // Tint based on class for visual differentiation (dimmed while on cooldown).
                    Color slotColor = isReady
                        ? GetClassColor(card.operatorData.operatorClass)
                        : new Color(0.22f, 0.22f, 0.24f, 0.7f);
                    deckFeedbacks[i].SetBaseColor(slotColor);
                    Color glow = Color.Lerp(slotColor, Color.white, 0.55f);
                    deckFeedbacks[i].glowColor = new Color(glow.r, glow.g, glow.b, 0.9f);

                    int upgradeLevels = Mathf.Clamp((int)card.rarity - (int)card.operatorData.baseRarity, 0, 2);
                    deckUpgradeBadgeLabels[i].SetArrowCount(upgradeLevels);
                    deckUpgradeBadges[i].SetActive(upgradeLevels > 0);
                    deckButtons[i].GetComponent<DeckSlotDragHandler>().SetCard(card, slotColor);
                }
                else
                {
                    Color emptyColor = new Color(0.06f, 0.07f, 0.10f, 0.5f);
                    deckButtonLabels[i].text = "";
                    deckButtons[i].interactable = false;
                    deckFeedbacks[i].SetBaseColor(emptyColor);
                    deckUpgradeBadges[i].SetActive(false);
                    deckButtons[i].GetComponent<DeckSlotDragHandler>().SetCard(null, emptyColor);
                }
            }

            UpdateDeckSelectionVisuals();
        }

        private void UpdateDeckSelectionVisuals()
        {
            if (deckFeedbacks == null || playerDeck == null) return;

            var deck = playerDeck.DeckSlots;
            for (int i = 0; i < DeckSlotCount; i++)
            {
                if (deckFeedbacks[i] == null) continue;

                DraftCard card = i < deck.Count ? deck[i] : null;
                bool selected = i == selectedDeckSlot && card != null && card.cooldownRoundsRemaining <= 0;
                deckFeedbacks[i].glowAlways = selected;
                deckFeedbacks[i].restScale = selected ? 1.06f : 1f;
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

            // Notify deck and draft system that this card was selected for deployment
            playerDeck.SelectCardForDeployment(index);
            if (draftSystem != null)
            {
                draftSystem.NotifyCardSelected(card);
            }

            selectedDeckSlot = index;
            UpdateDeckSelectionVisuals();

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
            AudioManager.Instance?.PlaySfx(SfxId.GameplayPause);
            SetPauseVisible(true);
        }

        private void ResumeGame()
        {
            if (gameManager == null) return;
            gameManager.ResumeGame();
            AudioManager.Instance?.PlaySfx(SfxId.GameplayResume);
            SetPauseVisible(false);
        }

        private void ExitToMenu()
        {
            Time.timeScale = 1f;
            MainMenuController.ReturnToStageSelectorOnLoad();
            SceneManager.LoadScene("MainMenu");
        }

        private void SetPauseVisible(bool show)
        {
            if (pausePanel == null) return;

            if (pauseRoutine != null) StopCoroutine(pauseRoutine);
            pauseRoutine = StartCoroutine(PauseFadeRoutine(show));
        }

        // Uses unscaled time so it still animates while the game is paused (timeScale 0).
        private IEnumerator PauseFadeRoutine(bool show)
        {
            float duration = show ? 0.22f : 0.14f;
            float from = pauseGroup.alpha;
            float to = show ? 1f : 0f;

            if (show) pausePanel.SetActive(true);
            pauseGroup.interactable = show;
            pauseGroup.blocksRaycasts = show;

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                pauseGroup.alpha = Mathf.Lerp(from, to, EaseOutCubic(k));

                float s = show ? Mathf.LerpUnclamped(0.92f, 1f, EaseOutBack(k)) : Mathf.Lerp(1f, 0.96f, k);
                pauseCard.localScale = new Vector3(s, s, 1f);
                yield return null;
            }

            pauseGroup.alpha = to;
            pauseCard.localScale = Vector3.one;
            if (!show) pausePanel.SetActive(false);
        }

        // ============================
        // Event Handlers
        // ============================

        private void HandleWaveStarted(int current, int total)
        {
            if (waveText != null)
            {
                waveText.text = $"WAVE {current}/{total}";
                Punch(waveText);
            }
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

            if (lastLives >= 0 && current < lastLives) OnLifeLost();
            lastLives = current;

            int total = max > 0 ? max : 3;
            string hearts = "";
            for (int i = 0; i < current; i++) hearts += "♥ ";
            for (int i = current; i < total; i++) hearts += "♡ ";
            lpText.text = $"{hearts.Trim()}  ({current} {(current == 1 ? "LIFE" : "LIVES")})";
        }

        private void OnLifeLost()
        {
            dangerFlashTime = 0f;
            lifeFlashTime = 0f;
            Punch(lpText);
            PlaySfx(lifeLostClip, 1f);
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

                int deployed = operatorManager.DeployedCount;
                if (deployed != lastSquadCount)
                {
                    if (lastSquadCount >= 0) Punch(squadCountText);
                    lastSquadCount = deployed;
                }
            }
        }

        // ============================
        // Animation & Feedback
        // ============================

        private void UpdateAnimations(float dt, float time)
        {
            float k = 1f - Mathf.Exp(-8f * dt);

            // Phase color follows the current phase (text + wave progress strip)
            if (phaseText != null) phaseText.color = Color.Lerp(phaseText.color, phaseColorTarget, k);
            if (waveProgressFill != null) waveProgressFill.color = Color.Lerp(waveProgressFill.color, phaseColorTarget, k);

            // Wave progress
            if (waveProgressFillRect != null)
            {
                float target = 0f;
                if (waveManager != null && waveManager.TotalWaves > 0)
                {
                    target = Mathf.Clamp01((float)waveManager.CurrentWaveNumber / waveManager.TotalWaves);
                }

                waveProgressDisplayed = Mathf.Lerp(waveProgressDisplayed, target, 1f - Mathf.Exp(-6f * dt));
                waveProgressFillRect.anchorMax = new Vector2(Mathf.Clamp01(waveProgressDisplayed), 1f);
            }

            // Start-wave button breathes while a wave can be started
            if (startWaveFeedback != null && startWaveButton != null)
            {
                startWaveFeedback.restScale = startWaveButton.interactable
                    ? 1f + 0.025f * (0.5f + 0.5f * Mathf.Sin(time * 3f))
                    : 1f;
            }

            // Text punches
            for (int i = punches.Count - 1; i >= 0; i--)
            {
                PunchState p = punches[i];
                if (p.target == null)
                {
                    punches.RemoveAt(i);
                    continue;
                }

                p.time += dt;
                if (p.time >= 0.4f)
                {
                    p.target.localScale = Vector3.one;
                    punches.RemoveAt(i);
                    continue;
                }

                float s = 1f + Mathf.Sin(p.time * 30f) * Mathf.Exp(-p.time * 9f) * 0.18f;
                p.target.localScale = new Vector3(s, s, 1f);
            }

            // Lives text flashes white then settles back to red
            if (lifeFlashTime >= 0f && lpText != null)
            {
                lifeFlashTime += dt;
                float lk = Mathf.Clamp01(lifeFlashTime / 0.6f);
                lpText.color = Color.Lerp(Color.white, LifeColor, lk);
                if (lk >= 1f) lifeFlashTime = -1f;
            }

            // Red vignette flash when a life is lost
            if (dangerFlashTime >= 0f && dangerFlashImage != null)
            {
                dangerFlashTime += dt;
                float dk = Mathf.Clamp01(dangerFlashTime / 0.7f);
                Color c = dangerFlashImage.color;
                c.a = (1f - dk) * (1f - dk) * 0.6f;
                dangerFlashImage.color = c;
                if (dk >= 1f) dangerFlashTime = -1f;
            }

            // Selected-operator label pop-in
            if (labelPopTime >= 0f)
            {
                labelPopTime += dt;
                float pk = Mathf.Clamp01(labelPopTime / 0.22f);
                ApplyLabelPop(pk);
                if (pk >= 1f)
                {
                    labelPopTime = -1f;
                    if (selectedOperatorLabelRoot != null) selectedOperatorLabelRoot.transform.localScale = Vector3.one;
                    if (retreatFeedback != null) retreatFeedback.introScale = 1f;
                }
            }

            // Smooth health bar
            if (selectedLabelShown)
            {
                selectedHealthDisplayed = Mathf.Lerp(selectedHealthDisplayed, selectedHealthTarget, 1f - Mathf.Exp(-10f * dt));
                ApplyHealthFill();
            }

            // Placement controls pop-in
            if (placementPopTime >= 0f && placementControlsRoot != null)
            {
                placementPopTime += dt;
                float k2 = Mathf.Clamp01(placementPopTime / 0.22f);
                float s = Mathf.LerpUnclamped(0.7f, 1f, EaseOutBack(k2));
                placementControlsRoot.transform.localScale = new Vector3(s, s, 1f);
                if (k2 >= 1f)
                {
                    placementControlsRoot.transform.localScale = Vector3.one;
                    placementPopTime = -1f;
                }
            }
        }

        private void Punch(Component target)
        {
            if (target == null) return;

            Transform t = target.transform;
            for (int i = 0; i < punches.Count; i++)
            {
                if (punches[i].target == t)
                {
                    punches[i].time = 0f;
                    return;
                }
            }

            punches.Add(new PunchState { target = t, time = 0f });
        }

        private void ShowPhaseBanner(string title, string subtitle, Color color)
        {
            if (phaseBannerGroup == null) return;

            phaseBannerTitle.text = title;
            phaseBannerTitle.color = color;
            phaseBannerSubtitle.text = subtitle;
            for (int i = 0; i < phaseBannerLines.Length; i++) phaseBannerLines[i].color = color;

            if (bannerRoutine != null) StopCoroutine(bannerRoutine);
            bannerRoutine = StartCoroutine(PhaseBannerRoutine());
        }

        private IEnumerator PhaseBannerRoutine()
        {
            const float inTime = 0.25f;
            const float hold = 1.0f;
            const float outTime = 0.35f;
            const float total = inTime + hold + outTime;

            float t = 0f;
            while (t < total)
            {
                t += Time.unscaledDeltaTime;

                float alpha;
                float slide;
                if (t < inTime)
                {
                    float k = t / inTime;
                    alpha = k;
                    slide = -(1f - EaseOutCubic(k)) * 140f;
                }
                else if (t < inTime + hold)
                {
                    alpha = 1f;
                    slide = 0f;
                }
                else
                {
                    float k = (t - inTime - hold) / outTime;
                    alpha = 1f - k;
                    slide = k * 140f;
                }

                phaseBannerGroup.alpha = Mathf.Clamp01(alpha);
                phaseBannerContent.anchoredPosition = new Vector2(slide, 0f);
                yield return null;
            }

            phaseBannerGroup.alpha = 0f;
            phaseBannerContent.anchoredPosition = Vector2.zero;
        }

        private IEnumerator IntroRoutine()
        {
            if (topBarRect == null || deckBarRect == null) yield break;

            Vector2 topTarget = topBarRect.anchoredPosition;
            Vector2 deckTarget = deckBarRect.anchoredPosition;
            topBarRect.anchoredPosition = topTarget + new Vector2(0f, 100f);
            deckBarRect.anchoredPosition = deckTarget + new Vector2(0f, -180f);

            const float duration = 0.55f;
            const float deckDelay = 0.1f;
            float t = 0f;
            while (t < duration + deckDelay)
            {
                t += Time.unscaledDeltaTime;
                float topK = EaseOutCubic(Mathf.Clamp01(t / duration));
                float deckK = EaseOutCubic(Mathf.Clamp01((t - deckDelay) / duration));
                topBarRect.anchoredPosition = topTarget + new Vector2(0f, 100f * (1f - topK));
                deckBarRect.anchoredPosition = deckTarget + new Vector2(0f, -180f * (1f - deckK));
                yield return null;
            }

            topBarRect.anchoredPosition = topTarget;
            deckBarRect.anchoredPosition = deckTarget;
        }

        private MenuButtonFeedback StyleButton(Button button, Color background, Color glow, float hoverScale, System.Action clicked = null)
        {
            button.GetComponent<Image>().color = background;
            return AttachFeedback(button, glow, hoverScale, clicked);
        }

        private MenuButtonFeedback AttachFeedback(Button button, Color glow, float hoverScale = 1.05f, System.Action clicked = null)
        {
            if (button == null) return null;

            // The feedback component drives the visuals, so disable Unity's color tint transition.
            button.transition = Selectable.Transition.None;

            var feedback = button.GetComponent<MenuButtonFeedback>();
            if (feedback == null) feedback = button.gameObject.AddComponent<MenuButtonFeedback>();

            feedback.hoverScale = hoverScale;
            feedback.respondToFocus = false; // clicked HUD buttons stay "selected"; don't keep them lit
            feedback.Hovered = PlayHover;
            if (clicked != null) feedback.Clicked = clicked;
            else feedback.Clicked = PlayClick;

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

        private void PlayHover() { PlaySfx(hoverClip, Random.Range(0.97f, 1.03f)); }
        private void PlayClick() { PlaySfx(clickClip, 1f); }

        private void PlaySfx(AudioClip clip, float pitch)
        {
            AudioManager.Instance?.PlaySfx(clip, sfxVolume, pitch);
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
        // UI Helpers
        // ============================

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Text CreateText(Transform parent, string objectName, string value, int fontSize, TextAnchor alignment)
        {
            var textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.text = value;
            text.font = UIFontHelper.GetPixelFont();
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
            text.raycastTarget = false;
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

        /// <summary>Transparent center, opaque edges — tinted red for the life-lost flash.</summary>
        private static Sprite CreateVignetteSprite()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float ux = x / (size - 1f) * 2f - 1f;
                    float uy = y / (size - 1f) * 2f - 1f;
                    float d = Mathf.Sqrt(ux * ux + uy * uy) / 1.41421f;
                    float a = Mathf.SmoothStep(0.35f, 1f, d);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
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