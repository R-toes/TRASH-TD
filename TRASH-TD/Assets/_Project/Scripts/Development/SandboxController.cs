using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
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
    /// Development-only controls for the SANDBOX stage. This component is intentionally
    /// enabled only for the explicitly marked sandbox stage.
    ///
    /// This file holds the sandbox state and actions; SandboxController.UI.cs builds the UI. The UI keeps the map clear:
    /// a slim toolbar at the top right opens one dropdown at a time.
    /// ENEMIES:   before a wave, clicking an enemy queues it (x1/x5/x10); during a wave it spawns at once. Right-click removes.
    /// OPERATORS: adds operator cards to the deck at base rarity.
    /// WAVES:     loads a real wave of this map's stage (any difficulty) with its own timing and spawn points.
    /// QUEUE:     edits the queue (counts, order, remove) and whether it stays after START.
    /// MAP:       turns the map's hazard on/off and restarts the sandbox on another stage's map.
    /// Toolbar icons kill all enemies, toggle the hazard, show the stats card and hide the sandbox UI.
    /// The HUD's START/END WAVE button runs the waves (<see cref="ToggleWave"/>). Hovering an enemy shows its live stats.
    /// </summary>
    [DefaultExecutionOrder(-100)] // before StageBootstrapper, so Esc closes an open dropdown before it reaches the pause menu
    public sealed partial class SandboxController : MonoBehaviour
    {
        private const float RefreshSeconds = 0.1f;
        private const int MaxGroupCount = 99;
        /// <summary>Wave number hand-built waves report to wave-start hazards: they count as a late wave.</summary>
        private const int CustomWaveHazardNumber = 99;
        private static readonly int[] SpawnAmounts = { 1, 5, 10 };

        // Kept across the scene reload a map switch does
        private static StageDifficulty waveDifficulty = StageDifficulty.Hard;
        private static int waveIndex;
        private static bool keepQueueAfterStart = true;
        private static int spawnAmountIndex;

        private readonly List<SandboxSpawnGroup> queue = new List<SandboxSpawnGroup>();
        private readonly List<EnemyData> availableEnemies = new List<EnemyData>();
        private readonly List<OperatorData> operatorData = new List<OperatorData>();
        private readonly List<StageData> mapStages = new List<StageData>();
        private readonly Dictionary<OperatorBase, int> damageByOperator = new Dictionary<OperatorBase, int>();
        private readonly List<KeyValuePair<OperatorBase, int>> damageRanking = new List<KeyValuePair<OperatorBase, int>>();

        public IReadOnlyList<EnemyData> AvailableEnemies => availableEnemies;
        public IReadOnlyList<OperatorData> OperatorDataList => operatorData;

        private StageBootstrapper bootstrapper;
        private WaveManager waveManager;
        private GameManager gameManager;
        private EnemyManager enemyManager;
        private PlayerDeck playerDeck;
        private GameplayHUDUI gameplayHud;
        private StageData currentMap;

        // Stage wave currently in the queue (0 when none)
        private int loadedWaveNumber;
        private string loadedWaveLabel;

        // Running wave
        private bool lastWaveState;
        private bool isClearingEnemies;
        private float waveTime;
        private bool waveClearReported;
        private int waveKills;
        private int waveLeaks;
        private int waveLeakLives;
        private string waveSource = "No wave yet";
        private float refreshTimer;

        public void Initialize(StageBootstrapper stage)
        {
            bootstrapper = stage;
            waveManager = stage.waveManager;
            gameManager = stage.gameManager;
            enemyManager = stage.enemyManager != null ? stage.enemyManager : EnemyManager.Instance;
            playerDeck = stage.playerDeck;
            gameplayHud = FindAnyObjectByType<GameplayHUDUI>();
            gameplayHud?.RegisterSandboxController(this);
            LoadEnemyData();
            LoadOperatorData();
            LoadMapStages();
            if (gameManager != null && gameManager.CurrentPhase == StagePhase.CardPick)
                gameManager.EnterPreparationPhase();
            BuildUI();

            if (playerDeck != null) playerDeck.OnDeckChanged += HandleDeckChanged;
            if (enemyManager != null)
            {
                enemyManager.OnEnemyDied += HandleEnemyDied;
                enemyManager.OnEnemyReachedExit += HandleEnemyLeaked;
            }
            EnemyBase.OperatorDamageDealt += HandleOperatorDamage;
            RefreshAll();
            SetStatus("Sandbox ready. Queue enemies (ENEMIES or WAVES), then press START WAVE.");
        }

        private void OnDestroy()
        {
            if (playerDeck != null) playerDeck.OnDeckChanged -= HandleDeckChanged;
            if (enemyManager != null)
            {
                enemyManager.OnEnemyDied -= HandleEnemyDied;
                enemyManager.OnEnemyReachedExit -= HandleEnemyLeaked;
            }
            EnemyBase.OperatorDamageDealt -= HandleOperatorDamage;
        }

        private void Update()
        {
            bool waveActive = IsWaveActive;
            if (waveActive != lastWaveState) RefreshAll();
            if (waveActive) TrackWaveClock();
            HandleEscape();
            UpdateUiFrame();

            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f) return;
            refreshTimer = RefreshSeconds;
            RefreshLive();
            RefreshTooltip();
            if (statsVisible && !uiHidden) RefreshStats();
        }

        private bool IsWaveActive => waveManager != null && waveManager.IsWaveInProgress;

        private int SpawnAmount => SpawnAmounts[Mathf.Clamp(spawnAmountIndex, 0, SpawnAmounts.Length - 1)];

        /// <summary>
        /// The wave clock runs while enemies are on the field or still to spawn, so TIME reads as time-to-clear
        /// even though a sandbox wave stays live until END WAVE. The first clear is reported in a toast.
        /// </summary>
        private void TrackWaveClock()
        {
            bool busy = waveManager.PendingSpawnCount > 0 || (enemyManager != null && enemyManager.ActiveEnemyCount > 0);
            if (busy)
            {
                waveTime += Time.deltaTime;
                return;
            }

            if (!waveClearReported && waveKills + waveLeaks > 0)
            {
                waveClearReported = true;
                SetStatus($"Wave cleared in {FormatTime(waveTime)}: {waveKills} kills, {waveLeaks} leaks." +
                    (waveLeakLives > 0 ? $" Would cost {waveLeakLives} {(waveLeakLives == 1 ? "life" : "lives")}." : ""), waveLeaks > 0);
            }
        }

        // ============================
        // Data
        // ============================

        private void LoadOperatorData()
        {
            operatorData.Clear();
            // Every operator in the project; the registry is saved into builds, so Development Builds get them all too
            operatorData.AddRange(UnitIntelRegistry.GetOperators());
            if (operatorData.Count == 0 && bootstrapper != null && bootstrapper.operatorPool != null)
                operatorData.AddRange(bootstrapper.operatorPool);

            SortOperators(operatorData);
        }

        public static void SortOperators(List<OperatorData> list)
        {
            if (list == null) return;
            list.RemoveAll(op => op == null);
            list.Sort((a, b) =>
            {
                if (ReferenceEquals(a, b)) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                int byClass = a.operatorClass.CompareTo(b.operatorClass);
                if (byClass != 0) return byClass;
                int byRarity = ((int)a.baseRarity).CompareTo((int)b.baseRarity);
                if (byRarity != 0) return byRarity;
                return string.Compare(a.operatorName, b.operatorName, System.StringComparison.OrdinalIgnoreCase);
            });
        }

        private void LoadEnemyData()
        {
            availableEnemies.Clear();
            // Every enemy in the project (see LoadOperatorData)
            availableEnemies.AddRange(UnitIntelRegistry.GetEnemies());
            if (availableEnemies.Count == 0 && bootstrapper != null && bootstrapper.stageData != null)
            {
                AddEnemiesFromWaves(bootstrapper.stageData.wavesEasy);
                AddEnemiesFromWaves(bootstrapper.stageData.wavesNormal);
                AddEnemiesFromWaves(bootstrapper.stageData.wavesHard);
            }

            SortEnemies(availableEnemies);
        }

        public static void SortEnemies(List<EnemyData> list)
        {
            if (list == null) return;
            list.RemoveAll(enemy => enemy == null);
            // Enemies sorted by class (archetype), then alphabetical within each class.
            list.Sort((a, b) =>
            {
                if (ReferenceEquals(a, b)) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                int byClass = a.archetype.CompareTo(b.archetype);
                if (byClass != 0) return byClass;
                return string.Compare(a.enemyName, b.enemyName, System.StringComparison.OrdinalIgnoreCase);
            });
        }

        private void LoadMapStages()
        {
            mapStages.Clear();
            foreach (StageData stage in Resources.LoadAll<StageData>("Stages"))
            {
                if (stage != null && stage.stageId != "SANDBOX" && stage.gridWidth > 0 && stage.gridHeight > 0)
                    mapStages.Add(stage);
            }
            mapStages.Sort((a, b) => string.Compare(a.stageId, b.stageId, System.StringComparison.OrdinalIgnoreCase));
            currentMap = mapStages.Find(stage => stage.stageId == StageBootstrapper.SandboxMapStageId);
        }

        private static string GetStageNumber(StageData stage) =>
            stage.stageId.StartsWith("STAGE_") ? stage.stageId.Substring(6).TrimStart('0') : stage.stageId;

        private static string GetMapLabel(StageData stage)
        {
            string number = GetStageNumber(stage);
            return string.IsNullOrEmpty(stage.mapName) ? $"Stage {number}" : $"{number}. {stage.mapName}";
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

        private WaveData[] GetMapWaves() => currentMap != null ? currentMap.GetWaves(waveDifficulty) : null;

        /// <summary>Enemy count, distinct spawn points and seconds of spawning in a stage wave.</summary>
        private static void MeasureWave(WaveData wave, out int enemies, out int spawns, out float span)
        {
            enemies = 0;
            span = 0f;
            var spawnPoints = new HashSet<int>();
            if (wave?.entries != null)
            {
                foreach (WaveEntry entry in wave.entries)
                {
                    if (entry == null || entry.enemyData == null || entry.count <= 0) continue;
                    enemies += entry.count;
                    spawnPoints.Add(entry.spawnPointIndex);
                    span = Mathf.Max(span, entry.startDelay + (entry.count - 1) * Mathf.Max(0f, entry.spawnInterval));
                }
            }
            spawns = spawnPoints.Count;
        }

        /// <summary>What the map's environment does, and whether it has a gameplay hazard at all.</summary>
        private static string GetHazardDescription(string stageId, out bool hasHazard)
        {
            hasHazard = true;
            switch (stageId)
            {
                case "STAGE_02":
                    return "FALLING ITEMS\nScrap drops on operators and tiles during waves: true damage, some stun.";
                case "STAGE_03":
                    return "SANDSTORM\nOperator attacks miss 10% of the time; enemies attack 5% faster.";
                case "STAGE_04":
                    return $"FACTORY HEAT\n+1 heat per wave start and per mid-wave deploy. 5 stacks ignite: {Stage4HeatHazard.BurnDamagePerTick} damage/s until retreated.";
                case "STAGE_05":
                    return $"TRAFFIC\nFrom wave {Stage5CarEncounter.FirstCarWave}, up to {Stage5CarEncounter.TotalRollsPerWave} cars cross the boulevard each wave. Hand-built waves count as late waves.";
                case "STAGE_06":
                    return $"ACID RAIN\n{StageBootstrapper.AcidRainDamagePerTick} true damage every 2s to every operator and enemy (acid-immune enemies excepted).";
                default:
                    hasHazard = false;
                    return "NO HAZARD\nThis map's weather is visual only.";
            }
        }

        // ============================
        // Actions
        // ============================

        private void HandleEnemyClicked(EnemyData enemy)
        {
            if (enemy == null || waveManager == null) return;
            int amount = SpawnAmount;

            if (IsWaveActive)
            {
                int spawned = 0;
                for (int i = 0; i < amount; i++)
                    if (waveManager.SpawnSandboxEnemyNow(enemy)) spawned++;
                if (spawned > 0)
                    SetStatus(spawned == 1 ? $"Spawned {enemy.enemyName}." : $"Spawned {spawned}x {enemy.enemyName}.");
                else
                    SetStatus($"Could not spawn {enemy.enemyName} (no path, see Console).", true);
                return;
            }

            // Repeated clicks on the same enemy grow its last queue line instead of adding a new one
            SandboxSpawnGroup last = queue.Count > 0 ? queue[queue.Count - 1] : null;
            if (last != null && !last.timed && last.enemy == enemy && last.count < MaxGroupCount)
                last.count = Mathf.Min(MaxGroupCount, last.count + amount);
            else
                queue.Add(new SandboxSpawnGroup { enemy = enemy, count = amount });

            SetStatus($"Queued {amount}x {enemy.enemyName}.");
            RefreshQueue();
        }

        /// <summary>Right-click on an enemy tile: takes that enemy back out of the queue, newest lines first.</summary>
        private void RemoveQueuedEnemy(EnemyData enemy)
        {
            if (enemy == null || IsWaveActive) return;

            int remaining = SpawnAmount;
            int removed = 0;
            for (int i = queue.Count - 1; i >= 0 && remaining > 0; i--)
            {
                SandboxSpawnGroup group = queue[i];
                if (group.enemy != enemy) continue;
                int take = Mathf.Min(remaining, group.count);
                group.count -= take;
                remaining -= take;
                removed += take;
                if (group.count == 0) queue.RemoveAt(i);
            }
            if (removed == 0) return;

            SetStatus($"Removed {removed}x {enemy.enemyName} from the queue.");
            RefreshQueue();
        }

        private void SetSpawnAmount(int index)
        {
            spawnAmountIndex = Mathf.Clamp(index, 0, SpawnAmounts.Length - 1);
            RefreshSpawnAmount();
        }

        private void ChangeGroupCount(SandboxSpawnGroup group, int delta)
        {
            group.count = Mathf.Clamp(group.count + delta, 0, MaxGroupCount);
            if (group.count == 0) queue.Remove(group);
            RefreshQueue();
        }

        private void RemoveGroup(SandboxSpawnGroup group)
        {
            queue.Remove(group);
            RefreshQueue();
        }

        /// <summary>Swaps a hand-queued line with the previous/next hand-queued line (timed lines don't have an order).</summary>
        private void MoveGroup(SandboxSpawnGroup group, int direction)
        {
            int index = queue.IndexOf(group);
            if (index < 0) return;
            for (int j = index + direction; j >= 0 && j < queue.Count; j += direction)
            {
                if (queue[j].timed) continue;
                queue[index] = queue[j];
                queue[j] = group;
                break;
            }
            RefreshQueue();
        }

        private void ClearQueue()
        {
            queue.Clear();
            RefreshQueue();
            SetStatus("Queue cleared.");
        }

        private void ToggleKeepQueue()
        {
            keepQueueAfterStart = !keepQueueAfterStart;
            RefreshQueue();
            SetStatus(keepQueueAfterStart
                ? "Queue kept after START, so the same wave can be replayed."
                : "Queue empties after START.");
        }

        private void SelectDifficulty(StageDifficulty difficulty)
        {
            waveDifficulty = difficulty;
            RefreshWaves();
        }

        private void SelectWave(int index)
        {
            waveIndex = index;
            RefreshWaves();
        }

        /// <summary>Copies the selected stage wave into the queue, keeping each entry's timing and spawn point.</summary>
        private void LoadSelectedWave(bool replaceQueue)
        {
            WaveData[] waves = GetMapWaves();
            if (waves == null || waveIndex < 0 || waveIndex >= waves.Length || waves[waveIndex]?.entries == null) return;

            if (replaceQueue) queue.Clear();
            int spawnPoints = waveManager != null ? waveManager.SandboxSpawnPointCount : 0;
            foreach (WaveEntry entry in waves[waveIndex].entries)
            {
                if (entry == null || entry.enemyData == null || entry.count <= 0) continue;
                queue.Add(new SandboxSpawnGroup
                {
                    enemy = entry.enemyData,
                    count = entry.count,
                    timed = true,
                    startDelay = entry.startDelay,
                    interval = entry.spawnInterval,
                    spawnPoint = entry.spawnPointIndex < spawnPoints ? entry.spawnPointIndex : -1
                });
            }

            loadedWaveNumber = waveIndex + 1;
            loadedWaveLabel = $"Stage {GetStageNumber(currentMap)} {waveDifficulty} W{loadedWaveNumber}";
            RefreshQueue();
            SetStatus($"{(replaceQueue ? "Loaded" : "Added")} {loadedWaveLabel}. Press START WAVE.");
        }

        private void AddOperator(OperatorData op)
        {
            if (playerDeck == null || op == null) return;
            if (playerDeck.AddCard(new DraftCard(op, op.baseRarity)))
                SetStatus($"Added {op.operatorName} to the deck.");
            else
                SetStatus($"Deck full ({PlayerDeck.MAX_DECK_SIZE}). Deploy or clear cards first.", true);
        }

        private void ClearDeck()
        {
            playerDeck?.ClearDeck();
            SetStatus("Deck cleared.");
        }

        private void SwitchMap(StageData stage)
        {
            if (stage == null || bootstrapper == null) return;
            if (stage == currentMap)
            {
                SetStatus($"Already on {GetMapLabel(stage)}.");
                return;
            }

            bootstrapper.SwitchSandboxMap(stage.stageId);
        }

        private void ToggleHazard()
        {
            bool enabled = !StageCombatModifiers.HazardsEnabled;
            StageCombatModifiers.HazardsEnabled = enabled;

            if (!enabled)
            {
                // Drop what the hazard already left behind: heat and fire on deployed operators, cars still to come
                bool heatMap = bootstrapper != null && bootstrapper.EnvironmentStageId == "STAGE_04";
                OperatorManager operators = bootstrapper != null ? bootstrapper.operatorManager : null;
                if (heatMap && operators != null)
                {
                    foreach (OperatorBase op in operators.DeployedOperators)
                    {
                        if (op == null) continue;
                        op.HeatStacks = 0;
                        op.IsBurning = false;
                        OperatorHeatBar.ShowFor(op);
                    }
                }
                FindAnyObjectByType<Stage5CarEncounter>()?.ClearQueue();
            }

            RefreshHazard();
            SetStatus(enabled ? "Hazard ON: the map's environment affects combat." : "Hazard OFF: visuals stay, effects stop.");
        }

        private void KillAllEnemies()
        {
            if (waveManager == null) return;
            isClearingEnemies = true;
            waveManager.KillAllSandboxEnemies();
            isClearingEnemies = false;
            SetStatus("All enemies killed. The wave is still live.");
        }

        /// <summary>Called by the HUD's Start/End Wave button.</summary>
        public void ToggleWave()
        {
            if (waveManager == null) return;

            if (IsWaveActive)
            {
                isClearingEnemies = true;
                waveManager.EndSandboxWave();
                isClearingEnemies = false;
                SetStatus("Wave ended. All enemies cleared.");
            }
            else
            {
                bool hasTimed = queue.Exists(group => group.timed);
                bool hasUntimed = queue.Exists(group => !group.timed);
                int hazardWave = hasTimed ? loadedWaveNumber : CustomWaveHazardNumber;
                if (waveManager.StartSandboxWave(queue, hazardWave))
                {
                    ResetWaveStats();
                    int total = CountQueued();
                    waveSource = hasTimed ? loadedWaveLabel + (hasUntimed ? " + extras" : "") : total > 0 ? "Custom wave" : "Empty wave";
                    SetStatus(total > 0
                        ? $"Sending {total} enemies."
                        : "Wave live with an empty queue. Click enemies to spawn them.");
                    if (!keepQueueAfterStart) queue.Clear();
                }
            }

            RefreshAll();
        }

        // ============================
        // Wave stats
        // ============================

        private void ResetWaveStats()
        {
            waveTime = 0f;
            waveClearReported = false;
            waveKills = 0;
            waveLeaks = 0;
            waveLeakLives = 0;
            damageByOperator.Clear();
        }

        private void HandleEnemyDied(EnemyBase enemy)
        {
            if (!isClearingEnemies) waveKills++;
        }

        private void HandleEnemyLeaked(EnemyBase enemy)
        {
            waveLeaks++;
            waveLeakLives += enemy != null && enemy.Data != null ? Mathf.Max(0, enemy.Data.lifePointCost) : 1;
        }

        private void HandleOperatorDamage(OperatorBase op, int damage)
        {
            if (op == null) return;
            damageByOperator.TryGetValue(op, out int total);
            damageByOperator[op] = total + damage;
        }

        private void HandleDeckChanged(IReadOnlyList<DraftCard> _) => RefreshDeck();

        private int CountQueued()
        {
            int total = 0;
            foreach (SandboxSpawnGroup group in queue) total += group.count;
            return total;
        }

        private int CountQueued(EnemyData enemy)
        {
            int total = 0;
            foreach (SandboxSpawnGroup group in queue)
                if (group.enemy == enemy) total += group.count;
            return total;
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.FloorToInt(seconds);
            return $"{total / 60}:{total % 60:00}";
        }

        private static string FormatSeconds(float seconds) =>
            seconds.ToString(seconds == Mathf.Floor(seconds) ? "0" : "0.#", CultureInfo.InvariantCulture) + "s";

        private static string FormatNumber(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
