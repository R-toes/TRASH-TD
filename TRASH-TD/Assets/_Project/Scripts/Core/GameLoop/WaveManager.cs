using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Core.Pathfinding;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// Manages wave progression and enemy spawning according to StageData (GDD 1.5, 1.11).
    /// Uses A* pathfinding (GDD 1.4.2) to compute movement paths from spawn points to exits.
    /// Triggers stage victory when all waves and enemies are cleared.
    /// </summary>
    public class WaveManager : MonoBehaviour
    {
        [SerializeField] private GridManager gridManager;
        [SerializeField] private EnemyManager enemyManager;

        private AStarPathfinder pathfinder;
        private WaveData[] waves;
        private int currentWaveIndex = -1;
        private int sandboxWaveNumber;
        private int sandboxSpawnPointCount;
        private int sandboxSpawnCursor;
        private bool isSandboxStage;
        private readonly List<Coroutine> sandboxSpawnCoroutines = new List<Coroutine>();

        private const float SandboxQueueSpawnIntervalSeconds = 1f;

        // Enemy stats are the same on every difficulty; difficulties differ only in their wave lists.
        private const int EnemyLevel = 1;

        // Cached paths: one ground route per spawn, shared by every enemy movement type.
        private readonly Dictionary<string, List<Vector3>> pathCache = new Dictionary<string, List<Vector3>>();

        public int CurrentWaveNumber => currentWaveIndex + 1;
        public int TotalWaves => waves != null ? waves.Length : 0;
        public int SandboxWaveNumber => sandboxWaveNumber;
        public bool IsActive { get; private set; } = false;
        public bool IsWaveInProgress { get; private set; } = false;

        /// <summary>Enemies of the running wave that haven't spawned yet (0 between waves). Shown on the HUD top bar.</summary>
        public int PendingSpawnCount { get; private set; }

        /// <summary>The wave <see cref="StartNextWave"/> will run next, or null when none is left.</summary>
        public WaveData PeekNextWave()
        {
            int next = currentWaveIndex + 1;
            return waves != null && next >= 0 && next < waves.Length ? waves[next] : null;
        }

        /// <summary>Total enemies a wave spawns (entries with no enemy data are skipped, as when spawning).</summary>
        public static int CountEnemies(WaveData wave)
        {
            int total = 0;
            if (wave?.entries == null) return total;
            foreach (WaveEntry entry in wave.entries)
            {
                if (entry != null && entry.enemyData != null) total += Mathf.Max(0, entry.count);
            }
            return total;
        }

        public event Action<int, int> OnWaveStarted;    // (currentWave, totalWaves)
        public event Action<int> OnWaveCompleted;        // (waveIndex)
        public event Action OnAllWavesCleared;
        /// <summary>Fired when a single wave's enemies are all defeated (for phase loop).</summary>
        public event Action OnSingleWaveFinished;

        private void Awake()
        {
            if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();
            if (enemyManager == null) enemyManager = FindAnyObjectByType<EnemyManager>();
        }

        /// <summary>
        /// Initializes the wave manager for a given stage and difficulty.
        /// </summary>
        public void Initialize(StageData stageData, StageDifficulty difficulty)
        {
            if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();
            if (enemyManager == null) enemyManager = FindAnyObjectByType<EnemyManager>();
            if (gridManager == null || enemyManager == null)
            {
                Debug.LogError("WaveManager: Cannot initialize without GridManager and EnemyManager.");
                return;
            }

            waves = stageData.GetWaves(difficulty);
            isSandboxStage = stageData.stageId == "SANDBOX";
            sandboxWaveNumber = 0;
            sandboxSpawnPointCount = stageData.spawnPoints != null ? stageData.spawnPoints.Length : 0;
            sandboxSpawnCursor = 0;
            sandboxSpawnCoroutines.Clear();
            currentWaveIndex = -1;
            pathCache.Clear();

            // Initialize custom A* Pathfinder
            pathfinder = new AStarPathfinder();
            pathfinder.Initialize(gridManager);

            // Pre-calculate paths for spawn and exit points
            PrecomputePaths(stageData);

            IsActive = true;
        }

        private void PrecomputePaths(StageData stageData)
        {
            if (stageData.spawnPoints == null || stageData.exitPoints == null) return;

            // 1. If explicit enemyPaths are configured on the stage, use them
            if (stageData.enemyPaths != null && stageData.enemyPaths.Length > 0)
            {
                foreach (var pathData in stageData.enemyPaths)
                {
                    if (pathData == null) continue;
                    int s = pathData.spawnPointIndex;
                    int e = pathData.exitPointIndex;

                    if (s < 0 || s >= stageData.spawnPoints.Length) continue;

                    // If explicit waypoints are defined, convert to world coordinates
                    if (pathData.waypoints != null && pathData.waypoints.Length > 0)
                    {
                        var worldPath = new List<Vector3>(pathData.waypoints.Length);
                        for (int i = 0; i < pathData.waypoints.Length; i++)
                        {
                            worldPath.Add(gridManager.GridToWorldPosition(pathData.waypoints[i]));
                        }
                        pathCache[GetPathKey(s)] = worldPath;
                        continue;
                    }

                    // Otherwise if a specific exit point index is designated, find path to that exit
                    if (e >= 0 && e < stageData.exitPoints.Length)
                    {
                        var spawnPos = stageData.spawnPoints[s];
                        var exitPos = stageData.exitPoints[e];

                        var path = pathfinder.FindPath(spawnPos, exitPos, EnemyMovementType.Ground);
                        if (path != null) pathCache[GetPathKey(s)] = path;
                    }
                }
            }

            // 2. Fallback for any spawn points not covered by explicit paths
            for (int s = 0; s < stageData.spawnPoints.Length; s++)
            {
                string pathKey = GetPathKey(s);
                if (!pathCache.ContainsKey(pathKey))
                {
                    var spawnPos = stageData.spawnPoints[s];
                    var path = pathfinder.FindBestPath(spawnPos, stageData.exitPoints, EnemyMovementType.Ground);
                    if (path != null) pathCache[pathKey] = path;
                }
            }
        }

        private string GetPathKey(int spawnIndex) => $"{spawnIndex}";

        /// <summary>
        /// Start wave progression (all waves in sequence — legacy mode).
        /// </summary>
        public void StartWaves()
        {
            if (waves == null || waves.Length == 0) return;
            StartCoroutine(WaveProgressionRoutine());
        }

        /// <summary>
        /// Start the next single wave (for card pick → prep → wave → card pick loop).
        /// Returns false if no more waves to start.
        /// </summary>
        public bool StartNextWave()
        {
            if (waves == null || waves.Length == 0) return false;

            // Advance to next wave
            currentWaveIndex++;
            if (currentWaveIndex >= waves.Length) return false;

            IsWaveInProgress = true;
            StartCoroutine(SingleWaveRoutine(currentWaveIndex));
            return true;
        }

        /// <summary>
        /// Set the wave index externally (used when GameManager tracks the index).
        /// </summary>
        public void SetWaveIndex(int index)
        {
            currentWaveIndex = index - 1; // StartNextWave will increment
        }

        public bool SpawnSandboxEnemyNow(EnemyData enemyData)
        {
            if (!isSandboxStage || !IsWaveInProgress || enemyData == null)
                return false;

            return TrySpawnSandboxEnemy(enemyData);
        }

        /// <summary>Number of map spawn points the sandbox can use.</summary>
        public int SandboxSpawnPointCount => sandboxSpawnPointCount;

        /// <summary>
        /// Starts a sandbox wave. Timed groups (loaded from a real stage wave) run in parallel with their own
        /// start delay, interval and spawn point, exactly like a stage wave. Untimed groups (hand-queued) follow
        /// one after another, one enemy a second, cycling through the spawn points.
        /// <paramref name="hazardWaveNumber"/> is the wave number reported to wave-start hazards (Stage 4 heat,
        /// Stage 5 traffic from wave 6), so a loaded wave behaves like that wave of its stage.
        /// </summary>
        public bool StartSandboxWave(IReadOnlyList<SandboxSpawnGroup> groups, int hazardWaveNumber)
        {
            if (!isSandboxStage || IsWaveInProgress)
                return false;

            sandboxWaveNumber++;
            IsWaveInProgress = true;
            sandboxSpawnCoroutines.Clear();

            var untimed = new List<SandboxSpawnGroup>();
            int pending = 0;
            if (groups != null)
            {
                foreach (SandboxSpawnGroup group in groups)
                {
                    if (group == null || group.enemy == null || group.count <= 0) continue;
                    // Copied so editing the sandbox queue mid-wave can't change what is spawning
                    SandboxSpawnGroup copy = group.Clone();
                    pending += copy.count;
                    if (copy.timed) sandboxSpawnCoroutines.Add(StartCoroutine(SpawnSandboxTimedGroupRoutine(copy)));
                    else untimed.Add(copy);
                }
            }
            PendingSpawnCount = pending;
            if (untimed.Count > 0)
                sandboxSpawnCoroutines.Add(StartCoroutine(SpawnSandboxQueueRoutine(untimed)));

            OnWaveStarted?.Invoke(hazardWaveNumber, 0);
            OperatorManager.Instance?.MarkAllDeployedOperatorsAsFought();
            return true;
        }

        public void EndSandboxWave()
        {
            if (!isSandboxStage)
                return;

            StopSandboxSpawning();
            IsWaveInProgress = false;
            KillAllSandboxEnemies();
        }

        /// <summary>Kills every enemy on the field; the wave stays live and queued spawns keep coming.</summary>
        public void KillAllSandboxEnemies()
        {
            if (!isSandboxStage)
                return;

            if (enemyManager == null)
                enemyManager = EnemyManager.Instance ?? FindAnyObjectByType<EnemyManager>();
            if (enemyManager != null)
                enemyManager.ClearAllForSandbox();
        }

        private void StopSandboxSpawning()
        {
            foreach (Coroutine routine in sandboxSpawnCoroutines)
            {
                if (routine != null) StopCoroutine(routine);
            }
            sandboxSpawnCoroutines.Clear();
            PendingSpawnCount = 0;
        }

        private IEnumerator SpawnSandboxQueueRoutine(List<SandboxSpawnGroup> groups)
        {
            bool first = true;
            foreach (SandboxSpawnGroup group in groups)
            {
                for (int i = 0; i < group.count; i++)
                {
                    if (!first)
                        yield return new WaitForSeconds(SandboxQueueSpawnIntervalSeconds);
                    first = false;
                    PendingSpawnCount = Mathf.Max(0, PendingSpawnCount - 1);
                    TrySpawnSandboxEnemy(group.enemy, group.spawnPoint);
                }
            }
        }

        private IEnumerator SpawnSandboxTimedGroupRoutine(SandboxSpawnGroup group)
        {
            if (group.startDelay > 0f)
                yield return new WaitForSeconds(group.startDelay);

            for (int i = 0; i < group.count; i++)
            {
                PendingSpawnCount = Mathf.Max(0, PendingSpawnCount - 1);
                TrySpawnSandboxEnemy(group.enemy, group.spawnPoint);
                if (i < group.count - 1 && group.interval > 0f)
                    yield return new WaitForSeconds(group.interval);
            }
        }

        /// <summary>Spawns at <paramref name="spawnPoint"/> when it has a path, otherwise at the next spawn point in turn.</summary>
        private bool TrySpawnSandboxEnemy(EnemyData enemyData, int spawnPoint = -1)
        {
            if (enemyManager == null)
                enemyManager = FindAnyObjectByType<EnemyManager>();
            if (enemyManager == null)
            {
                Debug.LogError("WaveManager: Cannot spawn sandbox enemy without an EnemyManager.");
                return false;
            }

            if (spawnPoint >= 0 && pathCache.TryGetValue(GetPathKey(spawnPoint), out List<Vector3> fixedPath) &&
                fixedPath != null && fixedPath.Count > 0)
                return enemyManager.SpawnEnemy(enemyData, fixedPath, EnemyLevel) != null;

            for (int i = 0; i < sandboxSpawnPointCount; i++)
            {
                int spawnIndex = (sandboxSpawnCursor + i) % sandboxSpawnPointCount;
                if (!pathCache.TryGetValue(GetPathKey(spawnIndex), out List<Vector3> path) ||
                    path == null || path.Count == 0)
                    continue;

                sandboxSpawnCursor = (spawnIndex + 1) % sandboxSpawnPointCount;
                return enemyManager.SpawnEnemy(enemyData, path, EnemyLevel) != null;
            }

            Debug.LogError("WaveManager: Cannot spawn sandbox enemy because no valid spawn path is available.");
            return false;
        }

        private IEnumerator WaveProgressionRoutine()
        {
            for (int w = 0; w < waves.Length; w++)
            {
                currentWaveIndex = w;
                WaveData currentWave = waves[w];

                // Pre-wave delay
                if (currentWave.preWaveDelay > 0f)
                {
                    yield return new WaitForSeconds(currentWave.preWaveDelay);
                }

                PendingSpawnCount = CountEnemies(currentWave);
                OnWaveStarted?.Invoke(CurrentWaveNumber, TotalWaves);
            OperatorManager.Instance?.MarkAllDeployedOperatorsAsFought();

                // Spawn all entries in wave
                yield return StartCoroutine(SpawnWaveEntriesRoutine(currentWave));
                PendingSpawnCount = 0;

                OnWaveCompleted?.Invoke(currentWaveIndex);
            }

            // Wait until all remaining active enemies are defeated
            while (enemyManager != null && enemyManager.ActiveEnemyCount > 0)
            {
                yield return new WaitForSeconds(0.5f);
            }

            OnAllWavesCleared?.Invoke();
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GamePlayState.Playing)
            {
                GameManager.Instance.TriggerVictory();
            }
        }

        /// <summary>
        /// Run a single wave, then wait for all enemies to die.
        /// </summary>
        private IEnumerator SingleWaveRoutine(int waveIndex)
        {
            if (waveIndex < 0 || waveIndex >= waves.Length)
            {
                IsWaveInProgress = false;
                yield break;
            }

            WaveData currentWave = waves[waveIndex];

            // Pre-wave delay
            if (currentWave.preWaveDelay > 0f)
            {
                yield return new WaitForSeconds(currentWave.preWaveDelay);
            }

            PendingSpawnCount = CountEnemies(currentWave);
            OnWaveStarted?.Invoke(CurrentWaveNumber, TotalWaves);
            OperatorManager.Instance?.MarkAllDeployedOperatorsAsFought();

            // Spawn all entries in wave
            yield return StartCoroutine(SpawnWaveEntriesRoutine(currentWave));
            PendingSpawnCount = 0;

            OnWaveCompleted?.Invoke(waveIndex);

            // Wait until all active enemies are defeated
            while (enemyManager != null && enemyManager.ActiveEnemyCount > 0)
            {
                yield return new WaitForSeconds(0.5f);
            }

            IsWaveInProgress = false;

            PlayerDeck.Instance?.AdvanceRedeployCooldownsOneRound();

            // Check if this was the last wave
            if (waveIndex >= waves.Length - 1)
            {
                OnAllWavesCleared?.Invoke();
                if (GameManager.Instance != null && GameManager.Instance.CurrentState == GamePlayState.Playing)
                {
                    GameManager.Instance.TriggerVictory();
                }
            }
            else
            {
                // Advance the phase loop here rather than relying on a UI listener,
                // so the stage keeps progressing even without a HUD.
                GameManager.Instance?.OnWaveFinished();
                OnSingleWaveFinished?.Invoke();
            }
        }

        private IEnumerator SpawnWaveEntriesRoutine(WaveData wave)
        {
            if (wave.entries == null || wave.entries.Length == 0) yield break;

            var entryCoroutines = new List<Coroutine>();
            foreach (var entry in wave.entries)
            {
                if (entry != null && entry.enemyData != null)
                {
                    entryCoroutines.Add(StartCoroutine(SpawnSingleEntryRoutine(entry)));
                }
            }

            // Wait for all spawn entries to finish spawning
            foreach (var coroutine in entryCoroutines)
            {
                yield return coroutine;
            }
        }

        private IEnumerator SpawnSingleEntryRoutine(WaveEntry entry)
        {
            if (entry.startDelay > 0f)
            {
                yield return new WaitForSeconds(entry.startDelay);
            }

            string pathKey = GetPathKey(entry.spawnPointIndex);
            if (!pathCache.TryGetValue(pathKey, out var path) || path == null || path.Count == 0)
            {
                PendingSpawnCount = Mathf.Max(0, PendingSpawnCount - Mathf.Max(0, entry.count));
                yield break;
            }

            for (int i = 0; i < entry.count; i++)
            {
                // Counted down before the spawn, so the HUD reads the new total when the spawn event fires
                PendingSpawnCount = Mathf.Max(0, PendingSpawnCount - 1);
                if (enemyManager != null)
                {
                    enemyManager.SpawnEnemy(entry.enemyData, path, EnemyLevel);
                }

                if (i < entry.count - 1 && entry.spawnInterval > 0f)
                {
                    yield return new WaitForSeconds(entry.spawnInterval);
                }
            }
        }

        public void StopWaves()
        {
            StopAllCoroutines();
            IsActive = false;
            PendingSpawnCount = 0;
        }
    }

    /// <summary>One line of the QA sandbox's spawn queue (see <see cref="WaveManager.StartSandboxWave"/>).</summary>
    public sealed class SandboxSpawnGroup
    {
        public EnemyData enemy;
        public int count = 1;
        /// <summary>True for groups loaded from a stage wave: they keep the wave's own timing and spawn point.</summary>
        public bool timed;
        public float startDelay;
        public float interval;
        /// <summary>Spawn point index, or -1 to cycle through the spawn points.</summary>
        public int spawnPoint = -1;

        public SandboxSpawnGroup Clone() => (SandboxSpawnGroup)MemberwiseClone();
    }
}
