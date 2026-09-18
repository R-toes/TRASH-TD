using System;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Core.GameLoop
{
    public enum GamePlayState
    {
        NotStarted,
        Playing,
        Paused,
        Victory,
        Defeat
    }

    /// <summary>
    /// Central game manager orchestrating stage lifecycle, DP generation,
    /// life points, 3-star conditions, and win/loss state (GDD 1.2, 1.4.6, 1.4.7).
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Current Stage")]
        [SerializeField] private StageData currentStage;
        [SerializeField] private StageDifficulty currentDifficulty = StageDifficulty.Normal;

        [Header("Economy & DP")]
        [SerializeField] private int startingDP = 10;
        [SerializeField] private int maxDP = 99;
        [SerializeField] private float dpGenerationInterval = 1.0f; // 1 DP per second
        [SerializeField] private int dpPerTick = 1;

        // --- Runtime State ---
        private int currentLifePoints;
        private int maxLifePoints;
        private int currentDP;
        private float dpTimer;
        private float elapsedTime;
        private GamePlayState currentState = GamePlayState.NotStarted;
        private bool leakedAnyEnemy = false;

        // --- Public Properties ---
        public StageData CurrentStage => currentStage;
        public StageDifficulty CurrentDifficulty => currentDifficulty;
        public int CurrentLifePoints => currentLifePoints;
        public int MaxLifePoints => maxLifePoints;
        public int CurrentDP => currentDP;
        public float ElapsedTime => elapsedTime;
        public GamePlayState CurrentState => currentState;

        // --- Events ---
        public event Action<int, int> OnLifePointsChanged; // (current, max)
        public event Action<int> OnDPChanged;              // (current)
        public event Action<GamePlayState> OnGameStateChanged;
        public event Action<int> OnStageVictory;           // (starsEarned 1-3)
        public event Action OnStageDefeat;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            if (EnemyManager.Instance != null)
            {
                EnemyManager.Instance.OnEnemyReachedExit += HandleEnemyReachedExit;
            }
        }

        /// <summary>
        /// Starts or restarts the stage with the specified stage data and difficulty.
        /// </summary>
        public void StartStage(StageData stageData, StageDifficulty difficulty)
        {
            currentStage = stageData;
            currentDifficulty = difficulty;

            maxLifePoints = stageData.GetLifePoints(difficulty);
            currentLifePoints = maxLifePoints;
            currentDP = startingDP;
            dpTimer = 0f;
            elapsedTime = 0f;
            leakedAnyEnemy = false;

            if (OperatorManager.Instance != null)
            {
                OperatorManager.Instance.SquadLimit = stageData.squadSizeLimit;
            }

            SetState(GamePlayState.Playing);
            OnLifePointsChanged?.Invoke(currentLifePoints, maxLifePoints);
            OnDPChanged?.Invoke(currentDP);
        }

        private void Update()
        {
            if (currentState != GamePlayState.Playing) return;

            elapsedTime += Time.deltaTime;

            // DP generation over time
            dpTimer += Time.deltaTime;
            if (dpTimer >= dpGenerationInterval)
            {
                dpTimer -= dpGenerationInterval;
                AddDP(dpPerTick);
            }

            // Optional stage timer check
            if (currentStage != null && currentStage.timeLimit > 0f && elapsedTime >= currentStage.timeLimit)
            {
                TriggerDefeat();
            }
        }

        public void AddDP(int amount)
        {
            currentDP = Mathf.Clamp(currentDP + amount, 0, maxDP);
            OnDPChanged?.Invoke(currentDP);
        }

        public bool TrySpendDP(int cost)
        {
            if (currentDP < cost) return false;

            currentDP -= cost;
            OnDPChanged?.Invoke(currentDP);
            return true;
        }

        public void PauseGame()
        {
            if (currentState == GamePlayState.Playing)
            {
                SetState(GamePlayState.Paused);
                Time.timeScale = 0f;
            }
        }

        public void ResumeGame()
        {
            if (currentState == GamePlayState.Paused)
            {
                SetState(GamePlayState.Playing);
                Time.timeScale = 1f;
            }
        }

        public void SetGameSpeed(float speed)
        {
            if (currentState == GamePlayState.Playing)
            {
                Time.timeScale = speed;
            }
        }

        private void HandleEnemyReachedExit(EnemyBase enemy)
        {
            if (currentState != GamePlayState.Playing) return;

            leakedAnyEnemy = true;
            int cost = (enemy != null && enemy.Data != null) ? enemy.Data.lifePointCost : 1;
            currentLifePoints = Mathf.Max(0, currentLifePoints - cost);
            OnLifePointsChanged?.Invoke(currentLifePoints, maxLifePoints);

            if (currentLifePoints <= 0)
            {
                TriggerDefeat();
            }
        }

        public void TriggerDefeat()
        {
            if (currentState == GamePlayState.Defeat || currentState == GamePlayState.Victory) return;

            SetState(GamePlayState.Defeat);
            Time.timeScale = 1f;
            OnStageDefeat?.Invoke();
        }

        public void TriggerVictory()
        {
            if (currentState == GamePlayState.Victory || currentState == GamePlayState.Defeat) return;

            SetState(GamePlayState.Victory);
            Time.timeScale = 1f;

            int stars = CalculateStars();
            OnStageVictory?.Invoke(stars);
        }

        private int CalculateStars()
        {
            // 3-star rating conditions (GDD 1.4.6):
            // 3 stars: Cleared with no leaks (full life points maintained)
            // 2 stars: Cleared with remaining life points >= 50%
            // 1 star: Cleared with any remaining life points
            if (!leakedAnyEnemy && currentLifePoints == maxLifePoints) return 3;
            if (currentLifePoints >= Mathf.CeilToInt(maxLifePoints * 0.5f)) return 2;
            return 1;
        }

        private void SetState(GamePlayState newState)
        {
            currentState = newState;
            OnGameStateChanged?.Invoke(currentState);
        }
    }
}
