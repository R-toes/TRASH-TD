using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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
        private static Sprite cooldownClockSprite;
        private static Sprite waveMarkSprite;
        private static Sprite enemySkullSprite;
        private static Sprite trashIconSprite;
        private static Sprite pauseIconSprite;
        private static Sprite exitIconSprite;
        private static readonly Dictionary<OperatorClass, Sprite> ClassIconSprites = new Dictionary<OperatorClass, Sprite>();

        public enum ButtonContainerType
        {
            Primary,
            Secondary,
            Dark,
            Danger,
            Warning,
            IconDark,
            IconDanger,
            IconPrimary,
            IconSecondary
        }

        private static readonly Dictionary<ButtonContainerType, Sprite> ButtonContainerSprites = new Dictionary<ButtonContainerType, Sprite>();

        public enum PanelContainerType
        {
            Dark,
            Metal,
            Glass,
            Modal,
            Rust,
            Teal,
            Tooltip
        }

        private static readonly Dictionary<PanelContainerType, Sprite> PanelContainerSprites = new Dictionary<PanelContainerType, Sprite>();

        // ── Palette ──────────────────────────────────────────
        private static readonly Color AccentColor = new Color(0.18f, 0.82f, 0.45f, 1f);
        private static readonly Color DarkButton = new Color(0.10f, 0.12f, 0.16f, 1f);
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

        private Sprite[] deckBgSprites;
        private Sprite[] topBarBgSprites;
        // Bottom Right Panel BG animations (1x, 2x, 3x speed animations, with legacy fallback).
        private Sprite[] bottomRightBg1xSprites;
        private Sprite[] bottomRightBg2xSprites;
        private Sprite[] bottomRightBg3xSprites;
        private Sprite[] bottomRightBgSprites;

        private Canvas canvas;
        private GameObject pausePanel;
        private GameObject stageInfoPanel;
        private Text phaseHeaderText;
        private Text waveText;
        private Image waveIconImage;
        private Text enemyText;
        private Image enemyIconImage;
        private Text lpText;
        private Text phaseText;
        private Text squadCountText;
        private GameObject selectedOperatorLabelRoot;
        private GameObject environmentBriefingPanel;
        private RectTransform environmentBriefingCardRect;
        private Text environmentBriefingTitle;
        private Text environmentBriefingDescription;
        private GameObject stage2ThrownItemsRoot;
        private Image stage2GlassIcon;
        private Image stage2MetalIcon;
        private Image stage2ToolsIcon;
        private GameObject stage5TrafficRoot;
        private Image stage5VehicleLeftIcon;
        private Image stage5VehicleCenterIcon;
        private Image stage5VehicleRightIcon;
        private Text stage5TrafficBodyText;
        private bool environmentBriefingDismissed;
        private Text selectedOperatorNameText;
        private Text selectedOperatorHealthText;
        private Text selectedOperatorAtkText;
        private Text selectedOperatorRarityText;
        private Image selectedOperatorHealthFill;
        private RectTransform selectedOperatorHealthFillRect;
        private GameObject selectedOperatorUpgradeBadge;
        private UpgradeArrowGraphic selectedOperatorUpgradeBadgeGraphic;
        private Button selectedOperatorShowMoreButton;
        private GameObject selectedOperatorDetailsPanel;
        private Image selectedOperatorDetailsAccent;
        private Image selectedOperatorDetailsPortrait;
        private Image selectedOperatorDetailsClassIcon;
        private Text selectedOperatorDetailsName;
        private Text[] selectedOperatorDetailsChipLabels;
        private Image[] selectedOperatorDetailsChipBackgrounds;
        private Outline[] selectedOperatorDetailsChipOutlines;
        private Text selectedOperatorDetailsRarity;
        private Text[] selectedOperatorDetailsStatValues;
        private Text selectedOperatorDetailsRoles;
        private Text selectedOperatorDetailsSkillHeading;
        private Text selectedOperatorDetailsSkill;
        private OperatorBase selectedOperatorDetailsTarget;
        private GameObject placementControlsRoot;
        private Button retreatOperatorButton;
        private Button placementConfirmButton;
        private Button startWaveButton;
        private Text startWaveButtonText;
        private Button[] speedButtons;
        private MenuButtonFeedback[] speedFeedbacks;
        private RectTransform trashDropZone;
        private GameObject discardConfirmationPanel;
        private DraftCard pendingDiscardCard;
        private int pendingDiscardSlot = -1;
        private Button[] deckButtons;
        private Text[] deckButtonLabels;
        private Image[] deckButtonImages;
        private Image[] deckPortraitImages;
        private Image[] deckClassBadgeImages;
        private Image[] deckClassIconImages;
        private GameObject[] deckClassBadges;
        private GameObject[] deckCooldownBadges;
        private Text[] deckCooldownLabels;
        private GameObject[] deckHeatBadges;
        private Text[] deckHeatLabels;
        private GameObject[] deckUpgradeBadges;
        private UpgradeArrowGraphic[] deckUpgradeBadgeLabels;
        // Framiliar Copy Menu
        private GameObject framiliarCopyMenuRoot;
        private Text framiliarSubtitleText;
        private Text framiliarEmptyMessageText;
        private readonly List<DraftCard> activeFramiliarMatchingCards = new List<DraftCard>();
        private Button[] framiliarCopyButtons;
        private Text[] framiliarCopyLabels;
        private Image[] framiliarCopyPortraits;
        private Image[] framiliarCopyClassBadgeImages;
        private Image[] framiliarCopyClassIconImages;
        private GameObject[] framiliarCopyClassBadges;
        private MenuButtonFeedback[] framiliarCopyFeedbacks;
        private TrashTD.Operators.FramiliarOperator activeFramiliarOperator;
        private TrashTD.Operators.FramiliarOperator lastFramiliarMenuOperator;
        private int lastFramiliarMenuRarity = -1;
        private GridCell lastFramiliarMenuCell;
        private int lastFramiliarMenuDeckCount = -1;
        public bool IsFramiliarCopyMenuOpen => framiliarCopyMenuRoot != null && framiliarCopyMenuRoot.activeSelf;
        private CardDraftSystem draftSystem;
        private WaveManager waveManager;
        private EnemyManager enemyManager;
        private GameManager gameManager;
        private PlayerDeck playerDeck;
        private GridManager gridManager;
        private OperatorManager operatorManager;
        private StageBootstrapper stageBootstrapper;
        private TrashTD.Development.SandboxController sandboxController;

        private int selectedDeckSlot = -1;
        public bool IsEnvironmentBriefingActive => environmentBriefingPanel != null && environmentBriefingPanel.activeSelf;
        public bool ShouldBlockDraftOverlay
        {
            get
            {
                if (environmentBriefingDismissed || gameManager == null || gameManager.CurrentStage == null) return false;
                string stageId = stageBootstrapper != null ? stageBootstrapper.EnvironmentStageId : gameManager.CurrentStage.stageId;
                return stageId == "STAGE_02" || stageId == "STAGE_03" || stageId == "STAGE_04" || stageId == "STAGE_05" || stageId == "STAGE_06";
            }
        }

        public event System.Action EnvironmentBriefingDismissed;

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
        private RectTransform livesTubeRect;
        private Image livesTubeFrame;
        private RectTransform livesLiquidRect;
        private GameObject livesSurface;
        private Image dangerFlashImage;
        private Image topBarBgAnimImage;
        private Image deckBgAnimImage;
        private float topBarBgAnimTime;
        private int topBarBgAnimFrame;
        private float deckBgAnimTime;
        private int deckBgAnimFrame;
        // Speed panel animation and crossfade: each speed (1x, 2x, 3x) has its own animated sprite loop.
        // When speed changes, "to" fades in over "from" for a smooth transition.
        private Image bottomRightBgFromImage;
        private Image bottomRightBgToImage;
        private int bottomRightBgFromSpeed = 0;
        private int bottomRightBgToSpeed = 0;
        private float bottomRightBgFade = 1f;
        private float bottomRightBgAnimTime;
        private int bottomRightBgAnimFrame = -1;
        private int currentSpeedSelectionIndex = -1;
        private CanvasGroup phaseBannerGroup;
        private RectTransform phaseBannerContent;
        private Text phaseBannerTitle;
        private Text phaseBannerSubtitle;
        private Image[] phaseBannerLines;
        private RectTransform cooldownToastRect;
        private CanvasGroup cooldownToastGroup;
        private Text cooldownToastTitle;
        private Text cooldownToastMessage;
        private Coroutine cooldownToastRoutine;
        private CanvasGroup pauseGroup;
        private RectTransform pauseCard;
        private AudioSettingsUI audioSettingsUI;
        private ReclaimersIntelUI intelUI;
        private CardDraftOverlayUI draftOverlayUI;
        private int lastEscapeHandledFrame = -1;
        private Button previewPathButton;
        private RectTransform bottomRightPanelRect;
        private Text previewPathButtonText;
        private MenuButtonFeedback previewPathFeedback;
        private GameplayPathPreviewOverlay pathPreviewOverlay;

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
        private float livesTarget = 1f;
        private float livesDisplayed = -1f; // < 0 until the first lives update, which snaps instead of draining
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
            draftSystem = FindAnyObjectByType<CardDraftSystem>();
            draftOverlayUI = FindAnyObjectByType<CardDraftOverlayUI>();
            waveManager = FindAnyObjectByType<WaveManager>();
            enemyManager = FindAnyObjectByType<EnemyManager>();
            gameManager = FindAnyObjectByType<GameManager>();
            playerDeck = FindAnyObjectByType<PlayerDeck>();
            gridManager = FindAnyObjectByType<GridManager>();
            operatorManager = FindAnyObjectByType<OperatorManager>();
            stageBootstrapper = FindAnyObjectByType<StageBootstrapper>();
            sandboxController = FindAnyObjectByType<TrashTD.Development.SandboxController>();

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            if (hoverClip == null) hoverClip = AudioManager.Instance?.GetClip(SfxId.UiHover);
            if (clickClip == null) clickClip = AudioManager.Instance?.GetClip(SfxId.UiClick);
            if (waveStartClip == null) waveStartClip = AudioManager.Instance?.GetClip(SfxId.GameplayWaveStart);
            if (lifeLostClip == null) lifeLostClip = AudioManager.Instance?.GetClip(SfxId.GameplayLifeLost);

            EnsureEventSystem();
            topBarBgSprites = Resources.LoadAll<Sprite>("UI/Top Panel BG");
            // Load the Deck BG sprite sheet frames from Resources at runtime.
            // The asset lives at Assets/Resources/UI/Deck BG.png (sprite mode: Multiple).
            deckBgSprites = Resources.LoadAll<Sprite>("UI/Deck BG");
            bottomRightBg1xSprites = LoadAndSortSprites("UI/Bottom Right Panel BG 1x animation");
            bottomRightBg2xSprites = LoadAndSortSprites("UI/Bottom Right Panel BG 2x animation");
            bottomRightBg3xSprites = LoadAndSortSprites("UI/Bottom Right Panel BG 3x animation");
            bottomRightBgSprites = LoadAndSortSprites("UI/Bottom Right Panel BG");
            BuildHud();
            if (canvas != null && pausePanel != null)
            {
                Transform pauseCard = pausePanel.transform.Find("PauseCard");
                if (pauseCard != null)
                {
                    audioSettingsUI = AudioSettingsUI.Create(
                        canvas.transform,
                        pauseCard,
                        button =>
                        {
                            StyleButton(button, ButtonContainerType.Secondary, new Color(0.55f, 0.75f, 1f, 1f), 1.04f);
                            ApplyPauseButtonLook(button, null, "icon_settings");
                        },
                        // Square gear at the right end of row 2, next to Retry
                        rect => SetPosition(rect, PausePx(PauseSettingsX, PauseRow2Px), new Vector2(0.5f, 1f), new Vector2(PauseButtonHeight, PauseButtonHeight), new Vector2(0.5f, 0.5f)));
                    intelUI = ReclaimersIntelUI.Create(
                        canvas.transform,
                        pauseCard,
                        button =>
                        {
                            StyleButton(button, ButtonContainerType.Secondary, new Color(0.4f, 1f, 0.65f, 1f), 1.04f);
                            ApplyPauseButtonLook(button, "INTEL", "icon_info");
                        },
                        // Full-width row 3, on the card's extra indicator row
                        rect => SetPosition(rect, PausePx(0f, PauseRow3Px), new Vector2(0.5f, 1f), new Vector2(PauseRowWidth, PauseButtonHeight), new Vector2(0.5f, 0.5f)));
                }
            }
        }

        private void Start()
        {
            if (waveManager != null)
            {
                waveManager.OnWaveStarted += HandleWaveStarted;
            }

            if (enemyManager != null)
            {
                enemyManager.OnEnemySpawned += HandleEnemyCountChanged;
                enemyManager.OnEnemyDied += HandleEnemyCountChanged;
                enemyManager.OnEnemyReachedExit += HandleEnemyCountChanged;
            }

            if (gameManager != null)
            {
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
            StartCoroutine(ShowEnvironmentBriefingWhenReady());
        }

        private void Update()
        {
            HandleKeyboardInput();
            if (gameManager != null)
            {
                int currentSpeed = Mathf.Clamp(Mathf.RoundToInt(gameManager.GameSpeed) - 1, 0, 2);
                if (currentSpeed != currentSpeedSelectionIndex)
                {
                    UpdateSpeedSelection(gameManager.GameSpeed);
                }
            }
            RefreshCounters();
            RefreshSelectedOperatorInfo();
            UpdateAnimations(Time.unscaledDeltaTime, Time.unscaledTime);
        }

        private void OnDestroy()
        {
            if (waveManager != null)
            {
                waveManager.OnWaveStarted -= HandleWaveStarted;
            }

            if (enemyManager != null)
            {
                enemyManager.OnEnemySpawned -= HandleEnemyCountChanged;
                enemyManager.OnEnemyDied -= HandleEnemyCountChanged;
                enemyManager.OnEnemyReachedExit -= HandleEnemyCountChanged;
            }

            if (gameManager != null)
            {
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
            CreatePathPreviewOverlay(root);
            CreateTopBar(root);
            CreateDeckBar(root);
            // Trash drop zone is created and parented inside CreateDeckBar to align with the deck background
            CreateDiscardConfirmationPanel(root);
            CreatePhaseBanner(root);
            CreateEnvironmentBriefing(root);
            CreatePlacementControls(root);
            CreateSelectedOperatorLabel(root);
            CreateSelectedOperatorDetailsPanel(root);
            CreateCooldownToast(root);
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
            labelRect.pivot = new Vector2(0.5f, 0f); // Pivot at bottom-center so panel grows upwards and never covers the operator!
            labelRect.sizeDelta = new Vector2(280f, 106f);

            Image background = selectedOperatorLabelRoot.GetComponent<Image>();
            ApplyPanelContainer(selectedOperatorLabelRoot, PanelContainerType.Tooltip);
            background.raycastTarget = false;

            var labelBorder = selectedOperatorLabelRoot.AddComponent<Outline>();
            labelBorder.effectColor = new Color(1f, 1f, 1f, 0.10f);
            labelBorder.effectDistance = new Vector2(1.5f, -1.5f);

            // Row 1: Name and Rarity (stars)
            selectedOperatorNameText = CreateText(selectedOperatorLabelRoot.transform, "Name", string.Empty, 15, TextAnchor.MiddleLeft);
            selectedOperatorNameText.fontStyle = FontStyle.Bold;
            selectedOperatorNameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            SetPosition(selectedOperatorNameText.GetComponent<RectTransform>(), new Vector2(12f, -8f), new Vector2(0f, 1f), new Vector2(180f, 22f), new Vector2(0f, 1f));
            selectedOperatorNameText.raycastTarget = false;

            selectedOperatorRarityText = CreateText(selectedOperatorLabelRoot.transform, "Rarity", string.Empty, 16, TextAnchor.MiddleRight);
            selectedOperatorRarityText.color = new Color(1f, 0.78f, 0.2f, 1f);
            SetPosition(selectedOperatorRarityText.GetComponent<RectTransform>(), new Vector2(-12f, -8f), new Vector2(1f, 1f), new Vector2(76f, 22f), new Vector2(1f, 1f));
            selectedOperatorRarityText.raycastTarget = false;

            selectedOperatorUpgradeBadge = CreateUpgradeBadge(selectedOperatorLabelRoot.transform, "SelectedOperatorUpgradeBadge", new Vector2(30f, 16f), new Vector2(-88f, -11f));
            selectedOperatorUpgradeBadgeGraphic = selectedOperatorUpgradeBadge.GetComponentInChildren<UpgradeArrowGraphic>();

            // Row 2: HP (text and health bar)
            selectedOperatorHealthText = CreateText(selectedOperatorLabelRoot.transform, "Health", string.Empty, 12, TextAnchor.MiddleLeft);
            selectedOperatorHealthText.fontStyle = FontStyle.Bold;
            SetPosition(selectedOperatorHealthText.GetComponent<RectTransform>(), new Vector2(12f, -31f), new Vector2(0f, 1f), new Vector2(110f, 18f), new Vector2(0f, 1f));
            selectedOperatorHealthText.raycastTarget = false;

            GameObject healthTrack = new GameObject("HealthTrack", typeof(RectTransform), typeof(Image));
            healthTrack.transform.SetParent(selectedOperatorLabelRoot.transform, false);
            Image healthTrackImage = healthTrack.GetComponent<Image>();
            healthTrackImage.color = new Color(0.12f, 0.14f, 0.16f, 1f);
            healthTrackImage.raycastTarget = false;
            SetPosition(healthTrack.GetComponent<RectTransform>(), new Vector2(-12f, -34f), new Vector2(1f, 1f), new Vector2(140f, 12f), new Vector2(1f, 1f));

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

            // Row 3: ATK
            selectedOperatorAtkText = CreateText(selectedOperatorLabelRoot.transform, "Atk", string.Empty, 12, TextAnchor.MiddleLeft);
            selectedOperatorAtkText.fontStyle = FontStyle.Bold;
            selectedOperatorAtkText.color = new Color(1f, 0.72f, 0.35f, 1f);
            SetPosition(selectedOperatorAtkText.GetComponent<RectTransform>(), new Vector2(12f, -53f), new Vector2(0f, 1f), new Vector2(256f, 18f), new Vector2(0f, 1f));
            selectedOperatorAtkText.raycastTarget = false;

            // Row 4: Show More
            selectedOperatorShowMoreButton = CreateButton(selectedOperatorLabelRoot.transform, "ShowOperatorDetailsButton", "SHOW MORE", new Vector2(140f, 22f), ButtonContainerType.Dark);
            SetPosition(selectedOperatorShowMoreButton.GetComponent<RectTransform>(), new Vector2(0f, -74f), new Vector2(0.5f, 1f), null, new Vector2(0.5f, 1f));
            Text showMoreLabel = selectedOperatorShowMoreButton.GetComponentInChildren<Text>();
            showMoreLabel.fontSize = 10;
            showMoreLabel.fontStyle = FontStyle.Bold;
            showMoreLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            showMoreLabel.verticalOverflow = VerticalWrapMode.Truncate;
            StyleButton(selectedOperatorShowMoreButton, ButtonContainerType.Dark, PrepColor, 1.06f);
            selectedOperatorShowMoreButton.onClick.AddListener(ToggleSelectedOperatorDetails);

            // Row 5+: Framiliar Copy Section (inside the tab)
            CreateFramiliarCopySection(selectedOperatorLabelRoot.transform);

            // Retreat button (positioned to the right of the tab, horizontally aligned with Row 1-4)
            retreatOperatorButton = CreateButton(root, "RetreatOperatorButton", string.Empty, new Vector2(44f, 44f), ButtonContainerType.IconDanger);
            SetPosition(retreatOperatorButton.GetComponent<RectTransform>(), new Vector2(151f, 53f), new Vector2(0.5f, 0.5f));
            SetPlacementButtonStyle(retreatOperatorButton, ButtonContainerType.IconDanger, 28);
            retreatOperatorButton.onClick.AddListener(RetreatSelectedOperator);
            retreatFeedback = AttachFeedback(retreatOperatorButton, new Color(1f, 0.45f, 0.4f, 1f), 1.1f);

            var exitIcon = GetExitIconSprite();
            if (exitIcon != null)
            {
                CreateIconImage(retreatOperatorButton.transform, "Icon", exitIcon, Color.white, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(24f, 24f), new Vector2(0.5f, 0.5f));
            }
            else
            {
                var label = retreatOperatorButton.GetComponentInChildren<Text>();
                if (label != null) label.text = "↶";
            }

            selectedOperatorLabelRoot.SetActive(false);
            retreatOperatorButton.gameObject.SetActive(false);
        }

        private void CreateSelectedOperatorDetailsPanel(Transform root)
        {
            selectedOperatorDetailsPanel = new GameObject(
                "SelectedOperatorDetailsPanel",
                typeof(RectTransform),
                typeof(Image),
                typeof(Outline));
            selectedOperatorDetailsPanel.transform.SetParent(root, false);

            RectTransform panelRect = selectedOperatorDetailsPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 0.5f);
            panelRect.anchorMax = new Vector2(1f, 0.5f);
            panelRect.pivot = new Vector2(1f, 0.5f);
            panelRect.sizeDelta = new Vector2(500f, 640f);
            panelRect.anchoredPosition = new Vector2(-24f, 0f);

            Image panelImage = selectedOperatorDetailsPanel.GetComponent<Image>();
            ApplyPanelContainer(selectedOperatorDetailsPanel, PanelContainerType.Metal);
            panelImage.raycastTarget = true;
            Outline panelOutline = selectedOperatorDetailsPanel.GetComponent<Outline>();
            panelOutline.effectColor = new Color(1f, 1f, 1f, 0.06f);
            panelOutline.effectDistance = new Vector2(2f, -2f);

            var accent = new GameObject("DetailAccent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(selectedOperatorDetailsPanel.transform, false);
            RectTransform accentRect = accent.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.sizeDelta = new Vector2(0f, 4f);
            selectedOperatorDetailsAccent = accent.GetComponent<Image>();
            selectedOperatorDetailsAccent.color = new Color(0.18f, 0.82f, 0.45f, 0.35f);
            selectedOperatorDetailsAccent.raycastTarget = false;

            Transform content = selectedOperatorDetailsPanel.transform;

            const float detailsMargin = 28f;
            const float detailsInnerWidth = 444f;
            var portraitObject = new GameObject("OperatorPortrait", typeof(RectTransform), typeof(Image));
            portraitObject.transform.SetParent(content, false);
            selectedOperatorDetailsPortrait = portraitObject.GetComponent<Image>();
            selectedOperatorDetailsPortrait.preserveAspect = true;
            selectedOperatorDetailsPortrait.raycastTarget = false;
            selectedOperatorDetailsPortrait.color = Color.white;
            SetPosition(selectedOperatorDetailsPortrait.rectTransform, new Vector2(-detailsMargin, -16f), new Vector2(1f, 1f), new Vector2(72f, 88f), new Vector2(1f, 1f));

            selectedOperatorDetailsName = CreateText(content, "OperatorName", string.Empty, 32, TextAnchor.MiddleLeft);
            selectedOperatorDetailsName.fontStyle = FontStyle.Bold;
            selectedOperatorDetailsName.resizeTextForBestFit = true;
            selectedOperatorDetailsName.resizeTextMinSize = 18;
            selectedOperatorDetailsName.resizeTextMaxSize = 32;
            selectedOperatorDetailsName.raycastTarget = false;
            SetPosition(selectedOperatorDetailsName.rectTransform, new Vector2(detailsMargin, -28f), new Vector2(0f, 1f), new Vector2(detailsInnerWidth - 90f, 42f), new Vector2(0f, 1f));

            selectedOperatorDetailsRarity = CreateText(content, "Rarity", string.Empty, 22, TextAnchor.MiddleLeft);
            selectedOperatorDetailsRarity.raycastTarget = false;
            SetPosition(selectedOperatorDetailsRarity.rectTransform, new Vector2(detailsMargin, -72f), new Vector2(0f, 1f), new Vector2(detailsInnerWidth - 90f, 28f), new Vector2(0f, 1f));

            const float classIconSize = 34f;
            var iconObject = new GameObject("OperatorClassIcon", typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(content, false);
            selectedOperatorDetailsClassIcon = iconObject.GetComponent<Image>();
            selectedOperatorDetailsClassIcon.preserveAspect = true;
            selectedOperatorDetailsClassIcon.raycastTarget = false;
            selectedOperatorDetailsClassIcon.color = Color.white;
            SetPosition(selectedOperatorDetailsClassIcon.rectTransform, new Vector2(detailsMargin, -114f), new Vector2(0f, 1f), new Vector2(classIconSize, classIconSize), new Vector2(0f, 1f));

            float chipsStartX = detailsMargin + classIconSize + 10f;
            float chipsTotalWidth = detailsInnerWidth - classIconSize - 10f;
            const float chipGap = 10f;
            float detailChipWidth = (chipsTotalWidth - chipGap * 2f) / 3f;

            selectedOperatorDetailsChipBackgrounds = new Image[3];
            selectedOperatorDetailsChipLabels = new Text[3];
            selectedOperatorDetailsChipOutlines = new Outline[3];
            for (int i = 0; i < selectedOperatorDetailsChipLabels.Length; i++)
            {
                var chip = new GameObject($"TypeChip_{i}", typeof(RectTransform), typeof(Image), typeof(Outline));
                chip.transform.SetParent(content, false);
                float chipX = chipsStartX + (detailChipWidth + chipGap) * i;
                SetPosition(chip.GetComponent<RectTransform>(), new Vector2(chipX, -114f), new Vector2(0f, 1f), new Vector2(detailChipWidth, 34f), new Vector2(0f, 1f));
                selectedOperatorDetailsChipBackgrounds[i] = chip.GetComponent<Image>();
                selectedOperatorDetailsChipBackgrounds[i].color = new Color(0.07f, 0.085f, 0.115f, 1f);
                selectedOperatorDetailsChipBackgrounds[i].raycastTarget = false;
                selectedOperatorDetailsChipOutlines[i] = chip.GetComponent<Outline>();
                selectedOperatorDetailsChipOutlines[i].effectDistance = new Vector2(1.5f, -1.5f);
                selectedOperatorDetailsChipOutlines[i].effectColor = new Color(1f, 1f, 1f, 0.2f);

                selectedOperatorDetailsChipLabels[i] = CreateText(chip.transform, $"Label_{i}", string.Empty, 13, TextAnchor.MiddleCenter);
                selectedOperatorDetailsChipLabels[i].fontStyle = FontStyle.Bold;
                selectedOperatorDetailsChipLabels[i].resizeTextForBestFit = true;
                selectedOperatorDetailsChipLabels[i].resizeTextMinSize = 9;
                selectedOperatorDetailsChipLabels[i].resizeTextMaxSize = 13;
                selectedOperatorDetailsChipLabels[i].raycastTarget = false;
                RectTransform chipTextRect = selectedOperatorDetailsChipLabels[i].rectTransform;
                chipTextRect.anchorMin = Vector2.zero;
                chipTextRect.anchorMax = Vector2.one;
                chipTextRect.offsetMin = Vector2.zero;
                chipTextRect.offsetMax = Vector2.zero;
            }

            selectedOperatorDetailsRoles = CreateText(content, "Roles", string.Empty, 12, TextAnchor.MiddleLeft);
            selectedOperatorDetailsRoles.fontStyle = FontStyle.Normal;
            selectedOperatorDetailsRoles.resizeTextForBestFit = true;
            selectedOperatorDetailsRoles.resizeTextMinSize = 9;
            selectedOperatorDetailsRoles.resizeTextMaxSize = 12;
            selectedOperatorDetailsRoles.raycastTarget = false;
            SetPosition(selectedOperatorDetailsRoles.rectTransform, new Vector2(detailsMargin, -156f), new Vector2(0f, 1f), new Vector2(detailsInnerWidth, 22f), new Vector2(0f, 1f));

            CreateOperatorDetailsDivider(content, detailsMargin, detailsInnerWidth, 190f);

            Text statsHeading = CreateText(content, "StatsHeading", "STATS", 11, TextAnchor.MiddleLeft);
            statsHeading.color = new Color(0.6f, 0.65f, 0.7f, 1f);
            statsHeading.raycastTarget = false;
            statsHeading.fontStyle = FontStyle.Bold;
            SetPosition(statsHeading.rectTransform, new Vector2(detailsMargin, -202f), new Vector2(0f, 1f), new Vector2(detailsInnerWidth, 18f), new Vector2(0f, 1f));

            selectedOperatorDetailsStatValues = new Text[6];
            Color[] statColors =
            {
                new Color(0.35f, 0.85f, 0.45f, 1f),
                new Color(0.95f, 0.40f, 0.35f, 1f),
                new Color(0.35f, 0.60f, 1f, 1f),
                new Color(0.70f, 0.45f, 1f, 1f),
                new Color(1f, 0.8f, 0.3f, 1f),
                new Color(0.4f, 0.85f, 0.9f, 1f)
            };
            string[] statNames = { "HP", "ATK", "DEF", "RES", "BLOCK", "ATK SPD" };
            float tileWidth = (detailsInnerWidth - 12f) / 2f;
            float tileHeight = 62f;
            float rowStep = tileHeight + 8f;
            float gridTop = 226f;
            float rightX = detailsMargin + tileWidth + 12f;
            for (int i = 0; i < selectedOperatorDetailsStatValues.Length; i++)
            {
                int row = i / 2;
                float x = i % 2 == 0 ? detailsMargin : rightX;
                selectedOperatorDetailsStatValues[i] = CreateOperatorDetailStatTile(
                    content,
                    statNames[i],
                    statColors[i],
                    x,
                    gridTop + rowStep * row,
                    tileWidth,
                    tileHeight);
            }

            selectedOperatorDetailsSkillHeading = CreateText(content, "SkillHeading", "ABILITY", 11, TextAnchor.MiddleLeft);
            selectedOperatorDetailsSkillHeading.color = new Color(0.6f, 0.65f, 0.7f, 1f);
            selectedOperatorDetailsSkillHeading.raycastTarget = false;
            selectedOperatorDetailsSkillHeading.fontStyle = FontStyle.Bold;
            SetPosition(selectedOperatorDetailsSkillHeading.rectTransform, new Vector2(detailsMargin, -450f), new Vector2(0f, 1f), new Vector2(detailsInnerWidth, 18f), new Vector2(0f, 1f));

            selectedOperatorDetailsSkill = CreateText(content, "SkillDescription", string.Empty, 15, TextAnchor.UpperLeft);
            selectedOperatorDetailsSkill.fontStyle = FontStyle.Normal;
            selectedOperatorDetailsSkill.lineSpacing = 1.25f;
            selectedOperatorDetailsSkill.resizeTextForBestFit = true;
            selectedOperatorDetailsSkill.resizeTextMinSize = 11;
            selectedOperatorDetailsSkill.resizeTextMaxSize = 15;
            selectedOperatorDetailsSkill.horizontalOverflow = HorizontalWrapMode.Wrap;
            selectedOperatorDetailsSkill.verticalOverflow = VerticalWrapMode.Truncate;
            selectedOperatorDetailsSkill.color = new Color(0.88f, 0.92f, 0.96f, 1f);
            selectedOperatorDetailsSkill.raycastTarget = false;
            SetPosition(selectedOperatorDetailsSkill.rectTransform, new Vector2(detailsMargin, -474f), new Vector2(0f, 1f), new Vector2(detailsInnerWidth, 134f), new Vector2(0f, 1f));

            selectedOperatorDetailsPanel.SetActive(false);
        }

        private Text CreateOperatorDetailStatTile(Transform parent, string label, Color accentColor, float x, float y, float width, float height)
        {
            var tile = new GameObject($"Stat_{label}", typeof(RectTransform), typeof(Image));
            tile.transform.SetParent(parent, false);
            SetPosition(tile.GetComponent<RectTransform>(), new Vector2(x, -y), new Vector2(0f, 1f), new Vector2(width, height), new Vector2(0f, 1f));
            tile.GetComponent<Image>().color = new Color(0.07f, 0.085f, 0.115f, 1f);
            tile.GetComponent<Image>().raycastTarget = false;

            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(tile.transform, false);
            RectTransform barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.sizeDelta = new Vector2(4f, 0f);
            barRect.anchoredPosition = Vector2.zero;
            Image barImage = bar.GetComponent<Image>();
            barImage.color = accentColor;
            barImage.raycastTarget = false;

            Text labelText = CreateText(tile.transform, "Label", label, 11, TextAnchor.MiddleLeft);
            labelText.color = new Color(0.6f, 0.65f, 0.7f, 1f);
            labelText.raycastTarget = false;
            labelText.fontStyle = FontStyle.Bold;
            SetPosition(labelText.rectTransform, new Vector2(18f, -8f), new Vector2(0f, 1f), new Vector2(width - 28f, 18f), new Vector2(0f, 1f));

            Text valueText = CreateText(tile.transform, "Value", "0", 26, TextAnchor.MiddleLeft);
            valueText.color = new Color(0.95f, 0.95f, 0.98f, 1f);
            valueText.raycastTarget = false;
            valueText.fontStyle = FontStyle.Bold;
            SetPosition(valueText.rectTransform, new Vector2(18f, -26f), new Vector2(0f, 1f), new Vector2(width - 28f, 32f), new Vector2(0f, 1f));
            return valueText;
        }

        private static void CreateOperatorDetailsDivider(Transform parent, float margin, float width, float top)
        {
            var divider = new GameObject("DetailsDivider", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(parent, false);
            SetPosition(divider.GetComponent<RectTransform>(), new Vector2(margin, -top), new Vector2(0f, 1f), new Vector2(width, 2f), new Vector2(0f, 1f));
            Image image = divider.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.08f);
            image.raycastTarget = false;
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

            var confirmButton = CreateButton(placementControlsRoot.transform, "ConfirmPlacementButton", "PLACE", new Vector2(84f, 34f), ButtonContainerType.Primary);
            SetPlacementButtonPosition(confirmButton, new Vector2(150f, 19f));
            SetPlacementButtonStyle(confirmButton, ButtonContainerType.Primary, 15);
            confirmButton.onClick.AddListener(() => stageBootstrapper?.ConfirmOperatorPlacement());
            confirmFeedback = AttachFeedback(confirmButton, AccentColor, 1.08f);

            var cancelButton = CreateButton(placementControlsRoot.transform, "CancelPlacementButton", "CANCEL", new Vector2(84f, 34f), ButtonContainerType.Danger);
            SetPlacementButtonPosition(cancelButton, new Vector2(150f, -21f));
            SetPlacementButtonStyle(cancelButton, ButtonContainerType.Danger, 14);
            cancelButton.onClick.AddListener(() => stageBootstrapper?.CancelOperatorPlacement());
            AttachFeedback(cancelButton, new Color(1f, 0.4f, 0.4f, 1f), 1.08f);

            placementConfirmButton = confirmButton;
            placementControlsRoot.SetActive(false);
        }

        private void CreateFacingButton(string objectName, string label, OperatorFacing facing, Vector2 position)
        {
            var button = CreateButton(placementControlsRoot.transform, objectName, label, new Vector2(42f, 42f), ButtonContainerType.IconDark);
            SetPlacementButtonPosition(button, position);
            SetPlacementButtonStyle(button, ButtonContainerType.IconDark, 22);
            button.onClick.AddListener(() => stageBootstrapper?.SetPlacementFacing(facing));
            AttachFeedback(button, PrepColor, 1.12f);
        }

        private static void SetPlacementButtonPosition(Button button, Vector2 position)
        {
            SetPosition(button.GetComponent<RectTransform>(), position, new Vector2(0.5f, 0.5f), null, new Vector2(0.5f, 0.5f));
        }

        private static void SetPlacementButtonStyle(Button button, Color backgroundColor, int fontSize)
        {
            ButtonContainerType container = ResolveContainerType(backgroundColor);
            SetPlacementButtonStyle(button, container, fontSize);
        }

        private static void SetPlacementButtonStyle(Button button, ButtonContainerType containerType, int fontSize)
        {
            ApplyButtonContainer(button, containerType);
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
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, GetCanvasCamera(), out Vector2 localPosition))
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
                selectedOperatorDetailsPanel.SetActive(false);
                selectedOperatorDetailsTarget = null;
                return;
            }

            Camera gameCamera = Camera.main;
            if (gameCamera == null) return;

            selectedOperatorNameText.text = operatorName;
            Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(gameCamera, worldPosition);
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, GetCanvasCamera(), out Vector2 localPosition))
            {
                RectTransform labelRect = selectedOperatorLabelRoot.GetComponent<RectTransform>();
                RectTransform retreatRect = retreatOperatorButton.GetComponent<RectTransform>();

                float halfW = labelRect.sizeDelta.x * 0.5f;
                float panelH = labelRect.sizeDelta.y;

                // Screen clamping: keep the panel and retreat button entirely on-screen
                float minX = -canvasRect.rect.width * 0.5f + halfW + 10f;
                float maxX = canvasRect.rect.width * 0.5f - halfW - 60f;
                float minY = -canvasRect.rect.height * 0.5f + 10f;
                float maxY = canvasRect.rect.height * 0.5f - panelH - 10f;

                localPosition.x = Mathf.Clamp(localPosition.x, minX, maxX);
                localPosition.y = Mathf.Clamp(localPosition.y, minY, maxY);

                labelRect.anchoredPosition = localPosition;
                selectedOperatorLabelRoot.SetActive(true);
                selectedOperatorShowMoreButton.gameObject.SetActive(true);

                // Retreat button positioned to the right of the tab, vertically aligned with Row 1-4
                float retreatX = localPosition.x + halfW + 28f;
                float retreatY = localPosition.y + panelH - 53f;
                retreatRect.anchoredPosition = new Vector2(retreatX, retreatY);
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
            int currentATK,
            OperatorRarity rarity,
            OperatorRarity baseRarity,
            Vector3 worldPosition)
        {
            // The skill text lives only in the SHOW MORE details panel (RefreshSelectedOperatorDetails).
            SetSelectedOperatorName(operatorName, worldPosition);
            if (string.IsNullOrEmpty(operatorName)) return;

            int upgradeLevels = Mathf.Clamp((int)rarity - (int)baseRarity, 0, 2);
            selectedOperatorUpgradeBadgeGraphic.SetArrowCount(upgradeLevels);
            selectedOperatorUpgradeBadge.SetActive(upgradeLevels > 0);
            float healthRatio = maxHP > 0 ? Mathf.Clamp01((float)currentHP / maxHP) : 0f;
            selectedOperatorHealthText.text = $"HP {currentHP} / {maxHP}";
            if (selectedOperatorAtkText != null)
            {
                selectedOperatorAtkText.text = $"ATK {currentATK}";
            }
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

        public void SetSelectedOperatorName(
            string operatorName,
            int currentHP,
            int maxHP,
            OperatorRarity rarity,
            OperatorRarity baseRarity,
            Vector3 worldPosition)
        {
            SetSelectedOperatorName(operatorName, currentHP, maxHP, 0, rarity, baseRarity, worldPosition);
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
                HideFramiliarCopyMenu();
                return;
            }

            if (selectedOperator is TrashTD.Operators.FramiliarOperator framiliar && !framiliar.IsCopied)
            {
                ShowFramiliarCopyMenu(framiliar);
            }
            else
            {
                HideFramiliarCopyMenu();
            }

            selectedOperatorDetailsTarget = selectedOperator;
            Vector3 labelPosition = selectedOperator.DeployedCell.WorldPosition + Vector3.up * gridManager.CellSize * 0.7f;
            SetSelectedOperatorName(
                selectedOperator.Data.operatorName,
                selectedOperator.CurrentHP,
                selectedOperator.MaxHP,
                selectedOperator.CurrentATK,
                selectedOperator.CurrentRarity,
                selectedOperator.Data.baseRarity,
                labelPosition);
        }

        private void ToggleSelectedOperatorDetails()
        {
            OperatorBase selectedOperator = operatorManager != null ? operatorManager.SelectedOperator : null;
            if (selectedOperator == null || selectedOperator.Data == null || selectedOperatorDetailsPanel == null) return;

            bool show = !selectedOperatorDetailsPanel.activeSelf;
            selectedOperatorDetailsPanel.SetActive(show);
            if (show) UpdateSelectedOperatorDetails(selectedOperator);
        }

        private void UpdateSelectedOperatorDetails(OperatorBase op)
        {
            if (op == null || op.Data == null) return;

            OperatorData data = op.Data;
            selectedOperatorDetailsTarget = op;
            selectedOperatorDetailsPortrait.sprite = data.portrait;
            selectedOperatorDetailsPortrait.enabled = data.portrait != null;
            selectedOperatorDetailsPortrait.color = Color.white;
            selectedOperatorDetailsName.text = data.operatorName;
            selectedOperatorDetailsClassIcon.sprite = GetClassIconSprite(data.operatorClass);
            selectedOperatorDetailsClassIcon.color = Color.white;
            selectedOperatorDetailsClassIcon.enabled = selectedOperatorDetailsClassIcon.sprite != null;
            selectedOperatorDetailsChipLabels[0].text = data.operatorClass.ToString().ToUpperInvariant();
            selectedOperatorDetailsChipLabels[1].text = data.position == OperatorPosition.Melee ? "MELEE" : "RANGED";
            Color classColor = GetDraftClassColor(data.operatorClass);
            Color positionColor = data.position == OperatorPosition.Melee
                ? new Color(0.95f, 0.60f, 0.25f, 1f)
                : new Color(0.35f, 0.78f, 0.92f, 1f);
            Color damageColor = data.damageType == DamageType.Arts
                ? new Color(0.70f, 0.50f, 1.00f, 1f)
                : new Color(0.90f, 0.45f, 0.35f, 1f);
            selectedOperatorDetailsChipLabels[2].text = data.damageType == DamageType.Arts ? "ARTS" : "PHYSICAL";
            Color[] chipColors = { classColor, positionColor, damageColor };
            Color tileColor = new Color(0.07f, 0.085f, 0.115f, 1f);
            for (int i = 0; i < chipColors.Length; i++)
            {
                selectedOperatorDetailsChipLabels[i].color = Color.Lerp(chipColors[i], Color.white, 0.35f);
                selectedOperatorDetailsChipBackgrounds[i].color = Color.Lerp(tileColor, chipColors[i], 0.22f);
                selectedOperatorDetailsChipOutlines[i].effectColor = new Color(chipColors[i].r, chipColors[i].g, chipColors[i].b, 0.55f);
            }

            int stars = Mathf.Clamp((int)op.CurrentRarity, 1, 5);
            Color rarityColor = GetOperatorRarityColor(stars);
            selectedOperatorDetailsAccent.color = new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.35f);
            selectedOperatorDetailsRarity.color = rarityColor;
            selectedOperatorDetailsRarity.text = new string('★', stars) + "<color=#3C424D>" + new string('☆', 5 - stars) + "</color>";
            selectedOperatorDetailsStatValues[0].text = data.GetScaledHP(op.CurrentRarity).ToString();
            selectedOperatorDetailsStatValues[1].text = data.GetScaledATK(op.CurrentRarity).ToString();
            selectedOperatorDetailsStatValues[2].text = data.GetScaledDEF(op.CurrentRarity).ToString();
            selectedOperatorDetailsStatValues[3].text = data.GetScaledRES(op.CurrentRarity).ToString();
            selectedOperatorDetailsStatValues[4].text = data.blockCount.ToString();
            selectedOperatorDetailsStatValues[5].text = $"{data.attackInterval:0.##}s";
            selectedOperatorDetailsRoles.text = data.roleTags != null && data.roleTags.Length > 0
                ? string.Join(" / ", data.roleTags)
                : GetOperatorDraftDescription(data.operatorClass);
            string skillDesc = op.GetSkillDescription();
            bool hasSkill = !string.IsNullOrWhiteSpace(skillDesc);
            selectedOperatorDetailsSkillHeading.gameObject.SetActive(hasSkill);
            selectedOperatorDetailsSkill.gameObject.SetActive(hasSkill);
            if (hasSkill) selectedOperatorDetailsSkill.text = skillDesc.Trim();
        }

        private static string GetOperatorDraftDescription(OperatorClass opClass)
        {
            return opClass switch
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

        private static Color GetDraftClassColor(OperatorClass opClass)
        {
            return opClass switch
            {
                OperatorClass.Guard => new Color(0.80f, 0.28f, 0.28f, 1f),
                OperatorClass.Defender => new Color(0.30f, 0.45f, 0.85f, 1f),
                OperatorClass.Sniper => new Color(0.30f, 0.75f, 0.30f, 1f),
                OperatorClass.Caster => new Color(0.70f, 0.30f, 0.85f, 1f),
                OperatorClass.Medic => new Color(0.80f, 0.78f, 0.30f, 1f),
                OperatorClass.Specialist => new Color(0.20f, 0.75f, 0.70f, 1f),
                _ => new Color(0.5f, 0.7f, 0.9f, 1f)
            };
        }

        private static Color GetOperatorRarityColor(int stars)
        {
            return stars switch
            {
                1 => new Color(0.35f, 0.85f, 0.45f, 1f),
                2 => new Color(0.35f, 0.65f, 1f, 1f),
                3 => new Color(1f, 0.82f, 0.25f, 1f),
                4 => new Color(0.75f, 0.45f, 1f, 1f),
                _ => new Color(1f, 0.3f, 0.3f, 1f)
            };
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
            if (topBarBgSprites != null && topBarBgSprites.Length > 0)
            {
                topBarBgAnimImage = stageInfoPanel.GetComponent<Image>();
                topBarBgAnimImage.sprite = topBarBgSprites[0];
                topBarBgAnimImage.type = Image.Type.Simple;
                topBarBgAnimImage.color = Color.white;
            }
            topBarRect = barRect;

            // Pause button (top-left)
            var pauseButton = CreateButton(stageInfoPanel.transform, "PauseButton", string.Empty, new Vector2(50f, 50f), ButtonContainerType.IconDark);
            SetPosition(pauseButton.GetComponent<RectTransform>(), new Vector2(10f, -41f), new Vector2(0f, 1f), new Vector2(50f, 50f), new Vector2(0f, 0.5f));
            pauseButton.onClick.AddListener(PauseGame);
            StyleButton(pauseButton, ButtonContainerType.IconDark, Color.white, 1.08f);

            var pauseIcon = GetPauseIconSprite();
            if (pauseIcon != null)
            {
                CreateIconImage(pauseButton.transform, "Icon", pauseIcon, Color.white, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(26f, 26f), new Vector2(0.5f, 0.5f));
            }
            else
            {
                var label = pauseButton.GetComponentInChildren<Text>();
                if (label != null) label.text = "||";
            }

            phaseHeaderText = CreateText(stageInfoPanel.transform, "PhaseHeaderText", "PHASE", 18, TextAnchor.MiddleLeft);
            SetPosition(phaseHeaderText.GetComponent<RectTransform>(), new Vector2(95f, -28f), new Vector2(0f, 1f), new Vector2(220f, 32f), new Vector2(0f, 0.5f));

            // Phase text remains directly below the wave title.
            phaseText = CreateText(stageInfoPanel.transform, "PhaseText", "PREPARATION", 12, TextAnchor.MiddleLeft);
            phaseText.color = PrepColor;
            SetPosition(phaseText.GetComponent<RectTransform>(), new Vector2(95f, -60f), new Vector2(0f, 1f), new Vector2(220f, 26f), new Vector2(0f, 0.5f));

            // The wave counter sits on the Top Panel BG's second screen (x 336..510, y -15..-75 at 3x),
            // with the wave progress bar under it.
            waveIconImage = CreateIconImage(stageInfoPanel.transform, "WaveIcon", GetWaveMarkSprite(), WaveColor, new Vector2(348f, -32f), new Vector2(0f, 1f), new Vector2(30f, 29f), new Vector2(0f, 0.5f));
            waveText = CreateText(stageInfoPanel.transform, "WaveText", "1/1", 22, TextAnchor.MiddleLeft);
            SetPosition(waveText.GetComponent<RectTransform>(), new Vector2(384f, -32f), new Vector2(0f, 1f), new Vector2(120f, 32f), new Vector2(0f, 0.5f));
            CreateWaveProgressBar(stageInfoPanel.transform);

            // Enemy count (center) uses a skull icon and a numeric count.
            enemyIconImage = CreateIconImage(stageInfoPanel.transform, "EnemyIcon", GetEnemySkullSprite(), Color.white, new Vector2(-35f, -45f), new Vector2(0.5f, 1f), new Vector2(36f, 36f), new Vector2(0.5f, 0.5f));
            enemyText = CreateText(stageInfoPanel.transform, "EnemyText", "0", 24, TextAnchor.MiddleLeft);
            SetPosition(enemyText.GetComponent<RectTransform>(), new Vector2(8f, -45f), new Vector2(0.5f, 1f), new Vector2(100f, 40f), new Vector2(0f, 0.5f));

            // Lives: a horizontal tube on the right-hand screen. The text only shows "LIVES LOCKED" in the sandbox.
            CreateLivesTube(stageInfoPanel.transform);
            lpText = CreateText(stageInfoPanel.transform, "LivesText", string.Empty, 24, TextAnchor.MiddleRight);
            lpText.color = LifeColor;
            SetPosition(lpText.GetComponent<RectTransform>(), new Vector2(-25f, -45f), new Vector2(1f, 1f), new Vector2(280f, 45f), new Vector2(1f, 0.5f));

            // Texts and progress strip should never eat clicks
            phaseHeaderText.raycastTarget = false;
            waveText.raycastTarget = false;
            phaseText.raycastTarget = false;
            enemyText.raycastTarget = false;
            lpText.raycastTarget = false;
        }

        // UI/bars sprites are drawn at 2x. bar_frame is 24x10 with its inner channel at x 2..21, y 2..7;
        // tube_frame is 22x64 (8 px end caps) with its inner channel at x 3..16, y 8..56 from the bottom.
        private const float BarPixelScale = 2f;
        private const float TubeInnerX = 3f;
        private const float TubeInnerBottom = 8f;
        private const float TubeInnerWidth = 14f;
        private const float TubeInnerHeight = 49f;

        private void CreateWaveProgressBar(Transform parent)
        {
            // Framed bar under the wave counter; the fill sprite follows the phase (see SetWaveProgressFillSprite)
            var frame = new GameObject("WaveProgressBar", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(parent, false);
            SetPosition(frame.GetComponent<RectTransform>(), new Vector2(346f, -60f), new Vector2(0f, 1f), new Vector2(154f, 10f * BarPixelScale), new Vector2(0f, 0.5f));
            Image frameImage = frame.GetComponent<Image>();
            frameImage.raycastTarget = false;
            Sprite frameSprite = LoadWholeTextureSprite("UI/bars/bar_frame", new Vector4(4f, 4f, 4f, 4f));
            if (frameSprite != null)
            {
                frameImage.sprite = frameSprite;
                frameImage.type = Image.Type.Sliced;
                frameImage.pixelsPerUnitMultiplier = 1f / BarPixelScale;
            }
            else
            {
                frameImage.color = new Color(1f, 1f, 1f, 0.07f);
            }

            var fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(frame.transform, false);
            var fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = new Vector2(2f, 2f) * BarPixelScale;
            fillAreaRect.offsetMax = new Vector2(-2f, -2f) * BarPixelScale;

            var fill = new GameObject("WaveProgressFill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            waveProgressFillRect = fill.GetComponent<RectTransform>();
            waveProgressFillRect.anchorMin = Vector2.zero;
            waveProgressFillRect.anchorMax = new Vector2(0f, 1f);
            waveProgressFillRect.offsetMin = Vector2.zero;
            waveProgressFillRect.offsetMax = Vector2.zero;
            waveProgressFill = fill.GetComponent<Image>();
            waveProgressFill.raycastTarget = false;
            waveProgressFill.type = Image.Type.Tiled;
            waveProgressFill.pixelsPerUnitMultiplier = 1f / BarPixelScale;
            SetWaveProgressFillSprite(StagePhase.Preparation);
        }

        private void SetWaveProgressFillSprite(StagePhase phase)
        {
            if (waveProgressFill == null) return;

            string fillName = phase switch
            {
                StagePhase.CardPick => "bar_fill_yellow",
                StagePhase.WaveActive => "bar_fill_red",
                _ => "bar_fill_blue"
            };

            Sprite sprite = LoadWholeTextureSprite("UI/bars/" + fillName);
            if (sprite != null)
            {
                waveProgressFill.sprite = sprite;
                waveProgressFill.color = Color.white;
            }
            else
            {
                waveProgressFill.sprite = null;
                waveProgressFill.color = phase switch
                {
                    StagePhase.CardPick => DraftColor,
                    StagePhase.WaveActive => WaveColor,
                    _ => PrepColor
                };
            }
        }

        private void CreateLivesTube(Transform parent)
        {
            // tube_frame is vertical; the root is built upright and turned -90 degrees, so the liquid
            // fills from the left (the tube's bottom) and drains toward it as lives are lost.
            var root = new GameObject("LivesTube", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            livesTubeRect = root.GetComponent<RectTransform>();
            // Centred on the Top Panel BG's right-hand screen (frame x 590..637 at 3x)
            SetPosition(livesTubeRect, new Vector2(-78f, -45f), new Vector2(1f, 1f), new Vector2(22f, 64f) * BarPixelScale, new Vector2(0.5f, 0.5f));
            livesTubeRect.localRotation = Quaternion.Euler(0f, 0f, -90f);

            livesTubeFrame = root.GetComponent<Image>();
            livesTubeFrame.raycastTarget = false;
            Sprite frameSprite = LoadWholeTextureSprite("UI/bars/tube_frame");
            if (frameSprite != null) livesTubeFrame.sprite = frameSprite;
            else livesTubeFrame.color = new Color(0.1f, 0.12f, 0.15f, 1f);

            var liquid = new GameObject("Liquid", typeof(RectTransform), typeof(Image));
            liquid.transform.SetParent(root.transform, false);
            livesLiquidRect = liquid.GetComponent<RectTransform>();
            SetPosition(livesLiquidRect, new Vector2(TubeInnerX, TubeInnerBottom) * BarPixelScale, Vector2.zero,
                new Vector2(TubeInnerWidth, TubeInnerHeight) * BarPixelScale, Vector2.zero);
            Image liquidImage = liquid.GetComponent<Image>();
            liquidImage.raycastTarget = false;
            Sprite liquidSprite = LoadWholeTextureSprite("UI/bars/tube_liquid");
            if (liquidSprite != null)
            {
                liquidImage.sprite = liquidSprite;
                liquidImage.type = Image.Type.Tiled;
                liquidImage.pixelsPerUnitMultiplier = 1f / BarPixelScale;
            }
            else
            {
                liquidImage.color = LifeColor;
            }

            // Bright meniscus on the liquid's leading edge
            livesSurface = new GameObject("Surface", typeof(RectTransform), typeof(Image));
            livesSurface.transform.SetParent(liquid.transform, false);
            var surfaceRect = livesSurface.GetComponent<RectTransform>();
            surfaceRect.anchorMin = new Vector2(0f, 1f);
            surfaceRect.anchorMax = new Vector2(1f, 1f);
            surfaceRect.pivot = new Vector2(0.5f, 1f);
            surfaceRect.sizeDelta = new Vector2(0f, 3f * BarPixelScale);
            surfaceRect.anchoredPosition = Vector2.zero;
            Image surfaceImage = livesSurface.GetComponent<Image>();
            surfaceImage.raycastTarget = false;
            Sprite surfaceSprite = LoadWholeTextureSprite("UI/bars/tube_surface");
            if (surfaceSprite != null)
            {
                surfaceImage.sprite = surfaceSprite;
            }
            else
            {
                Destroy(livesSurface);
                livesSurface = null;
            }
        }

        private void ApplyLivesLiquid(float ratio)
        {
            if (livesLiquidRect == null) return;

            float height = Mathf.Round(Mathf.Clamp01(ratio) * TubeInnerHeight) * BarPixelScale;
            livesLiquidRect.sizeDelta = new Vector2(TubeInnerWidth * BarPixelScale, height);
            livesLiquidRect.gameObject.SetActive(height > 0f);
            if (livesSurface != null) livesSurface.SetActive(height >= 3f * BarPixelScale);
        }

        private void CreateDeckBar(Transform root)
        {
            const float bgHeight = 170f;
            const float bgAspect = 378f / 58f;
            const float bgScale = bgHeight / 58f;

            // Bottom bar background
            var bar = new GameObject("DeckBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(root, false);
            var barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.sizeDelta = new Vector2(0f, bgHeight);
            barRect.anchoredPosition = Vector2.zero;
            Image barImage = bar.GetComponent<Image>();
            barImage.color = Color.clear;
            barImage.raycastTarget = false;
            deckBarRect = barRect;

            // Deck background sprite-sheet animation — aspect-correct, anchored to
            // the bottom-left corner of the deck bar. Width is derived from the first
            // frame's aspect ratio so the sprite is never distorted.
            if (deckBgSprites != null && deckBgSprites.Length > 0)
            {
                var deckBgObj = new GameObject("DeckBgAnim", typeof(RectTransform), typeof(Image));
                deckBgObj.transform.SetParent(bar.transform, false);
                var deckBgRect = deckBgObj.GetComponent<RectTransform>();
                deckBgRect.anchorMin = new Vector2(0f, 0f);
                deckBgRect.anchorMax = new Vector2(0f, 0f);
                deckBgRect.pivot   = new Vector2(0f, 0f);
                Sprite firstFrame = deckBgSprites[0];
                float aspect = firstFrame != null && firstFrame.rect.height > 0f
                    ? firstFrame.rect.width / firstFrame.rect.height
                    : bgAspect;
                deckBgRect.sizeDelta = new Vector2(bgHeight * aspect, bgHeight);
                deckBgRect.anchoredPosition = Vector2.zero;
                deckBgAnimImage = deckBgObj.GetComponent<Image>();
                deckBgAnimImage.sprite = firstFrame;
                deckBgAnimImage.color = Color.white;
                deckBgAnimImage.preserveAspect = false;
                deckBgAnimImage.raycastTarget = false;
                deckBgAnimImage.type = Image.Type.Simple;
            }

            // Deck slots container matching the DeckBgAnim sprite dimensions
            var slotsContainer = new GameObject("DeckSlots", typeof(RectTransform));
            slotsContainer.transform.SetParent(bar.transform, false);
            var slotsRect = slotsContainer.GetComponent<RectTransform>();
            slotsRect.anchorMin = new Vector2(0f, 0f);
            slotsRect.anchorMax = new Vector2(0f, 0f);
            slotsRect.pivot = new Vector2(0f, 0f);
            slotsRect.sizeDelta = new Vector2(bgHeight * bgAspect, bgHeight);
            slotsRect.anchoredPosition = Vector2.zero;

            deckButtons = new Button[DeckSlotCount];
            deckButtonLabels = new Text[DeckSlotCount];
            deckButtonImages = new Image[DeckSlotCount];
            deckPortraitImages = new Image[DeckSlotCount];
            deckClassBadgeImages = new Image[DeckSlotCount];
            deckClassIconImages = new Image[DeckSlotCount];
            deckClassBadges = new GameObject[DeckSlotCount];
            deckCooldownBadges = new GameObject[DeckSlotCount];
            deckCooldownLabels = new Text[DeckSlotCount];
            deckHeatBadges = new GameObject[DeckSlotCount];
            deckHeatLabels = new Text[DeckSlotCount];
            deckUpgradeBadges = new GameObject[DeckSlotCount];
            deckUpgradeBadgeLabels = new UpgradeArrowGraphic[DeckSlotCount];
            deckFeedbacks = new MenuButtonFeedback[DeckSlotCount];

            // In the 378x58 sprite, each bay's dark interior spans 35px between pillars (x = 8..42 for bay 1,
            // pitch 42px, so bays are centered at x = 25.5 + 42i) and 40px from the caution tape to the
            // bottom ledge (y = 7..46 from the top). The slot covers exactly that interior.
            const float bayLeftPx = 8f;
            const float bayWidthPx = 35f;
            const float bayPitchPx = 42f;
            const float bayTopPx = 7f;
            const float bayHeightPx = 40f;
            float slotWidth = bayWidthPx * bgScale;
            float slotHeight = bayHeightPx * bgScale;
            float slotCenterY = (58f - bayTopPx - bayHeightPx * 0.5f) * bgScale;

            for (int i = 0; i < DeckSlotCount; i++)
            {
                int slotIndex = i;
                float slotCenterX = (bayLeftPx + bayWidthPx * 0.5f + i * bayPitchPx) * bgScale;
                var slotBtn = CreateDeckSlot(slotsContainer.transform, $"DeckSlot_{i + 1}");
                var btnRect = slotBtn.GetComponent<RectTransform>();
                btnRect.anchorMin = new Vector2(0f, 0f);
                btnRect.anchorMax = new Vector2(0f, 0f);
                btnRect.pivot = new Vector2(0.5f, 0.5f);
                btnRect.sizeDelta = new Vector2(slotWidth, slotHeight);
                btnRect.anchoredPosition = new Vector2(slotCenterX, slotCenterY);

                deckButtons[i] = slotBtn;
                deckButtonLabels[i] = slotBtn.GetComponentInChildren<Text>();
                deckButtonImages[i] = slotBtn.GetComponent<Image>();
                deckPortraitImages[i] = slotBtn.transform.Find("OperatorPortrait").GetComponent<Image>();
                deckClassBadges[i] = CreateClassBadge(slotBtn.transform, out deckClassBadgeImages[i], out deckClassIconImages[i]);
                deckCooldownBadges[i] = CreateCooldownBadge(
                    slotBtn.transform,
                    out _,
                    out deckCooldownLabels[i]);
                deckHeatBadges[i] = CreateDeckHeatBadge(
                    slotBtn.transform,
                    out deckHeatLabels[i]);
                deckUpgradeBadges[i] = CreateUpgradeBadge(slotBtn.transform, $"DeckUpgradeBadge_{i + 1}", new Vector2(32f, 18f), new Vector2(-4f, -4f));
                deckUpgradeBadgeLabels[i] = deckUpgradeBadges[i].GetComponentInChildren<UpgradeArrowGraphic>();
                slotBtn.onClick.AddListener(() => SelectDeckSlot(slotIndex));
                var dragHandler = slotBtn.gameObject.AddComponent<DeckSlotDragHandler>();
                dragHandler.Bind(
                    slotIndex,
                    () => CanDragDeckCard(slotIndex),
                    HandleDeckCardDrag,
                    FinishDeckCardDrag);

                // No press-shrink on deck slots so it doesn't fight with dragging.
                deckFeedbacks[i] = AttachFeedback(slotBtn, Color.white, 1.04f);
                deckFeedbacks[i].pressScale = 1f;

                slotBtn.interactable = false;
            }

            CreateTrashDropZone(bar.transform);
            CreatePreviewPathButton(bar.transform);
            CreateBottomRightPanel(bar.transform, bgScale);
        }

        // Bottom Right Panel BG is 140x113 px per frame: a gauge housing on top (x = 47..139, y = 0..55 from
        // the top) over a full-width base the same 57-58px height as the Deck BG, so it uses the deck's scale.
        // It is parented to the deck bar so it slides in with it. Screens on the sprite (px, from the top-left):
        //   speed screen  x = 51..138, y = 26..51  -> 1x / 2x / 3x buttons
        //   left screen   x = 20..75,  y = 70..93  -> squad count
        //   hazard screen x = 80..136, y = 64..101 -> Start Wave button
        private const float BottomRightPanelWidthPx = 140f;
        private const float BottomRightPanelHeightPx = 113f;

        private void CreateBottomRightPanel(Transform deckBar, float pxScale)
        {
            var panel = new GameObject("BottomRightPanel", typeof(RectTransform));
            panel.transform.SetParent(deckBar, false);
            bottomRightPanelRect = panel.GetComponent<RectTransform>();
            SetPosition(bottomRightPanelRect, Vector2.zero, new Vector2(1f, 0f),
                new Vector2(BottomRightPanelWidthPx * pxScale, BottomRightPanelHeightPx * pxScale), new Vector2(1f, 0f));

            if (HasAnyBottomRightSprites())
            {
                bottomRightBgFromImage = CreateBottomRightBgLayer(panel.transform, "BottomRightBgFrom");
                bottomRightBgToImage = CreateBottomRightBgLayer(panel.transform, "BottomRightBgTo");
            }
            else
            {
                // Sprite missing: fall back to the kit's dark panel so the controls stay readable.
                panel.AddComponent<Image>().raycastTarget = false;
                ApplyPanelContainer(panel, PanelContainerType.Dark);
            }

            // Converts a point on the sprite (px from its top-left) to a position anchored at the panel's bottom-right.
            Vector2 PanelPx(float x, float yFromTop) => new Vector2(
                (x - BottomRightPanelWidthPx) * pxScale,
                (BottomRightPanelHeightPx - yFromTop) * pxScale);

            // Start Wave button fills the hazard-striped screen
            startWaveButton = CreateButton(panel.transform, "StartWaveButton", "START\nWAVE", new Vector2(152f, 100f), ButtonContainerType.Primary);
            startWaveButtonText = startWaveButton.GetComponentInChildren<Text>();
            SetPosition(startWaveButton.GetComponent<RectTransform>(), PanelPx(108f, 82.5f), new Vector2(1f, 0f),
                new Vector2(152f, 100f), new Vector2(0.5f, 0.5f));
            ApplyButtonContainer(startWaveButton, ButtonContainerType.Primary);
            startWaveButton.onClick.AddListener(StartWave);
            startWaveFeedback = AttachFeedback(startWaveButton, AccentColor, 1.06f, () => PlaySfx(clickClip, 1.1f));

            squadCountText = CreateText(panel.transform, "SquadCountText", "SQUAD 0/8", 16, TextAnchor.MiddleCenter);
            SetPosition(squadCountText.GetComponent<RectTransform>(), PanelPx(47.5f, 81.5f), new Vector2(1f, 0f),
                new Vector2(150f, 60f), new Vector2(0.5f, 0.5f));
            squadCountText.raycastTarget = false;

            var buttonsRoot = new GameObject("SpeedButtons", typeof(RectTransform));
            buttonsRoot.transform.SetParent(panel.transform, false);
            var buttonsRect = buttonsRoot.GetComponent<RectTransform>();
            SetPosition(buttonsRect, PanelPx(94.5f, 38.5f), new Vector2(1f, 0f), new Vector2(240f, 48f), new Vector2(0.5f, 0.5f));
            var layout = buttonsRoot.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            speedButtons = new Button[3];
            speedFeedbacks = new MenuButtonFeedback[3];
            for (int i = 0; i < speedButtons.Length; i++)
            {
                float speed = i + 1;
                var button = CreateButton(buttonsRoot.transform, $"Speed{speed:0}xButton", $"{speed:0}x", new Vector2(72f, 44f), ButtonContainerType.Dark);
                int index = i;
                button.onClick.AddListener(() => SetGameSpeed(index + 1));
                speedButtons[i] = button;
                speedFeedbacks[i] = AttachFeedback(button, AccentColor, 1.03f, () =>
                {
                    SetGameSpeed(index + 1);
                    PlayClick();
                });
            }

            UpdateSpeedSelection(gameManager != null ? gameManager.GameSpeed : 1f);
        }

        private static Image CreateBottomRightBgLayer(Transform parent, string objectName)
        {
            var layer = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            layer.transform.SetParent(parent, false);
            Stretch(layer.GetComponent<RectTransform>());
            Image image = layer.GetComponent<Image>();
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
            return image;
        }

        // Sets the speed animation set (0 = 1x, 1 = 2x, 2 = 3x). The first call snaps; later calls crossfade
        // smoothly over 0.2s while continuing the 12 fps animation loop on unscaled time.
        private void SetBottomRightBgSpeed(int speedIndex)
        {
            if (bottomRightBgToImage == null || !HasAnyBottomRightSprites()) return;

            speedIndex = Mathf.Clamp(speedIndex, 0, 2);
            if (bottomRightBgToSpeed == speedIndex && bottomRightBgFade >= 1f && bottomRightBgAnimFrame >= 0) return;

            if (bottomRightBgAnimFrame < 0)
            {
                bottomRightBgFromSpeed = speedIndex;
                bottomRightBgToSpeed = speedIndex;
                bottomRightBgFade = 1f;
                bottomRightBgAnimFrame = 0;
                Sprite initialSprite = GetBottomRightSprite(speedIndex, 0);
                if (initialSprite != null)
                {
                    if (bottomRightBgFromImage != null)
                    {
                        bottomRightBgFromImage.sprite = initialSprite;
                        SetImageAlpha(bottomRightBgFromImage, 1f);
                    }
                    if (bottomRightBgToImage != null)
                    {
                        bottomRightBgToImage.sprite = initialSprite;
                        SetImageAlpha(bottomRightBgToImage, 1f);
                    }
                }
            }
            else
            {
                // Mid-fade, start from whichever speed is currently more visible.
                bottomRightBgFromSpeed = bottomRightBgFade >= 0.5f
                    ? bottomRightBgToSpeed
                    : bottomRightBgFromSpeed;
                bottomRightBgToSpeed = speedIndex;
                bottomRightBgFade = 0f;
                if (bottomRightBgToImage != null)
                {
                    SetImageAlpha(bottomRightBgToImage, 0f);
                }
            }
        }

        private Sprite GetBottomRightSprite(int speedIndex, int animIndex)
        {
            Sprite[] set = speedIndex switch
            {
                0 => bottomRightBg1xSprites,
                1 => bottomRightBg2xSprites,
                2 => bottomRightBg3xSprites,
                _ => null
            };

            if (set != null && set.Length > 0)
            {
                return set[animIndex % set.Length];
            }

            if (bottomRightBgSprites != null && bottomRightBgSprites.Length > 0)
            {
                int frame = Mathf.Clamp(speedIndex, 0, bottomRightBgSprites.Length - 1);
                return bottomRightBgSprites[frame];
            }

            return null;
        }

        private bool HasAnyBottomRightSprites()
        {
            return (bottomRightBg1xSprites != null && bottomRightBg1xSprites.Length > 0) ||
                   (bottomRightBg2xSprites != null && bottomRightBg2xSprites.Length > 0) ||
                   (bottomRightBg3xSprites != null && bottomRightBg3xSprites.Length > 0) ||
                   (bottomRightBgSprites != null && bottomRightBgSprites.Length > 0);
        }

        private static Sprite[] LoadAndSortSprites(string resourcePath)
        {
            Sprite[] sprites = Resources.LoadAll<Sprite>(resourcePath);
            if (sprites != null && sprites.Length > 1)
            {
                System.Array.Sort(sprites, (a, b) =>
                {
                    if (a == null && b == null) return 0;
                    if (a == null) return -1;
                    if (b == null) return 1;
                    return GetSpriteIndex(a).CompareTo(GetSpriteIndex(b));
                });
            }
            return sprites;
        }

        private static int GetSpriteIndex(Sprite s)
        {
            if (s == null) return 0;
            int idx = s.name.LastIndexOf('_');
            if (idx >= 0 && int.TryParse(s.name.Substring(idx + 1), out int num))
                return num;
            return 0;
        }

        private static void SetImageAlpha(Image image, float alpha)
        {
            Color c = image.color;
            c.a = alpha;
            image.color = c;
        }

        private void CreateTrashDropZone(Transform parent)
        {
            var zone = new GameObject("TrashDropZone", typeof(RectTransform), typeof(Image), typeof(Button));
            zone.transform.SetParent(parent, false);
            trashDropZone = zone.GetComponent<RectTransform>();
            // Aligned precisely to the discard monitor screen on the Deck BG sprite sheet
            // Sprite discard screen: x=345..373 (center ~359), y=10..45 from top (center ~30.5 from bottom)
            // At scale (170/58): Center X = 1052.2f, Center Y = 89.4f, inner screen ~84x102f
            SetPosition(trashDropZone, new Vector2(1052f, 89f), new Vector2(0f, 0f),
                new Vector2(80f, 98f), new Vector2(0.5f, 0.5f));

            var image = zone.GetComponent<Image>();
            image.color = new Color(0.10f, 0.05f, 0.07f, 0.25f);
            image.raycastTarget = true;

            var icon = new GameObject("TrashIcon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(zone.transform, false);
            var iconRect = icon.GetComponent<RectTransform>();
            // icon_discard is 16x16 pixel art: draw it at exactly 2x in its own colours, so it stays crisp
            SetPosition(iconRect, new Vector2(0f, 10f), new Vector2(0.5f, 0.5f),
                new Vector2(32f, 32f), new Vector2(0.5f, 0.5f));
            var iconImage = icon.GetComponent<Image>();
            iconImage.sprite = CreateTrashIconSprite();
            iconImage.color = Color.white;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            // Size 11 needed ~77 px for "DISCARD" and clipped the last letter; 10 fits the 80 px screen
            var discardLabel = CreateText(zone.transform, "DiscardLabel", "DISCARD", 10, TextAnchor.MiddleCenter);
            discardLabel.color = new Color(1f, 0.78f, 0.78f, 1f);
            discardLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            discardLabel.raycastTarget = false;
            SetPosition(discardLabel.GetComponent<RectTransform>(), new Vector2(0f, -24f), new Vector2(0.5f, 0.5f),
                new Vector2(80f, 18f), new Vector2(0.5f, 0.5f));

            var button = zone.GetComponent<Button>();
            button.interactable = false;
            AttachFeedback(button, new Color(1f, 0.3f, 0.35f, 1f), 1.04f);
        }

        private void CreatePathPreviewOverlay(Transform root)
        {
            pathPreviewOverlay = GameplayPathPreviewOverlay.Create(
                root,
                canvas,
                gridManager,
                stageBootstrapper != null ? stageBootstrapper.stageData : null);
        }

        private void CreatePreviewPathButton(Transform parent)
        {
            previewPathButton = CreateButton(parent, "PreviewPathButton", "PREVIEW\nPATH", new Vector2(110f, 52f), ButtonContainerType.Dark);
            SetPosition(
                previewPathButton.GetComponent<RectTransform>(),
                new Vector2(1125f, 89f),
                new Vector2(0f, 0f),
                new Vector2(110f, 52f),
                new Vector2(0f, 0.5f));

            previewPathButtonText = previewPathButton.GetComponentInChildren<Text>();
            if (previewPathButtonText != null)
            {
                previewPathButtonText.fontSize = 14;
                previewPathButtonText.lineSpacing = 0.9f;
                previewPathButtonText.color = TextDim;
            }

            previewPathFeedback = AttachFeedback(previewPathButton, AccentColor, 1.05f, () => PlaySfx(clickClip, 1.1f));
            previewPathButton.onClick.AddListener(TogglePathPreview);
        }

        /// <summary>
        /// Screen rect of the free strip on the deck bar between the PREVIEW PATH button (or the discard screen,
        /// if that button is missing) and the bottom-right panel, at the discard screen's height. The card draft
        /// puts its RESUME button here while minimized. The bottom-right panel is anchored to the right edge, so
        /// the strip's width depends on the aspect ratio.
        /// </summary>
        public bool TryGetDeckBarFreeSlot(out Rect screenRect)
        {
            screenRect = default;
            RectTransform left = previewPathButton != null ? (RectTransform)previewPathButton.transform : trashDropZone;
            if (left == null || trashDropZone == null || bottomRightPanelRect == null || canvas == null) return false;

            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Rect leftRect = GetScreenRect(left, cam);
            Rect discardRect = GetScreenRect(trashDropZone, cam);
            Rect panelRect = GetScreenRect(bottomRightPanelRect, cam);
            if (panelRect.xMin <= leftRect.xMax) return false;

            screenRect = Rect.MinMaxRect(leftRect.xMax, discardRect.yMin, panelRect.xMin, discardRect.yMax);
            return true;
        }

        private static Rect GetScreenRect(RectTransform rect, Camera cam)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void EnsurePathPreviewOverlay()
        {
            if (stageBootstrapper == null) stageBootstrapper = FindAnyObjectByType<StageBootstrapper>();
            if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();

            StageData currentStage = stageBootstrapper != null && stageBootstrapper.stageData != null
                ? stageBootstrapper.stageData
                : (gameManager != null ? gameManager.CurrentStage : null);

            if (pathPreviewOverlay != null)
            {
                if (currentStage != null)
                {
                    pathPreviewOverlay.SetStage(currentStage, gridManager);
                }
                return;
            }
            if (canvas == null) return;

            pathPreviewOverlay = GameplayPathPreviewOverlay.Create(canvas.transform, canvas, gridManager, currentStage);
            if (topBarRect != null)
            {
                int topBarIndex = topBarRect.GetSiblingIndex();
                pathPreviewOverlay.transform.SetSiblingIndex(Mathf.Max(0, topBarIndex));
            }
        }

        public void TogglePathPreview()
        {
            EnsurePathPreviewOverlay();
            if (pathPreviewOverlay == null) return;

            pathPreviewOverlay.Toggle();
            UpdatePathPreviewButtonVisuals(pathPreviewOverlay.IsVisible);
        }

        private void UpdatePathPreviewButtonVisuals(bool active)
        {
            if (previewPathButton == null) return;

            ApplyButtonContainer(previewPathButton, active ? ButtonContainerType.Primary : ButtonContainerType.Dark);
            if (previewPathFeedback != null)
            {
                previewPathFeedback.glowAlways = active;
                previewPathFeedback.glowColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 1f);
            }
            if (previewPathButtonText != null)
            {
                previewPathButtonText.color = active ? Color.white : TextDim;
            }
        }

        private void CreateDiscardConfirmationPanel(Transform root)
        {
            discardConfirmationPanel = new GameObject("DiscardConfirmationPanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            discardConfirmationPanel.transform.SetParent(root, false);
            Stretch(discardConfirmationPanel.GetComponent<RectTransform>());
            discardConfirmationPanel.GetComponent<Image>().color = new Color(0.01f, 0.012f, 0.02f, 0.78f);

            var card = new GameObject("DiscardCard", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(discardConfirmationPanel.transform, false);
            var cardRect = card.GetComponent<RectTransform>();
            SetPosition(cardRect, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(480f, 260f), new Vector2(0.5f, 0.5f));
            ApplyModalCard(card);
            var cardOutline = card.AddComponent<Outline>();
            cardOutline.effectColor = AccentColor;
            cardOutline.effectDistance = new Vector2(2f, -2f);

            var title = CreateText(card.transform, "Title", "DISCARD OPERATOR?", 24, TextAnchor.MiddleCenter);
            SetPosition(title.GetComponent<RectTransform>(), new Vector2(0f, 74f), new Vector2(0.5f, 0.5f),
                new Vector2(430f, 40f), new Vector2(0.5f, 0.5f));
            var message = CreateText(card.transform, "Message", string.Empty, 17, TextAnchor.MiddleCenter);
            message.color = TextDim;
            message.name = "Message";
            SetPosition(message.GetComponent<RectTransform>(), new Vector2(0f, 28f), new Vector2(0.5f, 0.5f),
                new Vector2(430f, 32f), new Vector2(0.5f, 0.5f));

            var confirm = CreateButton(card.transform, "ConfirmDiscardButton", "DISCARD", new Vector2(175f, 52f), ButtonContainerType.Danger);
            SetPosition(confirm.GetComponent<RectTransform>(), new Vector2(-100f, -60f), new Vector2(0.5f, 0.5f),
                new Vector2(175f, 52f), new Vector2(0.5f, 0.5f));
            StyleButton(confirm, ButtonContainerType.Danger, LifeColor, 1.05f,
                ConfirmDiscard);

            var cancel = CreateButton(card.transform, "CancelDiscardButton", "CANCEL", new Vector2(175f, 52f), ButtonContainerType.Dark);
            SetPosition(cancel.GetComponent<RectTransform>(), new Vector2(100f, -60f), new Vector2(0.5f, 0.5f),
                new Vector2(175f, 52f), new Vector2(0.5f, 0.5f));
            StyleButton(cancel, ButtonContainerType.Dark, TextDim, 1.05f, CancelDiscard);

            discardConfirmationPanel.SetActive(false);
        }

        private void ShowDiscardConfirmation(int slotIndex, DraftCard card)
        {
            if (discardConfirmationPanel == null || card == null || card.operatorData == null) return;

            pendingDiscardSlot = slotIndex;
            pendingDiscardCard = card;
            var message = discardConfirmationPanel.transform.Find("DiscardCard/Message")?.GetComponent<Text>();
            if (message != null)
                message.text = $"Discard {card.operatorData.operatorName} from your deck?";
            discardConfirmationPanel.SetActive(true);
        }

        private void ConfirmDiscard()
        {
            if (playerDeck != null && pendingDiscardSlot >= 0 &&
                playerDeck.GetCard(pendingDiscardSlot) == pendingDiscardCard)
            {
                playerDeck.RemoveCard(pendingDiscardSlot);
                AudioManager.Instance?.PlaySfx(SfxId.UiCardDiscard, sfxVolume);
            }

            CancelDiscard();
        }

        private void CancelDiscard()
        {
            pendingDiscardCard = null;
            pendingDiscardSlot = -1;
            if (discardConfirmationPanel != null)
                discardConfirmationPanel.SetActive(false);
        }

        private void SetGameSpeed(int speed)
        {
            if (gameManager == null) return;

            gameManager.SetGameSpeed(speed);
            UpdateSpeedSelection(speed);
        }

        private void UpdateSpeedSelection(float speed)
        {
            if (speedButtons == null) return;

            int selectedIndex = Mathf.Clamp(Mathf.RoundToInt(speed) - 1, 0, speedButtons.Length - 1);
            currentSpeedSelectionIndex = selectedIndex;
            SetBottomRightBgSpeed(selectedIndex);
            for (int i = 0; i < speedButtons.Length; i++)
            {
                bool selected = i == selectedIndex;
                ApplyButtonContainer(speedButtons[i], selected ? ButtonContainerType.Primary : ButtonContainerType.Dark);
                speedFeedbacks[i].restScale = selected ? 1.12f : 1f;
                speedFeedbacks[i].glowAlways = selected;
                speedFeedbacks[i].glowColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 1f);
                var label = speedButtons[i].GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.color = selected ? Color.white : TextDim;
                    label.fontSize = selected ? 21 : 18;
                }
            }
        }

        private Button CreateDeckSlot(Transform parent, string objectName)
        {
            // Size and position are set by the caller to match a bay of the Deck BG sprite. The slot image stays
            // transparent (it only catches clicks and carries the hover/selection glow outline), so the bay
            // art shows through behind the operator.
            var button = CreateButton(parent, objectName, "", new Vector2(102f, 117f));
            var image = button.GetComponent<Image>();
            image.sprite = null;
            image.color = Color.clear;
            image.type = Image.Type.Simple;

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.fontSize = 11;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 8;
                label.resizeTextMaxSize = 11;
                label.color = Color.white;

                // Name + stars sit on the dark strip under the green bracket frame (bay y = 36..46 of 7..46).
                RectTransform labelRect = label.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0.03f, 0.01f);
                labelRect.anchorMax = new Vector2(0.97f, 0.27f);
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
            }

            // Portrait sits inside the bay's green bracket frame (bay y = 8..34 of 7..46), with a small inset.
            var portraitObject = new GameObject("OperatorPortrait", typeof(RectTransform), typeof(Image));
            portraitObject.transform.SetParent(button.transform, false);
            RectTransform portraitRect = portraitObject.GetComponent<RectTransform>();
            portraitRect.anchorMin = new Vector2(0.1f, 0.33f);
            portraitRect.anchorMax = new Vector2(0.9f, 0.94f);
            portraitRect.offsetMin = Vector2.zero;
            portraitRect.offsetMax = Vector2.zero;

            Image portraitImage = portraitObject.GetComponent<Image>();
            portraitImage.preserveAspect = true;
            portraitImage.raycastTarget = false;

            if (label != null)
            {
                label.transform.SetAsLastSibling();
            }

            return button;
        }

        private GameObject CreateClassBadge(Transform parent, out Image backgroundImage, out Image iconImage)
        {
            var badge = new GameObject("ClassBadge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(parent, false);
            RectTransform badgeRect = badge.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(1f, 0.36f);
            badgeRect.anchorMax = new Vector2(1f, 0.36f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            badgeRect.sizeDelta = new Vector2(26f, 26f);
            badgeRect.anchoredPosition = new Vector2(-6f, 0f);

            backgroundImage = badge.GetComponent<Image>();
            backgroundImage.raycastTarget = false;
            backgroundImage.color = Color.clear;

            var iconObject = new GameObject("ClassIcon", typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(badge.transform, false);
            iconImage = iconObject.GetComponent<Image>();
            iconImage.color = Color.white;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            RectTransform iconRect = iconImage.rectTransform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;

            badge.SetActive(false);
            return badge;
        }

        private static bool classIconsLoaded = false;

        public static void ReloadClassIcons()
        {
            classIconsLoaded = false;
            ClassIconSprites.Clear();
            EnsureClassIconsLoaded();
        }

        private static void EnsureClassIconsLoaded()
        {
            if (classIconsLoaded && ClassIconSprites.Count >= 6) return;

            Sprite[] sprites = null;

#if UNITY_EDITOR
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/class-icons.png");
            if (assets != null && assets.Length > 0)
            {
                var list = new List<Sprite>();
                for (int i = 0; i < assets.Length; i++)
                {
                    if (assets[i] is Sprite s) list.Add(s);
                }
                sprites = list.ToArray();
            }
#endif

            if (sprites == null || sprites.Length == 0)
            {
                sprites = Resources.LoadAll<Sprite>("UI/icons/class-icons");
            }

            if (sprites != null && sprites.Length > 0)
            {
                for (int i = 0; i < sprites.Length; i++)
                {
                    Sprite s = sprites[i];
                    if (s == null) continue;
                    switch (s.name)
                    {
                        case "class-icons_0": ClassIconSprites[OperatorClass.Sniper] = s; break;
                        case "class-icons_1": ClassIconSprites[OperatorClass.Medic] = s; break;
                        case "class-icons_2": ClassIconSprites[OperatorClass.Caster] = s; break;
                        case "class-icons_3": ClassIconSprites[OperatorClass.Defender] = s; break;
                        case "class-icons_4": ClassIconSprites[OperatorClass.Guard] = s; break;
                        case "class-icons_5": ClassIconSprites[OperatorClass.Specialist] = s; break;
                    }
                }
            }

            // Fallback: If sub-sprites are not found or sliced, load Texture2D and slice them manually
            // (0 - sniper, 1 - medic, 2 - caster, 3 - defender, 4 - guard, 5 - specialist)
            if (ClassIconSprites.Count < 6)
            {
                Texture2D tex = null;
#if UNITY_EDITOR
                tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/class-icons.png");
#endif
                if (tex == null)
                {
                    tex = Resources.Load<Texture2D>("UI/icons/class-icons");
                }
                if (tex == null)
                {
                    string diskPath = System.IO.Path.Combine(Application.dataPath, "_Project/Art/class-icons.png");
                    if (System.IO.File.Exists(diskPath))
                    {
                        byte[] raw = System.IO.File.ReadAllBytes(diskPath);
                        tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        tex.LoadImage(raw);
                    }
                }

                if (tex != null)
                {
                    tex.filterMode = FilterMode.Point;
                    if (!ClassIconSprites.ContainsKey(OperatorClass.Sniper))
                        ClassIconSprites[OperatorClass.Sniper] = Sprite.Create(tex, new Rect(0f, 32f, 32f, 32f), new Vector2(0.5f, 0.5f), 32f);
                    if (!ClassIconSprites.ContainsKey(OperatorClass.Medic))
                        ClassIconSprites[OperatorClass.Medic] = Sprite.Create(tex, new Rect(32f, 32f, 32f, 32f), new Vector2(0.5f, 0.5f), 32f);
                    if (!ClassIconSprites.ContainsKey(OperatorClass.Caster))
                        ClassIconSprites[OperatorClass.Caster] = Sprite.Create(tex, new Rect(64f, 32f, 32f, 32f), new Vector2(0.5f, 0.5f), 32f);
                    if (!ClassIconSprites.ContainsKey(OperatorClass.Defender))
                        ClassIconSprites[OperatorClass.Defender] = Sprite.Create(tex, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f), 32f);
                    if (!ClassIconSprites.ContainsKey(OperatorClass.Guard))
                        ClassIconSprites[OperatorClass.Guard] = Sprite.Create(tex, new Rect(32f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f), 32f);
                    if (!ClassIconSprites.ContainsKey(OperatorClass.Specialist))
                        ClassIconSprites[OperatorClass.Specialist] = Sprite.Create(tex, new Rect(64f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f), 32f);
                }
            }

            classIconsLoaded = true;
        }

        public static Sprite GetClassIconSprite(OperatorClass opClass)
        {
            EnsureClassIconsLoaded();
            if (ClassIconSprites.TryGetValue(opClass, out Sprite sprite) && sprite != null)
            {
                return sprite;
            }
            return null;
        }

        private GameObject CreateCooldownBadge(Transform parent, out Image clockImage, out Text roundsText)
        {
            var badge = new GameObject("CooldownBadge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(parent, false);
            RectTransform badgeRect = badge.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0f, 1f);
            badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0f, 1f);
            badgeRect.sizeDelta = new Vector2(48f, 28f);
            badgeRect.anchoredPosition = new Vector2(6f, -6f);

            Image background = badge.GetComponent<Image>();
            background.color = new Color(0.16f, 0.045f, 0.025f, 0.96f);
            background.raycastTarget = false;

            Outline outline = badge.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.58f, 0.18f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;

            var clockObject = new GameObject("ClockIcon", typeof(RectTransform), typeof(Image));
            clockObject.transform.SetParent(badge.transform, false);
            RectTransform clockRect = clockObject.GetComponent<RectTransform>();
            clockRect.anchorMin = new Vector2(0f, 0.5f);
            clockRect.anchorMax = new Vector2(0f, 0.5f);
            clockRect.pivot = new Vector2(0f, 0.5f);
            clockRect.sizeDelta = new Vector2(18f, 18f);
            clockRect.anchoredPosition = new Vector2(4f, 0f);

            clockImage = clockObject.GetComponent<Image>();
            clockImage.sprite = CreateCooldownClockSprite();
            clockImage.color = new Color(1f, 0.72f, 0.35f, 1f);
            clockImage.preserveAspect = true;
            clockImage.raycastTarget = false;

            roundsText = CreateText(badge.transform, "Rounds", "1", 14, TextAnchor.MiddleCenter);
            RectTransform roundsRect = roundsText.GetComponent<RectTransform>();
            roundsRect.anchorMin = new Vector2(0.43f, 0f);
            roundsRect.anchorMax = new Vector2(1f, 1f);
            roundsRect.offsetMin = Vector2.zero;
            roundsRect.offsetMax = new Vector2(-2f, 0f);
            roundsText.color = Color.white;
            roundsText.raycastTarget = false;

            badge.SetActive(false);
            return badge;
        }

        private GameObject CreateDeckHeatBadge(Transform parent, out Text heatText)
        {
            var badge = new GameObject("DeckHeatBadge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(parent, false);
            RectTransform badgeRect = badge.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(1f, 0f);
            badgeRect.anchorMax = new Vector2(1f, 0f);
            badgeRect.pivot = new Vector2(1f, 0f);
            badgeRect.sizeDelta = new Vector2(38f, 22f);
            badgeRect.anchoredPosition = new Vector2(-4f, 4f);

            Image background = badge.GetComponent<Image>();
            background.color = new Color(0.24f, 0.05f, 0.02f, 0.96f);
            background.raycastTarget = false;

            Outline outline = badge.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.35f, 0.08f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;

            heatText = CreateText(badge.transform, "HeatStacks", "🔥0", 12, TextAnchor.MiddleCenter);
            RectTransform textRect = heatText.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            heatText.color = new Color(1f, 0.82f, 0.35f, 1f);
            heatText.fontStyle = FontStyle.Bold;
            heatText.raycastTarget = false;

            badge.SetActive(false);
            return badge;
        }

        private static Sprite CreateCooldownClockSprite()
        {
            if (cooldownClockSprite != null) return cooldownClockSprite;

            cooldownClockSprite = LoadIconSprite("icon_clock");
            if (cooldownClockSprite != null) return cooldownClockSprite;

            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            void Pixel(int x, int y)
            {
                if (x >= 0 && x < size && y >= 0 && y < size)
                {
                    pixels[y * size + x] = Color.white;
                }
            }

            for (int y = 1; y < size - 1; y++)
            {
                for (int x = 1; x < size - 1; x++)
                {
                    float dx = x - 7.5f;
                    float dy = y - 7.5f;
                    float distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared >= 31f && distanceSquared <= 49f)
                    {
                        Pixel(x, y);
                    }
                }
            }

            Pixel(7, 7);
            Pixel(7, 8);
            Pixel(7, 9);
            Pixel(8, 7);
            Pixel(9, 7);

            texture.SetPixels(pixels);
            texture.Apply();
            cooldownClockSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            return cooldownClockSprite;
        }

        private void CreateFramiliarCopySection(Transform parent)
        {
            framiliarCopyMenuRoot = new GameObject("FramiliarCopySection", typeof(RectTransform));
            framiliarCopyMenuRoot.transform.SetParent(parent, false);

            RectTransform menuRect = framiliarCopyMenuRoot.GetComponent<RectTransform>();
            menuRect.anchorMin = new Vector2(0f, 0f);
            menuRect.anchorMax = new Vector2(1f, 1f);
            menuRect.pivot = new Vector2(0.5f, 1f);
            menuRect.offsetMin = Vector2.zero;
            menuRect.offsetMax = Vector2.zero;

            // Subtitle / Header
            framiliarSubtitleText = CreateText(framiliarCopyMenuRoot.transform, "Subtitle", string.Empty, 10, TextAnchor.MiddleCenter);
            framiliarSubtitleText.fontStyle = FontStyle.Bold;
            framiliarSubtitleText.color = new Color(0.25f, 0.95f, 0.85f, 1f);
            SetPosition(framiliarSubtitleText.rectTransform, new Vector2(0f, -102f), new Vector2(0.5f, 1f), new Vector2(264f, 16f), new Vector2(0.5f, 1f));
            framiliarSubtitleText.raycastTarget = false;

            // Empty state message
            framiliarEmptyMessageText = CreateText(framiliarCopyMenuRoot.transform, "EmptyMessage", string.Empty, 11, TextAnchor.MiddleCenter);
            framiliarEmptyMessageText.color = new Color(0.85f, 0.85f, 0.85f, 0.90f);
            SetPosition(framiliarEmptyMessageText.rectTransform, new Vector2(0f, -122f), new Vector2(0.5f, 1f), new Vector2(264f, 20f), new Vector2(0.5f, 1f));
            framiliarEmptyMessageText.raycastTarget = false;
            framiliarEmptyMessageText.gameObject.SetActive(false);

            // Up to 8 copy buttons (arranged dynamically in up to 4 columns)
            framiliarCopyButtons = new Button[DeckSlotCount];
            framiliarCopyLabels = new Text[DeckSlotCount];
            framiliarCopyPortraits = new Image[DeckSlotCount];
            framiliarCopyClassBadges = new GameObject[DeckSlotCount];
            framiliarCopyClassBadgeImages = new Image[DeckSlotCount];
            framiliarCopyClassIconImages = new Image[DeckSlotCount];
            framiliarCopyFeedbacks = new MenuButtonFeedback[DeckSlotCount];

            Vector2 btnSize = new Vector2(54f, 60f);

            for (int i = 0; i < DeckSlotCount; i++)
            {
                int slotIndex = i;
                var slotBtn = CreateButton(framiliarCopyMenuRoot.transform, $"CopySlot_{i + 1}", string.Empty, btnSize, ButtonContainerType.Dark);
                RectTransform btnRect = slotBtn.GetComponent<RectTransform>();
                btnRect.anchorMin = new Vector2(0.5f, 1f);
                btnRect.anchorMax = new Vector2(0.5f, 1f);
                btnRect.pivot = new Vector2(0.5f, 0.5f);
                btnRect.sizeDelta = btnSize;
                btnRect.anchoredPosition = Vector2.zero;

                // Portrait Image
                var portraitGo = new GameObject("OperatorPortrait", typeof(RectTransform), typeof(Image));
                portraitGo.transform.SetParent(slotBtn.transform, false);
                RectTransform portRect = portraitGo.GetComponent<RectTransform>();
                portRect.anchorMin = new Vector2(0.5f, 0.5f);
                portRect.anchorMax = new Vector2(0.5f, 0.5f);
                portRect.pivot = new Vector2(0.5f, 0.5f);
                portRect.sizeDelta = new Vector2(38f, 38f);
                portRect.anchoredPosition = new Vector2(0f, 6f);
                var portImg = portraitGo.GetComponent<Image>();
                portImg.preserveAspect = true;
                portImg.raycastTarget = false;
                framiliarCopyPortraits[i] = portImg;

                // Class Badge (top-left)
                framiliarCopyClassBadges[i] = CreateClassBadge(slotBtn.transform, out framiliarCopyClassBadgeImages[i], out framiliarCopyClassIconImages[i]);
                RectTransform badgeRect = framiliarCopyClassBadges[i].GetComponent<RectTransform>();
                badgeRect.anchorMin = new Vector2(0f, 1f);
                badgeRect.anchorMax = new Vector2(0f, 1f);
                badgeRect.pivot = new Vector2(0f, 1f);
                badgeRect.sizeDelta = new Vector2(14f, 14f);
                badgeRect.anchoredPosition = new Vector2(2f, -2f);

                // Label (Stars only)
                var label = slotBtn.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.fontSize = 11;
                    label.alignment = TextAnchor.MiddleCenter;
                    label.raycastTarget = false;
                    label.color = new Color(1f, 0.78f, 0.2f, 1f);
                    RectTransform lblRect = label.GetComponent<RectTransform>();
                    lblRect.anchorMin = new Vector2(0f, 0f);
                    lblRect.anchorMax = new Vector2(1f, 0f);
                    lblRect.pivot = new Vector2(0.5f, 0f);
                    lblRect.sizeDelta = new Vector2(0f, 16f);
                    lblRect.anchoredPosition = new Vector2(0f, 2f);
                    framiliarCopyLabels[i] = label;
                }

                framiliarCopyButtons[i] = slotBtn;
                framiliarCopyFeedbacks[i] = AttachFeedback(slotBtn, Color.white, 1.06f);
                slotBtn.onClick.AddListener(() => SelectFramiliarCopySlot(slotIndex));
                slotBtn.gameObject.SetActive(false);
            }

            framiliarCopyMenuRoot.SetActive(false);
        }

        public void ShowFramiliarCopyMenu(TrashTD.Operators.FramiliarOperator framiliar)
        {
            if (framiliar == null || framiliarCopyMenuRoot == null || selectedOperatorLabelRoot == null) return;
            activeFramiliarOperator = framiliar;

            GridCell cell = framiliar.DeployedCell;
            if (cell == null && operatorManager != null && operatorManager.GridManager != null)
            {
                cell = operatorManager.GridManager.GetCell(operatorManager.GridManager.WorldToGridPosition(framiliar.transform.position));
            }

            int currentRarity = (int)framiliar.CurrentRarity;
            int currentDeckCount = playerDeck != null && playerDeck.DeckSlots != null ? playerDeck.DeckSlots.Count : 0;

            if (lastFramiliarMenuOperator == framiliar &&
                lastFramiliarMenuRarity == currentRarity &&
                lastFramiliarMenuCell == cell &&
                lastFramiliarMenuDeckCount == currentDeckCount &&
                framiliarCopyMenuRoot.activeSelf)
            {
                return;
            }

            lastFramiliarMenuOperator = framiliar;
            lastFramiliarMenuRarity = currentRarity;
            lastFramiliarMenuCell = cell;
            lastFramiliarMenuDeckCount = currentDeckCount;
            activeFramiliarMatchingCards.Clear();

            bool isHighGround = cell != null && cell.TileType == TileType.HighGround;
            bool isLowGround = cell != null && (cell.TileType == TileType.LowGround || cell.TileType == TileType.EnemyPath);

            if (framiliarSubtitleText != null)
            {
                if (isHighGround)
                    framiliarSubtitleText.text = "COPY: HIGH GROUND (RANGED)";
                else if (isLowGround)
                    framiliarSubtitleText.text = "COPY: LOW GROUND (MELEE)";
                else
                    framiliarSubtitleText.text = "COPY OPERATOR";
            }

            var deck = playerDeck != null ? playerDeck.DeckSlots : null;
            if (deck != null)
            {
                for (int i = 0; i < deck.Count; i++)
                {
                    var card = deck[i];
                    if (card == null || card.operatorData == null) continue;
                    if (card.operatorData.operatorName == "Framiliar") continue;

                    // Enforce tile ground matching: only display matching operators, do not show non-matching!
                    if (cell != null && !cell.SupportsPosition(card.operatorData.position)) continue;

                    activeFramiliarMatchingCards.Add(card);
                }
            }

            int matchingCount = activeFramiliarMatchingCards.Count;
            RectTransform labelRect = selectedOperatorLabelRoot.GetComponent<RectTransform>();

            if (matchingCount == 0)
            {
                if (framiliarEmptyMessageText != null)
                {
                    framiliarEmptyMessageText.gameObject.SetActive(true);
                    framiliarEmptyMessageText.text = isHighGround
                        ? "No Ranged operators in deck."
                        : (isLowGround ? "No Melee operators in deck." : "No matching operators in deck.");
                }
                for (int i = 0; i < DeckSlotCount; i++)
                {
                    if (framiliarCopyButtons[i] != null) framiliarCopyButtons[i].gameObject.SetActive(false);
                }
                labelRect.sizeDelta = new Vector2(280f, 145f);
            }
            else
            {
                if (framiliarEmptyMessageText != null)
                {
                    framiliarEmptyMessageText.gameObject.SetActive(false);
                }

                int row0Count = Mathf.Min(matchingCount, 4);
                int row1Count = Mathf.Max(0, matchingCount - 4);
                bool hasTwoRows = row1Count > 0;

                // Dynamic size: 1 row is 196f (shorter/wider aspect), 2 rows is 260f (longer)
                labelRect.sizeDelta = new Vector2(280f, hasTwoRows ? 260f : 196f);

                int framiliarUpgradeLevels = Mathf.Max(0, (int)framiliar.CurrentRarity - (int)(framiliar.OriginalFramiliarData != null ? framiliar.OriginalFramiliarData.baseRarity : (framiliar.Data != null ? framiliar.Data.baseRarity : OperatorRarity.Star2)));

                const float btnWidth = 54f;
                const float btnSpacing = 8f;

                // Row 0 layout
                float row0Width = row0Count * btnWidth + Mathf.Max(0, row0Count - 1) * btnSpacing;
                float startX0 = -row0Width * 0.5f + (btnWidth * 0.5f);
                float row0CenterY = -152f;

                // Row 1 layout
                float row1Width = row1Count * btnWidth + Mathf.Max(0, row1Count - 1) * btnSpacing;
                float startX1 = -row1Width * 0.5f + (btnWidth * 0.5f);
                float row1CenterY = -218f;

                for (int i = 0; i < DeckSlotCount; i++)
                {
                    if (i < matchingCount)
                    {
                        var card = activeFramiliarMatchingCards[i];
                        framiliarCopyButtons[i].gameObject.SetActive(true);
                        RectTransform btnRect = framiliarCopyButtons[i].GetComponent<RectTransform>();

                        float posX;
                        float posY;
                        if (i < 4)
                        {
                            posX = startX0 + i * (btnWidth + btnSpacing);
                            posY = row0CenterY;
                        }
                        else
                        {
                            int colIndex = i - 4;
                            posX = startX1 + colIndex * (btnWidth + btnSpacing);
                            posY = row1CenterY;
                        }

                        btnRect.anchoredPosition = new Vector2(posX, posY);

                        framiliarCopyPortraits[i].sprite = card.operatorData.portrait;
                        framiliarCopyPortraits[i].enabled = card.operatorData.portrait != null;
                        framiliarCopyPortraits[i].color = Color.white;

                        Color classColor = GetClassColor(card.operatorData.operatorClass);
                        framiliarCopyClassBadgeImages[i].color = Color.clear;
                        framiliarCopyClassIconImages[i].sprite = GetClassIconSprite(card.operatorData.operatorClass);
                        framiliarCopyClassIconImages[i].color = Color.white;
                        framiliarCopyClassBadges[i].SetActive(true);

                        // Upgrade inheritance
                        int cardUpgrade = Mathf.Max(0, (int)card.rarity - (int)card.operatorData.baseRarity);
                        int effectiveUpgrade = Mathf.Max(framiliarUpgradeLevels, cardUpgrade);
                        OperatorRarity effectiveRarity = (OperatorRarity)Mathf.Clamp((int)card.operatorData.baseRarity + effectiveUpgrade, (int)card.operatorData.baseRarity, (int)OperatorRarity.Star5);

                        string stars = new string('★', (int)effectiveRarity);
                        if (effectiveUpgrade > cardUpgrade)
                        {
                            framiliarCopyLabels[i].text = $"<color=#FFD700>{stars}</color>";
                        }
                        else
                        {
                            framiliarCopyLabels[i].text = stars;
                        }
                        framiliarCopyLabels[i].color = new Color(1f, 0.78f, 0.2f, 1f);

                        framiliarCopyButtons[i].interactable = true;
                        framiliarCopyFeedbacks[i].glowColor = Color.Lerp(classColor, Color.white, 0.5f);
                    }
                    else
                    {
                        framiliarCopyButtons[i].gameObject.SetActive(false);
                    }
                }
            }

            framiliarCopyMenuRoot.SetActive(true);
        }

        public void HideFramiliarCopyMenu()
        {
            activeFramiliarOperator = null;
            lastFramiliarMenuOperator = null;
            lastFramiliarMenuRarity = -1;
            lastFramiliarMenuCell = null;
            lastFramiliarMenuDeckCount = -1;
            activeFramiliarMatchingCards.Clear();
            if (framiliarCopyMenuRoot != null)
            {
                framiliarCopyMenuRoot.SetActive(false);
            }
            if (selectedOperatorLabelRoot != null)
            {
                selectedOperatorLabelRoot.GetComponent<RectTransform>().sizeDelta = new Vector2(280f, 106f);
            }
        }

        private void SelectFramiliarCopySlot(int slotIndex)
        {
            if (activeFramiliarOperator == null) return;
            if (slotIndex < 0 || slotIndex >= activeFramiliarMatchingCards.Count) return;

            var card = activeFramiliarMatchingCards[slotIndex];
            var fram = activeFramiliarOperator;
            HideFramiliarCopyMenu();
            fram.CopyOperator(card);
        }

        private void CreateCooldownToast(Transform root)
        {
            var toast = new GameObject("CooldownToast", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            toast.transform.SetParent(root, false);
            cooldownToastRect = toast.GetComponent<RectTransform>();
            SetPosition(cooldownToastRect, new Vector2(0f, 188f), new Vector2(0.5f, 0f), new Vector2(560f, 76f), new Vector2(0.5f, 0f));

            Image background = toast.GetComponent<Image>();
            ApplyPanelContainer(toast, PanelContainerType.Tooltip);
            background.raycastTarget = false;

            Outline outline = toast.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.48f, 0.16f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = false;

            cooldownToastGroup = toast.GetComponent<CanvasGroup>();
            cooldownToastGroup.alpha = 0f;
            cooldownToastGroup.interactable = false;
            cooldownToastGroup.blocksRaycasts = false;

            cooldownToastTitle = CreateText(toast.transform, "Title", "OPERATOR ON COOLDOWN", 13, TextAnchor.MiddleCenter);
            cooldownToastTitle.color = new Color(1f, 0.65f, 0.25f, 1f);
            cooldownToastTitle.raycastTarget = false;
            SetPosition(cooldownToastTitle.GetComponent<RectTransform>(), new Vector2(0f, -18f), new Vector2(0.5f, 1f), new Vector2(520f, 26f), new Vector2(0.5f, 0.5f));

            cooldownToastMessage = CreateText(toast.transform, "Message", string.Empty, 14, TextAnchor.MiddleCenter);
            cooldownToastMessage.raycastTarget = false;
            SetPosition(cooldownToastMessage.GetComponent<RectTransform>(), new Vector2(0f, -49f), new Vector2(0.5f, 1f), new Vector2(520f, 28f), new Vector2(0.5f, 0.5f));
        }

        public void ShowCooldownWarning(string operatorName, int remainingRounds)
        {
            if (remainingRounds <= 0 || cooldownToastGroup == null) return;

            AudioManager.Instance?.PlaySfx(SfxId.UiDenied, sfxVolume);

            string displayName = string.IsNullOrWhiteSpace(operatorName) ? "OPERATOR" : operatorName.ToUpperInvariant();
            string roundLabel = remainingRounds == 1 ? "ROUND" : "ROUNDS";
            cooldownToastMessage.text = $"{displayName} READY IN {remainingRounds} {roundLabel}";

            if (cooldownToastRoutine != null) StopCoroutine(cooldownToastRoutine);
            cooldownToastRoutine = StartCoroutine(CooldownToastRoutine());
        }

        private System.Collections.IEnumerator CooldownToastRoutine()
        {
            const float fadeDuration = 0.18f;
            const float visibleDuration = 1.8f;
            Vector2 shownPosition = new Vector2(0f, 188f);
            Vector2 hiddenPosition = shownPosition + new Vector2(0f, -18f);
            cooldownToastRect.anchoredPosition = hiddenPosition;
            cooldownToastRect.localScale = Vector3.one * 0.94f;
            cooldownToastGroup.alpha = 0f;

            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / fadeDuration);
                float eased = EaseOutCubic(progress);
                cooldownToastGroup.alpha = eased;
                cooldownToastRect.anchoredPosition = Vector2.Lerp(hiddenPosition, shownPosition, eased);
                cooldownToastRect.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, eased);
                yield return null;
            }

            cooldownToastGroup.alpha = 1f;
            cooldownToastRect.anchoredPosition = shownPosition;
            cooldownToastRect.localScale = Vector3.one;
            yield return new WaitForSecondsRealtime(visibleDuration);

            elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / fadeDuration);
                cooldownToastGroup.alpha = 1f - progress;
                cooldownToastRect.anchoredPosition = Vector2.Lerp(shownPosition, hiddenPosition, progress);
                yield return null;
            }

            cooldownToastGroup.alpha = 0f;
            cooldownToastRect.anchoredPosition = hiddenPosition;
            cooldownToastRoutine = null;
        }

        public bool IsDraftMinimized => draftOverlayUI != null && draftOverlayUI.IsMinimized;

        /// <summary>Camera for screen-point conversions on the HUD canvas (null while it is an overlay canvas).</summary>
        private Camera GetCanvasCamera()
        {
            return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }

        // Deck cards can be deployed in Preparation, and during Card Pick while the draft is minimized
        // (the map & deck view), so a deploy can free a deck slot for the card about to be picked.
        private bool IsDeckDeployPhase => gameManager != null &&
            (gameManager.CurrentPhase == StagePhase.Preparation ||
             (gameManager.CurrentPhase == StagePhase.CardPick && IsDraftMinimized));

        // During Card Pick with the full draft screen up, the deck can only be rearranged and discarded from.
        private bool IsDeckEditOnly => gameManager != null && gameManager.CurrentPhase == StagePhase.CardPick && !IsDraftMinimized;

        private bool CanDragDeckCard(int slotIndex)
        {
            if (gameManager == null || playerDeck == null) return false;
            if (!IsDeckDeployPhase && !IsDeckEditOnly) return false;

            DraftCard card = playerDeck.GetCard(slotIndex);
            if (card == null) return false;
            if (card.cooldownRoundsRemaining <= 0 || IsDeckEditOnly) return true;

            ShowCooldownWarning(card.operatorData != null ? card.operatorData.operatorName : null, card.cooldownRoundsRemaining);
            return false;
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

        private void CreateEnvironmentBriefing(Transform root)
        {
            environmentBriefingPanel = new GameObject(
                "EnvironmentBriefing",
                typeof(RectTransform),
                typeof(Image),
                typeof(Canvas),
                typeof(GraphicRaycaster));
            environmentBriefingPanel.transform.SetParent(root, false);
            Stretch(environmentBriefingPanel.GetComponent<RectTransform>());
            environmentBriefingPanel.GetComponent<Image>().color = new Color(0.01f, 0.015f, 0.025f, 0.82f);

            Canvas overlayCanvas = environmentBriefingPanel.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingOrder = 300;

            GameObject card = new GameObject("BriefingCard", typeof(RectTransform), typeof(Image), typeof(Outline));
            card.transform.SetParent(environmentBriefingPanel.transform, false);
            environmentBriefingCardRect = card.GetComponent<RectTransform>();
            SetPosition(environmentBriefingCardRect, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(740f, 440f), new Vector2(0.5f, 0.5f));
            ApplyModalCard(card);
            Outline cardOutline = card.GetComponent<Outline>();
            cardOutline.effectColor = new Color(WaveColor.r, WaveColor.g, WaveColor.b, 0.8f);
            cardOutline.effectDistance = new Vector2(3f, -3f);

            environmentBriefingTitle = CreateText(card.transform, "Title", "ENVIRONMENTAL EFFECT", 30, TextAnchor.MiddleCenter);
            environmentBriefingTitle.color = WaveColor;
            SetPosition(environmentBriefingTitle.rectTransform, new Vector2(0f, -40f), new Vector2(0.5f, 1f), new Vector2(660f, 52f), new Vector2(0.5f, 1f));
            environmentBriefingTitle.raycastTarget = false;

            environmentBriefingDescription = CreateText(card.transform, "Description", string.Empty, 18, TextAnchor.MiddleCenter);
            environmentBriefingDescription.fontStyle = FontStyle.Normal;
            environmentBriefingDescription.horizontalOverflow = HorizontalWrapMode.Wrap;
            environmentBriefingDescription.verticalOverflow = VerticalWrapMode.Overflow;
            environmentBriefingDescription.color = new Color(0.86f, 0.9f, 0.95f, 1f);
            SetPosition(environmentBriefingDescription.rectTransform, new Vector2(0f, -100f), new Vector2(0.5f, 1f), new Vector2(640f, 210f), new Vector2(0.5f, 1f));
            environmentBriefingDescription.raycastTarget = false;

            CreateStage2BriefingItemsDisplay(card.transform);
            CreateStage5BriefingTrafficDisplay(card.transform);

            Button dismissButton = CreateButton(card.transform, "DismissButton", "UNDERSTOOD", new Vector2(250f, 58f), ButtonContainerType.Primary);
            SetPosition(dismissButton.GetComponent<RectTransform>(), new Vector2(0f, 32f), new Vector2(0.5f, 0f), null, new Vector2(0.5f, 0f));
            StyleButton(dismissButton, ButtonContainerType.Primary, AccentColor, 1.05f);
            dismissButton.onClick.AddListener(DismissEnvironmentBriefing);

            environmentBriefingPanel.SetActive(false);
        }

        private void CreateStage2BriefingItemsDisplay(Transform parent)
        {
            stage2ThrownItemsRoot = new GameObject("Stage2ItemsRoot", typeof(RectTransform));
            stage2ThrownItemsRoot.transform.SetParent(parent, false);
            RectTransform rootRect = stage2ThrownItemsRoot.GetComponent<RectTransform>();
            SetPosition(rootRect, new Vector2(0f, -178f), new Vector2(0.5f, 1f), new Vector2(780f, 245f), new Vector2(0.5f, 1f));

            // Create 3 item cards side by side: Glass (-260), Metal (0), Tools (+260)
            stage2GlassIcon = CreateBriefingItemCard(stage2ThrownItemsRoot.transform, -260f, "Glass", "GLASS", new Color(0.4f, 0.9f, 1f, 1f), "<b>Weak DoT Damage</b>\nInflicts continuous bleeding damage over 5s.");
            stage2MetalIcon = CreateBriefingItemCard(stage2ThrownItemsRoot.transform, 0f, "Metal", "METAL", new Color(1f, 0.8f, 0.3f, 1f), "<b>Direct True Damage</b>\nDeals heavy burst damage upon impact.");
            stage2ToolsIcon = CreateBriefingItemCard(stage2ThrownItemsRoot.transform, 260f, "Tools", "TOOLS", new Color(1f, 0.45f, 0.45f, 1f), "<b>True Damage + Stun</b>\nDeals direct damage and stuns operators for 2s.");

            stage2ThrownItemsRoot.SetActive(false);
        }

        private void CreateStage5BriefingTrafficDisplay(Transform parent)
        {
            stage5TrafficRoot = new GameObject("Stage5TrafficRoot", typeof(RectTransform));
            stage5TrafficRoot.transform.SetParent(parent, false);
            RectTransform rootRect = stage5TrafficRoot.GetComponent<RectTransform>();
            SetPosition(rootRect, new Vector2(0f, -112f), new Vector2(0.5f, 1f), new Vector2(780f, 350f), new Vector2(0.5f, 1f));

            // Single card container
            GameObject box = new GameObject("Stage5TrafficCard", typeof(RectTransform), typeof(Image), typeof(Outline));
            box.transform.SetParent(stage5TrafficRoot.transform, false);
            RectTransform boxRect = box.GetComponent<RectTransform>();
            SetPosition(boxRect, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(780f, 350f), new Vector2(0.5f, 0.5f));

            Image boxImg = box.GetComponent<Image>();
            boxImg.color = new Color(0.04f, 0.06f, 0.09f, 0.94f);

            Outline boxOutline = box.GetComponent<Outline>();
            boxOutline.effectColor = new Color(1f, 0.8f, 0.25f, 0.6f);
            boxOutline.effectDistance = new Vector2(1.5f, -1.5f);

            // 3-Vehicle lineup: Left, Center (Red Car), Right
            stage5VehicleLeftIcon = CreateIconImage(box.transform, "VehicleLeft", null, Color.white, new Vector2(-175f, 114f), new Vector2(0.5f, 0.5f), new Vector2(130f, 65f), new Vector2(0.5f, 0.5f));
            stage5VehicleLeftIcon.preserveAspect = true;

            stage5VehicleCenterIcon = CreateIconImage(box.transform, "VehicleCenter", null, Color.white, new Vector2(0f, 114f), new Vector2(0.5f, 0.5f), new Vector2(130f, 65f), new Vector2(0.5f, 0.5f));
            stage5VehicleCenterIcon.preserveAspect = true;

            stage5VehicleRightIcon = CreateIconImage(box.transform, "VehicleRight", null, Color.white, new Vector2(175f, 114f), new Vector2(0.5f, 0.5f), new Vector2(130f, 65f), new Vector2(0.5f, 0.5f));
            stage5VehicleRightIcon.preserveAspect = true;

            // Content text (clean single-line alignment across 720px width)
            stage5TrafficBodyText = CreateText(box.transform, "BodyText", "", 13, TextAnchor.MiddleCenter);
            stage5TrafficBodyText.fontStyle = FontStyle.Normal;
            stage5TrafficBodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            stage5TrafficBodyText.verticalOverflow = VerticalWrapMode.Overflow;
            stage5TrafficBodyText.lineSpacing = 1.28f;
            stage5TrafficBodyText.color = new Color(0.88f, 0.91f, 0.95f, 1f);
            SetPosition(stage5TrafficBodyText.rectTransform, new Vector2(0f, -42f), new Vector2(0.5f, 0.5f), new Vector2(760f, 220f), new Vector2(0.5f, 0.5f));
            stage5TrafficBodyText.raycastTarget = false;

            stage5TrafficRoot.SetActive(false);
        }

        private Image CreateBriefingItemCard(Transform parent, float xPos, string cardName, string title, Color titleColor, string description)
        {
            GameObject box = new GameObject($"Card_{cardName}", typeof(RectTransform), typeof(Image), typeof(Outline));
            box.transform.SetParent(parent, false);
            RectTransform boxRect = box.GetComponent<RectTransform>();
            SetPosition(boxRect, new Vector2(xPos, 0f), new Vector2(0.5f, 0.5f), new Vector2(245f, 240f), new Vector2(0.5f, 0.5f));

            Image boxImg = box.GetComponent<Image>();
            boxImg.color = new Color(0.04f, 0.06f, 0.09f, 0.92f);

            Outline boxOutline = box.GetComponent<Outline>();
            boxOutline.effectColor = new Color(titleColor.r, titleColor.g, titleColor.b, 0.55f);
            boxOutline.effectDistance = new Vector2(1.5f, -1.5f);

            // Sprite preview container
            GameObject iconBg = new GameObject("IconBg", typeof(RectTransform), typeof(Image));
            iconBg.transform.SetParent(box.transform, false);
            RectTransform iconBgRect = iconBg.GetComponent<RectTransform>();
            SetPosition(iconBgRect, new Vector2(0f, 62f), new Vector2(0.5f, 0.5f), new Vector2(64f, 64f), new Vector2(0.5f, 0.5f));
            iconBg.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);

            Image iconImg = CreateIconImage(iconBg.transform, "Icon", null, Color.white, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(52f, 52f), new Vector2(0.5f, 0.5f));

            // Title
            Text titleText = CreateText(box.transform, "Title", title, 18, TextAnchor.MiddleCenter);
            titleText.color = titleColor;
            SetPosition(titleText.rectTransform, new Vector2(0f, 12f), new Vector2(0.5f, 0.5f), new Vector2(220f, 26f), new Vector2(0.5f, 0.5f));
            titleText.raycastTarget = false;

            // Description
            Text descText = CreateText(box.transform, "Desc", description, 13, TextAnchor.MiddleCenter);
            descText.fontStyle = FontStyle.Normal;
            descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            descText.verticalOverflow = VerticalWrapMode.Overflow;
            descText.lineSpacing = 1.15f;
            descText.color = new Color(0.85f, 0.88f, 0.92f, 1f);
            SetPosition(descText.rectTransform, new Vector2(0f, -50f), new Vector2(0.5f, 0.5f), new Vector2(225f, 90f), new Vector2(0.5f, 0.5f));
            descText.raycastTarget = false;

            return iconImg;
        }

        private IEnumerator ShowEnvironmentBriefingWhenReady()
        {
            if (gameManager == null) yield break;

            while (isActiveAndEnabled && gameManager.CurrentStage == null)
            {
                yield return null;
            }

            if (!isActiveAndEnabled || gameManager.CurrentStage == null) yield break;
            StageData stage = gameManager.CurrentStage;

            // The sandbox shows the briefing for the environment of the map it runs on.
            string environmentStageId = stageBootstrapper != null ? stageBootstrapper.EnvironmentStageId : stage.stageId;
            string environmentName = stage.mapName;
            if (environmentStageId != stage.stageId)
            {
                StageData environmentStage = Resources.Load<StageData>("Stages/" + environmentStageId);
                if (environmentStage != null) environmentName = environmentStage.mapName;
            }

            if (environmentStageId == "STAGE_02")
            {
                if (environmentBriefingCardRect != null)
                {
                    environmentBriefingCardRect.sizeDelta = new Vector2(840f, 570f);
                }
                if (stage5TrafficRoot != null)
                {
                    stage5TrafficRoot.SetActive(false);
                }

                environmentBriefingTitle.text = "HAZARD ALERT";
                SetPosition(environmentBriefingTitle.rectTransform, new Vector2(0f, -36f), new Vector2(0.5f, 1f), new Vector2(700f, 40f), new Vector2(0.5f, 1f));

                environmentBriefingDescription.text =
                    $"<color=#FFD040>{environmentName}</color>\n" +
                    "Enemies are throwing scavenged items across the battlefield every 10 seconds!\n" +
                    "Watch out for the 3 types of thrown debris:";
                SetPosition(environmentBriefingDescription.rectTransform, new Vector2(0f, -86f), new Vector2(0.5f, 1f), new Vector2(760f, 80f), new Vector2(0.5f, 1f));
                environmentBriefingDescription.fontSize = 15;
                environmentBriefingDescription.lineSpacing = 1.15f;

                // Load sprites from stage or hazard
                Sprite[] itemSprites = stage.fallingItemSprites;
                if (itemSprites == null || itemSprites.Length == 0)
                {
                    var hazard = FindAnyObjectByType<TrashTD.Core.GameLoop.Stage2FallingItemsHazard>();
                    if (hazard != null) itemSprites = hazard.ItemSprites;
                }
                if (itemSprites == null || itemSprites.Length == 0)
                {
                    itemSprites = Resources.LoadAll<Sprite>("Levels/Level 2 thrown-items");
                }
#if UNITY_EDITOR
                if (itemSprites == null || itemSprites.Length == 0)
                {
                    var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level 2 thrown-items.png");
                    if (assets != null)
                    {
                        var list = new List<Sprite>();
                        foreach (var obj in assets)
                        {
                            if (obj is Sprite s) list.Add(s);
                        }
                        if (list.Count > 0)
                        {
                            list.Sort((a, b) => TrashTD.Core.GameLoop.Stage2FallingItemsHazard.ExtractTrailingIndex(a.name).CompareTo(TrashTD.Core.GameLoop.Stage2FallingItemsHazard.ExtractTrailingIndex(b.name)));
                            itemSprites = list.ToArray();
                        }
                    }
                }
#endif

                if (stage2ThrownItemsRoot != null)
                {
                    stage2ThrownItemsRoot.SetActive(true);

                    // Glass: index 0 (0..7)
                    if (stage2GlassIcon != null && itemSprites != null && itemSprites.Length > 0)
                    {
                        stage2GlassIcon.sprite = itemSprites[0];
                        stage2GlassIcon.color = Color.white;
                    }

                    // Metal: index 8 (8..15)
                    if (stage2MetalIcon != null && itemSprites != null && itemSprites.Length > 8)
                    {
                        stage2MetalIcon.sprite = itemSprites[8];
                        stage2MetalIcon.color = Color.white;
                    }

                    // Tools: index 16 (16..23)
                    if (stage2ToolsIcon != null && itemSprites != null && itemSprites.Length > 16)
                    {
                        stage2ToolsIcon.sprite = itemSprites[16];
                        stage2ToolsIcon.color = Color.white;
                    }
                }
            }
            else if (environmentStageId == "STAGE_05")
            {
                if (environmentBriefingCardRect != null)
                {
                    environmentBriefingCardRect.sizeDelta = new Vector2(840f, 570f);
                }
                if (stage2ThrownItemsRoot != null)
                {
                    stage2ThrownItemsRoot.SetActive(false);
                }

                environmentBriefingTitle.text = "HAZARD ALERT";
                SetPosition(environmentBriefingTitle.rectTransform, new Vector2(0f, -36f), new Vector2(0.5f, 1f), new Vector2(700f, 40f), new Vector2(0.5f, 1f));

                environmentBriefingDescription.text = $"<color=#FFD040>{environmentName}</color>";
                SetPosition(environmentBriefingDescription.rectTransform, new Vector2(0f, -76f), new Vector2(0.5f, 1f), new Vector2(700f, 32f), new Vector2(0.5f, 1f));
                environmentBriefingDescription.fontSize = 17;
                environmentBriefingDescription.lineSpacing = 1f;

                // Load vehicle sprites
                Sprite[] vehicleSprites = stage.trafficVehicleSprites;
                if (vehicleSprites == null || vehicleSprites.Length == 0)
                {
                    var encounter = FindAnyObjectByType<TrashTD.Core.GameLoop.Stage5CarEncounter>();
                    if (encounter != null) vehicleSprites = encounter.VehicleSprites;
                }
                if (vehicleSprites == null || vehicleSprites.Length == 0)
                {
                    vehicleSprites = Resources.LoadAll<Sprite>("Levels/Level 5 vehicles");
                }
#if UNITY_EDITOR
                if (vehicleSprites == null || vehicleSprites.Length == 0)
                {
                    var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level 5 vehicles.png");
                    if (assets == null || assets.Length == 0)
                    {
                        assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/level 5 vehicles.png");
                    }
                    if (assets != null)
                    {
                        var list = new List<Sprite>();
                        foreach (var obj in assets)
                        {
                            if (obj is Sprite s) list.Add(s);
                        }
                        if (list.Count > 0)
                        {
                            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                            vehicleSprites = list.ToArray();
                        }
                    }
                }
#endif

                if (stage5TrafficRoot != null)
                {
                    stage5TrafficRoot.SetActive(true);

                    if (vehicleSprites != null && vehicleSprites.Length > 0)
                    {
                        // Center is the red car (index 1)
                        if (stage5VehicleCenterIcon != null)
                        {
                            stage5VehicleCenterIcon.sprite = vehicleSprites.Length > 1 ? vehicleSprites[1] : vehicleSprites[0];
                            stage5VehicleCenterIcon.color = Color.white;
                        }

                        // Left is index 0
                        if (stage5VehicleLeftIcon != null)
                        {
                            stage5VehicleLeftIcon.sprite = vehicleSprites[0];
                            stage5VehicleLeftIcon.color = Color.white;
                        }

                        // Right is index 2 (or index 0 if not enough)
                        if (stage5VehicleRightIcon != null)
                        {
                            int rightIdx = vehicleSprites.Length > 2 ? 2 : 0;
                            stage5VehicleRightIcon.sprite = vehicleSprites[rightIdx];
                            stage5VehicleRightIcon.color = Color.white;
                        }
                    }

                    if (stage5TrafficBodyText != null)
                    {
                        stage5TrafficBodyText.text =
                            "Fast Vehicles drive across the 2 middle highway road lanes starting at wave 6.\n" +
                            "They crash into both enemies and operators in their way.\n\n" +
                            "Starts at 40 true damage (+20 every 2 tiles):\n" +
                            "<color=#FFD040>40 → 60 → 80 → 100 → 120 → 140 → 160</color>\n" +
                            "Longer distance = heavier hit when unblocked by an operator.\n\n" +
                            "Hitting an operator slows the car down, then it recovers.\n" +
                            "Stuns the operator for 5 seconds and resets damage to 40.";
                    }
                }
            }
            else
            {
                if (environmentBriefingCardRect != null)
                {
                    environmentBriefingCardRect.sizeDelta = new Vector2(740f, 440f);
                }
                if (stage2ThrownItemsRoot != null)
                {
                    stage2ThrownItemsRoot.SetActive(false);
                }
                if (stage5TrafficRoot != null)
                {
                    stage5TrafficRoot.SetActive(false);
                }
                environmentBriefingTitle.text = "ENVIRONMENTAL EFFECT";
                SetPosition(environmentBriefingTitle.rectTransform, new Vector2(0f, -40f), new Vector2(0.5f, 1f), new Vector2(660f, 52f), new Vector2(0.5f, 1f));
                SetPosition(environmentBriefingDescription.rectTransform, new Vector2(0f, -100f), new Vector2(0.5f, 1f), new Vector2(640f, 210f), new Vector2(0.5f, 1f));
                environmentBriefingDescription.fontSize = 18;
                environmentBriefingDescription.lineSpacing = 1f;

                string effectDescription;
                switch (environmentStageId)
                {
                    case "STAGE_03":
                        effectDescription =
                            "A sandstorm sweeps the battlefield. Attack intervals are 5% shorter, " +
                            "and each attack has a 10% chance to miss.";
                        break;
                    case "STAGE_04":
                        effectDescription =
                            "Extreme heat builds up on deployed operators each round. " +
                            "Overheated operators ignite and suffer continuous burn damage until retreated to the bench to cool down.";
                        break;
                    case "STAGE_06":
                        effectDescription =
                            "Acid rain periodically deals 3 true damage to deployed operators " +
                            "and active enemies every 2 seconds while a wave is running. Operators are protected between waves.";
                        break;
                    default:
                        yield break;
                }

                environmentBriefingDescription.text = $"{environmentName}\n\n{effectDescription}";
            }

            environmentBriefingPanel.SetActive(true);
        }

        private void DismissEnvironmentBriefing()
        {
            environmentBriefingDismissed = true;
            if (environmentBriefingPanel != null) environmentBriefingPanel.SetActive(false);
            EnvironmentBriefingDismissed?.Invoke();
        }

        private void CreatePausePanel(Transform root)
        {
            pausePanel = new GameObject("PausePanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            pausePanel.transform.SetParent(root, false);
            Stretch(pausePanel.GetComponent<RectTransform>());
            pausePanel.GetComponent<Image>().color = new Color(0.01f, 0.012f, 0.02f, 0.86f);

            pauseGroup = pausePanel.GetComponent<CanvasGroup>();
            pauseGroup.alpha = 0f;

            // Centered card: the 256x256 pause menu panel art drawn at 2x, grown by one indicator row (see
            // BuildPauseCardArt). Its header screen spans y 37..101 (title), a divider sits at y 103, and the body
            // screen spans y 104..251 (buttons), with four indicator rows (LED on the left, a slot on the right).
            var card = new GameObject("PauseCard", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(pausePanel.transform, false);
            pauseCard = card.GetComponent<RectTransform>();
            SetPosition(pauseCard, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(PauseArtPixels, PauseCardHeightPx) * PauseCardScale, new Vector2(0.5f, 0.5f));
            if (!BuildPauseCardArt(pauseCard))
            {
                ApplyModalCard(card);
            }

            // Header screen: title and subtitle centred as one block, the subtitle between two short rules
            var title = CreateText(card.transform, "PauseTitle", "PAUSED", 48, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Normal;
            SetPosition(title.GetComponent<RectTransform>(), PausePx(0f, PauseTitleRowPx), new Vector2(0.5f, 1f), new Vector2(400f, 60f), new Vector2(0.5f, 0.5f));
            title.color = AccentColor;
            title.raycastTarget = false;
            var titleShadow = title.gameObject.AddComponent<Shadow>();
            titleShadow.effectColor = new Color(0.02f, 0.14f, 0.07f, 1f);
            titleShadow.effectDistance = new Vector2(0f, -4f);

            var subtitle = CreateText(card.transform, "PauseSubtitle", "THE OPERATION WAITS", 12, TextAnchor.MiddleCenter);
            subtitle.fontStyle = FontStyle.Normal;
            SetPosition(subtitle.GetComponent<RectTransform>(), PausePx(0f, PauseSubtitleRowPx), new Vector2(0.5f, 1f), new Vector2(400f, 20f), new Vector2(0.5f, 0.5f));
            subtitle.color = TextDim;
            subtitle.raycastTarget = false;

            float ruleInner = subtitle.preferredWidth * 0.5f + 14f;
            Color ruleColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.45f);
            foreach (float side in new[] { -1f, 1f })
            {
                CreateIconImage(card.transform, "PauseSubtitleRule", null, ruleColor,
                    PausePx(side * (ruleInner + 18f), PauseSubtitleRowPx), new Vector2(0.5f, 1f), new Vector2(36f, 2f), new Vector2(0.5f, 0.5f));
            }

            // Body screen: one row per indicator light, all the same width on the card's centre line.
            // Row 2 is Retry plus the square Settings button (added by AudioSettingsUI in Awake, see PauseSettingsX).
            var resumeButton = CreatePauseButton(card.transform, "ResumeButton", "RESUME", "icon_play",
                ButtonContainerType.Primary, new Color(0.4f, 1f, 0.65f, 1f), 0f, PauseRow1Px, PauseRowWidth);
            resumeButton.onClick.AddListener(ResumeGame);

            var retryButton = CreatePauseButton(card.transform, "RetryButton", "RETRY", "icon_reroll",
                ButtonContainerType.Secondary, new Color(0.55f, 0.75f, 1f, 1f),
                -(PauseButtonHeight + PauseRowGap) * 0.5f, PauseRow2Px, PauseRowWidth - PauseButtonHeight - PauseRowGap);
            retryButton.onClick.AddListener(RetryStage);

            // Row 3 is the Reclaimers' Intel button (added by ReclaimersIntelUI in Awake).
            var exitButton = CreatePauseButton(card.transform, "ExitButton", "EXIT TO MENU", "icon_exit",
                ButtonContainerType.Danger, new Color(1f, 0.4f, 0.4f, 1f), 0f, PauseRow4Px, PauseRowWidth);
            exitButton.onClick.AddListener(ExitToMenu);

            pausePanel.SetActive(false);
        }

        private Button CreatePauseButton(Transform parent, string objectName, string label, string iconName,
            ButtonContainerType containerType, Color glow, float x, float rowPx, float width)
        {
            Button button = CreateButton(parent, objectName, label, new Vector2(width, PauseButtonHeight), containerType);
            SetPosition(button.GetComponent<RectTransform>(), PausePx(x, rowPx), new Vector2(0.5f, 1f), null, new Vector2(0.5f, 0.5f));
            StyleButton(button, containerType, glow, 1.04f);
            ApplyPauseButtonLook(button, label, iconName);
            return button;
        }

        /// <summary>
        /// Pause-card button look: the frame drawn at the card's 2x pixel scale, and a 2x icon plus label centred
        /// together as one group. A null/empty label gives an icon-only button.
        /// </summary>
        private static void ApplyPauseButtonLook(Button button, string label, string iconName)
        {
            Image frame = button.GetComponent<Image>();
            if (frame != null) frame.pixelsPerUnitMultiplier = 1f / PauseCardScale;

            Text text = button.GetComponentInChildren<Text>();
            bool hasLabel = text != null && !string.IsNullOrEmpty(label);
            if (text != null)
            {
                text.text = hasLabel ? label : string.Empty;
                text.fontSize = 16;
                text.fontStyle = FontStyle.Normal;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.alignment = TextAnchor.MiddleLeft;
                if (text.GetComponent<Shadow>() == null)
                {
                    var shadow = text.gameObject.AddComponent<Shadow>();
                    shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
                    shadow.effectDistance = new Vector2(0f, -2f);
                }
            }

            Sprite icon = LoadIconSprite(iconName);
            if (icon != null && icon.texture != null) icon.texture.filterMode = FilterMode.Point;
            float iconSize = 16f * PauseCardScale;
            float labelWidth = hasLabel ? text.preferredWidth : 0f;
            float gap = icon != null && hasLabel ? 10f : 0f;
            float left = -((icon != null ? iconSize : 0f) + gap + labelWidth) * 0.5f;

            if (icon != null)
            {
                Image iconImage = CreateIconImage(button.transform, "Icon", icon, Color.white,
                    new Vector2(left + iconSize * 0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(iconSize, iconSize), new Vector2(0.5f, 0.5f));
                iconImage.raycastTarget = false;
                left += iconSize + gap;
            }

            if (text != null)
            {
                RectTransform textRect = text.rectTransform;
                textRect.anchorMin = textRect.anchorMax = new Vector2(0.5f, 0.5f);
                textRect.pivot = new Vector2(0f, 0.5f);
                textRect.anchoredPosition = new Vector2(left, 0f);
                textRect.sizeDelta = new Vector2(labelWidth + 4f, PauseButtonHeight);
            }
        }

        private const string PauseCardSpritePath = "UI/panels/pause menu panel bg";
        private const float PauseArtPixels = 256f;
        private const float PauseCardScale = 2f;

        // The card repeats one 34 px band of the art (y 146..180, holding one LED and one slot) below itself,
        // so the body screen gets a fourth indicator row: 256 + 34 = 290 art px tall.
        private const float PauseBandTopPx = 146f;
        private const float PauseBandBottomPx = 180f;
        private const float PauseCardHeightPx = PauseArtPixels + PauseBandBottomPx - PauseBandTopPx;

        // Pause card layout. Rows are in art pixels from the top: the header block is centred in the header
        // screen (y 37..101); the button rows sit on the body's indicator lights (y 129 / 163 / 197 / 231).
        private const float PauseTitleRowPx = 58f;
        private const float PauseSubtitleRowPx = 86f;
        private const float PauseRow1Px = 129f;
        private const float PauseRow2Px = 163f;
        private const float PauseRow3Px = 197f;
        private const float PauseRow4Px = 231f;
        private const float PauseRowWidth = 324f;     // canvas units: clears the LEDs (x 40) and slots (x 214) evenly
        private const float PauseButtonHeight = 48f;  // 24 art px
        private const float PauseRowGap = 12f;
        private const float PauseSettingsX = (PauseRowWidth - PauseButtonHeight) * 0.5f; // square, right end of row 2

        /// <summary>
        /// Position on the pause card: <paramref name="x"/> in canvas units from the centre line,
        /// <paramref name="yFromTop"/> in pause-art pixels.
        /// </summary>
        private static Vector2 PausePx(float x, float yFromTop) => new Vector2(x, -yFromTop * PauseCardScale);

        /// <summary>
        /// Draws the pause art on the card in three strips: art y 0..180, the 146..180 band again, then 180..256.
        /// Returns false when the texture is missing.
        /// </summary>
        private static bool BuildPauseCardArt(RectTransform card)
        {
            var texture = Resources.Load<Texture2D>(PauseCardSpritePath);
            if (texture == null) return false;
            texture.filterMode = FilterMode.Point;

            card.GetComponent<Image>().color = Color.clear;
            AddPauseArtStrip(card, texture, "PauseArtTop", 0f, PauseBandBottomPx, 0f);
            AddPauseArtStrip(card, texture, "PauseArtBand", PauseBandTopPx, PauseBandBottomPx, PauseBandBottomPx);
            AddPauseArtStrip(card, texture, "PauseArtBottom", PauseBandBottomPx, PauseArtPixels,
                PauseBandBottomPx + PauseBandBottomPx - PauseBandTopPx);
            return true;
        }

        /// <summary>Art rows <paramref name="fromPx"/>..<paramref name="toPx"/> (from the top), placed at <paramref name="cardYPx"/> on the card.</summary>
        private static void AddPauseArtStrip(RectTransform card, Texture2D texture, string name, float fromPx, float toPx, float cardYPx)
        {
            float height = toPx - fromPx;
            Sprite sprite = Sprite.Create(texture, new Rect(0f, texture.height - toPx, texture.width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);

            var strip = new GameObject(name, typeof(RectTransform), typeof(Image));
            strip.transform.SetParent(card, false);
            strip.transform.SetAsFirstSibling();
            var image = strip.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            SetPosition(image.rectTransform, PausePx(0f, cardYPx), new Vector2(0.5f, 1f),
                new Vector2(PauseArtPixels, height) * PauseCardScale, new Vector2(0.5f, 1f));
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

            if (sandboxController != null)
            {
                if (phaseText != null) phaseText.text = "QA SANDBOX";
                phaseColorTarget = PrepColor;
                SetSandboxWaveButton(waveManager != null && waveManager.IsWaveInProgress);
                return;
            }

            StagePhase phase = gameManager.CurrentPhase;
            SetWaveProgressFillSprite(phase);

            switch (phase)
            {
                case StagePhase.CardPick:
                    if (phaseText != null) phaseText.text = "CARD PICK";
                    phaseColorTarget = DraftColor;
                    startWaveButton.interactable = false;
                    if (startWaveButtonText != null) startWaveButtonText.text = "DRAFTING...";
                    ApplyButtonContainer(startWaveButton, ButtonContainerType.Dark);
                    startWaveFeedback.SetBaseColor(WaitingColor);
                    startWaveFeedback.glowAlways = false;
                    break;

                case StagePhase.Preparation:
                    if (phaseText != null) phaseText.text = "PREPARATION";
                    phaseColorTarget = PrepColor;
                    startWaveButton.interactable = true;
                    if (startWaveButtonText != null) startWaveButtonText.text = "START\nWAVE";
                    ApplyButtonContainer(startWaveButton, ButtonContainerType.Primary);
                    startWaveFeedback.SetBaseColor(Color.white);
                    startWaveFeedback.glowAlways = true;
                    break;

                case StagePhase.WaveActive:
                    if (phaseText != null) phaseText.text = "Invading";
                    phaseColorTarget = WaveColor;
                    startWaveButton.interactable = false;
                    if (startWaveButtonText != null) startWaveButtonText.text = "WAVE\nACTIVE";
                    ApplyButtonContainer(startWaveButton, ButtonContainerType.Dark);
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
                        ShowPhaseBanner("Invading", "Hold the line", WaveColor);
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
                    deckPortraitImages[i].sprite = card.operatorData.portrait;
                    deckPortraitImages[i].enabled = card.operatorData.portrait != null;
                    deckPortraitImages[i].color = isReady ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
                    Color classColor = GetClassColor(card.operatorData.operatorClass);
                    deckClassBadgeImages[i].color = Color.clear;
                    deckClassIconImages[i].sprite = GetClassIconSprite(card.operatorData.operatorClass);
                    deckClassIconImages[i].color = Color.white;
                    deckClassBadges[i].SetActive(true);
                    deckButtonLabels[i].text = $"{card.operatorData.operatorName}\n{stars}";
                    deckButtons[i].interactable = true;
                    deckCooldownLabels[i].text = card.cooldownRoundsRemaining.ToString();
                    deckCooldownBadges[i].SetActive(!isReady);

                    if (deckHeatBadges != null && i < deckHeatBadges.Length && deckHeatBadges[i] != null)
                    {
                        bool hasHeat = card.heatStacks > 0;
                        deckHeatBadges[i].SetActive(hasHeat);
                        if (hasHeat && deckHeatLabels[i] != null)
                        {
                            deckHeatLabels[i].text = $"🔥{card.heatStacks}";
                        }
                    }

                    // No slot fill (the bay art shows through); the class color only drives the
                    // hover/selection glow and the drag ghost (dimmed while on cooldown).
                    Color slotColor = isReady
                        ? GetClassColor(card.operatorData.operatorClass)
                        : new Color(0.22f, 0.22f, 0.24f, 0.7f);
                    deckFeedbacks[i].SetBaseColor(Color.clear);
                    Color glow = Color.Lerp(slotColor, Color.white, 0.55f);
                    deckFeedbacks[i].glowColor = new Color(glow.r, glow.g, glow.b, 0.9f);

                    int upgradeLevels = Mathf.Clamp((int)card.rarity - (int)card.operatorData.baseRarity, 0, 2);
                    deckUpgradeBadgeLabels[i].SetArrowCount(upgradeLevels);
                    deckUpgradeBadges[i].SetActive(upgradeLevels > 0);
                    deckButtons[i].GetComponent<DeckSlotDragHandler>().SetCard(card, slotColor);
                }
                else
                {
                    Color emptyColor = new Color(0.06f, 0.07f, 0.10f, 0f);
                    deckButtonLabels[i].text = "";
                    deckPortraitImages[i].sprite = null;
                    deckPortraitImages[i].enabled = false;
                    deckClassBadges[i].SetActive(false);
                    deckCooldownLabels[i].text = "";
                    deckCooldownBadges[i].SetActive(false);
                    if (deckHeatBadges != null && i < deckHeatBadges.Length && deckHeatBadges[i] != null)
                    {
                        deckHeatBadges[i].SetActive(false);
                    }
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
            if (gameManager == null || playerDeck == null) return;
            if (!IsDeckDeployPhase && !IsDeckEditOnly) return;

            DraftCard card = playerDeck.GetCard(slotIndex);
            if (card == null) return;

            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (trashDropZone != null &&
                RectTransformUtility.RectangleContainsScreenPoint(trashDropZone, screenPosition, uiCamera))
            {
                stageBootstrapper?.CancelOperatorPlacementPreview(card);
                ShowDiscardConfirmation(slotIndex, card);
                return;
            }

            for (int targetIndex = 0; targetIndex < deckButtons.Length; targetIndex++)
            {
                RectTransform targetRect = deckButtons[targetIndex].GetComponent<RectTransform>();
                if (!RectTransformUtility.RectangleContainsScreenPoint(targetRect, screenPosition, uiCamera)) continue;

                stageBootstrapper?.CancelOperatorPlacementPreview(card);
                playerDeck.MoveCard(slotIndex, targetIndex);
                return;
            }

            if (IsDeckEditOnly || gridManager == null || stageBootstrapper == null) return;
            Camera gameCamera = Camera.main;
            if (gameCamera == null) return;

            Vector3 worldPosition = gameCamera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -gameCamera.transform.position.z));
            Vector2Int gridPosition = gridManager.WorldToGridPosition(worldPosition);
            if (!gridManager.IsInBounds(gridPosition))
            {
                stageBootstrapper.CancelOperatorPlacementPreview(card);
                return;
            }

            GridCell targetCell = gridManager.GetCell(gridPosition);
            if (targetCell != null && targetCell.CanDeploy(card.operatorData.position))
            {
                stageBootstrapper.BeginOperatorPlacement(card, gridPosition);
            }
            else
            {
                stageBootstrapper.CancelOperatorPlacementPreview(card);
            }
        }

        private void HandleDeckCardDrag(int slotIndex, Vector2 screenPosition)
        {
            if (IsDeckEditOnly || playerDeck == null || gridManager == null || stageBootstrapper == null) return;

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
                OperatorClass.Specialist => new Color(0.15f, 0.45f, 0.45f, 1f), // Teal-tinted
                _ => new Color(0.06f, 0.07f, 0.10f, 1f)
            };
        }

        private void SelectDeckSlot(int index)
        {
            if (playerDeck == null || IsDeckEditOnly) return;

            var card = playerDeck.GetCard(index);
            if (card == null) return;
            if (card.cooldownRoundsRemaining > 0)
            {
                ShowCooldownWarning(
                    card.operatorData != null ? card.operatorData.operatorName : null,
                    card.cooldownRoundsRemaining);
                return;
            }

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

        public void SetSelectedDeckSlot(int index)
        {
            selectedDeckSlot = index;
            UpdateDeckSelectionVisuals();
        }

        // ============================
        // Actions
        // ============================

        private void StartWave()
        {
            if (sandboxController != null)
            {
                sandboxController.ToggleWave();
                return;
            }
            if (gameManager == null || waveManager == null) return;
            if (gameManager.CurrentPhase != StagePhase.Preparation) return;

            // Enter wave phase
            gameManager.EnterWavePhase();

            // Set wave index and start
            waveManager.SetWaveIndex(gameManager.GetCurrentWaveIndex());
            waveManager.StartNextWave();
        }

        public void RegisterSandboxController(TrashTD.Development.SandboxController controller)
        {
            sandboxController = controller;
            SetSandboxWaveButton(waveManager != null && waveManager.IsWaveInProgress);
        }

        /// <summary>
        /// Parents the sandbox control panel onto the HUD canvas, below the pause overlay.
        /// </summary>
        public void AttachSandboxPanel(RectTransform panel)
        {
            if (panel == null || canvas == null) return;
            panel.SetParent(canvas.transform, false);
            if (pausePanel != null)
                panel.SetSiblingIndex(pausePanel.transform.GetSiblingIndex());
        }

        public void SetSandboxWaveButton(bool active)
        {
            if (startWaveButton == null) return;
            startWaveButton.interactable = true;
            ApplyButtonContainer(startWaveButton, active ? ButtonContainerType.Danger : ButtonContainerType.Primary);
            if (startWaveButtonText != null)
                startWaveButtonText.text = active ? "END\nWAVE" : "START\nWAVE";
        }

        public bool IsPaused => (pausePanel != null && pausePanel.activeSelf) || (gameManager != null && gameManager.CurrentState == GamePlayState.Paused);
        public bool HasConsumedEscapeThisFrame => Time.frameCount == lastEscapeHandledFrame;

        public void ConsumeEscapeKey()
        {
            lastEscapeHandledFrame = Time.frameCount;
            if (IsFramiliarCopyMenuOpen)
            {
                HideFramiliarCopyMenu();
                if (operatorManager != null) operatorManager.SelectOperator(null);
                return;
            }
        }

        public void TogglePause()
        {
            if (gameManager == null) return;
            if (gameManager.CurrentState == GamePlayState.Paused)
                ResumeGame();
            else if (gameManager.CurrentState == GamePlayState.Playing)
                PauseGame();
        }

        public void HandleEscapeKey()
        {
            if (Time.frameCount == lastEscapeHandledFrame) return;
            lastEscapeHandledFrame = Time.frameCount;
            if (IsFramiliarCopyMenuOpen)
            {
                HideFramiliarCopyMenu();
                if (operatorManager != null) operatorManager.SelectOperator(null);
                return;
            }

#if UNITY_EDITOR || DEBUG
            if (draftOverlayUI != null && draftOverlayUI.IsDeveloperPickerOpen)
            {
                draftOverlayUI.CloseDeveloperPicker();
                return;
            }
#endif

            if (intelUI != null && intelUI.HandleBack())
            {
                return;
            }

            if (audioSettingsUI != null && audioSettingsUI.IsOpen)
            {
                audioSettingsUI.Close();
                return;
            }

            if (IsPaused)
            {
                ResumeGame();
                return;
            }

            if (discardConfirmationPanel != null && discardConfirmationPanel.activeSelf)
            {
                CancelDiscard();
                return;
            }

            if (environmentBriefingPanel != null && environmentBriefingPanel.activeSelf)
            {
                DismissEnvironmentBriefing();
                return;
            }

            if (pathPreviewOverlay != null && pathPreviewOverlay.IsVisible)
            {
                TogglePathPreview();
                return;
            }

            if (stageBootstrapper != null && stageBootstrapper.IsPlacementOrSelectionActive())
            {
                stageBootstrapper.CancelPlacementOrSelection();
                return;
            }

            if (gameManager != null && gameManager.CurrentState == GamePlayState.Playing)
            {
                PauseGame();
            }
        }

        private void HandleKeyboardInput()
        {
            var keyboard = Keyboard.current;
            bool escapePressed = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!escapePressed && Input.GetKeyDown(KeyCode.Escape)) escapePressed = true;
#endif
            if (escapePressed)
            {
                HandleEscapeKey();
            }

            bool pPressed = keyboard != null && keyboard.pKey.wasPressedThisFrame;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!pPressed && Input.GetKeyDown(KeyCode.P)) pPressed = true;
#endif
            if (pPressed && previewPathButton != null && previewPathButton.gameObject.activeInHierarchy)
            {
                TogglePathPreview();
            }
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
            if (audioSettingsUI != null && audioSettingsUI.IsOpen)
            {
                audioSettingsUI.Close();
            }
            if (intelUI != null && intelUI.IsOpen)
            {
                intelUI.Close();
            }
            gameManager.ResumeGame();
            AudioManager.Instance?.PlaySfx(SfxId.GameplayResume);
            SetPauseVisible(false);
        }

        /// <summary>Restarts the current stage on the same difficulty — same flow as the results screen's Retry.</summary>
        private void RetryStage()
        {
            if (gameManager != null)
            {
                MainMenuController.SetPendingStage(gameManager.CurrentStage);
                MainMenuController.SetPendingStageDifficulty(gameManager.CurrentDifficulty);
            }

            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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
            HideFramiliarCopyMenu();
            if (waveText != null)
            {
                waveText.text = total > 0 ? $"{current}/{total}" : current.ToString();
                Punch(waveText);
            }
        }

        private void HandleEnemyCountChanged(EnemyBase enemy)
        {
            RefreshCounters();
        }

        private void HandleLPChanged(int current, int max)
        {
            if (sandboxController != null) return;
            UpdateLivesDisplay(current, max);
        }

        private void UpdateLivesDisplay(int current, int max)
        {
            if (livesTubeRect == null) return;

            if (lastLives >= 0 && current < lastLives) OnLifeLost();
            lastLives = current;

            int total = max > 0 ? max : 3;
            livesTarget = Mathf.Clamp01((float)current / total);
            if (livesDisplayed < 0f)
            {
                livesDisplayed = livesTarget;
                ApplyLivesLiquid(livesDisplayed);
            }
        }

        private void OnLifeLost()
        {
            dangerFlashTime = 0f;
            lifeFlashTime = 0f;
            Punch(livesTubeRect);
            PlaySfx(lifeLostClip, 1f);
        }

        private void RefreshCounters()
        {
            if (enemyText != null && enemyManager != null)
            {
                enemyText.text = enemyManager.ActiveEnemyCount.ToString();
            }

            if (sandboxController != null)
            {
                // Sandbox has no wave count, squad cap or life loss; avoid "x/2147483647" style overflow.
                if (waveText != null && waveManager != null)
                {
                    int currentWave = Mathf.Max(1, waveManager.SandboxWaveNumber);
                    waveText.text = currentWave.ToString();
                }
                if (lpText != null)
                    lpText.text = "LIVES LOCKED";
                if (livesTubeRect != null)
                    livesTubeRect.gameObject.SetActive(false);
                if (squadCountText != null && operatorManager != null)
                {
                    squadCountText.text = $"SQUAD {operatorManager.DeployedCount}";
                    squadCountText.color = Color.white;
                }
                return;
            }

            if (waveText != null && waveManager != null && waveManager.TotalWaves > 0)
            {
                int currentWave = Mathf.Max(1, waveManager.CurrentWaveNumber);
                waveText.text = $"{currentWave}/{waveManager.TotalWaves}";
            }

            if (gameManager != null)
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

            // Top bar background sprite-sheet animation (~12 fps)
            if (topBarBgAnimImage != null && topBarBgSprites != null && topBarBgSprites.Length > 1)
            {
                const float topBarBgFps = 12f;
                topBarBgAnimTime += dt;
                int newFrame = Mathf.FloorToInt(topBarBgAnimTime * topBarBgFps) % topBarBgSprites.Length;
                if (newFrame != topBarBgAnimFrame)
                {
                    topBarBgAnimFrame = newFrame;
                    if (topBarBgSprites[topBarBgAnimFrame] != null)
                        topBarBgAnimImage.sprite = topBarBgSprites[topBarBgAnimFrame];
                }
            }

            // Deck background sprite-sheet animation (~12 fps)
            if (deckBgAnimImage != null && deckBgSprites != null && deckBgSprites.Length > 1)
            {
                const float deckBgFps = 12f;
                deckBgAnimTime += dt;
                int newFrame = Mathf.FloorToInt(deckBgAnimTime * deckBgFps) % deckBgSprites.Length;
                if (newFrame != deckBgAnimFrame)
                {
                    deckBgAnimFrame = newFrame;
                    if (deckBgSprites[deckBgAnimFrame] != null)
                        deckBgAnimImage.sprite = deckBgSprites[deckBgAnimFrame];
                }
            }

            // Bottom Right Panel animated sprite-sheet and speed crossfade
            if (bottomRightBgToImage != null && HasAnyBottomRightSprites())
            {
                const float bottomRightBgFps = 12f;
                bottomRightBgAnimTime += dt;
                int animIndex = Mathf.FloorToInt(bottomRightBgAnimTime * bottomRightBgFps);
                bottomRightBgAnimFrame = animIndex;

                if (bottomRightBgFade < 1f)
                {
                    const float speedGaugeFadeDuration = 0.2f;
                    bottomRightBgFade = Mathf.Min(1f, bottomRightBgFade + dt / speedGaugeFadeDuration);
                    SetImageAlpha(bottomRightBgToImage, Mathf.SmoothStep(0f, 1f, bottomRightBgFade));
                }

                Sprite toSprite = GetBottomRightSprite(bottomRightBgToSpeed, animIndex);
                if (toSprite != null)
                {
                    bottomRightBgToImage.sprite = toSprite;
                }

                if (bottomRightBgFromImage != null)
                {
                    if (bottomRightBgFade < 1f)
                    {
                        Sprite fromSprite = GetBottomRightSprite(bottomRightBgFromSpeed, animIndex);
                        if (fromSprite != null)
                        {
                            bottomRightBgFromImage.sprite = fromSprite;
                        }
                    }
                    else if (toSprite != null)
                    {
                        bottomRightBgFromImage.sprite = toSprite;
                    }
                }
            }

            // Phase color follows the current phase (the progress bar swaps its fill sprite instead)
            if (phaseText != null) phaseText.color = Color.Lerp(phaseText.color, phaseColorTarget, k);

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

            // Lives tube drains toward the new level; its frame flashes red then settles back
            if (livesDisplayed >= 0f && !Mathf.Approximately(livesDisplayed, livesTarget))
            {
                livesDisplayed = Mathf.MoveTowards(livesDisplayed, livesTarget, dt * 1.5f);
                ApplyLivesLiquid(livesDisplayed);
            }

            if (lifeFlashTime >= 0f && livesTubeFrame != null)
            {
                lifeFlashTime += dt;
                float lk = Mathf.Clamp01(lifeFlashTime / 0.6f);
                livesTubeFrame.color = Color.Lerp(LifeColor, Color.white, lk);
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
            ButtonContainerType container = ResolveContainerType(background);
            return StyleButton(button, container, glow, hoverScale, clicked);
        }

        private MenuButtonFeedback StyleButton(Button button, ButtonContainerType containerType, Color glow, float hoverScale = 1.05f, System.Action clicked = null)
        {
            ApplyButtonContainer(button, containerType);
            return AttachFeedback(button, glow, hoverScale, clicked);
        }

        private static ButtonContainerType ResolveContainerType(Color color)
        {
            if (color == ReadyColor || color == AccentColor || (color.g > 0.4f && color.g > color.r && color.g > color.b))
                return ButtonContainerType.Primary;
            if (color == LifeColor || (color.r > 0.35f && color.g < 0.25f && color.b < 0.25f))
                return ButtonContainerType.Danger;
            if (color == PrepColor)
                return ButtonContainerType.Secondary;
            if (color == DraftColor)
                return ButtonContainerType.Warning;
            return ButtonContainerType.Dark;
        }

        public static Sprite GetButtonContainerSprite(ButtonContainerType type)
        {
            if (ButtonContainerSprites.TryGetValue(type, out Sprite cached) && cached != null)
                return cached;

            string path = type switch
            {
                ButtonContainerType.Primary => "UI/buttons/btn_primary_normal",
                ButtonContainerType.Secondary => "UI/buttons/btn_secondary_normal",
                ButtonContainerType.Dark => "UI/buttons/btn_dark_normal",
                ButtonContainerType.Danger => "UI/buttons/btn_danger_normal",
                ButtonContainerType.Warning => "UI/buttons/btn_warning_normal",
                ButtonContainerType.IconDark => "UI/buttons/btn_icon_dark_normal",
                ButtonContainerType.IconDanger => "UI/buttons/btn_icon_danger_normal",
                ButtonContainerType.IconPrimary => "UI/buttons/btn_icon_primary_normal",
                ButtonContainerType.IconSecondary => "UI/buttons/btn_icon_secondary_normal",
                _ => "UI/buttons/btn_dark_normal"
            };

            Sprite sprite = LoadButtonSprite(path);
            if (sprite != null)
            {
                ButtonContainerSprites[type] = sprite;
            }
            return sprite;
        }

        private static Sprite LoadButtonSprite(string path)
        {
            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                Sprite[] all = Resources.LoadAll<Sprite>(path);
                if (all != null && all.Length > 0) sprite = all[0];
            }

            if (sprite == null)
            {
                var tex = Resources.Load<Texture2D>(path);
                if (tex != null)
                {
                    tex.filterMode = FilterMode.Point;
                    sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(5f, 5f, 5f, 5f));
                }
            }
            else if (sprite.border == Vector4.zero && sprite.texture != null)
            {
                sprite = Sprite.Create(sprite.texture, sprite.rect, sprite.pivot, 100f, 0, SpriteMeshType.FullRect, new Vector4(5f, 5f, 5f, 5f));
            }

            return sprite;
        }

        public static void ApplyButtonContainer(Button button, ButtonContainerType type)
        {
            if (button == null) return;
            Image image = button.GetComponent<Image>();
            if (image == null) return;

            Sprite sprite = GetButtonContainerSprite(type);
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                image.color = Color.white;
            }

            var feedback = button.GetComponent<MenuButtonFeedback>();
            if (feedback != null)
            {
                feedback.SetBaseColor(Color.white);
            }
        }

        public static Sprite GetPanelContainerSprite(PanelContainerType type)
        {
            if (PanelContainerSprites.TryGetValue(type, out Sprite cached) && cached != null)
                return cached;

            string filename = type switch
            {
                PanelContainerType.Dark => "panel_dark",
                PanelContainerType.Metal => "panel_metal",
                PanelContainerType.Glass => "panel_glass",
                PanelContainerType.Modal => "panel_modal",
                PanelContainerType.Rust => "panel_rust",
                PanelContainerType.Teal => "panel_teal",
                PanelContainerType.Tooltip => "panel_tooltip",
                _ => "panel_dark"
            };

            Vector4 border = type switch
            {
                PanelContainerType.Metal => new Vector4(16f, 16f, 16f, 16f),
                PanelContainerType.Rust => new Vector4(16f, 16f, 16f, 16f),
                PanelContainerType.Modal => new Vector4(12f, 14f, 12f, 14f),
                PanelContainerType.Teal => new Vector4(12f, 12f, 12f, 12f),
                PanelContainerType.Tooltip => new Vector4(10f, 10f, 10f, 10f),
                PanelContainerType.Glass => new Vector4(10f, 10f, 10f, 10f),
                _ => new Vector4(12f, 12f, 12f, 12f)
            };

            Sprite sprite = LoadPanelSprite("UI/panels/" + filename, border);
            if (sprite != null)
            {
                PanelContainerSprites[type] = sprite;
            }
            return sprite;
        }

        private static Sprite LoadPanelSprite(string path, Vector4 border)
        {
            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                Sprite[] all = Resources.LoadAll<Sprite>(path);
                if (all != null && all.Length > 0) sprite = all[0];
            }

            if (sprite == null)
            {
                var tex = Resources.Load<Texture2D>(path);
                if (tex != null)
                {
                    tex.filterMode = FilterMode.Point;
                    sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
                }
            }
            else if (sprite.border == Vector4.zero && sprite.texture != null)
            {
                sprite = Sprite.Create(sprite.texture, sprite.rect, sprite.pivot, 100f, 0, SpriteMeshType.FullRect, border);
            }

            return sprite;
        }

        private static readonly Dictionary<string, Sprite> WholeTextureSprites = new Dictionary<string, Sprite>();

        /// <summary>
        /// Loads a UI texture from Resources as one Point-filtered sprite covering the whole image, ignoring any
        /// slicing in its importer, with an optional 9-slice border in source pixels. Cached; null if missing.
        /// </summary>
        private static Sprite LoadWholeTextureSprite(string path, Vector4 border = default)
        {
            if (WholeTextureSprites.TryGetValue(path, out Sprite cached) && cached != null)
                return cached;

            var tex = Resources.Load<Texture2D>(path);
            if (tex == null) return null;

            tex.filterMode = FilterMode.Point;
            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            WholeTextureSprites[path] = sprite;
            return sprite;
        }

        private static Sprite hazardBarSprite;

        private static Sprite GetHazardBarSprite()
        {
            if (hazardBarSprite != null) return hazardBarSprite;

            hazardBarSprite = Resources.Load<Sprite>("UI/decor/hazard_bar");
            if (hazardBarSprite == null)
            {
                Sprite[] all = Resources.LoadAll<Sprite>("UI/decor/hazard_bar");
                if (all != null && all.Length > 0) hazardBarSprite = all[0];
            }

            if (hazardBarSprite == null)
            {
                var tex = Resources.Load<Texture2D>("UI/decor/hazard_bar");
                if (tex != null)
                {
                    tex.filterMode = FilterMode.Point;
                    hazardBarSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                }
            }

            return hazardBarSprite;
        }

        private static void AttachHazardDecor(Transform card, float insetX = 16f, float insetY = 8f, float barHeight = 20f)
        {
            if (card == null) return;
            if (card.Find("TopHazardBar") != null || card.Find("BottomHazardBar") != null) return;

            Sprite sprite = GetHazardBarSprite();
            if (sprite == null) return;

            // Top Hazard Bar
            var topBar = new GameObject("TopHazardBar", typeof(RectTransform), typeof(Image));
            topBar.transform.SetParent(card, false);
            var topRect = topBar.GetComponent<RectTransform>();
            topRect.anchorMin = new Vector2(0f, 1f);
            topRect.anchorMax = new Vector2(1f, 1f);
            topRect.pivot = new Vector2(0.5f, 1f);
            topRect.anchoredPosition = new Vector2(0f, -insetY);
            topRect.sizeDelta = new Vector2(-insetX * 2f, barHeight);
            var topImage = topBar.GetComponent<Image>();
            topImage.sprite = sprite;
            topImage.type = Image.Type.Tiled;
            topImage.color = Color.white;
            topImage.raycastTarget = false;

            // Bottom Hazard Bar
            var bottomBar = new GameObject("BottomHazardBar", typeof(RectTransform), typeof(Image));
            bottomBar.transform.SetParent(card, false);
            var bottomRect = bottomBar.GetComponent<RectTransform>();
            bottomRect.anchorMin = new Vector2(0f, 0f);
            bottomRect.anchorMax = new Vector2(1f, 0f);
            bottomRect.pivot = new Vector2(0.5f, 0f);
            bottomRect.anchoredPosition = new Vector2(0f, insetY);
            bottomRect.sizeDelta = new Vector2(-insetX * 2f, barHeight);
            var bottomImage = bottomBar.GetComponent<Image>();
            bottomImage.sprite = sprite;
            bottomImage.type = Image.Type.Tiled;
            bottomImage.color = Color.white;
            bottomImage.raycastTarget = false;
        }

        private static void ApplyModalCard(GameObject card, float insetX = 16f, float insetY = 8f, float barHeight = 20f)
        {
            if (card == null) return;
            ApplyPanelContainer(card, PanelContainerType.Metal);
            AttachHazardDecor(card.transform, insetX, insetY, barHeight);
        }

        public static void ApplyPanelContainer(GameObject target, PanelContainerType type, Color? tint = null)
        {
            if (target == null) return;
            if (type == PanelContainerType.Modal)
            {
                ApplyModalCard(target);
                return;
            }

            var image = target.GetComponent<Image>();
            if (image == null) return;

            Sprite sprite = GetPanelContainerSprite(type);
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                image.color = tint ?? Color.white;
            }
        }

        private static Button CreateButton(Transform parent, string objectName, string label, Vector2 size, ButtonContainerType containerType = ButtonContainerType.Dark)
        {
            var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var button = buttonObject.GetComponent<Button>();
            buttonObject.GetComponent<RectTransform>().sizeDelta = size;

            ApplyButtonContainer(button, containerType);

            var text = CreateText(buttonObject.transform, "Label", label, 22, TextAnchor.MiddleCenter);
            text.raycastTarget = false;
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return button;
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

        private static void SetPosition(RectTransform rect, Vector2 position, Vector2 anchor, Vector2? size = null, Vector2? pivot = null)
        {
            if (pivot.HasValue) rect.pivot = pivot.Value;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            if (size.HasValue) rect.sizeDelta = size.Value;
        }

        private static Image CreateIconImage(Transform parent, string objectName, Sprite sprite, Color color, Vector2 position, Vector2 anchor, Vector2 size, Vector2 pivot)
        {
            var iconObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(parent, false);
            Image image = iconObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            SetPosition(image.rectTransform, position, anchor, size, pivot);
            return image;
        }

        public static Sprite LoadIconSprite(string iconName)
        {
            var sprite = Resources.Load<Sprite>("UI/icons/" + iconName);
            if (sprite != null) return sprite;

            var tex = Resources.Load<Texture2D>("UI/icons/" + iconName);
            if (tex != null)
            {
                tex.filterMode = FilterMode.Point;
                return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), Mathf.Max(tex.width, tex.height));
            }

            return null;
        }

        private static Sprite GetPauseIconSprite()
        {
            if (pauseIconSprite != null) return pauseIconSprite;
            pauseIconSprite = LoadIconSprite("icon_pause");
            return pauseIconSprite;
        }

        private static Sprite GetExitIconSprite()
        {
            if (exitIconSprite != null) return exitIconSprite;
            exitIconSprite = LoadIconSprite("icon_exit");
            return exitIconSprite;
        }

        private static Sprite GetWaveMarkSprite()
        {
            if (waveMarkSprite != null) return waveMarkSprite;

            waveMarkSprite = LoadIconSprite("icon_wave");
            if (waveMarkSprite != null) return waveMarkSprite;

            const int size = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[size * size];

            for (int x = 0; x < size; x++)
            {
                float t = x / (size - 1f);
                float centerY = size * 0.5f + Mathf.Sin(t * Mathf.PI * 2f) * size * 0.18f;
                for (int y = 0; y < size; y++)
                {
                    if (x >= 4 && x < size - 4 && Mathf.Abs(y - centerY) <= 2.5f)
                        pixels[y * size + x] = Color.white;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            waveMarkSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            return waveMarkSprite;
        }

        private static Sprite GetEnemySkullSprite()
        {
            if (enemySkullSprite != null) return enemySkullSprite;

            enemySkullSprite = LoadIconSprite("icon_skull");
            if (enemySkullSprite != null) return enemySkullSprite;

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size;
                    float ny = (y + 0.5f) / size;
                    float headX = (nx - 0.5f) / 0.35f;
                    float headY = (ny - 0.63f) / 0.32f;
                    float jawX = (nx - 0.5f) / 0.23f;
                    float jawY = (ny - 0.35f) / 0.15f;
                    bool skull = headX * headX + headY * headY <= 1f ||
                                 (nx >= 0.29f && nx <= 0.71f && ny >= 0.25f && ny <= 0.43f) ||
                                 jawX * jawX + jawY * jawY <= 1f;

                    if (!skull) continue;

                    float leftEyeX = (nx - 0.37f) / 0.075f;
                    float rightEyeX = (nx - 0.63f) / 0.075f;
                    float eyeY = (ny - 0.59f) / 0.09f;
                    bool eyeSocket = leftEyeX * leftEyeX + eyeY * eyeY <= 1f ||
                                     rightEyeX * rightEyeX + eyeY * eyeY <= 1f;
                    float noseWidth = Mathf.Lerp(0.07f, 0.01f, Mathf.Clamp01((ny - 0.43f) / 0.08f));
                    bool nose = ny >= 0.43f && ny <= 0.51f && Mathf.Abs(nx - 0.5f) <= noseWidth;
                    bool teethGap = ny >= 0.22f && ny <= 0.31f &&
                                    (Mathf.Abs(nx - 0.44f) <= 0.012f ||
                                     Mathf.Abs(nx - 0.50f) <= 0.012f ||
                                     Mathf.Abs(nx - 0.56f) <= 0.012f);

                    pixels[y * size + x] = eyeSocket || nose || teethGap ? Color.clear : Color.white;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            enemySkullSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            return enemySkullSprite;
        }

        private static Sprite CreateTrashIconSprite()
        {
            if (trashIconSprite != null) return trashIconSprite;

            trashIconSprite = LoadIconSprite("icon_discard");
            if (trashIconSprite != null) return trashIconSprite;

            const int width = 24;
            const int height = 24;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            void Pixel(int x, int y)
            {
                if (x >= 0 && x < width && y >= 0 && y < height)
                    pixels[y * width + x] = Color.white;
            }

            // Main bin body: wide rim, slightly tapered sides, and a solid base.
            for (int y = 4; y <= 17; y++)
            {
                int left = y <= 6 ? 4 : 5;
                int right = y <= 6 ? 19 : 18;
                for (int x = left; x <= right; x++) Pixel(x, y);
            }
            for (int x = 6; x <= 17; x++) Pixel(x, 3);

            // Lid and handle at the top of the can.
            for (int x = 3; x <= 20; x++) Pixel(x, 18);
            for (int x = 7; x <= 16; x++) Pixel(x, 20);
            for (int x = 9; x <= 14; x++) Pixel(x, 21);

            // Three vertical cut-outs make the bin read clearly at small size.
            for (int y = 7; y <= 15; y++)
            {
                pixels[y * width + 8] = Color.clear;
                pixels[y * width + 11] = Color.clear;
                pixels[y * width + 14] = Color.clear;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 24f);
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
            var eventSystem = FindAnyObjectByType<EventSystem>();
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