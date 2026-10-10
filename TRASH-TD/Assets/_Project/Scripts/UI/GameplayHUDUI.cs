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
        private static Sprite cooldownClockSprite;
        private static Sprite waveMarkSprite;
        private static Sprite enemySkullSprite;
        private static readonly Dictionary<OperatorClass, Sprite> ClassIconSprites = new Dictionary<OperatorClass, Sprite>();

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
        private Text environmentBriefingTitle;
        private Text environmentBriefingDescription;
        private bool environmentBriefingDismissed;
        private Text selectedOperatorNameText;
        private Text selectedOperatorSkillDescriptionText;
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
        private TrashTD.Development.SandboxController sandboxController;

        private int selectedDeckSlot = -1;
        public bool IsEnvironmentBriefingActive => environmentBriefingPanel != null && environmentBriefingPanel.activeSelf;
        public bool ShouldBlockDraftOverlay
        {
            get
            {
                if (environmentBriefingDismissed || gameManager == null || gameManager.CurrentStage == null) return false;
                string stageId = gameManager.CurrentStage.stageId;
                return stageId == "STAGE_03" || stageId == "STAGE_06";
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
        private Image dangerFlashImage;
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
            sandboxController = FindFirstObjectByType<TrashTD.Development.SandboxController>();

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            if (hoverClip == null) hoverClip = AudioManager.Instance?.GetClip(SfxId.UiHover);
            if (clickClip == null) clickClip = AudioManager.Instance?.GetClip(SfxId.UiClick);
            if (waveStartClip == null) waveStartClip = AudioManager.Instance?.GetClip(SfxId.GameplayWaveStart);
            if (lifeLostClip == null) lifeLostClip = AudioManager.Instance?.GetClip(SfxId.GameplayLifeLost);

            EnsureEventSystem();
            BuildHud();
            if (canvas != null && pausePanel != null)
            {
                Transform pauseCard = pausePanel.transform.Find("PauseCard");
                if (pauseCard != null)
                {
                    AudioSettingsUI.Create(
                        canvas.transform,
                        pauseCard,
                        button => StyleButton(button, DarkButton, TextDim, 1.05f),
                        rect => SetPosition(rect, new Vector2(0f, -326f), new Vector2(0.5f, 1f), new Vector2(340f, 60f), new Vector2(0.5f, 0.5f)));
                }
            }
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
            StartCoroutine(ShowEnvironmentBriefingWhenReady());
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
            CreateSpeedBar(root);
            CreateTrashDropZone(root);
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
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(250f, 104f);

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

            selectedOperatorShowMoreButton = CreateButton(selectedOperatorLabelRoot.transform, "ShowOperatorDetailsButton", "SHOW MORE", new Vector2(102f, 24f));
            SetPosition(selectedOperatorShowMoreButton.GetComponent<RectTransform>(), new Vector2(0f, -112f), new Vector2(0.5f, 1f), null, new Vector2(0.5f, 1f));
            Text showMoreLabel = selectedOperatorShowMoreButton.GetComponentInChildren<Text>();
            showMoreLabel.fontSize = 11;
            showMoreLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            showMoreLabel.verticalOverflow = VerticalWrapMode.Truncate;
            StyleButton(selectedOperatorShowMoreButton, DarkButton, PrepColor, 1.08f);
            selectedOperatorShowMoreButton.onClick.AddListener(ToggleSelectedOperatorDetails);

            selectedOperatorAtkText = CreateText(selectedOperatorLabelRoot.transform, "Atk", string.Empty, 14, TextAnchor.MiddleLeft);
            selectedOperatorAtkText.color = new Color(1f, 0.72f, 0.35f, 1f);
            SetPosition(selectedOperatorAtkText.GetComponent<RectTransform>(), new Vector2(10f, 26f), Vector2.zero, new Vector2(120f, 20f), Vector2.zero);
            selectedOperatorAtkText.raycastTarget = false;

            selectedOperatorHealthText = CreateText(selectedOperatorLabelRoot.transform, "Health", string.Empty, 14, TextAnchor.MiddleLeft);
            SetPosition(selectedOperatorHealthText.GetComponent<RectTransform>(), new Vector2(10f, 6f), Vector2.zero, new Vector2(88f, 20f), Vector2.zero);
            selectedOperatorHealthText.raycastTarget = false;

            GameObject healthTrack = new GameObject("HealthTrack", typeof(RectTransform), typeof(Image));
            healthTrack.transform.SetParent(selectedOperatorLabelRoot.transform, false);
            Image healthTrackImage = healthTrack.GetComponent<Image>();
            healthTrackImage.color = new Color(0.15f, 0.17f, 0.19f, 1f);
            healthTrackImage.raycastTarget = false;
            SetPosition(healthTrack.GetComponent<RectTransform>(), new Vector2(102f, 10f), Vector2.zero, new Vector2(138f, 12f), Vector2.zero);

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
            panelImage.color = new Color(0.045f, 0.055f, 0.075f, 0.96f);
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

            selectedOperatorDetailsChipBackgrounds = new Image[3];
            selectedOperatorDetailsChipLabels = new Text[3];
            selectedOperatorDetailsChipOutlines = new Outline[3];
            float detailChipWidth = (detailsInnerWidth - 24f) / 3f;
            for (int i = 0; i < selectedOperatorDetailsChipLabels.Length; i++)
            {
                var chip = new GameObject($"TypeChip_{i}", typeof(RectTransform), typeof(Image), typeof(Outline));
                chip.transform.SetParent(content, false);
                float chipX = detailsMargin + (detailChipWidth + 12f) * i;
                SetPosition(chip.GetComponent<RectTransform>(), new Vector2(chipX, -114f), new Vector2(0f, 1f), new Vector2(detailChipWidth, 34f), new Vector2(0f, 1f));
                selectedOperatorDetailsChipBackgrounds[i] = chip.GetComponent<Image>();
                selectedOperatorDetailsChipBackgrounds[i].color = new Color(0.07f, 0.085f, 0.115f, 1f);
                selectedOperatorDetailsChipBackgrounds[i].raycastTarget = false;
                selectedOperatorDetailsChipOutlines[i] = chip.GetComponent<Outline>();
                selectedOperatorDetailsChipOutlines[i].effectDistance = new Vector2(1.5f, -1.5f);
                selectedOperatorDetailsChipOutlines[i].effectColor = new Color(1f, 1f, 1f, 0.2f);

                if (i == 0)
                {
                    var iconObject = new GameObject("ClassIcon", typeof(RectTransform), typeof(Image));
                    iconObject.transform.SetParent(chip.transform, false);
                    selectedOperatorDetailsClassIcon = iconObject.GetComponent<Image>();
                    selectedOperatorDetailsClassIcon.preserveAspect = true;
                    selectedOperatorDetailsClassIcon.raycastTarget = false;
                    SetPosition(selectedOperatorDetailsClassIcon.rectTransform, new Vector2(8f, 0f), new Vector2(0f, 0.5f), new Vector2(22f, 22f), new Vector2(0f, 0.5f));
                }

                selectedOperatorDetailsChipLabels[i] = CreateText(chip.transform, $"Label_{i}", string.Empty, 13, TextAnchor.MiddleCenter);
                selectedOperatorDetailsChipLabels[i].fontStyle = FontStyle.Bold;
                selectedOperatorDetailsChipLabels[i].resizeTextForBestFit = true;
                selectedOperatorDetailsChipLabels[i].resizeTextMinSize = 9;
                selectedOperatorDetailsChipLabels[i].resizeTextMaxSize = 13;
                selectedOperatorDetailsChipLabels[i].raycastTarget = false;
                RectTransform chipTextRect = selectedOperatorDetailsChipLabels[i].rectTransform;
                chipTextRect.anchorMin = Vector2.zero;
                chipTextRect.anchorMax = Vector2.one;
                chipTextRect.offsetMin = new Vector2(i == 0 ? 32f : 0f, 0f);
                chipTextRect.offsetMax = new Vector2(-4f, 0f);
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
                selectedOperatorDetailsPanel.SetActive(false);
                selectedOperatorDetailsTarget = null;
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
                selectedOperatorShowMoreButton.gameObject.SetActive(true);
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
            int currentATK,
            OperatorRarity rarity,
            OperatorRarity baseRarity,
            string skillDescription,
            Vector3 worldPosition)
        {
            SetSelectedOperatorName(operatorName, worldPosition);
            if (string.IsNullOrEmpty(operatorName)) return;

            bool hasSkillDescription = !string.IsNullOrWhiteSpace(skillDescription);
            selectedOperatorLabelRoot.GetComponent<RectTransform>().sizeDelta = hasSkillDescription
                ? new Vector2(300f, 220f)
                : new Vector2(300f, 170f);
            selectedOperatorSkillDescriptionText.text = skillDescription;
            selectedOperatorSkillDescriptionText.gameObject.SetActive(hasSkillDescription);
            SetPosition(
                selectedOperatorShowMoreButton.GetComponent<RectTransform>(),
                new Vector2(0f, hasSkillDescription ? -126f : -58f),
                new Vector2(0.5f, 1f),
                new Vector2(102f, 24f),
                new Vector2(0.5f, 1f));

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
            string skillDescription,
            Vector3 worldPosition)
        {
            SetSelectedOperatorName(operatorName, currentHP, maxHP, 0, rarity, baseRarity, skillDescription, worldPosition);
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

            selectedOperatorDetailsTarget = selectedOperator;
            Vector3 labelPosition = selectedOperator.DeployedCell.WorldPosition + Vector3.up * gridManager.CellSize * 0.7f;
            SetSelectedOperatorName(
                selectedOperator.Data.operatorName,
                selectedOperator.CurrentHP,
                selectedOperator.MaxHP,
                selectedOperator.CurrentATK,
                selectedOperator.CurrentRarity,
                selectedOperator.Data.baseRarity,
                selectedOperator.Data.skillDescription,
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
            bool hasSkill = !string.IsNullOrWhiteSpace(data.skillDescription);
            selectedOperatorDetailsSkillHeading.gameObject.SetActive(hasSkill);
            selectedOperatorDetailsSkill.gameObject.SetActive(hasSkill);
            if (hasSkill) selectedOperatorDetailsSkill.text = data.skillDescription.Trim();
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

            phaseHeaderText = CreateText(stageInfoPanel.transform, "PhaseHeaderText", "PHASE", 28, TextAnchor.MiddleLeft);
            SetPosition(phaseHeaderText.GetComponent<RectTransform>(), new Vector2(95f, -28f), new Vector2(0f, 1f), new Vector2(220f, 32f), new Vector2(0f, 0.5f));

            // Phase text remains directly below the wave title.
            phaseText = CreateText(stageInfoPanel.transform, "PhaseText", "PREPARATION", 18, TextAnchor.MiddleLeft);
            phaseText.color = PrepColor;
            SetPosition(phaseText.GetComponent<RectTransform>(), new Vector2(95f, -60f), new Vector2(0f, 1f), new Vector2(220f, 26f), new Vector2(0f, 0.5f));

            // The wave counter replaces the squad count's former top-bar position.
            waveIconImage = CreateIconImage(stageInfoPanel.transform, "WaveIcon", GetWaveMarkSprite(), WaveColor, new Vector2(350f, -45f), new Vector2(0f, 1f), new Vector2(44f, 42f), new Vector2(0f, 0.5f));
            waveText = CreateText(stageInfoPanel.transform, "WaveText", "1/1", 26, TextAnchor.MiddleLeft);
            SetPosition(waveText.GetComponent<RectTransform>(), new Vector2(398f, -45f), new Vector2(0f, 1f), new Vector2(150f, 40f), new Vector2(0f, 0.5f));

            // Enemy count (center) uses a skull icon and a numeric count.
            enemyIconImage = CreateIconImage(stageInfoPanel.transform, "EnemyIcon", GetEnemySkullSprite(), Color.white, new Vector2(-35f, -45f), new Vector2(0.5f, 1f), new Vector2(36f, 36f), new Vector2(0.5f, 0.5f));
            enemyText = CreateText(stageInfoPanel.transform, "EnemyText", "0", 24, TextAnchor.MiddleLeft);
            SetPosition(enemyText.GetComponent<RectTransform>(), new Vector2(8f, -45f), new Vector2(0.5f, 1f), new Vector2(100f, 40f), new Vector2(0f, 0.5f));

            // Lives Counter (top-right, 3 lives)
            lpText = CreateText(stageInfoPanel.transform, "LivesText", "♥ ♥ ♥", 24, TextAnchor.MiddleRight);
            lpText.color = LifeColor;
            SetPosition(lpText.GetComponent<RectTransform>(), new Vector2(-25f, -45f), new Vector2(1f, 1f), new Vector2(280f, 45f), new Vector2(1f, 0.5f));

            // Texts and progress strip should never eat clicks
            phaseHeaderText.raycastTarget = false;
            waveText.raycastTarget = false;
            phaseText.raycastTarget = false;
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
            slotsRect.offsetMax = new Vector2(-420f, -15f);

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
            deckPortraitImages = new Image[DeckSlotCount];
            deckClassBadgeImages = new Image[DeckSlotCount];
            deckClassIconImages = new Image[DeckSlotCount];
            deckClassBadges = new GameObject[DeckSlotCount];
            deckCooldownBadges = new GameObject[DeckSlotCount];
            deckCooldownLabels = new Text[DeckSlotCount];
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
                deckPortraitImages[i] = slotBtn.transform.Find("OperatorPortrait").GetComponent<Image>();
                deckClassBadges[i] = CreateClassBadge(slotBtn.transform, out deckClassBadgeImages[i], out deckClassIconImages[i]);
                deckCooldownBadges[i] = CreateCooldownBadge(
                    slotBtn.transform,
                    out _,
                    out deckCooldownLabels[i]);
                deckUpgradeBadges[i] = CreateUpgradeBadge(slotBtn.transform, $"DeckUpgradeBadge_{i + 1}", new Vector2(34f, 18f), new Vector2(-5f, -5f));
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

            squadCountText = CreateText(bar.transform, "SquadCountText", "SQUAD 0/8", 16, TextAnchor.MiddleRight);
            RectTransform squadRect = squadCountText.GetComponent<RectTransform>();
            squadRect.anchorMin = new Vector2(1f, 0.5f);
            squadRect.anchorMax = new Vector2(1f, 0.5f);
            squadRect.pivot = new Vector2(1f, 0.5f);
            squadRect.sizeDelta = new Vector2(195f, 54f);
            squadRect.anchoredPosition = new Vector2(-205f, 0f);
            squadCountText.raycastTarget = false;
        }

        private void CreateSpeedBar(Transform root)
        {
            var bar = new GameObject("GameSpeedBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(root, false);
            var barRect = bar.GetComponent<RectTransform>();
            // Sits just above the 170px deck bar so it never covers the Start Wave button.
            SetPosition(barRect, new Vector2(-24f, 184f), new Vector2(1f, 0f), new Vector2(246f, 54f), new Vector2(1f, 0f));
            bar.GetComponent<Image>().color = BarColor;

            var outline = bar.AddComponent<Outline>();
            outline.effectColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.35f);
            outline.effectDistance = new Vector2(2f, -2f);

            var title = CreateText(bar.transform, "SpeedTitle", "SPEED", 14, TextAnchor.MiddleCenter);
            title.color = TextDim;
            title.raycastTarget = false;
            SetPosition(title.GetComponent<RectTransform>(), new Vector2(0f, 15f), new Vector2(0.5f, 1f), new Vector2(246f, 20f), new Vector2(0.5f, 1f));

            var buttonsRoot = new GameObject("SpeedButtons", typeof(RectTransform));
            buttonsRoot.transform.SetParent(bar.transform, false);
            var buttonsRect = buttonsRoot.GetComponent<RectTransform>();
            SetPosition(buttonsRect, new Vector2(0f, -7f), new Vector2(0.5f, 0.5f), new Vector2(216f, 32f), new Vector2(0.5f, 0.5f));
            var layout = buttonsRoot.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
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
                var button = CreateButton(buttonsRoot.transform, $"Speed{speed:0}xButton", $"{speed:0}x", new Vector2(66f, 30f));
                button.GetComponent<Image>().color = DarkButton;
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

        private void CreateTrashDropZone(Transform root)
        {
            var zone = new GameObject("TrashDropZone", typeof(RectTransform), typeof(Image), typeof(Button));
            zone.transform.SetParent(root, false);
            trashDropZone = zone.GetComponent<RectTransform>();
            SetPosition(trashDropZone, new Vector2(1034f, 85f), Vector2.zero,
                new Vector2(78f, 92f), new Vector2(0f, 0.5f));

            var image = zone.GetComponent<Image>();
            image.color = new Color(0.10f, 0.07f, 0.09f, 0.96f);
            image.raycastTarget = true;
            var outline = zone.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.25f, 0.3f, 0.55f);
            outline.effectDistance = new Vector2(2f, -2f);

            var icon = new GameObject("TrashIcon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(zone.transform, false);
            var iconRect = icon.GetComponent<RectTransform>();
            SetPosition(iconRect, new Vector2(0f, 14f), new Vector2(0.5f, 0.5f),
                new Vector2(38f, 42f), new Vector2(0.5f, 0.5f));
            var iconImage = icon.GetComponent<Image>();
            iconImage.sprite = CreateTrashIconSprite();
            iconImage.color = new Color(1f, 0.35f, 0.38f, 1f);
            iconImage.raycastTarget = false;

            var discardLabel = CreateText(zone.transform, "DiscardLabel", "DISCARD", 10, TextAnchor.MiddleCenter);
            discardLabel.color = new Color(1f, 0.78f, 0.78f, 1f);
            discardLabel.raycastTarget = false;
            SetPosition(discardLabel.GetComponent<RectTransform>(), new Vector2(0f, -31f), new Vector2(0.5f, 0.5f),
                new Vector2(74f, 20f), new Vector2(0.5f, 0.5f));

            var button = zone.GetComponent<Button>();
            button.interactable = false;
            AttachFeedback(button, new Color(1f, 0.3f, 0.35f, 1f), 1.04f);
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
            SetPosition(cardRect, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(470f, 240f), new Vector2(0.5f, 0.5f));
            card.GetComponent<Image>().color = DarkButton;
            var cardOutline = card.AddComponent<Outline>();
            cardOutline.effectColor = AccentColor;
            cardOutline.effectDistance = new Vector2(2f, -2f);

            var title = CreateText(card.transform, "Title", "DISCARD OPERATOR?", 24, TextAnchor.MiddleCenter);
            SetPosition(title.GetComponent<RectTransform>(), new Vector2(0f, 74f), new Vector2(0.5f, 0.5f),
                new Vector2(430f, 40f), new Vector2(0.5f, 0.5f));
            var message = CreateText(card.transform, "Message", string.Empty, 17, TextAnchor.MiddleCenter);
            message.color = TextDim;
            message.name = "Message";
            SetPosition(message.GetComponent<RectTransform>(), new Vector2(0f, 32f), new Vector2(0.5f, 0.5f),
                new Vector2(430f, 32f), new Vector2(0.5f, 0.5f));

            var confirm = CreateButton(card.transform, "ConfirmDiscardButton", "DISCARD", new Vector2(175f, 52f));
            SetPosition(confirm.GetComponent<RectTransform>(), new Vector2(-100f, -65f), new Vector2(0.5f, 0.5f),
                new Vector2(175f, 52f), new Vector2(0.5f, 0.5f));
            StyleButton(confirm, new Color(0.38f, 0.10f, 0.12f, 1f), LifeColor, 1.05f,
                ConfirmDiscard);

            var cancel = CreateButton(card.transform, "CancelDiscardButton", "CANCEL", new Vector2(175f, 52f));
            SetPosition(cancel.GetComponent<RectTransform>(), new Vector2(100f, -65f), new Vector2(0.5f, 0.5f),
                new Vector2(175f, 52f), new Vector2(0.5f, 0.5f));
            StyleButton(cancel, DarkButton, TextDim, 1.05f, CancelDiscard);

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
            for (int i = 0; i < speedButtons.Length; i++)
            {
                bool selected = i == selectedIndex;
                var image = speedButtons[i].GetComponent<Image>();
                image.color = selected ? new Color(0.08f, 0.28f, 0.16f, 1f) : DarkButton;
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
            var button = CreateButton(parent, objectName, "", new Vector2(115f, 130f));
            var image = button.GetComponent<Image>();
            image.sprite = CreateTrapezoidSprite();
            image.color = new Color(0.06f, 0.07f, 0.10f, 1f);
            image.type = Image.Type.Simple;

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.fontSize = 12;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 9;
                label.resizeTextMaxSize = 12;
                label.color = Color.white;

                RectTransform labelRect = label.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0.04f, 0.02f);
                labelRect.anchorMax = new Vector2(0.96f, 0.30f);
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
            }

            var portraitObject = new GameObject("OperatorPortrait", typeof(RectTransform), typeof(Image));
            portraitObject.transform.SetParent(button.transform, false);
            RectTransform portraitRect = portraitObject.GetComponent<RectTransform>();
            portraitRect.anchorMin = new Vector2(0.08f, 0.32f);
            portraitRect.anchorMax = new Vector2(0.92f, 0.94f);
            portraitRect.offsetMin = Vector2.zero;
            portraitRect.offsetMax = Vector2.zero;

            Image portraitImage = portraitObject.GetComponent<Image>();
            portraitImage.preserveAspect = true;
            portraitImage.raycastTarget = false;

            var labelBackground = new GameObject("OperatorLabelBackground", typeof(RectTransform), typeof(Image));
            labelBackground.transform.SetParent(button.transform, false);
            RectTransform labelBackgroundRect = labelBackground.GetComponent<RectTransform>();
            labelBackgroundRect.anchorMin = new Vector2(0.04f, 0.02f);
            labelBackgroundRect.anchorMax = new Vector2(0.96f, 0.30f);
            labelBackgroundRect.offsetMin = Vector2.zero;
            labelBackgroundRect.offsetMax = Vector2.zero;
            Image labelBackgroundImage = labelBackground.GetComponent<Image>();
            labelBackgroundImage.color = new Color(0.02f, 0.025f, 0.035f, 0.9f);
            labelBackgroundImage.raycastTarget = false;

            if (label != null)
            {
                label.transform.SetAsLastSibling();
            }

            return button;
        }

        private GameObject CreateClassBadge(Transform parent, out Image backgroundImage, out Image iconImage)
        {
            var badge = new GameObject("ClassBadge", typeof(RectTransform), typeof(Image), typeof(Outline));
            badge.transform.SetParent(parent, false);
            RectTransform badgeRect = badge.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(1f, 0.36f);
            badgeRect.anchorMax = new Vector2(1f, 0.36f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            badgeRect.sizeDelta = new Vector2(30f, 30f);
            badgeRect.anchoredPosition = new Vector2(-4f, 0f);

            backgroundImage = badge.GetComponent<Image>();
            backgroundImage.raycastTarget = false;
            Outline outline = badge.GetComponent<Outline>();
            outline.effectColor = new Color(0.02f, 0.025f, 0.035f, 0.95f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.useGraphicAlpha = false;

            var iconObject = new GameObject("ClassIcon", typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(badge.transform, false);
            iconImage = iconObject.GetComponent<Image>();
            iconImage.color = Color.white;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            RectTransform iconRect = iconImage.rectTransform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(5f, 5f);
            iconRect.offsetMax = new Vector2(-5f, -5f);

            badge.SetActive(false);
            return badge;
        }

        public static Sprite GetClassIconSprite(OperatorClass opClass)
        {
            if (ClassIconSprites.TryGetValue(opClass, out Sprite sprite) && sprite != null) return sprite;

            const int size = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[size * size];

            bool NearLine(float x, float y, Vector2 start, Vector2 end, float thickness)
            {
                Vector2 point = new Vector2(x, y);
                Vector2 line = end - start;
                float lengthSquared = line.sqrMagnitude;
                float t = lengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(point - start, line) / lengthSquared) : 0f;
                return Vector2.Distance(point, start + line * t) <= thickness;
            }

            bool NearCircle(float x, float y, Vector2 center, float radius)
            {
                return (new Vector2(x, y) - center).sqrMagnitude <= radius * radius;
            }

            bool IsShield(float x, float y)
            {
                if (y < 6f || y > 42f) return false;
                float outerHalfWidth = y >= 24f ? 17f : Mathf.Lerp(1f, 17f, (y - 6f) / 18f);
                float innerHalfWidth = y >= 27f ? 12.5f : Mathf.Lerp(0f, 12.5f, (y - 9f) / 18f);
                bool outer = Mathf.Abs(x - 24f) <= outerHalfWidth;
                bool inner = y >= 10f && y <= 38f && Mathf.Abs(x - 24f) < innerHalfWidth;
                return outer && !inner;
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    bool mark = false;
                    switch (opClass)
                    {
                        case OperatorClass.Guard:
                            mark = NearLine(px, py, new Vector2(12f, 10f), new Vector2(36f, 34f), 2.5f) ||
                                   NearLine(px, py, new Vector2(30f, 32f), new Vector2(37f, 25f), 2f) ||
                                   NearLine(px, py, new Vector2(24f, 23f), new Vector2(17f, 30f), 2f) ||
                                   NearLine(px, py, new Vector2(10f, 11f), new Vector2(17f, 4f), 2f);
                            break;
                        case OperatorClass.Defender:
                            mark = IsShield(px, py);
                            break;
                        case OperatorClass.Sniper:
                            float dx = (px - 18f) / 13f;
                            float dy = (py - 24f) / 19f;
                            float ellipse = dx * dx + dy * dy;
                            mark = (ellipse <= 1f && ellipse >= 0.76f && px <= 21f) ||
                                   NearLine(px, py, new Vector2(21f, 6f), new Vector2(21f, 42f), 1.5f) ||
                                   NearLine(px, py, new Vector2(19f, 24f), new Vector2(42f, 24f), 1.5f) ||
                                   NearLine(px, py, new Vector2(35f, 19f), new Vector2(42f, 24f), 1.5f) ||
                                   NearLine(px, py, new Vector2(35f, 29f), new Vector2(42f, 24f), 1.5f);
                            break;
                        case OperatorClass.Caster:
                            mark = NearLine(px, py, new Vector2(15f, 5f), new Vector2(31f, 36f), 2.6f) ||
                                   NearLine(px, py, new Vector2(10f, 15f), new Vector2(20f, 10f), 2f) ||
                                   ((NearCircle(px, py, new Vector2(31f, 37f), 8f) &&
                                     !NearCircle(px, py, new Vector2(31f, 37f), 5f)) ||
                                    (px >= 28f && px <= 34f && py >= 30f && py <= 42f));
                            break;
                        case OperatorClass.Medic:
                            mark = (px >= 19f && px <= 29f && py >= 8f && py <= 40f) ||
                                   (px >= 8f && px <= 40f && py >= 19f && py <= 29f);
                            break;
                    }

                    if (mark) pixels[y * size + x] = Color.white;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            ClassIconSprites[opClass] = sprite;
            return sprite;
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

        private static Sprite CreateCooldownClockSprite()
        {
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

        private void CreateCooldownToast(Transform root)
        {
            var toast = new GameObject("CooldownToast", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            toast.transform.SetParent(root, false);
            cooldownToastRect = toast.GetComponent<RectTransform>();
            SetPosition(cooldownToastRect, new Vector2(0f, 188f), new Vector2(0.5f, 0f), new Vector2(560f, 76f), new Vector2(0.5f, 0f));

            Image background = toast.GetComponent<Image>();
            background.color = new Color(0.035f, 0.04f, 0.055f, 0.97f);
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

        private bool CanDragDeckCard(int slotIndex)
        {
            if (gameManager == null || gameManager.CurrentPhase != StagePhase.Preparation || playerDeck == null) return false;

            DraftCard card = playerDeck.GetCard(slotIndex);
            if (card == null) return false;
            if (card.cooldownRoundsRemaining <= 0) return true;

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
            RectTransform cardRect = card.GetComponent<RectTransform>();
            SetPosition(cardRect, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(680f, 380f), new Vector2(0.5f, 0.5f));
            card.GetComponent<Image>().color = new Color(0.035f, 0.05f, 0.065f, 1f);
            Outline cardOutline = card.GetComponent<Outline>();
            cardOutline.effectColor = new Color(WaveColor.r, WaveColor.g, WaveColor.b, 0.8f);
            cardOutline.effectDistance = new Vector2(3f, -3f);

            environmentBriefingTitle = CreateText(card.transform, "Title", "ENVIRONMENTAL EFFECT", 30, TextAnchor.MiddleCenter);
            environmentBriefingTitle.color = WaveColor;
            SetPosition(environmentBriefingTitle.rectTransform, new Vector2(0f, -40f), new Vector2(0.5f, 1f), new Vector2(600f, 52f), new Vector2(0.5f, 1f));
            environmentBriefingTitle.raycastTarget = false;

            environmentBriefingDescription = CreateText(card.transform, "Description", string.Empty, 20, TextAnchor.MiddleCenter);
            environmentBriefingDescription.fontStyle = FontStyle.Normal;
            environmentBriefingDescription.horizontalOverflow = HorizontalWrapMode.Wrap;
            environmentBriefingDescription.verticalOverflow = VerticalWrapMode.Truncate;
            environmentBriefingDescription.color = new Color(0.86f, 0.9f, 0.95f, 1f);
            SetPosition(environmentBriefingDescription.rectTransform, new Vector2(0f, -100f), new Vector2(0.5f, 1f), new Vector2(560f, 150f), new Vector2(0.5f, 1f));
            environmentBriefingDescription.raycastTarget = false;

            Button dismissButton = CreateButton(card.transform, "DismissButton", "UNDERSTOOD", new Vector2(250f, 58f));
            SetPosition(dismissButton.GetComponent<RectTransform>(), new Vector2(0f, 28f), new Vector2(0.5f, 0f), null, new Vector2(0.5f, 0f));
            StyleButton(dismissButton, ReadyColor, AccentColor, 1.05f);
            dismissButton.onClick.AddListener(DismissEnvironmentBriefing);

            environmentBriefingPanel.SetActive(false);
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
            string effectDescription;
            switch (stage.stageId)
            {
                case "STAGE_03":
                    effectDescription =
                        "A sandstorm sweeps the battlefield. Attack intervals are 5% shorter, " +
                        "and each attack has a 10% chance to miss.";
                    break;
                case "STAGE_06":
                    effectDescription =
                        "Acid rain periodically deals 3 physical damage to deployed operators " +
                        "and active enemies every 2 seconds. Operators are protected during Preparation.";
                    break;
                default:
                    yield break;
            }

            environmentBriefingDescription.text = $"{stage.mapName}\n\n{effectDescription}";
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

            // Centered card
            var card = new GameObject("PauseCard", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(pausePanel.transform, false);
            pauseCard = card.GetComponent<RectTransform>();
            SetPosition(pauseCard, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(520f, 500f), new Vector2(0.5f, 0.5f));
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
            SetPosition(exitButton.GetComponent<RectTransform>(), new Vector2(0f, -402f), new Vector2(0.5f, 1f), null, new Vector2(0.5f, 0.5f));
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

            if (sandboxController != null)
            {
                if (phaseText != null) phaseText.text = "QA SANDBOX";
                phaseColorTarget = PrepColor;
                SetSandboxWaveButton(waveManager != null && waveManager.IsWaveInProgress);
                return;
            }

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
                    deckPortraitImages[i].sprite = card.operatorData.portrait;
                    deckPortraitImages[i].enabled = card.operatorData.portrait != null;
                    deckPortraitImages[i].color = isReady ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
                    Color classColor = GetClassColor(card.operatorData.operatorClass);
                    deckClassBadgeImages[i].color = classColor;
                    deckClassIconImages[i].sprite = GetClassIconSprite(card.operatorData.operatorClass);
                    deckClassBadges[i].SetActive(true);
                    deckButtonLabels[i].text = $"{card.operatorData.operatorName}\n{stars}";
                    deckButtons[i].interactable = true;
                    deckCooldownLabels[i].text = card.cooldownRoundsRemaining.ToString();
                    deckCooldownBadges[i].SetActive(!isReady);

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
                    deckPortraitImages[i].sprite = null;
                    deckPortraitImages[i].enabled = false;
                    deckClassBadges[i].SetActive(false);
                    deckCooldownLabels[i].text = "";
                    deckCooldownBadges[i].SetActive(false);
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
            if (startWaveButtonText != null)
                startWaveButtonText.text = active ? "END\nWAVE" : "START\nWAVE";
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
                waveText.text = total > 0 ? $"{current}/{total}" : current.ToString();
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
            if (sandboxController != null) return;
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
            lpText.text = hearts.Trim();
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

        private static Sprite GetWaveMarkSprite()
        {
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

        private static Sprite CreateTrashIconSprite()
        {
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