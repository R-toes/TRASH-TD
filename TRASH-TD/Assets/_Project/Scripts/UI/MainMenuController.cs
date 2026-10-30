using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using TrashTD.Audio;
using TrashTD.Data;
using StageProgress = TrashTD.Core.GameLoop.StageProgress;

namespace TrashTD.UI
{
    /// <summary>
    /// Main menu and stage selector flow for the first playable shell.
    /// Animated panels, staggered entrances, hover/press feedback (see MenuButtonFeedback),
    /// optional UI sounds and keyboard / gamepad focus support.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        private const string FirstPlayableStageId = "STAGE_01";
        private const string SecondPlayableStageId = "STAGE_02";
        private const string ThirdPlayableStageId = "STAGE_03";
        private const string FourthPlayableStageId = "STAGE_04";
        private const string FifthPlayableStageId = "STAGE_05";
        private const string SixthPlayableStageId = "STAGE_06";
        private const string SandboxStageId = "SANDBOX";
        private const string TutorialStageId = TrashTD.Core.GameLoop.TutorialDirector.TutorialStageId;
        private const string GameplaySceneName = "GameplayTest";
        private const float EntranceDuration = 0.45f;

        private static bool IsStagePlayable(string stageId)
        {
            return stageId == FirstPlayableStageId
                || stageId == SecondPlayableStageId
                || stageId == ThirdPlayableStageId
                || stageId == FourthPlayableStageId
                || stageId == FifthPlayableStageId
                || stageId == SixthPlayableStageId
                || (stageId == SandboxStageId && TrashTD.Development.DevMode.IsEnabled)
                || stageId == TutorialStageId;
        }

        /// <summary>Online, and unlocked by progress on at least one difficulty (StageProgress; dev mode opens all).</summary>
        private static bool IsStageOpen(StageData stage)
        {
            return stage != null && IsStagePlayable(stage.stageId) && StageProgress.IsAnyDifficultyUnlocked(stage.stageId);
        }

        /// <summary>The selected stage can be played on the selected difficulty.</summary>
        private bool IsSelectedDifficultyUnlocked =>
            selectedStage == null || IsTutorialStage(selectedStage) || StageProgress.IsUnlocked(selectedStage.stageId, selectedDifficulty);

        // ── Palette ──────────────────────────────────────────
        private static readonly Color AccentColor = new Color(0.18f, 0.82f, 0.45f, 1f);
        private static readonly Color AccentBright = new Color(0.18f, 0.92f, 0.50f, 1f);
        private static readonly Color TitleDim = new Color(0.16f, 0.80f, 0.44f, 1f);
        private static readonly Color TitleBright = new Color(0.32f, 1.00f, 0.64f, 1f);
        private static readonly Color BackgroundColor = new Color(0.025f, 0.03f, 0.045f, 1f);
        private static readonly Color TextDim = new Color(0.50f, 0.58f, 0.68f, 1f);
        private static readonly Color CardColor = new Color(0.06f, 0.07f, 0.10f, 1f);
        private static readonly Color CardLockedColor = new Color(0.045f, 0.05f, 0.065f, 1f);

        // Terminal (Pip-Boy) styling shared by the stage selector and the stage dossier
        private static readonly Color TerminalPanel = new Color(0.016f, 0.042f, 0.028f, 0.97f);
        private static readonly Color TerminalHeader = new Color(0.030f, 0.090f, 0.055f, 1f);
        private static readonly Color TerminalPlate = new Color(0.010f, 0.030f, 0.020f, 0.82f);
        private static readonly Color TerminalButton = new Color(0.035f, 0.090f, 0.060f, 0.95f);
        private static readonly Color TerminalText = new Color(0.45f, 0.78f, 0.56f, 1f);
        private static readonly Color TerminalLine = new Color(0.22f, 0.91f, 0.50f, 0.45f);

        // Title screen
        private static readonly Color HazardYellow = new Color(0.98f, 0.80f, 0.18f, 1f);
        private static readonly Color HazardInk = new Color(0.07f, 0.06f, 0.03f, 1f);
        private static readonly Color PrimaryLabel = new Color(0.02f, 0.10f, 0.05f, 1f);
        private static readonly Color RecRed = new Color(1f, 0.27f, 0.22f, 1f);
        private const float MenuColumnX = 144f;
        private const float MenuButtonLabelInset = 56f;
        private const string StageActTitle = "ACT 1";
        private const string StageZoneTitle = "NERC Development Zone";
        private const string StageSubtitleText = "Choose deployment";
        private const string StageZoneLore = "Once a thriving industrial region built around energy production, Calder Basin became the center of NERC's ambitious development program. What began as an effort to modernize the nation's energy infrastructure eventually left behind abandoned facilities, contaminated settlements, and entire districts deemed uninhabitable. Now, the NERC Development Zone is a fractured landscape of decaying industry and tightly controlled settlements, where the remnants of the corporation's legacy are impossible to escape.";
        private const string StageTickerIdle = "> AWAITING STAGE SELECTION";
        private const float StageLeftEdge = 280f;      // stage select title block x: clear of StageMapSidePanel (~244 wide at 1080 high)
        private const float StageBackButtonX = 150f;   // clear of the side panel's slanted bottom
        private const float TickerCharsPerSecond = 70f;

        private static readonly StageDifficulty[] DifficultyOrder =
        {
            StageDifficulty.Easy, StageDifficulty.Normal, StageDifficulty.Hard
        };

        [Header("Main Menu")]
        [SerializeField] private GameObject mainMenuPanel;
        [SerializeField] private GameObject stageSelectionPanel;

        [SerializeField] private Text titleText;
        [SerializeField] private Text subtitleText;
        [SerializeField] private Button startButton;
        [SerializeField] private Button exitButton;

        [Header("Stage Selector")]
        [SerializeField] private Button backButton;
        [SerializeField] private Button sandboxButton;
        [SerializeField] private Transform stageListRoot;
        [SerializeField] private Button stageButtonPrefab;
        [SerializeField] private GameObject stageDetailsPanel;
        [SerializeField] private Text stageDetailsTitle;
        [SerializeField] private Text stageDetailsDescription;
        [SerializeField] private Button playStageButton;
        [SerializeField] private Button stageDetailsBackButton;

        [Header("Pip-Boy Map (auto-created)")]
        [SerializeField] private Sprite stageMapBackgroundSprite;

        [Header("UI Sounds (optional)")]
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioClip clickClip;
        [SerializeField] private AudioClip backClip;
        [SerializeField] private AudioClip deniedClip;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.7f;

        private static StageDifficulty pendingStageDifficulty;
        private static bool hasPendingStageDifficulty;
        private static StageData pendingStageData;
        private static bool hasPendingStageData;
        private static bool returnToStageSelector;

        private readonly List<StageData> availableStages = new List<StageData>();
        private readonly List<MenuButtonFeedback> stageCards = new List<MenuButtonFeedback>();
        private readonly List<StageMapNode> stageMapNodes = new List<StageMapNode>();
        private StageMapLayout stageMapLayout;
        private StageMapNavigator mapNavigator;
        private StageMapSidePanel stageSidePanel;
        private Slider difficultySlider;
        private RectTransform difficultySliderRoot;
        private Image difficultyFill;
        private Image difficultyKnob;
        private Image difficultyKnobHighlight;
        private Image[] difficultyNotches;
        private Text[] difficultyNotchLabels;
        private StageData selectedStage;
        private int selectedStageIndex = -1;
        private StageDifficulty selectedDifficulty = StageDifficulty.Normal;
        private Text difficultyHeading;
        private Text difficultyLabel;
        private Text difficultyStats;
        private Text difficultyBlurb;
        private Text difficultyRecord; // Best stars on the selected difficulty, or what unlocks it
        private Text playStageLabel;
        private RectTransform difficultyBriefing;

        // Extra UI references created by the default layout
        private RectTransform stageDetailsModal;
        private Image modalAccentImage;
        private Color modalAccentTarget = AccentColor;
        private Text stageDetailsTag;
        private Text stageDetailsNumber;
        private Text stageProgressText;
        private RectTransform mainDivider;
        private RectTransform mainVersion;

        // Title screen
        private MainMenuBackdrop menuBackdrop;
        private Text menuFeedCaption;
        private RectTransform menuFeedStatus;
        private Image menuRecDot;
        private RectTransform titleBlock;
        private Text titleGlitchA;
        private Text titleGlitchB;
        private float titleGlitchTimer = 2.5f;
        private float titleGlitchRemaining;
        private Text bootLineText;
        private string bootLineTarget = string.Empty;
        private float bootLineTime;
        private Text menuHintText;
        private readonly List<MainMenuButtonView> mainMenuButtons = new List<MainMenuButtonView>();
        private RectTransform stageHeaderTitle;
        private RectTransform stageZoneTitle;
        private RectTransform stageHeaderSubtitle;
        private RectTransform stageZoneLore;
        private RectTransform stageHint;
        private RectTransform stageTitlePlate;
        private Text stageHeaderSubtitleText;
        private RectTransform stageTickerPlate;
        private Text stageTickerText;
        private string stageTickerTarget = StageTickerIdle;
        private float stageTickerTime;
        private bool stageTickerScrolls;
        private string stageTickerLoopText;
        private float stageTickerLoopPeriod;
        private float stageTickerScrollOffset;
        private float stageTickerHoldTime;

        // Stage dossier (details modal)
        private RectTransform stageDetailsColumn;
        private RectTransform stagePreviewFrame;
        private StageMapPreview stagePreview;
        private Text stagePreviewInfo;
        private Text stageDetailsStats;
        private Transform stageDetailsDivider;
        private MapTutorialGuide mapGuide;

        // State
        private bool menuInitialized;
        private bool stageSelectionOpen;
        private bool detailsOpen;
        private bool isLoadingStage;
        private bool showLoadingLabel;

        // Animation plumbing
        private readonly Dictionary<GameObject, Coroutine> panelRoutines = new Dictionary<GameObject, Coroutine>();
        private readonly List<EntranceItem> mainEntrance = new List<EntranceItem>();
        private readonly List<EntranceItem> stageEntrance = new List<EntranceItem>();
        private readonly List<EntranceItem> detailsEntrance = new List<EntranceItem>();
        private Coroutine mainEntranceRoutine;
        private Coroutine stageEntranceRoutine;
        private Coroutine detailsEntranceRoutine;
        private Coroutine overlayRoutine;
        private readonly List<Mote> motes = new List<Mote>();
        private Image fadeOverlay;
        private Text loadingLabel;
        private AudioSource sfxSource;
        private AudioSettingsUI audioSettingsUI;
        private ReclaimersIntelUI intelUI;

        private sealed class EntranceItem
        {
            public RectTransform rt;
            public CanvasGroup group;
            public MenuButtonFeedback feedback;
            public Vector2 target;
            public Vector2 offset;
            public float delay;
            public float scaleFrom = 1f;
        }

        private sealed class Mote
        {
            public RectTransform rt;
            public RectTransform layer;
            public Image image;
            public float speed;
            public float sway;
            public float phase;
            public float alpha;
        }

        private sealed class MainMenuButtonView
        {
            public MenuButtonFeedback feedback;
            public CanvasGroup selectFrame;
            public Text caret;
            public RectTransform label;
            public float hot;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetNavigationState()
        {
            returnToStageSelector = false;
            hasPendingStageDifficulty = false;
            pendingStageData = null;
            hasPendingStageData = false;
        }

        // ─────────────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────────────

        private void Awake()
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            if (hoverClip == null) hoverClip = AudioManager.Instance?.GetClip(SfxId.UiHover);
            if (clickClip == null) clickClip = AudioManager.Instance?.GetClip(SfxId.UiClick);
            if (backClip == null) backClip = AudioManager.Instance?.GetClip(SfxId.UiBack);
            if (deniedClip == null) deniedClip = AudioManager.Instance?.GetClip(SfxId.UiDenied);

            if (mainMenuPanel == null || stageSelectionPanel == null || startButton == null || exitButton == null)
            {
                BuildDefaultUi();
            }

            if (titleText == null)
            {
                titleText = GameObject.Find("TitleText")?.GetComponent<Text>();
            }

            if (subtitleText == null)
            {
                subtitleText = GameObject.Find("SubtitleText")?.GetComponent<Text>();
            }

            if (backButton == null)
            {
                backButton = GameObject.Find("BackButton")?.GetComponent<Button>();
            }

            if (stageListRoot == null)
            {
                stageListRoot = GameObject.Find("StageList")?.transform;
            }

            if (stageButtonPrefab == null)
            {
                stageButtonPrefab = GameObject.Find("StageButtonPrefab")?.GetComponent<Button>();
            }

            if (stageDetailsPanel == null)
            {
                BuildStageDetailsUi();
            }

            BuildStageButtons();
            SetUpMapNavigator();
            InitializeMenuBackdrop();
            BuildDifficultySelector();
            AttachMenuFeedback();
            Canvas menuCanvas = mainMenuPanel != null ? mainMenuPanel.GetComponentInParent<Canvas>() : null;
            if (menuCanvas != null)
            {
                intelUI = ReclaimersIntelUI.Create(
                    menuCanvas.transform,
                    mainMenuPanel.transform,
                    button => StyleMainMenuButton(button, "INTEL", "02", false),
                    rect => Place(rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(MenuColumnX, -144f), new Vector2(440f, 56f)));
                audioSettingsUI = AudioSettingsUI.Create(
                    menuCanvas.transform,
                    mainMenuPanel.transform,
                    button => StyleMainMenuButton(button, "SETTINGS", "03", false),
                    rect => Place(rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(MenuColumnX, -216f), new Vector2(440f, 56f)));
            }
            RegisterEntranceItems();
            UpdateDifficultyDisplay();

            overlayRoutine = StartCoroutine(FadeOverlayRoutine(1f, 0f, 0.4f));
            ShowMainMenu();
            menuInitialized = true;
        }

        private void Start()
        {
            AudioManager.Instance?.PlayMainMenuMusic();

            if (startButton != null)
            {
                startButton.onClick.RemoveAllListeners();
                startButton.onClick.AddListener(ShowStageSelection);
            }

            if (exitButton != null)
            {
                exitButton.onClick.RemoveAllListeners();
                exitButton.onClick.AddListener(ExitGame);
            }

            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.AddListener(PlayBack);
                backButton.onClick.AddListener(ShowMainMenu);
            }

            if (playStageButton != null)
            {
                playStageButton.onClick.RemoveAllListeners();
                playStageButton.onClick.AddListener(PlaySelectedStage);
            }

            if (stageDetailsBackButton != null)
            {
                stageDetailsBackButton.onClick.RemoveAllListeners();
                stageDetailsBackButton.onClick.AddListener(PlayBack);
                stageDetailsBackButton.onClick.AddListener(CloseStageDetails);
            }

            if (returnToStageSelector)
            {
                returnToStageSelector = false;
                ShowStageSelectionInternal();
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float time = Time.unscaledTime;

            UpdateMotes(dt, time);

            // Title screen: breathing title glow, glitches, boot line, REC light, menu selection
            if (mainMenuPanel != null && mainMenuPanel.activeInHierarchy)
            {
                if (titleText != null)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(time * 1.4f);
                    titleText.color = Color.Lerp(TitleDim, TitleBright, pulse);
                }

                UpdateTitleScreen(dt, time);
            }

            // Stage selector terminal: blinking cursor on the subtitle, typed-out status ticker
            if (stageSelectionPanel != null && stageSelectionPanel.activeInHierarchy)
            {
                bool cursorOn = Mathf.Repeat(time, 1f) < 0.5f;
                if (stageHeaderSubtitleText != null)
                {
                    stageHeaderSubtitleText.text = StageSubtitleText + (cursorOn ? "_" : " ");
                }

                if (stageTickerText != null)
                {
                    UpdateStageTicker(dt, cursorOn);
                }
            }

            // Difficulty tint on the details modal stripe
            if (modalAccentImage != null)
            {
                float k = 1f - Mathf.Exp(-12f * dt);
                modalAccentImage.color = Color.Lerp(modalAccentImage.color, modalAccentTarget, k);
            }

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !isLoadingStage)
            {
                HandleBack();
            }
        }

        private void HandleBack()
        {
            if (intelUI != null && intelUI.HandleBack())
            {
                return;
            }

            if (audioSettingsUI != null && audioSettingsUI.IsOpen)
            {
                PlayBack();
                audioSettingsUI.Close();
            }
            else if (detailsOpen)
            {
                PlayBack();
                CloseStageDetails();
            }
            else if (stageSelectionOpen)
            {
                PlayBack();
                ShowMainMenu();
            }
        }

        // ─────────────────────────────────────────────────────
        //  Static navigation API (unchanged)
        // ─────────────────────────────────────────────────────

        public static void ReturnToStageSelectorOnLoad()
        {
            returnToStageSelector = true;
        }

        public static void SetPendingStageDifficulty(StageDifficulty difficulty)
        {
            pendingStageDifficulty = difficulty;
            hasPendingStageDifficulty = true;
        }

        public static StageDifficulty ConsumePendingStageDifficulty(StageDifficulty fallback)
        {
            if (!hasPendingStageDifficulty) return fallback;

            hasPendingStageDifficulty = false;
            return pendingStageDifficulty;
        }

        public static void SetPendingStage(StageData stage)
        {
            pendingStageData = stage;
            hasPendingStageData = true;
        }

        public static StageData ConsumePendingStage(StageData fallback)
        {
            if (!hasPendingStageData) return fallback;

            hasPendingStageData = false;
            StageData stage = pendingStageData;
            pendingStageData = null;
            return stage != null ? stage : fallback;
        }

        // ─────────────────────────────────────────────────────
        //  Default UI construction
        // ─────────────────────────────────────────────────────

        public void BuildDefaultUi()
        {
            var canvasGo = GetOrCreateCanvas();
            canvasGo.transform.SetParent(null, false);

            // ── Main Menu Panel ──────────────────────────────
            if (mainMenuPanel == null)
            {
                mainMenuPanel = new GameObject("MainMenuPanel", typeof(RectTransform), typeof(Image));
                mainMenuPanel.transform.SetParent(canvasGo.transform, false);
                StretchFill(mainMenuPanel.GetComponent<RectTransform>());
                mainMenuPanel.GetComponent<Image>().color = BackgroundColor;
            }

            BuildTitleScreen();

            // ── Stage Selection Panel ────────────────────────
            if (stageSelectionPanel == null)
            {
                stageSelectionPanel = new GameObject("StageSelectionPanel", typeof(RectTransform), typeof(Image));
                stageSelectionPanel.transform.SetParent(canvasGo.transform, false);
                stageSelectionPanel.GetComponent<Image>().color = BackgroundColor;
                StretchFill(stageSelectionPanel.GetComponent<RectTransform>());
                stageSelectionPanel.SetActive(false);
            }

            // Ambient motes behind the map
            CreateAmbientLayer(stageSelectionPanel.transform, 22);

            // --- Pip-Boy Map Container (replaces old grid) ---
            if (stageListRoot == null)
            {
                var mapContainerGo = new GameObject("StageMapContainer", typeof(RectTransform), typeof(Image));
                mapContainerGo.transform.SetParent(stageSelectionPanel.transform, false);
                stageListRoot = mapContainerGo.transform;

                var mapContainerRect = mapContainerGo.GetComponent<RectTransform>();
                // Fills the whole screen
                StretchFill(mapContainerRect);

                var mapBg = mapContainerGo.GetComponent<Image>();
                if (stageMapBackgroundSprite == null)
                {
                    stageMapBackgroundSprite = LoadSpriteSafely("StageSelectorMap", "Assets/_Project/Art/Levels/Stage Selector Map.png");
                }

                if (stageMapBackgroundSprite != null)
                {
                    if (stageMapBackgroundSprite.texture != null)
                    {
                        stageMapBackgroundSprite.texture.filterMode = FilterMode.Point;
                    }
                    mapBg.sprite = stageMapBackgroundSprite;
                    mapBg.type = Image.Type.Simple;
                    mapBg.preserveAspect = false;
                    mapBg.color = Color.white;
                }
                else
                {
                    // Dark placeholder with subtle CRT-green tint if no art assigned
                    mapBg.color = new Color(0.02f, 0.05f, 0.03f, 1f);
                }

                // Add child Image stretched to fill the map sprite with 12-strip animation overlay
                var animOverlayGo = new GameObject("StageMap_Anim_Overlay", typeof(RectTransform), typeof(Image));
                animOverlayGo.transform.SetParent(mapContainerGo.transform, false);
                var animOverlayRect = animOverlayGo.GetComponent<RectTransform>();
                StretchFill(animOverlayRect);

                var animOverlayImage = animOverlayGo.GetComponent<Image>();
                animOverlayImage.sprite = null;
                animOverlayImage.type = Image.Type.Simple;
                animOverlayImage.preserveAspect = false;
                animOverlayImage.raycastTarget = false;
                animOverlayImage.color = Color.white;

                // Animates itself from Resources (12 fps), identically in the Editor and in builds
                var animOverlayComp = animOverlayGo.AddComponent<StageMapAnimOverlay>();
                animOverlayComp.FrameDuration = 0.0833f;

                // Add the layout driver so nodes scale with the map
                stageMapLayout = mapContainerGo.AddComponent<StageMapLayout>();

                // The CRT look comes from the game-wide CrtDisplay
            }

            // Terminal frame around the whole map screen
            AddCornerBrackets(stageSelectionPanel.GetComponent<RectTransform>(), 46f, 4f, TerminalLine, 18f);

            // Machine panel down the left edge; its joystick follows the map navigation
            stageSidePanel = StageMapSidePanel.Create(stageSelectionPanel.transform);

            // Backing plate so the title block reads over busy map art
            float headerX = 350f;
            float headerY = -115f;
            float headerWidth = 520f;
            float headerHeight = 124f;

            var titlePlate = new GameObject("StageSelectorTitlePlate", typeof(RectTransform), typeof(Image));
            titlePlate.transform.SetParent(stageSelectionPanel.transform, false);
            stageTitlePlate = titlePlate.GetComponent<RectTransform>();
            Place(stageTitlePlate, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(headerX, headerY), new Vector2(headerWidth, headerHeight));
            var titlePlateImage = titlePlate.GetComponent<Image>();
            titlePlateImage.color = TerminalPlate;
            titlePlateImage.raycastTarget = true;
            AddCornerBrackets(stageTitlePlate, 16f, 2f, AccentBright);

            // ACT 1 title (prominent large font size)
            var actTitle = MakeText(stageSelectionPanel.transform, "StageSelectorActTitle", StageActTitle, 38, TextAnchor.MiddleLeft,
                AccentBright, FontStyle.Bold);
            actTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            actTitle.verticalOverflow = VerticalWrapMode.Overflow;
            Place(actTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(headerX + 18f, headerY - 10f), new Vector2(280f, 44f));
            stageHeaderTitle = actTitle.rectTransform;
            var actTitleShadow = actTitle.gameObject.AddComponent<Shadow>();
            actTitleShadow.effectColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.35f);
            actTitleShadow.effectDistance = new Vector2(0f, -3f);

            // NERC Development Zone on a new line below ACT 1
            var zoneTitle = MakeText(stageSelectionPanel.transform, "StageSelectorZoneTitle", StageZoneTitle, 22, TextAnchor.MiddleLeft,
                new Color(0.92f, 0.98f, 0.94f, 1f), FontStyle.Bold);
            zoneTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            zoneTitle.verticalOverflow = VerticalWrapMode.Overflow;
            Place(zoneTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(headerX + 20f, headerY - 56f), new Vector2(headerWidth - 36f, 30f));
            stageZoneTitle = zoneTitle.rectTransform;
            var zoneTitleShadow = zoneTitle.gameObject.AddComponent<Shadow>();
            zoneTitleShadow.effectColor = new Color(0f, 0f, 0f, 0.50f);
            zoneTitleShadow.effectDistance = new Vector2(0f, -2f);

            // Subtitle: Choose deployment
            stageHeaderSubtitleText = MakeText(stageSelectionPanel.transform, "StageSelectorSubtitle", StageSubtitleText, 12, TextAnchor.MiddleLeft, TerminalText);
            stageHeaderSubtitleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            stageHeaderSubtitleText.verticalOverflow = VerticalWrapMode.Overflow;
            Place(stageHeaderSubtitleText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(headerX + 20f, headerY - 92f), new Vector2(300f, 20f));
            stageHeaderSubtitle = stageHeaderSubtitleText.rectTransform;

            // Info prompt
            var intelBadge = MakeText(stageSelectionPanel.transform, "StageSelectorIntelBadge", "◆ [?] INTEL", 11, TextAnchor.MiddleRight,
                new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.75f), FontStyle.Bold);
            Place(intelBadge.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(headerX + headerWidth - 120f, headerY - 92f), new Vector2(100f, 20f));

            // Hover lore card: auto-resizes to fit its text perfectly and appears beneath the title plate only when hovered
            var loreCard = new GameObject("StageSelectorLoreCard", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            loreCard.transform.SetParent(stageSelectionPanel.transform, false);
            stageZoneLore = loreCard.GetComponent<RectTransform>();
            stageZoneLore.anchorMin = new Vector2(0f, 1f);
            stageZoneLore.anchorMax = new Vector2(0f, 1f);
            stageZoneLore.pivot = new Vector2(0f, 1f);
            stageZoneLore.anchoredPosition = new Vector2(headerX, headerY - headerHeight - 8f);
            stageZoneLore.sizeDelta = new Vector2(headerWidth, 0f);

            var loreCardImage = loreCard.GetComponent<Image>();
            loreCardImage.color = new Color(0.012f, 0.035f, 0.024f, 0.96f);
            loreCardImage.raycastTarget = true;

            var loreVlg = loreCard.GetComponent<VerticalLayoutGroup>();
            loreVlg.padding = new RectOffset(18, 18, 14, 18);
            loreVlg.spacing = 8f;
            loreVlg.childControlWidth = true;
            loreVlg.childControlHeight = true;
            loreVlg.childForceExpandWidth = true;
            loreVlg.childForceExpandHeight = false;

            var loreCsf = loreCard.GetComponent<ContentSizeFitter>();
            loreCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            loreCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var loreOutline = loreCard.AddComponent<Outline>();
            loreOutline.effectColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.35f);
            loreOutline.effectDistance = new Vector2(1f, -1f);

            // Bracket overlay that stretches to fill the auto-sized lore card without interfering with layout
            var bracketOverlay = new GameObject("BracketOverlay", typeof(RectTransform), typeof(LayoutElement));
            bracketOverlay.transform.SetParent(loreCard.transform, false);
            StretchFill(bracketOverlay.GetComponent<RectTransform>());
            var overlayLe = bracketOverlay.GetComponent<LayoutElement>();
            overlayLe.ignoreLayout = true;
            AddCornerBrackets(bracketOverlay.GetComponent<RectTransform>(), 12f, 2f, AccentBright);

            var loreTag = MakeText(loreCard.transform, "LoreTag", "◆ NATIONAL ENERGY RESOURCE CORPORATION", 11, TextAnchor.MiddleLeft, AccentBright, FontStyle.Bold);
            loreTag.horizontalOverflow = HorizontalWrapMode.Overflow;
            var tagLe = loreTag.gameObject.AddComponent<LayoutElement>();
            tagLe.minHeight = 18f;
            tagLe.preferredHeight = 18f;
            tagLe.flexibleHeight = 0f;

            var loreDivider = new GameObject("LoreDivider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            loreDivider.transform.SetParent(loreCard.transform, false);
            var dividerLe = loreDivider.GetComponent<LayoutElement>();
            dividerLe.minHeight = 1f;
            dividerLe.preferredHeight = 1f;
            dividerLe.flexibleHeight = 0f;
            var loreDividerImage = loreDivider.GetComponent<Image>();
            loreDividerImage.color = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.35f);
            loreDividerImage.raycastTarget = false;

            var loreBody = MakeText(loreCard.transform, "LoreBody", StageZoneLore, 11, TextAnchor.UpperLeft, new Color(TerminalText.r, TerminalText.g, TerminalText.b, 0.92f));
            loreBody.lineSpacing = 1.38f;
            loreBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            loreBody.verticalOverflow = VerticalWrapMode.Overflow;
            var bodyLe = loreBody.gameObject.AddComponent<LayoutElement>();
            bodyLe.preferredWidth = headerWidth - 36f;
            bodyLe.flexibleHeight = 1f;

            var loreGroup = loreCard.GetComponent<CanvasGroup>();
            var hoverController = titlePlate.AddComponent<StageHeaderLoreHover>();
            hoverController.Init(loreGroup);
            var childTrigger = loreCard.AddComponent<StageHeaderLoreChildTrigger>();
            childTrigger.parentHover = hoverController;

            LayoutRebuilder.ForceRebuildLayoutImmediate(stageZoneLore);

            // Status ticker: types out the stage under the pointer
            var tickerPlate = new GameObject("StageTicker", typeof(RectTransform), typeof(Image));
            tickerPlate.transform.SetParent(stageSelectionPanel.transform, false);
            stageTickerPlate = tickerPlate.GetComponent<RectTransform>();
            Place(stageTickerPlate, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(TickerWidth, 48f));
            var tickerImage = tickerPlate.GetComponent<Image>();
            tickerImage.color = TerminalPlate;
            tickerImage.raycastTarget = false;
            AddCornerBrackets(stageTickerPlate, 12f, 2f, TerminalLine);

            // Fixed-size window; long messages scroll inside it instead of widening the plate
            var tickerViewport = new GameObject("StageTickerViewport", typeof(RectTransform), typeof(RectMask2D));
            tickerViewport.transform.SetParent(tickerPlate.transform, false);
            var tickerViewportRect = tickerViewport.GetComponent<RectTransform>();
            tickerViewportRect.anchorMin = Vector2.zero;
            tickerViewportRect.anchorMax = Vector2.one;
            tickerViewportRect.offsetMin = new Vector2(TickerTextInset, 0f);
            tickerViewportRect.offsetMax = new Vector2(-TickerTextInset, 0f);

            stageTickerText = MakeText(tickerViewport.transform, "StageTickerText", StageTickerIdle, TickerFontSize, TextAnchor.MiddleLeft, AccentBright, FontStyle.Bold);
            stageTickerText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var tickerTextRect = stageTickerText.rectTransform;
            tickerTextRect.anchorMin = new Vector2(0f, 0f);
            tickerTextRect.anchorMax = new Vector2(0f, 1f);
            tickerTextRect.pivot = new Vector2(0f, 0.5f);
            tickerTextRect.anchoredPosition = Vector2.zero;
            tickerTextRect.sizeDelta = new Vector2(TickerViewportWidth, 0f);

            stageProgressText = MakeText(stageSelectionPanel.transform, "StageProgressText", "", 14, TextAnchor.MiddleRight, TerminalText, FontStyle.Bold);
            Place(stageProgressText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-60f, -48f), new Vector2(300f, 26f));

            if (sandboxButton == null)
            {
                sandboxButton = CreateButtonChild(stageSelectionPanel.transform, "SandboxButton", "⚙  QA SANDBOX");
            }
            StyleTerminalButton(sandboxButton, AccentBright, 15);
            Place(sandboxButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 40f), new Vector2(200f, 48f));
            sandboxButton.onClick.RemoveAllListeners();
            sandboxButton.onClick.AddListener(() => { PlayClick(); SelectStage(6); });
            AttachFeedback(sandboxButton, PlayClick, AccentBright, 1.05f);

            var hint = MakeText(stageSelectionPanel.transform, "StageHint", "ESC  BACK", 12, TextAnchor.MiddleRight,
                new Color(TerminalText.r, TerminalText.g, TerminalText.b, 0.6f));
            Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 96f), new Vector2(200f, 22f));
            stageHint = hint.rectTransform;

            if (backButton == null)
            {
                backButton = CreateButtonChild(stageSelectionPanel.transform, "BackButton", "◀  BACK");
            }
            StyleTerminalButton(backButton, TerminalText, 16);
            Place(backButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(StageBackButtonX, 40f), new Vector2(200f, 48f));

            if (stageDetailsPanel == null)
            {
                BuildStageDetailsUi();
            }
        }

        // ─────────────────────────────────────────────────────
        //  Title screen
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// The start screen: a live stage-art backdrop with an enemy parade (MainMenuBackdrop) inside a camera
        /// viewfinder, the glitching TINY RECLAIMERS title with a hazard "TD" sticker, and a terminal-style menu column.
        /// </summary>
        private void BuildTitleScreen()
        {
            Transform panel = mainMenuPanel.transform;
            var left = new Vector2(0f, 0.5f);

            // Live backdrop (filled in by InitializeMenuBackdrop once the stages are loaded), toxic motes over it
            var backdropGo = new GameObject("MenuBackdrop", typeof(RectTransform));
            backdropGo.transform.SetParent(panel, false);
            StretchFill(backdropGo.GetComponent<RectTransform>());
            menuBackdrop = backdropGo.AddComponent<MainMenuBackdrop>();
            CreateAmbientLayer(panel, 30);
            backdropGo.transform.SetAsFirstSibling();

            // Camera viewfinder: corner brackets, version top-left, REC light + feed caption top-right
            var viewfinder = new GameObject("Viewfinder", typeof(RectTransform));
            viewfinder.transform.SetParent(panel, false);
            var viewfinderRect = viewfinder.GetComponent<RectTransform>();
            viewfinderRect.anchorMin = Vector2.zero;
            viewfinderRect.anchorMax = Vector2.one;
            viewfinderRect.offsetMin = new Vector2(28f, 44f);
            viewfinderRect.offsetMax = new Vector2(-28f, -28f);
            AddCornerBrackets(viewfinderRect, 54f, 3f, TerminalLine);

            var versionText = MakeText(panel, "VersionText", "TINY RECLAIMERS TD  //  v0.1 EARLY ACCESS", 12, TextAnchor.UpperLeft, TerminalText);
            Place(versionText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(56f, -50f), new Vector2(600f, 20f));
            mainVersion = versionText.rectTransform;

            var feedStatus = new GameObject("FeedStatus", typeof(RectTransform));
            feedStatus.transform.SetParent(panel, false);
            menuFeedStatus = feedStatus.GetComponent<RectTransform>();
            Place(menuFeedStatus, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-56f, -50f), new Vector2(720f, 52f));

            var recLabel = MakeText(menuFeedStatus, "RecLabel", "REC", 14, TextAnchor.UpperRight, RecRed);
            Place(recLabel.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(80f, 20f));

            var recDot = new GameObject("RecDot", typeof(RectTransform), typeof(Image));
            recDot.transform.SetParent(menuFeedStatus, false);
            Place(recDot.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-54f, 0f), new Vector2(14f, 14f));
            menuRecDot = recDot.GetComponent<Image>();
            menuRecDot.color = RecRed;
            menuRecDot.raycastTarget = false;

            menuFeedCaption = MakeText(menuFeedStatus, "FeedCaption", string.Empty, 12, TextAnchor.UpperRight, TerminalText);
            menuFeedCaption.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(menuFeedCaption.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -30f), new Vector2(720f, 20f));

            // Boot line typed above the title (text set by InitializeMenuBackdrop)
            bootLineText = MakeText(panel, "BootLine", string.Empty, 14, TextAnchor.MiddleLeft, TerminalText);
            Place(bootLineText.rectTransform, left, left, new Vector2(MenuColumnX, 284f), new Vector2(900f, 24f));

            // ── Title: "TINY" over the extruded "RECLAIMERS" (RGB-split glitch copies behind it), "TD" sticker ──
            var titleBlockGo = new GameObject("TitleBlock", typeof(RectTransform));
            titleBlockGo.transform.SetParent(panel, false);
            titleBlock = titleBlockGo.GetComponent<RectTransform>();
            Place(titleBlock, left, left, new Vector2(MenuColumnX - 8f, 150f), new Vector2(1300f, 220f));

            var tinyText = MakeText(titleBlock, "TitleTiny", "TINY", 48, TextAnchor.MiddleLeft, HazardYellow);
            tinyText.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(tinyText.rectTransform, left, left, new Vector2(6f, 72f), new Vector2(600f, 60f));
            var tinyShadow = tinyText.gameObject.AddComponent<Shadow>();
            tinyShadow.effectColor = new Color(0.25f, 0.17f, 0.02f, 1f);
            tinyShadow.effectDistance = new Vector2(0f, -5f);

            // The word sits in its own rect so the glitch can jitter its layers around zero
            var titleWordGo = new GameObject("TitleWord", typeof(RectTransform));
            titleWordGo.transform.SetParent(titleBlock, false);
            var titleWord = titleWordGo.GetComponent<RectTransform>();
            Place(titleWord, left, left, new Vector2(0f, -16f), new Vector2(1200f, 130f));

            Text MakeTitleLayer(string name, Color color)
            {
                Text layer = MakeText(titleWord, name, "RECLAIMERS", 100, TextAnchor.MiddleLeft, color);
                layer.horizontalOverflow = HorizontalWrapMode.Overflow;
                layer.verticalOverflow = VerticalWrapMode.Overflow;
                Place(layer.rectTransform, left, left, Vector2.zero, new Vector2(1200f, 130f));
                return layer;
            }

            titleGlitchA = MakeTitleLayer("TitleGlitchA", new Color(1f, 0.15f, 0.40f, 0f));
            titleGlitchB = MakeTitleLayer("TitleGlitchB", new Color(0.20f, 0.90f, 1f, 0f));

            if (titleText == null)
            {
                titleText = MakeTitleLayer("TitleText", AccentBright);
            }
            else
            {
                titleText.transform.SetParent(titleWord, false);
                Place(titleText.rectTransform, left, left, Vector2.zero, new Vector2(1200f, 130f));
            }

            if (titleText.GetComponent<Shadow>() == null)
            {
                // Chunky extrusion: the second shadow copies the first, giving two dark steps under the face
                var extrude = titleText.gameObject.AddComponent<Shadow>();
                extrude.effectColor = new Color(0.03f, 0.22f, 0.11f, 1f);
                extrude.effectDistance = new Vector2(0f, -7f);
                var extrudeDeep = titleText.gameObject.AddComponent<Shadow>();
                extrudeDeep.effectColor = new Color(0.01f, 0.07f, 0.04f, 1f);
                extrudeDeep.effectDistance = new Vector2(0f, -7f);
            }

            float titleWidth = titleText.preferredWidth > 1f ? titleText.preferredWidth : 1000f;
            Sprite hazardStripe = MainMenuBackdrop.LoadWholeTextureSprite("UI/decor/hazard_strip");

            var badge = new GameObject("TitleBadge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(titleBlock, false);
            var badgeRect = badge.GetComponent<RectTransform>();
            Place(badgeRect, left, left, new Vector2(titleWidth + 30f, -10f), new Vector2(176f, 108f));
            badgeRect.localRotation = Quaternion.Euler(0f, 0f, -6f);
            var badgeImage = badge.GetComponent<Image>();
            badgeImage.color = HazardYellow;
            badgeImage.raycastTarget = false;
            var badgeShadow = badge.AddComponent<Shadow>();
            badgeShadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            badgeShadow.effectDistance = new Vector2(8f, -8f);

            if (hazardStripe != null)
            {
                var badgeStripe = new GameObject("Stripe", typeof(RectTransform), typeof(Image));
                badgeStripe.transform.SetParent(badgeRect, false);
                StretchBottom(badgeStripe.GetComponent<RectTransform>(), 0f, 0f, 0f, 16f);
                var badgeStripeImage = badgeStripe.GetComponent<Image>();
                badgeStripeImage.sprite = hazardStripe;
                badgeStripeImage.type = Image.Type.Tiled;
                badgeStripeImage.raycastTarget = false;
            }

            var badgeText = MakeText(badgeRect, "BadgeText", "TD", 64, TextAnchor.MiddleCenter, HazardInk);
            badgeText.rectTransform.offsetMin = new Vector2(4f, 16f);

            var divider = new GameObject("HazardDivider", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(panel, false);
            mainDivider = divider.GetComponent<RectTransform>();
            Place(mainDivider, left, left, new Vector2(MenuColumnX, 24f), new Vector2(576f, 16f));
            var dividerImage = divider.GetComponent<Image>();
            dividerImage.raycastTarget = false;
            if (hazardStripe != null)
            {
                dividerImage.sprite = hazardStripe;
                dividerImage.type = Image.Type.Tiled;
            }
            else
            {
                dividerImage.color = HazardYellow;
            }

            // ── Menu column (INTEL is row 02 and SETTINGS row 03, added by ReclaimersIntelUI / AudioSettingsUI in Awake) ──
            if (startButton == null)
            {
                startButton = CreateButtonChild(panel, "StartButton", "PLAY");
            }
            Place(startButton.GetComponent<RectTransform>(), left, left, new Vector2(MenuColumnX, -64f), new Vector2(440f, 72f));
            StyleMainMenuButton(startButton, "PLAY", "01", true);

            if (exitButton == null)
            {
                exitButton = CreateButtonChild(panel, "ExitButton", "EXIT");
            }
            Place(exitButton.GetComponent<RectTransform>(), left, left, new Vector2(MenuColumnX, -288f), new Vector2(440f, 56f));
            StyleMainMenuButton(exitButton, "EXIT", "04", false);

            menuHintText = MakeText(panel, "MenuHint", "[ARROWS] SELECT    [ENTER] CONFIRM", 11, TextAnchor.MiddleLeft,
                new Color(TerminalText.r, TerminalText.g, TerminalText.b, 0.6f));
            Place(menuHintText.rectTransform, left, left, new Vector2(MenuColumnX, -352f), new Vector2(600f, 20f));
        }

        /// <summary>
        /// Terminal menu row: label on the left, index on the right, and a caret plus bracket frame while the row
        /// is selected. Hovering selects the row, so mouse and keyboard share a single highlight.
        /// </summary>
        private void StyleMainMenuButton(Button button, string label, string index, bool primary)
        {
            if (button == null) return;

            var image = button.GetComponent<Image>();
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = primary ? AccentColor : TerminalButton;

            Text labelText = button.GetComponentInChildren<Text>();
            if (labelText != null)
            {
                labelText.text = label;
                labelText.font = UIFontHelper.GetPixelFont();
                labelText.fontSize = primary ? 26 : 18;
                labelText.fontStyle = FontStyle.Normal;
                labelText.alignment = TextAnchor.MiddleLeft;
                labelText.color = primary ? PrimaryLabel : TerminalText;
                RectTransform labelRect = labelText.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(MenuButtonLabelInset, 0f);
                labelRect.offsetMax = new Vector2(-64f, 0f);
            }

            if (!primary)
            {
                var bar = new GameObject("AccentBar", typeof(RectTransform), typeof(Image));
                bar.transform.SetParent(button.transform, false);
                var barRect = bar.GetComponent<RectTransform>();
                barRect.anchorMin = Vector2.zero;
                barRect.anchorMax = new Vector2(0f, 1f);
                barRect.pivot = new Vector2(0f, 0.5f);
                barRect.offsetMin = Vector2.zero;
                barRect.offsetMax = new Vector2(4f, 0f);
                var barImage = bar.GetComponent<Image>();
                barImage.color = TerminalLine;
                barImage.raycastTarget = false;
            }

            Text indexText = MakeText(button.transform, "Index", index, 11, TextAnchor.MiddleRight,
                primary ? new Color(PrimaryLabel.r, PrimaryLabel.g, PrimaryLabel.b, 0.55f) : TerminalLine);
            indexText.rectTransform.offsetMax = new Vector2(-20f, 0f);

            Text caret = MakeText(button.transform, "Caret", ">", primary ? 26 : 18, TextAnchor.MiddleLeft,
                new Color(primary ? PrimaryLabel.r : AccentBright.r, primary ? PrimaryLabel.g : AccentBright.g, primary ? PrimaryLabel.b : AccentBright.b, 0f));
            caret.rectTransform.offsetMin = new Vector2(22f, 0f);

            var frame = new GameObject("SelectFrame", typeof(RectTransform), typeof(CanvasGroup));
            frame.transform.SetParent(button.transform, false);
            var frameRect = frame.GetComponent<RectTransform>();
            StretchFill(frameRect);
            AddCornerBrackets(frameRect, 14f, 3f, AccentBright, -8f);
            var frameGroup = frame.GetComponent<CanvasGroup>();
            frameGroup.alpha = 0f;
            frameGroup.blocksRaycasts = false;
            frameGroup.interactable = false;

            MenuButtonFeedback feedback = AttachFeedback(button, null, primary ? AccentBright : AccentColor, 1.03f);
            feedback.Hovered = () =>
            {
                PlayHover();
                FocusUi(button.gameObject);
            };

            mainMenuButtons.Add(new MainMenuButtonView
            {
                feedback = feedback,
                selectFrame = frameGroup,
                caret = caret,
                label = labelText != null ? labelText.rectTransform : null
            });
        }

        /// <summary>Starts the backdrop on the playable stages and writes the boot line. Runs after BuildStageButtons.</summary>
        private void InitializeMenuBackdrop()
        {
            var showcase = new List<StageData>();
            int online = 0;
            foreach (StageData stage in availableStages)
            {
                if (IsTutorialStage(stage)) continue; // No map art to pan across, and not a numbered stage
                bool playableStage = stage != null && stage.stageId != SandboxStageId && IsStagePlayable(stage.stageId);
                showcase.Add(playableStage ? stage : null); // keep index i = Stage i+1 for the caption
                if (playableStage) online++;
            }

            bootLineTarget = $"> TERMINAL READY  //  {online} STAGES ONLINE";
            if (menuBackdrop != null) menuBackdrop.Initialize(showcase, menuFeedCaption);
        }

        private void UpdateTitleScreen(float dt, float time)
        {
            bool cursorOn = Mathf.Repeat(time, 1f) < 0.5f;

            if (bootLineText != null)
            {
                bootLineTime += dt;
                int visible = Mathf.Clamp(Mathf.FloorToInt(bootLineTime * TickerCharsPerSecond * 0.6f), 0, bootLineTarget.Length);
                bool typing = visible < bootLineTarget.Length;
                bootLineText.text = bootLineTarget.Substring(0, visible) + (typing || cursorOn ? "_" : " ");
            }

            if (menuRecDot != null) menuRecDot.enabled = Mathf.Repeat(time, 1.4f) < 0.9f;

            UpdateTitleGlitch(dt);

            float k = 1f - Mathf.Exp(-14f * dt);
            foreach (MainMenuButtonView view in mainMenuButtons)
            {
                if (view.feedback == null) continue;

                view.hot = Mathf.Lerp(view.hot, view.feedback.IsHot ? 1f : 0f, k);
                view.selectFrame.alpha = view.hot;

                Color caretColor = view.caret.color;
                caretColor.a = view.hot * (cursorOn ? 1f : 0.35f);
                view.caret.color = caretColor;

                if (view.label != null) view.label.offsetMin = new Vector2(MenuButtonLabelInset + 10f * view.hot, 0f);
            }
        }

        /// <summary>Every few seconds the title tears for a moment: RGB-split copies and a sideways jitter.</summary>
        private void UpdateTitleGlitch(float dt)
        {
            if (titleText == null || titleGlitchA == null || titleGlitchB == null) return;

            if (titleGlitchRemaining <= 0f)
            {
                titleGlitchTimer -= dt;
                if (titleGlitchTimer > 0f) return;
                titleGlitchRemaining = Random.Range(0.12f, 0.3f);
            }

            titleGlitchRemaining -= dt;
            bool on = titleGlitchRemaining > 0f;
            if (!on) titleGlitchTimer = Random.Range(3.5f, 7.5f);

            SetGlitchLayer(titleGlitchA, on, new Vector2(Random.Range(-10f, -4f), Random.Range(-3f, 3f)));
            SetGlitchLayer(titleGlitchB, on, new Vector2(Random.Range(4f, 10f), Random.Range(-3f, 3f)));
            titleText.rectTransform.anchoredPosition = on ? new Vector2(Random.Range(-4f, 4f), 0f) : Vector2.zero;
        }

        private static void SetGlitchLayer(Text layer, bool on, Vector2 offset)
        {
            Color c = layer.color;
            c.a = on ? 0.8f : 0f;
            layer.color = c;
            layer.rectTransform.anchoredPosition = on ? offset : Vector2.zero;
        }

        // ─────────────────────────────────────────────────────
        //  Panel navigation
        // ─────────────────────────────────────────────────────

        public void ShowMainMenu()
        {
            bool instant = !menuInitialized;
            stageSelectionOpen = false;
            detailsOpen = false;
            SetBackgroundInteractable(true);

            SetPanelVisible(mainMenuPanel, true, instant);
            SetPanelVisible(stageSelectionPanel, false, instant);
            SetPanelVisible(stageDetailsPanel, false, true, stageDetailsModal);

            bootLineTime = -0.35f; // let the entrance start before typing
            PlayEntrance(mainEntrance, ref mainEntranceRoutine);
            FocusUi(startButton != null ? startButton.gameObject : null);
        }

        public void ShowStageSelection()
        {
            ShowStageSelectionInternal();
        }

        // ── Coalesce's stage-map intro (MapTutorialGuide), until the tutorial is finished or skipped ──

        public bool IsStageSelectionOpen => stageSelectionOpen && !isLoadingStage;
        public bool IsTutorialDossierOpen => detailsOpen && IsTutorialStage(selectedStage);
        public RectTransform PlayStageButtonRect => playStageButton != null ? playStageButton.GetComponent<RectTransform>() : null;

        /// <summary>The map marker with this stage number (0 = the tutorial), for Coalesce's pointer.</summary>
        public RectTransform GetStageNodeRect(int stageNumber)
        {
            foreach (StageMapNode node in stageMapNodes)
            {
                if (node != null && node.StageNumber == stageNumber) return node.GetComponent<RectTransform>();
            }
            return null;
        }

        private bool GuideRequiresTutorialStage => mapGuide != null && mapGuide.RequiresTutorialStage;

        /// <summary>The map marker showing this stage, or null.</summary>
        private StageMapNode FindStageNode(StageData stage)
        {
            if (stage == null) return null;
            foreach (StageMapNode node in stageMapNodes)
            {
                if (node != null && node.Stage == stage) return node;
            }
            return null;
        }

        private static bool IsTutorialStage(StageData stage) => stage != null && stage.stageId == TutorialStageId;

        private void ShowStageSelectionInternal()
        {
            CardDraftOverlayUI.HideAllForMenuTransition();
            stageSelectionOpen = true;
            detailsOpen = false;
            stageTickerTarget = string.Empty;
            SetStageTicker(StageTickerIdle);
            SetBackgroundInteractable(true);

            SetPanelVisible(mainMenuPanel, false, true);
            SetPanelVisible(stageSelectionPanel, true, true);
            SetPanelVisible(stageDetailsPanel, false, true, stageDetailsModal);
            ApplyDevModeVisibility();

            // Deselect all nodes when returning to the map
            foreach (var node in stageMapNodes)
            {
                if (node != null) node.SetSelected(false);
            }

            if (mapNavigator != null) mapNavigator.ResetView(true);
            PlayEntrance(stageEntrance, ref stageEntranceRoutine);
            PlayStageNodeIntros();

            // First visits: Coalesce introduces the map and asks for STAGE 00 (or a skip)
            if (mapGuide == null && !TrashTD.Core.GameLoop.TutorialDirector.IsTutorialDone)
            {
                mapGuide = MapTutorialGuide.Create(this);
            }

            StageMapNode focusNode = FindStageNode(selectedStage) ?? (stageMapNodes.Count > 0 ? stageMapNodes[0] : null);
            if (focusNode != null)
            {
                FocusUi(focusNode.gameObject);
            }
        }

        public void CloseStageDetails()
        {
            if (!detailsOpen) return;

            detailsOpen = false;
            SetBackgroundInteractable(true);
            SetPanelVisible(stageDetailsPanel, false, false, stageDetailsModal);

            StageMapNode selectedNode = FindStageNode(selectedStage);
            if (selectedNode != null)
            {
                FocusUi(selectedNode.gameObject);
            }
        }

        // Players never see QA SANDBOX (DevMode). The ESC hint drops into its slot so the corner isn't left empty.
        private void ApplyDevModeVisibility()
        {
            bool dev = TrashTD.Development.DevMode.IsEnabled;
            if (sandboxButton != null) sandboxButton.gameObject.SetActive(dev);
            if (stageHint == null) return;

            var hintPos = new Vector2(-60f, dev ? 96f : 52f);
            stageHint.anchoredPosition = hintPos;
            foreach (EntranceItem item in stageEntrance)
            {
                if (item.rt == stageHint) item.target = hintPos; // the entrance slides back to its stored target
            }
        }

        private void SetBackgroundInteractable(bool value)
        {
            // While the details modal is open, keep keyboard/gamepad navigation from drifting underneath it.
            if (stageListRoot != null) GetOrAddCanvasGroup(stageListRoot.gameObject).interactable = value;
            if (backButton != null) GetOrAddCanvasGroup(backButton.gameObject).interactable = value;
            if (sandboxButton != null) GetOrAddCanvasGroup(sandboxButton.gameObject).interactable = value;
        }

        private static void FocusUi(GameObject target)
        {
            if (target == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(target);
        }

        // ─────────────────────────────────────────────────────
        //  Stage selector
        // ─────────────────────────────────────────────────────

        // ── Normalized node positions for each of the 6 stages on the map art ──
        // Origin is top-left (0,0), bottom-right is (1,1).
        private static readonly Vector2[] StageNodePositions = new[]
        {
            new Vector2(0.245f, 0.740f),  // Stage 1
            new Vector2(0.380f, 0.550f),  // Stage 2
            new Vector2(0.470f, 0.740f),  // Stage 3
            new Vector2(0.610f, 0.460f),  // Stage 4
            new Vector2(0.820f, 0.670f),  // Stage 5 (placed where stage 6 previously was)
            new Vector2(0.820f, 0.350f),  // Stage 6 (straight up above stage 5)
        };

        // STAGE 00, the tutorial: the small outpost by the lake on the map's west side
        private static readonly Vector2 TutorialNodePosition = new Vector2(0.275f, 0.345f);

        private void BuildStageButtons()
        {
            availableStages.Clear();
            stageCards.Clear();
            stageMapNodes.Clear();
            stageEntrance.Clear();

            string[] stageNames = new[]
            {
                "Blackwater Facilities", "Milestone 9", "Railyard Drain",
                "NERC Works Plant", "New Calder Basin", "The Pale"
                , "QA Sandbox"
            };

            string[] stageDescriptions = new[]
            {
                "An abandoned NERC research complex hiding the remnants of its secret experiments.",
                "A deserted roadside fuel and repair station, long abandoned after supply routes collapsed.",
                "A decaying freight yard choked with debris, stagnant water, and industrial waste.",
                "A massive power plant left unstable after the disaster that crippled the region.",
                "A fortified district serving as the last refuge for survivors within the development zone.",
                "The ruined remains of old Calder Basin, devastated by nuclear catastrophe and industrial contamination.",
                "Development-only map for enemy and operator testing."
            };

            for (int i = 0; i < stageNames.Length; i++)
            {
                string id = i < 6 ? "STAGE_0" + (i + 1) : SandboxStageId;
                var stage = Resources.Load<StageData>("Stages/" + id);
                if (stage == null)
                {
                    stage = ScriptableObject.CreateInstance<StageData>();
                    stage.stageId = id;
                    stage.mapName = stageNames[i];
                    stage.shortDescription = stageDescriptions[i];
                }
                availableStages.Add(stage);
            }

            // The tutorial comes after the sandbox, so Stage i+1 and the sandbox keep their indices
            StageData tutorialStage = Resources.Load<StageData>(TrashTD.Core.GameLoop.TutorialDirector.TutorialStageResourcePath);
            if (tutorialStage != null) availableStages.Add(tutorialStage);

            if (stageListRoot == null)
            {
                return;
            }

            // Clean up any old children (except the scanlines overlay and layout)
            for (int i = stageListRoot.childCount - 1; i >= 0; i--)
            {
                var child = stageListRoot.GetChild(i);
                if (child.GetComponent<StageMapScanlines>() != null) continue;
                if (child.GetComponent<StageMapAnimOverlay>() != null || child.name == "StageMap_Anim_Overlay") continue;
                if (child.name.StartsWith("StageNode_")) Destroy(child.gameObject);
            }

            int unlocked = 0;
            int mapStageCount = Mathf.Min(6, availableStages.Count);
            for (int i = 0; i < mapStageCount; i++)
            {
                var stage = availableStages[i];
                bool isPlayable = IsStageOpen(stage);
                if (isPlayable) unlocked++;

                var node = CreateStageMapNode(stage, i + 1, isPlayable, StageNodePositions[i]);
                stageMapNodes.Add(node);

                // Nodes run their own boot-in (PlayStageNodeIntros), not the generic entrance
                var nodeFeedback = node.GetComponent<MenuButtonFeedback>();
                if (nodeFeedback != null) stageCards.Add(nodeFeedback);
            }

            if (tutorialStage != null)
            {
                var tutorialNode = CreateStageMapNode(tutorialStage, 0, true, TutorialNodePosition);
                stageMapNodes.Add(tutorialNode);
                var tutorialFeedback = tutorialNode.GetComponent<MenuButtonFeedback>();
                if (tutorialFeedback != null) stageCards.Add(tutorialFeedback);
            }

            // Re-apply the layout to position everything
            if (stageMapLayout != null) stageMapLayout.Apply();

            if (stageProgressText != null)
            {
                stageProgressText.text = $"{unlocked} / 6 UNLOCKED";
            }
        }

        /// <summary>
        /// Creates one stage marker (StageMapNode builds its own target + callout card) under the map container.
        /// The callout goes left of markers near the right edge so it stays on screen.
        /// </summary>
        private StageMapNode CreateStageMapNode(StageData stage, int stageNumber, bool isPlayable, Vector2 normalizedPos)
        {
            var go = new GameObject("StageNode_" + stageNumber, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(stageListRoot, false);

            // Invisible hit area; the visuals are children built by the node
            var hitArea = go.GetComponent<Image>();
            hitArea.color = new Color(1f, 1f, 1f, 0f);
            hitArea.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = hitArea;
            btn.transition = Selectable.Transition.None;

            var node = go.AddComponent<StageMapNode>();
            node.Build(stage, stageNumber, normalizedPos, !isPlayable, normalizedPos.x > 0.7f);
            node.Clicked += OnStageMapNodeClicked;
            node.HoverChanged += OnStageMapNodeHoverChanged;

            // Press / wobble / sounds only: the node draws its own hover and glow, so no scale-up or outline here
            var feedback = go.AddComponent<MenuButtonFeedback>();
            feedback.hoverScale = 1f;
            feedback.pressScale = 0.96f;
            feedback.hoverBrighten = 0f;
            feedback.pressDarken = 0f;
            feedback.Hovered = PlayHover;
            feedback.Clicked = isPlayable ? (System.Action)PlayClick : PlayDenied;

            return node;
        }

        /// <summary>Scroll / drag / zoom navigation for the stage map, with its minimap and zoom bar (StageMapNavigator).</summary>
        private void SetUpMapNavigator()
        {
            if (stageListRoot == null || stageSelectionPanel == null || mapNavigator != null) return;

            mapNavigator = stageListRoot.gameObject.AddComponent<StageMapNavigator>();
            mapNavigator.InputBlocked = () => detailsOpen || isLoadingStage || (audioSettingsUI != null && audioSettingsUI.IsOpen)
                || (mapGuide != null && mapGuide.IsTalking);
            mapNavigator.Initialize(
                stageSelectionPanel.GetComponent<RectTransform>(),
                stageMapLayout,
                stageMapBackgroundSprite,
                stageSelectionPanel.transform,
                stageMapNodes,
                button =>
                {
                    StyleTerminalButton(button, AccentBright, 14);
                    AttachFeedback(button, PlayClick, AccentBright, 1.05f);
                });

            if (stageSidePanel != null)
            {
                stageSidePanel.Navigator = mapNavigator;
                stageSidePanel.Grabbed = PlayClick;
            }

            // Under the stage dossier, which shares the panel
            if (stageDetailsPanel != null && stageDetailsPanel.transform.parent == mapNavigator.HudRoot.parent)
            {
                mapNavigator.HudRoot.SetSiblingIndex(stageDetailsPanel.transform.GetSiblingIndex());
            }
        }

        /// <summary>Staggered boot-in for the map markers, left to right in stage order.</summary>
        private void PlayStageNodeIntros()
        {
            for (int i = 0; i < stageMapNodes.Count; i++)
            {
                if (stageMapNodes[i] != null) stageMapNodes[i].PlayIntro(0.25f + stageMapNodes[i].StageNumber * 0.12f);
            }
        }

        /// <summary>
        /// Loads a sprite with multi-tier fallback: Resources single, Resources multiple/subassets, or direct disk loading.
        /// </summary>
        private static Sprite LoadSpriteSafely(string resourcePath, string projectRelativeFilePath)
        {
            // 1. Try single sprite load from Resources
            var s = Resources.Load<Sprite>(resourcePath);
            if (s != null)
            {
                if (s.texture != null) s.texture.filterMode = FilterMode.Point;
                return s;
            }

            // 2. Try multiple subassets from Resources
            var all = Resources.LoadAll<Sprite>(resourcePath);
            if (all != null && all.Length > 0 && all[0] != null)
            {
                if (all[0].texture != null) all[0].texture.filterMode = FilterMode.Point;
                return all[0];
            }

            // 3. Try reading directly from project folder on disk
            try
            {
                string fullPath = projectRelativeFilePath;
                if (!System.IO.Path.IsPathRooted(fullPath))
                {
                    fullPath = System.IO.Path.Combine(Application.dataPath, "..", projectRelativeFilePath);
                }
                if (System.IO.File.Exists(fullPath))
                {
                    byte[] bytes = System.IO.File.ReadAllBytes(fullPath);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.filterMode = FilterMode.Point;
                    if (tex.LoadImage(bytes))
                    {
                        tex.filterMode = FilterMode.Point;
                        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[MainMenuController] Failed disk sprite load from {projectRelativeFilePath}: {ex.Message}");
            }

            return null;
        }

        /// <summary>Called when a StageMapNode dot is clicked on the Pip-Boy map.</summary>
        private void OnStageMapNodeClicked(StageMapNode node)
        {
            if (node == null || node.Stage == null) return;

            int index = availableStages.IndexOf(node.Stage);
            if (index < 0) return;

            SelectStage(index);
        }

        private void OnStageMapNodeHoverChanged(StageMapNode node, bool hovered)
        {
            if (node == null || node.Stage == null) return;

            string message;
            if (!hovered)
            {
                message = StageTickerIdle;
            }
            else
            {
                string stageName = string.IsNullOrEmpty(node.Stage.mapName) ? "UNKNOWN" : node.Stage.mapName.ToUpperInvariant();
                message = node.IsLocked
                    ? $"> STAGE {node.StageNumber:00} // {stageName}  -  SIGNAL LOCKED"
                    : $"> STAGE {node.StageNumber:00} // {stageName}  -  CLICK TO OPEN DOSSIER";
            }

            SetStageTicker(message);

            // Keyboard focus can move to a marker that is off screen while zoomed in
            if (hovered && mapNavigator != null) mapNavigator.EnsureVisible(node);
        }

        private void SetStageTicker(string message)
        {
            if (message == stageTickerTarget) return;
            stageTickerTarget = message;
            stageTickerTime = 0f;
            stageTickerScrollOffset = 0f;
            stageTickerHoldTime = 0f;
            PrepareStageTickerScroll(message);
        }

        private const float TickerWidth = 820f;
        private const float TickerTextInset = 20f;
        private const float TickerViewportWidth = TickerWidth - 2f * TickerTextInset;
        private const int TickerFontSize = 15;
        private const string TickerLoopGap = "          ";
        private const float TickerScrollSpeed = 70f;  // canvas units per second
        private const float TickerScrollHold = 1.2f;  // pause after typing before the marquee starts

        /// <summary>
        /// Decides whether the message fits the fixed ticker window. If it doesn't, builds the looping
        /// marquee text (message + gap + message) and measures one loop period, so the scroll can wrap seamlessly.
        /// </summary>
        private void PrepareStageTickerScroll(string message)
        {
            if (stageTickerText == null) return;

            float messageWidth = MeasureStageTicker(message + "_");
            stageTickerScrolls = messageWidth > TickerViewportWidth;
            if (!stageTickerScrolls)
            {
                stageTickerText.rectTransform.sizeDelta = new Vector2(TickerViewportWidth, 0f);
                return;
            }

            // Period = distance from the first copy's start to the second's; measured as a difference so
            // it doesn't depend on whether trailing spaces count toward preferred width.
            stageTickerLoopText = message + TickerLoopGap + message;
            float loopWidth = MeasureStageTicker(stageTickerLoopText);
            stageTickerLoopPeriod = loopWidth - MeasureStageTicker(message);

            // RectMask2D culls by the text's rect, so it must cover the whole overflowing line
            stageTickerText.rectTransform.sizeDelta = new Vector2(Mathf.Max(loopWidth, messageWidth), 0f);
        }

        private float MeasureStageTicker(string text)
        {
            TextGenerationSettings settings = stageTickerText.GetGenerationSettings(Vector2.zero);
            return stageTickerText.cachedTextGeneratorForLayout.GetPreferredWidth(text, settings) / stageTickerText.pixelsPerUnit;
        }

        /// <summary>
        /// Types the message out; if it's too long for the window, the line slides left to keep the cursor in view,
        /// holds briefly, then loops as a marquee from wherever typing left it.
        /// </summary>
        private void UpdateStageTicker(float dt, bool cursorOn)
        {
            stageTickerTime += dt;
            int visible = Mathf.Min(stageTickerTarget.Length, Mathf.FloorToInt(stageTickerTime * TickerCharsPerSecond));
            bool typing = visible < stageTickerTarget.Length;

            if (typing)
            {
                stageTickerText.text = stageTickerTarget.Substring(0, visible) + "_";
                stageTickerScrollOffset = stageTickerScrolls
                    ? Mathf.Max(0f, MeasureStageTicker(stageTickerText.text) - TickerViewportWidth)
                    : 0f;
            }
            else if (stageTickerScrolls)
            {
                stageTickerText.text = stageTickerLoopText;
                stageTickerHoldTime += dt;
                if (stageTickerHoldTime >= TickerScrollHold && stageTickerLoopPeriod > 0f)
                {
                    stageTickerScrollOffset = Mathf.Repeat(stageTickerScrollOffset + TickerScrollSpeed * dt, stageTickerLoopPeriod);
                }
            }
            else
            {
                stageTickerText.text = stageTickerTarget + (cursorOn ? "_" : " ");
                stageTickerScrollOffset = 0f;
            }

            stageTickerText.rectTransform.anchoredPosition = new Vector2(-Mathf.Round(stageTickerScrollOffset), 0f);
        }

        private void SelectStage(int stageIndex)
        {
            if (stageIndex < 0 || stageIndex >= availableStages.Count)
            {
                return;
            }

            // Until the tutorial is done or skipped, Coalesce only lets STAGE 00 open
            if (GuideRequiresTutorialStage && !IsTutorialStage(availableStages[stageIndex]))
            {
                StageMapNode waitingNode = FindStageNode(availableStages[stageIndex]);
                if (waitingNode != null) waitingNode.GetComponent<MenuButtonFeedback>()?.Wobble();
                PlayDenied();
                return;
            }

            if (!IsStageOpen(availableStages[stageIndex]))
            {
                // Wobble the node's feedback if it exists
                StageMapNode lockedNode = FindStageNode(availableStages[stageIndex]);
                if (lockedNode != null)
                {
                    var fb = lockedNode.GetComponent<MenuButtonFeedback>();
                    if (fb != null) fb.Wobble();
                }
                return;
            }

            selectedStageIndex = stageIndex;
            selectedStage = availableStages[stageIndex];
            stageDetailsTitle.text = selectedStage.mapName;
            stageDetailsDescription.text = selectedStage.shortDescription;
            bool tutorial = IsTutorialStage(selectedStage);
            int stageNumber = tutorial ? 0 : stageIndex + 1;
            if (stageDetailsTag != null) stageDetailsTag.text = "STAGE " + stageNumber.ToString("00");
            if (stageDetailsNumber != null) stageDetailsNumber.text = stageNumber.ToString("00");
            if (stageDetailsStats != null)
            {
                stageDetailsStats.text = $"GRID {selectedStage.gridWidth}x{selectedStage.gridHeight}   //   SQUAD {selectedStage.squadSizeLimit}";
            }
            if (!tutorial) selectedDifficulty = PickOpenDifficulty(selectedStage, selectedDifficulty);
            UpdateDifficultyDisplay();

            // The tutorial's dossier is just the preview, name, brief and Back / Play
            SetDetailsBlockVisible(stageDetailsStats, !tutorial);
            SetDetailsBlockVisible(stageDetailsDivider, !tutorial);
            SetDetailsBlockVisible(difficultyLabel, !tutorial);
            SetDetailsBlockVisible(difficultySliderRoot, !tutorial);
            SetDetailsBlockVisible(difficultyBriefing, !tutorial);

            // Highlight the selected node, dim others
            foreach (StageMapNode node in stageMapNodes)
            {
                if (node != null) node.SetSelected(node.Stage == selectedStage);
            }

            detailsOpen = true;
            SetBackgroundInteractable(false);
            SetPanelVisible(stageDetailsPanel, true, false, stageDetailsModal);
            PlayEntrance(detailsEntrance, ref detailsEntranceRoutine);

            // Boot the tactical map once the panel is active, so the preview lays out against its real size
            if (stagePreview != null)
            {
                stagePreview.Show(selectedStage);
                if (stagePreviewInfo != null)
                {
                    // One coloured entry per route; spawn markers share their route's colour
                    var legend = new System.Text.StringBuilder("ROUTES ");
                    for (int i = 0; i < stagePreview.RouteCount; i++)
                    {
                        string hex = ColorUtility.ToHtmlStringRGB(StageMapPreview.GetRouteColor(i));
                        legend.Append($" <color=#{hex}>▶{i + 1}</color>");
                    }
                    legend.Append("     [!] SPAWN (ROUTE COLOUR)     <color=#52FF99>[+]</color> EXIT");
                    stagePreviewInfo.text = legend.ToString();
                }
            }
            FocusUi(playStageButton != null ? playStageButton.gameObject : null);
        }

        // ─────────────────────────────────────────────────────
        //  Stage details modal + difficulty picker
        // ─────────────────────────────────────────────────────

        private static void SetDetailsBlockVisible(Component block, bool visible)
        {
            if (block != null && block.gameObject.activeSelf != visible) block.gameObject.SetActive(visible);
        }

        private void BuildStageDetailsUi()
        {
            stageDetailsPanel = new GameObject("StageDetailsPanel", typeof(RectTransform), typeof(Image));
            stageDetailsPanel.transform.SetParent(stageSelectionPanel.transform, false);
            StretchFill(stageDetailsPanel.GetComponent<RectTransform>());
            stageDetailsPanel.GetComponent<Image>().color = new Color(0.004f, 0.012f, 0.008f, 0.90f);

            // Click outside the card to close
            var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            backdrop.transform.SetParent(stageDetailsPanel.transform, false);
            StretchFill(backdrop.GetComponent<RectTransform>());
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var backdropButton = backdrop.GetComponent<Button>();
            backdropButton.transition = Selectable.Transition.None;
            backdropButton.navigation = new Navigation { mode = Navigation.Mode.None };
            backdropButton.onClick.AddListener(() => { PlayBack(); CloseStageDetails(); });

            // Centered terminal window: stage preview on the left, dossier + difficulty on the right
            var modal = new GameObject("StageDetailsModal", typeof(RectTransform), typeof(Image));
            modal.transform.SetParent(stageDetailsPanel.transform, false);
            stageDetailsModal = modal.GetComponent<RectTransform>();
            Place(stageDetailsModal, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(DossierWidth, DossierHeight));
            modal.GetComponent<Image>().color = TerminalPanel;

            var modalBorder = modal.AddComponent<Outline>();
            modalBorder.effectColor = TerminalLine;
            modalBorder.effectDistance = new Vector2(2f, -2f);
            AddCornerBrackets(stageDetailsModal, 30f, 3f, AccentBright, -6f);

            // Title bar
            var header = new GameObject("DossierHeader", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(modal.transform, false);
            StretchTop(header.GetComponent<RectTransform>(), 0f, 0f, 0f, DossierHeaderHeight);
            var headerImage = header.GetComponent<Image>();
            headerImage.color = TerminalHeader;
            headerImage.raycastTarget = false;

            var headerLabel = MakeText(header.transform, "DossierHeaderLabel", "◆ STAGE DOSSIER", 15, TextAnchor.MiddleLeft, AccentBright, FontStyle.Bold);
            headerLabel.rectTransform.offsetMin = new Vector2(24f, 0f);
            var headerStatus = MakeText(header.transform, "DossierHeaderStatus", "UPLINK STABLE  //  ROUTE SCAN ACTIVE", 12, TextAnchor.MiddleRight, TerminalText);
            headerStatus.rectTransform.offsetMax = new Vector2(-24f, 0f);

            // Accent stripe under the title bar, tinted by difficulty
            var modalAccent = new GameObject("ModalAccent", typeof(RectTransform), typeof(Image));
            modalAccent.transform.SetParent(modal.transform, false);
            StretchTop(modalAccent.GetComponent<RectTransform>(), 0f, 0f, DossierHeaderHeight, 3f);
            modalAccentImage = modalAccent.GetComponent<Image>();
            modalAccentImage.color = AccentColor;
            modalAccentImage.raycastTarget = false;

            // Stage preview (left)
            var previewFrame = new GameObject("StagePreviewFrame", typeof(RectTransform), typeof(Image));
            previewFrame.transform.SetParent(modal.transform, false);
            stagePreviewFrame = previewFrame.GetComponent<RectTransform>();
            Place(stagePreviewFrame, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(DossierPadding, -(DossierHeaderHeight + 24f)), new Vector2(PreviewFrameWidth, DossierHeight - DossierHeaderHeight - 24f - DossierPadding));
            var previewFrameImage = previewFrame.GetComponent<Image>();
            previewFrameImage.color = new Color(0.008f, 0.022f, 0.015f, 1f);
            previewFrameImage.raycastTarget = false;
            var previewOutline = previewFrame.AddComponent<Outline>();
            previewOutline.effectColor = new Color(TerminalLine.r, TerminalLine.g, TerminalLine.b, 0.30f);
            previewOutline.effectDistance = new Vector2(1f, -1f);
            AddCornerBrackets(stagePreviewFrame, 18f, 2f, AccentBright);

            var previewTitle = MakeText(previewFrame.transform, "PreviewTitle", "TACTICAL MAP  //  ENEMY ROUTES", 12, TextAnchor.MiddleLeft, TerminalText, FontStyle.Bold);
            StretchTop(previewTitle.rectTransform, 16f, 16f, 8f, 20f);

            // No Graphic here: StageMapPreview puts its CRT monitor (a RawImage) on this object
            var screen = new GameObject("StagePreviewScreen", typeof(RectTransform));
            screen.transform.SetParent(previewFrame.transform, false);
            var screenRect = screen.GetComponent<RectTransform>();
            screenRect.anchorMin = Vector2.zero;
            screenRect.anchorMax = Vector2.one;
            screenRect.offsetMin = new Vector2(12f, 40f);
            screenRect.offsetMax = new Vector2(-12f, -34f);
            stagePreview = screen.AddComponent<StageMapPreview>();

            stagePreviewInfo = MakeText(previewFrame.transform, "PreviewLegend",
                "<color=#FF5C42>▶▶</color> ENEMY ROUTE    <color=#FF5C42>[!]</color> SPAWN    <color=#52FF99>[+]</color> EXIT", 12,
                TextAnchor.MiddleLeft, TerminalText);
            StretchBottom(stagePreviewInfo.rectTransform, 16f, 16f, 10f, 22f);

            // Dossier column (right)
            var column = new GameObject("DossierColumn", typeof(RectTransform));
            column.transform.SetParent(modal.transform, false);
            stageDetailsColumn = column.GetComponent<RectTransform>();
            stageDetailsColumn.anchorMin = Vector2.zero;
            stageDetailsColumn.anchorMax = Vector2.one;
            stageDetailsColumn.offsetMin = new Vector2(DossierPadding + PreviewFrameWidth + 32f, DossierPadding);
            stageDetailsColumn.offsetMax = new Vector2(-DossierPadding, -(DossierHeaderHeight + 24f));

            // Faint stage number watermark
            stageDetailsNumber = MakeText(column.transform, "StageDetailsNumber", "01", 130, TextAnchor.UpperRight,
                new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.07f), FontStyle.Bold);
            stageDetailsNumber.horizontalOverflow = HorizontalWrapMode.Overflow;
            stageDetailsNumber.verticalOverflow = VerticalWrapMode.Overflow;
            Place(stageDetailsNumber.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 10f), new Vector2(260f, 150f));

            stageDetailsTag = MakeText(column.transform, "StageDetailsTag", "STAGE 01", 13, TextAnchor.MiddleLeft, AccentBright, FontStyle.Bold);
            StretchTop(stageDetailsTag.rectTransform, 0f, 0f, 0f, 22f);

            stageDetailsTitle = MakeText(column.transform, "StageDetailsTitle", "Select a stage", 34, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            stageDetailsTitle.resizeTextForBestFit = true;
            stageDetailsTitle.resizeTextMinSize = 20;
            stageDetailsTitle.resizeTextMaxSize = 34;
            StretchTop(stageDetailsTitle.rectTransform, 0f, 0f, 24f, 52f);

            // Column blocks (622 px tall) are spaced evenly, DetailsGap apart: name (0..76), description,
            // stats + divider, difficulty label + slider, info strip, and the Back / Play buttons at the bottom.
            stageDetailsDescription = MakeText(column.transform, "StageDetailsDescription", "", 15, TextAnchor.UpperLeft, new Color(0.62f, 0.76f, 0.68f, 1f));
            FitText(stageDetailsDescription, 11, 15);
            stageDetailsDescription.lineSpacing = 1.25f;
            StretchTop(stageDetailsDescription.rectTransform, 0f, 0f, DetailsDescriptionTop, DetailsDescriptionHeight);

            stageDetailsStats = MakeText(column.transform, "StageDetailsStats", "", 12, TextAnchor.MiddleLeft, TerminalText, FontStyle.Bold);
            FitText(stageDetailsStats, 10, 12);
            StretchTop(stageDetailsStats.rectTransform, 0f, 0f, DetailsStatsTop, 22f);

            var divider = new GameObject("DetailsDivider", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(column.transform, false);
            stageDetailsDivider = divider.transform;
            StretchTop(divider.GetComponent<RectTransform>(), 0f, 0f, DetailsStatsTop + 30f, 2f);
            var dividerImage = divider.GetComponent<Image>();
            dividerImage.color = new Color(TerminalLine.r, TerminalLine.g, TerminalLine.b, 0.25f);
            dividerImage.raycastTarget = false;

            // Footer buttons
            stageDetailsBackButton = CreateButtonChild(column.transform, "StageDetailsBackButton", "◀  BACK");
            StyleTerminalButton(stageDetailsBackButton, TerminalText, 15);
            Place(stageDetailsBackButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(140f, 56f));

            playStageButton = CreateButtonChild(column.transform, "PlayStageButton", "▶  PLAY");
            playStageButton.GetComponent<Image>().color = AccentColor;
            var playBtnLabel = playStageButton.GetComponentInChildren<Text>();
            playStageLabel = playBtnLabel;
            if (playBtnLabel != null) { playBtnLabel.fontSize = 22; playBtnLabel.fontStyle = FontStyle.Bold; playBtnLabel.color = new Color(0.02f, 0.08f, 0.04f, 1f); }
            Place(playStageButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(250f, 62f));
            AddCornerBrackets(playStageButton.GetComponent<RectTransform>(), 12f, 2f, new Color(0.85f, 1f, 0.90f, 0.9f), -5f);

            stageDetailsPanel.SetActive(false);
        }

        private const float DossierWidth = 1300f;
        private const float DossierHeight = 720f;
        private const float DossierHeaderHeight = 46f;
        private const float DossierPadding = 28f;

        // Details column layout (column pixels from the top; the column is 622 px tall, Back / Play fill 560..622).
        // Blocks: name 0..76, description 96 px, stats + divider 32 px, difficulty label + slider 82 px, info strip
        // 80 px, buttons 62 px; the 194 px left over is split evenly into five gaps.
        private const float DetailsGap = 39f;
        private const float DetailsDescriptionTop = 76f + DetailsGap;                                   // 115
        private const float DetailsDescriptionHeight = 96f;
        private const float DetailsStatsTop = DetailsDescriptionTop + DetailsDescriptionHeight + DetailsGap; // 250
        private const float DifficultyLabelTop = DetailsStatsTop + 32f + DetailsGap;                   // 321
        private const float DifficultyBriefingTop = DifficultyLabelTop + 82f + DetailsGap;             // 442
        private const float DifficultyBriefingHeight = 80f;
        private const float PreviewFrameWidth = 800f;

        /// <summary>Four L-shaped terminal corner brackets; a negative inset pushes them outside the rect.</summary>
        private static void AddCornerBrackets(RectTransform target, float length, float thickness, Color color, float inset = 0f)
        {
            if (target == null) return;

            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2, i / 2);
                var inward = new Vector2(corner.x < 0.5f ? 1f : -1f, corner.y < 0.5f ? 1f : -1f);
                CreateBracketBar(target, corner, inward * inset, new Vector2(length, thickness), color);
                CreateBracketBar(target, corner, inward * inset, new Vector2(thickness, length), color);
            }
        }

        private static void CreateBracketBar(RectTransform parent, Vector2 corner, Vector2 offset, Vector2 size, Color color)
        {
            var go = new GameObject("CornerBracket", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), corner, corner, offset, size);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        /// <summary>Dark green terminal button with bracket corners (MenuButtonFeedback still owns hover/glow).</summary>
        private static void StyleTerminalButton(Button button, Color labelColor, int fontSize)
        {
            if (button == null) return;

            button.GetComponent<Image>().color = TerminalButton;
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.fontSize = fontSize;
                label.color = labelColor;
            }

            if (button.transform.Find("CornerBracket") == null)
            {
                AddCornerBrackets(button.GetComponent<RectTransform>(), 10f, 2f, TerminalLine);
            }
        }

        /// <summary>
        /// Difficulty picker in the stage details column, kept compact: a "> DIFFICULTY" label, a stepped pixel
        /// slider (EASY / NORMAL / HARD notches; press anywhere on it, drag, or use the arrow keys) and one framed
        /// strip with the choice, its waves and lives, and a one-line blurb. Colours follow the selection.
        /// </summary>
        private void BuildDifficultySelector()
        {
            if (stageDetailsPanel == null || difficultySlider != null) return;

            Transform parent = stageDetailsColumn != null ? (Transform)stageDetailsColumn
                : stageDetailsModal != null ? (Transform)stageDetailsModal : stageDetailsPanel.transform;

            difficultyLabel = MakeText(parent, "DifficultyLabel", "> DIFFICULTY", 12, TextAnchor.MiddleLeft, TerminalText, FontStyle.Bold);
            StretchTop(difficultyLabel.rectTransform, 0f, 0f, DifficultyLabelTop, 20f);

            BuildDifficultySlider(parent);

            // Info strip under the slider: ◆ NORMAL ... WAVES 20 / LIVES 15, blurb underneath
            var briefing = new GameObject("DifficultyBriefing", typeof(RectTransform), typeof(Image));
            briefing.transform.SetParent(parent, false);
            difficultyBriefing = briefing.GetComponent<RectTransform>();
            StretchTop(difficultyBriefing, 0f, 0f, DifficultyBriefingTop, DifficultyBriefingHeight);
            var briefingImage = briefing.GetComponent<Image>();
            briefingImage.color = TerminalPlate;
            briefingImage.raycastTarget = false;
            AddCornerBrackets(difficultyBriefing, 10f, 2f, TerminalLine);

            // Heading on the left half, waves/lives right-aligned in the right half (they never touch), blurb below
            difficultyHeading = MakeText(briefing.transform, "DifficultyHeading", "◆ NORMAL", 14, TextAnchor.MiddleLeft, TextDim, FontStyle.Bold);
            FitText(difficultyHeading, 11, 14);
            StretchTop(difficultyHeading.rectTransform, 18f, 18f, 16f, 22f);
            difficultyHeading.rectTransform.anchorMax = new Vector2(0.36f, 1f);

            difficultyStats = MakeText(briefing.transform, "DifficultyStats", "", 12, TextAnchor.MiddleRight, TerminalText, FontStyle.Bold);
            FitText(difficultyStats, 10, 12);
            StretchTop(difficultyStats.rectTransform, 18f, 18f, 16f, 22f);
            difficultyStats.rectTransform.anchorMin = new Vector2(0.36f, 1f);

            difficultyBlurb = MakeText(briefing.transform, "DifficultyBlurb", "", 11, TextAnchor.MiddleLeft, new Color(0.62f, 0.76f, 0.68f, 0.85f));
            FitText(difficultyBlurb, 9, 11);
            StretchTop(difficultyBlurb.rectTransform, 18f, 18f, 46f, 20f);
            difficultyBlurb.rectTransform.anchorMax = new Vector2(0.62f, 1f);

            difficultyRecord = MakeText(briefing.transform, "DifficultyRecord", "", 11, TextAnchor.MiddleRight, TextDim, FontStyle.Bold);
            FitText(difficultyRecord, 9, 11);
            StretchTop(difficultyRecord.rectTransform, 18f, 18f, 46f, 20f);
            difficultyRecord.rectTransform.anchorMin = new Vector2(0.62f, 1f);
        }

        /// <summary>Lets a Text shrink (down to minSize) instead of being cut off by its rect.</summary>
        private static void FitText(Text text, int minSize, int maxSize)
        {
            if (text == null) return;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = minSize;
            text.resizeTextMaxSize = maxSize;
        }

        // Difficulty slider geometry (column pixels): the slide area is inset so the knob and the end labels stay
        // inside the column; notches, labels and the knob all use the same 0 / 0.5 / 1 positions.
        private const float DifficultySliderTop = DifficultyLabelTop + 30f;
        private const float DifficultySliderInset = 46f;
        private const float DifficultyTrackHeight = 10f;
        private static readonly Color DifficultyTrackColor = new Color(0.020f, 0.050f, 0.034f, 1f);

        /// <summary>
        /// Stepped pixel slider: an outlined track with a fill in the selected colour, three square notches, a
        /// blocky knob (outline, colour, highlight row) and the tier names under the notches. A Unity Slider
        /// (whole numbers 0-2) drives it, with a transparent hit area over the whole row so a press anywhere jumps
        /// to the nearest notch.
        /// </summary>
        private void BuildDifficultySlider(Transform parent)
        {
            var root = new GameObject("DifficultySlider", typeof(RectTransform), typeof(Image), typeof(Slider));
            root.transform.SetParent(parent, false);
            var rootRect = root.GetComponent<RectTransform>();
            StretchTop(rootRect, 0f, 0f, DifficultySliderTop, 52f);
            Image hitArea = root.GetComponent<Image>();
            hitArea.color = new Color(0f, 0f, 0f, 0f);
            hitArea.raycastTarget = true;
            difficultySliderRoot = rootRect;

            // Track: dark channel with a 2 px outline
            var trackOutline = CreateBlock(root.transform, "TrackOutline", new Color(TerminalLine.r, TerminalLine.g, TerminalLine.b, 0.45f));
            StretchTop(trackOutline.rectTransform, DifficultySliderInset - 4f, DifficultySliderInset - 4f, 7f, DifficultyTrackHeight + 4f);
            var track = CreateBlock(root.transform, "Track", DifficultyTrackColor);
            StretchTop(track.rectTransform, DifficultySliderInset - 2f, DifficultySliderInset - 2f, 9f, DifficultyTrackHeight);

            // Fill and knob live in a slide area matching the notch positions
            var slideArea = new GameObject("SlideArea", typeof(RectTransform));
            slideArea.transform.SetParent(root.transform, false);
            var slideRect = slideArea.GetComponent<RectTransform>();
            StretchTop(slideRect, DifficultySliderInset, DifficultySliderInset, 9f, DifficultyTrackHeight);

            difficultyFill = CreateBlock(slideArea.transform, "Fill", AccentColor);
            difficultyFill.rectTransform.anchorMin = Vector2.zero;
            difficultyFill.rectTransform.anchorMax = new Vector2(0f, 1f);
            difficultyFill.rectTransform.offsetMin = new Vector2(0f, 2f);
            difficultyFill.rectTransform.offsetMax = new Vector2(0f, -2f);

            // Tier names sit in a strip with the same insets as the slide area, so they line up with the notches
            var labelArea = new GameObject("LabelArea", typeof(RectTransform));
            labelArea.transform.SetParent(root.transform, false);
            var labelAreaRect = labelArea.GetComponent<RectTransform>();
            StretchTop(labelAreaRect, DifficultySliderInset, DifficultySliderInset, 34f, 16f);

            // Notches at 0 / 0.5 / 1
            difficultyNotches = new Image[DifficultyOrder.Length];
            difficultyNotchLabels = new Text[DifficultyOrder.Length];
            for (int i = 0; i < DifficultyOrder.Length; i++)
            {
                float fraction = DifficultyOrder.Length > 1 ? i / (float)(DifficultyOrder.Length - 1) : 0f;
                Image notch = CreateBlock(slideArea.transform, "Notch" + i, Color.white);
                notch.rectTransform.anchorMin = notch.rectTransform.anchorMax = new Vector2(fraction, 0.5f);
                notch.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                notch.rectTransform.sizeDelta = new Vector2(6f, 6f);
                difficultyNotches[i] = notch;

                Text label = MakeText(labelArea.transform, "NotchLabel" + i, DifficultyOrder[i].ToString().ToUpperInvariant(), 11,
                    TextAnchor.MiddleCenter, TextDim, FontStyle.Bold);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                RectTransform labelRect = label.rectTransform;
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(fraction, 0.5f);
                labelRect.pivot = new Vector2(0.5f, 0.5f);
                labelRect.anchoredPosition = Vector2.zero;
                labelRect.sizeDelta = new Vector2(100f, 16f);
                difficultyNotchLabels[i] = label;
            }

            // Knob: outline block, colour block, highlight row, dark centre slit
            var handleArea = new GameObject("HandleArea", typeof(RectTransform));
            handleArea.transform.SetParent(root.transform, false);
            var handleAreaRect = handleArea.GetComponent<RectTransform>();
            StretchTop(handleAreaRect, DifficultySliderInset, DifficultySliderInset, 0f, 28f);

            Image knobOutline = CreateBlock(handleArea.transform, "Knob", new Color(0.02f, 0.05f, 0.03f, 1f));
            knobOutline.rectTransform.sizeDelta = new Vector2(20f, 0f);
            difficultyKnob = CreateBlock(knobOutline.transform, "KnobFace", AccentColor);
            SetBlockInset(difficultyKnob.rectTransform, 2f);
            difficultyKnobHighlight = CreateBlock(difficultyKnob.transform, "KnobHighlight", new Color(1f, 1f, 1f, 0.45f));
            difficultyKnobHighlight.rectTransform.anchorMin = new Vector2(0f, 1f);
            difficultyKnobHighlight.rectTransform.anchorMax = new Vector2(1f, 1f);
            difficultyKnobHighlight.rectTransform.pivot = new Vector2(0.5f, 1f);
            difficultyKnobHighlight.rectTransform.offsetMin = new Vector2(0f, -3f);
            difficultyKnobHighlight.rectTransform.offsetMax = Vector2.zero;
            Image slit = CreateBlock(difficultyKnob.transform, "KnobSlit", new Color(0f, 0f, 0f, 0.35f));
            slit.rectTransform.anchorMin = new Vector2(0.5f, 0.2f);
            slit.rectTransform.anchorMax = new Vector2(0.5f, 0.75f);
            slit.rectTransform.sizeDelta = new Vector2(2f, 0f);

            difficultySlider = root.GetComponent<Slider>();
            difficultySlider.fillRect = difficultyFill.rectTransform;
            difficultySlider.handleRect = knobOutline.rectTransform;
            difficultySlider.targetGraphic = difficultyKnob;
            difficultySlider.transition = Selectable.Transition.None;
            difficultySlider.direction = Slider.Direction.LeftToRight;
            difficultySlider.minValue = 0f;
            difficultySlider.maxValue = DifficultyOrder.Length - 1;
            difficultySlider.wholeNumbers = true;
            difficultySlider.SetValueWithoutNotify(System.Array.IndexOf(DifficultyOrder, selectedDifficulty));
            difficultySlider.onValueChanged.AddListener(value =>
            {
                int index = Mathf.Clamp(Mathf.RoundToInt(value), 0, DifficultyOrder.Length - 1);
                if (DifficultyOrder[index] == selectedDifficulty) return;
                PlaySfx(clickClip, 0.9f + 0.1f * index);
                SetDifficulty(DifficultyOrder[index]);
            });
        }

        private static Image CreateBlock(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void SetBlockInset(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private void SetDifficulty(StageDifficulty difficulty)
        {
            selectedDifficulty = difficulty;
            UpdateDifficultyDisplay();
        }

        private static readonly Color LockedDifficultyColor = new Color(0.55f, 0.58f, 0.56f, 1f);

        private bool IsDifficultyOpen(StageDifficulty difficulty) =>
            selectedStage == null || IsTutorialStage(selectedStage) || StageProgress.IsUnlocked(selectedStage.stageId, difficulty);

        /// <summary>Keeps the current difficulty if it's open on this stage, else Normal, Easy, then Hard.</summary>
        private static StageDifficulty PickOpenDifficulty(StageData stage, StageDifficulty preferred)
        {
            if (StageProgress.IsUnlocked(stage.stageId, preferred)) return preferred;
            foreach (StageDifficulty difficulty in new[] { StageDifficulty.Normal, StageDifficulty.Easy, StageDifficulty.Hard })
            {
                if (StageProgress.IsUnlocked(stage.stageId, difficulty)) return difficulty;
            }
            return preferred;
        }

        private void UpdateDifficultyDisplay()
        {
            Color selectedTint = GetDifficultyColor(selectedDifficulty);
            modalAccentTarget = selectedTint;

            if (difficultySlider != null)
            {
                int selectedIndex = System.Array.IndexOf(DifficultyOrder, selectedDifficulty);
                difficultySlider.SetValueWithoutNotify(selectedIndex);
                difficultyFill.color = new Color(selectedTint.r, selectedTint.g, selectedTint.b, 0.85f);
                difficultyKnob.color = selectedTint;

                for (int i = 0; i < DifficultyOrder.Length; i++)
                {
                    Color tint = GetDifficultyColor(DifficultyOrder[i]);
                    bool reached = i <= selectedIndex;
                    bool isSelected = i == selectedIndex;
                    difficultyNotches[i].color = reached ? Color.Lerp(tint, Color.white, 0.35f) : new Color(1f, 1f, 1f, 0.18f);
                    bool open = IsDifficultyOpen(DifficultyOrder[i]);
                    difficultyNotchLabels[i].color = isSelected ? (open ? tint : LockedDifficultyColor)
                        : new Color(TextDim.r, TextDim.g, TextDim.b, open ? 0.8f : 0.35f);
                }
            }

            if (difficultyHeading != null)
            {
                difficultyHeading.text = $"◆ {selectedDifficulty.ToString().ToUpperInvariant()}";
                difficultyHeading.color = selectedTint;
            }

            bool unlocked = IsSelectedDifficultyUnlocked;
            if (difficultyBlurb != null)
            {
                difficultyBlurb.text = GetDifficultyBlurb(selectedDifficulty);
            }

            if (difficultyRecord != null)
            {
                if (selectedStage == null || IsTutorialStage(selectedStage))
                {
                    difficultyRecord.text = string.Empty;
                }
                else if (!unlocked)
                {
                    difficultyRecord.text = "LOCKED // " + StageProgress.GetUnlockRequirement(selectedStage.stageId, selectedDifficulty);
                    difficultyRecord.color = LockedDifficultyColor;
                }
                else
                {
                    int best = StageProgress.GetBestStars(selectedStage.stageId, selectedDifficulty);
                    difficultyRecord.text = best > 0
                        ? "BEST " + new string('★', best) + "<color=#3C424D>" + new string('☆', 3 - best) + "</color>"
                        : "NOT CLEARED";
                    difficultyRecord.color = best > 0 ? new Color(0.98f, 0.78f, 0.28f, 1f) : TextDim;
                }
            }

            if (playStageButton != null)
            {
                Color playColor = unlocked ? AccentColor : new Color(0.20f, 0.24f, 0.22f, 1f);
                playStageButton.GetComponent<Image>().color = playColor;
                var playFeedback = playStageButton.GetComponent<MenuButtonFeedback>();
                if (playFeedback != null) playFeedback.SetBaseColor(playColor); // it lerps the image back to its base
                if (playStageLabel != null) playStageLabel.text = unlocked ? "▶  PLAY" : "LOCKED";
            }

            if (difficultyStats != null)
            {
                int waves = GetSelectedStageWaveCount(selectedDifficulty);
                int lives = selectedStage != null ? selectedStage.GetLifePoints(selectedDifficulty) : 0;
                difficultyStats.text = selectedStage != null
                    ? $"WAVES {(waves > 0 ? waves.ToString() : "?")} / LIVES {lives}"
                    : string.Empty;
            }
        }

        private int GetSelectedStageWaveCount(StageDifficulty difficulty)
        {
            if (selectedStage == null) return 0;

            WaveData[] waves = selectedStage.GetWaves(difficulty);
            if (waves != null && waves.Length > 0) return waves.Length;
            if (selectedStage.stageId != "STAGE_01" && selectedStage.stageId != "STAGE_02") return 0;

            return difficulty switch
            {
                StageDifficulty.Easy => 10,
                StageDifficulty.Normal => 20,
                StageDifficulty.Hard => 30,
                _ => 0
            };
        }

        private static Color GetDifficultyColor(StageDifficulty difficulty)
        {
            switch (difficulty)
            {
                case StageDifficulty.Easy: return new Color(0.30f, 0.86f, 0.58f, 1f);
                case StageDifficulty.Hard: return new Color(0.95f, 0.33f, 0.36f, 1f);
                default: return new Color(0.98f, 0.78f, 0.28f, 1f);
            }
        }

        private static int GetDifficultyLevel(StageDifficulty difficulty)
        {
            switch (difficulty)
            {
                case StageDifficulty.Easy: return 1;
                case StageDifficulty.Hard: return 3;
                default: return 2;
            }
        }

        private static string GetDifficultyBlurb(StageDifficulty difficulty)
        {
            switch (difficulty)
            {
                case StageDifficulty.Easy: return "Relaxed pace. Room to experiment.";
                case StageDifficulty.Hard: return "Relentless waves. No mercy.";
                default: return "Balanced waves. The intended experience.";
            }
        }

        private void PlaySelectedStage()
        {
            if (isLoadingStage || selectedStage == null || !IsStagePlayable(selectedStage.stageId))
            {
                return;
            }

            if (!IsSelectedDifficultyUnlocked)
            {
                var playFeedback = playStageButton != null ? playStageButton.GetComponent<MenuButtonFeedback>() : null;
                if (playFeedback != null) playFeedback.Wobble();
                PlayDenied();
                return;
            }

            SetPendingStage(selectedStage);
            SetPendingStageDifficulty(selectedDifficulty);
            isLoadingStage = true;

            if (overlayRoutine != null) StopCoroutine(overlayRoutine);
            StartCoroutine(LoadStageRoutine());
        }

        private IEnumerator LoadStageRoutine()
        {
            showLoadingLabel = true;
            yield return FadeOverlayRoutine(0f, 1f, 0.35f);
            SceneManager.LoadScene(GameplaySceneName);
        }

        private void ExitGame()
        {
            Debug.Log("Exit button pressed.");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ─────────────────────────────────────────────────────
        //  Feedback (sounds + hover/press component)
        // ─────────────────────────────────────────────────────

        private void AttachMenuFeedback()
        {
            // PLAY / EXIT get theirs from StyleMainMenuButton
            MenuButtonFeedback backFeedback = AttachFeedback(backButton, null, TextDim, 1.04f);
            if (backFeedback != null) backFeedback.Clicked = null;
            MenuButtonFeedback detailsBackFeedback = AttachFeedback(stageDetailsBackButton, null, TextDim, 1.04f);
            if (detailsBackFeedback != null) detailsBackFeedback.Clicked = null;
            AttachFeedback(playStageButton, () => PlaySfx(clickClip, 1.15f), AccentBright, 1.05f);
        }

        private MenuButtonFeedback AttachFeedback(Button button, System.Action clicked, Color glow, float hoverScale)
        {
            if (button == null) return null;

            // Feedback component drives the visuals, so disable Unity's color tint transition.
            button.transition = Selectable.Transition.None;

            var feedback = button.GetComponent<MenuButtonFeedback>();
            if (feedback == null) feedback = button.gameObject.AddComponent<MenuButtonFeedback>();

            feedback.hoverScale = hoverScale;
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
        private void PlayBack() { PlaySfx(backClip != null ? backClip : clickClip, 0.9f); }
        private void PlayDenied() { PlaySfx(deniedClip, 1f); }

        private void PlaySfx(AudioClip clip, float pitch)
        {
            AudioManager.Instance?.PlaySfx(clip, sfxVolume, pitch);
        }

        // ─────────────────────────────────────────────────────
        //  Panel fades + staggered entrances
        // ─────────────────────────────────────────────────────

        private void SetPanelVisible(GameObject panel, bool visible, bool instant, RectTransform popTarget = null)
        {
            if (panel == null) return;

            if (panelRoutines.TryGetValue(panel, out Coroutine running))
            {
                if (running != null) StopCoroutine(running);
                panelRoutines.Remove(panel);
            }

            CanvasGroup group = GetOrAddCanvasGroup(panel);

            if (instant || !isActiveAndEnabled)
            {
                panel.SetActive(visible);
                group.alpha = visible ? 1f : 0f;
                group.interactable = visible;
                group.blocksRaycasts = visible;
                if (popTarget != null) popTarget.localScale = Vector3.one;
                return;
            }

            if (!visible && !panel.activeSelf) return;

            panelRoutines[panel] = StartCoroutine(FadePanel(panel, group, visible, popTarget));
        }

        private IEnumerator FadePanel(GameObject panel, CanvasGroup group, bool show, RectTransform popTarget)
        {
            float duration = show ? 0.30f : 0.18f;
            float from = panel.activeSelf ? group.alpha : 0f;
            float to = show ? 1f : 0f;

            group.alpha = from;
            group.interactable = show;
            group.blocksRaycasts = show;

            if (show) panel.SetActive(true);
            if (show && popTarget != null) popTarget.localScale = Vector3.one * 0.9f;

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                group.alpha = Mathf.Lerp(from, to, EaseOutCubic(k));

                if (popTarget != null)
                {
                    float s = show ? Mathf.LerpUnclamped(0.9f, 1f, EaseOutBack(k)) : Mathf.Lerp(1f, 0.94f, k);
                    popTarget.localScale = new Vector3(s, s, 1f);
                }

                yield return null;
            }

            group.alpha = to;
            if (popTarget != null) popTarget.localScale = Vector3.one;
            if (!show) panel.SetActive(false);

            panelRoutines.Remove(panel);
        }

        private void RegisterEntranceItems()
        {
            mainEntrance.Clear();
            detailsEntrance.Clear();

            // Main menu
            RegisterEntrance(mainEntrance, bootLineText, Vector2.zero, 0.00f);
            RegisterEntrance(mainEntrance, titleBlock != null ? (Component)titleBlock : titleText, new Vector2(-70f, 0f), 0.05f);
            RegisterEntrance(mainEntrance, subtitleText, new Vector2(-50f, 0f), 0.12f);
            RegisterEntrance(mainEntrance, mainDivider, new Vector2(-50f, 0f), 0.16f);
            RegisterEntrance(mainEntrance, startButton, new Vector2(-70f, 0f), 0.22f);
            RegisterEntrance(mainEntrance, intelUI != null ? intelUI.IntelButton : null, new Vector2(-70f, 0f), 0.28f);
            RegisterEntrance(mainEntrance, audioSettingsUI != null ? audioSettingsUI.SettingsButton : null, new Vector2(-70f, 0f), 0.34f);
            RegisterEntrance(mainEntrance, exitButton, new Vector2(-70f, 0f), 0.40f);
            RegisterEntrance(mainEntrance, menuHintText, Vector2.zero, 0.48f);
            RegisterEntrance(mainEntrance, mainVersion, new Vector2(0f, 20f), 0.30f);
            RegisterEntrance(mainEntrance, menuFeedStatus, new Vector2(0f, 20f), 0.30f);

            // Stage selector header (cards were registered in BuildStageButtons)
            RegisterEntrance(stageEntrance, stageSidePanel, new Vector2(-90f, 0f), 0.00f);
            RegisterEntrance(stageEntrance, stageTitlePlate, new Vector2(-60f, 0f), 0.00f);
            RegisterEntrance(stageEntrance, stageTickerPlate, new Vector2(0f, -30f), 0.20f);
            RegisterEntrance(stageEntrance, stageHeaderTitle, new Vector2(-60f, 0f), 0.00f);
            RegisterEntrance(stageEntrance, stageZoneTitle, new Vector2(-60f, 0f), 0.03f);
            RegisterEntrance(stageEntrance, stageHeaderSubtitle, new Vector2(-40f, 0f), 0.06f);
            RegisterEntrance(stageEntrance, stageProgressText, new Vector2(40f, 0f), 0.12f);
            RegisterEntrance(stageEntrance, backButton, new Vector2(-40f, 0f), 0.16f);
            RegisterEntrance(stageEntrance, stageHint, Vector2.zero, 0.30f);
            RegisterEntrance(stageEntrance, mapNavigator != null ? mapNavigator.HudRoot : null, new Vector2(40f, 0f), 0.18f);

            // Stage details modal
            RegisterEntrance(detailsEntrance, stageDetailsNumber, Vector2.zero, 0.08f);
            RegisterEntrance(detailsEntrance, stageDetailsTag, new Vector2(0f, 12f), 0.05f);
            RegisterEntrance(detailsEntrance, stageDetailsTitle, new Vector2(0f, 12f), 0.08f);
            RegisterEntrance(detailsEntrance, stageDetailsDescription, new Vector2(0f, 12f), 0.12f);
            RegisterEntrance(detailsEntrance, stageDetailsStats, new Vector2(0f, 12f), 0.14f);
            RegisterEntrance(detailsEntrance, stagePreviewFrame, new Vector2(-24f, 0f), 0.02f);
            RegisterEntrance(detailsEntrance, difficultyLabel, new Vector2(0f, 12f), 0.16f);
            RegisterEntrance(detailsEntrance, difficultySliderRoot, new Vector2(0f, 12f), 0.22f);
            RegisterEntrance(detailsEntrance, difficultyBriefing, new Vector2(0f, 12f), 0.30f);
            RegisterEntrance(detailsEntrance, stageDetailsBackButton, new Vector2(0f, -12f), 0.38f);
            RegisterEntrance(detailsEntrance, playStageButton, new Vector2(0f, -12f), 0.42f);
        }

        private static void RegisterEntrance(List<EntranceItem> list, Component target, Vector2 offset, float delay, float scaleFrom = 1f)
        {
            if (target == null) return;

            var rt = target.GetComponent<RectTransform>();
            if (rt == null) return;

            // Layout groups own their children's positions, so only fade/scale those.
            if (rt.parent != null && rt.parent.GetComponent<LayoutGroup>() != null) offset = Vector2.zero;

            list.Add(new EntranceItem
            {
                rt = rt,
                group = GetOrAddCanvasGroup(rt.gameObject),
                feedback = rt.GetComponent<MenuButtonFeedback>(),
                target = rt.anchoredPosition,
                offset = offset,
                delay = delay,
                scaleFrom = scaleFrom
            });
        }

        private void PlayEntrance(List<EntranceItem> items, ref Coroutine routine)
        {
            if (items.Count == 0 || !isActiveAndEnabled) return;

            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(RunEntrance(items));
        }

        private IEnumerator RunEntrance(List<EntranceItem> items)
        {
            float total = 0f;
            foreach (var item in items)
            {
                ApplyEntrance(item, 0f);
                total = Mathf.Max(total, item.delay + EntranceDuration);
            }

            float t = 0f;
            while (t < total)
            {
                t += Time.unscaledDeltaTime;
                foreach (var item in items)
                {
                    float k = Mathf.Clamp01((t - item.delay) / EntranceDuration);
                    ApplyEntrance(item, EaseOutCubic(k));
                }

                yield return null;
            }

            foreach (var item in items) ApplyEntrance(item, 1f);
        }

        private static void ApplyEntrance(EntranceItem item, float k)
        {
            if (item.rt == null) return;

            item.group.alpha = k;
            if (item.offset != Vector2.zero) item.rt.anchoredPosition = item.target + item.offset * (1f - k);
            if (item.feedback != null) item.feedback.introScale = Mathf.Lerp(item.scaleFrom, 1f, k);
        }

        // ─────────────────────────────────────────────────────
        //  Scene fade overlay (fade-in on load, fade-out when starting a stage)
        // ─────────────────────────────────────────────────────

        private void EnsureFadeOverlay()
        {
            if (fadeOverlay != null) return;

            Canvas canvas = mainMenuPanel != null ? mainMenuPanel.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return;
            canvas = canvas.rootCanvas;

            var go = new GameObject("FadeOverlay", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvas.transform, false);
            StretchFill(go.GetComponent<RectTransform>());

            fadeOverlay = go.GetComponent<Image>();
            fadeOverlay.color = new Color(0.01f, 0.012f, 0.02f, 0f);
            fadeOverlay.raycastTarget = false;

            loadingLabel = MakeText(go.transform, "LoadingLabel", "LOADING...", 22, TextAnchor.MiddleCenter, new Color(TextDim.r, TextDim.g, TextDim.b, 0f));
            StretchFill(loadingLabel.rectTransform);
        }

        private void SetOverlayAlpha(float alpha)
        {
            Color c = fadeOverlay.color;
            c.a = alpha;
            fadeOverlay.color = c;

            if (loadingLabel != null)
            {
                Color l = loadingLabel.color;
                l.a = showLoadingLabel ? alpha : 0f;
                loadingLabel.color = l;
            }
        }

        private IEnumerator FadeOverlayRoutine(float from, float to, float duration)
        {
            EnsureFadeOverlay();
            if (fadeOverlay == null) yield break;

            fadeOverlay.transform.SetAsLastSibling();
            fadeOverlay.raycastTarget = true;
            SetOverlayAlpha(from);

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                SetOverlayAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(t / duration)));
                yield return null;
            }

            SetOverlayAlpha(to);
            fadeOverlay.raycastTarget = to > 0.01f;
        }

        // ─────────────────────────────────────────────────────
        //  Ambient background motes
        // ─────────────────────────────────────────────────────

        private void CreateAmbientLayer(Transform parent, int count)
        {
            var layerGo = new GameObject("AmbientLayer", typeof(RectTransform));
            layerGo.transform.SetParent(parent, false);
            var layer = layerGo.GetComponent<RectTransform>();
            StretchFill(layer);
            layer.SetAsFirstSibling();

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Mote", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(layer, false);

                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.zero;
                float size = Random.Range(4f, 11f);
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = new Vector2(Random.Range(0f, 1920f), Random.Range(0f, 1080f));
                rt.localRotation = Quaternion.Euler(0f, 0f, 45f);

                var image = go.GetComponent<Image>();
                image.raycastTarget = false;
                image.color = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0f);

                motes.Add(new Mote
                {
                    rt = rt,
                    layer = layer,
                    image = image,
                    speed = Random.Range(10f, 42f),
                    sway = Random.Range(6f, 22f),
                    phase = Random.Range(0f, Mathf.PI * 2f),
                    alpha = Random.Range(0.05f, 0.18f)
                });
            }
        }

        private void UpdateMotes(float dt, float time)
        {
            for (int i = 0; i < motes.Count; i++)
            {
                Mote m = motes[i];
                if (m.rt == null || !m.layer.gameObject.activeInHierarchy) continue;

                Vector2 size = m.layer.rect.size;
                if (size.x < 1f || size.y < 1f) size = new Vector2(1920f, 1080f);

                Vector2 pos = m.rt.anchoredPosition;
                pos.y += m.speed * dt;
                pos.x += Mathf.Sin(time * 0.6f + m.phase) * m.sway * dt;

                if (pos.y > size.y + 20f)
                {
                    pos.y = -20f;
                    pos.x = Random.value * size.x;
                }

                m.rt.anchoredPosition = pos;

                Color c = m.image.color;
                c.a = m.alpha * (0.6f + 0.4f * Mathf.Sin(time * 1.3f + m.phase * 2f));
                m.image.color = c;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Canvas / event system helpers
        // ─────────────────────────────────────────────────────

        private GameObject GetOrCreateCanvas()
        {
            var existingCanvas = CrtDisplay.FindGameCanvas();
            if (existingCanvas != null)
            {
                EnsureEventSystem();
                return existingCanvas.gameObject;
            }

            var canvas = new GameObject("MainMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvasComponent = canvas.GetComponent<Canvas>();
            canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();
            return canvas;
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

        // ─────────────────────────────────────────────────────
        //  UI building helpers
        // ─────────────────────────────────────────────────────

        private static CanvasGroup GetOrAddCanvasGroup(GameObject go)
        {
            var group = go.GetComponent<CanvasGroup>();
            if (group == null) group = go.AddComponent<CanvasGroup>();
            return group;
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

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Full-width strip hanging from the top edge of its parent.</summary>
        private static void StretchTop(RectTransform rt, float left, float right, float top, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -(top + height));
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Full-width strip sitting on the bottom edge of its parent.</summary>
        private static void StretchBottom(RectTransform rt, float left, float right, float bottom, float height)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, bottom + height);
        }

        private static Text MakeText(Transform parent, string name, string text, int fontSize, TextAnchor alignment, Color color, FontStyle style = FontStyle.Normal)
        {
            var t = CreateTextChild(parent, name, text, fontSize, alignment).GetComponent<Text>();
            t.color = color;
            t.fontStyle = style;
            t.raycastTarget = false;
            return t;
        }

        private static Button CreateButtonChild(Transform parent, string name, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220f, 60f);

            var image = go.GetComponent<Image>();
            image.color = new Color(0.15f, 0.18f, 0.24f, 1f);

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);

            var text = textGo.GetComponent<Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = 24;
            text.color = Color.white;
            text.raycastTarget = false;

            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            return go.GetComponent<Button>();
        }

        private static GameObject CreateTextChild(Transform parent, string name, string text, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var textComp = go.GetComponent<Text>();
            textComp.text = text;
            textComp.font = UIFontHelper.GetPixelFont();
            textComp.fontSize = fontSize;
            textComp.alignment = alignment;
            textComp.color = Color.white;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return go;
        }
    }

    /// <summary>
    /// Displays the sector lore card when hovering over the stage selection title block.
    /// </summary>
    public class StageHeaderLoreHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private CanvasGroup loreGroup;
        private int hoverCount;
        private float targetAlpha;

        public void Init(CanvasGroup group)
        {
            loreGroup = group;
            if (loreGroup != null)
            {
                loreGroup.alpha = 0f;
                loreGroup.blocksRaycasts = false;
            }
        }

        public void AddHover()
        {
            hoverCount++;
            targetAlpha = 1f;
            if (loreGroup != null) loreGroup.blocksRaycasts = true;
        }

        public void RemoveHover()
        {
            hoverCount = Mathf.Max(0, hoverCount - 1);
            if (hoverCount == 0)
            {
                targetAlpha = 0f;
                if (loreGroup != null) loreGroup.blocksRaycasts = false;
            }
        }

        public void OnPointerEnter(PointerEventData eventData) => AddHover();
        public void OnPointerExit(PointerEventData eventData) => RemoveHover();

        private void Update()
        {
            if (loreGroup == null) return;
            loreGroup.alpha = Mathf.MoveTowards(loreGroup.alpha, targetAlpha, Time.unscaledDeltaTime * 7f);
        }
    }

    public class StageHeaderLoreChildTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public StageHeaderLoreHover parentHover;
        public void OnPointerEnter(PointerEventData eventData) => parentHover?.AddHover();
        public void OnPointerExit(PointerEventData eventData) => parentHover?.RemoveHover();
    }
}
