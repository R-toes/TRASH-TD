using UnityEngine;
using UnityEngine.UI;
using TrashTD.Data;
using TrashTD.Systems;

namespace TrashTD.UI
{
    /// <summary>
    /// UI for non-linear Bloons-TD style map selection (GDD 1.4.7).
    /// Displays stage cards, 3 difficulty selection buttons with corresponding Life Points,
    /// and a Launch Stage button.
    /// </summary>
    public class MapSelectUI : MonoBehaviour
    {
        [Header("Manager")]
        [SerializeField] private MapSelectManager mapSelectManager;

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
            if (mapSelectManager == null) mapSelectManager = FindFirstObjectByType<MapSelectManager>();

            if (easyButton != null) easyButton.onClick.AddListener(() => SetDifficulty(StageDifficulty.Easy));
            if (normalButton != null) normalButton.onClick.AddListener(() => SetDifficulty(StageDifficulty.Normal));
            if (hardButton != null) hardButton.onClick.AddListener(() => SetDifficulty(StageDifficulty.Hard));
            if (launchButton != null) launchButton.onClick.AddListener(OnLaunchClicked);
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
        }

        private void OnDestroy()
        {
            if (mapSelectManager != null)
            {
                mapSelectManager.OnStageSelected -= UpdateMapDisplay;
                mapSelectManager.OnDifficultySelected -= UpdateDifficultyDisplay;
            }
        }

        public void SetDifficulty(StageDifficulty difficulty)
        {
            if (mapSelectManager != null)
            {
                mapSelectManager.SelectDifficulty(difficulty);
            }
        }

        private void UpdateMapDisplay(StageData stage)
        {
            if (stage == null) return;

            if (mapNameText != null) mapNameText.text = stage.mapName;
            if (squadLimitText != null) squadLimitText.text = $"Squad Limit: {stage.squadSizeLimit}";
            RefreshLifePoints();
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
                lifePointsText.text = $"Life Points: {lp}";
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
