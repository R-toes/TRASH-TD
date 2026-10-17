using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Systems;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// Stage 5 encounter: multi-roll car traffic on the boulevard lanes.
    /// Runs a 5-roll loop at wave start (15% chance per roll, 0-5 cars).
    /// Cars spawn with 10-30s intervals in random lanes (top: y=5, bottom: y=4)
    /// using randomized vehicle sprites.
    /// </summary>
    public class Stage5CarEncounter : MonoBehaviour
    {
        public const float SpawnChancePerRoll = 0.20f;
        public const int TotalRollsPerWave = 5;
        public const float MinIntervalSeconds = 5.0f;
        public const float MaxIntervalSeconds = 10.0f;
        public const int TopLaneY = 5;
        public const int BottomLaneY = 4;

        [System.Serializable]
        public struct ScheduledCar
        {
            public float intervalSeconds;
            public float spawnTimestamp;
            public int laneY;
            public int spriteIndex;
        }

        private GridManager gridManager;
        private WaveManager waveManager;
        private EnemyManager enemyManager;
        private OperatorManager operatorManager;
        private StageData stageData;
        private Sprite[] vehicleSprites;

        private readonly Queue<ScheduledCar> spawnQueue = new Queue<ScheduledCar>();
        private Coroutine queueProcessingRoutine;
        private readonly List<Stage5TrafficCar> activeCars = new List<Stage5TrafficCar>();

        public int QueuedCarCount => spawnQueue.Count;
        public IReadOnlyCollection<ScheduledCar> SpawnQueue => spawnQueue;

        public IReadOnlyList<Stage5TrafficCar> ActiveCars
        {
            get
            {
                activeCars.RemoveAll(c => c == null);
                return activeCars;
            }
        }

        public Sprite[] VehicleSprites => vehicleSprites;

        public void Initialize(
            GridManager grid,
            WaveManager wave,
            EnemyManager enemies,
            OperatorManager operators,
            StageData stage,
            Sprite[] customSprites = null)
        {
            gridManager = grid != null ? grid : Object.FindFirstObjectByType<GridManager>();
            waveManager = wave != null ? wave : Object.FindFirstObjectByType<WaveManager>();
            enemyManager = enemies != null ? enemies : Object.FindFirstObjectByType<EnemyManager>();
            operatorManager = operators != null ? operators : Object.FindFirstObjectByType<OperatorManager>();
            stageData = stage;

            if (customSprites != null && customSprites.Length > 0)
            {
                vehicleSprites = customSprites;
            }
            else if (stageData != null && stageData.trafficVehicleSprites != null && stageData.trafficVehicleSprites.Length > 0)
            {
                vehicleSprites = stageData.trafficVehicleSprites;
            }
            else
            {
                LoadVehicleSprites();
            }

            if (waveManager != null)
            {
                waveManager.OnWaveStarted += HandleWaveStarted;
            }
        }

        private void OnDestroy()
        {
            if (waveManager != null)
            {
                waveManager.OnWaveStarted -= HandleWaveStarted;
            }

            ClearQueue();
        }

        private void LoadVehicleSprites()
        {
#if UNITY_EDITOR
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level 5 vehicles.png");
            if (assets == null || assets.Length == 0)
            {
                assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/level 5 vehicles.png");
            }

            if (assets != null)
            {
                var list = new List<Sprite>();
                foreach (var obj in assets)
                {
                    if (obj is Sprite s) list.Add(s);
                }
                if (list.Count > 0)
                {
                    list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                    vehicleSprites = list.ToArray();
                    return;
                }
            }
#endif
            vehicleSprites = Resources.LoadAll<Sprite>("Levels/Level 5 vehicles");
        }

        private void HandleWaveStarted(int currentWave, int totalWaves)
        {
            RollAndScheduleCarsForWave();
        }

        public List<ScheduledCar> RollAndScheduleCarsForWave(
            float chance = SpawnChancePerRoll,
            int rollCount = TotalRollsPerWave)
        {
            var newlyScheduled = new List<ScheduledCar>();
            int successes = 0;
            for (int i = 0; i < rollCount; i++)
            {
                if (Random.value < chance)
                {
                    successes++;
                }
            }

            if (successes > 0)
            {
                float accumulated = 0f;
                int spriteCount = (vehicleSprites != null && vehicleSprites.Length > 0) ? vehicleSprites.Length : 3;

                for (int i = 0; i < successes; i++)
                {
                    float interval = Random.Range(MinIntervalSeconds, MaxIntervalSeconds);
                    accumulated += interval;
                    int laneY = (Random.value < 0.5f) ? TopLaneY : BottomLaneY;
                    int spriteIndex = Random.Range(0, spriteCount);

                    var car = new ScheduledCar
                    {
                        intervalSeconds = interval,
                        spawnTimestamp = accumulated,
                        laneY = laneY,
                        spriteIndex = spriteIndex
                    };

                    newlyScheduled.Add(car);
                    spawnQueue.Enqueue(car);
                }

                if (gameObject.activeInHierarchy && queueProcessingRoutine == null)
                {
                    queueProcessingRoutine = StartCoroutine(ProcessSpawnQueue());
                }
            }

            return newlyScheduled;
        }

        public void EnqueueCar(int laneY, float interval = 5.0f, int spriteIndex = 0)
        {
            var car = new ScheduledCar
            {
                intervalSeconds = interval,
                spawnTimestamp = interval,
                laneY = laneY,
                spriteIndex = spriteIndex
            };
            spawnQueue.Enqueue(car);

            if (gameObject.activeInHierarchy && queueProcessingRoutine == null)
            {
                queueProcessingRoutine = StartCoroutine(ProcessSpawnQueue());
            }
        }

        public void ClearQueue()
        {
            spawnQueue.Clear();
            if (queueProcessingRoutine != null)
            {
                StopCoroutine(queueProcessingRoutine);
                queueProcessingRoutine = null;
            }
        }

        private IEnumerator ProcessSpawnQueue()
        {
            while (spawnQueue.Count > 0)
            {
                ScheduledCar car = spawnQueue.Dequeue();
                float timer = 0f;
                while (timer < car.intervalSeconds)
                {
                    bool isPaused = GameManager.Instance != null && GameManager.Instance.IsPaused;
                    bool isCombatPaused = GameManager.Instance != null && !GameManager.Instance.IsSandbox && GameManager.Instance.CurrentPhase != StagePhase.WaveActive;

                    if (!isPaused && !isCombatPaused)
                    {
                        timer += Time.deltaTime;
                    }
                    yield return null;
                }

                SpawnCar(car.laneY, car.spriteIndex);
            }

            queueProcessingRoutine = null;
        }

        public Stage5TrafficCar SpawnCar(int laneY, int spriteIndex = 0)
        {
            Sprite carSprite = null;
            if (vehicleSprites != null && vehicleSprites.Length > 0)
            {
                int safeIdx = Mathf.Clamp(spriteIndex, 0, vehicleSprites.Length - 1);
                carSprite = vehicleSprites[safeIdx];
            }

            GameObject carObj = new GameObject($"Stage5Car_Lane{laneY}");
            carObj.transform.SetParent(transform);

            var car = carObj.AddComponent<Stage5TrafficCar>();
            car.Initialize(laneY, carSprite, gridManager, enemyManager, operatorManager);
            activeCars.Add(car);
            return car;
        }
    }
}

