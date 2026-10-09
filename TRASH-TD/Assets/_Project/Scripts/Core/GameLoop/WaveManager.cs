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
        private bool isSpawningWave = false;
        private bool allWavesSpawned = false;
        private int difficultyLevel = 1;
        private int sandboxWaveNumber;
        private int sandboxSpawnPointCount;
        private int sandboxSpawnCursor;
        private bool isSandboxStage;
        private Coroutine sandboxSpawnCoroutine;

        private const float SandboxQueueSpawnIntervalSeconds = 1f;

        // Cached paths: one ground route per spawn, shared by every enemy movement type.
        private readonly Dictionary<string, List<Vector3>> pathCache = new Dictionary<string, List<Vector3>>();

        public int CurrentWaveNumber => currentWaveIndex + 1;
        public int TotalWaves => waves != null ? waves.Length : 0;
        public int SandboxWaveNumber => sandboxWaveNumber;
        public bool IsActive { get; private set; } = false;
        public bool IsWaveInProgress { get; private set; } = false;

        public event Action<int, int> OnWaveStarted;    // (currentWave, totalWaves)
        public event Action<int> OnWaveCompleted;        // (waveIndex)
        public event Action OnAllWavesCleared;
        /// <summary>Fired when a single wave's enemies are all defeated (for phase loop).</summary>
        public event Action OnSingleWaveFinished;

        private void Awake()
        {
            if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>();
            if (enemyManager == null) enemyManager = FindFirstObjectByType<EnemyManager>();
        }

        /// <summary>
        /// Initializes the wave manager for a given stage and difficulty.
        /// </summary>
        public void Initialize(StageData stageData, StageDifficulty difficulty)
        {
            if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>();
            if (enemyManager == null) enemyManager = FindFirstObjectByType<EnemyManager>();
            if (gridManager == null || enemyManager == null)
            {
                Debug.LogError("WaveManager: Cannot initialize without GridManager and EnemyManager.");
                return;
            }

            waves = stageData.GetWaves(difficulty);
            difficultyLevel = (int)difficulty + 1;
            isSandboxStage = stageData.stageId == "SANDBOX";
            sandboxWaveNumber = 0;
            sandboxSpawnPointCount = stageData.spawnPoints != null ? stageData.spawnPoints.Length : 0;
            sandboxSpawnCursor = 0;
            sandboxSpawnCoroutine = null;
            currentWaveIndex = -1;
            allWavesSpawned = false;
            isSpawningWave = false;
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

        public bool StartSandboxWave(IReadOnlyList<EnemyData> queuedEnemies)
        {
            if (!isSandboxStage || IsWaveInProgress)
                return false;

            sandboxWaveNumber++;
            IsWaveInProgress = true;
            isSpawningWave = true;
            sandboxSpawnCoroutine = StartCoroutine(SpawnSandboxQueueRoutine(
                queuedEnemies != null ? new List<EnemyData>(queuedEnemies) : new List<EnemyData>()));
            return true;
        }

        public void EndSandboxWave()
        {
            if (!isSandboxStage)
                return;

            if (sandboxSpawnCoroutine != null)
            {
                StopCoroutine(sandboxSpawnCoroutine);
                sandboxSpawnCoroutine = null;
            }

            isSpawningWave = false;
            IsWaveInProgress = false;
            if (enemyManager != null)
                enemyManager.ClearAllForSandbox();
        }

        private IEnumerator SpawnSandboxQueueRoutine(List<EnemyData> queuedEnemies)
        {
            for (int i = 0; i < queuedEnemies.Count; i++)
            {
                if (queuedEnemies[i] != null)
                    TrySpawnSandboxEnemy(queuedEnemies[i]);

                if (i < queuedEnemies.Count - 1)
                    yield return new WaitForSeconds(SandboxQueueSpawnIntervalSeconds);
            }

            isSpawningWave = false;
            sandboxSpawnCoroutine = null;
        }

        private bool TrySpawnSandboxEnemy(EnemyData enemyData)
        {
            if (enemyManager == null)
                enemyManager = FindFirstObjectByType<EnemyManager>();
            if (enemyManager == null)
            {
                Debug.LogError("WaveManager: Cannot spawn sandbox enemy without an EnemyManager.");
                return false;
            }

            for (int i = 0; i < sandboxSpawnPointCount; i++)
            {
                int spawnIndex = (sandboxSpawnCursor + i) % sandboxSpawnPointCount;
                if (!pathCache.TryGetValue(GetPathKey(spawnIndex), out List<Vector3> path) ||
                    path == null || path.Count == 0)
                    continue;

                sandboxSpawnCursor = (spawnIndex + 1) % sandboxSpawnPointCount;
                return enemyManager.SpawnEnemy(enemyData, path, difficultyLevel) != null;
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

                OnWaveStarted?.Invoke(CurrentWaveNumber, TotalWaves);
                isSpawningWave = true;

                // Spawn all entries in wave
                yield return StartCoroutine(SpawnWaveEntriesRoutine(currentWave));
                isSpawningWave = false;

                OnWaveCompleted?.Invoke(currentWaveIndex);
            }

            allWavesSpawned = true;

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

            OnWaveStarted?.Invoke(CurrentWaveNumber, TotalWaves);

            // Spawn all entries in wave
            yield return StartCoroutine(SpawnWaveEntriesRoutine(currentWave));

            OnWaveCompleted?.Invoke(waveIndex);

            // Wait until all active enemies are defeated
            while (enemyManager != null && enemyManager.ActiveEnemyCount > 0)
            {
                yield return new WaitForSeconds(0.5f);
            }

            IsWaveInProgress = false;

            OperatorManager.Instance?.AdvanceRedeployCooldownsOneRound();
            PlayerDeck.Instance?.AdvanceRedeployCooldownsOneRound();

            // Check if this was the last wave
            if (waveIndex >= waves.Length - 1)
            {
                allWavesSpawned = true;
                OnAllWavesCleared?.Invoke();
                if (GameManager.Instance != null && GameManager.Instance.CurrentState == GamePlayState.Playing)
                {
                    GameManager.Instance.TriggerVictory();
                }
            }
            else
            {
                // Signal that this single wave is done (phase loop listens to this)
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
                yield break;
            }

            for (int i = 0; i < entry.count; i++)
            {
                if (enemyManager != null)
                {
                    enemyManager.SpawnEnemy(entry.enemyData, path, difficultyLevel);
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
        }
    }
}
