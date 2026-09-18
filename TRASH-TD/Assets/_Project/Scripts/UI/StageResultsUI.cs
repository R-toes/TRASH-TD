using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TrashTD.Core.GameLoop;

namespace TrashTD.UI
{
    /// <summary>
    /// Displays stage completion results (Victory with 1-3 stars, or Defeat) (GDD 1.4.6 & 1.9).
    /// </summary>
    public class StageResultsUI : MonoBehaviour
    {
        [Header("Root Panels")]
        [SerializeField] private GameObject resultsRoot;
        [SerializeField] private GameObject victoryPanel;
        [SerializeField] private GameObject defeatPanel;

        [Header("Victory Displays")]
        [SerializeField] private Text starsText;
        [SerializeField] private Text victoryTitleText;

        [Header("Buttons")]
        [SerializeField] private Button retryButton;
        [SerializeField] private Button returnToMapSelectButton;

        private void Awake()
        {
            if (retryButton != null) retryButton.onClick.AddListener(OnRetryClicked);
            if (returnToMapSelectButton != null) returnToMapSelectButton.onClick.AddListener(OnReturnClicked);

            if (resultsRoot != null) resultsRoot.SetActive(false);
        }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStageVictory += ShowVictory;
                GameManager.Instance.OnStageDefeat += ShowDefeat;
            }
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStageVictory -= ShowVictory;
                GameManager.Instance.OnStageDefeat -= ShowDefeat;
            }
        }

        public void ShowVictory(int stars)
        {
            if (resultsRoot != null) resultsRoot.SetActive(true);
            if (victoryPanel != null) victoryPanel.SetActive(true);
            if (defeatPanel != null) defeatPanel.SetActive(false);

            if (starsText != null)
            {
                starsText.text = $"{new string('★', stars)}{new string('☆', 3 - stars)}";
            }
        }

        public void ShowDefeat()
        {
            if (resultsRoot != null) resultsRoot.SetActive(true);
            if (victoryPanel != null) victoryPanel.SetActive(false);
            if (defeatPanel != null) defeatPanel.SetActive(true);
        }

        private void OnRetryClicked()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void OnReturnClicked()
        {
            // TODO: Load MapSelect scene when ready
            Debug.Log("Returning to Map Select...");
        }
    }
}
