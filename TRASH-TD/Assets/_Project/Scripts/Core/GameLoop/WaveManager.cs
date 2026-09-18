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

        // Cached paths: [spawnIndex, exitIndex, movementType]
        private readonly Dictionary<string, List<Vector3>> pathCache = new Dictionary<string, List<Vector3>>();

        public int CurrentWaveNumber => currentWaveIndex + 1;
        public int TotalWaves => waves != null ? waves.Length : 0;
        public bool IsActive { get; private set; } = false;

        public event Action<int, int> OnWaveStarted;    // (currentWave, totalWaves)
        public event Action<int> OnWaveCompleted;        // (waveIndex)
        public event Action OnAllWavesCleared;

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
            waves = stageData.GetWaves(difficulty);
            difficultyLevel = (int)difficulty + 1;
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

            for (int s = 0; s < stageData.spawnPoints.Length; s++)
            {
                var spawnPos = stageData.spawnPoints[s];
                // Compute for Ground and Air
                string groundKey = GetPathKey(s, EnemyMovementType.Ground);
                var groundPath = pathfinder.FindBestPath(spawnPos, stageData.exitPoints, EnemyMovementType.Ground);
                if (groundPath != null) pathCache[groundKey] = groundPath;

                string airKey = GetPathKey(s, EnemyMovementType.Air);
                var airPath = pathfinder.FindBestPath(spawnPos, stageData.exitPoints, EnemyMovementType.Air);
                if (airPath != null) pathCache[airKey] = airPath;
            }
        }

        private string GetPathKey(int spawnIndex, EnemyMovementType movementType) => $"{spawnIndex}_{movementType}";

        /// <summary>
        /// Start wave progression.
        /// </summary>
        public void StartWaves()
        {
            if (waves == null || waves.Length == 0) return;
            StartCoroutine(WaveProgressionRoutine());
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

            string pathKey = GetPathKey(entry.spawnPointIndex, entry.enemyData.movementType);
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
