using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using TrashTD.Audio;
using TrashTD.Data;

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
        private const string GameplaySceneName = "GameplayTest";
        private const float EntranceDuration = 0.45f;

        private static bool IsStagePlayable(string stageId)
        {
            return stageId == FirstPlayableStageId || stageId == SecondPlayableStageId;
        }

        // ── Palette ──────────────────────────────────────────
        private static readonly Color AccentColor = new Color(0.18f, 0.82f, 0.45f, 1f);
        private static readonly Color AccentBright = new Color(0.18f, 0.92f, 0.50f, 1f);
        private static readonly Color TitleDim = new Color(0.16f, 0.80f, 0.44f, 1f);
        private static readonly Color TitleBright = new Color(0.32f, 1.00f, 0.64f, 1f);
        private static readonly Color BackgroundColor = new Color(0.025f, 0.03f, 0.045f, 1f);
        private static readonly Color TextDim = new Color(0.50f, 0.58f, 0.68f, 1f);
        private static readonly Color DarkButton = new Color(0.12f, 0.14f, 0.18f, 1f);
        private static readonly Color CardColor = new Color(0.06f, 0.07f, 0.10f, 1f);
        private static readonly Color CardLockedColor = new Color(0.045f, 0.05f, 0.065f, 1f);
        private static readonly Color DifficultyCardColor = new Color(0.07f, 0.08f, 0.11f, 1f);

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
        [SerializeField] private Transform stageListRoot;
        [SerializeField] private Button stageButtonPrefab;
        [SerializeField] private GameObject stageDetailsPanel;
        [SerializeField] private Text stageDetailsTitle;
        [SerializeField] private Text stageDetailsDescription;
        [SerializeField] private Button playStageButton;
        [SerializeField] private Button stageDetailsBackButton;

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
        private readonly Dictionary<StageDifficulty, DifficultyCardView> difficultyCards = new Dictionary<StageDifficulty, DifficultyCardView>();
        private StageData selectedStage;
        private int selectedStageIndex = -1;
        private StageDifficulty selectedDifficulty = StageDifficulty.Normal;
        private Text difficultyHeading;
        private Text difficultyLabel;

        // Extra UI references created by the default layout
        private RectTransform stageDetailsModal;
        private Image modalAccentImage;
        private Color modalAccentTarget = AccentColor;
        private Text stageDetailsTag;
        private Text stageDetailsNumber;
        private Text stageProgressText;
        private RectTransform mainWatermark;
        private RectTransform mainDivider;
        private RectTransform mainVersion;
        private RectTransform stageHeaderTitle;
        private RectTransform stageHeaderSubtitle;
        private RectTransform stageHeaderDivider;
        private RectTransform stageHint;

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

        private sealed class DifficultyCardView
        {
            public Button button;
            public MenuButtonFeedback feedback;
            public Image tierBar;
            public Text nameText;
            public Text waveText;
            public Image[] pips;
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
            BuildDifficultySelector();
            AttachMenuFeedback();
            Canvas menuCanvas = mainMenuPanel != null ? mainMenuPanel.GetComponentInParent<Canvas>() : null;
            if (menuCanvas != null)
            {
                audioSettingsUI = AudioSettingsUI.Create(
                    menuCanvas.transform,
                    mainMenuPanel.transform,
                    button =>
                    {
                        button.GetComponent<Image>().color = DarkButton;
                        Text label = button.GetComponentInChildren<Text>();
                        if (label != null)
                        {
                            label.fontSize = 16;
                            label.color = new Color(0.6f, 0.65f, 0.72f, 1f);
                        }
                        AttachFeedback(button, PlayClick, TextDim, 1.04f);
                    },
                    rect => Place(rect, new Vector2(0f, 0.55f), new Vector2(0f, 0.5f), new Vector2(124f, -168f), new Vector2(200f, 48f)));
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

            // Slow breathing glow on the title
            if (titleText != null && mainMenuPanel != null && mainMenuPanel.activeInHierarchy)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 1.4f);
                titleText.color = Color.Lerp(TitleDim, TitleBright, pulse);
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
            if (detailsOpen)
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

            // Drifting ambient motes behind everything
            CreateAmbientLayer(mainMenuPanel.transform, 30);

            // Giant faint watermark on the right
            var watermark = MakeText(mainMenuPanel.transform, "Watermark", "TD", 360, TextAnchor.MiddleRight,
                new Color(1f, 1f, 1f, 0.035f), FontStyle.Bold);
            watermark.horizontalOverflow = HorizontalWrapMode.Overflow;
            watermark.verticalOverflow = VerticalWrapMode.Overflow;
            Place(watermark.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-90f, 0f), new Vector2(900f, 640f));
            watermark.transform.SetSiblingIndex(1);
            mainWatermark = watermark.rectTransform;

            // Decorative top accent stripe
            var accentStripe = new GameObject("AccentStripe", typeof(RectTransform), typeof(Image));
            accentStripe.transform.SetParent(mainMenuPanel.transform, false);
            var stripeRect = accentStripe.GetComponent<RectTransform>();
            stripeRect.anchorMin = new Vector2(0f, 1f);
            stripeRect.anchorMax = new Vector2(1f, 1f);
            stripeRect.pivot = new Vector2(0.5f, 1f);
            stripeRect.sizeDelta = new Vector2(0f, 4f);
            var stripeImage = accentStripe.GetComponent<Image>();
            stripeImage.color = AccentColor;
            stripeImage.raycastTarget = false;

            // Subtle vignette overlay — darker edges
            var vignette = new GameObject("Vignette", typeof(RectTransform), typeof(Image));
            vignette.transform.SetParent(mainMenuPanel.transform, false);
            StretchFill(vignette.GetComponent<RectTransform>());
            var vignetteImg = vignette.GetComponent<Image>();
            vignetteImg.color = new Color(0.01f, 0.015f, 0.025f, 0.35f);
            vignetteImg.raycastTarget = false;

            // ── Title — massive, left-aligned hero text ─────
            if (titleText == null)
            {
                titleText = MakeText(mainMenuPanel.transform, "TitleText", "TRASH TD", 88, TextAnchor.MiddleLeft,
                    AccentBright, FontStyle.Bold);
            }

            Place(titleText.rectTransform, new Vector2(0f, 0.55f), new Vector2(0f, 0.5f), new Vector2(120f, 70f), new Vector2(1000f, 120f));

            if (titleText.GetComponent<Shadow>() == null)
            {
                var titleShadow = titleText.gameObject.AddComponent<Shadow>();
                titleShadow.effectColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.35f);
                titleShadow.effectDistance = new Vector2(0f, -6f);
            }

            // ── Subtitle — underneath title, left-aligned ───
            if (subtitleText == null)
            {
                subtitleText = MakeText(mainMenuPanel.transform, "SubtitleText", "Tiny Reclaimers Against Spreading Hazard", 15,
                    TextAnchor.MiddleLeft, new Color(0.50f, 0.58f, 0.68f, 1f));
            }

            Place(subtitleText.rectTransform, new Vector2(0f, 0.55f), new Vector2(0f, 0.5f), new Vector2(124f, -10f), new Vector2(760f, 36f));

            // Decorative divider line below subtitle
            var divider = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(mainMenuPanel.transform, false);
            mainDivider = divider.GetComponent<RectTransform>();
            Place(mainDivider, new Vector2(0f, 0.55f), new Vector2(0f, 0.5f), new Vector2(124f, -42f), new Vector2(320f, 2f));
            var dividerImage = divider.GetComponent<Image>();
            dividerImage.color = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.4f);
            dividerImage.raycastTarget = false;

            // ── Buttons — left-aligned, stacked below title ─
            if (startButton == null)
            {
                startButton = CreateButtonChild(mainMenuPanel.transform, "StartButton", "▶  PLAY");
            }
            startButton.GetComponent<Image>().color = AccentColor;
            var startBtnLabel = startButton.GetComponentInChildren<Text>();
            if (startBtnLabel != null) startBtnLabel.fontSize = 22;
            Place(startButton.GetComponent<RectTransform>(), new Vector2(0f, 0.55f), new Vector2(0f, 0.5f), new Vector2(124f, -92f), new Vector2(300f, 64f));

            if (exitButton == null)
            {
                exitButton = CreateButtonChild(mainMenuPanel.transform, "ExitButton", "✕  EXIT");
            }
            exitButton.GetComponent<Image>().color = DarkButton;
            var exitBtnLabel = exitButton.GetComponentInChildren<Text>();
            if (exitBtnLabel != null) { exitBtnLabel.fontSize = 16; exitBtnLabel.color = new Color(0.6f, 0.65f, 0.72f, 1f); }
            Place(exitButton.GetComponent<RectTransform>(), new Vector2(0f, 0.55f), new Vector2(0f, 0.5f), new Vector2(124f, -224f), new Vector2(200f, 48f));

            // Version/footer text
            var versionText = MakeText(mainMenuPanel.transform, "VersionText", "v0.1  •  EARLY ACCESS", 11, TextAnchor.MiddleLeft,
                new Color(0.30f, 0.34f, 0.40f, 1f));
            Place(versionText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 18f), new Vector2(300f, 28f));
            mainVersion = versionText.rectTransform;

            // ── Stage Selection Panel ────────────────────────
            if (stageSelectionPanel == null)
            {
                stageSelectionPanel = new GameObject("StageSelectionPanel", typeof(RectTransform), typeof(Image));
                stageSelectionPanel.transform.SetParent(canvasGo.transform, false);
                stageSelectionPanel.GetComponent<Image>().color = BackgroundColor;
                StretchFill(stageSelectionPanel.GetComponent<RectTransform>());
                stageSelectionPanel.SetActive(false);
            }

            CreateAmbientLayer(stageSelectionPanel.transform, 22);

            // Header — left-aligned title block
            var headerTitle = MakeText(stageSelectionPanel.transform, "StageSelectorTitle", "SELECT STAGE", 52, TextAnchor.MiddleLeft,
                AccentBright, FontStyle.Bold);
            Place(headerTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(120f, -56f), new Vector2(900f, 70f));
            stageHeaderTitle = headerTitle.rectTransform;

            var headerSubtitle = MakeText(stageSelectionPanel.transform, "StageSelectorSubtitle", "CHOOSE YOUR DEPLOYMENT", 14, TextAnchor.MiddleLeft, TextDim);
            Place(headerSubtitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(124f, -130f), new Vector2(700f, 26f));
            stageHeaderSubtitle = headerSubtitle.rectTransform;

            var headerDivider = new GameObject("StageSelectorDivider", typeof(RectTransform), typeof(Image));
            headerDivider.transform.SetParent(stageSelectionPanel.transform, false);
            stageHeaderDivider = headerDivider.GetComponent<RectTransform>();
            Place(stageHeaderDivider, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(124f, -166f), new Vector2(320f, 2f));
            var headerDividerImage = headerDivider.GetComponent<Image>();
            headerDividerImage.color = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.4f);
            headerDividerImage.raycastTarget = false;

            stageProgressText = MakeText(stageSelectionPanel.transform, "StageProgressText", "", 16, TextAnchor.MiddleRight, TextDim);
            Place(stageProgressText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-120f, -82f), new Vector2(400f, 30f));

            var hint = MakeText(stageSelectionPanel.transform, "StageHint", "ESC  BACK", 12, TextAnchor.MiddleRight,
                new Color(0.30f, 0.34f, 0.40f, 1f));
            Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-120f, 70f), new Vector2(300f, 24f));
            stageHint = hint.rectTransform;

            if (backButton == null)
            {
                backButton = CreateButtonChild(stageSelectionPanel.transform, "BackButton", "◀  BACK");
            }
            backButton.GetComponent<Image>().color = DarkButton;
            var backBtnLabel = backButton.GetComponentInChildren<Text>();
            if (backBtnLabel != null) { backBtnLabel.fontSize = 16; backBtnLabel.color = new Color(0.6f, 0.65f, 0.72f, 1f); }
            Place(backButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(120f, 56f), new Vector2(200f, 48f));

            if (stageListRoot == null)
            {
                var stageListGo = new GameObject("StageList", typeof(RectTransform), typeof(GridLayoutGroup));
                stageListGo.transform.SetParent(stageSelectionPanel.transform, false);
                stageListRoot = stageListGo.transform;

                Place(stageListGo.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1180f, 470f));
            }

            var stageGrid = stageListRoot.GetComponent<GridLayoutGroup>();
            if (stageGrid == null)
            {
                stageGrid = stageListRoot.gameObject.AddComponent<GridLayoutGroup>();
            }

            stageGrid.cellSize = new Vector2(340f, 200f);
            stageGrid.spacing = new Vector2(28f, 28f);
            stageGrid.padding = new RectOffset(15, 15, 15, 15);
            stageGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            stageGrid.constraintCount = 3;
            stageGrid.childAlignment = TextAnchor.MiddleCenter;

            if (stageButtonPrefab == null)
            {
                stageButtonPrefab = CreateButtonChild(stageListRoot.transform, "StageButtonPrefab", "Stage");
                stageButtonPrefab.gameObject.name = "StageButtonPrefab";
                stageButtonPrefab.gameObject.SetActive(false);
            }

            if (stageDetailsPanel == null)
            {
                BuildStageDetailsUi();
            }
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

            PlayEntrance(mainEntrance, ref mainEntranceRoutine);
            FocusUi(startButton != null ? startButton.gameObject : null);
        }

        public void ShowStageSelection()
        {
            ShowStageSelectionInternal();
        }

        private void ShowStageSelectionInternal()
        {
            CardDraftOverlayUI.HideAllForMenuTransition();
            stageSelectionOpen = true;
            detailsOpen = false;
            SetBackgroundInteractable(true);

            SetPanelVisible(mainMenuPanel, false, true);
            SetPanelVisible(stageSelectionPanel, true, true);
            SetPanelVisible(stageDetailsPanel, false, true, stageDetailsModal);

            PlayEntrance(stageEntrance, ref stageEntranceRoutine);

            int focusIndex = selectedStageIndex >= 0 ? selectedStageIndex : 0;
            if (focusIndex < stageCards.Count && stageCards[focusIndex] != null)
            {
                FocusUi(stageCards[focusIndex].gameObject);
            }
        }

        public void CloseStageDetails()
        {
            if (!detailsOpen) return;

            detailsOpen = false;
            SetBackgroundInteractable(true);
            SetPanelVisible(stageDetailsPanel, false, false, stageDetailsModal);

            if (selectedStageIndex >= 0 && selectedStageIndex < stageCards.Count && stageCards[selectedStageIndex] != null)
            {
                FocusUi(stageCards[selectedStageIndex].gameObject);
            }
        }

        private void SetBackgroundInteractable(bool value)
        {
            // While the details modal is open, keep keyboard/gamepad navigation from drifting underneath it.
            if (stageListRoot != null) GetOrAddCanvasGroup(stageListRoot.gameObject).interactable = value;
            if (backButton != null) GetOrAddCanvasGroup(backButton.gameObject).interactable = value;
        }

        private static void FocusUi(GameObject target)
        {
            if (target == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(target);
        }

        // ─────────────────────────────────────────────────────
        //  Stage selector
        // ─────────────────────────────────────────────────────

        private void BuildStageButtons()
        {
            availableStages.Clear();
            stageCards.Clear();
            stageEntrance.Clear();

            string[] stageNames = new[]
            {
                "Stage 1", "Stage 2", "Stage 3",
                "Stage 4", "Stage 5", "Stage 6"
            };

            string[] stageDescriptions = new[]
            {
                "Clear the first wave of spreading hazard.",
                "Hold the line through the trash docks.",
                "Secure the scrapline junction.",
                "Reclaim the ruined market district.",
                "Push through the cinder dump.",
                "Take back the clockwork quarry."
            };

            for (int i = 0; i < stageNames.Length; i++)
            {
                string id = "STAGE_0" + (i + 1);
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

            if (stageListRoot == null)
            {
                return;
            }

            for (int i = stageListRoot.childCount - 1; i >= 0; i--)
            {
                var child = stageListRoot.GetChild(i);
                if (child.name != "StageButtonPrefab")
                {
                    Destroy(child.gameObject);
                }
            }

            int unlocked = 0;
            for (int i = 0; i < availableStages.Count; i++)
            {
                var stage = availableStages[i];
                var button = Instantiate(stageButtonPrefab, stageListRoot);
                button.gameObject.SetActive(true);
                button.name = "StageButton_" + (i + 1);

                bool isPlayable = IsStagePlayable(stage.stageId);
                if (isPlayable) unlocked++;

                StyleStageCard(button, stage, i, isPlayable);

                int stageIndex = i;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SelectStage(stageIndex));
            }

            if (stageProgressText != null)
            {
                stageProgressText.text = $"{unlocked} / {availableStages.Count} UNLOCKED";
            }
        }

        private void StyleStageCard(Button button, StageData stage, int index, bool isPlayable)
        {
            // Locked cards stay clickable so they can give "denied" feedback.
            button.interactable = true;

            var image = button.GetComponent<Image>();
            image.color = isPlayable ? CardColor : CardLockedColor;

            // Reuse the prefab label as the stage name
            Text nameText = button.GetComponentInChildren<Text>();
            if (nameText == null)
            {
                nameText = MakeText(button.transform, "Label", stage.mapName, 24, TextAnchor.MiddleLeft, Color.white);
            }
            nameText.text = stage.mapName;
            nameText.fontSize = 24;
            nameText.fontStyle = FontStyle.Bold;
            nameText.alignment = TextAnchor.MiddleLeft;
            nameText.color = isPlayable ? Color.white : new Color(0.45f, 0.49f, 0.55f, 1f);
            nameText.raycastTarget = false;
            StretchTop(nameText.rectTransform, 22f, 22f, 26f, 34f);

            // Big faint stage number
            var number = MakeText(button.transform, "StageNumber", (index + 1).ToString("00"), 96, TextAnchor.MiddleRight,
                new Color(1f, 1f, 1f, isPlayable ? 0.07f : 0.04f), FontStyle.Bold);
            number.horizontalOverflow = HorizontalWrapMode.Overflow;
            number.verticalOverflow = VerticalWrapMode.Overflow;
            Place(number.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-12f, -8f), new Vector2(220f, 130f));

            // Short description
            var description = MakeText(button.transform, "StageDescription", stage.shortDescription, 13, TextAnchor.UpperLeft,
                isPlayable ? new Color(0.58f, 0.65f, 0.74f, 1f) : new Color(0.32f, 0.36f, 0.42f, 1f));
            StretchTop(description.rectTransform, 22f, 22f, 70f, 60f);

            // Status line
            var status = MakeText(button.transform, "StageStatus", isPlayable ? "◆ AVAILABLE" : "✕ COMING SOON", 12, TextAnchor.MiddleLeft,
                isPlayable ? AccentBright : new Color(0.38f, 0.42f, 0.48f, 1f), FontStyle.Bold);
            StretchBottom(status.rectTransform, 22f, 22f, 18f, 22f);

            // Colored top accent stripe
            var cardAccent = new GameObject("CardAccent", typeof(RectTransform), typeof(Image));
            cardAccent.transform.SetParent(button.transform, false);
            var cardAccentRect = cardAccent.GetComponent<RectTransform>();
            cardAccentRect.anchorMin = new Vector2(0f, 1f);
            cardAccentRect.anchorMax = new Vector2(1f, 1f);
            cardAccentRect.pivot = new Vector2(0.5f, 1f);
            cardAccentRect.sizeDelta = new Vector2(0f, 3f);
            var accentImage = cardAccent.GetComponent<Image>();
            accentImage.color = isPlayable ? AccentColor : new Color(0.25f, 0.28f, 0.32f, 0.5f);
            accentImage.raycastTarget = false;

            // Feedback
            Color glow = isPlayable ? AccentBright : new Color(0.55f, 0.60f, 0.68f, 1f);
            System.Action clicked = isPlayable ? (System.Action)PlayClick : PlayDenied;
            var feedback = AttachFeedback(button, clicked, glow, isPlayable ? 1.045f : 1.01f);
            if (!isPlayable)
            {
                feedback.hoverBrighten = 0.03f;
                feedback.glowColor = new Color(glow.r, glow.g, glow.b, 0.35f);
            }

            stageCards.Add(feedback);
            RegisterEntrance(stageEntrance, button, Vector2.zero, 0.14f + index * 0.07f, 0.88f);
        }

        private void SelectStage(int stageIndex)
        {
            if (stageIndex < 0 || stageIndex >= availableStages.Count)
            {
                return;
            }

            if (!IsStagePlayable(availableStages[stageIndex].stageId))
            {
                if (stageIndex < stageCards.Count && stageCards[stageIndex] != null)
                {
                    stageCards[stageIndex].Wobble();
                }
                return;
            }

            selectedStageIndex = stageIndex;
            selectedStage = availableStages[stageIndex];
            stageDetailsTitle.text = selectedStage.mapName;
            stageDetailsDescription.text = selectedStage.shortDescription;
            if (stageDetailsTag != null) stageDetailsTag.text = "STAGE " + (stageIndex + 1).ToString("00");
            if (stageDetailsNumber != null) stageDetailsNumber.text = (stageIndex + 1).ToString("00");
            UpdateDifficultyDisplay();

            detailsOpen = true;
            SetBackgroundInteractable(false);
            SetPanelVisible(stageDetailsPanel, true, false, stageDetailsModal);
            PlayEntrance(detailsEntrance, ref detailsEntranceRoutine);
            FocusUi(playStageButton != null ? playStageButton.gameObject : null);
        }

        // ─────────────────────────────────────────────────────
        //  Stage details modal + difficulty picker
        // ─────────────────────────────────────────────────────

        private void BuildStageDetailsUi()
        {
            stageDetailsPanel = new GameObject("StageDetailsPanel", typeof(RectTransform), typeof(Image));
            stageDetailsPanel.transform.SetParent(stageSelectionPanel.transform, false);
            StretchFill(stageDetailsPanel.GetComponent<RectTransform>());
            stageDetailsPanel.GetComponent<Image>().color = new Color(0.01f, 0.012f, 0.02f, 0.88f);

            // Click outside the card to close
            var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            backdrop.transform.SetParent(stageDetailsPanel.transform, false);
            StretchFill(backdrop.GetComponent<RectTransform>());
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var backdropButton = backdrop.GetComponent<Button>();
            backdropButton.transition = Selectable.Transition.None;
            backdropButton.navigation = new Navigation { mode = Navigation.Mode.None };
            backdropButton.onClick.AddListener(() => { PlayBack(); CloseStageDetails(); });

            // Centered modal card
            var modal = new GameObject("StageDetailsModal", typeof(RectTransform), typeof(Image));
            modal.transform.SetParent(stageDetailsPanel.transform, false);
            stageDetailsModal = modal.GetComponent<RectTransform>();
            Place(stageDetailsModal, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 520f));
            modal.GetComponent<Image>().color = new Color(0.045f, 0.055f, 0.075f, 0.98f);

            var modalBorder = modal.AddComponent<Outline>();
            modalBorder.effectColor = new Color(1f, 1f, 1f, 0.06f);
            modalBorder.effectDistance = new Vector2(2f, -2f);

            // Faint stage number watermark
            stageDetailsNumber = MakeText(modal.transform, "StageDetailsNumber", "01", 150, TextAnchor.MiddleRight,
                new Color(1f, 1f, 1f, 0.05f), FontStyle.Bold);
            stageDetailsNumber.horizontalOverflow = HorizontalWrapMode.Overflow;
            stageDetailsNumber.verticalOverflow = VerticalWrapMode.Overflow;
            Place(stageDetailsNumber.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -24f), new Vector2(320f, 180f));

            // Accent stripe — tinted by difficulty
            var modalAccent = new GameObject("ModalAccent", typeof(RectTransform), typeof(Image));
            modalAccent.transform.SetParent(modal.transform, false);
            var maRect = modalAccent.GetComponent<RectTransform>();
            maRect.anchorMin = new Vector2(0f, 1f);
            maRect.anchorMax = new Vector2(1f, 1f);
            maRect.pivot = new Vector2(0.5f, 1f);
            maRect.sizeDelta = new Vector2(0f, 4f);
            modalAccentImage = modalAccent.GetComponent<Image>();
            modalAccentImage.color = AccentColor;
            modalAccentImage.raycastTarget = false;

            // Header block (left-aligned)
            stageDetailsTag = MakeText(modal.transform, "StageDetailsTag", "STAGE 01", 13, TextAnchor.MiddleLeft, AccentBright, FontStyle.Bold);
            StretchTop(stageDetailsTag.rectTransform, 44f, 44f, 30f, 22f);

            stageDetailsTitle = MakeText(modal.transform, "StageDetailsTitle", "Select a stage", 40, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            StretchTop(stageDetailsTitle.rectTransform, 44f, 44f, 56f, 56f);

            stageDetailsDescription = MakeText(modal.transform, "StageDetailsDescription", "", 16, TextAnchor.UpperLeft, new Color(0.58f, 0.65f, 0.74f, 1f));
            StretchTop(stageDetailsDescription.rectTransform, 44f, 120f, 118f, 48f);

            var divider = new GameObject("DetailsDivider", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(modal.transform, false);
            StretchTop(divider.GetComponent<RectTransform>(), 44f, 44f, 176f, 2f);
            var dividerImage = divider.GetComponent<Image>();
            dividerImage.color = new Color(1f, 1f, 1f, 0.08f);
            dividerImage.raycastTarget = false;

            // Footer buttons
            stageDetailsBackButton = CreateButtonChild(modal.transform, "StageDetailsBackButton", "◀  BACK");
            stageDetailsBackButton.GetComponent<Image>().color = DarkButton;
            var detailsBackLabel = stageDetailsBackButton.GetComponentInChildren<Text>();
            if (detailsBackLabel != null) { detailsBackLabel.fontSize = 14; detailsBackLabel.color = new Color(0.55f, 0.6f, 0.68f, 1f); }
            Place(stageDetailsBackButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(44f, 36f), new Vector2(180f, 50f));

            playStageButton = CreateButtonChild(modal.transform, "PlayStageButton", "▶  PLAY");
            playStageButton.GetComponent<Image>().color = AccentColor;
            var playBtnLabel = playStageButton.GetComponentInChildren<Text>();
            if (playBtnLabel != null) { playBtnLabel.fontSize = 22; playBtnLabel.fontStyle = FontStyle.Bold; }
            Place(playStageButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-44f, 36f), new Vector2(300f, 62f));

            stageDetailsPanel.SetActive(false);
        }

        private void BuildDifficultySelector()
        {
            if (stageDetailsPanel == null || difficultyCards.Count > 0) return;

            Transform parent = stageDetailsModal != null ? (Transform)stageDetailsModal : stageDetailsPanel.transform;

            difficultyLabel = MakeText(parent, "DifficultyLabel", "DIFFICULTY", 12, TextAnchor.MiddleLeft, TextDim, FontStyle.Bold);
            StretchTop(difficultyLabel.rectTransform, 44f, 44f, 194f, 20f);

            // Three equal cards in a row
            var row = new GameObject("DifficultyRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            StretchTop(row.GetComponent<RectTransform>(), 44f, 44f, 222f, 130f);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            foreach (var difficulty in DifficultyOrder)
            {
                difficultyCards[difficulty] = CreateDifficultyCard(row.transform, difficulty);
            }

            // Description line under the cards
            difficultyHeading = MakeText(parent, "DifficultyHeading", "◆ NORMAL", 14, TextAnchor.MiddleCenter, TextDim, FontStyle.Bold);
            StretchTop(difficultyHeading.rectTransform, 44f, 44f, 372f, 30f);
        }

        private DifficultyCardView CreateDifficultyCard(Transform parent, StageDifficulty difficulty)
        {
            var view = new DifficultyCardView();
            Color tint = GetDifficultyColor(difficulty);
            int level = GetDifficultyLevel(difficulty);

            view.button = CreateButtonChild(parent, difficulty + "DifficultyButton", difficulty.ToString().ToUpperInvariant());
            view.button.GetComponent<Image>().color = DifficultyCardColor;

            // Name (reuses the button label)
            view.nameText = view.button.GetComponentInChildren<Text>();
            view.nameText.fontSize = 20;
            view.nameText.fontStyle = FontStyle.Bold;
            view.nameText.raycastTarget = false;
            StretchTop(view.nameText.rectTransform, 8f, 8f, 26f, 30f);

            // Tier bar across the top
            var bar = new GameObject("TierBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(view.button.transform, false);
            StretchTop(bar.GetComponent<RectTransform>(), 0f, 0f, 0f, 4f);
            view.tierBar = bar.GetComponent<Image>();
            view.tierBar.raycastTarget = false;

            // Wave count
            view.waveText = MakeText(view.button.transform, "WaveText", "", 13, TextAnchor.MiddleCenter, TextDim);
            StretchTop(view.waveText.rectTransform, 8f, 8f, 62f, 22f);

            // Threat pips (1–3)
            view.pips = new Image[3];
            for (int i = 0; i < view.pips.Length; i++)
            {
                var pip = new GameObject("Pip" + (i + 1), typeof(RectTransform), typeof(Image));
                pip.transform.SetParent(view.button.transform, false);
                Place(pip.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2((i - 1) * 22f, 20f), new Vector2(16f, 8f));
                view.pips[i] = pip.GetComponent<Image>();
                view.pips[i].raycastTarget = false;
            }

            float pitch = 0.8f + 0.1f * level;
            view.feedback = AttachFeedback(view.button, () => PlaySfx(clickClip, pitch), tint, 1.04f);

            StageDifficulty captured = difficulty;
            view.button.onClick.AddListener(() => SetDifficulty(captured));
            return view;
        }

        private void SetDifficulty(StageDifficulty difficulty)
        {
            selectedDifficulty = difficulty;
            UpdateDifficultyDisplay();
        }

        private void UpdateDifficultyDisplay()
        {
            foreach (var difficulty in DifficultyOrder)
            {
                if (!difficultyCards.TryGetValue(difficulty, out DifficultyCardView view)) continue;

                bool isSelected = selectedDifficulty == difficulty;
                Color tint = GetDifficultyColor(difficulty);
                int level = GetDifficultyLevel(difficulty);

                view.feedback.SetBaseColor(isSelected ? Color.Lerp(DifficultyCardColor, tint, 0.22f) : DifficultyCardColor);
                view.feedback.restScale = isSelected ? 1.05f : 1f;
                view.feedback.glowAlways = isSelected;
                view.feedback.glowColor = new Color(tint.r, tint.g, tint.b, 0.9f);

                view.tierBar.color = isSelected ? tint : new Color(tint.r, tint.g, tint.b, 0.35f);

                view.nameText.text = difficulty.ToString().ToUpperInvariant();
                view.nameText.color = isSelected ? Color.white : new Color(0.55f, 0.60f, 0.68f, 1f);

                int waveCount = GetSelectedStageWaveCount(difficulty);
                view.waveText.text = waveCount > 0 ? $"{waveCount} WAVES" : "";
                view.waveText.color = isSelected ? new Color(0.80f, 0.85f, 0.92f, 1f) : TextDim;

                for (int i = 0; i < view.pips.Length; i++)
                {
                    bool filled = i < level;
                    view.pips[i].color = filled
                        ? (isSelected ? tint : new Color(tint.r, tint.g, tint.b, 0.45f))
                        : new Color(1f, 1f, 1f, 0.10f);
                }
            }

            Color selectedTint = GetDifficultyColor(selectedDifficulty);
            modalAccentTarget = selectedTint;

            if (difficultyHeading != null)
            {
                difficultyHeading.text = $"◆ {selectedDifficulty.ToString().ToUpperInvariant()}  -  {GetDifficultyBlurb(selectedDifficulty)}";
                difficultyHeading.color = selectedTint;
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
            AttachFeedback(startButton, null, AccentBright, 1.05f);
            AttachFeedback(exitButton, null, TextDim, 1.04f);
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
            RegisterEntrance(mainEntrance, mainWatermark, new Vector2(80f, 0f), 0.10f);
            RegisterEntrance(mainEntrance, titleText, new Vector2(-70f, 0f), 0.05f);
            RegisterEntrance(mainEntrance, subtitleText, new Vector2(-50f, 0f), 0.12f);
            RegisterEntrance(mainEntrance, mainDivider, new Vector2(-50f, 0f), 0.18f);
            RegisterEntrance(mainEntrance, startButton, new Vector2(-70f, 0f), 0.24f);
            RegisterEntrance(mainEntrance, audioSettingsUI != null ? audioSettingsUI.SettingsButton : null, new Vector2(-70f, 0f), 0.32f);
            RegisterEntrance(mainEntrance, exitButton, new Vector2(-70f, 0f), 0.40f);
            RegisterEntrance(mainEntrance, mainVersion, Vector2.zero, 0.45f);

            // Stage selector header (cards were registered in BuildStageButtons)
            RegisterEntrance(stageEntrance, stageHeaderTitle, new Vector2(-60f, 0f), 0.00f);
            RegisterEntrance(stageEntrance, stageHeaderSubtitle, new Vector2(-40f, 0f), 0.06f);
            RegisterEntrance(stageEntrance, stageHeaderDivider, new Vector2(-40f, 0f), 0.10f);
            RegisterEntrance(stageEntrance, stageProgressText, new Vector2(40f, 0f), 0.12f);
            RegisterEntrance(stageEntrance, backButton, new Vector2(-40f, 0f), 0.16f);
            RegisterEntrance(stageEntrance, stageHint, Vector2.zero, 0.30f);

            // Stage details modal
            RegisterEntrance(detailsEntrance, stageDetailsNumber, Vector2.zero, 0.08f);
            RegisterEntrance(detailsEntrance, stageDetailsTag, new Vector2(0f, 12f), 0.05f);
            RegisterEntrance(detailsEntrance, stageDetailsTitle, new Vector2(0f, 12f), 0.08f);
            RegisterEntrance(detailsEntrance, stageDetailsDescription, new Vector2(0f, 12f), 0.12f);
            RegisterEntrance(detailsEntrance, difficultyLabel, new Vector2(0f, 12f), 0.16f);

            int n = 0;
            foreach (var difficulty in DifficultyOrder)
            {
                if (difficultyCards.TryGetValue(difficulty, out DifficultyCardView view))
                {
                    RegisterEntrance(detailsEntrance, view.button, Vector2.zero, 0.18f + n * 0.06f, 0.9f);
                    n++;
                }
            }

            RegisterEntrance(detailsEntrance, difficultyHeading, new Vector2(0f, 12f), 0.36f);
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
            var existingCanvas = FindFirstObjectByType<Canvas>();
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
}