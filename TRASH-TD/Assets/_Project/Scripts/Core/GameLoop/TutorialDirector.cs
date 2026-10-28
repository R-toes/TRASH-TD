using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;
using TrashTD.UI;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// Runs Coalesce's orientation on STAGE_TUTORIAL: plays the TutorialScript steps in order on top of the normal
    /// CardPick → Preparation → WaveActive loop. It deals scripted hands (CardDraftSystem.SetScriptedOffer), holds
    /// the draft until Coalesce is ready (GameplayHUDUI.ShouldBlockDraftOverlay), only lets the wave start when a
    /// step asks for it, freezes the game (time scale 0) while Coalesce talks, and points at what to click.
    /// The stage can't be lost (GameManager.IsTutorial). Finishing shows the usual results screen; finishing or
    /// skipping marks the tutorial done (PlayerPrefs), which ends Coalesce's stage-map intro (MapTutorialGuide).
    /// It stays replayable from STAGE 00 on the map.
    /// </summary>
    public sealed class TutorialDirector : MonoBehaviour
    {
        public const string TutorialStageId = "TUTORIAL";
        public const string TutorialStageResourcePath = "Stages/STAGE_TUTORIAL";
        private const string TutorialDoneKey = "TrashTD.TutorialDone";
        private const string ScriptResourcePath = "Tutorial/TutorialScript";
        private const string MainMenuScene = "MainMenu";
        // Let a new hostile walk on screen for a moment before Coalesce freezes the wave to introduce it
        private const float EnemyIntroDelay = 1.4f;

        public static TutorialDirector Instance { get; private set; }

        /// <summary>The tutorial was finished or skipped on this install.</summary>
        public static bool IsTutorialDone => PlayerPrefs.GetInt(TutorialDoneKey, 0) == 1;

        public static void MarkTutorialDone()
        {
            PlayerPrefs.SetInt(TutorialDoneKey, 1);
            PlayerPrefs.Save();
        }

        private TutorialScript script;
        private StageData stageData;
        private GridManager gridManager;
        private StageBootstrapper bootstrapper;
        private GameManager gameManager;
        private OperatorManager operatorManager;
        private PlayerDeck playerDeck;
        private CardDraftSystem draftSystem;
        private GameplayHUDUI hud;
        private CardDraftOverlayUI draftOverlay;
        private TutorialDialogueUI dialogue;
        private TutorialStageVisuals visuals;

        private bool draftReleased;
        private OperatorData pickTarget;
        private bool waveStartAllowed;
        private bool advanced;
        private bool frozen;
        private bool ending;   // Skipped: leaving for the stage map
        private bool finished; // Script done: results screen (or free play if the waves aren't over)
        private int starsEarned = 3;
        private bool freePlay; // No script: the stage just plays normally
        private readonly HashSet<EnemyData> seenEnemies = new HashSet<EnemyData>();

        /// <summary>The draft waits until a Draft step deals the hand.</summary>
        public bool HoldsDraft => !draftReleased && !freePlay;
        /// <summary>START WAVE only works during a StartWave step.</summary>
        public bool AllowsWaveStart => freePlay || (waveStartAllowed && !dialogue.IsBlockingGame);

        /// <summary>Coalesce's outro is over; StageResultsUI waits for this before showing the tutorial's results.</summary>
        public bool IsFinished => finished || freePlay;

        /// <summary>Only the card Coalesce asked for can be picked (any card when the step names none).</summary>
        public bool CanPickCard(DraftCard card) => card != null && (freePlay || pickTarget == null || card.operatorData == pickTarget);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (EnemyManager.Instance != null) EnemyManager.Instance.OnEnemySpawned -= HandleEnemySpawned;
            if (gameManager != null) gameManager.OnStageVictory -= HandleStageVictory;
            if (dialogue != null)
            {
                dialogue.Advanced -= HandleAdvanced;
                dialogue.SkipConfirmed -= HandleSkipConfirmed;
                Destroy(dialogue.gameObject);
            }
        }

        /// <summary>
        /// Called by StageBootstrapper before the stage starts, so the first draft is already held. Builds the floor
        /// and Coalesce, then runs the script once the managers are up.
        /// </summary>
        public void Initialize(StageBootstrapper owner, StageData stage, GridManager grid)
        {
            bootstrapper = owner;
            stageData = stage;
            gridManager = grid;
            script = Resources.Load<TutorialScript>(ScriptResourcePath);
            if (script == null) Debug.LogError($"TutorialDirector: no TutorialScript at Resources/{ScriptResourcePath}.");

            visuals = TutorialStageVisuals.Create(transform, grid, stage);

            Sprite[] frames = UnitSpriteFrames.GetOperatorFrames(script != null ? script.speaker : null, out float frameDuration);
            dialogue = TutorialDialogueUI.Create(script != null ? script.speakerName : "COALESCE", frames, frameDuration);
            dialogue.Advanced += HandleAdvanced;
            dialogue.SkipConfirmed += HandleSkipConfirmed;
        }

        private void Start()
        {
            gameManager = GameManager.Instance;
            operatorManager = OperatorManager.Instance;
            playerDeck = PlayerDeck.Instance;
            draftSystem = CardDraftSystem.Instance;
            hud = FindAnyObjectByType<GameplayHUDUI>();
            draftOverlay = FindAnyObjectByType<CardDraftOverlayUI>();
            if (EnemyManager.Instance != null) EnemyManager.Instance.OnEnemySpawned += HandleEnemySpawned;
            if (gameManager != null) gameManager.OnStageVictory += HandleStageVictory;

            StartCoroutine(RunScript());
        }

        private void Update()
        {
            if (dialogue == null || gameManager == null || freePlay || finished) return;

            // Stay out of the way of the pause menu (it pauses the game itself)
            bool pauseMenu = hud != null && hud.IsPauseMenuOpen;
            dialogue.SetShown(!pauseMenu);
            if (pauseMenu) return;

            // Coalesce talking (or the skip prompt) freezes everything; tasks run at the chosen speed
            bool freeze = dialogue.IsBlockingGame && !ending;
            if (gameManager.CurrentState == GamePlayState.Playing)
            {
                if (freeze) Time.timeScale = 0f;
                else if (frozen) Time.timeScale = gameManager.GameSpeed;
            }
            frozen = freeze;
        }

        private void HandleAdvanced() => advanced = true;

        private void HandleStageVictory(int stars) => starsEarned = stars;

        private void HandleEnemySpawned(EnemyBase enemy)
        {
            if (enemy != null && enemy.Data != null) seenEnemies.Add(enemy.Data);
        }

        private void HandleSkipConfirmed() => EndTutorial();

        // ─────────────────────────────────────────────────────
        //  Script
        // ─────────────────────────────────────────────────────

        private IEnumerator RunScript()
        {
            // Let the HUD and draft overlay finish building
            yield return null;

            if (script == null || script.steps == null || script.steps.Length == 0)
            {
                freePlay = true;
                dialogue.SetShown(false);
                draftOverlay?.ReleaseHeldDraft();
                yield break;
            }

            foreach (TutorialStep step in script.steps)
            {
                if (step == null) continue;
                yield return RunStep(step);
                ClearPointers();
                if (ending || finished) yield break;
            }

            FinishTutorial();
        }

        private IEnumerator RunStep(TutorialStep step)
        {
            switch (step.action)
            {
                case TutorialAction.Say:
                    yield return Talk(step);
                    break;

                case TutorialAction.RevealPath:
                    visuals.RevealRoute();
                    yield return Talk(step);
                    break;

                case TutorialAction.GiftCards:
                    if (step.operatorData != null && playerDeck != null)
                    {
                        for (int i = 0; i < Mathf.Max(1, step.count); i++)
                            playerDeck.AddReturnedCard(new DraftCard(step.operatorData, step.operatorData.baseRarity));
                    }
                    yield return Talk(step);
                    break;

                case TutorialAction.Draft:
                    yield return DraftStep(step);
                    break;

                case TutorialAction.Deploy:
                    yield return DeployStep(step);
                    break;

                case TutorialAction.StartWave:
                    yield return StartWaveStep(step);
                    break;

                case TutorialAction.WaitForEnemy:
                    dialogue.HideBubble();
                    while (!ending && step.enemyData != null && !seenEnemies.Contains(step.enemyData) && !IsStageOver)
                        yield return null;
                    // Game time, so the hostile has walked the same distance at any speed
                    for (float waited = 0f; !ending && waited < EnemyIntroDelay && !IsStageOver; waited += Time.deltaTime)
                        yield return null;
                    break;

                case TutorialAction.WaitForWaveEnd:
                    dialogue.HideBubble();
                    while (!ending && gameManager.CurrentPhase == StagePhase.WaveActive && !IsStageOver)
                        yield return null;
                    break;

                case TutorialAction.Retreat:
                    yield return RetreatStep(step);
                    break;

                case TutorialAction.Finish:
                    yield return Talk(step);
                    FinishTutorial();
                    break;
            }
        }

        private bool IsStageOver => gameManager.CurrentState == GamePlayState.Victory || gameManager.CurrentState == GamePlayState.Defeat;

        /// <summary>A talk line: the game freezes until the player clicks past it.</summary>
        private IEnumerator Talk(TutorialStep step)
        {
            advanced = false;
            dialogue.SetLayout(TutorialDialogueLayout.Gameplay);
            dialogue.Say(step.text, false);
            ApplyHighlight(step);
            while (!advanced && !ending) yield return null;
        }

        private IEnumerator DraftStep(TutorialStep step)
        {
            while (!ending && gameManager.CurrentPhase != StagePhase.CardPick) yield return null;
            if (ending) yield break;

            pickTarget = step.operatorData;
            if (step.cards != null && step.cards.Length > 0) draftSystem?.SetScriptedOffer(step.cards);
            draftReleased = true;
            draftOverlay?.ReleaseHeldDraft();

            dialogue.SetLayout(TutorialDialogueLayout.Compact);
            dialogue.Say(step.text, true);
            while (!ending && gameManager.CurrentPhase == StagePhase.CardPick)
            {
                dialogue.PointAt(draftOverlay != null ? draftOverlay.GetOfferedCardRect(pickTarget) : null);
                yield return null;
            }

            // The next round's draft waits for its own step
            draftReleased = false;
            pickTarget = null;
            dialogue.SetLayout(TutorialDialogueLayout.Gameplay);
        }

        private IEnumerator DeployStep(TutorialStep step)
        {
            while (!ending && gameManager.CurrentPhase == StagePhase.CardPick) yield return null;

            dialogue.SetLayout(TutorialDialogueLayout.Gameplay);
            dialogue.Say(step.text, true);
            while (!ending && FindDeployed(step.operatorData) == null)
            {
                // Discarded, or lost in battle: nothing to deploy, so don't hold the tutorial up
                if (!HasReadyCard(step.operatorData)) yield break;

                // Point at the deck card until it's picked up, then only at the tiles
                bool placing = bootstrapper != null && bootstrapper.PendingDeployCard != null &&
                               bootstrapper.PendingDeployCard.operatorData == step.operatorData;
                dialogue.PointAt(!placing && hud != null ? hud.GetDeckSlotRect(step.operatorData) : null);
                visuals.SetHighlightedTiles(GetDeployTiles(step.operatorData));
                yield return null;
            }
        }

        private IEnumerator StartWaveStep(TutorialStep step)
        {
            while (!ending && gameManager.CurrentPhase != StagePhase.Preparation) yield return null;

            waveStartAllowed = true;
            dialogue.SetLayout(TutorialDialogueLayout.Gameplay);
            dialogue.Say(step.text, true);
            while (!ending && gameManager.CurrentPhase == StagePhase.Preparation)
            {
                dialogue.PointAt(hud != null ? hud.StartWaveButtonRect : null);
                yield return null;
            }
            waveStartAllowed = false;
        }

        private IEnumerator RetreatStep(TutorialStep step)
        {
            dialogue.SetLayout(TutorialDialogueLayout.Gameplay);
            dialogue.Say(step.text, true);
            OperatorBase target;
            while (!ending && (target = FindDeployed(step.operatorData)) != null)
            {
                visuals.SetHighlightedTiles(target.DeployedCell != null ? new[] { target.DeployedCell.GridPosition } : null);
                bool selected = operatorManager != null && operatorManager.SelectedOperator == target;
                dialogue.PointAt(selected && hud != null ? hud.RetreatButtonRect : null);
                yield return null;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Pointing
        // ─────────────────────────────────────────────────────

        private void ApplyHighlight(TutorialStep step)
        {
            ClearPointers();
            switch (step.highlight)
            {
                case TutorialHighlight.Lives:
                    dialogue.PointAt(hud != null ? hud.LivesRect : null);
                    break;
                case TutorialHighlight.DeckCard:
                    dialogue.PointAt(hud != null ? hud.GetDeckSlotRect(step.operatorData, false) : null);
                    break;
                case TutorialHighlight.StartWave:
                    dialogue.PointAt(hud != null ? hud.StartWaveButtonRect : null);
                    break;
                case TutorialHighlight.DraftCard:
                    dialogue.PointAt(draftOverlay != null ? draftOverlay.GetOfferedCardRect(step.operatorData) : null);
                    break;
                case TutorialHighlight.PathTiles:
                    visuals.SetHighlightedTiles(GetTiles(t => t == TileType.LowGround || t == TileType.EnemyPath));
                    break;
                case TutorialHighlight.HighGroundTiles:
                    visuals.SetHighlightedTiles(GetTiles(t => t == TileType.HighGround));
                    break;
                case TutorialHighlight.SpawnAndExit:
                    visuals.SetHighlightedTiles(GetTiles(t => t == TileType.SpawnPoint || t == TileType.ExitPoint));
                    break;
                case TutorialHighlight.Operator:
                    OperatorBase op = FindDeployed(step.operatorData);
                    if (op != null && op.DeployedCell != null) visuals.SetHighlightedTiles(new[] { op.DeployedCell.GridPosition });
                    break;
            }
        }

        private void ClearPointers()
        {
            if (dialogue != null) dialogue.PointAt(null);
            if (visuals != null) visuals.SetHighlightedTiles(null);
        }

        private List<Vector2Int> GetTiles(System.Predicate<TileType> match)
        {
            var tiles = new List<Vector2Int>();
            for (int y = 0; y < stageData.gridHeight; y++)
            {
                for (int x = 0; x < stageData.gridWidth; x++)
                {
                    if (match(stageData.GetTile(x, y))) tiles.Add(new Vector2Int(x, y));
                }
            }
            return tiles;
        }

        /// <summary>Free tiles this operator can stand on.</summary>
        private List<Vector2Int> GetDeployTiles(OperatorData operatorData)
        {
            var tiles = new List<Vector2Int>();
            if (operatorData == null || gridManager == null) return tiles;
            for (int y = 0; y < stageData.gridHeight; y++)
            {
                for (int x = 0; x < stageData.gridWidth; x++)
                {
                    GridCell cell = gridManager.GetCell(x, y);
                    if (cell != null && cell.CanDeploy(operatorData.position)) tiles.Add(cell.GridPosition);
                }
            }
            return tiles;
        }

        private bool HasReadyCard(OperatorData operatorData)
        {
            if (playerDeck == null || operatorData == null) return false;
            foreach (DraftCard card in playerDeck.DeckSlots)
            {
                if (card != null && card.operatorData == operatorData && card.cooldownRoundsRemaining <= 0) return true;
            }
            return false;
        }

        private OperatorBase FindDeployed(OperatorData operatorData)
        {
            if (operatorManager == null || operatorData == null) return null;
            foreach (OperatorBase op in operatorManager.DeployedOperators)
            {
                if (op != null && op.Data == operatorData) return op;
            }
            return null;
        }

        // ─────────────────────────────────────────────────────
        //  End
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// The script is over: remember it, send Coalesce off, and show the results screen like any cleared stage.
        /// If the waves aren't over yet (an edited script), the rest of the stage just plays freely.
        /// </summary>
        private void FinishTutorial()
        {
            if (finished || ending) return;
            finished = true;
            MarkTutorialDone();
            ClearPointers();
            dialogue.SetShown(false);
            draftReleased = true;

            if (gameManager.CurrentState == GamePlayState.Victory)
            {
                FindAnyObjectByType<StageResultsUI>()?.ShowVictory(starsEarned);
            }
            else
            {
                freePlay = true;
                if (gameManager.CurrentState == GamePlayState.Playing) Time.timeScale = gameManager.GameSpeed;
                if (gameManager.CurrentPhase == StagePhase.CardPick) draftOverlay?.ReleaseHeldDraft();
            }
        }

        /// <summary>Skipped: remember it and go back to the stage map.</summary>
        private void EndTutorial()
        {
            if (ending) return;
            ending = true;
            MarkTutorialDone();
            ClearPointers();
            Time.timeScale = 1f;
            MainMenuController.ReturnToStageSelectorOnLoad();
            SceneManager.LoadScene(MainMenuScene);
        }
    }
}
