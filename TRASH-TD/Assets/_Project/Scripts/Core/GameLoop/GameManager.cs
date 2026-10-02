using System;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// The phase within a single wave cycle.
    /// CardPick → Preparation → WaveActive → (next wave) CardPick → ...
    /// </summary>
    public enum StagePhase
    {
        CardPick,       // Player picks a card from the 3-card draft offer
        Preparation,    // Player places operators on the grid before starting the wave
        WaveActive      // Wave is spawning / enemies are alive
    }

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

        // --- Phase ---
        private StagePhase currentPhase = StagePhase.CardPick;
        private int currentWaveIndex = 0;

        public StagePhase CurrentPhase => currentPhase;

        // --- Events ---
        public event Action<int, int> OnLifePointsChanged; // (current, max)
        public event Action<int> OnDPChanged;              // (current)
        public event Action<GamePlayState> OnGameStateChanged;
        public event Action<StagePhase> OnPhaseChanged;
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

            maxLifePoints = 3; // 3 lives
            currentLifePoints = maxLifePoints;
            currentDP = 0;
            dpTimer = 0f;
            elapsedTime = 0f;
            leakedAnyEnemy = false;
            currentWaveIndex = 0;

            if (OperatorManager.Instance != null && stageData != null)
            {
                OperatorManager.Instance.SquadLimit = stageData.squadSizeLimit;
            }

            SetState(GamePlayState.Playing);
            OnLifePointsChanged?.Invoke(currentLifePoints, maxLifePoints);

            // Start with the card pick phase
            SetPhase(StagePhase.CardPick);
        }

        /// <summary>
        /// Transition to Preparation phase after the player confirms a card.
        /// </summary>
        public void EnterPreparationPhase()
        {
            if (currentState != GamePlayState.Playing) return;
            SetPhase(StagePhase.Preparation);
        }

        /// <summary>
        /// Transition to WaveActive phase when the player clicks Start Wave.
        /// </summary>
        public void EnterWavePhase()
        {
            if (currentState != GamePlayState.Playing) return;
            if (currentPhase != StagePhase.Preparation) return;
            SetPhase(StagePhase.WaveActive);
        }

        /// <summary>
        /// Called when a wave finishes. Moves to CardPick for next wave,
        /// or triggers victory if all waves are done.
        /// </summary>
        public void OnWaveFinished()
        {
            if (currentState != GamePlayState.Playing) return;
            currentWaveIndex++;

            var waves = currentStage.GetWaves(currentDifficulty);
            if (waves == null || currentWaveIndex >= waves.Length)
            {
                // All waves cleared — victory will be triggered by WaveManager
                // when all remaining enemies are defeated
                return;
            }

            // More waves remain — go back to card pick
            SetPhase(StagePhase.CardPick);
        }

        public int GetCurrentWaveIndex() => currentWaveIndex;

        private void Update()
        {
            if (currentState != GamePlayState.Playing) return;

            elapsedTime += Time.deltaTime;

            // Optional stage timer check
            if (currentStage != null && currentStage.timeLimit > 0f && elapsedTime >= currentStage.timeLimit)
            {
                TriggerDefeat();
            }
        }

        public void AddDP(int amount)
        {
            // DP system removed
        }

        public bool TrySpendDP(int cost)
        {
            // DP system removed — deployment is always free
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
            Time.timeScale = 0f;
            OnStageDefeat?.Invoke();
        }

        public void TriggerVictory()
        {
            if (currentState == GamePlayState.Victory || currentState == GamePlayState.Defeat) return;

            SetState(GamePlayState.Victory);
            Time.timeScale = 0f;

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

        private void SetPhase(StagePhase newPhase)
        {
            currentPhase = newPhase;
            OnPhaseChanged?.Invoke(currentPhase);
        }
    }
}
