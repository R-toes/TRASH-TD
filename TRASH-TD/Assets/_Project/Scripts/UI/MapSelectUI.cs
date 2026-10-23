using UnityEngine;
using UnityEngine.UI;
using TrashTD.Data;
using TrashTD.Systems;

namespace TrashTD.UI
{
    /// <summary>
    /// UI for non-linear Bloons-TD style map selection (GDD 1.4.7).
    /// Shows the Pip-Boy stage map with one clickable dot per stage, the selected stage's info,
    /// 3 difficulty selection buttons with corresponding Life Points, and a Launch Stage button.
    /// </summary>
    public class MapSelectUI : MonoBehaviour
    {
        [Header("Manager")]
        [SerializeField] private MapSelectManager mapSelectManager;

        [Header("Stage Map (Pip-Boy)")]
        [Tooltip("The 6 stage dots placed on the map art. Each node holds its own StageData.")]
        [SerializeField] private StageMapNode[] stageNodes;

        [Header("UI Text Displays")]
        [SerializeField] private Text mapNameText;
        [SerializeField] private Text difficultyText;
        [SerializeField] private Text lifePointsText;
        [SerializeField] private Text squadLimitText;

        [Header("Difficulty Buttons")]
        [SerializeField] private Button easyButton;
        [SerializeField] private Button normalButton;
        [SerializeField] private Button hardButton;

        [Header("Action Buttons")]
        [SerializeField] private Button launchButton;

        private void Awake()
        {
            if (mapSelectManager == null) mapSelectManager = FindAnyObjectByType<MapSelectManager>();

            if (easyButton != null) easyButton.onClick.AddListener(() => SetDifficulty(StageDifficulty.Easy));
            if (normalButton != null) normalButton.onClick.AddListener(() => SetDifficulty(StageDifficulty.Normal));
            if (hardButton != null) hardButton.onClick.AddListener(() => SetDifficulty(StageDifficulty.Hard));
            if (launchButton != null) launchButton.onClick.AddListener(OnLaunchClicked);

            if (stageNodes != null)
            {
                foreach (var node in stageNodes)
                {
                    if (node != null) node.Clicked += OnStageNodeClicked;
                }
            }
        }

        private void Start()
        {
            if (mapSelectManager != null)
            {
                mapSelectManager.OnStageSelected += UpdateMapDisplay;
                mapSelectManager.OnDifficultySelected += UpdateDifficultyDisplay;

                if (mapSelectManager.SelectedStage != null)
                {
                    UpdateMapDisplay(mapSelectManager.SelectedStage);
                }
                UpdateDifficultyDisplay(mapSelectManager.SelectedDifficulty);
            }

            RefreshStageNodes();
            RefreshLaunchButton();
        }

        private void OnDestroy()
        {
            if (mapSelectManager != null)
            {
                mapSelectManager.OnStageSelected -= UpdateMapDisplay;
                mapSelectManager.OnDifficultySelected -= UpdateDifficultyDisplay;
            }

            if (stageNodes != null)
            {
                foreach (var node in stageNodes)
                {
                    if (node != null) node.Clicked -= OnStageNodeClicked;
                }
            }
        }

        public void SetDifficulty(StageDifficulty difficulty)
        {
            if (mapSelectManager != null)
            {
                mapSelectManager.SelectDifficulty(difficulty);
            }
        }

        // ----- Stage map -----

        private void OnStageNodeClicked(StageMapNode node)
        {
            if (node != null) SelectStage(node.Stage);
        }

        /// <summary>
        /// Single hook into the manager for picking a stage from the map.
        /// NOTE: assumes MapSelectManager exposes SelectStage(StageData). If yours is named differently, change only this line.
        /// </summary>
        public void SelectStage(StageData stage)
        {
            if (stage == null || mapSelectManager == null) return;
            mapSelectManager.SelectStage(stage);
        }

        /// <summary>
        /// Hook your progression / save system in here to lock stages (locked dots are dimmed and can't be clicked).
        /// </summary>
        protected virtual bool IsStageUnlocked(StageData stage)
        {
            return true;
        }

        private void RefreshStageNodes()
        {
            if (stageNodes == null) return;

            StageData selected = mapSelectManager != null ? mapSelectManager.SelectedStage : null;
            foreach (var node in stageNodes)
            {
                if (node == null) continue;
                node.SetLocked(!IsStageUnlocked(node.Stage));
                node.SetSelected(selected != null && node.Stage == selected);
            }
        }

        private void RefreshLaunchButton()
        {
            // can't launch until a stage dot has been picked
            if (launchButton != null)
            {
                launchButton.interactable = mapSelectManager != null && mapSelectManager.SelectedStage != null;
            }
        }

        // ----- Info panel -----

        private void UpdateMapDisplay(StageData stage)
        {
            if (stage == null) return;

            if (mapNameText != null) mapNameText.text = stage.mapName;
            if (squadLimitText != null) squadLimitText.text = $"Squad Limit: {stage.squadSizeLimit}";
            RefreshLifePoints();
            RefreshStageNodes();
            RefreshLaunchButton();
        }

        private void UpdateDifficultyDisplay(StageDifficulty difficulty)
        {
            if (difficultyText != null) difficultyText.text = $"Difficulty: {difficulty}";
            RefreshLifePoints();
        }

        private void RefreshLifePoints()
        {
            if (mapSelectManager == null || mapSelectManager.SelectedStage == null) return;

            int lp = mapSelectManager.SelectedStage.GetLifePoints(mapSelectManager.SelectedDifficulty);
            if (lifePointsText != null)
            {
                lifePointsText.text = $"Lives: {lp}";
            }
        }

        private void OnLaunchClicked()
        {
            if (mapSelectManager != null)
            {
                mapSelectManager.LaunchSelectedStage();
            }
        }
    }
}