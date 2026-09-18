using UnityEngine;
using UnityEngine.UI;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.UI
{
    /// <summary>
    /// Displays detailed info of a selected operator or enemy on the battlefield (GDD 1.9).
    /// Provides interaction buttons (Retreat, Activate Skill).
    /// </summary>
    public class UnitInfoPanelUI : MonoBehaviour
    {
        [Header("Root Panel")]
        [SerializeField] private GameObject panelRoot;

        [Header("Text Fields")]
        [SerializeField] private Text nameText;
        [SerializeField] private Text classRoleText;
        [SerializeField] private Text rarityText;
        [SerializeField] private Text hpText;
        [SerializeField] private Text atkText;
        [SerializeField] private Text defText;
        [SerializeField] private Text resText;
        [SerializeField] private Text blockText;

        [Header("Controls")]
        [SerializeField] private Button retreatButton;
        [SerializeField] private Button skillButton;
        [SerializeField] private Button closeButton;

        private OperatorBase selectedOperator;

        private void Awake()
        {
            if (retreatButton != null) retreatButton.onClick.AddListener(OnRetreatClicked);
            if (skillButton != null) skillButton.onClick.AddListener(OnSkillClicked);
            if (closeButton != null) closeButton.onClick.AddListener(ClosePanel);

            ClosePanel();
        }

        public void InspectOperator(OperatorBase op)
        {
            selectedOperator = op;
            if (op == null || op.Data == null)
            {
                ClosePanel();
                return;
            }

            if (panelRoot != null) panelRoot.SetActive(true);

            if (nameText != null) nameText.text = op.Data.operatorName;
            if (classRoleText != null) classRoleText.text = $"{op.Data.operatorClass} ({op.Data.position})";
            if (rarityText != null) rarityText.text = new string('★', (int)op.CurrentRarity);
            if (hpText != null) hpText.text = $"HP: {op.CurrentHP} / {op.MaxHP}";
            if (atkText != null) atkText.text = $"ATK: {op.CurrentATK}";
            if (defText != null) defText.text = $"DEF: {op.CurrentDEF}";
            if (resText != null) resText.text = $"RES: {op.CurrentRES}";
            if (blockText != null) blockText.text = $"Block: {op.GetCurrentBlockCount()} / {op.GetBlockCount()}";
        }

        private void OnRetreatClicked()
        {
            if (selectedOperator != null && OperatorManager.Instance != null)
            {
                OperatorManager.Instance.RetreatOperator(selectedOperator);
                ClosePanel();
            }
        }

        private void OnSkillClicked()
        {
            if (selectedOperator != null)
            {
                selectedOperator.ActivateSkill();
            }
        }

        public void ClosePanel()
        {
            selectedOperator = null;
            if (panelRoot != null) panelRoot.SetActive(false);
        }
    }
}
