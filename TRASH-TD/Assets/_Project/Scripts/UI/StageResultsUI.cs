using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TrashTD.Audio;
using TrashTD.Core.GameLoop;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.UI
{
    /// <summary>
    /// Stage results as a "mission debrief" card in the terminal style (GDD 1.4.6 & 1.9).
    /// Victory: STAGE CLEARED, three stars that stamp in with their rule (CLEARED / LIVES 50%+ / NO LEAKS), a scan
    /// sweep and pixel sparks. Defeat: SIGNAL LOST in red with static and glitching, plus an after-action report
    /// (wave reached, hostiles still in play, top leaker). Buttons: NEXT STAGE (victory, when there is one) /
    /// RETRY / STAGE SELECT. Built in code on its own canvas; runs on unscaled time (the game is stopped).
    /// Any click or key during the intro skips to the end; ENTER presses the highlighted button, R retries.
    /// </summary>
    public class StageResultsUI : MonoBehaviour
    {
        // Palette (terminal green, matching Settings / Intel; red for defeat)
        private static readonly Color VictoryAccent = new Color(0.18f, 0.82f, 0.45f, 1f);
        private static readonly Color VictoryBright = new Color(0.32f, 1.00f, 0.64f, 1f);
        private static readonly Color VictoryCard = new Color(0.016f, 0.042f, 0.028f, 0.98f);
        private static readonly Color VictoryHeader = new Color(0.030f, 0.090f, 0.055f, 1f);
        private static readonly Color VictoryPanel = new Color(0.010f, 0.028f, 0.018f, 1f);
        private static readonly Color VictoryButton = new Color(0.035f, 0.090f, 0.060f, 1f);
        private static readonly Color DefeatAccent = new Color(0.95f, 0.33f, 0.36f, 1f);
        private static readonly Color DefeatBright = new Color(1f, 0.55f, 0.50f, 1f);
        private static readonly Color DefeatCard = new Color(0.045f, 0.014f, 0.016f, 0.98f);
        private static readonly Color DefeatHeader = new Color(0.10f, 0.030f, 0.034f, 1f);
        private static readonly Color DefeatPanel = new Color(0.030f, 0.008f, 0.010f, 1f);
        private static readonly Color DefeatButton = new Color(0.10f, 0.035f, 0.035f, 1f);
        private static readonly Color StarGold = new Color(1f, 0.82f, 0.30f, 1f);
        private static readonly Color StarOff = new Color(1f, 1f, 1f, 0.14f);
        private static readonly Color LabelDim = new Color(0.62f, 0.68f, 0.72f, 1f);
        private static readonly Color BodyText = new Color(0.86f, 0.92f, 0.88f, 1f);
        private static readonly Color SelectedLabel = new Color(0.02f, 0.06f, 0.04f, 1f);

        // Layout, in card pixels from the top-left (1920x1080 reference). Everything below the header lines up
        // on the content column (ContentX .. CardWidth - ContentX); vertical gaps are 24-32 px.
        private const float CardWidth = 960f;
        private const float CardHeight = 548f;
        private const float HeaderHeight = 60f;
        private const float ContentX = 48f;
        private const float ContentWidth = CardWidth - 2f * ContentX;   // 864
        private const float TitleCenterY = 118f;                        // y 85..151
        private const float SubtitleCenterY = 174f;                     // y 162..186
        private const float RuleY = 204f;
        private const float PanelTop = 228f;
        private const float PanelHeight = 168f;                         // y 228..396
        private const float ButtonsTop = 424f;
        private const float ButtonHeight = 60f;                         // y 424..484
        private const float ButtonGap = 24f;
        private const float HintCenterY = 514f;

        private static readonly string[] StarRules = { "CLEARED", "LIVES 50%+", "NO LEAKS" };

        private sealed class Spark
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Velocity;
            public float Life;
            public float Age;
        }

        // --- UI ---
        private GameObject root;
        private Image dimImage;
        private RectTransform card;
        private CanvasGroup cardGroup;
        private Image cardImage;
        private Outline cardOutline;
        private Image headerImage;
        private Image headerLine;
        private Image headerDot;
        private readonly List<Image> brackets = new List<Image>();
        private Text headerTitle;
        private Text headerStage;
        private Text titleText;
        private Text titleGhost;
        private Text subtitleText;
        private Image ruleImage;
        private RawImage noiseImage;
        private Image sweepImage;
        private Image panelImage;
        private Outline panelOutline;
        private RectTransform starsRoot;
        private readonly Image[] starImages = new Image[3];
        private readonly Image[] starPills = new Image[3];
        private readonly Outline[] starPillOutlines = new Outline[3];
        private readonly Text[] starLabels = new Text[3];
        private RectTransform insightRoot;
        private Text insightHeader;
        private Text insightText;
        private Button nextButton;
        private Button retryButton;
        private Button selectButton;
        private readonly List<Button> activeButtons = new List<Button>();
        private Button primaryButton;
        private Text hintText;
        private RectTransform sparksRoot;
        private readonly List<Spark> sparks = new List<Spark>();

        // --- Leaks per enemy type (for the defeat report), counted while the stage runs ---
        private readonly Dictionary<string, int> leaksByEnemy = new Dictionary<string, int>();
        private EnemyManager trackedEnemyManager;

        // --- Result state ---
        private bool isVictory;
        private int starCount;
        private StageData nextStage;
        private string fullTitle = string.Empty;
        private Coroutine showRoutine;
        private bool skipRequested;
        private bool introDone;
        private float glitchTimer;
        private readonly bool[] starStamped = new bool[3];

        private void Awake()
        {
            BuildUI();
            root.SetActive(false);
        }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStageVictory += ShowVictory;
                GameManager.Instance.OnStageDefeat += ShowDefeat;
            }

            trackedEnemyManager = EnemyManager.Instance;
            if (trackedEnemyManager != null) trackedEnemyManager.OnEnemyReachedExit += HandleEnemyLeaked;
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStageVictory -= ShowVictory;
                GameManager.Instance.OnStageDefeat -= ShowDefeat;
            }

            if (trackedEnemyManager != null) trackedEnemyManager.OnEnemyReachedExit -= HandleEnemyLeaked;
        }

        private void HandleEnemyLeaked(EnemyBase enemy)
        {
            string enemyName = enemy != null && enemy.Data != null ? enemy.Data.enemyName : "UNKNOWN";
            leaksByEnemy.TryGetValue(enemyName, out int count);
            leaksByEnemy[enemyName] = count + 1;
        }

        // ─────────────────────────────────────────────────────
        //  Show
        // ─────────────────────────────────────────────────────

        public void ShowVictory(int stars) => ShowResults(true, Mathf.Clamp(stars, 1, 3));

        public void ShowDefeat() => ShowResults(false, 0);

        private void ShowResults(bool victory, int stars)
        {
            isVictory = victory;
            starCount = stars;
            GameManager manager = GameManager.Instance;
            // The tutorial's results wait for Coalesce's outro; TutorialDirector shows them when it's over
            if (manager != null && manager.IsTutorial && TutorialDirector.Instance != null && !TutorialDirector.Instance.IsFinished) return;

            WaveManager waveManager = FindAnyObjectByType<WaveManager>();
            StageData stage = manager != null ? manager.CurrentStage : null;

            ApplyTheme();

            // Header: MISSION DEBRIEF | STAGE 03 // RAILYARD DRAIN // HARD
            string mapName = stage != null && !string.IsNullOrEmpty(stage.mapName) ? stage.mapName.ToUpperInvariant() : "UNKNOWN SECTOR";
            int stageNumber = GetStageNumber(stage);
            string difficulty = manager != null ? manager.CurrentDifficulty.ToString().ToUpperInvariant() : "NORMAL";
            string difficultyColor = manager == null || manager.CurrentDifficulty == StageDifficulty.Normal ? "#FAC747"
                : manager.CurrentDifficulty == StageDifficulty.Easy ? "#4DDB94" : "#F2545C";
            bool tutorial = manager != null && manager.IsTutorial;
            headerStage.text = tutorial
                ? $"STAGE 00 // {mapName}"
                : (stageNumber > 0 ? $"STAGE {stageNumber:00} // " : string.Empty) + $"{mapName} // <color={difficultyColor}>{difficulty}</color>";

            // Title + subtitle
            fullTitle = victory ? "STAGE CLEARED" : "SIGNAL LOST";
            titleText.text = string.Empty;
            titleGhost.text = string.Empty;
            bool timedOut = !victory && stage != null && stage.timeLimit > 0f && manager != null
                && manager.ElapsedTime >= stage.timeLimit && manager.CurrentLifePoints > 0;
            subtitleText.text = victory ? $"{mapName} SECURED" : timedOut ? "MISSION FAILED  -  TIME LIMIT REACHED" : "MISSION FAILED  -  ALL LIVES LOST";

            // Stars (victory) or after-action report (defeat), in the same framed panel
            starsRoot.gameObject.SetActive(victory);
            insightRoot.gameObject.SetActive(!victory);
            for (int i = 0; i < 3; i++)
            {
                starStamped[i] = false;
                starImages[i].color = new Color(1f, 1f, 1f, 0f);
                starLabels[i].color = new Color(LabelDim.r, LabelDim.g, LabelDim.b, 0f);
                starPills[i].color = new Color(0f, 0f, 0f, 0f);
                starPillOutlines[i].effectColor = new Color(0f, 0f, 0f, 0f);
            }

            if (!victory)
            {
                int totalWaves = waveManager != null ? waveManager.TotalWaves : 0;
                int waveIndex = manager != null ? manager.GetCurrentWaveIndex() : 0;
                int reached = totalWaves > 0 ? Mathf.Clamp(waveIndex + 1, 1, totalWaves) : waveIndex + 1;
                int inPlay = (EnemyManager.Instance != null ? EnemyManager.Instance.ActiveEnemyCount : 0)
                    + (waveManager != null ? waveManager.PendingSpawnCount : 0);
                string topLeaker = GetTopLeaker(out int topLeaks);
                insightText.text =
                    $"> REACHED WAVE {reached:00}" + (totalWaves > 0 ? $" / {totalWaves:00}" : string.Empty) + "\n" +
                    $"> {inPlay} HOSTILES STILL IN PLAY\n" +
                    (topLeaks > 0 ? $"> MOST LEAKS: {topLeaker.ToUpperInvariant()} ({topLeaks})" : "> NO LEAKS RECORDED");
            }

            // Buttons: NEXT STAGE (victory, if any) / RETRY / STAGE SELECT
            nextStage = victory ? FindNextStage(stage) : null;
            activeButtons.Clear();
            if (nextStage != null) activeButtons.Add(nextButton);
            activeButtons.Add(retryButton);
            activeButtons.Add(selectButton);
            nextButton.gameObject.SetActive(nextStage != null);
            primaryButton = victory ? (nextStage != null ? nextButton : selectButton) : retryButton;
            LayoutButtons();
            hintText.text = "[ENTER] " + ButtonLabel(primaryButton) + "      [R] RETRY";

            ClearSparks();
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            if (showRoutine != null) StopCoroutine(showRoutine);
            showRoutine = StartCoroutine(ShowRoutine());
        }

        private string ButtonLabel(Button button)
        {
            if (button == nextButton) return "NEXT STAGE";
            if (button == retryButton) return "RETRY";
            return "STAGE SELECT";
        }

        private Color Accent => isVictory ? VictoryAccent : DefeatAccent;
        private Color Bright => isVictory ? VictoryBright : DefeatBright;

        private void ApplyTheme()
        {
            Color accent = Accent;
            Color bright = Bright;

            cardImage.color = isVictory ? VictoryCard : DefeatCard;
            cardOutline.effectColor = new Color(accent.r, accent.g, accent.b, 0.55f);
            headerImage.color = isVictory ? VictoryHeader : DefeatHeader;
            headerLine.color = new Color(accent.r, accent.g, accent.b, 0.55f);
            headerDot.color = bright;
            foreach (Image bracket in brackets) bracket.color = bright;
            headerTitle.color = bright;
            titleText.color = bright;
            ruleImage.color = new Color(accent.r, accent.g, accent.b, 0.7f);
            panelImage.color = isVictory ? VictoryPanel : DefeatPanel;
            panelOutline.effectColor = new Color(accent.r, accent.g, accent.b, 0.3f);
            insightHeader.color = accent;
            noiseImage.gameObject.SetActive(!isVictory);
            sweepImage.gameObject.SetActive(isVictory);
            sweepImage.color = new Color(bright.r, bright.g, bright.b, 0f);

            foreach (Button button in new[] { nextButton, retryButton, selectButton })
            {
                MenuButtonFeedback feedback = button.GetComponent<MenuButtonFeedback>();
                feedback.glowColor = new Color(bright.r, bright.g, bright.b, 0.9f);
            }
        }

        private string GetTopLeaker(out int leaks)
        {
            string top = string.Empty;
            leaks = 0;
            foreach (KeyValuePair<string, int> pair in leaksByEnemy)
            {
                if (pair.Value > leaks)
                {
                    leaks = pair.Value;
                    top = pair.Key;
                }
            }
            return top;
        }

        private static int GetStageNumber(StageData stage)
        {
            if (stage == null || string.IsNullOrEmpty(stage.stageId)) return 0;
            int underscore = stage.stageId.LastIndexOf('_');
            return underscore >= 0 && int.TryParse(stage.stageId.Substring(underscore + 1), out int number) ? number : 0;
        }

        /// <summary>The following numbered stage in Resources/Stages (STAGE_03 → STAGE_04; the tutorial → STAGE_01), or null.</summary>
        private static StageData FindNextStage(StageData stage)
        {
            if (stage != null && stage.stageId == TutorialDirector.TutorialStageId) return Resources.Load<StageData>("Stages/STAGE_01");
            int number = GetStageNumber(stage);
            if (number <= 0) return null;
            return Resources.Load<StageData>($"Stages/STAGE_{number + 1:00}");
        }

        // ─────────────────────────────────────────────────────
        //  Animation (unscaled time; a click or key skips to the end)
        // ─────────────────────────────────────────────────────

        private float PanelStart => isVictory ? 1.0f : 0.95f;
        private float ButtonsStart => isVictory ? 2.05f : 1.45f;
        private float EndTime => ButtonsStart + 0.45f;

        private IEnumerator ShowRoutine()
        {
            skipRequested = false;
            introDone = false;
            SetButtonsInteractable(false);

            float t = 0f;
            float end = EndTime;
            while (t < end)
            {
                if (skipRequested) t = end;
                ApplyIntro(t);
                yield return null;
                t += Time.unscaledDeltaTime;
            }

            ApplyIntro(end);
            introDone = true;
            SetButtonsInteractable(true);
            showRoutine = null;
        }

        private void ApplyIntro(float t)
        {
            // Dimmer + card pop-in
            float cardK = Mathf.Clamp01(t / 0.35f);
            dimImage.color = new Color(0f, 0f, 0f, 0.86f * cardK);
            cardGroup.alpha = cardK;
            card.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, EaseOutBack(cardK));

            // Title types on
            float titleK = Mathf.Clamp01((t - 0.3f) / 0.55f);
            int chars = Mathf.CeilToInt(fullTitle.Length * titleK);
            string shown = fullTitle.Substring(0, Mathf.Clamp(chars, 0, fullTitle.Length));
            if (titleText.text != shown)
            {
                titleText.text = shown;
                titleGhost.text = shown;
                if (chars > 0 && chars < fullTitle.Length) PlaySfx(SfxId.UiHover, 0.35f, 1.4f);
            }

            float subK = Mathf.Clamp01((t - 0.8f) / 0.3f);
            subtitleText.color = new Color(BodyText.r, BodyText.g, BodyText.b, subK);
            ruleImage.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(0f, 160f, EaseOutCubic(subK)), 2f);

            // Panel fades in
            float panelK = Mathf.Clamp01((t - PanelStart * 0.85f) / 0.3f);
            panelImage.color = new Color(panelImage.color.r, panelImage.color.g, panelImage.color.b, panelK);

            if (isVictory)
            {
                // Scan line sweeps down the card
                float sweepK = Mathf.Clamp01((t - 0.2f) / 0.7f);
                sweepImage.rectTransform.anchoredPosition = new Vector2(0f, -Mathf.Lerp(HeaderHeight, CardHeight - 6f, EaseInOut(sweepK)));
                Color s = sweepImage.color;
                s.a = sweepK > 0f && sweepK < 1f ? 0.55f : 0f;
                sweepImage.color = s;

                // Stars stamp in one by one, each with its rule pill
                for (int i = 0; i < 3; i++)
                {
                    float start = PanelStart + 0.1f + i * 0.3f;
                    float k = Mathf.Clamp01((t - start) / 0.25f);
                    bool met = i < starCount;
                    Color starColor = met ? StarGold : StarOff;
                    starImages[i].color = new Color(starColor.r, starColor.g, starColor.b, starColor.a * k);
                    starImages[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(2.4f, 1f, EaseOutBack(k));

                    Color pillLine = met ? StarGold : LabelDim;
                    starPills[i].color = new Color(0f, 0f, 0f, 0.35f * k);
                    starPillOutlines[i].effectColor = new Color(pillLine.r, pillLine.g, pillLine.b, (met ? 0.8f : 0.25f) * k);
                    Color labelColor = met ? StarGold : LabelDim;
                    starLabels[i].color = new Color(labelColor.r, labelColor.g, labelColor.b, (met ? 1f : 0.5f) * k);

                    if (k > 0f && !starStamped[i])
                    {
                        starStamped[i] = true;
                        if (!skipRequested)
                        {
                            PlaySfx(met ? SfxId.UiConfirm : SfxId.UiClick, met ? 0.9f : 0.5f, met ? 1f + 0.12f * i : 0.7f);
                            if (met) BurstSparks(starImages[i].rectTransform.position, StarGold, 14);
                        }
                    }
                }
            }
            else
            {
                // Static floods in, then settles to a faint crawl (Update keeps it moving)
                float noise = Mathf.Lerp(0.6f, 0.1f, Mathf.Clamp01((t - 0.15f) / 0.9f));
                noiseImage.color = new Color(1f, 1f, 1f, noise);

                float insightK = Mathf.Clamp01((t - PanelStart) / 0.35f);
                insightText.color = new Color(BodyText.r, BodyText.g, BodyText.b, insightK);
                Color accent = Accent;
                insightHeader.color = new Color(accent.r, accent.g, accent.b, insightK);
            }

            // Buttons rise in
            float buttonsK = EaseOutCubic(Mathf.Clamp01((t - ButtonsStart) / 0.4f));
            for (int i = 0; i < activeButtons.Count; i++)
            {
                activeButtons[i].GetComponent<CanvasGroup>().alpha = buttonsK;
                RectTransform rect = activeButtons[i].GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -(ButtonsTop + (1f - buttonsK) * 24f));
            }
            hintText.color = new Color(LabelDim.r, LabelDim.g, LabelDim.b, 0.7f * buttonsK);
        }

        private void Update()
        {
            if (root == null || !root.activeSelf) return;
            float dt = Time.unscaledDeltaTime;

            HandleInput();
            UpdateSparks(dt);

            // Header status dot blinks
            Color bright = Bright;
            headerDot.color = new Color(bright.r, bright.g, bright.b, Mathf.Repeat(Time.unscaledTime * 1.5f, 1f) < 0.6f ? 1f : 0.25f);

            if (!isVictory)
            {
                // Static crawl and recurring glitch bursts on the title
                Rect uv = noiseImage.uvRect;
                uv.x = Random.value;
                uv.y = Random.value;
                noiseImage.uvRect = uv;
                if (introDone) noiseImage.color = new Color(1f, 1f, 1f, 0.08f + Random.value * 0.05f);

                glitchTimer -= dt;
                bool burst = glitchTimer < 0.12f;
                if (glitchTimer <= 0f) glitchTimer = Random.Range(1.4f, 3.2f);
                bool glitching = burst || !introDone;
                Vector2 jitter = glitching ? new Vector2(Random.Range(-7f, 7f), Random.Range(-2f, 2f)) : Vector2.zero;
                titleText.rectTransform.anchoredPosition = new Vector2(jitter.x * 0.4f, -TitleCenterY + jitter.y);
                titleGhost.rectTransform.anchoredPosition = new Vector2(-jitter.x - 4f, -TitleCenterY + 2f);
                titleGhost.color = new Color(0.3f, 0.95f, 1f, glitching ? 0.6f : 0.18f);
            }
            else
            {
                titleText.rectTransform.anchoredPosition = new Vector2(0f, -TitleCenterY);
                titleGhost.color = new Color(0f, 0f, 0f, 0f);
            }

            if (primaryButton != null && introDone)
            {
                MenuButtonFeedback feedback = primaryButton.GetComponent<MenuButtonFeedback>();
                feedback.restScale = 1f + 0.02f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f));
            }
        }

        private void HandleInput()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool anyKey = keyboard != null && keyboard.anyKey.wasPressedThisFrame;
            bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;

            if (!introDone)
            {
                if (anyKey || click) skipRequested = true;
                return;
            }

            if (keyboard == null) return;
            if ((keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) && primaryButton != null)
            {
                primaryButton.onClick.Invoke();
            }
            else if (keyboard.rKey.wasPressedThisFrame)
            {
                OnRetryClicked();
            }
        }

        private void SetButtonsInteractable(bool value)
        {
            foreach (Button button in activeButtons)
            {
                button.interactable = value;
                button.GetComponent<CanvasGroup>().blocksRaycasts = value;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Sparks (victory)
        // ─────────────────────────────────────────────────────

        private void BurstSparks(Vector3 worldPosition, Color color, int count)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(sparksRoot,
                RectTransformUtility.WorldToScreenPoint(null, worldPosition), null, out Vector2 local);
            for (int i = 0; i < count; i++)
            {
                Image image = CreateImage(sparksRoot, "Spark", i % 3 == 0 ? Color.white : color);
                RectTransform rect = image.rectTransform;
                float size = Random.Range(4f, 9f);
                rect.sizeDelta = new Vector2(size, size);
                rect.anchoredPosition = local;
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float speed = Random.Range(160f, 420f);
                sparks.Add(new Spark
                {
                    Rect = rect,
                    Image = image,
                    Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed + new Vector2(0f, 120f),
                    Life = Random.Range(0.5f, 0.9f)
                });
            }
        }

        private void UpdateSparks(float dt)
        {
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                Spark spark = sparks[i];
                spark.Age += dt;
                if (spark.Age >= spark.Life || spark.Rect == null)
                {
                    if (spark.Rect != null) Destroy(spark.Rect.gameObject);
                    sparks.RemoveAt(i);
                    continue;
                }

                spark.Velocity += new Vector2(0f, -900f) * dt;
                spark.Rect.anchoredPosition += spark.Velocity * dt;
                Color c = spark.Image.color;
                c.a = 1f - spark.Age / spark.Life;
                spark.Image.color = c;
            }
        }

        private void ClearSparks()
        {
            foreach (Spark spark in sparks)
            {
                if (spark.Rect != null) Destroy(spark.Rect.gameObject);
            }
            sparks.Clear();
        }

        // ─────────────────────────────────────────────────────
        //  Actions
        // ─────────────────────────────────────────────────────

        private void OnNextClicked()
        {
            GameManager manager = GameManager.Instance;
            if (nextStage == null) return;
            MainMenuController.SetPendingStage(nextStage);
            if (manager != null) MainMenuController.SetPendingStageDifficulty(manager.CurrentDifficulty);
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void OnRetryClicked()
        {
            GameManager manager = GameManager.Instance;
            if (manager != null)
            {
                MainMenuController.SetPendingStage(manager.CurrentStage);
                MainMenuController.SetPendingStageDifficulty(manager.CurrentDifficulty);
            }

            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void OnStageSelectClicked()
        {
            Time.timeScale = 1f;
            MainMenuController.ReturnToStageSelectorOnLoad();
            SceneManager.LoadScene("MainMenu");
        }

        // ─────────────────────────────────────────────────────
        //  Build
        // ─────────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasObject = new GameObject("StageResultsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            root = new GameObject("StageResultsOverlay", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(canvasObject.transform, false);
            SetStretch(root.GetComponent<RectTransform>());
            dimImage = root.GetComponent<Image>();
            dimImage.color = new Color(0f, 0f, 0f, 0f);

            // Card
            var cardObject = new GameObject("DebriefCard", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            cardObject.transform.SetParent(root.transform, false);
            card = cardObject.GetComponent<RectTransform>();
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(CardWidth, CardHeight);
            cardImage = cardObject.GetComponent<Image>();
            cardGroup = cardObject.GetComponent<CanvasGroup>();
            cardOutline = cardObject.AddComponent<Outline>();
            cardOutline.effectDistance = new Vector2(2f, -2f);
            AddCornerBrackets(card, 28f, 3f, -8f);

            // Static noise (defeat) and the scan sweep (victory), under the content
            noiseImage = new GameObject("Static", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            noiseImage.transform.SetParent(card, false);
            SetStretch(noiseImage.rectTransform);
            noiseImage.texture = GetNoiseTexture();
            noiseImage.uvRect = new Rect(0f, 0f, CardWidth / 128f, CardHeight / 128f);
            noiseImage.raycastTarget = false;

            sweepImage = CreateImage(card, "ScanSweep", Color.clear);
            sweepImage.rectTransform.anchorMin = new Vector2(0f, 1f);
            sweepImage.rectTransform.anchorMax = new Vector2(1f, 1f);
            sweepImage.rectTransform.pivot = new Vector2(0.5f, 1f);
            sweepImage.rectTransform.sizeDelta = new Vector2(0f, 6f);
            var sweepGlow = sweepImage.gameObject.AddComponent<Outline>();
            sweepGlow.effectColor = new Color(VictoryBright.r, VictoryBright.g, VictoryBright.b, 0.25f);
            sweepGlow.effectDistance = new Vector2(0f, 6f);

            // Header: blinking status dot + MISSION DEBRIEF on the left, stage line on the right
            headerImage = CreateImage(card, "Header", VictoryHeader);
            PlaceTopLeft(headerImage.rectTransform, 0f, 0f, CardWidth, HeaderHeight);
            headerLine = CreateImage(card, "HeaderLine", VictoryAccent);
            PlaceTopLeft(headerLine.rectTransform, 0f, HeaderHeight, CardWidth, 2f);
            headerDot = CreateImage(headerImage.rectTransform, "StatusDot", VictoryBright);
            PlaceTopLeft(headerDot.rectTransform, ContentX, (HeaderHeight - 10f) * 0.5f, 10f, 10f);
            headerTitle = CreateText(headerImage.rectTransform, "MISSION DEBRIEF", 16, TextAnchor.MiddleLeft, VictoryBright);
            PlaceTopLeft(headerTitle.rectTransform, ContentX + 22f, 0f, 320f, HeaderHeight);
            headerStage = CreateText(headerImage.rectTransform, string.Empty, 12, TextAnchor.MiddleRight, LabelDim);
            PlaceTopLeft(headerStage.rectTransform, CardWidth - ContentX - 520f, 0f, 520f, HeaderHeight);

            // Title (+ cyan ghost for the defeat glitch), subtitle and a short accent rule
            titleGhost = CreateText(card, string.Empty, 52, TextAnchor.MiddleCenter, Color.clear);
            PlaceCentered(titleGhost.rectTransform, TitleCenterY, CardWidth, 66f);
            titleText = CreateText(card, string.Empty, 52, TextAnchor.MiddleCenter, VictoryBright);
            PlaceCentered(titleText.rectTransform, TitleCenterY, CardWidth, 66f);
            var titleShadow = titleText.gameObject.AddComponent<Shadow>();
            titleShadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            titleShadow.effectDistance = new Vector2(0f, -4f);
            subtitleText = CreateText(card, string.Empty, 14, TextAnchor.MiddleCenter, BodyText);
            PlaceCentered(subtitleText.rectTransform, SubtitleCenterY, ContentWidth, 24f);
            ruleImage = CreateImage(card, "Rule", VictoryAccent);
            PlaceCentered(ruleImage.rectTransform, RuleY, 160f, 2f);

            // Framed panel shared by the stars (victory) and the after-action report (defeat)
            panelImage = CreateImage(card, "ResultPanel", VictoryPanel);
            PlaceTopLeft(panelImage.rectTransform, ContentX, PanelTop, ContentWidth, PanelHeight);
            panelOutline = panelImage.gameObject.AddComponent<Outline>();
            panelOutline.effectDistance = new Vector2(1f, -1f);

            starsRoot = CreateRect(panelImage.rectTransform, "Stars");
            SetStretch(starsRoot);
            Sprite starSprite = GameplayHUDUI.LoadIconSprite("icon_star");
            if (starSprite != null && starSprite.texture != null) starSprite.texture.filterMode = FilterMode.Point;
            float columnWidth = ContentWidth / 3f;
            for (int i = 0; i < 3; i++)
            {
                float centerX = columnWidth * (i + 0.5f);

                Image star = CreateImage(starsRoot, "Star" + (i + 1), Color.white);
                star.sprite = starSprite;
                star.preserveAspect = true;
                PlaceFromTopLeftCentered(star.rectTransform, centerX, 64f, 80f, 80f);
                starImages[i] = star;

                Image pill = CreateImage(starsRoot, "RulePill" + (i + 1), Color.clear);
                PlaceFromTopLeftCentered(pill.rectTransform, centerX, 132f, 200f, 30f);
                starPills[i] = pill;
                starPillOutlines[i] = pill.gameObject.AddComponent<Outline>();
                starPillOutlines[i].effectDistance = new Vector2(1.5f, -1.5f);

                Text label = CreateText(pill.rectTransform, StarRules[i], 12, TextAnchor.MiddleCenter, LabelDim);
                SetStretch(label.rectTransform);
                starLabels[i] = label;
            }

            insightRoot = CreateRect(panelImage.rectTransform, "AfterAction");
            SetStretch(insightRoot);
            insightHeader = CreateText(insightRoot, "// AFTER-ACTION REPORT", 12, TextAnchor.MiddleLeft, DefeatAccent);
            PlaceTopLeft(insightHeader.rectTransform, 32f, 22f, ContentWidth - 64f, 22f);
            insightText = CreateText(insightRoot, string.Empty, 14, TextAnchor.UpperLeft, BodyText);
            insightText.lineSpacing = 1.6f;
            insightText.verticalOverflow = VerticalWrapMode.Overflow;
            PlaceTopLeft(insightText.rectTransform, 32f, 60f, ContentWidth - 64f, PanelHeight - 76f);

            // Buttons (laid out per result in LayoutButtons) and the key hint
            nextButton = CreateButton(card, "NextStageButton", "NEXT STAGE", OnNextClicked);
            retryButton = CreateButton(card, "RetryButton", "RETRY", OnRetryClicked);
            selectButton = CreateButton(card, "StageSelectButton", "STAGE SELECT", OnStageSelectClicked);

            hintText = CreateText(card, string.Empty, 10, TextAnchor.MiddleCenter, LabelDim);
            PlaceCentered(hintText.rectTransform, HintCenterY, ContentWidth, 20f);

            sparksRoot = CreateRect(root.transform, "Sparks");
            SetStretch(sparksRoot);
        }

        /// <summary>
        /// Splits the content column evenly between the active buttons (24 px gaps), so the row lines up with
        /// the panel above. The primary button is filled with the accent colour.
        /// </summary>
        private void LayoutButtons()
        {
            Color accent = Accent;
            Color idle = isVictory ? VictoryButton : DefeatButton;
            int count = activeButtons.Count;
            float width = (ContentWidth - ButtonGap * (count - 1)) / count;
            for (int i = 0; i < count; i++)
            {
                Button button = activeButtons[i];
                RectTransform rect = button.GetComponent<RectTransform>();
                PlaceTopLeft(rect, ContentX + i * (width + ButtonGap), ButtonsTop, width, ButtonHeight);

                bool primary = button == primaryButton;
                MenuButtonFeedback feedback = button.GetComponent<MenuButtonFeedback>();
                feedback.SetBaseColor(primary ? accent : idle);
                feedback.glowAlways = primary;
                feedback.restScale = 1f;
                Text label = button.GetComponentInChildren<Text>();
                label.color = primary ? SelectedLabel : LabelDim;
            }
        }

        private Button CreateButton(RectTransform parent, string objectName, string label, UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = VictoryButton;
            Text text = CreateText(buttonObject.transform, label, 16, TextAnchor.MiddleCenter, LabelDim);
            SetStretch(text.rectTransform);
            AddCornerBrackets(buttonObject.GetComponent<RectTransform>(), 10f, 2f, 0f, register: false);

            Button button = buttonObject.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(onClick);

            MenuButtonFeedback feedback = buttonObject.AddComponent<MenuButtonFeedback>();
            feedback.hoverScale = 1.04f;
            feedback.respondToFocus = false;
            feedback.Hovered = () => PlaySfx(SfxId.UiHover, 0.7f, Random.Range(0.97f, 1.03f));
            feedback.Clicked = () => PlaySfx(SfxId.UiConfirm, 1f, 1f);
            Outline outline = buttonObject.AddComponent<Outline>();
            outline.useGraphicAlpha = false;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.effectColor = new Color(1f, 1f, 1f, 0f);
            feedback.glowOutline = outline;
            return button;
        }

        // ─────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────

        private static void PlaySfx(SfxId id, float volume, float pitch) => AudioManager.Instance?.PlaySfx(id, volume, pitch);

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

        private static float EaseInOut(float t) => t * t * (3f - 2f * t);

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            t = Mathf.Clamp01(t);
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private static Texture2D noiseTexture;

        private static Texture2D GetNoiseTexture()
        {
            if (noiseTexture != null) return noiseTexture;
            const int size = 64;
            noiseTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            var pixels = new Color32[size * size];
            var random = new System.Random(1337);
            for (int i = 0; i < pixels.Length; i++)
            {
                byte v = (byte)random.Next(0, 256);
                pixels[i] = new Color32(v, v, v, (byte)(random.Next(0, 3) == 0 ? 255 : 0));
            }
            noiseTexture.SetPixels32(pixels);
            noiseTexture.Apply();
            return noiseTexture;
        }

        private void AddCornerBrackets(RectTransform target, float length, float thickness, float inset, bool register = true)
        {
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2, i / 2);
                var inward = new Vector2(corner.x < 0.5f ? 1f : -1f, corner.y < 0.5f ? 1f : -1f);
                foreach (Vector2 size in new[] { new Vector2(length, thickness), new Vector2(thickness, length) })
                {
                    Image bar = CreateImage(target, "CornerBracket", VictoryBright);
                    RectTransform rect = bar.rectTransform;
                    rect.anchorMin = corner;
                    rect.anchorMax = corner;
                    rect.pivot = corner;
                    rect.sizeDelta = size;
                    rect.anchoredPosition = inward * inset;
                    if (register) brackets.Add(bar);
                    else bar.color = new Color(1f, 1f, 1f, 0.25f);
                }
            }
        }

        private static Text CreateText(Transform parent, string content, int size, TextAnchor alignment, Color color)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.text = content;
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            rectObject.transform.SetParent(parent, false);
            return rectObject.GetComponent<RectTransform>();
        }

        private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>Centre at (x, y) measured from the parent's top-left corner.</summary>
        private static void PlaceFromTopLeftCentered(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>Horizontally centred in the card, with its centre <paramref name="y"/> below the top edge.</summary>
        private static void PlaceCentered(RectTransform rect, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
