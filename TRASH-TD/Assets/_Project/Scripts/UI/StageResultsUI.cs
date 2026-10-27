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
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.UI
{
    /// <summary>
    /// Stage results as a "mission debrief" card in the green terminal style (GDD 1.4.6 & 1.9).
    /// Victory: STAGE CLEARED, three stars that stamp in with their rule (CLEARED / LIVES 50%+ / NO LEAKS), a scan
    /// sweep and pixel sparks. Defeat: SIGNAL LOST in red with static and glitching, plus an after-action report
    /// (wave reached, hostiles still in play, top leaker). Both show run stats that count up (time, lives, waves,
    /// defeated, leaked, deployed) and offer NEXT STAGE (victory, when there is one) / RETRY / STAGE SELECT.
    /// Built in code on its own canvas; runs on unscaled time (the game is stopped). Any click or key during the
    /// intro skips to the end; ENTER presses the highlighted button, R retries.
    /// </summary>
    public class StageResultsUI : MonoBehaviour
    {
        // Palette (terminal green, matching Settings / Intel; red for defeat)
        private static readonly Color VictoryAccent = new Color(0.18f, 0.82f, 0.45f, 1f);
        private static readonly Color VictoryBright = new Color(0.32f, 1.00f, 0.64f, 1f);
        private static readonly Color VictoryCard = new Color(0.016f, 0.042f, 0.028f, 0.98f);
        private static readonly Color VictoryHeader = new Color(0.030f, 0.090f, 0.055f, 1f);
        private static readonly Color VictoryButton = new Color(0.035f, 0.090f, 0.060f, 1f);
        private static readonly Color DefeatAccent = new Color(0.95f, 0.33f, 0.36f, 1f);
        private static readonly Color DefeatBright = new Color(1f, 0.55f, 0.50f, 1f);
        private static readonly Color DefeatCard = new Color(0.045f, 0.014f, 0.016f, 0.98f);
        private static readonly Color DefeatHeader = new Color(0.10f, 0.030f, 0.034f, 1f);
        private static readonly Color DefeatButton = new Color(0.10f, 0.035f, 0.035f, 1f);
        private static readonly Color StarGold = new Color(1f, 0.82f, 0.30f, 1f);
        private static readonly Color StarOff = new Color(1f, 1f, 1f, 0.16f);
        private static readonly Color LabelDim = new Color(0.62f, 0.68f, 0.72f, 1f);
        private static readonly Color BodyText = new Color(0.86f, 0.92f, 0.88f, 1f);
        private static readonly Color SelectedLabel = new Color(0.02f, 0.06f, 0.04f, 1f);

        // Layout, in card pixels from the top-left (1920x1080 reference)
        private const float CardWidth = 1100f;
        private const float CardHeight = 680f;
        private const float HeaderHeight = 64f;
        private const float Padding = 40f;
        private const float StatBoxHeight = 72f;
        private const float StatGap = 20f;
        private const float ButtonWidth = 260f;
        private const float ButtonHeight = 60f;

        private static readonly string[] StarRules = { "CLEARED", "LIVES 50%+", "NO LEAKS" };

        private sealed class StatBox
        {
            public Image Box;
            public Text Label;
            public Text Value;
            public int Target;
            public string Suffix;
            public bool IsTime;
        }

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
        private readonly List<Image> brackets = new List<Image>();
        private Text headerTitle;
        private Text headerStage;
        private Text titleText;
        private Text titleGhost;
        private Text subtitleText;
        private RawImage noiseImage;
        private Image sweepImage;
        private RectTransform starsRoot;
        private readonly Image[] starImages = new Image[3];
        private readonly Text[] starLabels = new Text[3];
        private RectTransform insightRoot;
        private Text insightHeader;
        private Text insightText;
        private readonly List<StatBox> statBoxes = new List<StatBox>();
        private Image divider;
        private Button nextButton;
        private Button retryButton;
        private Button selectButton;
        private readonly List<Button> activeButtons = new List<Button>();
        private Button primaryButton;
        private Text hintText;
        private RectTransform sparksRoot;
        private readonly List<Spark> sparks = new List<Spark>();

        // --- Run stats (counted while the stage runs) ---
        private int enemiesDefeated;
        private int enemiesLeaked;
        private int operatorsDeployed;
        private readonly Dictionary<string, int> leaksByEnemy = new Dictionary<string, int>();
        private EnemyManager trackedEnemyManager;
        private OperatorManager trackedOperatorManager;

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
            if (trackedEnemyManager != null)
            {
                trackedEnemyManager.OnEnemyDied += HandleEnemyDied;
                trackedEnemyManager.OnEnemyReachedExit += HandleEnemyLeaked;
            }

            trackedOperatorManager = OperatorManager.Instance;
            if (trackedOperatorManager != null) trackedOperatorManager.OnOperatorDeployed += HandleOperatorDeployed;
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStageVictory -= ShowVictory;
                GameManager.Instance.OnStageDefeat -= ShowDefeat;
            }

            if (trackedEnemyManager != null)
            {
                trackedEnemyManager.OnEnemyDied -= HandleEnemyDied;
                trackedEnemyManager.OnEnemyReachedExit -= HandleEnemyLeaked;
            }

            if (trackedOperatorManager != null) trackedOperatorManager.OnOperatorDeployed -= HandleOperatorDeployed;
        }

        private void HandleEnemyDied(EnemyBase enemy) => enemiesDefeated++;

        private void HandleEnemyLeaked(EnemyBase enemy)
        {
            enemiesLeaked++;
            string enemyName = enemy != null && enemy.Data != null ? enemy.Data.enemyName : "UNKNOWN";
            leaksByEnemy.TryGetValue(enemyName, out int count);
            leaksByEnemy[enemyName] = count + 1;
        }

        private void HandleOperatorDeployed(OperatorBase op) => operatorsDeployed++;

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
            WaveManager waveManager = FindAnyObjectByType<WaveManager>();
            StageData stage = manager != null ? manager.CurrentStage : null;

            ApplyTheme();

            // Header: MISSION DEBRIEF | STAGE 03 // RAILYARD DRAIN // HARD
            string mapName = stage != null && !string.IsNullOrEmpty(stage.mapName) ? stage.mapName.ToUpperInvariant() : "UNKNOWN SECTOR";
            int stageNumber = GetStageNumber(stage);
            string difficulty = manager != null ? manager.CurrentDifficulty.ToString().ToUpperInvariant() : "NORMAL";
            string difficultyColor = manager == null || manager.CurrentDifficulty == StageDifficulty.Normal ? "#FAC747"
                : manager.CurrentDifficulty == StageDifficulty.Easy ? "#4DDB94" : "#F2545C";
            headerStage.text = (stageNumber > 0 ? $"STAGE {stageNumber:00} // " : string.Empty) + $"{mapName} // <color={difficultyColor}>{difficulty}</color>";

            // Title + subtitle
            fullTitle = victory ? "STAGE CLEARED" : "SIGNAL LOST";
            titleText.text = string.Empty;
            titleGhost.text = string.Empty;
            bool timedOut = !victory && stage != null && stage.timeLimit > 0f && manager != null
                && manager.ElapsedTime >= stage.timeLimit && manager.CurrentLifePoints > 0;
            subtitleText.text = victory ? $"{mapName} SECURED" : timedOut ? "MISSION FAILED  -  TIME LIMIT REACHED" : "MISSION FAILED  -  ALL LIVES LOST";

            // Stars (victory) or after-action report (defeat)
            starsRoot.gameObject.SetActive(victory);
            insightRoot.gameObject.SetActive(!victory);
            for (int i = 0; i < 3; i++)
            {
                starStamped[i] = false;
                starImages[i].color = new Color(1f, 1f, 1f, 0f);
                starLabels[i].color = new Color(LabelDim.r, LabelDim.g, LabelDim.b, 0f);
            }

            int totalWaves = waveManager != null ? waveManager.TotalWaves : 0;
            int waveIndex = manager != null ? manager.GetCurrentWaveIndex() : 0;
            if (!victory)
            {
                int reached = totalWaves > 0 ? Mathf.Clamp(waveIndex + 1, 1, totalWaves) : waveIndex + 1;
                int inPlay = (EnemyManager.Instance != null ? EnemyManager.Instance.ActiveEnemyCount : 0)
                    + (waveManager != null ? waveManager.PendingSpawnCount : 0);
                string topLeaker = GetTopLeaker(out int topLeaks);
                insightText.text =
                    $"> REACHED WAVE {reached:00}" + (totalWaves > 0 ? $" / {totalWaves:00}" : string.Empty) + "\n" +
                    $"> {inPlay} HOSTILES STILL IN PLAY\n" +
                    (topLeaks > 0 ? $"> MOST LEAKS: {topLeaker.ToUpperInvariant()} ({topLeaks})" : "> NO LEAKS RECORDED");
            }

            // Stats
            int lives = manager != null ? manager.CurrentLifePoints : 0;
            int maxLives = manager != null ? manager.MaxLifePoints : 0;
            int wavesCleared = victory ? totalWaves : Mathf.Clamp(waveIndex, 0, Mathf.Max(0, totalWaves));
            SetStat(0, "TIME", manager != null ? Mathf.FloorToInt(manager.ElapsedTime) : 0, string.Empty, isTime: true);
            SetStat(1, "LIVES", lives, maxLives > 0 ? "/" + maxLives : string.Empty);
            SetStat(2, "WAVES", wavesCleared, totalWaves > 0 ? "/" + totalWaves : string.Empty);
            SetStat(3, "DEFEATED", enemiesDefeated, string.Empty);
            SetStat(4, "LEAKED", enemiesLeaked, string.Empty);
            SetStat(5, "DEPLOYED", operatorsDeployed, string.Empty);

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

        private void ApplyTheme()
        {
            Color accent = isVictory ? VictoryAccent : DefeatAccent;
            Color bright = isVictory ? VictoryBright : DefeatBright;

            cardImage.color = isVictory ? VictoryCard : DefeatCard;
            cardOutline.effectColor = new Color(accent.r, accent.g, accent.b, 0.55f);
            headerImage.color = isVictory ? VictoryHeader : DefeatHeader;
            headerLine.color = new Color(accent.r, accent.g, accent.b, 0.55f);
            foreach (Image bracket in brackets) bracket.color = bright;
            headerTitle.color = bright;
            titleText.color = bright;
            divider.color = new Color(accent.r, accent.g, accent.b, 0.3f);
            insightHeader.color = accent;
            noiseImage.gameObject.SetActive(!isVictory);
            sweepImage.gameObject.SetActive(isVictory);
            sweepImage.color = new Color(bright.r, bright.g, bright.b, 0f);

            foreach (StatBox stat in statBoxes)
            {
                stat.Box.color = isVictory ? VictoryButton : DefeatButton;
                stat.Value.color = bright;
            }

            foreach (Button button in new[] { nextButton, retryButton, selectButton })
            {
                MenuButtonFeedback feedback = button.GetComponent<MenuButtonFeedback>();
                feedback.glowColor = new Color(bright.r, bright.g, bright.b, 0.9f);
            }
        }

        private void SetStat(int index, string label, int value, string suffix, bool isTime = false)
        {
            StatBox stat = statBoxes[index];
            stat.Label.text = label;
            stat.Target = value;
            stat.Suffix = suffix;
            stat.IsTime = isTime;
            stat.Value.text = FormatStat(stat, 0);
        }

        private static string FormatStat(StatBox stat, int value)
        {
            if (stat.IsTime) return $"{value / 60:00}:{value % 60:00}";
            return value + stat.Suffix;
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

        /// <summary>The following numbered stage in Resources/Stages (STAGE_03 → STAGE_04), or null.</summary>
        private static StageData FindNextStage(StageData stage)
        {
            int number = GetStageNumber(stage);
            if (number <= 0) return null;
            return Resources.Load<StageData>($"Stages/STAGE_{number + 1:00}");
        }

        // ─────────────────────────────────────────────────────
        //  Animation (unscaled time; a click or key skips to the end)
        // ─────────────────────────────────────────────────────

        private float EndTime => isVictory ? 2.8f : 2.5f;
        private float StatsStart => isVictory ? 2.0f : 1.2f;
        private float ButtonsStart => isVictory ? 2.3f : 1.9f;

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

            float subK = Mathf.Clamp01((t - 0.85f) / 0.3f);
            subtitleText.color = new Color(BodyText.r, BodyText.g, BodyText.b, subK);

            if (isVictory)
            {
                // Scan line sweeps down the card
                float sweepK = Mathf.Clamp01((t - 0.2f) / 0.7f);
                sweepImage.rectTransform.anchoredPosition = new Vector2(0f, -Mathf.Lerp(HeaderHeight, CardHeight - 6f, EaseInOut(sweepK)));
                Color s = sweepImage.color;
                s.a = sweepK > 0f && sweepK < 1f ? 0.55f : 0f;
                sweepImage.color = s;

                // Stars stamp in one by one
                for (int i = 0; i < 3; i++)
                {
                    float start = 1.05f + i * 0.32f;
                    float k = Mathf.Clamp01((t - start) / 0.25f);
                    bool met = i < starCount;
                    Color starColor = met ? StarGold : StarOff;
                    starImages[i].color = new Color(starColor.r, starColor.g, starColor.b, starColor.a * k);
                    starImages[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(2.4f, 1f, EaseOutBack(k));
                    Color labelColor = met ? StarGold : LabelDim;
                    starLabels[i].color = new Color(labelColor.r, labelColor.g, labelColor.b, (met ? 1f : 0.45f) * k);

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
                float noise = t < 0.15f ? Mathf.Lerp(0.6f, 0.6f, t) : Mathf.Lerp(0.6f, 0.1f, Mathf.Clamp01((t - 0.15f) / 0.9f));
                noiseImage.color = new Color(1f, 1f, 1f, noise);

                float insightK = Mathf.Clamp01((t - 1.0f) / 0.35f);
                insightText.color = new Color(BodyText.r, BodyText.g, BodyText.b, insightK);
                insightHeader.color = new Color(insightHeader.color.r, insightHeader.color.g, insightHeader.color.b, insightK);
            }

            // Stats count up
            float statsK = Mathf.Clamp01((t - StatsStart) / 0.7f);
            float eased = 1f - (1f - statsK) * (1f - statsK);
            foreach (StatBox stat in statBoxes)
            {
                stat.Value.text = FormatStat(stat, Mathf.RoundToInt(stat.Target * eased));
            }

            // Buttons rise in
            float buttonsK = EaseOutCubic(Mathf.Clamp01((t - ButtonsStart) / 0.4f));
            for (int i = 0; i < activeButtons.Count; i++)
            {
                CanvasGroup group = activeButtons[i].GetComponent<CanvasGroup>();
                group.alpha = buttonsK;
                RectTransform rect = activeButtons[i].GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -(568f + (1f - buttonsK) * 30f));
            }
            hintText.color = new Color(LabelDim.r, LabelDim.g, LabelDim.b, 0.7f * buttonsK);
        }

        private void Update()
        {
            if (root == null || !root.activeSelf) return;
            float dt = Time.unscaledDeltaTime;

            HandleInput();
            UpdateSparks(dt);

            if (!isVictory)
            {
                // Static crawl and recurring glitch bursts on the title
                Rect uv = noiseImage.uvRect;
                uv.x = Random.value;
                uv.y = Random.value;
                noiseImage.uvRect = uv;
                if (introDone)
                {
                    noiseImage.color = new Color(1f, 1f, 1f, 0.08f + Random.value * 0.05f);
                }

                glitchTimer -= dt;
                bool burst = glitchTimer < 0.12f;
                if (glitchTimer <= 0f) glitchTimer = Random.Range(1.4f, 3.2f);
                bool glitching = burst || !introDone;
                Vector2 jitter = glitching ? new Vector2(Random.Range(-7f, 7f), Random.Range(-2f, 2f)) : Vector2.zero;
                titleText.rectTransform.anchoredPosition = new Vector2(jitter.x * 0.4f, -96f + jitter.y);
                titleGhost.rectTransform.anchoredPosition = new Vector2(-jitter.x - 4f, -96f + 2f);
                titleGhost.color = new Color(0.3f, 0.95f, 1f, glitching ? 0.6f : 0.18f);
            }
            else
            {
                titleText.rectTransform.anchoredPosition = new Vector2(0f, -96f);
                titleGhost.color = new Color(0f, 0f, 0f, 0f);
            }

            if (primaryButton != null && introDone)
            {
                MenuButtonFeedback feedback = primaryButton.GetComponent<MenuButtonFeedback>();
                feedback.restScale = 1f + 0.025f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f));
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
                CanvasGroup group = button.GetComponent<CanvasGroup>();
                group.blocksRaycasts = value;
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

            // Header
            headerImage = CreateImage(card, "Header", VictoryHeader);
            PlaceTopLeft(headerImage.rectTransform, 0f, 0f, CardWidth, HeaderHeight);
            headerLine = CreateImage(card, "HeaderLine", VictoryAccent);
            PlaceTopLeft(headerLine.rectTransform, 0f, HeaderHeight, CardWidth, 2f);
            headerTitle = CreateText(headerImage.rectTransform, "MISSION DEBRIEF", 18, TextAnchor.MiddleLeft, VictoryBright);
            PlaceTopLeft(headerTitle.rectTransform, Padding - 8f, 0f, 400f, HeaderHeight);
            headerStage = CreateText(headerImage.rectTransform, string.Empty, 12, TextAnchor.MiddleRight, LabelDim);
            PlaceTopLeft(headerStage.rectTransform, CardWidth - Padding + 8f - 640f, 0f, 640f, HeaderHeight);

            // Title (+ cyan ghost for the defeat glitch) and subtitle
            titleGhost = CreateText(card, string.Empty, 52, TextAnchor.MiddleCenter, Color.clear);
            PlaceCentered(titleGhost.rectTransform, -96f, CardWidth, 66f);
            titleText = CreateText(card, string.Empty, 52, TextAnchor.MiddleCenter, VictoryBright);
            PlaceCentered(titleText.rectTransform, -96f, CardWidth, 66f);
            var titleShadow = titleText.gameObject.AddComponent<Shadow>();
            titleShadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            titleShadow.effectDistance = new Vector2(0f, -4f);
            subtitleText = CreateText(card, string.Empty, 14, TextAnchor.MiddleCenter, BodyText);
            PlaceCentered(subtitleText.rectTransform, -168f, CardWidth, 24f);

            // Stars (victory)
            starsRoot = CreateRect(card, "Stars");
            PlaceTopLeft(starsRoot, 0f, 206f, CardWidth, 124f);
            Sprite starSprite = GameplayHUDUI.LoadIconSprite("icon_star");
            if (starSprite != null && starSprite.texture != null) starSprite.texture.filterMode = FilterMode.Point;
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 240f;
                Image star = CreateImage(starsRoot, "Star" + (i + 1), Color.white);
                star.sprite = starSprite;
                star.preserveAspect = true;
                star.rectTransform.anchorMin = star.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                star.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                star.rectTransform.anchoredPosition = new Vector2(x, -42f);
                star.rectTransform.sizeDelta = new Vector2(76f, 76f);
                starImages[i] = star;

                Text label = CreateText(starsRoot, StarRules[i], 12, TextAnchor.MiddleCenter, LabelDim);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                label.rectTransform.anchoredPosition = new Vector2(x, -104f);
                label.rectTransform.sizeDelta = new Vector2(220f, 20f);
                starLabels[i] = label;
            }

            // After-action report (defeat)
            insightRoot = CreateRect(card, "AfterAction");
            PlaceTopLeft(insightRoot, Padding + 120f, 206f, CardWidth - 2f * (Padding + 120f), 124f);
            Image insightBack = CreateImage(insightRoot, "Back", new Color(0f, 0f, 0f, 0.25f));
            SetStretch(insightBack.rectTransform);
            insightHeader = CreateText(insightRoot, "// AFTER-ACTION REPORT", 12, TextAnchor.MiddleLeft, DefeatAccent);
            PlaceTopLeft(insightHeader.rectTransform, 20f, 10f, 600f, 22f);
            insightText = CreateText(insightRoot, string.Empty, 13, TextAnchor.UpperLeft, BodyText);
            insightText.lineSpacing = 1.5f;
            insightText.verticalOverflow = VerticalWrapMode.Overflow;
            PlaceTopLeft(insightText.rectTransform, 20f, 40f, CardWidth - 2f * (Padding + 120f) - 40f, 80f);

            // Stats: 2 rows of 3
            float boxWidth = (CardWidth - 2f * Padding - 2f * StatGap) / 3f;
            for (int i = 0; i < 6; i++)
            {
                int column = i % 3;
                int row = i / 3;
                Image box = CreateImage(card, "Stat" + i, VictoryButton);
                PlaceTopLeft(box.rectTransform, Padding + column * (boxWidth + StatGap), 352f + row * (StatBoxHeight + 16f), boxWidth, StatBoxHeight);
                Outline boxOutline = box.gameObject.AddComponent<Outline>();
                boxOutline.effectColor = new Color(1f, 1f, 1f, 0.08f);
                boxOutline.effectDistance = new Vector2(1f, -1f);

                Text label = CreateText(box.rectTransform, string.Empty, 10, TextAnchor.MiddleLeft, LabelDim);
                PlaceTopLeft(label.rectTransform, 18f, 0f, boxWidth * 0.5f, StatBoxHeight);
                Text value = CreateText(box.rectTransform, "0", 24, TextAnchor.MiddleRight, VictoryBright);
                PlaceTopLeft(value.rectTransform, boxWidth * 0.4f, 0f, boxWidth * 0.6f - 18f, StatBoxHeight);
                statBoxes.Add(new StatBox { Box = box, Label = label, Value = value });
            }

            divider = CreateImage(card, "Divider", VictoryAccent);
            PlaceTopLeft(divider.rectTransform, Padding, 542f, CardWidth - 2f * Padding, 2f);

            // Buttons (laid out per result in LayoutButtons)
            nextButton = CreateButton(card, "NextStageButton", "NEXT STAGE", OnNextClicked);
            retryButton = CreateButton(card, "RetryButton", "RETRY", OnRetryClicked);
            selectButton = CreateButton(card, "StageSelectButton", "STAGE SELECT", OnStageSelectClicked);

            hintText = CreateText(card, string.Empty, 10, TextAnchor.MiddleCenter, LabelDim);
            PlaceCentered(hintText.rectTransform, -650f, CardWidth, 20f);

            sparksRoot = CreateRect(root.transform, "Sparks");
            SetStretch(sparksRoot);
        }

        /// <summary>Centres the active buttons on one row; the primary one is filled with the accent colour.</summary>
        private void LayoutButtons()
        {
            Color accent = isVictory ? VictoryAccent : DefeatAccent;
            Color idle = isVictory ? VictoryButton : DefeatButton;
            const float gap = 24f;
            float total = activeButtons.Count * ButtonWidth + (activeButtons.Count - 1) * gap;
            for (int i = 0; i < activeButtons.Count; i++)
            {
                Button button = activeButtons[i];
                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
                rect.anchoredPosition = new Vector2(-total * 0.5f + ButtonWidth * 0.5f + i * (ButtonWidth + gap), -568f);

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
            feedback.hoverScale = 1.05f;
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

        /// <summary>Horizontally centred in the card, with its centre <paramref name="y"/> below the top edge.</summary>
        private static void PlaceCentered(RectTransform rect, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, y);
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
