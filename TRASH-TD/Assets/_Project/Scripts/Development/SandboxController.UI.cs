using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TrashTD.Combat;
using TrashTD.Core.GameLoop;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;
using TrashTD.UI;

namespace TrashTD.Development
{
    /// <summary>
    /// The sandbox UI. Nothing covers the map by default: a slim toolbar under the HUD's top-right corner,
    /// a one-line live readout under it, and dropdowns that open one at a time below that.
    /// Dropdowns stay open while the map is used (spawning mid-wave, deploying from the deck) and close
    /// from their toolbar button, another dropdown, or Esc. Messages are short toasts at the top centre.
    /// </summary>
    public sealed partial class SandboxController
    {
        private enum Dropdown { None = -1, Enemies, Operators, Waves, Queue, Map }

        private static readonly string[] DropdownLabels = { "ENEMIES", "OPERATORS", "WAVES", "QUEUE", "MAP" };
        private static readonly float[] DropdownWidths = { 404f, 404f, 340f, 340f, 300f };

        // Palette: the HUD's terminal colours on flat dark surfaces
        private static readonly Color PanelTint = new Color(0.03f, 0.04f, 0.06f, 0.96f);
        private static readonly Color SurfaceColor = new Color(0.055f, 0.07f, 0.1f, 1f);
        private static readonly Color RowColor = new Color(0.09f, 0.12f, 0.165f, 1f);
        private static readonly Color RowCurrentColor = new Color(0.08f, 0.22f, 0.15f, 1f);
        private static readonly Color UntimedRowColor = new Color(0.12f, 0.15f, 0.2f, 1f);
        private static readonly Color ControlColor = new Color(0.15f, 0.19f, 0.25f, 1f);
        private static readonly Color DangerColor = new Color(0.52f, 0.16f, 0.15f, 1f);
        private static readonly Color WarnFill = new Color(0.45f, 0.32f, 0.07f, 1f);
        private static readonly Color AccentColor = new Color(0.18f, 0.82f, 0.45f, 1f);
        private static readonly Color AccentFill = new Color(0.1f, 0.42f, 0.25f, 1f);
        private static readonly Color PrepColor = new Color(0.5f, 0.8f, 1f, 1f);
        private static readonly Color WaveColor = new Color(1f, 0.45f, 0.35f, 1f);
        private static readonly Color GoldColor = new Color(1f, 0.78f, 0.25f, 1f);
        private static readonly Color BadgeTextColor = new Color(0.1f, 0.07f, 0.02f, 1f);
        private static readonly Color TextColor = new Color(0.92f, 0.95f, 0.98f, 1f);
        private static readonly Color TextDim = new Color(0.5f, 0.58f, 0.68f, 1f);
        private static readonly Color LineColor = new Color(0.2f, 0.26f, 0.34f, 1f);
        // Declared after the colours they read (static fields initialise in order within a file)
        private static readonly string DimHex = "#" + ColorUtility.ToHtmlStringRGB(TextDim);
        private static readonly string PrepHex = "#" + ColorUtility.ToHtmlStringRGB(PrepColor);
        private static readonly string WaveHex = "#" + ColorUtility.ToHtmlStringRGB(WaveColor);

        // Layout, in 1920x1080 reference units
        private const float ScreenMargin = 10f;
        private const float ToolbarTop = 96f;        // just under the HUD top bar (90)
        private const float ToolbarHeight = 46f;
        private const int ToolbarFontSize = 14;       // PressStart2P: one glyph is as wide as the font size
        private const float ToolbarIconSize = 22f;
        private const float ReadoutTop = ToolbarTop + ToolbarHeight + 3f;
        private const float ReadoutHeight = 26f;
        private const float DropdownTop = ReadoutTop + ReadoutHeight + 5f;
        private const float DropdownBottom = 180f;   // clears the deck bar (170)
        private const int GridColumns = 5;
        private static readonly Vector2 TileSize = new Vector2(72f, 84f);
        private const float ListRowHeight = 22f;
        private const float QueueRowHeight = 30f;
        private const float SmallButtonSize = 20f;
        private const float StatsCardWidth = 240f;
        private const float TooltipWidth = 190f;
        private const float ToastSeconds = 3f;
        private const int DamageMeterRows = 10;

        // Kept across the scene reload a map switch does
        private static Dropdown openDropdown = Dropdown.None;
        private static bool statsVisible;
        private static bool uiHidden;
        private static int enemyFilter = -1;    // EnemyArchetype index, -1 = all
        private static int operatorFilter = -1; // OperatorClass index, -1 = all
        private static string enemySearch = string.Empty;
        private static string operatorSearch = string.Empty;

        private sealed class UnitTile
        {
            public EnemyData enemy;
            public OperatorData op;
            public GameObject root;
            public GameObject badge;
            public Text badgeText;
            public string searchName;
        }

        private struct ScrollArea
        {
            public GameObject page;
            public RectTransform content;
            public LayoutElement element;
        }

        private RectTransform uiRoot;
        private RectTransform canvasRect;
        private Camera uiCamera;
        private readonly Vector3[] corners = new Vector3[4];

        // Toolbar, readout, toast, hint
        private GameObject toolbar;
        private GameObject showButton;
        private readonly Button[] dropdownButtons = new Button[5];
        private readonly GameObject[] dropdownPanels = new GameObject[5];
        private readonly ScrollArea[] dropdownScrolls = new ScrollArea[5];
        private Text queueButtonText;
        private Button killAllButton, hazardButton, statsButton;
        private Image hazardIcon;
        private GameObject readout;
        private Text readoutText;
        private GameObject toast;
        private CanvasGroup toastGroup;
        private Text toastText;
        private float toastTimer;
        private RectTransform hintRect;
        private Text hintText;

        // ENEMIES
        private readonly List<UnitTile> enemyTiles = new List<UnitTile>();
        private readonly List<Button> enemyFilterButtons = new List<Button>();
        private readonly Button[] amountButtons = new Button[3];
        private Text enemyHintText, enemyEmptyText;

        // OPERATORS
        private readonly List<UnitTile> operatorTiles = new List<UnitTile>();
        private readonly List<Button> operatorFilterButtons = new List<Button>();
        private Text deckCountText, operatorEmptyText;

        // WAVES
        private readonly Button[] difficultyButtons = new Button[3];
        private readonly List<Button> waveRowButtons = new List<Button>();
        private Text waveStageText, waveDetailTitle, waveDetailSummary;
        private Transform waveRowsRoot, rosterRoot;
        private Button loadWaveButton, addWaveButton;
        private WaveData[] builtWaves;
        private bool wavesBuilt;
        private int builtRosterIndex = -1;

        // QUEUE
        private Text queueTitleText, queueEmptyText;
        private Button keepQueueButton;
        private Transform queueRowsRoot;

        // MAP
        private Button mapHazardButton;

        // Stats card
        private GameObject statsCard;
        private Text statsWaveText, damageNamesText, damageValuesText;

        // Enemy tooltip
        private RectTransform tooltip;
        private Text tooltipTitle, tooltipBody;
        private RectTransform tooltipHpFill;
        private Image tooltipHpFillImage;
        private EnemyBase tooltipTarget;

        /// <summary>True while the pointer is over the open dropdown (right-clicks there edit the queue, not operators).</summary>
        public bool IsPointerOverDropdown
        {
            get
            {
                if (uiHidden || openDropdown == Dropdown.None || Mouse.current == null) return false;
                GameObject panel = dropdownPanels[(int)openDropdown];
                if (panel == null || !panel.activeInHierarchy) return false;
                Vector2 pointer = CrtDisplay.ScreenToPicture(Mouse.current.position.ReadValue());
                return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)panel.transform, pointer, uiCamera);
            }
        }

        // ============================
        // Open / close
        // ============================

        private void ToggleDropdown(Dropdown dropdown)
        {
            openDropdown = openDropdown == dropdown ? Dropdown.None : dropdown;
            ApplyVisibility();
        }

        private void ToggleStats()
        {
            statsVisible = !statsVisible;
            ApplyVisibility();
            if (statsVisible) RefreshStats();
        }

        private void SetUiHidden(bool hidden)
        {
            uiHidden = hidden;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            HideHint();
            if (toolbar != null) toolbar.SetActive(!uiHidden);
            if (readout != null) readout.SetActive(!uiHidden);
            if (showButton != null) showButton.SetActive(uiHidden);
            if (uiHidden && toast != null) toast.SetActive(false);
            for (int i = 0; i < dropdownPanels.Length; i++)
            {
                bool open = !uiHidden && (int)openDropdown == i;
                if (dropdownPanels[i] != null) dropdownPanels[i].SetActive(open);
                if (dropdownButtons[i] != null) Paint(dropdownButtons[i], open ? AccentFill : ControlColor);
            }
            if (statsCard != null) statsCard.SetActive(!uiHidden && statsVisible);
            if (statsButton != null) Paint(statsButton, statsVisible ? AccentFill : ControlColor);
            if (!uiHidden && openDropdown != Dropdown.None) FitDropdown(openDropdown);
        }

        /// <summary>Esc closes the open dropdown, unless a placement is being cancelled or the game is paused.</summary>
        private void HandleEscape()
        {
            if (uiHidden || openDropdown == Dropdown.None) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
            if (gameplayHud != null && (gameplayHud.HasConsumedEscapeThisFrame || gameplayHud.IsPaused)) return;
            if (bootstrapper != null && bootstrapper.IsPlacementActive) return;

            openDropdown = Dropdown.None;
            ApplyVisibility();
            gameplayHud?.ConsumeEscapeKey();
        }

        /// <summary>Per-frame UI work: the toast fade and keeping the enemy tooltip next to its enemy.</summary>
        private void UpdateUiFrame()
        {
            if (toastTimer > 0f && toast != null)
            {
                toastTimer -= Time.unscaledDeltaTime;
                toastGroup.alpha = Mathf.Clamp01(toastTimer / 0.4f);
                if (toastTimer <= 0f) toast.SetActive(false);
            }
            PositionTooltip();
        }

        private void SetStatus(string message, bool isProblem = false)
        {
            if (toast == null) return;
            toastText.text = message;
            toastText.color = isProblem ? WaveColor : GoldColor;
            toastTimer = ToastSeconds;
            toastGroup.alpha = 1f;
            toast.SetActive(!uiHidden);
            toast.transform.SetAsLastSibling();
        }

        // ============================
        // Refresh
        // ============================

        private void RefreshAll()
        {
            lastWaveState = IsWaveActive;
            RefreshSpawnAmount();
            RefreshQueue();
            RefreshDeck();
            RefreshWaves();
            RefreshHazard();
            RefreshLive();
            gameplayHud?.SetSandboxWaveButton(lastWaveState);
        }

        private void RefreshLive()
        {
            int alive = enemyManager != null ? enemyManager.ActiveEnemyCount : 0;
            if (readoutText != null)
            {
                int pending = lastWaveState && waveManager != null ? waveManager.PendingSpawnCount : CountQueued();
                string mode = lastWaveState ? $"<color={WaveHex}>WAVE LIVE</color>" : $"<color={PrepHex}>PRE-WAVE</color>";
                string leaks = waveLeaks > 0 ? $"<color={WaveHex}>{waveLeaks}</color>" : "0";
                readoutText.text =
                    $"{mode}   <color={DimHex}>TIME</color> {FormatTime(waveTime)}   <color={DimHex}>ALIVE</color> {alive}   " +
                    $"<color={DimHex}>{(lastWaveState ? "TO SPAWN" : "QUEUED")}</color> {pending}   " +
                    $"<color={DimHex}>KILLS</color> {waveKills}   <color={DimHex}>LEAKS</color> {leaks}";
            }
            if (killAllButton != null && killAllButton.interactable != (lastWaveState && alive > 0))
            {
                killAllButton.interactable = lastWaveState && alive > 0;
                Paint(killAllButton, DangerColor);
            }
        }

        private void RefreshSpawnAmount()
        {
            for (int i = 0; i < amountButtons.Length; i++)
                SetSelected(amountButtons[i], i == spawnAmountIndex);
            if (enemyHintText == null) return;
            int amount = SpawnAmount;
            enemyHintText.text = lastWaveState
                ? $"WAVE LIVE: click spawns {amount} now"
                : $"PRE-WAVE: click queues {amount}, right-click removes {amount}";
            enemyHintText.color = lastWaveState ? WaveColor : PrepColor;
        }

        private void RefreshQueue()
        {
            int total = CountQueued();
            if (!queue.Exists(group => group.timed)) loadedWaveNumber = 0;

            if (queueButtonText != null) queueButtonText.text = total > 0 ? $"QUEUE {total}" : "QUEUE";
            if (queueTitleText != null)
            {
                bool hasUntimed = queue.Exists(group => !group.timed);
                string source = loadedWaveNumber > 0 ? loadedWaveLabel + (hasUntimed ? " + extras" : "") : total > 0 ? "Hand-built" : "";
                queueTitleText.text = total > 0 ? $"{total} ENEMIES  <color={DimHex}>{source}</color>" : "QUEUE EMPTY";
            }
            if (keepQueueButton != null) Paint(keepQueueButton, keepQueueAfterStart ? AccentFill : ControlColor);

            foreach (UnitTile tile in enemyTiles)
                SetBadge(tile, CountQueued(tile.enemy));

            if (queueRowsRoot == null) return;
            ClearChildren(queueRowsRoot);
            queueEmptyText.gameObject.SetActive(queue.Count == 0);
            for (int i = 0; i < queue.Count; i++)
                MakeQueueRow(queueRowsRoot, queue[i], IsFirstUntimed(i), IsLastUntimed(i));
            FitDropdown(Dropdown.Queue);
        }

        private bool IsFirstUntimed(int index)
        {
            for (int i = index - 1; i >= 0; i--) if (!queue[i].timed) return false;
            return true;
        }

        private bool IsLastUntimed(int index)
        {
            for (int i = index + 1; i < queue.Count; i++) if (!queue[i].timed) return false;
            return true;
        }

        private void RefreshDeck()
        {
            if (playerDeck == null) return;
            if (deckCountText != null)
            {
                bool full = playerDeck.CardCount >= PlayerDeck.MAX_DECK_SIZE;
                deckCountText.text = $"DECK {playerDeck.CardCount}/{PlayerDeck.MAX_DECK_SIZE}";
                deckCountText.color = full ? WaveColor : AccentColor;
            }

            IReadOnlyList<DraftCard> deck = playerDeck.DeckSlots;
            foreach (UnitTile tile in operatorTiles)
            {
                int copies = 0;
                for (int i = 0; i < deck.Count; i++)
                    if (deck[i] != null && deck[i].operatorData == tile.op) copies++;
                SetBadge(tile, copies);
            }
        }

        private void RefreshWaves()
        {
            if (waveRowsRoot == null) return;

            for (int i = 0; i < difficultyButtons.Length; i++)
                SetSelected(difficultyButtons[i], i == (int)waveDifficulty);

            WaveData[] waves = GetMapWaves();
            if (!wavesBuilt || waves != builtWaves) RebuildWaveRows(waves);

            bool hasWaves = waves != null && waves.Length > 0;
            loadWaveButton.interactable = hasWaves;
            addWaveButton.interactable = hasWaves;
            Paint(loadWaveButton, AccentFill);
            Paint(addWaveButton, ControlColor);
            string stage = currentMap != null ? "STAGE " + GetStageNumber(currentMap) : "THIS MAP";
            waveStageText.text = hasWaves
                ? $"{stage}  <color={DimHex}>{waves.Length} WAVES, gold = spike</color>"
                : $"{stage}  <color={DimHex}>no waves on this difficulty</color>";

            if (!hasWaves)
            {
                waveDetailTitle.text = "-";
                waveDetailSummary.text = string.Empty;
                ClearChildren(rosterRoot);
                builtRosterIndex = -1;
                FitDropdown(Dropdown.Waves);
                return;
            }

            waveIndex = Mathf.Clamp(waveIndex, 0, waves.Length - 1);
            for (int i = 0; i < waveRowButtons.Count; i++)
                PaintBackground(waveRowButtons[i], i == waveIndex ? RowCurrentColor : RowColor);

            WaveData wave = waves[waveIndex];
            string name = wave != null && !string.IsNullOrEmpty(wave.waveName) ? wave.waveName : "(unnamed wave)";
            waveDetailTitle.text = $"WAVE {waveIndex + 1}  <color={DimHex}>{name}</color>";
            MeasureWave(wave, out int enemies, out int spawns, out float span);
            waveDetailSummary.text = $"{enemies} enemies, {spawns} {(spawns == 1 ? "spawn" : "spawns")}, {Mathf.CeilToInt(span)}s of spawning";
            if (builtRosterIndex != waveIndex)
            {
                builtRosterIndex = waveIndex;
                RebuildRoster(wave);
            }
            FitDropdown(Dropdown.Waves);
        }

        private void RefreshHazard()
        {
            bool on = StageCombatModifiers.HazardsEnabled;
            if (hazardButton != null)
            {
                Paint(hazardButton, on ? WarnFill : ControlColor);
                if (hazardIcon != null) hazardIcon.color = on ? GoldColor : TextDim;
            }
            if (mapHazardButton != null)
            {
                mapHazardButton.GetComponentInChildren<Text>().text = on ? "HAZARD ON" : "HAZARD OFF";
                Paint(mapHazardButton, on ? AccentFill : DangerColor);
            }
        }

        private void RefreshStats()
        {
            if (statsWaveText == null) return;

            int totalDamage = 0;
            foreach (KeyValuePair<OperatorBase, int> pair in damageByOperator) totalDamage += pair.Value;
            float seconds = Mathf.Max(1f, waveTime);

            statsWaveText.text =
                $"{waveSource}\n" +
                $"Time {FormatTime(waveTime)}   Kills {waveKills}\n" +
                $"Leaks {waveLeaks}" + (waveLeakLives > 0 ? $" (would cost {waveLeakLives} {(waveLeakLives == 1 ? "life" : "lives")})" : "") + "\n" +
                $"Damage {FormatNumber(totalDamage)} ({FormatNumber(Mathf.RoundToInt(totalDamage / seconds))}/s)";

            RefreshDamageMeter(seconds);
        }

        private void RefreshDamageMeter(float seconds)
        {
            damageRanking.Clear();
            foreach (KeyValuePair<OperatorBase, int> pair in damageByOperator)
                if (pair.Key != null) damageRanking.Add(pair);
            damageRanking.Sort((a, b) => b.Value.CompareTo(a.Value));

            if (damageRanking.Count == 0)
            {
                damageNamesText.text = "No damage dealt this wave.";
                damageValuesText.text = string.Empty;
                return;
            }

            var names = new StringBuilder();
            var values = new StringBuilder();
            int shown = Mathf.Min(DamageMeterRows, damageRanking.Count);
            for (int i = 0; i < shown; i++)
            {
                OperatorBase op = damageRanking[i].Key;
                if (i > 0) { names.Append('\n'); values.Append('\n'); }
                names.Append(i + 1).Append(". ").Append(op.Data != null ? op.Data.operatorName : op.name);
                values.Append(FormatNumber(damageRanking[i].Value)).Append("  ")
                    .Append(FormatNumber(Mathf.RoundToInt(damageRanking[i].Value / seconds))).Append("/s");
            }
            if (damageRanking.Count > shown) names.Append("\n+").Append(damageRanking.Count - shown).Append(" more");
            damageNamesText.text = names.ToString();
            damageValuesText.text = values.ToString();
        }

        private void ApplyEnemyFilter()
        {
            string search = enemySearch.Trim().ToLowerInvariant();
            int shown = 0;
            foreach (UnitTile tile in enemyTiles)
            {
                bool show = (enemyFilter < 0 || (int)tile.enemy.archetype == enemyFilter) &&
                            (search.Length == 0 || tile.searchName.Contains(search));
                tile.root.SetActive(show);
                if (show) shown++;
            }
            if (enemyEmptyText != null) enemyEmptyText.gameObject.SetActive(shown == 0);
            for (int i = 0; i < enemyFilterButtons.Count; i++)
                SetSelected(enemyFilterButtons[i], i - 1 == enemyFilter);
            FitDropdown(Dropdown.Enemies);
        }

        private void ApplyOperatorFilter()
        {
            string search = operatorSearch.Trim().ToLowerInvariant();
            int shown = 0;
            foreach (UnitTile tile in operatorTiles)
            {
                bool show = (operatorFilter < 0 || (int)tile.op.operatorClass == operatorFilter) &&
                            (search.Length == 0 || tile.searchName.Contains(search));
                tile.root.SetActive(show);
                if (show) shown++;
            }
            if (operatorEmptyText != null) operatorEmptyText.gameObject.SetActive(shown == 0);
            for (int i = 0; i < operatorFilterButtons.Count; i++)
                SetSelected(operatorFilterButtons[i], i - 1 == operatorFilter);
            FitDropdown(Dropdown.Operators);
        }

        private static void SetBadge(UnitTile tile, int count)
        {
            if (tile.badge == null) return;
            tile.badge.SetActive(count > 0);
            if (count > 0) tile.badgeText.text = "x" + count;
        }

        /// <summary>
        /// Sizes the open dropdown's list to its content, capped so the whole panel stays above the deck bar.
        /// Short lists give a short panel; long ones scroll.
        /// </summary>
        private void FitDropdown(Dropdown dropdown)
        {
            if (dropdown == Dropdown.None) return;
            GameObject panel = dropdownPanels[(int)dropdown];
            ScrollArea scroll = dropdownScrolls[(int)dropdown];
            if (panel == null || !panel.activeInHierarchy || scroll.content == null) return;

            RectTransform panelRect = (RectTransform)panel.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            float contentHeight = LayoutUtility.GetPreferredHeight(scroll.content);

            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            float chrome = layout.padding.vertical;
            int visible = 0;
            foreach (Transform child in panel.transform)
            {
                if (!child.gameObject.activeSelf) continue;
                LayoutElement element = child.GetComponent<LayoutElement>();
                if (element != null && element.ignoreLayout) continue;
                visible++;
                if (child.gameObject != scroll.page) chrome += LayoutUtility.GetPreferredHeight((RectTransform)child);
            }
            chrome += layout.spacing * Mathf.Max(0, visible - 1);

            float screenHeight = canvasRect != null ? canvasRect.rect.height : 1080f;
            float available = screenHeight - DropdownTop - DropdownBottom - chrome;
            scroll.element.preferredHeight = Mathf.Max(40f, Mathf.Min(contentHeight, available));
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        }

        // ============================
        // Enemy tooltip
        // ============================

        /// <summary>Picks the enemy under the mouse (not over UI, not while placing) and fills the tooltip.</summary>
        private void RefreshTooltip()
        {
            if (tooltip == null) return;
            bool wasShown = tooltip.gameObject.activeSelf;
            tooltipTarget = CanShowTooltip() ? FindHoveredEnemy() : null;
            tooltip.gameObject.SetActive(tooltipTarget != null);
            if (tooltipTarget == null) return;
            if (!wasShown) tooltip.SetAsLastSibling();

            EnemyBase enemy = tooltipTarget;
            EnemyData data = enemy.Data;
            tooltipTitle.text = data != null ? data.enemyName : enemy.name;

            float hpRatio = enemy.MaxHP > 0 ? Mathf.Clamp01((float)enemy.CurrentHP / enemy.MaxHP) : 0f;
            tooltipHpFill.anchorMax = new Vector2(hpRatio, 1f);
            tooltipHpFillImage.color = Color.Lerp(WaveColor, AccentColor, hpRatio);

            var body = new StringBuilder();
            if (data != null)
                body.Append(data.archetype).Append(", ").Append(data.movementType).Append(", ").Append(data.lifePointCost)
                    .Append(data.lifePointCost == 1 ? " life\n" : " lives\n");
            body.Append("HP  ").Append(enemy.CurrentHP).Append(" / ").Append(enemy.MaxHP).Append("  (").Append(Mathf.RoundToInt(hpRatio * 100f)).Append("%)\n");
            body.Append("ATK ").Append(enemy.CurrentATK).Append("  DEF ").Append(enemy.CurrentDEF);
            if (data != null)
            {
                int baseDef = data.GetScaledDEF(enemy.DifficultyLevel);
                if (enemy.CurrentDEF != baseDef)
                    body.Append(" <color=").Append(enemy.CurrentDEF > baseDef ? PrepHex : WaveHex).Append(">(").Append(baseDef).Append(")</color>");
            }
            body.Append("  RES ").Append(enemy.CurrentRES).Append('\n');
            body.Append("SPD ").Append(enemy.CurrentMoveSpeed.ToString("0.00", CultureInfo.InvariantCulture));
            if (data != null && !Mathf.Approximately(enemy.CurrentMoveSpeed, data.moveSpeed))
                body.Append(" <color=").Append(DimHex).Append(">(base ").Append(data.moveSpeed.ToString("0.00", CultureInfo.InvariantCulture)).Append(")</color>");

            var effects = new List<string>();
            if (enemy.IsBlocked) effects.Add(enemy.BlockingOperator != null && enemy.BlockingOperator.Data != null
                ? "Blocked by " + enemy.BlockingOperator.Data.operatorName : "Blocked");
            if (enemy.IsFrozen) effects.Add("Frozen");
            if (enemy.IsStunned) effects.Add("Stunned");
            if (enemy.IsSluggish) effects.Add("Sluggish");
            if (enemy.ChillAmount > 0f) effects.Add("Chilled");
            float aura = JunkernautEnemy.GetDamageReductionPercent(enemy);
            if (aura > 0f) effects.Add($"Junkernaut aura -{Mathf.RoundToInt(aura * 100f)}% dmg");
            if (effects.Count > 0) body.Append("\n<color=#FFC740>").Append(string.Join(", ", effects)).Append("</color>");

            tooltipBody.text = body.ToString();
            PositionTooltip();
        }

        private bool CanShowTooltip()
        {
            if (uiHidden || enemyManager == null || enemyManager.ActiveEnemyCount == 0) return false;
            if (gameplayHud != null && gameplayHud.IsPaused) return false;
            if (bootstrapper != null && bootstrapper.IsPlacementActive) return false;
            return EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject();
        }

        /// <summary>The live enemy within ~half a tile of the mouse, or null.</summary>
        private EnemyBase FindHoveredEnemy()
        {
            Camera cam = Camera.main;
            if (cam == null || Mouse.current == null) return null;

            Vector2 screen = CrtDisplay.ScreenToPicture(Mouse.current.position.ReadValue());
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
            float cell = bootstrapper != null && bootstrapper.gridManager != null ? bootstrapper.gridManager.CellSize : 1f;
            float bestDistance = (cell * 0.6f) * (cell * 0.6f);
            EnemyBase best = null;
            foreach (EnemyBase enemy in enemyManager.ActiveEnemies)
            {
                if (enemy == null || enemy.IsDead) continue;
                float distance = ((Vector2)(enemy.VisualPosition - world)).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = enemy;
                }
            }
            return best;
        }

        /// <summary>Keeps the tooltip beside its enemy (flipping left near the screen edge), inside the screen.</summary>
        private void PositionTooltip()
        {
            if (tooltip == null || !tooltip.gameObject.activeSelf) return;
            if (tooltipTarget == null || tooltipTarget.IsDead)
            {
                tooltipTarget = null;
                tooltip.gameObject.SetActive(false);
                return;
            }

            Camera cam = Camera.main;
            if (cam == null) return;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, tooltipTarget.VisualPosition);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(uiRoot, screen, uiCamera, out Vector2 local)) return;

            Rect bounds = uiRoot.rect;
            Vector2 size = tooltip.rect.size;
            float x = local.x + 26f;
            if (x + size.x > bounds.xMax - 8f) x = local.x - 26f - size.x;
            float y = Mathf.Clamp(local.y + 14f, bounds.yMin + 8f, bounds.yMax - 8f - size.y);
            tooltip.anchoredPosition = new Vector2(x, y);
        }

        // ============================
        // Hints
        // ============================

        private void AddHint(Component target, string text)
        {
            SandboxPointerEvents events = GetPointerEvents(target.gameObject);
            RectTransform rect = (RectTransform)target.transform;
            events.Entered += () => ShowHint(rect, text);
            events.Exited += HideHint;
        }

        private static SandboxPointerEvents GetPointerEvents(GameObject obj)
        {
            SandboxPointerEvents events = obj.GetComponent<SandboxPointerEvents>();
            return events != null ? events : obj.AddComponent<SandboxPointerEvents>();
        }

        /// <summary>Shows the hint right-aligned under the hovered control.</summary>
        private void ShowHint(RectTransform target, string text)
        {
            if (hintRect == null || target == null) return;
            hintText.text = text;
            hintRect.gameObject.SetActive(true);
            hintRect.SetAsLastSibling();
            target.GetWorldCorners(corners);
            hintRect.position = corners[3];
            hintRect.anchoredPosition += new Vector2(0f, -4f);
        }

        private void HideHint()
        {
            if (hintRect != null) hintRect.gameObject.SetActive(false);
        }

        // ============================
        // UI construction
        // ============================

        private void BuildUI()
        {
            GameObject root = MakeObject("SandboxUI", null);
            uiRoot = root.GetComponent<RectTransform>();
            if (!AttachToCanvas(uiRoot))
            {
                Destroy(root);
                uiRoot = null;
                return;
            }
            Stretch(uiRoot, 0f, 0f, 0f, 0f);
            Canvas canvas = root.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvas = canvas.rootCanvas;
                canvasRect = canvas.GetComponent<RectTransform>();
                uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            }

            BuildToolbar();
            BuildReadout();
            dropdownPanels[(int)Dropdown.Enemies] = BuildEnemiesDropdown();
            dropdownPanels[(int)Dropdown.Operators] = BuildOperatorsDropdown();
            dropdownPanels[(int)Dropdown.Waves] = BuildWavesDropdown();
            dropdownPanels[(int)Dropdown.Queue] = BuildQueueDropdown();
            dropdownPanels[(int)Dropdown.Map] = BuildMapDropdown();
            BuildStatsCard();
            BuildShowButton();
            BuildTooltip();
            BuildToast();
            BuildHint();

            ApplyEnemyFilter();
            ApplyOperatorFilter();
            ApplyVisibility();
        }

        private bool AttachToCanvas(RectTransform rect)
        {
            if (gameplayHud != null)
            {
                gameplayHud.AttachSandboxPanel(rect);
                return rect.parent != null;
            }

            Canvas canvas = CrtDisplay.FindGameCanvas();
            if (canvas == null) return false;
            rect.SetParent(canvas.transform, false);
            return true;
        }

        private void BuildToolbar()
        {
            toolbar = MakeObject("Toolbar", uiRoot);
            Place(toolbar.GetComponent<RectTransform>(), Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-ScreenMargin, -ToolbarTop), new Vector2(0f, ToolbarHeight));
            MakePanelBackground(toolbar, PanelTint);
            HorizontalLayoutGroup layout = toolbar.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = 3f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            toolbar.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            Sprite[] icons =
            {
                GameplayHUDUI.GetEnemyClassIconSprite(EnemyArchetype.Grunt),
                GameplayHUDUI.LoadIconSprite("icon_squad"),
                GameplayHUDUI.LoadIconSprite("icon_wave"),
                GameplayHUDUI.LoadIconSprite("icon_menu"),
                null
            };
            for (int i = 0; i < DropdownLabels.Length; i++)
            {
                Dropdown dropdown = (Dropdown)i;
                int widestLabel = dropdown == Dropdown.Queue ? "QUEUE 00".Length : DropdownLabels[i].Length;
                dropdownButtons[i] = MakeToolbarButton(toolbar.transform, icons[i], DropdownLabels[i], widestLabel);
                dropdownButtons[i].onClick.AddListener(() => ToggleDropdown(dropdown));
            }
            queueButtonText = dropdownButtons[(int)Dropdown.Queue].GetComponentInChildren<Text>();

            Image divider = MakeImage(toolbar.transform, "Divider", LineColor);
            Size(divider.gameObject, -1f, 1f, 0f);

            killAllButton = MakeToolbarIcon(toolbar.transform, "icon_skull", DangerColor, "Kill all enemies (the wave stays live)");
            killAllButton.onClick.AddListener(KillAllEnemies);

            GetHazardDescription(bootstrapper != null ? bootstrapper.EnvironmentStageId : null, out bool hasHazard);
            hazardButton = MakeToolbarIcon(toolbar.transform, "icon_warning", WarnFill, "Map hazard on/off");
            hazardIcon = hazardButton.transform.Find("Icon").GetComponent<Image>();
            hazardButton.onClick.AddListener(ToggleHazard);
            hazardButton.gameObject.SetActive(hasHazard);

            statsButton = MakeToolbarIcon(toolbar.transform, "icon_damage", ControlColor, "Wave stats and damage meter");
            statsButton.onClick.AddListener(ToggleStats);

            Button hide = MakeToolbarIcon(toolbar.transform, "icon_minus", ControlColor, "Hide the sandbox UI");
            hide.onClick.AddListener(() => SetUiHidden(true));
        }

        private void BuildReadout()
        {
            readout = MakeObject("Readout", uiRoot);
            Place(readout.GetComponent<RectTransform>(), Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-ScreenMargin, -ReadoutTop), new Vector2(0f, ReadoutHeight));
            Image background = readout.AddComponent<Image>();
            background.color = new Color(PanelTint.r, PanelTint.g, PanelTint.b, 0.82f);
            background.raycastTarget = false;
            HorizontalLayoutGroup layout = readout.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            readout.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            readoutText = MakeText(readout.transform, string.Empty, 11, TextColor, TextAnchor.MiddleRight);
            readoutText.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private void BuildToast()
        {
            toast = MakeObject("Toast", uiRoot);
            Place(toast.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -ToolbarTop), new Vector2(0f, 36f));
            MakePanelBackground(toast, PanelTint).raycastTarget = false;
            toastGroup = toast.AddComponent<CanvasGroup>();
            toastGroup.blocksRaycasts = false;
            HorizontalLayoutGroup layout = toast.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            toast.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            toastText = MakeText(toast.transform, string.Empty, 13, GoldColor, TextAnchor.MiddleCenter);
            toastText.horizontalOverflow = HorizontalWrapMode.Overflow;
            toast.SetActive(false);
        }

        private void BuildHint()
        {
            GameObject hint = MakeObject("Hint", uiRoot);
            hintRect = hint.GetComponent<RectTransform>();
            Place(hintRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.one, Vector2.zero, new Vector2(0f, 26f));
            Image background = hint.AddComponent<Image>();
            background.color = new Color(0.01f, 0.015f, 0.02f, 0.95f);
            background.raycastTarget = false;
            HorizontalLayoutGroup layout = hint.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            hint.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            hintText = MakeText(hint.transform, string.Empty, 11, TextColor, TextAnchor.MiddleCenter);
            hintText.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint.SetActive(false);
        }

        private void BuildShowButton()
        {
            Button button = MakeButton(uiRoot, "SANDBOX", ToolbarFontSize, AccentFill, TextColor);
            showButton = button.gameObject;
            Place(showButton.GetComponent<RectTransform>(), Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-ScreenMargin, -ToolbarTop), new Vector2(130f, ToolbarHeight - 6f));
            button.onClick.AddListener(() => SetUiHidden(false));
            AddHint(button, "Show the sandbox controls");
        }

        private void BuildStatsCard()
        {
            statsCard = MakeObject("StatsCard", uiRoot);
            Place(statsCard.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(ScreenMargin, -ToolbarTop), new Vector2(StatsCardWidth, 0f));
            MakePanelBackground(statsCard, new Color(PanelTint.r, PanelTint.g, PanelTint.b, 0.88f));
            Column(statsCard, new RectOffset(8, 8, 6, 8), 4f);
            statsCard.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Transform header = MakeStrip(statsCard.transform, 20f, 4f);
            Text title = MakeText(header, "WAVE STATS", 9, AccentColor, TextAnchor.MiddleLeft);
            Size(title.gameObject, -1f, -1f, 1f);
            Button close = MakeIconButton(header, "icon_close", ControlColor, 0f);
            close.onClick.AddListener(ToggleStats);

            statsWaveText = MakeText(statsCard.transform, string.Empty, 8, TextColor, TextAnchor.UpperLeft);

            MakeGroupHeader(statsCard.transform, "OPERATOR DAMAGE");
            damageNamesText = MakeText(statsCard.transform, string.Empty, 8, TextColor, TextAnchor.UpperLeft);
            // Values share the names' rect (right-aligned), so each line lines up with its operator
            damageValuesText = MakeText(damageNamesText.transform, string.Empty, 8, AccentColor, TextAnchor.UpperRight);
            Stretch(damageValuesText.rectTransform, 0f, 0f, 0f, 0f);
            MakeText(statsCard.transform, "Attack damage after DEF/RES and auras. Hazard and DoT damage isn't counted.", 7, TextDim, TextAnchor.UpperLeft);
        }

        private void BuildTooltip()
        {
            GameObject root = MakeObject("EnemyTooltip", uiRoot);
            tooltip = root.GetComponent<RectTransform>();
            Place(tooltip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Vector2(TooltipWidth, 0f));
            MakePanelBackground(root, new Color(PanelTint.r, PanelTint.g, PanelTint.b, 0.92f)).raycastTarget = false;
            Column(root, new RectOffset(7, 7, 5, 7), 3f);
            root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            tooltipTitle = MakeText(root.transform, string.Empty, 9, GoldColor, TextAnchor.MiddleLeft);
            FitText(tooltipTitle, 7);
            Size(tooltipTitle.gameObject, 13f);

            Image bar = MakeImage(root.transform, "HpBar", LineColor);
            Size(bar.gameObject, 4f);
            tooltipHpFillImage = MakeImage(bar.transform, "Fill", AccentColor);
            tooltipHpFill = tooltipHpFillImage.rectTransform;
            tooltipHpFill.anchorMin = Vector2.zero;
            tooltipHpFill.anchorMax = Vector2.one;
            tooltipHpFill.offsetMin = Vector2.zero;
            tooltipHpFill.offsetMax = Vector2.zero;

            tooltipBody = MakeText(root.transform, string.Empty, 8, TextColor, TextAnchor.UpperLeft);
            root.SetActive(false);
        }

        // ---- Dropdowns ----

        /// <summary>A dropdown panel under the toolbar's right edge; its height follows its content (see <see cref="FitDropdown"/>).</summary>
        private Transform MakeDropdownPanel(Dropdown dropdown, out GameObject panel)
        {
            panel = MakeObject(DropdownLabels[(int)dropdown] + "Dropdown", uiRoot);
            Place(panel.GetComponent<RectTransform>(), Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-ScreenMargin, -DropdownTop), new Vector2(DropdownWidths[(int)dropdown], 0f));
            MakePanelBackground(panel, PanelTint);
            Column(panel, new RectOffset(8, 8, 8, 8), 5f);
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Accent strip along the top edge (ignored by the layout)
            Image strip = MakeImage(panel.transform, "AccentStrip", AccentColor);
            strip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Place(strip.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(-12f, 2f));
            return panel.transform;
        }

        private GameObject BuildEnemiesDropdown()
        {
            Transform panel = MakeDropdownPanel(Dropdown.Enemies, out GameObject root);

            Transform top = MakeStrip(panel, 22f, 3f);
            MakeSearchField(top, "Search enemies...", enemySearch, value =>
            {
                enemySearch = value;
                ApplyEnemyFilter();
            });
            for (int i = 0; i < SpawnAmounts.Length; i++)
            {
                int index = i;
                amountButtons[i] = MakeButton(top, "x" + SpawnAmounts[i], 8, ControlColor, TextColor);
                Size(amountButtons[i].gameObject, -1f, 30f, 0f);
                amountButtons[i].onClick.AddListener(() => SetSpawnAmount(index));
                AddHint(amountButtons[i], $"Each click queues or spawns {SpawnAmounts[i]}");
            }

            Transform chips = MakeStrip(panel, 20f, 2f);
            AddFilterChip(chips, enemyFilterButtons, null, "ALL", "All classes", () => { enemyFilter = -1; ApplyEnemyFilter(); });
            foreach (EnemyArchetype archetype in System.Enum.GetValues(typeof(EnemyArchetype)))
            {
                int value = (int)archetype;
                AddFilterChip(chips, enemyFilterButtons, GameplayHUDUI.GetEnemyClassIconSprite(archetype), null, archetype.ToString(),
                    () => { enemyFilter = value; ApplyEnemyFilter(); });
            }

            enemyHintText = MakeText(panel, string.Empty, 8, PrepColor, TextAnchor.MiddleLeft);
            FitText(enemyHintText, 6);
            Size(enemyHintText.gameObject, 12f);

            ScrollArea scroll = MakeScrollArea(panel);
            dropdownScrolls[(int)Dropdown.Enemies] = scroll;
            Transform grid = MakeTileGrid(scroll.content);
            foreach (EnemyData enemy in availableEnemies)
            {
                Button tile = MakeTile(grid, GetEnemySprite(enemy), GameplayHUDUI.GetEnemyClassIconSprite(enemy), enemy.enemyName,
                    enemy.baseHP.ToString(CultureInfo.InvariantCulture) + " HP", TextDim, out GameObject badge, out Text badgeText);
                tile.onClick.AddListener(() => HandleEnemyClicked(enemy));
                GetPointerEvents(tile.gameObject).RightClicked = () => RemoveQueuedEnemy(enemy);
                enemyTiles.Add(new UnitTile
                {
                    enemy = enemy, root = tile.gameObject, badge = badge, badgeText = badgeText,
                    searchName = (enemy.enemyName ?? string.Empty).ToLowerInvariant()
                });
            }
            enemyEmptyText = MakeText(scroll.content, "No enemies match.", 8, TextDim, TextAnchor.MiddleCenter);
            return root;
        }

        private GameObject BuildOperatorsDropdown()
        {
            Transform panel = MakeDropdownPanel(Dropdown.Operators, out GameObject root);

            Transform top = MakeStrip(panel, 22f, 3f);
            MakeSearchField(top, "Search operators...", operatorSearch, value =>
            {
                operatorSearch = value;
                ApplyOperatorFilter();
            });
            deckCountText = MakeText(top, "DECK", 8, AccentColor, TextAnchor.MiddleCenter);
            FitText(deckCountText, 6);
            Size(deckCountText.gameObject, -1f, 58f, 0f);
            Button clear = MakeIconButton(top, "icon_discard", DangerColor, 0f);
            Size(clear.gameObject, -1f, 24f, 0f);
            clear.onClick.AddListener(ClearDeck);
            AddHint(clear, "Clear the deck");

            Transform chips = MakeStrip(panel, 20f, 2f);
            AddFilterChip(chips, operatorFilterButtons, null, "ALL", "All classes", () => { operatorFilter = -1; ApplyOperatorFilter(); });
            foreach (OperatorClass operatorClass in System.Enum.GetValues(typeof(OperatorClass)))
            {
                int value = (int)operatorClass;
                AddFilterChip(chips, operatorFilterButtons, GameplayHUDUI.GetClassIconSprite(operatorClass), null, operatorClass.ToString(),
                    () => { operatorFilter = value; ApplyOperatorFilter(); });
            }

            Text help = MakeText(panel, "Click adds a card at base rarity. No squad cap; retreats refund at once.", 8, TextDim, TextAnchor.MiddleLeft);
            FitText(help, 6);
            Size(help.gameObject, 12f);

            ScrollArea scroll = MakeScrollArea(panel);
            dropdownScrolls[(int)Dropdown.Operators] = scroll;
            Transform grid = MakeTileGrid(scroll.content);
            foreach (OperatorData op in operatorData)
            {
                Sprite classIcon = GameplayHUDUI.GetClassIconSprite(op.operatorClass);
                string stars = new string('*', Mathf.Clamp((int)op.baseRarity, 1, 5));
                Button tile = MakeTile(grid, op.portrait != null ? op.portrait : classIcon, classIcon, op.operatorName, stars, GoldColor,
                    out GameObject badge, out Text badgeText);
                tile.onClick.AddListener(() => AddOperator(op));
                operatorTiles.Add(new UnitTile
                {
                    op = op, root = tile.gameObject, badge = badge, badgeText = badgeText,
                    searchName = (op.operatorName ?? string.Empty).ToLowerInvariant()
                });
            }
            operatorEmptyText = MakeText(scroll.content, "No operators match.", 8, TextDim, TextAnchor.MiddleCenter);
            return root;
        }

        private GameObject BuildWavesDropdown()
        {
            Transform panel = MakeDropdownPanel(Dropdown.Waves, out GameObject root);

            Transform difficulties = MakeStrip(panel, 20f, 3f);
            StageDifficulty[] values = { StageDifficulty.Easy, StageDifficulty.Normal, StageDifficulty.Hard };
            for (int i = 0; i < values.Length; i++)
            {
                StageDifficulty difficulty = values[i];
                difficultyButtons[i] = MakeButton(difficulties, difficulty.ToString().ToUpperInvariant(), 8, ControlColor, TextColor);
                difficultyButtons[i].onClick.AddListener(() => SelectDifficulty(difficulty));
            }

            waveStageText = MakeText(panel, string.Empty, 8, TextColor, TextAnchor.MiddleLeft);
            FitText(waveStageText, 6);
            Size(waveStageText.gameObject, 12f);

            ScrollArea scroll = MakeScrollArea(panel);
            dropdownScrolls[(int)Dropdown.Waves] = scroll;
            waveRowsRoot = scroll.content;

            // Selected wave: roster and load buttons, always in view under the list
            GameObject detail = MakeObject("WaveDetail", panel);
            detail.AddComponent<Image>().color = SurfaceColor;
            Column(detail, new RectOffset(6, 6, 6, 6), 4f);

            waveDetailTitle = MakeText(detail.transform, string.Empty, 9, TextColor, TextAnchor.MiddleLeft);
            FitText(waveDetailTitle, 6);
            Size(waveDetailTitle.gameObject, 13f);

            GameObject roster = MakeObject("Roster", detail.transform);
            GridLayoutGroup rosterGrid = roster.AddComponent<GridLayoutGroup>();
            rosterGrid.cellSize = new Vector2(36f, 34f);
            rosterGrid.spacing = new Vector2(3f, 3f);
            rosterGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            rosterGrid.constraintCount = 8;
            rosterRoot = roster.transform;

            waveDetailSummary = MakeText(detail.transform, string.Empty, 8, TextDim, TextAnchor.MiddleLeft);
            FitText(waveDetailSummary, 6);
            Size(waveDetailSummary.gameObject, 12f);

            Transform buttons = MakeStrip(detail.transform, 24f, 4f);
            loadWaveButton = MakeButton(buttons, "LOAD", 9, AccentFill, TextColor);
            loadWaveButton.onClick.AddListener(() => LoadSelectedWave(true));
            AddHint(loadWaveButton, "Replace the queue with this wave");
            addWaveButton = MakeButton(buttons, "+ ADD", 9, ControlColor, TextColor);
            addWaveButton.onClick.AddListener(() => LoadSelectedWave(false));
            AddHint(addWaveButton, "Add this wave to the queue");
            return root;
        }

        private void RebuildWaveRows(WaveData[] waves)
        {
            builtWaves = waves;
            wavesBuilt = true;
            builtRosterIndex = -1;
            ClearChildren(waveRowsRoot);
            waveRowButtons.Clear();
            if (waves == null) return;

            for (int i = 0; i < waves.Length; i++)
            {
                int index = i;
                WaveData wave = waves[i];
                MeasureWave(wave, out int enemies, out _, out float span);
                bool spike = (i + 1) % 5 == 0 || i == waves.Length - 1;

                Button row = MakeButton(waveRowsRoot, null, 0, RowColor, TextColor);
                row.gameObject.name = "Wave " + (i + 1);
                Size(row.gameObject, ListRowHeight);

                Text number = MakeText(row.transform, (i + 1).ToString("00"), 9, spike ? GoldColor : TextColor, TextAnchor.MiddleLeft);
                Place(number.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(24f, 0f));
                string waveName = wave != null && !string.IsNullOrEmpty(wave.waveName) ? wave.waveName : "-";
                Text name = MakeText(row.transform, waveName, 8, TextDim, TextAnchor.MiddleLeft);
                FitText(name, 6);
                Stretch(name.rectTransform, 34f, 0f, 92f, 0f);
                Text info = MakeText(row.transform, $"{enemies} foes  {Mathf.CeilToInt(span)}s", 8, TextColor, TextAnchor.MiddleRight);
                FitText(info, 6);
                Place(info.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(84f, 0f));

                row.onClick.AddListener(() => SelectWave(index));
                waveRowButtons.Add(row);
            }
        }

        /// <summary>The selected wave's enemies, one cell per enemy type with its total count.</summary>
        private void RebuildRoster(WaveData wave)
        {
            ClearChildren(rosterRoot);
            if (wave?.entries == null) return;

            var counts = new Dictionary<EnemyData, int>();
            var order = new List<EnemyData>();
            foreach (WaveEntry entry in wave.entries)
            {
                if (entry == null || entry.enemyData == null || entry.count <= 0) continue;
                if (!counts.ContainsKey(entry.enemyData))
                {
                    counts[entry.enemyData] = 0;
                    order.Add(entry.enemyData);
                }
                counts[entry.enemyData] += entry.count;
            }

            foreach (EnemyData enemy in order)
            {
                GameObject cell = MakeObject(enemy.enemyName, rosterRoot);
                cell.AddComponent<Image>().color = RowColor;
                Image icon = MakeImage(cell.transform, "Icon", Color.white);
                icon.sprite = GetEnemySprite(enemy);
                icon.preserveAspect = true;
                Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(24f, 20f));
                Text count = MakeText(cell.transform, "x" + counts[enemy], 7, GoldColor, TextAnchor.LowerCenter);
                Stretch(count.rectTransform, 0f, 0f, 0f, 2f);
                AddHint(cell.GetComponent<Image>(), enemy.enemyName);
            }
        }

        private GameObject BuildQueueDropdown()
        {
            Transform panel = MakeDropdownPanel(Dropdown.Queue, out GameObject root);

            Transform header = MakeStrip(panel, 22f, 3f);
            queueTitleText = MakeText(header, "QUEUE", 9, TextColor, TextAnchor.MiddleLeft);
            FitText(queueTitleText, 6);
            Size(queueTitleText.gameObject, -1f, -1f, 1f);
            keepQueueButton = MakeIconButton(header, "icon_reroll", ControlColor, 0f);
            Size(keepQueueButton.gameObject, -1f, 24f, 0f);
            keepQueueButton.onClick.AddListener(ToggleKeepQueue);
            AddHint(keepQueueButton, "Keep the queue after START (replay the wave)");
            Button clear = MakeIconButton(header, "icon_discard", DangerColor, 0f);
            Size(clear.gameObject, -1f, 24f, 0f);
            clear.onClick.AddListener(ClearQueue);
            AddHint(clear, "Clear the queue");

            ScrollArea scroll = MakeScrollArea(panel);
            dropdownScrolls[(int)Dropdown.Queue] = scroll;
            GameObject rows = MakeObject("QueueRows", scroll.content);
            Column(rows, new RectOffset(0, 0, 0, 0), 3f);
            queueRowsRoot = rows.transform;
            queueEmptyText = MakeText(scroll.content, "Empty. Add enemies from ENEMIES or load one from WAVES.", 8, TextDim, TextAnchor.MiddleLeft);

            Text help = MakeText(panel, "Hand-queued: 1 per second, top to bottom. Wave lines keep their own timing and run alongside.", 7, TextDim, TextAnchor.UpperLeft);
            help.color = TextDim * new Color(1f, 1f, 1f, 0.85f);
            return root;
        }

        /// <summary>One queue line: icon, name over its timing, count stepper, reorder (hand-queued lines only) and remove.</summary>
        private void MakeQueueRow(Transform parent, SandboxSpawnGroup group, bool isFirstUntimed, bool isLastUntimed)
        {
            GameObject row = MakeObject("QueueRow", parent);
            row.AddComponent<Image>().color = group.timed ? RowColor : UntimedRowColor;
            Size(row, QueueRowHeight);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 5, 5);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            Image icon = MakeImage(row.transform, "Icon", Color.white);
            icon.sprite = GetEnemySprite(group.enemy);
            icon.preserveAspect = true;
            Size(icon.gameObject, -1f, 20f, 0f);

            GameObject labels = MakeObject("Labels", row.transform);
            Size(labels, -1f, -1f, 1f);
            Text name = MakeText(labels.transform, group.enemy.enemyName, 8, TextColor, TextAnchor.UpperLeft);
            FitText(name, 6);
            Place(name.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, 1f), new Vector2(2f, 0f), new Vector2(-2f, 10f));
            string spawn = group.spawnPoint >= 0 ? "Spawn " + (group.spawnPoint + 1) : "Any spawn";
            string timing = group.timed
                ? $"{spawn}, at {FormatSeconds(group.startDelay)}" + (group.count > 1 ? $", every {FormatSeconds(group.interval)}" : "")
                : "In order, 1 per second";
            Text detail = MakeText(labels.transform, timing, 7, TextDim, TextAnchor.LowerLeft);
            FitText(detail, 5);
            Place(detail.rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(2f, 0f), new Vector2(-2f, 9f));

            MakeIconButton(row.transform, "icon_minus", ControlColor, 0f).onClick.AddListener(() => ChangeGroupCount(group, -1));
            Text count = MakeText(row.transform, "x" + group.count, 9, GoldColor, TextAnchor.MiddleCenter);
            Size(count.gameObject, -1f, 26f, 0f);
            MakeIconButton(row.transform, "icon_plus", ControlColor, 0f).onClick.AddListener(() => ChangeGroupCount(group, 1));

            if (!group.timed)
            {
                Button up = MakeIconButton(row.transform, "icon_play", ControlColor, 90f);
                up.interactable = !isFirstUntimed;
                up.onClick.AddListener(() => MoveGroup(group, -1));
                Button down = MakeIconButton(row.transform, "icon_play", ControlColor, -90f);
                down.interactable = !isLastUntimed;
                down.onClick.AddListener(() => MoveGroup(group, 1));
            }
            MakeIconButton(row.transform, "icon_close", DangerColor, 0f).onClick.AddListener(() => RemoveGroup(group));
        }

        private GameObject BuildMapDropdown()
        {
            Transform panel = MakeDropdownPanel(Dropdown.Map, out GameObject root);

            string description = GetHazardDescription(bootstrapper != null ? bootstrapper.EnvironmentStageId : null, out bool hasHazard);
            int split = description.IndexOf('\n');
            GameObject hazard = MakeObject("Hazard", panel);
            hazard.AddComponent<Image>().color = SurfaceColor;
            Column(hazard, new RectOffset(6, 6, 6, 6), 3f);
            MakeText(hazard.transform, split >= 0 ? description.Substring(0, split) : description, 9, GoldColor, TextAnchor.UpperLeft);
            if (split >= 0) MakeText(hazard.transform, description.Substring(split + 1), 8, TextDim, TextAnchor.UpperLeft);
            if (hasHazard)
            {
                mapHazardButton = MakeButton(hazard.transform, "HAZARD ON", 8, AccentFill, TextColor);
                Size(mapHazardButton.gameObject, 22f);
                mapHazardButton.onClick.AddListener(ToggleHazard);
                AddHint(mapHazardButton, "OFF keeps the visuals but stops damage, debuffs and spawns");
            }

            Text note = MakeText(panel, "Switching map restarts the sandbox: deck, queue and operators reset.", 7, TextDim, TextAnchor.UpperLeft);
            note.color = TextDim;

            ScrollArea scroll = MakeScrollArea(panel);
            dropdownScrolls[(int)Dropdown.Map] = scroll;
            foreach (StageData stage in mapStages)
            {
                bool isCurrent = stage == currentMap;
                Button row = MakeListRow(scroll.content, null, GetMapLabel(stage), isCurrent ? "CURRENT" : string.Empty, AccentColor,
                    isCurrent ? RowCurrentColor : RowColor);
                row.onClick.AddListener(() => SwitchMap(stage));
            }
            return root;
        }

        // ============================
        // UI building blocks
        // ============================

        private static Sprite GetEnemySprite(EnemyData enemy)
        {
            if (enemy == null) return null;
            if (enemy.sprite != null) return enemy.sprite;
            if (enemy.animationFrames != null && enemy.animationFrames.Length > 0 && enemy.animationFrames[0] != null)
                return enemy.animationFrames[0];
            return GameplayHUDUI.GetEnemyClassIconSprite(enemy);
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        private static GameObject MakeObject(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            if (parent != null) obj.transform.SetParent(parent, false);
            return obj;
        }

        private static Image MakeImage(Transform parent, string name, Color color)
        {
            Image image = MakeObject(name, parent).AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>The kit's dark panel frame, tinted; blocks clicks to the map under it.</summary>
        private static Image MakePanelBackground(GameObject obj, Color tint)
        {
            Image image = obj.AddComponent<Image>();
            image.color = tint;
            GameplayHUDUI.ApplyPanelContainer(obj, GameplayHUDUI.PanelContainerType.Dark, new Color(1f, 1f, 1f, tint.a));
            return image;
        }

        private static void Column(GameObject obj, RectOffset padding, float spacing)
        {
            VerticalLayoutGroup layout = obj.GetComponent<VerticalLayoutGroup>();
            if (layout == null) layout = obj.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        /// <summary>Horizontal strip; buttons share its width equally unless <see cref="Size"/> gives them a fixed width.</summary>
        private static Transform MakeStrip(Transform parent, float height, float spacing)
        {
            GameObject strip = MakeObject("Strip", parent);
            Size(strip, height);
            HorizontalLayoutGroup layout = strip.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            return strip.transform;
        }

        /// <summary>Sets a layout size; -1 leaves a value unset.</summary>
        private static LayoutElement Size(GameObject obj, float height, float width = -1f, float flexibleWidth = -1f)
        {
            LayoutElement element = obj.GetComponent<LayoutElement>();
            if (element == null) element = obj.AddComponent<LayoutElement>();
            if (height >= 0f) element.preferredHeight = height;
            if (width >= 0f) { element.preferredWidth = width; element.minWidth = width; }
            if (flexibleWidth >= 0f) element.flexibleWidth = flexibleWidth;
            return element;
        }

        /// <summary>A scrolling list with a slim scrollbar. Its height is set by <see cref="FitDropdown"/>.</summary>
        private static ScrollArea MakeScrollArea(Transform parent)
        {
            GameObject page = MakeObject("Scroll", parent);
            page.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            LayoutElement element = page.AddComponent<LayoutElement>();
            element.preferredHeight = 120f;

            GameObject viewport = MakeObject("Viewport", page.transform);
            viewport.AddComponent<RectMask2D>();
            Stretch(viewport.GetComponent<RectTransform>(), 0f, 0f, 8f, 0f);

            GameObject content = MakeObject("Content", viewport.transform);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            Column(content, new RectOffset(4, 2, 4, 4), 3f);
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Image track = MakeImage(page.transform, "Scrollbar", new Color(1f, 1f, 1f, 0.05f));
            track.raycastTarget = true;
            Place(track.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-2f, 0f), new Vector2(4f, -8f));
            Image handle = MakeImage(track.transform, "Handle", LineColor);
            handle.raycastTarget = true;
            Stretch(handle.rectTransform, 0f, 0f, 0f, 0f);
            Scrollbar scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.targetGraphic = handle;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };

            ScrollRect scroll = page.AddComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            return new ScrollArea { page = page, content = contentRect, element = element };
        }

        private static Transform MakeTileGrid(Transform parent)
        {
            GameObject grid = MakeObject("Grid", parent);
            GridLayoutGroup layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = TileSize;
            layout.spacing = new Vector2(3f, 3f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = GridColumns;
            layout.childAlignment = TextAnchor.UpperCenter;
            return grid.transform;
        }

        /// <summary>
        /// A unit tile: artwork, class icon (top left), a count badge (top right, hidden at 0), name and a subtitle.
        /// </summary>
        private static Button MakeTile(Transform parent, Sprite art, Sprite classIcon, string label, string subtitle, Color subtitleColor,
            out GameObject badge, out Text badgeText)
        {
            Button tile = MakeButton(parent, null, 0, RowColor, TextColor);
            tile.gameObject.name = label;

            Image artwork = MakeImage(tile.transform, "Art", Color.white);
            artwork.sprite = art;
            artwork.preserveAspect = true;
            artwork.enabled = art != null;
            Stretch(artwork.rectTransform, 8f, 8f, 8f, 26f);

            if (classIcon != null)
            {
                Image icon = MakeImage(tile.transform, "Class", Color.white);
                icon.sprite = classIcon;
                icon.preserveAspect = true;
                Place(icon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(3f, -3f), new Vector2(12f, 12f));
            }

            Text name = MakeText(tile.transform, label, 7, TextColor, TextAnchor.LowerCenter);
            FitText(name, 5);
            Place(name.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(-4f, 10f));
            Text sub = MakeText(tile.transform, subtitle, 7, subtitleColor, TextAnchor.LowerCenter);
            FitText(sub, 5);
            Place(sub.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 3f), new Vector2(-4f, 9f));

            Image badgeImage = MakeImage(tile.transform, "Badge", GoldColor);
            Place(badgeImage.rectTransform, Vector2.one, Vector2.one, Vector2.one, new Vector2(-2f, -2f), new Vector2(24f, 12f));
            badgeText = MakeText(badgeImage.transform, string.Empty, 7, BadgeTextColor, TextAnchor.MiddleCenter);
            FitText(badgeText, 5);
            Stretch(badgeText.rectTransform, 1f, 0f, 1f, 0f);
            badge = badgeImage.gameObject;
            badge.SetActive(false);
            return tile;
        }

        private static InputField MakeSearchField(Transform parent, string placeholder, string value, UnityAction<string> onChanged)
        {
            GameObject obj = MakeObject("Search", parent);
            Image background = obj.AddComponent<Image>();
            background.color = RowColor;
            Size(obj, -1f, -1f, 1f);

            Text text = MakeText(obj.transform, string.Empty, 9, TextColor, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            text.lineSpacing = 1f;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(text.rectTransform, 6f, 2f, 6f, 2f);
            Text hint = MakeText(obj.transform, placeholder, 8, TextDim, TextAnchor.MiddleLeft);
            Stretch(hint.rectTransform, 6f, 2f, 6f, 2f);

            InputField field = obj.AddComponent<InputField>();
            field.targetGraphic = background;
            field.textComponent = text;
            field.placeholder = hint;
            field.lineType = InputField.LineType.SingleLine;
            field.customCaretColor = true;
            field.caretColor = AccentColor;
            field.selectionColor = new Color(AccentFill.r, AccentFill.g, AccentFill.b, 0.6f);
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.text = value ?? string.Empty;
            field.onValueChanged.AddListener(onChanged);
            return field;
        }

        private void AddFilterChip(Transform parent, List<Button> chips, Sprite icon, string label, string hint, UnityAction onClick)
        {
            Button chip = MakeButton(parent, label, 7, ControlColor, TextColor);
            if (icon != null)
            {
                Image image = MakeImage(chip.transform, "Icon", Color.white);
                image.sprite = icon;
                image.preserveAspect = true;
                Stretch(image.rectTransform, 2f, 3f, 2f, 3f);
            }
            chip.onClick.AddListener(onClick);
            AddHint(chip, hint);
            chips.Add(chip);
        }

        /// <summary>Icon plus label, wide enough for <paramref name="widestLabel"/> characters.</summary>
        private static Button MakeToolbarButton(Transform parent, Sprite icon, string label, int widestLabel)
        {
            Button button = MakeButton(parent, null, 0, ControlColor, TextColor);
            button.gameObject.name = label;

            float left = 10f;
            if (icon != null)
            {
                Image image = MakeImage(button.transform, "Icon", Color.white);
                image.sprite = icon;
                image.preserveAspect = true;
                Place(image.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f),
                    new Vector2(ToolbarIconSize, ToolbarIconSize));
                left = 8f + ToolbarIconSize + 8f;
            }
            Size(button.gameObject, -1f, left + widestLabel * ToolbarFontSize + 10f, 0f);
            Text text = MakeText(button.transform, label, ToolbarFontSize, TextColor, TextAnchor.MiddleLeft);
            FitText(text, 10);
            Stretch(text.rectTransform, left, 0f, 6f, 0f);
            return button;
        }

        private Button MakeToolbarIcon(Transform parent, string iconName, Color baseColor, string hint)
        {
            Button button = MakeIconButton(parent, iconName, baseColor, 0f);
            Size(button.gameObject, -1f, ToolbarHeight - 6f, 0f);
            Stretch(button.transform.Find("Icon").GetComponent<RectTransform>(), 9f, 9f, 9f, 9f);
            AddHint(button, hint);
            return button;
        }

        /// <summary>Small caps label with a hairline under it, separating groups in a list.</summary>
        private static void MakeGroupHeader(Transform parent, string label)
        {
            GameObject header = MakeObject("Group " + label, parent);
            Size(header, 18f);
            Text text = MakeText(header.transform, label, 8, TextDim, TextAnchor.LowerLeft);
            Stretch(text.rectTransform, 0f, 0f, 0f, 4f);
            Image line = MakeImage(header.transform, "Line", LineColor);
            Place(line.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 1f));
        }

        /// <summary>Clickable list row: optional icon, label, and a right-aligned tag.</summary>
        private static Button MakeListRow(Transform parent, Sprite icon, string label, string tag, Color tagColor, Color background)
        {
            Button button = MakeButton(parent, null, 0, background, TextColor);
            button.gameObject.name = label;
            Size(button.gameObject, ListRowHeight);

            float left = 8f;
            if (icon != null)
            {
                Image image = MakeImage(button.transform, "Icon", Color.white);
                image.sprite = icon;
                image.preserveAspect = true;
                Place(image.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(16f, 16f));
                left = 28f;
            }

            const float tagWidth = 70f;
            Text text = MakeText(button.transform, label, 9, TextColor, TextAnchor.MiddleLeft);
            FitText(text, 6);
            Stretch(text.rectTransform, left, 0f, tagWidth + 6f, 0f);

            Text tagText = MakeText(button.transform, tag, 8, tagColor, TextAnchor.MiddleRight);
            FitText(tagText, 6);
            Place(tagText.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(tagWidth, 0f));
            return button;
        }

        private static Button MakeButton(Transform parent, string label, int fontSize, Color baseColor, Color textColor)
        {
            GameObject obj = MakeObject(string.IsNullOrEmpty(label) ? "Button" : label, parent);
            Image image = obj.AddComponent<Image>();
            image.color = Color.white;
            Button button = obj.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            // A selected button would fire again on Enter/Space, which also confirm placements
            button.onClick.AddListener(ClearUiSelection);
            PaintBackground(button, baseColor);
            Size(obj, -1f, -1f, 1f);

            if (!string.IsNullOrEmpty(label))
            {
                Text text = MakeText(obj.transform, label, fontSize, textColor, TextAnchor.MiddleCenter);
                FitText(text, Mathf.Max(5, fontSize - 3));
                Stretch(text.rectTransform, 3f, 0f, 3f, 0f);
            }
            return button;
        }

        private static void ClearUiSelection()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private static Button MakeIconButton(Transform parent, string iconName, Color baseColor, float rotation)
        {
            Button button = MakeButton(parent, null, 0, baseColor, TextColor);
            Size(button.gameObject, -1f, SmallButtonSize, 0f);
            Image icon = MakeImage(button.transform, "Icon", TextColor);
            icon.sprite = GameplayHUDUI.LoadIconSprite(iconName);
            icon.preserveAspect = true;
            Stretch(icon.rectTransform, 4f, 4f, 4f, 4f);
            icon.rectTransform.localEulerAngles = new Vector3(0f, 0f, rotation);
            return button;
        }

        /// <summary>Colours a button through its ColorBlock so hover, press and disabled states all derive from one base.</summary>
        private static void PaintBackground(Button button, Color baseColor)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.14f);
            colors.pressedColor = Color.Lerp(baseColor, Color.black, 0.25f);
            colors.selectedColor = baseColor;
            colors.disabledColor = Color.Lerp(baseColor, PanelTint, 0.6f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.06f;
            button.colors = colors;
        }

        /// <summary><see cref="PaintBackground"/> plus the label colour (dim when disabled).</summary>
        private static void Paint(Button button, Color baseColor)
        {
            PaintBackground(button, baseColor);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = button.interactable ? TextColor : TextDim;
        }

        /// <summary>Difficulty, amount and filter chips: the selected one is filled with the accent.</summary>
        private static void SetSelected(Button button, bool selected)
        {
            if (button == null) return;
            PaintBackground(button, selected ? AccentFill : ControlColor);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = selected ? TextColor : TextDim;
        }

        private static Text MakeText(Transform parent, string value, int size, Color color, TextAnchor alignment)
        {
            Text text = MakeObject("Text", parent).AddComponent<Text>();
            text.text = value;
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.lineSpacing = 1.35f;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Single-line text that shrinks (down to <paramref name="minSize"/>) instead of wrapping or spilling.</summary>
        private static void FitText(Text text, int minSize)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = minSize;
            text.resizeTextMaxSize = text.fontSize;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
