using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TrashTD.Core.GameLoop;
using TrashTD.Data;
using TrashTD.Systems;
using TrashTD.UI;

namespace TrashTD.Development
{
    /// <summary>
    /// Development-only controls for the SANDBOX stage. This component is intentionally
    /// enabled only for the explicitly marked sandbox stage.
    ///
    /// Before a wave: clicking an enemy adds it to the spawn queue; START WAVE deploys the queue
    /// at the normal stage spawn interval. During a wave: clicking an enemy spawns it instantly;
    /// END WAVE cancels pending spawns and kills every enemy. Clicking an operator adds its card
    /// to the deck immediately.
    /// </summary>
    public sealed class SandboxController : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.025f, 0.035f, 0.05f, 0.94f);
        private static readonly Color ListColor = new Color(0.04f, 0.05f, 0.08f, 1f);
        private static readonly Color RowColor = new Color(0.1f, 0.14f, 0.2f, 1f);
        private static readonly Color HeaderColor = new Color(0.14f, 0.2f, 0.28f, 1f);
        private static readonly Color DangerColor = new Color(0.45f, 0.14f, 0.14f, 1f);
        private static readonly Color AccentColor = new Color(0.35f, 1f, 0.5f, 1f);
        private static readonly Color AirColor = new Color(0.55f, 0.8f, 1f, 1f);
        private static readonly Color GroundColor = new Color(1f, 0.8f, 0.45f, 1f);
        private static readonly Color DimColor = new Color(0.7f, 0.75f, 0.8f, 1f);

        private const float PanelWidth = 280f;
        private const float RowHeight = 30f;
        private const int RowFontSize = 11;

        private readonly List<EnemyData> enemyQueue = new List<EnemyData>();
        private readonly List<EnemyData> availableEnemies = new List<EnemyData>();
        private readonly List<OperatorData> operatorData = new List<OperatorData>();

        private StageBootstrapper bootstrapper;
        private WaveManager waveManager;
        private GameManager gameManager;
        private PlayerDeck playerDeck;
        private GameplayHUDUI gameplayHud;

        private Text modeText;
        private Text statusText;
        private Text queueHeaderText;
        private Text queueListText;
        private Text deckText;
        private bool lastWaveState;

        public void Initialize(StageBootstrapper stage)
        {
            bootstrapper = stage;
            waveManager = stage.waveManager;
            gameManager = stage.gameManager;
            playerDeck = stage.playerDeck;
            gameplayHud = FindFirstObjectByType<GameplayHUDUI>();
            gameplayHud?.RegisterSandboxController(this);
            LoadEnemyData();
            LoadOperatorData();
            if (gameManager != null && gameManager.CurrentPhase == StagePhase.CardPick)
                gameManager.EnterPreparationPhase();
            BuildPanel();

            if (playerDeck != null) playerDeck.OnDeckChanged += HandleDeckChanged;
            RefreshAll();
        }

        private void OnDestroy()
        {
            if (playerDeck != null) playerDeck.OnDeckChanged -= HandleDeckChanged;
        }

        private void Update()
        {
            bool waveActive = waveManager != null && waveManager.IsWaveInProgress;
            if (waveActive != lastWaveState) RefreshAll();
        }

        // ============================
        // Data
        // ============================

        private void LoadOperatorData()
        {
            operatorData.Clear();
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:OperatorData", new[] { "Assets/_Project/Data/Operators" });
            for (int i = 0; i < guids.Length; i++)
            {
                OperatorData data = UnityEditor.AssetDatabase.LoadAssetAtPath<OperatorData>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]));
                if (data != null) operatorData.Add(data);
            }
#endif
            if (operatorData.Count == 0 && bootstrapper != null && bootstrapper.operatorPool != null)
                operatorData.AddRange(bootstrapper.operatorPool);

            operatorData.RemoveAll(op => op == null);
            operatorData.Sort((a, b) => string.Compare(a.operatorName, b.operatorName, System.StringComparison.OrdinalIgnoreCase));
        }

        private void LoadEnemyData()
        {
            availableEnemies.Clear();
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:EnemyData", new[] { "Assets/_Project/Data/Enemies" });
            for (int i = 0; i < guids.Length; i++)
            {
                EnemyData data = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyData>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]));
                if (data != null) availableEnemies.Add(data);
            }
#endif
            if (availableEnemies.Count == 0 && bootstrapper != null && bootstrapper.stageData != null)
            {
                AddEnemiesFromWaves(bootstrapper.stageData.wavesEasy);
                AddEnemiesFromWaves(bootstrapper.stageData.wavesNormal);
                AddEnemiesFromWaves(bootstrapper.stageData.wavesHard);
            }

            // Ground enemies first, then air, alphabetical within each group.
            availableEnemies.Sort((a, b) =>
            {
                int byType = a.movementType.CompareTo(b.movementType);
                return byType != 0 ? byType : string.Compare(a.enemyName, b.enemyName, System.StringComparison.OrdinalIgnoreCase);
            });
        }

        private void AddEnemiesFromWaves(WaveData[] waves)
        {
            if (waves == null) return;
            for (int i = 0; i < waves.Length; i++)
            {
                if (waves[i]?.entries == null) continue;
                for (int j = 0; j < waves[i].entries.Length; j++)
                {
                    EnemyData data = waves[i].entries[j]?.enemyData;
                    if (data != null && !availableEnemies.Contains(data))
                        availableEnemies.Add(data);
                }
            }
        }

        // ============================
        // Actions
        // ============================

        private void HandleEnemyClicked(EnemyData enemy)
        {
            if (enemy == null || waveManager == null) return;

            if (waveManager.IsWaveInProgress)
            {
                SetStatus(waveManager.SpawnSandboxEnemyNow(enemy)
                    ? $"Spawned {enemy.enemyName}."
                    : $"Could not spawn {enemy.enemyName} (no path, see Console).");
                return;
            }

            enemyQueue.Add(enemy);
            SetStatus($"Queued {enemy.enemyName}.");
            RefreshQueue();
        }

        private void UndoLastQueued()
        {
            if (enemyQueue.Count == 0) return;
            enemyQueue.RemoveAt(enemyQueue.Count - 1);
            RefreshQueue();
        }

        private void ClearQueue()
        {
            enemyQueue.Clear();
            RefreshQueue();
        }

        private void AddOperator(OperatorData op)
        {
            if (playerDeck == null || op == null) return;
            if (playerDeck.AddCard(new DraftCard(op, op.baseRarity)))
                SetStatus($"Added {op.operatorName} to deck.");
            else
                SetStatus($"Deck full ({PlayerDeck.MAX_DECK_SIZE}). Deploy or clear cards first.");
        }

        private void ClearDeck()
        {
            playerDeck?.ClearDeck();
            SetStatus("Deck cleared.");
        }

        /// <summary>Called by the HUD's Start/End Wave button.</summary>
        public void ToggleWave()
        {
            if (waveManager == null) return;

            if (waveManager.IsWaveInProgress)
            {
                waveManager.EndSandboxWave();
                SetStatus("Wave ended. All enemies cleared.");
            }
            else if (waveManager.StartSandboxWave(enemyQueue))
            {
                SetStatus(enemyQueue.Count > 0
                    ? $"Deploying {enemyQueue.Count} queued enemies."
                    : "Wave live with an empty queue. Click enemies to spawn.");
                enemyQueue.Clear();
            }

            RefreshAll();
        }

        private void HandleDeckChanged(IReadOnlyList<DraftCard> _) => RefreshDeck();

        // ============================
        // Refresh
        // ============================

        private void RefreshAll()
        {
            lastWaveState = waveManager != null && waveManager.IsWaveInProgress;
            if (modeText != null)
            {
                modeText.text = lastWaveState
                    ? "WAVE LIVE\nClick an enemy to spawn it now."
                    : "PRE-WAVE\nClick enemies to queue them.";
                modeText.color = lastWaveState ? new Color(1f, 0.45f, 0.4f) : AccentColor;
            }
            RefreshQueue();
            RefreshDeck();
            gameplayHud?.SetSandboxWaveButton(lastWaveState);
        }

        private void RefreshQueue()
        {
            if (queueHeaderText != null)
                queueHeaderText.text = $"QUEUE ({enemyQueue.Count})";
            if (queueListText == null) return;

            if (enemyQueue.Count == 0)
            {
                queueListText.text = "(empty)";
                return;
            }

            // Collapse consecutive duplicates: "Sludge Grunt x3"
            var builder = new StringBuilder();
            for (int i = 0; i < enemyQueue.Count;)
            {
                int run = 1;
                while (i + run < enemyQueue.Count && enemyQueue[i + run] == enemyQueue[i]) run++;
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(enemyQueue[i].enemyName);
                if (run > 1) builder.Append(" x").Append(run);
                i += run;
            }
            queueListText.text = builder.ToString();
        }

        private void RefreshDeck()
        {
            if (deckText != null && playerDeck != null)
                deckText.text = $"DECK {playerDeck.CardCount}/{PlayerDeck.MAX_DECK_SIZE}";
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        // ============================
        // UI construction
        // ============================

        private void BuildPanel()
        {
            var panel = new GameObject("SandboxControls", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            RectTransform rect = panel.GetComponent<RectTransform>();
            if (gameplayHud != null)
            {
                gameplayHud.AttachSandboxPanel(rect);
            }
            else
            {
                Canvas canvas = FindFirstObjectByType<Canvas>();
                if (canvas == null) return;
                rect.SetParent(canvas.transform, false);
            }

            // Left column between the HUD top bar (90px) and the deck bar (170px), in 1920x1080 reference units.
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(10f, 180f);
            rect.offsetMax = new Vector2(10f + PanelWidth, -100f);
            panel.GetComponent<Image>().color = PanelColor;

            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 6f;
            ConfigureColumn(layout);

            Text title = MakeText(panel.transform, "QA SANDBOX", 16, AccentColor, TextAnchor.MiddleLeft);
            SetHeight(title, 24f);
            modeText = MakeText(panel.transform, "", 10, AccentColor, TextAnchor.UpperLeft);
            statusText = MakeText(panel.transform, "Ready.", 10, Color.yellow, TextAnchor.UpperLeft);

            // Everything else scrolls inside a masked viewport, so lists can never spill over other controls.
            Transform content = MakeScrollView(panel.transform);

            Transform enemyList = MakeSection(content, "ENEMIES", true);
            for (int i = 0; i < availableEnemies.Count; i++)
            {
                EnemyData enemy = availableEnemies[i];
                bool isAir = enemy.movementType == EnemyMovementType.Air;
                MakeRow(enemyList, enemy.enemyName, isAir ? "AIR" : "GND", isAir ? AirColor : GroundColor, RowColor)
                    .onClick.AddListener(() => HandleEnemyClicked(enemy));
            }

            queueHeaderText = MakeText(content, "QUEUE (0)", 12, Color.white, TextAnchor.MiddleLeft);
            SetHeight(queueHeaderText, 22f);
            queueListText = MakeText(content, "(empty)", 10, DimColor, TextAnchor.UpperLeft);
            Transform queueButtons = MakeButtonStrip(content);
            MakeRow(queueButtons, "UNDO", null, Color.white, HeaderColor).onClick.AddListener(UndoLastQueued);
            MakeRow(queueButtons, "CLEAR", null, Color.white, DangerColor).onClick.AddListener(ClearQueue);

            Transform operatorList = MakeSection(content, "OPERATORS", true);
            deckText = MakeText(operatorList, "DECK 0/8", 11, AccentColor, TextAnchor.MiddleLeft);
            SetHeight(deckText, 22f);
            for (int i = 0; i < operatorData.Count; i++)
            {
                OperatorData op = operatorData[i];
                MakeRow(operatorList, op.operatorName, op.operatorClass.ToString().ToUpperInvariant(), DimColor, RowColor)
                    .onClick.AddListener(() => AddOperator(op));
            }
            MakeRow(operatorList, "CLEAR DECK", null, Color.white, DangerColor).onClick.AddListener(ClearDeck);
        }

        private static void ConfigureColumn(VerticalLayoutGroup layout)
        {
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private static Transform MakeScrollView(Transform parent)
        {
            var root = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = ListColor;
            LayoutElement element = root.GetComponent<LayoutElement>();
            element.flexibleHeight = 1f;
            element.minHeight = 120f;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(root.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 4f;
            ConfigureColumn(layout);
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = root.GetComponent<RectTransform>();
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            return content.transform;
        }

        /// <summary>Collapsible section: a header button that shows/hides the returned list container.</summary>
        private static Transform MakeSection(Transform parent, string title, bool expanded)
        {
            Button header = MakeRow(parent, "", null, Color.white, HeaderColor);
            Text headerLabel = header.GetComponentInChildren<Text>();

            var list = new GameObject(title + "List", typeof(RectTransform), typeof(VerticalLayoutGroup));
            list.transform.SetParent(parent, false);
            VerticalLayoutGroup layout = list.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            ConfigureColumn(layout);

            void Apply(bool open)
            {
                list.SetActive(open);
                headerLabel.text = (open ? "v " : "> ") + title;
            }

            header.onClick.AddListener(() => Apply(!list.activeSelf));
            Apply(expanded);
            return list.transform;
        }

        private static Transform MakeButtonStrip(Transform parent)
        {
            var strip = new GameObject("ButtonStrip", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            strip.transform.SetParent(parent, false);
            strip.GetComponent<LayoutElement>().preferredHeight = RowHeight;
            HorizontalLayoutGroup layout = strip.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return strip.transform;
        }

        /// <summary>Clickable row with a left-aligned label and an optional right-aligned tag.</summary>
        private static Button MakeRow(Transform parent, string label, string tag, Color tagColor, Color background)
        {
            var obj = new GameObject(string.IsNullOrEmpty(label) ? "Row" : label,
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<Image>().color = background;
            obj.GetComponent<LayoutElement>().preferredHeight = RowHeight;

            Button button = obj.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            button.colors = colors;

            Text text = MakeText(obj.transform, label, RowFontSize, Color.white,
                tag == null ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 8f, tag == null ? 8f : 52f);

            if (tag != null)
            {
                Text tagText = MakeText(obj.transform, tag, 9, tagColor, TextAnchor.MiddleRight);
                Stretch(tagText.rectTransform, 8f, 8f);
            }
            return button;
        }

        private static Text MakeText(Transform parent, string value, int size, Color color, TextAnchor alignment)
        {
            var obj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            Text text = obj.GetComponent<Text>();
            text.text = value;
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.lineSpacing = 1.2f;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void SetHeight(Text text, float height)
        {
            text.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        }

        private static void Stretch(RectTransform rect, float left, float right)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, 0f);
            rect.offsetMax = new Vector2(-right, 0f);
        }
    }
}
