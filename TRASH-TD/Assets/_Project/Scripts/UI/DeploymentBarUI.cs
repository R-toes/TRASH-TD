using UnityEngine;
using UnityEngine.UI;
using TrashTD.Core.GameLoop;
using TrashTD.Systems;

namespace TrashTD.UI
{
    /// <summary>
    /// Displays stage HUD info: DP count, Life Points, Deployed count / Squad limit,
    /// and controls for drafting new units (GDD 1.9).
    /// </summary>
    public class DeploymentBarUI : MonoBehaviour
    {
        [Header("Status Displays")]
        [SerializeField] private Text dpText;
        [SerializeField] private Text lifePointsText;
        [SerializeField] private Text squadCountText;
        [SerializeField] private Text waveText;

        [Header("Draft Trigger Button")]
        [SerializeField] private Button requestDraftButton;

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnDPChanged += UpdateDPDisplay;
                GameManager.Instance.OnLifePointsChanged += UpdateLifePointsDisplay;
                UpdateDPDisplay(GameManager.Instance.CurrentDP);
                UpdateLifePointsDisplay(GameManager.Instance.CurrentLifePoints, GameManager.Instance.MaxLifePoints);
            }

            if (requestDraftButton != null)
            {
                requestDraftButton.onClick.AddListener(OnRequestDraftClicked);
            }
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnDPChanged -= UpdateDPDisplay;
                GameManager.Instance.OnLifePointsChanged -= UpdateLifePointsDisplay;
            }
        }

        private void Update()
        {
            if (OperatorManager.Instance != null && squadCountText != null)
            {
                squadCountText.text = $"Squad: {OperatorManager.Instance.DeployedCount} / {OperatorManager.Instance.SquadLimit}";
            }
        }

        private void UpdateDPDisplay(int currentDP)
        {
            if (dpText != null)
            {
                dpText.text = $"DP: {currentDP}";
            }
        }

        private void UpdateLifePointsDisplay(int currentLP, int maxLP)
        {
            if (lifePointsText != null)
            {
                lifePointsText.text = $"Life: {currentLP} / {maxLP}";
            }
        }

        private void OnRequestDraftClicked()
        {
            var draftSystem = FindFirstObjectByType<CardDraftSystem>();
            if (draftSystem != null)
            {
                draftSystem.GenerateDraftOffer();
            }
        }
    }
}
