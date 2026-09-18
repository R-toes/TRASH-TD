using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.GameLoop;
using TrashTD.Data;

namespace TrashTD.Systems
{
    /// <summary>
    /// Manages non-linear map selection (Bloons TD style, GDD 1.4.7):
    /// - All available maps are accessible non-linearly.
    /// - Each map supports 3 difficulty variants: Easy, Normal, Hard.
    /// - Life points: 10 (Easy), 5 (Normal), 1 (Hard).
    /// </summary>
    public class MapSelectManager : MonoBehaviour
    {
        public static MapSelectManager Instance { get; private set; }

        [Header("Available Maps")]
        [SerializeField] private List<StageData> availableStages = new List<StageData>();

        private StageData selectedStage;
        private StageDifficulty selectedDifficulty = StageDifficulty.Normal;

        public IReadOnlyList<StageData> AvailableStages => availableStages;
        public StageData SelectedStage => selectedStage;
        public StageDifficulty SelectedDifficulty => selectedDifficulty;

        public event Action<StageData> OnStageSelected;
        public event Action<StageDifficulty> OnDifficultySelected;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void SelectStage(StageData stage)
        {
            selectedStage = stage;
            OnStageSelected?.Invoke(selectedStage);
        }

        public void SelectDifficulty(StageDifficulty difficulty)
        {
            selectedDifficulty = difficulty;
            OnDifficultySelected?.Invoke(selectedDifficulty);
        }

        /// <summary>
        /// Launch the currently selected stage and difficulty into GameManager.
        /// </summary>
        public void LaunchSelectedStage()
        {
            if (selectedStage == null)
            {
                Debug.LogWarning("MapSelectManager: No stage selected to launch!");
                return;
            }

            if (GameManager.Instance != null)
            {
                GameManager.Instance.StartStage(selectedStage, selectedDifficulty);
            }
        }
    }
}
